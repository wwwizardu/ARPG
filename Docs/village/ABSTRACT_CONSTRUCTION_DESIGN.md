# 마을 추상 건설 시뮬레이션 (B-lite) — 설계 문서

> 상위 문서: [VILLAGE_PHASES.md](VILLAGE_PHASES.md) · 관련: [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md), [NPC_SIMPLIFICATION_PLAN.md](../npc/NPC_SIMPLIFICATION_PLAN.md)
>
> **상태**: 설계 초안 (2026-07-12). 미구현.
>
> **한 줄 요약**: 멀리 있는 마을이 "인구만" 추상 시뮬되고 건설은 멈춰 있다가, 플레이어가 다가오면 한꺼번에 우르르 착공되는 문제를 해결한다. 추상 층에서 **자원·건설 결정·건설 진행도까지** 시간에 따라 진짜로 굴리고(개수·진행만, 좌표는 미할당), 실체화 시점에 **이미 완공된 상태**로 배치한다.

---

## 1. 배경 / 문제

### 1.1 현재 동작

마을은 2단계 LOD로 돈다:

- **추상(abstract)**: 플레이어가 멀 때. [SettlementLiveState](../../Assets/Scripts/History/Chronicle/WorldSimState.cs)가 **인구(`Pop`)만** 로지스틱으로 성장. 건설·자원은 정지.
- **실체화(materialized)**: 플레이어가 가까워 청크가 뜰 때. ECS 엔티티 생성, [System_VillageBuildQueue](../../Assets/Scripts/Common/System/System_VillageBuildQueue.cs)·[System_VillagePassiveProduction](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs)가 실제로 돎.

### 1.2 버그

[HistoryManager.MaterializeSettlement](../../Assets/Scripts/History/HistoryManager.cs)가 실체화 시 **인구만 현재 시각으로 catch-up**하고 건설은 catch-up하지 않는다. 그 직후 빌드 큐가 "인구는 큰데 진행 중 공사는 0"인 상태를 보고 한 틱에 착공을 몰아친다 → 플레이어 눈앞에서 건물이 우르르 지어짐.

> 임시 완화: 착공 동시 한도를 `Population`에서 **1**로 낮춤 (2026-07-12, [System_VillageBuildQueue Phase 2](../../Assets/Scripts/Common/System/System_VillageBuildQueue.cs)). 우르르는 줄었으나, 여전히 도착 후 "지금부터" 하나씩 짓기 시작한다 — 오래 방치된 마을이 빈 채로 보이는 근본 문제는 남음.

### 1.3 근본 원인

**추상 층이 인구만 굴리고 건설·자원은 안 굴린다.** 인구는 "세계가 계속 돈다"는 철학으로 시뮬되는데 건물만 예외라, 인구/건물의 취급이 어긋난다.

---

## 2. 목표 / 비목표

### 목표
- 오래 방치된 마을은 방문 시 **인구에 걸맞게 이미 성장·건설된** 모습이어야 한다.
- 실체화 순간의 **건설 버스트(우르르 착공) 제거**.
- 인구와 동일한 철학: 건설·자원도 시간에 따라 추상적으로 진행 → **원장(source of truth)은 하나**.

### 비목표
- **타일 좌표의 추상 결정(B-full)은 하지 않는다.** 좌표는 실체화 시 배정한다 (§4.3, §7).
- 추상 층에서 GameObject/비주얼 인스턴스화는 하지 않는다 (청크 없이는 불가).
- NPC 물리 시뮬(전투·행동)은 범위 밖 — 기존대로 실체화 마을에서만.
- 마을 밸런스 상수 재보정은 별도 작업(선택 A). 본 문서는 **시점(when) 문제**만 다룬다.

---

## 3. 방식 결정: B-lite

### 3.1 왜 B (추상 시뮬)인가 — vs A (materialize catch-up)

- **A**: 추상 층은 인구만 유지, 실체화 순간 "인구 N·경과 M시간 → 건물 X개"를 역산해 즉시 완공 배치. 싸지만 건물이 인구의 **결정적 파생값**이라, 자원 부족으로 인한 건설 지연 등 개별 마을 이력을 표현 못 함. 인구/건물 취급이 어긋남.
- **B**: 건설도 시간에 따라 진짜로 진행. 인구와 철학적으로 일관. → **채택.**

### 3.2 왜 B-lite (좌표 미할당) 인가 — vs B-full (좌표까지 추상)

건물 배치는 두 결정으로 나뉜다: **①무엇을/얼마나** (개수·종류·진행도) vs **②어디에** (타일 좌표).

