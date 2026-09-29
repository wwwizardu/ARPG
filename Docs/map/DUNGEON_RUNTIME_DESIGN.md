# 절차 던전 런타임 설계 (초안)

> 상태: 2026-09-16 설계 초안. 구현 전. 이 문서의 **확정** 항목은 사용자 요청 또는 답변이고, **제안** 항목은 구현 전에 검토할 기본안이다.
>
> 범위: 오픈월드의 던전 입구 배치, 상호작용 입장, 별도 절차 맵, ESC 메뉴 퇴장, 완료 후 입구 교체, 저장·성능. 함정·보상 밸런스·난이도 선택 UI는 이 단계에서 다루지 않는다.

## 1. 목표와 현재 기준선

- 오픈월드에서 보이는 것은 **던전 입구**뿐이다. E 상호작용으로 들어가면 해당 던전 ID에 연결된 별도 절차 맵을 생성·로드한다. 던전에서는 ESC 메뉴의 **던전 나오기**로 기존 월드에 돌아온다. [확정: 사용자 요청]
- 발견한 던전 입구는 완료 전까지 고정 위치에 있다. 보스 처치가 완료 조건이다. 완료한 입구는 사라지고, 같은 마을 주변의 다른 유효 위치에 다음 입구가 생긴다. [확정: 사용자 요청 및 2026-09-16 답변]
- 던전에서 사망하면 월드의 해당 던전 입구 부근으로 돌아오고 내부 진행은 유지한다. 완료 후 사망이라면 사라진 입구의 이전 좌표 부근으로 돌아온다. [확정: 2026-09-16 답변]
- 손제작 마을과 절차 생성 마을을 포함한 **모든 마을은 근처에 활성 던전 2~3개를 유지한다.** 내부 맵은 입장 전까지 생성하지 않으며, 입구 위치가 미확정인 슬롯만 해당 마을 접근 시 시간분할로 배치한다. [확정: 사용자 요청 및 2026-09-19 답변]

현재 `MapManager`는 8×8 청크를 플레이어 주변에서 생성·렌더하고, Active/Warm 청크를 관리한다. 월드 변경은 타일 차이만 Map 파티션에 저장한다. 던전 공간을 월드 청크 좌표에 그대로 섞으면 월드의 청사진·마을·몬스터·NPC·타일 변경과 충돌하므로 **맵 컨텍스트를 분리**해야 한다. [`MAP_DESIGN.md`](MAP_DESIGN.md), [`MapManager.cs`](../../Assets/Scripts/Manager/MapManager.cs), [`MapManager_Spawner.cs`](../../Assets/Scripts/Map/MapManager_Spawner.cs), [`SAVE_PARTITION_DESIGN.md`](../tech/SAVE_PARTITION_DESIGN.md)

`MapFileData.MapType`에 `Dungeon` 값은 있지만 내부 던전 생성·전환 구현은 없다. 기존 던전 문서는 `_archive`에 있으며, 그 문서의 던전 내 저장 금지·일일 리셋·퇴장 제한은 이번 요청의 고정 입구·ESC 퇴장 요구와 맞지 않으므로 이번 런타임 계약으로 사용하지 않는다. [`MapFileData.cs`](../../Assets/Scripts/Map/MapFileData.cs), [`dungeonSystem.md`](../_archive/dungeonSystem.md)

## 2. 플레이 흐름

```text
월드에서 마을 접근
  → 해당 마을의 미확정 입구 슬롯만 위치 결정·저장
  → 근접 청크에서 입구 표현 활성화
  → E 상호작용 → 입장 체크포인트 → 던전 컨텍스트 활성화
  → 최초 입장만 구조 생성, 이후에는 같은 구조와 저장된 진행 복원
  → ESC → 메뉴의 "던전 나오기" → 퇴장 체크포인트 → 월드 복귀
  → 보스 처치 → 완료 체크포인트 → 기존 입구 제거·다음 세대 예약
  → 마을 근접 배치 예산 안에서 새 위치 확정·저장
```

