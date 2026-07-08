# NPC 자율 행동 & 원정 설계 (Activity / Venture System)

> ⚠️ **2026-07-08 갱신 — NPC 단순화 반영** ([NPC_SIMPLIFICATION_PLAN](NPC_SIMPLIFICATION_PLAN.md) D1):
> **모든 물리 NPC = 모험가(Adventurer) 단일**로 확정. 본 문서의 **Anchored(정주)/Worker(생산) 분류와 관련 절(§2 D1, §3.1 행동 클래스 등)은 폐기**됐다 —
> 상인·대장장이 등 마을 종속 NPC는 존재하지 않으며 서비스는 무인 건물이 제공하고, 자원 생산은 §0 추상 인구가 구동한다.
> 이 문서의 유효 범위는 **모험가의 활동/원정(Venture) 축**으로 축소된다. 아래 한 줄 요약·본문 중 정주/생산 NPC 서술은 기록용.

> **한 줄 요약**: 마을에 꼭 필요한 NPC(상인·대장장이 등)는 스케줄 없이 마을에 상주하며 서비스를 제공하고, 나머지 NPC는 **자기 직업/성향에 맞는 활동(몬스터 처치·던전 탐험 등)** 을 **혼자 또는 파티로** 수행한다. 활동 도중 NPC는 **부상·죽음**을 당할 수 있고 **월드 이벤트를 트리거**할 수 있다. 활동은 대부분 **LOD 기반 추상 시뮬레이션**으로 해결되어 관측 가능한 드라마(떠남 → 귀환/전사)를 만든다.

> **이 문서의 위치**: [README](../README.md)가 말하는 "NPC 일과 / 자율 의사결정 (Phase E)" 중 **자율 의사결정·활동 축**의 정본이다. 시계 기반 하루 일과(수면/식사 사이클)는 본 문서 범위가 아니다(§1.3 참조). 플레이어→NPC 상호작용은 [interactionSystem.md](interactionSystem.md), NPC↔NPC 관계 데이터는 [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md), NPC가 트리거하는 도전 이벤트는 [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md), 활동의 무대가 되는 오프라인/근거리 시뮬 규약은 [PROCEDURAL_HISTORY_DESIGN.md](../world_history/PROCEDURAL_HISTORY_DESIGN.md)가 담당한다.

**Status**: 설계 초안 (구현 대기) · **Last Updated**: 2026-07-01

---

## 1. 개요 & 범위

### 1.1 문제 정의

Phase A~D로 마을은 스스로 자라지만, NPC는 아직 "직무 보너스를 더하는 정적 인구수"에 가깝다([VILLAGE_PHASES §3](../village/VILLAGE_PHASES.md) — *"NPC 직무: 1:1 매칭, 보너스 가산만, 실제 이동/일과는 Phase E"*). 이 문서는 그 다음 단계로, **NPC가 스스로 무엇을 할지 정하고 실행하며 그 결과로 세계가 바뀌는** 행동 레이어를 설계한다.

### 1.2 설계 철학

- **관측 가능한 드라마 > 정밀 시뮬**: 플레이어가 "아침에 사냥꾼 셋이 북쪽 폐허로 떠났고, 저녁에 둘만 부상 입고 돌아왔다"를 **체감**하는 것이 목표. 매 프레임 실제 전투를 렌더링하는 것이 아니라, **추상 판정 + 서사 피드백**으로 만든다.
- **성향·관계가 행동을 만든다**: 누가 떠나는지, 누구와 파티를 맺는지, 무엇을 하는지가 `NpcStatComponent`(8성향)와 NPC↔NPC 관계에서 자연 도출된다. 임의 스크립트가 아니다.
- **기존 자산 재사용**: 정주 NPC는 이미 있는 서비스/직무 시스템 그대로, 파티는 [companionSystem](companionSystem.md) 개념 재사용, 죽음은 [NPC_RELATION_HISTORY §5.1](NPC_RELATION_HISTORY_DESIGN.md) 죽음 훅 재사용, 결과 기록은 연대기(Chronicle) append 재사용.
- **LOD 정합**: 원정 나간 NPC는 마을(활성 청크)을 떠난다 → [PROCEDURAL_HISTORY](../world_history/PROCEDURAL_HISTORY_DESIGN.md)의 "근거리 실체화 / 원거리 추상" 모델과 그대로 맞물린다.

### 1.3 범위 (In / Out)

