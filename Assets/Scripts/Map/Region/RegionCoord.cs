using UnityEngine;

namespace ARPG.Map
{
    /// <summary>
    /// Region = 4×4 청크 = 32×32 타일 좌표 격자 헬퍼. ECS entity 아님. MAP_DESIGN.md §5 참조.
    /// 용도:
    ///   1) region 단위 단일 biome — 한 region 안 모든 청크가 같은 biome (v1.8, 사용자 선택)
    ///   2) lore fragment 결정론 ID grouping (fragmentId = regionId * 1000 + index)
    ///   3) Race Settlement 배치 단위
    /// </summary>
    public static class RegionCoord
    {
        public const int CHUNKS_PER_REGION = 4;
        // chunkCoord >> 2 = chunkCoord / 4 (CHUNKS_PER_REGION). Bit shift는 음수에서 floor 보장.

        public static Vector2Int ChunkToRegion(Vector2Int chunkCoord)
            => new Vector2Int(chunkCoord.x >> 2, chunkCoord.y >> 2);

        public static int ToRegionId(Vector2Int regionCoord)
            => regionCoord.x * 100000 + regionCoord.y;

        public static int ChunkToRegionId(Vector2Int chunkCoord)
            => ToRegionId(ChunkToRegion(chunkCoord));

        /// <summary>region의 좌하단 청크 좌표 (regionGridX * 4, regionGridY * 4).</summary>
        public static Vector2Int RegionChunkOrigin(Vector2Int regionCoord)
            => new Vector2Int(regionCoord.x * CHUNKS_PER_REGION, regionCoord.y * CHUNKS_PER_REGION);
    }
}
