using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using ARPG.Map;
using ARPG.History;

namespace ARPG.EditorTools
{
    /// <summary>
    /// R1 검증 도구 — 런타임 SettlementChronicleSimulator를 Edit 모드에서 실행하고 연대기를 읽기 좋게 덤프한다.
    /// placement 계산은 ChronicleSimExport와 동일한 *순수 함수 미러*(AR 의존 제거)를 쓴다.
    /// ※ RaceSettlementPlacer.TryPlace 로직이 바뀌면 아래 BuildPlacements 미러도 같이 고칠 것.
    /// 설계 검증 마일스톤: 다종족 제국 형성 + 단일 제국 비수렴 + "읽을 만한 역사".
    /// </summary>
    public static class ChronicleDump
    {
        private const int ChunkSize = 8;
        private const int MinChunk = -98;
        private const int MaxChunk = 98;
        private const int DefaultSeed = 12345;

        [MenuItem("ARPG/Chronicle Sim/Dump Chronicle (seed 12345)", false, 410)]
        public static void DumpDefault() => DumpSeed(DefaultSeed);

        [MenuItem("ARPG/Chronicle Sim/Determinism Check (seed 12345 x2)", false, 411)]
        public static void DeterminismCheck()
        {
            ChronicleSaveData c1 = SettlementChronicleSimulator.Simulate(DefaultSeed, BuildPlacements(DefaultSeed));
            ChronicleSaveData c2 = SettlementChronicleSimulator.Simulate(DefaultSeed, BuildPlacements(DefaultSeed));
            bool same = c1.Events.Count == c2.Events.Count && Signature(c1) == Signature(c2);
            Debug.Log($"[ChronicleDump] 결정론 {(same ? "OK (동일)" : "FAIL (다름!)")} — run1 {c1.Events.Count}건 / run2 {c2.Events.Count}건");
        }

