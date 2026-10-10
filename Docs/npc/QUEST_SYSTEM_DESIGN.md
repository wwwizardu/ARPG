# 퀘스트 시스템 (NPC 소망 — 추적 · 완료 · 보상 · 기록) — 설계

> **목표**: NPC의 **소망(Wish)** 이 게임 안에서 *동작*하는 방식을 설계한다 — 관계가 깊어질 때 떠오른 소망을 추적하고, 충족을 판정해 보상(호감도 대폭↑ + 보답)을 주고, 그 상태가 세이브에 **기록**되기까지. **소망은 구조적으로 퀘스트다**(목표 대상 + 충족 판정 + 보상 + 상태). 그래서 본 문서는 퀘스트 시스템의 정본이되, *게시판에서 계약을 수락하는* 옛 모델이 아니라 **관계 속에서 자연 발생하는 소망**을 다룬다.
>
> **상태 (2026-10-10 갱신)**: 첫 소망 종류 **ItemGift는 구현됐다** — 단, 이 문서 §2·§6의 `NpcWish`·`WorldData.Wishes` 스키마가 아니라
> [NPC_NARRATIVE_CONTENT_DESIGN](NPC_NARRATIVE_CONTENT_DESIGN.md)의 통합 구조(작성 표 `NarrativeContent` Wish 행 + 원장 `NarrativeContentInstance`, §2.2·§7.2·§8.2)로 구현했다.
> 떠오름은 호감 30(사용자 결정 2026-10-10, 아래 §3의 "보통 Friend↑"를 대체), 충족은 선물로 아이템 전달·소모, 보상은 그 문서 §4.5의 관계 변화·의뢰 보답이다.
> 스파이크 타입 `NpcWish`·`WishCandidate`와 LLM 소망 생성은 2026-10-10에 지웠고 `WishKind`·`WishState`만 남겼다. Recover·Reunion·목표 진행(`QuestObjective`)·소망 UI 패널은 미구현이다.
> 이 문서의 상태머신(§2.2)·대상 보장(§4.0, §8.2)·관계 프레이밍(§1)은 계속 기준으로 삼는다.
>
> **경계 (3분할)**:
> - 소망이 *어떻게 떠오르나*(호감도 단계·대화로 사연 공개·플레이어 유추) → [interactionSystem.md §4](interactionSystem.md) (관계·선물 몰입 루프)
> - 소망이 *어떻게 생성되나*(컨텍스트→프롬프트→LLM→검증) → [NARRATIVE_ENGINE_DESIGN.md](../world_history/NARRATIVE_ENGINE_DESIGN.md)
> - 소망이 *어떻게 추적·완료·기록되나* → **본 문서** (소망 데이터 모델·상태머신·영속화의 **정본**)
>
> **선행/관련 문서**:
> - [interactionSystem.md](interactionSystem.md) — 관계·선물 루프, 소망 *떠오름*(§4.7) — 본 시스템의 진입점
> - [NARRATIVE_ENGINE_DESIGN.md](../world_history/NARRATIVE_ENGINE_DESIGN.md) — 소망 *생성*(LLM)
> - [WORLD_HISTORY_PLAN.md](../world_history/WORLD_HISTORY_PLAN.md) — 호감도 API(`ModifyPlayerRelation`)·Live 연대기 훅
> - [gameOverview.md](../overview/gameOverview.md) — 거시 진행에서 관계 루프의 위치(몰입 레이어)
> - [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) — 도전 이벤트(도전 축). `Reunion` 소망과 "가족 구출 원정"(§3-E)이 교차

---

## 1. 범위

| 포함 | 제외(다른 문서) |
|------|----------------|
| 소망(Wish) 데이터 모델 + 상태머신 | 소망이 떠오르는 *조건*(호감도 단계·대화) → [interactionSystem §4](interactionSystem.md) |
| 목표 추적(충족 감지) | 선호 모델·유추 선물(Tier 1)·보답 정책 → 〃 |
| 충족 판정 + 보상(호감도 대폭↑ + 보답) 지급 | 소망 *생성* 파이프라인(LLM) → [NARRATIVE_ENGINE](../world_history/NARRATIVE_ENGINE_DESIGN.md) |
| 기록/영속화(세이브) | |
| 소망 표시 UI(관계 패널 통합) | |
| **대상 어휘 + 후보 산출(생성과 공유)** | |

