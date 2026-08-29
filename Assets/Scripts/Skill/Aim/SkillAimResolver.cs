using UnityEngine;

namespace ARPG.Skill.Aim
{
    /// <summary>
    /// Unity 오브젝트나 ECS 저장소에 의존하지 않는 스킬 조준 계산입니다.
    /// </summary>
    public static class SkillAimResolver
    {
        public const float DirectionEpsilonSqr = 0.0001f;

        public static Vector2 ResolveDirection(Vector2 origin, Vector2 target, Vector2 fallbackDirection)
        {
            Vector2 direction = target - origin;
            if (direction.sqrMagnitude >= DirectionEpsilonSqr)
            {
                return direction.normalized;
            }

            return NormalizeOrFallback(fallbackDirection, Vector2.right);
        }

        public static Vector2 NormalizeOrFallback(Vector2 direction, Vector2 fallbackDirection)
        {
            if (direction.sqrMagnitude >= DirectionEpsilonSqr)
            {
                return direction.normalized;
            }

            if (fallbackDirection.sqrMagnitude >= DirectionEpsilonSqr)
            {
                return fallbackDirection.normalized;
            }

            return Vector2.right;
        }

        public static Vector2 RotateTowards(Vector2 currentDirection, Vector2 desiredDirection, float turnSpeedDegPerSec, float deltaTime)
        {
            Vector2 current = NormalizeOrFallback(currentDirection, desiredDirection);
            Vector2 desired = NormalizeOrFallback(desiredDirection, current);
            float currentAngle = DirectionToAngle(current);
            float desiredAngle = DirectionToAngle(desired);
            float maxAngleDelta = Mathf.Max(0f, turnSpeedDegPerSec) * Mathf.Max(0f, deltaTime);
            float nextAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, maxAngleDelta);
            return AngleToDirection(nextAngle);
        }

        public static float DirectionToAngle(Vector2 direction)
        {
            Vector2 normalized = NormalizeOrFallback(direction, Vector2.right);
            return Mathf.Atan2(normalized.y, normalized.x) * Mathf.Rad2Deg;
        }

        public static Vector2 AngleToDirection(float angleDeg)
        {
            float radians = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        public static float GetDisplayAngle(Vector2 direction, float axisOffsetDeg)
        {
            return DirectionToAngle(direction) + axisOffsetDeg;
        }

        public static Vector2 ResolveTargetPosition(
            Vector2 casterPosition,
            Vector2 mouseWorldPosition,
            Vector2 committedDirection,
            float minimumRange,
            float maximumRange)
        {
            float desiredDistance = Vector2.Distance(casterPosition, mouseWorldPosition);
            float minDistance = Mathf.Max(0f, minimumRange);
            float maxDistance = maximumRange > 0f ? Mathf.Max(minDistance, maximumRange) : desiredDistance;
            float committedDistance = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
            Vector2 direction = NormalizeOrFallback(committedDirection, Vector2.right);
            return casterPosition + direction * committedDistance;
        }
    }
}
