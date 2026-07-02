# NPC 상호작용 시스템 설계 (Interaction System)

## 개요
플레이어와 NPC 간 상호작용 행동 시스템. v1은 핵심 3종(대화·거래·선물)으로 시작하며, 성격과 관계를 종합하여 판정하고 결과에 따라 관계가 변동한다. 감정·태도·물리·관계 계열 등은 이후 확장한다.

> **설계 노트**: 플레이어 전용 사회 스탯(설득력·매력·은신 등)은 두지 않는다. 플레이어와 NPC는 가능한 한 **통합 스탯**을 공유하며, 상호작용 판정은 **대상 NPC의 성향(`NpcStatComponent`) + 플레이어와의 관계 4축**만으로 계산한다. 구현은 ECS 컴포넌트/시스템이 아니라 **서비스(`InteractionHelper`) + 매니저(`NpcManager`) 데이터** 구조로 간다 (§1 참조).

> **관계·선물 몰입 루프 (2026-06-30)**: 본 문서는 **상호작용 판정·관계 4축의 정본**이며, 그 위에 얹히는 **대화로 사연 알아가기 → 선호 유추 → 선물 → 호감도 → 보답** 몰입 루프도 본 문서(§4)가 정본으로 다룬다(호감도 단계·단계별 대화·선호 모델·유추 선물·보답). NPC가 토로하는 **특정 소망(Wish)의 메커니즘**(목표 추적·완료·보상·영속화)만 [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)가, **대화·선호·소망의 LLM 생성**은 [NARRATIVE_ENGINE_DESIGN.md](../world_history/NARRATIVE_ENGINE_DESIGN.md)가 담당. 옛 "퀘스트 수주" 게시판·계약 모델은 폐기되고, NPC의 목표는 관계가 깊어질 때 자연 발생하는 "소망"으로 대체됐다(§4.7). 한편 *전투·탐험 도전 축*(역사가 낳은 위협 — 고대신 각성/침공 등)은 본 몰입 루프와 **별개 축**으로 → [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) (단 그 §3-E "가족 구출 원정"이 `Reunion` 소망과 교차).

---

## 1. 상호작용 구조

### 상호작용 흐름
```
[플레이어 행동 선택]
        ↓
[판정 공식 계산]
  (성격 + 관계)
        ↓
[성공/실패 결정]
        ↓
[관계 변동 적용]
        ↓
[결과 텍스트 & 이벤트]
```

### 아키텍처: 서비스 + 매니저 데이터 (ECS 시스템 아님)

상호작용은 플레이어가 UI에서 대상 NPC에게 일으키는 **1회성 이산 이벤트**다. 매 프레임 다수 엔티티를 순회하는 연속 시뮬레이션이 아니므로 ECS 컴포넌트/시스템(폴링)으로 모델링하지 않는다. 대신 네 계층으로 나눈다.

| 계층 | 역할 | 구현 위치 |
|------|------|-----------|
| **데이터** | 4축 관계 보관 + 불변식(Trust 캡 등) | `NpcManager` (npcId 키 영속, ECS 컴포넌트 아님) |
| **서비스** | 판정(성공률) + 결과 산출 | `InteractionHelper` (static, 무상태) |
| **UI** | 행동 선택·표시·트리거 | `UINpcInteract` |
| **부수효과** | 전투/AI/동료/퀘스트 실행 | 기존 ECS 시스템에 위임 |

```
UI(UINpcInteract) ──선택──▶ InteractionHelper.Resolve()
                                 │ 읽기: NpcStatComponent / GetPlayerRelation
                                 │ 쓰기: NpcManager.ModifyPlayerRelation()  ← Trust 캡 등 불변식
                                 └ 위임: 전투/AI/동료/퀘스트 (기존 ECS)
                            ◀─InteractionResult(반환 struct)──┘
NpcManager._playerRelations ──Save/Load──▶ WorldData.PlayerNpcRelationList ──▶ 세이브
```