| 포함 (v1~v3) | 제외 / 후순위 |
|--------------|--------------|
| NPC 행동 분류(정주/생산/모험) | **스케줄** — 정해진 스케줄로 움직이지 않음. 행동은 아래 "행동 모델"(필요 + 소요 시간)로 결정 |
| 활동 카탈로그(몬스터 처치·던전 탐험 …) | 실시간 파티 전투 렌더링(→ v4 실체화 조우) |
| 솔로/파티 활동, 파티 편성 | 플레이어의 원정 실시간 합류(→ v4) |
| 추상 원정 판정(성공/부상/전사/이벤트) | 오프라인(추상) 마을의 원정 세부(→ 정착지 시뮬로 흡수, v4) |
| 부상 회복 · 영구 죽음 처리 | |
| 월드 이벤트 트리거 핸드오프 | 이벤트 자체의 발화/페이싱(→ [WORLD_EVENT §8](../world_history/WORLD_EVENT_DESIGN.md)) |
| 결과의 관계·연대기·자원 되먹임 | |

> **행동 모델**: NPC는 정해진 스케줄이 아니라 *필요*에 따라 행동을 시작하고, 그 *행동의 소요 시간*만큼 경과하면 행동을 마치거나 복귀한다. 정주 NPC는 이 루프조차 없이 항상 마을에 상주한다.

---

## 2. 핵심 결정 (Core Decisions)

| # | 결정 | 이유 |
|---|------|------|
| **D1** | NPC를 **행동 클래스** 3종(Anchored/Worker/Adventurer)으로 나눈다 | 사용자 요구: "상인·대장장이는 상주, 나머지는 활동". 데이터 주도(§3.1) |
| **D2** | 원정은 **추상 시뮬**(게임시간 경과 + 가중 판정)로 해결한다 | 성능·일관성. Phase A "추상 타이머, NPC 실제 이동 없음"([VILLAGE_PHASES §? MVP](../village/VILLAGE_PHASES.md)) 계보 |
| **D3** | 원정 상태는 **`NpcManager`(매니저)가 소유**한다. ECS 컴포넌트로 만들지 않는다 | 파티에 **오프라인 NPC**가 낄 수 있고 원정이 청크 활성 창을 넘어 지속됨. [NPC_RELATION_HISTORY §4.4](NPC_RELATION_HISTORY_DESIGN.md) 선례("의도적으로 ECS 컴포넌트를 만들지 않는다") |
| **D4** | **론칭은 LOD-gated**(실체화 마을만), **결과 판정은 LOD-무관**(매니저 타이머 이벤트) | 편성은 살아있는 NPC·마을 맥락 필요 / 판정은 정수 타이머라 어디서든 발화. PROCEDURAL fold-back 논리와 동형 |
| **D5** | 로직은 **`System_NpcActivity`(무상태 시스템) + `VentureHelper`(static 서비스)**, 상태만 매니저 | CLAUDE.md "시스템은 무상태, 모든 상태는 컴포넌트/매니저". [interactionSystem §1](interactionSystem.md)의 서비스+매니저 패턴과 동일 |
| **D6** | 죽음/부상/이벤트/보상은 **기존 훅에 되먹임**한다(새 개념 최소화) | 죽음→§5.1 정리 훅, 결과→연대기 append, 보상→마을 자원/아이템 |
| **D7** | NPC↔NPC 호감도는 **함께한 활동의 결과**로 변동한다. "같은 마을에 있으면 자동 상승"하는 패시브 근접 drift(구 `System_NpcRelationship`)는 **쓰지 않는다**(§8.3) | 행동이 곧 관계 서사가 되어 더 자연스럽다. 단 초기 관계 backfill(가족·라이벌)은 유지 |

---

## 3. NPC 행동 분류

### 3.1 행동 클래스 (데이터 주도)

`JobType`(현행: `None, Gatherer, Woodcutter, Farmer, Hunter, Miner, Builder, Guard, Merchant, Blacksmith, Scholar, Chief`)에 **`BehaviorClass` 컬럼**을 매핑한다. 코드에 `if (job == Merchant)` 식 분기를 두지 않는다(CLAUDE.md "TableId 분기 금지, 의미는 컬럼에").

```csharp
public enum NpcBehaviorClass
{
    Anchored,    // 마을 상주. 활동 루프 없음(=항상 대기). 서비스 제공.
    Worker,      // 마을 내/근교 생산 작업. 원정 없음(선택적 저위험 근교 채집만).
    Adventurer   // 원정 주체. 몬스터 처치·던전 탐험을 솔로/파티로.
}
```

| BehaviorClass | 해당 JobType(초기 매핑) | 행동 |
|---------------|------------------------|------|
| **Anchored** | Merchant, Blacksmith, Scholar, Chief, (Innkeeper) | 상주. 기존 서비스/직무 시스템 그대로. **본 시스템 대상 아님** |
| **Worker** | Farmer, Woodcutter, Miner, Gatherer, Builder | 작업 오브젝트 옆에서 생산(Phase D 직무 배치). 원정 미참여. (v3+: 저위험 근교 `GatherTrip` 옵션) |
| **Adventurer** | Hunter, Guard | 원정 주체. §4 활동 수행 |

