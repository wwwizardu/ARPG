#nullable enable
using ARPG.Base;
using UnityEngine;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Utility;
using TMPro;

namespace ARPG.UI
{
    public class UICharacter : UIBaseForm
    {
        /// <summary>
        /// 마우스 버튼을 누른 순간의 모드와 비교 대상 (INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §이용 흐름과 조작).
        /// 모드는 누르는 순간의 Shift 상태로 정하고, 클릭(뗄 때)은 이 기록대로 장착한다.
        /// </summary>
        private struct PressRecord
        {
            public SlotUI? Slot;
            public bool IsCompare;
            public ItemData? Item;
            public int ItemInstanceId;
            public bool HasTarget;                      // 비교 중 휠 클릭은 대상이 없다 (장착하지 않음)
            public GlobalEnum.EquipSlotType TargetSlot;
            public ItemData? TargetEquipped;
            public int TargetEquippedInstanceId;
        }

        [SerializeField] private Transform _characterRoot = null!;
        [SerializeField] private Transform _inventoryRoot = null!;

        private UIInventory? _inventoryUI;
        private CharacterUI? _characterUI;
        private bool _loadCompleted = false;

        // 인벤토리 장비 호버·비교 상태. 표시는 호버 진입, Shift 누름·뗌, 칸의 아이템 변경, 비교 대상 장착 변경 때만 다시 만들고
        // 매 프레임은 참조 비교만 한다.
        private SlotUI? _hoveredSlot = null;
        private ItemData? _shownItem = null;
        private bool _isComparing = false;
        private GlobalEnum.EquipSlotType _compareLeftClickSlot = GlobalEnum.EquipSlotType.Max;
        private GlobalEnum.EquipSlotType _compareRightClickSlot = GlobalEnum.EquipSlotType.Max;
        private ItemData? _compareLeftClickEquipped = null;
        private ItemData? _compareRightClickEquipped = null;
        private readonly PressRecord[] _pressRecords = new PressRecord[3]; // PointerEventData.InputButton: Left, Right, Middle

        // 인벤토리 슬롯에 넘기는 콜백. 초기화 때 한 번 만들어 모든 슬롯이 같은 인스턴스를 쓴다.
        private Action<SlotUI>? _onHoverInventorySlot;
        private Action<SlotUI>? _onExitInventorySlot;
        private Action<SlotUI, PointerEventData>? _onPressInventorySlot;

        public bool LoadCompleted => _loadCompleted;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _onHoverInventorySlot = OnHoverInventorySlot;
            _onExitInventorySlot = OnExitInventorySlot;
            _onPressInventorySlot = OnPressInventorySlot;

            LoadUI();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            // 첫 오픈은 LoadUI 비동기 완료 후 OnLoadCompleted가 그리므로 스킵.
            // 두 번째 이후 오픈에서 인벤토리 데이터(스킬북 장착/해제 등 외부 변경 반영)를 SlotUI에 동기화.
            if (_loadCompleted == true && _inventoryUI != null) _inventoryUI.RefreshAll();
        }

        public override void OnClose()
        {
            base.OnClose();

            // 닫으면 비교와 슬롯 강조를 남기지 않는다. 툴팁은 UIManager.Close가 닫는다.
            ClearHover();
            ClearPressRecords();
        }

        public override bool UpdateInput(Input.ArpgInput inInput)
        {
            if (_loadCompleted == false)
                return false;

            if (base.UpdateInput(inInput) == true)
                return true;

            if (inInput.UI.CloseInventory.WasReleasedThisFrame() == true)
            {
                Close();
                return true;
            }

            UpdateHoverDisplay(inInput);
            return false;
        }

        private async void LoadUI()
        {
            var loadTasks = new List<UniTask>();

            if (_characterUI == null)
            {
                loadTasks.Add(LoadCharacterUI());
            }

            if (_inventoryUI == null)
            {
                loadTasks.Add(LoadInventoryUI());
            }

            await UniTask.WhenAll(loadTasks);

            _characterUI?.OnLoadCompleted();
            _inventoryUI?.OnLoadCompleted();

            _loadCompleted = true;
            Debug.Log("UICharacter LoadUI completed");
        }

