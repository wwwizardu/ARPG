#nullable enable
using System;
using System.Collections.Generic;

namespace ARPG.Village
{
    public sealed class VillageAbstractSliceResult
    {
        public int NextCursor;
        public int VillagesVisited;
        public int VillagesAdvanced;
        public readonly List<VillageAbstractAdvanceResult> AdvanceResults = new();
    }

    /// <summary>
    /// System_SettlementSim이 사용하는 데이터 전용 라운드로빈 slice API.
    /// </summary>
    public static class VillageAbstractConstructionScheduler
    {
        public static VillageAbstractSliceResult AdvanceSlice(
            IReadOnlyList<VillageData> villages, int cursor, int villageBudget, float now,
            Func<VillageData, bool> shouldVisit,
            Func<VillageData, VillageAbstractAdvanceOptions> optionsFactory)
        {
            VillageAbstractSliceResult slice = new VillageAbstractSliceResult();
            int count = villages.Count;
            if (count == 0 || villageBudget <= 0)
            {
                slice.NextCursor = count == 0 ? 0 : NormalizeCursor(cursor, count);
                return slice;
            }

            int index = NormalizeCursor(cursor, count);
            int visitLimit = Math.Min(villageBudget, count);
            for (int visited = 0; visited < visitLimit; visited++)
            {
                VillageData village = villages[index];
                slice.VillagesVisited++;
                if (shouldVisit(village))
                {
                    VillageAbstractAdvanceOptions options = optionsFactory(village);
                    VillageAbstractAdvanceResult result =
                        VillageAbstractConstruction.AdvanceAbstractConstruction(village, now,
                            options);
                    slice.AdvanceResults.Add(result);
                    slice.VillagesAdvanced++;
                }

                index++;
                if (index >= count)
                    index = 0;
            }

            slice.NextCursor = index;
            return slice;
        }

        private static int NormalizeCursor(int cursor, int count)
        {
            int normalized = cursor % count;
            return normalized < 0 ? normalized + count : normalized;
        }
    }
}
