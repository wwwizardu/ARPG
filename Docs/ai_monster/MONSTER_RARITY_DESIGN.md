# 몬스터 등급 (마법·희귀) 설계

> 상태: 필드 스폰 구현 (2026-10-11), 실제 플레이 확인 전. 같은 날 출현 확률을 8/4 → 15/8로 올렸다(R3·R7). 같은 날 무작위 접사를 구현하면서 드랍 판정을 "기본 + 접사당"으로 바꿨다([MONSTER_AFFIX_DESIGN.md](MONSTER_AFFIX_DESIGN.md) §8).
> 작성일: 2026-10-11
> 범위: 같은 종을 일반·마법·희귀로 나누는 개체 등급, 등급별 스탯·드랍 보상, 필드 스폰 판정, HP바 표시. 무작위 접사는 확장 지점만 정한다(§7).
> 관련: [MONSTER_SPAWN_DESIGN.md](MONSTER_SPAWN_DESIGN.md) §3.4·§6.1(원래 등급 승격 계획), [BALANCE_DESIGN.md](../combat/BALANCE_DESIGN.md) §2·§3.3(TTK·등급 배율)

---

## 1. 목표

- 같은 종이 일반·마법·희귀로 나온다. 등급이 높을수록 HP·저항·공격력이 높다.
- 등급이 높을수록 **잡는 데 든 시간 대비 보상도 더 크다**. 높은 등급을 잡는 재미를 주기 위해서다.
- 필드 전체 드랍 총량은 등급 도입 전과 같게 유지한다. 일반 몬스터의 몫을 높은 등급으로 옮긴다.
- 무작위 접사(PoE식 "빠름", "화염 강화" 등)를 나중에 붙일 수 있게 구조를 잡는다.

## 2. 결정 목록

| 번호 | 결정 |
|---|---|
| R1 | 등급은 종이 아니라 **개체** 속성이다. 몬스터 시트 행을 등급별로 복제하지 않는다. `MonsterTable.Archetype`은 종 기본 등급으로 남기고, 종 등급이 일반인 몬스터만 승격한다. |
| R2 | 등급은 **무리 단위**로 정한다. 마법 무리는 전원 마법, 희귀 무리는 첫 구성원 1마리만 희귀이고 나머지는 일반 호위다. |
| R3 | 확률은 Zone 시트 `MagicPackRate`·`RarePackRate`(%)에 둔다. 값은 전 구역 15 / 8(처음 8 / 4에서 "잘 안 보인다"는 판단으로 상향). 평균 무리 크기가 2라서 몬스터 비율로는 마법 15%, 희귀 약 4%다. 희귀 무리는 평균 12.5무리마다, 절반 확률로는 8무리 안에 만난다. |
| R4 | 1차 범위는 필드 스폰만이다. 던전은 후속 작업이다(§8). |
| R5 | 보상은 **판정 횟수**(`DropRolls`)와 **아이템 희귀도**(`DropRarityBonus`)로 준다. 판정 횟수는 2026-10-11부터 `DropRolls` + `DropRollsPerAffix` × 접사 수다(MONSTER_AFFIX_DESIGN A4). 2026-10-21부터 `DropRarityBonus`는 높은 Tier 가중치가 아니라 아이템 희귀도(몬스터 묶음)이고, 몬스터 시트 `DropRateBonus`(꽝 칸 줄이기)는 `DropQuantityBonus`(판정 횟수 곱하기, 21종 모두 0)로 바뀌었다(ITEM_DROP_BALANCE §8.9). |
| R6 | 시간당 보상 목표는 일반 1.0 : 마법 약 1.4 : 희귀 약 1.6이다(원소 공격 기준, §3.3). 2026-10-11 마법 HP를 2 → 1.3으로 낮춰 마법이 약 2.1로 희귀보다 높아졌다가, 같은 날 접사 도입으로 마법 평균 판정이 3 → 2.54회가 되어 약 1.76으로 내려왔다(접사 난이도 미포함). |
| R7 | 필드 드랍 총량을 유지하기 위해 ItemDrop 1번 행의 `NothingRate`를 10 → 43으로 올렸다(확률 8/4 때는 27). 접사 도입으로 마리당 평균 판정이 1.66 → 1.68회가 되어 44로 다시 맞췄다(2026-10-11). |
| R8 | 마법·희귀는 만피여도 HP바를 보여 주고, HP바를 등급 색(파랑·노랑)으로 칠한다. |

