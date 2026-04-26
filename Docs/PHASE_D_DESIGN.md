# Phase D — 플레이어 이득 + 마을 ↔ 플레이어 루프 완성 상세 기획

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행 Phase: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅ · [PHASE_B_DESIGN.md](PHASE_B_DESIGN.md) ✅ · [PHASE_C_DESIGN.md](PHASE_C_DESIGN.md) ✅ (2026-04-26)
>
> **목표**: Phase A~C가 만든 "마을이 자가 성장한다"의 다음 한 걸음 — **플레이어가 마을과 상호작용해서 진짜 이득을 본다**. 마을은 서비스(상점/강화/여관/제단)를 제공하고, 플레이어는 모험에서 얻은 자원을 상점에서 팔아 골드로 바꾼다. 동시에 로드맵을 **필요도 스코어**로 교체하고, NPC가 **직업과 오브젝트로 직무를 갖게** 해서 마을이 "방향성 있는 유기체"로 진화한다.

---

## 1. Phase D 범위

### 1.1 Phase D가 하는 것

1. **`PlacedObjectComponent`** — 배치된 오브젝트의 위치/HP를 ECS로 승격. Phase A~C는 `PlacedObjectTypeIds`(ID-only)만 누적했지만, 세트 판정과 서비스 근접성에는 **위치 인덱스가 필수**.
2. **`PlacedObjectRegistry`** — 마을별 빠른 조회용 두 인덱스: `tableId → List<entityId>`, `tile → entityId`.
3. **세트 판정** — `VillageManager.HasObjectSet(villageId, setType, anchor?)`. 오브젝트 세트의 5×5 그룹 또는 마을 전체 공존 검사.
4. **`ProvidedService` 비트마스크** — `BuildableItemTable.Function`(현재 5~16 placeholder)을 `ProvidedService` enum 비트로 정식 매핑.
5. **`System_VillageServiceProximity`** — 플레이어 근처 활성 PlacedObject의 ProvidedService를 `PlayerNearbyServicesComponent`로 집계. 매 프레임 아닌 0.3s 인터벌.
6. **서비스 UI 오픈 (4종 MVP)** — F키 입력 시 인접 서비스 UI 호출.
   - **상점** (MerchantStall) — **구매 + 판매 양방향**. 매물 풀 + 인벤토리 자원·장비 매입.
   - **강화** (Furnace + Anvil) — 기존 Mod 시스템 재롤/추가.
   - **여관** (InnBed + Hearth) — 세이브 + HP/MP 만회복.
   - **제단** (Shrine) — 단기 버프 1회. 게임시간 12h 쿨.
7. **상점 매입(판매) 시스템** — Shop UI의 판매 탭. 인벤토리 자원/장비 → Gold. 환금이 플레이어의 1차 자원 활용 경로 (별도 기증 시스템 없음 — 상점에서 팔면 마을 Storage에도 일부 반영).
8. **필요도 스코어 도입** — `System_VillageNeedsEvaluation`(2h 인터벌). 하드코딩 로드맵을 점수 기반 동적 선택으로 단계 교체. `VillageBuildRoadmap`은 fallback으로 유지.
9. **배치 구역화** — 직업/세트 기반 클러스터 가산점. "공방 거리", "주거 구역" 자연 발생.
10. **`System_VillageJobAssignment`** — NPC `JobType` × 활성 작업 오브젝트 매칭. `PassiveProduction`이 매칭된 NPC만 직업 보너스 가산.

> **Phase D 한 줄 범위**: "플레이어가 마을 안에서 상점·강화·여관을 쓰고, 모험으로 모은 자원/장비를 상점에 팔아 골드로 바꾸며, NPC들이 자기 직업의 작업 오브젝트 옆에서 일한다."

### 1.2 Phase D가 **하지 않는** 것 (후속 Phase 이관)

- **배후 시뮬레이션** (비활성 청크 자원 누적, 출생, 분가) — Phase E
- **위협도 / 몬스터 침공 / 벽 파괴** — Phase F
- **퀘스트 시스템** — Phase F+
- **명성(Reputation) 시스템** — 기증을 폐기하면서 갱신 경로가 사라져 Phase F(퀘스트 보상)로 이관. Phase D는 Stage·Tier만으로 마을 매력도 표현
- **자원 기증** — 폐기. 플레이어의 자원 활용 경로는 "상점 판매"로 단일화 (§2.5 결정)
- **WatchTower 2×2** — Phase G
- **Stage 4 (City) 승격, StoneWall** — Phase F (벽 파괴 루프와 같이)
- **NPC 일과 시뮬레이션** (수면/식사/일터 이동) — Phase E (`System_AbstractVillageSimulation`이 압축 처리). Phase D는 PassiveProduction에 직업 보너스만 가산하는 수준에서 멈춘다.
- **장비 제작** (재료 → 새 장비) — Phase D는 **강화(재롤)만**. 제작 UI는 Phase F+

---

## 2. 핵심 설계 결정

### 2.1 PlacedObjectTypeIds → PlacedObjectComponent 전환 (가장 큰 변화)

**현재 (Phase B/C)**: `VillageData.PlacedObjectTypeIds`는 `List<int>` — TableId만 누적. Tier 승격의 단순 카운트, BuildQueue 진행률 표시에 충분했음.

**문제**: Phase D의 핵심 기능이 **위치 정보**를 요구한다.
- 세트 판정 ("Furnace와 Anvil이 5×5 안에 같이 있는가?")
- 서비스 근접성 ("플레이어 반경 3타일 이내에 MerchantStall이 있는가?")
- 배치 구역화 (카테고리 클러스터링)

**결정**: `PlacedObjectTypeIds`를 **유지하면서** `PlacedObjectComponent`(ECS) + `PlacedObjectRegistry`(인덱스)를 추가한다.
- `PlacedObjectTypeIds` — 카운트 전용으로 계속 사용 (Tier 승격, BuildQueue 호환). **세이브 정본**.
- `PlacedObjectComponent` — 위치/HP 등 런타임 상태. 세이브에는 좌표만 별도 직렬화 (`List<PlacedObjectSaveData>`).
- 양쪽 동시 갱신은 `OnObjectPlaced`에서 한 번에 처리. 일관성 유지.

> **대안 검토**: `PlacedObjectTypeIds`를 `List<PlacedObjectInfo>` (TableId+TileX+TileY)로 확장. → Phase B/C 코드(카운트 헬퍼) 다수 수정 필요 + 마이그레이션 부담. 양립 방식 채택.

### 2.2 테이블 컬럼 정본화 (No Hardcoded TableId)

> **원칙**: 코드에 `if (id == 160)` 같은 TableId 분기를 두지 않는다. 의미는 모두 **`BuildableItemTable` 컬럼에 데이터로 박는다**. `BuildableItemDefinition` 같은 코드 매핑 헬퍼는 만들지 않는다.

`BuildableItemTable`에 Phase D 컬럼 4개를 신규 추가:

| 컬럼 | 타입 | 의미 |
|------|------|------|
| `ProvidedService` | int (bitmask) | 이 오브젝트가 제공하는 서비스 (`ProvidedService` enum 비트 OR) |
| `Category` | int | `BuildableCategory` enum (Housing/Storage/Production/...) |
| `SetMembership` | int (bitmask) | 이 오브젝트가 어떤 세트의 어떤 역할인가 (`SetMemberTag` 비트 OR) |
| `AssociatedJobType` | int | 이 오브젝트를 작업장으로 쓰는 NPC 직업 (`JobType` enum, 0=None) |

> `Function`(Phase C placeholder)은 `ProvidedService` 본격 도입과 함께 **삭제**한다 (시트에서 컬럼 제거 + Tables.cs/DownloadTables.cs 동기). 마이그레이션 비용 거의 없음 — Phase C에서 게임플레이에 쓰이지 않은 placeholder.

```csharp
[Flags]
public enum ProvidedService : int
{
    None        = 0,
    Housing     = 1 << 0,
    Storage     = 1 << 1,
    Production  = 1 << 2,
    Cooking     = 1 << 3,
    Shop        = 1 << 4,
    Forge       = 1 << 5,
    Quench      = 1 << 6,
    Inn         = 1 << 7,
    Shrine      = 1 << 8,
    Signal      = 1 << 9,
    Civic       = 1 << 10,
    Beacon      = 1 << 11,
}
```

**시트 채울 값 (참고용 데이터 — 코드 분기가 아니라 시트 입력값)**:

| Id | Name | ProvidedService | Category | SetMembership | AssociatedJobType |
|---:|------|-----------------|----------|---------------|-------------------|
| 101 | Bedroll | Housing | Housing | Birth_Bed | 0 |
| 102 | Bed | Housing | Housing | Birth_Bed | 0 |
| 110 | Woodpile | Storage | Storage | 0 | 0 |
| 111 | Chest | Storage | Storage | 0 | 0 |
| 112 | Stockpile | Storage | Storage | 0 | 0 |
| 120 | CropPlot | Production | Production | 0 | Farmer |
| 130 | Well | Beacon | Service | 0 | 0 |
| 140 | ChoppingBlock | Production | Production | 0 | Woodcutter |
| 141 | DryingRack | Production | Production | 0 | Hunter |
| 142 | MiningCart | Production | Production | 0 | Miner |
| 150 | Hearth | Cooking | Cooking | Inn_Hearth \| Birth_Hearth | 0 |
| 151 | MerchantStall | Shop | Service | 0 | Merchant |
| 152 | TownPost | Civic | Service | 0 | 0 |
| 153 | InnBed | Inn | Service | Inn_Bed | 0 |
| 154 | SignalBrazier | Signal | Defense | 0 | 0 |
| 160 | Furnace | Forge | Forge | Forge_Heat | Blacksmith |
| 161 | Anvil | Forge | Forge | Forge_Anvil | Blacksmith |
| 162 | QuenchVat | Quench | Forge | Forge_Quench | 0 |
| 170 | Shrine | Shrine | Service | 0 | 0 |

