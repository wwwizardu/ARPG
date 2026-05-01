# Inn 기반 NPC 고용 시스템 — 설계 문서

> 상위 문서: [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md)
> 선행: [PHASE_C_DESIGN.md](PHASE_C_DESIGN.md) ✅ · [PHASE_D_DESIGN.md](PHASE_D_DESIGN.md) ✅
>
> **목표**: NPC가 마을에 즉시 종속되는 현재 자연 이민 시스템을, **여관(Inn)이라는 입국 게이트 + 플레이어 능동 고용**으로 교체한다. 마을이 클수록 더 많은 손님이, 더 빠르게 도착하고, 여관 규모가 곧 노동력 풀이 된다.

---

## 1. 범위

**이 문서가 설계하는 것**:
1. **여관 = 입국장(Hiring Pool)**: 이민자는 마을원이 되지 않고 Inn에 "방문자(Visitor)" 상태로 머무름
2. **플레이어 고용 액션**: Inn UI에서 방문자 목록 → 고용 결정 → 마을원으로 승격
3. **시작 인구 1명 (선고용)**: 첫 마을은 고용 절차 없이 바로 진행하는 1명으로 시작
4. **여관 용량 = NPC 풀 크기**: Inn_Bed 갯수가 동시에 머물 수 있는 방문자 수 결정
5. **마을 확장 가속**: Stage가 오를수록 이민 주기 단축 + 확률 상승 + 용량 증가

**제외**:
- NPC가 Inn까지 실제로 걸어오는 이동 시뮬레이션 (스폰 위치만 처리)
- 고용된 NPC의 직업 자동 배치 — 기존 [System_VillageJobAssignment](../Assets/Scripts/Common/System/System_VillageJobAssignment.cs)에 위임
- 거절/대기 시간 만료된 방문자 모델링 (단순히 새 손님으로 교체)
- 명성/평판이 고용 비용에 미치는 영향 → Phase F+

> **한 줄 범위**: "이민자가 와도 바로 마을원이 되지 않고, 여관에 머문다. 플레이어가 골드를 지불해야 마을원이 된다."

---

## 2. 핵심 설계 결정

### 2.1 시작 인구 = 1명 (선고용 기본 거주자)

