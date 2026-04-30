# Phase D — 플레이어 이득 + 마을 ↔ 플레이어 루프 완성 ✅ 코드 완료 (2026-04-28)

> 상위 문서: [VILLAGE_GROWTH_STAGES.md §10](VILLAGE_GROWTH_STAGES.md)
> 선행: [PHASE_A_DESIGN.md](PHASE_A_DESIGN.md) ✅ · [PHASE_B_DESIGN.md](PHASE_B_DESIGN.md) ✅ · [PHASE_C_DESIGN.md](PHASE_C_DESIGN.md) ✅
>
> **목표**: Phase A~C가 만든 "마을이 자가 성장한다"의 다음 한 걸음 — **플레이어가 마을과 상호작용해서 진짜 이득을 본다**. 마을은 서비스(상점/강화/여관/제단)를 제공, 플레이어는 모험에서 얻은 자원을 상점에 판다. 동시에 로드맵을 **필요도 스코어**로 교체하고, NPC가 **직업과 오브젝트로 직무를 갖게** 한다.

---

## 1. 범위

**Phase D가 한 것**:
1. **`PlacedObjectComponent` + Registry** — 배치 오브젝트의 위치/HP를 ECS로 승격, 마을별 `tableId → entityIds` + `tile → entityId` 인덱스
2. **세트 판정** — `VillageManager.HasObjectSet`, `SetMemberTag` 비트 + `ObjectSetCatalog` 단일 사전
3. **`ProvidedService` 비트마스크** — `BuildableItemTable.Function` 폐기, 정식 enum
4. **`System_VillageServiceProximity`** — 플레이어 근처 서비스를 0.3s 마다 `PlayerNearbyServicesComponent`로 집계
5. **서비스 UI 4종 스켈레톤** — Shop / Forge / Inn / Shrine. F키 입력 시 우선순위 라우팅
6. **상점 양방향 거래** — 매물 풀 + 인벤토리 매각 + 마을 자원 부분 환원
7. **필요도 스코어** — `System_VillageNeedsEvaluation` (2h). 로드맵은 fallback으로 유지
8. **배치 구역화** — 카테고리 클러스터 가산점 + 외곽 보호 마진 (비-Defense는 경계 안쪽 ≥ 2)
9. **NPC 직무 할당** — `System_VillageJobAssignment`, `JobBonusTable`로 직업 보너스 가산
10. **데이터 정본화** — TableId 분기 0건 (no hardcoded TableId)

**제외**:
- 배후 시뮬레이션, 출생/분가 → Phase E
- 위협도/몬스터 침공/벽 파괴 → Phase F
- 명성·자원 기증 시스템 → 폐기 (§2.5). 명성은 갱신 경로(퀘스트)와 함께 Phase F로
- WatchTower 2×2 → Phase G
- Stage 4 (City), StoneWall → Phase F
- NPC 일과 시뮬레이션 (수면/식사/일터 이동) → Phase E
- 장비 제작(재료 → 새 장비) → Phase F+. Phase D는 강화(재롤)만

> **한 줄 범위**: "플레이어가 상점·강화·여관을 쓰고, 자원/장비를 팔아 골드로 바꾸며, NPC들은 자기 직업의 작업 오브젝트 옆에서 일한다."

---

## 2. 핵심 설계 결정

### 2.1 `PlacedObjectTypeIds` 유지 + `PlacedObjectComponent` 양립

`PlacedObjectTypeIds`(List<int>, ID-only 누적)를 `List<PlacedObjectInfo>`(TableId+TileX+TileY)로 확장하면 Phase B/C 카운트 헬퍼 다수 + 마이그레이션 부담.

**결정**: 양립.
- `PlacedObjectTypeIds` — 카운트 전용, 세이브 정본 유지
- `PlacedObjectComponent` (ECS) — 위치/HP 등 런타임. 좌표는 `List<PlacedObjectSaveData>` 별도 직렬화
- 양쪽 동시 갱신은 `OnObjectPlaced` 한 곳에서

### 2.2 데이터 정본화 — No Hardcoded TableId

> 코드에 `if (id == 160)` 같은 TableId 분기를 두지 않는다. 의미는 모두 `BuildableItemTable` 컬럼에 박는다.

