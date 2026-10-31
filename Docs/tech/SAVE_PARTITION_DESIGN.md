# 세이브 파티션 설계

> **상태 (2026-10-05)**: 파티션 저장과 S-1~S-3 상시 스냅샷 구현 완료·핵심 기능 검증 통과. 그 위에 시뮬 6개 파트 묶음·증분 파일 쓰기를 구현했다(§5, SAVE_SNAPSHOT_DESIGN §13). S-2/S-3 장기 시뮬·실제 상점/던전 통합 비용 실측은 후속이다. 이번 개정의 진단 결과는 스냅샷 문서 §13.2에 기록한다.
> **광맥 파트 (2026-10-31)**: 월드 광맥 기록 `ore_veins` 파트(`SaveScope.OreVeins = 1 << 14`, `All` 포함)를 더했다. WorldDataVersion은 **22 그대로**이고, 파트가 없는 세이브는 빈 기록으로 연다. Player를 싣는 저장은 이 파트도 싣는다(§5). 기획은 [WORLD_ORE_VEIN_PLAN §8.1](../map/WORLD_ORE_VEIN_PLAN.md).
> **탐험 파트 도입·제거 (2026-10-10)**: 현재 WorldDataVersion은 **22**. 시야·안개의 탐험 기억 `exploration` 파트와 던전 기록의 탐험 칸을 더했다가 같은 날 제거했다. 22 세이브에 남은 것은 읽지 않고 다음 저장에서 빠진다(§8). 경위는 [FOG_OF_WAR_DESIGN §6](../map/FOG_OF_WAR_DESIGN.md).
> **내러티브 파트 개정 (2026-10-10)**: WorldDataVersion **21**. NPC 행적·반응의 진행 원장 `narrative` 파트(`SaveScope.Narrative`)를 더했다(§2·§3·§5·§8). 원장 안의 형식·dirty 그룹은 [NPC_NARRATIVE_CONTENT_DESIGN §7](../npc/NPC_NARRATIVE_CONTENT_DESIGN.md)을 따른다.
> **기억 저장 개정 (2026-10-04)**: WorldDataVersion을 **19**로 올렸다. 공동 활동 통계·최신 개인 사연·과거 동료 기억을 저장하고, `NpcState`와 `NpcSocial`은 부분 저장에서도 같은 revision으로 커밋한다. 기억 진단은 통과했으며 전체 플레이 모드·장애 주입 검증은 여전히 후속이다(§9).
>
> **범위**: 파일 분할, 저장 범위, NPC 샤딩, 원자 커밋, 레거시 마이그레이션.
> NPC 기본 장비는 기본 아이템 행 번호로 저장한다(2026-10-03). 그 밖의 별도 compact DTO/시드 재구성은 후속이다. [src: Docs/npc/NPC_SHOP_EQUIPMENT_DESIGN.md §11]

## 1. 목적

물리 NPC 목표가 대형 맵 최소 1,200명, 마을당 8명 밀도 목표로 증가하면서
300개 마을 기준 목표가 2,400명이 되었다. NPC 개별 장비·활동·관계를 포함한 기존
`WorldData.json` 전체 저장은 아이템 획득 같은 플레이어 전용 변경에도 월드 전체를
백업·직렬화하므로 프레임 hitch를 만든다.

세이브는 다음 원칙을 따른다.

1. 변경 소유권과 빈도에 따라 파티션을 나눈다.
2. 아이템·장비·스킬 변경은 Player와 다음 `ItemInstanceId`를 가진 World 메타만 저장한다.
3. 여러 파티션의 정합성은 manifest를 마지막에 교체해 보장한다.
4. NPC 개별 상태는 영구 `EntityId` 기준 32개 고정 샤드로 분산한다.
5. 운영 JSON은 `Formatting.None`을 사용한다.

## 2. 저장 디렉터리

```text
Saved/Slot_0/
  manifest.json
  manifest.prev.json
  parts/
    player.r{revision}.{guid}.json
    world.r{revision}.{guid}.json
    map.r{revision}.{guid}.json
    dungeons.r{revision}.{guid}.json
    narrative.r{revision}.{guid}.json
    ore_veins.r{revision}.{guid}.json
    villages.r{revision}.{guid}.json
    buildings.r{revision}.{guid}.json
    chronicle.r{revision}.{guid}.json
    world_sim.r{revision}.{guid}.json
    npc_meta.r{revision}.{guid}.json
    npc_social.r{revision}.{guid}.json
    npc_parties.r{revision}.{guid}.json
    npc_state_00..31.r{revision}.{guid}.json
```