②는 [VillageTileFinder.FindEmptyTileNearest](../../Assets/Scripts/Village/VillageTileFinder.cs)가 담당하는데, **지도 격자(walkable/occupied)를 읽어야 하고 이 격자는 로드된 청크에만 존재**한다. 원거리 마을은 격자가 없어 좌표를 정할 수 없다.

- **B-full**: `VillageData`만으로 도는 별도 점유 모델을 만들어 좌표까지 추상 결정. **비용 큼.**
- **B-lite**: ①만 추상으로 굴리고 좌표는 미할당으로 저장, 실체화 때 배정. **비용 낮음.**

**핵심 근거**: GameObject는 청크 로드 시에만 생성 가능 → 시각 인스턴스화는 어느 방식이든 **도착 순간에 몰린다**. 따라서 좌표를 미리 정하는 B-full의 추가 비용은 플레이어 체감 이득이 거의 없다. **B-lite 채택.**

B-lite에서도 "우르르 건설"은 사라진다 — 도착 시 건물은 **건설중이 아니라 완공 상태**로 배치되므로, 기존에 존재하던 건물이 청크와 함께 뜨는 것과 동일하게 보인다.

---

## 4. 아키텍처

### 4.1 대원칙: 공통 데이터 전용 코어 (정합성의 열쇠)

> ⚠️ **최대 리스크**: 추상 경제와 실시간 경제가 **다른 결과**를 내면, 실체화 순간 상태가 튀어 고치려던 버그가 재발한다.

이를 막는 유일한 방법은 **추상 경로와 실시간 경로가 하나의 코어 로직을 공유**하는 것이다. 즉 이 작업의 절반은 "새 기능"이 아니라 **기존 로직을 ECS 비의존 코어로 리팩터**하는 것.

```
                 ┌─────────────────────────────┐
                 │   공통 데이터 전용 코어        │
                 │   (VillageData만 입력)        │
                 │  · 자원 생산 (prodPop/carry)  │
                 │  · 건설 결정 점수 (Needs)     │
                 │  · 건설 진행/완공 판정        │
                 └───────────┬─────────────────┘
                             │
          ┌──────────────────┴───────────────────┐
     추상 경로                               실시간 경로
  (원거리, tick당 slice)                (실체화, 매 FixedUpdate)
  좌표=미할당,비주얼X                    좌표 배정+PlaceObject+ECS
```

### 4.2 기존 자산 (이미 준비됨)

| 자산 | 위치 | 상태 |
|---|---|---|
| `VillageData` (모든 마을에 존재) | [VillageManager.Initialize](../../Assets/Scripts/Village/VillageManager.cs) | ✅ 게임 시작 시 모든 마을 맵에 대해 등록. ECS(`EntityId`)만 지연 |
| 자원 저장 `VillageData.Resources` | VillageData | ✅ 데이터 전용 API (`GetResourceAmount`/`ConsumeResource`/`ProduceResource`) |
| 완성 건물 `List<PlacedObjectSaveData>` (`TableId,TileX,TileY,Hp…`) | VillageData | ✅ 위치·HP 포함 원장 |
| 진행중 공사 `List<BuildTaskSnapshot>` (`TableId,AccumulatedHours,TileX,TileY…`) | VillageData | ✅ 진행도·위치 포함 원장 |
| 실체화 복원 `Restore*FromData` | [VillageManager](../../Assets/Scripts/Village/VillageManager.cs) | ✅ 저장 좌표로 ECS 재생성 (round-trip 무손실) |
| 인구 closed-form advance | [SettlementPopulationModel.Advance](../../Assets/Scripts/History/Chronicle/SettlementPopulationModel.cs) | ✅ 복사할 템플릿 |
| 생산 prodPop/carry 로직 | [System_VillagePassiveProduction](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs) | ⚠️ 계산은 데이터 전용이나 carry가 ECS 컴포넌트에 상주 (§5) |

### 4.3 좌표 미할당 표현

추상 층에서 완공/착공된 건물은 좌표를 모른다. `PlacedObjectSaveData`/`BuildTaskSnapshot`의 `TileX/TileY`에 **sentinel(예: `int.MinValue`)**을 넣어 "미할당"을 표시한다. 실체화 복원 시:

- 좌표가 유효하면 → 기존대로 그 좌표에 복원.
- 좌표가 미할당이면 → **그 시점에** [VillageTileFinder.FindEmptyTileNearest](../../Assets/Scripts/Village/VillageTileFinder.cs)로 자리를 찾아 배정 후 배치.

