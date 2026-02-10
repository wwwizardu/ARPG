using UnityEngine;

namespace ARPG.Systems
{
    // 기본 System 인터페이스
    public interface ISystem
    {
        public int Priority { get; }

        /// <summary>
        /// 업데이트 간격 (초 단위)
        /// 0이면 매 프레임마다 실행
        /// 0.5f면 0.5초마다 실행
        /// 1.0f면 1초마다 실행
        /// </summary>
        public float UpdateInterval => 0f;  // 기본값: 매 프레임

        /// <summary>
        /// System 등록 시 1회 호출. 초기화 로직을 여기서 수행.
        /// </summary>
        public void OnCreate()
        {
            Debug.Log("ISystem OnCreate called");
        }

        public void OnReset()
        {
            Debug.Log("ISystem OnReset called");
        }
    }

    // Update 매 프레임마다 실행 (입력, UI 등)
    public interface IUpdateSystem : ISystem
    {
        public void OnUpdate(float inDeltaTime);
    }

    // FixedUpdate 고정 타임스텝으로 실행 (물리, 게임플레이 로직)
    public interface IFixedUpdateSystem : ISystem
    {
        public void OnFixedUpdate(float inFixedDeltaTime);
    }

    // LateUpdate FixedUpdate와 Update 이후 실행 (카메라, 렌더링 동기화)
    public interface ILateUpdateSystem : ISystem
    {
        public void OnLateUpdate(float inDeltaTime);
    }
}


