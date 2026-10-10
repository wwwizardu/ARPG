# 마을 성장·지역 차별화 개편 작업 계획

> **상태 (2026-07-15): 기획 합의안 기반 구현 계획 — V0·V1 완료, V2·V3·V4·V5·V6 구현·정적 검증 완료 (고정 인구·초기 Stage 확정 + Stage 누적 시설·상점 Tier·Cap 마일스톤 1회 적용 + Dwarf 슬롯 배치·기능성 벽·상호작용 프롬프트·제련소 Tier + TradeProfile 지역 상권·결정론 재고·가격 분리 + Race별 상권 세분화·Layout variant·역할 결합 초안. 특수 시설은 사용자 결정에 따라 Dwarf만 확정. V7 통합 검증(파리티 `CompareFixedState`·New Game 완성형 정적 검증 17/17) 완료 — 구 세이브 마이그레이션은 세이브 신규 시작 결정으로 제외. 시각·상호작용·상점 UI 실측과 비교 스크린샷은 플레이 모드 필요, 현행 정본/Wiki 갱신은 플레이 모드 성공 후 대기)**
>
> **핵심 결정**: 절차적 역사 생성이 끝난 시점의 정착지 인구를 고정 스냅샷으로 사용한다. 이 인구로 초기 Stage를 확정하고, 해당 Stage까지의 공통 시설·지역 특수 시설·상점·배치 구조를 누적 적용해 이미 발전한 마을로 등장시킨다. 신규 마을의 모든 일반·특수 건물은 플레이어가 직접 상호작용할 수 있어야 하며, 기능 없는 건물형 장식·외형 모듈은 생성하지 않는다. 런타임에서는 인구 자연 증가·감소, Stage 승격, 자동 시설 추가와 배치 확장을 진행하지 않는다. 런타임 마을 발전은 1차 개편의 재미 검증이 끝난 뒤 별도 후속 Phase에서 설계한다.
>
> **현재 런타임 정본**: 구현이 완료되기 전까지 [VILLAGE_RUNTIME_DESIGN.md](VILLAGE_RUNTIME_DESIGN.md), [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md), [BUILD_PRIORITY_DESIGN.md](BUILD_PRIORITY_DESIGN.md), [VILLAGE_NPC_BOUNDARY_DESIGN.md](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md)가 현행 동작의 정본이다. 이 문서는 그 정본을 변경하기 위한 작업 순서이지 현재 구현 계약이 아니다.
>
> **후속 사용자 결정 (2026-08-30, 마법 부여 접근성)**: V6의 Dwarf 전용 Forge 접근성 계약을 후속 설계에서 폐기한다. [ITEM_ENCHANTMENT_DESIGN.md](../combat/ITEM_ENCHANTMENT_DESIGN.md) MVP는 신규 게임의 모든 Race·Profile·초기 Stage 마을에 Forge 160을 최소 `FacilityTier=1`로 멱등 보장한다. 기존 누적 성장 결과가 Tier 2/3이면 높은 값을 유지하며, 구 세이브 전체 backfill은 범위에서 제외한다. 공통 보장은 `HistoryManager.EnsureNewGameMinimumForges`와 `VillageInitialDevelopment.EnsureMinimumForge`에 구현돼 있다. 아래 V6의 Dwarf 전용 설명은 공통 보장 구현 전 이력이다.
>
> **시작 마을 기본 상점 (2026-10-10 구현)**: 신규 게임은 공통 Forge 보장 뒤 연대기 미연결 BaseTown에 MerchantStall 151과 `ShopTier`를 최소 1로 보장한다(`EnsureNewGameBaseTownShops` → `EnsureMinimumShop`). 기존 상점은 중복 생성하지 않고 상위 Tier를 유지한다. 성장 계획 전체를 적용하거나 기존 세이브를 일괄 보정하지 않는다. 실제 배치는 기존 `RetryPendingPlacements`를 사용한다. 계약은 [VILLAGE_RUNTIME_DESIGN.md](VILLAGE_RUNTIME_DESIGN.md) §4와 [ABSTRACT_CONSTRUCTION_DESIGN.md](ABSTRACT_CONSTRUCTION_DESIGN.md) §11, 검증 결과는 [GOLD_BALANCE_DESIGN.md](../economy/GOLD_BALANCE_DESIGN.md) §11.3을 따른다.
>
> **관련 콘텐츠 방향**: [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md) · [MAP_DESIGN.md](../map/MAP_DESIGN.md)

---

## 1. 목표

1. 물리 NPC를 생산자·건설 작업자로 다시 연결하지 않고 마을의 자율 성장을 유지한다.
2. 절차적 역사 완료 시점의 인구를 고정하고, 그 값으로 게임 시작 시점의 Stage를 확정한다.
3. 확정 Stage까지의 필수·특수 시설과 상점 상태를 생성 단계에서 완성해 첫 방문부터 발전 수준이 일치하게 한다.
4. 모든 마을이 같은 일반 건물을 반복 건설하는 구조를 제거한다.
5. Stage마다 공통 시설, 특수 시설, 상점 품질, 배치 구조 중 하나 이상이 명확히 달라지게 한다.
6. 신규 마을에 생성되는 모든 건물은 플레이어가 직접 상호작용할 수 있어야 하며, 기능 없는 건물형 외형 모듈은 배치하지 않는다.
7. Race·Biome·정착지 역할을 마을 성장 계획과 상권에 연결한다.
8. New Game 생성, 첫 materialize, 세이브/로드가 같은 고정 인구·Stage·시설 결과를 유지한다.
9. 현행 로지스틱 인구 모델은 삭제하지 않고 후속 인구 설계를 위해 보존하되 현재 런타임에서는 사용하지 않는다.

### 1.1 비목표

- 물리 NPC 수를 마을 경제 인구로 사용하는 것
- NPC 직업·Working 상태를 생산 또는 건설 입력으로 복구하는 것
- 런타임 자연 출생·사망·이주·전쟁·식량에 의한 인구 변동을 이번 개편에 포함하는 것
- 런타임 시간 경과에 따른 Stage 승격·자동 시설 추가·마을 영역 확장을 이번 개편에 포함하는 것
- 모든 특수 건물의 최종 콘텐츠를 이번 기반 작업에서 한 번에 구현하는 것
- 침공·폐허·복구 전체 루프를 이 계획에 포함하는 것
- City 콘텐츠를 이름만 추가해 완성 처리하는 것

---

## 2. 현재 구조와 개편 이유

### 2.1 유지할 기반

| 기반 | 현재 소유자 | 개편 후 처리 |
|---|---|---|
| 자원·Stage·건설·완공 건물 영속 원장 | `VillageData` | 유지 |
| 정착지 인구 | 절차적 역사 결과 → `SettlementLiveState.Pop` → `VillageData.Population` | 역사 완료 값으로 고정, 초기 Stage 입력으로 사용 |
| 실시간/추상 건설 공통 코어 | `VillageConstructionRules`, `VillageConstructionLedger` | 초기 완공 원장 복원과 허용된 수리·이벤트에 한해 유지·확장 |
| 원거리 catch-up | `VillageAbstractConstructionScheduler` | 초기 발전용 자동 건설에는 사용하지 않음. 수리·명시적 이벤트 범위만 유지 |
| 서비스 귀속 | 건물의 `ProvidedService` | 유지 |
| materialize/fold-back | `HistoryManager`, `VillageManager` | 유지 |
| 완료 부작용 단일 적용 | `VillageConstructionLedger.ApplyCompletedBuild` | 유지 |

### 2.2 교체할 구조

| 현재 구조 | 문제 | 목표 구조 |
|---|---|---|
| ID 100~199 전체를 일반 건설 후보로 평가 | 모든 마을이 비슷한 건물 목록으로 수렴 | 마을 성장 계획이 허용한 보상·선택 시설만 후보화 |
| 기능 건물 주변에 비상호작용 건물·외형 모듈 배치 | 상호작용 대상을 구분하기 어렵고 마을이 번잡해짐 | `1건물 = 1상호작용 기능` 원칙, 건물형 장식은 신규 생성에서 제외 |
| Housing·Food·RequiredSet·Civic·Shop 승격 게이트 | 기능이 약한 건물을 승격용으로 강제 | 역사 종료 인구로 초기 Stage를 직접 확정하고 런타임 승격은 보류 |
| Wood/Stone 부족 시 1위 후보 무기한 대기 | 초기 발전 결과가 자원 RNG에 막힘 | 초기 Stage 누적 시설은 생성 시 완공 처리, 런타임 수리 등만 자원 사용 |
| 런타임 로지스틱 인구 tick | 장기 플레이 시 같은 바이옴의 마을이 수용력에 수렴 | 절차적 역사 완료 인구를 고정하고 후속 설계 전까지 tick 중단 |
| Stage마다 큰 Bounds로 일괄 확장 | 적은 1×1 오브젝트가 넓게 분산 | 지정 슬롯을 먼저 채우고 필요할 때만 방향성 확장 |
| Race별 청사진 선택 후 Village 계층에서 정체성 소실 | 특수 건물과 상점에 Race/Biome 전달 불가 | 저장 가능한 `VillageProfileId`로 성장·배치·상권 연결 |
| 모든 상점이 같은 전역 Item 풀과 BasePrice 사용 | 마을 간 방문·거래 이유 부족 | 지역 재고·가격·특수 시설 해금을 가진 TradeProfile |

### 2.3 제거하거나 용도를 바꿀 옛 전제

- `AssociatedJobType`은 마을 생산·건설에 사용하지 않는다.
- `JobBonusTable`을 현행 마을 경제에 재연결하지 않는다.
- Bed/Bedroll 수를 추상 인구와 1:1로 맞추지 않는다.
- Woodpile·Stockpile·CropPlot 반복 건설로 Stage를 열지 않는다.
- `ProvidedService`는 실제 상호작용·게임 기능을 표현하는 데 집중한다. 순수 성장 분류는 별도 Facility/Growth 역할 데이터로 표현한다.

---

## 3. 목표 성장 모델

### 3.1 런타임 인구 고정 정책

절차적 역사 생성이 완료되면 `Chronicle.Settlements[].Population`을 각 정착지의 런타임 인구 정본으로 확정한다.

- `WorldSimState.Settlements[].Pop`은 역사 결과로 1회 초기화한다.
- 게임시간 경과만으로 `SettlementPopulationModel.Advance`를 호출하지 않는다.
- 실체화 전 catch-up에서도 인구를 전진시키지 않는다.
- `VillageData.Population`은 고정된 raw 인구를 정량화한 값으로 유지한다.
- 물리 모험가의 스폰·사망·보충은 고정 경제 인구를 변경하지 않는다.
- Food 부족, 건물 파괴, 자원량도 고정 인구를 변경하지 않는다.
- `LastSimGameTime`은 현재 시각에 맞춰 두어, 후속 설계에서 인구 tick을 다시 켤 때 과거 전체가 backlog로 적용되지 않게 한다.

현 `SettlementPopulationModel`과 Biome별 성장률·수용력 데이터는 삭제하지 않는다. 자연 증가·감소, 사건 기반 손실, 식량, 이주를 어떤 모델로 연결할지는 별도 후속 설계에서 결정한다.

`SettlementId < 0`인 수제작 BaseTown도 예외로 물리 NPC 수를 사용하지 않는다. Profile 기본 인구 또는 New Game 초기화 시 확정한 경제 인구를 저장하고 이후 고정한다.

### 3.2 초기 Stage 확정

고정 인구는 런타임 성장량이나 미래 목표가 아니라 게임 시작 시 마을이 이미 달성한 `InitialStage`를 정한다.

```text
InitialStage = 고정 경제 인구가 충족하는 최고 Stage
               (단, VillageProfile의 MaxStage 이하)
```

절차적 역사와 마을 초기화 사이의 흐름은 다음과 같다.

```text
역사 종료 인구 고정
    → InitialStage 계산
    → Settlement부터 InitialStage까지의 누적 보상 산출
    → 시설·상점·Cap·배치 상태를 완공 상태로 기록
    → 첫 materialize에서 완성된 마을 복원
```

초기 Stage 판정에서 사용하지 않을 항목:

- `PromoMinHousing`
- `PromoMinFood`
- `PromoRequiredSet`
- `PromoRequiredCivic`
- `PromoRequiredShop`

초기 Stage 판정에 사용할 항목:

- `PromoMinPopulation`
- `SettlementFate` 운영 가능 여부
- `VillageProfile.MaxStage`

`TargetStage`, `LastPromotionGameTime`, 승격 간격과 런타임 승격 판정은 현재 범위에서 구현하지 않는다. 후속 런타임 발전 Phase가 시작될 때 재미 목표와 함께 다시 설계한다.

### 3.3 Stage 초안

현재 경제 인구 상한 25를 유지한다는 전제의 시작 수치다. 구현 전 시뮬레이션 덤프로 분포를 확인한 뒤 확정한다.

| Stage | 최소 인구 초안 | 게임 시작 시 포함할 기본 시설·상태 |
|---|---:|---|
| Settlement | 1 | 마을 중심 거점, 기본 시장 |
| Hamlet | 3 | Settlement 누적 + 상호작용 가능한 휴식소/여관, 중심부 시설 간격 축소 |
| Village | 8 | Hamlet 누적 + 주 특수 시설 Tier 1, 지역 상점 프로필 활성화 |
| Town | 15 | Village 누적 + 주 특수 시설 Tier 2, 보조 지역 시설, 상점 확장 |
| City | 22 | Town 누적 + 주 특수 시설 Tier 3 랜드마크, 최고 상권·도시 역할 시설 |

New Game bootstrap은 시간을 흘려 Stage를 순차 승격시키지 않는다. 고정 인구로 Stage를 한 번 계산한 뒤, 그 Stage까지의 누적 결과를 결정론적으로 생성한다. 따라서 인구 17인 마을은 첫 등장부터 Town Stage와 Town까지의 누적 시설을 갖는다.

### 3.4 런타임 상태 고정

- 현재 정책에서는 런타임 인구가 변하지 않는다.
- 런타임 시간 경과만으로 Stage가 오르거나 시설·상점 Tier·Bounds가 추가되지 않는다.
- 일반 자동 건설 후보도 초기 발전을 이어가기 위한 용도로 실행하지 않는다. 파괴 시설 수리나 명시적 이벤트 건설은 별도 기능으로 구분한다.
- 후속 인구·발전 시스템이 생기더라도 일반적인 인구 감소로 Stage를 강등하거나 건물을 철거하지 않는 것을 기본값으로 검토한다.
- `Ruined` 전환만 별도의 원자적 폐허 처리에서 Stage 서비스와 건물 상태를 변경한다.

### 3.5 Stage 누적 생성 명세

각 마을은 생성 시 하나의 성장 계획을 확정한다. 여기서 성장 계획은 런타임 승격 일정이 아니라 **각 Stage가 어떤 완성 상태를 가져야 하는지 정의하는 누적 생성 명세**다. Stage 시설은 필요도 점수로 임의 선택하지 않고 계획에 기록된 내용대로 적용한다.

권장 보상 타입:

- `AddCoreFacility`: 상호작용 가능한 공통 필수 시설 추가
- `AddSpecialFacility`: 상호작용 가능한 지역 특수 시설 추가
- `UpgradeFacility`: 같은 상호작용 건물을 상위 Tier 건물로 교체하고 기능 확장
- `UnlockShopTier`: 재고 슬롯·품질·지역 풀 해금
- `ApplyStageCaps`: Stage 기본 자원 Cap 적용
- `SelectLayoutTier`: 누적 기능 건물 수에 맞는 초기 Bounds와 슬롯 구성 선택

초기 보상 적용은 세이브·재실체화에서 중복되지 않아야 한다. Stage만 보고 부작용을 매번 재적용하지 않고, 저장된 마일스톤 상태를 정본으로 사용한다.

### 3.6 Stage별 특수 시설 구성

