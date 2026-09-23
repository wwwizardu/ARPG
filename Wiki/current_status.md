---
name: current-status
description: 최근 구현 흐름과 재개 지점을 코드·정본 문서 기준으로 종합한 프로젝트 현황.
type: status
last_updated: 2026-09-01
sources:
  - Docs/village/VILLAGE_REWORK_PLAN.md
  - Docs/ai_monster/MONSTER_SPAWN_DESIGN.md
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/combat/SKILL_RUNE_DESIGN.md
  - Docs/combat/POE2_CURSOR_TRACKING_PLAN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/npc/NPC_SIMULATION_DESIGN.md
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/economy/AUCTION_HOUSE_DESIGN.md
  - Wiki/combat.md
  - Wiki/skill_aim.md
  - Wiki/skill_propagation.md
  - Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md
  - Wiki/village.md
  - Assets/Scripts/Item/ItemEnchantmentService.cs
  - Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs
  - Assets/Scripts/UI/UIForge.cs
  - Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes
  - Assets/Scripts/
  - Assets/_BinaryData/TableData/
---

# 현재 개발 현황 — 2026-09-01

## 1. 한눈에 보기

현재 구현의 중심은 **고정 인구 기반 초기 완성형 마을**, **일반 장비 마법부여 수직 슬라이스**, **개별 NPC 활동·관계·대화**, **몬스터 콘텐츠 확장과 데이터 주도 스폰 기반**이다. 신규 게임의 모든 마을은 종족·Profile·초기 Stage와 무관하게 Forge 160을 최소 Tier 1로 보장하며, 기존 Dwarf 성장 계획의 Tier 2/3은 보존한다. 런타임 자동 건설·Stage 승격은 의도적으로 비활성화돼 있다. [src: Docs/village/VILLAGE_REWORK_PLAN.md, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5.1, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs, Assets/Scripts/Village/VillageProgressionRules.cs]

최근 몬스터 작업은 FireImp·GreenSlime·PoisonMushroom·StoneGolem·Wolf의 테이블·애니메이션 연결과 `MonsterTable.Biome` 기반 경량 스폰 풀까지 도달했다. 다만 현행 `MonsterTable.bytes`의 9개 행은 모두 `Biome=Any`라 실제 biome별 종 분리는 아직 일어나지 않으며, 가중치·Zone 필터·Archetype 승격·보스 청크도 후속이다. [src: Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1]

장비 옵션 글로벌화 기준선 위에 미장착 인벤토리 장비의 Prefix/Postfix를 랜덤 교체·추가하는 Forge MVP 런타임 구현이 완료됐다. Tier 1 이상 Forge는 같은 기능·비용을 사용하며, 변화의 오브 1개+25 Gold 교체와 엑잘티드 오브 1개+40 Gold 추가를 서비스가 후보 검증·가중 Tier/수치 롤·재료/Gold 소비·전후 스냅샷·저장 요청까지 처리한다. 비용 시트 다운로드 등록은 운영 경로 보강이며, 진단 실행과 Tier별/저장 왕복/실제 전투 PlayMode 확인은 구현 완료 후 회귀 검증으로 남아 있다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Base/Inventory.cs, Assets/Scripts/UI/UIForge.cs, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes, Assets/Scripts/Editor/DownloadTables.cs, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs, Wiki/combat.md]

마을 런타임 쪽에서는 새 Forge 후조건 외에 손제작 마을을 매니저 준비가 끝난 `Initialize()`에서 실체화하고, 저장된 Forge Tier를 이미 배치된 Building/컴포넌트에 재동기화하며, 벽 바깥에서도 Gate를 찾도록 근접 탐색을 현재 실체화된 마을 풀로 분리했다. 이 경로들은 실제 로드·청크·상호작용 PlayMode 확인이 필요하다. [src: Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Common/System/System_VillageServiceProximity.cs, Assets/Scripts/Manager/MapManager.cs, Wiki/village.md]