## 3. 등급 시트 (MonsterArchetype)

값은 시트가 정본이다. 아래는 2026-10-21 기준 값이다.

| 등급 | HpMul | DmgMul | ResistAdd | DropRolls | DropRarityBonus | ModTier11ChanceBp | ModTier12ChanceBp | AffixBaseCount | AffixExtraChance | DropRollsPerAffix | RarityPerAffix |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Normal | 1 | 1.3 | 0 | 1 | 0 | 50 | 10 | 0 | 10 | 1 | 5 |
| Magic | 1.3 | 1.56 | 10 | 1 (예전 3) | 20 (예전 50) | 100 | 20 | 1 | 35 | 1 | 5 |
| Rare | 5 | 1.95 | 20 | 6 (예전 10) | 60 (예전 150) | 200 | 50 | 1 | 50 | 2 | 5 |
| Boss | 20 | 2.6 | 0 | 1 | 0 | 500 | 100 | 0 | 0 | 0 | 5 |
| EndgameBoss | 60 | 2.6 | 0 | 1 | 0 | 1000 | 300 | 0 | 0 | 0 | 5 |
| Elite (마을 던전 보스) | 10 | 2.275 | 0 | 10 (예전 1) | 100 (예전 0) | 350 | 75 | 0 | 0 | 0 | 5 |
| Named | 12 | 2.2 | 25 | 10 | 100 (예전 250) | 400 | 100 | 0 | 0 | 1 | 5 |

- 2026-10-21(ITEM_DROP_BALANCE D20·D21): `DropRarityBonus`를 마법 20·희귀 60·네임드 100으로 낮추고, Elite 행을 판정 10회·희귀도 100으로 올렸다(던전 보스 사망 드롭). N열 `RarityPerAffix`(접사당 희귀도 5)를 추가했다.

- K~M열(`AffixBaseCount`·`AffixExtraChance`·`DropRollsPerAffix`, 2026-10-11 추가)은 접사 개수와 접사당 드랍 판정이다. 규칙은 [MONSTER_AFFIX_DESIGN.md](MONSTER_AFFIX_DESIGN.md) §3·§8.

- Boss·EndgameBoss의 새 칸(저항·보상)은 쓰는 곳이 없어 중립값으로 두었다.
- `ModTier11ChanceBp`·`ModTier12ChanceBp`(I·J열, 만분율, 2026-10-11 추가)는 드랍 장비 1개당 옵션 T11·T12 특수 판정 확률이다. 아이템 레벨 90 이상에서만 효과가 있다. 규칙은 [ITEM_LEVEL_MOD_TIER_DESIGN.md](../combat/ITEM_LEVEL_MOD_TIER_DESIGN.md) §4.
- 시트의 Normal `DmgMul`은 1.3이고, BALANCE_DESIGN §3.3 표에는 1.0으로 적혀 있다. 등급 간 공격력 비율(1.2배·1.5배)은 같다.
- 마법 `HpMul`은 2(BALANCE_DESIGN §3.3 예시값)였다가 2026-10-11 1.3으로 낮췄다.

### 3.1 스탯

