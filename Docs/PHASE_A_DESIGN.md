# Phase A — 자가 생성 최소 루프 (MVP) ✅ 완료 (2026-04-24)

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
>
> **목표**: "플레이어가 손대지 않아도 마을이 아주 느리게 스스로 돌아간다"를 **가장 작은 단위**로 증명한다.

---

## 1. 범위

**Phase A가 한 것**:
1. **시간 기반 패시브 자원 생산/소비** — NPC 수에 비례해 Food/Wood/Stone 누적, Food는 매시간 소비
2. **자원 저장 상한(Cap)** — 자원당 기본 50, 초과분은 절삭 + 잉여 플래그
3. **첫 오브젝트 제작** — Wood가 충분하면 Campfire 1개를 자동 건설 (마을 단위 추상 타이머)
4. **로그 기반 디버깅** — 화면 UI 대신 `Debug.Log` + `VillageDebugLog.SnapshotAll()`

**제외 (후속 Phase)**:
- 범용 배치 큐, 다수 오브젝트, 테이블 기반 레시피 → Phase B
- NPC 실제 이동/제작 애니메이션 → Phase E (배후 시뮬과 함께)
- Tier 승격, 벽, 직업 보너스, 세트 판정 → Phase C·D
- 출생/이탈/분가 → Phase E

---

## 2. 핵심 설계 결정

### 2.1 `VillageStorageComponent` ECS 승격은 부분만

`VillageData.Resources`(Dictionary)를 ECS 컴포넌트로 완전 단일화하려 했으나, `VillageData`가 세이브/로드·MapManager 초기화·NpcManager·5+ 시스템에서 참조 중이라 블래스트 반경이 Phase A MVP를 넘어간다.

**결정**: 컴포넌트는 신설하되 **`VillageData.Resources`와 병존**. 쓰기는 `VillageManager` 메서드로만 양쪽 동시 갱신, 세이브 정본은 `VillageData`. Phase B 이후 단일화 시점을 자유롭게 선택.

### 2.2 `ObjectType` enum 미확장

기존 `None/Stone/Npc/WoodWall` 4종 그대로. 신규 오브젝트 식별은 **`BuildableItemTable.Id`가 담당**. Tile 경로의 ulong에는 enum 대신 Id를 그대로 기록.

### 2.3 자원은 정수 + 게임시간 정수h 차분

- `deltaHour = floor(currentGameTime) - lastProcessedHour` → **분수 시간 절대 발생 안 함**
- 자원이 항상 정수 → 표시·세이브 단순, 배후 시뮬과 같은 로직 공유 가능
- Stone은 5h 누적 카운터로 +1 (생산 확률 표현)

### 2.4 Campfire 단일 하드코딩

Phase A는 범용 배치 큐를 도입하지 않고 **Campfire 1개 전용** 시스템(`System_VillageFirstBuild`). 이유: "자가 성장 루프"의 최소 증거를 만든 뒤 Phase B에서 일반화하는 게 검증 위험을 낮춘다.

### 2.5 추상 타이머 (NPC 실제 이동 없음)

"NPC가 하나라도 살아있으면 진행" — 이동·애니메이션·자재 픽업은 모두 생략. NPC 0명일 때 타이머 정지 여부도 단순화: **계속 진행** 허용. 체감은 "잠깐 자리를 비운 사이 완성"으로 수용 가능.

### 2.6 BuildableTileRegistry — lazy on-demand

타일 에셋은 **호출자가 `await EnsureLoadedAsync(id)` 후 PlaceObject**. 렌더러는 캐시 hit 시 동기 조회, 미스면 백그라운드 로드 트리거 후 null 반환 → Object 레이어만 잠깐 공란(Ground는 정상).

- 프리로드 없음 (Addressable 본래 이점 보존)
- 캐시 영구 유지 (타일은 소형 공유 에셋, 청크 왕복 시 깜빡임 0)
- 미등록 키는 `LoadResourceLocationsAsync` 사전 검증으로 InvalidKey 콘솔 노이즈 차단

### 2.7 화면 UI는 Phase A 범위 밖

수치 검증은 콘솔 로그 1줄 + `VillageDebugLog.Snapshot` 정적 헬퍼. 사용자가 이후 디버그 메뉴/MenuItem으로 트리거. 매 틱 자동 출력은 **금지** — 콘솔이 무용해짐.

### 2.8 하이브리드 건물 시스템 (Phase A 후반 통합)

`BuildableItemTable.SpawnType` 컬럼으로 **Tile**(나무 벽 같은 정적 반복 구조) / **Entity**(Campfire 같은 개별 오브젝트) 경로 공존. `BuildingManager`/`BuildingFactory` 신설로 Entity 경로를 캡슐화. 상세는 [archive/PHASE_A_HYBRID_PLAN.md](archive/PHASE_A_HYBRID_PLAN.md).

---

## 3. 구현 결과 요약

| 영역 | 결과물 |
|------|--------|
| 신규 시스템 | `System_VillagePassiveProduction` (Priority 57, 5s) · `System_VillageFirstBuild` (Priority 58, 5s, Phase B에서 BuildQueue로 흡수) |
| 신규 컴포넌트 | `VillageStorageComponent` (Pool 32) |
| 삭제 | `System_VillageResource` (5초마다 Pop×1 단순 생산) |
| 신규 인프라 | `BuildableTileRegistry` (lazy 캐시) · `BuildingManager`/`BuildingFactory` (Entity 경로) · `VillageDebugLog` (정적 헬퍼) |
| 신규 자산 | Campfire `CustomTile` + Addressable 키 `Tiles/Village/Campfire` · `Sprites/Items/Campfire` |
| 테이블 | `BuildableItemTable` Campfire 엔트리 (Id=100) |

> Phase A는 Campfire만 시드. Bedroll/Bed 등 후속 오브젝트는 Phase B의 범용 큐로 이관.

---

## 4. 한 줄 요약

> **시간이 흐르고 → 자원이 쌓이고 → NPC가 첫 모닥불을 짓는다. 그 한 조각이 돌아가면 Phase B에서 태울 수 있다.**
