# NPC 상점 기본 장비 설계

> 상태: 2026-10-03 사용자 결정 반영 완료(D116~D121·D123 확정, 열린 질문 Q1~Q4 종료). **2026-10-03 구현 완료, 커밋 대기** — D117은 구현 중 "기존 칸 교체"에서 "별도 칸 추가"로 개정됐다. 구현 결과·검증은 §11. **확정** 항목은 사용자 결정이고, **구현** 표시는 구현 단계에서 정한 방식이다(처음 [제안]이던 항목은 모두 제안대로 구현했다).
>
> 범위: 상점용 기본 아이템 테이블, 상점 진열, NPC 장비의 시작·획득·교체 흐름, 저장 표현, 기존 세이브 일괄 교체. 특수 NPC·이벤트 아이템은 다루지 않는다(보류).
>
> 결정 번호는 NPC_AMBITION·NPC_COMBAT 공용 연번을 잇는다(D116~D123).

## 1. 목표와 배경

- 일반 NPC가 옵션이 제각각인 장비를 들고 다니는 것은 게임에서 의미가 거의 없다. NPC 장비를 **상점에서 파는 고정 옵션 기본 아이템**으로 통일하고, NPC는 상점에서 다음 단계 기본 아이템을 사서 강해진다. [확정: 2026-10-03 사용자]
- 저장 비용도 함께 해결한다. 2026-10-03 실측(`[SaveTiming]`, 전체 저장 13.3MB)에서 메인 스레드가 **약 600ms** 멈췄고, 그중 587ms가 저장용 깊은 복사였다. 세이브 분석 결과 NPC 상태의 **79%가 장착 장비**였다(장비 하나에 값 약 57개: 옵션 66%, 계산 캐시 `WeaponStats` 29%). 기본 아이템은 옵션이 테이블로 정해지므로 "어느 기본 아이템인가"만 저장하면 된다. [`DUNGEON_WORLD_SIM_DESIGN.md`](../map/DUNGEON_WORLD_SIM_DESIGN.md) §6
- 구현 전 구조(2026-10-03 이전): NPC 시작 장비와 상점 매물은 모두 `ItemManager.CreateInventoryItemData`로 만든다. Base·Implicit 옵션은 테이블 고정이고, Prefix·Postfix는 1~3개씩, 품질은 1~100에서 무작위로 정한다. 전리품은 전투력이 +15% 이상 오르면 바로 장착한다(D35·D45). [`ItemManager.cs`](../../Assets/Scripts/Manager/ItemManager.cs) `CreateEquipmentData`, [`NpcEquipHelper.cs`](../../Assets/Scripts/Npc/NpcEquipHelper.cs), [`NpcManager_Ambition.cs`](../../Assets/Scripts/Manager/NpcManager_Ambition.cs) `GrantAcquiredItem`

## 2. 결정 요약

| # | 결정 | 상태 |
|---|---|---|
| D116 | **상점용 기본 아이템 테이블**을 둔다. 모든 장비 아이템마다 행이 있고, 한 장비에 고정 옵션이 다른 **변형 여러 개**를 둔다. 행마다 고정 옵션은 **1개**다. Base·Implicit은 기존 테이블 그대로 붙는다. 무작위 롤은 없다 | 확정 |
| D117 | 기존 상점 재고(보장·특산·희귀·특수 칸, 무작위 옵션 장비)는 **그대로 두고**, 회차마다 **기본 장비 칸**을 따로 추가 진열한다(상점 Tier 1~2는 3칸, 3~4는 4칸). 플레이어도 살 수 있다. 회차마다 일부 장비만 무작위로 진열되므로 NPC가 원하는 장비를 항상 살 수 있지는 않다 | 확정 (2026-10-03 개정: 기존 칸 교체 → 별도 칸 추가) |
| D118 | 일반 NPC의 장비는 기본 아이템이다. 시작 장비도 기본 아이템으로 만든다 | 확정 |
| D119 | NPC는 사냥·던전 전리품과 훔친 물건을 **장착하지 않는다.** 가방에 넣었다가 상점에 판다. NPC가 강해지는 경로는 상점 구매 하나이고, **기본 아이템만 산다**(기존 칸의 무작위 옵션 장비와 모험가 매입품은 사지 않음) | 확정 |
| D120 | 특수 NPC·이벤트가 쓰는 다양한 옵션 아이템은 나중으로 미룬다 | 확정(보류) |
| D121 | 기존 세이브의 NPC 장착 장비는 로드할 때 **일괄 교체**한다 | 확정 |
| D122 | 기본 아이템은 "어느 행인가"만 저장하고 옵션은 로드할 때 테이블에서 다시 만든다. 계산 캐시(`WeaponStats`·`EquipType`)는 모든 장비에서 저장하지 않는다 | 구현 (제안대로, 2026-10-03) |
| D123 | 기본 아이템 가격은 아이템 테이블의 `BasePrice`를 그대로 쓰고, 변형 사이에 차이를 두지 않는다 | 확정 |

