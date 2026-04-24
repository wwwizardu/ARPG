# Phase A — 자가 생성 최소 루프 (MVP) 상세 기획

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 전체 비전: [VILLAGE_EXPANSION_DESIGN.md](VILLAGE_EXPANSION_DESIGN.md)
>
> **목표**: "플레이어가 손대지 않아도 마을이 아주 느리게 스스로 돌아간다"를 **가장 작은 단위**로 증명한다.

---

## 1. Phase A 범위 (Scope)

### 1.1 Phase A가 하는 것
1. **시간 기반 패시브 자원 생산/소비** — NPC 수에 비례해 Food/Wood/Stone이 누적되고, 식량은 매 시간 소비된다.
2. **자원 저장 상한(Cap)** — 자원마다 기본 Cap 50. 초과분은 절삭되어 잉여 플래그가 켜진다 (Phase B에서 소비).
3. **첫 오브젝트(모닥불) 제작 루프** — 마을에는 처음에 **아무 오브젝트도 없다**. NPC가 자원을 모으고, 충분해지면 **Campfire를 1개 짓는다**. 이것이 Phase A가 증명할 자가 성장 루프의 최소 단위다.
4. **로그 기반 디버깅** — 상태 전환(착수/완료/환불/기아 등) 시 `Debug.Log` 1줄씩 남긴다. 화면 UI는 Phase A 범위 밖이며, 사용자가 이후에 직접 추가한다.

### 1.2 Phase A가 **하지 않는** 것 (Phase B 이상 이관)
- **범용** 오브젝트 배치 큐 (`ObjectPlacementTaskComponent`) / 다수 오브젝트 / 테이블 기반 레시피 — Phase B
  - Phase A는 **Campfire 단일 하드코딩**. 테이블도, 큐도 없음.
- NPC가 실제로 걸어가서 오브젝트를 제작하는 **물리적 이동·애니메이션** — Phase B
  - Phase A는 "마을 단위 추상 타이머"만. 누가 만드느냐는 "NPC가 하나라도 살아있으면 진행"으로 단순화.
- Tier 승격 시스템 (`System_VillageTierProgression`) — Phase C
- 벽/경계 자동 계획 (`System_VillageWallPlanner`) — Phase C
- 직업별 액티브 보너스, 오브젝트 세트 판정 — Phase D
- 배후 시뮬레이션 — Phase E

> **Phase A는 "시간이 흐른다 → 자원이 쌓인다 → NPC가 모닥불 하나를 짓는다"까지만 증명한다. 단, 그 루프가 **끝까지** 돌아가야 한다.**

---

## 2. 기존 시스템과의 차이

### 2.1 현재 상태 (2026-04-22 기준)

