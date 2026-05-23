# NPC 장비/인벤토리 시스템 설계

## 1. 목표

NPC에게 장비 슬롯 12칸 + 인벤토리 5칸을 부여한다. 장비 스탯은 플레이어와 동일한 경로로 `StatComponent`에 반영된다. 청크 비활성/활성 사이클을 거쳐도 데이터가 영속화된다.

## 2. 데이터 모델

### 2.1 [NpcSaveData](../Assets/Scripts/Npc/NpcSaveData.cs)

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

### 2.2 [GlobalEnum](../Assets/Scripts/Common/GlobalEnum.cs)

```csharp
public const int NPC_INVENTORY_SLOTCOUNT_MAX = 5;
```

## 3. 테이블

### 3.1 NpcTable 변경 ([Tables.cs](../Assets/Scripts/Common/Tables.cs))

- **제거**: `WeaponId` (코드 어디서도 사용 안 되던 유물 컬럼)
- **추가**: `EquipTableId` — `NpcEquipTable.Id` 참조. 0 = 무장 없음

### 3.2 NpcEquipTable (신규, Option A — 고정 세트)

```
Id | WeaponRight | WeaponLeft | Helmet | Armor | Gloves | Boots | Necklace | RingRight | RingLeft | Belt | EarringRight | EarringLeft
```

12개 슬롯 각각에 `ItemTable.Id` 명시. 슬롯이 0이면 비움.

`GetItemId(EquipSlotType slot)` 메서드로 슬롯 → ItemId 조회.

### 3.3 인스턴스 생성 정책

NPC 신규 생성 시 슬롯별 ItemId로 `ItemManager.CreateInventoryItemData(itemId)`를 호출 — **Base + Implicit + Prefix(1~2) + Postfix(1~2)** 가 모두 굴려진 인스턴스가 만들어진다. NPC마다 같은 EquipTableId라도 옵션이 다르게 굴려진다.

`CreateBaseEquipment`(=Prefix/Postfix 없는 깔끔한 장비) 아니라 `CreateInventoryItemData` 쪽을 쓰는 이유: 일반 드랍/상점과 동일한 톤을 유지하고, NpcStatComponent의 인스턴스 다양성 철학과 일관.

## 4. 시드 흐름

### 4.1 신규 생성 (3 경로 — [NpcManager](../Assets/Scripts/Manager/NpcManager.cs))

| 경로 | 위치 |
|---|---|
| 맵 파일 로드 | `RegisterNpcsFromMapFile` |
| 런타임 신규 스폰 | `SpawnNewNpc` |
| 여관 방문자 등록 | `SpawnVisitorNpc` |

모든 경로에서 `new NpcSaveData(...)` 직후 `SeedNpcEquipment(saveData)` 호출.

`SeedNpcEquipment` → `NpcEquipHelper.SeedInitialEquipment(saveData, npcTable.EquipTableId)`:
- `NpcEquipTable` 조회 → 슬롯별 ItemId → `CreateInventoryItemData(itemId)` → `saveData.InventoryEquip[slot]`에 채움.
- 이미 슬롯에 데이터가 있으면 덮어쓰지 않음(재호출 안전).

### 4.2 Load 경로

`NpcManager.Load`에서 saveData 복원 시 `saveData.OnLoadCompleted()` 호출 — `ItemData.Table`/`Equipment.Mods[i].Table` 등 `[NonSerialized]` 테이블 참조를 다시 잇는다.

**Load 경로에서는 SeedNpcEquipment를 호출하지 않는다** — 기존 장비 그대로 보존.

## 5. ECS 적용

### 5.1 [EntityFactory.CreateNpc](../Assets/Scripts/Factory/EntityFactory.cs) 시그니처 확장

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

## 6. 시트 작업 (외부 — 이 코드 작업에서 처리 안 됨)

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
- 변경 파일: `PlayerNearbyServicesComponent.cs`, `System_VillageServiceProximity.cs`, `System_Input.cs`, `ServiceUIRouter.cs`, `UIManager.cs`(AddressablePath), `UIInn.cs`(방문자 카드에 "장비" 버튼)
- 신규 파일: `Assets/Scripts/UI/UINpcInspect.cs`, `Assets/UI/NpcInspect/NpcInspect.uxml`, `Assets/UI/NpcInspect/NpcInspect.uss`