> **핵심 프레이밍**: 소망의 *메커니즘*(추적·완료·보상·상태)은 본 문서가 소유하되, **표면 UX는 끝까지 관계 프레이밍**이다 — "퀘스트를 받는다"가 아니라 "관계가 깊어져 그 사람의 갈망을 알게 되고 채워준다". 퀘스트 게시판·수락 버튼·퀘스트 로그는 **없다**.

---

## 2. 데이터 모델 (정본)

> 프로젝트 패턴(POCO + Newtonsoft 직렬화, `WorldData` 합류) 준수. 관계/연대기 시스템처럼 **ECS 컴포넌트가 아닌 매니저 + `WorldData`**에 상태를 둔다(엔티티 수명과 분리). 클래스명은 잠정.

**2층 구조 — 기계 골격 + 내러티브 스킨**: 게임이 *결정론적으로 실행·검증*하는 기계 골격(목표·대상·보상)과, 생성기가 채우는 내러티브 스킨(대사)을 한 객체에 담되 역할을 분리한다.

> **스파이크 선구현 (2026-07-09 확인, 2026-10-10 삭제)**: 아래 스키마의 부분집합인 스파이크판 `NpcWish`가 [NpcNarrativeTypes.cs](../../Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs)에 있었으나 2026-10-10에 지웠다(`WishKind`·`WishState` enum만 남음).
> 첫 소망(ItemGift)은 이 스키마로 이관하지 않고 [NPC_NARRATIVE_CONTENT_DESIGN](NPC_NARRATIVE_CONTENT_DESIGN.md)의 `NarrativeContentInstance`(Kind=Wish: 주인·`TargetItemId`·`WishState`·보고·의뢰 보답)로 구현했다(§7.2·§8.2). 아래 `NpcWish`·`QuestObjective`·`QuestReward`는 Recover·Reunion 등 다른 소망 종류를 설계할 때 참고하는 이전 설계다.

```csharp
// Assets/Scripts/Quest/NpcWish.cs (제안)
public class NpcWish
{
    // ── 기계 골격 (게임이 실행·검증, 닫힌 어휘) ──
    public int WishId;
    public int GiverNpcEntityId;          // 소망 주체 NPC (실재)
    public WishKind Kind;                 // 닫힌 enum (§2.1)
    public List<QuestObjective> Objectives; // 충족 조건(보통 1개)
    public QuestReward Reward;            // 충족 보답(아이템/골드) — 선택
    public int AffinityReward;            // 충족 시 대폭 호감도 상승(interactionSystem §3 ModifyPlayerRelation)
    public WishState State;               // 상태머신(§2.2)
    public int RevealTierGate;            // 떠오르는 호감도 단계(interactionSystem §4.2)
    public List<int> ContextEventIds;     // 근거 ChronicleEvent.EventId (검증·회고)

    // ── 내러티브 스킨 (생성기 산출, 골격과 정합) ──
    public string Title;                  // (선택) 회고/패널 표시용
    public string RevealDialogue;         // 소망을 토로하는 NPC 1인칭 대사
    public string FulfillDialogue;        // 충족 시 반응 대사
}

public struct QuestObjective
{
    public ObjectiveVerb Verb;            // 닫힌 enum
    public int TargetRef;                 // 실재 대상(§4 추적이 감지)
    public int Count;                     // 목표 수
    public int Progress;                  // 현재 진행(런타임)
}

public class QuestReward
{
    public int Gold;
    public List<int> ItemIds;             // 허용 아이템 풀에서만
}

public enum ObjectiveVerb { Kill, Collect, GoTo, TalkTo, Deliver }
public enum WishKind { ItemGift, Recover, Reunion }
public enum WishState { Hidden, Revealed, Fulfilled, Failed }
```

### 2.1 소망 종류(`WishKind`) ↔ 목표 동사(`ObjectiveVerb`)

소망은 옛 5종 동사로 **충족을 판정**한다. 단 `Kill`/`GoTo`는 *소망 아이템을 얻는 수단*일 뿐, 소망의 목표로 플레이어에게 제시되지 않는다.

