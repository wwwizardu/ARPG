namespace ARPG.Component
{
    /// <summary>
    /// Phase D: 배치된 마을 오브젝트의 ECS 컴포넌트.
    /// Phase A~C의 PlacedObjectTypeIds(ID-only 카운트)를 보조하여 위치/HP/세트 멤버십을 보존.
    /// 부착 시점: System_VillageBuildQueue.TryFinishAsync 성공 분기 (벽 제외).
    /// 캐시 필드(Service/SetMember)는 BuildableItemTable의 컬럼 값 그대로 — 부착 시 1회 복사.
    /// </summary>
    public struct PlacedObjectComponent
    {
        public int VillageId;
        public int TableId;             // BuildableItemTable.Id
        public int TileX;
        public int TileY;
        public int HP;
        public int MaxHP;
        public ProvidedService Service; // BuildableItemTable.ProvidedService 캐시 (Hot path 조회 절약용)
        public SetMemberTag SetMember;  // BuildableItemTable.SetMembership 캐시
        public int UsingNpcEntityId;    // -1 = 미사용. Phase D는 JobAssignment가 갱신
        public float LastUseGameTime;   // 쿨다운 추적 (Shrine 등). 0 = 미사용
    }
}
