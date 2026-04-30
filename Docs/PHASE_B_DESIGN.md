# Phase B — 오브젝트 배치 (핵심) ✅ 완료 (2026-04-24)

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅
>
> **목표**: Phase A의 "NPC 1명 → Campfire 1개" 단일 루프를 **다수 오브젝트의 순차 배치 큐**로 일반화. 하드코딩 로드맵을 따라 Bedroll, Bed, Woodpile, CropPlot, Chest, Bed 2, Well을 자동 건설.

---

## 1. 범위

**Phase B가 한 것**:
1. **범용 배치 큐** — `ObjectPlacementTaskComponent`로 마을당 여러 오브젝트 순차 건설. `System_VillageFirstBuild` → 범용 `System_VillageBuildQueue`로 흡수
2. **하드코딩 로드맵** — Stage 0(Settlement) 시퀀스를 코드 상수 배열로 (Phase D 점수교체 전 과도기)
3. **오브젝트 6종 추가** — Bedroll, Bed, Woodpile, Chest, CropPlot, Well (모두 1×1, Entity 경로)
4. **배치 위치 분산** — 같은 링 내 랜덤 픽으로 중심 편향 해소
5. **Cap 데이터 주도** — `BuildableItemTable.StorageCap_*` 컬럼을 그대로 가산
6. **하이브리드 경로 재사용** — Phase A의 `BuildingManager` / `BuildingFactory` 그대로

**제외**:
- Tier 승격 실행, 외곽 벽 → Phase C
- 필요도 스코어, 세트 판정, `ProvidedService` → Phase D
- NPC 실제 이동 → Phase E
- 제작 중 시각 표현 (재료 무더기 → 골조 → 완성) → Phase C+
- 오브젝트 파괴/재건 → Phase F

> **한 줄 범위**: "Phase A 루프를 N번 돌리는 것."

---

## 2. 핵심 설계 결정

### 2.1 마을당 동시 배치 = 1건

추상 타이머 단순화 + 자원 경쟁 방지. 다중 동시 진행은 Phase D(NPC 실 배정) 때 NPC 수만큼 자연스럽게 병렬화된다. SparseSet의 "엔티티당 1 컴포넌트" 제약과도 정합.

### 2.2 `ObjectPlacementTaskComponent`는 비세이브 (런타임만)

마을 엔티티는 로드 시 `CreateStorageEntity`에서 재생성되므로 Task 컴포넌트도 같이 재구성하려면 별도 경로 필요 → **`VillageData.CurrentBuild*` 6필드(TableId/StartedAt/TileX/TileY/ReservedWood/ReservedStone)가 세이브 정본**. 매 틱 컴포넌트 재구성. Phase A의 `FirstBuild*`를 이름만 일반화한 것.

### 2.3 Cap 확장은 데이터 주도 (`Function`과 분리)

Cap 증가량은 C# switch가 아니라 `BuildableItemTable.StorageCap_Food/Wood/Stone` 컬럼에서 직접 가산. `Function` 필드는 "Cap 외 효과"만 담당.

이점:
- 튜닝이 시트 한 곳에 집중 (Woodpile +100 → +120: 시트만 수정)
- 새 저장 오브젝트 추가 코드 0줄 (Barrel/Stockpile 등)
- Phase D의 `ObjectTable` 스키마와 컬럼 이름 동일 → 이행 시 흡수 가능

### 2.4 Chest는 공유 풀이 아닌 `Wood +30 + Stone +30` 근사

VILLAGE_GROWTH_STAGES.md §2.4의 "범용 Cap +30 (Wood/Stone/Metal 공유)"는 Phase B에선 **컬럼 2개로 근사**. 정확한 공유 풀은 Phase D의 본격 저장 시스템에서.

### 2.5 로드맵 6종 전부 `SpawnType=Entity`

Tile 경로는 현재 Id=1 나무 벽만 사용, Phase C의 Palisade/StoneWall까지 보존. 로드맵 오브젝트는 모두 **개별 상태(HP, 점유 NPC, 상호작용 UI)** 가 필요 → Entity가 자연스러움. CropPlot의 Walkable=true는 "Entity 경로에서 Blocked 비트 안 세팅"으로 처리 (`CustomTile` 에셋 불필요).

### 2.6 중심 편향 방지

결정적 스캔 순서(`dx/dy: -r→r`)가 항상 SW 코너부터 시작 → 7개 오브젝트가 한 사분면에 압축되는 부작용 발견. **`FindEmptyTileNearest`를 "가장 가까운 비어있지 않은 링 내에서 랜덤 픽"으로 변경**. 링 우선순위(중심 선호)는 유지.

> 8방위 점유 페널티·큰길 예약·구역화는 Phase C·D에서 정교화.

### 2.7 환불 경로

`PlaceObject` 실패(타일이 중간에 막힘) 시 `CurrentBuildReservedWood/Stone`로 자원 환불 후 다음 틱에 자동 재시도. Phase A의 환불 패턴을 Stone까지 확장한 것.

---

## 3. 구현 결과 요약

| 영역 | 결과물 |
|------|--------|
| 신규 컴포넌트 | `ObjectPlacementTaskComponent` (Pool 32) |
| 신규 시스템 | `System_VillageBuildQueue` (Priority 58, 5s) — `System_VillageFirstBuild` 자리 승계 |
| 신규 클래스 | `VillageBuildRoadmap` (정적, `SETTLEMENT_SEQUENCE` 7행) |
| 시스템 확장 | `VillageManager.OnObjectPlaced` (Cap 가산 + Storage 동기화) · `VillageTileFinder.FindEmptyTileNearest` (랜덤 픽) · `BuildableTileRegistry` (단순화 + InvalidKey 가드) |
| 테이블 | `BuildableItemTable` 신규 5컬럼 (`Cost_Wood`/`Cost_Stone`/`StorageCap_Food/Wood/Stone`) + 6종 신규 행 (101 Bedroll · 102 Bed · 110 Woodpile · 111 Chest · 120 CropPlot · 130 Well) |
| `VillageData` | `PlacedObjectTypeIds` (누적 List) + `CurrentBuild*` 6필드 추가, `HasCampfire`/`FirstBuild*`는 마이그레이션 후 obsolete |
| 신규 자산 | `Sprites/Items/{Bedroll,Bed,Woodpile,Chest,CropPlot,Well}` (placeholder 6장) |

**마이그레이션**: 구 세이브 `HasCampfire==true` → `PlacedObjectTypeIds.Add(100)` 1회. `FirstBuildStartedAt >= 0` → `CurrentBuild*` 승격.

---

## 4. 한 줄 요약

> **Phase B는 Phase A의 "모닥불 하나"를 "모닥불 + 침낭 + 침대 + 나무 야적장 + 텃밭 + 궤짝 + 침대 + 우물"로 일반화한다. 새로운 시스템 구조는 없다 — 큐와 로드맵뿐이다.**
