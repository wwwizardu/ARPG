# 스킬 기획 문서 (Skill Design)

현재 `System_Skill` / `SkillTable` / `DamageCalculator` 위에서 바로 또는 약간의 보강으로 구현 가능한 스킬 카탈로그.

---

## 0. 작성 원칙

- **시스템 우선**: 기획은 현재 코드가 표현 가능한 범위 안에서 정의한다. 새로운 메커니즘이 필요한 스킬은 §6의 시스템 보강 항목에 의존성을 명시한다.
- **테이블 컬럼만으로 정의**: 한 스킬 = `SkillTable` 한 행. 분기는 `Tags`/`SkillType`/`SkillTargetType`/`ProjectileId`로 표현하고, 코드 분기 추가는 최소화한다.
- **밸런스 수치는 가이드**: 본 문서의 데미지/쿨타임/범위는 초안. 최종 수치는 EquipmentBaseStat과 함께 튜닝.

---

## 1. 현재 시스템 능력 요약

스킬 행위는 `SkillTable` 컬럼 조합으로 결정된다. 기획 시 어느 컬럼이 어떤 행동을 만드는지 매핑.

### 1.1 분류 컬럼

| 컬럼 | 의미 | 비고 |
|---|---|---|
| `Tags` (`SkillTag`) | Attack/Spell/Physics/Fire/Ice/Lightning/Poison/Melee/Ranged/AoE/Projectile/Buff/Debuff/Move | 비트 플래그. 속도 배율(Attack=공속, Spell=시전속도) 분기에 사용 |
| `SkillType` | Melee/Range/Buff/Summon/Jump | `Jump`는 `JumpComponent` 자동 생성 분기 |
| `SubType` | None/SelfDestroy | 현재 SelfDestroy만 정의 |
| `DamageType` | Physics/Fire/Ice/Lightning/Poison | `DamageCalculator`에서 속성 저항 적용 |

### 1.2 타이밍/실행 컬럼

| 컬럼 | 의미 |
|---|---|
| `StartTime` / `ProcessTime` / `EndTime` | FSM 단계별 지속(초). Attack 태그면 무기 공속으로 일괄 스케일 |
| `DamageTime` | Process 내부 첫 히트 오프셋 비율(0~1) |
| `HitCount` / `HitInterval` | 다단히트 횟수 / 간격(초) |
| `BaseAttackSpeedMul` | Attack 스킬의 공속 보정(100=1.0x) |
| `Cooltime` | 쿨다운(초). `FinalCooldownReduction`(최대 90%) 적용 |
| `Mana` | 마나 소모 (현재 소모 처리 미구현, §6.2) |

### 1.3 타겟/범위 컬럼

| 컬럼 | 의미 |
|---|---|
| `SkillTargetType` | `SingleEntity` / `Direction` / `Position` |
| `SkillRangeMin` / `SkillRangeMax` | 시전 가능 사거리 (AI/UI용) |
| `SkillTargetRange1` | 히트 판정 거리 (즉발) |
| `SkillTargetRange2` | 히트 판정 각도 (360=원, 그 외=부채꼴) |

### 1.4 데미지 컬럼

| 컬럼 | 의미 |
|---|---|
| `DamageMin` / `DamageMax` | Spell 베이스 데미지. Attack은 무기에서 가져옴 |
| `BaseDamageMul` | 베이스 데미지 배율(100=1.0x). 플랫 합산 후 적용 |
| `BaseCriRate` | Spell 베이스 치명타율. Attack은 무기 사용 |

### 1.5 효과/연출 컬럼

| 컬럼 | 의미 |
|---|---|
| `ProjectileId` | >0이면 즉발 대신 발사체 스폰 (`ProjectileHelper`) |
| `ArcHeight` | >0이면 포물선 (Jump/투사체) |
| `AnimationName` | `AnimCategory` enum 매칭 (Idle/Move/Attack/Dead/Jump) |
| `StartEffectName` / `ActivateName` / `HitEffect` | 이펙트 키 (현재 미연결) |

### 1.6 ExecutionType (런타임)

`SkillComponent.ExecutionType`은 코드에 5종 분기(`Single`/`MultiHit`/`Channeling`/`Toggle`/`Charge`)가 있으며, `SkillTable.ExecutionType` 컬럼에 enum 문자열로 지정(시트도 문자열 표기, 빈 셀은 `MultiHit`로 기본값). `EntityFactory.CreateSkill`이 테이블 값을 그대로 매핑. 채널링은 입력 유지 메커니즘(§6.1)까지 모두 동작. Charge/Toggle은 분기는 잡혀있으나 처리 로직 미완.

