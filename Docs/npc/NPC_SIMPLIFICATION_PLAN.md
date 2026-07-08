# NPC 단순화 & 마을–NPC 분리 — 구현 계획

> **한 줄 요약**: 마을에 종속된 NPC(상인·대장장이·작업자)와 NPC 기반 마을 발전(작업 생산·NPC 건설)을 걷어내고, **모든 물리 NPC는 모험가**로 단일화한다. 마을 경제(자원·Tier)는 물리 NPC 수가 아니라 **§0 추상 인구**에서 구동하고, 건물은 **자가 건설**된다.

**Status**: **구현 완료 (SP1~SP7, 2026-07-08)** — Unity 에디터 리컴파일/플레이 검증 대기 (§7 체크리스트). dotnet 빌드(Assembly-CSharp) 오류 0 확인. · **Last Updated**: 2026-07-08

> **구현 노트**:
> - 삭제 대상 파일 6종(`System_VillageJobAssignment` · `NpcJobComponent` · `NpcAssignmentComponent` · `NpcBuildAssignmentComponent` · `BuildStateHandler` · `System_VillagePopulation`)은 `.cs`+`.cs.meta` **삭제 완료** (2026-07-08).
> - **UIInn은 삭제하지 않고 휴식(+6h)/세이브 전용으로 축소 유지** (사용자 결정, 2026-07-08) — 방문자/고용 UI만 제거. ServiceUIRouter Inn 라우팅·B키 테스트 진입점 유지.
> - 정량화 상수는 `VillageManager.ABSTRACT_POP_DIVISOR(50)/ABSTRACT_POP_CAP(25)`로 공유 (NpcManager 스폰 수와 동일 소스).
> - Iron/Gold 인구 편입(D3)은 Stone과 같은 누적 카운터 방식: Iron 10h당 +1/인구, Gold 20h당 +1/인구 (초기 캘리브레이션, `VillageData.IronTimer/GoldTimer` 세이브).
> - `v.Population` sync는 System_SettlementSim(Priority 50) tick — 생산(52)/Tier(60)/건설(66)보다 항상 먼저.
> - JobBonusTable/VillageStageTable의 이민·고용 컬럼 등 데이터 레이어는 미삭제(무해) — 시트 정리는 후속.
**선행/관련**: [PROCEDURAL_HISTORY_DESIGN §0](../world_history/PROCEDURAL_HISTORY_DESIGN.md)(런타임 LOD·추상 인구) · [WORLD_HISTORY_PLAN §11](../world_history/WORLD_HISTORY_PLAN.md)(RL1-3 구현) · [NPC_ACTIVITY_DESIGN](NPC_ACTIVITY_DESIGN.md)(모험가 활동/원정) · [VILLAGE_PHASES](../village/VILLAGE_PHASES.md)

---

## 1. 배경 / 문제

현재 코드는 **"모든 NPC는 한 마을에 소속 + 마을이 NPC를 소유·집계 + NPC가 일해서 자원 생산 + NPC가 건물 건설"**로 강하게 결합돼 있다.

