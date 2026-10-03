---
name: village
description: 마을 경제·건설·Tier·벽·서비스와 실시간/추상 LOD의 정본을 연결한다.
type: topic
last_updated: 2026-10-03
sources:
  - Docs/village/VILLAGE_RUNTIME_DESIGN.md
  - Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md
  - Docs/village/BUILD_PRIORITY_DESIGN.md
  - Docs/village/VILLAGE_REWORK_PLAN.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/village/VILLAGE_GROWTH_STAGES.md
  - Docs/npc/NPC_AMBITION_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
  - Assets/Scripts/Village/
  - Assets/Scripts/Common/System/
  - Assets/Scripts/History/HistoryManager.cs
  - Assets/Scripts/Manager/MapManager.cs
---

# Village — 런타임 정본 지도

## 1. 현재 구조

- `VillageData`가 자원·Cap·carry·완공 건물·진행 task·벽·Tier의 영속 원장이고, ECS 컴포넌트는 실체화된 마을의 런타임 미러다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §1]
- 가까운 마을은 `System_Village*`, 먼 마을은 `System_SettlementSim`과 `VillageAbstractConstructionScheduler`가 처리한다. 두 경로는 생산·후보 점수·비용·완료·Tier·벽 규칙을 공유한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §2, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §3]
- 마을 경제 인구는 물리 NPC 수가 아니라 `SettlementLiveState.Pop`을 정량화한 값이다. 물리 NPC는 모험가 활동·전투·관계 계층에 속하며 생산과 건설에 배정되지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1–4]
- 마을은 고정 인구가 정한 `InitialStage`까지의 시설·상점·벽을 완공 상태로 갖춘 **초기 완성형**으로 등장한다. 신규 게임에서는 종족·Profile·초기 Stage와 무관하게 Forge 160을 최소 Tier 1로 추가 보장하고, 기존 성장 계획이 만든 Tier 2/3 Forge는 낮추지 않는다. 런타임 시간 경과 자동 건설·Tier 승격은 `RuntimeProgressionEnabled=false`로 중단돼 있다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §4·§6, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5.1, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs]
- 마을 정체성은 저장 `VillageProfileId`로 고정하고, Growth/Layout/Trade 프로필이 성장 계획·슬롯 배치·지역 상권(결정론 재고·가격)을 연결한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §5]
- 손제작 마을(BaseTown 등)은 절차 정착지 연대기에 연결되지 않아 `SettlementId=-1`로 남고 항상 실체화 상태를 유지한다. 세이브 로드에서는 데이터 등록 단계가 아니라 Map·Building·Component가 준비된 `VillageManager.Initialize()`에서 실체화한다. 이 경우 물리 NPC 배분 1명 하한과 대화 홈 종족 정체성을 `VillageProfile.Race`가 대체한다(상세는 [npc.md](npc.md) §5). [src: Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Manager/NpcManager.cs]
- Shop·Forge·Inn·Shrine은 NPC가 아니라 배치 건물의 `ProvidedService`가 제공한다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md §7, Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §5]
- 모험가 NPC는 마을 상점과 **닫힌 경제**로 거래한다. NPC 전리품 매입 대금은 마을 Gold에서 나가고(부족하면 매입 거절), NPC의 장비 구매·치료비는 마을 Gold로 들어온다. 골드 생성처는 마을 인구 생산 하나다. 이 경로는 마을–NPC 경계의 예외로 명시돼 있다(D44). [src: Docs/npc/NPC_AMBITION_DESIGN.md §7.2·§9·§11, Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §3.1]
- 상점 재고는 기존 결정론 칸(보장·특산·희귀·특수, 무작위 옵션 장비) 뒤에 **기본 장비 칸**(상점 Tier 1~2는 3칸, 3~4는 4칸, 고정 옵션)을 같은 회차 RNG로 이어 롤한다. 플레이어도 사고, NPC는 기본 장비만 산다. 회차당 NPC 구매 한도는 기본 장비 칸을 빼고 센다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §4·§5.3, Docs/village/VILLAGE_REWORK_PLAN.md §8.4–8.5, Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Village/VillageTradeStockRoller.cs]

## 2. 정본 읽기 순서

| 질문 | 정본 |
|---|---|
| 전체 책임·데이터 흐름·정본 경계 | [VILLAGE_RUNTIME_DESIGN](../Docs/village/VILLAGE_RUNTIME_DESIGN.md) [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md] |
| 실시간/추상 자원·건설, materialize, 초기 발전 | [ABSTRACT_CONSTRUCTION_DESIGN](../Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md) [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md] |
| 건설 후보 점수와 자원 부족 대기 정책 | [BUILD_PRIORITY_DESIGN](../Docs/village/BUILD_PRIORITY_DESIGN.md) [src: Docs/village/BUILD_PRIORITY_DESIGN.md] |
| 경제 인구와 물리 NPC의 경계 | [VILLAGE_NPC_BOUNDARY_DESIGN](../Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md] |
| Stage별 장기 콘텐츠 목표 | [VILLAGE_GROWTH_STAGES](../Docs/village/VILLAGE_GROWTH_STAGES.md) [src: Docs/village/VILLAGE_GROWTH_STAGES.md] |
| 모든 신규 마을의 Forge 접근성과 마법부여 계약 | [ITEM_ENCHANTMENT_DESIGN](../Docs/combat/ITEM_ENCHANTMENT_DESIGN.md) §5 [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md] |
| 상점의 NPC 거래·상점 보호·Gold 순환 | [NPC_AMBITION_DESIGN](../Docs/npc/NPC_AMBITION_DESIGN.md) §7.2·§9 [src: Docs/npc/NPC_AMBITION_DESIGN.md] |
| 기본 장비 칸·기본 아이템 테이블·저장 표현 | [NPC_SHOP_EQUIPMENT_DESIGN](../Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md) §3–6 [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md] |

