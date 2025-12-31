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

                // 이동 로직: Velocity만 계산 (Position은 System_Render에서 계산)
                if (input.MoveDirection.sqrMagnitude > 0.0001f)
                {
                    // 속도 계산 (달리기 체크)
                    float speed = velocity.Speed;
                    if (input.IsSprinting)
                        speed *= velocity.SprintMultiplier;

                    // 정규화된 방향 * 속도
                    velocity.Velocity = input.MoveDirection * speed;

                    // TODO: 여기서 물리 충돌 검사 후 Velocity 조정
                    // velocity.Velocity = ApplyCollision(velocity.Velocity, transform.Position);
                }
                else
                {
                    // 입력이 없으면 정지
                    velocity.Velocity = Vector2.zero;
                }

                // Velocity만 저장 (Position은 System_Render에서 업데이트)
                AR.s.Component.AddComponent(entityId, velocity);
            }
        }

    }
}


