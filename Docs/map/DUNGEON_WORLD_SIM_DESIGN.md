# 던전 중 월드 시뮬레이션 설계

> 상태: 2026-10-03 설계 초안 → 2026-10-19 사용자 결정으로 설계 확정 → **2026-10-19 구현 완료**(준비 → 본체 → 검증 1회, 결과는 §11.1). 던전 안에서도 게임 시계·NPC·마을 시뮬·네임드 감독(출현 제외)이 돈다. 사용자 실플레이 확인과 커밋은 대기. **2026-10-05 선행 S-1~S-3 저장 스냅샷 구현·핵심 기능 검증 통과**. 이를 기반으로 시뮬 6개 파트 묶음·증분 저장을 구현했다(§6.3·Q3, SAVE_SNAPSHOT_DESIGN §13). **확정** 항목은 사용자 결정이다. "개념에서 따라옴"은 §1 목표(던전 = 월드에서 멀리 떠남)에서 바로 나오는 규칙이라 2026-10-19에 함께 확정했다. 남은 **제안**은 구현 방법(§4 진단 보호 등)이다.
>
> 개정(2026-10-19, 사용자 결정):
> - NPC 던전 완료는 플레이어가 들어가 본 던전만 막는다. 던전 안에 있다는 이유만으로 모든 던전을 막던 기본안을 버렸다(§5, Q2).
> - 네임드 위협 감독은 던전 안에서도 단계·사라짐을 진행하고, 새 출현만 퇴장 뒤로 미룬다(§3.6, Q6).
> - 소식창(월드 피드)은 던전 안에서도 필드와 똑같이 띄운다. 마을 자원 알림만 끈다(§3.7, Q5).
> - NPC 전역 보충은 던전 안에서도 그대로 진행한다. 위치 확인만 월드 지도를 보게 한다(§4.1, Q7).
> - 그 밖의 제안(§2 시스템 분류, §3 입장·퇴장 순서, §3.3 손제작 마을 생산, Q4 메뉴 일시정지, §7 시간 규칙)은 §1 목표에서 따라오므로 함께 확정했다.
> - 구현 순서를 준비(월드 맵 조회 정리) → 본체 → 검증 1회로 바꿨다(§11).
>
> 범위: 플레이어가 던전 안에 있는 동안 월드의 게임 시간·NPC·마을을 추상 시뮬레이션으로 계속 진행하는 규칙. 시스템 분류, 입장·퇴장 순서, 월드 맵 조회, 저장 범위, 검증. 던전 내부 콘텐츠와 경제 밸런스 수치는 다루지 않는다.

## 1. 목표와 배경

