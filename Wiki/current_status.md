---
name: current-status
description: 최근 구현 흐름과 재개 지점을 코드·정본 문서 기준으로 종합한 프로젝트 현황.
type: status
last_updated: 2026-10-24
sources:
  - Docs/npc/NPC_OBJECTIVE_DESIGN.md
  - Docs/combat/SKILL_DESIGN.md
  - Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md
  - Docs/npc/NPC_AMBITION_DESIGN.md
  - Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md
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
  - Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md
  - Docs/combat/ITEM_DROP_BALANCE_DESIGN.md
  - Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md
  - Docs/ai_monster/MONSTER_RARITY_DESIGN.md
  - Docs/combat/SKILL_RUNE_DESIGN.md
  - Docs/combat/POE2_CURSOR_TRACKING_PLAN.md
  - Docs/npc/PLAYER_RECOGNITION_DESIGN.md
  - Docs/tech/SAVE_PARTITION_DESIGN.md
  - Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md
  - Docs/economy/AUCTION_HOUSE_DESIGN.md
  - Docs/economy/GOLD_BALANCE_DESIGN.md
  - Wiki/npc.md
  - Wiki/village.md
  - Wiki/combat.md
  - Wiki/skill_aim.md
  - Wiki/skill_propagation.md
  - Wiki/monster_spawn_runtime.md
  - AGENTS.md
  - Assets/Scripts/
  - Assets/_BinaryData/TableData/
---

# 현재 개발 현황 — 2026-10-24

## 1. 한눈에 보기

2026-09-01 이후 구현의 중심은 **NPC 모험가 경제와 실전투**, **절차 던전**, **연쇄 스킬**, **몬스터 공격 예고**다. 그 이전 기준선(고정 인구 초기 완성형 마을, 모든 신규 마을 Forge Tier 1 보장, 일반 장비 마법부여 MVP, 커서 조준 스킬)은 그대로 유지된다. [src: Docs/npc/NPC_AMBITION_DESIGN.md 상태, Docs/npc/NPC_COMBAT_DESIGN.md 상태, Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Wiki/village.md, Wiki/combat.md, Wiki/skill_aim.md]

**NPC**: 약 2,400명의 NPC가 Rest·Hunt·Trade·DungeonDelve를 솔로 또는 파티로 오프스크린 수행하고, 다음 활동은 성격에서 나온 야망과 필요로 효용 선택한다. 전리품을 상점에 팔아 Gold를 얻고(닫힌 경제), 상점의 기본 장비를 사서 강해진다. 소문 교환, 낮은 확률의 NPC 던전 완료, 파티 자동 구성, 부상 등급, NPC 간 절도가 구현됐고, 협력·배신(P3)은 보류다. 화면 안에서는 조건부 전투, 던전 전용 동행, 플레이어–NPC 결투가 동작한다. 상세는 [npc.md](npc.md). [src: Docs/npc/NPC_AMBITION_DESIGN.md §2·§13, Docs/npc/NPC_COMBAT_DESIGN.md, Wiki/npc.md]

**2026-10-03 (커밋 `e3ed610`)**: NPC 장비를 무작위 옵션에서 **상점용 기본 아이템**(고정 옵션 1개)으로 바꿨다. 상점은 기존 재고 뒤에 기본 장비 칸을 따로 진열하고, NPC는 기본 장비만 산다. 기존 세이브는 WorldDataVersion 17 로드 때 NPC 장착 장비를 일괄 교체한다. NPC 평균 전투력이 25.96 → 10.75로 내려가 Hunt 기준 전투력을 10.75로 옮겼다. 함께 저장 표현을 줄여 전체 저장이 13.3MB → 6.3MB, 메인 스레드 복사가 599ms → 254~358ms가 됐다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md D116~D123·§11]

