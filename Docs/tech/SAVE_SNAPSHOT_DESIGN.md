# 세이브 스냅샷 설계 (S-1 NPC 상태 · S-2 마을 · S-3 시뮬)

> **상태 (2026-10-05)**: **S-1~S-3 스냅샷 코드 구현 완료·핵심 기능 검증 통과**. 이를 기반으로 **시뮬 6개 파트 묶음·증분 저장을 구현했다(§13)**. NPC 상태는 §7.1~7.2, 마을은 §11, 관계·파티·인구·WorldSim은 §12에 기록한다. S-2·S-3의 30일 장기 검증·일부 실제 플레이 경로와 마을 정리 평균 1ms 미만 목표는 남아 있다(§12.4).
>
> **한 줄**: 저장할 때 메인 스레드에서 전부 깊은 복사하던 것을, 메모리에 늘 들고 있는 **저장 스냅샷**으로 바꾼다. 바뀐 NPC·마을·시뮬 항목만 프레임 끝에 복사해 두고, 저장은 캐시 DTO로 루트를 조립한다. 전체 재생성 대상 저장의 복사 비용은 유지한다.
>
> **범위**: NPC 상태(`npc_state_00~31`)·마을(`villages`)·사회 관계(`npc_social`)·파티(`npc_parties`)·인구 메타(`npc_meta`)·월드 시뮬(`world_sim`)에 상시 스냅샷을 적용한다. 하나라도 저장 요청되면 여섯 파트를 하나의 manifest에서 함께 확정하고 변경 없는 파일은 기존 참조를 재사용한다. S-1 이력은 §1~9, S-2는 §11, S-3는 §12, 묶음·증분 계약은 §13에 기록한다.
>
> **정본 관계**: 파티션 구조·원자 커밋·복구 계약은 [SAVE_PARTITION_DESIGN.md](SAVE_PARTITION_DESIGN.md)가 정본이다. S-1~S-3는 캡처를 바꿨고 §13은 저장 범위·파일 재사용을 확장한다. 파일 형식·manifest schema·커밋 순서는 유지한다.

---

> **마을 2단계 상태 (2026-10-05)**: 구현 완료·핵심 기능 검증 통과. `VillageManager`가 마을별 불변 DTO와 변경 목록을 소유하며, 상점 부분 저장은 전체 마을 깊은 복사 대신 캐시 루트를 넘긴다. 장기 진단 미완료·프레임 비용 목표 미달은 §11.3에 기록했다. 추가 절도 진단의 시간 설정 오류는 §11.4에서 수정·재검증했다.

## 0. 새 세션 진행 방법 (먼저 읽기)

- **읽을 것**: 이 문서와 §5의 코드 파일만 읽는다. 위키·다른 설계 문서는 읽지 않아도 된다. 필요한 사실은 이 문서에 옮겨 두었다.
- **단계**: 준비(§6.1) → 본체(§6.2) → 검증(§7). 같은 세션이 끝까지 이어서 진행한다.
- **검증은 구현이 다 끝난 뒤 한 번만 한다** [확정: 2026-10-04 사용자]. 준비가 끝났다고 컴파일 확인·진단·플레이 모드 검증을 하지 않는다. 컴파일 확인도 §7에서 처음 한다. 컨텍스트를 아끼기 위해서다.
- **1단계 시작 전 확인**: §8 Q1·Q2는 2026-10-04에 이미 기본안으로 확정했다. 마을 2단계도 같은 재생성·검증 정책을 이어간다.
- 진행 중 이 문서에 없는 결정이 필요하면 그 시점에 멈추고 보고한다.
- **커밋하지 않는다.** 사용자가 직접 커밋한다.
- 응답과 보고는 한국어로 한다.

## 1. 배경

### 1.1 실측 (`[SaveTiming]`, 2026-10-03~04, NPC 2,406명)

| 저장 | 크기 | 메인 스레드 멈춤 |
|---|---|---|
| 전체 저장 (`All`) | 6.3~6.5MB | 254~420ms (거의 전부 깊은 복사) |
| 상점 거래 저장 (Player·World·Villages) | 1.79MB (그중 마을 1.77MB) | 72~80ms |

- 멈춤의 원인은 `SaveDtoSnapshot.Capture`의 깊은 복사다. JSON 변환·파일 쓰기는 백그라운드라 화면을 멈추지 않는다.
- 복사 속도는 대략 1MB당 40ms다. NPC 상태 2.86MB가 전체 저장 멈춤의 약 45%(약 115ms)이고, NPC 한 명은 약 0.05ms다(추정).
- 마을 파티션은 디스크에서는 855KB인데 플레이 중 저장에서는 1.7MB 안팎이다(상점 재고 롤로 보임, 추정).

### 1.2 왜 이 작업을 하는가

- [DUNGEON_WORLD_SIM_DESIGN.md](../map/DUNGEON_WORLD_SIM_DESIGN.md) §6.3은 "시뮬 묶음(NPC 상태·관계·파티·인구·마을·월드 시뮬)은 항상 함께 저장한다"를 제안한다. 그대로 하면 상점 거래 저장마다 NPC 32샤드를 깊은 복사하게 된다(그 문서의 Q3).
- 스냅샷 방식이면 저장 시점의 복사 비용이 거의 0이 되므로, 묶음 저장이 싸지고 Q3가 풀린다. 전체 저장·던전 출입 저장의 멈춤도 함께 준다.
- **1단계(NPC)만으로는 눈에 보이는 효과가 작다.** NpcState를 싣는 저장은 대부분 던전 출입처럼 화면 전환 뒤에 있다. 전환 없이 일어나는 것은 결투 종료 저장 정도다. 실제 효과는 2단계(마을)와 묶음 규칙이 함께 들어갈 때 나타난다. 1단계는 구조를 세우고, 가장 큰 파티션에서 그 구조를 검증하는 단계다.

### 1.3 검토했지만 채택하지 않은 안

| 안 | 채택하지 않은 이유 |
|---|---|
| 플레이어 거래 기록(상점 저장에서 마을을 빼고 구매 기록만 저장) | 상점 저장 문제만 푼다. 스냅샷이 묶음 저장 비용 전체를 해결한다 |
| 샤드 단위 증분 저장만 적용 | 처음에는 샤드(약 75명)가 평균 약 5초마다 바뀌므로 효과가 작다고 보았다. 2026-10-05 사용자 요청으로 스냅샷 위에 증분 파일 쓰기를 추가했다(§13). 갱신량이 많으면 여전히 대부분의 샤드를 쓰게 된다 |
| NPC 가방 분리 파티션 | 상점 일관성에 필요한 게 바로 가방·골드·장비 쪽이라 효과가 작다. 절도·치료에서 새 어긋남이 생긴다(D98 개정과 충돌) |
| 저장 시점에 바뀐 NPC만 복사 | 저장 간격이 길면 복사량이 늘어난다(5분이면 약 57%). 스냅샷은 간격과 상관없다 |

## 2. 확정 결정

| # | 결정 |
|---|---|
| S1 | **상시 스냅샷**: `NpcManager`가 "NPC ID → 저장용 DTO(`SaveDtoSnapshot.Capture` 결과)" 사전을 늘 들고 있다. NpcState 저장은 이 스냅샷으로 샤드를 만든다 |
| S2 | **변경 표시 함수 = `NpcManager.MarkSaveDirty(int entityId)`**. `NpcSaveData.SetDirty()`·`IsDirty` 필드·`GetSaveDataForWrite`는 만들지 않는다. `GetSaveData`는 그대로 둔다 |
| S3 | **바뀐 NPC 목록** = `HashSet<int>`(중복 방지) + `List<int>`(for 순회). `NpcManager` 인스턴스가 소유하고 `Reset()`·로드 때 비운다. static 금지 |
| S4 | **표시 위치 = 변경 함수의 입구**. 끝에서 표시하면 중간 `return` 경로가 일부만 고친 채 빠져나간다. 입구에서 표시하면 최악이 쓸데없는 복사 한 번이다. NPC 데이터는 NpcManager 안에서만 고친다(예외: `System_NpcSim`). 단, 대부분 아무것도 바꾸지 않는 함수(순회·판정 함수, 지울 게 없는 정리)의 입구에서는 표시하지 않는다. 그러면 헛복사가 가끔이 아니라 대부분이 된다. 이런 함수는 실제로 바꾸는 지점에서 표시한다(§7.2) |
| S5 | **복사 시점은 두 곳**: ① 프레임 끝 정리(LateUpdate 마지막 시스템) ② 저장 직전 정리. 표시 순간에는 복사하지 않는다. 복사되는 건 정리 시점의 상태다 |
| S6 | **교체만 하고 수정하지 않는다**: 스냅샷 항목은 한 번 만들면 고치지 않고 새 DTO로 갈아 끼운다. 저장은 스냅샷 사전을 얕게 복사해 백그라운드로 넘긴다. 백그라운드가 직전 저장을 쓰는 중에도 안전하다 |
| S7 | **복사 단위 = NPC 한 명 전체**(항목 단위로 나누지 않는다) |
| S8 | **처음 스냅샷은 로딩 중에 전부 복사한다**. 전원을 표시해 첫 프레임에 몰아 복사하지 않는다 |
| S9 | **전체 재생성**: 정해진 저장에서는 표시를 무시하고 스냅샷을 통째로 다시 만든다. 표시 누락의 피해를 그 시점까지로 가둔다. 시점은 §8 Q1 |
| S10 | **누락 검증 진단**: 스냅샷과 새 복사본을 NPC·항목별 JSON으로 비교한다. 30일 시뮬 진단은 NPC마다 처리 전에 "표시 없이 바뀐 NPC"를 검사한다(§7 V4). 에디터·개발 빌드 전용이고 출시 빌드에는 넣지 않는다 |
| S11 | **전환 스위치**: 준비 단계에서는 꺼 둔다(저장 경로는 지금 그대로). 본체 끝에 켠다. 문제가 생기면 끄는 것으로 되돌린다 |
| S12 | **복사 실패는 그 NPC만 격리한다**: `Capture`가 거부하는 데이터(외부 형식·converter·순환 등)를 만나면 그 NPC만 실패 목록(`_snapshotFailedIds`)에 넣고 나머지 복사는 계속한다. 바뀐 NPC 목록은 항상 비운다. 로그는 NPC당 처음 한 번만 남긴다. 다시 표시돼 복사에 성공하면 목록에서 뺀다. 저장 조립 직전에 실패 NPC를 한 번 더 복사하고, 그래도 실패하면 저장을 중단한다. 낡은 DTO를 조용히 저장하지 않기 위해서다 |
| S13 | **이동 중 위치는 저장하지 않는다** [확정: 2026-10-04 사용자]: 화면 밖에서 이동 중인 NPC(혼자 이동·원정 파티원)의 위치는 활동·파티 경로(출발점·목적지·귀환점·가는 구간 비율·출발/도착 시각)로 언제든 계산되는 값이다. 그래서 위치 갱신(`UpdateSoloTravelPosition`·`UpdatePartyTravelPositions`)은 표시하지 않고, 로드 때 경로로 다시 계산해 넣는다(`RestoreTravelPositions`). 저장 파일의 이 NPC 위치는 실제와 다를 수 있고, 검증 도구는 이 NPC의 위치를 비교하지 않는다(`IsSavePositionDerived`). 파티원 위치는 파티 경로로 계산하므로 NPC 상태를 싣는 저장은 파티(`NpcParties`)도 함께 싣는다 |

