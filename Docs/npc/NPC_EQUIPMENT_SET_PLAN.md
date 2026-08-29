# NPC 고정 장비 세트 다양화 구현 계획

> **상태 (2026-08-22): 원본 Google Sheets P1·P2 반영 완료 — 로컬 bytes 갱신, 코드 방어 강화, 진단 및 플레이 모드 검증 대기**
>
> **정본 관계**: NPC 장비 슬롯·저장·ECS 적용 계약은 [NPC_EQUIPMENT_DESIGN.md](NPC_EQUIPMENT_DESIGN.md)가 소유한다. 이 문서는 그 구조를 바꾸지 않고, 현행 `NpcEquipTable`을 이용해 신규 NPC의 시작 장비를 여러 고정 세트로 다양화하는 1차 작업 계획이다.
>
> **핵심 결정**: 1차에서는 NPC 인스턴스마다 장비를 독립 추첨하지 않는다. `NpcTable` 행마다 하나의 완성된 `NpcEquipTable` 세트를 지정하고, 같은 세트 안의 장비 품질·Prefix·Postfix만 기존 생성 경로에서 랜덤 롤한다.

---

## 1. 목표

1. 신규 NPC가 모두 `낡은 단검` 하나만 장착하는 현행 데이터를 제거한다.
2. 현재 존재하는 NPC 테이블 5종에 서로 구분되는 완성 장비 세트를 배정한다.
3. 새 풀 테이블이나 런타임 상태를 추가하지 않고 기존 `NpcTable.EquipTableId`와 `NpcEquipTable`을 그대로 사용한다.
4. 모든 세트는 현재 NPC의 근접 기본 AI와 호환되는 근접 무기를 사용한다.
5. 잘못된 슬롯·아이템 조합이 들어와도 NPC 생성은 계속하되 해당 장비만 건너뛰고 원인을 로그로 남긴다.
6. 세이브/로드와 청크 비활성화/재활성화에서 최초 생성된 장비 인스턴스를 그대로 보존한다.

### 1.1 비목표

- 같은 `NpcTableId` 안에서 여러 세트를 확률적으로 선택하는 장비 풀
- 슬롯별 무기·방어구 독립 랜덤 추첨
- `NpcSaveData.Inventory` 5칸에 소모품·재료·화폐를 넣는 시작 소지품 시스템
- NPC 활동 중 장비 교체·구매·획득·강화
- NPC 사망 시 보유 장비나 소지품 드랍
- NPC 무기 SpriteLibrary 또는 외형 갱신
- 원거리·마법 무기에 맞춘 NPC AI 및 스킬 분화
- 기존 세이브 NPC의 장비를 새 세트로 강제 교체하는 마이그레이션

---

## 2. 현재 기준선과 문제 원인

### 2.1 현행 데이터

현재 `NpcTable.bytes`의 NPC 3001~3005는 모두 `EquipTableId=1`을 참조한다. `NpcEquipTable.bytes`에는 세트 1 한 행만 있고, `WeaponRight=1`이며 나머지 11슬롯은 모두 0이다. `ItemTable`의 ItemId 1은 `낡은 단검`이다.

```text
NpcTable 3001 ─┐
NpcTable 3002 ─┤
NpcTable 3003 ─┼→ EquipTableId 1 → WeaponRight 1 → 낡은 단검
NpcTable 3004 ─┤
NpcTable 3005 ─┘
```

문제는 아이템 생성 코드가 단검을 강제하는 것이 아니라, 모든 NPC가 같은 장비 세트 한 행을 공유하는 테이블 구성에 있다.

### 2.2 유지할 생성 경로

신규 NPC는 `NpcSaveData` 생성 직후 다음 경로로 장비를 받는다.

```text
NpcManager.RegisterNpcsFromMapFile 또는 SpawnNewNpc
    → SeedNpcEquipment(saveData)
    → NpcTable.EquipTableId 조회
    → NpcEquipHelper.SeedInitialEquipment
    → NpcEquipTable의 12슬롯 순회
    → ItemManager.CreateInventoryItemData(itemId)
    → NpcSaveData.InventoryEquip 저장
```

실제 NPC 엔티티가 생성될 때는 `InventoryEquip`에 저장된 장비 Modifier가 `StatComponent`에 적용된다. 일반 `Inventory`는 이 흐름에 참여하지 않는다.

