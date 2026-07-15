# 마을 런타임 설계

> **상태**: 현행 정본
>
> 이 문서는 마을 런타임의 책임 경계와 핵심 결정을 정의한다. 실제 동작은 연결된 코드가 최종 기준이며, 세부 알고리즘과 이유는 담당 설계 문서가 소유한다.
>
> 세부 정본: [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md) · [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md) · [VILLAGE_NPC_BOUNDARY_DESIGN.md](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md)

---

## 1. 시스템 모델

마을은 **추상 인구를 가진 경제 단위이자 서비스 거점**이다. 물리 NPC의 생존·이동·직업과 마을 경제를 직접 연결하지 않는다.

| 영역 | 소유자 | 이유 |
|---|---|---|
| 자원·Cap·carry·완공 건물·진행 task·벽·Stage | `VillageData` | 청크가 꺼져도 계속 존재해야 하며 세이브와 추상 시뮬레이션의 입력이기 때문 |
| 실체화된 마을의 런타임 접근 | ECS 컴포넌트 | 활성 청크에서 빠르게 조회하고 화면·입력 시스템과 연결하기 위해 |
| 추상 인구와 정착지 Fate | `HistoryManager`의 `SettlementLiveState` | 월드 시간과 정착지 생사를 청크 로드 여부와 독립적으로 진행하기 위해 |
| 물리 NPC | NPC 시뮬레이션 계층 | 모험가의 활동·전투·관계가 마을 경제를 흔들지 않게 분리하기 위해 |

`VillageData`가 영속 원장이고 ECS 컴포넌트는 실체화된 마을의 미러다. 같은 값을 두 계층이 독립적으로 소유하지 않는다.

---

## 2. 실시간과 추상 LOD

마을은 두 실행 경로를 사용한다.

- **실체화 경로**: 가까운 마을을 `System_Village*`가 처리하고 실제 타일·Entity를 만든다.
- **추상 경로**: 먼 마을을 `System_SettlementSim`과 `VillageAbstractConstructionScheduler`가 시간 분할 처리한다.

두 경로는 생산, 건설 후보, 비용과 시간, 완료 부작용, Tier, 벽 규칙을 공통 데이터 전용 코어로 공유한다. 서로 다른 규칙을 사용하면 materialize 순간 자원·건물·Stage가 튀므로 별도 구현을 두지 않는다.

일반 건물의 좌표는 로드된 지도 격자가 필요하므로 추상 층에서 정하지 않는다. 추상 층은 종류·진행도·완료 여부만 확정하고, 실체화할 때 좌표를 배정한다. 자세한 이유와 복원 계약은 [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md)를 따른다.

---

## 3. 경제 인구와 물리 NPC

`VillageData.Population`은 `SettlementLiveState.Pop`을 `VillageManager.QuantizeAbstractPopulation`으로 변환한 경제용 값이다. 물리 NPC 수가 아니다.

`SettlementLiveState.Pop`은 절차적 역사 완료 시점에 **고정**된다. 일반 런타임 시간 경과·실체화 전환·물리 NPC 사망·보충·Food/자원·건물 파괴로 다시 전진하지 않으며(`SettlementPopulationModel.Advance`를 런타임 advance/materialize catch-up에서 호출하지 않는다), 인구 갱신 중단 시각(`LastSimGameTime`)은 현재 시각에 맞춰 후속 재활성화 시 backlog가 쌓이지 않게 한다. 로지스틱 인구 모델·Biome 성장률 데이터는 삭제하지 않고 후속 인구 설계용으로 보존한다. [VILLAGE_REWORK_PLAN.md](VILLAGE_REWORK_PLAN.md) §3.1 참조.

이 분리를 유지하는 이유는 다음과 같다.

- 모험가의 이동·전투·사망이 생산량과 Tier를 우연히 변경하지 않게 한다.
- 물리 NPC가 없는 원거리 마을도 같은 경제 규칙으로 전진할 수 있다.
- NPC 행동 시뮬레이션과 마을 밸런스를 독립적으로 조정할 수 있다.

`System_SettlementSim`이 Priority 50에서 경제 인구를 동기화한 뒤 생산 52, Tier 60, 건설 66이 실행된다. 물리 NPC 목록은 관계·활동·표시용이며 경제 계산의 입력으로 사용하지 않는다. 자세한 경계는 [VILLAGE_NPC_BOUNDARY_DESIGN.md](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md)를 따른다.

---

## 4. 자원과 자가 건설

자원 생산은 `VillageResourceSimulation`이 담당한다. 추상 인구를 제한된 생산 인구로 압축하고 Food/Wood/Stone/Iron/Gold를 시간 경계 단위로 계산한다. carry는 `VillageData`에만 저장하며 `VillageStorageComponent`는 표시용 미러다.

건물과 벽은 NPC 배정 없이 게임시간으로 진행한다. 자원 비용은 건설 결정을 제한하는 경제 게이트이므로 유지한다. 실시간과 추상 경로 모두 진행 중 task를 `VillageData.ActiveBuildTasks`에 즉시 반영하며, 전체 동시 task는 현재 1개로 제한한다.

