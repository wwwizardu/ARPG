#nullable enable
using ARPG.Village;

namespace ARPG.Component
{
    /// <summary>
    /// 벽 세그먼트 1칸의 런타임 ECS 컴포넌트.
    /// 활성 청크에서만 부착. 비활성 시 VillageData.WallSegments(SaveData)에서 보존, 활성 시 재구성.
    /// 시각은 RuleTile이 담당. 이 컴포넌트는 HP/타입/방위만 보유.
    /// </summary>
    public struct WallSegmentComponent
    {
        public int VillageId;
        public int SegmentId;
        public int TileX;
        public int TileY;
        public WallType Type;
        public WallOrientation Orient;
        public int CurrentHp;
        public int MaxHp;
    }
}
