using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.Behaviors
{
    public class MeleeAIBehavior : IAIBehavior
    {
        public void OnEnterState(int entityId, AIState state)
        {
            ComponentManager cm = AR.s.Component;

            switch (state)
            {
                case AIState.Chase:
                    Debug.Log($"Melee AI {entityId} started chasing target");
                    // 추격 시작 시 속도 증가
                    if (cm.TryGetComponent<MovementComponent>(entityId, out var movement))
                    {
                        //movement.MoveSpeed *= 1.2f;
                        //cm.SetComponent(entityId, movement);
                    }
                    break;

                case AIState.Attack:
                    if(cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == true)
                    {
                        velocity.Direction = Vector2.zero;
                        cm.SetComponent(entityId, velocity);
                    }

                    Debug.Log($"Melee AI {entityId} started attacking");
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
            }
        }

        public void OnExitState(int entityId, AIState state)
        {
            ComponentManager cm = AR.s.Component;

            switch (state)
            {
                case AIState.Chase:
                    // 추격 종료 시 속도 원복
                    if (cm.TryGetComponent<MovementComponent>(entityId, out var movement))
                    {
                        //movement.MoveSpeed /= 1.2f;
                        cm.SetComponent(entityId, movement);
                    }
                    break;
            }
        }

        private void UpdateIdle(int entityId, ComponentManager cm, float deltaTime)
        {
            // 타겟을 발견하면 추격 상태로 전환
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

            // 타겟 위치 가져오기
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
            // 타겟을 잃으면 대기 상태로
            else if (!cm.HasComponent<AICanSeeTargetTag>(entityId))
            {
                TransitionToState(entityId, cm, AIState.Idle);
            }
            else
            {
                // 타겟을 향해 이동
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
            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false)
                return;

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                return;

            if (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false)
                return;

            // 타겟이 없으면 Idle로 전환
            if (ai.TargetEntityId == -1)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            // 타겟 거리 확인
            if (cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var targetTransform) == false)
            {
                TransitionToState(entityId, cm, AIState.Idle);
                return;
            }

            float sqrDistance = (targetTransform.Position - transform.Position).sqrMagnitude;

            // 공격 범위 밖으로 나가면 다시 추격
            if (sqrDistance > behavior.AttackRange * behavior.AttackRange)
            {
                TransitionToState(entityId, cm, AIState.Chase);
            }
            else
            {
                // 근접 공격 실행
                // TODO: 스킬 시스템과 연동하여 공격 스킬 발동
                if(ARPG.Utility.SkillEntityIdHelper.GetSkillCommandComponent(0, entityId, targetTransform.Position, out var command) == true)
                {
                    AR.s.Component.SetComponent(entityId, command);
                }

                Debug.Log($"Melee AI {entityId} attacking target {ai.TargetEntityId}");
            }
        }

        private void TransitionToState(int entityId, ComponentManager cm, AIState newState)
        {
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var stateComponent) == false)
                return;

            AIState oldState = stateComponent.CurrentState;

            // 같은 상태로의 전환은 무시
            if (oldState == newState)
                return;

            // 상태 전환
            OnExitState(entityId, oldState);

            stateComponent.PreviousState = oldState;
            stateComponent.CurrentState = newState;
            stateComponent.StateEnterTime = Time.time;
            cm.SetComponent(entityId, stateComponent);

            OnEnterState(entityId, newState);
        }
    }
}