---

## 2. 스킬 카탈로그 개요

| 카테고리 | 슬롯 | 즉시 가능 | 보강 후 가능 |
|---|---|---|---|
| 근접 평타·광역 | A1 ~ A5 | ✅ | — |
| 이동/돌진 | M1 ~ M3 | M1만 | M2/M3 (§6.3) |
| 원거리 발사체 | R1 ~ R4 | ✅ | R3 관통/다중 (§6.4) |
| 원소 스펠 | S1 ~ S6 | S1·S2·S6 | S3·S4·S5 (§6.5/§6.6) |
| 버프/디버프 | B1 ~ B4 | — | 전부 (§6.2/§6.7) |
| 채널링/차징 | C1 ~ C3 | — | 전부 (§6.1) |

총 **25종 초안**. 한 캐릭터/직업이 모두 가지는 것은 아니며, 상위 시스템(스킬 트리/장비 그란트 등)이 슬롯에 매핑.

---

## 3. 근접 (Melee Attack)

모두 `SkillType=Melee`, `Tags=Attack|Melee|Physics`(+옵션).

### A1. Strike — 단발 평타 *(이미 존재, SkillId=1)*
- `SkillTargetType`: SingleEntity
- `HitCount`: 1, `HitInterval`: 0
- `ProcessTime`: 0.4 (무기 공속 스케일), `DamageTime`: 0.4
- 특징: 가장 가까운 적 지정. AI 기본 공격으로도 사용.

### A2. Cleave — 부채꼴 광역
- `SkillTargetType`: Direction
- `SkillTargetRange1`: 1.5 (거리), `SkillTargetRange2`: 90 (각도, 양쪽 합 ≈ 180°)
- `HitCount`: 1, `BaseAttackSpeedMul`: 80 (느린 풀스윙)
- `Cooltime`: 0
- 특징: 플레이어/근접 몬스터 공용. 마우스 방향 부채꼴.

### A3. Whirlwind — 자기 중심 360도 다단히트
- `SkillTargetType`: Direction (방향 무시, 각도 360)
- `SkillTargetRange2`: 360
- `HitCount`: 6, `HitInterval`: 0.15
- `ProcessTime`: 1.0, `DamageTime`: 0.1
- `Cooltime`: 6
- 특징: 단순 다단히트로 표현 가능. 진정한 채널링은 C1 참조.

### A4. Heavy Slam — 큰 풀백 후 광역 일격
- `SkillTargetType`: Direction
- `SkillTargetRange1`: 1.8, `SkillTargetRange2`: 120
- `StartTime`: 0.6 (긴 선딜), `ProcessTime`: 0.3, `DamageTime`: 0.1
- `BaseDamageMul`: 200, `Cooltime`: 5
- 특징: StartTime을 길게 두는 것만으로 풀백 모션 구현. 차지형(B-Charge)으로 강화는 C2.

### A5. Lacerate — 다단히트 + 출혈 디버프
- `SkillTargetType`: Direction (좁은 각도)
- `SkillTargetRange2`: 60
- `HitCount`: 3, `HitInterval`: 0.1
- 효과: `BuffEffectType.Blooding` 부여 (§6.7 디버프 적용 의존)
- `Cooltime`: 4

---

## 4. 이동 / 돌진 (Move)

`Tags`에 `Move` 포함. `SkillType=Jump`인 경우 자동 점프 처리.

### M1. QuickHop — 짧은 도약 *(이미 존재, SkillId=5)*
- `SkillType`: Jump, `SkillTargetType`: SingleEntity (제자리)
- `ArcHeight`: 0.5, `ProcessTime`: 0.3
- 특징: 회피용. `JumpComponent.InvincibleHeight` 이상에서 무적.

### M2. Leap Slam — 위치 도약 + 착지 광역 *(요구: §6.3)*
- `SkillType`: Jump, `SkillTargetType`: Position
- `ArcHeight`: 1.2, `SkillRangeMax`: 4.0
- 착지 데미지: `SkillTargetRange1`: 1.5 (원형, 각도 360)
- `Cooltime`: 6
- 의존: `GetEntitiesInSkillRange`에 `Position` 분기 + 착지 시점 히트 트리거(§6.3).

### M3. Dash Strike — 방향 돌진 + 경로 적 타격 *(요구: §6.3)*
- `SkillType`: Jump (혹은 Move 전용 시스템), `SkillTargetType`: Direction
- `ArcHeight`: 0.1 (지면 부근), `SkillRangeMax`: 3.0
- 경로상 적 1회 타격. 새로운 "라인 히트박스" 분기 필요(§6.3).

