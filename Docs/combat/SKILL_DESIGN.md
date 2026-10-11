# 스킬 기획 문서 (Skill Design)

현재 `System_Skill` / `SkillTable` / `DamageCalculator` 위에서 바로 또는 약간의 보강으로 구현 가능한 스킬 카탈로그.

> **현행 판정 기준 (2026-10-03, 이전 2026-09-20)**: "구현됨"은 해당 스킬의 `SkillTable` 행과 필요한 공용 런타임 경로가 모두 존재한다는 뜻이다. "런타임 준비 완료"는 공용 메커니즘은 존재하지만 해당 스킬의 테이블·효과·프리팹 콘텐츠가 아직 등록되지 않았다는 뜻이다. 이름이 같은 테이블 행이 없다는 사실만으로 시스템 미구현으로 판정하지 않는다.

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
| `Mana` | 플레이어 시전 명령 승인 후 실제 실행 직전에 소모. 마나 부족 시 명령을 거절하며, AI는 소비하지 않음 |

### 1.3 타겟/범위 컬럼

| 컬럼 | 의미 |
|---|---|
| `SkillTargetType` | `Direction` / `Position` |
| `SkillRangeMin` / `SkillRangeMax` | 시전 가능 사거리 (AI/UI용) |
| `SkillTargetRange1` | 히트 판정 거리 (즉발) |
| `SkillTargetRange2` | 히트 판정 **반각**(forward 기준 한쪽 각도, 전체 각 = 2배). 180 이상이면 원(각도 검사 생략), 그 외=부채꼴. 기존 행의 `360`도 원으로 동작한다 (2026-10-03 정정: `HitboxMath.PointInSector`의 `halfAngleDeg`) |

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
| `StartEffectName` / `ActivateName` / `HitEffect` | `ActivateName`은 `Effects/` 키일 때 첫 커밋 위치에 1회 생성하고, `HitEffect`는 회피가 아닌 실제 적중 위치에 생성. `StartEffectName`은 파싱만 하고 런타임 소비 경로가 아직 없음 |

### 1.6 ExecutionType (런타임)

`SkillComponent.ExecutionType`은 코드에 5종 분기(`Single`/`MultiHit`/`Channeling`/`Toggle`/`Charge`)가 있으며, `SkillTable.ExecutionType` 컬럼에 enum 문자열로 지정(시트도 문자열 표기, 빈 셀은 `MultiHit`로 기본값). `EntityFactory.CreateSkill`이 테이블 값을 그대로 매핑. 채널링은 입력 유지 메커니즘(§6.1)까지 모두 동작. Charge/Toggle은 분기는 잡혀있으나 처리 로직 미완.

---

## 2. 스킬 카탈로그 개요

| 카테고리 | 구현된 콘텐츠 | 런타임 준비 완료·콘텐츠 미등록 | 코드 보강 필요 |
|---|---|---|---|
| 근접 평타·광역 | A1·A3·A5 | A2·A4 | — |
| 이동/돌진 | M1 | M2 | M3 라인 히트 |
| 원거리 발사체 | R2 | R1·R3 | R4 투사체 포물선 |
| 원소 스펠 | S3·S4 | S1·S2·S5·S6 | — |
| 버프/디버프 | — | — | B2 토글 자기 버프·B3 아군 범위 버프 |
| 채널링/차징 | — | — | C1 라인 히트·C2 차지 해제 발동 |

총 **22종 초안**(2026-10-03 S3 재포함) 중 현재 테이블과 런타임이 함께 존재하는 스킬은 A1·A3·A5·M1·R2·S3·S4의 7종이다. A2·A4·M2·R1·R3·S1·S2·S5·S6의 9종은 공용 메커니즘이 이미 있어 콘텐츠 데이터 작성이 주 작업이다. 실제 코드 보강이 필요한 스킬은 M3·R4·B2·B3·C1·C2의 6종이다. 한 캐릭터/직업이 모두 가지는 것은 아니며, 상위 시스템(스킬 트리/장비 그란트 등)이 슬롯에 매핑한다.

`SkillTable`에는 이 카탈로그 외에도 몬스터 전용 `StrikeMonster`, `GoblinFirebolt`과 추가 스킬 `FireRing`이 있다. 이들은 카탈로그 구현 수에 포함하지 않는다.

> 2026-10-03 확인: 몬스터 전용 `SwampToadPoisonSpit`(SkillId 205)은 2026-09-24 커밋 "연쇄 스킬 추가"(c87d5de)에서 `SkillTable.bytes`에서 빠졌다. 그런데 `AiTable` Id 4(SwampToad, `MonsterTable` 1010의 AI)는 아직 `SkillId1=205`를 참조한다. 의도한 삭제인지 확인되지 않았으며, 시트 행을 복구하든 AI 참조를 바꾸든 데이터 정리가 필요하다. 이 상태에서는 `AttackTelegraphBuildValidator`가 "AI 4의 SkillId 205가 SkillTable에 없습니다"로 빌드를 중단했다(메뉴 실행으로 확인). → **2026-10-03 복구**: 삭제 직전 커밋의 205번 행을 시트 `Skill` 탭에 다시 넣고 다운로드했다. 값은 그대로이고 `MoveSpeedWhileUsingMul`만 같은 커밋의 일괄 변경(30 → 75)에 맞췄다. 빌드 검증기 통과.

과거 삭제한 S3 Chain Lightning은 `SingleEntity`를 복원하지 않고 Position 커밋과 공용 연쇄·분열 런타임으로 다시 도입했다(2026-09-24, c87d5de). 구현 계약은 [SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md](SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md)를 따른다. 런타임과 데이터 행(SkillId 202)이 추가되어 2026-10-03 카탈로그 §6 S3로 다시 포함했다.

---

## 3. 근접 (Melee Attack)

기획상 모두 근접 물리 공격이며 `Tags=Attack|Melee|Physics` 조합을 권장한다. 현행 A1·A3·A5 데이터는 호환성 때문에 `SkillType=Range`를 사용하고, A1은 `Melee` 태그도 아직 없다. 실제 근접 판정은 `SkillType`이 아니라 `ProjectileId=0`과 방향 부채꼴 범위로 동작한다.

### A1. Strike — 단발 평타 *(구현됨, SkillId=1)*
- `SkillTargetType`: Direction, `SkillTargetRange1`: 2.0 (거리), `SkillTargetRange2`: 45 (각도)
- `HitCount`: 1, `HitInterval`: 0
- 현행 데이터: `ProcessTime=0.85`, `DamageTime=0.9`, `BaseAttackSpeedMul=130`, `Cooltime=0`
- 특징: 커서 방향 좁은 부채꼴 판정. AI 기본 공격으로도 사용.

