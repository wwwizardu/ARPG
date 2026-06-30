# 내러티브 엔진 (LLM 대화·선호·소망 생성) — 설계

> **목표**: 이미 구현된 **절차적 역사 + NPC 관계** 백본을 *배경 컨텍스트*로 삼아, LLM이 **단계별 대화·선호 근거·소망(Wish)·세부 디테일**을 생성하는 레이어를 설계한다. 이 게임의 **핵심 재미(몰입) 검증 1순위**이며, 마을 자가성장·몬스터 침공보다 먼저 만든다.
>
> **방향 전환 (2026-06-30)**: 플레이어↔NPC 루프가 *퀘스트 계약*에서 **대화·선물·호감도** 관계로 바뀌었다([QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)). 그래서 본 엔진의 **주력 생성물도 "퀘스트 목표(objectives/verbs)"에서 "호감도 단계별 대화 + 선호 근거 + 특정 소망"으로 이동**한다. 옛 "대사 LLM 적용 미결정"은 이 모델에서 **대화가 곧 핵심 메커니즘(선호 힌트 전달자)**이므로 *적용 방향으로 해소*된다(§8 #9).
>
> **상태 (2026-06-30)**: 🟦 설계 초안. 백본(R0~R4)은 코드 완료(Unity 검증 전). **LLM 토대(`LLMManager`/`LLMClient`, Groq/Llama)도 이미 존재** — 본 엔진은 신규 클라이언트를 만들지 않고 이를 확장한다(§6).
>
> **경계**: 본 문서는 대화·선호·소망 *생성*(컨텍스트→프롬프트→LLM→검증)까지. 생성된 대화·선호·소망의 *동작·판정·기록*(호감도 단계·선물 반응·소망 충족·보답·세이브)은 [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md)가 담당하며, **데이터 모델의 정본도 그쪽**이다.
>
> **선행/관련 문서**:
> - [QUEST_SYSTEM_DESIGN.md](QUEST_SYSTEM_DESIGN.md) — 관계·선물·소망 동작·판정·기록(데이터 모델 정본)
> - [gameOverview.md](gameOverview.md) — 거시 진행에서 이 엔진의 위치 (핵심 재미)
> - [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md) — 입력 데이터(연대기/관계)의 구현 현황
> - [PROCEDURAL_HISTORY_DESIGN.md](PROCEDURAL_HISTORY_DESIGN.md) — 정착지 연대기/왕국/정복
> - [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md) — NPC 개인 관계/Sentiment
> - [MAP_DESIGN.md](MAP_DESIGN.md) — 유적 단편(종족·운명·바이옴)
> - [interactionSystem.md](interactionSystem.md) — NPC 상호작용 + **관계·선물 몰입 루프**(§4): 대화·선호·유추 선물·보답의 *런타임 정본*(대사 소비처)
> - [WORLD_EVENT_DESIGN.md](WORLD_EVENT_DESIGN.md) — 도전 이벤트(역사가 낳은 위협). 본 엔진이 그 *서술 스킨*(소문·명문·보스 대사)을 공급

---

## 1. 목표와 검증 가설

### 1.1 한 줄 정의

> **"맵에 흩뿌려진 절차적 역사와, 그 역사가 빚어낸 NPC들의 관계를 LLM에 컨텍스트로 먹여, *이 세계에서만 말이 되는* 대화·선호·소망을 즉석 생성한다 — 플레이어가 그 사연을 알아가며 관계를 맺도록."**

### 1.2 검증 가설 (이 엔진이 증명해야 할 것)

| # | 가설 | 무엇으로 판정 |
|---|------|--------------|
| H1 | 역사·관계를 배경으로 한 대화·소망이 **"이 NPC의 사연"으로 느껴진다** | 플레이어가 *대화만 듣고* 그 NPC가 무엇을 좋아할지/원하는지 한 문장으로 유추 가능한가 |
| H2 | 생성물이 **세계관과 충돌하지 않는다** (죽은 NPC가 말 걸기, 없는 사건 언급 등 환각 없음) | 정합성 가드 통과율, 수동 검수 오류율 |
| H3 | 같은 컨텍스트라도 **반복 플레이에서 신선**하다 | 동일 NPC 재방문 시 체감 중복도 |
| H4 | 호출 **비용/지연**이 게임플레이를 해치지 않는다 | 대화·소망 1건당 토큰/시간/원가, 캐시 적중률 |

> **비목표 (이 문서 범위 밖)**: 메인 스토리 라인, 풀 음성 대사, 분기 시나리오 트리. 이 엔진은 *창발(emergent)* 콘텐츠 생성기이지 작가가 쓴 고정 서사가 아니다.

---

## 2. 입력 컨텍스트 — 이미 구현된 데이터

LLM에 먹일 배경은 전부 백본(R0~R4, [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md))이 만든 **기존 데이터**다. 신규 시뮬은 필요 없고, "어떻게 추려서 프롬프트로 만드느냐"만 설계한다.

### 2.1 세계 역사 (연대기)

| 소스 | 타입 | 내용 |
|------|------|------|
| 연대기 사건 | `ChronicleEvent { EventId, Year, Type, SubjectSettlementId, ObjectSettlementId, KingdomId }` | Trade/Skirmish/War/Conquer/Alliance/Merge/Ruin/KingdomFound/KingdomJoin/Promotion |
| 왕국 | `KingdomData { KingdomId, Name, FoundingRace, CapitalSettlementId, MemberSettlementIds }` | 다종족 제국, 지배/피지배 종족 |
| 정착지 | `SettlementSnapshot { SettlementId, Race, RegionCoord, Population, KingdomId, Fate, ... }` | Fate ∈ Alive/Ruined/Merged/Conquered |

### 2.2 NPC 개인 관계

| 소스 | 타입 | 내용 |
|------|------|------|
| 관계 엔트리 | `NpcRelationEntry { FromEntityId, ToEntityId, Type, OriginEventId }` | ParentOf/MentorOf/GrudgeAgainst/Spouse/Sibling/Friend/Rival |
| 감정 | `NpcSentiment { Type, TargetSettlementId, OriginEventId }` | LostFamilyToWar/ProudOfKingdom/ResentsMerge/ResentsKingdom … |
| 호감도 | `NpcManager.GetAffinity(a, b)` | −100~+100 수치 |
| 성격 | `NpcStatComponent` (8성향) | Friendliness/Pride/Loyalty/Courage/Greed/Patience … (활성 NPC만) |

> `OriginEventId`가 관계/감정을 **연대기 사건과 연결**하는 핵심 키다 ("30년 전 {상대마을}과의 전쟁에서 가족을 잃음" → 그 사건이 곧 LLM 컨텍스트).

### 2.3 장소 (유적 단편)

| 소스 | 타입 | 내용 |
|------|------|------|
| 유적 단편 | `LoreFragmentComponent { RaceOrigin, FateKeyword, TileBiome, Level, ... }` | 종족 × 운명(burned/plagued/sealed…) × 바이옴 |

### 2.4 정착지↔마을 링크

`VillageData.SettlementId`로 런타임 마을과 연대기 정착지를 잇는다 → "지금 이 마을 = 연대기상 어느 정착지" 조회 (R1 산출물).

---

## 3. LLM 생성 대상

| 대상 | 정의 | 주요 입력 | 빈도/비용 |
|------|------|----------|----------|
| **세부 디테일** | NPC 이름·별명·말투, 정착지 표시명, 유적 명문 1줄 등 *짧은 채움* | 종족/성격/사연 키워드 | 高빈도·低비용 (배치/캐시) |
| **단계별 대화** ⭐ | 호감도 단계가 오를수록 더 사적인 사연(관계·Sentiment·연대기)을 회고하는 NPC 대사 — *선호 힌트의 전달자* | 대상 NPC 관계망 + `OriginEventId` 사건 + 현재 호감도 단계 | 中빈도·中비용 (단계별 캐시) |
| **선호 근거** | NPC가 *무엇을 좋아할지*의 근거(선호/혐오 `GiftCategory`) + 선물 반응 대사 | 종족·`Sentiment`·관계·성격·직업 ([interactionSystem §4.4](interactionSystem.md)) | 中빈도·低비용 |
| **소망 (Wish)** | NPC 개인 사연(원한·가족사·상실)에서 비롯된 *구체적 갈망*(특정 아이템/재회/유물 회수) | NPC 관계망 + 관련 연대기 + 인접 NPC/마을/폐허 | 低빈도·高비용 |
| **이벤트** | 마을/지역 단위로 벌어지는 상황(분쟁·재회·복수·교역 제안 등) | 마을 NPC 집합 + 연대기 + 파벌 | 低빈도·高비용 |

> 비용이 높을수록 더 적게, 더 좋은 모델/더 많은 컨텍스트로 생성한다 (§6 모델 티어링).
>
> ⭐ **대화가 주력이다.** 이 모델에선 플레이어가 *대화에서 선호를 유추*하므로([interactionSystem §4](interactionSystem.md)), 대화 품질이 곧 핵심 재미다. 옛 "대사 LLM 미결정"(§8 #9)은 적용 방향으로 해소.
>
> **이벤트(다수 NPC)는 MVP 후순위.** 핵심 재미 검증은 *NPC 1명과의 대화·선호·소망*부터. (도전 이벤트의 콘텐츠 카탈로그·발화는 → [WORLD_EVENT_DESIGN.md](WORLD_EVENT_DESIGN.md); 본 엔진은 그 *서술*만 담당)

### 3.1 출력 스키마 — QUEST_SYSTEM 참조

**대화·선호·소망 데이터 모델·`GiftCategory`/`WishKind` enum·영속화의 정본은 [QUEST_SYSTEM_DESIGN.md §3](QUEST_SYSTEM_DESIGN.md)** 다. 본 엔진은 그 형식에 맞는 산출물을 내놓는 게 책임이며, 여기서는 *생성기가 채워야 할 부분*만 요약한다.

**① 단계별 대화** — 현재 호감도 단계(`AffinityTier`)에서 공개해도 되는 백본 층만 회고하는 자유 서술. *닫힌 부분 없음*(순수 스킨)이나, 단계가 허용한 사연·`OriginEventId` 사건에만 근거해야 함(환각 금지).

**② 선호 근거** — 닫힌 어휘 선택 + 서술:

| 층 | 필드 | 생성기 역할 |
|----|------|------------|
| **기계 골격** | 선호/혐오 `GiftCategory[]`, 근거 `ContextEventIds[]` | 닫힌 카테고리·백본 안에서 **선택**(§4.4) |
| **내러티브 스킨** | 선물 반응 대사(`Loved/Liked/Neutral/Disliked`별) | 자유 **서술** |

**③ 소망 (Wish)** — 옛 "퀘스트"의 2층 구조를 계승(골격은 후보·예산 안에서 *선택*, 스킨은 *서술*):

| 층 | 필드 | 생성기 역할 |
|----|------|------------|
| **기계 골격** | `Kind`(WishKind), `TargetItemId`/`TargetRef`, `AffinityReward`, `Reward{Gold,ItemIds}`, `ContextEventIds[]` | 닫힌 어휘·후보·예산 안에서 **선택**(§4.4) |
| **내러티브 스킨** | `RevealDialogue`(소망 토로), `FulfillDialogue`(충족 반응) | 자유 **서술**(골격과 정합) |

- **`WishKind`**는 닫힌 enum `{ ItemGift, Recover, Reunion }`(의미·충족 감지는 [QUEST_SYSTEM §2.1·§4](QUEST_SYSTEM_DESIGN.md)), **`GiftCategory`**도 닫힌 enum(선호 도출은 [interactionSystem §4.4](interactionSystem.md)). LLM은 이 밖을 못 쓴다.
- 소망 대상(`TargetItemId`/`TargetRef`)은 **후보(candidates) 안에서만** 고른다(§4.4·[QUEST_SYSTEM §8](QUEST_SYSTEM_DESIGN.md)).
- 생성기가 내놓는 실제 JSON 모양은 §4.4(생성 계약)·§4.7(예시) 참고.

---

## 4. 생성 파이프라인

```
[트리거] → [컨텍스트 수집] → [컨텍스트 압축/요약] → [프롬프트 조립] → [LLM 호출(구조화 출력)]
        → [정합성 가드 검증] → [엔티티/세이브에 반영] → [플레이어에게 제시]
```

### 4.1 트리거 (언제 생성하나)

| 트리거 | 생성물 | 비고 |
|--------|--------|------|
| NPC 상호작용(F키) | 단계별 대화 + 선호 근거 | 캐시 우선, 미스 시 생성 |
| 호감도 단계 상승(특히 Friend↑) | 더 깊은 대화 해금 + (조건 충족 시) 특정 소망 떠오름 | 단계별 캐시 |
| 마을 첫 진입 | 마을 단위 이벤트 후보 | 진입 시 1회, 결과 캐시 |
| NPC 스폰(backfill 직후) | 이름·말투 등 세부 디테일 | 배치 생성 가능 |
| 유적 발견 | 명문 1줄 / 소망 시드(Recover 후보) | 결정론 좌표와 묶어 캐시 |
| (후속) 게임 시간 경과 | 관계 변동 기반 신규 이벤트 | Phase 2+ |

> **사전 생성 vs 온디맨드**: 비용·지연을 줄이기 위해 *세부 디테일·명문·선호 근거*는 사전/배치 생성, *대화·소망·이벤트*는 트리거 시 온디맨드 + 결과 캐시. (§6)

### 4.2 컨텍스트 수집 — "관련된 것만"

전체 연대기를 다 넣지 않는다. 대상 NPC/마을 기준으로 **관련 서브그래프만** 추린다:

1. 대상 NPC의 `GetRelations(npcId)` → 가족/원한/사제/라이벌 (1-홉, 필요 시 2-홉)
2. 각 관계의 `OriginEventId` → `ChronicleEvent` 역참조 (사건 본문)
3. 대상 NPC `Sentiments` + 소속 `KingdomData` (지배/피지배 여부)
4. 현재 마을 `SettlementSnapshot`(Fate/Race/Population) + 인접 마을 1~2개
5. 성격 `NpcStatComponent` 8성향 (말투·동기 톤)

→ 토큰 예산에 맞춰 **사건은 최신/중대 순 상위 N개**로 컷.

### 4.3 컨텍스트 압축

연대기 사건을 LLM에 그대로 던지지 않고 **사람이 읽는 1줄**로 평탄화한 뒤 넣는다 (디버그 덤프 헬퍼 재사용 — [WORLD_HISTORY_PLAN §4.2](WORLD_HISTORY_PLAN.md)).

```
ChronicleEvent(Type=Conquer, Year=412, Subject=S17, Object=S23, Kingdom=K3)
  → "412년, K3 왕국(드워프)이 엘프 정착지 S23을 정복해 편입했다."
```

### 4.4 생성 계약 (C 방식: 시스템이 후보 제공 → LLM이 선택·서술)

대상·보상(기계 골격)은 **시스템이 후보·예산으로 한정**하고, LLM은 *그 안에서 고르고 사연을 쓴다*. 환각의 원천(없는 대상·아이템)을 입력 단계에서 차단하는 게 핵심. 아래는 **소망(Wish) 생성**(가장 비싼 산출물) 기준이며, 단계별 대화·선호 근거는 같은 틀의 변형이다(끝에 정리).

**① 생성 요청 (시스템 → LLM, 내부 데이터)** — C# 프롬프트 빌더가 §4.2~4.3 수집물에서 구성:
- `context` : 관계 서브그래프 + 연대기 1줄들 + Sentiment (= 갈망의 뿌리)
- `wishKinds` : `[ItemGift, Recover, Reunion]` (닫힌 enum)
- `candidates` : **실현 가능 템플릿** 목록 `{ ref, desc }` — 소망 대상이 될 수 있는 것([QUEST_SYSTEM §8.2](QUEST_SYSTEM_DESIGN.md)). 헤어진 가족·인접 폐허/유물·고향 상기 아이템 등을 C# 규칙이 추림. LLM엔 `ref`+`desc`만 노출
- `rewardBudget` : 보답 골드 범위 + 허용 아이템 풀 (**MVP는 소형 고정 풀** — [QUEST_SYSTEM §8.2](QUEST_SYSTEM_DESIGN.md))

**② 프롬프트 (실제 전송 텍스트)** — ①을 평문으로 렌더 → `messages` 2개로 `RequestWithContextAsync` 호출.

`system` (규칙 — 거의 고정, 캐시 후보):
```
너는 이 판타지 세계에서 이 NPC의 사연을 아는 작가다.
- SETTING/NPC/HISTORY에 없는 인물·사건·장소·아이템을 지어내지 마라.
- 소망은 이 NPC의 사연(HISTORY/SENTIMENT/RELATIONS)에서 자연스럽게 비롯돼야 한다.
- wish.kind는 WISH_KINDS 안에서만 고른다.
- wish.target은 CANDIDATES의 ref 중에서만 고른다(새 ref 금지).
- reward.gold는 REWARD_BUDGET 범위 안. 서술은 한국어.
- 아래 JSON 스키마만 출력(다른 문장 금지):
{ "kind","target","affinityReward","reward":{"gold","items"},
  "revealDialogue","fulfillDialogue","usedEventIds" }
```
`user` (이번 NPC 데이터 — 매번 다름): `[NPC] [HISTORY] [RELATIONS] [SENTIMENT] [WISH_KINDS] [CANDIDATES] [REWARD_BUDGET]` 섹션을 평문으로 나열 + "위 정보로 이 NPC가 품을 법한 소망 1개를 JSON으로 생성하라."

**③ LLM 출력 → 검증 → 조립**: JSON 텍스트 수신 → 파싱 → §4.6 가드(`kind∈enum` / `target∈candidates` / `reward≤budget`) → `NpcWish`(Revealed) 조립.

**단계별 대화·선호 근거 계약 (같은 틀의 변형)**:

| 산출물 | candidates/budget | 추가 입력 | 출력 | 가드 |
|--------|-------------------|----------|------|------|
| 단계별 대화 | 없음 | 현재 `AffinityTier` + 그 단계 허용 백본 층 | 대사 텍스트 + `usedEventIds` | `usedEventIds∈context`, 단계 초과 사연 금지 |
| 선호 근거 | `allowedCategories`(GiftCategory enum) | 종족·Sentiment·성격·직업 | 선호/혐오 `GiftCategory[]` + 반응 대사 | `category∈enum` |

> 정적 부분(세계관·종족·사건 타입 사전·스키마)은 매 호출 반복되므로 캐싱 대상(§6.3). 한 바퀴 전체 예시는 §4.7.

### 4.5 구조화 출력

`NpcWish`(+ 선호 근거)에 직접 매핑하려면 구조화 출력이 필요하다(단계별 대화는 자유 텍스트라 해당 없음). 기존 `LLMClient`는 **자유 텍스트만 반환**하므로(§6.2), **OpenAI 호환 `response_format`(JSON 모드)/function calling 우선 → 미지원·불안정 시 프롬프트로 JSON 강제 + 파싱 + 스키마 검증** 경로로 받는다. 스키마 위반 시 재시도(§4.6).

### 4.6 정합성 가드 (환각/충돌 차단 — H2)

생성 결과를 반영 전에 검증:

- [ ] 참조한 NPC/마을 ID가 **실재**하고 **생존**(Fate≠Ruined, NPC 미사망)하는가
- [ ] 언급한 사건이 `ContextEventIds`에 실제로 있는가 (없는 역사 지어내기 차단)
- [ ] 보상이 허용 범위(아이템 테이블/골드 상한) 내인가
- [ ] 금칙어/길이/언어 검사
- → 실패 시: 재생성 1회 → 재실패 시 **규칙 기반 폴백 템플릿**으로 대체(엔진이 멈추지 않게)

### 4.7 워크드 예시 (데이터 → 대화·선호·소망 한 바퀴)

> 구조 검증용 **대표 샘플**(라이브 세이브 아님). "데이터만으로 *대화에서 선호를 유추*하고 그럴듯한 소망이 서는가"(H1)를 사람(=LLM 대역)으로 미리 확인한 것.

**컨텍스트(요약)**: NPC `이리엘`(Elf), 현재 마을 = 정복된 엘프 정착지 S23. 성격 Pride↑/Loyalty↓. 관계: 여동생 `카운`(npc#1857, S30 거주, 정복 때 헤어짐), 왕국 K3(드워프)에 `GrudgeAgainst`. Sentiment: `ResentsKingdom`. 연대기 `E412`("412년 K3가 S23 정복·편입").

**A. 단계별 대화 (사연 점진 공개 → 유추 단서)** — 호감도 단계가 오를수록:
- `Stranger`: "엘프의 마을이오. 살 게 있으면 보고 가시오."
- `Friend`: "…이 마을, 원래 우리 것이 아니었소. 드워프 깃발 아래 살게 될 줄이야."
- `Confidant`: "412년 그날, 동생 카운의 손을 놓쳤소. 가문 인장만 챙겼어도 핏줄을 증명할 텐데…"

→ **플레이어 유추**(시스템이 답을 안 줌): 고향·혈통을 그리워함 → 엘프 유물(`Relic`/`Lore`)이면 좋아하겠다, 정복자를 떠올리는 것(`Weapon`)은 싫어하겠다.

**B. 선호 근거 (생성, 위 유추의 정답지)**:
```json
{ "preferred":["Relic","Lore","Flower"], "disliked":["Weapon"],
  "reaction":{"Loved":"…고향의 숨결이 느껴지오.","Disliked":"그 쇳내는 그날을 떠올리게 하오."},
  "usedEventIds":["E412"] }
```

**C. 소망 (`Friend`↑에서 떠오름)**:
- **요청**: `wishKinds`=[ItemGift,Recover,Reunion]; `candidates`=[`npc#1857`"여동생 카운", `ruinF#9`"봉인된 엘프 폐허(가문 인장)", `item#73`"가문 인장"]; `rewardBudget`=100~200G.
- **LLM 출력**:
```json
{
  "kind": "Recover",
  "target": "ruinF#9",
  "affinityReward": 25,
  "reward": {"gold":160,"items":[]},
  "revealDialogue": "드워프들이 S23을 삼킨 그날 동생 손을 놓쳤소… 가문 인장이 옛 숲 신전에 봉인돼 있소. 그것만 있으면 카운을 떳떳이 볼 텐데.",
  "fulfillDialogue": "이게… 정말 그 인장이오? 이제야 동생을 볼 낯이 서는구려. 진심으로 고맙소.",
  "usedEventIds": ["E412"]
}
```
**검증**: `kind∈enum` ✅ / `target∈candidates` ✅ / 160≤200 ✅ / E412 실재 ✅ → `NpcWish`(Revealed) 조립.

**변주**: 같은 컨텍스트에서 candidates의 `npc#1857`(여동생)을 집으면 `Reunion` 소망(재회)이 나오고, 충족 시 Live 연대기 append → 한 데이터가 여러 결을 낳음(H3).

**판정**: 대화 힌트·선호·소망의 모든 조각(폐허=LoreFragment, 여동생=Sibling, 정복=Chronicle, 동기=Sentiment)이 데이터에서 직접 도출 → **데이터 충분성 확인**. 핵심은 플레이어가 **C(소망)를 듣기 전에 A(대화)만으로 선호를 유추**할 수 있다는 것(H1). 단, *소형 모델(Llama)의 실제 품질·형식 준수*는 키 연결 후 실측 필요.

---

## 5. 결과 반영 — 루프 폐합

생성·소비된 내러티브는 **다시 백본 데이터로 흘러들어** 다음 생성의 입력이 된다:

| 플레이어 행동 | 반영 |
|--------------|------|
| 선물/소망 충족 | 제공 NPC 호감도↑ (`ModifyPlayerRelation`), 관계자 Sentiment 갱신 |
| 가족 재회(Reunion) 소망 충족 | Live 연대기 append (신규 `ChronicleEvent`) — [WORLD_HISTORY_PLAN §6.2](WORLD_HISTORY_PLAN.md) Live 연대기 훅 재사용 |
| 깊은 관계 형성 | NPC↔NPC `ModifyAffinity`에 파급(가족/친구 통해) — 선택 |

> 이로써 "역사 → 관계 → 대화 → 선물 → 호감도 → 보답 → 역사"의 닫힌 루프가 된다 (gameOverview 핵심 재미 다이어그램). 단 위 표는 *의도*이고, 실제 실행 지점은 — 호감도·유추 선물·보답은 [interactionSystem §4.5·§4.6](interactionSystem.md), 소망 충족·기록은 [QUEST_SYSTEM §5·§6](QUEST_SYSTEM_DESIGN.md).

---

## 6. LLM 통합 — 기존 LLMManager 기준

> **기존 자산**: LLM 토대는 이미 구현·연결됨. 본 엔진은 신규 클라이언트를 만들지 않고 이를 확장한다. MVP는 **Groq/Llama 유지**.

| 자산 | 위치 | 사실 |
|------|------|------|
| `LLMManager` | [LLMManager.cs](../Assets/Scripts/Manager/LLMManager.cs) | `AR.s.LLM`로 접근, `Initialize()`가 AR 부팅 시 호출. `RequestAsync(prompt, system)` / `RequestWithContextAsync(messages, model, temperature)` |
| `LLMClient` | [LLMClient.cs](../Assets/Scripts/Manager/LLMClient.cs) | **Groq API**(OpenAI 호환 REST `/chat/completions`) + **Llama**. UniTask + UnityWebRequest + Newtonsoft |
| 모델 | 코드 상수 | `llama-3.3-70b-versatile`(기본), `llama-3.1-8b-instant` |
| API 키 | `{persistentDataPath}/Config/llm.txt` | 없으면 `IsReady=false` (그레이스풀) |

### 6.1 모델 티어링 (현 가용 범위)

가용 Llama 모델 안에서 비용/품질을 나눈다:

| 생성 대상 | 모델 | 이유 |
|-----------|------|------|
| 세부 디테일·명문·선호 근거 (고빈도, 짧음) | `llama-3.1-8b-instant` | 빠르고 저렴, 배치 |
| 단계별 대화·소망·이벤트 (맥락 중요) | `llama-3.3-70b-versatile` | 맥락/품질 |

→ 호출부는 `inModel` 인자로 이미 모델 지정 가능. 더 높은 품질·구조화가 필요하면 상위 Groq 모델 추가 또는 제공자 교체는 **후순위 결정**(§8 #6).

### 6.2 구조화 출력

현재 테스트 경로는 **자유 텍스트 JSON**(스키마 미강제, [LLMManager.cs:53](../Assets/Scripts/Manager/LLMManager.cs#L53)). 엔진은 다음으로 강화:
- OpenAI 호환 `response_format`(JSON 모드)/function calling 시도 → 미지원·불안정 시 **프롬프트로 JSON 강제 + 파싱 + 스키마 검증** 폴백
- 파싱/검증 실패 시 §4.6 가드로 재시도 → 규칙 기반 템플릿 폴백

> **신뢰도(2026 기준)**: 제약 디코딩(JSON 모드·JSON 스키마 강제·function calling)이 보편화돼 **깨진 JSON 자체는 대부분 사라졌다**. 남는 실패는 "유효 JSON인데 값이 틀림"(없는 ref·예산 초과)으로 *이동* → §4.6 **의미 검증**은 여전히 필요(C 방식 후보 화이트리스트가 이를 줄임). ⚠ Groq에서 *json_schema 강제*의 모델별 지원은 의존 전 현행 문서 확인.

### 6.3 비용 · 캐시 · 폴백 (제공자 무관)

- **프롬프트 캐싱**: Groq엔 Anthropic식 프롬프트 캐싱이 없음 → **로컬 결과 캐시**에 의존(아래).
- **온디맨드 + 결과 캐시**: 한 번 생성한 NPC 대사/이름은 세이브에 저장해 재호출 없이 재사용 (H3 신선도 vs 절감 균형은 §8 #2).
- **오프라인/실패 대비**: `IsReady=false`거나 호출 실패 시 §4.6 규칙 기반 폴백으로 게임 진행 보장 — 기존 `IsReady` 그레이스풀 패턴 계승 (LLM은 *향상*이지 *의존*이 아니게).
- **결정론 주의**: LLM 출력은 비결정론 → 세이브에 **결과를 저장**해 재현(같은 시드라도 생성 텍스트는 저장본이 정본).

---

## 7. 검증 MVP (수직 슬라이스)

핵심 재미를 *최소 비용*으로 증명하는 첫 슬라이스:

1. **대상 1종**: NPC 상호작용 시 → 그 NPC의 관계·Sentiment·연대기 1~2건을 컨텍스트로 **단계별 대화 1~2개 + 선호 근거** 생성 (소망·보답은 다음 단계)
2. **컨텍스트 파이프라인**: §4.2~4.3 (관계 서브그래프 → 사건 1줄 평탄화)
3. **자유 텍스트(대화) + 구조화 출력(선호 근거) + 정합성 가드** 최소판
4. **루프**: 플레이어가 대화를 읽고 → 선물 → 선호 일치 판정 → 호감도 변동 ([interactionSystem §4.5](interactionSystem.md)) — 소망·Live 연대기 append는 다음
5. **측정**: H1(**대화만으로 선호를 유추 가능한가**) 수동 평가 + H2(환각율) + H4(토큰/지연/원가) 로깅

> 백본이 이미 backfill로 관계·Sentiment를 만들고 NpcInspect 관계 탭이 동작하므로([WORLD_HISTORY_PLAN R4](WORLD_HISTORY_PLAN.md)), MVP는 **"그 데이터를 프롬프트로 → 대화·선호로"** 잇는 얇은 경로만 새로 만들면 된다.
>
> 기존 `5`키 테스트 생성([LLMManager.cs:53](../Assets/Scripts/Manager/LLMManager.cs#L53))이 *컨텍스트 없는* 원시 버전이므로, 거기에 §4.2 컨텍스트 조립을 끼워 **단계별 대화**부터 뽑는 것이 첫 걸음이다.

---

## 8. 미결정 / 리스크

| # | 항목 | 메모 |
|---|------|------|
| 1 | **온디맨드 vs 사전 생성 경계** | 어디까지 사전 배치, 어디부터 트리거 시 생성? 비용 H4 vs 신선도 H3 |
| 2 | **캐시 무효화** | 관계 변동 시 저장된 대사를 언제 재생성? (영구 캐시면 진부, 매번이면 비용) |
| 3 | **컨텍스트 토큰 예산** | 사건 상위 N · 관계 홉 수 — 품질 vs 비용 곡선 튜닝 필요 |
| 4 | **정합성 가드 강도** | 규칙 검증 vs LLM 자기검증(2차 호출) vs 둘 다 |
| 5 | **세이브 크기** | 생성 대화/소망 누적 직렬화 — 만료/정리 정책 |
| 6 | **제공자 교체 여부** | MVP는 Groq/Llama 유지. 품질/구조화 출력이 부족하면 상위 Groq 모델 또는 Claude 등으로 교체·추상화 (현 `inModel` 인자 활용) |
| 7 | **한국어 톤/품질** | 종족·성격별 말투 일관성, 검수 루프 |
| 8 | **관계/선물 UI** | 관계·선물 시스템 0% — 대화/선물/소망 표시 UI 별도 설계 필요([interactionSystem §5](interactionSystem.md) 관계 패널 + [QUEST_SYSTEM §7](QUEST_SYSTEM_DESIGN.md) 소망 표시). 옛 퀘스트 로그/추적기는 불필요 |
| 9 | **대화의 "유추 가능성" 보정** | (옛 "대사 LLM 적용 미결정"은 적용 방향으로 해소.) 새 과제: 대화가 선호 단서를 **충분히 흘리되 정답을 직접 말하지 않게** — 너무 모호하면 좌절, 너무 친절하면 유추 재미 소실([interactionSystem §4.3·§9](interactionSystem.md)) |

---

## 9. 한 줄 요약

> **"백본이 만든 역사와 관계를 컨텍스트로, LLM이 *이 세계에서만 말이 되는* 대화·선호·소망을 즉석에서 짜내고, 플레이어가 그 사연을 알아가며 맺은 관계가 다시 그 역사에 한 줄을 더한다."**
