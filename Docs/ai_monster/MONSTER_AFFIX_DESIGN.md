# 몬스터 접사 설계

> 상태: 필드 구현 (2026-10-11). 컴파일·테이블·개수 분포 확인 완료, 플레이 모드 확인 전(§13). 2026-10-15 스킬 페이지 같은 효과의 스탯형 접사 3종(112~114, §7.1) 추가·플레이 모드 확인
> 작성일: 2026-10-11
> 범위: 필드 몬스터의 무작위 접사(스탯형·오라형), 무리 단위 접사 판정, 접사 수에 따른 드랍 판정, 접사 이름표. 던전은 후속(§12).
> 관련: [MONSTER_RARITY_DESIGN.md](MONSTER_RARITY_DESIGN.md) §3·§7(이 문서가 §7 확장 지점을 확정한다), [combatSystem.md](../combat/combatSystem.md) §4(피해 계산), [BALANCE_DESIGN.md](../combat/BALANCE_DESIGN.md)

---

## 1. 목표

- 몬스터가 PoE처럼 무작위 접사를 가진다. 접사는 스탯 보정(물리 피해 증가, 치명타 확률 증가, 치명타 피해 증가 등)이거나 주변 아군을 강화하는 오라다.
- 일반은 거의 없고 가끔 1개, 마법은 1~2개, 희귀는 1~3개 정도를 가진다. 운이 좋으면 더 붙고, 최대 10개다.
- 새 접사는 시트 행 추가만으로 만든다. 새 효과 **종류**(죽을 때 폭발 등)만 코드가 필요하다.
- 접사가 많아 더 어려운 개체는 드랍도 많다. 필드 드랍 총량은 지금과 같게 유지한다.

## 2. 결정 목록

| 번호 | 결정 |
|---|---|
| A1 | 접사 개수 = 기본 개수 + 추가 판정 반복. 추가 판정에 성공할 때마다 1개씩 더하고, 실패하거나 10개가 되면 멈춘다. 등급 시트에 `AffixBaseCount`·`AffixExtraChance` 두 칸만 둔다(§3). |
| A2 | 값: 일반 0개 + 10%, 마법 1개 + 35%, 희귀 1개 + 50%. 기본 개수 칸은 마법·희귀가 접사 0개로 나오지 않게 하려고 둔다. |
| A3 | 접사는 **무리 단위**로 판정한다. 일반·마법 무리는 한 번 굴려 전원이 같은 접사를 갖는다. 희귀 무리는 희귀 1마리를 희귀 확률로 따로 굴리고, 일반 호위는 일반 확률로 한 번 굴려 호위끼리 공유한다(§4). MONSTER_RARITY_DESIGN §7의 "개체마다 굴린다"를 대체한다. |
| A4 | 드랍 판정 = `DropRolls` + `DropRollsPerAffix` × 접사 수. 기본 판정을 마법 3 → 1, 희귀 10 → 6으로 낮추고, 접사 1개당 일반 +1, 마법 +1, 희귀 +2를 더한다(§8). 2026-10-21부터 접사 1개당 아이템 희귀도 +5%(등급 시트 `RarityPerAffix`)도 준다(§8.5, ITEM_DROP_BALANCE D15). |
| A5 | 필드 총량 유지를 위해 ItemDrop 1번 행 `NothingRate`를 43 → 44로 올린다. |
| A6 | 접사 정의는 새 시트 `MonsterAffix`. 값은 행마다 고정이고, 티어는 같은 `Group`에 `MinLevel`이 다른 행으로 만든다. 1차는 % 값만 쓴다(고정값은 레벨에 따라 의미가 달라진다). |
| A7 | 효과 종류는 `Stat`(자기 스탯 보정)과 `Aura`(반경 안 아군에게 버프) 두 가지다. 오라는 기존 버프 시스템을 재사용하고, 버프 시트에 스탯 칸을 추가한다(§6.3). |
| A8 | "물리 피해 % 증가"를 걸 곳이 없으므로 속성별 피해 증가 스탯 6종을 새로 만든다. 스킬 공격 프로필의 속성별 피해 합계에 곱한다(§6.2). |
| A9 | 오라 접사는 마법·희귀 전용이다. 일반에 붙으면 무리 전체가 강해지기 때문이다. 적 대상 오라(플레이어 디버프)는 1차에서 뺀다. |
| A10 | 접사 이름은 HP바 아래 이름표로 보여 준다. 4개가 넘으면 앞 4개 + "+N"으로 줄인다. |
| A11 | 1차 범위는 필드만이다. 던전은 등급 도입과 함께 후속 작업이다. |

## 3. 접사 개수 판정

```text
개수 = AffixBaseCount
반복: 개수 < 10 이고 Random(0~99) < AffixExtraChance 이면 개수 += 1, 아니면 멈춤
```