### A2. Cleave — 부채꼴 광역 *(런타임 준비 완료, 콘텐츠 미등록)*
- `SkillTargetType`: Direction
- `SkillTargetRange1`: 1.5 (거리), `SkillTargetRange2`: 90 (각도, 양쪽 합 ≈ 180°)
- `HitCount`: 1, `BaseAttackSpeedMul`: 80 (느린 풀스윙)
- `Cooltime`: 0
- 특징: 플레이어/근접 몬스터 공용. 마우스 방향 부채꼴.
- 현행: `Direction` 원점 + `SkillTargetRange1/2` 부채꼴 판정이 이미 동작하므로 `SkillTable` 행과 연출만 추가하면 된다.

### A3. Whirlwind — 자기 중심 360도 다단히트 *(구현됨, SkillId=11)*
- `SkillTargetType`: Direction (방향 무시, 각도 360)
- 현행 데이터: `SkillTargetRange1=1.5`, `SkillTargetRange2=360`, `ExecutionType=Channeling`
- `ProcessTime=3.0`, `DamageTime=0.1`, `ChannelingInterval=0.15`, `Cooltime=0`
- 특징: 입력을 유지하는 동안 자기 중심 360도 피해를 반복한다. `TrackContinuously` 정책으로 틱마다 방향을 재커밋하지만 360도 판정이라 피해 범위에는 방향이 영향을 주지 않는다.

### A4. Heavy Slam — 큰 풀백 후 광역 일격 *(런타임 준비 완료, 콘텐츠 미등록)*
- `SkillTargetType`: Direction
- `SkillTargetRange1`: 1.8, `SkillTargetRange2`: 120
- `StartTime`: 0.6 (긴 선딜), `ProcessTime`: 0.3, `DamageTime`: 0.1
- `BaseDamageMul`: 200, `Cooltime`: 5
- 특징: StartTime을 길게 두는 것만으로 풀백 모션 구현. 차지형(B-Charge)으로 강화는 C2.
- 현행: 선딜·단일 커밋·부채꼴 피해·공격속도 스케일 경로가 모두 존재한다. 별도 코드 변경은 필요하지 않는다.

### A5. Lacerate — 다단히트 + 출혈 디버프 *(구현됨, SkillId=13)*
- `SkillTargetType`: Direction (좁은 각도)
- `SkillTargetRange1`: 1.5, `SkillTargetRange2`: 60
- `HitCount`: 3, `HitInterval`: 0.1
- 효과: `SkillEffectId=1002`의 `ApplyBuff(OnHit)`가 `BuffId=1` 출혈을 부여
- `Cooltime`: 4

---

## 4. 이동 / 돌진 (Move)

`Tags`에 `Move` 포함. `SkillType=Jump`인 경우 자동 점프 처리.

### M1. QuickHop — 짧은 도약 *(구현됨, SkillId=5)*
- `SkillType`: Jump, `SkillTargetType`: Direction, `SkillRangeMax`: 4.0
- 현행 데이터: `ArcHeight=0.8`, `ProcessTime=0.6`, `Cooltime=2`
- 특징: 회피용. `JumpComponent.InvincibleHeight` 이상에서 무적.

### M2. Leap Slam — 위치 도약 + 착지 광역 *(런타임 준비 완료, 콘텐츠 미등록)*
- `SkillType`: Jump, `SkillTargetType`: Position
- `ArcHeight`: 1.2, `SkillRangeMax`: 4.0
- 착지 데미지: `SkillTargetRange1`: 1.5 (원형, 각도 360)
- `Cooltime`: 6
- 현행: `Position` 중심 원형 판정, 점프 착지 위치 보정, `SkillLandingCommitComponent`를 통한 착지 시점 피해 커밋이 구현되어 있다. `SkillTable` 행과 착지 연출만 추가하면 된다.

### M3. Dash Strike — 방향 돌진 + 경로 적 타격 *(코드 보강 필요: §6.3)*
- `SkillType`: Jump (혹은 Move 전용 시스템), `SkillTargetType`: Direction
- `ArcHeight`: 0.1 (지면 부근), `SkillRangeMax`: 3.0
- 경로상 적 1회 타격. 새로운 "라인 히트박스" 분기 필요(§6.3).

---

## 5. 원거리 발사체 (Ranged Projectile)

모두 `Tags=Attack|Ranged|Projectile`(또는 `Spell` 대체), `ProjectileId>0`.

### R1. Power Shot — 단발 직선 화살 *(런타임 준비 완료, 콘텐츠 미등록)*
- `ProjectileId`: 101
- `SkillTargetType`: Direction
- `SkillRangeMax`: 8.0
- `ProcessTime`: 0.4, `DamageTime`: 0.3
- `Cooltime`: 0
- 현행: 단일 방향 투사체 생성·이동·피해·수명 경로는 몬스터 투사체와 R2가 사용 중이다. 정식 화살 `ProjectileTable` 행·프리팹과 스킬 행이 필요하다.

### R2. Multi Shot — 부채꼴 3발 ✅ *(2026-05-04 완료, rev 2)*
- Skill 시트 Id=101, **ProjectileId=1(임시, Test 발사체 재사용)**, **BaseProjectileCount=3**, SkillEffectIds=null
- 일반 발사체 경로(`System_Skill.ProcessSkillHit`의 `if(ProjectileId>0)` 분기)가 `BaseProjectileCount + Stat.ProjectileCountAdd`만큼 부채꼴로 발사하도록 확장됨.
- **SkillEffect 사용 안 함**: "스킬 자체의 발사체 개수"는 SkillTable 컬럼이 책임. SkillEffect.SpawnProjectile은 "스킬과 다른 종류의 발사체 스폰"(예: 폭발의 파편) 용도로만 사용.
- 분산 각도는 현재 `System_Skill`의 `SPREAD_ANGLE_PER_SHOT=15°` 상수로 고정. 추후 Stat 합산이나 SkillTable 컬럼으로 교체 예정. (2026-10-03 확인: 값은 여전히 15°이지만 정본은 `AttackTelegraphGeometryResolver.DefaultProjectileSpreadAngleDeg`이며 `System_Skill`의 지역 상수가 이를 참조한다. 공격 예고의 ProjectileFan도 같은 값을 쓴다.)
- **장비 mod/버프 효과 자동 적용**: `Stat.ProjectileCountAdd`가 캐스터에 부여되면 R1·R2·S1 등 모든 발사체 스킬이 자동으로 +N발 발사. 발사체 개수 조절은 SkillTable에 분기 없이 동작.
- 정식 화살 프리팹/Projectile 행이 마련되면 ProjectileId를 1→101로 갱신.
- `Cooltime`: 4

### R3. Piercing Bolt — 관통 볼트 *(런타임 준비 완료, 콘텐츠 미등록)*
- `ProjectileId`: 102 (`ProjectileTable.IsPiercing=true`)
- 직선 관통. 이미 `ProjectileTable.IsPiercing` 컬럼 존재.
- 현행: `ProjectileComponent.IsPiercing`과 관통 시 투사체를 제거하지 않는 충돌 경로가 구현되어 있다. 현재 `ProjectileTable`에는 `IsPiercing=true`인 행이 없으므로 데이터·프리팹 추가가 필요하다.

