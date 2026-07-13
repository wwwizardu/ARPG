#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 현재 진행 중인 오브젝트 배치 작업.
    /// 별도 task entity에 부착됨 (마을당 한 번에 1개씩 착공 — System_VillageBuildQueue Phase 2).
    /// 세이브는 VillageData.ActiveBuildTasks(List&lt;BuildTaskSnapshot&gt;)로 미러링.
    ///
    /// 자가 건설 (VILLAGE_NPC_BOUNDARY_DESIGN §4): NPC 배정 없이 게임시간 경과만큼 AccumulatedHours가 누적된다.
    /// 단 건설중 빌딩이 데미지를 받으면 진행도가 후퇴한다 (System_VillageBuildQueue HP↔진행도 동기화).
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
        public int BuildingEntityId;       // 즉시 생성된 건설중 빌딩 엔티티 (-1 = SpawnType.Tile 또는 미생성)
    }
}