플레이어 스킬은 커서 월드 좌표를 FixedUpdate 조준 상태로 추적하고, 투사체·범위·타격 직전의 정책별 커밋 방향을 효과가 소비한다. 플레이어 전용 방향 화살표 View와 사용 중 이동 감속·End 복구, Jump의 LaunchCommit 전환, 테이블 정책 필드와 순수 조준 Resolver/asmdef까지 구현됐다. 설계에 적힌 EditMode 테스트 파일은 현재 저장소에 없으며 대표 스킬 PlayMode 실측도 남아 있다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md, Assets/Scripts/Common/System/System_SkillAim.cs, Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs, Assets/Scripts/Skill/Aim/SkillAimResolver.cs, Assets/Scripts/Skill/Aim/ARPG.SkillAim.asmdef] [추론: `Assets/Tests/EditMode/`가 현재 저장소에 없음]

NPC 총량은 최신 밀도 설계에 따라 `max(맵 최소값, 마을 수×8)`이며 300개 마을 기준 2,400명이다. 이 규모에서 아이템 획득 때 전체 `WorldData`를 다시 저장하던 경로는 Player/World 파티션 저장으로 분리됐고, NPC 개별 상태는 32개 샤드와 manifest 원자 커밋을 사용한다. NPC 장비 compact 표현은 적용하지 않았으며, 레거시 마이그레이션과 플레이 모드 왕복·장애 복구 실측이 남아 있다. [src: Docs/npc/NPC_SIMULATION_DESIGN.md §D9, Docs/tech/SAVE_PARTITION_DESIGN.md §2–9, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/ItemManager.cs]

다음 NPC 기능으로 준비된 것은 **관찰 반응(Reactive Recognition)** 이다. 전리품 식별 배관(`ItemTable.SourceMonsterId` + `MonsterTrophyIdentifier`)은 구현됐지만 현행 `ItemTable.bytes`에는 `SourceMonsterId>0`인 아이템이 없고, Talk 시 감지·Trust 상승·인지 단계 저장·`ClearThreat` 대사 게이트는 미구현이다. [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/_BinaryData/TableData/ItemTable.bytes, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]

## 2. 시스템별 상태

