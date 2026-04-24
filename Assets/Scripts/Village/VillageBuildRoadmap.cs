#nullable enable

namespace ARPG.Village
{
    /// <summary>
    /// 로드맵 1행: 어떤 오브젝트를, 얼마나 오래 걸려서 짓는가.
    /// </summary>
    public readonly struct RoadmapEntry
    {
        public readonly int TableId;
        public readonly float BuildHours;

        public RoadmapEntry(int tableId, float buildHours)
        {
            TableId = tableId;
            BuildHours = buildHours;
        }
    }

    /// <summary>
    /// Phase B: Stage 0 (Settlement) → Stage 1 (Hamlet) 로드맵.
    /// 하드코딩 시퀀스. Phase D에서 필요도 스코어링으로 교체 예정.
    /// 인덱스 = PlacedObjectTypeIds에서 Campfire(100) 제외 카운트.
    /// </summary>
    public static class VillageBuildRoadmap
    {
        // Campfire(100)는 Phase A 호환 위해 시퀀스 외 별도 처리.
        // 순서: Bedroll → Bed1 → Woodpile → CropPlot → Chest → Bed2 → Well
        private static readonly RoadmapEntry[] SETTLEMENT_SEQUENCE = new[]
        {
            new RoadmapEntry(101, 1.5f),  // Bedroll
            new RoadmapEntry(102, 3.0f),  // Bed (첫 번째)
            new RoadmapEntry(110, 2.0f),  // Woodpile
            new RoadmapEntry(120, 2.0f),  // CropPlot
            new RoadmapEntry(111, 2.0f),  // Chest
            new RoadmapEntry(102, 3.0f),  // Bed (두 번째 — Stage 1 승격 조건)
            new RoadmapEntry(130, 5.0f),  // Well
        };

        public const int CAMPFIRE_TABLE_ID = 100;
        public const float CAMPFIRE_BUILD_HOURS = 2f;

        /// <summary>
        /// 다음으로 지을 오브젝트. 로드맵 소진 시 null.
        /// Campfire 미배치 시 항상 Campfire 우선.
        /// </summary>
        public static RoadmapEntry? GetNextTarget(VillageData village)
        {
            if (village.PlacedObjectTypeIds == null)
                return new RoadmapEntry(CAMPFIRE_TABLE_ID, CAMPFIRE_BUILD_HOURS);

            // Campfire 우선
            if (village.PlacedObjectTypeIds.Contains(CAMPFIRE_TABLE_ID) == false)
                return new RoadmapEntry(CAMPFIRE_TABLE_ID, CAMPFIRE_BUILD_HOURS);

            // SETTLEMENT_SEQUENCE 인덱스 = Campfire 제외한 누적 개수
            int placedExceptCampfire = 0;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                if (village.PlacedObjectTypeIds[i] != CAMPFIRE_TABLE_ID)
                    placedExceptCampfire++;
            }

            if (placedExceptCampfire >= SETTLEMENT_SEQUENCE.Length)
                return null;

            return SETTLEMENT_SEQUENCE[placedExceptCampfire];
        }

        /// <summary>
        /// 특정 TableId의 BuildHours 조회 (로드 시 ObjectPlacementTaskComponent 복원용).
        /// 로드맵에 없는 ID면 기본값 2h 반환.
        /// </summary>
        public static float GetBuildHours(int tableId)
        {
            if (tableId == CAMPFIRE_TABLE_ID)
                return CAMPFIRE_BUILD_HOURS;

            for (int i = 0; i < SETTLEMENT_SEQUENCE.Length; i++)
            {
                if (SETTLEMENT_SEQUENCE[i].TableId == tableId)
                    return SETTLEMENT_SEQUENCE[i].BuildHours;
            }
            return 2f;
        }
    }
}
