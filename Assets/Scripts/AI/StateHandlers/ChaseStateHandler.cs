using ARPG.Component;

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
            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false) return;

            // 타겟이 없으면 기본 상태로 복귀
            if (ai.TargetEntityId == -1)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            // 타겟 위치 가져오기
            if (cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 공격 범위 내면 Attack
            if (sqrDistance <= behavior.AttackRange * behavior.AttackRange)
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
