# World History — 절차적 역사 · 내러티브 엔진 · 도전 이벤트

---
name: world-history
description: world_history 도메인 종합 — 문서 5장의 정본 매핑, 설계 vs 구현 현황, LLM 생성 통제 장치 목록, 인접 도메인 경계.
type: topic
last_updated: 2026-10-04
sources:
  - Docs/world_history/ 전체 5장 (본문 정독)
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/npc/NPC_AMBITION_DESIGN.md §12.1–12.2
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs
  - Assets/Scripts/Manager/NpcManager_SharedActivities.cs
  - Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md
  - Assets/Scripts/History/, Assets/Scripts/Npc/Narrative/, Assets/Scripts/Manager/LLM* (심볼 수준)
---

## 1. 한눈에 보기

이 도메인은 세 층으로 구성된다. [src: Docs/world_history/ 각 문서 경계 선언]

1. **절차적 역사 백본** (PROCEDURAL_HISTORY) — 정착지 단위 coarse 시뮬로 연대기(Chronicle)·왕국·정복을 만들고, 게임 중에도 LOD로 지속(근처=실체화/멀리=추상 인구 틱). **핵심 원칙: "매크로는 시뮬, 개인 관계는 생성"** — NPC 개인은 시뮬하지 않는다.
2. **내러티브 엔진** (NARRATIVE_ENGINE + SPIKE) — 백본이 만든 역사·관계를 컨텍스트로, LLM이 대화·선호·소망을 생성. **게임의 핵심 재미 검증 1순위.**
3. **도전 이벤트** (WORLD_EVENT) — 역사를 씨앗으로 플레이어가 맞서는 위협을 발화. 몰입(관계) 축과 구분되는 도전(전투/탐험) 축.

**현재 상태**: 절차적 역사 백본과 런타임 LOD가 구현됐고, 추상 인구는 마을 경제의 입력이 된다. 마을은 현재 역사 종료 고정 인구로 초기 Stage와 완공 시설을 직접 확정하며 런타임 자동 발전은 비활성이다. 내러티브 엔진은 검증 스파이크 위에 최신 개인 사연 직접 조회·내용 기반 cache variant·NPC 단위 무효화가 구현됐지만 실제 플레이 품질 검증 전이고, 도전 이벤트는 미구현이다. [src: Docs/world_history/WORLD_HISTORY_PLAN.md, Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1, Docs/world_history/WORLD_EVENT_DESIGN.md, Docs/village/VILLAGE_REWORK_PLAN.md §V2–V7, Assets/Scripts/History/HistoryManager.cs]

**사연 저장과 생성 텍스트는 구분한다(2026-10-04)**: 부상·동료·아이템·던전 완료·절도 피해·사별 6종의 최신 요약/참조를 저장하고 기존 개인 토픽 5종에 연결한다. 중요 이력은 유지하며 반복 파티 활동은 통계, 절도 피해는 최신 1건으로 저장한다. 생성된 대사·소망 텍스트와 최근 화제 상태는 `NpcNarrativeRuntime` 메모리 캐시다. 최신 사연 30 PASS·공동 활동 27 PASS를 확인했고 실제 LLM 품질·재미·호출 비용 실측은 후속이다. [src: Docs/npc/NPC_AMBITION_DESIGN.md §12.1–12.2, Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs]

## 2. 정본 매핑 — "이걸 물으면 이 문서의 이 절"

