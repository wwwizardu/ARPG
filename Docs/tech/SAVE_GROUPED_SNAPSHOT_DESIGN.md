# 저장 스냅샷 묶음별 갱신 인계 문서

> **작성일**: 2026-10-05
>
> **상태 (2026-10-05)**: 12종 논리 파티션에 묶음 갱신과 저장 배치 동결을 구현했다. 기존 Editor 계약·실제 writer/reader 진단을 확장했고, 핵심 검사 2,188개를 통과했다. 적용 내용·실측·남은 검증은 §8에 기록한다.
>
> **목적**: 전체 저장 파티션의 묶음별 스냅샷 계약과 적용 상태, 검증 결과를 유지한다. 아래 설계 기준은 구현의 책임·검증 기준으로 계속 사용한다.

사용자는 마을 골드 하나가 바뀌었다는 이유로 마을 전체를 다시 복사하는 비용을 줄이기를 원한다. 같은 접근을 다른 저장 파일에도 적용한다. 기본 방향은 **변경된 항목과 묶음을 표시하고 기존 저장용 캐시에 반영하되, 백그라운드 저장이 읽는 캐시는 수정하지 않고 변경 표시만 누적하는 방식**이다.

이 문서는 메모리 캡처와 갱신의 변경을 다룬다. 파일 파티션, JSON 스키마, NPC 32샤드, manifest 원자 커밋과 증분 파일 쓰기는 유지한다. 필드별 변경을 파일에 직접 덧쓰는 패치 저장이나 이벤트 로그 저장으로 바꾸는 작업은 포함하지 않는다.

## 0 새 세션에서 시작하는 방법

1. `AGENTS.md`와 이 문서를 읽고, §8의 진행 상태에서 이어서 작업한다.
2. 설계·책임을 확인할 때는 AGENTS.md 순서대로 관련 Wiki를 먼저 읽는다. 이후 §1의 공통 코드와 기존 정본의 관련 절을 읽고, 도메인 구현을 시작할 때 §3의 해당 데이터 파일과 소유 매니저를 추가로 읽는다.
3. 기존의 전체 깊은 복사 경로를 비교 기준으로 남기고, §2의 동결 계약부터 구현한다. 이후 §5 순서대로 전체 범위에 적용한다.
4. 요청 범위는 **12종 논리 파티션 전체**다. 마을만 구현한 상태를 전체 완료로 보고하지 않는다. 작은 레코드는 §3처럼 단일 묶음으로 유지해도 적용 범위에 포함된다.
5. 구현 후 §6의 검증을 실행하고 §8에 결과와 미완료 항목을 기록한다. 작업이 여러 세션에 걸리면 변경 파일, 다음 시작점, 검증 여부를 이 문서에 남긴다.

기존 구현 정본은 [SAVE_PARTITION_DESIGN.md](SAVE_PARTITION_DESIGN.md)와 [SAVE_SNAPSHOT_DESIGN.md](SAVE_SNAPSHOT_DESIGN.md)다. 전자는 파일·커밋·복구·범위 계약을 계속 소유한다. 후자의 S5·S6·S12는 현행 구현 이력이며, 묶음 갱신이 적용된 경로에서는 이 문서의 §2 계약으로 변경한다. 최초 로드·전체 재생성·S13 위치 복원 등 유지할 계약은 §2에 명시한다. 새 계약의 상태를 표시할 때 기존 실측과 검증 결과를 새 방식의 결과로 재사용하지 않는다.

## 1 변경 전 구현과 적용 목표

| 항목 | 변경 전 코드 | 적용한 목표 |
| --- | --- | --- |
| NPC와 마을 변경 표시 | 변경된 ID만 기록 | ID와 변경 묶음, 필요하면 슬롯·청크·오브젝트 식별자도 기록 |
| NPC와 마을 캐시 갱신 | 변경된 항목 전체를 깊은 복사해 교체 | 기존 캐시에 변경 묶음만 반영 |
| 저장 중 프레임 갱신 | 이전 DTO를 수정하지 않고 새 DTO로 교체 | 저장이 읽는 캐시 수정을 보류하고 변경 표시를 누적 |
| 사회 관계·파티·인구·정착지 | 항목별 캐시 사용 | 작은 항목은 그대로 유지하고 필요한 큰 목록만 분리 |
| Player·World·Map·Buildings·Chronicle·Dungeons | 저장 시점 깊은 복사 | 각 소유자의 수명을 따르는 캐시를 추가 |
| 저장 범위와 파일 쓰기 | Simulation 여섯 파트 결합, 세대·내용 비교로 파일 재사용 | 유지 |

[src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Village/VillageManager_SaveSnapshot.cs, Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs]

공통 코드 앵커는 다음과 같다. 새 범용 매니저를 만들기 전에 기존 책임 안에서 확장한다.

| 코드 | 확인할 책임 |
| --- | --- |
| `Assets/Scripts/Data/SaveDtoSnapshot.cs` | 저장 필드 선택, 재귀 복사, 직렬화 조건과 기본값 생략 |
| `Assets/Scripts/Data/SaveSnapshotCache.cs` | 변경·실패 목록, 캐시와 세대, 명시적 진단 |
| `Assets/Scripts/Data/SavePartitionData.cs` | 파티션 루트, SaveScope와 Simulation 결합 |
| `Assets/Scripts/Manager/DataManager_PartitionedSave.cs` | 캡처, 저장 큐, worker, manifest 커밋, 증분 세대와 Reset |
| `Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs` | Priority 990 프레임 끝 갱신 |
| `Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs` | 기존 캐시·저장 왕복·증분·장애 진단 |
| `Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs` | 저장·복구·던전 생성 진단 |

## 2 저장 중 동결과 갱신 계약

### 2.1 메모리 소유권

- `VillageData`, `NpcSaveData`, `PlayerData` 등은 게임이 수정하는 현재 데이터다. 저장용 캐시는 별도의 사전·목록·값으로 유지한다. 런타임의 변경 가능한 사전·목록을 캐시에 직접 연결하지 않는다.
- 저장이 읽지 않는 동안에는 캐시의 기존 최상위 사전과 변경된 묶음을 수정할 수 있다. 골드 갱신 때문에 마을 전체 복사본이나 최상위 사전을 매번 새로 만들 필요는 없다.
- 변경된 목록·사전·아이템을 갱신할 때는 해당 부분의 독립된 저장 표현이 필요하다. 여기서 생기는 작은 복사와 할당까지 모두 없애는 목표는 아니다.
- 저장 배치마다 만드는 루트·목록·샤드 컨테이너는 유지할 수 있다. 핵심은 그 아래의 큰 항목 전체를 반복 깊은 복사하지 않는 것이다.
- mutable 캐시와 동결 상태는 소유 매니저와 DataManager 인스턴스가 관리한다. persistent mutable static 상태를 추가하지 않는다.

