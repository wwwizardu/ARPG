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

        public ItemData? [] _inventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        public List<ItemData?> _inventory = new List<ItemData?>();

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


