#nullable enable
using ARPG.Component;

namespace ARPG.Systems
{
    /// <summary>
    /// 작업 중인 NPC의 마을 자원 생산 시스템
    /// CurrentActivity == Working인 NPC만 직업에 따라 자원 생산
    /// </summary>
    public class System_VillageResource : IFixedUpdateSystem
    {
        public int Priority => 57;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<NpcJobComponent> pool = cm.GetComponentPool<NpcJobComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);

                if (cm.TryGetComponent<NpcScheduleComponent>(entityId, out var schedule) == false)
                    continue;

                if (schedule.CurrentActivity != ActivityType.Working)
                    continue;

                if (cm.TryGetComponent<NpcVillageComponent>(entityId, out var village) == false)
                    continue;

                NpcJobComponent job = pool.GetByIndex(i);

                // TODO: JobType + SkillLevel 기반 자원 생산량 계산
                // AR.s.Village.ProduceResource(village.VillageId, itemType, amount);
            }
        }

        public void OnReset() { }
    }
}
