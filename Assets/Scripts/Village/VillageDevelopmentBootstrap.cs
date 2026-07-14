#nullable enable
using ARPG.History;
using UnityEngine;

namespace ARPG.Village
{
    public sealed class VillageBootstrapPlan
    {
        public int SettlementId;
        public SettlementFate Fate;
        public bool IsEligible;
        public int EconomicPopulation;
        public float PopulationRank;
        public float BiomeCapacityRatio;
        public float Maturity;
        public float BootstrapHours;
        public int MaxGeneralBuildings;
        public VillageStage MaxStage;
        public float WallSeedFactor;
    }

    public sealed class VillageBootstrapAdvanceResult
    {
        public bool IsEligible;
        public bool IsComplete;
        public VillageAbstractAdvanceResult? AdvanceResult;
    }

    /// <summary>
    /// New Game 마을 초기 상태 확정.
    /// V2 (VILLAGE_REWORK_PLAN §3): synthetic 시간 진행(CreatePlans/AdvanceBootstrapSlice) 대신
    /// <see cref="ApplyFixedInitialState"/>가 고정 역사 인구로 계산한 초기 Stage를 직접 적용한다.
    /// 기존 plan/slice API는 V0 진단·parity 기준선 재현을 위해 보존하며 런타임에서는 호출하지 않는다.
    /// </summary>
    public static class VillageDevelopmentBootstrap
    {
        public static bool IsOperationalFate(SettlementFate fate)
        {
            return fate == SettlementFate.Alive || fate == SettlementFate.Conquered;
        }

        /// <summary>
        /// V2: 시간을 흘리지 않고 초기 Stage를 직접 적용한다. 진행 task·건설·벽을 만들지 않으며
        /// construction clock을 현재 런타임 시각에 정렬해 로드/실체화 시 과거 catch-up이 없게 한다.
        /// boundsRadius가 0 이하이면 Bounds는 건드리지 않고 실체화 시 Stage 기준으로 산출된다.
        /// </summary>
        public static void ApplyFixedInitialState(VillageData village,
            VillageStage initialStage, int boundsRadius, float runtimeGameTime)
        {
            village.Stage = initialStage;
            if (boundsRadius > 0)
            {
                village.BoundsX = Mathf.FloorToInt(village.PositionX) - boundsRadius;
                village.BoundsY = Mathf.FloorToInt(village.PositionY) - boundsRadius;
                village.BoundsW = boundsRadius * 2;
                village.BoundsH = boundsRadius * 2;
            }
            village.LastConstructionSimGameTime = runtimeGameTime;
            village.LastConstructionTierCheckGameTime = runtimeGameTime;
            village.InitialDevelopmentApplied = true;
        }

        public static VillageBootstrapPlan[] CreatePlans(SettlementLiveState[] settlements,
            int mapSeed)
        {
            VillageBootstrapPlan[] plans = new VillageBootstrapPlan[settlements.Length];
            int eligibleCount = 0;
            for (int i = 0; i < settlements.Length; i++)
            {
                if (IsOperationalFate(settlements[i].Fate))
                    eligibleCount++;
            }

            for (int i = 0; i < settlements.Length; i++)
            {
                SettlementLiveState settlement = settlements[i];
                bool eligible = IsOperationalFate(settlement.Fate);
                float rank = eligible
                    ? CalculatePopulationRank(settlements, settlement.Pop, eligibleCount)
                    : 0f;
                float capacityRatio = Mathf.Clamp01(settlement.Pop
                    / Mathf.Max(1f, BiomeData.Carry(settlement.Biome)));
                float maturity = Mathf.Clamp01(rank * 0.7f + capacityRatio * 0.3f);
                int population = VillageManager.QuantizeAbstractPopulation(
                    Mathf.RoundToInt(settlement.Pop));

                GetDevelopmentBand(population, out VillageStage maxStage,
                    out float minHours, out float maxHours, out int buildingLimit);

                ulong settlementChannel = DerivedSeed.ChannelVillageBootstrap
                    ^ unchecked((ulong)(uint)settlement.SettlementId * 0x9E3779B97F4A7C15UL);
                HistoryRng rng = new HistoryRng(DerivedSeed.Derive(mapSeed, settlementChannel));

                plans[i] = new VillageBootstrapPlan
                {
                    SettlementId = settlement.SettlementId,
                    Fate = settlement.Fate,
                    IsEligible = eligible,
                    EconomicPopulation = population,
                    PopulationRank = rank,
                    BiomeCapacityRatio = capacityRatio,
                    Maturity = maturity,
                    BootstrapHours = Mathf.Lerp(minHours, maxHours, maturity),
                    MaxGeneralBuildings = buildingLimit,
                    MaxStage = maxStage,
                    WallSeedFactor = (float)rng.NextDouble(),
                };
            }

            return plans;
        }