> 위 표는 **시트에 그대로 입력**할 값. 코드는 `t.ProvidedService`/`t.Category`/`t.SetMembership`/`t.AssociatedJobType`을 직접 읽는다. TableId 상수가 코드 어디에도 등장하지 않는다.

### 2.3 세트 정의 — `SetMemberTag` + `ObjectSetDefinition`

세트 구성도 코드 상수 분기로 박지 않는다. `SetMemberTag` 비트로 "이 오브젝트가 어떤 세트의 어떤 부품인가"를 표현하고, **세트 정의(요구 비트, 거리)는 정적 데이터 사전 1곳**에서 관리:

```csharp
[Flags]
public enum SetMemberTag : int
{
    None         = 0,
    Forge_Heat   = 1 << 0,   // Furnace
    Forge_Anvil  = 1 << 1,   // Anvil
    Forge_Quench = 1 << 2,   // QuenchVat
    Inn_Bed      = 1 << 3,   // InnBed
    Inn_Hearth   = 1 << 4,   // Hearth (Inn 측)
    Birth_Bed    = 1 << 5,   // Bed/Bedroll (Phase E 출생용)
    Birth_Hearth = 1 << 6,   // Hearth (Birth 측. Inn_Hearth와 동시 보유)
    Library_Book = 1 << 7,   // Bookshelf (Phase F+)
    Library_Desk = 1 << 8,   // Desk (Phase F+)
}
```

`ObjectSetDefinition` — 세트 종류별 요구 비트 + 판정 범위. **단일 정적 사전**이라 코드 한 곳에 모인다 (TableId 분기 X):

```csharp
public readonly struct ObjectSetDefinition
{
    public readonly SetMemberTag RequiredMask;
    public readonly int Range;          // 0 = 마을 전체, n>0 = anchor 기준 n×n
    public ObjectSetDefinition(SetMemberTag mask, int range) { RequiredMask = mask; Range = range; }
}

public static class ObjectSetCatalog
{
    public static readonly Dictionary<ObjectSetType, ObjectSetDefinition> All = new()
    {
        [ObjectSetType.ForgeBasic]    = new(SetMemberTag.Forge_Heat, 5),
        [ObjectSetType.ForgeStandard] = new(SetMemberTag.Forge_Heat | SetMemberTag.Forge_Anvil, 5),
        [ObjectSetType.ForgePremium]  = new(SetMemberTag.Forge_Heat | SetMemberTag.Forge_Anvil | SetMemberTag.Forge_Quench, 5),
        [ObjectSetType.Inn]           = new(SetMemberTag.Inn_Bed | SetMemberTag.Inn_Hearth, 0),
        [ObjectSetType.Birth]         = new(SetMemberTag.Birth_Bed | SetMemberTag.Birth_Hearth, 3),
        [ObjectSetType.Library]       = new(SetMemberTag.Library_Book | SetMemberTag.Library_Desk, 5),
    };
}
```

> Birth 세트는 "Bed×2 + Hearth"였지만 비트 1개로 "Bed 존재"만 검사 → 카운트가 필요한 세트는 `HasObjectSet` 시그니처가 추가 인자를 받도록 확장 (Phase E 도입 시점). Phase D는 비트 단순 OR 검사로 충분.

### 2.4 필요도 스코어 — 점진 교체

Phase B/C의 `VillageBuildRoadmap`은 **하드코딩 시퀀스**라 직관적이지만, "잉여 Stone이 있는데 Bed만 짓고 있다" 같은 비효율 발생.

**Phase D 전략**: 로드맵 **fallback 유지** + 점수 기반 후보 선택을 위에 얹는다.

```
GetNextTarget(v):
    candidates = NeedsEvaluation.GetCandidates(v)  // 후보 + 점수
    if (candidates.Count > 0)
        return candidates[0]                        // 최고점
    return VillageBuildRoadmap.GetNextTarget(v)     // fallback
```

후보가 없는 케이스 (정의되지 않은 새 Stage 등)에는 로드맵이 안전망. 이후 Phase E에서 로드맵 완전 제거.

### 2.5 자원 활용 경로 — "상점 판매" 단일화 (기증 시스템 폐기)

**대안 비교**:
| 안 | 흐름 | 단점 |
|----|------|------|
| (A) 기증 | 인벤토리 자원 → 마을 Storage 직접 가산 + 명성 | UI 2개(상점/기증), 마을 자원이 너무 빠르게 가속됨, 마을 자가 성장 의의 약화 |
| (B) **상점 판매** ★ | 인벤토리 자원/장비 → 상점에서 Gold로 매각 | 단순. 마을 Storage는 그대로 자가 누적 + 패시브 생산 의존 |

**채택: (B)**. 이유:
- **루프 단순화** — F키 한 번에 들어가는 Shop UI에서 구매·판매 모두 처리
- **마을 자가 성장 가치 보존** — 마을 자원 누적은 NPC 패시브/직업 보너스로 발생. 플레이어가 Wood 100을 들이부어 즉시 Stage 승격하는 단축 경로 차단
- **명성 시스템 보류** — 기증을 빼면 Phase D에서 Reputation을 갱신할 경로가 사라지므로 Phase F(퀘스트 보상)로 이관. Phase D는 Stage 기반 기본 매력도(Tier×10)만 사용
- **Gold가 1차 자원** — 플레이어는 상점 매물 구매·강화·여관·제단 비용을 Gold로 지불. 자원→Gold→서비스가 자연스러운 경제 사이클
- **마을 Storage 부분 환원** — 상점에서 매각된 자원의 **일부(50%)**가 마을 Storage(Cap 한도 내)에 적립. 직접 기증보다 효율은 낮지만 "내가 판 자원이 마을에 남는다"는 약한 연결감 유지 (자세한 룰 §8)

---

## 3. ECS 컴포넌트 신규/확장

### 3.1 `PlacedObjectComponent` (신규)

```csharp
public struct PlacedObjectComponent
{
    public int VillageId;
    public int TableId;             // BuildableItemTable.Id
    public int TileX;
    public int TileY;
    public int HP;
    public int MaxHP;
    public ProvidedService Service; // BuildableItemTable.ProvidedService 캐시 (Hot path 조회 절약용)
    public SetMemberTag SetMember;  // BuildableItemTable.SetMembership 캐시
    public int UsingNpcEntityId;    // -1 = 미사용. Phase D는 JobAssignment가 갱신
}
```

> `Service`/`SetMember`는 **테이블에서 읽은 값을 부착 시 캐시**. 부착 시점 코드:
> ```csharp
> var t = AR.s.Data.GetBuildableItem(tableId);
> var po = new PlacedObjectComponent {
>     TableId = tableId,
>     Service = (ProvidedService)t.ProvidedService,
>     SetMember = (SetMemberTag)t.SetMembership,
>     // ...
> };
> ```

**부착 시점**: `System_VillageBuildQueue.TryFinishAsync`의 성공 분기에서 `MapManager.PlaceObject` 직후 entity 발급 + 컴포넌트 부착 (벽 제외).

**파괴 시점** (Phase F): HP=0이면 `MapManager.RemoveObject` + entity 제거. Phase D는 파괴 진입점만 비워둔다.

### 3.2 `PlayerNearbyServicesComponent` (신규)

플레이어 엔티티에 부착. `System_VillageServiceProximity`가 0.3s마다 갱신:

```csharp
public struct PlayerNearbyServicesComponent
{
    public ProvidedService AvailableServices;       // 비트 OR
    public int NearestShopEntityId;                 // 상호작용 키 입력 시 사용할 PlacedObject
    public int NearestForgeEntityId;
    public int NearestInnEntityId;
    public int NearestShrineEntityId;
    public int NearestVillageId;                    // 어느 마을에 속한 서비스인지
}
```

→ UI 측에서는 이 컴포넌트만 보면 어떤 서비스 버튼을 켤지 알 수 있다.

### 3.3 `NpcAssignmentComponent` (신규, Phase D 최소형)

```csharp
public struct NpcAssignmentComponent
{
    public int VillageId;
    public int AssignedObjectEntityId;   // -1 = 무직
    public int AssignedTableId;          // 빠른 조회용
    public GlobalEnum.JobType JobType;   // NPC가 가진 직업 (NpcTable.JobType)
}
```

> **주의**: NPC의 실제 위치 이동/일과는 Phase E. Phase D는 "할당만" 한다 — `PassiveProduction`이 컴포넌트를 보고 보너스 가산.

### 3.4 `VillageComponent` 확장

```csharp
public struct VillageComponent
{
    public int VillageId;
    public VillageStage Stage;
    public RectInt Bounds;
    public float ThreatLevel;
    public int WallSegmentCount;
    public int CompletedWallSegments;
    public int LastNeedsEvalGameHour;    // ★ 신규: 필요도 스코어 게이트용
}
```

> Reputation 필드는 **Phase F**(퀘스트 보상으로 명성 갱신 도입 시점)로 보류. Phase D는 Stage 기반 매력도(Tier×10)만 사용.

---

## 4. PlacedObjectRegistry

### 4.1 구조

