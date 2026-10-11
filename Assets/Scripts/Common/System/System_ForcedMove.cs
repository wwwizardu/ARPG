using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 넉백·당기기(ForcedMoveComponent) 진행 시스템 (SKILL_DESIGN §6.10).
    /// 처음 빠르고 끝에 느린 곡선으로 대상을 옮기고, 벽은 축 분리 충돌로 미끄러진다. 끝나면 컴포넌트를 제거한다.
    /// 자기 이동도 System_Render가 Update에서 위치에 반영하므로, 같은 Update에서 진행해야 끊겨 보이지 않는다 (System_Jump와 같은 이유).
    /// </summary>
    public class System_ForcedMove : IUpdateSystem
    {
        // 한 번에 검사하는 이동량 = MoveRadius × 이 비율. 반경보다 작아야 프레임이 떨어져도 벽을 뚫지 않는다
        private const float STEP_RATIO = 0.9f;

        public int Priority => 810; // System_Jump(800) 이후, System_AttackTelegraph(900)·System_Render(1000) 이전

        public void OnCreate()
        {
            Debug.Log("[System_ForcedMove] Created");
        }

        public void OnReset()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<ForcedMoveComponent> pool = cm.GetComponentPool<ForcedMoveComponent>();
            for (int i = pool.Count - 1; i >= 0; i--)
                cm.RemoveComponent<ForcedMoveComponent>(pool.GetEntityId(i));
        }

        public void OnUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<ForcedMoveComponent> pool = cm.GetComponentPool<ForcedMoveComponent>();
            if (pool.Count == 0)
                return;

            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
            SparseSet<ColliderComponent> colliderPool = cm.GetComponentPool<ColliderComponent>();

            // 끝난 넉백을 제거하므로 역순 순회
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);
                ForcedMoveComponent move = pool.GetByIndex(i);

                if (transformPool.TryGet(entityId, out TransformComponent transform) == false)
                {
                    cm.RemoveComponent<ForcedMoveComponent>(entityId);
                    continue;
                }

                move.Elapsed += inDeltaTime;
                float t = Mathf.Clamp01(move.Elapsed / move.Duration);
                float easedTraveled = move.Distance * (1f - (1f - t) * (1f - t));
                float stepDistance = easedTraveled - move.Traveled;
                move.Traveled = easedTraveled;

                if (stepDistance > 0f)
                {
                    float moveRadius = colliderPool.TryGet(entityId, out ColliderComponent collider) == true ? collider.MoveRadius : 0f;
                    transform.Position = MoveWithCollision(transform.Position, move.Direction * stepDistance, moveRadius);
                    cm.SetComponent(entityId, transform);
                }

                if (t >= 1f)
                {
                    cm.RemoveComponent<ForcedMoveComponent>(entityId);
                    continue;
                }

                pool.SetByIndex(i, move);
            }
        }

        /// <summary>
        /// 이동량을 반경보다 작은 조각으로 나눠 축 분리 충돌을 적용한다. Collider가 없으면 System_Render처럼 그대로 통과.
        /// </summary>
        private static Vector2 MoveWithCollision(Vector2 inPosition, Vector2 inDelta, float inMoveRadius)
        {
            if (inMoveRadius <= 0f)
                return inPosition + inDelta;

            int steps = Mathf.Max(1, Mathf.CeilToInt(inDelta.magnitude / (inMoveRadius * STEP_RATIO)));
            Vector2 stepDelta = inDelta / steps;
            Vector2 position = inPosition;
            for (int i = 0; i < steps; i++)
                position = CollisionUtil.ResolveAxisSeparated(position, stepDelta, inMoveRadius);

            return position;
        }
    }
}
