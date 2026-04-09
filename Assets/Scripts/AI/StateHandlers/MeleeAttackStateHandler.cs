using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// 근접 Attack 상태: 정지 + 스킬 사용, KeepDistance 체크
    /// </summary>
    public class MeleeAttackStateHandler : IAIStateHandler
    {
        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false) return;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false) return;

            // 타겟이 없으면 기본 상태로
            if (ai.TargetEntityId == -1)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 공격 범위 밖이면 Chase
            if (sqrDistance > behavior.AttackRange * behavior.AttackRange)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Chase);
                return;
            }

            // KeepDistance 체크 — 너무 가까우면 Retreat
            float keepDistSqr = behavior.KeepDistance * behavior.KeepDistance;
            if (keepDistSqr > 0 && sqrDistance < keepDistSqr)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Retreat);
                return;
            }

            // 스킬 사용 (OnEnter에서 이미 정지)
            if (ARPG.Utility.SkillHelper.GetSkillCommandComponent(0, entityId, targetTransform.Position, out var command))
            {
                cm.SetComponent(entityId, command);
            }
        }

        public void OnExit(int entityId)
        {
        }
    }
}
