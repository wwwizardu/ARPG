using UnityEngine;

namespace ARPG.Map
{
    /// <summary>
    /// biome별 청크 생성 파라미터 매핑. MAP_DESIGN.md §4.4 참조.
    /// Phase 1은 코드 하드코딩 (§12 #13 결정에 따라 후속 시트 이관 가능).
    /// </summary>
    public static class BiomeTable
    {
        /// <summary>
        /// biome별 Hill 비트 임계값. elevation 노이즈가 이 값을 넘으면 Hill 비트 ON.
        /// 0~1, 작을수록 hill이 많이 나옴.
        /// </summary>
        public static float GetHillThreshold(GlobalEnum.BiomeType biome)
        {
            switch (biome)
            {
                case GlobalEnum.BiomeType.Mountain:  return 0.3f;   // hill 많이
                case GlobalEnum.BiomeType.Desert:    return 0.8f;   // hill 적게 (모래언덕만)
                case GlobalEnum.BiomeType.Swamp:     return 0.9f;   // hill 거의 없음 (저지)
                case GlobalEnum.BiomeType.Tundra:    return 0.7f;
                case GlobalEnum.BiomeType.Snow:      return 0.7f;
                default:                              return 0.6f;   // 기본 (현재 코드 값)
            }
        }

        /// <summary>
        /// biome별 몬스터 스폰 슬롯 밀도 배율.
        /// ZoneTable에서 뽑은 Main/Sub 슬롯 수에 각각 한 번만 적용한다.
        /// </summary>
        public static float GetMonsterSpawnMultiplier(GlobalEnum.BiomeType biome)
        {
            switch (biome)
            {
                case GlobalEnum.BiomeType.Forest:    return 1.2f;
                case GlobalEnum.BiomeType.Swamp:     return 1.3f;
                case GlobalEnum.BiomeType.Mountain:  return 1.3f;
                case GlobalEnum.BiomeType.Tundra:    return 0.7f;
                case GlobalEnum.BiomeType.Snow:      return 0.7f;
                default:                              return 1.0f;
            }
        }
    }
}