## 3. 상점용 기본 아이템 테이블 (D116)

### 3.1 열 구성 [구현]

| 열 | 뜻 |
|---|---|
| `Id` | 기본 아이템 행 번호. 저장에 쓰는 식별자다 |
| `ItemId` | 장비 아이템(`ItemTable`) 번호. 무기·방어구·장신구·방패 전부 |
| `ModTableId` | 고정 옵션 1개. 그 장비 종류에 허용된(`ModTable.AllowedEquipTypes`) Prefix 또는 Postfix 옵션 |
| `Value1`, `Value2` | 고정 옵션 값 |
| `Weight` | 같은 `ItemId`의 변형 사이에서 진열될 가중치 |

- 행 수: 장비 67종 × 변형 2~4개 = 약 130~270행. 변형 수는 초안을 만든 뒤 시트에서 장비별로 조정한다. [확정: Q4]
- 고정 옵션 값은 그 옵션의 **가장 낮은 Tier 값**을 기준으로 한다. "기본" 아이템이므로 몬스터 드랍이나 전리품보다 약해야 한다. [확정: Q2] 범위 안의 값은 **Tier 1 중간값(내림)**으로 한다. [확정: 2026-10-03 사용자]
- 품질(Quality)은 고정값 1을 쓴다. 지금 품질은 생성할 때 무작위로 정해질 뿐 게임 로직 어디에서도 쓰이지 않는다. 기존 `CreateBaseEquipment`도 1을 쓴다.
- Implicit 옵션이 있는 장비는 Implicit 값을 Tier 범위 최소값으로 고정한다(로드 때 테이블에서 다시 만들어야 하므로). 2026-10-03 현재 ItemImplicit 테이블은 비어 있다.
- **구현 (2026-10-03)**: 시트 `BasicItem` 탭(gid 786446863, A~F 데이터 + G~H 보조 열), `BasicItemTable.bytes` 198행 = 장비 66종 × 변형 3개(`초급 마법서`는 베이스 스탯이 없어 장비 데이터가 생기지 않으므로 제외). 초안 도구 `Editor/BasicItemDraftGenerator.cs`.
  - **Id = ItemId × 10 + 변형 번호(1~9)**. 저장 식별자이므로 기존 Id를 바꾸거나 재사용하지 않는다.
  - 모든 옵션의 Tier 가중치가 같아(100/60/30) "가중치가 높은 옵션"으로는 고를 수 없으므로, 장비 종류별 선호 목록을 쓴다. 무기: 추가 물리·공격 속도·치명타 확률 / 투구·갑옷·허리띠·방패: 최대 체력·방어력·저항 1종 / 장갑: 최대 체력·방어력·치명타 피해 / 장화: 최대 체력·방어력·이동 속도 / 목걸이·반지: 체력 재생·치명타 피해·저항 1종. 저항 종류는 ItemId에 따라 화염·냉기·번개·독을 돌아가며 고른다. 가중치는 모두 100.

### 3.2 아이템 생성

`ItemManager`에 기본 아이템 생성 진입점을 하나 둔다. 행 번호를 받아 Base·Implicit(기존 테이블) + 고정 옵션 1개, 품질 1로 `ItemData`를 만들고, 장비 데이터에 행 번호(`BasicItemId`)를 기록한다. 상점 진열, NPC 시작 장비, 일괄 교체가 모두 이 진입점을 쓴다. [구현: `ItemManager.CreateBasicItem`·`CreateBasicEquipmentData`·`PickBasicItemVariant`]