> **동적 승격**: 성향이 강한 NPC는 클래스 규칙을 넘어설 수 있다(선택). 예) `Courage`·`Curiosity`가 매우 높은 `Worker`는 가끔 파티에 자원 참가. v1은 **JobType 매핑 고정**으로 시작하고, 성향 기반 예외는 v3에서 튜닝(§16).

### 3.2 정주(Anchored) NPC — "그냥 서 있으면 된다"

사용자 요구를 그대로: **상인·대장장이류는 스케줄이 없다.** 이들은 이미 Phase D의 서비스 근접 집계·직무 배치로 자기 작업 오브젝트 옆에 상주한다. 본 시스템은 이들을 **활동 후보에서 제외**하기만 한다. 별도 신규 로직 없음.

### 3.3 인구 안전판 (마을 공동화 방지)

원정으로 마을이 텅 비면 [NpcManager](../../Assets/Scripts/Manager/NpcManager.cs)의 마을 고갈(`DepletedAt`) 로직이 오발화한다. 따라서:

- 마을별 **최소 상주 인원**(Anchored + 최소 Worker)은 원정 차출 대상에서 제외.
- 동시 원정 파티 수 상한(마을 규모 비례). 자세한 캡은 §16 미결정.

---

## 4. 활동 카탈로그 (Activity Catalog)

```csharp
public enum ActivityType
{
    None = 0,        // 대기(마을에 있음)
    // v1 코어
    Hunt,            // 몬스터 처치 — 근처 Zone의 몬스터 개체군 토벌
    DungeonDelve,    // 던전 탐험 — 근처 던전 1개 공략
    // v2+
    Patrol,          // 마을/도로 경계 순찰(저위험, Guard)
    GatherTrip,      // 근교 자원 원정(저위험, Worker)
    Escort,          // 카라반/주민 호위(v3)
    Pilgrimage       // 타 정착지 방문 — 관계/무역 씨앗(v4, PROCEDURAL 연계)
}
```

각 활동은 **대상(Target)** 과 **난이도(Difficulty)** 를 가진다.

| 활동 | 대상 소스 | 난이도 산출 | 보상 | 이벤트 트리거 가능성 |
|------|----------|------------|------|---------------------|
| **Hunt** | 근처 Zone(몬스터 스폰) → [MONSTER_SPAWN §Zone×Grade](../ai_monster/MONSTER_SPAWN_DESIGN.md) | Zone 깊이 + 마을 거리 | 소재/골드 → 마을 자원·상점 재고 | 낮음 (배회 명수 조우) |
| **DungeonDelve** | 근처 던전 → [dungeonSystem(보류)](../_archive/dungeonSystem.md) 티어 1~5 | 던전 Tier + 거리 | 희귀 전리품, 지식(Scholar) | **높음** (봉인/균열 자극 → Awakening/Rift) |
| **Patrol** | 마을 반경 | 낮음(위협도 연동) | 위협도↓, 소소한 조우 | 낮음 |
| **GatherTrip** | 근교 자원 노드 | 매우 낮음 | Food/Wood/Stone | 매우 낮음 |

> **거리 스케일링**은 [WORLD_EVENT §4 Common DNA](../world_history/WORLD_EVENT_DESIGN.md)와 동일 원칙: 멀수록 어렵고 보상·이벤트 확률이 높다. NPC의 원정 반경은 마을 Tier·파티 전력에 비례해 서서히 넓어진다.

> **던전 의존성 주의**: 던전 시스템은 현재 **보류**(오픈월드로 대체 운영, [dungeonSystem.md](../_archive/dungeonSystem.md)). `DungeonDelve`는 던전을 **완전 실체화하지 않고 추상 대상(시드+티어)** 으로만 참조하므로, 본 시스템은 던전 풀 구현 없이도 성립한다. 던전 실체화(플레이어가 같은 던전에 들어가 파티와 조우)는 v4 확장.

---

## 5. 데이터 모델

### 5.1 원정 상태 (매니저 소유, 영속)

