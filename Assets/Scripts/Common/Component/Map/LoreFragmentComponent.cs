using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 유적 단편 (Lore Fragment). MAP_DESIGN.md §6 참조.
    /// 좌표 기반 결정론으로 청크 활성화 시 생성, 비활성화 시 폐기.
    /// 동적 상태(State / 글귀)는 WorldData.LoreSaveData에 영구 보존.
    /// </summary>
    public struct LoreFragmentComponent
    {
        public int FragmentId;                  // 결정론 ID. regionId * 1000 + slotIndex
        public Vector2Int Coord;                // 월드 타일 좌표 (결정론 — DeterministicCoord.InRegion)
        public GlobalEnum.BiomeType TileBiome;  // 이 fragment가 속한 region의 biome (한 region = 단일 biome, v1.8)
        public GlobalEnum.RaceOrigin RaceOrigin;// BiomeRaceTable에서 TileBiome 기준으로 선택
        public GlobalEnum.FateKeyword FateKeyword;
        public LoreFragmentLevel Level;         // L1 마커 / L2 소구조 / L3 청사진
        public int VariantIndex;                // L2/L3일 때 어느 패턴 선택했는지
        public LoreFragmentState State;         // Pristine / Discovered / Inscribed
    }

    /// <summary>
    /// 유적 단편의 비주얼 표현 레벨. MAP_DESIGN.md §6.3 참조.
    /// </summary>
    public enum LoreFragmentLevel
    {
        L1Marker = 0,            // 1×1 타일 (비석, 깨진 검, 백골)
        L2SmallStructure = 1,    // 2×2~3×3 코드 패턴 (폐허 잔해)
        L3Blueprint = 2,         // 5×5+ .bytes 청사진 (큰 폐허, 무덤)
    }

    /// <summary>
    /// 유적 단편의 동적 상태. WorldData.LoreSaveData가 Discovered/Inscribed만 영구 보존.
    /// </summary>
    public enum LoreFragmentState
    {
        Pristine = 0,            // 미발견 (기본)
        Discovered = 1,          // 플레이어가 방문/조사했지만 글귀 미생성 (예: LLM 실패)
        Inscribed = 2,           // LLM 글귀 캐시 보유. Inscriptions[FragmentId]에 텍스트
    }
}
