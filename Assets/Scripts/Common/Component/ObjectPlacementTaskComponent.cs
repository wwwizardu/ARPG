#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 현재 진행 중인 오브젝트 배치 작업.
    /// 별도 task entity에 부착됨 (마을당 최대 Population 개 동시 진행).
    /// 세이브는 VillageData.ActiveBuildTasks(List&lt;BuildTaskSnapshot&gt;)로 미러링.
    ///
    /// 진행 시간은 AccumulatedHours가 정본. 배정된 NPC가 Build 상태로 현장에 도달했을 때만 누적된다.
    /// 위협 감지로 NPC가 Flee/Chase 전이하면 자동으로 일시정지, 복귀 시 재개.
    /// 완료 판정: AccumulatedHours &gt;= BuildDurationHours.
    /// </summary>
    public struct ObjectPlacementTaskComponent
    {
        public int VillageId;
        public int TargetTableId;          // BuildableItemTable.Id
        public int TileX;
        public int TileY;
        public float StartedAt;            // 게임시간 (h) — 디버그/UI용 시작 시각
        public float AccumulatedHours;     // 실제 작업 진행 누적 시간 (h)
        public float BuildDurationHours;   // 완료까지 필요한 게임시간
        public int ReservedWoodCost;       // 착수 시 차감한 자원 (배치 실패 시 환불용)
        public int ReservedStoneCost;
        public int AssignedNpcEntityId;    // 배정된 NPC의 EntityId (-1 = 미배정)
        public int BuildingEntityId;       // 즉시 생성된 건설중 빌딩 엔티티 (-1 = SpawnType.Tile 또는 미생성)
    }
}