### 2.2 저장 배치의 순서

1. 저장 큐와 기존 I/O gate에서 이번 배치의 순서를 확보한다.
2. 기존 정책대로 SaveScope를 확장하고, ECS 되쓰기·던전 체크포인트 등 현재 데이터 캡처를 실행한다.
3. **메인 스레드에서 await 없이** 이번 범위의 변경 묶음을 모두 반영한다. 실패 묶음을 재시도하고, 불완전하면 저장을 중단한다.
4. 저장 루트를 조립하고 이번 배치의 캐시 인스턴스, 수명, 캡처 세대를 고정한다.
5. worker가 참조할 캐시를 동결한 뒤 백그라운드 JSON 변환·파일 쓰기를 시작한다.
6. 저장 중 게임은 현재 데이터를 계속 수정한다. `MarkSaveDirty`는 ID·묶음을 계속 누적하지만, 프레임 갱신은 동결된 캐시를 수정하지 않는다.
7. worker가 데이터를 더 이상 읽지 않는 것을 확인한 뒤 성공·실패·취소 모든 경로에서 해당 배치의 동결을 해제한다. 커밋과 증분 세대 확정은 기존 계약을 따른다.
8. 누적된 변경은 이후 프레임에서 반영한다. 다음 저장이 먼저 오면 그 저장 직전까지 모두 반영한다.

현재 `IsSaving`은 캡처 전에도 true이고, 추가 저장 요청의 큐 대기를 담당한다. 따라서 모든 Flush에 단순히 `if (IsSaving) return`을 붙이면 저장 직전 필수 갱신까지 막는다. **캡처 단계와 worker가 읽는 단계를 구분**해야 한다. 기본 동결 범위는 이번 배치가 실제로 참조하는 캐시다. 요청되지 않은 캐시는 worker와 공유하지 않는 경우 계속 갱신할 수 있다.

[src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs `SavePartitionedAsync`, `RunSaveQueueAsync`, `WritePartitionSnapshotAsync`, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs]

### 2.3 변경 표시와 갱신

- 변경 ID별 묶음 비트마스크를 누적한다. 같은 항목의 여러 묶음은 OR로 합치며 한 묶음을 여러 번 표시해도 한 번만 갱신한다.
- 변경 함수를 통해 실제 저장 상태가 바뀌는 지점을 표시한다. 대부분 읽기만 하는 순회·판정 함수는 표시하지 않는다. async 함수의 await 뒤에서 추가로 바뀌는 상태도 표시한다.
- 신규 항목은 전체 묶음을 초기 캡처한다. 삭제는 캐시 제거로 처리하고, 목록 재정렬·슬롯 비움·청크 삭제도 명시적으로 반영한다.
- API 전환 기간의 기존 `MarkSaveDirty(id)`는 전체 묶음 표시로 동작하게 해 누락을 방지한다. 최종적으로 뜨거운 경로는 구체적 묶음을 표시하도록 전환하고, 전체 표시가 남은 이유를 기록한다.
- 반영은 표시된 필드에만 접근해 캡처한다. `Capture(항목 전체)` 후 필요한 필드만 뽑는 구현은 큰 복사 비용을 그대로 남기므로 사용하지 않는다.
- 한 묶음의 캡처 결과를 먼저 준비하고 성공한 뒤 적용한다. 실패한 묶음은 실패 상태로 남겨 저장을 차단하며, 다른 묶음과 다른 항목의 갱신은 계속할 수 있다.
- 저장 중 쌓인 변경을 완료 직후 무조건 전수 반영하지 않는다. 프레임별 예산으로 나눌 수 있지만 저장 직전에는 남은 변경을 모두 반영한다.

### 2.4 직렬화와 증분 세대

- `SaveDtoSnapshot`의 `JsonIgnore`, `JsonProperty` 이름, `ShouldSerialize`, 기본값·null 생략, converter 거부, 순환 참조 거부 규칙을 유지한다. 선택된 필드가 기본값이나 생략 조건으로 돌아가면 캐시의 이전 키도 제거해야 한다.
- 저장 조건이 연결된 필드들은 함께 캡처한다. 예를 들어 기본 장비를 일반 장비로 바꿀 때 `BasicItemId`와 Mods·Quality의 저장 형태가 함께 바뀐다.
- 기존 목록·사전·필드 순서를 유지한다. 생략된 필드를 재추가할 때 단순히 사전 끝에 넣어 JSON 순서가 바뀌지 않게 한다. 동일 상태의 기존/신규 JSON 바이트 동등성과 증분 해시 재사용을 검증한다.
- 프레임 변경 목록과 디스크 확정 세대는 분리한다. **이번 배치가 캡처한 세대만 성공으로 확정**하고 저장 중 새 변경을 확정하거나 지우지 않는다. 실패한 저장은 다음 요청의 파일 재사용 판단에서 성공으로 취급하지 않는다.
- 목록의 멤버십·순서 변경을 값 수정과 따로 추적하는 세대는 두지 않는다. 저장 루트는 저장할 때마다 현재 원장 순서대로 다시 조립하므로 추가·삭제·순서 변경은 조립된 루트에 그대로 반영된다. 저장 파일에는 현재 전체 상태를 직렬화한다. 메모리 일부만 갱신해도 변경된 파일은 기존 writer가 전체 JSON으로 쓴다.

[src: Assets/Scripts/Data/SaveDtoSnapshot.cs, Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `SelectIncrementalSnapshotParts`, Docs/tech/SAVE_SNAPSHOT_DESIGN.md §13]

### 2.5 수명과 유지할 계약

