# 🎮 ARPG 프로젝트 대시보드

> **코어 키퍼** 스타일 오픈월드 탐험 + **림월드** 스타일 NPC 자율 시뮬레이션
>
> **현재 진행도**: ████████████████████░░░░░░░░░░ **60%**

**마지막 업데이트**: 2026-10-03 (빠른 참조의 NPC·던전 행과 기획 문서 목록·상태). 아래 "구현 완료 시스템 / 진행 중 / 미구현" 절의 비율과 상세는 2026-07-13 기준이다 — 최신 구현 현황은 [Wiki/current_status.md](../Wiki/current_status.md)를 본다.

---

## 📋 빠른 참조

| 항목 | 상태 | 다음 단계 |
|------|------|----------|
| **전투 시스템** | ✅ 95% | VFX / 사운드 / 넉백 (후순위) |
| **AI 시스템** | ✅ 90% | 몬스터별 세부 패턴, 길찾기 (보스급 다페이즈) |
| **스킬 시스템** | ✅ 95% | 스킬북 장착 운용, 룬/이펙트 |
| **마을 자가성장 런타임** | ✅ 구현 | Unity LOD·세이브 왕복 검증, 폐허 전환 |
| **여관(Inn)** | ✅ 휴식(+6h)·세이브 건물 서비스 | 콘텐츠 확장 |
| **상점 / 강화 / 제단 서비스** | ✅ 코드 완료 | 콘텐츠 확장 |
| **NPC 관계 시스템** | 🚧 25% | 관계 변동 로직, N:N 관계 |
| **관계·선물 루프** | ❌ 0% | 대화·유추·선물·보답(interactionSystem §4) + NPC 소망 추적(QUEST_SYSTEM). 옛 "퀘스트 시스템" 대체 |
| **던전 시스템** | ✅ 구현 (2026-09-19) — 마을별 2~3 슬롯·입장/퇴장·보스 완료·동행·NPC 완료 | 함정, 생성 재시도, 배치 실패율 진단, Profiler 실측 |
| **NPC 모험가 경제 (야망)** | 🟨 부분 구현 — P1·P2·P4, 파티·부상, 상점 기본 장비 | 마을 골드 밸런스, P3 협력·배신(보류), P5·P6 |
| **NPC 실전투** | ✅ B1~B5 (2026-09-27) — 조건부 전투·던전 동행·결투 | 도주·NPC 간 결투 화면 장면(보류) |
| **절차적 역사 생성 (백본)** | ✅ 코드 완료 (R0~R4 대부분 + RL1-3) | Unity 플레이 검증, R4 회고 대사, 멀리 정치 사건 지속 |

---

## 🎯 게임 핵심 컨셉

### 3대 축
```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  플레이어 액션   │ ←→ │  마을 자가성장   │ ←→ │  서비스 / 거래   │
│                 │     │                 │     │                 │
│ • 실시간 전투    │     │ • 자원 패시브    │     │ • 상점 양방향   │
│ • 오픈월드 탐험  │     │ • 오브젝트 배치  │     │ • 강화/여관     │
│ • 스킬북 장착    │     │ • Tier 승격     │     │ • 제단 서비스   │
│ • 장비 성장      │     │ • 외곽 벽 인프라 │     │ • 무인 건물     │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

### 게임 루프
```
[오픈월드 탐험] → [몬스터/자원 획득] → [마을 귀환]
                                            ↓
