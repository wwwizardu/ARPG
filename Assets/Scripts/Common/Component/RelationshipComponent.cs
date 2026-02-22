namespace ARPG.Component
{
    /// <summary>
    /// 엔티티 간 단방향 관계 데이터
    /// From → To 방향의 관계를 나타낸다.
    /// 관계 엔티티 ID는 EntityIdHelper.CreateRelationshipEntity()로 생성
    /// </summary>
    public struct RelationshipComponent
    {
        public int FromEntityId;
        public int ToEntityId;
        public int Affinity;        // 친밀도 (0~100)
        
    }
}
