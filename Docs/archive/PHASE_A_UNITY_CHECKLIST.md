# Phase A — Unity Editor 작업 체크리스트

> 코드 작업은 완료됐음. 이 문서는 **Unity 에디터에서 수동으로 해야 하는 작업**만 나열.
> 기획: [../PHASE_A_DESIGN.md](../PHASE_A_DESIGN.md) / 구현: [PHASE_A_IMPLEMENTATION.md](PHASE_A_IMPLEMENTATION.md)

---

## 0. 사전 작업 (Unity 리컴파일)

- [ ] Unity Editor로 포커스 이동 → 자동 리임포트 대기
- [ ] Console에 **컴파일 에러 0** 확인
  - VSCode의 IDE 진단에 `BuildableTileRegistry`, `VillageStorageComponent`, `System_VillagePassiveProduction`, `System_VillageFirstBuild`가 "not found"로 뜬 것은 csproj 갱신 전 상태. 리임포트 후 사라져야 정상
- [ ] 기존 빌드가 실행되는지 Play Mode로 1분 확인 (마을 등록·NPC 스폰 정상)

---

## 1. 데이터 테이블 작업

### 1.1 `BuildableItemTable`에 Campfire 행 추가 (하이브리드: Entity 타입)

**위치**: 구글 시트 → `BuildableItem` 시트 (**작업 완료됨** — MCP로 이미 추가 반영)

| 컬럼 | 값 |
|------|-----|
| `Id` | **100** |
| `Name` | `Campfire` |
| `Tooltip` | `마을을 밝히는 첫 모닥불` |
| `IsBreakable` | `FALSE` |
| `HP` | `50` |
| `DropItemId` | `0` |
| `Size_Width` | `1` |
| `Size_Height` | `1` |
| `Recipe` | `0` |
| `Function` | `0` |
| `ResourceName` | `Sprites/Items/Campfire` |
| `SpawnType` | **`Entity`** (신규 L열) |
| `AnimationId` | **`0`** (신규 M열, Phase A는 정적) |

> **Entity 타입**: Tilemap 셀이 아닌 `Prefabs/Building` 기반 `GameObject`로 생성됨.
> `ResourceName`은 `Sprite` Addressable 키로 해석 (SpawnType=Tile과 의미 다름).
> `Id=100` 상수가 `System_VillageFirstBuild.CAMPFIRE_BUILDABLE_ID`와 매칭됨.
> 기존 `Id=1` 나무벽은 `SpawnType=Tile`로 Tile 경로 유지.
> 설계 전체는 [PHASE_A_HYBRID_PLAN.md](PHASE_A_HYBRID_PLAN.md) 참조.

### 1.2 테이블 재빌드 (`.bytes` 생성)

- [ ] 프로젝트의 **테이블 Export 메뉴 실행** (보통 `Tools/Export Tables` 또는 유사)
- [ ] `Assets/Resources/Data/` 아래 `BuildableItemTable.bytes` 갱신 확인
- [ ] Play Mode 진입 시 콘솔에 `BuildableItemTable` 로드 로그 확인 (기존 패턴)

### 1.3 `VillageTable` 점검 (Stage 0 마을)

| 필드 | 권장값 |
|------|-------|
| `DefaultNpcList` | `"1"` 등 1명 NPC Id |
| `RespawnCooldown` | `12` (게임시간 h) |
| `SpawnRadius` | `3` |

- [ ] 현재 시트에 Stage 0용 마을 행이 존재하는지 확인
- [ ] 없으면 추가 + 테이블 재빌드

---

## 2. 에셋 제작 (Prefabs/Entity 통합 경로)

### 2.1 Campfire 스프라이트

- [ ] 폴더 준비: `Assets/Art/Sprites/Items/` (없으면 생성)
- [ ] Placeholder 스프라이트 1장 준비
  - 임시로 단색 + 텍스트 라벨("🔥" 또는 "CF") 가능
  - 크기: 타일 1칸 (프로젝트 그리드 기준)
- [ ] `Assets/Art/Sprites/Items/Campfire.png`로 저장
- [ ] 임포트 설정 확인:
  - `Texture Type` = `Sprite (2D and UI)`
  - `Sprite Mode` = `Single`
  - `Pixels Per Unit` = 기존 엔티티/타일과 동일
  - `Filter Mode` = `Point (no filter)` (픽셀아트)
  - `Compression` = `None`

### 2.2 `Prefabs/Entity`에서 레거시 컴포넌트 제거

건물은 별도 프리팹을 만들지 않고 `Prefabs/Entity`를 재사용한다.
런타임 애니메이션은 `SpriteAnimationData`가 `_sr.sprite`를 직접 교체하므로 `SpriteLibrary`/`SpriteResolver` 컴포넌트는 더 이상 필요 없다.

