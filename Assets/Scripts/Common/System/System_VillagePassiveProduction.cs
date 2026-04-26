#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 인구 기반 자원 패시브 생산/소비 시스템.
    /// 게임시간 1h 단위로 정수 자원을 누적 (소수 버퍼 없음).
    /// 소비는 Food만 (NPC당 1/h), 생산은 Food +2/h, Wood +1/h, Stone 5h당 +1.
    /// Food 0 유지 24h 경계 시 경고 1회.
    /// </summary>
    public class System_VillagePassiveProduction : IFixedUpdateSystem
    {
        // 생산 (NPC 1명당 게임시간 1h)
        private const int FOOD_PRODUCE_PER_HOUR = 2;
        private const int WOOD_PRODUCE_PER_HOUR = 1;
        private const int STONE_PRODUCE_EVERY_N_HOURS = 5;  // 5h마다 NPC당 +1

        // 소비
        private const int FOOD_CONSUME_PER_HOUR = 1;

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

                int pop = v.Population;

                int foodBefore = storage.FoodAmount;
                int woodBefore = storage.WoodAmount;
                int stoneBefore = storage.StoneAmount;

                // 정수 곱셈으로 deltaHours 만큼 누적
                int foodDelta = (FOOD_PRODUCE_PER_HOUR - FOOD_CONSUME_PER_HOUR) * pop * deltaHours;
                int woodDelta = WOOD_PRODUCE_PER_HOUR * pop * deltaHours;

                storage.FoodAmount = ApplyCap(storage.FoodAmount + foodDelta, storage.FoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Food);
                storage.WoodAmount = ApplyCap(storage.WoodAmount + woodDelta, storage.WoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Wood);

                // Stone: 5h 누적 카운터 방식
                storage.StoneTimer += deltaHours;
                if (storage.StoneTimer >= STONE_PRODUCE_EVERY_N_HOURS)
                {
                    int cycles = storage.StoneTimer / STONE_PRODUCE_EVERY_N_HOURS;
                    int stoneDelta = pop * cycles;
                    storage.StoneAmount = ApplyCap(storage.StoneAmount + stoneDelta, storage.StoneCap, ref storage.SurplusFlags, VillageSurplusFlags.Stone);
                    storage.StoneTimer -= cycles * STONE_PRODUCE_EVERY_N_HOURS;
                }

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
            v.StoneTimer = s.StoneTimer;
        }

        public void OnReset()
        {
            _lastProcessedHour = 0;
        }
    }
}
