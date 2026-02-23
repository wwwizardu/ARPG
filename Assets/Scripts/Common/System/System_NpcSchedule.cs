#nullable enable
using ARPG.Component;

namespace ARPG.Systems
{
    /// <summary>
    /// NPC 성격 기반 활동 결정 시스템
    /// NpcStatComponent를 읽어 NpcScheduleComponent.CurrentActivity를 갱신
    /// </summary>
    public class System_NpcSchedule : IFixedUpdateSystem
    {
        public int Priority => 55;
        public float UpdateInterval => 1.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<NpcScheduleComponent> pool = cm.GetComponentPool<NpcScheduleComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                NpcScheduleComponent schedule = pool.GetByIndex(i);

                // TODO: NpcStatComponent 성격 기반으로 활동 결정 로직 구현
                schedule.ActivityTimer += inFixedDeltaTime;

                pool.SetByIndex(i, schedule);
            }
        }

        public void OnReset() { }
    }
}