---

## 5. 원거리 발사체 (Ranged Projectile)

모두 `Tags=Attack|Ranged|Projectile`(또는 `Spell` 대체), `ProjectileId>0`.

### R1. Power Shot — 단발 직선 화살
- `ProjectileId`: 101
- `SkillTargetType`: Direction
- `SkillRangeMax`: 8.0
- `ProcessTime`: 0.4, `DamageTime`: 0.3
- `Cooltime`: 0

### R2. Multi Shot — 부채꼴 3발 ✅ *(2026-05-04 완료, rev 2)*
- Skill 시트 Id=101, **ProjectileId=1(임시, Test 발사체 재사용)**, **BaseProjectileCount=3**, SkillEffectIds=null
- 일반 발사체 경로(`System_Skill.ProcessSkillHit`의 `if(ProjectileId>0)` 분기)가 `BaseProjectileCount + Stat.ProjectileCountAdd`만큼 부채꼴로 발사하도록 확장됨.
- **SkillEffect 사용 안 함**: "스킬 자체의 발사체 개수"는 SkillTable 컬럼이 책임. SkillEffect.SpawnProjectile은 "스킬과 다른 종류의 발사체 스폰"(예: 폭발의 파편) 용도로만 사용.
- 분산 각도는 현재 `System_Skill`의 `SPREAD_ANGLE_PER_SHOT=15°` 상수로 고정. 추후 Stat 합산이나 SkillTable 컬럼으로 교체 예정.
- **장비 mod/버프 효과 자동 적용**: `Stat.ProjectileCountAdd`가 캐스터에 부여되면 R1·R2·S1 등 모든 발사체 스킬이 자동으로 +N발 발사. 발사체 개수 조절은 SkillTable에 분기 없이 동작.
- 정식 화살 프리팹/Projectile 행이 마련되면 ProjectileId를 1→101로 갱신.
- `Cooltime`: 4

### R3. Piercing Bolt — 관통 볼트
- `ProjectileId`: 102 (`ProjectileTable.IsPiercing=true`)
- 직선 관통. 이미 `ProjectileTable.IsPiercing` 컬럼 존재.

### R4. Arc Shot — 포물선 원거리 *(요구: §6.4)*
- `ProjectileId`: 103, `ArcHeight`: 1.5
- 적의 머리 위로 떨어지는 활. 발사체에도 ArcHeight 사용 분기 필요(점프 외 투사체에 미적용).

---

## 6. 원소 스펠 (Spell)

모두 `Tags=Spell|<Element>`. 데미지 = `DamageMin~DamageMax`. 무기 공속이 아닌 시전 속도 사용.

### S1. Fireball — 단발 화염 발사체
- `ProjectileId`: 201
- `DamageType`: Fire
- 적중 시 `BuffEffectType.Ignite` 1스택 (§6.7)

### S2. Ice Nova — 자기 중심 360도 냉기
- `SkillTargetType`: Direction (각도 360)
- `SkillTargetRange1`: 2.0, `SkillTargetRange2`: 360
- `HitCount`: 1
- 효과: `Chill` 부여(이속 -30%, 3초)
- `Cooltime`: 5

### S3. Chain Lightning — 연쇄 번개 *(요구: §6.5)*
- `SkillTargetType`: SingleEntity
- 첫 타겟 적중 후 N회 연쇄 (반경 3, N=3)
- 의존: 신규 컴포넌트/서비스 필요(연쇄 타겟 탐색). 또는 `ProjectileTable`에 ChainCount 추가.

### S4. Poison Cloud — 위치 지정 장판 *(요구: §6.6)*
- `SkillTargetType`: Position
- 장판 지속 시간 동안 진입 적에게 Poison DoT
- 의존: 지속형 `AreaEffectComponent` + 신규 시스템(`System_AreaEffect`).

### S5. Lightning Strike — 위치 즉발 광역 *(요구: §6.3)*
- `SkillTargetType`: Position
- `SkillTargetRange1`: 1.2, `SkillTargetRange2`: 360
- `DamageType`: Lightning
- 의존: M2와 동일하게 Position 즉발 분기.

### S6. Frost Bolt — 단발 냉기 발사체
- `ProjectileId`: 202, `DamageType`: Ice
- 적중 시 `Chill` 부여 (§6.7)

---

## 7. 버프 / 디버프 (Buff/Debuff)

