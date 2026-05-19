using UnityEngine;
using ARPG.History;

namespace ARPG.Map
{
    /// <summary>
    /// Region/Chunk 안에 결정론 좌표 1점 생성. MAP_DESIGN.md §7.3 (Race Settlement) / §6.4 (LoreFragment) 공용.
    ///
    /// 보장:
    ///   1. 재현 — 같은 (regionCoord/chunkCoord, subId, mapSeed) → 항상 같은 좌표
    ///   2. 균등 분포 — SplitMix64로 비트 섞어 인접 region 좌표가 격자 정렬 안 됨
    ///   3. 객체별 분리 — subId로 settlement(0) / lore[0~N] / 향후 객체 공존
    /// </summary>
    public static class DeterministicCoord
    {
        /// <summary>
        /// Region 안 임의 1점 (월드 타일 좌표). region 영역 = 4×4 청크 = 32×32 타일.
        /// subId로 같은 region 안 여러 객체 좌표 분리 (settlement vs lore_fragment 등).
        /// </summary>
        public static Vector2Int InRegion(Vector2Int regionCoord, int subId, int mapSeed, int chunkSize = 8)
        {
            int regionTileSize = RegionCoord.CHUNKS_PER_REGION * chunkSize; // 32
            int originX = regionCoord.x * regionTileSize;
            int originY = regionCoord.y * regionTileSize;

            ulong h = HashFor(regionCoord, subId, mapSeed);
            int dx = (int)(h % (ulong)regionTileSize);
            int dy = (int)((h >> 32) % (ulong)regionTileSize);
            return new Vector2Int(originX + dx, originY + dy);
        }

        /// <summary>
        /// Chunk 안 임의 1점 (월드 타일 좌표). chunk 영역 = 8×8 타일.
        /// subId로 같은 chunk 안 여러 객체 좌표 분리.
        /// </summary>
        public static Vector2Int InChunk(Vector2Int chunkCoord, int subId, int mapSeed, int chunkSize = 8)
        {
            int originX = chunkCoord.x * chunkSize;
            int originY = chunkCoord.y * chunkSize;

            ulong h = HashFor(chunkCoord, subId, mapSeed);
            int dx = (int)(h % (ulong)chunkSize);
            int dy = (int)((h >> 32) % (ulong)chunkSize);
            return new Vector2Int(originX + dx, originY + dy);
        }

        /// <summary>Chunk 중심 좌표 (월드 타일). offset 없이 결정론적으로 정중앙.</summary>
        public static Vector2Int ChunkCenter(Vector2Int chunkCoord, int chunkSize = 8)
        {
            return new Vector2Int(
                chunkCoord.x * chunkSize + chunkSize / 2,
                chunkCoord.y * chunkSize + chunkSize / 2);
        }

        /// <summary>좌표 + subId + mapSeed → SplitMix64 hash. 결정론 분포 보장.</summary>
        public static ulong HashFor(Vector2Int coord, int subId, int mapSeed)
        {
            ulong key = (ulong)(uint)coord.x
                      ^ ((ulong)(uint)coord.y << 16)
                      ^ ((ulong)(uint)subId << 32)
                      ^ ((ulong)(uint)mapSeed << 48);
            return DerivedSeed.Derive(unchecked((int)key), DerivedSeed.ChannelCoord);
        }
    }
}