**2026-10-10 (커밋 대기)**: NPC 행적·반응 새 구조 1·2단계를 구현했다. 던전 세대의 40%에 소문이 붙고, 그 마을 주민 1명이 소문 주인이 되어 플레이어에게 부탁한다. 보스 처치로 해결하고 부탁한 NPC에게 보고하면 반응과 의뢰 보답을 받는다. 그 뒤 `PlayerDeed` 소문이 같은 마을에 퍼지고, 들은 NPC가 관련도만큼 한 번 반응한다. 옛 던전 시나리오 코드와 번들은 삭제했고 구 형식은 호환하지 않는다. 진행 원장은 새 `narrative` 저장 파트(WorldDataVersion 21)다. 같은 날 3단계 소망 ItemGift도 구현했다. 호감 30 이상인 NPC가 소망을 털어놓고, 그 아이템(첫 콘텐츠는 구리 목걸이)을 선물로 건네면 소모·감사·의뢰 보답·행적 소문이 한 번에 저장된다. 반응 표에는 대상(All·Owner·Others) 열이 생겼다. LLM 소망 스파이크는 지웠고 저장 버전은 21 그대로다. 5단계로 호기심 반응을 고른 NPC가 아는 다른 던전 소문을 소개하고, 그 소문은 소개한 NPC의 부탁 칸에 들어간다(보고·보답도 그 NPC). 4단계로 최근 부상 화제의 기본 문구도 시트(3001)에서 읽는다("지난 던전 원정에서 호되게 당했소. 다음엔 더 조심해야겠소."). 저장소·생성 진단은 모두 PASS이고 실제 플레이 확인은 남았다. 상세는 [npc.md](npc.md). [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md 머리말·§7.7·§8.1·§8.2·§8.3]

**2026-10-11 (커밋 대기)**: 장비 일반 옵션을 T3에서 **T12**까지 늘리고 장비에 **아이템 레벨**을 저장했다. 몬스터 드랍은 몬스터 레벨, NPC 전리품·상점 랜덤 매물은 지역 레벨이 아이템 레벨이다. 아이템 레벨로 T1~T10 중 5단계 구간이 정해지고(1–10은 T1~T3, 90–99는 T6~T10) 구간 안에서도 레벨이 높을수록 높은 티어가 잘 나온다. T11·T12는 아이템 레벨 90 이상 몬스터 드랍에서만 등급별 확률로 붙는다. 세이브 버전은 그대로다. 컴파일·테이블 검증·진단·플레이 모드 생성 확인은 통과했고 실제 플레이 확인은 남았다. 마법부여와 던전 드랍 레벨은 사용자가 따로 작업한다. 상세는 [combat.md](combat.md). [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §1–§4·§7·§8, Wiki/combat.md]

**2026-10-12 (커밋 대기)**: 명중 시 **넉백·당기기**를 구현했다. 판정 범위의 중심(직접 공격은 공격자, 범위 공격은 범위 중심, 발사체는 날아온 방향)을 기준으로 짧게 밀거나 중심 쪽으로 당긴다. 효과는 SkillEffect 행으로 정의해 스킬·장판에 붙이며, 이날은 어떤 콘텐츠에도 연결하지 않았다(2026-10-13 멧돼지 돌진이 첫 사용). PlayMode 측정은 통과했고 손 조작 확인은 콘텐츠에 붙일 때 한다. 상세는 [combat.md](combat.md). [src: Docs/combat/SKILL_DESIGN.md §6.10, Wiki/combat.md]

**2026-10-13 (커밋 대기)**: 인벤토리 **장비 비교 툴팁**을 구현했다. 인벤토리 장비 툴팁 아래에 `[Shift] 비교` 안내가 붙고, Shift를 누르고 있는 동안 현재 장착 아이템(칸이 둘이면 두 칸 모두)과 수치·옵션 차이를 카드로 나란히 보여준다. 비교 중 좌클릭은 화면 왼쪽 칸, 우클릭은 화면 오른쪽 칸에 바로 장착하고, Shift 없는 클릭은 기존 자동 선택 그대로다. PlayMode 확인 일부와 묶음 배치 검토가 남았다. 상세는 [combat.md](combat.md). [src: Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §이용 흐름과 조작, Wiki/combat.md]

**2026-10-13 (커밋 대기)**: 같은 날 **멧돼지 돌진**을 만들며 땅 위 직선 돌진(`SkillType.Dash`)과 넉백 저항 칸을 추가했다. 멧돼지는 0.6초 동안 띠로 예고하며 초당 45°까지만 방향을 돌리고, 출발 순간 방향을 고정해 0.5초에 6칸을 달린다. 지나가는 길의 적을 한 번씩 맞혀 옆으로 2칸 밀고, 벽에 닿으면 바로 멈춘다. 돌진 중에는 맞지만 밀리지 않고, 준비 중에 밀어내면 띠째 빗나간다. 함께 **몬스터도 표의 조준 방식으로 대상을 따라 돈다**(평타 90°/초, 원거리·위치 180°/초). AI 1번 점프는 몬스터용(6번)으로 바꿨다. PlayMode 측정은 통과했고 손 조작 체감 확인이 남았다. 상세는 [combat.md](combat.md)·[skill_aim.md](skill_aim.md). [src: Docs/combat/SKILL_DESIGN.md §4 M4·§6.11, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15, Wiki/combat.md, Wiki/skill_aim.md]