        private async UniTask LoadCharacterUI()
        {
            // 캐릭터 UI 로드 로직 (필요시 구현)
            _characterUI = await LoadUIAsync<CharacterUI>("UI/CharacterUI", _characterRoot);
            if (_characterUI == null)
            {
                Debug.LogError("[UICharacter] Failed to load Character UI");
                return;
            }

            _characterUI?.Initialize(OnClickSlot);
        }

        private async UniTask LoadInventoryUI()
        {
            _inventoryUI = await LoadUIAsync<UIInventory>(AddressablePath.Inventory, _inventoryRoot);
            if (_inventoryUI == null)
            {
                Debug.LogError("[UICharacter] Failed to load Inventory UI");
                return;
            }

            await _inventoryUI!.Initialize(AddressablePath.Inventory, AR.s.Player.Inventory, OnClickSlot); // 슬롯 40개로 초기화
            _inventoryUI.SetSlotPointerHandlers(_onHoverInventorySlot!, _onExitInventorySlot!, _onPressInventorySlot!);
        }

        private async UniTask<T?> LoadUIAsync<T>(string inName, Transform inParent) where T : UIBase
        {
            var handle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(inName, inParent);
            await handle.ToUniTask();

            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[UICharacter] Failed to load UI: {inName}");
                return null;
            }

            var uiComponent = handle.Result.GetComponent<T>();
            if (uiComponent == null)
            {
                Debug.LogError($"[UICharacter] UI component {typeof(T).Name} not found on loaded prefab: {inName}");
                return null;
            }

