# Phase A — 구현 문서

> 기획 근거: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md)
> 상위 비전: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
>
> 이 문서는 **실제 코드 변경 가이드**다. 파일 단위로 "무엇을 / 어디에 / 어떻게" 수정할지 기술한다.

---

## 0. 변경 요약 (Change Summary)

### 0.1 신규 파일

| 경로 | 역할 |
|------|------|
| `Assets/Scripts/Common/Component/VillageStorageComponent.cs` | 마을 자원 ECS 컴포넌트 |
| `Assets/Scripts/Common/System/System_VillagePassiveProduction.cs` | 생산/소비 시스템 (기존 `System_VillageResource` 대체) |
| `Assets/Scripts/Common/System/System_VillageFirstBuild.cs` | 첫 Campfire 제작 루프 (자원 체크 → 착수 → 2h 후 배치) |
| `Assets/Scripts/Village/VillageTileFinder.cs` | 마을 중심 주변 빈 타일 탐색 유틸 |
| `Assets/Scripts/Village/VillageDebugLog.cs` | 마을 상태 스냅샷 로그 정적 유틸 (테스트용 수동 호출) |
| `Assets/Scripts/Map/BuildableTileRegistry.cs` | `BuildableItemTable`의 모든 엔트리에 대한 `TileBase`를 앱 시작 시 Addressable로 미리 로드해 캐시하는 정적 레지스트리 |

### 0.2 수정 파일

| 경로 | 변경 요약 |
|------|----------|
| `Assets/Scripts/Common/GlobalEnum.cs` | **변경 없음**. 오브젝트 식별은 `BuildableItemTable.Id`로 처리 (enum의 `Npc`는 특수 구분용으로 유지) |
| `Assets/Scripts/Village/VillageData.cs` | `ResourceCaps` / `HungerHoursAccumulated` / `StoneTimer` / `RegisteredAt` / `HasCampfire` / `FirstBuildStartedAt` / `FirstBuildTileX` / `FirstBuildTileY` / `EntityId` 필드 추가 (정수 기반, 소수 버퍼 없음) |
| `Assets/Scripts/Village/VillageManager.cs` | `RegisterVillage`에 엔티티·컴포넌트 생성 연동, `Load` 마이그레이션 확장, Produce/Consume이 컴포넌트도 갱신 |
| `Assets/Scripts/Manager/ComponentManager.cs` | `VillageStorageComponent` 풀 크기 등록 (32) |
| `Assets/Scripts/Manager/SystemManager.cs` | `System_VillageResource` 등록 **삭제**, `System_VillagePassiveProduction` + `System_VillageFirstBuild` 등록 추가 |
| `Assets/Scripts/Map/MapManager_Renderer.cs` | `ObjectSet[objectId]` 직접 인덱싱 앞에 `BuildableTileRegistry.Get(id)` 조회 추가 (레거시 `ObjectSet` fallback 유지) |
| `Assets/Scripts/Manager/MapManager.cs` | `BuildableTileRegistry.TileLoaded` 이벤트 구독 → 해당 Id 타일이 포함된 활성 청크 재렌더 트리거 (간단하게는 활성 청크 전부 재렌더) |
| `Assets/Scripts/Common/System/System_VillageResource.cs` | **삭제** |

> **`NpcManager.EnsureVillagePopulated`는 수정하지 않는다.** 오브젝트 배치와 결합하지 않음 — NPC 스폰과 오브젝트 제작은 완전히 분리된 시스템으로 설계.
>
> **디버그 UI는 Phase A 범위 밖.** 화면 UI는 사용자가 이후 직접 추가한다. Phase A는 상태 전환 `Debug.Log`와 수동 스냅샷 로그만 제공.

### 0.3 에셋 / 데이터 작업

| 작업 | 대상 |
|------|------|
| `BuildableItemTable`에 **Campfire 엔트리 추가** (Id=100, Name=`Campfire`, ResourceName=`Tiles/Village/Campfire`) | Google Sheets 또는 CSV |
| CustomTile 에셋 생성 (Campfire placeholder sprite) | `Assets/Art/Tiles/Village/Campfire.asset` |
| 생성한 CustomTile을 **Addressable 그룹에 등록**. 키는 `ResourceName`과 **완전히 일치** (예: `Tiles/Village/Campfire`) | Unity Addressables Groups 창 |
| VillageTable CSV/시트에 Stage0 마을용 엔트리 확인 | `Assets/Resources/Data/` 혹은 Google Sheets |

> `ThemeTileSet.ObjectSet` 에셋은 **수정하지 않는다**. 신규 배치 오브젝트는 전부 Addressable 경로로 로드된다.

---

## 1. 단계별 작업 순서

> 아래 순서대로 작업하면 중간 단계마다 빌드·플레이가 가능하다.

### Step 1 — `BuildableItemTable` Campfire 엔트리 추가
구글 시트 또는 CSV에 Campfire 행 추가. `Id=100` / `Name=Campfire` / `HP=50` / `Size_Width=1` / `Size_Height=1` / `ResourceName=Tiles/Village/Campfire`.

### Step 2 — `BuildableTileRegistry` 작성 (lazy 캐시)
정적 레지스트리 파일 작성. **앱 초기화 연동 없음** — 호출자가 `EnsureLoadedAsync(id)`로 필요할 때 로드. `AR.Initialize`는 변경하지 않는다.

### Step 3 — `MapManager_Renderer` 패치
`ObjectSet[objectId]` 직전에 `BuildableTileRegistry.Get((int)objectId)` 조회 추가. 결과 null이면 기존 `ObjectSet` fallback. 렌더러는 동기 조회만 수행 — 사전 ensure는 호출자 책임.

### Step 3-A — `MapManager`가 `TileLoaded` 이벤트 구독
맵 로드 1회 스캔은 **도입하지 않는다.** 대신 `Get(id)`가 캐시 미스일 때 자동으로 백그라운드 로드를 시작 (§2.1-A 참조). 로드 완료 시 `BuildableTileRegistry.TileLoaded` 이벤트가 발생하면 `MapManager`가 활성 청크를 재렌더:

```csharp
// MapManager.Initialize 안
BuildableTileRegistry.TileLoaded += OnBuildableTileLoaded;

private void OnBuildableTileLoaded(int buildableId)
{
    // Phase A는 단순하게 활성 청크 전부 재렌더 (Campfire 1개라 비용 미미)
    // Phase B에서 "어느 청크에 어느 Id가 있는지" 인덱스로 세분화 가능
    ForceRedrawActiveChunks();
}
```

`Reset` 시 구독 해제:
```csharp
BuildableTileRegistry.TileLoaded -= OnBuildableTileLoaded;
```

레거시 값(Stone=1, Npc=2, WoodWall=3)은 `BuildableItemTable`에 없으므로 `LoadInternalAsync`에서 조용히 무시 (§2.1-A). 재렌더 이벤트도 발생 안 함.

### Step 4 — `VillageData` 필드 확장
JSON 역직렬화 하위호환 유지를 위해 필드만 먼저 추가. 기본값 세팅·로드 마이그레이션을 `VillageManager.Load`에 추가.

### Step 5 — `VillageStorageComponent` + ComponentManager 풀 등록
컴포넌트 struct 생성, ComponentManager에 풀 사이즈 등록.

### Step 6 — `VillageManager` 통합
`RegisterVillage`에서 엔티티 생성 + 컴포넌트 추가. `ProduceResource` / `ConsumeResource`가 컴포넌트도 갱신하도록 수정. `Load` 마이그레이션.

### Step 7 — `System_VillagePassiveProduction` 작성, 기존 `System_VillageResource` 삭제
시스템 등록을 `SystemManager`에서 교체.

### Step 8 — `VillageTileFinder` 작성
링 확장 BFS로 빈 타일 찾는 정적 유틸.

### Step 9 — `System_VillageFirstBuild` 작성
제작 조건 체크 → 착수 → 타이머 → 완료/환불 흐름 구현. `SystemManager`에 Priority 58로 등록.

### Step 10 — `VillageDebugLog` 작성
수동 호출용 정적 스냅샷 로그 유틸.

### Step 11 — 타일 에셋 제작 + Addressable 등록 (Unity Editor 작업)
Campfire placeholder 스프라이트 1장 + CustomTile 에셋. IsWalkable=true. Addressable 그룹에 `ResourceName` 키로 등록.

### Step 12 — 테스트 (§7)

---

## 2. 신규/수정 파일 상세

### 2.1 `GlobalEnum.cs` — 변경 없음

**위치**: [Assets/Scripts/Common/GlobalEnum.cs:28-34](../Assets/Scripts/Common/GlobalEnum.cs#L28-L34)

`ObjectType` enum은 **수정하지 않는다**. Phase A에서 Campfire를 포함한 모든 배치 오브젝트는 `BuildableItemTable.Id`로 식별한다.

**이유 요약**:
- `MapManager.PlaceObject(x, y, int objectId)`의 `objectId`는 이미 임의의 정수 (타일 ObjectLayer 10비트에 저장). enum 값일 필요 없음
- `ObjectType` enum은 실제로 `Npc = 2`만 유의미하게 사용됨 (NPC 구분 필터링). Stone/WoodWall은 비교되는 코드 없음
- `BuildableItemTable`이 이미 존재 (Id/Name/HP/Size/ResourceName 등) → 신규 오브젝트 추가 시 enum 수정 불필요
- 10비트(1023) 한계는 Phase B 이후에도 충분

**상수 정의** (`System_VillageFirstBuild` 내부):
```csharp
private const int CAMPFIRE_BUILDABLE_ID = 100; // BuildableItemTable의 Campfire 엔트리 Id
```

> 장기적으로 이 상수도 `AR.s.Data.GetBuildableItemByName("Campfire").Id` 같은 조회 헬퍼로 대체 가능. Phase A는 단순성 위해 상수 1개만.

---

### 2.1-A `BuildableTileRegistry.cs` (신규)

**경로**: `Assets/Scripts/Map/BuildableTileRegistry.cs`

**lazy 캐시 방식**. 앱 시작 시 **아무것도 로드하지 않는다**. 호출자가 `EnsureLoadedAsync(id)`를 await하면 해당 타일만 Addressable로 로드해 캐시. 이후 `Get(id)`는 동기 조회. 타일 에셋은 소형 공유 자원이므로 캐시는 **영구 유지**(청크별 ref-count 불필요).

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public static class BuildableTileRegistry
    {
        private static readonly Dictionary<int, TileBase> _cache = new();
        // 동일 Id에 대한 중복 로드 방지 — in-flight UniTask 공유
        private static readonly Dictionary<int, UniTask<TileBase?>> _inflight = new();

        /// <summary>타일 로드 완료 이벤트. MapManager가 구독해 활성 청크를 재렌더.</summary>
        public static event Action<int>? TileLoaded;

        /// <summary>1개 타일 비동기 로드. 이미 로드/로딩 중이면 기존 Task 반환. await 가능한 경로에서 사용 (예: TryFinishAsync).</summary>
        public static UniTask<TileBase?> EnsureLoadedAsync(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var cached))
                return UniTask.FromResult<TileBase?>(cached);

            if (_inflight.TryGetValue(buildableId, out var pending))
                return pending;

            var task = LoadInternalAsync(buildableId);
            _inflight[buildableId] = task;
            return task;
        }

        /// <summary>
        /// 동기 조회. 캐시 hit이면 타일 반환, 미스면 백그라운드 로드를 트리거하고 null 반환.
        /// 렌더러가 호출 — 미스 시 Object 레이어가 일시적으로 공란(지면은 정상 표시).
        /// 로드 완료 시 TileLoaded 이벤트 → MapManager가 해당 위치 재렌더.
        /// </summary>
        public static TileBase? Get(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var tile))
                return tile;

            // 캐시 미스 → 백그라운드 로드 트리거 (fire-and-forget)
            // 중복 로드는 _inflight가 방지
            EnsureLoadedAsync(buildableId).Forget();
            return null;
        }

        private static async UniTask<TileBase?> LoadInternalAsync(int buildableId)
        {
            try
            {
                var table = AR.s.Data.GetBuildableItem(buildableId);
                if (table == null)
                {
                    // 레거시 ObjectType 값(1=Stone, 2=Npc, 3=WoodWall 등) — ObjectSet이 처리, 조용히 무시
                    return null;
                }
                if (string.IsNullOrEmpty(table.ResourceName))
                {
                    Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} ResourceName 비어있음");
                    return null;
                }

                var handle = Addressables.LoadAssetAsync<TileBase>(table.ResourceName);
                TileBase tile = await handle.ToUniTask();
                if (tile != null)
                {
                    _cache[buildableId] = tile;
                    TileLoaded?.Invoke(buildableId);  // MapManager가 구독 → 재렌더
                }
                return tile;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} 로드 실패: {e.Message}");
                return null;
            }
            finally
            {
                _inflight.Remove(buildableId);
            }
        }

        public static void Reset()
        {
            _cache.Clear();
            _inflight.Clear();
            TileLoaded = null;
        }
    }
}
```

**특징**:
- **lazy on-demand**: 앱 시작 시 아무 로드 없음. 실제 렌더링 또는 `EnsureLoadedAsync` 호출 시에만 로드
- **`Get(id)`가 자동 트리거**: 렌더러가 미스해도 별도 설정 없이 백그라운드 로드가 자동 시작. 호출자는 null 반환만 안전하게 처리하면 됨
- **`TileLoaded` 이벤트**: `MapManager`가 구독해서 활성 청크를 재렌더 → 잠시 안 보였던 오브젝트가 자동으로 채워짐 (Ground 지면은 이미 정상이었고 Object 레이어만 업데이트)
- **`_inflight` 중복 방지**: 여러 호출자가 동시에 같은 Id를 요청해도 실제 Addressable 호출은 1회
- **`EnsureLoadedAsync`**: `TryFinishAsync`처럼 await 가능한 경로에서 오브젝트 표시 지연을 피하고 싶을 때 사용 (권장). 렌더 경로는 자동 트리거로 충분

---

### 2.1-B `MapManager_Renderer.cs` 패치

**위치**: [MapManager_Renderer.cs:92-99](../Assets/Scripts/Map/MapManager_Renderer.cs#L92-L99)

**Before**:
```csharp
if (objectId > 0 && _themeTileSet.ObjectSet != null &&
    objectId < (ulong)_themeTileSet.ObjectSet.Length)
{
    _tempObjectTileArray[index] = _themeTileSet.ObjectSet[objectId];
}
```

**After**:
```csharp
if (objectId > 0)
{
    TileBase? tile = BuildableTileRegistry.Get((int)objectId);
    if (tile == null
        && _themeTileSet.ObjectSet != null
        && objectId < (ulong)_themeTileSet.ObjectSet.Length)
    {
        tile = _themeTileSet.ObjectSet[objectId];  // 레거시 (Npc=2 등)
    }
    _tempObjectTileArray[index] = tile;
}
```

- 동기 조회, 하루 성능 영향 없음 (Dictionary Lookup 1회)
- Registry에 없으면 레거시 `ObjectSet`으로 fallback → Stone/Npc/WoodWall 기존 동작 유지
- 둘 다 없으면 `null` 할당 → Object 레이어만 공란 (Ground 지면 타일은 별도 레이어라 정상 표시)

---

### 2.2 `VillageStorageComponent.cs` (신규)

**경로**: `Assets/Scripts/Common/Component/VillageStorageComponent.cs`

```csharp
#nullable enable