| `WishKind` | 의미 | 충족 Objective | 완료 감지(§4) |
|-----------|------|---------------|--------------|
| ItemGift | 특정 아이템을 갖고 싶음 | `Deliver`(아이템 → 주체 NPC) | 선물 + 아이템 소모 |
| Recover | 유실된 유물을 되찾고 싶음 | `Collect`(폐허/유적 유물) → `Deliver` | 인벤토리 변화 + 전달 |
| Reunion | 헤어진 가족과 재회하고 싶음 | `TalkTo`(대상 가족 NPC) | NPC 상호작용 |

> `Kill`/`GoTo`는 위 아이템을 *얻는 과정*에서 자연히 쓰이지만(예: 몬스터 드랍·폐허 진입), 소망 자체의 추적 대상은 아니다. 새 메커닉이 필요한 종류는 enum 확장.

### 2.2 상태머신

```
        호감도 단계 도달(§3)        충족 조건 달성(§4)
Hidden ──────────▶ Revealed ───────────────────────▶ Fulfilled ──▶ (보상 + 호감도 대폭↑ + 보답)
                      │
                      │ 실패조건(대상 NPC 영구 사망 / 대상 정착지 Fate=Ruined)
                      ▼
                    Failed
```

- **Hidden**: 생성됐으나 아직 안 떠오름(호감도 단계 미달).
- **Revealed**: NPC가 토로해 플레이어가 인지(§3). 추적 시작. *수락 단계 없음* — 채워줄지는 플레이어 자유.
- **Fulfilled**: 충족 판정됨 → 보상 단계로(§5).
- **Failed**: 실패조건 충족(대상 NPC 사망, 대상 정착지 폐허화 등).

> 옛 모델의 `Offered→Active`(수락) 대신 `Hidden→Revealed`(대화로 떠오름)다. 게시판/수락 버튼이 없으므로 "수락" 상태가 없다.

---

## 3. 떠오름 (interactionSystem 핸드오프)

소망이 *언제·어떻게* 떠오르는지는 [interactionSystem §4.7](interactionSystem.md)이 정본이다. 요약:

1. 플레이어가 NPC와 대화로 관계를 쌓아 호감도가 `RevealTierGate`(보통 `Friend`↑, [interactionSystem §4.2](interactionSystem.md))에 도달. (2026-10-10 사용자 결정: 첫 구현은 호감 30 — [NPC_NARRATIVE_CONTENT_DESIGN](NPC_NARRATIVE_CONTENT_DESIGN.md) 머리말)
2. 깊은 대화 비트에서 NPC가 백본 사연에서 비롯된 소망을 토로(`RevealDialogue`) → 본 시스템에서 `Hidden → Revealed`, 추적 시작.
3. 인지: 퀘스트 로그가 아니라 **관계 패널**에 *"이 NPC가 ~을 바람"* 으로 은근히 표시(§7).

> 해당 NPC에 `Revealed` 소망이 없고 조건이 맞으면 생성 트리거 → [NARRATIVE_ENGINE §4.1](../world_history/NARRATIVE_ENGINE_DESIGN.md). NPC당 동시 `Revealed` 상한·소망 연쇄는 §10 미결정.

---

## 4. 대상 보장 & 목표 추적

### 4.0 대상 보장

소망 대상은 **시스템이 실현 가능하다고 아는 템플릿**(§8.2)에서 골라졌으므로, 충족 경로가 보장된다. 옛 `spawn-on-accept`(수락 시 일괄 스폰)는 **불필요** — 소망 아이템은 평범한 경제/전리품/제작/유적에서 *플레이어가 알아서* 구한다(이 시스템은 진출 강제가 아니므로).

| `WishKind` | 대상 보장 |
|-----------|----------|
| ItemGift | 대상 아이템이 ItemTable·획득 경로에 존재(후보 산출 시 보장, §8.2) |
| Recover | 대상 유물의 출처(폐허/유적 ref)가 실재·도달 가능 |
| Reunion | 대상 가족 NPC 존재 보장(없으면 스폰/대체) |

- 필요 시 종료(Fulfilled/Failed) 처리 후 임시 바인딩 정리.

### 4.1 충족 감지

목표 진행은 **폴링보다 이벤트 훅** 우선 — 기존 게임플레이 시스템이 발생시키는 신호를 구독해 `QuestObjective.Progress`를 증가시킨다.