| 주제 | 정본 | 비고 |
|------|------|------|
| 정착지 시뮬·연대기·왕국·정복 모델 | [PROCEDURAL_HISTORY_DESIGN §3–4](../Docs/world_history/PROCEDURAL_HISTORY_DESIGN.md) | 사건 카탈로그 9종은 §3.3, append-only |
| 런타임 LOD(인구·마을 건설·materialize·fold-back) | [PROCEDURAL §0](../Docs/world_history/PROCEDURAL_HISTORY_DESIGN.md) + [ABSTRACT_CONSTRUCTION](../Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md) | 역사/인구 층은 PROCEDURAL, 자원·건설·Tier·벽과 초기 발전은 ABSTRACT_CONSTRUCTION이 정본 [src: Docs/world_history/PROCEDURAL_HISTORY_DESIGN.md §0, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §6–8] |
| 구현 이력·잔여 작업(R0~R4, RL1-3) | [WORLD_HISTORY_PLAN](../Docs/world_history/WORLD_HISTORY_PLAN.md) | 문서-코드 현황 대조는 §0이 선례 |
| LLM 생성 파이프라인(컨텍스트→프롬프트→검증) | [NARRATIVE_ENGINE_DESIGN §4](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) | C 방식 생성 계약은 §4.4 ⭐ |
| **교차 일관성** (사건 정본화·지식 거리·NPC 전기 로그) | [NARRATIVE_ENGINE_DESIGN §4.8](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) | 2026-07-09 추가, 미구현. 사실=사건 앵커 / 관점=NPC. 데이터: PROCEDURAL §3.4 · RELATION_HISTORY §4.2 |
| LLM 통합(Groq/Llama, 모델 티어링, 캐시·폴백) | [NARRATIVE_ENGINE §6](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) | `LLMManager`/`LLMClient` 기존 자산 확장 |
| 검증 스파이크 범위·통과 기준·대화 토픽 카탈로그 | [NARRATIVE_ENGINE_SPIKE](../Docs/world_history/NARRATIVE_ENGINE_SPIKE.md) | 셔플백·화제 캡은 §4.5 |
| 소망(Wish) 데이터 모델·추적·영속화 | [QUEST_SYSTEM_DESIGN](../Docs/npc/QUEST_SYSTEM_DESIGN.md) (npc 소속) | 엔진은 "그 스키마에 맞는 산출물"만 책임 |
| 관계·선물 루프(대화의 런타임 소비처) | [interactionSystem §4](../Docs/npc/interactionSystem.md) (npc 소속) | |
| 도전 이벤트 카탈로그·역사→도전 변환 | [WORLD_EVENT_DESIGN §3·§5](../Docs/world_history/WORLD_EVENT_DESIGN.md) | 발화·페이싱(디렉터)은 §8 미결정 |
| NPC 개인 관계·backfill (연대기의 소비자) | [NPC_RELATION_HISTORY_DESIGN](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) (npc 소속) | 3계층(L1/L2/L3)의 L2·L3 |

## 3. 문서 최신성 · 발견된 드리프트

- **PROCEDURAL_HISTORY / WORLD_HISTORY_PLAN** — 2026-07-08 구현 상태 배너까지 반영된 최신 상태. 설계-구현 이탈(§0.2 fold-back, `Pop` float 등)도 문서 스스로 기록.
- **NARRATIVE_ENGINE_DESIGN** — 초안의 장기 설계와 스파이크 구현을 구분한다. 2026-10-04 §6.3은 생성 텍스트의 세이브 영속화를 목표 설계로, 현행 대사/소망/최근 화제를 메모리 캐시로 명시했다. [src: Docs/world_history/NARRATIVE_ENGINE_DESIGN.md §6.3, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs]
- **NARRATIVE_ENGINE_SPIKE** (07-02) — "1차 구현 완료(Unity 실측 전)". 코드(`Scripts/Npc/Narrative/`) 존재 확인.
- **WORLD_EVENT_DESIGN** (06-30) — 콘텐츠 카탈로그 단계, 미구현 0%. 유효.
- **NARRATIVE_DIALOGUE_VARIETY_DESIGN** — 최초 Phase 1(2026-07-15) 이후 절도 피해와 최신 사연 저장 개정(2026-10-04)이 반영됐다. 최신 슬롯 6종 → 개인 토픽 5종, 시각 기반 후보 선택·내용 기반 variant·NPC 단위 무효화가 현재 계약이다. [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5, Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs]
- 과거 드리프트 2건은 2026-07-09 해소됨: Docs/README.md 대시보드의 절차적 역사 "보류"·SPIKE "구현 대기" 표기 → 실제 구현 상태로 정정, WORLD_HISTORY_PLAN 헤더의 "NPC 소속 분리 논의 중" → §11.8과 일치하게 폐기 표기. [src: 양 문서 2026-07-09 갱신]

