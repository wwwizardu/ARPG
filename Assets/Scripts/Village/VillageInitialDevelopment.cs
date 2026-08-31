#nullable enable
using System;
using System.Collections.Generic;

namespace ARPG.Village
{
    public enum MinimumForgeEnsureResult
    {
        Unchanged,
        Created,
        TierNormalized,
        DataError,
    }

    public sealed class VillageInitialDevelopmentResult
    {
        public int MilestonesApplied;
        public int MilestonesSkippedAlreadyApplied;
        public int MilestonesSkippedInvalid;
        public int FacilitiesCreated;
        public int FacilityTierUpgrades;
        public int ShopTierResult;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// V3 (VILLAGE_REWORK_PLAN §3.5/§4): Settlement→InitialStage 누적 마일스톤을
    /// 자원·건설시간·진행 task 없이 완공 원장으로 정확히 한 번 적용한다.
    /// - 중복 방지 정본은 VillageData.AppliedGrowthMilestones의 MilestoneKey다.
    /// - 완공 부작용(Cap·서비스·세트)은 VillageConstructionLedger.ApplyCompletedBuild
    ///   단일 계약을 그대로 사용한다 (materialize 복원은 이 부작용을 재적용하지 않는다).
    /// - 좌표 미할당 완공 레코드는 PlacedObjects[].PlacementPending으로 보존되고
    ///   기존 materialize 경로(RestorePlacedObjectsFromData)가 결정론적으로 재시도한다
    ///   (§5.3 PendingInitialPlacements 역할 — 별도 리스트로 중복 저장하지 않는다).
    /// - UnityEngine에 의존하지 않는 순수 로직 — 로그는 호출자가 Errors/Warnings로 처리한다.
    /// </summary>
    public static class VillageInitialDevelopment
    {
        /// <summary>
        /// 신규 게임 마을의 공통 Forge 최소 보장. 종족별 성장 계획과 별개인 마지막
        /// 후조건으로 적용하며, Dwarf 계획이 만든 상위 Tier는 그대로 보존한다.
        /// PlacedObjects/PlacedObjectTypeIds가 불일치하거나 Forge가 중복된 데이터는
        /// 임의로 고치지 않고 오류로 거부한다.
        /// </summary>
        public static MinimumForgeEnsureResult EnsureMinimumForge(VillageData village,
            Func<int, Tables.BuildableItemTable?> tableResolver, out string error)
        {
            error = string.Empty;
            int forgeTableId = VillageInitialFacilityCatalog.FORGE_TABLE_ID;
            int recordCount = 0;
            int typeIdCount = 0;
            PlacedObjectSaveData? forgeRecord = null;

            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (placed.TableId != forgeTableId)
                    continue;

                recordCount++;
                forgeRecord = placed;
            }

            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                if (village.PlacedObjectTypeIds[i] == forgeTableId)
                    typeIdCount++;
            }

            if (recordCount > 1 || typeIdCount > 1 || recordCount != typeIdCount)
            {
                error = $"Village {village.VillageId}: Forge {forgeTableId} 원장 불일치/중복 " +
                    $"(records={recordCount}, typeIds={typeIdCount})";
                return MinimumForgeEnsureResult.DataError;
            }

            if (recordCount == 1)
            {
                if (forgeRecord!.FacilityTier >= 1)
                    return MinimumForgeEnsureResult.Unchanged;

                forgeRecord.FacilityTier = 1;
                return MinimumForgeEnsureResult.TierNormalized;
            }

            Tables.BuildableItemTable? table = tableResolver(forgeTableId);
            if (table == null || table.Id != forgeTableId
                || table.SpawnType != GlobalEnum.BuildableSpawnType.Entity
                || ((ProvidedService)table.ProvidedService & ProvidedService.Forge) == 0)
            {
                error = $"Village {village.VillageId}: Forge {forgeTableId} 테이블이 없거나 " +
                    "Entity/Forge 서비스 계약을 충족하지 않음";
                return MinimumForgeEnsureResult.DataError;
            }

