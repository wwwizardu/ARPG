---
name: current-status
description: 최근 구현 흐름과 재개 지점을 코드·정본 문서 기준으로 종합한 프로젝트 현황.
type: status
last_updated: 2026-10-03
sources:
  - Docs/npc/NPC_AMBITION_DESIGN.md
  - Docs/npc/NPC_COMBAT_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
  - Docs/npc/NPC_EQUIPMENT_DESIGN.md
  - Docs/npc/NPC_SIMULATION_DESIGN.md
  - Docs/map/DUNGEON_RUNTIME_DESIGN.md
  - Docs/map/DUNGEON_WORLD_SIM_DESIGN.md
  - Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md
  - Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md
  - Docs/village/VILLAGE_REWORK_PLAN.md
  - Docs/ai_monster/MONSTER_SPAWN_DESIGN.md
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/combat/SKILL_RUNE_DESIGN.md
  - Docs/combat/POE2_CURSOR_TRACKING_PLAN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/economy/AUCTION_HOUSE_DESIGN.md
  - Wiki/npc.md
  - Wiki/village.md
  - Wiki/combat.md
  - Wiki/skill_aim.md
  - Wiki/skill_propagation.md
  - Wiki/monster_spawn_runtime.md
  - Assets/Scripts/
  - Assets/_BinaryData/TableData/
---

# 현재 개발 현황 — 2026-10-03

## 1. 한눈에 보기

2026-09-01 이후 구현의 중심은 **NPC 모험가 경제와 실전투**, **절차 던전**, **연쇄 스킬**, **몬스터 공격 예고**다. 그 이전 기준선(고정 인구 초기 완성형 마을, 모든 신규 마을 Forge Tier 1 보장, 일반 장비 마법부여 MVP, 커서 조준 스킬)은 그대로 유지된다. [src: Docs/npc/NPC_AMBITION_DESIGN.md 상태, Docs/npc/NPC_COMBAT_DESIGN.md 상태, Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Wiki/village.md, Wiki/combat.md, Wiki/skill_aim.md]

**NPC**: 약 2,400명의 NPC가 Rest·Hunt·Trade·DungeonDelve를 솔로 또는 파티로 오프스크린 수행하고, 다음 활동은 성격에서 나온 야망과 필요로 효용 선택한다. 전리품을 상점에 팔아 Gold를 얻고(닫힌 경제), 상점의 기본 장비를 사서 강해진다. 소문 교환, 낮은 확률의 NPC 던전 완료, 파티 자동 구성, 부상 등급, NPC 간 절도가 구현됐고, 협력·배신(P3)은 보류다. 화면 안에서는 조건부 전투, 던전 전용 동행, 플레이어–NPC 결투가 동작한다. 상세는 [npc.md](npc.md). [src: Docs/npc/NPC_AMBITION_DESIGN.md §2·§13, Docs/npc/NPC_COMBAT_DESIGN.md, Wiki/npc.md]

**2026-10-03 (커밋 대기)**: NPC 장비를 무작위 옵션에서 **상점용 기본 아이템**(고정 옵션 1개)으로 바꿨다. 상점은 기존 재고 뒤에 기본 장비 칸을 따로 진열하고, NPC는 기본 장비만 산다. 기존 세이브는 WorldDataVersion 17 로드 때 NPC 장착 장비를 일괄 교체한다. NPC 평균 전투력이 25.96 → 10.75로 내려가 Hunt 기준 전투력을 10.75로 옮겼다. 함께 저장 표현을 줄여 전체 저장이 13.3MB → 6.3MB, 메인 스레드 복사가 599ms → 254~358ms가 됐다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md D116~D123·§11]