입장 전의 입구와 던전 구조는 별개다. 월드 시작 시 모든 던전의 방·타일·몬스터를 생성하지 않는다. 플레이어가 해당 마을 주변에 접근하면 입구의 **논리 위치**를 확정하고, 입구 청크가 활성화될 때만 표현용 오브젝트를 만든다. 내부 구조는 첫 입장 때 생성한다. 이후 청크 타일은 필요할 때만 구조 메타데이터에서 재구성한다. [제안]

입구 상호작용은 기존 Player/Interact 액션(E)을 사용한다. NPC·마을 서비스와 입구가 동시에 가까우면 **거리상 최근접 후보 하나**를 프롬프트와 E 입력이 함께 선택하도록 상호작용 후보 라우팅을 확장한다. 입구를 마을 시설 데이터로 위장하지 않는다. [`System_Input.cs`](../../Assets/Scripts/Common/System/System_Input.cs), [`ServiceUIRouter.cs`](../../Assets/Scripts/Village/ServiceUIRouter.cs), [`System_VillageInteractionPrompt.cs`](../../Assets/Scripts/Common/System/System_VillageInteractionPrompt.cs)

ESC는 현재 UIManager에서 열린 UI가 없을 때 `UIMainMenu`를 연다. 던전 중에만 **던전 나오기** 버튼을 보이고, 누르면 퇴장 요청을 처리한다. 메뉴가 열려 있는 동안 던전 전투·이동을 멈추는 입력/시간 정책도 명시적으로 적용해야 한다. 완료 이후에도 메뉴 퇴장은 사용할 수 있다. [`UIManager.cs`](../../Assets/Scripts/Manager/UIManager.cs), [`UIMainMenu.cs`](../../Assets/Scripts/UI/UIMainMenu.cs)

## 3. 입구 배치와 교체

### 3.1 마을별 슬롯

모든 마을마다 `SlotIndex` 0..N-1을 두고, 슬롯마다 활성 `DungeonId`를 하나만 가진다. `VillageData.VillageId`를 영속 마을 키로 쓰므로 손제작 마을의 `SettlementId=-1`도 동일하게 포함한다. 슬롯 수 N은 마을을 처음 등록할 때 결정론적으로 2 또는 3으로 확정해 저장하며, 완료 후에도 같은 슬롯 수를 유지한다. [`VillageData.cs`](../../Assets/Scripts/Village/VillageData.cs), [`village.md`](../../Wiki/village.md)

`DungeonId = (WorldId 또는 월드 시드, VillageId, SlotIndex, Generation)`의 안정적인 조합이다. `Generation`은 해당 슬롯을 완료할 때만 증가한다. 입구 위치 추첨은 별도 `DerivedSeed` 채널과 이 조합을 사용해 **접근 순서·프레임·청크 로드 순서에 영향을 받지 않는다**. `UnityEngine.Random`의 전역 상태나 월드 몬스터 스폰 RNG를 공유하지 않는다. [제안]

1. 플레이어가 마을 접근 범위에 들어오면 아직 위치가 없는 슬롯만 처리한다. 한 프레임의 후보 평가 수와 청크 조회 수에 상한을 둔다.
2. 마을 경계 바깥의 거리 띠에서 후보 좌표를 결정론적 순서로 추출한다. 월드 경계, 타일 통행 가능 여부, 입구 발판 크기, 마을·청사진·벽·건물·기존 입구와의 겹침, 최소 입구 간격을 검사한다. 최종 입구 앞에는 플레이어가 설 공간과 E 상호작용 경로가 있어야 한다.
3. 통과한 첫 후보의 좌표와 입구 ID를 Dungeon 저장 파티션에 기록한다. **발견 여부와 무관하게 한 번 확정한 위치는 완료 전까지 변경하지 않는다.** 이는 요구 조건보다 강한 안정성 계약이다.
4. 입구가 속한 월드 청크가 활성일 때만 비주얼/콜라이더를 재사용 가능한 오브젝트로 실체화하고, 청크가 비활성일 때 제거·풀에 돌려준다. 논리 레코드는 유지한다.

