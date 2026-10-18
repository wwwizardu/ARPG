#nullable enable
using System.Collections.Generic;
using ARPG;
using ARPG.Data;
using ARPG.UI;
using UnityEngine;

namespace ARPG.Item
{
    public class Inventory
    {
        private System.Action? _changed;
        public System.Action? Changed { get => _changed; set => _changed = value; }

        private int _maxSlotCount = 0;

        private List<ItemData?> _items = null!;

        public List<ItemData?> Items => _items;

        public void Initialize(List<ItemData?> inItemData, int inMaxSlotCount)
        {
            _items = inItemData;
            _maxSlotCount = inMaxSlotCount;

            if (_items == null)
            {
                Debug.LogError("[Inventory] Initialize - inItemData is null");
                return;
            }

            if (inItemData.Count != inMaxSlotCount)
            {
                Debug.LogWarning($"[Inventory] Initialize - inItemData count({inItemData.Count}) != inMaxSlotCount({inMaxSlotCount})");
            }
        }

        public int AddItem(ItemData inItem) // 추가된 아이템의 슬롯 인덱스 반환, 실패 시 -1
        {
            if (inItem == null)
                return -1;

            // Stackable이 true일 경우에만 같은 ID의 아이템을 찾아 수량 증가
            if (inItem.Table != null && inItem.Table.Stackable)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i] != null && _items[i]!.Id == inItem.Id)
                    {
                        _changed?.Invoke();
                        _items[i]!.Quantity += inItem.Quantity;
                        return i;
                    }
                }
            }

            // Stackable이 false이거나 같은 아이템이 없으면 빈 슬롯 찾기
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
                {
                    _changed?.Invoke();
                    _items[i] = inItem;
                    return i;
                }
            }

            // 빈 슬롯이 없음
            return -1;
        }

        /// <summary>AddItem(inItem)이 성공할지 미리 확인한다 (같은 규칙: Stackable이면 같은 ID 슬롯에 합산, 아니면 빈 슬롯).</summary>
        public bool CanAddItem(ItemData inItem)
        {
            if (inItem == null)
                return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null)
                    return true;
                if (inItem.Table != null && inItem.Table.Stackable && _items[i]!.Id == inItem.Id)
                    return true;
            }
            return false;
        }

        public bool AddItem(ItemData inItem, int slotIndex, out ItemData? replacedItem)
        {
            replacedItem = null;

            if (inItem == null)
                return false;

            // 인덱스 유효성 검사
            if (slotIndex < 0 || slotIndex >= _items.Count)
                return false;

            // Stackable이고 해당 슬롯에 같은 ID의 아이템이 있으면 수량 증가 (장비는 같은 ID여도 교체)
            if (inItem.Table != null && inItem.Table.Stackable && _items[slotIndex] != null && _items[slotIndex]!.Id == inItem.Id)
            {
                _changed?.Invoke();
                _items[slotIndex]!.Quantity += inItem.Quantity;
                return true;
            }

            // 해당 슬롯에 다른 아이템이 있으면 교체
            if (_items[slotIndex] != null)
            {
                replacedItem = _items[slotIndex];
            }
            _changed?.Invoke();
            _items[slotIndex] = inItem;
            return true;
        }

        // public int RemoveItem(ItemData inItem, int inQuantity, out ItemData? outRemainItem) // 제거된 아이템의 슬롯 인덱스 반환, 실패 시 -1
        // {
        //     outRemainItem = null;

        //     if (inItem == null)
        //         return -1;

        //     for (int i = 0; i < _items.Count; i++)
        //     {
        //         if (_items[i] == inItem)
        //         {
        //             // 제거할 수량이 현재 수량보다 많으면 에러
        //             if (_items[i]!.Quantity < inQuantity)
        //             {
        //                 Debug.LogError($"[Inventory] RemoveItem - Not enough quantity. Current: {_items[i]!.Quantity}, Requested: {inQuantity}");
        //                 return -1;
        //             }

        //             // 제거할 수량이 현재 수량과 같으면 슬롯 비우기
        //             if (_items[i]!.Quantity == inQuantity)
        //             {
        //                 _items[i] = null;
        //                 return i;
        //             }
        //             else
        //             {
        //                 // 수량만 감소
        //                 _items[i]!.Quantity -= inQuantity;
        //                 outRemainItem = _items[i];
        //                 return i;
        //             }
        //         }
        //     }

        //     return -1;
        // }

        public bool RemoveItem(int inSlotIndex, int inQuantity, out ItemData? outRemainItem)
        {
            outRemainItem = null;

            // 인덱스 유효성 검사
            if (inSlotIndex < 0 || inSlotIndex >= _items.Count)
                return false;
            if (_items[inSlotIndex] == null)
                return false;

            // 제거할 수량이 현재 수량보다 많으면 에러
            if (_items[inSlotIndex]!.Quantity < inQuantity)
            {
                Debug.LogError($"[Inventory] RemoveItem - Not enough quantity. Current: {_items[inSlotIndex]!.Quantity}, Requested: {inQuantity}");
                return false;
            }

            // 제거할 수량이 현재 수량과 같으면 슬롯 비우기
            if (_items[inSlotIndex]!.Quantity == inQuantity)
            {
                _changed?.Invoke();
                _items[inSlotIndex] = null;
                return true;
            }
            else
            {
                // 수량만 감소
                _changed?.Invoke();
                _items[inSlotIndex]!.Quantity -= inQuantity;
                outRemainItem = _items[inSlotIndex];
                return true;
            }
        }

        /// <summary>
        /// RemoveItem으로 꺼낸 아이템을 원래 수량으로 되돌린다 (저장 실패 롤백). 같은 아이템이 아직 슬롯에 있으면 그 슬롯에서,
        /// 원래 슬롯이 비었으면 그 슬롯에, 그 사이 다른 아이템이 들어왔으면 빈 슬롯에 넣는다. 넣을 곳이 없으면 false.
        /// </summary>
        public bool RestoreItem(int slotIndex, ItemData item, int quantity)
        {
            if (item == null || quantity <= 0)
                return false;
            int current = _items.IndexOf(item);
            if (current < 0 && slotIndex >= 0 && slotIndex < _items.Count && _items[slotIndex] == null)
                current = slotIndex;
            if (current >= 0)
            {
                _changed?.Invoke();
                item.Quantity = quantity;
                _items[current] = item;
                return true;
            }
            item.Quantity = quantity;
            return AddItem(item) >= 0;
        }

        /// <summary>
        /// 인벤토리 전체 스택에서 ItemId의 보유 수량을 합산한다.
        /// </summary>
        public int GetTotalQuantity(int itemId)
        {
            if (itemId <= 0 || _items == null)
                return 0;

            int total = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                ItemData? item = _items[i];
                if (item == null || item.Id != itemId || item.Quantity <= 0)
                    continue;
                total += item.Quantity;
            }
            return total;
        }

        public bool CanConsumeItem(int itemId, int quantity)
        {
            return itemId > 0 && quantity > 0 && GetTotalQuantity(itemId) >= quantity;
        }

        /// <summary>
        /// 전체 수량을 먼저 검증한 다음 낮은 슬롯 인덱스부터 결정론적으로 소비한다.
        /// 수량이 부족하면 어떤 스택도 변경하지 않는다.
        /// </summary>
        public bool TryConsumeItem(int itemId, int quantity)
        {
            if (CanConsumeItem(itemId, quantity) == false)
                return false;

            int remaining = quantity;
            for (int i = 0; i < _items.Count && remaining > 0; i++)
            {
                ItemData? item = _items[i];
                if (item == null || item.Id != itemId || item.Quantity <= 0)
                    continue;

                int consume = Mathf.Min(item.Quantity, remaining);
                _changed?.Invoke();
                item.Quantity -= consume;
                remaining -= consume;
                if (item.Quantity == 0)
                    _items[i] = null;
            }

            return remaining == 0;
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
            ItemData? fromItem = _items[inFromIndex];
            ItemData? toItem = _items[inToIndex];
            _changed?.Invoke();
            _items[inFromIndex] = toItem;
            _items[inToIndex] = fromItem;

            return true;
        }

        public List<ItemData?> GetItems()
        {
            return _items;
        }

        public ItemData? GetItemBySlotIndex(int inIndex)
        {
            if (inIndex < 0 || inIndex >= _items.Count)
                return null;

            return _items[inIndex];
        }
    }
}


