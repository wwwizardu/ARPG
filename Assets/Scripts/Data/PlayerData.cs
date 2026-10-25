#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using ARPG.Utility;

namespace ARPG.Data
{
    [Serializable]
    public class PlayerData
    {
        // V1 → V2: _skillBookSlots 배열 추가 (SKILLBOOK_DESIGN.md §3.4)
        public ushort Version = 2;

        public int PlayerId; // 고유 플레이어 ID
        public int PlayerTableId;
        public int Level;
        public int Exp;     // 현재 레벨 안에서 모은 경험치 (다음 레벨 필요량 R(L)에 닿으면 레벨업, PLAYER_LEVEL_CHARM_DESIGN §3.4)
        public int Gold;

        // 레벨업으로 받은 능력치 포인트 (PLAYER_LEVEL_CHARM_DESIGN §4.2). 넣은 포인트는 StatTable 기본 능력치 위에 더한다.
        // 칸이 없는 옛 세이브는 0이다. 넣은 포인트 + 남은 포인트 = (레벨 − 1) × 5 (LoadCompleted가 맞춘다)
        public int AllocatedStr;
        public int AllocatedDex;
        public int AllocatedInt;
        public int UnspentAttributePoints;

        public float PositionX;
        public float PositionZ;
        public Map.MapKind CurrentMapKind;
        public string ActiveDungeonId = string.Empty;
        public float DungeonPositionX;
        public float DungeonPositionY;
        public float WorldReturnX;
        public float WorldReturnY;

        public int CurrentHp;
        public int CurrentMp;

        // 장착한 아이템
        public ItemData?[] _inventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        // 인벤토리 아이템
        public List<ItemData?> _inventory = new List<ItemData?>();
        public int[] _quickItemIds = new int[4];

        // 스킬북 슬롯 (SKILLBOOK_DESIGN.md §3.4) — 슬롯 0=좌클릭, 1=Space, 2~9=숫자키 1~8
        public ItemData?[] _skillBookSlots = new ItemData?[GlobalEnum.PLAYER_SKILL_SLOT_COUNT];

        // 참 판 자리 9칸 (PLAYER_LEVEL_CHARM_DESIGN §6.1). 3×3 왼쪽 위부터 행 순서, 열리는 레벨은 PlayerLevelRules.
        // 칸이 없는 옛 세이브는 빈 9칸으로 시작한다
        public ItemData?[] _charmBoardSlots = new ItemData?[PlayerLevelRules.CHARM_BOARD_SLOT_COUNT];

        public void Initialize(int inPlayerId = 0)
        {
            PlayerId = inPlayerId;
            PlayerTableId = 1;
            Level = 1;
            Exp = 0;
            Gold = 0;
            AllocatedStr = 0;
            AllocatedDex = 0;
            AllocatedInt = 0;
            UnspentAttributePoints = 0;

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

            // 스킬북 슬롯 초기화 + 슬롯 0에 기본 책 시드
            for (int i = 0; i < _skillBookSlots.Length; i++)
            {
                _skillBookSlots[i] = null;
            }
            SeedDefaultStarterSkillBook();
            SeedDefaultStarterWeapon();
            _quickItemIds = new int[4];
            for (int i = 0; i < 2; i++)
            {
                ItemData? potion = AR.s.Item.CreateInventoryItemData(9000 + i, 5);
                if (potion == null) continue;
                _inventory[i] = potion;
                _quickItemIds[i] = potion.Id;
            }

            _charmBoardSlots = new ItemData?[PlayerLevelRules.CHARM_BOARD_SLOT_COUNT];
        }

        public void LoadCompleted()
        {
            if (_quickItemIds == null || _quickItemIds.Length != 4)
            {
                int[] previous = _quickItemIds ?? Array.Empty<int>();
                _quickItemIds = new int[4];
                Array.Copy(previous, _quickItemIds, Math.Min(previous.Length, 4));
            }
            for (int i = 0; i < 4; i++)
                if (AR.s.Data.GetItem(_quickItemIds[i])?.ItemType != GlobalEnum.ItemType.Consumable) _quickItemIds[i] = 0;
            NormalizeProgression();

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

            // 스킬북 슬롯 테이블 세팅
            for (int i = 0; i < _skillBookSlots.Length; i++)
            {
                _skillBookSlots[i]?.OnLoadCompleted();
            }

            // 참 판 자리 (옛 세이브는 null 또는 길이가 다를 수 있다)
            if (_charmBoardSlots == null || _charmBoardSlots.Length != PlayerLevelRules.CHARM_BOARD_SLOT_COUNT)
            {
                ItemData?[] loaded = _charmBoardSlots ?? System.Array.Empty<ItemData?>();
                _charmBoardSlots = new ItemData?[PlayerLevelRules.CHARM_BOARD_SLOT_COUNT];
                for (int i = 0; i < loaded.Length && i < _charmBoardSlots.Length; i++)
                    _charmBoardSlots[i] = loaded[i];
            }
            for (int i = 0; i < _charmBoardSlots.Length; i++)
            {
                _charmBoardSlots[i]?.OnLoadCompleted();
            }
        }