            VillageConstructionLedger.ApplyCompletedBuild(village, table,
                VillageConstructionPosition.UnassignedTile,
                VillageConstructionPosition.UnassignedTile);
            village.PlacedObjects[village.PlacedObjects.Count - 1].FacilityTier = 1;
            return MinimumForgeEnsureResult.Created;
        }

        /// <summary>
        /// Profile → 성장 명세 결정. 저장된 GrowthPlanId가 카탈로그에 없거나 Profile이 없으면
        /// V1 fallback 정책과 같은 결정론 기본 명세를 사용하고 경고를 반환한다.
        /// </summary>
        public static VillageGrowthPlan ResolvePlan(VillageProfile? profile,
            out string warning)
        {
            if (profile == null)
            {
                warning = "VillageProfile 미확정 — 기본 성장 명세"
                    + $"({VillageGrowthPlanCatalog.DefaultPlan.GrowthPlanId})를 사용합니다.";
                return VillageGrowthPlanCatalog.DefaultPlan;
            }

            if (VillageGrowthPlanCatalog.TryGet(profile.GrowthPlanId,
                    out VillageGrowthPlan plan) == true)
            {
                warning = string.Empty;
                return plan;
            }

            warning = $"GrowthPlanId {profile.GrowthPlanId}가 카탈로그에 없음 — 기본 성장 명세"
                + $"({VillageGrowthPlanCatalog.DefaultPlan.GrowthPlanId})를 사용합니다.";
            return VillageGrowthPlanCatalog.DefaultPlan;
        }