**2026-10-18 (커밋 대기)**: NPC **단기 목표** 1단계를 구현했다. NPC가 목표 1개(소문 확인·장비 마련·원수 갚기·설욕)를 가지고, 목표가 활동 확률·원정 대상·파티·대화·검사 창 줄을 기울인다. 옛 확인 원정은 소문 확인 목표로 옮겼고, 원수 갚기는 플레이어에게 부탁한다. 진단(30·60일, 결정론 불일치 0)은 통과했고 실제 플레이 확인이 남았다. 상세는 [npc.md](npc.md). [src: Docs/npc/NPC_OBJECTIVE_DESIGN.md 머리말·§12.1, Wiki/npc.md]

**2026-10-19 (커밋 대기)**: **던전 중 월드 시뮬**을 구현했다. 던전 안에서도 게임 시간·NPC 활동·마을 생산·정착지 시뮬이 필드에서 멀리 떠나 있을 때와 같게 돈다(`IWorldSimSystem` 5개). 월드 좌표가 필요한 청크 로더·몬스터 스폰·입구 배치 등 10개는 지금처럼 멈춘다. 입장할 때 근처 연대기 마을을 접고 실체화된 네임드 엔티티를 내린다. 네임드 감독은 던전 안에서도 단계·사라짐을 진행하고 새 출현만 퇴장 뒤로 미룬다. NPC 던전 완료는 플레이어가 들어가 본 던전만 막는다. 던전 안 소식창은 필드와 같고 마을 자원 알림만 끈다. 진단(Run 30·60, 닫힌 경제 오차 0)과 던전 입장·퇴장·완료·사망·재접속 플레이 검증을 통과했고, 사용자 실플레이 확인이 남았다. [src: Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태·§2·§3·§5·§11.1]

**2026-10-20 (커밋 대기)**: 로드할 때마다 BaseTown 같은 실체화 마을이 게임 시작부터의 시간을 다시 생산하던 기존 문제를 고쳤다. 마을 생산 시스템이 저장된 시각을 불러오기 전에 기준 시각을 읽어 0부터 시작했기 때문이다. [src: Docs/economy/GOLD_BALANCE_DESIGN.md §11.2, Assets/Scripts/Common/System/System_VillagePassiveProduction.cs]

**2026-10-21 (커밋 대기)**: **아이템 드롭·제작 밸런스**를 구현했다. 장비에 일반·마법·희귀 등급(붙은 접두·접미 수로 판정)과 품질(0~20, 일반 장비만 다시 굴리기)이 생겼다. 플레이어 드롭은 몬스터 레벨 구간표(`DropBand`)로 등급 확률·옵션 수·품질·수량이 오르고, 장비는 부위 몫 → 현재 구간 80%로 고른다. 아이템 수량은 판정 횟수를 곱하고 희귀도는 플레이어·몬스터·장소 묶음을 곱하는 PoE2 방식이다(새 스탯 `ItemQuantity`·`ItemRarity`). 마법부여 티어는 아이템 레벨을 따르고, 진화의 오브·품질 다시 굴리기를 추가했으며 엑잘티드는 일반 장비에 쓸 수 없다. 던전은 입구 지역 레벨을 따르고 보스 사망 드롭·상자 드롭·던전 수량 +70%가 붙었다. 플레이어 판매가에 등급·레벨 배율을 곱한다. NPC 전리품·상점 매물은 옛 규칙 그대로다(오브 종류만 새 가중치). 세이브 버전은 그대로이고 이전 장비는 품질 0이다. 진단 1~5 통과(NPC 결정론은 구현 전 코드 사본 대조), 플레이 확인 남음. 상세는 [combat.md](combat.md). [src: Docs/combat/ITEM_DROP_BALANCE_DESIGN.md 상태·§2·§16, Wiki/combat.md]

