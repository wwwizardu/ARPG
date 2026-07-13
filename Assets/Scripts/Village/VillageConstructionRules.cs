#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 실시간/추상 착공이 공유하는 비용, 시간, task 스냅샷 규칙.
    /// </summary>
    public static class VillageConstructionRules
    {
        public const float DefaultBuildHours = 2f;

        public static float GetBuildHours(Tables.BuildableItemTable table)
        {
            if (VillageWallRules.IsWallTable(table.Id))
                return VillageWallRules.PalisadeBuildHours;
            return table.BuildHours > 0f ? table.BuildHours : DefaultBuildHours;
        }

        public static int GetProgressHp(Tables.BuildableItemTable table,
            float accumulatedHours)
        {
            if (table.HP <= 0)
                return 0;
            float duration = Mathf.Max(0.0001f, GetBuildHours(table));
            return Mathf.Clamp(Mathf.RoundToInt(accumulatedHours / duration * table.HP),
                0, table.HP);
        }

        public static bool CanAfford(VillageData village, Tables.BuildableItemTable table)
        {
            int wood = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Wood);
            if (wood < table.Cost_Wood)
                return false;
            if (VillageWallRules.IsWallTable(table.Id))
                return true;

            int stone = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Stone);
            return stone >= table.Cost_Stone;
        }

        public static bool TryReserve(VillageData village, Tables.BuildableItemTable table)
        {
            if (CanAfford(village, table) == false)
                return false;
            if (VillageResourceSimulation.Consume(village, GlobalEnum.ItemType.Wood,
                    table.Cost_Wood) == false)
                return false;
            if (VillageWallRules.IsWallTable(table.Id))
                return true;
            if (VillageResourceSimulation.Consume(village, GlobalEnum.ItemType.Stone,
                    table.Cost_Stone))
                return true;

            VillageResourceSimulation.Produce(village, GlobalEnum.ItemType.Wood, table.Cost_Wood);
            return false;
        }

        public static void Refund(VillageData village, BuildTaskSnapshot task)
        {
            VillageResourceSimulation.Produce(village, GlobalEnum.ItemType.Wood,
                task.ReservedWood);
            VillageResourceSimulation.Produce(village, GlobalEnum.ItemType.Stone,
                task.ReservedStone);
        }
    }
}
