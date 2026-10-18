using ARPG.Component;
using ARPG.Systems;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    // MovementSystem은 고정 타임스텝으로 실행 (물리 기반 이동)
    public partial struct System_Move : IFixedUpdateSystem
    {
        public int Priority => 100;
        
        public void OnCreate()
        {
            Debug.Log("System_Move Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Move Reset called");
        }

        // FixedUpdate로 변경: 일정한 시간 간격으로 이동 처리
        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // ComponentManager에서 필요한 컴포넌트 풀 가져오기
            SparseSet<InputComponent> inputPool = AR.s.Component.GetComponentPool<InputComponent>();

            // InputComponent를 가진 모든 엔티티 순회
            for (int i = 0; i < inputPool.Count; i++)
            {
                int entityId = inputPool.GetEntityId(i);

                // 필요한 컴포넌트 가져오기
                if (AR.s.Component.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false)
                    continue;

                if (AR.s.Component.TryGetComponent<StatComponent>(entityId, out var stat) == false)
                    continue;

                InputComponent input = inputPool.GetByIndex(i);

                // 점프(공중)·넉백·돌진 중에는 System_Jump·System_ForcedMove·System_Dash가 위치를 직접 제어하므로 velocity 이동 정지
                bool isPositionControlled = AR.s.Component.HasComponent<JumpComponent>(entityId)
                    || AR.s.Component.HasComponent<ForcedMoveComponent>(entityId)
                    || AR.s.Component.HasComponent<DashComponent>(entityId);

                // 이동 로직: Direction과 Speed 설정 (Position은 System_Render에서 계산)
                if (isPositionControlled == false && input.MoveDirection.sqrMagnitude > 0.0001f)
                {
                    // 방향 설정 (정규화된 입력)
                    velocity.Direction = input.MoveDirection;
                    velocity.Speed = stat.FinalMoveSpeedValue * SkillHelper.GetSkillMovementMultiplier(entityId)
                        * BuffHelper.GetMoveSlowMultiplier(entityId);

                    // TODO: 여기서 물리 충돌 검사 후 Direction 조정
                    // velocity.Direction = ApplyCollision(velocity.Direction, transform.Position);
                }
                else
                {
                    // 입력이 없거나 공중이면 정지 (방향을 0으로)
                    velocity.Direction = Vector2.zero;
                }

                // Velocity만 저장 (Position은 System_Render에서 업데이트)
                AR.s.Component.SetComponent(entityId, velocity);
            }

            UpdateMovementStates();
        }

        private void UpdateMovementStates()
        {
            SparseSet<StateComponent> statePool = AR.s.Component.GetComponentPool<StateComponent>();

            for (int i = 0; i < statePool.Count; i++)
            {
                int entityId = statePool.GetEntityId(i);
                if (AR.s.Component.HasComponent<JumpComponent>(entityId) == true)
                    continue;

                if (AR.s.Component.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false)
                    continue;

                StateComponent state = statePool.GetByIndex(i);
                // 넉백·돌진 중 AI는 추적 방향을 계속 쓰지만 실제로는 걷지 않으므로 Idle로 둔다
                bool isWalking = velocity.Velocity.sqrMagnitude > 0.001f
                    && AR.s.Component.HasComponent<ForcedMoveComponent>(entityId) == false
                    && AR.s.Component.HasComponent<DashComponent>(entityId) == false;
                Creature.MovementStates nextState = isWalking == true
                    ? Creature.MovementStates.Walking
                    : Creature.MovementStates.Idle;

                if (state.MoveState == nextState)
                    continue;

                state.MovementStatePrev = state.MoveState;
                state.MoveState = nextState;
                statePool.SetByIndex(i, state);
            }
        }
    }
}