| 항목 | 현재 구현 | 위치 |
|------|-----------|------|
| 자원 저장소 | `VillageData.Resources : Dictionary<ItemType,int>` | [VillageData.cs](../Assets/Scripts/Village/VillageData.cs) |
| 패시브 생산 | `System_VillageResource` — 5초마다 Population × 1씩 Food/Wood/Stone 모두 동일 생산 | [System_VillageResource.cs](../Assets/Scripts/Common/System/System_VillageResource.cs) |
| 소비 | **없음** | — |
| Cap | **없음** (무한 누적) | — |
| 게임 시간 단위 | `AR.s.Time.CurrentGameTime` 사용 가능 | `TimeManager` |
| 초기 오브젝트 | `EnsureVillagePopulated`가 NPC만 스폰, 오브젝트는 배치 안 함 **(Phase A도 이 전제를 그대로 유지)** | [NpcManager.cs:186-228](../Assets/Scripts/Manager/NpcManager.cs#L186-L228) |
| 디버그 UI | 없음 | — |
| ObjectType enum | None/Stone/Npc/WoodWall 4종 | [GlobalEnum.cs:28-34](../Assets/Scripts/Common/GlobalEnum.cs#L28-L34) |

### 2.2 Phase A 이후 상태

| 항목 | 변경 |
|------|------|
| `System_VillageResource` | **삭제**. `System_VillagePassiveProduction`으로 대체 (게임시간 1h 인터벌, 소수 누적, 소비 포함) |
| `VillageData` | `ResourcesFloat`(소수 누적 버퍼) + `ResourceCaps`(Cap 오버라이드) 추가 |
| `VillageStorageComponent` | **신규 ECS 컴포넌트** — 현재 `VillageData.Resources`를 Village 엔티티에 붙이는 형태로 승격. 단, Phase A에서는 `VillageData`와 병존하며 읽기는 컴포넌트, 쓰기는 양쪽에 동기화 (Phase B에서 단일화) |
| `ObjectType` enum | **변경 없음**. 오브젝트 식별은 `BuildableItemTable.Id`로 처리 (enum은 `Npc` 특수 구분용으로만 유지) |
| `BuildableItemTable` | **`Campfire` 엔트리 1개 추가** (Name/HP/Size/`ResourceName`/Recipe/Function). Id는 시트에서 할당 (예: 100). `ResourceName`은 Campfire `CustomTile`의 Addressable 키 |
| `MapManager.PlaceObject` | 그대로 사용 (시그니처 변경 없음). `objectId` 인자에 `BuildableItemTable.Id`를 그대로 전달 |
| `BuildableTileRegistry` (신규) | **lazy 캐시**. `EnsureLoadedAsync(id)`로 호출자가 필요할 때만 Addressable 로드, `Get(id)`는 캐시 동기 조회. 프리로드 없음, 청크별 ref-count 없음, 영구 유지 (타일은 소형 공유 에셋) |
| **로드 책임은 호출자** | 오브젝트를 배치하는 쪽이 `await EnsureLoadedAsync(id)` **먼저** → 그다음 `PlaceObject`. 렌더러는 캐시 hit 보장 상태에서 동기 조회 |
| `MapManager_Renderer` | `ObjectSet[id]` 직접 인덱싱 → **`BuildableTileRegistry.Get(id)` 우선, 실패 시 `ObjectSet` fallback** (Npc/Stone/WoodWall 레거시). null이면 **Object 레이어가 공란** — Ground(지면) 타일은 정상 표시, 그 위 오브젝트만 안 보임 |
| `ThemeTileSet.ObjectSet` | **수정 불필요** (신규 배치 오브젝트는 Addressable 경로 사용) |
| `EnsureVillagePopulated` | **변경 없음**. 시드 오브젝트 배치 훅 **도입하지 않는다**. 오브젝트 생성은 별도 시스템이 자원·시간 조건을 보고 결정 |
| `System_VillageFirstBuild` (신규) | 마을별 "첫 오브젝트(Campfire)" 제작 루프 담당 |
| 디버그 UI | **Phase A 범위 밖**. 상태 전환은 `Debug.Log`로만 노출. 화면 UI는 사용자가 이후 추가 |

> ECS 승격을 Phase A에서 **완전 단일화하지 않는** 이유: `VillageData`는 이미 세이브/로드·MapManager 초기화·NpcManager·5+ 시스템에서 참조 중. Phase A에서 단일화하면 블래스트 반경이 커져 MVP 범위를 넘어간다. Phase A는 **컴포넌트를 병렬로 유지**해 Phase B가 단일화 시점을 고르도록 한다.

---

## 3. 자원 시스템 상세

> **모든 자원은 정수**. 소수 누적 버퍼 없음. 사용자가 보는 숫자와 내부 저장이 동일 → 직관적.

### 3.1 생산 공식 (정수)

```
NPC 1명당 게임시간 1시간 기준:
  Food  += 2
  Wood  += 1
  Stone : 5시간당 +1 (StoneTimer 누적)
```

- 매 틱마다 `deltaHour = floor(currentGameTime) - lastProcessedHour` 계산 후 정수 곱셈
- Stone은 `StoneTimer += deltaHour`로 누적해 5 넘을 때마다 `+Population`, 잔여는 유지
- **NPC 0명이면 생산 중단**
- **Cap 도달 시**: `Amount`를 Cap에 클램프, `SurplusFlags`에 해당 비트 설정 (Phase B에서 필요도 스코어 가중치로 사용)

### 3.2 소비 공식 (정수)

```
NPC 1명당 게임시간 1시간:
  Food -= 1
```

- 순 Food 증감 = `(2 - 1) × Pop × deltaHour = Pop × deltaHour` (시간당 +Pop)
- `FoodAmount`가 0으로 떨어져 **유지되는 시간**을 `HungerHoursAccumulated`로 누적
- 이 값이 **24를 넘는 순간 1회 경고 로그** (`[HungerTick] Village {id} exceeded 24h without food`)
- Food가 다시 0 초과가 되면 `HungerHoursAccumulated = 0` 리셋
- 실제 NPC 이탈 로직은 Phase E로 이관

### 3.3 Cap 테이블 (Phase A 기본값)

저장 오브젝트가 없으므로 기본값만:

| 자원 | Cap |
|------|-----|
| Food | 50 |
| Wood | 50 |
| Stone | 50 |

> Phase B에서 `ObjectTable.StorageCap_*`로 확장 (Woodpile +100, Chest +30, Barrel +50 등).

### 3.4 인터벌 선택

- **실제 구현 인터벌**: `UpdateInterval = 5.0f` (초)를 유지
- **"게임시간 정수 h 단위"로 누적**: 게임시간이 정수 시간만큼 진행됐을 때에만 생산/소비 적용 → **분수 시간 절대 발생 안 함**
- `deltaHour`는 항상 정수 (`floor` 차분)
- 이유:
  - 자원이 항상 정수로 유지 → 표시·세이브 단순
  - 게임시간이 배속/일시정지돼도 정수 시간 경계에서 단일 틱으로 반영
  - 프레임 독립 — 배후 시뮬레이션(추상 틱)과 같은 로직 공유 가능

```
lastProcessedHour : int = 0
OnFixedUpdate:
    currentHour = floor(AR.s.Time.CurrentGameTime)
    deltaHour = currentHour - lastProcessedHour
    if deltaHour <= 0: return
    deltaHour = min(deltaHour, MAX_DELTA_HOURS)   // 예: 1
    for each village:
        Produce(village, deltaHour)     // 정수 연산
        Consume(village, deltaHour)
    lastProcessedHour = currentHour
```

---

## 4. 첫 오브젝트 제작 루프 (Campfire 전용)

### 4.1 기본 방침

- **마을은 처음에 아무 오브젝트도 없다.** Bedroll도 없다.
- NPC가 존재하고 자원이 충분히 쌓이면 **Campfire 1개**가 지어진다.
- 제작은 **마을 단위의 추상 타이머**로 모델링한다. NPC의 실제 이동·애니메이션·위치 점유는 Phase B 이상.
- Phase A에서 **Campfire는 딱 1개**만 제작한다. 두 번째 오브젝트부터는 Phase B의 테이블·큐 시스템이 담당.

### 4.2 제작 조건 (모두 만족 시 진행)

1. 마을에 아직 Campfire가 없다 (`VillageData.HasCampfire == false`)
2. 살아있는 NPC ≥ 1명 (`Population >= 1`)
3. Wood 저장량 ≥ **제작 비용 `CAMPFIRE_WOOD_COST = 3`**
4. 마을 중심 주변 `SpawnRadius` 이내에 빈 Walkable 타일이 존재

### 4.3 제작 흐름 (`System_VillageFirstBuild`)

Priority 58, UpdateInterval 5.0s (게임시간 보정):

```
for each village v:
    if v.HasCampfire: continue
    if v.Population < 1: continue

    # 1) 착수 — 자원 예약(차감) 후 타이머 시작
    if v.FirstBuildStartedAt < 0:
        if v.Wood < CAMPFIRE_WOOD_COST: continue   # 자원 대기
        if FindEmptyTile(v.Center, SpawnRadius) == null: continue  # 자리 대기
        v.ConsumeResource(Wood, CAMPFIRE_WOOD_COST)
        v.FirstBuildStartedAt = now
        v.FirstBuildTargetTile = FindEmptyTile(...)
        log("[FirstBuild] v#{id} 착수: Wood -3, 타일={tile}, 예상완료={now + CAMPFIRE_BUILD_HOURS}h")

    # 2) 진행 — 게임시간 누적
    elapsed = now - v.FirstBuildStartedAt
    if elapsed < CAMPFIRE_BUILD_HOURS: continue

    # 3) 완료 — 실제 배치
    if MapManager.PlaceObject(v.FirstBuildTargetTile, ObjectType.Campfire):
        v.HasCampfire = true
        v.FirstBuildStartedAt = -1
        log("[FirstBuild] v#{id} Campfire 완성")
    else:
        # 자리가 중간에 막혔다 → 자원 환불 후 재착수
        v.ProduceResource(Wood, CAMPFIRE_WOOD_COST)
        v.FirstBuildStartedAt = -1
        log("[FirstBuild] v#{id} 배치 실패, 환불 후 재시도 대기")
```

### 4.4 수치

| 상수 | 값 | 근거 |
|------|-----|-----|
| `CAMPFIRE_WOOD_COST` | **3** | NPC 1명 기준 Wood 0.2/h × 15h ≈ 3. 첫 밤 안에 지을 수 있는 체감 |
| `CAMPFIRE_BUILD_HOURS` | **2** 게임시간 | Phase B의 "실시간 N분 = 오브젝트 1개" 결정 #5 이전의 임시값 |

> 두 값은 튜닝 포인트. 플레이 테스트 후 기획 조정 가능.

### 4.5 위치 탐색

마을 중심부터 **링 확장 BFS**로 가장 가까운 빈 타일:

```
FindEmptyTile(centerTile, maxRadius):
    for r in 0..maxRadius:
        for each tile on ring r:
            if MapManager.IsWalkable(tile) and MapManager.GetObjectTypeAt(tile) == None:
                return tile
    return null
```

- `maxRadius = ceil(VillageTable.SpawnRadius)` (기본 3)
- 모두 실패 시 **진행 보류**(자리 대기 상태). 재시도는 다음 틱에 자동으로 다시 시도 → Phase A에서도 "재시도" 자연 발생. 별도 재시도 정책 불필요.

### 4.6 배치 후처리

1. `MapManager.PlaceObject(x, y, CAMPFIRE_BUILDABLE_ID)` 호출. `CAMPFIRE_BUILDABLE_ID`는 `BuildableItemTable`의 Campfire 엔트리 Id (예: 100). 실패 시 `false` 반환으로 환불·재시도
2. `MapFileData._objectList`에 기록 (기존 경로 재사용) → 세이브 시 영구 보존. `ObjectType` 필드는 `None(0)` 저장 (NPC가 아니므로)
3. `VillageData.HasCampfire = true` 세이브 필드로 재로드 시 중복 배치 방지

### 4.7 비주얼 (Phase A 최소)

- `Campfire`용 `CustomTile` 에셋 1개 제작
- **Addressable 그룹에 등록** — 키는 `BuildableItemTable.Campfire.ResourceName` 값과 동일 (예: `"Tiles/Village/Campfire"`)
- **로드 시점 (lazy on-demand)**:
  - **신규 제작 시**: `System_VillageFirstBuild.TryFinish`가 `await BuildableTileRegistry.EnsureLoadedAsync(100)` → 캐시에 들어간 다음 `PlaceObject` 호출. 이 경로는 Object 렌더 지연 없음.
  - **렌더러에서 처음 만나는 Id**: `Get(id)`가 캐시 미스면 **백그라운드 로드 트리거** 후 `null` 반환 → 해당 위치의 **Object 레이어만 잠깐 공란** (Ground 지면 타일은 정상 표시). 로드 완료 시 `TileLoaded` 이벤트 → `MapManager`가 해당 타일 재렌더.
- **맵 로드 1회 스캔은 도입하지 않는다.** 청크 첫 진입 시 **오브젝트(예: Campfire)가 지면 위에 잠깐 안 보이는 것은 허용**. 지면은 정상이고, 비동기 로드가 금방 끝나므로 체감 영향 미미.

### 청크 활성/비활성과의 상호작용

플레이어가 이동하며 청크가 반복적으로 활성/비활성 되어도 정상 동작:
- 캐시는 **영구 유지** (타일은 소형 공유 에셋, release 불필요)
- 청크 재진입 시 `Get(id)`는 캐시 hit → 즉시 렌더
- 빠른 청크 왕복(경계 진동)에서도 깜빡임·재로드 없음
- **세이브 로드 후 새 청크 첫 진입**: Ground 지면은 정상 표시, 그 위의 **오브젝트(Campfire 등)만 몇 프레임 안 보이다가** 로드 완료 시 자동 재렌더. 의도된 허용 범위.
- 진행 중 새로 배치되는 오브젝트(`TryFinishAsync` 경로): 사전 await하므로 표시 지연 없음

**메모리 관점**: 실제로 렌더된 적 있는 타일 타입만 캐시 누적. 안 쓰이는 Id는 영원히 안 올라옴 → Addressable 본래 이점 최대 활용.
- **프리로드 없음** — 쓰지 않는 오브젝트는 메모리 올라오지 않음 (Addressable 본래 이점 유지)
- **`ThemeTileSet.ObjectSet` 에셋은 수정하지 않는다**
- `IsWalkable = true` (NPC 통과 가능; Phase B에서 기능 결합 시 타일 교체)
- **프리팹 인스턴싱은 아직 안 함** — 타일 스프라이트만. GameObject 렌더는 Phase B에서 오브젝트 세트와 함께.
- 제작 중 시각 표현(재료 무더기 → 골조 → 완성)은 Phase B(§12.5). Phase A는 완료 타일만 표시.

---

## 5. VillageStorageComponent (ECS 신규)

### 5.1 구조체 정의

```csharp
public struct VillageStorageComponent
{
    public int VillageId;
    public float FoodAmount;    // 소수 누적
    public float WoodAmount;
    public float StoneAmount;
    public int FoodCap;
    public int WoodCap;
    public int StoneCap;
    public int HungerTickCount; // Food <= 0인 연속 게임시간
    public byte SurplusFlags;   // bit0 Food, bit1 Wood, bit2 Stone
}
```

- **단일 엔티티 = 단일 마을**. `VillageManager`가 `RegisterVillage` 시 엔티티 생성 + 컴포넌트 추가.
- `EntityIdHelper.CreateEntity()`로 ID 발급, `VillageData`에 `EntityId` 필드 추가 (JsonIgnore).

### 5.2 Pool 크기

`ComponentManager.Initialize()`:
```csharp
_componentPools[typeof(VillageStorageComponent)] = new SparseSet<VillageStorageComponent>(32);
```
마을 상한은 현재 맵 기준 <10. 32로 충분히 여유.

### 5.3 기존 `VillageData.Resources`와의 관계

Phase A:
- **읽기 경로**: 디버그 UI는 `VillageStorageComponent`에서 읽음
- **쓰기 경로**: `VillageManager.ProduceResource / ConsumeResource`가 컴포넌트와 `VillageData.Resources` 둘 다 갱신
- **세이브/로드**: `VillageData.Resources`가 정본 (`VillageData`에 `ResourcesFloat` 추가 → 소수 보존)
- **엔티티 생성 타이밍**: `VillageManager.RegisterVillage` → 엔티티 생성 → 컴포넌트 추가. `Load` 후에는 로드된 `VillageData`를 바탕으로 컴포넌트 복원.

Phase B에서 단일화:
- `VillageData.Resources`를 삭제, 세이브는 `VillageStorageComponent` 자체를 직렬화 (별도 SaveData 래퍼)

---

## 6. 로그 명세

Phase A는 화면 UI를 만들지 않는다. 모든 상태 전환은 **콘솔 로그 1줄**로 관찰한다.

### 6.1 필수 로그

각 시스템이 특정 이벤트에서 남겨야 하는 로그:

| 태그 | 시점 | 포맷 | 레벨 |
|------|------|------|------|
| `[FirstBuild]` | Campfire 제작 착수 | `v{id} 착수: Wood -3, tile=(x,y), 완료 예정={endGameH}h` | Log |
| `[FirstBuild]` | Campfire 완성 | `v{id} Campfire 완성 at (x,y)` | Log |
| `[FirstBuild]` | 배치 실패 + 환불 | `v{id} 배치 실패, Wood +3 환불 후 재시도 대기` | Warning |
| `[HungerTick]` | Food 0 유지 24h 경과 | `Village {id} exceeded 24h without food` | Warning |
| `[VillageManager]` | 마을 최초 등록 | `Initial village {id} created at (x,y) (TableId=…)` | Log (기존) |
| `[EnsureVillagePopulated]` | 최초 NPC 스폰 | (기존 로그 유지) | Log |

> 자원 생산은 매 틱 로그를 남기지 **않는다**. 과도하게 시끄러워서 콘솔이 무용해짐. 필요 시 스냅샷 로그는 §6.2 방식으로 "필요할 때만" 찍는다.

### 6.2 선택적 스냅샷 로그 (테스트용)

수치 검증이 필요할 때만 **개발자가 수동으로 호출**하는 정적 헬퍼를 제공한다. 기본 시스템에서는 호출하지 않는다:

```csharp
VillageDebugLog.Snapshot(villageId);   // 단일 마을 즉시 1줄 로그
VillageDebugLog.SnapshotAll();         // 전체 마을
```

출력 포맷:
```
[VillageSnapshot] v0 Stage=Settlement Pop=1/1 Food=23.4/50 Wood=2.0/50 Stone=0.6/50 Hunger=0 Build=대기
[VillageSnapshot] v0 Stage=Settlement Pop=1/1 Food=25.0/50 Wood=0.0/50 Stone=0.9/50 Hunger=0 Build=제작중 30%(남은 1.4h, tile=3,5)
[VillageSnapshot] v0 Stage=Settlement Pop=1/1 Food=28.4/50 Wood=1.2/50 Stone=1.2/50 Hunger=0 Build=✓완성
```

- 콘솔에서 입력 가능한 디버그 메뉴(향후 UI)에서 버튼으로 트리거하거나, 에디터 MenuItem으로 단축키 바인딩
- Phase A에서는 정적 유틸 파일 하나만 준비. 호출은 개발자가 테스트 중 직접 수행.

### 6.3 마을 선택·카메라 이동

**Phase A에서는 다루지 않는다.** 화면 UI에서 마을을 선택하거나 카메라 이동하는 기능은 사용자가 이후 추가할 UI에 포함. 로그는 `VillageId`를 항상 포함하므로 여러 마을이 있어도 식별 가능.

---

## 7. 시스템 등록

| Priority | 시스템 | Phase | 인터벌 | 상태 |
|----------|--------|-------|--------|------|
| 57 | `System_VillageResource` | — | 5.0s | **삭제** |
| 57 | **`System_VillagePassiveProduction`** | A | 5.0s (게임시간 보정) | **신규** |
| 58 | **`System_VillageFirstBuild`** | A | 5.0s (게임시간 보정) | **신규** |
| 59 | `System_VillageRespawn` | 기존 | 5.0s | 유지 |

> Priority 57 → 58 순서로 **먼저 자원이 갱신되고 그 다음 프레임에 첫 제작이 판정**되도록 한다.

---

## 8. Stage 0 → Stage 1 승격 조건 (표시만, 승격 로직은 Phase C)

| 조건 | 충족 감지 주체 | Phase |
|------|--------------|-------|
| 인구 ≥ 3 | `VillageData.Population` | A (표시) |
| 정식 Bed ≥ 2 | `VillageManager.HasObjectSet` | B |
| Food ≥ 30 | `VillageStorageComponent.FoodAmount` | A (표시) |
| 게임시간 ≥ 24h (마을 등록 이후) | `VillageData.RegisteredAt` (신규 필드) | A (표시) |

- Phase A는 조건 **표시만**. 실제 승격 실행은 Phase C의 `System_VillageTierProgression`.
- `RegisteredAt` 필드 추가 후 `RegisterVillage` 시점에 `AR.s.Time.CurrentGameTime`로 초기화.

---

## 9. 세이브/로드 영향

### 9.1 신규 필드 (VillageData)

```
ResourceCaps             : Dictionary<ItemType,int>     // Cap 오버라이드. 비어있으면 기본 50
HungerHoursAccumulated   : int                          // Food 0 유지 누적 시간
StoneTimer               : int                          // 5h 누적 카운터 (0~4)
RegisteredAt             : float                        // 게임시간 (마을 등록 시각)
HasCampfire              : bool                         // Phase A: 첫 Campfire 완성 플래그
FirstBuildStartedAt      : float                        // -1 = 미착수
FirstBuildTileX          : int                          // 제작 타일 좌표 (FirstBuildStartedAt >= 0일 때만 유효)
FirstBuildTileY          : int
EntityId                 : int [JsonIgnore]             // 런타임 재발급
```

> `ResourcesFloat`는 **도입하지 않는다**. 기존 `VillageData.Resources` (정수 Dictionary)만 사용. 소수 누적이 필요 없어졌으므로 단일 정본.

### 9.2 하위 호환

- 구 세이브(필드 없음) → JSON 역직렬화 시 기본값으로 채워짐 (`Newtonsoft.Json` 기본 동작)
- `RegisteredAt == 0`이면 `CurrentGameTime`으로 초기화 (기존 마을은 "지금 등록" 취급)
- `FirstBuildStartedAt == 0` → 로드 시 `-1`로 보정 (미착수 취급)
- `ResourceCaps`가 null이면 빈 Dictionary로 초기화

### 9.3 마이그레이션 코드 위치

`VillageManager.Load` 내부, 기존의 `TableId <= 0` 마이그레이션 블록 근처.

---

## 10. 리스크와 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| 게임시간이 일시정지/배속으로 비선형 → 생산/제작 타이머 폭주 | 중 | `deltaHour`에 상한 (ex: 최대 1.0h/틱). 첫 제작 타이머는 `elapsed = now - StartedAt` 절대값이므로 자체 영향 없음 |
| 제작 착수 후 타일 위에 다른 것(돌·NPC)이 올라옴 | 저 | 완료 시 `PlaceObject` 반환값으로 감지 → 자원 환불 + 재착수 (§4.3) |
| ECS 컴포넌트와 `VillageData` 양쪽 동기화 실패 (불일치 버그) | 중 | 모든 쓰기를 `VillageManager` 메서드로만 수행. 직접 `Resources[k] = v` 금지 |
| 자원이 영영 안 쌓여 제작이 영영 안 일어남 (NPC 0명 상태 유지) | 저 | 이 경우 마을 전멸과 동등. `System_VillageRespawn` 쿨타임 만료 시 재스폰되며 자연 재개. 별도 처리 불필요 |

### 10.1 제작 루프의 "NPC 없음" 처리

- Phase A는 추상 타이머이므로 제작 중 NPC가 전멸해도 타이머는 멈추지 않는다.
  - **이유**: 구현 단순성. 플레이어 체감에도 "잠깐 자리를 비운 사이 완성" 정도는 허용.
  - **대안** (선택): `System_VillageFirstBuild`에서 `Population == 0`이면 타이머 정지. 재개 시 `StartedAt`를 `now - (정지 시점까지 경과)`로 보정.
- Phase B에서 NPC 직접 크래프팅으로 전환 시 자동 해결.

---

## 11. Phase A 완료 기준 (DoD)

다음이 모두 **빌드/에디터에서 콘솔 로그 또는 씬 상태로 확인 가능**해야 한다:

- [ ] 마을이 등록된 씬에서 플레이 시작 시점엔 **Campfire가 없다**. NPC만 보인다.
- [ ] `VillageDebugLog.SnapshotAll()`을 호출하면 Food/Wood/Stone 수치가 게임시간 경과에 맞게 증가한다 (정수 공식 일치).
- [ ] Pop=1, 게임시간 10h 체류 후 Food = 10 (순증 +1/h × 10h), Wood = 10 (+1/h × 10h), Stone = 2 (5h마다 +1, 10h = 2회).
- [ ] Wood가 `CAMPFIRE_WOOD_COST = 3`에 도달하면 `[FirstBuild] v{id} 착수: Wood -3 …` 로그가 뜬다 (Pop=1 기준 3시간 만에).
- [ ] 착수 후 `CAMPFIRE_BUILD_HOURS = 2` 게임시간 경과 시 Campfire 타일이 마을 근처 빈 타일에 **나타나고** `[FirstBuild] v{id} Campfire 완성` 로그가 뜬다.
- [ ] Campfire 완성 후 같은 마을에서 추가 Campfire 로그가 **찍히지 않는다** (`HasCampfire` 준수).
- [ ] Food Cap 도달 시 스냅샷 수치가 50에서 멈추고 `SurplusFlags`에 Food 비트가 설정된다.
- [ ] Food 0 상태로 24h 경과 시 콘솔에 `[HungerTick] Village {id} exceeded 24h without food` 경고가 뜬다.
- [ ] NPC 전원 사망 시 `DepletedAt` 기록되고 쿨타임 만료 후 **기존 Campfire는 그대로**, NPC만 재스폰된다.
- [ ] 제작 중 세이브 → 로드 → 자원/Cap/ResourcesFloat/FirstBuildStartedAt 유지, 남은 시간 정확히 이어서 진행.

---

## 12. 결정 필요 이슈 (Phase A 착수 전)

| # | 이슈 | 제안 기본값 | 비고 |
|---|------|------------|------|
| 1 | `VillageStorageComponent`를 Phase A에서 **완전 도입**할지, `VillageData.Resources` 확장으로 미룰지 | **도입**. Phase B에서 단일화. | §5 참고 |
| 2 | Campfire의 `BuildableItemTable.Id` | **100** (테이블 시트에서 할당). 10비트 한계(1023) 이내 유지 | 상수 `CAMPFIRE_BUILDABLE_ID` |
| 3 | Campfire 타일 에셋 | 임시 스프라이트 1장 (placeholder) + Addressable 키 `Tiles/Village/Campfire` 등록 | 아티스트 작업 병렬. `BuildableItemTable.ResourceName`과 일치 |
| 4 | `deltaHour` 상한 | **1.0h/틱** | 일시정지 복귀 시 폭주 방지 |
| 5 | `CAMPFIRE_WOOD_COST` / `CAMPFIRE_BUILD_HOURS` | **3 / 2h** | 플레이 테스트 후 튜닝 |
| 6 | Hunger 누적 단위 | **게임시간 24h**(문서 초안 그대로) | 튜닝 포인트 |
| 7 | NPC 0명 상태 중 제작 타이머 정지 여부 | **진행** (체감 허용). 필요 시 정지 모드 토글 | §10.1 |

---

## 13. 테스트 시나리오 요약

1. **정상 진행**: 새 맵 → 플레이 → 게임시간 30h 관찰 → Wood ≥ 3 도달 → 착수 로그 → 2h 뒤 Campfire 타일 출현
2. **전멸 복구**: Campfire 완성 후 NPC 모두 사망 → 쿨타임 대기 → 재스폰 → Campfire는 그대로, 재제작 트리거 안 됨
3. **세이브 무결성**: 제작 중(StartedAt 기록된 상태)에 저장 → 로드 → 남은 시간 정확히 이어서 완료
4. **Cap 도달**: Pop 10 환경에서 1시간 방치 → Wood 50에서 멈춤, Surplus 플래그
5. **기아**: Population 10, Food 0 유지 → 24h 후 경고 로그
6. **좁은 마을**: `SpawnRadius` 범위 내 빈 타일 없도록 맵 구성 → 타일 생길 때까지 제작 대기 → 타일 뚫리면 자동 착수

---

## 14. 한 줄 요약

> **Phase A는 자가 성장 루프의 최소 증거를 만든다: 시간이 흐르고 → 자원이 쌓이고 → NPC가 첫 모닥불을 짓는다. 그 한 조각이 돌아가면 Phase B에서 태울 수 있다.**
