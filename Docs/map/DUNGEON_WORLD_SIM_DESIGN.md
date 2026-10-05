# 던전 중 월드 시뮬레이션 설계 (초안)

> 상태: 2026-10-03 설계 초안. 던전 중 월드 시뮬 게임플레이는 구현 전이다. **2026-10-05 선행 S-1~S-3 저장 스냅샷 구현·핵심 기능 검증 통과**. 이를 기반으로 시뮬 6개 파트 묶음·증분 저장을 구현했다(§6.3·Q3, SAVE_SNAPSHOT_DESIGN §13). **확정** 항목은 사용자 결정이고, **제안** 항목은 구현 전에 검토할 기본안이다.
>
> 범위: 플레이어가 던전 안에 있는 동안 월드의 게임 시간·NPC·마을을 추상 시뮬레이션으로 계속 진행하는 규칙. 시스템 분류, 입장·퇴장 순서, 월드 맵 조회, 저장 범위, 검증. 던전 내부 콘텐츠와 경제 밸런스 수치는 다루지 않는다.

## 1. 목표와 배경

- **목표**: 플레이어가 던전에 들어가도 월드 시뮬레이션은 멈추지 않고 **추상 시뮬레이션으로 계속 돈다.** 월드 입장에서는 "플레이어가 던전에 있음"을 "플레이어가 월드에서 아주 멀리 떠나 있음"과 같게 처리한다. [확정: 2026-10-03 사용자]
- **배경**: 마을 골드 밸런스에서 물가 상승·안정화 시점을 **현실 플레이 시간**으로 정하기로 했다. 지금은 던전 안에서 게임 시간이 멈추므로, 플레이어마다 던전에서 보내는 비율만큼 월드 경과 시간이 달라진다.
- 원래 던전 문서는 "던전 활성 동안 각 시스템은 현재 맵 종류를 확인하거나 월드 작업을 일시 중지한다"를 열어 두었고, 구현은 **일시 중지** 쪽을 택했다. 이 문서는 그중 시간으로 도는 시뮬레이션을 **월드 맵 기준으로 계속 실행**하는 쪽으로 바꾼다. [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §4

### 1.1 현재 동작 (2026-10-03 코드 기준)

던전 입장(`ActivateInterior`)은 월드를 통째로 얼려 옆에 치우고, 같은 ECS와 맵 자리에 던전을 올린다. [`DungeonManager.cs`](../../Assets/Scripts/Dungeon/DungeonManager.cs) `ActivateInterior`

1. `SuspendWorld` — 렌더 중인 월드 청크를 모두 비활성화한다. 청크 비활성화 콜백으로 NPC·건물·몬스터는 **필드에서 멀어질 때와 같은 경로**로 저장 후 비실체화되고, 입구 표현도 내려간다. [`MapManager_Dungeon.cs`](../../Assets/Scripts/Map/MapManager_Dungeon.cs), [`MapManager_Spawner.cs`](../../Assets/Scripts/Map/MapManager_Spawner.cs)
2. `SuspendExceptPlayer` — 남은 엔티티(주로 **실체화된 마을의 ECS 엔티티**: 마을·저장소 컴포넌트, 진행 중 task, 배치 오브젝트)의 컴포넌트를 풀에서 꺼내 보관하고 GameObject를 끈다. 플레이어와 플레이어의 스킬·버프만 남는다. [`ComponentManager_MapContext.cs`](../../Assets/Scripts/Manager/ComponentManager_MapContext.cs)
3. 던전 맵을 새로 만들어 `ActiveMap`에 둔다. 이때부터 `AR.s.Map`은 **던전 맵**을 가리킨다(`ActiveMap ?? _mapManager`). [`AR.cs`](../../Assets/Scripts/Manager/AR.cs)

`SystemManager.CanRun`은 플레이어가 던전 안이면 `IWorldMapSystem` 11개를 실행하지 않는다. 여기에 `System_Time`이 들어 있어 **게임 시계도 멈춘다**. 퇴장 때는 보관한 값을 그대로 되돌리고(`RestoreSuspended`) 월드 청크를 다시 켠다. [`SystemManager.cs`](../../Assets/Scripts/Manager/SystemManager.cs), [`IWorldMapSystem.cs`](../../Assets/Scripts/Common/Interface/IWorldMapSystem.cs)

## 2. 시스템 분류

`IWorldMapSystem` 묶음에는 성격이 다른 두 종류가 섞여 있다. **월드 좌표·월드 엔티티가 꼭 필요한 시스템**은 던전 안에서 입력이 사라지므로 계속 멈춘다. **게임 시간으로 도는 추상 시뮬레이션**은 월드 맵을 명시적으로 조회하도록 고친 뒤 던전 안에서도 실행한다. [제안]

| 시스템 (Priority) | 하는 일 | 던전 중 | 이유 |
|---|---|---|---|
| `System_Time` (5) | 게임 시계 | **실행** | 이 문서의 목적 |
| `System_SettlementSim` (50) | 먼 정착지 인구·자원 tick, 실체화 전환 | **실행** | 월드 맵 기준으로 바꾸면 좌표가 필요 없다 (§4) |
| `System_VillagePassiveProduction` (52) | 실체화 마을 자원 생산 | **실행** (§3.3) | 손제작 마을은 접히지 않으므로 이 경로로 생산해야 한다 |
| `System_NpcSim` (59) | 오프스크린 NPC 활동·파티·보충 | **실행** | 월드 맵 기준으로 바꾸면 좌표가 필요 없다 (§4) |
| `System_VillageTierProgression` (60) | Tier 승격 | 정지 | 런타임 발전 꺼짐, 실체화 마을 ECS 필요 |
| `System_VillageServiceProximity` (61) | 플레이어 근처 시설 집계 | 정지 | 플레이어 월드 좌표 필요 |
| `System_VillageBuildQueue` (66) | 건설 큐 실행 | 정지 | 월드 타일 배치 |
| `System_VillageWallPlanner` (67) | 벽 계획 | 정지 | 월드 타일 배치 |
| `System_DungeonPlacement` (110) | 마을 접근 시 입구 배치 | 정지 | 플레이어 월드 좌표 필요 |
| `System_MapChunkLoader` (120) | 청크 로드·언로드 | 정지 | 플레이어 월드 좌표 필요 |
| `System_MonsterSpawn` (130) | 월드 몬스터 스폰 | 정지 | 플레이어 월드 좌표 필요 |

- 실행 쪽 4개는 `IWorldMapSystem`에서 빼고 새 표식 인터페이스 `IWorldSimSystem`을 붙인다. 뜻은 "게임 시간으로 도는 월드 추상 시뮬레이션 — 맵 컨텍스트와 무관하게 실행하고, 월드 맵은 `AR.s.WorldMap`으로만 조회한다"이다. `CanRun`은 이 표식을 건드리지 않는다. [제안]
- 던전 메뉴가 열려 게임플레이가 멈춘 동안(`GameplayPaused`)은 지금처럼 Input·Checkpoint 외 모든 시스템이 멈추고, 시계도 멈춘다. [제안: 유지]

## 3. 입장·퇴장 순서

### 3.1 입장: 연대기 마을을 먼저 접는다

실체화된 연대기 마을(`SettlementId ≥ 0`, `IsMaterialized`)은 **엔티티를 보관함으로 빼기 전에** 추상으로 접는다(`FoldBackSettlement`). [제안: 필수]

```text
ActivateInterior (변경 후)
  StopTransientCombat, UnloadContextDrops         (지금과 같음)
  SuspendWorld                                    (지금과 같음 — NPC·건물·몬스터 비실체화)
  연대기 마을 전부 FoldBackSettlement              (신규 — 필드에서 멀어질 때와 같은 순서)
  SuspendExceptPlayer                             (이제 손제작 마을 ECS와 잔여 엔티티만 보관)
  CreateDungeonMap → ActiveMap                    (지금과 같음)
```

- **왜 필수인가**: 마을 접기(`DematerializeVillage`)는 ECS 컴포넌트 값을 원장(`VillageData`)으로 되돌려 쓴 뒤 엔티티를 파괴한다. 정착지 시뮬이 던전 안에서 돌면, 월드 청크가 모두 꺼져 있으므로 실체화된 마을을 "멀어짐"으로 보고 접으려 한다. 이때 컴포넌트가 보관함에 들어가 있으면 진행 중 task·배치 오브젝트를 읽지 못해 잃고, 보관 중인 엔티티 ID를 파괴한다. 퇴장 때 `RestoreSuspended`가 파괴된 ID에 옛 컴포넌트를 되살려 유령 마을 엔티티가 생긴다. [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) `DematerializeVillage`·`FlushVillageEntityState`, [`HistoryManager.cs`](../../Assets/Scripts/History/HistoryManager.cs) `UpdateMaterialization`·`FoldBackSettlement`
- 순서는 필드와 같다. 필드에서도 청크가 먼저 꺼지고, 다음 정착지 시뮬 tick에서 마을이 접힌다.
- 접은 마을은 던전에 있는 동안 정착지 시뮬의 추상 경로(자원 생산·clock 정렬, 런타임 발전은 꺼짐)로 진행한다.
- 재접속 복원(`RestoreSessionAsync`)도 같은 `ActivateInterior`를 거치므로 같은 규칙이 적용된다.

### 3.2 손제작 마을

손제작 마을(`SettlementId = -1`, BaseTown 등)은 추상 층에 참여하지 않아 접을 수 없고 항상 실체화 상태로 남는다. 던전에 있는 동안 이 마을의 ECS는 보관함에 있고, **원장(`VillageData`)만** 바뀐다(NPC 거래 골드, Hunt 성공 Food 유입, §3.3의 생산). [`village.md`](../../Wiki/village.md) §1

### 3.3 자원 생산 (손제작 마을)

`System_VillagePassiveProduction`은 지금 `VillageStorageComponent`가 있어야만 생산한다. 던전 중에는 컴포넌트가 보관함에 있어 손제작 마을의 생산이 멈춘다. [`System_VillagePassiveProduction.cs`](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs)

- 생산 계산(`VillageResourceSimulation.AdvanceWholeHours`)은 원장만 쓰므로, **컴포넌트가 없어도 원장은 진행하고 미러 갱신(`SyncStorageFromData`)만 건너뛴다.** 이미 컴포넌트가 없으면 안전하게 건너뛰는 함수다. [제안]
- 이중 생산은 생기지 않는다. 접힌 연대기 마을은 `EntityId < 0`이라 이 시스템이 건너뛰고, 정착지 시뮬은 실체화된 마을을 건너뛴다.
- 던전 중에는 이 시스템의 마을 자원 알림(`SetNotify`)을 띄우지 않는다. [제안]

### 3.4 퇴장: 미러를 원장에서 다시 채운다

```text
ExitAsync (변경 후)
  ... RestoreSuspended                            (지금과 같음)
  실체화 마을 미러 재동기화                          (신규 — 손제작 마을)
  FinishCompanionDungeonExit → MovePlayer → ResumeWorld   (지금과 같음)
```

- `RestoreSuspended`는 입장 시점의 컴포넌트 값을 되살리므로, 던전 중 바뀐 원장과 미러가 어긋난다. 복원 직후 실체화된 마을마다 저장소·마을 컴포넌트를 원장에서 다시 채운다(`SyncStorageFromData`, 벽 통계 동기화). 다음 생산 tick(최대 5초)에서도 맞춰지지만, 그 사이 UI가 옛 값을 보이지 않게 즉시 한다. [제안]
- `ResumeWorld`의 청크 활성화가 NPC(던전 중 진행된 위치·상태)·건물·몬스터를 평소 경로로 실체화하고, 정착지 시뮬이 근처 연대기 마을을 다시 실체화한다. 던전 중에도 추상 tick이 돌았으므로 실체화 catch-up은 작다. [`ABSTRACT_CONSTRUCTION_DESIGN.md`](../village/ABSTRACT_CONSTRUCTION_DESIGN.md) §7
- 퇴장 후 월드는 **입장 직전 모습 그대로가 아니다.** 몬스터는 새로 스폰되고, NPC는 그동안의 활동 결과대로 다른 곳에 있거나 부상·사망했을 수 있다. 이것이 이 설계의 의도다. [확정: 목표에서 따라옴]

### 3.5 동행 NPC

기존 계약을 그대로 쓴다. 던전 안 동행 NPC는 `System_NpcSim`의 판정에서 이미 제외된다(`IsCompanionInDungeon`). NPC_COMBAT 문서는 이 제외를 "구조가 바뀌어도 동행 NPC가 판정되지 않게 하는 방어선"으로 두었으므로, 이번 변경에서 실제로 쓰이게 된다. [`NPC_COMBAT_DESIGN.md`](../npc/NPC_COMBAT_DESIGN.md) §4.5, [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §5.3

## 4. 월드 맵 조회 규칙

**규칙**: 던전 중에도 실행되는 시뮬레이션 경로는 `AR.s.Map`이 아니라 `AR.s.WorldMap`만 쓴다. 필드에서는 두 값이 같으므로 이 정리만으로는 필드 동작이 바뀌지 않는다. [제안: 필수]

2026-10-03 검색에서 찾은, 던전 맵으로 읽으면 틀어지는 지점:

| 위치 | 용도 | 던전 맵으로 읽으면 |
|---|---|---|
| [`System_NpcSim.cs`](../../Assets/Scripts/Common/System/System_NpcSim.cs) 35행 | `worldSeed = AR.s.Map.mapSeed` | 던전 시드로 RNG를 만들어 활동 결과의 결정론이 깨진다 |
| `System_NpcSim.cs` 203~206·230~233행, [`NpcManager_Delve.cs`](../../Assets/Scripts/Manager/NpcManager_Delve.cs) 91~94행 | 맵 경계 | 던전 경계(청크 −2~16)로 사냥 경로·목표 위치가 잘린다 |
| [`NpcManager_Ambition.cs`](../../Assets/Scripts/Manager/NpcManager_Ambition.cs) `GetNpcLootZoneLevel` | 전리품 Zone | 마을 청크 좌표가 던전 활성 청크와 겹치면 Zone 1로 읽어 전리품 등급이 틀린다 |
| [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) 1758행 | 상점 재고 롤 seed | NPC 거래가 재고를 롤하면 던전 시드로 롤한 재고가 저장된다 |
| [`HistoryManager.cs`](../../Assets/Scripts/History/HistoryManager.cs) `UpdateMaterialization` | 마을 근처 판정 `IsChunkActive` | 마을 청크 좌표가 던전 활성 청크와 겹치면 던전 안에서 마을을 실체화한다 |
| [`NpcManager.cs`](../../Assets/Scripts/Manager/NpcManager.cs) 410·739행 | NPC 엔티티 생성 판정 `IsChunkActive` | 보충 NPC가 던전 안에 엔티티로 생성될 수 있다 |
| `NpcManager.cs` `GetRandomSpawnPositionInVillage` → [`CollisionUtil.cs`](../../Assets/Scripts/Common/Utility/CollisionUtil.cs) 148·180·223행 | 보충 NPC 스폰 위치 충돌 검사 | 던전 타일로 충돌을 검사한다 |

- 이 목록은 검색 결과이며 완전하지 않을 수 있다. 구현 때 `System_NpcSim`·`System_SettlementSim`·`System_VillagePassiveProduction`·`System_Time`의 진입점에서 닿는 호출 경로 전체(특히 `Village`·`Npc`·`History`·`CollisionUtil`·`VillageTileFinder`)를 다시 감사한다.
- **진단 보호**: 개발 빌드에서 `IWorldSimSystem` 실행 중에 플레이어가 던전 안이면 `AR.s.Map` getter가 경고를 남기게 한다. 감사에서 빠진 경로를 플레이 중에 잡기 위해서다. [제안]
- 월드 맵을 `AR.s.WorldMap`으로 조회하더라도 던전 중 월드 타일맵은 비어 있다(`SuspendWorld`가 렌더 청크를 지운다). 렌더 타일맵을 읽는 `MapManager.IsWalkable` 같은 함수는 던전 중 시뮬 경로에서 쓰지 않는다. 청크 데이터 조회가 청크 활성화·스폰 같은 부작용을 내지 않는지도 검증한다. [제안]

### 4.1 NPC 보충

전역 총량 보충(`TryReplenishGlobalPopulation`)은 스폰 위치를 정하려고 마을 주변 타일·충돌을 조회한다. 던전 중에는 이 조회를 믿을 수 없으므로 **보충을 퇴장 뒤로 미룬다.** 보충 간격이 6게임시간이고 사망이 드물어 영향이 작다. [제안] [`NpcManager.cs`](../../Assets/Scripts/Manager/NpcManager.cs) `TryReplenishGlobalPopulation`

## 5. NPC 던전 완료

NPC 던전 완료(`TryCompleteByNpc`)는 플레이어가 던전 안이면(`IsInside`) 모두 거부한다. 던전 중 시뮬이 돌면, 그동안 **어느 던전도** NPC가 완료하지 못한다. [`DungeonManager_NpcCompletion.cs`](../../Assets/Scripts/Dungeon/DungeonManager_NpcCompletion.cs)

- **1단계는 이 보호를 유지한다.** 슬롯 교체는 입구 표현과 입구 역색인 재구축(던전 중 정지된 `System_DungeonPlacement`의 `TickPlacement`)을 건드리기 때문이다. Delve 원정 자체(전리품·부상·소문)는 그대로 진행되고 완료 판정만 거부된다. [제안]
- 영향: 게임 시간당 NPC 완료 빈도가 던전 체류 비율만큼 줄어든다. 진단에 `PlayerInside` 거절 수를 집계한다.
- 후속: "플레이어가 들어간 던전만 보호"(`DungeonRecord` 보호)로 완화할지 검토한다(§10 Q2).

## 6. 저장

### 6.1 현재 저장 범위

| 시점 | 범위 |
|---|---|
| 던전 입장 커밋 | Player · World · Map · NpcState · Dungeons |
| 던전 퇴장 커밋 | Player · World · NpcState · Dungeons |
| 던전 완료 커밋 | Player · World · NpcState · Dungeons |
| 던전 체크포인트 (30초) | Player · World · Dungeons |
| 필드 상점 거래 | Player · World · Villages |
| 메뉴 수동 저장·최초 월드 저장 | 전체 |

위 표는 호출 측의 요청 범위다. 현재 DataManager는 `NpcState`·`NpcSocial` 중 하나가 있으면 둘 다 포함하고 `NpcState`가 실리면 `NpcParties`도 추가한다. 따라서 던전 입장·퇴장·완료 저장에는 세 NPC 파트가 함께 실리지만 NpcMeta·Villages·WorldSim은 자동으로 추가하지 않는다. 미저장 NPC 던전 완료의 추가 결합과 최초/이관의 All 승격은 기존 정책을 따른다. **S-3 스냅샷은 이 결합 범위를 바꾸지 않았다.**

[`DungeonManager.cs`](../../Assets/Scripts/Dungeon/DungeonManager.cs), [`DataManager_PartitionedSave.cs`](../../Assets/Scripts/Manager/DataManager_PartitionedSave.cs), [`SAVE_PARTITION_DESIGN.md`](../tech/SAVE_PARTITION_DESIGN.md) §5

### 6.2 문제: 시뮬 결과가 파티션마다 다른 시점으로 저장된다

시뮬레이션이 바꾸는 파티션은 NpcState·NpcSocial·NpcParties·NpcMeta·Villages·WorldSim이다. 던전 입장·퇴장·완료 커밋은 현재 결합 뒤에도 이 중 **NpcState·NpcSocial·NpcParties만** 싣는다. 예를 들어 NPC가 상점에 물건을 팔면 NPC Gold(NpcState)는 저장되고 마을 Gold(Villages)는 저장되지 않는다. 그 사이 비정상 종료되면 닫힌 경제 합계가 어긋난다(골드 복제 또는 소실). [`NPC_AMBITION_DESIGN.md`](../npc/NPC_AMBITION_DESIGN.md) §9

이 위험은 **지금 필드에도 있다**(입장 커밋에는 Villages가 빠지고, 상점 저장에는 NPC 파트가 빠진다). 던전 중 시뮬이 돌면 저장 사이에 쌓이는 변화가 커져 위험이 커진다.

### 6.3 규칙: 시뮬 묶음은 항상 함께 저장한다

- **시뮬 묶음** = NpcState · NpcSocial · NpcParties · NpcMeta · Villages · WorldSim. 이 중 하나라도 싣는 저장은 묶음 전체를 하나의 manifest에서 함께 확정한다. 변경 없는 파일은 이전 생성 revision·참조를 재사용한다. [구현: 2026-10-05 사용자 묶음·증분 저장 요청]
- 던전 입장·퇴장·완료 커밋은 NpcState 대신 시뮬 묶음을 싣는다.
- 던전 체크포인트(30초)는 Player·World·Dungeons 범위를 유지한다. 단, 미저장 NPC 던전 완료가 있으면 슬롯과 보상이 갈라지지 않도록 기존 완료 결합이 시뮬 묶음 전체로 확장된다. NPC 변화 자체로 즉시 저장을 요청하지 않는 원칙(D47)은 유지한다.
- 필드 상점 저장은 플레이어 구매 재고(`MerchantStock`) 때문에 Villages가 꼭 필요하다. 그래서 이 규칙을 따르면 상점 거래마다 시뮬 묶음 전체를 싣게 된다. Villages를 빼면 구매 뒤 비정상 종료 시 재고가 줄지 않아 같은 물건을 다시 살 수 있으므로 대안이 아니다.
- 비정상 종료 시 시뮬 결과는 마지막 시뮬 묶음 저장 시점으로 돌아간다. 필드에서도 시뮬 결과는 주로 수동 전체 저장 때 남으므로 같은 수준이다.
- 비용: S-1~S-3 캐시 위에 증분 파일 쓰기를 적용했다. 디스크 확정 세대가 같은 파트/샤드는 JSON 직렬화를 건너뛰고, 후보도 schema·길이·SHA-256이 같으면 새 파일을 만들지 않는다. 많이 바뀐 저장은 여전히 32샤드 모두를 쓸 수 있으며 전체 재생성 대상의 복사 비용은 유지한다([`SAVE_SNAPSHOT_DESIGN.md`](../tech/SAVE_SNAPSHOT_DESIGN.md) §13).
  - 던전 입장·퇴장·완료 커밋의 NpcState 요청은 묶음 전체로 확장하며, 기존 정책대로 캐시를 전체 재생성한다. Player·World·Dungeons 체크포인트는 미저장 NPC 완료가 있을 때만 Dungeons와 묶음을 함께 확정한다.
  - **상점 거래 저장**은 Player·World + 묶음 전체를 확정한다. 모든 여섯 파트를 매번 새 파일로 쓰는 것은 아니다. 저장 중 추가 변경·실패한 변경은 다음 요청까지 보존한다. 실제 상점·던전의 전체 메인/백그라운드 비용 실측은 후속이다.

## 7. 게임 시간을 보는 다른 규칙

던전 안에서 게임 시간이 흐르면 아래 규칙도 함께 흐른다. 모두 "월드 시간이 흘렀다"는 뜻이라 의도에 맞으므로 **그대로 둔다.** [제안]

- 상점 재입고 회차, 성소 쿨다운(`UIShrine`), 던전 입구 교체 쿨다운(`PlacementReadyGameTime`)
- NPC 부상 회복·쇼핑 쿨다운·절도 쿨다운, 대화·소문의 "N일 전" 표현
- 던전 내부 규칙이 게임 시간을 쓰는 곳은 완료 트랜잭션의 교체 쿨다운 기준 시각 하나뿐이다. 영향이 없다.

## 8. 성능

- 던전 중에도 NPC 시뮬(0.25초마다 12명)과 정착지 시뮬(0.5초마다 정착지 48곳 + 마을 건설 12곳)이 돈다. 필드와 같은 비용이지만 던전 전투 프레임과 겹치므로 Profiler로 실측한다.
- 입장 프레임에 근처 연대기 마을 접기가 추가된다(보통 1~3곳). 퇴장 때는 필드 이동과 같은 경로로 다시 실체화된다.

## 9. 영향받는 튜닝·문서

- **D63**(NPC 던전 완료 빈도 목표)은 "플레이어가 던전 안에 있는 동안 월드 시간이 멈춘다"를 전제로 한다. §5 보호를 유지하면 실제 플레이 시간당 빈도는 크게 바뀌지 않지만 다시 측정한다. [`NPC_AMBITION_DESIGN.md`](../npc/NPC_AMBITION_DESIGN.md) D63
- **경제 밸런스**: 물가 시계는 게임 시간이다([GOLD_BALANCE_DESIGN.md](../economy/GOLD_BALANCE_DESIGN.md) D125, 목표 420게임일). 골드 설계를 먼저 적용했고(2026-10-03), 이 문서가 구현되면 물가 시계가 던전을 포함한 플레이 시간(메뉴 일시정지 제외)이 된다. 그 전까지는 던전에 머무는 시간만큼 실제 안정 시점이 늦어진다.
- **확정 후 갱신할 문서**: [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §5.3 "시스템 가동", [`NPC_COMBAT_DESIGN.md`](../npc/NPC_COMBAT_DESIGN.md) §4.5 참고 문장, NPC_AMBITION D63 설명.

## 10. 열린 질문

| # | 질문 | 기본안 |
|---|---|---|
| Q1 | ~~시간 배율: `AR.prefab`은 현실 5초 = 게임 1시간인데, 코드 기본값과 D63 설명은 60초를 전제로 한다. 어느 쪽이 의도인가?~~ | **닫음 (2026-10-03, GOLD_BALANCE D124)** — 60초 = 게임 1시간. `AR.prefab` 5 → 60 적용 |
| Q2 | 던전 안에 있는 동안 NPC 던전 완료를 모두 막을지, 플레이어가 들어간 던전만 막을지 | 1단계는 모두 막음 (§5) |
| Q3 | 상점 거래 저장에도 시뮬 묶음을 실으면 거래마다 NPC 32샤드를 쓴다. 그대로 갈지, 바뀐 샤드만 쓰는 증분 저장을 먼저 넣을지 | **방향 결정 (2026-10-04)**: 증분 저장 대신 **상시 스냅샷**으로 저장 시 전체 깊은 복사를 우회한다 → [SAVE_SNAPSHOT_DESIGN.md](../tech/SAVE_SNAPSHOT_DESIGN.md). 샤드 단위 증분은 샤드가 약 5초마다 바뀌어 효과가 작다. **S-1 NPC·S-2 마을·S-3 관계/파티/인구/WorldSim 구현·핵심 기능 검증 통과**(2026-10-05). S-3 계약 28/28, 실제 writer/reader 왕복 4/4, 1일 복제 시뮬 누락 0이며 네 파트 캡처는 36~57ms → 1.1~1.7ms였다(전체 저장 시간과 구분). S-2/S-3 장기 검증·마을 프레임 비용 한계는 **스냅샷 문서 §11.3·§12.4**를 따른다. **2026-10-05 사용자 요청으로 묶음·증분 파일 쓰기도 구현했다(스냅샷 문서 §13)**. 초기 결정은 캡처 우회였으며 이번 개정은 그 위에 파일 재사용을 추가한다. 실제 플레이의 전체 비용 실측과 이 문서의 던전 중 월드 시뮬 게임플레이는 후속이다 |
| Q4 | 던전 메뉴가 열린 일시정지 중에도 시간을 멈출지 | 멈춤 유지 (§2) |
| Q5 | 던전 중 월드 쪽 알림(마을 자원, NPC 관련)을 띄울지 | 띄우지 않음 (§3.3) |

## 11. 구현 순서와 검증

저장 캡처(S-1~S-3)와 §6.3의 묶음·증분 저장 선행 구현을 완료했다([SAVE_SNAPSHOT_DESIGN §13](../tech/SAVE_SNAPSHOT_DESIGN.md)). 아래 던전 게임플레이 단계는 아직 미착수다.

1. **월드 맵 조회 정리**(§4)를 먼저 단독으로 한다. 필드에서는 `AR.s.Map == AR.s.WorldMap`이므로 동작이 바뀌지 않아야 한다. 검증: 진단 `Run(30)`의 라이브 해시·지표가 정리 전과 같음.
2. **시스템 분류와 입장·퇴장 순서**(§2·§3): `IWorldSimSystem`, 입장 시 연대기 마을 접기, 손제작 마을 생산의 원장 경로, 퇴장 시 미러 재동기화, 던전 중 보충 보류.
3. **저장 묶음 연동 확인**(§6.3): 선행 저장 정책이 던전 입장·퇴장·완료 커밋에도 적용되고 30초 체크포인트 범위는 유지되는지 검사한다.
4. **회귀 진단 1회**: 진단 `Run(30)`·`Run(60)`, 던전 입장·퇴장·완료·사망, 던전 안 저장·재접속.

필수 검증:

- 던전 안에 N분 머무르면 게임 시간이 그만큼 흐른다(지금 배율이면 현실 5초당 1게임시간).
- 던전 중 NPC 활동 전이(Rest·Hunt·Trade·Delve)와 마을 Gold가 진행되고, 닫힌 경제 검산 오차가 0이다.
- 던전 맵 위에 월드 NPC·몬스터·마을 엔티티가 생기지 않는다.
- 던전 중 시뮬 경로의 `AR.s.Map` 접근 경고가 0이다(§4 진단 보호).
- 퇴장 후 근처 연대기 마을이 실체화되고, 손제작 마을의 저장소 미러가 원장과 같다.
- 손제작 마을(BaseTown)의 자원 생산이 던전 중에도 진행되고, 이중 생산이 없다.
- 입장·퇴장 커밋 직후 강제 종료 → 로드 시 마을 Gold + NPC Gold 합계가 커밋 시점과 같다.
- 동행 NPC는 던전 중 판정에서 제외되고, 결투 중 저장 거부는 그대로다.
- 플레이어가 입장한 던전의 NPC 완료 0건(P2 기준)이 유지된다.

검증 주의: 플레이 모드 입장·퇴장·저장은 실제 세이브에 커밋되므로, 테스트 전에 세이브 폴더를 백업하고 끝나면 복원해 동일한지 확인한다.
