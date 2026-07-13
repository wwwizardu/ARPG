# NPC — 관계 · 상호작용 · 소망 · 자율 활동

---
name: npc
description: NPC 도메인 종합 — 정본 매핑, 코드 구현 현황, 마을 경제와의 경계.
type: topic
last_updated: 2026-07-13
sources:
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/npc/ 관계·활동·장비 문서
  - Assets/Scripts/Npc/, Assets/Scripts/Manager/NpcManager.cs (심볼 수준)
  - Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Npc/NpcActivityResolver.cs
---

## 1. 한눈에 보기

NPC 도메인의 목표는 **"살아있는 관계"** — NPC가 서로(가족/원한) 그리고 플레이어와(호감도 4축) 관계를 갖고, 대화로 사연이 드러나고, 선물·소망 충족으로 관계가 깊어지며, 모험가로서 자율 활동을 한다. [src: Docs/npc/interactionSystem.md §개요, Docs/npc/NPC_ACTIVITY_DESIGN.md §1.2]

**핵심 경계**: 모든 물리 NPC는 모험가이며, 마을 경제는 물리 NPC가 아닌 추상 인구가 구동하고 건물은 자가 건설된다. 서비스도 특정 NPC가 아니라 배치 건물이 제공한다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1–5]

**현재 MVP**: 모든 생존 NPC는 `System_NpcSim`의 시간분할 처리 대상이며, 활동 범위는 Rest와 솔로 Hunt다. Hunt는 Food·부상·사망·`Fell` 연대기·전기에 결과를 남기고, 활동 중 LOD 전환도 이어진다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11.1, Assets/Scripts/Common/System/System_NpcSim.cs]

## 2. 정본 매핑 — "이걸 물으면 이 문서의 이 절"

