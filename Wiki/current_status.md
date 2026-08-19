# 현재 개발 현황 — 2026-08-19

---
name: current-status
description: 최근 구현 흐름과 재개 지점을 코드·정본 문서 기준으로 종합한 프로젝트 현황.
type: status
last_updated: 2026-08-19
sources:
  - Docs/village/VILLAGE_REWORK_PLAN.md
  - Docs/ai_monster/MONSTER_SPAWN_DESIGN.md
  - Docs/combat/SKILL_RUNE_DESIGN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/economy/AUCTION_HOUSE_DESIGN.md
  - Assets/Scripts/ 및 Assets/_BinaryData/TableData/ 관련 현행 코드·테이블
---

## 1. 한눈에 보기

현재 구현의 중심은 **고정 인구 기반 초기 완성형 마을**, **개별 NPC 활동·관계·대화**, **몬스터 콘텐츠 확장과 데이터 주도 스폰 기반**이다. 마을 V7은 정적 검증을 마쳤지만 실제 청크 materialize·상호작용·벽·상점 UI의 플레이 모드 실측이 남아 있고, 런타임 자동 건설·Stage 승격은 의도적으로 비활성화돼 있다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7–V8, Assets/Scripts/Village/VillageProgressionRules.cs]

최근 몬스터 작업은 FireImp·GreenSlime·PoisonMushroom·StoneGolem·Wolf의 테이블·애니메이션 연결과 `MonsterTable.Biome` 기반 경량 스폰 풀까지 도달했다. 다만 현행 `MonsterTable.bytes`의 9개 행은 모두 `Biome=Any`라 실제 biome별 종 분리는 아직 일어나지 않으며, 가중치·Zone 필터·Archetype 승격·보스 청크도 후속이다. [src: Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1]

다음 NPC 기능으로 준비된 것은 **관찰 반응(Reactive Recognition)** 이다. 전리품 식별 배관(`ItemTable.SourceMonsterId` + `MonsterTrophyIdentifier`)은 구현됐지만 현행 `ItemTable.bytes`에는 `SourceMonsterId>0`인 아이템이 없고, Talk 시 감지·Trust 상승·인지 단계 저장·`ClearThreat` 대사 게이트는 미구현이다. [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/_BinaryData/TableData/ItemTable.bytes, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]

## 2. 시스템별 상태

| 영역 | 구현된 기준선 | 남은 핵심 |
|------|---------------|-----------|
| **마을** | V1~V7: Profile, 고정 인구, 초기 Stage, 누적 시설, Dwarf 벽·제련소, Race별 배치·상권, 결정론 상점, 고정 상태 파리티 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V1–V7] | V7 플레이 모드 A~F 실측. 런타임 성장 V8은 별도 승인 후 검토 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7 체크리스트·§V8] |
| **NPC 활동·관계** | Rest·솔로/파티 Hunt, 오프스크린 LOD, 부상·사망·Deed, 플레이어 관계 4축과 저장 [src: Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Npc/NpcSaveData.cs] | 이주·WORLD_EVENT 연동, 관계·선물·소망 본 루프 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11, Docs/npc/interactionSystem.md §4, Docs/npc/QUEST_SYSTEM_DESIGN.md] |
| **NPC 대화** | 사연 토픽 4종, 사건별 cache variant, Deed·사망 기억 발생 시 NPC 단위 캐시 무효화 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs] | Phase 1 플레이 검증, 관계·감정·성격 토픽 Phase 2 판단 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1–2] |
| **관찰 반응** | 전리품→소스 몬스터 식별 인터페이스와 Biome 근거 데이터 [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/Scripts/Common/Tables.cs] | 실제 전리품 데이터, 감지·Trust·영속화·대사·보상 전체 [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획] |
| **몬스터 스폰** | `Any ∪ biome전용` 균등 풀을 그룹별·리스폰별 샘플 [src: Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs, Assets/Scripts/Manager/MonsterManager.cs] | 실제 biome 분류 데이터, 가중치, Zone×풀, 엘리트·보스 [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1] |
| **스킬 페이지** | P-1 본체와 인스턴스 roll 완료, 드랍·상점 일부 완료 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] | 현행 효과 4개가 모두 Tier 1이므로 Tier 2/3 콘텐츠 추가, P-2 조건 처리, 장착 UI 폴리시 [src: Assets/_BinaryData/TableData/SkillEffectTable.bytes, Docs/combat/SKILL_RUNE_DESIGN.md §10] |
| **경매장** | 월드 공유 물물교환 시장의 설계와 기본값 확정 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §디자인] | GO 신호 전이며 세이브·가치·시뮬·거래·건물·UI 전부 미구현 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획] |

## 3. 권장 재개 순서

1. V7 마을 A~F, NPC 대화 Phase 1, 신규 몬스터 Animation·Addressables·스폰을 Unity 플레이 모드에서 검증한다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7 플레이 모드 검증 체크리스트, Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1 검증 지표]
2. 새 몬스터의 `Biome`, AI, Drop과 실제 전리품 `SourceMonsterId` 데이터를 채워 관찰 반응의 입력을 완성한다. [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §제약·전제]
3. 관찰 반응 Track B의 감지→Trust→저장→대사 게이트→보상 순서를 구현한다. [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]
4. 플레이 근거에 따라 스킬 페이지 P-2/P-3, 마을 V8, 경매장 중 다음 세로 슬라이스를 선택한다. 경매장은 현재 설계만 있고 착수 승인은 없다. [src: Docs/combat/SKILL_RUNE_DESIGN.md §10, Docs/village/VILLAGE_REWORK_PLAN.md §V8, Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획]

## 4. 문서 신뢰도 주의

`Docs/README.md`는 2026-07-13 이후의 마을 V1~V7·NPC 대화·몬스터 작업을 충분히 반영하지 못하며, `Docs/overview/implementationStatus.md`는 2026-04-08 기준이라 현재 상태 대시보드로 쓰면 안 된다. 설계 의도는 각 주제의 최신 `Docs/` 정본을, 구현 여부는 코드·테이블을 우선한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/_SCHEMA.md]

