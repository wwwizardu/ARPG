using System.Collections.Generic;
using UnityEngine;
using ARPG.History;

namespace ARPG.Map
{
    /// <summary>
    /// Race Settlement 절차 배치 — MAP_DESIGN.md §7.3.
    /// chunk-grain 정책 (v1.9):
    ///   1. region 순회 → region histogram의 DominantBiome으로 race 선택
    ///   2. region 안 16 chunk 중 chunkBiome이 race 호환 chunk 후보 수집
    ///   3. hash로 1 chunk 선택 → chunk 안 좌표 결정
    ///   4. 충돌 검사 후 mapFileDic에 청사진 등록 (콘텐츠 없으면 로그만)
    /// </summary>
    public class RaceSettlementPlacer
    {
        public struct SettlementPlacement
        {
            public Vector2Int RegionCoord;
            public Vector2Int ChunkCoord;
            public Vector2Int WorldCoord;       // 청사진 origin이 될 타일 좌표
            public GlobalEnum.RaceType Race;
            public GlobalEnum.SettlementSize Size;
            public GlobalEnum.BiomeType ChunkBiome;
            public GlobalEnum.BiomeType RegionDominantBiome;
        }

        /// <summary>
        /// 전체 맵의 settlement 절차 배치 결과 계산. 결정론 — 같은 mapSeed면 항상 같은 배치.
        /// </summary>
        public static List<SettlementPlacement> ComputeAll(int mapSeed, int chunkSize, int minChunkX, int maxChunkX, int minChunkY, int maxChunkY)
        {
            var results = new List<SettlementPlacement>();

            int minRegionX = minChunkX >> 2;
            int maxRegionX = maxChunkX >> 2;
            int minRegionY = minChunkY >> 2;
            int maxRegionY = maxChunkY >> 2;

            for (int rx = minRegionX; rx <= maxRegionX; rx++)
            {
                for (int ry = minRegionY; ry <= maxRegionY; ry++)
                {
                    Vector2Int regionCoord = new(rx, ry);
                    if (TryPlace(regionCoord, mapSeed, chunkSize, minChunkX, maxChunkX, minChunkY, maxChunkY, out SettlementPlacement placement))
                        results.Add(placement);
                }
            }

            return results;
        }

        /// <summary>
        /// 단일 region에 대한 settlement 배치 결정.
        /// chunk-grain: region 16 chunk 중 race 호환 chunk만 후보 → hash로 1개 선택.
        /// </summary>
        public static bool TryPlace(
            Vector2Int regionCoord, int mapSeed, int chunkSize,
            int minChunkX, int maxChunkX, int minChunkY, int maxChunkY,
            out SettlementPlacement placement)
        {
            placement = default;

            // 1) Region histogram 조회 (lazy compute)
            RegionData rd = AR.s.Map.GetOrCreateRegionData(regionCoord);
            GlobalEnum.BiomeType dominant = rd.DominantBiome;

            // 2) race 선택 — Region dominant biome 기반
            ulong settlementHash = DerivedSeed.Derive(
                unchecked((int)((uint)regionCoord.x ^ ((uint)regionCoord.y << 16) ^ (uint)mapSeed)),
                DerivedSeed.ChannelSettlement);

            GlobalEnum.RaceType? raceOpt = RaceBiomeTable.PickRace(dominant, settlementHash);
            if (raceOpt.HasValue == false)
                return false;   // Wasteland 등 — settlement 없음

            GlobalEnum.RaceType race = raceOpt.Value;

            // 3) race 호환 chunk 후보 수집 — region 16 chunk 중 chunkBiome 매칭
            Vector2Int regionChunkOrigin = RegionCoord.RegionChunkOrigin(regionCoord);
            var chunkCandidates = new List<Vector2Int>();
            for (int cx = 0; cx < RegionCoord.CHUNKS_PER_REGION; cx++)
            {
                for (int cy = 0; cy < RegionCoord.CHUNKS_PER_REGION; cy++)
                {
                    Vector2Int chunkCoord = new(regionChunkOrigin.x + cx, regionChunkOrigin.y + cy);
                    if (chunkCoord.x < minChunkX || chunkCoord.x > maxChunkX ||
                        chunkCoord.y < minChunkY || chunkCoord.y > maxChunkY)
                        continue;

                    GlobalEnum.BiomeType chunkBiome = AR.s.Map.GetChunkBiome(chunkCoord);
                    if (RaceBiomeTable.IsCompatible(chunkBiome, race))
                        chunkCandidates.Add(chunkCoord);
                }
            }

            if (chunkCandidates.Count == 0)
                return false;   // race-호환 chunk 없음 (dominant biome cluster가 chunk 중심에 안 잡힘)

            // 4) hash로 chunk 1개 선택
            int chunkIdx = (int)(settlementHash % (ulong)chunkCandidates.Count);
            Vector2Int chosenChunk = chunkCandidates[chunkIdx];

            // 5) chunk 안 좌표 — 일단 중심. 추후 청사진 footprint 고려해 offset 조정 가능.
            Vector2Int worldCoord = DeterministicCoord.ChunkCenter(chosenChunk, chunkSize);

            // 6) size 결정 — v1 초기는 Village 고정 (§12 #14)
            GlobalEnum.SettlementSize size = GlobalEnum.SettlementSize.Village;

            placement = new SettlementPlacement
            {
                RegionCoord = regionCoord,
                ChunkCoord = chosenChunk,
                WorldCoord = worldCoord,
                Race = race,
                Size = size,
                ChunkBiome = AR.s.Map.GetChunkBiome(chosenChunk),
                RegionDominantBiome = dominant,
            };
            return true;
        }
    }
}