## 4. 코드 현황 — 설계 vs 구현

| 축 | 설계 | 코드 | 상태 |
|----|------|------|------|
| 연대기 시뮬(New Game, Tier 2 정복-병합) | PROCEDURAL §3–4 | [SettlementChronicleSimulator.cs](../Assets/Scripts/History/Chronicle/), ChronicleEvent/Snapshot/KingdomData | ✅ (Unity 플레이 검증 전) |
| 런타임 LOD — 고정 인구·자원 clock·materialize·fold-back | PROCEDURAL §0 + VILLAGE_REWORK V2·V7 | HistoryManager, WorldSimState, System_SettlementSim, VillageAbstractConstruction. `RuntimeProgressionEnabled=false`라 자동 건설·Stage 승격은 중단 | ✅ 정적 파리티 통과, 실제 청크 왕복 검증 후속 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V2·§V7, Assets/Scripts/Village/VillageProgressionRules.cs, Assets/Scripts/Common/System/System_SettlementSim.cs] |
| New Game 역사 기반 초기 완성형 마을 | VILLAGE_REWORK V2–V3 | HistoryManager → VillageDevelopmentBootstrap(고정 상태) + VillageInitialDevelopment(누적 시설) | ✅ Alive/Conquered 1회, Ruined/Merged 제외 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V2–V3, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs] |
| 추상 인구 → 마을 경제 소싱 | PLAN §11.7 | System_SettlementSim tick → `v.Population` | ✅ (NPC 단순화 SP1) |
| 관계 backfill(L2)·호감도 채널 | RELATION_HISTORY §3–4 | NpcRelationBackfill, NpcManager 관계 region | ✅ |
| 대화 생성 스파이크 + 최신 개인 사연 | SPIKE §4 + DIALOGUE_VARIETY §5 | [Scripts/Npc/Narrative/](../Assets/Scripts/Npc/Narrative/) — 최신 부상/동료/피해/자랑 중 2개 + 최신 사별 별도 1개, 티어·레퍼토리 적용, 내용 기반 variant | 🟨 구현·저장/이관 진단 통과, 생성 텍스트는 메모리 캐시·실제 플레이 실측 후속 [src: Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs, Docs/npc/NPC_AMBITION_DESIGN.md §12.2] |
| LLM 클라이언트 | NARRATIVE_ENGINE §6 | [LLMManager.cs](../Assets/Scripts/Manager/LLMManager.cs)/[LLMClient.cs](../Assets/Scripts/Manager/LLMClient.cs) (Groq/Llama) | ✅ (JSON 모드 옵션 미구현 — 프롬프트 강제로 대체 중) |
| 소망 생성→추적→보상 본 구현 | QUEST_SYSTEM | 스파이크 DTO만 | ❌ |
| R4 회고 대사·Sentiment 표시 | PLAN §7 | 없음 | ❌ (명시적 잔여 작업) |
| 교차 일관성 (사건 정본·지식 거리·전기 로그) | NARRATIVE_ENGINE §4.8 | 없음 | ❌ (2026-07-09 설계, 스파이크 측정 선행) |
| 멀리 정치 사건 지속(§0.4-나)·위협도·이벤트 디렉터 | PROCEDURAL §0.4, WORLD_EVENT §8 | 없음 | ❌ (설계상 보류) |

## 5. ★ LLM 생성 통제 장치 — "중구난방"을 막는 현행 설계

