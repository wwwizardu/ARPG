# Phase B — 오브젝트 배치 (핵심) 상세 기획 ✅ 완료 (2026-04-24)

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행 Phase: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅ 완료 · [archive/PHASE_A_HYBRID_PLAN.md](archive/PHASE_A_HYBRID_PLAN.md) ✅ 완료
>
> **목표**: Phase A가 증명한 "NPC 1명 → Campfire 1개" 단일 루프를 **다수 오브젝트의 순차 배치 큐**로 일반화한다. 하드코딩된 Stage 0→1 로드맵을 따라 Bedroll, Bed, Woodpile, CropPlot, Chest, Bed 2, Well을 자동으로 짓게 만든다.

---

## 1. Phase B 범위

### 1.1 Phase B가 하는 것

1. **범용 배치 큐**: `ObjectPlacementTaskComponent`를 통해 마을 1개당 여러 오브젝트를 순차 건설. Phase A의 `System_VillageFirstBuild`는 범용 `System_VillageBuildQueue`로 흡수.
2. **하드코딩 로드맵**: Stage 0(Settlement)에서 Stage 1(Hamlet)로 가는 우선순위 시퀀스를 테이블이 아니라 **코드 상수 배열**로 선언. Phase D의 필요도 스코어링으로 교체하기 전 과도기.
3. **오브젝트 종류 확장**: 생활(Bedroll, Bed, Well), 저장(Woodpile, Chest), 생산(CropPlot) 6종 추가. 모두 1×1.
4. **배치 위치 자동 탐색**: 중심 나선형(현 `VillageTileFinder.FindEmptyTileNearest`)을 **다수 후보 반환**으로 확장해 연속 배치 시 타일 집중화 방지.
5. **Cap 확장**: Woodpile, Chest가 완성되면 해당 마을의 자원 Cap이 즉시 증가. Phase A의 고정 Cap(50)이 동적으로 변한다.
6. **하이브리드 경로 재사용**: `SpawnType=Tile`(나무 벽처럼 반복 정적 구조)과 `SpawnType=Entity`(Campfire처럼 개별 오브젝트)를 테이블로 분기 — Phase A의 `BuildingManager` / `BuildingFactory`를 그대로 사용.
7. **공용 `Prefabs/Entity` 유지**: Phase A에서 이미 엔티티·건물 공용으로 통합 완료. Phase B는 신규 프리팹 제작 없이 오브젝트 Sprite만 추가.

### 1.2 Phase B가 **하지 않는** 것 (후속 Phase 이관)

- **Tier 승격 실행**: 조건 만족 시 Stage를 실제로 올리는 건 Phase C (`System_VillageTierProgression`). Phase B는 조건을 **로그로만 표시**.
- **외곽 벽/경계**: Palisade, StoneWall, 게이트, 망루 — Phase C.
- **필요도 스코어링**: `System_VillageNeedsEvaluation`, 직업 수요, 세트 완성 보너스 — Phase D (Phase B는 고정 로드맵 배열만).
- **오브젝트 세트 판정**: `VillageManager.HasObjectSet`, `ProvidedService` 플래그 — Phase D.
- **NPC 실제 이동·애니메이션**: Phase A와 동일한 **마을 단위 추상 타이머**. 누가 만드느냐는 "NPC가 하나라도 살아있으면 진행". Phase E(배후 시뮬레이션)에서 구체·추상 양 경로를 통합할 때 본격화.
- **제작 중 시각 표현**(재료 무더기 → 골조 → 완성): Phase B는 완료 시 한 번에 스폰. Phase C~D에서 추가 여지.
- **오브젝트 파괴/재건 루프**: Phase F.

> **Phase B의 한 줄 범위: "Phase A 루프를 N번 돌리는 것."**

---

## 2. 기존 시스템과의 차이

### 2.1 현재 상태 (2026-04-24 기준)

