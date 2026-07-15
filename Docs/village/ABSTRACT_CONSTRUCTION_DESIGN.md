# 마을 추상 건설 시뮬레이션 설계

> **상태**: 현행 정본
>
> 상위 문서: [VILLAGE_RUNTIME_DESIGN.md](VILLAGE_RUNTIME_DESIGN.md)
>
> 관련 문서: [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md) · [VILLAGE_NPC_BOUNDARY_DESIGN.md](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md)
>
> **책임**: 원거리 마을의 자원·건설·Tier·벽을 시간에 따라 전진시키고, 실체화 전후에 같은 원장을 유지하는 계약을 정의한다.

---

## 1. 해결하려는 문제

인구만 원거리에서 성장하고 자원과 건설이 멈추면, 오래 방치한 마을도 첫 방문 때 빈 상태로 나타난다. 실체화 직후 큰 경제 인구를 본 BuildQueue가 그때부터 건설을 시작해 플레이어 앞에서 성장 과정이 몰린다.

건물 수를 인구에서 역산해 방문 순간 생성하는 방식도 사용하지 않는다. 그 방식은 자원 부족, 건설 순서, 남은 진행도 같은 마을별 이력을 잃는다.

따라서 원거리에서도 다음 상태를 실제 게임시간에 따라 전진시킨다.

- 자원과 carry
- 일반 건물의 선택·착공·진행·완료
- Tier 승격
- 벽 계획·진행·완료

목표는 방문할 때 “지금부터 건설하는 마을”이 아니라 “부재 중에도 성장해 온 마을”을 보여주는 것이다.

---

## 2. B-lite 결정

추상 층은 **무엇을 얼마나 지었는지**는 결정하지만, 일반 건물의 **타일 좌표**는 결정하지 않는다.

건물 배치는 두 문제로 나뉜다.

1. 종류·개수·진행도
2. 실제 타일 좌표

두 번째 문제는 로드된 청크의 walkable·occupied 격자를 읽어야 한다. 원거리 마을에는 이 격자가 없으므로 별도 추상 점유 모델을 만들지 않는 한 정확히 판정할 수 없다.

추상 점유 모델을 추가하지 않는 이유:

- 실제 Map과 별도 점유 상태를 동기화해야 한다.
- 플레이어·월드 변경으로 실체화 시 다시 충돌할 수 있다.
- GameObject와 Tilemap은 어차피 청크 로드 시 생성된다.
- 좌표를 미리 정해도 플레이어가 체감하는 이득이 작다.

따라서 일반 건물은 미할당 좌표로 저장하고 실체화할 때 `VillageTileFinder`로 배치한다. 벽은 Bounds에서 좌표를 결정할 수 있으므로 추상 층에서도 세그먼트 좌표를 유지한다.

---

## 3. 단일 원장과 공통 코어

`VillageData`가 실시간·추상·세이브가 공유하는 영속 원장이다.

| 상태 | 원장 |
|---|---|
| 자원 수량·Cap | `VillageData.Resources`, `ResourceCaps` |
| 생산 소수 이월 | `FoodCarry`, `WoodCarry`, `StoneCarry`, `IronCarry`, `GoldCarry` |
| 완공 일반 건물 | `PlacedObjects`, `PlacedObjectTypeIds` |
| 진행 중 일반/벽 task | `ActiveBuildTasks` |
| 벽 계획·완료 | `WallSegments`, `CompletedWallSegments` |
| Tier와 Bounds | `Stage`, Bounds 필드 |
| 추상 진행 시각 | `LastConstructionSimGameTime`, `LastConstructionTierCheckGameTime` |

ECS 컴포넌트와 Building Entity는 실체화된 상태의 미러다. 추상 경로가 ECS 컴포넌트를 생성하거나 원장으로 사용하지 않는다.

실시간과 추상 경로는 다음 데이터 전용 코어를 공유한다.

| 책임 | 공통 진입점 |
|---|---|
| 생산·Cap·carry | `VillageResourceSimulation` |
| 후보 점수 | `VillageNeedsEvaluator` |
| 비용·시간·진행 HP | `VillageConstructionRules` |
| task·완료 부작용·서비스·세트 | `VillageConstructionLedger` |
| Tier 판정·영속 변경 | `VillageProgressionRules` |
| 벽 계획·완료 | `VillageWallRules` |

