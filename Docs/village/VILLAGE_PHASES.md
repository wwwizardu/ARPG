# 마을 자가 성장 시스템 — Phase A~D 통합 요약

> 상위 문서: [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md)
>
> **상태**: Phase A/B/C/D 모두 코드 완료 (2026-04-24 ~ 2026-04-28). 후속 Phase E~G는 §5 참조.
>
> 본 문서는 Phase A~D 4개 설계 문서를 통합한 요약본. 각 Phase의 핵심 결정과 산출물만 보존, 세부 히스토리는 생략.

---

## 1. 한 줄 요약

| Phase | 완료일 | 한 줄 요약 |
|-------|--------|-----------|
| **A** | 2026-04-24 | 시간이 흐르고 → 자원이 쌓이고 → NPC가 첫 모닥불을 짓는다 |
| **B** | 2026-04-24 | 모닥불 → 침낭/침대/야적장/궤짝/텃밭/우물 일반화 (범용 BuildQueue) |
| **C** | 2026-04-26 | Settlement → Hamlet → Village → Town 4단계 승격 + Palisade 외벽 |
| **D** | 2026-04-28 | 플레이어가 상점·강화·여관·제단을 쓰고, NPC는 직업으로 일한다 |

---

## 2. 누적 발전 흐름

```
Phase A: 시간 → 자원 → 1 모닥불 (Campfire 단일 하드코딩)
   │   System_VillagePassiveProduction · System_VillageFirstBuild
   ↓
Phase B: + 범용 BuildQueue + 오브젝트 6종
   │   System_VillageFirstBuild → System_VillageBuildQueue 흡수
   │   ObjectPlacementTaskComponent + VillageBuildRoadmap
   ↓
Phase C: + Tier 승격 + 외벽 + Stage 1~2 (총 16+ 오브젝트)
   │   System_VillageTierProgression · System_VillageWallPlanner
   │   VillageComponent (Stage/Bounds/ThreatLevel)
   │   Priority 대역 정책 (50-69 Village domain)
   ↓
Phase D: + 서비스 (Shop/Forge/Inn/Shrine) + 세트 + 필요도 + 직무
       PlacedObjectComponent + ObjectSetCatalog
       System_VillageNeedsEvaluation · ServiceProximity · JobAssignment
       데이터 정본화 (No Hardcoded TableId)
```

---

## 3. Phase별 요약

### 3.A — 자가 생성 최소 루프 (MVP) ✅ 2026-04-24

**목표**: "플레이어가 손대지 않아도 마을이 아주 느리게 스스로 돌아간다"의 **가장 작은 단위** 증명.

**범위**:
- 시간 기반 패시브 자원 생산/소비 (NPC 수 비례, Food/Wood/Stone, 시간당 정수)
- 자원 저장 상한(Cap) — 기본 50, 초과 절삭
- 첫 오브젝트 (Campfire 1개) 자동 건설 — 마을 단위 추상 타이머
- `Debug.Log` + `VillageDebugLog.SnapshotAll()` 디버깅 (UI 없음)

**핵심 결정**:
- **`VillageStorageComponent` 부분 승격** — `VillageData.Resources`와 병존 (블래스트 반경 제어)
- **`ObjectType` enum 미확장** — 신규 오브젝트 식별은 `BuildableItemTable.Id`가 담당
- **자원은 정수 + 게임시간 정수h 차분** — `deltaHour = floor(currentGameTime) - lastProcessedHour`
- **Campfire 단일 하드코딩** — 범용 BuildQueue는 Phase B에서. MVP는 1개 전용 시스템
- **추상 타이머** — NPC 실제 이동 없음. NPC 0명 시 정지 X (계속 진행)
- **BuildableTileRegistry lazy on-demand** — `await EnsureLoadedAsync(id)`. 캐시 영구 유지
- **하이브리드 건물** — `BuildableItemTable.SpawnType` (Tile / Entity 경로 공존)

**산출물**:
- 신규 시스템: `System_VillagePassiveProduction` (Priority 57→52 in C) · `System_VillageFirstBuild` (Phase B에서 BuildQueue로 흡수)
- 신규 컴포넌트: `VillageStorageComponent` (Pool 32)
- 신규 인프라: `BuildableTileRegistry` (lazy 캐시) · `BuildingManager`/`BuildingFactory` (Entity 경로) · `VillageDebugLog`
- 테이블: `BuildableItemTable` Campfire 엔트리 (Id=100)

