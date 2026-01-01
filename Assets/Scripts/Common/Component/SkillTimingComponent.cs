#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 타이밍 데이터를 관리하는 컴포넌트
    /// 각 상태별 지속 시간을 저장
    /// </summary>
    public struct SkillTimingComponent
    {
        /// <summary>시작 단계 지속 시간 (준비 모션)</summary>
        public float StartDuration;

        /// <summary>진행 단계 지속 시간 (공격 판정까지의 시간)</summary>
        public float ProcessDuration;

        /// <summary>종료 단계 지속 시간 (후딜레이)</summary>
        public float EndDuration;

        /// <summary>
        /// 전체 스킬 실행 시간
        /// </summary>
        public readonly float TotalDuration => StartDuration + ProcessDuration + EndDuration;

        /// <summary>
        /// 공격 속도에 따라 타이밍 값들을 갱신
        /// </summary>
        /// <param name="baseAttackSpeed">기본 공격 속도</param>
        /// <param name="attackDamageTime">공격 판정 시간</param>
        /// <param name="attackSpeedMultiplier">공격 속도 배율</param>
        public void UpdateFromAttackSpeed(float baseAttackSpeed, float attackDamageTime, float attackSpeedMultiplier)
        {
            if (attackSpeedMultiplier <= 0f)
                attackSpeedMultiplier = 1f;

            ProcessDuration = attackDamageTime / attackSpeedMultiplier;
            EndDuration = baseAttackSpeed / attackSpeedMultiplier;
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
