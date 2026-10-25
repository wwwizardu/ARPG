---
name: wiki-map
description: Docs/ 전체 주제를 얕고 넓게 조망하는 지도 페이지. 주제별 상세 위키 페이지의 진입점.
type: map
last_updated: 2026-10-25
sources:
  - Wiki/current_status.md
  - Wiki/combat.md
  - Wiki/skill_aim.md
  - Wiki/skill_propagation.md
  - Docs/README.md
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md
  - Docs/combat/ITEM_DROP_BALANCE_DESIGN.md
  - Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md
  - Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md
  - Docs/combat/WEAPON_PROGRESSION_DESIGN.md
  - Docs/village/VILLAGE_RUNTIME_DESIGN.md
  - Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md
  - Docs/village/BUILD_PRIORITY_DESIGN.md
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/npc/NPC_AMBITION_DESIGN.md
  - Docs/npc/NPC_COMBAT_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
  - Docs/map/DUNGEON_RUNTIME_DESIGN.md
  - Wiki/npc.md
  - Wiki/monster_spawn_runtime.md
  - Docs/
  - Assets/Scripts/
---

# Wiki 지도 (Map)

> 이 페이지는 프로젝트 전체를 **얕고 넓게** 보여주는 지도다. 각 주제를 깊게 다루지 않으며,
> 상세 내용은 원본 문서(`Docs/`)와 주제별 위키 페이지가 담당한다.
> 원본 `Docs/`는 source of truth이며 불변이다. [src: Wiki/_SCHEMA.md]

## 주제별 지도

> **현재 작업 재개 지점**은 [current_status.md](current_status.md)를 먼저 본다. 이 페이지는 코드·테이블과 최신 주제 문서를 대조해 완료·미검증·미구현을 구분한다. [src: Wiki/current_status.md]

