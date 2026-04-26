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
    /// </summary>
    public static class VillageTileFinder
    {
        // 후보 수집 버퍼 (allocation-free 재사용)
        private static readonly List<Vector2Int> _bucket = new(32);

        // Phase C: 큰길 예약 옵션 (호출자가 Stage 기반으로 전달)
        // 0이면 비활성. >0이면 중심에서 그 반경까지 N/E/S/W 축선 1칸을 빈 칸 후보에서 제외
        private static int _roadReserveRadius = 0;

        /// <summary>
        /// 호출자가 Stage 기반으로 이 값을 세팅 후 FindEmptyTileNearest 호출.
        /// Settlement=0 (좁아서 비활성), Hamlet=4, Village=6, Town+=8 권장.
        /// </summary>
        public static void SetRoadReserveRadius(int radius)
        {
            _roadReserveRadius = radius;
        }

        /// <summary>
        /// 중심에서 가장 가까운 빈 Walkable 타일을 반환.
        /// 같은 거리 후보 중 인접 점유 셀 수가 가장 적은 것을 우선 (분산 + 간격).
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            if (IsEmpty(center, center))
                return center;

            for (int r = 1; r <= maxRadius; r++)
            {
                _bucket.Clear();
                CollectRing(center, r, _bucket);
                if (_bucket.Count == 0) continue;

                // 인접 점유 페널티 최소 후보 선택 + 동률시 랜덤
                Vector2Int best = _bucket[0];
                int bestPenalty = CountOccupiedNeighbors(best);
                for (int i = 1; i < _bucket.Count; i++)
                {
                    int p = CountOccupiedNeighbors(_bucket[i]);
                    if (p < bestPenalty || (p == bestPenalty && Random.value < 0.5f))
                    {
                        best = _bucket[i];
                        bestPenalty = p;
                    }
                }
                return best;
            }
            return null;
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