```csharp
public static class PlacedObjectRegistry
{
    private static readonly Dictionary<int /*villageId*/, VillageIndex> _byVillage = new();
    private class VillageIndex
    {
        public Dictionary<int /*tableId*/, List<int /*entityId*/>> ByTable = new();
        public Dictionary<Vector2Int, int /*entityId*/> ByTile = new();
    }

    public static void Register(int villageId, int entityId, int tableId, Vector2Int tile);
    public static void Unregister(int villageId, int entityId);
    public static List<int>? GetEntitiesByTableId(int villageId, int tableId);
    public static int GetEntityAtTile(int villageId, Vector2Int tile);  // 없으면 -1
    public static List<int> GetAllEntitiesInBounds(int villageId, RectInt bounds);
    public static void Clear(int villageId);
}
```

### 4.2 호출 지점

- **등록**: `System_VillageBuildQueue.TryFinishAsync` 성공 분기
- **해제**: Phase F의 파괴 처리 (Phase D는 미사용)
- **조회**:
  - `VillageManager.HasObjectSet`
  - `System_VillageServiceProximity`
  - `System_VillageNeedsEvaluation`
  - `VillageTileFinder` 카테고리 클러스터 가산점

### 4.3 세이브/로드 호환

런타임 인덱스는 휘발성. 세이브에는 `VillageData.PlacedObjects: List<PlacedObjectSaveData>`만 들어간다:

```csharp
[Serializable]
public class PlacedObjectSaveData
{
    public int TableId;
    public int TileX;
    public int TileY;
    public int HP;
    public int MaxHP;
}
```

`VillageManager.Load`에서 각 마을의 `PlacedObjects`를 순회하며 `EntityIdHelper.CreateEntity` + `PlacedObjectComponent` 부착 + `PlacedObjectRegistry.Register`. 동시에 `PlacedObjectTypeIds`도 카운트 일치 검증 (마이그레이션).

---

## 5. 세트 판정 — `VillageManager.HasObjectSet`

### 5.1 ObjectSetType enum

```csharp
public enum ObjectSetType
{
    ForgeBasic,
    ForgeStandard,
    ForgePremium,
    Inn,
    Birth,                              // Phase E
    Library,                            // Phase F+
}
```

설명(Furnace+Anvil 등)은 §2.3 `ObjectSetCatalog`에 데이터로 정의 — 코드 분기로 박지 않는다.

### 5.2 API

```csharp
// anchor 미지정 → 마을 전체 검사 (Range는 ObjectSetCatalog의 정의값 따름)
public bool HasObjectSet(int villageId, ObjectSetType setType);

// anchor 지정 → ObjectSetCatalog의 Range × Range 영역 검사
public bool HasObjectSet(int villageId, ObjectSetType setType, Vector2Int anchor);
```

### 5.3 구현 핵심 — SetMember 비트 OR

```csharp
public bool HasObjectSet(int villageId, ObjectSetType setType, Vector2Int anchor)
{
    if (ObjectSetCatalog.All.TryGetValue(setType, out var def) == false) return false;

    // 검사할 영역 결정 — Range == 0이면 마을 전체
    List<int> entities;
    if (def.Range == 0)
    {
        entities = PlacedObjectRegistry.GetAllEntitiesInVillage(villageId);
    }
    else
    {
        int half = def.Range / 2;
        RectInt rect = new(anchor.x - half, anchor.y - half, def.Range, def.Range);
        entities = PlacedObjectRegistry.GetAllEntitiesInBounds(villageId, rect);
    }

    // 영역 내 존재하는 SetMember 비트를 모두 OR
    SetMemberTag covered = SetMemberTag.None;
    for (int i = 0; i < entities.Count; i++)
    {
        if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entities[i], out var po))
            covered |= po.SetMember;
    }

    return (covered & def.RequiredMask) == def.RequiredMask;
}

// 마을 전체 오버로드 — anchor 무시
public bool HasObjectSet(int villageId, ObjectSetType setType)
    => HasObjectSet(villageId, setType, Vector2Int.zero);
```

→ 새 세트 추가는 `SetMemberTag` 비트 + `ObjectSetCatalog` 한 줄 + 시트 `SetMembership` 채우기만으로 끝. C# 코드 분기 추가 X.

---

## 6. 서비스 근접성 — `System_VillageServiceProximity`

### 6.1 시스템 정의

```csharp
public class System_VillageServiceProximity : IUpdateSystem  // ★ Update phase (UI 반응성)
{
    public int Priority => 61;          // 60-64 Lifecycle (Phase C 정책)
    public float UpdateInterval => 0.3f; // 매 프레임 X — 0.3s 충분

    private const float SHOP_RANGE_SQR    = 3f * 3f;
    private const float FORGE_RANGE_SQR   = 3f * 3f;
    private const float INN_RANGE_SQR     = 4f * 4f;
    private const float SHRINE_RANGE_SQR  = 5f * 5f;

    public void OnUpdate(float dt) { ... }
}
```

### 6.2 핵심 로직

1. 플레이어 위치 (`AR.s.Data.CurrentPlayerEntityId` → TransformComponent) 조회
2. 플레이어가 어느 마을 안인지 — `VillageManager.FindVillageContaining` (기존 SpawnRadius 사용). **단 1개 마을만 후보**
3. 마을 안이면 `PlacedObjectRegistry.GetAllEntitiesInBounds`로 후보 PlacedObject 수집
4. 각 후보 (`PlacedObjectComponent`)의 `Service` 비트와 거리 검사
5. `PlayerNearbyServicesComponent`의 `AvailableServices` + `Nearest*EntityId` 갱신
6. 마을 밖이면 모두 -1 / None으로 클리어

> **단일 마을 한정**: 플레이어가 두 마을의 SpawnRadius에 동시에 들어가도 가장 가까운 1개만 잡는다. 마을 밖 어디서도 서비스가 잡히지 않는 보장은 §10.3 외곽 보호 마진(비-Defense 오브젝트는 경계로부터 ≥ 2타일 안쪽)으로 자연스럽게 성립한다 — 벽 밖 플레이어는 서비스에 인접할 수 없음.

> **세트 서비스 처리**: Forge UI는 `Furnace`만 있어도 일단 켜되, **`HasObjectSet(ForgeStandard)` 결과로 UI 내부 기능 단계 결정**. NearestForgeEntityId는 가장 가까운 Furnace의 entityId를 가리킨다.

### 6.3 입력 처리

`System_Input` (또는 신규 `System_PlayerInteract`)이 F키 처리:

```csharp
if (inputComponent.Interact && playerHasNearby.AvailableServices != ProvidedService.None)
{
    ServiceUIRouter.Open(playerHasNearby);   // 우선순위: Shop > Forge > Inn > Shrine
}
```

`ServiceUIRouter`는 단순 라우터 — 우선순위에 따라 `AR.s.UI.Show("ShopUI" / "ForgeUI" / "InnUI" / "ShrineUI")` 호출.

> **충돌 케이스**: 한 영역에 여러 서비스 — 가장 가까운 것 우선. UX 명확성 위해 화면 하단에 "[F] 상점 / Tab으로 다른 서비스" 같은 힌트 권장(Phase D 후반).

---

## 7. 서비스 UI 4종

### 7.1 상점 UI (`UIShopMerchant`)

**진입**: F키 + MerchantStall 반경 3타일. UI는 **구매 / 판매 두 탭**.

**매물 풀 (구매 탭)**:
- 풀 조건: `ItemTable.Tier ≤ (int)v.Stage` AND `ItemTable.BasePrice > 0` (BasePrice가 시트의 매물 자격 플래그를 겸함)
- 게임시간 24h마다 풀에서 무작위 N개 추출 — 코드에 아이템 ID 분기 없음, 순수 데이터 필터
- 매물 잔량은 마을당 별도 관리 (`VillageData.MerchantStock: List<MerchantStockEntry>`)

**구매 처리**:
- `finalPrice = BasePrice` (Phase D는 할인 없음 — 명성 시스템 보류)
- 인벤토리 Gold 차감 → `Inventory.AddItem(itemId, count)` → 매물 잔량 감소

**판매 탭** — 자세한 룰은 §8

> Inventory API는 `AR.s.Player.Inventory` 사용 (CLAUDE.md / MEMORY.md 참조).

### 7.2 강화 UI (`UIForge`)

**진입**: F키 + Furnace 반경 3타일. UI 내부에서 `HasObjectSet(ForgeStandard, anchorFurnace)` 검사.

**기능 단계**:
| 단계 | 조건 | 활성 기능 |
|------|------|-----------|
| 1 (기초) | Furnace만 | 장비 분해 (재료 환원) |
| 2 (표준) | + Anvil | Mod 재롤 (Currency 사용) |
| 3 (고급) | + QuenchVat | Mod 추가 (빈 슬롯 채우기) + 재롤 비용 -20% |

**비용**:
- 재롤: 기존 ARPG의 Currency 시스템 (`DropCurrencyTable`) 그대로 사용
- 단계 3 할인: -20% (단계만으로 결정. 명성 의존 없음)

> **외부 의존성**: 기존 `ARPG.Mod` 시스템. Phase D에서 Mod 재롤 API가 없다면 단순 `MultiplyTier`로 임시 구현 후 Phase F에서 본격화.

### 7.3 여관 UI (`UIInn`)

**진입**: F키 + InnBed 반경 4타일. `HasObjectSet(Inn)` 검사 (마을 전체 + Hearth 필요).

**기능**:
1. **세이브** — `AR.s.Data.Save()` 호출 (기존 세이브 시스템 그대로)
2. **휴식** — HP/MP 100% 회복. 게임 시간 +6h 즉시 진행 (Phase E 배후 시뮬과 호환)
3. **빠른 이동** (Phase F+) — 다른 마을의 InnBed로 텔레포트

**비용**: Gold (Tier별 차등)
- Hamlet: 10G
- Village: 25G
- Town: 50G

### 7.4 제단 UI (`UIShrine`)

