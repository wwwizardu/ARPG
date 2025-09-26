#nullable enable
using System.Collections.Generic;
using ARPG;
using ARPG.Data;
using UnityEngine;

public class Inventory
{
    private int _maxSlotCount = 0;

    private List<ItemData?> _items = null!;

    public void Initialize(List<ItemData?> inItemList, int inSlotMaxCount)
    {
        _items = inItemList;
        _maxSlotCount = inSlotMaxCount;

        if (_items == null)
        {
            Debug.LogError("[Inventory] Initialize - inItemList is null");
        }
    }

    public bool AddItem(ItemData inItem)
    {
        if (inItem == null)
            return false;
        
        // 같은 ID의 아이템이 있는지 확인하여 수량 증가
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] != null && _items[i]!.Id == inItem.Id)
            {
                _items[i]!.Quantity += inItem.Quantity;
                return true;
            }
        }

        // 같은 아이템이 없으면 빈 슬롯 찾기
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] == null)
            {
                _items[i] = inItem;
                return true;
            }
        }

        // 빈 슬롯이 없음
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

        // 해당 슬롯에 같은 ID의 아이템이 있으면 수량 증가
        if (_items[slotIndex] != null && _items[slotIndex]!.Id == inItem.Id)
        {
            _items[slotIndex]!.Quantity += inItem.Quantity;
            return true;
        }

        // 해당 슬롯에 다른 아이템이 있으면 교체
        if (_items[slotIndex] != null)
        {
            replacedItem = _items[slotIndex];
        }

        _items[slotIndex] = inItem;
        return true;
    }

    public bool RemoveItem(ItemData inItem)
    {
        if (inItem == null)
            return false;

        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] == inItem)
            {
                _items[i] = null;
                return true;
            }
        }

        return false;
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