### 3.B — 범용 배치 큐 ✅ 2026-04-24

**목표**: Phase A의 "NPC 1명 → Campfire 1개" 단일 루프를 **다수 오브젝트 순차 배치 큐**로 일반화.

**범위**:
- 범용 BuildQueue — `ObjectPlacementTaskComponent`로 마을당 여러 오브젝트 순차 건설
- 하드코딩 로드맵 — Stage 0(Settlement) 시퀀스 (Phase D 점수교체 전 과도기)
- 오브젝트 6종 추가 — Bedroll, Bed, Woodpile, Chest, CropPlot, Well (모두 1×1, Entity 경로)
- 배치 위치 분산 — 같은 링 내 랜덤 픽으로 중심 편향 해소
- Cap 데이터 주도 — `BuildableItemTable.StorageCap_*` 컬럼 직접 가산

**핵심 결정**:
- **마을당 동시 배치 = 1건** — 추상 타이머 단순화 + 자원 경쟁 방지. SparseSet 제약과 정합
- **`ObjectPlacementTaskComponent`는 비세이브** — `VillageData.CurrentBuild*` 6필드가 세이브 정본
- **Cap 확장은 데이터 주도** — `Function` 컬럼과 분리. 시트 한 곳에서 튜닝
- **Chest는 공유 풀이 아닌 `Wood +30 + Stone +30` 근사** — 정확한 공유 풀은 Phase D
- **로드맵 6종 전부 `SpawnType=Entity`** — CropPlot Walkable=true는 Entity 경로에서 Blocked 비트 미세팅
- **`FindEmptyTileNearest`를 링 내 랜덤 픽으로** — SW 코너 편향 해소
- **환불 경로** — `PlaceObject` 실패 시 `CurrentBuildReservedWood/Stone`로 자원 환불 후 재시도

**산출물**:
- 신규 컴포넌트: `ObjectPlacementTaskComponent` (Pool 32)
- 신규 시스템: `System_VillageBuildQueue` (Priority 58→66 in C)
- 신규 클래스: `VillageBuildRoadmap` (정적, `SETTLEMENT_SEQUENCE` 7행)
- 테이블: `BuildableItemTable` 신규 5컬럼 + 6행 (101 Bedroll · 102 Bed · 110 Woodpile · 111 Chest · 120 CropPlot · 130 Well)
- `VillageData.PlacedObjectTypeIds` + `CurrentBuild*` 6필드 추가

### 3.C — Tier 승격 + Stage 1·2 + 외벽 인프라 ✅ 2026-04-26

**목표**: Phase B의 "Stage 0 자가 건설 루프"에 **Tier 승격**을 붙여 Settlement → Hamlet → Village → Town. 외곽 벽(Palisade) 인프라.

**범위**:
- Tier 승격 — `System_VillageTierProgression` (4h 인터벌). 조건 만족 시 Stage 전환 + Bounds 확장 + 패시브 ×배수
- `VillageComponent` — Stage/Bounds/ThreatLevel
- 로드맵 확장 — Stage 1(Hamlet) 8종 + Stage 2(Village) 11종
- 외곽 벽 인프라 — `System_VillageWallPlanner` · `WallSegmentComponent` · RuleTile
- 배치 정교화 — 8방위 점유 페널티 + Stage별 큰길 예약 (Hamlet 4 / Village 6 / Town 8)
- 인구 통합 — `System_VillageRespawn` → `System_VillagePopulation` (자연 이민 흡수)
- Priority 대역 정책 — 50-69 Village domain을 4-sub band로 (§4.1 참조)

