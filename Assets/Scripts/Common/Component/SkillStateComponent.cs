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
        Start,      // 시작(시전 시간, 준비 모션)
        Process,    // 진행(실제 스킬 효과 발생)
        End,        // 종료(후딜레이)
        Completed,  // 완료 (실제 효과는 없지만 상태를 구분하기 위채)
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

        /// <summary>Single 타입 스킬의 효과가 이미 적용되었는지 여부</summary>
        public bool IsEffectApplied;

        /// <summary>남은 쿨타임 (초). 0 이하면 사용 가능</summary>
        public float CooldownRemaining;

        /// <summary>
        /// 스킬이 실행 중인지 여부
        /// </summary>
        public readonly bool IsRunning => State != SkillState.None;

        /// <summary>
        /// 쿨타임이 끝났는지 여부
        /// </summary>
        public readonly bool IsCooldownReady => CooldownRemaining <= 0f;

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
            IsEffectApplied = false;
            CooldownRemaining = 0f;
        }
    }
}
