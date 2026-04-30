#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 중심 주변의 빈 타일을 링 확장 BFS(체비쇼프 거리)로 찾는 유틸리티.
    /// System_VillageBuildQueue가 오브젝트 배치 위치 결정에 사용.
    ///
    /// 정책 진화:
    ///  - Phase B: 가장 가까운 비어있지 않은 링 내 랜덤 픽 — 시각 클러스터링 방지
    ///  - Phase C: + 인접 점유 페널티 (8방위 점유 셀 카운트, 낮을수록 우선) — 건물 사이 1칸 간격 유도
    ///  - Phase C: + "큰길" 예약 (중심에서 N/S/E/W 통로 1칸 보존) — NPC 통행 확보
    ///  - Phase D: + 외곽 마진 (비-Defense 카테고리는 boundsRadius - margin 안쪽만) — Defense는 외곽 띠 점유
    ///  - Phase D: + 카테고리 클러스터 가산점 (같은 카테고리 인접 셀 +1, 다른 카테고리 -0.5) — "공방 거리" 자연 발생
    /// </summary>
    public static class VillageTileFinder
    {
        // 후보 수집 버퍼 (allocation-free 재사용)
        private static readonly List<Vector2Int> _bucket = new(32);

        // Phase C: 큰길 예약 옵션 (호출자가 Stage 기반으로 전달)
        // 0이면 비활성. >0이면 중심에서 그 반경까지 N/E/S/W 축선 1칸을 빈 칸 후보에서 제외
        private static int _roadReserveRadius = 0;

        // Phase D: 외곽 보호 마진 (비-Defense 카테고리는 경계로부터 ≥ 2타일 안쪽만)
        public const int OUTSKIRT_MARGIN_TILES = 2;

        /// <summary>
        /// 호출자가 Stage 기반으로 이 값을 세팅 후 FindEmptyTileNearest 호출.
        /// Settlement=0 (좁아서 비활성), Hamlet=4, Village=6, Town+=8 권장.
        /// </summary>
        public static void SetRoadReserveRadius(int radius)
        {
            _roadReserveRadius = radius;
        }

        /// <summary>
        /// Phase B 호환 오버로드: 카테고리/villageId 미지정 시 외곽 마진·클러스터 가산점 비활성.
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            return FindEmptyTileNearest(center, maxRadius, -1, BuildableCategory.None, 0);
        }

        /// <summary>
        /// Phase D: 외곽 마진 + 카테고리 클러스터링 적용.
        /// boundsRadius=0이면 외곽 마진 비활성 (Phase B 호환).
        /// villageId=-1이면 클러스터 가산점 비활성.
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(
            Vector2Int center, int maxRadius,
            int villageId, BuildableCategory category, int boundsRadius)
        {
            if (IsEmpty(center, center) && IsValidPlacement(center, center, boundsRadius, category))
                return center;

            for (int r = 1; r <= maxRadius; r++)
            {
                _bucket.Clear();
                CollectRing(center, r, _bucket);
                if (_bucket.Count == 0) continue;

                // 외곽 마진 하드 필터 (비-Defense는 경계 안쪽만)
                if (boundsRadius > 0)
                {
                    for (int i = _bucket.Count - 1; i >= 0; i--)
                    {
                        if (IsValidPlacement(_bucket[i], center, boundsRadius, category) == false)
                            _bucket.RemoveAt(i);
                    }
                    if (_bucket.Count == 0) continue;
                }

                // 후보 점수 = -점유이웃 + 카테고리클러스터(같은 +1 / 다른 -0.5)
                Vector2Int best = _bucket[0];
                float bestScore = ScoreCandidate(best, villageId, category);
                for (int i = 1; i < _bucket.Count; i++)
                {
                    float s = ScoreCandidate(_bucket[i], villageId, category);
                    if (s > bestScore || (Mathf.Approximately(s, bestScore) && Random.value < 0.5f))
                    {
                        best = _bucket[i];
                        bestScore = s;
                    }
                }
                return best;
            }
            return null;
        }

        // ========== Phase D: 외곽 마진 ==========

        /// <summary>
        /// 비-Defense 카테고리는 경계로부터 ≥ OUTSKIRT_MARGIN_TILES 안쪽만 허용.
        /// Defense(벽/게이트)는 경계 위에만 (Phase D MVP에서 일반 BuildQueue는 Defense를 다루지 않음 — WallPlanner 전담).
        /// boundsRadius == 0이면 마진 비활성 (Phase B 호환).
        /// </summary>
        private static bool IsValidPlacement(Vector2Int tile, Vector2Int center, int boundsRadius, BuildableCategory category)
        {
            if (boundsRadius <= 0) return true;
            int distFromCenter = Mathf.Max(Mathf.Abs(tile.x - center.x), Mathf.Abs(tile.y - center.y));
            int distFromBoundary = boundsRadius - distFromCenter;
            if (category == BuildableCategory.Defense)
                return distFromBoundary <= 0;       // 경계 위
            return distFromBoundary >= OUTSKIRT_MARGIN_TILES;  // 안쪽 ≥ 2타일
        }

        // ========== Phase D: 카테고리 클러스터링 + 점유 페널티 통합 점수 ==========

        private static float ScoreCandidate(Vector2Int tile, int villageId, BuildableCategory category)
        {
            float score = -CountOccupiedNeighbors(tile);

            if (villageId >= 0 && category != BuildableCategory.None)
                score += CategoryClusterBonus(tile, villageId, category);

            return score;
        }

        /// <summary>
        /// 같은 카테고리 인접 +1, 다른 카테고리 인접 -0.5 (8방위).
        /// "공방 거리"·"주거 구역" 같은 자연 발생 클러스터링 유도.
        /// </summary>
        private static float CategoryClusterBonus(Vector2Int tile, int villageId, BuildableCategory category)
        {
            float bonus = 0f;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int n = new Vector2Int(tile.x + dx, tile.y + dy);
                    int neighborEntity = PlacedObjectRegistry.GetEntityAtTile(villageId, n);
                    if (neighborEntity < 0) continue;
                    if (AR.s.Component.TryGetComponent<ARPG.Component.PlacedObjectComponent>(neighborEntity, out var po) == false) continue;

                    Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(po.TableId);
                    if (t == null) continue;
                    BuildableCategory neighborCat = (BuildableCategory)t.Category;
                    if (neighborCat == category) bonus += 1f;
                    else if (neighborCat != BuildableCategory.None) bonus -= 0.5f;
                }
            }
            return bonus;
        }

        /// <summary>링 r(체비쇼프 거리)의 가장자리 빈 타일을 모두 모은다. 큰길 예약 셀 제외.</summary>
        private static void CollectRing(Vector2Int center, int r, List<Vector2Int> output)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int ax = Mathf.Abs(dx);
                    int ay = Mathf.Abs(dy);
                    // 링 가장자리만 (내부는 이전 반복에서 검사됨)
                    if (ax != r && ay != r)
                        continue;
                    Vector2Int candidate = new Vector2Int(center.x + dx, center.y + dy);
                    if (IsEmpty(candidate, center))
                        output.Add(candidate);
                }
            }
        }

        /// <summary>
        /// 빈 타일 판정. center 인자는 큰길 예약 체크용.
        /// </summary>
        public static bool IsEmpty(Vector2Int tile, Vector2Int center)
        {
            // 큰길 예약 셀이면 즉시 제외
            if (_roadReserveRadius > 0 && IsReservedRoad(tile, center, _roadReserveRadius))
                return false;

            // 타일 중심 월드 좌표로 변환 (Tilemap 좌표 기준 +0.5 보정)
            Vector3 world = new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0f);
            if (AR.s.Map.IsWalkable(world) == false)
                return false;
            // Tile 타입 오브젝트는 타일 비트에 기록됨
            if (AR.s.Map.GetObjectIdAt(tile.x, tile.y) != 0)
                return false;
            // Entity 타입 건물은 BuildingManager의 점유 타일 집합에 기록됨
            if (AR.s.Building != null && AR.s.Building.IsTileOccupied(tile.x, tile.y))
                return false;
            return true;
        }

        /// <summary>
        /// 마을 중심에서 N/S/E/W 4방위로 폭 1타일 통로를 예약.
        /// 축선 위(다른 축 == 0) + 반경 내 → 큰길로 간주.
        /// </summary>
        private static bool IsReservedRoad(Vector2Int tile, Vector2Int center, int radius)
        {
            int dx = tile.x - center.x;
            int dy = tile.y - center.y;
            return (dx == 0 && Mathf.Abs(dy) <= radius)
                || (dy == 0 && Mathf.Abs(dx) <= radius);
        }

        /// <summary>
        /// 8방위 인접 셀 중 빈 칸이 아닌 셀(=점유) 카운트.
        /// 큰길 예약 영향 없음 — 순수히 점유 여부만 본다.
        /// </summary>
        private static int CountOccupiedNeighbors(Vector2Int tile)
        {
            int count = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int n = new Vector2Int(tile.x + dx, tile.y + dy);
                    Vector3 w = new Vector3(n.x + 0.5f, n.y + 0.5f, 0f);
                    bool occupied =
                        AR.s.Map.IsWalkable(w) == false
                        || AR.s.Map.GetObjectIdAt(n.x, n.y) != 0
                        || (AR.s.Building != null && AR.s.Building.IsTileOccupied(n.x, n.y));
                    if (occupied) count++;
                }
            }
            return count;
        }
    }
}