### 2.3 현행 랜덤 범위

고정 세트는 장비의 **기본 ItemId와 슬롯 조합**만 고정한다. 각 슬롯의 `ItemData`는 NPC 생성 시 새 인스턴스로 만들어지므로 다음 값은 NPC마다 달라진다.

- `ItemInstanceId`
- 품질 1~100
- Prefix 1~2개와 각 수치
- Postfix 1~2개와 각 수치

따라서 같은 세트를 쓰는 NPC라도 최종 장비 옵션과 전투 수치는 완전히 같지 않다.

---

## 3. 1차 목표 모델

### 3.1 세트가 다양성의 최소 단위다

`NpcEquipTable` 한 행을 전투 콘셉트가 완결된 하나의 로드아웃으로 취급한다.

- 오른손/왼손 무기 조합을 세트에서 함께 결정한다.
- 방어구와 장신구 밀도를 세트 콘셉트에 맞춘다.
- 슬롯별 독립 추첨은 하지 않는다.
- 모든 ItemId는 해당 슬롯과 호환되어야 한다.
- `0`은 의도적으로 비운 슬롯이다.

이 방식은 검·방패, 경장 단검, 중장 근접처럼 조합의 의미를 유지하고 잘못된 무기 조합을 데이터 단계에서 줄인다.

### 3.2 NPC와 세트의 관계

1차 구현에서는 관계가 항상 1:1이다.

```text
NpcTable 한 행 ── EquipTableId 하나 ── NpcEquipTable 한 행
```

같은 `NpcTableId`로 생성된 NPC는 같은 기본 세트를 사용한다. 향후 인스턴스별 랜덤 세트가 필요해지면 이 위에 `EquipPoolId → 여러 EquipTableId` 선택 계층을 추가하며, `NpcEquipTable` 자체는 그대로 재사용한다.

### 3.3 전투 호환 정책

현행 NPC 5종은 모두 `AiTableId=1`을 사용하고, 해당 AI는 근접 행동과 기본 공격 스킬 `StrikeMonster`를 사용한다. 1차 세트는 검·단검·도끼·방패 같은 근접 장비만 사용한다.

다음 장비는 ItemTable에 존재하더라도 1차 세트에서 제외한다.

- 활·석궁·투창·표창: 원거리 행동과 투사체 스킬이 연결되지 않음
- 지팡이·완드·마법서: 마법 전용 AI와 스킬 구성이 없음
- Tier 2~3 장비: NPC 기본 스탯 대비 장비 격차 검증 전
- 귀걸이: 현재 ItemTable에 대응 아이템이 없음

---

## 4. 고정 장비 세트 데이터 초안

모든 세트는 현재 존재하며 `EquipmentBaseStatTable`이 연결된 Tier 1 아이템만 사용한다. 아래 값은 1차 구현 기준안이며, 플레이 모드 전투 실측에서 과도한 격차가 확인되면 슬롯 수를 줄이는 방향으로 조정한다.

| EquipTableId | 세트 콘셉트 | WeaponRight | WeaponLeft | Helmet | Armor | Gloves | Boots | Necklace | RingRight | RingLeft | Belt | Earrings |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 대장장이형 근접 | 20 구리 도끼 | 0 | 11 | 12 | 13 | 14 | 0 | 0 | 0 | 15 | 0 / 0 |
| 2 | 검방 수호형 | 19 구리 한손검 | 18 목재 방패 | 11 | 12 | 13 | 14 | 0 | 17 | 0 | 0 | 0 / 0 |
| 3 | 경장 단검형 | 21 단도 | 0 | 11 | 0 | 13 | 14 | 0 | 0 | 0 | 15 | 0 / 0 |
| 4 | 결투가형 | 4 샤브르 | 0 | 0 | 12 | 13 | 14 | 16 | 0 | 0 | 0 | 0 / 0 |
| 5 | 중장 검사형 | 6 전쟁 검 | 0 | 11 | 12 | 13 | 14 | 16 | 17 | 0 | 15 | 0 / 0 |

숫자만 모은 실제 시트 입력 형태:

| Id | WeaponRight | WeaponLeft | Helmet | Armor | Gloves | Boots | Necklace | RingRight | RingLeft | Belt | EarringRight | EarringLeft | Note |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 20 | 0 | 11 | 12 | 13 | 14 | 0 | 0 | 0 | 15 | 0 | 0 | 대장장이형 근접 |
| 2 | 19 | 18 | 11 | 12 | 13 | 14 | 0 | 17 | 0 | 0 | 0 | 0 | 검방 수호형 |
| 3 | 21 | 0 | 11 | 0 | 13 | 14 | 0 | 0 | 0 | 15 | 0 | 0 | 경장 단검형 |
| 4 | 4 | 0 | 0 | 12 | 13 | 14 | 16 | 0 | 0 | 0 | 0 | 0 | 결투가형 |
| 5 | 6 | 0 | 11 | 12 | 13 | 14 | 16 | 17 | 0 | 15 | 0 | 0 | 중장 검사형 |

### 4.1 NpcTable 배정 초안

| NpcTable.Id | 현재 JobType | 목표 EquipTableId | 비고 |
|---:|---|---:|---|
| 3001 | Blacksmith | 1 | 대장장이형 근접 |
| 3002 | Merchant | 2 | 검방 수호형 |
| 3003 | Merchant | 3 | 경장 단검형 |
| 3004 | Merchant | 4 | 결투가형 |
| 3005 | Merchant | 5 | 중장 검사형 |

`JobType`은 물리 NPC를 마을 생산자로 되돌리는 입력이 아니며, 이 배정에서는 세트 콘셉트를 구분하는 flavor로만 사용한다.

---

## 5. 생성·저장 계약

### 5.1 신규 NPC

- 맵 파일에서 처음 등록되는 NPC와 `SpawnNewNpc`로 생성되는 NPC에만 세트를 시드한다.
- 장비 슬롯마다 `CreateInventoryItemData(itemId)`를 한 번 호출한다.
- 같은 ItemId를 여러 슬롯에 사용하더라도 서로 다른 `ItemInstanceId`와 옵션을 가진 별도 인스턴스를 만든다.
- 세트 일부 아이템 생성이 실패해도 나머지 슬롯 시드는 계속한다.

### 5.2 청크 재활성화

- `NpcSaveData.InventoryEquip`에 저장된 인스턴스를 재사용한다.
- `NpcEquipTable`을 다시 읽어 세트를 재생성하지 않는다.
- 청크를 벗어났다 돌아와도 장비와 옵션이 바뀌지 않아야 한다.

### 5.3 세이브 로드

- 기존처럼 `NpcSaveData.OnLoadCompleted()`로 `ItemData` 내부 테이블 참조만 복원한다.
- Load 경로에서는 `SeedNpcEquipment`을 호출하지 않는다.
- 테이블 변경 전에 저장된 NPC는 낡은 단검을 계속 보유할 수 있다.
- 새 세트 적용 확인은 New Game 또는 변경 후 새로 보충된 NPC를 기준으로 한다.

### 5.4 재호출 안전성

- 이미 값이 있는 장비 슬롯은 덮어쓰지 않는다.
- 빈 슬롯만 시드한다.
- 향후 구 세이브 보정이 필요해도 기존 장비를 교체하는 암묵적 마이그레이션은 추가하지 않는다.

---

## 6. 구현 계획

### P0. 데이터 사전 검증

대상:

- `ItemTable.bytes`
- `EquipmentBaseStatTable.bytes`
- `NpcTable.bytes`
- `NpcEquipTable.bytes`
- `AiTable.bytes`
- `SkillTable.bytes`

작업:

1. 세트에서 사용할 ItemId가 모두 ItemTable에 존재하는지 확인한다.
2. 각 ItemId가 Base 또는 Implicit 데이터를 가져 실제 `EquipmentData`를 만들 수 있는지 확인한다.
3. 아이템 Category와 목표 장비 슬롯이 호환되는지 확인한다.
4. NPC AI가 근접 공격이므로 원거리·마법 무기를 제외한다.

완료 기준:

- §4의 모든 ItemId가 생성 가능한 장비로 확인된다.
- 존재하지 않거나 장비 데이터가 없는 ItemId가 0건이다.

### P1. NpcEquipTable 세트 5종 작성