- **목표**: 플레이어가 던전에 들어가도 월드 시뮬레이션은 멈추지 않고 **추상 시뮬레이션으로 계속 돈다.** 월드 입장에서는 "플레이어가 던전에 있음"을 "플레이어가 월드에서 아주 멀리 떠나 있음"과 같게 처리한다. [확정: 2026-10-03 사용자]
- **배경**: 마을 골드 밸런스에서 물가 상승·안정화 시점을 **현실 플레이 시간**으로 정하기로 했다. 지금은 던전 안에서 게임 시간이 멈추므로, 플레이어마다 던전에서 보내는 비율만큼 월드 경과 시간이 달라진다.
- 원래 던전 문서는 "던전 활성 동안 각 시스템은 현재 맵 종류를 확인하거나 월드 작업을 일시 중지한다"를 열어 두었고, 구현은 **일시 중지** 쪽을 택했다. 이 문서는 그중 시간으로 도는 시뮬레이션을 **월드 맵 기준으로 계속 실행**하는 쪽으로 바꾼다. [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §4

### 1.1 구현 전 동작 (2026-10-03 코드 기준, 2026-10-19 재확인)

> 2026-10-19 구현으로 아래 3단계 앞에 네임드 엔티티 내리기·연대기 마을 접기가 들어갔고(§3.1), 시계·NPC·마을 시뮬은 더 이상 멈추지 않는다(§2). 이 절은 바꾸기 전 구조를 설명한다.

던전 입장(`ActivateInterior`)은 월드를 통째로 얼려 옆에 치우고, 같은 ECS와 맵 자리에 던전을 올린다. [`DungeonManager.cs`](../../Assets/Scripts/Dungeon/DungeonManager.cs) `ActivateInterior`

1. `SuspendWorld` — 렌더 중인 월드 청크를 모두 비활성화한다. 청크 비활성화 콜백으로 NPC·건물·몬스터는 **필드에서 멀어질 때와 같은 경로**로 저장 후 비실체화되고, 입구 표현도 내려간다. [`MapManager_Dungeon.cs`](../../Assets/Scripts/Map/MapManager_Dungeon.cs), [`MapManager_Spawner.cs`](../../Assets/Scripts/Map/MapManager_Spawner.cs)
2. `SuspendExceptPlayer` — 남은 엔티티(주로 **실체화된 마을의 ECS 엔티티**: 마을·저장소 컴포넌트, 진행 중 task, 배치 오브젝트)의 컴포넌트를 풀에서 꺼내 보관하고 GameObject를 끈다. 플레이어와 플레이어의 스킬·버프만 남는다. [`ComponentManager_MapContext.cs`](../../Assets/Scripts/Manager/ComponentManager_MapContext.cs)
3. 던전 맵을 새로 만들어 `ActiveMap`에 둔다. 이때부터 `AR.s.Map`은 **던전 맵**을 가리킨다(`ActiveMap ?? _mapManager`). [`AR.cs`](../../Assets/Scripts/Manager/AR.cs)

`SystemManager.CanRun`은 플레이어가 던전 안이면 `IWorldMapSystem` 15개를 실행하지 않는다(2026-10-19 기준 — 설계 뒤 네임드 위협 3개·입구 자막 1개가 추가됐다). 여기에 `System_Time`이 들어 있어 **게임 시계도 멈춘다**. 퇴장 때는 보관한 값을 그대로 되돌리고(`RestoreSuspended`) 월드 청크를 다시 켠다. [`SystemManager.cs`](../../Assets/Scripts/Manager/SystemManager.cs), [`IWorldMapSystem.cs`](../../Assets/Scripts/Common/Interface/IWorldMapSystem.cs)

## 2. 시스템 분류

`IWorldMapSystem` 묶음에는 성격이 다른 두 종류가 섞여 있다. **월드 좌표·월드 엔티티가 꼭 필요한 시스템**은 던전 안에서 입력이 사라지므로 계속 멈춘다. **게임 시간으로 도는 추상 시뮬레이션**은 월드 맵을 명시적으로 조회하도록 고친 뒤 던전 안에서도 실행한다. [확정: 개념에서 따라옴]

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
| `System_NamedThreatDirector` (135) | 네임드 출현·단계·사라짐·소문 주인 재배정 | **일부 실행** (§3.6) | 단계·사라짐은 게임 시간과 기록만 본다. 출현은 플레이어 월드 위치가 필요하다 |
| `System_NamedThreatPresence` (136) | 네임드 엔티티 생성·제거 | 정지 | 월드 청크·엔티티 필요 |
| `System_NamedThreatTrail` (137) | 영역 안 흔적 자막 | 정지 | 플레이어 월드 좌표 필요 |
| `System_DungeonEntranceSign` (520) | 입구 소문 징후 자막 | 정지 | 플레이어 월드 좌표 필요 |

- 실행 쪽 4개와 네임드 감독은 `IWorldMapSystem`에서 빼고 새 표식 인터페이스 `IWorldSimSystem`을 붙인다. 뜻은 "게임 시간으로 도는 월드 추상 시뮬레이션 — 맵 컨텍스트와 무관하게 실행하고, 월드 맵은 `AR.s.WorldMap`으로만 조회한다"이다. `CanRun`은 이 표식을 건드리지 않는다. 네임드 감독은 던전 안이면 출현 판정만 건너뛴다(§3.6). [확정: 개념에서 따라옴] [구현: [`IWorldSimSystem.cs`](../../Assets/Scripts/Common/Interface/IWorldSimSystem.cs) — `IWorldSimSystem` 5개, `IWorldMapSystem` 10개]
- 던전 메뉴가 열려 게임플레이가 멈춘 동안(`GameplayPaused`)은 지금처럼 Input·Checkpoint 외 모든 시스템이 멈추고, 시계도 멈춘다. [확정: 지금과 같음, Q4]

## 3. 입장·퇴장 순서

### 3.1 입장: 연대기 마을을 먼저 접는다

실체화된 연대기 마을(`SettlementId ≥ 0`, `IsMaterialized`)은 **엔티티를 보관함으로 빼기 전에** 추상으로 접는다(`FoldBackSettlement`). [확정: 필수]

```text
ActivateInterior (변경 후)
  StopTransientCombat, UnloadContextDrops         (지금과 같음)
  SuspendWorld                                    (지금과 같음 — NPC·건물·몬스터 비실체화)
  네임드 엔티티 내리기                              (신규 — §3.6)
  연대기 마을 전부 FoldBackSettlement              (신규 — 필드에서 멀어질 때와 같은 순서)
  SuspendExceptPlayer                             (이제 손제작 마을 ECS와 잔여 엔티티만 보관)
  CreateDungeonMap → ActiveMap                    (지금과 같음)
```

- **왜 필수인가**: 마을 접기(`DematerializeVillage`)는 ECS 컴포넌트 값을 원장(`VillageData`)으로 되돌려 쓴 뒤 엔티티를 파괴한다. 정착지 시뮬이 던전 안에서 돌면, 월드 청크가 모두 꺼져 있으므로 실체화된 마을을 "멀어짐"으로 보고 접으려 한다. 이때 컴포넌트가 보관함에 들어가 있으면 진행 중 task·배치 오브젝트를 읽지 못해 잃고, 보관 중인 엔티티 ID를 파괴한다. 퇴장 때 `RestoreSuspended`가 파괴된 ID에 옛 컴포넌트를 되살려 유령 마을 엔티티가 생긴다. [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) `DematerializeVillage`·`FlushVillageEntityState`, [`HistoryManager.cs`](../../Assets/Scripts/History/HistoryManager.cs) `UpdateMaterialization`·`FoldBackSettlement`
- 순서는 필드와 같다. 필드에서도 청크가 먼저 꺼지고, 다음 정착지 시뮬 tick에서 마을이 접힌다.
- 접은 마을은 던전에 있는 동안 정착지 시뮬의 추상 경로(자원 생산·clock 정렬, 런타임 발전은 꺼짐)로 진행한다.
- 재접속 복원(`RestoreSessionAsync`)도 같은 `ActivateInterior`를 거치므로 같은 규칙이 적용된다.
- 구현(2026-10-19): `NamedThreatManager.DespawnAllEntities` → `DestroyTag` 즉시 정리(`FlushDestroyedEntities`) → `HistoryManager.FoldBackAllSettlements` → `SuspendExceptPlayer`. 마을 접기는 엔티티를 바로 파괴하므로(`DestroyEntity`) 따로 정리할 것이 없다. [`DungeonManager.cs`](../../Assets/Scripts/Dungeon/DungeonManager.cs) `ActivateInterior`

### 3.2 손제작 마을

손제작 마을(`SettlementId = -1`, BaseTown 등)은 추상 층에 참여하지 않아 접을 수 없고 항상 실체화 상태로 남는다. 던전에 있는 동안 이 마을의 ECS는 보관함에 있고, **원장(`VillageData`)만** 바뀐다(NPC 거래 골드, Hunt 성공 Food 유입, §3.3의 생산). [`village.md`](../../Wiki/village.md) §1

- **저장 동기화 보호** (구현 중 발견, 2026-10-19): 저장은 Villages를 실을 때마다 `VillageManager.SyncTaskToData`로 실체화 마을의 ECS(진행 중 건설 task·벽 계획 태그·`VillageComponent`)를 원장으로 옮긴다. 컴포넌트가 보관함에 있는 동안 이 동기화가 돌면 빈 task 풀을 읽어 원장의 진행 중 task와 벽 계획 플래그를 지운다. 시뮬 묶음(§6.3) 때문에 던전 퇴장 커밋·네임드 기록 저장·NPC 완료 결합 저장이 모두 Villages를 싣는다. 그래서 저장소 컴포넌트가 없는(보관 중인) 실체화 마을은 이 동기화를 건너뛴다. 필드에서는 실체화 마을에 항상 저장소 컴포넌트가 있어 동작이 같다. 입장 직전 입장 커밋이 이미 동기화했으므로 보관 중에는 원장이 정본이다. [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) `SyncTaskToData`

### 3.3 자원 생산 (손제작 마을)

`System_VillagePassiveProduction`은 지금 `VillageStorageComponent`가 있어야만 생산한다. 던전 중에는 컴포넌트가 보관함에 있어 손제작 마을의 생산이 멈춘다. [`System_VillagePassiveProduction.cs`](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs)

- 생산 계산(`VillageResourceSimulation.AdvanceWholeHours`)은 원장만 쓰므로, **컴포넌트가 없어도 원장은 진행하고 미러 갱신(`SyncStorageFromData`)만 건너뛴다.** 이미 컴포넌트가 없으면 안전하게 건너뛰는 함수다. [확정: 개념에서 따라옴 — 필드에서도 플레이어가 멀리 있는 손제작 마을은 계속 생산한다]
- 이중 생산은 생기지 않는다. 접힌 연대기 마을은 `EntityId < 0`이라 이 시스템이 건너뛰고, 정착지 시뮬은 실체화된 마을을 건너뛴다.
- 던전 중에는 이 시스템의 마을 자원 알림(`SetNotify`)을 띄우지 않는다. [확정: 2026-10-19 사용자, Q5 — §3.7]

### 3.4 퇴장: 미러를 원장에서 다시 채운다

```text
ExitAsync (변경 후)
  ... RestoreSuspended                            (지금과 같음)
  실체화 마을 미러 재동기화                          (신규 — 손제작 마을)
  FinishCompanionDungeonExit → MovePlayer → ResumeWorld   (지금과 같음)
```

- `RestoreSuspended`는 입장 시점의 컴포넌트 값을 되살리므로, 던전 중 바뀐 원장과 미러가 어긋난다. 복원 직후 실체화된 마을마다 저장소·마을 컴포넌트를 원장에서 다시 채운다(`SyncStorageFromData`, 벽 통계 동기화). 다음 생산 tick(최대 5초)에서도 맞춰지지만, 그 사이 UI가 옛 값을 보이지 않게 즉시 한다. [확정: 개념에서 따라옴] [구현: `VillageManager.ResyncMaterializedMirrors`]
- `ResumeWorld`의 청크 활성화가 NPC(던전 중 진행된 위치·상태)·건물·몬스터를 평소 경로로 실체화하고, 정착지 시뮬이 근처 연대기 마을을 다시 실체화한다. 던전 중에도 추상 tick이 돌았으므로 실체화 catch-up은 작다. [`ABSTRACT_CONSTRUCTION_DESIGN.md`](../village/ABSTRACT_CONSTRUCTION_DESIGN.md) §7
- 퇴장 후 월드는 **입장 직전 모습 그대로가 아니다.** 몬스터는 새로 스폰되고, NPC는 그동안의 활동 결과대로 다른 곳에 있거나 부상·사망했을 수 있다. 이것이 이 설계의 의도다. [확정: 목표에서 따라옴]

### 3.5 동행 NPC

기존 계약을 그대로 쓴다. 던전 안 동행 NPC는 `System_NpcSim`의 판정에서 이미 제외된다(`IsCompanionInDungeon`). NPC_COMBAT 문서는 이 제외를 "구조가 바뀌어도 동행 NPC가 판정되지 않게 하는 방어선"으로 두었으므로, 이번 변경에서 실제로 쓰이게 된다. [`NPC_COMBAT_DESIGN.md`](../npc/NPC_COMBAT_DESIGN.md) §4.5, [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §5.3

### 3.6 네임드 위협

네임드 감독은 던전 안에서도 돈다. **새 출현만 퇴장 뒤로 미룬다.** [확정: 2026-10-19 사용자, Q6] [`NamedThreatManager_Director.cs`](../../Assets/Scripts/NamedThreat/NamedThreatManager_Director.cs) `TickDirector`·`AdvanceDirector`

- 단계 전이, 사라짐(출현 180게임시간 뒤), 소문 주인 재배정, 끝난 기록 정리는 게임 시간과 저장 기록만 본다. 필드에서 플레이어가 그 마을과 멀리 있을 때와 똑같이 진행한다.
- 출현 기준은 "플레이어와 가장 가까운 마을"인데([`NAMED_THREAT_DESIGN.md`](../world_history/NAMED_THREAT_DESIGN.md) §4), 던전 안에서는 플레이어 월드 위치가 없다. 지금 `TickDirector`는 플레이어 위치를 읽어 `AdvanceDirector`에 넘기므로, 던전 안이면 출현 판정(`TryAppear`)만 건너뛰게 나눈다. 퇴장 뒤 첫 감독 판정에서 평소처럼 출현을 본다.
- 감독을 멈춘 채 시간만 흐르게 하면 안 되는 이유: 단계가 오르지 않고 180게임시간이 지나도 사라지지 않는다. 그동안 NPC 사냥(`System_NpcSim`)은 그 네임드와의 마주침을 계속 굴린다. 마주칠 네임드를 찾는 `GetActiveForVillage`는 사라질 시각을 보지 않는다.
- 엔티티·흔적(`System_NamedThreatPresence`·`System_NamedThreatTrail`)은 필드 화면용이라 멈춘다.
- **입장 때 네임드 엔티티를 먼저 내린다.** 네임드 엔티티는 청크 비활성화 콜백이 아니라 `Presence` tick이 지운다(`DespawnEntity`). 입장 때 엔티티가 남아 있으면 `SuspendExceptPlayer`가 보관함에 넣는다. 그러면 감독은 "엔티티가 있는 동안 사라짐을 미룬다" 규칙 때문에 던전에 있는 동안 사라짐을 처리하지 못한다. 필드에서 멀어질 때 `Presence`가 하는 것과 같은 처리이고, 퇴장 뒤 다시 만드는 것도 `Presence`가 평소대로 한다. [`NamedThreatManager_Presence.cs`](../../Assets/Scripts/NamedThreat/NamedThreatManager_Presence.cs)
- 던전 안의 단계 전이·사라짐 소식은 §3.7대로 소식창에 뜬다(칭호를 들은 네임드만 — 지금 규칙 그대로). 사라짐은 World·NpcState·Narrative를 바로 저장하므로, 던전 안에서 이 저장이 성공하는지 검증한다(§11).

### 3.7 알림

[확정: 2026-10-19 사용자, Q5]

- **소식창(월드 피드, `UIManager.PushWorldFeed`)은 필드와 똑같이 띄운다.** 필드에서도 보이지 않는 NPC의 평범한 사냥 성공·실패는 띄우지 않고, 부상·사망·네임드 관련 줄은 위치와 관계없이 띄운다. 던전 안에서는 모든 NPC가 보이지 않는 NPC이므로 부상·사망·네임드 소식만 뜬다. 추가 작업은 없다. [`System_NpcSim.cs`](../../Assets/Scripts/Common/System/System_NpcSim.cs) `NotifyHuntOutcome`·`NotifyDelveOutcome`·`NotifyPartyHuntOutcome`
- **마을 자원 알림은 끈다.** `System_VillagePassiveProduction`의 자원 현황 알림(`SetNotify`, "마을 N [단계]: Food …")은 근처 실체화 마을용이다. 던전 안에서는 손제작 마을 생산(§3.3)이 이 경로를 타므로 띄우지 않는다. 다른 마을 자원 알림(`ConsumeResource`, 알림을 켠 `ProduceResource`)은 던전 중 멈추는 건설 큐나 플레이어 상점 판매에서만 불린다. NPC 사냥 식량은 알림을 끈 `ProduceResource`를 쓴다.

## 4. 월드 맵 조회 규칙

**규칙**: 던전 중에도 실행되는 시뮬레이션 경로는 `AR.s.Map`이 아니라 `AR.s.WorldMap`만 쓴다. 필드에서는 두 값이 같으므로 이 정리만으로는 필드 동작이 바뀌지 않는다. [확정: 필수]

2026-10-03 검색에서 찾고 2026-10-19에 다시 확인한, 던전 맵으로 읽으면 틀어지는 지점(행 번호는 2026-10-19 기준):

| 위치 | 용도 | 던전 맵으로 읽으면 |
|---|---|---|
| [`System_NpcSim.cs`](../../Assets/Scripts/Common/System/System_NpcSim.cs) `OnFixedUpdate` 36행 | `worldSeed = AR.s.Map.mapSeed` | 던전 시드로 RNG를 만들어 활동 결과의 결정론이 깨진다 |
| `System_NpcSim.cs` `StartNextActivity`·`StartPartyHunt` 222~225·249~252행, [`NpcManager_Delve.cs`](../../Assets/Scripts/Manager/NpcManager_Delve.cs) `TryPrepareDelveTarget` 92~95행 | 맵 경계 | 던전 경계(청크 −2~16)로 사냥 경로·목표 위치가 잘린다 |
| [`NpcManager_Objective.cs`](../../Assets/Scripts/Manager/NpcManager_Objective.cs) `RecalculateObjective`·`OfferObjectiveCandidate`·`AuditObjectiveDeterminism` 199·305·988행 | 단기 목표 RNG seed `AR.s.Map.mapSeed` | 던전 시드로 굴려 단기 목표의 결정론이 깨진다 (설계 뒤 추가된 코드) |
| [`NpcManager_Ambition.cs`](../../Assets/Scripts/Manager/NpcManager_Ambition.cs) `GetNpcLootZoneLevel` | 전리품 Zone | 마을 청크 좌표가 던전 활성 청크와 겹치면 Zone 1로 읽어 전리품 등급이 틀린다 |
| [`VillageManager.cs`](../../Assets/Scripts/Village/VillageManager.cs) `RollMerchantStock` 1793·1802행 | 상점 재고 롤 seed, 재고 장비 Zone | NPC 거래가 재고를 롤하면 던전 시드·던전 Zone으로 롤한 재고가 저장된다 |
| [`HistoryManager.cs`](../../Assets/Scripts/History/HistoryManager.cs) `UpdateMaterialization` 536행 | 마을 근처 판정 `IsChunkActive` | 마을 청크 좌표가 던전 활성 청크와 겹치면 던전 안에서 마을을 실체화한다 |
| [`NpcManager.cs`](../../Assets/Scripts/Manager/NpcManager.cs) `SpawnNewNpc`·`SpawnNpc` 432·775행 | NPC 엔티티 생성 판정 `IsChunkActive` | 보충 NPC가 던전 안에 엔티티로 생성될 수 있다 |
| `NpcManager.cs` `GetRandomSpawnPositionInVillage` → [`CollisionUtil.cs`](../../Assets/Scripts/Common/Utility/CollisionUtil.cs) `IsBlockedAt`·`TryFindNearestFree` | 보충 NPC 스폰 위치 충돌 검사 | 던전 타일로 충돌을 검사한다. `CollisionUtil`은 던전 전투도 쓰므로 `AR.s.Map`을 바꾸지 않고 보충 경로에 월드 맵을 넘긴다 (§4.1) |

- 이 목록은 검색 결과이며 완전하지 않을 수 있다. 구현 때 `System_NpcSim`·`System_SettlementSim`·`System_VillagePassiveProduction`·`System_Time`의 진입점에서 닿는 호출 경로 전체(특히 `Village`·`Npc`·`History`·`CollisionUtil`·`VillageTileFinder`)를 다시 감사한다.
  - 감사 결과(2026-10-19): 표의 8곳 외에 던전 중 시뮬 경로에서 닿는 `AR.s.Map` 조회는 없었다. 표의 곳은 모두 `AR.s.WorldMap`으로 바꿨고, 보충 위치 확인은 `CollisionUtil`의 맵 인자 오버로드(`IsBlockedAt(map, …)`·`TryFindNearestFree(map, …)`)에 월드 맵을 넘긴다(기존 호출은 그대로 `AR.s.Map`). `VillageTileFinder`·마을 실체화·벽 타일 복원·`BuildingManager.PlaceBuilding`은 월드 렌더 청크가 켜져야 닿는 경로라 던전 중에는 실행되지 않는다. `DetermineGlobalNpcTarget`·`NpcRelationBackfill`·연대기 생성의 `AR.s.Map`은 새 월드 초기화 전용이다. 네임드 감독은 원래 `AR.s.WorldMap`만 쓴다. 맵 조회가 아닌 빈틈 하나(`SyncTaskToData`)는 §3.2에 적었다.
- **진단 보호**: `IWorldSimSystem` 실행 중에 플레이어가 던전 안이면 `AR.s.Map` getter가 경고를 남기게 한다. 감사에서 빠진 경로를 플레이 중에 잡기 위해서다. [구현: 에디터 전용(`#if UNITY_EDITOR`) — AGENTS.md의 "가벼운 상시 검사는 에디터에서만, 플레이어 빌드 제외" 규칙을 따라 개발 빌드에는 넣지 않았다. `SystemManager`가 시스템 실행 앞뒤로 `AR.BeginWorldSimMapGuard`·`EndWorldSimMapGuard`를 부르고, 경고(`[WorldSimMapGuard]`)는 시스템·호출 위치마다 한 번만 남긴다]
- 월드 맵을 `AR.s.WorldMap`으로 조회하더라도 던전 중 월드 타일맵은 비어 있다(`SuspendWorld`가 렌더 청크를 지운다). 렌더 타일맵을 읽는 `MapManager.IsWalkable` 같은 함수는 던전 중 시뮬 경로에서 쓰지 않는다. 청크 데이터 조회가 청크 활성화·스폰 같은 부작용을 내지 않는지도 검증한다. [제안]

### 4.1 NPC 보충

전역 총량 보충(`TryReplenishGlobalPopulation`, 6게임시간 간격)은 **던전 안에서도 그대로 진행한다.** [확정: 2026-10-19 사용자, Q7] [`NpcManager.cs`](../../Assets/Scripts/Manager/NpcManager.cs) `TryReplenishGlobalPopulation`·`GetRandomSpawnPositionInVillage`

- 필드에서도 보충은 플레이어와 먼 마을에 자주 일어난다. 이때 스폰 위치는 화면 타일맵이 아니라 청크 데이터(`MapManager.IsTileBlocked` → `GetTileAt`)로 확인한다. 청크가 꺼져 있으면 데이터로만 생기고, 엔티티는 청크가 켜질 때 `SpawnNpc`가 위치를 다시 확인해 만든다. 던전 안도 같은 경우다.
- 바꿀 것은 위치 확인이 던전 맵을 보지 않게 하는 것뿐이다(§4 표 마지막 행). 던전 맵의 `GetTileAt`은 던전 타일을 샘플하므로, 그대로 두면 월드 마을 좌표를 던전 지형으로 검사한다.
- 처음 기본안(보충을 퇴장 뒤로 미룸)은 이 조회를 믿을 수 없다는 이유였다. 원인이 `AR.s.Map`이 던전을 가리키는 것이었으므로 버렸다.
- 월드 `GetTileAt`은 아직 없는 청크 데이터를 만든다(`GetOrCreateChunk`). 필드의 먼 마을 보충과 같은 동작이지만, 던전 중에 청크 활성화·스폰 같은 부작용이 없는지 검증한다(§11).

## 5. NPC 던전 완료

**플레이어가 들어가 본 던전만 막는다.** 지금 NPC 던전 완료(`TryCompleteByNpc`)는 플레이어가 던전 안이면(`IsInside`) 모든 던전을 거부한다. 이 검사(`EvaluateNpcCompletion`의 `PlayerInside` 거부)를 뺀다. [확정: 2026-10-19 사용자, Q2] [`DungeonManager_NpcCompletion.cs`](../../Assets/Scripts/Dungeon/DungeonManager_NpcCompletion.cs)

- 플레이어가 들어가 본 던전은 `DungeonRecord`가 있으면 거부하는 검사(`PlayerEntered`)로 이미 막힌다. 지금 들어가 있는 던전도 입장 때 기록이 생기므로 여기서 막힌다. 저장 트랜잭션 중 거부(`Busy`), 마을당 72시간 쿨다운, 절반 넘는 교체 금지도 그대로다.
- `IsInside` 검사는 던전 안에서 월드가 멈추던 때 넣은 방어선이라 실제로 걸린 적이 없다. 처음 기본안은 슬롯 교체가 던전 중 멈춘 `System_DungeonPlacement`의 입구 역색인 재구축을 건드린다는 이유로 이 검사를 유지하려 했다. 코드는 이미 이 경우를 처리한다.
  - 입구 역색인은 바로 다시 만들지 않고 `_entranceIndexDirty`로 표시해 다음 `TickPlacement`(퇴장 뒤)로 미룬다. 슬롯 객체를 제자리에서 바꾸므로 옛 참조도 즉시 `HasEntrance = false`를 본다.
  - 던전 안에 있는 동안 월드 입구 엔티티는 청크 비활성화(`RefreshEntranceChunk(…, false)`)로 이미 내려가 있다. 퇴장 때 청크가 켜져도 `HasEntrance = false`인 슬롯에는 입구를 만들지 않는다. [`DungeonManager_Entrances.cs`](../../Assets/Scripts/Dungeon/DungeonManager_Entrances.cs) `RefreshEntranceChunk`
  - 새 입구는 원래도 교체 쿨다운 뒤에 배치하므로 퇴장 뒤 배치 시스템이 처리한다.
  - 미저장 NPC 완료가 있으면 던전 체크포인트가 Dungeons와 시뮬 묶음을 함께 확정한다(§6.3).
- 영향: 던전에 머무는 비율과 관계없이 게임 시간당 NPC 완료 빈도가 필드와 같다(§9 D63). 처음 기본안의 `PlayerInside` 거절 수 집계는 필요 없다.
- 검증: NPC 완료가 소문을 미해결로 끝내는 처리(`EndDungeonNarrativeUnresolved`)가 던전 안에서 화면 알림을 띄우는지 확인한다(§3.7, §11).

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
- 던전 체크포인트(30초)는 Player·World·Dungeons 범위를 유지한다. 단, 미저장 NPC 던전 완료가 있으면 슬롯과 보상이 갈라지지 않도록 기존 완료 결합이 시뮬 묶음 전체로 확장된다. NPC 변화 자체로 즉시 저장을 요청하지 않는 원칙(D47)은 유지한다. 던전 안에서도 다른 던전의 NPC 완료가 일어나므로(§5) 던전 중에 이 결합이 실제로 쓰인다.
- 필드 상점 저장은 플레이어 구매 재고(`MerchantStock`) 때문에 Villages가 꼭 필요하다. 그래서 이 규칙을 따르면 상점 거래마다 시뮬 묶음 전체를 싣게 된다. Villages를 빼면 구매 뒤 비정상 종료 시 재고가 줄지 않아 같은 물건을 다시 살 수 있으므로 대안이 아니다.
- 비정상 종료 시 시뮬 결과는 마지막 시뮬 묶음 저장 시점으로 돌아간다. 필드에서도 시뮬 결과는 주로 수동 전체 저장 때 남으므로 같은 수준이다.
- 비용: S-1~S-3 캐시 위에 증분 파일 쓰기를 적용했다. 디스크 확정 세대가 같은 파트/샤드는 JSON 직렬화를 건너뛰고, 후보도 schema·길이·SHA-256이 같으면 새 파일을 만들지 않는다. 많이 바뀐 저장은 여전히 32샤드 모두를 쓸 수 있으며 전체 재생성 대상의 복사 비용은 유지한다([`SAVE_SNAPSHOT_DESIGN.md`](../tech/SAVE_SNAPSHOT_DESIGN.md) §13).
  - 던전 입장·퇴장·완료 커밋의 NpcState 요청은 묶음 전체로 확장하며, 기존 정책대로 캐시를 전체 재생성한다. Player·World·Dungeons 체크포인트는 미저장 NPC 완료가 있을 때만 Dungeons와 묶음을 함께 확정한다.
  - **상점 거래 저장**은 Player·World + 묶음 전체를 확정한다. 모든 여섯 파트를 매번 새 파일로 쓰는 것은 아니다. 저장 중 추가 변경·실패한 변경은 다음 요청까지 보존한다. 실제 상점·던전의 전체 메인/백그라운드 비용 실측은 후속이다.

## 7. 게임 시간을 보는 다른 규칙

던전 안에서 게임 시간이 흐르면 아래 규칙도 함께 흐른다. 모두 "월드 시간이 흘렀다"는 뜻이라 의도에 맞으므로 **그대로 둔다.** [확정: 개념에서 따라옴]

- 상점 재입고 회차, 성소 쿨다운(`UIShrine`), 던전 입구 교체 쿨다운(`PlacementReadyGameTime`)
- NPC 부상 회복·쇼핑 쿨다운·절도 쿨다운, 대화·소문의 "N일 전" 표현
- 네임드 위협의 단계·사라짐·출현 간격(§3.6)
- 던전 내부 규칙이 게임 시간을 쓰는 곳은 완료 트랜잭션의 교체 쿨다운 기준 시각 하나뿐이다. 영향이 없다.

## 8. 성능

- 던전 중에도 NPC 시뮬(0.25초마다 12명)과 정착지 시뮬(0.5초마다 정착지 48곳 + 마을 건설 12곳)이 돈다. 필드와 같은 비용이지만 던전 전투 프레임과 겹치므로 Profiler로 실측한다.
- 입장 프레임에 근처 연대기 마을 접기가 추가된다(보통 1~3곳). 퇴장 때는 필드 이동과 같은 경로로 다시 실체화된다.

## 9. 영향받는 튜닝·문서

- **D63**(NPC 던전 완료 빈도 목표)은 "플레이어가 던전 안에 있는 동안 월드 시간이 멈춘다"를 전제로 한다. §5대로 들어가 본 던전만 막으면 게임 시간당 완료 빈도는 던전 안에서도 필드와 같다. 대신 던전 안에서도 시간이 흐르므로, 실제 플레이 시간당 빈도는 던전에 머무는 시간만큼 늘어난다. 구현 뒤 다시 측정한다. [`NPC_AMBITION_DESIGN.md`](../npc/NPC_AMBITION_DESIGN.md) D63
- **경제 밸런스**: 물가 시계는 게임 시간이다([GOLD_BALANCE_DESIGN.md](../economy/GOLD_BALANCE_DESIGN.md) D125, 목표 420게임일). 골드 설계를 먼저 적용했고(2026-10-03), 이 문서가 구현되면 물가 시계가 던전을 포함한 플레이 시간(메뉴 일시정지 제외)이 된다. 그 전까지는 던전에 머무는 시간만큼 실제 안정 시점이 늦어진다.
- **구현 후 갱신할 문서**(지금 동작을 설명하므로 구현 전에는 고치지 않는다) — 2026-10-19 반영 완료. 같은 내용을 담은 DUNGEON_RUNTIME §3.3 보호 규칙, NPC_AMBITION §7.1 보호 규칙, NAMED_THREAT 시간·예외·시스템 표도 함께 고쳤다. 위키는 아직 반영 전: [`DUNGEON_RUNTIME_DESIGN.md`](DUNGEON_RUNTIME_DESIGN.md) §5.3 "시스템 가동", [`NPC_COMBAT_DESIGN.md`](../npc/NPC_COMBAT_DESIGN.md) §4.5 참고 문장, NPC_AMBITION D63 설명, [`NAMED_THREAT_DESIGN.md`](../world_history/NAMED_THREAT_DESIGN.md) §4.1 "던전 안에서는 월드 시스템과 함께 감독도 멈춘다" 문장. 위키(`Wiki/current_status.md`·`Wiki/README.md`의 던전 중 월드 시뮬 "초안" 표기)는 구현 뒤 사용자 확인을 받아 반영한다.

## 10. 열린 질문

| # | 질문 | 기본안 |
|---|---|---|
| Q1 | ~~시간 배율: `AR.prefab`은 현실 5초 = 게임 1시간인데, 코드 기본값과 D63 설명은 60초를 전제로 한다. 어느 쪽이 의도인가?~~ | **닫음 (2026-10-03, GOLD_BALANCE D124)** — 60초 = 게임 1시간. `AR.prefab` 5 → 60 적용 |
| Q2 | ~~던전 안에 있는 동안 NPC 던전 완료를 모두 막을지, 플레이어가 들어간 던전만 막을지~~ | **닫음 (2026-10-19 사용자)** — 플레이어가 들어가 본 던전만 막는다. `IsInside` 검사를 뺀다 (§5) |
| Q3 | 상점 거래 저장에도 시뮬 묶음을 실으면 거래마다 NPC 32샤드를 쓴다. 그대로 갈지, 바뀐 샤드만 쓰는 증분 저장을 먼저 넣을지 | **방향 결정 (2026-10-04)**: 증분 저장 대신 **상시 스냅샷**으로 저장 시 전체 깊은 복사를 우회한다 → [SAVE_SNAPSHOT_DESIGN.md](../tech/SAVE_SNAPSHOT_DESIGN.md). 샤드 단위 증분은 샤드가 약 5초마다 바뀌어 효과가 작다. **S-1 NPC·S-2 마을·S-3 관계/파티/인구/WorldSim 구현·핵심 기능 검증 통과**(2026-10-05). S-3 계약 28/28, 실제 writer/reader 왕복 4/4, 1일 복제 시뮬 누락 0이며 네 파트 캡처는 36~57ms → 1.1~1.7ms였다(전체 저장 시간과 구분). S-2/S-3 장기 검증·마을 프레임 비용 한계는 **스냅샷 문서 §11.3·§12.4**를 따른다. **2026-10-05 사용자 요청으로 묶음·증분 파일 쓰기도 구현했다(스냅샷 문서 §13)**. 초기 결정은 캡처 우회였으며 이번 개정은 그 위에 파일 재사용을 추가한다. 실제 플레이의 전체 비용 실측과 이 문서의 던전 중 월드 시뮬 게임플레이는 후속이다 |
| Q4 | ~~던전 메뉴가 열린 일시정지 중에도 시간을 멈출지~~ | **닫음 (2026-10-19)** — 멈춤 유지, 지금과 같음 (§2) |
| Q5 | ~~던전 중 월드 쪽 알림(마을 자원, NPC 관련)을 띄울지~~ | **닫음 (2026-10-19 사용자)** — 소식창은 필드와 똑같이 띄우고, 마을 자원 알림만 끈다 (§3.7) |
| Q6 | ~~네임드 위협 감독(설계 뒤 추가)을 던전 중에 어떻게 할지~~ | **닫음 (2026-10-19 사용자)** — 단계·사라짐은 진행하고 새 출현만 퇴장 뒤로 미룬다. 입장 때 네임드 엔티티를 먼저 내린다 (§3.6) |
| Q7 | ~~던전 중 NPC 전역 보충을 미룰지~~ | **닫음 (2026-10-19 사용자)** — 그대로 진행한다. 위치 확인만 월드 지도를 보게 한다 (§4.1) |

## 11. 구현 순서와 검증

저장 캡처(S-1~S-3)와 §6.3의 묶음·증분 저장 선행 구현을 완료했다([SAVE_SNAPSHOT_DESIGN §13](../tech/SAVE_SNAPSHOT_DESIGN.md)). 아래 던전 게임플레이 단계는 2026-10-19에 모두 끝냈다(결과 §11.1). 진행은 준비 → 본체 → 검증 1회로 하고, 준비 단계에서는 따로 검증하지 않는다(2026-10-19 개정 — 처음 안은 월드 맵 조회 정리 직후 단독 검증이었다).

0. **착수 전 기준값**: 진단 `Run(30)`의 라이브 해시·지표를 떠 둔다. 필드 동작이 바뀌지 않았는지 마지막 검증에서 비교한다.
1. **준비 — 월드 맵 조회 정리**(§4): 던전 안에서도 도는 시뮬 경로의 `AR.s.Map`을 `AR.s.WorldMap`으로 바꾸고(보충 위치 확인은 월드 맵을 넘김), 개발 빌드 경고(§4 진단 보호)를 넣는다. 필드에서는 `AR.s.Map == AR.s.WorldMap`이므로 동작이 바뀌지 않아야 한다.
2. **본체 — 시스템 분류와 입장·퇴장 순서**(§2·§3·§5): `IWorldSimSystem`, 입장 시 네임드 엔티티 내리기·연대기 마을 접기, 손제작 마을 생산의 원장 경로와 던전 중 자원 알림 끄기, 퇴장 시 미러 재동기화, 네임드 감독의 던전 중 출현 건너뛰기, NPC 완료 `IsInside` 검사 제거.
3. **검증 1회**: 진단 `Run(30)`(기준값과 비교)·`Run(60)`, 던전 입장·퇴장·완료·사망, 던전 안 저장·재접속, 아래 필수 검증, Profiler 실측(§8). 선행 저장 정책이 던전 입장·퇴장·완료 커밋에 적용되고 30초 체크포인트 범위가 유지되는지(§6.3)도 여기서 본다.

필수 검증:

- 던전 안에 N분 머무르면 게임 시간이 그만큼 흐른다(현실 60초당 1게임시간, Q1).
- 던전 중 NPC 활동 전이(Rest·Hunt·Trade·Delve)와 마을 Gold가 진행되고, 닫힌 경제 검산 오차가 0이다.
- 던전 맵 위에 월드 NPC·몬스터·마을·네임드 엔티티가 생기지 않는다.
- 던전 중 시뮬 경로의 `AR.s.Map` 접근 경고가 0이다(§4 진단 보호).
- 퇴장 후 근처 연대기 마을이 실체화되고, 손제작 마을의 저장소 미러가 원장과 같다.
- 손제작 마을(BaseTown)의 자원 생산이 던전 중에도 진행되고, 이중 생산이 없고, 자원 알림이 뜨지 않는다.
- 입장·퇴장 커밋 직후 강제 종료 → 로드 시 마을 Gold + NPC Gold 합계가 커밋 시점과 같다.
- 동행 NPC는 던전 중 판정에서 제외되고, 결투 중 저장 거부는 그대로다.
- 플레이어가 들어가 본 던전의 NPC 완료는 0건이고, 다른 던전은 던전 안에 있는 동안에도 NPC 완료가 일어난다. 퇴장 뒤 완료된 슬롯의 옛 입구는 없고, 교체 쿨다운 뒤 새 입구가 생긴다.
- 던전 안에서 네임드 단계가 오르고, 출현 180게임시간이 지나면 사라지며, 그 뒤 NPC가 그 네임드와 마주치지 않는다. 던전 중 새 출현은 없고 퇴장 뒤 출현 판정이 돈다. 던전 중 사라짐 저장이 성공한다.
- 소식창은 던전 안에서도 부상·사망·네임드 소식을 띄운다.
- 던전 중에도 전역 보충이 일어나고, 보충 NPC가 던전 맵 위에 엔티티로 생기지 않는다. 퇴장 뒤 그 NPC가 걸을 수 있는 자리에 나타나고, 던전 중 청크 활성화·스폰 부작용이 없다.

검증 주의: 플레이 모드 입장·퇴장·저장은 실제 세이브에 커밋되므로, 테스트 전에 세이브 폴더를 백업하고 끝나면 복원해 동일한지 확인한다.

### 11.1 구현·검증 결과 (2026-10-19)

코드 위치: `IWorldSimSystem.cs`(신규), `SystemManager`(분류·진단 보호 호출), `AR`(진단 보호), `DungeonManager`(입장·퇴장 순서), `DungeonManager_NpcCompletion`(`PlayerInside` 제거), `NamedThreatManager_Director`·`_Presence`, `HistoryManager`, `VillageManager`, `System_VillagePassiveProduction`, §4 표의 월드 맵 조회 8곳, `CollisionUtil`(맵 인자 오버로드), 진단 `VerifyNpcCompletionRules`(규칙④ 추가). 라이브 세이브(게임 약 9~31h)로 검증했고 끝난 뒤 세이브를 백업과 같게 복원했다.

- **컴파일**: 오류 0, 바꾼 파일의 새 경고 0. `IWorldSimSystem` 5개 + `IWorldMapSystem` 10개.
- **필드 동작 불변 (Run(30))**: 착수 전과 구현 뒤 모두 라이브 해시 전후 동일, P2 체크리스트 PASS 14/FAIL 0, 절도 감사 7/0·5/0, 닫힌 경제 검산 오차 0, 결정론 불일치 0, 저장 표시 누락 0. 시작 시각이 0.3게임시간 달라 해시 값 자체와 지표는 같지 않다(기준 → 구현 뒤: Rest/Hunt/Trade/Delve 43.30/37.38/14.63/4.69% → 43.08/37.57/14.73/4.62%, Hunt 성공 64.49 → 64.54%, NPC 완료 0.400 → 0.367회, 전투력 +2.47 → +2.50%).
- **Run(60)**: 해시 동일, 닫힌 경제 0, 절도 감사 7/0·5/0, 결정론 0. P2 FAIL 2건(업그레이드 뒤 자랑 토픽 0명, 30일 기준을 적용한 NPC 완료 0.780회)은 이전부터 알려진 실패다.
- **NPC 던전 완료 규칙 진단**: 20/20 PASS — 새 규칙④ "던전 안에서 들어가 있는 던전은 `PlayerEntered`, 같은 마을 다른 던전은 완료" 포함.
- **네임드 30일 진단**: 출현 10, 한 마을 2마리 이상 0, 간격·사라짐 시각 위반 0, 네임드 사망 0, 소문 사실 대조 45/45. "3단계 늘어난 몫" +10.5%p FAIL은 착수 전 기준 실행에도 있던 튜닝 수치다(+12.1%p).
- **시나리오 진단**(`ARPG/Dungeon/Run Scenario Diagnostics`): 전체 PASS(묶음 저장 계약 49/49 포함).
- **플레이 검증** (모두 통과):
  - 입장 커밋·퇴장 커밋·완료 커밋·사망 퇴장 커밋이 모두 시뮬 묶음을 실었다(`Simulation` 범위). 던전 체크포인트는 Player·World·Dungeons이고, 미저장 NPC 완료가 있을 때만 Simulation을 함께 실었다.
  - 입장 때 실체화된 연대기 마을(v151)을 접었고, 실체화된 네임드 엔티티를 내렸다. 던전 맵 위 NPC 엔티티는 동행 1명뿐이었고, 월드 렌더 청크는 던전 내내 0이었다.
  - 시간: 실제 72.5초에 1.147게임시간(약 63초/게임시간). 입장 뒤 1.7게임시간 동안 NPC 300명이 활동을 진행했다.
  - BaseTown: 저장소 컴포넌트가 보관 중인 동안 원장이 생산됐다(Food·Wood 21 → 23, Gold 39 → 49). 던전 안 마을 자원 알림은 0건, 퇴장 직후 미러 = 원장(44/41/8, 사망 퇴장 50/50/10).
  - 소식창: 던전 안에서 NPC 부상 줄과 네임드 단계·사라짐 줄이 떴다. NPC 완료가 소문을 미해결로 끝내는 처리는 화면 알림 없이 다음 대화의 끝 알림만 남긴다(코드 확인).
  - 네임드(출현 시각을 당겨 시험): 던전 안에서 2단계 전이, 사라짐과 저장(World·NpcState·Narrative 범위, 성공)이 일어났다. 사라진 뒤 그 마을에 활성 네임드가 없어 NPC 마주침이 생길 수 없다. 출현 간격이 지났어도 던전 안 새 출현은 없었고, 퇴장 뒤 첫 판정에서 v151에 새 네임드가 나왔다.
  - NPC 완료: 던전 안에서 들어가 있는 던전은 `PlayerEntered`, 같은 마을 다른 슬롯은 완료됐다. 자연 발생 완료도 던전 안에서 2건 있었다. 퇴장 뒤 옛 입구 엔티티는 없었고(옛 자리 청크 활성 상태에서 0개), 교체 쿨다운 뒤 새 입구가 생겼다.
  - 전역 보충: 던전 안에서 1명이 보충됐다(데이터만, 엔티티 컴포넌트 없음, 월드 기준 막히지 않은 자리). 월드 데이터 청크만 45 → 46개(Warm)로 늘고 렌더·스폰은 없었다. 퇴장 뒤 그 자리 청크를 켜자 걸을 수 있는 칸에 실체화됐다.
  - 동행: 던전 안 동행 NPC는 Rest 종료 시각이 지나도 판정되지 않았다. 결투 중 저장은 수동·체크포인트 모두 거부됐다.
  - 던전 안 전체 저장 → 재접속: 던전 안으로 복원, 동행 재소환, 월드 렌더 청크 0, 시간 계속.
  - 사망 퇴장 커밋 직후 강제 종료 → 저장 파일 합계: 마을 Gold 31,664 + 생존 NPC Gold 113,469 = 145,133으로 커밋 시점 값과 같다.
  - `[WorldSimMapGuard]` 경고 0건, 오류·예외 0건.
- **성능 (에디터, 던전 안)**: `System_NpcSim` tick 평균 0.256ms·최대 5.94ms(0.25초마다, 초당 약 1.0ms), `System_SettlementSim` 평균 0.211ms·최대 0.33ms(0.5초마다, 초당 약 0.4ms), 생산·네임드 감독 약 0.001ms. 던전 프레임 메인 스레드 평균 4.65ms·p95 5.95ms·최대 10.47ms, FixedUpdate 스크립트 평균 0.02ms. 입장 때 연대기 마을 1곳 접기 4.0ms.
- **못 한 확인**: 실제 전투 중 프레임 측정(전투 없이 측정), 단계 전이·사라짐을 실제 시간으로 기다린 확인(출현 시각을 당겨 시험), 던전 안 라이브 닫힌 경제 검산(진단 Run(30)·Run(60)으로 대신), 진행 중 건설 task가 있는 손제작 마을에서의 §3.2 저장 동기화 보호(시험 세이브의 BaseTown에 task가 없어 코드 확인만).
- **발견한 기존 문제 (이번 변경과 무관)**: `System_VillagePassiveProduction`의 `_lastProcessedHour`가 로드 때 0에서 시작한다(시스템 생성이 저장 시각 로드보다 먼저). 그래서 로드할 때마다 실체화 마을(BaseTown)이 게임 시작부터의 시간을 5초에 1시간씩 다시 생산한다(이번 검증에서 로드 직후 Gold +50, 31h 세이브 기준). → 2026-10-20 사용자 요청으로 수정했다(첫 tick에서 불러온 시각으로 잡음, [GOLD_BALANCE_DESIGN §11.2](../economy/GOLD_BALANCE_DESIGN.md)).
