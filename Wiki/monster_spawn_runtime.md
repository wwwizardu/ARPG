---
name: monster-spawn-runtime
description: 청크 난이도 기반 스폰 슬롯, 플레이어 거리 기반 생성, 슬롯 쿨타임, Active/Warm/Expired 청크 캐시의 구현 계약.
type: topic
status: verified
last_updated: 2026-09-03
sources:
  - Docs/ai_monster/MONSTER_SPAWN_DESIGN.md
  - Docs/map/MAP_DESIGN.md
  - Assets/Scripts/Common/System/System_MonsterSpawn.cs
  - Assets/Scripts/Manager/MonsterManager.cs
  - Assets/Scripts/Manager/MapManager.cs
  - Assets/Scripts/Map/MapManager_Generator.cs
  - Assets/Scripts/Map/MapManager_Spawner.cs
  - Assets/Scripts/Map/MapChunkData.cs
  - Assets/Scripts/Factory/EntityFactory.cs
  - Assets/Scripts/Common/System/System_EntityActivation.cs
  - Assets/Scripts/Common/System/System_EntityDestroy.cs
---

# 몬스터 런타임 스폰과 Warm 청크 캐시

> **상태: 구현 및 대조 완료.** 맵의 지형·biome·Zone 백본은 기존 결정론 계약을 유지하지만,
> **몬스터 후보는 예외로서 청크 세대 생성 시 런타임 랜덤으로 정하고 Active/Warm 동안만 보존한다.**
> Expired 후 재진입하면 새 후보를 뽑는다.
> [src: Assets/Scripts/Map/MapManager_Generator.cs, Assets/Scripts/Map/MapChunkData.cs]
> 기존
> [`MONSTER_SPAWN_DESIGN.md`](../Docs/ai_monster/MONSTER_SPAWN_DESIGN.md)의 종 선택·가중치·
> 엘리트/보스 설계는 유지하고, 여기서는 **언제·어디서 생성하고 청크 이탈 후 상태를 어떻게
> 보존하는지**만 다룬다.

## 1. 결정 요약

현재 구현의 기준은 아래와 같다.

1. 맵 렌더 반경과 몬스터 스폰 반경을 분리한다.
2. 몬스터는 활성 청크 전체가 아니라 플레이어와의 **월드 거리 링** 안에서만 생성한다.
3. 스폰 후보는 청크 최초 생성 시 만든다. 기존 `ZoneTable`의 Main/Sub 그룹 수 범위를 슬롯 수 범위로
   재사용하고, 청크 대표 biome의 밀도 배율을 적용해 최종 슬롯 수를 결정한다.
   플레이어 주변의 현재 개체 수는 후보 수나 밀도를 결정하지 않는다.
4. 생성된 후보 배열은 `Active`와 `Warm` 동안 그대로 보존한다.
5. 스폰 후보의 결정론적 재생성과 게임 세이브 영속화는 이번 범위에 포함하지 않는다.
6. 청크는 즉시 풀로 반환하지 않고 `Active → Warm → Expired` 생명주기를 갖는다.
7. 슬롯 배열은 한 청크 세대 동안 삭제·재정렬하지 않는다. 막힌 슬롯은 `Disabled` 처리한다.
8. 몬스터 또는 그룹이 죽으면 출신 슬롯에 재사용 쿨타임을 건다.
9. 슬롯별 코루틴 없이 중앙 시스템이 일정 주기로 거리·시간·점유 상태를 검사한다.
10. Warm 캐시는 TTL과 최대 개수 상한을 함께 사용한다.

## 2. 현재 구현

### 2.1 최초 스폰과 리스폰

`System_MonsterSpawn`은 0.5초마다 Active 청크의 빈 슬롯을 수집하고, 플레이어와 10~24 거리 링에
있는 후보를 미사용 우선·`LastUsedTime` 오름차순·랜덤 동률 순으로 처리한다. 한 검사에서는 최대
1개 그룹과 12개 엔티티만 예약한다. [src: Assets/Scripts/Common/System/System_MonsterSpawn.cs,
Assets/Scripts/Manager/MonsterManager.cs]

