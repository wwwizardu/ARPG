# 🎮 ARPG 프로젝트 대시보드

> **코어 키퍼** 스타일 오픈월드 탐험 + **림월드** 스타일 NPC 자율 시뮬레이션
>
> **현재 진행도**: ████████████████████░░░░░░░░░░ **60%**

**마지막 업데이트**: 2026-05-20

---

## 📋 빠른 참조

| 항목 | 상태 | 다음 단계 |
|------|------|----------|
| **전투 시스템** | ✅ 95% | VFX / 사운드 / 넉백 (후순위) |
| **AI 시스템** | ✅ 90% | 몬스터별 세부 패턴, 길찾기 (보스급 다페이즈) |
| **스킬 시스템** | ✅ 95% | 스킬북 장착 운용, 룬/이펙트 |
| **마을 자가성장 Phase A~D** | ✅ 완료 | Phase E (배후 시뮬, 출생/분가) |
| **여관(Inn) 고용 시스템** | ✅ 코드 완료 | 시트 데이터 입력 |
| **상점 / 강화 / 제단 서비스** | ✅ 코드 완료 | 콘텐츠 확장 |
| **NPC 관계 시스템** | 🚧 25% | 관계 변동 로직, N:N 관계 |
| **관계·선물 루프** | ❌ 0% | 대화·유추·선물·보답(interactionSystem §4) + NPC 소망 추적(QUEST_SYSTEM). 옛 "퀘스트 시스템" 대체 |
| **던전 시스템** | ⏸ 보류 | 오픈월드로 대체 운영 중 |
| **절차적 역사 생성** | ⏸ 보류 | 대화·소망 시스템 이후 본 구현 |

---

## 🎯 게임 핵심 컨셉

### 3대 축
```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  플레이어 액션   │ ←→ │  마을 자가성장   │ ←→ │  서비스 / 거래   │
│                 │     │                 │     │                 │
│ • 실시간 전투    │     │ • 자원 패시브    │     │ • 상점 양방향   │
│ • 오픈월드 탐험  │     │ • 오브젝트 배치  │     │ • 강화/여관     │
│ • 스킬북 장착    │     │ • Tier 승격     │     │ • Inn 고용      │
│ • 장비 성장      │     │ • 외곽 벽 인프라 │     │ • NPC 직무      │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

### 게임 루프
```
[오픈월드 탐험] → [몬스터/자원 획득] → [마을 귀환]
                                            ↓
[다시 탐험] ← [고용/강화/구매] ← [상점에 매각, 골드 확보]
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
| **[SKILLBOOK_DESIGN.md](combat/SKILLBOOK_DESIGN.md)** | 스킬북 장착식 스킬 (코드 완료) |
| **[SKILL_RUNE_DESIGN.md](combat/SKILL_RUNE_DESIGN.md)** | 스킬 룬 / 모디파이어 |
| **[SKILL_EFFECT_IDEAS.md](combat/SKILL_EFFECT_IDEAS.md)** | 스킬 이펙트 아이디어 |
| **[AI_SKILL_SELECTION_IMPLEMENTATION.md](combat/AI_SKILL_SELECTION_IMPLEMENTATION.md)** | AI 다중 스킬 가중치 선택 |
| **[ModSystem_Changelog.md](combat/ModSystem_Changelog.md)** | 장비/스탯 mod 시스템 변경 이력 |

### 🤖 AI / 몬스터
| 문서 | 내용 |
|------|------|
| **[monsterAIPattern.md](ai_monster/monsterAIPattern.md)** | 몬스터 AI 패턴 |
| **[MONSTER_SPAWN_DESIGN.md](ai_monster/MONSTER_SPAWN_DESIGN.md)** | 데이터 주도 스폰 풀 (Zone × 등급) |
| **[COLLISION_PATHFINDING_DESIGN.md](map/COLLISION_PATHFINDING_DESIGN.md)** | 축 분리 슬라이딩 + 타일 A* |

### 🏘️ 마을 / NPC 자가성장 ⭐
| 문서 | 단계 | 상태 |
|------|------|------|
| **[VILLAGE_GROWTH_STAGES.md](village/VILLAGE_GROWTH_STAGES.md)** | 마을 성장 전체 기획 (Stage 0~4) | 본 문서 |
| **[VILLAGE_PHASES.md](village/VILLAGE_PHASES.md)** | Phase A~D 통합 요약 (MVP → BuildQueue → Tier 승격 + 외벽 → 서비스 + 직무) | ✅ 코드 완료 |
| **[BUILD_PRIORITY_DESIGN.md](village/BUILD_PRIORITY_DESIGN.md)** | 자가 건설 4계층 가중치 점수 |
| **[INN_HIRING_DESIGN.md](village/INN_HIRING_DESIGN.md)** | 여관 기반 NPC 고용 (자연 이민 대체) |

