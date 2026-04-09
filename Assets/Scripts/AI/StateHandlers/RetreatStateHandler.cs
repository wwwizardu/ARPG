using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Retreat 상태: 타겟 반대 방향으로 이동, KeepDistance 확보 시 Attack 복귀
    /// 근접/원거리 공통 (KeepDistance 값에 따라 거리 다름)
    /// </summary>
    public class RetreatStateHandler : IAIStateHandler
    {
        private const float RETREAT_MAX_TIME = 0.5f;

        public void OnEnter(int entityId)
        {
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false) return;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;

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

            // 후퇴 시간 초과 시 Attack으로 강제 전환
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var stateComponent))
            {
                if (Time.time - stateComponent.StateEnterTime >= RETREAT_MAX_TIME)
                {
                    AIStateHelper.TransitionToState(entityId, AIState.Attack);
                    return;
                }
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // KeepDistance 확보 여부 확인
            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior))
            {
                float keepDistSqr = behavior.KeepDistance * behavior.KeepDistance;
                if (sqrDistance >= keepDistSqr)
                {
                    AIStateHelper.TransitionToState(entityId, AIState.Attack);
                    return;
                }
            }

            // 타겟 반대 방향으로 후퇴
            AIStateHelper.MoveAwayFrom(entityId, targetTransform.Position);
        }

        public void OnExit(int entityId)
        {
        }
    }
}