**던전**: 마을마다 2~3개 슬롯, 마을 접근 시 입구 배치, E 입장, 분리된 맵 컨텍스트, ESC 퇴장, 사망 복귀, 보스 완료 트랜잭션, `dungeons` 저장 파티션이 구현됐다. 던전 안에서는 월드 시스템(NPC 시뮬·시간)이 멈춘다 — 이를 바꾸는 월드 시뮬 계속 실행안은 초안이다. [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§4, Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태]

**몬스터·스킬**: 몬스터는 거리 링·슬롯·Warm 청크 캐시 기반으로 스폰되고, 공격 예고는 Phase 0~2가 구현됐다. 현행 `MonsterTable` 11종은 모두 `Biome=Any`라 biome별 종 분리는 아직 일어나지 않는다. Chain Lightning(Instant 4연쇄)이 구현됐고 Projectile·분열·OnHit 전파는 코드만 있고 PlayMode 검증 전이다. [src: Wiki/monster_spawn_runtime.md, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0]

## 2. 시스템별 상태

| 영역 | 구현된 기준선 | 남은 핵심 |
|------|---------------|-----------|
| **마을** | V1~V7 Profile·고정 인구·초기 Stage·누적 시설·Race별 배치/상권·결정론 상점, 신규 게임 Forge 160 Tier 1 보장, 벽 바깥 Gate 탐색, NPC 상점 거래(닫힌 경제)와 기본 장비 칸 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V1–V7·§8.4, Wiki/village.md] | 마을 골드 밸런스(매입 거절로 NPC 가방 포화, D109 ②), V7 PlayMode A~F 재실측, 런타임 성장 V8은 보류 [src: Docs/npc/NPC_AMBITION_DESIGN.md D109, Wiki/village.md §4] |
| **NPC 활동·경제** | Rest·Hunt·Trade·DungeonDelve(솔로·파티), 야망 효용 선택, 장비 전투력·개인 상한, 소문, NPC 던전 완료, 파티 자동 구성, 부상 등급, NPC 간 절도, 기본 장비 구매 [src: Docs/npc/NPC_AMBITION_DESIGN.md §2·§13, Wiki/npc.md §4] | P3 협력·배신(보류, §14 Q14~Q20), P5 양방향 배신·결투 명분/처단, P6 Forge·경매장 참여, 대화 사연 밸런스(자랑 토픽 60·90일 FAIL, D109 ①), 이주·WORLD_EVENT [src: Docs/npc/NPC_AMBITION_DESIGN.md §13·§14] |
| **NPC 실전투** | B1~B5: 진영·조건부 인식, 던전 전용 임시 동행(D97 최종), 플레이어–NPC 결투, 드랍 줍기(디버그 호출만), 전투 사망 연결 [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태] | 도주(D25)·NPC 간 결투 화면 장면(D24) 보류 [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태] |
| **NPC 장비** | 12슬롯·세트 5종(슬롯별 장착 확률)·검사 UI, 상점용 기본 아이템 198행(장비 66종 × 3), 시작 장비 = 기본 아이템, 전리품·장물 미장착 [src: Docs/npc/NPC_EQUIPMENT_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §3·§5] | 커밋, 시트 `BasicItem` 변형 다듬기(기획), NPC 장비 강화(P6a)와 기본 아이템의 관계 결정 [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §3.1·§11, Wiki/combat.md] |
| **던전** | 슬롯·입구 배치·입장/퇴장·사망 복귀·보스 완료·진행 차이 저장·동행 NPC·NPC 완료, 에디터 진단 [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§3.3·§5.3] | 함정, 생성 재시도, 입구 배치 실패율 진단, GeneratorVersion 마이그레이션, 사망 패널, Profiler 실측. 던전 중 월드 시뮬 계속 실행안 검토 [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/map/DUNGEON_WORLD_SIM_DESIGN.md] |
| **세이브** | 소유권별 파티션, NPC 상태 32샤드, manifest/previous 원자 커밋·검증, 레거시 자동 마이그레이션(WorldDataVersion 17). 기본 아이템 행 번호 저장, 계산 캐시·기본값 생략, 저장 복사기가 `DefaultValueHandling.Ignore`를 따름 [src: Docs/tech/SAVE_PARTITION_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3] | 장기 증가분(가방 전리품·소문) — 30일 시뮬 끝 NPC 데이터 약 11MB, 마을 골드 밸런스와 함께 [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3] |
| **NPC 대화** | 사연 토픽, 사건별 cache variant, 소문·자랑·절도 토픽, Deed·사망 기억 발생 시 NPC 단위 캐시 무효화 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5, Docs/npc/NPC_AMBITION_DESIGN.md D80~D83·D106] | 사연 2자리 경쟁(자랑 토픽이 밀림, D109 ①), Phase 2 판단 [src: Docs/npc/NPC_AMBITION_DESIGN.md D109] |
| **관찰 반응** | 전리품→소스 몬스터 식별 인터페이스 [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs] | 현행 `ItemTable`에 `SourceMonsterId>0` 아이템 0건. 감지·Trust·영속화·대사·보상 전체 [src: Assets/_BinaryData/TableData/ItemTable.bytes, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획] |
| **몬스터 스폰** | 거리 링·청크 슬롯·슬롯 쿨타임·Active/Warm/Expired 캐시, 마을 스폰 금지 영역(5타일), 던전 안 정지 [src: Wiki/monster_spawn_runtime.md, Assets/Scripts/Map/MapManager_Generator.cs `MarkVillageSpawnExclusion`, Assets/Scripts/Common/System/System_MonsterSpawn.cs `IWorldMapSystem`] | 실제 biome 분류 데이터(11종 모두 Any), 가중치, Zone×풀, 엘리트·보스 [src: Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1] |
| **몬스터 공격 예고** | Phase 0~2 구현(타이밍 해석·배치 렌더·셰이더), Phase 3 일부 [src: Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Assets/Scripts/Common/System/System_AttackTelegraph.cs] | Phase 4, 취소 페이드·경고음·LOD·최소 가독 시간 진단 [src: Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md] |
| **장비 옵션·마법부여** | 무기 Base/고유 강화 분리, 옵션 글로벌화, 미장착 장비 Prefix/Postfix 교체·추가 MVP, 마법부여한 기본 아이템은 일반 아이템으로 전환 [src: Wiki/combat.md] | Editor 진단 실행, Tier 1/2/3·저장 왕복·장착 후 실제 타격 PlayMode 검증, 분해·재롤·장착 장비 작업은 후속 [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12·§14] |
| **스킬 조준·이동 정책** | 커서 추적→효과 직전 커밋, 정책별 재커밋, 화살표 View, 감속·복구, Jump LaunchCommit [src: Wiki/skill_aim.md] | EditMode 테스트 추가·실행, 대표 스킬 PlayMode 실측 [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§10.1] |
| **스킬 전파** | 공용 연쇄·분열 런타임, Instant 4연쇄 Chain Lightning, 대기 주문, 결투 반영 적대 판정 [src: Wiki/skill_propagation.md, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0] | Projectile/분열/OnHit PlayMode 검증, EditMode 테스트 [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §9] |
| **스킬 페이지** | P-1 본체와 인스턴스 roll, 드랍·상점 일부 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] | Tier 2/3 콘텐츠, P-2 조건 처리, 장착 UI 폴리시 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] |
| **경매장** | 설계와 기본값 확정 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §디자인] | GO 신호 전, 전부 미구현 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획] |