> NPC의 영속 id == 엔티티 id이므로(NPC↔NPC 호감도와 동일 규약), 서비스는 같은 id로 `NpcStatComponent`(라이브 컴포넌트)와 관계 데이터(영속)를 함께 조회한다. 상호작용은 NPC가 눈앞에 스폰돼 있을 때만 일어나므로 오프라인 NPC 조회 문제는 없다.

### 서비스 API (InteractionHelper — static·무상태)

```csharp
// UI가 성공률을 미리 표시
public static float GetSuccessRate(int npcId, InteractionType type);

// 굴림 → 관계 변동 적용 → 부수효과 위임 → UI용 결과 반환
public static InteractionResult Resolve(int npcId, InteractionType type);

// UI 표시용 반환 struct (ECS 컴포넌트 아님)
public struct InteractionResult
{
    public bool Success;
    public int AffinityDelta, TrustDelta, FearDelta, IntimacyDelta;
    public ResultKind Kind;   // 성공 / 실패 / 전투개시 / 경비호출 등
}
```

타입별 공식·결과 델타는 v1 3종(대화·거래·선물)만 `switch`로 시작하고, 밸런싱 단계(§9)에서 계수를 시트/SO로 이관한다. 이후 추가되는 타입도 같은 `switch`에 확장한다.

### 상호작용 타입
```csharp
public enum InteractionType
{
    // v1 핵심 3종 — 이후 대화 세분화·감정·태도·물리·관계 계열로 확장 예정
    Talk,       // 대화
    Trade,      // 거래 (물건 사기/팔기)
    Gift,       // 선물
}
```

---

## 2. 판정 공식 (Interaction별)

### 기본 공식 구조
```csharp
float CalculateSuccessRate(InteractionType type, int playerEntityId, int npcEntityId)
{
    // 1. 컴포넌트 가져오기 (플레이어 측 입력 항 없음 — NPC 성향 + 관계만)
    var npcStat = GetComponent<NpcStatComponent>(npcEntityId);
    var relationship = GetRelationship(playerEntityId, npcEntityId);

    // 2. 베이스 확률
    float baseRate = 50f;

    // 3. 상호작용별 가중치 적용
    float modifiedRate = baseRate + CalculateModifiers(type, npcStat, relationship);

    // 4. 최종 확률 (0~100 클램프)
    return Mathf.Clamp(modifiedRate, 0f, 100f);
}
```

> ※ 플레이어 사회 스탯을 제거하면서 각 공식의 player 항이 빠졌다. base 수치와 NPC/관계 가중치는 **추후 재밸런싱 대상**(§9 참조).

> v1 3종(대화·거래·선물)은 모두 **판정 없이 성립하는 결정형 행동**이다(항상 성공). 위 성공률 공식 프레임은 이후 추가될 **판정형 타입**(설득·위협·소매치기 등)을 위한 골격으로 남겨둔다.

### 1. Talk (대화)
**효과**: 친밀도 서서히 상승, 정보 획득 가능. 호감도 단계에 따라 NPC의 사연이 점진 공개(§4.3).

**판정**: 항상 성공 (100%)

**결과**:
- 성공: Intimacy +1, Affinity +1
- 대화 주제·호감도 단계에 따라 NPC 반응·공개 사연이 달라짐

---

### 2. Trade (거래)
**효과**: NPC 상점과 물건 사기/팔기

**판정**: 항상 성공 (100%) — 거래창 진입

**결과**:
- 상점 UI 오픈, 매매 성립
- 관계 4축 변동 없음(기본). 반복 거래로 소소한 Intimacy 상승 여부는 밸런싱 단계(§9)에서 결정
- (추후) 흥정·뇌물 등 판정형 거래 행동으로 확장

---

### 3. Gift (선물)
**효과**: 호감도 상승. 관계·선물 몰입 루프의 핵심 행동(§4).

**판정**: 항상 성공 (100%)

**결과** (선물 가치에 비례):
- 저가 (10G 이하): Affinity +3
- 중가 (10~50G): Affinity +7
- 고가 (50G 이상): Affinity +15, Intimacy +5
- NPC 선호 아이템: 추가 보너스 +10

