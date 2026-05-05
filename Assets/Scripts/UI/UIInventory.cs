#nullable enable
using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System;

namespace ARPG.UI
{
    public class UIInventory : Base.UIBase
    {
        [SerializeField] int SlotCount = 20;
        [SerializeField] Transform _slotRoot;

        private SlotUI? []? _slotUIs = null;
        private Item.Inventory _inventory = null!;

        public async Task Initialize(string inName, Item.Inventory inInventory, Action<SlotUI, PointerEventData> onClickSlot)
        {
            base.Initialize(inName, false);

            _inventory = inInventory;
            SlotCount = inInventory.Items.Count;

            var loadResult = await LoadSlots(onClickSlot);
            if (loadResult == false)
            {
                Debug.LogError("[UIInventory] Failed to load slots");
                return;
            }
        }

        public void OnLoadCompleted()
        {
            if (_slotUIs == null || _inventory.Items == null)
            {
                Debug.LogError("[UIInventory] OnLoadCompleted - _slotUIs or _items is null");
                return;
            }

            RefreshAll();
        }

        // 인벤토리 데이터(_inventory.Items)와 SlotUI 캐시를 강제 동기화.
        // 빈 슬롯은 Reset() 처리하므로 외부에서 데이터만 변경된 경우(예: PlayerSkillManager.EquipSkillBook)에도 UI가 따라간다.
        public void RefreshAll()
        {
            if (_slotUIs == null || _inventory == null || _inventory.Items == null) return;

            for (int i = 0; i < _slotUIs.Length && i < _inventory.Items.Count; i++)
            {
                SlotUI? slot = _slotUIs[i];
                if (slot == null) continue;

                Data.ItemData? item = _inventory.Items[i];
                if (item != null) slot.SetItem(item);
                else slot.Reset();
            }
        }

        public override void Initialize(string inName, bool isForm = false)
        {
            Debug.LogError("[UIInventory] Initialize called, use Initialize(string inName, int inSlotMaxCount)");
        }

        public bool AddItem(Data.ItemData inItem)
        {
            int slotIndex = _inventory.AddItem(inItem);
            if (slotIndex == -1)
                return false;

            _slotUIs?[slotIndex]?.SetItem(inItem);
            return true;
        }

        public bool AddItem(Data.ItemData inItem, int slotIndex, out Data.ItemData? replacedItem)
        {
            if(_inventory.AddItem(inItem, slotIndex, out replacedItem) == false)
                return false;

            _slotUIs?[slotIndex]?.SetItem(inItem);
            return true;
        }

        // public bool RemoveItem(Data.ItemData inItem, int inQuantity)
        // {
        //     int slotIndex = _inventory.RemoveItem(inItem, inQuantity, out var removedItem);
        //     if (slotIndex == -1)
        //         return false;

        //     if (removedItem == null)
        //     {
        //         _slotUIs?[slotIndex]?.Reset();    
        //     }
        //     else
        //     {
        //         _slotUIs?[slotIndex]?.SetItem(removedItem);
        //     }

        //     return true;
        // }

        public bool RemoveItem(int inSlotIndex, int inQuantity)
        {
            if (_inventory.RemoveItem(inSlotIndex, inQuantity, out var remainItem) == false)
                return false;

            if (remainItem == null)
            {
                _slotUIs?[inSlotIndex]?.Reset();
            }
            else
            {
                _slotUIs?[inSlotIndex]?.Refresh();
            }

            return true;
        }

        public bool MoveItem(int inFromIndex, int inToIndex)
        {
            return _inventory.MoveItem(inFromIndex, inToIndex);
        }

        public List<Data.ItemData?> GetItems()
        {
            return _inventory.GetItems();
        }

        public Data.ItemData? GetItemBySlotIndex(int inIndex)
        {
            return _inventory.GetItemBySlotIndex(inIndex);
        }

        public bool HasEmptySlot()
        {
            for (int i = 0; i < _inventory.Items.Count; i++)
            {
                if (_inventory.Items[i] == null)
                    return true;
            }
            return false;
        }
        
        private async Task<bool> LoadSlots(Action<SlotUI, PointerEventData> onClickSlot)
        {
            try
            {
                _slotUIs = new SlotUI?[SlotCount];

                for (int i = 0; i < SlotCount; i++)
                {
                    var handle = Addressables.InstantiateAsync(AddressablePath.SlotUI, _slotRoot);
                    var slotObject = await handle.Task;

                    if (slotObject == null)
                    {
                        Debug.LogError($"[UIInventory] Failed to load slot {i}");
                        return false;
                    }

                    SlotUI slotUI = slotObject.GetComponent<SlotUI>();
                    if (slotUI == null)
                    {
                        Debug.LogError($"[UIInventory] Slot prefab does not contain SlotUI component at index {i}");
                        return false;
                    }
                    
                    slotUI.Initialize(i, onClickSlot);                    
                    _slotUIs[i] = slotUI;
                }

                Debug.Log($"[UIInventory] Successfully loaded {SlotCount} slots");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UIInventory] Failed to load slots: {ex.Message}");
                return false;
            }
        }

    }
}