| 항목 | 현재 구현 | 위치 |
|------|-----------|------|
| 배치 큐 | 없음 (마을당 Campfire 1개 전용 상태 플래그 `HasCampfire`) | [VillageData.cs:29](../Assets/Scripts/Village/VillageData.cs#L29) |
| 배치 시스템 | `System_VillageFirstBuild` — Campfire 단일 루프 | [System_VillageFirstBuild.cs](../Assets/Scripts/Common/System/System_VillageFirstBuild.cs) |
| 배치 위치 탐색 | `VillageTileFinder.FindEmptyTileNearest` — 단일 타일 반환 | [VillageTileFinder.cs:16](../Assets/Scripts/Village/VillageTileFinder.cs#L16) |
| Cap | 고정 50 (자원별), `ResourceCaps` 오버라이드만 지원 | [VillageManager.cs:12,252](../Assets/Scripts/Village/VillageManager.cs#L252) |
| 건물 팩토리 | `BuildingFactory.CreateBuilding` — Entity 경로, 정적/애니 분기 지원 | [BuildingFactory.cs](../Assets/Scripts/Factory/BuildingFactory.cs) |
| 건물 매니저 | `BuildingManager` — 청크 매핑, `IsTileOccupied`, Save/Load | [BuildingManager.cs](../Assets/Scripts/Manager/BuildingManager.cs) |
| `BuildableItemTable` 엔트리 | Id=1 나무벽(Tile), Id=100 Campfire(Entity) 2종 | [BuildableItemTable.bytes](../Assets/_BinaryData/TableData/BuildableItemTable.bytes) |
| `ObjectType` enum | None/Stone/Npc/WoodWall 4종 (Phase A에서 미확장) | [GlobalEnum.cs:28-34](../Assets/Scripts/Common/GlobalEnum.cs#L28-L34) |

### 2.2 Phase B 이후 상태

| 항목 | 변경 |
|------|------|
| `System_VillageFirstBuild` | **삭제**. 범용 `System_VillageBuildQueue`로 대체 |
| `VillageData` | `HasCampfire`/`FirstBuildStartedAt`/`FirstBuildTile*` 필드 **삭제**. 대체로 **마을 엔티티에 0~1개의 `ObjectPlacementTaskComponent`** 부착 (연속 배치 시 Task 엔티티 1개 재사용). 하위 호환은 `VillageData.PlacedObjectTypeIds: List<int>` 누적 리스트로 유지 (이미 어떤 오브젝트를 지었는지 세이브) |
| **`ObjectPlacementTaskComponent`** | **신규 ECS 컴포넌트** — 마을 엔티티에 붙어 현재 진행 중인 배치 작업 1건을 표현. 기존 `FirstBuild*` 필드의 일반화 |
| **`VillageBuildRoadmap`** | **신규 정적 클래스** — Stage별 하드코딩 배열. `GetNextTarget(village)` API |
| **`VillageTileFinder.FindEmptyTiles(center, radius, count)`** | **신규 오버로드** — 링 확장으로 여러 후보 반환. 단건 오버로드는 유지 |
| **`System_VillageBuildQueue`** | **신규** (Priority 58, Phase A의 자리) — 마을당 1 태스크, 자원 체크 → 타일 탐색 → 자원 차감 → 타이머 누적 → 완료 시 `MapManager.PlaceObject` |
| `MapManager.PlaceObject` | **변경 없음** (Phase A 시그니처 유지). `SpawnType`은 `BuildableItemTable`이 책임 |
| `BuildingManager.PlaceBuilding` | **변경 없음**. Phase A에서 이미 범용화됨 |
| **Cap 확장**: `VillageManager.OnObjectPlaced(villageId, tableId)` | **신규** — 완료 콜백. `BuildableItemTable.StorageCap_Food/Wood/Stone` 컬럼을 **데이터 주도**로 읽어 가산 (§7.1). `Function`과 분리 |
| `BuildableItemTable` | 엔트리 6개 추가 (Bedroll, Bed, Woodpile, CropPlot, Chest, Well). 컬럼 **+5**: `Cost_Wood`, `Cost_Stone`, `StorageCap_Food`, `StorageCap_Wood`, `StorageCap_Stone` (§4.2, §4.3) |
| `ObjectType` enum | 확장하지 않는다 (Phase A 결정). 식별은 `BuildableItemTable.Id`가 담당 |
| `Prefabs/Entity` | **재사용** (Phase A 통합 완료). 신규 프리팹 0개 |
| `CustomTile` 에셋 | `SpawnType=Tile`인 오브젝트(현재 없음)에만 필요. 로드맵 6종은 모두 Entity |

> **결정**: 로드맵 6종(Bedroll/Bed/Woodpile/CropPlot/Chest/Well) 전부 `SpawnType=Entity`. 이유는 §6.1.

---

## 3. 범용 배치 큐

### 3.1 `ObjectPlacementTaskComponent` 정의

```csharp
// Assets/Scripts/Common/Component/ObjectPlacementTaskComponent.cs
namespace ARPG.Component
{
    public struct ObjectPlacementTaskComponent
    {
        public int VillageId;           // 소유 마을
        public int TargetTableId;       // BuildableItemTable.Id
        public int TileX;
        public int TileY;
        public float StartedAt;         // 게임시간, -1 = 미착수
        public float BuildDurationHours;// 태스크별 건설 시간 (Phase B는 테이블에서 읽기)
        public int ReservedWoodCost;    // 착수 시 차감한 자원 (환불용)
        public int ReservedStoneCost;
    }
}
```

- **엔티티당 최대 1개**. 마을 엔티티(VillageData.EntityId)에 직접 부착.
- Phase A의 `VillageData.FirstBuildStartedAt/TileX/TileY`를 그대로 승격.
- `ReservedWoodCost/StoneCost`로 환불 경로 단순화 (Phase A는 Wood만, Phase B는 Stone도 쓰는 오브젝트가 생김).
- **Phase A 호환**: `VillageData.HasCampfire`는 `VillageData.PlacedObjectTypeIds.Contains(100)`으로 대체.

### 3.2 `System_VillageBuildQueue` 로직

Priority 58, UpdateInterval 5.0s (Phase A `System_VillageFirstBuild`의 자리).

```
OnFixedUpdate:
    now = AR.s.Time.CurrentGameTime
    for each village v in AR.s.Village.GetAllVillages():
        if v.Population < 1: continue
        if v.EntityId < 0: continue

        hasTask = ComponentManager.HasComponent<ObjectPlacementTaskComponent>(v.EntityId)

        if hasTask == false:
            TryStartNextTask(v, now)
            continue

        task = ComponentManager.GetComponent<ObjectPlacementTaskComponent>(v.EntityId)
        if task.StartedAt < 0f:
            // 착수 전: 자원/자리 대기 상태 (StartedAt == -1)
            // 실제로는 §3.3에서 TryStartNextTask가 바로 StartedAt 세팅하므로 이 경로는 거의 안 탐
            continue

        elapsed = now - task.StartedAt
        if elapsed < task.BuildDurationHours: continue

        TryFinishAsync(v, task).Forget()
```

### 3.3 `TryStartNextTask(village, now)`

```
target = VillageBuildRoadmap.GetNextTarget(v)
if target == null: return          // 로드맵 소진

table = AR.s.Data.GetBuildableItem(target.TableId)
if table == null: return

# 자원 체크
woodCost = GetRequiredWood(table)
stoneCost = GetRequiredStone(table)
if v.Wood < woodCost or v.Stone < stoneCost: return   # 자원 대기

# 타일 탐색 (이미 계획된 타일은 제외)
center = floor(v.Position)
radius = ceil(VillageTable.SpawnRadius)
tile = VillageTileFinder.FindEmptyTileNearest(center, radius)
if tile == null: return            # 자리 대기

# 자원 차감 + 태스크 생성
AR.s.Village.ConsumeResource(v.VillageId, Wood, woodCost)
AR.s.Village.ConsumeResource(v.VillageId, Stone, stoneCost)

ObjectPlacementTaskComponent task = new {
    VillageId = v.VillageId,
    TargetTableId = target.TableId,
    TileX = tile.x,
    TileY = tile.y,
    StartedAt = now,
    BuildDurationHours = target.BuildHours,
    ReservedWoodCost = woodCost,
    ReservedStoneCost = stoneCost
}
AR.s.Component.AddComponent(v.EntityId, task)

log("[BuildQueue] v{id} 착수 '{Name}': Wood -{w}, Stone -{s}, tile={t}, 완료={now+hours}h")
```

### 3.4 `TryFinishAsync(village, task)`

```csharp
async UniTask TryFinishAsync(VillageData v, ObjectPlacementTaskComponent task)
{
    // 중복 방어: 태스크를 먼저 제거 (Phase A의 HasCampfire = true 선제 잠금 패턴)
    AR.s.Component.RemoveComponent<ObjectPlacementTaskComponent>(v.EntityId);

    Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TargetTableId);
    if (table == null) return;

    // Tile 경로라면 사전 로드, Entity 경로는 BuildingFactory가 내부에서 처리
    if (table.SpawnType == GE.BuildableSpawnType.Tile)
        await BuildableTileRegistry.EnsureLoadedAsync(task.TargetTableId);

    bool placed = AR.s.Map.PlaceObject(task.TileX, task.TileY, task.TargetTableId);
    if (placed == false)
    {
        // 자리가 막힘 → 환불
        if (task.ReservedWoodCost > 0)
            AR.s.Village.ProduceResource(v.VillageId, ItemType.Wood, task.ReservedWoodCost);
        if (task.ReservedStoneCost > 0)
            AR.s.Village.ProduceResource(v.VillageId, ItemType.Stone, task.ReservedStoneCost);
        Debug.LogWarning($"[BuildQueue] v{v.VillageId} '{table.Name}' 배치 실패, 환불 후 재시도 대기");
        return;
    }

    // 성공 기록 + Cap 등 효과 반영
    v.PlacedObjectTypeIds.Add(task.TargetTableId);
    AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId);

    Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY})");
}
```

### 3.5 태스크가 1건인 이유

- 마을당 동시 배치 1건 유지는 **추상 타이머** 단순화 + 자원 경쟁 방지.
- 다중 동시 진행은 Phase D(직업 + NPC 실제 배정) 때 자연스럽게 NPC 수 만큼 병렬화된다.
- 엔티티당 1개 컴포넌트 제약도 유지됨 → SparseSet 모델과 정합.

### 3.6 Phase A 필드의 마이그레이션

`VillageData`의 Phase A 전용 필드 처리:

| 기존 필드 | Phase B 처리 |
|-----------|--------------|
| `HasCampfire` | **삭제**. 로드 시 true면 `PlacedObjectTypeIds.Add(100)` 후 필드 제거. [JsonProperty] 없으면 역직렬화 시 무시됨 — 구 세이브도 호환. |
| `FirstBuildStartedAt` | **삭제**. 로드 시 >= 0 이면 "로드 직전 진행 중이던 Campfire 태스크" 로 간주하고 `ObjectPlacementTaskComponent` 재구성 |
| `FirstBuildTileX/Y` | 동일 |

마이그레이션 코드는 `VillageManager.Load` 내부 (기존 Phase A 마이그레이션 블록 근처).

---

## 4. 하드코딩 로드맵

### 4.1 `VillageBuildRoadmap` 정적 클래스

```csharp
// Assets/Scripts/Village/VillageBuildRoadmap.cs
namespace ARPG.Village
{
    public readonly struct RoadmapEntry
    {
        public readonly int TableId;
        public readonly float BuildHours;
        public RoadmapEntry(int id, float hours)
        {
            TableId = id;
            BuildHours = hours;
        }
    }

    public static class VillageBuildRoadmap
    {
        // Stage 0 → Stage 1 (Settlement → Hamlet) 시퀀스
        // VILLAGE_GROWTH_STAGES.md §3.1 + Phase B 하드코딩 주석:
        //   "Bedroll → Bed → Woodpile → CropPlot → Chest → Bed 2 → Well"
        // Campfire(Id=100)는 Phase A에서 특례로 먼저 지어지므로 여기선 생략
        // (PlacedObjectTypeIds에 100이 이미 들어있는 상태를 가정)
        private static readonly RoadmapEntry[] SETTLEMENT_SEQUENCE = new[]
        {
            new RoadmapEntry(101, 1.5f),  // Bedroll
            new RoadmapEntry(102, 3.0f),  // Bed (첫 번째)
            new RoadmapEntry(110, 2.0f),  // Woodpile
            new RoadmapEntry(120, 2.0f),  // CropPlot
            new RoadmapEntry(111, 2.0f),  // Chest
            new RoadmapEntry(102, 3.0f),  // Bed (두 번째, 승격 조건)
            new RoadmapEntry(130, 5.0f),  // Well
        };

        // 마을이 다음으로 지을 오브젝트. 로드맵 소진 시 null.
        public static RoadmapEntry? GetNextTarget(VillageData village)
        {
            // Campfire가 아직 없으면 Campfire가 최우선 (Phase A 호환)
            if (village.PlacedObjectTypeIds.Contains(100) == false)
                return new RoadmapEntry(100, 2.0f);

            // Campfire 외 지은 수를 세서 시퀀스 인덱스 결정
            // (단순 구현: PlacedObjectTypeIds에서 Campfire 제외 후 count → SETTLEMENT_SEQUENCE 인덱스)
            int placedExceptCampfire = 0;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                if (village.PlacedObjectTypeIds[i] != 100)
                    placedExceptCampfire++;
            }

            if (placedExceptCampfire >= SETTLEMENT_SEQUENCE.Length)
                return null;

            return SETTLEMENT_SEQUENCE[placedExceptCampfire];
        }
    }
}
```

- `Bed`가 2번 등장하지만 **같은 TableId를 2개 짓는다**. 로드맵 인덱스는 "이미 지은 개수"로만 판정하므로 자연스럽게 다음 Bed를 가리킨다.
- Stage 1+ 시퀀스는 **Phase C에서 추가** (Stage 전환과 함께). Phase B는 SETTLEMENT_SEQUENCE만.

### 4.2 Cap 확장은 **데이터 주도** (`Function`과 분리)

**원칙**: Cap 증가량은 C# switch가 아니라 테이블 컬럼에서 직접 읽는다.

`BuildableItemTable`에 신규 컬럼 3종:
```csharp
[JsonProperty("StorageCap_Food")]  public int StorageCap_Food  = 0;
[JsonProperty("StorageCap_Wood")]  public int StorageCap_Wood  = 0;
[JsonProperty("StorageCap_Stone")] public int StorageCap_Stone = 0;
```

완료 콜백은 테이블 값을 그대로 가산 (구현은 §7.1):
```csharp
if (table.StorageCap_Food  > 0) AddCap(v, ItemType.Food,  table.StorageCap_Food);
if (table.StorageCap_Wood  > 0) AddCap(v, ItemType.Wood,  table.StorageCap_Wood);
if (table.StorageCap_Stone > 0) AddCap(v, ItemType.Stone, table.StorageCap_Stone);
```

이점:
- **튜닝이 시트 한 곳에 집중** — Woodpile의 +100을 +120으로 바꾸는 데 C# 재컴파일 불필요
- **새 저장 오브젝트 추가 코드 0줄** — Barrel(Food +50), Stockpile(Stone +80) 등은 시트 행만 추가
- **`Function` 스위치의 Cap 관련 case 소거** — `Function`은 "Cap 외 효과"만 담당

`Function` 필드의 남은 역할:

| Function 값 | 효과 | Phase B 처리 |
|------------:|------|--------------|
| 0 | 효과 없음 (Campfire, Bedroll, Bed, Woodpile, Chest) | — |
| 3 | Food 생산 보너스 (CropPlot) | 플래그 기록만. 실제 +0.8/h는 Phase D |
| 4 | 마을 생산 ×1.05 (Well) | 플래그 기록만. 실제 승수는 Phase D |

- **Cap이 Function과 분리**된 결과 Woodpile/Chest의 `Function`은 0이 됨 — 다른 모든 "효과 없음" 오브젝트와 동일한 스키마
- Phase D에서 `Function`을 `ProvidedService` 비트마스크로 확장할 때도 Cap 로직은 영향 없음 (이미 별도 컬럼)
- Phase D의 `ObjectTable` 스키마(VILLAGE_GROWTH_STAGES.md §9.1)와 필드 이름이 **동일** → 이행 시 이름 변경 없이 컬럼 구조 그대로 흡수 가능

### 4.3 필요 자원 컬럼

Phase A의 `CAMPFIRE_WOOD_COST = 3` 상수 대신 `BuildableItemTable`에 신규 컬럼:
```csharp
[JsonProperty("Cost_Wood")]  public int Cost_Wood  = 0;
[JsonProperty("Cost_Stone")] public int Cost_Stone = 0;
```

Phase D의 `RequiredWood/Stone/Metal/Food` 컬럼(VILLAGE_GROWTH_STAGES.md §9.1의 `ObjectTable` 스키마)의 선행 형태. Phase B는 Wood/Stone만 사용.

### 4.4 Stage 1 승격 조건 확인 로그 (Phase C 준비)

로드맵 소진 시점에서 §0 문서의 승격 조건을 체크해 로그 1줄:

```
[TierCheck] v{id} Stage0→1 조건: 인구3+(현재{pop}), Bed2+({bedCount}), Food30+({food}), 24h체류+({hours}h) → {전부 충족 여부}
```

실제 승격은 Phase C에서 `System_VillageTierProgression`이 처리.

---

## 5. 배치 위치 탐색 확장

### 5.1 현재 구현

`VillageTileFinder.FindEmptyTileNearest(center, maxRadius)`:
- 링 확장 BFS
- 첫 번째 빈 타일 반환
- `IsWalkable` + `GetObjectIdAt == 0` + `BuildingManager.IsTileOccupied == false`

### 5.2 Phase B 확장

단건 오버로드는 **그대로 유지**하고, 다수 후보를 위한 오버로드 추가:

```csharp
// 최대 N개의 빈 타일을 링 확장 순서대로 수집. 연속 배치 시 산포 힌트에 사용.
public static int FindEmptyTiles(Vector2Int center, int maxRadius, Vector2Int[] output);
// 반환값: 실제로 채운 개수
```

- `Vector2Int[]` allocation-free: 호출부가 `stackalloc` 또는 pooled buffer 전달
- 시스템 단순화를 위해 **첫 결과(nearest)를 그대로 사용**하는 정책을 Phase B에서 유지 — 즉 `FindEmptyTileNearest` 그대로 써도 됨
- 다중 후보가 필요해지면 §5.3에서 활용

### 5.3 중심 편향 방지 ✅ 완료 (2026-04-24)

**증상**: 결정적 스캔 순서(`dx: -r→r, dy: -r→r`)가 항상 SW 코너부터 시작 → 7개 오브젝트가 한 사분면에 압축돼 시각적으로 부자연스러움.

**해결**: `FindEmptyTileNearest`를 "가장 가까운 비어있지 않은 링 내에서 랜덤 픽"으로 변경. 링 우선순위(중심 선호)는 유지하면서 같은 거리 후보들을 분산 선택.

**향후 발전 단계** (Phase C 또는 필요 시):
- 인접 점유 셀 8방위 페널티 → 건물 간 1칸 간격
- "큰길" 예약 → NPC 통행 통로 보존
- 직업/세트 기반 구역화 (Phase D — Blacksmith 영역, 주거 영역 등)

---

## 6. 오브젝트 목록 (Phase B 추가 7종, Campfire 제외)

### 6.1 `SpawnType` 결정 기준

VILLAGE_GROWTH_STAGES.md §12.9의 Walkable/Blocked 구분과 Phase A의 하이브리드 정책을 엮으면:

| 오브젝트 | Walkable | 고유성 | SpawnType | 근거 |
|----------|---------:|--------|-----------|------|
| Bedroll | No | 저 | **Entity** | 개별 상태(HP, 수리 등) 필요, Campfire와 동등 |
| Bed | No | 저 | **Entity** | 주거 슬롯 소유, 향후 점유 NPC와 연결 |
| Woodpile | No | 저 | **Entity** | Cap 컴포넌트 등 확장 여지 |
| Chest | No | 저 | **Entity** | 상호작용 UI 연결 필요 |
| CropPlot | **Yes** | 저 | **Entity** | Farmer 배치 예정, Walkable=true는 `CustomTile.IsWalkable`이 아니라 Entity 경로에서 Blocked 비트 안 세팅 → 통과 가능 (현행 동작 유지) |
| Well | No | 저 | **Entity** | 생활 기반 서비스 상호작용 |

- **전부 Entity**. Tile 경로는 현재 Id=1 나무 벽만 사용하며, Phase C의 Palisade/StoneWall까지 보존한다.
- Entity는 타일 비트에 흔적이 없으므로 `CustomTile` 에셋 제작 불필요.
- Walkable 제어는 **Entity 경로에서 Blocked 비트를 세팅하지 않는** 현행 동작에 맡긴다. 몬스터/NPC가 CropPlot 위를 지나가게 허용됨 → Phase D의 Farmer 배치 때 예약 로직으로 보완 예정.

### 6.2 `BuildableItemTable` 신규 엔트리

Phase B 신규 컬럼 **5개**: `Cost_Wood`, `Cost_Stone`, `StorageCap_Food`, `StorageCap_Wood`, `StorageCap_Stone`.
`Function`은 Cap 외 효과(Phase D 예약)만 담당.

| Id | Name | HP | Size | Cost_Wood | Cost_Stone | Cap_Food | Cap_Wood | Cap_Stone | Function | SpawnType | Anim |
|---:|------|---:|-----:|----------:|-----------:|---------:|---------:|----------:|---------:|-----------|-----:|
| 100 | Campfire | 50 | 1×1 | 3 | 0 | 0 | 0 | 0 | 0 | Entity | 0 | *(기존 행, Cap 컬럼 0)* |
| 101 | Bedroll | 20 | 1×1 | 2 | 0 | 0 | 0 | 0 | 0 | Entity | 0 |
| 102 | Bed | 40 | 1×1 | 10 | 0 | 0 | 0 | 0 | 0 | Entity | 0 |
| 110 | Woodpile | 30 | 1×1 | 8 | 0 | 0 | **100** | 0 | 0 | Entity | 0 |
| 111 | Chest | 35 | 1×1 | 6 | 0 | 0 | **30** | **30** | 0 | Entity | 0 |
| 120 | CropPlot | 20 | 1×1 | 5 | 0 | 0 | 0 | 0 | 3 | Entity | 0 |
| 130 | Well | 60 | 1×1 | 0 | 15 | 0 | 0 | 0 | 4 | Entity | 0 |

- Woodpile/Chest의 `Function`은 **0**. Cap 효과는 Cap 컬럼이 담당하므로 Function 값 불필요.
- `ResourceName` 컬럼은 표에서 생략 — 각 행의 값은 `Sprites/Items/{Name}` (예: `Sprites/Items/Bedroll`).
- `Recipe` / `AnimationId`는 Phase B는 전부 0. Phase C 이후 애니 필요 시 점진적 부여.
- `ResourceName`은 placeholder sprite 경로 — 단색/실루엣 PNG 1장씩 준비하면 충분.

### 6.3 Addressable 키 추가

| Address | 타입 | 용도 |
|---------|------|------|
| `Sprites/Items/Bedroll` | Sprite | 정적 placeholder |
| `Sprites/Items/Bed` | Sprite | 정적 placeholder |
| `Sprites/Items/Woodpile` | Sprite | 정적 placeholder |
| `Sprites/Items/Chest` | Sprite | 정적 placeholder |
| `Sprites/Items/CropPlot` | Sprite | 정적 placeholder |
| `Sprites/Items/Well` | Sprite | 정적 placeholder |

- 모두 `Assets/Art/Sprites/Items/` 아래 PNG import.
- Addressable 그룹: 기존 `Sprites/Items/Campfire`와 동일 그룹 사용.
- 프리팹 신규 0개 (`Prefabs/Entity` 재사용).

---

## 7. Cap 확장

### 7.1 즉시 반영 경로

`System_VillageBuildQueue.TryFinishAsync` 완료 직후:

```csharp
AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId);
```

`VillageManager.OnObjectPlaced` 구현 — **테이블 컬럼을 그대로 읽는 데이터 주도 방식**:

```csharp
public void OnObjectPlaced(int villageId, int tableId)
{
    if (_villages.TryGetValue(villageId, out VillageData? v) == false) return;
    Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(tableId);
    if (t == null) return;

    // Cap 확장: 테이블 컬럼을 그대로 가산
    if (t.StorageCap_Food  > 0) AddCap(v, GE.ItemType.Food,  t.StorageCap_Food);
    if (t.StorageCap_Wood  > 0) AddCap(v, GE.ItemType.Wood,  t.StorageCap_Wood);
    if (t.StorageCap_Stone > 0) AddCap(v, GE.ItemType.Stone, t.StorageCap_Stone);

    // Function: Cap 외 효과 (Phase D에서 본격 처리)
    // 3 = Food 생산 보너스 (CropPlot), 4 = 생산 ×1.05 (Well)
    // Phase B는 플래그만 기록 — 실제 효과는 Phase D의 직업/세트 시스템
}

private void AddCap(VillageData v, GE.ItemType type, int delta)
{
    int cur = v.ResourceCaps.TryGetValue(type, out int c) ? c : DEFAULT_RESOURCE_CAP;
    v.ResourceCaps[type] = cur + delta;

    // StorageComponent 동기화
    if (v.EntityId >= 0 &&
        AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s))
    {
        if (type == GE.ItemType.Wood)  s.WoodCap  = v.ResourceCaps[type];
        if (type == GE.ItemType.Stone) s.StoneCap = v.ResourceCaps[type];
        if (type == GE.ItemType.Food)  s.FoodCap  = v.ResourceCaps[type];
        AR.s.Component.SetComponent(v.EntityId, s);
    }

    Debug.Log($"[Cap] v{v.VillageId} {type} +{delta} → {v.ResourceCaps[type]}");
}
```

### 7.2 Chest의 "공유 Cap"에 대한 타협

VILLAGE_GROWTH_STAGES.md §2.4는 Chest를 "범용 Cap +30 (Wood/Stone/Metal 공유)"로 설명. 구현 단순화를 위해 **`StorageCap_Wood=30` + `StorageCap_Stone=30`**으로 근사 (테이블 컬럼 조합). Phase D의 본격 저장 시스템에서 정확한 "공유 풀" 구조로 리팩터링.

### 7.3 로드 시 재계산

세이브 데이터에는 `ResourceCaps`가 직접 저장되므로 재계산 불필요. 단, 구 세이브(Cap 증가 전) 호환을 위해:
- `VillageManager.Load` 내부에서 `PlacedObjectTypeIds`를 순회하며 `OnObjectPlaced` 재실행 (Idempotent 하도록 주의 — 이미 반영된 Cap이면 중복 가산 방지)
- 또는 더 간단: `ResourceCaps`가 이미 저장되어 있으면 그대로 신뢰, 로드 후 `OnObjectPlaced`는 **호출하지 않음**

→ **후자 채택**. Phase B 로드맵은 부작용이 Cap 증가뿐이므로 `ResourceCaps` 자체가 정본. 구 세이브는 Cap 50 기본값 유지 → Chest 지을 때 즉시 반영됨.

---

## 8. 시스템 등록

| Priority | 시스템 | Phase | 인터벌 | 상태 |
|---------:|--------|-------|--------|------|
| 57 | `System_VillagePassiveProduction` | A | 5.0s | 유지 |
| 58 | `System_VillageFirstBuild` | A | 5.0s | **삭제** |
| 58 | **`System_VillageBuildQueue`** | B | 5.0s | **신규** (자리 승계) |
| 59 | `System_VillageRespawn` | 기존 | 5.0s | 유지 |

---

## 9. 세이브/로드 영향

### 9.1 신규 필드 (`VillageData`)

```csharp
public List<int> PlacedObjectTypeIds = new();  // 이미 완성된 오브젝트 TableId 누적 (중복 허용)
```

- 로드맵 인덱스 = `PlacedObjectTypeIds` 개수 (Campfire 제외) 로 계산 → 상태 필드 최소화
- **Stage 승격 전/후에 누적되는 값이므로 영구 보존 필수**
- 마이그레이션: 구 세이브에서 `HasCampfire == true` → `PlacedObjectTypeIds.Add(100)` (§3.6)

### 9.2 삭제 필드

| 기존 필드 | 처리 |
|-----------|------|
| `HasCampfire` | `[JsonIgnore]`로 표시 후 마이그레이션 한 번 통과 후 제거. 또는 역직렬화 후 즉시 `PlacedObjectTypeIds.Add(100)` 변환 |
| `FirstBuildStartedAt` | 로드 시 >= 0 이면 `ObjectPlacementTaskComponent` 재구성 (TableId=100, TileX/Y 복원) |
| `FirstBuildTileX/Y` | 동일 |

구 세이브 호환을 위해 Phase B 초기 버전에서는 필드 유지 + `[Obsolete]` 주석 → 다음 릴리즈에서 완전 제거.

### 9.3 `ObjectPlacementTaskComponent` 세이브

**Phase B는 컴포넌트 자체를 세이브하지 않는다**. 이유:
- 마을 엔티티가 로드 시 `VillageManager.Load` → `CreateStorageEntity`에서 재생성되므로 Task 컴포넌트를 같이 재구성하려면 별도 경로 필요
- 단순화: `VillageData`에 `CurrentTaskStartedAt`, `CurrentTaskTableId`, `CurrentTaskTileX`, `CurrentTaskTileY` 4개 필드만 추가해 직렬화 → 로드 시 재빌드
- 이 4필드가 Phase A의 `FirstBuild*` 필드를 **일반화**한 것. 이름만 바꾸면 됨.

**결정**: Phase A의 `FirstBuild*`를 **이름만 변경**해서 재사용:

```csharp
// VillageData (Phase B)
public int CurrentBuildTableId = 0;        // 0 = 미착수
public float CurrentBuildStartedAt = -1f;  // -1 = 미착수
public int CurrentBuildTileX;
public int CurrentBuildTileY;
public int CurrentBuildReservedWood;       // 환불용
public int CurrentBuildReservedStone;      // 환불용
public List<int> PlacedObjectTypeIds = new();
```

→ `ObjectPlacementTaskComponent`는 **매 틱 재구성**되는 런타임 컴포넌트로만 사용. **세이브는 `VillageData`가 정본**. 이 단순화로 마이그레이션이 쉬워짐.

> 대안: 완전한 ECS화 — Phase D 때 `VillageStorageComponent` 리팩터링과 함께 `ObjectPlacementTaskComponent`를 세이브 대상에 포함. Phase B는 범위 밖.

### 9.4 하위 호환

- 구 `HasCampfire == true` → `PlacedObjectTypeIds.Add(100)` 1회 마이그레이션
- `PlacedObjectTypeIds == null` → 빈 리스트로 초기화 (Newtonsoft 기본값)
- `Cost_Wood/Stone` / `StorageCap_Food/Wood/Stone` 컬럼 없는 구 BuildableItemTable → `Parse` 시 기본값 0 (단, Phase B 실행 시점에는 테이블 재다운로드 필수)

---

## 10. 디버그 로그

`[BuildQueue]` 태그로 Phase A의 `[FirstBuild]` 대체:

| 시점 | 로그 | 레벨 |
|------|------|------|
| 태스크 착수 | `[BuildQueue] v{id} 착수 '{Name}': Wood -{w}, Stone -{s}, tile=({x},{y}), 완료={endH}h` | Log |
| 태스크 완료 | `[BuildQueue] v{id} '{Name}' 완성 at ({x},{y})` | Log |
| 배치 실패 + 환불 | `[BuildQueue] v{id} '{Name}' 배치 실패, Wood +{w} Stone +{s} 환불 후 재시도 대기` | Warning |
| 로드맵 소진 | `[BuildQueue] v{id} Stage0 로드맵 완료 — 다음 Phase C 승격 대기` (1회만) | Log |
| 자원/자리 대기 | 매 틱 로그 **금지**. `VillageDebugLog.Snapshot`에서만 노출 |

`VillageDebugLog.Snapshot` 포맷 확장 (Phase A의 `Build=대기/제작중/✓완성` → Phase B는 진행 오브젝트 이름 포함):

```
[VillageSnapshot] v0 Stage=Settlement Pop=2/3 Food=23/50 Wood=5/150 Stone=2/50 Hunger=0 Build=Bed(40%) Placed=Campfire,Bedroll,Bed
```

---

## 11. 리스크와 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| 로드맵 순차 배치가 현실적 타이밍에 안 맞음 (Bed 짓기 전에 Bedroll 파괴) | 저 | Phase B는 파괴 이벤트가 없으므로 해당 없음. Phase F에서 재평가 |
| `PlacedObjectTypeIds` 리스트가 무한 증가 | 저 | Stage 0 → 1 시퀀스는 7개 고정. Phase C 이후도 수십 개 단위 → 세이브 부담 무시 가능 |
| 구 세이브의 `HasCampfire` 마이그레이션 중 `PlacedObjectTypeIds`에 중복 추가 | 중 | 마이그레이션 함수에서 `Contains(100)` 체크 후 추가 |
| `BuildableItemTable` 재다운로드 잊으면 Cost_Wood/Stone이 0 → 자원 소모 없이 즉시 배치 | 중 | `ParseBuildableItemTable`에서 `Cost_Wood` 필드 미존재 시 **컬럼 오류 로그** + fallback 값 적용. DoD에 "테이블 재다운로드 확인" 포함 |
| 마을 중심에 빈 타일 부족 → 태스크 영원히 대기 | 저 | Phase A와 동일: 다음 틱에 자동 재시도. 플레이어가 청소하거나 링 반경이 자연 확장되면 진행 재개 |
| 동시에 여러 마을에서 Task 생성 시 동일 좌표 중복 예약 | 저 | 마을 간 거리가 SpawnRadius의 2배 이상이므로 타일 후보 겹치지 않음. 겹치면 `PlaceObject`가 실패로 반환 → 환불 경로 동작 |
| `ObjectPlacementTaskComponent`의 엔티티 = 마을 엔티티 — 기존 `VillageStorageComponent`와 공존 | 저 | SparseSet은 한 타입당 독립 풀이므로 충돌 없음. Pool 크기만 `<= 32` 동일 |
| `VillageTable.SpawnRadius`가 작아 빈 타일이 곧 고갈 (1~2 수준) | 중 | Phase B는 roadmap 7개만 — SpawnRadius 3이면 충분(링3까지 47칸). Phase C에서 Bounds 동적 확장으로 해결 |
| Cap 증가가 SpritRotation 같이 전파 안 돼 Storage 표기 불일치 | 중 | `OnObjectPlaced`에서 `VillageStorageComponent` 직접 SetComponent. `VillageManager.ProduceResource` 경로에서도 `SyncStorageComponent` 호출 유지 |

---

## 12. 구현 순서 (작업 분해)

Phase A DoD 기준, 하위 순서로 진행. 각 스텝은 컴파일/플레이 통과를 유지한다.

### Step 1 — 데이터 스키마 ✅ 완료 (2026-04-24)
- [x] 1.1 `BuildableItemTable`에 5개 필드 추가 ([Tables.cs:208-217](../Assets/Scripts/Common/Tables.cs#L208-L217))
    - `Cost_Wood`, `Cost_Stone`, `StorageCap_Food`, `StorageCap_Wood`, `StorageCap_Stone`
- [x] 1.2 `DownloadTables.cs`의 `ParseBuildableItemTable` 확장 — 범위 `A:M` → `A:R` ([DownloadTables.cs:44,363-391](../Assets/Scripts/Editor/DownloadTables.cs#L363-L391))
- [x] 1.3 Google Sheets `BuildableItem` 시트 갱신 (Google Sheets MCP로 직접 적용)
    - N~R 5개 컬럼 헤더 추가 + 기존 2행 백필 + 신규 6행 추가
- [x] 1.4 `BuildableItemTable.bytes` 직접 갱신 (시트와 동일 내용 반영) ([BuildableItemTable.bytes](../Assets/_BinaryData/TableData/BuildableItemTable.bytes))
    - 사용자가 향후 `ARPG/Download Table` 실행 시 시트와 동일 결과 보장

**DoD**: ✅ 시트 ↔ bytes 일치. Id=110 Woodpile.StorageCap_Wood == 100 등 검증

### Step 2 — VillageData 확장 + 마이그레이션 ✅ 완료
- [x] 2.1 `VillageData`에 `PlacedObjectTypeIds`, `CurrentBuildTableId/StartedAt/TileX/TileY/ReservedWood/ReservedStone` 추가 ([VillageData.cs](../Assets/Scripts/Village/VillageData.cs))
- [x] 2.2 `FirstBuild*`, `HasCampfire`는 필드 유지 (구 세이브 호환). `HasCampfire`는 Campfire 완성 시 여전히 true 세팅 (외부 시스템 호환)
- [x] 2.3 `VillageManager.Load` 마이그레이션: `HasCampfire==true` && `!Contains(100)` → Add 100, `FirstBuildStartedAt >= 0` → `CurrentBuild*` 승격 ([VillageManager.cs:201-225](../Assets/Scripts/Village/VillageManager.cs#L201-L225))

### Step 3 — ECS 컴포넌트 ✅ 완료
- [x] 3.1 [ObjectPlacementTaskComponent.cs](../Assets/Scripts/Common/Component/ObjectPlacementTaskComponent.cs) 신설
- [x] 3.2 `ComponentManager.Initialize()`에 풀 32 등록 ([ComponentManager.cs:68](../Assets/Scripts/Manager/ComponentManager.cs#L68))

### Step 4 — 로드맵 + VillageManager 확장 ✅ 완료
- [x] 4.1 [VillageBuildRoadmap.cs](../Assets/Scripts/Village/VillageBuildRoadmap.cs) 신설 — `SETTLEMENT_SEQUENCE` 7개
- [x] 4.2 `VillageManager.OnObjectPlaced` + `AddCap` + `SyncTaskToData` + `RestoreTaskFromData` 추가 ([VillageManager.cs](../Assets/Scripts/Village/VillageManager.cs))
- [x] 4.3 `[Cap]` 로그 1줄 출력

### Step 5 — System_VillageBuildQueue 구현 ✅ 완료
- [x] 5.1 [System_VillageBuildQueue.cs](../Assets/Scripts/Common/System/System_VillageBuildQueue.cs) 신설. Priority 58, Interval 5.0s
- [x] 5.2 `System_VillageFirstBuild.cs` 삭제 (+.meta)
- [x] 5.3 `SystemManager.Initialize()`에서 교체 등록 ([SystemManager.cs:54-56](../Assets/Scripts/Manager/SystemManager.cs#L54-L56))
- [x] 5.4 `DataManager`의 세이브 경로에 `SyncTaskToData()` 훅 추가 ([DataManager.cs:300-301](../Assets/Scripts/Manager/DataManager.cs#L300-L301))

### Step 6 — Addressable 자산 준비 ✅ 완료 (사용자 작업)
- [x] 6.1 `Assets/Art/Sprites/Items/` 아래 placeholder PNG 6장 import
    - `Bedroll.png`, `Bed.png`, `Woodpile.png`, `Chest.png`, `CropPlot.png`, `Well.png`
- [x] 6.2 Default Local Group에 `Sprites/Items/{Name}` 키 6개 등록 (기존 Campfire 포함 7종)
- [x] 6.3 Build Addressables 완료

**DoD**: ✅ 런타임에 `Addressables.LoadAssetAsync<Sprite>("Sprites/Items/{Name}")` 7종 모두 성공

### Step 7 — 디버그 로그 정제 ✅ 완료
- [x] 7.1 `VillageDebugLog.Snapshot` 포맷 확장 ([VillageDebugLog.cs](../Assets/Scripts/Village/VillageDebugLog.cs))
    - `Build=` 영역에 현재 진행 중 오브젝트 이름 + 진행률, 또는 다음 후보 이름
    - `Placed=[...]` 누적 리스트 추가
- [x] 7.2 부수 발견 + 수정: `BuildableTileRegistry`의 `Tile/WoodWall` 미등록 키 InvalidKeyException + 다중 awaiter `Already continuation registered` 경합 — `LoadResourceLocationsAsync` 사전 검증 + `_cache`/`_loading` 분리 구조로 단순화 ([BuildableTileRegistry.cs](../Assets/Scripts/Map/BuildableTileRegistry.cs))

### Step 8 — 문서 갱신 ✅ 완료
- [x] 8.1 [VILLAGE_GROWTH_STAGES.md §10 Phase B](VILLAGE_GROWTH_STAGES.md) 체크박스 갱신
- [x] 8.2 본 문서에 완료 마킹 + Step별 ✅/⏳ 상태

---

## 13. DoD (완료 기준)

### 13.1 기본 루프
- [ ] 새 맵 플레이 시작 → 플레이어 개입 없이 로그로 `Campfire → Bedroll → Bed → Woodpile → CropPlot → Chest → Bed → Well` 순차 완성 확인
- [ ] 각 오브젝트의 GameObject가 마을 중심 주변 링 반경에 분산 배치되어 씬에 출현
- [ ] 마지막 Well 완성 후 `[BuildQueue] v{id} Stage0 로드맵 완료 — 다음 Phase C 승격 대기` 로그 1회

### 13.2 자원 시스템
- [ ] Woodpile 완성 시점에 Wood Cap이 150(=50+100)으로 즉시 증가
- [ ] Chest 완성 시점에 Wood/Stone Cap 각 +30
- [ ] 자원 부족 시 Task 생성 안 됨, 로그로 대기 상태 추정 가능 (매 틱 로그는 **없어야** 함)

### 13.3 환불/재시도
- [ ] Task 진행 중 플레이어가 타깃 타일에 돌(맵 에디터) 배치 → 완료 시점에 `배치 실패, 환불 후 재시도 대기` 로그 + 자원 복원
- [ ] 재시도 시 VillageTileFinder가 다른 빈 타일 반환 → 정상 완료

### 13.4 세이브
- [ ] 로드맵 중간(예: Woodpile 완성, CropPlot 30% 진행) 저장 → 종료 → 로드 → CropPlot 이어서 진행 → 완료
- [ ] 구 Phase A 세이브(Campfire만) 로드 → `PlacedObjectTypeIds = [100]` 복원 + Bedroll부터 진행 재개, 크래시 없음

### 13.5 하이브리드 경로 비회귀
- [ ] 맵 에디터로 Id=1 나무 벽 수동 배치 → Tile 경로 그대로, GameObject 안 생김 (Phase A §7.4 재확인)
- [ ] NPC 스폰/이동/HP바 영향 없음

### 13.6 Tier 조건 로그 (Phase C 준비)
- [ ] 인구 3+, Bed 2+, Food 30+, 24h 체류 **모두 충족** 상태에서 `VillageDebugLog.Snapshot` → 승격 가능 여부 표시

---

## 14. 결정 필요 이슈

| # | 이슈 | 제안 기본값 | 비고 |
|---|------|------------|------|
| 1 | `Cost_Wood/Stone` 컬럼을 Phase B에서 도입할지, RoadmapEntry에 포함할지 | **테이블 컬럼 도입** | §4.3, Phase D의 `ObjectTable` 스키마로 자연스럽게 수렴 |
| 1-b | Cap 확장을 `Function` switch로 할지, 테이블 컬럼으로 할지 | **`StorageCap_*` 3개 컬럼 도입** (데이터 주도) | §4.2, §7.1. `Function`은 Cap 외 효과만 담당 |
| 2 | Chest 공유 Cap을 Wood/Stone 각 +30 근사로 갈지, 공유 풀로 갈지 | **각 +30 근사** (`StorageCap_Wood=30` + `StorageCap_Stone=30`) | §7.2. 정확한 공유 풀은 Phase D |
| 3 | `ObjectPlacementTaskComponent` 세이브 | **비세이브, VillageData 필드가 정본** | §9.3 |
| 4 | Bedroll이 Bed와 기능 중복 — 로드맵에서 뺄까? | **유지** (Stage 0 시드 느낌) | §0 전제의 "Bedroll + Campfire로 시작" 서사 보존. Phase C에서 시드 특례로 재배치 |
| 5 | Well(Id=130)의 Function=4(생산 ×1.05)를 Phase B에서 실제 적용할까? | **플래그만 기록, 승수 미적용** | Phase D에서 구현. 지금 넣으면 패시브 생산 수치가 예측 불가 |
| 6 | Phase B 중 CropPlot을 Walkable로 유지할지 | **유지** (현 Entity 경로 기본값) | §6.1. Phase D의 Farmer 점유 로직으로 예약 |
| 7 | 로드맵이 소진된 후의 시스템 거동 | **루프 대기 + 로그 1회** | Phase C의 `System_VillageTierProgression`이 붙기 전까지 정지 상태 허용 |

---

## 15. Phase C 대비 (선행 투자)

Phase B에서 미리 손대두면 Phase C 착수가 쉬워지는 것들:

1. **`VillageComponent`** — §9.1에 언급된 Bounds/ThreatLevel 보유. Phase B에서 **빈 스켈레톤만** 만들어두고 Bounds는 `VillageTable.SpawnRadius` 기반 Rect로 초기화. Phase C에서 볼록껍질로 교체.
2. **`RoadmapEntry`를 테이블화**: `Cost_Wood/Stone` 도입하면 `RoadmapEntry`는 `(TableId, BuildHours)`만 남음. `BuildHours`도 테이블로 옮기면 **RoadmapEntry = int[]** (TableId만) 수준으로 단순화 가능. 그러나 튜닝이 한 곳(시트)으로 집중되는 이점이 있으니 고려.
3. **`[TierCheck]` 로그**: §4.4. Phase C의 `System_VillageTierProgression`이 같은 조건식을 `if`로 판정만 바꿔 재사용.

---

## 16. 한 줄 요약

> **Phase B는 Phase A의 "모닥불 하나"를 "모닥불 + 침낭 + 침대 + 나무 야적장 + 텃밭 + 궤짝 + 침대 + 우물" 로 일반화한다. 새로운 시스템 구조는 없다 — 큐와 로드맵뿐이다.**
