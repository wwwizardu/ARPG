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
    /// Phase D: 강화 UI (Furnace anchor 기준).
    /// HasObjectSet으로 단계 결정:
    ///   - 1단계 (Furnace만): 분해
    ///   - 2단계 (+ Anvil): Mod 재롤
    ///   - 3단계 (+ QuenchVat): Mod 추가 + 비용 -20%
    ///
    /// 사용자 흐름: 인벤토리 그리드에서 장비 선택 → 분해/재롤/MOD추가 버튼 클릭.
    /// </summary>
    public class UIForge : UIBaseForm
    {
        public enum ForgeTier
        {
            None,
            Basic,      // Furnace
            Standard,   // + Anvil
            Premium,    // + Anvil + QuenchVat
        }

        private const int REROLL_BASE_COST = 50;
        private const int ADDMOD_BASE_COST = 80;

        private UIDocument? _document;
        private Button? _disassembleBtn;
        private Button? _rerollBtn;
        private Button? _addModBtn;
        private IconButton? _closeBtn;
        private Text? _tierText;
        private Text? _discountText;
        private Text? _selectedText;

        private int _forgeEntityId = -1;
        private int _villageId = -1;
        private ForgeTier _tier = ForgeTier.None;
        private int _selectedSlotIndex = -1;
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIForge] UIDocument 컴포넌트 없음");
                return;
            }

            EnsureBound();
        }

        /// <summary>
        /// UIDocument는 SetActive 토글 시마다 rootVisualElement를 재구성하므로
        /// root 변경을 감지해 매번 재바인딩. UIManager.Close는 SetActive(false)만 호출하고
        /// 두 번째 Show 시 Initialize를 다시 호출하지 않기 때문.
        /// </summary>
        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return; // 같은 root → 이미 바인딩됨

            _lastRoot = root;

            _disassembleBtn = root.Q<Button>("disassemble-btn");
            _rerollBtn = root.Q<Button>("reroll-btn");
            _addModBtn = root.Q<Button>("addmod-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _tierText = root.Q<Text>("tier-text");
            _discountText = root.Q<Text>("discount-text");
            _selectedText = root.Q<Text>("selected-text");

            if (_disassembleBtn != null) _disassembleBtn.clicked += OnClickDisassemble;
            if (_rerollBtn != null) _rerollBtn.clicked += OnClickReroll;
            if (_addModBtn != null) _addModBtn.clicked += OnClickAddMod;
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void Bind(int forgeEntityId)
        {
            EnsureBound();
            _forgeEntityId = forgeEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_forgeEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIForge] Bind 실패 — entityId={forgeEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;
            Vector2Int anchor = new Vector2Int(po.TileX, po.TileY);

            // 세트 단계 평가 (가장 높은 단계 기준)
            if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgePremium, anchor))
                _tier = ForgeTier.Premium;
            else if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgeStandard, anchor))
                _tier = ForgeTier.Standard;
            else if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgeBasic, anchor))
                _tier = ForgeTier.Basic;
            else
                _tier = ForgeTier.None;

            Debug.Log($"[Forge] v{_villageId} 강화 단계 {_tier}");
            _selectedSlotIndex = -1;
            RefreshAll();
        }

        public void BindForTest(ForgeTier tier = ForgeTier.Premium)
        {
            EnsureBound();
            _forgeEntityId = -1;
            _villageId = -1;
            _tier = tier;
            _selectedSlotIndex = -1;
            RefreshAll();
            Debug.Log($"[UIForge] BindForTest — 테스트 모드 ({tier})");
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            RefreshAll();
        }

        // ========== 액션 ==========

        public void OnClickDisassemble()
        {
            if (_tier < ForgeTier.Basic) return;
            ItemData? item = GetSelectedItem();
            if (item == null) { Debug.Log("[Forge] 선택된 장비 없음"); return; }

            // TODO: 인벤토리 슬롯 → 자원 환원 + 슬롯 비우기
            Debug.Log($"[Forge] 분해: {item.Table?.Name} (slot={_selectedSlotIndex})");
            _selectedSlotIndex = -1;
            RefreshAll();
        }

        public void OnClickReroll()
        {
            if (_tier < ForgeTier.Standard) return;
            ItemData? item = GetSelectedItem();
            if (item == null) { Debug.Log("[Forge] 선택된 장비 없음"); return; }

            int cost = ApplyDiscount(REROLL_BASE_COST);
            // TODO: Currency 차감 + Mod 풀 재롤
            Debug.Log($"[Forge] Mod 재롤: {item.Table?.Name} (slot={_selectedSlotIndex}, 비용={cost}G)");
        }

        public void OnClickAddMod()
        {
            if (_tier < ForgeTier.Premium) return;
            ItemData? item = GetSelectedItem();
            if (item == null) { Debug.Log("[Forge] 선택된 장비 없음"); return; }

            int cost = ApplyDiscount(ADDMOD_BASE_COST);
            // TODO: Currency 차감 + Mod 빈 슬롯 채우기
            Debug.Log($"[Forge] Mod 추가: {item.Table?.Name} (slot={_selectedSlotIndex}, 비용={cost}G)");
        }

        // ========== 인벤토리 그리드 ==========

        private void RebuildInventoryGrid()
        {
            // 매번 root에서 다시 query — 캐시된 _inventoryGrid는 두 번째 Show 시 stale일 수 있음
            // (UIDocument가 비활성/활성될 때 rootVisualElement가 재구성되면서 자식 참조가 끊김)
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;

            VisualElement? grid = root.Q<VisualElement>("inventory-grid");
            if (grid == null) return;

            grid.Clear();

            var inventory = AR.s.Player?.Inventory;
            if (inventory == null)
            {
                AddEmptyMessage(grid, "플레이어 인벤토리 없음");
                return;
            }

            var items = inventory.Items;
            if (items == null || items.Count == 0)
            {
                AddEmptyMessage(grid, "인벤토리 비어있음");
                return;
            }

            int equipmentCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int slotIdx = i; // closure capture
                ItemData? item = items[i];

                VisualElement slot = new VisualElement();
                slot.AddToClassList("forge-inv-slot");

                bool isEquipment = item != null && item.Table != null
                    && item.Table.ItemType == GlobalEnum.ItemType.Equipment;

                if (isEquipment)
                {
                    slot.AddToClassList("forge-inv-slot-equipment");
                    if (slotIdx == _selectedSlotIndex)
                        slot.AddToClassList("forge-inv-slot-selected");

                    VisualElement icon = new VisualElement();
                    icon.AddToClassList("forge-inv-icon");

                    Sprite? sprite = item!.Table != null && string.IsNullOrEmpty(item.Table.SpriteName) == false
                        ? AR.s.Data.GetSprite(item.Table.SpriteName)
                        : null;
                    if (sprite != null)
                        icon.style.backgroundImage = new StyleBackground(sprite);

                    slot.Add(icon);
                    slot.RegisterCallback<ClickEvent>(_ => OnSelectSlot(slotIdx));
                    slot.tooltip = item.Table?.Name ?? string.Empty;
                    equipmentCount++;
                }
                else if (item != null)
                {
                    // 장비 아닌 아이템 — 회색 처리 (선택 불가)
                    slot.AddToClassList("forge-inv-slot-disabled");
                }
                // else: 빈 슬롯 — 기본 스타일

                grid.Add(slot);
            }

            if (equipmentCount == 0)
                AddEmptyMessage(grid, "장비가 없습니다");
        }

        private void AddEmptyMessage(VisualElement grid, string msg)
        {
            Text emptyText = new Text { text = msg };
            emptyText.AddToClassList("forge-inv-empty-text");
            grid.Add(emptyText);
        }

        private void OnSelectSlot(int slotIndex)
        {
            ItemData? item = AR.s.Player?.Inventory?.GetItemBySlotIndex(slotIndex);
            if (item == null || item.Table == null || item.Table.ItemType != GlobalEnum.ItemType.Equipment)
                return;

            _selectedSlotIndex = slotIndex;
            Debug.Log($"[Forge] 선택: slot={slotIndex} {item.Table.Name}");
            RefreshAll();
        }

        // ========== 갱신 ==========

        private ItemData? GetSelectedItem()
        {
            if (_selectedSlotIndex < 0) return null;
            return AR.s.Player?.Inventory?.GetItemBySlotIndex(_selectedSlotIndex);
        }

        private int ApplyDiscount(int baseCost)
        {
            return _tier == ForgeTier.Premium ? Mathf.RoundToInt(baseCost * 0.8f) : baseCost;
        }

        private void RefreshAll()
        {
            if (_tierText != null)
            {
                _tierText.text = _tier switch
                {
                    ForgeTier.Premium => "Premium 단계 (Furnace + Anvil + QuenchVat)",
                    ForgeTier.Standard => "Standard 단계 (Furnace + Anvil)",
                    ForgeTier.Basic => "Basic 단계 (Furnace)",
                    _ => "강화 불가 — 화로 세트 없음",
                };
            }

            if (_discountText != null)
            {
                _discountText.text = _tier == ForgeTier.Premium
                    ? "Premium 할인: 모든 비용 -20%"
                    : string.Empty;
            }

            RebuildInventoryGrid();

            ItemData? selected = GetSelectedItem();
            if (_selectedText != null)
            {
                _selectedText.text = selected != null
                    ? $"선택: {selected.Table?.Name}"
                    : "강화할 장비를 선택하세요";
            }

            // 단계 + 선택 모두 충족해야 활성
            bool hasSelection = selected != null;
            if (_disassembleBtn != null) _disassembleBtn.SetEnabled(_tier >= ForgeTier.Basic && hasSelection);
            if (_rerollBtn != null) _rerollBtn.SetEnabled(_tier >= ForgeTier.Standard && hasSelection);
            if (_addModBtn != null) _addModBtn.SetEnabled(_tier >= ForgeTier.Premium && hasSelection);

            // 비용 표시 (Premium 할인 반영)
            if (_rerollBtn != null) _rerollBtn.title = $"Mod 재롤 ({ApplyDiscount(REROLL_BASE_COST)}G)";
            if (_addModBtn != null) _addModBtn.title = $"Mod 추가 ({ApplyDiscount(ADDMOD_BASE_COST)}G)";
        }
    }
}
