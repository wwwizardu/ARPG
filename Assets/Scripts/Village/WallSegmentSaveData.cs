#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// 벽 세그먼트 1칸의 영구 세이브 데이터.
    /// VillageData.WallSegments에 누적. 청크 활성/비활성과 무관하게 보존.
    /// 벽·성문은 엔티티가 아니라 맵 타일 구조물이다. 마을 실체화 때 이 데이터로 맵 타일 오브젝트를 배치한다.
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
        public bool IsOpen;     // V4: Gate 개폐 상태 (Gate 세그먼트 전용). true = 열림(통행 가능, 타일 제거)
    }
}