그룹은 고정 Main/Sub 슬롯을 앵커로 사용하고 반경 오프셋에 개체를 배치한다. 종은 청크 대표
biome의 `Any ∪ biome 전용` 풀에서 그룹마다 균등 샘플링한다.
[src: Assets/Scripts/Manager/MonsterManager.cs, Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs]

### 2.2 청크와 후보 위치

`GenerateChunkData`는 맵 파일·타일 변경 오버레이까지 끝난 최종 타일에서 Hill/Blocked/Object가 없는
셀을 모은다. `ZoneTable`의 Main/Sub 그룹 수 범위에 대표 biome 배율을 한 번 적용한 뒤, 중복과
앵커 최소 간격을 검사해 별도 슬롯 배열을 만든다. `MonsterSpawn` 타일 비트는 후보 정본으로 쓰지
않는다. [src: Assets/Scripts/Map/MapManager_Generator.cs, Assets/Scripts/Map/Biome/BiomeTable.cs]

먼 청크는 Warm으로 전환되어 타일맵에서만 제거되고, 슬롯 배열과 세대 상태를 보존한다. TTL 300초
또는 Warm 20개 상한의 LRU 축출 때 Expired가 되어 슬롯을 버리고 청크 풀로 반환된다.
[src: Assets/Scripts/Map/MapChunkData.cs, Assets/Scripts/Map/MapManager_Generator.cs]

### 2.3 몬스터 생명주기

청크 이탈 시 몬스터 GameObject와 활성화 컴포넌트만 비활성화하고 EntityId, 출신 슬롯, 생존 수와
쿨다운은 유지한다. Expired 때 ECS·렌더·애니메이션·GameObject와 양방향 추적을 함께 제거한다.
[src: Assets/Scripts/Manager/MonsterManager.cs, Assets/Scripts/Common/System/System_EntityActivation.cs]

## 3. 목표 생명주기

```text
Active
  렌더링됨
  거리 기반 스폰 검사 가능
  몬스터 활성화 가능
    │ 플레이어가 렌더 범위를 벗어남
    ▼
Warm
  렌더링 제거
  MapChunkData·후보·슬롯·몬스터 상태 유지
  새 스폰 검사 중지
  몬스터 GameObject 비활성화
    ├─ TTL 안에 재진입 ───────────────→ Active
    └─ TTL 만료 또는 캐시 상한 축출
        ▼
Expired
  몬스터와 ECS 추적 완전 정리
  슬롯 상태 제거
  MapChunkData를 풀에 반환
```

Warm에서 Active로 돌아올 때는 지형과 후보를 재생성하지 않는다. 기존 몬스터, 점유 슬롯,
쿨타임을 그대로 사용한다. Expired 이후 재진입할 때만 새 청크 세대와 새 랜덤 후보를 만든다.

## 4. 스폰 후보 정책

### 4.1 청크 난이도 기반 생성과 보존

후보 위치는 새 청크 세대가 생성될 때 한 번 선택한다. 별도 슬롯 수 컬럼은 추가하지 않고 기존
`ZoneTable.MainGroupCountMin/Max`와 `SubGroupCountMin/Max`를 각각 Main/Sub 슬롯 수 범위로
재사용한다. 청크 생성 시 각 범위에서 기본 슬롯 수를 한 번 뽑고, 청크 대표 biome의
`BiomeTable.GetMonsterSpawnMultiplier`를 각 슬롯 수에 적용한다. 슬롯 종류는 청크 세대 동안
고정한다. [src: Assets/Scripts/Common/Tables.cs, Assets/Scripts/Map/Biome/BiomeTable.cs]