- Reset/Load가 worker 실행과 겹쳐도 worker가 읽는 옛 DTO 내부를 Clear·수정하지 않는다. 새 수명의 캐시를 준비하고, 옛 배치가 옛 캐시만 해제하도록 인스턴스와 수명을 연결한다. 옛 worker는 새 수명에 커밋할 수 없다.
- 캐시를 넘겨받은 객체가 영구 불변이라는 기존 계약은 변경된다. 저장 배치가 동결을 유지하는 동안의 불변성을 보장한다. 진단이 완료 후에도 옛 루트를 붙잡고 영구 불변을 기대하는 검사는 새 계약에 맞춰 수정한다.
- 수동·최초·이관 전체 저장과 던전 입장·퇴장·완료의 전체 재생성 정책은 우선 유지한다. 재생성은 동결되지 않은 캡처 단계에서 실행한다. 재생성 비용까지 제거하는 별도 정책 변경을 섞지 않는다.
- 최초/이관의 `captureRuntimeManagers=false`와 전환 스위치 OFF 경로는 기존 깊은 복사를 유지한다.
- Simulation 여섯 파트의 공동 캡처·커밋, 미저장 NPC 던전 완료의 Dungeons 결합, NPC State/Social 기억 정합성, S13 이동 위치 재계산을 유지한다.
- 던전 동행 NPC의 던전 좌표를 월드 저장 위치에 반영하지 않는 규칙과 NPC HP를 새로 저장하지 않는 현행 범위를 유지한다.

[src: Docs/tech/SAVE_PARTITION_DESIGN.md §5, Docs/tech/SAVE_SNAPSHOT_DESIGN.md S8·S9·S12·S13, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `ResetPartitionedSaveState`, Docs/map/DUNGEON_RUNTIME_DESIGN.md §5.3]

## 3 파티션별 묶음 구조

다음 표의 묶음 이름은 구현 기본안이다. 물리 JSON에 새 그룹 객체를 추가하거나 현재 데이터 클래스를 분해할 필요는 없다. 모든 루트의 `Version` 같은 스키마 정보는 유지하고 초기화·이관 때 갱신한다. `[JsonIgnore]`와 런타임 계산 캐시는 대상에서 제외한다.

### 3.1 Player

소유 항목은 `PlayerId`다. 플레이어–NPC 관계는 저장 루트 안에서 `NpcId`별로 별도 관리한다.

| 묶음 | 저장 대상 |
| --- | --- |
| 기본 정보 | `Version`, `PlayerId`, `PlayerTableId` |
| 성장과 지갑 | `Level`, `Exp`, `Gold` |
| 생명과 마나 | `CurrentHp`, `CurrentMp` |
| 위치와 맵 전환 | `PositionX`, `PositionZ`, `CurrentMapKind`, `ActiveDungeonId`, `DungeonPositionX/Y`, `WorldReturnX/Y` |
| 인벤토리 | `_inventory`의 변경된 슬롯과 목록 구성 |
| 장착 장비 | `_inventoryEquip`의 변경된 슬롯 |
| 장착 스킬북 | `_skillBookSlots`의 변경된 슬롯과 책 내부 변경 |
| 플레이어 NPC 관계 | `NpcRelations`의 변경된 `NpcId` 항목 전체 |
| 루트 선택 | `CurrentPlayerIndex`, Players의 추가·삭제·순서 |

PlayerData 목록은 DataManager, ECS 되쓰기는 PlayerManager, 플레이어–NPC 관계는 NpcManager의 기존 소유권을 따른다. 위치·맵 전환 필드는 함께 반영한다. 슬롯 안 아이템을 변경한 경우 슬롯 참조가 같아도 표시해야 한다.

[src: Assets/Scripts/Data/PlayerData.cs, Assets/Scripts/Data/SavePartitionData.cs `PlayerSaveRoot`, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `CapturePendingParts`, Assets/Scripts/Npc/NpcSaveData.cs `PlayerNpcRelationEntry`]

### 3.2 World

작은 루트이므로 세 묶음으로 관리한다.

| 묶음 | 저장 대상 |
| --- | --- |
| 월드 메타 | `WorldDataVersion`, `WorldSeed`, `ElapsedWorldGameHours`, `NextItemInstanceId` |
| 동행 | `NpcCompanion.ReservedNpcId`, `IsInDungeon` |
| 물가 | `GoldPrice.BaselineWorldGold`, `Index`, `LastUpdateDay` |

월드 메타는 숫자 몇 개를 함께 갱신해도 된다. 임시 동행은 NpcSaveData로 옮기지 않는다.

[src: Assets/Scripts/Data/SavePartitionData.cs `WorldSaveRoot`, Assets/Scripts/Npc/NpcCompanionState.cs, Assets/Scripts/Village/GoldPriceRules.cs]

### 3.3 Map

| 묶음 | 항목 단위 | 저장 대상 |
| --- | --- | --- |
| 월드 드랍 | 드랍 변경·추가·삭제 | `WorldDropItems`의 위치와 `ItemData` |
| 타일 변경 | `ChunkX`, `ChunkY` | 해당 청크의 `Modifications`와 목록 구성 |

드랍 레코드에는 별도 DropId가 없다. 현재 항목의 소유와 추가·제거 경계를 먼저 조사하고, 런타임 추적 키나 변경 순번으로 처리한다. 검증 없이 `ItemInstanceId`가 모든 드랍의 고유 키라고 가정하지 않는다. 이 작업을 위해 새 영속 ID 필드를 추가하는 스키마 변경은 범위에 포함하지 않는다.

[src: Assets/Scripts/Data/SavePartitionData.cs `MapSaveRoot`, Assets/Scripts/Data/WorldData.cs `WorldDropItemData`, Assets/Scripts/Map/TileModification.cs]

### 3.4 Villages

항목 키는 `VillageId`다.

| 묶음 | 저장 대상 |
| --- | --- |
| 기본 정보 | `VillageId`, `TableId`, `SettlementId`, `VillageProfileId`, `PositionX/Y`, `RegisteredAt` |
| 인구와 마을 상태 | `Population`, `FixedPopulation`, `Stage`, `BoundsX/Y/W/H`, `ThreatLevel`, `HasBeenPopulated` |
| 자원과 생산 | `Resources`, `ResourceCaps`, 모든 `*Carry`·`*Timer`, `HungerHoursAccumulated` |
| 진행 중 건설 | `ActiveBuildTasks`, `LastConstructionSimGameTime`, `LastConstructionTierCheckGameTime`, 호환용 `CurrentBuild*`, `FirstBuild*` |
| 시설과 완공 원장 | `PlacedObjects`, `PlacedObjectTypeIds`, `InitialDevelopmentApplied`, `AppliedGrowthMilestones`, `HasCampfire` |
| 벽 | `WallSegments`, `WallSegmentCount`, `CompletedWallSegments`, `WallPlanRequested` |
| 상점 | `MerchantStock`, `ShopTier`, `LastMerchantRollGameTime`, `MerchantRestockCycle`, `NpcPurchaseCycle`, `NpcPurchaseCount`, `LastNpcTradeGameTime` |

