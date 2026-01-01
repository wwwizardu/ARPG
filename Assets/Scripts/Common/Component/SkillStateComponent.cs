#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 실행 상태
    /// </summary>
    public enum SkillState : byte
    {
        /// <summary>실행 중이 아님</summary>
        None,
        /// <summary>시작 단계</summary>
        Start,
        /// <summary>진행 단계 (데미지 등 주요 효과 발생)</summary>
        Process,
        /// <summary>종료 단계 (후딜레이)</summary>
        End,
    }

    /// <summary>
    /// 스킬의 실행 상태를 관리하는 컴포넌트
    /// </summary>
    public struct SkillStateComponent
    {
        /// <summary>현재 스킬 상태</summary>
        public SkillState State;

        /// <summary>현재 상태에서 경과된 시간</summary>
        public float ElapsedTime;

        /// <summary>이전 상태 (디버깅/로직 처리용)</summary>
        public SkillState PreviousState;

        /// <summary>
        /// 스킬이 실행 중인지 여부
        /// </summary>
        public readonly bool IsRunning => State != SkillState.None;

        /// <summary>
        /// 상태를 변경하고 경과 시간을 초기화
        /// </summary>
        public void ChangeState(SkillState newState)
        {
            PreviousState = State;
            State = newState;
            ElapsedTime = 0f;
        }

        /// <summary>
        /// 상태를 초기화
        /// </summary>
        public void Reset()
        {
            State = SkillState.None;
            PreviousState = SkillState.None;
            ElapsedTime = 0f;
        }
    }
}