biome 배율 적용 시 정수 손실 편향을 피하기 위해 확률적 반올림을 사용한다. 예를 들어 계산값이
`1.3`이면 1개에 30% 확률로 1개를 더한다. 최종 Main/Sub 슬롯 수는 각각 0 이상 해당 Zone의
`MainGroupCountMax`/`SubGroupCountMax` 이하로 제한한다. 따라서 biome은 Zone이 허용한 상한 안에서
밀도를 낮추거나 높인다. 슬롯 수 계산에는 청크 중심 cluster의 대표 biome인 `chunkBiome`을 사용하고,
유효 셀 선택 단계에서 biome 배율을 다시 적용하지 않는다.
[src: Assets/Scripts/Map/MapChunkData.cs, Assets/Scripts/Manager/MonsterManager.cs]

현재 `ZoneTable` 데이터는 Zone 3 이상 대부분이 같은 Sub 그룹 수 범위를 사용하므로, 고난이도
청크에서 실제 슬롯을 늘리려면 테이블 원본의 Main/Sub 그룹 수 범위를 별도로 조정해야 한다.
[src: Assets/_BinaryData/TableData/ZoneTable.bytes]

개념적인 생성 순서는 다음과 같다.

```text
1. 맵·청사진·타일 변경 오버레이가 끝난 뒤 유효 셀 수집
2. ZoneTable의 Main/Sub 그룹 수 범위에서 기본 슬롯 수 추출
3. 청크 대표 biome 배율 적용 후 확률적 반올림과 Zone 최대값 클램프
4. 유효 셀에서 중복 없이 Main/Sub 슬롯을 선택하고 최소 간격·청크 경계 검사
5. 배치 제약 때문에 목표 수를 채우지 못하면 실제 배치된 수만 채택
```

슬롯은 그룹 앵커이므로 최종 몬스터 밀도는 `슬롯 수 × 그룹 크기`의 영향을 받는다. 난이도에 따라
두 값을 동시에 올릴 경우 밀도가 중복 증가하므로 각각 독립적으로 튜닝한다. 슬롯의 Main/Sub 종류는
청크 생성 시 확정하며, 실제 생성·리스폰 때 해당 종류의 `MainGroupSizeMin/Max` 또는
`SubGroupSizeMin/Max` 범위에서 그룹 크기를 뽑는다. 몬스터 종도 실제 생성 시 biome 풀에서 뽑는다.
Active/Warm 동안 후보 배열을 그대로 보존하므로 재진입 일관성을 위해 결정론적 재생성이 필요하지
않다. [src: Assets/Scripts/Common/Tables.cs, Assets/Scripts/Manager/MonsterManager.cs]

```text
최초 생성: 청크 난이도에 맞는 수의 랜덤 후보 배열 생성
Active/Warm: 동일 배열 재사용
Expired: 후보 배열 폐기
다음 생성: 당시 청크 난이도에 맞춰 새로운 랜덤 후보 배열 생성
```

이 정책은 `MAP_DESIGN.md`의 “같은 좌표는 같은 타일” 원칙과 분리한다. Ground/Hill/Object 같은 맵
백본은 계속 결정론적으로 생성하되, 몬스터 후보는 타일 정본이 아니라 **일시적인 생태 런타임
상태**로 취급한다. 구현에서는 랜덤 후보를 `tiles`의 영구 의미로 사용하지 않고 최종 타일의
Hill/Blocked/Object 상태를 검사해 별도 후보 배열을 만든다. [src: Docs/map/MAP_DESIGN.md §1.1·§2.3·§2.4]

### 4.2 슬롯 식별

한 청크 세대 동안 후보 배열은 불변이다. 따라서 런타임 슬롯은 `slotIndex`로 식별할 수 있다.

```text
ChunkCoord + GenerationId + SlotIndex
```

