---
title: 장비 옵션·마법부여와 무기 공격 프로필
sources:
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md
  - Docs/combat/WEAPON_PROGRESSION_DESIGN.md
  - Docs/combat/SKILLBOOK_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
  - Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md
  - Docs/ai_monster/MONSTER_RARITY_DESIGN.md
  - Assets/Scripts/Item/ItemLevelModTierRules.cs
  - Assets/Scripts/Common/Utility/DropHelper.cs
  - Assets/Scripts/Editor/ItemEnchantmentDiagnostics_ItemLevel.cs
  - Assets/_BinaryData/TableData/MonsterArchetypeTable.bytes
  - Assets/Scripts/Common/Component/SkillAttackProfileComponent.cs
  - Assets/Scripts/Common/Component/StatComponent.cs
  - Assets/Scripts/Common/Component/StatModifierBuffer.cs
  - Assets/Scripts/Common/GlobalEnum.cs
  - Assets/Scripts/Common/System/System_Skill.cs
  - Assets/Scripts/Data/ItemData.cs
  - Assets/Scripts/Common/Utility/EquipHelper.cs
  - Assets/Scripts/Common/Utility/WeaponHelper.cs
  - Assets/Scripts/Common/System/System_StatCalculation.cs
  - Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs
  - Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs
  - Assets/Scripts/Skill/Combat/DamageCalculator.cs
  - Assets/Scripts/Common/Tables.cs
  - Assets/Scripts/Data/DataManager_Runtime.cs
  - Assets/Scripts/Data/DataManager_Table.cs
  - Assets/Scripts/Data/PlayerData.cs
  - Assets/Scripts/Data/WorldData.cs
  - Assets/Scripts/Editor/DownloadTables.cs
  - Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs
  - Assets/Scripts/Base/Inventory.cs
  - Assets/Scripts/Item/EquipmentOptionText.cs
  - Assets/Scripts/Item/ItemEnchantmentService.cs
  - Assets/Scripts/Manager/ItemManager.cs
  - Assets/Scripts/Npc/NpcSaveData.cs
  - Assets/Scripts/UI/CharacterUI.cs
  - Assets/Scripts/Manager/TooltipManager.cs
  - Assets/Scripts/UI/UIForge.cs
  - Assets/UI/Tooltip/Tooltip.uxml
  - Assets/UI/Forge/Forge.uxml
  - Assets/UI/Forge/Forge.uss
  - Assets/_BinaryData/TableData/ModTable.bytes
  - Assets/_BinaryData/TableData/ModTierTable.bytes
  - Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes
  - Docs/combat/SKILL_DESIGN.md
  - Docs/combat/SKILL_EFFECT_IDEAS.md
  - Assets/Scripts/Skill/Combat/ForcedMoveStarter.cs
  - Assets/Scripts/Skill/Combat/SkillEffectContext.cs
  - Assets/Scripts/Skill/Combat/SkillHitProcessor.cs
  - Assets/Scripts/Common/System/System_ForcedMove.cs
  - Assets/Scripts/Common/System/System_AreaEffect.cs
  - Assets/Scripts/Common/System/System_Render.cs
  - Assets/Scripts/Common/System/System_Move.cs
  - Assets/_BinaryData/TableData/SkillEffectTable.bytes
  - Assets/_BinaryData/TableData/AreaEffectTable.bytes
last_synced: 2026-09-01
last_updated: 2026-10-12
status: implemented-contract-active-validation-pending
---

# 장비 옵션·마법부여와 무기 공격 프로필

## 한 줄 요약

무기의 고정 기본 능력과 인스턴스별 고유 강화만 무기 캐시에 들어가며, 장비의 Implicit/Prefix/Postfix는 부위와 무관한 글로벌 Modifier로 합산된다. 미장착 인벤토리 장비의 Prefix/Postfix는 Tier 1 이상 제련소에서 랜덤 교체·추가할 수 있고, 스킬 공격 프로필은 변경된 옵션까지 태그에 맞춰 한 번 계산해 예상값과 실제 전투가 같은 값을 소비한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계·§스킬 공격 프로필 구성, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §0–§8, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs] 새로 만드는 장비의 Prefix/Postfix 티어(T1~T12)는 장비에 저장된 아이템 레벨로 정해진다(2026-10-11). [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §1, Assets/Scripts/Item/ItemLevelModTierRules.cs] 명중 시 넉백·당기기(2026-10-12 구현)도 이 페이지 뒤쪽에 정리한다. [src: Docs/combat/SKILL_DESIGN.md §6.10]

## 문서 정본

현행 설계 규칙은 `combatSystem.md`의 장비 옵션·공격 프로필 절에, 마이그레이션과 남은 과제는 `ModSystem_Changelog.md`의 2026-08-22 항목에 반영돼 있다. 변경 이력의 초기 Mod 리팩터링 절은 당시 구조를 기록한 역사이며, 실제 구현 여부는 현재 코드를 함께 확인한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계–§갱신·저장·표시, Docs/combat/ModSystem_Changelog.md §2026-08-22 — 장비 옵션 글로벌화와 무기 고유 강화 분리, Assets/Scripts/Data/ItemData.cs:L299-400]

