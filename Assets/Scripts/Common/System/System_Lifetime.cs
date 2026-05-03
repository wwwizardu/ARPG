using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// LifetimeComponent의 Remaining을 매 fixed step마다 감소시키고,
    /// 0 이하가 되면 DestroyTag를 부착해 System_EntityDestroy가 정리하도록 위임.
    /// 토템·지뢰·지속형 장판 등 자연 만료 엔티티의 공용 시스템.
    /// </summary>
    public class System_Lifetime : IFixedUpdateSystem
    {
        public int Priority => 70;  // AI Behavior(50) 이후, Move(100) 이전
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_Lifetime] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<LifetimeComponent> pool = cm.GetComponentPool<LifetimeComponent>();
            if (pool == null || pool.Count == 0)
                return;

            // 역순 순회: 만료 시 DestroyTag 부착이 일어나므로 인덱스 안정성 확보
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);
                LifetimeComponent lifetime = pool.GetByIndex(i);

                lifetime.Remaining -= inFixedDeltaTime;

                if (lifetime.Remaining <= 0f)
                {
                    cm.AddComponent(entityId, new DestroyTag());
                    Debug.Log($"[System_Lifetime] Entity {entityId} expired, DestroyTag attached");
                    continue;
                }

                cm.SetComponent(entityId, lifetime);
            }
        }
    }
}