- `SlotIndex`: 후보 배열 위치.
- `GenerationId`: 풀 객체가 Expired 후 다른 청크나 새 세대로 재사용될 때 이전 비동기 결과를 거부.
- 슬롯이 막히면 배열에서 삭제하지 않고 `IsDisabled = true`로 둔다.
- 청크 세대가 Expired될 때만 배열 전체를 폐기한다.

64비트 ID 패킹이나 월드 좌표 세이브는 이번 범위에 필요하지 않다. 향후 게임 저장 후에도 같은
생태 배치를 복원해야 한다면 후보 배열 직렬화 또는 결정론적 재생성 중 하나를 별도 결정한다.

### 4.3 실제 위치의 다양성

후보는 실제 개체 위치가 아니라 그룹 앵커 풀이다. 거리·점유·쿨타임 조건을 통과한 빈 슬롯은
생성 대상이 되며, 회당 생성 예산에 따라 여러 검사 주기에 걸쳐 점진적으로 처리한다. 그룹 개체는
앵커 주변에 랜덤 오프셋으로 배치하고 기존 몬스터와 최소 거리를 확보해 고정 스폰처럼 보이지 않게
한다. 플레이어 주변 목표 개체 수로 일부 슬롯을 제외하지 않는다.

최종 오프셋 위치는 Hill, Blocked, Object, 청크 경계, 다른 개체 점유를 다시 검사한다. 몬스터 간
최소 거리는 두 몬스터의 `MoveRadius` 합으로 계산한다.
[src: Assets/Scripts/Manager/MonsterManager.cs, Assets/Scripts/Manager/MapManager.cs,
Assets/Scripts/Factory/EntityFactory.cs]

## 5. 거리 기반 스폰 디렉터

맵 렌더 청크는 기존 반경으로 유지하되, 스폰 디렉터는 후보의 월드 좌표와 플레이어 위치를
`sqrMagnitude`로 비교한다.

직렬화 대상 설정:

- 최소 스폰 거리: 눈앞 팝인 방지.
- 최대 스폰 거리: 불필요한 원거리 생성 방지.
- 활성화·비활성화 거리: 시각/동작 히스테리시스.
- 검사 주기.
- 회당 최대 생성 그룹/개체 수.
- 슬롯 재사용 쿨타임 최소/최대.

스폰 조건은 모두 만족해야 한다.

```text
Active 청크
AND 최소 거리² <= 후보 거리² <= 최대 거리²
AND 슬롯 Enabled
AND 슬롯 미점유
AND Pending 아님
AND 현재 시간 >= NextSpawnTime
AND 지형·점유·그룹 간격 검사 통과
```

`hasSpawned`와 `originalSpawnCount - aliveCount`는 청크 전체 최초 스폰 모델이므로 제거하거나 호환
래퍼로 축소한다. 새 밀도 기준은 청크 생성 시 확정된 슬롯 수이고, 런타임 생성·보충 기준은 거리 링,
슬롯 점유, 쿨타임과 회당 생성 예산이다. 최대 개체 수는 청크당 최대 슬롯 수, 슬롯당 최대 그룹 크기,
Active 청크 수와 최대 Warm 청크 수의 조합으로 구조적으로 제한한다.
[src: Assets/Scripts/Common/System/System_MonsterSpawn.cs, Assets/Scripts/Map/MapChunkData.cs]

거리 링은 **새 생성 여부만** 결정한다. 이미 생성된 그룹이 링 밖으로 이동해도 파괴하거나 슬롯 점유를
해제하지 않는다. `System_EntityActivation`은 별도의 활성화·비활성화 거리로 GameObject 표시와 동작을
전환할 수 있지만, 이 경우에도 엔티티와 출신 슬롯 점유는 유지한다. 출신 청크가 Warm이 될 때
비활성화하고, Expired될 때만 완전히 제거한다.
[src: Assets/Scripts/Common/System/System_EntityActivation.cs, Assets/Scripts/Manager/MonsterManager.cs]

## 6. 슬롯과 그룹 상태

