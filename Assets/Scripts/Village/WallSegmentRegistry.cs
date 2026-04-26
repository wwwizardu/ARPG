#nullable enable

namespace ARPG.Village
{
    /// <summary>
    /// 마을별 벽 세그먼트(VillageData.WallSegments)에 대한 정적 조회/갱신 유틸.
    /// Phase C 단순화: 별도 dict 없이 VillageData가 정본. 향후 활성 청크 캐시 필요 시 확장.
    /// </summary>
    public static class WallSegmentRegistry
    {
        /// <summary>
        /// 다음으로 지을 미완성 세그먼트(IsBuilt=false) 반환. 없으면 null.
        /// 큐 순서 = WallSegments 리스트 순서 (WallPlanner가 채운 순서 따름).
        /// </summary>
        public static WallSegmentSaveData? GetNextUnbuiltSegment(VillageData v)
        {
            if (v.WallSegments == null) return null;
            for (int i = 0; i < v.WallSegments.Count; i++)
            {
                if (v.WallSegments[i].IsBuilt == false)
                    return v.WallSegments[i];
            }
            return null;
        }

        /// <summary>
        /// 세그먼트의 IsBuilt를 true로 마킹 + 마을 통계 갱신.
        /// 호출자가 VillageComponent 동기화 필요 시 별도 처리.
        /// </summary>
        public static void MarkSegmentBuilt(VillageData v, int segmentId)
        {
            if (v.WallSegments == null) return;
            for (int i = 0; i < v.WallSegments.Count; i++)
            {
                if (v.WallSegments[i].SegmentId == segmentId)
                {
                    v.WallSegments[i].IsBuilt = true;
                    v.CompletedWallSegments++;
                    return;
                }
            }
        }

        public static int CountUnbuilt(VillageData v)
        {
            if (v.WallSegments == null) return 0;
            int count = 0;
            for (int i = 0; i < v.WallSegments.Count; i++)
                if (v.WallSegments[i].IsBuilt == false) count++;
            return count;
        }

        public static int CountBuilt(VillageData v)
        {
            if (v.WallSegments == null) return 0;
            int count = 0;
            for (int i = 0; i < v.WallSegments.Count; i++)
                if (v.WallSegments[i].IsBuilt) count++;
            return count;
        }
    }
}