### 🗺️ 맵 / 분위기
| 문서 | 내용 |
|------|------|
| **[MAP_DESIGN.md](map/MAP_DESIGN.md)** | 청크 8×8, Zone, 비트 레이아웃, 유적 단편 |
| **[THEME_TILESET_REFACTOR.md](map/THEME_TILESET_REFACTOR.md)** | 바이옴 비주얼 차별화 리팩토링 |
| **[PROCEDURAL_HISTORY_DESIGN.md](world_history/PROCEDURAL_HISTORY_DESIGN.md)** | 정착지 통폐합 / 연대기 시뮬 (런타임 LOD) |
| **[NPC_RELATION_HISTORY_DESIGN.md](npc/NPC_RELATION_HISTORY_DESIGN.md)** | NPC 개인 관계 (가족/원한/호감도) |
| **[WORLD_HISTORY_PLAN.md](world_history/WORLD_HISTORY_PLAN.md)** | 위 두 문서 구현 계획 (R0~R4) |
| **[NARRATIVE_ENGINE_DESIGN.md](world_history/NARRATIVE_ENGINE_DESIGN.md)** ⭐ | 역사·관계 → LLM 대화·선호·소망 *생성* (핵심 재미 검증 1순위) |
| **[QUEST_SYSTEM_DESIGN.md](npc/QUEST_SYSTEM_DESIGN.md)** ⭐ | 퀘스트 시스템 — NPC **소망(Wish)** 추적·완료·보상·기록 (소망 데이터 모델 정본). 관계·선물 루프는 interactionSystem §4 |
| [archive/PROCEDURAL_HISTORY_BACKBONE.md](_archive/PROCEDURAL_HISTORY_BACKBONE.md) | 깊은 과거 백본 (템플릿 4시대) — ⛔ 미고려/아카이브 |

### 🤝 동료 / 상호작용 (설계만)
| 문서 | 상태 |
|------|------|
| **[companionSystem.md](npc/companionSystem.md)** | 설계 완료, 미구현 |
| **[interactionSystem.md](npc/interactionSystem.md)** | 설계 완료, 미구현 — 30행동·관계 4축 + **관계·선물 몰입 루프**(§4: 대화·선호·유추 선물·보답) |
| **[NPC_ACTIVITY_DESIGN.md](npc/NPC_ACTIVITY_DESIGN.md)** ⭐ | 설계 초안 — NPC 자율 행동/원정 (상주 서비스 vs 몬스터 처치·던전 탐험 파티, 부상·죽음, 월드이벤트 트리거). Phase E 자율 의사결정 정본 |
| **[dungeonSystem.md](_archive/dungeonSystem.md)** | 설계 완료, ⏸ 오픈월드 운영으로 보류 |

### 🛠️ 기술 / 시스템
| 문서 | 내용 |
|------|------|
| **[ECS_PERFORMANCE_OPTIMIZATION_PLAN.md](tech/ECS_PERFORMANCE_OPTIMIZATION_PLAN.md)** | SparseSet 조회 비용 감소 단계별 계획 |
| **[resourceSystem.md](village/resourceSystem.md)** | 자원 시스템 개요 |

### 📂 아카이브
- **[archive/](archive)** — Phase A 초기 계획 / 체크리스트 등 (역사 참고용)

---

## ✅ 구현 완료 시스템 (핵심 12개 + 마을 Phase A~D)

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

### 마을 자가성장 (Phase A~D 완료)
| 시스템 | 비고 |
|--------|------|
| **자원 패시브 생산** | NPC당 시간 비례 Food/Wood/Stone, Stage별 배수 |
| **자원 Cap** | Woodpile +100, Stockpile +80 등 데이터 주도 |
| **범용 배치 큐** | ObjectPlacementTaskComponent + System_VillageBuildQueue |
| **Tier 승격** | Settlement→Hamlet→Village→Town (4h 인터벌) |
| **외곽 벽 인프라** | Palisade + Gate, Hamlet 진입 시 자동 계획 |
| **PlacedObject Registry** | ECS 승격, tableId/tile 양방향 인덱스 |
| **세트 판정** | ObjectSetCatalog (Forge/Inn/Birth 등) |
| **ProvidedService 비트마스크** | Shop/Forge/Inn/Shrine |
| **서비스 근접 집계** | System_VillageServiceProximity (0.3s) + F키 라우팅 |
| **상점 양방향 거래** | 매물 풀 + 매각 + 자원 50% 환원 |
| **필요도 스코어** | System_VillageNeedsEvaluation (2h) + 4계층 가중치 |
| **NPC 직무 할당** | System_VillageJobAssignment + JobBonusTable |
| **여관 고용** | Inn = Visitor 풀, 플레이어 능동 고용 (자연 이민 대체) |

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
- ⬜ 런타임 호감도 변동 = **활동 기반**(설계 [NPC_ACTIVITY §8.3](npc/NPC_ACTIVITY_DESIGN.md), 패시브 drift 폐기) / N:N / 이벤트