### R4. Arc Shot — 포물선 원거리 *(코드 보강 필요: §6.4)*
- `ProjectileId`: 103, `ArcHeight`: 1.5
- 적의 머리 위로 떨어지는 활. 발사체에도 ArcHeight 사용 분기 필요(점프 외 투사체에 미적용).

---

## 6. 원소 스펠 (Spell)

모두 `Tags=Spell|<Element>`. 데미지 = `DamageMin~DamageMax`. 무기 공속이 아닌 시전 속도 사용.

### S1. Fireball — 단발 화염 발사체 *(런타임 준비 완료, 콘텐츠 미등록)*
- `ProjectileId`: 201
- `DamageType`: Fire
- 적중 시 `BuffEffectType.Ignite` 1스택 (§6.7)
- 현행: Spell 투사체, `ApplyBuff` OnHit, 점화 `BuffTable` 행은 존재한다. Fireball 전용 `SkillTable`·`ProjectileTable`·`SkillEffectTable` 행과 프리팹을 연결해야 한다.

### S2. Ice Nova — 자기 중심 360도 냉기 *(런타임 준비 완료, 콘텐츠 미등록)*
- `SkillTargetType`: Direction (각도 360)
- `SkillTargetRange1`: 2.0, `SkillTargetRange2`: 360
- `HitCount`: 1
- 효과: `Chill` 부여(이속 -30%, 3초)
- `Cooltime`: 5
- 현행: 자기 중심 360도 피해와 `ApplyBuff` OnHit, 냉동 `BuffTable` 행이 존재한다. 스킬·효과 데이터와 연출을 추가하면 된다.

### S3. Chain Lightning — 위치 지정 연쇄 번개 *(구현됨, SkillId=202 / SkillEffect 1020 / SkillPropagation 1, 2026-09-24)*
- `Tags`: Spell, Lightning / `SkillType`: Range / `ExecutionType`: Single
- `SkillTargetType`: Position, `SkillRangeMax`: 6
- `DamageType`: Lightning, `DamageMin~Max`: 10~15, `Mana`: 20, `Cooltime`: 0.5
- `SkillEffectIds`: [1020 `ChainLightningPropagation`] — `EffectType=SkillPropagation`, `Trigger=OnSkillCommit`, `Param1=1`
- `SkillPropagationTable` 1 `ChainLightning_4Hits`: Instant 전달, `ChildCount=1`, `MaxDepth=3`(최대 4명), `HopInterval=0.2`, `MaxTurnAngleDeg=60`, 연출 `Effects/ChainLightningSegment`
- 현행: `OnSkillCommit`에서 기본 Position 원형 타격을 억제하고 공용 전파 런타임이 커밋 위치 주변 첫 적부터 연쇄한다. `SingleEntity`는 복원하지 않았다. 계약·검증 현황은 [SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md](SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md).

### S4. Poison Cloud — 위치 지정 장판 *(구현됨, SkillId=203 / AreaEffectId=1)*
- `SkillTargetType`: Position
- 장판 지속 시간 동안 진입 적에게 Poison DoT
- 현행: `AreaEffectComponent`, `System_AreaEffect`, `AreaEffectTable`, 수명 정리, Addressable 프리팹까지 연결되어 있다.

### S5. Lightning Strike — 위치 즉발 광역 *(런타임 준비 완료, 콘텐츠 미등록)*
- `SkillTargetType`: Position
- `SkillTargetRange1`: 1.2, `SkillTargetRange2`: 360
- `DamageType`: Lightning
- 현행: `Position` 중심 즉발 원형 판정이 구현되어 있어 스킬 데이터와 연출만 필요하다. 기존 예약 ID 204는 현재 `GoblinFirebolt`가 사용 중이므로 새 ID를 배정해야 한다.

### S6. Frost Bolt — 단발 냉기 발사체 *(런타임 준비 완료, 콘텐츠 미등록)*
- `ProjectileId`: 202, `DamageType`: Ice
- 적중 시 `Chill` 부여 (§6.7)
- 현행: Spell 투사체와 `ApplyBuff` OnHit, 냉동 `BuffTable` 행이 존재한다. 기존 예약 ID 205는 현재 `SwampToadPoisonSpit`이 사용 중이므로 새 ID가 필요하다. (2026-10-03: 한때 `SkillTable`에서 빠졌다가 복구됐다 — §2.)

---

## 7. 버프 / 디버프 (Buff/Debuff)

전부 `SkillType=Buff` 또는 `Tags=Buff/Debuff`. 적중 대상 버프는 §6.2 경로가 존재하지만, B2의 자기 토글 버프와 B3의 아군 범위 선택은 별도 보강이 필요하다.

### B2. Iron Skin — 토글 방어 *(코드 보강 필요: §6.1, §6.2)*
- ExecutionType: Toggle (컬럼과 런타임 분기는 존재)
- 효과: 방어 +30%, 이속 -10%, 마나 채널 소모
- 의존: 자기 버프 적용, 토글 OFF 시 정확한 효과 제거, 지속 마나 소비 구현.

### B3. Battle Roar — 주변 아군 버프 *(코드 보강 필요)*
- `SkillTargetType`: Direction (각도 360)
- 효과: 자기 + 아군 공격력 +10%, 5초
- 의존: 아군 판별 로직(Faction/Tag) + Buff 적용.
- 현행: `FactionComponent`는 존재하지만 일반 스킬 범위 조회는 적대 진영만 수집한다. 자기 자신과 같은 진영을 대상으로 하는 별도 범위 선택 및 버프 적용 경로가 필요하다.

---

## 8. 채널링 / 차징 / 토글 (요구: §6.1)

`ExecutionType` 컬럼화와 입력 유지 검사는 구현됐다. C1은 라인 히트, C2는 입력 해제 시 차지 비율 확정과 발동이 남았다.

### C1. Beam Cast — 빔 채널링 *(부분 구현: 채널링 완료, 라인 히트 필요)*
- ExecutionType: Channeling
- `SkillTargetType`: Direction
- `ChannelingInterval`: 0.2, 매 틱 라인 히트
- 의존: `ProcessChannelingSkill`, 입력 유지, `ExecutionType` 컬럼화, 틱별 조준 재커밋은 구현됐다. **라인 히트박스 분기**(§6.3)와 Beam 전용 데이터·연출이 남았다.

### C2. Charged Bolt — 차징 발사체 *(코드 보강 필요)*
- ExecutionType: Charge
- `MaxChargeTime`: 1.5, `MinChargeRatio`: 0.3
- 입력 떼는 순간 차지 비율로 데미지 스케일된 발사체
- 의존: `ProcessChargeSkill`의 입력 끊김 분기(현재 TODO).

---

## 9. 시스템 보강 작업 (의존성 있는 스킬에 필요)

위 §3 ~ §8에서 참조한 작업을 한곳에 모음. 우선순위 순.

### §6.1 ExecutionType 컬럼화 + 입력 유지 메커니즘 ✅ *(완료, 2026-05-04 확인)*
**현재 상태**: 채널링은 동작 가능. Charge/Toggle은 ExecutionType 분기까지는 마련됐으나 실제 처리 로직은 미완.

