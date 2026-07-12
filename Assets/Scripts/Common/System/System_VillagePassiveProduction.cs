#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 인구 기반 자원 패시브 생산/소비 시스템.
    /// 게임시간 1h 단위로 생산하되, 소수부는 VillageStorageComponent의 *Carry 버퍼에 이월(정수만 저장).
    /// 인구는 v.Population(§0 추상 인구 정량화 값) — 물리 NPC 수와 무관 (NPC_SIMPLIFICATION_PLAN D2/D3).
    ///
    /// ★ 생산 스케일: v.Population(1~25)을 그대로 곱하면 상한 마을이 저점 대비 25배를 찍어 폭주하므로,
    ///   생산에 쓰는 인구를 prodPop(1.0~PROD_POP_MAX)으로 압축한다 → 저점 대비 최대 2배로 완만하게 증가.
    ///   (경제 인구 1 → Food 순증 24/일, 경제 인구 25 → 48/일)
    /// 저점(경제 인구 1) 시간당: Food 순증 +1, Wood +1, Stone 0.2, Iron 0.1, Gold 0.05.
    /// Food 0 유지 24h 경계 시 경고 1회.
    /// </summary>
    public class System_VillagePassiveProduction : IFixedUpdateSystem
    {
        // 생산 (prodPop=1.0 = 경제 인구 1 기준, 게임시간 1h당)
        private const int FOOD_PRODUCE_PER_HOUR = 2;
        private const int WOOD_PRODUCE_PER_HOUR = 1;
        private const float STONE_PER_HOUR = 0.2f;   // 구 5h당 +1
        // D3: Iron/Gold도 인구 기반 편입 (구 직업 보너스 경로 대체). 캘리브레이션 초기값 — 밸런스 튜닝 대상.
        private const float IRON_PER_HOUR = 0.1f;    // 구 10h당 +1
        private const float GOLD_PER_HOUR = 0.05f;   // 구 20h당 +1

        // 소비
        private const int FOOD_CONSUME_PER_HOUR = 1;

        // 생산용 인구 압축: 경제 인구 1~ABSTRACT_POP_CAP → prodPop 1.0~PROD_POP_MAX (선형).
        // 상한 마을 생산이 저점의 PROD_POP_MAX배를 넘지 않도록 억제 (밸런스 튜닝 대상).
        private const float PROD_POP_MAX = 2.0f;

        // 안전장치: 배속·복귀 시 한 번에 반영할 최대 시간
        private const int MAX_DELTA_HOURS_PER_TICK = 1;

        // 기아 경고 임계 (게임시간)
        private const int HUNGER_WARN_THRESHOLD = 24;

        // 도메인 대역 (CLAUDE.md): 50-54 Resource (Phase C에서 57→52 재할당)
        public int Priority => 52;
        public float UpdateInterval => 5.0f;

        private int _lastProcessedHour;

        public void OnCreate()
        {
            _lastProcessedHour = Mathf.FloorToInt(AR.s.Time.CurrentGameTime);
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            int currentHour = Mathf.FloorToInt(AR.s.Time.CurrentGameTime);
            int deltaHours = currentHour - _lastProcessedHour;
            if (deltaHours <= 0) return;
            if (deltaHours > MAX_DELTA_HOURS_PER_TICK)
                deltaHours = MAX_DELTA_HOURS_PER_TICK;

            var villages = AR.s.Village.GetAllVillages();
            foreach (VillageData v in villages)
            {
                if (v.EntityId < 0) continue;
                if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var storage) == false)
                    continue;
                if (v.Population <= 0) continue;

                // 생산에 쓰는 인구는 경제 인구를 prodPop(1.0~PROD_POP_MAX)으로 압축한 값.
                float prodPop = ProdPop(v.Population);

                int foodBefore = storage.FoodAmount;
                int woodBefore = storage.WoodAmount;
                int stoneBefore = storage.StoneAmount;

                // prodPop × 시간당 rate × deltaHours → 소수부는 *Carry에 이월, 정수부만 Amount에 반영.
                int foodDelta = TakeWhole(ref storage.FoodCarry, (FOOD_PRODUCE_PER_HOUR - FOOD_CONSUME_PER_HOUR) * prodPop * deltaHours);
                int woodDelta = TakeWhole(ref storage.WoodCarry, WOOD_PRODUCE_PER_HOUR * prodPop * deltaHours);
                int stoneDelta = TakeWhole(ref storage.StoneCarry, STONE_PER_HOUR * prodPop * deltaHours);

                storage.FoodAmount = ApplyCap(storage.FoodAmount + foodDelta, storage.FoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Food);
                storage.WoodAmount = ApplyCap(storage.WoodAmount + woodDelta, storage.WoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Wood);
                storage.StoneAmount = ApplyCap(storage.StoneAmount + stoneDelta, storage.StoneCap, ref storage.SurplusFlags, VillageSurplusFlags.Stone);

                // Iron/Gold: VillageData.Resources에 직접 기록 (Storage 캐시는 Food/Wood/Stone만) — Cap 없음(기존 동작 유지).
                int ironDelta = TakeWhole(ref storage.IronCarry, IRON_PER_HOUR * prodPop * deltaHours);
                if (ironDelta > 0) AccumulateResource(v, GlobalEnum.ItemType.Iron, ironDelta);
                int goldDelta = TakeWhole(ref storage.GoldCarry, GOLD_PER_HOUR * prodPop * deltaHours);
                if (goldDelta > 0) AccumulateResource(v, GlobalEnum.ItemType.Gold, goldDelta);

                bool changed = storage.FoodAmount != foodBefore
                    || storage.WoodAmount != woodBefore
                    || storage.StoneAmount != stoneBefore;
                if (changed)
                {
                    AR.s.UI.SetNotify($"마을 {v.VillageId} [{v.Stage}]: Food {storage.FoodAmount}/{storage.FoodCap}  Wood {storage.WoodAmount}/{storage.WoodCap}  Stone {storage.StoneAmount}/{storage.StoneCap}");
                }

                // 기아 경고 (Food 0 유지 24h 경계 크로싱 1회)
                if (storage.FoodAmount <= 0)
                {
                    storage.FoodAmount = 0;
                    int before = storage.HungerHoursAccumulated;
                    storage.HungerHoursAccumulated += deltaHours;
                    if (before < HUNGER_WARN_THRESHOLD && storage.HungerHoursAccumulated >= HUNGER_WARN_THRESHOLD)
                        Debug.LogWarning($"[HungerTick] Village {v.VillageId} exceeded {HUNGER_WARN_THRESHOLD}h without food");
                }
                else
                {
                    storage.HungerHoursAccumulated = 0;
                }

                AR.s.Component.SetComponent(v.EntityId, storage);

                // VillageData 정본 동기화 (세이브/구 API 호환)
                WriteBack(v, storage);
            }

            _lastProcessedHour = currentHour;
        }

        /// <summary>
        /// 경제 인구(1~ABSTRACT_POP_CAP)를 생산용 인구 prodPop(1.0~PROD_POP_MAX)으로 선형 압축.
        /// pop 1 → 1.0, pop 상한 → PROD_POP_MAX. 상한 마을이 저점의 PROD_POP_MAX배를 넘지 않게 억제.
        /// </summary>
        private static float ProdPop(int pop)
        {
            int cap = VillageManager.ABSTRACT_POP_CAP;
            if (pop <= 1 || cap <= 1) return 1f;
            if (pop >= cap) return PROD_POP_MAX;
            return 1f + (pop - 1) * (PROD_POP_MAX - 1f) / (cap - 1);
        }

        /// <summary>
        /// carry에 amount(소수 가능)를 더하고 정수부를 꺼내 반환한다. 소수부는 carry에 남아 다음 tick으로 이월.
        /// </summary>
        private static int TakeWhole(ref float carry, float amount)
        {
            carry += amount;
            int whole = Mathf.FloorToInt(carry);
            carry -= whole;
            return whole;
        }

        private static int ApplyCap(int value, int cap, ref byte flags, byte bit)
        {
            if (value >= cap)
            {
                flags |= bit;
                return cap;
            }
            flags = (byte)(flags & ~bit);
            return value < 0 ? 0 : value;
        }

        private static void WriteBack(VillageData v, VillageStorageComponent s)
        {
            v.Resources[GlobalEnum.ItemType.Food] = s.FoodAmount;
            v.Resources[GlobalEnum.ItemType.Wood] = s.WoodAmount;
            v.Resources[GlobalEnum.ItemType.Stone] = s.StoneAmount;
            v.HungerHoursAccumulated = s.HungerHoursAccumulated;
        }

        /// <summary>
        /// VillageData.Resources에 직접 가산 (Storage 캐시는 Food/Wood/Stone 한정이라 Iron/Gold는 직접).
        /// </summary>
        private static void AccumulateResource(VillageData v, GlobalEnum.ItemType type, int amount)
        {
            int current = v.Resources.TryGetValue(type, out int c) ? c : 0;
            v.Resources[type] = current + amount;
        }

        public void OnReset()
        {
            _lastProcessedHour = 0;
        }
    }
}