| Verb | 감지 훅(기존 시스템 연동) |
|------|--------------------------|
| Deliver | 대상 NPC `GiveGift`/상호작용 **+ 아이템 보유 확인 → 소모** |
| Collect | **인벤토리 변경** → 해당 아이템 보유 수가 Count 도달 |
| TalkTo | 대상 NPC **F키 상호작용** |
| GoTo | 플레이어 위치 → 대상 타일/Zone/유적 **근접 판정**(가벼운 주기 체크) |
| Kill | 전투 **사망 이벤트** → `TargetRef` 일치 시 +1 |

- 모든 `Objectives`의 `Progress ≥ Count` → `Revealed → Fulfilled`.
- 실패조건(대상 NPC 영구 사망, 대상 정착지 `Fate=Ruined`) 감지 시 `Revealed → Failed`.

> 구현 형태: 관계/연대기 시스템처럼 **`WorldData` + 매니저**에 상태를 두고, 충족 감지는 이벤트 훅으로 연결. `Deliver`는 [interactionSystem §4.5](interactionSystem.md) `GiveGift` 경로와 공유(소망 아이템이면 대폭 보상).

---

## 5. 충족 · 보상

`Fulfilled` 진입 시:

1. **호감도 대폭 상승**: `AffinityReward` → `NpcManager.ModifyPlayerRelation` ([interactionSystem §3](interactionSystem.md)).
2. **보상 지급(선택)**: `Reward.Gold`(지갑) + `Reward.ItemIds`(인벤토리).
3. **보답 연계**: 깊은 관계 보답(동료/교습/소개 등)은 [interactionSystem §4.6](interactionSystem.md).
4. **반응 대사**: `FulfillDialogue` 표시.
5. **내러티브 반영**(선택/후속): Reunion 충족 → Live 연대기 `ChronicleEvent` append, 관계자 `Sentiment` 갱신 ([WORLD_HISTORY_PLAN §6.2](../world_history/WORLD_HISTORY_PLAN.md)); 복수성 소망 → `GrudgeAgainst` 해소.

> **보상 풀 (MVP)**: 보답은 소수 **고정 아이템 풀 + 고정 골드 범위**에서만. ItemTable 화이트리스트·밸런스는 후순위(§10). 생성 요청의 `rewardBudget`도 이 풀.

> 이 단계가 [NARRATIVE_ENGINE §5 "결과 반영"](../world_history/NARRATIVE_ENGINE_DESIGN.md)의 "역사→관계→대화→선물→소망→보답→역사" 닫힌 루프의 **실제 실행 지점**이다. (생성 문서는 *의도*, 본 문서가 *메커니즘*)

---

## 6. 기록 / 영속화

### 6.1 저장 구조
- `WorldData`에 `public List<NpcWish> Wishes = new();` 추가 (Revealed/Fulfilled 포함).
- Newtonsoft 직렬화 + **마이그레이션 가드**: 구 세이브(`Wishes==null`) 로드 시 빈 리스트 보장 (R0-B 패턴).

### 6.2 저장/복원
- `Save()/Load()` ↔ `WorldData.Wishes`. Revealed의 `Progress`까지 보존(세션 간 진행 유지).
- 생성된 `RevealDialogue`/`FulfillDialogue`도 결과 캐시로 저장(재호출 없이 재현 — [NARRATIVE_ENGINE §6.3](../world_history/NARRATIVE_ENGINE_DESIGN.md)).

### 6.3 만료 / 정리 정책 (미결정 — §10)
- Fulfilled 소망의 보관 기간(회고/연대기용 vs 세이브 비대화).
- 대상 소멸 시 Revealed → Failed 자동 정리.
- NPC **영구 삭제** 시 관련 소망 정리(`NpcManager` 정리 훅에 합류).

---

## 7. UI

- **소망 표시**: 퀘스트 로그 **아님**. [interactionSystem §5](interactionSystem.md) 관계 패널에 *"이 NPC가 ~을 바람"* 한 줄 힌트.
- **진행 표시(선택)**: 패널에서 현재 목표·진행(예: 유물 회수 여부).
- **충족 연출**: `FulfillDialogue` + 호감도 상승 피드백.

> 현재 UI 50%, 관계/선물·소망 UI 미구현(README). 옛 "퀘스트 로그/추적기"는 **불필요**.

---

## 8. 대상 어휘 & 후보 산출 (생성과 공유)

