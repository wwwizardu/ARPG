namespace ARPG.Component
{
    /// <summary>
    /// 토템·지뢰·소환물 등 "다른 엔티티가 시전한 스킬을 대신 발사"하는 엔티티가 가지는 링크.
    /// 원 시전자 추적이 필요한 보상·경험치·소유권 판정 등에 사용한다.
    /// 흡혈 같은 자기 유지 효과는 실제 공격 주체(토템 등)에게 적용한다.
    /// </summary>
    public struct CasterLinkComponent
    {
        public int CasterEntityId;
    }
}
