using UnityEngine;

namespace ARPG.Skill.Telegraph
{
    public enum SkillImpactKind : byte
    {
        None,
        DirectImpact,
        ProjectileRelease,
        AreaActivation,
        ChannelImpact,
        LandingImpact,
        DashLaunch,     // 돌진 출발(실행 단계 첫 틱). 피해는 돌진 중 경로 판정이 담당 (SKILL_DESIGN §6.11)
        EffectActivation, // 직접 타격·발사체·장판 없이 커밋 효과만 실행하는 1회 커밋 (몬스터 소환, SKILL_DESIGN §6.12)
    }

    public enum SkillImpactExecutionMode : byte
    {
        Single,
        MultiHit,
        Channeling,
    }

    public readonly struct SkillImpactTimingInput
    {
        public readonly SkillImpactExecutionMode ExecutionMode;
        public readonly float StartDuration;
        public readonly float ProcessDuration;
        public readonly float DamageTime;
        public readonly float HitInterval;
        public readonly float ChannelingInterval;
        public readonly int HitCount;
        public readonly bool HasDirectImpact;
        public readonly bool HasProjectile;
        public readonly bool HasAreaEffect;
        public readonly bool UsesLandingCommit;
        public readonly bool UsesDash;
        public readonly bool HasCommitEffect;   // OnSkillCommit 효과가 있는지 (현재: 몬스터 소환). 다른 전달이 없을 때만 커밋을 만든다

        public SkillImpactTimingInput(
            SkillImpactExecutionMode executionMode,
            float startDuration,
            float processDuration,
            float damageTime,
            float hitInterval,
            float channelingInterval,
            int hitCount,
            bool hasDirectImpact,
            bool hasProjectile,
            bool hasAreaEffect,
            bool usesLandingCommit,
            bool usesDash = false,
            bool hasCommitEffect = false)
        {
            ExecutionMode = executionMode;
            StartDuration = startDuration;
            ProcessDuration = processDuration;
            DamageTime = damageTime;
            HitInterval = hitInterval;
            ChannelingInterval = channelingInterval;
            HitCount = hitCount;
            HasDirectImpact = hasDirectImpact;
            HasProjectile = hasProjectile;
            HasAreaEffect = hasAreaEffect;
            UsesLandingCommit = usesLandingCommit;
            UsesDash = usesDash;
            HasCommitEffect = hasCommitEffect;
        }
    }

    public readonly struct SkillImpactSchedule
    {
        public readonly SkillImpactKind Kind;
        public readonly float StartDuration;
        public readonly float FirstProcessCommitOffset;
        public readonly float FirstCommitOffset;
        public readonly float RepeatInterval;
        public readonly int CommitCount;

        public bool HasCommit => Kind != SkillImpactKind.None;
        public bool IsUnbounded => CommitCount < 0;

        public SkillImpactSchedule(
            SkillImpactKind kind,
            float startDuration,
            float firstProcessCommitOffset,
            float repeatInterval,
            int commitCount)
        {
            Kind = kind;
            StartDuration = Mathf.Max(0f, startDuration);
            FirstProcessCommitOffset = Mathf.Max(0f, firstProcessCommitOffset);
            FirstCommitOffset = StartDuration + FirstProcessCommitOffset;
            RepeatInterval = Mathf.Max(0f, repeatInterval);
            CommitCount = commitCount;
        }

        public float GetProcessCommitOffset(int commitIndex)
        {
            int safeIndex = Mathf.Max(0, commitIndex);
            return FirstProcessCommitOffset + safeIndex * RepeatInterval;
        }

        public float GetCommitOffset(int commitIndex)
        {
            return StartDuration + GetProcessCommitOffset(commitIndex);
        }

        public bool HasCommitAt(int commitIndex)
        {
            if (HasCommit == false || commitIndex < 0)
                return false;

            return IsUnbounded == true || commitIndex < CommitCount;
        }
    }