## 3. 동작

### 3.1 데이터 (새 partial `NpcManager_SaveSnapshot.cs` 권장)

```csharp
// 저장용 DTO. 한 번 만든 값은 고치지 않고 교체만 한다 (S6).
private readonly Dictionary<int, object?> _saveSnapshot = new();
private readonly HashSet<int> _dirtyNpcIds = new();
private readonly List<int> _dirtyNpcOrder = new();
private readonly SaveDtoSnapshot _snapshotCopier = new();   // 이 매니저 전용 (복사기는 계약 캐시를 가진다)

public void MarkSaveDirty(int entityId)
{
    if (_dirtyNpcIds.Add(entityId) == true)
        _dirtyNpcOrder.Add(entityId);
}

public void FlushSaveSnapshot()
{
    for (int i = 0; i < _dirtyNpcOrder.Count; i++)
    {
        int id = _dirtyNpcOrder[i];
        if (_npcSaveDict.TryGetValue(id, out NpcSaveData? live) == true)
            _saveSnapshot[id] = _snapshotCopier.Capture(live);
        else
            _saveSnapshot.Remove(id);
    }
    _dirtyNpcIds.Clear();
    _dirtyNpcOrder.Clear();
}

public void RebuildSaveSnapshot() { /* 목록 비우고 _npcSaveDict 전체를 Capture해 _saveSnapshot 교체 */ }
```

설명용 예시다. 이름과 세부 시그니처는 구현 때 정한다. `_snapshotCopier`는 `DataManager._saveSnapshots`와 따로 둔다. 같은 인스턴스를 공유하면 저장 캡처와 정리가 계약 캐시를 함께 쓰게 된다. 둘 다 메인 스레드라 문제는 없지만 소유를 나누는 편이 단순하다.

### 3.2 프레임 흐름 (예시)

- **프레임 100**
  - FixedUpdate: 시뮬이 철수를 처리하면서 `MarkSaveDirty(철수)`를 부른다. 사냥 결과와 전리품이 반영된다. 같이 방문했지만 활동 도중인 NPC는 바뀌는 게 없어 표시하지 않는다.
  - LateUpdate 마지막: `FlushSaveSnapshot`이 철수 한 명을 복사해(약 0.05ms) 스냅샷을 교체한다.
- **프레임 101**
  - FixedUpdate: 절도가 일어나 도둑과 피해자 둘 다 표시된다.
  - Update: 결투 종료 저장이 시작된다. 저장 직전 정리로 둘을 복사하고, 스냅샷 사전을 얕게 복사해 백그라운드로 넘긴다.
- **프레임 102 이후**
  - 같은 NPC가 또 바뀌면 새 DTO로 교체한다. 백그라운드는 프레임 101에 받은 자기 사전을 쓰므로 영향이 없다.

### 3.3 저장 흐름

- `DataManager.CapturePendingParts`의 NpcState 분기에서 `captureRuntimeManagers == true`이고 스위치가 켜져 있으면:
  1. `SaveAllActiveNpcs()` — 실체화된 NPC를 ECS에서 저장 데이터로 되쓴다. 위치나 상태(`Condition`)가 저장 데이터와 달라진 NPC만 표시한다. 제자리에 있던 NPC까지 표시하면 저장마다 깊은 복사가 반복된다(2026-10-05 리뷰 후속).
  2. 전체 재생성 대상 저장이면 `RebuildSaveSnapshot()`, 아니면 `FlushSaveSnapshot()`.
  3. 샤드 32개의 루트 DTO를 스냅샷 DTO로 조립한다. `AddPendingPart`의 `Capture`를 거치지 않는 변형을 둔다. 조립 전에 복사 실패 NPC를 다시 복사하고, 실패가 남으면 예외로 저장을 중단한다(S12).
- `captureRuntimeManagers == false`(최초·이관 저장의 `_worldData` 경로)는 지금 그대로 둔다.
- **샤드 루트 조립 방법(권장)**: `NpcShardSaveRoot { ShardIndex, Npcs = 빈 사전 }`을 `Capture`해 루트 DTO를 얻는다. 그 안의 `Npcs` 컨테이너(그 캡처가 새로 만든 사전이라 넘기기 전에는 채워도 된다)에 캐시된 NPC DTO를 넣는다. 복사기 출력 형식을 손으로 흉내 내지 않아도 된다. 넣는 순서는 지금처럼 `_npcSaveDict` 키 순서를 따른다. 샤드 번호는 기존 경로(`SplitNpcShards`)와 스냅샷 경로가 `DataManager.GetNpcShardIndex` 하나를 같이 쓴다.
- `[SaveTiming]`에 스냅샷 정리·조립 시간을 따로 찍는다.

### 3.4 수명

- `NpcManager.Reset()`: 스냅샷과 목록을 비운다.
- 로드: `NpcManager.Initialize`가 끝날 때(Load → LoadAffinity·이관 → 관계·파티 로드 → 동행 검증 → 이동 위치 재계산 `RestoreTravelPositions`(S13) 뒤) `RebuildSaveSnapshot()` (S8). `TimeManager`는 `NpcManager` 뒤에 초기화되므로 재계산은 저장된 세계 시각(`ElapsedWorldGameHours`)을 쓴다.
- NPC 추가(`_npcSaveDict[entityId] = saveData` 두 곳, 신규 생성 경로): 추가 직후 표시한다. 로드 경로의 추가는 S8 재생성이 덮는다.
- NPC 삭제: 지금 런타임 경로에서 사전에서 지우는 곳은 없다(사망자도 남는다). 지우는 경로를 만들면 표시하면 된다. 정리가 "사전에 없음"을 보고 스냅샷에서 지운다.

### 3.5 정리 시스템

- 새 `System_SaveSnapshotFlush` (`ILateUpdateSystem`, Priority **990**). 지금 LateUpdate는 900 EntityMessage, 950 EntityDestroy가 마지막이고, 둘 다 NPC 데이터를 바꿀 수 있으므로 그 뒤에 둔다.
- `IWorldMapSystem`을 붙이지 않는다. 던전 안에서도 돌아야 한다.
- `GameplayPaused`(던전 메뉴) 중에는 `SystemManager.CanRun`이 Input·Checkpoint 외 모든 시스템을 막는다. 이 동안 정리가 돌지 않아도 저장 직전 정리가 있으므로 문제없다. `CanRun` 예외로 두지 않는다.
- 비동기 continuation(`await` 뒤)은 Update 단계에서 돌고 LateUpdate보다 먼저다. 그래서 정리 시스템이 프레임의 마지막 쓰기 뒤에 온다.

## 4. 표시를 넣을 곳 (본체 작업 목록)

**원칙**: NPC 데이터(필드·목록·안의 `ItemData`·`Activity` 등)를 바꾸는 함수는 입구에서 `MarkSaveDirty`를 부른다. 두 NPC를 바꾸면 둘 다 표시한다. 읽기만 하는 곳은 손대지 않는다. 대부분 아무것도 바꾸지 않는 순회·판정 함수는 표시하지 않는다(S4).

