#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// 벽 세그먼트 1칸의 영구 세이브 데이터.
    /// VillageData.WallSegments에 누적. 청크 활성/비활성과 무관하게 보존.
    /// 실제 ECS 컴포넌트(WallSegmentComponent)는 활성 청크에서만 부착, 로드 시 재구성.
    /// </summary>
    [Serializable]
    public class WallSegmentSaveData
    {
        public int SegmentId;
        public int TileX;
        public int TileY;
        public int Type;        // WallType: 0=Palisade, 1=StoneWall
        public int Orient;      // WallOrientation: 방위/코너/게이트
        public int CurrentHp;
        public int MaxHp;
        public bool IsBuilt;    // false = 큐에 있지만 아직 미배치 (System_VillageWallPlanner가 큐잉)
    }
}