[다시 탐험] ← [강화/구매/휴식] ← [상점에 매각, 골드 확보]
```

---

## 📚 기획 문서

### 🌍 전체 개요 & 컨벤션
- **[gameOverview.md](overview/gameOverview.md)** — 게임 3대 축, Phase 1~3, 기술 스택
- **[implementationStatus.md](overview/implementationStatus.md)** ⭐ — 시스템별 구현 현황 + 로드맵
- **[../CLAUDE.md](../CLAUDE.md)** — 코딩 컨벤션, 커스텀 ECS API

### ⚔️ 전투 / 스킬
| 문서 | 내용 |
|------|------|
| **[combatSystem.md](combat/combatSystem.md)** | 실시간 전투, 페이즈, 데미지 6단계 공식 |
| **[combatSystemCompletion.md](combat/combatSystemCompletion.md)** | 전투 완성 로드맵 (사실상 완료) |
| **[BALANCE_DESIGN.md](combat/BALANCE_DESIGN.md)** | PoE2 톤 밸런스 곡선 / 방어 공식 / TTK |
| **[SKILL_DESIGN.md](combat/SKILL_DESIGN.md)** | 현재 시스템 위 스킬 카탈로그 |
| **[SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md](combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md)** | 주문·투사체 공용 연쇄/분열 전파 시스템 — Instant 연쇄(Chain Lightning) 구현, Projectile·분열·OnHit은 PlayMode 검증 전 |
| **[SKILLBOOK_DESIGN.md](combat/SKILLBOOK_DESIGN.md)** | 스킬북 장착식 스킬 (코드 완료) |
| **[SKILL_RUNE_DESIGN.md](combat/SKILL_RUNE_DESIGN.md)** | 스킬 룬 / 모디파이어 |
| **[SKILL_EFFECT_IDEAS.md](combat/SKILL_EFFECT_IDEAS.md)** | 스킬 이펙트 아이디어 |
| **[AI_SKILL_SELECTION_IMPLEMENTATION.md](combat/AI_SKILL_SELECTION_IMPLEMENTATION.md)** | AI 다중 스킬 가중치 선택 |
| **[MONSTER_ATTACK_TELEGRAPH_DESIGN.md](combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md)** | 몬스터 공격 범위·피해 타이밍 예고와 중앙 배치 렌더링 — Phase 0~2 구현(2026-09-19), Phase 3 일부 |
| **[ModSystem_Changelog.md](combat/ModSystem_Changelog.md)** | 장비/스탯 mod 시스템 변경 이력 |
| **[ITEM_LEVEL_MOD_TIER_DESIGN.md](combat/ITEM_LEVEL_MOD_TIER_DESIGN.md)** | 아이템 레벨과 장비 옵션 티어 T1~T12: 레벨 구간·가중치, T11·T12 등급별 특수 판정 — 구현(2026-10-11), 플레이 확인 전 |

### 🤖 AI / 몬스터
| 문서 | 내용 |
|------|------|
| **[monsterAIPattern.md](ai_monster/monsterAIPattern.md)** | 몬스터 AI 패턴 |
| **[MONSTER_SPAWN_DESIGN.md](ai_monster/MONSTER_SPAWN_DESIGN.md)** | 데이터 주도 스폰 풀 (Zone × 등급) |
| **[MONSTER_RARITY_DESIGN.md](ai_monster/MONSTER_RARITY_DESIGN.md)** | 몬스터 등급(마법·희귀): 개체 등급·무리 단위 판정·등급별 스탯·드랍 보상·HP바 색, 무작위 접사 확장 지점 — 필드 구현(2026-10-11) |
| **[MONSTER_AFFIX_DESIGN.md](ai_monster/MONSTER_AFFIX_DESIGN.md)** | 몬스터 접사: 등급별 추가 판정 개수(최대 10)·무리 단위 판정·스탯형/오라형 접사 시트·속성별 피해 증가 스탯·접사당 드랍 판정 — 필드 구현(2026-10-11), 플레이 확인 전 |
| **[COLLISION_PATHFINDING_DESIGN.md](map/COLLISION_PATHFINDING_DESIGN.md)** | 축 분리 슬라이딩 + 타일 A* |

### 🏘️ 마을 / NPC 자가성장 ⭐
| 문서 | 단계 | 상태 |
|------|------|------|
| **[VILLAGE_RUNTIME_DESIGN.md](village/VILLAGE_RUNTIME_DESIGN.md)** | 마을 전체 책임·데이터 흐름 | 현행 정본 |
| **[ABSTRACT_CONSTRUCTION_DESIGN.md](village/ABSTRACT_CONSTRUCTION_DESIGN.md)** | 실시간/추상 건설·materialize·초기 발전 | 현행 정본 |
| **[BUILD_PRIORITY_DESIGN.md](village/BUILD_PRIORITY_DESIGN.md)** | 자가 건설 후보 점수·대기 정책 | 현행 정본 |
| **[VILLAGE_NPC_BOUNDARY_DESIGN.md](npc/VILLAGE_NPC_BOUNDARY_DESIGN.md)** | 경제 인구와 물리 NPC의 경계 | 현행 정본 |
| **[VILLAGE_GROWTH_STAGES.md](village/VILLAGE_GROWTH_STAGES.md)** | Stage 0~4 장기 콘텐츠 목표 | 장기 기획 |

### 🗺️ 맵 / 분위기
| 문서 | 내용 |
|------|------|
| **[MAP_DESIGN.md](map/MAP_DESIGN.md)** | 청크 8×8, Zone, 비트 레이아웃, 유적 단편 |
| **[THEME_TILESET_REFACTOR.md](map/THEME_TILESET_REFACTOR.md)** | 바이옴 비주얼 차별화 리팩토링 |
| **[DUNGEON_RUNTIME_DESIGN.md](map/DUNGEON_RUNTIME_DESIGN.md)** | 절차 던전 런타임 — 입구 배치·입장/퇴장·완료·저장 (✅ 구현 2026-09-19, 함정 등 일부 미구현) |
| **[DUNGEON_WORLD_SIM_DESIGN.md](map/DUNGEON_WORLD_SIM_DESIGN.md)** | 던전 중 월드 시뮬 계속 실행 (설계 초안 2026-10-03, 검토 대기) |
| **[FOG_OF_WAR_DESIGN.md](map/FOG_OF_WAR_DESIGN.md)** | 플레이어 시야·안개(시야 밖 거리 어둡기), 벽·시설 차단(반투명 그림자), 이동 엔티티 숨김 (✅ 구현 2026-10-10, 사용자 실플레이 확인 전) |
| **[PROCEDURAL_HISTORY_DESIGN.md](world_history/PROCEDURAL_HISTORY_DESIGN.md)** | 정착지 통폐합 / 연대기 시뮬 (런타임 LOD) |
| **[NPC_RELATION_HISTORY_DESIGN.md](npc/NPC_RELATION_HISTORY_DESIGN.md)** | NPC 개인 관계 (가족/원한/호감도) |
| **[WORLD_HISTORY_PLAN.md](world_history/WORLD_HISTORY_PLAN.md)** | 위 두 문서 구현 계획 (R0~R4) |
| **[NARRATIVE_ENGINE_DESIGN.md](world_history/NARRATIVE_ENGINE_DESIGN.md)** ⭐ | 역사·관계 → LLM 대화·선호·소망 *생성* (핵심 재미 검증 1순위) |
| **[NARRATIVE_ENGINE_SPIKE.md](world_history/NARRATIVE_ENGINE_SPIKE.md)** | 위 엔진 **검증 스파이크** — Groq/Llama로 대화+소망 실현 가능성 확인 (🟨 1차 구현 완료 2026-07-02, Unity 실측 전) |
| **[QUEST_SYSTEM_DESIGN.md](npc/QUEST_SYSTEM_DESIGN.md)** ⭐ | 퀘스트 시스템 — NPC **소망(Wish)** 추적·완료·보상·기록 (소망 데이터 모델 정본). 관계·선물 루프는 interactionSystem §4 |
| [archive/PROCEDURAL_HISTORY_BACKBONE.md](_archive/PROCEDURAL_HISTORY_BACKBONE.md) | 깊은 과거 백본 (템플릿 4시대) — ⛔ 미고려/아카이브 |

### 🤝 NPC 활동 / 동료 / 상호작용
| 문서 | 상태 |
|------|------|
| **[companionSystem.md](npc/companionSystem.md)** | 설계 완료, 미구현 |
| **[interactionSystem.md](npc/interactionSystem.md)** | 설계 완료, 미구현 — 30행동·관계 4축 + **관계·선물 몰입 루프**(§4: 대화·선호·유추 선물·보답) |
| **[NPC_ACTIVITY_DESIGN.md](npc/NPC_ACTIVITY_DESIGN.md)** ⭐ | 설계 초안 — 모험가 자율 행동/원정 (몬스터 처치·던전 탐험 파티, 부상·죽음, 월드이벤트 트리거). 활동 *내용* 정본 (⚠ 정주/작업자 분류는 단순화로 폐기 — 모험가 단일. 시뮬 *프레임*은 NPC_SIMULATION으로 승격) |
| **[NPC_SIMULATION_DESIGN.md](npc/NPC_SIMULATION_DESIGN.md)** ⭐ | ✅ MVP 구현 — 개별 NPC LOD 시뮬 *기반*: 근처=풀 ECS/멀리=경량 상태 tick, Rest·Hunt·Trade·DungeonDelve(솔로·파티), RNG salt 정본(§11.1). 이주·WORLD_EVENT는 후속. NPC 시뮬 아키텍처 정본 |
| **[NPC_AMBITION_DESIGN.md](npc/NPC_AMBITION_DESIGN.md)** | 🟨 부분 구현 — P1(09-28)·P2 던전·소문(09-29)·파티 자동 구성 개정 D85~D90·부상 등급 D91~D96(09-30)·P4 NPC 간 절도(10-02) 구현, 상점 기본 장비 D116~D123(10-03, 커밋 대기). P3 협력·배신 ⏸ 보류, P5 양방향 배신·결투 명분/처단·P6 Forge·경매장 미구현. NPC 야망·모험가 경제: 성격→야망→활동 효용 선택, 장비 전투력·소문, 닫힌 경제 상점 거래. 결정표 정본(D1~D123) |
| **[NPC_COMBAT_DESIGN.md](npc/NPC_COMBAT_DESIGN.md)** | ✅ B 단계 구현 완료 (B1~B5, 2026-09-27) — NPC_AMBITION에서 분리. 진영·조건부 인식, 던전 전용 임시 동행(D97 최종), 플레이어↔NPC 결투, 드랍 줍기(디버그 호출만), 전투 사망 연결. 보류: 도주(D25), NPC 간 결투 화면 장면(D24) |
| **[NPC_EQUIPMENT_DESIGN.md](npc/NPC_EQUIPMENT_DESIGN.md)** | ✅ 구현 — NPC 장비 12슬롯·인벤토리 5칸·시작 장비 시드·검사 UI(UINpcInspect) |
| **[NPC_EQUIPMENT_SET_PLAN.md](npc/NPC_EQUIPMENT_SET_PLAN.md)** | ✅ 구현 (2026-08-23) — NPC 5종 시작 장비 세트·슬롯별 장착 확률. 플레이 모드 검증 기록 없음 |
| **[NPC_SHOP_EQUIPMENT_DESIGN.md](npc/NPC_SHOP_EQUIPMENT_DESIGN.md)** | ✅ 구현 (2026-10-03, 커밋 대기) — NPC 장비 = 상점용 기본 아이템, 상점 기본 장비 칸, 전리품 미장착, 장비 저장 표현·일괄 교체(D116~D123) |
| **[dungeonSystem.md](_archive/dungeonSystem.md)** | 옛 던전 설계 (아카이브) — 현행 던전은 [DUNGEON_RUNTIME_DESIGN.md](map/DUNGEON_RUNTIME_DESIGN.md) |

### 🛠️ 기술 / 시스템
| 문서 | 내용 |
|------|------|
| **[ECS_PERFORMANCE_OPTIMIZATION_PLAN.md](tech/ECS_PERFORMANCE_OPTIMIZATION_PLAN.md)** | SparseSet 조회 비용 감소 단계별 계획 |
| **[SAVE_PARTITION_DESIGN.md](tech/SAVE_PARTITION_DESIGN.md)** | 도메인 파티션·NPC 32샤드·manifest 원자 커밋 세이브 정본 |

### 📂 아카이브
- **[_archive/](_archive)** — 과거 계획과 체크리스트 보관

---

## ✅ 구현 완료 시스템

### 기반 시스템
| 시스템 | 비고 |
|--------|------|
| **ECS 아키텍처** | Component / System / SparseSet / EntityIdHelper |
| **맵 시스템** | 8×8 청크, Perlin Noise, BaseTown 오버레이, 결정론 |
| **입력/이동/애니메이션** | Unity Input System 통합, 8방향 |
| **시간 시스템** | 게임 시간 + 배속 조절 |
| **렌더링** | URP 2D, 스프라이트 |

### 전투 / AI (95%↑)
| 시스템 | 비고 |
|--------|------|
| **스킬 시스템** | WindUp→Active→Recovery, SkillTag(Flags), 쿨타임 감소 |
| **스킬북 장착** | 인벤토리 책 → 슬롯 장착 → SkillId 갱신 |
| **데미지 계산** | 6단계 공식, 치명타/회피/막기, 속성 5종 + 저항 |
| **상태이상** | 점화/냉기/중독 독립 판정, DoT, 스택 |
| **버프 시스템** | StackCount, 결정적 ID, StatModifier 통합 |
| **스탯 시스템** | StatComponent 재계산, FinalElementAttack |
| **발사체** | System_Projectile + AddressablePool 공용 풀 |
| **AI 상태 머신** | Idle/Patrol/Chase/Attack/Retreat/Return |
| **AI 행동** | Melee / Ranged + AiTable 다중 스킬 가중치 |
| **데미지 텍스트** | FloatingTextManager + DOTween, 타입별 색상 |

### 마을 자가성장
| 시스템 | 비고 |
|--------|------|
| **자원 생산** | 추상 경제 인구를 제한된 생산 인구로 압축해 Food/Wood/Stone/Iron/Gold 생산. 실시간/추상 경로가 `VillageResourceSimulation` 공유 |
| **자원 Cap** | Woodpile +100, Stockpile +80 등 데이터 주도 |
| **범용 배치 큐 (자가 건설)** | ObjectPlacementTaskComponent + System_VillageBuildQueue — 타이머로 자가 완성, 자원 소모 유지 (2026-07-08 단순화 D4 — NPC 배정 제거) |
| **Tier 승격** | Settlement→Hamlet→Village→Town. 2026-08 이후 마을은 `InitialStage`까지 완공된 **초기 완성형**으로 등장하고 런타임 자동 승격·건설은 `RuntimeProgressionEnabled=false`로 꺼져 있다(V8 후속) — [Wiki/village.md](../Wiki/village.md) |
| **외곽 벽 인프라** | Palisade + Gate, Hamlet 진입 시 자동 계획 |
| **PlacedObject Registry** | ECS 승격, tableId/tile 양방향 인덱스 |
| **세트 판정** | ObjectSetCatalog (Forge/Inn/Birth 등) |
| **ProvidedService 비트마스크** | Shop/Forge/Inn/Shrine |
| **서비스 근접 집계** | System_VillageServiceProximity (0.3s) + F키 라우팅 |
| **상점 양방향 거래** | 결정론 재고(보장·특산·희귀·특수 + 기본 장비 칸) + 매각 + 자원 환원. NPC 모험가도 같은 상점에서 닫힌 경제로 거래([NPC_AMBITION §7.2](npc/NPC_AMBITION_DESIGN.md), [NPC_SHOP_EQUIPMENT §4](npc/NPC_SHOP_EQUIPMENT_DESIGN.md)) |
| **필요도 스코어** | `VillageNeedsEvaluator`를 착공 시점에 inline 호출. 존재·주거·Food Cap·Tier 게이트·세트·잉여 점수 |
| **원거리 성장** | `VillageAbstractConstruction`이 자원·건설·Tier·벽을 시간 분할 진행하고 materialize 전에 catch-up |

### 기타
| 시스템 | 비고 |
|--------|------|
| **아이템 / 인벤토리 / 장비** | ItemTable, 드랍, UI 완성 |
| **충돌 / 길찾기** | 축 분리 슬라이딩 + 타일 A* (Physics2D 미사용) |
| **테이블 / 데이터 시스템** | .bytes 바이너리 + ImmutableDictionary, Addressable 로드 |
| **치트 시스템** | UICheat (개발용) |

---

## 🚧 진행 중

### NPC 관계 시스템 (25%)
- ✅ NpcManager 호감도/관계 데이터 + 초기 backfill (구 RelationshipComponent/System_Relationship 폐기)
- ✅ 런타임 호감도 변동(활동 기반) 일부: 파티 원정 결과(D85~D90·D108), 절도 발각(D102) — [NPC_AMBITION](npc/NPC_AMBITION_DESIGN.md) §2
- ⬜ N:N 확장 / 이벤트 연동 (설계 [NPC_ACTIVITY §8.3](npc/NPC_ACTIVITY_DESIGN.md), 패시브 drift 폐기)

### NPC 일과 / 자율 의사결정
- ✅ 물리 NPC는 모험가이며 마을 경제 인구와 분리 ([VILLAGE_NPC_BOUNDARY_DESIGN](npc/VILLAGE_NPC_BOUNDARY_DESIGN.md))
- ✅ `System_NpcSim` 기반 Rest·Hunt·Trade·DungeonDelve(솔로·파티)와 오프스크린 LOD, 야망 효용 선택 — [NPC_SIMULATION §11.1](npc/NPC_SIMULATION_DESIGN.md), [NPC_AMBITION](npc/NPC_AMBITION_DESIGN.md)
- ✅ 소문 교환·NPC 던전 완료·부상 등급·NPC 간 절도, 상점 기본 장비 구매로 성장 (2026-09-28~10-03)
- ⬜ 이주·WORLD_EVENT 연동, 협력·배신(P3 보류) ([NPC_ACTIVITY_DESIGN](npc/NPC_ACTIVITY_DESIGN.md), [NPC_AMBITION §13](npc/NPC_AMBITION_DESIGN.md))

### UI 시스템 (50%)
- ✅ Inventory / Character / Shop / Forge / Inn / Shrine / MainMenu / HpBar / FloatingText
- ⬜ 미니맵 / 관계·선물 UI / 대화 UI / 파티 UI

### 절차적 역사 생성 (백본 코드 완료 — 2026-07-08)
- ✅ 연대기 시뮬(R1) · 관계 backfill(R2) · Live 연대기(R3) · NpcInspect 관계 탭(R4 일부)
- ✅ 런타임 LOD(RL1-3): 추상 인구 틱 + materialize/catch-up + fold-back — 추상 인구가 마을 경제(`v.Population`)의 입력원 (NPC 단순화 연동)
- ⬜ Unity 플레이 검증, R4 회고 대사·Sentiment 표시, 멀리 정치 사건 지속(보류) — [WORLD_HISTORY_PLAN.md](world_history/WORLD_HISTORY_PLAN.md)

---

## ❌ 미구현

| 우선순위 | 시스템 | 의존성 |
|---------|--------|--------|
| 🔥 **High** | NPC 이주 확장 (파티 활동은 구현됨) | NPC 시뮬레이션 |
| 🔥 **High** | 정착지 분가 / 이탈 | Population 확장 |
| 🔥 **High** | NPC 상호작용 (30가지 행동) | 관계 시스템 |
| 🔥 **High** | 관계·선물 루프 (대화·선호·선물·보답, interactionSystem §4) | NPC 시스템 |
| 🔥 **High** | 퀘스트 — NPC 소망 (떠오름·추적·보상) | 관계·선물 루프 |
| 📝 **Medium** | 대화 시스템 (LLM 통합 옵션) | NPC 시스템 |
| 📝 **Medium** | 이벤트 / 침공 | 위협도, 벽 파괴 |
| 📝 **Medium** | 동료 영입 / 명령 (던전 전용 임시 동행은 구현됨, NPC_COMBAT D97) | 관계 시스템 |
| 📝 **Medium** | 장비 제작 | 아이템 시스템 |
| 🎨 **Low** | 세이브/로드 보강 (파티션 세이브·32샤드·원자 커밋은 구현됨, [SAVE_PARTITION_DESIGN](tech/SAVE_PARTITION_DESIGN.md)) | 모든 시스템 |
| 🎨 **Low** | 사운드 / VFX 통합 | — |
| 🎨 **Low** | 카메라 개선 / 튜토리얼 / 로컬라이제이션 | — |
| 🎨 **Low** | 절차적 역사 잔여 (R4 회고 대사, 멀리 정치 사건 지속) | 대화·소망 |

---

## 🗓️ 마을 다음 기능

- Unity 플레이 모드에서 추상↔실체화와 세이브 왕복 검증
- 런타임 폐허 전환의 원자적 진입점
- 정착지 분가·이탈
- 위협도·몬스터 침공·벽 파괴와 복구
- City 콘텐츠, StoneWall, WatchTower

---

## 🎨 핵심 게임플레이 (현재 가능)

```
🗺️  오픈월드 탐험
  └─ 청크 기반, Perlin Noise, Zone 1~99, 결정론

