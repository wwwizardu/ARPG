# 마을 성장 단계별 상세 기획

> 전체 비전은 [VILLAGE_EXPANSION_DESIGN.md](VILLAGE_EXPANSION_DESIGN.md) 참고.
> 이 문서는 **"NPC 1명 시드 → 시간 흐름에 따른 자가 성장 → 외벽 완성"** 까지의 구체적 단계를 다룬다.

---

## 0. 설계 전제

- **시작은 항상 정착민 NPC 1명 + 침낭(Bedroll) 1개 + 모닥불(Campfire) 1개**. 플레이어 개입 없이도 마을은 "아주 느리게" 성장한다.
- **"건물"이 아니라 "오브젝트"를 만든다.** 집 한 채를 통째로 짓는 것이 아니라, **기능 단위의 소형 오브젝트**를 타일 위에 배치한다.
  - **집** ≡ Bed(침대) + Chest(궤짝)
  - **대장간** ≡ Furnace(화로) + Anvil(모루) + QuenchVat(물통)
  - **여관** ≡ InnBed(여관 침대) 여럿 + Hearth(화덕)
  - **상점** ≡ MerchantStall(노점 판매대)
  - **도서관/길드** ≡ Bookshelf(서가) + Desk(책상)

  → 오브젝트 조합이 "건물 기능"을 만들어낸다. 데이터 상 "집 건물"이라는 엔티티는 **존재하지 않는다**.
- **실제로 짓는 연속 구조물은 '벽'뿐**. 외곽 방어(팰리세이드, 돌담, 게이트, 망루)만 다중 타일이 맞물리는 구조다.
- **자원은 두 경로**: 패시브 생산(NPC 있으면 누적) + 플레이어 기증(점프 부스트).
- **진행은 게임 시간(hour) 기준**. 실시간 30분 ≈ 게임 1일.

이 접근의 이점:
1. 기존 1타일 1오브젝트 시스템을 그대로 사용 → 신규 타일 인프라 거의 불필요
2. 세분화된 진행도 — "대장간 완성"이 아니라 "화로만 있음 / 모루도 생김 / 물통까지 완비"의 단계적 서비스
3. 배치 유연성 — NPC가 빈 타일 사정에 맞춰 오브젝트를 떨어뜨려 배치
4. 파괴 유연성 — 몬스터 침공으로 모루만 부서져도 화로는 살아있음

---

## 1. 시드 단계 — Stage 0: Seed (NPC 1명)

### 1.1 시작 상태
| 항목 | 값 |
|------|-----|
| 인구 | 1 (Founder NPC) |
| 오브젝트 | Bedroll(침낭) × 1, Campfire(모닥불) × 1 |
| 자원 | Wood 10, Food 5 |
| Tier | `VillageStage.Settlement` |
| 경계 | 마을 중심 기준 반경 6타일 원형 |
| 외벽 | 없음 (BoundaryMarker 표지석 2~4개로 경계 표시만) |

### 1.2 Founder NPC
- 직업: `Gatherer` (만능형 채집 직업)
- 초기 `NpcStatComponent`: Loyalty 높게 설정 → 이탈 방지
- 죽으면 `RespawnCooldown` 후 재스폰 (기존 `EnsureVillagePopulated` 로직 활용)

### 1.3 Stage 0 종료 조건 → Stage 1 승격
아래 **전부** 만족 시 `VillageStage.Hamlet`으로 승격:
- 인구 ≥ 3
- 설치된 **정식 Bed 오브젝트** ≥ 2개 (Bedroll 제외, 내구성 있는 Bed)
- 식량 저장량 ≥ 30
- 게임 시간 경과 ≥ 24h (최소 체류 기간, 너무 빠른 승격 방지)

---

## 2. 시간에 따른 자원 자가생성

### 2.1 패시브 생산 (오브젝트 무관)
NPC 1명당 기본 주기 생산량. `System_VillagePassiveProduction` (FixedUpdate, UpdateInterval=게임 1h 상당):

```
NPC당 시간당 생산:
  Food  +0.5
  Wood  +0.2
  Stone +0.05
```

