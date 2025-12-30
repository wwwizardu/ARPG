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

                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                InputComponent input = inputPool.GetByIndex(i);

                // 이동 로직
                if (input.MoveDirection.sqrMagnitude > 0.0001f)
                {
                    // 속도 계산 (달리기 체크)
                    float speed = velocity.Speed;
                    if (input.IsSprinting)
                        speed *= velocity.SprintMultiplier;

                    // 정규화된 방향 * 속도
                    velocity.Velocity = input.MoveDirection.normalized * speed;

                    // 위치 업데이트
                    transform.Position += velocity.Velocity * inFixedDeltaTime;
                }
                else
                {
                    // 입력이 없으면 정지
                    velocity.Velocity = Vector2.zero;
                }

                // 업데이트된 컴포넌트 저장
                AR.s.Component.AddComponent(entityId, velocity);
                AR.s.Component.AddComponent(entityId, transform);
            }
        }

    }
}


