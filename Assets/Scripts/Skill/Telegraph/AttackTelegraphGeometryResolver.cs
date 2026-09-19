using UnityEngine;

namespace ARPG.Skill.Telegraph
{
    public enum AttackTelegraphShape : byte
    {
        None,
        OwnerCircle,
        OwnerSector,
        TargetCircle,
        ProjectileLane,
        ProjectileFan,
        LandingCircle,
    }

    public enum AttackTelegraphOriginMode : byte
    {
        FollowOwner,
        SnapshotOwner,
        TargetPosition,
        LandingPosition,
    }

    public readonly struct AttackTelegraphGeometryInput
    {
        public readonly bool IsDirectionTarget;
        public readonly bool IsPositionTarget;
        public readonly bool HasDirectImpact;
        public readonly bool HasAreaEffect;
        public readonly bool HasProjectile;
        public readonly bool UsesLandingCommit;
        public readonly float SkillTargetRange;
        public readonly float SkillTargetHalfAngleDeg;
        public readonly float OwnerHitRadius;
        public readonly float PlayerHitRadius;
        public readonly float AreaEffectRadius;
        public readonly float ProjectileSpeed;
        public readonly float ProjectileLifetime;
        public readonly float ProjectileHitRadius;
        public readonly int ProjectileCount;
        public readonly float ProjectileSpreadAngleDeg;

        public AttackTelegraphGeometryInput(
            bool isDirectionTarget,
            bool isPositionTarget,
            bool hasDirectImpact,
            bool hasAreaEffect,
            bool hasProjectile,
            bool usesLandingCommit,
            float skillTargetRange,
            float skillTargetHalfAngleDeg,
            float ownerHitRadius,
            float playerHitRadius,
            float areaEffectRadius,
            float projectileSpeed,
            float projectileLifetime,
            float projectileHitRadius,
            int projectileCount,
            float projectileSpreadAngleDeg)
        {
            IsDirectionTarget = isDirectionTarget;
            IsPositionTarget = isPositionTarget;
            HasDirectImpact = hasDirectImpact;
            HasAreaEffect = hasAreaEffect;
            HasProjectile = hasProjectile;
            UsesLandingCommit = usesLandingCommit;
            SkillTargetRange = skillTargetRange;
            SkillTargetHalfAngleDeg = skillTargetHalfAngleDeg;
            OwnerHitRadius = ownerHitRadius;
            PlayerHitRadius = playerHitRadius;
            AreaEffectRadius = areaEffectRadius;
            ProjectileSpeed = projectileSpeed;
            ProjectileLifetime = projectileLifetime;
            ProjectileHitRadius = projectileHitRadius;
            ProjectileCount = projectileCount;
            ProjectileSpreadAngleDeg = projectileSpreadAngleDeg;
        }
    }

    public readonly struct AttackTelegraphGeometry
    {
        public readonly AttackTelegraphShape Shape;
        public readonly AttackTelegraphOriginMode OriginMode;
        public readonly float Range;
        public readonly float HalfAngleDeg;
        public readonly float Width;
        public readonly int ProjectileCount;
        public readonly float ProjectileSpreadAngleDeg;

        public AttackTelegraphGeometry(
            AttackTelegraphShape shape,
            AttackTelegraphOriginMode originMode,
            float range,
            float halfAngleDeg,
            float width,
            int projectileCount,
            float projectileSpreadAngleDeg)
        {
            Shape = shape;
            OriginMode = originMode;
            Range = Mathf.Max(0f, range);
            HalfAngleDeg = Mathf.Clamp(halfAngleDeg, 0f, 180f);
            Width = Mathf.Max(0f, width);
            ProjectileCount = Mathf.Max(1, projectileCount);
            ProjectileSpreadAngleDeg = Mathf.Max(0f, projectileSpreadAngleDeg);
        }
    }

