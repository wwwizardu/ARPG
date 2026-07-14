#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ARPG.Data;
using ARPG.History;
using ARPG.Map;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// VILLAGE_REWORK_PLAN V0 전용 진단. 현재 프로젝트의 Library를 사용하는 Editor에서만 실행한다.
    /// 결과는 프로젝트 Temp에 기록하며 Asset/세이브 데이터는 변경하지 않는다.
    /// </summary>
    public static class VillageV0Diagnostics
    {
        private const int ChunkSize = 8;
        private const int MinChunk = -98;
        private const int MaxChunk = 98;
        private const string OutputPath = "Temp/VILLAGE_V0_RUNTIME_DUMP.md";

        private static readonly int[] _seeds = { 12345, 1001, 1002 };

        [MenuItem("ARPG/Village/Run V0 Diagnostics", false, 420)]
        public static void Run()
        {
            DiagnosticContext? context = null;
            try
            {
                context = DiagnosticContext.Create();
                StringBuilder report = new StringBuilder();
                report.AppendLine("# Village V0 Runtime Dump");
                report.AppendLine();
                report.AppendLine($"- Unity: {Application.unityVersion}");
                report.AppendLine($"- Generated UTC: {DateTime.UtcNow:O}");
                report.AppendLine("- Seeds: 12345, 1001, 1002");

                List<int> appliedOperationalRawPopulations = new List<int>();
                for (int i = 0; i < _seeds.Length; i++)
                {
                    AnalyzeSeed(_seeds[i], report,
                        appliedOperationalRawPopulations);
                }

                AppendRawPopulationThresholds(appliedOperationalRawPopulations, report);
                AppendParity(report);
                File.WriteAllText(OutputPath, report.ToString());
                Debug.Log($"[VillageV0Diagnostics] 완료: {OutputPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[VillageV0Diagnostics] 실패: {exception}");
                throw;
            }
            finally
            {
                context?.Dispose();
            }
        }

        private static void AnalyzeSeed(int seed, StringBuilder report,
            List<int> appliedOperationalRawPopulations)
        {
            List<RaceSettlementPlacer.SettlementPlacement> placements =
                BuildPlacements(seed);
            List<RaceSettlementPlacer.SettlementPlacement> repeated =
                BuildPlacements(seed);
            bool placementDeterministic = PlacementsEqual(placements, repeated);
            PlacementFilterResult placementFilter = FilterAppliedPlacements(placements);

            ChronicleSaveData chronicle = SettlementChronicleSimulator.Simulate(seed,
                placements);
            SettlementLiveState[] live = CreateLiveStates(chronicle);
            bool planDeterministic =
                VillageDevelopmentBootstrap.ValidatePlanReproducibility(live, seed,
                    out string planDifferences);
            VillageBootstrapPlan[] plans = VillageDevelopmentBootstrap.CreatePlans(live,
                seed);

            Dictionary<int, int> economicPopulationCounts = new Dictionary<int, int>();
            Dictionary<VillageStage, int> proposedStageCounts =
                new Dictionary<VillageStage, int>();
            Dictionary<SettlementFate, int> fateCounts =
                new Dictionary<SettlementFate, int>();
            int operational = 0;
            int appliedOperational = 0;
            int rawMin = int.MaxValue;
            int rawMax = int.MinValue;
            long rawTotal = 0;

            for (int i = 0; i < live.Length; i++)
            {
                SettlementLiveState settlement = live[i];
                Increment(fateCounts, settlement.Fate);
                if (VillageDevelopmentBootstrap.IsOperationalFate(settlement.Fate)
                    == false)
                    continue;

                int raw = Mathf.RoundToInt(settlement.Pop);
                int economic = VillageManager.QuantizeAbstractPopulation(raw);
                operational++;
                rawMin = Mathf.Min(rawMin, raw);
                rawMax = Mathf.Max(rawMax, raw);
                rawTotal += raw;
                Increment(economicPopulationCounts, economic);
                Increment(proposedStageCounts, GetProposedStage(economic));
                if (placementFilter.AppliedSettlementIds.Contains(
                        settlement.SettlementId))
                {
                    appliedOperational++;
                    appliedOperationalRawPopulations.Add(raw);
                }
            }

            report.AppendLine();
            report.AppendLine($"## Seed {seed}");
            report.AppendLine();
            report.AppendLine($"- placements={placements.Count}, applied={placementFilter.AppliedSettlementIds.Count}, outOfBounds={placementFilter.SkippedOutOfBounds}, overlap={placementFilter.SkippedOverlap}");
            report.AppendLine($"- placement deterministic={placementDeterministic}, bootstrap plan deterministic={planDeterministic} {planDifferences}");
            report.AppendLine($"- chronicle={chronicle.Settlements.Count}, operational={operational}, raw population min/max/avg={rawMin}/{rawMax}/{(operational > 0 ? rawTotal / (double)operational : 0d):F1}");
            report.AppendLine($"- applied operational={appliedOperational}");
            report.AppendLine($"- fate: {FormatDictionary(fateCounts)}");
            report.AppendLine($"- economic population: {FormatDictionary(economicPopulationCounts)}");
            report.AppendLine($"- proposed stage (1/3/8/15/22): {FormatDictionary(proposedStageCounts)}");

            AppendBootstrap(seed, plans, placementFilter.AppliedSettlementIds, report);
        }

        private static void AppendRawPopulationThresholds(List<int> populations,
            StringBuilder report)
        {
            populations.Sort();
            report.AppendLine();
            report.AppendLine("## Applied Operational Raw Population Thresholds");
            report.AppendLine();
            report.AppendLine($"- sample={populations.Count}");
            if (populations.Count == 0)
                return;

            int hamlet = GetTopFractionThreshold(populations, 0.90f);
            int village = GetTopFractionThreshold(populations, 0.70f);
            int town = GetTopFractionThreshold(populations, 0.35f);
            int cityTop10 = GetTopFractionThreshold(populations, 0.10f);
            int cityTop5 = GetTopFractionThreshold(populations, 0.05f);
            report.AppendLine($"- percentile cutoffs: P10={hamlet}, P30={village}, P50={GetPercentile(populations, 0.50f)}, P65={town}, P75={GetPercentile(populations, 0.75f)}, P90={cityTop10}, P95={cityTop5}");
            AppendStageCandidate("City target top 10%", populations, hamlet,
                village, town, cityTop10, report);
            AppendStageCandidate("City target top 5%", populations, hamlet,
                village, town, cityTop5, report);
        }

        private static void AppendStageCandidate(string name, List<int> populations,
            int hamletThreshold, int villageThreshold, int townThreshold,
            int cityThreshold, StringBuilder report)
        {
            Dictionary<VillageStage, int> counts =
                new Dictionary<VillageStage, int>();
            for (int i = 0; i < populations.Count; i++)
            {
                int population = populations[i];
                VillageStage stage;
                if (population >= cityThreshold)
                    stage = VillageStage.City;
                else if (population >= townThreshold)
                    stage = VillageStage.Town;
                else if (population >= villageThreshold)
                    stage = VillageStage.Village;
                else if (population >= hamletThreshold)
                    stage = VillageStage.Hamlet;
                else
                    stage = VillageStage.Settlement;
                Increment(counts, stage);
            }

            report.AppendLine();
            report.AppendLine($"### {name}");
            report.AppendLine();
            report.AppendLine($"- thresholds: Settlement < {hamletThreshold}, Hamlet >= {hamletThreshold}, Village >= {villageThreshold}, Town >= {townThreshold}, City >= {cityThreshold}");
            report.AppendLine($"- actual distribution: {FormatStageDistribution(counts, populations.Count)}");
        }

        private static int GetTopFractionThreshold(List<int> sortedPopulations,
            float topFraction)
        {
            int targetCount = Mathf.CeilToInt(sortedPopulations.Count * topFraction);
            int index = Mathf.Clamp(sortedPopulations.Count - targetCount, 0,
                sortedPopulations.Count - 1);
            return sortedPopulations[index];
        }

        private static int GetPercentile(List<int> sortedPopulations,
            float percentile)
        {
            int rank = Mathf.CeilToInt(sortedPopulations.Count * percentile);
            int index = Mathf.Clamp(rank - 1, 0, sortedPopulations.Count - 1);
            return sortedPopulations[index];
        }

        private static string FormatStageDistribution(
            Dictionary<VillageStage, int> counts, int total)
        {
            Array stages = Enum.GetValues(typeof(VillageStage));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < stages.Length; i++)
            {
                VillageStage stage = (VillageStage)stages.GetValue(i);
                counts.TryGetValue(stage, out int count);
                if (i > 0)
                    builder.Append(", ");
                builder.Append(stage).Append('=').Append(count).Append(" (")
                    .Append((count * 100d / total).ToString("F1"))
                    .Append("%)");
            }
            return builder.ToString();
        }

        private static void AppendBootstrap(int seed, VillageBootstrapPlan[] plans,
            HashSet<int> appliedSettlementIds, StringBuilder report)
        {
            int finalized = 0;
            int failures = 0;
            int activeTasks = 0;
            Dictionary<VillageStage, int> stageCounts =
                new Dictionary<VillageStage, int>();
            Dictionary<int, int> buildingCountCounts = new Dictionary<int, int>();
            Dictionary<string, int> completionSequences =
                new Dictionary<string, int>();

            for (int i = 0; i < plans.Length; i++)
            {
                VillageBootstrapPlan plan = plans[i];
                if (plan.IsEligible == false
                    || appliedSettlementIds.Contains(plan.SettlementId) == false)
                    continue;

                VillageData village = new VillageData(plan.SettlementId, Vector2.zero)
                {
                    SettlementId = plan.SettlementId,
                    LastConstructionSimGameTime = 0f,
                    LastConstructionTierCheckGameTime = 0f,
                };
                VillageBootstrapAdvanceResult advance =
                    VillageDevelopmentBootstrap.AdvanceBootstrapSlice(village, plan,
                        VillageAbstractConstruction.DefaultGeneralEventBudget,
                        VillageAbstractConstruction.DefaultWallEventBudget);
                int slices = 1;
                while (advance.IsComplete == false && advance.AdvanceResult != null
                    && (advance.AdvanceResult.Status
                            == VillageAbstractAdvanceStatus.GeneralBudgetExhausted
                        || advance.AdvanceResult.Status
                            == VillageAbstractAdvanceStatus.WallBudgetExhausted)
                    && slices < 4096)
                {
                    advance = VillageDevelopmentBootstrap.AdvanceBootstrapSlice(village,
                        plan, VillageAbstractConstruction.DefaultGeneralEventBudget,
                        VillageAbstractConstruction.DefaultWallEventBudget);
                    slices++;
                }

                bool success = advance.IsComplete
                    && VillageDevelopmentBootstrap.FinalizeBootstrap(village, plan, 0f);
                if (success == false)
                {
                    failures++;
                    continue;
                }

                finalized++;
                activeTasks += village.ActiveBuildTasks.Count;
                Increment(stageCounts, village.Stage);
                Increment(buildingCountCounts, village.PlacedObjectTypeIds.Count);
                string sequence = village.PlacedObjectTypeIds.Count == 0
                    ? "(none)"
                    : string.Join(",", village.PlacedObjectTypeIds);
                Increment(completionSequences,
                    $"{village.Stage}|{village.PlacedObjectTypeIds.Count}|{sequence}");
            }

            report.AppendLine($"- bootstrap seed={seed}: finalized={finalized}, failures={failures}, activeTasks={activeTasks}");
            report.AppendLine($"- bootstrap stage: {FormatDictionary(stageCounts)}");
            report.AppendLine($"- bootstrap completed-building count: {FormatDictionary(buildingCountCounts)}");
            report.AppendLine("- bootstrap completion sequences (top 12):");
            List<KeyValuePair<string, int>> ordered = completionSequences
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(12)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
                report.AppendLine($"  - {ordered[i].Value} × {ordered[i].Key}");
        }

        private static void AppendParity(StringBuilder report)
        {
            int[] populations = { 1, 3, 8, 15, 22, 25 };
            report.AppendLine();
            report.AppendLine("## VillageSimulationParity");
            report.AppendLine();
            for (int i = 0; i < populations.Length; i++)
            {
                int population = populations[i];
                GetCurrentDevelopmentBand(population, out VillageStage maxStage,
                    out float targetHours, out int buildingLimit);
                VillageAbstractAdvanceOptions unlimited = CreateOptions(maxStage,
                    buildingLimit, int.MaxValue, int.MaxValue);
                bool hourlyFast = VillageSimulationParity.AdvanceAndCompare(
                    CreateVillage(population), CreateVillage(population), targetHours,
                    unlimited, out string hourlyDifferences);

                VillageAbstractAdvanceOptions sliced = CreateOptions(maxStage,
                    buildingLimit, 1, 1);
                bool slicedResult = VillageSimulationParity.AdvanceSlicedAndCompare(
                    CreateVillage(population), CreateVillage(population), targetHours,
                    sliced, 4096, out string slicedDifferences);
                report.AppendLine($"- pop={population}, hours={targetHours:F0}, hourlyFast={hourlyFast} {hourlyDifferences}, sliced={slicedResult} {slicedDifferences}");
            }
        }

        private static VillageAbstractAdvanceOptions CreateOptions(VillageStage maxStage,
            int buildingLimit, int generalBudget, int wallBudget)
        {
            return new VillageAbstractAdvanceOptions
            {
                GeneralEventBudget = generalBudget,
                WallEventBudget = wallBudget,
                MaxGeneralBuildings = buildingLimit,
                MaxStage = maxStage,
                MaxWallCompletionRatio = 1f,
                UseBootstrapWallLimit = true,
                BootstrapMaturity = 0.5f,
                BootstrapWallSeedFactor = 0.5f,
                IsOperational = true,
            };
        }

        private static VillageData CreateVillage(int population)
        {
            return new VillageData(0, Vector2.zero)
            {
                Population = population,
                LastConstructionSimGameTime = 0f,
                LastConstructionTierCheckGameTime = 0f,
            };
        }

        private static void GetCurrentDevelopmentBand(int population,
            out VillageStage maxStage, out float targetHours, out int buildingLimit)
        {
            if (population <= 2)
            {
                maxStage = VillageStage.Settlement;
                targetHours = 36f;
                buildingLimit = 3;
                return;
            }
            if (population <= 7)
            {
                maxStage = VillageStage.Hamlet;
                targetHours = 96f;
                buildingLimit = 8;
                return;
            }
            if (population <= 14)
            {
                maxStage = VillageStage.Village;
                targetHours = 216f;
                buildingLimit = 16;
                return;
            }

            maxStage = VillageStage.Town;
            targetHours = 408f;
            buildingLimit = 28;
        }

        private static VillageStage GetProposedStage(int population)
        {
            if (population >= 22)
                return VillageStage.City;
            if (population >= 15)
                return VillageStage.Town;
            if (population >= 8)
                return VillageStage.Village;
            if (population >= 3)
                return VillageStage.Hamlet;
            return VillageStage.Settlement;
        }

        private static SettlementLiveState[] CreateLiveStates(ChronicleSaveData chronicle)
        {
            SettlementLiveState[] result =
                new SettlementLiveState[chronicle.Settlements.Count];
            for (int i = 0; i < chronicle.Settlements.Count; i++)
            {
                SettlementSnapshot snapshot = chronicle.Settlements[i];
                result[i] = new SettlementLiveState
                {
                    SettlementId = snapshot.SettlementId,
                    Race = snapshot.Race,
                    Biome = snapshot.Biome,
                    WorldCoordX = snapshot.WorldCoordX,
                    WorldCoordY = snapshot.WorldCoordY,
                    Pop = snapshot.Population,
                    KingdomId = snapshot.KingdomId,
                    Fate = snapshot.Fate,
                    FoundedYear = snapshot.FoundedYear,
                    VillageId = -1,
                };
            }
            return result;
        }

        private static List<RaceSettlementPlacer.SettlementPlacement> BuildPlacements(
            int mapSeed)
        {
            List<RaceSettlementPlacer.SettlementPlacement> results =
                new List<RaceSettlementPlacer.SettlementPlacement>();
            int minRegionX = MinChunk >> 2;
            int maxRegionX = MaxChunk >> 2;
            int minRegionY = MinChunk >> 2;
            int maxRegionY = MaxChunk >> 2;

            for (int blockX = minRegionX; blockX <= maxRegionX;
                blockX += RaceSettlementPlacer.REGIONS_PER_SETTLEMENT_BLOCK)
            {
                for (int blockY = minRegionY; blockY <= maxRegionY;
                    blockY += RaceSettlementPlacer.REGIONS_PER_SETTLEMENT_BLOCK)
                {
                    int width = Mathf.Min(
                        RaceSettlementPlacer.REGIONS_PER_SETTLEMENT_BLOCK,
                        maxRegionX - blockX + 1);
                    int height = Mathf.Min(
                        RaceSettlementPlacer.REGIONS_PER_SETTLEMENT_BLOCK,
                        maxRegionY - blockY + 1);
                    int regionCount = width * height;
                    int startSlot = RaceSettlementPlacer.GetSettlementBlockStartSlot(
                        blockX, blockY, mapSeed, regionCount);
                    int targetCount = Mathf.Min(
                        RaceSettlementPlacer.SETTLEMENTS_PER_BLOCK, regionCount);
                    int added = 0;

                    for (int attempt = 0; attempt < regionCount; attempt++)
                    {
                        int slot = RaceSettlementPlacer.GetSettlementBlockSlot(startSlot,
                            attempt, regionCount);
                        Vector2Int regionCoord = new Vector2Int(
                            blockX + slot / height, blockY + slot % height);
                        if (TryPlaceMirror(regionCoord, mapSeed,
                                out RaceSettlementPlacer.SettlementPlacement placement)
                            == false)
                            continue;
                        results.Add(placement);
                        added++;
                        if (added >= targetCount)
                            break;
                    }
                }
            }
            return results;
        }

        private static bool TryPlaceMirror(Vector2Int regionCoord, int mapSeed,
            out RaceSettlementPlacer.SettlementPlacement placement)
        {
            placement = default;
            RegionData region = RegionData.Compute(regionCoord, ChunkSize, mapSeed);
            GlobalEnum.BiomeType dominant = region.DominantBiome;
            ulong settlementHash = DerivedSeed.Derive(
                unchecked((int)((uint)regionCoord.x
                    ^ ((uint)regionCoord.y << 16)
                    ^ (uint)mapSeed)),
                DerivedSeed.ChannelSettlement);
            GlobalEnum.RaceType? raceOption = RaceBiomeTable.PickRace(dominant,
                settlementHash);
            if (raceOption.HasValue == false)
                return false;

            GlobalEnum.RaceType race = raceOption.Value;
            Vector2Int origin = RegionCoord.RegionChunkOrigin(regionCoord);
            List<Vector2Int> candidates = new List<Vector2Int>();
            for (int x = 0; x < RegionCoord.CHUNKS_PER_REGION; x++)
            {
                for (int y = 0; y < RegionCoord.CHUNKS_PER_REGION; y++)
                {
                    Vector2Int chunk = new Vector2Int(origin.x + x, origin.y + y);
                    if (chunk.x < MinChunk || chunk.x > MaxChunk
                        || chunk.y < MinChunk || chunk.y > MaxChunk)
                        continue;
                    GlobalEnum.BiomeType biome = BiomeNoise.ComputeChunkBiome(
                        chunk.x, chunk.y, ChunkSize, mapSeed);
                    if (RaceBiomeTable.IsCompatible(biome, race))
                        candidates.Add(chunk);
                }
            }
            if (candidates.Count == 0)
                return false;

            Vector2Int chosen = candidates[
                (int)(settlementHash % (ulong)candidates.Count)];
            placement = new RaceSettlementPlacer.SettlementPlacement
            {
                RegionCoord = regionCoord,
                ChunkCoord = chosen,
                WorldCoord = DeterministicCoord.ChunkCenter(chosen, ChunkSize),
                Race = race,
                Size = GlobalEnum.SettlementSize.Village,
                ChunkBiome = BiomeNoise.ComputeChunkBiome(chosen.x, chosen.y,
                    ChunkSize, mapSeed),
                RegionDominantBiome = dominant,
            };
            return true;
        }

        private static PlacementFilterResult FilterAppliedPlacements(
            List<RaceSettlementPlacer.SettlementPlacement> placements)
        {
            PlacementFilterResult result = new PlacementFilterResult();
            List<RectInt> occupied = new List<RectInt>();
            Dictionary<GlobalEnum.RaceType, Vector2Int> sizes =
                LoadRaceBlueprintSizes();
            MapFileData baseTown = ReadMapFile("BaseTown.bytes");
            occupied.Add(new RectInt(baseTown.StartPosition.x, baseTown.StartPosition.y,
                baseTown.Width, baseTown.Height));
            int minWorld = MinChunk * ChunkSize;
            int maxWorldExclusive = (MaxChunk + 1) * ChunkSize;

            for (int i = 0; i < placements.Count; i++)
            {
                RaceSettlementPlacer.SettlementPlacement placement = placements[i];
                Vector2Int size = sizes[placement.Race];
                RectInt candidate = new RectInt(
                    placement.WorldCoord.x - size.x / 2,
                    placement.WorldCoord.y - size.y / 2,
                    size.x, size.y);
                if (candidate.xMin < minWorld || candidate.yMin < minWorld
                    || candidate.xMax > maxWorldExclusive
                    || candidate.yMax > maxWorldExclusive)
                {
                    result.SkippedOutOfBounds++;
                    continue;
                }

                bool overlaps = false;
                for (int j = 0; j < occupied.Count; j++)
                {
                    if (candidate.Overlaps(occupied[j]))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps)
                {
                    result.SkippedOverlap++;
                    continue;
                }

                occupied.Add(candidate);
                result.AppliedSettlementIds.Add(i);
            }
            return result;
        }

        private static Dictionary<GlobalEnum.RaceType, Vector2Int>
            LoadRaceBlueprintSizes()
        {
            Dictionary<GlobalEnum.RaceType, Vector2Int> result =
                new Dictionary<GlobalEnum.RaceType, Vector2Int>();
            Array values = Enum.GetValues(typeof(GlobalEnum.RaceType));
            for (int i = 0; i < values.Length; i++)
            {
                GlobalEnum.RaceType race = (GlobalEnum.RaceType)values.GetValue(i);
                string path = Path.Combine(Application.dataPath, "_BinaryData",
                    "TilemapData", $"BaseTown_{race}.bytes");
                if (File.Exists(path) == false)
                    continue;
                MapFileData blueprint = ReadMapFile($"BaseTown_{race}.bytes");
                result[race] = new Vector2Int(blueprint.Width, blueprint.Height);
            }
            return result;
        }

        private static MapFileData ReadMapFile(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "_BinaryData",
                "TilemapData", fileName);
            using FileStream stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);
            return MapFileData.ReadFromBinary(reader);
        }

        private static bool PlacementsEqual(
            List<RaceSettlementPlacer.SettlementPlacement> first,
            List<RaceSettlementPlacer.SettlementPlacement> second)
        {
            if (first.Count != second.Count)
                return false;
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i].RegionCoord != second[i].RegionCoord
                    || first[i].ChunkCoord != second[i].ChunkCoord
                    || first[i].WorldCoord != second[i].WorldCoord
                    || first[i].Race != second[i].Race
                    || first[i].ChunkBiome != second[i].ChunkBiome
                    || first[i].RegionDominantBiome
                        != second[i].RegionDominantBiome)
                    return false;
            }
            return true;
        }

        private static string FormatDictionary<TKey>(Dictionary<TKey, int> values)
            where TKey : notnull
        {
            List<KeyValuePair<TKey, int>> ordered = values
                .OrderBy(pair => pair.Key)
                .ToList();
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(ordered[i].Key).Append('=').Append(ordered[i].Value);
            }
            return builder.ToString();
        }

        private static void Increment<TKey>(Dictionary<TKey, int> values, TKey key)
            where TKey : notnull
        {
            values.TryGetValue(key, out int current);
            values[key] = current + 1;
        }

        private sealed class PlacementFilterResult
        {
            public readonly HashSet<int> AppliedSettlementIds = new HashSet<int>();
            public int SkippedOutOfBounds;
            public int SkippedOverlap;
        }

        private sealed class DiagnosticContext : IDisposable
        {
            private readonly FieldInfo _instanceField;
            private readonly AR? _previousInstance;
            private readonly FieldInfo _buildableTableField;
            private readonly FieldInfo _stageTableField;
            private readonly DataManager _data;
            private readonly object? _previousBuildableTable;
            private readonly object? _previousStageTable;

            private DiagnosticContext(FieldInfo instanceField, AR? previousInstance,
                FieldInfo buildableTableField, FieldInfo stageTableField,
                DataManager data, object? previousBuildableTable,
                object? previousStageTable)
            {
                _instanceField = instanceField;
                _previousInstance = previousInstance;
                _buildableTableField = buildableTableField;
                _stageTableField = stageTableField;
                _data = data;
                _previousBuildableTable = previousBuildableTable;
                _previousStageTable = previousStageTable;
            }

            public static DiagnosticContext Create()
            {
                List<BuildableItemTable> buildables = ReadTable<BuildableItemTable>(
                    "BuildableItemTable.bytes");
                List<VillageStageTable> stages = ReadTable<VillageStageTable>(
                    "VillageStageTable.bytes");
                FieldInfo instanceField = typeof(PrefabSingleton<AR>).GetField(
                    "_instance", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException("PrefabSingleton<AR>._instance");
                AR? previousInstance = instanceField.GetValue(null) as AR;
                AR ar = previousInstance != null
                    ? previousInstance
                    : LoadPrefabAR();
                DataManager data = ar.Data;
                FieldInfo buildableTableField = GetField(data,
                    "_buildableItemTable");
                FieldInfo stageTableField = GetField(data, "_villageStageTable");
                object? previousBuildableTable = buildableTableField.GetValue(data);
                object? previousStageTable = stageTableField.GetValue(data);
                buildableTableField.SetValue(data,
                    buildables.ToImmutableDictionary(table => table.Id));
                stageTableField.SetValue(data,
                    stages.ToImmutableDictionary(table => table.Id));
                instanceField.SetValue(null, ar);
                return new DiagnosticContext(instanceField, previousInstance,
                    buildableTableField, stageTableField, data,
                    previousBuildableTable, previousStageTable);
            }

            public void Dispose()
            {
                _instanceField.SetValue(null, _previousInstance);
                _buildableTableField.SetValue(_data, _previousBuildableTable);
                _stageTableField.SetValue(_data, _previousStageTable);
            }

            private static AR LoadPrefabAR()
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Resources/AR.prefab")
                    ?? throw new FileNotFoundException(
                        "AR prefab was not found.", "Assets/Resources/AR.prefab");
                return prefab.GetComponent<AR>()
                    ?? throw new MissingComponentException(
                        "Assets/Resources/AR.prefab has no AR component.");
            }

            private static List<T> ReadTable<T>(string fileName)
            {
                string path = Path.Combine(Application.dataPath, "_BinaryData",
                    "TableData", fileName);
                return JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path))
                    ?? new List<T>();
            }

            private static FieldInfo GetField(object target, string fieldName)
            {
                return target.GetType().GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(target.GetType().FullName,
                        fieldName);
            }
        }
    }
}