## 3. 권장 재개 순서

1. **커밋 정리**: NPC 상점 기본 장비·저장 축소·관련 문서 변경이 미커밋이다. 측정용 `[SaveTiming]` 로그를 남길지 함께 정한다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]
2. ~~**데이터 정리**: `AiTable` 4번(SwampToad)이 SkillTable에서 빠진 SkillId 205를 참조~~ → 2026-10-03 205번 행 복구, 빌드 검증기 통과. [src: Docs/combat/SKILL_DESIGN.md §2, Assets/_BinaryData/TableData/SkillTable.bytes, Assets/Scripts/Editor/AttackTelegraphBuildValidator.cs]
3. **마을 골드 밸런스** (D109 ②): 매입 거절로 NPC 가방이 차는 문제, 가방 전리품에 따른 세이브 증가, 절도 빈도, 첫 며칠에 몰리는 NPC 구매를 함께 본다. [src: Docs/npc/NPC_AMBITION_DESIGN.md D109, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §7·§11.2]
4. **대화 사연 밸런스** (D109 ①): 자랑 토픽이 사연 자리에서 밀리는 문제. [src: Docs/npc/NPC_AMBITION_DESIGN.md D109]
5. **PlayMode 검증 묶음**: 던전 Profiler·입구 배치 실패율, 마법부여 Tier·저장 왕복, 마을 V7 A~F, 스킬 조준·전파 대표 스킬, 공격 예고 가독성. [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md §7, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12, Docs/village/VILLAGE_REWORK_PLAN.md §V7, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md]
6. **다음 설계 후보**: 던전 중 월드 시뮬(초안 검토 대기), NPC 장비 강화(P6a), 평판(Renown), P3 협력·배신 재개, 관찰 반응 데이터. 경매장은 착수 승인이 없다. [src: Docs/map/DUNGEON_WORLD_SIM_DESIGN.md, Docs/npc/NPC_AMBITION_DESIGN.md §13·§14, Docs/npc/PLAYER_RECOGNITION_DESIGN.md, Docs/economy/AUCTION_HOUSE_DESIGN.md]

