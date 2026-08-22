---
title: 장비 옵션 글로벌화와 무기 공격 프로필
sources:
  - Docs/combat/combatSystem.md
  - Docs/combat/ModSystem_Changelog.md
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
  - Assets/Scripts/Manager/ItemManager.cs
  - Assets/Scripts/Npc/NpcSaveData.cs
  - Assets/Scripts/UI/CharacterUI.cs
  - Assets/Scripts/Manager/TooltipManager.cs
  - Assets/UI/Tooltip/Tooltip.uxml
  - Assets/_BinaryData/TableData/ModTable.bytes
last_synced: 2026-08-22
status: verified
---

# 장비 옵션 글로벌화와 무기 공격 프로필

## 한 줄 요약

무기의 고정 기본 능력과 인스턴스별 고유 강화만 무기 캐시에 들어가며, 장비의 Implicit/Prefix/Postfix는 부위와 무관한 글로벌 Modifier로 합산된다. 스킬 공격 프로필이 태그에 맞는 피해·치명타·공격속도를 한 번 계산해 예상값과 실제 전투가 같은 값을 소비한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계·§스킬 공격 프로필 구성, Assets/Scripts/Data/ItemData.cs:L143-201, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L75-162]

## 문서 정본

현행 설계 규칙은 `combatSystem.md`의 장비 옵션·공격 프로필 절에, 마이그레이션과 남은 과제는 `ModSystem_Changelog.md`의 2026-08-22 항목에 반영돼 있다. 변경 이력의 초기 Mod 리팩터링 절은 당시 구조를 기록한 역사이며, 실제 구현 여부는 현재 코드를 함께 확인한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계–§갱신·저장·표시, Docs/combat/ModSystem_Changelog.md §2026-08-22 — 장비 옵션 글로벌화와 무기 고유 강화 분리, Assets/Scripts/Data/ItemData.cs:L299-400]

## 책임 경계

| 구분 | 저장·정의 | 런타임 책임 |
| --- | --- | --- |
| 무기 기본 능력 | `EquipmentBaseStatTable`에서 생성된 `ModSlot.Base` | `EquipmentData.WeaponStats`가 Base만 읽는다. 무기 Base는 글로벌 Modifier로 등록하지 않는다. [src: Assets/Scripts/Data/ItemData.cs:L299-400, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-137] |
| 무기 고유 강화 | `WeaponLocalEnhancementInstance` 목록. 피해 5속성·치명타·공격속도 증가율을 일반 `ModInstance`와 분리 저장 | 종류별 증가율을 합산한 뒤 해당 무기 Base에 한 번 적용한다. [src: Assets/Scripts/Common/GlobalEnum.cs:L487-506, Assets/Scripts/Data/ItemData.cs:L156-184, Assets/Scripts/Data/ItemData.cs:L299-400] |
| 글로벌 장비 옵션 | 모든 Implicit/Prefix/Postfix | 장착 부위와 무관하게 `StatModifier` 또는 `ModPoolComponent`로 등록한다. `Tags=None`은 모든 스킬, 태그가 있으면 그 태그를 모두 가진 스킬에만 적용한다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Common/Utility/EquipHelper.cs:L127-241, Assets/Scripts/Skill/Combat/SkillOffensiveStatResolver.cs:L42-128] |

`AllowedEquipTypes`는 옵션이 롤 가능한 장비 종류만 제한하며 계산 경로를 바꾸지 않는다. `Group`은 테이블상 “같은 그룹 중복 불가” 의미지만, 현행 `GetModPool`과 `RollRandomMods`는 아직 Group 중복을 검사하지 않는다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Assets/Scripts/Common/Tables.cs:L678-688, Assets/Scripts/Data/DataManager_Table.cs:L701-719, Assets/Scripts/Manager/ItemManager.cs:L566-600]

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
- 구 세이브는 `WeaponLocalEnhancements` null을 빈 목록으로 복구하고 무기 캐시를 무효화한다. 플레이어·NPC 인벤토리와 장비는 기존 ItemData 로드 경로를 사용하며 월드 드랍도 `WorldData.LoadCompleted`에서 복구한다. [src: Assets/Scripts/Data/ItemData.cs:L250-272, Assets/Scripts/Data/PlayerData.cs:L78-98, Assets/Scripts/Npc/NpcSaveData.cs:L142-151, Assets/Scripts/Data/WorldData.cs:L40-49]
- 장비 툴팁은 기본 능력, 무기 고유 강화, 장비 옵션을 별도 영역으로 표시하며 고정 치명타와 증가 치명타 문구를 구분한다. [src: Docs/combat/combatSystem.md §갱신·저장·표시, Assets/UI/Tooltip/Tooltip.uxml:L46-73, Assets/Scripts/Manager/TooltipManager.cs:L384-590]
- `ModTable` 5002는 `IncreasedStat/AttackSpeedMul`, 1003은 `IncreasedStat/CriRateMul`로 글로벌 증가 버킷에 연결된다. [src: Assets/_BinaryData/TableData/ModTable.bytes:L1, Assets/Scripts/Common/System/System_StatCalculation.cs:L232-257]

## 연결된 것들

- 전체 구현 현황: [current_status.md](current_status.md)
- NPC 장비도 같은 ItemData 복구 경로와 무기 조회 경로의 영향을 받는다: [npc.md](npc.md) [src: Assets/Scripts/Npc/NpcSaveData.cs:L142-151, Assets/Scripts/Common/Utility/WeaponHelper.cs:L12-38]

## 미해결 / 주의

- 강화 재료 테이블은 무기 종류 제한 없이 사용할 스키마만 존재한다. 실제 테이블 등록·다운로드·재료 데이터·후보 롤·상한·소비 트랜잭션·제련 UI는 아직 구현되지 않았다. [src: Docs/combat/combatSystem.md §장비 옵션 책임 경계, Docs/combat/ModSystem_Changelog.md §무기 고유 강화 준비 상태, Assets/Scripts/Common/Tables.cs:L379-395, Assets/Scripts/Data/DataManager_Table.cs, Assets/Scripts/Editor/DownloadTables.cs]
- CharacterUI는 슬롯 0 스킬의 치명타를 소수 둘째 자리까지 표시하지만, Spell의 공격속도 행을 숨기지는 않고 프로필 값 0을 표시한다. 주문 전용 UI 정책은 후속 작업이다. [src: Assets/Scripts/UI/CharacterUI.cs:L155-191, Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs:L148-160]
- 테이블 참조와 C# 컴파일은 정적 검증됐지만, 장착/해제/교체·세이브 왕복·Attack/Spell 태그·실제 타격과 툴팁 일치에 대한 전체 플레이 모드 체크리스트는 별도 실측이 필요하다. [src: Docs/combat/ModSystem_Changelog.md §UI와 검증 상태]
