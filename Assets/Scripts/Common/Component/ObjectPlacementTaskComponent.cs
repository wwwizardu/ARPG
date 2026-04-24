#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 현재 진행 중인 오브젝트 배치 작업.
    /// 마을 엔티티(VillageData.EntityId)에 1개 부착. 완료 시 제거.
    /// 세이브는 VillageData.CurrentBuild* 필드가 정본 — 이 컴포넌트는 런타임 전용.
    /// </summary>
    public struct ObjectPlacementTaskComponent
    {
        public int VillageId;
        public int TargetTableId;          // BuildableItemTable.Id
        public int TileX;
        public int TileY;
        public float StartedAt;            // 게임시간 (h)
        public float BuildDurationHours;   // 완료까지 걸리는 게임시간
        public int ReservedWoodCost;       // 착수 시 차감한 자원 (배치 실패 시 환불용)
        public int ReservedStoneCost;
    }
}