        public static bool ValidatePlanReproducibility(SettlementLiveState[] settlements,
            int mapSeed, out string differences)
        {
            VillageBootstrapPlan[] first = CreatePlans(settlements, mapSeed);
            VillageBootstrapPlan[] second = CreatePlans(settlements, mapSeed);
            for (int i = 0; i < first.Length; i++)
            {
                bool expectedEligible = IsOperationalFate(first[i].Fate);
                if (first[i].IsEligible != expectedEligible)
                {
                    differences = $"plan[{i}] Fate eligibility mismatch";
                    return false;
                }
                if (first[i].SettlementId != second[i].SettlementId
                    || first[i].IsEligible != second[i].IsEligible
                    || first[i].EconomicPopulation != second[i].EconomicPopulation
                    || first[i].MaxGeneralBuildings != second[i].MaxGeneralBuildings
                    || first[i].MaxStage != second[i].MaxStage
                    || Mathf.Abs(first[i].BootstrapHours
                        - second[i].BootstrapHours) > 0.0001f
                    || Mathf.Abs(first[i].WallSeedFactor
                        - second[i].WallSeedFactor) > 0.0001f)
                {
                    differences = $"plan[{i}] is not reproducible";
                    return false;
                }
            }

            differences = string.Empty;
            return true;
        }

        public static VillageBootstrapAdvanceResult AdvanceBootstrapSlice(VillageData village,
            VillageBootstrapPlan plan, int generalEventBudget, int wallEventBudget)
        {
            VillageBootstrapAdvanceResult result = new VillageBootstrapAdvanceResult
            {
                IsEligible = plan.IsEligible,
            };
            if (plan.IsEligible == false)
                return result;

            village.Population = plan.EconomicPopulation;
            VillageAbstractAdvanceResult advance =
                VillageAbstractConstruction.AdvanceAbstractConstruction(village,
                    plan.BootstrapHours, new VillageAbstractAdvanceOptions
                    {
                        GeneralEventBudget = generalEventBudget,
                        WallEventBudget = wallEventBudget,
                        MaxGeneralBuildings = plan.MaxGeneralBuildings,
                        MaxStage = plan.MaxStage,
                        MaxWallCompletionRatio = 1f,
                        UseBootstrapWallLimit = true,
                        BootstrapMaturity = plan.Maturity,
                        BootstrapWallSeedFactor = plan.WallSeedFactor,
                        IsOperational = true,
                    });

            result.AdvanceResult = advance;
            result.IsComplete = advance.IsCaughtUp;
            return result;
        }

        /// <summary>
        /// 모든 bootstrap slice가 끝난 뒤 New Game 활성화 호출자가 실행한다.
        /// synthetic bootstrap clock을 실제 런타임 clock으로 넘기고 1회 플래그를 확정한다.
        /// </summary>
        public static bool FinalizeBootstrap(VillageData village, VillageBootstrapPlan plan,
            float runtimeGameTime)
        {
            if (plan.IsEligible == false
                || village.LastConstructionSimGameTime + 0.0001f < plan.BootstrapHours)
                return false;

            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[i];
                task.StartedAt = runtimeGameTime - task.AccumulatedHours;
            }
            village.LastConstructionSimGameTime = runtimeGameTime;
            village.LastConstructionTierCheckGameTime = runtimeGameTime;
            village.InitialDevelopmentApplied = true;
            return true;
        }

        private static float CalculatePopulationRank(SettlementLiveState[] settlements,
            float population, int eligibleCount)
        {
            if (eligibleCount <= 1)
                return 0.5f;

            int lower = 0;
            int equal = 0;
            for (int i = 0; i < settlements.Length; i++)
            {
                if (IsOperationalFate(settlements[i].Fate) == false)
                    continue;
                if (settlements[i].Pop < population)
                    lower++;
                else if (Mathf.Abs(settlements[i].Pop - population) < 0.0001f)
                    equal++;
            }

            float averageIndex = lower + (equal - 1) * 0.5f;
            return Mathf.Clamp01(averageIndex / (eligibleCount - 1));
        }

        private static void GetDevelopmentBand(int population, out VillageStage maxStage,
            out float minHours, out float maxHours, out int buildingLimit)
        {
            if (population <= 2)
            {
                maxStage = VillageStage.Settlement;
                minHours = 12f;
                maxHours = 36f;
                buildingLimit = 3;
            }
            else if (population <= 7)
            {
                maxStage = VillageStage.Hamlet;
                minHours = 48f;
                maxHours = 96f;
                buildingLimit = 8;
            }
            else if (population <= 14)
            {
                maxStage = VillageStage.Village;
                minHours = 120f;
                maxHours = 216f;
                buildingLimit = 16;
            }
            else
            {
                maxStage = VillageStage.Town;
                minHours = 240f;
                maxHours = 408f;
                buildingLimit = 28;
            }
        }
    }
}
