# Phase A — Unity Editor 작업 체크리스트

> 코드 작업은 완료됐음. 이 문서는 **Unity 에디터에서 수동으로 해야 하는 작업**만 나열.
> 기획: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) / 구현: [PHASE_A_IMPLEMENTATION.md](PHASE_A_IMPLEMENTATION.md)

---

## 0. 사전 작업 (Unity 리컴파일)

- [ ] Unity Editor로 포커스 이동 → 자동 리임포트 대기
- [ ] Console에 **컴파일 에러 0** 확인
  - VSCode의 IDE 진단에 `BuildableTileRegistry`, `VillageStorageComponent`, `System_VillagePassiveProduction`, `System_VillageFirstBuild`가 "not found"로 뜬 것은 csproj 갱신 전 상태. 리임포트 후 사라져야 정상
- [ ] 기존 빌드가 실행되는지 Play Mode로 1분 확인 (마을 등록·NPC 스폰 정상)

---

## 1. 데이터 테이블 작업

### 1.1 `BuildableItemTable`에 Campfire 행 추가

**위치**: 구글 시트 (또는 CSV 원본) → `BuildableItemTable` 시트

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
| `ResourceName` | `Tiles/Village/Campfire` |

> `ResourceName`은 Addressable 키와 **완전 일치**해야 함 (§3.2에서 동일 문자열 사용).
> `Id=100` 상수가 `System_VillageFirstBuild.CAMPFIRE_BUILDABLE_ID`와 매칭됨. 변경 시 코드 상수도 동시 수정.

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

## 2. 에셋 제작

### 2.1 Campfire `CustomTile` 에셋

- [ ] 폴더 준비: `Assets/Art/Tiles/Village/` (없으면 생성)
- [ ] Placeholder 스프라이트 1장 준비
  - 임시로 단색 + 텍스트 라벨("🔥" 또는 "CF") 가능
  - 크기: 타일 1칸 (프로젝트 그리드 기준)
- [ ] `CustomTile` 에셋 생성:
  - 마우스 우클릭 → Create → `CustomTile` (프로젝트에 기존 Create 메뉴가 있어야 함)
  - 파일명: `Campfire.asset`
- [ ] 인스펙터 설정:
  - `_sprite` → 위 Placeholder 스프라이트
  - `_layer` → `Object` (GlobalEnum.TileLayer)
  - `_isWalkable` → **`true`** (NPC 통과 가능)
  - `_customData` → 기본값 0 유지

### 2.2 파일 경로 확인

- [ ] 최종 에셋 경로: `Assets/Art/Tiles/Village/Campfire.asset`

---

## 3. Addressables 등록

### 3.1 그룹 설정

- [ ] `Window` → `Asset Management` → `Addressables` → `Groups` 창 열기
- [ ] 기존 그룹 중 타일/에셋용 그룹 선택 (없으면 신규 그룹 `Tiles` 또는 `VillageTiles` 생성, Schema: `Packed Assets`)
- [ ] `Campfire.asset`을 해당 그룹으로 드래그

### 3.2 Address 설정 (**필수**)

- [ ] 추가된 Campfire 엔트리 선택 → **Address** 필드를 다음으로 변경:
  ```
  Tiles/Village/Campfire
  ```
- [ ] `BuildableItemTable.Campfire.ResourceName` 값과 **완전 일치** 확인 (오타·대소문자 주의)

### 3.3 Labels (선택)

- [ ] 필요하면 `village-tile` 라벨 부착 (검색·관리 편의)

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
- [ ] `MapManager`의 ThemeTileSet 연결 확인 (기존 상태 유지)

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