| 영역 | 구현된 기준선 | 남은 핵심 |
|------|---------------|-----------|
| **마을** | V1~V7 Profile·고정 인구·초기 Stage·누적 시설·Race별 배치/상권·결정론 상점에 더해, 신규 게임 모든 마을 Forge 160 Tier 1 후조건과 벽 바깥 Gate 탐색·로드 실체화 보강 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V1–V7, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5.1, Assets/Scripts/Village/VillageInitialDevelopment.cs, Assets/Scripts/Common/System/System_VillageServiceProximity.cs, Assets/Scripts/Village/VillageManager.cs] | 공통 Forge·로드·Gate를 포함한 V7 PlayMode A~F 재실측. 구 세이브 Forge backfill은 범위 밖이며 런타임 성장 V8은 별도 승인 후 검토 [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5.1·§12, Docs/village/VILLAGE_REWORK_PLAN.md §V7 체크리스트·§V8] |
| **NPC 활동·관계** | Rest·솔로/파티 Hunt, 오프스크린 LOD, 부상·사망·Deed, 플레이어 관계 4축, 300개 마을 기준 목표 2,400명 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §D9·§11, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Npc/NpcSaveData.cs] | 이주·WORLD_EVENT 연동, 관계·선물·소망 본 루프 [src: Docs/npc/NPC_SIMULATION_DESIGN.md §11, Docs/npc/interactionSystem.md §4, Docs/npc/QUEST_SYSTEM_DESIGN.md] |
| **세이브** | 소유권별 파티션, NPC 상태 32샤드, manifest/previous 원자 커밋·검증, 레거시 자동 마이그레이션. 아이템·스킬 변경은 Player+World만 저장 [src: Docs/tech/SAVE_PARTITION_DESIGN.md §2–8, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/ItemManager.cs, Assets/Scripts/Manager/PlayerSkillManager.cs] | Unity 플레이 모드 왕복·손상 복구·Profiler 실측. NPC 장비 compact DTO는 필요 시 후속 [src: Docs/tech/SAVE_PARTITION_DESIGN.md §9] |
| **NPC 대화** | 사연 토픽 4종, 사건별 cache variant, Deed·사망 기억 발생 시 NPC 단위 캐시 무효화 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1, Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs] | Phase 1 플레이 검증, 관계·감정·성격 토픽 Phase 2 판단 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5 Phase 1–2] |
| **관찰 반응** | 전리품→소스 몬스터 식별 인터페이스와 Biome 근거 데이터 [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs, Assets/Scripts/Common/Tables.cs] | 실제 전리품 데이터, 감지·Trust·영속화·대사·보상 전체 [src: Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획] |
| **몬스터 스폰** | `Any ∪ biome전용` 균등 풀을 그룹별·리스폰별 샘플 [src: Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs, Assets/Scripts/Manager/MonsterManager.cs] | 실제 biome 분류 데이터, 가중치, Zone×풀, 엘리트·보스 [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1] |
| **장비 옵션·마법부여·공격 프로필** | 무기 Base/고유 강화 분리와 일반 옵션 글로벌화, 태그별 공격 프로필, 미장착 장비 Prefix/Postfix 랜덤 교체/추가, 테이블 비용, 메모리 트랜잭션, Forge 전후 비교 UI와 공통 옵션 문구까지 MVP 런타임 구현 완료 [src: Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Common/Utility/EquipHelper.cs, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs, Assets/Scripts/Item/EquipmentOptionText.cs] | 운영 보강: 비용 시트 다운로드 등록. 회귀 검증: Editor 진단, Tier 1/2/3·실패 무변경·저장 왕복·장착 후 프로필/실제 타격. 분해·전체/수치 재롤·장착 장비 작업·무기 고유 제련은 후속 범위 [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12·§14, Assets/Scripts/Editor/DownloadTables.cs] |
| **스킬 조준·이동 정책** | 커서 추적→효과 직전 커밋, 정책별 다단/채널 재커밋, 플레이어 화살표 View, Start/Process 감속과 End 복구, Jump LaunchCommit, A:AQ 파서·테이블 [src: Assets/Scripts/Common/System/System_SkillAim.cs, Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs, Assets/Scripts/Editor/DownloadTables.cs] | 설계된 EditMode 테스트 어셈블리/파일 추가와 실행, Direction·Position·Area·Projectile·MultiHit·Channeling·Jump 대표 스킬 PlayMode 실측 [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§10.1·§검증 체크리스트, Assets/Scripts/Skill/Aim/SkillAimResolver.cs] |
| **스킬 전파** (2026-09-23 추가) | 공용 연쇄·분열 런타임과 Instant 4연쇄 Chain Lightning, 대상에 붙는 대기 주문, `DeliveryType`별 전달 상한, 전파 Projectile 방문 대상 통과까지 구현하고 코드 리뷰 반영 [src: Wiki/skill_propagation.md, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §13] | `HopInterval=0.2` 연쇄감 확인, Projectile/분열/OnHit 데이터 행 추가와 PlayMode 검증, 리뷰 반영분 플레이 테스트 [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §3.4·§10.2] |
| **스킬 페이지** | P-1 본체와 인스턴스 roll 완료, 드랍·상점 일부 완료 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] | 현행 효과 4개가 모두 Tier 1이므로 Tier 2/3 콘텐츠 추가, P-2 조건 처리, 장착 UI 폴리시 [src: Assets/_BinaryData/TableData/SkillEffectTable.bytes, Docs/combat/SKILL_RUNE_DESIGN.md §10] |
| **경매장** | 월드 공유 물물교환 시장의 설계와 기본값 확정 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §디자인] | GO 신호 전이며 세이브·가치·시뮬·거래·건물·UI 전부 미구현 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획] |

