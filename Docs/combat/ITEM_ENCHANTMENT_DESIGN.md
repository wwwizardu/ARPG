# 아이템 마법 부여 시스템 — 설계 문서

> 선행: [combatSystem.md](combatSystem.md) §장비 옵션 책임 경계 · [ModSystem_Changelog.md](ModSystem_Changelog.md) §2026-08-22/2026-08-29 · [VILLAGE_REWORK_PLAN.md](../village/VILLAGE_REWORK_PLAN.md) §V4/§V6 및 2026-08-30 후속 결정
>
> **상태 (2026-08-31)**: 랜덤 옵션 교체·추가 구현 반영. MVP 마법 부여는 Tier 1부터 모든 핵심 작업을 허용하고, 신규 게임의 모든 마을에 최소 Tier 1 제련소를 보장한다.
>
> **2026-10-21 갱신**: [ITEM_DROP_BALANCE_DESIGN.md](ITEM_DROP_BALANCE_DESIGN.md) 구현으로 장비 등급(일반·마법·희귀)·품질이 생겼다. 티어 자격은 아이템 레벨 규칙(§3.5), 진화의 오브(§14.3)·품질 다시 굴리기를 추가했고, 엑잘티드는 일반 장비에 쓸 수 없다. 작업·비용의 정본은 그 문서 §5·§6이다.
>
> 이 문서는 일반 장비의 `Prefix`/`Postfix` 하나를 무작위로 다른 효과로 교체하거나 새 효과 하나를 무작위로 추가하는 마법 부여 수직 슬라이스를 정의한다. 현행 코드와 문서가 충돌하면 코드를 진실로 본다.

---

## 0. 한 줄 요약

플레이어는 제련소에서 인벤토리 장비와 랜덤 교체 또는 랜덤 추가 작업을 선택한다. 전용 서비스가 유효 후보 중 옵션 종류·티어·수치를 실행 시 롤하고 필요한 재료·Gold를 자동 차감한다. 성공 직후 실제 롤 결과를 기준으로 전·후 옵션 비교를 표시하고 플레이어 데이터를 저장한다.

---

## 1. 목표와 비목표

### 1.1 목표

- 기존 `EquipmentData.Mods`와 `ModTable`/`ModTierTable`을 그대로 사용한다.
- 플레이어 입력을 **장비 선택 → 랜덤 교체/추가 실행**으로 제한한다.
- 플레이어는 결과 Mod나 교체 대상을 선택하지 않는다.
- 결과 Mod 종류는 균등 선택하고, 티어와 수치는 자격 범위 안에서 가중치·범위 롤한다.
- 옵션 변경과 옵션 추가를 재료·골드 소비가 포함된 하나의 인메모리 트랜잭션으로 처리한다.
- 성공 결과에 변경 전·후 옵션 스냅샷을 함께 반환해 비교 화면을 구성한다.
- UI는 후보 개수와 비용을 보여주되 실제 후보와 변경은 전용 서비스가 소유한다.
- 유효한 제련소라면 `FacilityTier` 1부터 옵션 교체와 신규 옵션 추가를 모두 허용한다.
- 종족·Profile·초기 Stage와 관계없이 신규 게임의 모든 마을에 Forge 160을 최소 `FacilityTier=1`로 배치한다.
- Tier 2/3은 MVP에서 기능·비용 차이를 두지 않고 후속 확장 지점으로 남긴다.
- 저장 후 재로드, 툴팁, 장착 후 스탯과 실제 전투 결과가 같은 옵션을 소비하게 한다.

### 1.2 비목표

- `ModSlot.Base` 변경
- `ModSlot.Implicit` 변경
- `WeaponLocalEnhancements`를 일반 마법 부여와 통합
- 장착 중인 장비를 첫 버전에서 직접 마법 부여
- 분해 기능 구현
- 아이템 희귀도 체계 도입
- 카오스/신성한 오브의 전체 기능을 첫 수직 슬라이스에 포함
- NPC 장비 마법 부여

분해 버튼은 현행 UI에 남기되 분해 트랜잭션은 별도 작업으로 유지한다. 현행 옵션 변경/추가 Tier 게이트는 새 마법 부여 흐름에 적용하지 않는다.

---

## 2. 현행 기준선

### 2.1 옵션 저장과 계산 경계

`EquipmentData.Mods`는 Base·Implicit·Prefix·Postfix의 모든 `ModInstance`를 저장한다. `ModInstance`는 `ModTableId`, `Slot`, `Tier`, `Value1`, `Value2`를 저장하고 로드 후 테이블 참조를 다시 연결한다.

| 옵션 구분 | 현행 책임 | 마법 부여 정책 |
|---|---|---|
| `Base` | `EquipmentBaseStatTable`에서 생성되는 아이템 기본 능력 | 변경 불가 |
| `Implicit` | 유니크/특수 아이템 고정 옵션 | 변경 불가 |
| `Prefix` | 글로벌 장비 옵션 | 변경·추가 가능 |
| `Postfix` | 글로벌 장비 옵션 | 변경·추가 가능 |
| `WeaponLocalEnhancements` | 해당 무기의 Base에만 적용되는 증가율 | 별도 무기 제련 시스템 |

일반 Prefix/Postfix는 무기에 붙더라도 글로벌 Modifier 경로를 사용한다. 마법 부여가 이를 무기 로컬 옵션으로 변환해서는 안 된다.

관련 코드:

- [ItemData.cs](../../Assets/Scripts/Data/ItemData.cs) — `EquipmentData`, `ModInstance` 복구, 슬롯별 Group 검사
- [EquipHelper.cs](../../Assets/Scripts/Common/Utility/EquipHelper.cs) — 장착 시 글로벌 Modifier 등록
- [System_SkillAttackProfileCalculation.cs](../../Assets/Scripts/Common/System/System_SkillAttackProfileCalculation.cs) — 전투용 공격 프로필 재계산
- [TooltipManager.cs](../../Assets/Scripts/Manager/TooltipManager.cs) — Base/Implicit/Prefix/Postfix 표시

### 2.2 현행 제련소 UI

`UIForge`는 다음 기반을 이미 가진다.

- 플레이어 인벤토리의 장비만 표시하는 선택 그리드
- `PlacedObjectComponent.FacilityTier` 기반 `Basic`/`Standard`/`Premium` 판정
- Tier 1 분해, Tier 2 Mod 재롤, Tier 3 Mod 추가와 비용 20% 할인 게이트
- UIDocument 재활성화 시 `rootVisualElement`를 다시 바인딩하는 생명주기 처리