자원 Carry에는 금고 유지비 `VaultUpkeepCarry`도 포함한다. 시설 항목과 벽 세그먼트의 수정은 변경된 항목만 갱신할 수 있다. 시설 배치 목록에는 아직 BuildingEntityId가 없는 항목도 있으므로 이 ID 하나로 모든 항목을 키 지정하지 않는다. 목록의 삽입·삭제·순서 변경은 별도 처리한다.

[src: Assets/Scripts/Village/VillageData.cs, Assets/Scripts/Village/PlacedObjectSaveData.cs, Assets/Scripts/Village/WallSegmentSaveData.cs, Assets/Scripts/Village/MerchantStockEntry.cs, Assets/Scripts/Village/BuildTaskSnapshot.cs]

### 3.5 Buildings

항목 키는 BuildingManager의 건물 Entity ID다. **건물 한 개 전체를 단일 묶음으로 유지**한다. 저장 필드는 `TableId`, `VillageId`, `WorldTileX/Y`, `CurrentHp`, `IsUnderConstruction`, `EntityId`, `IsActive`, `FacilityTier`다. 작은 레코드이므로 내부 분할보다 변경 건물만 갱신하는 편이 적합하다.

시설 Tier·HP 등이 Village의 완공 원장과 함께 바뀌면 두 캐시와 해당 저장 범위를 함께 반영한다.

[src: Assets/Scripts/Village/BuildingSaveData.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `CapturePendingParts`]

### 3.6 NPC State 32샤드

항목 키는 영구 `EntityId`다. 샤드는 기존 `entityId & 31`을 유지한다.

| 묶음 | 저장 대상 |
| --- | --- |
| 신원과 성향 | `NpcTableId`, `EntityId`, `Name`, `Description`, `JobType`, `SkillLevel`, `NpcStat`, `InitialPower`, `PowerCapBonus` |
| 위치와 소속 | `PositionX/Y`, `VillageId`, `IsActive`, `AbandonedSiteSinceGameTime` |
| 상태와 부상 | `Condition`, `WoundCount`, `LastWoundGameTime`, `LastWoundActivity` |
| 활동 | `Activity` 전체, `LastSimGameTime` |
| 지갑과 거래 | `Gold`, `LastTradeGameTime` |
| 인벤토리 | `Inventory`의 변경된 슬롯 |
| 장착 장비 | `InventoryEquip`의 변경된 슬롯 |
| 소문 | `Rumors` |
| 절도 | `LastTheftGameTime`, `TheftClues` |
| 사건 이력 | `Deeds` |
| 관계 기억과 감정 | `DeceasedRelations`, `SharedActivityMemories`, `Sentiments` |
| 최신 개인 사연 | `PersonalStories`의 저장 대상 필드 |

`Activity`의 PartyId·종류·시작/종료 시각·경로·난수 기준·결과는 함께 캡처한다. S13의 파생 위치 갱신만으로 위치 묶음을 표시하지 않는다. `PersonalStories`의 아이템·던전 완료·사별 참조 인덱스는 JsonIgnore이므로 새 저장 필드로 추가하지 않는다. 부상·절도·동료 기억과 사연이 함께 바뀌면 해당 묶음들을 함께 표시한다.

[src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Npc/NpcActivityState.cs, Docs/tech/SAVE_SNAPSHOT_DESIGN.md S13]

### 3.7 NPC Social

| 묶음 | 항목 단위 | 저장 대상 |
| --- | --- | --- |
| 호감도와 공동 활동 | 정규화된 `NpcPairKey` | `NpcA`, `NpcB`, `Affinity`, `SharedActivities` 전체 |
| 유형 관계 | `FromEntityId` | 해당 From의 저장 관계 목록 |

현재 항목별 캐시를 유지한다. 작은 호감도 엔트리 내부까지 분할하지 않는다. 사망으로 활성 쌍을 지울 때 State의 과거 동료 기억·사연 갱신을 함께 표시한다. 대칭 관계의 중복 저장 방지와 기존 순서를 유지한다.

[src: Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/Npc/NpcSaveData.cs `NpcAffinityEntry`, Assets/Scripts/Npc/NpcRelationEntry.cs]

### 3.8 NPC Parties

항목 키는 `PartyId`다.

| 묶음 | 저장 대상 |
| --- | --- |
| 파티 구성 | `PartyId`, `LeaderId`, `MemberIds` |
| 공통 활동 | `Activity` 전체 |

가입·탈퇴·파티 해산은 관련 NPC의 `Activity.PartyId`와 함께 표시한다. 공통 이동 경로의 시간축은 파티가 소유하는 현재 계약을 유지한다.

[src: Assets/Scripts/Npc/NpcPartyState.cs, Assets/Scripts/Npc/NpcActivityState.cs]

### 3.9 NPC Meta

**인구 관리 전체를 단일 묶음으로 유지**한다. `TargetNpcCount`, `NextReplenishGameTime`, `ReplenishVillageCursor`, `NeedsImmediateTargetTopUp` 네 필드다. 생성·보충으로 State 항목이 추가될 때 메타도 함께 표시한다.

[src: Assets/Scripts/Npc/NpcPopulationState.cs]

### 3.10 World Sim

| 묶음 | 항목 단위 | 저장 대상 |
| --- | --- | --- |
| 루트 정보 | 루트 | Version, 배열 구성에 따른 `IsInitialized` 저장 표현 |
| 정착지 상태 | 기존 고정 배열 인덱스 | `SettlementLiveState` 전체 |

정착지 상태는 ID·종족·바이옴·좌표·인구·왕국·Fate·건국 연도·시뮬 시각·실체화·마을 연결의 작은 레코드다. 현재 인덱스별 갱신을 유지한다. 인구·Fate·실체화와 Village 원장이 함께 바뀌는 경계는 관련 묶음을 함께 표시한다.

[src: Assets/Scripts/History/Chronicle/WorldSimState.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs]

### 3.11 Chronicle

| 묶음 | 항목 단위 | 저장 대상 |
| --- | --- | --- |
| 연대기 메타 | 루트 | Version, `SimulatedYears` |
| 사건 | 새 EventId 항목 | `Events`의 신규 사건 추가 |
| 정착지 역사 | `SettlementId` | `Settlements`의 해당 역사 스냅샷 |
| 왕국 | `KingdomId` | 해당 왕국 정보와 `MemberSettlementIds` |

