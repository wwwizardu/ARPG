namespace ARPG.Component
{
    /// <summary>
    /// Phase D: 플레이어 근처에서 사용 가능한 서비스 집계 컴포넌트.
    /// System_VillageServiceProximity가 0.3s마다 갱신.
    /// Interact 키 입력 시 ServiceUIRouter가 이 컴포넌트의 Available/Nearest*/DistanceSqr 필드를 보고
    /// 거리 비교 후 가장 가까운 후보의 UI를 라우팅.
    /// 플레이어 엔티티에 1:1 부착.
    /// </summary>
    public struct PlayerNearbyServicesComponent
    {
        public ProvidedService AvailableServices;   // 비트 OR — 임계 거리 안에 들어온 시설들

        public int NearestShopEntityId;
        public float NearestShopDistanceSqr;
        public int NearestForgeEntityId;
        public float NearestForgeDistanceSqr;
        public int NearestInnEntityId;
        public float NearestInnDistanceSqr;
        public int NearestShrineEntityId;
        public float NearestShrineDistanceSqr;
        public int NearestCivicEntityId;
        public float NearestCivicDistanceSqr;

        // NPC 검색 (NPC_EQUIPMENT_DESIGN.md UI 패스) — ServiceUIRouter가 거리 통일 비교에 사용.
        // NPC는 ProvidedService 도메인이 아니라 별도 필드. -1 = 임계 거리 안에 없음.
        public int NearestNpcEntityId;
        public float NearestNpcDistanceSqr;

        public int NearestVillageId;                // 어느 마을에 속한 서비스인지 (-1 = 마을 밖)
    }
}