**2026-10-24 (커밋 대기)**: **플레이어 레벨·능력치·적중 판정·참 판**을 구현했다. 플레이어는 몬스터 처치(근처 20칸)로 레벨 1~100을 올리고, 레벨업마다 받는 5포인트를 힘(HP)·민첩(회피)·지능(마나)에 나눈다(19레벨까지 무료로 되돌리기). 모든 타격은 정확도 대 회피로 빗나갈 수 있고(하한 25%), 큰 한 방과 장판 6개 스킬은 회피 불가 태그로 항상 맞는다. 몬스터 정확도·회피는 레벨 곡선 × 종 배율, NPC 정확도는 고향 마을 지역 레벨 기준이다. 10레벨마다 열리는 3×3 판 자리에 3×3 참 판을 끼우고 모양 6종 참을 박아 옵션을 얻으며, 판·참은 몬스터·던전·NPC 전리품·상점 참 칸에서 나온다. 기존 세이브 플레이어는 1레벨에서 시작한다. 적중률·세이브 왕복·드롭 분포 진단은 통과했고, NPC 시뮬레이션 30일은 개인 상한 초과 39명(전투력 식 변경, 사용자 결정으로 그대로 둠) 외 통과다. 플레이 확인이 남았다. 상세는 [combat.md](combat.md). [src: Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md 머리말·§3–§8, Wiki/combat.md]

**던전**: 마을마다 2~3개 슬롯, 마을 접근 시 입구 배치, E 입장, 분리된 맵 컨텍스트, ESC 퇴장, 사망 복귀, 보스 완료 트랜잭션, `dungeons` 저장 파티션이 구현됐다. 던전 안에서도 게임 시간·NPC·마을 시뮬·네임드 감독(새 출현 제외)이 계속 돌고, 월드 좌표가 필요한 시스템만 멈춘다(2026-10-19 구현, 사용자 실플레이 확인 전). [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§4, Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태·§2] 2026-10-21부터 새로 만드는 던전은 입구 청크 지역 레벨 +2(보스 +4)이고, 보스 사망 드롭·상자 드롭·던전 안 수량 +70%가 있다. 완료 트랜잭션은 그대로다. [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md §5.4, Docs/combat/ITEM_DROP_BALANCE_DESIGN.md §7.6]

**몬스터·스킬**: 몬스터는 거리 링·슬롯·Warm 청크 캐시 기반으로 스폰되고, 공격 예고는 Phase 0~2가 구현됐다. [src: Wiki/monster_spawn_runtime.md, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태] 종은 청크 biome마다 `공용(Biome=Any) ∪ 그 biome 전용` 풀에서 그룹마다 균등하게 고른다(2026-07-24 구현). 어떤 종이 공용이고 어떤 종이 전용인지는 `MonsterTable`의 Biome 칸이 정본이다. [src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §7.2.1, Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs, Assets/Scripts/Data/DataManager_Table.cs, Assets/_BinaryData/TableData/MonsterTable.bytes] Chain Lightning(Instant 4연쇄)이 구현됐고 Projectile·분열·OnHit 전파는 코드만 있고 PlayMode 검증 전이다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0]

## 2. 시스템별 상태

