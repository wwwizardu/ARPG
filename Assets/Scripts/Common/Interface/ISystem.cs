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
        /// System 등록 시 1회 호출.
        /// 주의: System은 struct이므로 이 함수 안에서 instance 필드 값을 변경하면
        /// ValueType.GetHashCode()가 달라져 SystemManager의 Dictionary에서
        /// KeyNotFoundException이 발생할 수 있다.
        /// 캐시용 컬렉션 등은 반드시 static readonly로 선언할 것.
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


