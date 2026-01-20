using System.Collections.Generic;
using ARPG.Component;
using ARPG.Systems;
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
        public readonly void OnFixedUpdate(float inFixedDeltaTime)
        {

            if(AR.s.Component.TryGetComponent<StatComponent>(0, out var stat) == false)
            {
                Debug.LogError("System_Move: StatComponent for entity 0 not found.");
                return;
            }   

            // ComponentManager에서 필요한 컴포넌트 풀 가져오기
            SparseSet<InputComponent> inputPool = AR.s.Component.GetComponentPool<InputComponent>();

            // InputComponent를 가진 모든 엔티티 순회
            for (int i = 0; i < inputPool.Count; i++)
            {
                int entityId = inputPool.GetEntityId(i);

                // 필요한 컴포넌트 가져오기
                if (AR.s.Component.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false)
                    continue;

                InputComponent input = inputPool.GetByIndex(i);

                // 이동 로직: Direction과 Speed 설정 (Position은 System_Render에서 계산)
                if (input.MoveDirection.sqrMagnitude > 0.0001f)
                {
                    // 방향 설정 (정규화된 입력)
                    velocity.Direction = input.MoveDirection;
                    velocity.Speed = stat.FinalMoveSpeed;

                    // TODO: 여기서 물리 충돌 검사 후 Direction 조정
                    // velocity.Direction = ApplyCollision(velocity.Direction, transform.Position);
                }
                else
                {
                    // 입력이 없으면 정지 (방향을 0으로)
                    velocity.Direction = Vector2.zero;
                }

                // Velocity만 저장 (Position은 System_Render에서 업데이트)
                AR.s.Component.SetComponent(entityId, velocity);

                if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state) == true)
                {
                    CheckStateChanges(entityId, ref velocity, ref state);
                }
            }


        }

        private readonly void CheckStateChanges(int entityId, ref VelocityComponent refVelocity,  ref StateComponent refState)
        {
            // 이동 상태 업데이트
            if (refVelocity.Velocity.sqrMagnitude > 0.001f)
            {
                if(refState.MoveState != Creature.MovementStates.Walking)
                {
                    refState.MovementStatePrev = refState.MoveState;
                    refState.MoveState = Creature.MovementStates.Walking;    
                }
            }
            else
            {
                if(refState.MoveState != Creature.MovementStates.Idle)
                {
                    refState.MovementStatePrev = refState.MoveState;
                    refState.MoveState = Creature.MovementStates.Idle;
                }
            }

            AR.s.Component.SetComponent(entityId, refState);
        }

    }
}