후보가 모두 실패하면 슬롯은 `PlacementPending`으로 두고 시간분할로 추가 후보를 검사한다. 검증을 통과하지 않은 입구를 벽·건물 위에 강제로 만들지 않는다. 접근 가능한 후보가 부족한 마을에서 2~3개를 무조건 보장할 수 있는지 진단으로 확인하고, 그때 거리 띠·발판 조건을 조정한다. [제안]

### 3.2 완료 후 새 위치

보스 처치 이벤트가 같은 `DungeonId`에 대해 한 번만 완료를 적용한다. 기존 입구를 비활성으로 바꾸고 해당 슬롯의 `Generation`을 1 올린다. 완료 보상과 새 세대의 `PlacementPending` 상태를 **같은 저장 revision**에 커밋한다. 이벤트의 세대가 현재 슬롯 세대와 다르면 중복 완료·보상을 거부한다. 성공하면 월드에 보이는 이전 입구를 즉시 없앤다. 새 위치의 후보 평가는 마을 근접 배치 예산에서 시간분할하며, 이전 좌표와 다른 활성 입구를 제외한다. 새 좌표가 확정되면 그 입구 레코드를 별도로 저장하고 해당 청크가 가시 범위에 있을 때 표현을 활성화한다. 이렇게 하면 보스 처치 프레임에 주변 청크를 한꺼번에 만들지 않는다. [제안]

완료 당시 플레이어가 아직 내부에 있어도 현재 던전 컨텍스트는 **퇴장할 때까지 유지**한다. 던전 안에서 저장·재접속하더라도 이 완료된 세션을 복원할 수 있어야 한다. 월드 복귀 좌표는 이전 입구 부근의 안전한 타일로 유지한다. 복귀 좌표가 건설·타일 변경으로 막혔다면 인접 통행 가능 타일을 찾는다. 새 입구는 다른 위치여야 하지만, 오래전 완료한 좌표까지 영구 금지하면 저장 목록이 끝없이 자라므로 **직전 위치 재사용 금지**를 기본안으로 둔다. [제안]

### 3.3 NPC 완료 추가 계약 (2026-09-29, NPC_AMBITION P2a)

화면 밖 모험가 NPC가 드물게 던전을 완료해 슬롯 세대를 넘길 수 있다. 완료 확률·보상·빈도 같은 게임플레이 규칙은 [NPC_AMBITION_DESIGN §7.1](../npc/NPC_AMBITION_DESIGN.md)(D55·D57·D58·D63·D66~D68)이 소유하고, 이 절은 **슬롯 변경과 저장 계약**만 정한다. [src: Assets/Scripts/Dungeon/DungeonManager_NpcCompletion.cs] [확정]