하지만 `OnClickReroll`과 `OnClickAddMod`는 로그만 남기며 재료·옵션·저장 데이터를 변경하지 않는다.

위 Tier별 기능 차이는 현행 코드 기준선일 뿐이며 MVP 마법 부여 계약으로 승계하지 않는다. 새 흐름은 실제 Forge 여부와 Tier 1 이상인지만 검증한다.

관련 코드:

- [UIForge.cs](../../Assets/Scripts/UI/UIForge.cs)
- [Forge.uxml](../../Assets/UI/Forge/Forge.uxml)
- [Forge.uss](../../Assets/UI/Forge/Forge.uss)
- [ServiceUIRouter.cs](../../Assets/Scripts/Village/ServiceUIRouter.cs)

### 2.3 이미 존재하는 재료

`ItemTable.bytes`에는 다음 Currency 아이템이 존재한다.

| ItemId | 이름 | 첫 적용 범위 |
|---:|---|---|
| 1000 | 진화의 오브 | 2026-10-21: 일반 → 마법(옵션 1개 추가, 오브 1개 + 15 Gold). ITEM_DROP_BALANCE §6.1 |
| 1001 | 변화의 오브 | MVP: 기존 Prefix/Postfix 하나를 같은 슬롯의 다른 랜덤 옵션으로 교체할 때 소비 |
| 1002 | 엑잘티드 오브 | MVP: 새 Prefix/Postfix 하나를 랜덤으로 추가할 때 소비 |
| 1003 | 카오스 오브 | 후속: 전체 일반 옵션 재롤 |
| 1004 | 신성한 오브 | 후속: 현재 옵션별 “수치 재부여” 항목 실행 시 자동 소비 |

플레이어는 결과 옵션을 선택하지 않고 변화의 오브 교체 또는 엑잘티드 오브 추가 작업을 실행한다. 서비스는 대응하는 오브를 인벤토리에서 찾아 자동 차감한다.

현행 Currency 드랍은 `DropTable.CurrencyPoolMode=1`이며 `DropPoolBuilder`가 `ItemType.Currency`, `DropRate`, `DropLevel`로 자동 풀을 만든다. 위 오브들은 `DropLevel=1`, `DropRate>0`이므로 별도 `DropCurrencyTable` 명시 행에 의존하지 않고 풀 후보가 된다.

### 2.4 콘텐츠 준비 상태

2026-08-29 테이블 기준 현황은 다음과 같다.

- 일반 Prefix/Postfix Mod 총 14개: Prefix 7개, Postfix 7개
- Mod 1103 `추가 번개 데미지`, 1104 `추가 중독 데미지`는 `ModTierTable` 행이 없다.
- 무기는 일반 옵션 후보가 11개다.
- Shield/Helmet/Armor/Gloves/Belt는 후보가 각 2개뿐이다.
- Boots는 후보가 3개다.
- Necklace/Ring/Earring은 일반 옵션 후보가 0개다.
- 현행 `ItemManager.RollRandomMods`는 `ModTierTable.Weight`와 `RequiredLevel`을 사용하지 않고 Mod와 티어를 균등 선택한다. (2026-10-11 변경: 새 장비는 아이템 레벨 기준 구간·가중치로 티어를 고른다 — [ITEM_LEVEL_MOD_TIER_DESIGN.md](ITEM_LEVEL_MOD_TIER_DESIGN.md))

따라서 기능 코드만 연결하면 일부 장비에서 후보가 없거나 선택 폭이 지나치게 좁다. §11 콘텐츠 보강을 MVP 완료 조건에 포함한다.

---

## 3. 확정 규칙

### 3.1 변경 가능한 슬롯

- 마법 부여는 `Prefix`와 `Postfix`만 대상으로 한다.
- 변화의 오브는 교체 가능한 기존 옵션 하나를 균등하게 선택하고 같은 슬롯의 다른 `ModTable`로 교체한다.
- 엑잘티드 오브는 빈 자리가 있는 Prefix/Postfix의 유효 `ModTable` 후보 전체에서 하나를 균등하게 선택해 추가한다.
- 교체 결과는 기존 옵션과 다른 `ModTableId`여야 한다.
- 교체는 Prefix/Postfix 개수를 바꾸지 않으며, 추가는 선택된 슬롯의 수를 하나 늘린다.
- Prefix와 Postfix는 각각 최대 3개다.
- Base와 Implicit은 후보 목록과 요청 검증 모두에서 거부한다.

랜덤 교체는 기존 옵션의 슬롯을 유지한다. Prefix를 Postfix로 바꾸거나 그 반대로 바꾸지 않는다. 랜덤 추가는 Prefix가 가득 차면 Postfix만, Postfix가 가득 차면 Prefix만 후보로 사용한다.

### 3.2 Group 중복

같은 `ModSlot` 안에는 동일한 비어 있지 않은 `ModTable.Group`이 둘 이상 존재할 수 없다.

- 추가 후보는 같은 Slot·Group이 이미 있으면 제외한다.
- 교체 후보는 교체 대상 자신을 제외한 나머지 옵션과 Slot·Group이 겹치면 제외한다.
- 현행 중복 방지 규칙상 같은 Slot·Group은 최대 하나여야 하며, 구 세이브에 둘 이상 있으면 랜덤 작업을 실행하지 않고 데이터 오류로 거부한다.
- 서로 다른 슬롯의 동일 Group은 허용한다.

예를 들어 변화의 오브가 Prefix 추가 화염 피해를 교체 대상으로 뽑으면, 장비에 적용 가능하고 다른 Prefix와 Group이 겹치지 않는 다른 Prefix 중 하나를 결과로 뽑는다.

### 3.3 장비 종류 자격

`EquipmentData.EquipType`은 단일 비트여야 한다. 후보 Mod의 `AllowedEquipTypes`가 그 비트를 포함할 때만 선택할 수 있다.

`AllowedEquipTypes`는 롤 가능 여부만 제한하며 계산 경로를 바꾸지 않는다.

### 3.4 랜덤 후보와 결과 롤

UI는 결과 `ModTable`을 선택하거나 미리 표시하지 않는다. 실제 옵션 종류, `ModTierTable`, `Value1`/`Value2`는 실행 시 한 번만 롤한다.

