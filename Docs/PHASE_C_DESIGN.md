# Phase C — Tier 승격 + Stage 1·2 확장 + 벽 인프라 ✅ 완료 (2026-04-26)

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅ · [PHASE_B_DESIGN.md](PHASE_B_DESIGN.md) ✅
> 후속: [PHASE_D_DESIGN.md](PHASE_D_DESIGN.md)
>
> **목표**: Phase B의 "Stage 0 자가 건설 루프"에 **Tier 승격**을 붙여 마을이 Settlement → Hamlet → Village → Town으로 자라게 한다. 외곽 벽(Palisade) 인프라를 깔고, 배치 분산을 정교화한다.

---

## 1. 범위

**Phase C가 한 것**:
1. **Tier 승격** — `System_VillageTierProgression` (4h 인터벌). 조건 만족 시 Stage 전환 + Bounds 확장 + 패시브 ×배수
2. **`VillageComponent`** — Stage/Bounds/ThreatLevel 보유, Phase D 필요도 스코어 참조용
3. **로드맵 확장** — Stage 1(Hamlet) 8종 + Stage 2(Village) 11종 시퀀스
4. **외곽 벽 인프라 (Palisade)** — `System_VillageWallPlanner`, `WallSegmentComponent`, RuleTile
5. **배치 정교화** — 8방위 점유 페널티 + Stage별 "큰길" 예약 (Hamlet 4 / Village 6 / Town 8)
6. **인구 시스템 통합** — `System_VillageRespawn` → `System_VillagePopulation`. 자연 이민 흡수
7. **Priority 대역 정책** — 50-69 Village domain을 Resource/Population/Lifecycle/Construction 4-sub band로 분할

**제외**:
- Stage 3 → 4 (City), StoneWall, WatchTower(2×2) → Phase G / 후속
- 세트 판정(`HasObjectSet`), `ProvidedService` 비트마스크 → Phase D
- 벽 파괴/재건, ThreatLevel 실제 변동 → Phase F
- 배후 시뮬레이션 → Phase E

> **한 줄 범위**: "마을이 Town까지 자라고, 진작에 나무 울타리가 자동으로 생긴다."

---

## 2. 핵심 설계 결정

### 2.1 Stage 0~1 마을 경계 시각화 안 함

`BoundaryMarker` 같은 명시적 시각 표지를 도입하지 않는다. 플레이어는 **벽(Stage 1+)** 또는 **NPC 행동(이동 범위, 일과 패턴)**으로 마을 영역을 자연스럽게 추측 — emergent perception이 explicit marker보다 게임플레이에 풍부함.

→ Step U2에서 BoundaryMarker(Id 182)는 시트/Addressable 모두 미도입.

### 2.2 벽 트리거 시점: Town(3) → Hamlet(1) (2026-04-26 변경)

초기 설계는 Stage 3 도달 시 벽 시작이었으나, Hamlet 진입 시점에 활성화하도록 앞당김. 벽은 **Hamlet Bounds(20×20) 기준 외곽으로 1회 계획**, 이후 Stage 승격으로 Bounds 확장돼도 벽은 그대로 유지 (성벽 안 = 마을 코어, 바깥 = 농지/외곽).

이점: 초기부터 마을 영역이 시각적으로 명확, Phase D의 `ServiceProximity`(플레이어가 마을 안일 때만 서비스 잡힘)와 잘 맞음.

### 2.3 벽/로드맵 자원 기반 교대 (2026-04-28 정책 뒤집기)

**최종 룰** — `System_VillageBuildQueue.TryStartNextTask`:
- Gate(외벽 입구)는 **항상 우선**
- 일반 건물 자원 충분 시 → **항상 일반 건물 우선**
- 자원 부족 시에만 → 벽 fallback

**히스토리**: 초안은 "잉여 Wood 시 벽 우선"이었으나, 일반 건물 비용이 낮을 때(Hearth 등 Stone-only) 벽이 무한 도배되는 부작용 발생 → 폐기.

### 2.4 벽 단일 큐 (병렬 X)

`ObjectPlacementTaskComponent`가 마을당 1슬롯이라 N칸 동시 큐잉 불가. **벽 1칸 완성 → 다음 칸 시작** 순차 처리. 60칸짜리 외벽이면 게임시간 30분 단축 빌드시간으로 ~30분 실시간. 병렬화는 Phase D 이후 NPC 단위 배정으로 자연 해소.

