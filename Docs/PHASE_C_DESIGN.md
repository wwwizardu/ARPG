# Phase C — Tier 승격 + Stage 1·2 확장 + 벽 인프라 상세 기획 ✅ 코드 완료 (2026-04-24)

> **잔여 작업 1건 (Unity 에디터, 사용자)**:
> - Step U2: RuleTile 2종(Palisade / PalisadeGate) 작성 + Addressable 등록

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행 Phase: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅ 완료 · [PHASE_B_DESIGN.md](PHASE_B_DESIGN.md) ✅ 완료
>
> **목표**: Phase B가 만든 "Stage 0 자가 건설 루프"에 **Tier 승격**을 붙여 마을이 Settlement → Hamlet → Village → Town 으로 실제로 자라게 한다. 외곽 벽(Palisade)의 인프라를 깔고, 배치 분산을 정교화한다.

---

## 1. Phase C 범위

### 1.1 Phase C가 하는 것

1. **`System_VillageTierProgression`** — 게임시간 4h마다 마을의 승격 조건 체크 + 실제 Stage 전환
2. **승격 사이드 이펙트** — Bounds 확장(반경 6→10→14→…), 패시브 생산 보너스, 로드맵 시퀀스 전환
3. **`VillageComponent`** (신규 ECS 컴포넌트) — Bounds, ThreatLevel 보유. Phase D의 필요도 스코어가 참조
4. **로드맵 확장** — Stage 1(Hamlet) 8종 + Stage 2(Village) 8종 시퀀스 추가
5. **외곽 벽 인프라 (Palisade)** — `WallSegmentComponent`, `System_VillageWallPlanner`, RuleTile. **Stage 3 도달 시 자동 건설 시작**
6. **배치 분산 정교화** — 인접 점유 셀 8방위 페널티 + "큰길" 예약 (Phase B 후속)
7. **`System_VillageRespawn` → `System_VillagePopulation` 확장** — 자연 이민 로직을 동일 시스템에 흡수. 별도 Immigration 시스템 신설 X (§3.5)
8. **마을 시스템 Priority 대역 정책 도입** — 50-69 범위를 도메인별 5단위 슬롯으로 분할 (§8). 이후 Phase D~F의 신규 마을 시스템도 같은 대역 사용

> **디자인 결정**: Stage 0~1의 마을 경계 시각화(BoundaryMarker)는 **명시적으로 도입하지 않음**. 플레이어가 벽(Stage 3+) 또는 NPC 행동(이동 범위, 일과 패턴)을 통해 자연스럽게 마을 영역을 추측하도록 유도 — emergent perception이 explicit marker보다 게임플레이에 풍부함.

### 1.2 Phase C가 **하지 않는** 것 (후속 Phase 이관)

- **Stage 3 → 4 (Town → City)**: StoneWall 업그레이드, WatchTower(2×2), Altar — Phase C+ 또는 Phase G
- **WatchTower 멀티타일 처리**: `MapManager.PlaceMultiTileObject` — Phase G
- **오브젝트 세트 판정** (`HasObjectSet`), `ProvidedService` 비트마스크 → 서비스 UI — Phase D
- **벽 파괴/재건 루프**, ThreatLevel 실제 변동 — Phase F
- **배후 시뮬레이션** (비활성 청크에서도 승격 진행) — Phase E
- **Stage 4+ 로드맵, Bookshelf/Desk 세트 조합 등** — Phase D 이상

> **Phase C 한 줄 범위**: "마을이 Town(Stage 3)까지 자라고, Town 시점에 나무 울타리가 자동으로 생긴다."

---

## 2. 기존 시스템과의 차이

### 2.1 현재 상태 (Phase B 완료, 2026-04-24 기준)

