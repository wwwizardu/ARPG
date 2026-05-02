namespace ARPG.Component
{
    /// <summary>
    /// 건물 엔티티의 메타데이터 + 런타임 상태
    /// 청크 언로드 시 BuildingSaveData로 스냅샷, 로드 시 복원
    /// </summary>
    public struct BuildingComponent
    {
        public int TableId;
        public int VillageId;             // -1 = 마을 소속 아님
        public int WorldTileX;
        public int WorldTileY;
        public int CurrentHp;             // 0 이하 = 파괴
        public bool IsUnderConstruction;  // true = 건설 중. CurrentHp는 진행도 역할 (0→MaxHp). 데미지로 깎이면 진행 후퇴.
    }
}
