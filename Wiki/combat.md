---
title: 장비 옵션·마법부여와 무기 공격 프로필
sources:
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
  - Docs/combat/ITEM_ENCHANTMENT_DESIGN.md
  - Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md
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
last_synced: 2026-09-01
status: implemented-contract-active-validation-pending
---

# 장비 옵션·마법부여와 무기 공격 프로필

## 한 줄 요약

무기의 고정 기본 능력과 인스턴스별 고유 강화만 무기 캐시에 들어가며, 장비의 Implicit/Prefix/Postfix는 부위와 무관한 글로벌 Modifier로 합산된다. 미장착 인벤토리 장비의 Prefix/Postfix는 Tier 1 이상 제련소에서 랜덤 교체·추가할 수 있고, 스킬 공격 프로필은 변경된 옵션까지 태그에 맞춰 한 번 계산해 예상값과 실제 전투가 같은 값을 소비한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계·§스킬 공격 프로필 구성, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §0–§8, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs]

## 문서 정본

현행 설계 규칙은 `combatSystem.md`의 장비 옵션·공격 프로필 절에, 마이그레이션과 남은 과제는 `ModSystem_Changelog.md`의 2026-08-22 항목에 반영돼 있다. 변경 이력의 초기 Mod 리팩터링 절은 당시 구조를 기록한 역사이며, 실제 구현 여부는 현재 코드를 함께 확인한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계–§갱신·저장·표시, Docs/combat/ModSystem_Changelog.md §2026-08-22 — 장비 옵션 글로벌화와 무기 고유 강화 분리, Assets/Scripts/Data/ItemData.cs:L299-400]

일반 장비 마법부여의 작업·비용·후보·Forge 접근성 계약은 `ITEM_ENCHANTMENT_DESIGN.md`가 정본이며, 구현 여부는 `ItemEnchantmentService`, Forge UI, 비용·Mod 테이블을 함께 확인한다. 마법부여는 무기 고유 강화와 별도 경로다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes]

### `ITEM_ENCHANTMENT_DESIGN.md` 문서 수명주기

**보존 판정은 유지(active)**다. 구현이 끝났더라도 랜덤 선택 방식, 슬롯·Group·Tier 자격, 메모리 원자성의 범위, Forge 접근성, 비목표와 후속 확장의 설계 의도를 다른 원본 한 곳에서 모두 대체하지 못하므로 `_archive` 대상이 아니다. `combat.md`는 이 원본을 대체하지 않고 현재 코드와 대조한 짧은 진입점 역할을 한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §1·§3–§9·§14–§15, Docs/combat/combatSystem.md, Wiki/_SCHEMA.md] [추론: 현행 계약과 후속 확장 근거가 다른 원본으로 완전히 이관되기 전까지 active 유지]

| 문서 구간 | 현재 읽는 방식 |
| --- | --- |
| §0–§1, §3–§5, §6.1–§6.2, §6.4, §7–§9, §15 | 구현된 MVP의 행위·경계·완료 조건을 설명하는 현행 계약이다. 실제 구현 판정은 서비스·UI·테이블 코드와 함께 확인한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §0–§9·§15, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs] |
| §6.3 | 런타임 비용 조회는 구현됐지만, 신규 Google Sheet 등록·단건 다운로드 문장은 아직 운영 경로에 반영되지 않았다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §6.3, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs] |
| §12 | 구현 여부가 아니라 회귀 검증 기준이다. Editor 진단 코드와 PlayMode 체크리스트의 근거로 계속 사용한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs] |
| §14 | 신성한/카오스/진화의 오브, 무기 고유 제련, 장착 장비 작업, Forge 상위 Tier의 후속 설계 경계다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §14] |
| §2.2·§2.4 | 구현 전 기준선이다. 옛 Tier 게이트·로그 전용 UI와 일반 Affix 14개 스냅샷은 현행 상태가 아니다. 현재 UI는 서비스를 실행하고, 테이블은 Prefix 10개·Postfix 13개와 Tier 행 91개를 가진다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §2.2·§2.4, Assets/Scripts/UI/UIForge.cs, Assets/_BinaryData/TableData/ModTable.bytes, Assets/_BinaryData/TableData/ModTierTable.bytes] |
| §10–§11·§13 | 파일별 구현 계획·콘텐츠 보강안·Phase 순서로서 구현 이력으로만 읽는다. 현재 파일 위치와 완료 여부는 코드·테이블이 정본이다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §10–§11·§13, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Village/VillageInitialDevelopment.cs, Assets/_BinaryData/TableData/ModTable.bytes] |