진행 상태 (2026-08-22): 원본 `ARPG Tables/NpcEquip!A2:N6` 반영 및 재조회·화면 검증 완료. Unity Download Table을 통한 로컬 bytes 갱신은 아직 수행하지 않았다.

대상:

- 외부 `NpcEquipTable` 시트
- 생성 결과 `Assets/_BinaryData/TableData/NpcEquipTable.bytes`

작업:

1. 기존 세트 1을 §4의 대장장이형 세트로 변경한다.
2. 세트 2~5 행을 추가한다.
3. `Note`에 세트 콘셉트를 기록한다.
4. Unity 메뉴 `ARPG/Download Table/NpcEquipTable`로 bytes를 갱신한다.

완료 기준:

- 세트 ID 1~5가 모두 로드된다.
- 각 행의 12슬롯 값이 §4 기준안과 일치한다.

### P2. NpcTable에 세트 배정

진행 상태 (2026-08-22): 원본 `ARPG Tables/Npc!K2:K6`에 NPC 3001~3005의 `EquipTableId=1~5` 반영 및 재조회·화면 검증 완료. Unity Download Table을 통한 로컬 bytes 갱신은 아직 수행하지 않았다.

대상:

- 외부 `NpcTable` 시트
- 생성 결과 `Assets/_BinaryData/TableData/NpcTable.bytes`

작업:

1. NPC 3001~3005의 `EquipTableId`를 각각 1~5로 변경한다.
2. Unity 메뉴 `ARPG/Download Table/NpcTable`로 bytes를 갱신한다.

완료 기준:

- 5개 NPC 테이블 행이 서로 다른 `EquipTableId`를 참조한다.
- 모든 참조 대상 `NpcEquipTable` 행이 존재한다.

### P3. 슬롯 호환성 방어 강화

대상:

- `Assets/Scripts/Npc/NpcEquipHelper.cs`

현행 문제:

- `ItemData.Equipment != null`만 확인한 뒤 `InventoryEquip[i]`에 직접 대입한다.
- Item Category가 해당 슬롯에 맞는지는 확인하지 않는다.

작업:

1. 생성된 장비를 직접 배열에 넣지 않고 `NpcSaveData.EquipItem(slot, item, out replacedItem)`을 통해 배치한다.
2. 슬롯이 비어 있는 경우만 호출해 기존 재호출 안전성을 유지한다.
3. 슬롯 불일치 시 NPC 생성은 중단하지 않고 `equipTableId`, `slot`, `itemId`, 실제 `EquipType`을 Warning으로 남긴다.
4. 실패한 아이템은 인벤토리로 이동시키지 않고 폐기한다.

완료 기준:

- 올바른 세트는 Warning 없이 전 슬롯이 들어간다.
- 의도적으로 잘못된 테스트 조합은 해당 슬롯만 비워지고 Warning이 남는다.

### P4. 정적 진단 추가

대상 후보:

- `Assets/Scripts/Editor/NpcEquipmentSetDiagnostics.cs`

진단 항목:

1. `NpcTable.EquipTableId > 0`인 모든 행의 세트 존재 여부
2. 세트의 0이 아닌 모든 ItemId 존재 여부
3. `EquipmentData` 생성 자격(Base 또는 Implicit) 여부
4. 슬롯과 EquipmentType 호환 여부
5. 현재 NPC AI 기준 원거리·마법 무기 포함 여부
6. 세트별 채워진 슬롯 수와 장비 이름 요약 출력

완료 기준:

- 오류 0건
- NPC 3001~3005에 대해 서로 다른 5개 세트 요약이 출력됨

### P5. 플레이 모드 검증

검증 절차:

1. 기존 세이브가 아닌 New Game에서 NPC를 생성한다.
2. NPC 검사 UI로 NPC 3001~3005의 장비 슬롯을 확인한다.
3. 같은 `NpcTableId` 인스턴스 두 명의 기본 세트는 같고 옵션·품질은 달라질 수 있음을 확인한다.
4. NPC 공격 시 무기 Base와 장비 Modifier가 전투 스탯에 반영되는지 확인한다.
5. 검방 세트의 왼손 방패 Modifier가 적용되는지 확인한다.
6. 청크 비활성화 후 재진입해 ItemId·ItemInstanceId·옵션이 유지되는지 확인한다.
7. 저장 후 재실행해 같은 장비 인스턴스가 복원되는지 확인한다.
8. 콘솔에 `NpcEquipHelper` Warning/Error가 없는지 확인한다.