채택 내역:
- `SkillTable`에 컬럼 추가: `ExecutionType`(0=Single,1=MultiHit,2=Channeling,3=Toggle,4=Charge), `ChannelingInterval`, `MaxChargeTime`, `MinChargeRatio`.
- `EntityFactory.CreateSkill`이 테이블 `ExecutionType`과 채널링·차지 필드를 그대로 매핑한다(이전 MultiHit 하드코딩 해소). [EntityFactory.cs:841-846]
- 입력 유지는 **`InputComponent.SkillSlotHeldMask`(int 비트마스크)** 로 채택 — 별도 `SkillInputHeldTag` 컴포넌트는 만들지 않음. 슬롯 매핑: bit 0=Attack(좌클릭), bit 1=Jump(Space), bit 2~9=Digit1~8.
- `System_Input`이 매 프레임 `IsPressed()`로 마스크를 갱신한다. [System_Input.cs:137-154]
- `ProcessChannelingSkill`이 `SlotIndex`로 비트를 조회한다. 입력을 떼면 `End` 상태로 전이해 후딜레이와 쿨타임을 정상 처리한다. AI는 `InputComponent`가 없으므로 항상 held로 취급하고 `ProcessDuration`이 종료를 결정한다. [System_Skill.cs:1062-1103]
- `ProcessProcessState`가 플레이어 채널링일 때만 `ProcessDuration` 자동 종료를 건너뛴다. [System_Skill.cs:412-450]

남은 작업: Charge(`ProcessChargeSkill` 입력 끊김 분기), Toggle(`ProcessToggleSkill` 효과 적용/제거).

영향: C1의 채널링 수명주기는 활성화됐다. C2(Charge)·B2(Toggle)는 분기까지만 존재하며 실제 처리 로직은 미완이다.

### §6.2 마나 소모 + 적중 버프 적용 훅 ✅ *(완료)*
- 플레이어는 명령 승인 시 `SkillTable.Mana`와 `StatComponent.CurrentMp`를 검사하고, 실제 스킬 실행이 시작될 때 마나를 차감한다. 마나 부족·Stun·쿨타임 등으로 거절된 명령은 소비하지 않는다. AI는 마나를 소비하지 않는다.
- 적중 버프는 `SkillEffectTable`의 `EffectType.ApplyBuff` + `Trigger.OnHit` 조합을 `SkillEffectExecutor`가 처리한다. A5 Lacerate가 이 경로로 출혈을 적용한다.
- `BuffTable`에는 출혈·점화·냉동·중독이 존재한다. S1·S2·S6은 전용 `SkillEffectTable` 행과 스킬 연결만 남았다.
- 자기 버프와 아군 범위 버프는 이 완료 범위에 포함되지 않으며 B2·B3에서 별도로 구현한다.

### §6.3 Position/라인 즉발 히트 분기 ◐ *(Position·착지 완료, 라인 히트 남음)*
- 완료: `GetEntitiesInSkillRange`가 `SkillTargetType.Position`을 `TargetPosition` 중심 원형으로 판정한다. 이 경로는 S5 Lightning Strike를 데이터만으로 표현할 수 있다.
- 완료: 비채널링 Jump 직접 피해는 `System_Jump`가 보정된 실제 착지 위치를 `SkillLandingCommitComponent`로 전달하고, 다음 스킬 FixedUpdate에서 한 번 커밋한다. M2 Leap Slam의 공용 런타임 의존성은 해결됐다.
- 남음: M3 Dash Strike와 C1 Beam Cast에 필요한 선분/캡슐 형태의 라인 히트. 현재 `Direction`과 `Position` 모두 `HitboxMath.CircleVsSector`만 사용한다.

### §6.4 발사체 다중/관통/포물선 확장 ◐ *(다중·관통 완료, 포물선 남음)*
- ~~다중 발사~~: `SkillTable.BaseProjectileCount` 컬럼 + `Stat.ProjectileCountAdd` 합산으로 처리 (R2 완료). System_Skill의 ProjectileId 분기가 N발 부채꼴 발사로 확장. **임의의 발사체 스킬에 자동 적용** — 장비 mod/버프가 +N 부여하면 R1·R2·S1 등 모든 스킬이 자동으로 다중 발사. ProjectileTable에는 컬럼 추가 없음, SkillEffect 행도 필요 없음.
- ~~관통~~: `ProjectileTable.IsPiercing` → `ProjectileComponent.IsPiercing` 매핑과 관통 시 투사체 유지 경로가 구현됐다. R3은 `IsPiercing=true`인 데이터 행과 프리팹만 필요하다.
- ※ §6.8 `SkillEffectType.SpawnProjectile`은 별도 용도로 유지: "스킬과 다른 종류의 발사체를 스폰"(예: 폭발의 파편, 시체 폭발)에만 사용. 일반 Multi Shot에 사용 금지.
- 포물선(R4): 발사체에 `ArcHeight` 적용 미해결. 별도 `ArcProjectile` 분기 또는 ProjectileSystem 보강 필요.
- 영향: R4 (포물선만 미해결).

### §6.6 지속형 장판 (AreaEffect) ✅ *(완료)*
- `SkillTable.AreaEffectId`, `AreaEffectTable`, `AreaEffectComponent`, `EntityFactory.CreateAreaEffect`, `System_AreaEffect`가 연결되어 있다.
- `System_Lifetime`이 장판 만료를 담당하고, `CasterLinkComponent`와 시전자 진영 스냅샷으로 적대 대상만 틱 처리한다.
- S4 Poison Cloud(`SkillId=203`, `AreaEffectId=1`)와 추가 스킬 FireRing(`SkillId=206`, `AreaEffectId=2`)이 현행 데이터를 사용한다.

### §6.7 BuffEffect 트리거 매핑 ✅ *(§6.8 SkillEffect로 흡수 완료)*
- 별도 `OnHitBuffEffectId` 컬럼은 추가하지 않았다. `SkillTable.SkillEffectIds` → `SkillEffectTable(ApplyBuff, OnHit)` → `SkillEffectExecutor` 경로가 동일 책임을 수행한다.
- A5 Lacerate는 효과 1002로 출혈을 적용한다. S1 Fireball, S2 Ice Nova, S6 Frost Bolt는 점화·냉동 효과 데이터 연결이 남았다.

### §6.8 SkillEffect 합성형 효과 시스템 ◐ *(핵심 골격 완료, 일부 트리거·EffectType 확장 남음)*
**목적**: "명중 시 HP 흡수", "투사체 분리", "치명타 시 추가 발사체" 등의 옵션을 코드 분기 없이 데이터로 합성한다. PoE 보조 젬과 본질적으로 같은 모델.