```csharp
public enum VenturePhase { Forming, Departing, Away, Returning, Resolved }

public enum VentureOutcome
{
    None = 0,
    Success,         // 목표 달성, 전원 무사
    PartialSuccess,  // 달성했으나 일부 부상
    Repelled,        // 실패·후퇴(부상 위주)
    Casualty,        // 사망자 발생
    EventTriggered   // 무언가를 깨움(월드 이벤트 핸드오프) — 다른 결과와 병존 가능(플래그)
}

// NpcManager가 Dictionary<int ventureId, VentureState>로 보관 (런타임)
public class VentureState
{
    public int VentureId;
    public int VillageId;
    public ActivityType Type;
    public List<int> MemberNpcIds;   // 1명 = 솔로
    public int LeaderNpcId;          // 편성 주도 NPC(성향/직업 가중 최고)
    public int TargetKey;            // Hunt: zoneId / DungeonDelve: 던전 시드
    public int Difficulty;           // §4 산출 (Zone깊이·Tier + 거리)
    public VenturePhase Phase;
    public double DepartGameTime;
    public double EtaGameTime;        // 결과 판정이 발화할 게임시간
    public VentureOutcome Outcome;    // 판정 후 세팅
}
```

### 5.2 NPC 측 필드 추가 (`NpcSaveData`)

```csharp
public NpcBehaviorClass BehaviorClass;   // JobTable에서 캐시(§3.1)
public int ActiveVentureId;              // 0 = 원정 없음
public double WoundedUntilGameTime;      // 0 = 건강 / > now = 회복 중(원정 불가)
```

> 원정 중인 NPC는 세계에 존재하지 않는다 → 기존 `NpcSaveData.IsActive=false` + `ActiveVentureId>0`로 표현(청크 비활성과 구분). 마을이 활성인데 엔티티가 없는 상태 = "원정 나감(Away)".

### 5.3 세이브 직렬화

```csharp
// WorldData 추가
public List<VentureEntry> VentureList = new();   // 진행 중 원정만(Resolved는 제거)

[Serializable]
public class VentureEntry
{
    public int VentureId, VillageId, LeaderNpcId, TargetKey, Difficulty;
    public int Type;                 // ActivityType
    public int Phase;                // VenturePhase
    public List<int> MemberNpcIds = new();
    public double DepartGameTime, EtaGameTime;
}
```

`_ventures`(런타임 Dict) ↔ `WorldData.VentureList`(세이브). 저장 패턴은 `_npcAffinity`/`_npcRelations`와 동일. `NpcSaveData` 3필드는 기존 NPC 세이브에 추가(Version 증가). **ECS 풀 추가 없음**(§D3).

---

## 6. 아키텍처 (계층 & 위임)

```
                        ┌───────────────────────────────────────────┐
System_NpcActivity ────▶│ (매 인터벌, 실체화 마을만)                 │
 (무상태 디렉터/리졸버)  │  1) 편성: idle Adventurer 스코어링→파티→ CreateVenture ─▶ 멤버 despawn(Away)
 Priority ~59            │  2) 판정: Eta 도달 원정 → VentureHelper.RollOutcome → 적용
                        └───────────────────────────────────────────┘
        │ 읽기: NpcStat / NpcJob / 관계·호감도 / 마을 위협·필요도 / Zone·던전 난이도
        │ 위임(math): VentureHelper.Score / PartyPower / RollOutcome / EstimateDuration   (static·무상태)
        ▼
NpcManager._ventures ──소유·영속──▶ CreateVenture / GetVenture / ResolveVenture / Cancel
        │ 결과 적용 시 위임:
        ├─ 생존/부상 귀환 → SpawnNpc(재실체화) + WoundedUntil 세팅
        ├─ 사망           → UnregisterNpc(오프라인 죽음 경로) + §8 관계·연대기 훅
        ├─ 보상           → VillageManager 자원/상점 재고 · ItemManager 드랍
        └─ 이벤트 트리거  → WorldEvent 디렉터 핸드오프(§9)
NpcManager._ventures ──Save/Load──▶ WorldData.VentureList
```

### 6.1 시스템 (`System_NpcActivity`)

- `IFixedUpdateSystem`, **Priority 59**(Population 밴드 55-59 말미). Activity가 **활동 결과로 NPC↔NPC 호감도를 기록(writer, §8.3)** 하고 편성 시 그 호감도를 **읽으므로(reader, §7.2)** 자기 완결이다 — 별도 패시브 drift 시스템(`System_NpcRelationship`)에 의존하지 않는다(§2 D7). `UpdateInterval`은 게임시간 수십 분 상당(잦을 필요 없음).
- **무상태**: 모든 상태는 `NpcManager._ventures` + `NpcSaveData`.
- **LOD-gated**: 실체화(materialized) 마을에 대해서만 편성 로직 수행(§D4).
- 두 페이즈를 매 틱 처리: (1) 신규 편성, (2) 만기 원정 판정. 판정은 LOD-무관(§6.2).

### 6.2 편성=LOD-gated, 판정=LOD-무관 (핵심)