- [ ] `Assets/Prefabs/Game/Creature/Entity.prefab` 열기
- [ ] 자식 `SpriteRenderer` GameObject 선택
- [ ] **`SpriteLibrary` 컴포넌트 Remove**
- [ ] **`SpriteResolver` 컴포넌트 Remove**
- [ ] 프리팹 저장
- [ ] Play Mode에서 기존 몬스터/NPC/플레이어 애니메이션 정상 재생 확인 (비회귀)

### 2.3 레거시 CustomTile 에셋 (불필요)

기존 계획에 있던 `Assets/Art/Tiles/Village/Campfire.asset` (`CustomTile`)은 **하이브리드 경로에서 사용 안 함**.
이미 만들어뒀다면 Addressable 등록만 빼면 되고, 안 만들었다면 건너뛰어도 됨.

---

## 3. Addressables 등록

통합 경로에서는 **신규 항목 1개만** 등록 (Sprite). 프리팹은 기존 `Prefabs/Entity` 재사용.

### 3.1 그룹 설정

- [ ] `Window` → `Asset Management` → `Addressables` → `Groups` 창 열기
- [ ] 기존 그룹 또는 `Items` 그룹에 Campfire 스프라이트 드래그

### 3.2 Address 설정 (**필수**)

| 에셋 | Address (코드와 정확히 일치) |
|---|---|
| `Assets/Art/Sprites/Items/Campfire.png` | `Sprites/Items/Campfire` |

- [ ] Campfire Sprite Address = `Sprites/Items/Campfire` (`BuildableItem` 시트의 `ResourceName`과 완전 일치)
- [ ] `Prefabs/Entity`는 기존에 이미 등록되어 있음 — 확인만

> 기존 `Tiles/Village/Campfire` (TileBase) 항목은 사용 안 함. 등록돼 있다면 제거하거나 방치.

### 3.3 Labels (선택)

- [ ] `village-asset` 라벨 부착 가능 (검색·관리 편의)

### 3.4 콘텐츠 빌드

- [ ] `Addressables Groups` 창 상단 메뉴 → `Build` → `New Build` → `Default Build Script`
- [ ] 빌드 완료 후 `Library/com.unity.addressables/` 에 카탈로그 갱신 확인
- [ ] Play Mode Script 설정 확인:
  - 개발 중: `Use Asset Database (fastest)` 권장 (에셋 변경 즉시 반영)
  - 빌드 테스트: `Use Existing Build`

---

## 4. ThemeTileSet 확인 (변경 없음)

- [ ] `Assets/Art/Tiles/Theme/*.asset` (프로젝트 현재 ThemeTileSet 에셋들)
- [ ] **수정하지 않는다** (Phase A는 Addressable 경로 사용 → `ObjectSet` 배열 확장 불필요)
- [ ] 기존 `ObjectSet[1..3]` (Stone/Npc/WoodWall)은 레거시 fallback으로 유지됨

---

## 5. 씬/프리팹 확인

- [ ] `GameScene` 또는 메인 플레이 씬 열기
- [ ] `AR` 오브젝트(MonoBehaviour)의 Serialized 매니저 참조 확인:
  - `Village Manager`, `Map Manager`, `Time Manager` 등 모두 연결됨
  - **`Building Manager` 신규 필드 연결** (하이브리드 도입분)
    - AR 프리팹/오브젝트에 `BuildingManager` 컴포넌트 추가 (GameObject에 New Component) 후 `_buildingManager` 필드에 할당
- [ ] `Prefabs/Entity`의 SpriteLibrary/SpriteResolver 컴포넌트가 제거되었는지 재확인 (§2.2)
- [ ] `MapManager`의 ThemeTileSet 연결 확인 (기존 상태 유지)
- [ ] `GameScene`에 **`_buildingRoot`** Serialized 필드 추가 연결 필요
  - Hierarchy에 `BuildingRoot` GameObject 만들고 `GameScene._buildingRoot`에 드래그

---

## 6. 세이브 파일 처리

### 6.1 신규 필드 추가로 호환성 영향

`VillageData`에 다음 필드가 추가됨:
- `ResourceCaps`, `StoneTimer`, `HungerHoursAccumulated`, `RegisteredAt`
- `HasCampfire`, `FirstBuildStartedAt`, `FirstBuildTileX/Y`

Newtonsoft.Json이 기본값으로 채우지만 다음 케이스 주의:
- `FirstBuildStartedAt == 0f`인 구 세이브 → 코드에서 자동으로 `-1f`로 보정 (미착수 취급)
- `ResourceCaps`가 `null`이면 로드 코드가 빈 Dictionary로 초기화

### 6.2 권장 조치

- [ ] 개발 중에는 **기존 세이브 파일 삭제** (테스트 혼동 방지)
  - 경로: `%USERPROFILE%/AppData/LocalLow/<Company>/<Product>/` 또는 프로젝트 설정된 SavePath
- [ ] "새 게임"으로 시작해서 Phase A 동작 검증

---

## 7. 테스트 환경 준비

### 7.1 게임 시간 배속

Play Mode에서 빠르게 테스트하려면 **TimeManager 설정 조정**:

