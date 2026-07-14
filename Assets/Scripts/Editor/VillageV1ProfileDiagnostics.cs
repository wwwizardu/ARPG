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
    /// VILLAGE_REWORK_PLAN V1 전용 정적 진단. 세이브 파일과 월드 상태는 변경하지 않는다.
    /// </summary>
    public static class VillageV1ProfileDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V1_PROFILE_DUMP.md";

        [MenuItem("ARPG/Village/Run V1 Profile Diagnostics", false, 421)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();

            ValidateCatalog(passed, failed);
            ValidateResolver(passed, failed);
            ValidateSeedReproducibility(passed, failed);
            ValidateSaveCompatibility(passed, failed);
            ValidateBaseTownPopulation(passed, failed);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V1 Profile Diagnostics");
            report.AppendLine();
            report.AppendLine($"- Unity: {Application.unityVersion}");
            report.AppendLine($"- Generated UTC: {DateTime.UtcNow:O}");
            report.AppendLine($"- Profiles: {VillageProfileCatalog.AllProfiles.Count}");
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

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Temp");
            File.WriteAllText(OutputPath, report.ToString());

            if (failed.Count > 0)
            {
                string message = string.Join(" | ", failed);
                Debug.LogError($"[VillageV1ProfileDiagnostics] 실패: {message}");
                throw new InvalidOperationException(message);
            }
            Debug.Log($"[VillageV1ProfileDiagnostics] 완료: {OutputPath}");
        }

        private static void ValidateCatalog(List<string> passed, List<string> failed)
        {
            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            Check(profiles.Count == 66,
                "BaseTown 1개 + Race/Biome 13조합 × 역할 5종 = 66개 Profile",
                passed, failed);

            HashSet<string> profileIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> growthPlanIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> layoutProfileIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> tradeProfileIds = new HashSet<string>(StringComparer.Ordinal);
            bool idsValid = true;
            bool capSchemaValid = true;
            bool definitionsAreIsolated = true;
            for (int i = 0; i < profiles.Count; i++)
            {
                VillageProfile profile = profiles[i];
                idsValid &= profileIds.Add(profile.ProfileId);
                idsValid &= growthPlanIds.Add(profile.GrowthPlanId);
                idsValid &= layoutProfileIds.Add(profile.LayoutProfileId);
                idsValid &= tradeProfileIds.Add(profile.TradeProfileId);
                idsValid &= string.IsNullOrWhiteSpace(profile.ProfileId) == false;
                idsValid &= string.IsNullOrWhiteSpace(profile.GrowthPlanId) == false;
                idsValid &= string.IsNullOrWhiteSpace(profile.LayoutProfileId) == false;
                idsValid &= string.IsNullOrWhiteSpace(profile.TradeProfileId) == false;
                capSchemaValid &= profile.StageCapModifiers.Count == 5;
                capSchemaValid &= profile.DefaultEconomicPopulation
                    >= VillageProfile.MIN_ECONOMIC_POPULATION;
                capSchemaValid &= profile.DefaultEconomicPopulation
                    <= VillageProfile.MAX_ECONOMIC_POPULATION;

                if (i > 0)
                {
                    definitionsAreIsolated &= ReferenceEquals(
                        profiles[i - 1].SecondaryFacilityIds,
                        profile.SecondaryFacilityIds) == false;
                    definitionsAreIsolated &= ReferenceEquals(
                        profiles[i - 1].StageCapModifiers,
                        profile.StageCapModifiers) == false;
                }
            }

            Check(idsValid,
                "Profile/Growth/Layout/Trade ID가 비어 있지 않고 Profile별로 고유함",
                passed, failed);
            Check(capSchemaValid,
                "모든 Profile이 5개 Stage Cap 보정과 1~25 기본 경제 인구를 가짐",
                passed, failed);
            Check(definitionsAreIsolated,
                "Profile별 시설·Stage Cap 컬렉션이 가변 참조를 공유하지 않음",
                passed, failed);
        }

        private static void ValidateResolver(List<string> passed, List<string> failed)
        {
            VillageProfileContext humanPlains = new VillageProfileContext(
                GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Plains,
                VillageSettlementRole.Standard);
            VillageProfileContext humanForest = new VillageProfileContext(
                GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Forest,
                VillageSettlementRole.Standard);
            VillageProfileContext elfForest = new VillageProfileContext(
                GlobalEnum.RaceType.Elf, GlobalEnum.BiomeType.Forest,
                VillageSettlementRole.Standard);
            VillageProfileContext capital = new VillageProfileContext(
                GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Plains,
                VillageSettlementRole.Capital);

            VillageProfileResolution first = VillageProfileResolver.Resolve(humanPlains);
            VillageProfileResolution repeated = VillageProfileResolver.Resolve(humanPlains);
            VillageProfileResolution biomeVariant =
                VillageProfileResolver.Resolve(humanForest);
            VillageProfileResolution raceVariant = VillageProfileResolver.Resolve(elfForest);
            VillageProfileResolution roleVariant = VillageProfileResolver.Resolve(capital);

            Check(first.UsedFallback == false
                && ReferenceEquals(first.Profile, repeated.Profile),
                "같은 resolver 입력은 동일한 저장 Profile을 재현함",
                passed, failed);
            Check(first.Profile.ProfileId != biomeVariant.Profile.ProfileId
                && first.Profile.GrowthPlanId != biomeVariant.Profile.GrowthPlanId
                && first.Profile.TradeProfileId != biomeVariant.Profile.TradeProfileId,
                "같은 Race의 다른 Biome이 다른 성장·상권 Profile을 받음",
                passed, failed);
            Check(biomeVariant.Profile.ProfileId != raceVariant.Profile.ProfileId
                && biomeVariant.Profile.GrowthPlanId != raceVariant.Profile.GrowthPlanId
                && biomeVariant.Profile.TradeProfileId != raceVariant.Profile.TradeProfileId,
                "다른 Race가 다른 성장·상권 Profile을 받음",
                passed, failed);
            Check(first.Profile.ProfileId != roleVariant.Profile.ProfileId
                && roleVariant.Profile.Role == VillageSettlementRole.Capital,
                "정착지 역할이 Profile 선택에 반영됨",
                passed, failed);

            VillageProfileResolution incompatible = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Dwarf,
                    GlobalEnum.BiomeType.Desert, VillageSettlementRole.Standard));
            VillageProfileResolution incompatibleRepeated = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Dwarf,
                    GlobalEnum.BiomeType.Desert, VillageSettlementRole.Standard));
            Check(incompatible.UsedFallback == true
                && string.IsNullOrWhiteSpace(incompatible.Warning) == false
                && incompatible.Profile.ProfileId == incompatibleRepeated.Profile.ProfileId
                && incompatible.Profile.Biome == GlobalEnum.BiomeType.Mountain,
                "누락 Race/Biome 조합은 경고와 함께 결정론적 Race 기본 Profile로 fallback",
                passed, failed);

            VillageProfileResolution baseTown = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Human,
                    GlobalEnum.BiomeType.Plains, VillageSettlementRole.BaseTown));
            Check(baseTown.Profile.ProfileId
                    == VillageProfileCatalog.BASE_TOWN_PROFILE_ID
                && baseTown.UsedFallback == false,
                "수제작 BaseTown은 명시적 기본 Profile을 사용함",
                passed, failed);
        }

        private static void ValidateSaveCompatibility(List<string> passed,
            List<string> failed)
        {
            VillageData source = new VillageData(17, new Vector2(3f, 4f))
            {
                SettlementId = 9,
                VillageProfileId = "village.elf.forest.capital",
                Population = 12,
                FixedPopulation = -1,
            };
            string json = JsonConvert.SerializeObject(source);
            VillageData? roundTrip = JsonConvert.DeserializeObject<VillageData>(json);
            Check(roundTrip != null
                && roundTrip.VillageProfileId == source.VillageProfileId
                && roundTrip.FixedPopulation == source.FixedPopulation,
                "VillageProfileId와 FixedPopulation이 JSON save/load 왕복됨",
                passed, failed);

            const string legacyJson =
                "{\"VillageId\":3,\"TableId\":1,\"SettlementId\":8," +
                "\"Population\":7,\"PositionX\":1.0,\"PositionY\":2.0}";
            VillageData? legacy = JsonConvert.DeserializeObject<VillageData>(legacyJson);
            VillageProfileResolution first = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Nomad,
                    GlobalEnum.BiomeType.Desert, VillageSettlementRole.Standard));
            VillageProfileResolution repeated = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Nomad,
                    GlobalEnum.BiomeType.Desert, VillageSettlementRole.Standard));
            Check(legacy != null
                && string.IsNullOrWhiteSpace(legacy.VillageProfileId)
                && legacy.FixedPopulation == -1
                && first.Profile.ProfileId == repeated.Profile.ProfileId,
                "Profile 필드가 없는 구 JSON이 로드되고 같은 History 입력으로 결정론적 보정 가능",
                passed, failed);
        }

        private static void ValidateSeedReproducibility(List<string> passed,
            List<string> failed)
        {
            const int seed = 12345;
            List<RaceSettlementPlacer.SettlementPlacement> firstPlacements =
                RaceSettlementPlacer.ComputeAll(seed, 8, -98, 98, -98, 98);
            List<RaceSettlementPlacer.SettlementPlacement> repeatedPlacements =
                RaceSettlementPlacer.ComputeAll(seed, 8, -98, 98, -98, 98);
            ChronicleSaveData firstChronicle = SettlementChronicleSimulator.Simulate(
                seed, firstPlacements);
            ChronicleSaveData repeatedChronicle = SettlementChronicleSimulator.Simulate(
                seed, repeatedPlacements);

            bool deterministic = firstChronicle.Settlements.Count
                == repeatedChronicle.Settlements.Count;
            List<string> profileIds = new List<string>();
            for (int i = 0; deterministic == true
                && i < firstChronicle.Settlements.Count; i++)
            {
                SettlementSnapshot first = firstChronicle.Settlements[i];
                SettlementSnapshot repeated = repeatedChronicle.Settlements[i];
                VillageSettlementRole firstRole = ResolveRole(firstChronicle, first);
                VillageSettlementRole repeatedRole = ResolveRole(repeatedChronicle,
                    repeated);
                VillageProfileResolution firstProfile = VillageProfileResolver.Resolve(
                    new VillageProfileContext(first.Race, first.Biome, firstRole));
                VillageProfileResolution repeatedProfile = VillageProfileResolver.Resolve(
                    new VillageProfileContext(repeated.Race, repeated.Biome,
                        repeatedRole));

                deterministic &= first.SettlementId == repeated.SettlementId;
                deterministic &= firstProfile.UsedFallback == false;
                deterministic &= repeatedProfile.UsedFallback == false;
                deterministic &= firstProfile.Profile.ProfileId
                    == repeatedProfile.Profile.ProfileId;
                profileIds.Add(firstProfile.Profile.ProfileId);
            }

            string savedProfileIds = JsonConvert.SerializeObject(profileIds);
            List<string>? loadedProfileIds =
                JsonConvert.DeserializeObject<List<string>>(savedProfileIds);
            bool saveRoundTrip = loadedProfileIds != null
                && loadedProfileIds.Count == profileIds.Count;
            for (int i = 0; saveRoundTrip == true && i < profileIds.Count; i++)
                saveRoundTrip &= profileIds[i] == loadedProfileIds![i];

            Check(deterministic && saveRoundTrip,
                "seed 12345의 전체 History 정착지가 반복 생성 및 ProfileId save/load에서 재현됨",
                passed, failed);
        }

        private static void ValidateBaseTownPopulation(List<string> passed,
            List<string> failed)
        {
            VillageProfile profile = VillageProfileCatalog.BaseTownProfile;
            int legacy = VillageProfilePopulationPolicy.ResolveBaseTownFixedPopulation(
                -1, 9, profile, out BaseTownPopulationSource legacySource);
            int profileDefault = VillageProfilePopulationPolicy
                .ResolveBaseTownFixedPopulation(-1, 0, profile,
                    out BaseTownPopulationSource defaultSource);
            int saved = VillageProfilePopulationPolicy.ResolveBaseTownFixedPopulation(
                6, 24, profile, out BaseTownPopulationSource savedSource);

            Check(legacy == 9
                && legacySource == BaseTownPopulationSource.LegacyPopulation,
                "구 BaseTown의 유효 경제 인구는 FixedPopulation으로 1회 승격됨",
                passed, failed);
            Check(profileDefault == profile.DefaultEconomicPopulation
                && defaultSource == BaseTownPopulationSource.ProfileDefault,
                "BaseTown 인구 누락은 Profile 기본값으로 보정됨",
                passed, failed);
            Check(saved == 6
                && savedSource == BaseTownPopulationSource.SavedFixedPopulation,
                "저장된 BaseTown FixedPopulation이 물리 NPC 수와 무관하게 우선됨",
                passed, failed);
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