새 장비의 아이템 레벨과 옵션 티어(구간·가중치·T11·T12 특수 판정)는 `ITEM_LEVEL_MOD_TIER_DESIGN.md`가 정본이고, 티어별 값·열리는 레벨·등급별 특수 판정 확률은 ModTier·MonsterArchetype 시트가 정본이다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §2·§4·§5]

일반 장비 마법부여의 작업·비용·후보·Forge 접근성 계약은 `ITEM_ENCHANTMENT_DESIGN.md`가 정본이며, 구현 여부는 `ItemEnchantmentService`, Forge UI, 비용·Mod 테이블을 함께 확인한다. 마법부여는 무기 고유 강화와 별도 경로다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes]

### `ITEM_ENCHANTMENT_DESIGN.md` 문서 수명주기

**보존 판정은 유지(active)**다. 구현이 끝났더라도 랜덤 선택 방식, 슬롯·Group·Tier 자격, 메모리 원자성의 범위, Forge 접근성, 비목표와 후속 확장의 설계 의도를 다른 원본 한 곳에서 모두 대체하지 못하므로 `_archive` 대상이 아니다. `combat.md`는 이 원본을 대체하지 않고 현재 코드와 대조한 짧은 진입점 역할을 한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §1·§3–§9·§14–§15, Docs/combat/combatSystem.md, Wiki/_SCHEMA.md] [추론: 현행 계약과 후속 확장 근거가 다른 원본으로 완전히 이관되기 전까지 active 유지]

| 문서 구간 | 현재 읽는 방식 |
| --- | --- |
| §0–§1, §3–§5, §6.1–§6.2, §6.4, §7–§9, §15 | 구현된 MVP의 행위·경계·완료 조건을 설명하는 현행 계약이다. 실제 구현 판정은 서비스·UI·테이블 코드와 함께 확인한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §0–§9·§15, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs] |
| §6.3 | 런타임 비용 조회는 구현됐지만, 신규 Google Sheet 등록·단건 다운로드 문장은 아직 운영 경로에 반영되지 않았다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §6.3, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs] |
| §12 | 구현 여부가 아니라 회귀 검증 기준이다. Editor 진단 코드와 PlayMode 체크리스트의 근거로 계속 사용한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs] |
| §14 | 신성한/카오스/진화의 오브, 무기 고유 제련, 장착 장비 작업, Forge 상위 Tier의 후속 설계 경계다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §14] |
| §2.2·§2.4 | 구현 전 기준선이다. 옛 Tier 게이트·로그 전용 UI와 일반 Affix 14개 스냅샷은 현행 상태가 아니다. 현재 UI는 서비스를 실행하고, 테이블은 Prefix 10개·Postfix 13개와 Tier 행 276개(일반 옵션 23종 × T1~T12, 2026-10-11)를 가진다. Base 옵션은 `EquipmentBaseStat`의 장비별 고정값과 Tier 0을 쓰므로 티어 행이 없다(쓰이지 않던 Base 티어 22행은 2026-10-11 삭제). [src: Assets/Scripts/Data/ItemData.cs `InitBaseMods`] [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §2.2·§2.4, Assets/Scripts/UI/UIForge.cs, Assets/_BinaryData/TableData/ModTable.bytes, Assets/_BinaryData/TableData/ModTierTable.bytes] |
| §10–§11·§13 | 파일별 구현 계획·콘텐츠 보강안·Phase 순서로서 구현 이력으로만 읽는다. 현재 파일 위치와 완료 여부는 코드·테이블이 정본이다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §10–§11·§13, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs, Assets/_BinaryData/TableData/ModTable.bytes] |

따라서 MVP 런타임 구현은 완료로 분류하되, 문서 안의 모든 시제와 “현행 기준선”을 현재 상태로 읽지는 않는다. 문서를 제거하거나 전체를 구현 현황 대시보드로 사용하는 대신, 확정 규칙·검증 기준·후속 경계의 원본으로 유지한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs] [추론: 구현 완료와 원본 문서의 보존 가치는 별개]

## 무기 도달거리와 스킬 범위 확정 설계

2026-10-05 사용자 확정 규칙이며 **미구현**이다. 범위 옵션은 도달거리와 타격반경의 길이를 늘린다. 옵션끼리는 합산하고 무기 도달거리 배율은 독립적으로 곱해, 동일 스킬·옵션에서 무기별 거리 비율을 유지한다. [src: Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md §확정 규칙·§현행 코드와 구현 경계]

```text
범위 증가 배율 = 1 + 범위 증가 옵션 합계 / 100
최종 도달거리 = 스킬 기본 도달거리 × 무기 배율 × 범위 증가 배율
최종 타격반경 = 스킬 기본 타격반경 × 무기 배율 × 범위 증가 배율
```

위 식의 정본은 `WEAPON_SKILL_RANGE_DESIGN.md`다. 부채꼴 각도는 유지하고 무기 범위를 사용하지 않는 스킬의 무기 배율은 1.0으로 둔다. 계열별 초기 배율의 상태는 무기 성장 설계를 따르며, 최종 배율·범위 옵션 수치·적용 스킬 목록·고정 거리 추가 옵션은 후속 결정이다. [src: Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md §확정 규칙·§후속 결정, Docs/combat/WEAPON_PROGRESSION_DESIGN.md §주문 무기 성장 확정 설계]