- 소수 누적 후 `Floor` 처리
- 전멸 상태(NPC 0명)면 패시브 중단

### 2.2 직업별 액티브 보너스 (오브젝트 기반)
NPC가 `Working` 상태 + 자기 직업의 작업 오브젝트가 마을 내 존재할 때만 가산:

| 직업 | 필요 오브젝트 | 시간당 추가 생산 |
|------|---------------|------------------|
| Woodcutter | ChoppingBlock(도끼대) | Wood +1.5 |
| Miner | MiningCart(광차) | Stone +1.0, Metal +0.2 |
| Farmer | CropPlot(텃밭) | Food +0.8 / plot |
| Hunter | DryingRack(건조대) | Food +1.0, Leather +0.3 |
| Blacksmith | Furnace + Anvil 세트 | Metal → 장비 변환 |
| Merchant | MerchantStall(노점) | Gold 환전 루트 개방 |

※ "세트"는 §5에서 정의 — 같은 마을 내에 둘 다 존재하면 성립.

### 2.3 소비량 (인구 유지 비용)
- **식량 소비**: NPC 1명당 시간당 Food 0.3
  - 저장량 0 → `HungerTick` 시작 → 일정 시간 내 미해결 시 **이탈**
- **오브젝트 수리비**: 월 1회 Wood/Stone 소량 (Tier ≥ 2부터)
  - 벽은 세그먼트 단위, 일반 오브젝트는 개당 소량

### 2.4 자원 상한(Cap) — 저장 오브젝트 누적

저장 오브젝트 미보유 시 각 자원 기본 상한 50. 보유하면 개수만큼 누적:

| 오브젝트 | 효과 |
|----------|------|
| Woodpile(나무 야적장) | Wood Cap +100 |
| Stockpile(돌 더미) | Stone Cap +80 |
| Chest(궤짝) | 범용 Cap +30 (Wood/Stone/Metal 공유) |
| Barrel(통) | Food Cap +50 |
| Strongbox(금고) | Gold Cap +500 |

→ 오브젝트를 더 배치할수록 Cap 누적. 상한 초과분은 **잉여 플래그**로 표시해 다음 배치의 스코어 가중치를 올린다.

---

## 3. 오브젝트 배치 로드맵 (Stage별 우선순위)

`System_VillageNeedsEvaluation`이 자동 결정하지만, 테이블 레벨에서 **Stage 게이트**(RequiredStage)를 둔다.

### 3.1 Stage 0 → Stage 1 (Settlement → Hamlet)
테마: **기본 생존 오브젝트**

| # | 오브젝트 | 기능 | 크기 | 자원 |
|---|---------|------|------|------|
| 1 | **Bed**(침대) | 정식 주거 1인 + 출생 조건 만족 | 1×1 | Wood 10 |
| 2 | **Woodpile**(나무 야적장) | Wood Cap +100 | 1×1 | Wood 8 |
| 3 | **CropPlot**(텃밭) | Farmer 활성화 / Food +0.8/h | 1×1 | Wood 5 |
| 4 | **Chest**(궤짝) | 범용 Cap +30 | 1×1 | Wood 6 |
| 5 | **Bed** 2번째 | 주거 +1 (승격 조건) | 1×1 | Wood 10 |
| 6 | **Well**(우물) | 생활 기반, 마을 전체 생산 ×1.05 | 1×1 | Stone 15 |

→ Hamlet 승격 시 경계 반경 6 → 10타일 확장.

### 3.2 Stage 1 → Stage 2 (Hamlet → Village)
인구 목표 8~12명 / 테마: **생산 오브젝트 해금**

| # | 오브젝트 | 기능 | 크기 | 자원 |
|---|---------|------|------|------|
| 1 | ChoppingBlock(도끼대) | Woodcutter 가동 | 1×1 | Wood 15 |
| 2 | Stockpile(돌 더미) | Stone Cap +80 | 1×1 | Stone 10 |
| 3 | MiningCart(광차) | Miner 가동 | 1×1 | Wood 20, Metal 5 |
| 4 | Hearth(화덕) | 요리·Food 품질 보존 | 1×1 | Stone 20 |
| 5 | DryingRack(건조대) | Hunter 가동 | 1×1 | Wood 20 |
| 6 | MerchantStall(노점 판매대) | 기초 상점 UI 오픈 | 1×1 | Wood 25 |
| 7 | TownPost(마을 표지판) | 필요도 가중치 +10, Tier 판정 코어 | 1×1 | Wood 30, Stone 15 |
| 8 | Bed × 추가 | 인구 8까지 수용 | 1×1 | Wood 10/개 |