- 실행 전에는 작업 설명, 유효 후보 개수와 자동 소비 비용만 표시한다.
- 실행 전 조회는 난수를 소비하거나 확정 결과를 만들지 않는다.
- 변화의 오브는 교체 가능한 기존 옵션을 먼저 균등 롤하고 그 옵션과 같은 슬롯의 다른 유효 Mod를 균등 롤한다.
- 엑잘티드 오브는 추가 가능한 Prefix/Postfix Mod 전체를 합친 후보에서 Mod 하나를 균등 롤한다. 슬롯을 먼저 50:50으로 뽑지 않는다.
- 선택된 Mod의 티어는 `Weight` 가중치로, 수치는 해당 티어 범위에서 롤한다.
- 서비스는 롤 직전에 변경 전 전체 옵션 스냅샷을 만든다.
- 커밋 후 변경 후 전체 옵션 스냅샷을 만들고 저장을 요청한다.
- UI는 성공 결과를 받아 전·후 옵션을 나란히 비교한다.

### 3.5 티어 자격

선택한 Mod의 티어 중 다음 조건을 모두 만족하는 행만 결과 후보가 된다.

```text
ModTier.RequiredLevel <= PlayerData.Level
ModTier.Tier <= TargetItem.Table.Tier
ModTier.Weight > 0
```

후보 티어는 `Weight` 가중치로 하나를 선택한다. `Value1`과 `Value2`는 각 후보 티어의 최소·최대 범위에서 양 끝을 포함해 롤한다.

자격 티어가 하나도 없는 Mod는 UI 후보에서 제외하고 서비스 요청도 거부한다.

이 규칙은 첫 버전에서 새 `ItemLevel` 저장 필드를 추가하지 않기 위한 최소 정책이다. 실제 드랍 레벨별 세밀한 아이템 레벨이 필요해지면 `ItemData.ItemLevel` 도입을 별도 설계한다.

2026-10-11 갱신: `EquipmentData.ItemLevel`이 도입됐고 옵션은 T12까지 늘었다([ITEM_LEVEL_MOD_TIER_DESIGN.md](ITEM_LEVEL_MOD_TIER_DESIGN.md)). `ModTier.RequiredLevel`은 이제 "티어가 열리는 아이템 레벨"(T1~T3 = 1, T4 = 11 … T10~T12 = 90)이다. 마법부여 규칙은 위 세 조건 그대로 두었으므로(사용자 결정, 마법부여는 별도 작업) 플레이어 레벨 1 기준으로 T1~T3 중 장비 Tier 이하가 나온다. 아이템 레벨이 높은 장비의 옵션을 바꾸면 T3 이하로 내려갈 수 있다. T11·T12는 요구 레벨 90이라 마법부여에서 나오지 않는다.

**2026-10-21 갱신 (ITEM_DROP_BALANCE D9, 구현)**: 위 세 조건을 버리고 새 장비 생성과 같은 아이템 레벨 규칙(`ItemLevelModTierRules.BuildNormalTierCandidates`)을 쓴다. 아이템 레벨 0(옛·기본·시작 장비)은 1로 본다. 결과가 마법 상태인 작업(진화, 마법 장비의 변화, 접두 또는 접미 하나뿐인 마법 장비의 엑잘티드)은 마법 규칙(후보 중 위 2단계 가중치 2배)을 쓴다. T11·T12는 여전히 마법부여에서 나오지 않는다. 플레이어 레벨과 장비 Tier는 더 이상 보지 않는다. [src: Assets/Scripts/Item/ItemEnchantmentService.cs `ItemEnchantmentRules.BuildEligibleTiers`·`ResolveAffixTargets`]

### 3.6 재료와 골드

- 플레이어는 재료를 직접 선택하지 않는다.
- 서비스가 판정한 작업이 교체면 변화의 오브, 추가면 엑잘티드 오브 비용 규칙을 사용한다.
- UI는 각 랜덤 작업 아래에 필요 재료·보유량·Gold를 표시한다.
- 재료 수량은 인벤토리 전체에서 `ItemId` 기준으로 합산한다.
- 소비는 낮은 슬롯 인덱스부터 결정론적으로 진행한다.
- 재료가 여러 스택으로 나뉘어 있어도 하나의 비용 규칙 수량으로 사용할 수 있다.
- MVP에서는 Forge Tier에 따른 재료·Gold 할인을 적용하지 않는다.
- Tier 1/2/3은 같은 작업에 같은 재료 개수와 Gold를 지불한다.
- 옵션 교체는 변화의 오브 1개와 25 Gold를 소비한다.
- 신규 옵션 추가는 엑잘티드 오브 1개와 40 Gold를 소비한다.
- 25/40 Gold는 현행 `UIForge` 임시값 50/80 Gold의 절반으로 확정한 MVP 시작값이다.
- 상위 Tier 할인이나 전용 비용 규칙은 Tier 2/3 확장 설계에서 별도로 결정한다.

### 3.7 대상 범위

MVP는 플레이어 인벤토리에 있는 장비만 허용한다.

서비스는 UI가 넘긴 슬롯 인덱스만 신뢰하지 않고 `ItemInstanceId`까지 다시 비교한다. 장착 장비는 인벤토리 목록에 없으므로 요청할 수 없다.

장착 장비 지원은 Modifier 제거→옵션 변경→Modifier 재등록→Stat Dirty→SkillAttackProfile 재계산이 필요하므로 후속으로 분리한다.

---

## 4. 플레이어 흐름

```text
제련소 상호작용
  → 인벤토리 장비 선택
  → [랜덤 옵션 변경] 또는 [랜덤 옵션 추가] 비용·후보 확인
  → 작업 버튼 클릭
  → 변경: 기존 옵션 하나와 같은 슬롯의 다른 옵션 롤
  → 추가: 빈 슬롯에 적용 가능한 옵션 하나 롤
  → 실행 직전 변경 전 옵션 스냅샷 생성
  → 실제 옵션 종류·티어·수치 롤
  → 옵션 적용 + 재료·Gold 자동 차감
  → 변경 후 옵션 스냅샷 생성
  → Player 저장 요청
  → 성공 화면에서 변경 전·후 옵션 비교
```

선택 순서가 바뀌어 이전 선택이 무효가 되면 다음 단계 상태를 초기화한다.

| 변경된 선택 | 반드시 초기화할 상태 |
|---|---|
| 대상 장비 | 두 작업의 후보·비용 계산, 이전 성공 비교 결과 |
| 작업 실행 | 이전 성공 비교 결과 |