동시 task를 1개로 제한하는 이유는 추상 인구가 커진 마을이 materialize 직후 대량 착공하는 것을 막고, 자원 소비와 화면 변화를 읽을 수 있는 속도로 유지하기 위해서다.

단, **초기 발전은 게임시간 건설로 재생하지 않는다.** 생성 시 `InitialStage`까지의 누적 시설·상점·Cap을 자원·건설시간·진행 task 없이 완공 상태로 기록하고(`VillageInitialDevelopment.ApplyInitialMilestones`, `VillageGrowthPlanCatalog`), 시간 경과에 따른 일반 자동 건설 후보 평가는 `RuntimeProgressionEnabled=false`로 중단돼 있다. `VillageData.ActiveBuildTasks`와 자원 소비는 파괴 시설 수리·명시적 이벤트에만 쓰인다. 초기 완공 시설은 좌표 미할당(`PlacementPending`) 레코드로 저장돼 첫 materialize에서 배치된다.

다음 건물의 선택과 자원 부족 정책은 [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md)를 따르되, 위 후보 평가는 런타임 발전이 재개될 때만 실행된다.

---

## 5. 건물 데이터와 완료 계약

건물의 서비스·카테고리·세트·가중치·마을별 상한·저장 Cap은 `BuildableItemTable` 데이터로 표현한다. 새 건물의 의미를 여러 시스템의 TableId 분기로 복제하지 않는다. Campfire와 벽처럼 도메인 자체를 정의하는 고정 식별자가 필요하면 전용 규칙 클래스 한 곳에만 둔다.

`SpawnType`에 따라 배치 경로를 나눈다.

- **Entity**: 개별 HP·상호작용·저장 상태가 필요한 건물. 착공 시 Entity를 만들고 진행도와 HP를 동기화한다.
- **Tile**: 반복되는 정적 구조물. 완료 시 타일 리소스를 준비해 Map에 배치한다.

완료 부작용은 `VillageConstructionLedger.ApplyCompletedBuild`에서 한 번만 적용한다. 완공 레코드를 materialize하면서 월드에 복원할 때 다시 호출하지 않는다. 배치 공간이 없으면 레코드를 버리지 않고 `PlacementPending`으로 보존한다.

---

## 6. Tier와 벽

Stage는 절차적 역사 완료 인구로 **생성 시 한 번 확정하는 `InitialStage`**다. `VillageProgressionRules.CalculateInitialStage`가 원본 역사 인구와 `VillageProfile.MaxStage`만으로 판정하며, 임계값(597/1,679/2,592/3,419)은 코드 상수다. `VillageStageTable`의 주거·식량·필수 세트·Civic·Shop 승격 게이트는 **런타임 승격 판정에 사용하지 않는다** — `VillageProgressionRules.RuntimeProgressionEnabled=false`로 시간 경과에 따른 Stage 승격·강등을 중단했다. (게이트를 되살리는 런타임 발전은 후속 Phase의 별도 승인 대상이다. [VILLAGE_REWORK_PLAN.md](VILLAGE_REWORK_PLAN.md) §3.2·§3.4 참조.)

벽은 Hamlet 이상 초기 Stage에서 Bounds 둘레 완공 Palisade/Gate 세그먼트로 **생성 시 확정**한다(`VillageWallRules.ApplyInitialCompletedWalls`, 자원·건설시간·task 없음). Gate 개폐·HP 피해 경로는 정본화돼 있고, 타일 배치는 첫 materialize(`RestoreWallTilesFromData`)가 담당한다. 기능 없는 청사진 경계는 로드 시 제거해 초기 완공 벽과 중복되지 않게 한다. 런타임 벽 증축·추상 벽 계획은 위 게이트로 함께 중단돼 있다.

---

## 7. 서비스와 오브젝트 세트

Shop, Forge, Inn, Shrine 서비스는 NPC가 아니라 배치된 건물의 `ProvidedService`로 열린다. `System_VillageServiceProximity`가 플레이어 주변 서비스를 집계한다.

복합 기능은 큰 건물 타입 하나가 아니라 작은 오브젝트의 `SetMembership` 조합으로 표현한다. 이 방식은 일부 시설만 완성되거나 파괴된 상태를 자연스럽게 나타내고, 새 조합을 데이터와 `ObjectSetCatalog`에 국소적으로 추가할 수 있게 한다.

---

## 8. 정본 경계

| 질문 | 정본 |
|---|---|
| 전체 책임과 데이터 흐름 | 이 문서 |
| 실시간/추상 정합성, materialize, 초기 발전 | [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md) |
| 건설 후보 점수와 대기/fallback | [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md) |
| 경제 인구와 물리 NPC의 경계 | [VILLAGE_NPC_BOUNDARY_DESIGN.md](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) |
| 장기 Stage 콘텐츠 목표 | [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md) |
| 시스템 우선순위 대역 | [AGENTS.md](../../AGENTS.md) |

`VILLAGE_GROWTH_STAGES.md`는 미래 콘텐츠 목표를 다루며 현재 런타임 계약을 재정의하지 않는다. 문서와 코드가 충돌하면 구현을 임의로 문서에 맞추지 말고 차이를 먼저 확인한다.