| 영역 | 구현된 기준선 | 남은 핵심 |
|------|---------------|-----------|
| **마을** | V1~V7 Profile·고정 인구·초기 Stage·누적 시설·Race별 배치/상권·결정론 상점, 신규 게임 Forge 160 Tier 1·BaseTown 상점 151 최소 Tier 1 보장, 벽 바깥 Gate 탐색, NPC 상점 거래(닫힌 경제)와 기본 장비 칸·골드 밸런스 [src: Docs/village/VILLAGE_REWORK_PLAN.md §V1–V7·§8.4·§시작 마을 기본 상점, Docs/economy/GOLD_BALANCE_DESIGN.md §11.2·§11.3, Wiki/village.md] | V7 PlayMode A~F와 BaseTown 상점 배치·상호작용 실측, 런타임 성장 V8은 보류 [src: Wiki/village.md §4] |
| **NPC 활동·경제** | Rest·Hunt·Trade·DungeonDelve(솔로·파티), 야망 효용 선택, 장비 전투력·개인 상한, 소문, NPC 던전 완료, 파티 자동 구성, 부상 등급, NPC 간 절도, 기본 장비 구매 [src: Docs/npc/NPC_AMBITION_DESIGN.md §2·§13, Wiki/npc.md §4] | P3 협력·배신(보류, §14 Q14~Q20), P5 양방향 배신·결투 명분/처단, P6 Forge·경매장 참여, 대화 사연 밸런스(자랑 토픽 60·90일 FAIL, D109 ①), 이주·WORLD_EVENT [src: Docs/npc/NPC_AMBITION_DESIGN.md §13·§14] |
| **NPC 실전투** | B1~B5: 진영·조건부 인식, 던전 전용 임시 동행(D97 최종), 플레이어–NPC 결투, 드랍 줍기(디버그 호출만), 전투 사망 연결 [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태] | 도주(D25)·NPC 간 결투 화면 장면(D24) 보류 [src: Docs/npc/NPC_COMBAT_DESIGN.md 상태] |
| **NPC 장비** | 12슬롯·세트 5종(슬롯별 장착 확률)·검사 UI, 상점용 기본 아이템 198행(장비 66종 × 3), 시작 장비 = 기본 아이템, 전리품·장물 미장착 [src: Docs/npc/NPC_EQUIPMENT_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §3·§5] | 시트 `BasicItem` 변형 다듬기(기획), NPC 장비 강화(P6a)와 기본 아이템의 관계 결정 [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §3.1·§11, Wiki/combat.md] |
| **던전** | 슬롯·입구 배치·입장/퇴장·사망 복귀·보스 완료·진행 차이 저장·동행 NPC·NPC 완료, 에디터 진단 [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§3.3·§5.3]. 상자·입구는 엔티티다(2026-10-09 구현, 사용자 플레이 확인). 매니저 전용 마커를 없앴고, 입구 소문 징후는 자막 전용이다. 소리·흔적 연출은 2026-10-09에 없앴다. 벽·성문은 원칙상 맵 타일로 둔다 [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태·§4.1, AGENTS.md ECS Rules]. 던전 세대의 40%에 소문 콘텐츠가 붙는다(소문 주인·부탁은 [npc.md](npc.md)). 자막 문구는 그 콘텐츠의 `SignText`다. 플레이어가 입구 8칸 안에 들어오면 `System_DungeonEntranceSign`이 입구 엔티티의 자식 표시를 켠다. 지정 몬스터(늑대 등)를 잡아도 꺼지지 않고, 보스 처치로 소문이 해결될 때까지 뜬다(2026-10-10) [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md §2.1·§3.1·§8.1, Assets/Scripts/Common/System/System_DungeonEntranceSign.cs, Assets/Scripts/Dungeon/DungeonManager_ScenarioPresentation.cs, Assets/Scripts/Manager/NpcManager_Narrative.cs] | 함정, 생성 재시도, 입구 배치 실패율 진단, GeneratorVersion 마이그레이션, 사망 패널, 전투 중 Profiler 실측. 던전 중 월드 시뮬은 2026-10-19 구현, 사용자 실플레이 확인 남음 [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태·§11.1] |
| **세이브** | 소유권별 파티션, NPC 상태 32샤드, manifest/previous 원자 커밋·검증, 레거시 자동 마이그레이션(현재 WorldDataVersion 21). 기본 아이템 행 번호 저장, 계산 캐시·기본값 생략, 저장 복사기가 `DefaultValueHandling.Ignore`를 따름 [src: Docs/tech/SAVE_PARTITION_DESIGN.md, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3, Assets/Scripts/Manager/DataManager.cs]. 2026-10-10 `narrative` 파트 추가: 21 미만 세이브는 빈 원장으로 열고, 21 이상에서 파트가 없거나 손상되면 로드 실패, 구 시나리오 필드는 무시한다 [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md §7.7·§8.1] | 장기 증가분(가방 전리품·소문) — 30일 시뮬 끝 NPC 데이터 약 11MB, 마을 골드 밸런스와 함께 [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11.3] |
| **NPC 대화** | 사연 토픽, 사건별 cache variant, 소문·자랑·절도 토픽, Deed·사망 기억 발생 시 NPC 단위 캐시 무효화 [src: Docs/world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md §5, Docs/npc/NPC_AMBITION_DESIGN.md D80~D83·D106]. 던전 소문 부탁·진행 확인 화제(대화 후보에 고정), 보고·행적 반응 대화. 검사 창의 "근처 소식"·"조사 결과 이야기" 버튼을 없애고 "대화"로 통합(2026-10-10). 소망 털어놓기·떠올리기 화제(고정), 선물 목록의 "(소망)" 표시와 소망 전달 대화, 검사 창 소망 한 줄, 최근 부상 화제 문구를 시트 3001에서 읽음, 보고·소문 반응 대사의 후속 소개(소개한 NPC가 부탁)(2026-10-10) [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md 머리말·§8.1·§8.2·§8.3, Assets/Scripts/UI/UINpcInspect.cs, Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs] | 사연 2자리 경쟁(자랑 토픽이 밀림, D109 ①), Phase 2 판단 [src: Docs/npc/NPC_AMBITION_DESIGN.md D109]. 부탁·보고·소망·부상 문구·후속 소개 실제 플레이 확인, 선물 선호 판정 [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md §8·§8.1·§8.2·§8.3] |
| **관찰 반응** | 전리품→소스 몬스터 식별 인터페이스 [src: Assets/Scripts/Common/Utility/MonsterTrophyIdentifier.cs] | 현행 `ItemTable`에 `SourceMonsterId>0` 아이템 0건. 감지·Trust·영속화·대사·보상 전체 [src: Assets/_BinaryData/TableData/ItemTable.bytes, Docs/npc/PLAYER_RECOGNITION_DESIGN.md §구현 계획] |
| **몬스터 스폰** | 거리 링·청크 슬롯·슬롯 쿨타임·Active/Warm/Expired 캐시, 마을 스폰 금지 영역(5타일), 던전 안 정지 [src: Wiki/monster_spawn_runtime.md, Assets/Scripts/Map/MapManager_Generator.cs `MarkVillageSpawnExclusion`, Assets/Scripts/Common/System/System_MonsterSpawn.cs `IWorldMapSystem`] | biome 전용 종 확대(공용 종은 모든 biome에 섞임), 가중치(지금은 균등), Zone×풀, 엘리트·보스 [src: Assets/_BinaryData/TableData/MonsterTable.bytes, Docs/ai_monster/MONSTER_SPAWN_DESIGN.md §1.2·§7.2.1] |
| **몬스터 공격 예고** | Phase 0~2 구현(타이밍 해석·배치 렌더·셰이더), Phase 3 일부. 돌진 직선 띠 예고, 몬스터 조준 회전을 따라 도는 예고(2026-10-13) [src: Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태·§5.8, Assets/Scripts/Common/System/System_AttackTelegraph.cs] | Phase 4, 취소 페이드·경고음·LOD·최소 가독 시간 진단 [src: Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md] |
| **장비 옵션·마법부여** | 무기 Base/고유 강화 분리, 옵션 글로벌화, 미장착 장비 Prefix/Postfix 교체·추가 MVP, 마법부여한 기본 아이템은 일반 아이템으로 전환 [src: Wiki/combat.md]. 아이템 레벨 저장과 옵션 티어 T1~T12(일반 판정 T1~T10 구간·가중치, T11·T12 몬스터 등급별 특수 판정), 툴팁 아이템 레벨 표시(2026-10-11) [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §1·§6]. 인벤토리 장비 비교 툴팁(Shift 유지 비교, 화면 위치 기준 좌·우클릭 장착, 2026-10-13) [src: Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md, Wiki/combat.md]. 장비 등급·품질, 드롭 구간표·수량·희귀도, 마법부여 아이템 레벨 티어·진화·품질 다시 굴리기, 판매가 배율(2026-10-21, 진단 1~5 통과) [src: Docs/combat/ITEM_DROP_BALANCE_DESIGN.md §16, Wiki/combat.md] | Tier 1/2/3·저장 왕복·장착 후 실제 타격 PlayMode 검증, 분해·카오스/신성·장착 장비 작업·유니크는 후속 [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12·§14, Docs/combat/ITEM_DROP_BALANCE_DESIGN.md §12]. 드롭·등급·품질·Forge 4작업·판매가·던전 드롭 플레이 확인과 초안 수치 조정, 아이템 44 기본 능력 행 공백, 두 드롭 스탯을 주는 옵션·버프 콘텐츠(0건) [src: Docs/combat/ITEM_DROP_BALANCE_DESIGN.md §14·§16]. T4 이상 고레벨 수치 밸런스 [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §7·§8]. 비교 툴팁의 남은 PlayMode 확인과 묶음 배치·옵션 비교 키 검토, 후속 1~6 [src: Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §첫 구현 범위와 후속 확장, Wiki/combat.md] |
| **플레이어 레벨·참 판** | 레벨·경험치(`ExpTable`)·능력치 포인트와 되돌리기, 능력치 보너스(공용), 정확도 대 회피 적중 판정과 회피 불가 태그, 몬스터 종 배율·접사 날렵한 변환, NPC 정확도·전투력 회피 항, 3×3 참 판·참 6모양·판 자리 9칸, 드롭·NPC 전리품·상점 참 칸·판매, 경험치 바·캐릭터 창 분배·참 판 창·확인 창(2026-10-24, 진단 통과) [src: Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md §3–§8, Wiki/combat.md] | 플레이 확인, 설계 문서 상태 줄·구현 중 결정과 §13 다른 문서 반영, 판 사이 시너지·사망 경험치·NPC 레벨(보류), 경험치 곡선·되돌리기 금액·드롭 비중 플레이 측정 조정, NPC 개인 상한 초과 39명(그대로 둠) [src: Docs/combat/PLAYER_LEVEL_CHARM_DESIGN.md §12·§13, Wiki/combat.md] |
| **스킬 조준·이동 정책** | 커서 추적→효과 직전 커밋, 정책별 재커밋, 화살표 View, 감속·복구, Jump·Dash LaunchCommit, AI도 표의 조준 방식으로 대상 추적(2026-10-13) [src: Wiki/skill_aim.md, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15] | EditMode 테스트 추가·실행, 대표 스킬 PlayMode 실측 [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§10.1] |
| **스킬 전파** | 공용 연쇄·분열 런타임, Instant 4연쇄 Chain Lightning, 대기 주문, 결투 반영 적대 판정 [src: Wiki/skill_propagation.md, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0] | Projectile/분열/OnHit PlayMode 검증, EditMode 테스트 [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §9] |
| **넉백·당기기** | 판정 범위 중심 기준 밀기·당기기, SkillEffect `Knockback`/`Pull`(1030·1031), 장판 `ForcedMoveEffectId`, `System_ForcedMove`(2026-10-12). 돌진 경로 옆 밀기 1032(멧돼지 돌진이 사용)와 실행 단계 넉백 저항 `ForcedMoveImmune`(2026-10-13) [src: Docs/combat/SKILL_DESIGN.md §6.10, Wiki/combat.md] | 1030·1031·장판 연결(0건)과 손 조작 확인, 등급별 배율·벽 충돌 조건 [src: Docs/combat/SKILL_DESIGN.md §6.10] |
| **돌진** | `SkillType.Dash`: 준비 중 조준 회전→출발 때 고정, 일정 속도 직진, 경로 선분 판정·관통·대상당 1회, 벽 즉시 멈춤, 직선 띠 예고, `System_Dash`(Update 805). 첫 콘텐츠 멧돼지 돌진(Skill 52·AI 6)(2026-10-13) [src: Docs/combat/SKILL_DESIGN.md §4 M4·§6.11, Wiki/combat.md] | 손 조작 체감 확인, 플레이어용 M3 Dash Strike 콘텐츠 [src: Docs/combat/SKILL_DESIGN.md §4 M3·§6.11 검증 결과] |
| **스킬 페이지** | P-1 본체와 인스턴스 roll, 드랍·상점 일부 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] | Tier 2/3 콘텐츠, P-2 조건 처리, 장착 UI 폴리시 [src: Docs/combat/SKILL_RUNE_DESIGN.md §10] |
| **경매장** | 설계와 기본값 확정 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §디자인] | GO 신호 전, 전부 미구현 [src: Docs/economy/AUCTION_HOUSE_DESIGN.md §구현 계획] |