전·후 비교는 실행 전에 결과를 확정해 보여주는 미리보기가 아니다. 실제 마법 부여가 성공한 뒤 서비스가 반환한 두 스냅샷을 표시한다.

---

## 5. 제련소 Tier 계약

MVP는 접근성을 우선해 현행 V4의 옵션 변경/추가 게이트를 완화한다.

| Forge Tier | 기능 | MVP 마법 부여 |
|---|---|---|
| `None` | 사용 불가 | 모든 작업 비활성 |
| `Basic` / Tier 1 | 분해 + 마법 부여 | 옵션 교체·신규 추가 모두 가능 |
| `Standard` / Tier 2 | Tier 1과 동일 | MVP 기능·비용 차이 없음. 후속 확장 예약 |
| `Premium` / Tier 3 | Tier 1과 동일 | MVP 기능·비용 차이 없음. 후속 확장 예약 |

Tier 1은 MVP의 전체 핵심 루프를 제공한다. Tier 2/3을 방문해도 적용 가능 후보, 결과 티어, 재료 수량, Gold 비용은 달라지지 않으며 Premium 할인도 적용하지 않는다. 후속 상위 Tier 기능은 Tier 1의 옵션 교체·추가 기능을 회수하지 않는 가산형 확장으로 설계한다.

### 5.1 마을별 접근성 계약

- 신규 게임에서 생성되는 모든 `VillageData`는 Race·Profile·`InitialStage`와 관계없이 Forge 160을 정확히 하나 가진다.
- 최소 보장 Tier는 1이다. 기존 누적 성장 마일스톤이 같은 Forge를 Tier 2/3으로 올리면 더 높은 값을 유지하며 Tier 1로 낮추지 않는다.
- Forge는 더 이상 Dwarf 전용 접근성 게이트가 아니다. Dwarf의 상권·배치 정체성과 후속 상위 Tier 확장 가능성은 별도로 유지한다.
- 최소 Forge 보장은 초기 완공 원장에 멱등 적용한다. 동일 TableId/업그레이드 그룹의 Forge가 이미 있으면 중복 생성하지 않는다.
- 기존 세이브 전체 backfill은 현행 마을 개편의 New Game 중심 방침에 따라 MVP 범위에서 제외한다.

현행 `UIForge.ForgeTier`는 UI 내부 enum이다. 서비스가 UI 타입에 의존하지 않도록 `GlobalEnum.ForgeTier`로 이동하고 UI와 서비스가 함께 사용한다.

서비스 요청은 생산 환경에서 `ForgeTier` 숫자를 직접 신뢰하지 않는다. `ForgeEntityId`의 `PlacedObjectComponent.FacilityTier`와 구 세이브 ObjectSet fallback으로 유효 Tier를 다시 계산하고 Tier 1 이상인지 확인한다. `BindForTest`의 강제 Tier는 에디터/테스트 경로에만 둔다.

---

## 6. 데이터 계약

### 6.1 신규 enum

`GlobalEnum.cs`에 다음을 추가한다.

```csharp
public enum ForgeTier
{
    None,
    Basic,
    Standard,
    Premium,
}

public enum ItemEnchantmentOperation
{
    ReplaceAffix,
    AddAffix,
    RerollAffixValues,
    RerollAllAffixes,
}
```

MVP UI는 앞의 두 작업 중 하나를 명시적으로 요청한다. 서비스가 결과 Mod와 교체 대상을 랜덤으로 결정하며, 나머지는 비용 테이블에 행을 넣기 전까지 `UnsupportedOperation`으로 거부한다.

### 6.2 신규 테이블

`Tables.cs`에 `ItemEnchantmentCostTable`을 추가한다. 이 테이블은 랜덤 작업 종류를 재료·Gold 비용으로 변환하는 규칙이다.

| 필드 | 타입 | 의미 |
|---|---|---|
| `Id` | `int` | 비용 규칙 ID |
| `MaterialItemId` | `int` | 소비할 `ItemTable.Id` |
| `Operation` | `ItemEnchantmentOperation` | 요청한 변경/추가/후속 작업 종류 |
| `MaterialCount` | `int` | 기본 재료 소비량 |
| `GoldCost` | `int` | 기본 Gold 비용 |
| `RequiredForgeTier` | `ForgeTier` | 최소 제련소 Tier |

`LoadLate()`에서 `MaterialItemId`가 실제 `ItemTable`의 Currency인지 확인한 참조를 연결한다. 잘못된 행은 로드 진단에서 오류로 보고하고 UI 후보에서 제외한다.

MVP 비용은 다음 값으로 시작한다. 수치는 코드 상수로 두지 않고 테이블에서 조정한다.

| CostId | MaterialItemId | Operation | MaterialCount | GoldCost | RequiredForgeTier |
|---:|---:|---|---:|---:|---|
| 1 | 1001 | `ReplaceAffix` | 1 | 25 | `Basic` |
| 2 | 1002 | `AddAffix` | 1 | 40 | `Basic` |

MVP 비용 행의 `RequiredForgeTier`는 모두 `Basic`으로 둔다. 필드는 후속 Tier 2/3 전용 작업을 데이터로 추가할 수 있도록 확장 지점으로 유지한다.

### 6.3 테이블 로더

`DataManager_Table`은 다음 조회를 제공한다.

```text
GetItemEnchantmentCost(operation)
```

UI가 랜덤 교체/추가 작업을 조회하면 서비스는 `GetItemEnchantmentCost(operation)`으로 필요 재료와 Gold를 계산한다. 결과 `ModTable` 후보는 서비스 내부에서만 구성한다.

`DownloadTables`에는 신규 시트 등록, 단건 다운로드 메뉴, enum 이름 파서를 추가한다. Google Sheet gid는 실제 시트 생성 후 등록하며 문서에서 임의 값을 만들지 않는다.

### 6.4 저장 데이터

MVP는 새 영속 필드를 요구하지 않는다.

- 결과 옵션: 기존 `EquipmentData.Mods`
- 결과 수치: 기존 `ModInstance.Tier/Value1/Value2`
- 재료: 기존 플레이어 인벤토리 `ItemData.Quantity`
- Gold: 기존 `PlayerData.Gold`