**진입**: F키 + Shrine 반경 5타일.

**기능**: 1회용 단기 버프. 게임시간 12h 쿨다운(VillageData.LastShrineUseGameTime).

| 버프 | 지속 | 효과 |
|------|------|------|
| 가호의 빛 | 30분 게임시간 | 받는 데미지 -10% |
| 사냥꾼의 정확 | 30분 게임시간 | 치명타 +10% |
| 헤르메스의 발 | 30분 게임시간 | 이속 +20% |

**비용**: Gold (Tier별 차등 — Hamlet 5G / Village 10G / Town 20G).

> 버프는 기존 `BuffTable`/`Buff` 시스템 재사용 (`AR.s.Buff.ApplyBuff(playerEntity, buffId, duration)`).

---

## 8. 상점 매입(판매) 시스템

§7.1의 Shop UI **판매 탭** 상세 스펙. 별도 진입점 없음 — MerchantStall 근접 시 같은 UI 안에서 탭 전환.

### 8.1 UI 흐름 (Shop UI 판매 탭)

UI 구성:
- 좌측: 인벤토리 (판매 가능 아이템만 표시 — `ItemTable.BasePrice > 0`인 자원/장비/소비)
- 우측: 매각 미리보기 (수량 × 단가 → Gold 환산 + 마을 환원 자원 미리보기)
- 가운데: 수량 슬라이더 + "판매" 버튼

### 8.2 매각가 산출 — `ItemTable.SellRatioBp` 컬럼

```
sellPrice = BasePrice × SellRatioBp / 100
```

`SellRatioBp`(basis points / 100)는 시트 컬럼 1개로 정의 — 카테고리 분기 코드 X. 시트에 채울 값:

| 아이템 카테고리 | SellRatioBp |
|-----------------|-------------|
| 자원 (Wood/Stone/Food/Copper/Iron/Herb) | 50 |
| 장비 (Equipment) | 40 |
| 소비 (Consumable) | 40 |
| Gold (Currency) | 0 |
| Quest 아이템 | 0 |

`SellRatioBp == 0` → 매각 불가. UI에서 자동 비활성. 코드는 컬럼 값만 본다.

> **Mod 가치 미반영 결정**: Phase D는 BasePrice만 본다. Mod 가치 반영(레어/희귀 = 가격 ↑)은 Phase F+에서 본격 정책 결정.

### 8.3 마을 Storage 부분 환원 — `ItemTable.ReturnResourceType` + `ReturnRatioBp` 컬럼

환원 매핑도 코드 분기 X. `ItemTable`에 컬럼 2개 추가:

| 컬럼 | 타입 | 의미 |
|------|------|------|
| `ReturnResourceType` | int | 환원 대상 마을 자원 (`ItemType` enum, 0이면 환원 없음) |
| `ReturnRatioBp` | int | 환원 비율 ×100 (50=0.5, 100=1.0). 0이면 환원 없음 |

시트 채울 값:
| 인벤토리 ItemType | ReturnResourceType | ReturnRatioBp |
|-------------------|--------------------|---------------|
| Wood | Wood | 50 |
| Stone | Stone | 50 |
| Food | Food | 50 |
| Copper | Stone | 50 |
| Iron | Stone | 100 |
| Herb | Food | 100 |
| Gold/Equipment/Consumable/Quest | 0 | 0 |

```csharp
// SellItemToMerchant 내부 - 환원 처리
int returnAmount = amount * item.ReturnRatioBp / 100;
if (item.ReturnResourceType != 0 && returnAmount > 0)
    ProduceResource(villageId, (ItemType)item.ReturnResourceType, returnAmount);
```

> **취지**: "내가 판 자원이 마을에 남는다"는 약한 연결감 + 마을 Storage Cap 효용 제공. Cap을 초과하면 해당 잉여분만 폐기되고 Gold는 정상 지급. 새 아이템 추가는 시트 한 행 — 코드 X.

### 8.4 API

```csharp
public class VillageManager
{
    /// <summary>
    /// MerchantStall 상점 매각 처리. 인벤토리 → Gold + 자원 부분 환원.
    /// 반환: 실제 지급된 Gold (실패 시 -1).
    /// </summary>
    public int SellItemToMerchant(int villageId, int itemTableId, int amount)
    {
        Tables.ItemTable? item = AR.s.Data.GetItem(itemTableId);
        if (item == null || item.BasePrice <= 0 || item.SellRatioBp <= 0) return -1;

        // 1. 인벤토리 차감 (실패 시 atomic abort)
        if (AR.s.Player.Inventory.RemoveItem(itemTableId, amount) == false) return -1;

        // 2. Gold 지급 — SellRatioBp 컬럼 직접 적용
        int gold = item.BasePrice * amount * item.SellRatioBp / 100;
        AR.s.Player.Inventory.AddCurrency(GoldItemId, gold);

        // 3. 마을 Storage 부분 환원 — ReturnResourceType/ReturnRatioBp 컬럼 직접 읽기
        if (item.ReturnResourceType != 0 && item.ReturnRatioBp > 0)
        {
            int returnAmount = amount * item.ReturnRatioBp / 100;
            if (returnAmount > 0)
                ProduceResource(villageId, (ItemType)item.ReturnResourceType, returnAmount);  // Cap 초과는 자동 폐기
        }

        AR.s.UI.SetNotify($"판매: {amount} {item.Name} → +{gold}G");
        return gold;
    }
}
```

원자성: 인벤토리 차감 실패 시 다음 단계 진행 안 함. Cap 초과로 환원 일부가 폐기되더라도 Gold는 정상 지급(이건 일부러 — 플레이어 손해 없음).

---

## 9. 필요도 스코어 — `System_VillageNeedsEvaluation`

### 9.1 시스템 정의

```csharp
public class System_VillageNeedsEvaluation : IFixedUpdateSystem
{
    public int Priority => 61;          // 60-64 Lifecycle
    public float UpdateInterval => 5.0f; // 게임시간 2h 게이트 내장

    private const float CHECK_INTERVAL_HOURS = 2f;
}
```

### 9.2 점수 계산 (VILLAGE_GROWTH_STAGES.md §5.2 기반)

```csharp
struct ScoredCandidate { public int TableId; public float Score; }

private static List<ScoredCandidate> EvaluateCandidates(VillageData v)
{
    var list = new List<ScoredCandidate>();

    // 마을의 현재 SetMember 비트 OR — 세트 완성 보너스 계산용 (한 번만 계산)
    SetMemberTag covered = AggregateSetMembers(v);

    foreach (Tables.BuildableItemTable t in StageEligibleTables(v.Stage))
    {
        if (HasResourcesFor(v, t) == false) continue;  // 자원 부족 → 후보 제외

        float score = t.BaseWeight;
        ProvidedService service = (ProvidedService)t.ProvidedService;
        SetMemberTag member = (SetMemberTag)t.SetMembership;

        // 주거 결손
        if ((service & ProvidedService.Housing) != 0)
            score += Mathf.Max(0, v.Population - CountHousing(v)) * 50f;

        // 식량 결손
        if ((service & ProvidedService.Production) != 0)
            score += Mathf.Max(0, FoodDailyConsume(v) - FoodDailyProduce(v)) * 40f;

        // Cap 초과 잉여 (StorageCap_* 컬럼이 있는 오브젝트일수록 잉여 압력 ↑)
        score += SurplusFor(v, t) * 0.3f;

        // 세트 완성 보너스 — 이 오브젝트를 추가하면 어떤 세트가 새로 완성되는가?
        score += SetCompletionBonus(member, covered) * 80f;

        // 벽 결손 × 위협도 — Category=Defense 오브젝트만
        if ((BuildableCategory)t.Category == BuildableCategory.Defense)
            score += WallDeficitRatio(v) * (v.ThreatLevel + 0.1f) * 30f;

        // 직업 수요 — AssociatedJobType 컬럼이 있고 그 직업의 NPC가 있으면 가중
        if (t.AssociatedJobType != 0)
            score += JobDemand(v, (GlobalEnum.JobType)t.AssociatedJobType) * 15f;

        list.Add(new ScoredCandidate { TableId = t.Id, Score = score });
    }
    list.Sort((a, b) => b.Score.CompareTo(a.Score));
    return list;
}

/// <summary>
/// 새 멤버 비트가 이미 커버된 비트와 합쳐졌을 때 ObjectSetCatalog의 어떤 세트가 새로 완성되는지 카운트.
/// 1을 반환하면 1개 세트 완성, 2면 2개. 각 세트당 보너스 가산.
/// </summary>
private static int SetCompletionBonus(SetMemberTag candidateMember, SetMemberTag covered)
{
    if (candidateMember == SetMemberTag.None) return 0;
    SetMemberTag after = covered | candidateMember;
    int newlyCompleted = 0;
    foreach (var def in ObjectSetCatalog.All.Values)
    {
        bool wasComplete  = (covered & def.RequiredMask) == def.RequiredMask;
        bool isComplete   = (after   & def.RequiredMask) == def.RequiredMask;
        if (wasComplete == false && isComplete) newlyCompleted++;
    }
    return newlyCompleted;
}
```

**핵심 포인트**: TableId 분기 0개. 세트 보너스는 `ObjectSetCatalog`를 순회해서 "이 멤버 추가가 어떤 세트의 마지막 퍼즐인가"를 자동 판단. 세트가 추가되어도 이 코드는 변경 불필요 — `ObjectSetCatalog`에 한 줄 추가하고 시트의 `SetMembership`만 채우면 끝.

### 9.3 BuildQueue 통합

`System_VillageBuildQueue.TryStartNextTask` 변경:

