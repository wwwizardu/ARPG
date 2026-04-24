#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 중심 주변의 빈 타일을 링 확장 BFS(체비쇼프 거리)로 찾는 유틸리티.
    /// System_VillageBuildQueue가 오브젝트 배치 위치 결정에 사용.
    ///
    /// 정책: "가장 가까운 비어있지 않은 링" 내에서 **랜덤 픽** —
    ///   링 우선순위(중심 선호)는 유지 + 같은 링 내 결과를 분산해 시각적 클러스터링 방지.
    /// </summary>
    public static class VillageTileFinder
    {
        // 후보 수집 버퍼 (allocation-free 재사용). 링 r의 둘레는 8r → 최대 ~8*radius 슬롯.
        private static readonly List<Vector2Int> _bucket = new(32);

        /// <summary>
        /// 중심에서 가장 가까운 빈 Walkable 타일을 반환. 같은 거리에 여러 후보가 있으면 랜덤 선택.
        /// 빈 타일 = IsWalkable=true AND ObjectLayer=0 AND BuildingManager 미점유.
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            if (IsEmpty(center))
                return center;

            for (int r = 1; r <= maxRadius; r++)
            {
                _bucket.Clear();
                CollectRing(center, r, _bucket);

                if (_bucket.Count > 0)
                    return _bucket[Random.Range(0, _bucket.Count)];
            }
            return null;
        }

        /// <summary>링 r(체비쇼프 거리)의 가장자리 빈 타일을 모두 모은다.</summary>
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
                    if (IsEmpty(candidate))
                        output.Add(candidate);
                }
            }
        }

        public static bool IsEmpty(Vector2Int tile)
        {
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
    }
}