Events는 append-only이므로 신규 사건만 저장 캐시에 추가할 수 있다. 역사 종료 스냅샷과 runtime WorldSim을 합치지 않는다. 초기 생성·로드·마이그레이션은 전체 초기 캡처한다.

[src: Assets/Scripts/History/Chronicle/ChronicleSaveData.cs, Assets/Scripts/History/Chronicle/ChronicleEvent.cs, Assets/Scripts/History/Chronicle/SettlementSnapshot.cs, Assets/Scripts/History/Chronicle/KingdomData.cs]

### 3.12 Dungeons

| 묶음 | 항목 단위 | 저장 대상 |
| --- | --- | --- |
| 전체 집계 | 루트 | Version, `CompletedCount` |
| 마을 슬롯 관리 | `VillageId` | SlotCount, LastNpcCompletionGameTime, 슬롯 추가·삭제·순서 |
| 슬롯 상태 | VillageId와 SlotIndex | DungeonSlot 전체의 세대·ID·종류·입구·배치 재시도·NPC 교체 세대 |
| 던전 기본 정보 | `DungeonId` | 소속 마을·슬롯·세대, TableId, Seed, GeneratorVersion, BossRoomId, CompletionGold, ChestGold, ReplacementCooldownHours |
| 고정 레이아웃 | DungeonId | Rooms, Corridors, Objects |
| 완료와 진행 | DungeonId | Completed, KilledObjects, OpenedChests |
| 몬스터 상태 | DungeonId와 오브젝트 ID | Monsters의 해당 HP·위치 레코드 |
| 드랍 | DungeonId 내 변경 항목 | Drops의 추가·수정·삭제 |
| 타일 변경 | DungeonId와 청크 좌표 | 해당 TileModifications |

몬스터 HP·위치 변경 때 고정 레이아웃을 캡처하지 않는다. `CaptureCheckpoint`의 현재 ECS 되쓰기와 변경 세트 수집 경계를 함께 바꾼다. 완료 후 기록 제거·슬롯 세대 전진·총 완료 횟수·보상·플레이어 복귀는 기존 트랜잭션과 저장 검증을 유지한다. 던전별 삭제를 캐시에 반영하지 않아 끝난 기록이 다시 저장되는 일이 없어야 한다.

[src: Assets/Scripts/Data/DungeonSaveData.cs, Docs/map/DUNGEON_RUNTIME_DESIGN.md §5, Assets/Scripts/Manager/DataManager_PartitionedSave.cs `CapturePendingParts`]

### 3.13 공통 아이템과 목록

인벤토리·장비·드랍·상점·스킬북 소켓 안의 `ItemData`는 **아이템 하나 전체**를 기본 갱신 단위로 둔다. 수량·Mods·고유 강화·소켓 페이지를 바꾸면 현재 소유 묶음과 슬롯/항목을 표시한다. 아이템이 이동하면 원래 소유자와 새 소유자를 모두 표시한다. 변경 가능한 런타임 아이템을 캐시에 참조 공유하지 않는다.

아이템 내부의 `EquipmentData` 조건부 저장과 `SocketedPageItems` 직렬화 이름 등 기존 계약을 그대로 사용한다. Table 참조·무기 계산 캐시·JsonIgnore 필드는 복사 대상이 아니다. 모든 목록의 null 슬롯과 순서를 보존한다.

[src: Assets/Scripts/Data/ItemData.cs, Assets/Scripts/Data/SaveDtoSnapshot.cs]

## 4 함께 표시하고 저장할 변경

묶음은 독립적으로 추적하되 하나의 게임 동작이 여러 묶음과 파티션을 바꾸면 전부 표시한다.

| 동작 | 함께 반영할 대상 |
| --- | --- |
| NPC 상점 구매 | NPC 지갑·장비/가방, Village 자원·상점, 변경된 사연·이력 |
| NPC 생활비와 치료 | NPC 지갑·활동/부상·사연, Village 자원 |
| 플레이어 아이템 장착 | Player 인벤토리·장착 슬롯, 필요하면 보정된 HP/MP |
| 플레이어 마법부여 | Player 지갑·재료 슬롯·대상 아이템, 변경된 ItemInstanceId 메타가 있으면 World |
| 파티 가입과 귀환 | Parties 구성/활동, 멤버 State 활동/결과, Social 공동 활동·호감도, 관련 기억·사연 |
| NPC 사망 | State 상태·기억, Social 관계 삭제, Parties 구성, Population 필요 상태, 실제 발생한 Chronicle 사건 |
| 시설 완공과 Tier 변경 | Village 건설·시설·상점/자원 중 변경 부분, 해당 Building, 필요하면 Map 타일 |
| 던전 입퇴장과 완료 | Player 위치·생명/지갑/아이템 중 변경 부분, World 동행, Dungeons 체크포인트·슬롯·집계, 관련 NPC 상태 |

이 표는 변경 경계 점검용이며 발생하지 않은 변경까지 자동 표시할 필요는 없다. 기존 Player/World 요청 범위, Simulation 결합, NPC 던전 완료 추가 결합을 유지한다. Chronicle·Buildings·Map이 실제로 바뀐 동작은 해당 파티션을 명시적으로 요청한다. Simulation에 포함되지 않은 파일이 자동 저장된다고 가정하지 않는다.

## 5 구현 순서

### 5.1 공통 갱신과 저장 수명

- 기존 복사기에서 선택된 저장 필드만 캡처하고 생략된 필드 제거까지 표현하는 기능을 확장한다.
- 소유 캐시의 변경 마스크·세대·실패 묶음과 저장 배치의 동결 수명을 구현한다.
- DataManager의 캡처·worker·finally·Reset/Load·저장 큐에 동결을 연결한다. 독립 DTO인 기존 fallback은 유지한다.
- 기존 진단의 영구 불변 검사를 배치 동결 검사로 바꾸고, 새 수명에 옛 worker가 영향을 주지 않는 검사를 추가한다.

### 5.2 기존 상시 스냅샷 파티션

- Village와 NPC State의 마스크를 각 도메인 변경 경계에 적용한다.
- Parties를 구성·활동으로 나누고 Social·Population·WorldSim은 현재 작은 항목 단위를 유지한다.
- 전체 표시 fallback이 뜨거운 경로에 남아 새 방식이 실제로 전체 복사를 계속하지 않는지 검사한다.
- 복제 시뮬의 캐시 Swap·복원·라이브 해시 검사에 새 마스크·동결·실패·세대를 포함한다.

