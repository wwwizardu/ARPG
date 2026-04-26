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
    /// Phase B/C: Stage별 자가 건설 로드맵.
    /// Settlement → Hamlet → Village → Town까지 하드코딩 시퀀스.
    /// 인덱스 = PlacedObjectTypeIds에서 시퀀스 시작 이후 누적된 항목 카운트.
    /// Phase D에서 필요도 스코어링으로 교체 예정.
    /// </summary>
    public static class VillageBuildRoadmap
    {
        public const int CAMPFIRE_TABLE_ID = 100;
        public const float CAMPFIRE_BUILD_HOURS = 2f;

        // Stage 0 (Settlement → Hamlet) 시퀀스 — Campfire 외 7종
        // Bedroll → Bed → Woodpile → CropPlot → Chest → Bed 2 → Well
        private static readonly RoadmapEntry[] SETTLEMENT_SEQUENCE = new[]
        {
            new RoadmapEntry(101, 1.5f),  // Bedroll
            new RoadmapEntry(102, 3.0f),  // Bed (1)
            new RoadmapEntry(110, 2.0f),  // Woodpile
            new RoadmapEntry(120, 2.0f),  // CropPlot
            new RoadmapEntry(111, 2.0f),  // Chest
            new RoadmapEntry(102, 3.0f),  // Bed (2) — Stage 1 승격 조건
            new RoadmapEntry(130, 5.0f),  // Well
        };

        // Stage 1 (Hamlet → Village) 시퀀스 — 8종
        // VILLAGE_GROWTH_STAGES.md §3.2 참조
        private static readonly RoadmapEntry[] HAMLET_SEQUENCE = new[]
        {
            new RoadmapEntry(102, 3.0f),  // Bed (3번째)
            new RoadmapEntry(140, 4.0f),  // ChoppingBlock
            new RoadmapEntry(112, 3.0f),  // Stockpile (Cap_Stone +80)
            new RoadmapEntry(150, 4.0f),  // Hearth
            new RoadmapEntry(141, 4.0f),  // DryingRack
            new RoadmapEntry(151, 5.0f),  // MerchantStall
            new RoadmapEntry(152, 6.0f),  // TownPost — Stage 2 승격 트리거
            new RoadmapEntry(102, 3.0f),  // Bed (4번째)
        };

        // Stage 2 (Village → Town) 시퀀스 — 11종 (대장간 세트 + 인구 확장 Bed)
        // VILLAGE_GROWTH_STAGES.md §3.3 참조
        private static readonly RoadmapEntry[] VILLAGE_SEQUENCE = new[]
        {
            new RoadmapEntry(160, 6.0f),  // Furnace
            new RoadmapEntry(161, 5.0f),  // Anvil — Stage 3 승격 조건 일부
            new RoadmapEntry(142, 4.0f),  // MiningCart
            new RoadmapEntry(162, 4.0f),  // QuenchVat
            new RoadmapEntry(153, 4.0f),  // InnBed
            new RoadmapEntry(170, 6.0f),  // Shrine
            new RoadmapEntry(154, 5.0f),  // SignalBrazier
            new RoadmapEntry(102, 3.0f),  // Bed (5)
            new RoadmapEntry(102, 3.0f),  // Bed (6)
            new RoadmapEntry(102, 3.0f),  // Bed (7)
            new RoadmapEntry(102, 3.0f),  // Bed (8) — Stage 3 승격 조건 (Bed 8개)
        };

        /// <summary>
        /// 다음으로 지을 오브젝트. 로드맵 소진 시 null.
        /// Campfire 미배치 시 항상 Campfire 우선. 그 외엔 Stage별 시퀀스.
        /// </summary>
        public static RoadmapEntry? GetNextTarget(VillageData village)
        {
            if (village.PlacedObjectTypeIds == null)
                return new RoadmapEntry(CAMPFIRE_TABLE_ID, CAMPFIRE_BUILD_HOURS);

            // Campfire 우선 (Phase A 호환)
            if (village.PlacedObjectTypeIds.Contains(CAMPFIRE_TABLE_ID) == false)
                return new RoadmapEntry(CAMPFIRE_TABLE_ID, CAMPFIRE_BUILD_HOURS);

            // Stage별 시퀀스 분기
            return village.Stage switch
            {
                VillageStage.Settlement => GetNextFromSequence(village, SETTLEMENT_SEQUENCE, includeBefore: false),
                VillageStage.Hamlet     => GetNextFromSequence(village, HAMLET_SEQUENCE, includeBefore: true, prevSequences: SETTLEMENT_SEQUENCE),
                VillageStage.Village    => GetNextFromSequence(village, VILLAGE_SEQUENCE, includeBefore: true, prevSequences1: SETTLEMENT_SEQUENCE, prevSequences2: HAMLET_SEQUENCE),
                _ => null,  // Town 이상은 Phase C+ (현재 Town 진입 시 벽 빌더가 처리)
            };
        }

        /// <summary>
        /// 특정 TableId의 BuildHours 조회 — 로드 시 ObjectPlacementTaskComponent 복원용.
        /// </summary>
        public static float GetBuildHours(int tableId)
        {
            if (tableId == CAMPFIRE_TABLE_ID)
                return CAMPFIRE_BUILD_HOURS;

            float h = FindHoursIn(SETTLEMENT_SEQUENCE, tableId);
            if (h > 0f) return h;
            h = FindHoursIn(HAMLET_SEQUENCE, tableId);
            if (h > 0f) return h;
            h = FindHoursIn(VILLAGE_SEQUENCE, tableId);
            if (h > 0f) return h;
            return 2f;  // Wall segments 등 로드맵 외 기본값
        }

        // ========== 내부 헬퍼 ==========

        /// <summary>
        /// 시퀀스에서 다음 오브젝트 결정.
        /// includeBefore=true이면 prevSequences의 항목들을 PlacedObjectTypeIds에서 차감 후 인덱스 결정.
        /// </summary>
        private static RoadmapEntry? GetNextFromSequence(
            VillageData v,
            RoadmapEntry[] seq,
            bool includeBefore,
            RoadmapEntry[]? prevSequences = null,
            RoadmapEntry[]? prevSequences1 = null,
            RoadmapEntry[]? prevSequences2 = null)
        {
            // 시퀀스 시작 이후 누적된 항목 수 계산
            int placedTotal = CountPlacedExceptCampfire(v);

            // 이전 Stage 시퀀스 길이만큼 차감
            int offset = 0;
            if (includeBefore)
            {
                if (prevSequences != null) offset += prevSequences.Length;
                if (prevSequences1 != null) offset += prevSequences1.Length;
                if (prevSequences2 != null) offset += prevSequences2.Length;
            }

            int idx = placedTotal - offset;
            if (idx < 0) idx = 0;  // 안전장치 (이론상 발생 안 함)
            if (idx >= seq.Length) return null;
            return seq[idx];
        }

        private static int CountPlacedExceptCampfire(VillageData v)
        {
            int count = 0;
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
            {
                if (v.PlacedObjectTypeIds[i] != CAMPFIRE_TABLE_ID)
                    count++;
            }
            return count;
        }

        private static float FindHoursIn(RoadmapEntry[] seq, int tableId)
        {
            for (int i = 0; i < seq.Length; i++)
            {
                if (seq[i].TableId == tableId)
                    return seq[i].BuildHours;
            }
            return 0f;
        }
    }
}