- HP: `BaseMaxHp × HpPerLevel^(Level-1) × HpMul`
- 공격력: `BaseSkillDamage × DmgPerLevel^(Level-1) × DmgMul` (모든 스킬 데미지에 곱해짐)
- 저항: 화염·냉기·번개·독 기본 저항에 `ResistAdd`를 더한다. 곱하지 않는 이유는 저항 0인 종도 똑같이 단단해지게 하기 위해서다. 상한은 피해 계산(`DamageCalculator.GetResistanceReduction`)이 몬스터의 최대 저항(기본 75)으로 자른다.
- 물리 방어(Defense)는 바꾸지 않는다.

### 3.2 보상

- 사망 시 `DropRolls` + `DropRollsPerAffix` × 접사 수만큼 판정한다. 판정마다 기존 드랍 규칙(꽝·화폐·장비·스킬북·페이지)을 그대로 쓴다.
- 판정이 2회 이상이면 아이템이 한 점에 겹치지 않게 판정마다 반경 0.8칸 안으로 위치를 흩는다. 흩은 칸이 막혀 있으면 원래 위치에 떨군다.
- `DropRarityBonus`는 몬스터 시트의 보너스와 접사 수 × `RarityPerAffix`에 더해 아이템 희귀도(몬스터 묶음)가 된다. 장비가 희귀·마법으로 나올 확률과 Tier 2 이상 화폐(엑잘티드)의 비중을 올린다. 장비 Tier(구간)·옵션 티어·품질에는 영향이 없다. 계산식은 [ITEM_DROP_BALANCE_DESIGN.md](../combat/ITEM_DROP_BALANCE_DESIGN.md) §8.2 (2026-10-21, 예전에는 높은 Tier 장비 가중치였다).
- 판정 횟수에는 아이템 수량을 곱한다(× (1 + Q/100), 몬스터 1마리 상한 40회). 꽝 칸은 줄이지 않는다.
- 드랍 장비의 아이템 레벨은 몬스터 레벨이고, 옵션 티어는 아이템 레벨로 정해진다. 등급은 옵션 티어 중 T11·T12 특수 판정 확률만 올린다(§3 표, [ITEM_LEVEL_MOD_TIER_DESIGN.md](../combat/ITEM_LEVEL_MOD_TIER_DESIGN.md)).

평균 드랍 개수 (`NothingRate` 44 → 판정 1번에 약 47.6%, 접사 포함 평균):

| 등급 | 평균 판정 횟수 | 평균 개수 |
|---|---|---|
| Normal | 1.11 | 0.53 |
| Magic | 2.54 | 1.21 |
| Rare | 10.0 | 4.76 |
| 필드 평균(마리당, R3 비율) | 1.68 | 0.80 (등급 도입 전 0.8과 같음) |

`NothingRate`를 다시 맞출 때: 판정 1번당 확률 p = 0.8 ÷ (마리당 평균 판정 횟수), 꽝 값 = 40 × (1 − p) ÷ p (나머지 칸 합계가 40일 때). 마리당 평균 판정 횟수는 접사를 포함해 [MONSTER_AFFIX_DESIGN.md](MONSTER_AFFIX_DESIGN.md) §8.3 식으로 구한다(`ARPG/Diagnostics/Monster Affix Distribution` 진단이 권장값을 계산한다).

### 3.3 시간당 보상 계산

판정 횟수 = 잡는 시간 배율 × 목표 보상 배율. 잡는 시간에는 저항 보너스도 넣는다(저항 +10 → 약 1.11배, +20 → 1.25배, 저항 0인 종 기준).

| 등급 | 잡는 시간(원소 공격) | 판정 횟수 | 시간당 보상 | 물리 공격 빌드 |
|---|---|---|---|---|
| Normal | 1 | 1 | 1.0 | 1.0 |
| Magic | 약 1.44 | 3 | 약 2.1 | 2.3 |
| Rare | 약 6.25 | 10 | 약 1.6 | 2.0 |

위 표는 접사 도입 전 값이다. 접사 도입 후 평균 판정은 일반 1.11, 마법 2.54, 희귀 10.0회라 시간당 보상은 마법 약 1.76, 희귀 약 1.6이다(접사 난이도 미포함, MONSTER_AFFIX_DESIGN §8.4).

