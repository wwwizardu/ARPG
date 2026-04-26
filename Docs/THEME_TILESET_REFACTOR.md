# ThemeTileSet 리팩터 — Addressable 기반 바이옴 시스템

> **목표**: 바이옴별 지형 비주얼(용암/설원/사막/숲)을 효율적으로 로드/캐시하면서, 신규 오브젝트는 개별 Addressable로 처리하는 **이원 구조** 정립.
>
> 단계적 마이그레이션 — 디자이너 워크플로우 보존 + 메모리 효율 + 기존 코드 영향 최소화.

---

## 1. 배경

### 1.1 원래 의도

`ThemeTileSet` ScriptableObject는 **바이옴별 지형 비주얼 차별화**를 위한 디자인:
- Forest 지역 → green grass ground, oak hill cliff
- Lava 지역 → lava ground, obsidian hill cliff
- Snow 지역 → snow ground, ice hill cliff
- Desert 지역 → sand ground, sandstone hill cliff

각 바이옴별 SO 1개에 그 지역에서 쓰는 모든 TileBase 참조를 모아둠 — 디자이너가 한 화면에서 작업 + 미리보기 가능.

### 1.2 Phase A 이후 변화

[Phase A](archive/PHASE_A_HYBRID_PLAN.md)에서 마을 자가 건설 시작 → 신규 오브젝트(Campfire, Bed 등)가 추가되면서:

- 오브젝트는 **모든 바이옴에서 동일한 모양** (Anvil이 Forest/Lava에서 모양이 달라질 이유 없음)
- `BuildableItemTable + Addressable per-tile` 패턴 도입 ([BuildableTileRegistry.cs](../Assets/Scripts/Map/BuildableTileRegistry.cs))
- ThemeTileSet에 새 ObjectSet 슬롯 추가하기 어색해짐 (디자이너가 매 바이옴마다 같은 anvil 드래그?)

→ **Object 레이어는 ThemeTileSet에서 분리**가 자연스러워짐.

### 1.3 현재 상태 (단계 1 완료)

| 자산 종류 | 로드 경로 | 비고 |
|----------|----------|------|
| **Object 신규 자산** (Anvil, Hearth, ...) | `BuildableTileRegistry` (Addressable per-tile) | ✅ Phase B/C 통합 완료 |
| **Object 레거시 자산** (Stone, Npc, WoodWall) | `ThemeTileSet.ObjectSet` fallback | ⚠️ Deprecated, 단계 2 완료 시 제거 |
| **Ground/Hill 지형** | `ThemeTileSet.TileSet[]` 직접 참조 | 👈 단계 2의 주 대상 |
| **NpcSet** | (확인 필요 — 현재 사용 여부 미파악) | 단계 3에서 정리 |

---

## 2. 단계 1 — ObjectSet Deprecated 표시 ✅ 완료 (2026-04-26)

### 변경 내용

- [ThemeTileSet.cs](../Assets/Scripts/Map/Tile/ThemeTileSet.cs) — `ObjectSet` 필드에 `[System.Obsolete]` attribute + 헤더 라벨 변경
- [MapManager_Renderer.cs](../Assets/Scripts/Map/MapManager_Renderer.cs) — fallback 사용 부분에 `#pragma warning disable 0618` + 의도 주석

### 효과

- 신규 코드/디자이너가 ObjectSet에 자산 추가하려 하면 **컴파일 경고로 안내**
- 기존 fallback은 그대로 동작 (레거시 호환 유지)
- 단계 2 시점에 본 단계의 `#pragma warning disable` + fallback 분기 제거

---

## 3. 단계 2 — ThemeTileSet 자체를 Addressable로

### 3.1 목표

각 바이옴 SO를 Addressable 자산으로 등록 → 활성 지역의 테마만 메모리에 로드.

### 3.2 자산 배치

```
Assets/Art/Tilemap/Themes/
├── Theme_Forest.asset       ← ThemeTileSet (현 단일 SO에서 분기)
├── Theme_Lava.asset
├── Theme_Snow.asset
├── Theme_Desert.asset
└── Theme_Cave.asset

Addressable Group: "Themes" (Default Local Group과 분리해 라이프사이클 관리)
키: Theme/Forest, Theme/Lava, Theme/Snow, Theme/Desert, Theme/Cave
```

각 SO 안에 들어있는 TileBase 참조는 Addressables 의존성 그래프가 자동 처리 — `Theme/Lava` 1번 로드하면 그 안의 lava ground / lava hill / lava cliff 모두 같은 번들에서 같이 로드.

### 3.3 ThemeTileSet 구조 정리

단계 2에서 ObjectSet 완전 제거:

```csharp
public class ThemeTileSet : ScriptableObject
{
    [Header("Theme Info")]
    public string themeName;
    public Sprite themeIcon;

    [Header("Ground / Hill / Cliff / Water")]
    public TileBase[] TileSet;

    // ObjectSet 필드 삭제 (단계 1에서 deprecated → 단계 2에서 제거)

    [Header("Npc (사용 시)")]
    public TileBase[] NpcSet;
}
```

`MapManager_Renderer.cs`의 fallback 분기 + `#pragma warning` 도 같이 삭제.

### 3.4 MapFileData에 ThemeName 추가

```csharp
public class MapFileData
{
    // ... 기존 필드들
    public string ThemeName = "Forest";   // Addressable 키 suffix
}
```

또는 `enum MapTheme { Forest, Lava, Snow, Desert, Cave }` 로 enum화 (오타 방지).

### 3.5 ThemeTileSetRegistry 신설

`BuildableTileRegistry`와 동일한 패턴 — `_cache` + `_loading` HashSet + WaitUntil 다중 awaiter 지원:

```csharp
public static class ThemeTileSetRegistry
{
    private static readonly Dictionary<string, ThemeTileSet?> _cache = new();
    private static readonly HashSet<string> _loading = new();

    public static event Action<string>? ThemeLoaded;

    public static async UniTask<ThemeTileSet?> EnsureLoadedAsync(string themeName);
    public static ThemeTileSet? Get(string themeName);
    public static void Reset();
}
```

키 형식: `Theme/{themeName}`. 미등록 키는 `LoadResourceLocationsAsync` 사전 검증으로 InvalidKeyException 방지.

### 3.6 MapManager_Renderer 변경

```csharp
// 이전
_tempTileArray[index] = _themeTileSet.TileSet[baseTileType];

// 이후
ThemeTileSet? activeTheme = ThemeTileSetRegistry.Get(currentChunkTheme);
_tempTileArray[index] = activeTheme?.TileSet[baseTileType] ?? defaultGroundTile;
```

`currentChunkTheme`는 청크 단위 결정 (§4 참조).

### 3.7 단계 2 작업 분해

| Step | 작업 | 추정 |
|------|------|------|
| 2.1 | 기존 단일 ThemeTileSet에서 바이옴별 SO 분기 (Theme_Forest 1개부터 시작) | 30분 |
| 2.2 | Addressable Group "Themes" 생성, 키 등록 | 10분 |
| 2.3 | `MapFileData.ThemeName` 필드 + 마이그레이션 (구 세이브 → "Forest" 기본값) | 20분 |
| 2.4 | `ThemeTileSetRegistry` 신설 | 30분 |
| 2.5 | `MapManager_Renderer` 적용 | 20분 |
| 2.6 | ObjectSet 필드 + fallback 분기 + #pragma 제거 | 10분 |
| 2.7 | 동작 검증 (1개 바이옴부터) | 30분 |
| **합계** | | **~2.5h** |

### 3.8 단계 2 적용 시점

다음 중 하나가 트리거:
- 신규 바이옴 추가 필요 (Lava/Snow 등 첫 도입)
- 메모리 압박 (여러 바이옴 동시 로드 부담)
- ObjectSet fallback 코드 정리 욕구

→ Phase D~E 어딘가 자연스럽게 들어감.

---

## 4. 단계 3 — 청크별 테마 + Ref-count 기반 로드/해제

### 4.1 목표

플레이어가 바이옴 경계를 넘나들 때 **부드러운 메모리 관리**:
- 활성 청크가 사용하는 테마만 메모리 유지
- 비활성 청크의 테마는 ref-count 0이면 Addressables.Release

### 4.2 청크 → 테마 매핑

여러 방식 가능:

| 방식 | 설명 | 장단점 |
|------|------|--------|
| **A. 청크 단위 ThemeName** | `MapChunkData.ThemeName` 필드 | 가장 유연, 마이크로 바이옴 가능. 세이브 부담 약간 증가 |
| **B. MapFileData(맵) 단위** | 맵 1개 = 테마 1개 | 단순, Phase B/C와 일관. 맵 안 다중 바이옴 불가 |
| **C. 좌표 기반 절차적** | World position → biome map → theme | 동적, 무한 맵 친화적. 결정 로직 복잡 |

→ **B 권장** (단순, Phase 진행 중 추가 변경 최소). 마이크로 바이옴 필요 시 단계 3+에서 A로 진화.

### 4.3 Ref-count 관리

```csharp
public static class ThemeTileSetRegistry
{
    private static readonly Dictionary<string, int> _refCount = new();
    private static readonly Dictionary<string, AsyncOperationHandle<ThemeTileSet>> _handles = new();

    public static async UniTask<ThemeTileSet?> AcquireAsync(string themeName)
    {
        // ref-count++, 처음이면 Addressables.LoadAssetAsync
    }

    public static void Release(string themeName)
    {
        // ref-count--, 0이면 Addressables.Release(handle)
    }
}
```