`manifest.json`은 각 논리 파티션의 실제 파일명·스키마 버전·길이·SHA-256을 가진다.
파티션 파일은 덮어쓰지 않고 새 revision으로 만든다.

## 3. 파티션 책임

| 파티션 | 데이터 |
|---|---|
| Player | `PlayerData` 목록, 현재 플레이어 인덱스, 플레이어↔NPC 관계 |
| World | WorldData 버전, 다음 `ItemInstanceId` |
| Map | 월드 드랍 아이템, 타일 변경 |
| Dungeons | `DungeonSaveRoot`: 던전 슬롯·세대·입구, 방문한 던전의 맵·방·배치·몬스터 상태·처치·클리어·드롭 ([DUNGEON_RUNTIME_DESIGN](../map/DUNGEON_RUNTIME_DESIGN.md)) |
| Narrative | `NarrativeSaveRoot`: 콘텐츠 실행 인스턴스(던전 소문 사건의 대상·소문 주인·진행·보고·보답, 소망의 주인·대상 아이템·진행·전달 반응·보답), 정의·정책 해시 스냅샷, 세대 선정 기록 ([NPC_NARRATIVE_CONTENT_DESIGN §7.2](../npc/NPC_NARRATIVE_CONTENT_DESIGN.md)) |
| OreVeins | `OreVeinSaveRoot`: 광맥이 있거나 고갈 대기 중인 청크의 광맥 기록(청크 좌표·광종·칸·잔량·굴린 날짜·상태·재생 가능 시각·마지막 활성 시각). 소유자는 `OreVeinManager` ([WORLD_ORE_VEIN_PLAN §4.2](../map/WORLD_ORE_VEIN_PLAN.md)) |
| Villages | `VillageData` 목록 |
| Buildings | `BuildingSaveData` 딕셔너리 |
| Chronicle | 정착지 연대기와 왕국 스냅샷 |
| WorldSim | 런타임 정착지 추상 상태 |
| NpcMeta | 목표 인원·보충 시각·순회 커서 |
| NpcSocial | NPC↔NPC 현재 호감도·유형 관계, 쌍당 공동 활동 통계(`NpcAffinityEntry.SharedActivities`) |
| NpcParties | 파티 리더·멤버·공통 활동 상태 |
| NpcState 00~31 | 개별 `NpcSaveData`: 중요 Deeds·사망 관계 기억·최신 사연 요약/참조(`PersonalStories`)·과거 동료 활동 기억(`SharedActivityMemories`)·부탁 칸(`NarrativeRequest`)·최근 원정 부상 던전(`LastWoundDungeonId`) |

플레이어↔NPC 관계는 단일 플레이어 전제의 기존 WorldData 필드에서 Player 파티션으로
이동한다. 런타임 소유자는 계속 `NpcManager`지만 저장 범위는 플레이어 진행과 일치한다.

공동 활동은 사냥·던전 원정·공동 클리어 횟수, 원정 호감도 누적 변화, 마지막 활동·시각을 쌍당 한 번 저장한다. 현재 호감도와 누적 변화는 별개다. 사망 등으로 활성 쌍을 제거할 때는 양쪽 NPC의 State에 상대 이름·마지막 호감도·활동 기억을 남긴다. 필드 의미와 최신 사연 6종은 [NPC_AMBITION_DESIGN §12.1](../npc/NPC_AMBITION_DESIGN.md)을 따른다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs]

## 4. NPC 샤딩

샤드 번호는 `entityId & 31`이다. `EntityId`는 양수 영구 ID이고 32는 2의 거듭제곱이므로
분포가 균등하고, NPC가 이동하거나 소속 마을이 바뀌어도 샤드 이동이 없다.

로드 시에는 32개 샤드를 모두 읽어 하나의 `NpcManager` 저장 딕셔너리로 병합한다.
오프스크린 NPC도 개별 시뮬레이션 대상이므로 샤드는 지연 로딩 단위가 아니라 저장·검증
크기를 제한하는 단위다.

## 5. 저장 범위