### 5.3 Player와 World

- 기존 소유 매니저에 캐시를 추가하고 Player 슬롯·아이템 내부 수정·관계·위치 되쓰기 경계를 표시한다.
- World 메타·동행·물가를 분리한다. 읽기용 속성 접근만으로 변경 표시가 발생하지 않도록 한다.

### 5.4 Map과 Buildings와 Chronicle

- 드랍·청크 변경, 건물 Entity ID, 신규 연대기 사건·정착지·왕국별 캐시를 연결한다.
- 추가·삭제·로드·신규 게임·마이그레이션을 모두 포함한다. 문서에 없는 영속 추적 ID를 임의 추가하지 않는다.

### 5.5 Dungeons와 통합

- 슬롯과 방문 기록의 캐시, 고정 레이아웃 분리, 오브젝트·드랍·청크 변경 추적을 구현한다.
- 모든 파티션의 기존/신규 캡처 동등성, 파일 재사용과 원자 커밋을 확인한다.
- §6 검증과 §7 실측 후 완료 상태를 갱신한다. 기존 정본의 적용 상태를 정리하고 Wiki 갱신은 AGENTS.md의 사용자 확인 규칙을 따른다.

## 6 검증 기준

아래 기준을 기존 Editor 진단에 확장했다. 실행 결과는 §8.2에 기록한다. 별도 Unity 프로젝트·테스트 호스트·일회용 검증 스크립트는 추가하지 않았다.

### 6.1 데이터와 묶음 계약

- 모든 파티션에서 동일 현재 데이터의 기존 전체 캡처와 새 캐시 JSON이 같다. 기본값/빈 목록 생략·복귀·재추가·null 슬롯·목록 순서를 포함한다.
- 자원만 변경하면 Village 상점·시설·벽의 깊은 캡처가 실행되지 않는다. NPC 활동 변경은 가방·장비·이력 캡처를 실행하지 않는다. 던전 몬스터 변경은 고정 레이아웃 캡처를 실행하지 않는다.
- 같은 항목의 복수 묶음 표시, 반복 표시, 신규·삭제·삭제 후 재추가, 슬롯 이동, 목록 재정렬과 clear가 모두 반영된다.
- 아이템 객체 참조가 같아도 수량·옵션·소켓 변경이 반영된다. BasicItemId 전환과 저장 조건 변경 후 이전 생략 필드가 올바르게 복구된다.
- 묶음 캡처 실패는 저장을 차단하고 재시도로 복구된다. 실패 시 일부 키만 바뀐 묶음이 writer에 노출되지 않는다.
- 표시 누락 진단은 전수 비교 전에 전체 재생성해 누락을 숨기지 않는다. 동일 묶음의 다음 정상 표시가 이전 누락을 가리지 않도록 기존 관찰자 정책을 확장한다.

### 6.2 저장 중 동작과 파일 계약

- worker를 일시 정지한 동안 런타임 데이터와 변경 표시를 수정한다. 이번 파일은 캡처 시점 상태, 다음 파일은 새 상태를 가진다.
- 저장 중 프레임 Flush·진단 Flush·재생성·큐 요청·삭제가 동결된 컨테이너를 수정하지 않는다.
- 캡처 실패, 직렬화/파일 쓰기/manifest 교체 실패, 취소, Reset/Load 후에도 동결이 남지 않고 미저장 변경이 보존된다.
- Reset 후 옛 worker가 완료돼도 새 캐시의 동결이나 세대를 잘못 해제·확정하지 않는다. 기존 I/O gate와 lifetime 복구도 유지된다.
- 변경 없는 파일 재사용, 변경 파티션만 파일 생성, 저장 중 추가 변경과 큐 병합, previous manifest 전체 복구를 통과한다.
- 최초/이관 fallback, 스위치 OFF, 전체 재생성 요청이 저장 큐에서 유지되는 경로를 검증한다.

기존 메뉴 `ARPG/Save/Diagnostics/Verify Village Snapshot Contracts`, `Verify Simulation Snapshot Contracts`, `Verify Simulation Snapshot Save Round Trip`과 `ARPG/Dungeon/Run Storage and Generation Diagnostics`를 우선 확장한다. 필요하면 기존 저장 진단 안에 Player·Map 등의 검사 범위를 추가한다.

[src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs]

### 6.3 런타임 경계와 빌드

- Runtime/Editor 기존 빌드 경로로 컴파일한다. 프로젝트 루트의 기존 `Assembly-CSharp.csproj`, `Assembly-CSharp-Editor.csproj` 또는 현재 열린 Unity의 컴파일 경로를 사용한다. 새 프로젝트나 빌드 래퍼를 만들지 않는다.
- 상점 거래·생활비·치료·절도·사망·파티 귀환·이주·청크 materialize/fold-back, Player 장착·마법부여·스킬 소켓 변경, 던전 입퇴장·완료·동행·재접속을 확인한다.
- 기존 복제 시뮬의 충분한 기간 표본으로 변경 누락 0과 라이브 상태 복원 해시 동등성을 검사한다. 장기 진단을 완료하지 못하면 기간과 남은 항목을 보고한다.
- 사용자 저장 파일을 덮어쓰지 않는 격리 진단 경로를 사용하고, 작업에서 만든 임시 저장·로그·보고서·빌드 설정만 정리한다. 기존 도구·사용자 변경·공유 Unity 캐시는 보존한다.
- 유효한 현재 프로젝트 검증 경로가 없으면 한계를 보고한다. 검증을 위해 Unity 프로젝트를 복제하지 않는다.

## 7 성능 비교와 완료 판단

변경 전 마을 300개 표본에서는 전체 깊은 복사 63.81~90.17ms, 캐시 루트 조립 0.043~0.052ms였다. 이 비교는 캐시 갱신·JSON·파일 I/O를 제외한다. 변경 프레임의 마을 갱신 표본은 86회 평균 2.306ms, 최대 7.397ms였고, 변경 없는 프레임은 평균에 포함되지 않았다. 이 값은 이전 방식의 이력이며 새 실측과 직접 비교하지 않는다. [src: Docs/tech/SAVE_SNAPSHOT_DESIGN.md §11.3]

새 실측에서는 같은 데이터와 변경 패턴으로 다음을 각각 기록한다.

