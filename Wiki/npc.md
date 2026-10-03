# NPC — 관계 · 상호작용 · 소망 · 자율 활동

---
name: npc
description: NPC 도메인 종합 — 정본 매핑, 코드 구현 현황, 마을 경제와의 경계.
type: topic
last_updated: 2026-10-03
sources:
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/npc/NPC_SIMULATION_DESIGN.md
  - Docs/npc/NPC_AMBITION_DESIGN.md
  - Docs/npc/NPC_COMBAT_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
  - Docs/npc/NPC_EQUIPMENT_DESIGN.md
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/npc/ 관계·활동·장비 문서
  - Assets/Scripts/Npc/, Assets/Scripts/Manager/NpcManager.cs (심볼 수준)
  - Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Npc/NpcActivityResolver.cs
---

## 1. 한눈에 보기

NPC 도메인의 목표는 **"살아있는 관계"** — NPC가 서로(가족/원한) 그리고 플레이어와(호감도 4축) 관계를 갖고, 대화로 사연이 드러나고, 선물·소망 충족으로 관계가 깊어지며, 모험가로서 자율 활동을 한다. [src: Docs/npc/interactionSystem.md §개요, Docs/npc/NPC_ACTIVITY_DESIGN.md §1.2]

**핵심 경계**: 모든 물리 NPC는 모험가이며, 마을 경제는 물리 NPC가 아닌 추상 인구가 구동하고 건물은 자가 건설된다. 서비스도 특정 NPC가 아니라 배치 건물이 제공한다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1–5]

**현재 구현**: 모든 생존 NPC는 `System_NpcSim`의 시간분할 처리 대상이며, 활동은 Rest·Hunt(솔로/파티)·Trade(마을 상점)·DungeonDelve(던전 추상 원정)다. 다음 활동은 성격에서 나온 **야망**과 필요(Gold·가방·부상 회복)로 효용 선택한다. Hunt는 Food·부상·사망·`Fell` 연대기·주요 사건 이력에 결과를 남기고, 활동 중 LOD 전환도 이어진다. 플레이어 관계 4축은 `NpcManager`가 저장하며 Talk/Gift가 Affinity·Intimacy를 실제로 바꾼다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11.1, Docs/npc/NPC_AMBITION_DESIGN.md §4·§6, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/Utility/InteractionHelper.cs]

**모험가 경제(야망 P1·P2·P4)**: NPC는 전리품을 상점에 팔아 Gold를 얻고(Hunt는 골드를 주지 않음), 그 Gold로 상점의 **기본 장비**를 사서 강해진다. 대금은 마을 Gold와 NPC Gold 사이에서만 움직이는 닫힌 경제다. Rest가 끝나면 예비금(치료비 × 3)을 넘은 Gold의 70%를 생활비로 소속 마을에 내고(1.8% 소멸), 세계 물가 지수가 가격에 배율을 붙인다(GOLD_BALANCE D124~D137). 전투력은 장착 장비로 계산하고, 개인 상한(시작 전투력 +20%, 강자 +35~50%)과 +15% 문턱 때문에 성장은 거의 평탄하다. 소문 교환·NPC 던전 완료(낮은 확률)·파티 자동 구성·부상 등급·NPC 간 절도까지 구현됐고, 협력·배신(P3)은 보류다. [src: Docs/npc/NPC_AMBITION_DESIGN.md §2·§5·§7·§9·§10.1, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §2]

**장비(2026-10-03)**: 일반 NPC 장비는 무작위 옵션이 아닌 **상점용 기본 아이템**(고정 옵션 1개)이다. 시작 장비도 기본 아이템이고, 전리품·장물은 장착하지 않고 판다. 기존 세이브는 로드 때 일괄 교체된다(WorldDataVersion 17). [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md D116~D123·§11, Docs/npc/NPC_EQUIPMENT_DESIGN.md §3.3·§4.2]

**실전투(NPC_COMBAT B1~B5)**: 화면 안 NPC는 조건부로 적을 인식해 싸우고, 동행은 던전 전용이며(D97), 플레이어–NPC 결투가 있다(HP 1로 끝남). 드랍 줍기 런타임은 있지만 지금은 디버그 메뉴로만 호출된다. 도주(D25)와 NPC 간 결투의 화면 장면(D24)은 보류다. [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태·§4.6·§6.1·§7.6, Assets/Scripts/Manager/NpcManager_Duel.cs, Assets/Scripts/Manager/NpcManager_Pickup.cs]

