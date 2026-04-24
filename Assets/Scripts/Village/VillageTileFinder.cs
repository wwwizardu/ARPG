#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 중심 주변의 빈 타일을 링 확장 BFS로 찾는 유틸리티.
    /// System_VillageFirstBuild의 Campfire 배치 위치 결정 및 Phase B의 범용 배치 시스템에서 공유 사용.
    /// </summary>
    public static class VillageTileFinder
    {
        /// <summary>
        /// 중심 타일에서 가장 가까운 "빈 Walkable 타일"을 반환. 없으면 null.
        /// 빈 타일 = IsWalkable=true AND ObjectLayer=0 (오브젝트 없음).
        /// </summary>
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            if (IsEmpty(center))
                return center;

            for (int r = 1; r <= maxRadius; r++)
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
                            return candidate;
                    }
                }
            }
            return null;
        }

        public static bool IsEmpty(Vector2Int tile)
        {
            // 타일 중심 월드 좌표로 변환 (Tilemap 좌표 기준 +0.5 보정)
            Vector3 world = new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0f);
            if (AR.s.Map.IsWalkable(world) == false)
                return false;
            // 이미 오브젝트가 있는 타일은 "빈" 타일이 아님
            return AR.s.Map.GetObjectIdAt(tile.x, tile.y) == 0;
        }
    }
}