- 확률: P(개수 = 기본 + k) = p^k × (1 − p). 10개는 p^(10 − 기본).
- 평균: 기본 + Σ(k=1..10−기본) p^k.
- 상한 10은 코드 상수다. `MonsterAffixComponent`의 칸 수와 묶여 있다.
- 뽑을 수 있는 후보 접사가 모자라면 그 자리에서 멈춘다(§5.2).

등급 시트 값과 100마리 기준 분포:

| 등급 | AffixBaseCount | AffixExtraChance | 0개 | 1개 | 2개 | 3개 | 4개 | 5개 이상 | 평균 |
|---|---|---|---|---|---|---|---|---|---|
| Normal | 0 | 10 | 90 | 9 | 0.9 | 0.1 | – | – | 0.11 |
| Magic | 1 | 35 | – | 65 | 22.8 | 8.0 | 2.8 | 1.5 | 1.54 |
| Rare | 1 | 50 | – | 50 | 25 | 12.5 | 6.3 | 6.2 | 2.0 |
| Boss·EndgameBoss | 0 | 0 | 100 | – | – | – | – | – | 0 |

- 희귀 10개는 0.2%(희귀 512마리 중 1마리)다.
- 필드 몬스터 비율(일반 81%, 마법 15%, 희귀 4%, MONSTER_RARITY_DESIGN R3) 기준으로 몬스터 100마리 중 약 27마리가 접사를 가진다.

## 4. 무리 단위 판정

### 4.1 규칙

| 무리 등급 | 판정 |
|---|---|
| 일반 무리 | 일반 확률로 1회. 전원 공유 |
| 마법 무리 | 마법 확률로 1회. 전원 공유 |
| 희귀 무리 | 희귀 구성원(계획의 첫 구성원)은 희귀 확률로 따로 1회. 일반 호위는 일반 확률로 1회, 호위끼리 공유 |

- 예: 마법 늑대 3마리 무리가 "빠른"을 뽑으면 3마리 모두 이동 속도 +25%다.
- 예: 희귀 무리에서 희귀 1마리는 "강력한·분노의 오라", 호위 2마리는 접사 없음(90% 확률).
- 종 등급이 일반이 아닌 몬스터(`MonsterTable.Archetype`이 Boss 등)는 자기 등급 행 값(현재 0개)을 쓴다.

### 4.2 판정 시점과 데이터 흐름

1. `MonsterManager.TrySpawnSlot` → `BuildGroupSpawnPlan`이 구성원 종·위치·등급을 확정한다.
2. 계획이 만들어진 뒤 접사를 굴린다. 종이 확정된 뒤라야 피해 종류 조건(§5.2)을 검사할 수 있기 때문이다.
3. 결과를 `PlannedMonsterSpawn`에 담아 `EntityFactory.CreateMonster`에 넘긴다. 등급(`archetypeOverride`)을 넘기는 것과 같은 방식이다. `EntityFactory`는 굴리지 않고 적용만 한다.
4. 판정 로직은 상태 없는 순수 로직이므로 정적 클래스(예: `MonsterAffixRoller`)에 둔다. 던전 배치도 나중에 같은 로직을 쓴다. 후보 목록은 매번 비우고 쓰는 스크래치 버퍼로 둔다.

### 4.3 종이 섞인 무리

`GroupMode.Mixed` 무리는 구성원마다 종이 다르다. 공유 접사의 피해 종류 조건(`RequiredDamageType`)은 **판정 대상 구성원 전원**이 그 종류의 스킬을 가질 때만 통과한다. 희귀 구성원은 따로 굴리므로 자기 종만 본다.

## 5. 접사 시트 `MonsterAffix` (신규)

### 5.1 칸

| 칸 | 뜻 | 예 |
|---|---|---|
| Id | | 101 |
| Name | 이름표에 표시할 이름 | 강력한 |
| Kind | `Stat` / `Aura` | Stat |
| Group | 같은 그룹은 한 몬스터에 중복 금지(티어 구분용) | PhysDmg |
| Weight | 뽑기 가중치 | 100 |
| MinLevel | 몬스터 레벨이 이 값 이상일 때만 후보 | 1 |
| AllowedArchetypes | 붙을 수 있는 등급. 새 비트 enum `MonsterArchetypeMask`(Normal=1, Magic=2, Rare=4, All=7) | All |
| RequiredDamageType | 이 피해 종류의 스킬이 있는 몬스터에만 후보. `None`이면 제한 없음 | Physics |
| Stat1·ModType1·Value1 ~ Stat3·ModType3·Value3 | Kind=Stat일 때 거는 스탯 보정(`GE.Stat`, `StatModifierType`, 값) | PhysDamageInc / Multiply / 30 |
| AuraBuffId | Kind=Aura일 때 반경 안에 거는 버프(BuffTable Id) | 101 |
| AuraRadius | Kind=Aura일 때 반경(칸) | 6 |

