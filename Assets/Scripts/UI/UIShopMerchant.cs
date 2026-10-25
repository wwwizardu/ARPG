#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Village;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: MerchantStall 상점 UI (구매/판매 두 탭).
    /// ServiceUIRouter가 Show 호출 시 NearestShopEntityId로 Bind.
    ///
    /// 구매: VillageData.MerchantStock 풀에서 매물 표시
    /// 판매: 플레이어 인벤토리 → 매각가 + 자원 환원
    /// </summary>
    public class UIShopMerchant : UIBaseForm
    {
        private UIDocument? _document;
        private Button? _buyTabBtn;
        private Button? _sellTabBtn;
        private IconButton? _closeBtn;
        private Text? _goldText;
        private Text? _selectedSellText;
        private Button? _sellActionBtn;

        private int _shopEntityId = -1;
        private int _villageId = -1;
        private bool _isBuyTab = true;
        private int _selectedSellSlotIndex = -1;
        private VisualElement? _lastRoot;
        // 판매 칸 element — 인덱스 = 인벤토리 슬롯 인덱스. RebuildSellGrid가 다시 채운다.
        private readonly List<VisualElement> _sellSlots = new();

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIShopMerchant] UIDocument 컴포넌트 없음");
                return;
            }

            EnsureBound();
        }

        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _buyTabBtn = root.Q<Button>("buy-tab-btn");
            _sellTabBtn = root.Q<Button>("sell-tab-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _goldText = root.Q<Text>("gold-text");
            _selectedSellText = root.Q<Text>("selected-sell-text");
            _sellActionBtn = root.Q<Button>("sell-action-btn");

            if (_buyTabBtn != null) _buyTabBtn.clicked += OnClickBuyTab;
            if (_sellTabBtn != null) _sellTabBtn.clicked += OnClickSellTab;
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
            if (_sellActionBtn != null) _sellActionBtn.clicked += OnClickSellAction;
        }

        public void Bind(int shopEntityId)
        {
            EnsureBound();
            _shopEntityId = shopEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shopEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIShopMerchant] Bind 실패 — entityId={shopEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;

            // 24h 게이트 — 매물 풀 재롤
            AR.s.Village.EnsureMerchantStockFresh(_villageId);

            _isBuyTab = true;
            _selectedSellSlotIndex = -1;
            RefreshAll();
        }

        public void BindForTest()
        {
            EnsureBound();
            _shopEntityId = -1;
            _villageId = -1;
            _isBuyTab = true;
            _selectedSellSlotIndex = -1;
            RefreshAll();
            Debug.Log("[UIShopMerchant] BindForTest — 테스트 모드");
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            RefreshAll();
        }

        public override void OnClose()
        {
            AR.s.Tooltip.Hide();
            base.OnClose();
        }

        // ========== 탭 전환 ==========

        public void OnClickBuyTab()
        {
            _isBuyTab = true;
            RefreshAll();
        }

        public void OnClickSellTab()
        {
            _isBuyTab = false;
            RefreshAll();
        }

        // ========== 구매 ==========

        public void OnClickBuyStock(int stockIndex)
        {
            if (_villageId < 0)
            {
                Debug.Log($"[Shop] (테스트모드) 구매 stockIndex={stockIndex}");
                return;
            }
            int gold = AR.s.Village.BuyItemFromMerchant(_villageId, stockIndex, 1);
            if (gold < 0) return;
            RefreshAll();
        }

        // ========== 판매 ==========

        public void OnClickSellAction()
        {
            if (_selectedSellSlotIndex < 0) return;
            if (_villageId < 0)
            {
                Debug.Log($"[Shop] (테스트모드) 매각 slotIndex={_selectedSellSlotIndex}");
                return;
            }
            // 참이 박힌 판은 참이 함께 팔린다는 확인 창을 먼저 띄운다 (PLAYER_LEVEL_CHARM_DESIGN §6.5·§8.9)
            ItemData? item = AR.s.Player?.Inventory?.GetItemBySlotIndex(_selectedSellSlotIndex);
            int charmCount = item?.CharmBoard?.Charms.Count ?? 0;
            if (charmCount > 0)
            {
                int price = Mathf.Max(0, AR.s.Village.GetPlayerSellPrice(_villageId, item));
                int slotIndex = _selectedSellSlotIndex;
                UIConfirmDialog? dialog = AR.s.UI.Show<UIConfirmDialog>(AddressablePath.ConfirmDialog, UIManager.Layer.Popup);
                dialog?.Setup($"참 {charmCount}개가 함께 팔립니다. 판매 가격 {price:N0}골드", () => SellSlot(slotIndex));
                return;
            }

            SellSlot(_selectedSellSlotIndex);
        }

        private void SellSlot(int slotIndex)
        {
            int gold = AR.s.Village.SellItemToMerchant(_villageId, slotIndex, 1);
            if (gold < 0) return;
            _selectedSellSlotIndex = -1;
            RefreshAll();
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            if (_lastRoot == null) return;

            // 탭 전환·구매·판매로 목록을 다시 만들면 호버 중이던 element가 사라지므로 툴팁을 먼저 닫는다.
            AR.s.Tooltip.Hide();

            // 골드 표시
            int gold = AR.s.Data.Player?.Gold ?? 0;
            if (_goldText != null) _goldText.text = $"골드: {gold:N0} G";

            // 탭 상태 (variant 토글)
            if (_buyTabBtn != null) _buyTabBtn.variant = _isBuyTab ? ButtonVariant.Accent : ButtonVariant.Default;
            if (_sellTabBtn != null) _sellTabBtn.variant = _isBuyTab ? ButtonVariant.Default : ButtonVariant.Accent;

            // 패널 표시 토글
            VisualElement? buyPanel = _lastRoot.Q<VisualElement>("buy-panel");
            VisualElement? sellPanel = _lastRoot.Q<VisualElement>("sell-panel");
            if (buyPanel != null) buyPanel.style.display = _isBuyTab ? DisplayStyle.Flex : DisplayStyle.None;
            if (sellPanel != null) sellPanel.style.display = _isBuyTab ? DisplayStyle.None : DisplayStyle.Flex;

            if (_isBuyTab) RebuildBuyList();
            else RebuildSellGrid();
        }

        // ========== 구매 리스트 ==========

        private void RebuildBuyList()
        {
            if (_lastRoot == null) return;
            VisualElement? list = _lastRoot.Q<VisualElement>("buy-list");
            if (list == null) return;
            list.Clear();

            if (_villageId < 0)
            {
                AddEmptyText(list, "테스트 모드 — 매물 없음");
                return;
            }

            VillageData? v = AR.s.Village.GetVillage(_villageId);
            if (v == null || v.MerchantStock == null || v.MerchantStock.Count == 0)
            {
                AddEmptyText(list, "매물 없음");
                return;
            }

            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                int stockIdx = i;
                MerchantStockEntry entry = v.MerchantStock[i];
                if (entry.Item == null || entry.Item.Table == null) continue;
                // NPC_AMBITION D53: 품절 매물은 구매자(플레이어·NPC)와 관계없이 목록에서 숨긴다 (D42의 "○○가 구매함" 표시 폐기).
                if (entry.RemainingCount <= 0) continue;

                ItemData stockItem = entry.Item;
                Tables.ItemTable table = stockItem.Table;

                // V5 (§8.5): roll 시점 확정 가격 스냅샷 표시. 0(방어)이면 BasePrice fallback.
                int price = entry.UnitPrice > 0 ? entry.UnitPrice : table.BasePrice;
                bool canAfford = (AR.s.Data.Player?.Gold ?? 0) >= price;

                VisualElement row = new();
                row.AddToClassList("shop-buy-row");

                // 아이콘
                VisualElement icon = new();
                icon.AddToClassList("shop-buy-icon");
                if (string.IsNullOrEmpty(table.SpriteName) == false)
                {
                    Sprite? sprite = AR.s.Data.GetSprite(table.SpriteName);
                    if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                }
                row.Add(icon);
                RecoveryPotionIcon.Draw(icon, table);

                // 이름 + 재고 — 스킬북은 책 이름 옆에 스킬명, 스킬 페이지는 페이지 이름 옆에 효과명 부기
                string displayName = table.Name;
                if (stockItem.SkillBook?.Table != null)
                    displayName = $"{table.Name} — {stockItem.SkillBook.Table.Name}";
                else if (stockItem.SkillPage?.Table != null)
                    displayName = $"{table.Name} — {stockItem.SkillPage.Table.Name}";

                VisualElement info = new();
                info.AddToClassList("shop-buy-info");
                Text nameText = new()
                {
                    text = $"{displayName} (재고 {entry.RemainingCount})"
                };
                nameText.AddToClassList("shop-buy-name");
                // V5 (§8.5): 지역 가격 사유 표기 — 지역 특산/현지 부족/희귀 수입.
                string reasonLabel = GetPriceReasonLabel(entry.PriceReason);
                // NPC_AMBITION D42: NPC가 판 전리품 진열 슬롯 표기.
                if (entry.SlotKind == TradeStockSlotKind.AdventurerConsignment)
                    reasonLabel = reasonLabel.Length > 0 ? $"모험가 매입품 · {reasonLabel}" : "모험가 매입품";
                // NPC_SHOP_EQUIPMENT D117: 고정 옵션 기본 장비 칸 표기.
                else if (entry.SlotKind == TradeStockSlotKind.BasicEquipment)
                    reasonLabel = reasonLabel.Length > 0 ? $"기본 장비 · {reasonLabel}" : "기본 장비";
                // PLAYER_LEVEL_CHARM_DESIGN §6.6: 참 칸 표기.
                else if (entry.SlotKind == TradeStockSlotKind.Charm)
                    reasonLabel = reasonLabel.Length > 0 ? $"참 칸 · {reasonLabel}" : "참 칸";
                Text priceText = new()
                {
                    text = reasonLabel.Length > 0
                        ? $"{price:N0} G · {reasonLabel}"
                        : $"{price:N0} G"
                };
                priceText.AddToClassList("shop-buy-price");
                info.Add(nameText);
                info.Add(priceText);
                row.Add(info);

                // 구매 버튼
                Button buyBtn = new() { title = "구매" };
                buyBtn.size = Size.S;
                buyBtn.variant = ButtonVariant.Accent;
                buyBtn.SetEnabled(canAfford == true);
                buyBtn.clicked += () => OnClickBuyStock(stockIdx);
                row.Add(buyBtn);

                // 호버 툴팁 — roll 시점에 완성된 매물 ItemData를 그대로 넘긴다.
                row.userData = stockItem;
                VisualElement rowRef = row;
                row.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(rowRef.userData as ItemData, GetSlotScreenRect(rowRef)));
                row.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());

                list.Add(row);
            }
        }

        /// <summary>
        /// UI Toolkit VisualElement → Screen Space Rect (Y up). 글로벌 툴팁 anchor용.
        /// UISkillBook의 동명 헬퍼와 동일 정책 — 향후 공통 유틸로 추출 가능.
        /// </summary>
        private static Rect GetSlotScreenRect(VisualElement slot)
        {
            if (slot == null) return Rect.zero;
            IPanel? panel = slot.panel;
            if (panel == null) return Rect.zero;

            VisualElement visualTree = panel.visualTree;
            float panelW = visualTree.resolvedStyle.width;
            float panelH = visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f) return Rect.zero;

            float scaleX = Screen.width / panelW;
            float scaleY = Screen.height / panelH;

            Rect bound = slot.worldBound;
            float xMin = bound.xMin * scaleX;
            float xMax = bound.xMax * scaleX;
            float yMaxScreen = Screen.height - bound.yMin * scaleY;
            float yMinScreen = Screen.height - bound.yMax * scaleY;

            return new Rect(xMin, yMinScreen, xMax - xMin, yMaxScreen - yMinScreen);
        }

        // ========== 판매 그리드 ==========

        private void RebuildSellGrid()
        {
            if (_lastRoot == null) return;
            VisualElement? grid = _lastRoot.Q<VisualElement>("sell-grid");
            if (grid == null) return;
            grid.Clear();
            _sellSlots.Clear();

            var inventory = AR.s.Player?.Inventory;
            if (inventory == null || inventory.Items == null || inventory.Items.Count == 0)
            {
                AddEmptyText(grid, "인벤토리 비어있음");
                UpdateSellSelection();
                return;
            }

            var items = inventory.Items;
            int sellableCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int slotIdx = i;
                ItemData? item = items[i];

                VisualElement slot = new();
                slot.AddToClassList("shop-sell-slot");
                _sellSlots.Add(slot);

                bool sellable = item != null && item.Table != null && item.Table.SellRatioBp > 0;

                if (sellable)
                {
                    slot.AddToClassList("shop-sell-slot-sellable");
                    if (slotIdx == _selectedSellSlotIndex)
                        slot.AddToClassList("shop-sell-slot-selected");

                    VisualElement icon = new();
                    icon.AddToClassList("shop-sell-icon");
                    if (item!.Table != null && string.IsNullOrEmpty(item.Table.SpriteName) == false)
                    {
                        Sprite? sprite = AR.s.Data.GetSprite(item.Table.SpriteName);
                        if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                    }
                    slot.Add(icon);
                    RecoveryPotionIcon.Draw(icon, item.Table);

                    if (item.Quantity > 1)
                    {
                        Text qty = new() { text = item.Quantity.ToString() };
                        qty.AddToClassList("shop-sell-qty");
                        slot.Add(qty);
                    }

                    slot.RegisterCallback<ClickEvent>(_ => OnSelectSellSlot(slotIdx));
                    sellableCount++;
                }
                else if (item != null)
                {
                    slot.AddToClassList("shop-sell-slot-disabled");
                }

                // 호버 툴팁 — 팔 수 없는 아이템도 무엇인지 알 수 있게 같은 툴팁을 띄운다.
                if (item != null)
                {
                    slot.userData = item;
                    VisualElement slotRef = slot;
                    slot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetSlotScreenRect(slotRef)));
                    slot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
                }

                grid.Add(slot);
            }

            if (sellableCount == 0)
                AddEmptyText(grid, "매각 가능한 아이템 없음");

            UpdateSellSelection();
        }

        private void OnSelectSellSlot(int slotIndex)
        {
            ItemData? item = AR.s.Player?.Inventory?.GetItemBySlotIndex(slotIndex);
            if (item == null || item.Table == null || item.Table.SellRatioBp <= 0) return;

            _selectedSellSlotIndex = slotIndex;
            // 칸을 다시 만들지 않고 선택 표시만 옮긴다 — 마우스 아래 칸이 그대로라 툴팁이 끊기지 않는다.
            for (int i = 0; i < _sellSlots.Count; i++)
                _sellSlots[i].EnableInClassList("shop-sell-slot-selected", i == slotIndex);
            UpdateSellSelection();
        }

        private void UpdateSellSelection()
        {
            ItemData? item = _selectedSellSlotIndex >= 0
                ? AR.s.Player?.Inventory?.GetItemBySlotIndex(_selectedSellSlotIndex)
                : null;

            if (_selectedSellText != null)
            {
                if (item != null && item.Table != null)
                {
                    // V5 (§8.3): 상인 매입가 = BasePrice × BuyFromPlayerBp / 10000 (TradeProfile 정본).
                    // 장비는 등급·아이템 레벨 배율을 더 곱한다 (ITEM_DROP_BALANCE_DESIGN §9). 실제 지급액과 같은 함수다.
                    // 마을 컨텍스트가 없으면(테스트 모드) 기존 SellRatioBp 표기 유지.
                    int sellPrice = _villageId >= 0
                        ? AR.s.Village.GetPlayerSellPrice(_villageId, item)
                        : item.Table.BasePrice * item.Table.SellRatioBp / 100;
                    if (sellPrice < 0)
                        sellPrice = 0;
                    string reasonLabel = _villageId >= 0
                        ? GetPriceReasonLabel(
                            AR.s.Village.GetMerchantBuyPriceReason(_villageId, item.Table))
                        : string.Empty;
                    _selectedSellText.text = reasonLabel.Length > 0
                        ? $"선택: {item.Table.Name} — 매각가 {sellPrice:N0} G ({reasonLabel})"
                        : $"선택: {item.Table.Name} — 매각가 {sellPrice:N0} G";
                }
                else
                {
                    _selectedSellText.text = "매각할 아이템을 선택하세요";
                }
            }

            if (_sellActionBtn != null)
                _sellActionBtn.SetEnabled(item != null);
        }

        private void AddEmptyText(VisualElement parent, string msg)
        {
            Text emptyText = new() { text = msg };
            emptyText.AddToClassList("shop-empty-text");
            parent.Add(emptyText);
        }

        /// <summary>V5 (§8.5): 지역 가격 사유 표기. Standard는 빈 문자열(표기 없음).</summary>
        private static string GetPriceReasonLabel(TradePriceReason reason)
        {
            switch (reason)
            {
                case TradePriceReason.LocalSpecialty: return "지역 특산";
                case TradePriceReason.LocalShortage: return "현지 부족";
                case TradePriceReason.RareImport: return "희귀 수입";
                default: return string.Empty;
            }
        }
    }
}