```csharp
RoadmapEntry? next;
var candidates = AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc)
    ? VillageNeedsCache.Get(v.VillageId)
    : null;
if (candidates != null && candidates.Count > 0)
    next = ToRoadmapEntry(candidates[0].TableId);
else
    next = VillageBuildRoadmap.GetNextTarget(v);
```

`VillageNeedsCache`는 `System_VillageNeedsEvaluation`이 게임시간 2h마다 갱신해 두는 마을별 후보 캐시.

> **로드맵 fallback의 의의**: NeedsEvaluation이 후보 0개를 반환하는 케이스(자원 모두 부족, Stage 데이터 부재 등)에 안전망. Phase E에서 fallback 의존 없어지면 제거.

---

## 10. 배치 구역화 (`VillageTileFinder` 확장)

### 10.1 카테고리 클러스터 가산점

기존 `VillageTileFinder.FindEmptyTileNearest`에 카테고리 매개변수 추가:

```csharp
public static Vector2Int? FindEmptyTileNearest(
    Vector2Int center,
    int maxRadius,
    int villageId = -1,
    BuildableCategory category = BuildableCategory.None)
```

후보 점수 = `-CountOccupiedNeighbors(t) + ClusterBonus(t, villageId, category)`. 같은 카테고리 인접 셀 1개당 +1 (8방위), 다른 카테고리 인접은 -0.5.

### 10.2 BuildableCategory enum

```csharp
public enum BuildableCategory
{
    None, Housing, Storage, Production, Cooking,
    Forge, Service, Defense, Decor
}
```

`BuildableItemTable.Category` 컬럼이 정본 (§2.2 표). `VillageTileFinder`는 `t.Category` 직접 읽음.

### 10.3 외곽 보호 마진 (Outskirt Protection)

**문제**: 비-Defense 오브젝트(상점/화로/여관/주거 등)가 마을 경계 바로 안쪽에 배치되면, 플레이어가 마을 밖에서도 가까이 가서 서비스를 쓸 수 있는 모호한 영역이 생긴다. `System_VillageServiceProximity`(§6)는 "플레이어가 마을 안에 있을 때만" 서비스를 잡도록 설계됐는데, 이 보장이 깨지면 UX가 흐릿해진다.

**해결**: 비-Defense 카테고리는 마을 경계로부터 **최소 2타일 안쪽에만** 배치. 외곽 1~2타일 띠는 **벽(Defense)만 들어갈 수 있는 보호 영역**.

```csharp
private const int OUTSKIRT_MARGIN_TILES = 2;

private static bool IsValidPlacement(
    Vector2Int tile, Vector2Int center, int boundsRadius,
    BuildableCategory category)
{
    int dx = Mathf.Abs(tile.x - center.x);
    int dy = Mathf.Abs(tile.y - center.y);
    int distFromCenter = Mathf.Max(dx, dy);          // 체비셰프 거리 (사각 경계)
    int distFromBoundary = boundsRadius - distFromCenter;

    if (category == BuildableCategory.Defense)
    {
        // 벽/게이트는 정확히 경계 위 (WallPlanner가 별도 처리하므로 일반 BuildQueue에선 거의 불필요)
        return distFromBoundary <= 0;
    }
    else
    {
        // 비-Defense: 경계로부터 최소 OUTSKIRT_MARGIN_TILES 안쪽
        return distFromBoundary >= OUTSKIRT_MARGIN_TILES;
    }
}
```

`FindEmptyTileNearest`의 후보 평가에 이 검사를 **하드 필터**로 추가 — 외곽 영역 후보는 점수 계산 전에 탈락. 큰길 예약(`IsReservedRoad`, Phase C)과 같은 위치에서 처리.

**Stage별 영역 효과** (boundsRadius - 2 = 비-Defense 가용 반경):
| Stage | boundsRadius | 비-Defense 가용 반경 | 가용 면적 |
|-------|:------------:|:--------------------:|:---------:|
| Settlement | 6 | 4 | 9×9 = 81타일 |
| Hamlet | 10 | 8 | 17×17 = 289타일 |
| Village | 14 | 12 | 25×25 = 625타일 |
| Town | 18 | 16 | 33×33 = 1089타일 |
| City | 24 | 22 | 45×45 = 2025타일 |

> Settlement는 영역이 작아 마진 2가 빠듯할 수 있음 — 81타일에서 큰길 예약(roadRadius=0이라 영향 없음)·기존 오브젝트·NPC 위치 제외 후 사용. Stage 0에 배치되는 시드 오브젝트(Campfire, Bedroll, 첫 Bed 1~2개) 정도는 충분. **Settlement margin=1**로 완화 검토 가능 (튜닝 이슈, §18에 추가).

### 10.4 ServiceProximity와의 일관성

§6.2의 "플레이어가 마을 안에 있을 때만 서비스 잡힘" 룰이 §10.3 외곽 마진과 자연스럽게 맞물린다:

- **상점/화로/여관/제단** 모두 외곽에서 ≥ 2타일 안쪽 → 플레이어가 이들에 인접하려면 마을 깊숙이 들어와야 함
- **벽 외곽선** = 마을 영역 시각 경계 → 플레이어는 게이트로 들어와야만 서비스 영역에 접근
- 결과: "벽 밖에서 서비스 사용" 같은 어색한 케이스 발생 X

### 10.5 효과 (클러스터링)

- Forge (Furnace, Anvil, QuenchVat)이 모이면 "공방 거리" 발생 → `HasObjectSet(ForgePremium)` 자동 만족
- Bed들이 모여 "주거 구역" 발생
- MerchantStall + Inn(InnBed)이 모이면 "광장" 발생

> 단순 가산점이라 "절대 같이 안 붙는" 강제는 아님. 빈 타일 부족 시 카테고리 무관하게 폴백.

---

## 11. NPC 직무 할당 — `System_VillageJobAssignment`

### 11.1 시스템 정의

```csharp
public class System_VillageJobAssignment : IFixedUpdateSystem
{
    public int Priority => 68;           // 65-69 Construction
    public float UpdateInterval => 5.0f; // 게임시간 1h 게이트 내장
    private const float CHECK_INTERVAL_HOURS = 1f;
}
```

### 11.2 매칭 룰 — `BuildableItemTable.AssociatedJobType` 컬럼

직업↔오브젝트 매핑은 **`BuildableItemTable.AssociatedJobType` 컬럼이 정본**. 코드 매핑 사전 X.

매칭 조건: `npc.JobType == buildableTable.AssociatedJobType` (둘 다 0 아님). 시트에 채울 값은 §2.2 표 참조.

> Gatherer(만능) NPC는 `AssociatedJobType=0`인 오브젝트는 매칭 안 되고 우선순위가 가장 낮음 — 빈 슬롯 모두 차고 나면 가장 가까운 작업 오브젝트 1곳에 임시 배정 (Fallback). 이 fallback도 `JobType` 비교 한 줄로 처리.

### 11.3 알고리즘 — TableId 분기 없음

```csharp
for each village v in AR.s.Village.GetAllVillages():
    var busy = CollectAssignedEntityIds(v);
    var idleNpcs = CollectIdleNpcs(v);

    // 활성 작업 오브젝트 후보 = 마을 내 모든 PlacedObject 중 AssociatedJobType > 0인 것
    var workplaces = PlacedObjectRegistry
        .GetAllEntitiesInVillage(v.VillageId)
        .Where(eid => GetAssociatedJob(eid) != JobType.None)
        .ToList();

    foreach (int npcEntityId in idleNpcs)
    {
        var npcJob = GetNpcJobType(npcEntityId);
        if (npcJob == JobType.None) continue;

        for (int i = 0; i < workplaces.Count; i++)
        {
            int placedEntityId = workplaces[i];
            if (busy.Contains(placedEntityId)) continue;
            if (GetAssociatedJob(placedEntityId) != npcJob) continue;
            Assign(npcEntityId, placedEntityId);
            busy.Add(placedEntityId);
            break;
        }
    }

private static JobType GetAssociatedJob(int placedEntityId)
{
    if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(placedEntityId, out var po) == false)
        return JobType.None;
    var t = AR.s.Data.GetBuildableItem(po.TableId);
    return t != null ? (JobType)t.AssociatedJobType : JobType.None;
}
```

NPC 1명당 1 오브젝트, 1 오브젝트당 NPC 1명. Phase D MVP는 그뿐 — 실제 이동/일과는 Phase E.

### 11.4 PassiveProduction 보너스 가산 — `JobBonusTable` 신설

직업별 시간당 가산값도 코드 분기로 박지 않는다. 신규 `JobBonusTable` 시트 1개로 데이터화:

| JobBonusTable 컬럼 | 타입 | 의미 |
|--------------------|------|------|
| `Id` | int | (TableBase) |
| `JobType` | int | `JobType` enum (1행 = 1직업) |
| `Resource1Type` | int | `ItemType` enum (Wood/Stone/Food/Iron/Gold...) |
| `Resource1PerHour` | float | 시간당 가산량 |
| `Resource2Type` | int | (선택, 없으면 0) |
| `Resource2PerHour` | float | |

시트 데이터 (참고):
| Id | JobType | Resource1 | /h | Resource2 | /h |
|---:|---------|-----------|-----|-----------|-----|
| 1 | Woodcutter | Wood | 1.5 | — | — |
| 2 | Miner | Stone | 1.0 | Iron | 0.2 |
| 3 | Farmer | Food | 0.8 | — | — |
| 4 | Hunter | Food | 1.0 | — | — |
| 5 | Merchant | Gold | 0.5 | — | — |

(Blacksmith는 Phase F 제작 큐에서 다루므로 Phase D에서는 0)