신규 컬럼 6개: `ProvidedService`(bitmask) · `Category` · `SetMembership`(bitmask) · `AssociatedJobType` · `BaseWeight` · `MaxPerVillage`. 구 `Function` 컬럼 폐기.

새 오브젝트 추가 시나리오: 시트 1행 + (필요 시) `SetMemberTag`/`ObjectSetCatalog` 한 줄 → C# 변경 0.

Phase C `System_VillageTierProgression`의 `BED_ID/TOWNPOST_ID/FURNACE_ID/ANVIL_ID/MERCHANTSTALL_ID` 상수 5개는 **Step 5b에서 모두 제거**, `CountByService`/`HasObjectSet`로 대체.

### 2.3 세트 정의 — `SetMemberTag` 비트 + `ObjectSetCatalog` 단일 사전

세트 구성도 코드 분기로 박지 않음. `SetMemberTag`로 "이 오브젝트가 어떤 세트의 어떤 부품"을 표현하고, **세트 정의(요구 비트, 거리)는 정적 사전 1곳**(`ObjectSetCatalog.All`).

```
ForgeBasic    = Forge_Heat (5×5)
ForgeStandard = Forge_Heat | Forge_Anvil (5×5)
ForgePremium  = Forge_Heat | Forge_Anvil | Forge_Quench (5×5)
Inn           = Inn_Bed | Inn_Hearth (마을 전체)
Birth         = Birth_Bed | Birth_Hearth (3×3) — Phase E
Library       = Library_Book | Library_Desk (5×5) — Phase F+
```

`HasObjectSet`은 영역 내 SetMember 비트 OR이 RequiredMask를 모두 덮는지 검사. 새 세트 추가는 비트 + 사전 한 줄 + 시트.

### 2.4 자원 활용 = 상점 판매 단일화 (기증 시스템 폐기)

| 안 | 흐름 | 단점 |
|----|------|------|
| (A) 기증 | 인벤토리 자원 → 마을 Storage 직접 가산 + 명성 | UI 2개, 마을 자원 가속 → 자가 성장 의의 약화 |
| (B) **상점 판매** ★ | 인벤토리 → Gold + 자원 부분 환원(50%) | 단순. 마을 Storage는 자가 누적 + 패시브 의존 유지 |

**채택: (B)**. 이유:
- F키 한 번에 들어가는 Shop UI에서 구매·판매 모두 처리
- 플레이어가 Wood 100을 들이부어 즉시 Stage 승격하는 단축 경로 차단 (자가 성장 가치 보존)
- 매각된 자원 50%(Cap 한도 내)가 마을 Storage에 환원 → "내가 판 자원이 마을에 남는다"는 약한 연결감 유지
- 명성(Reputation) 시스템은 갱신 경로(기증)가 사라지므로 **Phase F(퀘스트 보상)로 이관**. Phase D는 Stage(Tier×10) 기반 매력도만

### 2.5 매각가/환원 — `ItemTable` 컬럼 직접

신규 4컬럼: `BasePrice` · `SellRatioBp` (×100, basis points) · `ReturnResourceType` · `ReturnRatioBp`.

```
sellPrice    = BasePrice × SellRatioBp / 100
returnAmount = amount    × ReturnRatioBp / 100
```

카테고리 분기 코드 X. 시트 입력 가이드: 자원 50/장비 40/소비 40, Iron은 Stone 100% 환원(가치 반영), Quest/Currency는 0(매각 불가).

원자성: 인벤토리 차감 실패 시 다음 단계 진행 안 함. Cap 초과 환원분은 자동 폐기되지만 Gold는 정상 지급(플레이어 손해 없음).

### 2.6 필요도 스코어 — 로드맵 fallback 유지

`VillageBuildRoadmap`은 하드코딩 시퀀스라 "잉여 Stone이 있는데 Bed만 짓는" 비효율 발생.

**Phase D 전략**: 점수 기반 후보를 위에 얹고 로드맵을 fallback. NeedsEvaluation 후보 0건일 때(자원 모두 부족, Stage 데이터 부재) 안전망. Phase E에서 fallback 제거 예정.

#### 점수 함수 튜닝 (2026-04-28)

초안: `BaseWeight + 결손 + 세트 + 직업 수요`. → **CropPlot이 영원히 1위**가 되는 문제 (체감 부재).

