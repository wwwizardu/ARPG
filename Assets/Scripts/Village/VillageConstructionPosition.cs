#nullable enable

namespace ARPG.Village
{
    /// <summary>
    /// B-lite 추상 건설에서 아직 타일을 배정하지 않은 일반 건물 좌표 계약.
    /// 벽 세그먼트는 Bounds에서 좌표를 계산하므로 이 sentinel을 사용하지 않는다.
    /// </summary>
    public static class VillageConstructionPosition
    {
        public const int UnassignedTile = int.MinValue;

        public static bool IsAssigned(int tileX, int tileY)
        {
            return tileX != UnassignedTile && tileY != UnassignedTile;
        }

        public static bool IsAssigned(PlacedObjectSaveData placed)
        {
            return IsAssigned(placed.TileX, placed.TileY);
        }

        public static bool IsAssigned(BuildTaskSnapshot task)
        {
            return IsAssigned(task.TileX, task.TileY);
        }
    }
}