## 4. 상점 진열 (D117)

- **기존 재고는 바꾸지 않는다.** 재고 롤러(`VillageTradeStockRoller`)가 재입고 회차마다 보장·특산·희귀·특수 칸에서 무작위로 일부 품목을 뽑고, 장비는 지금처럼 `CreateInventoryItemData`로 무작위 옵션을 굴린다. 결과도 지금과 같다. [확정: 2026-10-03 사용자, D117 개정] [`VillageTradeStockRoller.cs`](../../Assets/Scripts/Village/VillageTradeStockRoller.cs)
- **기본 장비 칸 추가** (`TradeStockSlotKind.BasicEquipment`): 기존 재고를 다 롤한 뒤 **같은 회차 RNG를 이어서** 기본 장비 칸을 롤한다. 기존 재고의 난수열을 건드리지 않으므로 기존 결과는 그대로이고, 결정론은 지금처럼 (마을, 회차) 단위다. [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) `RollMerchantStock`
  - 칸 수: 상점 Tier 1~2는 3칸, 3~4는 4칸. [확정: 2026-10-03 사용자]
  - 후보: 상점 Tier가 여는 아이템 Tier(`GetMaxItemTier`) 이하이고 기본 아이템 변형이 있는 장비. 같은 확률로 뽑고, 기본 장비 칸끼리는 같은 장비가 겹치지 않는다. 같은 회차 기존 칸에 같은 장비(무작위 옵션)가 있어도 된다. [확정: 2026-10-03 사용자]
  - 변형은 시트 가중치로 고른다. 칸마다 1개, 가격은 다른 매물과 같은 식(BasePrice × 지역 판매 비율, D123).
  - 상점 UI에 "기본 장비"로 표기한다.
- **모험가 매입품**(D42, NPC가 판 실제 전리품)도 그대로 둔다.
- 스킬북·스킬 페이지·소모품·자원은 바뀌지 않는다.
- 결과: 무작위 옵션 장비(기존 칸·모험가 매입품)는 전부 플레이어 몫이고, NPC는 기본 장비 칸에서만 사므로(§5.3) NPC 장비는 항상 기본 아이템으로 유지된다.

## 5. NPC 장비 흐름

### 5.1 시작 장비 (D118)

`NpcEquipTable`(슬롯별 `ItemId`와 장착 확률)은 그대로 쓴다. 슬롯마다 `ItemId`가 정해지면, 그 아이템의 변형 중 하나를 **entityId와 슬롯으로 만든 해시**로 골라 기본 아이템을 만든다. 무작위 옵션 롤은 하지 않는다. [`NpcEquipHelper.cs`](../../Assets/Scripts/Npc/NpcEquipHelper.cs)
- 해시: `DerivedSeed.ChannelNpcBasicEquipment`("NPCBSEQP") ^ 슬롯 번호로 만든 채널을 `Derive(entityId, …)`에 넣어 [0,1) 값을 얻는다. worldSeed와 무관해 같은 NPC·슬롯은 언제 계산해도 같은 변형이 나온다. 일괄 교체(§6.2)도 같은 함수(`NpcEquipHelper.PickBasicVariant`)를 쓴다. NpcActivity salt 번호는 쓰지 않는다.
- 변형이 없는 장비면 경고를 남기고 Base만 있는 장비(`CreateBaseEquipment`)로 대신한다. 2026-10-03 현재 NpcEquipTable의 장비 13종은 모두 변형이 있다.

### 5.2 전리품·장물 (D119)

- `GrantAcquiredItem`의 자동 장착 평가를 없앴다(가방에 넣기만 한다). 전리품(Hunt·Delve·던전 완료 보상)과 장물(D101)은 모두 가방에 넣는다. 가방이 가득 차면 가치가 가장 낮은 것을 버리는 규칙(D35)은 그대로다.
- 실체화 중 전리품의 지연 장착도 없어졌다. 실체화 중 **구매**의 지연 장착은 그대로 두고, 목록 이름을 `_deferredPurchaseUpgradeItemIds`로 바꿨다(장착 출처 "Trade").
- 가방의 물건은 Trade 활동에서 상점에 판다(§7.2 기존 규칙).

