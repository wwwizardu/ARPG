#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using ARPG.Tables;
using ARPG.Utility;
using UnityEditorInternal.Profiling.Memory.Experimental;

namespace ARPG.Data
{
    [Serializable]
    public class PlayerData
    {
        
        public ushort Version = 1;

        public int PlayerId; // 고유 플레이어 ID
        public int PlayerTableId;
        public int Level;
        public int Exp;
        public int Gold;

        public float PositionX;
        public float PositionZ;

        public int CurrentHp;
        public int CurrentMp;

        // 장착한 아이템
        public ItemData?[] _inventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        // 인벤토리 아이템
        public List<ItemData?> _inventory = new List<ItemData?>();

        public void Initialize(int inPlayerId = 0)
        {
            PlayerId = inPlayerId;
            PlayerTableId = 1;
            Level = 1;
            Exp = 0;
            Gold = 0;

            PositionX = 0;
            PositionZ = 0;

            CreatureTable? playerTable = AR.s.Data.GetPlayer(PlayerTableId);
            if(playerTable != null)
            {
                CurrentHp = playerTable.Stat.MaxHp;
                CurrentMp = playerTable.Stat.MaxMp;
            }
            else
            {
                CurrentHp = 100;
                CurrentMp = 100;
            }

            for (int i = 0; i < (int)GlobalEnum.EquipSlotType.Max; i++)
            {
                _inventoryEquip[i] = null;
            }

            _inventory.Clear();
            for (int i = 0; i < GlobalEnum.PLAYER_INVENTORY_SLOTCOUNT_MAX; i++)
            {
                _inventory.Add(null);
            }
        }

        public void LoadCompleted()
        {
            // 장착 아이템 테이블 세팅
            for (int i = 0; i < _inventoryEquip.Length; i++)
            {
                _inventoryEquip[i]?.OnLoadCompleted();
            }

            // 인벤토리 아이템 테이블 세팅
            for (int i = 0; i < _inventory.Count; i++)
            {
                _inventory[i]?.OnLoadCompleted();
            }
        }

        public bool EquipItem(GlobalEnum.EquipSlotType inEquipType, ItemData inItem, out ItemData? replacedItem)
        {
            replacedItem = null;

            if (inItem == null)
                return false;

            if (inItem?.Equipment?.Table == null)
                return false;

            if (inItem.Equipment.Table.EquipType != EquipHelper.SlotToEquipmentType(inEquipType))
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

        public ItemData? GetEquipedItem(GlobalEnum.EquipSlotType inEquipType)
        {
            return _inventoryEquip[(int)inEquipType];
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


