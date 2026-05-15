using ARPG.Component;
using ARPG.Utility;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Chase 상태: 타겟을 추적, 공격 범위 도달 시 Attack, 타겟 상실 시 기본 상태로 복귀
    /// 몬스터/NPC 공통
    /// </summary>
    public class ChaseStateHandler : IAIStateHandler
    {
        public void OnEnter(int entityId)
        {
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false) return;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;

            // 타겟이 없으면 기본 상태로 복귀
            if (ai.TargetEntityId == -1)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            // 타겟 위치 가져오기
            if (AIStateHelper.TryGetValidTargetTransform(entityId, ref ai, out var targetTransform) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 교전 사거리(쓸 수 있는 스킬의 최대 사거리) 안이면 Attack 전이
            float engagementSqr = SkillHelper.GetEngagementRangeSqr(entityId, SkillHelper.AiSkillSlotCount);
            if (engagementSqr > 0f && sqrDistance <= engagementSqr)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Attack);
            }
            // 타겟을 시야에서 잃으면 기본 상태로
            else if (cm.HasComponent<AICanSeeTargetTag>(entityId) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
            }
            else
            {
                // 타겟을 향해 이동
                AIStateHelper.MoveToward(entityId, targetTransform.Position);
            }
        }

        public void OnExit(int entityId)
        {
        }
    }
}