> **생성 후보(candidates)와 추적 대상은 같은 어휘**다. [NARRATIVE_ENGINE §4.4](../world_history/NARRATIVE_ENGINE_DESIGN.md)의 `candidates`는 결국 §4의 추적이 감지할 수 있는 대상이어야 하므로, 이 매핑·해소를 본 문서가 정본으로 둔다.

### 8.1 후보 산출 규칙 (백본 데이터 → 유효 대상)

| 입력 데이터 (백본) | → 소망 후보 대상 | `WishKind` |
|--------------------|-----------------|-----------|
| `Sibling`·`Spouse` + `Fate=Merged/Conquered`(헤어짐) | 그 가족 NPC + 현재 정착지 | Reunion |
| 인접 `LoreFragment`(NPC 종족 일치) | 폐허 위치 / 그 안 유물 아이템 | Recover |
| `MentorOf`(스승) | 스승의 유실 기술서·유물 | Recover / ItemGift |
| 종족·`Sentiment`(고향 상실 등) | 고향을 상기시키는 특정 아이템 | ItemGift |
| `GrudgeAgainst {Kingdom}` (복수성) | 그 왕국 관련 유물/표식 | ItemGift / Recover |

### 8.2 후보 = 실현 가능 템플릿

후보는 "이미 있는 것"이 아니라 **시스템이 제공/발견 가능하다고 아는 템플릿**이다. 그래서 *생성 가능 범위 = 실현 가능 범위*가 보장된다(없는 아이템을 소망하지 않게).

- **템플릿 형태**: `{ ref, descForLLM }` — LLM엔 `ref`+`desc`만 노출.
- **부적합 제외**: ItemTable에 없거나 획득 경로가 전무한 대상은 후보에서 제외(환각·막다른 길 차단).
- **허용 아이템 풀 (MVP)**: 소망 아이템·보답 모두 **소형 고정 풀 + 고정 골드 범위**에서. 화이트리스트·밸런스는 후순위(§10).

> 이 규칙은 *생성*(후보로 LLM에 전달)과 *추적*(§4)의 **공통 기반**이라 본 문서가 정본.

---

## 9. 생성 연동 (경계)

```
[NARRATIVE_ENGINE]                          [QUEST_SYSTEM (본 문서)]
컨텍스트→후보→프롬프트→LLM→검증  ──산출──▶  NpcWish(Hidden)
                                            → (호감도 단계 도달) Revealed → 추적 → 충족 → 보상 → 기록
       ▲                                                  │
       └────────────── 떠오름 트리거는 interactionSystem §4.7 ───────┘
```

- 내러티브 엔진은 **본 문서의 스키마(§2)에 맞는 `NpcWish`(Hidden)** 를 산출하면 끝.
- 본 시스템은 *누가 만들었는지 무관*하게 그 소망을 추적·충족 처리.
- 후보 어휘(§8)는 양쪽이 공유.

---

## 10. 미결정 / 리스크

| # | 항목 | 메모 |
|---|------|------|
| 1 | **동시 Revealed 상한 / 소망 연쇄** | NPC당 1개 vs 큐/체인(다음 소망이 이어짐), 과포화 방지 |
| 2 | **만료·정리 정책** | Fulfilled 보관 기간, 대상 소멸 시 Failed, 세이브 비대화 (§6.3) |
| 3 | **충족 경로 안내 정도** | Recover의 폐허 위치를 어디까지 표시할지(스포일러 vs 막막함) |
| 4 | **GoTo/근접 판정 단위** | 타일/Zone/유적 반경 — 정밀도 vs 비용 |
| 5 | **보상 밸런스 (후순위)** | MVP 소형 고정 풀 확정. ItemTable 화이트리스트·Zone 연동·상한은 이후 |
| 6 | **실패조건 범위** | 어디까지 Failed로 볼지(대상 사망/폐허/시간초과) |
| 7 | **클래스명** | `NpcWish` vs `Wish`, `QuestReward` 공용 위치 |

---

## 11. 한 줄 요약

> **"관계가 깊어져 NPC가 털어놓은 소망을, 플레이어가 게시판도 수락도 없이 채워주고 — 5종 목표로 충족을 판정해 호감도와 보답을 돌려주며, 그 상태가 세이브에 남고 역사로 되먹임된다."**
