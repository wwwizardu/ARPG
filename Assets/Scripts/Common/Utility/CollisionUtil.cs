using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 정적 충돌(지형 Blocked + 건물 footprint) 해결.
    /// 축 분리(Axis-separated) 슬라이딩: X축·Y축을 독립 검사하여 한 축이 막혀도 다른 축은 진행.
    /// 1 Unity unit = 1 tile, 원형 충돌체.
    /// </summary>
    public static class CollisionUtil
    {
        // 스턱 상태에서 빈 칸 탐색 최대 반경 (타일)
        private const int ESCAPE_SEARCH_RADIUS = 4;

        // 경로 step 크기 = radius × 이 비율 (반경보다 작아야 터널링 방지)
        private const float STEP_RATIO = 0.9f;

        public static Vector2 ResolveAxisSeparated(Vector2 currentPos, Vector2 intendedDelta, float radius)
        {
            Vector2 result = currentPos;

            // X축 단독 시도
            Vector2 testX = new Vector2(result.x + intendedDelta.x, result.y);
            if (IsBlockedAt(testX, radius) == false)
                result.x = testX.x;

            // Y축 단독 시도 (X 결과 위에 누적)
            Vector2 testY = new Vector2(result.x, result.y + intendedDelta.y);
            if (IsBlockedAt(testY, radius) == false)
                result.y = testY.y;

            return result;
        }

        /// <summary>
        /// from→to 직선 경로를 sub-step으로 검사하여 첫 막힘 직전 지점을 반환.
        /// 경로가 깨끗하면 to 그대로 반환. 시작점이 막혀있으면 from 반환.
        /// 점프 trajectory 검사용 (벽을 가로질러 반대편으로 텔레포트 방지).
        /// </summary>
        public static Vector2 ClipTrajectory(Vector2 from, Vector2 to, float radius)
        {
            Vector2 delta = to - from;
            float magnitude = delta.magnitude;
            if (magnitude < 0.0001f)
                return from;

            float stepLen = radius * STEP_RATIO;
            int steps = Mathf.CeilToInt(magnitude / stepLen);
            Vector2 stepDelta = delta / steps;

            Vector2 lastClear = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 cursor = from + stepDelta * i;
                if (IsBlockedAt(cursor, radius) == true)
                    return lastClear;
                lastClear = cursor;
            }
            return lastClear;
        }

        /// <summary>
        /// pos를 중심으로 나선형 외곽 탐색하여 첫 번째 빈 타일 중심을 반환.
        /// 점프 착지 시 freeze된 위치마저 막혀있을 때 (비행 중 건물 생성 등) 탈출 용도.
        /// </summary>
        public static bool TryFindNearestFree(Vector2 pos, float radius, out Vector2 result)
        {
            int centerX = Mathf.FloorToInt(pos.x);
            int centerY = Mathf.FloorToInt(pos.y);

            for (int r = 1; r <= ESCAPE_SEARCH_RADIUS; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        // 외곽 링만 (안쪽은 이전 r에서 검사 완료)
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                            continue;

                        int tx = centerX + dx;
                        int ty = centerY + dy;
                        Vector2 candidate = new Vector2(tx + 0.5f, ty + 0.5f);
                        if (IsBlockedAt(candidate, radius) == false)
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }
            }

            result = pos;
            return false;
        }

        /// <summary>
        /// 원이 (pos, radius)에 위치할 때 차단 타일과 겹치는지 검사.
        /// 원이 걸치는 모든 타일 셀을 순회하며 원-AABB 거리 검사.
        /// </summary>
        public static bool IsBlockedAt(Vector2 pos, float radius)
        {
            int minX = Mathf.FloorToInt(pos.x - radius);
            int maxX = Mathf.FloorToInt(pos.x + radius);
            int minY = Mathf.FloorToInt(pos.y - radius);
            int maxY = Mathf.FloorToInt(pos.y + radius);

            float radiusSqr = radius * radius;

            for (int tx = minX; tx <= maxX; tx++)
            {
                for (int ty = minY; ty <= maxY; ty++)
                {
                    if (AR.s.Map.IsTileBlocked(tx, ty) == false)
                        continue;

                    float closestX = Mathf.Clamp(pos.x, tx, tx + 1f);
                    float closestY = Mathf.Clamp(pos.y, ty, ty + 1f);
                    float dx = pos.x - closestX;
                    float dy = pos.y - closestY;
                    if (dx * dx + dy * dy < radiusSqr)
                        return true;
                }
            }

            return false;
        }
    }
}