    /// <summary>
    /// 스킬의 실제 커밋 스케줄을 계산하는 순수 Resolver입니다.
    /// ECS, Time, 렌더 오브젝트에 접근하지 않습니다.
    /// </summary>
    public static class SkillImpactTimingResolver
    {
        public static SkillImpactSchedule Resolve(in SkillImpactTimingInput input)
        {
            float processDuration = Mathf.Max(0f, input.ProcessDuration);
            float damageOffset = Mathf.Clamp01(input.DamageTime) * processDuration;
            int hitCount = Mathf.Max(1, input.HitCount);

            // 돌진은 실행 단계 첫 틱에 출발하는 1회 커밋이다. 돌진 중 경로 판정은 이 스케줄을 쓰지 않는다
            if (input.UsesDash == true && input.HasDirectImpact == true)
            {
                return new SkillImpactSchedule(
                    SkillImpactKind.DashLaunch,
                    input.StartDuration,
                    0f,
                    0f,
                    1);
            }

            if (input.UsesLandingCommit == true && input.HasDirectImpact == true)
            {
                return new SkillImpactSchedule(
                    SkillImpactKind.LandingImpact,
                    input.StartDuration,
                    processDuration,
                    0f,
                    1);
            }

            SkillImpactKind kind;
            if (input.HasProjectile == true)
            {
                kind = SkillImpactKind.ProjectileRelease;
            }
            else if (input.ExecutionMode == SkillImpactExecutionMode.Channeling && input.HasDirectImpact == true)
            {
                kind = SkillImpactKind.ChannelImpact;
            }
            else if (input.HasDirectImpact == true)
            {
                kind = SkillImpactKind.DirectImpact;
            }
            else if (input.HasAreaEffect == true)
            {
                // 직접 타격이 없는 순수 장판 스킬. 장판이 깔린 뒤의 반복은 AreaEffectTable.TickInterval이
                // 담당하므로 스킬 쪽 커밋은 장판을 만드는 1회로 끝낸다(실행 타입과 무관).
                return new SkillImpactSchedule(
                    SkillImpactKind.AreaActivation,
                    input.StartDuration,
                    damageOffset,
                    0f,
                    1);
            }
            else if (input.HasCommitEffect == true)
            {
                // 피해 없이 효과만 있는 스킬(몬스터 소환). DamageTime 지점의 1회 커밋에서 OnSkillCommit 효과만 실행한다
                return new SkillImpactSchedule(
                    SkillImpactKind.EffectActivation,
                    input.StartDuration,
                    damageOffset,
                    0f,
                    1);
            }
            else
            {
                return default;
            }

            if (input.ExecutionMode == SkillImpactExecutionMode.Channeling)
            {
                return new SkillImpactSchedule(
                    kind,
                    input.StartDuration,
                    damageOffset,
                    input.ChannelingInterval,
                    -1);
            }

            int commitCount = input.ExecutionMode == SkillImpactExecutionMode.MultiHit ? hitCount : 1;
            return new SkillImpactSchedule(
                kind,
                input.StartDuration,
                damageOffset,
                input.HitInterval,
                commitCount);
        }

        /// <summary>
        /// 현행 상태 머신의 Start와 Process가 각각 최소 한 FixedUpdate를 소비하는 계약까지 포함해
        /// 명령 수락 시점부터 지정 커밋까지의 실제 고정 틱 오프셋을 계산합니다.
        /// </summary>
        public static float GetQuantizedCommitOffset(
            in SkillImpactSchedule schedule,
            int commitIndex,
            float fixedDeltaTime)
        {
            float step = Mathf.Max(0.0001f, fixedDeltaTime);
            int startTicks = Mathf.Max(1, Mathf.CeilToInt(schedule.StartDuration / step));
            int requestedProcessTicks = Mathf.CeilToInt(schedule.GetProcessCommitOffset(commitIndex) / step);
            int processTicks = Mathf.Max(commitIndex + 1, Mathf.Max(1, requestedProcessTicks));
            return (startTicks + processTicks) * step;
        }

        /// <summary>
        /// 이미 Process 상태인 시점에서 다음 커밋까지 남은 시간을 고정 틱으로 양자화합니다.
        /// 한 FixedUpdate에 커밋 하나만 실행하는 상태 머신 계약을 보존합니다.
        /// </summary>
        public static float GetQuantizedRemainingDelay(
            in SkillImpactSchedule schedule,
            int commitIndex,
            float currentProcessElapsed,
            float fixedDeltaTime)
        {
            float step = Mathf.Max(0.0001f, fixedDeltaTime);
            float remaining = schedule.GetProcessCommitOffset(commitIndex) - Mathf.Max(0f, currentProcessElapsed);
            int remainingTicks = Mathf.Max(1, Mathf.CeilToInt(remaining / step));
            return remainingTicks * step;
        }
    }
}