**구현 위치**:
- `GlobalEnum.SkillTrigger` (9종 전체 정의 — 2026-09-24 `OnSkillCommit` 추가)
- `SkillEffectTable.Kind` 컬럼 (`StatBonus` / `EffectAction`)로 처리 경로 분기:
  - **StatBonus** — `SkillStatBonusHelper.Rebuild`가 트리거별 `SkillStatBonusOn*Component`로 사전 합산. 매 적중마다 디스패치 없이 `SkillAttackProfileComponent`에 캐시되어 `DamageCalculator`가 일괄 소비. 예: `LifeSteal`, `ProjectileCountAdd`.
  - **EffectAction** — `SkillEffectExecutor.Trigger(...)` 단일 진입점이 EffectType별 switch로 분기. 예: `ApplyBuff`, `DelegateToTotem`, `SkillPropagation`.
- `SkillEffectContext` (OwnerId/TargetId/SkillId/TargetPos/DamageResult/IsCrit/CancelOriginalCast, 2026-09-24 `PropagationRootEntityId`/`SuppressDefaultImpact` 추가)
- `SkillTable.SkillEffectIds: List<int>` 컬럼 + 구글 시트 SkillEffect 시트
- 등록된 효과: 1001 LifeSteal_15% (StatBonus), 1002 OnHit_Bleed (EffectAction), 1010 Delegate_Totem_8s (EffectAction), 1020 ChainLightningPropagation (EffectAction, `SkillPropagation`·`OnSkillCommit`, S3), 2003 MultiShot_3 (StatBonus, R2)

**옵션 합산 컨벤션**: 옵션은 두 종류로 나뉨.
- **(1) 스킬 고유 속성**: `SkillTable` 컬럼으로 표현 (예: `ProjectileId`, `BaseProjectileCount`). 발사체 개수처럼 "스킬마다 다를 수 있지만 하나의 스킬 내에선 base 값"인 항목.
- **(2) 외부 modifier**: `Stat.<Aspect>Add` enum으로 누적, `StatModifier`로 장비/버프 부여. 사용처 측에서 `base + Stat.Final*Add`로 합산. 예: `Stat.ProjectileCountAdd`.
- 새 modifier 추가 시: Stat enum 1줄 + StatComponent 2줄(Base/Final) + System_StatCalculation case 1개 + 합산 코드 1곳.

**SkillEffect.SpawnProjectile은 별도 용도**: "스킬과 다른 종류의 발사체 스폰"(폭발 파편, 시체 폭발 등). 일반 Multi Shot에는 사용 안 함.

**남은 작업**: SpawnProjectileOnHit(분리) / SpawnAreaEffectOnKill / ManaRestoreOnKill / Knockback·Pull(§6.10 설계 확정, 2026-10-12) / DelegateToMine·Trap 등 `SkillEffectType` enum 추가 + Executor case 추가. 연쇄·분열은 투사체 전용 Stat을 개별 추가하기 전에 [공용 스킬 전파 계획](SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md)의 `SkillPropagation` 모델로 구현한다. (2026-10-03 확인: `SkillPropagation` EffectAction 구현됨. Instant 연쇄는 S3로 사용 중이고, Projectile 연쇄·분열과 Instant 분열은 코드 경로만 있고 데이터·PlayMode 검증이 없다.)

**문제 의식**: 현재는 새 옵션 1개 = `SkillTable` 컬럼 1개 + `System_Skill` 분기 1개. 옵션 N × 스킬 M 조합 폭발. `ApplySkillEffectToEntity`의 TODO 주석들이 이 한계를 드러냄.

**핵심 데이터 모델**:
```
SkillTable
└─ SkillEffectIds: List<int>            // 새 컬럼

SkillEffectTable                         // 새 테이블
├─ Id
├─ EffectType: GE.Stat                   // ApplyBuff/DelegateToTotem(EffectAction), LifeSteal/ProjectileCountAdd/...(StatBonus)
├─ Kind: GE.SkillEffectKind              // StatBonus(Helper가 컴포넌트로 흡수) / EffectAction(Executor가 트리거 시점 실행)
├─ Trigger: GE.SkillTrigger              // OnSkillCommand/OnSkillStart/OnSkillCommit/OnHit/OnCrit/OnKill/OnProjectileSpawn/OnProjectileHit/OnSkillEnd
├─ Param1, Param2, Param3: float
└─ Probability: int
```

**Trigger (Hook) 포인트**:
| Trigger | 시점 | 컨텍스트 |
|---|---|---|
| `OnSkillCommand` | `ProcessSkillCommands` 진입 직후 (캔슬·위임 가능) | Owner, SkillId, TargetPos |
| `OnSkillStart` | Process 진입 직후 | Owner |
| `OnSkillCommit` | 실제 `DamageTime` 커밋 직후, Projectile/Area/직접 타격 분기 전 (`SuppressDefaultImpact`로 기본 전달 억제 가능, 2026-09-24 추가) | Owner, SkillEntity, 커밋 TargetPos |
| `OnHit` | 적중한 적마다 1회 | Owner, Target, DamageResult |
| `OnCrit` | 치명타 적중 | 위 + IsCrit |
| `OnKill` | 적중으로 적이 사망 | Owner, KilledTarget |
| `OnProjectileSpawn` | `ProjectileHelper.SpawnProjectile`에서 논리 Projectile 생성 직후 | ProjectileEntity |
| `OnProjectileHit` | 발사체 적중 시 | ProjectileEntity, Target |
| `OnSkillEnd` | End 단계 종료 | Owner |

**디스패처**: `SkillEffectExecutor` 정적 유틸 1곳에 모아 EffectType별 switch. `System_Skill`은 트리거 호출 1줄씩만 추가.

**도입 효과 (예시)**:
| 옵션 | EffectType | Kind | Trigger | Param 의미 |
|---|---|---|---|---|
| 명중 시 HP 흡수 (스킬 한정) | LifeSteal | StatBonus | OnHit | Percent (캐릭터 stat과 합산되어 `profile.LifeStealRatio`로 캐시) |
| 점화 1스택 부여 | ApplyBuff | EffectAction | OnHit | BuffId, Stack |
| 처치 시 마나 회복 | ManaRestoreOnKill | EffectAction | OnKill | Amount |
| 치명타 시 추가 발사체 | SpawnProjectileOnCrit | EffectAction | OnCrit | ProjectileId, Count |
| 투사체 분리 | SpawnProjectileOnHit | EffectAction | OnProjectileHit | Count, Spread, DamageRatio |
| 시체 폭발 | SpawnAreaEffectOnKill | EffectAction | OnKill | Radius, Damage |
| 명중 시 넉백·당기기 (§6.10) | Knockback / Pull | EffectAction | OnHit | Param1=거리(칸), Param2=시간(초) |
| **토템 시전 위임** (§6.9) | DelegateToTotem | EffectAction | OnSkillCommand | TotemTableId, Duration |
| **지뢰 시전 위임** (§6.9) | DelegateToMine | EffectAction | OnSkillCommand | MineTableId, MaxMines |
| **연쇄·분열 전파** (구현됨, S3) | SkillPropagation | EffectAction | OnSkillCommit / OnHit | SkillPropagationTableId |