| 변경 | 범위 |
|---|---|
| 아이템 획득, 장비·스킬북·스킬 페이지 변경 | Player + World(ItemInstanceId) |
| 상점 구매·판매 | Player + World + Simulation(여섯 시뮬 파트) |
| NpcState·NpcSocial·NpcParties·NpcMeta·Villages·WorldSim 중 하나라도 요청 | Simulation 전체, 변경 없는 파일 참조 재사용 |
| 던전 소문 부탁 표시, 소망 털어놓기 표시, 보고·알림·소망 전달 표시 확정, 후속 소개 확정(소개한 NPC의 부탁 칸) | Narrative + Simulation(Narrative가 NpcState를 끌어온다) |
| 소문 보고(반응·관계·의뢰 보답), 소망 아이템 전달(소모·반응·관계·의뢰 보답) | Player + World + Narrative + Simulation |
| `PlayerDeed` 소문 반응 | Player + World + Simulation |
| 던전 보스 완료 | 기존 완료 범위 + Narrative(소문 해결·미해결 종료를 같은 manifest로) |
| 거점·메뉴 수동 저장, 최초 월드 저장 | 전체 파티션 |
| Player를 싣는 모든 저장 | 요청 범위 + OreVeins (캔 광석과 광맥 잔량을 같은 manifest로) |

NPC 죽음처럼 NPC 상태·관계·파티·연대기가 함께 바뀌는 체크포인트는 manifest 한 revision에
묶인다. 향후 자동저장을 추가할 때도 변경된 샤드와 연관 파티션을 같은 revision으로 요청한다.

**현재 결합 계약(2026-10-05)**: 저장 스냅샷을 잡기 전에 `SaveScope.Simulation`의 여섯 파트 중 하나라도 요청되면 묶음 전체로 확장한다. 여섯 상태는 같은 메인 스레드에서 캡처되어 하나의 manifest 교체로 확정된다. `Chronicle`은 자동으로 추가하지 않는다. 미저장 NPC 던전 완료가 있으면 `Dungeons` 또는 묶음 요청에 Dungeons와 Simulation 전체를 함께 싣고, 캡처한 완료 revision만 성공 후 확정한다. 이관으로 전체 저장이 필요한 상태면 요청과 관계없이 `All`로 승격한다. 기존 파티 경로 위치 복원(S13)과 State/Social 공동 활동 일관성도 이 묶음에 포함된다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Data/SavePartitionData.cs]

**Narrative 결합(2026-10-10)**: NPC의 부탁 칸과 `PlayerDeed` 소문(NpcState)이 원장의 사건(Narrative)을 가리키므로, Narrative를 싣는 저장은 NpcState도 싣고 그래서 시뮬 묶음 전체가 된다. 즉시 저장하지 않는 사건 변경(새 세대 선정·소문 주인 선정·미해결 종료)은 NPC 던전 완료와 같은 방식으로 처리한다. Dungeons나 시뮬 묶음을 싣는 다음 저장에 Narrative와 시뮬 묶음을 붙이고, 캡처한 Narrative revision만 성공 뒤 확정한다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/NpcManager_Narrative.cs, Assets/Scripts/Manager/NpcManager_NarrativeDialogue.cs, Assets/Scripts/Dungeon/DungeonManager.cs]

**광맥 결합(2026-10-31)**: Player를 싣는 저장은 `OreVeins`도 싣는다. 결합 규칙은 저장 계층(`WritePartitionSnapshotAsync`)의 Narrative→NpcState 규칙 옆에 한 줄로 둔다. 그래서 아이템 줍기·스킬·마법부여·상점·던전 입장/퇴장/자동 저장/완료·결투·소망·보고 저장과, 던전 안이라 Player가 자동으로 더해지는 저장이 모두 광맥 파트를 싣는다. 광맥 기록 변경은 owned 스냅샷 캐시에 청크 키로 표시만 하고 즉시 저장하지 않는다. 내용이 같으면 기존 파일 참조를 재사용한다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Manager/DataManager_SaveSnapshot.cs, Assets/Scripts/OreVein/OreVeinManager.cs]

