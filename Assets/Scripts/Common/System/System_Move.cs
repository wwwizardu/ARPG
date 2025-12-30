using System.Collections.Generic;
using ARPG.Systems;
using UnityEngine;

namespace ARPG.Systems
{
    // MovementSystem은 고정 타임스텝으로 실행 (물리 기반 이동)
    public partial struct System_Move : IFixedUpdateSystem
    {
        public int Priority => 100;
        private List<IMovable> _movableEntities;

        public void OnCreate()
        {
            _movableEntities = new List<IMovable>();
            Debug.Log("System_Move Created");
        }

        // FixedUpdate로 변경: 일정한 시간 간격으로 이동 처리
        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            for (int i = 0; i < _movableEntities.Count; i++)
            {
                

                _movableEntities[i].UpdateVelocity(inFixedDeltaTime);
                _movableEntities[i].UpdatePosition(inFixedDeltaTime);
            }
        }

        public bool RegisterEntity(IEntity inEntity)
        {
            var movableEntity = inEntity as IMovable;

            if (movableEntity != null)
            {
                _movableEntities.Add(movableEntity);
                return true;
            }

            return false;
        }

        public bool UnregisterEntity(IEntity inEntity)
        {
            var movableEntity = inEntity as IMovable;

            if (movableEntity != null)
            {
                _movableEntities.Remove(movableEntity);
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            _movableEntities.Clear();
        }
    }
}


