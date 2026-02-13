using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 거리 기반 엔티티 활성화/비활성화 시스템
    /// ActivationDistanceComponent가 있는 모든 엔티티를 대상으로
    /// 플레이어와의 거리를 체크하여 히스테리시스 방식으로 GameObject를 활성화/비활성화
    /// 몬스터, NPC, 아이템 등 모든 엔티티 타입에서 사용 가능
    /// </summary>
    public class System_EntityActivation : IFixedUpdateSystem
    {
        /// <summary>
        /// Priority 500: 게임플레이 로직 이후, 렌더링 이전
        /// </summary>
        public int Priority => 500;

        /// <summary>
        /// 0.5초마다 실행
        /// </summary>
        public float UpdateInterval => 0.5f;

        public void OnCreate()
        {
            Debug.Log("[System_EntityActivation] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_EntityActivation] Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            Creature.ArpgPlayer player = AR.s.MyPlayer;
            if (player == null)
                return;

            Vector3 playerPos = player.transform.position;
            ComponentManager cm = AR.s.Component;
            SparseSet<ActivationDistanceComponent> pool = cm.GetComponentPool<ActivationDistanceComponent>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                ActivationDistanceComponent activation = pool.GetByIndex(i);

                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                float distSqr = (playerPos - new Vector3(transform.Position.x, transform.Position.y, 0f)).sqrMagnitude;

                if (activation.IsActivated)
                {
                    if (distSqr > activation.DeactivationDistanceSqr)
                    {
                        activation.IsActivated = false;
                        if (AR.s.Message.TryGetEntity(entityId, out var entity))
                        {
                            entity.gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    if (distSqr <= activation.ActivationDistanceSqr)
                    {
                        activation.IsActivated = true;
                        if (AR.s.Message.TryGetEntity(entityId, out var entity))
                        {
                            entity.gameObject.SetActive(true);
                        }
                    }
                }

                pool.Set(entityId, activation);
            }
        }
    }
}