**증분 파일 쓰기**: 프레임 정리와 독립된 캐시/샤드 세대를 `DataManager`의 마지막 성공 세대와 비교한다. 같으면 기존 참조를 재사용하고 JSON 직렬화를 건너뛴다. 후보 파트도 schema·JSON 길이·SHA-256이 기존과 같으면 새 파일을 만들지 않는다. 성공 후 캡처 세대만 확정하며 저장 도중 추가 변경·실패한 변경은 다음 요청에 남긴다. Reset/Load는 성공 세대를 비운다. 여섯 파트의 파일 생성 revision은 서로 달라도 하나의 manifest가 함께 선택한 전체 상태다. 계약·진단은 [SAVE_SNAPSHOT_DESIGN §13](SAVE_SNAPSHOT_DESIGN.md)을 따른다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

**NPC 상태 스냅샷(2026-10-04)**: 위 저장 범위와 32샤드·manifest 계약을 유지한다. `NpcManager`가 NPC별 불변 DTO와 변경 목록을 소유하고, LateUpdate Priority 990 및 저장 직전에 변경 NPC만 다시 복사한다. 저장은 새 샤드 루트에 캐시된 NPC DTO를 넣어 전체 NPC 깊은 복사를 우회한다. 수동·최초·이관 전체 저장과 던전 입장·퇴장·완료 커밋에서는 전체 재생성한다. 최초·이관의 런타임 미캡처 경로는 기존 `_worldData` 깊은 복사를 유지한다. 던전 커밋의 재생성 요청은 저장 큐에서도 해당 배치에 유지된다. 복사에 실패한 NPC가 남아 있으면 낡은 DTO를 쓰지 않도록 그 저장을 중단한다([SAVE_SNAPSHOT_DESIGN](SAVE_SNAPSHOT_DESIGN.md) S12). 화면 밖에서 이동 중인 NPC의 위치는 경로 계산값이라 저장 파일에서 실제와 다를 수 있고, 로드 때 활동·파티 경로로 다시 계산한다(S13). [src: Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs, Assets/Scripts/Manager/SystemManager.cs, Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

**마을 상태 스냅샷(2026-10-05)**: `VillageManager`도 마을별 불변 DTO·변경·실패 목록을 소유하며, 같은 Priority 990과 저장 직전에 정리한다. 저장은 기존 목록 순서의 새 `VillageSaveRoot`에 캐시 DTO를 넣어 전체 마을 깊은 복사를 우회한다. Load 마이그레이션 뒤 초기 캐시를 만들고 Reset/Load에서 수명을 정리한다. ECS task/컴포넌트 되쓰기와 materialize/fold-back, 상점·자원·추상 시뮬 변경 경계를 표시한다. NPC 단계와 같은 재생성·큐·실패 중단 정책을 공유하고, 최초/이관의 런타임 미캡처 경로와 저장 범위·파일 형식은 유지한다. [src: Assets/Scripts/Village/VillageManager_SaveSnapshot.cs, Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Village/VillageManager_NpcTrade.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

**S-3 시뮬 스냅샷(2026-10-05)**: NpcSocial·NpcParties·NpcMeta·WorldSim도 상시 스냅샷을 사용한다. `NpcManager`는 호감도 쌍·From NPC별 유형 관계 목록·파티·인구 메타를, `HistoryManager`는 정착지 인덱스별 상태를 소유한다. 각 캐시는 인스턴스 필드인 `SaveSnapshotCache<TKey>`로 변경·실패 목록과 불변 DTO를 관리하고, Priority 990 및 저장 직전에 갱신한다. 저장은 기존 JSON 순서의 루트에 캐시 DTO를 넣어 깊은 복사를 우회하며, 전체 재생성·큐·실패 시 저장 중단·Reset/Load 정책을 공유한다. 스위치가 꺼진 경로와 `captureRuntimeManagers=false`는 기존 캡처를 유지한다. 상세 계약은 [SAVE_SNAPSHOT_DESIGN §12.1](SAVE_SNAPSHOT_DESIGN.md)에 기록한다. [src: Assets/Scripts/Data/SaveSnapshotCache.cs, Assets/Scripts/Manager/NpcManager_SimulationSaveSnapshot.cs, Assets/Scripts/History/HistoryManager_SaveSnapshot.cs, Assets/Scripts/Common/System/System_SaveSnapshotFlush.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

## 6. 원자 커밋과 복구

1. 변경 없는 파티션 참조는 그대로 유지하고, 변경 파티션만 고유 파일의 `.tmp`에 기록한다.
2. 기록이 끝난 파일을 최종 이름으로 이동한다.
3. 길이와 SHA-256을 manifest에 기록한다.
4. 새 manifest를 작성한다.
5. `File.Replace`로 active manifest를 교체하고 이전 active를 `manifest.prev.json`으로 남긴다.

manifest 교체 전 종료되면 기존 manifest가 기존 파일을 계속 가리킨다. 교체 후에는 새
manifest가 이미 완성된 파일만 가리킨다. active manifest나 파티션 검증이 실패하면 이전
manifest 전체로 폴백하며, 개별 NPC 샤드만 버리고 진행하지 않는다.

성공한 저장 뒤 active/previous manifest가 참조하지 않는 옛 파티션만 정리한다.

## 7. 로드와 ID 계약

파티션 검증·병합 후 NPC/건물 영구 EntityId를 모두 예약하고 플레이어 엔티티를 생성한다.
`ItemInstanceId`는 World 파티션의 다음 ID와 로드된 모든 아이템의 최대 ID+1 중 큰 값으로
복구한다. 테이블 참조는 기존 `OnLoadCompleted` 경로로 복원한다.

NPC 로드는 개인 기록 보정 → 호감도 쌍 복원·구 파티 기록 이관 → 최신 개인 사연 참조 재구성 순으로 처리한다. 최신 아이템/던전 완료/사별 인덱스와 동료 ID·과거 기억 인덱스는 로드 때 복구하며 영구 사건 ID로 취급하지 않는다. 따라서 목록을 압축한 구 세이브도 대화에서는 최신 슬롯을 직접 읽을 수 있다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs]

## 8. 레거시 마이그레이션

manifest가 없고 `WorldData.json`/`PlayerData.json`이 있으면 기존 버전 마이그레이션을 먼저
수행한 뒤 로딩 화면에서 전체 파티션 revision 1을 작성한다. manifest 커밋이 성공해도 기존
두 파일과 backup은 자동 삭제하지 않아 수동 복구 경로를 보존한다.

**버전 19의 NPC 기억 이관**: 구 `PartyAffinityChanged` 양쪽 사본을 한 쌍의 통계로 옮기며 현재 호감도는 보존한다. 구 기록에 없는 사냥/던전 횟수는 추정하지 않고 미집계 표시를 남긴다. 구 절도 피해 기록은 최신 1건으로 압축하고 중요 Deeds·사망 관계 기억은 보존한다. 기존 장비 교체(16 → 17)도 별도의 이전 마이그레이션으로 유지한다. [src: Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs]

기존 파티션 세이브에서 기억 이관이 발생하면 다음 저장을 전체 저장으로 승격해 State·Social·World 버전을 함께 커밋한다. 로드 단계에서 기존 파티션 파일을 바로 덮어쓰지 않는다. `PersonalStories`가 null이면 생략하고, 빈 과거 동료 기억 목록과 기본값 필드도 생략한다. 아이템·던전 완료·사별 참조 인덱스는 로드 때 항상 다시 만들므로 저장하지 않는다. 실제 백그라운드 저장 복사도 JSON 기본값 생략 설정을 따른다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Data/SaveDtoSnapshot.cs]

**버전 21의 narrative 파트(2026-10-10)**: 21 미만 세이브는 narrative 파트가 없어도 정상이며 빈 원장으로 연다. 버전 이관이 일어났으므로 첫 저장은 `All`로 승격해 파트를 처음 쓴다. 21 이상인데 파트가 없거나, 읽을 수 없거나, 원장 정합성 검사(`NarrativeSaveValidation`)를 통과하지 못하면 로드 실패로 처리한다. dungeons 파트의 15 이상 검사와 같은 방식이다. 구 던전 시나리오 필드가 남은 개발 세이브는 그 필드만 무시하고 연다. [src: Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Npc/Narrative/NarrativeSaveValidation.cs, Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md §7.7]

**버전 22 — exploration 파트 도입 뒤 제거(2026-10-10)**: 시야 탐험 기억용 `exploration` 파트와 던전 기록의 `ExploredBlocks`·`ExploredGameTime`을 더해 22로 올렸다가 같은 날 제거했다. 버전은 22를 유지하고 데이터 이관은 없다. 22 세이브에 남은 `exploration` 파트는 읽지 않으며, 다음 저장의 manifest로 옮기지 않는다(`OBSOLETE_PARTS`). 파일은 이전 manifest도 가리키지 않게 된 뒤 정리 단계가 지운다. 던전 기록의 옛 칸은 JSON 기본 설정(모르는 필드 무시)으로 읽히고 다음 던전 저장에서 빠진다. `SaveScope`의 `1 << 13`은 다시 쓰지 않는다. [src: Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Data/SavePartitionData.cs, Docs/map/FOG_OF_WAR_DESIGN.md §6]

**ore_veins 파트(2026-10-31)**: 버전을 올리지 않는다(22 유지). 파트가 없으면 버전과 상관없이 빈 기록으로 열고 청크가 켜질 때 새로 굴린다. 있는데 읽을 수 없거나 루트 버전이 1이 아니면 그 manifest를 거절한다(이전 manifest 폴백). 광맥 기록은 다시 굴리면 되는 데이터라 전체 저장 승격이 필요 없다. 로드 뒤 `OreVeinManager.Initialize`가 잘못된 기록·같은 청크 중복을 버린다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/OreVein/OreVeinManager.cs]