namespace ARPG.Component
{
    public struct VillageStorageComponent
    {
        public int VillageId;

        // 자원 수치 (정수)
        public int FoodAmount;
        public int WoodAmount;
        public int StoneAmount;

        // 자원 Cap (기본 50, ResourceCaps로 오버라이드 가능)
        public int FoodCap;
        public int WoodCap;
        public int StoneCap;

        // Stone은 5시간당 +1 → 시간 누적 카운터 (0~4)
        public int StoneTimer;

        // Food 0 유지된 게임시간 누적 (24h 넘으면 경고 1회)
        public int HungerHoursAccumulated;

        // Cap 도달 여부 비트 플래그
        public byte SurplusFlags;
    }

    public static class VillageSurplusFlags
    {
        public const byte Food = 1 << 0;
        public const byte Wood = 1 << 1;
        public const byte Stone = 1 << 2;
    }
}
```

> CLAUDE.md 규칙에 따라 pure data struct, 로직 없음. **float 없음** — 모든 수치는 정수.

---

### 2.3 `VillageData.cs` 수정

**위치**: [Assets/Scripts/Village/VillageData.cs](../Assets/Scripts/Village/VillageData.cs)

**추가 필드 (모두 정수, 소수 버퍼 없음)**:
```csharp
public Dictionary<GlobalEnum.ItemType, int> ResourceCaps = new();
public int HungerHoursAccumulated;
public int StoneTimer;          // 5시간 누적 카운터 (0~4)
public float RegisteredAt;      // 게임시간 (float 허용: 등록 시각 기록용)

// Phase A: 첫 Campfire 제작 상태
public bool HasCampfire;
public float FirstBuildStartedAt = -1f;   // -1 = 미착수. 게임시간 저장
public int FirstBuildTileX;
public int FirstBuildTileY;