구현 시 사용·AI 교전 사거리와 실제 타격 형상을 구분하고, 거리 제한·판정·공격 예고가 같은 계산을 사용하도록 연결한다. 캐릭터 Collider 크기는 범위 배율로 확대하지 않는다. [src: Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md §현행 코드와 구현 경계]

## 무기 계열별 성장 방향

2026-10-05 사용자 요청으로 성장 방향과 현행 장비 공백을 정리했으며 콘텐츠 재배치와 구현은 아직 하지 않았다. 지원하는 계열마다 성장 구간별 장비와 획득 경로를 마련해 같은 계열을 유지하며 업그레이드할 수 있게 한다. 계열의 거리·공속·한 방 피해 성향을 유지하며 상위 장비의 기본 성능과 옵션 수준을 높이는 방향이다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §정리한 성장 원칙]

같은 날 사용자 확정으로 표창을 제외하고 투창을 유지한다. 대검·활·석궁·지팡이는 양손이며 보조 장비를 사용할 수 없다. 한손검·단검·도끼·투창·완드는 한손 계열로 쌍수를 허용한다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §지원 계열과 장착 규칙]

쌍수 계산 초기안은 각 무기의 고유 강화 반영 후 피해를 `(오른손 + 왼손) × 0.7`로 합성하고 APS·무기 기본 치명타·도달거리 배율은 산술평균한다. 기존 타격 횟수와 타격당 치명타 한 번을 유지하며 별도 쌍수 공속 보너스는 없다. 동일 수치 두 자루의 무기 피해 항은 한 자루 대비 40% 증가한다. 평균 도달거리 배율을 기존 범위 식에 대입하며 공통 옵션과 스킬 계수의 계산 경로는 유지한다. 이 초기안은 **확정·미구현**이며 현행 무기 조회는 오른손 우선 경로다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §쌍수 계산 초기안·§쌍수 구현의 현행 기준, Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md §확정 규칙]

현재 장비를 이름 기준으로 대응하면 한손검·단검·도끼 등은 Tier 1·2·3에 장비가 있고 대검·석궁은 Tier 1, 투창은 Tier 2·3 장비가 비어 있다. 기존 표창 행과 연결 데이터는 아직 삭제하지 않았다. 초·중·후반 3구간은 점검용 틀이며 근접 수치는 초안, 원거리는 임시 기준이다. 최종 구간 수·전체 계열의 최종 밸런스·추가 고유 효과·신규 장비 목록은 미확정이다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §현재 장비의 계열별 배치·§근접 4종 기준 성능표 초안·§원거리 3종 임시 기준 성능표·§이번 정리의 범위]

지팡이·완드는 같은 날 사용자 확정으로 주문 강화용 고정 Implicit으로 성장시킨다. 초기 기준은 지팡이 주문 피해 증가 +60%·고정 시전 속도 증가 없음·거리 배율 1.30, 완드 한 자루 주문 피해 증가 +20%·시전 속도 증가 +10%·거리 배율 1.00이다. 상위 장비는 고정 주문 피해 증가 수치를 높이며 구간별 실제 값은 후속 결정이다. 이 설계는 **확정·미구현**이다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §주문 무기 성장 확정 설계]

완드 쌍수의 주문 피해 증가·시전 속도 옵션은 글로벌 합산하여 동일 완드 두 개가 +40%·+20%를 제공한다. 무기 피해 ×0.7과 APS 평균은 Attack용 무기 수치에 적용하고 주문 강화 옵션에는 적용하지 않는다. 주문 기본 치명타·시전 시간은 스킬 값을 유지하고 무기 범위를 사용하는 주문의 거리 배율은 두 무기의 평균을 사용한다. 현행 시전 속도 소비는 구현돼 있지만 주문 피해 % 증가 옵션과 고정 주문 옵션 배치·무기 범위 연결은 구현 과제다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §주문 무기 성장 확정 설계, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs, Assets/Scripts/Common/System/System_Skill.cs]

계열별 전체 배치표와 다음 작업 순서는 [무기 성장 정리](../Docs/combat/WEAPON_PROGRESSION_DESIGN.md)에 있다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md]

## 스킬북 내부 스킬 레벨 성장 계획

2026-10-05 사용자 확정으로 스킬북 안의 스킬 레벨을 **책마다 개별 저장하고 골드로 올리는 방식**을 사용하며 아직 미구현이다. 책 등급은 기존 페이지 용량·슬롯을 담당하고 스킬 레벨은 해당 책을 장착한 슬롯의 피해 성장을 담당한다. 동일 SkillId의 책도 레벨을 공유하지 않으며 장착·해제·이동·거래·저장 후 그 책의 레벨과 페이지를 유지한다. 책 등급 승급이나 책 간 레벨 이전은 초기 계획에 포함하지 않는다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §스킬북 내부 스킬 레벨 성장 계획]

기존 피해 배율 초안의 +0~+10은 스킬 Lv.1~Lv.11로 대응시키고, 새 책과 레벨 필드가 없는 기존 책은 Lv.1로 시작하는 계획이다. 최대 레벨·공격과 주문의 레벨별 피해 배율·전체 성장 비교는 수치 초안이며 골드 금액·수급·레벨업 시점은 추후 결정이다. 저장·슬롯별 런타임 레벨·피해 프로필·골드 갱신·UI·기존 진단의 순서로 구현한다. 공용 SkillTable의 Tier·책 생성 후보와 AI 기본 스킬에는 책 레벨을 반영하지 않는다. [src: Docs/combat/WEAPON_PROGRESSION_DESIGN.md §스킬 레벨별 피해 배율 초안·§피해 적용 항과 계산 순서·§구현 순서와 완료 조건, Docs/combat/SKILLBOOK_DESIGN.md §2.7·§2.8]

