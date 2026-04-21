# AI 스킬 선택 시스템

AI 엔티티가 여러 스킬을 가중치 기반 랜덤으로 선택하고, 스킬 사거리에 따라 Chase/Attack 상태 전이를 결정하는 시스템.

---

## 데이터 모델

### AiTable ([Tables.cs:309-321](Assets/Scripts/Common/Tables.cs#L309-L321))

```csharp
public class AiTable : TableBase
{
    public string Name;
    public GE.AiType AiType;
    public AIBehaviorType BehaviorType;
    public float DetectionRange;
    public int SkillId1;      public int SkillWeight1;
    public int SkillId2;      public int SkillWeight2;
    public int SkillId3;      public int SkillWeight3;
}
```

- **슬롯 수**: 3 (`SkillHelper.AiSkillSlotCount`)
- **가중치 의미**: 랜덤 선택 비율. `0`이면 해당 슬롯은 절대 선택되지 않음
- **왜 AiTable에?** "스킬을 어느 빈도로 쓰는가"는 AI의 행동 설정. 같은 스킬도 AI별 비율을 달리 세팅 가능. 플레이어 전용 스킬은 AiTable에 없어서 자동으로 AI 선택에서 배제됨.

### 구글 시트 (AI 시트, gid=947794841, 범위 `A:K`)

컬럼 배치는 **interleaved** (Id-Weight 쌍이 붙어있음):

| A | B | C | D | E | F | G | H | I | J | K |
|---|---|---|---|---|---|---|---|---|---|---|
| Id | Name | AiType | BehaviorType | DetectionRange | SkillId1 | SkillWeight1 | SkillId2 | SkillWeight2 | SkillId3 | SkillWeight3 |

`DownloadTables.ParseAiTable`이 이 순서대로 파싱하므로 컬럼 순서 변경 금지.

---

## 핵심 API

[SkillHelper.cs](Assets/Scripts/Common/Utility/SkillHelper.cs)

```csharp
public const int AiSkillSlotCount = 3;

// Chase/Attack 전이 판정용 교전 사거리(제곱). Chase와 Attack이 이 값을 공유해야 핑퐁 없음.
public static float GetEngagementRangeSqr(int ownerEntityId, int slotCount);

// 가중치 기반 스킬 선택. AI 전용. -1이면 발동 가능한 슬롯 없음.
public static int PickFireSkill(int ownerEntityId, Vector2 targetPosition, int slotCount);

// 플레이어/AI 공통. SkillCommandComponent 생성. 쿨타임/실행중이면 false.
public static bool GetSkillCommandComponent(int slotIndex, int entityId, Vector2 targetPosition, out SkillCommandComponent command);
```

---

## 교전 사거리 (GetEngagementRangeSqr)

- **1순위**: 쿨타임이 풀려 있는 스킬들의 `SkillRangeMax` 중 최대값(제곱)
- **Fallback**: 모두 쿨타임 중이면 전체 존재 스킬의 `SkillRangeMax` 중 최대값 → 그 자리에서 대기
- **핑퐁 방지 원리**: Chase→Attack과 Attack→Chase 전이가 동일한 이 값을 사용 → 판정이 뒤집히지 않음
- **정적 `AttackRange` 불필요**: `AIBehaviorTypeComponent.AttackRange`에 의존하지 않음

---

## 가중치 스킬 선택 (PickFireSkill)

### 후보 제외 조건 (`IsFireCandidate`)

1. `AiTable.SkillWeight[slot] <= 0`
2. 스킬 엔티티 존재 안 함
3. `SkillStateComponent.IsRunning == true`
4. `SkillStateComponent.IsCooldownReady == false`
5. `sqrDistance > SkillRangeMax²` (사거리 초과)
6. `sqrDistance < SkillRangeMin²` (너무 가까움)

### 알고리즘 (2-pass)