**영향**: §6.2(버프 적용)·§6.7·§6.9 모두 EffectType으로 흡수됨. 새 옵션 추가 시 EffectType enum 1줄 + 디스패처 case 1개 + 테이블 행 1개. 기존 SkillTable 행 변경 0.

### §6.9 토템·지뢰·함정 — 시전 주체 위임 ◐ *(토템 완료, 지뢰·함정 미구현)*
**목적**: PoE 보조 젬(Spell Totem / Remote Mine / Trap)처럼 **플레이어가 시전한 스킬을 다른 엔티티가 대신 시전하도록** 한다.

**토템 현행 구현**:
- `SkillEffectTable` 효과 1010 `Delegate_Totem_8s`가 `OnSkillCommand`에서 원래 시전을 취소하고 `EntityFactory.CreateTotem`을 호출한다.
- 토템은 시전자의 `StatComponent`와 `FactionComponent`를 스냅샷 복사하고 `CasterLinkComponent`, `LifetimeComponent`, `TotemTag`를 가진다.
- 슬롯 0에 원본 스킬을 생성하며 `StationaryAttackStateHandler`가 같은 진영을 제외한 사거리 내 적을 자율 공격한다.
- `System_Lifetime`은 만료 시 `DestroyTag`를 부착하고, HP 0 정리는 기존 `System_HpCheck` 경로를 사용한다.

**남은 작업**:
- `DelegateToMine` / `DelegateToTrap` EffectType과 실행기 분기
- `MineComponent` / `TrapComponent`, 기폭·근접 트리거 시스템과 데이터
- 토템의 실제 스킬별 PlayMode 검증과 전용 View/연출

**영향**: 토템 위임은 데이터로 사용 가능하다. 지뢰·함정은 후속 카테고리로 남는다.

### §6.10 넉백·당기기 (강제 이동) ○ *(설계 확정 2026-10-12, 미구현)*
**목적**: 명중한 대상을 짧게 밀어내거나(넉백) 끌어당긴다(당기기). 두 방식은 **멈출 거리 계산만 다르고** 이동 처리는 같은 경로를 쓴다. 나중에 갈고리처럼 끝까지 끌어오는 방식이 생겨도 멈춤 계산만 추가한다.

**방향 규칙** — 판정 범위의 중심이 기준점이다. 위치는 발 위치(`TransformComponent.Position`)로 계산하고 몸통 보정(`HitOffset`)은 쓰지 않는다.

| 공격 종류 | 기준점 | 밀기 | 당기기 (멈추는 곳) |
|---|---|---|---|
| 직접 공격 (`Direction`, 예: Strike·360도 노바) | 공격자 발 위치 | 공격자 반대쪽 | 공격자 쪽, 두 몸이 닿는 거리 |
| 지점 공격 (`Position` 직접 타격) | 커밋된 지정 지점 | 지점 바깥쪽 | 지점 쪽, 지점 |
| 장판·폭발 (`AreaEffect`) | 장판 중심 | 중심 바깥쪽 | 중심 쪽, 중심 |
| 발사체·연쇄 | 없음 | 날아온 방향(`ImpactDirection`) | 쏜 사람(Owner) 쪽, 두 몸이 닿는 거리 |

- 직접·지점 공격의 기준점은 `System_Skill.ProcessSkillHit`가 이미 계산하는 `commitPosition`이다(Direction=시전자 위치, Position=지정 지점). 다단 타격은 타격마다 다시 계산된 값을 쓴다.
- "두 몸이 닿는 거리" = 공격자 `HitRadius` + 대상 `HitRadius`. 이미 그 안쪽(장판은 중심)이면 움직이지 않는다. 설정 거리는 상한이며 기준점을 넘어가지 않는다.
- 기준점과 대상 위치가 같으면 공격자 조준 방향(`SkillTargetComponent.TargetDirection`)으로 밀고, 그 방향도 없으면(장판 등) 적용하지 않는다.

**예시 (거리 1칸·0.15초)**: Strike에 맞은 고블린은 0.15초 동안 플레이어 반대쪽으로 1칸 밀린다(처음 빠르고 끝에서 느려짐). 그동안 스스로 움직이지 못하고, 뒤에 벽이 있으면 벽에서 멈추거나 벽을 따라 미끄러진다. 불의 고리 중심에서 0.5칸 떨어진 적은 밀기면 1.5칸 위치로, 당기기면 0.5칸만 끌려와 중심에서 멈춘다. 반복 장판에 밀기를 붙이면 틱마다 밀려 장판 밖으로 나가고, 당기기를 붙이면 소용돌이처럼 계속 끌려온다.

**데이터**:
- `SkillEffect` 행: `EffectType=Knockback`(밀기) 또는 `Pull`(당기기), `Kind=EffectAction`, `Trigger=OnHit`, `Param1=거리(칸)`, `Param2=걸리는 시간(초)`, `Param3`=미사용, `Probability`=발동 확률. `Tier=0`·`PageCost=0`으로 스킬 페이지 랜덤 풀에서 제외한다.
- 스킬에 붙이기: `SkillTable.SkillEffectIds`에 행 Id를 넣는다. 직접 타격·발사체·연쇄는 모두 `SkillHitProcessor` → `OnHit`을 지나므로 별도 분기가 없다.
- 장판·폭발에 붙이기: `AreaEffectTable.ForcedMoveEffectId`(새 열, `SkillEffect` 행 Id, 0=없음). 장판은 `SkillEffect` 트리거를 실행하지 않으므로 `System_AreaEffect`가 대상마다 피해·버프 뒤에 직접 적용한다. 반복 장판은 틱마다, 단발 폭발은 1회 적용한다.
- 다운로드 검사: `Knockback`/`Pull` 행은 `Trigger=OnHit`·`Kind=EffectAction`·`Param1>0`·`Param2>0`이어야 한다. `ForcedMoveEffectId`가 가리키는 행은 `Knockback`/`Pull`이어야 하며, 다운로드 시점에 대조할 수 없으면 실행 시 오류 로그 후 무시한다.
- 시험 기본값은 1칸·0.15초. 시험 연결은 Strike(Skill 1)에 밀기, 불의 고리(AreaEffect 2)에 밀기, 독구름(AreaEffect 1)에 당기기이며, 검증 뒤 유지 여부를 정한다.

**적용 규칙 (2026-10-12 확정)**:
1. 밀리는 동안 대상의 스킬·공격 준비는 취소하지 않는다. 행동 중단은 기절의 역할로 남긴다.
2. 밀리는 중인 대상에게 새 넉백·당기기가 오면 무시한다. 멀티샷 3발이 동시에 맞아도 한 번만 밀린다.
3. 발사체·연쇄의 밀기는 날아온 방향, 당기기는 쏜 사람 쪽이다.
4. 몬스터 등급과 무관하게 모두 같은 거리로 밀린다. 등급별 배율은 후속(`MonsterArchetypeTable` 열 후보)이다.
5. 스킬 페이지(룬)로 드랍하지 않는다.
6. 적용하지 않는 경우: 회피(`OnHit`은 회피해도 실행되므로 효과 쪽에서 `DamageResult.IsEvaded`를 확인), 이 타격으로 사망(HP 0), 점프 중(`JumpComponent`), 토템(`TotemTag`), 스스로 움직이지 않는 대상(`VelocityComponent` 없음). 막기(Block)는 밀린다.

