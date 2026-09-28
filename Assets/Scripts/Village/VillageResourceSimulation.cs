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
        private const float GOLD_PER_HOUR = 0.05f;
        private const int FOOD_CONSUME_PER_HOUR = 1;
        private const float PROD_POP_MAX = 2f;

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

        public static void AdvanceWholeHours(VillageData village, int elapsedHours)
        {
            if (elapsedHours <= 0 || village.Population <= 0)
                return;

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
            int goldDelta = TakeWhole(ref village.GoldCarry,
                GOLD_PER_HOUR * productionPopulation * elapsedHours);
            AccumulateUncapped(village, GlobalEnum.ItemType.Iron, ironDelta);
            AccumulateUncapped(village, GlobalEnum.ItemType.Gold, goldDelta);

            if (GetAmount(village, GlobalEnum.ItemType.Food) <= 0)
                village.HungerHoursAccumulated += elapsedHours;
            else
                village.HungerHoursAccumulated = 0;
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