| 측정 | 구분할 조건 |
| --- | --- |
| 평소 프레임 갱신 | 변경 없는 프레임과 변경 있는 프레임, 항목 수와 묶음별 호출 수 |
| 비용과 할당 | 평균·최대·가능하면 p95, 할당량과 GC 횟수 |
| 저장 직전 캡처 | 캐시 반영·루트 조립·fallback 복사를 분리 |
| 저장 중 누적 | 동결 시간, 누적 변경 수, 해제 후 처리량과 프레임 비용 |
| 백그라운드와 커밋 | JSON·파일 준비·manifest 교체, 파일 생성과 재사용 수 |
| 전체 재생성 | 부분 갱신과 별도 표본으로 측정 |

작은 묶음만 바뀌는 경우의 복사량 감소와 데이터 정합성을 완료 기준으로 삼는다. 저장이 드물고 변경이 많을 때의 누적 비용, 동결 해제 후 부하도 함께 보고한다. 모든 저장 시간이 0이 되거나 기존 프레임 비용이 특정 수치까지 감소한다고 미리 보장하지 않는다.

## 8 구현 상태와 검증 결과

| 작업 | 상태 | 결과 |
| --- | --- | --- |
| 공통 선택 캡처와 동결 수명 | 완료 | 필드 마스크, 묶음별 준비/실패 재시도, lease별 옛 수명 격리, worker finally 해제 |
| 기존 상시 스냅샷 여섯 파티션 | 완료 | NPC 12묶음·Village 7묶음·Party 2묶음. Social·Population·WorldSim은 작은 항목 전체 유지 |
| Player와 World | 완료 | Player 7묶음·슬롯/관계 항목별 캐시, World 3묶음. ECS/트랜잭션 숫자 거울은 저장 경계에서 비교 |
| Map과 Buildings와 Chronicle | 완료 | 드랍 객체 참조·청크 좌표·건물 EntityId·연대기 종류/ID별 캐시, 추가/삭제/순서 반영 |
| Dungeons와 통합 | 완료 | 기록 6묶음, 마을 슬롯은 단일 묶음. 몬스터/슬롯의 바뀐 하위 항목만 복사 |
| 빌드와 데이터 검증 | 핵심 통과 | §8.2. 기존 Runtime/Editor 프로젝트 사용 |
| 성능 비교 | 표본 측정 완료 | §8.3. 합성 마을 표본이며 실제 장면 비용과 구분 |

### 8.1 적용 코드와 구현 선택

- `SaveSnapshotGroups`가 저장 필드를 묶음에 연결하고, `SaveDtoSnapshot.UpdateGroup`은 선택된 묶음의 getter만 읽는다. 선택된 목록·사전의 항목은 저장 표현을 비교해 변경 항목만 복사하며, 수량·옵션·소켓을 제자리에서 수정해도 반영한다. 생략 조건이 바뀌면 이전 키를 제거/복구하고 Newtonsoft 계약 순서로 최상위 필드 표를 다시 조립한다. 기존 전체 `Capture`는 fallback과 비교 기준으로 남겼다. [src: Assets/Scripts/Data/SaveSnapshotGroups.cs, Assets/Scripts/Data/SaveDtoSnapshot.cs]
- `SaveSnapshotCache`는 ID→OR 마스크, 실패 마스크, 변경 세대, reader 수를 인스턴스로 소유한다. 동결 중 Flush/Rebuild는 DTO를 수정하지 않는다. Reset은 새 State로 교체하며 lease가 옛 State만 해제한다. 한 묶음 실패는 해당 묶음을 적용하지 않고 저장을 막으며, 다른 묶음 갱신은 계속한다. [src: Assets/Scripts/Data/SaveSnapshotCache.cs]
- NPC·Village·History·사회/파티/인구는 기존 소유 매니저의 캐시를 확장했다. 나머지 여섯 원장은 DataManager의 수명에 묶인 캐시를 사용한다. Map 드랍 키는 런타임 객체 참조이며 새 영속 ID를 만들지 않았다. Player 관계·Chronicle 항목·슬롯·청크의 추가/삭제/목록 순서는 저장 경계에서 원장과 대조한다. [src: Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Village/VillageManager_SaveSnapshot.cs, Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs, Assets/Scripts/Manager/DataManager_SaveSnapshot.cs]
- 저장 배치는 캡처/증분 선택 후 worker가 실제 참조하는 캐시만 동결한다. lease 목록은 성공/실패/Reset 모든 경로에서 finally로 해제한다. 프레임 끝 priority 990에서 해제된 캐시의 누적 표시를 반영한다. 이번 배치의 세대만 확정하는 정책, Simulation 6파트 결합, NPC 32샤드, manifest 커밋/복구/내용 해시 파일 재사용은 유지했다. 새 여섯 파트는 기존 내용 해시로 파일을 재사용하며 세대만으로 JSON 변환을 생략하는 최적화는 기존 Simulation 범위에만 유지한다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/DataManager_SaveSnapshot.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs]
- 전체 표시가 남은 곳은 신규·로드·이관·마을 materialize/fold-back처럼 여러 영역을 동시에 바꾸는 경계다. 수동/최초/던전 트랜잭션의 재생성 정책과 `captureRuntimeManagers=false`/스위치 OFF fallback을 유지했다. Player/World의 작은 scalar 거울은 직접 ECS·맵 트랜잭션 되쓰기를 포괄하도록 저장 시 비교한다. [src: Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/DataManager_SaveSnapshot.cs]
- 복제 시뮬 진단은 캐시 인스턴스 전체와 DataManager 원장 캐시를 Swap/복원한다. 새 마스크·실패·동결·세대·타이밍을 라이브 해시에 포함하며, 다른 묶음의 pending 표시나 동일 묶음의 다음 표시가 앞선 누락을 가리지 않게 검사한다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Data/SaveSnapshotCache.cs]

### 8.2 실행 검증 (2026-10-05)

현재 연결된 Unity는 **6000.4.2f1**이다. 기존 프로젝트에서 다음 검사를 실행했다.