따라서 MVP 런타임 구현은 완료로 분류하되, 문서 안의 모든 시제와 “현행 기준선”을 현재 상태로 읽지는 않는다. 문서를 제거하거나 전체를 구현 현황 대시보드로 사용하는 대신, 확정 규칙·검증 기준·후속 경계의 원본으로 유지한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs] [추론: 구현 완료와 원본 문서의 보존 가치는 별개]

## 책임 경계

| 구분 | 저장·정의 | 런타임 책임 |
| --- | --- | --- |
| 무기 기본 능력 | `EquipmentBaseStatTable`에서 생성된 `ModSlot.Base` | `EquipmentData.WeaponStats`(저장하지 않는 계산 캐시, 2026-10-03 D122)가 Base만 읽는다. 무기 Base는 글로벌 Modifier로 등록하지 않는다. [src: Assets/Scripts/Data/ItemData.cs:L299-400, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-137] |
| 무기 고유 강화 | `WeaponLocalEnhancementInstance` 목록. 피해 5속성·치명타·공격속도 증가율을 일반 `ModInstance`와 분리 저장 | 종류별 증가율을 합산한 뒤 해당 무기 Base에 한 번 적용한다. [src: Assets/Scripts/Common/GlobalEnum.cs:L487-506, Assets/Scripts/Data/ItemData.cs:L156-184, Assets/Scripts/Data/ItemData.cs:L299-400] |
| 글로벌 장비 옵션 | 모든 Implicit/Prefix/Postfix | 장착 부위와 무관하게 `StatModifier` 또는 `ModPoolComponent`로 등록한다. `Tags=None`은 모든 스킬, 태그가 있으면 그 태그를 모두 가진 스킬에만 적용한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-241, Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs:L42-128] |

`AllowedEquipTypes`는 옵션이 롤 가능한 장비 종류만 제한하며 계산 경로를 바꾸지 않는다. `Group` 중복은 같은 `ModSlot` 안에서만 금지한다. Base·Implicit 초기화와 Prefix·Postfix 랜덤 롤 모두 자기 슬롯의 기존 Group만 검사하므로 서로 다른 슬롯 사이의 같은 Group은 허용된다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Manager/ItemManager.cs]

## 일반 장비 마법부여 MVP

### 범위와 비용

- 대상은 플레이어 인벤토리의 수량 1인 미장착 장비다. Base·Implicit·`WeaponLocalEnhancements`는 바꾸지 않으며 Prefix/Postfix만 슬롯별 최대 3개까지 다룬다. 요청은 인벤토리 슬롯과 `ItemInstanceId`를 함께 재검증한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3·§7, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]
- `ReplaceAffix`는 교체 가능한 기존 Prefix/Postfix 하나를 균등 선택하고 같은 슬롯의 다른 유효 Mod로 교체한다. 비용은 변화의 오브 1개와 25 Gold다. `AddAffix`는 빈 Prefix/Postfix 전체 후보에서 Mod 하나를 균등 선택하며 엑잘티드 오브 1개와 40 Gold를 소비한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3.1·§3.4·§3.6, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes]
- 상점 기본 아이템(`EquipmentData.BasicItemId > 0`, 고정 옵션)도 대상이 될 수 있다. 커밋 트랜잭션은 옵션을 바꾸기 직전에 `BasicItemId`를 지워 일반 아이템으로 만들고, 이후 옵션 전체가 저장된다. NPC는 기본 아이템만 사므로 마법부여한 장비는 NPC 구매 후보에서 빠진다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §6.1·D122, Assets/Scripts/Item/ItemEnchantmentService.cs `ItemEnchantmentTransaction.TryCommit`]
- Basic·Standard·Premium은 MVP의 두 작업과 비용이 같다. 서비스는 실제 Forge Entity의 `FacilityTier`를 다시 읽고, Tier 0 구 배치만 ObjectSet으로 보완한다. UI나 요청이 넘긴 숫자만으로 생산 경로를 승인하지 않는다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §5, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]