수정: 두 가지 추가
- **체감(diminishing returns)**: 같은 타입 보유 개수당 -12점 → CropPlot/Bed 등 무한 1위 방지
- **식량 풍족 시 Production 페널티**: -15 → 후보 자연스럽게 순환

세트 완성 보너스는 **자동 계산** — `ObjectSetCatalog`를 순회해 "이 멤버 추가가 어떤 세트의 마지막 퍼즐인가"를 판단. 세트 추가 시 점수 함수 변경 불필요.

### 2.7 외곽 보호 마진 (Outskirt Protection)

비-Defense 오브젝트(상점/화로/여관/주거)가 마을 경계 바로 안쪽에 배치되면 플레이어가 마을 밖에서도 서비스에 인접할 수 있는 모호한 영역 발생 → `ServiceProximity`(§6)의 "마을 안일 때만" 보장이 깨진다.

**해결**: `OUTSKIRT_MARGIN_TILES = 2`. 비-Defense는 경계로부터 ≥ 2타일 안쪽, Defense(벽/게이트)는 정확히 경계 위. 외곽 1~2타일 띠는 **벽만 들어갈 수 있는 보호 영역**.

Settlement(boundsRadius=6, 비-Defense 가용 9×9=81타일)에서 빠듯할 수 있음 — 시드 오브젝트 정도는 충분하지만, 부족하면 Settlement만 margin=1로 완화 검토.

### 2.8 ServiceProximity는 단일 마을 한정

플레이어가 두 마을의 SpawnRadius에 동시에 들어가도 가장 가까운 1개만 잡는다. 마을 밖에서 서비스가 잡히지 않는 보장은 §2.7 외곽 마진과 자연스럽게 맞물림.

세트 서비스(Forge): `Furnace`만 있어도 UI 켜되 **`HasObjectSet(ForgeStandard)` 결과로 UI 내부 기능 단계 결정**. NearestForgeEntityId는 가장 가까운 Furnace.

### 2.9 Shrine 쿨다운은 엔티티 단위

`PlacedObjectComponent.LastUseGameTime` (= `PlacedObjectSaveData.LastUseGameTime`)에 저장. ECS 일관성 + 미래 확장성(Shrine 종류별 다른 쿨다운, Phase F 파괴/재건 시 자연 리셋). 어뷰징(같은 마을 다중 Shrine으로 쿨 우회)은 `BuildableItemTable.MaxPerVillage = 1`이 NeedsEvaluation 후보 필터에서 강제.

### 2.10 NPC 직무: 1:1 매칭, 보너스 가산만

NPC 1명 ↔ 작업 오브젝트 1개. Phase D MVP는 "할당만" — `PassiveProduction`이 컴포넌트를 보고 보너스 가산. 실제 이동/일과는 Phase E.

직업별 시간당 가산값은 신규 `JobBonusTable` 시트 1개 (5행: Woodcutter/Miner/Farmer/Hunter/Merchant). 새 직업 추가 = 시트 1행, C# 0줄.

### 2.11 카테고리 클러스터 가산점

`VillageTileFinder`에 카테고리 매개변수 추가. 후보 점수 = `-점유이웃 + ClusterBonus`. 같은 카테고리 인접 8방위당 +1, 다른 카테고리 -0.5.

효과: Forge가 모이면 "공방 거리", Bed들이 모이면 "주거 구역" 자연 발생. **단순 가산점이라 강제는 아님** — 빈 타일 부족 시 카테고리 무관하게 폴백.

---

## 3. 구현 결과 요약

### 정본 데이터 (시트 컬럼)

| 시트 | 변경 |
|------|------|
| `BuildableItemTable` | 신규 6컬럼: `ProvidedService`/`Category`/`SetMembership`/`AssociatedJobType`/`BaseWeight`/`MaxPerVillage`. 구 `Function` 코드 폐기. 시트 23행 입력 |
| `ItemTable` | 신규 4컬럼: `BasePrice`/`SellRatioBp`/`ReturnResourceType`/`ReturnRatioBp`. 시트 77행 입력 |
| `JobBonusTable` (신설) | 5행 — Woodcutter/Miner/Farmer/Hunter/Merchant |