> **선호 판정 일반화 (§4 관계·선물 루프)**: 위 "선호 아이템 +10"은 단일 보너스가 아니라, NPC의 백본 사연에서 도출된 **선호/혐오 카테고리에 따른 반응 배수**(`Loved/Liked/Neutral/Disliked`)로 일반화된다 — `Gift`가 *관계 루프의 핵심 행동*이기 때문(정본 §4.4·§4.5). **혐오 아이템은 호감도를 깎을 수도** 있다(예: 전쟁으로 가족을 잃은 NPC에게 무기류). 또한 `Gift`로 **특정 소망(Wish) 아이템**을 건네면 호감도가 대폭 상승하며, 그 충족 메커니즘은 [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)가 담당한다(§4.7).

---

## 3. 관계 변동 시스템

### 관계 데이터 (NpcManager 소유)
```csharp
// 런타임 — NpcManager가 Dictionary<int npcId, PlayerRelation> 로 보관
public struct PlayerRelation
{
    public int Affinity;    // -100 ~ +100
    public int Trust;       // 0 ~ 100
    public int Fear;        // 0 ~ 100
    public int Intimacy;    // 0 ~ 100
    public int TrustCap;    // 0 ~ 100, Trust 상한 (한번 깎이면 같이 하락)
}

// 세이브 DTO — WorldData.PlayerNpcRelationList 에 List 로 직렬화
[Serializable]
public class PlayerNpcRelationEntry
{
    public int NpcId;
    public int Affinity, Trust, Fear, Intimacy;
    public int TrustCap = 100;
}
```

저장은 NPC↔NPC 호감도와 **동일 패턴**: `NpcManager._playerRelations`(런타임 Dict) ↔ `WorldData.PlayerNpcRelationList`(세이브). 키는 npcId 단일(1명의 플레이어 → 다수 NPC라 페어 키 불필요). NPC **영구 삭제** 시 `NpcManager`의 기존 정리 훅에서 함께 제거하고, 청크 unload(일시 비활성)에는 유지한다.

### 매니저 API (NpcManager, 플레이어 관계 region)
```csharp
public PlayerRelation GetPlayerRelation(int npcId);              // 없으면 기본값(전부 0, TrustCap=100)
public bool HasMetPlayer(int npcId);
public void ModifyPlayerRelation(int npcId, int dAff, int dTrust, int dFear, int dIntimacy);
public void SetPlayerRelation(int npcId, PlayerRelation rel);    // 디버그/특수 세팅
public List<PlayerNpcRelationEntry> SavePlayerRelations();
public void LoadPlayerRelations(List<PlayerNpcRelationEntry>? list);
```

### 변동 규칙 (ModifyPlayerRelation 내부 — 불변식)
변동 로직은 매니저에 둔다. 어떤 호출자(서비스·이벤트·퀘스트 보상)든 동일 규칙(Trust 비대칭·캡)을 강제로 따르게 하기 위함.
```csharp
public void ModifyPlayerRelation(int npcId, int dAff, int dTrust, int dFear, int dIntimacy)
{
    var rel = GetPlayerRelation(npcId);

    // 1. Affinity
    rel.Affinity = Mathf.Clamp(rel.Affinity + dAff, -100, 100);

    // 2. Trust (감소는 즉시 / 증가는 절반, 상한은 TrustCap)
    if (dTrust < 0)
    {
        rel.Trust = Mathf.Max(0, rel.Trust + dTrust);
        rel.TrustCap = Mathf.Max(rel.Trust, rel.TrustCap + dTrust / 2); // 큰 폭 하락 시 상한도 영구 하락
    }
    else
    {
        rel.Trust = Mathf.Min(rel.TrustCap, rel.Trust + dTrust / 2);
    }

    // 3. Fear / Intimacy
    rel.Fear = Mathf.Clamp(rel.Fear + dFear, 0, 100);
    rel.Intimacy = Mathf.Clamp(rel.Intimacy + dIntimacy, 0, 100); // 일반 상호작용 외 시간 경과로도 증가

    SetPlayerRelation(npcId, rel);
}
```

