# NPC 장비/인벤토리 시스템 설계

> ⚠️ **마을–NPC 경계 반영** ([VILLAGE_NPC_BOUNDARY_DESIGN](VILLAGE_NPC_BOUNDARY_DESIGN.md)):
> 여관 고용/방문자 시스템 제거로 본 문서의 **방문자 관련 서술은 구식**이 됐다 —
> §4.1의 `SpawnVisitorNpc` 시드 경로(현행 장비 시드는 2경로), §8.4의 진입 경로 2(Inn 방문자 카드 "장비" 버튼 · `UINpcInspect.BindSaveData`)는 **삭제된 코드**다. 해당 지점에 취소선으로 표시했다.
>
> **세트 다양화**: NPC 5종의 시작 장비 세트(NpcEquipTable 1~5, 슬롯별 장착 확률)는 [NPC_EQUIPMENT_SET_PLAN.md](NPC_EQUIPMENT_SET_PLAN.md)대로 구현됐다(2026-08-23). 플레이 모드 검증 기록은 없다.
>
> **2026-10-03**: 장비 인스턴스는 상점용 기본 아이템이다(§3.3, [NPC_SHOP_EQUIPMENT_DESIGN.md](NPC_SHOP_EQUIPMENT_DESIGN.md) D118·D121).

## 1. 목표

NPC에게 장비 슬롯 12칸 + 인벤토리 5칸을 부여한다. 장비 스탯은 플레이어와 동일한 경로로 `StatComponent`에 반영된다. 청크 비활성/활성 사이클을 거쳐도 데이터가 영속화된다.

## 2. 데이터 모델

### 2.1 [NpcSaveData](../../Assets/Scripts/Npc/NpcSaveData.cs)

기존 영구 저장 클래스에 두 필드 추가:

```csharp
// 12칸 — EquipSlotType.Max
public ItemData?[] InventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

// 5칸 — NPC_INVENTORY_SLOTCOUNT_MAX (인덱스가 슬롯 의미)
public List<ItemData?> Inventory = new List<ItemData?>();
```

부속 메서드:
- `EnsureSlotsInitialized()` — 생성자/로드 모두에서 호출. 구 세이브(필드 없음) 안전 처리.
- `OnLoadCompleted()` — `ItemData.OnLoadCompleted()` 일괄 호출 (PlayerData와 동일 패턴).
- `EquipItem/UnequipItem/GetEquipedItem` — PlayerData와 동일 시그니처. 슬롯 호환성 검사 `Utils.ContainsEquipmentType`.

### 2.2 [GlobalEnum](../../Assets/Scripts/Common/GlobalEnum.cs)

```csharp
public const int NPC_INVENTORY_SLOTCOUNT_MAX = 5;
```

## 3. 테이블

### 3.1 NpcTable 변경 ([Tables.cs](../../Assets/Scripts/Common/Tables.cs))

- **제거**: `WeaponId` (코드 어디서도 사용 안 되던 유물 컬럼)
- **추가**: `EquipTableId` — `NpcEquipTable.Id` 참조. 0 = 무장 없음

### 3.2 NpcEquipTable (신규, Option A — 고정 세트)

```
Id | Desc | WeaponRight | WeaponRightChance | WeaponLeft | WeaponLeftChance | … | EarringLeft | EarringLeftChance
```

12개 슬롯마다 `ItemTable.Id`와 장착 확률(0~100)을 쌍으로 둔다(시트 A:Z, `Desc`는 세트 설명). 슬롯 ItemId가 0이면 비움, 확률 0이면 장착하지 않음. 확률은 NPC를 처음 만들 때 한 번만 판정한다. [src: Assets/Scripts/Common/Tables.cs `NpcEquipTable`]

`GetItemId(EquipSlotType slot)`·`GetEquipChance(EquipSlotType slot)`로 슬롯 → ItemId·확률 조회.

### 3.3 인스턴스 생성 정책

> **2026-10-03 변경** ([NPC_SHOP_EQUIPMENT_DESIGN](NPC_SHOP_EQUIPMENT_DESIGN.md) D118): 일반 NPC의 장비는 **상점용 기본 아이템**(고정 옵션)이다. 아래가 현행 정책이고, 무작위 옵션 롤 정책은 폐기됐다.

