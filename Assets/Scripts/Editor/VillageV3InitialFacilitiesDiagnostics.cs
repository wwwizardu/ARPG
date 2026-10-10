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
    /// VILLAGE_REWORK_PLAN V3 전용 정적 진단. 세이브 파일과 월드 상태는 변경하지 않는다.
    /// Stage 누적 마일스톤 검증·1회 적용·Tier 교체·상점 Tier·Cap·PendingInitialPlacements를 검증하고,
    /// seed 표본의 마을별 마일스톤·시설 목록·Cap을 덤프한다. 실제 청크 materialize 배치와
    /// UI 상호작용 실측은 플레이 모드 전용이라 리포트에 제한으로 기록한다.
    /// </summary>
    public static class VillageV3InitialFacilitiesDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V3_INITIAL_FACILITIES_DUMP.md";
        private const string BuildableTablePath =
            "Assets/_BinaryData/TableData/BuildableItemTable.bytes";
        private const int SampleSeed = 12345;

        [MenuItem("ARPG/Village/Run V3 Initial Facilities Diagnostics", false, 423)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();
            StringBuilder dump = new StringBuilder();

            Func<int, Tables.BuildableItemTable?> resolver = LoadTableResolver();

            ValidateFacilityCatalogContract(resolver, passed, failed);
            ValidateAllGrowthPlans(resolver, passed, failed);
            ValidateCumulativeApplication(resolver, passed, failed);
            ValidateTierReplacement(resolver, passed, failed);
            ValidateExactlyOnce(resolver, passed, failed);
            ValidateMinimumBaseTownShop(resolver, passed, failed);
            ValidateSaveRoundTrip(resolver, passed, failed);
            ValidateZeroResourceAndNoTasks(resolver, passed, failed);
            ValidateNonInteractiveRejected(resolver, passed, failed);
            ValidateDeterminism(resolver, passed, failed);
            ValidateFrozenAfterApplication(resolver, passed, failed);
            ValidateSeedSample(resolver, passed, failed, dump);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V3 Initial Facilities Diagnostics");
            report.AppendLine();
            report.AppendLine($"- Unity: {Application.unityVersion}");
            report.AppendLine($"- Generated UTC: {DateTime.UtcNow:O}");
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
            report.AppendLine("- 실제 청크 기반 첫 materialize에서 PendingInitialPlacements 배치·재시도 실측");
            report.AppendLine("- 배치된 초기 시설의 실제 상호작용 UI(Shop/Forge/Inn) 진입 실측 (VillageHub 라우팅은 V4)");
            report.AppendLine("- materialize/fold-back 반복 후 시설·Cap 중복 없음 실측 (V7 통합 검증 연계)");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Temp");
            File.WriteAllText(OutputPath, report.ToString());

            if (failed.Count > 0)
            {
                string message = string.Join(" | ", failed);
                Debug.LogError($"[VillageV3InitialFacilitiesDiagnostics] 실패: {message}");
                throw new InvalidOperationException(message);
            }
            Debug.Log($"[VillageV3InitialFacilitiesDiagnostics] 완료: {OutputPath}");
        }

        // ========== 테이블 로딩 (DataManager 없이 원본 .bytes 직접 파싱 — 상태 비변경) ==========

        private static Func<int, Tables.BuildableItemTable?> LoadTableResolver()
        {
            string json = File.ReadAllText(BuildableTablePath, Encoding.UTF8);
            List<Tables.BuildableItemTable>? rows =
                JsonConvert.DeserializeObject<List<Tables.BuildableItemTable>>(json);
            Dictionary<int, Tables.BuildableItemTable> byId =
                new Dictionary<int, Tables.BuildableItemTable>();
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                    byId[rows[i].Id] = rows[i];
            }
            return id => byId.TryGetValue(id, out Tables.BuildableItemTable? table)
                ? table
                : null;
        }

        private static VillageGrowthPlan GetDwarfPlan()
        {
            VillageProfileResolution resolution = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Dwarf,
                    GlobalEnum.BiomeType.Mountain, VillageSettlementRole.Standard));
            VillageGrowthPlanCatalog.TryGet(resolution.Profile.GrowthPlanId,
                out VillageGrowthPlan plan);
            return plan;
        }

        private static VillageData CreateVillage(int id)
        {
            return new VillageData(id, new Vector2(100f, 50f)) { SettlementId = id };
        }

        // ========== 검증 ==========

        private static void ValidateMinimumBaseTownShop(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData village = CreateVillage(0);
            village.SettlementId = -1;
            VillageInitialDevelopment.EnsureMinimumForge(village, resolver, out _);
            MinimumShopEnsureResult first = VillageInitialDevelopment.EnsureMinimumShop(
                village, resolver, out string error);
            int shopId = VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID;
            PlacedObjectSaveData? shop = village.PlacedObjects.Find(p => p.TableId == shopId);
            Check(first == MinimumShopEnsureResult.Created && error.Length == 0
                && village.ShopTier == 1 && shop != null && shop.FacilityTier == 1
                && shop.PlacementPending == true && village.PlacedObjects.Count == 2
                && VillageConstructionLedger.CountPlaced(village, shopId) == 1
                && village.ActiveBuildTasks.Count == 0
                && village.AppliedGrowthMilestones.Count == 0,
                "BaseTown은 Forge를 보존하고 상점 하나와 ShopTier 1만 완공 생성함",
                passed, failed);
            if (shop == null)
                return;

            MinimumShopEnsureResult repeated = VillageInitialDevelopment.EnsureMinimumShop(
                village, resolver, out _);
            Check(repeated == MinimumShopEnsureResult.Unchanged
                && village.PlacedObjects.Count == 2
                && VillageConstructionLedger.CountPlaced(village, shopId) == 1,
                "시작 마을 상점 보장을 반복해도 상점이 중복되지 않음", passed, failed);

            shop.FacilityTier = 3;
            village.ShopTier = 4;
            MinimumShopEnsureResult higher = VillageInitialDevelopment.EnsureMinimumShop(
                village, resolver, out _);
            Check(higher == MinimumShopEnsureResult.Unchanged && shop.FacilityTier == 3
                && village.ShopTier == 4,
                "기존 상점 시설 Tier와 상권 Tier를 낮추지 않음", passed, failed);

            shop.FacilityTier = 0;
            village.ShopTier = 0;
            MinimumShopEnsureResult normalized = VillageInitialDevelopment.EnsureMinimumShop(
                village, resolver, out _);
            Check(normalized == MinimumShopEnsureResult.TierNormalized
                && shop.FacilityTier == 1 && village.ShopTier == 1
                && village.PlacedObjects.Count == 2,
                "기존 상점의 미설정 Tier를 중복 생성 없이 1로 정규화함", passed, failed);

            VillageData loaded = JsonConvert.DeserializeObject<VillageData>(
                JsonConvert.SerializeObject(village))!;
            MinimumShopEnsureResult restored = VillageInitialDevelopment.EnsureMinimumShop(
                loaded, resolver, out _);
            Check(restored == MinimumShopEnsureResult.Unchanged
                && VillageSimulationParity.CompareFixedState(village, loaded,
                    out string _) == true,
                "시작 마을 상점과 Tier가 저장/복원 뒤에도 유지됨", passed, failed);

            loaded.PlacedObjectTypeIds.Add(shopId);
            string beforeError = JsonConvert.SerializeObject(loaded);
            MinimumShopEnsureResult invalid = VillageInitialDevelopment.EnsureMinimumShop(
                loaded, resolver, out _);
            Check(invalid == MinimumShopEnsureResult.DataError
                && JsonConvert.SerializeObject(loaded) == beforeError,
                "상점 원장 불일치/중복은 상태 변경 없이 거부함", passed, failed);

            VillageData missingTable = CreateVillage(99);
            MinimumShopEnsureResult missing = VillageInitialDevelopment.EnsureMinimumShop(
                missingTable, _ => null, out _);
            Check(missing == MinimumShopEnsureResult.DataError
                && missingTable.PlacedObjects.Count == 0 && missingTable.ShopTier == 0,
                "상점 테이블 누락 시 시설과 상권 Tier를 변경하지 않음", passed, failed);
        }

        private static void ValidateFacilityCatalogContract(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            bool ok = VillageInitialFacilityCatalog.All.Count == 5;
            for (int i = 0; i < VillageInitialFacilityCatalog.All.Count; i++)
            {
                VillageInitialFacilityDefinition def = VillageInitialFacilityCatalog.All[i];
                ok &= string.IsNullOrWhiteSpace(def.InteractionId) == false;
                ok &= string.IsNullOrWhiteSpace(def.Prompt) == false;
                ok &= string.IsNullOrWhiteSpace(def.ProvidedFunction) == false;

                Tables.BuildableItemTable? table = resolver(def.TableId);
                ok &= table != null
                    && table.SpawnType == GlobalEnum.BuildableSpawnType.Entity;
                if (def.RequiresTableServiceRouting == true && table != null)
                    ok &= ((ProvidedService)table.ProvidedService & def.RoutedService) != 0;
            }
            Check(ok,
                "V0 InteractiveFacility 5종(100/151/153/160/170)의 상호작용 ID·프롬프트·제공 기능·" +
                "Entity 스폰·서비스 라우팅 계약이 실제 테이블과 일치함",
                passed, failed);
        }

        private static void ValidateAllGrowthPlans(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            List<string> errors = new List<string>();
            int planCount = 0;
            foreach (string growthPlanId in VillageGrowthPlanCatalog.AllGrowthPlanIds)
            {
                if (VillageGrowthPlanCatalog.TryGet(growthPlanId,
                        out VillageGrowthPlan plan) == false)
                    continue;
                planCount++;
                VillageInitialDevelopment.ValidatePlan(plan, resolver, errors);
            }
            Check(planCount == VillageProfileCatalog.AllProfiles.Count && errors.Count == 0,
                $"전체 성장 명세 {planCount}개(Profile 수와 동일)가 허용 목록·테이블·누적 무결성 검증을 통과함" +
                (errors.Count > 0 ? $" (오류: {string.Join("; ", errors)})" : string.Empty),
                passed, failed);
        }

        private static void ValidateCumulativeApplication(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();

            // Town 초기 Stage: 하위 Stage 결과(허브·시장·여관) + 특수 시설 Tier 2 + 상점 3 + Cap 150.
            VillageData town = CreateVillage(1);
            VillageInitialDevelopmentResult townResult = VillageInitialDevelopment
                .ApplyInitialMilestones(town, dwarfPlan, VillageStage.Town, resolver);
            bool townOk = townResult.HasErrors == false
                && CountOf(town, VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID) == 1
                && CountOf(town, VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID) == 1
                && CountOf(town, VillageInitialFacilityCatalog.INN_TABLE_ID) == 1
                && CountOf(town, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 1
                && town.ShopTier == VillageGrowthPlanCatalog.SHOP_TIER_TOWN
                && GetCap(town, GlobalEnum.ItemType.Food)
                    == VillageGrowthPlanCatalog.STAGE_BASE_CAP_TOWN
                && GetCap(town, GlobalEnum.ItemType.Wood)
                    == VillageGrowthPlanCatalog.STAGE_BASE_CAP_TOWN
                && GetCap(town, GlobalEnum.ItemType.Stone)
                    == VillageGrowthPlanCatalog.STAGE_BASE_CAP_TOWN
                && GetFacilityTier(town, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 2;
            Check(townOk,
                "Dwarf Town 초기 Stage가 하위 Stage 필수 결과(허브·시장·여관)와 제련소 Tier 2·상점 Tier 3·" +
                "Stage Cap 150을 빠짐없이 포함함",
                passed, failed);

            // Settlement 초기 Stage: 허브+시장만, 여관·특수 시설 없음.
            VillageData settlement = CreateVillage(2);
            VillageInitialDevelopment.ApplyInitialMilestones(settlement, dwarfPlan,
                VillageStage.Settlement, resolver);
            Check(CountOf(settlement, VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID) == 1
                && CountOf(settlement, VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID) == 1
                && CountOf(settlement, VillageInitialFacilityCatalog.INN_TABLE_ID) == 0
                && CountOf(settlement, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 0
                && settlement.ShopTier == VillageGrowthPlanCatalog.SHOP_TIER_SETTLEMENT
                && GetCap(settlement, GlobalEnum.ItemType.Wood)
                    == VillageGrowthPlanCatalog.STAGE_BASE_CAP_SETTLEMENT,
                "Settlement 초기 Stage는 중심 거점+기본 시장·상점 Tier 1·기본 Cap만 생성함(상위 Stage 결과 미포함)",
                passed, failed);

            // 비-Dwarf 기본 명세: 특수 시설 없음 (기능 미확정 시설은 외형만 먼저 만들지 않음).
            VillageData human = CreateVillage(3);
            VillageInitialDevelopment.ApplyInitialMilestones(human,
                VillageGrowthPlanCatalog.DefaultPlan, VillageStage.City, resolver);
            Check(CountOf(human, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 0
                && CountOf(human, VillageInitialFacilityCatalog.SHRINE_TABLE_ID) == 0
                && CountOf(human, VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID) == 1
                && human.ShopTier == VillageGrowthPlanCatalog.SHOP_TIER_CITY,
                "특수 시설이 미확정인 Profile(Human 기본)은 City에서도 공통 시설·상점 Tier·Cap만 받음",
                passed, failed);
        }

        private static void ValidateTierReplacement(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData city = CreateVillage(4);
            VillageInitialDevelopment.ApplyInitialMilestones(city, dwarfPlan,
                VillageStage.City, resolver);

            // 상위 Tier가 하위 Tier를 교체·흡수: 제련소 건물은 1개, Tier만 3.
            Check(CountOf(city, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 1
                && GetFacilityTier(city, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 3
                && city.PlacedObjects.Count == city.PlacedObjectTypeIds.Count,
                "City까지 누적 적용해도 주 특수 시설은 1개 레코드로 유지되고 Tier만 3으로 교체됨(모듈 증식 없음)",
                passed, failed);
        }

        private static void ValidateExactlyOnce(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData village = CreateVillage(5);
            VillageInitialDevelopmentResult firstResult = VillageInitialDevelopment
                .ApplyInitialMilestones(village, dwarfPlan, VillageStage.City, resolver);

            int facilityCount = village.PlacedObjects.Count;
            int woodCap = GetCap(village, GlobalEnum.ItemType.Wood);
            int shopTier = village.ShopTier;
            int milestoneCount = village.AppliedGrowthMilestones.Count;

            VillageInitialDevelopmentResult secondResult = VillageInitialDevelopment
                .ApplyInitialMilestones(village, dwarfPlan, VillageStage.City, resolver);

            Check(firstResult.MilestonesApplied == milestoneCount
                && secondResult.MilestonesApplied == 0
                && secondResult.MilestonesSkippedAlreadyApplied == milestoneCount
                && village.PlacedObjects.Count == facilityCount
                && GetCap(village, GlobalEnum.ItemType.Wood) == woodCap
                && village.ShopTier == shopTier
                && village.AppliedGrowthMilestones.Count == milestoneCount,
                "같은 마일스톤 재적용 시 AppliedGrowthMilestones 키로 전부 건너뛰어 시설·Cap·상점 Tier가 " +
                "정확히 한 번만 적용됨",
                passed, failed);
        }

        private static void ValidateSaveRoundTrip(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData source = CreateVillage(6);
            VillageInitialDevelopment.ApplyInitialMilestones(source, dwarfPlan,
                VillageStage.Town, resolver);

            VillageData loaded = JsonConvert.DeserializeObject<VillageData>(
                JsonConvert.SerializeObject(source))!;
            bool fieldsPreserved = loaded.AppliedGrowthMilestones.Count
                    == source.AppliedGrowthMilestones.Count
                && loaded.ShopTier == source.ShopTier
                && loaded.PlacedObjects.Count == source.PlacedObjects.Count
                && GetFacilityTier(loaded, VillageInitialFacilityCatalog.FORGE_TABLE_ID) == 2
                && GetCap(loaded, GlobalEnum.ItemType.Stone)
                    == GetCap(source, GlobalEnum.ItemType.Stone);

            VillageInitialDevelopmentResult reapplied = VillageInitialDevelopment
                .ApplyInitialMilestones(loaded, dwarfPlan, VillageStage.Town, resolver);
            Check(fieldsPreserved
                && reapplied.MilestonesApplied == 0
                && loaded.PlacedObjects.Count == source.PlacedObjects.Count
                && GetCap(loaded, GlobalEnum.ItemType.Wood)
                    == GetCap(source, GlobalEnum.ItemType.Wood),
                "마일스톤 원장·ShopTier·FacilityTier·Cap이 JSON save/load 왕복에서 유지되고 " +
                "왕복 후 재적용에도 시설·Cap이 중복되지 않음",
                passed, failed);
        }

        private static void ValidateZeroResourceAndNoTasks(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData village = CreateVillage(7);
            VillageInitialDevelopmentResult result = VillageInitialDevelopment
                .ApplyInitialMilestones(village, dwarfPlan, VillageStage.City, resolver);

            int pending = VillageInitialDevelopment.CountPendingInitialPlacements(village);
            bool allPendingUnassigned = true;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                allPendingUnassigned &= village.PlacedObjects[i].PlacementPending == true
                    && VillageConstructionPosition.IsAssigned(village.PlacedObjects[i])
                        == false;
            }

            Check(result.HasErrors == false
                && GetAmount(village, GlobalEnum.ItemType.Wood) == 0
                && GetAmount(village, GlobalEnum.ItemType.Stone) == 0
                && village.ActiveBuildTasks.Count == 0
                && village.PlacedObjects.Count > 0
                && pending == village.PlacedObjects.Count
                && allPendingUnassigned,
                "자원 0 상태에서도 필수·특수 시설이 자원 소비·진행 task 없이 완공 상태로 생성되고, " +
                "전부 PendingInitialPlacements(PlacementPending)로 materialize 배치를 대기함",
                passed, failed);
        }

        private static void ValidateNonInteractiveRejected(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            // Woodpile(110)은 Retire 분류의 건물형 비상호작용 오브젝트 — 검증 실패해야 한다.
            VillageGrowthPlan invalidPlan = new VillageGrowthPlan("growth.test.invalid",
                new List<VillageGrowthMilestone>
                {
                    new VillageGrowthMilestone(VillageStage.Settlement,
                        VillageGrowthRewardType.AddCoreFacility, 110, 1, 0, 0, 0, 0),
                });
            List<string> errors = new List<string>();
            bool valid = VillageInitialDevelopment.ValidatePlan(invalidPlan, resolver,
                errors);

            VillageData village = CreateVillage(8);
            VillageInitialDevelopmentResult result = VillageInitialDevelopment
                .ApplyInitialMilestones(village, invalidPlan, VillageStage.City, resolver);

            Check(valid == false && errors.Count > 0
                && result.HasErrors == true
                && result.MilestonesSkippedInvalid == 1
                && village.PlacedObjects.Count == 0
                && village.AppliedGrowthMilestones.Count == 0,
                "건물형 비상호작용 오브젝트(Woodpile 110)가 초기 시설 목록에 들어오면 데이터 검증에서 " +
                "실패하고 완공 원장이 생성되지 않음",
                passed, failed);
        }

        private static void ValidateDeterminism(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData first = CreateVillage(9);
            VillageData second = CreateVillage(9);
            VillageInitialDevelopment.ApplyInitialMilestones(first, dwarfPlan,
                VillageStage.Town, resolver);
            VillageInitialDevelopment.ApplyInitialMilestones(second, dwarfPlan,
                VillageStage.Town, resolver);

            bool identical = first.PlacedObjectTypeIds.Count
                    == second.PlacedObjectTypeIds.Count
                && first.AppliedGrowthMilestones.Count
                    == second.AppliedGrowthMilestones.Count
                && first.ShopTier == second.ShopTier;
            for (int i = 0; identical && i < first.PlacedObjectTypeIds.Count; i++)
                identical &= first.PlacedObjectTypeIds[i] == second.PlacedObjectTypeIds[i];
            for (int i = 0; identical && i < first.AppliedGrowthMilestones.Count; i++)
                identical &= first.AppliedGrowthMilestones[i]
                    == second.AppliedGrowthMilestones[i];

            Check(identical,
                "같은 Profile·초기 Stage는 항상 같은 순서의 시설 목록·마일스톤 키를 만듦(RNG·점수 미사용)",
                passed, failed);
        }

        private static void ValidateFrozenAfterApplication(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageGrowthPlan dwarfPlan = GetDwarfPlan();
            VillageData village = CreateVillage(10);
            village.Population = 10;
            VillageInitialDevelopment.ApplyInitialMilestones(village, dwarfPlan,
                VillageStage.Town, resolver);
            int facilityCount = village.PlacedObjects.Count;
            int shopTier = village.ShopTier;
            int woodCap = GetCap(village, GlobalEnum.ItemType.Wood);

            VillageAbstractAdvanceResult advance = VillageAbstractConstruction
                .AdvanceAbstractConstruction(village, 10000f,
                    new VillageAbstractAdvanceOptions
                    {
                        AllowDevelopment = false,
                        IsOperational = true,
                    });

            Check(advance.IsCaughtUp
                && village.PlacedObjects.Count == facilityCount
                && village.ShopTier == shopTier
                && GetCap(village, GlobalEnum.ItemType.Wood) == woodCap
                && village.ActiveBuildTasks.Count == 0
                && village.AppliedGrowthMilestones.Count > 0,
                "초기 마일스톤 적용 후 10,000h 동결 advance에도 시설·ShopTier·Cap·마일스톤 원장이 불변",
                passed, failed);
        }

        private static void ValidateSeedSample(
            Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed, StringBuilder dump)
        {
            List<RaceSettlementPlacer.SettlementPlacement> placements =
                RaceSettlementPlacer.ComputeAll(SampleSeed, 8, -98, 98, -98, 98);
            ChronicleSaveData chronicle = SettlementChronicleSimulator.Simulate(
                SampleSeed, placements);

            dump.AppendLine($"## Seed {SampleSeed} 마을별 초기 마일스톤·시설·Cap 표본 (선두 12개 운영 정착지)");
            dump.AppendLine();
            dump.AppendLine("| SettlementId | Profile | RawPop | InitialStage | Milestones | Facilities(Tier) | ShopTier | Cap F/W/S | Pending |");
            dump.AppendLine("|---:|---|---:|---|---:|---|---:|---|---:|");

            Dictionary<VillageStage, int> stageCounts = new Dictionary<VillageStage, int>();
            int operational = 0;
            int errorVillages = 0;
            bool deterministic = true;

            for (int i = 0; i < chronicle.Settlements.Count; i++)
            {
                SettlementSnapshot snapshot = chronicle.Settlements[i];
                if (VillageDevelopmentBootstrap.IsOperationalFate(snapshot.Fate) == false)
                    continue;

                VillageProfileResolution profile = VillageProfileResolver.Resolve(
                    new VillageProfileContext(snapshot.Race, snapshot.Biome,
                        ResolveRole(chronicle, snapshot)));
                VillageStage stage = VillageProgressionRules.CalculateInitialStage(
                    snapshot.Population, profile.Profile.MaxStage);
                VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(
                    profile.Profile, out _);

                VillageData village = CreateVillage(snapshot.SettlementId);
                VillageInitialDevelopmentResult result = VillageInitialDevelopment
                    .ApplyInitialMilestones(village, plan, stage, resolver);
                if (result.HasErrors)
                    errorVillages++;

                VillageData repeated = CreateVillage(snapshot.SettlementId);
                VillageInitialDevelopment.ApplyInitialMilestones(repeated, plan, stage,
                    resolver);
                deterministic &= repeated.PlacedObjectTypeIds.Count
                    == village.PlacedObjectTypeIds.Count;
                deterministic &= repeated.ShopTier == village.ShopTier;

                Increment(stageCounts, stage);
                if (operational < 12)
                {
                    dump.AppendLine($"| {snapshot.SettlementId} " +
                        $"| {profile.Profile.ProfileId} | {snapshot.Population} | {stage} " +
                        $"| {village.AppliedGrowthMilestones.Count} " +
                        $"| {FormatFacilities(village)} | {village.ShopTier} " +
                        $"| {GetCap(village, GlobalEnum.ItemType.Food)}/" +
                        $"{GetCap(village, GlobalEnum.ItemType.Wood)}/" +
                        $"{GetCap(village, GlobalEnum.ItemType.Stone)} " +
                        $"| {VillageInitialDevelopment.CountPendingInitialPlacements(village)} |");
                }
                operational++;
            }

            dump.AppendLine();
            dump.AppendLine($"- operational settlements: {operational}");
            dump.AppendLine($"- initial stage distribution: {FormatStages(stageCounts, operational)}");
            dump.AppendLine();

            Check(operational > 0 && errorVillages == 0,
                $"seed {SampleSeed} 운영 정착지 {operational}개 전부 초기 마일스톤 적용에 데이터 검증 오류가 없음",
                passed, failed);
            Check(deterministic,
                $"seed {SampleSeed} 전체 정착지에서 같은 Profile·인구의 반복 적용이 같은 시설 수·ShopTier를 만듦",
                passed, failed);
        }

        // ========== 헬퍼 ==========

        private static int CountOf(VillageData village, int tableId)
        {
            return VillageConstructionLedger.CountPlaced(village, tableId);
        }

        private static int GetFacilityTier(VillageData village, int tableId)
        {
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                if (village.PlacedObjects[i].TableId == tableId)
                    return village.PlacedObjects[i].FacilityTier;
            }
            return -1;
        }

        private static int GetCap(VillageData village, GlobalEnum.ItemType type)
        {
            return VillageResourceSimulation.GetCap(village, type);
        }

        private static int GetAmount(VillageData village, GlobalEnum.ItemType type)
        {
            return VillageResourceSimulation.GetAmount(village, type);
        }

        private static string FormatFacilities(VillageData village)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(village.PlacedObjects[i].TableId)
                    .Append("(T").Append(village.PlacedObjects[i].FacilityTier).Append(')');
            }
            return builder.Length == 0 ? "-" : builder.ToString();
        }

        private static VillageSettlementRole ResolveRole(ChronicleSaveData chronicle,
            SettlementSnapshot settlement)
        {
            if (settlement.Fate == SettlementFate.Conquered)
                return VillageSettlementRole.Conquered;

            for (int i = 0; i < chronicle.Kingdoms.Count; i++)
            {
                if (chronicle.Kingdoms[i].CapitalSettlementId == settlement.SettlementId)
                    return VillageSettlementRole.Capital;
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
