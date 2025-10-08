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
        private List<Data.ItemData?> _items = null!;

        public async Task Initialize(string inName, List<Data.ItemData?> inInventory, int inSlotMaxCount, Action<SlotUI, PointerEventData> onClickSlot)
        {
            SlotCount = inSlotMaxCount;
            base.Initialize(inName, false);

            _items = inInventory;
            if(_items == null)
            {
                Debug.LogError("[UIInventory] Player inventory is null");
                return;
            }

            var loadResult = await LoadSlots(onClickSlot);
            if (loadResult == false)
            {
                Debug.LogError("[UIInventory] Failed to load slots");
                return;
            }
        }

        public void OnLoadCompleted()
        {
            if (_slotUIs == null || _items == null)
            {
                Debug.LogError("[UIInventory] OnLoadCompleted - _slotUIs or _items is null");
                return;
            }

            for (int i = 0; i < _slotUIs.Length && i < _items.Count; i++)
            {
                if (_slotUIs[i] != null && _items[i] != null)
                {
                    _slotUIs[i]!.SetItem(_items[i]!);
                }
            }
        }

        public override void Initialize(string inName, bool isForm = false)
        {
            Debug.LogError("[UIInventory] Initialize called, use Initialize(string inName, int inSlotMaxCount)");
        }

        public bool AddItem(Data.ItemData inItem)
        {
            if (inItem == null)
                return false;

            // 같은 ID의 아이템이 있는지 확인하여 수량 증가
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null && _items[i]!.Id == inItem.Id)
                {
                    _items[i]!.Quantity += inItem.Quantity;
                    _slotUIs?[i]?.Refresh();
                    return true;
                }
            }

            // 같은 아이템이 없으면 빈 슬롯 찾기
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
                {
                    _items[i] = inItem;
                    _slotUIs?[i]?.SetItem(inItem);
                    return true;
                }
            }

            // 빈 슬롯이 없음
            return false;
        }

        public bool AddItem(Data.ItemData inItem, int slotIndex, out Data.ItemData? replacedItem)
        {
            replacedItem = null;

            if (inItem == null)
                return false;

            // 인덱스 유효성 검사
            if (slotIndex < 0 || slotIndex >= _items.Count)
                return false;

            // 해당 슬롯에 같은 ID의 아이템이 있으면 수량 증가
            if (_items[slotIndex] != null && _items[slotIndex]!.Id == inItem.Id)
            {
                _items[slotIndex]!.Quantity += inItem.Quantity;
                _slotUIs?[slotIndex]?.Refresh();
                return true;
            }

            // 해당 슬롯에 다른 아이템이 있으면 교체
            if (_items[slotIndex] != null)
            {
                replacedItem = _items[slotIndex];
            }

            _items[slotIndex] = inItem;
            _slotUIs?[slotIndex]?.SetItem(inItem);
            return true;
        }

        public bool RemoveItem(Data.ItemData inItem)
        {
            if (inItem == null)
                return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == inItem)
                {
                    _items[i] = null;
                    _slotUIs?[i]?.Reset();
                    return true;
                }
            }

            return false;
        }

        public bool RemoveItem(int inSlotIndex)
        {
            // 인덱스 유효성 검사
            if (inSlotIndex < 0 || inSlotIndex >= _items.Count)
                return false;

            if (_items[inSlotIndex] == null)
                return false;

            _items[inSlotIndex] = null;
            _slotUIs?[inSlotIndex]?.Reset();
            return true;
        }

        public bool MoveItem(int inFromIndex, int inToIndex)
        {
            // 인덱스 유효성 검사
            if (inFromIndex < 0 || inFromIndex >= _items.Count ||
                inToIndex < 0 || inToIndex >= _items.Count)
            {
                return false;
            }

            // 같은 위치로 이동하는 경우
            if (inFromIndex == inToIndex)
                return true;

            // 아이템 위치 교환 (빈 슬롯이어도 교환 가능)
            Data.ItemData? fromItem = _items[inFromIndex];
            Data.ItemData? toItem = _items[inToIndex];

            _items[inFromIndex] = toItem;
            _items[inToIndex] = fromItem;

            return true;
        }

        public List<Data.ItemData?> GetItems()
        {
            return _items;
        }

        public Data.ItemData? GetItemBySlotIndex(int inIndex)
        {
            if (inIndex < 0 || inIndex >= _items.Count)
                return null;

            return _items[inIndex];
        }

        public bool HasEmptySlot()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
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