## 9. 검증 현황과 후속 검증

2026-10-10 exploration 파트 제거: `ARPG/Dungeon/Run Storage and Generation Diagnostics` 전체 PASS — 파트 44개, 옛 v22 exploration 파트가 있는 manifest 로드 성공·다음 manifest에서 빠짐, 던전 기록의 옛 탐험 칸 무시, 저장 묶음 45/45. 실제 v22 세이브(리비전 214)를 편집기 플레이로 열고 저장(215)하자 manifest에서 exploration이 빠졌다(확인 뒤 백업으로 되돌림). 상세는 [FOG_OF_WAR_DESIGN §9](../map/FOG_OF_WAR_DESIGN.md). [src: Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs]

2026-10-10 narrative 파트: `ARPG/Dungeon/Run Storage and Generation Diagnostics`에서 저장 묶음 42/42(narrative 루트·부탁 칸·부상 던전 묶음 포함), 21 미만 빈 원장·21 이상 파트 누락/손상 거절, 부탁·완료·보고 저장 실패 주입 롤백이 PASS다. 실제 플레이 저장 확인은 남았다. 상세는 [NPC_NARRATIVE_CONTENT_DESIGN §8.1](../npc/NPC_NARRATIVE_CONTENT_DESIGN.md). [src: Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs, Assets/Scripts/Editor/DungeonRuntimeDiagnostics_Scenarios.cs, Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics_Grouped.cs]