- [ ] 씬의 `TimeManager` 컴포넌트 인스펙터에서 `_realSecondsPerGameHour` 값 확인
- [ ] 기본 `60` (실시간 60초 = 게임 1시간)
- [ ] 테스트용 일시 변경 권장: `5` (실시간 5초 = 게임 1시간) → 10분 체류로 10h 관찰 가능
- [ ] **테스트 완료 후 원래 값으로 복구**

### 7.2 콘솔 필터

Unity Console 창에서 다음 문자열로 필터링하면 Phase A 이벤트만 보임:
- `[FirstBuild]` — 제작 착수/완성/환불
- `[HungerTick]` — 기아 경고
- `[VillageSnapshot]` — 수동 스냅샷
- `[VillageManager]` — 마을 등록 (기존)
- `[EnsureVillagePopulated]` — NPC 스폰 (기존)

### 7.3 VillageDebugLog 호출 방법 (선택)

테스트 중 수치 스냅샷을 찍으려면:

- **옵션 A**: 디버그 MonoBehaviour에 버튼 바인딩
  ```csharp
  [UnityEditor.MenuItem("Debug/Village/Snapshot All")]
  static void SnapAll() { ARPG.Village.VillageDebugLog.SnapshotAll(); }
  ```
  이렇게 Editor 스크립트 1개 만들면 메뉴에서 호출 가능.
- **옵션 B**: 기존 디버그 키 바인딩이 있으면 거기에 추가
- **옵션 C**: 그냥 임시로 Update() 어딘가에서 `Input.GetKeyDown(KeyCode.F9)` 체크 후 호출

---

## 8. 검증 체크리스트 (DoD)

다음을 순서대로 확인해서 모두 통과하면 Phase A 완료.

### 8.1 기본 플로우

- [ ] Play Mode 진입 → 콘솔에 `[VillageManager] Initial village …`, `[EnsureVillagePopulated] …` 로그
- [ ] 마을 중심에 NPC 1명만 보이고 **Campfire 없음**
- [ ] `VillageDebugLog.SnapshotAll()` 호출 → `Pop=1/1 Food=0/50 Wood=0/50 Stone=0/50 Build=대기`

### 8.2 자원 누적 (Pop=1, 10h 체류)

| 시점 | 기대값 |
|------|-------|
| 3h | `Food=3, Wood=3, Stone=0, StoneTimer=3/5` → **`[FirstBuild] v0 착수: Wood -3`** |
| 5h | `Food=5, Wood=2, Stone=1, StoneTimer=0` → **`[FirstBuild] v0 Campfire 완성`** + 씬에 타일 출현 |
| 10h | `Food=10, Wood=7, Stone=2, StoneTimer=0, Build=✓완성` |

> Wood: 10h × 1 = 10 생산, 3 차감 → 7
> Food: (2-1) × 10 = 10 순증, 0에서 시작이므로 10
> Stone: 5h·10h에 각 +1 → 2

### 8.3 경계 조건

- [ ] Campfire 완성 후 같은 마을 유지 → 추가 `[FirstBuild]` 로그 **안 뜸**
- [ ] 세이브 → 종료 → 로드 → 자원/Cap/HasCampfire 유지, 타일 복원 (몇 프레임 블랭크 후 자동 렌더)
- [ ] NPC 전멸 → 쿨타임 경과 후 재스폰 → Campfire 타일 그대로, 재제작 로그 안 뜸
- [ ] Pop 10, Food 0 상태로 24h 체류 → `[HungerTick] Village 0 exceeded 24h without food` 1회 (중복 안 뜸)
- [ ] Pop 5, 11h 체류 → 스냅샷에 `Wood=50/50*` (* = Surplus 비트)

### 8.4 세이브 호환

- [ ] Phase A 이전 세이브 파일이 있었다면 로드 시 크래시 없음
- [ ] 로드 후 자원 수치 정상 표시

### 8.5 Addressable 동작

- [ ] 첫 청크 활성화 시 Campfire 타일이 즉시 보이진 않고 **잠깐 뒤 나타남** (Ground 지면은 정상)
- [ ] 한 번 로드 후 다른 청크로 이동 → 돌아오면 즉시 표시 (캐시 hit)

---

## 9. 롤백 플랜 (문제 발생 시)

`System_VillageFirstBuild` 또는 자원 공식이 이상하면:

- [ ] `SystemManager.Initialize()`에서 Priority 57/58 등록 라인 주석 처리
- [ ] 구 `System_VillageResource` 복원은 Git에서 revert (`System_VillageResource.cs` + .meta 복구 + SystemManager 되돌리기)

---

## 10. 완료 후

- [ ] 모든 체크리스트 통과 시 Phase A 완료
- [ ] `TimeManager._realSecondsPerGameHour` 테스트 값 원복
- [ ] Addressables Play Mode Script를 출시 설정으로 되돌림
- [ ] Phase B 착수 준비 (범용 오브젝트 배치 큐 + 하드코딩 로드맵)