### Trust 회복의 어려움
- **Trust 감소**: 즉시 적용 (배신 시 -80)
- **Trust 회복**: 느리게 증가 (사과 성공 시 +5)
- **Trust 최대치**: 한번 감소하면 최대치도 감소
  - 예: Trust 80에서 20으로 감소 → 회복해도 최대 60까지만

---

## 4. 관계·선물 몰입 루프 (호감도 단계 · 대화 · 선호 · 유추 선물 · 보답)

> 30가지 상호작용(§1) 위에 얹히는 **몰입 레이어**다 — 대화로 NPC의 사연을 알아가고, 그 사연에서 *무엇을 주면 좋아할지* 헤아려 선물하고, 호감도가 오르면 NPC가 보답한다. **더 깊은 지역 진출을 강제하는 진행 엔진이 아니다**(진출 동기는 메인 이벤트 등 별도 시스템 몫). 본 절은 §3 관계 4축 위에서 동작한다. 대화·선호·소망의 *LLM 생성*은 [NARRATIVE_ENGINE_DESIGN.md](../world_history/NARRATIVE_ENGINE_DESIGN.md), NPC가 토로하는 *특정 소망의 메커니즘(추적·완료·보상)*은 [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)가 담당.

### 4.1 핵심 루프

```
                       ┌──────────────────────────────────────────┐
                       ▼                                          │
[대화] ─── 호감도가 오를수록 ───▶ [더 사적인 사연 공개]            │
   │        (백본: 관계·Sentiment·연대기 점진 노출)               │
   ▼                                                              │
[유추] ─ 사연에서 "이걸 주면 좋아하겠다" 헤아림                    │
   │                                                              │
   ▼                                                              │
[선물] ─ 선호 일치도 × 가치 → 호감도 변동 ───────────────────────┘
   │
   ├──▶ (호감도 단계 도달) NPC가 [특정 소망] 토로 ─▶ 충족(→ QUEST_SYSTEM) 시 대폭↑ + 보답
   └──▶ (호감도 임계 도달) NPC가 [보답] (아이템/동료/교습/소개 …)
```

| 결 | 빈도/강도 | 동기 | 본문 |
|----|----------|------|------|
| **유추 선물** | 지속·잔잔(소~중폭) | 대화로 헤아린 선호 | §4.5 |
| **특정 소망** | 이벤트성·대폭 | NPC가 토로한 구체적 갈망 | §4.7 → [QUEST_SYSTEM](QUEST_SYSTEM_DESIGN.md) |

> 두 결 모두 **NPC의 백본(역사·관계·Sentiment)에서 자연 발생**한다. 선호도 소망도 임의 태그가 아니라 *그 NPC가 살아온 사연*에서 도출되므로, "이 세계에서만 말이 되는" 관계를 빚는다.

### 4.2 호감도 단계 (AffinityTier)

대화 심도·소망·보답의 게이트. `Affinity`(§3) 기준, `Intimacy`가 보조.

```csharp
public enum AffinityTier { Stranger, Acquaintance, Friend, Confidant }
// 경계(잠정): <20 / 20–49 / 50–79 / 80+
```

| 단계 | 호감도(잠정) | 공개되는 백본 층 | 선호/소망 |
|------|-------------|-----------------|----------|
| **Stranger** | <20 | 직업·마을 표면 잡담 | 선호 힌트 거의 없음 |
| **Acquaintance** | 20–49 | 종족·취향 일반론 | 거친 선호 카테고리 암시 |
| **Friend** | 50–79 | 개인 관계·과거 일부(`GetRelations`·일부 `Sentiment`) | 구체적 선호 단서, **첫 소망 가능** |
| **Confidant** | 80+ | 트라우마·핵심 `Sentiment`·연대기 사건(`OriginEventId`) | 깊은 소망, 보답 |

### 4.3 단계별 대화 (사연 점진 공개 → 유추 단서)