## 3. 권장 재개 순서

1. ~~**커밋 정리**: NPC 상점 기본 장비·저장 축소·관련 문서 변경이 미커밋~~ → 2026-10-03 `e3ed610`으로 커밋했다. 측정용 `[SaveTiming]` 로그는 지금도 저장이 성공할 때마다 출력되며, 남길지는 따로 정한다. [src: git e3ed610, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `LogSaveTiming`]
2. ~~**데이터 정리**: `AiTable` 4번(SwampToad)이 SkillTable에서 빠진 SkillId 205를 참조~~ → 2026-10-03 205번 행 복구, 빌드 검증기 통과. [src: Docs/combat/SKILL_DESIGN.md §2, Assets/_BinaryData/TableData/SkillTable.bytes, Assets/Scripts/Editor/AttackTelegraphBuildValidator.cs]
3. **마을 골드 밸런스** (D109 ②): 매입 거절로 NPC 가방이 차는 문제, 가방 전리품에 따른 세이브 증가, 절도 빈도, 첫 며칠에 몰리는 NPC 구매를 함께 본다. [src: Docs/npc/NPC_AMBITION_DESIGN.md D109, Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §7·§11.2]
4. **대화 사연 밸런스** (D109 ①): 자랑 토픽이 사연 자리에서 밀리는 문제. [src: Docs/npc/NPC_AMBITION_DESIGN.md D109]
5. **PlayMode 검증 묶음**: 던전 Profiler·입구 배치 실패율, 마법부여 Tier·저장 왕복, 마을 V7 A~F, 스킬 조준·전파 대표 스킬, 공격 예고 가독성, 아이템 레벨 드랍(원거리 드랍 툴팁·기본 아이템 툴팁·상점 매물 레벨), 장비 등급·품질 드롭과 Forge 4작업·캐릭터 창 두 줄·판매가·던전 보스/상자 드롭(2026-10-21), 장비 비교 툴팁 남은 항목(비교 중 장비 변경·UI 닫기, 저장 상태 불변, 안내 없는 호버, 줄바꿈), 멧돼지 돌진과 몬스터 조준 회전의 손 조작 체감(밀어서 피하기·옆으로 비켜 피하기), 플레이어 레벨·참 판(캐릭터 창 줄 배치와 분배·되돌리기, 경험치 바·레벨업 글자, 참 판 창 끌어다 놓기·회전·툴팁, 상점 참 칸·판 판매 확인 창, 2026-10-24). [src: Docs/combat/SKILL_DESIGN.md §6.11 검증 결과, Docs/map/DUNGEON_RUNTIME_DESIGN.md §7, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12, Docs/village/VILLAGE_REWORK_PLAN.md §V7, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md, Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §8, Docs/combat/ITEM_DROP_BALANCE_DESIGN.md §16, Docs/combat/INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §구현 연결과 완료 기준, Wiki/combat.md]
6. **NPC 행적·반응 마무리**: 설계 문서 §8의 1~5단계는 모두 구현됐다. 남은 것은 커밋과 실제 플레이 확인(대화 화면의 부탁·보고 표시, 소문 교환 속도, 소망 공개·선물 목록·전달 대사, 부상 문구, 후속 소개와 이어지는 부탁)이다. 마을 평판은 보류다. [src: Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md 머리말·§8·§8.1~§8.4]
7. **다음 설계 후보**: NPC 장비 강화(P6a), 평판(Renown), P3 협력·배신 재개, 관찰 반응 데이터. 경매장은 착수 승인이 없다. 던전 중 월드 시뮬은 2026-10-19에 구현해 후보에서 뺐다. [src: Docs/map/DUNGEON_WORLD_SIM_DESIGN.md 상태, Docs/npc/NPC_AMBITION_DESIGN.md §13·§14, Docs/npc/PLAYER_RECOGNITION_DESIGN.md, Docs/economy/AUCTION_HOUSE_DESIGN.md]

