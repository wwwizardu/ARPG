# 몬스터 AI 시스템 기획서

**작성일**: 2026-04-16
**현재 상태**: 설계 중
**의존성**: AI 시스템, 전투 시스템, 스킬 시스템

---

## 목차
1. [개요](#1-개요)
2. [3-State 상태 머신](#2-3-state-상태-머신)
3. [성격 태그 시스템](#3-성격-태그-시스템)
4. [태그 상세 정의](#4-태그-상세-정의)
5. [태그 조합 규칙](#5-태그-조합-규칙)
6. [몬스터 예시](#6-몬스터-예시)
7. [Attack 상태 내부 행동](#7-attack-상태-내부-행동)
8. [구현 구조](#8-구현-구조)

---

## 1. 개요

### 설계 방향
기존 BehaviorType별 핸들러 조합 방식을 폐기하고, **3가지 기본 상태 + 성격 태그 가중치** 기반 AI로 전환한다.

### 핵심 원칙
- **상태는 3개**: 대기(Idle), 공격(Attack), 도망(Flee)
- **태그가 성격을 결정**: 태그 조합으로 다양한 AI 패턴을 만듦
- **태그는 중첩 가능**: 하나의 몬스터에 여러 태그 부여 가능
- **데이터 기반**: 태그 배정은 테이블에서 관리

---

## 2. 3-State 상태 머신

### 상태 정의

| 상태 | 설명 |
|------|------|
| **Idle (대기)** | 타겟 없음. 제자리 대기 또는 느리게 배회 |
| **Attack (공격)** | 타겟 추적 + 공격. 거리 조절, 스킬 사용 포함 |
| **Flee (도망)** | 위협으로부터 이탈. 조건 충족 시 재교전 |

### 기본 전환 규칙 (태그 없을 때)

```
         타겟 감지                    타겟 상실
  ┌────────────────→┐          ┌────────────────→┐
  │                 │          │                 │
┌──────┐         ┌──────┐         ┌──────┐
│ Idle │         │Attack│         │ Flee │
└──────┘         └──────┘         └──────┘
  ▲                 │          ▲                 │
  │                 │          │                 │
  └────────────────←┘          └────────────────←┘
       타겟 상실               HP < FleeThreshold
                               재교전 조건 충족
```

### 기본 전환 조건 (태그가 수정하는 값)

| 전환 | 조건 | 기본값 |
|------|------|--------|
| Idle → Attack | 감지 범위 내 타겟 발견 | DetectionRange (AiTable) |
| Attack → Flee | 현재 HP% < **FleeThreshold** | 20% |
| Flee → Attack | 도망 후 **FleeDuration**초 경과 | 2.0초 |
| Attack → Idle | 타겟 상실 (거리/시간) | 기존 Perception 로직 유지 |
| Flee → Idle | 타겟 상실 + 안전 거리 확보 | LoseTargetRange |

---

## 3. 성격 태그 시스템

### 개념

태그는 몬스터의 성격을 나타내며, **상태 전환 조건을 수정**한다.
하나의 몬스터에 **여러 태그를 중첩**할 수 있다.

### 태그가 제어하는 파라미터

| 파라미터 | 설명 | 기본값 |
|----------|------|--------|
| **FleeThreshold** | 도망 시작 HP% | 20% |
| **FleeDuration** | 도망 지속 시간(초) | 2.0 |
| **FleeSpeedMul** | 도망 시 이동 속도 배율 | 1.0 |
| **CanFlee** | 도망 가능 여부 | true |
| **AttackSpeedMul** | 공격 속도 배율 | 1.0 |
| **MoveSpeedMul** | 추격 이동 속도 배율 | 1.0 |
| **KeepDistance** | 공격 시 유지 거리 | 0 (밀착) |
| **LeashRange** | 스폰 지점에서 최대 이동 거리 | 0 (무제한) |
| **CallAlly** | 주변 동료 호출 여부 | false |
| **AllyRequired** | 동료가 있어야 공격하는지 | false |
| **LowHpAttackMul** | 저HP 시 공격력 배율 | 1.0 |
| **LowHpSpeedMul** | 저HP 시 이동 속도 배율 | 1.0 |
| **LowHpThreshold** | "저HP" 기준 % | 30% |
| **PreferSkillSlot** | 첫 공격 시 우선 사용할 스킬 슬롯 | -1 (없음) |

---

## 4. 태그 상세 정의

### 4.1 겁쟁이 (Coward)

**컨셉**: 쉽게 도망가고, 오래 도망가며, 도망 속도가 빠르다.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| FleeThreshold | **50%** | 일찍 도망 |
| FleeDuration | **4.0초** | 오래 도망 |
| FleeSpeedMul | **1.3** | 도망 시 빠름 |

**행동 예시**:
```
Attack 중 HP 50% 이하 → Flee (빠른 속도로 4초간 도망) → Attack 복귀
```

---

### 4.2 분노 (Enraged)

**컨셉**: 절대 도망가지 않는다. HP가 낮을수록 공격적.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| CanFlee | **false** | 도망 불가 |
| LowHpAttackMul | **1.5** | 저HP 시 공격력 150% |
| LowHpSpeedMul | **1.2** | 저HP 시 이동 빠름 |
| LowHpThreshold | **40%** | HP 40% 이하에서 발동 |

**행동 예시**:
```
Attack 중 HP 감소 → 도망 안 함 → HP 40% 이하 시 공격력 1.5배, 이동 1.2배로 광폭화
```

---

### 4.3 신중 (Cautious)

**컨셉**: 거리를 유지하며 싸운다. 위험하면 일찍 후퇴.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| FleeThreshold | **40%** | 일찍 후퇴 |
| FleeDuration | **3.0초** | 적당히 후퇴 |
| KeepDistance | **3.0** | 거리 유지 공격 |

**행동 예시**:
```
Attack 시 타겟과 3.0 거리 유지 → HP 40% 이하 Flee → 3초 후 Attack 복귀
```

---

### 4.4 돌진 (Charger)

**컨셉**: 빠르게 돌진하여 첫 타격. 잘 도망가지 않는다.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| FleeThreshold | **10%** | 거의 안 도망 |
| MoveSpeedMul | **1.5** | 추격 시 빠름 |
| PreferSkillSlot | **1** | 첫 공격 시 슬롯 1(돌진 스킬) 우선 |

**행동 예시**:
```
Idle → 타겟 감지 → Attack (1.5배 속도로 돌진, 슬롯 1 스킬 우선 사용) → 이후 일반 공격
```

---

### 4.5 영역 (Territorial)

**컨셉**: 자기 영역 안에서만 싸운다. 영역 밖으로 나가면 흥미를 잃는다.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| LeashRange | **8.0** | 스폰 지점에서 8.0 이상 벗어나면 복귀 |
| FleeThreshold | **15%** | 영역 내에서는 잘 안 도망 |

**행동 예시**:
```
Attack 중 스폰 지점에서 8.0 이상 벗어남 → Idle로 전환, 스폰 지점으로 복귀
영역 내에서는 HP 15%까지 끈질기게 공격
```

---

### 4.6 군집 (Pack)

**컨셉**: 동료를 부르고, 동료와 함께 공격한다.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| CallAlly | **true** | Attack 진입 시 주변 동료에게 타겟 공유 |
| FleeThreshold | **25%** | 기본보다 약간 높음 |

**행동 예시**:
```
Idle → 타겟 감지 → Attack 진입 시 범위 내 동료 몬스터도 Attack 전환
→ 함께 공격 → HP 25% 이하 Flee
```

**동료 호출 범위**: 자신의 DetectionRange 내 같은 종족 몬스터

---

### 4.7 소심 (Timid)

**컨셉**: 혼자서는 싸우지 못한다. 동료가 있어야 공격.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| AllyRequired | **true** | 근처에 동료가 없으면 Flee |
| FleeThreshold | **60%** | 매우 쉽게 도망 |
| FleeDuration | **5.0초** | 오래 도망 |

**행동 예시**:
```
타겟 감지 → 주변 동료 확인 → 동료 있으면 Attack, 없으면 Flee
Attack 중 동료 전멸 → Flee 전환
HP 60% 이하 → Flee
```

---

### 4.8 광폭 (Berserk)

**컨셉**: HP가 낮아지면 도망 대신 폭주. 더 강해지고 더 빨라진다.

| 파라미터 | 수정값 | 비고 |
|----------|--------|------|
| CanFlee | **false** | 도망 불가 |
| LowHpAttackMul | **2.0** | 저HP 시 공격력 2배 |
| LowHpSpeedMul | **1.5** | 저HP 시 이동 1.5배 |
| LowHpThreshold | **30%** | HP 30% 이하에서 발동 |
| AttackSpeedMul | **1.3** | 평소에도 빠른 공격 |

**행동 예시**:
```
Attack → HP 30% 이하 → 도망 안 함, 공격력 2배, 이동 1.5배로 폭주
사망까지 공격 지속
```

---

### 태그 요약표

| 태그 | FleeThreshold | FleeDuration | CanFlee | 핵심 특징 |
|------|--------------|-------------|---------|-----------|
| **겁쟁이** | 50% | 4.0초 | O | 일찍, 오래, 빠르게 도망 |
| **분노** | - | - | **X** | 도망 불가, 저HP 시 강화 |
| **신중** | 40% | 3.0초 | O | 거리 유지 공격 |
| **돌진** | 10% | 2.0초 | O | 빠른 돌진, 잘 안 도망 |
| **영역** | 15% | - | O | 영역 밖 복귀 |
| **군집** | 25% | 2.0초 | O | 동료 호출 |
| **소심** | 60% | 5.0초 | O | 동료 필요, 쉽게 도망 |
| **광폭** | - | - | **X** | 도망 불가, 저HP 시 대폭 강화 |

---

## 5. 태그 조합 규칙

### 5.1 중첩 방식

태그가 같은 파라미터를 수정할 때:
- **수치 (FleeThreshold, Duration 등)**: 태그 중 **가장 유리한(몬스터에게)** 값 적용
- **bool (CanFlee)**: 하나라도 false면 false (**AND** 연산)
- **배율 (SpeedMul, AttackMul)**: **곱연산** (1.3 x 1.5 = 1.95)

### 5.2 조합 예시

**겁쟁이 + 군집**: "무리 지어 다니지만 겁이 많은 몬스터"
```
FleeThreshold: max(50%, 25%) = 50%  (겁쟁이 우선)
CallAlly: true                       (군집)
FleeDuration: max(4.0, 2.0) = 4.0   (겁쟁이 우선)
→ 동료를 부르지만 HP 50% 이하면 4초간 도망
```

**분노 + 돌진**: "돌진해서 끝까지 싸우는 몬스터"
```
CanFlee: false                       (분노)
MoveSpeedMul: 1.5                    (돌진)
PreferSkillSlot: 1                   (돌진)
LowHpAttackMul: 1.5                 (분노)
→ 빠르게 돌진, 저HP에서 강화, 절대 도망 안 함
```

**소심 + 겁쟁이**: "매우 겁이 많은 몬스터"
```
AllyRequired: true                   (소심)
FleeThreshold: max(60%, 50%) = 60%  (소심 우선)
FleeDuration: max(5.0, 4.0) = 5.0   (소심 우선)
→ 혼자면 도망, 동료 있어도 HP 60%면 도망, 5초간 도망
```

### 5.3 금지 조합

| 조합 | 이유 |
|------|------|
| 분노 + 겁쟁이 | 모순 (도망 불가인데 쉽게 도망?) → CanFlee=false가 우선 |
| 광폭 + 소심 | 모순 → CanFlee=false가 우선, AllyRequired 무시 |

금지는 아니지만 **CanFlee=false가 항상 우선**하여 도망 관련 태그가 무효화된다.

---

## 6. 몬스터 예시

### 6.1 슬라임

| 항목 | 값 |
|------|-----|
| 태그 | **(없음)** |
| 스킬 | 몸통 박치기 (슬롯 0) |
| 이동 속도 | 느림 |
| 행동 | 기본 AI. 감지→추격→공격→HP 20% 이하 도망→2초 후 복귀 |

---

### 6.2 고블린 정찰병

| 항목 | 값 |
|------|-----|
| 태그 | **겁쟁이 + 군집** |
| 스킬 | 베기(슬롯 0), 돌진(슬롯 1) |
| 이동 속도 | 빠름 |
| 행동 | 동료 부르고 함께 공격 → HP 50% 이하면 빠르게 도망 → 4초 후 복귀 |

---

### 6.3 고블린 전사

| 항목 | 값 |
|------|-----|
| 태그 | **돌진 + 분노** |
| 스킬 | 베기(슬롯 0), 돌진 베기(슬롯 1) |
| 이동 속도 | 빠름 |
| 행동 | 돌진으로 접근→끝까지 공격→저HP 시 강화→사망까지 싸움 |

---

### 6.4 해골 궁수

| 항목 | 값 |
|------|-----|
| 태그 | **신중** |
| 스킬 | 화살(슬롯 0) |
| 이동 속도 | 보통 |
| 행동 | 거리 3.0 유지하며 공격 → HP 40% 이하 후퇴 → 3초 후 복귀 |

---

### 6.5 박쥐

| 항목 | 값 |
|------|-----|
| 태그 | **광폭** |
| 스킬 | 물기(슬롯 0) |
| 이동 속도 | 매우 빠름 |
| 행동 | 빠르게 접근→공격→HP 30% 이하에서 폭주(공격력 2배)→사망까지 |

---

### 6.6 독 거미

| 항목 | 값 |
|------|-----|
| 태그 | **영역 + 신중** |
| 스킬 | 독 물기(슬롯 0), 독액 분사(슬롯 1) |
| 이동 속도 | 느림 |
| 행동 | 영역(8.0) 안에서만 싸움 → 거리 유지 공격 → 영역 밖이면 복귀 |

---

### 6.7 늑대

| 항목 | 값 |
|------|-----|
| 태그 | **군집 + 돌진** |
| 스킬 | 물기(슬롯 0) |
| 이동 속도 | 빠름 |
| 행동 | 무리 호출→빠르게 돌진→잘 도망 안 함(10%)→집단 공격 |

---

### 6.8 보스: 고블린 왕

| 항목 | 값 |
|------|-----|
| 태그 | **분노 + 군집** |
| 스킬 | 왕의 일격(슬롯 0), 투척(슬롯 1), 전투 함성(슬롯 2) |
| 이동 속도 | 보통 |
| 행동 | 동료 호출→절대 도망 안 함→저HP 시 강화→다중 스킬 사용 |

---

## 7. Attack 상태 내부 행동

Attack 상태는 내부적으로 **추격(Chase)**, **공격(Strike)**, **거리 조절(Reposition)** 행동을 포함한다.

### 7.1 Attack 상태 내부 루프

```
[Attack 상태 진입]
     │
     ▼
  타겟과 거리 측정
     │
     ├─ 거리 > AttackRange → 추격 (타겟 방향 이동)
     │
     ├─ 거리 < KeepDistance → 거리 조절 (타겟 반대 방향 이동)
     │
     └─ KeepDistance ≤ 거리 ≤ AttackRange → 공격 (스킬 발동)
```

### 7.2 스킬 선택

Attack 상태에서 스킬을 발동할 때:

```
1. PreferSkillSlot >= 0 이고 아직 미사용 → 해당 슬롯 사용 (첫 공격용)
2. 슬롯 2 → 1 → 0 순으로:
   - 스킬 존재?
   - 쿨타임 완료?
   - 타겟이 스킬 사거리(SkillRangeMin~Max) 내?
   → 조건 충족하는 첫 슬롯 사용
3. 모두 쿨타임 중 → 대기 (이동하며 기다림)
```

### 7.3 저HP 강화 (분노/광폭 태그)

Attack 상태에서 매 프레임 HP% 체크:
```
if (현재HP% < LowHpThreshold)
{
    실제 공격력 = 기본 공격력 × LowHpAttackMul
    실제 이동속도 = 기본 이동속도 × LowHpSpeedMul
}
```

---

## 8. 구현 구조

### 8.1 기존 코드와의 관계

| 기존 | 변경 후 |
|------|---------|
| AIState enum 7종 | **3종** (Idle, Attack, Flee) |
| AIBehaviorType enum 7종 | **제거** → 태그 조합으로 대체 |
| AIBehaviorFactory | **제거** → 3-State 시스템으로 대체 |
| 7종 StateHandler | **3종** (IdleHandler, AttackHandler, FleeHandler) |
| AIBehaviorTypeComponent | **AIPersonalityComponent** (태그 + 파라미터) |

### 8.2 새로운 컴포넌트

```csharp
// 성격 태그 (비트 플래그)
[Flags]
public enum AIPersonalityTag
{
    None       = 0,
    Coward     = 1 << 0,   // 겁쟁이
    Enraged    = 1 << 1,   // 분노
    Cautious   = 1 << 2,   // 신중
    Charger    = 1 << 3,   // 돌진
    Territorial= 1 << 4,   // 영역
    Pack       = 1 << 5,   // 군집
    Timid      = 1 << 6,   // 소심
    Berserk    = 1 << 7,   // 광폭
}

// AI 성격 컴포넌트 (태그에서 계산된 최종 파라미터)
public struct AIPersonalityComponent
{
    public AIPersonalityTag Tags;

    // 태그에서 계산된 값
    public float FleeThreshold;     // 도망 HP%
    public float FleeDuration;      // 도망 지속 시간
    public float FleeSpeedMul;      // 도망 속도 배율
    public bool CanFlee;            // 도망 가능 여부
    public float KeepDistance;      // 유지 거리
    public float LeashRange;        // 최대 이동 거리 (0=무제한)
    public float MoveSpeedMul;      // 추격 속도 배율
    public float AttackSpeedMul;    // 공격 속도 배율
    public bool CallAlly;           // 동료 호출
    public bool AllyRequired;       // 동료 필요
    public float LowHpAttackMul;    // 저HP 공격 배율
    public float LowHpSpeedMul;     // 저HP 이동 배율
    public float LowHpThreshold;    // 저HP 기준%
    public int PreferSkillSlot;     // 첫 공격 우선 슬롯 (-1=없음)
}
```

### 8.3 AiTable 변경

```
기존: Id, Name, AiType, BehaviorType, DetectionRange, SkillId1, SkillId2, SkillId3
변경: Id, Name, AiType, PersonalityTags, DetectionRange, SkillId1, SkillId2, SkillId3
                        ~~~~~~~~~~~~~~~
                        BehaviorType 대신 태그 문자열
                        예: "Coward,Pack" 또는 비트 플래그 정수
```

### 8.4 실행 흐름

```
[System_AI_Perception] (0.2초마다)
  → 타겟 감지/상실 (기존과 동일)

[System_AI_Behavior] (매 프레임)
  → AIState 확인
  → 현재 상태 핸들러 실행:
      Idle:   타겟 있으면 → Attack
      Attack: HP 체크 → Flee? / 영역 체크 → Idle? / 스킬 선택 + 이동
      Flee:   타이머 체크 → Attack 복귀? / 타겟 상실 → Idle?
```

---

## 참고 자료

### 관련 기획 문서
- [combatSystem.md](./combatSystem.md) - 전투 시스템
- [implementationStatus.md](./implementationStatus.md) - 구현 현황

### 관련 코드
- `Assets/Scripts/AI/` - AI 시스템 전체
- `Assets/Scripts/Common/System/System_AI_Behavior.cs`
- `Assets/Scripts/Common/System/System_AI_Perception.cs`
- `Assets/Scripts/Factory/EntityFactory.cs`

---

**작성자**: Claude Code