### NPC 일과 / 자율 의사결정 (35%)
- ✅ NpcStatComponent (8가지 성격), NpcJobComponent
- ⬜ 직업별 행동, 자율 의사결정, 일과 시뮬레이션 → **Phase E**

### UI 시스템 (50%)
- ✅ Inventory / Character / Shop / Forge / Inn / Shrine / MainMenu / HpBar / FloatingText
- ⬜ 미니맵 / 관계·선물 UI / 대화 UI / 파티 UI

### 절차적 역사 생성 (보류)
- POCO/다운로드 메뉴만 등록, 시스템 본체 미구현
- 대화·소망 시스템 이후 재개

---

## ❌ 미구현

| 우선순위 | 시스템 | 의존성 |
|---------|--------|--------|
| 🔥 **High** | NPC 일과 시뮬 (Phase E) | 이동, 길찾기 |
| 🔥 **High** | 출생 / 분가 / 이탈 (Phase E) | Population 확장 |
| 🔥 **High** | NPC 상호작용 (30가지 행동) | 관계 시스템 |
| 🔥 **High** | 관계·선물 루프 (대화·선호·선물·보답, interactionSystem §4) | NPC 시스템 |
| 🔥 **High** | 퀘스트 — NPC 소망 (떠오름·추적·보상) | 관계·선물 루프 |
| 📝 **Medium** | 대화 시스템 (LLM 통합 옵션) | NPC 시스템 |
| 📝 **Medium** | 이벤트 / 침공 (Phase F) | 위협도, 벽 파괴 |
| 📝 **Medium** | 동료 영입 / 명령 | 관계 시스템 |
| 📝 **Medium** | 크래프팅 (장비 제작 Phase F+) | 아이템 시스템 |
| 🎨 **Low** | 세이브/로드 보강 | 모든 시스템 |
| 🎨 **Low** | 사운드 / VFX 통합 | — |
| 🎨 **Low** | 카메라 개선 / 튜토리얼 / 로컬라이제이션 | — |
| 🎨 **Low** | 절차적 역사 본 구현 | 대화·소망 |

---

## 🗓️ Phase 로드맵 (마을 자가성장 트랙)

```
✅ Phase A: 자가 생성 최소 루프 (MVP)         완료 2026-04-24
✅ Phase B: 오브젝트 배치 핵심 (범용 큐)      완료 2026-04-24
✅ Phase C: Tier 승격 + Stage 1·2 + 외벽     완료 2026-04-26
✅ Phase D: 서비스 + 상점 + 필요도 + 직무     완료 2026-04-28
⬜ Phase E: 배후 시뮬레이션 + 출생/분가       다음
⬜ Phase F: 위협도 / 몬스터 침공 / 벽 파괴
⬜ Phase G: City Stage + StoneWall + WatchTower
```

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
  └─ Shop(양방향 거래) / Forge(강화) / Inn(고용) / Shrine
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
마을 시뮬     → VILLAGE_GROWTH_STAGES.md → PHASE_A~D_DESIGN.md
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

> **"플레이어가 직접 싸우는 액션 RPG에서, 마을은 시간이 흐르면 알아서 자라고, 플레이어는 그 마을의 상점·강화·여관을 통해 진짜 이득을 얻으며, NPC들은 자기 직업의 작업 오브젝트 옆에서 살아간다."**

**현재 위치**: 마을 자가성장 Phase A~D 완료, 전투/AI 핵심 로직 완성 (60%)
**다음 목표**: Phase E (배후 시뮬레이션 + 출생/분가/이탈) + NPC 관계 시스템

---

**문서 관리 원칙**:
- 매주 진행도 업데이트는 [implementationStatus.md](overview/implementationStatus.md)
- Phase 완료 시 본 문서의 Phase 로드맵 + 빠른 참조 업데이트
- 새 설계 문서 추가 시 본 문서의 "기획 문서" 섹션에 링크 등록

**마지막 업데이트**: 2026-05-20