- **편성**은 살아있는 NPC·마을 필요도·관계를 읽어야 하므로 실체화 마을에서만.
- **판정**은 `EtaGameTime ≤ 현재`인 원정을 매니저 타이머로 발화 → `VentureHelper.RollOutcome`. 이는 정수/난수 연산이라 마을이 그 사이 비실체화되어도(플레이어가 멀어져도) 발화 가능. 귀환 시 마을이 여전히 실체화면 엔티티 재스폰, 아니면 `NpcSaveData`만 갱신(다음 실체화 때 반영). PROCEDURAL fold-back 논리와 동형.

### 6.3 서비스 (`VentureHelper` — static·무상태)

```csharp
public static class VentureHelper
{
    public static bool          CanVenture(int npcId);                 // Adventurer·비부상·비원정·비동료
    public static float         ScoreActivity(int npcId, ActivityType t, int villageId); // 성향+필요도+위험선호
    public static float         PartyPower(IReadOnlyList<int> members); // 전투력×시너지(호감도/관계/직업조합)
    public static double        EstimateDuration(VentureState v);       // 거리+난이도 → 게임시간
    public static VentureOutcome RollOutcome(VentureState v);           // §7 판정
}
```

> v1은 계수를 코드에 두고, 밸런싱 단계(§16)에서 시트/SO로 이관한다([interactionSystem §9] 동일 전략).

---

## 7. 의사결정 & 파티 편성 & 결과 판정

### 7.1 누가 떠나는가 (활동 스코어링)

`System_NpcActivity`가 실체화 마을의 **자격 있는 NPC**(`CanVenture`: Adventurer · 비부상 · 원정중 아님 · 플레이어 동료 아님)를 모아 활동별 점수를 낸다.

```
ScoreActivity = base(activity)
  + Courage   * wCourage      // 용감할수록 위험 활동 선호
  + Curiosity * wCuriosity    // 호기심 → 던전/미지 선호
  + jobFit(JobType, activity) // Hunter→Hunt, Guard→Patrol …
  + villageNeed(activity)     // 위협도↑ → Hunt/Patrol 가중, 자원부족 → GatherTrip
  - riskAversion(Patience, currentPartyPower vs difficulty)
```

임계치를 넘는 활동이 있으면 후보로 등록. `Chief`/`Guard`가 있으면 마을 필요도(위협·자원) 가중이 커진다.

### 7.2 누구와 가는가 (파티 매치메이킹)

혼자 갈지, 파티를 맺을지는 **활동 난이도 vs 개인 전력**과 **관계**가 정한다.

- 난이도가 개인 전력을 크게 웃돌면 파티 필요 → 리더가 동료를 부른다.
- 후보는 **NPC↔NPC 호감도·관계**로 가중: `Friend`/`Sibling`/`Spouse`는 기꺼이 합류, `Rival`/`GrudgeAgainst`는 거부([NpcManager](../../Assets/Scripts/Manager/NpcManager.cs) 관계 API 재사용).
- 파티 크기: **1~4명**([companionSystem](companionSystem.md) 슬롯 감각 재사용). 직업 시너지(전위+원거리, 전투+치유)는 `PartyPower` 배수.
- 편성 성사 → `CreateVenture` → 멤버 despawn(Away) → `Departing`→`Away`.

> 파티로 함께 살아남으면 멤버 간 호감도↑(§8.3). 함께 갔다 누군가 잃으면 원한/`Sentiment` 씨앗(§8.3). **관계가 행동을 만들고, 행동이 관계를 되먹인다** — 이 활동 기반 변동이 NPC↔NPC 호감도의 주 소스이며, "같은 마을에 있으면 자동 상승"하는 패시브 근접 drift는 쓰지 않는다(§2 D7).

### 7.3 결과 판정 (`RollOutcome` — 추상)

원정 만기(`EtaGameTime`)에 1회 판정.

```
partyPower = PartyPower(members)                 // 전투력×시너지
riskRatio  = Difficulty / max(partyPower, ε)

// 1) 주 결과 버킷 (riskRatio가 클수록 나쁜 쪽으로)
//    Success / PartialSuccess / Repelled / Casualty
//    멤버 평균 Courage·Patience가 하방을 완충, 난수 가미

// 2) 개별 멤버 판정 (부상/사망은 멤버 단위)
foreach member:
    p_wound = f(riskRatio, member.SkillLevel, member.Courage)
    p_death = g(riskRatio) * (1 - survivalMitigation(party, member))
    → Healthy / Wounded / Dead

// 3) 이벤트 트리거 (독립 롤, 주 결과와 병존)
p_event = h(activity, Difficulty/Tier, zoneFateKeyword, maxCuriosity(party))
    → true면 Outcome |= EventTriggered (§9)

// 4) 보상 (성공도 비례)
loot = scale(activity, Difficulty, successDegree)
```