설계의 대원칙: **"골격/스킨 분리 — 기계 골격(사실·대상·보상)은 시스템이 정하고, LLM은 그 안에서 선택하고 문장만 입힌다."** 개별 장치: [src: NARRATIVE_ENGINE §3.1·§4.4–4.6, SPIKE §4, QUEST §8, interactionSystem §4.4]

| # | 장치 | 내용 | 정본 |
|---|------|------|------|
| 1 | 닫힌 어휘 | `WishKind`/`GiftCategory` 닫힌 enum — "LLM은 이 밖을 못 쓴다" | NARRATIVE_ENGINE §3.1 |
| 2 | C 방식 후보 화이트리스트 | 소망 대상은 시스템이 추린 `candidates {ref,desc}` 안에서만 선택. 새 ref 금지 | NARRATIVE_ENGINE §4.4 |
| 3 | 실현 가능 템플릿 | 후보는 ItemTable에 실재·획득 경로 있는 것만 — "생성 가능 범위 = 실현 가능 범위" | QUEST §8.2 |
| 4 | 사실 접지(C-method) | 소문·회고는 진짜 사실(연대기 1줄 평문화)을 건네고 말투만 입힘. 컨텍스트는 관련 서브그래프만 | NARRATIVE_ENGINE §4.2–4.3, SPIKE §4.5 |
| 5 | 정합성 가드(사후 검증) | `usedEventIds⊆컨텍스트`, `target∈candidates`, `reward≤예산`, 참조 대상 실재·생존 검사 → 실패 시 재시도 1회 → 규칙 템플릿 폴백 | NARRATIVE_ENGINE §4.6 |
| 6 | 백본 유래 선호 | 선호는 임의 태그가 아니라 종족·Sentiment·직업→카테고리 도출 규칙 공유 — 어느 NPC든 같은 규칙 | interactionSystem §4.4 |
| 7 | 티어 게이트 | 호감도 단계가 허용한 백본 층만 회고 가능("단계 초과 사연 금지" 가드) | NARRATIVE_ENGINE §4.4 표 |
| 8 | 결과 캐시의 목표와 현재 범위 | **목표**: LLM 결과를 세이브에 저장해 정본으로 재현. **현재**: 대사·소망·최근 화제는 메모리 캐시, 사연 원본·통계·최신 참조만 영속 | NARRATIVE_ENGINE §6.3 [src: Docs/world_history/NARRATIVE_ENGINE_DESIGN.md §6.3, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs] |
| 9 | 화제 캡 + 셔플백 | NPC 시드의 기본 1~3 + 관계 티어를 2~5로 제한하고 성격 편향을 적용한다. 긴급 화제 우선·최근 화제 순환 유지 | 현행 선택 코드 [src: Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs] |
| 10 | 그레이스풀 폴백 | LLM 없음/실패 시 `BaseLine`/규칙 템플릿으로 진행 — "LLM은 향상이지 의존이 아니다" | NARRATIVE_ENGINE §6.3 |

### 통제가 얇거나 없는 지점 (기준 논의 후보)