→ 이 Stage 종료 시 **외곽 벽 건설이 해금**된다 (§4).

### 3.3 Stage 2 → Stage 3 (Village → Town)
인구 목표 15~20명 / 테마: **전문 오브젝트 + 1차 방어**

| # | 오브젝트 | 기능 | 크기 | 자원 |
|---|---------|------|------|------|
| 1 | **Palisade 외곽 구축** | 1차 방어선 (§4.2) | 타일당 | Wood 8/타일 |
| 2 | **PalisadeGate × 2** | 관문 | 1×1 | Wood 40/개 |
| 3 | Furnace(화로) | Blacksmith 1단계 | 1×1 | Stone 40, Wood 30 |
| 4 | Anvil(모루) | Blacksmith 2단계 (장비 강화 UI) | 1×1 | Stone 20, Metal 15 |
| 5 | QuenchVat(물통) | Blacksmith 3단계 (제작 품질 ↑) | 1×1 | Wood 15, Metal 5 |
| 6 | InnBed(여관 침대) | 플레이어 세이브/휴식 | 1×1 | Wood 20, Cloth 3 |
| 7 | Shrine(소형 제단) | 버프, 평판 | 1×1 | Stone 30 |
| 8 | SignalBrazier(봉홧대) | 야간 시야, 몬스터 접근 경보 | 1×1 | Wood 20, Metal 5 |

→ 팰리세이드 완성 후 돌담 업그레이드 해금.

### 3.4 Stage 3 → Stage 4 (Town → City)
인구 목표 25~30명 / 테마: **돌 성벽 + 도시 기능**

| # | 오브젝트 | 기능 | 크기 | 자원 |
|---|---------|------|------|------|
| 1 | **StoneWall 교체** | 내구 +400, 원거리 몬스터 차단 | 타일당 | Stone 20/타일 |
| 2 | **StoneGate** | 대형 관문 | 1×1 | Stone 80, Metal 20 |
| 3 | **WatchTower**(망루) | 궁수 NPC 배치, 사거리 방어 | **2×2** | Stone 100, Wood 40 |
| 4 | WeaponRack(무기 거치대) | Guard 직업 활성화 | 1×1 | Wood 20, Metal 10 |
| 5 | Altar(대제단) | 고급 버프/부활 | 1×1 | Stone 120 |
| 6 | Bookshelf + Desk | 스킬/퀘스트 해금 (세트 조합) | 1×1 | Wood 60, Gold 200 |
| 7 | TradeLedger(거래 장부) | 원거리 교역 | 1×1 | Wood 40, Gold 100 |

예외: **WatchTower만 2×2 멀티 타일**. 나머지는 전부 1×1.

---

## 4. 외곽 방어 — 벽의 진화

### 4.1 3단계 벽 업그레이드

```
Stage 1: 표지석  →  Stage 3: 나무 울타리   →  Stage 4: 돌담 + 망루
(경계 표시만)       (Palisade)                (StoneWall + WatchTower)
내구: N/A           내구: 100/세그먼트         내구: 500/세그먼트
비용: 0             비용: Wood 8/타일         비용: Stone 20/타일
```

### 4.2 벽 배치 알고리즘 (`System_VillageWallPlanner`)