⚔️  실시간 전투
  └─ 5속성 데미지 + 저항, 상태이상, 발사체 풀링, 가중치 AI

📖  스킬북 장착
  └─ 인벤토리 책을 슬롯에 장착 → 스킬 갱신

🏘️  자가성장 마을
  └─ 오브젝트 단위 자가 건설, 4-Stage Tier, 외곽 벽 자동

🛒  마을 서비스
  └─ Shop(양방향 거래) / Forge(강화) / Inn(휴식) / Shrine — 무인 건물

🕳️  절차 던전
  └─ 마을 주변 입구 → E 입장 → 보스 완료 → 새 입구, 던전 전용 NPC 동행

🧭  모험가 NPC
  └─ 사냥·던전·상점 거래·소문·절도를 스스로 하고, 플레이어와 결투 가능
```

---

## 🛠️ 기술 스택

### Unity
- **Engine**: Unity 6000.0.42f1 (LTS)
- **Rendering**: URP 17.0.4 (2D)
- **Input**: Unity Input System 1.13.1

### 커스텀 ECS
- **Component**: 순수 데이터 struct
- **System**: ISystem 인터페이스 (Update / FixedUpdate / LateUpdate)
- **Storage**: SparseSet (O(1) 접근)
- **NOT Unity DOTS**: 자체 구현
- **Priority 대역**: Village 도메인은 50-69 (Resource 50-54 / Population 55-59 / Lifecycle 60-64 / Construction 65-69)

### 데이터 / 인프라
- **테이블**: `.bytes` 바이너리 + `ImmutableDictionary<int, T>`
- **세이브**: Newtonsoft JSON (`WorldData`, `VillageData`)
- **에셋**: Addressables (lazy 로드 + 캐시)
- **오브젝트 풀**: AddressablePool (string 키 공용)
- **충돌**: 축 분리 슬라이딩 + 타일 A* (Physics2D 미사용)

---

## 📝 코딩 컨벤션 (요약)

- **멤버 변수**: `_variableName` (언더스코어)
- **메서드 순서**: public → protected → private
- **루프**: `for` 사용 (foreach 금지, GC 회피)
- **거리 체크**: `SqrMagnitude` 사용
- **조건문**: 명시적 비교 (`== false`, `== null`)
- **데이터 정본화**: 코드에 TableId 분기 금지, 의미는 컬럼에 박는다
- **하드코딩 금지**: BuildableItemTable / SetMemberTag 등 시트 + 사전으로 표현

자세한 내용: [../CLAUDE.md](../CLAUDE.md)

---

## 🚀 빠른 시작 가이드

### 1. 프로젝트 파악
```
1. 이 문서 (README.md) — 전체 지도
2. gameOverview.md — 컨셉
3. implementationStatus.md — 시스템별 현황
4. CLAUDE.md — 코딩 규약 + ECS API
```

### 2. 시스템 이해 (관심 영역별)
```
전투/스킬     → combatSystem.md → SKILL_DESIGN.md → BALANCE_DESIGN.md
마을 시뮬     → VILLAGE_RUNTIME_DESIGN.md → ABSTRACT_CONSTRUCTION_DESIGN.md / BUILD_PRIORITY_DESIGN.md
맵/탐험       → MAP_DESIGN.md → MONSTER_SPAWN_DESIGN.md
AI            → AI_SKILL_SELECTION_IMPLEMENTATION.md → monsterAIPattern.md
충돌/길찾기   → COLLISION_PATHFINDING_DESIGN.md
```

### 3. 구현 시작
```
1. implementationStatus.md에서 "다음 작업" 확인
2. 해당 시스템 문서 + 관련 Phase 문서 참조
3. 코딩 컨벤션 (CLAUDE.md) 준수
4. 완료 후 implementationStatus.md / 본 문서 업데이트
```

---

## 📂 폴더 구조

```
Assets/Scripts/
├── Manager/          # GlobalManager, ComponentManager, SystemManager,
│                     # DataManager, MapManager, VillageManager, MonsterManager,
│                     # ItemManager, BuildingManager, TimeManager 등
├── Common/
│   ├── Component/    # ECS 컴포넌트 (struct)
│   ├── System/       # ECS 시스템 (ISystem)
│   ├── Interface/    # ISystem 인터페이스
│   └── SparseSet.cs  # ECS 컴포넌트 저장소
├── AI/               # AI 행동 (Melee/Ranged Behavior, AIStateHelper)
├── Buff/             # 버프 시스템 (BuffInstance, BuffSystem)
├── Skill/            # 스킬 헬퍼, DamageCalculator
├── Creature/         # 캐릭터 구현
├── Map/              # 청크 / 타일 / 바이옴
├── Village/          # 마을 자가성장 관련
├── Data/             # DataManager_Table, 세이브 데이터
├── Factory/          # EntityFactory, BuildingFactory
├── UI/               # UIInventory, UIShop, UIForge, UIInn, UIShrine, 등
└── Editor/           # DownloadTables, 에디터 메뉴
```

---

## 🎯 핵심 목표

> **"플레이어가 직접 싸우는 액션 RPG에서, 마을은 시간이 흐르면 추상 인구를 바탕으로 알아서 자라고, 플레이어는 그 마을의 상점·강화·여관을 통해 진짜 이득을 얻으며, NPC들은 모험가로서 세계를 누비고 그들이 겪은 일이 관계와 역사로 쌓인다."**

**현재 위치**: 마을 실시간/추상 자가성장과 전투/AI 핵심 로직 구현 (60%)
**다음 목표**: 마을 LOD 통합 검증·폐허 전환·분가와 NPC 관계 시스템

---

**문서 관리 원칙**:
- 매주 진행도 업데이트는 [implementationStatus.md](overview/implementationStatus.md)
- 주요 기능의 정본이나 구현 상태가 바뀌면 빠른 참조와 관련 Wiki를 함께 업데이트
- 새 설계 문서 추가 시 본 문서의 "기획 문서" 섹션에 링크 등록

**마지막 업데이트**: 2026-07-09