NPC 신규 생성 시 슬롯별 ItemId마다 `BasicItemTable`의 변형 하나를 **entityId·슬롯 해시**(`NpcEquipHelper.PickBasicVariant`, `DerivedSeed.ChannelNpcBasicEquipment`)로 골라 `ItemManager.CreateBasicItem(basicItemId)`로 만든다. 결과는 **Base·Implicit(기존 테이블) + 고정 옵션 1개, 품질 1**이고 무작위 롤이 없다. 같은 NPC·슬롯은 언제 계산해도 같은 변형이 나온다. 변형이 없는 장비면 경고를 남기고 `CreateBaseEquipment`(Base만)로 대신한다.

- 저장은 행 번호(`EquipmentData.BasicItemId`)만 하고, 로드 때 테이블에서 옵션을 다시 만든다 (D122).
- NPC는 전리품·장물을 장착하지 않고 상점의 기본 장비 칸에서만 사서 강해진다 (D119). 그래서 NPC 장비는 항상 기본 아이템으로 남는다.
- (폐기된 이전 정책) `CreateInventoryItemData`로 Prefix·Postfix 1~3개씩 무작위로 굴려 NPC마다 옵션이 달랐다. 옵션이 제각각인 일반 NPC 장비는 게임에서 의미가 작고, 세이브의 NPC 상태 79%를 차지해 바꿨다.

## 4. 시드 흐름

### 4.1 신규 생성 (2 경로 — [NpcManager](../../Assets/Scripts/Manager/NpcManager.cs))

| 경로 | 위치 |
|---|---|
| 맵 파일 로드 | `RegisterNpcsFromMapFile` |
| 런타임 신규 스폰 | `SpawnNewNpc` |
| ~~여관 방문자 등록~~ | ~~`SpawnVisitorNpc`~~ — **삭제됨** (2026-07-08, SP5 방문자 시스템 제거) |

모든 경로에서 `new NpcSaveData(...)` 직후 `SeedNpcEquipment(saveData)` 호출.

`SeedNpcEquipment` → `NpcEquipHelper.SeedInitialEquipment(saveData, npcTable.EquipTableId)`:
- `NpcEquipTable` 조회 → 슬롯별 ItemId·장착 확률 → 기본 아이템 변형(entityId·슬롯 해시) → `CreateBasicItem` → `saveData.InventoryEquip[slot]`에 채움 (§3.3).
- 이미 슬롯에 데이터가 있으면 덮어쓰지 않음(재호출 안전).

### 4.2 Load 경로

`NpcManager.Load`에서 saveData 복원 시 `saveData.OnLoadCompleted()` 호출 — `ItemData.Table`/`Equipment.Mods[i].Table` 등 `[NonSerialized]` 테이블 참조를 다시 잇고, 행 번호만 저장된 기본 아이템은 옵션을 다시 만든다.

**Load 경로에서는 SeedNpcEquipment를 호출하지 않는다** — 기존 장비 그대로 보존.

- **예외 — WorldDataVersion 17 마이그레이션 (D121)**: 버전 16 이하 세이브는 로드 때 한 번 `ReplaceEquipmentWithBasicItems`가 NPC 장착 장비를 같은 ItemId의 기본 아이템(시드와 같은 해시)으로 바꾼다. `ItemData` 객체·`ItemInstanceId`는 유지해 장비 소문(ItemOwner)이 계속 유효하고, 개인 상한 기준(`InitialPower`)만 다시 계산한다. 가방·플레이어·상점은 그대로다.

## 5. ECS 적용

### 5.1 [EntityFactory.CreateNpc](../../Assets/Scripts/Factory/EntityFactory.cs) 시그니처 확장

```csharp
public static async UniTask<(int, EntityBase?)> CreateNpc(
    int npcTableId, Vector3 position,
    Transform? parent = null, int savedEntityId = -1,
    NpcSaveData? saveData = null)   // ← 추가
```

AI 컴포넌트 부착 이후, `RegisterToSystems` 이전에 한 줄 추가:

```csharp
if (saveData != null)
    EquipHelper.ApplyAllEquipmentModifiers(entityId, saveData.InventoryEquip);
```

플레이어의 `_inventoryEquip` 복원 경로와 동일한 entityId 기반 API — NPC 전용 분기 없음.

### 5.2 NpcManager.SpawnNpc 전달

```csharp
var (createdId, entity) = await EntityFactory.CreateNpc(
    saveData.NpcTableId, spawnPos3D, _npcParent, entityId, saveData);
```

## 6. 시트 작업 (완료 — 아래는 당시 작업 기록)