## 3. 권장 재개 순서

1. Unity Editor에서 `ARPG/Diagnostics/Item Enchantment MVP`를 실행하고 비용·후보·Group·상한·실패 무변경·성공 소비·모든 신규 마을 Forge 보장을 확인한다. 이어서 Tier 1/2/3, Forge 닫기/재열기, 저장 왕복, 마법부여 장비 장착 후 Tooltip·스탯·공격 프로필·실제 타격을 PlayMode에서 검증한다. [src: Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12]
2. 마을 V7 A~F에 공통 Forge 배치, 손제작 마을 로드 실체화, 벽 바깥 Gate 열기, 배치된 Forge Tier 동기화를 포함해 실제 청크와 상호작용을 재검증한다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §V7 플레이 모드 검증 체크리스트, Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Common/System/System_VillageServiceProximity.cs]
3. 스킬 조준 EditMode 테스트 어셈블리/파일을 추가해 순수 Resolver를 검증하고 Direction·Position·Area·Projectile·MultiHit·Channeling·Jump 대표 PlayMode 실측을 이어간다. 파티션 세이브의 레거시 마이그레이션·재로드·previous 폴백도 함께 검증한다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§10.1·§검증 체크리스트, Assets/Scripts/Skill/Aim/SkillAimResolver.cs, Docs/tech/SAVE_PARTITION_DESIGN.md §9]
4. 새 몬스터의 `Biome`, AI, Drop과 실제 전리품 `SourceMonsterId` 데이터를 채워 관찰 반응 입력을 완성하고, Track B의 감지→Trust→저장→대사 게이트→보상을 구현한다. [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획]
5. 플레이 근거에 따라 스킬 페이지 P-2/P-3, 마을 V8, 경매장 중 다음 세로 슬라이스를 선택한다. 경매장은 현재 설계만 있고 착수 승인은 없다. [src: Docs/combat/SKILL_RUNE_DESIGN.md §10, Docs/village/VILLAGE_REWORK_PLAN.md §V8, Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획]

## 4. 문서 신뢰도 주의

`Docs/README.md`는 2026-07-13 이후의 마을 V1~V7·NPC 대화·몬스터 작업을 충분히 반영하지 못하며, `Docs/overview/implementationStatus.md`는 2026-04-08 기준이라 현재 상태 대시보드로 쓰면 안 된다. 설계 의도는 각 주제의 최신 `Docs/` 정본을, 구현 여부는 코드·테이블을 우선한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/_SCHEMA.md]

`VILLAGE_REWORK_PLAN.md`의 2026-08-30 Forge 메모는 공통 보장을 미구현으로 남겨 현재 코드 및 2026-08-31 `ITEM_ENCHANTMENT_DESIGN.md` 상태와 충돌한다. 현행 구현 판단에는 코드와 더 최신 마법부여 정본을 사용하고, 과거 메모는 구현 전 결정 이력으로만 취급한다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §후속 사용자 결정, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §상태·§5.1, Assets/Scripts/History/HistoryManager.cs]

`ITEM_ENCHANTMENT_DESIGN.md`는 구현 완료 후에도 active 원본으로 유지한다. §3–§5·§6.1–§6.2·§6.4·§7–§9·§15는 구현 계약, §12는 회귀 검증, §14는 후속 경계로 사용한다. §6.3의 시트 다운로드 문장은 운영 보강 항목이며, 구현 전 기준선인 §2.2·§2.4와 계획 중심의 §10–§11·§13은 현행 상태가 아니라 이력으로 읽는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs, Wiki/combat.md] [추론: 확정 규칙과 후속 설계 근거가 다른 원본으로 완전히 이관되기 전까지 보존]