착공 상태(`BuildTaskSnapshot`)로 추상 중 시작된 건물은 `BuildingEntityId = -1` (비주얼 엔티티 없음) — 실체화 시 생성.

---

## 5. 상태 저장 (VillageData 확장)

추상 생산은 소수 누적이 필요한데, 현재 carry 버퍼는 ECS `VillageStorageComponent`에 있다(실체화 중에만 존재). 추상 층에서도 굴리려면 **carry를 `VillageData`로 이동**한다.

```csharp
// VillageData 신규 필드 (세이브, 추가만 하므로 하위호환)
public float FoodCarry, WoodCarry, StoneCarry, IronCarry, GoldCarry;   // 생산 소수 이월
public float LastConstructionSimGameTime;   // 건설 추상 clock (인구의 LastSimGameTime과 대칭)
```

- 실시간 경로도 이 `VillageData` carry를 읽고 쓰도록 통일 → 컴포넌트의 `*Carry`는 제거 또는 미러링만. **carry의 원장을 `VillageData` 단일화** (정합성).
- 기존 dead 필드 `StoneTimer/IronTimer/GoldTimer`(§선택 A 리팩터로 사용 중단)는 이번에 정리 가능.

> 세이브 마이그레이션: 필드 추가라 구 세이브 로드 시 0으로 초기화되어 안전. `LastConstructionSimGameTime=0`이면 첫 tick에서 현재 시각으로 초기화(인구 패턴과 동일).

---

## 6. 추상 건설 advance 알고리즘

인구는 closed-form(로지스틱)이지만, **건설은 자원 게이팅 때문에 closed-form 불가** (자원이 모여야 착공, 착공 후 시간 경과로 완공). 따라서 **stepped fast-forward**로 경과 시간을 진행한다.

### 6.1 시간 스텝

경과 `elapsedHours = now - LastConstructionSimGameTime`를 **스텝 단위**(예: 1일=24h, 또는 "다음 이벤트까지")로 끊어 반복. 스텝 수는 건물 수(마을당 `MaxPerVillage`/Stage 상한)와 자원 속도로 상한이 걸려 유한.

```
while (LastConstructionSimGameTime < now):
    step = min(STEP_HOURS, now - LastConstructionSimGameTime)

    1) 자원 생산:  공통 코어로 step 시간분 자원 누적 (prodPop/carry, §4.1)
    2) 건설 진행:  진행중 task들의 AccumulatedHours += step; 완공(>=Duration) 시
                   → PlacedObjects/PlacedObjectTypeIds로 이동 (좌표 미할당)
    3) 착공 결정:  동시 한도(현재 1) 미만이고 자원 충분하면
                   → VillageNeedsEvaluator(데이터 전용)로 다음 건물 선정
                   → 자원 차감, BuildTaskSnapshot 추가 (좌표 미할당, EntityId=-1)
    LastConstructionSimGameTime += step
```

> STEP_HOURS는 정확도 vs 비용 트레이드오프. 건설 시간(수 시간~) 대비 하루 스텝이면 충분히 근사. **결정 필요 (§9).**

### 6.2 데이터 전용화가 필요한 기존 로직

[VillageNeedsEvaluator](../../Assets/Scripts/Village/VillageNeedsEvaluator.cs)는 ~70% 데이터 전용이나 ECS를 읽는 3곳이 있다. 각각 `VillageData` 기반 변형 필요:

| ECS 의존 | 대체 소스 |
|---|---|
| `CountInProgressTasksByTableId` (task 풀 스캔) | `v.ActiveBuildTasks` 카운트 |
| `CountByService(Housing 등)` (PlacedObjectComponent) | `v.PlacedObjects` + `BuildableItemTable.ProvidedService` 룩업 |
| `AggregateSetMembers` (세트 커버리지) | `v.PlacedObjects` + `BuildableItemTable.SetMembership` 룩업 |

착공의 나머지(자원 체크/차감, `BuildHours` 선정, task 레코드 추가)와 벽 세그먼트 조회(`WallSegmentRegistry`)는 이미 데이터 전용.

### 6.3 라운드로빈 예산

인구의 [AdvanceAbstractSlice](../../Assets/Scripts/History/HistoryManager.cs)처럼, 건설도 tick당 일부 마을만 처리하는 **라운드로빈 slice**로 부하를 분산 (`IsMaterialized` 마을은 skip — 실시간 경로가 담당).

---

## 7. 실체화 (materialize) 경로

### 7.1 catch-up 훅

