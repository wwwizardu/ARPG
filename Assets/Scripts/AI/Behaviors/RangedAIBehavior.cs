using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.Behaviors
{
    public class RangedAIBehavior : IAIBehavior
    {
        private const float KEEP_DISTANCE = 7f;     // 유지할 거리
        private const float RETREAT_MAX_TIME = 0.5f; // 최대 후퇴 시간 (초)
        private const float ATTACK_MIN_TIME = 1.5f;  // Attack 진입 후 최소 유지 시간 (Retreat 방지)

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
                    if (cm.TryGetComponent<StatComponent>(entityId, out var stat))
                    {
                        velocity.Direction = direction;
                        velocity.Speed = stat.FinalMoveSpeed;
                        cm.SetComponent(entityId, velocity);
                    }
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

            // 공격 범위 밖으로 나가면 추격
            if (sqrDistance > behavior.AttackRange * behavior.AttackRange)
            {
                TransitionToState(entityId, cm, AIState.Chase);
                return;
            }

            // 타겟이 너무 가까우면 후퇴 (Attack 최소 유지 시간 이후에만)
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var stateComp))
            {
                float timeInAttack = Time.time - stateComp.StateEnterTime;
                if (sqrDistance < keepDistanceSqr * 0.5f && timeInAttack >= ATTACK_MIN_TIME)
                {
                    TransitionToState(entityId, cm, AIState.Retreat);
                    return;
                }
            }

            // 원거리 공격 실행 (정지 상태에서)
            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity))
            {
                velocity.Direction = Vector2.zero;
                velocity.Speed = 0f;
                cm.SetComponent(entityId, velocity);
            }

            if (ARPG.Utility.SkillHelper.GetSkillCommandComponent(0, entityId, targetTransform.Position, out var command) == true)
            {
                AR.s.Component.SetComponent(entityId, command);
            }
        }

        private void UpdateRetreat(int entityId, ComponentManager cm, float deltaTime)
        {
            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false)
                return;

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                return;

            // 타겟이 없으면 Idle로 전환
            if (ai.TargetEntityId == -1)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform) == false)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            // 후퇴 시간 초과 시 Attack으로 강제 전환
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var stateComponent))
            {
                if (Time.time - stateComponent.StateEnterTime >= RETREAT_MAX_TIME)
                {
                    TransitionToState(entityId, cm, AIState.Attack);
                    return;
                }
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
                    if (cm.TryGetComponent<StatComponent>(entityId, out var stat))
                    {
                        velocity.Direction = direction;
                        velocity.Speed = stat.FinalMoveSpeed;
                        cm.SetComponent(entityId, velocity);
                    }
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