특수 건물을 Stage마다 계속 새로 추가하기보다 상호작용 가능한 주 특수 시설 하나를 단계적으로 교체·강화한다. Tier 상승은 주변에 비상호작용 건물을 붙이는 방식이 아니라 같은 시설 엔티티의 외형 자산과 제공 기능을 교체하는 방식으로 표현한다.

```text
Village : 주 특수 시설 Tier 1
Town    : 같은 시설 Tier 2 + 선택적 보조 시설 1개
City    : 같은 시설 Tier 3 랜드마크 + 도시 역할 시설
```

예시:

| Profile | Village | Town | City |
|---|---|---|---|
| Dwarf | 제련소 Tier 1 | 같은 제련소 Tier 2 + 광산 거래소 | 같은 제련소 Tier 3 |
| Elf | 정령 수림 Tier 1 | 같은 정령 수림 Tier 2 + 마력 공방 | 같은 정령 수림 Tier 3 |
| Orc | 투기장 Tier 1 | 같은 투기장 Tier 2 + 전쟁 물자소 | 같은 투기장 Tier 3 |

표에 추가되는 광산 거래소·마력 공방·전쟁 물자소도 각각 독립된 직접 상호작용을 제공할 때만 배치한다. 특수 시설의 실제 종류·효과는 콘텐츠 Phase에서 확정하며, 기능이 확정되지 않은 시설은 외형만 먼저 만들지 않는다.

---

## 4. 자원 정책

### 4.1 원칙

자원은 런타임 수리·방어·명시적 소비를 위한 보조 시스템으로 유지한다. 초기 Stage와 그 누적 시설 생성에는 사용하지 않는다.

| 용도 | 자원 정책 |
|---|---|
| 초기 Stage 확정 | 자원 미사용 |
| 초기 공통 필수 시설 | 생성 시 완공 처리 |
| 초기 주·보조 특수 시설 | 생성 시 해당 Stage Tier까지 완공 처리 |
| 후속 런타임 Stage 발전 | 이번 범위에서 미실행 |
| 초기 Stage의 벽·방어 시설 | 생성 시 완공 처리 |
| 파괴 시설 수리 | 자원 사용 유지 |

### 4.2 건설 자금 정책

`VillageConstructionRules`에 다음 의미를 구분할 수 있는 데이터가 필요하다. 명칭은 구현 시 확정한다.

- `InitialMilestone`: 초기 Stage 누적 보상. 자원·건설시간 없이 완공 원장과 배치 상태 생성
- `LocalResources`: 기존 Wood/Stone/Metal 예약 규칙 사용

초기 Stage 보상은 진행 중 task로 남기지 않는다. 추상 상태에서는 완공 원장으로 생성하고, 실체화된 마을은 첫 프레임부터 해당 시설이 배치된 상태여야 한다. 런타임 건설시간과 변화 연출은 후속 발전 Phase에서 검토한다.

### 4.3 자원 Cap

기본 Cap은 Stage가 제공하고 특수 시설이 보정한다.

```text
최종 Cap = StageBaseCap + VillageProfileModifier + CompletedFacilityBonus
```

- Woodpile·Chest·Stockpile은 필수 성장 건물에서 제외한다.
- 기존 세이브의 배치 오브젝트와 Cap 보너스는 마이그레이션 후에도 유지한다.
- 신규 마을은 반복 저장 오브젝트 없이 초기 Stage 기본 Cap으로 생성된다.

---

## 5. 데이터 구조 계획

> 아래 타입명은 작업 경계를 설명하기 위한 제안이다. 기존 클래스와 중복 여부를 다시 확인한 뒤 확정한다.

### 5.1 `VillageStageTable` 정리

유지:

- `BoundsRadius` 또는 이를 대체할 Layout Tier
- `PromoMinPopulation`: 런타임 승격 기준이 아니라 초기 Stage 최소 인구로 의미 변경
- 표시 이름

폐기/미사용 전환:

- `PromoMinHousing`
- `PromoMinFood`
- `PromoRequiredSet`
- `PromoRequiredCivic`
- `PromoRequiredShop`

추가 후보:

- `BaseCap_Food`, `BaseCap_Wood`, `BaseCap_Stone`
- `DefaultShopTier`
- `LayoutTier`

### 5.2 마을 프로필

`VillageProfile` 계열 데이터가 최소 다음 값을 제공한다.

- `ProfileId`
- 적용 Race/Biome 또는 명시적 수제작 마을 키
- `GrowthPlanId`
- `LayoutProfileId`
- `TradeProfileId`
- 주 특수 시설 ID
- 선택 가능한 보조 시설 목록
- Stage별 자원 Cap 보정

프로필은 Race만으로 고정하지 않는다. 향후 `Capital`, `Frontier`, `TradeHub`, `Conquered` 같은 정착지 역할을 추가할 수 있어야 한다.

### 5.3 `VillageData` 저장 필드

추가 후보:

- `VillageProfileId`: 생성 시 확정한 프로필
- `FixedPopulation`: `SettlementId < 0` BaseTown처럼 History 정착지에 연결되지 않는 마을의 고정 경제 인구. History 정착지는 `SettlementLiveState.Pop`을 정본으로 사용
- `InitialStage`: 고정 인구와 Profile 상한에서 계산해 `VillageData.Stage`에 반영한 생성 결과. 별도 필드가 필요한지는 구현 시 결정
- `AppliedGrowthMilestones`: Stage 보상 중복 적용 방지 비트/키 목록
- `PendingInitialPlacements`: 완공 상태는 확정됐지만 월드 배치가 실패해 재시도가 필요한 초기 시설
- `ShopTier` 또는 TradeProfile에서 파생 가능한 최소 상태

`TargetStage`, `LastPromotionGameTime`, 런타임 승격 대기열은 후속 발전 Phase 전까지 추가하지 않는다.

Race/Biome 전체를 중복 저장할지는 구현 시 결정한다. 기본 방향은 History의 `SettlementLiveState`를 입력으로 프로필을 한 번 결정하고, 결과 `VillageProfileId`만 저장해 세이브 재현성을 보장하는 것이다. `SettlementId < 0`인 수제작 BaseTown은 명시적인 기본 프로필을 사용한다.

### 5.4 건물 의미 데이터

`BuildableItemTable` 또는 별도 시설 데이터에 다음 의미가 필요하다.

- 시설 역할: Core / Shop / Rest / Special
- 플레이어 상호작용 ID와 표시 프롬프트
- 해당 상호작용을 통해 실행되는 제공 서비스
- 시설 업그레이드 그룹
- 시설 Tier
- 건설 자금 정책
- Tier 교체용 건물 자산 ID
- 마을 성장 계획에서 선택 가능한지

신규 마을의 건물 후보는 유효한 플레이어 상호작용 진입점을 가져야 한다. 서비스가 없거나 수동으로 사용할 수 없는 건물형 오브젝트는 `Decoration`으로 돌리지 않고 신규 생성 후보에서 제외한다. 도로·바닥·경계처럼 건물이 아닌 환경 표현은 별도 Layout 데이터에서 최소한으로 다룬다.

기존 TableId는 세이브 호환을 위해 유지한다. 폐기 후보는 행을 바로 삭제하지 않고 신규 성장 계획에서 제외한다.

---

## 6. 건물 재분류 계획

초기 분류안이며 실제 상호작용 구현 여부를 다시 점검한 뒤 확정한다. 신규 마을의 건물 목록에는 플레이어가 직접 사용할 수 있는 시설만 남긴다. 여러 오브젝트가 한 서비스를 표현하고 있다면 장식 묶음으로 유지하지 않고, 대표 상호작용 건물 하나로 교체한다.

| 현재 항목 | 초기 처리 방향 |
|---|---|
| Campfire | 정보·체크포인트·세이브/귀환 중 명확한 직접 상호작용을 제공할 때만 중심 시설로 유지 |
| MerchantStall | 상점 UI를 여는 단일 시장 건물로 유지, TradeProfile 연결 |
| InnBed + Hearth | 개별 건물 세트를 생성하지 않고 상호작용 가능한 여관 또는 휴식소 건물 하나로 교체 |
| Furnace + Anvil + QuenchVat | 세 건물을 외형 묶음으로 합치지 않는다. 상호작용 가능한 제련소 건물 하나로 교체하고, 각 오브젝트에 고유 상호작용을 부여하지 않는 한 신규 배치에서 제외 |
| Shrine | 기도·버프·지역 의뢰 등 직접 상호작용이 확정된 Profile에서만 특수 시설로 유지 |
| Bed/Bedroll | 여관과 기능이 중복되므로 신규 마을 건물 후보에서 제외. 독립 휴식 기능이 필요할 때만 하나의 상호작용 시설로 재설계 |
| Woodpile/Chest/Stockpile | 장식으로 배치하지 않는다. 직접 보관 UI를 제공하는 단일 저장 시설이 필요할 때만 후보화 |
| CropPlot | 플레이어가 직접 이용하는 농업 기능이 구현된 Profile에서만 시설로 유지. 그 전에는 생성하지 않음 |
| ChoppingBlock/DryingRack/MiningCart | 소품으로 흡수하지 않는다. 고유 상호작용이 없으면 신규 배치에서 제외 |
| Well/TownPost/SignalBrazier | 실제 직접 상호작용이 확정된 항목만 유지하고 나머지는 장식화하지 않고 생략 |
| Palisade/Gate | 일반 시설 건물과 분리된 방어 구조물로 관리. Gate는 개폐 상호작용, Palisade는 충돌·HP 등 명확한 방어 기능이 있을 때만 생성 |

---

## 7. 배치와 밀도 계획

### 7.1 한 건물, 한 상호작용

신규 마을에서는 하나의 배치 건물을 하나의 명확한 상호작용 시설로 취급한다.

```text
상호작용 건물 1개
    = 배치·저장되는 건물 Entity 1개
    + 플레이어가 확인할 수 있는 상호작용 프롬프트 1개
    + 해당 건물이 제공하는 기능·서비스 상태
```

예:

- 시장 건물 → 거래 UI
- 여관 건물 → 휴식·여관 서비스 UI
- 제련소 건물 → 제련·수리 UI

Tier가 오를 때는 주변에 건물형 모듈을 추가하지 않는다. 같은 건물 Entity의 Tier, 외형 자산, 상호작용 기능을 함께 교체한다. 비상호작용 천막·창고·공방·주택처럼 건물로 오인할 수 있는 오브젝트는 배치하지 않는다.

도로·바닥 타일처럼 건물이 아닌 환경 표현은 허용하되 최소한으로 사용한다. 상호작용 가능한 건물에는 일관된 하이라이트·아이콘·프롬프트를 적용해 사용 가능 여부를 즉시 구분할 수 있게 한다.

### 7.2 지정 슬롯 우선

- `VillageTileFinder`의 전역 점수 탐색을 시설의 기본 배치기로 사용하지 않는다.
- LayoutProfile이 시설 역할별 상대 좌표/슬롯을 제공한다.
- 지정 슬롯이 점유됐을 때만 작은 반경의 fallback 탐색을 사용한다.
- 같은 seed와 프로필에서 같은 시설 위치가 나오게 한다.

### 7.3 초기 Bounds 산정

1. `InitialStage`까지 필요한 누적 상호작용 건물과 Tier 교체 목록을 먼저 확정한다.
2. 중심 구역의 기능별 건물 슬롯을 우선 채우고, 상위 Tier는 같은 슬롯에서 기존 건물을 교체한다.
3. 남은 주요 시설이 들어갈 슬롯이 없을 때만 생성 단계에서 Bounds를 확장한다.
4. 사방 균등 확장 대신 LayoutProfile이 지정한 구역·방향을 사용한다.
5. 도로·광장 예약 폭은 초기 완성 시설 수에 맞게 축소한다.

게임 시작 후 시간 경과에 따른 Bounds 확장은 V8 전까지 수행하지 않는다.

### 7.4 첫 수직 슬라이스 목표

Dwarf 광산 마을을 첫 검증 Profile로 사용한다.

- 전체 기능 구역: 약 14×14 이내에서 시작
- 공통 시설 3개 이하 + 주 특수 시설 1개
- 입구에서 모든 공통 서비스까지 짧은 동선
- 비상호작용 건물로 빈 공간을 채우지 않고, 기능 건물 간격과 도로 폭을 줄여 밀도를 확보
- 기존 34×23 또는 36×23 청사진과 비교 스크린샷 확보

정확한 크기는 Unity 시각 검증 후 조정한다. 숫자 자체보다 `상호작용 건물 비율`, `기능 밀도`, `이동 거리`, `시각적 혼잡도`를 기준으로 평가한다.

### 7.5 벽 정책 선결

현재 Race 청사진의 경계 오브젝트와 Hamlet 런타임 Palisade가 함께 존재할 수 있다. 수직 슬라이스 전에 다음 중 하나를 선택한다.

1. 청사진 장식 경계 제거 + 초기 Stage에 맞는 기능성 방어 구조물 생성
2. 기존 경계에 충돌·HP·Gate 개폐 기능을 부여해 하나의 방어 구조로 사용
3. 방어 기능이 없다면 경계 오브젝트를 신규 마을에서 생략

권장 기본값은 **기능 없는 청사진 경계 제거 + 초기 Stage에 따른 기능성 벽·Gate 생성**이다. 벽의 런타임 증축은 V8 전까지 진행하지 않는다.

---

## 8. 지역 상점 계획

### 8.1 목표

- 모든 마을이 같은 5개 랜덤 아이템을 판매하지 않는다.
- 지역 특산품은 현지에서 싸고 풍부하다.
- 부족품은 현지 매입가가 높아 교역 동기를 만든다.
- 전투 장비 무한 차익과 TradeGood 교역을 구분한다.
- 특수 시설과 상점 품질이 연결된다.

### 8.2 아이템 거래 태그

`ItemTable`에 `TradeTag` Flags 추가를 검토한다.

예시:

- `Ore`, `HeavyArmor`, `Arcane`, `Ranged`
- `Herb`, `Poison`, `Cold`, `TravelSupply`
- `TradeGood`, `RareImport`

ItemId 직접 나열은 초기 예외 또는 보장 품목에만 사용한다.

### 8.3 TradeProfile

최소 데이터:

- 보장 품목 그룹
- 지역 특산품 가중 풀
- 희귀/수입품 가중 풀
- 태그별 플레이어 구매가 배율
- 태그별 상인 매입가 배율
- 수량 범위
- 재입고 시간
- Stage/ShopTier별 슬롯 수
- 특수 시설 Tier에 따른 추가 풀

가격 배율 단위는 명확한 10,000분율을 권장한다.

```text
플레이어 구매가 = BasePrice × SellToPlayerBp / 10000
상인 매입가     = BasePrice × BuyFromPlayerBp / 10000
```

기존 `SellRatioBp`의 단위와 이름은 혼동 가능성이 있으므로 마이그레이션 때 의미를 명확히 분리한다.

### 8.4 재고 구성 초안

5슬롯 기준:

- 공통 필수품 2
- 지역 특산품 2
- 특수 시설/희귀 수입품 1

상점 Tier가 오르면 슬롯 수뿐 아니라 보장 품목의 Tier와 특수 시설 풀을 확장한다.

**기본 장비 칸 (2026-10-03 추가, [NPC_SHOP_EQUIPMENT_DESIGN](../npc/NPC_SHOP_EQUIPMENT_DESIGN.md) D117)**: 위 슬롯과 **별도로** 고정 옵션 기본 장비를 상점 Tier 1~2는 3칸, 3~4는 4칸 진열한다(`TradeStockSlotKind.BasicEquipment`). 후보는 상점 Tier가 여는 아이템 Tier 이하에 기본 아이템 변형이 있는 장비이고, 같은 확률로 칸끼리 중복 없이 뽑는다. 기존 슬롯의 품목·인스턴스(무작위 옵션)는 바뀌지 않는다. 플레이어도 살 수 있고, NPC는 이 칸의 기본 아이템만 산다.

### 8.5 재현성과 저장

