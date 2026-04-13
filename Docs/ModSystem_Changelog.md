# Mod 시스템 리팩토링 변경사항

## 개요

장비 스탯 시스템을 기존 고정 테이블 기반(`EquipmentBaseStatTable`, `EquipmentStatTable`, `Stat`, `EquipmentStatData`)에서
**Mod 기반 시스템**(`ModTable`, `ModTierTable`, `ItemImplicitTable`, `ModInstance`)으로 전면 교체.

장비 옵션이 테이블에서 하드코딩된 스탯 리스트가 아니라, Mod 정의 → 티어별 값 범위 → 랜덤 롤링 구조로 동작하게 됨.

---

## 1. 제거된 구조

| 항목 | 파일 | 설명 |
|------|------|------|
| `EquipmentBaseStatTable` | Tables.cs | 장비 기본 고정 스탯 테이블 (List\<Stat\>) |
| `EquipmentStatTable` | Tables.cs | 장비 Prefix/Postfix 스탯 테이블 |
| `Stat` 클래스 | Tables.cs | `{Type, Value}` 구조체 |
| `EquipmentStatData` | ItemData.cs | `Prefix: List<Stat>`, `Postfix: List<Stat>` |
| `EquipmentData.BaseStats` | ItemData.cs | 기본 스탯 리스트 (저장용) |
| `EquipmentData.ComputedStats` | ItemData.cs | BaseStats + StatData 합산 결과 |
| `ComputeStats()` | ItemData.cs | ComputedStats 재계산 메서드 |
| `InitBaseStats()` | ItemData.cs | 테이블에서 BaseStats 생성 |
| `GetComputedStatValue()` | ItemData.cs | ComputedStats에서 특정 스탯 조회 |
| `GetEquipmentBaseStat()` | DataManager_Table.cs | EquipmentBaseStatTable 조회 API |
| `GetEquipmentStat()` | DataManager_Table.cs | EquipmentStatTable 조회 API |
| `ItemTable.EquipmentBaseStat` | Tables.cs | ItemTable의 LoadLate() 참조 연결 |
| `CreatePrefixOptions()` | ItemManager.cs | 랜덤 Enum 기반 Prefix 생성 |
| `CreatePostfixOptions()` | ItemManager.cs | 랜덤 Enum 기반 Postfix 생성 |

---

## 2. 추가된 구조

### 2.1 Enum (GlobalEnum.cs)

| Enum | 값 | 설명 |
|------|----|------|
| `ModEffectType` | FlatStat, AddedPhysDamage, AddedFireDamage, AddedIceDamage, AddedLightningDamage, AddedPoisonDamage, IncreasedStat, IncreasedDamage, DamageConversion, ResistPenetration, BleedOnHit, IgniteOnHit, FreezeOnHit, PoisonOnHit, LifeOnKill, ManaOnHit, LifeOnHit | Mod 효과 종류 |
| `ModApplyType` | Passive, OnCalculate, OnEvent | Mod 적용 시점 |
| `ModSlot` | Implicit, Prefix, Postfix | Mod 슬롯 종류 |

### 2.2 테이블 (Tables.cs)

| 테이블 | 필드 | 설명 |
|--------|------|------|
| `ModTable` | Id, Name, EffectType, ApplyType, Slot, Group, Element, Tags, TargetStat | Mod 정의 (모든 장비 옵션의 기본 정보) |
| `ModTierTable` | Id, ModId, Tier, Min1, Max1, Min2, Max2, RequiredLevel, Weight | Mod 티어별 값 범위 |
| `ItemImplicitTable` | Id, ItemId, ModId, Tier | 아이템별 고정 Implicit Mod 매핑 |
| `ModInstance` | ModTableId, Slot, Tier, Value1, Value2, Table(참조) | 실제 부여된 Mod 인스턴스 (저장 대상) |

### 2.3 컴포넌트 (ModPoolComponent.cs) - 신규 파일

| 구조체 | 설명 |
|--------|------|
| `ModPoolComponent` | 엔티티에 장착된 장비의 OnCalculate/OnEvent Mod 보관 (최대 32개, 고정 배열) |
| `ActiveMod` | ModPool 내 개별 Mod 데이터 (SourceItemInstanceId, ModTableId, EffectType, ApplyType, Element, Tags, Value1, Value2) |

