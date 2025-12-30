using UnityEngine;

namespace ARPG.Systems
{
    // 기본 System 인터페이스
    public interface ISystem
    {
        public int Priority { get; }

        public void OnCreate()
        {
            Debug.Log("ISystem OnCreate called");
        }

        public bool RegisterEntity(IEntity inEntity);
        public bool UnregisterEntity(IEntity inEntity);
        public void Dispose();
    }

    // Update 매 프레임마다 실행 (입력, 렌더링, UI 등)
    public interface IUpdateSystem : ISystem
    {
        public void OnUpdate(float inDeltaTime);
    }

    // FixedUpdate 고정 타임스텝으로 실행 (물리, 게임플레이 로직)
    public interface IFixedUpdateSystem : ISystem
    {
        public void OnFixedUpdate(float inFixedDeltaTime);
    }
}


