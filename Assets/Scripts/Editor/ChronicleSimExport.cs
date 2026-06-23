using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using ARPG.Map;
using ARPG.History;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 연대기 시뮬 프로토타입용 — RaceSettlementPlacer.ComputeAll의 실제 배치 결과를
    /// JSON으로 export. 독립 콘솔 시뮬(Tools/ChronicleSimPrototype)이 입력으로 소비한다.
    ///
    /// 왜 ComputeAll을 직접 안 부르고 미러링하나:
    ///   RaceSettlementPlacer.TryPlace는 AR.s.Map.GetChunkBiome / GetOrCreateRegionData를
    ///   호출 → AR 싱글톤(플레이모드) 필요. 그 둘은 활성 청크가 없을 때 각각
    ///   BiomeNoise.ComputeChunkBiome / RegionData.Compute 와 동일한 순수 함수다.
    ///   여기선 그 순수 함수를 직접 호출해 Edit 모드에서 여러 시드를 일괄 export 한다.
    ///   (결과는 활성 청크가 없는 한 실제 ComputeAll과 byte 단위로 동일)
    ///
    /// ※ RaceSettlementPlacer.ComputeAll/TryPlace 로직이 바뀌면 아래 미러도 같이 고칠 것.
    /// </summary>
    public static class ChronicleSimExport
    {
        // MapManager 기본값과 동일 (조사 결과: chunkSize=8, 경계 [-98,98], 기본 시드 12345)
        private const int ChunkSize = 8;
        private const int MinChunk = -98;
        private const int MaxChunk = 98;
        private const int DefaultSeed = 12345;

        [MenuItem("ARPG/Chronicle Sim/Export Default Seed (12345)", false, 400)]
        public static void ExportDefault()
        {
            string dir = OutputDir();
            int count = ExportSeed(DefaultSeed, dir);
            Debug.Log($"[ChronicleSimExport] seed {DefaultSeed}: {count} settlements → {dir}");
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("ARPG/Chronicle Sim/Export Sweep (20 seeds)", false, 401)]
        public static void ExportSweep()
        {
            string dir = OutputDir();
            var seeds = new List<int> { DefaultSeed };
            for (int i = 1; i <= 20; i++)
                seeds.Add(1000 + i); // 1001..1020

            int total = 0;
            for (int i = 0; i < seeds.Count; i++)
            {
                int c = ExportSeed(seeds[i], dir);
                total += c;
                EditorUtility.DisplayProgressBar("Chronicle Sim Export",
                    $"seed {seeds[i]} ({c} settlements)", (i + 1f) / seeds.Count);
            }
            EditorUtility.ClearProgressBar();
            Debug.Log($"[ChronicleSimExport] {seeds.Count} seeds, {total} settlements total → {dir}");
            EditorUtility.RevealInFinder(dir);
        }

        // ──────────────────────────────────────────────────────────────────

        private static string OutputDir()
        {
            // Application.dataPath = <project>/Assets → 프로젝트 루트의 Tools/ 아래에 둔다 (Assets 밖 = Unity 컴파일 제외)
            string root = Directory.GetParent(Application.dataPath)!.FullName;
            string dir = Path.Combine(root, "Tools", "ChronicleSimPrototype", "placements");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static int ExportSeed(int mapSeed, string dir)
        {
            List<ExportSettlement> settlements = MirrorComputeAll(
                mapSeed, ChunkSize, MinChunk, MaxChunk, MinChunk, MaxChunk);

            var file = new ExportFile
            {
                MapSeed = mapSeed,
                ChunkSize = ChunkSize,
                Settlements = settlements,
            };

            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            File.WriteAllText(Path.Combine(dir, $"seed_{mapSeed}.json"), json);
            return settlements.Count;
        }

        // RaceSettlementPlacer.ComputeAll 미러 (AR 의존 제거판)
        private static List<ExportSettlement> MirrorComputeAll(
            int mapSeed, int chunkSize, int minChunkX, int maxChunkX, int minChunkY, int maxChunkY)
        {
            var results = new List<ExportSettlement>();

            int minRegionX = minChunkX >> 2;
            int maxRegionX = maxChunkX >> 2;
            int minRegionY = minChunkY >> 2;
            int maxRegionY = maxChunkY >> 2;

            int id = 0;
            for (int rx = minRegionX; rx <= maxRegionX; rx++)
            {
                for (int ry = minRegionY; ry <= maxRegionY; ry++)
                {
                    if (TryPlaceMirror(new Vector2Int(rx, ry), mapSeed, chunkSize, out ExportSettlement s))
                    {
                        s.Id = id++;
                        results.Add(s);
                    }
                }
            }
            return results;
        }

        // RaceSettlementPlacer.TryPlace 미러 — AR.s.Map.* → 순수 정적 함수로 치환
        private static bool TryPlaceMirror(
            Vector2Int regionCoord, int mapSeed, int chunkSize, out ExportSettlement placement)
        {
            placement = null;

            // 1) Region histogram (AR.s.Map.GetOrCreateRegionData → RegionData.Compute)
            RegionData rd = RegionData.Compute(regionCoord, chunkSize, mapSeed);
            GlobalEnum.BiomeType dominant = rd.DominantBiome;

            // 2) race 선택 — RaceSettlementPlacer.TryPlace와 동일한 hash 식
            ulong settlementHash = DerivedSeed.Derive(
                unchecked((int)((uint)regionCoord.x ^ ((uint)regionCoord.y << 16) ^ (uint)mapSeed)),
                DerivedSeed.ChannelSettlement);

            GlobalEnum.RaceType? raceOpt = RaceBiomeTable.PickRace(dominant, settlementHash);
            if (raceOpt.HasValue == false)
                return false;
            GlobalEnum.RaceType race = raceOpt.Value;

            // 3) race 호환 chunk 후보 (AR.s.Map.GetChunkBiome → BiomeNoise.ComputeChunkBiome)
            Vector2Int regionChunkOrigin = RegionCoord.RegionChunkOrigin(regionCoord);
            var chunkCandidates = new List<Vector2Int>();
            for (int cx = 0; cx < RegionCoord.CHUNKS_PER_REGION; cx++)
            {
                for (int cy = 0; cy < RegionCoord.CHUNKS_PER_REGION; cy++)
                {
                    Vector2Int chunkCoord = new(regionChunkOrigin.x + cx, regionChunkOrigin.y + cy);
                    GlobalEnum.BiomeType chunkBiome =
                        BiomeNoise.ComputeChunkBiome(chunkCoord.x, chunkCoord.y, chunkSize, mapSeed);
                    if (RaceBiomeTable.IsCompatible(chunkBiome, race))
                        chunkCandidates.Add(chunkCoord);
                }
            }
            if (chunkCandidates.Count == 0)
                return false;

            // 4) hash로 chunk 1개 선택
            int chunkIdx = (int)(settlementHash % (ulong)chunkCandidates.Count);
            Vector2Int chosenChunk = chunkCandidates[chunkIdx];

            // 5) chunk 중심 좌표
            Vector2Int worldCoord = DeterministicCoord.ChunkCenter(chosenChunk, chunkSize);
            GlobalEnum.BiomeType chosenBiome =
                BiomeNoise.ComputeChunkBiome(chosenChunk.x, chosenChunk.y, chunkSize, mapSeed);

            placement = new ExportSettlement
            {
                RegionX = regionCoord.x,
                RegionY = regionCoord.y,
                ChunkX = chosenChunk.x,
                ChunkY = chosenChunk.y,
                WorldX = worldCoord.x,
                WorldY = worldCoord.y,
                Race = (int)race,
                RaceName = race.ToString(),
                Biome = (int)chosenBiome,
                BiomeName = chosenBiome.ToString(),
                RegionDominantBiome = (int)dominant,
            };
            return true;
        }

        // ── 직렬화 DTO (콘솔 측 Placement와 1:1 대응) ──
        private class ExportFile
        {
            public int MapSeed;
            public int ChunkSize;
            public List<ExportSettlement> Settlements = new();
        }

        private class ExportSettlement
        {
            public int Id;
            public int RegionX;
            public int RegionY;
            public int ChunkX;
            public int ChunkY;
            public int WorldX;
            public int WorldY;
            public int Race;
            public string RaceName;
            public int Biome;
            public string BiomeName;
            public int RegionDominantBiome;
        }
    }
}