1. **경계 산출**: `VillageComponent.Bounds`의 볼록껍질 또는 단순 Rect를 외곽선으로
2. **벽 후보 타일 수집**: 경계선에 접한 빈 타일 목록
3. **게이트 우선 배치**: 마을 중심에서 가장 가까운 도로/입구와 겹치는 타일을 Gate로 예약 (방위당 1개까지)
4. **세그먼트 단위 생성**: 게이트 사이를 Wall 세그먼트로 쪼개 `ConstructionTaskComponent` 큐에 추가
5. **건설 우선순위**: 몬스터 침입 방향 > 플레이어 주된 접근 방향 > 나머지
6. **부분 완성 허용**: 한 번에 전부 못 지어도 경계의 10% 이상만 벽으로 닫혀도 "일부 방어" 효과 발동

### 4.3 벽 효과 (게임플레이 반영)
- **Palisade**: 몬스터 이동 경로 차단 (경로탐색 비용 +∞). 원거리 몬스터는 관통.
- **StoneWall**: 일반 몬스터 관통 불가, 파괴 시 세그먼트 HP 감소
- **WatchTower**: Guard NPC 공격 사거리 +3, 배후 시뮬레이션에서도 야간 몬스터 출현 확률 감소

### 4.4 벽 재건/수리
- HP 0 시 세그먼트 전체가 **폐허(Rubble)** 상태 → 해당 타일 통과 가능
- 마을 필요도 스코어에 "벽 결손률" 포함 → 자재만 있으면 자동 복구

---

## 5. 오브젝트 세트와 필요도 스코어

### 5.1 오브젝트 세트 판정

기능은 오브젝트 단독이 아니라 **세트 조합**으로 열린다:

| 기능 | 필요 오브젝트 세트 | 판정 범위 |
|------|-------------------|-----------|
| 주거 1인 | Bed × 1 | 마을 전체 (각 Bed = 주거 슬롯 1) |
| 대장간 기초 | Furnace | 같은 타일 그룹 (5×5) |
| 대장간 강화 서비스 | Furnace + Anvil | 같은 타일 그룹 (5×5) |
| 대장간 제작 품질 보너스 | Furnace + Anvil + QuenchVat | 같은 타일 그룹 (5×5) |
| 여관 | InnBed(1개 이상) + Hearth | 마을 전체 |
| 도서관/길드 | Bookshelf + Desk | 같은 타일 그룹 (5×5) |
| 출생 조건 | Bed × 2 + Hearth | 같은 타일 그룹 (3×3) |

→ 판정 로직은 `VillageManager`에 `HasObjectSet(villageId, ObjectSetType)` API 하나로 통합.

### 5.2 필요도 스코어 (VILLAGE_EXPANSION_DESIGN §3.3 확장)

`System_VillageNeedsEvaluation`이 게임시간 2h마다 재계산:

```
필요도(오브젝트) =
    BaseWeight (테이블)
  + (인구 - 보유_Bed수) × 50              // 주거 결손
  + (식량_하루소비 - 식량_하루생산) × 40  // 식량 결손 → CropPlot, DryingRack 우선
  + (자원_Cap_초과량) × 0.3               // 잉여 자재 → 오브젝트 배치 유도
  + (벽_결손률 × 위협도 × 30)             // 외벽 필요성
  + (직업_수요 - 직업_현재수) × 15        // 일자리 결손
  + 세트_완성_보너스 × 20                  // 이미 Furnace 있으면 Anvil의 스코어 급상승
  + 지도자_성향_보너스
  - 자원_부족_페널티 × ∞                  // 자재 없으면 후보 탈락
```

- **세트 완성 보너스**: 세트의 일부가 이미 있으면 나머지 오브젝트의 BaseWeight에 가산 → 완성 유도
- **위협도**: 최근 N시간 마을 근처 몬스터 스폰/전투 수의 지수이동평균
- **지도자**: `Loyalty` 최고 NPC의 성향(Courage↑ → 벽 가중, Greed↑ → 상점 가중)

---

## 6. 인구 증가 규칙

### 6.1 자연 증가 (이민)
게임 시간 24h마다 1명 확률 스폰:
- **미사용 Bed ≥ 1**
- 식량 저장량 ≥ 인구 × 5
- 마을 `Stage` ≥ Hamlet
- 확률: 기본 20% + (마을 명성 × 1%)  ※ 명성 = Tier × 10 + 기증 누적

