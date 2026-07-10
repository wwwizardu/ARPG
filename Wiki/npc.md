# NPC — 관계 · 상호작용 · 소망 · 자율 활동

---
name: npc
description: NPC 도메인 종합 — 문서 7장의 정본 매핑, 최신성 순서, 코드 구현 현황, 인접 도메인 경계.
type: topic
last_updated: 2026-07-09
sources:
  - Docs/npc/ 전체 7장 (본문 정독)
  - Assets/Scripts/Npc/, Assets/Scripts/Manager/NpcManager.cs (심볼 수준)
---

## 1. 한눈에 보기

NPC 도메인의 목표는 **"살아있는 관계"** — NPC가 서로(가족/원한) 그리고 플레이어와(호감도 4축) 관계를 갖고, 대화로 사연이 드러나고, 선물·소망 충족으로 관계가 깊어지며, 모험가로서 자율 활동을 한다. [src: Docs/npc/interactionSystem.md §개요, Docs/npc/NPC_ACTIVITY_DESIGN.md §1.2]

**가장 중요한 최근 사건**: 2026-07-08 **NPC 단순화**가 코드까지 반영됐다 — 모든 물리 NPC는 **모험가 단일**, 마을 경제는 물리 NPC가 아닌 **추상 인구**가 구동, 건물은 자가 건설, 이민/여관 고용 제거. 이 결정은 npc 폴더의 다른 문서 일부를 부분적으로 구식화했다(§3). [src: Docs/npc/NPC_SIMPLIFICATION_PLAN.md §2, §Status]

## 2. 정본 매핑 — "이걸 물으면 이 문서의 이 절"