| 주제 | 한 줄 요약 | 문서 위치 | 흩어짐 | 코드 연결도 |
|------|-----------|----------|:------:|:----------:|
| **current_status** → [current_status.md](current_status.md) | 2026-10-25 기준 구현 현황·미검증 항목·권장 재개 순서 [src: Wiki/current_status.md] | 최신 관련 Docs + 코드·테이블 대조 | — | 상 |
| **overview** | 게임 컨셉(3대 축)과 시스템별 구현 현황·로드맵 | `Docs/overview/` 2장 (gameOverview, implementationStatus) + `Docs/README.md`(대시보드) | 하 | 하 |
| **combat** → [combat.md](combat.md) | 장비 옵션·마법부여·공격 프로필, 아이템 레벨과 옵션 티어 T1~T12(구현, 2026-10-11), 장비 등급·품질·드롭 구간표·수량/희귀도·마법부여 아이템 레벨 티어·진화/품질 작업·던전 드롭·판매가 배율(구현, 2026-10-21, 플레이 확인 전), 무기 범위·장착·쌍수·표창 제외·주문 무기 성장·책별 스킬 레벨과 골드 성장의 확정 방식(미구현), 계열별 기준 성능과 장비 공백 정리, 명중 시 넉백·당기기(구현, 2026-10-12), 인벤토리 장비 비교 툴팁(구현, 2026-10-13), 땅 위 돌진·실행 단계 넉백 저항과 멧돼지 돌진(구현, 2026-10-13), 플레이어 레벨·능력치·정확도 대 회피 적중 판정·3×3 참 판(구현, 2026-10-24, 플레이 확인 전), 참 일반·마법·희귀 등급과 접두·접미·모양별 옵션·모양별 참 값 표(구현, 2026-10-25, 플레이 확인 전), 몬스터 소환과 고블린 주술사의 쿨타임 없는 긴 시전 화염탄 + 보조 소환(구현, 2026-10-25, 플레이 확인 전) [src: Wiki/combat.md, Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md, Docs/combat/SKILL_DESIGN.md §6.10·§6.11·§6.12, Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md, Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md, Docs/combat/ITEM_DROP_BALANCE_DESIGN.md, Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md, Docs/combat/WEAPON_PROGRESSION_DESIGN.md] | `Docs/combat/` + 마법부여·전투 코드와 테이블 [src: Docs/combat/, Assets/Scripts/Item/] | 상 | 상 |
| **skill_aim** → [skill_aim.md](skill_aim.md) | 커서 조준 추적·효과 커밋, 플레이어 방향 화살표 View, 스킬 사용 중 이동 감속, 몬스터 스킬도 표의 조준 방식으로 대상 추적(2026-10-13) [src: Wiki/skill_aim.md, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15] | `Docs/combat/POE2_CURSOR_TRACKING_PLAN.md` + 조준·스킬·이동 코드 | 중 | 상 |
| **skill_propagation** → [skill_propagation.md](skill_propagation.md) | 연쇄·분열 공용 전파 런타임, 대상에 붙는 대기 주문, Chain Lightning 수직 슬라이스 [src: Wiki/skill_propagation.md] | `Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md` + `Skill/Propagation/`·전파 시스템 코드 | 하 | 상 |
| **ai_monster** → [monster_spawn_runtime.md](monster_spawn_runtime.md) | 몬스터 AI·데이터 주도 종 풀, 거리 기반 스폰/Warm 청크 캐시(구현·대조 완료), 공격 예고(Phase 0~2 구현), 스폰 슬롯 밖에서 주인을 따라 사라지는 소환 몬스터(2026-10-25, 상세는 combat) [src: Wiki/monster_spawn_runtime.md, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Docs/combat/SKILL_DESIGN.md §6.12] | `Docs/ai_monster/` 2장 + 런타임 스폰 제안 | 중 | 상 |
| **map** | 청크 기반 오픈월드 맵 생성·바이옴 타일셋·충돌/길찾기, 절차 던전 런타임(구현), 던전 중 월드 시뮬(구현 2026-10-19, 실플레이 확인 전), 던전 드롭(입구 지역 레벨·보스/상자 드롭, 구현 2026-10-21) [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§5.4, Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태] | `Docs/map/` (MAP_DESIGN, COLLISION_PATHFINDING_DESIGN, THEME_TILESET_REFACTOR, DUNGEON_RUNTIME_DESIGN, DUNGEON_WORLD_SIM_DESIGN 등) | 중 | 중 |
| **village** → [village.md](village.md) | 마을 자가성장 LOD·서비스와 신규 게임 전 마을 Forge Tier 1 접근성 [src: Wiki/village.md] | `Docs/village/` + 마법부여 Forge 접근성 계약 [src: Docs/village/, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md] | 중 | 상 |
| **npc** → [npc.md](npc.md) | NPC 관계·상호작용·소망(퀘스트)·자율 활동, 야망·모험가 경제(구현 P1·P2·P4), 실전투·동행·결투(B1~B5), 장비(상점 기본 아이템), 던전 소문·부탁·행적 반응·소망 ItemGift·최근 부상 사연 문구·후속 소개(NARRATIVE 1~5단계) [src: Wiki/npc.md] | `Docs/npc/` (VILLAGE_NPC_BOUNDARY, NPC_AMBITION, NPC_COMBAT, NPC_SHOP_EQUIPMENT, NPC_NARRATIVE_CONTENT 포함) | 상 | 상 |
| **world_history** → [world_history.md](world_history.md) | 절차적 역사 생성(정착지 연대기)과 LLM 내러티브 엔진(대화·소망 생성) | `Docs/world_history/` 5장 (PROCEDURAL_HISTORY_DESIGN, WORLD_HISTORY_PLAN, WORLD_EVENT_DESIGN, NARRATIVE_ENGINE_DESIGN, NARRATIVE_ENGINE_SPIKE) | 상 | 중 |
| **tech** | ECS SparseSet 최적화와 2,400 NPC 대응 파티션 세이브·32샤드·원자 커밋, 2026-10-03 저장 크기 축소(13.3MB → 6.3MB) [src: Docs/tech/SAVE_PARTITION_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3] | `Docs/tech/` 2장 (ECS_PERFORMANCE_OPTIMIZATION_PLAN, SAVE_PARTITION_DESIGN) | 하 | 중 |
| **economy** | 지역 상권·결정론 상점, NPC 닫힌 경제(전리품 판매·기본 장비 구매)는 구현, 월드 공유 물물교환 경매장은 설계 확정·미착수 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V5–V6, Docs/npc/NPC_AMBITION_DESIGN.md §9, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §4, Docs/economy/AUCTION_HOUSE_DESIGN.md] | `Docs/economy/AUCTION_HOUSE_DESIGN.md` + Village Trade 코드 | 하 | 중 |
| **_archive** | 보류·폐기된 과거 계획 (던전, Phase A 초기안, 역사 백본) | `Docs/_archive/` 5장 | — | — |