| 검사 | 결과 |
| --- | --- |
| 기존 Village 계약 | 17/17 PASS |
| 기존 Simulation 계약 | 29/29 PASS |
| 새 묶음 계약 | **34/34 PASS**. 선택 캡처·하위 슬롯·같은 참조의 수량/옵션/소켓·BasicItemId 전환·생략/재추가·순서·동결/Reset·묶음 실패 복구·12종 원장 검사 |
| 실제 partition writer/reader | **2,188 checks PASS**. 12종 논리 파티션/43개 물리 파트의 fallback→캐시 JSON 동등성·참조 재사용·변경 파일 수·삭제·재생성·큐 병합 |
| 저장 중 변경 | 기존 Simulation 6파트와 새 여섯 파트 모두 previous manifest는 캡처 시점 값, 최신 manifest는 누적 변경 값. 동결 중 Flush가 worker DTO를 바꾸지 않음 |
| 장애/수명 | serialize·part-write·after-part·before-manifest 실패 후 원자 manifest 보존과 재시도, Reset 중 old worker 차단, previous manifest 전체 복구 PASS |
| Dungeon 기존 메뉴 | 전체 PASS. 300마을 슬롯/32 레이아웃 표본, DTO 독립성, 완료/재접속/롤백, 원자 진입 저장, v14/v15 이관, corruption 복구, 실제 manifest 교체 실패, 2,400 NPC 왕복, 저장 큐/Load/Reset drain |
| Runtime/Editor 빌드 | 기존 `Assembly-CSharp.csproj`, `Assembly-CSharp-Editor.csproj`의 `dotnet build --no-restore` 모두 오류 0 |
| 장기 복제 시뮬 | **7게임일/168시간 PASS**. 마을 300개/NPC 2,406명, NPC·Village·Simulation 각각 표시 누락 0/끝 비교 누락 0, 에러 로그 0, 라이브 해시 전후 동일(`52a210b8966a4de4`) |
| NPC 던전 완료 규칙 | 실패 0, 라이브 해시 동일. 방문 기록 보호·슬롯 세대/입구/쿨다운·절반 교체 제한·미저장 완료 결합·플레이어 완료 및 롤백 불변 |

왕복 진단은 프로젝트 Temp의 별도 DataManager/원장/캐시에서 실행하며 생성한 파일을 finally에서 제거한다. 플레이 진단은 기존 저장을 Temp에 복사해 읽고 쓰며 사용자 저장 원본을 수정하지 않는다. [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics_Grouped.cs, Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs, Assets/Scripts/Manager/DataManager.cs]

최종 복제 시뮬은 실제 `System_NpcSim`과 같은 Activity/Location/Condition 마스크와 자원/파티 활동 마스크를 사용했다. 전체 표시가 세부 누락을 덮는 것을 피했다. 113.4초 동안 Hunt 24,916회·Delve 3,023회·치료 4,869회·판매 9,916회·구매 198회·생활비 11,101회·이주 도착 148명·사망 3명을 처리했다. NPC/Village/Social/Party/Population 및 새 원장 캐시 참조·내부 상태를 포함한 라이브 해시가 같았다. 최초 실행에서 새 캐시의 최초 생성 300건을 누락으로 오인한 관찰자를 수정했고, 수정 후 최종 표본이 통과했다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Data/SaveSnapshotCache.cs]

7일 표본을 넘어선 30일/장기 표본과 실제 UI를 통한 Player 장착·마법부여·소켓 변경, 청크 materialize/fold-back, 던전 동행·입퇴장 경로의 수동 플레이 확인은 남아 있다. 해당 데이터/트랜잭션 계약은 격리 DTO·writer·던전 진단으로 검증했으며 수동 플레이 완료로 보고하지 않는다. 이번 작업의 임시 저장 복사본·시뮬 CSV/요약·빈 진단 임시 디렉터리는 결과를 이 문서에 옮긴 뒤 제거하고, 플레이 설정과 Editor 상태를 원래대로 복원했다.

### 8.3 새 방식 성능 표본

`Verify Village Snapshot Contracts`에 `MeasureGroupedSnapshotUpdates`를 연결했다. 동일한 합성 Village 300개(각 stock 12, 시설 12, 벽 32), 모든 마을의 Gold 변경 패턴, warm-up 후 20회 표본으로 측정했다. 전체 재생성은 별도 5회 표본이다. 아래는 **JSON/파일 I/O를 제외한 메인 스레드 갱신**이다. [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics_Grouped.cs]

| 경로 | 평균 ms | p95 ms | 최대 ms | GC0 |
| --- | ---: | ---: | ---: | ---: |
| 항목 전체 복사 기준 | 105.725 | 209.884 | 218.057 | 2 |
| 자원 묶음 갱신 | 22.569 | 23.390 | 24.692 | 0 |
| 변경 없는 Flush | 0.000 | 0.000 | 0.001 | 0 |
| 전체 묶음 재생성 | 195.350 | 206.187 | 206.187 | 0 |
| 캐시 루트 조립 | 0.025 | 0.042 | 0.043 | 0 |
| 동결 중 변경 누적/Flush | 0.033 | 0.033 | 0.043 | 0 |

동결 중 캡처는 0회였고 중복 표시된 자원 300묶음은 해제 후 한 번씩 반영됐다(29.419ms). 최종 300/300 JSON은 전체 캡처와 같았다. Mono의 `GC.GetAllocatedBytesForCurrentThread`가 모든 경로에서 0을 반환해 **할당량은 측정 불가**로 처리한다. 합성 표본에서는 작은 묶음 갱신 평균이 줄었으나 전체 재생성 비용은 더 컸다. 실측에는 GC와 Editor 부하가 섞여 있으며 플레이 장면의 보장치로 사용하지 않는다.

실제 상점/입퇴장 장면의 main capture·background JSON·파일 준비·manifest 타이밍, 드문 저장의 장시간 누적 표본과 Player/Map/Dungeon별 성능 분포는 아직 별도 실측하지 않았다. 기존 SaveTiming의 새 여섯 원장 갱신 시간은 기타 manager 캡처 잔여 시간에 포함된다.

다음 세션의 요청으로 사용할 수 있는 문구:

```text
Docs/tech/SAVE_GROUPED_SNAPSHOT_DESIGN.md를 읽고 묶음별 저장 스냅샷 갱신을 구현해줘.
기존 캐시는 변경된 묶음만 갱신하고, 백그라운드 저장이 읽는 동안에는
캐시 수정을 보류하면서 변경 표시를 누적하는 방식으로 진행해줘.
범위는 문서의 12종 논리 파티션 전체이며, 작은 항목은 단일 묶음을 유지해도 돼.
현재 JSON 스키마, 파티션, NPC 32샤드, 원자 커밋, 증분 파일 재사용과
Simulation 공동 저장 계약을 유지하고, 기존 Editor 진단을 확장해 검증해줘.
진행 상태와 검증 결과를 문서에 갱신해줘.
```
