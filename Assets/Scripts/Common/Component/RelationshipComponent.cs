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
        public int Affinity;        // 친밀도 (-100~100)
        public int Trust;           // 신뢰도 (0~100)
        public int Fear;            // 공포 (0~100)
        public int Intimacy;        // 친밀도 깊이 (0~100)
    }
}