- **진입점 분리**: 플레이어 완료(`DungeonCompletionTransaction` — 플레이어 Gold·`DungeonRecord.Completed`·`CompletedCount`)는 바꾸지 않는다. NPC 완료는 슬롯 단위 진입점 `DungeonManager.TryCompleteByNpc`로 한다. 플레이어 Gold, `DungeonRecord`, 전역 `CompletedCount`는 건드리지 않는다.
- **슬롯 변경**: 슬롯을 제자리에서 바꾼다 — `Generation`+1, 새 `DungeonId`, 입구가 있었으면 없애고 그 좌표를 이전 입구(`HasPreviousEntrance`/`PreviousX,Y`)로 기록, `PlacementReadyGameTime` = 지금 + 교체 쿨다운, `NextCandidate` = 0. 입구가 배치되지 않은 슬롯도 완료할 수 있다(입구는 플레이어가 마을 근처에 와야 배치된다). 입구 역색인은 dirty 표시 후 다음 `TickPlacement` 첫머리에서 다시 만든다.
- **새 저장 필드**(기본값 + 로드 보정, 스키마 버전 유지): `VillageDungeonSlots.LastNpcCompletionGameTime`(−1 = 없음, 마을당 72h 쿨다운), `DungeonSlot.NpcReplacedGeneration`(−1 = 없음, 현재 `Generation`과 같으면 NPC 교체 세대). 플레이어 완료는 새 슬롯 객체를 만들어 표시가 기본값으로 돌아간다.
- **보호 규칙**(하나라도 해당하면 거부): 플레이어가 입장한 던전(`DungeonRecord` 존재) / 플레이어가 던전 안(`IsInside`) / 입장·배치 저장 진행 중 / 요청 DungeonId가 현재 슬롯에 없음(이미 세대 전진) / 마을 72h 쿨다운 / 이번 완료 뒤 NPC 교체 슬롯이 그 마을 슬롯 수의 절반 초과. **`DungeonRecord` 보호는 세이브 무결성 조건이기도 하다** — 로드 시 저장 검증(`DungeonSaveValidation`)은 기록이 있는 던전의 슬롯 `DungeonId`가 기록과 같은지 확인하므로, 기록 있는 던전의 세대를 넘기면 로드가 깨진다.
- **저장**: 즉시 저장하지 않는다. 미저장 NPC 완료가 있으면 다음 저장이 Dungeons 또는 NpcState 중 하나를 실을 때 둘을 함께 실어, 슬롯 변경과 NPC 보상이 같은 manifest revision에 커밋된다. 커밋 성공 시 미저장 표시를 지운다.

## 4. 던전 내부 맵

월드와 던전은 타일맵·청크 캐시·타일 변경·몬스터·드랍 아이템·좌표계가 분리된 `MapContext`를 사용한다. 기존 `AR.s.Map`을 전역 월드 맵으로 가정하는 시스템이 많으므로, 구현 시 **현재 컨텍스트 조회 경로**를 먼저 마련해야 한다. 단순히 `MapManager.SetSeed()`로 시드만 바꾸면 월드 청사진과 다른 매니저의 상태를 격리할 수 없다. [`AR.cs`](../../Assets/Scripts/Manager/AR.cs), [`MapManager_Generator.cs`](../../Assets/Scripts/Map/MapManager_Generator.cs), [`System_MapChunkLoader.cs`](../../Assets/Scripts/Common/System/System_MapChunkLoader.cs)

최초 입장 시 `DungeonId`에서 `DungeonSeed`를 파생한다. 작은 **방 그래프와 복도 메타데이터**를 한 번 생성한다. 입구 방에서 모든 필수 방으로 갈 수 있어야 하고, 보스 방은 입구와 충분히 떨어진 연결된 방으로 선택한다. 생성 실패 시 제한된 횟수만 재시도하고 검증되지 않은 구조는 로드하지 않는다. 구조를 확정하면 `GeneratorVersion`과 방/복도 메타데이터를 저장한다. 던전 크기, 방 수, 보스 콘텐츠는 별도 데이터로 조정한다. [제안]

타일 전체 배열은 저장하지 않는다. 방/복도 메타데이터에서 플레이어 주변 8×8 청크만 래스터화·렌더하며, 플레이어가 이동하면 기존 청크 풀을 재사용한다. 구조 메타데이터를 저장하므로 생성 알고리즘이 업데이트되어도 진행 중 던전의 레이아웃이 달라지지 않는다. 함정·상자·몬스터는 `(DungeonId, RoomId, LocalObjectId)` 같은 안정 ID로 만들고, 처치·획득·열림 등의 **차이 데이터만** 저장한다. [제안]