- **성향 반영**: `Courage`는 사망 완충, `Curiosity`는 이벤트 확률↑(봉인을 굳이 건드림), `Patience`는 무리한 심층 진입 억제.
- **거리/난이도 스케일**: 멀고 깊을수록 사망·이벤트·보상이 함께 커진다(WORLD_EVENT DNA).
- **결정론**: 판정 난수는 원정 시드(VentureId+DepartGameTime 등) 기반으로, 세이브/로드·리플레이 일관성 확보(프로젝트 결정론 원칙).

---

## 8. 부상 & 죽음 처리

### 8.1 부상 (Wounded)

- 부상 멤버는 귀환하되 `WoundedUntilGameTime = now + recoveryHours(severity)`.
- 회복 기간: 원정 차출 제외, (선택) 생산 보너스 감소. 회복 완료 시 자동 복귀.
- (v3) 치유 직업/시설(신전·`Scholar`/약초상)이 회복 단축 → 사회적 상호작용 고리.

### 8.2 영구 죽음 (Casualty)

원정 중 죽은 NPC는 **세계에 엔티티가 없다**(이미 Away). 따라서 `System_EntityDestroy`(NpcTag) 경로가 아니라, **매니저 레벨의 "오프라인 죽음" 경로**로 정리한다. 다만 정리 내용은 기존 영구삭제와 동일 훅을 재사용:

- `RemoveAllAffinityInvolving` / `RemoveAllRelationsInvolving`([NpcManager](../../Assets/Scripts/Manager/NpcManager.cs)) 호출.
- 마을 인구/고갈(`DepletedAt`) 상태 갱신(전원 사망 시).
- **유족 되먹임**([NPC_RELATION_HISTORY §5.1] 죽음 훅): 가족/파티원에 `GrudgeAgainst`(원인 대상) 또는 `LostFamilyToWar`류 `Sentiment` 생성 후보 — 이는 [interactionSystem §4](interactionSystem.md) 선물 선호와 [WORLD_EVENT §3-E 구출 원정](../world_history/WORLD_EVENT_DESIGN.md) 씨앗이 된다.
- **연대기 append**: 새 `ChronicleEventType.Fell`(가칭) — "○○가 {대상}에서 돌아오지 못하다". [PROCEDURAL §5 Live append](../world_history/PROCEDURAL_HISTORY_DESIGN.md) 경로 재사용.

> **드라마의 원천**: 함께 떠난 파티원의 죽음은 생존자 관계를 영구히 바꾼다. 플레이어가 아끼던 NPC의 전사는 [interactionSystem](interactionSystem.md) 관계 루프와 교차하는 강한 서사 사건.

### 8.3 함께한 활동 → 관계 변동 (NPC↔NPC 호감도의 주 소스)

NPC↔NPC 호감도는 **"같은 마을에 있으면 자동 상승"하는 패시브 근접 drift가 아니라, 함께 수행한 활동의 결과**로 변동한다(§2 D7). 행동이 곧 관계 서사가 되어 더 자연스럽다.

| 원정 결과(§5.1 `VentureOutcome`) | 파티원 간 관계 변동 |
|------|------|
| Success / PartialSuccess | 멤버 상호 **호감도↑** (성과·개인 기여·`Friendliness` 성향 가중) |
| Repelled (부상 위주 후퇴) | 소폭↑ 또는 무변화 (고생을 함께함) |
| Casualty (파티원 전사) | 생존자에 상실감. 사인·관계에 따라 **원한(`GrudgeAgainst`)/`Sentiment` 후보**(§8.2 죽음 훅과 연동) |

- **저장**: `NpcManager` 호감도/관계 데이터에 `ModifyAffinity`/`AddRelation`로 반영(기존 API, ±100 clamp).
- **초기 관계 유지**: 가족·형제·라이벌 등 초기 backfill([NPC_RELATION_HISTORY §4](NPC_RELATION_HISTORY_DESIGN.md))은 그대로. 본 절은 그 위에서의 **런타임 변동**만 담당하며, [NPC_RELATION_HISTORY §5.2](NPC_RELATION_HISTORY_DESIGN.md)의 패시브 drift 시스템을 **대체**한다.
- **솔로 활동**은 파티 관계 변동 없음(개인 `SkillLevel`·평판 등만 변동).

---

## 9. 월드 이벤트 트리거

원정 판정에서 `EventTriggered`가 서면, 원정은 **도전 이벤트의 씨앗**을 [WORLD_EVENT](../world_history/WORLD_EVENT_DESIGN.md) 디렉터에 넘긴다.