**현재**: `VillageTable.DefaultNpcList = "3001,3001,3001"` (3명, [Tables.cs:364-376](../Assets/Scripts/Common/Tables.cs#L364))

**변경**: `DefaultNpcList = "3001"` (1명).

이 1명은 **이민자가 아니라 마을의 시작 거주자**이므로 고용 절차 없이 즉시 `VillageId` 설정 + `RegisterNpcToVillage` 호출. 즉 [System_VillagePopulation.cs:243-253](../Assets/Scripts/Common/System/System_VillagePopulation.cs#L243)의 `SpawnDefaultNpcsForVillage` 경로는 현행 유지, **데이터만 1명으로 줄임**.

### 2.2 이민자 ≠ 마을원 — `NpcStatus` 도입

`NpcSaveData`에 상태 필드 1개 추가 ([NpcSaveData.cs](../Assets/Scripts/Npc/NpcSaveData.cs)):

```csharp
public enum NpcStatus
{
    Resident = 0,       // 마을원 (기본값, 기존 동작 유지)
    InnVisitor = 1,     // 여관 방문자 (고용 가능)
}

public class NpcSaveData
{
    // ... 기존 필드 ...
    public NpcStatus Status = NpcStatus.Resident;  // 신규
    public int StayingAtVillageId;                  // 신규: Visitor가 머무는 마을 (Resident면 무시)
    public float ArrivedGameTime;                   // 신규: 방문자 도착 시각 (UI 표시용)
}
```

**왜 별도 필드인가**:
- `VillageId == 0`을 "미고용"으로 쓰면 ID가 0인 마을과 충돌하고 의미가 모호
- `Status` 명시 + `StayingAtVillageId`로 "어느 여관에 있는지" 분명히 표현
- 마을원으로 승격 시 `Status = Resident; VillageId = StayingAtVillageId; StayingAtVillageId = 0`

**기존 호환**: 새 필드는 기본값(Resident, 0, 0)이라 기존 세이브는 그대로 마을원으로 로드됨.

### 2.3 여관 용량 — 고정 2명 (향후 업그레이드 확장)

```
InnCapacity = 2  (상수)
```

여관에 동시에 머물 수 있는 방문자는 **고정 2명**. 추가 Inn_Bed를 더 짓거나 세트를 완성해도 현 단계에서는 슬롯이 늘지 않는다.

- **이민 발생 전제조건**: Inn 세트(`Inn_Bed | Inn_Hearth`) 완성. Phase D에서 이미 정의된 `ObjectSetCatalog.Inn` ([ObjectSetCatalog.cs:32](../Assets/Scripts/Village/ObjectSetCatalog.cs#L32)) 판정을 그대로 사용. 여관이 없으면 이민도, Inn UI 접근도 없음.
- **만석 동작**: 2명이 차면 다음 이민 틱은 무시. 플레이어가 한 명 고용해야 새 손님이 옴 (§2.5).
- **시작 1명의 첫 빌드**: 자동 건설 우선순위([System_VillageNeedsEvaluation.cs](../Assets/Scripts/Common/System/System_VillageNeedsEvaluation.cs))에 Inn 세트(Bed + Hearth)를 식량/잠자리 다음으로 상위 배치. 시작 1명이 짓고 나면 이민 풀이 열림.

**왜 고정값인가**:
- Inn_Bed 갯수 기반은 직관적이지만 Phase D 세트 시스템과 별도의 "오브젝트 카운트" 헬퍼가 필요해 코드 복잡도 증가
- 초기 단계에서는 슬롯 2개 + 빠른 회전(고용)으로 충분히 인구가 늘어남 (§5 시뮬레이션 참고)
- 후에 **여관 업그레이드 시스템** 도입 시 `Inn Lv.1 = 2명, Lv.2 = 4명, Lv.3 = 6명` 식으로 확장. 그 때는 `InnCapacity` 함수가 `village.InnLevel`을 참조하도록 한 곳만 바꾸면 됨.

**향후 업그레이드 확장 메모** (§8 미결정에 상세):
- `VillageData.InnLevel` 필드 신설
- 업그레이드 비용/조건은 별도 설계 (자원 + Stage 게이트)
- `GetInnCapacity` 함수 시그니처는 미리 `(int villageId)`로 두어 미래 확장 시 호출부 변경 0

### 2.4 이민 가속 — Stage별 주기 단축 + 확률 상승

**현재** ([System_VillagePopulation.cs:22, 57-58](../Assets/Scripts/Common/System/System_VillagePopulation.cs#L22)):
- 8 게임시간 고정 주기
- Settlement 10% / Hamlet 15% / Village 20% / Town 25% / City 30%

**변경 후**:

| Stage | 체크 주기 | 도착 확률 | 기대 도착 간격 |
|-------|-----------|-----------|----------------|
| Settlement | **6h** | **40%** | ~15h |
| Hamlet | 5h | 50% | ~10h |
| Village | 4h | 60% | ~6.7h |
| Town | 3h | 75% | ~4h |
| City | 2h | 90% | ~2.2h |

**산정 근거**:
- "확률을 크게 올린다"는 사용자 요구
- 시작 1명 → 다음 단계까지 Settlement에서 평균 1~2회 도착이면 Pop 2~3 도달 가능 (게임시간 ~30h)
- 만석이면 어차피 무시되므로 후반 90%도 폭주하지 않음 (자연스러운 캡)
- 확률·주기 두 축 모두 수정해 "수치는 작게 늘렸지만 효과가 크다"는 인상 강화

**상수 위치**: [System_VillagePopulation.cs](../Assets/Scripts/Common/System/System_VillagePopulation.cs)에 Stage 인덱스 배열로:

```csharp
private static readonly float[] CHECK_HOURS_BY_STAGE  = { 6f, 5f, 4f, 3f, 2f };
private static readonly float[] ARRIVE_CHANCE_BY_STAGE = { 0.40f, 0.50f, 0.60f, 0.75f, 0.90f };
```

### 2.5 만석 시 동작 — 단순 무시

도착 체크 시 `(현재 Inn 방문자 수) >= InnCapacity`면 그 틱은 스폰 안 함. 별도 큐/대기열 없음.

**왜 큐 없이**:
- 큐는 UI/세이브 복잡도만 키우고 플레이어가 보지 못하는 가상 NPC가 됨
- 만석은 "고용해서 비우라"는 자연스러운 압력으로 충분
- 큐 도입은 PVE 콘텐츠 압박이 생길 때(Phase F+) 재검토

### 2.6 식량/잠자리 게이트 — 단순화

**현재**: 이민 시 빈 침대 + 식량(Pop × 5) 검사 ([System_VillagePopulation.cs:67-72](../Assets/Scripts/Common/System/System_VillagePopulation.cs#L67))

**변경 후**:
- "빈 침대" 검사 → "Inn 빈자리" 검사로 교체 (방문자는 마을 침대를 차지하지 않음)
- 식량 검사: **Resident 인구 × 5만** 계산 (방문자는 Inn에서 자체 보급한다고 의제). 단, **고용 시점에 식량 5 일시 차감** — 마을 가정에 새 인원이 들어왔다는 의미

### 2.7 고용 비용 — Stage Tier × 골드 기본 + 직업 가산

```
HireCost(npc, village) = BaseCost[Stage] + JobBonusCost[npc.JobType]
```

| Stage | BaseCost (Gold) |
|-------|-----------------|
| Settlement | 0 (튜토리얼적 무료) |
| Hamlet | 50 |
| Village | 150 |
| Town | 400 |
| City | 1000 |

`JobBonusCost`는 `JobBonusTable`(Phase D)에 컬럼 1개 추가하거나 단일 사전으로. 노동(Worker) 0, 농부 +20, 상인 +50, 대장장이 +100 등.

> 비용 곡선의 의도: Settlement는 "고용이 무엇인지 가르치는 단계"라 무료. Hamlet부터 가벼운 골드 압력, Town 이후 본격적인 의사결정.

### 2.8 방문자의 직업 — 도착 시 결정, 고용 후 확정

**도착 시점**:
- `VillageTable.DefaultNpcIds`에서 무작위 선택 (현재 동작 유지)
- 이 NpcTableId가 가진 기존 `NpcTable.JobType` 필드를 "희망 직업"으로 노출 (별도 컬럼 신설 X)
- 플레이어는 UI에서 "농부 NPC가 와있다"는 정보를 보고 결정

**고용 시점**:
- `NpcSaveData.JobType = NpcTable.JobType`으로 즉시 확정
- 이후 [System_VillageJobAssignment](../Assets/Scripts/Common/System/System_VillageJobAssignment.cs)가 적합 오브젝트 옆에 배치

**왜 도착 시 결정**:
- 고용을 결정하는 가치가 "이 직업이 지금 우리 마을에 필요한가"로 명확해짐
- 같은 NPC가 매번 다른 직업으로 보이는 것보다 일관성 있음

### 2.9 Tier 승격 조건 — 시작 1명에 맞춰 조정

[System_VillageTierProgression.cs:68](../Assets/Scripts/Common/System/System_VillageTierProgression.cs#L68) 현재:
```
Settlement → Hamlet: Pop >= 3 && Bed >= 2 && Food >= 30 && age >= 24h
```

**변경 후**: Pop 조건은 유지(3명). 시작 1명이지만 Inn 가속으로 24h 안에 도달 가능. 단 안전장치로:

```
Settlement → Hamlet: Pop >= 3 && Bed >= 2 && Food >= 30 && age >= 24h
                     && HasObjectSet(Inn)   // 여관이 시스템의 전제조건임을 강조
```

`HasObjectSet(ObjectSetType.Inn)` 판정은 [VillageManager](../Assets/Scripts/Village/VillageManager.cs)에 Phase D에서 이미 도입된 헬퍼라 추가 코드 없음.

이후 단계 조건은 그대로.

> 이 절은 의존성이 큰 변경이므로 §6 단계별 마이그레이션의 마지막에 적용한다.

### 2.10 방문자 체류 제한 — 일정 시간 후 자동 이탈

방문자가 영구적으로 머물면 만석 상태가 풀리지 않고, 플레이어가 자리를 비운 사이 새 후보를 못 받아 풀이 정체된다. **고용 결정의 압력**과 **슬롯 회전**을 만들기 위해 체류 시간을 둔다.

```
StayDuration = 24 게임시간 (Stage 무관, 1차 고정)
```

- 도착 시각은 `NpcSaveData.ArrivedGameTime` (§2.2)에 기록
- `now - ArrivedGameTime >= StayDuration` 이면 자동 이탈 → Visitor 인덱스에서 제거 + Entity 디스폰
- 이탈은 조용히 처리 (페널티/평판 영향 없음 — Phase F에서 평판 시스템과 함께 검토)
- 같은 NpcTableId의 NPC가 다음 이민 틱에 다시 올 수 있음 (재방문 차단 X)

**왜 Stage 무관 단순값**:
- Stage별 차등(예: 도시일수록 짧게)은 직관적이지만 1차 구현엔 과한 복잡도
- 24h = 실시간 ~12분 (게임시간 배율 2x 가정) → 한 세션 안에 결정 압력은 있고, 잠깐 자리 비워도 다 놓치진 않는 균형
- 후속 튜닝 항목으로 §8에 남김

**체크 주기**:
- 별도 시스템 없이 `System_VillagePopulation` 이민 틱(Stage별 6h~2h) 안에 함께 처리 — 어차피 다음 도착을 받으려면 만석 여부를 봐야 하므로 같은 루프에서 만료 검사
- 즉 만료는 정확히 24h 시점이 아니라 **다음 이민 틱**에 일괄 처리됨 (~6h 오차). 게임플레이에 영향 미미

**UI 표시**:
- Inn UI 방문자 행에 **남은 시간** 표시 ("12h" 또는 진행 바)
- 잔여 6h 이하일 때 시각 강조 (색상/아이콘) — "곧 떠납니다" 신호

---

## 3. 데이터 흐름

```
[현재]
이민 틱 → SpawnNewNpc(villageId) → RegisterNpcToVillage → 끝 (즉시 마을원)

[변경 후]
이민 틱 → ① 만료 정리: Visitor 중 ArrivedGameTime+24h 경과한 자 디스폰
       → ② 여관 빈자리 확인 → SpawnVisitorNpc(villageId)
       → NpcSaveData.Status=InnVisitor, StayingAtVillageId=v.Id, ArrivedGameTime=now
       → 마을 등록 안 함, Visitor 인덱스에만 등록

플레이어 → Inn UI 진입 → 방문자 목록 + 남은 시간 표시
       → 고용 버튼 클릭 → HireVisitor(entityId)
       → Status=Resident, VillageId=StayingAtVillageId, 골드/식량 차감
       → RegisterNpcToVillage 이제 호출
```

---

## 4. 신규/변경 시스템·매니저

### 4.1 `NpcManager` 확장

[NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs)에 추가:

```csharp
// 방문자 인덱스 (마을당 EntityId 리스트)
Dictionary<int, List<int>> _innVisitorsByVillageId;

void SpawnVisitorNpc(int npcTableId, Vector2 spawnPos, int villageId);
List<int> GetInnVisitors(int villageId);
int GetInnVisitorCount(int villageId);
bool HireVisitor(int entityId, out string failReason);
int GetInnCapacity(int villageId);            // §2.3 — 현재 항상 2 반환, 향후 Inn Lv 참조
void EvictExpiredVisitors(int villageId);     // §2.10 — 24h 경과한 Visitor 정리
float GetVisitorRemainingHours(int entityId); // UI 표시용
```

**핵심 규칙**:
- `SpawnVisitorNpc`는 `RegisterNpcToVillage`를 **호출하지 않음**, `ArrivedGameTime = now` 기록
- `HireVisitor`만이 Visitor → Resident 전이 + RegisterNpcToVillage 호출
- `EvictExpiredVisitors`는 디스폰 + 인덱스 제거 + Debug.Log만, 페널티 없음
- 만석/식량 부족 등 실패는 `failReason`으로 반환, UI가 토스트 표시

### 4.2 `System_VillagePopulation` 수정

- §2.4 표대로 Stage별 주기/확률 분기
- 이민 틱 진입 시 **먼저 만료 정리**:
  ```csharp
  AR.s.Npc.EvictExpiredVisitors(v.VillageId);  // §2.10 — 24h 경과 디스폰
  ```
- 잠자리 검사를 Inn 빈자리 검사로 교체:
  ```csharp
  int capacity = AR.s.Npc.GetInnCapacity(v.VillageId);
  int visitors = AR.s.Npc.GetInnVisitorCount(v.VillageId);
  if (visitors >= capacity) continue;
  ```
- `SpawnNewNpc(...)` 호출을 `SpawnVisitorNpc(...)`로 교체
- 식량 게이트는 Resident 기준만 검사

### 4.3 `UIInn` 재구성 — 방문자 중심 레이아웃

현재 [UIInn.cs](../Assets/Scripts/UI/UIInn.cs)는 휴식이 화면 중앙의 메인 액션이지만, 새 시스템에서는 **고용이 핵심 의사결정**이고 휴식은 부가 기능. 탭 구조 대신 방문자 목록을 메인에 두고 휴식/세이브는 하단 보조 버튼 영역으로 정리한다.

```
[Inn UI]
┌────────────────────────────────────────────────────────────┐
│ 여관                                                  [×]   │
│ 세트 활성 — 손님 1/2                                        │
├────────────────────────────────────────────────────────────┤
│ Visitor list (메인)                                        │
│ ┌────────────────────────────────────────────────────────┐ │
│ │ [초상화] Bram                              ⏱ 18h 남음   │ │
│ │          희망 직업: 농부  ·  숙련도 ★★☆                │ │
│ │          "고향에선 보리농사를 지었습니다."              │ │
│ │          ─────────────────────────────  [Hire 0G]      │ │
│ └────────────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────────────┐ │
│ │ (빈 슬롯 — 곧 새 손님이 도착합니다)                     │ │
│ └────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────┤
│ 부가 액션                                                   │
│  [Rest +6h — 30G]   [Save]                                  │
└────────────────────────────────────────────────────────────┘
```

**레이아웃 원칙**:
- **상단 헤더**: 세트 상태 + 슬롯 카운트 (`GetInnVisitorCount` / `GetInnCapacity`)
- **메인 영역(70% 높이)**: 방문자 카드 리스트. 빈 슬롯도 회색 placeholder로 표시해 "여관에 자리가 있다"를 시각화
- **하단 보조 영역(30% 높이)**: 기존 Rest/Save 버튼을 가로로 나란히. 시각적 위계로 "이건 부가 기능"임을 분명히

**방문자 카드 표시 정보** — 플레이어가 "이 사람을 고용할까"를 결정하는 데 필요한 모든 정보를 한 카드 안에:

| 영역 | 표시 내용 | 데이터 출처 |
|------|-----------|-------------|
| 초상화 | 색상 placeholder (1차 구현) | — |
| 이름 | NpcTable.Name | `NpcTable` |
| **남은 시간** | `12h 남음` / 6h 이하 시 빨강+⚠ 아이콘 | `GetVisitorRemainingHours(entityId)` |
| 희망 직업 | `희망 직업: 농부` 등 한글 라벨 | `NpcTable.JobType` (기존 필드) |
| 숙련도 | `★★☆` (3점 만점, SkillLevel 0~100을 0~3 별로 매핑) | `NpcSaveData.SkillLevel` |
| 한 줄 소개 | 1~2줄 flavor text — 결정 보조 + 마을 분위기 | `NpcSaveData.Description` (생성 시 풀에서 랜덤) |
| Hire 버튼 | `[Hire 50G]` 또는 비활성 | `HireCost` 계산 |

**남은 시간 시각화 규칙** (§2.10 강조 정책 구체화):
- ≥ 12h: 일반 회색 텍스트
- 6h ~ 12h: 노랑/주황 강조
- < 6h: 빨강 + ⚠ 아이콘 + (선택) 살짝 깜빡임 — 즉시 결정 압력
- 표시 단위: 정수 시간 (h). 실시간 1초 단위로 줄어드는 게 아니라 UI Refresh 또는 게임시간 1h 변할 때 갱신

**버튼 활성/비활성** + 비활성 사유 표시:
- Hire 버튼이 비활성일 때 버튼 위 또는 아래에 작은 문구로 사유 노출:
  - 골드 부족 → `골드 부족 (보유 0G / 필요 50G)`
  - 식량 부족 → `식량 부족 (마을 자원 확인)`
  - 그 외 실패는 `HireVisitor`의 `failReason` 그대로
- Rest: 기존과 동일 — 세트 미완성 시 비활성

**세트 미완성 상태**:
- 메인 영역은 빈 슬롯 2개 + "여관 세트(Bed + Hearth)를 완성하면 손님이 옵니다" 안내문
- Rest 버튼도 비활성 (현행 유지)

**UI 갱신 타이밍**:
- Inn UI Open / Tab Visible 시점에 1회 전체 갱신
- 게임시간 변화 콜백(또는 단순히 1초 폴링)으로 남은 시간 텍스트 + 색상 갱신
- Visitor 도착/만료/고용 이벤트 시 리스트 재구성

**UXML 구조 변경 요약**:
- 기존 `rest-btn`, `save-btn`, `status-text`, `cost-text` ID는 하단 보조 영역에 그대로 유지
- 메인 영역에 `VisualElement#visitor-list` 신규 + 행 템플릿 (`VisitorCard.uxml` 권장) 신규
- 행 템플릿 내부 ID 예시: `portrait-img`, `name-text`, `remaining-text`, `job-text`, `skill-stars`, `desc-text`, `hire-btn`, `hire-fail-reason`
- 헤더에 `slot-count-text` 신규

### 4.4 데이터 추가

- `NpcSaveData`: §2.2의 3개 필드 + `Description` (인스턴스별 flavor text)
- `NpcTable` (현재 [Tables.cs:133](../Assets/Scripts/Common/Tables.cs#L133)):
  - `JobType` ✅ 이미 존재 — Visitor "희망 직업"으로 그대로 사용
  - **소개/초상화는 NpcTable에 두지 않음** — 같은 NpcTableId라도 인스턴스마다 다른 소개를 보여주는 게 마을 분위기에 좋고, 시트 컬럼도 늘지 않음
- `JobBonusTable`(Phase D): `HireCostBonus` 컬럼 1개 (없으면 코드 사전)
- `VillageTable.DefaultNpcList`: `"3001,3001,3001"` → `"3001"`

**소개(Description) 생성 규칙**:
- NPC 생성 시점(`SpawnNewNpc`/`SpawnVisitorNpc`/`RegisterNpcsFromMapFile`)에 `NpcManager._descriptionPool`에서 직업별 풀의 한 줄을 랜덤 선택, `NpcSaveData.Description`에 박는다
- 이후 인스턴스 수명 동안 고정 — 세이브/로드 시에도 보존
- 풀에 없는 직업은 `JobType.None` 풀로 fallback
- 새 문구 추가: `NpcManager._descriptionPool` 사전 한 곳만 수정

**초상화(PortraitSprite)**:
- 1차 구현은 색상 placeholder만 (실 자산 없음)
- 추후 도입 시 `NpcSaveData.PortraitSprite` 필드 추가 + 풀 기반 선택 (Description과 동일 패턴)

### 4.5 세이브/로드

- 새 필드는 `JsonProperty` 기본 직렬화로 자동 저장
- 로드 시 `Status == InnVisitor`인 NPC는 `_innVisitorsByVillageId`에 재인덱싱 (`NpcManager.OnLoaded` 또는 `LoadNpcsFromSaveData` 단계)
- 기존 세이브는 모두 Resident로 로드되어 호환

---

## 5. 밸런스 시뮬레이션 (Settlement 단계)

가정: 시작 Pop 1, Inn 세트(Bed+Hearth) 즉시 건설 (8h 소요), 식량 충분, Capacity = 2.

| 게임시간 | 이벤트 | Pop | Visitor |
|----------|--------|-----|---------|
| 0h | 시작 | 1 | 0 |
| 8h | Inn 세트 완공 | 1 | 0 |
| 14h | 첫 도착 체크 (40%) → 적중 | 1 | 1 |
| 18h | 플레이어 고용 (무료) | 2 | 0 |
| 20h | 다음 체크 → 빗나감 | 2 | 0 |
| 26h | 다음 체크 → 적중 | 2 | 1 |
| 30h | 플레이어 고용 | 3 | 0 |
| 30h+ | Pop 3, Bed 2, age 24h+ → **Hamlet 승격 가능** |

확률 40% 6h 주기면 평균 ~15h마다 1명 도착. 시작 1명에서 Pop 3까지 약 30h 게임시간(=실시간 약 15분, 게임시간 배율에 따라 다름). **현재 3명 시작 + 24h 대기와 시간 비용이 비슷**하지만 **플레이어가 의사결정을 하는 30분**이라 체감이 완전히 다름.

---

## 6. 단계별 마이그레이션

수정 순서 (각 단계 끝에서 빌드/플레이 가능):

1. **데이터 구조** — `NpcSaveData` 3개 필드 추가, `NpcStatus` enum 정의. 기존 동작 변화 없음.
2. **Visitor API** — `NpcManager`에 `SpawnVisitorNpc`/`HireVisitor`/`GetInnCapacity`/`GetInnVisitors` 추가. 호출자 없음.
3. **Inn UI 재구성** — `UIInn.cs`/`Inn.uxml`을 §4.3대로 방문자 메인/Rest 보조 레이아웃으로 변경, API 연결. 현 시점에서는 항상 빈 슬롯.
4. **이민 시스템 교체** — [System_VillagePopulation](../Assets/Scripts/Common/System/System_VillagePopulation.cs)을 `SpawnVisitorNpc` 경로로 변경. **이 시점부터 새 이민자는 Visitor**.
5. **Stage별 주기/확률** — §2.4 적용. 게임시간 진행으로 검증.
6. **시작 인구 1명** — `VillageTable.DefaultNpcList = "3001"`. Tier 승격 조건에 `HasObjectSet(Inn)` 추가.
7. **고용 비용** — Stage별 BaseCost + 직업별 보너스 적용. Settlement는 0이라 4번까지는 무료 고용 가능.
8. **밸런스 튜닝** — §5 표 기반 실측, 확률/주기 ±20% 범위에서 조정.

---

## 7. 검증 체크리스트

- [ ] 새 게임 시작 시 마을당 NPC 1명만 스폰
- [ ] Inn 세트(Bed+Hearth) 미완성 Settlement에서 이민 발생 안 함
- [ ] 동시에 최대 2명 Visitor 누적, 3명째 도착 시 그 틱 무시
- [ ] Inn UI 메인 영역에 Visitor 카드 표시, Hire 버튼 클릭 시 골드/식량 차감 + Resident 승격
- [ ] **Visitor 카드에 초상화 / 이름 / 희망 직업 / 숙련도(★) / 한 줄 소개 / 남은 시간 / 비용이 모두 표시됨**
- [ ] **Hire 버튼 비활성 시 사유 문구가 카드 안에 노출됨** (예: "골드 부족 (보유 0G / 필요 50G)")
- [ ] 고용 직후 Inn 빈자리 +1, 다음 체크에 새 Visitor 도착 가능
- [ ] **Visitor 도착 후 24 게임시간 경과 시 다음 이민 틱에 자동 디스폰** (§2.10)
- [ ] **만료 디스폰으로 빈자리 생기면 같은 틱에 새 Visitor 도착 가능**
- [ ] **Inn UI 카드의 남은 시간이 ≥12h 회색 / 6~12h 노랑·주황 / <6h 빨강+⚠ 으로 단계 변화**
- [ ] **남은 시간 표시가 게임시간 진행에 따라 1h 단위로 갱신**
- [ ] **Visitor 상태로 저장 → 30 게임시간 진행 후 로드 시 만료 처리되어 사라짐**
- [ ] Stage 승격 시 다음 체크부터 새 주기/확률 적용
- [ ] 기존 세이브 로드 시 모든 NPC가 Resident, 동작 동일
- [ ] Visitor 상태로 저장 → 게임 종료 → 재로딩 시 Inn UI에 동일 Visitor 복원 (만료 전이라면)

---

## 8. 미결정/후속 검토

- **여관 업그레이드 시스템 (가장 큰 후속)**: 본 문서는 Capacity 2 고정. 향후 Inn 레벨 1/2/3 = 2/4/6명 식으로 확장. 업그레이드 비용·조건·UI는 별도 설계. `VillageData.InnLevel` 신설, `GetInnCapacity`만 수정하면 본 시스템과 호환.
- **체류 시간 Stage 차등화**: §2.10은 24h 일괄. 후반 Stage(City)에서 12h로 줄여 회전을 빠르게 하거나, 초반(Settlement)에서 36h로 늘려 학습 여유를 줄지 실측 후 결정.
- **만료 페널티/평판**: 현재 만료 이탈은 무손실. Phase F 평판 시스템 도입 시 "고용 안 하고 보낸 Visitor 비율"이 다음 도착 확률에 영향?
- **Visitor 거절(능동 추방)**: 슬롯이 마음에 안 드는 NPC로 막혔을 때 즉시 비우는 버튼. 만료 시스템이 있어 강제 필요성은 낮지만 UX 보강용으로 검토
- **여러 마을 간 Visitor 이동**: 한 마을이 만석이면 다른 마을로 우회? 지금은 마을별 독립
- **튜토리얼/안내**: 첫 1명만 있을 때 "여관을 지으세요" 라는 명시적 가이드. UI/HUD 시스템과 별도 협의

---

## 9. 영향받는 파일 (예상)

| 파일 | 변경 |
|------|------|
| [NpcSaveData.cs](../Assets/Scripts/Npc/NpcSaveData.cs) | 필드 4개(Status/StayingAtVillageId/ArrivedGameTime/Description) + `NpcStatus` enum 추가 |
| [NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs) | Visitor API 추가 + 직업별 소개 풀(`_descriptionPool`) + 생성 시점 랜덤 선택 |
| [System_VillagePopulation.cs](../Assets/Scripts/Common/System/System_VillagePopulation.cs) | Stage 분기, Visitor 경로로 전환 |
| [System_VillageTierProgression.cs](../Assets/Scripts/Common/System/System_VillageTierProgression.cs) | `HasObjectSet(Inn)` 조건 추가 |
| [UIInn.cs](../Assets/Scripts/UI/UIInn.cs) | 방문자 메인 리스트 + Hire 핸들러, Rest/Save를 하단 보조로 이동 |
| [Inn.uxml](../Assets/UI/Inn/Inn.uxml) / [Inn.uss](../Assets/UI/Inn/Inn.uss) | 방문자 목록 마크업 |
| `BuildableItemTable` / `NpcTable` / `JobBonusTable` | 컬럼 추가 (시트 변경 + 파서) |
| `VillageTable` 시트 | `DefaultNpcList` 1명으로 |

---

> **체감 변화 한 줄**: "마을 만들고 가만히 있으면 자라던 시스템"이 → "여관을 짓고 손님을 받고 골드로 사람을 사는 시스템"으로 바뀐다.