**핵심 결정**:
- **Stage 0~1 마을 경계 시각화 안 함** — 벽/NPC 행동으로 emergent perception (explicit marker 미도입)
- **벽 트리거 시점: Hamlet (1)** — 초기 Town(3) 안에서 앞당김. Hamlet Bounds(20×20) 기준 외곽 1회 계획
- **벽/로드맵 자원 기반 교대** — 일반 건물 자원 충분 시 우선, 부족 시에만 벽 fallback. Gate는 항상 우선
- **벽 단일 큐 (병렬 X)** — `ObjectPlacementTaskComponent` 1슬롯 제약. 60칸 외벽 = 게임시간 30분
- **세트 판정은 단순 카운트** — Stage 2→3 "Furnace + Anvil"은 `PlacedObjectTypeIds.Contains`. 거리 판정은 Phase D `HasObjectSet`로
- **`System_VillageRespawn` → `System_VillagePopulation`로 흡수** — 자연 이민을 별도 시스템 X. Settlement도 이민 허용
- **`VillageTable.DefaultNpcList` 시작 인구 보정** — `3001,3001,3001`로 Stage 0→1 게이트(Pop ≥ 3) 즉시 충족

**산출물**:
- 신규 컴포넌트: `VillageComponent` (Stage/Bounds/ThreatLevel) · `WallPlanRequestTag` · `WallSegmentComponent`
- 신규 시스템: `System_VillageTierProgression` (Priority 60, 4h) · `System_VillageWallPlanner` (Priority 67) · `System_VillagePopulation` (Priority 56)
- 신규 클래스: `WallTypes` enum · `WallSegmentRegistry` · `WallSegmentSaveData`
- 시스템 확장: PassiveProduction Stage별 ×배수 (Settlement 1.0 → Town 1.3) · BuildRoadmap Stage switch + `HAMLET_SEQUENCE`/`VILLAGE_SEQUENCE` · `VillageTileFinder` 8방위 페널티 + 큰길 예약 · `GetBoundsRadius` (Settlement=6 / Hamlet=10 / Village=14 / Town=18 / City=24)
- 테이블: `Cost_Metal` 컬럼 + 17행 (Stage 1: 8, Stage 2: 8, Wall: Palisade/PalisadeGate)
- Priority 재할당: PassiveProduction 57→52 · BuildQueue 58→66

### 3.D — 플레이어 ↔ 마을 루프 완성 ✅ 2026-04-28

**목표**: 마을이 자가 성장한다의 다음 한 걸음 — **플레이어가 마을과 상호작용해서 진짜 이득을 본다**. 서비스(상점/강화/여관/제단), 자원 판매, 필요도 스코어, NPC 직무.

**범위**:
- `PlacedObjectComponent` + Registry — 위치/HP를 ECS로 승격, `tableId → entityIds` + `tile → entityId` 인덱스
- 세트 판정 — `HasObjectSet`, `SetMemberTag` 비트 + `ObjectSetCatalog` 단일 사전
- `ProvidedService` 비트마스크 — `Function` 컬럼 폐기, 정식 enum
- `System_VillageServiceProximity` — 플레이어 근처 서비스를 0.3s마다 `PlayerNearbyServicesComponent`로 집계
- 서비스 UI 4종 (Shop/Forge/Inn/Shrine) — F키 입력 시 우선순위 라우팅
- 상점 양방향 거래 — 매물 풀 + 인벤토리 매각 + 마을 자원 부분 환원(50%)
- 필요도 스코어 — `System_VillageNeedsEvaluation` (2h). 로드맵은 fallback
- 배치 구역화 — 카테고리 클러스터 가산점 + 외곽 보호 마진 (비-Defense는 경계 ≥ 2 안쪽)
- NPC 직무 할당 — `System_VillageJobAssignment` · `JobBonusTable` 직업 보너스
- **데이터 정본화** — TableId 분기 0건 (§4.2)

