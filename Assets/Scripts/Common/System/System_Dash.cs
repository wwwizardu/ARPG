using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 돌진(DashComponent) 진행 시스템 (SKILL_DESIGN §6.11).
    /// 일정한 속도로 직진시키고, 벽에 닿으면 미끄러지지 않고 그 자리에서 멈춘다.
    /// 끝나면 IsFinished만 표시한다. 경로 판정과 컴포넌트 제거는 System_Skill이 마지막 판정 뒤에 한다.
    /// 자기 이동도 System_Render가 Update에서 위치에 반영하므로, 같은 Update에서 진행해야 끊겨 보이지 않는다 (System_Jump와 같은 이유).
    /// </summary>
    public class System_Dash : IUpdateSystem
    {
        // 남은 이동량이 이보다 작으면 이번 프레임은 움직이지 않는다
        private const float MIN_STEP_SQR = 0.000001f;

        public int Priority => 805; // System_Jump(800) 이후, System_ForcedMove(810)·System_Render(1000) 이전

        public void OnCreate()
        {
            Debug.Log("[System_Dash] Created");
        }

        public void OnReset()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DashComponent> pool = cm.GetComponentPool<DashComponent>();
            for (int i = pool.Count - 1; i >= 0; i--)
                cm.RemoveComponent<DashComponent>(pool.GetEntityId(i));
        }

        public void OnUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DashComponent> pool = cm.GetComponentPool<DashComponent>();
            if (pool.Count == 0)
                return;

            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
            SparseSet<ColliderComponent> colliderPool = cm.GetComponentPool<ColliderComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                DashComponent dash = pool.GetByIndex(i);
                if (dash.IsFinished == true)
                    continue;

                int entityId = pool.GetEntityId(i);
                if (transformPool.TryGet(entityId, out TransformComponent transform) == false)
                {
                    dash.IsFinished = true;
                    pool.SetByIndex(i, dash);
                    continue;
                }

                dash.Elapsed += inDeltaTime;
                float t = dash.Duration > 0f ? Mathf.Clamp01(dash.Elapsed / dash.Duration) : 1f;
                float stepDistance = dash.Distance * t - dash.Traveled;

                if (stepDistance * stepDistance > MIN_STEP_SQR)
                {
                    Vector2 intended = transform.Position + dash.Direction * stepDistance;
                    Vector2 reached = intended;
                    if (colliderPool.TryGet(entityId, out ColliderComponent collider) == true && collider.MoveRadius > 0f)
                        reached = CollisionUtil.ClipTrajectory(transform.Position, intended, collider.MoveRadius);

                    dash.Traveled += (reached - transform.Position).magnitude;
                    transform.Position = reached;
                    cm.SetComponent(entityId, transform);

                    // 벽에 닿으면 남은 거리를 버리고 끝낸다 (미끄러지지 않음, 2026-10-13 사용자 결정)
                    if ((intended - reached).sqrMagnitude > MIN_STEP_SQR)
                        dash.IsFinished = true;
                }

                if (t >= 1f)
                    dash.IsFinished = true;

                pool.SetByIndex(i, dash);
            }
        }
    }
}