전부 `SkillType=Buff` 또는 `Tags=Buff/Debuff`. 적용은 §6.2(스킬 효과로 BuffComponent 부여) 의존.

### B1. War Cry — 자기 공격력 버프
- `SkillTargetType`: SingleEntity (자기 자신)
- 효과: 공격력 +20%, 10초
- `Cooltime`: 30

### B2. Iron Skin — 토글 방어 *(요구: §6.1, §6.2)*
- ExecutionType: Toggle (현재 고정 MultiHit이라 컬럼화 필요)
- 효과: 방어 +30%, 이속 -10%, 마나 채널 소모
- 의존: Toggle 분기 활성화 + 효과 적용/제거 구현.

### B3. Battle Roar — 주변 아군 버프
- `SkillTargetType`: Direction (각도 360)
- 효과: 자기 + 아군 공격력 +10%, 5초
- 의존: 아군 판별 로직(Faction/Tag) + Buff 적용.

### B4. Curse of Weakness — 단일 디버프
- `SkillTargetType`: SingleEntity
- 효과: 적 공격력 -15%, 8초
- `Tags`: Spell | Debuff

---

## 8. 채널링 / 차징 / 토글 (요구: §6.1)

`ExecutionType` 컬럼화 + 입력 유지 검사가 선행되어야 동작.

### C1. Beam Cast — 빔 채널링
- ExecutionType: Channeling
- `SkillTargetType`: Direction
- `ChannelingInterval`: 0.2, 매 틱 라인 히트
- 의존: `ProcessChannelingSkill`은 코드상 존재. 입력 유지(`SkillCommandComponent` 잔존) 처리는 이미 됨. **라인 히트박스 분기**(§6.3)와 ExecutionType 컬럼화만 추가.

### C2. Charged Bolt — 차징 발사체
- ExecutionType: Charge
- `MaxChargeTime`: 1.5, `MinChargeRatio`: 0.3
- 입력 떼는 순간 차지 비율로 데미지 스케일된 발사체
- 의존: `ProcessChargeSkill`의 입력 끊김 분기(현재 TODO).

### C3. Drain Life — 단일 흡혈 채널링
- ExecutionType: Channeling
- `SkillTargetType`: SingleEntity
- 매 틱 데미지 + LifeSteal 100%
- 의존: C1과 동일.

---

## 9. 시스템 보강 작업 (의존성 있는 스킬에 필요)

위 §3 ~ §8에서 참조한 작업을 한곳에 모음. 우선순위 순.

### §6.1 ExecutionType 컬럼화 + 입력 유지 메커니즘 ✅ *(완료, 2026-05-04 확인)*
**현재 상태**: 채널링은 동작 가능. Charge/Toggle은 ExecutionType 분기까지는 마련됐으나 실제 처리 로직은 미완.

채택 내역:
- `SkillTable`에 컬럼 추가: `ExecutionType`(0=Single,1=MultiHit,2=Channeling,3=Toggle,4=Charge), `ChannelingInterval`, `MaxChargeTime`, `MinChargeRatio`.
- `EntityFactory.CreateSkill`이 테이블 `ExecutionType`을 그대로 매핑 (이전 MultiHit 하드코딩 해소). [EntityFactory.cs:612-615]
- 입력 유지는 **`InputComponent.SkillSlotHeldMask`(int 비트마스크)** 로 채택 — 별도 `SkillInputHeldTag` 컴포넌트는 만들지 않음. 슬롯 매핑: bit 0=Attack(좌클릭), bit 1=Jump(Space), bit 2~9=Digit1~8.
- `System_Input`이 매 프레임 `IsPressed()`로 마스크 갱신. [System_Input.cs:117-136]
- `ProcessChannelingSkill`이 `SlotIndex`로 비트 조회. 입력 떼면 `End` 상태로 전이(후딜레이/쿨타임 정상 처리). AI는 `InputComponent` 없으므로 항상 held 취급, `ProcessDuration`이 종료를 결정. [System_Skill.cs:680-709]
- `ProcessProcessState`가 플레이어 채널링일 때만 `ProcessDuration` 자동 종료를 스킵. [System_Skill.cs:340-342]

남은 작업: Charge(`ProcessChargeSkill` 입력 끊김 분기), Toggle(`ProcessToggleSkill` 효과 적용/제거).

영향: C1·C3 활성화. C2(Charge)·B2(Toggle)는 분기는 잡혀있으나 처리 로직 미완.