### 2.4 DataManager API (DataManager_Table.cs)

| 메서드 | 설명 |
|--------|------|
| `GetMod(int id)` | ModTable 단건 조회 |
| `GetModTier(int modId, int tier)` | ModId + Tier 조합으로 ModTierTable 조회 |
| `GetModTiers(int modId)` | 특정 ModId의 모든 티어 목록 |
| `GetItemImplicits(int itemId)` | 특정 아이템의 Implicit Mod 목록 |
| `GetModPool(ModSlot slot)` | 특정 슬롯의 Mod 풀 (랜덤 롤링용) |

---

## 3. 변경된 핵심 로직

### 3.1 EquipmentData (ItemData.cs)

**Before**: `BaseStats`(List\<Stat\>) + `StatData`(Prefix/Postfix List\<Stat\>) → `ComputedStats` 재계산
**After**: `Mods`(List\<ModInstance\>) 단일 리스트, Slot 필드로 Implicit/Prefix/Postfix 구분

주요 메서드 변경:
- `GetPhysicsDamage()` → `GetDamageRange(ModEffectType.AddedPhysDamage)` 기반
- `IsPhysicsDamage()` / `IsFireDamage()` 등 → 각 원소별 `GetDamageRange()` 기반
- `GetCriticalRate()` → FlatStat + CriRate Mod 합산 (신규)
- `GetAttackSpeed()` → FlatStat + AttackSpeed Mod 합산 (Mod 기반으로 변경)
- `GetModValue(effectType)` → 특정 EffectType의 Value1 합산 (신규)
- `GetDamageRange(effectType)` → 특정 EffectType의 (Value1, Value2) 합산 (신규)
- `InitImplicitMods(itemId)` → ItemImplicitTable에서 Implicit Mod 생성 (신규)
- `OnLoadCompleted()` → ModInstance.Table 참조 연결

### 3.2 아이템 생성 (ItemManager.cs)

**Before**: `EquipmentBaseStatTable` 유무로 장비 판별 → `InitBaseStats()` + `CreatePrefix/PostfixOptions()` (랜덤 Enum 기반) → `ComputeStats()`
**After**: `ItemImplicitTable` 유무로 장비 판별 → `InitImplicitMods()` + `RollRandomMods()` (ModTable/ModTierTable 기반) → 각 Mod에 `OnLoadCompleted()` 호출

`RollRandomMods(equipment, slot, count)` 흐름:
1. `GetModPool(slot)`으로 해당 슬롯의 Mod 목록 조회
2. 랜덤 Mod 선택
3. `GetModTiers(modId)`로 티어 목록 → 랜덤 티어 선택
4. `Min1~Max1`, `Min2~Max2` 범위에서 값 롤링
5. `ModInstance`로 추가

### 3.3 장비 장착/해제 (EquipHelper.cs)

**Before**: `ComputedStats` 순회 → 일괄 `StatModifier.Add` 등록
**After**: `Mods` 순회 → `ApplyType`별 분기 처리:
- **Passive** → `ApplyPassiveMod()`: EffectType별 StatModifier 등록 (FlatStat, AddedPhysDamage, AddedFireDamage 등 각각 매핑)
- **OnCalculate / OnEvent** → `ApplyActiveModToPool()`: `ModPoolComponent`에 `ActiveMod`로 등록

해제 시: `StatModifierHelper.RemoveModifiersBySource()` + `ModPoolComponent.RemoveBySource()`

### 3.4 버프 틱 데미지 (System_BuffUpdate.cs)

- 속성 저항 적용 추가: `GetResistanceForDamageType()` → `DamageCalculator.GetResistanceReduction()` 사용
- `DamageType`이 `Physics` 하드코딩에서 `buff.DamageType` 동적 참조로 변경
- 에디터 전용 디버그 로그에 버프 이름 표시 추가

### 3.5 데미지 계산 (DamageCalculator.cs)