- 재입고 난수는 `SettlementId + RestockCycle + DerivedSeed 채널`로 결정한다.
- `UnityEngine.Random` 기반 재롤을 제거한다.
- `MerchantStockEntry`에 확정 구매가 또는 가격 산출에 필요한 Profile/회차를 저장한다.
- 상점 UI에는 가격과 함께 `지역 특산`, `현지 부족`, `희귀 수입` 같은 이유를 표시한다.
- 상점 재롤 직후 세이브되지 않아도 같은 결과를 재현해야 한다.
- 기본 장비 칸(2026-10-03)은 기존 슬롯을 다 롤한 뒤 **같은 회차 RNG를 이어** 롤하고 변형 선택도 같은 RNG를 쓴다. 그래서 기존 슬롯의 결과는 그대로이고 (마을, 회차) 결정론도 유지된다. 기본 아이템 매물은 행 번호만 저장된다(D122).

### 8.6 교역 밸런스

- 전투 장비는 일반적인 구매/매각 스프레드를 유지한다.
- 지역 간 양수 차익은 `TradeGood` 태그에 우선 허용한다.
- 재고 수량·재입고 주기·플레이어 운반량으로 차익 규모를 제한한다.
- 마을 자원 환원은 지역 생산량에 직접 연결하기 전 별도 밸런스 검증을 거친다.

---

## 9. Phase 요약

> 상태 범례: ⬜ 미착수 · 🟡 진행 중 · ✅ 완료 · 보류 후속 범위

| Phase | 내용 | 핵심 산출물 | 선행 | 위험도 | 상태 |
|---|---|---|---|---|---|
| **V0** | 기준선·콘텐츠 분류 | 현행 덤프, 건물 Keep/Convert/Retire 표, 수치 확정안 | 없음 | 낮음 | ✅ |
| **V1** | Village Profile·저장 기반 | Profile resolver, `VillageProfileId`, 구 세이브 기본 프로필 | V0 | 중 | ✅ |
| **V2** | 고정 인구·초기 Stage 확정 | 역사 종료 인구 고정, 초기 Stage 계산, 런타임 발전 차단 | V1 | 중 | 🟡 (구현·정적 검증 완료, 플레이 모드 실측 2건은 V7 연계) |
| **V3** | 초기 Stage 누적 시설 생성 | 마일스톤 원장, 완공 상태 생성, 명시적 시설 목록 | V2 | 높음 | 🟡 (구현·정적 검증 완료, materialize 배치·상호작용 실측은 V4/V7 연계) |
| **V4** | Dwarf 배치 수직 슬라이스 | 상호작용 건물 슬롯, Tier 교체 자산, 기능성 벽, 밀도 검증 | V3 | 높음/콘텐츠 | 🟡 (구현·정적 검증 완료, 시각·상호작용 실측과 비교 스크린샷은 플레이 모드 필요) |
| **V5** | 지역 상권 | TradeTag/Profile, 결정론 재고, 지역 가격 | V1, V2 | 높음 | 🟡 (구현·정적 검증 완료, 상점 UI 표시·구매/판매 실측은 플레이 모드 필요, V7 연계) |
| **V6** | Race/Biome Profile 확장 | Race별 상권 세분화, Layout variant, 역할 결합 초안 (특수 시설은 Dwarf만 — 사용자 결정) | V4, V5 | 콘텐츠 큼 | 🟡 (구현·정적 검증 완료, 상점·배치 시각 실측은 플레이 모드 필요, V7 연계) |
| **V7** | 통합 검증·정본 갱신 (마이그레이션 제외 — 세이브 신규 시작) | parity `CompareFixedState`, New Game 완성형 정적 검증(하니스 17/17), 플레이 모드 절차, 문서 갱신 | V3~V6 | 높음 | 🟡 (파리티·New Game 완성형 정적 검증 완료. 마이그레이션은 사용자 결정으로 제외. 플레이 모드 실측·현행 정본/Wiki 갱신은 대기) |
| **V8 (후속)** | 런타임 마을 발전 재미 검증 | 성장 계기·속도·연출 프로토타입과 채택 여부 | V7 이후 별도 승인 | 미정 | 보류 |

**권장 의존 그래프**

```text
V0 → V1 → V2 → V3 → V4 → V6 → V7
          └──────→ V5 ──────┘

V7 완료 및 1차 재미 검증 → V8 후속 검토
```

---

## 10. Phase별 작업

### V0 — 기준선·콘텐츠 분류

#### 목표

코드를 바꾸기 전에 현재 성장 결과와 콘텐츠 분류를 확정한다.

#### 작업

- [x] map seed 표본 3개 이상에서 정착지별 절차적 역사 완료 인구 스냅샷 분포 덤프
- [x] 현 New Game에서 호출되는 bootstrap 동일 경로의 Stage·건물 수·완공 순서 덤프
- [x] 현 Buildable 전 항목을 `InteractiveFacility / FunctionalDefense / LayoutEnvironment / Retire`로 분류
- [x] 현 Race 청사진 크기·빈 바닥·경계·기능 오브젝트 수 기록
- [x] 고정 인구별 초기 Stage 임계값 초안 검증
- [x] 벽 정책 3안 중 하나 확정
- [x] 첫 특수 시설 Profile로 Dwarf 제련소 확정 여부 결정

#### 검증 기준

- [x] 실제 적용된 운영 정착지의 원본 역사 완료 인구에서 각 초기 Stage가 충분한 표본을 가진다.
- [x] City가 지나치게 흔하거나 영구적으로 불가능하지 않다.
- [x] 폐기 후보 TableId의 기존 세이브 처리 방침이 모두 정해졌다.
- [x] `InteractiveFacility`로 남긴 모든 항목에 플레이어 상호작용 진입점과 식별 프롬프트가 정의돼 있다.

#### V0 정적 기준선 결과 (2026-07-14)

정적 기준선은 `BuildableItemTable.bytes`, Race 청사진 7종, `System_VillageServiceProximity`, `ServiceUIRouter`, `MapManager.PlaceObjectInternal`, `RaceSettlementPlacer`를 대조해 확정했다. 인구 분포와 bootstrap 결과는 아래 Unity Editor 런타임 기준선에 별도로 기록했다.

##### Buildable 분류와 신규 생성 처리

`InteractiveFacility`의 상호작용 ID와 프롬프트는 V0 콘텐츠 계약이다. 현재 코드에서 실제 UI 라우팅이 존재하는 것은 Shop·Forge·Inn·Shrine이며, `VillageHub`와 Tier 교체 시설은 V3/V4에서 구현한다.

| Id | 현재 항목 | V0 분류 | 신규 생성 처리와 상호작용 계약 |
|---:|---|---|---|
| 1 | 나무 벽 | Retire | 기능 없는 청사진 경계. 신규 Race 청사진에서 제외하고 기능성 방어 구조로 대체 |
| 100 | Campfire | InteractiveFacility / Convert | 단일 중심 거점 `VillageHub`, 프롬프트 `마을 거점 이용`, 체크포인트·세이브·귀환 중 확정 기능 제공 |
| 101 | Bedroll | Retire | 신규 생성 제외. 여관/휴식소로 흡수 |
| 102 | Bed | Retire | 신규 생성 제외. 여관/휴식소로 흡수 |
| 110 | Woodpile | Retire | 직접 저장 UI가 없어 신규 생성 제외 |
| 111 | Chest | Retire | 직접 저장 UI가 없어 신규 생성 제외 |
| 112 | Stockpile | Retire | 직접 저장 UI가 없어 신규 생성 제외 |
| 120 | CropPlot | Retire | 직접 농업 상호작용이 없어 신규 생성 제외 |
| 130 | Well | Retire | `Beacon` 서비스가 입력/UI로 라우팅되지 않아 신규 생성 제외 |
| 140 | ChoppingBlock | Retire | 직접 상호작용이 없어 신규 생성 제외 |
| 141 | DryingRack | Retire | 직접 상호작용이 없어 신규 생성 제외 |
| 142 | MiningCart | Retire | 직접 상호작용이 없어 신규 생성 제외 |
| 150 | Hearth | Retire | `Cooking` 서비스가 입력/UI로 라우팅되지 않아 신규 생성 제외. 여관 기능에 흡수 |
| 151 | MerchantStall | InteractiveFacility / Keep | `Shop`, 프롬프트 `거래` |
| 152 | TownPost | Retire | `Civic`은 근접 집계만 되고 UI 라우팅이 없어 신규 생성 제외 |
| 153 | InnBed | InteractiveFacility / Convert | 단일 여관/휴식소 건물 `Inn`, 프롬프트 `휴식` |
| 154 | SignalBrazier | Retire | `Signal` 입력/UI가 없어 신규 생성 제외 |
| 160 | Furnace | InteractiveFacility / Convert | Dwarf 주 특수 시설 단일 제련소의 기반, `Forge`, 프롬프트 `제련·수리` |
| 161 | Anvil | Retire | 현재 Forge UI는 열 수 있지만 단일 제련소와 기능이 중복되므로 신규 개별 생성 제외 |
| 162 | QuenchVat | Retire | `Quench` 입력/UI가 없고 단일 제련소의 외형 모듈로도 생성하지 않음 |
| 170 | Shrine | InteractiveFacility / Keep | 실제 Shrine 콘텐츠가 있는 Profile에서만 `Shrine`, 프롬프트 `기도` |
| 180 | Palisade | FunctionalDefense / Convert | 기능성 벽으로 유지하되 V4에서 충돌·HP·피해 처리를 완성해야 함 |
| 181 | PalisadeGate | FunctionalDefense / Convert | 기능성 Gate로 유지하되 V4에서 개폐 상호작용·HP를 완성해야 함 |

현재 Buildable에는 `LayoutEnvironment`로 유지할 항목이 없다. 도로·바닥은 건물이 아닌 Layout 데이터에서만 다룬다.

모든 Retire TableId는 삭제하지 않고 신규 Profile/GrowthPlan 후보에서만 제외한다. V0/V1~V6 동안 구 세이브 레코드와 기존 배치는 그대로 보존한다. 상호작용 가능한 대체 시설이 완성된 항목만 V7에서 상태를 이전하고, 대체 시설이 없는 항목은 레코드를 보존한 `Retired` 상태로 전환할 수 있으나 자동 삭제하거나 장식 건물로 노출하지 않는다.

##### Race 청사진 기준선

`빈 보행 바닥`은 ground가 있고 Object/Blocked가 없는 셀이다. `기능 오브젝트`는 ObjectId 100 이상인 Buildable 또는 `_objectList` 엔트리로 집계했다.

| 청사진 | 크기 | ground | 빈 보행 바닥 | 시각 경계 Id 1 | Blocked | 기능 오브젝트 |
|---|---:|---:|---:|---:|---:|---:|
| Dwarf | 34×23 | 609 | 539 (88.5%) | 70 | 0 | 0 |
| Elf | 34×23 | 580 | 510 (87.9%) | 70 | 0 | 0 |
| Human | 34×23 | 651 | 581 (89.2%) | 70 | 0 | 0 |
| Lizardman | 36×23 | 609 | 539 (88.5%) | 70 | 0 | 0 |
| Nomad | 36×23 | 609 | 539 (88.5%) | 70 | 0 | 0 |
| Orc | 36×23 | 609 | 539 (88.5%) | 70 | 0 | 0 |
| Yeti | 36×23 | 609 | 539 (88.5%) | 70 | 0 | 0 |

##### 선결 결정

- 벽 정책은 1안, **기능 없는 청사진 경계 Id 1 제거 + 초기 Stage에 맞는 기능성 Palisade/Gate 생성**으로 확정한다.
- 현재 Id 180/181은 Tile 배치 시 둘 다 `Blocked`만 설정된다. `IsBreakable`/HP를 소비하는 Tile 피해 경로와 Gate 개폐 입력은 확인되지 않았으므로, V4 구현과 Unity 시각·충돌 검증 전에는 기능성 방어 완성으로 간주하지 않는다.
- 첫 특수 시설 Profile은 **Dwarf 광산 마을의 단일 제련소**로 확정한다. Id 160을 전환 기반으로 사용하고 Id 161/162를 별도 건물이나 외형 모듈로 생성하지 않는다.

##### 진단 경로 충돌

- `ChronicleDump.BuildPlacements`는 모든 region을 순회하지만 현재 `RaceSettlementPlacer.ComputeAll`은 2×2 region 블록당 최대 2개를 선택한다.
- `ChronicleSimExport.TryPlaceMirror`는 현재 `RaceSettlementPlacer.TryPlace`의 map chunk 경계 필터를 복제하지 않는다.
- 따라서 기존 두 Editor 덤프의 출력은 V0 인구·bootstrap 검증 근거로 사용하지 않는다. `Assets/Scripts/Editor/VillageV0Diagnostics.cs`가 현재 배치 순서·경계 필터를 복제해 seed 12345/1001/1002를 진단했으며, 아래 결과는 프로젝트 로컬 `Temp/VILLAGE_V0_RUNTIME_DUMP.md`를 근거로 기록했다.

##### 런타임 기준선 결과와 정책 결정

`VillageV0Diagnostics`를 Unity 6000.4.2f1에서 실행했다. 세 seed 모두 정착지 배치와 bootstrap 계획 재현성이 일치했고 bootstrap 실패는 없었다.

| seed | 계산 배치 | 실제 적용 | 운영 정착지 | 경제 인구 25 | 제안 City | bootstrap 완결 | 실패 | 잔여 ActiveBuildTask |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 12345 | 1,250 | 872 | 1,147 | 884 | 911 | 794 | 0 | 4 |
| 1001 | 1,250 | 888 | 1,160 | 860 | 889 | 822 | 0 | 6 |
| 1002 | 1,250 | 874 | 1,150 | 911 | 923 | 800 | 0 | 6 |
| **합계** | **3,750** | **2,634** | **3,457** | **2,655** | **2,723** | **2,416** | **0** | **16** |

제안 임계값 `1/3/8/15/22`의 합산 Stage 표본은 Settlement 1, Hamlet 16, Village 504, Town 213, City 2,723이다. City가 운영 정착지의 78.8%이며 Settlement·Hamlet 표본은 극히 적으므로 임계값 초안과 두 검증 기준은 통과하지 못했다.

이는 현재 `VillageManager.QuantizeAbstractPopulation`이 역사 인구를 `pop / 50`으로 변환하고 경제 인구를 최대 25로 제한하는 코드와 직접 연결된다. 운영 정착지 3,457개 중 2,655개(76.8%)가 이미 상한 25다. 따라서 현 경제 인구만 Stage 입력으로 사용하면 City 임계값을 25 이하로 둘 때 City가 최소 76.8%가 되고, 25보다 높게 둘 때 City가 영구적으로 불가능해진다. V0에서는 초기 Stage 입력을 원본 역사 완료 인구로 분리하고 경제 인구 `1~25`는 생산량 계산에만 유지하는 정책으로 확정한다. 현 bootstrap 코드는 아직 경제 인구와 `MaxStage = Town`을 사용하므로 실제 전환은 V2에서 구현한다.

Stage 입력을 경제 인구와 분리한 후보를 검증하기 위해 실제 맵에 적용된 운영 정착지 2,416개의 원본 역사 완료 인구를 합산했다. 백분위 경계는 P10 597, P30 1,679, P50 2,521, P65 2,592, P75 2,795, P90 3,182, P95 3,419다.

| 후보 | Settlement | Hamlet | Village | Town | City |
|---|---:|---:|---:|---:|---:|
| City 상위 10% | `< 597`: 241 (10.0%) | `597~1,678`: 482 (20.0%) | `1,679~2,591`: 841 (34.8%) | `2,592~3,181`: 610 (25.2%) | `>= 3,182`: 242 (10.0%) |
| City 상위 5% | `< 597`: 241 (10.0%) | `597~1,678`: 482 (20.0%) | `1,679~2,591`: 841 (34.8%) | `2,592~3,418`: 731 (30.3%) | `>= 3,419`: 121 (5.0%) |