**런타임 구조**:
- `ForcedMoveComponent`(대상 엔티티): 방향, 총 거리(당기기는 멈춤 거리로 잘라 둔 값), 시간, 경과 시간, 지금까지 이동한 거리. 세이브에 저장하지 않는다. `ComponentManager._poolSizes` 128.
- `ForcedMoveStarter`(정적·상태 없음): 적용 판정, 방향, 멈춤 거리를 계산해 컴포넌트를 붙인다. `SkillEffectExecutor`와 `System_AreaEffect`가 같이 쓴다.
- `System_ForcedMove`(`IUpdateSystem`, Priority 810 — `System_Jump` 800 뒤, `System_Render` 1000 전): 처음 빠르고 끝에 느린 곡선으로 진행한다. 한 프레임 이동량을 `MoveRadius × 0.9` 이하 조각으로 나눠 `CollisionUtil.ResolveAxisSeparated`로 처리해 벽을 따라 미끄러지고 프레임이 떨어져도 벽을 뚫지 않는다. 끝나면 컴포넌트를 제거하고, `OnReset`에서 모두 제거한다.
- 적중 정보: `SkillHitRequest`·`SkillEffectContext`에 기준점(`HitOrigin`, `HasHitOrigin`)을 추가한다. `ImpactDirection`은 전파의 첫 탐색 축으로 쓰이므로 기준점 용도로 덮어쓰지 않는다.
- 자기 이동 차단: `System_Render`의 속도→위치 반영에서 `ForcedMoveComponent` 대상을 건너뛴다(플레이어·AI·NPC 공통 한 곳). `System_Move`는 점프 중과 같이 플레이어 입력 속도를 0으로 두고, 이동 상태를 `Idle`로 둔다(AI는 넉백 중에도 추적 방향을 쓰므로 걷기 애니메이션 방지).
- 정리: 점프 시작(`System_Skill.StartJump`) 때 넉백을 끝낸다(점프 우선). 던전 출입 때 `DungeonManager`가 `JumpComponent`를 지우는 곳에서 함께 지운다.

**Update에서 처리하는 이유**: 자기 이동은 `System_Render`(Update)에서 위치에 반영되고 보간이 없어서, FixedUpdate에서만 움직이면 화면에서 끊겨 보인다(`System_Jump`도 같은 이유로 Update). 끝 위치를 정해 두고 보간하는 방식은 벽을 따라 미끄러지지 못한다. 이 결정으로 `SKILL_EFFECT_IDEAS.md` #6의 초안 메모(FixedUpdate 110~120, 끝 위치 보간)를 대체한다.

**파일 지도**: `GlobalEnum.Stat`(맨 끝에 `Knockback`·`Pull` 추가 — 스탯 버킷 수는 `DelegateToTotem` 기준이라 영향 없음), `Common/Component/ForcedMoveComponent.cs`(신규), `Skill/Combat/ForcedMoveStarter.cs`(신규), `Common/System/System_ForcedMove.cs`(신규)·`SystemManager` 등록, `SkillHitProcessor`·`SkillEffectContext`·`SkillEffectExecutor`, `System_Skill`(`ProcessSkillHit` 기준점·`StartJump` 정리), `System_AreaEffect`·`Tables.cs`(`AreaEffectTable`)·`DownloadTables.cs`, `System_Render`·`System_Move`, `DungeonManager`, `ComponentManager`.

**후속 확장 (미구현)**: 등급별 넉백 배율, 벽 충돌 조건(`SKILL_RUNE_DESIGN.md` §5.2의 `WallNearby`·`KnockbackTarget`)은 `ForcedMoveComponent`에 "의도한 거리보다 덜 움직였는지"를 기록해 연결한다. 끝까지 끌어오는 갈고리형 당기기는 멈춤 계산 방식을 하나 추가한다.

**검증 (구현 완료 후 1회)**: 런타임·에디터 컴파일, 표 다운로드 검사, PlayMode로 ① Strike 넉백 방향·거리 ② 벽에서 멈춤·미끄러짐·관통 없음 ③ 멀티샷 동시 적중 1회 ④ 장판 밀기·당기기와 중심 멈춤 ⑤ 직접 공격 당기기의 접촉 거리 멈춤 ⑥ 회피·사망·점프 중 미적용 ⑦ 밀리는 몬스터의 공격 계속 진행 ⑧ 몬스터에게 밀린 플레이어의 조작 복귀 ⑨ 밀리는 중 던전 출입. 전투 전용 진단 메뉴는 없어서 PlayMode로 확인한다.

---

## 10. 스킬 ID 예약 (제안)

테이블 충돌 방지를 위해 ID 대역 예약.

| 대역 | 카테고리 | 예약 슬롯 |
|---|---|---|
| 1 ~ 49 | 근접 (A) | A1=1 (기존), A2=10, A3=11, A4=12, A5=13 |
| 50 ~ 99 | 이동 (M) | M1=5 (기존, QuickHop), M2=50, M3=51 |
| 100 ~ 199 | 원거리 (R) | R1=100, R2=101, R3=102, R4=103 |
| 200 ~ 299 | 스펠 (S) | S1=200, S2=201, S3=202 (기존, ChainLightning), S4=203 (기존), S5/S6=미정 |
| 300 ~ 399 | 버프 (B) | B2=301, B3=302 |
| 400 ~ 499 | 채널/차징 (C) | C1=400, C2=401 |

ProjectileId 대역도 동일 규칙 권장: 화살 100번대, 마법 200번대.

기존 제안 ID 204와 205는 각각 몬스터 전용 `GoblinFirebolt`, `SwampToadPoisonSpit`이 사용 중이다. S5 Lightning Strike와 S6 Frost Bolt를 추가할 때 충돌하지 않는 새 ID를 배정한다. (2026-10-03: 205 행은 한때 `SkillTable`에서 빠졌다가 복구됐다 — §2. 206은 `FireRing`이 사용 중이다.)

---

## 11. 구현 우선순위 로드맵

현행 공용 런타임을 재사용하고, 필요한 새 메커니즘을 묶어 추가하는 순서다.

1. **데이터 전용 스킬 추가**: A2 Cleave, A4 Heavy Slam, M2 Leap Slam, R1 Power Shot, R3 Piercing Bolt, S1 Fireball, S2 Ice Nova, S5 Lightning Strike, S6 Frost Bolt. 스킬·투사체·효과·프리팹을 연결하고 S5/S6에는 충돌하지 않는 새 ID를 배정한다.
2. **라인 히트 공용화**: 선분/캡슐 판정을 하나의 런타임 경로로 추가한 뒤 M3 Dash Strike와 C1 Beam Cast가 공유한다.
3. **포물선 투사체**: Projectile 런타임에 높이 진행과 충돌 정책을 추가해 R4 Arc Shot을 구현한다.
4. **자기·아군 버프**: 자기 자신과 동일 진영 범위 선택, 적용·제거 수명주기를 추가해 B2 Iron Skin과 B3 Battle Roar를 구현한다. B2는 토글 OFF와 지속 마나 소비까지 포함한다.
5. **차지 해제 발동**: `ProcessChargeSkill`에 입력 해제 감지, 최소/최대 차지 비율, 피해 스케일을 추가해 C2 Charged Bolt를 구현한다.
6. **후속 확장**: 필요할 때 지뢰·함정 위임과 신규 SkillEffectType을 추가한다.