        private static void DumpSeed(int seed)
        {
            List<RaceSettlementPlacer.SettlementPlacement> placements = BuildPlacements(seed);
            ChronicleSaveData c = SettlementChronicleSimulator.Simulate(seed, placements);

            // settlementId → race 조회용
            var raceById = new Dictionary<int, GlobalEnum.RaceType>();
            int alive = 0, conquered = 0, merged = 0, ruined = 0;
            for (int i = 0; i < c.Settlements.Count; i++)
            {
                SettlementSnapshot s = c.Settlements[i];
                raceById[s.SettlementId] = s.Race;
                switch (s.Fate)
                {
                    case SettlementFate.Alive: alive++; break;
                    case SettlementFate.Conquered: conquered++; break;
                    case SettlementFate.Merged: merged++; break;
                    case SettlementFate.Ruined: ruined++; break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"========== Chronicle Dump (seed {seed}) ==========");
            sb.AppendLine($"정착지 {c.Settlements.Count} · 사건 {c.Events.Count} · 왕국 {c.Kingdoms.Count} · {c.SimulatedYears}년");
            sb.AppendLine($"운명: Alive {alive} / Conquered {conquered} / Merged {merged} / Ruined {ruined}");

            // ── 왕국 (다종족 제국 / 단일 제국 수렴 검증) ──
            int totalActive = alive + conquered;
            int largestKingdom = 0;
            int multiRaceKingdoms = 0;
            sb.AppendLine("\n--- 왕국 ---");
            for (int i = 0; i < c.Kingdoms.Count; i++)
            {
                KingdomData k = c.Kingdoms[i];
                int foreignMembers = 0;
                for (int m = 0; m < k.MemberSettlementIds.Count; m++)
                {
                    if (raceById.TryGetValue(k.MemberSettlementIds[m], out var r) && r != k.FoundingRace)
                        foreignMembers++;
                }
                if (foreignMembers > 0) multiRaceKingdoms++;
                if (k.MemberSettlementIds.Count > largestKingdom) largestKingdom = k.MemberSettlementIds.Count;
                sb.AppendLine($"  [{k.KingdomId}] {k.Name} (건국 {k.FoundingRace}) — 멤버 {k.MemberSettlementIds.Count}" +
                              (foreignMembers > 0 ? $", 타종족 {foreignMembers} ★다종족" : ""));
            }

            double largestPct = totalActive > 0 ? 100.0 * largestKingdom / totalActive : 0;
            sb.AppendLine("\n--- 검증 지표 ---");
            sb.AppendLine($"다종족 제국 수: {multiRaceKingdoms} (목표 ≥1)");
            sb.AppendLine($"최대 왕국 점유율: {largestPct:F1}% of 활성 {totalActive} (목표 <40% = 단일 제국 비수렴)");

            // ── 사건 타임라인 (앞 40건) ──
            sb.AppendLine("\n--- 사건 타임라인 (앞 40건) ---");
            int show = Mathf.Min(40, c.Events.Count);
            for (int i = 0; i < show; i++)
            {
                ChronicleEvent e = c.Events[i];
                string subj = RaceTag(raceById, e.SubjectSettlementId);
                string obj = e.ObjectSettlementId >= 0 ? " → " + RaceTag(raceById, e.ObjectSettlementId) : "";
                sb.AppendLine($"  {e.Year,3}년 {e.Type,-12} S{e.SubjectSettlementId}{subj}{obj}");
            }

            Debug.Log(sb.ToString());
        }

        private static string RaceTag(Dictionary<int, GlobalEnum.RaceType> raceById, int id)
            => raceById.TryGetValue(id, out var r) ? $"({r})" : "";

        // 사건 시퀀스 시그니처 (결정론 비교용)
        private static int Signature(ChronicleSaveData c)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < c.Events.Count; i++)
                {
                    ChronicleEvent e = c.Events[i];
                    h = h * 31 + e.Year;
                    h = h * 31 + (int)e.Type;
                    h = h * 31 + e.SubjectSettlementId;
                    h = h * 31 + e.ObjectSettlementId;
                }
                return h;
            }
        }

        // ── placement 순수 미러 (ChronicleSimExport.TryPlaceMirror와 동일 로직, 실제 struct 출력) ──
        private static List<RaceSettlementPlacer.SettlementPlacement> BuildPlacements(int mapSeed)
        {
            var results = new List<RaceSettlementPlacer.SettlementPlacement>();
            int minRegionX = MinChunk >> 2, maxRegionX = MaxChunk >> 2;
            int minRegionY = MinChunk >> 2, maxRegionY = MaxChunk >> 2;

            for (int rx = minRegionX; rx <= maxRegionX; rx++)
            {
                for (int ry = minRegionY; ry <= maxRegionY; ry++)
                {
                    if (TryPlaceMirror(new Vector2Int(rx, ry), mapSeed, out RaceSettlementPlacer.SettlementPlacement p))
                        results.Add(p);
                }
            }
            return results;
        }

        private static bool TryPlaceMirror(Vector2Int regionCoord, int mapSeed,
            out RaceSettlementPlacer.SettlementPlacement placement)
        {
            placement = default;

            RegionData rd = RegionData.Compute(regionCoord, ChunkSize, mapSeed);
            GlobalEnum.BiomeType dominant = rd.DominantBiome;

            ulong settlementHash = DerivedSeed.Derive(
                unchecked((int)((uint)regionCoord.x ^ ((uint)regionCoord.y << 16) ^ (uint)mapSeed)),
                DerivedSeed.ChannelSettlement);

            GlobalEnum.RaceType? raceOpt = RaceBiomeTable.PickRace(dominant, settlementHash);
            if (raceOpt.HasValue == false)
                return false;
            GlobalEnum.RaceType race = raceOpt.Value;

            Vector2Int regionChunkOrigin = RegionCoord.RegionChunkOrigin(regionCoord);
            var chunkCandidates = new List<Vector2Int>();
            for (int cx = 0; cx < RegionCoord.CHUNKS_PER_REGION; cx++)
            {
                for (int cy = 0; cy < RegionCoord.CHUNKS_PER_REGION; cy++)
                {
                    Vector2Int chunkCoord = new(regionChunkOrigin.x + cx, regionChunkOrigin.y + cy);
                    GlobalEnum.BiomeType chunkBiome =
                        BiomeNoise.ComputeChunkBiome(chunkCoord.x, chunkCoord.y, ChunkSize, mapSeed);
                    if (RaceBiomeTable.IsCompatible(chunkBiome, race))
                        chunkCandidates.Add(chunkCoord);
                }
            }
            if (chunkCandidates.Count == 0)
                return false;

            int chunkIdx = (int)(settlementHash % (ulong)chunkCandidates.Count);
            Vector2Int chosenChunk = chunkCandidates[chunkIdx];
            Vector2Int worldCoord = DeterministicCoord.ChunkCenter(chosenChunk, ChunkSize);
            GlobalEnum.BiomeType chosenBiome = BiomeNoise.ComputeChunkBiome(chosenChunk.x, chosenChunk.y, ChunkSize, mapSeed);

            placement = new RaceSettlementPlacer.SettlementPlacement
            {
                RegionCoord = regionCoord,
                ChunkCoord = chosenChunk,
                WorldCoord = worldCoord,
                Race = race,
                Size = GlobalEnum.SettlementSize.Village,
                ChunkBiome = chosenBiome,
                RegionDominantBiome = dominant,
            };
            return true;
        }
    }
}