두 후보 모두 모든 Stage에 최소 121개 이상의 표본이 있고 City가 가능하면서 과반이 되지 않는다. 콘텐츠 희소성과 제작량을 고려해 **City 상위 5%안**을 채택한다. 초기 Stage 임계값은 Settlement `< 597`, Hamlet `>= 597`, Village `>= 1,679`, Town `>= 2,592`, City `>= 3,419`로 확정한다. 세 seed 합산 실제 분포는 10.0%/20.0%/34.8%/30.3%/5.0%이며 모든 Stage가 충분한 표본을 가진다.

현 bootstrap 합산 결과는 Stage Settlement 2,042, Hamlet 60, Village 249, Town 65이며 완공 건물 수는 2개 1곳, 8개 10곳, 16개 339곳, 28개 2,066곳이다. finalize 성공 뒤에도 세 seed 합계 16개의 `ActiveBuildTask`가 남아 있으므로 초기 완성형 전환 시 제거해야 할 현행 기준선으로 기록한다.

`VillageSimulationParity`의 sliced 비교는 인구 1/3/8/15/22/25에서 모두 일치했다. hourly-fast 비교는 인구 8과 15에서 `LastConstructionTierCheckGameTime`이 각각 `216 != 213`, `408 != 405`로 불일치했다. `AdvanceAbstractConstruction`은 진입 시 4시간 경과 검사를 Stage 상한과 무관하게 실행하지만 fast-forward의 다음 tier-check 예약은 `Stage < MaxStage`일 때만 수행하므로 생기는 현행 parity 실패다. V0 범위에서는 코드를 수정하지 않는다.

---

### V1 — Village Profile·저장 기반

#### 주요 코드 접점

- `Assets/Scripts/Village/VillageData.cs`
- `Assets/Scripts/Village/VillageManager.cs`
- `Assets/Scripts/History/HistoryManager.cs`
- `Assets/Scripts/History/Chronicle/WorldSimState.cs`
- `Assets/Scripts/Map/MapManager_Generator.cs`
- `Assets/Scripts/Common/Tables.cs`
- `Assets/Scripts/Data/DataManager_Table.cs`
- `Assets/Scripts/Editor/DownloadTables.cs`
- `Assets/Scripts/Manager/DataManager.cs` 세이브 마이그레이션

#### 작업

- [x] Profile 데이터 스키마 확정
- [x] Race/Biome/정착지 역할 → Profile resolver 구현
- [x] `VillageData.VillageProfileId` 저장
- [x] 수제작 BaseTown 기본 Profile과 고정 경제 인구 초기화 경로 추가
- [x] 구 세이브 로드 시 결정론적으로 기본 Profile 보정
- [x] 프로필 누락 시 명시적 fallback과 경고
- [x] Profile이 다른 마을에서 같은 성장/상권 데이터를 잘못 공유하지 않는지 확인

#### 검증 기준

- [x] 같은 seed와 세이브에서 모든 마을의 ProfileId가 재현된다.
- [x] Race/Biome이 다른 마을이 의도한 다른 Profile을 받는다.
- [x] 구 세이브에 Profile 필드가 없어도 로드된다.
- [x] `SettlementId < 0` BaseTown도 Profile과 물리 NPC 수에 의존하지 않는 고정 경제 인구를 가진다.

##### V1 구현·검증 결과

- `VillageProfile`은 Profile/Race/Biome/역할, Growth/Layout/Trade 연결 ID, 주·보조 시설, Stage별 Cap 보정, `MaxStage`, 기본 경제 인구를 읽기 전용 스키마로 제공한다. 현재 카탈로그는 BaseTown 1개와 지원 Race/Biome 13조합 × 역할 5종을 가진다.
- History의 Race/Biome과 현재 확인 가능한 역할(Standard/Capital/Conquered)로 Profile을 결정하며, Frontier/TradeHub 역할 입력도 저장 ID 경계에 포함했다. 저장된 유효 `VillageProfileId`는 이후 재계산하지 않는다.
- 구 세이브의 빈 Profile, 알 수 없는 Profile, 지원하지 않는 Race/Biome은 명시적인 결정론 fallback을 사용하고 경고한다. BaseTown은 `base_town.dwarf_mining`과 저장된 `FixedPopulation`을 사용하며 물리 NPC 수를 참조하지 않는다.
- `Assembly-CSharp`와 `Assembly-CSharp-Editor` 컴파일이 오류 0건으로 통과했다. Profile 66개 고유성, 성장·상권 ID와 컬렉션 격리, Race/Biome/역할 분기, fallback 경고, JSON 신규·구 포맷, BaseTown 인구 우선순위에 대한 정적 검증 471개 assertion이 통과했다.
- 동일 seed의 History 입력 재현성은 V0 진단 결과를 사용했고, V1 resolver의 동일 입력 재현성과 `VillageProfileId` JSON 왕복을 결합해 Profile 재현성을 확인했다. `ARPG/Village/Run V1 Profile Diagnostics`에서 현재 Editor 내부 전체 seed 검증을 다시 실행할 수 있다.

---

### V2 — 고정 인구·초기 Stage 확정

#### 주요 코드 접점

- `Assets/Scripts/Village/VillageProgressionRules.cs`
- `Assets/Scripts/Common/System/System_VillageTierProgression.cs`
- `Assets/Scripts/Common/System/System_SettlementSim.cs`
- `Assets/Scripts/Village/VillageAbstractConstruction.cs`
- `Assets/Scripts/Village/VillageDevelopmentBootstrap.cs`
- `Assets/Scripts/Village/VillageManager.cs`
- `Assets/Scripts/History/HistoryManager.cs`
- `Assets/Scripts/History/Chronicle/SettlementPopulationModel.cs`
- `Assets/Scripts/History/Chronicle/WorldSimState.cs`
- `Assets/_BinaryData/TableData/VillageStageTable.bytes`

#### 작업

- [x] 절차적 역사 완료 직후 각 정착지의 최종 인구를 `SettlementLiveState.Pop`에 고정
- [x] 일반 런타임 추상 advance에서 `SettlementPopulationModel.Advance`를 호출하지 않도록 인구 갱신 경로 차단
- [x] materialize 직전 catch-up과 fold-back에서 고정 인구를 덮어쓰지 않도록 정리
- [x] `LastSimGameTime`을 현재 시각에 맞춰 유지해 인구 재활성화 시 과거 backlog가 누적되지 않게 처리
- [x] `VillageData.Population`을 고정 인구 스냅샷에서만 동기화하고 물리 NPC 수 기반 BaseTown fallback 제거 (V1의 `FixedPopulation` 경로 확인·유지 — 물리 NPC 수 참조 없음)
- [x] 고정 인구와 `VillageProfile.MaxStage`로 `InitialStage`를 한 번 계산하고 `VillageData.Stage`에 반영
- [x] 초기 Stage 계산에서 Housing/Food/Set/Civic/Shop·자원·건물 수 게이트 제거
- [x] `System_VillageTierProgression`의 런타임 승격 실행을 중단하거나 후속 기능 플래그 뒤로 격리
- [x] 일반 런타임 자동 시설 추가·Stage 기반 Bounds 확장 경로 중단
- [x] bootstrap의 synthetic 시간 진행을 제거하고 초기 Stage를 직접 적용
- [x] UI/디버그에서 다음 승격 조건을 숨기고 현재 고정 인구·초기 Stage·Profile을 표시 (게임 UI는 승격 조건을 노출하지 않음을 확인, `VillageDebugLog`의 TierCheck를 고정 상태 표시로 교체)
- [x] 현재 정책에서 인구 증감·Stage 승격·강등이 발생하지 않음을 명시 (`VillageProgressionRules.RuntimeProgressionEnabled` 주석과 이 문서에 기록)

#### 검증 기준

- [x] 게임시간을 장기간 진행해도 `SettlementLiveState.Pop`과 `VillageData.Population`이 변하지 않는다. (정적: 런타임의 `Pop` 변이 지점이 chronicle 생성기와 보존된 모델 내부에만 존재, 10,000h/1년 동결 advance 하니스 통과)
- [ ] 실체화 여부, 물리 NPC 사망·보충, Food 0, 건물 파괴가 고정 인구를 바꾸지 않는다. — 코드 경로상 결합 없음은 확인했으나 Unity 플레이 모드 실측이 필요해 미체크 (V7 통합 검증과 연계)
- [x] 같은 seed의 New Game에서 고정 인구와 `InitialStage`가 항상 동일하다. (V1에서 검증한 동일 seed History 재현성 + `CalculateInitialStage` 순수 함수 + JSON 왕복. Editor 내 재확인: `ARPG/Village/Run V2 Fixed State Diagnostics`)
- [x] 인구 갱신 중단 시각이 정렬되어 후속 재활성화 때 누적 catch-up이 발생하지 않는다. (slice/materialize/fold-back 전 경로가 `LastSimGameTime = now` 정렬)
- [x] Housing/Food/Set/Civic/Shop·자원이 0이어도 인구에 맞는 초기 Stage가 적용된다. (자원·건물 0 상태 정적 하니스 통과)
- [x] 게임시간을 장기간 진행해도 Stage와 시설 구성이 자동으로 증가하지 않는다. (정적: TierProgression/BuildQueue/WallPlanner 게이트 + 동결 advance 하니스 통과)
- [ ] 첫 materialize와 save/load 후에도 초기 Stage가 유지된다. — JSON save/load 왕복과 왕복 후 동결 advance는 통과했으나, 실제 청크 기반 첫 materialize는 플레이 모드 실측이 필요해 미체크 (V7 통합 검증과 연계)

##### V2 구현·검증 결과 (2026-07-14)

- 인구 고정: `HistoryManager.AdvanceAbstractSlice`와 `MaterializeSettlement`에서 `SettlementPopulationModel.Advance` 호출을 제거하고 `LastSimGameTime`만 현재 시각으로 정렬한다. 모델과 Biome 성장률·수용력 데이터는 삭제하지 않았고 직접 호출 시 여전히 동작함을 확인했다(후속 인구 설계용 보존).
- 초기 Stage: `VillageProgressionRules.CalculateInitialStage(원본 역사 인구, MaxStage)`가 V0 확정 임계값 `597/1,679/2,592/3,419`로 한 번 계산한다. **임계값은 테이블이 아니라 코드 상수로 구현했다** — `VillageStageTable.PromoMinPopulation`은 경제 인구(1~25) 단위의 런타임 승격 게이트이고 Google Sheets(`DownloadTables`)에서 재생성되므로, 원본 인구(수백~수천) 단위 초기 Stage 임계값을 같은 컬럼에 싣는 §5.1 안은 적용하지 않았다.
- bootstrap: `BootstrapNewGameVillageDevelopment`가 synthetic 시간 진행(CreatePlans/AdvanceBootstrapSlice 루프) 대신 `VillageDevelopmentBootstrap.ApplyFixedInitialState`로 Stage·Bounds·clock을 직접 적용한다. 진행 task를 만들지 않으므로 V0에서 기록한 bootstrap 잔존 `ActiveBuildTask` 문제가 신규 게임에서 자연 해소된다. 기존 plan/slice API는 V0 진단·parity 재현용으로 보존했다.
- 런타임 발전 중단: `VillageProgressionRules.RuntimeProgressionEnabled`(현재 false) 뒤로 `System_VillageTierProgression`·`System_VillageBuildQueue`·`System_VillageWallPlanner`를 격리하고, 일반 런타임 추상 advance와 materialize catch-up은 `AllowDevelopment=false`로 자원 생산 catch-up·clock 정렬만 수행한다. 진행 task 스냅샷·완공 건물·Cap은 삭제·재적용 없이 동결 보존된다.
- Stage별 누적 시설 생성은 V3 범위로 구현하지 않았다. 따라서 V2 시점의 New Game 마을은 초기 Stage는 갖지만 시설은 비어 있다(V3에서 `BootstrapNewGameVillageDevelopment` 지점에 이어 붙임).
- 검증: `Assembly-CSharp`/`Assembly-CSharp-Editor` 컴파일 오류 0건(dotnet msbuild). 컴파일된 어셈블리를 참조하는 Unity 외부 정적 하니스에서 29개 assertion 통과 — 임계값 경계, MaxStage 상한, 경제 인구(1~25) 분리(raw 1,250과 2,711이 같은 경제 인구 25에서 다른 초기 Stage), 자원 0 초기 적용, 10,000h/1년 동결 advance의 Stage·시설·task 불변과 자원 생산 지속, 비운영 clock 정렬, JSON save/load 왕복, BaseTown `FixedPopulation` 우선순위.
- Editor 진단: `ARPG/Village/Run V2 Fixed State Diagnostics`가 Profile·고정 원본 인구·경제 인구·초기 Stage 표본과 seed 재현성·Stage 분포를 `Temp/VILLAGE_V2_FIXED_STATE_DUMP.md`로 덤프한다. seed 기반 연대기 재현 검증은 `Mathf.PerlinNoise`(Unity icall) 의존으로 Editor 전용이며, 현재 Editor가 프로젝트를 점유 중이라 이번 세션에서는 배치 실행하지 못했다(메뉴에서 실행 가능). 실제 청크 materialize/fold-back·물리 NPC 실측은 플레이 모드 전용 제한으로 리포트에 기록된다.

---

### V3 — 초기 Stage 누적 시설 생성

#### 주요 코드 접점

- `Assets/Scripts/Village/VillageNeedsEvaluator.cs`
- `Assets/Scripts/Village/VillageConstructionRules.cs`
- `Assets/Scripts/Village/VillageConstructionLedger.cs`
- `Assets/Scripts/Village/VillageAbstractConstruction.cs`
- `Assets/Scripts/Common/System/System_VillageBuildQueue.cs`
- `Assets/Scripts/Village/BuildTaskSnapshot.cs`

#### 작업

- [x] Stage별 누적 시설·상점·Cap·배치 보상 데이터 정의 (`VillageGrowthPlanCatalog` — 필요도 점수가 아닌 Profile별 명시적 목록. SelectLayoutTier는 V4 범위라 제외)
- [x] Settlement부터 `InitialStage`까지 적용된 마일스톤을 `VillageData`에 완공 상태로 저장 (`AppliedGrowthMilestones` MilestoneKey 목록 + `PlacedObjects` 완공 레코드)
- [x] 초기화 시 누적 보상을 자원·건설시간·진행 task 없이 정확히 한 번 적용 (`VillageInitialDevelopment.ApplyInitialMilestones`)
- [x] 일반 필요도 기반 자동 건설 후보 생성을 런타임 발전 용도로 실행하지 않음 (V2 `RuntimeProgressionEnabled`/`AllowDevelopment` 게이트 유지 확인 — V3에서 변경 없음)
- [x] 초기 시설 목록을 Profile 허용 목록으로 제한하고 전역 ID 스캔 제거 (초기 생성은 GrowthPlan 명시 목록만 사용 — `VillageNeedsEvaluator`의 100~199 스캔은 초기 생성 경로에서 호출되지 않음)
- [x] 상위 시설 Tier가 하위 Tier를 교체·흡수하는 초기 생성 정책 구현 (`UpgradeFacility`가 같은 레코드의 `FacilityTier`만 상승 — 건물 증식 없음, Tier 교체 자산은 V4)
- [x] 모든 초기 시설 데이터에 직접 상호작용 ID·프롬프트·제공 기능 검증 추가 (`VillageInitialFacilityCatalog` 계약 + `ValidatePlan` 테이블 대조)
- [x] 건물형 비상호작용 오브젝트가 초기 시설 목록에 들어오면 데이터 검증에서 실패 처리 (허용 목록 5종 외 전부 실패, 완공 원장 미생성)
- [x] 서비스·Cap·세트 완료 부작용이 한 번만 적용되는지 보장 (`ApplyCompletedBuild` 단일 계약 + MilestoneKey 재적용 차단)
- [x] materialize 시 기존 완료 보상을 재적용하지 않음 (materialize 복원 경로는 마일스톤 적용을 호출하지 않으며, 기존 복원 중복 방지 계약 유지)
- [x] 배치 실패 시설은 `PendingInitialPlacements`로 보존하고 결정론적으로 재시도 (별도 리스트가 아니라 기존 `PlacedObjects[].PlacementPending`이 정본 — §5.3 역할 충족, `RestorePlacedObjectsFromData`가 재시도)