- 마을 경제(자원 생산·Tier 승격·식량·건설 동시성)가 [`v.Population`](../../Assets/Scripts/Village/VillageManager.cs#L220)에 물려 있고, 그 값은 **물리 NPC 수**(`NpcEntityIds.Count`)다.
- 자원 생산([System_VillagePassiveProduction](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs))이 인구 + **직업 배정 보너스**로 계산된다.
- 건물은 NPC가 배정받아 걸어가 짓는다([System_VillageBuildQueue](../../Assets/Scripts/Common/System/System_VillageBuildQueue.cs) + [BuildStateHandler](../../Assets/Scripts/AI/StateHandlers/BuildStateHandler.cs)).
- 여관 고용/이민([System_VillagePopulation](../../Assets/Scripts/Common/System/System_VillagePopulation.cs) `TickImmigration`, [UIInn](../../Assets/UI))으로 주민을 늘린다.

**목표 모델**: 마을은 **소유자가 아니라 인구(추상)를 가진 경제 단위 + 서비스 거점**. 물리 NPC는 **마을이 낳는 모험가**뿐이며, 마을 경제와 분리된다.

---

## 2. 핵심 결정 (확정)

| # | 결정 | 비고 |
|---|------|------|
| D1 | **모든 물리 NPC = 모험가.** 마을 종속 NPC(Anchored)·작업자(Worker) 개념 제거 | 이전 (b) Anchored/Free 분류안은 폐기 — 분류 자체가 불필요 |
| D2 | **마을 경제(`v.Population`)는 §0 추상 인구에서 소싱** (물리 NPC 수 아님) | 생산·Tier·식량·건설 캘리브레이션 유지(기존 pop/50·cap25 정량화 재사용) |
| D3 | **자원 생산은 인구 기반** — Iron/Gold도 직업보너스 대신 **인구 기반**으로 편입 | 직업 보너스 경로 제거 |
| D4 | **건물·벽 자가 건설** — NPC 배정 없이 타이머로 완성. **자원 소모는 유지**(경제 게이트) | |
| D5 | **모험가 스폰 = 마을 초기 1회 코호트.** 재스폰은 **보류**(초기 코호트로 재미 검증 먼저) | 상시 배출은 후속 |
| D6 | **이민 / 여관 고용 시스템 제거** | 주민 유입 기능 불필요 |
| D7 | **마을 폐허(Ruin) = 추상 인구 0.** 단 **구현 보류**(나중에) | 현재 물리-사망 기반 depletion은 제거하되 새 Ruin은 미구현 |

---

## 3. 아키텍처 원칙

### 3.1 인구(추상) vs 물리 NPC 분리
- **인구** = 정착지 정량값([§0 `SettlementLiveState.Pop`](../world_history/PROCEDURAL_HISTORY_DESIGN.md)). 마을 경제를 구동. RL1-3가 이미 시간에 따라 굴림.
- **물리 NPC** = 마을이 초기에 낳은 **모험가 코호트**. 경제와 무관, 월드를 돌아다님(이동은 후속 레이어).

### 3.2 `v.Population` 재배선 (핵심)
`v.Population`은 마을 경제의 **단일 허브**이고 **쓰기가 한 곳**뿐이라 재배선이 작다:
```
// 지금: VillageManager.RegisterNpcToVillage() → data.Population = data.NpcEntityIds.Count;   (물리 수)
// 변경: v.Population = Quantize(HistoryManager.GetSettlementPopulation(v.SettlementId));       (추상 인구)
//       Quantize = 기존 ComputeTargetNpcCount의 pop/50, cap25 정량화 재사용 → 생산/Tier 재튜닝 불필요
```
- `NpcEntityIds`는 유지하되 **역할 축소**: "여기서 태어난/현재 있는 물리 모험가" (backfill·UI·리스트 전용). **더 이상 경제를 구동하지 않음.**
- 갱신 시점: materialize 시 + 주기적 sync(예: [System_SettlementSim](../../Assets/Scripts/Common/System/System_SettlementSim.cs) 또는 생산 tick 진입 시). 로드 시 세이브값 신뢰하지 말고 재계산.

### 3.3 서비스는 이미 건물 기반 (변경 없음)
상점/대장간/여관/사당은 [System_VillageServiceProximity](../../Assets/Scripts/Common/System/System_VillageServiceProximity.cs)가 **배치 오브젝트(`PlacedObjectComponent.Service`) 근접**으로 연다. 거래도 마을 단위([BuyItemFromMerchant](../../Assets/Scripts/Village/VillageManager.cs)). → **상인 NPC 제거해도 상점·제작·거래 정상.** (건물이 무인일 뿐)

---

## 4. 제거 / 재작업 / 유지 맵

### 4.1 제거 (DELETE)
| 대상 | 파일 |
|------|------|
| NPC 직업↔작업오브젝트 매칭 시스템 | [System_VillageJobAssignment.cs](../../Assets/Scripts/Common/System/System_VillageJobAssignment.cs) + SystemManager 등록 |
| 직업/작업/건설 배정 컴포넌트 | [NpcJobComponent](../../Assets/Scripts/Common/Component/NpcJobComponent.cs) · [NpcAssignmentComponent](../../Assets/Scripts/Common/Component/NpcAssignmentComponent.cs) · [NpcBuildAssignmentComponent](../../Assets/Scripts/Common/Component/NpcBuildAssignmentComponent.cs) |
| NPC 건설 AI 상태 | [BuildStateHandler.cs](../../Assets/Scripts/AI/StateHandlers/BuildStateHandler.cs) + [AIBehaviorFactory](../../Assets/Scripts/AI/AIBehaviorFactory.cs) 등록 + AIState enum의 Build |
| 이민 | [System_VillagePopulation](../../Assets/Scripts/Common/System/System_VillagePopulation.cs) `TickImmigration` |
| 여관 고용/방문자 | NpcManager Inn region(`HireVisitor`/`SpawnVisitorNpc`/`EvictExpiredVisitors`/visitor 인덱스), [UIInn](../../Assets/Scripts/UI/UIInn.cs), `NpcStatus.InnVisitor` 경로 |
| 물리-사망 기반 마을 전멸 | NpcManager [UnregisterNpcByEntityId](../../Assets/Scripts/Manager/NpcManager.cs#L570)의 `DepletedAt`/`Ruin` 트리거 (D7 — 새 Ruin은 보류) |

### 4.2 재작업 (REWORK)
| 대상 | 변경 |
|------|------|
| [System_VillagePassiveProduction](../../Assets/Scripts/Common/System/System_VillagePassiveProduction.cs) | `AggregateJobBonus`(직업 보너스) 제거. Food/Wood/Stone + **Iron/Gold 모두 인구(`v.Population`) 비례**로 생산 (D3). `NpcEntityIds` 순회 제거 |
| [System_VillageBuildQueue](../../Assets/Scripts/Common/System/System_VillageBuildQueue.cs) | **자가 건설**: `AccumulatedHours`를 게임시간으로만 누적. NPC 배정/걸어가기/BuildState/`ReattachBuildAssignmentIfAny` 제거. 완성 시 건물 생성·자원 소모는 유지. 벽 세그먼트도 동일(같은 큐) |
| [ObjectPlacementTaskComponent](../../Assets/Scripts/Common/Component/ObjectPlacementTaskComponent.cs) | `AssignedNpcEntityId` 제거(+ `BuildTaskSnapshot`/`VillageManager.SyncTaskToData`/`RestoreTaskFromData`의 NPC 필드). 타이머·좌표·예약자원은 유지 |
| NpcManager [EnsureVillagePopulated](../../Assets/Scripts/Manager/NpcManager.cs#L287) / [SpawnDefaultNpcsForVillage](../../Assets/Scripts/Manager/NpcManager.cs#L371) | 초기 1회 코호트만 스폰(D5). 재스폰·이민 훅 제거. 스폰된 NPC는 **모험가**로 취급 |
| [VillageManager.RegisterNpcToVillage](../../Assets/Scripts/Village/VillageManager.cs#L211) | `Population = NpcEntityIds.Count` 제거. `v.Population`은 §0에서 소싱(3.2). NpcEntityIds 등록은 유지(코호트 명단) |
| SystemManager | 삭제 시스템 등록 해제. [System_VillagePopulation](../../Assets/Scripts/Common/System/System_VillagePopulation.cs)은 이민 제거 후 남는 책임 없으면 통째 제거 |

### 4.3 유지 (KEEP — 그대로 동작)
| 대상 | 이유 |
|------|------|
| [System_VillageTierProgression](../../Assets/Scripts/Common/System/System_VillageTierProgression.cs) | `Population >= PromoMinPopulation` — `v.Population`이 추상 소싱이라 그대로 동작(정량화 유지 시 테이블 불변) |
| [System_VillageServiceProximity](../../Assets/Scripts/Common/System/System_VillageServiceProximity.cs) + 상점/대장간/여관(건물) | 건물 기반이라 무영향(3.3) |
| [NpcRelationBackfill](../../Assets/Scripts/Npc/Relation/NpcRelationBackfill.cs) | 초기 코호트(D5)를 대상으로 그대로 — 함께 태어난 모험가들의 가족/관계 시드 |
| §0 RL1-3([System_SettlementSim](../../Assets/Scripts/Common/System/System_SettlementSim.cs) 등) | 추상 인구가 이제 `v.Population`의 입력원 — 오히려 명확한 소비처 확보 |
| 모험가 AI/전투/대화/inspect/호감도 | 물리 NPC(모험가)로 유지 |
| JobType | **flavor·장비 시드 용도로만 잔존**(Description 풀·`EquipTableId`). 기계적 직업 시스템은 제거 |

---

## 5. 단계별 작업

| Phase | 내용 | 규모 |
|-------|------|------|
| **SP1** | `v.Population` 재배선(§3.2) — 쓰기 1곳 제거 + §0 소싱 sync | S |
| **SP2** | 생산 인구 기반 통일(§4.2) — 직업보너스 제거 + Iron/Gold 편입(D3) | S |
| **SP3** | 자가 건설(§4.2) — BuildQueue/Task NPC 분리, BuildState·배정 컴포넌트 삭제 | M |
| **SP4** | 직업/작업자 시스템 삭제(§4.1) — JobAssignment·컴포넌트·AIState | S~M |
| **SP5** | 이민/여관 고용 제거(§4.1) — TickImmigration·Inn·Visitor 경로 | M |
| **SP6** | 스폰 정리(§4.2) — 초기 코호트=모험가, 재스폰/전멸 트리거 제거(D5·D7) | S~M |
| **SP7** | 문서 갱신(§9) | M |

> 권장 순서: **SP1 → SP2 → (SP4·SP3) → SP6 → SP5 → SP7.** SP1/SP2가 경제를 추상 인구로 옮겨 안정화한 뒤, 결합 제거(SP3·SP4)와 스폰/이민 정리(SP6·SP5)를 진행.

---

## 6. 보류 (Deferred — 이번 범위 밖)

- **마을 폐허(Ruin) 로직** — 규칙은 "추상 인구 0 → Ruin"으로 확정하되 **구현은 나중에**(D7). 현재는 물리-사망 depletion만 제거.
- **모험가 재스폰/상시 배출** — 초기 코호트로 재미 검증 후 추가(D5).
- **모험가 자율 이동/이주/탐험** — 오프스크린 이동 인프라(analytic-A) + 결정 레이어. §0 위에 얹는 별도 레이어([NPC_ACTIVITY_DESIGN](NPC_ACTIVITY_DESIGN.md)).
- **무인 서비스 건물 연출**(상점 주인 없음) — 필요 시 후속.

---

## 7. 검증 기준 (Unity 리컴파일 후)

- [ ] 마을에 물리 주민이 없어도(모험가만) **자원이 인구에 비례해 계속 생산**됨(식량/목재/석재/철/금).
- [ ] 물리 모험가가 죽거나 사라져도 **생산·Tier·식량이 변하지 않음**(경제=추상 인구).
- [ ] 건물·벽이 **NPC 없이 타이머로 자가 완성**되고, 완성 시 Wood/Stone 소모.
- [ ] Tier 승격이 추상 인구 기준으로 정상 동작(정량화 유지 시 기존 임계값).
- [ ] 이민/여관 고용 UI·경로가 제거되어 컴파일/런타임 오류 없음.
- [ ] 초기 코호트 스폰 + `NpcRelationBackfill` 관계 시드 정상.

---

## 8. 작업량 추정

- 순수 삭제(SP4·SP5)와 `v.Population` 재배선(SP1)·생산 통일(SP2)은 **작음~중간**. 서비스가 건물 기반이라 상점/제작 영향 없음.
- 본체는 **자가 건설(SP3)** — 타이머 누적은 이미 있어 NPC 의존만 걷어내면 됨(중간).
- **총계: 중간 규모 (수 일).** §5의 결정이 모두 확정돼 설계 리스크는 낮음. 캘리브레이션(정량화 유지)으로 재밸런싱 회피.

---

## 9. 문서 영향 (갱신 필요)

| 문서 | 갱신 |
|------|------|
| [VILLAGE_PHASES](../village/VILLAGE_PHASES.md) | NPC 직무/작업자 기반 발전 → 추상 인구 기반으로. NPC 건설 → 자가 건설 |
| [NPC_ACTIVITY_DESIGN](NPC_ACTIVITY_DESIGN.md) | Anchored/Worker/Adventurer 3분류 → **모험가 단일**로 축소. 정주 NPC 절 삭제 |
| [WORLD_HISTORY_PLAN §11](../world_history/WORLD_HISTORY_PLAN.md) | §0 추상 인구의 소비처 = 마을 경제(`v.Population`) 명시 |
| [INN_HIRING_DESIGN](../village/INN_HIRING_DESIGN.md) | 시스템 제거 — deprecated 표기 |
| resourceSystem.md 등 | 자원 생산 주체 = 인구(추상)로 정정 |

---

## 10. 리스크 / 주의

- **`NpcEntityIds` ↔ `v.Population` 혼동 재발 방지**: 경제는 반드시 `v.Population`(추상)만 읽고, `NpcEntityIds`는 명단 용도로만. 이 원칙을 코드 주석으로 못박을 것.
- **정량화 유지가 캘리브레이션 안전판**: `v.Population`을 raw 추상 인구(수백~수천)로 바꾸면 생산이 폭주. 반드시 기존 pop/50·cap25 정량화 유지.
- **세이브 마이그레이션**: 기존 세이브의 직업/배정/방문자 데이터는 로드 시 무시/정리. `v.Population`은 로드 후 §0에서 재계산.
- **Ruin 보류로 인한 임시 상태**: 인구 0이어도 마을이 폐허가 되진 않음(경고/노옵). 의도된 임시 상태.