        /// <summary>
        /// 레벨·경험치·포인트 칸을 규칙 범위로 맞춘다. 옛 세이브(Level 1, 포인트 칸 없음)는 그대로 1레벨·포인트 0이다.
        /// 넣은 포인트가 레벨이 준 총합보다 많으면(손상) 모두 되돌려 남은 포인트로 돌린다.
        /// </summary>
        private void NormalizeProgression()
        {
            Level = PlayerLevelRules.ClampLevel(Level);
            if (Level >= PlayerLevelRules.MAX_LEVEL || Exp < 0)
                Exp = 0;

            AllocatedStr = Math.Max(0, AllocatedStr);
            AllocatedDex = Math.Max(0, AllocatedDex);
            AllocatedInt = Math.Max(0, AllocatedInt);
            int total = PlayerLevelRules.GetTotalAttributePoints(Level);
            int allocated = AllocatedStr + AllocatedDex + AllocatedInt;
            if (allocated > total)
            {
                AllocatedStr = 0;
                AllocatedDex = 0;
                AllocatedInt = 0;
                allocated = 0;
            }
            UnspentAttributePoints = total - allocated;
        }

        /// <summary>
        /// 신규 플레이어에게 기본 무기(낡은 단검) 우측 슬롯에 장착 시드.
        /// Prefix/Postfix 없는 깔끔한 베이스 무기. 시드 실패해도 게임 진행은 막지 않음.
        /// </summary>
        public void SeedDefaultStarterWeapon()
        {
            if (AR.s?.Item == null)
            {
                UnityEngine.Debug.LogWarning("[PlayerData] SeedDefaultStarterWeapon - ItemManager not ready, skipping seed");
                return;
            }

            ItemData? weapon = AR.s.Item.CreateBaseEquipment(GlobalEnum.DEFAULT_STARTER_WEAPON_ITEM_ID);
            if (weapon != null)
            {
                _inventoryEquip[(int)GlobalEnum.EquipSlotType.WeaponRight] = weapon;
            }
        }

        /// <summary>
        /// 신규 플레이어/V1→V2 마이그레이션 시 슬롯 0에 기본 스킬북(Common 등급 + Strike) 시드.
        /// 시드 실패해도 게임 진행은 막지 않음 (테이블 미로드/책 ItemId 부재 등).
        /// </summary>
        public void SeedDefaultStarterSkillBook()
        {
            if (AR.s?.Item == null)
            {
                UnityEngine.Debug.LogWarning("[PlayerData] SeedDefaultStarterSkillBook - ItemManager not ready, skipping seed");
                return;
            }

            ItemData? defaultBook = AR.s.Item.CreateSkillBook(
                GlobalEnum.DEFAULT_SKILLBOOK_ITEM_ID_COMMON,
                GlobalEnum.DEFAULT_STARTER_SKILL_ID);
            if (defaultBook != null)
            {
                _skillBookSlots[0] = defaultBook;
            }
        }

        public bool EquipItem(GlobalEnum.EquipSlotType inEquipType, ItemData inItem, out ItemData? replacedItem)
        {
            replacedItem = null;

            if (inItem == null)
                return false;

            if (inItem?.Equipment == null)
                return false;

            GlobalEnum.EquipmentTypeMask allowedMask = EquipHelper.SlotToEquipmentTypeMask(inEquipType);
            if (Utils.ContainsEquipmentType(allowedMask, inItem.Equipment.EquipType) == false)
                return false;

            // 기존에 장착된 아이템이 있는 경우 교체할 아이템으로 설정
            if (_inventoryEquip[(int)inEquipType] != null)
            {
                replacedItem = _inventoryEquip[(int)inEquipType];
            }

            // 아이템 장착
            AR.s?.Data?.MarkPlayerSaveDirty(this, PlayerSaveGroup.Equipment);
            _inventoryEquip[(int)inEquipType] = inItem;
            
            return true;
        }

        public bool UnequipItem(GlobalEnum.EquipSlotType inEquipType, out Data.ItemData? unequippedItem)
        {
            unequippedItem = null;
            if (_inventoryEquip[(int)inEquipType] == null)
                return false;

            unequippedItem = _inventoryEquip[(int)inEquipType];
            AR.s?.Data?.MarkPlayerSaveDirty(this, PlayerSaveGroup.Equipment);
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