**현행 인원 기준**은 `max(맵 규모 최소값, 마을 수×8)`이며 대형 맵 최소값은 1,200명이다. 마을당 최소 5명을 먼저 보장하므로 300개 마을에서는 목표가 2,400명이다. 모든 NPC를 한 프레임에 처리하지 않고 라운드로빈 시간분할하며, 저장은 개별 상태를 영구 `EntityId` 기준 32개 샤드로 나눈다. 장비 compact 표현은 기본 아이템 행 번호 저장으로 해결됐고(아이템 1개 약 560B → 62B), 활동 상태 기본값도 생략해 NPC 상태가 10.7MB → 2.85MB가 됐다(57게임시간 세이브 실측). 장기 증가분은 가방 전리품과 소문이다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md D9·§11.1, Docs/tech/SAVE_PARTITION_DESIGN.md §3–5, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3, Docs/npc/NPC_EQUIPMENT_DESIGN.md §9]

대화 다양성 Phase 1도 구현돼 Deed·사망 기억을 사연 토픽으로 만들고 해당 NPC의 대화 캐시를 무효화한다. 플레이 검증은 아직 남아 있다. [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs]

## 2. 정본 매핑 — "이걸 물으면 이 문서의 이 절"

| 주제 | 정본 | 비고 |
|------|------|------|
| 플레이어↔NPC 관계 4축(Affinity/Trust/Fear/Intimacy) + 불변식(Trust 캡) | [interactionSystem §3](../Docs/npc/interactionSystem.md) | 데이터는 NpcManager 소유, ECS 아님 |
| 상호작용 판정·v1 행동 3종(Talk/Trade/Gift) | [interactionSystem §1–2](../Docs/npc/interactionSystem.md) | 서비스(`InteractionHelper`)+매니저 구조 |
| 관계·선물 몰입 루프(호감도 단계·대화 공개·선호·유추 선물·보답) | [interactionSystem §4](../Docs/npc/interactionSystem.md) | 옛 "퀘스트 수주" 모델을 대체 |
| 소망(Wish) 데이터 모델·상태머신·추적·완료·보상·영속화 | [QUEST_SYSTEM_DESIGN §2–6](../Docs/npc/QUEST_SYSTEM_DESIGN.md) | 소망 *떠오름*은 interactionSystem §4.7 |
| 소망·대화·선호의 **LLM 생성** | [NARRATIVE_ENGINE_DESIGN](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) (world_history 소속) | 후보 어휘는 QUEST_SYSTEM §8과 공유 |
| NPC↔NPC 관계(가족/원한/Sentiment)·backfill | [NPC_RELATION_HISTORY_DESIGN §4](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | 연대기(L1)는 PROCEDURAL_HISTORY 소관 |
| NPC 전기 로그(거주 이력·겪은 일)·지식 거리 | **상세 스키마 = [NPC_SIMULATION §5.1](../Docs/npc/NPC_SIMULATION_DESIGN.md)** / 개념·지식거리 = [NARRATIVE_ENGINE §4.8](../Docs/world_history/NARRATIVE_ENGINE_DESIGN.md) / 채널 = [RELATION_HISTORY §4.2](../Docs/npc/NPC_RELATION_HISTORY_DESIGN.md) | `Deeds`는 무제한 누적 주요 사건 이력이다. 사망(`Fell`)·Hunt 부상·파티 Hunt 후 호감도 변화를 기록하며, 주요 아이템 획득은 보상 경로 추가 때 연결한다. [src: Assets/Scripts/Npc/NpcBiography.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/System/System_NpcSim.cs] |
| NPC↔NPC 호감도 **런타임 변동** | [NPC_ACTIVITY_DESIGN §8.3](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | **활동 기반**. 패시브 근접 drift(구 System_NpcRelationship)는 폐기 [src: NPC_RELATION_HISTORY §5.2] |
| 모험가 자율 활동/원정(Venture)·부상·죽음 — 활동 *내용* | [NPC_ACTIVITY_DESIGN §4–10](../Docs/npc/NPC_ACTIVITY_DESIGN.md) | 활동 내용 설계. 시뮬 프레임은 아래 문서가 소유 |
| NPC 시뮬 *기반*(개별 LOD·오프스크린 상태·로밍·월드영향) | [NPC_SIMULATION_DESIGN](../Docs/npc/NPC_SIMULATION_DESIGN.md) | MVP 구현 완료: Rest/솔로·파티 Hunt, 개인 LOD 전환, 결과 반영, `max(맵 최소값, 마을 수×8)` 총량/점진 보충. 300개 마을 기준 목표 2,400명. 이주·WORLD_EVENT 유발은 후속. [src: Docs/npc/NPC_SIMULATION_DESIGN.md D9·§11, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs] |
| NPC=모험가·경제 분리·자가 건설 | [VILLAGE_NPC_BOUNDARY_DESIGN](../Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md) | 현행 정본 |
| 동료(파티 영입·명령·성장) | [companionSystem](../Docs/npc/companionSystem.md) | 최구식 문서 — §3 주의 필수 |
| NPC 장비 12슬롯·검사 UI(UINpcInspect)·시작 장비 시드 | [NPC_EQUIPMENT_DESIGN](../Docs/npc/NPC_EQUIPMENT_DESIGN.md) | 구현 완료. 시작 장비는 기본 아이템(§3.3) |
| NPC 동기(야망)·효용 선택·모험가 경제(Gold·상점 거래·닫힌 경제)·전투력·성장 곡선·소문·NPC 던전 완료·파티·부상·절도 | [NPC_AMBITION_DESIGN](../Docs/npc/NPC_AMBITION_DESIGN.md) — 규칙 정본은 §2 결정표(D1~D137, 골드 밸런스 D124~D137의 상세는 [GOLD_BALANCE_DESIGN](../Docs/economy/GOLD_BALANCE_DESIGN.md)) | 결정 번호는 AMBITION·COMBAT·SHOP_EQUIPMENT 공용 연번 |
| NPC 실전투 런타임(진영·인식·동행·드랍 줍기·결투) | [NPC_COMBAT_DESIGN](../Docs/npc/NPC_COMBAT_DESIGN.md) | 화면 안 전투를 소유. 사회 규칙은 AMBITION이 결과 훅으로 공급 |
| NPC 장비 출처(상점 기본 아이템)·기본 장비 칸·장비 저장 표현·기존 세이브 일괄 교체 | [NPC_SHOP_EQUIPMENT_DESIGN](../Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md) | D116~D123, 구현 결과·검증은 §11 |
| 플레이어 상태 관찰→Trust·대사·보상 | [PLAYER_RECOGNITION_DESIGN](../Docs/npc/PLAYER_RECOGNITION_DESIGN.md) | 전리품 식별 배관만 구현, 반응 본체 미구현 [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §제약·전제·§구현 계획, Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs] |

## 3. 정본 우선순위

갱신 날짜가 아니라 질문의 책임으로 정본을 선택한다.

1. 마을 경제와 물리 NPC의 경계는 **VILLAGE_NPC_BOUNDARY_DESIGN**이 우선한다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md]
2. 현재 개인 LOD·Rest·솔로/파티 Hunt·전역 총량 보충은 **NPC_SIMULATION_DESIGN**이 소유한다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11.1]
3. 파티와 추가 활동의 내용은 **NPC_ACTIVITY_DESIGN**이 소유하되 실행 프레임은 NPC_SIMULATION 계약을 따른다. [src: Docs/npc/NPC_ACTIVITY_DESIGN.md §1, Docs/npc/NPC_SIMULATION_DESIGN.md §10]
4. 플레이어 관계·선물은 **interactionSystem**, 소망 데이터는 **QUEST_SYSTEM_DESIGN**, NPC 간 관계와 backfill은 **NPC_RELATION_HISTORY_DESIGN**이 소유한다. [src: Docs/npc/interactionSystem.md, Docs/npc/QUEST_SYSTEM_DESIGN.md, Docs/npc/NPC_RELATION_HISTORY_DESIGN.md]
5. 장비 슬롯·시드·검사 UI는 **NPC_EQUIPMENT_DESIGN**, 장비가 *무엇인지*(기본 아이템)·저장 표현·상점 기본 장비 칸은 **NPC_SHOP_EQUIPMENT_DESIGN**이 소유한다. Inn 방문자 경로는 사용하지 않는다. [src: Docs/npc/NPC_EQUIPMENT_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md, Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §5]
7. NPC가 *왜* 어떤 활동을 고르는지(야망·효용)와 자원 흐름(Gold·상점·소문·절도)은 **NPC_AMBITION_DESIGN**이 소유하고, 실행 프레임(종료 시점 1회 판정·결정론 RNG·salt 정본)은 NPC_SIMULATION 계약을 따른다. 화면 안 전투는 **NPC_COMBAT_DESIGN**이 소유한다. [src: Docs/npc/NPC_AMBITION_DESIGN.md 책임·§11, Docs/npc/NPC_SIMULATION_DESIGN.md §11.1, Docs/npc/NPC_COMBAT_DESIGN.md]
6. **companionSystem**은 미구현 제안이다. 마을 직업 NPC나 상주 서비스 NPC를 전제로 한 내용은 적용하지 않는다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §1·§5]