### 5.3 상점 구매 (D119)

- **NPC 구매 후보는 기본 아이템(`BasicItemId > 0`)만이다.** 기존 칸의 무작위 옵션 장비는 이 조건으로 빠진다. 칸 종류가 아니라 아이템 자체로 거르므로, 나중에 무작위 옵션 장비가 나오는 경로가 늘어도 NPC 장비는 기본 아이템으로 남는다. [확정: 2026-10-03 사용자] [`VillageManager_NpcTrade.cs`](../../Assets/Scripts/Village/VillageManager_NpcTrade.cs) `IsNpcPurchasableEntry`
- **모험가 매입품 제외는 유지한다.** NPC가 갈아 끼우고 판 기본 아이템이 모험가 매입품으로 진열되면 `BasicItemId > 0`이라, 이 조건이 없으면 NPC가 되사게 된다.
- "희귀 칸 매물은 재입고 간격의 50%가 지난 뒤에만 NPC가 살 수 있다" 규칙(`NpcRareSlotUnlockFraction`)은 없앤다. NPC가 희귀·특수 칸에서 살 수 있는 물건이 없어져(무작위 옵션 장비, 스킬북·페이지는 원래 안 삼) 결과를 바꾸지 못한다.
- **회차당 구매 한도 식은 그대로**다(롤된 매물 종류 수 × 25% 내림, 1~3건). 종류 수는 모험가 매입품과 함께 **기본 장비 칸도 빼고** 센다. 한도 값은 지금과 같다(상점 Tier 1~3은 1건, Tier 4는 2건). [확정: 2026-10-03 사용자]
- 나머지 구매 조건은 바꾸지 않는다. D45 문턱(전체 전투력 +15%, 개인 상한 80% 초과 시 +30%), 개인 상한, 치료비 예비금, 48게임시간 쿨다운, 상점 보호의 6시간 간격이 그대로 적용된다. [`NPC_AMBITION_DESIGN.md`](NPC_AMBITION_DESIGN.md) §7.2
- 매물이 회차마다 무작위라서, 원하는 슬롯의 더 좋은 장비가 늘 진열되지는 않는다. 이것이 성장 속도를 늦추는 주된 장치다(사용자 결정).
- 갈아 끼우고 벗은 기본 아이템은 가방으로 가서 다음 Trade에서 매입가(40%)에 팔린다.

### 5.4 기록·소문

- 구매도 전리품과 같은 장착 경로(`ApplyUpgrade`)를 쓰므로, 자랑 기록(`NotableItemAcquired`, D83)과 장비 소문(ItemOwner, D71)은 **구매에서** 계속 생긴다.
- 훔친 장비를 장착하는 경로(D110)는 없어진다. 늦은 발각은 이미 거의 판매 소문 경로라 영향이 작다.

## 6. 저장 표현과 일괄 교체

### 6.1 저장 표현 (D122) [구현]

- 장비 데이터(`EquipmentData`)에 `BasicItemId`(기본 아이템 행 번호, 0 = 일반 아이템)를 둔다.
- `BasicItemId > 0`이면 옵션 목록과 품질을 저장하지 않는다. 로드할 때 `OnLoadCompleted`가 행 번호로 옵션을 다시 만든다. 저장 복사기(`SaveDtoSnapshot`)는 이미 Newtonsoft의 조건부 저장(`ShouldSerialize`)을 따른다.
- **안전망**: 저장할 때 옵션이 테이블 행과 다르면 기본 아이템이 아닌 것으로 보고 전체를 저장한다. 로드에서 옵션이 함께 저장돼 있으면 저장된 옵션을 쓰고 `BasicItemId`를 지운다. 옵션을 바꾸는 경로(마법부여 `ItemEnchantmentService`)는 먼저 `BasicItemId`를 지운다.
- 기획자가 테이블 값을 고치면, 이미 가진 기본 아이템도 다음 로드부터 새 값을 쓴다. 기본 아이템은 밸런스 조정이 바로 반영되는 것이 맞다고 본다.
- 모든 장비에서 `WeaponStats`(Base 옵션과 고유 강화로 계산되는 무기 능력 캐시)와 `EquipType`(테이블 분류에서 정해지는 장비 종류)을 저장하지 않는다. 지금도 로드할 때 다시 계산하고 저장값은 버린다. [`ItemData.cs`](../../Assets/Scripts/Data/ItemData.cs) `EquipmentData.OnLoadCompleted`
- 기대 효과(추정): NPC 장착 장비 약 7.9MB → 약 0.4MB, NPC 상태 10.7MB → 약 3MB, 전체 저장 깊은 복사 약 590ms → 250ms 안팎. 마을 파티션의 상점 재고도 함께 줄어든다. `[SaveTiming]` 로그로 확인한다.

