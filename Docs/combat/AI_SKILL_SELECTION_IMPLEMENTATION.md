# AI 스킬 선택 시스템

AI 엔티티가 여러 스킬을 가중치 기반 랜덤으로 선택하고, 스킬 사거리에 따라 Chase/Attack 상태 전이를 결정하는 시스템.

---

## 데이터 모델

### AiTable ([Tables.cs:534-547](../Assets/Scripts/Common/Tables.cs#L534-L547))

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
    public float KeepDistance;  // Attack에서 이보다 가까우면 Retreat. 0=후퇴 안함
}
```

- **슬롯 수**: 3 (`SkillHelper.AiSkillSlotCount`)
- **가중치 의미**: 랜덤 선택 비율. `0`이면 해당 슬롯은 절대 선택되지 않음
- **왜 AiTable에?** "스킬을 어느 빈도로 쓰는가"는 AI의 행동 설정. 같은 스킬도 AI별 비율을 달리 세팅 가능. 플레이어 전용 스킬은 AiTable에 없어서 자동으로 AI 선택에서 배제됨.
- **KeepDistance**: 보통 Melee는 0(붙어서 공격), Ranged는 7(카이팅) 정도. 교전 사거리보다 크게 잡으면 Attack 진입 즉시 Retreat이 발동되어 핑퐁이 생기므로 주의.

### 구글 시트 (AI 시트, gid=947794841, 범위 `A:L`)

컬럼 배치는 **interleaved** (Id-Weight 쌍이 붙어있음):

| A | B | C | D | E | F | G | H | I | J | K | L |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Id | Name | AiType | BehaviorType | DetectionRange | SkillId1 | SkillWeight1 | SkillId2 | SkillWeight2 | SkillId3 | SkillWeight3 | KeepDistance |

`DownloadTables.ParseAiTable`이 이 순서대로 파싱하므로 컬럼 순서 변경 금지.

---

## 핵심 API

[SkillHelper.cs](../Assets/Scripts/Common/Utility/SkillHelper.cs)

```csharp
public const int AiSkillSlotCount = 3;

// Chase/Attack 전이 판정용 교전 사거리(제곱). Chase와 Attack이 이 값을 공유해야 핑퐁 없음.
// 유효 사거리 = (attacker.HitRadius + SkillRangeMax + target.HitRadius)² — 몸 가장자리 기준.
public static float GetEngagementRangeSqr(int ownerEntityId, int targetEntityId, int slotCount);

// 가중치 기반 스킬 선택. AI 전용. -1이면 발동 가능한 슬롯 없음.
public static int PickFireSkill(int ownerEntityId, int targetEntityId, Vector2 targetPosition, int slotCount);

// 엔티티의 HitRadius 조회 (ColliderComponent 없으면 0). 엔티티 ID -1도 안전.
public static float GetEntityHitRadius(int entityId);

// 플레이어/AI 공통. SkillCommandComponent 생성. 쿨타임/실행중이면 false.
public static bool GetSkillCommandComponent(int slotIndex, int entityId, Vector2 targetPosition, out SkillCommandComponent command);
```

---

## 교전 사거리 (GetEngagementRangeSqr)

### 유효 사거리 공식

```
유효 사거리 = attacker.HitRadius + SkillRangeMax + target.HitRadius
            └─ 자기 몸 반경 ──┘  └─ 팔/무기 reach ─┘  └─ 표적 몸 반경 ─┘
