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