각 단계는 테이블 정합성 검사 후 Direction·Position·Jump·Projectile·Area·MultiHit·Channeling 대표 스킬을 PlayMode에서 검증한다.

---

## 12. 변경 이력

- 2026-05-02: 초안 작성. 25종 스킬 카탈로그 + 7개 시스템 보강 항목 정의.
- 2026-05-02 (rev 2): §6.8 SkillEffect 합성형 효과 시스템 추가, §6.9 토템·지뢰·함정 시전 주체 위임 추가. §6.7을 §6.8에 흡수 가능 항목으로 표시. §11 로드맵 재정렬 (SkillEffect 우선).
- 2026-05-04: §6.1 완료 표시. 입력 유지 메커니즘은 `SkillInputHeldTag`(제안) 대신 `InputComponent.SkillSlotHeldMask` 비트마스크로 채택. Charge/Toggle 처리 로직은 미완으로 남음. §11 로드맵에서 §6.1 의존 항목 정리. A3 Whirlwind 채널링 변형 추가 (Skill 시트 Id=11, ExecutionType=2).
- 2026-05-04 (rev 2): §6.8 SkillEffect 골격 완료 표시 (Executor/Context/Trigger·EffectType enum/SkillEffect 시트). LifeStealOnHit/ApplyBuffOnHit/DelegateToTotem 3종 구현됨. A5 Lacerate 추가 (Skill 시트 Id=13, SkillEffectIds=[1002 OnHit_Bleed]).
- 2026-05-04 (rev 3): R2 Multi Shot 구현 — `SkillEffectType.SpawnProjectile` + `Stat.ProjectileCountAdd` 추가. SkillEffect 2003 / Skill Id=101. 발사체 옵션 합산 컨벤션(Param=base, Stat=modifier) 도입. §6.4를 §6.8에 흡수. 분산 각도는 코드 상수(15°/발) 고정 — 추후 Stat 추가 시 교체 예정.
- 2026-05-04 (rev 4): R2 설계 재검토 — SpawnProjectile EffectType 기반 접근은 "스킬당 별도 SkillEffect 행 필요"라는 조합 폭발 문제로 폐기. 대신 **`SkillTable.BaseProjectileCount` 컬럼**을 추가하고 System_Skill의 ProjectileId 분기를 N발 부채꼴 발사로 확장. R2는 `ProjectileId=1, BaseProjectileCount=3`만으로 정의. `Stat.ProjectileCountAdd`는 모든 발사체 스킬에 자동 적용. SpawnProjectile EffectType은 "스킬과 다른 종류의 발사체 스폰" 용도로 살려둠.
- 2026-05-16: `LifeSteal` 효과 경로 단일화. 기존 `LifeStealOnHit` (EffectAction)을 폐기하고 `SkillEffectKind.StatBonus + EffectType.LifeSteal`로 통합. `SkillStatBonusOnHitComponent.LifeStealBonus` 필드 추가 → `SkillAttackProfileComponent.LifeStealRatio`에서 캐릭터 stat과 합산 캐시. SkillEffectExecutor의 `LifeStealOnHit` 케이스 제거, `DownloadTables.InferSkillEffectKindFromEffectType`에서 `LifeSteal` 분기 제거(이제 default StatBonus로 추론). 매 적중마다 트리거 디스패치 없이 `DamageCalculator`가 단일 비율로 흡혈 처리. SkillEffect 1001 행 `Kind=StatBonus` 확정.
- 2026-08-28: `SkillTargetType.SingleEntity` 폐기. 네임락(대상 ID 고정) 방식을 사용하지 않기로 결정하여 enum 멤버와 관련 런타임 경로(`FindClosestEntity`, `SkillTargetComponent.TargetId`/`TargetType`/`SetEntityTarget`/`HasTarget`)를 모두 제거. 이에 의존한 S3 Chain Lightning / B1 War Cry / B4 Curse of Weakness / C3 Drain Life 4종과, 유일 의존 스킬이 사라진 §6.5(연쇄 메커니즘)를 카탈로그에서 삭제 — 25종 → 21종. 문서와 실제 테이블이 어긋나 있던 A1 Strike / M1 QuickHop의 타겟 타입을 실제 값(`Direction`)으로 정정. 아울러 `SkillUsePolicyResolver.RequiresDirectionalAim`이 명시된 `AimTrackingMode`보다 먼저 검사되어 명시값을 무시하던 문제를 수정 — 이제 이 판정은 `Default` 해석 규칙과 화살표 표시 판단에만 쓰인다(명시값 우선).
- 2026-09-20: 코드·바이너리 테이블 재대조. 구현 판정을 `공용 런타임`과 `개별 콘텐츠 등록`으로 분리하고 21종을 구현 6종·런타임 준비 9종·코드 보강 필요 6종으로 재분류했다. 마나 소비, Position 즉발, Jump 착지 커밋, 관통, AreaEffect, ApplyBuff, 토템 위임의 완료 상태를 반영하고 로드맵을 남은 작업 기준으로 교체했다. S5/S6의 기존 예약 ID 204/205 충돌도 기록했다.
- 2026-10-03: 코드·바이너리 테이블 재대조. S3 Chain Lightning을 구현됨으로 카탈로그에 다시 포함했다(SkillId 202 / SkillEffect 1020 / SkillPropagation 1, 2026-09-24 c87d5de) — 22종 중 구현 7종. §6.8에 `SkillTrigger.OnSkillCommit`(9종), `SkillPropagation` EffectAction, 효과 1020, `SkillEffectContext` 전파 필드를 반영했다. §1.3 `SkillTargetRange2`를 반각(180 이상=원)으로 정정했다. `SwampToadPoisonSpit`(205)이 `SkillTable`에서 빠졌는데 `AiTable` Id 4가 아직 참조해 빌드가 막히던 문제를 §2에 기록하고, 같은 날 205번 행을 복구했다. R2 분산 각도 상수의 정본 위치를 갱신했다.
- 2026-10-12: §6.10 넉백·당기기(강제 이동) 설계 확정, 미구현. 판정 범위 중심을 기준점으로 밀거나 당기고, `SkillEffect` 행(`Knockback`/`Pull`)과 `AreaEffectTable.ForcedMoveEffectId`로 붙인다. §6.8의 `KnockbackOnHit` 표기를 갱신하고 `SKILL_EFFECT_IDEAS.md` #6 초안 메모를 대체했다.