```

- **`SkillRangeMax`의 의미**: "몸 밖으로 뻗어나가는 스킬의 reach" (팔/무기 길이). 큰 몹은 같은 SkillRangeMax 값으로도 자기 몸 너머로 자연스럽게 도달.
- **실제 hit 판정과 일치**: [System_Skill.CheckCircleRangeEntities](../Assets/Scripts/Common/System/System_Skill.cs)도 동일하게 `owner.HitRadius + SkillTargetRange1`을 사용. AI가 fire하는 순간과 hit가 발생하는 순간의 사거리 정의가 동일해야 "맞을 거리에서만 fire"가 보장됨.

### 슬롯 단위 선택

- **1순위**: 쿨타임이 풀려 있는 스킬들의 유효 사거리 중 최대값(제곱)
- **Fallback**: 모두 쿨타임 중이면 전체 존재 스킬의 유효 사거리 중 최대값 → 그 자리에서 대기
- **핑퐁 방지 원리**: Chase→Attack과 Attack→Chase 전이가 동일한 이 값을 사용 → 판정이 뒤집히지 않음
- **정적 `AttackRange` 불필요**: `AIBehaviorTypeComponent.AttackRange`에 의존하지 않음 (현재 dead field)

---

## 가중치 스킬 선택 (PickFireSkill)

### 선택 시점

- 소유자의 `StateComponent.Condition`이 `Normal`일 때만 새 스킬을 선택한다.
- `Start`, `Process`, `End` 동안에는 소유자가 `UseSkill` 상태이므로 추첨과 다음 명령 예약을 하지 않는다. 후딜까지 완료되어 `Normal`로 돌아온 뒤 다음 AI 틱에서 선택한다.
- 이미 `SkillCommandComponent`가 있으면 처리될 때까지 다시 추첨하거나 명령을 덮어쓰지 않는다.
- 가중치는 해당 시점의 쿨타임·사거리 조건을 만족하는 후보끼리 적용한다. 두 후보가 모두 사용 가능할 때만 80:20이 80%:20%가 된다.

### 후보 제외 조건 (`IsFireCandidate`)

1. `AiTable.SkillWeight[slot] <= 0`
2. 스킬 엔티티 존재 안 함
3. `SkillStateComponent.IsRunning == true`
4. `SkillStateComponent.IsCooldownReady == false`
5. `sqrDistance > (attacker.HitRadius + SkillRangeMax + target.HitRadius)²` (유효 사거리 초과)
6. `sqrDistance < SkillRangeMin²` (너무 가까움 — 중심거리 기준, 몸 반경 보정 없음)

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

### Chase ([ChaseStateHandler.cs](../Assets/Scripts/AI/StateHandlers/ChaseStateHandler.cs))
```
distance² ≤ GetEngagementRangeSqr  →  Attack 전이
시야 상실                           →  기본 상태 복귀
그 외                              →  타겟으로 이동
```

### Melee/Ranged Attack ([MeleeAttackStateHandler.cs](../Assets/Scripts/AI/StateHandlers/MeleeAttackStateHandler.cs), [RangedAttackStateHandler.cs](../Assets/Scripts/AI/StateHandlers/RangedAttackStateHandler.cs))
```
distance² > GetEngagementRangeSqr  →  Chase 복귀
KeepDistance 내부 진입              →  Retreat (AiTable.KeepDistance, EntityFactory가 컴포넌트에 복사)
스킬 실행 중(Start/Process/End)       →  다음 스킬 선택·예약 안 함
기존 스킬 명령 처리 대기 중           →  재추첨 안 함
PickFireSkill → 슬롯 선택          →  GetSkillCommandComponent 발동
선택 불가(쿨/사거리 전원 실패)        →  대기 (그 자리에 머무름)
```

**Ranged만의 특이점**: KeepDistance 기반 Retreat은 아래 두 조건을 모두 만족할 때만 허용
- `ATTACK_MIN_TIME`(1.5초): Attack 진입 후 최소 유지 시간. 추격 직후 바로 도망가지 않게 한다.
- `RETREAT_COOLDOWN`(4초): 후퇴를 시작한 시점부터 다음 후퇴까지의 최소 간격. `AIStateComponent.NextRetreatTime`에 기록하며, 쿨타임 중에는 가까이 붙어도 그 자리에서 공격한다. 붙으면 도망가기를 무한 반복하는 카이팅을 막기 위한 값이다.

> ⚠️ **KeepDistance 주의**: 값이 교전 사거리(`attacker.HitRadius + SkillRangeMax + target.HitRadius`)보다 크면 Attack 진입과 동시에 Retreat이 발동되어 **Attack ↔ Retreat ↔ Chase 핑퐁**이 발생함. Melee는 보통 0, Ranged는 교전 사거리보다 의도적으로 작게 잡을 것.

---

## 플레이어 스킬 발동 경로

[System_Input.UseSkill](../Assets/Scripts/Common/System/System_Input.cs) → `SkillHelper.GetSkillCommandComponent` 위임.

AI와 플레이어가 **동일한 `GetSkillCommandComponent`**로 발동 조건(존재/IsRunning/IsCooldownReady/TargetType 세팅)을 공유. 어느 한 쪽에 새 규칙을 추가하면 양쪽에 자동 반영됨.

플레이어는 `PickFireSkill`을 거치지 않음 (가중치 없이 슬롯을 직접 지정).

---

## SkillStateComponent 주요 필드

[SkillStateComponent.cs](../Assets/Scripts/Common/Component/SkillStateComponent.cs)

- `State`: None/Start/Process/End/Completed
- `CooldownRemaining`: 초 단위 남은 쿨타임. `OnSkillComplete` 시점에 `Table.Cooltime * (1 - FinalCooldownReduction/100)`으로 세팅
- `IsRunning`: `State != None`
- `IsCooldownReady`: `CooldownRemaining <= 0`

**쿨타임 시작 시점**: 스킬 Start→Process→End→None 전이 시점(= `OnSkillComplete`). 스킬 실행 중에는 쿨타임이 흐르지 않음.

몬스터 기본 근접 공격(`StrikeMonster`, Skill Id=2)은 `Cooltime=0`, `ProcessTime=0.85`, `EndTime=1.15`를 사용한다. 기존 쿨타임 1초를 후딜에 더했으므로 완료 직후 기본 공격도 다시 선택할 수 있다. EndTime은 공격속도에 따라 보정되며, 후딜 동안 공격 애니메이션의 마지막 프레임을 유지한다. 점프의 쿨타임과 AI 가중치 80:20은 유지한다.

---

## SkillTable 참고 필드

[Tables.cs:261](../Assets/Scripts/Common/Tables.cs#L261) (SkillTable)

- `SkillRangeMax`: **몸 밖으로 뻗는 추가 사거리** (팔/무기 reach). `0` = 무제한/무효. 실제 도달 거리 = `attacker.HitRadius + SkillRangeMax + target.HitRadius`. 같은 0.8 값이라도 거대 몹은 자기 몸 너머까지 자연스럽게 도달.
- `SkillRangeMin`: 너무 가까우면 발사 안 함. 중심거리 기준 (몸 반경 보정 없음). `0` = 제한 없음.
- `SkillTargetRange1`: 실제 hit 검정 사거리 (System_Skill.CheckCircleRangeEntities). 동일하게 `attacker.HitRadius`가 더해져 적용됨. 대개 `SkillRangeMax`와 같은 값으로 두면 됨.
- `Cooltime`: 초 단위. `CooldownReduction` 스탯(0~90% 클램프) 적용됨
- `SkillTargetType`: `Direction`/`Position` — `GetSkillCommandComponent`가 스킬 테이블에서 읽어 command에 세팅

---

## 성능

`GetEngagementRangeSqr`와 `PickFireSkill`은 AI 핸들러 OnUpdate(FixedUpdate 50fps)마다 호출. 슬롯 3 × 엔티티 수 × 2 pass ≈ 수만 TryGetComponent/초. SparseSet O(1)이라 현 규모에서는 병목 아님. 필요 시 캐싱+무효화 패턴으로 전환 가능.

---

## 테이블 정합성 검증

[`DownloadTables.ValidateAiSetup`](../Assets/Scripts/Editor/DownloadTables.cs)이 시트 다운로드 직후 자동 실행. 시트 입력 실수를 디자이너에게 즉시 피드백.

### 검증 항목

| # | 검사 | 심각도 |
|---|---|---|
| 1 | `KeepDistance ≥ attacker.HR + max(SkillRangeMax)` (target.HR=0 보수적 하한) | Warning — Attack↔Retreat 핑퐁 위험 |
| 2 | `SkillRangeMin > SkillRangeMax` (각 SkillTable) | Error — 영원히 발사 불가 |
| 3 | AiTable의 SkillId1/2/3 → SkillTable broken reference | Error |
| 3-1 | Creature/Npc의 AiTableId → AiTable broken reference | Error |
| 4 | SkillId 설정됐는데 모든 SkillWeight=0 | Warning — AI가 스킬 발사 안 함 |
| 4-1 | SkillWeight>0인데 짝 SkillId=0 | Warning — 가중치 무효 |
| 5 | DetectionRange=0 | Warning — 적을 감지 못 함 |

검증 결과는 로그로 출력되고 마지막에 `Errors: N, Warnings: M` 요약. 시트 수정 → 다운로드 → 즉시 로그 확인 흐름.

### Check 1의 보수성

런타임 `target.HitRadius`를 모르기 때문에 가장 작은(0) 타겟을 가정한 하한 검사. 실제 큰 타겟이라면 engagement가 더 커서 안전할 수 있지만, 작은 타겟에선 확실히 핑퐁이 발생하므로 경고 정당함.
