using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// 테마별 타일 세트 정의 (MAP_DESIGN.md §4.5 Option A)
// 런타임 렌더러는 Ground 필드를 사용 — cluster biome용 단일 ground sprite.
// 청사진(.bytes)의 ground 비트는 GroundTileSet으로 별도 룩업.
public class ThemeTileSet : ScriptableObject
{
    [Header("Theme Info")]
    public string themeName;
    public Sprite themeIcon;

    [Header("Biome Ground Tile")]
    [Tooltip("이 바이옴이 사용할 바닥 타일 (Plains는 잔디, Desert는 모래 등). 청사진의 ground 비트는 GroundTileSet에서 별도 룩업.")]
    public TileBase Ground;

    // ObjectSet — DEPRECATED.
    // Phase A 이후 신규 오브젝트는 BuildableTileRegistry(Addressable) 경로로 일원화됨.
    // 본 필드는 Phase A 이전 레거시 자산(Stone/Npc/WoodWall 등) fallback 호환용으로만 유지.
    // 신규 추가 금지. 자세한 마이그레이션 계획은 Docs/THEME_TILESET_REFACTOR.md 참조.
    [Obsolete("신규 오브젝트는 BuildableItemTable + Addressable로 등록. ObjectSet은 레거시 fallback 전용. 자세한 내용은 Docs/THEME_TILESET_REFACTOR.md")]
    [Header("Object (DEPRECATED — Phase A 이후 BuildableTileRegistry로 이관)")]
    public TileBase[] ObjectSet;

    [Header("Npc")]
    public TileBase[] NpcSet;
}