2026-10-05 묶음·증분 개정: Unity 및 Runtime/Editor 빌드 오류 0(기존 경고 22/13개), Village 계약 17/17·Simulation 계약 28/28·실제 S-3 JSON/SHA-256/파일 참조 왕복 4/4 PASS. 여섯 범위 결합·파일 재사용·단일 샤드/마을 변경·삭제·저장 중 추가 변경/큐·네 실패 지점/재시도·Reset 취소·공유 파일을 포함한 previous 전체 읽기는 1,916개 검사 PASS다. 기존 DungeonRuntimeDiagnostics의 전체 저장·복구·스케줄링 진단도 PASS. 1일 복제 시뮬은 NPC·마을·S-3 누락 0·에러 0·라이브 해시 동일이며, 30일 및 실제 플레이 통합 비용 실측은 남긴다. 조건·표본·한계는 [SAVE_SNAPSHOT_DESIGN §13.2](SAVE_SNAPSHOT_DESIGN.md)를 따른다. [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs]

2026-10-04 기억 저장 개정에서 Runtime/Editor 빌드 오류 0을 확인했다. Editor 진단 메뉴 `ARPG/Npc/Diagnostics/Ambition Sim/Validate Shared Activities`는 27 PASS, `Validate Personal Stories`는 30 PASS였다. 저장 복사/로드, 이관 반복, 활성·사망 쌍 기억과 최신 참조를 검사한다. 실제 세이브 읽기 전용 복제 실측의 범위와 수치는 [NPC_AMBITION_DESIGN §12.2](../npc/NPC_AMBITION_DESIGN.md)에 기록했다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics_SharedActivities.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics_PersonalStories.cs]