월드 NPC·마을 시뮬레이션·건물 근접 UI·월드 몬스터 스폰은 던전 플레이어 좌표를 월드 좌표로 해석하면 안 된다. 던전 활성 동안 각 시스템은 현재 맵 종류를 확인하거나 월드 작업을 일시 중지한다. 던전 전투 시스템은 던전 엔티티만 조회한다. 월드로 돌아가면 기존 월드 컨텍스트와 매니저 상태를 복원한다. [`System_NpcSim.cs`](../../Assets/Scripts/Common/System/System_NpcSim.cs), [`System_SettlementSim.cs`](../../Assets/Scripts/Common/System/System_SettlementSim.cs), [`System_MonsterSpawn.cs`](../../Assets/Scripts/Common/System/System_MonsterSpawn.cs)

## 5. 전환·퇴장·저장

### 5.1 입장과 퇴장의 순서

입장: 상호작용 가능 거리·활성 `DungeonId` 재확인 → 중복 전환 잠금 → 던전 구조 복원/최초 생성 및 검증 → 월드 복귀 좌표, 현재 플레이어 상태, `ActiveDungeonId`, `GeneratorVersion`, 방/복도 구조 메타데이터, 초기 진행 상태를 같은 revision에 저장 → 저장 성공 후 월드 청크/월드 엔티티 비활성화 → 던전 컨텍스트 활성화 → 입구 방 안전 지점에 플레이어 배치 → UI 잠금 해제. 저장이 실패하면 월드에 남고 입장을 취소한다. 비동기 중에 입구가 교체되거나 씬이 바뀌면 작업을 취소한다. 따라서 첫 입장 직후 게임이 종료되어도 같은 던전 구조를 복원할 수 있다. [제안]

입장 저장은 **초기 던전 상태를 확정하는 체크포인트**다. 플레이 중의 몬스터 처치·상자 개봉·보스 상태 같은 진행 차이는 발생할 때 메모리에 누적하고, ESC 퇴장·사망·보스 처치·수동 저장 및 주기적 던전 자동저장 시 Dungeon 관련 파티션과 Player 파티션을 함께 커밋한다. 매 공격이나 매 프레임 저장하지 않는다. 자동저장 주기는 플레이 실측 후 정하되, 저장 중 종료되면 manifest 규칙에 따라 마지막으로 성공한 체크포인트로 복원한다. [제안]

퇴장: 메뉴 버튼 → 중복 전환 잠금 → 던전 진행 상태와 플레이어 상태를 체크포인트로 저장 → 던전 엔티티/청크 비활성화 → 월드 컨텍스트 복원 → 저장한 입구 부근 안전 지점에 플레이어 배치 → 월드 청크 활성화 → UI 잠금 해제. 저장 실패 시 전환을 완료하지 않고 재시도할 수 있게 한다. [제안]

던전 사망: 플레이어 HP가 0 이하가 된 시점에 던전 진행 차이를 체크포인트로 저장하고, 일반 `DestroyTag`가 플레이어 엔티티를 제거하기 전에 사망 전환을 처리한다. 던전 컨텍스트를 닫고 원래 입구 부근의 안전한 월드 타일에서 플레이어를 복귀·체력 복구한다. 체력 복구량·사망 패널은 기존 플레이어 부활 기능이 아직 코드에 없으므로 구현 때 별도로 정한다. 몬스터 처치·상자 등 내부 진행은 초기화하지 않는다. [`System_HpCheck.cs`](../../Assets/Scripts/Common/System/System_HpCheck.cs), [`System_EntityDestroy.cs`](../../Assets/Scripts/Common/System/System_EntityDestroy.cs) [확정: 복귀 위치·진행 유지, 제안: 처리 순서]

실제 `GameScene`은 플레이어 위치를 `PlayerData.PositionX/Z`에서 생성하고, `PlayerManager.Save()`는 현재 플레이어 위치를 그대로 기록한다. 던전 중 저장한 좌표를 월드 시작 좌표로 잘못 읽지 않도록 Player 저장에 `CurrentMapKind`, `ActiveDungeonId`, `DungeonPosition`, `WorldReturnPosition`을 분리한다. 로드 시 활성 던전 기록이 있으면 그 던전을 복원하고, 없으면 월드 위치에서 시작한다. [`GameScene.cs`](../../Assets/Scripts/Scene/GameScene.cs), [`PlayerManager.cs`](../../Assets/Scripts/Manager/PlayerManager.cs), [`PlayerData.cs`](../../Assets/Scripts/Data/PlayerData.cs)