`VILLAGE_GROWTH_STAGES`는 미래 콘텐츠 방향이며 현재 런타임 계약을 재정의하지 않는다. [src: Docs/village/VILLAGE_GROWTH_STAGES.md]

## 3. 핵심 계약

- 완료 부작용은 `VillageConstructionLedger.ApplyCompletedBuild`에서 한 번만 적용하고, 기존 완공 레코드를 월드에 복원할 때 다시 호출하지 않는다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §4]
- 미할당 Entity 건물은 materialize 때 타일과 BuildingManager 레코드를 만든다. 실패하면 `PlacementPending`으로 남겨 다음 materialize 또는 Bounds 확장 후 재시도한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §5·§9]
- 추상 catch-up이 끝나지 않으면 실체화하지 않는다. fold-back에서는 두 construction clock을 현재 시각에 맞춰 같은 시간을 다시 처리하지 않게 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §7·§10]
- `NpcEntityIds.Count`, NPC 직업, Working 상태를 마을 인구·생산·건설의 입력으로 사용하지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §8]
- 초기 시설은 필요도 점수가 아니라 `VillageGrowthPlanCatalog` 마일스톤으로 완공 생성하며, 중복 방지 정본은 `VillageData.AppliedGrowthMilestones`다. `VillageSimulationParity.CompareFixedState`가 고정 인구·Profile·Stage·시설·상점 스냅샷이 save/load·동결 advance·fold-back 반복 전후 불변임을 검증한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §11·§13]
- 신규 게임 Forge 보장은 종족별 성장 마일스톤 적용 뒤 모든 `VillageData`에 후조건으로 한 번 실행한다. Forge 원장 레코드와 `PlacedObjectTypeIds`가 정확히 하나로 일치하면 상위 Tier를 보존하고, 없으면 완공 원장으로 Tier 1을 생성한다. 중복·불일치 데이터는 임의 수정하지 않고 오류로 거부하며 구 세이브 전체 backfill은 하지 않는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5.1, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs]
- Forge 보장으로 새 미할당 시설이 생기거나 Tier 0이 정규화되면 pending 배치를 재시도한다. 이미 배치된 시설은 재시도 과정에서도 저장 `FacilityTier`를 Building 레코드와 `PlacedObjectComponent`에 다시 동기화한다. [src: Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageManager.cs]
- Gate는 닫힌 벽 바깥에서도 열 수 있어야 하므로 마을 내부 판정과 분리한다. 근접 시스템은 현재 실체화된 `VillageComponent` 풀의 완공 Gate를 전역 탐색하며 Shop·Forge·Inn·Shrine 등 일반 시설은 계속 현재 마을 안에서만 찾는다. [src: Assets/Scripts/Common/System/System_VillageServiceProximity.cs]
- 신규 게임 초기 발전은 `GameScene.CreateMap()` 전에도 청크를 조회할 수 있으므로 `MapManager.Initialize()`가 랜덤 생성기를 먼저 준비한다. [src: Assets/Scripts/Manager/MapManager.cs, Assets/Scripts/History/HistoryManager.cs]

## 4. 남은 일

- 기존 V7 통합 검증은 정적 하니스 기준을 갖추고 있으나, 이번 공통 Forge 후조건·로드 시 손제작 마을 실체화·벽 바깥 Gate 탐색까지 포함한 실제 청크 materialize와 상호작용은 PlayMode에서 다시 실측해야 한다. 마법부여 전용 Editor 진단 코드도 추가됐지만 실행 결과 아티팩트는 저장소에 없다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs] [추론: 저장소에 진단 실행 결과 파일이 없음]
- `VILLAGE_REWORK_PLAN.md`의 2026-08-30 메모는 공통 Forge 보장을 아직 미구현이라고 기록하지만, 더 최신 마법부여 정본과 현재 코드는 신규 게임 부트스트랩에 이를 연결한다. 원본 메모가 정리되기 전까지 해당 문장은 구현 전 이력으로만 읽고 현행 동작은 코드와 `ITEM_ENCHANTMENT_DESIGN.md`를 우선한다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §후속 사용자 결정, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §상태·§5.1, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs]
- 런타임 마을 발전(인구 증감·Stage 승격·자동 건설)은 V8 후속으로 보류돼 있고 게이트는 비활성이다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §9]
- 마을 골드 밸런스: 마을 Gold 부족으로 NPC 전리품 매입이 거절돼 30일 뒤 NPC 가방이 약 90% 찬다. 가방 전리품(무작위 옵션 장비)은 세이브 장기 증가의 주원인이기도 하다. 다음 작업 후보로 남아 있다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §7·§11.3, Docs/npc/NPC_AMBITION_DESIGN.md D109] [추론: 다음 작업 우선순위는 사용자 결정 대기]
- 런타임 폐허 전환은 Fate·Pop·task·건설 중 Building·materialize 차단·clock을 함께 바꾸는 원자적 진입점으로 구현해야 한다. [src: Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §12]