#### 검증 기준

- [x] 각 초기 Stage가 하위 Stage의 필수 결과를 빠짐없이 포함한다. (정적 하니스: Settlement~City 5단계 누적 시설·Tier·ShopTier·Cap 전수 확인)
- [x] 자원 0 상태에서도 초기 Stage의 필수·특수 시설이 완공 상태로 생성된다. (자원 소비 0·예약 0 확인)
- [x] New Game 완료 시 초기 발전용 진행 task가 남지 않는다. (bootstrap이 task를 생성하지 않음 + 적용 후 `ActiveBuildTasks.Count == 0`)
- [ ] save/load 및 materialize 반복 후 시설·Cap 중복이 없다. — JSON save/load 왕복 + 왕복 후 재적용 무중복은 정적 통과. 실제 청크 materialize/fold-back 반복은 플레이 모드 실측이 필요해 미체크 (V7 통합 검증과 연계)
- [x] 같은 seed·Profile·인구는 동일한 시설 목록과 배치를 만든다. — 시설 목록·마일스톤 키 결정론은 정적 통과 (배치 좌표 결정론은 V4 슬롯 배치 구현 후 플레이 모드에서 실측)
- [ ] 생성된 모든 일반·특수 시설에서 실제 상호작용을 시작할 수 있다. — Shop/Forge/Inn/Shrine의 `ServiceUIRouter` 라우팅 계약은 정적 대조 통과. VillageHub(100) UI 라우팅은 V4 범위이고 실측은 플레이 모드 필요라 미체크 (V4/V7 연계)

##### V3 구현·검증 결과 (2026-07-14)

- 성장 명세: `VillageGrowthPlanCatalog`가 Profile 66개의 `GrowthPlanId`별 명시적 마일스톤 목록을 코드로 정의한다. **Stage 기본 Cap(50/75/100/150/200 초안)·상점 Tier(1/2/3/4)·시설 목록은 테이블이 아니라 코드 상수로 구현했다** — `VillageStageTable`은 Google Sheets(`DownloadTables`)에서 재생성되므로 V2 임계값과 같은 정책이며, §5.1의 `BaseCap_*`/`DefaultShopTier` 컬럼 추가안은 적용하지 않았다. Cap은 마일스톤에 이전 Stage 대비 증가분으로 기록해 §4.3 `StageBaseCap + ProfileModifier + FacilityBonus` 합성이 성립한다.
- 초기 시설 허용 목록: V0 InteractiveFacility 계약 그대로 `VillageInitialFacilityCatalog` 5종(100 VillageHub·151 MerchantStall·153 Inn·160 Forge·170 Shrine)만 허용한다. 각 항목은 상호작용 ID·프롬프트·제공 기능·서비스 라우팅 요건을 가지며, `ValidatePlan`이 실제 `BuildableItemTable`(SpawnType=Entity, ProvidedService 비트)과 대조해 위반 시 실패 처리한다. Woodpile(110)·TownPost(152)·Anvil(161) 등 Retire 항목은 검증 실패로 완공 원장이 생성되지 않는다(행 삭제 없음).
- 적용 경로: `HistoryManager.BootstrapNewGameVillageDevelopment`가 V2 `ApplyFixedInitialState` 직후 `VillageInitialDevelopment.ApplyInitialMilestones`를 호출한다. 자원·건설시간·진행 task 없이 `VillageConstructionLedger.ApplyCompletedBuild` 단일 계약으로 완공 원장을 만들고, `VillageData.AppliedGrowthMilestones`(MilestoneKey)가 중복 적용을 차단한다. 시설은 좌표 미할당 + `PlacementPending` 완공 레코드로 저장되어 첫 materialize의 기존 경로(`RestorePlacedObjectsFromData`)가 배치·재시도한다 — §5.3 `PendingInitialPlacements`는 별도 리스트로 중복 저장하지 않고 이 상태를 정본으로 사용한다.
- Tier 교체·흡수: Town/City의 `UpgradeFacility`는 새 건물을 추가하지 않고 같은 `PlacedObjects` 레코드의 `FacilityTier`(신규 저장 필드, 구 세이브 0=1로 간주)만 2/3으로 올린다. Tier별 교체 자산·기능 차이는 V4 범위.
- Profile 연결: V0 선결대로 절차적 Dwarf Profile 10종에 주 특수 시설 160(단일 제련소)을 연결했다(저장 ID 불변, 정의만 확장 — V1 계약). 다른 Race와 수제작 BaseTown Profile은 기능 미확정 시설을 만들지 않는 원칙에 따라 특수 시설 없음으로 유지하며 V6에서 채운다. `ShopTier`는 `VillageData` 신규 필드로 해금 상태만 저장한다(재고·가격은 V5).
- 검증: `Assembly-CSharp`/`Assembly-CSharp-Editor` 컴파일 오류 0건(dotnet msbuild). Unity 외부 정적 하니스(`Temp/V3StaticHarness`, 실제 `BuildableItemTable.bytes` 파싱) 15개 assertion 통과 — 시설 계약·전체 명세 66개 검증·Stage별 누적 전수·Tier 교체·자원 0 적용·1회 적용·JSON 왕복·구 세이브 호환·비상호작용 거부·누적 무결성·결정론·10,000h 동결 advance 불변·Cap 합성. V2 하니스 29개 assertion도 회귀 없이 통과.
- Editor 진단: `ARPG/Village/Run V3 Initial Facilities Diagnostics`가 검증 전체 + seed 12345 마을별 마일스톤·시설(Tier)·ShopTier·Cap·Pending 표본을 `Temp/VILLAGE_V3_INITIAL_FACILITIES_DUMP.md`로 덤프한다(연대기 재현은 Editor 전용 — `Mathf.PerlinNoise` icall). 실제 청크 materialize 배치·UI 상호작용 실측은 플레이 모드 전용 제한으로 리포트에 기록된다.
- Editor 진단 실행 결과 (2026-07-14, Unity 6000.4.2f1): 14/14 통과. seed 12345 운영 정착지 1,096개 전수에서 초기 마일스톤 데이터 검증 오류 0건, 반복 적용 결정론 통과. 초기 Stage 분포 Settlement 11.9% / Hamlet 18.2% / Village 35.0% / Town 30.7% / City 4.1% — V0 확정 목표 분포(10.0/20.0/34.8/30.3/5.0%)와 오차 2%p 이내로 임계값이 의도대로 동작함을 확인했다.

---

### V4 — Dwarf 배치 수직 슬라이스

#### 주요 코드·콘텐츠 접점

- `Assets/Scripts/Village/VillageTileFinder.cs`
- `Assets/Scripts/Common/System/System_VillageBuildQueue.cs`
- `Assets/Scripts/Map/MapFileData.cs`
- `Assets/_BinaryData/TilemapData/BaseTown_Dwarf.bytes`
- 시설별 Addressable 스프라이트/타일

#### 작업

- [x] Dwarf LayoutProfile의 중심·시장·여관·제련소 슬롯 정의 (`VillageLayoutCatalog` — V1 `LayoutProfileId` 연결, 상대 오프셋 고정으로 결정론)
- [x] 역할 슬롯 우선 배치와 작은 반경 fallback 구현 (`VillageSlotPlacer` + `VillageManager.FindInitialPlacementTile` — 실패 시 PlacementPending 유지, 전역 점수 탐색으로 우회하지 않음)
- [x] 시장·여관·제련소의 단일 건물 Entity와 상호작용 프롬프트 구현 (VillageHub(100) 진입점 = 체크포인트 세이브, `System_VillageInteractionPrompt`가 "[F] 프롬프트" 표시 — 화면 실측은 플레이 모드)
- [x] 제련소 Tier별 교체 건물 자산과 기능 차이 정의 (FacilityTier 1/2/3 = 분해/재롤/추가+할인 UIForge 게이트, 자산은 `{ResourceName}_T{n}` 키 우선 + 기본 자산 fallback — Tier 아트 2종은 후속 제작 결정)
- [x] Settlement·Hamlet·Village·Town 초기 완성형 기능 건물 슬롯 구성 제작 (Stage별 콤팩트 Bounds 11×11→13×13→14×14)
- [x] 도로·광장 폭 축소 (Dwarf 레이아웃: 광장 3×3, 큰길 폭 1타일 — 기존 Stage 테이블 폭 3·반경 4~10 대비 축소, fallback 탐색에만 적용)
- [x] 벽 정책 적용 (Dwarf 청사진 장식 경계 Id 1 로드 시 제거 + Hamlet 이상 완공 Palisade/Gate 세그먼트 생성 + HP 피해 경로·Gate 개폐 — 충돌·개폐 실측은 플레이 모드)
- [x] 기능 건물 수·기능 구역 크기·건물 간 이동 거리 측정 (정적: Town 기능 구역 14×14, 시설 4종, 남쪽 입구→최원거리 시설 체비쇼프 9)
- [ ] 기존 청사진과 동일 카메라 조건의 비교 스크린샷 확보 — Unity 플레이 모드 필요 (아래 시각 검증 절차)

#### 검증 기준

- [x] 모든 공통 서비스가 입구/중심에서 과도하게 멀지 않다. (정적: 남쪽 Gate→시장/여관 5, 허브 7, 제련소 9타일. 시각 확인은 스크린샷 절차 연계)
- [ ] 기능 시설이 서로 구분되지만 비상호작용 건물 때문에 번잡해 보이지 않는다. — Unity 시각 검증 필요
- [ ] 화면에 보이는 모든 일반·특수 건물은 일관된 표시로 상호작용 가능 여부를 알 수 있다. — 프롬프트 시스템 구현 완료, 플레이 모드 실측 필요
- [x] Furnace·Anvil·QuenchVat 같은 외형 전용 건물 오브젝트가 별도로 생성되지 않는다. (정적 하니스: 전체 Stage에서 161/162 미생성, 제련소 단일 레코드)
- [x] 각 초기 Stage의 누적 시설이 서로 겹치지 않는다. (슬롯 고유성 + 벽·시설 비겹침 정적 통과 — 실제 맵 점유는 플레이 모드 확인)
- [x] 시설 배치 실패 시 마일스톤이 유실되지 않는다. (슬롯·fallback 전체 점유 시 PlacementPending 유지 — 완공 원장 불변)
- [x] 청사진 경계와 런타임 벽이 중복되지 않는다. (Dwarf: 경계 Id 1 로드 시 제거 + 벽은 세그먼트 정본에서만 생성. 타 Race·BaseTown: 벽 미생성으로 기존 경계 유지)

##### V4 구현·검증 결과 (2026-07-14)

- 레이아웃: `VillageLayoutProfile`/`VillageLayoutCatalog`가 V1 `LayoutProfileId`를 해석한다. 절차적 Dwarf 레이아웃 10종(`layout.dwarf.*`)만 콤팩트 기능 구역(Settlement 11×11 / Hamlet 13×13 / Village 이상 14×14)과 기능성 벽을 갖고, 그 외 Profile(수제작 BaseTown 포함)은 같은 슬롯 배치기를 쓰되 Bounds·예약은 기존 Stage 테이블 값을 유지하고 벽을 만들지 않는다(Race variant는 V6). 슬롯은 중심 기준 고정 상대 오프셋 — Hub(0,0)·시장(-3,-2)·여관(3,-2)·주 특수 시설(3,2)·Shrine(-3,2)·보조(4,4). N-S 큰길 축과 광장 3×3은 fallback 후보에서 예약 제외된다.
- 배치 계층: materialize의 미할당 완공 레코드 배치가 `FindMaterializationTile`(VillageTileFinder 최근접 탐색)에서 `FindInitialPlacementTile`(슬롯 우선 + 슬롯 주변 반경 3 고정 순서 fallback, RNG 없음)로 교체됐다. 허용 목록 밖 TableId(구 세이브 일반 오브젝트)만 기존 최근접 탐색을 유지한다. 배치 실패 시 기존 `PlacementPending` 재시도 계약이 그대로 유지되고 마일스톤은 유실되지 않는다. V3 마일스톤 적용 로직은 수정하지 않았다(배치 계층만 교체).
- 벽 정책(1안): `BaseTown_Dwarf.bytes`는 Unity 씬 Tilemap + `MapFileSaver` 인스펙터로만 재생성되는 자산이라 수정하지 않고, `LoadRaceBlueprint`가 Dwarf 로드 시 장식 경계 Id 1(70셀)의 오브젝트 비트를 결정론적으로 제거한다. `VillageWallRules.ApplyInitialCompletedWalls`가 Hamlet 이상 초기 Stage에서 Bounds 둘레 완공 Palisade/Gate 세그먼트(만HP, Gate 2개 N/S)를 자원·건설시간·task 없이 생성하고, 타일 배치는 materialize(`RestoreWallTilesFromData`, 멱등)가 담당한다. Tile 피해 경로는 `VillageManager.ApplyWallDamage`(HP 차감→0에서 파괴·타일 제거·Completed 감소), Gate 개폐는 `ToggleGate`(열림=타일 제거로 통행, 닫힘=타일 재배치, 플레이어가 문 위면 거부)로 구현했다. 공격자(몬스터 AI)가 벽을 때리는 연동은 침공 콘텐츠 범위로 미구현이며 피해 경로 API·상태 계약만 정본화했다.
- 상호작용: `System_VillageServiceProximity`가 VillageHub(TableId 100, 테이블 ProvidedService 비변경)와 근접 Gate 후보를 추가 수집하고, `ServiceUIRouter.TryGetBestCandidate`(프롬프트·F키 공용 선택기)가 Shop/Forge/Inn/Shrine/Hub/Gate/NPC 중 최근접을 고른다. Hub 상호작용 = 체크포인트 세이브(`DataManager.Save`) + 알림. 신규 `System_VillageInteractionPrompt`(Priority 62)가 코드 생성 월드 캔버스로 "[F] {프롬프트}"를 대상 위에 표시한다 — 프롬프트에 보이는 대상과 F키가 여는 대상이 항상 같다. 구 세이브 Campfire도 같은 TableId라 자동으로 거점 상호작용을 얻는다(V0 Convert 계약).
- 제련소 Tier: `PlacedObjectComponent.FacilityTier`/`BuildingSaveData.FacilityTier`로 저장 Tier가 런타임에 전파되고, UIForge가 Tier 1/2/3 → 분해/·재롤/·추가+비용-20% 기능 게이트로 소비한다(FacilityTier 0인 구 세이브만 기존 Furnace+Anvil+QuenchVat 세트 평가 fallback). Tier 교체 자산은 같은 Entity에서 `{ResourceName}_T{n}` Addressable 키를 우선 시도하고 없으면 기본 자산으로 대체한다(모듈 증식 없음). **신규 아트 4종(Furnace_T2/T3, 열린 Gate 타일, VillageHub 전용 스프라이트)은 임시 대체(기존 자산 재사용)로 진행하기로 결정** — 키 등록만으로 코드 수정 없이 반영된다.
- 검증: `Assembly-CSharp`/`Assembly-CSharp-Editor` 컴파일 오류 0건(dotnet msbuild). 신규 정적 하니스(`Temp/V4StaticHarness`, 실제 `BuildableItemTable.bytes` 파싱) 18개 assertion 통과 — 레이아웃 해석 66 Profile·슬롯 결정론·fallback 결정론(100회 반복)·전체 점유 시 Pending 유지·Stage별 초기 벽(둘레 수·Gate 2·Bounds 정합·슬롯 비겹침·재적용 무변화)·벽 피해·Gate 개폐 왕복·Dwarf Town 통합·§7.4 측정(기능 구역 14×14, 입구→시설 ≤9)·JSON 왕복(IsOpen·FacilityTier)·Tier 자산 키·161/162 미생성. V2(29)·V3(15) 하니스 회귀 없음.
- Editor 진단: `ARPG/Village/Run V4 Layout Diagnostics`가 레이아웃·청사진 경계 측정·Stage별 슬롯 배치 시뮬레이션·§7.4 밀도 비교표를 `Temp/VILLAGE_V4_LAYOUT_DUMP.md`로 덤프한다. 실제 청크 배치·벽 충돌·프롬프트·비교 스크린샷은 플레이 모드 전용 항목으로 리포트에 기록된다. V3 미체크 2건(materialize 반복 무중복, 전 시설 상호작용 실측)은 V4 플레이 모드 검증 절차에 포함한다.

