#nullable enable
using System.Collections.Generic;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: 마을별 NeedsEvaluation 후보 1위 캐시.
    /// System_VillageNeedsEvaluation이 게임시간 2h마다 갱신.
    /// System_VillageBuildQueue.TryStartNextTask가 로드맵보다 먼저 조회.
    ///
    /// 정본은 System_VillageNeedsEvaluation의 평가 함수. 이 캐시는 휘발성 (세이브 X).
    /// </summary>
    public static class VillageNeedsCache
    {
        private static readonly Dictionary<int, RoadmapEntry> _topByVillage = new();

        public static void Set(int villageId, RoadmapEntry entry)
        {
            _topByVillage[villageId] = entry;
        }

        public static void Clear(int villageId)
        {
            _topByVillage.Remove(villageId);
        }

        public static void ClearAll()
        {
            _topByVillage.Clear();
        }

        /// <summary>
        /// 마을의 다음 빌드 타겟. 캐시 미스 시 null → BuildQueue가 로드맵 fallback.
        /// </summary>
        public static RoadmapEntry? GetNextTarget(VillageData v)
        {
            if (_topByVillage.TryGetValue(v.VillageId, out var entry))
            {
                // 후보가 이미 마을에 있는 MaxPerVillage 도달 시 무효화 (안전 가드)
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(entry.TableId);
                if (t != null && t.MaxPerVillage > 0)
                {
                    int existing = CountByTableId(v, entry.TableId);
                    if (existing >= t.MaxPerVillage) return null;
                }
                return entry;
            }
            return null;
        }

        private static int CountByTableId(VillageData v, int tableId)
        {
            if (v.PlacedObjectTypeIds == null) return 0;
            int count = 0;
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
                if (v.PlacedObjectTypeIds[i] == tableId) count++;
            return count;
        }
    }
}