새 `ModInstance`는 추가 직후 `OnLoadCompleted()`로 `ModTable` 참조를 연결한다. 저장 후 재로드에서도 기존 `ItemData.OnLoadCompleted()` 경로가 같은 참조를 복구한다.

---

## 7. 런타임 요청과 결과

### 7.1 요청

UI는 가변 객체 참조 대신 식별자를 요청에 담는다.

```text
ForgeEntityId
TargetInventorySlot
TargetItemInstanceId
Operation
```

서비스는 `Operation`에 따라 교체 대상과 결과 Mod 후보를 다시 구성한다. UI는 결과 ModId, 교체 대상 인덱스나 재료 ID를 요청에 담지 않는다.

### 7.2 실행 가능 상태와 비용 조회

각 랜덤 작업에 대한 서비스의 읽기 전용 조회는 다음을 제공한다.

```text
CanApply
FailureReason
ResolvedOperation
MaterialItemId / RequiredCount / OwnedCount
BaseGoldCost / FinalGoldCost / OwnedGold
PrefixCount / PostfixCount / SlotLimit
ReplaceableAffixCount / RandomCandidateCount
```

이 조회는 난수를 사용하지 않는다. 실행 전에 확정 결과 비교를 만들지 않으며 버튼 활성화와 비용 안내에만 사용한다.

### 7.3 실행 결과

성공 결과는 실제 생성된 `ModInstance`, 요청 작업, 차감 수량, 차감 Gold와 변경 전·후 옵션 스냅샷을 반환한다. 교체 결과에는 실제로 제거된 옵션 스냅샷도 포함한다.

```text
ResolvedOperation
ReplacedMod              // 랜덤 교체일 때만
AppliedMod
ConsumedMaterialItemId / ConsumedMaterialCount
ConsumedGold
BeforeOptions[]
AfterOptions[]
```

`BeforeOptions`와 `AfterOptions`는 `ModInstance` 객체 참조가 아니라 ModTableId·Slot·Tier·Value1·Value2·표시 문구를 복사한 불변 스냅샷이다.

대표 실패 코드는 다음과 같다.

- `ForgeNotFound`
- `ForgeTierInsufficient`
- `TargetNotFound`
- `TargetChanged`
- `NotEquipment`
- `ImmutableSlot`
- `AmbiguousExistingGroup`
- `AffixLimitReached`
- `NoAffixAvailable`
- `NoRandomCandidate`
- `MaterialMissing`
- `GoldInsufficient`
- `IncompatibleMod`
- `DuplicateGroup`
- `TierUnavailable`
- `UnsupportedOperation`

UI는 문자열 규칙을 직접 판단하지 않고 실패 코드를 한국어 상태 문구로 매핑한다.

---

## 8. 전용 서비스와 트랜잭션

### 8.1 책임 위치

신규 `Assets/Scripts/Item/ItemEnchantmentService.cs`가 다음을 전담한다.

- 제련소 Tier 해석
- 보유 재료 집계
- 랜덤 교체 대상과 같은 슬롯의 다른 `ModTable` 후보 필터
- 빈 Prefix/Postfix의 랜덤 추가 `ModTable` 후보 필터
- 작업별 비용 규칙과 자동 소비 재료 해석
- 옵션 종류·티어·수치 결과 롤
- 변경 전·후 옵션 스냅샷 생성
- 실행 전 전체 검증
- 옵션·재료·Gold 변경
- 저장 요청

`ItemManager`가 서비스 인스턴스를 소유하고 `AR.s.Item.Enchantment`로 노출한다. 서비스는 UI 상태를 보관하지 않는다.

이 작업은 플레이어 입력에 한 번 반응하는 이벤트성 도메인 작업이므로 새 ECS `System_*`, Component, ComponentManager pool, SystemManager priority 등록은 필요하지 않다.

### 8.2 실행 순서

실행은 모든 실패 가능한 조건을 먼저 검증한 뒤 커밋한다.

```text
1. ForgeEntityId와 실제 ForgeTier 확인
2. 인벤토리 슬롯과 ItemInstanceId 재확인
3. EquipmentData와 EquipType 확인
4. 요청 Operation과 ItemEnchantmentCost 확인
5. 기존 Prefix/Postfix의 Slot·Group 중복과 슬롯 상한 확인
6. 교체면 교체 가능한 기존 옵션과 같은 슬롯의 다른 Mod 후보 구성
7. 추가면 빈 슬롯에 추가 가능한 Prefix/Postfix Mod 후보 구성
8. 각 Mod의 자격 ModTier 후보와 전체 Weight 확인
9. 인벤토리 전체 자동 소비 재료 수량 확인
10. Gold 확인
11. 변경 전 옵션 불변 스냅샷 생성
12. 실제 교체 대상·Mod 종류·Tier·Value 롤
13. 새 ModInstance 완성 및 Table 참조 연결
14. 기존 랜덤 대상 교체 또는 새 랜덤 Mod 추가
15. 재료 자동 소비
16. Gold 소비
17. EquipmentData 캐시 무효화
18. 변경 후 옵션 불변 스냅샷 생성
19. SavePlayer() 한 번 호출
20. 전·후 스냅샷을 포함한 성공 결과 반환
```

Unity 메인 스레드에서 1회 실행하므로 검증 이후 동시 변경은 발생하지 않는 것을 전제로 한다. 그래도 대상과 재료는 커밋 직전에 한 번 더 조회해 UI stale 상태를 막는다.

### 8.3 인메모리 원자성

- 검증 실패 시 어떤 데이터도 변경하지 않는다.
- 결과 Mod는 리스트에 넣기 전에 완성한다.
- 재료 소비는 사전 합산 성공 후 낮은 슬롯부터 수행한다.
- 성공 경로에는 중간 `SavePlayer()`를 두지 않는다.
- UI는 직접 `Mods`, `Quantity`, `Gold`를 수정하지 않는다.

현행 `SavePlayer()`는 비동기 저장 요청이며 디스크 쓰기 실패까지 되돌리는 내구성 트랜잭션은 제공하지 않는다. MVP의 원자성 범위는 게임 메모리 상태다. 저장 장애 복구 정책은 파티션 세이브 시스템의 기존 책임을 따른다.

### 8.4 인벤토리 지원 API

`Inventory`에 다음 집중 API를 추가한다.

```text
GetTotalQuantity(itemId)
CanConsumeItem(itemId, quantity)
TryConsumeItem(itemId, quantity)
```