### §6.2 마나 소모 + 버프 적용 훅 *(우선순위: 높음, 버프/스펠 전반)*
- `ProcessSkillCommands`에서 시전 직전 `Mana` 소모 검사 (StatComponent.CurrentMana).
- `ApplySkillEffectToEntity`의 TODO 자리에 `BuffComponent` 부여 로직 (`BuffTable` 매핑 컬럼 신설: 예 `OnHitBuffId`, `SelfBuffId`).
- 영향: B1·B2·B3·B4·A5·S1·S2·S6.

### §6.3 Position/라인 즉발 히트 분기 *(우선순위: 높음, 도약·번개 강타·빔)*
- `GetEntitiesInSkillRange`에 `case SkillTargetType.Position` 추가 (TargetPosition 중심 원형).
- 라인 히트(돌진/빔용) 분기: 두 점 + 두께로 검사. 새 enum 값 또는 `SkillTargetRange2`를 두께로 재해석.
- 점프 스킬은 착지 시점에 한 번 더 ProcessSkillHit 트리거가 필요(현재 ProcessTime 종료 = 착지 = ProcessHit과 어긋나는지 검토).
- 영향: M2·M3·S5·C1.

### §6.4 발사체 다중/포물선 확장 ✅ *(다중 발사 완료, 포물선만 남음)*
- ~~다중 발사~~: `SkillTable.BaseProjectileCount` 컬럼 + `Stat.ProjectileCountAdd` 합산으로 처리 (R2 완료). System_Skill의 ProjectileId 분기가 N발 부채꼴 발사로 확장. **임의의 발사체 스킬에 자동 적용** — 장비 mod/버프가 +N 부여하면 R1·R2·S1 등 모든 스킬이 자동으로 다중 발사. ProjectileTable에는 컬럼 추가 없음, SkillEffect 행도 필요 없음.
- ※ §6.8 `SkillEffectType.SpawnProjectile`은 별도 용도로 유지: "스킬과 다른 종류의 발사체를 스폰"(예: 폭발의 파편, 시체 폭발)에만 사용. 일반 Multi Shot에 사용 금지.
- 포물선(R4): 발사체에 `ArcHeight` 적용 미해결. 별도 `ArcProjectile` 분기 또는 ProjectileSystem 보강 필요.
- 영향: R4 (포물선만 미해결).

### §6.5 연쇄 / 다중 타겟 메커니즘 *(우선순위: 낮음)*
- 신규 컴포넌트 `ChainComponent` 또는 `ProjectileTable.ChainCount`.
- 첫 타겟에서 가장 가까운 미타격 적 N개 탐색.
- 영향: S3.

### §6.6 지속형 장판 (AreaEffect) *(우선순위: 낮음)*
- `AreaEffectComponent { Position, Radius, DamageType, TickInterval, Duration, OwnerId, BuffId }`.
- 신규 `System_AreaEffect`(FixedUpdate, Priority 250 정도).
- 영향: S4.

### §6.7 BuffEffect 트리거 매핑 *(우선순위: 중, §6.8 도입 시 흡수)*
- 스킬 적중 시 `BuffEffectTable` 적용 규칙. `SkillTable`에 `OnHitBuffEffectId` 컬럼.
- DamageCalculator 또는 ApplySkillEffectToEntity 단계에서 부여.
- 영향: A5·S1·S2·S6.
- **주**: §6.8 SkillEffect 시스템이 도입되면 `EffectType.ApplyBuffOnHit`로 자연스럽게 흡수됨. 단독 컬럼 추가는 §6.8을 건너뛸 때만 의미 있음.

### §6.8 SkillEffect 합성형 효과 시스템 ✅ *(골격 완료, 2026-05-04 확인. 신규 EffectType 확장만 남음)*
**목적**: "명중 시 HP 흡수", "투사체 분리", "치명타 시 추가 발사체" 등의 옵션을 코드 분기 없이 데이터로 합성한다. PoE 보조 젬과 본질적으로 같은 모델.

**구현 위치**:
- `GlobalEnum.SkillTrigger` (8종 전체 정의), `GlobalEnum.SkillEffectType` (None/LifeStealOnHit/ApplyBuffOnHit/DelegateToTotem/SpawnProjectile 5종 구현)
- `SkillEffectExecutor.Trigger(trigger, ctx, effectIds)` 단일 진입점, EffectType별 switch
- `SkillEffectContext` (OwnerId/TargetId/SkillId/TargetPos/DamageResult/IsCrit/CancelOriginalCast)
- `SkillTable.SkillEffectIds: List<int>` 컬럼 + 구글 시트 SkillEffect 시트
- 등록된 효과: 1001 LifeSteal_15%, 1002 OnHit_Bleed, 1010 Delegate_Totem_8s, 2003 MultiShot_3 (R2)

