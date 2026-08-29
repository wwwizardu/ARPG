# 현재 개발 현황 — 2026-08-28

---
name: current-status
description: 최근 구현 흐름과 재개 지점을 코드·정본 문서 기준으로 종합한 프로젝트 현황.
type: status
last_updated: 2026-08-28
sources:
  - Docs/village/VILLAGE_REWORK_PLAN.md
  - Docs/ai_monster/MONSTER_SPAWN_DESIGN.md
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/SKILL_RUNE_DESIGN.md
  - Docs/combat/POE2_CURSOR_TRACKING_PLAN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/npc/NPC_SIMULATION_DESIGN.md
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/economy/AUCTION_HOUSE_DESIGN.md
  - Wiki/combat.md
  - Wiki/skill_aim.md
  - Assets/Scripts/ 및 Assets/_BinaryData/TableData/ 관련 현행 코드·테이블
---

## 1. 한눈에 보기

현재 구현의 중심은 **고정 인구 기반 초기 완성형 마을**, **개별 NPC 활동·관계·대화**, **몬스터 콘텐츠 확장과 데이터 주도 스폰 기반**이다. 마을 V7은 정적 검증을 마쳤지만 실제 청크 materialize·상호작용·벽·상점 UI의 플레이 모드 실측이 남아 있고, 런타임 자동 건설·Stage 승격은 의도적으로 비활성화돼 있다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7–V8, Assets/Scripts/Village/VillageProgressionRules.cs]

최근 몬스터 작업은 FireImp·GreenSlime·PoisonMushroom·StoneGolem·Wolf의 테이블·애니메이션 연결과 `MonsterTable.Biome` 기반 경량 스폰 풀까지 도달했다. 다만 현행 `MonsterTable.bytes`의 9개 행은 모두 `Biome=Any`라 실제 biome별 종 분리는 아직 일어나지 않으며, 가중치·Zone 필터·Archetype 승격·보스 청크도 후속이다. [src: Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1]

장비 옵션은 무기 로컬/타 장비 글로벌의 이중 경로를 폐기하고, 무기 Base·고유 강화만 무기 캐시에 두며 모든 Implicit/Prefix/Postfix를 글로벌 Modifier로 처리하는 기준선까지 구현됐다. 치명타는 소수 확률을 보존해 UI·예상 피해·실제 판정이 같은 값을 사용하고, 공격속도는 프로필 APS가 실제 스킬 시간을 구동한다. 실제 강화 재료·제련 트랜잭션과 전체 플레이 모드 검증은 후속이다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계–§갱신·저장·표시, Docs/combat/ModSystem_Changelog.md §2026-08-22 — 장비 옵션 글로벌화와 무기 고유 강화 분리, Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs, Assets/Scripts/Skill/Combat/DamageCalculator.cs, Wiki/combat.md]

플레이어 스킬은 커서 월드 좌표를 FixedUpdate 조준 상태로 추적하고, 투사체·범위·타격 직전의 정책별 커밋 방향을 효과가 소비한다. 플레이어 전용 방향 화살표 View와 사용 중 이동 감속·End 복구, Jump의 LaunchCommit 전환, 테이블 정책 필드와 EditMode 순수 로직 테스트까지 구현됐으며, 현재 열려 있는 Unity 에디터에서 대표 스킬 PlayMode 실측은 남아 있다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md, Assets/Scripts/Common/System/System_SkillAim.cs, Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs, Assets/Tests/EditMode/SkillAimResolverTests.cs, Wiki/skill_aim.md]

NPC 총량은 최신 밀도 설계에 따라 `max(맵 최소값, 마을 수×8)`이며 300개 마을 기준 2,400명이다. 이 규모에서 아이템 획득 때 전체 `WorldData`를 다시 저장하던 경로는 Player/World 파티션 저장으로 분리됐고, NPC 개별 상태는 32개 샤드와 manifest 원자 커밋을 사용한다. NPC 장비 compact 표현은 적용하지 않았으며, 레거시 마이그레이션과 플레이 모드 왕복·장애 복구 실측이 남아 있다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md D9, Docs/tech/SAVE_PARTITION_DESIGN.md §2–9, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/ItemManager.cs]

다음 NPC 기능으로 준비된 것은 **관찰 반응(Reactive Recognition)** 이다. 전리품 식별 배관(`ItemTable.SourceMonsterId` + `MonsterTrophyIdentifier`)은 구현됐지만 현행 `ItemTable.bytes`에는 `SourceMonsterId>0`인 아이템이 없고, Talk 시 감지·Trust 상승·인지 단계 저장·`ClearThreat` 대사 게이트는 미구현이다. [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/_BinaryData/TableData/ItemTable.bytes, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]

## 2. 시스템별 상태

