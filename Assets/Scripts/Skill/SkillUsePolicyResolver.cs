#nullable enable
using ARPG.Component;
using ARPG.Tables;
using GE = GlobalEnum;

namespace ARPG.Skill
{
    /// <summary>
    /// 테이블의 Default 값을 시전자와 스킬 특성에 맞는 런타임 정책으로 해석합니다.
    /// </summary>
    public static class SkillUsePolicyResolver
    {
        public const float DefaultAimTurnSpeedDegPerSec = 720f;
        public const float DefaultMoveSpeedWhileUsingMultiplier = 0.3f;

        public static GE.SkillAimTrackingMode ResolveAimTrackingMode(SkillComponent skill, bool hasPlayerInput)
        {
            if (skill.Table == null)
            {
                return GE.SkillAimTrackingMode.SnapshotOnCommand;
            }

            // 명시값은 시전자 종류와 무관하게 적용한다. AI도 표에 적힌 방식으로 대상을 추적한다
            // (POE2_CURSOR_TRACKING_PLAN §15, 2026-10-13). 빈 칸인 AI 스킬만 명령 시점에 고정한다.
            if (skill.Table.AimTrackingMode != GE.SkillAimTrackingMode.Default)
            {
                return skill.Table.AimTrackingMode;
            }

            if (hasPlayerInput == false)
            {
                return GE.SkillAimTrackingMode.SnapshotOnCommand;
            }

            if (RequiresDirectionalAim(skill.Table) == false)
            {
                return GE.SkillAimTrackingMode.SnapshotOnCommand;
            }

            if (skill.ExecutionType == SkillExecutionType.Channeling)
            {
                return GE.SkillAimTrackingMode.TrackContinuously;
            }

            return GE.SkillAimTrackingMode.TrackUntilCommit;
        }

        public static GE.SkillMovementMode ResolveMovementMode(SkillComponent skill, bool hasPlayerInput)
        {
            if (skill.Table == null)
            {
                return GE.SkillMovementMode.FullSpeed;
            }

            if (skill.Table.MovementMode != GE.SkillMovementMode.Default)
            {
                return skill.Table.MovementMode;
            }

            if (hasPlayerInput == false)
            {
                return GE.SkillMovementMode.FullSpeed;
            }

            if (skill.Table.SkillType == GE.SkillType.Jump
                || skill.Table.SkillType == GE.SkillType.Dash
                || (skill.Table.Tags & GE.SkillTag.Move) != 0)
            {
                return GE.SkillMovementMode.OwnedMotion;
            }

            if (skill.ExecutionType == SkillExecutionType.Toggle || skill.Table.SkillType == GE.SkillType.Buff)
            {
                return GE.SkillMovementMode.FullSpeed;
            }

            if ((skill.Table.Tags & (GE.SkillTag.Attack | GE.SkillTag.Spell)) != 0)
            {
                return GE.SkillMovementMode.Reduced;
            }

            return GE.SkillMovementMode.FullSpeed;
        }

        public static float ResolveAimTurnSpeed(SkillTable table)
        {
            return table.AimTurnSpeedDegPerSec > 0f
                ? table.AimTurnSpeedDegPerSec
                : DefaultAimTurnSpeedDegPerSec;
        }

        public static float ResolveMovementMultiplier(SkillTable table, GE.SkillMovementMode movementMode)
        {
            switch (movementMode)
            {
                case GE.SkillMovementMode.Reduced:
                    return table.MoveSpeedWhileUsingMul > 0f
                        ? table.MoveSpeedWhileUsingMul / 100f
                        : DefaultMoveSpeedWhileUsingMultiplier;

                case GE.SkillMovementMode.Locked:
                case GE.SkillMovementMode.LockedThroughEnd:
                    return 0f;

                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 후딜(End) 동안 이동 배율을 정상 속도로 서서히 되돌리는지 여부.
        /// false면 후딜 내내 실행 단계 배율을 유지하고, 스킬이 끝나는 순간 정상 속도로 돌아간다.
        /// </summary>
        public static bool RecoversMovementDuringEnd(GE.SkillMovementMode movementMode)
        {
            return movementMode != GE.SkillMovementMode.LockedThroughEnd;
        }

        /// <summary>
        /// 조준 방향이 스킬 결과에 영향을 주는지 여부.
        /// 명시값이 없는 <see cref="GE.SkillAimTrackingMode.Default"/>의 해석 규칙과 화살표 표시 판단에만 사용한다.
        /// 테이블에 정책이 명시된 스킬을 이 값으로 차단하면 안 된다(명시값 우선).
        /// </summary>
        public static bool RequiresDirectionalAim(SkillTable table)
        {
            // 자기 중심 360도 스킬은 방향이 적중 여부를 바꾸지 않는다.
            bool isSelfCenteredFullCircle = table.SkillTargetType == GE.SkillTargetType.Direction
                && table.SkillTargetRange2 >= 360f
                && table.SkillRangeMax <= 0f
                && table.ProjectileId <= 0
                && table.AreaEffectId <= 0;
            return isSelfCenteredFullCircle == false;
        }
    }
}
