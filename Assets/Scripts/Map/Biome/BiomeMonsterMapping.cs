namespace ARPG.Map
{
    /// <summary>
    /// biome → 메인/서브 몬스터 ID 매핑. MAP_DESIGN.md §6.5 참조.
    ///
    /// Phase 1b 시작: 모든 biome이 폴백 ID로 매핑 (콘텐츠 작업 전).
    /// 디자이너가 monster ID 결정 후 case 추가 시 자동으로 biome별 분기.
    ///
    /// 단순 ID 1개 형태로 시작 — 가중치 풀(여러 ID + weights)은 §12 #13 후속 결정 시 확장.
    /// </summary>
    public static class BiomeMonsterMapping
    {
        /// <summary>biome별 메인 몬스터 ID. 매핑 없으면 폴백 반환.</summary>
        public static int GetMainMonsterId(GlobalEnum.BiomeType biome, int fallback)
        {
            switch (biome)
            {
                // 콘텐츠 추가 시 case 추가:
                // case GlobalEnum.BiomeType.Forest:    return 2001;   // 늑대
                // case GlobalEnum.BiomeType.Desert:    return 2010;   // 사막 거미
                // case GlobalEnum.BiomeType.Mountain:  return 2020;   // 골렘
                // case GlobalEnum.BiomeType.Swamp:     return 2030;   // 슬라임
                // case GlobalEnum.BiomeType.Tundra:    return 2040;   // 얼음 트롤
                // case GlobalEnum.BiomeType.Snow:      return 2040;
                // case GlobalEnum.BiomeType.Steppe:    return 2050;   // 들개
                // case GlobalEnum.BiomeType.Plains:    return 2000;   // 일반 몬스터
                default: return fallback;
            }
        }

        /// <summary>biome별 서브 몬스터 ID. 매핑 없으면 폴백 반환.</summary>
        public static int GetSubMonsterId(GlobalEnum.BiomeType biome, int fallback)
        {
            switch (biome)
            {
                // 콘텐츠 추가 시 case 추가
                default: return fallback;
            }
        }
    }
}
