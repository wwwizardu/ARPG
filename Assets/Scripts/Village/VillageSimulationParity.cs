#nullable enable
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>NPC 거래·치료로 마을 Gold가 움직인 1건 (패리티 검증 입력). Amount &gt; 0 입금, &lt; 0 출금.</summary>
    public struct VillageNpcGoldTransfer
    {
        public float GameTime;
        public int Amount;

        public VillageNpcGoldTransfer(float gameTime, int amount)
        {
            GameTime = gameTime;
            Amount = amount;
        }
    }

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

        /// <summary>
        /// NPC_AMBITION_DESIGN §9, D41: NPC 거래·치료 Gold 이동이 섞여도 두 경로가 같은 결과를 내는지 검증한다.
        /// 실시간 참조는 시간 경계마다 전진하며 그 시각까지의 이동을 적용하고, 추상 경로는 이동 시각까지
        /// fast-forward한 뒤 같은 이동을 적용한다. 두 경로 모두 VillageResourceSimulation의 NPC 전용 Gold
        /// 경로(상한 없음·잔액 부족 시 실패)를 쓰므로 Gold·carry·건설 결과와 실패 건수가 같아야 한다.
        /// transfers는 GameTime 오름차순이어야 한다. Amount &gt; 0 = 마을 입금, &lt; 0 = 마을 출금.
        /// </summary>
        public static bool AdvanceWithNpcGoldAndCompare(VillageData realtimeReference,
            VillageData abstractFastForward, float targetGameTime,
            VillageAbstractAdvanceOptions options, IReadOnlyList<VillageNpcGoldTransfer> transfers,
            out string differences)
        {
            VillageAbstractAdvanceOptions validationOptions = CopyOptions(options);
            validationOptions.GeneralEventBudget = int.MaxValue;
            validationOptions.WallEventBudget = int.MaxValue;

            int realtimeNext = 0;
            int realtimeRejected = 0;
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
                realtimeNext = ApplyNpcGoldTransfersUntil(realtimeReference, transfers, realtimeNext,
                    next, ref realtimeRejected);
            }
            realtimeNext = ApplyNpcGoldTransfersUntil(realtimeReference, transfers, realtimeNext,
                float.MaxValue, ref realtimeRejected);

            int abstractRejected = 0;
            for (int i = 0; i < transfers.Count; i++)
            {
                float at = Mathf.Min(transfers[i].GameTime, targetGameTime);
                if (at > abstractFastForward.LastConstructionSimGameTime)
                {
                    VillageAbstractAdvanceResult partial =
                        VillageAbstractConstruction.AdvanceAbstractConstruction(abstractFastForward,
                            at, validationOptions);
                    if (partial.IsCaughtUp == false)
                    {
                        differences = $"fast-forward stopped: {partial.Status} at {partial.ReachedGameTime}";
                        return false;
                    }
                }
                ApplyNpcGoldTransfer(abstractFastForward, transfers[i], ref abstractRejected);
            }
            VillageAbstractAdvanceResult fast =
                VillageAbstractConstruction.AdvanceAbstractConstruction(abstractFastForward,
                    targetGameTime, validationOptions);
            if (fast.IsCaughtUp == false)
            {
                differences = $"fast-forward stopped: {fast.Status} at {fast.ReachedGameTime}";
                return false;
            }

            bool same = Compare(realtimeReference, abstractFastForward, out differences);
            if (realtimeRejected != abstractRejected)
            {
                differences += $"npc gold rejected {realtimeRejected}!={abstractRejected}; ";
                same = false;
            }
            return same;
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
            CompareFloat(builder, "VaultUpkeepCarry", realtime.VaultUpkeepCarry,
                abstracted.VaultUpkeepCarry, floatTolerance);
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

        /// <summary>
        /// V7 (VILLAGE_REWORK_PLAN §12): 고정 인구/Profile/초기 Stage/누적 시설·상점 상태 비교.
        /// 추상/실시간 <see cref="Compare"/>(자원·carry·task·clock·좌표)와 별개로, New Game·save/load
        /// 왕복·동결 advance·fold-back 반복 전후에 "완성형 마을"의 정체성이 불변인지 검증한다.
        /// 시설은 좌표가 아니라 (TableId, FacilityTier) 멀티셋으로, 상점은 결정론 roll 순서대로 비교한다.
        /// </summary>
        public static bool CompareFixedState(VillageData a, VillageData b,
            out string differences, float floatTolerance = 0.0001f)
        {
            StringBuilder builder = new StringBuilder();

            if (a.Population != b.Population)
                builder.Append($"Population {a.Population}!={b.Population}; ");
            if (a.VillageProfileId != b.VillageProfileId)
                builder.Append($"Profile {a.VillageProfileId}!={b.VillageProfileId}; ");
            if (a.Stage != b.Stage)
                builder.Append($"Stage {a.Stage}!={b.Stage}; ");
            if (a.ShopTier != b.ShopTier)
                builder.Append($"ShopTier {a.ShopTier}!={b.ShopTier}; ");
            if (a.MerchantRestockCycle != b.MerchantRestockCycle)
                builder.Append($"MerchantRestockCycle {a.MerchantRestockCycle}"
                    + $"!={b.MerchantRestockCycle}; ");
            CompareFloat(builder, "LastMerchantRollGameTime",
                a.LastMerchantRollGameTime, b.LastMerchantRollGameTime, floatTolerance);
            // NPC_AMBITION P1b: NPC 구매 상점 보호 원장 (save/load 왕복 보존 확인)
            if (a.NpcPurchaseCycle != b.NpcPurchaseCycle || a.NpcPurchaseCount != b.NpcPurchaseCount)
                builder.Append($"NpcPurchase {a.NpcPurchaseCycle}/{a.NpcPurchaseCount}"
                    + $"!={b.NpcPurchaseCycle}/{b.NpcPurchaseCount}; ");
            CompareFloat(builder, "LastNpcTradeGameTime",
                a.LastNpcTradeGameTime, b.LastNpcTradeGameTime, floatTolerance);

            CompareStringMultiset(builder, "milestone",
                a.AppliedGrowthMilestones, b.AppliedGrowthMilestones);
            CompareFacilityMultiset(builder, a.PlacedObjects, b.PlacedObjects);
            CompareMerchantStock(builder, a.MerchantStock, b.MerchantStock);

            differences = builder.ToString();
            return differences.Length == 0;
        }

        /// <summary>순서 무관 멀티셋 비교 — 누락·중복(무중복 계약 위반)을 모두 잡는다.</summary>
        private static void CompareStringMultiset(StringBuilder builder, string label,
            List<string>? a, List<string>? b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                builder.Append($"{label} count {countA}!={countB}; ");
                return;
            }
            List<string> sortedA = a != null ? new List<string>(a) : new List<string>();
            List<string> sortedB = b != null ? new List<string>(b) : new List<string>();
            sortedA.Sort(string.CompareOrdinal);
            sortedB.Sort(string.CompareOrdinal);
            for (int i = 0; i < sortedA.Count; i++)
            {
                if (sortedA[i] != sortedB[i])
                {
                    builder.Append($"{label}[{i}] {sortedA[i]}!={sortedB[i]}; ");
                    return;
                }
            }
        }

        /// <summary>시설 정체성·Tier 비교 — 좌표/배치 상태와 무관하게 (TableId, FacilityTier) 멀티셋.</summary>
        private static void CompareFacilityMultiset(StringBuilder builder,
            List<PlacedObjectSaveData>? a, List<PlacedObjectSaveData>? b)
        {
            List<string> keysA = FacilityKeys(a);
            List<string> keysB = FacilityKeys(b);
            if (keysA.Count != keysB.Count)
            {
                builder.Append($"facility count {keysA.Count}!={keysB.Count}; ");
                return;
            }
            keysA.Sort(string.CompareOrdinal);
            keysB.Sort(string.CompareOrdinal);
            for (int i = 0; i < keysA.Count; i++)
            {
                if (keysA[i] != keysB[i])
                {
                    builder.Append($"facility[{i}] {keysA[i]}!={keysB[i]}; ");
                    return;
                }
            }
        }

        private static List<string> FacilityKeys(List<PlacedObjectSaveData>? placed)
        {
            List<string> keys = new List<string>();
            if (placed == null)
                return keys;
            for (int i = 0; i < placed.Count; i++)
            {
                PlacedObjectSaveData p = placed[i];
                // FacilityTier 0(구 세이브/일반 오브젝트)은 1로 정규화 — PlacedObjectSaveData 계약과 일치.
                int tier = p.FacilityTier > 0 ? p.FacilityTier : 1;
                keys.Add($"{p.TableId}:{tier}");
            }
            return keys;
        }

        /// <summary>상점 스냅샷 비교 — 결정론 roll 순서대로 품목·수량·가격·회차·사유·슬롯.</summary>
        private static void CompareMerchantStock(StringBuilder builder,
            List<MerchantStockEntry>? a, List<MerchantStockEntry>? b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                builder.Append($"merchant stock count {countA}!={countB}; ");
                return;
            }
            for (int i = 0; i < countA; i++)
            {
                MerchantStockEntry ea = a![i];
                MerchantStockEntry eb = b![i];
                int idA = ea.Item != null ? ea.Item.Id : -1;
                int idB = eb.Item != null ? eb.Item.Id : -1;
                if (idA != idB)
                    builder.Append($"stock[{i}].Item {idA}!={idB}; ");
                if (ea.RemainingCount != eb.RemainingCount)
                    builder.Append($"stock[{i}].Count {ea.RemainingCount}"
                        + $"!={eb.RemainingCount}; ");
                if (ea.UnitPrice != eb.UnitPrice)
                    builder.Append($"stock[{i}].UnitPrice {ea.UnitPrice}"
                        + $"!={eb.UnitPrice}; ");
                if (ea.RestockCycle != eb.RestockCycle)
                    builder.Append($"stock[{i}].RestockCycle {ea.RestockCycle}"
                        + $"!={eb.RestockCycle}; ");
                if (ea.TradeProfileId != eb.TradeProfileId)
                    builder.Append($"stock[{i}].TradeProfileId {ea.TradeProfileId}"
                        + $"!={eb.TradeProfileId}; ");
                if (ea.SlotKind != eb.SlotKind)
                    builder.Append($"stock[{i}].SlotKind {ea.SlotKind}!={eb.SlotKind}; ");
                if (ea.PriceReason != eb.PriceReason)
                    builder.Append($"stock[{i}].PriceReason {ea.PriceReason}"
                        + $"!={eb.PriceReason}; ");
                if (ea.NpcBuyerEntityId != eb.NpcBuyerEntityId)
                    builder.Append($"stock[{i}].NpcBuyer {ea.NpcBuyerEntityId}"
                        + $"!={eb.NpcBuyerEntityId}; ");
            }
        }

        private static int ApplyNpcGoldTransfersUntil(VillageData village,
            IReadOnlyList<VillageNpcGoldTransfer> transfers, int startIndex, float untilGameTime,
            ref int rejected)
        {
            int index = startIndex;
            while (index < transfers.Count && transfers[index].GameTime <= untilGameTime)
            {
                ApplyNpcGoldTransfer(village, transfers[index], ref rejected);
                index++;
            }
            return index;
        }

        private static void ApplyNpcGoldTransfer(VillageData village, VillageNpcGoldTransfer transfer,
            ref int rejected)
        {
            if (transfer.Amount >= 0)
            {
                VillageResourceSimulation.DepositNpcGold(village, transfer.Amount);
                return;
            }
            if (VillageResourceSimulation.TryWithdrawNpcGold(village, -transfer.Amount) == false)
                rejected++;
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
