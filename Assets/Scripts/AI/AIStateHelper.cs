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
        /// NPC의 기본 상태 반환.
        /// NPC + NpcBuildAssignmentComponent 보유 → Build (위협 처리 후 작업 재개)
        /// NPC → Patrol
        /// 몬스터 → Idle
        /// </summary>
        public static AIState GetDefaultState(int entityId)
        {
            if (IsNpc(entityId))
            {
                if (AR.s.Component.HasComponent<NpcBuildAssignmentComponent>(entityId))
                    return AIState.Build;
                return AIState.Patrol;
            }
            return AIState.Idle;
        }

        /// <summary>
        /// 이동 정지. PathfindingComponent도 함께 비활성화.
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
            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var pf))
            {
                if (pf.Status != PathfindingStatus.None)
                {
                    pf.Status = PathfindingStatus.None;
                    cm.SetComponent(entityId, pf);
                }
            }
        }

        /// <summary>
        /// 타겟 방향으로 이동.
        /// PathfindingComponent가 있으면 Goal 갱신 → System_Pathfinding이 A* + waypoint로 Direction 덮어씀.
        /// 없으면 직선 방향 설정 (구 동작).
        /// </summary>
        public static void MoveToward(int entityId, Vector2 targetPosition, float speedMultiplier = 1f)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false) return;
            if (cm.TryGetComponent<StatComponent>(entityId, out var stat) == false) return;

            // Speed 항상 설정 (PF 있어도 handler 의도한 속도 유지)
            velocity.Speed = stat.FinalMoveSpeed * speedMultiplier;
            // 임시 Direction (PF 미연산/실패 케이스 fallback). System_Pathfinding이 Following 상태면 덮어씀.
            velocity.Direction = (targetPosition - transform.Position).normalized;
            cm.SetComponent(entityId, velocity);

            // PathfindingComponent에 Goal 반영
            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var pf))
            {
                Vector2Int targetTile = new Vector2Int(
                    Mathf.FloorToInt(targetPosition.x),
                    Mathf.FloorToInt(targetPosition.y));

                bool changed = false;
                if (pf.Goal != targetTile)
                {
                    pf.LastGoal = pf.Goal;
                    pf.Goal = targetTile;
                    pf.Status = PathfindingStatus.Computing;
                    changed = true;
                }
                else if (pf.Status == PathfindingStatus.None || pf.Status == PathfindingStatus.Failed)
                {
                    // 같은 Goal이지만 비활성/실패 상태면 재계산 트리거
                    pf.Status = PathfindingStatus.Computing;
                    changed = true;
                }

                if (changed == true)
                    cm.SetComponent(entityId, pf);
            }
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