구현 모델:

```csharp
public sealed class MonsterSpawnSlotState
{
    public Vector2Int LocalPosition;
    public MonsterSpawnGroupKind GroupKind;
    public float NextSpawnTime;
    public float LastUsedTime;
    public bool IsDisabled;
    public bool IsPending;
    public int PendingVersion;
    public int AliveMemberCount;
}

public sealed class ChunkSpawnRuntimeData
{
    public int GenerationId;
    public float LastActiveTime;
    public MonsterSpawnSlotState[] Slots;
}
```

`MonsterSpawnGroupKind`는 `Main`과 `Sub`를 구분한다. 슬롯 종류는 청크 세대 동안 바뀌지 않지만,
해당 슬롯에서 생성되는 그룹 크기와 몬스터 종은 생성할 때마다 대응하는 Zone 범위와 biome 풀에서
다시 샘플링한다.

지형과 함께 Warm/Expired되는 후보·슬롯은 `MapChunkData`의 청크 런타임 데이터가 소유하고, 실제
EntityId 등록·제거와 출신 슬롯 역조회는 `MonsterManager`가 소유한다. `PendingVersion`은 Warm 취소
뒤 같은 슬롯에 새 예약이 생겨도 이전 비동기 완료를 구분한다.
[src: Assets/Scripts/Map/MapChunkData.cs, Assets/Scripts/Manager/MonsterManager.cs]

### 6.1 생성 예약

Addressables 생성을 시작하기 전에 `IsPending = true`로 예약한다. 완료 시 다음을 재검사한다.

- 같은 청크 좌표와 `GenerationId`인가.
- 청크가 여전히 Active인가.
- 슬롯이 Enabled이며 Pending 예약이 유효한가.
- 후보가 여전히 거리·지형 조건을 만족하는가.

실패하거나 세대가 바뀌면 예약을 해제하고 생성 결과를 안전하게 정리한다.

### 6.2 사망 쿨타임

몬스터 또는 그룹은 출신 슬롯을 가진다. 그룹 단위 스폰의 기본 정책은 **그룹 전멸 시 앵커 슬롯
쿨타임 시작**이다.

```text
그룹 생성 성공
  → AliveMemberCount 설정

구성원 사망
  → AliveMemberCount 감소

AliveMemberCount == 0
  → 슬롯 점유 해제
  → NextSpawnTime = now + Random(cooldownMin, cooldownMax)
  → LastUsedTime = now
```

Warm 전환은 사망이 아니므로 슬롯 점유와 생존 수를 유지한다. 쿨타임은 절대 시간으로 계속 흐르며,
Warm 중 만료되어도 새 스폰은 하지 않는다. Active 복귀 후 거리·슬롯 조건을 만족할 때만 다시
생성한다.

슬롯마다 Coroutine이나 `UniTask.Delay`를 만들지 않는다. `System_MonsterSpawn`이 일정 주기로 시간
값만 비교한다.

## 7. Warm 캐시 상한

TTL만 사용하면 빠르게 이동할 때 Warm 청크가 계속 쌓일 수 있으므로 다음 두 제한을 함께 둔다.

1. Warm TTL.
2. 최대 Warm 청크 수.

상한을 넘으면 가장 오래 접근하지 않은 Warm 청크부터 Expired 처리한다. 청크별 타이머 대신 중앙
관리자가 재사용 캐시로 일정 주기마다 검사한다. 맵은 Active/Warm 보존 컬렉션과 렌더 중인 Active
청크 컬렉션을 분리하고 상태 필드로 Expired를 구분한다.
[src: Assets/Scripts/Map/MapManager_Generator.cs, Assets/Scripts/Manager/MapManager.cs]

Expired 처리는 다음 순서를 보장한다.

