#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    public enum VillageAbstractAdvanceStatus
    {
        CaughtUp,
        GeneralBudgetExhausted,
        WallBudgetExhausted,
        NotOperational,
        InvalidTargetTime,
        Blocked,
    }

    public sealed class VillageAbstractAdvanceOptions
    {
        public int GeneralEventBudget = 128;
        public int WallEventBudget = 64;
        public int MaxGeneralBuildings = int.MaxValue;
        public VillageStage MaxStage = VillageStage.Town;
        public float MaxWallCompletionRatio = 1f;
        public bool UseBootstrapWallLimit;
        public float BootstrapMaturity;
        public float BootstrapWallSeedFactor;
        public bool IsOperational = true;
        // V2: false면 자원 생산 catch-up과 clock 정렬만 수행하고, task 진행·완공·착공·
        // Tier 승격·벽 계획 등 모든 발전 이벤트를 실행하지 않는다(런타임 고정 정책).
        // 기본값 true — bootstrap/parity/진단 등 기존 호출부의 동작을 바꾸지 않는다.
        public bool AllowDevelopment = true;
    }

    public sealed class VillageAbstractAdvanceResult
    {
        public VillageAbstractAdvanceStatus Status;
        public float ReachedGameTime;
        public int GeneralEventsProcessed;
        public int WallEventsProcessed;
        public int GeneralBuildsCompleted;
        public int WallSegmentsCompleted;
        public readonly List<VillageStage> Promotions = new();

        public bool IsCaughtUp => Status == VillageAbstractAdvanceStatus.CaughtUp;
    }

    /// <summary>
    /// ECS와 맵을 읽지 않고 VillageData 원장만 이벤트 단위로 전진한다.
    /// System_SettlementSim과 HistoryManager materialize catch-up이 이 코어를 공유한다.
    /// </summary>
    public static class VillageAbstractConstruction
    {
        public const int DefaultGeneralEventBudget = 128;
        public const int DefaultWallEventBudget = 64;

        private const float TIER_CHECK_INTERVAL_HOURS = 4f;
        private const float EPSILON = 0.0001f;

        private enum WorkKind
        {
            None,
            General,
            Wall,
        }

        private readonly struct StartDecision
        {
            public readonly WorkKind Kind;
            public readonly Tables.BuildableItemTable? Table;
            public readonly WallSegmentSaveData? WallSegment;

            public StartDecision(WorkKind kind, Tables.BuildableItemTable? table,
                WallSegmentSaveData? wallSegment)
            {
                Kind = kind;
                Table = table;
                WallSegment = wallSegment;
            }
        }

        public static VillageAbstractAdvanceResult AdvanceAbstractConstruction(
            VillageData village, float now, int generalEventBudget, int wallEventBudget,
            bool isOperational = true)
        {
            return AdvanceAbstractConstruction(village, now, new VillageAbstractAdvanceOptions
            {
                GeneralEventBudget = generalEventBudget,
                WallEventBudget = wallEventBudget,
                IsOperational = isOperational,
            });
        }

        public static VillageAbstractAdvanceResult AdvanceAbstractConstruction(
            VillageData village, float now, VillageAbstractAdvanceOptions options)
        {
            VillageAbstractAdvanceResult result = new VillageAbstractAdvanceResult();
            EnsureCollections(village);

            if (options.IsOperational == false)
            {
                village.LastConstructionSimGameTime = now;
                village.LastConstructionTierCheckGameTime = now;
                result.Status = VillageAbstractAdvanceStatus.NotOperational;
                result.ReachedGameTime = now;
                return result;
            }

            float current = village.LastConstructionSimGameTime;
            if (now + EPSILON < current)
            {
                result.Status = VillageAbstractAdvanceStatus.InvalidTargetTime;
                result.ReachedGameTime = current;
                return result;
            }

            if (options.AllowDevelopment == false)
            {
                AdvanceFrozen(village, current, now);
                result.Status = VillageAbstractAdvanceStatus.CaughtUp;
                return Finish(result, village);
            }

            int generalBudget = Mathf.Max(0, options.GeneralEventBudget);
            int wallBudget = Mathf.Max(0, options.WallEventBudget);

            while (true)
            {
                current = village.LastConstructionSimGameTime;

                int completedIndex = FindCompletedTaskIndex(village);
                if (completedIndex >= 0)
                {
                    BuildTaskSnapshot completed = village.ActiveBuildTasks[completedIndex];
                    WorkKind completedKind = VillageWallRules.IsWallTable(completed.TableId)
                        ? WorkKind.Wall
                        : WorkKind.General;
                    if (TryConsumeBudget(completedKind, ref generalBudget, ref wallBudget,
                            result) == false)
                        return Finish(result, village);

                    CompleteTask(village, completedIndex, completed, result);
                    continue;
                }

                if (current - village.LastConstructionTierCheckGameTime
                    >= TIER_CHECK_INTERVAL_HOURS - EPSILON)
                {
                    if (TryConsumeBudget(WorkKind.General, ref generalBudget, ref wallBudget,
                            result) == false)
                        return Finish(result, village);

                    village.LastConstructionTierCheckGameTime = current;
                    TryPromote(village, current, options.MaxStage, result);
                    continue;
                }

                if (village.WallPlanRequested)
                {
                    if (TryConsumeBudget(WorkKind.Wall, ref generalBudget, ref wallBudget,
                            result) == false)
                        return Finish(result, village);
                    if (VillageWallRules.PlanWalls(village) == false)
                    {
                        result.Status = VillageAbstractAdvanceStatus.Blocked;
                        return Finish(result, village);
                    }
                    continue;
                }

                StartDecision decision = default;
                if (village.ActiveBuildTasks.Count == 0)
                {
                    decision = GetStartDecision(village, options);
                    if (decision.Kind != WorkKind.None
                        && decision.Table != null
                        && VillageConstructionRules.CanAfford(village, decision.Table))
                    {
                        if (TryConsumeBudget(decision.Kind, ref generalBudget, ref wallBudget,
                                result) == false)
                            return Finish(result, village);

                        StartTask(village, current, decision);
                        continue;
                    }
                }

                if (current >= now - EPSILON)
                {
                    village.LastConstructionSimGameTime = now;
                    result.Status = VillageAbstractAdvanceStatus.CaughtUp;
                    return Finish(result, village);
                }

                WorkKind advanceKind;
                float next = FindNextEventTime(village, current, now, decision, options,
                    out advanceKind);
                if (next <= current + EPSILON)
                {
                    result.Status = VillageAbstractAdvanceStatus.Blocked;
                    return Finish(result, village);
                }

                if (TryConsumeBudget(advanceKind, ref generalBudget, ref wallBudget,
                        result) == false)
                    return Finish(result, village);

                AdvanceTo(village, current, next);
            }
        }

        /// <summary>
        /// V2 런타임 고정 catch-up: 자원 생산만 정수 시간 단위로 전진시키고 두 construction clock을
        /// 현재 시각으로 정렬한다. 진행 task의 AccumulatedHours는 건드리지 않으므로(동결·보존)
        /// 시간 경과만으로 시설·Stage·벽이 늘지 않는다. O(1) — 이벤트 예산이 필요 없다.
        /// </summary>
        private static void AdvanceFrozen(VillageData village, float current, float now)
        {
            int wholeHours = Mathf.FloorToInt(now + EPSILON)
                - Mathf.FloorToInt(current + EPSILON);
            VillageResourceSimulation.AdvanceWholeHours(village, wholeHours);
            village.LastConstructionSimGameTime = now;
            village.LastConstructionTierCheckGameTime = now;
        }

        private static void EnsureCollections(VillageData village)
        {
            if (village.Resources == null)
                village.Resources = new Dictionary<GlobalEnum.ItemType, int>();
            if (village.ResourceCaps == null)
                village.ResourceCaps = new Dictionary<GlobalEnum.ItemType, int>();
            if (village.ActiveBuildTasks == null)
                village.ActiveBuildTasks = new List<BuildTaskSnapshot>();
            if (village.PlacedObjectTypeIds == null)
                village.PlacedObjectTypeIds = new List<int>();
            if (village.PlacedObjects == null)
                village.PlacedObjects = new List<PlacedObjectSaveData>();
            if (village.WallSegments == null)
                village.WallSegments = new List<WallSegmentSaveData>();
        }

        private static VillageAbstractAdvanceResult Finish(VillageAbstractAdvanceResult result,
            VillageData village)
        {
            result.ReachedGameTime = village.LastConstructionSimGameTime;
            return result;
        }

        private static bool TryConsumeBudget(WorkKind kind, ref int generalBudget,
            ref int wallBudget, VillageAbstractAdvanceResult result)
        {
            if (kind == WorkKind.Wall)
            {
                if (wallBudget <= 0)
                {
                    result.Status = VillageAbstractAdvanceStatus.WallBudgetExhausted;
                    return false;
                }
                wallBudget--;
                result.WallEventsProcessed++;
                return true;
            }

            if (generalBudget <= 0)
            {
                result.Status = VillageAbstractAdvanceStatus.GeneralBudgetExhausted;
                return false;
            }
            generalBudget--;
            result.GeneralEventsProcessed++;
            return true;
        }

        private static int FindCompletedTaskIndex(VillageData village)
        {
            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[i];
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TableId);
                if (table == null || task.AccumulatedHours + EPSILON
                    >= VillageConstructionRules.GetBuildHours(table))
                    return i;
            }
            return -1;
        }

        private static void CompleteTask(VillageData village, int taskIndex,
            BuildTaskSnapshot task, VillageAbstractAdvanceResult result)
        {
            village.ActiveBuildTasks.RemoveAt(taskIndex);
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TableId);
            if (table == null)
            {
                VillageConstructionRules.Refund(village, task);
                return;
            }

            if (VillageWallRules.IsWallTable(task.TableId))
            {
                if (VillageWallRules.MarkSegmentBuiltAt(village, task.TileX, task.TileY))
                {
                    result.WallSegmentsCompleted++;
                }
                return;
            }

            int tileX = VillageConstructionPosition.IsAssigned(task) == true
                ? task.TileX
                : VillageConstructionPosition.UnassignedTile;
            int tileY = VillageConstructionPosition.IsAssigned(task) == true
                ? task.TileY
                : VillageConstructionPosition.UnassignedTile;
            VillageConstructionLedger.ApplyCompletedBuild(village, table, tileX, tileY,
                task.BuildingEntityId);
            result.GeneralBuildsCompleted++;
        }

        private static void TryPromote(VillageData village, float now, VillageStage maxStage,
            VillageAbstractAdvanceResult result)
        {
            if (village.Stage >= maxStage || village.Stage >= VillageStage.City)
                return;

            VillageStage next = village.Stage + 1;
            Tables.VillageStageTable? table = AR.s.Data.GetVillageStage(next);
            if (table == null || VillageProgressionRules.CanPromoteTo(village, table, now) == false)
                return;

            VillageProgressionRules.ApplyPromotion(village, next,
                VillageManager.GetBoundsRadius(next));
            result.Promotions.Add(next);
        }

        private static StartDecision GetStartDecision(VillageData village,
            VillageAbstractAdvanceOptions options)
        {
            WallSegmentSaveData? wall = WallSegmentRegistry.GetNextUnbuiltSegment(village);
            float wallRatioLimit = options.MaxWallCompletionRatio;
            if (options.UseBootstrapWallLimit)
            {
                wallRatioLimit = Mathf.Min(wallRatioLimit,
                    VillageWallRules.GetBootstrapCompletionRatio(village.Stage,
                        options.BootstrapMaturity, options.BootstrapWallSeedFactor));
            }
            bool wallAllowed = wall != null
                && VillageWallRules.CanBuildAnotherSegment(village,
                    wallRatioLimit);
            if (wallAllowed && wall!.Orient == (int)WallOrientation.Gate)
                return CreateWallDecision(wall);

            if (CountCommittedGeneralBuildings(village) < options.MaxGeneralBuildings)
            {
                List<int> ranked = VillageNeedsEvaluator.GetRankedCandidates(village);
                if (ranked.Count > 0)
                {
                    Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(ranked[0]);
                    if (table != null)
                        return new StartDecision(WorkKind.General, table, null);
                }
            }

            if (wallAllowed)
                return CreateWallDecision(wall);
            return default;
        }

        private static StartDecision CreateWallDecision(WallSegmentSaveData? wall)
        {
            if (wall == null)
                return default;
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(
                VillageWallRules.GetTableId(wall));
            return table == null
                ? default
                : new StartDecision(WorkKind.Wall, table, wall);
        }

        private static int CountCommittedGeneralBuildings(VillageData village)
        {
            int count = village.PlacedObjectTypeIds.Count;
            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                if (VillageWallRules.IsWallTable(village.ActiveBuildTasks[i].TableId) == false)
                    count++;
            }
            return count;
        }

        private static void StartTask(VillageData village, float current,
            StartDecision decision)
        {
            Tables.BuildableItemTable table = decision.Table!;
            if (VillageConstructionRules.TryReserve(village, table) == false)
                return;

            bool isWall = decision.Kind == WorkKind.Wall;
            BuildTaskSnapshot task = new BuildTaskSnapshot
            {
                TableId = table.Id,
                StartedAt = current,
                AccumulatedHours = 0f,
                TileX = isWall
                    ? decision.WallSegment!.TileX
                    : VillageConstructionPosition.UnassignedTile,
                TileY = isWall
                    ? decision.WallSegment!.TileY
                    : VillageConstructionPosition.UnassignedTile,
                ReservedWood = table.Cost_Wood,
                ReservedStone = isWall ? 0 : table.Cost_Stone,
                BuildingEntityId = -1,
                PlacementPending = isWall == false,
            };
            VillageConstructionLedger.UpsertTask(village, task);
        }

        private static float FindNextEventTime(VillageData village, float current, float now,
            StartDecision waitingDecision, VillageAbstractAdvanceOptions options,
            out WorkKind eventKind)
        {
            float next = now;
            eventKind = WorkKind.General;

            if (village.ActiveBuildTasks.Count > 0)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[0];
                eventKind = VillageWallRules.IsWallTable(task.TableId)
                    ? WorkKind.Wall
                    : WorkKind.General;
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TableId);
                if (table != null)
                {
                    float remaining = Mathf.Max(0f,
                        VillageConstructionRules.GetBuildHours(table) - task.AccumulatedHours);
                    float completion = current + remaining;
                    if (completion < next + EPSILON)
                    {
                        next = Mathf.Min(next, completion);
                    }
                }
            }
            else if (waitingDecision.Kind != WorkKind.None && waitingDecision.Table != null)
            {
                eventKind = waitingDecision.Kind;
                float affordable = GetAffordableEventTime(village, waitingDecision.Table, current);
                if (affordable < next + EPSILON)
                {
                    next = Mathf.Min(next, affordable);
                    eventKind = waitingDecision.Kind;
                }
            }

            float tierCheck = village.LastConstructionTierCheckGameTime
                + TIER_CHECK_INTERVAL_HOURS;
            if (village.Stage < options.MaxStage
                && tierCheck > current + EPSILON
                && tierCheck < next - EPSILON)
            {
                next = tierCheck;
                eventKind = WorkKind.General;
            }

            if (village.Stage < options.MaxStage)
            {
                float hourBoundary = Mathf.Floor(current + EPSILON) + 1f;
                if (hourBoundary < next - EPSILON)
                {
                    next = hourBoundary;
                    eventKind = WorkKind.General;
                }
            }

            return next;
        }

        private static float GetAffordableEventTime(VillageData village,
            Tables.BuildableItemTable table, float current)
        {
            int woodHours = VillageResourceSimulation.GetHoursUntilAmount(village,
                GlobalEnum.ItemType.Wood, table.Cost_Wood);
            if (woodHours < 0)
                return float.PositiveInfinity;

            int requiredHours = woodHours;
            if (VillageWallRules.IsWallTable(table.Id) == false)
            {
                int stoneHours = VillageResourceSimulation.GetHoursUntilAmount(village,
                    GlobalEnum.ItemType.Stone, table.Cost_Stone);
                if (stoneHours < 0)
                    return float.PositiveInfinity;
                requiredHours = Mathf.Max(requiredHours, stoneHours);
            }
            if (requiredHours <= 0)
                return current;
            return Mathf.Floor(current + EPSILON) + requiredHours;
        }

        private static void AdvanceTo(VillageData village, float current, float next)
        {
            int wholeHours = Mathf.FloorToInt(next + EPSILON)
                - Mathf.FloorToInt(current + EPSILON);
            VillageResourceSimulation.AdvanceWholeHours(village, wholeHours);

            float elapsed = next - current;
            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[i];
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TableId);
                if (table == null)
                    continue;
                task.AccumulatedHours = Mathf.Min(
                    VillageConstructionRules.GetBuildHours(table),
                    task.AccumulatedHours + elapsed);
            }

            village.LastConstructionSimGameTime = next;
        }
    }
}