**옵션 합산 컨벤션**: 옵션은 두 종류로 나뉨.
- **(1) 스킬 고유 속성**: `SkillTable` 컬럼으로 표현 (예: `ProjectileId`, `BaseProjectileCount`). 발사체 개수처럼 "스킬마다 다를 수 있지만 하나의 스킬 내에선 base 값"인 항목.
- **(2) 외부 modifier**: `Stat.<Aspect>Add` enum으로 누적, `StatModifier`로 장비/버프 부여. 사용처 측에서 `base + Stat.Final*Add`로 합산. 예: `Stat.ProjectileCountAdd`.
- 새 modifier 추가 시: Stat enum 1줄 + StatComponent 2줄(Base/Final) + System_StatCalculation case 1개 + 합산 코드 1곳.

**SkillEffect.SpawnProjectile은 별도 용도**: "스킬과 다른 종류의 발사체 스폰"(폭발 파편, 시체 폭발 등). 일반 Multi Shot에는 사용 안 함.

**남은 작업**: SpawnProjectileOnHit(분리) / SpawnAreaEffectOnKill / ManaRestoreOnKill / KnockbackOnHit / DelegateToMine·Trap 등 `SkillEffectType` enum 추가 + Executor case 추가. 합산형 옵션은 `Stat.ProjectilePierceAdd` / `ProjectileChainAdd` / `ProjectileSpreadAdd` / `ProjectileForkCountAdd` 추가.

**문제 의식**: 현재는 새 옵션 1개 = `SkillTable` 컬럼 1개 + `System_Skill` 분기 1개. 옵션 N × 스킬 M 조합 폭발. `ApplySkillEffectToEntity`의 TODO 주석들이 이 한계를 드러냄.

**핵심 데이터 모델**:
```
SkillTable
└─ SkillEffectIds: List<int>            // 새 컬럼

SkillEffectTable                         // 새 테이블
├─ Id
├─ EffectType: GE.SkillEffectType        // LifeStealOnHit, ApplyBuffOnHit, ProjectileSplit, ...
├─ Trigger: GE.SkillTrigger              // OnSkillStart/OnHit/OnCrit/OnKill/OnProjectileSpawn/OnProjectileHit/OnSkillEnd/OnSkillCommand
├─ Param1, Param2, Param3: float
└─ Probability: int
```

**Trigger (Hook) 포인트**:
| Trigger | 시점 | 컨텍스트 |
|---|---|---|
| `OnSkillCommand` | `ProcessSkillCommands` 진입 직후 (캔슬·위임 가능) | Owner, SkillId, TargetPos |
| `OnSkillStart` | Process 진입 직후 | Owner |
| `OnHit` | 적중한 적마다 1회 | Owner, Target, DamageResult |
| `OnCrit` | 치명타 적중 | 위 + IsCrit |
| `OnKill` | 적중으로 적이 사망 | Owner, KilledTarget |
| `OnProjectileSpawn` | 발사체 생성 직후 | ProjectileEntity |
| `OnProjectileHit` | 발사체 적중 시 | ProjectileEntity, Target |
| `OnSkillEnd` | End 단계 종료 | Owner |

**디스패처**: `SkillEffectExecutor` 정적 유틸 1곳에 모아 EffectType별 switch. `System_Skill`은 트리거 호출 1줄씩만 추가.

**도입 효과 (예시)**:
| 옵션 | EffectType | Trigger | Param 의미 |
|---|---|---|---|
| 명중 시 HP 흡수 (스킬 한정) | LifeStealOnHit | OnHit | Percent |
| 점화 1스택 부여 | ApplyBuffOnHit | OnHit | BuffId, Stack |
| 처치 시 마나 회복 | ManaRestoreOnKill | OnKill | Amount |
| 치명타 시 추가 발사체 | SpawnProjectileOnCrit | OnCrit | ProjectileId, Count |
| 투사체 분리 | SpawnProjectileOnHit | OnProjectileHit | Count, Spread, DamageRatio |
| 시체 폭발 | SpawnAreaEffectOnKill | OnKill | Radius, Damage |
| 명중 시 넉백 | KnockbackOnHit | OnHit | Force |
| **토템 시전 위임** (§6.9) | DelegateToTotem | OnSkillCommand | TotemTableId, Duration |
| **지뢰 시전 위임** (§6.9) | DelegateToMine | OnSkillCommand | MineTableId, MaxMines |