## 4. 문서 신뢰도 주의

- `Docs/README.md`는 2026-10-03에 문서 목록과 상태 줄을 갱신했지만, "구현 완료 시스템·진행 중·미구현" 절의 비율과 상세는 2026-07-13 기준이다. `Docs/overview/implementationStatus.md`는 2026-04-08 기준이다. 현재 상태는 이 페이지와 각 주제 정본을, 구현 여부는 코드·테이블을 우선한다. [src: Docs/README.md, Docs/overview/implementationStatus.md, Wiki/_SCHEMA.md]
- `DUNGEON_RUNTIME_DESIGN.md`·`MONSTER_ATTACK_TELEGRAPH_DESIGN.md`·`NPC_EQUIPMENT_SET_PLAN.md`는 구현 뒤에도 "구현 전/검증 대기" 상태 줄이 남아 있다가 2026-10-03에 코드 대조로 고쳤다. 던전 문서는 구현된 [제안]을 [구현]으로 바꿨고(남은 [제안]은 미구현), 공격 예고·스킬·연쇄 계획 문서는 "(2026-10-03 확인: …)" 메모와 ✅/◐/❌ 진행 표시를 붙였다. [src: Docs/map/DUNGEON_RUNTIME_DESIGN.md 상태, Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md 상태, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §9, Docs/npc/NPC_EQUIPMENT_SET_PLAN.md 상태]
- NPC 결정 번호는 NPC_AMBITION·NPC_COMBAT·NPC_SHOP_EQUIPMENT 공용 연번이고(현재 D123까지), 정본 결정표는 `NPC_AMBITION_DESIGN.md` §2다. [src: Docs/npc/NPC_AMBITION_DESIGN.md §2]
- BaseTown 상점 보장은 2026-10-10 신규 게임 초기화에 적용됐으며 기존 세이브 일괄 보정은 하지 않는다. `GOLD_BALANCE_DESIGN.md` §1의 상점 없는 BaseTown·31곳 수치는 2026-10-03 수정 전 진단 기준이다. [src: Docs/village/VILLAGE_REWORK_PLAN.md §시작 마을 기본 상점, Docs/economy/GOLD_BALANCE_DESIGN.md §1·§8 Q10·§11.3]
- `ITEM_ENCHANTMENT_DESIGN.md`는 구현 완료 후에도 active 원본으로 유지한다. §3–§5·§6.1–§6.2·§6.4·§7–§9·§15는 구현 계약, §12는 회귀 검증, §14는 후속 경계로 읽는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Wiki/combat.md]