---

### V5 — 지역 상권

#### 주요 코드 접점

- `Assets/Scripts/Common/Tables.cs`의 `ItemTable`
- `Assets/Scripts/Village/MerchantStockEntry.cs`
- `Assets/Scripts/Village/VillageManager.cs` 상점 API
- `Assets/Scripts/UI/UIShopMerchant.cs`
- `Assets/Scripts/History/Generator/DerivedSeed.cs`
- `Assets/Scripts/Manager/DataManager.cs` 세이브 마이그레이션

#### 작업

- [x] TradeTag 데이터 추가 (`TradeTag` Flags + `ItemTradeTagCatalog` — **사용자 결정에 따라 시트 컬럼이 아닌 코드 카탈로그**. §8.2 예시 태그 전부 정의, 현 아이템 83종 중 존재 품목에만 매핑. 시트 컬럼 이전은 V6/V7 검토)
- [x] VillageTradeProfile 데이터 추가 (`VillageTradeProfileCatalog` — 보장 품목 그룹·특산/희귀 가중 풀·태그별 bp 배율·수량 범위·재입고 시간·ShopTier별 슬롯·특수 시설 추가 풀. V5 범위 제한대로 아키타입은 Dwarf 광산 + 비-Dwarf 공통 2종, 66개 TradeProfileId 전부 해석 — 저장 ID 불변, 정의만 채움(V1 계약))
- [x] 보장/특산/희귀 슬롯별 재고 생성 (§8.4 — ShopTier1 = 공통 2+특산 2+희귀 1. 특산 풀이 Tier 게이트로 비면 일반 품목 fallback으로 슬롯 수 유지)
- [x] `UnityEngine.Random` 재고 롤을 결정론 RNG로 교체 (`VillageTradeStockRoller` + `HistoryRng`. `ItemManager.CreateInventoryItemData`에 선택적 `HistoryRng` 주입 — 장비 Mod/Quality·스킬북 SkillId·페이지 EffectId 인스턴스 롤까지 같은 시드 채널. 미주입 호출(드랍 등)은 기존 동작 유지)
- [x] Profile·Stage·특수 시설 Tier를 재고 풀에 반영 (Stage는 직접 쓰지 않고 V3 `VillageData.ShopTier` 누적 결과를 소비 — 재계산 없음. `PlacedObjects[].FacilityTier`(제련소 160)가 §8.3 추가 풀 게이트)
- [x] 플레이어 구매가와 상인 매입가 분리 (BasePrice × SellToPlayerBp/10000, × BuyFromPlayerBp/10000. 기존 `SellRatioBp`(÷100 퍼센트)는 매각 가능 플래그로만 유지 — 의미 분리)
- [x] `MerchantStockEntry` 가격 스냅샷/회차 저장 (`UnitPrice`·`RestockCycle`·`TradeProfileId`·`SlotKind`·`PriceReason` + `VillageData.MerchantRestockCycle` — 재롤 직후 세이브 없이도 같은 회차 재현)
- [x] UI에 지역 가격과 사유 표시 (UIShopMerchant 구매가 = 스냅샷, `지역 특산`/`현지 부족`/`희귀 수입` 병기, 매각 탭 매입가·사유 — 화면 실측은 플레이 모드)
- [x] 기존 MerchantStock 구 포맷 마이그레이션 또는 안전한 재롤 (§11.5 — **유실 없음**: 가격 필드 없는 매물은 로드 시 진열품을 유지하고 가격·사유만 Profile로 스냅샷. `MerchantRestockCycle=-1` 구 세이브는 기존 24h 게이트가 지나면 결정론 롤로 자연 전환)

#### 검증 기준

- [x] 같은 마을·같은 재입고 회차는 save/load 후 같은 재고를 만든다. (정적: 같은 (mapSeed, SettlementId, 회차) 100회 반복 동일 계획 + JSON 왕복 스냅샷 보존. ItemData 인스턴스(Mod/Quality) 결정론은 같은 rng 체인으로 설계 — Unity 실측은 플레이 모드)
- [x] 서로 다른 Profile의 품목과 가격 분포가 통계적으로 구분된다. (정적: 200회차 표본에서 Dwarf 광물+중장구 1,155건 vs 비-Dwarf 0건, 같은 스킬 북 구매가 230G vs 200G)
- [x] Dwarf 광산촌→부족 지역 운송에서 제한된 양수 차익이 존재한다. (정적: 철 +5G/개·구리 +2G/개, TradeGood 재고 4~8개로 규모 제한, 역방향·비-Dwarf 광물 미진열로 순환 차단 — 실제 왕복 플레이 실측은 플레이 모드)
- [x] 일반 장비 왕복 구매·판매로 무한 차익이 생기지 않는다. (정적: 전 66 프로필 × 전 품목에서 같은 상점 매입가 ≤ 구매가, 非TradeGood 전 프로필 최대 매입가(4,500bp) ≤ 최소 구매가(8,500bp))
- [x] 초기 Stage와 특수 시설 Tier가 높은 마을은 첫 재입고부터 해당 풀이 열린다. (정적: 회차 0에서 City(ShopTier4·제련소 T3)가 T3 품목·특수 시설 풀 즉시 개방, 제련소 T1은 닫힘)

##### V5 구현·검증 결과 (2026-07-15)

- TradeTag 저장 방식: 시트 컬럼(§8.2 원안)과 코드 카탈로그를 비교 보고 후 **코드 카탈로그로 사용자 확정**. TableData .bytes는 `DownloadTables`로 재생성되므로 V2~V4 임계값·Cap·GrowthPlan과 같은 정책이다. `ItemTradeTagCatalog`가 ItemId→`TradeTag`(Flags: Ore/HeavyArmor/Arcane/Ranged/Herb/Poison/Cold/TravelSupply/TradeGood/RareImport)를 정의하며, 현 카탈로그에서는 광물(2002·2003)=Ore|TradeGood, 금속 방어구 9종=HeavyArmor, 지팡이·완드·마법서·마법사 방어구 13종=Arcane, 원거리 8종=Ranged, 스킬 북/상위 페이지=Arcane|RareImport다.
- 상권 프로필: `VillageTradeProfileCatalog`가 `VillageProfileCatalog`의 66개 `TradeProfileId` 전부를 해석한다(저장 ID 불변 — V1 계약). V5 범위 제한대로 아키타입은 2종 — **Dwarf 광산**(보장 철·구리·일반 페이지, 특산 Ore w3+HeavyArmor w2, 희귀 RareImport w2+Arcane w1, 제련소 Tier2+에서 HeavyArmor w3+Ore w2 추가 풀, TradeGood 8000/3000bp·특산 8500/4000bp·Arcane 11500/4500bp)과 **비-Dwarf 공통**(보장 페이지/북, 특산 일반 품목 w3+Ranged w1, 희귀 RareImport w2+Arcane w1, TradeGood 15000/13000bp). 기본 배율 10000/4000bp, 재입고 24h, 슬롯은 ShopTier 1~4 = 5/6/7/8(보장 2~3+특산 2~3+희귀 1~2), 아이템 Tier 해금 = min(ShopTier, 3). Race별 세분화·역할(TradeHub 등) 결합은 V6.
- 결정론 롤: `VillageTradeStockRoller`(순수 정적 로직)가 `DerivedSeed.ChannelMerchantStock ^ SettlementId ^ RestockCycle` 채널에서 파생된 `HistoryRng`로 계획(품목·수량·확정 가격·슬롯 종류·사유)을 만들고, `VillageManager.RollMerchantStock`이 같은 rng를 이어 `ItemManager.CreateInventoryItemData(id, 1, rng)`로 인스턴스를 완성한다 — `UnityEngine.Random` 재고 경로 제거(§8.5). BaseTown(SettlementId<0)은 `-1 - VillageId`를 정착지 키로 사용한다. 재입고 게이트는 `MerchantRestockCycle`(게임시간/재입고 간격 몫) 비교로, 같은 회차 재진입·재롤은 항상 같은 결과다. 회차 내 구매 잔량 감소는 저장 매물이 정본이라 유지된다.
- 가격 분리: 플레이어 구매가 = `BasePrice × SellToPlayerBp / 10000`(최소 1G, roll 시 `MerchantStockEntry.UnitPrice`로 확정), 상인 매입가 = `BasePrice × BuyFromPlayerBp / 10000`(`VillageManager.GetMerchantBuyPrice`). 기존 `SellRatioBp`(÷100)는 매각 가능 플래그로만 남겨 의미 혼동을 제거했다. UIShopMerchant는 스냅샷 가격과 사유(지역 특산/현지 부족/희귀 수입)를 병기한다.
- 구 세이브(§11.5): WorldData 버전 증가 없음(V7 범위 준수). `VillageManager.Load`의 `BackfillLegacyStockPriceSnapshots`가 가격 필드 없는 매물의 진열품을 유지한 채 가격·사유만 Profile로 스냅샷한다 — **유실 없음**. Item=null 구구(V2) 포맷 폐기·24h 경과 재롤은 기존 정책 그대로다.
- 검증: `Assembly-CSharp`/`Assembly-CSharp-Editor` 컴파일 오류 0건(dotnet msbuild). Unity 외부 정적 하니스(`Temp/V5StaticHarness`, 실제 `ItemTable.bytes` 파싱 + 컴파일된 Assembly-CSharp 참조) **27개 assertion 통과** — 카탈로그 66 해석·보장 품목 유효·회차 계산·100회 결정론·회차/정착지 분리(각 20/20 고유)·보장 슬롯·ShopTier별 슬롯 수와 Tier 게이트·분포 구분·가격 스냅샷 재계산 일치·사유 규칙·왕복/운송 차익 불변식·TradeGood 차익과 규모 제한·특수 시설 풀 게이트(제련소 T2 개방 63/100회차·T1 닫힘)·첫 재입고 개방·BaseTown 아키타입 공유·JSON 신규/구 포맷 왕복. 참고: V2~V4 하니스 폴더는 Unity의 Temp 정리로 소실되어 재실행하지 못했고, 회귀는 전체 컴파일과 기존 `ARPG/Village/Run V2~V4` Editor 진단으로 확인한다.
- Editor 진단: `ARPG/Village/Run V5 Trade Diagnostics`가 위 검증 전체 + 분포/가격 표본을 `Temp/VILLAGE_V5_TRADE_DUMP.md`로 덤프한다(V0~V4 진단과 같은 패턴, 메뉴 425).
- **2026-10-03 후속 (NPC 상점 기본 장비)**: `RollMerchantStock`이 기존 계획 뒤에 `VillageTradeStockRoller.RollBasicEquipmentPlan`으로 기본 장비 칸을 이어 롤하고 `ItemManager.CreateBasicItem`으로 인스턴스를 만든다(§8.4). 이번 회차를 옛 코드로 롤한 라이브 재고 37개 마을과 기존 슬롯 결과가 같음을 확인했다. NPC 구매 필터·회차 한도는 [NPC_AMBITION_DESIGN](../npc/NPC_AMBITION_DESIGN.md) §7.2.
- Editor 진단 실행 결과 (2026-07-15, Unity 6000.4.2f1, 메뉴 실행): **8/8 통과**. 분포 표본(Town ShopTier3, 200회차)에서 Dwarf는 보장 철·구리 200/200 + 중장구 9종이 상위를 채우고 비-Dwarf는 보장 페이지·북 200/200 + 희귀 풀 상위 페이지·북·마법서로 상위 품목이 완전히 갈렸다(일반 장비는 미태그 풀 ~40종에 분산). 가격 표본 검산 — 철 Dwarf 구매 8G→비-Dwarf 매입 13G(+5G/개), 구리 4G→6G(+2G), 철 갑옷 Dwarf 특산 51G/매입 24G(왕복·운송 손해), 마법사 로브 103G·스킬 북 230G(Dwarf 현지 부족 프리미엄), 가죽 조끼 30/12G(표준) — 전부 bp 규칙과 일치.
- 플레이 모드 검증 절차(V7 통합 검증 연계, V4 미체크 항목과 별개로 상점 항목만 추가): ① Dwarf 마을 시장에서 상점 UI를 열어 스냅샷 가격·사유 표기(광물 지역 특산 할인, 스킬 북 현지 부족 프리미엄, 희귀 슬롯 표기)와 구매 차감액 일치 확인 ② 재롤 직후 세이브 없이 종료→재로드→같은 회차 재진입 시 같은 진열(품목·수량·가격·장비 Mod) 확인 ③ 구 세이브 로드 시 기존 진열 유지 + 가격 보정 로그 확인 ④ Dwarf 광산촌에서 철/구리 구매 → 비-Dwarf 마을 매각으로 양수 차익, 일반 장비 왕복은 손해 확인 ⑤ Town/City 첫 방문 상점에서 T2/T3 품목·특수 시설 풀 등장 확인.

---

### V6 — Race/Biome Profile 확장

> **V6 사용자 결정 (2026-07-15)**: 신규 UI/시트 행 없이 재사용 가능한 시설 서비스가 Forge·Shrine뿐이고
> Shrine 버프는 미배선(TODO)이라, 주 특수 시설은 **Dwarf(제련소 160)만 확정**한다. 나머지 Race는 특수 시설
> 없음(0)으로 두고 **상권(TradeProfile)과 배치(Layout)로 차별화**하며, 특수 시설은 확장점만 유지한다
> (신규 UI·시트 행·건물은 이번 범위에서 만들지 않음). 따라서 "모든 Profile이 주 특수 시설 보유" 검증 기준은
> 의도적으로 미달로 남긴다. Trade 재료는 물약·약초 아이템이 없어 실태그 5종(Ore/HeavyArmor/Arcane/Ranged/
> RareImport) 순열로만 세분화한다. Layout은 슬롯 오프셋만 Race별 변주(콤팩트/벽 없음). 역할은 가벼운 실제
> 효과(TradeHub 상권 강화·Capital Cap 상향) + Frontier/Conquered 확장점.
>
> **후속 변경 (2026-08-30)**: 위 Dwarf 전용 결정은 당시 구현·검증 이력으로만 유지한다. 마법 부여 MVP 구현 시 Forge 160을 모든 신규 게임 마을의 최소 Tier 1 공통 접근 서비스로 승격한다. 상세 계약은 `ITEM_ENCHANTMENT_DESIGN.md` §5.1을 따른다.

#### 작업

- [~] Human/Elf/Orc/Nomad/Lizardman/Yeti 주 특수 시설 — **결정: 특수 시설 없음(0) 유지, 확장점만 정리** (위 결정). `GetPrimarySpecialFacilityId` 주석에 신규 시설 추가 절차 명문화. 기능 미확정 시설을 외형만 만들지 않음.
- [x] Dwarf 수직 슬라이스 보정·확정 — Forge 160 주 특수 시설·layout.dwarf.mining(콤팩트+벽)·제련소 Tier 풀 V4 값 유지 확인, 회귀 없음.
- [x] Profile별 TradeTag/가격 배율 확정 — `VillageTradeProfileCatalog`를 2 아키타입에서 Race별 7 빌더로 확장. 특산/부족/보장/배율 순열. `ItemTradeTagCatalog`(실태그) 불변.
- [x] Profile별 Layout variant 최소 1종 — `VillageLayoutCatalog`에 非Dwarf 6 Race variant(슬롯 오프셋 상이, 벽 없음·기존 Bounds) 추가 + `Resolve` race 토큰 분기. `VillageSlotPlacer`·`VillageWallRules` 로직 불변.
- [x] Capital/Frontier/TradeHub 역할 결합 규칙 초안 — TradeHub=수입품 유입+광물 매입 프리미엄(14000bp), Capital=Village+ 자원 Cap 상향(누적 +50), Frontier/Conquered=주석 확장점.