- `GetCritMultiplier()` 수정: `FinalCriDamage > 0 ? FinalCriDamage/100 : 1.5` → `1.5 + FinalCriDamage/100` (기본 1.5배 + 추가 배율)
- `GetResistanceReduction()` 접근제한자: `private` → `public` (System_BuffUpdate에서 참조)
- `EstimatedDamage` 계산: Max 값에서 `+1` 제거 (Max 데미지가 실제 Max값으로 표시)
- 출혈 발동: `BuffInstance.TickDamage` 설정 추가 (기존에 데미지 미적용 버그 수정)

### 3.6 UI 툴팁

- **TooltipEquipmentWeapon.cs**: `GetCriticalRate()` 호출 추가 + `%` 포맷 적용
- **UITooltipEquipment.cs**: `StatData.Prefix`/`Postfix` foreach → `Mods` for 루프, Implicit 제외, `ModTable.Name` 기반 표시

### 3.7 테이블 다운로드 (DownloadTables.cs)

- `EquipmentBaseStatTable`, `EquipmentStatTable` 다운로드 제거
- `ModTable`, `ModTierTable`, `ItemImplicitTable` 파싱 추가
- `ParseModTable()`: Enum 파싱, SkillTag 파싱 포함
- `ParseModTierTable()`: 9컬럼 int 파싱
- `ParseItemImplicitTable()`: 4컬럼 int 파싱

### 3.8 ComponentManager.cs

- `ModPoolComponent` 풀 등록 (크기 10)

---

## 4. 데이터 흐름 요약

```
[테이블 정의]
ModTable (Mod 정의) ← ModTierTable (티어별 값 범위)
                    ← ItemImplicitTable (아이템별 Implicit 매핑)

[아이템 생성]
ItemManager.CreateEquipmentData()
  ├─ InitImplicitMods() → ItemImplicitTable 조회 → ModInstance(Slot=Implicit) 생성
  ├─ RollRandomMods(Prefix) → ModTable(Slot=Prefix) 풀에서 랜덤 → ModInstance 생성
  ├─ RollRandomMods(Postfix) → ModTable(Slot=Postfix) 풀에서 랜덤 → ModInstance 생성
  └─ OnLoadCompleted() → 각 ModInstance.Table 참조 연결

[장착]
EquipHelper.ApplyEquipmentModifiers()
  ├─ Passive Mod → StatModifier 등록 (FlatStat, AddedDamage, IncreasedStat)
  └─ OnCalculate/OnEvent Mod → ModPoolComponent에 ActiveMod로 등록

[해제]
EquipHelper.RemoveEquipmentModifiers()
  ├─ StatModifierHelper.RemoveModifiersBySource()
  └─ ModPoolComponent.RemoveBySource()

[툴팁 표시]
UITooltipEquipment.SetEquipmentData()
  ├─ Implicit → TooltipEquipmentWeapon/Armor에서 표시 (물리피해, 공격속도, 치명타)
  └─ Prefix/Postfix → _textStat 리스트에 ModTable.Name + Value로 표시
```

---

## 5. 바이너리 데이터 파일 (신규)

| 파일 | 설명 |
|------|------|
| `ModTable.bytes` | Mod 정의 테이블 |
| `ModTierTable.bytes` | Mod 티어 테이블 |
| `ItemImplicitTable.bytes` | 아이템 Implicit 매핑 테이블 |

---

## 6. 버그 수정 (부수적)

| 항목 | 내용 |
|------|------|
| 출혈 데미지 미적용 | `BuffInstance.TickDamage`가 설정되지 않던 버그 → `DamageCalculator`에서 출혈 발동 시 `TickDamage` 설정 추가 |
| 버프 속성 저항 미적용 | 버프 틱 데미지에 속성 저항이 적용되지 않던 문제 → `GetResistanceForDamageType()` + `DamageCalculator.GetResistanceReduction()` 적용 |
| 치명타 배율 계산 오류 | `FinalCriDamage > 0 ? FinalCriDamage/100 : 1.5` → `1.5 + FinalCriDamage/100` (기본 배율 누락 수정) |
| 데미지 Max +1 오류 | `EstimatedDamage` 계산 시 `FinalAttackMax + 1` → `FinalAttackMax` (불필요한 +1 제거) |
| 아이템 생성 시 Table 참조 누락 | `CreateEquipmentData()`에서 `ModInstance.OnLoadCompleted()` 미호출 → 툴팁 데미지 0 표시 수정 |