> 사람 관계처럼, 처음부터 속을 다 보이지 않는다. 호감도가 오를수록 NPC가 더 사적인 이야기를 꺼내고, 그 이야기가 곧 *선호·소망의 단서*가 된다. 이게 "유추"의 동력이다.

- 게이트는 `Affinity` 중심, `Intimacy` 보조 — 친밀 대화(`Talk`)가 사적 대화를 앞당길 수 있다(추후 추가될 비밀 고백·정보 캐묻기 등 친밀도 상호작용도 동일 역할).
- 각 단계의 대사는 **온디맨드 생성 + 결과 캐시**([NARRATIVE_ENGINE §6.3](../world_history/NARRATIVE_ENGINE_DESIGN.md)). 단계가 오를 때 새 대화 토픽이 해금된다.
- **시스템은 정답을 말하지 않는다.** 플레이어가 대화에서 "이 사람은 고향을 그리워하는구나 → 엘프 유물이면 좋아하겠다"를 스스로 잇는다. (단서를 충분히 흘리되 정답을 직접 말하지 않게 하는 보정은 §9 밸런스 — 너무 모호하면 좌절, 너무 친절하면 유추 재미 소실.)

### 4.4 선호 (Preference) — 백본 유래, 정적 태그 아님

선호는 NPC에 박아둔 고정 태그가 아니라 **백본 데이터에서 도출**한다. 그래야 대화로 드러난 사연과 *정합*하고("고향을 잃은 엘프"가 엘프 유물을 좋아함), 유추가 정직해진다.

```csharp
public struct GiftReaction          // 선물 시 시스템이 산출
{
    public ReactionTier Tier;       // Loved / Liked / Neutral / Disliked
    public int AffinityDelta;       // §2 Gift 가치 효과에 배수/보정
    public string ReactionDialogue; // NPC 반응 대사(생성/캐시)
}
public enum ReactionTier { Loved, Liked, Neutral, Disliked }
// 선물 카테고리 — 닫힌 어휘(아이템 테이블 태그와 매핑)
public enum GiftCategory { Food, Craft, Gem, Relic, Lore, Weapon, Trinket, Flower, Drink, ... }
```

**선호 도출 규칙 (백본 → 선호↑/혐오↓)** — 생성([NARRATIVE_ENGINE §4.4](../world_history/NARRATIVE_ENGINE_DESIGN.md))과 공유:

| 입력 데이터 (백본) | → 선호/혐오 |
|--------------------|------------|
| 종족(Race) | Elf→`Lore`/`Flower`↑, Dwarf→`Gem`/`Craft`↑ (문화) |
| `Sentiment: LostFamilyToWar` | 고향/추모 `Relic`/`Lore`↑, 전쟁 상기 `Weapon`↓ |
| `Sentiment: ProudOfKingdom` | 소속 왕국 상징물↑ |
| 관계 `MentorOf`(스승) | 기술서·`Lore`↑ |
| 8성향 `Greed`↑ / `Pride`↑ | `Gem`/고가품↑ / 명예 `Trinket`↑·값싼 선물 싫어함 |
| 직업(대장장이/농부 …) | `Craft`·광석↑ / `Food`·씨앗↑ |

### 4.5 유추 선물 (Tier 1) — `Gift` 확장

§2 `Gift`의 *가치 효과* 위에 **선호 일치 판정**을 얹는다.

1. 플레이어가 임의의 아이템 선물 (§2 `Gift` 진입점)
2. 시스템이 §4.4 규칙으로 **`GiftReaction` 산출**(`Loved/Liked/Neutral/Disliked`)
3. **호감도 변동** = 가치 효과(저/중/고가) × 반응 배수 + 관계 맥락 보너스
   - 예: `Loved` ×2.0, `Liked` ×1.3, `Neutral` ×1.0, **`Disliked` 음수**(전쟁으로 가족 잃은 NPC에게 무기류 등)
   - §2 `Gift`의 "선호 아이템 +10"은 이 반응 판정으로 **일반화**된다