## 8. UI — NPC 검사 패널 (UINpcInspect)

### 8.1 진입점 — Interact 키(E) 거리 통일 라우팅
기존 시설 라우터(ServiceUIRouter)를 거리 비교 통일 구조로 리팩토링했다.

- [System_VillageServiceProximity](../Assets/Scripts/Common/System/System_VillageServiceProximity.cs) 0.3s마다 갱신
  - 시설 후보 거리(`Nearest*DistanceSqr`)도 `PlayerNearbyServicesComponent`에 저장
  - 끝에 `AR.s.Npc.FindNearestActiveNpc(playerPos, 1.5f tile)` 추가 — 마을 안/밖 무관
- [ServiceUIRouter.Open](../Assets/Scripts/Village/ServiceUIRouter.cs)
  - 후보(Shop/Forge/Inn/Shrine/NPC) 중 sqr 거리 최소 선택
  - 기존 "Shop 항상 우선" 동작이 거리 기반으로 통일됨 (의도된 변화)
- NPC 임계 거리: **1.5 tile** (시설 임계 3~5보다 짧음 — 시설 옆 우연 매칭 방지)

### 8.2 NPC 거리 검색 — 청크 인덱스 활용
[NpcManager.FindNearestActiveNpc](../Assets/Scripts/Manager/NpcManager.cs):
- 플레이어 청크 + 인접 8청크의 `_chunkNpcs`만 순회
- `IsActive==false`(Inn 방문자), `Condition==Dead` 제외
- NPC 수와 무관하게 클릭당 평균 ~45회 거리 계산

### 8.3 UI 구조 (UI Toolkit)
- [UINpcInspect.cs](../Assets/Scripts/UI/UINpcInspect.cs) — `UIBaseForm`, `UIDocument` 기반
- [NpcInspect.uxml](../Assets/UI/NpcInspect/NpcInspect.uxml) — 좌(장비/인벤) | 우(성향/스탯 ScrollView)
- [NpcInspect.uss](../Assets/UI/NpcInspect/NpcInspect.uss) — Inn UI 톤과 통일

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
2. **Inn 방문자 카드의 "장비" 버튼**: 방문자는 월드에 없어 entityId 검색이 안 되므로 saveData를 직접 주입 (`UINpcInspect.BindSaveData(saveData)`). 단, 방문자는 ECS 컴포넌트가 없어 성향/전투 스탯은 표시 안 됨(장비/인벤만)

### 8.5 Unity 측 추가 작업 (사용자 수동)
- `Assets/UI/NpcInspect/NpcInspect.prefab` 생성 — `UIDocument` 컴포넌트 + `UINpcInspect` MonoBehaviour
- UIDocument의 Source Asset → `NpcInspect.uxml` 연결
- Addressable 그룹에 등록 → 키 `UI/NpcInspect` ([AddressablePath.NpcInspect](../Assets/Scripts/Manager/UIManager.cs))

## 9. 미구현/후속 작업

- **무기 시각 반영**: 플레이어의 무기 SpriteLibrary 갱신 경로를 NPC에도 적용해야 무기가 시각적으로 보임. 1차 패스에선 스탯 적용까지만.
- **NPC 사망 시 드랍**: 보유 장비를 떨어뜨릴지 여부는 별도 결정 필요 (현재는 `DropComponent` 기반 드랍만).
- **NPC 장비 변경 UI**: 현재 읽기 전용. 플레이어가 NPC 장비를 갈아입히는 기능은 별도 패스.
- **세이브 용량**: 200명 평균 ~4KB/명, 총 ~800KB(JSON minified). 실측 후 필요 시 `NullValueHandling.Ignore` 또는 gzip 압축 검토 (이 작업 범위 밖).

## 9. 저장 공간 추정

| 시나리오 | NPC 1명 | 200명 minified | 200명 gzip(~30%) |
|---|---|---|---|
| Worst (전 슬롯/인벤 가득) | ~7.5 KB | ~1.5 MB | ~450 KB |
| 평균 (무기+방어구 4~5 + 인벤 절반) | ~4 KB | ~800 KB | ~240 KB |
| 최소 (무기 1개) | ~0.5 KB | ~100 KB | ~30 KB |

세이브 포맷 자체는 변경하지 않음. 필요 시 후속 작업.