#### 검증 기준

- [~] 모든 Profile이 공통 시설 3개 이하 + 주 특수 시설을 가진다. — **의도적 부분 충족**: 공통 시설 ≤3(Hub/시장/여관)은 전 Profile 충족, 주 특수 시설은 Dwarf만(사용자 결정). 非Dwarf는 상권·배치로 대체 차별화.
- [x] 특수 시설 없이 바닥 테마만 다른 마을이 없다. — 非Dwarf는 상권(품목·가격)+배치(슬롯 오프셋)로 구분되며 바닥 테마만 다르지 않다(정적 하니스 확인).
- [x] 같은 Race라도 역할 Profile을 추가할 확장 지점이 있다. — 역할별 Profile ID·Capital Cap·TradeHub 상권이 실제 갈리고 Frontier/Conquered 확장점 주석.
- [x] 마을별 판매 품목·가격·서비스 중 최소 2개 축이 구분된다. — 품목(특산 태그별 진열)+가격(특산/부족 배율) 2축이 Race별로 구분됨(하니스 검증).

##### V6 구현·검증 결과 (2026-07-15)

- 특수 시설: 사용자 결정대로 Dwarf(160)만 확정, 나머지 Race `PrimarySpecialFacilityId=0` 유지. [VillageProfile.cs](../../Assets/Scripts/Village/VillageProfile.cs) `GetPrimarySpecialFacilityId` 주석에 "신규 특수 시설 UI+BuildableItemTable 행 확보 시 여기 Race→ID 분기 + `VillageInitialFacilityCatalog` 계약 등록 → GrowthPlan 자동 마일스톤(V3 로직 불변)" 확장 절차를 명문화. Shrine·신규 UI·시트 행·건물 미추가. Dwarf 수직 슬라이스(V4)는 값 변경 없이 확인.
- Trade: [VillageTradeProfile.cs](../../Assets/Scripts/Village/VillageTradeProfile.cs) `CreateProfile`을 Race 분기로 확장하고 Dwarf/Elf/Orc/Nomad/Human/Lizardman/Yeti 7 빌더를 추가했다. 실태그 5종 + 무태그 일반 풀의 "특산(할인 8500bp)·부족(프리미엄 11500bp)·보장 품목·가격 배율" 순열로 세분화 — Dwarf(광물·중장구 특산/비전 부족), Elf(비전 특산/중장구 부족), Orc(원거리 특산/비전 부족), Nomad(수입품 특산/중장구 부족), Human(중립·균형), Lizardman(일반품 특산/중장구·비전 부족), Yeti(중장구 특산/비전 부족). V5 밴드(非TradeGood 매입 ≤4500 ≤ 판매 8500, TradeGood Dwarf 8000/3000↔非Dwarf 15000/13000)를 유지해 66프로필 차익 불변식이 성립한다. `ItemTradeTagCatalog`·`VillageTradeStockRoller`·시드 채널·`MerchantStockEntry` 스키마 불변.
- Layout: [VillageLayoutProfile.cs](../../Assets/Scripts/Village/VillageLayoutProfile.cs) `VillageLayoutCatalog`에 非Dwarf 6 Race variant를 추가하고 `Resolve`를 race 토큰 분기로 바꿨다. 各 variant는 시설 슬롯 상대 오프셋만 상이(7종 시장 슬롯 오프셋 전부 고유)하고 `usesCompactBounds=false`·`hasFunctionalWalls=false`로 기존 Stage 테이블 Bounds·벽 없음을 유지한다. Dwarf(`layout.dwarf.*`)는 콤팩트+벽(V4), BaseTown(`layout.base_town.dwarf`)은 공통 기본을 그대로 유지. `VillageSlotPlacer`·`VillageWallRules` 로직 미수정(데이터만).
- 역할: TradeHub는 `VillageTradeProfileCatalog`가 특산 풀에 수입품(RareImport)을 더하고 광물 매입을 14000bp로 올린다(판매 15000 미만 유지). Capital은 `VillageProfileCatalog`의 `StageCapModifiers`를 Village+20/Town+35/City+50으로 채워 GrowthPlan이 자동 합성한다(최종 Food Cap = Standard +50). Frontier/Conquered는 주석 확장점.
- 검증: `Assembly-CSharp`/`Assembly-CSharp-Editor` 컴파일 오류 0건(dotnet msbuild). Unity 외부 정적 하니스(`Temp/V6StaticHarness`, 실제 `ItemTable.bytes` 파싱 + 컴파일된 Assembly-CSharp 참조) **30개 assertion 통과** — 카탈로그 66 해석·66프로필 차익 불변식·非TradeGood 밴드(4500/8500)·Race별 가격 구분(Elf/Orc/Yeti/Nomad)·Race별 분포 구분·非Dwarf 광물 미진열·TradeGood Dwarf→非Dwarf 6종 양수 차익·TradeHub 매입 프리미엄·Capital Cap +50·가격 사유·Layout variant(Dwarf 콤팩트+벽/非Dwarf 벽 없음/7종 오프셋 고유/BaseTown 기본)·Resolve 결정론·슬롯 배치 결정론·결정론 롤 회귀·JSON 왕복·구 포맷 로드. 참고: V5 하니스의 Human 분포/보장 가정은 V6 데이터 세분화로 더 이상 성립하지 않아(설계상 변경) V6 하니스로 대체했다.
- Editor 진단: `ARPG/Village/Run V6 Profile Diagnostics`(메뉴 426)가 위 검증 + Race별 가격·상위 진열·Layout 오프셋 표본을 `Temp/VILLAGE_V6_PROFILE_DUMP.md`로 덤프한다(V0~V5 패턴). Editor가 프로젝트를 점유 중일 수 있어 이번 세션에서는 batchmode 실행하지 않았다(메뉴에서 실행 가능). 상점·배치 시각 실측은 플레이 모드 전용으로 리포트에 절차 기록.
- 플레이 모드 검증 절차(V7 통합 검증 연계): ① Race별 마을 첫 방문 시 상점 품목·가격 사유가 갈리는지(Elf 비전 할인·Dwarf 비전 프리미엄·Orc 원거리 할인 등) ② Race별 시장/여관/허브 슬롯 위치가 화면상 구분되는지 ③ TradeHub 마을 광물 매각 프리미엄·Capital 마을 자원 Cap 상향 확인.

---

### V7 — 마이그레이션·통합 검증·정본 갱신

> **V7 범위 결정 (2026-07-15)**: 사용자가 세이브 파일을 지우고 New Game으로 새로 시작하기로 결정해
> **구 세이브 마이그레이션은 이번 범위에서 생략**한다. WorldData 버전 증가(현행 12 유지)와 구조 backfill
> 코드를 만들지 않으며, 아래 마이그레이션 항목(§11)과 §14 완료 정의 #10은 "구 세이브 없음으로 적용 제외"로
> 처리한다. V7은 **New Game 완성형 마을의 통합 검증·정본 갱신**에 집중한다. 근거·조사 보고는 이 세션의
> 세이브 스키마 확인 결과와 §11 주석 참조.

#### 작업

- [~] WorldData 버전 증가와 구 세이브 마이그레이션 — **제외**(세이브 신규 시작 결정). 버전 12 유지, 마이그레이션 코드 미작성.
- [~] 구 세이브의 정착지별 인구를 1회 고정 스냅샷으로 확정하고 `LastSimGameTime` 정렬 — **제외**(구 세이브 없음). New Game은 V2에서 이미 인구 고정·clock 정렬.
- [~] History에 연결되지 않은 BaseTown의 고정 경제 인구 backfill — **제외**(구 세이브 없음). New Game/`FixedPopulation` 경로는 V1~V2에서 확정.
- [~] 기존 `PlacedObjectTypeIds`, `PlacedObjects`, `ActiveBuildTasks` 보존 — **제외**(구 세이브 없음). 로드 시점 방어 backfill은 기존 코드에 그대로 유지.
- [~] Retire 건물은 신규 후보에서만 제외하고 기존 배치는 유지 — **제외**(구 세이브 없음). Retire 제외 정책은 V0~V3에서 이미 신규 생성 경로에 적용.
- [~] 기존 Stage와 계산된 초기 Stage 중 높은 값을 마이그레이션 Stage로 채택하고 누적 마일스톤 backfill — **제외**(구 세이브 없음). New Game은 `InitialStage`를 그대로 사용.
- [~] 기존 MerchantStock을 새 포맷으로 안전하게 재롤 — **제외**(구 세이브 없음). V5 `BackfillLegacyStockPriceSnapshots`는 로드 경로에 그대로 유지.
- [x] `VillageSimulationParity`에 고정 인구/Profile/초기 Stage/누적 시설·상점 상태 비교 추가 (`CompareFixedState` — 추상/실시간 `Compare`와 별개 순수 비교기. 좌표 무관 (TableId,FacilityTier) 멀티셋·상점 스냅샷·마일스톤 멀티셋 비교)
- [x] New Game 초기화, 첫 materialize, 장기간 무변화, save/load와 fold-back 반복 검증 — 정적: New Game 완성형 재현(부트스트랩 미러) → save/load 왕복·동결 advance 10,000h·fold-back 5회 반복 후 `CompareFixedState` 불변·무중복. 실제 청크 첫 materialize는 플레이 모드(아래 절차).
- [ ] Unity 플레이 모드에서 실제 청크·Addressables·UI 검증 — 절차 정리 완료(`ARPG/Village/Run V7` 덤프 + 아래 체크리스트), 사용자 실행 대기.
- [x] 현행 정본 문서 갱신 (2026-07-15) — [VILLAGE_RUNTIME_DESIGN](VILLAGE_RUNTIME_DESIGN.md) §3(고정 인구)·§4(초기 완공·런타임 자동 건설 동결)·§6(런타임 승격 동결·`InitialStage`·초기 완공 벽), [ABSTRACT_CONSTRUCTION_DESIGN](ABSTRACT_CONSTRUCTION_DESIGN.md) §11(New Game 직접 확정+누적 마일스톤)·§13(`CompareFixedState`), [BUILD_PRIORITY_DESIGN](BUILD_PRIORITY_DESIGN.md)(후보 점수는 런타임 발전 시에만·초기 시설은 GrowthPlan), [VILLAGE_NPC_BOUNDARY_DESIGN](../npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) §2(고정 인구)를 현재 구현에 맞게 갱신. 플레이 실측 A~F는 아래 체크리스트로 별도.
- [x] 사용자 확인 후 `Wiki/village.md` 종합 내용 갱신 (2026-07-15, 사용자 확인 후 간결 반영 — §1 초기 완성형·Profile/상권, §3 마일스톤·`CompareFixedState`, §4 플레이 실측·V8 보류).

#### 검증 기준

- [~] 구 세이브 로드 시 예외가 없다. — **제외**(구 세이브 없음).
- [~] 기존 완공 건물과 자원 Cap이 사라지거나 중복되지 않는다. — **제외**(구 세이브 없음). New Game 무중복은 아래 정적 통과로 확인.
- [x] New Game과 save load 모두 Profile·고정 인구·초기 Stage를 가진다. (정적: 완성형 재현 + save/load 왕복 `CompareFixedState` 불변)
- [x] 장기간 경과와 materialize/fold-back 반복 후에도 고정 인구·Stage·시설 구성이 변하지 않는다. (정적: 동결 advance 10,000h + fold-back 5회 반복 불변. 실제 청크 materialize는 플레이 모드)
- [ ] 원거리에서 생성된 마을을 처음 방문했을 때 인구에 맞는 Stage·필수 시설·특수 시설·상점이 이미 완성돼 있다. — 정적: 완성형 데이터 재현 통과. 화면 실측은 플레이 모드.
- [x] materialize/fold-back 반복 후 초기 마일스톤과 재고가 중복되지 않는다. (정적: 마일스톤 재적용 신규 0 + fold-back 5회 무중복. 음성 검증으로 비교기 유효성 확인)

##### V7 구현·검증 결과 (2026-07-15)

- 범위: 사용자 결정으로 **구 세이브 마이그레이션 생략**, New Game 완성형 통합 검증에 집중. 세이브 스키마 조사 결과 — `WorldData.Version=12`([WorldData.cs](../../Assets/Scripts/Data/WorldData.cs)), `MigrateWorldData`는 각 스텝이 `LoadCompleted`/`OnLoadCompleted` 방어 backfill에 위임하는 비파괴 패턴([DataManager.cs](../../Assets/Scripts/Manager/DataManager.cs)), V1 Profile·V5 재고 backfill은 버전 무관·멱등으로 `VillageManager.Load`에 상주. V1~V6이 버전을 올리지 않아 디스크 세이브가 전부 v12라 파괴적 변경 리스크가 컸고, 신규 시작 결정으로 그 리스크를 제거했다.
- 파리티: [VillageSimulationParity.cs](../../Assets/Scripts/Village/VillageSimulationParity.cs)에 `CompareFixedState`(+헬퍼 3종) 추가. 기존 `Compare`(추상/실시간 자원·carry·task·clock·좌표)는 불변 — V2~V6 하니스 회귀 없음. 새 비교기는 Population·VillageProfileId·Stage·ShopTier·`MerchantRestockCycle`·`LastMerchantRollGameTime`·`AppliedGrowthMilestones`(순서 무관 멀티셋)·시설 (TableId,FacilityTier) 멀티셋·`MerchantStock` 스냅샷을 비교한다.
- 정적 하니스: `Temp/V7StaticHarness`(net10.0 콘솔, 컴파일된 Assembly-CSharp 참조, 실제 `ItemTable.bytes`/`BuildableItemTable.bytes` 파싱). `HistoryManager.BootstrapNewGameVillageDevelopment`의 마을당 처리(정량화→초기 Stage→Bounds→벽→누적 마일스톤→결정론 상점 롤)를 Unity 밖에서 재현하고 **17개 assertion 통과** — 초기 Stage 임계값·MaxStage 클램프, Dwarf City/Elf City/Dwarf Town 완성형(Stage·시설·마일스톤·ShopTier·매물·진행 task 0), save/load 왕복 불변(Dwarf City/Elf City/BaseTown), 마일스톤 재적용 신규 0·불변, 동결 advance 10,000h 후 불변(Dwarf/Elf), fold-back 5회 반복 불변·무중복, 원장 정합(진행 task 0 + `PlacedObjectTypeIds`==`PlacedObjects`), 음성 검증(비교기가 ShopTier·마일스톤·FacilityTier·상점가 차이 검출). 컴파일: `Assembly-CSharp`/`Assembly-CSharp-Editor` 오류 0건(dotnet msbuild).
- Editor 진단: `ARPG/Village/Run V7 Integration Diagnostics`(메뉴 427)가 같은 통합 불변식을 Unity 안에서 재실행하고 Dwarf/Elf 완성형 표본 + 아래 플레이 모드 체크리스트를 `Temp/VILLAGE_V7_INTEGRATION_DUMP.md`로 덤프한다(V0~V6 패턴).
- Editor 진단 실행 결과 (2026-07-15, Unity 6000.4.2f1, 메뉴 실행): **13/13 통과**(하니스 17개와 동일 커버리지 — Editor는 idempotent·freeze를 각 1 Record로 합침). 표본 정합 확인 — Dwarf City: 시설 4(Hub 100·시장 151·여관 153·제련소 160 **FacilityTier 3**), ShopTier 4·매물 8슬롯, 철 8G·구리 4G 지역 특산·스킬품 현지 부족 프리미엄·제련소 T3 `FacilityPool` 슬롯 개방, 진행 task 0. Elf City: 시설 3(특수 시설 없음 — Elf `PrimarySpecialFacilityId=0`, V6 결정), 마일스톤 12 = Dwarf 15 − 3(제련소 Add/Upgrade×2), 비전 특산 할인. Dwarf Town: 제련소 T2·ShopTier 3·매물 7. 전 시설 `PlacementPending=True`(추상 완공, materialize가 배치) — 정상. 실제 청크 materialize·상호작용·벽·상점 UI·Race 배치 시각 실측은 아래 플레이 모드 체크리스트(A~F)로 남는다.
- 남은 것: 아래 플레이 모드 체크리스트(사용자 실행) → 성공 시 현행 정본 문서(VILLAGE_RUNTIME_DESIGN 등) 갱신(제안→반영) → 사용자 확인 후 Wiki/village.md 갱신.