### 5.2 저장 소유권

새 `Dungeons` 파티션에 마을별 슬롯의 세대·입구 좌표, 방문 던전의 구조 메타데이터·진행 차이를 둔다. 완료한 세대의 구조·개별 처치 목록은 **플레이어가 그 던전에서 퇴장한 뒤** 삭제한다. 완료된 내부 세션이 활성인 동안은 재접속 복원을 위해 남긴다. **슬롯의 현재 세대보다 낮은 ID는 완료된 것으로 판정**하므로 반복 클리어마다 영구 기록이 늘지 않는다. 업적·퀘스트가 총 클리어 횟수를 필요로 하면 별도 집계값을 저장한다. World 파티션에는 안정 월드 시드/ID를 보존해야 한다. 지금 `GameScene.CreateMap(12345, ...)`가 상수를 전달하고 `MapManager.mapSeed`가 필드로 존재하므로, 던전 ID를 월드 시드에 연결하기 전에 **세이브된 시드가 실제 월드 생성의 정본**이 되도록 정리해야 한다. [`GameScene.cs`](../../Assets/Scripts/Scene/GameScene.cs), [`MapManager.cs`](../../Assets/Scripts/Manager/MapManager.cs), [`SavePartitionData.cs`](../../Assets/Scripts/Data/SavePartitionData.cs)

`SaveScope.Dungeons`와 manifest의 `dungeons` 파트를 추가한다. 구 세이브/구 manifest에는 파트가 없으므로 로드 시 빈 던전 상태로 마이그레이션하고, 기존 Player·World·Map 파티션을 파괴하지 않는다. 부분 저장에서도 Dungeon 파트만 바뀌면 NPC 32샤드나 모든 마을 데이터를 다시 쓰지 않는다. 던전 내 아이템 획득, 보스 보상, 플레이어 상태, 맵 변경이 함께 발생한 체크포인트는 관련 파티션을 한 manifest revision으로 묶는다. [`SAVE_PARTITION_DESIGN.md`](../tech/SAVE_PARTITION_DESIGN.md), [`DataManager_PartitionedSave.cs`](../../Assets/Scripts/Manager/DataManager_PartitionedSave.cs)

보스 처치와 재입장 진행 복원은 같은 `DungeonId`에 대해 멱등이어야 한다. 저장 도중 종료되면 이전 manifest 전체로 돌아가므로 완료·보상·입구 교체 중 일부만 적용되는 상태를 만들지 않는다. 저장된 `GeneratorVersion`이 지원되지 않으면 임의 재생성하지 않고 명확한 복구/마이그레이션 경로를 제공한다. [제안]

### 5.3 동행 NPC 추가 계약

[NPC_COMBAT_DESIGN §4.4·§4.5](../npc/NPC_COMBAT_DESIGN.md)의 임시 동행(던전 전용)이 던전 컨텍스트 전환에 끼워지는 자리다. 동행의 규칙(요청 조건·추종·전투)은 NPC_COMBAT이 소유하고, 이 절은 **전환·저장 순서**만 정한다. 구현은 `NpcManager_Companion`이 담당하고 `DungeonManager`가 호출한다. [`NpcManager_Companion.cs`](../../Assets/Scripts/Manager/NpcManager_Companion.cs), [`DungeonManager.cs`](../../Assets/Scripts/Dungeon/DungeonManager.cs)

**저장 소유권**: 동행 상태(`NpcCompanionState` — 예약 NPC EntityId, 던전 안 동행 여부)는 **World 파티션**(`WorldSaveRoot.NpcCompanion`)에 둔다. 입장·퇴장·체크포인트 커밋이 모두 World를 포함하므로 플레이어 위치와 동행 상태가 한 revision에 묶인다. `NpcSaveData`에는 필드를 추가하지 않는다. [확정]

