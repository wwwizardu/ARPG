# Village — 런타임 정본 지도

---
name: village
description: 마을 경제·건설·Tier·벽·서비스와 실시간/추상 LOD의 정본을 연결한다.
type: topic
last_updated: 2026-07-22
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
- 마을은 고정 인구가 정한 `InitialStage`까지의 시설·상점·벽을 완공 상태로 갖춘 **초기 완성형**으로 등장한다. 런타임 시간 경과 자동 건설·Tier 승격은 `RuntimeProgressionEnabled=false`로 중단돼 있고(재개는 후속 Phase), 점수순 자가 건설·동시 task 1개 규칙은 수리·이벤트와 발전 재개 시에만 쓰인다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §4·§6, Docs/village/BUILD_PRIORITY_DESIGN.md]
- 마을 정체성은 저장 `VillageProfileId`로 고정하고, Growth/Layout/Trade 프로필이 성장 계획·슬롯 배치·지역 상권(결정론 재고·가격)을 연결한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §5]
- 손제작 마을(BaseTown 등)은 절차 정착지 연대기에 연결되지 않아 `SettlementId=-1`로 남고 항상 실체화 상태를 유지한다. 이 경우 물리 NPC 배분 1명 하한과 대화 홈 종족 정체성을 `VillageProfile.Race`가 대체한다(상세는 [npc.md](npc.md) §5). [src: Assets/Scripts/Village/VillageManager.cs:43-54, Assets/Scripts/Manager/NpcManager.cs]
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
- 초기 시설은 필요도 점수가 아니라 `VillageGrowthPlanCatalog` 마일스톤으로 완공 생성하며, 중복 방지 정본은 `VillageData.AppliedGrowthMilestones`다. `VillageSimulationParity.CompareFixedState`가 고정 인구·Profile·Stage·시설·상점 스냅샷이 save/load·동결 advance·fold-back 반복 전후 불변임을 검증한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §11·§13]

## 4. 남은 일

- V7 통합 검증은 정적(하니스·Editor 진단)으로 통과했고, 실제 청크 materialize·상호작용·벽·상점 UI·Race 배치 시각 실측은 플레이 모드 체크리스트(A~F)로 남아 있다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7]
- 런타임 마을 발전(인구 증감·Stage 승격·자동 건설)은 V8 후속으로 보류돼 있고 게이트는 비활성이다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §9]
- 런타임 폐허 전환은 Fate·Pop·task·건설 중 Building·materialize 차단·clock을 함께 바꾸는 원자적 진입점으로 구현해야 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §12]