### 6.2 출생 (Tier 3+)
- **Bed × 2 + Hearth가 같은 타일 그룹(3×3 이내)** 조건 만족 시 커플 NPC 스폰 확률
- 자녀는 성장 기간 후 일반 NPC 전환 (단순화: 즉시 성인)

### 6.3 이탈
- 식량 부족 기간 누적 → 이탈 확률 상승
- `Loyalty` 수치가 낮은 NPC부터 순차 이탈
- 이탈 NPC는 `NpcSaveData.Condition = Migrated`로 표시, 마을 인구 제외

### 6.4 분가 (City → 신규 Seed)
- 인구 > 수용상한 + 여유버퍼
- 근처 빈 청크(직선거리 ≥ N타일) 탐색 → **Bedroll + Campfire만 들고** 새 Settlement 시드로 NPC 1명 파견
- 신규 마을은 Stage 0부터 다시 시작

---

## 7. NPC 행동 — 제작/배치 사이클

Gatherer/Builder NPC 1명의 하루 루틴 예:

```
06:00  기상 (Bed에서 일어남)
06:30  WorkQueue 조회 → 근처 ObjectPlacementTask 선택
07:00  자원 Reservation 확인
       → 자재 부족이면 채집 서브태스크로 전환
07:00~10:00  Woodpile/Stockpile에서 자재 픽업 → 배치 지점 운반
10:00~13:00  오브젝트 제작 (Progress += delta)
13:00  식사 (Food -1, Hearth 또는 Well 근처 선호)
13:30~19:00  다음 오브젝트 or 벽 세그먼트
19:00  귀가 (Bed 근처) → 수면 → 06:00까지 대기
```

- 배후 시뮬레이션 시: 이 루틴을 "시간당 Progress +X" 한 줄로 압축 (§8)
- 전투 발생 시 도주, Guard 직업 + WeaponRack 보유 NPC만 교전

---

## 8. 배후 시뮬레이션 (청크 비활성 상태)

`System_AbstractVillageSimulation`이 게임 시간 10분(추상 틱)마다:

```csharp
for each Village v in inactive chunks:
    // 자원 생산/소비 일괄
    v.Resources[Food]  += (passive_food + activeBonus) * v.Population
    v.Resources[Wood]  += ...
    v.Resources[Food]  -= consumePerNpc * v.Population

    // 오브젝트 제작 진행 일괄
    for each ObjectPlacementTask t in v.Tasks:
        t.Progress += craftSpeedPerWorker * assignedWorkers
        if t.Progress >= 1.0f:
            FinalizeObjectPlacement(v, t)

    // 인구/승격 판정
    EvaluatePopulation(v)
    EvaluateTierUp(v)
```

- 복귀 시 청크 활성화 → 추상 상태를 구체 엔티티로 **복원** (배치된 오브젝트는 타일맵에 이미 기록됨)
- **공정성**: 추상 틱의 생산량 = 구체 시뮬 평균 × 0.7

---

## 9. 신규 테이블 / 컴포넌트

### 9.1 테이블

**`ObjectTable`** (신규):
```
Id, Name, Category, RequiredStage
RequiredWood, RequiredStone, RequiredMetal, RequiredFood
CraftTime, TileWidth (기본 1), TileHeight (기본 1)
StorageCap_Wood/Stone/Food/Metal   // 저장 오브젝트용
ProduceType, ProducePerHour         // 생산 오브젝트용
ProvidedService (bitmask)           // Shop/Forge/Inn/Shrine/Guard/Housing/…
AssociatedJob                       // 사용 직업
ObjectSetTag                        // "Blacksmith"/"Library" 등 세트 소속
BaseWeight
PrefabKey                           // Addressable
DestructionHP
```

Category 예: `Housing / Storage / Production / Crafting / Service / Defense / Decor`.

**`VillageTable`** (기존 확장):
- `InitialObjects` (Bedroll, Campfire 등 시드 목록)
- `BoundsRadius`, `MaxBoundsRadius`
- `PassiveProductionMultiplier`

### 9.2 컴포넌트