```csharp
// System_VillagePassiveProduction.OnFixedUpdate에 추가
foreach (int npcEntityId in v.NpcEntityIds)
{
    if (AR.s.Component.TryGetComponent<NpcAssignmentComponent>(npcEntityId, out var a) == false) continue;
    if (a.AssignedObjectEntityId < 0) continue;

    var jb = AR.s.Data.GetJobBonusByJobType(a.JobType);
    if (jb == null) continue;
    AccumulateProduction(v, (ItemType)jb.Resource1Type, jb.Resource1PerHour, dtHours);
    if (jb.Resource2Type != 0)
        AccumulateProduction(v, (ItemType)jb.Resource2Type, jb.Resource2PerHour, dtHours);
}
```

→ 직업 추가/튜닝은 시트 행 수정만. C# 변경 0줄.

---

## 12. 시스템 등록 갱신

PHASE_C_DESIGN.md §8 정책 그대로. Phase D 추가 슬롯:

| Priority | 시스템 | Phase | 인터벌 | 도메인 |
|---------:|--------|-------|--------|--------|
| 52 | `System_VillagePassiveProduction` | A | 5.0s | Resource |
| 56 | `System_VillagePopulation` | A→C | 5.0s | Population |
| 60 | `System_VillageTierProgression` | C | 5.0s | Lifecycle |
| **61** | **`System_VillageNeedsEvaluation`** | **D** | **5.0s** | **Lifecycle** |
| **61** | **`System_VillageServiceProximity`** | **D** | **0.3s (Update)** | **Lifecycle** |
| 66 | `System_VillageBuildQueue` | B | 5.0s | Construction |
| 67 | `System_VillageWallPlanner` | C | 5.0s | Construction |
| **68** | **`System_VillageJobAssignment`** | **D** | **5.0s** | **Construction** |

> Priority 61이 두 시스템에 중복 — Phase는 다르지만 같은 도메인 슬롯. 등록 순서로 결정 (Needs → Proximity), 어차피 IUpdate / IFixedUpdate 분리.

---

## 13. 세이브/로드 변경

### 13.1 신규 필드 (`VillageData`)

```csharp
// PlacedObject 정본 (좌표/HP)
public List<PlacedObjectSaveData> PlacedObjects = new();

// 마지막 Shrine 사용 (쿨다운)
public float LastShrineUseGameTime;

// 상점 매물 (24h마다 재롤)
public List<MerchantStockEntry> MerchantStock = new();
public float LastMerchantRollGameTime;
```

### 13.2 마이그레이션

구 세이브에 `PlacedObjects`가 비어 있고 `PlacedObjectTypeIds`만 있는 경우:
- 좌표 정보가 없으므로 **세이브에는 카운트만 보존**되어 있는 셈
- `VillageManager.Load`에서 `PlacedObjectTypeIds`만 보고 `PlacedObjects` 자동 재생성 — 좌표는 마을 중심에서 무작위(현재 BuildQueue가 사용하는 `VillageTileFinder` 호출)로 재배치
- 마이그레이션 로그 1회: `[Phase D Migration] v{id} N개 오브젝트 좌표 재배치`

> 이 마이그레이션은 게임플레이 영향이 거의 없는 수준 (이미 마을 안 어딘가). 단, **첫 로드 직후 클러스터링이 깨질 수 있음** — 신규 게임에선 발생 안 함.

### 13.3 `BuildableItemTable` 컬럼 변경

**삭제**:
- `Function` (Phase C placeholder, 의미 없음. ProvidedService로 대체)

**신규 추가** (모두 정수 컬럼, 기본값 0):
| 컬럼 | 타입 | 의미 |
|------|------|------|
| `ProvidedService` | int (bitmask) | `ProvidedService` enum 비트 OR. 서비스 제공 식별 |
| `Category` | int | `BuildableCategory` enum (Housing/Storage/Production/Cooking/Forge/Service/Defense/Decor) |
| `SetMembership` | int (bitmask) | `SetMemberTag` enum 비트 OR. 세트 부품 식별 |
| `AssociatedJobType` | int | `JobType` enum. 0이면 작업 오브젝트 아님 |
| `BaseWeight` | int | 필요도 스코어 베이스 (기본 10) |

기존 컬럼(`Cost_Wood`/`Cost_Stone`/`Cost_Metal`/`StorageCap_*`)은 그대로.

### 13.4 신규 테이블 — `JobBonusTable`

| 컬럼 | 타입 | 의미 |
|------|------|------|
| `Id` | int | TableBase |
| `JobType` | int | `JobType` enum (행당 직업 1개) |
| `Resource1Type` | int | `ItemType` enum |
| `Resource1PerHour` | float | 시간당 가산량 |
| `Resource2Type` | int | (선택) |
| `Resource2PerHour` | float | (선택) |

§11.4 표 그대로 5행. `DataManager.GetJobBonusByJobType(JobType)` 헬퍼.

### 13.5 `ItemTable` 신규 컬럼

| 컬럼 | 타입 | 기본값 | 의미 |
|------|------|--------|------|
| `BasePrice` | int | 0 | 상점 매물 기본 가격 (Gold). 0이면 비매품(매물 미노출) |
| `SellRatioBp` | int | 0 | 매각 비율 ×100 (50/40). 0이면 매각 불가 |
| `ReturnResourceType` | int | 0 | 매각 시 마을 환원 자원 (`ItemType` enum, 0=환원 없음) |
| `ReturnRatioBp` | int | 0 | 환원 비율 ×100 (50/100). 0이면 환원 없음 |

§8.2 / §8.3 표 그대로 시트에 입력. 코드는 컬럼만 본다 — 카테고리·아이템별 분기 X.

### 13.6 Phase C에서 가져온 하드코딩 정리 (선행 정리)