| 영역 | 구현된 기준선 | 남은 핵심 |
|------|---------------|-----------|
| **마을** | V1~V7: Profile, 고정 인구, 초기 Stage, 누적 시설, Dwarf 벽·제련소, Race별 배치·상권, 결정론 상점, 고정 상태 파리티 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V1–V7] | V7 플레이 모드 A~F 실측. 런타임 성장 V8은 별도 승인 후 검토 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7 체크리스트·§V8] |
| **NPC 활동·관계** | Rest·솔로/파티 Hunt, 오프스크린 LOD, 부상·사망·Deed, 플레이어 관계 4축, 300개 마을 기준 목표 2,400명 [src: Docs/npc/NPC_SIMULATION_DESIGN.md D9·§11, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Npc/NpcSaveData.cs] | 이주·WORLD_EVENT 연동, 관계·선물·소망 본 루프 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11, Docs/npc/interactionSystem.md §4, Docs/npc/QUEST_SYSTEM_DESIGN.md] |
| **세이브** | 소유권별 파티션, NPC 상태 32샤드, manifest/previous 원자 커밋·검증, 레거시 자동 마이그레이션. 아이템·스킬 변경은 Player+World만 저장 [src: Docs/tech/SAVE_PARTITION_DESIGN.md §2–8, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/ItemManager.cs, Assets/Scripts/Manager/PlayerSkillManager.cs] | Unity 플레이 모드 왕복·손상 복구·Profiler 실측. NPC 장비 compact DTO는 필요 시 후속 [src: Docs/tech/SAVE_PARTITION_DESIGN.md §9] |
| **NPC 대화** | 사연 토픽 4종, 사건별 cache variant, Deed·사망 기억 발생 시 NPC 단위 캐시 무효화 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs] | Phase 1 플레이 검증, 관계·감정·성격 토픽 Phase 2 판단 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1–2] |
| **관찰 반응** | 전리품→소스 몬스터 식별 인터페이스와 Biome 근거 데이터 [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/Scripts/Common/Tables.cs] | 실제 전리품 데이터, 감지·Trust·영속화·대사·보상 전체 [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획] |
| **몬스터 스폰** | `Any ∪ biome전용` 균등 풀을 그룹별·리스폰별 샘플 [src: Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs, Assets/Scripts/Manager/MonsterManager.cs] | 실제 biome 분류 데이터, 가중치, Zone×풀, 엘리트·보스 [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1] |
| **장비 옵션·공격 프로필** | 무기 Base/고유 강화 분리, 일반 옵션 글로벌화, 태그별 추가 피해, 합산 Increased, 소수 치명타와 프로필 기반 실제 공속 [src: Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Common/Utility/EquipHelper.cs, Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs] | 강화 재료·롤·소비·제련 UI, Spell 공격속도 행 숨김, 전체 플레이 모드 실측 [src: Docs/combat/ModSystem_Changelog.md §무기 고유 강화 준비 상태·§UI와 검증 상태, Assets/Scripts/UI/CharacterUI.cs] |
| **스킬 조준·이동 정책** | 커서 추적→효과 직전 커밋, 정책별 다단/채널 재커밋, 플레이어 화살표 View, Start/Process 감속과 End 복구, Jump LaunchCommit, A:AQ 파서·테이블 [src: Assets/Scripts/Common/System/System_SkillAim.cs, Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs, Assets/Scripts/Editor/DownloadTables.cs] | Unity Test Runner EditMode 실행과 Direction·Position·Area·Projectile·MultiHit·Channeling·Jump 대표 스킬 PlayMode 실측 [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§검증 체크리스트, Assets/Tests/EditMode/SkillAimResolverTests.cs] |
| **스킬 페이지** | P-1 본체와 인스턴스 roll 완료, 드랍·상점 일부 완료 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] | 현행 효과 4개가 모두 Tier 1이므로 Tier 2/3 콘텐츠 추가, P-2 조건 처리, 장착 UI 폴리시 [src: Assets/_BinaryData/TableData/SkillEffectTable.bytes, Docs/combat/SKILL_RUNE_DESIGN.md §10] |
| **경매장** | 월드 공유 물물교환 시장의 설계와 기본값 확정 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §디자인] | GO 신호 전이며 세이브·가치·시뮬·거래·건물·UI 전부 미구현 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획] |

## 3. 권장 재개 순서

1. 스킬 조준의 좁은 EditMode 테스트를 Unity Test Runner에서 실행하고, Direction·Position·Area·Projectile·MultiHit·Channeling·Jump 대표 스킬을 PlayMode에서 커서 이동·프레임레이트·감속·취소·사망·Reset 관점으로 실측한다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§검증 체크리스트, Assets/Tests/EditMode/SkillAimResolverTests.cs]
2. 파티션 세이브의 레거시 마이그레이션·재로드, 좁은 저장 범위, active 손상 시 previous 폴백을 Unity 플레이 모드와 Profiler에서 검증한다. 이어서 V7 마을 A~F, NPC 대화 Phase 1, 신규 몬스터 Animation·Addressables·스폰과 장비 옵션 글로벌화의 장착/해제·실제 타격·세이브 왕복을 검증한다. [src: Docs/tech/SAVE_PARTITION_DESIGN.md §9, Docs/village/VILLAGE_REWORK_PLAN.md §V7 플레이 모드 검증 체크리스트, Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1 검증 지표, Docs/combat/ModSystem_Changelog.md §UI와 검증 상태]
3. 새 몬스터의 `Biome`, AI, Drop과 실제 전리품 `SourceMonsterId` 데이터를 채워 관찰 반응의 입력을 완성한다. [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §제약·전제]
4. 관찰 반응 Track B의 감지→Trust→저장→대사 게이트→보상 순서를 구현한다. [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]
5. 플레이 근거에 따라 스킬 페이지 P-2/P-3, 마을 V8, 경매장 중 다음 세로 슬라이스를 선택한다. 경매장은 현재 설계만 있고 착수 승인은 없다. [src: Docs/combat/SKILL_RUNE_DESIGN.md §10, Docs/village/VILLAGE_REWORK_PLAN.md §V8, Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획]

## 4. 문서 신뢰도 주의

`Docs/README.md`는 2026-07-13 이후의 마을 V1~V7·NPC 대화·몬스터 작업을 충분히 반영하지 못하며, `Docs/overview/implementationStatus.md`는 2026-04-08 기준이라 현재 상태 대시보드로 쓰면 안 된다. 설계 의도는 각 주제의 최신 `Docs/` 정본을, 구현 여부는 코드·테이블을 우선한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/_SCHEMA.md]