`TryConsumeItem`은 전체 수량을 먼저 확인한 뒤 낮은 슬롯부터 소비한다. 마법 부여 서비스가 `Inventory.Items`를 직접 순회하며 여러 곳에서 수량을 수정하지 않게 한다.

---

## 9. UI 설계

### 9.1 레이아웃

현행 640px 단일 패널을 장비와 두 랜덤 작업을 보여주는 2열 패널로 확장한다. 결과 옵션과 재료는 선택하지 않는다.

```text
┌───────────────────────────────────────────────────────────────┐
│ 제련소 Tier / Tier 1 이상 전체 기능 사용 가능 / 닫기          │
├───────────────────────────┬───────────────────────────────────┤
│ 장비 인벤토리             │ 마법 부여 작업                    │
│ [장비 그리드]             │ 변화: 랜덤 옵션 변경             │
│                           │ 엑잘티드: 랜덤 옵션 추가         │
│ 선택 장비와 현재 옵션     │ 후보 개수 / 비용 / 실행 버튼     │
├───────────────────────────┴───────────────────────────────────┤
│ 결과 옵션은 실행 후 공개                                      │
├───────────────────────────────────────────────────────────────┤
│ 성공 후 결과: 변경 전 옵션 │ 변경 후 옵션                    │
└───────────────────────────────────────────────────────────────┘
```

장비 목록에는 `ScrollView`를 사용하고 패널의 최대 높이를 제한한다. 성공 비교 영역은 다음 장비를 선택하거나 새 작업을 실행할 때 닫는다.

### 9.2 선택과 표시

- 장비 그리드는 현행과 같이 인벤토리 장비만 활성화한다.
- 선택 장비의 Base·Implicit·Prefix·Postfix는 현재 상태 확인용으로만 표시한다.
- 플레이어는 기존 옵션, 결과 옵션이나 재료를 클릭하지 않는다.
- 변화/엑잘티드 작업 카드는 설명, 후보 개수, 재료 이름·보유/필요 수량·Gold를 표시한다.
- 후보가 없으면 작업 버튼을 비활성화하고 원인을 표시한다.
- 실행 후 실제 결과 수치를 강조하고 변경 전·후 전체 옵션을 나란히 표시한다.
- 비교 화면을 만든 뒤 인벤토리 재료 수량과 현재 장비 옵션을 다시 빌드한다.

### 9.3 실행 버튼 활성 조건

다음을 모두 만족할 때만 활성화한다.

```text
유효 Forge
유효 대상 장비
지원되는 랜덤 교체/추가 Operation
유효 랜덤 후보 존재
유효 비용 규칙
재료 충분
Gold 충분
서비스 조회 CanApply == true
```

버튼 활성 상태와 관계없이 클릭 시 서비스가 전체 조건을 다시 검사한다.

### 9.4 옵션 문구 공유

`TooltipManager.FormatModLine`의 현행 로직은 private이다. Forge가 같은 포맷을 복제하지 않도록 이를 `Assets/Scripts/Item/EquipmentOptionText.cs`의 순수 포맷 책임으로 이동한다.

- TooltipManager와 UIForge가 같은 포맷 함수를 사용한다.
- 고정 치명타와 증가 치명타를 구분한다.
- 공격속도·피해 범위·퍼센트 표기가 두 UI에서 일치한다.

### 9.5 UIDocument 생명주기

현행 `EnsureBound()`의 root 변경 감지는 유지한다. 새 버튼과 리스트 콜백도 root가 바뀔 때만 연결하고 같은 root에 중복 연결하지 않는다.

닫기/재열기 후에는 장비·마법 부여 선택과 성공 비교 결과를 초기화하고 인벤토리·Forge Tier를 다시 조회한다.

---

## 10. 파일별 구현 계획

| 단계 | 파일 | 변경 |
|---|---|---|
| 도메인 enum | `Assets/Scripts/Common/GlobalEnum.cs` | `ForgeTier`, `ItemEnchantmentOperation` 추가. UI 내부 ForgeTier 제거 |
| 테이블 스키마 | `Assets/Scripts/Common/Tables.cs` | `ItemEnchantmentCostTable` 추가 |
| 테이블 로드 | `Assets/Scripts/Data/DataManager_Table.cs` | 로드·LoadLate·조회 API 추가 |
| 테이블 다운로드 | `Assets/Scripts/Editor/DownloadTables.cs` | 신규 시트 등록·파서·메뉴 추가 |
| 콘텐츠 | `Assets/_BinaryData/TableData/ItemEnchantmentCostTable.bytes` | 자동 교체/추가용 변화·엑잘티드 비용 행 |
| 인벤토리 소비 | `Assets/Scripts/Base/Inventory.cs` | ItemId 기준 합산·원자 소비 API |
| 도메인 서비스 | `Assets/Scripts/Item/ItemEnchantmentService.cs` | 후보·자동 작업/비용 판정·검증·롤·스냅샷·커밋 |
| 서비스 소유 | `Assets/Scripts/Manager/ItemManager.cs` | 서비스 초기화와 접근점 |
| 공통 문구 | `Assets/Scripts/Item/EquipmentOptionText.cs` | Mod 표시 포맷 단일화 |
| 툴팁 | `Assets/Scripts/Manager/TooltipManager.cs` | 공통 포맷 사용 |
| UI 로직 | `Assets/Scripts/UI/UIForge.cs` | 장비/마법 부여 선택·자동 비용·실행·전후 비교·갱신 |
| UI 레이아웃 | `Assets/UI/Forge/Forge.uxml` | 2열 선택 + 성공 전후 비교 구조 |
| UI 스타일 | `Assets/UI/Forge/Forge.uss` | 스크롤·선택·비활성·결과 스타일 |
| 마을 접근성 | `Assets/Scripts/Village/VillageGrowthPlan.cs`, `VillageProfile.cs`, `VillageLayoutProfile.cs` | 모든 신규 게임 마을에 Forge 160 Tier 1 최소 보장·중복 없는 배치 슬롯 확보 |
| 데이터 보강 | `ModTable.bytes`, `ModTierTable.bytes` | 누락 티어와 부위별 후보 추가 |
| 진단 | `Assets/Scripts/Editor/ItemEnchantmentDiagnostics.cs` | 규칙·테이블·트랜잭션 정적 진단 |

관련 `.meta` 파일은 Unity가 생성한 값을 사용한다. 수동으로 중복 GUID를 만들지 않는다.

---

