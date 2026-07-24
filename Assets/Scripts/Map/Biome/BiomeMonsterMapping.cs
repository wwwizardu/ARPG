namespace ARPG.Map
{
    /// <summary>
    /// biome → 스폰 몬스터 ID 선택. MAP_DESIGN.md §6.5 / MONSTER_SPAWN_DESIGN.md 참조.
    ///
    /// 데이터 주도: 풀은 MonsterTable.Biome에서 파생한다(DataManager가 로드 시 인덱싱).
    /// - 공용 몬스터(Biome=Any) = 모든 biome 풀에 포함.
    /// - 특정 biome 몬스터 = 그 biome 풀에만 포함.
    /// 한 청크의 풀 = 공용 ∪ 그 biome 전용. 풀이 비면 호출자가 넘긴 fallback 사용.
    ///
    /// 무상태 순수 로직 — 데이터는 DataManager가 소유(생명주기), 여기선 조회+샘플만.
    /// 가중치/Zone 필터 등 확장은 SpawnPoolTable 도입(MONSTER_SPAWN_DESIGN §5) 시.
    /// </summary>
    public static class BiomeMonsterMapping
    {
        /// <summary>
        /// biome 스폰 풀(공용 ∪ biome전용)에서 몬스터 ID 1개를 균등 샘플링. 풀이 비면 fallback 반환.
        /// </summary>
        public static int SampleMonsterId(GlobalEnum.BiomeType biome, int fallback)
        {
            var data = AR.s.Data;
            if (data == null)
                return fallback;

            var common = data.GetCommonMonsterIds();
            var specific = data.GetBiomeSpecificMonsterIds(biome);

            int total = common.Count + specific.Count;
            if (total <= 0)
                return fallback;

            int roll = UnityEngine.Random.Range(0, total);
            return roll < common.Count ? common[roll] : specific[roll - common.Count];
        }
    }
}