### 6.2 일괄 교체 (D121)

- 시점: 로드 보정. `WorldDataVersion`을 16 → 17로 올리고, 마이그레이션 뒤 첫 저장은 기존 규칙대로 전체 저장으로 강제된다. [`DataManager.cs`](../../Assets/Scripts/Manager/DataManager.cs) `MigrateWorldData`
- 대상: **NPC 장착 장비만.** 같은 `ItemId`의 기본 아이템 변형으로 바꾸고, 변형은 §5.1과 같은 해시로 고른다.
- **`ItemInstanceId`는 유지한다.** 장비 소문(ItemOwner)이 소유자·슬롯·인스턴스 번호를 가리키므로, 번호를 바꾸면 기존 소문이 무효가 된다.
- 그대로 두는 것:
  - NPC 가방: 전리품이므로 팔면 된다.
  - 플레이어 장비·가방·창고.
  - 상점 재고: 다음 재입고 때 자연스럽게 바뀐다. 이번 회차를 즉시 다시 롤하면 이미 산 재고가 되살아나는 문제가 생긴다.
- 교체 뒤 **개인 상한 기준(`InitialPower`)을 다시 계산한다.** 기존 장비는 옵션이 많아 전투력이 높았으므로, 그대로 두면 상한이 높게 남아 구매로 크게 성장할 수 있다. 시작 Gold와 상한 편차(`PowerCapBonus`)는 바꾸지 않는다. 기존 초기화 함수(`EnsureAmbitionInitialized`)는 Gold도 함께 정하므로 전투력만 다시 계산하는 경로를 따로 둔다.
- 전투력 캐시를 비운다.

## 7. 영향받는 규칙·튜닝

- **Hunt 전투력 정규화**: NPC 평균 전투력이 내려가므로, Hunt 성공률·피해·사망률의 기준값을 다시 맞춰 Hunt 성공률(약 66%)과 사망률 기준(30일 0.5~1%)을 유지한다. → 구현: `HuntReferencePower` 26 → 10.75, Food 문턱 그대로(§11.1). 진단 메뉴 Hunt Power Normalization을 쓴다. [`NpcActivityResolver.cs`](../../Assets/Scripts/Npc/NpcActivityResolver.cs)
- **NPC 성장 기조(D10·D11)**: 성장 경로가 상점 구매 하나가 된다. "희귀 행운" 표의 던전 완료 보상 업그레이드 행은 일반 NPC에서 사라진다(특수 NPC로 이월, D120). 개인 상한을 기본 장비 기준으로 다시 잡으면, 일반 NPC(+20%)는 평생 업그레이드 한두 번 정도의 여지만 남는다. "거의 평탄"과 맞는지 진단으로 확인한다. → 확인됨(§11.2).
- **골드 순환**: 전리품을 전부 팔게 되어 매입 수요가 늘고, 구매가 마을로 골드가 돌아가는 주된 경로가 된다. 마을 골드 밸런스(NPC 소비·금고 초과분 소멸) 작업과 함께 맞춘다.
- **소문·자랑**: 자랑 토픽 실패(D109 ①)는 업그레이드 출처가 구매로 모이면서 빈도가 바뀐다. → 측정: 30일 실행 PASS, 60·90일 실행은 여전히 FAIL(§11.2).
- **진단** (`NpcAmbitionSimDiagnostics`): 업그레이드 사건 중 Loot·Delve 0건, Trade만 남음. 평균 전투력 평탄, 개인 상한 초과 0, Hunt 비중·성공률, 사망률, 세이브 크기, `[SaveTiming]`.