## 책임 경계

| 구분 | 저장·정의 | 런타임 책임 |
| --- | --- | --- |
| 무기 기본 능력 | `EquipmentBaseStatTable`에서 생성된 `ModSlot.Base` | `EquipmentData.WeaponStats`(저장하지 않는 계산 캐시, 2026-10-03 D122)가 Base만 읽는다. 무기 Base는 글로벌 Modifier로 등록하지 않는다. [src: Assets/Scripts/Data/ItemData.cs:L299-400, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-137] |
| 무기 고유 강화 | `WeaponLocalEnhancementInstance` 목록. 피해 5속성·치명타·공격속도 증가율을 일반 `ModInstance`와 분리 저장 | 종류별 증가율을 합산한 뒤 해당 무기 Base에 한 번 적용한다. [src: Assets/Scripts/Common/GlobalEnum.cs:L487-506, Assets/Scripts/Data/ItemData.cs:L156-184, Assets/Scripts/Data/ItemData.cs:L299-400] |
| 글로벌 장비 옵션 | 모든 Implicit/Prefix/Postfix | 장착 부위와 무관하게 `StatModifier` 또는 `ModPoolComponent`로 등록한다. `Tags=None`은 모든 스킬, 태그가 있으면 그 태그를 모두 가진 스킬에만 적용한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-241, Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs:L42-128] |

`AllowedEquipTypes`는 옵션이 롤 가능한 장비 종류만 제한하며 계산 경로를 바꾸지 않는다. `Group` 중복은 같은 `ModSlot` 안에서만 금지한다. Base·Implicit 초기화와 Prefix·Postfix 랜덤 롤 모두 자기 슬롯의 기존 Group만 검사하므로 서로 다른 슬롯 사이의 같은 Group은 허용된다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Manager/ItemManager.cs]

## 새 장비의 아이템 레벨과 옵션 티어

2026-10-11 사용자 결정으로 구현됐고 실제 플레이 확인은 남았다. 일반 옵션을 T3에서 T12까지 늘렸고, 장비를 만들 때 아이템 레벨(1~99)을 정해 `EquipmentData.ItemLevel`에 저장한다. 몬스터 드랍은 몬스터 레벨, NPC 사냥·던전 원정 전리품은 기준 레벨, 상점 랜덤 매물은 마을이 있는 청크의 지역 레벨이다. 기본 아이템·시작 장비·이전 장비는 0(없음)이며 저장을 생략하므로 세이브 버전은 그대로다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §1·§6, Assets/Scripts/Data/ItemData.cs `EquipmentData.ItemLevel`, Assets/Scripts/Common/Utility/DropHelper.cs, Assets/Scripts/Npc/NpcActivityResolver.cs `RollHuntLoot`, Assets/Scripts/Village/VillageManager.cs `RollMerchantStock`]

**일반 판정(T1~T10)**: ModTier `RequiredLevel`은 이제 "티어가 열리는 아이템 레벨"이다(T1~T3 1, T4 11, T5 24, T6 37, T7 50, T8 63, T9 76, T10 90). 열린 가장 높은 티어가 상한이고 상한에서 4단계 아래까지만 후보라서, 아이템 레벨 1–10은 T1~T3, 90–99는 T6~T10이 나온다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §2, Assets/_BinaryData/TableData/ModTierTable.bytes, Assets/Scripts/Item/ItemLevelModTierRules.cs `BuildNormalTierCandidates`]

후보 안에서는 `Weight`(T1 100 → T10 10)로 고르되, 구간 진행도에 따라 새로 열린 상한 티어는 30%에서 100%로(마지막 구간은 200%까지) 늘고, 다음 구간에서 빠질 최저 티어는 100%에서 30%로 준다. 그래서 같은 구간의 레벨 90과 99도 결과가 다르고(T10 약 2% → 약 16%), 구간 경계에서 티어가 서서히 들어오고 빠진다. 티어를 고를 때 난수를 한 번 쓰는 것은 이전과 같다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §3, Assets/Scripts/Item/ItemLevelModTierRules.cs, Assets/Scripts/Manager/ItemManager.cs `RollRandomMods`]

**특수 판정(T11·T12)**: 일반 판정 뒤 장비 1개당 한 번 굴려, 성공하면 이미 붙은 옵션 하나를 T11 또는 T12로 올리고 수치를 다시 뽑는다. 아이템 레벨 90 이상인 몬스터 드랍에서만 일어나며, 확률은 몬스터 등급 시트 I·J열(만분율)로 일반 0.5%/0.1%에서 엔드게임 보스 10%/3%까지 오른다. NPC 전리품·상점 매물·마법부여에는 없다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §4, Docs/ai_monster/MONSTER_RARITY_DESIGN.md §3, Assets/_BinaryData/TableData/MonsterArchetypeTable.bytes, Assets/Scripts/Manager/ItemManager.cs `TryApplySpecialTier`]