완료 기준:

- NPC 5종이 계획된 서로 다른 세트를 장착한다.
- 생성·청크 왕복·세이브 왕복에서 장비 손실 또는 재추첨이 없다.
- 기본 공격과 스탯 계산에 회귀가 없다.

---

## 7. 변경 파일 예상

| 파일 | 변경 내용 |
|---|---|
| `Assets/_BinaryData/TableData/NpcEquipTable.bytes` | 세트 1 변경, 세트 2~5 추가 |
| `Assets/_BinaryData/TableData/NpcTable.bytes` | NPC 3001~3005에 세트 1~5 배정 |
| `Assets/Scripts/Npc/NpcEquipHelper.cs` | 슬롯 호환성 검증 경로 사용 및 실패 로그 보강 |
| `Assets/Scripts/Editor/NpcEquipmentSetDiagnostics.cs` | 세트 참조·아이템·슬롯 정적 진단 신규 추가 |
| `Docs/npc/NPC_EQUIPMENT_DESIGN.md` | 본 계획서 링크와 구현 완료 후 상태 반영 |

새 ECS 컴포넌트, 시스템 등록, ComponentManager 풀 크기 변경은 필요하지 않다. 장비와 소지품의 영속 정본은 계속 `NpcSaveData`가 소유한다.

---

## 8. 위험과 대응

| 위험 | 영향 | 대응 |
|---|---|---|
| 잘못된 슬롯 ItemId | 비정상 조합·Modifier 오적용 | P3 런타임 검증 + P4 정적 진단 |
| 세트별 장비 수 차이 | NPC 전투력 편차 확대 | 1차는 Tier 1 한정, 플레이 모드에서 슬롯 수 조정 |
| 원거리 무기와 근접 AI 혼용 | 공격 거리·외형 불일치 | 1차 근접 무기 한정 |
| 기존 세이브가 계속 단검 사용 | 변경 확인 혼란 | New Game 기준 검증, 기존 세이브 비마이그레이션 명시 |
| 외형에 무기가 표시되지 않음 | 장비 데이터와 화면 외형 차이 | 현행 알려진 제약 유지, SpriteLibrary 연동은 별도 작업 |
| 일반 Inventory가 비어 있음 | 검사 UI 소지품 다양성 없음 | 시작 소지품 시스템을 후속 계획으로 분리 |

---

## 9. 최종 인수 기준

- [ ] `NpcEquipTable`에 유효한 고정 세트 5종이 존재한다.
- [ ] NPC 3001~3005가 각각 세트 1~5를 참조한다.
- [ ] 모든 세트가 현재 근접 NPC AI와 호환되는 무기를 사용한다.
- [ ] 모든 0이 아닌 ItemId가 존재하고 장비 인스턴스를 생성할 수 있다.
- [ ] 모든 아이템 Category가 배치 슬롯과 호환된다.
- [ ] 신규 NPC 생성 시 세트 전체가 `InventoryEquip`에 저장된다.
- [ ] 같은 세트라도 아이템 옵션은 인스턴스별로 롤된다.
- [ ] 청크 왕복과 세이브 왕복에서 장비가 재추첨되지 않는다.
- [ ] 기존 세이브의 장비를 암묵적으로 덮어쓰지 않는다.
- [ ] 정적 진단 오류 및 런타임 장비 시드 Warning이 0건이다.

---

## 10. 후속 확장 경계

1차 고정 세트가 안정화된 뒤에만 다음 확장을 검토한다.

1. `EquipPoolId`와 가중치 엔트리를 추가해 같은 NPC 유형이 여러 완성 세트 중 하나를 선택하게 한다.
2. `InventoryProfileId`를 추가해 일반 `Inventory`에 소모품·재료·화폐를 시드한다.
3. NPC AI/스킬 프로필을 분리한 뒤 궁수·마법사 세트를 추가한다.
4. NPC 사망 드랍, 활동 보상 획득, 장비 교체를 각각 별도 책임으로 설계한다.

후속 기능도 최초 생성 결과를 `NpcSaveData`에 저장하고, 청크 실체화 시 다시 추첨하지 않는 원칙을 유지한다.
