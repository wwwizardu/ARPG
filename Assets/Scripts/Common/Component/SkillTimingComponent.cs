#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 타이밍 데이터를 관리하는 컴포넌트
    /// Base: 테이블에서 설정한 원본 값
    /// Start/Process/End Duration: 속도 배율이 적용된 실제 사용 값
    /// </summary>
    public struct SkillTimingComponent
    {
        /// <summary>원본 시작 단계 지속 시간</summary>
        public float BaseStartDuration;

        /// <summary>원본 진행 단계 지속 시간</summary>
        public float BaseProcessDuration;

        /// <summary>원본 종료 단계 지속 시간</summary>
        public float BaseEndDuration;

        /// <summary>시작 단계 지속 시간 (속도 배율 적용)</summary>
        public float StartDuration;

        /// <summary>진행 단계 지속 시간 (속도 배율 적용)</summary>
        public float ProcessDuration;

        /// <summary>종료 단계 지속 시간 (속도 배율 적용)</summary>
        public float EndDuration;

        /// <summary>
        /// 전체 스킬 실행 시간
        /// </summary>
        public readonly float TotalDuration => StartDuration + ProcessDuration + EndDuration;

        /// <summary>
        /// 속도 배율 적용 (스킬 시작 시 호출)
        /// Base Duration을 기준으로 배율을 나눠 실제 Duration 설정
        /// </summary>
        /// <param name="speedMultiplier">속도 배율 (1.0 = 기본, 2.0 = 2배속)</param>
        public void ApplySpeedMultiplier(float speedMultiplier)
        {
            // 상한 없음 — 속도를 아무리 쌓아도 상태 머신이 Start/Process/End에 최소 1틱씩 쓰므로
            // 실제 시전 시간은 FixedUpdate 3틱 밑으로 내려가지 않는다.
            // 하한만 유지: 0 이하가 들어오면 duration이 무한대 또는 음수가 된다.
            if (speedMultiplier <= 0.1f)
                speedMultiplier = 0.1f;

            StartDuration = BaseStartDuration / speedMultiplier;
            ProcessDuration = BaseProcessDuration / speedMultiplier;
            EndDuration = BaseEndDuration / speedMultiplier;
        }

        /// <summary>
        /// 타이밍 초기화
        /// </summary>
        public void Reset()
        {
            StartDuration = 0f;
            ProcessDuration = 0f;
            EndDuration = 0f;
        }
    }
}
