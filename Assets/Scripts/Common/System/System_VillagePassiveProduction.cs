#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 실체화된 마을의 시간당 자원 생산을 진행한다.
    /// 계산과 carry 원장은 VillageResourceSimulation/VillageData에 있고,
    /// VillageStorageComponent는 실시간 표시용 미러로만 유지한다.
    /// </summary>
    public class System_VillagePassiveProduction : IFixedUpdateSystem, IWorldMapSystem
    {
        private const int MAX_DELTA_HOURS_PER_TICK = 1;
        private const int HUNGER_WARN_THRESHOLD = 24;

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
            if (deltaHours <= 0)
                return;
            if (deltaHours > MAX_DELTA_HOURS_PER_TICK)
                deltaHours = MAX_DELTA_HOURS_PER_TICK;

            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData village = villages[i];
                if (village.EntityId < 0 || village.Population <= 0)
                    continue;
                if (AR.s.Component.TryGetComponent<VillageStorageComponent>(village.EntityId, out _) == false)
                    continue;

                int foodBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Food);
                int woodBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Wood);
                int stoneBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Stone);
                int hungerBefore = village.HungerHoursAccumulated;

                VillageResourceSimulation.AdvanceWholeHours(village, deltaHours);
                AR.s.Village.SyncStorageFromData(village);

                int food = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Food);
                int wood = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Wood);
                int stone = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Stone);
                if (food != foodBefore || wood != woodBefore || stone != stoneBefore)
                {
                    int foodCap = VillageResourceSimulation.GetCap(village, GlobalEnum.ItemType.Food);
                    int woodCap = VillageResourceSimulation.GetCap(village, GlobalEnum.ItemType.Wood);
                    int stoneCap = VillageResourceSimulation.GetCap(village, GlobalEnum.ItemType.Stone);
                    AR.s.UI.SetNotify($"마을 {village.VillageId} [{village.Stage}]: Food {food}/{foodCap}  Wood {wood}/{woodCap}  Stone {stone}/{stoneCap}");
                }

                if (hungerBefore < HUNGER_WARN_THRESHOLD
                    && village.HungerHoursAccumulated >= HUNGER_WARN_THRESHOLD)
                    Debug.LogWarning($"[HungerTick] Village {village.VillageId} exceeded {HUNGER_WARN_THRESHOLD}h without food");
            }

            // 긴 프레임/배속 복귀의 backlog를 버리지 않고 다음 tick에서 한 시간씩 이어서 처리한다.
            _lastProcessedHour += deltaHours;
        }

        public void OnReset()
        {
            _lastProcessedHour = 0;
        }
    }
}