## 4. 코드 현황 — 설계 vs 구현

| 축 | 설계 | 코드 | 상태 |
|----|------|------|------|
| NPC↔NPC 호감도 채널 | RELATION_HISTORY §3 | `NpcManager` GetAffinity/ModifyAffinity/SaveAffinity ([NpcManager.cs:588-669](../Assets/Scripts/Manager/NpcManager.cs)) | ✅ 구현 |
| NPC↔NPC 관계(유형) + backfill | RELATION_HISTORY §4 | `NpcRelationEntry`, `NpcSentiment`, [NpcRelationBackfill.cs](../Assets/Scripts/Npc/Relation/NpcRelationBackfill.cs), NpcManager AddRelation/GetRelations | ✅ 구현 |
| 대화 토픽 선택·내러티브 런타임 | NARRATIVE_ENGINE(+SPIKE) | [Scripts/Npc/Narrative/](../Assets/Scripts/Npc/Narrative/) — DialogueTopicSelector, NpcNarrativeService(`Llm/Cache/Fallback`), `AffinityTier`, `NpcWish`/`WishState` 타입 | 🚧 부분 구현 — 타입·선택 로직 존재 |
| 플레이어↔NPC 관계 4축(`PlayerRelation`) | interactionSystem §3 | `NpcManager.GetPlayerRelation`/`SetPlayerRelation`/`ModifyPlayerRelation`/Save/Load, `InteractionHelper` Talk/Gift 반영 | ✅ 구현 [src: Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/Utility/InteractionHelper.cs, Assets/Scripts/Npc/NpcSaveData.cs] |
| 선물·선호 판정(`GiftReaction`) / 소망 추적·보상 | interactionSystem §4 / QUEST_SYSTEM | 타입 골격(`NpcWish`, `WishKind`)만 Narrative에 존재 | ❌ 미구현 (문서상 0%) |
| 관찰 반응 — 몬스터 전리품 1호 신호 | PLAYER_RECOGNITION | `ItemTable.SourceMonsterId` + `MonsterTrophyIdentifier` 존재. 현행 ItemTable 데이터에는 연결된 전리품 없음 | 🟡 식별 배관만 구현, 감지·Trust·영속화·대사·보상 미구현 [src: Assets/Scripts/Common/Tables.cs, Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/_BinaryData/TableData/ItemTable.bytes] |
| 개인 활동 시뮬 | NPC_SIMULATION §11.1 | `System_NpcSim`, `NpcActivityState`, `NpcActivityResolver` — Rest/솔로·파티 Hunt/Trade/DungeonDelve, 결정론 결과, analytic 경로 | ✅ 구현 [src: Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Npc/NpcActivityResolver.cs] |
| 야망·효용 선택·장비 전투력(P1) | AMBITION §4–6·§10.1 | `NpcPowerEvaluator`, `NpcManager_Ambition`, Hunt 기준 전투력 10.75(2026-10-03 재정규화) | ✅ 구현 [src: Docs/npc/NPC_AMBITION_DESIGN.md §5.1, Assets/Scripts/Npc/NpcPowerEvaluator.cs, Assets/Scripts/Npc/NpcActivityResolver.cs] |
| 상점 거래·닫힌 경제·기본 장비 구매 | AMBITION §7.2·§9, SHOP_EQUIPMENT §4–5 | `NpcManager_Trade`, `VillageManager_NpcTrade`, `VillageTradeStockRoller.RollBasicEquipmentPlan` | ✅ 구현. 마을 골드 밸런스(생활비·물가 지수·금고 유지비) 2026-10-03 구현 [src: Assets/Scripts/Manager/NpcManager_Trade.cs, Assets/Scripts/Village/VillageManager_NpcTrade.cs, Assets/Scripts/Manager/NpcManager_LivingExpense.cs, Assets/Scripts/Village/VillageManager_GoldPrice.cs] |
| 소문·DungeonDelve·NPC 던전 완료(P2) | AMBITION §5.2·§7.1·§7.4 | `NpcManager_Delve`, `DungeonManager_NpcCompletion`, 소문 교환 | ✅ 구현 [src: Assets/Scripts/Manager/NpcManager_Delve.cs, Assets/Scripts/Dungeon/DungeonManager_NpcCompletion.cs] |
| 파티 자동 구성 개정·부상 등급 | AMBITION D85~D96 | 모집·수락 확률, 중상 10%·부상 요약 | ✅ 구현 [src: Docs/npc/NPC_AMBITION_DESIGN.md §2] |
| NPC 간 절도(P4) | AMBITION §7.6, D98~D115 | `NpcManager_Theft` — 현장 적발·단서·판매 소문 늦은 발각 | ✅ 구현. 훔친 장비는 장착하지 않음(D119) [src: Assets/Scripts/Manager/NpcManager_Theft.cs] |
| 사라진 터 강제 이주(NS4 일부) | AMBITION D138~D141 | `NpcManager_Relocation` — 터(폐허·합병) 소속 NPC가 72h 뒤 가장 가까운 상점 마을로 `Relocate`, 배분·보충에서 터 제외. `VillageId` = 소속 마을 | ✅ 구현(2026-10-03). 자발적 이주·거주 이력은 후속 [src: Assets/Scripts/Manager/NpcManager_Relocation.cs] |
| 협력·배신(P3) | AMBITION §7.5–7.6·§14 | — | ⏸ 보류 [src: Docs/npc/NPC_AMBITION_DESIGN.md §14] |
| 실전투·동행(던전 전용)·드랍 줍기·결투 | NPC_COMBAT B1~B5 | `System_AI_Perception` 조건부 인식, `NpcManager_Companion`·`NpcManager_Duel`·`NpcManager_Pickup`, `System_Duel` | ✅ 구현(드랍 줍기는 디버그 호출만). 도주·NPC 간 결투 화면 장면은 보류 [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태, Assets/Scripts/Manager/NpcManager_Duel.cs] |
| 전역 NPC 총량·점진 보충 | NPC_SIMULATION D9 | `NpcPopulationState`, `NpcManager.EnsureAllVillagesPopulated`/`TryReplenishGlobalPopulation` — 대형 맵 최소 1,200명, 마을당 8명 목표·최소 5명 | ✅ 구현. 300개 마을 기준 2,400명 [src: Docs/npc/NPC_SIMULATION_DESIGN.md D9, Assets/Scripts/Npc/NpcPopulationState.cs, Assets/Scripts/Manager/NpcManager.cs] |
| NPC 저장 분할 | SAVE_PARTITION_DESIGN | Player/World/Map/Village 등 책임 파티션 + NPC 상태 32샤드 + manifest 원자 커밋 | ✅ 구현. 장비 compact 표현은 기본 아이템 행 번호 저장(D122)으로 해결, 저장 복사기가 `DefaultValueHandling.Ignore`를 따르게 수정(2026-10-03). 전체 저장 13.3MB → 6.3MB [src: Docs/tech/SAVE_PARTITION_DESIGN.md §2–9, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Data/SaveDtoSnapshot.cs] |
| 사망 관계 기억 | NPC_SIMULATION §9-10 | `DeceasedRelationMemory`, `NpcManager.MarkNpcDead`, `UINpcInspect` | ✅ 구현 [src: Assets/Scripts/Npc/NpcRelationEntry.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/UI/UINpcInspect.cs] |
| 파티 원정·이주·WORLD_EVENT 유발 | NPC_SIMULATION NS4 | Hunt 파티는 구현. 이주·WORLD_EVENT 유발은 MVP 범위 밖 | 🟡 부분 구현 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11, Assets/Scripts/Common/System/System_NpcSim.cs] |
| 동료 시스템 | companionSystem | 없음 | ❌ 미구현 |
| NPC 장비·검사 UI·기본 아이템 | NPC_EQUIPMENT, SHOP_EQUIPMENT | NpcEquipHelper, UINpcInspect, `BasicItemTable`, `ItemManager.CreateBasicItem` | ✅ 구현 [src: Assets/Scripts/Npc/NpcEquipHelper.cs, Assets/Scripts/Manager/ItemManager.cs] |

