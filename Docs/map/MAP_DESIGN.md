# 맵 시스템 설계

> **목표**: 오픈월드 절차 생성 맵의 현재 코드 상태를 정리하고, **유적 단편(Lore Fragment)** 기반의 분위기 시스템을 통합한다. 호기심을 자극하는 단편적 흔적을 흩뿌리고, 플레이어 행동의 파급력은 별도 Live 시스템(향후)이 누적한다.
>
> **위치**: 코드는 대부분 작성됨 ([Assets/Scripts/Map/](../../Assets/Scripts/Map), [MapManager.cs](../../Assets/Scripts/Manager/MapManager.cs)). 본 문서는 **현재 시스템의 사실 정리 + 유적 단편 시스템의 신규 설계**를 합친다.
>
> **선행/관련 문서**: [PROCEDURAL_HISTORY_DESIGN.md](../world_history/PROCEDURAL_HISTORY_DESIGN.md) (정착지 통폐합/연대기) · 공간 배치 백본은 [archive/PROCEDURAL_HISTORY_BACKBONE.md §6](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)을 본 문서가 대체/흡수 · [THEME_TILESET_REFACTOR.md](THEME_TILESET_REFACTOR.md) (바이옴 비주얼) · [MONSTER_SPAWN_DESIGN.md](../ai_monster/MONSTER_SPAWN_DESIGN.md) (스폰 풀) · [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) (유적 단편 fate `sealed`/`plagued`/`burned`가 도전 이벤트의 씨앗)

---

## 1. 디자인 철학

### 1.1 핵심 원칙
1. **결정론** — `mapSeed` 하나로 같은 좌표는 항상 같은 타일. 청크 unload→reload 시 동일 결과.
2. **빈틈이 기능** — 유적은 단편적 흔적. 사용자가 머릿속에서 연결하도록 *완결된 역사를 강요하지 않는다*.
3. **현재 > 과거** — Past는 *공간 분위기*만 제공. 플레이어의 *현재 행동*과 그 *파급력*이 본 게임 루프.
4. **데이터 주도** — 청사진(`.bytes`), BuildableItemTable(Addressable), 유적 카탈로그 모두 코드 외부.
5. **ECS 통합** — 청크 hook을 통해 ECS Entity / VFX 트리거.

### 1.2 참조 컨셉
- Dark Souls / Elden Ring — 단편 lore로 호기심 자극, 빈틈은 플레이어 상상으로 채움
- Core Keeper — 청크 기반 오픈월드 탐험
- RimWorld — 플레이어 행동의 파급력 (본 시스템에서는 향후 Live Backbone이 담당)

---

## 2. 현재 시스템 — 사실 정리

### 2.1 청크 구조