## 11. 콘텐츠 보강 최소안

기능 수직 슬라이스가 모든 주요 부위에서 동작하려면 다음을 먼저 채운다.

### 11.1 누락 티어

- Mod 1103 `추가 번개 데미지`
- Mod 1104 `추가 중독 데미지`

기존 추가 물리·화염·냉기 피해 티어 곡선을 기준으로 값 범위와 RequiredLevel/Weight를 맞춘다.

### 11.2 부위별 최소 후보

첫 콘텐츠 목표는 주요 장비 종류마다 Prefix 2개 이상, Postfix 2개 이상이다.

| 부위 | 최소 후보 방향 |
|---|---|
| Weapon | 기존 피해·공속·치명타·상태이상 후보 활용 |
| Helmet/Armor/Gloves/Belt/Shield | 체력·방어 + 저항/자원/회복 계열 중 현행 전투 소비 경로가 있는 것만 추가 |
| Boots | 체력·방어 + 이동속도 유지 |
| Necklace/Ring/Earring | 생명력·치명타·공속·스킬 태그 옵션 중 실제 소비 경로가 검증된 것부터 추가 |

테이블에 이름만 추가하고 전투 소비 코드가 없는 효과를 UI 후보로 노출해서는 안 된다.

### 11.3 밸런스 데이터

다음 값은 플레이 테스트로 조정하되 코드 변경 없이 테이블에서 바꿀 수 있어야 한다.

- 작업별 재료 개수
- 작업별 GoldCost
- ModTier Weight
- 부위별 후보 수
- 오브 DropRate
- 상점 판매 여부와 가격

---

## 12. 검증 계획

### 12.1 정적·Editor 진단

`ItemEnchantmentDiagnostics`는 실제 bytes 테이블을 읽어 다음을 검증한다.

- 모든 비용 규칙의 MaterialItemId가 존재하고 Currency다.
- 모든 MVP 비용 규칙의 `RequiredForgeTier`가 `Basic`이고 Operation 조합이 유효하다.
- `ReplaceAffix` 비용이 변화의 오브 1개 + 25 Gold, `AddAffix` 비용이 엑잘티드 오브 1개 + 40 Gold다.
- 모든 신규 게임 Village Profile·초기 Stage 결과에 Forge 160이 정확히 하나 있고 `FacilityTier >= 1`이다.
- 기존 Tier 2/3 Forge에 최소 보장 규칙을 재적용해도 Tier가 낮아지거나 Forge가 중복되지 않는다.
- 모든 일반 Mod가 단일 유효 Slot을 가진다.
- 모든 노출 후보에 자격 `ModTierTable` 행이 하나 이상 있다.
- Min/Max, Weight, RequiredLevel이 유효 범위다.
- 모든 주요 장비 종류에 최소 후보 수가 있다.
- 변화의 오브는 교체 가능한 기존 옵션 하나와 같은 슬롯의 다른 Mod를 롤한다.
- 교체 결과는 기존 `ModTableId`와 달라야 하고 Prefix/Postfix 슬롯은 유지된다.
- 엑잘티드 오브는 빈 자리가 있는 Prefix/Postfix 전체 후보에서 Mod 하나를 롤한다.
- Prefix/Postfix가 모두 가득 차면 랜덤 추가를 거부한다.
- 구 세이브에 같은 Slot·Group이 둘 이상이면 임의 선택 없이 오류로 거부한다.
- Prefix/Postfix 3개 상한이 동작한다.
- Base/Implicit은 교체 대상과 추가 후보에서 제외된다.
- 재료·Gold 부족 실패에서 입력 스냅샷이 변하지 않는다.
- 성공 시 정확히 한 Mod만 교체되거나 하나만 추가된다.
- 성공 시 정확한 재료 수량과 Gold만 감소한다.
- 고정 seed에서 티어와 값이 범위 안에 들어간다.

### 12.2 컴파일

- `Assembly-CSharp` 오류 0
- `Assembly-CSharp-Editor` 오류 0
- 신규 nullable 경고와 사용되지 않는 직렬화 필드 확인

### 12.3 PlayMode 체크리스트

1. Tier 1 제련소: 랜덤 옵션 교체와 랜덤 옵션 추가 모두 실행 가능
2. Tier 2 제련소: Tier 1과 같은 후보·기능·비용으로 교체·추가 가능
3. Tier 3 제련소: Tier 1과 같은 후보·기능·비용으로 교체·추가 가능하며 별도 Gold 할인 없음
4. Race·Profile·초기 Stage가 다른 신규 마을마다 Tier 1 이상 제련소가 배치되고 상호작용 가능
5. 옵션 교체 시 변화의 오브 1개 + 25 Gold, 추가 시 엑잘티드 오브 1개 + 40 Gold 정확히 차감
6. 재료 없음/Gold 부족: 실행 불가, 데이터 무변경
7. 장비 선택 후 변화/엑잘티드 작업 버튼만으로 실행하고 결과 옵션을 선택하지 않음
8. Prefix/Postfix 각각 3개에서 추가 차단
9. 같은 슬롯 Group 중복 차단, 다른 슬롯 같은 Group 허용
10. Base/Implicit이 선택 불가
11. 실행 후 자동 재료 수량·Gold·실제 Mod 수치 갱신
12. Forge 닫기→재열기 후 콜백 중복 없이 1회만 실행
13. 성공 직후 변경 전·후 전체 옵션 비교 표시
14. 저장→종료→재로드 후 옵션·재료·Gold 유지
15. 마법 부여 장비 장착 후 CharacterUI·Tooltip·예상 피해 갱신
16. Attack/Spell 태그가 있는 옵션의 적용 범위 일치
17. 실제 타격·치명타·공격속도와 공격 프로필 일치

### 12.4 실패 안전성

각 실패 사례 전후에 다음 스냅샷을 비교한다.

```text
Target Equipment Mods
Inventory item ids / instance ids / quantities
Player Gold
```

실패 결과에서는 세 스냅샷이 모두 동일해야 한다.

---

## 13. 구현 순서

### Phase E0 — 콘텐츠·계약 준비

1. 모든 신규 게임 마을에 Forge 160 Tier 1을 최소 보장하고 Profile·Stage별 중복 없는 초기 완공 결과를 진단한다.
2. Prefix/Postfix 누락 티어와 부위별 최소 후보를 채운다.
3. `ForgeTier`와 `ItemEnchantmentOperation`을 도메인 enum으로 정리한다.
4. `ItemEnchantmentCostTable`과 교체 1개+25G, 추가 1개+40G 비용 규칙을 추가한다.
5. 테이블 로드와 Editor 진단을 통과시킨다.

