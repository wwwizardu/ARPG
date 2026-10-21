#nullable enable
using System.Collections.Generic;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 실체화된 마을의 시간당 자원 생산을 진행한다.
    /// 계산과 carry 원장은 VillageResourceSimulation/VillageData에 있고,
    /// VillageStorageComponent는 실시간 표시용 미러로만 유지한다.
    /// 던전 안에서도 돈다 (IWorldSimSystem): 접히지 않는 손제작 마을은 ECS가 보관함에 있어도 원장만 진행하고,
    /// 마을 자원 알림은 띄우지 않는다 (DUNGEON_WORLD_SIM_DESIGN §3.3·§3.7).
    /// </summary>
    public class System_VillagePassiveProduction : IFixedUpdateSystem, IWorldSimSystem
    {
        private const int MAX_DELTA_HOURS_PER_TICK = 1;
        private const int HUNGER_WARN_THRESHOLD = 24;

        public int Priority => 52;
        public float UpdateInterval => 5.0f;

        // 마지막으로 생산을 처리한 게임시간(정수 시). -1이면 아직 미초기화.
        private int _lastProcessedHour = -1;

        public void OnCreate()
        {
            // 시스템은 저장된 게임 시각을 불러오기 전에 만들어진다. 여기서 시각을 읽으면 0이 되어 로드할 때마다
            // 게임 시작부터의 시간을 다시 생산하므로, 첫 tick에서 불러온 시각으로 잡는다.
            _lastProcessedHour = -1;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            int currentHour = Mathf.FloorToInt(AR.s.Time.CurrentGameTime);
            if (_lastProcessedHour < 0)
            {
                _lastProcessedHour = currentHour;
                return;
            }
            int deltaHours = currentHour - _lastProcessedHour;
            if (deltaHours <= 0)
                return;
            if (deltaHours > MAX_DELTA_HOURS_PER_TICK)
                deltaHours = MAX_DELTA_HOURS_PER_TICK;

            // 근처 실체화 마을용 자원 현황 알림 — 던전 안에서는 끈다 (§3.7)
            bool notify = AR.s.Dungeon.IsInside == false;
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData village = villages[i];
                // 인구 0 마을도 부른다 — AdvanceWholeHours가 생산 없이 금고 유지비만 적용한다 (GOLD_BALANCE D135).
                // 접힌 마을(EntityId < 0)은 정착지 시뮬의 추상 경로가 생산하므로 건너뛴다 (이중 생산 없음).
                // 컴포넌트가 보관함에 있어도(던전 안 손제작 마을) 원장은 진행한다 — 미러 갱신은 SyncStorageFromData가 건너뛴다.
                if (village.EntityId < 0)
                    continue;

                int foodBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Food);
                int woodBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Wood);
                int stoneBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Stone);
                int hungerBefore = village.HungerHoursAccumulated;

                AR.s.Village.MarkSaveDirty(village.VillageId, ARPG.Data.VillageSaveGroup.Resources);
                VillageResourceSimulation.AdvanceWholeHours(village, deltaHours);
                AR.s.Village.SyncStorageFromData(village);

                int food = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Food);
                int wood = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Wood);
                int stone = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Stone);
                if (notify == true && (food != foodBefore || wood != woodBefore || stone != stoneBefore))
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
            _lastProcessedHour = -1;
        }
    }
}
