namespace ARPG.Component
{
    /// <summary>
    /// 진영 식별 컴포넌트. 스킬·발사체·AI가 적대 대상을 가려낼 때 사용.
    /// 동일 FactionId = 아군, 다른 FactionId = 적. FactionComponent가 없는 엔티티는 중립으로 간주(스킬 대상 아님).
    /// 토템·지뢰는 caster의 FactionId를 그대로 복사.
    /// </summary>
    public struct FactionComponent
    {
        public Faction FactionId;
    }

    public enum Faction : byte
    {
        Neutral = 0,    // 중립 (NPC 등 — 스킬 대상 아님)
        Player  = 1,    // 플레이어와 그 동맹 (토템/지뢰/소환물)
        Hostile = 2,    // 적대 진영 (몬스터)
    }
}