| 원정 상황 | → WORLD_EVENT 카탈로그 |
|-----------|------------------------|
| 던전 심층에서 봉인/유물 자극 | **A. Awakenings** (고대 위협 각성) |
| 오염된 폐허를 건드림 | **B. Spreading** (역병/부패 확산) |
| 차원 균열 진입 | **F. Rifts** (절차적 던전) |
| 파티 전멸·실종 | **E. Lost** (가족/원정대 구출) — 유족 `Sentiment`와 교차 |

- **핸드오프 데이터**: `VentureState`(대상 시드, 난이도, 참가자, 발생 Zone) + 트리거 종류.
- **경계**: 이벤트의 **발화 타이밍·페이싱·동시 상한**은 본 문서가 아니라 [WORLD_EVENT §8 미결정(디렉터)](../world_history/WORLD_EVENT_DESIGN.md) 소관. 본 문서는 **트리거 진입점**만 제공한다.
- **되먹임**: 파티가 살아 돌아오면 "소문"의 출처가 되고(NPC 대사), 실종되면 그 자체가 구출 이벤트(E)가 된다 → 연대기 반영(WORLD_EVENT §6 루프 종결).

---

## 10. LOD & 오프라인 정합

| 상황 | 처리 |
|------|------|
| 실체화 마을 NPC 원정 출발 | 멤버 despawn(Away), 원정 등록. 플레이어에겐 "떠남"으로 관측 |
| 원정 중 플레이어가 멀어져 마을 비실체화 | 원정은 매니저 상태로 **그대로 유지**. 판정은 타이머로 계속 발화(§6.2) |
| 귀환 시 마을 실체화 상태 | 생존자 재스폰(SpawnNpc). 비실체화면 `NpcSaveData`만 갱신, 다음 실체화 때 반영 |
| 처음부터 추상(원거리) 마을 | v1~v3: 원정 로직 미실행. 인구 변동은 [PROCEDURAL 정착지 시뮬](../world_history/PROCEDURAL_HISTORY_DESIGN.md)이 추상 처리. **완전 통합은 v4**(§16) |

---

## 11. 관측 & UI (플레이어 체감)

시뮬이 추상이어도 **플레이어가 반드시 체감**해야 의미가 있다.

- **마을 패널**: 진행 중 원정 목록(대상·파티·귀환 예정), 부상자.
- **[NpcInspect](../../Assets/UI/NpcInspect/NpcInspect.uxml)**: 부상 상태, 원정 이력, 파티 관계.

---

## 12. 시스템 등록 & 우선순위

```csharp
// SystemManager.Initialize()
// Priority 59: NPC 활동/원정 디렉터+리졸버 (FixedUpdate) - Population 밴드 말미
RegisterSystems(new System_NpcActivity());   // UpdateInterval = 게임시간 기반(수십 분 상당)
```

| Priority | 시스템 | 비고 |
|----------|--------|------|
| 56 | System_VillagePopulation | 인구 |
| ~~58~~ | ~~System_NpcRelationship~~ | **미사용/폐기** — 패시브 근접 호감도 drift 폐기. NPC↔NPC 호감도는 §8.3 활동 기반이 담당 |
| **59** | **System_NpcActivity** | **본 문서(신규)** — 활동 결정·원정 수명주기 + **활동 결과로 호감도 기록(§8.3)** |
| 60~64 | Lifecycle(Tier·서비스) | |

> Village 도메인 대역(50-69) 정책 준수([CLAUDE.md](../../CLAUDE.md) · [VILLAGE_PHASES §4.1](../village/VILLAGE_PHASES.md)).
> `System_NpcRelationship` 폐기로 Population 밴드 58 슬롯은 비게 되며, 기존 코드/설계([NPC_RELATION_HISTORY §5.2](NPC_RELATION_HISTORY_DESIGN.md))는 정리 대상.

---

## 13. 구현 우선순위 (Phase)

### Phase 1 — MVP (솔로 Hunt, 추상 판정)
- [ ] `NpcBehaviorClass` + JobTable 매핑 (Anchored 제외 로직)
- [ ] `NpcManager` 원정 region: `_ventures` + Create/Get/Resolve/Cancel + Save/Load
- [ ] `NpcSaveData` 3필드 + WorldData.VentureList (Version 증가)
- [ ] `System_NpcActivity`: 자격 검사 → 솔로 `Hunt` 출발/귀환, Away despawn/respawn
- [ ] `VentureHelper.RollOutcome` v0: Success/Repelled/Wounded/Casualty(솔로)
- [ ] 알림 피드(떠남/귀환/전사)

### Phase 2 — 파티 & 결과 되먹임
- [ ] 파티 매치메이킹(호감도·관계·직업 시너지, `PartyPower`)
- [ ] `DungeonDelve` 대상(던전 시드+티어, 실체화 없이)
- [ ] 부상/회복(`WoundedUntil`) 시스템
- [ ] 죽음 매니저 경로 + 유족 관계/`Sentiment` 훅 + 연대기 `Fell` append
- [ ] 보상 → 마을 자원/상점 재고/아이템 드랍