1. 새 비동기 생성 예약 차단과 세대 무효화.
2. 몬스터 GameObject·ECS·역조회 제거.
3. 슬롯과 후보 상태 제거.
4. 렌더 상태가 없음을 확인.
5. `MapChunkData`를 풀에 반환.

## 8. 활성화와 제거 규칙

- 새 몬스터 생성 직후 실제 거리와 `ActivationDistanceComponent.IsActivated`, GameObject 활성 상태를
  일치시킨다. [src: Assets/Scripts/Manager/MonsterManager.cs,
  Assets/Scripts/Common/System/System_EntityActivation.cs]
- Active → Warm에서는 몬스터를 파괴하지 않고 비활성화한다.
- Warm → Active에서는 실제 거리 조건을 만족하는 기존 몬스터만 활성화한다.
- Warm → Expired에서는 몬스터와 슬롯 연결을 완전히 제거한다.
- 사망·제거된 EntityId는 전역 몬스터 목록뿐 아니라 청크/그룹/슬롯 역조회에서도 즉시 제거한다.
  [src: Assets/Scripts/Manager/MonsterManager.cs, Assets/Scripts/Common/System/System_EntityDestroy.cs]
- 활성 청크는 TTL 만료 대상에서 제외한다.
- 거리 링 이탈만으로 몬스터를 제거하거나 슬롯을 비우지 않는다. 첫 구현에서는 전투·추적 중인
  몬스터를 위해 출신 청크의 Warm 전환을 지연하는 별도 예외를 두지 않는다.

## 9. 책임 경계

| 책임 | 소유자 |
|---|---|
| 타일·biome·Zone 난이도와 슬롯 수·후보 생성 | `MapManager` / `MapChunkData` |
| Active/Warm/Expired 및 LRU 캐시 | `MapManager` |
| 거리 링·회당 생성 예산 | `System_MonsterSpawn` |
| 슬롯 점유와 EntityId 연결 | `MonsterManager` + 청크 런타임 데이터 |
| 몬스터 ECS 조립 | `EntityFactory` |
| 사망 시 슬롯 통지 | 사망/제거 시스템 → `MonsterManager` |
| GameObject 거리 활성화 | `System_EntityActivation` |
| 종 선택 | 기존 `BiomeMonsterMapping`, 향후 SpawnPool |

`MONSTER_SPAWN_DESIGN.md`의 SpawnPool/가중치/Archetype은 “어떤 종을 뽑는가”를 담당하고 이
문서의 슬롯/거리/청크 수명은 “언제 어디에 존재시키는가”를 담당한다.
[src: Docs/ai_monster/MONSTER_SPAWN_DESIGN.md]

## 10. 적용 순서

아래 순서로 구현하고 마지막에 계약 전체를 통합 검증한다.

1. 청크 상태와 Warm 캐시를 도입해 재진입/TTL/LRU를 검증한다.
2. 기존 ZoneTable의 Main/Sub 그룹 수를 슬롯 수로 변환하고 대표 biome 배율을 적용한다. Main/Sub
   종류가 고정된 랜덤 후보 배열을 추가해 청크 세대 동안 보존하고, `GenerationId`와 불변 슬롯 배열
   규칙도 함께 적용한다.
3. 스폰 판정을 거리 링 + 슬롯 상태 + 회당 생성 예산으로 교체한다.
4. 슬롯 Pending과 비동기 완료 재검사를 적용한다.
5. EntityId ↔ 슬롯 연결과 그룹 전멸 쿨타임을 적용한다.
6. 지형·점유·최종 오프셋 검증과 최근 사용 후보 분산을 적용한다.
7. 기존 `hasSpawned`/`originalSpawnCount`/60초 일괄 리스폰 경로를 제거한다.
8. 활성화 초기 상태, TTL 활성 청크 제외, 죽은 ID 정리를 통합 검증한다.

## 11. 검증 계약