        /// <summary>
        /// 성장 명세의 데이터 검증 (§5.4). 초기 시설은 반드시 V0 InteractiveFacility 허용 목록에 있고
        /// 상호작용 ID·프롬프트·제공 기능이 정의돼 있어야 하며, 테이블 행이 Entity 스폰이어야 한다.
        /// 건물형 비상호작용 오브젝트가 목록에 들어오면 여기서 실패한다.
        /// </summary>
        public static bool ValidatePlan(VillageGrowthPlan plan,
            Func<int, Tables.BuildableItemTable?> tableResolver, List<string> errors)
        {
            int before = errors.Count;
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> addedFacilities = new HashSet<int>();
            VillageStage previousStage = VillageStage.Settlement;

            for (int i = 0; i < plan.Milestones.Count; i++)
            {
                VillageGrowthMilestone milestone = plan.Milestones[i];

                if (keys.Add(milestone.MilestoneKey) == false)
                    errors.Add($"{plan.GrowthPlanId}: 중복 MilestoneKey {milestone.MilestoneKey}");
                if (milestone.Stage < previousStage)
                    errors.Add($"{plan.GrowthPlanId}: 마일스톤이 Stage 오름차순이 아님 " +
                        $"({milestone.MilestoneKey})");
                previousStage = milestone.Stage;

                switch (milestone.Type)
                {
                    case VillageGrowthRewardType.AddCoreFacility:
                    case VillageGrowthRewardType.AddSpecialFacility:
                        if (ValidateFacility(plan, milestone, tableResolver, errors) == true)
                            addedFacilities.Add(milestone.FacilityTableId);
                        break;
                    case VillageGrowthRewardType.UpgradeFacility:
                        if (ValidateFacility(plan, milestone, tableResolver, errors) == true
                            && addedFacilities.Contains(milestone.FacilityTableId) == false)
                        {
                            errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — " +
                                "하위 Stage에 같은 시설의 Add 마일스톤이 없어 누적 명세가 불완전함");
                        }
                        if (milestone.FacilityTier < 2)
                            errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — " +
                                "UpgradeFacility Tier는 2 이상이어야 함");
                        break;
                    case VillageGrowthRewardType.UnlockShopTier:
                        if (milestone.ShopTier < 1)
                            errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — " +
                                "ShopTier는 1 이상이어야 함");
                        break;
                    case VillageGrowthRewardType.ApplyStageCaps:
                        break;
                    default:
                        errors.Add($"{plan.GrowthPlanId}: 알 수 없는 보상 타입 {milestone.Type}");
                        break;
                }
            }

            return errors.Count == before;
        }

        /// <summary>
        /// Settlement부터 initialStage까지의 누적 마일스톤을 1회 적용한다.
        /// 자원을 소비·예약하지 않고 ActiveBuildTasks를 만들지 않으며, 이미 적용된 키는 건너뛴다.
        /// 시설은 좌표 미할당 완공 레코드로 생성되어 첫 materialize에서 배치된다.
        /// </summary>
        public static VillageInitialDevelopmentResult ApplyInitialMilestones(
            VillageData village, VillageGrowthPlan plan, VillageStage initialStage,
            Func<int, Tables.BuildableItemTable?> tableResolver)
        {
            VillageInitialDevelopmentResult result = new VillageInitialDevelopmentResult();

            if (village.AppliedGrowthMilestones == null)
                village.AppliedGrowthMilestones = new List<string>();

            List<string> validationErrors = new List<string>();
            ValidatePlan(plan, tableResolver, validationErrors);
            HashSet<string> invalidKeys = CollectInvalidFacilityKeys(plan, tableResolver);
            for (int i = 0; i < validationErrors.Count; i++)
                result.Errors.Add(validationErrors[i]);

            for (int i = 0; i < plan.Milestones.Count; i++)
            {
                VillageGrowthMilestone milestone = plan.Milestones[i];
                if (milestone.Stage > initialStage)
                    continue;
                if (invalidKeys.Contains(milestone.MilestoneKey) == true)
                {
                    result.MilestonesSkippedInvalid++;
                    continue;
                }
                if (village.AppliedGrowthMilestones.Contains(milestone.MilestoneKey) == true)
                {
                    result.MilestonesSkippedAlreadyApplied++;
                    continue;
                }

                switch (milestone.Type)
                {
                    case VillageGrowthRewardType.ApplyStageCaps:
                        ApplyStageCaps(village, milestone);
                        break;
                    case VillageGrowthRewardType.UnlockShopTier:
                        if (milestone.ShopTier > village.ShopTier)
                            village.ShopTier = milestone.ShopTier;
                        break;
                    case VillageGrowthRewardType.AddCoreFacility:
                    case VillageGrowthRewardType.AddSpecialFacility:
                        AddCompletedFacility(village, milestone, tableResolver, result);
                        break;
                    case VillageGrowthRewardType.UpgradeFacility:
                        UpgradeFacility(village, milestone, tableResolver, result);
                        break;
                }

                village.AppliedGrowthMilestones.Add(milestone.MilestoneKey);
                result.MilestonesApplied++;
            }

            result.ShopTierResult = village.ShopTier;
            return result;
        }

        /// <summary>
        /// 완공은 확정됐지만 월드 배치가 남은 초기 시설 수 (§5.3 PendingInitialPlacements).
        /// 정본은 PlacedObjects[].PlacementPending — materialize가 결정론적으로 재시도한다.
        /// </summary>
        public static int CountPendingInitialPlacements(VillageData village)
        {
            int count = 0;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                if (village.PlacedObjects[i].PlacementPending == true)
                    count++;
            }
            return count;
        }

        private static bool ValidateFacility(VillageGrowthPlan plan,
            VillageGrowthMilestone milestone,
            Func<int, Tables.BuildableItemTable?> tableResolver, List<string> errors)
        {
            int before = errors.Count;
            int tableId = milestone.FacilityTableId;

            if (VillageInitialFacilityCatalog.TryGet(tableId,
                    out VillageInitialFacilityDefinition definition) == false)
            {
                errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — TableId {tableId}는 " +
                    "V0 InteractiveFacility 허용 목록에 없는 건물형 비상호작용 오브젝트");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.InteractionId) == true
                || string.IsNullOrWhiteSpace(definition.Prompt) == true
                || string.IsNullOrWhiteSpace(definition.ProvidedFunction) == true)
            {
                errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — 시설 {tableId}의 " +
                    "상호작용 ID/프롬프트/제공 기능 계약이 비어 있음");
            }

            Tables.BuildableItemTable? table = tableResolver(tableId);
            if (table == null)
            {
                errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — " +
                    $"BuildableItemTable {tableId} 행 없음");
                return false;
            }
            if (table.SpawnType != GlobalEnum.BuildableSpawnType.Entity)
            {
                errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — 시설 {tableId}는 " +
                    $"SpawnType {table.SpawnType}라 미할당 완공 복원(배치 재시도)이 불가능함");
            }
            if (definition.RequiresTableServiceRouting == true
                && ((ProvidedService)table.ProvidedService & definition.RoutedService) == 0)
            {
                errors.Add($"{plan.GrowthPlanId}: {milestone.MilestoneKey} — 시설 {tableId}의 " +
                    $"테이블 ProvidedService에 라우팅 서비스 {definition.RoutedService} 비트가 없음");
            }

            return errors.Count == before;
        }

        private static HashSet<string> CollectInvalidFacilityKeys(VillageGrowthPlan plan,
            Func<int, Tables.BuildableItemTable?> tableResolver)
        {
            HashSet<string> invalid = new HashSet<string>(StringComparer.Ordinal);
            List<string> scratch = new List<string>();
            for (int i = 0; i < plan.Milestones.Count; i++)
            {
                VillageGrowthMilestone milestone = plan.Milestones[i];
                if (milestone.Type != VillageGrowthRewardType.AddCoreFacility
                    && milestone.Type != VillageGrowthRewardType.AddSpecialFacility
                    && milestone.Type != VillageGrowthRewardType.UpgradeFacility)
                    continue;

                scratch.Clear();
                if (ValidateFacility(plan, milestone, tableResolver, scratch) == false)
                    invalid.Add(milestone.MilestoneKey);
            }
            return invalid;
        }

        private static void ApplyStageCaps(VillageData village,
            VillageGrowthMilestone milestone)
        {
            if (milestone.CapAddFood != 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Food,
                    milestone.CapAddFood);
            if (milestone.CapAddWood != 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Wood,
                    milestone.CapAddWood);
            if (milestone.CapAddStone != 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Stone,
                    milestone.CapAddStone);
        }

        private static void AddCompletedFacility(VillageData village,
            VillageGrowthMilestone milestone,
            Func<int, Tables.BuildableItemTable?> tableResolver,
            VillageInitialDevelopmentResult result)
        {
            Tables.BuildableItemTable? table = tableResolver(milestone.FacilityTableId);
            if (table == null)
                return;   // CollectInvalidFacilityKeys에서 걸러지므로 도달하지 않음

            // 자원·건설시간 없이 완공 원장 생성 — 부작용은 단일 계약 한 곳에서만 적용된다.
            VillageConstructionLedger.ApplyCompletedBuild(village, table,
                VillageConstructionPosition.UnassignedTile,
                VillageConstructionPosition.UnassignedTile);
            PlacedObjectSaveData record =
                village.PlacedObjects[village.PlacedObjects.Count - 1];
            record.FacilityTier = milestone.FacilityTier > 0 ? milestone.FacilityTier : 1;
            result.FacilitiesCreated++;
        }

        /// <summary>
        /// 상위 Tier가 하위 Tier를 교체·흡수한다: 새 건물을 추가하지 않고 같은 시설 레코드의
        /// FacilityTier만 올린다 (Tier 교체 자산·기능 차이는 V4). 하위 레코드가 없으면
        /// 상위 Tier로 직접 생성해 흡수하고 경고를 남긴다.
        /// </summary>
        private static void UpgradeFacility(VillageData village,
            VillageGrowthMilestone milestone,
            Func<int, Tables.BuildableItemTable?> tableResolver,
            VillageInitialDevelopmentResult result)
        {
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (placed.TableId != milestone.FacilityTableId)
                    continue;

                int currentTier = placed.FacilityTier > 0 ? placed.FacilityTier : 1;
                if (milestone.FacilityTier > currentTier)
                    placed.FacilityTier = milestone.FacilityTier;
                result.FacilityTierUpgrades++;
                return;
            }

            result.Warnings.Add($"{milestone.MilestoneKey} — 하위 Tier 시설 " +
                $"{milestone.FacilityTableId} 레코드가 없어 상위 Tier로 직접 생성(흡수)");
            AddCompletedFacility(village, milestone, tableResolver, result);
        }
    }
}
