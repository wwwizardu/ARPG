# 내러티브 엔진 — LLM 대화·소망 검증 스파이크 (Verification Spike)

> **목적**: [NARRATIVE_ENGINE_DESIGN.md §7](NARRATIVE_ENGINE_DESIGN.md)의 "검증 MVP 수직 슬라이스"를 **결정 확정본**으로 구체화한 실행 계획. "**Groq/Llama로 이 몰입 루프(문맥 대화 → 호감도 상승 → NPC의 요청/소망)가 실제로 되는가**"를 최소 비용으로 증명하는 것이 유일한 목표다. 완성 코드가 아니라 **검증용 얇은 경로**를 만든다.
>
> **상태 (2026-07-02)**: 🟦 스파이크 계획 (구현 대기). 결정 3건 확정(§1).
>
> **경계**: 본 문서는 *검증 스파이크*의 범위·설계·통과 기준까지. 대화·소망의 *정본 데이터 모델·동작*은 [QUEST_SYSTEM_DESIGN.md](../npc/QUEST_SYSTEM_DESIGN.md), *생성 엔진의 완성형*은 [NARRATIVE_ENGINE_DESIGN.md](NARRATIVE_ENGINE_DESIGN.md), *런타임 소비처(대화·선물·보답)*는 [interactionSystem.md §4](../npc/interactionSystem.md)가 담당한다. 스파이크는 이들의 **부분집합을 최소 구현**해 가설만 검증한다.
>
> **관련 문서**:
> - [NARRATIVE_ENGINE_DESIGN.md](NARRATIVE_ENGINE_DESIGN.md) — 생성 엔진 정본(§4 파이프라인·§4.4 생성 계약·§6 LLM 통합·§7 MVP)
> - [QUEST_SYSTEM_DESIGN.md](../npc/QUEST_SYSTEM_DESIGN.md) — 소망(Wish) 데이터 모델·생애주기(§2)·후보 풀(§8)
> - [interactionSystem.md](../npc/interactionSystem.md) — 호감도 티어(§4.2)·대화 점진 공개(§4.3)·대화 소비처
> - [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md) — 입력 백본(연대기/관계) 구현 현황

---

## 1. 확정 결정 (2026-07-02)