T1~T3 값은 바꾸지 않아 기존 장비와 상점 기본 아이템은 그대로다. T4~T12는 T3 최대값 기준으로 고정 수치형은 T10 6배·T12 9배, 퍼센트형은 T10 2.2배·T12 2.8배, 저항은 T10 44·T12 50까지 키운 초안이다. 고레벨 몬스터 곡선과의 대조는 하지 않았다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §5·§7, Assets/_BinaryData/TableData/ModTierTable.bytes]

다운로드 검증은 Mod마다 T1부터 빈칸 없는 티어, 줄지 않는 `RequiredLevel`, T12 초과 금지를 확인한다. 진단 메뉴 `Item Enchantment MVP`는 구간표·레벨별 평균 티어 비감소·특수 판정·등급 확률·아이템 레벨 저장 왕복을 검사하며 2026-10-11 PASS였다. [src: Assets/Scripts/Editor/DownloadTables.cs `ValidateModSetup`, Assets/Scripts/Editor/ItemEnchantmentDiagnostics_ItemLevel.cs, Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §8]

## 일반 장비 마법부여 MVP

### 범위와 비용

- 대상은 플레이어 인벤토리의 수량 1인 미장착 장비다. Base·Implicit·`WeaponLocalEnhancements`는 바꾸지 않으며 Prefix/Postfix만 슬롯별 최대 3개까지 다룬다. 요청은 인벤토리 슬롯과 `ItemInstanceId`를 함께 재검증한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3·§7, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]
- `ReplaceAffix`는 교체 가능한 기존 Prefix/Postfix 하나를 균등 선택하고 같은 슬롯의 다른 유효 Mod로 교체한다. 비용은 변화의 오브 1개와 25 Gold다. `AddAffix`는 빈 Prefix/Postfix 전체 후보에서 Mod 하나를 균등 선택하며 엑잘티드 오브 1개와 40 Gold를 소비한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3.1·§3.4·§3.6, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes]
- 상점 기본 아이템(`EquipmentData.BasicItemId > 0`, 고정 옵션)도 대상이 될 수 있다. 커밋 트랜잭션은 옵션을 바꾸기 직전에 `BasicItemId`를 지워 일반 아이템으로 만들고, 이후 옵션 전체가 저장된다. NPC는 기본 아이템만 사므로 마법부여한 장비는 NPC 구매 후보에서 빠진다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §6.1·D122, Assets/Scripts/Item/ItemEnchantmentService.cs `ItemEnchantmentTransaction.TryCommit`]
- Basic·Standard·Premium은 MVP의 두 작업과 비용이 같다. 서비스는 실제 Forge Entity의 `FacilityTier`를 다시 읽고, Tier 0 구 배치만 ObjectSet으로 보완한다. UI나 요청이 넘긴 숫자만으로 생산 경로를 승인하지 않는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]

### 후보와 트랜잭션

후보는 장비 종류 비트가 단일이고 `AllowedEquipTypes`에 포함되며, 실제 런타임 소비 경로가 허용 목록에 있는 Passive 효과여야 한다. 같은 Slot·Group 중복은 제외하고 구 세이브에 이미 중복이 있으면 실행을 거부한다. 선택된 Mod의 Tier는 `RequiredLevel <= Player Level`, `Tier <= Item Tier`, `Weight > 0`을 만족하는 행에서 Weight 가중치로 뽑고 값은 해당 범위에서 롤한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3.2–§3.5, Assets/Scripts/Item/ItemEnchantmentService.cs] 이 규칙은 2026-10-11에도 그대로 두었다(마법부여는 별도 작업). 플레이어 레벨이 1에서 오르지 않으므로 이전에는 사실상 T1만 나왔고, `RequiredLevel` 재해석 뒤에는 T1~T3 중 장비 Tier 이하가 나온다. T11·T12는 요구 레벨 90이라 나오지 않는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3.5 2026-10-11 갱신, Assets/Scripts/Data/PlayerData.cs `Level`]

`Inspect`는 난수를 소비하지 않고 후보 수·재료·Gold·실패 원인만 반환한다. `Execute`는 모든 조건을 다시 확인한 뒤 변경 전 스냅샷을 만들고, 완성된 새 Mod를 교체/추가하며, 인벤토리 전체 스택에서 낮은 슬롯부터 재료를 소비하고 Gold를 차감한다. 성공 후 무기 캐시를 무효화하고 `SavePlayer()`를 한 번 요청하며 변경 전·후 불변 스냅샷을 반환한다. 원자성은 메모리 변경 범위이며 저장 요청 실패까지 되돌리는 디스크 트랜잭션은 아니다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §7–§8, Assets/Scripts/Base/Inventory.cs, Assets/Scripts/Item/ItemEnchantmentService.cs]

### 콘텐츠와 UI

현행 Mod 콘텐츠는 마법부여 후보용 최대 마나·체력/마나 재생·4속성 저항·치명타 피해·시전속도 옵션과 각 Tier 행을 포함하며, 추가 번개/중독 피해에도 Tier 행이 연결돼 있다. 다운로드 시에는 Mod와 ModTier를 함께 받아 Group·장비 마스크·참조·범위·Weight를 검증한 뒤 저장한다. [src: Assets/_BinaryData/TableData/ModTable.bytes, Assets/_BinaryData/TableData/ModTierTable.bytes, Assets/Scripts/Editor/DownloadTables.cs]