- enum은 기존 시트처럼 이름 문자열로 적는다.
- 적 대상 오라용 `AuraTarget` 칸은 쓰는 곳이 생길 때 추가한다(§12).

### 5.2 뽑기

1. 후보 = `AllowedArchetypes`에 등급이 포함되고, `MinLevel` ≤ 몬스터 레벨이며, `RequiredDamageType` 조건을 통과하는 행.
2. 이미 뽑은 접사와 `Group`이 같은 행은 제외한다.
3. 남은 후보에서 `Weight`로 하나를 뽑는다. 개수만큼 반복한다.
4. 후보가 비면 거기서 멈춘다. 개수가 목표보다 적어도 오류가 아니다.

`RequiredDamageType` 검사: `MonsterTable.AiTable`의 스킬 목록에서 `SkillTable.DamageType`을 본다.

## 6. 효과 적용

### 6.1 스탯형

- `EntityFactory.CreateMonster`의 등급 스케일링 다음 자리([EntityFactory.cs:101](../../Assets/Scripts/Factory/EntityFactory.cs#L101))에서 `AR.s.Data.AddStatModifier`로 등록한다.
- 출처는 새 `StatModifierSource.MonsterAffix`, `SourceId` = 접사 Id다. 몬스터가 죽으면 기존 정리 경로(`DataManager.RemoveAllModifiers`)가 함께 지운다.
- 등록 후 `StatDirtyTag`로 재계산을 요청한다.
- **생명력 증가 접사:** `ApplyMonsterArchetypeScaling`이 현재 HP를 등급 반영 최대치로 채운 뒤 접사 보정이 붙는다. 재계산에서 최대 HP만 늘고 현재 HP는 그대로라 HP바가 덜 찬 채로 나온다. 스폰 후 첫 재계산에서 현재 HP를 최대치로 채우는 처리가 필요하다(예: 일회용 태그를 `System_StatCalculation`이 소비).
- 상태는 `MonsterAffixComponent`(접사 Id 고정 10칸, 개수, 오라 개수)에 둔다. `MonsterArchetypeComponent`와 섞지 않는다. 풀 크기는 256(등급 컴포넌트와 같음).

### 6.2 새 스탯: 속성별 피해 증가

**문제:** 몬스터 피해는 대부분 스킬 표의 `DamageMin/Max`에서 나온다. 물리 공격력(`AttackMin/Max`) % 보정은 스탯 표 기본 공격력(현재 1~2)에만 곱해진다([SkillOffensiveStatResolver.cs](../../Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs) `ResolveValue`). 장비의 `IncreasedDamage`(OnCalculate) 옵션도 `ModPoolComponent`에 등록만 되고 피해 계산에서 쓰이지 않는다.

**추가:** `GE.Stat`에 `PhysDamageInc`, `FireDamageInc`, `IceDamageInc`, `LightningDamageInc`, `PoisonDamageInc`, `DamageInc`(전체).

- `System_SkillAttackProfileCalculation.BuildProfile`에서 속성별 합계(무기 + 스탯 + 스킬 표 + 페이지)에 `(1 + (속성 증가 + 전체 증가) / 100)`을 곱한다.
- 예: 스킬 피해 20~30 + 강력한(+30%) → 26~39.
- 태그 조건 보정도 받을 수 있도록 `SkillOffensiveStatResolver`의 지원 목록·스냅샷에 넣는다.
- 플레이어 장비의 "피해 % 증가" 옵션도 이 스탯을 쓸 수 있다. 이 보정을 가진 대상이 없으면 플레이어 피해는 바뀌지 않는다.
- enum 위치: SkillEffect 구역(`LifeSteal`) 앞, `ProjectileCountAdd` 뒤에 넣는다. 스탯 버킷 크기(`STAT_BUCKET_COUNT = DelegateToTotem + 1`)가 그대로 새 값을 포함한다. 시트는 enum을 이름으로 저장하므로 중간 삽입이 안전하다.

기존 스탯으로 바로 되는 것: 치명타 확률 증가(`CriRateMul`), 치명타 피해(`CriDamageMul`, 배율 = 1.5 + 값/100), 이동 속도(`MoveSpeed` Multiply), 최대 생명력(`Hp` Multiply), 방어도(`Defense` Multiply), 원소 저항, 회피.

몬스터 스탯 시트의 현재 치명타는 5%, 치명타 피해 +10(1.6배)이다. 몬스터는 무기가 없어 공격 스킬의 무기 치명타가 0이므로, 치명타 확률은 캐릭터 기본값 5%에 증가율이 곱해진다.

### 6.3 오라형

**버프 시트 확장:** 지금 버프 시트는 효과 종류 1개와 값 1개만 있고, 스탯 버프는 코드 하드코딩 예시(Id 1001~1003, [BuffHelper.cs:439](../../Assets/Scripts/Common/Utility/BuffHelper.cs#L439))뿐이다.

- BuffTable에 `Stat1·ModType1·Value1 ~ Stat3·ModType3·Value3` 칸을 추가한다.
- 새 `BuffEffectType.StatModifier`가 이 칸을 읽어 `StatModifierSource.Buff`로 등록한다.
- 기존 버프 5종(출혈·점화·냉동·중독·둔화)은 바뀌지 않는다.
- 오라 버프 Id는 하드코딩 예시와 겹치지 않게 101번대를 쓴다.

**`System_MonsterAura` (신규):**

- 게임플레이 우선순위 범위(100~500), `UpdateInterval` 0.5초, Update 단계.
- `MonsterAffixComponent` 풀에서 오라 개수가 1 이상인 몬스터만 본다.
- 오라마다 반경 안의 같은 진영 엔티티(자기 자신 포함)에 오라 버프를 건다. 대상 검색은 기존 시스템처럼 `FactionComponent` 풀을 돌며 `SqrMagnitude`로 비교한다.
- 버프 지속은 1초(검사 주기의 2배)다. 이미 걸려 있으면 `BuffInstance`의 남은 시간만 직접 되돌린다. **`BuffHelper.AddBuff`의 갱신 경로는 매번 `StatDirtyTag`를 붙이므로**([BuffHelper.cs:120](../../Assets/Scripts/Common/Utility/BuffHelper.cs#L120)) 오라 갱신에는 쓰지 않는다. 처음 걸 때만 `AddBuff`를 쓴다.
- 오라 주인이 죽거나 반경을 벗어나면 갱신이 끊겨 1초 안에 버프가 사라진다.
- 같은 오라를 가진 몬스터가 여럿이어도 버프는 하나다(버프 엔티티 Id = 대상 + 버프 Id, `MaxStack` 1). 서로 다른 오라는 겹친다.
- 예: "분노의 오라" 희귀 1마리와 일반 호위 3마리가 있으면 반경 6칸 안 4마리 모두 물리 피해 +20%다. 희귀를 먼저 잡으면 1초 뒤 호위의 버프가 꺼진다.

## 7. 초기 접사 목록

희귀가 10개까지 갈 수 있도록 14종으로 시작했고, 2026-10-15에 스킬 페이지 같은 효과를 내는 스탯형 3종(112~114)을 더해 17종이다. 가중치는 전부 100, `MinLevel` 1이다. 수치는 플레이 확인 후 조정한다.

| Id | 이름 | 종류 | 효과 | 허용 등급 | 피해 종류 조건 |
|---|---|---|---|---|---|
| 101 | 강력한 | Stat | `PhysDamageInc` +30% | All | Physics |
| 102 | 맹렬한 | Stat | `DamageInc` +20% | Magic·Rare | – |
| 103 | 정밀한 | Stat | `CriRateMul` +60 (5% → 8%) | All | – |
| 104 | 잔혹한 | Stat | `CriDamageMul` +40 (1.6배 → 2.0배) | All | – |
| 105 | 빠른 | Stat | `MoveSpeed` +25% | All | – |
| 106 | 단단한 | Stat | `Hp` +25% | All | – |
| 107 | 철갑의 | Stat | `Defense` +50% | All | – |
| 108 | 불타는 | Stat | `FireResist` +30 | All | – |
| 109 | 얼어붙은 | Stat | `IceResist` +30 | All | – |
| 110 | 번개 맞은 | Stat | `LightningResist` +30 | All | – |
| 111 | 날렵한 | Stat | `Evasion` +15 | Magic·Rare | – |
| 112 | 난사하는 | Stat | `ProjectileCountAdd` +1 (발사체 스킬이 한 발 더) | Magic·Rare | – |
| 113 | 피에 굶주린 | Stat | `BloodingRate` +20 (물리 피해 타격마다 출혈 확률 +20%) | All | Physics |
| 114 | 작열하는 | Stat | `IgniteRate` +20 (화염 피해 타격마다 점화 확률 +20%) | All | Fire |
| 201 | 분노의 오라 | Aura | 버프 101: `PhysDamageInc` +20%, 반경 6 | Magic·Rare | Physics |
| 202 | 신속의 오라 | Aura | 버프 102: `MoveSpeed` +15%, 반경 6 | Magic·Rare | – |
| 203 | 수호의 오라 | Aura | 버프 103: `Defense` +30%, 반경 6 | Magic·Rare | – |

오라 버프 행(BuffTable 101~103): `BuffType` Buff, `Duration` 1, `MaxStack` 1, `IsDispellable` false, `BuffEffectId` StatModifier.

- 분노의 오라(201)는 구현 때 피해 종류 조건을 Physics로 바꿨다. 물리 스킬이 없는 무리에 붙으면 받는 쪽도 효과가 없기 때문이다.
- 치명타 피해(`CriDamageMul`)는 반드시 `Add`로 적는다. `Multiply`면 기본값 10의 %가 되어 +40%가 +4로 줄어든다.
- 시트 `MonsterAffix` 탭의 T열 `Note`는 사람이 읽는 설명이고 다운로드하지 않는다(범위 A:S).

### 7.1 스킬 페이지 같은 효과를 내는 스탯형 접사 (2026-10-15 사용자 결정)

스탯형 접사는 스킬이 아니라 몬스터 본인에게 보정으로 붙는다. 스킬은 쓸 때마다 시전자 스탯을 읽으므로, 아래 스탯은 **그 몬스터가 쓰는 모든 스킬**에 스킬 페이지처럼 작용한다. 코드 변경 없이 시트 행만 추가했다.

| 스탯 | 작용 | 읽는 곳 |
|---|---|---|
| `ProjectileCountAdd` | 모든 발사체 스킬이 `BaseProjectileCount + 값`발을 15° 부채꼴로 쏜다. 바닥에 장판을 까는 발사체(독거미 거미줄 207 등)는 원래 한 발이라 늘지 않는다 | [System_Skill.cs](../../Assets/Scripts/Common/System/System_Skill.cs) `ProcessSkillHit`·`LaunchAreaDeliveryProjectile` |
| `BloodingRate` | 물리 피해가 들어간 타격마다 이 확률(%)로 출혈(버프 1) | [DamageCalculator.cs](../../Assets/Scripts/Skill/Combat/DamageCalculator.cs) `ApplyBleeding` |
| `IgniteRate` | 화염 피해가 들어간 타격마다 이 확률(%)로 점화(버프 2, 화염 피해의 50%를 4초) | [DamageCalculator.cs](../../Assets/Scripts/Skill/Combat/DamageCalculator.cs) `ApplyIgnite` |

- 몬스터 스탯 시트의 기본 출혈·점화 확률은 0 또는 5라, 접사가 붙으면 20~25%가 된다.
- 난사하는(112)은 피해 종류 조건이 없어 발사체 스킬이 없는 근접 몬스터에도 붙을 수 있다. 그 경우 이름표만 뜨고 효과는 없다. 사용자 결정으로 그대로 둔다("운이니까 상관없다"). 원거리 몬스터는 가까이서 두 발을 다 맞히면 피해가 두 배라 맹렬한(102)처럼 마법·희귀 전용으로 뒀다.
- 지금 스탯형으로 **안 되는** 것:
  - 흡혈(`LifeSteal`): 스탯 계산이 이 보정을 처리하지 않아 무시된다([System_StatCalculation.cs](../../Assets/Scripts/Common/System/System_StatCalculation.cs) `ApplyModifier`에 경우 없음).
  - 중독 확률(`PoisonRate`): 계산·소비 경로가 없다.
  - 넉백·적중 시 버프·토템·연쇄 같은 발동형 효과: 스탯이 아니라 효과 실행이라 새 접사 종류가 필요하다(§12 "새 효과 종류").

## 8. 드랍 판정

### 8.1 등급 시트 값

| 등급 | DropRolls (지금 → 변경) | DropRollsPerAffix | 평균 판정 (지금 → 변경) |
|---|---|---|---|
| Normal | 1 → 1 | 1 | 1 → 1.11 |
| Magic | 3 → **1** | 1 | 3 → **2.54** |
| Rare | 10 → **6** | 2 | 10 → **10.0** |
| Boss·EndgameBoss | 1 → 1 | 0 | 1 → 1 |

`DropComponent.DropRolls` = `Max(1, DropRolls + DropRollsPerAffix × 접사 수)`. 계산 자리는 `EntityFactory.CreateMonster`의 `DropComponent` 생성부다.

### 8.2 접사 수별 평균 드랍 개수

`NothingRate` 44 기준으로 판정 1번에 47.6%다.

| 등급 | 접사 0 | 1 | 2 | 3 | 4 | 10 |
|---|---|---|---|---|---|---|
| Normal | 0.48 (1회) | 0.95 (2회) | 1.43 | – | – | – |
| Magic | – | 0.95 (2회) | 1.43 (3회, 지금과 같음) | 1.90 | 2.38 | – |
| Rare | – | 3.81 (8회) | 4.76 (10회, 지금과 같음) | 5.71 | 6.67 | 12.4 (26회) |

등급 평균은 일반 0.53, 마법 1.21, 희귀 4.76개다.

### 8.3 필드 총량과 NothingRate

```text
마리당 평균 판정 = Σ 등급 몬스터 비율 × (DropRolls + DropRollsPerAffix × 평균 접사 수)
               = 0.81 × 1.11 + 0.15 × 2.54 + 0.04 × 10.0 = 1.68   (지금 1.66)
판정 1번당 확률 p = 0.8 ÷ 마리당 평균 판정 = 0.476
NothingRate = 40 × (1 − p) ÷ p = 44   (나머지 칸 합계가 40일 때)
```

- 무리 공유 판정(A3)은 평균을 바꾸지 않는다. 같은 접사가 무리에 몰려 나올 뿐이다.
- 등급 확률·추가 판정 확률·판정 값을 바꾸면 위 식으로 `NothingRate`를 다시 계산한다. 이 식은 MONSTER_RARITY_DESIGN §3.2의 식을 접사까지 넓힌 것이다.
- 2026-10-21부터 판정 횟수에 아이템 수량을 곱한다(판정 = 기본 판정 × (1 + Q/100), ITEM_DROP_BALANCE §8.2). 총량 유지 기준은 레벨 1~9 구간이고 그 구간의 장소 수량 보너스는 0%라 위 식과 44는 그대로다. 진단은 구간 수량을 곱한 판정 수로 권장값을 계산한다(`MonsterAffixDiagnostics.AppendDropRolls`).

### 8.5 아이템 희귀도 (2026-10-21, ITEM_DROP_BALANCE D15)

- 접사 1개당 아이템 희귀도 +5%를 몬스터 묶음(③)에 더한다. 값은 등급 시트 N열 `RarityPerAffix`(모든 등급 5)다. 접사 수는 판정 횟수에 쓰는 수와 같다(네임드의 고정 접사 포함).
- 수량 보너스는 주지 않는다. 접사가 이미 판정 횟수로 양을 늘리기 때문이다. 그래서 필드 총량과 꽝 칸 44는 바뀌지 않는다.
- 계산 자리는 `EntityFactory.CreateMonster`의 `DropComponent.DropRarityBonus`(= 종 + 등급 + 접사 수 × `RarityPerAffix`)다. 희귀도의 효과(장비 등급·상위 화폐)는 [ITEM_DROP_BALANCE_DESIGN.md](../combat/ITEM_DROP_BALANCE_DESIGN.md) §8.

### 8.4 시간당 보상

MONSTER_RARITY_DESIGN §3.3 기준이다(원소 공격, 접사 난이도는 넣지 않은 값).

| 등급 | 잡는 시간 | 평균 판정 | 시간당 보상 (지금 → 변경) |
|---|---|---|---|
| Normal | 1 | 1.11 | 1.0 → 1.11 |
| Magic | 약 1.44 | 2.54 | 2.1 → **1.76** |
| Rare | 약 6.25 | 10.0 | 1.6 → 1.6 |

- 마법의 시간당 보상이 희귀보다 높던 문제(R6 "판정 횟수 조정 여부 미정")가 줄어든다.
- 접사는 잡는 시간을 늘리고, 그만큼 접사당 판정을 더한다. 개체별 난이도와 보상이 함께 오르내린다.

## 9. 생성 흐름 요약

```text
MonsterManager.TrySpawnSlot
  RollPackArchetype → BuildGroupSpawnPlan (종·위치·등급 확정)
  → 접사 판정: 무리 공유 1회 (희귀 무리는 희귀 1회 + 호위 1회)
  → PlannedMonsterSpawn에 접사 담기
EntityFactory.CreateMonster(..., archetypeOverride, affixes)
  AddCreatureComponents
  ApplyMonsterArchetypeScaling
  접사 적용: 스탯 보정 등록, MonsterAffixComponent 부착, StatDirtyTag, 첫 재계산 HP 채움 요청
  MonsterArchetypeComponent
  DropComponent (DropRolls + DropRollsPerAffix × 접사 수)
  AutoRegisterChildHandlers (HpBarView, 접사 이름표 View)
System_MonsterAura (0.5초): 오라 버프 걸기·갱신
```

## 10. 코드·시트 지도

| 대상 | 위치 | 할 일 |
|---|---|---|
| 스탯 enum | `Common/GlobalEnum.cs` `Stat` | 피해 증가 6종 추가(§6.2 위치) |
| 스탯 계산 | `Common/System/System_StatCalculation.cs` `ApplyModifier` | 새 스탯을 담을 Final 필드 반영(태그 없는 보정) |
| 공격 프로필 | `Common/System/System_SkillAttackProfileCalculation.cs` `BuildProfile`, `Skill/Combat/SkillOffensiveStatResolver.cs` | 속성별 합계에 증가 배율 곱하기, 태그 지원 목록·스냅샷 |
| 보정 출처 | `Common/Component/StatModifierBuffer.cs` `StatModifierSource` | `MonsterAffix` 추가 |
| 버프 | `Common/Tables.cs` `BuffTable`, `Common/Utility/BuffHelper.cs` `LoadBuffEffects`, `GlobalEnum.BuffEffectType` | 스탯 칸 3세트, `StatModifier` 효과 |
| 등급 시트 | `Common/Tables.cs` `MonsterArchetypeTable`, `Editor/DownloadTables.cs`(범위 `A:J` → `A:M`) | `AffixBaseCount`·`AffixExtraChance`·`DropRollsPerAffix` |
| 접사 시트 | `Common/Tables.cs` 새 `MonsterAffixTable`, `Editor/DownloadTables.cs`, `Data/DataManager_Table.cs` | 새 시트 등록·로드·조회 |
| 판정 | `Manager/MonsterManager.cs` `TrySpawnSlot`·`BuildGroupSpawnPlan`·`PlannedMonsterSpawn` + 새 `MonsterAffixRoller` | 무리 단위 판정 |
| 적용 | `Factory/EntityFactory.cs` `CreateMonster` | 접사 인자, 보정 등록, 드랍 판정 |
| 컴포넌트 | 새 `MonsterAffixComponent`, `Manager/ComponentManager.cs` `_poolSizes` | 256 |
| 오라 | 새 `System_MonsterAura`, `Manager/SystemManager.cs` | 우선순위 주석과 함께 등록 |
| 이름표 | 새 View(`IEntityMessageHandler`), UICanvas 프리팹 | HP바 아래, 4개 초과 시 "+N". 안개는 Visual째 숨기므로 따로 처리하지 않는다 |
| HP 채움 | `System_StatCalculation` | 스폰 후 첫 재계산에서 현재 HP = 최대 HP |
| 진단 | `Editor/MonsterAffixDiagnostics.cs` (`ARPG/Diagnostics/Monster Affix Distribution`) | 개수 분포·드랍 판정·권장 NothingRate·종별 후보 부족 |

**구현 메모 (2026-10-11):**
- 접사 컴포넌트는 `Common/Component/MonsterAffixComponent.cs`(고정 10칸, `ModPoolComponent`와 같은 방식), HP 채움 태그는 `SpawnHpRefillTag`, 판정은 `Creature/MonsterAffixRoller.cs`(namespace `ARPG.Monster`)다.
- `MonsterManager.AssignGroupAffixes`는 "같은 무리에서 같은 등급인 구성원끼리" 한 번 굴린다. 희귀 무리는 희귀 1마리와 일반 호위로 자연히 나뉜다. 종 등급이 Boss 같은 구성원은 자기 등급 행(0개)을 쓴다.
- `System_MonsterAura`는 Priority 140, Update 단계, 0.5초 간격이다. 오라 주인·대상 모두 살아 있고, 제거 예약이 없고, 거리 비활성화되지 않은 엔티티만 다룬다.
- 이름표는 접사가 있는 몬스터에서만 글자 오브젝트를 만든다(TMP 기본 폰트 BMJUA). HP바는 접사가 있으면 만피여도 보인다(이름표가 같은 캔버스에 있어서).
- 버프 시트 스탯 칸·새 피해 증가 스탯은 다른 버프·플레이어 장비가 쓰지 않으면 동작을 바꾸지 않는다.

**함정:**
- 시트 MCP로는 열을 추가할 수 없다. 새 칸이 그리드 밖이면 사용자에게 열 삽입을 부탁한다.
- 희귀 26회 판정은 아이템 12개 안팎이 반경 0.8칸 안에 흩어진다. 플레이 확인 때 겹침을 본다.
- 버프 하드코딩 예시 Id 1001~1003과 오라 버프 Id가 겹치면 예시 효과까지 같이 붙는다.

## 11. 진행 순서

1. **준비** (동작 변화 없음): 피해 증가 스탯 6종과 프로필 반영, `StatModifierSource.MonsterAffix`, 버프 시트 스탯 칸과 `StatModifier` 효과. 이 보정을 쓰는 데이터가 없으므로 동작이 바뀌지 않는다.
2. **본체**: 등급 시트 3칸, `MonsterAffix` 시트·로드, 무리 단위 판정, 접사 적용·드랍 판정, HP 채움, `System_MonsterAura`, 이름표 View, 시트 작성(등급 값, 접사 14행, 오라 버프 3행, `NothingRate` 44)과 재다운로드.
3. **검증 1회** (§13).

## 12. 후속 작업

- **던전**: `DungeonObject`에 등급·접사 Id를 기록해 재입장 시 같게 한다(MONSTER_RARITY_DESIGN §8 던전 등급과 함께).
- **개수 증가 요인**: 지역 레벨·던전 깊이에 따라 추가 판정 확률을 더하는 칸(예: Zone `AffixExtraChanceAdd`).
- **적 대상 오라**: `AuraTarget` 칸, 플레이어 디버프 표시.
- **오라 범위 표시**: 바닥 원 등 시각 효과(아트 필요).
- **새 효과 종류**: 죽을 때 폭발, 적중 시 버프 등. SkillEffect·Buff 경로를 재사용하는 쪽으로 검토한다.
- **고정값 접사**: 레벨 스케일링 규칙을 정한 뒤 추가한다.
- **골드 밸런스**: 희귀 개체 편차가 커졌다(접사 1개 3.8개 ~ 10개 12.4개). GOLD_BALANCE_DESIGN의 판매 가정과 대조한다.
- **MONSTER_RARITY_DESIGN 갱신**: §3 표의 `DropRolls`, R6·R7, §7을 이 문서 기준으로 고쳤다(2026-10-11).

## 13. 검증

결과 (2026-10-11):

- **컴파일**: Runtime·Editor 통과. 두 어셈블리에 새 타입이 들어간 것을 확인했다.
- **시트**: MonsterArchetype K~M열과 G열(마법 1, 희귀 6), Buff L~T열과 101~103행, ItemDrop C2(44), 새 탭 MonsterAffix(gid 1703620452) 14행.
- **테이블 재다운로드**: MonsterAffixTable 14행 생성. BuffTable은 1~5행이 그대로이고 101~103행만 추가됐다. DropTable은 1번 행 `NothingRate` 43 → 44만 바뀌었다. MonsterArchetypeTable은 시트 값과 일치한다.
- **개수 분포**: 실제 `MonsterAffixRoller.RollCount`를 20만 회 돌렸다(편집 모드, 시드 고정).

  | 등급 | 0개 | 1개 | 2개 | 3개 | 4개 | 5개+ | 10개 | 평균 | 평균 판정 |
  |---|---|---|---|---|---|---|---|---|---|
  | Normal | 90.0 | 9.0 | 0.91 | 0.08 | 0.01 | 0 | 0 | 0.111 | 1.111 |
  | Magic | – | 64.8 | 22.8 | 8.0 | 2.85 | 1.49 | 0.007 | 1.542 | 2.542 |
  | Rare | – | 49.9 | 25.1 | 12.5 | 6.19 | 6.27 | 0.18 | 1.997 | 9.995 |

  필드 마리당 평균 판정은 1.68회다(§8.3과 같음).
- **미실행**: 플레이 모드 확인과 `Monster Affix Distribution` 진단(종별 후보 부족 포함). 같은 시간에 다른 작업이 같은 Unity 에디터를 쓰고 있어 플레이 모드가 외부 재컴파일로 중간에 끊겼다.

결과 (2026-10-15, §7.1 접사 3종 추가):

- **시트·테이블**: MonsterAffix 16~18행 추가 후 단일 다운로드. MonsterAffixTable이 14 → 17행이 됐고 기존 14행은 바뀌지 않았다. 112는 피해 종류 조건 없음, 113은 Physics, 114는 Fire 조건으로 읽혔다.
- **플레이 모드**: 접사를 지정해 플레이어 옆에 만들었다.
  - 마법 고블린 주술사(112·114): `FinalProjectileCountAdd` 1, 점화 확률 25(기본 5 + 20). 화염구가 매 시전 두 발씩 나갔다.
  - 늑대(113): 출혈 확률 25(기본 5 + 20). 2.8초 만에 플레이어에게 출혈이 걸렸다.
- **관찰**: 생성 직후 첫 공격은 접사 반영 전 스탯으로 나갔다(주술사 첫 화염구 한 발). 스킬(FixedUpdate)이 스탯 재계산(Update 50)보다 먼저 한 번 돌 수 있어서이며, 모든 스탯형 접사에 공통인 기존 동작이다. 필드 몬스터는 화면 밖 거리 링에서 생겨 실제 플레이에서는 드러나기 어렵다. [추론]
- 접사 이름표 표시와 근접 몬스터에 112가 붙은 경우는 화면으로 확인하지 않았다.

검증 항목:

- **컴파일**: Runtime·Editor.
- **테이블**: 재다운로드 후 bytes diff가 의도한 칸만 바뀌었는지 확인한다(MonsterArchetype, MonsterAffix, BuffTable, DropTable).
- **분포 진단**(`ARPG/Diagnostics/Monster Affix Distribution`, 플레이 모드 전용): 등급별 접사 개수 분포를 대량 판정해 §3 표와 대조한다. 희귀가 후보 부족으로 일찍 멈추는 비율, 마리당 평균 드랍 판정(§8.3 1.68)도 함께 본다.
- **플레이 확인**:
  1. 일반 무리 10개 중 1개꼴로 접사, 마법 무리 전원이 같은 이름표
  2. 희귀 무리에서 희귀와 호위의 접사가 따로 나오는지
  3. 강력한 접사 몬스터의 피해 숫자가 약 1.3배인지
  4. 오라 반경 안 아군 강화, 오라 주인 처치 후 1초 안에 해제
  5. 단단한 접사 몬스터가 HP바 가득 찬 상태로 나오는지
  6. 접사 많은 희귀 처치 시 드랍이 흩어져 떨어지는지