## 4. 문서 신뢰도 주의

- `Docs/README.md`는 2026-10-03에 문서 목록과 상태 줄을 갱신했지만, "구현 완료 시스템·진행 중·미구현" 절의 비율과 상세는 2026-07-13 기준이다. `Docs/overview/implementationStatus.md`는 2026-04-08 기준이다. 현재 상태는 이 페이지와 각 주제 정본을, 구현 여부는 코드·테이블을 우선한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/_SCHEMA.md]
- `DUNGEON_RUNTIME_DESIGN.md`·`MONSTER_ATTACK_TELEGRAPH_DESIGN.md`·`NPC_EQUIPMENT_SET_PLAN.md`는 구현 뒤에도 "구현 전/검증 대기" 상태 줄이 남아 있다가 2026-10-03에 코드 대조로 고쳤다. 던전 문서는 구현된 [제안]을 [구현]으로 바꿨고(남은 [제안]은 미구현), 공격 예고·스킬·연쇄 계획 문서는 "(2026-10-03 확인: …)" 메모와 ✅/◐/❌ 진행 표시를 붙였다. [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §9, Docs/npc/NPC_EQUIPMENT_SET_PLAN.md 상태]
- NPC 결정 번호는 NPC_AMBITION·NPC_COMBAT·NPC_SHOP_EQUIPMENT 공용 연번이고(현재 D123까지), 정본 결정표는 `NPC_AMBITION_DESIGN.md` §2다. [src: Docs/npc/NPC_AMBITION_DESIGN.md §2]
- `VILLAGE_REWORK_PLAN.md`의 2026-08-30 Forge 메모는 공통 보장을 미구현으로 남겨 현재 코드 및 `ITEM_ENCHANTMENT_DESIGN.md` 상태와 충돌한다. 현행 판단에는 코드와 마법부여 정본을 쓴다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §후속 사용자 결정, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §상태·§5.1]
- `ITEM_ENCHANTMENT_DESIGN.md`는 구현 완료 후에도 active 원본으로 유지한다. §3–§5·§6.1–§6.2·§6.4·§7–§9·§15는 구현 계약, §12는 회귀 검증, §14는 후속 경계로 읽는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Wiki/combat.md]
