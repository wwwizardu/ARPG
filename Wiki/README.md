# Wiki 지도 (Map)

---
name: wiki-map
description: Docs/ 전체 주제를 얕고 넓게 조망하는 지도 페이지. 주제별 상세 위키 페이지의 진입점.
type: map
last_updated: 2026-07-13
sources:
  - Docs/README.md
  - Docs/village/VILLAGE_RUNTIME_DESIGN.md
  - Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md
  - Docs/village/BUILD_PRIORITY_DESIGN.md
  - Docs/npc/VILLAGE_NPC_BOUNDARY_DESIGN.md
  - Docs/ 하위 전 폴더 (파일 목록·분량·상호 링크 수준의 얕은 스캔)
  - Assets/Scripts/ (폴더·파일명·주요 심볼 수준의 얕은 스캔)
---

> 이 페이지는 프로젝트 전체를 **얕고 넓게** 보여주는 지도다. 각 주제를 깊게 다루지 않으며,
> 상세 내용은 원본 문서(`Docs/`)와 (앞으로 만들) 주제별 위키 페이지가 담당한다.
> 원본 `Docs/`는 source of truth이며 불변이다. [src: Wiki/_SCHEMA.md]

## 주제별 지도

| 주제 | 한 줄 요약 | 문서 위치 | 흩어짐 | 코드 연결도 |
|------|-----------|----------|:------:|:----------:|
| **overview** | 게임 컨셉(3대 축)과 시스템별 구현 현황·로드맵 | `Docs/overview/` 2장 (gameOverview, implementationStatus) + `Docs/README.md`(대시보드) | 하 | 하 |
| **combat** | 실시간 전투·데미지 공식·스킬/스킬북/룬·밸런스·AI 스킬 선택 | `Docs/combat/` 9장 (combatSystem, combatSystemCompletion, BALANCE_DESIGN, SKILL_DESIGN, SKILLBOOK_DESIGN, SKILL_RUNE_DESIGN, SKILL_EFFECT_IDEAS, AI_SKILL_SELECTION_IMPLEMENTATION, ModSystem_Changelog) | 상 | 상 |
| **ai_monster** | 몬스터 AI 상태머신 패턴과 데이터 주도 스폰 풀 | `Docs/ai_monster/` 2장 (monsterAIPattern, MONSTER_SPAWN_DESIGN) | 하 | 중 |
| **map** | 청크 기반 오픈월드 맵 생성·바이옴 타일셋·충돌/길찾기 | `Docs/map/` 3장 (MAP_DESIGN, COLLISION_PATHFINDING_DESIGN, THEME_TILESET_REFACTOR) | 중 | 중 |
| **village** → [village.md](village.md) | 마을 자가성장 — 자원·건설·Tier·벽의 실시간/추상 LOD와 서비스 [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md] | `Docs/village/` 4장 (런타임·추상 건설·우선순위·성장 목표) | 중 | 상 |
| **npc** → [npc.md](npc.md) | NPC 관계·상호작용·소망(퀘스트)·자율 활동·동료·장비 | `Docs/npc/` (VILLAGE_NPC_BOUNDARY_DESIGN 포함) | 상 | 중 |
| **world_history** → [world_history.md](world_history.md) | 절차적 역사 생성(정착지 연대기)과 LLM 내러티브 엔진(대화·소망 생성) | `Docs/world_history/` 5장 (PROCEDURAL_HISTORY_DESIGN, WORLD_HISTORY_PLAN, WORLD_EVENT_DESIGN, NARRATIVE_ENGINE_DESIGN, NARRATIVE_ENGINE_SPIKE) | 상 | 중 |
| **tech** | ECS SparseSet 조회 비용 최적화 단계별 계획 | `Docs/tech/` 1장 (ECS_PERFORMANCE_OPTIMIZATION_PLAN) | 하 | 중 |
| **_archive** | 보류·폐기된 과거 계획 (던전, Phase A 초기안, 역사 백본) | `Docs/_archive/` 5장 | — | — |

**판정 근거 (얕은 스캔 수준):**

- **흩어짐**: 폴더 내 문서 수 + 문서 간 마크다운 링크 밀도로 판단.
  npc(NPC_ACTIVITY 34개, QUEST 20개, RELATION_HISTORY 20개 링크)와 world_history(NARRATIVE_ENGINE 27개, WORLD_EVENT 29개, PROCEDURAL_HISTORY 25개 링크)가 상호 참조 밀도 최상위. [src: Docs/ 전체 링크 스캔]
  combat은 링크는 적지만 9장으로 문서 수 최다이며, 스킬 관련 내용이 4~5개 문서(SKILL_DESIGN/SKILLBOOK/RUNE/EFFECT_IDEAS/AI_SKILL_SELECTION)에 나뉘어 있다. [추론: 파일명·README 설명 기준, 본문 중복 여부는 미검증]