## 8. 확정 후 갱신할 문서

> ✅ 2026-10-03 갱신 완료 — 아래 세 문서와 `NPC_SIMULATION_DESIGN.md` §11.1(새 RNG 채널·전리품 미장착).

- [`NPC_EQUIPMENT_DESIGN.md`](NPC_EQUIPMENT_DESIGN.md) §3.3 "인스턴스 생성 정책"(무작위 옵션 롤 이유)·§4 시드 흐름·§9 저장 공간 추정
- [`NPC_AMBITION_DESIGN.md`](NPC_AMBITION_DESIGN.md) §7.1(전리품 자동 장착)·§7.2·§10.1 성장 표, D35·D83·D110 설명, 결정표에 D116~D123 추가
- [`VILLAGE_REWORK_PLAN.md`](../village/VILLAGE_REWORK_PLAN.md) V5 상점 재고 인스턴스 설명

## 9. 구현 순서와 검증

1. **준비**: 기본 아이템 테이블(DTO·로드·초안 생성 도구), 기본 아이템 생성 진입점, 저장 표현(§6.1)과 `WeaponStats`·`EquipType` 저장 제외. 이 단계에서는 게임 동작이 바뀌지 않는다. 검증: 컴파일, 기본 아이템 저장·로드 왕복 동일, 옵션 변경 안전망.
2. **본체**: 상점 진열(§4), NPC 시작 장비(§5.1), 전리품·장물 미장착(§5.2), 일괄 교체(§6.2), Hunt 정규화(§7).
3. **회귀 진단 1회**: 진단 Run(30)·Run(60)과 P2·P4 체크리스트, 전체 저장 `[SaveTiming]`.

필수 검증:

- 상점의 기본 장비 칸 매물이 모두 기본 아이템이고 칸 수가 상점 Tier 규칙(3/3/4/4)과 맞다. 기존 칸 장비는 무작위 옵션 그대로다.
- NPC 구매 중 기본 아이템이 아닌 것이 0건이다.
- NPC 장착 장비가 모두 기본 아이템이다(일괄 교체 직후와 Run(60) 끝).
- 전리품·장물 장착 0건. 구매 업그레이드는 발생하고 개인 상한 초과는 0이다.
- 교체 전후 `ItemInstanceId`가 같고, 장비 소문 대상 실존 100%가 유지된다.
- 기본 아이템의 저장·로드 왕복 뒤 옵션과 전투력이 같다. 마법부여한 기본 아이템은 전체 옵션이 저장된다.
- Hunt 성공률·사망률이 기준 안에 있다.
- 전체 저장 크기와 깊은 복사 시간이 줄었다(`[SaveTiming]`).

검증 주의: 일괄 교체는 로드 보정이라 게임이 저장하면 세이브 형식이 바뀐다. 테스트 전에 세이브를 백업하고 끝나면 복원한다.

## 10. 열린 질문

| # | 질문 | 기본안 |
|---|---|---|
| ~~Q1~~ | 희귀·특수 칸(`RareImport`·`FacilityPool`)에서 뽑힌 장비를 어떻게 할지 | **확정(2026-10-03)**: 지금처럼 무작위 옵션, NPC는 사지 않음 (D117·D119, §4·§5.3) |
| ~~Q2~~ | 고정 옵션 값을 어느 수준으로 할지 | **확정(2026-10-03)**: 그 옵션의 가장 낮은 Tier 값 (§3.1) |
| ~~Q3~~ | 같은 장비의 변형 사이에 가격 차이를 둘지 | **확정(2026-10-03)**: 두지 않음 (D123) |
| ~~Q4~~ | 변형을 장비마다 몇 개 둘지 | **확정(2026-10-03)**: 2~4개, 초안 생성 후 시트에서 조정 (§3.1) |

## 11. 구현 결과 (2026-10-03, 커밋 대기)

### 11.1 구현 메모

