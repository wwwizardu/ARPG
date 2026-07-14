#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.History;
using ARPG.Map;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// VILLAGE_REWORK_PLAN V2 전용 정적 진단. 세이브 파일과 월드 상태는 변경하지 않는다.
    /// 고정 인구·초기 Stage 계산·런타임 발전 동결을 검증하고, seed별 Profile/원본 역사 인구/
    /// 경제 인구/초기 Stage 분포를 덤프한다. materialize/fold-back·물리 NPC 경로는
    /// Unity 플레이 모드가 필요해 이 진단에서 실행하지 못하며 리포트에 제한으로 기록한다.
    /// </summary>
    public static class VillageV2FixedStateDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V2_FIXED_STATE_DUMP.md";
        private const int SampleSeed = 12345;

        [MenuItem("ARPG/Village/Run V2 Fixed State Diagnostics", false, 422)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();
            StringBuilder dump = new StringBuilder();

            ValidateRuntimeProgressionDisabled(passed, failed);
            ValidateInitialStageThresholds(passed, failed);
            ValidateMaxStageCap(passed, failed);
            ValidateInitialStateApplication(passed, failed);
            ValidateFrozenAdvance(passed, failed);
            ValidateNotOperationalClockAlignment(passed, failed);
            ValidatePopulationModelPreserved(passed, failed);
            ValidateSaveRoundTrip(passed, failed);
            ValidateSeedFixedState(passed, failed, dump);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V2 Fixed State Diagnostics");
            report.AppendLine();
            report.AppendLine($"- Unity: {Application.unityVersion}");
            report.AppendLine($"- Generated UTC: {DateTime.UtcNow:O}");
            report.AppendLine($"- RuntimeProgressionEnabled: {VillageProgressionRules.RuntimeProgressionEnabled}");
            report.AppendLine($"- Passed: {passed.Count}");
            report.AppendLine($"- Failed: {failed.Count}");
            report.AppendLine();
            report.AppendLine("## Passed");
            report.AppendLine();
            for (int i = 0; i < passed.Count; i++)
                report.AppendLine($"- {passed[i]}");
            report.AppendLine();
            report.AppendLine("## Failed");
            report.AppendLine();
            if (failed.Count == 0)
                report.AppendLine("- None");
            for (int i = 0; i < failed.Count; i++)
                report.AppendLine($"- {failed[i]}");
            report.AppendLine();
            report.Append(dump);
            report.AppendLine();
            report.AppendLine("## Play-mode-only (이 진단에서 검증 불가)");
            report.AppendLine();
            report.AppendLine("- 실제 청크 기반 materialize/fold-back 왕복에서의 고정 인구·Stage 유지");
            report.AppendLine("- 물리 NPC 사망·보충, Food 0, 건물 파괴가 고정 인구를 바꾸지 않는 실측");
            report.AppendLine("- 실제 세이브 파일 save/load 왕복 (여기서는 JSON 직렬화 왕복만 검증)");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Temp");
            File.WriteAllText(OutputPath, report.ToString());

            if (failed.Count > 0)
            {
                string message = string.Join(" | ", failed);
                Debug.LogError($"[VillageV2FixedStateDiagnostics] 실패: {message}");
                throw new InvalidOperationException(message);
            }
            Debug.Log($"[VillageV2FixedStateDiagnostics] 완료: {OutputPath}");
        }

        private static void ValidateRuntimeProgressionDisabled(List<string> passed,
            List<string> failed)
        {
            Check(VillageProgressionRules.RuntimeProgressionEnabled == false,
                "런타임 승격·자동 건설·벽 계획 스위치가 V2 정책대로 꺼져 있음",
                passed, failed);
        }

        private static void ValidateInitialStageThresholds(List<string> passed,
            List<string> failed)
        {
            bool ok = true;
            ok &= Calc(0) == VillageStage.Settlement;
            ok &= Calc(1) == VillageStage.Settlement;
            ok &= Calc(596) == VillageStage.Settlement;
            ok &= Calc(597) == VillageStage.Hamlet;
            ok &= Calc(1678) == VillageStage.Hamlet;
            ok &= Calc(1679) == VillageStage.Village;
            ok &= Calc(2591) == VillageStage.Village;
            ok &= Calc(2592) == VillageStage.Town;
            ok &= Calc(3418) == VillageStage.Town;
            ok &= Calc(3419) == VillageStage.City;
            ok &= Calc(int.MaxValue) == VillageStage.City;
            Check(ok,
                "V0 확정 원본 인구 임계값(597/1,679/2,592/3,419) 경계값이 정확히 적용됨",
                passed, failed);
        }

        private static void ValidateMaxStageCap(List<string> passed, List<string> failed)
        {
            bool ok = true;
            ok &= VillageProgressionRules.CalculateInitialStage(3419, VillageStage.Town)
                == VillageStage.Town;
            ok &= VillageProgressionRules.CalculateInitialStage(3419,
                VillageStage.Settlement) == VillageStage.Settlement;
            ok &= VillageProgressionRules.CalculateInitialStage(2592, VillageStage.City)
                == VillageStage.Town;
            ok &= VillageProgressionRules.CalculateInitialStage(100, VillageStage.Town)
                == VillageStage.Settlement;
            Check(ok, "VillageProfile.MaxStage 상한보다 높은 초기 Stage가 적용되지 않음",
                passed, failed);
        }

        private static void ValidateInitialStateApplication(List<string> passed,
            List<string> failed)
        {
            // 자원 0, 건물 0, Housing/Food/Set/Civic/Shop 게이트 전부 미충족인 빈 마을에도
            // 인구 기반 초기 Stage가 그대로 적용되는지 — 계산 입력이 인구·MaxStage뿐임을 검증.
            VillageData village = new VillageData(1, new Vector2(100f, 50f))
            {
                SettlementId = 5,
            };
            VillageStage stage = VillageProgressionRules.CalculateInitialStage(3419,
                VillageStage.City);
            VillageDevelopmentBootstrap.ApplyFixedInitialState(village, stage, 24, 7f);

            bool ok = village.Stage == VillageStage.City
                && village.InitialDevelopmentApplied == true
                && Mathf.Approximately(village.LastConstructionSimGameTime, 7f)
                && Mathf.Approximately(village.LastConstructionTierCheckGameTime, 7f)
                && village.BoundsX == 100 - 24
                && village.BoundsY == 50 - 24
                && village.BoundsW == 48
                && village.BoundsH == 48
                && village.ActiveBuildTasks.Count == 0
                && village.PlacedObjectTypeIds.Count == 0
                && village.WallPlanRequested == false;
            Check(ok,
                "자원·건물·게이트 0 상태에서도 초기 Stage·Bounds·clock이 진행 task 없이 직접 적용됨",
                passed, failed);

            VillageData noRadius = new VillageData(2, new Vector2(10f, 10f));
            VillageDevelopmentBootstrap.ApplyFixedInitialState(noRadius,
                VillageStage.Hamlet, 0, 3f);
            Check(noRadius.Stage == VillageStage.Hamlet && noRadius.BoundsW == 0,
                "BoundsRadius를 얻지 못한 경우 Stage만 적용하고 Bounds는 실체화 시 산출로 위임함",
                passed, failed);
        }

        private static void ValidateFrozenAdvance(List<string> passed, List<string> failed)
        {
            VillageData village = CreateFrozenSample();
            int taskCountBefore = village.ActiveBuildTasks.Count;
            float taskHoursBefore = village.ActiveBuildTasks[0].AccumulatedHours;

            VillageAbstractAdvanceResult first = VillageAbstractConstruction
                .AdvanceAbstractConstruction(village, 10000f, CreateFrozenOptions());
            bool frozenAfterLongTime = first.IsCaughtUp
                && village.Stage == VillageStage.Village
                && village.PlacedObjectTypeIds.Count == 1
                && village.ActiveBuildTasks.Count == taskCountBefore
                && Mathf.Approximately(village.ActiveBuildTasks[0].AccumulatedHours,
                    taskHoursBefore)
                && village.WallSegments.Count == 0
                && Mathf.Approximately(village.LastConstructionSimGameTime, 10000f)
                && Mathf.Approximately(village.LastConstructionTierCheckGameTime, 10000f)
                && first.GeneralBuildsCompleted == 0
                && first.WallSegmentsCompleted == 0
                && first.Promotions.Count == 0;
            Check(frozenAfterLongTime,
                "AllowDevelopment=false 추상 advance 10,000h에서 Stage·시설·task가 동결되고 clock만 정렬됨",
                passed, failed);

            int foodAfter = VillageResourceSimulation.GetAmount(village,
                GlobalEnum.ItemType.Food);
            int woodAfter = VillageResourceSimulation.GetAmount(village,
                GlobalEnum.ItemType.Wood);
            Check(foodAfter > 0 && woodAfter > 0,
                "동결 중에도 경제 인구 기반 자원 생산 catch-up은 계속됨(생산량 계산 전용 인구 유지)",
                passed, failed);

            VillageAbstractAdvanceResult second = VillageAbstractConstruction
                .AdvanceAbstractConstruction(village, 20000f, CreateFrozenOptions());
            VillageAbstractAdvanceResult third = VillageAbstractConstruction
                .AdvanceAbstractConstruction(village, 20000f, CreateFrozenOptions());
            bool repeatable = second.IsCaughtUp && third.IsCaughtUp
                && village.Stage == VillageStage.Village
                && village.ActiveBuildTasks.Count == taskCountBefore
                && Mathf.Approximately(village.ActiveBuildTasks[0].AccumulatedHours,
                    taskHoursBefore)
                && village.PlacedObjectTypeIds.Count == 1
                && Mathf.Approximately(village.LastConstructionSimGameTime, 20000f);
            Check(repeatable,
                "동결 advance 반복(장기 경과 + 같은 시각 재호출)에도 Stage·시설·task 구성이 변하지 않음",
                passed, failed);
        }

        private static void ValidateNotOperationalClockAlignment(List<string> passed,
            List<string> failed)
        {
            VillageData village = CreateFrozenSample();
            VillageAbstractAdvanceOptions options = CreateFrozenOptions();
            options.IsOperational = false;
            VillageAbstractAdvanceResult result = VillageAbstractConstruction
                .AdvanceAbstractConstruction(village, 5000f, options);
            Check(result.Status == VillageAbstractAdvanceStatus.NotOperational
                && Mathf.Approximately(village.LastConstructionSimGameTime, 5000f)
                && Mathf.Approximately(village.LastConstructionTierCheckGameTime, 5000f)
                && village.PlacedObjectTypeIds.Count == 1,
                "비운영(Ruined 등) 마을도 clock만 현재 시각으로 정렬되고 상태는 보존됨",
                passed, failed);
        }

        private static void ValidatePopulationModelPreserved(List<string> passed,
            List<string> failed)
        {
            // V2는 호출만 중단하고 모델은 후속 인구 설계를 위해 보존한다 (VILLAGE_REWORK_PLAN §3.1).
            SettlementLiveState settlement = new SettlementLiveState
            {
                SettlementId = 0,
                Biome = GlobalEnum.BiomeType.Plains,
                Fate = SettlementFate.Alive,
                Pop = 100f,
            };
            float before = settlement.Pop;
            SettlementPopulationModel.Advance(ref settlement, 365);
            Check(settlement.Pop > before,
                "SettlementPopulationModel은 삭제되지 않고 직접 호출 시 여전히 동작함(후속 설계용 보존)",
                passed, failed);
        }

        private static void ValidateSaveRoundTrip(List<string> passed, List<string> failed)
        {
            VillageData source = new VillageData(11, new Vector2(1f, 2f))
            {
                SettlementId = 42,
                VillageProfileId = "village.dwarf.mountain.standard",
                Population = 25,
                FixedPopulation = -1,
                Stage = VillageStage.Town,
                InitialDevelopmentApplied = true,
                LastConstructionSimGameTime = 123.5f,
                LastConstructionTierCheckGameTime = 123.5f,
            };
            string json = JsonConvert.SerializeObject(source);
            VillageData? loaded = JsonConvert.DeserializeObject<VillageData>(json);
            Check(loaded != null
                && loaded.Stage == VillageStage.Town
                && loaded.Population == 25
                && loaded.InitialDevelopmentApplied == true
                && loaded.VillageProfileId == source.VillageProfileId
                && Mathf.Approximately(loaded.LastConstructionSimGameTime, 123.5f),
                "초기 Stage·경제 인구·1회 적용 플래그·clock이 JSON save/load 왕복에서 유지됨",
                passed, failed);

            SettlementLiveState live = new SettlementLiveState
            {
                SettlementId = 7,
                Pop = 2711f,
                LastSimGameTime = 456f,
                Fate = SettlementFate.Alive,
            };
            string liveJson = JsonConvert.SerializeObject(live);
            SettlementLiveState liveLoaded =
                JsonConvert.DeserializeObject<SettlementLiveState>(liveJson);
            Check(Mathf.Approximately(liveLoaded.Pop, 2711f)
                && Mathf.Approximately(liveLoaded.LastSimGameTime, 456f),
                "고정 원본 인구(SettlementLiveState.Pop)와 LastSimGameTime이 JSON 왕복에서 유지됨",
                passed, failed);
        }

        private static void ValidateSeedFixedState(List<string> passed,
            List<string> failed, StringBuilder dump)
        {
            List<RaceSettlementPlacer.SettlementPlacement> placements =
                RaceSettlementPlacer.ComputeAll(SampleSeed, 8, -98, 98, -98, 98);
            ChronicleSaveData chronicle = SettlementChronicleSimulator.Simulate(
                SampleSeed, placements);
            List<RaceSettlementPlacer.SettlementPlacement> repeatedPlacements =
                RaceSettlementPlacer.ComputeAll(SampleSeed, 8, -98, 98, -98, 98);
            ChronicleSaveData repeatedChronicle = SettlementChronicleSimulator.Simulate(
                SampleSeed, repeatedPlacements);

            Dictionary<VillageStage, int> stageCounts =
                new Dictionary<VillageStage, int>();
            Dictionary<int, HashSet<VillageStage>> stagesByEconomicPop =
                new Dictionary<int, HashSet<VillageStage>>();
            bool deterministic = chronicle.Settlements.Count
                == repeatedChronicle.Settlements.Count;
            int operational = 0;

            dump.AppendLine($"## Seed {SampleSeed} 고정 상태 표본 (선두 12개 운영 정착지)");
            dump.AppendLine();
            dump.AppendLine("| SettlementId | Profile | RawPop | EconPop | InitialStage |");
            dump.AppendLine("|---:|---|---:|---:|---|");

            for (int i = 0; deterministic == true
                && i < chronicle.Settlements.Count; i++)
            {
                SettlementSnapshot snapshot = chronicle.Settlements[i];
                SettlementSnapshot repeated = repeatedChronicle.Settlements[i];
                if (VillageDevelopmentBootstrap.IsOperationalFate(snapshot.Fate)
                    == false)
                    continue;

                VillageProfileResolution profile = VillageProfileResolver.Resolve(
                    new VillageProfileContext(snapshot.Race, snapshot.Biome,
                        ResolveRole(chronicle, snapshot)));
                VillageProfileResolution repeatedProfile = VillageProfileResolver.Resolve(
                    new VillageProfileContext(repeated.Race, repeated.Biome,
                        ResolveRole(repeatedChronicle, repeated)));

                int rawPop = snapshot.Population;
                int economicPop = VillageManager.QuantizeAbstractPopulation(rawPop);
                VillageStage stage = VillageProgressionRules.CalculateInitialStage(
                    rawPop, profile.Profile.MaxStage);
                VillageStage repeatedStage = VillageProgressionRules.CalculateInitialStage(
                    repeated.Population, repeatedProfile.Profile.MaxStage);

                deterministic &= snapshot.SettlementId == repeated.SettlementId;
                deterministic &= rawPop == repeated.Population;
                deterministic &= profile.Profile.ProfileId
                    == repeatedProfile.Profile.ProfileId;
                deterministic &= stage == repeatedStage;

                Increment(stageCounts, stage);
                if (stagesByEconomicPop.TryGetValue(economicPop,
                        out HashSet<VillageStage>? stages) == false)
                {
                    stages = new HashSet<VillageStage>();
                    stagesByEconomicPop[economicPop] = stages;
                }
                stages.Add(stage);

                if (operational < 12)
                {
                    dump.AppendLine($"| {snapshot.SettlementId} " +
                        $"| {profile.Profile.ProfileId} | {rawPop} | {economicPop} " +
                        $"| {stage} |");
                }
                operational++;
            }

            dump.AppendLine();
            dump.AppendLine($"- operational settlements: {operational}");
            dump.AppendLine($"- initial stage distribution: {FormatStages(stageCounts, operational)}");
            dump.AppendLine();

            Check(deterministic && operational > 0,
                "같은 seed의 고정 원본 인구·Profile·초기 Stage가 반복 생성에서 재현됨",
                passed, failed);

            bool allStagesPresent = stageCounts.Count >= 4;
            Check(allStagesPresent,
                "seed 12345 운영 정착지의 초기 Stage가 단일 값으로 퇴화하지 않음(4개 이상 Stage 존재)",
                passed, failed);

            bool cappedEconomicPopSplits = stagesByEconomicPop.TryGetValue(
                    VillageProfile.MAX_ECONOMIC_POPULATION,
                    out HashSet<VillageStage>? cappedStages) == true
                && cappedStages.Count > 1;
            Check(cappedEconomicPopSplits,
                "경제 인구 상한(25)에 묶인 정착지들이 원본 인구에 따라 서로 다른 초기 Stage를 받음",
                passed, failed);
        }

        private static VillageStage Calc(int rawPopulation)
        {
            return VillageProgressionRules.CalculateInitialStage(rawPopulation,
                VillageStage.City);
        }

        private static VillageData CreateFrozenSample()
        {
            VillageData village = new VillageData(3, new Vector2(0f, 0f))
            {
                SettlementId = 9,
                Population = 10,
                Stage = VillageStage.Village,
                LastConstructionSimGameTime = 0f,
                LastConstructionTierCheckGameTime = 0f,
            };
            village.PlacedObjectTypeIds.Add(151);
            village.ActiveBuildTasks.Add(new BuildTaskSnapshot
            {
                TableId = 153,
                StartedAt = 0f,
                AccumulatedHours = 1.5f,
                TileX = 1,
                TileY = 1,
                ReservedWood = 4,
                ReservedStone = 0,
                BuildingEntityId = -1,
                PlacementPending = false,
            });
            return village;
        }

        private static VillageAbstractAdvanceOptions CreateFrozenOptions()
        {
            return new VillageAbstractAdvanceOptions
            {
                AllowDevelopment = false,
                IsOperational = true,
            };
        }

        private static VillageSettlementRole ResolveRole(ChronicleSaveData chronicle,
            SettlementSnapshot settlement)
        {
            if (settlement.Fate == SettlementFate.Conquered)
                return VillageSettlementRole.Conquered;

            for (int i = 0; i < chronicle.Kingdoms.Count; i++)
            {
                if (chronicle.Kingdoms[i].CapitalSettlementId
                    == settlement.SettlementId)
                {
                    return VillageSettlementRole.Capital;
                }
            }
            return VillageSettlementRole.Standard;
        }

        private static string FormatStages(Dictionary<VillageStage, int> counts, int total)
        {
            Array stages = Enum.GetValues(typeof(VillageStage));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < stages.Length; i++)
            {
                VillageStage stage = (VillageStage)stages.GetValue(i);
                counts.TryGetValue(stage, out int count);
                if (i > 0)
                    builder.Append(", ");
                double share = total > 0 ? count * 100d / total : 0d;
                builder.Append(stage).Append('=').Append(count)
                    .Append(" (").Append(share.ToString("F1")).Append("%)");
            }
            return builder.ToString();
        }

        private static void Increment<TKey>(Dictionary<TKey, int> values, TKey key)
            where TKey : notnull
        {
            values.TryGetValue(key, out int current);
            values[key] = current + 1;
        }

        private static void Check(bool condition, string description,
            List<string> passed, List<string> failed)
        {
            if (condition == true)
                passed.Add(description);
            else
                failed.Add(description);
        }
    }
}