[JsonIgnore] public int EntityId = -1;
```

**생성자 초기화 추가**:
```csharp
public VillageData(int villageId, Vector2 position)
{
    VillageId = villageId;
    Position = position;
    Stage = VillageStage.Settlement;
    Population = 0;
    TableId = 0;
    HasBeenPopulated = false;
    DepletedAt = 0f;
    HungerHoursAccumulated = 0;
    StoneTimer = 0;
    RegisteredAt = 0f;
    HasCampfire = false;
    FirstBuildStartedAt = -1f;
}
```

> `RegisteredAt`과 `FirstBuildStartedAt`은 "게임시간 기록"이라 float 유지 (`AR.s.Time.CurrentGameTime` 자체가 float). **자원 수치는 전부 int**.

---

### 2.4 `ComponentManager.cs` 수정

**위치**: [Assets/Scripts/Manager/ComponentManager.cs](../Assets/Scripts/Manager/ComponentManager.cs) 초기화 블록 (라인 20-74)

**추가**:
```csharp
_componentPools[typeof(VillageStorageComponent)] = new SparseSet<VillageStorageComponent>(32);
```

> `using ARPG.Component;` 추가 필요 확인.

---

### 2.5 `VillageManager.cs` 수정

#### 2.5.1 상수 정의 (클래스 최상단)
```csharp
private const int DEFAULT_RESOURCE_CAP = 50;
```

#### 2.5.2 `RegisterVillage` 확장

**Before** (라인 37-50):
```csharp
public void RegisterVillage(int villageId, Vector2 position, int tableId = 0)
{
    if (_villages.ContainsKey(villageId))
    {
        Debug.LogWarning($"[VillageManager] Village {villageId} already registered");
        return;
    }

    VillageData data = new VillageData(villageId, position)
    {
        TableId = tableId
    };
    _villages[villageId] = data;
}
```

**After**:
```csharp
public void RegisterVillage(int villageId, Vector2 position, int tableId = 0)
{
    if (_villages.ContainsKey(villageId))
    {
        Debug.LogWarning($"[VillageManager] Village {villageId} already registered");
        return;
    }

    VillageData data = new VillageData(villageId, position)
    {
        TableId = tableId,
        RegisteredAt = AR.s.Time.CurrentGameTime,
    };
    _villages[villageId] = data;

    CreateStorageEntity(data);
}

private void CreateStorageEntity(VillageData data)
{
    int entityId = EntityIdHelper.CreateEntity();
    data.EntityId = entityId;

    VillageStorageComponent storage = new VillageStorageComponent
    {
        VillageId = data.VillageId,
        FoodAmount = GetInt(data, GlobalEnum.ItemType.Food),
        WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood),
        StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone),
        FoodCap = GetCap(data, GlobalEnum.ItemType.Food),
        WoodCap = GetCap(data, GlobalEnum.ItemType.Wood),
        StoneCap = GetCap(data, GlobalEnum.ItemType.Stone),
        StoneTimer = data.StoneTimer,
        HungerHoursAccumulated = data.HungerHoursAccumulated,
        SurplusFlags = 0,
    };
    AR.s.Component.AddComponent(entityId, storage);
}

private static int GetInt(VillageData data, GlobalEnum.ItemType type)
{
    return data.Resources.TryGetValue(type, out int v) ? v : 0;
}