4. **반응 대사** → 유추가 맞았는지 체감
5. `ModifyPlayerRelation`(§3) 반영 (불변식은 §3)

> 같은 아이템도 NPC마다 반응이 다르다(선호가 백본 유래라). 이 *차이*가 "이 사람을 알아가는" 재미의 핵심.

### 4.6 보답 (NPC → 플레이어)

깊어진 관계에 NPC가 먼저 베푼다 — **순수한 관계 보상**(진출 게이트가 아님).

| 트리거 | 보답 |
|--------|------|
| 호감도 임계(Friend/Confidant 진입) | 아이템/골드, NPC 특산물 |
| 소망 충족 | §4.7 |
| (확장) 깊은 신뢰 | 동료 합류·교습·소개 등 해금 (추후 추가될 관계·협력 타입) |

- 무한 파밍 방지 위해 **쿨다운/임계 1회성**.

### 4.7 특정 소망 (Tier 2) — 떠오름 → QUEST_SYSTEM 핸드오프

NPC가 관계가 깊어질 때 백본 사연에서 비롯된 **구체적 소망**을 토로한다. 이는 구조적으로 **퀘스트**이므로, *떠오름·인지*는 본 문서가, *메커니즘(목표 추적·완료 판정·보상·상태·영속화)*은 [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)가 담당한다.

1. **떠오름**: 호감도가 `Friend`↑에 이르고 깊은 대화 비트에서 NPC가 소망을 토로(예: "가문 인장이 옛 숲 신전에 봉인돼 있소…").
2. **인지**: 퀘스트 게시판/수락 버튼 없음. 관계 패널에 *"이 NPC가 ~을 바람"* 정도로 은근히 표시(§5). 채워줄지는 플레이어 자유.
3. **충족·보상**: 충족 판정·보상 지급은 [QUEST_SYSTEM](QUEST_SYSTEM_DESIGN.md). 충족 시 **호감도 대폭 상승** + 보답(§4.6) + 서사 반영이 본 루프로 되먹임.

> **표면 UX는 끝까지 관계 프레이밍**이다 — "퀘스트를 받는다"가 아니라 "관계가 깊어져 그 사람의 갈망을 알게 되고 채워준다".

---

## 5. 상호작용 UI

### 대화 창
```
┌─────────────────────────────────────────────┐
│  ○○○ (대장장이, Lv 12)                      │
│  호감도: ████████░░ (75)                    │
│  신뢰도: ██████░░░░ (60)                    │
│  ────────────────────────────────────────── │
│  "무엇을 도와드릴까요?"                     │
│  ────────────────────────────────────────── │
│  [대화]      [거래]      [선물]              │
│  ────────────────────────────────────────── │
│  [나가기]                                    │
└─────────────────────────────────────────────┘
```

### 판정 결과 UI

> v1 3종(대화·거래·선물)은 판정 없이 즉시 성립하므로, 아래 성공률·주사위 UI는 **추후 추가될 판정형 행동**(설득·위협 등)용 골격이다.

```
┌─────────────────────────────────────┐
│  행동 판정 중...                    │
│  ──────────────────────────────── │
│  성공률: 75%                        │
│  ██████████████████████████████     │
│  ──────────────────────────────── │
│  [주사위 굴리기]                    │
└─────────────────────────────────────┘

→ 성공 시:
┌─────────────────────────────────────┐
│  성공!                              │
│  ──────────────────────────────── │
│  ○○○: "알겠어요, 도와드릴게요."    │
│  ──────────────────────────────── │
│  신뢰도 +3, 호감도 +2               │
└─────────────────────────────────────┘
```

---

## 6. 특수 상호작용 (컨텍스트 기반)

### 상황별 추가 행동
```
전투 중:
  - [도와주기]: 적 처치 시 Affinity +10
  - [방치하기]: NPC 사망 시 Affinity -20

위험 상황:
  - [구출하기]: 함정/몬스터로부터 구출, Affinity +20

개인 목표:
  - [도와주기]: NPC 개인 목표 달성 지원, Intimacy +15
```