- **세계관 톤·말투 일관성** — 종족·성격별 말투의 통일 기준 없음. 문서도 미결정으로 인정 [src: NARRATIVE_ENGINE §8 #7].
- ~~**NPC 간 교차 일관성**~~ → **설계됨 (2026-07-09, 미구현)**: 사건 정본화 + 지식 거리로 해결안 기록 → [NARRATIVE_ENGINE §4.8](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md). 사실은 사건(EventId) 앵커·공유 정본으로 일치 강제, 관점은 NPC별 발산 허용. 착수 전 스파이크에서 충돌 빈도 측정.
- ~~**자유 서술 필드의 창작 허용 폭**~~ → §4.8이 부분 해결: firsthand 사건은 정본 척추(누가·언제·어디·결과)에 묶고, distant 소문·감정 묘사만 자유. (문장 단위 미세 창작의 상한은 여전히 열림)
- **캐시 무효화 범위** — Deed·부상 요약·최신 절도 피해·공동 활동 통계·사망 관계 기억을 갱신할 때 해당 NPC의 대화 캐시를 제거한다. 관계·마을·세계 상태 전반에 대한 일반 무효화 정책은 아직 없다. [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs]
- **고유명사 캐논** — 정착지 표시명 미결정(PLAN 잔여), 왕국명은 "{종족} 왕국 N" 수준. LLM이 지명·인명을 서술에 쓸 때 참조할 이름 사전이 없다 [src: WORLD_HISTORY_PLAN 헤더, PROCEDURAL §7-3].
- **세부 디테일 생성(이름·별명·말투)의 정본 부재** — 생성 대상 표에 있으나(§3) 산출물을 어디에 저장해 캐논으로 삼는지 미설계 [추론].

## 6. 인접 도메인 경계

- **npc와 양방향으로 가장 깊다**: 연대기(L1, 본 도메인)가 관계 backfill(L2, npc)의 입력이고, 내러티브 엔진의 산출물(대화·소망)은 interactionSystem/QUEST_SYSTEM(npc)이 소비·판정한다. **스파이크 코드가 `Scripts/Npc/Narrative/`에 있는 이유.** → [npc.md](npc.md) §5
- **village**: 추상 인구가 `v.Population`의 입력이며, 같은 Priority 50 tick에서 원거리 자원·건설·Tier·벽을 slice 처리한다. materialize 전 catch-up하고 fold-back 시 두 건설 clock을 현재 시각에 맞춘다. → [village.md](village.md) [src: Assets/Scripts/Common/System/System_SettlementSim.cs, Assets/Scripts/History/HistoryManager.cs, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md §7]
- **map**: 정착지 배치(`RaceSettlementPlacer`)가 시뮬 입력, `Fate`가 폐허/점령 variant로 맵에 표현, LoreFragment가 이벤트·소망(Recover)의 씨앗. [src: PROCEDURAL §3.1·§3.5]
- **아키텍처 관례**: ECS 컴포넌트를 만들지 않는다 — 정본은 매니저(HistoryManager/NpcManager) + WorldData. 난수는 `DerivedSeed` 채널만(UnityEngine.Random 금지). [src: PROCEDURAL §6, PLAN §4.2]

## 7. 작업별 읽기 가이드

| 하려는 작업 | 읽을 것 (순서대로) |
|------------|-------------------|
| LLM 생성 일관성 기준 설계 | 본 페이지 §5 → NARRATIVE_ENGINE §4.4–4.6 → SPIKE §4.5 |
| 내러티브 엔진 본 구현 | SPIKE(현행 코드 기준) → NARRATIVE_ENGINE §4·§6 → QUEST_SYSTEM §2·§8 |
| 최신 개인 사연·캐시·이관 변경 | DIALOGUE_VARIETY §5 → NPC_AMBITION §12.1–12.2 → SAVE_PARTITION §5·§8 → `NpcSaveData_PersonalStories`·`NpcManager_SharedActivities`·`DialogueTopicSelector` [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md, Docs/npc/NPC_AMBITION_DESIGN.md, Docs/tech/SAVE_PARTITION_DESIGN.md] |
| 연대기 시뮬·LOD 수정 | PROCEDURAL §0 → ABSTRACT_CONSTRUCTION §6–8 → HistoryManager/System_SettlementSim 코드 [src: Docs/world_history/PROCEDURAL_HISTORY_DESIGN.md, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md] |
| R4 회고 대사 (잔여 작업) | PLAN §7 → NARRATIVE_ENGINE §4.3(1줄 평문화) |
| 도전 이벤트 착수 | WORLD_EVENT 전체 → §8 미결정 확인 → NPC_ACTIVITY §9(원정 트리거) |
