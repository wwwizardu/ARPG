#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// VillageData를 원장으로 사용하는 마을 자원 생산·저장 규칙.
    /// 실시간 시스템과 추상 시뮬레이션이 같은 계산을 호출한다.
    /// </summary>
    public static class VillageResourceSimulation
    {
        public const int DefaultResourceCap = 50;

        private const int FOOD_PRODUCE_PER_HOUR = 2;
        private const int WOOD_PRODUCE_PER_HOUR = 1;
        private const float STONE_PER_HOUR = 0.2f;
        private const float IRON_PER_HOUR = 0.1f;
        // GOLD_BALANCE_DESIGN D134: 0.05 → 0.13 (×2.6).
        private const float GOLD_PER_HOUR = 0.13f;
        private const int FOOD_CONSUME_PER_HOUR = 1;
        private const float PROD_POP_MAX = 2f;

        // GOLD_BALANCE_DESIGN D132 B-보조: 금고 Gold가 기준선(300G × 서비스 물가 배율, D128)을 넘으면 넘은 만큼을 하루 5%씩 지운다.
        public const int VaultUpkeepBaseGold = 300;
        public const float VaultUpkeepDailyRate = 0.05f;
        // 시간당 비율 h = 1 − 0.95^(1/24) — 24시간 연속 적용하면 초과분의 5%가 사라진다.
        private static readonly float VaultUpkeepHourlyRate =
            1f - Mathf.Pow(1f - VaultUpkeepDailyRate, 1f / 24f);

        public static int GetAmount(VillageData village, GlobalEnum.ItemType type)
        {
            return village.Resources.TryGetValue(type, out int amount) ? amount : 0;
        }

        public static int GetCap(VillageData village, GlobalEnum.ItemType type)
        {
            if (village.ResourceCaps.TryGetValue(type, out int cap))
                return cap;
            return DefaultResourceCap;
        }

        public static int Produce(VillageData village, GlobalEnum.ItemType type, int amount)
        {
            if (amount <= 0)
                return 0;

            int before = GetAmount(village, type);
            int after = Mathf.Min(before + amount, GetCap(village, type));
            village.Resources[type] = after;
            return after - before;
        }

        public static bool Consume(VillageData village, GlobalEnum.ItemType type, int amount)
        {
            if (amount <= 0)
                return true;

            int current = GetAmount(village, type);
            if (current < amount)
                return false;

            village.Resources[type] = current - amount;
            return true;
        }

        /// <summary>
        /// NPC 거래·치료 전용 Gold 입금 (NPC_AMBITION_DESIGN D34, D41). 인구 생산 Gold와 같은 규칙으로
        /// 저장 상한 없이 누적한다. 닫힌 경제라 호출자는 같은 금액을 NPC Gold에서 먼저 뺀 상태여야 한다.
        /// </summary>
        public static void DepositNpcGold(VillageData village, int amount)
        {
            AccumulateUncapped(village, GlobalEnum.ItemType.Gold, amount);
        }

        /// <summary>
        /// NPC 거래 전용 Gold 출금 (D34, D41). 잔액이 부족하면 아무것도 빼지 않고 false — 상점은 매입을 거절한다.
        /// </summary>
        public static bool TryWithdrawNpcGold(VillageData village, int amount)
        {
            return Consume(village, GlobalEnum.ItemType.Gold, amount);
        }

        public static int AddCap(VillageData village, GlobalEnum.ItemType type, int amount)
        {
            int next = GetCap(village, type) + amount;
            village.ResourceCaps[type] = next;
            return next;
        }

        /// <summary>
        /// 정수 시간만큼 자원 생산과 금고 유지비(D132 B-보조)를 진행한다. 실시간·추상 경로가 같은 함수를 부른다.
        /// Gold는 시간마다 "생산 → 유지비" 순서로 돌려, 여러 시간을 한 번에 진행해도 1시간씩 진행한 결과와 같다
        /// (VillageSimulationParity). 인구 0인 마을은 생산 없이 유지비만 적용한다 (D135).
        /// </summary>
        /// <returns>금고 유지비로 지운 Gold. 진단은 금고 변화량 + 이 값을 생산으로, 이 값을 소멸로 따로 센다.</returns>
        public static int AdvanceWholeHours(VillageData village, int elapsedHours)
        {
            if (elapsedHours <= 0)
                return 0;
            int upkeepBaseline = GetVaultUpkeepBaseline();
            if (village.Population <= 0)
                return ApplyVaultUpkeepHours(village, elapsedHours, upkeepBaseline);

            float productionPopulation = GetProductionPopulation(village.Population);
            int foodDelta = TakeWhole(ref village.FoodCarry,
                (FOOD_PRODUCE_PER_HOUR - FOOD_CONSUME_PER_HOUR) * productionPopulation * elapsedHours);
            int woodDelta = TakeWhole(ref village.WoodCarry,
                WOOD_PRODUCE_PER_HOUR * productionPopulation * elapsedHours);
            int stoneDelta = TakeWhole(ref village.StoneCarry,
                STONE_PER_HOUR * productionPopulation * elapsedHours);

            Produce(village, GlobalEnum.ItemType.Food, foodDelta);
            Produce(village, GlobalEnum.ItemType.Wood, woodDelta);
            Produce(village, GlobalEnum.ItemType.Stone, stoneDelta);

            // 기존 실시간 규칙과 동일하게 Iron/Gold는 별도 저장 상한 없이 누적한다.
            int ironDelta = TakeWhole(ref village.IronCarry,
                IRON_PER_HOUR * productionPopulation * elapsedHours);
            AccumulateUncapped(village, GlobalEnum.ItemType.Iron, ironDelta);

            // 유지비가 그 시간의 금고 잔액에 따라 달라지므로 Gold만 시간 단위로 생산 → 유지비를 반복한다.
            float goldPerHour = GOLD_PER_HOUR * productionPopulation;
            int upkeepBurned = 0;
            for (int hour = 0; hour < elapsedHours; hour++)
            {
                AccumulateUncapped(village, GlobalEnum.ItemType.Gold,
                    TakeWhole(ref village.GoldCarry, goldPerHour));
                upkeepBurned += ApplyVaultUpkeepHour(village, upkeepBaseline);
            }

            if (GetAmount(village, GlobalEnum.ItemType.Food) <= 0)
                village.HungerHoursAccumulated += elapsedHours;
            else
                village.HungerHoursAccumulated = 0;
            return upkeepBurned;
        }

        /// <summary>
        /// 생산 없이 금고 유지비만 정수 시간만큼 적용한다 (D135 — 운영하지 않는 사라진 터의 추상 경로).
        /// </summary>
        /// <returns>지운 Gold.</returns>
        public static int AdvanceVaultUpkeepHours(VillageData village, int elapsedHours)
        {
            if (elapsedHours <= 0)
                return 0;
            return ApplyVaultUpkeepHours(village, elapsedHours, GetVaultUpkeepBaseline());
        }

        /// <summary>
        /// 현재 carry와 시간당 생산량으로 requiredAmount에 도달하는 정수 생산 tick 수를 계산한다.
        /// 0=이미 충족, -1=인구/Cap 때문에 도달 불가.
        /// </summary>
        public static int GetHoursUntilAmount(VillageData village, GlobalEnum.ItemType type,
            int requiredAmount)
        {
            int current = GetAmount(village, type);
            if (current >= requiredAmount)
                return 0;
            if (village.Population <= 0 || GetCap(village, type) < requiredAmount)
                return -1;

            float productionPopulation = GetProductionPopulation(village.Population);
            float rate;
            float carry;
            if (type == GlobalEnum.ItemType.Wood)
            {
                rate = WOOD_PRODUCE_PER_HOUR * productionPopulation;
                carry = village.WoodCarry;
            }
            else if (type == GlobalEnum.ItemType.Stone)
            {
                rate = STONE_PER_HOUR * productionPopulation;
                carry = village.StoneCarry;
            }
            else if (type == GlobalEnum.ItemType.Food)
            {
                rate = (FOOD_PRODUCE_PER_HOUR - FOOD_CONSUME_PER_HOUR)
                    * productionPopulation;
                carry = village.FoodCarry;
            }
            else
            {
                return -1;
            }

            if (rate <= 0f)
                return -1;

            float needed = requiredAmount - current - carry;
            int hours = Mathf.CeilToInt(needed / rate - 0.000001f);
            return Mathf.Max(1, hours);
        }

        private static float GetProductionPopulation(int population)
        {
            int cap = VillageManager.ABSTRACT_POP_CAP;
            if (population <= 1 || cap <= 1)
                return 1f;
            if (population >= cap)
                return PROD_POP_MAX;
            return 1f + (population - 1) * (PROD_POP_MAX - 1f) / (cap - 1);
        }

        /// <summary>
        /// 금고 유지비 기준선 = 300G × 서비스 물가 배율 (D128·D132). 한 번의 진행 동안 고정한다.
        /// 플레이 중이 아니면(편집기 진단) 싱글톤을 만들지 않고 배율 1로 본다.
        /// </summary>
        private static int GetVaultUpkeepBaseline()
        {
            if (Application.isPlaying == false || AR.s == null || AR.s.Village == null)
                return VaultUpkeepBaseGold;
            return AR.s.Village.GetServicePrice(VaultUpkeepBaseGold);
        }

        private static int ApplyVaultUpkeepHours(VillageData village, int elapsedHours, int baseline)
        {
            int burned = 0;
            for (int hour = 0; hour < elapsedHours; hour++)
            {
                // 생산이 없으니 한 번 기준선 이하가 되면 남은 시간도 지울 것이 없다 (carry도 바뀌지 않는다).
                if (GetAmount(village, GlobalEnum.ItemType.Gold) <= baseline)
                    break;
                burned += ApplyVaultUpkeepHour(village, baseline);
            }
            return burned;
        }

        /// <summary>1시간분 유지비: carry += 초과분 × h, 정수만큼 금고에서 지운다. 기준선 이하면 carry도 그대로 둔다.</summary>
        private static int ApplyVaultUpkeepHour(VillageData village, int baseline)
        {
            int gold = GetAmount(village, GlobalEnum.ItemType.Gold);
            int excess = gold - baseline;
            if (excess <= 0)
                return 0;
            int burn = TakeWhole(ref village.VaultUpkeepCarry, excess * VaultUpkeepHourlyRate);
            if (burn <= 0)
                return 0;
            if (burn > excess)
                burn = excess;
            village.Resources[GlobalEnum.ItemType.Gold] = gold - burn;
            return burn;
        }

        private static int TakeWhole(ref float carry, float amount)
        {
            carry += amount;
            int whole = Mathf.FloorToInt(carry);
            carry -= whole;
            return whole;
        }

        private static void AccumulateUncapped(VillageData village, GlobalEnum.ItemType type, int amount)
        {
            if (amount <= 0)
                return;
            village.Resources[type] = GetAmount(village, type) + amount;
        }
    }
}