2026-10-04 NPC 상태 스냅샷 추가 검증: Runtime/Editor 컴파일 오류 0, Shared Activities 28 PASS / Personal Stories 32 PASS. 실제 NPC 2,406명의 기존 캡처와 캐시 조립은 32샤드 JSON이 동일했다. 30일 복제 시뮬레이션은 누락 0·라이브 해시 동일·기존 주요 지표 PASS, 던전 전환·완료·결투·동행·거래·청크 전환·전투 사망·수동 저장 후에도 누락 0이었다. 초기 로드 보정을 마치고 게임 시간을 멈춘 실제 저장·재접속·재저장에서 NPC 샤드 32개 SHA-256이 동일했다. 결투 종료 부분 저장의 NPC 정리/조립은 0.501/0.853ms, 전체 재생성은 약 129~132ms였다. 프레임 정리는 재접속 후 210회 평균 1.089ms·최대 3.857ms로 1ms 미만 목표가 일부 구간에서 미달했다. 상세 조건과 초기 구간 측정은 [SAVE_SNAPSHOT_DESIGN §7.1](SAVE_SNAPSHOT_DESIGN.md)에 기록한다. 같은 날 리뷰 후속으로, 30일 진단이 매 tick 전원을 다시 복사해 누락을 가리던 문제를 고쳐 다시 검증했고(표시 누락 0), 방문만으로는 표시하지 않고 이동 중 위치도 저장하지 않도록 바꿔 프레임 정리가 약 70초 표본 평균 0.288ms가 됐다(§7.2). [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

2026-10-05 마을 스냅샷 검증·성능 기록: [SAVE_SNAPSHOT_DESIGN §11.3](SAVE_SNAPSHOT_DESIGN.md). 기존 Save 진단과 30일 복제 진단에 마을 경로 동등성·표시 누락·실패 격리·라이브 캐시 복원 검사를 추가했다. 완료 결과는 계약 17/17·실제 마을 JSON 동일·1일 복제 시뮬 누락 0이며, 30일 완료 결과는 없다. 변경 프레임 마을 정리 평균 2.306ms·최대 7.397ms로 평균 1ms 미만 목표도 남아 있다. [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs]

2026-10-05 S-3 스냅샷 검증: Unity 및 Runtime/Editor 빌드 오류 0(기존 경고 22/13개), Shared Activities 28 PASS / Personal Stories 32 PASS, 격리 계약 28/28 PASS. NPC 2,406명·마을 300개·파티 339개·정착지 상태 338개의 GameScene에서 S-3 네 파티션의 기존/캐시 JSON이 4/4 동일했다. 1일 복제 시뮬은 NPC·마을·S-3 누락 0·에러 0·라이브 해시 동일이다. 같은 프로젝트 Temp의 별도 DataManager로 실제 전체 저장(revision 1)·부분 저장(revision 2) 후 manifest reader를 사용해 네 루트 JSON·SHA-256 동등성과 이전 DTO 불변을 확인했다(4/4 PASS). 사용자 저장 파일 88개는 덮어쓰지 않고 해시 동일을 확인했으며 작업용 파일은 정리했다. 기존 깊은 복사 36.1~56.8ms에 비해 S-3 캐시 정리·조립은 1.1~1.7ms였다(JSON·파일 I/O·전체 재생성 제외). 상세 조건·실제 저장 타이밍은 [SAVE_SNAPSHOT_DESIGN §12.3](SAVE_SNAPSHOT_DESIGN.md)에 기록한다. [src: Assets/Scripts/Editor/NpcSaveSnapshotDiagnostics.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

위 묶음·증분 및 기존 던전 진단은 격리 원장의 실제 writer/reader·장애 주입 결과다. 아래 실제 플레이의 저장·복구·Profiler 검증과 구분한다. S-2·S-3 통합 30일 시뮬과 일부 실제 던전/청크/건설 경로는 미완료다([SAVE_SNAPSHOT_DESIGN §12.4](SAVE_SNAPSHOT_DESIGN.md)).

- 레거시 → 파티션 → 재로드 왕복 후 NPC/마을/관계/장비·Mod 수 동일성
- 아이템 획득 시 Player/World 이외 파티션 revision 불변
- 상점 구매 시 Player/World + 시뮬 여섯 파트 함께 확정, 변경된 파일만 생성
- manifest 교체 전·후 강제 종료 복구
- active 파티션 손상 시 previous manifest 전체 폴백
- NPC 사망·파티 Hunt·마을 materialize 직후 전체 체크포인트 왕복
- Unity Profiler로 아이템 획득 프레임과 전체 체크포인트 프레임 실측