| 항목 | 값 | 근거 |
|------|-----|------|
| 청크 크기 | 8×8 타일 | [MapManager.cs:15](../../Assets/Scripts/Manager/MapManager.cs#L15) `chunkSize = 8` |
| 청크 풀 크기 | 20 | [MapManager.cs:42](../../Assets/Scripts/Manager/MapManager.cs#L42) `POOL_SIZE = 20` |
| 로드 반경 | 2 (5×5 청크 = 25개 활성) | [MapManager.cs:41](../../Assets/Scripts/Manager/MapManager.cs#L41) `_loadRadius = 2` |
| 맵 경계 | chunkX/Y ∈ [-98, 98] | [MapManager.cs:22-25](../../Assets/Scripts/Manager/MapManager.cs#L22-L25) |
| 활성 청크 | `_activeChunks: Dictionary<Vector2Int, MapChunkData>` | 생성된 청크 |
| 렌더 청크 | `_renderedChunks: HashSet<Vector2Int>` | 활성의 부분집합, 타일맵에 그려진 청크 |

청크 풀 재사용: unload 시 `_chunkPool`로 반환, 다음 로드 시 pop해서 재초기화. → **같은 청크를 다시 로드하면 `GenerateChunkData`가 다시 호출됨** (결정론 의존).

### 2.2 좌표/Zone

- **월드 좌표 → 청크**: `chunkX = floor(worldX / 8)`, `chunkY = floor(worldY / 8)`
- **Zone**: `max(|cx|, |cy|) + 1`, [1, 99] 클램프 — *난이도 거리 지표*. 시드 청크 (0,0) = Zone 1. ([MapManager_Generator.cs:77-79](../../Assets/Scripts/Map/MapManager_Generator.cs#L77-L79))
- **Zone은 바이옴이 아님** — 의미적 분류(산맥/숲/사막)는 현재 코드에 없음. 비주얼만 [ThemeTileSet](../../Assets/Scripts/Map/Tile/ThemeTileSet.cs)이 차별화하려는 의도였으나 인스펙터에 단일 SO만 박혀있는 상태.

### 2.3 타일 비트 레이아웃 (`ulong` 64비트)

[GlobalEnum.cs:43-61](../../Assets/Scripts/Common/GlobalEnum.cs#L43-L61):

| 비트 | 폭 | 용도 | 마스크 |
|------|-----|------|--------|
| 0-9 | 10b | Ground Layer (`GroundTileSet` 인덱스) | `GroundLayerMask = 0x3FF` |
| 10-19 | 10b | Object Layer (`BuildableItemTable.Id`) | `ObjectLayerMask = 0x3FF << 10` |
| 20 | 1b | Hill | `Hill = 1 << 20` |
| 21 | 1b | MonsterSpawn 후보 | `MonsterSpawn = 1 << 21` |
| 22 | 1b | Blocked (이동 불가) | `Blocked = 1 << 22` |
| 23-63 | 41b | **미사용** | (확장 여유) |

**Ground Layer 값** — 청사진(.bytes)의 ground 비트는 [`GroundTileSet`](../../Assets/Scripts/Map/Tile/GroundTileSet.cs)의 인덱스를 가리킨다:
- `0` = passthrough (청사진이 ground 미지정 → 렌더러가 cluster biome의 `ThemeTileSet.Ground`로 fallback)
- `1~` = `GroundTileSet.tiles[idx]` 룩업

→ 절차 생성 셀은 ground 비트를 0으로 둔다 (cluster biome으로 자동 렌더). 청사진이 깐 셀만 인덱스를 채운다.

### 2.4 생성 파이프라인 — `GenerateChunkData`

[MapManager_Generator.cs:73-143](../../Assets/Scripts/Map/MapManager_Generator.cs#L73-L143):

```
1. Zone 계산:    zone = clamp(max(|cx|,|cy|)+1, 1, 99)
2. 절차 생성 (Perlin):
     terrainNoise(scale=0.1)   > 0.4 → Glass (잔디)
     elevationNoise(scale=0.03) > 0.6 → Hill 비트 ON
     hillFlag==0 && _randomGenerator.NextDouble() < 0.1 → MonsterSpawn 비트
3. OverlayMapFileData(chunk):    BaseTown.bytes 등 사전 청사진 적용
4. OverlayTileModifications(chunk): 플레이어 PlaceObject 누적 기록 적용
5. monsterSpawnPositions 최종 수집 (3, 4단계에서 비트가 살아남은 후보만)
```

**결정론 분석**:
- Perlin(`worldX + mapSeed`) → ✅ 좌표 의존, 결정론
- `_randomGenerator: System.Random` 으로 MonsterSpawn 결정 → ⚠️ **상태 의존**. 청크 reload 시 `_randomGenerator` 상태가 다르므로 다른 결과. 같은 청크라도 처음 본 순서에 따라 스폰 위치 변동.
- `MapFileData` 오버레이 → ✅ 결정론
- `TileModifications` → ✅ 직렬화된 기록 그대로 적용

→ **유적 단편 시스템은 결정론을 강제해야 한다** (§8 참고).

### 2.5 청사진 — MapFileData

[MapFileData.cs](../../Assets/Scripts/Map/MapFileData.cs):
- JSON 직렬화 → `.bytes` 파일 ([_BinaryData/TilemapData/](../../Assets/_BinaryData/TilemapData))
- 필드: `MapType` (Village/Dungeon/Field), `Width`, `Height`, `StartPosition: Vector2Int`, `_tileData: ulong[]`, `_objectList: List<MapFileObjectData>`
- 현재 1개만 존재: `BaseTown.bytes` ([MapManager_Generator.cs:352-388](../../Assets/Scripts/Map/MapManager_Generator.cs#L352-L388))
- 로딩: `LoadFixedMapFiles()`에서 게임 시작 시 1회 → `_mapFileDataDic` (key = `StartPosition`)
- 작성 도구: [MapFileSaver.cs](../../Assets/Scripts/Map/MapFileSaver.cs) (Editor에서 Tilemap → `.bytes`)
- 청크 적용: [`OverlayMapFileData(chunk)`](../../Assets/Scripts/Map/MapManager_Generator.cs#L145-L194)가 청크와 청사진의 bounding-box 겹침 계산 → 겹치는 셀만 덮어쓰기 (`mapTileData != 0` 조건)

### 2.6 오브젝트 타일 시스템

**이중 채널**:
- **신규**: `BuildableItemTable.Id` → Addressable lazy 로드 ([BuildableTileRegistry.cs](../../Assets/Scripts/Map/BuildableTileRegistry.cs)). 캐시 영구, 로드 완료 시 `TileLoaded` 이벤트 → MapManager가 활성 청크 재렌더.
- **레거시**: `ThemeTileSet.ObjectSet` (Obsolete, fallback 전용)

**SpawnType**:
- `Tile` — 타일 비트에 `objectId` 기록 + Tilemap 셀로 렌더
- `Entity` — 타일 비트 무변경, BuildingManager가 EntityBase GameObject로 스폰 ([MapManager.cs:311-331](../../Assets/Scripts/Manager/MapManager.cs#L311-L331))

### 2.7 저장/로드

`WorldData`에는 `_tileModifications` 사전만 직렬화됨:
- 직렬화: [`SaveTileModifications`](../../Assets/Scripts/Manager/MapManager.cs#L454) → `List<ChunkModificationData>` (청크당 변경된 셀만)
- 역직렬화: [`LoadTileModifications`](../../Assets/Scripts/Manager/MapManager.cs#L475)
- **백본 청크(Perlin + MapFileData)는 저장하지 않음** — `mapSeed` + 청사진으로 재생성

### 2.8 청크 활성화 hook

[MapManager_Spawner.cs:11-34](../../Assets/Scripts/Map/MapManager_Spawner.cs#L11-L34) `OnChunkActivated(chunkCoord, chunkData)`:
- Monster: 이미 스폰된 청크면 활성화. 초기 스폰은 별도 `System_MonsterSpawn` 담당
- Npc: `AR.s.Npc.OnChunkActivated`
- Building: `AR.s.Building.OnChunkActivated`

`OnChunkDeactivated`: 위 매니저들이 각각 비활성화/상태 저장.

### 2.9 렌더링

3개 Tilemap 레이어 ([MapManager.cs:10-12](../../Assets/Scripts/Manager/MapManager.cs#L10-L12)):
- `_tileMap` — Ground 레이어 (GroundTileSet 인덱스 룩업, cluster biome fallback)
- `_tileMap_Hill` — Hill 레이어 (`_ruleTile_Hill`)
- `_tileMap_Object` — Object 레이어 (BuildableTileRegistry → ObjectSet fallback)

`SetTilesBlock`으로 청크 단위 일괄 적용. 재사용 배열(`_tempTileArray`)로 GC 최소화.

---

## 3. 현재 시스템의 한계 (갭)

유적 단편 시스템 도입을 위해 해결해야 할 부분:

| 갭 | 현재 | 필요 | 영향 |
|----|------|------|------|
| **바이옴 분류** | TileType 4종 + Zone(거리). 고도 Perlin은 있으나 per-tile Hill 비트용만 | 의미적 바이옴 (Whittaker 3축: 온도×습도×고도). **per-tile** 결정 | 유적/몬스터 종류가 좌표에 따라 다름 |
| **온도/습도 채널** | 없음. `terrainNoise`는 Glass/Ground 단순 분기용 | per-tile Perlin 2개 추가 | Forest/Desert/Swamp/Tundra 자동 분리 |
| **TileType 종류** | 4종 (None/Ground/Glass/StoneGround) — Glass/Ground만 사용 | Sand, SwampMud, DryGrass, Snow 추가 | 바이옴별 비주얼 차별화 |
| **Region 좌표 격자** | 청크만 존재 | 청크 위 *암묵적 격자* (8×8 청크 = 1 region 좌표). `RegionCoord` static 헬퍼만 | lore fragment 결정론 ID grouping |
| **결정론 PRNG** | `System.Random` (상태 의존) | 좌표 기반 hash (SplitMix64 등) | 청크 reload 결과 일관성 |
| **다중 청사진** | BaseTown 1개 | N개 유적 청사진 + variant | 유적 다양성 |
| **`_tileModifications` 우회** | 백본은 직접 비트, 플레이어만 mod 기록 — 이미 구현됨 | 유적도 같은 패턴 활용 | 세이브 크기 절감 |
| **단일 ThemeTileSet** | 인스펙터에 SO 1개 박힘 | Region 바이옴별 TileSet 선택 | [THEME_TILESET_REFACTOR.md](THEME_TILESET_REFACTOR.md)와 합류 필요 |
| **공간 인덱스** | `_activeChunks` Dictionary만 | 청크→유적, region→유적 역인덱스 | 청크 진입 시 O(1) 조회 |
| **OnChunkActivated 확장 슬롯** | Monster/Npc/Building 3개 | + Lore (유적 비주얼 보강, VFX, Lore 아이템 spawn) | 단순 hook 추가 |

---

## 4. 신규 — 바이옴 시스템 (v1.9: cluster-grained + Region histogram)

### 4.1 의도

> **v1.9 (2026-05-20) — Cluster-grained biome + Region histogram 도입**.
> v1.8 region 단위 단일 biome은 settlement/lore 일관성을 보장했으나 **시각상 region 경계(32타일)가 칼날처럼 도드라지는 문제** 발생. v1.9에서 결정/시각을 분리: **시각은 cluster(2×2 타일) 단위로 다양**하고, **race/settlement 결정은 region histogram의 dominant biome 사용**.

- **biome 시각 단위**: **cluster (2×2 타일)**. 청크 내 4×4=16개 cluster, 각 cluster가 자기 biome으로 렌더.
- **biome 결정 단위 (race/settlement)**: **Region (4×4 청크 = 32×32 타일)**. region이 256개 cluster의 histogram 보유 → `DominantBiome` 또는 `PickWeightedBiome(hash)`로 race 결정.
- **격자감 완화**: cluster grain이 작아서(2타일) 자연스러운 변화. region 경계는 race 결정에만 영향, 시각은 cluster 단위로 흐트러짐.

→ **v1.7 옵션 Y(per-tile)와 v1.8 region 단일의 절충안**. 시각 다양성(옵션 Y의 장점) + 문화 단위 일관성(v1.8의 장점)을 모두 확보. §12 #10 결정 재정정.

**3축 채택 — Whittaker 모델**. 절차 생성 PCG의 표준이며, 본 프로젝트의 현재 상태(고도만 존재)를 자연 확장.

| 축 | 현재 코드 | 추가 여부 |
|----|---------|----------|
| 고도 | ✅ 있음 ([MapManager_Generator.cs:92-96](../../Assets/Scripts/Map/MapManager_Generator.cs#L92-L96), per-tile Hill 비트용) | region 단위 채널 분리 필요 |
| **온도** | ❌ 없음 | 신규 |
| **습도** | ❌ 없음 | 신규 |

→ Live Backbone 도입 시 **부패도**가 bias 축으로 추가될 자리는 §4.6에 hook만 열어둠.

### 4.2 BiomeType (enum)

```csharp
namespace ARPG
{
    public enum BiomeType
    {
        // 온도 × 습도 (Whittaker)
        Plains = 0,       // 중간 온도 + 중간 습도
        Forest = 1,       // 중간 온도 + 습함
        Desert = 2,       // 더움 + 건조
        Swamp = 3,        // 더움 + 매우 습함 / 또는 저고도 + 습함
        Steppe = 4,       // 더움 + 중간 습도 (초원)
        Tundra = 5,       // 추움 + 건조
        Snow = 6,         // 추움 + 중간~습함

        // 고도 변형 (다른 축 덮어쓰기)
        Mountain = 10,

        // 향후 (Live Backbone — bias 축에 의해 변형)
        Wasteland = 30,   // 부패도 ↑ 시 위 바이옴이 덮어쓰여짐. Phase 7 이후
    }
}
```

128 비트마스크 태그 시스템(현재 [HistoryTagRegistry](../../Assets/Scripts/History/Data/HistoryTagRegistry.cs))은 *부가 태그*용으로 보존. **BiomeType은 enum** — region당 1개 단순 분류.

### 4.3 결정 채널 — cluster 단위 + domain warping

#### 4.3.1 핵심 함수 — `ComputeClusterBiome` + `ComputeTileBiome`

> **v1.9 변경**: biome 결정 최소 단위 = **cluster (2×2 타일)**. `ComputeClusterBiome(clusterGridX, clusterGridY, mapSeed)`가 cluster 좌측-하단 코너 좌표에서 `ComputeTileBiome` 호출. 한 cluster 안 4개 타일은 같은 biome.
> `ComputeChunkBiome`은 **청크 중심 cluster의 biome**을 반환 (legacy 1-biome caller용 — MonsterManager 등).
> `ComputeRegionBiome`은 legacy 호환용으로 남김 (race 결정에는 사용하지 말 것 — `RegionData.DominantBiome` 권장).

```csharp
public static BiomeType ComputeTileBiome(int worldX, int worldY)
{
    // 1) Domain warping — 격자감 제거. 좌표를 살짝 흔든 뒤 노이즈 샘플.
    float warpX = worldX + Perlin((worldX + warpSeedA) * 0.02f, (worldY + warpSeedA) * 0.02f) * 16f;
    float warpY = worldY + Perlin((worldX + warpSeedB) * 0.02f, (worldY + warpSeedB) * 0.02f) * 16f;

    // 2) 3축 Perlin — Whittaker 모델
    float temperature = Perlin((warpX + tempSeed)  * 0.005f, (warpY + tempSeed)  * 0.005f);  // 광역
    float humidity    = Perlin((warpX + humidSeed) * 0.008f, (warpY + humidSeed) * 0.008f);  // 중간
    float elevation   = Perlin((warpX + elevSeed)  * 0.012f, (warpY + elevSeed)  * 0.012f);  // fine

    // 3) 1차: 고도 — 강한 변형
    if (elevation > 0.75f) return BiomeType.Mountain;
    if (elevation < 0.20f && humidity > 0.55f) return BiomeType.Swamp;

    // 4) 2차: Whittaker (온도 × 습도)
    bool cold = temperature < 0.33f;
    bool hot  = temperature > 0.66f;
    bool dry  = humidity    < 0.33f;
    bool wet  = humidity    > 0.66f;

    if (cold && dry) return BiomeType.Tundra;
    if (cold)        return BiomeType.Snow;
    if (hot && dry)  return BiomeType.Desert;
    if (hot && wet)  return BiomeType.Swamp;
    if (hot)         return BiomeType.Steppe;
    if (wet)         return BiomeType.Forest;
    return           BiomeType.Plains;
}
```

**스케일 가이드** (v1.9.1 ×0.5 적용 — biome 패치 2배 확대):
- 온도 **0.0025** (가장 coarse) — 주기 ~400 타일, 광역 기후대
- 습도 **0.004** — 주기 ~250 타일, 강수 패치
- 고도 **0.006** (가장 fine) — 주기 ~166 타일, 산맥/저지대
- warp 0.02 + amplitude 16 — 경계를 16타일 정도 흐트림 (cluster grain과 결합해 자연스러운 경계)

> **v1.9.1 (2026-05-20) 조정 사유**: 기본 스케일에서 "조금만 가도 biome 변하는" 체감 → 모든 scale ×0.5로 패치 크기 2배 확대.

**시드 채널 분리** (`warpSeedA/B`, `tempSeed`, `humidSeed`, `elevSeed`)는 `DerivedSeed.Derive(mapSeed, channelId)` ([DerivedSeed.cs](../../Assets/Scripts/History/Generator/DerivedSeed.cs), 구현됨)로 생성.

#### 4.3.2 cluster 단위 biome + Region histogram — v1.9

> **v1.9 정정 (2026-05-20)**: v1.8 region 단일 biome → v1.9 **cluster(2×2)** 단위 biome 결정 + **Region histogram**으로 race 결정 분리.
>
> v1.8의 문제: region 경계(32 타일)가 시각상 칼날처럼 도드라짐. Plains↔Snow 인접 region이면 32타일 직선 경계가 명확히 보임.
>
> 해결: **시각/논리 분리**.
> - **시각**: cluster(2×2 타일)마다 자기 biome으로 렌더 → 경계가 cluster grain(2 타일)으로 흐트러져 자연스러움.
> - **race/settlement 결정**: Region 전체의 cluster histogram → `DominantBiome`으로 결정. 작은 patch에 잘못된 race가 들어가는 v1.7 옵션 Y의 문제 차단.
>
> 저장:
> - `MapChunkData.clusterBiomes[16]` — 청크 내 cluster biome 배열 (8/2 = 4×4 = 16).
> - `MapChunkData.chunkBiome` — 청크 중심 cluster의 biome (MonsterManager 등 legacy 1-biome caller용 대표값).
> - `RegionData.BiomeStats` — region 256개 cluster의 histogram (Lazy compute + cache).

### 4.4 청크 생성과의 연결

`GenerateChunkData`가 **청크 내 16개 cluster**의 biome을 미리 샘플 → cluster별 BiomeTable 룩업 캐시 → 셀 루프에서 cluster 인덱스로 즉시 조회.

```csharp
// v1.9 cluster-grained — 청크 내 4×4=16 cluster의 biome 결정
int clustersPerSide = chunkSize / BiomeNoise.CLUSTER_SIZE;   // 8 / 2 = 4
int clusterCount = clustersPerSide * clustersPerSide;        // 16
chunk.clusterBiomes = new BiomeType[clusterCount];

for (int cx = 0; cx < clustersPerSide; cx++)
for (int cy = 0; cy < clustersPerSide; cy++)
{
    int globalClusterX = chunk.chunkX * clustersPerSide + cx;
    int globalClusterY = chunk.chunkY * clustersPerSide + cy;
    chunk.clusterBiomes[cy * clustersPerSide + cx] =
        BiomeNoise.ComputeClusterBiome(globalClusterX, globalClusterY, mapSeed);
}

// 청크 대표 biome (MonsterManager 등 1-biome caller용)
chunk.chunkBiome = BiomeNoise.ComputeChunkBiome(chunk.chunkX, chunk.chunkY, chunkSize, mapSeed);

// cluster별 파라미터 캐시 (BiomeTable 룩업 1회)
// v1.10: ground TileType 박지 않음. 절차 생성 셀의 ground 비트 = 0 (cluster biome으로 렌더러 fallback)
float[]    clusterHillThresholds = new float[clusterCount];
float[]    clusterSpawnRates = new float[clusterCount];
for (int i = 0; i < clusterCount; i++) {
    BiomeType cb = chunk.clusterBiomes[i];
    clusterHillThresholds[i] = BiomeTable.GetHillThreshold(cb);
    clusterSpawnRates[i] = _monsterSpawnRate * BiomeTable.GetMonsterSpawnMultiplier(cb);
}

// 셀 루프 — cluster 인덱스로 룩업
for (int x = 0; x < chunkSize; x++)
for (int y = 0; y < chunkSize; y++)
{
    int clusterIdx = (y / BiomeNoise.CLUSTER_SIZE) * clustersPerSide + (x / BiomeNoise.CLUSTER_SIZE);
    float hillThreshold = clusterHillThresholds[clusterIdx];
    float spawnRate     = clusterSpawnRates[clusterIdx];
    // ... Hill/Spawn flag 결정 후 CombineTileData(0UL, 0u, hillFlag, monsterSpawnFlag)
}
```

**성능**:
- 청크당 cluster biome 샘플 16회 × Perlin 5회 = **80 Perlin** (v1.7 옵션 Y의 1/4, v1.8 region 단일의 16배지만 절대량 무해)
- 60fps 한 프레임의 ~0.1% — 무해

| 바이옴 | Ground sprite (cluster biome) | Hill 임계 보정 | MonsterSpawn 확률 |
|--------|------------------------------|---------------|-----------------|
| Plains | ThemeTileSet_Plains.Ground | 기본 | 기본 |
| Forest | ThemeTileSet_Forest.Ground | 기본 | 약간 ↑ |
| Desert | ThemeTileSet_Desert.Ground | ↓ | 기본 |
| Swamp | ThemeTileSet_Swamp.Ground | ↓↓ | ↑ |
| Steppe | ThemeTileSet_Steppe.Ground | 기본 | 기본 |
| Tundra | ThemeTileSet_Tundra.Ground | 기본 | ↓ |
| Snow | ThemeTileSet_Snow.Ground | 기본 | ↓ |
| Mountain | ThemeTileSet_Mountain.Ground | ↑↑ | ↑ |

→ **v1.10**: 절차 생성의 ground sprite는 전적으로 cluster biome의 `ThemeTileSet.Ground`로 결정. 청사진(`.bytes`)이 깐 셀만 `GroundLayerMask` 비트에 `GroundTileSet` 인덱스를 기록 (절대 sprite 지정, 환경 독립).

### 4.5 ThemeTileSet 통합 + GroundTileSet 분리 (v1.9 Option A → v1.10)

> **v1.9 정정**: 기존 `TileSet[]` (TileType-indexed 배열) 방식이 "Desert ThemeTileSet에 Snow 슬롯이 있는" 어색한 결합을 만들어, **Option A**로 리팩토링.
> **v1.10 정정**: 청사진(`.bytes`)의 ground 인덱스 → sprite 복원이 `_themeTileSet.TileSet[]`에 의존해 컨텍스트마다 결과가 달라지는 문제를 해결하기 위해 `TileSet[]`을 폐기하고 글로벌 `GroundTileSet` 도입. `TileType` enum / `BiomeTable.GetGroundTileType` / `MapManager.GetTileTypeAt` / `_ruleTile` 모두 폐기됨.

**ThemeTileSet 구조 (v1.10)** ([ThemeTileSet.cs](../../Assets/Scripts/Map/Tile/ThemeTileSet.cs)):
```csharp
public class ThemeTileSet : ScriptableObject
{
    public string themeName;
    public Sprite themeIcon;
    public TileBase Ground;                  // 이 바이옴의 바닥 타일 1개 (cluster biome 렌더용)
    [Obsolete] public TileBase[] ObjectSet;  // legacy (별도 마이그레이션 예정)
    public TileBase[] NpcSet;
}
```
→ v1.10에서 `TileSet[]` 필드 제거됨.

**GroundTileSet 구조 (v1.10 신규)** ([GroundTileSet.cs](../../Assets/Scripts/Map/Tile/GroundTileSet.cs)):
```csharp
[CreateAssetMenu(menuName = "ARPG/Ground Tile Set")]
public class GroundTileSet : ScriptableObject
{
    public TileBase[] tiles;   // index = Ground Tile Id. tiles[0] = null (passthrough)
}
```
- 자산 위치: `Assets/Art/Tilemap/Tiles/Ground/GroundTileSet.asset`
- 청사진(`.bytes`) 디자이너가 깐 `CustomTile._customData` = 이 자산의 인덱스 = 청크 비트의 `GroundLayerMask`
- 모든 종족/콘텐츠가 한 자산에서 ID로 룩업 → 환경 독립적, sprite 손실 없음

**MapManager 인스펙터** ([MapManager_Renderer.cs:17-24](../../Assets/Scripts/Map/MapManager_Renderer.cs#L17-L24)):
```csharp
[SerializeField] private ThemeTileSet _themeTileSet;          // 폴백 (BiomeThemeEntry 미설정 시)
[SerializeField] private BiomeThemeEntry[] _themeEntries;     // 바이옴별 매핑
[SerializeField] private GroundTileSet _groundTileSet;        // 청사진 ground 인덱스 룩업 (v1.10)

[Serializable]
public struct BiomeThemeEntry { public BiomeType Biome; public ThemeTileSet TileSet; }
```

**렌더 로직 (v1.10 단일 분기)** ([MapManager_Renderer.cs](../../Assets/Scripts/Map/MapManager_Renderer.cs)):
- 청크 렌더 시 cluster당 `ResolveThemeForBiome(chunk.clusterBiomes[i])` 호출하여 `_clusterThemes[16]` 캐시
- 셀 루프 분기:
  - `idx > 0` && `GroundTileSet.tiles[idx]` 유효 → `tiles[idx]` 그리기
  - 그 외 (`idx == 0` / 자산 미할당 / 범위 밖 / null 슬롯) → cluster biome의 `theme.Ground`로 fallback
- `TileType.Ground/None` 별도 분기 폐기 — 모두 단일 룩업으로 흡수

**누락 타일 진단 로그** ([MapManager_Renderer.cs](../../Assets/Scripts/Map/MapManager_Renderer.cs)):
- `theme == null` (매핑 + 폴백 모두 없음): biome별 1회 LogError
- `theme.Ground == null` (필드 비었음): biome별 1회 LogError
- `_groundTileSet == null` && 청사진이 ground 비트 사용 시: 세션 1회 LogError + cluster biome으로 fallback
- HashSet dedupe로 스팸 방지, `MapManager.Reset()`에서 `ClearMissingTileLog()` 호출

**격자감 평가** (cluster grain = 2 타일):
- v1.9 cluster grain(2 타일)이 캐릭터 2배 크기 → 자연스러운 변화
- 시각 부족 시 보강 옵션 (선택사항):
  - **A1 색조 variant** — 같은 biome 안에서 자연스러운 다양성
  - **A2 transition tile** — cluster 경계 dithering/transition (Don't Starve 식)

### 4.6 부패도(Wasteland) bias — 폐지

> v1.4 정정: AmbientTag 시스템 자체가 폐지됨(§12 #12). 부패도 bias도 함께 폐지. `BiomeType.Wasteland`는 enum에 남겨두되 현재 어디서도 결정되지 않음. 향후 Live Backbone 도입 시 다시 설계.

---

## 5. Region — 좌표 격자 헬퍼 + Biome Histogram

### 5.1 정의

**Region = 4×4 청크(=32×32 타일)를 묶는 *좌표 격자***. ECS entity 없음 — `RegionCoord` static 헬퍼 + `RegionData` lazy-cache (v1.9 신규).

**역할** (v1.9):
1. **biome histogram 단위** — 256개 cluster의 biome count → `DominantBiome` 또는 `PickWeightedBiome(hash)` 제공 (race/settlement 결정용)
2. lore fragment 결정론 ID grouping (`fragmentId = regionId * 1000 + index`)
3. Race Settlement 배치 단위

```csharp
public static class RegionCoord
{
    public const int CHUNKS_PER_REGION = 4;

    public static Vector2Int ChunkToRegion(Vector2Int chunkCoord)
        => new Vector2Int(chunkCoord.x >> 2, chunkCoord.y >> 2);

    public static int ToRegionId(Vector2Int regionCoord)
        => regionCoord.x * 100000 + regionCoord.y;

    public static int ChunkToRegionId(Vector2Int chunkCoord)
        => ToRegionId(ChunkToRegion(chunkCoord));
}
```

### 5.1.1 RegionData (v1.9 신규)

```csharp
public class RegionData
{
    public Vector2Int Coord;
    public Dictionary<BiomeType, int> BiomeStats;    // cluster count 집계
    public BiomeType DominantBiome;                   // 최다 count (동률 시 enum 작은 값)
    public int TotalClusters;

    public static RegionData Compute(Vector2Int regionCoord, int chunkSize, int mapSeed);
    public BiomeType PickWeightedBiome(uint hash);   // histogram 가중 결정론 선택
}
```

**저장 위치** ([MapManager.cs](../../Assets/Scripts/Manager/MapManager.cs)):
- `Dictionary<Vector2Int, RegionData> _regionData` — lazy cache, `Reset()` 시 clear
- `MapManager.GetOrCreateRegionData(regionCoord)` — 첫 호출 시 256 cluster 샘플 → 캐시
- `MapManager.GetRegionDataForChunk(chunkCoord)` — chunk 좌표 → region 변환 후 위 호출

**사용 (race 결정 예)**:
```csharp
RegionData rd = AR.s.Map.GetRegionDataForChunk(chunkCoord);
BiomeType raceBiome = rd.DominantBiome;   // 또는 rd.PickWeightedBiome(hash) — Snow 12 + Plains 4 → 75% Snow
int raceId = BiomeRaceTable.PickRace(raceBiome, hash);
```

- **맵 경계**: [-98, 98] 청크 → Region 좌표 ~[-24, 24], 약 49×49 = 2401 region 후보. 활성 region만 캐시 (Lazy) — 메모리 무해 (region당 Dictionary ~수십 바이트).

### 5.2 폐지된 것들 — RegionComponent + AmbientTag

| 항목 | 상태 | 결정 |
|------|------|------|
| `RegionComponent` ECS struct | 폐지 | v1.3 §12 #11 |
| `AmbientTag` 시스템 (Dictionary, RegionAmbientData) | 폐지 | v1.4 §12 #12 |
| v1.8 region 단일 biome (`chunkBiome` = region biome) | 정정 (v1.9) | cluster-grained로 전환, `chunkBiome`은 chunk 중심 cluster 대표값 |

→ **v1.9에서 `RegionData` 부활** (Histogram 한정, ECS는 아님). 좌표 변환 static 함수 + 데이터 캐시 클래스만 존재. 향후 (Live Backbone 등) 필요 시 확장.

Phase 0에서 작성된 [`RegionComponent`](../../Assets/Scripts/Common/Component/History/RegionComponent.cs)는 **Phase 1에서 제거**.

### 5.3 청크 ↔ Region 변환 — 용도

```csharp
// lore fragment 결정론 ID 생성 시:
int regionId = RegionCoord.ChunkToRegionId(chunkCoord);
// → fragmentId = regionId * 1000 + index (§6.4)
```

→ 좌표 변환의 **유일한 용도가 lore fragment grouping** (한 region 안 0~3 fragment 자연 분포). 그 외 시스템은 region 모름.

---

## 6. 신규 — 유적 단편 (Lore Fragment) 시스템

### 6.1 핵심 모델

**유적 = "어떤 종족이 / 어떤 운명을 / 어떤 바이옴에서" 단편 3태그 + 비주얼 + LLM 글귀 1줄**

| 차원 | 예시 |
|------|------|
| race_origin | dwarf, elf, human, orc, ancient, monster |
| fate_keyword | burned, plagued, betrayed, sealed, forgotten, drowned |
| biome | mountain, forest, desert, ... (region biome 그대로) |

→ 3개의 작은 enum (또는 비트 태그)으로 충분. 별도 actor 인스턴스(드워프 종족 entity) **불필요**. 사건 템플릿 / 인과 사슬 / 시대 진행 **불필요**.

### 6.2 LoreFragmentComponent (ECS)

```csharp
public struct LoreFragmentComponent
{
    public int FragmentId;          // 결정론 ID. regionId * 1000 + slotIndex 식
    public Vector2Int Coord;        // 월드 타일 좌표 (결정론 — DeterministicCoord)
    public BiomeType TileBiome;     // 이 fragment 좌표 타일의 actual biome (캐시 — ComputeTileBiome 호출 절약)
    public int RaceOrigin;          // enum 값. TileBiome 기준 BiomeRaceTable에서 선택
    public int FateKeyword;         // enum 값
    public LoreFragmentLevel Level; // L1(마커) / L2(소구조) / L3(청사진)
    public int VariantIndex;        // L2/L3일 때 어느 패턴 선택했는지
    public LoreFragmentState State; // Pristine / Discovered / Inscribed
}

public enum LoreFragmentLevel { L1Marker = 0, L2SmallStructure = 1, L3Blueprint = 2 }
public enum LoreFragmentState { Pristine = 0, Discovered = 1, Inscribed = 2 }
```

**필드 의미 (v1.4)**:
- `FragmentId`만 결정론 ID로 유지. **`RegionId` 필드 제거** — AmbientTag 폐지로 region을 참조할 시스템 없음. fragmentId 안에 regionId가 이미 인코딩됨(`fragmentId / 1000 = regionId`).
- `TileBiome`은 *이 fragment 좌표 타일의 actual biome*. 매번 `ComputeTileBiome` 호출하지 않게 캐시. race_origin/LLM prompt 등에서 사용.

**유의어 정리**: 백본([archive](../_archive/PROCEDURAL_HISTORY_BACKBONE.md))의 `SettlementComponent` / `HistoryActorComponent`를 **이 단순 모델로 흡수**. Live Backbone(향후) 도입 시점에 `HistoricalEventComponent`만 따로 부활.

### 6.3 비주얼 레벨

| 레벨 | 표현 | 청크 점유 | 데이터 |
|------|------|----------|--------|
| **L1 Marker** | 단일 타일에 비석/유물 1개 | 1×1 | `BuildableItemTable.Id` 직접 |
| **L2 Small Structure** | 폐허 잔해, 작은 무너진 벽 | 2×2~3×3 코드 패턴 | 하드코딩 패턴 배열 |
| **L3 Blueprint** | 큰 폐허, 무덤, 도서관 | 5×5+ | `.bytes` 청사진 (BaseTown과 같은 포맷) |

→ 백본 [archive/PROCEDURAL_HISTORY_BACKBONE.md §6.6.4](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)의 L1~L3 표를 그대로 채택. L4(내부 던전)는 본 문서 범위 밖 ([dungeonSystem.md](../_archive/dungeonSystem.md)).

### 6.4 결정론 배치

청크 활성화 시 그 청크가 속한 region 좌표 단위로 fragment를 결정. region 자체는 entity 아님 — 좌표만으로 결정론 동작.

```csharp
// LoreFragmentPlacer — 청크 활성화 hook에서 호출
public void OnChunkActivated(Vector2Int chunkCoord)
{
    int regionId = RegionCoord.ChunkToRegionId(chunkCoord);

    // 이미 이 region에서 처리됐는지 확인 (한 region의 다른 청크들이 먼저 활성화됐을 수 있음)
    if (_processedRegions.Contains(regionId)) return;
    _processedRegions.Add(regionId);

    // 1) 결정론 fragment 개수
    ulong regionSeed = SplitMix64((ulong)regionId * CHANNEL_LORE_COUNT * (ulong)AR.s.Map.mapSeed);
    int count = (int)(regionSeed % 4);   // 0~3개

    for (int i = 0; i < count; i++)
    {
        int fragmentId = regionId * 1000 + i;
        ulong h = SplitMix64(regionSeed ^ (ulong)i * 0xC0DE);

        // 2) 결정론 좌표 — region 64×64 타일 어딘가 한 셀
        Vector2Int coord = DeterministicCoord.InRegion(regionId, fragmentId, AR.s.Map.mapSeed);

        // 3) 그 좌표 타일의 actual biome (per-tile)
        BiomeType tileBiome = ComputeTileBiome(coord.x, coord.y);

        // 4) biome 기반 race_origin 선택. 보정 X — 그 타일의 actual biome 그대로 사용
        int raceOrigin = BiomeRaceTable.PickRace(tileBiome, h);
        int fateKeyword = (int)(SplitMix64(h ^ 0x1A) % NumFates);
        LoreFragmentLevel level = WeightedPickLevel(h);   // L1 70% / L2 25% / L3 5%

        // 5) entity 생성
        int entityId = EntityIdHelper.CreateEntity();
        AR.s.Component.AddComponent(entityId, new LoreFragmentComponent {
            FragmentId  = fragmentId,
            Coord       = coord,
            TileBiome   = tileBiome,
            RaceOrigin  = raceOrigin,
            FateKeyword = fateKeyword,
            Level       = level,
            VariantIndex = (int)(SplitMix64(h ^ 0xFE) % VariantCount(level)),
            State       = LoreFragmentState.Pristine,
        });

        // 6) WorldData에서 동적 상태 복원
        RestoreFragmentState(entityId, fragmentId);
    }
}
```

`DeterministicCoord.InRegion`은 [DeterministicCoord.cs](../../Assets/Scripts/Map/Region/DeterministicCoord.cs) SplitMix64 구현 그대로. 이미 구현됨(설계 기록: [archive §6.4](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)).

**v1.3 변경점**:
- ~~region.Biome 기반 race_origin~~ → **fragment 좌표 타일의 actual biome** 기반 (한 region 안 여러 biome 자연 처리)
- ~~`AdjustToMatchBiome` 보정~~ → **불필요**. 그 타일의 biome 그대로 사용
- ~~region entity 활성화 hook~~ → **청크 활성화 hook**. region이 entity 아님 → "이 청크가 속한 region 처음 본 거면 fragment 결정" 로직

**결과**: 한 region(8×8 청크)이 Forest 60% / Plains 30% / Swamp 10%로 섞여 있으면, fragment들도 각 좌표 타일의 actual biome에 맞춰 *자연스럽게 분포*. Forest 영역에는 Forest 종족 유적, Swamp 영역에는 Swamp 종족 유적 자동 분배.

### 6.5 Monster Pool ↔ Biome 연결

[MONSTER_SPAWN_DESIGN.md](../ai_monster/MONSTER_SPAWN_DESIGN.md)의 spawn pool 시스템과 본 문서의 per-tile biome을 잇는 부분.

> **구현 현황 (2026-07-24)** — 아래 §6.5.3의 `BiomeMonsterTable`(가중치 포함 별도 테이블)은 **미구현**이다.
> 대신 **`MonsterTable.Biome`을 풀 키로 직접 쓰는 경량 방식**이 구현되었다: `BiomeType.Any`=공용(모든 biome 풀),
> 특정 biome=그 biome 전용. 청크 biome의 풀 = `공용 ∪ biome전용`에서 그룹별 균등 샘플, 풀 비면 인스펙터 fallback.
> 상세·코드 앵커는 [MONSTER_SPAWN_DESIGN.md §7.2.1](../ai_monster/MONSTER_SPAWN_DESIGN.md). §6.5.2 청크 biome 결정(`ComputeChunkBiome`)은
> 그대로 사용된다. §6.5.3~6.5.4(가중치 테이블·WeightedPick)는 Phase 2~3 확장 목표로 유지.

#### 6.5.1 결정 단위

| 단위 | 선택 이유 |
|------|---------|
| **per-tile** | 너무 잘게 — 한 청크 안 몬스터가 셀마다 종이 달라지면 산만 |
| **per-chunk (다수결)** | 청크당 64타일의 다수결 biome으로 pool 선택. 한 청크는 한 종 집단 |
| ~~per-region~~ | biome 단위가 region 아니므로 부적합 |

→ **per-chunk 다수결 채택**. 청크 1개 = 한 몬스터 군집의 자연스러운 크기.

#### 6.5.2 청크 biome 결정

```csharp
// MapManager_Generator의 GenerateChunkData 끝부분 또는 신규 헬퍼
public static BiomeType ComputeChunkBiome(MapChunkData chunk)
{
    // 청크 중심 좌표 1점 샘플로 시작 — Phase 1에서는 단순
    int cx = chunk.chunkX * 8 + 4;
    int cy = chunk.chunkY * 8 + 4;
    return ComputeTileBiome(cx, cy);
    // 더 정확한 다수결이 필요하면 64타일 전수 조사 후 가장 많은 biome 반환
}
```

→ Phase 1은 **청크 중심 1점**, Phase 후반 시각 평가 후 다수결로 보강 옵션.

#### 6.5.3 BiomeMonsterTable

```csharp
// Assets/Scripts/Tables/BiomeMonsterTable.cs (POCO)
public class BiomeMonsterTable
{
    public int Id;
    public BiomeType Biome;
    public int[] MainMonsterIds;     // 메인 종 후보 (weight = MainWeight 합)
    public int[] SubMonsterIds;      // 서브 종 후보
    public int[] MainWeights;        // 메인 종별 가중치
    public int[] SubWeights;
    public int SpawnRateMultiplier;  // 100 = 기본. 200 = 2배
}
```

→ 기존 MonsterManager의 인스펙터 박힌 `_mainMonsterTableId / _subMonsterTableId` 두 개를 **biome별로 다중화**. MONSTER_SPAWN_DESIGN.md Phase 2~3과 자연 연결.

#### 6.5.4 MonsterManager 통합

```csharp
// MonsterManager가 청크 활성화 시 호출 (또는 System_MonsterSpawn에서)
public void SpawnMonstersInChunk(Vector2Int chunkCoord, MapChunkData chunk)
{
    BiomeType chunkBiome = ComputeChunkBiome(chunk);
    BiomeMonsterTable table = AR.s.Data.GetBiomeMonster(chunkBiome);

    foreach (var spawnPos in chunk.monsterSpawnPositions)
    {
        int monsterId = WeightedPick(table.MainMonsterIds, table.MainWeights);
        SpawnMonster(monsterId, spawnPos);
    }
}
```

→ **biome → spawn pool → 실제 몬스터** 깔끔 연결. Phase 1 작업 범위에 포함.

### 6.6 청크 생성과의 통합

`GenerateChunkData`에 신규 단계 추가:

```
1. Zone 계산
2. Perlin 지형 (바이옴별 변형 — §4.4)
3. OverlayMapFileData(chunk)          [기존, BaseTown 등]
4. OverlayLoreFragments(chunk)         [NEW] — L1/L2/L3 모두 처리
5. OverlayTileModifications(chunk)    [기존, 마지막 — 플레이어 행동이 유적을 덮어쓸 수 있음]
6. monsterSpawnPositions 수집
```

**순서 핵심**: 유적이 TileModifications보다 *먼저* — 플레이어가 유적 위에 침대 놓으면 침대가 보여야 한다.

### 6.7 `_tileModifications` 우회

유적은 `MapManager.PlaceObject()` **호출 X**. `OverlayLoreFragments` 안에서 `chunk.tiles[lx, ly]` 비트만 직접 갱신.

```csharp
// L1 마커 예시:
ulong cur = chunk.tiles[lx, ly];
cur &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
cur |= ((ulong)markerObjectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask;
cur |= (ulong)GlobalEnum.TileFlag.Blocked;
cur &= ~(ulong)GlobalEnum.TileFlag.MonsterSpawn;
chunk.tiles[lx, ly] = cur;
```

→ [`SaveTileModifications`](../../Assets/Scripts/Manager/MapManager.cs#L454)은 `_tileModifications`만 직렬화. 유적은 결정론 재생성. 세이브 크기 **0바이트** 추가.

### 6.8 L3 청사진 공유 헬퍼

BaseTown의 `OverlayMapFileData`와 L3 청사진의 적용 로직이 같음 → 헬퍼 추출:

```csharp
// 신규: OverlayBlueprintAt(chunk, blueprint, worldOrigin)
//   - 기존 OverlayMapFileData 본문([MapManager_Generator.cs:145-194])의 bounding box 겹침
//     로직을 추출
//   - BaseTown은 mapFileData.StartPosition을 origin으로
//   - L3 fragment는 fragment.Coord를 origin으로
```

→ Phase 5(아래)에서 작업. 즉시는 아님.

---

## 7. Race Settlement (살아있는 종족 거점)

### 7.1 개념

**Race Settlement = 절차 생성된 *살아있는* 종족 거점**. §6 LoreFragment(*죽은 흔적*)와 대조되는 *현재 활동 중인* 마을/요새. NPC가 살고, 거래/대화/퀘스트 대상.

| 비교 | LoreFragment (§6) | Race Settlement (§7) |
|------|-----------------|--------------------|
| 상태 | 죽음 / 폐허 | 살아있음 |
| NPC | 없음 | 있음 (`NpcManager` 관리) |
| 상호작용 | 조사 → LLM 글귀 | 대화, 거래, 퀘스트 |
| 컴포넌트 | LoreFragmentComponent (신규) | **없음** — 기존 `VillageManager` 활용 |
| 청사진 | L1~L3 (race × fate × biome) | race × size 종류 (`.bytes`) |
| 라이프사이클 | 청크 활성/비활성마다 entity 재생성 | 청크 활성화 시 NPC 스폰 (기존 인프라) |

### 7.2 통합 모델 — BaseTown 청사진 + VillageManager 확장 (M1)

**기존 시스템을 그대로 확장**. 새 ECS 컴포넌트 / 매니저 / save 데이터 추가 X.

```
[현재 — BaseTown 1개]
LoadFixedMapFiles():
  _mapFileDataDic[BaseTown.StartPos] = BaseTown.bytes
    ↓ OverlayMapFileData 청크에 적용
    ↓ RegisterAllNpcsToManager → NpcManager + VillageManager (villageId 발급)

[Race Settlement 후 — N개 절차 추가]
LoadFixedMapFiles() 직후 RaceSettlementPlacer.PlaceAll():
  for each region in worldGrid:
    if 결정론 배치 → coord, race, size, variant 결정
    _mapFileDataDic[coord] = race_village_blueprint.MapFileData
  → 이후 흐름 그대로 (OverlayMapFileData, RegisterAllNpcsToManager, ...)
```

→ **신규 코드는 `RaceSettlementPlacer` 1개 클래스 + `RaceBiomeTable` 데이터 + N개 청사진 `.bytes` 파일**. 다른 시스템 영향 0.

### 7.3 결정론 배치 흐름 — chunk-grain 정책 (v1.9)

> **v1.9 정정**: v1.7 옵션 Y 방식 (`DeterministicCoord.InRegion` 한 점 추첨 후 그 좌표 biome 사용)은 *settlement footprint(8×8+)가 단일 cluster(2×2) 정보만으로 결정되어* footprint 영역이 다른 biome cluster에 걸치는 문제 발생. v1.9에서 **chunk-grain** 채택: region race는 dominant biome으로, settlement 배치는 race 호환 chunk(8×8) 단위로.

**알고리즘** ([RaceSettlementPlacer.cs](../../Assets/Scripts/Map/Race/RaceSettlementPlacer.cs)):

```csharp
public static class RaceSettlementPlacer
{
    public static bool TryPlace(Vector2Int regionCoord, int mapSeed, int chunkSize, out SettlementPlacement placement)
    {
        // 1) Region histogram → dominant biome (v1.9 cluster 통계)
        RegionData rd = AR.s.Map.GetOrCreateRegionData(regionCoord);
        BiomeType dominant = rd.DominantBiome;

        // 2) race 선택 — Region dominant biome 기반 (lazy compute + cache)
        ulong h = DerivedSeed.Derive(seedKey(regionCoord, mapSeed), DerivedSeed.ChannelSettlement);
        RaceType? race = RaceBiomeTable.PickRace(dominant, h);
        if (race.HasValue == false) return false;   // Wasteland 등 — settlement 없음

        // 3) race 호환 chunk 후보 수집 — region 16 chunk 중 chunkBiome 매칭
        List<Vector2Int> chunkCandidates = new();
        Vector2Int regionChunkOrigin = RegionCoord.RegionChunkOrigin(regionCoord);
        for (int cx = 0; cx < RegionCoord.CHUNKS_PER_REGION; cx++)
        for (int cy = 0; cy < RegionCoord.CHUNKS_PER_REGION; cy++)
        {
            Vector2Int chunkCoord = new(regionChunkOrigin.x + cx, regionChunkOrigin.y + cy);
            BiomeType chunkBiome = AR.s.Map.GetChunkBiome(chunkCoord);
            if (RaceBiomeTable.IsCompatible(chunkBiome, race.Value))
                chunkCandidates.Add(chunkCoord);
        }
        if (chunkCandidates.Count == 0) return false;   // 호환 chunk 0개 → 그 region은 마을 없음

        // 4) hash로 chunk 1개 선택
        int chunkIdx = (int)(h % (ulong)chunkCandidates.Count);
        Vector2Int chosenChunk = chunkCandidates[chunkIdx];

        // 5) chunk 중심 좌표 — 청사진 footprint가 chunk 1개와 자연 매치
        Vector2Int worldCoord = DeterministicCoord.ChunkCenter(chosenChunk, chunkSize);

        placement = new SettlementPlacement { RegionCoord, ChunkCoord=chosenChunk, WorldCoord, Race=race.Value, ... };
        return true;
    }

    public static List<SettlementPlacement> ComputeAll(int mapSeed, int chunkSize, ...);  // 전체 맵 순회
}
```

**chunk-grain의 의미**:
- biome 결정 단위 = **cluster (2×2 타일)** — 시각 자연스러움 (v1.9 §4)
- race 결정 단위 = **Region histogram** — 문화 일관성
- settlement 배치 단위 = **chunk (8×8 타일)** — race 호환 chunk만 후보, footprint와 자연 매치

**호환 chunk 없는 region 처리**:
- 예: Plains-dominant인데 16 chunk 모두 가장자리 noise로 Snow chunk로 잡혀버린 경우 — `chunkCandidates.Count == 0`이면 settlement 없음
- 자연스러운 **배치 밀도 가중치** 효과 (§12 #15 자동 해결)

**`ComputeAll`은 게임 시작 시** [`InitializeChunkPool`](../../Assets/Scripts/Map/MapManager_Generator.cs)에서 `LoadFixedMapFiles()` 직후 호출. 결정론이라 같은 mapSeed면 항상 같은 배치. **v1.10.2 (2026-05-22) 기준 청사진 로드 + 충돌 검사 + `_mapFileDataDic` 등록까지 모두 구현 완료** (§13 Phase 2 참조).

### 7.4 RaceBiomeTable + Blueprint 명명 규칙 (v1.10.2)

```csharp
// 코드 하드코딩 — RaceBiomeTable.cs
RaceBiomeTable:
   Plains   → [Human]
   Forest   → [Elf, Human]
   Mountain → [Dwarf]
   Desert   → [Nomad, Human]
   Swamp    → [Lizardman]
   Tundra   → [Yeti, Nomad]
   Snow     → [Yeti, Dwarf]
   Steppe   → [Nomad]

청사진 명명 규칙 (Assets/_BinaryData/TilemapData/):
   BaseTown_Human.bytes
   BaseTown_Elf.bytes
   BaseTown_Dwarf.bytes
   BaseTown_Lizardman.bytes
   BaseTown_Nomad.bytes
   BaseTown_Orc.bytes
   BaseTown_Yeti.bytes
```

→ 청사진은 디자이너가 [MapFileSaver](../../Assets/Scripts/Map/MapFileSaver.cs)로 작성. **BaseTown과 동일 워크플로**.
→ **v1.10.2 결정**: 별도 `SettlementBlueprintCatalog.cs` 클래스 도입 없이 **명명 규칙 `BaseTown_{race}.bytes`**로 충분. `LoadRaceBlueprint(race)`가 직접 파일명 조합해서 로드. 향후 (race × size × variant) 다중 청사진이 필요해지면 카탈로그 클래스로 확장.

### 7.5 lifecycle — 기존 인프라 그대로

청사진이 `_mapFileDataDic`에 들어가면 *변경 없이* 기존 흐름 흐름:

| 단계 | 처리 |
|------|------|
| 청크 생성 | `OverlayMapFileData(chunk)` → 청사진 비트 자동 박힘 |
| 청크 활성화 | `OnChunkActivated` → `NpcManager.OnChunkActivated` → NPC 스폰/복원 |
| 청크 비활성화 | NPC 비활성화 (기존) |
| 세이브 | `_tileModifications`만 직렬화. settlement 자체는 결정론 재생성 → **0바이트** |
| 로드 | 재시작 시 `PlaceAll()` 재호출로 같은 배치 복원 |

→ Race Settlement는 *완전히 기존 인프라 안에서* 작동. ECS 컴포넌트 / 별도 save 시스템 / 새 hook 0개.

### 7.6 충돌 회피

| 영역 | 회피 방식 |
|------|---------|
| **BaseTown** | `_mapFileDataDic`의 기존 entry와 bounding box 비교 |
| **다른 settlement** | 이미 추가된 entry와 같은 검사 |
| **LoreFragment L3 청사진** (Phase 5 이후) | 같은 방식 — `_historicalBlueprints` entry와 비교 |
| 충돌 시 | 해당 region에서 settlement 드롭 (인접 region이 다음 차례) |

### 7.7 결정 보류 (Phase 진입 시 정리)

§12 #13~#18 참조 — Race-Biome 매핑 데이터, 마을 크기 종류, 배치 밀도, variant 개수, 충돌 우선순위, VILLAGE_GROWTH_STAGES 연계 등.

---

## 8. 결정론 보강

### 8.1 현재 문제
- `_randomGenerator = new System.Random(mapSeed)` ([MapManager.cs:39](../../Assets/Scripts/Manager/MapManager.cs#L39))
- `_randomGenerator.NextDouble()` 호출 순서가 청크 로드 순서에 의존 → **같은 청크가 두 번째로 로드되면 다른 spawn 결과**

### 8.2 해결
유적 시스템은 **모든 의사난수를 좌표 기반 hash**로 처리. 이미 정의된 `HistoryRng` (SplitMix64, 설계 기록: [archive](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)) 채널 분리 활용:

```csharp
// LoreFragment 채널
private const ulong CHANNEL_LORE_COUNT = 0xA001;
private const ulong CHANNEL_LORE_PROP  = 0xA002;
private const ulong CHANNEL_LORE_COORD = 0xA003;

int fragmentCount = (int)(SplitMix64((ulong)regionId ^ CHANNEL_LORE_COUNT * (ulong)mapSeed) % 4);
```

**기존 MonsterSpawn 결정론 결함은 별도 항목** (§13 마이그레이션). 유적과 분리해서 따로 정리.

### 8.3 PRNG 채널 목록

| 채널 | 용도 | 위치 |
|------|------|------|
| `CHANNEL_WARP_X` | per-tile biome 좌표 warp X axis | BiomeNoise |
| `CHANNEL_WARP_Y` | per-tile biome 좌표 warp Y axis | BiomeNoise |
| `CHANNEL_TEMPERATURE` | 온도 Perlin seed | BiomeNoise |
| `CHANNEL_HUMIDITY` | 습도 Perlin seed | BiomeNoise |
| `CHANNEL_ELEVATION` | 고도 Perlin seed | BiomeNoise |
| `CHANNEL_LORE_COUNT` | region별 fragment 수 | LoreFragmentPlacer |
| `CHANNEL_LORE_PROP` | fragment 속성 (race/fate/level) | LoreFragmentPlacer |
| `CHANNEL_LORE_COORD` | fragment 좌표 | DeterministicCoord |
| `CHANNEL_VARIANT` | L2/L3 variant 선택 | LoreFragmentPlacer |
| `CHANNEL_MONSTER_SPAWN` (향후) | 결정론 monster spawn | MapManager_Generator (개선) |

`DerivedSeed.cs` (Phase 0 완료분) 그대로 사용.

---

## 9. LLM 글귀 — 첫 진입 시 lazy 생성

### 9.1 흐름
1. 플레이어가 fragment에 *상호작용* (조사) → `LoreFragmentComponent.State == Pristine`이면 LLM 호출
2. 프롬프트 입력: `{ race_origin, fate_keyword, biome, fragmentId }`
3. LLM 결과 → `LoreInscriptionSaveData[fragmentId] = inscription` (`WorldData`)
4. 상태 → `Inscribed`. 다음부터는 캐시 텍스트만 표시.

### 9.2 저장 위치
`WorldData.HistorySaveData.LoreInscriptions: Dictionary<int, string>` — 백본 [archive §8.2](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)의 `HistorySaveData` 클래스를 단순화해서 흡수.

### 9.3 폴백
LLM 실패/오프라인 시 KoreanJosaResolver 기반 단순 템플릿:
- `"이곳에 {race}들의 {fate_keyword 한국어 매핑}이 있었다고 전해진다."`

### 9.4 결정 보류 사항 (§12 참조)
- 톤(Dark Souls 단편 vs 구체 단서)
- 유적 간 *느슨한* 교차 참조 허용 여부
- 시대 라벨 (Ancient/Dark) 유지 여부

---

## 10. 청크 hook 확장

[OnChunkActivated](../../Assets/Scripts/Map/MapManager_Spawner.cs#L11)에 Lore 처리 추가:

```csharp
public void OnChunkActivated(Vector2Int chunkCoord, MapChunkData chunkData)
{
    // 기존:
    if (AR.s.Monster?.HasChunkSpawned(chunkCoord) == true)
        AR.s.Monster.ActivateChunkMonsters(chunkCoord);
    AR.s.Npc?.OnChunkActivated(chunkCoord);
    AR.s.Building?.OnChunkActivated(chunkCoord);

    // 신규:
    AR.s.Lore?.OnChunkActivated(chunkCoord);   // VFX/조명 트리거 + interactable entity spawn
}
```

`LoreManager.OnChunkActivated`:
- 이 청크가 속한 region 좌표 계산 (`RegionCoord.ChunkToRegionId(chunkCoord)`)
- 해당 region의 fragment가 미생성이면 결정론 생성(§6.4)
- L1/L2/L3 마커는 이미 `GenerateChunkData`에서 타일에 박혀있음
- 추가 작업: 상호작용 entity 생성 (플레이어가 다가가 조사 → LLM 트리거 entry point)
- ~~AmbientTag → VFX/조명~~ — v1.4에서 폐지 (§12 #12)

---

## 11. 데이터 모델 요약

### 11.1 신규 ECS 컴포넌트

| 컴포넌트 | 목적 | 풀 크기 (가이드) |
|---------|------|----------------|
| `LoreFragmentComponent` (신규) | 유적 단편 | 512 (region당 ~3, 활성 region 수에 비례) |

> **`RegionComponent` 폐지** (v1.3, §12 #11) — region은 ECS entity가 아니라 *좌표 격자 + AmbientTag Dictionary*. Phase 0 작성분은 Phase 1에서 제거. ComponentManager `_poolSizes` entry 삭제.

### 11.2 신규 카탈로그 (테이블 — 옵션)

| 테이블 | 내용 | 필수? |
|--------|------|-------|
| `BiomeRaceTable` | 바이옴별 등장 가능 race_origin 풀 | ✅ |
| `FateKeywordTable` | fate_keyword id ↔ 한국어 ↔ 영문 키 | ✅ |
| `LoreMarkerTable` | L1 마커 objectId 풀 (race × fate별) | ✅ |
| `LorePatternTable` | L2 소구조 패턴 데이터 | ✅ |
| `LoreBlueprintTable` | L3 청사진 `.bytes` 키 매핑 | △ (L3 도입 시점) |

→ 처음에는 코드 하드코딩으로 시작 → 데이터 분리는 점진적으로.

### 11.3 새 enum
- `BiomeType` (§4.2)
- `RaceOrigin`, `FateKeyword`, `LoreFragmentLevel`, `LoreFragmentState`

### 11.4 WorldData 확장

```csharp
public class WorldData
{
    public ushort Version;       // ++ (마이그레이션 필요)
    // ... 기존 ...
    public LoreSaveData LoreSaveData = new();
}

[Serializable]
public class LoreSaveData
{
    public Dictionary<int, string> Inscriptions = new();   // fragmentId → LLM 텍스트
    public List<int> DiscoveredFragmentIds = new();        // 발견 상태 추적
    // ~~RegionAmbientTags~~ — v1.4에서 폐지 (§12 #12). 향후 Live Backbone 도입 시 재도입
}
```

→ `mapSeed`는 이미 `WorldData`에 있음 (확인 필요). 백본은 저장하지 않음. 발견한 fragment의 state + LLM 글귀만 직렬화.

---

## 12. 결정 보류 사항

| # | 항목 | 옵션 | 추천 |
|---|------|------|------|
| 1 | 글귀 톤 | A. 단편/추상 (Dark Souls 풍) / B. 구체 단서 | A — 호기심 자극 우선 |
| 2 | 유적 간 *느슨한* 교차 참조 | 같은 race_origin끼리 LLM이 가끔 "형제 부족" 식 언급 vs 완전 독립 단편 | 완전 독립 — 모순 위험 0 |
| 3 | 시대 라벨 유지 | A. "고대/암흑기" 모호한 시대 힌트만 / B. 시대 완전 폐기 | A — 분위기 가산점만 가져감 |
| 4 | Live 사건 → 새 유적 생성 | A. 보스 처치 자리에 비석 (Live Backbone 도입 시) / B. Live는 텍스트만, 시각 X | A는 Live Backbone 도입 후. 본 문서 범위 밖 |
| 5 | BiomeRaceTable 시드 | 코드 하드코딩 vs 시트 분리 | 하드코딩으로 시작 → 콘텐츠 늘면 시트 |
| 6 | LoreFragment의 `RaceOrigin` 표현 | enum vs string id vs 비트 태그 | enum (단순) — 변경 부담 적음 |
| 7 | 결정론적 monster spawn 보강 | 본 페이즈에서 함께 / 별도 페이즈 | 별도 (§13 Phase 5) |
| 8 | RegionLookup 위치 | `AR.s.Region` 신규 매니저 / `MapManager` 내부 / `HistoryManager` 안 | `MapManager` 내부 — region은 맵의 일부 |
| 9 | 바이옴 결정 축 (확정) | A. 온도+습도+고도 (3축) / B. + 마나 / C. + 물 근접 | **A 채택** (2026-05-19) — 마나/물근접은 후속 |
| 10 | 바이옴 렌더링 방식 (확정) | X. RimWorld식 (region 단위 단일) / Y. Minecraft식 (per-tile warped Perlin) / **Z. Cluster-grained + Region histogram** | **Z 채택** (2026-05-20, v1.9 재정정) — v1.8 옵션 X에서 region 경계(32 타일)가 시각상 칼날처럼 도드라지는 문제 발생. v1.9에서 **시각 = cluster (2×2 타일) 단위**, **race 결정 = Region histogram의 DominantBiome**으로 분리. 시각 자연스러움(옵션 Y 장점) + 문화 단위 일관성(옵션 X 장점) 모두 확보 |
| 11 | RegionComponent ECS 폐지 (확정) | D-1. 필드 + entity 폐지, AmbientTag만 Dictionary로 / D-2. 필드만 제거, entity 유지 / D-3. BiomeDistribution 가중치 | **D-1 채택** (2026-05-19) — 옵션 Y로 region이 biome 단위 아니게 됐고, 남은 역할(AmbientTag)이 ECS struct 정당화 부족. `Dictionary<int, RegionAmbientData>` 한 개로 충분 |
| 12 | AmbientTag 시스템 자체 폐지 (확정) | 유지 / **폐지** | **폐지 채택** (2026-05-19) — 최소 단위로 시작. region 단위 동적 누적은 현재 사용처가 없음 (Live Backbone Phase 7 이전에 의미 없음). 향후 필요 시 재도입. region에 관한 데이터 구조가 **0**으로 줄어듦 — `RegionCoord` static 헬퍼만 남음 |
| 13 | Race-Biome 매핑 데이터 위치 | 코드 하드코딩 / 시트 `.bytes` 테이블 | Race Settlement Phase 진입 시 (콘텐츠 양 보고 결정) |
| 14 | 마을 크기 종류 (Race Settlement) | 1단계 (Village만) / 3단계 (Village/Town/City) | Phase 진입 시 |
| 15 | 배치 밀도 (확정) | region 당 ≤1 / N region 당 1 / 청크 단위 / 기타 | **자연 가중치 채택** (2026-05-20, v1.9) — chunk-grain 알고리즘에서 race-호환 chunk 후보 풀 크기가 region마다 다름 → biome 분포가 곧 배치 밀도. Wasteland/race-호환 chunk 0개인 region은 자연스럽게 skip. 명시적 SETTLEMENT_FREQUENCY 게이트는 도입 안 함 |
| 16 | 청사진 variant 개수 | (race × size) 조합당 1~3종 / 더 다양 | 콘텐츠 작업 시 |
| 17 | LoreFragment 영역과의 충돌 회피 | 단순 bounding box / 우선순위 정의 (BaseTown > Race Settlement > LoreFragment L3) | Phase 진입 시 |
| 18 | `VILLAGE_GROWTH_STAGES.md` 시스템과 연계 | 절차 settlement도 성장? / 정적 유지? | Phase 후속 (마을 시스템 안정화 후) |

---

## 13. 마이그레이션 로드맵

각 Phase는 빌드 통과 + 동작 가능한 단위. 콘텐츠(유적 청사진, 마커 스프라이트)는 코드와 분리.

### Phase 0 — 인프라 ✅ 완료 (2026-05-19)

**활용 자산** (PROCEDURAL_HISTORY Phase 0 재활용):
- ✅ [`HistoryRng`](../../Assets/Scripts/History/Generator/HistoryRng.cs), [`DerivedSeed`](../../Assets/Scripts/History/Generator/DerivedSeed.cs), [`HistoryTagRegistry`](../../Assets/Scripts/History/Data/HistoryTagRegistry.cs)
- ✅ [`HistoryManager`](../../Assets/Scripts/History/HistoryManager.cs) 골격
- ✅ Phase 0 컴포넌트 4종(Live Backbone 예약) — `HistoryActor/Tags/HistoricalEvent/Memory`

**완료된 작업**:
- ✅ `BiomeType`, `RaceType`, `RaceOrigin`, `FateKeyword`, `SettlementSize` enum → [GlobalEnum.cs](../../Assets/Scripts/Common/GlobalEnum.cs)
- ✅ `LoreFragmentComponent` struct + `LoreFragmentLevel` + `LoreFragmentState` enum → [LoreFragmentComponent.cs](../../Assets/Scripts/Common/Component/Map/LoreFragmentComponent.cs)
- ✅ `TileType` enum 확장 (Sand=4, SwampMud=5, DryGrass=6, Snow=7)
- ✅ ComponentManager `_poolSizes`에 `LoreFragmentComponent` 등록 (512)
- ✅ 폐지 파일 제거: `RegionComponent.cs`, `SettlementComponent.cs` (§12 #11, M1 Race Settlement 통합)
- ✅ ComponentManager에서 `RegionComponent` / `SettlementComponent` 풀 등록 제거

### Phase 1 — region 단위 바이옴 + Monster pool 통합 ✅ 코드 완료 (2026-05-19, v1.8 → 2026-05-20, v1.9)

> **사용자 우선순위 (v1.4)**: 온도/습도로 biome이 잘 생성 + 거기에 맞춘 종족/몬스터 배치. LLM/state는 후순위.
> **v1.8**: per-tile (옵션 Y) → region 단위 단일 biome 회수.
> **v1.9 재정정 (2026-05-20)**: region 단일 경계감 문제 → **Cluster-grained (2×2 타일)** 시각 + **Region histogram** race 결정으로 분리.

**완료된 작업**:
- ✅ [`Map/Region/RegionCoord.cs`](../../Assets/Scripts/Map/Region/RegionCoord.cs) — 청크↔region 좌표 변환 (CHUNKS_PER_REGION = 4)
- ✅ [`Map/Biome/BiomeNoise.cs`](../../Assets/Scripts/Map/Biome/BiomeNoise.cs) — `ComputeTileBiome` + `ComputeRegionBiome` + `ComputeChunkBiome` (warped Perlin + Whittaker, region 단위 결정)
- ✅ [`Map/Biome/BiomeTable.cs`](../../Assets/Scripts/Map/Biome/BiomeTable.cs) — biome → TileType/Hill/spawn 보정 매핑
- ✅ [`Map/Biome/BiomeMonsterMapping.cs`](../../Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs) — biome → 몬스터 ID 샘플. **2026-07-24: 데이터 주도로 재작성** (`MonsterTable.Biome` 기반 `공용(Any)∪biome전용` 풀 균등 샘플, 빈 풀이면 fallback). 상세 MONSTER_SPAWN_DESIGN.md §7.2.1
- ✅ [`MapChunkData.cs`](../../Assets/Scripts/Map/MapChunkData.cs) — `chunkBiome` 필드 (region biome 캐시). ~~`tileBiomes[,]`~~ v1.8에서 제거
- ✅ `GenerateChunkData` — 청크당 `ComputeChunkBiome` 1회 호출, 모든 64 타일에 동일 biome 적용 (per-tile loop 단순화, Perlin 호출 64배 절감)
- ✅ `MapManager_Renderer` — `BiomeThemeEntry[]` 인스펙터 + `Dictionary<BiomeType, ThemeTileSet>` 청크당 1회 선택
- ✅ `MapManager.GetChunkBiome(chunkCoord)` 외부 API
- ✅ `MonsterManager` — `SpawnInitialMonstersInChunk` + `RespawnMonsterInChunk`가 biome 기반 ID 선택 (인스펙터 폴백 보존)
- ✅ `DerivedSeed` 신규 8채널 (WarpX/Y, Temperature, Humidity, Elevation, Settlement, LoreCount, LoreProp)

**v1.9 추가 작업 (2026-05-20)**:
- ✅ [`BiomeNoise.cs`](../../Assets/Scripts/Map/Biome/BiomeNoise.cs) — `CLUSTER_SIZE = 2` 상수 + `ComputeClusterBiome(clusterGridX, clusterGridY, mapSeed)` 추가. `ComputeChunkBiome`은 청크 중심 cluster 샘플로 재정의 (legacy 1-biome caller용)
- ✅ [`MapChunkData.cs`](../../Assets/Scripts/Map/MapChunkData.cs) — `clusterBiomes[16]` 배열 필드 추가
- ✅ [`MapManager_Generator.cs`](../../Assets/Scripts/Map/MapManager_Generator.cs) — cluster biome 미리 샘플 → cluster당 BiomeTable 룩업 캐시 → 셀별 baseTileType/hillThreshold/spawnRate 적용
- ✅ [`MapManager_Renderer.cs`](../../Assets/Scripts/Map/MapManager_Renderer.cs) — `_clusterThemes[16]` 캐시 + 셀 루프에서 cluster 인덱스로 즉시 룩업. 누락 타일 진단 LogError 4종 (theme/Ground/_ruleTile/None) + dedupe
- ✅ [`Region/RegionData.cs`](../../Assets/Scripts/Map/Region/RegionData.cs) **신규** — `BiomeStats` histogram + `DominantBiome` + `PickWeightedBiome(hash)`. Lazy compute (256 cluster 샘플 → 캐시)
- ✅ [`MapManager.cs`](../../Assets/Scripts/Manager/MapManager.cs) — `_regionData` Dictionary cache + `GetOrCreateRegionData(regionCoord)` + `GetRegionDataForChunk(chunkCoord)` 공개 API. `Reset()` 시 clear
- ✅ [`ThemeTileSet.cs`](../../Assets/Scripts/Map/Tile/ThemeTileSet.cs) **Option A 리팩토링** — `Ground` 단일 필드 추가, 기존 `TileSet[]`은 `[Obsolete]` (MapFileSaver 에디터 룩업 전용)
- ✅ Perlin scale ×0.5 적용 — biome 패치 크기 2배 확대 (TEMPERATURE 0.005→0.0025, HUMIDITY 0.008→0.004, ELEVATION 0.012→0.006)

**v1.10 추가 작업 (2026-05-21) — 청사진 ground sprite 손실 해결**:

문제: 청사진(`.bytes`)이 깐 sprite가 로드 시 `_themeTileSet.TileSet[]` 인스펙터 슬롯에 의존해 컨텍스트마다 결과가 달라짐 (Inspector 슬롯이 Forest 테마면 Sand 청사진도 Forest sprite로 복원됨). cluster biome도 청크 비트의 TileType을 무시하고 cluster로 재룩업해서 청사진 비트가 사실상 무의미.

해결: 청사진의 GroundLayerMask 비트를 **글로벌 `GroundTileSet` 인덱스**로 의미 재해석. cluster biome은 청크 비트가 0인 셀에서만 적용 (passthrough).

- ✅ [`GroundTileSet.cs`](../../Assets/Scripts/Map/Tile/GroundTileSet.cs) **신규** — `tiles[]` 단일 ScriptableObject. `[0]=null(passthrough)`, `[1~]=종족/콘텐츠별 ground TileBase`. Hard reference (메모리 상주, lazy 아님).
- ✅ [`MapManager_Renderer.cs`](../../Assets/Scripts/Map/MapManager_Renderer.cs) — `_groundTileSet` 슬롯 추가. None/Ground/cluster 3분기 → **단일 룩업** ((`idx>0` && valid) → `tiles[idx]`, 그 외 → cluster biome fallback). 미할당 시 LogError 1회.
- ✅ [`MapFileSaver.cs`](../../Assets/Scripts/Map/MapFileSaver.cs) — `_groundTileSet` 슬롯 추가. `ConvertUintToTile`이 GroundTileSet 룩업으로 교체 (`TileSet[]` 의존 제거).
- ✅ [`MapManager_Generator.cs`](../../Assets/Scripts/Map/MapManager_Generator.cs) — 절차 생성에서 `clusterBaseTileTypes[]` / `BiomeTable.GetGroundTileType` 제거. 청크 비트의 ground = 0 (passthrough). `CombineTileData` 시그니처 `TileType → uint`.
- ✅ [`BiomeTable.cs`](../../Assets/Scripts/Map/Biome/BiomeTable.cs) — `GetGroundTileType` 메서드 삭제.
- ✅ [`GlobalEnum.cs`](../../Assets/Scripts/Common/GlobalEnum.cs) — `TileType` enum 완전 폐기.
- ✅ [`MapManager.cs`](../../Assets/Scripts/Manager/MapManager.cs) — `GetTileTypeAt` 메서드 삭제 (호출처 0), `_ruleTile` 필드 제거 (사용처 0).
- ✅ [`ThemeTileSet.cs`](../../Assets/Scripts/Map/Tile/ThemeTileSet.cs) — `[Obsolete] TileSet[]` 필드 완전 제거. `Ground`와 `NpcSet`, `[Obsolete] ObjectSet`만 잔존.

**완료된 콘텐츠 작업 (2026-05-22)**:
- ✅ `Assets/Art/Tilemap/Tiles/Ground/GroundTileSet.asset` 자산 생성 + `tiles[]` 채우기 (`[0]=null`, `[1]=맨땅 RuleTile`, `[2~]=콘텐츠 추가 시 확장`)
- ✅ MapManager / MapFileSaver 인스펙터에 `_groundTileSet` 슬롯 할당
- ✅ AR 프리팹 MapManager 인스펙터에 `BiomeThemeEntry[]` 채우기 (biome별 ThemeTileSet) + 폴백 `_themeTileSet`
- ✅ 각 ThemeTileSet SO의 `Ground` 필드에 해당 바이옴 바닥 타일 할당

**보류된 작업**:
- ✅ ~~`BiomeMonsterMapping.cs`에 case 추가 (몬스터 종류 부족으로 보류)~~ → **2026-07-24 해소**: 하드코딩 case 대신 `MonsterTable.Biome` 데이터 주도 풀로 재작성. 몬스터가 `Any`면 모든 biome, 특정 biome이면 그 biome에만 등장. 콘텐츠(몬스터 종)만 늘리면 시트만으로 확장
- ⏸ 시각 검증 후 *필요 시* A1(색조 variant) 또는 A2(transition tile) 보강
- ⏸ Desert/Tundra 임계값 조정 (확률 3% → 10%+) — 필요 시 §4.3.1 조정

**원래 명세 (참고)**:

**신규 파일**:
- `Assets/Scripts/Map/Region/RegionCoord.cs` — static 좌표 변환 헬퍼 (`ChunkToRegion`, `ToRegionId`). ECS 아님
- `Assets/Scripts/Map/Biome/BiomeNoise.cs` — **`ComputeTileBiome(worldX, worldY)`** static 함수. domain warp + 3축 Perlin + Whittaker threshold (§4.3.1)
- `Assets/Scripts/Map/Biome/BiomeTable.cs` — POCO, 바이옴별 TileType/Hill 임계/스폰 보정 매핑 (`ApplyBiomeTile` 헬퍼용)
- ~~`Assets/Scripts/Tables/BiomeMonsterTable.cs` — POCO, biome별 main/sub 몬스터 ID 풀 + 가중치 (§6.5.3)~~ — **생성 안 함**. 별도 테이블 대신 `MonsterTable.Biome`을 풀 키로 직접 쓰는 경량 방식 채택(가중치는 Phase 2~3로 이관). 상세 MONSTER_SPAWN_DESIGN.md §7.2.1

**수정**:
- `MapManager.cs` — `BiomeThemeEntry[]` 인스펙터 (Dictionary 매핑)
- `GenerateChunkData` — **타일마다** `ComputeTileBiome(wx, wy)` 호출 → `ApplyBiomeTile(chunk, lx, ly, biome)` (TileType, Hill 임계, MonsterSpawn 확률 분기)
- `GlobalEnum.TileType` — Sand(4), SwampMud(5), DryGrass(6), Snow(7) 추가 (10비트 여유 충분)
- `MapManager_Renderer` — 타일 GroundLayer 값 → 적절한 `ThemeTileSet` 선택 (Dictionary 매핑)
- `DerivedSeed` — `CHANNEL_WARP_X/Y / TEMPERATURE / HUMIDITY / ELEVATION` 5개 채널 추가
- `ComponentManager` — Phase 0에서 등록한 `RegionComponent` 풀 제거 (§12 #11)
- `MonsterManager` — 청크 활성화 시 `ComputeChunkBiome(chunk)` → `BiomeMonsterTable` 조회 → 풀에서 spawn ID 결정 ([MONSTER_SPAWN_DESIGN.md] Phase 2 연계)
- `DataManager_Table` — BiomeMonsterTable 로딩 추가

**삭제 대상** (Phase 0 작성분):
- [RegionComponent.cs](../../Assets/Scripts/Common/Component/History/RegionComponent.cs) — 폐지

**검증**:
- 청크 생성 시 `[Biome] tile(wx,wy) T=0.42 H=0.71 E=0.55 → Forest` 샘플 로그
- 같은 `mapSeed`에서 모든 타일의 `ComputeTileBiome` 결과 100% 재현
- **시각적으로 격자 정렬 없음** — biome 경계가 자연스러운 곡선/구불구불 형태
- 한 region(8×8 청크) 안에 다른 biome 패치가 자연스럽게 섞임
- Whittaker 7개 바이옴 + Mountain 모두 적어도 한 번씩 등장하는지 시드 5개 샘플링
- **monster spawn 확인**: Forest 청크 진입 시 Forest 풀의 몬스터, Desert 청크 진입 시 Desert 풀의 몬스터 (인스펙터 박힌 단일 ID 안 씀)
- `RegionCoord.ChunkToRegionId((23, 41))` = `RegionCoord.ToRegionId((2, 5))` 변환 일관성 단위 테스트

### Phase 2 — Race Settlement 절차 배치 ✅ 코드 완료 (2026-05-22, v1.10.2)

> **사용자 우선순위 (v1.5)**: biome 생성 직후 종족별 살아있는 거점이 맵에 나타나야 함.
> **v1.10.2 (2026-05-22) 완료**: 알고리즘/배치 결정 + 청사진 로드 + 충돌 검사 + `_mapFileDataDic` 등록까지 모두 코드 구현 + 청사진 7종 작성됨. 별도 `SettlementBlueprintCatalog.cs` 클래스 도입 없이 명명 규칙 `BaseTown_{race}.bytes`로 해결.

**완료된 작업 (2026-05-20, v1.9 골격)**:
- ✅ [`Map/Region/DeterministicCoord.cs`](../../Assets/Scripts/Map/Region/DeterministicCoord.cs) — Region/Chunk 안 결정론 좌표 (`InRegion` / `InChunk` / `ChunkCenter` / `HashFor`)
- ✅ [`Map/Race/RaceBiomeTable.cs`](../../Assets/Scripts/Map/Race/RaceBiomeTable.cs) — biome → race 호환 매핑 (코드 하드코딩, 8 biome × 1~2 race)
- ✅ [`Map/Race/RaceSettlementPlacer.cs`](../../Assets/Scripts/Map/Race/RaceSettlementPlacer.cs) — **chunk-grain 알고리즘** (§7.3). `TryPlace(regionCoord)` + `ComputeAll(mapSeed)` 정적 메서드
- ✅ `DerivedSeed.ChannelSettlement` (Phase 0에서 미리 예약된 채널 활용)
- ✅ [`MapManager_Generator.cs`](../../Assets/Scripts/Map/MapManager_Generator.cs) `InitializeChunkPool()`에서 `LoadFixedMapFiles()` 직후 `PlaceRaceSettlements()` 호출

**v1.10.2 추가 완료 (2026-05-22)**:
- ✅ Race-specific 청사진 7종 작성: `BaseTown_Human/Elf/Dwarf/Lizardman/Nomad/Orc/Yeti.bytes` ([Assets/_BinaryData/TilemapData/](../../Assets/_BinaryData/TilemapData))
- ✅ [`MapManager_Generator.LoadRaceBlueprint(race)`](../../Assets/Scripts/Map/MapManager_Generator.cs) — 명명 규칙 `BaseTown_{race}.bytes`로 파일 로드. 파일 없으면 LogError + skip. **별도 `SettlementBlueprintCatalog.cs` 클래스 불필요** (명명 규칙으로 충분)
- ✅ [`MapManager_Generator.HasMapFileOverlap(blueprint)`](../../Assets/Scripts/Map/MapManager_Generator.cs) — 기존 `_mapFileDataDic` 엔트리들과 bounding box 충돌 검사. 겹치면 skip
- ✅ `PlaceRaceSettlements`가 청사진 origin을 settlement chunk 중심으로 이동(`newOrigin = WorldCoord - size/2`) 후 `_mapFileDataDic[newOrigin] = blueprint` 등록 → 이후 `OverlayMapFileData`가 자동 적용
- ✅ 통계 로그: `[MapManager] Settlement 배치 결과: 적용=N / 청사진없음=N / 충돌스킵=N`

**적용 콘솔 로그 형식**:
```
[MapManager] Race Settlement 계산 완료: N개 region에 배치
  [Settlement] region=(-12,3) dominant=Plains chunkBiome=Plains → Race=Human Size=Village chunk=(-48,15) origin=(-388,116) 16x16
  ...
[MapManager] Settlement 배치 결과: 적용=N / 청사진없음=0 / 충돌스킵=N
```

**검증**:
- ✅ 같은 `mapSeed`에서 settlement 좌표/race/size 100% 재현
- ✅ biome dominant ↔ race 매핑 의도대로 작동 (Plains-dominant → Human, Forest-dominant → Elf/Human, ...)
- ✅ 호환 chunk 없는 region 스킵 (Wasteland-dominant region)
- ✅ chunk 분포 균등성 (region마다 다른 chunk 선택)
- ✅ 충돌 시 skip 동작 (BaseTown 영역 / 다른 settlement와 겹침)
- 시각 검증 (게임 안 실제 마을 확인) — 이후 단계

**Phase 2 종료 (2026-05-22, v1.10.3)** — 아래 후속 항목은 별도 트랙으로 분리. Phase 2 자체는 완료.

### Phase 2.x — Race Settlement 후속 트랙 ⏸ 보류 (추후 작업)

> 사용자 결정 (2026-05-22): Phase 2 코드 완료 시점에서 후속 디테일은 별도 트랙으로 미룸. 우선순위 낮음, 필요 시 재개.

- ⏸ **size variant** — 현재 race당 1종 청사진만. `Village/Town/City` 다단계 또는 `_small_01/_small_02` variant 도입. §12 #14, #16 결정 필요
- ⏸ **NPC race 통합** — 청사진의 NPC 슬롯이 race별 NPC 종으로 자동 매핑되는지 검증. `RegisterAllNpcsToManager` 흐름에 race 정보 흘리기
- ⏸ **§12 #13~#18 결정 보류 사항** — 마을 크기 종류 / variant 개수 / 충돌 우선순위 / VILLAGE_GROWTH_STAGES 연계

### Phase 3 — 유적 단편 배치 (2~3일)
**신규 파일**:
- `Assets/Scripts/Map/Lore/LoreFragmentPlacer.cs` — region 활성화 시 fragment 결정
- `Assets/Scripts/Map/Lore/LoreManager.cs` (또는 `HistoryManager` 안에 흡수) — `AR.s.Lore`
- `Assets/Scripts/Map/Lore/BiomeRaceTable.cs` (하드코딩 OK 단계)

**수정**:
- `MapManager_Generator.cs` — `OverlayLoreFragments(chunk)` 신규 단계 추가 (3과 4 사이)
- `OnChunkActivated` — Lore hook 추가

**검증**:
- 시드 12345에서 region (3, 7) fragment 좌표 100% 재현
- 청크 unload → reload 시 같은 위치에 같은 fragment
- `SaveTileModifications`에 백본 유적 entry 0개

### Phase 4 — L1 마커 비주얼 + 상호작용 (2~3일)
**신규**:
- L1 마커용 `BuildableItemTable` 행 (비석, 깨진 검, 백골) + Addressable 등록
- 상호작용 entity (조사 가능한 lore stub)
- LLM 호출 stub → 첫 진입 시 콘솔에 prompt만 출력 (실제 호출은 Phase 6)

**수정**:
- `LoreFragmentPlacer` — L1만 처리 (L2/L3는 다음 Phase)

**검증**: 게임 안에서 청크 진입 시 비석 visible, 조사 시 콘솔에 LLM prompt 출력

### Phase 5 — L2/L3 + 청사진 헬퍼 + 결정론 monster spawn 보강 (3~4일)
**신규**:
- L2 패턴 (3×3 폐허 잔해 등) — 코드 배열
- L3 청사진 `.bytes` 파일 2~3개 (placeholder, MapFileSaver로 생성)
- `OverlayBlueprintAt(chunk, blueprint, origin)` 헬퍼 — 기존 `OverlayMapFileData` 본문 추출
- 결정론 monster spawn — `_randomGenerator` 호출을 좌표 hash로 교체

**수정**:
- `OverlayMapFileData` → 새 헬퍼 호출로 리팩토링
- `MapManager_Generator` — `_randomGenerator` 분리

### Phase 6 — LLM 글귀 본격 + WorldData 마이그레이션 (2~3일)
**신규**:
- `LoreInscriptionGenerator.cs` — LLM 호출 + 캐시 (LLMManager 기존 인프라 활용)
- `KoreanJosaResolver` 폴백 (설계: [archive/PROCEDURAL_HISTORY_BACKBONE.md §9](../_archive/PROCEDURAL_HISTORY_BACKBONE.md))
- `LoreSaveData` 클래스 + `WorldData.LoreSaveData` 필드
- `DataManager.MigrateWorldData` 분기 (Version 증가)

**수정**:
- LoreFragment 조사 entity → 첫 진입 시 LLM 호출 → State 갱신 + 저장

### Phase 7 — Live Backbone 연결 (장기, 별도 트랙)
백본 [archive §8](../_archive/PROCEDURAL_HISTORY_BACKBONE.md) + 본 문서 §12 #4 옵션 A. (런타임 Live 연대기는 [PROCEDURAL_HISTORY_DESIGN.md §5](../world_history/PROCEDURAL_HISTORY_DESIGN.md)도 참고)
- `System_LiveLoreCollector` — 보스 처치/마을 설립 broadcast → AmbientTag 갱신, 선택적으로 새 fragment 생성
- 본 문서 범위에서는 hook만 열어둠

**총 작업량 추정**: 코드 12~18일 + 콘텐츠(스프라이트, 청사진, 글귀 톤 튜닝) 별도.

---

## 14. 파일 구조 (제안)

```
Assets/Scripts/
├── Manager/
│   ├── MapManager.cs                       # 기존 + _regionLookup 추가
│   └── (옵션) LoreManager.cs               # AR.s.Lore (또는 HistoryManager에 흡수)
├── Map/
│   ├── MapManager_Generator.cs             # OverlayLoreFragments 단계 추가
│   ├── MapManager_Renderer.cs              # ThemeTileSet Dictionary 적용
│   ├── MapManager_Spawner.cs               # Lore hook 추가
│   ├── MapChunkData.cs                     # 기존
│   ├── MapFileData.cs                      # 기존
│   ├── MapFileSaver.cs                     # 기존
│   ├── TileModification.cs                 # 기존
│   ├── BuildableTileRegistry.cs            # 기존
│   ├── Tile/ThemeTileSet.cs                # 기존 (cluster biome용 Ground sprite)
│   ├── Tile/GroundTileSet.cs                # v1.10 NEW (청사진 ground 통합 룩업)
│   ├── Region/                             # NEW
│   │   └── RegionCoord.cs                  # static 좌표 변환 헬퍼 (ECS 아님)
│   ├── Biome/                              # NEW
│   │   ├── BiomeNoise.cs                   # ComputeTileBiome (warped Perlin)
│   │   └── BiomeTable.cs                   # biome → Hill/spawn 보정
│   ├── Race/                               # Race Settlement — M1 (실제 폴더명: Race/, 구 명세 Settlement/)
│   │   ├── RaceSettlementPlacer.cs         # ✅ 절차 배치, _mapFileDataDic 등록 (v1.10.2 완료)
│   │   ├── RaceBiomeTable.cs               # ✅ biome → race 매핑
│   │   └── DeterministicCoord.cs           # ✅ Region/Chunk 결정론 좌표
│   │   # SettlementBlueprintCatalog.cs는 도입 안 함 — 명명 규칙 `BaseTown_{race}.bytes`로 대체
│   └── Lore/                               # NEW
│       ├── LoreFragmentPlacer.cs           # 청크당 fragment 결정 + 비트 갱신
│       ├── LoreInteractable.cs             # 상호작용 entity
│       ├── LoreInscriptionGenerator.cs     # LLM 호출 + 폴백
│       └── BiomeRaceTable.cs               # 바이옴 → race_origin 풀 (lore용)
├── Common/
│   ├── Component/
│   │   └── Map/LoreFragmentComponent.cs    # NEW. RegionComponent는 폐지 (§12 #11)
│   └── GlobalEnum.cs                       # BiomeType / RaceType / RaceOrigin / FateKeyword / SettlementSize
└── Data/
    ├── WorldData.cs                        # LoreSaveData 필드 추가
    └── LoreSaveData.cs                     # NEW
```

---

## 15. 함정 / 주의사항

### 15.1 결정론
- **`System.Random`/`UnityEngine.Random` 금지** — 좌표 기반 hash로
- **Dictionary 순회 순서 의존 금지** — 결정론적 순회는 sorted key 사용
- `mapSeed`와 PRNG 채널 분리 철저 (§8.3)

### 15.2 청크 풀 재사용
- `GetChunkFromPool()` → `SetChunkPosition` → `GenerateChunkData` 흐름 — 청크는 풀로 돌아왔다 다시 나옴
- `GenerateChunkData`는 idempotent여야 함 (같은 좌표는 항상 같은 결과)
- 현재 `monsterSpawnPositions.Clear()`는 있으나 `_randomGenerator` 상태가 reset되지 않음 → §8.1 결정론 문제

### 15.3 청사진 좌표 경계
- L3 청사진이 region/청크 경계에 걸치면 일부만 그려질 수 있음 → `OverlayBlueprintAt`의 bounding-box 클리핑이 자동 처리
- 단, 시각이 잘릴 가능성 → 좌표 결정 시 청사진 크기만큼 가장자리 마진 두기

### 15.4 BaseTown과의 좌표 충돌
- BaseTown은 `StartPosition`이 고정 ([_BinaryData/TilemapData/BaseTown.bytes](../../Assets/_BinaryData/TilemapData/BaseTown.bytes))
- BaseTown이 차지하는 region에는 fragment 배치 차단 (또는 BaseTown 외곽으로 밀어내기)

### 15.5 ThemeTileSet 단일 박힘 해소
- 현재 `MapManager._themeTileSet`이 인스펙터에 1개만 박혀있음 ([MapManager_Renderer.cs:9](../../Assets/Scripts/Map/MapManager_Renderer.cs#L9))
- Phase 1에서 `Dictionary<BiomeType, ThemeTileSet>`으로 전환 — 인스펙터는 `[SerializeField] private BiomeThemeEntry[] _themeEntries` 패턴으로 디자이너 친화

### 15.6 마이그레이션 안전
- WorldData Version 증가 시 [DataManager.cs](../../Assets/Scripts/Manager/DataManager.cs)의 backup + migrate 로직 활용
- LoreSaveData 필드 추가는 옵셔널(`= new()`)로 시작 → 기존 세이브 호환

---

## 16. 절차적 역사 문서와의 관계

> **2026-06-26 갱신**: 구 `PROCEDURAL_HISTORY_DESIGN.md`가 분리됨 — 깊은 과거 백본(아래 표의 §1~§16)은 [archive/PROCEDURAL_HISTORY_BACKBONE.md](../_archive/PROCEDURAL_HISTORY_BACKBONE.md)로 이관(미고려), 정착지 통폐합/연대기는 활성 [PROCEDURAL_HISTORY_DESIGN.md](../world_history/PROCEDURAL_HISTORY_DESIGN.md)로 재편. **아래 표의 섹션 번호는 아카이브 백본 문서 기준**이다.

본 문서 도입에 따라 (구) 백본 문서는 다음과 같이 정리:

| PROCEDURAL_HISTORY 섹션 | 본 문서 처리 |
|------------------------|-------------|
| §1 (생성 철학), §2 (시간축) | **대체 (스코프 축소)** — Past Backbone 폐지. Live Backbone만 향후 부활 |
| §3 (ECS 데이터 모델) | **부분 흡수 + 폐지** — RegionComponent는 v1.3에서 폐지(§12 #11). **SettlementComponent는 v1.5에서 부활 X 결정** — Race Settlement(§7) M1 통합 모델로 흡수, VillageManager가 그 역할. HistoricalEvent류는 Live Backbone 후 |
| §4 (태그 시스템) | **유지** — Live Backbone과 AmbientTag에서 재사용 |
| §5 (슬롯 매칭/생성 루프) | **폐지** — 유적은 슬롯 매칭 안 함, 시간 진행 안 함 |
| §6 (공간 배치) | **본 문서로 흡수** — Region 격자, 결정론 좌표, `_tileModifications` 우회 모두 본 문서가 정본 |
| §7 (System Priority) | Phase 7(Live Backbone) 시점에 부활 |
| §8 (Live Backbone) | **보류 → Phase 7** — 본 문서 §12 #4 옵션 A로 통합 |
| §9 (한국어 조사) | **유지** — 본 문서 §9.3 폴백으로 사용 |
| §11~16 (예시/주의/결정) | 본 문서가 Lore 관점에서 재정리 |
| §13.10 진행 상황 | Phase 0 자산 활용 가능 — 본 문서 §13 Phase 0이 짧은 이유 |

→ 백본 문서(아카이브)는 미고려 보존본. 정착지 통폐합/연대기는 [PROCEDURAL_HISTORY_DESIGN.md](../world_history/PROCEDURAL_HISTORY_DESIGN.md), 공간 배치/유적은 본 문서가 정본.

---

**문서 상태**: 설계 v1.10.3 (Phase 0~2 완료. Phase 2.x 후속 트랙(size variant / NPC race 통합 / §12 보류 사항)은 추후 작업으로 분리. 다음 진입 후보: Phase 3 유적 단편)
**작성일**: 2026-05-19
**최근 갱신**:
- 2026-05-19 (v1.1) — 바이옴 결정 축 옵션 A 채택 (온도+습도+고도 3축, Whittaker 모델)
- 2026-05-19 (v1.2) — 바이옴 렌더링 방식 옵션 Y 채택 (per-tile warped Perlin)
- 2026-05-19 (v1.3) — RegionComponent ECS 폐지 (§12 #11)
- 2026-05-19 (v1.4) — AmbientTag 시스템 폐지 (§12 #12)
- 2026-05-19 (v1.5) — §7 Race Settlement 신설 (M1 모델). 섹션 번호 §7~§16 재조정
- 2026-05-19 (v1.6) — Phase 0 완료
- 2026-05-19 (v1.7) — Phase 1 코드 완료 (per-tile biome 모델)
- 2026-05-19 (v1.8) — **옵션 Y → region 단위 단일 biome 회수**. Region size 8×8 청크 → **4×4 청크** 축소. `BiomeNoise.ComputeRegionBiome` 신규. `MapChunkData.tileBiomes[,]` 제거. `GenerateChunkData` 청크당 1 biome으로 단순화 (Perlin 64배 절감). 사유: settlement/lore 단위 모순(작은 patch에 잘못된 race) 차단 + region size 축소로 격자감 완화. §12 #10 결정 재선택, §4/§5 갱신
- 2026-05-22 (v1.10.1) — v1.10 콘텐츠 작업 완료 처리: GroundTileSet.asset 생성/슬롯 할당/BiomeThemeEntry/ThemeTileSet.Ground 채움 모두 ✅. BiomeMonsterMapping은 몬스터 종류 부족으로 보류 표기
- 2026-05-22 (v1.10.2) — **Phase 2 Race Settlement 청사진 적용 완료**. 청사진 7종(`BaseTown_{race}.bytes`) 작성 + `LoadRaceBlueprint(race)` (명명 규칙) + `HasMapFileOverlap(blueprint)` + `_mapFileDataDic` 등록까지 모두 구현. 별도 `SettlementBlueprintCatalog.cs` 도입 안 함 — 명명 규칙으로 대체. §7.3/§7.4/§13 Phase 2/§14 파일 구조 갱신
- 2026-05-22 (v1.10.3) — **Phase 2 종료, Phase 2.x 후속 트랙 분리**. size variant / NPC race 통합 / §12 #13~#18 결정 보류 사항은 별도 보류 트랙으로 미룸 (사용자 결정). Phase 2 자체는 ✅ 완료 상태로 마감
**선행 의존**: Map 청크 시스템 (✅ 코드 완료) · BuildableTileRegistry (✅) · WorldData 직렬화 (✅) · LLM 인프라 (✅ Groq Llama 3.3 70B, Phase 6에서 사용) · NpcManager (✅ RegisterNpcsFromMapFile) · VillageManager (✅) · PROCEDURAL_HISTORY Phase 0 (일부 재활용)
**미해결**: §12 결정 보류 사항 1~8, 13~18 (#9 바이옴 축, #10 렌더링 방식, #11 RegionComponent 폐지, #12 AmbientTag 폐지는 확정. M1 Race Settlement 통합 방식 확정)
