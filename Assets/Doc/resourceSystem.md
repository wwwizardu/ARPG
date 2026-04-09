# 자원 생산-가공-소비 체인 설계 문서

**Last Updated**: 2026-04-09
**관련 문서**: [townDesign.md](./townDesign.md), [implementationStatus.md](./implementationStatus.md)
**의존 시스템**: VillageManager, NpcManager, System_NpcSchedule, System_VillageResource

---

## 목차
1. [설계 방향](#1-설계-방향)
2. [자원 분류](#2-자원-분류)
3. [직업별 생산 규칙](#3-직업별-생산-규칙)
4. [가공 레시피](#4-가공-레시피)
5. [소비 규칙](#5-소비-규칙)
6. [자원 부족 영향](#6-자원-부족-영향)
7. [직업 숙련도](#7-직업-숙련도)
8. [자원 순환 시나리오](#8-자원-순환-시나리오)
9. [ECS 설계](#9-ecs-설계)
10. [밸런싱 파라미터](#10-밸런싱-파라미터)

---

## 1. 설계 방향

### 핵심 원칙
- **1단계 가공 체인**: 원자재 → 가공품 (다단계 체인 없음)
- **마을 공유 저장소**: 모든 자원은 `VillageData.Resources`에 통합 관리
- **NPC 개인 소유 없음**: 생산/소비 모두 마을 저장소를 경유
- **직업 기반 분업**: JobType에 따라 생산하는 자원이 결정됨
- **NPC 활동 연동**: Working 상태인 NPC만 자원을 생산함

### 자원 흐름 개요
```
[생산자 NPC]          [가공자 NPC]           [소비]
Farmer   → Food  ─────────────────────────→ NPC 식사
Hunter   → Food  ─────────────────────────→ NPC 식사
Hunter   → Herb  ──→ Scholar → Medicine ──→ NPC 치료
Miner    → Stone ─────────────────────────→ 건설 자재
Miner    → Iron  ──→ Blacksmith → Tool ───→ 생산 효율↑
Miner    → Copper ─→ Blacksmith → Weapon ─→ Guard 전투력↑
Lumberjack → Wood ─────────────────────────→ 건설 자재
Builder  → (Wood+Stone → Object) ─────────→ 건물 건설
Scholar  → (Herb → Medicine) ─────────────→ NPC 치료
Merchant → Gold (교역) ───────────────────→ 건설 비용
```

---

## 2. 자원 분류

### 원자재 (Raw Resources)

| 자원 | ItemType | 설명 | 주 생산자 |
|------|----------|------|-----------|
| Food (식량) | `ItemType.Food` (5) | NPC 생존 필수. 배고픔 해소 | Farmer, Hunter |
| Wood (목재) | `ItemType.Wood` (6) | 건설, 도구 제작 재료 | Lumberjack* |
| Stone (석재) | `ItemType.Stone` (7) | 건설, 업그레이드 재료 | Miner |
| Copper (구리) | `ItemType.Copper` (8) | 초기 무기/도구 재료 | Miner |
| Iron (철) | `ItemType.Iron` (9) | 중급 무기/도구 재료 | Miner |
| Herb (약초) | `ItemType.Herb` (11) | 약품 제작 재료 | Hunter, Scholar |
| Gold (골드) | `ItemType.Gold` (10) | 화폐. 교역, 건설 비용 | Merchant |

> *Lumberjack는 현재 JobType에 없으므로, Farmer가 겸업하거나 추후 추가 검토

### 가공품 (Processed Resources)

| 자원 | ItemType | 설명 | 가공자 | 원자재 |
|------|----------|------|--------|--------|
| Tool (도구) | `ItemType.Tool`* | 생산 효율 보너스 부여 | Blacksmith | Iron ×2 |
| Weapon (무기) | `ItemType.Weapon`* | Guard 전투력 상승 | Blacksmith | Iron ×1, Copper ×1 |
| Armor (방어구) | `ItemType.Armor`* | Guard 방어력 상승 | Blacksmith | Iron ×2, Copper ×1 |
| Medicine (약품) | `ItemType.Medicine`* | NPC HP 회복, 전염병 치료 | Scholar | Herb ×2 |
| Object (건축자재) | `ItemType.Object` (12) | 건물 건설 최종 재료 | Builder | Wood ×2, Stone ×1 |

> *표시: 현재 ItemType enum에 없는 타입. 추가 필요.

### ItemType 확장 (필요)

```csharp
// GlobalEnum.cs - ItemType에 추가 필요
Tool = 13,
Weapon = 14,
Armor = 15,
Medicine = 16,
```

---

## 3. 직업별 생산 규칙

### 기본 생산량

NPC가 **Working 상태**일 때, 게임 내 **1시간**(= System_NpcSchedule의 Work 1사이클)마다 자원을 생산한다.

| 직업 (JobType) | 생산 자원 | 기본량/사이클 | 비고 |
|----------------|-----------|---------------|------|
| **Farmer** | Food | 3 | 안정적 식량 공급 |
| **Hunter** | Food | 2 | 식량 보조 |
| **Hunter** | Herb | 1 | 약초 부산물 |
| **Miner** | Stone | 2 | 기본 채굴 |
| **Miner** | Iron | 1 | 철광 채굴 |
| **Miner** | Copper | 1 | 구리 채굴 (SkillLevel < 30이면 Copper만) |
| **Blacksmith** | (가공) | — | 원자재 소비 → 가공품 생산 (레시피 참조) |
| **Builder** | (가공) | — | 원자재 소비 → Object 생산 (레시피 참조) |
| **Scholar** | (가공) | — | 원자재 소비 → Medicine 생산 (레시피 참조) |
| **Merchant** | Gold | 2 | 교역 수익 (마을 Stage↑ → 보너스) |
| **Guard** | — | — | 자원 생산 없음. 마을 방어 담당 |
| **Chief** | — | — | 자원 생산 없음. 마을 관리/버프 담당 |

### 생산량 공식

```
실제 생산량 = 기본량 × SkillBonus × ToolBonus

SkillBonus = 1.0 + (SkillLevel / 100)
    SkillLevel  0 → ×1.0
    SkillLevel 50 → ×1.5
    SkillLevel 100 → ×2.0

ToolBonus = 마을에 Tool이 1개 이상 있으면 1.2, 없으면 1.0
    (Tool은 소비되지 않고, 마을 보유량으로 효과 판정)
```

**예시**: SkillLevel 60 Farmer, Tool 보유 마을
```
Food 생산 = 3 × 1.6 × 1.2 = 5.76 → 5 (소수점 버림, 나머지 누적)
```

### 소수점 처리
- 생산량은 float로 누적하고, 정수 부분만 저장소에 추가
- 소수점 잔여분은 `NpcJobComponent`에 `ProductionCarry` (float)로 다음 사이클에 이월

---

## 4. 가공 레시피

가공 직업(Blacksmith, Builder, Scholar)은 원자재를 소비하여 가공품을 생산한다.
마을 저장소에 원자재가 부족하면 **가공 불가** → 대기 또는 다른 활동으로 전환.

### 레시피 테이블

| 가공자 | 레시피 이름 | 입력 | 출력 | 사이클 |
|--------|------------|------|------|--------|
| **Blacksmith** | 도구 제작 | Iron ×2 | Tool ×1 | 1 |
| **Blacksmith** | 무기 제작 | Iron ×1, Copper ×1 | Weapon ×1 | 1 |
| **Blacksmith** | 방어구 제작 | Iron ×2, Copper ×1 | Armor ×1 | 2 |
| **Builder** | 건축자재 제작 | Wood ×2, Stone ×1 | Object ×1 | 1 |
| **Scholar** | 약품 제작 | Herb ×2 | Medicine ×1 | 1 |

### 가공 우선순위

가공 직업의 NPC는 다음 우선순위로 레시피를 선택한다:

**Blacksmith**:
```
1. Tool이 부족한가? (마을 Tool < Population × 0.3) → 도구 제작
2. Weapon이 부족한가? (Guard 수 > Weapon 수) → 무기 제작
3. 기본값 → 도구 제작
```

**Builder**:
```
1. 건설 중인 건물이 있는가? → 건축자재 제작
2. Object 재고 < 10 → 건축자재 제작
3. 기본값 → 건축자재 제작
```

**Scholar**:
```
1. 마을 NPC 중 HP 낮은 NPC가 있는가? → 약품 제작
2. Medicine 재고 < Population × 0.5 → 약품 제작
3. 기본값 → 약품 제작 (여유분 비축)
```

### 가공 SkillLevel 보너스

```
가공 결과량 = 기본 출력 × (1 + SkillLevel / 200)
    SkillLevel  0 → ×1.0 (기본)
    SkillLevel 50 → ×1.25
    SkillLevel 100 → ×1.5
```

가공은 원자재 채취보다 보너스 계수가 낮다 (÷200 vs ÷100). 원자재 공급이 병목이 되도록 의도.

---

## 5. 소비 규칙

### 5.1 NPC 생존 소비 (자동)

NPC는 **Eat 활동** 시 마을 저장소에서 Food를 소비한다.

| 소비 항목 | 소비량 | 조건 | 주기 |
|-----------|--------|------|------|
| Food | 1 | NPC가 Eat 활동 수행 시 | Hunger >= 70일 때 Eat 선택 |

- Eat 1회 = Food 1 소비, Hunger -40 감소
- 마을 Food가 0이면 Eat 불가 → Hunger 계속 상승 → 6장 자원 부족 영향 참조

### 5.2 치료 소비

| 소비 항목 | 소비량 | 조건 |
|-----------|--------|------|
| Medicine | 1 | NPC HP가 50% 이하일 때, 자동 사용 |

- Medicine 사용 → NPC HP를 최대치의 50% 회복
- Medicine 재고 없으면 자연 회복에 의존 (느림)

### 5.3 건설 소비

건물 건설 시 한 번에 소비. 건설 시스템 문서에서 상세 정의 예정.

| 건물 | Object | Wood | Stone | Gold | 기타 |
|------|--------|------|-------|------|------|
| Farm | 3 | 5 | — | 10 | — |
| Forge | 5 | — | 5 | 20 | Iron ×5 |
| Market | 3 | 3 | — | 30 | — |
| Wall (성벽) | — | — | 15 | 10 | — |
| Housing | 3 | 5 | 3 | 15 | — |
| Tavern | 3 | 5 | — | 20 | — |
| Library | 5 | — | 5 | 30 | Herb ×5 |
| Training Ground | 3 | 3 | — | 15 | Weapon ×2 |

### 5.4 마을 유지비 (자동)

마을 단계가 올라갈수록 유지 비용이 발생한다.

| 마을 단계 | Gold 소비/사이클 | 비고 |
|-----------|-----------------|------|
| Settlement | 0 | 유지비 없음 |
| Hamlet | 1 | 기본 유지 |
| Village | 3 | 시설 유지 |
| Town | 5 | 행정 비용 |
| City | 10 | 대규모 유지 |

- 소비 주기: System_VillageResource 업데이트마다 (5초)
- Gold 부족 시 → 시설 효율 저하 (생산량 ×0.8)

---

## 6. 자원 부족 영향

### 6.1 식량 부족 (Food = 0)

| 경과 시간 | 영향 | 수치 |
|-----------|------|------|
| 즉시 | Eat 불가 | Hunger 계속 상승 |
| Hunger >= 80 | Morale 감소 시작 | Morale -5/사이클 |
| Hunger >= 90 | 작업 효율 저하 | 생산량 ×0.5 |
| Hunger = 100 | NPC 이탈 판정 | 매 사이클 10% 확률로 마을 떠남 |

**NPC 이탈 판정 공식**:
```
이탈 확률 = 기본 10% - (Loyalty × 0.1)
    Loyalty 100인 NPC → 0% (절대 이탈 안 함)
    Loyalty  50인 NPC → 5%
    Loyalty   0인 NPC → 10%
```

### 6.2 자원 부족 시 가공 중단

- 가공 직업 NPC는 원자재 부족 시 **가공 불가**
- 가공 불가 → FreeTime(자유 행동)으로 전환
- 장기간 가공 불가 → 해당 NPC Morale -2/사이클

### 6.3 Gold 부족

- 마을 유지비 미납 → 시설 효율 저하 (전체 생산량 ×0.8)
- 2사이클 연속 미납 → 시설 효율 추가 저하 (×0.6)
- 건설 불가 (Gold이 건설 조건)

### 6.4 Medicine 부족

- 부상 NPC 자연 회복만 가능 (느림)
- 전염병 이벤트 발생 시 대처 불가 → 피해 확산

---

## 7. 직업 숙련도 (SkillLevel)

### 7.1 경험치 획득

NPC가 Work 활동을 수행할 때마다 SkillLevel이 상승한다.

```
SkillLevel 상승량 = 1 / (1 + CurrentSkillLevel / 20)
    SkillLevel  0 → +1.0/사이클
    SkillLevel 20 → +0.5/사이클
    SkillLevel 50 → +0.29/사이클
    SkillLevel 80 → +0.2/사이클
```

- 초반에 빠르게 오르고, 후반에 느려지는 감쇠 곡선
- SkillLevel은 float로 내부 관리, 표시는 int
- 최대값: 100

### 7.2 숙련도 효과 요약

| SkillLevel | 생산 보너스 | 가공 보너스 | 비고 |
|------------|------------|------------|------|
| 0 | ×1.0 | ×1.0 | 초보 |
| 25 | ×1.25 | ×1.125 | 견습 |
| 50 | ×1.5 | ×1.25 | 숙련 |
| 75 | ×1.75 | ×1.375 | 전문가 |
| 100 | ×2.0 | ×1.5 | 장인 |

### 7.3 Miner 특수 규칙

| SkillLevel | 채굴 가능 자원 |
|------------|---------------|
| 0~29 | Stone, Copper |
| 30~59 | Stone, Copper, Iron |
| 60+ | Stone, Copper, Iron (Iron 생산량 ×1.5 추가 보너스) |

---

## 8. 자원 순환 시나리오

### 8.1 Settlement (정착지, NPC 3~5명)

```
구성 예시: Farmer ×2, Hunter ×1, Miner ×1, Builder ×1

자원 흐름:
  Farmer ×2 → Food 6/사이클  (NPC 5명 소비 ≈ 약 5 Food/일)
  Hunter ×1 → Food 2, Herb 1
  Miner ×1  → Stone 2, Copper 1
  Builder ×1 → Object 1 (Wood 2 + Stone 1 소비)

문제점: Wood 생산자 없음 → 나무는 플레이어가 공급하거나 Farmer 겸업 필요
목표: 식량 자급자족 달성, 첫 번째 건물(Farm) 건설
```

### 8.2 Hamlet (작은 마을, NPC 8~12명)

```
구성 예시: Farmer ×3, Hunter ×1, Miner ×2, Blacksmith ×1, Builder ×1, Merchant ×1, Chief ×1

자원 흐름:
  Farmer ×3 → Food 9/사이클
  Hunter ×1 → Food 2, Herb 1
  총 Food 생산: 11 → NPC 10명 소비 충분

  Miner ×2 → Stone 4, Iron 2, Copper 2
  Blacksmith ×1 → Tool 1 (Iron 2 소비) → 전체 생산량 1.2배
  Builder ×1 → Object 1
  Merchant ×1 → Gold 2

체인 효과: Blacksmith가 Tool 생산 → 모든 생산 NPC 효율 ×1.2
목표: Forge, Market 건설 → Town 단계 준비
```

### 8.3 Village (마을, NPC 15~20명)

```
구성 예시: Farmer ×5, Hunter ×2, Miner ×3, Blacksmith ×2, Builder ×2, Scholar ×1,
          Merchant ×2, Guard ×2, Chief ×1

자원 흐름:
  Food 생산: 15 + 4 = 19 → NPC 20명 소비 (약간 부족 → Hunter 추가 또는 Farm 건물 보너스)
  Iron 생산: 3 → Blacksmith Tool 1 + Weapon 1 소비
  Herb 생산: 2 → Scholar Medicine 1 생산
  Gold 생산: 4 → 유지비 3 + 저축 1

체인 효과: Tool(생산↑) + Weapon(Guard 전투력↑) + Medicine(HP 회복)
목표: Wall, Library 건설 → 몬스터 습격 방어
```

### 8.4 Town/City (소도시~도시, NPC 25~40+명)

```
자원 흐름이 충분히 안정화되어 잉여 자원 발생
→ Merchant 교역으로 Gold 축적
→ 대규모 건설 프로젝트 (Training Ground, Library)
→ 외부 위협 (몬스터 습격) 규모 증가에 대비

핵심: 자원 균형 유지 + 방어력 확보 + 마을 확장
```

---

## 9. ECS 설계

### 9.1 컴포넌트 변경

#### NpcJobComponent 확장
```csharp
public struct NpcJobComponent
{
    public GlobalEnum.JobType JobType;
    public int SkillLevel;              // 0~100 (표시용 int)
    public float SkillExp;              // 실제 숙련도 (float, 내부 관리)
    public float ProductionCarry;       // 생산량 소수점 이월분
    public int PersonalGoalType;
}
```

#### NpcNeedsComponent 신규
```csharp
public struct NpcNeedsComponent
{
    public float Hunger;    // 0(포만)~100(굶주림). 시간 경과 시 증가
    public float Fatigue;   // 0(활력)~100(탈진). 활동 시 증가
    public float Morale;    // 0(최저)~100(최고). 상황에 따라 변동
}
```

### 9.2 시스템 변경

#### System_VillageResource 확장

현재: 인구수 기반 단순 생산 (Food, Wood, Stone 일괄)

변경 후:
```
1. NpcScheduleComponent 풀 순회
2. Working 상태인 NPC만 필터
3. NpcJobComponent.JobType으로 분기
4. 생산 직업 → 생산량 계산 → VillageManager.ProduceResource()
5. 가공 직업 → 원자재 확인 → 소비 + 가공품 생산
6. 소비 로직 → 마을 유지비 차감
7. SkillLevel 경험치 증가
```

#### System_NpcSchedule 연동

NPC가 **Work 선택** 시:
```
1. CurrentActivity = Working 설정
2. ActivityTimer 시작
3. 게임 내 1시간(= 실시간 N초) 경과 → 생산 1사이클 완료
4. System_VillageResource가 생산 처리
```

NPC가 **Eat 선택** 시:
```
1. CurrentActivity = Eating 설정
2. VillageManager.ConsumeResource(villageId, Food, 1)
3. 성공 → Hunger -40
4. 실패 (Food = 0) → Eat 불가, 다른 활동으로 전환
```

### 9.3 VillageManager 확장

#### 추가 메서드
```csharp
// 마을에 Tool이 있는지 확인 (생산 보너스 판정)
bool HasTool(int villageId)

// 마을 전체 생산 효율 계수 반환 (Gold 부족 시 패널티 등)
float GetProductionEfficiency(int villageId)

// 가공 레시피 실행 (원자재 소비 + 가공품 생산)
bool TryProcessRecipe(int villageId, RecipeType recipe)
```

#### RecipeType enum (신규)
```csharp
public enum RecipeType
{
    CraftTool,      // Iron ×2 → Tool ×1
    CraftWeapon,    // Iron ×1, Copper ×1 → Weapon ×1
    CraftArmor,     // Iron ×2, Copper ×1 → Armor ×1
    BuildObject,    // Wood ×2, Stone ×1 → Object ×1
    BrewMedicine,   // Herb ×2 → Medicine ×1
}
```

### 9.4 데이터 흐름도

```
[매 5초 - System_VillageResource]
    │
    ├─ NpcSchedulePool 순회
    │   ├─ Working 상태? → 생산/가공 처리
    │   │   ├─ 생산 직업 → ProduceResource()
    │   │   └─ 가공 직업 → TryProcessRecipe()
    │   └─ Working 아님 → skip
    │
    ├─ 마을 유지비 차감
    │   └─ ConsumeResource(Gold, 유지비)
    │
    └─ SkillLevel 경험치 증가
        └─ NpcJobComponent.SkillExp += 증가량

[매 1초 - System_NpcSchedule]
    │
    ├─ Needs 평가 (Hunger, Fatigue, Morale 증감)
    ├─ 활동 선택 (우선순위 기반)
    │   ├─ Eat → ConsumeResource(Food, 1)
    │   ├─ Work → Working 상태 진입
    │   ├─ Rest → Fatigue 감소
    │   └─ etc.
    └─ 활동 목적지 설정 → 이동
```

---

## 10. 밸런싱 파라미터

모든 밸런싱 상수는 한 곳에서 관리한다. 추후 데이터 테이블(ScriptableObject 또는 JSON)로 이동 가능.

### 10.1 생산 파라미터

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| `FARMER_FOOD_BASE` | 3 | Farmer 기본 Food 생산량 |
| `HUNTER_FOOD_BASE` | 2 | Hunter 기본 Food 생산량 |
| `HUNTER_HERB_BASE` | 1 | Hunter 기본 Herb 생산량 |
| `MINER_STONE_BASE` | 2 | Miner 기본 Stone 생산량 |
| `MINER_IRON_BASE` | 1 | Miner 기본 Iron 생산량 |
| `MINER_COPPER_BASE` | 1 | Miner 기본 Copper 생산량 |
| `MERCHANT_GOLD_BASE` | 2 | Merchant 기본 Gold 생산량 |
| `TOOL_BONUS` | 1.2 | Tool 보유 시 생산 보너스 배율 |
| `SKILL_PRODUCTION_DIVISOR` | 100 | 생산 스킬보너스 = 1 + SkillLevel/이 값 |
| `SKILL_PROCESSING_DIVISOR` | 200 | 가공 스킬보너스 = 1 + SkillLevel/이 값 |

### 10.2 소비 파라미터

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| `EAT_FOOD_COST` | 1 | Eat 1회 Food 소비량 |
| `EAT_HUNGER_REDUCE` | 40 | Eat 1회 Hunger 감소량 |
| `MEDICINE_HP_RESTORE` | 0.5 | Medicine 사용 시 HP 회복 비율 (최대HP의 50%) |
| `GOLD_EFFICIENCY_PENALTY` | 0.8 | Gold 부족 시 생산 효율 패널티 |

### 10.3 Needs 파라미터

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| `HUNGER_INCREASE_RATE` | 3.0 | 사이클당 Hunger 자연 증가량 |
| `FATIGUE_WORK_RATE` | 5.0 | Work 시 사이클당 Fatigue 증가량 |
| `FATIGUE_REST_RATE` | 15.0 | Rest 시 사이클당 Fatigue 감소량 |
| `MORALE_FOOD_SHORTAGE_RATE` | 5.0 | 식량 부족 시 사이클당 Morale 감소량 |
| `HUNGER_EAT_THRESHOLD` | 70 | Eat 활동 선택 임계값 |
| `FATIGUE_REST_THRESHOLD` | 80 | Rest 활동 선택 임계값 |
| `FATIGUE_WORK_MAX` | 60 | Work 가능 최대 Fatigue |
| `NPC_LEAVE_CHANCE` | 0.1 | Hunger 100일 때 NPC 이탈 기본 확률 |

### 10.4 숙련도 파라미터

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| `SKILL_EXP_BASE` | 1.0 | 기본 경험치 획득량 |
| `SKILL_EXP_DECAY` | 20.0 | 감쇠 계수 (높을수록 후반 느림) |
| `SKILL_MAX` | 100 | 최대 SkillLevel |
| `MINER_IRON_UNLOCK` | 30 | Miner Iron 채굴 해금 SkillLevel |
| `MINER_IRON_BONUS_LEVEL` | 60 | Miner Iron 추가 보너스 해금 |

### 10.5 마을 유지비

| 마을 단계 | Gold/사이클 |
|-----------|------------|
| Settlement | 0 |
| Hamlet | 1 |
| Village | 3 |
| Town | 5 |
| City | 10 |

---

## 부록: 자원 균형 검증

### Settlement 단계 시뮬레이션 (NPC 5명)

```
구성: Farmer ×2, Hunter ×1, Miner ×1, Builder ×1
SkillLevel: 전원 0 (초기)

[생산/사이클]
  Food: Farmer 3×2 + Hunter 2 = 8
  Herb: Hunter 1
  Stone: Miner 2
  Copper: Miner 1
  Wood: 0 (생산자 없음 — 플레이어 공급 필요)

[소비/일 (≈ 8사이클 가정)]
  Food: NPC 5명 × Eat 약 2회/일 = 10 Food

[수지]
  Food: 생산 64(8×8) - 소비 10 = +54 → 충분 (잉여)
  → 밸런스 조정 필요: Hunger 증가 속도 또는 생산량 조절
  → 또는 사이클 = 게임 내 1시간이 아닌 더 긴 간격으로 설정
```

> 실제 밸런싱은 플레이 테스트를 통해 사이클 시간과 생산량을 조절해야 함.
> 위 수치는 **초기 가이드라인**이며, 테스트 후 조정 예정.

---

**문서 관리**:
- 구현 시작 시 ECS 설계(9장) 기준으로 코드 작성
- 밸런싱 파라미터(10장)는 상수 클래스 또는 데이터 테이블로 관리
- 플레이 테스트 후 수치 업데이트

**마지막 업데이트**: 2026-04-09