- **저장 표현(D122)**: `EquipmentData.BasicItemId`. `ShouldSerializeBasicItemId/Mods/Quality`가 `IsIntactBasicItem()`(행 존재·ItemId·품질 1·옵션이 행 원형과 같음)으로 갈린다. 온전하면 행 번호만, 아니면 전체 옵션만 저장한다(행 번호는 저장 안 함). 로드(`OnLoadCompleted`): 옵션이 없으면 행 원형으로 다시 만든다. 옵션이 있으면 저장된 옵션을 쓰고, 행과 다를 때만 `BasicItemId`를 지운다(재호출 안전). 행이 사라졌으면 같은 장비의 첫 변형으로 대신한다. `WeaponStats`·`EquipType`은 모든 장비에서 `[JsonIgnore]`.
- **옵션 원형**: `BasicItemTable.TemplateMods`를 테이블 로드 끝(`DataManager.BuildBasicItemIndex`)에 행 검증과 함께 만든다. 생성은 원형 복사, 저장 생략 판정은 원형 비교다. 잘못된 행은 오류를 남기고 제외한다.
- **상점(D117)**: `TradeStockSlotKind.BasicEquipment = 5`(enum 끝). `VillageTradeStockRoller.RollBasicEquipmentPlan` + `VillageTradeProfile.GetBasicEquipmentSlotCount`. UI 표기 "기본 장비".
- **NPC 구매(D119)**: `IsNpcPurchasableEntry`에 `BasicItemId > 0`. 희귀 칸 50% 규칙(`NpcRareSlotUnlockFraction`·`IsRareSlotUnlockedForNpc`·`IsRareStockSlot`) 삭제. 회차 한도는 `BasicEquipment` 칸을 세지 않는다.
- **전리품·장물(D119)**: `GrantAcquiredItem`은 가방에 넣기만 한다(반환값 없음). 절도 결과는 항상 `StolenToBag`(`StolenEquipped` enum 값은 남겨 둠). 실체화 중 지연 장착은 구매 전용(`_deferredPurchaseUpgradeItemIds`, 출처 "Trade").
- **일괄 교체(D121)**: `WorldDataVersion` 16 → 17. `MigrateWorldData`는 표시만 하고, `NpcManager.Load`가 `OnLoadCompleted` 직후 `ReplaceEquipmentWithBasicItems`로 교체한다(장착 장비만, `ItemData` 객체·`ItemInstanceId` 유지, `InitialPower`만 재계산). 사망 NPC도 포함한다.
- **Hunt 정규화(§7)**: `HuntReferencePower` 26 → **10.75**(교체 뒤 세이브 생존 2,387명 평균). `SkillScorePerLevel`은 `HuntReferencePower × 0.05`로 묶었다(지금 SkillLevel은 전원 0). Food 문턱(0.3925/0.56)은 그대로 둔다 — 새 분포에서도 Food/Hunt 1.375(교체 전 1.386).

### 11.2 검증 결과 (라이브 세이브 rev 128 = 약 57게임시간, 검증 뒤 원본 복원)