> ✅ 완료: NpcTable `EquipTableId` 컬럼, NpcEquip 탭(gid 1566085089, `Desc` + 슬롯별 ItemId·Chance 쌍, A:Z)이 `DownloadTables.cs`에 등록돼 있다. 아래 1~3은 처음 도입할 때의 작업 목록이며 열 구성은 §3.2가 현행이다. [src: Assets/Scripts/Editor/DownloadTables.cs]

이번 코드는 시트가 준비되지 않은 상태에서도 안전하게 컴파일/실행되도록 했다 (`AR.s.Data.GetNpcEquipTable`이 `null` 반환하면 시드 skip). 실제 사용을 위해 사용자가 시트 측에서 다음 작업 필요:

1. **NpcTable 시트** ([gid=1460299278](https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/edit#gid=1460299278))
   - K열 헤더 `WeaponId` → `EquipTableId` 로 변경
   - 기존 행의 값은 NpcEquipTable의 Id로 다시 입력(또는 0 = 무장 없음)
2. **NpcEquipTable 시트** 신설
   - 컬럼 A=Id, B=WeaponRight … M=EarringLeft (12 슬롯), N=Note(설명)
   - `DownloadTables.cs`의 `Register<NpcEquipTable>(list, "0000000000&range=A:N")`에 신규 시트 gid 입력
3. **Download Table** 메뉴로 `NpcTable.bytes`, `NpcEquipTable.bytes` 갱신

## 7. 컴파일 영향

**데이터/시드 패스**
- 변경 파일: `Tables.cs`, `GlobalEnum.cs`, `NpcSaveData.cs`, `DataManager_Table.cs`, `DownloadTables.cs`, `EntityFactory.cs`, `NpcManager.cs`
- 신규 파일: `Assets/Scripts/Npc/NpcEquipHelper.cs`
- `NpcTable.WeaponId` 사용처는 코드에 없었음 — 컴파일 깨짐 없음.
- `MonsterTable.WeaponId`는 별도 컬럼으로 그대로 유지(이 작업과 무관).

**UI 패스**
- 변경 파일: `PlayerNearbyServicesComponent.cs`, `System_VillageServiceProximity.cs`, `System_Input.cs`, `ServiceUIRouter.cs`, `UIManager.cs`(AddressablePath), ~~`UIInn.cs`(방문자 카드에 "장비" 버튼)~~ (SP5에서 방문자 UI 제거 — UIInn은 휴식/세이브 전용으로 축소)
- 신규 파일: `Assets/Scripts/UI/UINpcInspect.cs`, `Assets/UI/NpcInspect/NpcInspect.uxml`, `Assets/UI/NpcInspect/NpcInspect.uss`

## 8. UI — NPC 검사 패널 (UINpcInspect)

### 8.1 진입점 — Interact 키(E) 거리 통일 라우팅
기존 시설 라우터(ServiceUIRouter)를 거리 비교 통일 구조로 리팩토링했다.

- [System_VillageServiceProximity](../../Assets/Scripts/Common/System/System_VillageServiceProximity.cs) 0.3s마다 갱신
  - 시설 후보 거리(`Nearest*DistanceSqr`)도 `PlayerNearbyServicesComponent`에 저장
  - 끝에 `AR.s.Npc.FindNearestActiveNpc(playerPos, 1.5f tile)` 추가 — 마을 안/밖 무관
- [ServiceUIRouter.Open](../../Assets/Scripts/Village/ServiceUIRouter.cs)
  - 후보(Shop/Forge/Inn/Shrine/NPC) 중 sqr 거리 최소 선택
  - 기존 "Shop 항상 우선" 동작이 거리 기반으로 통일됨 (의도된 변화)
- NPC 임계 거리: **1.5 tile** (시설 임계 3~5보다 짧음 — 시설 옆 우연 매칭 방지)

### 8.2 NPC 거리 검색 — 청크 인덱스 활용
[NpcManager.FindNearestActiveNpc](../../Assets/Scripts/Manager/NpcManager.cs):
- 플레이어 청크 + 인접 8청크의 `_chunkNpcs`만 순회
- `IsActive==false`(~~Inn 방문자~~ → 현행: 비활성 NPC), `Condition==Dead` 제외
- NPC 수와 무관하게 클릭당 평균 ~45회 거리 계산

### 8.3 UI 구조 (UI Toolkit)
- [UINpcInspect.cs](../../Assets/Scripts/UI/UINpcInspect.cs) — `UIBaseForm`, `UIDocument` 기반
- [NpcInspect.uxml](../../Assets/UI/NpcInspect/NpcInspect.uxml) — 좌(장비/인벤) | 우(성향/스탯 ScrollView)
- [NpcInspect.uss](../../Assets/UI/NpcInspect/NpcInspect.uss) — Inn UI 톤과 통일

표시 정보(풀 노출):
- 이름, 직업, Description
- 장비 12슬롯 (`NpcSaveData.InventoryEquip`)
- 인벤토리 5칸 (`NpcSaveData.Inventory`)
- 성향 8종 (`NpcStatComponent`) — ECS 활성 NPC만 표시
- 전투 스탯 풀 — `StatComponent`의 모든 `Final*` 필드 (HP, 마나, 공격력 5종, 방어/저항, 회피/막기/생명력 흡수 등)

슬롯 동작(읽기 전용):
- 호버 → `AR.s.Tooltip.Show(item, rect)` (기존 UIShopMerchant와 같은 패턴)
- 클릭 무시 — 변경 불가 모드

### 8.4 진입 경로
1. **월드에서 E키**: ServiceUIRouter → 시설보다 NPC가 가까우면 NPC 패널 (`UINpcInspect.Bind(entityId)`)
2. ~~**Inn 방문자 카드의 "장비" 버튼**: 방문자는 월드에 없어 entityId 검색이 안 되므로 saveData를 직접 주입 (`UINpcInspect.BindSaveData(saveData)`). 단, 방문자는 ECS 컴포넌트가 없어 성향/전투 스탯은 표시 안 됨(장비/인벤만)~~ — **삭제됨** (2026-07-08, SP5 방문자 시스템 제거. `BindSaveData`도 코드에서 제거됨. 현행 진입 경로는 1번뿐)

### 8.5 Unity 측 추가 작업 (사용자 수동)
- `Assets/UI/NpcInspect/NpcInspect.prefab` 생성 — `UIDocument` 컴포넌트 + `UINpcInspect` MonoBehaviour
- UIDocument의 Source Asset → `NpcInspect.uxml` 연결
- Addressable 그룹에 등록 → 키 `UI/NpcInspect` ([AddressablePath.NpcInspect](../../Assets/Scripts/Manager/UIManager.cs))

## 9. 미구현/후속 작업

- **무기 시각 반영**: 플레이어의 무기 SpriteLibrary 갱신 경로를 NPC에도 적용해야 무기가 시각적으로 보임. 1차 패스에선 스탯 적용까지만.
- **NPC 사망 시 드랍**: 보유 장비를 떨어뜨릴지 여부는 별도 결정 필요 (현재는 `DropComponent` 기반 드랍만).
- **NPC 장비 변경 UI**: 현재 읽기 전용. 플레이어가 NPC 장비를 갈아입히는 기능은 별도 패스.
- **세이브 용량**: 장비 compact 표현은 2026-10-03 기본 아이템 행 번호 저장(D122)으로 해결됐다. 남은 증가 요인은 가방 전리품(무작위 옵션 장비)과 소문이다 (§9). ([SAVE_PARTITION_DESIGN](../tech/SAVE_PARTITION_DESIGN.md))

## 9. 저장 공간 추정

> **2026-10-03 실측으로 대체** ([NPC_SHOP_EQUIPMENT_DESIGN](NPC_SHOP_EQUIPMENT_DESIGN.md) §11). 아래 과거 추정 표는 무작위 옵션 장비 기준이라 폐기한다.

| 항목 | 아이템 1개 | 실측 (생존 약 2,390명, 약 57게임시간 세이브) |
|---|---|---|
| 무작위 옵션 장비 (교체 전 장착, 지금도 가방 전리품) | 약 560B (계산 캐시 제외 뒤 약 500B) | 장착 10,892개 = 7.9MB |
| 기본 아이템 (현행 장착) | 62B — `{"Id","ItemInstanceId","Equipment":{"BasicItemId"}}` | 장착 약 10,900개 ≈ 0.7MB |
| NPC 상태 전체 (32샤드) | — | 10.71MB → 2.85MB |

- 저장하지 않는 것: 기본 아이템의 옵션·품질, 모든 장비의 `WeaponStats`·`EquipType`(계산값), null 인스턴스 데이터·Quantity 1·장비 안의 중복 Id·빈 고유 강화 목록, 활동 상태의 기본값 필드.
- 현행 세이브는 NPC 개별 상태를 `EntityId & 31` 기준 32개 파일로 나눈다.
- 장기 증가: 30일 시뮬 끝 NPC 데이터 약 11MB — 대부분 가방 전리품(매입 거절로 가방이 참)과 소문이다. 마을 골드 밸런스에서 다룬다.