```
1. 후보 슬롯들의 SkillWeight 총합(totalWeight) 계산
2. roll = Random.Range(0, totalWeight)
3. 누적 가중치(acc)가 roll을 넘어서는 시점의 슬롯 반환
```

**예**: 슬롯0(W=70), 슬롯1(W=20), 슬롯2(W=10) 모두 발동 가능 시

| roll 범위 | 선택 슬롯 |
|---|---|
| 0 ~ 69 | 슬롯 0 |
| 70 ~ 89 | 슬롯 1 |
| 90 ~ 99 | 슬롯 2 |

---

## 상태 전이 흐름

### Chase ([ChaseStateHandler.cs](Assets/Scripts/AI/StateHandlers/ChaseStateHandler.cs))
```
distance² ≤ GetEngagementRangeSqr  →  Attack 전이
시야 상실                           →  기본 상태 복귀
그 외                              →  타겟으로 이동
```

### Melee/Ranged Attack ([MeleeAttackStateHandler.cs](Assets/Scripts/AI/StateHandlers/MeleeAttackStateHandler.cs), [RangedAttackStateHandler.cs](Assets/Scripts/AI/StateHandlers/RangedAttackStateHandler.cs))
```
distance² > GetEngagementRangeSqr  →  Chase 복귀
KeepDistance 내부 진입              →  Retreat (기존 AIBehaviorTypeComponent.KeepDistance)
PickFireSkill → 슬롯 선택          →  GetSkillCommandComponent 발동
선택 불가(쿨/사거리 전원 실패)        →  대기 (그 자리에 머무름)
```

**Ranged만의 특이점**: `ATTACK_MIN_TIME` 경과 후에만 KeepDistance 기반 Retreat 허용

---

## 플레이어 스킬 발동 경로

[System_Input.UseSkill](Assets/Scripts/Common/System/System_Input.cs) → `SkillHelper.GetSkillCommandComponent` 위임.

AI와 플레이어가 **동일한 `GetSkillCommandComponent`**로 발동 조건(존재/IsRunning/IsCooldownReady/TargetType 세팅)을 공유. 어느 한 쪽에 새 규칙을 추가하면 양쪽에 자동 반영됨.

플레이어는 `PickFireSkill`을 거치지 않음 (가중치 없이 슬롯을 직접 지정).

---

## SkillStateComponent 주요 필드

[SkillStateComponent.cs](Assets/Scripts/Common/Component/SkillStateComponent.cs)

- `State`: None/Start/Process/End/Completed
- `CooldownRemaining`: 초 단위 남은 쿨타임. `OnSkillComplete` 시점에 `Table.Cooltime * (1 - FinalCooldownReduction/100)`으로 세팅
- `IsRunning`: `State != None`
- `IsCooldownReady`: `CooldownRemaining <= 0`

**쿨타임 시작 시점**: 스킬 Start→Process→End→None 전이 시점(= `OnSkillComplete`). 스킬 실행 중에는 쿨타임이 흐르지 않음.

---

## SkillTable 참고 필드

[Tables.cs:261](Assets/Scripts/Common/Tables.cs#L261) (SkillTable)

- `SkillRangeMin` / `SkillRangeMax`: 사거리. `0` = 무제한/무효
- `Cooltime`: 초 단위. `CooldownReduction` 스탯(0~90% 클램프) 적용됨
- `SkillTargetType`: `SingleEntity`/`Direction`/`Position` — `GetSkillCommandComponent`가 스킬 테이블에서 읽어 command에 세팅

---

## 성능

`GetEngagementRangeSqr`와 `PickFireSkill`은 AI 핸들러 OnUpdate(FixedUpdate 50fps)마다 호출. 슬롯 3 × 엔티티 수 × 2 pass ≈ 수만 TryGetComponent/초. SparseSet O(1)이라 현 규모에서는 병목 아님. 필요 시 캐싱+무효화 패턴으로 전환 가능.
