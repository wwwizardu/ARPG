# 마을 자가 건설 우선순위 설계

> **상태**: 현행 정본
>
> 상위 문서: [VILLAGE_RUNTIME_DESIGN.md](VILLAGE_RUNTIME_DESIGN.md)
>
> **책임**: 마을이 다음에 지을 일반 건물을 고르는 점수와, 자원 부족·배치 실패 시의 처리 정책을 정의한다. 자원·건설 진행·materialize 계약은 [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md)가 소유한다.

---

## 1. 결정

일반 건설 후보는 `VillageNeedsEvaluator.GetRankedCandidates(VillageData)`가 매 착공 시점에 즉시 평가한다. 별도 로드맵이나 캐시는 사용하지 않는다.

이유:

- 고정 시퀀스와 필요도 점수를 함께 사용하면 어느 쪽이 우선인지 불명확해진다.
- 인구·자원·완공 건물·진행 task가 계속 변하므로 캐시는 쉽게 낡는다.
- 실시간과 추상 경로가 같은 데이터 전용 평가기를 호출해야 두 경로의 성장 결과를 비교할 수 있다.

`System_VillageNeedsEvaluation`은 존재하지 않는다. `System_VillageBuildQueue`와 추상 건설 코어가 필요할 때 evaluator를 직접 호출한다.

---

## 2. 후보 풀과 제외 조건

현재 일반 후보는 BuildableItem TableId 100 이상 200 미만에서 조회한다. 다음 후보는 제외한다.

- 테이블에 없는 항목
- `SpawnType=Tile`인 항목
- Palisade와 Gate처럼 벽 규칙이 소유하는 항목
- 완공 수 + 진행 중 수가 `MaxPerVillage`에 도달한 항목
- 최종 점수가 0 이하인 항목

완공 건물과 진행 task를 함께 세는 이유는 같은 건물을 건설 중인데 다시 선택하는 중복 착공을 막기 위해서다.

일반 건물의 의미는 `ProvidedService`, `SetMembership`, `BaseWeight`, `MaxPerVillage`, `StorageCap_*` 데이터로 판정한다. Campfire와 벽 ID는 마을 존재와 벽 도메인을 정의하는 예외이며 각각 `VillageNeedsEvaluator`와 `VillageWallRules` 한 곳에서만 소유한다.

---

## 3. 점수 모델

```text
TotalScore =
    existence
  + housingDeficit
  + foodCapacityDeficit
  + baseWeight
  + tierGate
  + setCompletion
  + resourceSurplus
  - diminishing
```

### 3.1 존재

Campfire가 완공 또는 건설 중이 아니면 `+5000`을 부여한다.

Campfire는 마을의 최소 시각 정체성이므로 다른 성장 후보보다 먼저 확보한다. `MaxPerVillage`와 진행 task 카운트가 중복 건설을 막는다.

### 3.2 주거 결핍

후보에 `ProvidedService.Housing`이 있으면 다음 점수를 더한다.

```text
max(0, Population - HousingCount) × 1000
```

경제 인구보다 주거가 부족한 동안에만 큰 가중치를 주므로, 필요한 수를 채운 뒤 같은 주거를 무한히 짓지 않는다.

### 3.3 식량 저장 Cap 결핍

`StorageCap_Food > 0`인 후보에는 다음 점수를 더한다.

```text
targetFoodCap = max(Population × 5, nextStage.PromoMinFood)
max(0, targetFoodCap - currentFoodCap) × 50
```

현재 Food 양이 아니라 **저장 상한 부족**을 평가한다. 현재량은 시간이 해결하지만 Cap 부족은 건물을 지어야만 해결되기 때문이다.

### 3.4 기본 성장과 Tier 게이트

- `BuildableItemTable.BaseWeight`
- 후보가 다음 Stage의 미충족 건물 조건을 채우면 `+400`

Tier 게이트 대상은 Food Cap, Housing, 필수 ObjectSet, Civic, Shop이다. 현재 Food 양·인구·최소 나이는 건물 하나로 직접 해결할 수 없으므로 후보 가산에 사용하지 않는다.

승격 조건은 `VillageStageTable`을 `TierGapDetector`와 `VillageProgressionRules`가 함께 읽는다. 점수 평가기가 승격 규칙을 별도로 복제하지 않는다.

### 3.5 세트 완성

후보의 `SetMembership`을 기존 마을의 세트 비트와 합쳤을 때 새로 완성되는 `ObjectSetCatalog` 항목마다 `+80`을 부여한다.

부분 시설을 방치하지 않고 나머지 구성요소를 자연스럽게 이어 짓게 하기 위한 가산이다.