Forge UI는 장비와 두 랜덤 작업의 2열 구조이며 실행 전에는 후보 수와 비용만, 성공 후에는 실제 변경 전·후 옵션을 표시한다. Tooltip과 Forge는 `EquipmentOptionText`를 공유해 치명타·공격속도·저항·재생 등의 문구를 같은 방식으로 만든다. 분해 버튼은 보이지만 MVP에서는 비활성이다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §9, Assets/Scripts/UI/UIForge.cs, Assets/UI/Forge/Forge.uxml, Assets/UI/Forge/Forge.uss, Assets/Scripts/Item/EquipmentOptionText.cs, Assets/Scripts/Manager/TooltipManager.cs]

## 계산 규칙

### 공통 집계

고정값은 먼저 합산하고 같은 Stat의 증가율은 모두 더한 뒤 한 번만 곱한다. Override는 마지막에 적용한다. 따라서 10% 증가와 20% 증가는 30% 증가이지 1.1×1.2가 아니다. [src: Docs/combat/combatSystem.md §스킬 공격 프로필 구성, Assets/Scripts/Common/System/System_StatCalculation.cs:L75-128]

```text
Final = (Base + Sum(GlobalFlat)) * (1 + Sum(GlobalIncreased) / 100)
```

### 치명타 확률

Attack은 강화 반영 무기 치명타를, Spell은 `SkillTable.BaseCriRate`를 스킬 기본값으로 사용한다. 캐릭터 기본 치명타·글로벌 고정 치명타·OnHit 보너스를 더한 뒤 글로벌 증가율을 한 번 적용한다. [src: Docs/combat/combatSystem.md §스킬 공격 프로필 구성, Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs:L104-118, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L135-141]

```text
FinalCrit = Clamp(
    (SkillBaseCrit + CharacterBaseCrit + Sum(GlobalFlatCrit) + OnHitCrit)
    * (1 + Sum(GlobalCritIncrease) / 100),
    0, 100)
```

치명타 확률은 `float`로 보존한다. CharacterUI는 소수 둘째 자리까지 표시하고, 예상 피해와 실제 치명타 난수 판정도 같은 `TotalCritRate`를 사용한다. 예를 들어 무기 4% + 캐릭터 5%에 8% 증가면 9.72%다. [src: Docs/combat/combatSystem.md §스킬 공격 프로필 구성, Assets/Scripts/Common/Component/SkillAttackProfileComponent.cs:L20-23, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L135-141, Assets/Scripts/Skill/Combat/DamageCalculator.cs:L121-153, Assets/Scripts/Skill/Combat/DamageCalculator.cs:L238-244, Assets/Scripts/UI/CharacterUI.cs:L163-187]

### 공격속도

Attack의 최종 초당 공격 횟수는 강화 반영 무기 APS에 스킬 배율과 글로벌 공격속도 증가 합계를 곱한다. 실제 Start/Process/End 시간도 이 프로필 APS의 역수를 사용한다. Spell 프로필의 공격속도는 0이며 실제 주문 시간에는 기존 시전속도 경로만 적용한다. [src: Docs/combat/combatSystem.md §공격 속도 / 시전 속도, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L148-160, Assets/Scripts/Common/System/System_Skill.cs:L243-272]

```text
FinalAttackSpeedPerSecond
    = EnhancedWeaponAttackSpeed
    * SkillTable.BaseAttackSpeedMul / 100
    * (1 + Sum(GlobalAttackSpeedIncrease) / 100)
```

### 추가 피해

무기 캐시에는 Base와 고유 강화 피해만 들어간다. 일반 추가 피해는 `StatModifier`로 등록되어 스킬 태그가 일치할 때 무기 피해·SkillTable 피해·스킬 페이지 OnHit 피해와 합산된다. 현행 비-Base 추가 피해 Mod는 모두 `Tags=Attack`이다. [src: Docs/combat/combatSystem.md §스킬 공격 프로필 구성, Assets/Scripts/Data/ItemData.cs:L308-400, Assets/Scripts/Common/Utility/EquipHelper.cs:L187-231, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L80-133, Assets/_BinaryData/TableData/ModTable.bytes:L1]

## 갱신과 표시