**입장**: 저장 직전에 예약을 검증한다(`StageCompanionDungeonEntry`). 예약 NPC가 죽었거나 부상·원정 중이면 예약을 취소하고 알린다. 통과하면 `IsInDungeon = true`를 입장 커밋에 함께 싣고, 저장이 실패하면 되돌린다. 저장 성공 → `ActivateInterior`(월드 청크 비활성화로 월드 쪽 NPC 엔티티가 ID를 유지한 채 비실체화됨) → 예약 NPC를 **같은 EntityId**로 플레이어 옆 통행 가능한 타일에 소환한다(`SummonCompanionAsync`). 소환 실패는 입장을 실패시키지 않는다. [확정]

**재접속 복원**: `RestoreSessionAsync`가 `ActivateInterior` 뒤 같은 소환을 호출한다. 로드 시 플레이어가 던전 밖인데 `IsInDungeon`이 남아 있으면 해제하고, 예약 NPC가 없거나 죽었으면 예약을 지운다. [확정]

**퇴장(ESC·사망 공통)**: 저장 직전에 동행 상태를 비워(`StageCompanionDungeonExit`) 퇴장 커밋에 싣고, 저장이 실패하면 되돌린다. 저장 성공 후 던전 맵을 내리고 월드 엔티티를 복원(`RestoreSuspended`)한 뒤, **`ResumeWorld` 전에** 동행 NPC를 정리한다(`FinishCompanionDungeonExit`): 진영 Neutral·전투 태그 제거 → 던전 쪽 엔티티 비실체화 → 소속 마을의 NPC 배치 위치(기존 스폰 규칙)로 저장 위치·청크 인덱스 갱신 → 활동 Idle. 귀환 위치 계산이 월드 맵을 조회해야 하므로 `ActiveMap` 해제 뒤여야 하고, 이어지는 `ResumeWorld`의 청크 활성화가 그 자리에 NPC를 실체화한다. 소속 마을이 없는 NPC는 플레이어의 입구 복귀 지점으로 돌아간다. [확정]

**던전 좌표 격리**: 동행 중에는 NPC의 던전 좌표를 월드 저장 위치(`NpcSaveData.Position`)·청크 인덱스에 쓰지 않는다. `SaveAllActiveNpcs`, 길찾기 보정(`TryRelocateMaterializedNpc`), `UnregisterNpcByEntityId`가 동행 중 NPC의 좌표 갱신을 건너뛴다. 저장 위치는 입장 전 월드 위치로 유지되고, 퇴장 시 마을 위치로 교체된다. [확정]

**던전 안 사망**: 동행 NPC가 전투로 죽으면 기존 사망 파이프라인(`System_HpCheck` → `System_EntityDestroy` → `MarkNpcDead`)이 영구 사망을 기록하고, 이어서 동행을 즉시 끝낸다(`EndCompanionOnDeath`). 이후 체크포인트·재접속은 죽은 NPC를 소환하지 않는다. `HandleDeath`는 NPC를 가로채지 않는다(플레이어·던전 오브젝트 전용). [확정]

**시스템 가동**: 던전 안에서는 `IWorldMapSystem`(`System_NpcSim`, `System_Time` 등)이 멈추므로 동행 NPC의 활동 판정도 멈춘다. 동행 NPC는 `System_AI_Behavior`의 "비활성 청크 AI 정지"에서 예외로 두어, 뒤처져도 순간이동할 수 있게 한다. [확정]

**하지 않는 것**: 동행 NPC의 HP는 저장하지 않는다(재접속 시 최대 HP로 소환). NPC는 던전 드랍·상자·완료 보상을 받지 않는다(D16). 필드 동행과 함께 입장하는 최종 형태(§4.4)는 C1에서 이 계약 위에 얹는다.

## 6. 성능 계약

