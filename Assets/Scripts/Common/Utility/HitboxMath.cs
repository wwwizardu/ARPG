using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 충돌/명중 판정 기하학 헬퍼.
    /// 의도적으로 ECS와 무관하게 좌표·반경·각도 입력만 받는 순수 함수만 제공한다.
    /// 컴포넌트 조회는 호출 측의 책임 — 이 헬퍼는 산술만 담당.
    /// </summary>
    public static class HitboxMath
    {
        /// <summary>
        /// 두 원의 교차 판정. 반경 0이면 점 vs 원으로 동작.
        /// </summary>
        public static bool CircleVsCircle(Vector2 a, float ra, Vector2 b, float rb)
        {
            float threshold = ra + rb;
            return (a - b).sqrMagnitude <= threshold * threshold;
        }

        /// <summary>
        /// 부채꼴 안에 점이 있는지 판정. halfAngleDeg는 forward 기준 한쪽 각도(전체 각의 절반).
        /// halfAngleDeg가 180 이상이면 각도 검사 생략(거리만 검사).
        /// </summary>
        public static bool PointInSector(Vector2 point, Vector2 origin, Vector2 forward, float range, float halfAngleDeg)
        {
            Vector2 delta = point - origin;
            float sqrDist = delta.sqrMagnitude;
            if (sqrDist > range * range)
                return false;

            if (halfAngleDeg >= 180f)
                return true;

            float sqrLen = delta.sqrMagnitude;
            if (sqrLen <= 0.0001f)
                return true;

            Vector2 dir = delta / Mathf.Sqrt(sqrLen);
            Vector2 fwd = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.right;

            float dot = Vector2.Dot(fwd, dir);
            float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
            return dot >= cosHalf;
        }

        /// <summary>
        /// 원이 부채꼴과 교차하는지 판정 (간이). 중심점이 부채꼴 안에 있거나,
        /// 또는 거리 차이가 반경 이내면 명중으로 본다.
        /// </summary>
        public static bool CircleVsSector(Vector2 c, float r, Vector2 origin, Vector2 forward, float range, float halfAngleDeg)
        {
            // 중심이 부채꼴 안 → 명중
            if (PointInSector(c, origin, forward, range + r, halfAngleDeg))
                return true;

            // 중심이 밖이지만 가장자리가 닿는 경우는 위 range+r 확장으로 근사 처리
            return false;
        }

        /// <summary>
        /// 비교용 제곱 거리 — sqrt 회피.
        /// </summary>
        public static float SqrDistance(Vector2 a, Vector2 b)
        {
            return (a - b).sqrMagnitude;
        }
    }
}