### 3.6 자원 잉여

- Wood가 후보 비용의 3배 이상이면 `+5`
- Stone이 후보 비용의 3배 이상이면 `+5`

큰 점수 계층을 뒤집지 않으면서 저장고에 묶인 자원을 소비하도록 주는 약한 가산이다.

### 3.7 중복 감쇠

```text
(완공 수 + 진행 중 수) × 20 차감
```

같은 TableId가 기본 가중치만으로 계속 1위를 차지하는 것을 막는다. `MaxPerVillage`는 hard cap, 감쇠는 soft cap 역할을 한다.

### 3.8 방어 후보

벽은 일반 후보 점수에 포함하지 않는다. 현재 정책은 다음과 같다.

1. 미완성 Gate가 있으면 절대 우선한다.
2. 일반 후보를 점수순으로 시도한다.
3. 일반 후보가 없거나 모두 물리적으로 배치 불가능하면 Palisade를 진행한다.

`ThreatLevel` 기반 벽 점수는 현재 구현되어 있지 않다. 위협도에 따른 방어 우선순위가 필요해질 때 별도 결정으로 추가한다.

---

## 4. 자원 부족과 fallback

점수 1위 후보가 자원 부족이면 낮은 후보로 우회하지 않고 기다린다.

```text
Started               → 착공 종료
WaitForResources      → 전체 시도 종료, 다음 tick까지 대기
NoTileOrTableMissing  → 다음 순위 후보 시도
```

이유:

- 자원 부족은 생산 시간이 해결하는 일시적 상태다.
- 일시적 부족 때문에 싼 건물로 우회하면 점수가 표현한 마을의 필요를 무시하게 된다.
- 테이블 누락이나 배치 공간 부족은 같은 tick에서 기다려도 해결되지 않으므로 다음 후보를 시도한다.

실체화 경로는 타일을 찾을 수 있으므로 `NoTileOrTableMissing`을 판정한다. 추상 경로는 좌표를 정하지 않으며, 자원이 준비된 1위 후보를 미할당 task로 시작한다. 좌표 배정 실패는 materialize 시 `PlacementPending`으로 보존한다.

---

## 5. 착공과 진행 계약

- 실시간·추상 경로의 전체 동시 task 한도는 현재 마을당 1개다.
- 비용과 건설 시간은 `VillageConstructionRules`를 공유한다.
- 일반 건물의 `BuildHours`가 0이면 2시간을 사용한다.
- Palisade와 Gate는 `VillageWallRules.PalisadeBuildHours`를 사용한다.
- NPC를 배정하지 않으며 게임시간 경과로 진행한다.
- 실체화된 Entity 건물은 HP와 진행도를 양방향 동기화한다. 건설 중 받은 피해는 진행도를 후퇴시킨다.

동시 task를 1개로 제한하는 이유는 큰 추상 인구가 실체화될 때 여러 건물을 한꺼번에 착공하는 버스트를 막고 자원 소비 순서를 결정적으로 유지하기 위해서다.

---

## 6. 구현 진입점

| 책임 | 진입점 |
|---|---|
| 후보 생성·점수 | `VillageNeedsEvaluator.GetRankedCandidates` |
| Tier 건물 결핍 | `TierGapDetector.DoesCandidateFillGap` |
| 실체화 착공·진행·완료 | `System_VillageBuildQueue` |
| 추상 착공·진행·완료 | `VillageAbstractConstruction` |
| 비용·시간·진행 HP | `VillageConstructionRules` |
| 완료 수·서비스·세트·task 원장 | `VillageConstructionLedger` |
| 벽 계획·완료 | `VillageWallRules`, `WallSegmentRegistry` |

---

## 7. 검증 기준

- Campfire가 없으면 첫 일반 후보가 Campfire다.
- 완공 및 진행 중 항목을 합쳐 `MaxPerVillage`와 감쇠를 계산한다.
- 경제 인구보다 Housing이 부족하면 Housing 후보가 우선된다.
- 다음 Stage의 Food Cap을 수용하지 못하면 `StorageCap_Food` 후보가 우선된다.
- 1위 후보의 자원이 부족하면 다른 일반 후보를 착공하지 않는다.
- 1위 후보가 실체화 마을에서 배치 불가능하면 다음 후보를 시도한다.
- Gate는 일반 후보보다 먼저, Palisade는 일반 후보 뒤에 처리한다.
- 실시간과 추상 실행이 같은 초기 상태·시간에서 자원, 완공 순서, task, Stage 결과를 일치시킨다.