| 위치 | 할 일 |
|---|---|
| `NpcManager.TryGetNextSimulationWork` / `TryGetNextSimulationNpc` | **표시하지 않는다**. 방문만으로는 바뀌는 게 없다. `System_NpcSim.AdvanceNpcActivity`가 바꾸기 직전 두 곳에서 표시한다: 새 활동 시작 직전, "활동이 아직 안 끝남" 검사 통과 직후 |
| 이동 위치 갱신 (`UpdateSoloTravelPosition`·`UpdatePartyTravelPositions`) | **표시하지 않는다**(S13). 경로로 계산되는 값이고 로드 때 다시 계산한다. 경로 필드를 바꾸는 곳(활동 시작·경로 재계획 `ResumeHuntPathFromCurrentPosition`)과 경로로 계산할 수 없는 위치 변경(`SetNpcPosition`·`SaveAndDeactivateNpc`)은 표시한다 |
| `System_NpcSim` 파티 처리 (리더·멤버 `GetSaveData` 7곳) | 실제로 고치는 멤버를 표시한다 |
| `NpcManager.cs` | 신규 생성 2곳, `SaveAndDeactivateNpc`(청크 비활성화), `SaveAllActiveNpcs`, 사망 처리, 행적·장비·관계 기억을 쓰는 함수 |
| NpcManager partial 13개 파일 | Ambition(전리품·업그레이드), Companion, Delve, Duel, LivingExpense, Party, Pickup, Relocation, Rumor, SharedActivities(`StoreSharedActivityMemory`·`RecordLatestCompanion` — `PersonalStories`), Theft(도둑·피해자), Trade |
| NpcManager 밖에서 `NpcSaveData` 변경 메서드를 부르는 곳 | `RecordDeed`·`RecordExpeditionWound`·장비 장착 계열 등. 찾아서 호출 측에서 표시하거나, NpcManager 함수로 옮긴다 |

**찾는 방법** (재조사 범위를 줄이기 위한 출발점):
```
grep -rn "GetSaveData(\|SaveDataView\|TryGetNextSimulation" Assets/Scripts --include=*.cs
grep -n "_npcSaveDict" Assets/Scripts/Manager/NpcManager*.cs        # 2026-10-04 기준 75곳
grep -n "public .*(" Assets/Scripts/Npc/NpcSaveData*.cs              # 변경 메서드 후보
```

**읽기 전용으로 확인된 외부 호출**(2026-10-04): `DialogueTopicSelector`, `UINpcInspect`, `NpcRumorPhrasing`, `NpcDialogueContextBuilder`, `WeaponHelper`. `UIVillage`·`NpcRelationBackfill`은 구현 때 읽기 전용인지 확인한다. `VillageManager`·`BuildingManager`의 `GetSaveData`는 건물용이라 상관없다.

## 5. 코드 앵커 (심볼 기준, 줄 번호 아님)

| 파일 | 심볼 | 메모 |
|---|---|---|
| `Manager/DataManager_PartitionedSave.cs` | `WritePartitionSnapshotAsync` | 범위 확정 → `CapturePendingParts` → `Task.Run(PreparePartitionFiles)` → `CommitManifest` |
| 〃 | `CapturePendingParts` NpcState 분기, `AddPendingPart`, `SplitNpcShards`, `GetNpcShardIndex`, `GetNpcShardPartKey` | `AddPendingPart`가 `_saveSnapshots.Capture(root)`로 깊은 복사한다. 여기를 우회한다. 샤드 번호는 `GetNpcShardIndex` 하나를 스냅샷 경로와 같이 쓴다 |
| 〃 | `LogSaveTiming`, `_deepCopyTimer` | `[SaveTiming]` 한 줄 로그 |
| `Data/SaveDtoSnapshot.cs` | `Capture` | 결과는 `Dictionary<string, object?>` 같은 독점 DTO 컨테이너다. `[JsonIgnore]`·기본값 생략·`ShouldSerialize`를 따른다 |
| `Manager/NpcManager.cs` | `Save()`, `SaveAllActiveNpcs`, `SaveAndDeactivateNpc`, `GetSaveData`, `TryGetNextSimulationWork`, `TryGetNextSimulationNpc`, `Reset`, `Initialize`, `Load` | `Save()` = 실체화 NPC 되쓰기 + 사전 얕은 복사 |
| 〃 | `UpdateSoloTravelPosition`, `UpdatePartyTravelPositions`, `RestoreTravelPositions`, `IsSavePositionDerived` | 이동 위치 계산·로드 재계산·검증 제외 판정 (S13) |
| `Manager/DataManager_PartitionedSave.cs` | `WritePartitionSnapshotAsync` 범위 확장 | `NpcState`가 실리면 `NpcParties`도 싣는다 (S13) |
| `Editor/NpcSaveSnapshotDiagnostics.cs` | `Validate`, `StripDerivedPositions` | V3 경로 동등성. 바이트가 다르면 이동 NPC 위치를 양쪽에서 빼고 다시 비교한다 |
| `Manager/NpcManager_SaveSnapshot.cs` | `MarkSaveDirty`, `FlushSaveSnapshot`, `RebuildSaveSnapshot`, `CaptureSaveSnapshotShards`, `ValidateSaveSnapshot`, `TryFindUnmarkedSaveChange` | 스냅샷·변경 목록·복사 실패 목록(S12) 소유. 마지막 둘은 에디터·개발 빌드 전용 |
| `Common/System/System_NpcSim.cs` | `AdvanceNpcActivity` | 바꾸기 직전 표시 2곳(§4) |
| `Manager/NpcManager_Ambition.cs` | `SaveDataView` | `IReadOnlyDictionary`지만 값(`NpcSaveData`)은 고칠 수 있다. 소비자를 확인한다 |
| `Manager/SystemManager.cs` | `Initialize`(등록·Priority 주석), `CanRun`, `LateUpdate` | 새 시스템 등록 |
| `Editor/NpcAmbitionSimDiagnostics.cs` | 시뮬 구성의 `Swap(swaps, npc, ...)` 목록, 라이브 해시 필드 목록(`_npcSaveDict` … `_goldFlowCounters`), `AdvanceNpc`, `CheckUnmarkedSaveChange` | 스냅샷 필드(`_saveSnapshot`·`_dirtyNpcIds`·`_dirtyNpcOrder`·`_snapshotFailedIds`·측정값)를 Swap한다(§9). `AdvanceNpc`는 `System_NpcSim`과 같은 위치에서 표시한다 |
| `Data/SaveSnapshotCache.cs` | `MarkDirty`, `Flush`, `Rebuild`, `PrepareForSave`, `Get`, `Reset` | S-3의 인스턴스 소유 공용 캐시. 항목별 실패 격리·저장 전 재시도·누락 비교·진단 관찰자 |
| `Manager/NpcManager_SimulationSaveSnapshot.cs` | `RefreshSimulationSaveSnapshots`, `CaptureSocialSaveSnapshotRoot`, `CapturePartySaveSnapshotRoot`, `CapturePopulationSaveSnapshotRoot` | 관계·파티·인구 캐시와 저장 루트 조립 |
| `History/HistoryManager_SaveSnapshot.cs` | `MarkSaveDirty`, `FlushSaveSnapshot`, `RebuildSaveSnapshot`, `CaptureSaveSnapshotRoot` | 정착지 배열 인덱스별 WorldSim 캐시 |
| `Editor/NpcSaveSnapshotDiagnostics.cs` | `ValidateSimulation`, `VerifySimulationContracts`, `VerifySimulationSaveRoundTripAsync` | S-3 경로 동등성·격리 계약·프로젝트 Temp의 실제 writer/reader 왕복 |

## 6. 단계

### 6.1 준비 (게임 동작 변화 없음, 검증 없음)

1. `NpcManager_SaveSnapshot.cs`: §3.1의 필드·`MarkSaveDirty`·`FlushSaveSnapshot`·`RebuildSaveSnapshot`·샤드 루트 조립 함수·초기화를 만들고, `Reset()`에서 부른다.
2. 전환 스위치(S11): 예를 들어 `static readonly bool`. 기본은 꺼짐이다. `const bool`은 도달할 수 없는 코드 경고(CS0162)가 나므로 피한다.
3. `System_SaveSnapshotFlush` 작성과 `SystemManager.Initialize` 등록(Priority 990 주석 포함).
4. `DataManager` NpcState 분기에 스냅샷 경로를 추가한다. 스위치가 꺼져 있으면 기존 경로를 탄다.
5. 로드 끝 재생성 호출(스위치가 켜졌을 때만).
6. 누락 검증 진단(S10)을 작성만 한다. 예: 메뉴 `ARPG/Save/Diagnostics/Validate Npc Save Snapshot`과 진단에서 부를 수 있는 공개 함수. 하는 일은 정리 → NPC마다 스냅샷 DTO JSON과 새 `Capture` JSON 비교 → 다르면 NPC ID와 **최상위 항목 이름**(`Rumors`, `Activity` 등)을 보고.
7. `NpcAmbitionSimDiagnostics`의 Swap 목록에 `_saveSnapshot`·`_dirtyNpcIds`·`_dirtyNpcOrder`를 빈 새 값으로 추가한다.

### 6.2 본체

1. §4의 표시를 넣는다.
2. 전체 재생성 시점(§8 Q1)을 저장 경로에 연결한다.
3. `[SaveTiming]`에 스냅샷 정리·조립 시간을 추가한다.
4. 스위치를 켠다.
5. 문서를 갱신한다: 이 문서 상태 줄, [SAVE_PARTITION_DESIGN.md](SAVE_PARTITION_DESIGN.md) §5(저장 범위)·§9(검증), [DUNGEON_WORLD_SIM_DESIGN.md](../map/DUNGEON_WORLD_SIM_DESIGN.md) Q3. 위키 반영은 제안만 하고 고치지 않는다.

### 6.3 검증 → §7 (여기서 처음이자 한 번)

## 7. 검증 (구현이 끝난 뒤 한 번)