- **코드 연결도**: `Assets/Scripts/` 폴더·파일명 수준 확인.
  - combat: `Common/System/`의 System_Skill·System_SkillAttackProfileCalculation·System_StatCalculation·System_BuffUpdate·System_Projectile·System_AreaEffect + `Skill/`(DamageCalculator) + `AI/` 공격 핸들러 등 광범위. [src: Assets/Scripts/Common/System/, Assets/Scripts/Skill/]
  - village: `Village/`의 공통 시뮬레이션 코어·원장·배치 코드와 `System_Village*` 런타임 시스템이 연결된다. [src: Assets/Scripts/Village/, Assets/Scripts/Common/System/]
  - npc: `Npc/` 10개 파일(NpcRelationEntry, DialogueTopicSelector, NpcNarrative* 등) — 구현 진행률 25~35%로 코드가 아직 성장 중. [src: Assets/Scripts/Npc/, Docs/README.md]
  - world_history: 문서상 "보류"이지만 `History/` 15개 파일(HistoryManager, SettlementChronicleSimulator 등)과 System_SettlementSim이 이미 존재. [src: Assets/Scripts/History/]

## 상세 위키 페이지 우선순위 (종합 이득 큰 순)

1. **npc** — 7개 문서가 서로를 "정본"으로 지목하며 역할이 쪼개져 있다(관계·선물 루프는 interactionSystem §4, 소망 데이터 모델은 QUEST_SYSTEM, 호감도 변동은 NPC_ACTIVITY §8.3). 다음 작업(Phase E·관계 시스템)이 전부 이 영역이라, 어느 문서의 어느 절이 권위인지 한 장으로 종합할 이득이 가장 크다. [src: Docs/README.md]
2. **world_history** — 상호 링크 밀도가 가장 높고(문서당 14~29개), 설계(PROCEDURAL_HISTORY)·계획(WORLD_HISTORY_PLAN)·파생 설계(NARRATIVE_ENGINE)·검증(SPIKE)이 층층이 얽혀 있다. 문서상 "보류"인데 코드(`History/` 15파일, NpcNarrative*)는 이미 존재해 문서-코드 간 현황 대조 가치도 높다.
3. **combat** — 문서 수 최다(9장)에 combatSystem vs combatSystemCompletion, SKILL 계열 4~5장의 내용 분산이 있고, 관련 코드가 System/Skill/Buff/AI 여러 폴더에 걸쳐 가장 넓게 퍼져 있다. [추론: 문서 간 실제 중복 정도는 본문 대조 전이므로 추정]

**village 종합 페이지 추가 완료**: 전체 책임은 `VILLAGE_RUNTIME_DESIGN.md`, 추상 LOD와 초기 발전은 `ABSTRACT_CONSTRUCTION_DESIGN.md`, 후보 선택은 `BUILD_PRIORITY_DESIGN.md`가 정본이다. [src: Docs/village/VILLAGE_RUNTIME_DESIGN.md, Docs/village/ABSTRACT_CONSTRUCTION_DESIGN.md, Docs/village/BUILD_PRIORITY_DESIGN.md]

## 위키 종합 필요성이 낮은 주제

- **overview** — `Docs/README.md` 자체가 이미 잘 관리되는 단일 대시보드(지도) 역할을 하고 있다.
- **tech** — 단일 문서 1장으로 자기완결적.
- **ai_monster** — 2장뿐이고 상호 의존이 얕다.
- **map** — 3장이지만 MAP_DESIGN.md(1,031행) 한 장에 대부분이 집중돼 있어 이미 사실상 단일 정본. [추론]
- **_archive** — 보류/폐기 문서로 위키 종합 대상에서 제외.

## 참고

- 위키 운영 규칙: [_SCHEMA.md](_SCHEMA.md)
- 주제 경계가 흐린 지점: NPC_RELATION_HISTORY_DESIGN은 `Docs/npc/`에 있지만 Docs/README.md에서는 "맵/분위기·역사" 섹션에 묶여 있고, NARRATIVE_ENGINE은 world_history 소속이면서 코드는 `Scripts/Npc/`(NpcNarrative*)에 있다 — npc·world_history 위키 페이지를 만들 때 경계 정의가 필요. [src: Docs/README.md, Assets/Scripts/Npc/]