| 주제 | 정본 | 비고 |
|------|------|------|
| 플레이어↔NPC 관계 4축(Affinity/Trust/Fear/Intimacy) + 불변식(Trust 캡) | [interactionSystem §3](../Docs/npc/interactionSystem.md) | 데이터는 NpcManager 소유, ECS 아님 |
| 상호작용 판정·v1 행동 3종(Talk/Trade/Gift) | [interactionSystem §1–2](../Docs/npc/interactionSystem.md) | 서비스(`InteractionHelper`)+매니저 구조 |
| 관계·선물 몰입 루프(호감도 단계·대화 공개·선호·유추 선물·보답) | [interactionSystem §4](../Docs/npc/interactionSystem.md) | 옛 "퀘스트 수주" 모델을 대체 |
| 소망(Wish) 데이터 모델·상태머신·추적·완료·보상·영속화 | [QUEST_SYSTEM_DESIGN §2–6](../Docs/npc/QUEST_SYSTEM_DESIGN.md) | 소망 *떠오름*은 interactionSystem §4.7 |
| 소망·대화·선호의 **LLM 생성** | [NARRATIVE_ENGINE_DESIGN](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) (world_history 소속) | 후보 어휘는 QUEST_SYSTEM §8과 공유 |
| NPC↔NPC 관계(가족/원한/Sentiment)·backfill | [NPC_RELATION_HISTORY_DESIGN §4](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | 연대기(L1)는 PROCEDURAL_HISTORY 소관 |
| NPC 전기 로그(거주 이력·겪은 일)·지식 거리 | **상세 스키마 = [NPC_SIMULATION §5.1](../Docs/npc/NPC_SIMULATION_DESIGN.md)** / 개념·지식거리 = [NARRATIVE_ENGINE §4.8](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) / 채널 = [RELATION_HISTORY §4.2](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | `Deeds` 중 사망(`Fell`) 기록은 구현됨. 거주 이력·다른 사건 카탈로그는 후속. [src: Assets/Scripts/Manager/NpcManager.cs, Docs/npc/NPC_SIMULATION_DESIGN.md §11.1] |
| NPC↔NPC 호감도 **런타임 변동** | [NPC_ACTIVITY_DESIGN §8.3](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | **활동 기반**. 패시브 근접 drift(구 System_NpcRelationship)는 폐기 [src: NPC_RELATION_HISTORY §5.2] |
| 모험가 자율 활동/원정(Venture)·부상·죽음 — 활동 *내용* | [NPC_ACTIVITY_DESIGN §4–10](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | 활동 내용 설계. 시뮬 프레임은 아래 문서가 소유 |
| NPC 시뮬 *기반*(개별 LOD·오프스크린 상태·로밍·월드영향) | [NPC_SIMULATION_DESIGN](../Docs/npc/NPC_SIMULATION_DESIGN.md) | MVP 구현 완료: Rest/솔로 Hunt, 개인 LOD 전환, 결과 반영, 전역 총량/점진 보충. 파티·이주·WORLD_EVENT 유발은 후속. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11, Assets/Scripts/Common/System/System_NpcSim.cs] |
| NPC=모험가·경제 분리·자가 건설 | [VILLAGE_NPC_BOUNDARY_DESIGN](../Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) | 현행 정본 |
| 동료(파티 영입·명령·성장) | [companionSystem](../Docs/npc/companionSystem.md) | 최구식 문서 — §3 주의 필수 |
| NPC 장비 12슬롯·검사 UI(UINpcInspect) | [NPC_EQUIPMENT_DESIGN](../Docs/npc/NPC_EQUIPMENT_DESIGN.md) | 구현 완료 |

## 3. 정본 우선순위

갱신 날짜가 아니라 질문의 책임으로 정본을 선택한다.

1. 마을 경제와 물리 NPC의 경계는 **VILLAGE_NPC_BOUNDARY_DESIGN**이 우선한다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md]
2. 현재 개인 LOD·Rest·솔로 Hunt·전역 총량 보충은 **NPC_SIMULATION_DESIGN**이 소유한다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11.1]
3. 파티와 추가 활동의 내용은 **NPC_ACTIVITY_DESIGN**이 소유하되 실행 프레임은 NPC_SIMULATION 계약을 따른다. [src: Docs/npc/NPC_ACTIVITY_DESIGN.md §1, Docs/npc/NPC_SIMULATION_DESIGN.md §10]
4. 플레이어 관계·선물은 **interactionSystem**, 소망 데이터는 **QUEST_SYSTEM_DESIGN**, NPC 간 관계와 backfill은 **NPC_RELATION_HISTORY_DESIGN**이 소유한다. [src: Docs/npc/interactionSystem.md, Docs/npc/QUEST_SYSTEM_DESIGN.md, Docs/npc/NPC_RELATION_HISTORY_DESIGN.md]
5. 장비는 **NPC_EQUIPMENT_DESIGN**이 소유하지만 Inn 방문자 경로는 사용하지 않는다. [src: Docs/npc/NPC_EQUIPMENT_DESIGN.md, Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §5]
6. **companionSystem**은 미구현 제안이다. 마을 직업 NPC나 상주 서비스 NPC를 전제로 한 내용은 적용하지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1·§5]

## 4. 코드 현황 — 설계 vs 구현

| 축 | 설계 | 코드 | 상태 |
|----|------|------|------|
| NPC↔NPC 호감도 채널 | RELATION_HISTORY §3 | `NpcManager` GetAffinity/ModifyAffinity/SaveAffinity ([NpcManager.cs:588-669](../Assets/Scripts/Manager/NpcManager.cs)) | ✅ 구현 |
| NPC↔NPC 관계(유형) + backfill | RELATION_HISTORY §4 | `NpcRelationEntry`, `NpcSentiment`, [NpcRelationBackfill.cs](../Assets/Scripts/Npc/Relation/NpcRelationBackfill.cs), NpcManager AddRelation/GetRelations | ✅ 구현 |
| 대화 토픽 선택·내러티브 런타임 | NARRATIVE_ENGINE(+SPIKE) | [Scripts/Npc/Narrative/](../Assets/Scripts/Npc/Narrative/) — DialogueTopicSelector, NpcNarrativeService(`Llm/Cache/Fallback`), `AffinityTier`, `NpcWish`/`WishState` 타입 | 🚧 부분 구현 — 타입·선택 로직 존재 |
| 플레이어↔NPC 관계 4축(`PlayerRelation`) | interactionSystem §3 | NpcManager에 해당 API 없음 (GetPlayerRelation/ModifyPlayerRelation 부재) | ❌ 미구현 |
| 선물·선호 판정(`GiftReaction`) / 소망 추적·보상 | interactionSystem §4 / QUEST_SYSTEM | 타입 골격(`NpcWish`, `WishKind`)만 Narrative에 존재 | ❌ 미구현 (문서상 0%) |
| 개인 활동 시뮬(MVP) | NPC_SIMULATION §11.1 | `System_NpcSim`, `NpcActivityState`, `NpcActivityResolver` — Rest/솔로 Hunt, 결정론 결과, analytic Hunt 경로 | ✅ 구현 [src: Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Npc/NpcActivityResolver.cs] |
| 전역 NPC 총량·점진 보충 | NPC_SIMULATION D9 | `NpcPopulationState`, `NpcManager.EnsureAllVillagesPopulated`/`TryReplenishGlobalPopulation` | ✅ 구현 [src: Assets/Scripts/Npc/NpcPopulationState.cs, Assets/Scripts/Manager/NpcManager.cs] |
| 사망 관계 기억 | NPC_SIMULATION §9-10 | `DeceasedRelationMemory`, `NpcManager.MarkNpcDead`, `UINpcInspect` | ✅ 구현 [src: Assets/Scripts/Npc/NpcRelationEntry.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/UI/UINpcInspect.cs] |
| 파티 원정·이주·WORLD_EVENT 유발 | NPC_SIMULATION NS4 | MVP 범위 밖 | ❌ 미구현 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11] |
| 동료 시스템 | companionSystem | 없음 | ❌ 미구현 |
| NPC 장비·검사 UI | NPC_EQUIPMENT | NpcEquipHelper, UINpcInspect | ✅ 구현 |

