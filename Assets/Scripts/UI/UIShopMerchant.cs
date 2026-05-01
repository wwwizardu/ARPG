#nullable enable
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
            int gold = AR.s.Village.SellItemToMerchant(_villageId, _selectedSellSlotIndex, 1);
            if (gold < 0) return;
            _selectedSellSlotIndex = -1;
            RefreshAll();
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            if (_lastRoot == null) return;

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
                if (entry.RemainingCount <= 0) continue;

                Tables.ItemTable? table = AR.s.Data.GetItem(entry.ItemTableId);
                if (table == null) continue;

                int price = table.BasePrice;
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

                // 이름 + 재고
                VisualElement info = new();
                info.AddToClassList("shop-buy-info");
                Text nameText = new() { text = $"{table.Name} (재고 {entry.RemainingCount})" };
                nameText.AddToClassList("shop-buy-name");
                Text priceText = new() { text = $"{price:N0} G" };
                priceText.AddToClassList("shop-buy-price");
                info.Add(nameText);
                info.Add(priceText);
                row.Add(info);

                // 구매 버튼
                Button buyBtn = new() { title = "구매" };
                buyBtn.size = Size.S;
                buyBtn.variant = ButtonVariant.Accent;
                buyBtn.SetEnabled(canAfford);
                buyBtn.clicked += () => OnClickBuyStock(stockIdx);
                row.Add(buyBtn);

                list.Add(row);
            }
        }

        // ========== 판매 그리드 ==========

        private void RebuildSellGrid()
        {
            if (_lastRoot == null) return;
            VisualElement? grid = _lastRoot.Q<VisualElement>("sell-grid");
            if (grid == null) return;
            grid.Clear();

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

                    if (item.Quantity > 1)
                    {
                        Text qty = new() { text = item.Quantity.ToString() };
                        qty.AddToClassList("shop-sell-qty");
                        slot.Add(qty);
                    }

                    slot.RegisterCallback<ClickEvent>(_ => OnSelectSellSlot(slotIdx));
                    slot.tooltip = item.Table?.Name ?? string.Empty;
                    sellableCount++;
                }
                else if (item != null)
                {
                    slot.AddToClassList("shop-sell-slot-disabled");
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
            RefreshAll();
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
                    int sellPrice = item.Table.BasePrice * item.Table.SellRatioBp / 100;
                    _selectedSellText.text = $"선택: {item.Table.Name} — 매각가 {sellPrice:N0} G";
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
    }
}
