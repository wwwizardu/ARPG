namespace ARPG.Component
{
    /// <summary>
    /// 거리 기반 활성화/비활성화 컴포넌트
    /// System_EntityActivation에서 플레이어와의 거리를 체크하여
    /// 히스테리시스 방식으로 GameObject를 활성화/비활성화
    /// 몬스터, NPC, 아이템 등 모든 엔티티에서 사용 가능
    /// </summary>
    public struct ActivationDistanceComponent
    {
        public float ActivationDistanceSqr;
        public float DeactivationDistanceSqr;
        public bool IsActivated;
    }
}