### Phase 3 — 월드 이벤트 & 스케일
- [ ] `EventTriggered` → WORLD_EVENT 디렉터 핸드오프(A/B/E/F)
- [ ] 거리/난이도 스케일링, 원정 반경 확장(마을 Tier 비례)
- [ ] 소문 전파(NPC 대사 연동), `Patrol`/`GatherTrip` 추가
- [ ] 인구 안전판(최소 상주·동시 원정 캡) 튜닝

### Phase 4 — 실체화 조우 & 오프라인 통합
- [ ] 플레이어가 오픈월드에서 원정 파티와 조우/합류(실체화 전투)
- [ ] 추상(원거리) 마을 원정을 PROCEDURAL 정착지 시뮬에 통합
- [ ] UI 폴리시(원정 패널·추모·이력)

---

## 14. 미결정 사항 (구현 전 결정 필요)

1. **동시 원정 캡 & 최소 상주 인원** 공식 — 마을 Tier/인구 대비 몇 %까지 차출? (공동화·고갈 오발화 방지)
2. **시간 모델** — 원정 소요를 게임시간 몇 시간~며칠로? 플레이어가 "떠남→귀환"을 한 세션에 관측 가능한 페이싱.
3. **행동 클래스 근원** — JobType 매핑 고정 vs 성향(`Courage`) 동적 승격 혼합 시점(§3.1).
4. **보상 vs 플레이어 경제** — NPC가 물어오는 전리품이 상점 재고/마을 부에 미치는 인플레 관리.
5. **동료 시스템 충돌** — 플레이어가 [companion](companionSystem.md)으로 영입한 NPC는 원정 대상에서 제외(§6.3 `CanVenture`)로 처리하나, 경계 사례(원정 중 영입 시도 등) 규칙 확정 필요.
6. **연대기 이벤트 타입 확장** — `Fell`(전사)·`Delved`(공략 성공) 등 추가 여부와 [PROCEDURAL §3.3](../world_history/PROCEDURAL_HISTORY_DESIGN.md) 목록 정합.
7. **부상 악화/사망** — 미치료 부상이 사망으로 진행? 치유 직업 도입 시점.
8. **던전 대상 소스** — 던전 보류 상태에서 `DungeonDelve` 대상을 Zone 시드로 임시 표현할지, 경량 던전 레지스트리를 둘지.

---

## 15. 의존성 & 관련 문서

| 의존 | 관계 |
|------|------|
| [NpcManager](../../Assets/Scripts/Manager/NpcManager.cs) | 원정 상태 소유, 스폰/디스폰/죽음 정리 훅 |
| [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md) | 파티 매치메이킹(초기 관계·호감도 backfill), 죽음 훅(§5.1) 유족 관계/Sentiment. **단 §5.2 패시브 drift는 본 문서 §8.3(활동 기반)이 대체** |
| [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) | 이벤트 트리거 핸드오프(§9), 이벤트 발화·페이싱은 그쪽 소관 |
| [PROCEDURAL_HISTORY_DESIGN.md](../world_history/PROCEDURAL_HISTORY_DESIGN.md) | LOD 실체화/fold-back, 연대기 append, 오프라인 마을 통합(v4) |
| [companionSystem.md](companionSystem.md) | 파티 크기·시너지·명령 개념 재사용, 동료 충돌 규칙 |
| [interactionSystem.md](interactionSystem.md) | 원정 결과가 플레이어→NPC 관계·선물 루프와 교차 |
| [MONSTER_SPAWN_DESIGN.md](../ai_monster/MONSTER_SPAWN_DESIGN.md) | Hunt 대상(Zone×Grade), 난이도 소스 |
| [dungeonSystem.md (보류)](../_archive/dungeonSystem.md) | DungeonDelve 대상(Tier), v4 실체화 조우 |
| [VILLAGE_PHASES.md](../village/VILLAGE_PHASES.md) | Priority 대역(50-69), Phase E 자리매김 |
| [CLAUDE.md](../../CLAUDE.md) | ECS/무상태·데이터주도·성능 규약 |

---

**Status**: 설계 초안 (구현 대기) — 서비스+매니저 구조(ECS 컴포넌트 아님), LOD-gated 편성 + LOD-무관 판정, 추상 원정 시뮬
**Dependencies**: NPC 시스템, NPC 관계, 월드 이벤트, 절차적 역사(LOD), 동료/파티, 몬스터 스폰, (보류)던전
**Last Updated**: 2026-07-01