**영향**: §6.2(버프 적용)·§6.7·§6.9 모두 EffectType으로 흡수됨. 새 옵션 추가 시 EffectType enum 1줄 + 디스패처 case 1개 + 테이블 행 1개. 기존 SkillTable 행 변경 0.

### §6.9 토템·지뢰·함정 — 시전 주체 위임 *(우선순위: 중, §6.8 의존)*
**목적**: PoE 보조 젬(Spell Totem / Remote Mine / Trap)처럼 **플레이어가 시전한 스킬을 다른 엔티티가 대신 시전하도록** 한다.

**아키텍처 적합성**: `SkillComponent.OwnerEntityId`가 임의 엔티티가 될 수 있고, `EntityIdHelper.CreateSkillEntity(ownerId, slot)`가 결정적 ID라 토템 엔티티가 자기 슬롯에 스킬을 가질 수 있음. 자율 발사 인프라(`AIComponent` + `SkillHelper.PickFireSkill`)도 재활용 가능.

**참고 — HP/사망 처리는 모두 기존 인프라가 자동**: 토템은 caster의 StatComponent를 스냅샷 복사하므로 HP를 가지며, FactionComponent가 caster 진영을 그대로 따르므로 적의 공격 대상이 됨. HP가 0이 되면 `System_HpCheck`가 `DestroyTag`를 부착해 정리. 별도 토템 사망 코드 불필요.

**4가지 결정**:

1. **스탯 모델 — 스냅샷 (권장)**
   - 토템/지뢰 스폰 시 caster의 `StatComponent`를 그대로 복사 (live link 아님).
   - 흡혈/킬보상 등은 caster에게 귀속하기 위해 `CasterLinkComponent { CasterEntityId }` 신설.
   - 기준: 스냅샷 단순성 + caster 사망 시 토템이 살아있어도 안전.

2. **자율 시전 — 기존 AI 재활용 + Faction 추가**
   - `FactionComponent { byte FactionId }` 신설 (0=Neutral, 1=Player, 2=Hostile).
   - `FindClosestEntity`/`CheckCircleRangeEntities`에 "다른 진영만" 필터 1줄 추가.
   - 토템 전용 `AiTable` 1행 + `SkillHelper.PickFireSkill` 그대로 사용.
   - **새 시스템 0개**.

3. **생존 모델 — 공통 LifetimeComponent + 보조별 추가 컴포넌트**
   ```
   struct LifetimeComponent { float Remaining; }              // 공통
   struct MineComponent     { byte State; float TriggerRange; }
   struct TrapComponent     { float TriggerRange; }
   ```
   - 토템: `Lifetime` + `AI*`
   - 지뢰: `Lifetime` + `Mine` (대기 → 명령 기폭 → 자폭 FSM)
   - 함정: `Trap` (적 근접 트리거)
   - 신규 시스템: `System_Lifetime` (FixedUpdate, Priority ~70). `System_Mine`은 지뢰 도입 시.

4. **위임 메커니즘 — §6.8 SkillEffect 의존**
   - 새 EffectType: `DelegateToTotem` / `DelegateToMine` / `DelegateToTrap`.
   - Trigger: `OnSkillCommand` (스킬 시전 직전). 디스패처가 위임 효과를 발견하면 **caster의 원래 시전을 캔슬**하고 토템/지뢰 엔티티를 스폰. 스폰된 엔티티가 `CreateSkill(totemId, slot=0, 원래 SkillId)`로 동일 스킬을 자신의 슬롯에 보유.
   - 효과: 한 SkillTable 행("Fireball")이 보조 효과 ID 조합에 따라 일반 시전 / 토템 / 지뢰 / 함정 모두로 동작.

**영향**: 토템·지뢰·함정 스킬군 전체. 새 카테고리 "T" 추가 가능 (예: T1 Spell Totem, T2 Remote Mine, T3 Arrow Trap).

---

## 10. 스킬 ID 예약 (제안)

테이블 충돌 방지를 위해 ID 대역 예약.

| 대역 | 카테고리 | 예약 슬롯 |
|---|---|---|
| 1 ~ 49 | 근접 (A) | A1=1 (기존), A2=10, A3=11, A4=12, A5=13 |
| 50 ~ 99 | 이동 (M) | M1=5 (기존, QuickHop), M2=50, M3=51 |
| 100 ~ 199 | 원거리 (R) | R1=100, R2=101, R3=102, R4=103 |
| 200 ~ 299 | 스펠 (S) | S1=200 ~ S6=205 |
| 300 ~ 399 | 버프 (B) | B1=300 ~ B4=303 |
| 400 ~ 499 | 채널/차징 (C) | C1=400 ~ C3=402 |

