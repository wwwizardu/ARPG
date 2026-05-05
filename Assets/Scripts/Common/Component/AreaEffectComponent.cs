namespace ARPG.Component
{
    /// <summary>
    /// 장판(지속 영역 효과) 컴포넌트. 자체 엔티티에 부착되며 System_AreaEffect가 매 틱 처리.
    /// 만료는 LifetimeComponent + System_Lifetime이 담당 (DestroyTag 부착).
    /// FactionComponent는 부착하지 않음 — 적 AI가 장판을 공격 대상으로 보지 않도록.
    /// 적/아군 판정은 CasterFaction 스냅샷으로 수행.
    /// </summary>
    public struct AreaEffectComponent
    {
        public int OwnerEntityId;           // 시전자(데미지/킬 보상 귀속용)
        public int AreaEffectTableId;       // AreaEffectTable Id
        public int SkillId;                 // 트리거된 스킬 Id (0이면 순수 장판)
        public Faction CasterFaction;       // 시전자 진영 스냅샷 (caster 사망 후에도 안전한 적/아군 판정)
        public float Radius;                // 효과 반경
        public float TickInterval;          // 틱 간격(초)
        public float NextTickIn;            // 다음 틱까지 남은 시간(초)
    }

    public struct AreaEffectTag { }
}