**핵심 결정**:
- **`PlacedObjectTypeIds` 유지 + `PlacedObjectComponent` 양립** — 카운트 전용은 ID-only 그대로, 위치/HP는 ECS. `OnObjectPlaced` 한 곳에서 동시 갱신
- **데이터 정본화 — No Hardcoded TableId** — Phase C 상수 5개(`BED_ID/TOWNPOST_ID/...`) Step 5b에서 모두 제거. `CountByService`/`HasObjectSet`로 대체
- **세트 정의 = `SetMemberTag` 비트 + `ObjectSetCatalog` 단일 사전** — 세트 추가 = 비트 + 사전 한 줄 + 시트
- **자원 활용 = 상점 판매 단일화** — 기증 시스템 폐기. 매각 시 자원 50% 마을 환원. 명성은 Phase F로 이관
- **매각가/환원 = `ItemTable` 컬럼** — `BasePrice` · `SellRatioBp` · `ReturnResourceType` · `ReturnRatioBp`. 카테고리 분기 코드 X
- **필요도 스코어 + 로드맵 fallback** — 점수 함수에 *체감(diminishing returns)* + *식량 풍족 시 Production 페널티* 추가 (CropPlot 영원 1위 방지)
- **외곽 보호 마진** — 비-Defense ≥ 2타일 안쪽, Defense는 경계 위. ServiceProximity 보장과 맞물림
- **ServiceProximity는 단일 마을 한정** — 두 마을 동시 잡히면 가장 가까운 1개만
- **Shrine 쿨다운은 엔티티 단위** — `PlacedObjectComponent.LastUseGameTime`. ECS 일관성. 어뷰징은 `MaxPerVillage=1`
- **NPC 직무: 1:1 매칭, 보너스 가산만** — 실제 이동/일과는 Phase E
- **카테고리 클러스터 가산점** — 같은 카테고리 인접 8방위당 +1, 다른 카테고리 -0.5. 강제 X, 부족 시 폴백

**산출물**:
- 시트: `BuildableItemTable` 신규 6컬럼 (`ProvidedService`/`Category`/`SetMembership`/`AssociatedJobType`/`BaseWeight`/`MaxPerVillage`) + 23행 입력 · `ItemTable` 4컬럼 (`BasePrice`/`SellRatioBp`/`ReturnResourceType`/`ReturnRatioBp`) + 77행 · 신규 `JobBonusTable` 5행
- ECS 컴포넌트: `PlacedObjectComponent` · `PlayerNearbyServicesComponent` · `NpcAssignmentComponent` · `PlacedObjectRegistry` · `PlacedObjectSaveData` · `ObjectSetCatalog` (6종 세트 정의)
- API: `HasObjectSet(setType, anchor?)` · `CountByService(service)` · `SellItemToMerchant`/`BuyItemFromMerchant` · `EnsureMerchantStockFresh`/`RollMerchantStock` · `OnObjectPlaced(tableId, tileX, tileY)`
- 신규 시스템: `System_VillageNeedsEvaluation` (Priority 61, 2h) · `System_VillageServiceProximity` (Priority 61, IUpdate 0.3s) · `System_VillageJobAssignment` (Priority 68, 1h)
- 시스템 확장: PassiveProduction `JobBonusTable` 가산 · TierProgression 상수 모두 제거 · BuildQueue NeedsCache 1순위 · VillageTileFinder 외곽 마진 + 클러스터
- UI: `ServiceUIRouter` · `UIShopMerchant`/`UIForge`/`UIInn`/`UIShrine` 스켈레톤 · `System_Input` 라우팅

**잔여 작업**:
1. Step U1 — Unity prefab 4종 + Addressable 등록 (사용자 작업)
2. Step 10 후속 — prefab 부착 후 UI 슬롯 위젯 본격
3. 점수 함수 튜닝 — 실 플레이 후 BaseWeight/페널티 가중치 조정

---

## 4. 공통 아키텍처 (Phase 간 일관성)

### 4.1 Priority 대역 정책

Phase C에서 도입. 마을 시스템을 도메인별 5단위 슬롯으로 분류. 후속 Phase E~F 신규 시스템도 같은 대역에 끼워넣음.

| 대역 | 도메인 | 책임 | 예시 |
|------|--------|------|------|
| 50-54 | Resource | 자원 생산/소비/저장 | PassiveProduction (52) |
| 55-59 | Population | NPC 인구 | Population (56) |
| 60-64 | Lifecycle | 마을 상태 전이 | TierProgression (60) · NeedsEvaluation (61, D) · ServiceProximity (61, D) |
| 65-69 | Construction | 오브젝트/벽 건설 | BuildQueue (66) · WallPlanner (67) · JobAssignment (68, D) |

### 4.2 데이터 정본화 — No Hardcoded TableId

Phase D에서 본격 도입. **코드에 `if (id == 160)` 같은 TableId 분기를 두지 않는다.** 의미는 모두 `BuildableItemTable` 컬럼에 박는다.