##### V7 플레이 모드 검증 체크리스트 (사용자 실행 — V2~V6 유예 실측 통합)

New Game으로 새 세이브를 만들고 아래를 확인한다. (`ARPG/Village/Run V7 Integration Diagnostics` 덤프에도 동일 목록 기록)

- **A. 고정 인구 불변**: 1일·30일·1년 경과, 실체화/추상 전환, 물리 NPC 사망·보충, Food 0·건물 파괴에도 `SettlementLiveState.Pop`·`VillageData.Population` 불변.
- **B. 첫 materialize 완성형**: 원거리 생성 마을 첫 방문 시 인구에 맞는 Stage·필수 시설·특수 시설·상점이 이미 완성(건설 과정 미노출). materialize/fold-back 반복 후 초기 마일스톤·재고 무중복. `PlacementPending` 시설이 다음 materialize/Bounds 확장 후 배치.
- **C. 상호작용**: 화면의 모든 일반·특수 건물에 프롬프트가 뜨고 F키 대상 일치(허브=체크포인트 세이브, 시장=거래, 여관=휴식, 제련소=제련·수리). Furnace/Anvil/QuenchVat 비상호작용 건물 미생성. 제련소 Tier 1/2/3 UIForge 기능 게이트.
- **D. 벽·Gate(Dwarf)**: Hamlet 이상 둘레 Palisade + N/S Gate 2개, 벽 충돌·HP 피해·Gate 개폐, 청사진 경계와 벽 미중복.
- **E. 상점 스냅샷·차익**: 스냅샷 가격·사유(지역 특산/현지 부족/희귀 수입) 표기와 차감액 일치. 재롤 직후 세이브 없이 종료→재로드→같은 회차 재진입 시 같은 진열. Dwarf 광산촌 철/구리→非Dwarf 매각 양수 차익, 일반 장비 왕복 손해. Town/City 첫 방문 상점에 T2/T3 품목·특수 시설 풀 등장.
- **F. Race별 상권·배치·역할(V6)**: Race별 상점 품목·가격 사유 구분(Elf 비전 할인·Dwarf 비전 프리미엄 등), Race별 시장/여관/허브 슬롯 위치 구분, TradeHub 매각 프리미엄·Capital 자원 Cap 상향 실측.

---

### V8 (후속) — 런타임 마을 발전 재미 검증

#### 착수 조건

- V0~V7의 초기 완성형 마을 개편이 완료되고 플레이 테스트가 끝났을 것
- 지역별 시설·상권 차이가 실제 탐험과 거래 동기를 만드는지 먼저 검증했을 것
- 런타임 변화가 필요하다는 플레이 근거와 기대 재미를 정의했을 것

#### 후속 검토 항목

- 성장 계기: 단순 시간, 퀘스트, 지역 사건, 교역, 플레이어 투자 중 무엇을 사용할지
- 인구 변화: 자연 증감 재활성화 여부와 전쟁·식량·이주의 연결 방식
- Stage 변화: 초기 `InitialStage` 이후 확장 Stage 또는 별도 발전 트랙 사용 여부
- 변화 연출: 공사 기간, NPC 반응, 외형 변화, 상점 해금의 가시성
- 실패·후퇴: 인구 감소와 시설 파괴가 Stage에 미치는 영향
- 세이브 호환: 고정 인구 기간의 경과 시간을 소급하지 않는 마이그레이션

V8은 현재 구현 범위에 포함하지 않는다. V0~V7에서는 후속 확장을 막지 않는 데이터 경계만 유지하고, 비활성 런타임 승격 시스템이나 숨은 성장 tick을 미리 추가하지 않는다.

---

## 11. 세이브 마이그레이션 정책

> **V7 결정 (2026-07-15)**: 사용자가 세이브를 지우고 New Game으로 새로 시작하기로 결정해 **이 절의
> 구 세이브 마이그레이션은 이번 범위에서 실행하지 않는다.** WorldData 버전은 12로 유지하고 구조 backfill
> 코드를 만들지 않는다. 아래 정책은 향후 구 세이브 보존이 필요해질 때(예: 배포 후 세이브 유지)를 위한
> 설계 기록으로 남긴다. 참고: V1 Profile 보정·V5 재고 스냅샷 backfill은 이미 버전 무관·멱등으로
> `VillageManager.Load`에 상주하므로, 마이그레이션을 재개할 경우 남은 작업은 주로 §11.2 인구 스냅샷과
> §11.3 `Max(기존 Stage, InitialStage)`·누적 마일스톤 backfill이다. `MigrateWorldData`는 `WorldSim`이
> 채워지기 전(Village.Load 이전)에 실행되므로, 인구 의존 backfill은 `HistoryManager.Initialize`의
> `EnsureWorldSimInitialized` 직후(New Game 부트스트랩과 대칭 지점)에 두는 것이 코드 흐름상 자연스럽다.

### 11.1 기존 마을 Profile

1. `SettlementId >= 0`: History의 Race/Biome으로 Profile 결정
2. `SettlementId < 0`: BaseTown 기본 Profile 지정
3. 결정된 `VillageProfileId`를 저장해 이후 재계산하지 않음

### 11.2 기존 마을 인구

- `SettlementId >= 0`: 마이그레이션 시점에 저장된 History/`WorldSimState` 인구를 고정 스냅샷으로 채택한다. History를 다시 생성하거나 로드 경과 시간만큼 인구를 전진시키지 않는다.
- `SettlementId < 0`: 저장된 `VillageData.Population`이 유효하면 이를 고정한다. 값이 없으면 BaseTown Profile 기본값으로 1회 초기화하며 물리 NPC 수로 재계산하지 않는다.
- `VillageData.Population`과 `SettlementLiveState.Pop`이 모두 존재하지만 다르면 History에 연결된 마을은 후자를 우선하고, 마이그레이션 로그를 남긴다.
- 마이그레이션 완료 시 `LastSimGameTime`을 현재 게임 시각에 맞춰 과거 인구 갱신 backlog를 버린다.
- 기존 로지스틱 모델의 성장률·수용력 데이터는 삭제하지 않지만 후속 기획 전까지 런타임에서 소비하지 않는다.

### 11.3 기존 Stage와 누적 시설

- 고정 인구로 `InitialStage`를 계산한다.
- 구 세이브는 `Max(기존 Stage, InitialStage)`를 마이그레이션 Stage로 사용해 기존 발전을 강등하지 않는다. New Game은 계산된 `InitialStage`를 그대로 사용한다.
- 이미 존재하는 시설은 해당 Stage 마일스톤 완료로 인정한다.
- 마이그레이션 Stage까지 필요한 필수·특수 시설이 없으면 진행 task가 아니라 완공 원장과 배치 대기 상태로 backfill한다.
- 마이그레이션만으로 Cap·서비스 완료 부작용을 다시 적용하지 않는다.

### 11.4 기존 일반 건물

- TableId 삭제 금지
- 상호작용 가능한 대체 시설이 있으면 저장 위치를 기준으로 해당 시설에 마이그레이션하고 HP 등 유지 가능한 상태를 이전
- 대체 시설이 없는 비상호작용 건물은 신규 생성 후보에서 제외하고, 구 세이브 레코드는 호환·롤백을 위해 보존하되 월드에는 materialize하지 않는 `Retired` 상태 검토
- 플레이 기능이 제거된 건물을 장식 건물로 계속 노출하지 않음
- 기존 제공 서비스 제거나 시설 교체가 필요하면 세이브 영향과 UI 접근성을 별도 검증
- 구 세이브 마이그레이션 후에도 화면에 표시되는 일반·특수 건물은 모두 직접 상호작용 가능해야 함

### 11.5 기존 상점

- 가격 필드가 없는 기존 `MerchantStockEntry`는 다음 상점 진입 또는 로드 직후 재롤
- 재롤 전 인스턴스 장비를 플레이어가 구매하지 않은 상태이므로 유실 허용 여부를 명시
- 안전한 기본값을 넣을 수 있으면 기존 진열품을 유지하고 가격만 Profile로 스냅샷

---

## 12. 통합 검증 매트릭스

| 축 | 사례 |
|---|---|
| 고정 인구 | 0, 1, 2, 3, 7, 8, 14, 15, 21, 22, 25, BaseTown 기본값, 구 세이브 불일치 값 |
| Fate | Alive, Conquered, Ruined, Merged |
| 실행 경로 | 실체화, 장기간 추상 advance, materialize 직전 catch-up, fold-back |
| 세이브 | New Game, 구버전 세이브, 신규 세이브 왕복 |
| Profile | BaseTown, 7 Race Profile, Profile 누락 fallback |
| 초기 발전 | 고정 인구별 InitialStage, Stage별 누적 시설, 자원 0, 초기 배치 실패·재시도 |
| 런타임 고정 | 1일·30일·1년 경과, 실체화/추상 전환, 물리 NPC 사망·보충 |
| 상호작용 | 모든 일반·특수 건물 프롬프트, Tier 교체 전후, 비상호작용 Buildable 차단, 구 세이브 Retire/교체 |
| 상점 | 재입고 전후, 서로 다른 초기 특수 시설 Tier, 가격 차익 |
| 배치 | 슬롯 성공, 슬롯 점유 fallback, Bounds 확장, 벽 충돌 |

핵심 불변식:

- 물리 NPC 수와 직업은 Stage·생산·건설의 입력이 아니다.
- 절차적 역사 생성 완료 후 일반 런타임 시간 경과만으로 `SettlementLiveState.Pop`과 `VillageData.Population`이 변하지 않는다.
- 물리 NPC 사망·보충, Food·자원 변화, 건물 파괴는 고정 경제 인구를 변경하지 않는다.
- 인구 갱신을 중단한 동안의 경과 시간은 미래 인구 모델 재활성화 시 backlog로 적용되지 않는다.
- 같은 seed·Profile·고정 인구에서는 같은 초기 Stage와 누적 시설 결과가 나온다.
- 초기 Stage 마일스톤은 정확히 한 번 적용된다.
- New Game 완료 시 고정 인구에 맞는 Stage·필수 시설·특수 시설·상점이 이미 완성돼 있다.
- 신규 또는 마이그레이션 후 화면에 표시되는 모든 일반·특수 건물은 플레이어가 직접 상호작용할 수 있다.
- 기능 없는 건물형 오브젝트는 장식이나 Tier 외형 모듈로 생성되지 않는다.
- 일반 런타임 시간 경과로 Stage·시설·상점 Tier·Bounds가 증가하지 않는다.
- 후속 인구 변동 시스템을 추가하더라도 일반 인구 감소는 Stage 강등을 일으키지 않는 것을 기본값으로 한다.
- 초기 필수 시설은 자원 부족이나 건설시간 때문에 대기하지 않는다.
- 완공 복원은 Cap·서비스·마일스톤을 중복 적용하지 않는다.
- 상점 재고와 가격은 저장 또는 결정론 재생으로 복원된다.

---

## 13. 구현 순서상 선결 결정

| 결정 | 권장 기본값 | 확정 시점 |
|---|---|---|
| 런타임 인구 정책 | 절차적 역사 완료 인구 스냅샷 고정, 자연 증감 중단 | V0/V2 |
| Stage 인구 기준 | 1 / 3 / 8 / 15 / 22 | V0 |
| 초기 Stage 적용 | 인구 기준 Stage까지 누적 결과를 New Game 생성 시 완공 처리 | V2/V3 |
| 런타임 승격 간격 | 현재 미사용, V8 재미 검증에서 재설계 | V8 |
| 건물 생성 원칙 | `1건물 = 1상호작용 기능`, 비상호작용 건물형 오브젝트 생략 | V0/V3 |
| 첫 Profile | Dwarf 광산 마을 | V0 |
| 필수 시설 수 | 중심 거점 + 시장 + 휴식소, 최대 3개 | V0 |
| 특수 시설 배치 | Village Tier 1, Town Tier 2, City Tier 3 | V0 |
| Forge 최소 접근성 | 신규 게임의 모든 Race·Profile·초기 Stage 마을에 Forge 160 Tier 1 이상 보장. 기존 Tier 2/3은 유지 | 2026-08-30 마법 부여 MVP |
| Forge 구 세이브 | 전체 backfill 제외, New Game 완성형부터 적용 | 2026-08-30 마법 부여 MVP |
| 초기 필수 시설 비용 | 자원·건설시간 미사용, 완공 원장으로 생성 | V3 |
| 벽 정본 | 기능 없는 청사진 경계 제거, 충돌·HP·Gate 개폐 기능을 가진 방어 구조만 사용 | V4 |
| 상점 슬롯 | 공통 2 + 특산 2 + 특수/희귀 1 | V5 |
| 일반 교역 차익 | TradeGood만 의도적으로 허용 | V5 |
| TradeTag 저장 | 시트 컬럼 대신 코드 카탈로그(`ItemTradeTagCatalog`) — 시트 이전은 V6/V7 재검토 | V5 |

---

## 14. 완료 정의

다음 조건을 모두 만족하면 개편 1차 완료로 본다.

1. 절차적 역사 완료 후 장기간 플레이, 실체화 전환, save/load를 거쳐도 마을의 고정 경제 인구가 변하지 않는다.
2. 고정 인구가 `InitialStage`를 정하고, New Game의 마을이 첫 등장부터 그 Stage까지의 필수·특수 시설과 상점 상태를 갖춘다.
3. Stage별 누적 시설·Tier 교체·상점·배치 결과가 생성 시 중복 없이 적용된다.
4. 자원과 건설시간이 0이어도 초기 완성 상태가 달라지지 않는다.
5. 모든 구현 대상 마을에 저장 가능한 Profile과 고정 인구가 있다.
6. 신규 및 마이그레이션된 마을에 표시되는 모든 일반·특수 건물에서 직접 상호작용을 시작할 수 있다.
7. 적어도 Dwarf 수직 슬라이스가 비상호작용 건물 없이 기존 청사진보다 기능 밀도가 높고 이동 동선이 짧다.
8. 적어도 2개 Profile의 상점 품목과 가격이 명확히 다르다.
9. 고정 인구와 초기 완성 결과의 추상/실체화 parity 및 New Game/save-load 왕복이 통과한다.
10. 기존 세이브의 인구·건물·Cap·진행 task가 유실되거나 중복되지 않는다.
11. 런타임 인구 변화·Stage 승격·자동 시설 추가가 V8 전까지 실행되지 않는다.
12. 현행 village 정본 문서와 Wiki가 실제 구현에 맞게 갱신된다.

> **V7 완료 정의 상태 (2026-07-15)**:
> - #10(기존 세이브 유실·중복 없음)은 **세이브 신규 시작 결정으로 적용 제외** — 구 세이브가 없으므로 무손실
>   대상이 없다. New Game 완성형의 무중복은 정적 하니스(마일스톤 재적용 신규 0·fold-back 5회 무중복)로 확인.
> - #1~#5·#9·#11: 코드 + 정적 검증(V2~V7 하니스, `CompareFixedState`)으로 충족. 실체화·물리 NPC 결합
>   부재는 코드 경로로 확인했고 화면 실측만 플레이 모드로 남는다.
> - #6·#7·#8: 정적으로는 통과(시설·상권·배치 데이터). 시각·상호작용 실측은 V7 플레이 모드 체크리스트.
> - #12: 현행 정본 문서(VILLAGE_RUNTIME_DESIGN·ABSTRACT_CONSTRUCTION·BUILD_PRIORITY·VILLAGE_NPC_BOUNDARY)와
>   Wiki/village.md를 현재 구현에 맞게 **갱신 완료(2026-07-15)**.
