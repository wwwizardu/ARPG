using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 점프 시스템 - 점프 중인 엔티티의 포물선 궤적을 갱신하고 착지를 처리
    /// Update에서 실행되어 렌더 프레임 레이트에 맞춰 부드럽게 보간
    /// </summary>
    public partial struct System_Jump : IUpdateSystem
    {
        /// <summary>
        /// 무적 판정이 시작되는 높이. 이 값 이상이면 모든 공격 회피
        /// 점프별로 다르지 않은 게임 플레이 규칙이라 전역 상수로 관리
        /// </summary>
        public const float InvincibleHeight = 0.5f;

        public int Priority => 800; // System_Render(1000) 이전 실행

        public void OnCreate()
        {
            Debug.Log("System_Jump Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Jump Reset called");
        }

        public void OnUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<JumpComponent> jumpPool = cm.GetComponentPool<JumpComponent>();

            // 착지 처리를 위해 역순 순회 (제거 시 인덱스 꼬임 방지)
            for (int i = jumpPool.Count - 1; i >= 0; i--)
            {
                int entityId = jumpPool.GetEntityId(i);
                JumpComponent jump = jumpPool.GetByIndex(i);

                jump.Elapsed += inDeltaTime;

                if (jump.Elapsed >= jump.Duration)
                {
                    // 착지
                    Land(entityId, ref jump);
                    continue;
                }

                // 포물선 궤적 계산
                float t = jump.Elapsed / jump.Duration;
                jump.Height = 4f * jump.MaxHeight * t * (1f - t);

                // 수평 위치 보간
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
                {
                    Vector2 horizontalPos = Vector2.Lerp(jump.StartPosition, jump.EndPosition, t);
                    transform.Position = horizontalPos;
                    cm.SetComponent(entityId, transform);
                }

                cm.SetComponent(entityId, jump);
            }
        }

        /// <summary>
        /// 착지 처리 - 최종 위치 확정, JumpComponent 제거, 상태 초기화
        /// </summary>
        private void Land(int entityId, ref JumpComponent jump)
        {
            ComponentManager cm = AR.s.Component;

            // 최종 위치 확정
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
            {
                transform.Position = jump.EndPosition;
                cm.SetComponent(entityId, transform);
            }

            // 이동 상태 복귀
            if (cm.TryGetComponent<StateComponent>(entityId, out var state) == true)
            {
                if (state.MoveState == Creature.MovementStates.Jumping)
                {
                    state.MovementStatePrev = state.MoveState;
                    state.MoveState = Creature.MovementStates.Idle;
                    cm.SetComponent(entityId, state);
                }
            }

            // JumpComponent 제거
            cm.RemoveComponent<JumpComponent>(entityId);

            Debug.Log($"[System_Jump] Landed - EntityId: {entityId}, Position: {jump.EndPosition}");
        }
    }
}
