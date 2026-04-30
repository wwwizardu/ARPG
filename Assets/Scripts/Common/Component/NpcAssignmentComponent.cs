namespace ARPG.Component
{
    /// <summary>
    /// Phase D: NPC가 배정받은 작업 오브젝트(PlacedObject) 정보.
    /// System_VillageJobAssignment가 NpcTable.JobType × BuildableItemTable.AssociatedJobType 매칭으로 부착/해제.
    /// System_VillagePassiveProduction이 이 컴포넌트를 보고 직업 보너스 가산.
    ///
    /// Phase D MVP: 배정만. 실제 NPC 이동/일과 시뮬은 Phase E.
    /// </summary>
    public struct NpcAssignmentComponent
    {
        public int VillageId;
        public int AssignedObjectEntityId;      // PlacedObjectComponent 엔티티 (-1 = 무직)
        public int AssignedTableId;             // 빠른 조회용 (BuildableItemTable.Id)
        public GlobalEnum.JobType JobType;      // NPC가 가진 직업 (NpcTable.JobType 캐시)
    }
}