    /// <summary>
    /// 실제 Collider와 테이블 수치에서 플레이어 중심 기준 위험 도형을 만드는 순수 Resolver입니다.
    /// </summary>
    public static class AttackTelegraphGeometryResolver
    {
        public const float DefaultProjectileSpreadAngleDeg = 15f;
        public const float DirectionEpsilonSqr = 0.0001f;

        public static AttackTelegraphGeometry Resolve(in AttackTelegraphGeometryInput input)
        {
            int projectileCount = Mathf.Max(1, input.ProjectileCount);
            float spreadAngle = Mathf.Max(0f, input.ProjectileSpreadAngleDeg);

            // 장판은 예고 대상이 아니다. 커밋 시점에 즉시 생성되므로 장판 자체의 비주얼이 위험을 알리고,
            // 예고를 또 그리면 같은 위험을 두 번 표시하게 된다. 그래서 여기서 HasAreaEffect를 보지 않고
            // 도형은 직접 타격 기준으로만 결정한다. 장판+직접타격 스킬은 직접 타격 범위만 예고된다.
            //
            // HasAreaEffect / IsPositionTarget / AreaEffectRadius 입력과 TargetCircle / TargetPosition은
            // "준비 시간이 있는 장판"(생성까지 대기가 필요한 장판) 예고용으로 예약해 둔 미사용 상태다.
            // 자세한 내용은 MONSTER_ATTACK_TELEGRAPH_DESIGN.md §5.3 참조.

            if (input.HasProjectile == true)
            {
                AttackTelegraphShape shape = projectileCount > 1
                    ? AttackTelegraphShape.ProjectileFan
                    : AttackTelegraphShape.ProjectileLane;
                float pathLength = Mathf.Max(0f, input.ProjectileSpeed) * Mathf.Max(0f, input.ProjectileLifetime);
                float fullWidth = 2f * (Mathf.Max(0f, input.ProjectileHitRadius) + Mathf.Max(0f, input.PlayerHitRadius));
                float halfSpread = (projectileCount - 1) * spreadAngle * 0.5f;
                return new AttackTelegraphGeometry(
                    shape,
                    AttackTelegraphOriginMode.FollowOwner,
                    pathLength,
                    halfSpread,
                    fullWidth,
                    projectileCount,
                    spreadAngle);
            }

            if (input.HasDirectImpact == false)
                return default;

            float directRange = Mathf.Max(0f, input.OwnerHitRadius)
                + Mathf.Max(0f, input.SkillTargetRange)
                + Mathf.Max(0f, input.PlayerHitRadius);

            if (input.UsesLandingCommit == true)
            {
                return new AttackTelegraphGeometry(
                    AttackTelegraphShape.LandingCircle,
                    AttackTelegraphOriginMode.LandingPosition,
                    directRange,
                    180f,
                    0f,
                    1,
                    0f);
            }

            if (input.IsDirectionTarget == false)
                return default;

            float halfAngle = Mathf.Clamp(input.SkillTargetHalfAngleDeg, 0f, 180f);
            AttackTelegraphShape directShape = halfAngle >= 180f
                ? AttackTelegraphShape.OwnerCircle
                : AttackTelegraphShape.OwnerSector;
            return new AttackTelegraphGeometry(
                directShape,
                AttackTelegraphOriginMode.FollowOwner,
                directRange,
                halfAngle,
                0f,
                1,
                0f);
        }

        public static int ResolveProjectileCount(int baseCount, int ownerAdditionalCount, int skillAdditionalCount)
        {
            return Mathf.Max(1, Mathf.Max(1, baseCount) + ownerAdditionalCount + skillAdditionalCount);
        }

        public static Vector2 NormalizeOrFallback(Vector2 direction, Vector2 fallbackDirection)
        {
            if (direction.sqrMagnitude >= DirectionEpsilonSqr)
                return direction.normalized;

            if (fallbackDirection.sqrMagnitude >= DirectionEpsilonSqr)
                return fallbackDirection.normalized;

            return Vector2.right;
        }
    }
}
