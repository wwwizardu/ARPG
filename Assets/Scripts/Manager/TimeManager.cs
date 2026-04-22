#nullable enable
using UnityEngine;

namespace ARPG.Manager
{
    /// <summary>
    /// 게임 내 경과 시간을 추적하는 매니저
    /// 실시간 _realSecondsPerGameHour 초마다 게임 시간 1시간이 경과
    /// System_Time에서 매 프레임 Tick()을 호출
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        [SerializeField] private float _realSecondsPerGameHour = 60f; // 실시간 60초 = 게임 1시간

        private float _elapsedGameTime;     // 누적 게임 시간 (시간 단위)
        private float _accumulator;         // 실시간 누적기

        /// <summary>
        /// 누적 게임 시간 (시간 단위, 정수 부분만)
        /// </summary>
        public float ElapsedGameTime => _elapsedGameTime;

        /// <summary>
        /// 누적 게임 시간 (시간 단위, 소수점 포함). 쿨타임 비교 등 고해상도 비교용.
        /// </summary>
        public float CurrentGameTime => _elapsedGameTime + (_accumulator / _realSecondsPerGameHour);

        public void Initialize()
        {
            _elapsedGameTime = 0f;
            _accumulator = 0f;
        }

        public void Reset()
        {
            _elapsedGameTime = 0f;
            _accumulator = 0f;
        }

        public void Tick(float inDeltaTime)
        {
            _accumulator += inDeltaTime;

            if (_accumulator >= _realSecondsPerGameHour)
            {
                _elapsedGameTime += 1f;
                _accumulator -= _realSecondsPerGameHour;
            }
        }
    }
}