**사전 준비**: 플레이 모드 저장은 실제 세이브에 커밋된다. 시작 전에 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/ARPG/Saved`를 `C:\Projects\ARPG_save_backup_snapshot`으로 백업한다. 끝나면 복원하고 `diff -r`로 동일한지 확인한다. 동일하면 묻지 않고 백업을 삭제한다.

| # | 항목 | 통과 기준 |
|---|---|---|
| V1 | 컴파일: `refresh_unity`(scope all, compile request) → `read_console`. 콘솔이 비면 `%LOCALAPPDATA%\Unity\Editor\Editor.log`의 마지막 "Reloading assemblies" 이후 `error CS` 개수 | 오류 0 |
| V2 | 기존 진단: `ARPG/Npc/Diagnostics/Ambition Sim/Validate Shared Activities`, `Validate Personal Stories` | 28 / 32 PASS |
| V3 | **경로 동등성**: 같은 상태에서 스냅샷 경로와 기존 `Capture` 경로로 32샤드 DTO를 만들어 JSON 비교(진단 코드). 화면 밖 이동 NPC의 위치는 비교에서 뺀다(S13) | 바이트 동일(이동 NPC 위치 제외) |
| V4 | **시뮬 경로 누락**: 플레이 모드 GameScene에서 튜닝 진단 `Run(30)`. NPC마다 처리 전에 "표시 없이 바뀐 NPC"를 검사하고(`TryFindUnmarkedSaveChange`), 처리 직후 바로 정리한다. tick 끝에 몰아서 정리하면 같은 tick 뒤쪽의 누락이 가려진다. 시뮬이 끝나면 Swap 복원 전에 전수 비교(`ValidateSaveSnapshot`)한다 | 표시 누락 0, 끝 비교 누락 0, 라이브 해시 동일, 기존 지표 유지 |
| V5 | **플레이 경로 누락**: 던전 입장·퇴장·완료, 결투, 동행, NPC 상점 거래가 일어난 뒤, 청크 이동(실체화·비실체화), NPC 사망, 수동 저장. 각 저장 직후 누락 검증 | 누락 0 |
| V6 | 저장·로드 왕복: 저장 → 로드 → 저장. 이동 NPC 위치는 로드 때 다시 계산하므로 비교에서 뺀다(S13) | 두 저장의 NPC 샤드 바이트 동일(이동 NPC 위치 제외) |
| V7 | 성능: `[SaveTiming]` 결투 종료 저장(Player·World·NpcState)과 전체 저장, 프레임 끝 정리 시간의 평균·최대 | 재생성 대상이 아닌 저장에서 NpcState 복사 ≈ 0. 정리 평균 1ms 미만(목표) |

- 튜닝 진단은 `execute_menu_item`으로 부르면 로그가 잘 안 잡힌다. 리플렉션(`execute_code`)으로 호출하고, 긴 실행은 같은 진단을 다시 예약하지 말고 `Logs/NpcAmbitionSim/` 결과 파일로 완료를 확인한다.
- Validate 메뉴 두 개는 `execute_menu_item` + `read_console`(`filter_text`)로 결과를 읽을 수 있다.
- 플레이 중 스크립트를 고치면 Unity가 플레이를 강제 종료한다. 고치기 전에 `isPlaying == false`를 확인한다.

### 7.1 구현 검증 결과 (2026-10-04)

| 항목 | 결과 |
|---|---|
| V1 컴파일 | Unity 전체 refresh 후 컴파일 오류 0. Runtime/Editor 빌드도 오류 0(기존 경고 22/13개) |
| V2 기존 진단 | Shared Activities 28 PASS / Personal Stories 32 PASS |
| V3 경로 동등성 | 실제 NPC 2,406명에서 기존 깊은 복사와 캐시 조립 JSON 32/32 동일, 누락 0 |
| V4 30일 복제 시뮬 | 누락 0, 라이브 해시 동일, 에러 0. 평균 전투력 +2.59%, Hunt 37.79%, 상한 초과 0, Gold 검산 오차 0으로 기존 주요 기준 PASS. ※ 당시 진단은 매 tick 전원을 표시·복사해 구조상 항상 누락 0이 나오는 검사였다. 고친 진단으로 다시 한 결과는 §7.2 |
| V5 플레이 경로 | 실제 관리 API와 전투 사망 파이프라인으로 던전 입장·퇴장·보스 완료, 결투 종료, 동행 소환·귀환, NPC 상점 판매, 청크 실체화·비실체화, NPC 사망, 수동 저장을 실행. 각 경로 저장 후 누락 0 |
| V6 저장·로드·저장 | 초기 로드 보정을 마친 상태에서 게임 시간을 멈추고 재접속. 두 저장의 NPC 샤드 32개 SHA-256 동일. 시간이 흐르는 초기 프레임의 이동·기존 활동 초기값 보정은 동일 상태 비교에서 제외 |
| V7 저장 비용 | 결투 종료 부분 저장: NPC 정리 0.501ms, 조립 0.853ms, 메인 합계 28.5ms. 전체 재생성 대상은 NPC 복사 약 129~132ms를 유지 |
| V7 프레임 정리 | 재접속 후 일반 플레이 210회 평균 1.089ms, 최대 3.857ms. 초기 플레이 998회 평균 1.135ms, 최대 3.985ms. 평균 1ms 미만 목표는 일부 구간 미달 |

실측은 Unity Editor 6000.4.2f1의 GameScene과 실제 세이브에서 수행했다. 프레임 정리는 변경 목록이 비어 있지 않은 호출만 집계한다. 30일 진단은 한 tick에 전체 NPC를 처리하므로 그 tick 정리 시간(평균 264.172ms, 최대 617.373ms)은 런타임 프레임 비용과 구분한다(§7.2의 진단 변경 뒤로는 NPC마다 바로 정리하므로 tick 끝 정리 값은 0이다). 진단의 스냅샷·변경 목록·측정값도 복제 및 복원하며, 해시는 DTO 내용과 변경 목록까지 검사한다. [src: Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

### 7.2 리뷰 후속 변경과 재검증 (2026-10-04)

같은 날 코드 리뷰에서 나온 항목을 고치고 다시 검증했다.

| 변경 | 내용 |
|---|---|
| 복사 실패 격리 (S12) | NPC 하나의 복사가 실패하면 정리 전체가 멈추고 매 프레임 같은 예외가 나던 문제. 실패 NPC만 격리하고, 저장 직전 재시도에도 실패하면 저장을 중단한다 |
| 방문 표시 제거 | 순회 함수가 방문한 NPC를 모두 표시해 초당 약 48명을 헛복사했다. 이제 `System_NpcSim`이 바꾸기 직전에만 표시한다(§4) |
| 헛표시 정리 | 소문 정리(`PruneInvalidRumors`)는 실제로 지울 때만 표시한다. `SpawnNpc`는 입구 표시를 지우고 await 뒤 표시만 남겼다. 판정 함수 `GetTheftThiefBlock`·`HasTradeOpportunity`·`BuildActivityChoiceContext`의 표시를 뺐다. 이 함수들이 부르는 칸 보정(`EnsureSlotsInitialized`)은 생성·로드 때 이미 끝나 있다. `UpdateOffscreenNpcPosition`은 실체화 NPC(위치를 ECS가 가짐)를 걸러 낸 뒤에 표시한다 |
| 샤드 번호 일원화 | `DataManager.GetNpcShardIndex`를 기존 경로와 스냅샷 경로가 같이 쓴다. `CaptureSaveSnapshotShards`는 샤드 수를 인자로 받지 않는다 |
| 저장 분기 구조 | `CapturePendingParts` NpcState 분기 끝의 `return;`을 `if / else`로 바꿨다. 뒤에 파티션 블록(S-2 마을 등)을 붙여도 런타임 저장에서 빠지지 않는다 |
| 시뮬 진단 | 이전 V4는 매 tick 전원을 표시·복사해 누락이 있어도 항상 0이었다. NPC마다 처리 전 검사 → 처리 → 바로 정리로 바꿨다(§7 V4). 진단의 활동 처리 사본도 `System_NpcSim`과 같은 위치에서 표시한다 |
| 이동 위치 미저장 (S13) | 화면 밖 이동 NPC(혼자 이동·파티원)의 위치 갱신은 표시하지 않고, 로드 때 경로로 다시 계산한다. 위치 계산은 `NpcManager`의 `UpdateSoloTravelPosition`·`UpdatePartyTravelPositions` 한 곳으로 모았다(`System_NpcSim`도 이것을 부른다) |
| 파티 저장 결합 | `NpcState`를 싣는 저장은 `NpcParties`도 싣는다. 전에는 전체 저장 때만 실려, 마지막 전체 저장 뒤 꾸려진 파티가 로드 때 사라졌다 |

| 검증 | 결과 |
|---|---|
| 컴파일 | 오류 0, 바꾼 파일의 새 경고 없음 |
| V4 30일 복제 시뮬 (2회) | 표시 누락 0, 끝 비교 누락 0, 라이브 해시 동일, 에러 0, Gold 검산 오차 0. 평균 전투력 +2.51~2.62%, Hunt 37.6~37.9%, 상한 초과 0 |
| 검사기 동작 | NPC 하나의 Gold를 표시 없이 바꾸면 `Gold`로 잡고, 같은 누락은 한 번만 세고, 표시된 NPC는 건너뛴다 |
| 실제 게임 누락 검증 메뉴 (3회) | 누락 0, 경로 동등성 32/32 |
| 실제 저장 | 부분 저장(Player·World·NpcState)과 전체 저장 모두 성공. 부분 저장 NPC 정리/조립 0.574/1.161ms |
| V7 프레임 정리 (S13 전) | 60초 일반 플레이 260회 평균 0.952ms, 최대 3.707ms. 실체화 NPC 헛표시를 고친 뒤 0.918/4.151ms. 이전 기록(1.089/3.857ms)과 같은 조건 비교는 아니다 |
| V7 프레임 정리 (S13 뒤) | 약 70초 일반 플레이에서 비어 있지 않은 정리 42회(S13 전 같은 시간 약 300회), 평균 0.288ms, 최대 0.774ms. 측정 때 위치를 저장하지 않는 이동 NPC는 1,657명(혼자 581, 파티원 1,076, 파티 339개) |
| 30일 시뮬 (S13 뒤) | 표시 누락 0, 끝 비교 누락 0, 라이브 해시 동일, 에러 0, Gold 검산 오차 0. 전투력 +2.48%, Hunt 37.80% |
| S13 검사기 | 이동 NPC는 위치만 바꾸면 누락으로 잡지 않고 Gold 변경은 잡는다. 이동 중이 아닌 NPC의 위치 변경은 잡는다. 원본 JSON은 32샤드 모두 달랐고(저장본 위치가 옛값), 이동 NPC 위치를 빼면 모두 같았다 |
| 파티 결합 저장 | `Player·World·NpcState` 요청에 `NpcParties`가 실렸다(152KB). 깊은 복사 45.7ms → 51.6ms |
| 재접속 | 부분 저장의 파티 344개가 그대로 로드됐다(전에는 마지막 전체 저장의 325개로 돌아갔을 상태). 저장 파일 위치는 이동 NPC 1,986명 중 233명만 저장 시각의 경로 위치와 같았지만, 로드 직후 메모리는 1,394명이 같았다(나머지는 검사 전 시뮬 방문으로 현재 시각 위치가 됨). 재접속 뒤 누락 검증 PASS |

**남은 점**
- 당시 파티 저장은 저장할 때 깊은 복사했다(약 6ms). **2026-10-05 S-3에서 파티 스냅샷 구현 완료**. 현재 저장 경로와 검증은 §12를 따른다.
- 표시 누락은 30일 시뮬 진단과 검증 메뉴를 돌릴 때만 드러난다. 평소 플레이 중 자동 감시(에디터·개발 빌드)는 하지 않기로 했다 [확정: 2026-10-04 사용자]. 컴파일 단계에서 막는 방법(쓰기 전용 통로·속성 자동 표시)도 비용이 커서 하지 않는다.
- 진단 요약의 "tick끝 정리" 값은 이제 항상 0이다.

[src: Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs]

## 8. 시작 전 확인 질문 (기본안 포함)

| # | 질문 | 기본안 |
|---|---|---|
| Q1 | 스냅샷 전체 재생성(S9)을 어떤 저장에서 할까 | **수동 저장·최초 저장·이관 저장(이미 `All`) + 던전 입장·퇴장·완료 커밋.** 모두 화면 전환·메뉴 뒤라 재생성 멈춤(약 115ms)이 가려진다. 결투·상점 등 나머지는 정리만 한다. 표시 누락의 피해 범위는 "다음 던전 출입 또는 수동 저장까지"다. 저장 범위(`SaveScope`) 자체는 바꾸지 않는다 |
| Q2 | 출시 빌드 안전망 | 누락 검증은 에디터·개발 빌드에만 둔다. 출시 빌드에서 검증이 못 잡은 누락은 다음 재생성 전에 게임을 끄면 그 변경이 사라진다. 이 위험은 감수한다 |

2026-10-04 사용자 확인: Q1·Q2 모두 기본안으로 구현했다.

## 9. 함정

- **`await`·코루틴 뒤 재사용**: 정리가 목록을 비우므로, 프레임을 넘겨 같은 객체를 고치면 표시가 없다. 던전 커밋(`EnterAsync`/`ExitAsync`/`CompleteAsync`)처럼 `await`가 있는 경로에서 NPC를 고치면, `await` 뒤에 다시 표시한다.
- **진단 복제 오염**: `NpcAmbitionSimDiagnostics`는 NpcManager 필드를 복제본으로 바꿔 끼우고 같은 코드를 돌린다. 스냅샷 필드를 Swap하지 않으면 복제 NPC ID가 라이브 목록에 들어간다. 진단 도중 정리가 돌면 라이브 스냅샷이 복제 데이터로 덮인다.
- **실체화 NPC**: 화면 안 NPC의 상태는 ECS에 있다가 `SaveAllActiveNpcs`·`SaveAndDeactivateNpc`에서 저장 데이터로 되쓰인다. 이 두 곳에서 표시해야 한다. 위치는 매 프레임 바뀌므로 그때그때 표시하지 않는다.
- **DTO 불변**: 스냅샷 DTO는 여러 저장에 걸쳐 재사용된다. `PreparePartitionFiles`(백그라운드)가 DTO를 읽기만 하는지 확인한다. 고치는 곳이 있으면 S6이 깨진다.
- **`SaveDataView`**: 읽기 전용 사전처럼 보이지만 값은 고칠 수 있다. 진단 외 런타임 소비자가 고치지 않는지 확인한다.
- **대량 표시**: 이주(사라진 터 148명)처럼 한 프레임에 많은 NPC가 바뀌면 그 프레임 정리가 커진다. V7에서 최대값을 본다. 상한은 두지 않는다(저장 직전에는 어차피 전부 정리해야 한다).
- **샤드 순서**: V3 바이트 비교는 기존 경로와 같은 키 순서(`_npcSaveDict` 순서)로 조립해야 통과한다.
- **순회·판정 함수의 표시**: 대부분 아무것도 바꾸지 않는 함수의 입구에서 표시하면 헛복사가 대부분이 된다(방문 표시 때 초당 약 48명). 실제로 바꾸는 지점에서 표시한다(S4).
- **경로로 계산할 수 없는 위치 변경**: 이동 위치만 표시하지 않는다(S13). 활동 경로 필드를 바꾸거나, 경로와 무관하게 위치를 옮기는 곳(스폰 위치 보정, 실체화 해제 때 ECS 위치 되쓰기)은 표시해야 한다. 표시하지 않으면 로드 때 옛 경로로 계산돼 엉뚱한 곳에 놓인다.
- **진단의 활동 처리 사본**: 30일 진단은 `System_NpcSim` 로직 일부를 자체 사본(`AdvanceNpc`·`StartNextActivity` 등)으로 돌린다. 표시 위치가 다르면 진단이 런타임 누락을 놓치거나 가짜 누락을 낸다. `System_NpcSim`을 고치면 사본도 맞춘다.

## 10. 후속 단계

1. **S-2 마을(Villages)**: 2026-10-05 구현 완료·핵심 기능 검증 통과(한계는 §11.3). 상점 부분 저장에서 전체 마을 깊은 복사를 우회한다. 실체화 마을의 ECS 되쓰기·fold-back 경로는 변경 표시 후 정리한다.
2. **S-3 관계·파티·인구·WorldSim**: 2026-10-05 구현 완료·핵심 기능 검증 통과(§12). NpcSocial·NpcParties·NpcMeta·WorldSim의 저장 시 전체 깊은 복사를 우회한다.
3. **묶음·증분 저장**: 2026-10-05 사용자 요청으로 구현했다(§13). [DUNGEON_WORLD_SIM_DESIGN.md](../map/DUNGEON_WORLD_SIM_DESIGN.md) §6.3의 여섯 파트 결합에 파일 재사용을 적용한다. 던전 중 월드 시뮬 게임플레이 구현과 실제 플레이의 저장 비용 실측은 별도 후속 작업이다.
4. S-3에서 "ID → DTO 스냅샷 + 바뀐 목록"을 `SaveSnapshotCache<TKey>` 인스턴스 클래스로 구현했다(static 금지). NPC 상태·마을의 기존 캐시는 유지하고 S-3 캐시만 이 클래스를 사용한다.


## 11. S-2 마을(Villages) 스냅샷 (2026-10-05)

### 11.1 적용 계약

- `VillageManager_SaveSnapshot.cs`가 `VillageId → 불변 DTO`, 중복 방지 집합·변경 순서 목록, 복사 실패 목록, 전용 복사기와 프레임 측정값을 소유한다. `SaveSnapshotEnabled=false`면 기존 깊은 복사 경로를 쓴다. S-2 당시 NPC 캐시를 일반화하는 별도 공용 클래스는 추가하지 않았다. 이후 S-3의 공용 캐시는 §12에 기록하며, 마을 캐시는 그대로 유지한다.
- 변경한 마을 전체를 복사한다. LateUpdate Priority 990의 기존 정리 시스템에 마을 정리를 이어 붙였고, 저장 직전에도 정리한다. 게시한 DTO는 교체만 하며 백그라운드 저장에 넘긴 DTO를 수정하지 않는다(S3·S5~S7·S12).
- 저장 루트는 빈 `VillageSaveRoot`를 기존 복사기로 캡처한 후 새 `Villages` 목록에 캐시 DTO를 넣는다. `_villageList` 순서를 유지하므로 기존 `Save()` 캡처와 JSON 바이트가 같다. 파티션·schema·manifest 형식과 SaveScope 결합 정책은 유지한다.
- `ClearVillages()`에서 캐시·변경·실패·측정을 함께 비우고, Load 마이그레이션이 끝나면 전원을 한 번 복사한다(S8). 새 마을 등록과 신규 게임 부트스트랩은 변경을 표시한다.
- 수동·최초·이관 All 저장과 던전 트랜잭션의 기존 재생성 정책을 NPC·마을이 공유한다. 큐의 요청 이름은 `_queuedSaveSnapshotRebuild`/`rebuildSaveSnapshots`로 바꿨다. 그 저장 범위에 Villages가 있을 때만 마을을 재생성하며, 최초·이관의 `captureRuntimeManagers=false` 경로는 `_worldData` 전체 캡처를 유지한다.
- 출시 빌드에는 누락 비교와 프레임 측정을 넣지 않는다. 일상 플레이 자동 전수 비교도 추가하지 않았다. 누락 비교는 명시적 진단에서만 수행한다(§8 Q2·§7.2의 기존 사용자 결정).

### 11.2 변경 표시와 진단

- `VillageManager`: 등록·연대기 연결·프로필/고정 인구 보정·자원 생산/소비·NPC Gold 입출금·상점 재입고/구매/위탁·완공·벽 피해/문 개폐·실체화 복원·fold-back·저장 직전 ECS task/컴포넌트 되쓰기에서 표시한다. 반복 인구 조회는 저장값이 실제 달라졌을 때만, 재입고는 기존 회차 early return을 통과했을 때만 표시한다. `NpcEntityIds`/`EntityId`는 JsonIgnore라 명단만 바뀌는 조회/등록은 표시하지 않는다.
- 외부 변경 경계: 실시간 자원 생산, 추상 slice 옵션 생성(자원 carry·clock 전진), History 초기 발전·materialize catch-up·fold-back clock, NPC 첫 코호트 플래그, 건설 task 진행/완공·벽 계획·Tier 승격을 표시한다. 비동기 Tile 완공 뒤의 벽 기록에서도 다시 표시해 프레임을 넘긴 변경을 놓치지 않는다.
- 기존 `NpcSaveSnapshotDiagnostics.cs`에 `Validate Village Save Snapshot`과 `Verify Village Snapshot Contracts` 메뉴를 확장했다. 먼저 원장 누락을 비교하고 그 뒤 ECS 되쓰기 후 기존/캐시 경로 JSON을 비교한다. 격리된 계약 진단은 빈 루트·목록 순서·중첩 DTO 불변·중복 표시·누락 항목·실패 격리/저장 중단/재시도·제거·수명 등을 검사하며 실제 세이브를 쓰지 않는다.
- NPC 복제 진단은 마을 캐시·변경·실패·측정값과 진단 관찰자를 Swap/복원하고 캐시 내용·변경 목록·관찰자 수명도 라이브 해시에 포함한다. 에디터 전용 관찰자를 복제 진단 중에만 `MarkSaveDirty` 입구에 연결해, 다음 정상 표시가 이전의 미표시 변경을 가리기 전에 검사한다. NPC 처리 직후 표시한 마을을 즉시 정리하고, 끝에 전수를 비교한다. 진단의 자체 생산/사냥 식량 처리 사본도 실제 경계처럼 표시한다. 첫 30일 검사에 사용했던 NPC마다 소속 마을을 두 번 전수 복사하는 방식은 반복 실행 비용이 지나치게 커서 제거했다. 평소 플레이와 출시 빌드에는 이 관찰자가 없다.

- 복제 진단은 매 게임 시간 `DisplayCancelableProgressBar`로 진행률·취소를 제공하고 취소/실패에도 finally에서 원장을 복원한다. 실패 보고에는 수집한 불일치 필드도 포함한다. 끝 측정에서 재현된 `HasFreeBagSlot`의 활동 보정 부작용은 조회에서 제거했고 기존 절도 진단에 회귀 검사를 추가했다.

[src: Assets/Scripts/Village/VillageManager_SaveSnapshot.cs, Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Village/VillageManager_NpcTrade.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs, Assets/Scripts/Common/System/System_SettlementSim.cs, Assets/Scripts/Common/System/System_VillagePassiveProduction.cs, Assets/Scripts/Common/System/System_VillageBuildQueue.cs, Assets/Scripts/Common/System/System_VillageWallPlanner.cs, Assets/Scripts/Common/System/System_VillageTierProgression.cs, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/NpcManager_Theft.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs]

### 11.3 검증 결과

| 항목 | 결과 |
|---|---|
| 컴파일 | Unity 재시작 후 스크립트 컴파일 및 Runtime/Editor .NET 빌드 오류 0. 기존 경고 Runtime 22 / Editor 13개. 재시작 때 지워진 NuGet assets는 작업용 중간 경로에서 다시 생성했다. |
| 기존 NPC 기본 진단 | Shared Activities 28 PASS / Personal Stories 32 PASS. |
| 마을 계약 | 격리된 `VerifyVillageContracts` 17/17 PASS. 목록 순서·중첩 DTO 불변·중복 표시·미표시 변경·실패 격리/저장 중단/재시도·삭제·Reset/Load 수명 검사 포함. |
| 실제 데이터 동등성 | 마을 300개, NPC 2,406명인 GameScene에서 기존 전체 캡처와 스냅샷 조립 JSON 동일. 마을 전수 비교 불일치 0. |
| 거래·저장 | 재입고·NPC 상점 진입/거래 시도·Gold 입금/출금 후 미표시 변경 없음. 이 NPC 거래 시도 자체는 판매 0 / 구매 없음이었다. 실제 플레이어 상점 구매(50G)와 자동 저장 성공, revision 증가, 구매 후 마을 누락 0·기존 JSON 동일. 명시적 Player/World/Villages 부분 저장과 던전 트랜잭션 API의 전체 캐시 재생성 저장도 성공. 게시한 이전 루트·중첩 DTO JSON 불변. |
| 저장·로드·저장 | 게임 시간 정지 상태에서 플레이 모드를 종료·재진입해 실제 세이브 로드. 로드 전후 마을 JSON 동일, 캐시 누락 0. 다시 부분 저장한 마을 파티션 1,850,865바이트의 SHA-256 동일(`7CBA4167DC0B2C846211F3DD66DAC651D785210AB9C02C1B0AFC928249C59814`, revision 137 → 138). |
| 1일 복제 시뮬 | 최종 실행 20.9초. NPC와 마을 모두 표시 누락 0 / 끝 비교 누락 0, 에러 로그 0, 라이브 상태 해시 전후 동일(`0d51312e344b07fe`). |
| 긴 진단 중단 | 최초 30일 실행은 NPC마다 소속 마을 전체를 반복 복사하는 과도한 검사로 에디터 메인 스레드를 오래 점유했다. 사용자 승인으로 에디터를 강제 종료·재시작했고, 검사 비용을 줄인 뒤 사용자가 선택한 가벼운 1일 검증으로 바꿨다. 30일 완료 결과는 없다. 진단에는 매 게임 시간 취소 버튼과 finally 상태 복원을 추가했다. |
| NPC 조회 부작용 수정 | 짧은 검사의 최종 NPC `Activity` 불일치 원인은 `HasFreeBagSlot` → `EnsureSlotsInitialized`가 가방 조회 중 `OutcomeSeedGameTime`까지 보정하는 것이었다(독립 재현: 0 → 12). 생성·로드에서 칸 보정을 마친 원장에 대해 조회만 하도록 바꾸고 기존 절도 진단에 회귀 검사를 추가했다. 그 검사와 수정 뒤 1일 스냅샷 검사는 PASS. |
| 추가 절도 종합 진단 | 초기 실행에서 판매 소문을 통한 늦은 발각 검사 2개가 FAIL이었다. 이후 진단의 미래 도난 시각과 현재 판매·발각 시각이 역전된 것이 원인임을 확인했다. §11.4 수정 뒤 재검증은 실패 0건, 장착 1/1·판매 2/2 PASS, 라이브 해시 동일·게임 시각 복원 True였다. |
| 원래 세이브 보호 | 검증 전 백업했고 에디터 종료 뒤 원본 파일 88개를 복원, 모든 SHA-256과 파일 수 동일 확인. 작업 중 생성한 저장·로그·백업·빌드 산출물은 확인 후 정리한다. |

성능은 저장 시 **마을 캡처 부분**과 전체 저장을 구분한다.

- 같은 실제 마을 300개를 3회 캡처: 기존 깊은 복사 90.17 / 63.81 / 64.73ms, 캐시 루트 조립 0.052 / 0.045 / 0.043ms. 이는 JSON 변환·파일 쓰기와 갱신 비용을 제외한 동일 데이터 비교다.
- 실제 `[SaveTiming]` 상점 자동 저장(rev 135): 마을 정리 0.656ms + 조립 0.040ms, 전체 메인 캡처 15.8ms(플레이어 등 깊은 복사 11.7ms 포함). 이어진 부분 저장(rev 136): 마을 정리 0.468ms + 조립 0.042ms, 전체 메인 캡처 1.6ms. 파일 쓰기/교체 비용까지 0이 된다는 뜻은 아니다.
- 재생성이 요청된 던전 트랜잭션 API(rev 137)는 정책대로 마을 전체를 다시 복사했고 정리 70.74ms였다. 이 비용은 화면 전환/수동 저장의 기존 재생성 정책에 남는다.
- 초반 플레이 표본에서 변경이 있던 프레임의 마을 정리 86회 평균 2.306ms / 최대 7.397ms. **평균 1ms 미만 목표는 이 표본에서 미달**이다. 추상 마을의 자원 carry·clock 갱신도 변경으로 표시해 원장을 보존한다. 정리하지 않은 프레임은 이 평균의 분모에 포함되지 않는다.
- 1일 검증을 30일 장기 검증으로 대신했다고 보지 않는다. 청크 경계 이동, 실제 던전 입장/퇴장/완료와 진행 중 건설·벽 계획을 모두 플레이로 재실행하지는 않았다. 해당 저장 경계와 변경 표시는 코드 검토에 포함했고, 런타임 마을 발전이 꺼진 현재 설정을 유지했다. 이 절의 S-2 검증에는 시뮬 묶음 저장·S-3을 넣지 않았다. 이후 S-3 구현·검증은 §12에 기록한다.


### 11.4 절도 진단 시간 순서 수정 (2026-10-05)

- 원인: `VerifyTheftRules`는 도난을 `t0 + 60..62`와 재절도를 `t0 + 80..81`에 주입하면서, 판매·발각에는 전진하지 않은 `AR.s.Time.CurrentGameTime`(`t0`)을 썼다. `RecordDeed`는 최신 절도 피해 사연만 보관하므로 시간상 과거인 발각 기록을 버렸고, 감사는 도난 기록(`ItemStolen`, 참가자 없음)을 읽었다. 판매 소문의 `SubjectNpcId`에는 판매자가 정상 기록되어 있었다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Manager/NpcManager_Theft.cs]
- 수정은 Editor 진단에 한정했다. 주입한 도난 시각에 맞춰 복제 진단의 게임 시계를 전진시키고, 장착 발각은 `t0 + 70`, 판매·발각은 `t0 + 71`, 재절도 후 판매·발각은 `t0 + 82`로 맞췄다. `TradeAtShop`이 매니저 시각을 읽으므로 전달 인자와 매니저 시계를 함께 맞춘다. 시간이 역행하면 진단을 실패시키며, 도난보다 판매·발각이 늦고 현재 시각과 같은지 확인하는 검사 2개를 추가했다.
- 시간 매니저의 `_elapsedGameTime`·`_accumulator`를 진입 시 보관하고 finally에서 복원한다. 성공·오류 보고에 시각 복원 여부를 넣으며 PASS 조건에도 포함했다. 게임의 최신 사연 보관 규칙이나 판매자 기록 코드는 바꾸지 않았다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs]
- 검증: Unity 및 Runtime/Editor 빌드 오류 0(기존 경고 합계 35개). `VerifyTheftRules` 약 5.13초, 실패 0건. 늦은 발각 독립 감사는 장착 1/1·판매 2/2 PASS. 라이브 상태 해시 동일 True, 게임 시각 복원 True. 검증 뒤 원래 저장 파일 88개의 SHA-256 동일 확인 및 작업용 산출물 정리를 수행했다.

## 12. S-3 관계·파티·인구·WorldSim 스냅샷 (2026-10-05)

### 12.1 적용 계약

| 파티션 | 캐시 소유자·복사 단위 |
|---|---|
| NpcSocial | `NpcManager`: 호감도는 `NpcPairKey` 쌍별, 유형 관계는 From NPC별 outgoing 목록 |
| NpcParties | `NpcManager`: `PartyId`별 파티 전체 |
| NpcMeta | `NpcManager`: 인구 메타 루트 1개 |
| WorldSim | `HistoryManager`: 초기화 뒤 고정된 정착지 배열 인덱스별 상태 |

- `SaveSnapshotCache<TKey>`가 불변 DTO·중복 방지 집합·변경 순서·실패 목록·전용 `SaveDtoSnapshot`을 소유한다. 각 매니저 인스턴스의 필드로 두며, 게시한 DTO는 수정하지 않고 교체한다. NPC 상태·마을의 기존 캐시는 유지한다.
- `NpcManager.SimulationSaveSnapshotEnabled`와 `HistoryManager.SaveSnapshotEnabled`를 켰다. 기존 LateUpdate Priority 990 시스템이 NPC·마을 뒤에 S-3 캐시도 정리하며, 저장 직전에는 요청 범위에 포함된 캐시만 정리한다. 변경 목록이 비어 있으면 복사 호출을 건너뛴다.
- Load/Initialize의 이관·복원이 끝나면 초기 캐시를 만들고 Reset/Load에서 이전 캐시를 비운다. 신규 등록·삭제도 표시해 해당 항목을 추가·제거한다. 수동·최초·이관 전체 저장과 던전 트랜잭션의 기존 재생성 정책·저장 큐 요청을 공유한다.
- 항목 복사 실패는 그 항목만 격리하고 다른 항목 정리는 계속한다. 저장 루트 조립 직전에 재시도하며, 실패가 남거나 필요한 DTO가 없으면 저장을 중단한다. 낡은 DTO로 조용히 저장하지 않는다(S12).
- `DataManager.CapturePendingParts`에서 런타임 캡처와 스위치가 켜진 네 파티션은 `AddPendingSnapshotPart`로 깊은 복사를 우회한다. 최초·이관의 `captureRuntimeManagers=false`와 스위치가 꺼진 경로는 기존 캡처를 유지한다. 기존 관계·파티·정착지 순서와 JSON 기본값 생략을 따르며, WorldSim의 Version·IsInitialized·null 배열 표현도 유지한다.
- **S-3 단계에서는 파일 형식·manifest·저장 범위 결합을 바꾸지 않았다.** 당시 결합은 `NpcState ↔ NpcSocial`, `NpcState → NpcParties`와 미저장 NPC 던전 완료의 결합이었다. 이후 여섯 파트 결합·증분 저장 개정은 §13을 따른다.

[src: Assets/Scripts/Data/SaveSnapshotCache.cs, Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

### 12.2 변경 표시와 진단

- `NpcManager`의 호감도 생성·공동 활동 통계 갱신·이관·쌍 제거, 유형 관계 인덱싱·제거, 파티 생성·로드·해산, 인구 메타 갱신 경계에 표시를 연결했다. 새 파티는 사전에 등록하기 전에 표시하며, `System_NpcSim`도 원정 파티 상태를 바꾸기 전에 표시한다.
- `HistoryManager`의 추상 clock 전진·materialize·fold-back 변경에 정착지 인덱스를 표시한다. 마을 원장 표시와 WorldSim 표시는 각 캐시에서 별도로 처리한다.
- 기존 `NpcSaveSnapshotDiagnostics`에 다음 Editor 메뉴를 추가했다. 계약 진단은 격리된 데이터에서 동작하며, 저장 왕복 진단은 같은 프로젝트 안의 별도 `DataManager`와 `DiagnosticSavePath`를 사용한다.
  - `ARPG/Save/Diagnostics/Validate Simulation Save Snapshots`: 미표시 변경을 검사한 뒤 네 파티션의 기존 캡처/캐시 JSON 비교.
  - `ARPG/Save/Diagnostics/Verify Simulation Snapshot Contracts`: 빈 루트·순서·중첩 DTO 불변·중복 표시·삭제·실패 격리/중단/재시도·Reset/Load 수명 등을 검사.
  - `ARPG/Save/Diagnostics/Verify Simulation Snapshot Save Round Trip`: `Temp/SaveSnapshotDiagnostics/{GUID}`에 실제 writer로 전체 저장·부분 저장하고 manifest reader로 읽는다. 네 루트의 JSON·SHA-256·revision·이전 DTO 불변을 검사하고, finally에서 작업용 파일·fixture를 정리하며 시간 배율·던전 완료 저장 revision을 복원한다.
- 복제 시뮬 진단은 S-3 캐시와 인구/WorldSim 상태를 Swap/복원하고 라이브 해시에 포함한다. 진단 중에만 변경 표시 입구의 관찰자로 이전 미표시 변경을 검사하고, NPC·파티 처리 뒤 정리하며 끝에 전수를 비교한다. 자체 파티 처리 사본의 표시도 런타임과 맞췄다. 누락 비교·측정은 Editor/개발 빌드에 한정하며 관찰자는 Editor 진단에서만 사용한다. 평소 플레이 자동 전수 검사는 추가하지 않았다.

[src: Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs, Assets/Scripts/Manager/NpcManager_Party.cs, Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs]

### 12.3 검증 결과

기존 프로젝트의 Unity Editor 6000.4.2f1과 GameScene에서 검증했다. 별도 Unity 프로젝트는 만들지 않았다.

| 항목 | 결과 |
|---|---|
| 컴파일 | Unity 스크립트 컴파일 및 기존 Runtime/Editor .NET 빌드 오류 0. 기존 경고 Runtime 22 / Editor 13개, 새 경고 없음. |
| 기존 NPC 진단 | Shared Activities 28 PASS / Personal Stories 32 PASS. |
| S-3 계약 | 격리된 계약 검사 28/28 PASS. 실패 격리·저장 중단·재시도와 DTO 불변 검사 포함. |
| 실제 데이터 | NPC 2,406명·마을 300개·파티 339개·정착지 상태 338개. S-3 네 파티션의 기존 전체 캡처와 캐시 조립 JSON 4/4 동일, 미표시 변경 0. |
| 기존 캐시 회귀 | NPC 32샤드 경로 동등성 32/32(이동 위치 제외, S13), NPC·마을 누락 0, 마을 JSON 동일. |
| 1일 복제 시뮬 | NPC·마을·S-3 모두 표시 누락 0 / 끝 비교 누락 0, 에러 로그 0. 라이브 상태 해시 전후 동일(`1776683e11b054e4`). |
| 실제 writer/reader 왕복 | 프로젝트 Temp의 전체 저장 revision 1 → 부분 저장 revision 2. NpcSocial·NpcParties·NpcMeta·WorldSim 4/4 PASS: 저장/읽기 JSON 동일, 동일 상태의 두 저장 SHA-256 동일, 이전 게시 DTO 불변. 부분 저장에는 기존 결합에 따라 NpcState도 포함됐다. |
| 원래 세이브·정리 | 사용자 저장 파일 88개를 덮어쓰지 않았고 모든 SHA-256·파일 수 동일 확인. 작업용 저장·백업·빌드 산출물·scratch 보고서는 사용이 끝난 뒤 삭제했다. |

성능은 **S-3 네 파티션의 메인 스레드 캡처**와 전체 저장 시간을 구분한다.

| 같은 데이터 3회 비교 | 1회 | 2회 | 3회 |
|---|---|---|---|
| 기존 전체 깊은 복사 | 56.7582ms | 36.8534ms | 36.1494ms |
| 캐시 정리 + 루트 조립 | 1.1477ms | 1.1726ms | 1.6965ms |

- 위 비교는 JSON 직렬화·파일 I/O를 포함하지 않으며, 전체 재생성 비용을 없앤 수치도 아니다.
- 임시 부분 저장의 `[SaveTiming]`: 메인 캡처 4.4ms, 깊은 복사 0.0ms, S-3 정리 0.020ms·조립 1.564ms. manifest 준비까지 메인 합계 약 7.0ms, 백그라운드 약 686ms였다. 범위는 WorldSim·NpcMeta·NpcState·NpcSocial·NpcParties이며, 아직 시뮬 6개 파트 묶음 저장의 실측은 아니다.
- 전체 재생성 대상 저장은 정책대로 캐시를 다시 복사한다. 최초·이관 경로와 화면 전환/수동 저장의 이 비용은 남아 있다. 프레임 전체 1ms 목표 달성을 뜻하지 않으며, 마을 정리 목표 미달은 §11.3을 따른다.

### 12.4 남은 작업

- **묶음·증분 저장 구현**: §13으로 완료했다. 실제 상점 거래·던전 커밋의 전체 비용 실측은 장기·PlayMode 통합 검증과 함께 남긴다.
- **장기 검증**: S-2·S-3가 모두 포함된 30일 복제 시뮬 완료 결과는 아직 없다. 기존 S-1의 30일 PASS를 S-2·S-3 검증 결과로 보지 않는다.
- **실제 연동 경로**: S-3 적용 후 실제 던전 입장·퇴장·완료, 청크 왕복, 진행 중 건설·벽 계획을 모두 재실행하지 않았다. 임시 writer/reader 왕복과 1일 복제 시뮬은 이 PlayMode 통합 검증을 대신하지 않는다.
- **성능**: 마을의 변경 프레임 정리 평균 1ms 미만 목표는 §11.3 표본에서 미달했다. S-3 캡처 개선과 별도로 남은 성능 작업이다.
- **후속 기능**: 던전 중 월드 시뮬 계속 실행은 아직 구현하지 않았다. 캡처 방식·묶음 저장 선행 작업과 구분한다.

## 13. 시뮬 묶음·증분 저장 (2026-10-05)

### 13.1 확정 계약

- `SaveScope.Simulation` = NpcState·NpcSocial·NpcParties·NpcMeta·Villages·WorldSim. 하나라도 요청되면 여섯 파트를 같은 메인 스레드 상태에서 캡처하고 **하나의 manifest 교체**로 확정한다. 각 파일의 생성 revision이 같아야 한다는 뜻은 아니다. 변경 없는 파일의 FileName·Revision·Length·Sha256·SchemaVersion은 그대로 재사용한다.
- 미저장 NPC 던전 완료가 있으면 `Dungeons` 또는 시뮬 묶음 요청에 Dungeons와 묶음 전체를 포함한다. 완료 revision은 캡처 시점 값만 성공 후 확정하므로 저장 도중의 새 완료는 다음 저장에 남는다. 일반 Player/World 저장과 NPC 완료가 없는 Player/World/Dungeons 체크포인트에는 시뮬 묶음을 추가하지 않는다. 최초·이관의 All 승격은 유지한다.
- **프레임 정리 목록과 디스크 확정 세대를 분리한다.** NPC는 샤드별, 마을은 루트별, S-3는 캐시별 세대를 가진다. 표시·전체 재생성·Reset·진단의 누락 보정이 세대를 전진시킨다. S-3 식별자에는 캐시 인스턴스를, WorldSim에는 Version·IsInitialized·배열 길이/null도 포함한다. 프레임 정리는 미저장 세대를 지우지 않는다.
- `DataManager` 인스턴스가 파트별 마지막 성공 세대를 소유한다. 불완전 스냅샷의 재시도/저장 중단을 기존대로 수행한 뒤 루트를 조립하며, 이전 manifest 참조와 성공 세대가 같으면 백그라운드 직렬화 대상에서 뺀다. **manifest 교체 성공 직후 캡처했던 세대만 확정**한다. 캡처 실패·직렬화/쓰기/manifest 실패·Reset으로 취소된 저장은 성공 세대를 전진시키지 않는다. 다음 표시가 없어도 다음 묶음 요청이 실패한 변경을 다시 저장한다.
- 최초·이관·스위치 OFF·전체 재생성 경로도 직렬화 결과의 schema·길이·SHA-256이 기존 파일과 같으면 새 파일을 만들지 않는다. 변경 표시가 실제 값 변경 없이 수행된 경우도 기존 참조를 재사용한다. 런타임 미캡처 저장은 캐시 성공 세대를 설정하지 않으며, Reset/Load/previous 복구는 그 세대를 비운다.
- 파일 형식과 순서, `.tmp → 파일 rename → manifest File.Replace` 원자 커밋, active/previous 전체 검증·폴백은 유지한다. 정리는 active와 previous가 참조하는 모든 파일을 보존한다. 실패로 남은 파일은 다음 성공 저장 정리에서 제거한다.
- `[SaveTiming]`은 새 파일 수·재사용 참조 수·실제로 쓴 바이트를 구분한다. 전체 캐시 재생성 비용은 기존 정책대로 남으며, 변경이 많으면 32샤드 모두 새 파일이 될 수 있다. 변경 없는 캐시도 저장 직전 검증/루트 조립은 수행하지만 JSON 직렬화·쓰기에서는 제외된다.

[src: Assets/Scripts/Data/SavePartitionData.cs, Assets/Scripts/Data/SaveSnapshotCache.cs, Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Village/VillageManager_SaveSnapshot.cs, Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

### 13.2 진단과 검증

- 기존 `Verify Simulation Snapshot Save Round Trip` 메뉴를 확장했다. 실제 S-3 네 루트 왕복에 **파일 참조 동일성**을 추가하고, 별도 원장·캐시·DataManager로 여섯 범위 결합, Player/World 범위 유지, 단일 NPC 샤드/마을 변경, 신규 NPC·빈 샤드 삭제, 전체 재생성/헛표시 재사용, NPC 완료 revision, 저장 중 추가 변경·큐, 네 실패 지점/재시도, Reset 취소, 손상 후 previous 전체 폴백을 검사한다.
- 추가 진단은 플레이를 잠시 정지하고 AR의 원장 소유자 참조를 격리 매니저로 교체한다. finally에서 I/O를 기다린 뒤 참조·정지 상태·로그 핸들러를 복원하고 자기 Temp 저장·fixture만 삭제한다. 기존 복제 시뮬도 새 NPC 샤드·마을 세대를 Swap/복원하고 라이브 해시에 포함한다.
- 기존 `DungeonRuntimeDiagnostics`도 파일 생성 revision과 manifest 확정 revision을 구분하도록 갱신했다. 던전 입장 시 변경 없는 World 참조와 이관의 All 캡처/동일 파일 재사용을 검사한다.

이번 개정은 현재 프로젝트 Unity Editor 6000.4.2f1의 GameScene에서 검증했다. 별도 Unity 프로젝트는 만들지 않았다.

| 항목 | 이번 개정 결과 |
|---|---|
| 컴파일 | Unity 스크립트 컴파일 및 기존 Runtime/Editor .NET 빌드 오류 0. 기존 경고 Runtime 22 / Editor 13개 유지. 작업용 Output/Intermediate 경로 사용. |
| 기존 캐시 계약 | Village 17/17 PASS, Simulation 28/28 PASS. |
| 실제 S-3 왕복 | NPC 2,406명·마을 300개에서 4/4 PASS. JSON·SHA-256·이전 DTO 불변에 더해 변경 없는 네 파트의 FileName도 동일. 부분 요청의 범위는 Simulation 전체. |
| 묶음·증분 계약 | 1,916개 검사 PASS. 여섯 요청 범위·단일 샤드/마을 쓰기·신규/삭제·전체 재생성/헛표시 재사용·NPC 완료 세대·저장 중 변경/큐·serialize/part-write/after-part/before-manifest 실패 후 재시도·Reset 취소·손상 active 거부와 공유 파일을 포함한 previous 전체 읽기 포함. |
| 기존 던전/저장 진단 | DungeonTable·300마을 슬롯·레이아웃·snapshot isolation·던전 완료 롤백/중복 방지·active 손상/previous reader·레거시·큐 병합/배치별 결과·정리/다음 저장 배제·정리 실패 재시도·실제 manifest 교체 I/O 실패·이관 All 승격·2,400 NPC 왕복·Load/Reset 배제 모두 PASS. |
| 1일 복제 시뮬 | 19.7초. NPC·마을·S-3 표시 누락 0 / 끝 비교 누락 0, 에러 로그 0. 라이브 해시 동일(`68e2b60dd9a8a418`). 복원 뒤 라이브 전수 비교도 세 영역 누락 0. 30일 검증을 대신하지 않는다. |
| 사용자 저장·정리 | 원본 저장 파일 88개를 백업하고 종료 후 파일 수·SHA-256 동일 확인. fixture 저장은 프로젝트 Temp만 사용했다. 작업용 저장·빌드·백업과 이번 1일 시뮬의 CSV 2개/요약 1개만 삭제하며 기존 진단 로그·캐시는 보존한다. |

- 실제 데이터의 변경 없는 부분 저장 표본은 새 파일 **0개**였다. 캐시 갱신 세대가 달랐던 일부 후보도 JSON이 같아 기존 파일을 재사용했다. 첫 런타임 캡처와 전체 재생성은 후보 직렬화가 필요할 수 있으며, 이 결과를 모든 플레이 저장의 비용 0으로 해석하지 않는다.
- 격리된 원장의 여섯 파트 변경 표본(revision 20)은 새 파일 6개·기존 참조 37개 재사용, 메인 캡처 0.3ms·백그라운드 15.5ms였다. 소규모 fixture 수치이며 실제 2,400 NPC 시뮬의 변경량·I/O 성능을 대표하지 않는다.
- 진단 fixture 수정 이력: 초기 구성의 빈 Player 목록이 테이블 초기화를 요구해 실패하여 월드 플레이어를 명시했다. 복구 검사도 게임 초기화·전역 EntityId 예약을 호출하는 전체 Load 대신 기존 manifest reader의 active 거부/previous 전체 읽기로 한정했다. 위 PASS는 수정 후 최종 실행 결과다.

[src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs]