---

## 7. 구현 우선순위

### Phase 1: 기본 상호작용
- [ ] PlayerRelation 데이터 + NpcManager 관계 API (Get/Modify/Save/Load + 영구삭제 정리 훅)
- [ ] 영속 배선 (WorldData.PlayerNpcRelationList ↔ DataManager Save/Load, Version 증가)
- [ ] InteractionHelper 서비스 (GetSuccessRate / Resolve → InteractionResult)
- [ ] 3가지 기본 행동 (Talk, Trade, Gift)
- [ ] 상호작용 UI (UINpcInteract — 대화 창)

### Phase 2: 판정 시스템
- [ ] 성공률 계산 공식 구현 (NPC 성향 + 관계 4축)
- [ ] 주사위 굴리기 UI
- [ ] 결과 텍스트 시스템

### Phase 3: 관계 변동
- [ ] 관계 변동 로직
- [ ] Trust 회복 난이도
- [ ] 관계 히스토리 기록

### Phase 4: 고급 상호작용
- [ ] 추가 상호작용 타입 구현 (대화 세분화·감정·태도·거래(흥정/뇌물)·물리·관계 계열)
- [ ] 특수 상호작용 (컨텍스트)
- [ ] 상호작용 결과 이벤트

### Phase 5: 피드백
- [ ] 상호작용 애니메이션
- [ ] 사운드 효과
- [ ] 관계 변화 알림

---

## 8. 테스트 시나리오

### 테스트 1: 대화 (Talk)
1. NPC에게 다가가기
2. "대화" 선택
3. 대화 텍스트 확인 (호감도 단계에 따른 사연 노출)
4. Intimacy +1, Affinity +1 확인

### 테스트 2: 거래 (Trade)
1. "거래" 선택
2. 상점 UI 오픈 확인
3. 물건 매매 성립 확인

### 테스트 3: 선물 (Gift)
1. "선물" 선택 → 아이템 선택
2. 선물 가치별 Affinity 상승 확인 (저/중/고가)
3. NPC 선호/혐오 반응 배수 확인 (Loved/Disliked 대사)

---

## 9. 밸런스 고려사항

### 성공률 범위
> v1 3종(대화·거래·선물)은 항상 성공하므로 아래는 **판정형 타입 추가 시** 적용되는 기준이다.

- **너무 쉬움 (80% 이상)**: 긴장감 부족
- **적정 (50~70%)**: 선택의 무게감
- **너무 어려움 (30% 이하)**: 좌절감

> 플레이어 사회 스탯 제거로 판정이 NPC 성향 + 관계 4축에만 의존한다. 따라서 동일 NPC·동일 관계 상태에서는 성공률이 고정되므로, 관계를 쌓아 성공률을 끌어올리는 흐름이 자연스럽도록 base/가중치를 튜닝한다.

### 관계 회복 비용
- **Affinity 회복**: 비교적 쉬움 (선물, 칭찬)
- **Trust 회복**: 매우 어려움 (시간 + 여러 번의 사과/도움)
- **Intimacy 증가**: 장기적 상호작용 필요

### 리스크 vs 보상
- **안전한 행동** (대화·선물): 작고 잔잔한 보상
- **위험한 행동** (추후 추가될 거짓말·뇌물·소매치기 등): 큰 보상 but 실패 시 큰 페널티

---

**Last Updated**: 2026-07-02
**Status**: 설계 완료, 구현 대기 중 (플레이어 사회 스탯 제거 — 판정은 NPC 성향 + 관계 4축 / 서비스 + 매니저 데이터 구조, ECS 아님. §4 관계·선물 몰입 루프 편입 — 소망 메커니즘만 QUEST_SYSTEM. **InteractionType을 v1 핵심 3종 Talk·Trade·Gift로 축소 — 감정·태도·물리·관계 계열은 이후 확장**)
**Dependencies**: NPC 시스템, 관계 시스템, 대화 시스템(NARRATIVE_ENGINE 생성), 소망 추적(QUEST_SYSTEM)
