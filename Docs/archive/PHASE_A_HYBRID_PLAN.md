# Phase A — 하이브리드 건물 시스템 도입 계획 ✅ 완료 (2026-04-24)

> Phase A의 Campfire를 **EntityBase 기반 엔티티**로 구현하면서, 기존 Tile 경로는 **범용 경량 건물용으로 유지**한다. `BuildableItemTable.SpawnType` 컬럼으로 분기하고, `AnimationId` 컬럼으로 정적/애니메이션을 구분한다.
>
> 관련 문서: [../PHASE_A_DESIGN.md](../PHASE_A_DESIGN.md) · [PHASE_A_IMPLEMENTATION.md](PHASE_A_IMPLEMENTATION.md) · [PHASE_A_UNITY_CHECKLIST.md](PHASE_A_UNITY_CHECKLIST.md)

---

## 1. 목표

1. Campfire를 `GameObject` (EntityBase) 기반으로 배치 — 향후 불꽃 애니/상호작용/파티클 확장 여지 확보
2. 기존 Tile 경로는 그대로 유지 — 벽/바닥 같은 반복 정적 구조물에 그대로 활용
3. `BuildableItemTable.SpawnType` 컬럼으로 경로 분기, `AnimationId` 컬럼으로 **정적 Sprite vs SpriteLibrary 애니** 분기
4. pathfinding / 세이브 / `System_VillageFirstBuild` 로직은 **무변경**
5. 기존 `System_Animation` + `SpriteAnimationComponent` 파이프라인을 **건물에도 재활용** — 건물 전용 애니 시스템 만들지 않음

---

## 2. 핵심 설계 결정

### 2.1 단일 진입점: `MapManager.PlaceObject`

호출부(`System_VillageFirstBuild` 등)는 기존 그대로 `PlaceObject(x, y, buildableId)` 호출. 내부에서만 `SpawnType`으로 분기.

```
PlaceObject(x, y, id)
  ├── SpawnType == Tile   → 타일 비트(Blocked + objectId) + RenderSingleObjectTile (기존)
  └── SpawnType == Entity → 타일 비트 무변경 + BuildingManager.PlaceBuilding(x, y, id)
```

**Entity 타입은 타일 비트를 건드리지 않는다**. 이유는 §2.3 참조.

### 2.2 NpcManager 패턴 복제

[NpcManager.cs](Assets/Scripts/Manager/NpcManager.cs) 와 동일 구조:

| NpcManager | BuildingManager (신규) |
|---|---|
| `_npcSaveDict: Dictionary<int, NpcSaveData>` | `_buildingSaveDict: Dictionary<int, BuildingSaveData>` |
| `_chunkNpcs: Dictionary<Vector2Int, List<int>>` | `_chunkBuildings: Dictionary<Vector2Int, List<int>>` |
| `OnChunkActivated/Deactivated` | `OnChunkActivated/Deactivated` |
| `SpawnNpc` / `SaveAndDeactivateNpc` | `SpawnBuilding` / `SaveAndDeactivateBuilding` |
| `Save() / Load()` | `Save() / Load()` |

훅은 [MapManager_Spawner.cs](Assets/Scripts/Map/MapManager_Spawner.cs) 의 `OnChunkActivated/Deactivated`에 한 줄씩 추가.

### 2.3 상태 저장의 이원화 — 진실원천 분리

| 데이터 | Tile 타입 | Entity 타입 |
|---|---|---|
| 위치 + 종류 | 청크 타일 비트 (`ObjectLayerMask`) | `BuildingSaveData` |
| 빈 칸 판정 | 타일 Blocked 비트 | `BuildingManager._occupiedTiles` HashSet |
| HP/타이머 등 상태 | (현재 없음, 필요 시 dict 추가) | `BuildingSaveData` |

**Entity 타입이 타일 비트를 안 건드리는 이유**:

프로젝트의 `TileFlag.Blocked` 비트는 **물리 충돌이 아님** — 이동 차단용으로 쓰이지 않음. 실제로 읽는 곳은 [VillageTileFinder.CanBuildOnTile()](Assets/Scripts/Village/VillageTileFinder.cs#L45) **1곳뿐**이고, 용도는 "새 건물 지을 빈 칸인지 판정".

`VillageTileFinder`가 `BuildingManager._occupiedTiles`까지 같이 조회하면 Blocked 비트 세팅 없이 같은 기능 달성. Entity의 진실원천이 `BuildingManager` 하나로 완전히 집중됨.

이점:
- 청크 세이브 파일 무변경 (Entity는 타일 비트에 흔적 없음)
- 멀티타일 건물이 자연스럽게 처리됨 (`_occupiedTiles`에 해당 칸 전부 추가)
- `MapManager_Renderer`가 Entity 타입 타일을 재렌더 시도하지 않음 (objectId=0이므로)
- BuildingManager와 타일 비트의 정합성 걱정 없음 (타일 비트에 건물 흔적이 없으니까)

### 2.4 프리팹 통합: `Prefabs/Entity` 공용

**`Prefabs/Entity`를 엔티티(Monster/NPC/Player)와 건물(Campfire 등) 모두 공용 사용**. 별도 `Prefabs/Building` 프리팹 만들지 않음.

핵심 근거: [SpriteAnimationData.SetSprite()](Assets/Scripts/Animation/SpriteAnimationData.cs) 가 `_spriteRenderer.sprite`를 **직접 교체**하고 `SpriteLibraryAsset`은 생성자에서 직접 소비. 런타임 애니메이션은 **`SpriteLibrary`/`SpriteResolver` 컴포넌트를 쓰지 않음**.

→ `Prefabs/Entity`에서 **SpriteLibrary/SpriteResolver 컴포넌트 제거** (레거시 정리). SpriteRenderer만 남김.

`BuildableItemTable.AnimationId`가 런타임 구성을 결정:

| `AnimationId` | `BuildingFactory` 동작 | 결과 |
|---|---|---|
| `0` (정적) | `Addressables.LoadAssetAsync<Sprite>(table.ResourceName)` → `_sr.sprite` 할당 | 정적 스프라이트, System_Animation 미관여 |
| `> 0` (애니) | `SpriteAnimationComponent` 부착 + `SpriteLibraryAsset` 비동기 로드 + `SpriteAnimationData` 생성 → `System_Animation` 등록 | 기존 엔티티 애니 파이프 그대로 동작 |

이점:
- **프리팹 1개로 모든 엔티티/건물 커버** — 에셋 관리 단순
- `EntityBase.SetSpriteLibrary()` 같은 레거시 메서드 제거 (실제 미사용)
- `SpriteLibrary`/`SpriteResolver` 런타임 AddComponent 비용 없음
- 애니 건물은 **`AnimationTable` 재활용** — `CreatureTable.AnimationId`와 동일 파이프

**건물과 엔티티의 차이는 Factory 레벨에서만 존재**:
- `EntityFactory.CreateMonster/Npc/Player`: HP바/Shadow/AI/Skill/Stat 전부 부착
- `BuildingFactory.CreateBuilding`: `BuildingTag` + `BuildingComponent`만 부착 (HP바/Shadow 없음)

### 2.5 `BuildableItemTable.ResourceName`의 이중 해석

현재: `TileBase` Addressable 키 (`Tiles/Village/Campfire`)

변경 후:
- `SpawnType == Tile` → `TileBase` 키로 해석 (기존)
- `SpawnType == Entity` + `AnimationId == 0` → `Sprite` 키로 해석 (예: `Sprites/Items/Campfire`)
- `SpawnType == Entity` + `AnimationId > 0` → SpriteLibraryAsset 경로는 `AnimationTable.SpriteLibraryPath`에서 가져옴. `ResourceName`은 placeholder로만 사용 (또는 초기 프레임용 Sprite)

### 2.6 외부 시스템에서 "이 칸에 뭐 있나" 조회

Entity 타입은 타일 비트에 흔적이 없으므로 `GetObjectIdAt`는 0을 반환한다. 외부 시스템이 종류 무관하게 "건물 있나?"를 물어야 하면 다음 래퍼를 사용:

```csharp
public bool IsAnyObjectAt(int worldX, int worldY)
    => GetObjectIdAt(worldX, worldY) > 0
       || AR.s.Building.IsTileOccupied(worldX, worldY);
```

현재는 `VillageTileFinder`만 필요. 추후 AI/스폰 등이 요청하면 확장.

### 2.7 변경하지 않는 것

- `EntityFactory` (몬스터/NPC/플레이어 경로 무손상)
- `CreatureTable`, `StatTable` 스키마
- `System_VillageFirstBuild` — `PlaceObject` 시그니처가 안 바뀌니 호출만 유지
- 기존 pathfinding / `IsWalkable` / `GetObjectIdAt`
- `System_Animation`, `SpriteAnimationComponent` — 건물도 그대로 사용
- `VillageData.HasCampfire` — 마일스톤 플래그로 유지 (건물 존재 여부는 BuildingManager가 별도 소유)
- `AnimationTable` — 건물/크리처 공용

---

## 3. 변경 파일 목록

### 신규
| 경로 | 역할 |
|---|---|
| `Assets/Scripts/Common/Component/BuildingTag.cs` | 빈 태그 struct |
| `Assets/Scripts/Common/Component/BuildingComponent.cs` | TableId, VillageId, WorldTileX/Y, CurrentHp |
| `Assets/Scripts/Factory/BuildingFactory.cs` | `Prefabs/Entity` 로드 + 정적/애니 분기 + 건물 전용 컴포넌트 부착 |
| `Assets/Scripts/Manager/BuildingManager.cs` | 상태 dict + 청크 매핑 + Save/Load |
| `Assets/Scripts/Village/BuildingSaveData.cs` | 직렬화 구조체 |
| (Unity 에셋) `Assets/Art/Sprites/Items/Campfire.png` + import 설정 | Campfire 정적 placeholder 스프라이트 |

**신규 프리팹 불필요** — 기존 `Prefabs/Entity` 재사용 (단, SpriteLibrary/SpriteResolver 제거)

### 수정
| 경로 | 변경 내용 |
|---|---|
| `Assets/Scripts/Common/Tables.cs` → `BuildableItemTable` | `SpawnType`, `AnimationId` 필드 추가 |
| `Assets/Scripts/Common/GlobalEnum.cs` | `BuildableSpawnType` enum 추가 |
| `Assets/Scripts/Editor/DownloadTables.cs` | `ParseBuildableItemTable` 컬럼 증가 (A:K → A:M), `SpawnType`/`AnimationId` 파싱 |
| `Assets/Scripts/Manager/MapManager.cs` → `PlaceObject` | SpawnType 분기 |
| `Assets/Scripts/Map/MapManager_Spawner.cs` → `OnChunkActivated/Deactivated` | BuildingManager 호출 한 줄 |
| `Assets/Scripts/Manager/ComponentManager.cs` | `BuildingTag`, `BuildingComponent` 풀 등록 |
| `Assets/Scripts/AR.cs` (또는 글로벌 접근점) | `AR.s.Building` 게터 추가 |
| `Assets/Scripts/Manager/DataManager.cs` 세이브 경로 | `BuildingSaveData` 직렬화 |
| (Google Sheets) `BuildableItem` 시트 | `SpawnType`, `AnimationId` 컬럼 추가, Campfire 행 세팅 |

### 손대지 않는 것
- `System_VillageFirstBuild.cs`
- `System_Animation.cs`, `SpriteAnimationComponent.cs`, `SpriteAnimationData.cs`
- `BuildableTileRegistry.cs` (Tile 경로용으로 그대로 유지)
- `VillageData.cs` (`HasCampfire`는 마일스톤 플래그로 유지)

### 추가 정리 (프리팹 통합 부수 작업)
| 경로 | 변경 내용 |
|---|---|
| `Assets/Scripts/Base/EntityBase.cs` | `SetSpriteLibrary()` 메서드 제거 (런타임 미사용) |
| `Assets/Scripts/Factory/EntityFactory.cs` | `LoadAnimationAsync`에서 `SetSpriteLibrary` 호출 제거 |
| (Unity 에셋) `Assets/Prefabs/Game/Creature/Entity.prefab` | SpriteRenderer 자식의 `SpriteLibrary`, `SpriteResolver` 컴포넌트 제거 |

---

## 4. 단계별 작업 순서

### Step 1 — 데이터 스키마 (15분)
1.1 `GlobalEnum.cs`에 `BuildableSpawnType { Tile = 0, Entity = 1 }` 추가
1.2 `BuildableItemTable`에 필드 추가:
  ```csharp
  public BuildableSpawnType SpawnType;
  public int AnimationId;   // 0 = 정적 Sprite, > 0 = AnimationTable 참조
  ```
1.3 Google Sheets `BuildableItem` 시트 컬럼 추가:
  - L열: `SpawnType` (`Tile` / `Entity`)
  - M열: `AnimationId` (정수, 0이면 정적)
  - Campfire 행: `SpawnType=Entity`, `AnimationId=0`, `ResourceName=Sprites/Items/Campfire`
  - 기존 나무벽 행: `SpawnType=Tile`, `AnimationId=0` (변경 최소화)
1.4 `DownloadTables.cs`:
  - 다운로드 범위 `A:K` → `A:M` (`534887250&range=A:M`)
  - `ParseBuildableItemTable` 끝에 2줄 추가:
    ```csharp
    table.SpawnType = (GlobalEnum.BuildableSpawnType)Enum.Parse(
        typeof(GlobalEnum.BuildableSpawnType), values[11]);
    table.AnimationId = int.Parse(values[12]);
    ```
  - 길이 체크 `< 11` → `< 13`

**DoD**: Unity에서 `ARPG/Download Table` 실행 → `BuildableItemTable.bytes` 갱신, 런타임에 `GetBuildableItem(100).SpawnType == Entity && AnimationId == 0` 확인

### Step 2 — ECS 태그/컴포넌트 (5분)
2.1 `BuildingTag.cs` (빈 struct, `MonsterTag` 패턴 복제)
2.2 `BuildingComponent.cs`:
```csharp
public struct BuildingComponent {
    public int TableId;
    public int VillageId;      // -1 = 마을 소속 아님
    public int WorldTileX;
    public int WorldTileY;
    public int CurrentHp;      // 0 이하 = 파괴
}
```
2.3 `ComponentManager.Initialize()`에 풀 등록 (2줄)

**DoD**: 컴파일 성공

### Step 3 — 기존 프리팹 정리 + Sprite 임포트 (Unity 작업, 10분)
3.1 **`Assets/Prefabs/Game/Creature/Entity.prefab`에서 SpriteLibrary, SpriteResolver 컴포넌트 제거**
  - 자식 SpriteRenderer 오브젝트 인스펙터에서 두 컴포넌트 Remove
  - 런타임 애니메이션은 `SpriteAnimationData`가 `_sr.sprite`를 직접 교체하므로 영향 없음
3.2 Campfire Sprite 임포트: `Assets/Art/Sprites/Items/Campfire.png` (단색 placeholder)
3.3 Sprite Addressable 키: `Sprites/Items/Campfire`

**DoD**:
- Entity.prefab에서 SpriteLibrary/SpriteResolver 제거 후 기존 몬스터/NPC/플레이어 애니메이션이 정상 재생되는지 Play Mode 확인
- Campfire Sprite가 Addressable Groups에 등록되고 Build 완료

### Step 4 — BuildingFactory (45분)
4.1 `BuildingFactory.cs` 신설:

```csharp
public static class BuildingFactory
{
    private const string BUILDING_PREFAB_KEY = "Prefabs/Building";

    public static async UniTask<(int entityId, EntityBase? entity)> CreateBuilding(
        int tableId, int worldTileX, int worldTileY,
        int villageId, int savedEntityId = -1, int savedHp = -1)
    {
        var table = AR.s.Data.GetBuildableItem(tableId);
        if (table == null) return (-1, null);

        // 1. 프리팹 인스턴스
        Vector3 pos = new Vector3(worldTileX + 0.5f, worldTileY + 0.5f, -0.01f);
        var obj = await Addressables.InstantiateAsync(
            BUILDING_PREFAB_KEY, pos, Quaternion.identity).ToUniTask();
        var entity = obj.GetComponent<EntityBase>();

        // 2. EntityId 발급
        if (savedEntityId >= 0) entity.SetEntityId(savedEntityId);
        entity.SetupEntityId();
        int entityId = entity.EntityId;

        // 3. BuildingTag + BuildingComponent
        AR.s.Component.AddComponent(entityId, new BuildingTag());
        AR.s.Component.AddComponent(entityId, new BuildingComponent {
            TableId = tableId,
            VillageId = villageId,
            WorldTileX = worldTileX,
            WorldTileY = worldTileY,
            CurrentHp = savedHp >= 0 ? savedHp : table.HP
        });

        // 4. System_Render 등록
        var renderSystem = AR.s.System.GetSystem<System_Render>();
        renderSystem?.RegisterEntity(entityId, entity);

        // 5. 스프라이트 경로 분기
        if (table.AnimationId == 0)
        {
            await LoadStaticSprite(entity, table.ResourceName);
        }
        else
        {
            await SetupAnimatedSprite(entityId, entity, table.AnimationId);
        }

        return (entityId, entity);
    }

    private static async UniTask LoadStaticSprite(EntityBase entity, string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName)) return;
        var sprite = await Addressables.LoadAssetAsync<Sprite>(resourceName).ToUniTask();
        if (sprite != null && entity.SpriteRenderer != null)
            entity.SpriteRenderer.sprite = sprite;
    }

    private static async UniTask SetupAnimatedSprite(int entityId, EntityBase entity, int animationId)
    {
        var animTable = AR.s.Data.GetAnimation(animationId);
        if (animTable == null) return;

        // SpriteLibrary / SpriteResolver 런타임 부착
        var sr = entity.SpriteRenderer;
        if (sr.GetComponent<SpriteLibrary>() == null)
            sr.gameObject.AddComponent<SpriteLibrary>();
        if (sr.GetComponent<SpriteResolver>() == null)
            sr.gameObject.AddComponent<SpriteResolver>();

        // SpriteAnimationComponent 추가 (기존 파이프 진입)
        AR.s.Component.AddComponent(entityId, new SpriteAnimationComponent {
            AnimationTableId = animationId,
            LoadState = AnimationLoadState.None,
            PlaybackSpeed = 1f,
            CurrentCategory = GlobalEnum.AnimCategory.Idle,
            IsLooping = true,
            IsPlaying = true,
            FrameDuration = 0.1f
        });
        AR.s.Component.AddComponent(entityId, new AnimatorComponent());

        // SpriteLibraryAsset 비동기 로드 → System_Animation에 등록
        // (EntityFactory.LoadAnimationAsync 로직 참고, 건물용 경량 버전)
        LoadSpriteLibraryAsync(entityId, entity, animTable).Forget();
    }
}
```

4.2 `LoadSpriteLibraryAsync`는 [EntityFactory.LoadAnimationAsync](Assets/Scripts/Factory/EntityFactory.cs#L567) 를 기반으로 경량화:
- HP바, Shadow 관련 코드 제거
- `SpriteLibraryAsset` 로드 → `EntityBase.SetSpriteLibrary()` 호출
- `System_Animation.RegisterSpriteAnimation` 호출 (기존 그대로)

**DoD**:
- 단독 테스트: `BuildingFactory.CreateBuilding(100, 0, 0, -1)` → 원점에 Campfire GameObject 출현, SpriteRenderer에 Placeholder 스프라이트 표시
- 인스펙터에서 `SpriteLibrary` 컴포넌트가 **없음** 확인 (AnimationId=0이므로)

### Step 5 — BuildingManager (60분)
5.1 `BuildingSaveData.cs`:
```csharp
[Serializable]
public class BuildingSaveData {
    public int TableId;
    public int VillageId;
    public int WorldTileX;
    public int WorldTileY;
    public int CurrentHp;
    public bool IsActive;  // 청크 언로드 동안 false
}
```
5.2 `BuildingManager.cs` — NpcManager 구조 그대로 + 점유 칸 집합:
  - `_buildingSaveDict: Dictionary<int, BuildingSaveData>`
  - `_chunkBuildings: Dictionary<Vector2Int, List<int>>`
  - `_occupiedTiles: HashSet<Vector2Int>` — **신규: 빈 칸 판정용**. Entity 타입이 점유한 월드 좌표 집합. 멀티타일은 Size_Width/Height만큼 추가
  - `Initialize()`, `Reset()`
  - `PlaceBuilding(worldX, worldY, tableId, villageId)` → EntityId 발급 + SaveData 등록 + `_occupiedTiles`에 추가 + `SpawnBuilding` 호출
  - `SpawnBuilding(entityId, saveData)` → `BuildingFactory.CreateBuilding` 래핑
  - `RemoveBuilding(entityId)` → SaveDict에서 제거 + `_occupiedTiles`에서 제거 + Destroy
  - `IsTileOccupied(worldX, worldY) → bool` — VillageTileFinder가 조회
  - `OnChunkActivated(chunkCoord)` → 해당 청크 건물들 Spawn
  - `OnChunkDeactivated(chunkCoord)` → `SaveAndDeactivateBuilding` (HP를 SaveData로 복사 후 Destroy, `_occupiedTiles`는 유지)
  - `Save() / Load(dict, chunkSize)` — `DataManager`가 호출. Load 시 `_occupiedTiles` 재구축
5.3 `AR.s.Building` 게터 추가 (GlobalManager 또는 AR 프리팹)
5.4 `DataManager`의 세이브/로드 경로에 `BuildingSaveData` 직렬화 포함

**DoD**: `PlaceBuilding` 직접 호출 시 GameObject 출현 + `Save/Load` 왕복 후 복원 + `IsTileOccupied` 정상 반환

### Step 6 — MapManager 통합 (20분)
6.1 `MapManager.PlaceObject` 내부에 분기:
```csharp
public bool PlaceObject(int worldX, int worldY, int objectId)
{
    if (objectId <= 0) return false;

    var table = AR.s.Data.GetBuildableItem(objectId);

    if (table == null || table.SpawnType == GlobalEnum.BuildableSpawnType.Tile)
    {
        // 기존 경로: 타일 비트(objectId + Blocked) 세팅 + 활성 청크 즉시 렌더
        SetTileBitsAndRender(worldX, worldY, objectId);
    }
    else // Entity 타입: 타일 비트 무변경, BuildingManager에 전적으로 위임
    {
        int villageId = AR.s.Village.FindVillageContaining(worldX, worldY);
        AR.s.Building.PlaceBuilding(worldX, worldY, objectId, villageId);
    }

    return true;
}
```
6.2 [MapManager_Spawner.cs](Assets/Scripts/Map/MapManager_Spawner.cs) 의 `OnChunkActivated/Deactivated`에 한 줄씩:
```csharp
AR.s.Building?.OnChunkActivated(chunkCoord);
AR.s.Building?.OnChunkDeactivated(chunkCoord);
```
6.3 `VillageManager.FindVillageContaining(x, y)` 헬퍼 추가 — 가장 가까운 마을을 SpawnRadius 이내에서 검색 후 ID 반환, 없으면 -1
6.4 [VillageTileFinder.cs:45](Assets/Scripts/Village/VillageTileFinder.cs#L45) 의 빈 칸 판정 보강:
```csharp
// 기존: IsWalkable만 확인 (타일 Blocked 비트)
if (AR.s.Map.IsWalkable(world) == false) return false;
// 추가: BuildingManager의 Entity 점유 칸도 확인
if (AR.s.Building.IsTileOccupied(tile.x, tile.y)) return false;
```

**DoD**: `System_VillageFirstBuild`가 `PlaceObject(x, y, 100)` 호출 시 Campfire GameObject 출현, `BuildingManager._buildingSaveDict`/`_occupiedTiles`에 엔트리 추가됨. 다시 빈 칸 검색 시 해당 위치가 후보에서 제외됨

### Step 7 — 세이브/로드 통합 (20분)
7.1 `DataManager` (세이브 컨테이너)에 `Dictionary<int, BuildingSaveData> BuildingSaveDatas` 추가
7.2 초기화 순서: NPC와 동일하게 맵 로드 후 `AR.s.Building.Load(data, chunkSize)` 호출
7.3 저장 시 `AR.s.Building.Save()` 호출
7.4 구 세이브 로드 시 null → 빈 dict 초기화 안전 처리

**DoD**:
- 세이브 → 종료 → 로드 → Campfire가 같은 위치에 유지됨
- 구 세이브 파일 로드 시 크래시 없음

### Step 8 — 문서 갱신 (10분)
8.1 [PHASE_A_UNITY_CHECKLIST.md](PHASE_A_UNITY_CHECKLIST.md) 갱신:
  - §1.1 Campfire 테이블 행에 `SpawnType=Entity`, `AnimationId=0` 추가
  - §2.1 "CustomTile 에셋 생성" → "Campfire Sprite 임포트 + `Prefabs/Building.prefab` 생성"
  - §3 Addressables 항목:
    - `Tiles/Village/Campfire` (TileBase) **삭제**
    - `Sprites/Items/Campfire` (Sprite) 추가
    - `Prefabs/Building` (GameObject) 추가

**DoD**: 체크리스트만 보고 Unity 작업자가 혼란 없이 진행 가능

---

## 5. 데이터/에셋 상세

### 5.1 Campfire 테이블 최종 형태 (§1.1 재작성)

| 컬럼 | 값 | 비고 |
|---|---|---|
| Id | 100 | |
| Name | Campfire | |
| Tooltip | 마을을 밝히는 첫 모닥불 | |
| IsBreakable | FALSE | |
| HP | 50 | 향후 확장용. 현재 Phase A는 `BuildingComponent.CurrentHp`에 복사만, 전투 미연결 |
| DropItem | 0 | |
| Size_Width | 1 | |
| Size_Height | 1 | |
| Recipe | 0 | |
| Function | 0 | |
| ResourceName | `Sprites/Items/Campfire` | **Entity + AnimationId=0 → Sprite 키로 해석** |
| SpawnType | `Entity` | **신규 L열** |
| AnimationId | `0` | **신규 M열**. Phase A는 정적. Phase B에서 불꽃 flicker 시 값 부여 |

### 5.2 Addressable 키 최종

| Address | 타입 | 용도 |
|---|---|---|
| `Prefabs/Entity` | GameObject (prefab) | 엔티티/건물 공용 (기존 등록 유지) |
| `Sprites/Items/Campfire` | Sprite | Campfire 정적 placeholder |

기존 `Tiles/Village/Campfire` (TileBase) 항목은 **제거** (또는 미사용으로 방치).

> 건물은 `Prefabs/Entity`를 재사용. 별도 `Prefabs/Building`은 만들지 않음.

### 5.3 Phase B 애니메이션 전환 시 필요한 것 (참고)

Campfire에 flicker 애니를 붙일 때:
1. `Assets/Art/SpriteLibraries/Village/Campfire.spriteLib` 생성 — `Idle` 카테고리에 2~4프레임 Label 등록
2. Addressable 등록 (키 예: `SpriteLibraries/Village/Campfire`)
3. `AnimationTable`에 행 추가 (예: `Id=1000, SpriteLibraryPath=SpriteLibraries/Village/Campfire, IdleFrame=0.2`)
4. `BuildableItemTable.Campfire.AnimationId = 1000`으로 변경
5. 코드 변경 0 — `BuildingFactory`가 자동으로 `SpriteLibrary`/`Resolver` 부착 + `System_Animation`이 재생

---

## 6. 리스크와 대응

| 리스크 | 영향 | 대응 |
|---|---|---|
| 세이브 호환성 — `BuildingSaveData` 없는 구 세이브 로드 | 크래시 또는 빈 dict | Newtonsoft 기본값(빈 dict)로 안전. `DataManager` 초기화 시 null 체크 |
| 청크 경계에서 Campfire 중복 Spawn | 같은 EntityId 2번 Instantiate | `NpcManager.SpawnNpc` 패턴 그대로 — "이미 활성이면 skip" |
| `VillageData.HasCampfire`와 `BuildingManager` 상태 불일치 | 재건축 로직 꼬임 | `System_VillageFirstBuild`는 `HasCampfire`만 본다 (재건축 방지 플래그). `BuildingManager`는 "실제 존재 여부"의 진실원천. 두 필드의 의미가 다르므로 중복이 아님. 문서화 필수 |
| Entity 점유 칸을 타일 비트에 안 남기므로 외부 시스템이 Entity 위치를 몰라봄 | AI/스폰이 Campfire 위에 몬스터 스폰 가능 | 현재 프로젝트는 Blocked 비트를 AI/이동에 쓰지 않음 — 영향 없음. 향후 "건물 위 스폰 금지" 요구사항 생기면 `System_MonsterSpawn`이 `AR.s.Building.IsTileOccupied`도 조회하도록 확장 (Phase A 범위 밖) |
| `VillageManager.FindVillageContaining` 미구현 | 컴파일 에러 | Step 6.3에서 간단 구현 추가 (SpawnRadius 이내 최근접 마을) |
| Addressable 로드 타이밍 — 청크 활성화 즉시 GameObject가 안 보임 | 1~2프레임 blank | NpcManager와 동일 현상, 허용 |
| `SpriteLibrary`/`SpriteResolver` 런타임 `AddComponent` 시 Awake 타이밍 | `SpriteRenderer`가 한 프레임 빈 상태로 보일 수 있음 | AddComponent 직후 SpriteLibraryAsset 로드 완료될 때까지 `sr.sprite`에 placeholder 유지. 필요 시 `ResourceName`의 Sprite를 먼저 세팅해두는 방식 고려 |
| `AnimationTable` 스키마 — 건물용 카테고리(`Dead` 등)가 크리처와 의미 충돌 | 없음 (현재는 `Idle`만 사용) | Phase B에서 건물 전용 카테고리 필요해지면 `AnimCategory` enum 확장. Phase A 범위 밖 |
| Tile 경로 비회귀 — 기존 나무벽 깨짐 | 배치된 벽 렌더 안 됨 | 기존 나무벽 `SpawnType=Tile` 확인 + Play Mode 수동 테스트 (§7.4) |

---

## 7. DoD (최종 검증)

Phase A 기존 DoD ([PHASE_A_UNITY_CHECKLIST.md §8](PHASE_A_UNITY_CHECKLIST.md#L184)) **전부 통과** + 다음 추가:

### 7.1 엔티티 전환 검증
- [ ] Play Mode → Campfire 완성 시점에 **씬 Hierarchy에 GameObject** 출현 (Tilemap 타일 아님)
- [ ] Campfire GameObject에 `EntityBase`, `BuildingTag`, `BuildingComponent`, `TransformComponent` 확인
- [ ] Campfire의 SpriteRenderer 자식에 `SpriteLibrary`/`SpriteResolver`가 **붙어있지 않음** (AnimationId=0이므로)
- [ ] Campfire 타일의 `GetObjectIdAt`이 **0 반환** (Entity 타입은 타일 비트 미기록)
- [ ] Campfire 타일의 `GetTileAt` 결과에 Blocked 비트 **없음**
- [ ] `BuildingManager.IsTileOccupied(x, y) == true` 반환
- [ ] NPC가 Campfire 타일 위를 통과 가능 (현 동작 유지)

### 7.2 청크 수명 검증
- [ ] 플레이어를 Campfire에서 `_loadRadius+2` 청크만큼 이동 → 씬에서 Campfire GameObject **사라짐**
- [ ] 되돌아오면 **재생성**, HP/상태 유지
- [ ] 멀리 있는 동안 `BuildingManager._buildingSaveDict`에는 데이터 유지

### 7.3 세이브 호환
- [ ] 세이브 → 종료 → 로드 → Campfire 위치·HP 유지
- [ ] Phase A 이전 세이브 파일(=`BuildingSaveData` 없음) 로드 시 크래시 없음, 빈 dict로 초기화

### 7.4 Tile 경로 비회귀
- [ ] 기존 `Id=1` 나무벽을 수동 배치 → 여전히 Tilemap 셀로 렌더 (GameObject 안 생김)
- [ ] pathfinding, Blocked 비트 동작 동일

### 7.5 애니 경로 스모크 테스트 (선택, Phase B 대비)
- [ ] 임시로 `BuildableItemTable.Campfire.AnimationId`를 기존 애니 테이블 Id(예: 아무 몬스터용)로 세팅 후 실행 → SpriteLibrary/Resolver 자동 부착, System_Animation이 Idle 카테고리 재생
- [ ] 원복 후 정적 동작 재확인

---

## 8. 완료 후 다음 수순 (Phase B 대비)

- 건물에 파티클(불꽃)·클릭 상호작용 붙이려면 `BuildingFactory`에 `AddInteractionComponents` 분기 추가
- 여러 건물 타입 배치 큐: `BuildableItemTable` + `BuildingFactory`가 이미 준비됨, `System_VillageFirstBuild`를 제네릭 `System_VillageBuildQueue`로 일반화
- Campfire 불꽃 애니: §5.3 참조 — 코드 변경 없이 테이블/에셋만으로 전환 가능
- 건물 HP 전투 연결: `BuildingComponent.CurrentHp`와 `StatComponent` 연동 필요 시 `IsBreakable==true`인 건물에만 `StatComponent` 조건부 부착
