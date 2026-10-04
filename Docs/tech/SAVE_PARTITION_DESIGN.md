# 세이브 파티션 설계

> **상태 (2026-08-22)**: 구현 완료, Unity 플레이 모드 왕복·장애 주입 검증 전.
> **기억 저장 개정 (2026-10-04)**: 현재 WorldDataVersion은 **19**. 공동 활동 통계·최신 개인 사연·과거 동료 기억을 저장하고, `NpcState`와 `NpcSocial`은 부분 저장에서도 같은 revision으로 커밋한다. 기억 진단은 통과했으며 전체 플레이 모드·장애 주입 검증은 여전히 후속이다(§9).
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
| Villages | `VillageData` 목록 |
| Buildings | `BuildingSaveData` 딕셔너리 |
| Chronicle | 정착지 연대기와 왕국 스냅샷 |
| WorldSim | 런타임 정착지 추상 상태 |
| NpcMeta | 목표 인원·보충 시각·순회 커서 |
| NpcSocial | NPC↔NPC 현재 호감도·유형 관계, 쌍당 공동 활동 통계(`NpcAffinityEntry.SharedActivities`) |
| NpcParties | 파티 리더·멤버·공통 활동 상태 |
| NpcState 00~31 | 개별 `NpcSaveData`: 중요 Deeds·사망 관계 기억·최신 사연 요약/참조(`PersonalStories`)·과거 동료 활동 기억(`SharedActivityMemories`) |

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
| 상점 구매·판매 | Player + World + Villages |
| NPC 상태 또는 NPC 사회 데이터 저장 | NpcState + NpcSocial을 함께 포함 |
| 거점·메뉴 수동 저장, 최초 월드 저장 | 전체 파티션 |

NPC 죽음처럼 NPC 상태·관계·파티·연대기가 함께 바뀌는 체크포인트는 manifest 한 revision에
묶인다. 향후 자동저장을 추가할 때도 변경된 샤드와 연관 파티션을 같은 revision으로 요청한다.

**현재 결합 계약**: 저장 스냅샷을 잡기 전에 `NpcState`·`NpcSocial` 중 하나라도 포함된 요청은 둘 다 포함하도록 확장한다. 두 데이터는 같은 메인 스레드 상태에서 복사되고 한 manifest revision에 커밋된다. `NpcParties`·`Chronicle`까지 자동으로 추가한다는 뜻은 아니다. 미저장 NPC 던전 완료가 있으면 `Dungeons`·`NpcState`·`NpcSocial` 중 하나라도 실린 요청에 기존 `Dungeons` + `NpcState` 결합을 먼저 적용하고, 그 뒤 `NpcSocial`까지 넓힌다. 그래서 어느 쪽에서 시작한 요청이든 세 파티션이 함께 저장된다. 이관으로 전체 저장이 필요한 상태면 요청 범위와 관계없이 `All`로 승격한다. [src: Assets/Scripts/Manager/DataManager_PartitionedSave.cs]

## 6. 원자 커밋과 복구

1. 변경 파티션을 고유 파일의 `.tmp`에 기록한다.
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

**현재 버전 19의 NPC 기억 이관**: 구 `PartyAffinityChanged` 양쪽 사본을 한 쌍의 통계로 옮기며 현재 호감도는 보존한다. 구 기록에 없는 사냥/던전 횟수는 추정하지 않고 미집계 표시를 남긴다. 구 절도 피해 기록은 최신 1건으로 압축하고 중요 Deeds·사망 관계 기억은 보존한다. 기존 장비 교체(16 → 17)도 별도의 이전 마이그레이션으로 유지한다. [src: Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs]

기존 파티션 세이브에서 기억 이관이 발생하면 다음 저장을 전체 저장으로 승격해 State·Social·World 버전을 함께 커밋한다. 로드 단계에서 기존 파티션 파일을 바로 덮어쓰지 않는다. `PersonalStories`가 null이면 생략하고, 빈 과거 동료 기억 목록과 기본값 필드도 생략한다. 아이템·던전 완료·사별 참조 인덱스는 로드 때 항상 다시 만들므로 저장하지 않는다. 실제 백그라운드 저장 복사도 JSON 기본값 생략 설정을 따른다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/DataManager.cs, Assets/Scripts/Manager/DataManager_PartitionedSave.cs, Assets/Scripts/Data/SaveDtoSnapshot.cs]

## 9. 검증 현황과 후속 검증

2026-10-04 기억 저장 개정에서 Runtime/Editor 빌드 오류 0을 확인했다. Editor 진단 메뉴 `ARPG/Npc/Diagnostics/Ambition Sim/Validate Shared Activities`는 27 PASS, `Validate Personal Stories`는 30 PASS였다. 저장 복사/로드, 이관 반복, 활성·사망 쌍 기억과 최신 참조를 검사한다. 실제 세이브 읽기 전용 복제 실측의 범위와 수치는 [NPC_AMBITION_DESIGN §12.2](../npc/NPC_AMBITION_DESIGN.md)에 기록했다. [src: Assets/Scripts/Editor/NpcAmbitionSimDiagnostics_SharedActivities.cs, Assets/Scripts/Editor/NpcAmbitionSimDiagnostics_PersonalStories.cs]

아래 전체 저장·복구·Profiler 검증은 위 기억 진단과 구분해 후속으로 남긴다.

- 레거시 → 파티션 → 재로드 왕복 후 NPC/마을/관계/장비·Mod 수 동일성
- 아이템 획득 시 Player/World 이외 파티션 revision 불변
- 상점 구매 시 Player/World/Villages만 변경
- manifest 교체 전·후 강제 종료 복구
- active 파티션 손상 시 previous manifest 전체 폴백
- NPC 사망·파티 Hunt·마을 materialize 직후 전체 체크포인트 왕복
- Unity Profiler로 아이템 획득 프레임과 전체 체크포인트 프레임 실측