- 최대 스폰 거리 밖에서는 몬스터 프리팹을 만들지 않는다.
- 최소 스폰 거리 안에서는 눈앞 스폰이 발생하지 않는다.
- 같은 지형 조건에서 Zone/난이도별 설정에 맞는 슬롯 수가 청크 생성 시 확정된다.
- Main/Sub 슬롯 수가 기존 ZoneTable 그룹 수 범위와 대표 biome 배율을 따르며 각 Zone 최대값을
  초과하지 않는다.
- 플레이어 주변 현재 개체 수가 청크 슬롯 수나 정상 생성 여부를 바꾸지 않는다.
- 플레이어 이동 시 새 거리 링에 들어온 빈 슬롯을 회당 생성 예산 안에서 점진적으로 처리한다.
- Active → Warm → Active에서는 동일 후보·슬롯·몬스터 상태를 복원한다.
- Warm → Expired 후 재진입하면 새 랜덤 후보를 생성하며 이전 세대 상태가 섞이지 않는다.
- Warm 중에는 새 몬스터를 생성하지 않는다.
- 사망한 그룹의 슬롯은 쿨타임 전까지 재사용하지 않는다.
- 쿨타임 만료 후에도 거리·슬롯 조건을 충족해야 생성한다.
- 생성된 그룹이 거리 링 밖으로 나가도 제거되거나 슬롯 점유가 해제되지 않는다.
- 거리 비활성화로 GameObject가 꺼져도 엔티티와 슬롯 점유는 유지된다.
- 슬롯 배열은 한 세대 동안 삭제·재정렬되지 않는다.
- 비동기 생성 도중 Warm/Expired 전환 또는 세대 교체가 일어나도 잔여 엔티티가 남지 않는다.
- 기존 몬스터, 그룹, 다른 예약 위치와 겹치지 않는다.
- 그룹 오프셋은 Hill/Blocked/Object/청크 경계를 침범하지 않는다.
- 활성 청크가 TTL로 제거되지 않는다.
- Warm 캐시는 TTL과 최대 개수 상한을 지킨다.
- Zone 레벨과 biome 종 선택 규칙은 유지한다.
- 슬롯별 Update/Coroutine/Delay를 만들지 않고 중앙 주기 검사와 재사용 버퍼를 사용한다.
- 별도 Unity 프로젝트를 만들지 않고 현재 프로젝트의 진단 또는 정적 검증 경로를 사용한다.

## 12. 확정 튜닝값

초기 런타임 값은 다음과 같이 확정했다.

- 최소/최대 스폰 거리: 10 / 24.
- 활성화/비활성화 거리: 25 / 30.
- 검사 주기: 0.5초.
- Warm TTL과 최대 Warm 청크 수: 300초 / 20개.
- 회당 최대 그룹/개체 수: 1 / 12.
- 사망 쿨타임 범위: 45~75초.
- 슬롯 선택: 미사용 우선, 이후 `LastUsedTime`이 오래된 순, 동률은 랜덤.
- 개체 겹침 최소 거리: 두 몬스터의 `MoveRadius` 합.
- ZoneTable의 Main/Sub 그룹 수 범위. 이 값이 슬롯 수 범위로도 사용된다.
- biome 슬롯 밀도 배율.
[src: Assets/Prefabs/Manager/MonsterManager.prefab,
Assets/Prefabs/Manager/MapManager.prefab,
Assets/Scripts/Common/System/System_MonsterSpawn.cs,
Assets/Scripts/Manager/MonsterManager.cs]

## 13. 관련 문서

- [`MONSTER_SPAWN_DESIGN.md`](../Docs/ai_monster/MONSTER_SPAWN_DESIGN.md) — 종 풀, 가중치,
  Zone/Archetype/보스 확장.
- [`MAP_DESIGN.md`](../Docs/map/MAP_DESIGN.md) — 맵 백본, 청크 풀, 타일 비트, 활성화 hook.
- [`current_status.md`](current_status.md) — 코드·테이블 기준 현재 구현 상태.