| 시점 | 수행 | 하지 않는 작업 |
|---|---|---|
| 월드 시작 | 기존 마을/월드 로드, Dungeon 저장 인덱스 복원 | 모든 입구 후보 탐색, 모든 내부 맵 생성 |
| 마을 근접 | 해당 마을의 미확정 슬롯만 시간분할 배치·저장 | 모든 마을 주변 타일/청크 생성 |
| 입구 청크 활성 | 고정 좌표의 입구 비주얼만 활성 | 던전 구조·몬스터 생성 |
| 최초 입장 | 해당 던전 방 그래프 생성·검증, 시작 청크 렌더 | 전체 던전 타일맵 렌더 |
| 재입장 | 구조 메타데이터와 진행 차이 로드 | 새 시드 추첨·전체 타일 저장 |
| 완료 | 이전 입구 제거·다음 세대 pending·부분 체크포인트 | 같은 프레임의 새 위치 전체 탐색, 전체 NPC/마을 파티션 재직렬화 |

입구 레코드는 `VillageId → Slot[]`, `ChunkCoord → DungeonId[]` 역인덱스로 찾는다. 상호작용은 현재 근접 후보만 검사한다. 별도 입구별 Update/Coroutine을 두지 않고 한 시스템에서 일정 간격으로 배치와 표현을 처리한다. 월드 청크 풀과 던전 청크 풀은 동시에 대량 활성화하지 않고, 전환 중 이전 컨텍스트를 먼저 접는다. 후보 검사·방 생성·청크 렌더에는 프레임별 예산을 두되, 밀리초 목표는 Unity Profiler 실측 후 확정한다. [제안]

## 7. 구현 순서와 검증

1. 월드 시드의 저장 정본과 `MapContext` 격리 계약을 만든다. 월드의 청크·몬스터·NPC·건물·UI가 던전 좌표를 소비하지 않는지 확인한다.
2. Dungeon 저장 DTO/파티션과 구 세이브 마이그레이션을 만든다. 슬롯 세대·고정 위치·완료 교체의 저장 왕복을 먼저 검증한다.
3. 마을 접근 기반 입구 배치, 청크별 입구 표현, 최근접 E 상호작용을 연결한다.
4. 방 그래프 생성·연결성 검증, 청크 단위 래스터화, 진행 차이 저장을 연결한다.
5. 입장/퇴장 전환과 ESC 메뉴 버튼을 연결하고, 보스 사망 이벤트에서 완료 체크포인트를 처리한다.
6. 기존 프로젝트의 EditMode/PlayMode 진단 및 Profiler로 초기 로드·마을 근접·첫 입장·재입장·완료 프레임을 측정한다. 별도 Unity 프로젝트는 만들지 않는다.

필수 검증: 같은 세이브를 로드하거나 입구 청크를 여러 번 unload/reload해도 완료 전 ID·좌표가 동일함; 미방문 던전 내부 생성 0건; E 입력은 최근접 입구 하나에만 적용됨; ESC 퇴장 뒤 안전한 월드 좌표 복귀; 재입장 때 구조·진행 정책 유지; 보스 처치 한 번에 기존 입구만 사라지고 pending 세대가 생기며, 배치 예산 안에서 다른 좌표의 새 입구 하나가 확정됨; 저장 실패/강제 종료 후 입구·보상·완료가 한 revision 기준으로 일치함; 구 세이브 로드 가능; 마을 주변 목표 수와 배치 실패율 측정. [제안]

## 8. 확정된 진행 정책

- ESC 퇴장·사망·재접속 후 재입장하면 같은 구조와 마지막 체크포인트의 진행 차이를 복원한다. 처치 상태와 열린 상자를 초기화하지 않는다. [확정: 던전 상태 저장 요구]

보스 처치가 완료 조건이고, 사망 시 입구 복귀·진행 유지와 모든 마을의 활성 던전 2~3개 유지도 확정됐다. 던전 크기·입구와 마을 거리·난이도·보상·미니맵·사망 체력 복구량은 실제 플레이·Profiler 실측과 콘텐츠 테이블을 보며 후속 튜닝한다.