### 후보와 트랜잭션

후보는 장비 종류 비트가 단일이고 `AllowedEquipTypes`에 포함되며, 실제 런타임 소비 경로가 허용 목록에 있는 Passive 효과여야 한다. 같은 Slot·Group 중복은 제외하고 구 세이브에 이미 중복이 있으면 실행을 거부한다. 선택된 Mod의 Tier는 `RequiredLevel <= Player Level`, `Tier <= Item Tier`, `Weight > 0`을 만족하는 행에서 Weight 가중치로 뽑고 값은 해당 범위에서 롤한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §3.2–§3.5, Assets/Scripts/Item/ItemEnchantmentService.cs]

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
- 장비 툴팁은 기본 능력, 무기 고유 강화, 장비 옵션을 별도 영역으로 표시하며 고정 치명타와 증가 치명타 문구를 구분한다. 옵션 문구는 Forge 비교 화면과 같은 포맷터를 사용한다. [src: Docs/combat/combatSystem.md §갱신·저장·표시, Assets/UI/Tooltip/Tooltip.uxml, Assets/Scripts/Manager/TooltipManager.cs, Assets/Scripts/Item/EquipmentOptionText.cs]
- 툴팁은 콘텐츠나 `display` 변경 직후 위치를 한 번 계산하고, 실제 크기가 확정되는 `GeometryChangedEvent`에서 보정해 화면 경계 배치를 갱신한다. [src: Assets/Scripts/Manager/TooltipManager.cs]
- `ModTable` 5002는 `IncreasedStat/AttackSpeedMul`, 1003은 `IncreasedStat/CriRateMul`로 글로벌 증가 버킷에 연결된다. [src: Assets/_BinaryData/TableData/ModTable.bytes:L1, Assets/Scripts/Common/System/System_StatCalculation.cs:L232-257]

## 연결된 것들

- 전체 구현 현황: [current_status.md](current_status.md)
- NPC 장비도 같은 ItemData 복구 경로와 무기 조회 경로의 영향을 받는다: [npc.md](npc.md) [src: Assets/Scripts/Npc/NpcSaveData.cs:L142-151, Assets/Scripts/Common/Utility/WeaponHelper.cs:L12-38]

## 미해결 / 주의

- MVP 런타임 구현과 별개로 `ItemEnchantmentCostTable.bytes`의 Google Sheet 등록·단건 다운로드 메뉴는 아직 `DownloadTables`에 없다. 비용 데이터를 시트 정본으로 운영하려면 실제 gid를 확정해 등록해야 한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §6.3, Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs]
- `RerollAffixValues`, `RerollAllAffixes`, 분해, 장착 중 장비 마법부여, 무기 고유 제련은 지원하지 않는다. 현행 서비스는 랜덤 교체와 랜덤 추가만 승인한다. [src: Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §1.2·§14, Assets/Scripts/Item/ItemEnchantmentService.cs, Assets/Scripts/UI/UIForge.cs]
- `ItemEnchantmentDiagnostics`에는 비용·후보·Group·상한·실패 무변경·성공 소비·신규 마을 Forge 보장 진단이 구현돼 있다. 실행 결과를 저장한 아티팩트는 저장소에 없으므로 Unity Editor 진단과 Tier 1/2/3·저장 왕복·장착 후 전투의 PlayMode 체크는 구현 완료 후 회귀 검증 항목으로 남긴다. [src: Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs, Docs/combat/ITEM_ENCHANTMENT_DESIGN.md §12] [추론: 저장소에 진단 실행 결과 파일이 없음]
- CharacterUI는 슬롯 0 스킬의 치명타를 소수 둘째 자리까지 표시하지만, Spell의 공격속도 행을 숨기지는 않고 프로필 값 0을 표시한다. 주문 전용 UI 정책은 후속 작업이다. [src: Assets/Scripts/UI/CharacterUI.cs:L155-191, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L148-160]
- 기존 장비 옵션 글로벌화의 장착/해제/교체·세이브 왕복·Attack/Spell 태그·실제 타격과 툴팁 일치에 대한 전체 플레이 모드 체크리스트도 별도 실측이 필요하다. [src: Docs/combat/ModSystem_Changelog.md §UI와 검증 상태]