> 소망 타입 골격(`NpcWish` 등)은 내러티브 **검증 스파이크의 부분집합 선구현**이다 — 파일 주석과 QUEST 문서 모두 "정본은 QUEST §2, 구현 시 이관"을 명시(2026-07-09 문서 갱신 반영). [src: Docs/npc/QUEST_SYSTEM_DESIGN.md §상태·§2, Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs 상단 주석]

## 5. 인접 도메인 경계

- **world_history와 가장 깊게 얽힌다** (→ [world_history.md](world_history.md)): 대화·소망의 LLM *생성*은 NARRATIVE_ENGINE(world_history) 소관이지만 **코드는 `Scripts/Npc/Narrative/`에 있다**. 연대기(L1)는 PROCEDURAL_HISTORY가 만들고, npc의 backfill(L2)이 그것을 *입력*으로 쓴다. [src: NPC_RELATION_HISTORY §1]
- **village**: 마을 경제(`v.Population`)는 물리 NPC와 무관하며 추상 인구에서 소싱한다. NPC 관련 시스템은 Village Priority 대역(50-69)의 Population 밴드(55-59)를 쓴다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §2, Docs/npc/NPC_ACTIVITY_DESIGN.md §12]
- **WORLD_EVENT**: 원정 결과 `EventTriggered` → 이벤트 디렉터 핸드오프. `Reunion` 소망 × "가족 구출 원정"(§3-E) 교차. [src: NPC_ACTIVITY §9, QUEST_SYSTEM §경계]
- **아키텍처 관례**: 영속 관계·활동 상태의 정본은 `NpcManager` + `WorldData`/`NpcSaveData`다. 실체화 Hunt의 기본 AI만 멈추기 위해 `NpcActivityPausedTag`를 일시 ECS 태그로 사용한다. [src: Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/System/System_NpcSim.cs]

## 6. 작업별 읽기 가이드

| 하려는 작업 | 읽을 것 (순서대로) |
|------------|-------------------|
| 관계·선물 루프 / 상호작용 구현 | interactionSystem §1–4 → QUEST_SYSTEM(소망 걸치면) |
| 소망(퀘스트) 구현 | QUEST_SYSTEM 전체 → interactionSystem §4.7 → NARRATIVE_ENGINE §4 |
| 현재 NPC 자율 활동(MVP) | NPC_SIMULATION §11.1 → `System_NpcSim` → `NpcActivityResolver` |
| 파티·이주·WORLD_EVENT 확장 | NPC_SIMULATION NS4 → NPC_ACTIVITY(참고 범위) → WORLD_EVENT |
| NPC 관계 데이터 다루기 | NPC_RELATION_HISTORY → NpcManager 관계 region 코드 |
| 대화 시스템 | NARRATIVE_ENGINE(+SPIKE) → Scripts/Npc/Narrative/ 현행 코드 |
| 동료 시스템 | companionSystem — 단 §3의 충돌 지점 확인 후 착수 |