### ECS 컴포넌트 + Registry

| 신규 | 용도 |
|------|------|
| `PlacedObjectComponent` | TableId/TileX/Y/HP + Service/SetMember 캐시 + LastUseGameTime |
| `PlayerNearbyServicesComponent` | 활성 서비스 비트 OR + Nearest{Shop/Forge/Inn/Shrine/Civic}EntityId |
| `NpcAssignmentComponent` | NPC ↔ 작업 오브젝트 1:1 결합 |
| `PlacedObjectRegistry` | 마을별 `tableId → entityIds` + `tile → entityId` 휘발성 인덱스 |
| `PlacedObjectSaveData` + `MerchantStockEntry` | 영구 세이브 + `VillageManager.Load` 마이그레이션 |
| `ObjectSetCatalog` | 6종 세트 정의 단일 사전 |

### API + 시스템

| 신규/확장 | 비고 |
|-----------|------|
| `HasObjectSet(setType, anchor?)` | `SetMemberTag` 비트 OR 검사 |
| `CountByService(service)` | Tier 승격 + Snapshot 등에서 사용. TableId 분기 0건 |
| `SellItemToMerchant` / `BuyItemFromMerchant` | `ItemTable` 컬럼 직접 적용 |
| `EnsureMerchantStockFresh` + `RollMerchantStock` | 게임시간 24h 게이트 |
| `OnObjectPlaced(tableId, tileX, tileY)` | Cap 가산 + ECS 엔티티 발급 + Registry 등록 |
| `System_VillageNeedsEvaluation` (Priority 61, 2h) | 점수 기반 후보. 체감/식량 풍족 페널티 포함 |
| `System_VillageServiceProximity` (Priority 61, IUpdate 0.3s) | 단일 마을 한정 |
| `System_VillageJobAssignment` (Priority 68, 1h) | `AssociatedJobType` × `NpcJobComponent.JobType` 직접 비교 |
| `System_VillagePassiveProduction` 확장 | `JobBonusTable` 가산 (Wood/Stone/Food/Iron/Gold) |
| `System_VillageTierProgression` 정리 | 상수 5개 + `CountInPlaced` 모두 제거. `CountByService` + `HasObjectSet`로 |
| `System_VillageBuildQueue` | NeedsCache 1순위 채택, 자원 부족 시 벽 fallback |
| `VillageTileFinder` 확장 | 외곽 마진 + 카테고리 클러스터 |

### UI + 입력

| 신규 | 비고 |
|------|------|
| `ServiceUIRouter` | F키 → Shop > Forge > Inn > Shrine 우선순위 라우팅 |
| `UIShopMerchant`/`UIForge`/`UIInn`/`UIShrine` | 스켈레톤 — Bind + 세트 단계 평가 + 쿨다운 + VillageManager API 연결 |
| `AddressablePath` 4개 키 | `UI/{ShopMerchant,Forge,Inn,Shrine}` |
| `System_Input` | `IsInteracting` + `AvailableServices != None` → `ServiceUIRouter.Open` |

### 디버그

새 로그 태그: `[Shop]` / `[Sell]` / `[Service]` / `[Needs]` / `[JobAssign]` / `[Forge]`. `VillageDebugLog.Snapshot` 확장 — `FormatTierCheck` 데이터 정본화, `FormatServices` (활성 서비스 비트 OR), `FormatJobs` (직무 할당률).

---

## 4. 잔여 작업

1. **Step U1** — Unity prefab 4종(`UI/{ShopMerchant,Forge,Inn,Shrine}`) + Addressable 등록 (사용자 작업)
2. **Step 10 후속** — prefab 부착 후 UI 슬롯 위젯 바인딩 본격 (현재 `RefreshAll` TODO만 남음)
3. **점수 함수 튜닝** — 디버깅 + 실 플레이 후 BaseWeight/페널티 가중치 조정

---

## 5. 한 줄 요약

> **Phase D는 마을이 만들어 낸 서비스가 플레이어에게 진짜 이득이 되게 만든다 — 상점에서 사고팔고, 화로에서 강화하고, 여관에서 자고, 제단에서 가호를 받는다. 그 사이 NPC들은 자기 직업의 작업 오브젝트 옆에서 일하기 시작한다.**
