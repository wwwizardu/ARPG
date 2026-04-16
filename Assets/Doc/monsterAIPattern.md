# 몬스터 종류별 AI 패턴 기획서

**작성일**: 2026-04-15
**현재 상태**: 설계 완료, 구현 전
**의존성**: AI 시스템 (85%), 전투 시스템 (95%), 스킬 시스템 (100%)

---

## 목차
1. [개요](#1-개요)
2. [현재 시스템 분석](#2-현재-시스템-분석)
3. [몬스터 종류 정의](#3-몬스터-종류-정의)
4. [다중 스킬 선택 시스템](#4-다중-스킬-선택-시스템)
5. [AiTable 확장 데이터](#5-aitable-확장-데이터)
6. [보스 AI 패턴](#6-보스-ai-패턴)
7. [구현 계획](#7-구현-계획)
8. [테스트 시나리오](#8-테스트-시나리오)

---

## 1. 개요

### 목표
현재 근접(Melee)/원거리(Ranged) 2종뿐인 AI를 **6종 일반 몬스터 + 2종 보스**로 확장한다.
기존 `AIBehaviorFactory` + `IAIStateHandler` 아키텍처를 최대한 활용하고, 새로운 StateHandler 추가는 최소화한다.

### 핵심 원칙
- **테이블 기반 분기**: 몬스터별 차이는 코드가 아닌 AiTable/SkillTable 데이터로 표현
- **기존 StateHandler 재활용**: 새로운 BehaviorType 추가 시에도 기존 핸들러 조합으로 구성
- **다중 스킬 선택**: AiTable.SkillId1~3을 상황별로 선택하는 로직 추가

---

## 2. 현재 시스템 분석

### 구현 완료된 것

| 항목 | 내용 |
|------|------|
| **BehaviorType** | Melee, Ranged, Patrol, PatrolRanged 구현 / Defensive, Aggressive, Support 미구현 |
| **StateHandler** | Idle, Patrol, Chase, MeleeAttack, RangedAttack, Retreat, Flee (7종) |
| **AiTable** | Id 1(근접), Id 2(원거리) 2종만 존재 |
| **MonsterTable** | Id 1001~1003 3종, 모두 이름 "Monster" |
| **SkillTable** | 몬스터용 Id 2(근접), 3(자폭), 4(발사체) 3종 |

### 코드 확장 포인트

```
[AiTable 데이터 추가]     ← 몬스터별 AI 세팅 (테이블만 추가하면 됨)
[SkillTable 데이터 추가]  ← 몬스터 스킬 추가 (테이블만 추가하면 됨)
[MonsterTable 데이터 추가] ← 몬스터 종류 추가 (테이블만 추가하면 됨)

[스킬 선택 로직 추가]    ← SkillId1~3 중 상황별 선택 (코드 추가 필요)
[Aggressive 프로필 추가]  ← AIBehaviorFactory에 등록 (코드 추가 필요)
[보스 StateHandler 추가]  ← 페이즈 전환 로직 (코드 추가 필요)
```

---

## 3. 몬스터 종류 정의

### 3.1 슬라임 (Slime) - 근접 기본형

**컨셉**: 느리지만 단단한 기본 적. 초보 플레이어가 전투를 배우는 상대.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Melee |
| **DetectionRange** | 4.0 |
| **AttackRange** | 0.8 (SkillRangeMax 참조) |
| **KeepDistance** | 0.5 (거의 밀착) |
| **이동 속도** | 느림 (StatTable.MoveSpeed 낮게) |
| **특징** | 단일 스킬, 단순 추격 |

**행동 패턴**:
```
Idle → (감지) → Chase(느림) → Attack(몸통 박치기) → Chase 반복
후퇴 없음 (RetreatThreshold = 0)
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 |
|-------|---------|------|------|
| SkillId1 | 2 | 몸통 박치기 | 근접 단타, 낮은 데미지 |

---

### 3.2 고블린 (Goblin) - 근접 공격형

**컨셉**: 빠르고 교활한 근접 딜러. 체력이 낮으면 후퇴했다가 다시 돌진.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Melee |
| **DetectionRange** | 6.0 |
| **AttackRange** | 1.0 |
| **KeepDistance** | 1.5 |
| **이동 속도** | 빠름 |
| **특징** | 2가지 스킬, 후퇴 후 재진입 |

**행동 패턴**:
```
Idle → (감지) → Chase(빠름) → Attack(베기/찌르기) → Retreat(HP 30% 이하) → Chase 재진입
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 | 선택 조건 |
|-------|---------|------|------|-----------|
| SkillId1 | 10 | 베기 | 기본 근접 공격, 쿨타임 짧음 | 기본 |
| SkillId2 | 11 | 돌진 베기 | 전방 돌진 + 높은 데미지, 긴 쿨타임 | 거리 2.0 이상 & 쿨타임 OK |

---

### 3.3 해골 궁수 (Skeleton Archer) - 원거리 기본형

**컨셉**: 멀리서 화살을 쏘고 거리를 유지. 접근하면 후퇴.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Ranged |
| **DetectionRange** | 7.0 |
| **AttackRange** | 5.0 (SkillRangeMax 참조) |
| **KeepDistance** | 7.0 (최대한 거리 유지) |
| **이동 속도** | 보통 |
| **특징** | 발사체 공격, 거리 유지 AI |

**행동 패턴**:
```
Idle → (감지) → Chase(적정 거리까지) → Attack(화살) → Retreat(접근 시) → Attack 반복
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 | 선택 조건 |
|-------|---------|------|------|-----------|
| SkillId1 | 4 | 화살 | 기본 발사체 공격 | 기본 |
| SkillId2 | 12 | 다연발 | 3연속 화살, 긴 쿨타임 | 쿨타임 OK |

---

### 3.4 박쥐 (Bat) - 공격형 (Aggressive)

**컨셉**: 매우 빠르고 공격적. 감지 즉시 돌진, 후퇴 없이 사망까지 공격.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Aggressive |
| **DetectionRange** | 5.0 |
| **AttackRange** | 0.6 |
| **KeepDistance** | 0 (밀착 허용) |
| **이동 속도** | 매우 빠름 |
| **특징** | 후퇴 없음, 빠른 공격 속도 |

**행동 패턴**:
```
Idle → (감지) → Chase(매우 빠름) → Attack(물기, 빠른 쿨타임) → Chase 반복
후퇴 없음: Retreat 핸들러 = null (Attack에서 범위 벗어나면 즉시 Chase)
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 | 선택 조건 |
|-------|---------|------|------|-----------|
| SkillId1 | 13 | 물기 | 빠른 근접 공격, 짧은 쿨타임(0.3초) | 기본 |

**Aggressive 프로필** (AIBehaviorFactory에 신규 등록):
| State | Handler | 비고 |
|-------|---------|------|
| Idle | IdleStateHandler | 기존 재사용 |
| Chase | ChaseStateHandler | 기존 재사용 |
| Attack | MeleeAttackStateHandler | 기존 재사용, KeepDistance=0이므로 Retreat 전환 안 됨 |
| Retreat | **null** | 후퇴하지 않음 |

---

### 3.5 독 거미 (Poison Spider) - 근접 + 상태이상

**컨셉**: 느리지만 독 공격. 독 DoT으로 지속 데미지를 준다.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Melee |
| **DetectionRange** | 5.0 |
| **AttackRange** | 0.8 |
| **KeepDistance** | 1.0 |
| **이동 속도** | 느림 |
| **특징** | 독 상태이상 부여 스킬 |

**행동 패턴**:
```
Idle → (감지) → Chase(느림) → Attack(독 물기/독액 분사) → Retreat(HP 낮으면) → Chase
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 | 선택 조건 |
|-------|---------|------|------|-----------|
| SkillId1 | 14 | 독 물기 | 근접 공격 + 중독 버프 | 기본 |
| SkillId2 | 15 | 독액 분사 | 전방 부채꼴 AoE + 중독 | 쿨타임 OK & 거리 1.5 이내 |

---

### 3.6 마법사 (Dark Mage) - 원거리 + 다중 스킬

**컨셉**: 다양한 마법을 사용하는 지능적인 적. 상황에 따라 스킬을 선택.

| 항목 | 값 |
|------|-----|
| **BehaviorType** | Ranged |
| **DetectionRange** | 8.0 |
| **AttackRange** | 6.0 |
| **KeepDistance** | 8.0 |
| **이동 속도** | 느림 |
| **특징** | 3가지 스킬을 상황별 선택 |

**행동 패턴**:
```
Idle → (감지) → Chase(적정 거리까지) → Attack(화염탄/냉기파/번개) → Retreat(접근 시) → Attack
```

**스킬 구성**:
| 슬롯 | SkillId | 이름 | 설명 | 선택 조건 |
|-------|---------|------|------|-----------|
| SkillId1 | 16 | 화염탄 | 발사체, 점화 버프 | 기본 (다른 스킬 쿨타임일 때) |
| SkillId2 | 17 | 냉기파 | 전방 AoE, 냉기 디버프 | 타겟 거리 3.0 이내 & 쿨타임 OK |
| SkillId3 | 18 | 번개 | 즉발 고데미지, 긴 쿨타임 | 쿨타임 OK (우선순위 최고) |

---

### 몬스터 종류 요약

| 몬스터 | BehaviorType | 속도 | 스킬 수 | 핵심 특징 |
|--------|-------------|------|---------|-----------|
| 슬라임 | Melee | 느림 | 1 | 단순, 초보용 |
| 고블린 | Melee | 빠름 | 2 | 후퇴 + 재돌진 |
| 해골 궁수 | Ranged | 보통 | 2 | 거리 유지 + 발사체 |
| 박쥐 | **Aggressive** | 매우 빠름 | 1 | 후퇴 없는 속공 |
| 독 거미 | Melee | 느림 | 2 | 독 상태이상 |
| 마법사 | Ranged | 느림 | 3 | 다중 스킬 선택 |

---

## 4. 다중 스킬 선택 시스템

### 4.1 개요

현재 모든 AI는 `SkillHelper.GetSkillCommandComponent(0, entityId, targetPos)`로 **슬롯 0(SkillId1)만 사용**한다.
이를 확장하여 SkillId1~3 중 상황에 맞는 스킬을 선택하도록 한다.

### 4.2 선택 로직

```
스킬 선택 우선순위:
  1. SkillId3 (가장 강력한 스킬) → 쿨타임 OK이면 사용
  2. SkillId2 (조건부 스킬)     → 조건 충족 & 쿨타임 OK이면 사용
  3. SkillId1 (기본 스킬)       → 항상 사용 가능 (폴백)
```

### 4.3 AiTable 확장 필드

현재 AiTable에 스킬 선택 조건을 추가한다:

```
기존: SkillId1, SkillId2, SkillId3

추가: Skill2MinRange   (float) - SkillId2 사용 최소 거리 (0=거리 무관)
      Skill2MaxRange   (float) - SkillId2 사용 최대 거리 (0=거리 무관)
      Skill3MinRange   (float) - SkillId3 사용 최소 거리
      Skill3MaxRange   (float) - SkillId3 사용 최대 거리
```

### 4.4 선택 알고리즘 (의사 코드)

```csharp
// AISkillSelector.SelectSkill(entityId, targetEntityId) → int skillSlotIndex

int SelectSkill(int entityId, int targetEntityId)
{
    AiTable aiTable = GetAiTable(entityId);
    float distance = GetDistance(entityId, targetEntityId);

    // 3번 슬롯 (최고 우선순위)
    if (aiTable.SkillId3 > 0 && IsSkillReady(entityId, slotIndex: 2))
    {
        if (IsInRange(distance, aiTable.Skill3MinRange, aiTable.Skill3MaxRange))
            return 2;
    }

    // 2번 슬롯
    if (aiTable.SkillId2 > 0 && IsSkillReady(entityId, slotIndex: 1))
    {
        if (IsInRange(distance, aiTable.Skill2MinRange, aiTable.Skill2MaxRange))
            return 1;
    }

    // 1번 슬롯 (폴백)
    if (IsSkillReady(entityId, slotIndex: 0))
        return 0;

    return -1; // 모든 스킬 쿨타임 중
}
```

### 4.5 적용 위치

`MeleeAttackStateHandler`와 `RangedAttackStateHandler`에서 현재:
```csharp
SkillHelper.GetSkillCommandComponent(0, entityId, targetPos);
```
이를 다음으로 변경:
```csharp
int slot = AISkillSelector.SelectSkill(entityId, targetEntityId);
if (slot >= 0)
    SkillHelper.GetSkillCommandComponent(slot, entityId, targetPos);
```

---

## 5. AiTable 확장 데이터

### 5.1 AiTable 필드 추가

```csharp
public class AiTable : TableBase
{
    // 기존 필드
    public string Name;
    public GE.AiType AiType;
    public AIBehaviorType BehaviorType;
    public float DetectionRange;
    public int SkillId1;
    public int SkillId2;
    public int SkillId3;

    // 추가 필드
    public float Skill2MinRange;    // SkillId2 사용 최소 거리 (0=무관)
    public float Skill2MaxRange;    // SkillId2 사용 최대 거리 (0=무관)
    public float Skill3MinRange;    // SkillId3 사용 최소 거리
    public float Skill3MaxRange;    // SkillId3 사용 최대 거리
    public float KeepDistance;      // 유지 거리 오버라이드 (0=EntityFactory 기본값)
    public float RetreatHpPercent;  // 후퇴 HP 비율 (0=후퇴 안 함, 30=HP 30% 이하 시 후퇴)
}
```

### 5.2 AiTable 데이터 (신규)

| Id | Name | BehaviorType | DetectionRange | KeepDistance | RetreatHpPercent | SkillId1 | SkillId2 | Skill2MinRange | Skill2MaxRange | SkillId3 | Skill3MinRange | Skill3MaxRange |
|----|------|-------------|----------------|-------------|------------------|----------|----------|----------------|----------------|----------|----------------|----------------|
| 1 | 슬라임 | Melee | 4.0 | 0.5 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| 2 | 해골 궁수 | Ranged | 7.0 | 7.0 | 20 | 4 | 12 | 0 | 0 | 0 | 0 | 0 |
| 3 | 고블린 | Melee | 6.0 | 1.5 | 30 | 10 | 11 | 2.0 | 6.0 | 0 | 0 | 0 |
| 4 | 박쥐 | Aggressive | 5.0 | 0 | 0 | 13 | 0 | 0 | 0 | 0 | 0 | 0 |
| 5 | 독 거미 | Melee | 5.0 | 1.0 | 25 | 14 | 15 | 0 | 1.5 | 0 | 0 | 0 |
| 6 | 마법사 | Ranged | 8.0 | 8.0 | 20 | 16 | 17 | 0 | 3.0 | 18 | 0 | 0 |
| 10 | 보스:거대 슬라임 | Melee | 10.0 | 0 | 0 | 20 | 21 | 0 | 0 | 22 | 0 | 0 |
| 11 | 보스:고블린 왕 | Aggressive | 10.0 | 2.0 | 0 | 23 | 24 | 3.0 | 8.0 | 25 | 0 | 0 |

### 5.3 SkillTable 데이터 (신규 몬스터 스킬)

| Id | Name | 설명 | DamageMin-Max | SkillRangeMax | Cooltime | ProjectileId | DamageType |
|----|------|------|---------------|---------------|----------|-------------|------------|
| 10 | 고블린 베기 | 기본 근접 | 5-8 | 1.0 | 0.8 | 0 | Physical |
| 11 | 고블린 돌진 | 전방 돌진 공격 | 12-18 | 3.0 | 4.0 | 0 | Physical |
| 12 | 다연발 | 3연속 화살 | 2-4 | 5.0 | 5.0 | 2 | Physical |
| 13 | 물기 | 빠른 근접 | 3-5 | 0.6 | 0.3 | 0 | Physical |
| 14 | 독 물기 | 근접 + 중독 | 4-6 | 0.8 | 1.0 | 0 | Poison |
| 15 | 독액 분사 | 전방 AoE | 6-10 | 1.5 | 6.0 | 0 | Poison |
| 16 | 화염탄 | 발사체 | 6-10 | 6.0 | 1.5 | 3 | Fire |
| 17 | 냉기파 | 전방 AoE | 8-12 | 3.0 | 5.0 | 0 | Cold |
| 18 | 번개 | 즉발 고데미지 | 15-25 | 6.0 | 8.0 | 0 | Lightning |
| 20 | 보스:몸통 압사 | 넓은 근접 AoE | 10-15 | 2.0 | 1.5 | 0 | Physical |
| 21 | 보스:점프 착지 | 범위 충격파 | 20-30 | 4.0 | 6.0 | 0 | Physical |
| 22 | 보스:분열 소환 | 소환 스킬 | 0-0 | 0 | 15.0 | 0 | Physical |
| 23 | 보스:왕의 일격 | 강력한 근접 | 15-20 | 1.5 | 1.0 | 0 | Physical |
| 24 | 보스:투척 단검 | 중거리 발사체 | 8-12 | 5.0 | 3.0 | 4 | Physical |
| 25 | 보스:전투 함성 | 자기 버프 (공격력 증가) | 0-0 | 0 | 20.0 | 0 | Physical |

---

## 6. 보스 AI 패턴

### 6.1 거대 슬라임 (King Slime) - 페이즈 전환형

**컨셉**: HP에 따라 행동이 변하는 페이즈 보스.

```
[Phase 1] HP 100~50%
  - 느린 이동, 몸통 압사(SkillId1) 반복
  - 가끔 점프 착지(SkillId2) 사용

[Phase 2] HP 50~0%
  - 이동 속도 증가
  - 분열 소환(SkillId3) 1회 사용 → 작은 슬라임 2~3마리 스폰
  - 공격 패턴 빨라짐 (쿨타임 감소)
```

**구현 방식**:
- `BossPhaseComponent` 추가 (현재 페이즈, 페이즈별 속도 배율, 쿨타임 배율)
- HP 50% 이하 진입 시 Phase 2로 전환
- Phase 2에서 SkillId3(분열)을 1회 사용 후 SkillId3 비활성화

### 6.2 고블린 왕 (Goblin King) - 공격형 보스

**컨셉**: 후퇴 없이 공격적으로 싸우며 근접/원거리를 혼합.

```
[Phase 1] HP 100~40%
  - 왕의 일격(SkillId1) 기본 근접
  - 거리가 멀면 투척 단검(SkillId2) 사용
  - Aggressive: 후퇴하지 않고 계속 추격

[Phase 2] HP 40~0%
  - 전투 함성(SkillId3) 1회 사용 → 공격력 버프
  - 공격 속도 증가
  - 더 공격적으로 변화
```

### 6.3 보스 공통 구조

보스는 일반 몬스터와 동일한 AI 프레임워크를 사용하되, **BossPhaseComponent**로 페이즈 전환을 처리한다.

```csharp
// BossPhaseComponent (신규)
public struct BossPhaseComponent
{
    public int CurrentPhase;           // 현재 페이즈 (1~)
    public float PhaseHpThreshold;     // 다음 페이즈 진입 HP% (예: 50)
    public float SpeedMultiplier;      // 페이즈별 이동 속도 배율
    public float CooldownMultiplier;   // 페이즈별 쿨타임 배율 (0.7 = 30% 빠름)
    public bool HasUsedPhaseSkill;     // 페이즈 전환 스킬 사용 여부
}
```

**System_BossPhase** (Priority: 45, FixedUpdate):
```
1. BossPhaseComponent 풀 순회
2. HP% < PhaseHpThreshold이면 Phase 전환
3. Phase 전환 시:
   - CurrentPhase 증가
   - SpeedMultiplier / CooldownMultiplier 적용
   - HasUsedPhaseSkill = false (페이즈 스킬 사용 가능)
4. HasUsedPhaseSkill == false이면 SkillId3 우선 사용
```

---

## 7. 구현 계획

### 7.1 작업 순서 (총 예상 3~4일)

#### Task 1: 다중 스킬 선택 시스템 (1일)

**신규 파일**:
- `Assets/Scripts/AI/AISkillSelector.cs`

**수정 파일**:
- `Assets/Scripts/Common/Tables.cs` - AiTable에 필드 추가
- `Assets/Scripts/AI/StateHandlers/MeleeAttackStateHandler.cs` - SelectSkill 호출
- `Assets/Scripts/AI/StateHandlers/RangedAttackStateHandler.cs` - SelectSkill 호출

**작업 내용**:
1. AiTable에 Skill2MinRange, Skill2MaxRange, Skill3MinRange, Skill3MaxRange 추가
2. AiTable에 KeepDistance, RetreatHpPercent 추가
3. AISkillSelector 정적 클래스 생성
4. MeleeAttackStateHandler/RangedAttackStateHandler에서 AISkillSelector 사용
5. EntityFactory.AddAIComponents()에서 AiTable의 KeepDistance, RetreatHpPercent 반영

#### Task 2: Aggressive 프로필 추가 (0.5일)

**수정 파일**:
- `Assets/Scripts/AI/AIBehaviorFactory.cs` - Aggressive 프로필 등록

**작업 내용**:
1. Aggressive 프로필 등록 (Idle, null, Chase, MeleeAttack, null, null, null)
2. Retreat 핸들러를 null로 설정하여 후퇴 불가

#### Task 3: AiTable/SkillTable/MonsterTable 데이터 추가 (0.5일)

**수정 파일**:
- Google Sheets → AiTable, SkillTable, MonsterTable 데이터 추가
- bytes 파일 재생성

**작업 내용**:
1. 6종 몬스터 AiTable 데이터 추가 (Id 1~6)
2. 몬스터 스킬 데이터 추가 (Id 10~18)
3. MonsterTable에 6종 몬스터 추가
4. StatTable에 몬스터별 스탯 추가 (HP, 공격력, 방어력, 이동속도 등)

#### Task 4: 보스 AI 시스템 (1일)

**신규 파일**:
- `Assets/Scripts/Common/Component/BossPhaseComponent.cs`
- `Assets/Scripts/Common/System/System_BossPhase.cs`

**수정 파일**:
- `Assets/Scripts/AI/AISkillSelector.cs` - 보스 페이즈별 쿨타임 배율 적용
- `Assets/Scripts/Factory/EntityFactory.cs` - CreateBoss() 메서드 추가
- `Assets/Scripts/Manager/ComponentManager.cs` - BossPhaseComponent 풀 추가

**작업 내용**:
1. BossPhaseComponent 정의
2. System_BossPhase 구현 (페이즈 전환, 속도/쿨타임 배율)
3. EntityFactory.CreateBoss() 메서드 (CreateMonster + BossPhaseComponent)
4. 보스 AiTable/SkillTable 데이터 추가

#### Task 5: 테스트 및 밸런싱 (1일)

- 6종 일반 몬스터 AI 동작 확인
- 다중 스킬 선택 동작 확인
- 보스 페이즈 전환 확인
- 데미지/HP 밸런싱
- 버그 수정

### 7.2 코드 변경 범위 요약

| 구분 | 파일 | 변경 내용 |
|------|------|-----------|
| **신규** | `AISkillSelector.cs` | 다중 스킬 선택 로직 |
| **신규** | `BossPhaseComponent.cs` | 보스 페이즈 데이터 |
| **신규** | `System_BossPhase.cs` | 보스 페이즈 전환 시스템 |
| 수정 | `Tables.cs` | AiTable 필드 추가 |
| 수정 | `AIBehaviorFactory.cs` | Aggressive 프로필 등록 (1줄) |
| 수정 | `MeleeAttackStateHandler.cs` | SelectSkill 호출 (2줄) |
| 수정 | `RangedAttackStateHandler.cs` | SelectSkill 호출 (2줄) |
| 수정 | `EntityFactory.cs` | KeepDistance/RetreatHpPercent 반영, CreateBoss() |
| 수정 | `ComponentManager.cs` | BossPhaseComponent 풀 등록 |
| 데이터 | AiTable.bytes | 8종 AI 데이터 |
| 데이터 | SkillTable.bytes | 16종 스킬 데이터 |
| 데이터 | MonsterTable.bytes | 8종 몬스터 데이터 |

---

## 8. 테스트 시나리오

### 시나리오 1: 슬라임 기본 동작
```
1. 슬라임 근처로 이동
2. 슬라임이 느리게 추격 시작
3. 공격 범위 진입 시 몸통 박치기
4. HP가 낮아져도 후퇴하지 않음 (RetreatHpPercent = 0)
5. 사망 시 아이템 드랍
```

### 시나리오 2: 고블린 다중 스킬
```
1. 고블린 감지 범위 진입
2. 거리 2.0 이상이면 돌진 베기(SkillId2) 사용
3. 근접 상태에서는 기본 베기(SkillId1) 사용
4. HP 30% 이하 시 후퇴
5. 거리 확보 후 다시 돌진
```

### 시나리오 3: 박쥐 공격형 AI
```
1. 박쥐 감지 범위 진입
2. 매우 빠르게 돌진
3. 빠른 속도로 물기 공격 반복 (쿨타임 0.3초)
4. 범위 벗어나면 즉시 재추격 (후퇴 없음)
5. HP 관계없이 끝까지 공격
```

### 시나리오 4: 마법사 스킬 선택
```
1. 마법사 감지 범위 진입
2. 원거리에서 화염탄(SkillId1) 기본 공격
3. 번개(SkillId3) 쿨타임 완료 시 즉시 사용 (최고 우선순위)
4. 플레이어가 3.0 이내 접근 시 냉기파(SkillId2) 사용
5. 너무 가까우면 후퇴 후 다시 원거리 공격
```

### 시나리오 5: 거대 슬라임 보스 페이즈
```
1. Phase 1: 느린 이동, 몸통 압사 + 가끔 점프 착지
2. HP 50% 도달 → Phase 2 전환
3. Phase 2 진입 시 분열 소환(SkillId3) 1회 사용
4. 작은 슬라임 2~3마리 스폰
5. 이동 속도 증가, 쿨타임 감소
6. 분열 스킬은 1회만 사용 (HasUsedPhaseSkill)
```

---

## 참고 자료

### 관련 기획 문서
- [combatSystem.md](./combatSystem.md) - 전투 시스템 전체 설계
- [combatSystemCompletion.md](./combatSystemCompletion.md) - 전투 시스템 완성 계획
- [implementationStatus.md](./implementationStatus.md) - 구현 현황

### 관련 코드
- `Assets/Scripts/AI/` - AI 시스템 전체
- `Assets/Scripts/AI/StateHandlers/` - 상태 핸들러
- `Assets/Scripts/AI/AIBehaviorFactory.cs` - 행동 프로필 팩토리
- `Assets/Scripts/AI/AISkillSelector.cs` - (신규) 스킬 선택
- `Assets/Scripts/Common/Tables.cs` - 테이블 정의
- `Assets/Scripts/Factory/EntityFactory.cs` - 엔티티 생성

---

**작성자**: Claude Code
**다음 단계**: 구현 시작 (Task 1: 다중 스킬 선택 시스템)