핵심 컬럼 6개: `ProvidedService`(bitmask) · `Category` · `SetMembership`(bitmask) · `AssociatedJobType` · `BaseWeight` · `MaxPerVillage`. 구 `Function` 폐기.

새 오브젝트 추가 시나리오: 시트 1행 + (필요 시) `SetMemberTag`/`ObjectSetCatalog` 한 줄 → C# 변경 0.

Phase C 임시 상수 5개(`BED_ID/TOWNPOST_ID/FURNACE_ID/ANVIL_ID/MERCHANTSTALL_ID`)는 Phase D Step 5b에서 모두 제거됨. `CountByService` + `HasObjectSet`로 대체.

### 4.3 하이브리드 건물 시스템 — Tile vs Entity

Phase A에서 도입. `BuildableItemTable.SpawnType` 컬럼이 경로 분기:
- **Tile** — Tilemap 셀로 렌더, `objectId`를 타일 비트에 기록 (나무 벽 같은 정적 반복 구조)
- **Entity** — `EntityBase` GameObject로 스폰, `BuildingManager` 관리 (Campfire 같은 개별 상태/상호작용 필요한 오브젝트)

Phase B의 로드맵 6종 모두 Entity. Tile 경로는 Phase C의 Palisade/StoneWall까지 보존.

### 4.4 Cap 데이터 주도

Phase B에서 도입. Cap 증가량은 C# switch가 아니라 `BuildableItemTable.StorageCap_Food/Wood/Stone` 컬럼에서 직접 가산.

이점:
- 튜닝이 시트 한 곳에 집중 (Woodpile +100 → +120: 시트만 수정)
- 새 저장 오브젝트 추가 = 코드 0줄 (Barrel/Stockpile 등)

### 4.5 세트 정의 비트마스크

Phase D에서 도입. 세트 구성도 코드 분기로 박지 않음.

```
ForgeBasic    = Forge_Heat (5×5)
ForgeStandard = Forge_Heat | Forge_Anvil (5×5)
ForgePremium  = Forge_Heat | Forge_Anvil | Forge_Quench (5×5)
Inn           = Inn_Bed | Inn_Hearth (마을 전체)
Birth         = Birth_Bed | Birth_Hearth (3×3) — Phase E
Library       = Library_Book | Library_Desk (5×5) — Phase F+
```

`HasObjectSet`은 영역 내 SetMember 비트 OR이 RequiredMask를 모두 덮는지 검사. 새 세트 추가 = 비트 + 사전 한 줄 + 시트.

---

## 5. 후속 / 미해결 (Phase E~G)

| Phase | 범위 (예정) |
|-------|----------|
| **E** | 배후 시뮬레이션 (NPC 일과 — 수면/식사/일터 이동) · 출생/이탈/분가 · NeedsEvaluation 로드맵 fallback 제거 |
| **F** | 위협도 실제 변동 · 몬스터 침공 · 벽 파괴/재건 · 명성(Reputation) 시스템 (퀘스트 보상 경로) · 장비 제작 (재료 → 새 장비) · StoneWall · Stage 4 (City) |
| **G** | WatchTower 2×2 · 후속 고급 콘텐츠 |

---

## 6. 외부 참조

- 상위: [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md) §10
- Phase A 디테일 보존본: [archive/PHASE_A_HYBRID_PLAN.md](../_archive/PHASE_A_HYBRID_PLAN.md) · [archive/PHASE_A_IMPLEMENTATION.md](../_archive/PHASE_A_IMPLEMENTATION.md) · [archive/PHASE_A_UNITY_CHECKLIST.md](../_archive/PHASE_A_UNITY_CHECKLIST.md)
- 관련 시스템: [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md) · [INN_HIRING_DESIGN.md](INN_HIRING_DESIGN.md) · [COLLISION_PATHFINDING_DESIGN.md](../map/COLLISION_PATHFINDING_DESIGN.md) · [SKILLBOOK_DESIGN.md](../combat/SKILLBOOK_DESIGN.md) · [NPC_RELATION_HISTORY_DESIGN.md](../npc/NPC_RELATION_HISTORY_DESIGN.md)

---

**문서 통합일**: 2026-05-19. 원본 `PHASE_A_DESIGN.md`~`PHASE_D_DESIGN.md` 폐기 후 본 문서가 정본.