- 장착·해제 시 Modifier 변경 직후 소유 스킬 프로필을 다시 만들고, `StatDirtyTag` 계산 후에도 파생 프로필을 동기화한다. [src: Assets/Scripts/Common/Utility/EquipHelper.cs:L145-152, Assets/Scripts/Common/Utility/EquipHelper.cs:L290-297, Assets/Scripts/Common/System/System_StatCalculation.cs:L145-161]
- 스탯 재계산으로 최대 HP/MP가 낮아지면 현재 HP/MP를 새 최대치 범위로 보정한 뒤 파생 컴포넌트와 공격 프로필을 동기화한다. [src: Assets/Scripts/Common/System/System_StatCalculation.cs]
- 구 세이브는 `WeaponLocalEnhancements` null을 빈 목록으로 복구하고 무기 캐시를 무효화한다. 플레이어·NPC 인벤토리와 장비는 기존 ItemData 로드 경로를 사용하며 월드 드랍도 `WorldData.LoadCompleted`에서 복구한다. [src: Assets/Scripts/Data/ItemData.cs:L250-272, Assets/Scripts/Data/PlayerData.cs:L78-98, Assets/Scripts/Npc/NpcSaveData.cs:L142-151, Assets/Scripts/Data/WorldData.cs:L40-49]
- 장비 툴팁은 기본 능력, 무기 고유 강화, 장비 옵션을 별도 영역으로 표시하며 고정 치명타와 증가 치명타 문구를 구분한다. 옵션 문구는 Forge 비교 화면과 같은 포맷터를 사용한다. [src: Docs/combat/combatSystem.md §갱신·저장·표시, Assets/UI/Tooltip/Tooltip.uxml, Assets/Scripts/Manager/TooltipManager.cs, Assets/Scripts/Item/EquipmentOptionText.cs] 아이템 레벨이 있는 장비는 등급 줄 아래에 "아이템 레벨 N"을 표시하고, 0이면 숨긴다(2026-10-11). [src: Assets/Scripts/Manager/TooltipManager.cs `BuildEquipmentContent`, Assets/UI/Tooltip/Tooltip.uxml `eq-item-level`]
- 툴팁은 콘텐츠나 `display` 변경 직후 위치를 한 번 계산하고, 실제 크기가 확정되는 `GeometryChangedEvent`에서 보정해 화면 경계 배치를 갱신한다. [src: Assets/Scripts/Manager/TooltipManager.cs]
- `ModTable` 5002는 `IncreasedStat/AttackSpeedMul`, 1003은 `IncreasedStat/CriRateMul`로 글로벌 증가 버킷에 연결된다. [src: Assets/_BinaryData/TableData/ModTable.bytes:L1, Assets/Scripts/Common/System/System_StatCalculation.cs:L232-257]

## 넉백·당기기 (강제 이동)

2026-10-12 구현했고 사용자 커밋 전이다. 명중한 대상을 판정 범위의 중심에서 짧게 밀어내거나 중심 쪽으로 끌어당긴다. 정본은 `SKILL_DESIGN.md` §6.10이고, `SKILL_EFFECT_IDEAS.md` #6의 초안 메모(FixedUpdate·끝 위치 보간)는 이 설계로 대체됐다. [src: Docs/combat/SKILL_DESIGN.md §6.10, Docs/combat/SKILL_EFFECT_IDEAS.md #6]

| 공격 | 기준점 | 밀기 | 당기기가 멈추는 곳 |
|---|---|---|---|
| 직접 공격(`Direction`) | 공격자 발 위치 | 공격자 반대쪽 | 두 몸이 닿는 거리 |
| 지점 공격(`Position`) | 커밋 지점 | 지점 바깥쪽 | 지점 |
| 장판·폭발 | 장판 중심 | 중심 바깥쪽 | 중심 |
| 발사체·연쇄 | 없음 | 날아온 방향 | 쏜 사람과 닿는 거리 |

[src: Docs/combat/SKILL_DESIGN.md §6.10 방향 규칙, Assets/Scripts/Skill/Combat/ForcedMoveStarter.cs `TryStartFromHit`·`TryStartFromArea`]

- **데이터**: 효과는 SkillEffect 행(`Knockback`/`Pull`, `OnHit`, Param1=거리·Param2=시간)으로 정의한다. 스킬에는 `SkillEffectIds`로, 장판에는 `AreaEffectTable.ForcedMoveEffectId`로 붙인다. 장판은 SkillEffect 트리거를 실행하지 않으므로 `System_AreaEffect`가 피해·버프 뒤에 직접 적용하고 발동 확률도 따로 판정한다. 등록 행은 1030 `Knockback_Short`·1031 `Pull_Short`(1칸·0.15초)이며, 현재 연결된 스킬·장판은 없다. [src: Docs/combat/SKILL_DESIGN.md §6.10 데이터, Assets/Scripts/Common/System/System_AreaEffect.cs, Assets/_BinaryData/TableData/SkillEffectTable.bytes, Assets/_BinaryData/TableData/AreaEffectTable.bytes]
- **적용 규칙**: 밀리는 대상의 스킬은 취소하지 않는다. 밀리는 중에 온 새 넉백은 무시한다. 회피, 이 타격으로 사망, 점프 중, 토템, 스스로 움직이지 않는 대상은 제외하고 막기는 밀린다. 몬스터 등급과 무관하며, 스킬 페이지 랜덤 풀에는 넣지 않는다(Tier 0). [src: Docs/combat/SKILL_DESIGN.md §6.10 적용 규칙, Assets/Scripts/Skill/Combat/ForcedMoveStarter.cs `CanStart`]
- **런타임**: `ForcedMoveStarter`가 판정·방향·멈춤 거리를 계산해 `ForcedMoveComponent`를 붙인다. `System_ForcedMove`(Update 810)가 처음 빠르고 끝에 느린 곡선으로 진행하고, 이동량을 이동 반경 0.9배 이하로 나눠 축 분리 충돌을 적용한다. 넉백 중에는 `System_Render`가 자기 이동을 위치에 반영하지 않고, `System_Move`는 플레이어 입력 속도를 0으로·이동 상태를 Idle로 둔다. 점프 시작과 던전 출입 초기화가 넉백을 지우며, 세이브에는 저장하지 않는다. [src: Assets/Scripts/Common/System/System_ForcedMove.cs, Assets/Scripts/Common/System/System_Render.cs, Assets/Scripts/Common/System/System_Move.cs, Assets/Scripts/Common/System/System_Skill.cs `StartJump`, Assets/Scripts/Dungeon/DungeonManager.cs]
- 적중 정보에 기준점 종류(`HitOriginKind`: None/Owner/Point)와 위치를 새로 실었다. `ImpactDirection`은 전파 탐색 축이라 이 용도로 덮어쓰지 않는다. [src: Assets/Scripts/Skill/Combat/SkillEffectContext.cs, Assets/Scripts/Skill/Combat/SkillHitProcessor.cs, Assets/Scripts/Common/System/System_Skill.cs `ProcessSkillHit`]
- **검증**: 2026-10-12 컴파일·표 다운로드와 PlayMode 측정을 통과했다. 측정 항목은 방향·거리·곡선, 벽 정지·미끄러짐·무관통, 중복 무시, 장판 밀기·당기기, 접촉 거리 멈춤, 예외, 자기 이동 차단이다. 측정은 검증용으로 잠시 붙인 연결로 했고 그 연결은 되돌렸다. [src: Docs/combat/SKILL_DESIGN.md §6.10 검증 결과]