레벨 10 이상 구역부터는 Tier 보너스만큼 질적인 보상이 더해진다.

## 4. 스폰 판정

- `MonsterManager.TrySpawnSlot`이 무리마다 `RollPackArchetype(zone)`을 한 번 굴린다. 0~99 중 `[0, RarePackRate)`면 희귀, `[RarePackRate, RarePackRate + MagicPackRate)`면 마법, 나머지는 일반이다. 두 값의 합은 100 이하여야 한다.
- `BuildGroupSpawnPlan`이 구성원마다 `ResolveMemberArchetype`으로 등급을 정해 `PlannedMonsterSpawn.Archetype`에 담는다. 희귀 무리는 계획의 첫 구성원(위치 예약에 성공한 첫 개체)이 희귀다.
- Zone 행이 없으면(인스펙터 fallback) 일반이다. 시트에 칸이 없거나 비어 있으면 0으로 읽어 등급 무리가 나오지 않는다.
- 리스폰은 슬롯 쿨타임 뒤 새 무리로 다시 굴린다. Warm 청크는 엔티티를 그대로 보존하므로 재진입해도 등급이 유지되고, 따로 저장하지 않는다.
- 확률을 바꾸면 필드 드랍 총량도 바뀐다. 총량을 유지하려면 §3.2 식으로 `NothingRate`를 다시 계산한다.

## 5. 생성 순서 (`EntityFactory.CreateMonster`)

1. `AddCreatureComponents` — 스탯·HP바 등 공통 컴포넌트
2. 등급 결정: `archetypeOverride ?? MonsterTable.Archetype`
3. `ApplyMonsterArchetypeScaling` — HP·공격력 배율, 저항 가산, `StatDirtyTag` 재요청(HP바 대기 중 첫 재계산이 끝났어도 피격 감소 캐시가 새 저항을 반영하도록)
4. 무작위 접사 적용 — 무리 생성 때 굴린 접사를 받아 스탯 보정 등록·`MonsterAffixComponent` 부착([MONSTER_AFFIX_DESIGN.md](MONSTER_AFFIX_DESIGN.md) §6)
5. `MonsterArchetypeComponent { Archetype }` 부착 — 모든 몬스터
6. `DropComponent` — `DropRolls`(등급 기본 + 접사당 × 접사 수), `DropRarityBonus`(종 + 등급 합), `ModTier11ChanceBp`·`ModTier12ChanceBp`(등급), `MonsterLevel`(드랍 장비의 아이템 레벨)
7. `AutoRegisterChildHandlers` — `HpBarView`가 등급 컴포넌트를 읽는다

던전 몬스터는 `archetypeOverride`를 넘기지 않아 종 등급(현재 전부 일반)을 쓴다.

## 6. 표시

- `HpBarView`가 `MonsterArchetypeComponent`를 읽어 마법은 파랑, 희귀는 노랑으로 HP 채움 색을 바꾼다. 일반 몬스터·NPC·플레이어는 프리팹 색(초록)을 유지한다.
- 마법·희귀는 만피여도 HP바를 보여 교전 전에 등급을 알 수 있다. 시야 밖에서는 안개가 Visual째 숨긴다.

## 7. 무작위 접사 확장 지점 (미구현)

> 2026-10-11 접사를 [MONSTER_AFFIX_DESIGN.md](MONSTER_AFFIX_DESIGN.md) 기준으로 구현했다. 아래는 구현 전 확장 지점 기록이다. 아래와 다른 점: 개체가 아니라 **무리 단위**로 판정하고(희귀는 따로), 개수는 `AffixBaseCount`·`AffixExtraChance` 두 칸으로 정한다. §3의 `DropRolls`(마법 3 → 1, 희귀 10 → 6, 접사당 판정 추가)와 R7 `NothingRate`(43 → 44)가 바뀌었다.