| 컴포넌트 | 용도 |
|----------|------|
| `VillageComponent` | Tier, Bounds, Population, PlacedObjectList, ThreatLevel |
| `VillageStorageComponent` | Resources + Cap |
| **`PlacedObjectComponent`** (구 BuildingComponent) | ObjectTableId, VillageId, TilePosition, HP, IsUnderConstruction, UsingNpcEntityId |
| `ObjectPlacementTaskComponent` | TargetTableId, TilePosition, Progress, AssignedNpcEntityId, ResourcesReserved |
| `NpcAssignmentComponent` | CurrentTaskEntityId, TaskType |
| `WallSegmentComponent` | Orientation, ConnectedGateId, SegmentHP |

### 9.3 시스템

| 시스템 | 주기 | 책임 |
|--------|------|------|
| `System_VillagePassiveProduction` | 1h | 패시브 자원 생성 |
| `System_VillageNeedsEvaluation` | 2h | 필요도 스코어 → 배치 큐 |
| `System_VillageWallPlanner` | 6h | 외곽 벽 세그먼트 계획 |
| `System_NpcTaskAssignment` | 1h | 유휴 NPC 할당 |
| **`System_NpcCrafting`** (구 NpcConstruction) | FixedUpdate | 오브젝트 제작/배치 진행 |
| `System_VillageTierProgression` | 4h | Tier 승격 판정 |
| `System_AbstractVillageSimulation` | 10분 | 비활성 청크 일괄 |

---

## 10. 구현 Phase

### Phase A — 자가 생성 최소 루프 (MVP)
- [ ] `System_VillagePassiveProduction`
- [ ] `VillageStorageComponent` (기존 `VillageData.Resources`를 ECS로 승격)
- [ ] 디버그 UI: 마을 클릭 시 자원/인구/Tier 표시
- [ ] Stage 0 시드: `EnsureVillagePopulated` 성공 시 Bedroll + Campfire 자동 배치

### Phase B — 오브젝트 배치 (핵심)
- [ ] `ObjectType` enum 확장 (§12.2)
- [ ] `ObjectTable` 정의 + 구글 시트
- [ ] `ObjectPlacementTaskComponent` + `System_NpcCrafting`
- [ ] 하드코딩 로드맵: Bed → Woodpile → CropPlot → Chest → Bed 2 → Well
- [ ] 배치 위치 자동 탐색 (중심 나선형)
- [ ] 기존 `MapManager.PlaceObject`(1×1) 그대로 사용

### Phase C — Tier 승격 + 벽
- [ ] `System_VillageTierProgression`
- [ ] 경계(`VillageComponent.Bounds`) 타일 계산
- [ ] `System_VillageWallPlanner` + Palisade 건설
- [ ] Palisade/StoneWall RuleTile 에셋 제작
- [ ] `WallSegmentComponent` 세그먼트 HP 관리

### Phase D — 플레이어 이득 (루프 완성)
- [ ] 오브젝트 세트 판정 `VillageManager.HasObjectSet`
- [ ] `ProvidedService` 플래그 기반 서비스 UI 오픈
  - MerchantStall 근처 → 상점 UI
  - Furnace + Anvil 근처 → 강화 UI
  - InnBed + Hearth 근처 → 여관 UI
- [ ] 기증 UX + 자원 변환 (`ItemTable.ResourceType`)

### Phase E — 배후 시뮬레이션
- [ ] `System_AbstractVillageSimulation`
- [ ] 인구 자연 증가 / 이탈
- [ ] 분가 (City → 신규 Seed)

### Phase F — 위협과 반응
- [ ] 몬스터 침공 이벤트, ThreatLevel 상승
- [ ] Guard NPC + WeaponRack + WatchTower 조합 → 사격 AI
- [ ] 오브젝트/벽 파괴 → 잔해 → 자동 복구

### Phase G — WatchTower (2×2 예외 처리)
- [ ] 멀티 타일 앵커/풋프린트 헬퍼 `MapManager.PlaceMultiTileObject`
- [ ] 2×2 풋프린트 + 파사드 GameObject 하이브리드 렌더

---

## 11. 결정 필요 이슈

