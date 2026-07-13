# Village — 런타임 정본 지도

---
name: village
description: 마을 경제·건설·Tier·벽·서비스와 실시간/추상 LOD의 정본을 연결한다.
type: topic
last_updated: 2026-07-13
sources:
  - Docs/village/VILLAGE_RUNTIME_DESIGN.md
  - Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md
  - Docs/village/BUILD_PRIORITY_DESIGN.md
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/village/VILLAGE_GROWTH_STAGES.md
  - Assets/Scripts/Village/
  - Assets/Scripts/Common/System/System_Village*.cs
---

## 1. 현재 구조

- `VillageData`가 자원·Cap·carry·완공 건물·진행 task·벽·Tier의 영속 원장이고, ECS 컴포넌트는 실체화된 마을의 런타임 미러다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §1]
- 가까운 마을은 `System_Village*`, 먼 마을은 `System_SettlementSim`과 `VillageAbstractConstructionScheduler`가 처리한다. 두 경로는 생산·후보 점수·비용·완료·Tier·벽 규칙을 공유한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §2, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §3]
- 마을 경제 인구는 물리 NPC 수가 아니라 `SettlementLiveState.Pop`을 정량화한 값이다. 물리 NPC는 모험가 활동·전투·관계 계층에 속하며 생산과 건설에 배정되지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1–4]
- 건물과 벽은 게임시간으로 자가 건설되고 전체 동시 task는 1개다. 일반 후보는 점수순으로 평가하며, 1위의 자원이 부족하면 낮은 후보로 우회하지 않는다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §4, Docs/village/BUILD_PRIORITY_DESIGN.md §4–5]
- Shop·Forge·Inn·Shrine은 NPC가 아니라 배치 건물의 `ProvidedService`가 제공한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §7, Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §5]

## 2. 정본 읽기 순서

| 질문 | 정본 |
|---|---|
| 전체 책임·데이터 흐름·정본 경계 | [VILLAGE_RUNTIME_DESIGN](../Docs/village/VILLAGE_RUNTIME_DESIGN.md) [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md] |
| 실시간/추상 자원·건설, materialize, 초기 발전 | [ABSTRACT_CONSTRUCTION_DESIGN](../Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md) [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md] |
| 건설 후보 점수와 자원 부족 대기 정책 | [BUILD_PRIORITY_DESIGN](../Docs/village/BUILD_PRIORITY_DESIGN.md) [src: Docs/village/BUILD_PRIORITY_DESIGN.md] |
| 경제 인구와 물리 NPC의 경계 | [VILLAGE_NPC_BOUNDARY_DESIGN](../Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md] |
| Stage별 장기 콘텐츠 목표 | [VILLAGE_GROWTH_STAGES](../Docs/village/VILLAGE_GROWTH_STAGES.md) [src: Docs/village/VILLAGE_GROWTH_STAGES.md] |

`VILLAGE_GROWTH_STAGES`는 미래 콘텐츠 방향이며 현재 런타임 계약을 재정의하지 않는다. [src: Docs/village/VILLAGE_GROWTH_STAGES.md]

## 3. 핵심 계약

- 완료 부작용은 `VillageConstructionLedger.ApplyCompletedBuild`에서 한 번만 적용하고, 기존 완공 레코드를 월드에 복원할 때 다시 호출하지 않는다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §4]
- 미할당 Entity 건물은 materialize 때 타일과 BuildingManager 레코드를 만든다. 실패하면 `PlacementPending`으로 남겨 다음 materialize 또는 Bounds 확장 후 재시도한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §5·§9]
- 추상 catch-up이 끝나지 않으면 실체화하지 않는다. fold-back에서는 두 construction clock을 현재 시각에 맞춰 같은 시간을 다시 처리하지 않게 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §7·§10]
- `NpcEntityIds.Count`, NPC 직업, Working 상태를 마을 인구·생산·건설의 입력으로 사용하지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §8]

## 4. 남은 일

- 실제 청크·Addressables, PlacementPending 재시도, materialize/fold-back, 진행 task와 완공 레코드의 세이브 왕복을 Unity 플레이 모드에서 통합 검증해야 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §13]
- 런타임 폐허 전환은 Fate·Pop·task·건설 중 Building·materialize 차단·clock을 함께 바꾸는 원자적 진입점으로 구현해야 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §12]