| 항목 | 현재 구현 | 위치 |
|------|-----------|------|
| Tier 승격 | **없음**. `VillageData.Stage`는 항상 `Settlement` (Phase B에서 로드맵 소진 시 로그만) | [VillageData.cs](../Assets/Scripts/Village/VillageData.cs) |
| 마을 경계 | `VillageTable.SpawnRadius` (정적 float) | [VillageManager.FindVillageContaining](../Assets/Scripts/Village/VillageManager.cs#L130) |
| `VillageComponent` | **없음**. Storage/Task만 존재 | — |
| 로드맵 | Stage 0 시퀀스 7개 (`SETTLEMENT_SEQUENCE`) | [VillageBuildRoadmap.cs](../Assets/Scripts/Village/VillageBuildRoadmap.cs) |
| 벽 | 맵 에디터의 나무벽(Id=1, Tile 경로)만 존재. 마을 자동 건설 무관 | — |
| 배치 정책 | 가장 가까운 링 + 같은 링 내 랜덤 | [VillageTileFinder.cs](../Assets/Scripts/Village/VillageTileFinder.cs) |
| ObjectType enum | None/Stone/Npc/WoodWall 4종 (Phase A부터 동일) | [GlobalEnum.cs:28-34](../Assets/Scripts/Common/GlobalEnum.cs#L28-L34) |

### 2.2 Phase C 이후 상태

| 항목 | 변경 |
|------|------|
| `VillageData.Stage` | Tier 승격 시점에 `Hamlet/Village/Town`로 전이 |
| **`VillageComponent`** | **신규 ECS 컴포넌트** — `Stage`, `Bounds`(Rect), `ThreatLevel`(float, Phase F 대비). 마을 엔티티에 부착 |
| **`System_VillageTierProgression`** | **신규** Priority 60, 게임시간 4h 인터벌. Stage별 조건 체크 + 승격 |
| `VillageBuildRoadmap` | **Stage별 시퀀스 분기**: `GetNextTarget(v)`이 `v.Stage`로 분기. `HAMLET_SEQUENCE`, `VILLAGE_SEQUENCE` 신규 배열. Phase D에서 필요도 스코어로 교체 시까지 유지 |
| `BuildableItemTable` | 엔트리 16개 추가 (Stage 1: 8종, Stage 2: 8종 — §3) |
| **`WallSegmentComponent`** | **신규 ECS 컴포넌트** — `SegmentId`, `Orientation`, `ConnectedGateId`, `SegmentHP` |
| **`System_VillageWallPlanner`** | **신규** Priority 61, 게임시간 6h 인터벌. Stage 3 진입 시 Bounds 외곽선 → 벽 세그먼트 큐 생성 |
| `VillageTileFinder` | 인접 점유 페널티 + "큰길" 보존 옵션 추가 — Phase B 단순 랜덤 픽의 후속 |
| `Palisade` / `PalisadeGate` 타일 | **신규** RuleTile (Stage 3+). Stage 0~1 경계는 시각화 X (디자인 결정 — §1.1 노트) |
| `ObjectType` enum | **변경 없음** (식별은 `BuildableItemTable.Id`. Phase A 결정 유지) |

> **단, "Stage 3 진입 = Palisade 자동 건설" 동작 자체는 Phase C에 포함.** Stage 4(StoneWall+WatchTower)는 미포함.

---

## 3. Tier 승격 시스템

### 3.1 승격 조건 (VILLAGE_GROWTH_STAGES.md §1.3, §3 발췌)

| 전이 | 인구 | Bed | 식량 | 게임시간 | 추가 조건 |
|------|------|-----|------|----------|-----------|
| **Stage 0 → 1** (Settlement → Hamlet) | ≥ 3 | ≥ 2 | Food 저장 ≥ 30 | 등록 후 ≥ 24h | (없음) |
| **Stage 1 → 2** (Hamlet → Village) | ≥ 8 | ≥ 4 | Food 저장 ≥ 80 | 등록 후 ≥ 72h | TownPost 1개 |
| **Stage 2 → 3** (Village → Town) | ≥ 15 | ≥ 8 | Food 저장 ≥ 200 | 등록 후 ≥ 168h(7일) | Furnace+Anvil 세트, MerchantStall 1개 |
| Stage 3 → 4 (Town → City) | — | — | — | — | **Phase C+ 이관** |

- 모든 조건 만족 시 승격. 한 번에 한 단계씩.
- Stage 0→1, 1→2 조건은 카운트만 — 세트 판정(Furnace+Anvil 등)은 Phase D의 `HasObjectSet`이지만 Phase C에서 단순 카운트로 임시 구현 (PlacedObjectTypeIds 카운트).

### 3.2 `System_VillageTierProgression` 구조

Priority 60, UpdateInterval = 게임시간 4h 환산 (실시간 기준 약 2분):

```csharp
public class System_VillageTierProgression : IFixedUpdateSystem
{
    public int Priority => 60;
    public float UpdateInterval => 5.0f;  // 게임시간 보정은 내부에서

    private float _lastCheckGameTime = -1f;
    private const float CHECK_INTERVAL_HOURS = 4f;

    public void OnFixedUpdate(float dt)
    {
        float now = AR.s.Time.CurrentGameTime;
        if (now - _lastCheckGameTime < CHECK_INTERVAL_HOURS) return;
        _lastCheckGameTime = now;

        foreach (VillageData v in AR.s.Village.GetAllVillages())
        {
            if (v.EntityId < 0) continue;
            VillageStage next = EvaluateNextStage(v, now);
            if (next != v.Stage)
                Promote(v, next);
        }
    }

    private VillageStage EvaluateNextStage(VillageData v, float now) { ... }
    private void Promote(VillageData v, VillageStage next) { ... }
}
```

### 3.3 승격 사이드 이펙트 (`Promote`)

```csharp
private void Promote(VillageData v, VillageStage next)
{
    VillageStage prev = v.Stage;
    v.Stage = next;

    // 1. Bounds 확장
    int radius = GetBoundsRadius(next);  // Settlement=6, Hamlet=10, Village=14, Town=18
    if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
    {
        vc.Stage = next;
        vc.Bounds = new RectInt(
            Mathf.FloorToInt(v.PositionX) - radius,
            Mathf.FloorToInt(v.PositionY) - radius,
            radius * 2, radius * 2
        );
        AR.s.Component.SetComponent(v.EntityId, vc);
    }

    // 2. 패시브 생산 보너스 (VillageTable.PassiveProductionMultiplier 또는 하드코딩)
    //    Phase C는 Stage별 ×1.0 → ×1.1 → ×1.2 → ×1.3 단순 가산
    //    실제 적용은 System_VillagePassiveProduction이 v.Stage 참조

    // 3. Stage 3 도달 시 벽 빌더 활성화
    if (next == VillageStage.Town && prev < VillageStage.Town)
        AR.s.Component.AddComponent(v.EntityId, new WallPlanRequestTag());

    Debug.Log($"[TierProgression] v{v.VillageId} {prev} → {next} (Bounds={vc.Bounds}, Pop={v.Population})");
}
```

### 3.4 `EvaluateNextStage` 조건 로직

```csharp
private VillageStage EvaluateNextStage(VillageData v, float now)
{
    if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false)
        return v.Stage;

    int bedCount = CountInPlaced(v, BED_TABLE_ID);
    float ageHours = now - v.RegisteredAt;

    switch (v.Stage)
    {
        case VillageStage.Settlement:
            if (v.Population >= 3 && bedCount >= 2 && s.FoodAmount >= 30 && ageHours >= 24f)
                return VillageStage.Hamlet;
            break;
        case VillageStage.Hamlet:
            if (v.Population >= 8 && bedCount >= 4 && s.FoodAmount >= 80 && ageHours >= 72f
                && CountInPlaced(v, TOWNPOST_TABLE_ID) >= 1)
                return VillageStage.Village;
            break;
        case VillageStage.Village:
            if (v.Population >= 15 && bedCount >= 8 && s.FoodAmount >= 200 && ageHours >= 168f
                && CountInPlaced(v, FURNACE_TABLE_ID) >= 1
                && CountInPlaced(v, ANVIL_TABLE_ID) >= 1
                && CountInPlaced(v, MERCHANTSTALL_TABLE_ID) >= 1)
                return VillageStage.Town;
            break;
    }
    return v.Stage;
}

private static int CountInPlaced(VillageData v, int tableId)
{
    if (v.PlacedObjectTypeIds == null) return 0;
    int count = 0;
    for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
        if (v.PlacedObjectTypeIds[i] == tableId) count++;
    return count;
}
```

### 3.5 인구 증가 — System_VillagePopulation으로 통합

**기존 `System_VillageRespawn`을 `System_VillagePopulation`으로 리네임**해 두 가지 책임을 한 시스템에 통합:

| 책임 | 트리거 | 비고 |
|------|--------|------|
| 정원 재스폰 (기존 Respawn) | NPC 전멸 후 쿨다운 만료 | Phase A부터 존재 |
| 자연 이민 (신규, Phase C) | 게임시간 24h마다 확률 체크 | Hamlet+ 한정 |

```csharp
public class System_VillagePopulation : IFixedUpdateSystem
{
    public int Priority => 56;        // Population 도메인 대역 (55-59)
    public float UpdateInterval => 5.0f;

    private float _lastImmigrationCheckGameTime;
    private const float IMMIGRATION_CHECK_HOURS = 24f;

    public void OnFixedUpdate(float dt)
    {
        // (기존) 정원 재스폰 — 매 5s 체크
        TickRespawn();

        // (신규) 자연 이민 — 게임시간 24h마다
        float now = AR.s.Time.CurrentGameTime;
        if (now - _lastImmigrationCheckGameTime >= IMMIGRATION_CHECK_HOURS)
        {
            _lastImmigrationCheckGameTime = now;
            TickImmigration();
        }
    }

    private void TickImmigration()
    {
        foreach (VillageData v in AR.s.Village.GetAllVillages())
        {
            if (v.Stage < VillageStage.Hamlet) continue;
            int bedCount = CountInPlaced(v, BED_TABLE_ID);
            if (bedCount <= v.Population) continue;          // 빈 침대 필요
            if (v.Resources[ItemType.Food] < v.Population * 5) continue;
            float chance = 0.20f + (int)v.Stage * 0.05f;     // Hamlet 25%, Village 30%, Town 35%
            if (Random.value < chance)
                SpawnImmigrantNpc(v);
        }
    }
}
```

### 통합 이점
- 별도 `System_VillageImmigration.cs` 파일 불필요
- 둘 다 **동일 도메인**(NPC 인구) + **동일 데이터 의존**(`VillageData.Population`/`NpcEntityIds`)
- Priority 슬롯 1개 절감 (계획상 60→56)

### 미포함 (이후 Phase)
- 출생, 분가 — Phase E (배후 시뮬레이션)

---

## 4. `VillageComponent`

### 4.1 정의

```csharp
public struct VillageComponent
{
    public int VillageId;
    public VillageStage Stage;       // Settlement/Hamlet/Village/Town/City
    public RectInt Bounds;           // 마을 경계 (Tier 승격 시 확장)
    public float ThreatLevel;        // 0.0~1.0, Phase F에서 본격 사용 (Phase C는 0 고정)
    public int WallSegmentCount;     // Stage 3+ 통계용
    public int CompletedWallSegments;
}
```

### 4.2 생성 시점

`VillageManager.CreateStorageEntity` 끝에 추가:

```csharp
private void CreateStorageEntity(VillageData data)
{
    int entityId = EntityIdHelper.CreateEntity();
    data.EntityId = entityId;

    // (기존) VillageStorageComponent 부착

    // (신규) VillageComponent 부착
    int radius = GetBoundsRadius(data.Stage);
    AR.s.Component.AddComponent(entityId, new VillageComponent {
        VillageId = data.VillageId,
        Stage = data.Stage,
        Bounds = new RectInt(
            Mathf.FloorToInt(data.PositionX) - radius,
            Mathf.FloorToInt(data.PositionY) - radius,
            radius * 2, radius * 2),
        ThreatLevel = 0f,
        WallSegmentCount = 0,
        CompletedWallSegments = 0,
    });
}
```

### 4.3 세이브

`VillageData`에 다음 필드 추가:
```csharp
public int BoundsX, BoundsY, BoundsW, BoundsH;  // RectInt 직렬화
public float ThreatLevel;
```

`SyncTaskToData`에 마찬가지로 컴포넌트 → VillageData 동기화 추가. 로드 시 `RestoreComponentsFromData`로 복원.

### 4.4 사용 시나리오

| 호출자 | 사용 |
|--------|------|
| `System_VillageTierProgression` | Bounds 갱신, Stage 갱신 |
| `System_VillageWallPlanner` | Bounds 외곽선 추출 → 벽 후보 타일 |
| `VillageManager.FindVillageContaining` | Phase D 이후 Bounds 직접 검사 (현재는 SpawnRadius) |
| Phase D 필요도 스코어 | ThreatLevel 가중치 |

---

## 5. 로드맵 확장

### 5.1 Stage 1 (Hamlet) 시퀀스 — 8종

VILLAGE_GROWTH_STAGES.md §3.2:

```csharp
private static readonly RoadmapEntry[] HAMLET_SEQUENCE = new[]
{
    new RoadmapEntry(102, 3.0f),  // Bed (3번째)
    new RoadmapEntry(140, 4.0f),  // ChoppingBlock (Wood 15)
    new RoadmapEntry(112, 3.0f),  // Stockpile (Stone 10, Cap_Stone +80)
    new RoadmapEntry(150, 4.0f),  // Hearth (Stone 20)
    new RoadmapEntry(141, 4.0f),  // DryingRack (Wood 20)
    new RoadmapEntry(151, 5.0f),  // MerchantStall (Wood 25)
    new RoadmapEntry(152, 6.0f),  // TownPost (Wood 30, Stone 15) — 승격 트리거
    new RoadmapEntry(102, 3.0f),  // Bed (4번째)
};
```

### 5.2 Stage 2 (Village) 시퀀스 — 8종

VILLAGE_GROWTH_STAGES.md §3.3:

```csharp
private static readonly RoadmapEntry[] VILLAGE_SEQUENCE = new[]
{
    new RoadmapEntry(160, 6.0f),  // Furnace (Stone 40, Wood 30)
    new RoadmapEntry(161, 5.0f),  // Anvil (Stone 20, Metal 15)
    new RoadmapEntry(142, 4.0f),  // MiningCart (Wood 20, Metal 5)
    new RoadmapEntry(162, 4.0f),  // QuenchVat (Wood 15, Metal 5)
    new RoadmapEntry(153, 4.0f),  // InnBed (Wood 20)
    new RoadmapEntry(170, 6.0f),  // Shrine (Stone 30)
    new RoadmapEntry(154, 5.0f),  // SignalBrazier (Wood 20, Metal 5)
    // Bed 추가 (인구 15까지 수용) — 5~8번째
    new RoadmapEntry(102, 3.0f),
    new RoadmapEntry(102, 3.0f),
    new RoadmapEntry(102, 3.0f),
    new RoadmapEntry(102, 3.0f),
};
```

### 5.3 `GetNextTarget` Stage 분기

```csharp
public static RoadmapEntry? GetNextTarget(VillageData village)
{
    // Campfire 우선 (Phase B 호환)
    if (village.PlacedObjectTypeIds.Contains(CAMPFIRE_TABLE_ID) == false)
        return new RoadmapEntry(CAMPFIRE_TABLE_ID, CAMPFIRE_BUILD_HOURS);

    return village.Stage switch
    {
        VillageStage.Settlement => GetNextFromSequence(village, SETTLEMENT_SEQUENCE),
        VillageStage.Hamlet     => GetNextFromSequence(village, HAMLET_SEQUENCE, skipPlaced: true),
        VillageStage.Village    => GetNextFromSequence(village, VILLAGE_SEQUENCE, skipPlaced: true),
        _ => null,  // Stage 3+는 Phase C+
    };
}
```

`skipPlaced=true`일 때는 `PlacedObjectTypeIds`에서 같은 TableId가 이미 시퀀스 길이만큼 있는지 카운트해서 그다음을 반환 — Stage 0의 Campfire 제외 카운트 방식의 일반화.

### 5.4 `BuildableItemTable` 신규 엔트리 16종

Id 대역:
- 140대: 생산 도구 (ChoppingBlock, MiningCart, DryingRack)
- 150대: 서비스/인프라 (Hearth, MerchantStall, TownPost, InnBed, SignalBrazier)
- 160대: 대장간 (Furnace, Anvil, QuenchVat)
- 170대: 종교 (Shrine)
- 180대: 외곽 벽 — Palisade(180), PalisadeGate(181)

(상세 표는 §10에 통합)

---

## 6. 외곽 벽 시스템

### 6.1 정책 (Phase C 범위)

- **Stage 3(Town) 도달 시**에만 자동 건설 트리거
- Palisade만 (StoneWall은 Phase C+ 이관)
- 게이트는 마을의 "주된 진입 방향" 1~2개에 자동 배치
- 벽은 **Tile 경로** (`SpawnType=Tile` + RuleTile) — 연속 구조라 GameObject 단위 부담 회피

### 6.2 `WallSegmentComponent`

```csharp
public struct WallSegmentComponent
{
    public int VillageId;
    public int SegmentId;            // 마을 내 일련번호
    public int TileX, TileY;
    public WallType Type;            // Palisade / StoneWall (Phase C+ 대비)
    public WallOrientation Orient;   // Horizontal/Vertical/CornerNE/.../GateLink
    public int SegmentHP;
    public int MaxHP;
}

public enum WallType { Palisade = 0, StoneWall = 1 }
public enum WallOrientation { Horizontal, Vertical, CornerNE, CornerNW, CornerSE, CornerSW, Gate }
```

세그먼트 = 벽 한 칸. 멀티 칸 묶음 처리는 RuleTile이 시각적으로만 담당.

### 6.3 `System_VillageWallPlanner`

Priority 61, UpdateInterval = 게임시간 6h. `WallPlanRequestTag` 컴포넌트가 붙은 마을만 처리:

```
1. VillageComponent.Bounds 외곽선 타일 수집 (Rect 둘레)
2. 게이트 후보 타일 결정 (마을 중심에서 가장 가까운 도로/입구 — Phase C는 단순히 N/S 방위 1개씩)
3. 각 외곽 타일 → ObjectPlacementTaskComponent 큐 추가 (단, 1마을 1태스크 제약 때문에 큐 별도 관리)
4. 게이트는 Palisade가 아닌 PalisadeGate Id로 배치
5. 모든 세그먼트 큐에 들어가면 WallPlanRequestTag 제거
```

**문제**: 현재 `ObjectPlacementTaskComponent`는 마을당 1개 슬롯. 벽 N개를 동시 큐잉할 수 없음.

**해결**: Phase C는 **순차 1건씩** — 일반 오브젝트 큐와 동일한 단일 슬롯 사용. 벽 1칸 완성 → 다음 칸 시작. 60칸짜리 외벽이면 60개 태스크 = 60×BuildHours 시간. 게임시간 단위로는 충분한 진행 속도.

대안: 벽 전용 별도 컴포넌트 `WallPlacementTaskComponent`로 병렬 진행. **Phase C는 단순화를 위해 단일 큐 채택**, Phase D 이후 NPC 단위 병렬화 시 자연 해소.

### 6.4 벽 효과 (게임플레이 반영, 최소)

VILLAGE_GROWTH_STAGES.md §4.3 발췌. Phase C 범위:
- Palisade 타일은 `Blocked` 비트 → 자동으로 NPC/몬스터 이동 차단 (기존 `IsWalkable` 활용)
- 게이트 타일은 `Walkable=true` → 통행 허용
- 원거리 몬스터 관통 등 정교 효과는 Phase F

### 6.5 RuleTile 자산

Unity 작업:
- `Palisade.asset` — 9방위 자동 연결 RuleTile (직선/코너/T자)
- `PalisadeGate.asset` — 정적 Sprite (게이트는 회전 불필요)
- 게이트 옆 Palisade는 게이트와 자연스럽게 연결되도록 룰 작성

> Stage 0~1의 마을 경계는 시각화하지 않음 (디자인 결정 — §1.1 노트). 플레이어는 NPC 행동/벽으로 추측.

---

## 7. 배치 정교화

### 7.1 인접 점유 페널티

`VillageTileFinder.FindEmptyTileNearest`에 후보 평가 추가:

```csharp
// 같은 링 내에서 단순 랜덤 → 점유 인접 셀 수 적은 후보 우선 선택
public static Vector2Int? FindEmptyTileNearest(Vector2Int center, int maxRadius)
{
    if (IsEmpty(center)) return center;

    for (int r = 1; r <= maxRadius; r++)
    {
        _bucket.Clear();
        CollectRing(center, r, _bucket);
        if (_bucket.Count == 0) continue;

        // 페널티: 8방위 인접 점유 셀 수 (낮을수록 좋음)
        Vector2Int best = _bucket[0];
        int bestPenalty = CountOccupiedNeighbors(best);
        for (int i = 1; i < _bucket.Count; i++)
        {
            int p = CountOccupiedNeighbors(_bucket[i]);
            if (p < bestPenalty || (p == bestPenalty && Random.value < 0.5f))
            {
                best = _bucket[i];
                bestPenalty = p;
            }
        }
        return best;
    }
    return null;
}

private static int CountOccupiedNeighbors(Vector2Int tile)
{
    int count = 0;
    for (int dx = -1; dx <= 1; dx++)
    for (int dy = -1; dy <= 1; dy++)
    {
        if (dx == 0 && dy == 0) continue;
        if (IsEmpty(new Vector2Int(tile.x + dx, tile.y + dy)) == false) count++;
    }
    return count;
}
```

### 7.2 "큰길" 보존 (선택, Phase C 후반)

마을 중심에서 N/E/S/W 방위로 폭 1타일 통로를 예약:

```csharp
public static bool IsReservedRoad(Vector2Int tile, Vector2Int center, int radius) {
    int dx = tile.x - center.x, dy = tile.y - center.y;
    // 축 위에 있고 (다른 축 == 0), 반경 안에 있으면 큰길
    return (dx == 0 && Mathf.Abs(dy) <= radius)
        || (dy == 0 && Mathf.Abs(dx) <= radius);
}
```

`IsEmpty`에 추가 체크:

```csharp
if (IsReservedRoad(tile, center, roadRadius)) return false;
```

`roadRadius`는 Stage별로 차등 (Settlement=2, Hamlet=4, Village=6).

→ 십자형 통로가 자연스럽게 만들어지고, NPC 통행/플레이어 동선이 보장됨.

---

## 8. 시스템 등록

마을 시스템은 **Priority 50-69 대역**에 배치한다. 도메인별 5단위 슬롯으로 분류해 이후 Phase D~F의 신규 마을 시스템을 같은 대역에 끼워넣는다.

### 8.1 Priority 대역 정책 (Village Domain)

| 대역  | 도메인       | 책임                                       | 향후 추가 예시 |
|-------|--------------|--------------------------------------------|----------------|
| 50-54 | Resource     | 자원 생산/소비, 저장 갱신                  | (Phase E) 추상 시뮬 |
| 55-59 | Population   | NPC 인구 (재스폰, 이민, 출생, 분가)        | (Phase E) 출생/분가 |
| 60-64 | Lifecycle    | 마을 상태 전이 (Tier 승격, 평판, 위협도)   | (Phase D) 필요도 평가 / (Phase F) ThreatLevel 갱신 |
| 65-69 | Construction | 오브젝트/벽 건설 큐                        | (Phase D) JobAssignment / (Phase F) WallRepair |

### 8.2 등록 테이블 (Phase C 적용 후)

| Priority | 시스템                                | Phase | 인터벌 | 도메인       | 상태  |
|---------:|---------------------------------------|-------|--------|--------------|-------|
| **52**   | `System_VillagePassiveProduction`     | A     | 5.0s   | Resource     | Priority 57→52 재할당 + Stage별 ×배수 추가 |
| **56**   | `System_VillagePopulation`            | A→C   | 5.0s   | Population   | 구 `System_VillageRespawn` 리네임 + 이민 흡수 (§3.5) |
| **60**   | `System_VillageTierProgression`       | C     | 5.0s   | Lifecycle    | 신규 |
| **66**   | `System_VillageBuildQueue`            | B     | 5.0s   | Construction | Priority 58→66 재할당. 로드맵이 Stage 분기 |
| **67**   | `System_VillageWallPlanner`           | C     | 5.0s   | Construction | 신규 — Town+ 마을의 벽 세그먼트 큐 생성 |

### 8.3 Phase D~F 추가 예약 슬롯

| Priority | 시스템 (예정)                         | Phase | 도메인       |
|---------:|---------------------------------------|-------|--------------|
| 50       | `System_AbstractVillageSimulation`    | E     | Resource     |
| 58       | `System_VillageBirth`                 | E     | Population   |
| 61       | `System_VillageNeedsEvaluation`       | D     | Lifecycle    |
| 62       | `System_VillageThreatLevel`           | F     | Lifecycle    |
| 68       | `System_VillageJobAssignment`         | D     | Construction |
| 69       | `System_VillageWallRepair`            | F     | Construction |

→ Phase C 구현 시점에 SystemManager에서 **기존 3개 시스템의 Priority 재할당** + 신규 2개 추가.

---

## 9. 세이브/로드 영향

### 9.1 신규 필드 (`VillageData`)

```csharp
// VillageComponent 미러
public int BoundsX, BoundsY, BoundsW, BoundsH;
public float ThreatLevel;
public int WallSegmentCount;
public int CompletedWallSegments;

// 벽 빌더 상태 (활성 시)
public bool WallPlanRequested;  // WallPlanRequestTag 미러
public List<WallSegmentSaveData> WallSegments;  // 각 세그먼트 위치+HP
```

### 9.2 `WallSegmentSaveData`

```csharp
[Serializable]
public class WallSegmentSaveData
{
    public int SegmentId;
    public int TileX, TileY;
    public int Type;       // WallType
    public int Orient;     // WallOrientation
    public int SegmentHP;
    public int MaxHP;
    public bool IsBuilt;   // false = 큐에 있지만 아직 미배치
}
```

### 9.3 마이그레이션

- 구 세이브 (`VillageComponent` 필드 없음) → 로드 시 `Stage` 기반으로 Bounds 재계산
- `WallSegments == null` → 빈 리스트
- 구 세이브 + Stage > Settlement면 **마이그레이션 시점에 Bounds 자동 산출**

---

## 10. 신규 `BuildableItemTable` 엔트리 (16종 + 벽 3종)

| Id | Name | HP | Cost_W | Cost_S | Cost_M | Cap_F | Cap_W | Cap_S | Function | SpawnType |
|---:|------|---:|-------:|-------:|-------:|------:|------:|------:|---------:|-----------|
| 102 | Bed | (기존) | | | | | | | | Entity |
| 112 | Stockpile | 30 | 0 | 10 | 0 | 0 | 0 | **80** | 0 | Entity |
| 140 | ChoppingBlock | 25 | 15 | 0 | 0 | 0 | 0 | 0 | 5 | Entity |
| 141 | DryingRack | 25 | 20 | 0 | 0 | 0 | 0 | 0 | 6 | Entity |
| 142 | MiningCart | 30 | 20 | 0 | 5 | 0 | 0 | 0 | 7 | Entity |
| 150 | Hearth | 50 | 0 | 20 | 0 | 0 | 0 | 0 | 8 | Entity |
| 151 | MerchantStall | 30 | 25 | 0 | 0 | 0 | 0 | 0 | 9 | Entity |
| 152 | TownPost | 60 | 30 | 15 | 0 | 0 | 0 | 0 | 10 | Entity |
| 153 | InnBed | 35 | 20 | 0 | 0 | 0 | 0 | 0 | 11 | Entity |
| 154 | SignalBrazier | 40 | 20 | 0 | 5 | 0 | 0 | 0 | 12 | Entity |
| 160 | Furnace | 80 | 30 | 40 | 0 | 0 | 0 | 0 | 13 | Entity |
| 161 | Anvil | 60 | 0 | 20 | 15 | 0 | 0 | 0 | 14 | Entity |
| 162 | QuenchVat | 40 | 15 | 0 | 5 | 0 | 0 | 0 | 15 | Entity |
| 170 | Shrine | 60 | 0 | 30 | 0 | 0 | 0 | 0 | 16 | Entity |
| 180 | Palisade | 100 | 8 | 0 | 0 | 0 | 0 | 0 | 0 | **Tile** |
| 181 | PalisadeGate | 100 | 40 | 0 | 0 | 0 | 0 | 0 | 0 | **Tile** |

**`Cost_Metal` 컬럼 추가 필요** — Phase C에서 `BuildableItemTable`에 1개 컬럼 더 추가 (`Cost_Metal: int = 0`).

`Function` 값 5~16: Phase D의 `ProvidedService` 비트마스크/직업 활성화로 점진 의미 부여. Phase C는 **플래그만 기록**.

---

## 11. 디버그 로그

새 태그:
| 태그 | 시점 | 포맷 |
|------|------|------|
| `[TierProgression]` | 승격 발생 | `v{id} {prev} → {next} (Bounds=..., Pop=N)` |
| `[TierProgression]` | 조건 미달 | (매 4h 로그 X — 너무 시끄러움. snapshot에서만 노출) |
| `[WallPlanner]` | 벽 계획 시작 | `v{id} Town 벽 계획 시작: 외곽 N칸, 게이트 M개` |
| `[WallPlanner]` | 세그먼트 완성 | `v{id} 벽 {built}/{total} ({pct}%)` (10% 단위로만) |
| `[Immigration]` | 이민 NPC 스폰 | `v{id} 이민자 도착 (Pop {old}→{new})` |

`VillageDebugLog.Snapshot` 확장:
```
[VillageSnapshot] v0 Stage=Hamlet Pop=4/8 Food=45/180 Wood=12/180 Stone=8/130 Hunger=0
                  Bounds=(-10,-10,20,20) Threat=0.00 Wall=0/24
                  Build=Bed(67%) Placed=Campfire,Bedroll,Bed,Woodpile,...
                  TierCheck: Pop✓3+ Bed✗4(2) Food✓80+ Age✓72h+ TownPost✗0 → 미달
```

---

## 12. 리스크와 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| Bounds 확장 시 기존 NPC/몬스터 위치가 마을 안으로 흡수 | 중 | Bounds 변경은 시각/스코어용. 실제 Walkable/spawn에 영향 없음. Phase F의 ThreatLevel에서 다룰 문제 |
| 벽 60칸 순차 건설이 너무 느림 (60 × 4h = 10일) | 중 | Stage 3 도달 시점에서 자원 충분 가정. 게임시간 30분 = 1일이므로 실시간 5시간. **WallBuildHours**를 Palisade는 30분 게임시간으로 단축 |
| `WallSegmentComponent`가 청크 비활성 시 사라짐 | 중 | 벽은 Tile 경로 → `MapFileData._objectList`에 저장. WallSegmentComponent는 활성 청크에서만 부착, 로드 시 재구성 (BuildingManager 패턴) |
| 동시에 여러 마을이 Stage 3 진입 | 저 | 독립 큐. 마을 간 자원 경쟁 없음 |
| Palisade RuleTile이 게이트와 자연스럽게 연결 안 됨 | 중 | RuleTile 룰에 "이웃이 Palisade OR PalisadeGate"면 직선으로 인식하도록 작성 |
| 큰길 예약이 너무 빡빡해서 빈 타일 부족 | 저 | `roadRadius` 튜닝 + Stage 1+에선 큰길도 조금 좁힘 |
| `Cost_Metal` 컬럼 추가 시 Phase B의 `BuildableItemTable.bytes` 호환 | 저 | 기본값 0이라 구 세이브/시트 호환 무손실. 시트 한 컬럼만 추가 |

---

## 13. 구현 순서 (작업 분해)

### Step 1 — 데이터 스키마 ✅ 완료
- [x] 1.1 `VillageStage` enum 확인 — 이미 정의됨 ([VillageStage.cs](../Assets/Scripts/Common/Enum/VillageStage.cs))
- [x] 1.2 `BuildableItemTable.Cost_Metal` 추가 ([Tables.cs](../Assets/Scripts/Common/Tables.cs) + [DownloadTables.cs](../Assets/Scripts/Editor/DownloadTables.cs) A:R → A:S)
- [x] 1.3 Google Sheets 신규 16행 추가 (Stockpile, ChoppingBlock, DryingRack, MiningCart, Hearth, MerchantStall, TownPost, InnBed, SignalBrazier, Furnace, Anvil, QuenchVat, Shrine, Palisade, PalisadeGate, Stockpile)
  - **BoundaryMarker(Id 182) 제거** — 디자인 결정에 따라 마을 경계 시각화 미도입 (§1.1)
- [x] 1.4 `BuildableItemTable.bytes` 동기 갱신

### Step 2 — ECS 컴포넌트 ✅ 완료
- [x] 2.1 [VillageComponent.cs](../Assets/Scripts/Common/Component/VillageComponent.cs) 신설 (Stage/Bounds/ThreatLevel)
- [x] 2.2 [WallPlanRequestTag.cs](../Assets/Scripts/Common/Component/WallPlanRequestTag.cs) 신설 (빈 태그)
- [x] 2.3 [WallSegmentComponent.cs](../Assets/Scripts/Common/Component/WallSegmentComponent.cs) 신설 (Phase F 준비)
- [x] 2.4 `ComponentManager`에 풀 32/32/500 등록 ([ComponentManager.cs:68-70](../Assets/Scripts/Manager/ComponentManager.cs#L68-L70))

### Step 3 — VillageData 확장 + 마이그레이션 ✅ 완료
- [x] 3.1 `BoundsX/Y/W/H`, `ThreatLevel`, `WallSegmentCount`, `CompletedWallSegments`, `WallPlanRequested` 추가 ([VillageData.cs](../Assets/Scripts/Village/VillageData.cs))
- [x] 3.2 [WallSegmentSaveData.cs](../Assets/Scripts/Village/WallSegmentSaveData.cs) 클래스 + `List<WallSegmentSaveData> WallSegments` 추가
- [x] 3.3 `VillageManager.Load`에서 Phase C 마이그레이션 (WallSegments null → empty, Bounds 자동 산출)

### Step 4 — VillageManager 확장 ✅ 완료
- [x] 4.1 `CreateStorageEntity`에서 `VillageComponent` 함께 부착 ([VillageManager.cs](../Assets/Scripts/Village/VillageManager.cs))
- [x] 4.2 `GetBoundsRadius(stage)` public static 헬퍼 추가
- [x] 4.3 `SyncTaskToData`에 VillageComponent + WallPlanRequestTag 미러링 추가
- [x] 4.4 로드 후 `WallPlanRequested == true`이면 `CreateStorageEntity` 끝에서 태그 재부착

### Step 5 — VillageBuildRoadmap Stage 분기 ✅ 완료
- [x] 5.1 `HAMLET_SEQUENCE` 8종 + `VILLAGE_SEQUENCE` 11종 정적 배열 추가 ([VillageBuildRoadmap.cs](../Assets/Scripts/Village/VillageBuildRoadmap.cs))
- [x] 5.2 `GetNextTarget` Stage switch 분기 + `GetNextFromSequence` 헬퍼 (이전 Stage offset 차감)
- [x] 5.3 `GetBuildHours` Stage 0/1/2 모든 TableId 커버

### Step 6 — System_VillageTierProgression ✅ 완료
- [x] 6.1 [System_VillageTierProgression.cs](../Assets/Scripts/Common/System/System_VillageTierProgression.cs) 신설 (Priority 60)
- [x] 6.2 `EvaluateNextStage`, `Promote`, `CountInPlaced` 구현
- [x] 6.3 BoundaryMarker 배치 로직 — **제거됨** (디자인 결정, §1.1)
- [x] 6.4 Town 진입 시 `WallPlanRequestTag` 자동 부착

### Step 7 — System_VillageRespawn → System_VillagePopulation ✅ 완료
- [x] 7.1 `System_VillageRespawn.cs` 삭제 (+.meta)
- [x] 7.2 [System_VillagePopulation.cs](../Assets/Scripts/Common/System/System_VillagePopulation.cs) 신설 (Priority 56, Population 도메인)
- [x] 7.3 `TickImmigration()` — 게임시간 24h마다 Hamlet+ 마을 확률 스폰
- [x] 7.4 SystemManager 등록 클래스명 변경

### Step 8 — VillageTileFinder 정교화 ✅ 완료
- [x] 8.1 [VillageTileFinder.cs](../Assets/Scripts/Village/VillageTileFinder.cs)에 인접 점유 페널티 (8방위 카운트) 추가
- [x] 8.2 `IsReservedRoad` + `SetRoadReserveRadius` API 추가 (큰길 예약)
- [x] 8.3 `System_VillageBuildQueue.GetRoadReserveRadius(stage)` Stage 기반 반경 전달

### Step 9 — 외곽 벽 인프라 ✅ 완료
- [x] 9.1 [WallTypes.cs](../Assets/Scripts/Village/WallTypes.cs) — `WallType`, `WallOrientation` enum
- [x] 9.2 [System_VillageWallPlanner.cs](../Assets/Scripts/Common/System/System_VillageWallPlanner.cs) (Priority 67) — Bounds 외곽 추출 + 게이트 N/S 1개씩
- [x] 9.3 [WallSegmentRegistry.cs](../Assets/Scripts/Village/WallSegmentRegistry.cs) — 마을별 미완성 세그먼트 큐 조회/마킹
- [x] 9.4 BuildQueue에 벽 우선 처리 — `TryStartWallTask`/`OnWallSegmentCompleted` + `IsWallTask` 분기

### Step 10 — SystemManager Priority 재할당 ✅ 완료
- [x] 10.1 PassiveProduction 57 → **52** ([System_VillagePassiveProduction.cs](../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs))
- [x] 10.2 BuildQueue 58 → **66** ([System_VillageBuildQueue.cs](../Assets/Scripts/Common/System/System_VillageBuildQueue.cs))
- [x] 10.3 SystemManager.Initialize 도메인 대역별 등록 + 주석 정비

### Step 11 — 디버그 로그 ✅ 완료
- [x] 11.1 [VillageDebugLog.cs](../Assets/Scripts/Village/VillageDebugLog.cs)에 Bounds, Wall 진행률, ThreatLevel 추가
- [x] 11.2 `FormatTierCheck` — Stage별 승격 조건 항목 ✓/✗ 표시

### Step 12 — 문서 갱신 ✅ 완료
- [x] 12.1 PHASE_C_DESIGN.md 완료 마킹
- [x] 12.2 VILLAGE_GROWTH_STAGES.md Phase C 체크박스 갱신

### Step U1 — Sprite 자산 (Unity 작업) ⏳ 사용자 진행 필요
신규 14종 placeholder PNG import + Addressable 등록 (`Sprites/Items/{Stockpile, ChoppingBlock, DryingRack, MiningCart, Hearth, MerchantStall, TownPost, InnBed, SignalBrazier, Furnace, Anvil, QuenchVat, Shrine}`).

### Step U2 — RuleTile 자산 (Unity 작업) ⏳ 사용자 진행 필요
- `Assets/Art/Tilemap/RuleTile_Palisade.asset` (9방위 자동 연결)
- `Assets/Art/Tilemap/RuleTile_PalisadeGate.asset` (정적)
- BoundaryMarker는 **제외** (디자인 결정 — §1.1)
- Addressable 키: `Tile/Palisade`, `Tile/PalisadeGate`
- Build Addressables

**총 예상 시간**: 코드 ~6.5시간 ✅ + Unity 자산 ~1.5시간 ⏳ = 약 8시간 (코드 부분 완료)

---

## 14. DoD (완료 기준)

### 14.1 Tier 승격 동작
- [ ] Phase B 완료 상태에서 플레이 → Stage 0 로드맵 소진 → 게임시간 24h+ 경과 + Bed 2개 + Pop 3 + Food 30 만족 → `[TierProgression] v0 Settlement → Hamlet` 로그 + Bounds 6→10 확장
- [ ] Hamlet 진입 후 HAMLET_SEQUENCE 시작, TownPost 완성 + Pop 8 + Food 80 + 72h → Village 승격
- [ ] Village 진입 후 VILLAGE_SEQUENCE 시작, Furnace+Anvil+MerchantStall + Pop 15 + 168h → Town 승격
- [ ] Town 진입 시 `WallPlanRequestTag` 부착 → `[WallPlanner] 벽 계획 시작` 로그

### 14.2 벽 건설
- [ ] Town 진입 후 외곽 타일에 Palisade가 순차 배치되는 것 시각 확인
- [ ] 게이트 위치는 Walkable 유지, NPC 통과 가능
- [ ] 모든 세그먼트 완성 시 `[WallPlanner] 벽 100%` 로그

### 14.3 배치 정교화
- [ ] Stage 1 빌드 시점에 새 오브젝트가 기존 오브젝트 인접 셀을 회피하는 경향 시각 확인
- [ ] 마을 중심에서 N/S/E/W 방위 통로 1칸이 비어있음 (큰길 예약)

### 14.4 세이브/로드
- [ ] Hamlet 상태 저장 → 종료 → 로드 → Stage/Bounds 유지, 진행 중 태스크 이어짐
- [ ] Town에 벽 절반 완성 상태 저장 → 로드 → 벽 위치/HP 유지, 미완성 세그먼트 큐 이어짐

### 14.5 비회귀
- [ ] Phase B 7개 오브젝트 자동 배치 + Cap 확장 그대로 동작
- [ ] Stage 0 로그 (`[BuildQueue]`) 변화 없음

---

## 15. 결정 필요 이슈

| # | 이슈 | 제안 기본값 | 비고 |
|---|------|------------|------|
| 1 | Stage 0→1 게임시간 24h 조건이 너무 빠름/느림? | **24h** (실시간 12분) | Phase B 플레이 후 튜닝 |
| 2 | 벽 1칸 건설 시간 | **30분 게임시간** (실시간 15초) | 외벽 60칸 = 약 30분 실시간. 너무 길면 단축 |
| 3 | 세트 판정 (`Furnace+Anvil`)을 Phase C에서 단순 PlacedObjectTypeIds 카운트로 처리 vs Phase D `HasObjectSet` 도입 | **단순 카운트** (Phase D로 미룸) | "같은 타일 그룹(5×5)" 판정은 Phase D |
| 4 | 자연 이민 로직을 별도 `System_VillageImmigration`으로 신설할까, 기존 시스템에 통합할까? | **`System_VillageRespawn` → `System_VillagePopulation`으로 확장 후 흡수** (§3.5, §8) | 같은 도메인(NPC 인구) + 같은 데이터 의존 → 별도 신설 불필요 |
| 5 | 큰길 예약을 Phase C 초기부터 활성화? | **Stage 1부터** (Settlement는 좁은 마을이라 큰길 굳이) | `roadRadius=0`이면 비활성 |
| 6 | StoneWall(Stage 3→4)을 Phase C에 포함? | **미포함** (Phase C+로 분리) | StoneWall은 단순 Palisade 변종 |
| 7 | Bounds 확장 시 기존 외부 오브젝트(채집물 등) 처리 | **무시** (그대로 둠) | 마을 안으로 들어와도 동작 변경 없음 |
| 8 | 패시브 생산 ×배수 (Stage별) | Settlement=1.0, Hamlet=1.1, Village=1.2, Town=1.3 | 데이터 주도로 `VillageTable.PassiveProductionMultiplier` 컬럼 활용 |

---

## 16. Phase D 대비 (선행 투자)

Phase C에서 미리 손대두면 Phase D 착수가 쉬워지는 것들:

1. **`Function` 컬럼 의미 부여** — Phase C는 플래그만 기록(5~16). Phase D의 `ProvidedService` 비트마스크 매핑 시 직접 변환
2. **`PlacedObjectTypeIds` 위치 정보** — 현재 TableId만 누적. Phase D의 세트 판정(같은 5×5 그룹)을 위해 `List<PlacedObjectInfo>` (TableId + TileX + TileY)로 확장 검토 — 단, Phase C는 카운트만이라 OK
3. **`VillageComponent.ThreatLevel` 필드** — 0 고정이지만 Phase F에서 단순히 값만 갱신하면 됨

---

## 17. 한 줄 요약

> **Phase C는 마을이 진짜로 자라게 한다 — Settlement에서 Town까지의 4단계 승격을 게임시간 일주일 안에 거쳐, 마지막엔 나무 울타리로 둘러싸인 정착지가 된다.**