> 소망 타입 골격(`NpcWish` 등)은 내러티브 **검증 스파이크의 부분집합 선구현**이다 — 파일 주석과 QUEST 문서 모두 "정본은 QUEST §2, 구현 시 이관"을 명시(2026-07-09 문서 갱신 반영). [src: Docs/npc/QUEST_SYSTEM_DESIGN.md §상태·§2, Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs 상단 주석]

## 5. 인접 도메인 경계

- **world_history와 가장 깊게 얽힌다** (→ [world_history.md](world_history.md)): 대화·소망의 LLM *생성*은 NARRATIVE_ENGINE(world_history) 소관이지만 **코드는 `Scripts/Npc/Narrative/`에 있다**. 연대기(L1)는 PROCEDURAL_HISTORY가 만들고, npc의 backfill(L2)이 그것을 *입력*으로 쓴다. [src: NPC_RELATION_HISTORY §1]
- **village**: 마을 경제(`v.Population`)는 물리 NPC와 무관하며 추상 인구에서 소싱한다. NPC 관련 시스템은 Village Priority 대역(50-69)의 Population 밴드(55-59)를 쓴다. [src: Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md §2, Docs/npc/NPC_ACTIVITY_DESIGN.md §12]
- **손제작 마을(연대기 미연결, `SettlementId<0`)**: 초기 인구 배분에서 최소 가중치(1)로 밀려 0명이 되기 쉬워, `EnsureHandcraftedVillageQuota`가 물리 NPC 1명을 하한 보장한다(전역 총량은 최다 절차 마을에서 1명 이관). 대화 컨텍스트는 연대기 정착지 스냅샷이 없으므로 `VillageProfile.Race`로 홈 종족 정체성·향수 소망 후보를 접지한다(관계·연대기 기반 sentiment는 없음 — solo 배치 결과). [src: Assets/Scripts/Manager/NpcManager.cs (EnsureHandcraftedVillageQuota), Assets/Scripts/Npc/Narrative/NpcDialogueContextBuilder.cs (settlementId<0 폴백)]
- **WORLD_EVENT**: 원정 결과 `EventTriggered` → 이벤트 디렉터 핸드오프. `Reunion` 소망 × "가족 구출 원정"(§3-E) 교차. [src: NPC_ACTIVITY §9, QUEST_SYSTEM §경계]
- **아키텍처 관례**: 영속 관계·활동 상태의 정본은 `NpcManager` + `WorldData`/`NpcSaveData`다. 실체화 Hunt의 기본 AI만 멈추기 위해 `NpcActivityPausedTag`를 일시 ECS 태그로 사용한다. [src: Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/System/System_NpcSim.cs]