private static int GetCap(VillageData data, GlobalEnum.ItemType type)
{
    if (data.ResourceCaps.TryGetValue(type, out int cap))
        return cap;
    return DEFAULT_RESOURCE_CAP;
}
```

#### 2.5.3 `ProduceResource` / `ConsumeResource` — 컴포넌트 동기화

**Produce After**:
```csharp
public void ProduceResource(int villageId, GlobalEnum.ItemType type, int amount)
{
    if (_villages.TryGetValue(villageId, out VillageData? data) == false)
        return;

    int cap = GetCap(data, type);
    int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
    int newAmount = Mathf.Min(current + amount, cap);

    data.Resources[type] = newAmount;

    SyncStorageComponent(data);
}
```

**Consume After**:
```csharp
public bool ConsumeResource(int villageId, GlobalEnum.ItemType type, int amount)
{
    if (_villages.TryGetValue(villageId, out VillageData? data) == false)
        return false;

    int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
    if (current < amount)
        return false;

    data.Resources[type] = current - amount;

    SyncStorageComponent(data);
    return true;
}
```

> 모든 수치는 정수. `System_VillagePassiveProduction`도 정수 곱셈으로 deltaHour만큼 누적한 뒤 이 API를 통하거나 컴포넌트에 직접 쓴다.

#### 2.5.4 `SyncStorageComponent` 헬퍼
```csharp
private void SyncStorageComponent(VillageData data)
{
    if (data.EntityId < 0)
        return;

    if (AR.s.Component.TryGetComponent<VillageStorageComponent>(data.EntityId, out var storage) == false)
        return;

    storage.FoodAmount = GetInt(data, GlobalEnum.ItemType.Food);
    storage.WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood);
    storage.StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone);
    AR.s.Component.SetComponent(data.EntityId, storage);
}
```

#### 2.5.5 `Load` 마이그레이션

**기존** (라인 134-153) 뒤에 추가:
```csharp
public void Load(List<VillageData> villageDatas)
{
    _villages.Clear();
    if (villageDatas == null) return;

    for (int i = 0; i < villageDatas.Count; i++)
    {
        VillageData data = villageDatas[i];

        if (data.TableId <= 0)
            data.TableId = data.VillageId + 1;

        // Phase A: 하위호환
        if (data.ResourceCaps == null)
            data.ResourceCaps = new Dictionary<GlobalEnum.ItemType, int>();
        if (data.RegisteredAt <= 0f)
            data.RegisteredAt = AR.s.Time.CurrentGameTime;

        // Phase A: FirstBuildStartedAt 기본 -1 보정 (구 세이브는 0으로 역직렬화될 수 있음)
        if (data.HasCampfire == false && data.FirstBuildStartedAt == 0f)
            data.FirstBuildStartedAt = -1f;

        _villages[data.VillageId] = data;
        CreateStorageEntity(data);
    }

    Debug.Log($"[VillageManager] Loaded {_villages.Count} villages");
}
```

#### 2.5.6 `Reset`에서 엔티티 정리

```csharp
public void Reset()
{
    foreach (VillageData data in _villages.Values)
    {
        if (data.EntityId >= 0)
        {
            EntityIdHelper.DestroyEntity(data.EntityId, false);
            data.EntityId = -1;
        }
    }
    _villages.Clear();
}
```

---

### 2.6 `System_VillagePassiveProduction.cs` (신규)

**경로**: `Assets/Scripts/Common/System/System_VillagePassiveProduction.cs`

```csharp
#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    public class System_VillagePassiveProduction : IFixedUpdateSystem
    {
        // 생산 (NPC 1명당 게임시간 1h)
        private const int FOOD_PRODUCE_PER_HOUR = 2;
        private const int WOOD_PRODUCE_PER_HOUR = 1;
        private const int STONE_PRODUCE_EVERY_N_HOURS = 5;  // 5h마다 NPC당 +1

        // 소비
        private const int FOOD_CONSUME_PER_HOUR = 1;

        // 안전장치: 배속·복귀 시 한 번에 반영할 최대 시간
        private const int MAX_DELTA_HOURS_PER_TICK = 1;

        // 기아 경고 임계 (게임시간)
        private const int HUNGER_WARN_THRESHOLD = 24;

        public int Priority => 57;
        public float UpdateInterval => 5.0f;

        private int _lastProcessedHour;

        public void OnCreate()
        {
            _lastProcessedHour = Mathf.FloorToInt(AR.s.Time.CurrentGameTime);
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            int currentHour = Mathf.FloorToInt(AR.s.Time.CurrentGameTime);
            int deltaHours = currentHour - _lastProcessedHour;
            if (deltaHours <= 0) return;
            if (deltaHours > MAX_DELTA_HOURS_PER_TICK)
                deltaHours = MAX_DELTA_HOURS_PER_TICK;

            var villages = AR.s.Village.GetAllVillages();
            foreach (VillageData v in villages)
            {
                if (v.EntityId < 0) continue;
                if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var storage) == false)
                    continue;
                if (v.Population <= 0) continue;

                int pop = v.Population;

                // 정수 곱셈으로 deltaHours 만큼 누적
                int foodDelta = (FOOD_PRODUCE_PER_HOUR - FOOD_CONSUME_PER_HOUR) * pop * deltaHours;
                int woodDelta = WOOD_PRODUCE_PER_HOUR * pop * deltaHours;

                storage.FoodAmount = ApplyCap(storage.FoodAmount + foodDelta, storage.FoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Food);
                storage.WoodAmount = ApplyCap(storage.WoodAmount + woodDelta, storage.WoodCap, ref storage.SurplusFlags, VillageSurplusFlags.Wood);

                // Stone: 5h 누적 카운터 방식
                storage.StoneTimer += deltaHours;
                if (storage.StoneTimer >= STONE_PRODUCE_EVERY_N_HOURS)
                {
                    int cycles = storage.StoneTimer / STONE_PRODUCE_EVERY_N_HOURS;
                    int stoneDelta = pop * cycles;
                    storage.StoneAmount = ApplyCap(storage.StoneAmount + stoneDelta, storage.StoneCap, ref storage.SurplusFlags, VillageSurplusFlags.Stone);
                    storage.StoneTimer -= cycles * STONE_PRODUCE_EVERY_N_HOURS;
                }

                // 기아 경고 (Food 0 유지 24h 경계 크로싱 1회)
                if (storage.FoodAmount <= 0)
                {
                    storage.FoodAmount = 0;
                    int before = storage.HungerHoursAccumulated;
                    storage.HungerHoursAccumulated += deltaHours;
                    if (before < HUNGER_WARN_THRESHOLD && storage.HungerHoursAccumulated >= HUNGER_WARN_THRESHOLD)
                        Debug.LogWarning($"[HungerTick] Village {v.VillageId} exceeded {HUNGER_WARN_THRESHOLD}h without food");
                }
                else
                {
                    storage.HungerHoursAccumulated = 0;
                }

                AR.s.Component.SetComponent(v.EntityId, storage);

                // VillageData 정본 동기화 (세이브/구 API 호환)
                WriteBack(v, storage);
            }

            _lastProcessedHour = currentHour;
        }

        private static int ApplyCap(int value, int cap, ref byte flags, byte bit)
        {
            if (value >= cap)
            {
                flags |= bit;
                return cap;
            }
            flags = (byte)(flags & ~bit);
            return value < 0 ? 0 : value;
        }

        private static void WriteBack(VillageData v, VillageStorageComponent s)
        {
            v.Resources[GlobalEnum.ItemType.Food] = s.FoodAmount;
            v.Resources[GlobalEnum.ItemType.Wood] = s.WoodAmount;
            v.Resources[GlobalEnum.ItemType.Stone] = s.StoneAmount;
            v.HungerHoursAccumulated = s.HungerHoursAccumulated;
            v.StoneTimer = s.StoneTimer;
        }

        public void OnReset()
        {
            _lastProcessedHour = 0;
        }
    }
}
```

---

### 2.7 `System_VillageResource.cs` 삭제

**대상**: [Assets/Scripts/Common/System/System_VillageResource.cs](../Assets/Scripts/Common/System/System_VillageResource.cs)

- 파일 삭제 + `.meta` 삭제
- 역할을 `System_VillagePassiveProduction`이 완전히 대체

---

### 2.8 `SystemManager.cs` 수정

**위치**: [Assets/Scripts/Manager/SystemManager.cs](../Assets/Scripts/Manager/SystemManager.cs) Priority 57 블록

**Before**:
```csharp
// Priority 57: Village resource production (FixedUpdate)
System_VillageResource systemVillageResource = new();
RegisterSystems(systemVillageResource);
```

**After**:
```csharp
// Priority 57: Village passive production / consumption (FixedUpdate, 게임시간 1h 보정)
System_VillagePassiveProduction systemVillagePassive = new();
RegisterSystems(systemVillagePassive);

