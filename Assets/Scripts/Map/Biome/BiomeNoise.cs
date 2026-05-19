using UnityEngine;
using ARPG.History;

namespace ARPG.Map
{
    /// <summary>
    /// per-tile biome 결정 함수. MAP_DESIGN.md §4.3 참조.
    ///
    /// domain warping + 3축 Perlin (온도/습도/고도) + Whittaker threshold.
    /// 같은 (worldX, worldY, mapSeed) 입력 → 항상 같은 BiomeType 결과 (결정론).
    ///
    /// 사용:
    ///   var biome = BiomeNoise.ComputeTileBiome(worldX, worldY, AR.s.Map.mapSeed);
    /// </summary>
    public static class BiomeNoise
    {
        // 스케일 가이드 — MAP_DESIGN.md §4.3.1
        private const float SCALE_WARP        = 0.02f;   // 좌표 흐트림용
        private const float SCALE_TEMPERATURE = 0.005f;  // 가장 광역 — 큰 기후대
        private const float SCALE_HUMIDITY    = 0.008f;  // 중간 — 강수 패치
        private const float SCALE_ELEVATION   = 0.012f;  // 가장 fine — 산맥/저지

        private const float WARP_AMPLITUDE    = 16f;     // 경계 흐트림 강도(타일 단위)

        // Whittaker threshold
        private const float ELEV_MOUNTAIN     = 0.75f;
        private const float ELEV_SWAMP_LOW    = 0.20f;
        private const float HUMID_SWAMP_HIGH  = 0.55f;
        private const float COLD_THRESHOLD    = 0.33f;
        private const float HOT_THRESHOLD     = 0.66f;
        private const float DRY_THRESHOLD     = 0.33f;
        private const float WET_THRESHOLD     = 0.66f;

        public static GlobalEnum.BiomeType ComputeTileBiome(int worldX, int worldY, int mapSeed)
        {
            // 1) Domain warping — 좌표를 살짝 흔들어 격자감 제거
            float warpSeedA = (float)(DerivedSeed.Derive(mapSeed, DerivedSeed.ChannelWarpX) & 0xFFFFu);
            float warpSeedB = (float)(DerivedSeed.Derive(mapSeed, DerivedSeed.ChannelWarpY) & 0xFFFFu);
            float warpX = worldX + (Mathf.PerlinNoise((worldX + warpSeedA) * SCALE_WARP, (worldY + warpSeedA) * SCALE_WARP) - 0.5f) * 2f * WARP_AMPLITUDE;
            float warpY = worldY + (Mathf.PerlinNoise((worldX + warpSeedB) * SCALE_WARP, (worldY + warpSeedB) * SCALE_WARP) - 0.5f) * 2f * WARP_AMPLITUDE;

            // 2) 3축 Perlin
            float tempSeed  = (float)(DerivedSeed.Derive(mapSeed, DerivedSeed.ChannelTemperature) & 0xFFFFu);
            float humidSeed = (float)(DerivedSeed.Derive(mapSeed, DerivedSeed.ChannelHumidity) & 0xFFFFu);
            float elevSeed  = (float)(DerivedSeed.Derive(mapSeed, DerivedSeed.ChannelElevation) & 0xFFFFu);

            float temperature = Mathf.PerlinNoise((warpX + tempSeed)  * SCALE_TEMPERATURE, (warpY + tempSeed)  * SCALE_TEMPERATURE);
            float humidity    = Mathf.PerlinNoise((warpX + humidSeed) * SCALE_HUMIDITY,    (warpY + humidSeed) * SCALE_HUMIDITY);
            float elevation   = Mathf.PerlinNoise((warpX + elevSeed)  * SCALE_ELEVATION,   (warpY + elevSeed)  * SCALE_ELEVATION);

            // 3) 1차: 고도 — 강한 변형
            if (elevation > ELEV_MOUNTAIN) return GlobalEnum.BiomeType.Mountain;
            if (elevation < ELEV_SWAMP_LOW && humidity > HUMID_SWAMP_HIGH) return GlobalEnum.BiomeType.Swamp;

            // 4) 2차: Whittaker (온도 × 습도)
            bool cold = temperature < COLD_THRESHOLD;
            bool hot  = temperature > HOT_THRESHOLD;
            bool dry  = humidity    < DRY_THRESHOLD;
            bool wet  = humidity    > WET_THRESHOLD;

            if (cold && dry) return GlobalEnum.BiomeType.Tundra;
            if (cold)        return GlobalEnum.BiomeType.Snow;
            if (hot && dry)  return GlobalEnum.BiomeType.Desert;
            if (hot && wet)  return GlobalEnum.BiomeType.Swamp;
            if (hot)         return GlobalEnum.BiomeType.Steppe;
            if (wet)         return GlobalEnum.BiomeType.Forest;
            return           GlobalEnum.BiomeType.Plains;
        }

        /// <summary>
        /// region 중심 1점 샘플로 region 단위 biome 결정 (MAP_DESIGN.md §4, v1.8 옵션 X-방향 재채택).
        /// 한 region 안의 모든 청크/타일은 이 biome으로 통일.
        /// </summary>
        public static GlobalEnum.BiomeType ComputeRegionBiome(int regionGridX, int regionGridY, int chunkSize, int mapSeed)
        {
            int regionTileSize = RegionCoord.CHUNKS_PER_REGION * chunkSize;
            int centerX = regionGridX * regionTileSize + regionTileSize / 2;
            int centerY = regionGridY * regionTileSize + regionTileSize / 2;
            return ComputeTileBiome(centerX, centerY, mapSeed);
        }

        /// <summary>
        /// 청크의 biome 조회. 청크가 속한 region의 biome 반환 (region 단위 단일 biome 정책).
        /// MonsterManager 풀 선택 / Renderer ThemeTileSet 선택 등에 사용.
        /// </summary>
        public static GlobalEnum.BiomeType ComputeChunkBiome(int chunkX, int chunkY, int chunkSize, int mapSeed)
        {
            // 청크가 속한 region 좌표
            int regionGridX = chunkX >> 2;   // CHUNKS_PER_REGION = 4
            int regionGridY = chunkY >> 2;
            return ComputeRegionBiome(regionGridX, regionGridY, chunkSize, mapSeed);
        }
    }
}