**판정 근거 (얕은 스캔 수준):**

- **흩어짐**: 폴더 내 문서 수 + 문서 간 마크다운 링크 밀도로 판단.
  npc(NPC_ACTIVITY 34개, QUEST 20개, RELATION_HISTORY 20개 링크)와 world_history(NARRATIVE_ENGINE 27개, WORLD_EVENT 29개, PROCEDURAL_HISTORY 25개 링크)가 상호 참조 밀도 최상위. [추론: Docs/ 전체 링크 스캔]
  combat은 링크는 적지만 11장으로 문서 수가 많으며, 스킬 관련 내용이 4~5개 문서(SKILL_DESIGN/SKILLBOOK/RUNE/EFFECT_IDEAS/AI_SKILL_SELECTION)에 나뉘어 있다. [src: Docs/combat/] [추론: 파일명·README 설명 기준, 본문 중복 여부는 미검증]
- **코드 연결도**: `Assets/Scripts/` 폴더·파일명 수준 확인.
  - combat: `Common/System/`의 System_Skill·System_SkillAttackProfileCalculation·System_StatCalculation·System_BuffUpdate·System_Projectile·System_AreaEffect + `Skill/`(DamageCalculator) + `AI/` 공격 핸들러 등 광범위. [src: Assets/Scripts/Common/System/, Assets/Scripts/Skill/]
  - village: `Village/`의 공통 시뮬레이션 코어·원장·배치 코드와 `System_Village*` 런타임 시스템이 연결된다. [src: Assets/Scripts/Village/, Assets/Scripts/Common/System/]
  - npc: `Npc/`(활동·전투력·장비 시드·관계·소문·내러티브)와 `Manager/NpcManager_*.cs`(Ambition·Trade·Delve·Party·Theft·Companion·Duel·Pickup 등 partial), `System_NpcSim`이 연결된다. 선물 선호 판정은 아직 미구현이고, 소망은 ItemGift 1종만 구현됐다(2026-10-10). [src: Assets/Scripts/Npc/, Assets/Scripts/Manager/, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager_NarrativeWish.cs, Wiki/npc.md §4]
  - world_history: 문서상 "보류"이지만 `History/` 15개 파일(HistoryManager, SettlementChronicleSimulator 등)과 System_SettlementSim이 이미 존재. [src: Assets/Scripts/History/]

## 상세 위키 페이지 우선순위 (종합 이득 큰 순)

1. **npc** — 7개 문서가 서로를 "정본"으로 지목하며 역할이 쪼개져 있다(관계·선물 루프는 interactionSystem §4, 소망 데이터 모델은 QUEST_SYSTEM, 호감도 변동은 NPC_ACTIVITY §8.3). 다음 작업(Phase E·관계 시스템)이 전부 이 영역이라, 어느 문서의 어느 절이 권위인지 한 장으로 종합할 이득이 가장 크다. [src: Docs/README.md]
2. **world_history** — 상호 링크 밀도가 가장 높고(문서당 14~29개), 설계(PROCEDURAL_HISTORY)·계획(WORLD_HISTORY_PLAN)·파생 설계(NARRATIVE_ENGINE)·검증(SPIKE)이 층층이 얽혀 있다. 문서상 "보류"인데 코드(`History/` 15파일, NpcNarrative*)는 이미 존재해 문서-코드 간 현황 대조 가치도 높다.
3. **combat** — 11개 문서에 combatSystem vs combatSystemCompletion, 마법부여, SKILL 계열 4~5장의 내용 분산이 있고, 관련 코드가 System/Skill/Buff/AI 여러 폴더에 걸쳐 가장 넓게 퍼져 있다. [src: Docs/combat/] [추론: 문서 간 실제 중복 정도는 본문 대조 전이므로 추정]