1. **첫 NPC 고정 vs 랜덤** — 마을마다 Founder를 `VillageTable.FounderNpcId`로 지정할지, 랜덤으로 뽑을지.
2. **시드 스폰 타이밍** — 플레이어가 마을 입장해야 시드 생성인지, 월드 로드 시 전부 생성인지.
3. **오브젝트 인접성 판정 거리** — "대장간 = Furnace + Anvil 같은 위치" 판정을 3×3 / 5×5 / 마을 전체 중 어디로? (§5.1은 5×5 가정)
4. **오브젝트 재배치 가능 여부** — NPC가 이미 배치된 오브젝트를 자원 최적화 위해 옮길 수 있는지, 영구 고정인지.
5. **제작 속도 단위** — "실시간 N분 = 오브젝트 1개" 기준선 결정 필요.
6. **벽/오브젝트 파괴 시 자재 환불** — 잔해에서 20~30% 회수 허용 여부.
7. **플레이어 시점 동기화** — 외출 후 복귀 시 "이 오브젝트가 생겼다" 알림/타임랩스 vs 조용히 반영.

---

## 12. 타일 표현 방식

### 12.1 기본 원칙
**모든 오브젝트는 기본 1×1 타일 1오브젝트.** 현재 `MapManager` 시스템을 그대로 사용.

예외는 둘뿐:
- **WatchTower** (2×2) — 풋프린트 점유 + 파사드 GameObject 하이브리드
- **Wall** — 개별 타일 단위 ObjectType이지만 RuleTile로 연결 스프라이트 자동 선택

### 12.2 ObjectType enum 확장

현재 4종(`None/Stone/Npc/WoodWall`)을 다음과 같이 확장. 10비트 제한 내 여유 충분:

```
// 기존
None = 0, Stone = 1, Npc = 2, WoodWall = 3

// 주거·생활
Bedroll = 10, Bed = 11, Campfire = 12, Hearth = 13, Well = 14

// 저장
Woodpile = 15, Chest = 16, Barrel = 17, Stockpile = 18, Strongbox = 19

// 생산
CropPlot = 20, ChoppingBlock = 21, MiningCart = 22, DryingRack = 23

// 대장간
Furnace = 30, Anvil = 31, QuenchVat = 32, WeaponRack = 33

// 서비스
MerchantStall = 40, Shrine = 41, Altar = 42
Bookshelf = 43, Desk = 44, TradeLedger = 45
TownPost = 46, SignalBrazier = 47, InnBed = 48

// 벽 (RuleTile)
Palisade = 60, PalisadeGate = 61, StoneWall = 62, StoneGate = 63

// 멀티 타일 (예외)
WatchTower_Anchor = 70, WatchTower_Footprint = 71

// 제작 중 시각
CraftingSite = 80, CraftingFrame = 81, CraftingShell = 82

// 경계 표시
BoundaryMarker = 90
```

### 12.3 오브젝트 → 엔티티 연결

타일에는 ObjectType만 기록(기존 ulong 시스템). 상태(HP, 사용중 NPC, 수리 필요 등)는 **PlacedObjectComponent 엔티티**가 담당:

```csharp
public struct PlacedObjectComponent
{
    public int ObjectTableId;
    public int VillageId;
    public Vector2Int TilePosition;
    public int HP;
    public bool IsUnderConstruction;
    public int UsingNpcEntityId;  // -1 = 미사용
}
```

타일 → 엔티티 역참조 (활성 청크에서만 사용):
- `VillageManager` 또는 신규 `PlacedObjectRegistry`에 `Dictionary<Vector2Int, int>` 보관

### 12.4 배치 흐름

```
1. System_NpcCrafting이 ObjectPlacementTask.Progress를 1.0까지 진행
2. 완료 시 FinalizeObjectPlacement() 호출
   a) MapManager.PlaceObject(x, y, ObjectTable.ObjectTypeId)
      → ulong ObjectLayer에 id 기록, Blocked 플래그 설정 (Walkable 오브젝트는 제외)
   b) PlacedObjectComponent 엔티티 생성 + _objectTileMap 등록
   c) Addressable로 프리팹 로드 → 타일 위치에 GameObject 인스턴스화
3. MapFileData._objectList 업데이트 (영구 세이브)
```

