using ARPG.Component;
using ARPG.Utility;
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

            if (AIStateHelper.TryGetValidTargetTransform(entityId, ref ai, out var targetTransform) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 교전 사거리(쓸 수 있는 스킬 기준) 밖이면 Chase
            float engagementSqr = SkillHelper.GetEngagementRangeSqr(entityId, SkillHelper.AiSkillSlotCount);
            if (engagementSqr > 0f && sqrDistance > engagementSqr)
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

            // 스킬 발동 - AiTable 가중치 기반 랜덤 선택
            int slotIndex = SkillHelper.PickFireSkill(entityId, targetTransform.Position, SkillHelper.AiSkillSlotCount);
            if (slotIndex < 0)
                return;

            if (SkillHelper.GetSkillCommandComponent(slotIndex, entityId, targetTransform.Position, out var command))
            {
                cm.SetComponent(entityId, command);
            }
        }

        public void OnExit(int entityId)
        {
        }
    }
}
