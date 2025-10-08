#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ARPG.Data
{
    [Serializable]
    public class PlayerData
    {
        public ushort Version = 1;
        public int Level;
        public int Exp;
        public int Gold;

        public float PositionX;
        public float PositionZ;

        public int MaxInventorySlotCount = 40;

        // 장착한 아이템
        public ItemData?[] _inventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        // 인벤토리 아이템
        public List<ItemData?> _inventory = new List<ItemData?>();

        public void Initialize(int inInventorySlotMaxCount)
        {
            Level = 1;
            Exp = 0;
            Gold = 0;

            PositionX = 0;
            PositionZ = 0;

            MaxInventorySlotCount = inInventorySlotMaxCount;

            for (int i = 0; i < (int)GlobalEnum.EquipSlotType.Max; i++)
            {
                _inventoryEquip[i] = null;
            }

            _inventory.Clear();
            for (int i = 0; i < inInventorySlotMaxCount; i++)
            {
                _inventory.Add(null);
            }
        }

        public bool EquipItem(GlobalEnum.EquipSlotType inEquipType, ItemData inItem, out ItemData? replacedItem)
        {
            replacedItem = null;

            if (inItem == null)
                return false;

            if (inItem?.Equipment?.Table == null )
                return false;

            if (inItem.Equipment.Table.EquipType != inEquipType)
                return false;

            // 기존에 장착된 아이템이 있는 경우 교체할 아이템으로 설정
            if (_inventoryEquip[(int)inEquipType] != null)
            {
                replacedItem = _inventoryEquip[(int)inEquipType];
            }

            // 아이템 장착
            _inventoryEquip[(int)inEquipType] = inItem;

            return true;
        }

        public bool UnequipItem(GlobalEnum.EquipSlotType inEquipType, out Data.ItemData? unequippedItem)
        {
            unequippedItem = null;
            if (_inventoryEquip[(int)inEquipType] == null)
                return false;

            unequippedItem = _inventoryEquip[(int)inEquipType];
            _inventoryEquip[(int)inEquipType] = null;

            return true;
        }

        public bool Load()
        {
            // 로드 로직
            return true;
        }

        public bool Save()
        {
            // 저장 로직
            return true;
        }
    } 
}


