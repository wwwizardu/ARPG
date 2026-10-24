#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Data;
using ARPG.Utility;
using TMPro;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 캐릭터·인벤토리 창 (I키). UI Toolkit 한 장에 능력치(UICharacter_Stats.cs)·장비 12칸·인벤토리를 둔다.
    /// 장착·해제는 PlayerData와 EquipHelper를 그대로 부르고, 칸은 기억한 아이템 참조·수량이 데이터와 다를 때만 다시 그린다.
    /// 인벤토리 장비 비교(INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md)의 호버 상태와 버튼별 장착도 여기서 맡는다.
    /// </summary>
    public partial class UICharacter : UIBaseForm
    {
        /// <summary>
        /// 마우스 버튼을 누른 순간의 모드와 비교 대상 (INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §이용 흐름과 조작).
        /// 모드는 누르는 순간의 Shift 상태로 정하고, 클릭(같은 칸에서 뗄 때)은 이 기록대로 장착한다.
        /// </summary>
        private struct PressRecord
        {
            public bool IsPressed;                      // 이 칸에서 눌렀다 — 같은 칸에서 떼야 클릭이다
            public int SlotIndex;
            public bool IsCompare;
            public ItemData? Item;
            public int ItemInstanceId;
            public bool HasTarget;                      // 비교 중 휠 클릭은 대상이 없다 (장착하지 않음)
            public GlobalEnum.EquipSlotType TargetSlot;
            public ItemData? TargetEquipped;
            public int TargetEquippedInstanceId;
        }

        /// <summary>칸 element와 마지막으로 그린 아이템. 칸 root의 userData로 붙여 콜백이 찾는다.</summary>
        private sealed class SlotView
        {
            public int Index;
            public VisualElement Root = null!;
            public VisualElement Icon = null!;
            public Text? Quantity;      // 인벤토리 칸만
            public VisualElement? EmptyLabel; // 장비 칸만 — 빈 칸에 칸 이름
            public ItemData? ShownItem;
            public int ShownQuantity;
        }

        private const int POINTER_BUTTON_COUNT = 3; // PointerEventBase.button: 0 왼쪽, 1 오른쪽, 2 휠

        private UIDocument? _document;
        private VisualElement? _lastRoot;
        private readonly SlotView?[] _equipSlots = new SlotView?[(int)GlobalEnum.EquipSlotType.Max];
        private readonly List<SlotView> _inventorySlots = new(GlobalEnum.PLAYER_INVENTORY_SLOTCOUNT_MAX);

        // 인벤토리 장비 호버·비교 상태. 표시는 호버 진입, Shift 누름·뗌, 칸의 아이템 변경, 비교 대상 장착 변경 때만 다시 만들고
        // 매 프레임은 참조 비교만 한다.
        private int _hoveredInventoryIndex = -1;
        private ItemData? _shownItem = null;
        private bool _isComparing = false;
        private GlobalEnum.EquipSlotType _compareLeftClickSlot = GlobalEnum.EquipSlotType.Max;
        private GlobalEnum.EquipSlotType _compareRightClickSlot = GlobalEnum.EquipSlotType.Max;
        private ItemData? _compareLeftClickEquipped = null;
        private ItemData? _compareRightClickEquipped = null;
        private readonly PressRecord[] _pressRecords = new PressRecord[POINTER_BUTTON_COUNT];
        private readonly int[] _pressedEquipSlots = { -1, -1, -1 };

        // 칸·버튼 콜백. Initialize는 Show마다 불리므로 처음 한 번만 만들고, 칸을 다시 만들 때도 같은 인스턴스를 쓴다.
        private bool _handlersCreated;
        private System.Action _closeClickedHandler = null!;
        private EventCallback<PointerUpEvent> _rootPointerUpHandler = null!;
        private EventCallback<PointerEnterEvent> _inventoryEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _inventoryLeaveHandler = null!;
        private EventCallback<PointerDownEvent> _inventoryDownHandler = null!;
        private EventCallback<PointerUpEvent> _inventoryUpHandler = null!;
        private EventCallback<PointerEnterEvent> _equipEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _equipLeaveHandler = null!;
        private EventCallback<PointerDownEvent> _equipDownHandler = null!;
        private EventCallback<PointerUpEvent> _equipUpHandler = null!;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UICharacter] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            if (_handlersCreated == false)
            {
                CreateHandlers();
                CreateStatHandlers();
                _handlersCreated = true;
            }
            EnsureBound();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            SubscribeStatMessages();
            SyncSlots(true);
            UpdateCharacterStat();
        }

        public override void OnClose()
        {
            // 닫으면 비교와 슬롯 강조를 남기지 않는다. 툴팁은 UIManager.Close가 닫는다.
            ClearHover();
            ClearPressRecords();
            UnsubscribeStatMessages();
            base.OnClose();
        }

        public override bool UpdateInput(Input.ArpgInput inInput)
        {
            if (base.UpdateInput(inInput) == true)
                return true;

            if (inInput.UI.CloseInventory.WasReleasedThisFrame() == true)
            {
                Close();
                return true;
            }

            // 참 판 창처럼 위에 뜬 창이 인벤토리를 바꿔도 따라간다 (바뀐 칸만 다시 그림)
            SyncSlots(false);
            UpdateHoverDisplay(inInput);
            return false;
        }

        protected override void OnDestroy()
        {
            UnsubscribeStatMessages();
            base.OnDestroy();
        }

        private void CreateHandlers()
        {
            _closeClickedHandler = OnClickClose;
            _rootPointerUpHandler = OnRootPointerUp;
            _inventoryEnterHandler = OnInventorySlotEnter;
            _inventoryLeaveHandler = OnInventorySlotLeave;
            _inventoryDownHandler = OnInventorySlotDown;
            _inventoryUpHandler = OnInventorySlotUp;
            _equipEnterHandler = OnEquipSlotEnter;
            _equipLeaveHandler = OnEquipSlotLeave;
            _equipDownHandler = OnEquipSlotDown;
            _equipUpHandler = OnEquipSlotUp;
        }

        /// <summary>
        /// UIDocument는 SetActive 토글 시마다 rootVisualElement를 재구성하므로
        /// root 변경을 감지해 버튼을 다시 묶고 칸 element를 다시 만든다.
        /// </summary>
        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            IconButton? closeBtn = root.Q<IconButton>("close-btn");
            if (closeBtn != null) closeBtn.clicked += _closeClickedHandler;
            // 칸 콜백(대상 단계)이 끝난 뒤 올라오는 뗌으로 누름 기록을 지운다 — 칸 밖에서 뗀 누름이 다음 클릭으로 남지 않게
            root.RegisterCallback(_rootPointerUpHandler);

            BindEquipSlots(root);
            BindInventorySlots(root);
            BindStats(root);
        }

        private void OnClickClose()
        {
            Close();
        }

        // ========== 칸 만들기·그리기 ==========

        private void BindEquipSlots(VisualElement root)
        {
            for (int i = 0; i < _equipSlots.Length; i++)
            {
                GlobalEnum.EquipSlotType slotType = (GlobalEnum.EquipSlotType)i;
                VisualElement? element = root.Q<VisualElement>($"equip-{slotType}");
                if (element == null)
                {
                    Debug.LogError($"[UICharacter] 장비 칸 element 없음 — equip-{slotType}");
                    _equipSlots[i] = null;
                    continue;
                }

                VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("char-equip-icon");
                element.Add(icon);

                SlotView view = new SlotView
                {
                    Index = i,
                    Root = element,
                    Icon = icon,
                    EmptyLabel = element.Q(className: "char-equip-label"),
                };
                element.userData = view;
                element.RegisterCallback(_equipEnterHandler);
                element.RegisterCallback(_equipLeaveHandler);
                element.RegisterCallback(_equipDownHandler);
                element.RegisterCallback(_equipUpHandler);
                _equipSlots[i] = view;
            }
        }

        private void BindInventorySlots(VisualElement root)
        {
            _inventorySlots.Clear();
            VisualElement? grid = root.Q<VisualElement>("inventory-grid");
            if (grid == null)
            {
                Debug.LogError("[UICharacter] inventory-grid element 없음");
                return;
            }
            grid.Clear();

            int slotCount = AR.s.Player?.Inventory?.Items?.Count ?? 0;
            for (int i = 0; i < slotCount; i++)
            {
                VisualElement slot = new VisualElement();
                slot.AddToClassList("char-inv-slot");

                VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("char-inv-icon");
                slot.Add(icon);

                Text quantity = new Text { pickingMode = PickingMode.Ignore };
                quantity.AddToClassList("char-inv-qty");
                slot.Add(quantity);

                SlotView view = new SlotView { Index = i, Root = slot, Icon = icon, Quantity = quantity };
                slot.userData = view;
                slot.RegisterCallback(_inventoryEnterHandler);
                slot.RegisterCallback(_inventoryLeaveHandler);
                slot.RegisterCallback(_inventoryDownHandler);
                slot.RegisterCallback(_inventoryUpHandler);
                grid.Add(slot);
                _inventorySlots.Add(view);
            }
        }

        /// <summary>칸에 기억한 아이템 참조·수량이 데이터와 다른 칸만 다시 그린다. force면 전부 그린다.</summary>
        private void SyncSlots(bool force)
        {
            PlayerData? player = AR.s.Data?.Player;
            if (player != null)
            {
                for (int i = 0; i < _equipSlots.Length && i < player._inventoryEquip.Length; i++)
                {
                    SlotView? view = _equipSlots[i];
                    if (view == null)
                        continue;
                    ItemData? item = player._inventoryEquip[i];
                    if (force == true || view.ShownItem != item)
                        DrawSlot(view, item);
                }
            }

            List<ItemData?>? items = AR.s.Player?.Inventory?.Items;
            if (items == null)
                return;

            // 인벤토리 칸 수가 바뀌었으면(새 플레이어 데이터) 칸을 다시 만든다
            if (_inventorySlots.Count != items.Count && _lastRoot != null)
            {
                BindInventorySlots(_lastRoot);
                force = true;
            }

            for (int i = 0; i < _inventorySlots.Count && i < items.Count; i++)
            {
                SlotView view = _inventorySlots[i];
                ItemData? item = items[i];
                int quantity = item != null ? item.Quantity : 0;
                if (force == true || view.ShownItem != item || view.ShownQuantity != quantity)
                    DrawSlot(view, item);
            }
        }

        private static void DrawSlot(SlotView view, ItemData? item)
        {
            view.ShownItem = item;
            view.ShownQuantity = item != null ? item.Quantity : 0;

            Sprite? sprite = null;
            if (item != null)
            {
                if (item.Table == null)
                    Debug.LogError($"[UICharacter] DrawSlot - itemTable is null, itemId({item.Id})");
                else if (string.IsNullOrEmpty(item.Table.SpriteName) == false)
                    sprite = AR.s.Data.GetSprite(item.Table.SpriteName);
            }
            view.Icon.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
            view.Root.EnableInClassList("char-slot-filled", item != null);

            if (view.Quantity != null)
            {
                bool showQuantity = item != null && item.Quantity > 1;
                view.Quantity.text = showQuantity == true ? item!.Quantity.ToString() : string.Empty;
                view.Quantity.style.display = showQuantity == true ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (view.EmptyLabel != null)
                view.EmptyLabel.style.display = item == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static bool TryGetSlotView(IEventHandler? target, out SlotView view)
        {
            view = null!;
            if (target is not VisualElement element || element.userData is not SlotView slotView)
                return false;
            view = slotView;
            return true;
        }

        private static bool IsValidButton(int button)
        {
            return button >= 0 && button < POINTER_BUTTON_COUNT;
        }

        private void OnRootPointerUp(PointerUpEvent evt)
        {
            if (IsValidButton(evt.button) == false)
                return;
            _pressRecords[evt.button] = default;
            _pressedEquipSlots[evt.button] = -1;
        }

        // ========== 장비 칸 — 호버 툴팁, 클릭 해제 ==========

        private void OnEquipSlotEnter(PointerEnterEvent evt)
        {
            if (TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;

            ItemData? item = AR.s.Data.Player.GetEquipedItem((GlobalEnum.EquipSlotType)view.Index);
            if (item != null && item.Table != null)
                AR.s.Tooltip.Show(item, GetSlotScreenRect(view.Root));
        }

        private void OnEquipSlotLeave(PointerLeaveEvent evt)
        {
            AR.s.Tooltip.Hide();
        }

        private void OnEquipSlotDown(PointerDownEvent evt)
        {
            if (IsValidButton(evt.button) == false || TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;
            _pressedEquipSlots[evt.button] = view.Index;
        }

        private void OnEquipSlotUp(PointerUpEvent evt)
        {
            if (IsValidButton(evt.button) == false || TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;
            if (_pressedEquipSlots[evt.button] != view.Index)
                return;
            _pressedEquipSlots[evt.button] = -1;
            UnequipToInventory((GlobalEnum.EquipSlotType)view.Index);
        }

        private void UnequipToInventory(GlobalEnum.EquipSlotType equipSlot)
        {
            Item.Inventory? inventory = AR.s.Player?.Inventory;
            if (inventory == null)
                return;

            // 인벤토리에 여유 공간이 있는지 먼저 체크
            if (HasEmptySlot(inventory) == false)
            {
                Debug.Log("[UICharacter] UnequipToInventory - Inventory full, cannot unequip item");
                return;
            }

            if (UnequipItem(equipSlot, out ItemData? unequippedItem) == false)
                return;

            // 해제된 아이템을 인벤토리에 추가. 여유 공간을 체크했으므로 실패하면 다시 장착한다
            if (unequippedItem != null && inventory.AddItem(unequippedItem) == -1)
            {
                Debug.LogError("[UICharacter] UnequipToInventory - Failed to add unequipped item to inventory (should not happen)");
                EquipItem(equipSlot, unequippedItem, out _);
            }

            SyncSlots(false);
            AR.s.Tooltip.Hide(); // 칸이 비었다
        }

        // ========== 인벤토리 칸 — 호버·비교, 클릭 장착 ==========

        private void OnInventorySlotEnter(PointerEnterEvent evt)
        {
            if (TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;

            _hoveredInventoryIndex = view.Index;
            _shownItem = null;
            RefreshHoverDisplay(AR.s.UI.Input);
        }

        private void OnInventorySlotLeave(PointerLeaveEvent evt)
        {
            if (TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;
            if (_hoveredInventoryIndex != view.Index)
                return;

            ClearHover();
            AR.s.Tooltip.Hide();
        }

        private void OnInventorySlotDown(PointerDownEvent evt)
        {
            if (IsValidButton(evt.button) == false || TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;

            // 같은 프레임에 Shift를 누른 경우까지 누르는 순간의 상태로 표시를 맞춘 뒤 모드를 기록한다
            Input.ArpgInput input = AR.s.UI.Input;
            if (_hoveredInventoryIndex == view.Index && IsHoverDisplayStale(input) == true)
                RefreshHoverDisplay(input);

            PressRecord record = new PressRecord { IsPressed = true, SlotIndex = view.Index };
            if (_hoveredInventoryIndex == view.Index && _isComparing == true && _shownItem != null)
            {
                record.IsCompare = true;
                record.Item = _shownItem;
                record.ItemInstanceId = _shownItem.ItemInstanceId;
                if (evt.button == (int)MouseButton.LeftMouse)
                {
                    record.HasTarget = true;
                    record.TargetSlot = _compareLeftClickSlot;
                    record.TargetEquipped = _compareLeftClickEquipped;
                }
                else if (evt.button == (int)MouseButton.RightMouse)
                {
                    record.HasTarget = true;
                    record.TargetSlot = _compareRightClickSlot;
                    record.TargetEquipped = _compareRightClickEquipped;
                }
                if (record.TargetEquipped != null)
                    record.TargetEquippedInstanceId = record.TargetEquipped.ItemInstanceId;
            }
            _pressRecords[evt.button] = record;
        }

        private void OnInventorySlotUp(PointerUpEvent evt)
        {
            if (IsValidButton(evt.button) == false || TryGetSlotView(evt.currentTarget, out SlotView view) == false)
                return;

            PressRecord press = _pressRecords[evt.button];
            _pressRecords[evt.button] = default;
            if (press.IsPressed == false || press.SlotIndex != view.Index)
                return; // 다른 칸에서 누르고 여기서 뗐다 — 클릭이 아니다

            if (press.IsCompare == true)
                EquipFromComparison(view.Index, press); // 비교 중: 누른 버튼의 화면 위치 슬롯
            else
                EquipFromInventoryAuto(view.Index);     // Shift 없는 클릭: 현행 자동 선택 (버튼 구분 없음)
            SyncSlots(false);

            // 장착 후 포인터가 있는 칸의 현재 내용으로 다시 표시한다 (Shift 유지 중이면 비교, 칸이 비면 닫기)
            Input.ArpgInput input = AR.s.UI.Input;
            if (_hoveredInventoryIndex == view.Index && IsHoverDisplayStale(input) == true)
                RefreshHoverDisplay(input);
        }

        private void ClearPressRecords()
        {
            for (int i = 0; i < _pressRecords.Length; i++)
            {
                _pressRecords[i] = default;
                _pressedEquipSlots[i] = -1;
            }
        }

        // ========== 장착·해제 ==========

        private void EquipFromInventoryAuto(int inSlotIndex)
        {
            ItemData? itemData = AR.s.Player.Inventory.GetItemBySlotIndex(inSlotIndex);
            if (itemData == null || itemData.Table == null || itemData.Equipment == null) // 아이템이 없거나 장비 아이템이 아닌 경우
                return;

            GlobalEnum.EquipSlotType targetSlot = EquipHelper.GetBestSlot(
                itemData.Equipment.EquipType,
                AR.s.Data.Player._inventoryEquip
            );

            EquipFromInventory(inSlotIndex, itemData, targetSlot);
        }

        /// <summary>
        /// 비교 중 클릭: 누른 순간 표시된 비교의 대상 슬롯에 장착한다.
        /// 장착 직전에 인벤토리 칸·ItemInstanceId·대상 칸 장착 가능 여부·현재 장착 아이템을 다시 확인하고,
        /// 그 사이 바뀌었으면 오래된 비교로 교체하지 않고 비교를 갱신한다 (다시 클릭하면 장착).
        /// </summary>
        private void EquipFromComparison(int inSlotIndex, in PressRecord inPress)
        {
            // 비교 중 휠 클릭은 장착하지 않는다
            if (inPress.HasTarget == false)
                return;

            ItemData? current = AR.s.Player.Inventory.GetItemBySlotIndex(inSlotIndex);
            ItemData? equipped = AR.s.Data.Player.GetEquipedItem(inPress.TargetSlot);
            bool isValid = current != null
                && current == inPress.Item
                && current.ItemInstanceId == inPress.ItemInstanceId
                && current.Equipment != null
                && CanEquip(inPress.TargetSlot, current) == true
                && equipped == inPress.TargetEquipped
                && (equipped == null || equipped.ItemInstanceId == inPress.TargetEquippedInstanceId);
            if (isValid == false)
            {
                Debug.Log($"[UICharacter] EquipFromComparison - Comparison target changed, refreshing, inSlotIndex({inSlotIndex}), targetSlot({inPress.TargetSlot})");
                if (_hoveredInventoryIndex == inSlotIndex)
                    RefreshHoverDisplay(AR.s.UI.Input);
                return;
            }

            EquipFromInventory(inSlotIndex, current!, inPress.TargetSlot);
        }

        private void EquipFromInventory(int inSlotIndex, ItemData inItem, GlobalEnum.EquipSlotType inTargetSlot)
        {
            if (EquipItem(inTargetSlot, inItem, out ItemData? replacedItem) == false)
                return;

            Item.Inventory inventory = AR.s.Player.Inventory;
            if (replacedItem != null)
            {
                // 기존 장착 아이템이 있었다면 장착한 아이템이 있던 칸에 다시 넣기
                if (inventory.AddItem(replacedItem, inSlotIndex, out _) == false)
                    Debug.LogWarning($"[UICharacter] EquipFromInventory - Inventory full, cannot add replaced item, inSlotIndex({inSlotIndex})");
            }
            else
            {
                // 장착 성공했으니 인벤토리에서 아이템 제거
                inventory.RemoveItem(inSlotIndex, 1, out _);
            }
        }

        // 지정된 장비 슬롯에 아이템을 장착한다. 기존 장착 아이템은 replacedItem으로 돌려준다.
        private static bool EquipItem(GlobalEnum.EquipSlotType inEquipType, ItemData inItem, out ItemData? replacedItem)
        {
            replacedItem = null;
            if (inItem == null)
                return false;

            // 장비 아이템 데이터에 세팅
            if (AR.s.Data.Player.EquipItem(inEquipType, inItem, out replacedItem) == false)
                return false;

            int playerEntityId = AR.s.Data.Player.PlayerId;

            // 교체되는 아이템이 있으면 기존 modifier 제거
            if (replacedItem != null)
                EquipHelper.RemoveEquipmentModifiers(playerEntityId, replacedItem.ItemInstanceId);

            // 새 장비의 modifier 적용
            EquipHelper.ApplyEquipmentModifiers(playerEntityId, inItem);
            return true;
        }

        // 지정된 장비 슬롯을 비운다. 해제된 아이템은 unequippedItem으로 돌려준다.
        private static bool UnequipItem(GlobalEnum.EquipSlotType inEquipType, out ItemData? unequippedItem)
        {
            // 장비 아이템 데이터에 세팅
            if (AR.s.Data.Player.UnequipItem(inEquipType, out unequippedItem) == false)
                return false;

            // 해제된 장비의 modifier 제거
            if (unequippedItem != null)
                EquipHelper.RemoveEquipmentModifiers(AR.s.Data.Player.PlayerId, unequippedItem.ItemInstanceId);
            return true;
        }

        private static bool CanEquip(GlobalEnum.EquipSlotType inEquipType, ItemData inItem)
        {
            if (inItem == null || inItem.Equipment == null)
                return false;

            if (AR.s.Data.GetItem(inItem.Id) == null)
            {
                Debug.LogError($"[UICharacter] CanEquip - ItemTable not found for ID: {inItem.Id}");
                return false;
            }

            GlobalEnum.EquipmentTypeMask allowedMask = EquipHelper.SlotToEquipmentTypeMask(inEquipType);
            return Utils.ContainsEquipmentType(allowedMask, inItem.Equipment.EquipType);
        }

        private static bool HasEmptySlot(Item.Inventory inventory)
        {
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                if (inventory.Items[i] == null)
                    return true;
            }
            return false;
        }

        // ========== 인벤토리 장비 호버·비교 ==========

        private void UpdateHoverDisplay(Input.ArpgInput inInput)
        {
            if (_hoveredInventoryIndex < 0)
                return;

            if (IsHoverDisplayStale(inInput) == true)
                RefreshHoverDisplay(inInput);

            // 인벤토리 슬롯 위 휠은 비교 본문만 스크롤한다 (인벤토리에는 스크롤 영역이 없다)
            if (_isComparing == true)
            {
                float wheel = inInput.UI.ScrollWheel.ReadValue<Vector2>().y;
                if (wheel != 0f)
                    AR.s.Tooltip.ScrollEquipmentComparison(wheel);
            }
        }

        /// <summary>표시를 만든 뒤 칸의 아이템, Shift 상태, 비교 대상 장착 아이템이 바뀌었는지 (참조 비교만 한다).</summary>
        private bool IsHoverDisplayStale(Input.ArpgInput inInput)
        {
            if (_hoveredInventoryIndex < 0)
                return false;

            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(_hoveredInventoryIndex);
            if (item != _shownItem)
                return true;
            if (ShouldCompare(inInput, item) != _isComparing)
                return true;

            if (_isComparing == true)
            {
                PlayerData player = AR.s.Data.Player;
                if (player.GetEquipedItem(_compareLeftClickSlot) != _compareLeftClickEquipped)
                    return true;
                if (player.GetEquipedItem(_compareRightClickSlot) != _compareRightClickEquipped)
                    return true;
            }
            return false;
        }

        private void RefreshHoverDisplay(Input.ArpgInput inInput)
        {
            if (_hoveredInventoryIndex < 0 || _hoveredInventoryIndex >= _inventorySlots.Count)
            {
                ClearHover();
                return;
            }

            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(_hoveredInventoryIndex);
            _shownItem = item;
            if (item == null || item.Table == null)
            {
                // 빈 칸에 장착해 칸이 비면 툴팁을 닫는다
                StopComparing();
                AR.s.Tooltip.Hide();
                return;
            }

            Rect anchor = GetSlotScreenRect(_inventorySlots[_hoveredInventoryIndex].Root);
            if (item.Table.ItemType != GlobalEnum.ItemType.Equipment || item.Equipment == null)
            {
                // 장비가 아닌 아이템은 비교 안내 없는 단일 툴팁 (참·참 판은 모양·옵션, 스킬북·페이지는 스킬, 그 밖은 이름·설명)
                StopComparing();
                AR.s.Tooltip.Show(item, anchor);
                return;
            }

            if (IsCompareRequested(inInput) == false)
            {
                StopComparing();
                AR.s.Tooltip.ShowWithCompareHint(item, anchor);
                return;
            }

            EquipHelper.GetScreenSideSlots(item.Equipment.EquipType,
                out GlobalEnum.EquipSlotType leftClickSlot, out GlobalEnum.EquipSlotType rightClickSlot);
            PlayerData player = AR.s.Data.Player;
            _compareLeftClickSlot = leftClickSlot;
            _compareRightClickSlot = rightClickSlot;
            _compareLeftClickEquipped = player.GetEquipedItem(leftClickSlot);
            _compareRightClickEquipped = player.GetEquipedItem(rightClickSlot);
            _isComparing = true;

            SetCompareHighlight(leftClickSlot, rightClickSlot);
            AR.s.Tooltip.ShowEquipmentComparison(item,
                leftClickSlot, _compareLeftClickEquipped,
                rightClickSlot, _compareRightClickEquipped,
                anchor);
        }

        private bool ShouldCompare(Input.ArpgInput inInput, ItemData? inItem)
        {
            if (inItem == null || inItem.Table == null || inItem.Equipment == null)
                return false;
            if (inItem.Table.ItemType != GlobalEnum.ItemType.Equipment)
                return false;
            return IsCompareRequested(inInput);
        }

        /// <summary>Shift(좌·우)를 누르고 있고, 이 UI가 전면이며, 입력 필드가 포커스를 갖지 않을 때만 비교한다.</summary>
        private bool IsCompareRequested(Input.ArpgInput inInput)
        {
            if (inInput.UI.CompareEquipment.IsPressed() == false)
                return false;
            if (AR.s.UI.CurrentUI != this)
                return false;
            if (IsTextInputFocused() == true)
                return false;
            return true;
        }

        private static bool IsTextInputFocused()
        {
            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            GameObject? selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
                return false;

            if (selected.TryGetComponent(out TMP_InputField tmpInput) == true && tmpInput.isFocused == true)
                return true;
            if (selected.TryGetComponent(out UnityEngine.UI.InputField legacyInput) == true && legacyInput.isFocused == true)
                return true;
            return false;
        }

        /// <summary>인벤토리 장비 비교 대상 장비 칸만 테두리로 강조한다. 칸이 하나인 장비는 두 인자가 같다.</summary>
        private void SetCompareHighlight(GlobalEnum.EquipSlotType inScreenLeftSlot, GlobalEnum.EquipSlotType inScreenRightSlot)
        {
            for (int i = 0; i < _equipSlots.Length; i++)
            {
                SlotView? view = _equipSlots[i];
                if (view == null)
                    continue;
                GlobalEnum.EquipSlotType slotType = (GlobalEnum.EquipSlotType)i;
                view.Root.EnableInClassList("char-equip-slot-compare", slotType == inScreenLeftSlot || slotType == inScreenRightSlot);
            }
        }

        private void StopComparing()
        {
            if (_isComparing == true)
                SetCompareHighlight(GlobalEnum.EquipSlotType.Max, GlobalEnum.EquipSlotType.Max);

            _isComparing = false;
            _compareLeftClickSlot = GlobalEnum.EquipSlotType.Max;
            _compareRightClickSlot = GlobalEnum.EquipSlotType.Max;
            _compareLeftClickEquipped = null;
            _compareRightClickEquipped = null;
        }

        private void ClearHover()
        {
            StopComparing();
            _hoveredInventoryIndex = -1;
            _shownItem = null;
        }

        /// <summary>
        /// UI Toolkit VisualElement → Screen Space Rect (Y up). 글로벌 툴팁 anchor용.
        /// UIShopMerchant/UISkillBook/UINpcInspect의 동명 헬퍼와 동일 정책.
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
    }
}
