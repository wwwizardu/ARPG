using ARPG.Component;
using ARPG.Map;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 정적 충돌(지형 Blocked + 건물 footprint) 해결.
    /// 축 분리(Axis-separated) 슬라이딩: X축·Y축을 독립 검사하여 한 축이 막혀도 다른 축은 진행.
    /// 1 Unity unit = 1 tile, 원형 충돌체. 언덕 칸은 바닥 쪽 테두리를 그림만큼 줄인 상자로 검사한다.
    /// </summary>
    public static class CollisionUtil
    {
        // 스턱 상태에서 빈 칸 탐색 최대 반경 (타일)
        private const int ESCAPE_SEARCH_RADIUS = 4;

        // 경로 step 크기 = radius × 이 비율 (반경보다 작아야 터널링 방지)
        private const float STEP_RATIO = 0.9f;

        // 충돌 내부 탈출 시 부동소수 오차로 같은 침투량 이동을 허용하지 않기 위한 최소 개선량
        private const float PENETRATION_IMPROVEMENT_EPSILON = 0.0001f;

        public static Vector2 ResolveAxisSeparated(Vector2 currentPos, Vector2 intendedDelta, float radius)
        {
            // 정상 위치에서는 기존 축 분리 충돌을 그대로 사용한다.
            if (IsBlockedAt(currentPos, radius) == false)
                return ResolveFromClearPosition(currentPos, intendedDelta, radius);

            // 스폰/동적 건물 배치로 이미 충돌 내부라면 침투량이 줄어드는 축만 허용한다.
            // 단순 충돌 무시는 멀티타일 건물을 관통할 수 있으므로 사용하지 않는다.
            Vector2 result = currentPos;
            float penetration = GetStaticPenetration(result, radius);

            Vector2 testX = new Vector2(result.x + intendedDelta.x, result.y);
            float penetrationX = GetStaticPenetration(testX, radius);
            if (penetrationX <= 0f ||
                penetrationX < penetration - PENETRATION_IMPROVEMENT_EPSILON)
            {
                result.x = testX.x;
                penetration = penetrationX;
            }

            Vector2 testY = new Vector2(result.x, result.y + intendedDelta.y);
            float penetrationY = GetStaticPenetration(testY, radius);
            if (penetrationY <= 0f ||
                penetrationY < penetration - PENETRATION_IMPROVEMENT_EPSILON)
            {
                result.y = testY.y;
            }

            return result;
        }

        /// <summary>
        /// 건물 footprint AABB와 현재 활성 캐릭터의 이동 원이 겹치는지 검사한다.
        /// 건물 배치 시점에만 호출하는 저빈도 O(Collider 수) 질의다.
        /// </summary>
        public static bool IsCreatureOverlappingFootprint(int worldX, int worldY,
            int width, int height)
        {
            if (width <= 0 || height <= 0)
                return false;

            ComponentManager cm = AR.s.Component;
            SparseSet<ColliderComponent> pool = cm.GetComponentPool<ColliderComponent>();
            Vector2 min = new Vector2(worldX, worldY);
            Vector2 max = new Vector2(worldX + width, worldY + height);

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                bool isCreature = cm.HasComponent<InputComponent>(entityId) == true
                    || cm.HasComponent<NpcTag>(entityId) == true
                    || cm.HasComponent<MonsterTag>(entityId) == true;
                if (isCreature == false || cm.HasComponent<DestroyTag>(entityId) == true)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                ColliderComponent collider = pool.GetByIndex(i);
                if (CircleOverlapsAabb(transform.Position, collider.MoveRadius, min, max) == true)
                    return true;
            }

            return false;
        }

        public static bool CircleOverlapsAabb(Vector2 center, float radius,
            Vector2 min, Vector2 max)
        {
            float closestX = Mathf.Clamp(center.x, min.x, max.x);
            float closestY = Mathf.Clamp(center.y, min.y, max.y);
            float dx = center.x - closestX;
            float dy = center.y - closestY;
            return dx * dx + dy * dy < radius * radius;
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
        public static bool TryFindNearestFree(Vector2 pos, float radius, out Vector2 result,
            bool requireActiveChunk = false)
        {
            return TryFindNearestFree(AR.s.Map, pos, radius, out result, requireActiveChunk);
        }

        /// <summary>
        /// 지정한 맵 기준 TryFindNearestFree. 던전 안에서도 도는 월드 시뮬(NPC 보충)이 월드 맵을 넘긴다 —
        /// AR.s.Map은 던전 전투가 쓰므로 바꾸지 않는다 (DUNGEON_WORLD_SIM_DESIGN §4.1).
        /// </summary>
        public static bool TryFindNearestFree(MapManager map, Vector2 pos, float radius, out Vector2 result,
            bool requireActiveChunk = false)
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
                        if (requireActiveChunk == true &&
                            map.IsChunkActiveAtWorld(candidate) == false)
                            continue;
                        if (IsBlockedAt(map, candidate, radius) == false)
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
        /// 원이 걸치는 모든 타일 셀을 순회하며 원-AABB 거리 검사. 상자는 MapManager.GetBlockedTileBounds (언덕 테두리는 그림만큼 줄어듦).
        /// </summary>
        public static bool IsBlockedAt(Vector2 pos, float radius)
        {
            return IsBlockedAt(AR.s.Map, pos, radius);
        }

        /// <summary>지정한 맵 기준 IsBlockedAt (월드 시뮬 경로용, TryFindNearestFree(MapManager, ...)와 같은 이유).</summary>
        public static bool IsBlockedAt(MapManager map, Vector2 pos, float radius)
        {
            int minX = Mathf.FloorToInt(pos.x - radius);
            int maxX = Mathf.FloorToInt(pos.x + radius);
            int minY = Mathf.FloorToInt(pos.y - radius);
            int maxY = Mathf.FloorToInt(pos.y + radius);

            for (int tx = minX; tx <= maxX; tx++)
            {
                for (int ty = minY; ty <= maxY; ty++)
                {
                    if (map.IsTileBlocked(tx, ty) == false)
                        continue;

                    // 칸 전체와 먼저 비교해 겹치지 않는 칸은 이웃 조회 없이 넘긴다 (줄어든 상자는 칸 전체 안쪽)
                    if (CircleOverlapsAabb(pos, radius, new Vector2(tx, ty), new Vector2(tx + 1f, ty + 1f)) == false)
                        continue;

                    map.GetBlockedTileBounds(tx, ty, out Vector2 boundsMin, out Vector2 boundsMax);
                    if (CircleOverlapsAabb(pos, radius, boundsMin, boundsMax) == true)
                        return true;
                }
            }

            return false;
        }

        private static Vector2 ResolveFromClearPosition(Vector2 currentPos,
            Vector2 intendedDelta, float radius)
        {
            Vector2 result = currentPos;

            Vector2 testX = new Vector2(result.x + intendedDelta.x, result.y);
            if (IsBlockedAt(testX, radius) == false)
                result.x = testX.x;

            Vector2 testY = new Vector2(result.x, result.y + intendedDelta.y);
            if (IsBlockedAt(testY, radius) == false)
                result.y = testY.y;

            return result;
        }

        private static float GetStaticPenetration(Vector2 pos, float radius)
        {
            MapManager map = AR.s.Map;
            int minX = Mathf.FloorToInt(pos.x - radius);
            int maxX = Mathf.FloorToInt(pos.x + radius);
            int minY = Mathf.FloorToInt(pos.y - radius);
            int maxY = Mathf.FloorToInt(pos.y + radius);
            float maxPenetration = 0f;

            for (int tx = minX; tx <= maxX; tx++)
            {
                for (int ty = minY; ty <= maxY; ty++)
                {
                    if (map.IsTileBlocked(tx, ty) == false)
                        continue;

                    map.GetBlockedTileBounds(tx, ty, out Vector2 boundsMin, out Vector2 boundsMax);
                    float closestX = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
                    float closestY = Mathf.Clamp(pos.y, boundsMin.y, boundsMax.y);
                    float dx = pos.x - closestX;
                    float dy = pos.y - closestY;
                    float distSqr = dx * dx + dy * dy;
                    float penetration;

                    if (distSqr > 0f)
                    {
                        penetration = radius - Mathf.Sqrt(distSqr);
                    }
                    else
                    {
                        // 중심이 AABB 내부인 경우 최근접 면까지의 거리 + 반경이 탈출 침투량이다.
                        float horizontalDistance = Mathf.Min(pos.x - boundsMin.x,
                            boundsMax.x - pos.x);
                        float verticalDistance = Mathf.Min(pos.y - boundsMin.y,
                            boundsMax.y - pos.y);
                        float edgeDistance = Mathf.Min(horizontalDistance,
                            verticalDistance);
                        penetration = radius + Mathf.Max(0f, edgeDistance);
                    }

                    if (penetration > maxPenetration)
                        maxPenetration = penetration;
                }
            }

            return maxPenetration;
        }
    }
}
