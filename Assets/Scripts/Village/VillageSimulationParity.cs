#nullable enable
using System.Text;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 실시간 시간당 참조 실행과 추상 fast-forward 결과의 정합성 검증용 비교기.
    /// 호출자가 동일 초기 상태를 두 경로로 전진한 뒤 결과를 전달한다.
    /// </summary>
    public static class VillageSimulationParity
    {
        private static readonly GlobalEnum.ItemType[] _resourceTypes =
        {
            GlobalEnum.ItemType.Food,
            GlobalEnum.ItemType.Wood,
            GlobalEnum.ItemType.Stone,
            GlobalEnum.ItemType.Iron,
            GlobalEnum.ItemType.Gold,
        };

        /// <summary>
        /// 동일하게 복제된 두 VillageData를 각각 시간 경계 단위 참조 경로와 이벤트 fast-forward로
        /// 전진한 뒤 자원/carry/완공 목록/task/Stage를 비교한다.
        /// </summary>
        public static bool AdvanceAndCompare(VillageData realtimeReference,
            VillageData abstractFastForward, float targetGameTime,
            VillageAbstractAdvanceOptions options, out string differences)
        {
            VillageAbstractAdvanceOptions validationOptions = CopyOptions(options);
            validationOptions.GeneralEventBudget = int.MaxValue;
            validationOptions.WallEventBudget = int.MaxValue;

            while (realtimeReference.LastConstructionSimGameTime
                < targetGameTime - 0.0001f)
            {
                float nextHour = Mathf.Floor(
                    realtimeReference.LastConstructionSimGameTime + 0.0001f) + 1f;
                float next = Mathf.Min(targetGameTime, nextHour);
                VillageAbstractAdvanceResult step =
                    VillageAbstractConstruction.AdvanceAbstractConstruction(realtimeReference,
                        next, validationOptions);
                if (step.IsCaughtUp == false)
                {
                    differences = $"hourly reference stopped: {step.Status} at "
                        + $"{step.ReachedGameTime}";
                    return false;
                }
            }

            VillageAbstractAdvanceResult fast =
                VillageAbstractConstruction.AdvanceAbstractConstruction(abstractFastForward,
                    targetGameTime, validationOptions);
            if (fast.IsCaughtUp == false)
            {
                differences = $"fast-forward stopped: {fast.Status} at {fast.ReachedGameTime}";
                return false;
            }

            return Compare(realtimeReference, abstractFastForward, out differences);
        }

        public static bool AdvanceSlicedAndCompare(VillageData uninterrupted,
            VillageData sliced, float targetGameTime,
            VillageAbstractAdvanceOptions slicedOptions, int maxSlices,
            out string differences)
        {
            VillageAbstractAdvanceOptions unlimited = CopyOptions(slicedOptions);
            unlimited.GeneralEventBudget = int.MaxValue;
            unlimited.WallEventBudget = int.MaxValue;
            VillageAbstractAdvanceResult reference =
                VillageAbstractConstruction.AdvanceAbstractConstruction(uninterrupted,
                    targetGameTime, unlimited);
            if (reference.IsCaughtUp == false)
            {
                differences = $"uninterrupted reference stopped: {reference.Status}";
                return false;
            }

            int slices = 0;
            while (slices < maxSlices)
            {
                VillageAbstractAdvanceResult result =
                    VillageAbstractConstruction.AdvanceAbstractConstruction(sliced,
                        targetGameTime, slicedOptions);
                slices++;
                if (result.IsCaughtUp == true)
                    return Compare(uninterrupted, sliced, out differences);
                if (result.Status != VillageAbstractAdvanceStatus.GeneralBudgetExhausted
                    && result.Status != VillageAbstractAdvanceStatus.WallBudgetExhausted)
                {
                    differences = $"sliced advance stopped: {result.Status} at "
                        + result.ReachedGameTime;
                    return false;
                }
            }

            differences = $"sliced advance exceeded {maxSlices} slices at "
                + sliced.LastConstructionSimGameTime;
            return false;
        }

        public static bool ValidateMaterializedLedger(VillageData village,
            out string differences)
        {
            StringBuilder builder = new StringBuilder();
            if (village.PlacedObjectTypeIds.Count != village.PlacedObjects.Count)
                builder.Append("completed type/object count mismatch; ");

            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (placed.PlacementPending == true)
                    builder.Append($"placed[{i}] pending; ");
                if (VillageConstructionPosition.IsAssigned(placed) == false)
                    builder.Append($"placed[{i}] unassigned; ");
            }

            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[i];
                if (task.PlacementPending == true)
                    builder.Append($"task[{i}] pending; ");
                if (VillageConstructionPosition.IsAssigned(task) == false)
                    builder.Append($"task[{i}] unassigned; ");
                if (VillageWallRules.IsWallTable(task.TableId) == false
                    && task.BuildingEntityId < 0)
                    builder.Append($"task[{i}] missing building; ");
            }

            differences = builder.ToString();
            return differences.Length == 0;
        }

        public static bool Compare(VillageData realtime, VillageData abstracted,
            out string differences, float floatTolerance = 0.0001f)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < _resourceTypes.Length; i++)
            {
                GlobalEnum.ItemType type = _resourceTypes[i];
                int realtimeAmount = VillageResourceSimulation.GetAmount(realtime, type);
                int abstractAmount = VillageResourceSimulation.GetAmount(abstracted, type);
                if (realtimeAmount != abstractAmount)
                    builder.Append($"{type} amount {realtimeAmount}!={abstractAmount}; ");

                int realtimeCap = VillageResourceSimulation.GetCap(realtime, type);
                int abstractCap = VillageResourceSimulation.GetCap(abstracted, type);
                if (realtimeCap != abstractCap)
                    builder.Append($"{type} cap {realtimeCap}!={abstractCap}; ");
            }

            CompareFloat(builder, "FoodCarry", realtime.FoodCarry, abstracted.FoodCarry,
                floatTolerance);
            CompareFloat(builder, "WoodCarry", realtime.WoodCarry, abstracted.WoodCarry,
                floatTolerance);
            CompareFloat(builder, "StoneCarry", realtime.StoneCarry, abstracted.StoneCarry,
                floatTolerance);
            CompareFloat(builder, "IronCarry", realtime.IronCarry, abstracted.IronCarry,
                floatTolerance);
            CompareFloat(builder, "GoldCarry", realtime.GoldCarry, abstracted.GoldCarry,
                floatTolerance);
            CompareFloat(builder, "LastConstructionSimGameTime",
                realtime.LastConstructionSimGameTime,
                abstracted.LastConstructionSimGameTime, floatTolerance);
            CompareFloat(builder, "LastConstructionTierCheckGameTime",
                realtime.LastConstructionTierCheckGameTime,
                abstracted.LastConstructionTierCheckGameTime, floatTolerance);

            if (realtime.Stage != abstracted.Stage)
                builder.Append($"Stage {realtime.Stage}!={abstracted.Stage}; ");
            if (realtime.PlacedObjectTypeIds.Count != abstracted.PlacedObjectTypeIds.Count)
            {
                builder.Append("completed count ")
                    .Append(realtime.PlacedObjectTypeIds.Count)
                    .Append("!=")
                    .Append(abstracted.PlacedObjectTypeIds.Count)
                    .Append("; ");
            }
            else
            {
                for (int i = 0; i < realtime.PlacedObjectTypeIds.Count; i++)
                {
                    if (realtime.PlacedObjectTypeIds[i] != abstracted.PlacedObjectTypeIds[i])
                    {
                        builder.Append($"completed[{i}] ")
                            .Append(realtime.PlacedObjectTypeIds[i])
                            .Append("!=")
                            .Append(abstracted.PlacedObjectTypeIds[i])
                            .Append("; ");
                    }
                }
            }

            if (realtime.ActiveBuildTasks.Count != abstracted.ActiveBuildTasks.Count)
            {
                builder.Append("active task count ")
                    .Append(realtime.ActiveBuildTasks.Count)
                    .Append("!=")
                    .Append(abstracted.ActiveBuildTasks.Count)
                    .Append("; ");
            }
            else
            {
                for (int i = 0; i < realtime.ActiveBuildTasks.Count; i++)
                {
                    BuildTaskSnapshot realtimeTask = realtime.ActiveBuildTasks[i];
                    BuildTaskSnapshot abstractTask = abstracted.ActiveBuildTasks[i];
                    if (realtimeTask.TableId != abstractTask.TableId)
                        builder.Append($"task[{i}].TableId ")
                            .Append(realtimeTask.TableId).Append("!=")
                            .Append(abstractTask.TableId).Append("; ");
                    CompareFloat(builder, $"task[{i}].AccumulatedHours",
                        realtimeTask.AccumulatedHours, abstractTask.AccumulatedHours,
                        floatTolerance);
                    if (realtimeTask.TileX != abstractTask.TileX
                        || realtimeTask.TileY != abstractTask.TileY)
                        builder.Append($"task[{i}].tile mismatch; ");
                    if (realtimeTask.ReservedWood != abstractTask.ReservedWood
                        || realtimeTask.ReservedStone != abstractTask.ReservedStone)
                        builder.Append($"task[{i}].reserved mismatch; ");
                    if (realtimeTask.PlacementPending
                        != abstractTask.PlacementPending)
                        builder.Append($"task[{i}].pending mismatch; ");
                }
            }

            if (realtime.CompletedWallSegments != abstracted.CompletedWallSegments)
                builder.Append("wall completed ")
                    .Append(realtime.CompletedWallSegments).Append("!=")
                    .Append(abstracted.CompletedWallSegments).Append("; ");
            if (realtime.WallSegments.Count != abstracted.WallSegments.Count)
                builder.Append("wall segment count mismatch; ");
            else
            {
                for (int i = 0; i < realtime.WallSegments.Count; i++)
                {
                    WallSegmentSaveData realtimeWall = realtime.WallSegments[i];
                    WallSegmentSaveData abstractWall = abstracted.WallSegments[i];
                    if (realtimeWall.IsBuilt != abstractWall.IsBuilt
                        || realtimeWall.TileX != abstractWall.TileX
                        || realtimeWall.TileY != abstractWall.TileY)
                        builder.Append($"wall[{i}] mismatch; ");
                }
            }

            differences = builder.ToString();
            return differences.Length == 0;
        }

        private static VillageAbstractAdvanceOptions CopyOptions(
            VillageAbstractAdvanceOptions source)
        {
            return new VillageAbstractAdvanceOptions
            {
                GeneralEventBudget = source.GeneralEventBudget,
                WallEventBudget = source.WallEventBudget,
                MaxGeneralBuildings = source.MaxGeneralBuildings,
                MaxStage = source.MaxStage,
                MaxWallCompletionRatio = source.MaxWallCompletionRatio,
                UseBootstrapWallLimit = source.UseBootstrapWallLimit,
                BootstrapMaturity = source.BootstrapMaturity,
                BootstrapWallSeedFactor = source.BootstrapWallSeedFactor,
                IsOperational = source.IsOperational,
            };
        }

        private static void CompareFloat(StringBuilder builder, string name, float realtime,
            float abstracted, float tolerance)
        {
            if (Mathf.Abs(realtime - abstracted) > tolerance)
                builder.Append(name).Append(' ')
                    .Append(realtime).Append("!=").Append(abstracted).Append("; ");
        }
    }
}