### Phase E1 — 서비스 수직 슬라이스

1. Inventory ItemId 합산·소비 API를 추가한다.
2. 마법 부여 후보 필터와 읽기 전용 비용 조회를 구현한다.
3. 기존 옵션 하나와 같은 슬롯의 다른 Mod를 롤하는 ReplaceAffix 트랜잭션을 구현한다.
4. 빈 슬롯의 전체 유효 Mod 중 하나를 롤하는 AddAffix 트랜잭션을 구현한다.
5. 변경 전·후 옵션 스냅샷 결과를 구현한다.
6. 실패 무변경과 성공 자동 소비를 Editor 진단으로 검증한다.

### Phase E2 — Forge UI

1. UXML을 장비/랜덤 작업 2열 + 성공 전후 비교 구조로 변경한다.
2. 장비 선택 상태와 변화/엑잘티드 작업 버튼을 구현한다.
3. 작업별 후보 개수·재료·비용·실패 이유를 표시한다.
4. 두 작업 버튼과 성공 전후 비교를 연결한다.
5. 공통 옵션 문구 포맷을 Tooltip과 공유한다.

### Phase E3 — 통합 검증

1. 컴파일과 Editor 진단을 실행한다.
2. Tier 1/2/3에서 같은 마법 부여 기능과 비용이 적용되는지 확인한다.
3. 저장 왕복을 확인한다.
4. 장착 후 스탯·공격 프로필·실제 전투를 확인한다.
5. 밸런스 값을 테이블에서 조정한다.

---

## 14. 후속 확장

### 14.1 신성한 오브

현재 Prefix/Postfix 중 수치를 재부여할 대상을 고르는 별도 흐름을 만든다. ModTableId·Slot·Tier는 유지하고 현재 Tier의 Value1/Value2만 다시 롤하며 신성한 오브를 자동 차감한다.

### 14.2 카오스 오브

모든 Prefix/Postfix를 제거한 뒤 슬롯별 개수를 정하고 새 후보를 가중치 롤한다. 전체 옵션을 바꾸는 작업이므로 별도 확인 화면을 사용한다.

### 14.3 진화의 오브

현행 아이템에는 Normal/Magic/Rare 희귀도 상태가 없다. 진화의 오브 의미를 정의하려면 희귀도별 Prefix/Postfix 개수와 전환 규칙을 먼저 설계한다.

**2026-10-21 구현 (ITEM_DROP_BALANCE §4·§6)**: 등급은 붙은 접두·접미 수로 판정한다(0개 일반, 각 1개 이하 마법, 그 밖 희귀). 진화의 오브(`EvolveToMagic`)는 일반 장비에 접두·접미 후보 전체에서 옵션 1개를 붙여 마법으로 만든다(오브 1개 + 15 Gold, 마법 규칙). 엑잘티드(`AddAffix`)는 일반 장비에 쓸 수 없고, 마법 장비는 비어 있는 반대쪽 마법 칸을 먼저 채운다. 품질 다시 굴리기(`RerollQuality`)는 일반 장비 품질을 0~20에서 균등하게 새로 정한다(골드 20만, 낮아질 수 있음). 비용표 `ItemEnchantmentCostTable.bytes`는 시트 탭 없이 bytes만 있어 행 3·4를 직접 추가했다(품질 행은 재료 칸 0·0).

### 14.4 무기 고유 제련

`WeaponLocalEnhancementMaterialTable`과 `WeaponLocalEnhancements`를 사용한다. 피해 속성·치명타·공격속도의 Base 증가율 누적과 MaxTotal 규칙을 처리하며 일반 Prefix/Postfix 마법 부여와 같은 리스트를 사용하지 않는다.

### 14.5 장착 장비 직접 작업

장착 장비를 대상으로 확장할 때는 다음 원자 흐름을 추가한다.

```text
기존 SourceId Modifier 제거
→ 옵션 트랜잭션
→ 변경된 Mod Modifier 재등록
→ StatDirtyTag
→ SkillAttackProfile 재계산
→ UI 갱신
```

이 경로는 인벤토리 장비 MVP가 저장·전투 검증을 통과한 뒤 도입한다.

### 14.6 Forge Tier 2/3 확장

Tier 2/3의 추가 이점은 MVP 이후 별도 콘텐츠·밸런스 설계로 정한다. 전용 작업, 추가 선택지, 편의 기능, 비용 혜택 등은 후보일 뿐 현재 계약으로 확정하지 않는다. 확장 후에도 Tier 1의 랜덤 옵션 교체·추가 핵심 기능은 유지한다.

---

## 15. 완료 정의

다음을 모두 만족하면 MVP를 완료로 본다.

- 장비만 선택하고 변화/엑잘티드 작업 버튼으로 실행할 수 있다.
- 변화의 오브는 기존 Prefix/Postfix 하나를 같은 슬롯의 다른 랜덤 옵션으로 교체한다.
- 엑잘티드 오브는 빈 Prefix/Postfix 자리에 유효한 랜덤 옵션 하나를 추가한다.
- 성공 직후 실제 결과 기준 변경 전·후 옵션 비교가 표시된다.
- Base/Implicit/WeaponLocalEnhancements 책임 경계가 유지된다.
- 모든 MVP 마법 부여 작업이 Forge Tier 1 이상에서 가능하고 Tier 1/2/3의 비용이 동일하다.
- 신규 게임의 모든 마을이 Race·Profile·초기 Stage와 관계없이 Forge 160을 최소 Tier 1로 제공한다.
- 옵션 교체는 변화의 오브 1개+25 Gold, 신규 추가는 엑잘티드 오브 1개+40 Gold를 정확히 소비한다.
- 후보 필터, Group 중복, 슬롯 상한, 티어 자격이 서비스에서 강제된다.
- 실패 시 옵션·재료·Gold가 변하지 않는다.
- 성공 즉시 저장이 요청되고 결과·재료·Gold가 저장 왕복 후 유지된다.
- 툴팁·캐릭터 스탯·공격 프로필·실제 전투가 같은 결과를 표시하고 소비한다.
- 주요 장비 부위에 실제 선택 가능한 최소 옵션 콘텐츠가 존재한다.
