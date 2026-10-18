namespace ARPG.Component
{
    /// <summary>
    /// 네임드 위협 엔티티 표시 (NAMED_THREAT_DESIGN §5.4·§12.1). 존재 시스템(System_NamedThreatPresence)이 만든 몬스터에만 붙는다.
    /// 기록(칭호·단계·피해)은 NamedThreatManager가 가진 세계 저장 기록에 있고, 이 컴포넌트는 그 기록의 Id만 담는다.
    /// </summary>
    public struct NamedThreatComponent
    {
        public int ThreatId;
    }
}