ProjectileId 대역도 동일 규칙 권장: 화살 100번대, 마법 200번대.

---

## 11. 구현 우선순위 로드맵

기획 문서 채택 후 권장 구현 순서. **§6.8(SkillEffect)을 가장 먼저 도입하면 이후 옵션·보조가 데이터 추가만으로 동작**하므로 우선순위가 가장 높음.

1. **시스템 변경 0**: A2 Cleave, A3 Whirlwind(채널링 변형 포함, ExecutionType=2), A4 Heavy Slam, R1 Power Shot, R3 Piercing Bolt, S2 Ice Nova (테이블 행만 추가). ✅ §6.1 완료로 채널링도 시스템 변경 0 그룹에 포함.
2. **§6.8 SkillEffect 골격 도입** (트리거 enum + Executor 빈 디스패치 + System_Skill 8개 호출 지점). 동작 변화 0, 토대만 마련.
3. **§6.2 + §6.8 첫 EffectType (LifeStealOnHit, ApplyBuffOnHit)**: A5 Lacerate, B1 War Cry, B4 Curse of Weakness, S1 Fireball, S6 Frost Bolt. (§6.1은 완료)
4. **§6.3**: M2 Leap Slam, M3 Dash Strike, S5 Lightning Strike, C1 Beam Cast.
5. **§6.4 + EffectType.SpawnProjectile 계열**: R2 Multi Shot, R4 Arc Shot, "투사체 분리" 옵션.
6. **§6.9 토템·지뢰·함정 위임** (§6.8 + Faction + Lifetime 의존): T1 Spell Totem, T2 Remote Mine, T3 Trap.
7. **§6.6**: S4 Poison Cloud.
8. **마지막**: S3 Chain Lightning, B2 Iron Skin, B3 Battle Roar, C2 Charged Bolt, C3 Drain Life.

---

## 12. 변경 이력

- 2026-05-02: 초안 작성. 25종 스킬 카탈로그 + 7개 시스템 보강 항목 정의.
- 2026-05-02 (rev 2): §6.8 SkillEffect 합성형 효과 시스템 추가, §6.9 토템·지뢰·함정 시전 주체 위임 추가. §6.7을 §6.8에 흡수 가능 항목으로 표시. §11 로드맵 재정렬 (SkillEffect 우선).
- 2026-05-04: §6.1 완료 표시. 입력 유지 메커니즘은 `SkillInputHeldTag`(제안) 대신 `InputComponent.SkillSlotHeldMask` 비트마스크로 채택. Charge/Toggle 처리 로직은 미완으로 남음. §11 로드맵에서 §6.1 의존 항목 정리. A3 Whirlwind 채널링 변형 추가 (Skill 시트 Id=11, ExecutionType=2).
- 2026-05-04 (rev 2): §6.8 SkillEffect 골격 완료 표시 (Executor/Context/Trigger·EffectType enum/SkillEffect 시트). LifeStealOnHit/ApplyBuffOnHit/DelegateToTotem 3종 구현됨. A5 Lacerate 추가 (Skill 시트 Id=13, SkillEffectIds=[1002 OnHit_Bleed]).
- 2026-05-04 (rev 3): R2 Multi Shot 구현 — `SkillEffectType.SpawnProjectile` + `Stat.ProjectileCountAdd` 추가. SkillEffect 2003 / Skill Id=101. 발사체 옵션 합산 컨벤션(Param=base, Stat=modifier) 도입. §6.4를 §6.8에 흡수. 분산 각도는 코드 상수(15°/발) 고정 — 추후 Stat 추가 시 교체 예정.
- 2026-05-04 (rev 4): R2 설계 재검토 — SpawnProjectile EffectType 기반 접근은 "스킬당 별도 SkillEffect 행 필요"라는 조합 폭발 문제로 폐기. 대신 **`SkillTable.BaseProjectileCount` 컬럼**을 추가하고 System_Skill의 ProjectileId 분기를 N발 부채꼴 발사로 확장. R2는 `ProjectileId=1, BaseProjectileCount=3`만으로 정의. `Stat.ProjectileCountAdd`는 모든 발사체 스킬에 자동 적용. SpawnProjectile EffectType은 "스킬과 다른 종류의 발사체 스폰" 용도로 살려둠.
