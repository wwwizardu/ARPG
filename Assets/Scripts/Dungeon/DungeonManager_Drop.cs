using ARPG.Data;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Dungeon
{
    /// <summary>
    /// 던전 드롭 (ITEM_DROP_BALANCE_DESIGN.md §7.6): 장소 보너스 조회와 상자 드롭 판정.
    /// 몬스터·보스 드롭은 필드와 같은 사망 경로(System_HpCheck → DropHelper)를 쓴다. 완료 처리는 바꾸지 않는다.
    /// </summary>
    public partial class DungeonManager
    {
        /// <summary>
        /// 플레이어가 던전 안이면 그 던전 표의 장소 보너스(수량·희귀도 %)를 돌려준다. 드롭 계산부가 ④ 장소 묶음으로 더한다.
        /// 던전 밖이거나 표를 찾지 못하면 false.
        /// </summary>
        public bool TryGetPlayerLocationDropBonus(out int quantityPercent, out int rarityPercent)
        {
            quantityPercent = 0;
            rarityPercent = 0;
            if (IsInside == false || _active == null)
                return false;

            Tables.DungeonTable table = AR.s.Data.GetDungeonTable(_active.DungeonTableId);
            if (table == null)
                return false;

            quantityPercent = table.DropQuantityBonus;
            rarityPercent = table.DropRarityBonus;
            return true;
        }

        /// <summary>
        /// 상자를 열 때 공통 드롭 판정을 굴린다. 몬스터 묶음 없이 플레이어·장소 묶음만 적용하고(DropBonusResolver.ResolveForChest),
        /// 아이템 레벨은 그 던전 일반 몬스터의 레벨이다. 상자 장비는 T11·T12 특수 판정을 하지 않는다.
        /// </summary>
        private void RollChestDrops(DungeonObject chest)
        {
            Tables.DungeonTable table = AR.s.Data.GetDungeonTable(_active.DungeonTableId);
            if (table == null || table.ChestDropRolls <= 0)
                return;

            int itemLevel = ResolveChestItemLevel(_active, chest);
            Vector3 position = new Vector3(chest.X + 0.5f, chest.Y + 0.5f, -0.01f);
            DropHelper.ProcessChestDrops(table.ChestDropRolls, itemLevel, position);
        }

        /// <summary>상자 레벨이 저장돼 있으면(새 던전) 그 값, 없으면(옛 던전) 같은 던전 일반 몬스터의 레벨.</summary>
        private static int ResolveChestItemLevel(DungeonRecord record, DungeonObject chest)
        {
            if (chest.Level > 0)
                return chest.Level;

            for (int i = 0; i < record.Objects.Count; i++)
            {
                DungeonObject obj = record.Objects[i];
                if (obj.Kind == DungeonObjectKind.Monster && obj.Level > 0)
                    return obj.Level;
            }
            return 1;
        }

        private DungeonObject FindObject(int objectId)
        {
            for (int i = 0; i < _active.Objects.Count; i++)
            {
                if (_active.Objects[i].Id == objectId)
                    return _active.Objects[i];
            }
            return null;
        }
    }
}
