# NPC 자율 마을 확장 시스템 기획

플레이어 전투 → 전리품 기증 → NPC 자율 건설 → 마을 발전 → 플레이어 이득의 순환 루프를 만드는 시스템.

---

## 1. 핵심 루프

```
[전투] 플레이어가 몬스터 처치, 전리품 획득
   ↓
[기증/판매] NPC 또는 마을 창고에 아이템 전달
   ↓
[자원 변환] 아이템이 마을 자원(목재/석재/식량/금화/특수)으로 환산
   ↓
[자율 판단] 마을이 필요도를 계산, NPC가 적합한 작업 선택
   ↓
[건설/생산] NPC가 타일 위에 건물·벽·설비 배치·업그레이드
   ↓
[기능 해금] 상점 확장, 공방 강화, 방어력 상승, 버프 서비스
   ↓
[플레이어 이득] 더 강한 장비·안전한 거점·새 퀘스트
   ↓  (루프 반복)
```

설계 원칙:
- **플레이어는 지시하지 않는다.** 자원만 공급하고, 어떤 건물을 지을지는 NPC/마을이 결정.
- **모든 결정은 데이터 기반.** 마을 상태 + NPC 직업/성향 → 우선순위 스코어.
- **배후 시뮬레이션 가능.** 플레이어가 없어도 마을이 계속 발전 (DF 방식).

---

## 2. 자원 시스템

### 2.1 마을 자원 종류

| 자원 | 용도 | 획득처 |
|------|------|--------|
| 목재 | 기본 건물, 가구 | 나무 드랍, 숲 NPC 채집 |
| 석재 | 성벽, 상위 건물 | 광산 드랍, 광부 NPC 채집 |
| 금속 | 공방, 무기 제작 | 몬스터 드랍, 광물 |
| 식량 | 인구 유지, 축제 | 몬스터 고기, 농장 |
| 금화 | 상점·교역 확장 | 판매 대금, 보스 드랍 |
| 특수 자원 | 고급 건물·버프 해금 | 희귀 드랍 (보스 코어 등) |

### 2.2 아이템 → 자원 변환

- `ItemTable`에 `ResourceType` + `ResourceValue` 필드 추가
- 예: "철 광석" → Metal +5, "몬스터 가죽" → Food +2, "골드" → Gold +N
- 플레이어가 창고 UI에서 아이템 선택 후 기증/판매
  - **기증**: 평판/호감도 상승 + 자원만 반영
  - **판매**: 금화 획득 + 자원도 일부 반영 (상인 수수료)

### 2.3 창고 컴포넌트 (ECS)

```csharp
public struct VillageStorageComponent
{
    public int Wood;
    public int Stone;
    public int Metal;
    public int Food;
    public int Gold;
    // 특수 자원은 별도 Dictionary or 고정 슬롯
}
```

마을 엔티티(VillageEntity)에 붙는 컴포넌트. `VillageManager`가 EntityId로 접근.

---

## 3. NPC 자율 건설

### 3.1 직업 체계 확장 (`NpcJobComponent`)

기존 `JobType` enum을 확장:

| 직업 | 역할 |
|------|------|
| Builder | 건설 작업 (모든 건물 공통) |
| Woodcutter | 목재 채집 |
| Miner | 석재·금속 채취 |
| Farmer | 식량 생산 (농장 가동 시) |
| Merchant | 상점 운영 (거래 가격 영향) |
| Blacksmith | 무기/방어구 강화 서비스 |
| Guard | 성벽/망루 순찰, 마을 방어 |
| Priest | 버프 서비스, 신전 운영 |

NPC 스폰 시 `NpcTable`의 `JobType` 필드 or 마을 수요에 따라 배정.

### 3.2 건설 작업 큐

```csharp
public struct ConstructionTaskComponent
{
    public int VillageId;
    public int BuildingTableId;     // 지을 건물 테이블 ID
    public Vector2Int TilePosition;
    public float Progress;           // 0~1
    public int AssignedNpcEntityId; // -1 = 미할당
    public int RequiredWood, RequiredStone, RequiredMetal;
    public bool ResourcesReserved;
}
```

흐름:
1. **마을이 필요도 계산** → 가장 필요한 건물 선정 → `ConstructionTaskComponent` 엔티티 생성
2. **자원 예약** — 창고에서 해당 자원 차감·예약
3. **NPC 할당** — 근처의 유휴 Builder가 적합도(거리 + 성향) 순으로 picking
4. **작업 진행** — `System_NpcConstruction`이 매 틱 Progress 증가
5. **완료** — 건물 엔티티 생성, 타일맵에 반영, 과제 컴포넌트 제거

### 3.3 필요도 스코어링

마을 상태에서 매 N초마다 "다음 지을 건물" 결정:

```
필요도(건물) = 기본가중치
  + (현재_직업수 부족분 × 가중치)
  + (인구 - 주거용량) × α      // 집이 부족하면 집 건설
  + (식량 부족도) × β          // 식량 < 인구 → 농장
  + (마을_방어력 부족도) × γ   // 적 출현 시 성벽
  - 자원_부족_페널티
```

각 건물 템플릿(`BuildingTable`)에 `BaseWeight`, `Category`, `PopulationCapacity`, `RequiresTier` 등 정의.

### 3.4 성향의 영향

기존 `NpcStatComponent`가 행동에 영향:

| 성향 | 효과 |
|------|------|
| Courage | 위험 지역(성벽 외곽) 근무 수락 확률 |
| Greed | 거래 가격 가산, 기증 호감도 감소폭 |
| Loyalty | 마을 이탈 확률, 퀘스트 수락률 |
| Patience | 장시간 건설 작업 집중도 |
| Curiosity | 새 건물 유형 시도 확률 |
| Friendliness | 플레이어 기증 시 보상률 |

### 3.5 건설 위치 결정

타일 기반이므로:
- **마을 경계**는 `VillageComponent.Bounds`(Rect)로 정의
- 후보 타일 탐색: 경계 내 빈 타일 중
  - 기존 건물과 인접 (도로 연결성)
  - 필요 지형 조건 만족 (농장=평지, 광산=돌 인접)
  - 통행 경로 차단하지 않음
- 점수 계산 후 최고점 타일 선택

---

## 4. 마을 발전 Tier

| Tier | 이름 | 조건 | 해금 |
|------|------|------|------|
| 1 | Hamlet | 인구 3+, 기본 집 2채 | 기초 상점 |
| 2 | Village | 인구 10+, 식량 창고 | 대장간, 여관 |
| 3 | Town | 인구 25+, 성벽 완성 | 공방 강화, 교역로 |
| 4 | City | 인구 50+, 특수 자원 | 길드, 고급 퀘스트 |

Tier 상승은 `VillageComponent.Tier` 필드로 표현. 상위 Tier 해금 시 새 `BuildingTable` 카테고리가 건설 큐에 올라올 수 있음.

### 4.1 인구 증가

- 주거 용량에 여유 있고 식량 충분 → 일정 주기로 이민자 NPC 스폰
- `EntityFactory.CreateNpc()`로 생성 + `NpcSaveData` 등록
- 인구가 주거 용량 + 여유를 넘으면 **분가**: 가까운 빈 청크에 새 마을 시드 (Tier 1부터)

---

## 5. 플레이어 피드백 루프

단순히 "건물이 지어진다"가 아니라 **구체적 이득**이 있어야 함:

### 5.1 경제 루프
- **상점 확장**: 상점 Tier 상승 → 재고 증가, 고급 아이템 판매, 할인율
- **공방**: 대장간 건설 → 장비 강화·수리, 제작 아이템 해금
- **교역**: Tier 3+ 마을 간 교역로 → 멀리 있는 자원 획득 가능

### 5.2 거점 루프
- **성벽/망루**: 몬스터 침입 방어, 플레이어 원거리 귀환 시 마을 생존
- **창고**: 플레이어 개인 보관함 용량 증가
- **여관**: 휴식 시 버프, 세이브 포인트

### 5.3 관계 루프
- **호감도**: 기증 누적 → NPC 개별 호감도 상승
- **추종자**: 고호감 + 특정 직업 NPC가 일정 시간 동행 동료
- **퀘스트 해금**: Tier/호감도 조건 충족 시 고유 퀘스트

### 5.4 기능 서비스
- **신전**: 버프/부활
- **도서관**: 스킬·레시피 학습
- **길드**: 의뢰 보드

---

## 6. 기존 시스템과의 통합

### 6.1 청크 시스템
- 마을은 여러 청크에 걸칠 수 있음 → `VillageComponent.ChunkList`
- 플레이어가 멀어지면 **추상 시뮬레이션 모드**로 전환:
  - 고해상도 개별 NPC 행동 중지
  - N초마다 "추상 틱" — 자원 생산량/소비량 일괄 계산, 건설 진행도 일괄 증가
  - 플레이어가 돌아오면 결과만 반영한 상태로 활성화

### 6.2 NpcManager 연동
- `NpcSaveData`에 추상 시뮬레이션용 필드 추가: `AssignedTaskId`, `LastTickTime`
- 청크 비활성화 중에도 `NpcManager.TickAbstractSimulation()`이 주기적으로 호출되어 세계 진행

### 6.3 MapFile / 타일맵
- 건설 완료 시 `MapFileData._objectList`에 `ObjectType.Building` 추가
- 타일 레이어(`TileLayer.Wall`, `TileLayer.Floor`)에 반영
- 세이브 시 `MapFileSaver`가 자동 보존

### 6.4 새로 추가할 ECS 구성