이번 구현은 접사를 넣지 않았다. 붙일 때의 자리와 규칙만 정한다.

- **데이터**: 새 시트 `MonsterAffix` — Id, 표시 이름, 효과(Stat·ModifierType·Value, 또는 Buff·SkillEffect 참조), 가중치, 허용 등급, 최소 레벨. 등급 시트에 `AffixCountMin`·`AffixCountMax` 칸을 추가한다(예: 마법 1~2, 희귀 3~4). 지금은 쓰는 곳이 없어 칸을 만들지 않았다.
- **판정 시점**: 개체 생성 시 등급이 정해진 뒤, 등급 스케일링 다음(§5의 4번). 무리 단위 판정(§4)과 달리 개체마다 굴린다.
- **적용 방식**: 등급 배율은 기본값(Base)을 바꾸고, 접사는 `StatModifier`로 붙인다. 출처는 새 `StatModifierSource.MonsterAffix`(SourceId = 접사 Id)다. 접사는 출처별로 조회·표시·제거할 수 있어야 하고, 버프·장비와 같은 합산 규칙(`System_StatCalculation`)을 따라야 하기 때문이다.
- **상태**: 별도 `MonsterAffixComponent`(고정 슬롯의 접사 Id)에 둔다. `MonsterArchetypeComponent`에 섞지 않는다.
- **표시**: 접사 이름표는 별도 View(`IEntityMessageHandler`)로 만든다.
- **스킬형 접사**(죽을 때 폭발, 오라 등)는 Buff·SkillEffect 경로를 재사용하는 쪽으로 검토한다.
- **던전 영속**: 던전 몬스터에 등급·접사를 줄 때는 `DungeonObject`에 함께 기록해야 재입장 시 같다(§8).

## 8. 후속 작업

- **던전 등급**: `DungeonObject`에 `Archetype`(이후 접사 Id) 필드 추가 — 던전 저장 형식이 바뀐다. `DungeonLayout`에서 결정론적으로 판정한다. 던전 보스를 Boss 등급으로 올릴지도 함께 정한다.
- **던전 일반 몬스터 드랍 감소**: 던전 몬스터도 ItemDrop 1번을 써서, R7로 던전 일반 몬스터 드랍이 40% 줄었다(0.8 → 0.48개). → 2026-10-21 던전 장소 보너스(수량 +70%)로 해결했다(ITEM_DROP_BALANCE §7.6, 던전 표 `DropQuantityBonus`).
- **구역별 확률 차등**: 초반 구역(3~4)의 희귀 확률을 낮추는 등.
- **골드 밸런스**: 필드 총량은 유지했지만 희귀 몬스터 한 마리가 6개를 떨군다. GOLD_BALANCE_DESIGN의 플레이어 판매 가정과 대조가 필요하다.
- **BALANCE_DESIGN §3.3**: 표의 Normal `DmgMul` 1.0과 시트 1.3이 다르다.

## 9. 검증

- 2026-10-11: Runtime·Editor 컴파일 통과. MonsterArchetype·ItemDrop 테이블 재다운로드, bytes diff가 의도한 칸만 바뀐 것 확인.
- 2026-10-11: Zone 시트 N·O열 추가(15/8), ItemDrop `NothingRate` 43. ZoneTable·DropTable 재다운로드, diff가 해당 칸만 바뀐 것 확인.
- 플레이 확인 항목(미실행):
  1. 마법·희귀 무리 등장 빈도(희귀 무리는 1마리만 노랑 HP바)
  2. 마법·희귀 HP바가 만피여도 보이고 색이 맞는지
  3. 원소 공격 데미지 숫자가 저항만큼 줄어드는지
  4. 희귀 처치 시 여러 개가 흩어져 떨어지는지, 벽 옆에서 벽 안으로 떨어지지 않는지
  5. Warm 청크 재진입 시 등급 유지