## 6. 작업별 읽기 가이드

| 하려는 작업 | 읽을 것 (순서대로) |
|------------|-------------------|
| 관계·선물 루프 / 상호작용 구현 | interactionSystem §1–4 → QUEST_SYSTEM(소망 걸치면) |
| 소망(퀘스트) 구현 | QUEST_SYSTEM 전체 → interactionSystem §4.7 → NARRATIVE_ENGINE §4 |
| 현재 NPC 자율 활동 | NPC_SIMULATION §11.1 → `System_NpcSim` → `NpcActivityResolver` |
| NPC 경제·야망·소문·절도 규칙 변경 | AMBITION §2 결정표 → 해당 §7 절 → NPC_SIMULATION §11.1(salt 정본) → 진단 `ARPG/Npc/Diagnostics/Ambition Sim` |
| NPC 장비·상점 기본 장비 | NPC_SHOP_EQUIPMENT §2·§11 → NPC_EQUIPMENT §3.3·§4 → 진단 `ARPG/Diagnostics/Basic Items` |
| NPC 화면 안 전투·동행·결투 | NPC_COMBAT 상태·§4–7 |
| 파티·이주·WORLD_EVENT 확장 | NPC_SIMULATION NS4 → NPC_ACTIVITY(참고 범위) → WORLD_EVENT |
| NPC 관계 데이터 다루기 | NPC_RELATION_HISTORY → NpcManager 관계 region 코드 |
| 대화 시스템 | NARRATIVE_ENGINE(+SPIKE) → Scripts/Npc/Narrative/ 현행 코드 |
| 관찰 반응 구현 | PLAYER_RECOGNITION_DESIGN → interactionSystem §3 → InteractionHelper/UINpcInspect → Narrative 현행 코드 [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §코드 앵커] |
| 동료 시스템 | companionSystem — 단 §3의 충돌 지점 확인 후 착수 |
