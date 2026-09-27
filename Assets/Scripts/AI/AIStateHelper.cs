using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.AI
{
    /// <summary>
    /// AI 상태 전환 유틸리티
    /// 모든 StateHandler에서 공통으로 사용하는 상태 전환 로직
    /// </summary>
    public static class AIStateHelper
    {
        private const float PATH_RETRY_COOLDOWN = 1f;

        /// <summary>
        /// 생성 시 설정된 최초 AI 상태의 진입 처리를 실행한다.
        /// AIBehaviorTypeComponent와 AIStateComponent 등록이 끝난 뒤 한 번만 호출한다.
        /// </summary>
        public static void EnterInitialState(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var state) == false) return;
            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false) return;

            state.StateEnterTime = Time.time;
            cm.SetComponent(entityId, state);

            IAIStateHandler handler = AIBehaviorFactory.GetStateHandler(behavior.BehaviorType, state.CurrentState);
            if (handler != null)
            {
                handler.OnEnter(entityId);
            }
        }

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
        /// 결투 중 NPC → Idle (상대를 다시 인식하면 Chase. Patrol의 겁많음 Flee 전환·Follow 추종을 타지 않게 한다)
        /// 동행 NPC → Follow (Chase/Attack에서 타겟을 잃으면 추종으로 복귀)
        /// NPC → Patrol
        /// 몬스터 → Idle
        /// </summary>
        public static AIState GetDefaultState(int entityId)
        {
            if (AR.s.Component.HasComponent<DuelComponent>(entityId) && IsNpc(entityId))
                return AIState.Idle;
            if (AR.s.Component.HasComponent<NpcCompanionComponent>(entityId))
                return AIState.Follow;
            if (IsNpc(entityId))
                return AIState.Patrol;
            return AIState.Idle;
        }

        public static bool TryGetValidTargetTransform(
            int entityId,
            ref AIComponent ai,
            out TransformComponent targetTransform,
            bool requireStatComponent = true)
        {
            targetTransform = default;

            if (ai.TargetEntityId == -1)
                return false;

            ComponentManager cm = AR.s.Component;
            int targetEntityId = ai.TargetEntityId;
            if (AR.s.Entity.IsEntityRegistered(targetEntityId) == false ||
                cm.HasComponent<DestroyTag>(targetEntityId) ||
                cm.TryGetComponent<TransformComponent>(targetEntityId, out targetTransform) == false ||
                (requireStatComponent && cm.HasComponent<StatComponent>(targetEntityId) == false) ||
                FactionHelper.IsHostileTo(entityId, targetEntityId) == false)
            {
                ClearTarget(entityId, ref ai);
                targetTransform = default;
                return false;
            }

            return true;
        }

        public static void ClearTarget(int entityId, ref AIComponent ai)
        {
            ai.TargetEntityId = -1;
            ComponentManager cm = AR.s.Component;
            cm.SetComponent(entityId, ai);
            cm.RemoveComponent<AICanSeeTargetTag>(entityId);
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
            Vector2 directDirection = (targetPosition - transform.Position).normalized;
            velocity.Speed = stat.FinalMoveSpeedValue * speedMultiplier * SkillHelper.GetSkillMovementMultiplier(entityId);
            // 임시 Direction (PF 미연산/실패 케이스 fallback). System_Pathfinding이 Following 상태면 덮어씀.

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
                else if (pf.Status == PathfindingStatus.None)
                {
                    // 같은 Goal이지만 비활성 상태면 재계산 트리거
                    pf.Status = PathfindingStatus.Computing;
                    changed = true;
                }
                else if (pf.Status == PathfindingStatus.Failed)
                {
                    if (Time.time - pf.LastRecomputedTime >= PATH_RETRY_COOLDOWN)
                    {
                        pf.Status = PathfindingStatus.Computing;
                        changed = true;
                    }
                    else
                    {
                        velocity.Direction = Vector2.zero;
                        cm.SetComponent(entityId, velocity);
                        return;
                    }
                }

                velocity.Direction = directDirection;
                cm.SetComponent(entityId, velocity);

                if (changed == true)
                    cm.SetComponent(entityId, pf);

                return;
            }

            velocity.Direction = directDirection;
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
            velocity.Speed = stat.FinalMoveSpeedValue * speedMultiplier * SkillHelper.GetSkillMovementMultiplier(entityId);
            cm.SetComponent(entityId, velocity);
        }
    }
}
