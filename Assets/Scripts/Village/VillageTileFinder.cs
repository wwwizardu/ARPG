#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 중심 주변의 빈 타일을 찾는 유틸리티.
    /// System_VillageBuildQueue가 오브젝트 배치 위치 결정에 사용.
    ///
    /// 정책 진화:
    ///  - Phase B: 가장 가까운 비어있지 않은 링 내 랜덤 픽 — 시각 클러스터링 방지
    ///  - Phase C: + 인접 점유 페널티 (8방위) + 큰길 예약 (축선 1타일)
    ///  - Phase D: + 외곽 마진 + 카테고리 클러스터 가산점 (8방위)
    ///  - Phase E: A1 최소 간격 하드 필터, A2 전역 후보 누적 → 최고점, A3 점유 페널티 체비쇼프 2 확장,
    ///             A4 광장 예약, B1 테이블 기반 MinSeparation 오버라이드, B2 거리 차등 클러스터 보너스,
    ///             B3 큰길 폭 옵션 (Hamlet+)
    ///
    /// 호출 패턴: BuildQueue가 매 호출 전에 SetRoadReserve / SetPlazaRadius로 Stage별 옵션을 세팅한 뒤
    /// FindEmptyTileNearest를 호출. villageId/category/boundsRadius는 인자로 전달.
    /// </summary>
    public static class VillageTileFinder
    {
        // 후보 수집 버퍼 (allocation-free 재사용)
        private static readonly List<Vector2Int> _bucket = new(128);

        // 큰길 예약: 축선 길이(중심에서 N/E/S/W 반경) + 축선 폭(±halfWidth)
        // 0이면 비활성. >0이면 해당 셀을 빈 칸 후보에서 제외.
        private static int _roadReserveRadius = 0;
        private static int _roadHalfWidth = 0;

        // A4: 광장 예약 (마을 중심 box). 0=비활성, 1=3x3, 2=5x5
        private static int _plazaRadius = 0;

        // 외곽 보호 마진 (비-Defense 카테고리는 경계로부터 ≥ 2타일 안쪽만)
        public const int OUTSKIRT_MARGIN_TILES = 2;

        /// <summary>
        /// Stage 기반 큰길 예약 옵션. radius=축선 길이, halfWidth=축선 폭(0=1타일, 1=3타일).
        /// </summary>
        public static void SetRoadReserve(int radius, int halfWidth = 0)
        {
            _roadReserveRadius = radius;
            _roadHalfWidth = halfWidth;
        }

        /// <summary>Phase C 호환 진입점 — 폭 1타일.</summary>
        public static void SetRoadReserveRadius(int radius) => SetRoadReserve(radius, 0);

        /// <summary>A4: 마을 중심 광장 예약. 0=비활성, 1=3x3, 2=5x5.</summary>
        public static void SetPlazaRadius(int radius) => _plazaRadius = radius;

        /// <summary>
        /// Phase B 호환 오버로드: 외곽 마진·클러스터·MinSeparation 비활성.
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            return FindEmptyTileNearest(center, maxRadius, -1, BuildableCategory.None, 0, 0);
        }

        /// <summary>
        /// Phase D 호환 오버로드 (MinSeparation 미지정).
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(
            Vector2Int center, int maxRadius,
            int villageId, BuildableCategory category, int boundsRadius)
        {
            return FindEmptyTileNearest(center, maxRadius, villageId, category, boundsRadius, 0);
        }

        /// <summary>
        /// Phase E 본 진입점:
        ///  1) r=1..maxRadius 모든 빈 칸을 누적 (A2)
        ///  2) 외곽 마진 + 광장/큰길 예약 + MinSeparation 하드 필터 (A1, A4)
        ///  3) 0개면 minSep 1단계 완화 후 재시도 (테이블이 빡빡할 때 수렴)
        ///  4) ScoreCandidate로 최고점 선택 — 거리 차등 클러스터(B2) + 점유 페널티 거리 2(A3) + 중심 미세 가산
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(
            Vector2Int center, int maxRadius,
            int villageId, BuildableCategory category, int boundsRadius,
            int minSeparationOverride)
        {
            int minSep = minSeparationOverride > 0
                ? minSeparationOverride
                : GetDefaultMinSeparation(category);

            return FindWithFallback(center, maxRadius, villageId, category, boundsRadius, minSep);
        }

        /// <summary>
        /// 카테고리 기본 minSeparation. BuildableItemTable.MinSeparation > 0이면 그쪽이 우선.
        /// 마을 시각이 빽빽해 보이는 근본 원인은 거리 1 인접 → 카테고리 기본은 2.
        /// </summary>
        public static int GetDefaultMinSeparation(BuildableCategory category)
        {
            return category switch
            {
                BuildableCategory.Housing    => 2,
                BuildableCategory.Storage    => 1,  // 창고는 작업장 옆에 붙어있어도 자연스러움
                BuildableCategory.Production => 2,
                BuildableCategory.Cooking    => 2,
                BuildableCategory.Forge      => 2,  // Furnace/Anvil/QuenchVat 같은 세트도 거리 2 — 세트 페어링은 클러스터 보너스가 처리
                BuildableCategory.Service    => 2,
                BuildableCategory.Defense    => 0,  // WallPlanner 전담 — BuildQueue 일반 경로 미사용
                BuildableCategory.Decor      => 1,
                _ => 1,
            };
        }

        // ========== 내부: minSep 완화 fallback ==========

        private static Vector2Int? FindWithFallback(
            Vector2Int center, int maxRadius,
            int villageId, BuildableCategory category, int boundsRadius,
            int minSep)
        {
            // 후보 누적
            _bucket.Clear();
            if (IsEmpty(center, center))
                _bucket.Add(center);
            for (int r = 1; r <= maxRadius; r++)
                CollectRing(center, r, _bucket);

            // 외곽 마진 + minSep 하드 필터
            for (int i = _bucket.Count - 1; i >= 0; i--)
            {
                if (IsValidPlacement(_bucket[i], center, boundsRadius, category) == false
                    || SatisfiesMinSeparation(_bucket[i], minSep) == false)
                    _bucket.RemoveAt(i);
            }

            if (_bucket.Count == 0)
            {
                // minSep 1단계 완화 후 재시도 — 마을이 너무 비좁아진 후기에 무한 정체 방지
                if (minSep > 1)
                    return FindWithFallback(center, maxRadius, villageId, category, boundsRadius, minSep - 1);
                return null;
            }

            // 점수 최대 후보 픽 (동점은 50/50 랜덤)
            Vector2Int best = _bucket[0];
            float bestScore = ScoreCandidate(best, center, villageId, category);
            for (int i = 1; i < _bucket.Count; i++)
            {
                float s = ScoreCandidate(_bucket[i], center, villageId, category);
                if (s > bestScore || (Mathf.Approximately(s, bestScore) && Random.value < 0.5f))
                {
                    best = _bucket[i];
                    bestScore = s;
                }
            }
            return best;
        }

        // ========== A1: minSeparation 하드 필터 ==========

        /// <summary>
        /// 후보 타일에서 체비쇼프 거리 1 ~ minSep-1 범위에 점유 타일이 없으면 통과.
        /// minSep ≤ 1이면 항상 통과 (제약 없음).
        /// </summary>
        private static bool SatisfiesMinSeparation(Vector2Int tile, int minSep)
        {
            if (minSep <= 1) return true;
            int range = minSep - 1;
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int n = new Vector2Int(tile.x + dx, tile.y + dy);
                    if (IsOccupiedTile(n)) return false;
                }
            }
            return true;
        }

        // ========== Phase D: 외곽 마진 ==========

        /// <summary>
        /// 비-Defense 카테고리는 경계로부터 ≥ OUTSKIRT_MARGIN_TILES 안쪽만 허용.
        /// Defense는 경계 위에만 (BuildQueue 일반 경로는 Defense 처리 안 함 — WallPlanner 전담).
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

        // ========== A3 + B2: 거리 차등 점수 ==========

        /// <summary>
        /// 후보 타일 점수.
        ///  A3) 거리 1 점유 -1.0 / 거리 2 점유 -0.4 (체비쇼프) — 점유 페널티 확장
        ///  B2) 같은 카테고리: d=1~2 +1.0, d=3~4 +0.3
        ///       다른 카테고리: d=1~2 -0.6 (인접 회피), d=3~4 -0.1
        ///  중심 거리 미세 페널티 0.05/타일 — 동점 시 살짝 안쪽 선호
        /// </summary>
        private static float ScoreCandidate(Vector2Int tile, Vector2Int center, int villageId, BuildableCategory category)
        {
            const int SCAN_RADIUS = 4;
            float score = 0f;

            for (int dx = -SCAN_RADIUS; dx <= SCAN_RADIUS; dx++)
            {
                for (int dy = -SCAN_RADIUS; dy <= SCAN_RADIUS; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    Vector2Int n = new Vector2Int(tile.x + dx, tile.y + dy);

                    // A3: 일반 점유 페널티 (벽/몬스터/오브젝트 모두 포함)
                    if (d <= 2 && IsOccupiedTile(n))
                        score += (d == 1) ? -1.0f : -0.4f;

                    // B2: 카테고리 클러스터 (마을 PlacedObject만)
                    if (villageId >= 0 && category != BuildableCategory.None)
                    {
                        int neighborEntity = PlacedObjectRegistry.GetEntityAtTile(villageId, n);
                        if (neighborEntity < 0) continue;
                        if (AR.s.Component.TryGetComponent<ARPG.Component.PlacedObjectComponent>(neighborEntity, out var po) == false) continue;

                        Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(po.TableId);
                        if (t == null) continue;
                        BuildableCategory neighborCat = (BuildableCategory)t.Category;
                        if (neighborCat == category)
                            score += SameCategoryBonus(d);
                        else if (neighborCat != BuildableCategory.None)
                            score += DifferentCategoryPenalty(d);
                    }
                }
            }

            // 중심 거리 미세 가산 (동점 시 살짝 안쪽 선호)
            int cd = Mathf.Max(Mathf.Abs(tile.x - center.x), Mathf.Abs(tile.y - center.y));
            score -= cd * 0.05f;

            return score;
        }

        private static float SameCategoryBonus(int chebyshevDist) => chebyshevDist switch
        {
            1 => 1.0f,
            2 => 1.0f,
            3 => 0.3f,
            4 => 0.3f,
            _ => 0f,
        };

        private static float DifferentCategoryPenalty(int chebyshevDist) => chebyshevDist switch
        {
            1 => -0.6f,
            2 => -0.4f,
            3 => -0.1f,
            _ => 0f,
        };

        // ========== 빈 타일 / 점유 판정 ==========

        /// <summary>링 r(체비쇼프)의 가장자리 빈 타일을 모두 모은다. 광장/큰길 예약 셀 제외.</summary>
        private static void CollectRing(Vector2Int center, int r, List<Vector2Int> output)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int ax = Mathf.Abs(dx);
                    int ay = Mathf.Abs(dy);
                    if (ax != r && ay != r) continue; // 링 가장자리만
                    Vector2Int candidate = new Vector2Int(center.x + dx, center.y + dy);
                    if (IsEmpty(candidate, center))
                        output.Add(candidate);
                }
            }
        }

        /// <summary>
        /// 빈 타일 판정. center 인자는 광장/큰길 예약 체크용.
        /// </summary>
        public static bool IsEmpty(Vector2Int tile, Vector2Int center)
        {
            // 광장/큰길 예약 셀이면 즉시 제외
            if (IsReservedTile(tile, center))
                return false;

            return IsOccupiedTile(tile) == false;
        }

        private static bool IsOccupiedTile(Vector2Int n)
        {
            Vector3 world = new Vector3(n.x + 0.5f, n.y + 0.5f, 0f);
            if (AR.s.Map.IsWalkable(world) == false) return true;
            if (AR.s.Map.GetObjectIdAt(n.x, n.y) != 0) return true;
            if (AR.s.Building != null && AR.s.Building.IsTileOccupied(n.x, n.y)) return true;
            return false;
        }

        /// <summary>
        /// A4 광장 + 큰길 (4축선) 예약 통합 판정.
        /// 광장: |dx|, |dy| 둘 다 ≤ _plazaRadius (체비쇼프 box)
        /// 큰길: 한 축이 ≤ _roadHalfWidth, 다른 축이 ≤ _roadReserveRadius
        /// </summary>
        private static bool IsReservedTile(Vector2Int tile, Vector2Int center)
        {
            int dx = tile.x - center.x;
            int dy = tile.y - center.y;
            int adx = Mathf.Abs(dx);
            int ady = Mathf.Abs(dy);

            if (_plazaRadius > 0 && adx <= _plazaRadius && ady <= _plazaRadius)
                return true;

            if (_roadReserveRadius > 0)
            {
                int hw = _roadHalfWidth;
                if (adx <= hw && ady <= _roadReserveRadius) return true;
                if (ady <= hw && adx <= _roadReserveRadius) return true;
            }
            return false;
        }
    }
}
