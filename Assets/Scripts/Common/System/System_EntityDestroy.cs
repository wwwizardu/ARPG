using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 엔티티 제거 시스템
    /// DestroyTag가 있는 엔티티를 순회하여 정리 및 GameObject 파괴
    ///
    /// 실행 흐름:
    /// 1. DestroyTag가 있는 엔티티 순회 (역순)
    /// 2. EntityRegistry에서 EntityBase 조회
    /// 3. EntityBase.OnEntityDestroy() 호출 (타입별 정리)
    /// 4. EntityIdHelper.DestroyEntity() 호출 (ECS 컴포넌트 정리)
    /// 5. GameObject 파괴
    /// </summary>
    public class System_EntityDestroy : ILateUpdateSystem
    {
        /// <summary>
        /// Priority 950: System_EntityMessage(900) 이후 실행
        /// DeathMessage 처리 완료 후 제거 수행
        /// </summary>
        public int Priority => 950;

        public void OnCreate()
        {
            Debug.Log("[System_EntityDestroy] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_EntityDestroy] Reset");
        }

        public void OnLateUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DestroyTag> pool = cm.GetComponentPool<DestroyTag>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);

                // MonsterTag가 있으면 MonsterManager에서 제거
                if (cm.HasComponent<MonsterTag>(entityId))
                {
                    AR.s.Monster.UnregisterMonsterByEntityId(entityId);
                }

                // NpcTag가 있으면 NpcManager에서 제거
                if (cm.HasComponent<NpcTag>(entityId))
                {
                    AR.s.Npc.UnregisterNpcByEntityId(entityId);
                }

                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    entity.OnEntityDestroy();
                    EntityIdHelper.DestroyEntity(entityId);
                    Object.Destroy(entity.gameObject);
                }
                else
                {
                    // EntityBase 없이 ECS만 있는 엔티티 (발사체 등)
                    // System_Render에 등록된 GameObject 정리
                    var renderSystem = AR.s.System.GetSystem<System_Render>();
                    if (renderSystem != null)
                    {
                        GameObject go = renderSystem.GetGameObject(entityId);
                        if (go != null)
                        {
                            Object.Destroy(go);
                        }
                        renderSystem.UnregisterGameObject(entityId);
                    }

                    EntityIdHelper.DestroyEntity(entityId);
                }
            }
        }
    }
}
