namespace ARPG.Component
{
    /// <summary>
    /// 월드 광맥 엔티티 (WORLD_ORE_VEIN_PLAN §3·§8.2). 정본은 ore_veins 저장 기록이며 이 엔티티는 활성 청크에서만 존재한다.
    /// 채광 대상은 청크 좌표·굴린 날짜로 가리킨다 (§7.1).
    /// </summary>
    public struct OreVeinComponent
    {
        public int ChunkX;
        public int ChunkY;
        public int RollDay;
    }
}