두 경로가 같은 로직을 공유하는 이유는 materialize 순간 상태가 튀는 것을 막고, 동일 초기 상태의 결과를 직접 비교할 수 있게 하기 위해서다.

---

## 4. 완료 적용 계약

건물 완료는 다음 상태를 함께 변경한다.

- `PlacedObjectTypeIds`
- `PlacedObjects`
- `ResourceCaps`
- Campfire 호환 플래그 등 완료 부작용

이 처리는 `VillageConstructionLedger.ApplyCompletedBuild` 한 곳에서 수행한다.

호출 규칙:

- 실시간 건설이 실제 배치와 완성에 성공했을 때 한 번 호출한다.
- 추상 건설이 완료됐을 때 미할당 좌표로 한 번 호출한다.
- 이미 완공된 레코드를 materialize하며 월드에 복원할 때는 호출하지 않는다.

복원 중 다시 호출하면 건물 수와 Cap이 중복 증가하므로 금지한다.

---

## 5. 미할당과 PlacementPending

`VillageConstructionPosition.UnassignedTile`이 일반 건물의 미할당 좌표를 표현한다. 직접 `int.MinValue`를 비교하는 코드를 복제하지 않는다.

추상 착공 task:

- 좌표 미할당
- `BuildingEntityId = -1`
- `PlacementPending = true`

추상 완공 건물도 같은 방식으로 좌표를 미할당 상태로 보존한다.

실체화 시 빈 타일을 찾지 못하면 레코드를 삭제하거나 완료를 취소하지 않는다. `PlacementPending`으로 남겨 다음 materialize 또는 Tier Bounds 확장 뒤 다시 시도한다.

이유는 논리적 완료 상태와 월드 배치 상태를 구분하기 위해서다. 배치 실패만으로 완료 레코드를 버리면 건물 수·서비스·Cap과 실제 월드가 서로 다른 진실을 갖게 된다.

---

## 6. 시간 전진

건설은 자원이 모여야 착공하므로 인구처럼 단일 closed-form 식으로 계산할 수 없다. 고정 24시간 step도 사용하지 않는다. 2시간짜리 건물의 다음 착공이 하루씩 지연되고, 긴 방치 시간에는 불필요한 반복이 커지기 때문이다.

대신 다음 의미 있는 이벤트까지 전진한다.

- 진행 task 완료
- 1위 후보의 Wood/Stone 충족
- 정수 시간 생산 경계
- Tier 판정 경계
- 목표 시각 `now`

```text
while simulationClock < now and budget remains:
    next = earliest relevant event
    crossed whole-hour boundaries → shared resource production
    active task → advance progress and apply completion
    tier/wall state → evaluate with shared rules
    no active task → choose and reserve next build
    simulationClock = next
```

자원 충족 시각을 정확히 계산할 수 없으면 최대 다음 정수 시간 경계를 사용한다. task는 소수 시간만큼 진행할 수 있지만 생산은 실시간 규칙과 동일하게 통과한 정수 시간 경계에서만 적용한다.

전체 동시 task는 실시간과 추상 경로 모두 현재 1개다.

---

## 7. 시간 분할과 예산

`System_SettlementSim`은 원거리 마을을 라운드로빈으로 방문한다. 다음 예산을 분리한다.

- 한 tick에 방문할 마을 수
- 마을별 일반 건설 이벤트 수
- 마을별 벽 이벤트 수

마을 수 예산은 전체 정착지 수에 따른 프레임 비용을 제한한다. 일반/벽 이벤트 예산은 오래 방치한 단일 마을이나 많은 벽 세그먼트가 tick을 독점하지 않게 한다.

예산을 소진하면 마지막으로 확정한 clock과 task 진행도를 `VillageData`에 남긴다. 다음 slice는 같은 상태에서 이어서 처리한다.

materialize 직전 catch-up 결과가 `IsCaughtUp=false`이면 마을을 실체화하지 않는다. 다음 tick에서 이어서 완료한 후에만 `IsMaterialized`를 변경한다.

---

## 8. Tier와 벽