// Priority 58: Village first-build (Campfire 전용, Phase A MVP)
System_VillageFirstBuild systemVillageFirstBuild = new();
RegisterSystems(systemVillageFirstBuild);
```

---

### 2.9 `VillageTileFinder.cs` (신규)

**경로**: `Assets/Scripts/Village/VillageTileFinder.cs`

```csharp
#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    public static class VillageTileFinder
    {
        public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
        {
            if (IsEmpty(center))
                return center;

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int ax = Mathf.Abs(dx);
                        int ay = Mathf.Abs(dy);
                        if (ax != r && ay != r)
                            continue;
                        Vector2Int candidate = new Vector2Int(center.x + dx, center.y + dy);
                        if (IsEmpty(candidate))
                            return candidate;
                    }
                }
            }
            return null;
        }

        public static bool IsEmpty(Vector2Int tile)
        {
            Vector3 world = new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0f);
            if (AR.s.Map.IsWalkable(world) == false)
                return false;
            return AR.s.Map.GetObjectIdAt(tile.x, tile.y) == 0;
        }
    }
}
```

> **`MapManager.GetObjectIdAt(x, y)` 필요**. 없으면 공개 메서드 추가:
> ```csharp
> public int GetObjectIdAt(int worldX, int worldY)
> {
>     ulong tile = GetTileAt(worldX, worldY);
>     return (int)((tile & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10);
> }
> ```
> PlaceObject가 ObjectLayerMask 비트(10~19)에 ID를 기록하는 것과 대칭.

---

### 2.10 `System_VillageFirstBuild.cs` (신규)

**경로**: `Assets/Scripts/Common/System/System_VillageFirstBuild.cs`

```csharp
#nullable enable
using ARPG.Map;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    public class System_VillageFirstBuild : IFixedUpdateSystem
    {
        // 기획 §4.4
        private const int CAMPFIRE_WOOD_COST = 3;
        private const float CAMPFIRE_BUILD_HOURS = 2f;
        private const int DEFAULT_MAX_RADIUS = 3;

        // BuildableItemTable의 Campfire 엔트리 Id
        private const int CAMPFIRE_BUILDABLE_ID = 100;

        public int Priority => 58;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            var villages = AR.s.Village.GetAllVillages();

            foreach (VillageData v in villages)
            {
                if (v.HasCampfire)
                    continue;
                if (v.Population < 1)
                    continue;

                if (v.FirstBuildStartedAt < 0f)
                {
                    TryStart(v, now);
                    continue;
                }

                float elapsed = now - v.FirstBuildStartedAt;
                if (elapsed < CAMPFIRE_BUILD_HOURS)
                    continue;

                TryFinishAsync(v).Forget();
            }
        }

        private void TryStart(VillageData v, float now)
        {
            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            if (wood < CAMPFIRE_WOOD_COST)
                return;

            VillageTable? table = AR.s.Data.GetVillageTable(v.TableId);
            int maxRadius = table != null ? Mathf.CeilToInt(table.SpawnRadius) : DEFAULT_MAX_RADIUS;

            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            Vector2Int? target = VillageTileFinder.FindEmptyTileNearest(center, maxRadius);
            if (target.HasValue == false)
                return;

            if (AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, CAMPFIRE_WOOD_COST) == false)
                return;

            v.FirstBuildStartedAt = now;
            v.FirstBuildTileX = target.Value.x;
            v.FirstBuildTileY = target.Value.y;

            Debug.Log($"[FirstBuild] v{v.VillageId} 착수: Wood -{CAMPFIRE_WOOD_COST}, tile=({target.Value.x},{target.Value.y}), 완료 예정={now + CAMPFIRE_BUILD_HOURS:F1}h");
        }

        // v.HasCampfire를 먼저 true로 잠가 중복 Forget 호출 방어
        private async UniTask TryFinishAsync(VillageData v)
        {
            if (v.HasCampfire) return;
            v.HasCampfire = true;

            // Addressable 타일 사전 로드 (lazy, 이미 로드돼 있으면 즉시 반환)
            await BuildableTileRegistry.EnsureLoadedAsync(CAMPFIRE_BUILDABLE_ID);

            bool placed = AR.s.Map.PlaceObject(v.FirstBuildTileX, v.FirstBuildTileY, CAMPFIRE_BUILDABLE_ID);
            if (placed)
            {
                v.FirstBuildStartedAt = -1f;
                Debug.Log($"[FirstBuild] v{v.VillageId} Campfire 완성 at ({v.FirstBuildTileX},{v.FirstBuildTileY})");
                return;
            }

            // 자리가 중간에 막힘 → 자원 환불 + 재착수 대기 + HasCampfire 원복
            v.HasCampfire = false;
            AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, CAMPFIRE_WOOD_COST);
            v.FirstBuildStartedAt = -1f;
            Debug.LogWarning($"[FirstBuild] v{v.VillageId} 배치 실패, Wood +{CAMPFIRE_WOOD_COST} 환불 후 재시도 대기");
        }

        public void OnReset() { }
    }
}
```

**`SystemManager` 등록 추가** (Priority 58):
```csharp
System_VillageFirstBuild systemVillageFirstBuild = new();
RegisterSystems(systemVillageFirstBuild);
```

---

### 2.11 `NpcManager.cs` — 변경 없음

`EnsureVillagePopulated`는 기존 로직을 **그대로 유지**한다. Phase A의 Campfire 제작은 별도 시스템에서 자원·NPC 상태를 주기적으로 관찰해 처리하므로, NPC 스폰 훅과 결합할 필요가 없다.

---

### 2.12 `VillageDebugLog.cs` (신규)

**경로**: `Assets/Scripts/Village/VillageDebugLog.cs`

Phase A는 화면 UI 대신 **정적 스냅샷 로그**로 검증한다. 개발자가 필요한 시점에 직접 호출:

```csharp
#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Village
{
    public static class VillageDebugLog
    {
        public static void SnapshotAll()
        {
            foreach (VillageData v in AR.s.Village.GetAllVillages())
                Snapshot(v);
        }

        public static void Snapshot(int villageId)
        {
            VillageData? v = AR.s.Village.GetVillage(villageId);
            if (v == null)
            {
                Debug.LogWarning($"[VillageSnapshot] v{villageId} 존재하지 않음");
                return;
            }
            Snapshot(v);
        }

        private static void Snapshot(VillageData v)
        {
            if (v.EntityId < 0 || AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false)
            {
                Debug.LogWarning($"[VillageSnapshot] v{v.VillageId} StorageComponent 없음");
                return;
            }

            VillageTable? t = AR.s.Data.GetVillageTable(v.TableId);
            int targetPop = t != null ? t.DefaultNpcIds.Count : 0;
            string build = FormatBuild(v, AR.s.Time.CurrentGameTime);

            string food = Fmt("Food", s.FoodAmount, s.FoodCap, (s.SurplusFlags & VillageSurplusFlags.Food) != 0);
            string wood = Fmt("Wood", s.WoodAmount, s.WoodCap, (s.SurplusFlags & VillageSurplusFlags.Wood) != 0);
            string stone = Fmt("Stone", s.StoneAmount, s.StoneCap, (s.SurplusFlags & VillageSurplusFlags.Stone) != 0);

            Debug.Log($"[VillageSnapshot] v{v.VillageId} Stage={v.Stage} Pop={v.Population}/{targetPop} {food} {wood} {stone} Hunger={s.HungerHoursAccumulated}h StoneTimer={s.StoneTimer}/5 Build={build}");
        }

        private static string Fmt(string label, int amount, int cap, bool surplus)
        {
            string tail = surplus ? "*" : "";
            return $"{label}={amount}/{cap}{tail}";
        }

        private static string FormatBuild(VillageData v, float now)
        {
            if (v.HasCampfire)
                return "✓완성";
            if (v.FirstBuildStartedAt < 0f)
                return "대기";
            float elapsed = now - v.FirstBuildStartedAt;
            float total = 2f;
            float remain = Mathf.Max(0f, total - elapsed);
            int pct = Mathf.Clamp(Mathf.FloorToInt(elapsed / total * 100f), 0, 100);
            return $"제작중 {pct}%(남은 {remain:F1}h, tile={v.FirstBuildTileX},{v.FirstBuildTileY})";
        }
    }
}
```

**호출 방법**:
- 개발 중 에디터에서 `MenuItem` 추가(선택)로 단축키 바인딩, 혹은 테스트 스크립트에서 직접 호출
- 사용자가 이후 UI를 추가하면 그 UI의 갱신 경로에 같은 데이터 접근 패턴(`VillageStorageComponent` TryGetComponent)을 그대로 재사용하면 된다

---

### 2.13 `MapManager.cs` — 보조 헬퍼 확인/추가

`VillageTileFinder`와 `System_VillageFirstBuild.TryFinish`가 "이 타일에 이미 오브젝트가 있는가?"를 확인하려면 **타일의 raw ObjectId 조회**가 필요하다. 없으면 공개 메서드 추가:

```csharp
public int GetObjectIdAt(int worldX, int worldY)
{
    ulong tile = GetTileAt(worldX, worldY);
    return (int)((tile & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10);
}
```

- PlaceObject가 `(objectId << 10) & ObjectLayerMask`로 기록하는 것의 대칭 연산
- 반환값 0 = 빈 타일. 0 초과 = `BuildableItemTable.Id` 또는 (특수 케이스) 기존 Stone/WoodWall 레거시 값

> 이미 존재하는 조회 경로 확인 후 이 헬퍼를 추가하거나, 동일 역할의 기존 메서드가 있으면 그것을 사용.

---

## 3. 데이터 테이블 작업

### 3.1 `VillageTable` 엔트리 점검

| 필드 | Phase A 권장값 | 비고 |
|------|--------------|------|
| `DefaultNpcList` | `"1"` 등 1명 | 문서 §1 "Founder 1명" |
| `RespawnCooldown` | `12` (게임시간 h) | 튜닝 포인트 |
| `SpawnRadius` | `3` | 시드 배치도 이 값 사용 |

> CSV/Google Sheets에서 Stage0 마을 로우가 존재하는지 확인. 없으면 기본 1명 마을 엔트리 추가.

### 3.2 `BuildableItemTable`에 Campfire 엔트리 추가 **(Phase A 필수)**

구글 시트(또는 CSV)의 `BuildableItemTable`에 다음 행을 추가:

| 필드 | 값 | 비고 |
|------|-----|-----|
| `Id` | **100** | `CAMPFIRE_BUILDABLE_ID` 상수와 일치. 10비트(1023) 이내 유지 |
| `Name` | `Campfire` | 조회용 |
| `Tooltip` | `마을을 밝히는 첫 모닥불` | 임시 |
| `IsBreakable` | `false` (Phase A는 파괴 없음) | Phase F에서 true |
| `HP` | `50` | 임시 |
| `DropItemId` | `0` | 없음 |
| `Size_Width` | `1` | 1×1 |
| `Size_Height` | `1` | 1×1 |
| `Recipe` | `0` | Phase A는 하드코딩 Wood 3, Recipe 시스템은 Phase B |
| `Function` | `0` | Phase A는 기능 없음 |
| `ResourceName` | Campfire CustomTile의 Addressable 키 또는 경로 | 실제 값은 에셋 제작 후 기입 |

> `BuildableItemTable` 로드 코드는 이미 [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs)에 구현돼 있음. 테이블 행만 추가하면 `AR.s.Data.GetBuildableItem(100)`으로 조회 가능.

### 3.3 (Phase A 에서는 생성하지 않음)
- `ObjectTable` (범용 배치 오브젝트 테이블) — Phase B에서 `BuildableItemTable` 확장 또는 신설
- Stage 테이블 (전용) — Phase C

---

## 4. 에셋 작업

### 4.1 타일 에셋 (`CustomTile`)

| 파일 | BuildableItemTable.Id | Addressable 키 | IsWalkable |
|------|----------------------|----------------|-----------|
| `Assets/Art/Tiles/Village/Campfire.asset` | **100** | `Tiles/Village/Campfire` (= `ResourceName`과 일치) | true |

- 스프라이트는 임시 placeholder 사용 가능 (단색 + 텍스트 라벨)
- Unity **Addressables Groups** 창에서 이 CustomTile을 그룹에 추가하고 **Address**를 `Tiles/Village/Campfire`로 설정
- 빌드 시 Addressable 콘텐츠 빌드 필요 (일반적 Addressables 파이프라인과 동일)
- `ThemeTileSet.ObjectSet` **수정 불필요**. 기존 Stone/Npc/WoodWall만 ObjectSet에 남아있고 신규 오브젝트는 Addressable 경로로 로드됨

### 4.2 프리팹

Phase A에는 프리팹 작업 없음. 디버그 UI는 사용자가 이후에 추가한다.

---

## 5. Boolean 비교 규칙 (프로젝트 규칙)

모든 신규 코드에서 **`!` 연산자 금지**. 예시:

```csharp
// ❌
if (!condition)

// ✅
if (condition == false)
```

```csharp
// ❌
if (!dict.ContainsKey(key))

// ✅
if (dict.ContainsKey(key) == false)
```

---

## 6. 테스트 체크리스트 (DoD와 동기화)

### 6.1 스모크 테스트

- [ ] `Play` 진입 시점엔 콘솔에 `[VillageManager] Initial village …`, `[EnsureVillagePopulated] …`만 뜨고 **Campfire 없음**
- [ ] 씬에는 NPC만 보이고 오브젝트 타일 없음
- [ ] `VillageDebugLog.SnapshotAll()` 호출 → `Pop=1/1 Food=X/50 Wood=X/50 Stone=X/50 … Build=대기` 로그 (X는 정수)
- [ ] Pop=1로 3h 체류 → Wood=3 도달 → 콘솔에 `[FirstBuild] v0 착수: Wood -3` 로그가 자동으로 뜸
- [ ] Pop=1로 10h 체류 시 스냅샷 검증: **Food=10, Wood=10, Stone=2, StoneTimer=0** (Stone은 5h, 10h에 각 +1)
- [ ] 착수 후 게임시간 2h 경과 → `[FirstBuild] v0 Campfire 완성` 로그가 자동으로 뜨고, 씬의 타일에 Campfire 스프라이트 출현
- [ ] 완성 후 스냅샷 호출 → `Build=✓완성` 표시

### 6.2 경계 조건

- [ ] 마을 중심 주변이 모두 돌벽인 맵 → 자원이 쌓여도 제작 착수 로그가 안 뜸 (조용히 대기). 한 타일 뚫어주면 다음 틱에 자동 착수
- [ ] 제작 중(StartedAt 기록 상태) 세이브 → 재시작 → 로드 → 남은 시간 정확히 이어서 완료 로그
- [ ] NPC 전멸 → 쿨타임 경과 후 재스폰 → `HasCampfire == true`면 **재제작 로그 안 뜸**
- [ ] 제작 착수 직후 타겟 타일에 돌을 배치 → 완료 시점에 `PlaceObject` 실패 → `Wood +3 환불 후 재시도 대기` 경고 로그
- [ ] Pop 10, Food 0 상태로 시간 가속 → 24h 시점에 `[HungerTick] … exceeded 24h without food` 경고 1회 (중복 안 뜸)
- [ ] Pop 5, 10h 체류 → Wood 50에서 정지 (cap), 스냅샷 `Wood=50/50*` 별표 표시

### 6.3 회귀

- [ ] 기존 `System_VillageResource` 삭제 이후 다른 시스템 참조 없음 (컴파일 에러 0)
- [ ] 구 세이브(Phase A 이전 세이브) 로드 성공 + 마이그레이션 적용 (`FirstBuildStartedAt` → -1)

---

## 7. 롤백 플랜

Phase A 배포 후 회귀 발견 시:
1. `SystemManager`에서 `System_VillagePassiveProduction` 및 `System_VillageFirstBuild` 등록 제거

`VillageData` 신규 필드는 세이브에 남아도 이후 Phase A 재투입 시 재사용 가능 (하위호환 유지). `NpcManager`는 애초에 수정하지 않았으므로 롤백할 것 없음. `VillageDebugLog`는 호출자가 없으면 자동으로 비활성(정적 유틸).

---

## 8. 연관 Phase로 넘어가는 핸드오프

Phase A 완료 시점에 **Phase B 팀**에 넘겨야 하는 것:

| 산출물 | 용도 |
|--------|------|
| `VillageStorageComponent` 표준 Read API | Phase B 생산 오브젝트가 Cap/Amount 조회 / 사용자의 이후 UI도 동일 경로 사용 |
| `VillageTileFinder` 유틸 | Phase B의 `ObjectPlacementTaskComponent`가 동일 빈타일 탐색 로직을 공유/확장 |
| `System_VillageFirstBuild`의 (자원 예약 → 타이머 → 완료/환불) 패턴 | Phase B의 **다수 오브젝트용 `System_NpcCrafting`이 일반화할 1-인스턴스 레퍼런스** |
| `VillageData.HasCampfire` 외 신규 필드 | Phase B 오브젝트 배치 기록 구조 설계 시 참고 (Phase B는 `PlacedObjectComponent`로 범용화) |
| `BuildableItemTable.Id` 직접 사용 패턴 | Phase B에서 **ObjectType enum 확장 없이** 새 오브젝트를 테이블 행만으로 추가 가능 |
| `BuildableTileRegistry` + Addressable 로드 방식 | Phase B에서 수십 개 오브젝트 추가해도 `ThemeTileSet` 손대지 않음. 테이블 행 + Addressable 등록만 |
| `MapManager.GetObjectIdAt` 헬퍼 | Phase B의 오브젝트 세트 판정(`HasObjectSet`)에서 타일 순회 시 재사용 |
| `VillageDebugLog.Snapshot`의 데이터 접근 패턴 | 사용자가 이후 추가할 디버그 UI가 같은 포맷/같은 조회 경로를 재사용 |

---

## 9. 작업 예상 공수

| 작업 | 예상 |
|------|------|
| 코드 구현 (§2.2~§2.13) | 0.4일 |
| `BuildableTileRegistry` lazy 캐시 + 자동 트리거 + 이벤트 (§2.1-A) | 0.2일 |
| `MapManager_Renderer` 패치 (§2.1-B) | 0.1일 |
| `MapManager`에서 `TileLoaded` 이벤트 구독 → 활성 청크 재렌더 | 0.2일 |
| `BuildableItemTable` Campfire 행 추가 | 0.1일 |
| `MapManager.GetObjectIdAt` 추가 또는 조회 경로 확정 | 0.2일 |
| Campfire CustomTile 에셋 + Addressable 등록 | 0.2일 |
| 테스트·튜닝·세이브 호환 검증 | 1일 |
| **합계** | **~2.4일** |

> Addressables 사용이 처음이면 +0.3일 (그룹 설정·빌드 파이프라인 이해).
> 활성 청크 재렌더 API(`ForceRedrawActiveChunks` 또는 유사)가 없으면 간단 구현 +0.2일.
> 디버그 UI 추가는 사용자가 이후 진행 (별도 공수).