**컴포넌트:**
- `VillageComponent` — Tier, Bounds, Population, BuildingList
- `VillageStorageComponent` — 자원
- `BuildingComponent` — BuildingTableId, VillageId, HP, IsUnderConstruction
- `ConstructionTaskComponent` — 건설 중 상태
- `NpcAssignmentComponent` — NPC가 할당된 작업(건설/채집/경비)

**시스템:**
- `System_VillageNeedsEvaluation` — 마을 필요도 계산, 건설 과제 생성
- `System_NpcTaskAssignment` — 유휴 NPC에게 작업 할당
- `System_NpcConstruction` — 건설 작업 진행
- `System_NpcResourceGathering` — 채집 진행
- `System_VillageTierProgression` — Tier 승격 판정
- `System_AbstractVillageSimulation` — 비활성 청크 배후 시뮬레이션

**매니저:**
- `VillageManager` (이미 존재): 마을 목록, NPC-마을 매핑, 자원 API

---

## 7. 테이블 신규 항목

### 7.1 BuildingTable
```
Id, Name, Category, Tier, RequiredWood, RequiredStone, RequiredMetal,
ConstructionTime, TileWidth, TileHeight, PopulationCapacity,
ProvidedService, BaseWeight, RequiredVillageTier, PrefabName
```

`Category`: House / Farm / Shop / Workshop / Wall / Tower / Temple / ...
`ProvidedService`: 건물이 제공하는 기능 플래그

### 7.2 ItemTable 확장
- `ResourceType` (None/Wood/Stone/Metal/Food/Gold/Special)
- `ResourceValue` (int)
- `DonationFavor` (기증 시 호감도 가산치)

### 7.3 NpcTable 확장
- `JobType` 기본값 (이미 컴포넌트에 있음)
- `CanLearnJobs` (직업 변경 가능 여부)

---

## 8. 구현 단계

### Phase 1: 자원 & 기증 (기반)
- [ ] `VillageStorageComponent` + UI
- [ ] `ItemTable`에 자원 변환 필드
- [ ] 플레이어 → NPC/마을 기증/판매 UX
- [ ] 간단한 자원 시각화 (마을 창고 건물 클릭 시 팝업)

### Phase 2: 건설 시스템 (핵심)
- [ ] `BuildingTable` 정의 + 구글 시트
- [ ] `ConstructionTaskComponent` + `System_NpcConstruction`
- [ ] Builder NPC 직업 구현 (채집→운반→건설)
- [ ] 타일 배치 완료 후 `BuildingComponent` 엔티티 생성

### Phase 3: 마을 판단 (자율성)
- [ ] `System_VillageNeedsEvaluation` 필요도 스코어링
- [ ] 건설 위치 자동 탐색
- [ ] Tier 체계 + 승격 판정

### Phase 4: 플레이어 이득 (루프 완성)
- [ ] 상점 확장 (재고/가격)
- [ ] 대장간 서비스 (강화/제작)
- [ ] 성벽 방어 효과
- [ ] 호감도 시스템

### Phase 5: 배후 시뮬레이션
- [ ] 청크 비활성 상태 추상 틱
- [ ] 인구 증가 + 분가
- [ ] 멀리 있는 마을도 발전 지속

### Phase 6: 확장
- [ ] 마을 간 교역
- [ ] 추종자 시스템
- [ ] 축제/이벤트
- [ ] 몬스터 침공 이벤트 (성벽 효용 검증)

---

## 9. 열린 질문 (결정 필요)

1. **플레이어 간섭 정도** — NPC가 완전 자율인지, 플레이어가 "이 건물을 원한다"는 힌트(청사진)를 줄 수 있게 할지?
2. **건물 파괴** — 몬스터 침공으로 건물이 부서질 수 있는지, 부서지면 자원 손실 얼마?
3. **NPC 사망 처리** — 마을 NPC가 죽으면 재스폰되는지, 영구 손실인지?
4. **복수 마을** — 플레이어가 여러 마을을 돕는 구조인지, 하나에 집중하는지?
5. **적대 세력** — 다른 마을/팩션이 있어서 경쟁 구조가 있는지?
6. **직업 변경** — NPC가 시간이 지나면서 마을 수요에 따라 직업을 바꿀 수 있는지?
7. **시각화 수준** — 청크 밖 마을의 발전을 플레이어가 어떻게 인지하는지 (월드맵 아이콘? 소문?)

---

## 10. 레퍼런스

- **Rise to Ruins** — 구역 지정 → NPC 자율 건설 방식의 원조
- **Black & White 2** — 자원 공급 기반 자율 확장
- **Dwarf Fortress** — 배후 시뮬레이션, 성향/인간관계 심도
- **Kenshi** — ARPG와 결합한 팩션 자율 확장 (가장 가까운 선례)