원거리에서 건물만 늘리고 Tier가 멈추면 충분히 성장한 마을도 첫 방문 전까지 Settlement로 남는다. 벽도 실체화 후에야 계획돼 오래된 마을이 빈 상태로 보인다.

따라서 Tier와 벽을 추상 시뮬레이션에 포함한다.

- `VillageProgressionRules`가 `VillageStageTable` 조건을 `VillageData`만으로 판정한다.
- 승격 시 Stage와 Bounds를 변경하고 필요한 벽 계획 플래그를 남긴다.
- `VillageWallRules`가 Bounds 기반 세그먼트를 만든다.
- 벽 task는 좌표를 유지하고 자원·시간·완료를 추상에서 진행한다.
- 실체화 시 실제 타일이 점유돼 있으면 현행 벽 정책에 따라 해당 세그먼트를 건너뛴다.

벽은 일반 건물과 같은 자원을 소비하지만 이벤트 예산은 분리한다.

---

## 9. materialize 순서

`HistoryManager.MaterializeSettlement`은 다음 순서를 지킨다.

1. 최신 `SettlementLiveState.Pop`을 경제 인구로 정량화한다.
2. 추상 자원·건설·Tier·벽을 현재 시각까지 catch-up한다.
3. catch-up이 끝났을 때만 materialized 상태로 전환한다.
4. `VillageData`에서 ECS와 월드 오브젝트를 복원한다.

인구를 먼저 갱신하는 이유는 이전 인구로 건설 후보와 Tier를 평가하지 않게 하기 위해서다.

### 완공 Entity 건물

1. `VillageTileFinder`로 좌표를 찾는다.
2. `MapManager.PlaceObject`로 월드에 배치한다.
3. `BuildingManager` 저장 레코드와 연결한다.
4. 기존 `PlacedObjectSaveData`에 좌표와 EntityId를 기록한다.
5. 완료 부작용은 다시 적용하지 않는다.

### 진행 중 Entity 건물

같은 snapshot에 좌표와 EntityId를 기록하고, `VillageConstructionRules.GetProgressHp`로 누적 시간과 HP를 맞춘다. 기존 좌표나 Building 레코드가 있으면 재사용해 중복 생성하지 않는다.

### Tile 건물과 벽

필요한 타일 리소스를 준비한 뒤 Map 수정 경로로 배치한다. Entity 복원 경로에 동기적으로 섞지 않는다.

---

## 10. fold-back 순서

`HistoryManager.FoldBackSettlement`은 ECS 상태를 `VillageData`에 flush한 뒤 실체화를 해제한다.

- task와 완공 건물을 원장에 반영한다.
- carry는 이미 `VillageData`에 있으므로 복사 원장을 만들지 않는다.
- `LastConstructionSimGameTime`과 `LastConstructionTierCheckGameTime`을 현재 시각에 맞춘다.

clock을 맞추는 이유는 실시간에서 이미 처리한 시간을 추상 경로가 다시 계산하지 않게 하기 위해서다.

---

## 11. New Game 초기 발전

절차적 역사는 New Game 전에 오랜 기간을 시뮬레이션하지만, 그 전체 시간을 건설 시간으로 재생하면 모든 마을이 비슷한 최고 단계에 수렴한다. 그래서 시간을 흘려 Stage를 순차 승격시키지 않고, **고정 인구로 `InitialStage`를 한 번 계산한 뒤 그 Stage까지의 누적 상태를 결정론적으로 생성**한다(VILLAGE_REWORK_PLAN.md §3.2·§3.3·§3.5).

`HistoryManager.BootstrapNewGameVillageDevelopment`가 마을마다 다음을 수행한다.

1. `VillageManager.QuantizeAbstractPopulation`으로 고정 경제 인구를 반영한다(생산 계산용, 1~25).
2. `VillageProgressionRules.CalculateInitialStage(원본 역사 인구, Profile.MaxStage)`로 초기 Stage를 확정한다.
3. `VillageDevelopmentBootstrap.ApplyFixedInitialState`로 Stage·Bounds·construction clock을 **직접 대입**한다(synthetic 시간 진행 없음 → 진행 task 미생성).
4. Hamlet 이상 + 기능성 벽 레이아웃이면 `VillageWallRules.ApplyInitialCompletedWalls`로 둘레 벽을 완공 상태로 생성한다.
5. `VillageInitialDevelopment.ApplyInitialMilestones`로 Settlement→InitialStage 누적 마일스톤(시설·`UpgradeFacility` Tier·`UnlockShopTier`·`ApplyStageCaps`)을 자원·건설시간·task 없이 완공 원장으로 1회 적용한다.