### 2.5 세트 판정은 단순 카운트 (Phase D HasObjectSet 도입 전)

Stage 2→3 승격 조건 "Furnace + Anvil"은 Phase C에서 **`PlacedObjectTypeIds.Contains(FURNACE_ID) && Contains(ANVIL_ID)` 단순 카운트**. "같은 5×5 안" 같은 거리 판정은 Phase D `HasObjectSet`로 본격화. Phase C는 코드에 `BED_ID/TOWNPOST_ID/...` 상수 5개를 일시 도입 (Phase D Step 5b에서 모두 제거됨).

### 2.6 `System_VillageRespawn` → `System_VillagePopulation`로 흡수

기존 정원 재스폰 시스템에 자연 이민(Phase C 신규)을 별도 `System_VillageImmigration`으로 신설 대신 **같은 시스템에 통합**. 이유:

- 동일 도메인 (NPC 인구) + 동일 데이터 의존 (`VillageData.Population`/`NpcEntityIds`)
- Priority 슬롯 1개 절감
- Settlement 이민 허용 (2026-04-26): `Stage < Hamlet` 가드 제거, Bedroll도 잠자리로 카운트, Stage별 확률 (Settlement 10% → City 30%)

### 2.7 Priority 대역 정책 (50-69 Village domain)

마을 시스템을 도메인별 5단위 슬롯으로 분류. Phase D~F 신규 시스템도 같은 대역에 끼워넣는다.

| 대역 | 도메인 | 책임 | 예시 |
|------|--------|------|------|
| 50-54 | Resource | 자원 생산/소비/저장 | PassiveProduction (52) |
| 55-59 | Population | NPC 인구 | Population (56) |
| 60-64 | Lifecycle | 마을 상태 전이 | TierProgression (60) · NeedsEvaluation (61, D) |
| 65-69 | Construction | 오브젝트/벽 건설 | BuildQueue (66) · WallPlanner (67) · JobAssignment (68, D) |

### 2.8 `VillageTable.DefaultNpcList` 시작 인구 보정

`3001,3001,3001` 로 시작 Pop 3 보장 → Stage 0→1 게이트(Pop ≥ 3) 즉시 충족 가능. 이전엔 Pop 1로 시작해 이민까지 24h 대기.

---

## 3. 구현 결과 요약

| 영역 | 결과물 |
|------|--------|
| 신규 컴포넌트 | `VillageComponent` (Stage/Bounds/ThreatLevel) · `WallPlanRequestTag` · `WallSegmentComponent` (HP/Type/Orient — Phase F 파괴 시점부터 본격 사용) |
| 신규 시스템 | `System_VillageTierProgression` (Priority 60, 4h) · `System_VillageWallPlanner` (Priority 67) · `System_VillagePopulation` (Priority 56, 구 Respawn 리네임 + 이민 흡수) |
| 신규 클래스 | `WallTypes`(enum) · `WallSegmentRegistry` · `WallSegmentSaveData` |
| 시스템 확장 | `System_VillagePassiveProduction` Stage별 ×배수 (Settlement 1.0 → Town 1.3) · `VillageBuildRoadmap` Stage switch 분기 + `HAMLET_SEQUENCE`/`VILLAGE_SEQUENCE` · `VillageTileFinder` 8방위 페널티 + 큰길 예약 · `VillageManager.GetBoundsRadius` (Settlement=6 / Hamlet=10 / Village=14 / Town=18 / City=24) |
| 테이블 | `BuildableItemTable.Cost_Metal` 컬럼 추가 + 신규 17행 (Stage 1: 8종, Stage 2: 8종, Wall: Palisade/PalisadeGate) |
| Priority 재할당 | PassiveProduction 57→52 · BuildQueue 58→66 |
| 신규 자산 | 14종 Sprite placeholder + Palisade/PalisadeGate RuleTile + Addressable 키 |

**후속 정리(Phase C+ 이관)**: `WallSegmentSaveData.TableId` 필드 추가. StoneWall 도입 시 BuildQueue 분기 코드 0으로. 현재는 `PALISADE_TABLE_ID`/`PALISADE_GATE_TABLE_ID` 상수 분기 유지로 충분.

---

## 4. 한 줄 요약

> **Phase C는 마을이 진짜로 자라게 한다 — Settlement에서 Town까지의 4단계 승격을 게임시간 일주일 안에 거쳐, 마지막엔 나무 울타리로 둘러싸인 정착지가 된다.**