Phase C의 `System_VillageTierProgression`에 하드코딩된 TableId 상수가 있다 ([System_VillageTierProgression.cs](../Assets/Scripts/Common/System/System_VillageTierProgression.cs#L26-L31) — `BED_ID=102`, `TOWNPOST_ID=152`, `FURNACE_ID=160`, `ANVIL_ID=161`, `MERCHANTSTALL_ID=151`). Phase D에서 정리:

- **승격 조건을 ProvidedService/SetMember 기반으로 재해석**:
  - `bedCount >= N` → `CountByService(v, ProvidedService.Housing) >= N`
  - `TownPost 1개` → `CountByService(v, ProvidedService.Civic) >= 1`
  - `Furnace+Anvil 세트` → `HasObjectSet(v, ObjectSetType.ForgeStandard)` (마을 전체 검사)
  - `MerchantStall 1개` → `CountByService(v, ProvidedService.Shop) >= 1`

→ Phase D 작업 후 `System_VillageTierProgression`의 const 상수 5개 모두 제거. Bed Tier 승격 로직은 새 헬퍼 `CountByService` 사용.

---

## 14. 디버그 로그

| 태그 | 시점 | 포맷 |
|------|------|------|
| `[Shop]` | 구매 | `v{id} 구매 {itemName} ×{n} = {gold}G` |
| `[Sell]` | 매각 | `v{id} 판매 {itemName} ×{n} → +{gold}G (자원 환원: {resType} +{ret})` |
| `[Service]` | UI 오픈 | `v{id} 서비스 열기: {service} (NPC entityId={id})` |
| `[Needs]` | 후보 갱신 | `v{id} 후보 {n}건: top={name}({score:F1})` (2h마다 1줄) |
| `[JobAssign]` | 매칭 성공 | `v{id} npc{id}({jobType}) → {tableName}({entityId})` |
| `[Forge]` | UI 단계 결정 | `v{id} 강화 단계 {1/2/3} (HasSet=...)` |

`VillageDebugLog.Snapshot` 확장 — 활성 서비스, 직무 할당률 추가:
```
[VillageSnapshot] v0 Stage=Town Pop=15/8 Threat=0.00 Wall=24/24
                  Services=Shop|Forge|Inn|Shrine  Jobs=12/15
                  ...
```

---

## 15. 리스크와 대응

| # | 리스크 | 영향 | 대응 |
|---|--------|------|------|
| 1 | `PlacedObjectTypeIds` ↔ `PlacedObjects` 동기화 어긋남 | 중 | `OnObjectPlaced`에서 동시 갱신 + 로드 시 검증 헬퍼 (개수 일치 안 하면 `PlacedObjects`를 정본으로 재구성) |
| 2 | UI 4개 동시 진입 가능 (Shrine + Inn 겹침) | 저 | `ServiceUIRouter` 우선순위 (Shop > Forge > Inn > Shrine). 화면 좌하단 힌트 표시 |
| 3 | Mod 재롤 API 미정 | 중 | Phase D MVP는 단순 `MultiplyTier` 임시. 본격 Mod 재롤은 Phase F에서 정책 결정 |
| 4 | 매각 자원 환원으로 마을 자가 성장이 너무 가속 | 중 | 환원 비율 50% 캡 + Cap 초과분 자동 폐기. 추가 튜닝은 플레이 후 |
| 5 | NeedsEvaluation 계산 비용 (마을 N개 × 후보 16종) | 저 | 2h 인터벌이라 1초당 0.3회 미만. 마을 100개여도 무관 |
| 6 | NPC 직무 할당 후 PassiveProduction이 매번 NpcAssignmentComponent 조회 | 저 | Pool iter + TryGetComponent 1회 분기. 영향 미미 |
| 7 | ServiceProximity 매 0.3s 마을 검색 | 저 | `FindVillageContaining`은 마을 수가 적어 O(N) 무관. 100마을부터 공간분할 고려 |
| 8 | 매각 처리 중 인벤토리 차감 후 Gold 지급 실패 시 자원 분실 | 중 | `Inventory.RemoveItem`은 마지막 단계 직전에. Gold 지급 실패 가능성은 사실상 없으므로 RemoveItem이 성공한 시점 = 거래 확정 |
| 9 | BasePrice/BaseWeight 컬럼 없는 구 시트 호환 | 저 | 신규 컬럼 기본값 0으로 추가, 미정의 아이템은 비매품 |
| 10 | Phase E의 배후 시뮬과 Phase D 직무 할당 충돌 | 중 | Phase D는 활성 청크 한정. 비활성 청크 도달 시 컴포넌트 휘발 → Phase E `AbstractVillageSimulation`이 NpcAssignment를 보고 압축 처리 |
| 11 | Settlement(boundsRadius=6)에서 외곽 마진 2 적용 시 가용 9×9 = 81타일이 빠듯 | 중 | Stage 0 시드 오브젝트(Campfire, Bedroll, Bed 1~2개)는 충분히 들어가지만, 로드맵 후반(Well 등)에서 부족하면 Settlement만 margin=1로 완화 (§18 #12) |
| 12 | Bounds 확장 직전에 외곽에 배치된 오브젝트가 승격 후 "마을 영역에 흡수" | 저 | `OnPromote`에서 새 Bounds 안쪽 = 자동 안전. Bounds가 작아지는 케이스 없음 (단조 증가) |

---

## 16. 구현 순서 (작업 분해)

### Step 1 — 데이터 스키마 (시트/테이블)
- [ ] 1.1 `BuildableItemTable` 컬럼: `Function` **삭제** + `ProvidedService`/`Category`/`SetMembership`/`AssociatedJobType`/`BaseWeight` 5개 신규 추가 (시트 + Tables.cs + DownloadTables.cs)
- [ ] 1.2 기존 17행에 §2.2 표 값 입력
- [ ] 1.3 `ItemTable.BasePrice`, `SellRatioBp`, `ReturnResourceType`, `ReturnRatioBp` 4컬럼 추가 (시트 + Tables.cs)
- [ ] 1.4 자원/장비 행 채우기: BasePrice + SellRatioBp(자원=50, 장비/소비=40) + 자원 행은 ReturnResourceType/ReturnRatioBp(§8.3 표)도 입력
- [ ] 1.5 신규 시트 `JobBonusTable` 작성 + 5행 입력 + Tables.cs/DownloadTables.cs/DataManager 추가
- [ ] 1.6 `DataManager.GetJobBonusByJobType(JobType)` 헬퍼 추가

### Step 2 — ECS 컴포넌트
- [ ] 2.1 [PlacedObjectComponent.cs](../Assets/Scripts/Common/Component/PlacedObjectComponent.cs) 신설 — `Service`/`SetMember` 캐시 포함
- [ ] 2.2 [PlayerNearbyServicesComponent.cs](../Assets/Scripts/Common/Component/PlayerNearbyServicesComponent.cs) 신설
- [ ] 2.3 [NpcAssignmentComponent.cs](../Assets/Scripts/Common/Component/NpcAssignmentComponent.cs) 신설
- [ ] 2.4 `VillageComponent`에 `LastNeedsEvalGameHour` 필드 추가 (Reputation은 Phase F로 보류)
- [ ] 2.5 ComponentManager 풀 등록 (PlacedObject 1024, Nearby 8, Assignment 256)

### Step 3 — Enum + 카탈로그
- [ ] 3.1 [ProvidedService.cs](../Assets/Scripts/Common/Enum/ProvidedService.cs) — `[Flags]` enum
- [ ] 3.2 [BuildableCategory.cs](../Assets/Scripts/Common/Enum/BuildableCategory.cs) — enum
- [ ] 3.3 [ObjectSetType.cs](../Assets/Scripts/Common/Enum/ObjectSetType.cs) — enum
- [ ] 3.4 [SetMemberTag.cs](../Assets/Scripts/Common/Enum/SetMemberTag.cs) — `[Flags]` enum
- [ ] 3.5 [ObjectSetCatalog.cs](../Assets/Scripts/Village/ObjectSetCatalog.cs) — `Dictionary<ObjectSetType, ObjectSetDefinition>` (§2.3 6행)
- [ ] 3.6 (선택) `JobType` enum에 누락된 직업(Hunter/Merchant 등) 보강 — 기존 enum 검토

### Step 4 — Registry + VillageData 확장
- [ ] 4.1 [PlacedObjectRegistry.cs](../Assets/Scripts/Village/PlacedObjectRegistry.cs) 신설
- [ ] 4.2 [PlacedObjectSaveData.cs](../Assets/Scripts/Village/PlacedObjectSaveData.cs) 신설 + `VillageData.PlacedObjects` 추가
- [ ] 4.3 `VillageData.LastShrineUseGameTime`, `MerchantStock`, `LastMerchantRollGameTime` 추가
- [ ] 4.4 `VillageManager.Load` 마이그레이션 로직 (구 세이브 → PlacedObjects 자동 재생성)

### Step 5 — VillageManager API
- [ ] 5.1 `HasObjectSet(villageId, setType, anchor?)` — §5.3 `SetMember` OR 기반 (TableId 분기 X)
- [ ] 5.2 `CountByService(villageId, ProvidedService)` — Registry 순회 + 비트 검사 (Tier 승격 등에서 사용)
- [ ] 5.3 `SellItemToMerchant(villageId, itemTableId, amount)` 구현 — Gold 지급 + 자원 50% 환원
- [ ] 5.4 `BuyItemFromMerchant(villageId, stockEntryIndex, amount)` 구현 — Gold 차감 + 매물 잔량 감소
- [ ] 5.5 `OnObjectPlaced` 확장 — PlacedObjectComponent 부착 + Registry.Register

### Step 5b — System_VillageTierProgression 정리 (Phase C 하드코딩 제거)
- [ ] 5b.1 `BED_ID`/`TOWNPOST_ID`/`FURNACE_ID`/`ANVIL_ID`/`MERCHANTSTALL_ID` 상수 5개 제거
- [ ] 5b.2 승격 조건 `bedCount`/`TownPost`/`MerchantStall` 카운트 → `CountByService` 호출로 교체
- [ ] 5b.3 Furnace+Anvil 세트 검사 → `HasObjectSet(villageId, ObjectSetType.ForgeStandard)` 호출로 교체

### Step 6 — System_VillageBuildQueue 통합 변경
- [ ] 6.1 완료 시 entity 발급 + PlacedObjectComponent 부착 (벽 제외) + Registry 등록
- [ ] 6.2 `TryStartNextTask`에 NeedsEvaluation 후보 1순위로 — 없으면 로드맵 fallback

### Step 7 — System_VillageNeedsEvaluation
- [ ] 7.1 [System_VillageNeedsEvaluation.cs](../Assets/Scripts/Common/System/System_VillageNeedsEvaluation.cs) 신설 (Priority 61, 2h 인터벌)
- [ ] 7.2 점수 함수 + 후보 정렬 + `VillageNeedsCache` 갱신

### Step 8 — System_VillageServiceProximity
- [ ] 8.1 [System_VillageServiceProximity.cs](../Assets/Scripts/Common/System/System_VillageServiceProximity.cs) 신설 (Priority 61, IUpdate 0.3s)
- [ ] 8.2 PlayerNearbyServicesComponent 갱신
- [ ] 8.3 `ServiceUIRouter` 신설 (우선순위 라우팅)

### Step 9 — 입력 처리
- [ ] 9.1 InputAction "Interact"(F) 바인딩 확인 (이미 있으면 재사용)
- [ ] 9.2 System_Input에서 Interact 입력 시 `ServiceUIRouter.Open` 호출

### Step 10 — 서비스 UI 4종
- [ ] 10.1 [UIShopMerchant.cs](../Assets/Scripts/UI/UIShopMerchant.cs) — 구매/판매 두 탭. 매물 N개, BasePrice, 매각가 산출 + 자원 환원 미리보기
- [ ] 10.2 [UIForge.cs](../Assets/Scripts/UI/UIForge.cs) — 분해/재롤/추가 (Mod 시스템 연동)
- [ ] 10.3 [UIInn.cs](../Assets/Scripts/UI/UIInn.cs) — 세이브/휴식/(전송)
- [ ] 10.4 [UIShrine.cs](../Assets/Scripts/UI/UIShrine.cs) — 버프 3종 + 쿨다운

### Step 11 — 매물 풀 관리
- [ ] 11.1 `MerchantStockEntry` 정의 (`ItemTableId`, `RemainingCount`)
- [ ] 11.2 `VillageManager.RollMerchantStock(villageId)` — 24h 게임시간마다 매물 N개 무작위 재롤. `ItemTable.Tier ≤ Stage` + `BasePrice > 0` 풀에서 추출
- [ ] 11.3 매물 풀이 비어 있거나 마지막 재롤이 24h 이전이면 진입 시 자동 재롤

### Step 12 — System_VillageJobAssignment
- [ ] 12.1 [System_VillageJobAssignment.cs](../Assets/Scripts/Common/System/System_VillageJobAssignment.cs) 신설 (Priority 68, 1h 게이트)
- [ ] 12.2 매칭 알고리즘 — Registry 순회 + `BuildableItemTable.AssociatedJobType` 컬럼 비교 (TableId 분기 X)
- [ ] 12.3 NpcAssignmentComponent 부착/해제
- [ ] 12.4 PassiveProduction에 `JobBonusTable` 기반 가산 추가 (직업당 Resource1/Resource2 컬럼)

### Step 13 — VillageTileFinder 카테고리 클러스터링 + 외곽 마진
- [ ] 13.1 `FindEmptyTileNearest` 시그니처 확장 — `BuildableCategory category` 매개변수
- [ ] 13.2 외곽 보호 하드 필터 — 비-Defense 카테고리는 `boundsRadius - distFromCenter < OUTSKIRT_MARGIN_TILES`인 타일 탈락 (§10.3)
- [ ] 13.3 카테고리 클러스터 가산점 (§10.5)
- [ ] 13.4 BuildQueue가 `BuildableItemTable.Category` 컬럼 직접 읽어 전달

### Step 14 — 디버그 로그 + 스냅샷
- [ ] 14.1 새 태그 5종 (`[Shop]`, `[Sell]`, `[Service]`, `[Needs]`, `[JobAssign]`, `[Forge]`)
- [ ] 14.2 `VillageDebugLog.Snapshot` 확장

### Step 15 — 문서 갱신
- [ ] 15.1 PHASE_D_DESIGN.md 완료 마킹
- [ ] 15.2 VILLAGE_GROWTH_STAGES.md Phase D 체크박스 갱신

### Step U1 — Unity 자산 (사용자 진행)
- [ ] 4종 서비스 UI prefab + Addressable 등록 (`UI/ShopMerchant`, `UI/Forge`, `UI/Inn`, `UI/Shrine`)
- [ ] UIDocument 또는 Canvas 베이스 — 기존 UI 스타일 따라

**총 예상 시간**: 코드 ~12시간 + Unity UI ~5시간 = 약 17시간

---

## 17. DoD (완료 기준)

### 17.1 PlacedObject + Registry
- [ ] Phase C 완료 상태에서 플레이 → BuildQueue가 새 오브젝트 배치 시 PlacedObjectComponent 부착 + Registry 등록 로그
- [ ] `HasObjectSet(ForgeStandard, anchorFurnace)` 단위 테스트 통과

### 17.2 서비스 UI
- [ ] MerchantStall 옆 F → 상점 UI 오픈, 매물 노출
- [ ] Furnace 옆 F → 강화 UI 오픈. Anvil 추가 시 단계 2로 변경
- [ ] InnBed 근처 F → 여관 UI. 휴식 시 HP 100% + 게임시간 +6h 진행
- [ ] Shrine 근처 F → 버프 3종 중 1개 선택 → 30분 게임시간 적용 + 12h 쿨다운

### 17.3 상점 판매(매각) — 시트값 가정 BasePrice 1/Wood 10/Iron, SellRatioBp 50/50, ReturnRatioBp 50/100
- [ ] MerchantStall 옆 F → 상점 UI 판매 탭 → Wood 10개 판매 → Gold +5, 마을 Wood +5 (시트값에서 자동 산출)
- [ ] Iron 1개 판매 → Gold +5, 마을 Stone +1 (ReturnRatioBp=100)
- [ ] Tier=1 장비 판매 → Gold만 지급, ReturnResourceType=0이라 환원 없음
- [ ] Cap 초과 자원 환원 시도 → 자원 폐기되지만 Gold는 정상 지급
- [ ] 인벤토리 차감 실패(아이템 부족) → 거래 무효, Gold/자원 모두 변동 없음

### 17.4 필요도 스코어
- [ ] 신규 게임 시작 → 2h 후 `[Needs] v0 후보 N건: top=...` 로그
- [ ] BuildQueue가 NeedsEvaluation 후보를 우선 채택 (로그로 확인)
- [ ] NeedsEvaluation 후보 0건 시 로드맵 fallback 정상 동작

### 17.5 NPC 직무
- [ ] ChoppingBlock 배치 + Woodcutter NPC 존재 → 1h 후 `[JobAssign] v0 npc{id}(Woodcutter) → ChoppingBlock` 로그 (매칭은 `BuildableItemTable.AssociatedJobType` 컬럼 기반)
- [ ] 매칭된 NPC가 있는 마을의 Wood +1.5/h 추가 가산 확인 (`JobBonusTable` 시트값 기반 — 코드 분기 X)
- [ ] 새 직업 추가 시나리오 — `JobBonusTable`에 1행 추가 + 시트의 `AssociatedJobType` 컬럼 갱신만으로 동작

### 17.6 데이터 정본화 (no hardcoded TableId)
- [ ] `grep -rn "TableId.*==" Assets/Scripts/Common/System/System_Village*` 결과: 마을 시스템 코드에 TableId 정수 비교가 0건 (벽 Palisade ID 같은 시스템 본질적 ID 제외)
- [ ] `System_VillageTierProgression`에 `BED_ID`/`TOWNPOST_ID`/`FURNACE_ID`/`ANVIL_ID`/`MERCHANTSTALL_ID` 상수가 모두 제거됨
- [ ] `BuildableItemTable.Function` 컬럼이 시트와 코드 양쪽에서 제거됨
- [ ] 새 오브젝트(예: Bookshelf) 추가 시나리오 — 시트 1행 + ProvidedService/SetMembership/Category/AssociatedJobType 입력만으로 코드 변경 0줄 추가 가능 (단, 새 SetMemberTag 비트가 필요한 경우 enum + ObjectSetCatalog만 수정)

### 17.7 외곽 보호 마진
- [ ] Hamlet(boundsRadius=10) 마을 운영 → 비-Defense 오브젝트 모두 마을 중심으로부터 체비셰프 거리 ≤ 8 (= boundsRadius - 2) 안에만 배치
- [ ] Palisade/Gate(Defense)는 정확히 경계(거리=10)에 배치
- [ ] 플레이어가 마을 벽 바로 바깥(SpawnRadius 밖)에 서 있을 때 `PlayerNearbyServicesComponent.AvailableServices == None`
- [ ] 플레이어가 게이트로 진입한 직후 비로소 가장 가까운 서비스가 잡힘

### 17.8 비회귀
- [ ] Phase A~C 자가 건설 루프 그대로 동작 (Settlement → Hamlet → Village → Town 자동 승격)
- [ ] 벽 자동 건설 정상
- [ ] 이민/재스폰 정상

---

## 18. 결정 필요 이슈

| # | 이슈 | 제안 기본값 | 비고 |
|---|------|-------------|------|
| 1 | 상점 매물 풀: `MerchantStockTable` 신설 vs `ItemTable.BasePrice`만으로 무작위 풀 | **`BasePrice`만** (Phase D MVP) | 풀 정교화는 Phase F+ |
| 2 | 강화 UI의 Mod 재롤 비용 정책 | **기존 Currency 그대로** | Phase F에서 본격 |
| 3 | 휴식 시 게임시간 +6h 진행 vs +12h | **+6h** | 짧게 잡고 자주 들리게 |
| 4 | Shrine 버프 종류 3종 — 어떤 게 좋은가? | 가호의 빛 / 사냥꾼의 정확 / 헤르메스의 발 | 후속 튜닝 |
| 5 | 매각 SellRatio 자원 0.5 / 장비 0.4 — 베이스라인 적정성 | **0.5 / 0.4** | 실 플레이 후 튜닝 |
| 6 | 매각 자원 환원 비율 50% — 마을 자가성장 가속 영향 | **50%** | 너무 빠르면 30%로 하향 |
| 7 | NeedsEvaluation의 BaseWeight 기본값 | **10** | 시트로 옮기되 첫 패스는 코드 상수 |
| 8 | NPC 1직업 = 1오브젝트 vs 다대일 | **1:1** Phase D | 다대일은 Phase E 정책 |
| 9 | F키 충돌 — 기존 PickupItem과 우선순위 | **서비스 > Pickup** (Phase D 우선) | 둘 다 가까우면 서비스가 이김 |
| 10 | 마을 PlacedObject 마이그레이션 시 좌표 무작위 재생성 | **그대로** | 영향 미미 + 코드 단순. 정확한 좌표 복원은 Phase E |
| 11 | 매물 재롤 주기 24h가 적당한가 | **24h** | 자주 들르도록 짧게 시작. 너무 잦으면 48h |
| 12 | 외곽 보호 마진 (`OUTSKIRT_MARGIN_TILES`) — 모든 Stage 동일 vs Stage별 차등 | **2 (모든 Stage 동일)** | Settlement(boundsRadius=6, 가용 9×9)에서 빠듯하면 Settlement만 1로. 글로벌 상수 1줄 변경 |
| 13 | Defense 카테고리 일반 BuildQueue 처리 | **WallPlanner가 전담**, BuildQueue는 비-Defense만 | 명시적 분리. WallPlanner는 외곽 경계 타일을 직접 큐잉 |

---

## 19. Phase E 대비 (선행 투자)

Phase D에서 미리 손대두면 Phase E (배후 시뮬레이션) 착수가 쉬워지는 것들:

1. **`PlacedObjectComponent`** — Phase E의 비활성 청크에서도 좌표/HP가 보존되어 추상 시뮬이 직접 참조 가능
2. **`NpcAssignmentComponent`** — Phase E `System_AbstractVillageSimulation`이 직무 할당된 NPC만 보너스 가산하는 형태로 그대로 사용
3. **`MerchantStock` 재롤 주기** — Phase E에서 비활성 청크의 마을도 매물이 갱신되도록 추상 시뮬에 게임시간 24h 게이트 흡수
4. **NeedsEvaluation** — Phase E도 같은 점수 함수 재사용 (활성 청크는 BuildQueue 진행, 비활성은 추상 시뮬 진행. 둘 다 후보 1위 선택)

---

## 20. 한 줄 요약

> **Phase D는 마을이 만들어 낸 서비스가 플레이어에게 진짜 이득이 되게 만든다 — 상점에서 사고팔고, 화로에서 강화하고, 여관에서 자고, 제단에서 가호를 받는다. 그 사이 NPC들은 자기 직업의 작업 오브젝트 옆에서 일하기 시작한다.**