**village 종합 페이지 추가 완료**: 전체 책임은 `VILLAGE_RUNTIME_DESIGN.md`, 추상 LOD와 초기 발전은 `ABSTRACT_CONSTRUCTION_DESIGN.md`, 후보 선택은 `BUILD_PRIORITY_DESIGN.md`가 정본이다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md, Docs/village/BUILD_PRIORITY_DESIGN.md]

**combat 종합 페이지 갱신 완료**: 장비 옵션 글로벌화 이후의 무기 Base·고유 강화·글로벌 옵션 책임과 공격 프로필에 더해, 미장착 장비 Prefix/Postfix 랜덤 교체·추가, 비용·후보·트랜잭션·Forge UI·회귀 검증을 [combat.md](combat.md)에 종합했다. 현행 계산 규칙은 `combatSystem.md`, 마이그레이션은 `ModSystem_Changelog.md`, 마법부여의 확정 규칙·검증·후속 경계는 구현 완료 후에도 `ITEM_ENCHANTMENT_DESIGN.md`를 active 원본으로 유지한다. 문서의 구현 전 기준선과 Phase 계획은 이력으로 구분한다. [src: Wiki/combat.md, Docs/combat/combatSystem.md, Docs/combat/ModSystem_Changelog.md, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md]

## 위키 종합 필요성이 낮은 주제

- **overview** — 게임 컨셉 지도 역할은 `Docs/README.md`가 유지하지만 구현 현황은 2026-07-13 이후 드리프트됐다. 별도 장기 주제 페이지 대신 [current_status.md](current_status.md)가 최신 코드·테이블 대조를 보완한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/current_status.md]
- **tech** — ECS 성능과 세이브 파티션 문서가 각각 자기완결적이며, 세이브 현황(저장 표현·크기)은 current_status와 npc 위키가 연결한다. [src: Docs/tech/ECS_PERFORMANCE_OPTIMIZATION_PLAN.md, Docs/tech/SAVE_PARTITION_DESIGN.md, Wiki/current_status.md, Wiki/npc.md]
- **economy** — 현행 추가 문서는 경매장 1장이고 구현 전 코드 앵커까지 한 문서에 모여 있다. 지역 상권 구현 현황은 village 위키와 current_status가 연결한다. [src: Docs/economy/AUCTION_HOUSE_DESIGN.md, Wiki/village.md, Wiki/current_status.md]
- **ai_monster** — 원본 2장의 상호 의존은 얕지만, 거리 기반 스폰·Warm 청크 캐시는 맵 풀링과 몬스터 생명주기를 함께 바꾸므로 별도 구현 계약 페이지를 둔다(구현·대조 완료). [src: Wiki/monster_spawn_runtime.md]
- **map** — 3장이지만 MAP_DESIGN.md(1,031행) 한 장에 대부분이 집중돼 있어 이미 사실상 단일 정본. [추론]
- **_archive** — 보류/폐기 문서로 위키 종합 대상에서 제외.

## 참고

- 위키 운영 규칙: [_SCHEMA.md](_SCHEMA.md)
- 주제 경계가 흐린 지점: NPC_RELATION_HISTORY_DESIGN은 `Docs/npc/`에 있지만 Docs/README.md에서는 "맵/분위기·역사" 섹션에 묶여 있고, NARRATIVE_ENGINE은 world_history 소속이면서 코드는 `Scripts/Npc/`(NpcNarrative*)에 있다 — npc·world_history 위키 페이지를 만들 때 경계 정의가 필요. [src: Docs/README.md, Assets/Scripts/Npc/]
