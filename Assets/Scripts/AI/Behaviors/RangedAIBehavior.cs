using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.Behaviors
{
    public class RangedAIBehavior : IAIBehavior
    {
        private const float KEEP_DISTANCE = 7f;  // 유지할 거리

        public void OnEnterState(int entityId, AIState state)
        {
            ComponentManager cm = AR.s.Component;

            switch (state)
            {
                case AIState.Attack:
                    Debug.Log($"Ranged AI {entityId} started attacking from distance");
                    break;

                case AIState.Retreat:
                    Debug.Log($"Ranged AI {entityId} retreating to maintain distance");
                    break;
            }
        }

        public void OnUpdateState(int entityId, AIState state, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            switch (state)
            {
                case AIState.Idle:
                    UpdateIdle(entityId, cm, deltaTime);
                    break;

                case AIState.Chase:
                    UpdateChase(entityId, cm, deltaTime);
                    break;

                case AIState.Attack:
                    UpdateAttack(entityId, cm, deltaTime);
                    break;

                case AIState.Retreat:
                    UpdateRetreat(entityId, cm, deltaTime);
                    break;
            }
        }

        public void OnExitState(int entityId, AIState state)
        {
            // 필요시 구현
        }

        private void UpdateIdle(int entityId, ComponentManager cm, float deltaTime)
        {
            if (cm.HasComponent<AICanSeeTargetTag>(entityId))
            {
                TransitionToState(entityId, cm, AIState.Chase);
            }
        }

        private void UpdateChase(int entityId, ComponentManager cm, float deltaTime)
        {
            if (!cm.TryGetComponent<AIComponent>(entityId, out var ai))
                return;

            if (!cm.TryGetComponent<TransformComponent>(entityId, out var transform))
                return;

            if (!cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior))
                return;

            // 타겟이 없으면 Idle로 전환
            if (ai.TargetEntityId == -1)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            if (!cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform))
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 공격 범위 내면 공격 상태로 전환
            if (sqrDistance <= behavior.AttackRange * behavior.AttackRange)
            {
                TransitionToState(entityId, cm, AIState.Attack);
            }
            else if (!cm.HasComponent<AICanSeeTargetTag>(entityId))
            {
                TransitionToState(entityId, cm, AIState.Idle);
            }
            else
            {
                // 적정 거리까지 이동 (너무 가까이 가지 않음)
                Vector2 direction = (targetTransform.Position - transform.Position).normalized;

                if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    velocity.Direction = direction;
                    cm.SetComponent(entityId, velocity);
                }
            }
        }

        private void UpdateAttack(int entityId, ComponentManager cm, float deltaTime)
        {
            if (!cm.TryGetComponent<AIComponent>(entityId, out var ai))
                return;

            if (!cm.TryGetComponent<TransformComponent>(entityId, out var transform))
                return;

            if (!cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior))
                return;

            // 타겟이 없으면 Idle로 전환
            if (ai.TargetEntityId == -1)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            if (!cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform))
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;
            float keepDistanceSqr = KEEP_DISTANCE * KEEP_DISTANCE;

            // 타겟이 너무 가까우면 후퇴
            if (sqrDistance < keepDistanceSqr * 0.5f)
            {
                TransitionToState(entityId, cm, AIState.Retreat);
            }
            // 공격 범위 밖으로 나가면 추격
            else if (sqrDistance > behavior.AttackRange * behavior.AttackRange)
            {
                TransitionToState(entityId, cm, AIState.Chase);
            }
            else
            {
                // 원거리 공격 실행 (정지 상태에서)
                if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    velocity.Direction = Vector2.zero;
                    cm.SetComponent(entityId, velocity);
                }

                // TODO: 원거리 스킬 발동
                Debug.Log($"Ranged AI {entityId} attacking target {ai.TargetEntityId}");
            }
        }

        private void UpdateRetreat(int entityId, ComponentManager cm, float deltaTime)
        {
            if (!cm.TryGetComponent<AIComponent>(entityId, out var ai))
                return;

            if (!cm.TryGetComponent<TransformComponent>(entityId, out var transform))
                return;

            // 타겟이 없으면 Idle로 전환
            if (ai.TargetEntityId == -1)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            if (!cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform))
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;
            float keepDistanceSqr = KEEP_DISTANCE * KEEP_DISTANCE;

            // 적정 거리를 확보하면 다시 공격
            if (sqrDistance >= keepDistanceSqr)
            {
                TransitionToState(entityId, cm, AIState.Attack);
            }
            else
            {
                // 타겟 반대 방향으로 후퇴
                Vector2 direction = (transform.Position - targetTransform.Position).normalized;

                if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    velocity.Direction = direction;
                    cm.SetComponent(entityId, velocity);
                }
            }
        }

        private void TransitionToState(int entityId, ComponentManager cm, AIState newState)
        {
            if (!cm.TryGetComponent<AIStateComponent>(entityId, out var stateComponent))
                return;

            AIState oldState = stateComponent.CurrentState;

            // 같은 상태로의 전환은 무시
            if (oldState == newState)
                return;

            OnExitState(entityId, oldState);

            stateComponent.PreviousState = oldState;
            stateComponent.CurrentState = newState;
            stateComponent.StateEnterTime = Time.time;
            cm.SetComponent(entityId, stateComponent);

            OnEnterState(entityId, newState);
        }
    }
}
