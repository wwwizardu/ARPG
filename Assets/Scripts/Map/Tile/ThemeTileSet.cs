using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// 테마별 타일 세트 정의 (MAP_DESIGN.md §4.5 Option A)
// 런타임 렌더러는 Ground 필드를 사용. TileSet[] 인덱스 = TileType 방식은 레거시(MapFileSaver 에디터 룩업 전용).
public class ThemeTileSet : ScriptableObject
{
    [Header("Theme Info")]
    public string themeName;
    public Sprite themeIcon;

    [Header("Biome Ground Tile")]
    [Tooltip("이 바이옴이 사용할 바닥 타일 (Plains는 잔디, Desert는 모래 등). MapFileData가 깐 Ground(맨땅)는 MapManager._ruleTile로 별도 처리됨.")]
    public TileBase Ground;

    // TileSet[] — DEPRECATED.
    // 런타임 렌더러는 Ground 필드를 사용하도록 이관됨 (MAP_DESIGN.md §4.5 Option A).
    // MapFileSaver 에디터 도구가 .bytes 로드 시 TileType → CustomTile 역변환 룩업으로만 유지.
    [Obsolete("런타임 렌더러는 Ground 필드 사용. TileSet[]은 MapFileSaver 에디터 도구의 역변환 룩업용 레거시.")]
    [Header("Tiles (Legacy - MapFileSaver editor only)")]
    public TileBase[] TileSet;

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