### 4.4 청크 활성화 훅 통합

```csharp
// MapManager_Spawner.OnChunkActivated
string theme = GetChunkTheme(chunkCoord);
ThemeTileSetRegistry.AcquireAsync(theme).Forget();

// MapManager_Spawner.OnChunkDeactivated
string theme = GetChunkTheme(chunkCoord);
ThemeTileSetRegistry.Release(theme);
```

`BuildingManager`/`NpcManager`의 청크 활성화 훅 패턴과 일관.

### 4.5 단계 3 작업 분해

| Step | 작업 | 추정 |
|------|------|------|
| 3.1 | `_refCount`, `_handles` 추가 + `Acquire`/`Release` 메서드 | 30분 |
| 3.2 | `OnChunkActivated/Deactivated` 훅 통합 | 15분 |
| 3.3 | (선택) 청크 단위 ThemeName 지원 (방식 A) | 60분 |
| 3.4 | 메모리 사용량 측정 + 누수 검증 | 30분 |
| **합계** | | **~2h** (방식 A 제외 시 1h) |

### 4.6 단계 3 적용 시점

- 메모리 프로파일링에서 테마 자산이 누적되는 게 보일 때
- 모바일 빌드 등 메모리 제약 시점
- 멀티 바이옴 맵이 본격화될 때

→ **단계 2가 완료된 후, 실제 문제가 보일 때**. 미리 만들 필요 없음 (YAGNI).

---

## 5. 단계 4 (선택, 장기) — 바이옴 경계 블렌딩

각 바이옴 경계에서 시각 전환을 부드럽게:

- Forest → Lava 경계: 갈변/그을림 그라디언트
- 경계 청크에서 두 테마 동시 로드 + 알파 블렌딩
- 또는 별도 "transition" 타일셋 (Forest_Lava_Edge.asset 등)

매우 게임 마무리 단계 작업. Phase F~G 즈음.

---

## 6. 신규/제거 파일 요약 (단계 2 적용 시)

### 신규
- `Assets/Art/Tilemap/Themes/Theme_*.asset` (바이옴 수만큼)
- `Assets/Scripts/Map/Tile/ThemeTileSetRegistry.cs`

### 수정
- `Assets/Scripts/Map/Tile/ThemeTileSet.cs` — `ObjectSet` 필드 삭제
- `Assets/Scripts/Map/MapManager_Renderer.cs` — fallback 분기 + `#pragma warning` 삭제, `_themeTileSet` 직접 참조 → `ThemeTileSetRegistry.Get(currentTheme)` 호출
- `Assets/Scripts/Map/MapFileData.cs` (또는 해당 파일) — `ThemeName` 필드 추가
- `Assets/Scripts/Map/MapManager_Spawner.cs` — `OnChunkActivated/Deactivated`에 `ThemeTileSetRegistry` 호출 추가 (단계 3)

### 잔존 자산
- 기존 단일 `ThemeTileSet` 자산 — Theme_Forest로 리네임 후 다른 바이옴 자산은 복제+편집

---

## 7. 트레이드오프 / 대안 검토

### 검토했지만 채택 안 한 옵션

| 옵션 | 채택 안 한 이유 |
|------|----------------|
| **A. 개별 타일 Addressable + 테마 prefix 키** (`Tile/Forest/Ground`) | 키 수 폭발, 디자이너가 한 화면에서 바이옴 미리보기 못 함 |
| **C. Addressable Labels** (`Tile/Ground` + label `Lava`) | 라벨 기반 variant 선택은 Unity에서 흔치 않음, 디버깅 비직관 |
| **D. Resources.Load 그대로** | 모든 테마 메모리 항상 로드, Addressable 통일성 깨짐 |

### 채택 (B — 테마 SO Addressable)의 핵심 장점

1. **디자이너 친화** — 바이옴별 SO 1개에서 모든 지형 타일 한눈에 정의/미리보기
2. **묶음 로드 효율** — Addressable 의존성 그래프가 한 번에 처리, 개별 키 다수 로드 비용 회피
3. **점진 마이그레이션 가능** — 단계 1 → 2 → 3 순으로 영향 범위 작게
4. **객체 시스템과 일관** — Theme-dependent 자산만 SO 묶음, Theme-independent는 개별 Addressable

---

## 8. 한 줄 요약

> **지형 비주얼은 ThemeTileSet ScriptableObject 묶음(바이옴별 1개) Addressable로, 오브젝트는 BuildableTileRegistry 개별 Addressable로 — 각자 자기 목적에 맞는 로드 단위를 가진다.**
