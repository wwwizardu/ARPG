namespace ARPG.Component
{
    /// <summary>
    /// Phase D: 플레이어 근처에서 사용 가능한 서비스 집계 컴포넌트.
    /// System_VillageServiceProximity가 0.3s마다 갱신.
    /// F키 입력 시 ServiceUIRouter가 이 컴포넌트의 Available/Nearest* 필드만 보고 UI 라우팅.
    /// 플레이어 엔티티에 1:1 부착.
    /// </summary>
    public struct PlayerNearbyServicesComponent
    {
        public ProvidedService AvailableServices;   // 비트 OR

        public int NearestShopEntityId;             // 상호작용 키 입력 시 사용할 PlacedObject (-1 = 없음)
        public int NearestForgeEntityId;            // Furnace anchor (HasObjectSet으로 단계 결정)
        public int NearestInnEntityId;              // InnBed (Hearth 세트는 마을 전체 검사)
        public int NearestShrineEntityId;
        public int NearestCivicEntityId;            // TownPost (필요도 가중 — Phase E 이상)

        public int NearestVillageId;                // 어느 마을에 속한 서비스인지 (-1 = 마을 밖)
    }
}