### 12.5 제작 중 시각 표현

Progress에 따라 타일 스프라이트만 교체 (`PlaceObject`로 덮어쓰기):

| Progress | 스프라이트 | ObjectType |
|----------|-----------|------------|
| 0.0~0.3 | 재료 무더기 | CraftingSite |
| 0.3~0.7 | 반조립 골조 | CraftingFrame |
| 0.7~1.0 | 거의 완성 | CraftingShell |
| 1.0 | 완성된 오브젝트 | 실제 ObjectType |

### 12.6 벽 (RuleTile 활용)

- Palisade/StoneWall을 Unity `RuleTile`로 구현 → 이웃 타일 확인 후 직선/코너/T자 스프라이트 자동
- Gate도 같은 라인으로 인식하도록 룰 작성 (Gate 옆 Wall이 자연스럽게 연결)
- HP는 타일 단위가 아니라 **세그먼트(연결된 벽 덩어리)** 단위로 관리 → `WallSegmentComponent`

### 12.7 예외: WatchTower (2×2)

- 앵커 타일: `WatchTower_Anchor` (좌하단)
- 나머지 3타일: `WatchTower_Footprint` (Blocked만 담당, 시각 없음)
- 렌더: 2×2 영역 위에 **GameObject 1개** (Addressable 프리팹)
- 현재 시스템에 `PlaceMultiTileObject(anchor, width, height, tableId)` 헬퍼만 추가하면 충분 (Phase G)

### 12.8 경계(Bounds) 시각화

- Stage 0: `BoundaryMarker` 타일 4~8개를 경계선 샘플로 배치
- Stage 3+: 실제 벽이 생기면 벽이 곧 경계
- 에디터 기즈모: `VillageComponent.Bounds`를 원/다각형으로 렌더 (디버그 옵션)

### 12.9 걷기/통과 가능 오브젝트

`CustomTile.IsWalkable`을 ObjectType별로 설정:
- Walkable: CropPlot, BoundaryMarker, CraftingSite(제작 중 NPC 통과용)
- Blocked: Bed, Furnace, Anvil, Wall, 등 대부분

→ 기존 `MapManager.IsWalkable()`이 `CustomTile.IsWalkable` 이미 확인하므로 **코드 변경 불필요**. CustomTile 에셋 설정만 잘 하면 됨.

### 12.10 기존 시스템 연동

- **경로 탐색**: `IsWalkable()` 그대로 → 오브젝트/벽 자동 장애물화
- **몬스터 스폰**: 마을 경계 내 `MonsterSpawn` 플래그는 오브젝트 배치 시 clear (마을 내부 몬스터 스폰 방지)
- **세이브**: `MapFileData._objectList` + `TileModification` 그대로 사용. `PlacedObjectComponent.HP`만 별도 세이브 필드에 직렬화

### 12.11 장점 요약 (건물 방식 대비)

1. **기존 1×1 시스템 100% 재활용** — 신규 타일 인프라 거의 없음
2. **세분화된 진행도** — Furnace만 있고 Anvil 없으면 "대장간 1단계" 상태로 진행도 표현
3. **파괴 유연성** — 모루만 부서져도 화로는 생존 → 자연스러운 피해 복구 서사
4. **NPC 배치 유연** — 빈 타일만 있으면 어디든 배치, 경로도 NPC가 알아서 우회
5. **시각 자산 단위 작음** — 스프라이트 1장 = 오브젝트 1개 → 제작·업데이트 빠름
6. **멀티 타일은 WatchTower 하나만** → 복잡한 건물 시스템을 뒤로 미룰 수 있음 (Phase G)

---

## 13. 한 줄 요약

> **"정착민 1명이 24시간 안에 침대와 텃밭을 만들고, 일주일 안에 이웃과 화덕을 나누고, 한 달 안에 화로·모루·노점을 갖춘 울타리 마을로 자라며, 한 달 반이면 돌담 위 망루에서 밤을 밝힌다."**
> 이 체감을 **"벽 + 기능 오브젝트 조합"**으로 만들어낸다.