| 주제 | 정본 | 비고 |
|------|------|------|
| 플레이어↔NPC 관계 4축(Affinity/Trust/Fear/Intimacy) + 불변식(Trust 캡) | [interactionSystem §3](../Docs/npc/interactionSystem.md) | 데이터는 NpcManager 소유, ECS 아님 |
| 상호작용 판정·v1 행동 3종(Talk/Trade/Gift) | [interactionSystem §1–2](../Docs/npc/interactionSystem.md) | 서비스(`InteractionHelper`)+매니저 구조 |
| 관계·선물 몰입 루프(호감도 단계·대화 공개·선호·유추 선물·보답) | [interactionSystem §4](../Docs/npc/interactionSystem.md) | 옛 "퀘스트 수주" 모델을 대체 |
| 소망(Wish) 데이터 모델·상태머신·추적·완료·보상·영속화 | [QUEST_SYSTEM_DESIGN §2–6](../Docs/npc/QUEST_SYSTEM_DESIGN.md) | 소망 *떠오름*은 interactionSystem §4.7 |
| 소망·대화·선호의 **LLM 생성** | [NARRATIVE_ENGINE_DESIGN](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) (world_history 소속) | 후보 어휘는 QUEST_SYSTEM §8과 공유 |
| NPC↔NPC 관계(가족/원한/Sentiment)·backfill | [NPC_RELATION_HISTORY_DESIGN §4](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | 연대기(L1)는 PROCEDURAL_HISTORY 소관 |
| NPC 전기 로그(거주 이력·겪은 일)·지식 거리 | **상세 스키마 = [NPC_SIMULATION §5.1](../Docs/npc/NPC_SIMULATION_DESIGN.md)** / 개념·지식거리 = [NARRATIVE_ENGINE §4.8](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) / 채널 = [RELATION_HISTORY §4.2](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | 2026-07-09 설계, 미구현. deed = 연대기 참조 + NPC 레벨 열린 이벤트(원정 결과·전우사망 등), NPC당 캡 |
| NPC↔NPC 호감도 **런타임 변동** | [NPC_ACTIVITY_DESIGN §8.3](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | **활동 기반**. 패시브 근접 drift(구 System_NpcRelationship)는 폐기 [src: NPC_RELATION_HISTORY §5.2] |
| 모험가 자율 활동/원정(Venture)·부상·죽음 — 활동 *내용* | [NPC_ACTIVITY_DESIGN §4–10](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | 단 Anchored/Worker 분류는 폐기(§3). 시뮬 프레임은 아래로 승격 |
| NPC 시뮬 *기반*(개별 LOD·오프스크린 상태·로밍·월드영향) | [NPC_SIMULATION_DESIGN](../Docs/npc/NPC_SIMULATION_DESIGN.md) | 2026-07-09 설계 초안(미구현). **핵심 결정 D1~D12 완료** — 개별 시뮬·고정총량(보충)·오프스크린 상태·연속이동/조우·전기 로그·행동선택. 잔여 = 세부 튜닝(실측 후)·범위 밖(노변 이벤트) |
| NPC=모험가 단일화·경제 분리·자가 건설 (확정 결정) | [NPC_SIMPLIFICATION_PLAN §2](../Docs/npc/NPC_SIMPLIFICATION_PLAN.md) | 구현 완료 (2026-07-08) |
| 동료(파티 영입·명령·성장) | [companionSystem](../Docs/npc/companionSystem.md) | 최구식 문서 — §3 주의 필수 |
| NPC 장비 12슬롯·검사 UI(UINpcInspect) | [NPC_EQUIPMENT_DESIGN](../Docs/npc/NPC_EQUIPMENT_DESIGN.md) | 구현 완료 |

## 3. 문서 최신성 — 어느 문서가 어느 문서를 이기나

갱신일 역순. 뒤 문서와 충돌하면 **앞(최신) 문서가 이긴다**.

1. **NPC_SIMPLIFICATION_PLAN** (2026-07-08, 구현 완료) — 최상위. Anchored/Worker 분류, 이민/여관 고용, NPC 직무·건설을 전부 폐기했다. [src: 동 문서 §2, §4.1]
2. **interactionSystem / QUEST_SYSTEM_DESIGN** (2026-07-02 / 06-30) — 관계·선물·소망 축의 현행 정본. 서로 경계가 명시돼 있어 충돌 없음.
3. **NPC_ACTIVITY_DESIGN** (2026-07-01) — 문서 상단 배너가 스스로 밝히듯 **§2 D1·§3.1(행동 클래스 3분류)은 폐기**, 유효 범위는 "모험가의 원정 축"만. [src: 동 문서 상단 배너]
4. **NPC_RELATION_HISTORY_DESIGN** (2026-06-26) — 유효하나 §5.1 표의 "이민/Inn 고용" 행은 단순화로 무의미해짐 [추론: SIMPLIFICATION D6이 이민·고용을 제거].
5. **NPC_EQUIPMENT_DESIGN** — 구현 완료 문서지만 §4.1 `SpawnVisitorNpc`·§8.4 "Inn 방문자 카드" 경로는 단순화 SP5에서 삭제됨 — 문서에 취소선·배너로 반영 완료(2026-07-09). [src: Docs/npc/NPC_EQUIPMENT_DESIGN.md 상단 배너]
6. **companionSystem** (2026-04-01, 미구현) — 가장 오래됨. `player.Charisma` 판정(§2)은 이후 "플레이어 사회 스탯 없음" 결정과 충돌 [src: interactionSystem 설계 노트], 대장장이/상인 동료(§7)는 모험가 단일화와 충돌 [추론]. 파티 크기·시너지 *개념*만 NPC_ACTIVITY §7.2가 재사용.

## 4. 코드 현황 — 설계 vs 구현

| 축 | 설계 | 코드 | 상태 |
|----|------|------|------|
| NPC↔NPC 호감도 채널 | RELATION_HISTORY §3 | `NpcManager` GetAffinity/ModifyAffinity/SaveAffinity ([NpcManager.cs:588-669](../Assets/Scripts/Manager/NpcManager.cs)) | ✅ 구현 |
| NPC↔NPC 관계(유형) + backfill | RELATION_HISTORY §4 | `NpcRelationEntry`, `NpcSentiment`, [NpcRelationBackfill.cs](../Assets/Scripts/Npc/Relation/NpcRelationBackfill.cs), NpcManager AddRelation/GetRelations | ✅ 구현 |
| 대화 토픽 선택·내러티브 런타임 | NARRATIVE_ENGINE(+SPIKE) | [Scripts/Npc/Narrative/](../Assets/Scripts/Npc/Narrative/) — DialogueTopicSelector, NpcNarrativeService(`Llm/Cache/Fallback`), `AffinityTier`, `NpcWish`/`WishState` 타입 | 🚧 부분 구현 — 타입·선택 로직 존재 |
| 플레이어↔NPC 관계 4축(`PlayerRelation`) | interactionSystem §3 | NpcManager에 해당 API 없음 (GetPlayerRelation/ModifyPlayerRelation 부재) | ❌ 미구현 |
| 선물·선호 판정(`GiftReaction`) / 소망 추적·보상 | interactionSystem §4 / QUEST_SYSTEM | 타입 골격(`NpcWish`, `WishKind`)만 Narrative에 존재 | ❌ 미구현 (문서상 0%) |
| 원정(Venture) 시스템 | NPC_ACTIVITY §5–7 | NpcManager에 `_ventures` 없음, System_NpcActivity 없음 | ❌ 미구현 |
| 동료 시스템 | companionSystem | 없음 | ❌ 미구현 |
| NPC 장비·검사 UI | NPC_EQUIPMENT | NpcEquipHelper, UINpcInspect | ✅ 구현 |

> 소망 타입 골격(`NpcWish` 등)은 내러티브 **검증 스파이크의 부분집합 선구현**이다 — 파일 주석과 QUEST 문서 모두 "정본은 QUEST §2, 구현 시 이관"을 명시(2026-07-09 문서 갱신 반영). [src: Docs/npc/QUEST_SYSTEM_DESIGN.md §상태·§2, Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs 상단 주석]

## 5. 인접 도메인 경계

- **world_history와 가장 깊게 얽힌다** (→ [world_history.md](world_history.md)): 대화·소망의 LLM *생성*은 NARRATIVE_ENGINE(world_history) 소관이지만 **코드는 `Scripts/Npc/Narrative/`에 있다**. 연대기(L1)는 PROCEDURAL_HISTORY가 만들고, npc의 backfill(L2)이 그것을 *입력*으로 쓴다. [src: NPC_RELATION_HISTORY §1]
- **village**: 단순화 이후 마을 경제(`v.Population`)는 물리 NPC와 무관 — 추상 인구(§0, world_history)에서 소싱. NPC 관련 시스템은 Village Priority 대역(50-69)의 Population 밴드(55-59)를 쓴다. [src: NPC_SIMPLIFICATION §3.2, NPC_ACTIVITY §12]
- **WORLD_EVENT**: 원정 결과 `EventTriggered` → 이벤트 디렉터 핸드오프. `Reunion` 소망 × "가족 구출 원정"(§3-E) 교차. [src: NPC_ACTIVITY §9, QUEST_SYSTEM §경계]
- **아키텍처 관례**: 이 도메인은 의도적으로 **ECS 컴포넌트를 만들지 않는다** — 관계/원정 상태는 오프라인 NPC 간에도 살아야 하므로 정본은 `NpcManager` + `WorldData`. [src: NPC_RELATION_HISTORY §4.4, NPC_ACTIVITY §2 D3]

## 6. 작업별 읽기 가이드

| 하려는 작업 | 읽을 것 (순서대로) |
|------------|-------------------|
| 관계·선물 루프 / 상호작용 구현 | interactionSystem §1–4 → QUEST_SYSTEM(소망 걸치면) |
| 소망(퀘스트) 구현 | QUEST_SYSTEM 전체 → interactionSystem §4.7 → NARRATIVE_ENGINE §4 |
| NPC 자율 활동(Phase E) | NPC_SIMPLIFICATION(현행 전제) → NPC_ACTIVITY(폐기 절 제외) |
| NPC 관계 데이터 다루기 | NPC_RELATION_HISTORY → NpcManager 관계 region 코드 |
| 대화 시스템 | NARRATIVE_ENGINE(+SPIKE) → Scripts/Npc/Narrative/ 현행 코드 |
| 동료 시스템 | companionSystem — 단 §3의 충돌 지점 확인 후 착수 |
