using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// 테마별 타일 세트 정의
// TileType과 TileSet의 인덱스가 일치해야 함
public class ThemeTileSet : ScriptableObject
{
    [Header("Theme Info")]
    public string themeName;
    public Sprite themeIcon;

    // 지형 비주얼 (바이옴별 차별화 — Phase 단계 2에서 Addressable화 예정)
    [Header("Tiles (Ground / Hill / Cliff 등)")]
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
