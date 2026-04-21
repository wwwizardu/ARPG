using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// 원거리 Attack 상태: 정지 + 스킬 사용, KeepDistance 체크 + ATTACK_MIN_TIME 이후 Retreat
    /// </summary>
    public class RangedAttackStateHandler : IAIStateHandler
    {
        private const float ATTACK_MIN_TIME = 1.5f;

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

            // 교전 사거리(쓸 수 있는 스킬 기준) 밖이면 Chase
            float engagementSqr = SkillHelper.GetEngagementRangeSqr(entityId, SkillHelper.AiSkillSlotCount);
            if (engagementSqr > 0f && sqrDistance > engagementSqr)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Chase);
                return;
            }

            // KeepDistance 체크 — Attack 최소 유지 시간 이후에만 Retreat
            float keepDistSqr = behavior.KeepDistance * behavior.KeepDistance;
            if (keepDistSqr > 0 && sqrDistance < keepDistSqr * 0.5f)
            {
                if (cm.TryGetComponent<AIStateComponent>(entityId, out var stateComp))
                {
                    float timeInAttack = Time.time - stateComp.StateEnterTime;
                    if (timeInAttack >= ATTACK_MIN_TIME)
                    {
                        AIStateHelper.TransitionToState(entityId, AIState.Retreat);
                        return;
                    }
                }
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
