#nullable enable
using ARPG.Base;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using ARPG.Data;

namespace ARPG.UI
{
    public class UICharacter : UIBaseForm
    {
        [SerializeField] private Transform _characterRoot;
        [SerializeField] private Transform _inventoryRoot;

        private UIInventory? _inventoryUI;
        private CharacterUI? _characterUI;
        private bool _loadCompleted = false;

        public bool LoadCompleted => _loadCompleted;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            LoadUI();
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

            await _inventoryUI!.Initialize(AddressablePath.Inventory, AR.s.Data.Player._inventory, AR.s.Data.Player._inventory.Count, OnClickSlot); // 슬롯 40개로 초기화
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

        private void OnClickSlot(SlotUI.UISlotType inSlotType, int inSlotIndex, UnityEngine.EventSystems.PointerEventData inEventData)
        {
            if (inSlotType == SlotUI.UISlotType.Inventory) // 인벤토리 클릭 - 장비 장착
            {
                ItemData? itemData = _inventoryUI!.GetItemBySlotIndex(inSlotIndex);
                if (itemData == null || itemData.Table == null || itemData.Equipment == null) // 아이템이 없거나 장비 아이템이 아닌 경우
                    return;

                if (_characterUI != null && _characterUI.EquipItem(GlobalEnum.EquipSlotType.WeaponLeft, itemData, out var replacedItem))
                {
                    if (replacedItem != null)
                    {
                        // 기존 장착 아이템이 있었다면 인벤토리에 다시 넣기
                        if (_inventoryUI.AddItem(replacedItem, inSlotIndex, out _) == false)
                        {
                            Debug.LogWarning($"[UICharacter] OnClickSlot - Inventory full, cannot add replaced item, inSlotIndex({inSlotIndex})");
                        }
                    }
                    else
                    {
                        // 장착 성공했으니 인벤토리에서 아이템 제거
                        _inventoryUI.RemoveItem(inSlotIndex);
                    }
                }
            }
            else if (inSlotType == SlotUI.UISlotType.Equipment) // 장비 클릭 - 장비 해제
            {
                // 인벤토리에 여유 공간이 있는지 먼저 체크
                if (_inventoryUI!.HasEmptySlot() == false)
                {
                    Debug.Log("[UICharacter] OnClickSlot - Inventory full, cannot unequip item");
                    return;
                }

                if (_characterUI != null && _characterUI.UnequipItem(inSlotIndex, out var unequippedItem))
                {
                    if (unequippedItem != null)
                    {
                        // 해제된 아이템을 인벤토리에 추가
                        if (_inventoryUI.AddItem(unequippedItem) == false)
                        {
                            Debug.LogError("[UICharacter] OnClickSlot - Failed to add unequipped item to inventory (should not happen)");
                            // 여유 공간을 체크했으므로 이 상황은 발생하지 않아야 함
                            // 만약 발생하면 다시 장착
                            _characterUI.EquipItem((GlobalEnum.EquipSlotType)inSlotIndex, unequippedItem, out _);
                        }
                    }
                }
            }

            Debug.Log($"[UICharacter] OnClickSlot - SlotType({inSlotType}), SlotIndex({inSlotIndex})");
        }
    }
}