세 가지를 확정하고, 이로써 [NARRATIVE_ENGINE §8 #1](NARRATIVE_ENGINE_DESIGN.md)(온디맨드 vs 사전생성)을 **스파이크 한정**으로 해소한다.

| # | 결정 | 내용 | 근거 |
|---|------|------|------|
| **D1 검증 범위** | **대화 + 소망 구조화** | 티어별 대화(자유텍스트) + 소망 1건(구조화 JSON) 생성·검증. 선물감지·호감도 실반영은 제외(로그로만 확인). | 사용자가 말한 "요청까지 하는 흐름"의 실현 가능성이 핵심 검증 대상 |
| **D2 위협 접지** | **스파이크용 시드/하드코딩** | "마을 근처 몬스터로 밖이 위험"을 테스트 NPC에 문맥으로 주입. `VillageData.ThreatLevel`은 MVP에서 항상 0(Phase F)이므로 우회. | LLM 검증에 집중, 실데이터 배관은 검증 통과 후 |
| **D3 생성 타이밍** | **하이브리드: 첫 클릭 생성 → 캐시** | 첫 대화 클릭 시 온디맨드 생성 후 캐시, 재클릭은 즉시 재생. 설계(§6.3) 그대로. | 지연 UX는 1회만 체감, 신선도/비용 균형 |

> **D1 범위 주의 — §7과의 차이**: [NARRATIVE_ENGINE §7](NARRATIVE_ENGINE_DESIGN.md)의 MVP는 *대화 + **선호 근거***를 짝지어 소망을 미룬다. 본 스파이크는 사용자 우선순위(에스컬레이션되는 "요청")를 반영해 *대화 + **소망***으로 간다. 선호 근거(저비용 구조화)는 동일 틀의 변형이므로 여력 시 곁들일 수 있으나 스파이크 필수는 아니다.

---

## 2. 검증 가설 (무엇을 증명하나)

[NARRATIVE_ENGINE §1.2](NARRATIVE_ENGINE_DESIGN.md)의 H1~H4를 스파이크 리스크로 재정의:

| 리스크 | 질문 | 판정 |
|--------|------|------|
| **R1 접지 (H1·H2)** | Llama-70B가 후보·문맥만으로 *이 NPC의 실제 사연*(연대기/Sentiment/관계)에 근거해 말하고, 없는 인물/사건을 지어내지 않는가? | 창작(가짜 ref/사건) 오류율, 수동 검수 |
| **R2 구조화 출력** | Groq에서 소망 JSON(`kind∈enum`·`target∈후보`·`reward≤예산`)이 안정적으로 나오는가? | 스키마 통과율(재시도 포함), 폴백 동작 |
| **R3 에스컬레이션 (H3)** | 티어가 오를수록 대화 심화 → Friend에서 소망 등장이 자연스러운가? | 티어별 출력 수동 평가 |
| **R4 지연·비용 (H4)** | 대화 1건당 지연·토큰이 게임을 해치지 않는가? 실패 시 안 끊기는가? | 70B 호출 p50/p95, `IsReady=false`/에러 폴백 |

> 이 스파이크가 통과하면 "LLM으로 이 몰입 루프가 성립"이 증명되고, 본 구현([NARRATIVE_ENGINE](NARRATIVE_ENGINE_DESIGN.md) 실체화 + [QUEST 소망](../npc/QUEST_SYSTEM_DESIGN.md) 배선)으로 확장한다.

---

## 3. 흐름

```
대화 클릭 (또는 디버그 훅)
  → 캐시 조회 (npcId, tier)                         ← D3 하이브리드
     ├ 있음 → 즉시 재생
     └ 없음 → ① 문맥 빌드 → ② 대화 생성(자유텍스트) → 캐시 → 표시
  → tier == Friend↑ 이고 소망 미생성이면:
       ③ 소망 생성(구조화 JSON) → ④ 검증 → 재시도 1회 → 템플릿 폴백 → 캐시
```

---

## 4. 설계

### 4.1 문맥 빌더 (접지의 핵심)

[NARRATIVE_ENGINE §4.2~4.3](NARRATIVE_ENGINE_DESIGN.md)의 "관련 서브그래프만 + 1줄 평문화"를 최소 구현. **전부 이미 구현된 데이터**를 읽는다:

| 입력 | 소스 (구현됨) |
|------|--------------|
| 성격·직업·설명 | `NpcSaveData.NpcStat`(8성향)·`JobType`·`Description` |
| 개인 관계 | `NpcManager.GetRelations(npcId)` → `NpcRelationEntry`(가족/스승/원한) |
| 감정 → 사건 | `NpcSentiment` + `OriginEventId` → `ChronicleEvent` 역참조 |
| 마을·정착지 | `VillageData`(Stage/Population) → `SettlementSnapshot`(Fate/Race) |
| **위협 (시드)** | **D2: "근처 몬스터로 밖이 위험"을 파라미터로 주입** — `VillageData.ThreatLevel`(=0) 대체. *현재 상황(present)* 문맥으로 역사(past) 문맥과 별도로 취급, 나중에 실데이터로 교체 쉽게 격리 |

→ 연대기 사건은 1줄 평문화(§4.3 예: `"412년, K3 왕국이 엘프 정착지 S23을 정복했다."`). 토큰 예산에 맞춰 최신/중대 상위 N개 컷.

### 4.2 프롬프트 (C 방식 — 시스템이 후보 제공, LLM은 선택·서술)

[NARRATIVE_ENGINE §4.4](NARRATIVE_ENGINE_DESIGN.md) 계약을 그대로 사용. 두 종류 호출:

- **대화 (자유텍스트)** — `system`: 이 NPC 역할 + 규칙(*제공된 사실만 사용, 인물/사건 창작 금지*, 한국어, 성격에서 어조). `user`: 플랫 문맥 + 현재 `AffinityTier` + "이 티어에서 할 대사 1~2문장". 모델 `llama-3.3-70b-versatile`.
- **소망 (구조화)** — `system`: §4.4 규칙 + JSON 스키마. `user`: 문맥 + 닫힌 enum `WISH_KINDS=[ItemGift,Recover,Reunion]` + **실제 후보 목록**`{ref,desc}`([QUEST §8](../npc/QUEST_SYSTEM_DESIGN.md)에서 C# 규칙이 추림) + `REWARD_BUDGET`. 출력: `{kind,target,affinityReward,reward{gold,items},revealDialogue,fulfillDialogue,usedEventIds}`.

### 4.3 구조화 출력 검증 (R2)

[NARRATIVE_ENGINE §4.5·§4.6·§6.2](NARRATIVE_ENGINE_DESIGN.md):
1. Groq `response_format`(JSON 모드) 시도 → 미지원/불안정 시 **프롬프트 강제 JSON**. (스파이크에서 둘 다 실측 비교)
2. Newtonsoft 파싱 → 의미 검증: `kind∈enum`·`target∈후보`·`gold≤예산`·`usedEventIds⊆문맥`.
3. 실패 → **1회 재시도** → 재실패 → **규칙 템플릿 폴백**(예: 향수템 `ItemGift`) — 엔진이 멈추지 않게.

### 4.4 캐시 (D3 하이브리드) · 에스컬레이션

- **캐시**: 스파이크는 **런타임 dict**로 시작(첫 클릭 생성 → 재클릭 즉시). 세이브 영속(`WorldData`)은 통과 후 사소한 후속.
- **티어**: Stranger/Acquaintance/Friend/Confidant ([interactionSystem §4.2](../npc/interactionSystem.md)). 관계 계층(`ModifyPlayerRelation`)이 아직 없으므로 **스파이크는 디버그로 티어 강제 전환**해 각 티어 대화를 관찰(R3). 소망은 Friend↑ 게이트.

---

## 5. 통과 기준

| 리스크 | 측정 지표 | 목표(예시, 실측 후 조정) |
|--------|----------|------------------------|
| R1 접지 | N개 NPC × 티어에서 창작(가짜 이름/사건) 건수 | ≈0, 수동 검수 오류율 낮음 |
| R2 JSON | 소망 생성 스키마 통과율(재시도 포함) / 폴백 정상 | ≥90% / 폴백 무중단 |
| R3 에스컬레이션 | 티어 상승 시 사연 심화 + Friend 소망 자연 등장 | 수동 평가 통과 |
| R4 지연 | 70B 호출 p50/p95, 에러/키없음 시 폴백 | 체감 허용 범위, 폴백 확인 |

---

## 6. 신규/수정 코드 (footprint)

| 항목 | 파일 | 성격 |
|------|------|------|
| 문맥 빌더 | `NpcDialogueContextBuilder`(신규) | 백본+시드위협 → 평문 |
| 생성 서비스 | `NpcNarrativeService`(신규) | BuildContext / GenerateDialogue(tier) / GenerateWish(tier) + 검증·폴백 |
| 소망 DTO | `NpcWish` 최소판(신규) | [QUEST §2](../npc/QUEST_SYSTEM_DESIGN.md) 부분집합 |
| JSON 모드 옵션 | [LLMClient.cs](../../Assets/Scripts/Manager/LLMClient.cs)(소폭) | `response_format` 지원 추가 |
| 디버그 훅 | [LLMManager.cs:53](../../Assets/Scripts/Manager/LLMManager.cs#L53) 재활용 | 키로 NPC·티어 순회 생성 |
| 표시 | [UINpcInspect.cs](../../Assets/Scripts/UI/UINpcInspect.cs) 대화 패널(소폭) | 생성 대화 표시 |
| 시드 위협 | 작은 struct/파라미터 | D2 격리 |

> 백본(연대기/관계/Sentiment)·`LLMManager`(Groq/Llama)·`UINpcInspect`는 이미 존재하므로, 스파이크는 **"데이터 → 프롬프트 → 대화/소망"을 잇는 얇은 경로 + 검증·폴백**만 새로 만든다.

## 7. 작업 순서 (GO 후)

1. `NpcDialogueContextBuilder` — 백본 조회 + 1줄 평문화 + 시드 위협
2. `NpcNarrativeService.GenerateDialogue(tier)` — 대화 호출 + 캐시 (R1·R3·R4)
3. 디버그 훅 + `UINpcInspect` 표시 — 티어 강제 전환으로 에스컬레이션 관찰
4. `NpcWish` 최소 DTO + `GenerateWish` — 구조화 호출 + 검증 + 재시도 + 템플릿 폴백 (R2)
5. (선택) `LLMClient` JSON 모드 옵션 실측 비교
6. 통과 기준(§5) 측정 → 본 구현 확장 판단

---

## 8. 미결정 / 후속

- **관계 실반영**: 대화/선물 → 호감도 반영(`NpcManager.ModifyPlayerRelation`)은 별도 트랙(관계 데이터 계층). 스파이크는 티어 강제로 우회.
- **위협 실데이터**: `VillageData.ThreatLevel`(Phase F) 구현 후 D2 시드를 실제 근접 몬스터 수/난이도로 교체.
- **선호 근거 병행 여부**: §7의 선호 근거(저비용 구조화)를 대화의 "정답지"로 곁들일지(H1 검증 강화) — 여력 시.
- **캐시 영속·무효화**: 관계 변동 시 재생성 정책은 [NARRATIVE_ENGINE §8 #2](NARRATIVE_ENGINE_DESIGN.md).

---

**Last Updated**: 2026-07-02
**Status**: 🟦 스파이크 계획 (구현 대기, 결정 3건 확정)
**Dependencies**: 백본(연대기/관계/Sentiment) ✅ 구현 · `LLMManager`(Groq/Llama) ✅ · `UINpcInspect` ✅