- 일괄 교체: NPC 2,406명, 장착 장비 10,892개 교체, 변형 없음 0. 교체 전 세이브와 대조하면 슬롯별 `ItemInstanceId`·ItemId 10,892/10,892 유지, `PowerCapBonus` 불일치 0, InitialPower = 교체 뒤 전투력 2,387/2,387.
- 상점(복제본 재롤, 상점 269곳): 기본 장비 칸 수 3/3/4/4 일치, 전부 기본 아이템·중복 없음·Tier 게이트 안. 기존 칸에 기본 아이템 섞임 0. 같은 (마을, 회차) 재롤 동일. **이번 회차를 옛 코드로 롤한 라이브 재고 37곳과 기존 칸 결과 동일.**
- 저장 표현: 198행 전부 저장·로드 왕복 뒤 옵션·무기 능력·전투력·JSON 동일. 안전망·마법부여 해제·구 키 무시·행 삭제 대체 통과. 실제 전체 저장 → 재로드에서 NPC 장착 장비 10,923개·전투력 합계·InitialPower·기본 장비 매물 895건 동일.
- 진단(Run 7/30/60/90, Probe 30): 기본 아이템 아닌 구매 0, 장착 장비 중 기본 아이템 아님 시작·끝 0, 업그레이드 Loot·Delve 0(Trade만), 개인 상한 초과 0, 소문 대상 실존 100%, 닫힌 경제 오차 0, P4a·P4b 감사 전부 PASS, 강제 검증 3종(완료·소문·절도) 실패 0. P2 체크리스트 30일 14/14(자랑 토픽도 PASS), 60·90일은 알려진 2건(자랑 토픽, 완료 0.5회)만 FAIL.
- Hunt: 성공률 65.2~65.8%(교체 전 65.7%), 사망 30일 0.88~0.92%·60일 0.75%·90일 1.88%(30일 기준 0.5~1%), Hunt 비중 41.8~43.7%, 파티 비율 44~46%.
- 성장: 평균 전투력 30일 +1.8~2.4%, 90일 +2.0%. 구매 업그레이드는 첫 며칠에 몰리고(60일 실행: 1일 66건, 30일까지 211건, 31~60일 9건) 그 뒤 거의 없다 — 개인 상한 +20% 안에서 다음 단계 기본 장비 한 번이면 여지가 끝난다(§7 "거의 평탄"과 일치).
- 저장(`[SaveTiming]`, 전체 저장): 13.31MB → **9.41~9.46MB**, NPC 상태 10.71MB → **5.74MB**, 메인 스레드 복사 599ms → **300~355ms**(도메인 리로드 직후 첫 저장은 431ms). 마을 파티션은 1.2~1.48MB → 1.94~1.97MB(기본 장비 칸 895건 + 플레이 중 재입고 진행분, 따로 나눠 재지 않음).

### 11.3 후속: 저장 기본값 생략 (2026-10-03, 커밋 대기)

- **발견**: 저장 복사기(`SaveDtoSnapshot`)가 `ShouldSerialize`만 따르고 멤버의 `DefaultValueHandling.Ignore`는 무시했다. 그래서 D93(Deed 기본값 생략)·소문·NPC 부상/절도 필드의 기본값 생략이 **실제 세이브에는 적용되지 않고 있었다**(진단의 NPC JSON 크기는 `JsonConvert`로 직접 재서 실제보다 작게 나왔다). 복사기가 Newtonsoft 직렬화기와 같은 규칙(값이 `[DefaultValue]` 또는 값 형식 기본값과 같으면 생략)을 따르게 고쳤다. [사용자 승인]
- **A. 아이템 빈 필드 생략**: `ItemData`의 null인 `Equipment`·`SkillBook`·`SkillPage`, `Quantity` 1(로드 때 1로 채움, 코드에서 만든 아이템의 기본값 0은 그대로), `EquipmentData.Id`(소유 아이템 Id와 같음, 로드 때 채움), 빈 `WeaponLocalEnhancements`. 기본 아이템 1개 146B → **62B** (`{"Id","ItemInstanceId","Equipment":{"BasicItemId"}}`).
- **B. 활동 상태 기본값 생략**: `NpcActivityState` 19개 필드 전부 `[DefaultValue]`(필드 초기값과 같게) + `DefaultValueHandling.Ignore`. NPC당 420B → **57B**, 빈 상태 = `{}`. NPC·파티 활동 모두 해당.
- 검증: 기본 아이템·Quantity 0/1/7·활동 상태 2,406건·Deed 973건 실제 저장 경로 왕복 동일, 저장 복사기 결과 = Newtonsoft 직렬화 결과(NPC 300명), 저장 파일 "로드 → 저장 → 로드 → 저장"이 NPC 2,407명·마을 300곳에서 바이트 단위로 동일. Run(30)·Run(60) 라이브 해시 동일·에러 0·감사 전부 PASS(60일은 알려진 2건만 FAIL).
- 저장(`[SaveTiming]`, 로드 몇 분 뒤 전체 저장 4회): **6.23~6.40MB**(기본 장비만 적용 때 9.41~9.46MB, 원래 13.31MB), NPC 상태 **2.84~2.88MB**(5.74 / 10.71MB), 메인 스레드 복사 254~358ms, 백그라운드 약 0.95s(원래 2.1s). 진단 기준 NPC 데이터는 로드 직후 4,416KB → 2,775KB, 30일 끝 13.3MB → 11.2MB(남은 증가분은 대부분 가방 전리품·소문 — 마을 골드 밸런스에서 다룬다).