            return uiComponent;
        }

        private void OnClickSlot(SlotUI inClickedSlot, UnityEngine.EventSystems.PointerEventData inEventData)
        {
            if (inClickedSlot.SlotType == SlotUI.UISlotType.Inventory) // 인벤토리 클릭 - 장비 장착
            {
                PressRecord press = TakePressRecord(inEventData.button, inClickedSlot);
                if (press.IsCompare == true)
                    EquipFromComparison(inClickedSlot, press); // 비교 중: 누른 버튼의 화면 위치 슬롯
                else
                    EquipFromInventoryAuto(inClickedSlot);     // Shift 없는 클릭: 현행 자동 선택 (버튼 구분 없음)

                // 장착 후 포인터가 있는 칸의 현재 내용으로 다시 표시한다 (Shift 유지 중이면 비교, 칸이 비면 닫기)
                Input.ArpgInput input = AR.s.UI.Input;
                if (_hoveredSlot == inClickedSlot && IsHoverDisplayStale(input) == true)
                    RefreshHoverDisplay(input);
            }
            else if (inClickedSlot.SlotType == SlotUI.UISlotType.Equipment) // 장비 클릭 - 장비 해제
            {
                // 인벤토리에 여유 공간이 있는지 먼저 체크
                if (_inventoryUI!.HasEmptySlot() == false)
                {
                    Debug.Log("[UICharacter] OnClickSlot - Inventory full, cannot unequip item");
                    return;
                }

                SlotUI_Equip? equipSlot = inClickedSlot as SlotUI_Equip;
                if (equipSlot == null)
                {
                    Debug.LogError("[UICharacter] OnClickSlot - Clicked slot is not an equipment slot (should not happen)");
                    return;
                }

                if (_characterUI != null && _characterUI.UnequipItem(equipSlot.EquipSlotType, out var unequippedItem))
                {
                    if (unequippedItem != null)
                    {
                        // 해제된 아이템을 인벤토리에 추가
                        if (_inventoryUI.AddItem(unequippedItem) == false)
                        {
                            Debug.LogError("[UICharacter] OnClickSlot - Failed to add unequipped item to inventory (should not happen)");
                            // 여유 공간을 체크했으므로 이 상황은 발생하지 않아야 함
                            // 만약 발생하면 다시 장착
                            _characterUI.EquipItem((GlobalEnum.EquipSlotType)equipSlot.EquipSlotType, unequippedItem, out _);
                        }
                    }
                }
            }

            // Debug.Log($"[UICharacter] OnClickSlot - SlotType({inSlotType}), SlotIndex({inSlotIndex})");
        }

        private void EquipFromInventoryAuto(SlotUI inClickedSlot)
        {
            ItemData? itemData = _inventoryUI!.GetItemBySlotIndex(inClickedSlot.SlotIndex);
            if (itemData == null || itemData.Table == null || itemData.Equipment == null) // 아이템이 없거나 장비 아이템이 아닌 경우
                return;

            GlobalEnum.EquipSlotType targetSlot = EquipHelper.GetBestSlot(
                itemData.Equipment.EquipType,
                AR.s.Data.Player._inventoryEquip
            );

            EquipFromInventory(inClickedSlot, itemData, targetSlot);
        }

        /// <summary>
        /// 비교 중 클릭: 누른 순간 표시된 비교의 대상 슬롯에 장착한다.
        /// 장착 직전에 인벤토리 칸·ItemInstanceId·대상 칸 장착 가능 여부·현재 장착 아이템을 다시 확인하고,
        /// 그 사이 바뀌었으면 오래된 비교로 교체하지 않고 비교를 갱신한다 (다시 클릭하면 장착).
        /// </summary>
        private void EquipFromComparison(SlotUI inClickedSlot, in PressRecord inPress)
        {
            // 비교 중 휠 클릭은 장착하지 않는다
            if (inPress.HasTarget == false)
                return;
            if (_inventoryUI == null || _characterUI == null)
                return;

            ItemData? current = _inventoryUI.GetItemBySlotIndex(inClickedSlot.SlotIndex);
            ItemData? equipped = AR.s.Data.Player.GetEquipedItem(inPress.TargetSlot);
            bool isValid = current != null
                && current == inPress.Item
                && current.ItemInstanceId == inPress.ItemInstanceId
                && current.Equipment != null
                && _characterUI.CanEquip(inPress.TargetSlot, current) == true
                && equipped == inPress.TargetEquipped
                && (equipped == null || equipped.ItemInstanceId == inPress.TargetEquippedInstanceId);
            if (isValid == false)
            {
                Debug.Log($"[UICharacter] EquipFromComparison - Comparison target changed, refreshing, inSlotIndex({inClickedSlot.SlotIndex}), targetSlot({inPress.TargetSlot})");
                if (_hoveredSlot == inClickedSlot)
                    RefreshHoverDisplay(AR.s.UI.Input);
                return;
            }

            EquipFromInventory(inClickedSlot, current!, inPress.TargetSlot);
        }

        private void EquipFromInventory(SlotUI inClickedSlot, ItemData inItem, GlobalEnum.EquipSlotType inTargetSlot)
        {
            if (_characterUI != null && _characterUI.EquipItem(inTargetSlot, inItem, out var replacedItem))
            {
                if (replacedItem != null)
                {
                    // 기존 장착 아이템이 있었다면 인벤토리에 다시 넣기
                    if (_inventoryUI!.AddItem(replacedItem, inClickedSlot.SlotIndex, out _) == false)
                    {
                        Debug.LogWarning($"[UICharacter] OnClickSlot - Inventory full, cannot add replaced item, inSlotIndex({inClickedSlot.SlotIndex})");
                    }
                }
                else
                {
                    // 장착 성공했으니 인벤토리에서 아이템 제거
                    _inventoryUI!.RemoveItem(inClickedSlot.SlotIndex, 1);
                }
            }
        }

        // ========== 인벤토리 장비 호버·비교 ==========

        private void OnHoverInventorySlot(SlotUI inSlot)
        {
            _hoveredSlot = inSlot;
            _shownItem = null;
            RefreshHoverDisplay(AR.s.UI.Input);
        }

        private void OnExitInventorySlot(SlotUI inSlot)
        {
            if (_hoveredSlot != inSlot)
                return;

            ClearHover();
            AR.s.Tooltip.Hide();
        }

        private void OnPressInventorySlot(SlotUI inSlot, PointerEventData inEventData)
        {
            int buttonIndex = (int)inEventData.button;
            if (buttonIndex < 0 || buttonIndex >= _pressRecords.Length)
                return;

            // 같은 프레임에 Shift를 누른 경우까지 누르는 순간의 상태로 표시를 맞춘 뒤 모드를 기록한다
            Input.ArpgInput input = AR.s.UI.Input;
            if (_hoveredSlot == inSlot && IsHoverDisplayStale(input) == true)
                RefreshHoverDisplay(input);

            PressRecord record = new PressRecord { Slot = inSlot };
            if (_hoveredSlot == inSlot && _isComparing == true && _shownItem != null)
            {
                record.IsCompare = true;
                record.Item = _shownItem;
                record.ItemInstanceId = _shownItem.ItemInstanceId;
                if (inEventData.button == PointerEventData.InputButton.Left)
                {
                    record.HasTarget = true;
                    record.TargetSlot = _compareLeftClickSlot;
                    record.TargetEquipped = _compareLeftClickEquipped;
                }
                else if (inEventData.button == PointerEventData.InputButton.Right)
                {
                    record.HasTarget = true;
                    record.TargetSlot = _compareRightClickSlot;
                    record.TargetEquipped = _compareRightClickEquipped;
                }
                if (record.TargetEquipped != null)
                    record.TargetEquippedInstanceId = record.TargetEquipped.ItemInstanceId;
            }
            _pressRecords[buttonIndex] = record;
        }

        private PressRecord TakePressRecord(PointerEventData.InputButton inButton, SlotUI inSlot)
        {
            int index = (int)inButton;
            if (index < 0 || index >= _pressRecords.Length)
                return default;

            PressRecord record = _pressRecords[index];
            _pressRecords[index] = default;
            return record.Slot == inSlot ? record : default;
        }

        private void ClearPressRecords()
        {
            for (int i = 0; i < _pressRecords.Length; i++)
            {
                _pressRecords[i] = default;
            }
        }

        private void UpdateHoverDisplay(Input.ArpgInput inInput)
        {
            if (_hoveredSlot == null || _inventoryUI == null)
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
            if (_hoveredSlot == null || _inventoryUI == null)
                return false;

            ItemData? item = _inventoryUI.GetItemBySlotIndex(_hoveredSlot.SlotIndex);
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
            if (_hoveredSlot == null || _inventoryUI == null)
            {
                ClearHover();
                return;
            }

            ItemData? item = _inventoryUI.GetItemBySlotIndex(_hoveredSlot.SlotIndex);
            _shownItem = item;
            if (item == null || item.Table == null)
            {
                // 빈 칸에 장착해 칸이 비면 툴팁을 닫는다
                StopComparing();
                AR.s.Tooltip.Hide();
                return;
            }

            Rect anchor = _hoveredSlot.GetScreenRect();
            if (item.Table.ItemType != GlobalEnum.ItemType.Equipment || item.Equipment == null)
            {
                // 스킬북·스킬 페이지는 안내 없는 기존 단일 툴팁, 그 밖의 아이템은 툴팁 없음 (SlotUI 기본 동작과 같다)
                StopComparing();
                if (item.Table.ItemType == GlobalEnum.ItemType.Equipment
                    || item.Table.ItemType == GlobalEnum.ItemType.SkillBook
                    || item.Table.ItemType == GlobalEnum.ItemType.SkillPage)
                    AR.s.Tooltip.Show(item, anchor);
                else
                    AR.s.Tooltip.Hide();
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

            if (_characterUI != null)
                _characterUI.SetCompareHighlight(leftClickSlot, rightClickSlot);
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

        private void StopComparing()
        {
            if (_isComparing == true && _characterUI != null)
                _characterUI.ClearCompareHighlight();

            _isComparing = false;
            _compareLeftClickSlot = GlobalEnum.EquipSlotType.Max;
            _compareRightClickSlot = GlobalEnum.EquipSlotType.Max;
            _compareLeftClickEquipped = null;
            _compareRightClickEquipped = null;
        }

        private void ClearHover()
        {
            StopComparing();
            _hoveredSlot = null;
            _shownItem = null;
        }
    }
}
