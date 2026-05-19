using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Map
{
    /// <summary>
    /// Region 단위 biome 통계 (v1.9). cluster-grained biome 시스템에서:
    ///   - 청크 내 cluster마다 자기 biome으로 렌더 (시각 자연스러움)
    ///   - Region이 모든 cluster biome의 histogram 보유 → race/settlement 결정 시 사용
    ///
    /// 사용:
    ///   var regionData = AR.s.Map.GetOrCreateRegionData(regionCoord);
    ///   BiomeType raceBiome = regionData.DominantBiome;  // 또는 PickWeightedBiome(hash)
    /// </summary>
    public class RegionData
    {
        public Vector2Int Coord;
        public Dictionary<GlobalEnum.BiomeType, int> BiomeStats;
        public GlobalEnum.BiomeType DominantBiome;
        public int TotalClusters;

        /// <summary>region 전체의 cluster를 sampling하여 histogram 채움. 결정론.</summary>
        public static RegionData Compute(Vector2Int regionCoord, int chunkSize, int mapSeed)
        {
            var data = new RegionData
            {
                Coord = regionCoord,
                BiomeStats = new Dictionary<GlobalEnum.BiomeType, int>()
            };

            int clustersPerChunkSide = chunkSize / BiomeNoise.CLUSTER_SIZE;
            int regionChunkOriginX = regionCoord.x * RegionCoord.CHUNKS_PER_REGION;
            int regionChunkOriginY = regionCoord.y * RegionCoord.CHUNKS_PER_REGION;

            // region 내 모든 cluster 좌표 순회 (4 chunks × 4 clusters_per_chunk_side = 16 clusters per side)
            int clustersPerSide = RegionCoord.CHUNKS_PER_REGION * clustersPerChunkSide;
            int regionClusterOriginX = regionChunkOriginX * clustersPerChunkSide;
            int regionClusterOriginY = regionChunkOriginY * clustersPerChunkSide;

            for (int cx = 0; cx < clustersPerSide; cx++)
            {
                for (int cy = 0; cy < clustersPerSide; cy++)
                {
                    int globalClusterX = regionClusterOriginX + cx;
                    int globalClusterY = regionClusterOriginY + cy;
                    GlobalEnum.BiomeType biome = BiomeNoise.ComputeClusterBiome(globalClusterX, globalClusterY, mapSeed);
                    if (data.BiomeStats.TryGetValue(biome, out int count))
                        data.BiomeStats[biome] = count + 1;
                    else
                        data.BiomeStats[biome] = 1;
                }
            }
            data.TotalClusters = clustersPerSide * clustersPerSide;

            // dominant = 최다 count. 동률 시 BiomeType enum 값 작은 쪽 (결정론).
            int maxCount = -1;
            GlobalEnum.BiomeType dominant = GlobalEnum.BiomeType.Plains;
            foreach (var kv in data.BiomeStats)
            {
                if (kv.Value > maxCount || (kv.Value == maxCount && (int)kv.Key < (int)dominant))
                {
                    maxCount = kv.Value;
                    dominant = kv.Key;
                }
            }
            data.DominantBiome = dominant;
            return data;
        }

        /// <summary>
        /// histogram 가중치 기반 결정론 biome 선택. race 분포에 사용 가능 (Snow 12 + Plains 4 → 75% Snow).
        /// </summary>
        public GlobalEnum.BiomeType PickWeightedBiome(uint hash)
        {
            if (TotalClusters <= 0)
                return DominantBiome;
            int target = (int)(hash % (uint)TotalClusters);
            int acc = 0;
            // 결정론 위해 enum 값 기준 정렬 순회
            foreach (var kv in BiomeStats)
            {
                acc += kv.Value;
                if (target < acc)
                    return kv.Key;
            }
            return DominantBiome;
        }
    }
}
