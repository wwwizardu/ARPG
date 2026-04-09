using ARPG.Component;
using UnityEngine;

namespace ARPG.AI
{
    /// <summary>
    /// AI 상태 전환 유틸리티
    /// 모든 StateHandler에서 공통으로 사용하는 상태 전환 로직
    /// </summary>
    public static class AIStateHelper
    {
        public static void TransitionToState(int entityId, AIState newState)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var state) == false) return;
            if (state.CurrentState == newState) return;
            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false) return;

            // 이전 상태 Exit
            IAIStateHandler oldHandler = AIBehaviorFactory.GetStateHandler(behavior.BehaviorType, state.CurrentState);
            if (oldHandler != null)
            {
                oldHandler.OnExit(entityId);
            }

            // 상태 전환
            state.PreviousState = state.CurrentState;
            state.CurrentState = newState;
            state.StateEnterTime = Time.time;
            cm.SetComponent(entityId, state);

            // 새 상태 Enter
            IAIStateHandler newHandler = AIBehaviorFactory.GetStateHandler(behavior.BehaviorType, newState);
            if (newHandler != null)
            {
                newHandler.OnEnter(entityId);
            }
        }

        /// <summary>
        /// NPC인지 확인 (NpcTag 보유 여부)
        /// </summary>
        public static bool IsNpc(int entityId)
        {
            return AR.s.Component.HasComponent<NpcTag>(entityId);
        }

        /// <summary>
        /// NPC의 기본 상태 반환 (Patrol), 몬스터는 Idle
        /// </summary>
        public static AIState GetDefaultState(int entityId)
        {
            if (IsNpc(entityId))
            {
                return AIState.Patrol;
            }
            return AIState.Idle;
        }

        /// <summary>
        /// 이동 정지
        /// </summary>
        public static void StopMovement(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity))
            {
                velocity.Direction = Vector2.zero;
                velocity.Speed = 0f;
                cm.SetComponent(entityId, velocity);
            }
        }

        /// <summary>
        /// 타겟 방향으로 이동
        /// </summary>
        public static void MoveToward(int entityId, Vector2 targetPosition, float speedMultiplier = 1f)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false) return;
            if (cm.TryGetComponent<StatComponent>(entityId, out var stat) == false) return;

            Vector2 direction = (targetPosition - transform.Position).normalized;
            velocity.Direction = direction;
            velocity.Speed = stat.FinalMoveSpeed * speedMultiplier;
            cm.SetComponent(entityId, velocity);
        }

        /// <summary>
        /// 타겟 반대 방향으로 이동
        /// </summary>
        public static void MoveAwayFrom(int entityId, Vector2 threatPosition, float speedMultiplier = 1f)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false) return;
            if (cm.TryGetComponent<StatComponent>(entityId, out var stat) == false) return;

            Vector2 direction = (transform.Position - threatPosition).normalized;
            velocity.Direction = direction;
            velocity.Speed = stat.FinalMoveSpeed * speedMultiplier;
            cm.SetComponent(entityId, velocity);
        }
    }
}
