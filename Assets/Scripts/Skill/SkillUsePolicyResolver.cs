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
            if (skill.Table == null || hasPlayerInput == false)
            {
                return GE.SkillAimTrackingMode.SnapshotOnCommand;
            }

            if (skill.Table.AimTrackingMode != GE.SkillAimTrackingMode.Default)
            {
                return skill.Table.AimTrackingMode;
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

            if (skill.Table.SkillType == GE.SkillType.Jump || (skill.Table.Tags & GE.SkillTag.Move) != 0)
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
                    return 0f;

                default:
                    return 1f;
            }
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