원칙:

- 이번 실행에서 New Game 연대기를 생성한 경우(`generatedNewGameHistory`)에만 실행한다.
- `Alive`와 `Conquered`만 대상으로 한다. `Ruined`와 `Merged`는 제외한다.
- 같은 map seed·Profile·고정 인구에서 같은 결과가 나온다(임계값·명세가 결정론).
- 마을별 `InitialDevelopmentApplied=true`로 1회 적용을 확정하고, 세이브 로드나 재실체화에서 재적용하지 않는다(중복 방지 정본은 `VillageData.AppliedGrowthMilestones`).

완료 부작용은 §4의 `ApplyCompletedBuild` 단일 계약을 그대로 쓴다. 시간 경과에 따른 일반 자동 건설·Tier 승격은 `RuntimeProgressionEnabled=false`로 중단돼 있다(런타임 발전 재개는 후속 Phase). 이전 방식인 시간 압축 bootstrap(`VillageDevelopmentBootstrap.CreatePlans`/`AdvanceBootstrapSlice`, 공통 자원·건설·완료·Tier 코어 실행)은 parity·진단 재현용으로만 보존한다.

---

## 12. Fate와 폐허 전환

운영 가능 Fate는 `Alive`와 `Conquered`다. `Ruined`와 `Merged`는 정상 생산·건설·Tier 진행과 materialize 대상에서 제외한다.

현재 `IsOperational=false`인 추상 경로는 clock을 현재 시각에 맞춰 backlog만 막는다. 런타임에서 살아 있는 마을을 폐허로 바꾸는 완전한 원자적 진입점은 후속 작업이다.

그 진입점은 최소한 다음 상태를 함께 변경해야 한다.

1. `SettlementLiveState.Fate`와 Pop
2. Ruin 연대기 이벤트
3. 진행 task 제거
4. 건설 중 Building 제거
5. 정상 materialize 차단
6. 추상 clock 정렬

예약 자원은 마을 붕괴로 유실된 것으로 취급해 환불하지 않는다. 연대기 이벤트만 추가하거나 Pop만 0으로 만드는 부분 전환은 허용하지 않는다.

---

## 13. 검증 계약

`VillageSimulationParity.Compare`(실시간/추상 parity)는 다음을 비교한다.

- 시간 경계 참조 실행과 이벤트 fast-forward
- 무제한 실행과 낮은 예산 slice 재개
- 자원·Cap·carry
- 완공 순서와 진행 task
- 벽과 Stage
- 두 construction clock

`VillageSimulationParity.CompareFixedState`(V7)는 초기 완성형 마을의 정체성이 save/load 왕복·동결(무발전) advance·fold-back 반복 전후에 불변인지 별도로 비교한다: 고정 인구·`VillageProfileId`·Stage·`ShopTier`·`AppliedGrowthMilestones`(순서 무관 멀티셋)·시설 (TableId, FacilityTier) 멀티셋·`MerchantStock` 스냅샷. 좌표·자원 amount는 비교하지 않는다(그건 `Compare`의 몫). 마일스톤 재적용이 신규 0이고 fold-back 반복 후에도 중복이 없음을 함께 확인한다.

materialize 검증은 다음 불변식을 확인한다.

- 기존 완공 레코드 복원 전후 완료 개수와 Cap이 변하지 않는다.
- 성공한 배치에는 미할당 좌표가 남지 않는다.
- 실패한 배치는 `PlacementPending`으로 보존된다.
- 기존 좌표와 BuildingEntityId를 가진 레코드를 중복 생성하지 않는다.

남은 수동 통합 검증:

- 실제 청크 활성화와 Addressables 스폰
- PlacementPending 강제 실패 후 재시도
- materialize/fold-back 반복
- 진행 task와 완공 레코드의 세이브 왕복
- 런타임 폐허 전환 진입점 구현 후 Fate별 경로