## 연결된 것들

- 전체 구현 현황: [current_status.md](current_status.md)
- NPC 장비도 같은 ItemData 복구 경로와 무기 조회 경로의 영향을 받는다: [npc.md](npc.md) [src: Assets/Scripts/Npc/NpcSaveData.cs:L142-151, Assets/Scripts/Common/Utility/WeaponHelper.cs:L12-38]

## 미해결 / 주의

- 넉백·당기기는 아직 어떤 스킬·장판에도 연결되지 않았다. 손 조작 확인(밀리는 몬스터의 공격 계속, 플레이어 조작 복귀, 던전 출입, 체감)은 콘텐츠에 붙일 때 한다. AreaEffect↔SkillEffect 참조 대조는 전체 다운로드에서만 돌고, 단일 다운로드의 잘못된 Id는 실행 시 오류 로그 후 무시된다. 등급별 배율, 벽 충돌 조건(`WallNearby`·`KnockbackTarget`), 갈고리형 당기기는 후속이다. [src: Docs/combat/SKILL_DESIGN.md §6.10, Assets/Scripts/Editor/DownloadTables.cs `ValidateForcedMoveSetup`, Docs/combat/SKILL_RUNE_DESIGN.md §5.2]
- 마법부여는 아이템 레벨 규칙과 연결되지 않아 결과가 T3 이하다. 아이템 레벨이 높은 장비의 옵션을 바꾸면 티어가 내려갈 수 있다. 아이템 레벨 연결 또는 재화별 Tier 규칙은 사용자가 따로 작업한다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §7, Docs/combat/WEAPON_PROGRESSION_DESIGN.md §적용 항과 Tier 접근 정책]
- 던전 몬스터 레벨이 1, 보스가 5라서 던전 드랍 장비는 T1~T3이다. 던전 드랍 레벨도 사용자가 따로 작업한다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §7, Assets/_BinaryData/TableData/DungeonTable.bytes]
- 아이템 레벨·옵션 티어의 플레이 확인(원거리 드랍 툴팁, 기본 아이템 툴팁에 줄 없음, 상점 매물의 마을 지역 레벨)은 남았다. 상점 매물·NPC 전리품은 같은 시드여도 이전과 다른 옵션이 나온다. [src: Docs/combat/ITEM_LEVEL_MOD_TIER_DESIGN.md §7·§8]
- MVP 런타임 구현과 별개로 `ItemEnchantmentCostTable.bytes`의 Google Sheet 등록·단건 다운로드 메뉴는 아직 `DownloadTables`에 없다. 비용 데이터를 시트 정본으로 운영하려면 실제 gid를 확정해 등록해야 한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §6.3, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs]
- `RerollAffixValues`, `RerollAllAffixes`, 분해, 장착 중 장비 마법부여, 무기 고유 제련은 지원하지 않는다. 현행 서비스는 랜덤 교체와 랜덤 추가만 승인한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §1.2·§14, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]
- `ItemEnchantmentDiagnostics`에는 비용·후보·Group·상한·실패 무변경·성공 소비·신규 마을 Forge 보장 진단이 구현돼 있다. 실행 결과를 저장한 아티팩트는 저장소에 없으므로 Unity Editor 진단과 Tier 1/2/3·저장 왕복·장착 후 전투의 PlayMode 체크는 구현 완료 후 회귀 검증 항목으로 남긴다. [src: Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12] [추론: 저장소에 진단 실행 결과 파일이 없음]
- CharacterUI는 슬롯 0 스킬의 치명타를 소수 둘째 자리까지 표시하지만, Spell의 공격속도 행을 숨기지는 않고 프로필 값 0을 표시한다. 주문 전용 UI 정책은 후속 작업이다. [src: Assets/Scripts/UI/CharacterUI.cs:L155-191, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L148-160]
- 기존 장비 옵션 글로벌화의 장착/해제/교체·세이브 왕복·Attack/Spell 태그·실제 타격과 툴팁 일치에 대한 전체 플레이 모드 체크리스트도 별도 실측이 필요하다. [src: Docs/combat/ModSystem_Changelog.md §UI와 검증 상태]