[MaterializeSettlement](../../Assets/Scripts/History/HistoryManager.cs)에서 인구 flush 직후, `MaterializeVillage` **호출 전에** 건설 catch-up을 실행 (인구 flush와 대칭). 커서가 못 들른 경과분을 §6 알고리즘으로 현재 시각까지 진행 → `VillageData`가 최신 건설 상태를 반영한 뒤 ECS 복원.

### 7.2 타일 배정 + 배치

[RestorePlacedObjectsFromData](../../Assets/Scripts/Village/VillageManager.cs) / [RestoreTaskFromData](../../Assets/Scripts/Village/VillageManager.cs) 수정:

- 좌표 유효 → 기존대로.
- 좌표 미할당(§4.3) → `VillageTileFinder`로 자리 배정 후 배치. 완공 건물은 `isUnderConstruction=false`로 배치(비주얼 완성). 진행중 task는 `AccumulatedHours` 보존한 채 건설중 엔티티 생성.

> 배치 실패(자리 없음) 처리: 미할당 건물이 들어갈 자리가 없으면 건너뛰거나 경계 확장 로직에 맡김 — **정책 결정 필요 (§9)**.

### 7.3 fold-back

[FoldBackSettlement](../../Assets/Scripts/History/HistoryManager.cs)는 이미 `DematerializeVillage`로 ECS→`VillageData` flush 후 추상 재개. 추가 작업 없음 — 다음 추상 tick이 flush된 `ActiveBuildTasks`/`PlacedObjects`에서 이어감. `LastConstructionSimGameTime=now`로 재설정 (인구 패턴과 동일).

---

## 8. 작업 분해

| # | 작업 | 크기 | 산출물 |
|---|---|---|---|
| 1 | 공통 데이터 전용 코어 추출: 생산(prodPop/carry)을 `VillageData` 기반으로, carry 원장 이동 | 중 | `VillageData` carry 필드, 생산 코어 함수 |
| 2 | `VillageNeedsEvaluator` 데이터 전용 변형 (§6.2 3함수) | 중 | ECS 비의존 점수 경로 |
| 3 | 추상 건설 advance stepped 함수 (§6) | 중~대 | `AdvanceAbstractConstruction(VillageData, now)` |
| 4 | 좌표 미할당 sentinel + 실체화 타일 배정 (§4.3, §7.2) | 중 | `Restore*FromData` 수정 |
| 5 | catch-up 훅 + tick slice (§6.3, §7.1) | 소 | `MaterializeSettlement`/`System_SettlementSim` 훅 |
| 6 | 세이브 마이그레이션 + dead 필드 정리 (§5) | 소 | 하위호환 로드 |
| 7 | 검증/튜닝: 무-버스트, **추상↔실시간 정합성**, 세이브 왕복 | 중 | 테스트 시나리오 |

**규모: 중~대 기능.** 집중 세션 5~7회 예상.

---

## 9. 열린 질문 (구현 전 확정 필요)

1. **STEP_HOURS** (§6.1): 하루(24h) 단위 vs 이벤트 단위? 하루 단위를 기본 제안 (근사 충분·단순).
2. **미할당 건물 배치 실패 정책** (§7.2): 자리 없으면 skip? 경계 확장? 재시도?
3. **동시 착공 한도**: 현재 임시로 1. 추상에서도 1 유지? 아니면 인구 기반 소수로 복원?
4. **자원 Cap 상호작용**: 추상 생산도 Cap(FoodCap 등) 적용? 적용 시 오래 방치된 마을은 Cap에서 포화 → 건설 자원이 항상 충분. (선택 A 밸런스와 연동)
5. **carry 원장**: 실시간 경로도 `VillageData` carry로 완전 통일할지(권장), 컴포넌트 미러링 유지할지.

---

## 10. 참고

- 임시 완화(착공 한도 1) 및 자원 prodPop 압축은 2026-07-12 선행 적용됨 (본 문서와 별개, [System_VillageBuildQueue](../../Assets/Scripts/Common/System/System_VillageBuildQueue.cs) · [System_VillagePassiveProduction](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs)).
- 추상 인구 계층: [SettlementPopulationModel](../../Assets/Scripts/History/Chronicle/SettlementPopulationModel.cs), [HistoryManager](../../Assets/Scripts/History/HistoryManager.cs), [System_SettlementSim](../../Assets/Scripts/Common/System/System_SettlementSim.cs).
- 마을 경제 인구 소싱: [NPC_SIMPLIFICATION_PLAN.md](../npc/NPC_SIMPLIFICATION_PLAN.md).
