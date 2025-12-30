using ARPG.Component;
using ARPG.Systems;
using UnityEngine;

namespace ARPG.Systems
{
    // MovementSystem은 고정 타임스텝으로 실행 (일정한 이동 속도 보장)
    public struct System_Movement : IFixedUpdateSystem
    {
        public int Priority => 100;

        private ComponentManager _componentManager;

        public void OnCreate()
        {
            Debug.Log("System_Movement Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Movement Reset called");
        }

        public void Initialize(ComponentManager componentManager)
        {
            _componentManager = componentManager;
        }

        // FixedUpdate: 고정 타임스텝으로 이동 처리
        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (_componentManager == null)
                return;

            // InputComponent와 VelocityComponent를 가진 모든 엔티티 처리
            SparseSet<InputComponent> inputs = _componentManager.GetComponentPool<InputComponent>();
            SparseSet<VelocityComponent> velocities = _componentManager.GetComponentPool<VelocityComponent>();
            SparseSet<TransformComponent> transforms = _componentManager.GetComponentPool<TransformComponent>();

            // 각 엔티티에 대해 처리
            for (int i = 0; i < inputs.Count; i++)
            {
                // TODO: entityId를 가져오는 방법 개선 필요
                // 현재 SparseSet에서 entityId를 직접 가져올 수 없음
                // 예시를 위해 임시로 작성

                // InputComponent input = inputs.Get(entityId);
                // VelocityComponent velocity = velocities.Get(entityId);
                // TransformComponent transform = transforms.Get(entityId);

                // // 속도 계산
                // float currentSpeed = velocity.Speed;
                // if (input.IsSprinting)
                // {
                //     currentSpeed *= velocity.SprintMultiplier;
                // }

                // velocity.Velocity = input.MoveDirection.normalized * currentSpeed;

                // // 위치 업데이트
                // transform.Position += velocity.Velocity * inFixedDeltaTime;

                // // 컴포넌트 업데이트
                // _componentManager.AddComponent(entity, velocity);
                // _componentManager.AddComponent(entity, transform);
            }
        }

        public bool RegisterEntity(IEntity inEntity)
        {
            return false;
        }

        public bool UnregisterEntity(IEntity inEntity)
        {
            return false;
        }

        public void Dispose()
        {
            _componentManager = null;
        }
    }
}
