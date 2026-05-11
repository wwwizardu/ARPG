# 마을 자가 건설 우선순위 시스템 — 설계 문서

> 상위 문서: [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md) · [PHASE_D_DESIGN.md](PHASE_D_DESIGN.md)
> 관련: [INN_HIRING_DESIGN.md](INN_HIRING_DESIGN.md)
>
> **목표**: 마을이 자가 건설 시 어떤 오브젝트를 다음 차례에 지을지 결정하는 로직을 **4계층 가중치 점수 단일 시스템**으로 일원화한다. 기존 `VillageBuildRoadmap`(하드코딩 순서) + `VillageNeedsCache`(점수)의 이중 구조가 만들었던 일관성 문제를 해소한다.

---

## 1. 폐기 대상

| 폐기 | 이유 |
|------|------|
| `VillageBuildRoadmap.SETTLEMENT_SEQUENCE` 외 시퀀스 배열 | 인덱스 기반 순회가 NeedsCache 개입 시 어긋남. Pop=1 같은 특수 상황 케이스 누락 |
| `VillageBuildRoadmap.GetNextTarget()` | 시퀀스 폐기와 함께 |
| `System_VillageBuildQueue`의 Roadmap fallback 분기 | 단일 점수 시스템으로 통합 |
| Campfire의 if문 특수 처리 | Layer 1로 흡수 (no hardcoded TableId) |

**완전 제거**:
- `VillageBuildRoadmap.cs` 파일 자체 삭제
- `RoadmapEntry` struct 폐기 — `GetRankedCandidates`는 `List<int>`(TableId)만 반환, BuildHours 등은 호출자가 `BuildableItemTable`에서 조회
- BuildHours는 `BuildableItemTable.BuildHours` 컬럼으로 이관 (시트 정본화)

---

## 2. 4계층 점수 모델

```
TotalScore(candidate) =
    L1_EXISTENCE   (0 또는 5000)        +
    L2_DEFICIT     (0 ~ Pop × 1000)     +
    L3_GROWTH      (BaseWeight + 가산)  +
    L4_DEFENSE     (Wall × Threat × 30) −
    DiminishingPenalty (existing × 20)
```

각 Layer는 **상호 배타적이지 않음** — 한 후보가 여러 Layer 점수를 동시에 받음 (예: InnBed = L2 housing 결핍 + L3 Inn 세트 게이트).

### 2.1 L1 — 존재 (Existence) — 5000점

```
candidate == Campfire(100) AND 마을에 Campfire 0개 → +5000
```

- 마을의 정체성. 없으면 마을이 아님
- Campfire 1개 짓고 나면 L1 = 0
- **Anti-spam**: Campfire는 BuildableItemTable에서 `MaxPerVillage = 1` 보장 (또는 코드에서 강제)
- 점수 우선순위 자체로 1위가 되어 §3 자원 대기 정책의 적용 대상.

> **No hardcoded TableId 정책 충돌 검토**:
> Layer 1만 Campfire를 명시 참조. 향후 BuildableItemTable에 `IsExistenceCore` 플래그를 추가하여 데이터 정본화하는 방향이 이상적이지만, "Campfire = 마을 존재"는 마을 시스템의 axiom이라 코드 1줄 분기는 허용. 다른 마을 정체성 오브젝트가 추가되면 그 시점에 컬럼화.

### 2.2 L2 — 결핍 (Deficit) — Pop × 1000점

**Housing 결핍** (Pop > housing):
```
candidate가 ProvidedService.Housing 비트 보유
  → +(Pop − housing) × 1000
```

**Food 결핍** (Food < Pop × 5):
```
candidate가 ProvidedService.Production 비트 보유
  → +(Pop × 5 − Food) × 50
```

**핵심 효과**:
- Pop=1, housing=0: Bedroll +1000, Bed +1000, InnBed +1000 (셋 다 동일 점수)
- Pop=1, housing=1: Bedroll/Bed/InnBed L2 = 0 → **무한 침대 빌드 자동 차단**
- Pop=3, housing=2: Bed +1000 (1명분 결핍)
- 식량 deficit이 30이면 CropPlot/DryingRack에 +1500

> **인구 기반 동적 cap**: housing ≥ pop이 되는 순간 L2 = 0이 되어 다른 후보가 자연스럽게 1위. 사용자 직관 반영.

### 2.3 L3 — 성장 (Growth) — BaseWeight + 가산

기본 점수에 다양한 contextual 가산을 더한다:

| 가산 항목 | 점수 | 트리거 |
|-----------|------|--------|
| `BaseWeight` (테이블) | 10 (현 기본) | 모든 후보 |
| **Tier 게이트 보너스** | **+400** | 다음 Stage 진입 미충족 조건을 이 후보가 채움 |
| 세트 완성 보너스 | +80 | 후보 추가가 새 세트(`ObjectSetCatalog`)를 완성 |
| 직업 매칭 | +50 | `AssociatedJobType`이 마을 NPC의 직업과 일치 |
| Cap 초과 잉여 | +5 | Wood/Stone 보유량이 비용의 3배 이상 (소비 압력) |

**Tier 게이트 보너스의 의미** — 사용자 의도의 핵심.

[System_VillageTierProgression](../Assets/Scripts/Common/System/System_VillageTierProgression.cs)이 검사하는 조건 중 미충족인 것을 BuildQueue가 인지하여, 그 조건을 채우는 후보에 **+400** 가산.

| 현재 Stage | 미충족 시 +400 받는 후보 |
|-----------|--------------------------|
| Settlement | Inn 세트 미완 → InnBed, Hearth |
| Settlement | Shop 부재 → MerchantStall |
| Settlement | housing < 2 → Bedroll/Bed/InnBed (L2와 별도, 정원 미달이 아니어도 게이트 차단) |
| Hamlet | Civic 부재 → TownPost |
| Hamlet | housing < 4 → Bed |
| Village | Forge 세트 미완 → Furnace, Anvil |
| Village | Shop 부재 → MerchantStall (정상 흐름엔 Settlement에서 충족됨, 파괴 복구용 잔존 게이트) |
| Village | housing < 8 → Bed |

> 중복 가산 가능: Pop=1 Settlement에서 Bedroll은 L2 (housing 결핍) +1000 + L3 게이트 +400 = +1400. 이게 마지막 Pop=1까지 housing=2를 강제로 채우는 동력.

### 2.4 L4 — 방어 (Defense)

```
WallScore = wall_deficit × ThreatLevel × 30
```

- 위협도 0 (현재 모든 마을): WallScore = 0 → 벽은 다른 후보가 다 짓고 자원이 남을 때만 짓는다 (현 BuildQueue의 fallback 로직 유지)
- 위협도 1: 결손 1당 +30 (일반 BaseWeight 10 수준 보다 크지만 L3 게이트(+400)에 밀림)
- 위협도 5(최대): 결손 1당 +150 → L3 일반 후보 능가, L2 결핍과 비등
- **Gate**: 항상 절대 우선 (현 BuildQueue 유지)

> **위협도 시스템은 Phase F 도입 예정**. 현재는 항상 0으로 간주, L4 = 0.

### 2.5 Anti-spam

**Hard cap** (현재 유지):
```
candidate.MaxPerVillage > 0 AND existing >= MaxPerVillage → 후보 제외
```

**Soft cap** (Layer 자체에서):
- L2 housing/food deficit이 0이면 침대/생산건물의 가장 큰 가산 사라짐 → 자동으로 다음 후보로 넘어감

**Diminishing returns** (강화):
```
existing × 20 점 차감 (현재 12 → 20)
```
- L1/L2/L3 점수 폭이 커진 만큼 페널티도 키움
- 같은 TableId가 무한 1위 굳는 것 방지

---

## 3. 자원 부족 처리 — 점수 우선순위 절대 존중

**핵심 정책**: 1위 후보가 무엇이든 자원 부족이면 다른 후보로 우회하지 않고 대기. Campfire뿐 아니라 Hearth, Bed, InnBed 등 모든 상위 후보에 동일하게 적용된다. 점수가 낮은 항목이 단지 비용이 싸다는 이유로 먼저 지어지는 일은 없다.

**실패 사유 분리**:
- **자원 부족**: 시간이 지나면 패시브 생산으로 해소되므로 **대기** (다른 후보 시도 X)
- **자리 없음 / 테이블 누락**: 영구 블로커이므로 **다음 후보 시도**
- 모든 후보 자리 없음 → 벽 fallback
- 벽도 없으면 다음 틱까지 휴면

```csharp
enum BuildAttemptResult { Started, WaitForResources, NoTileOrTableMissing }

List<int> ranked = VillageNeedsEvaluator.GetRankedCandidates(v);
foreach (int id in ranked) {
    var r = TryStartGeneralBuild(v, id, now);
    if (r == Started) return;
    if (r == WaitForResources) return;          // 점수 우선순위 존중 — 대기
    // NoTileOrTableMissing → 다음 후보로 이동
}
// 모든 후보 자리 없음 → 벽 fallback
if (wallSeg != null) TryStartWallTask(v, wallSeg, now);
```

**왜 자리 없음만 fallback?**
- 자리 없음은 마을 면적/카테고리 격리/광장 예약 등 **물리적 조건** — 시간 지나도 안 풀림 (Tier 승격으로 면적이 늘어야 해소)
- 자원 부족은 패시브 생산으로 시간이 해결 — 1~2 분 만에 모이는 자원 때문에 우선순위를 깨면 본 시스템 의의 상실

> **(후속) 오브젝트 파괴/이동 통합**: 추후 오브젝트 파괴(또는 이동) 시스템이 구현되면, 1위 후보가 자리 없음으로 막힐 때 단순 fallback이 아니라
> **"필수적이지 않고 우선순위가 가장 떨어지는 기존 오브젝트를 파괴하거나 이동시켜 자리를 확보 → 1위 후보 배치"**
> 흐름으로 확장한다. 예: Bedroll 1개 자리에 InnBed(상위) 들어와야 할 때 Bedroll 철거 + 자원 일부 환원.
> 후보 선정 기준은 같은 점수 시스템의 역방향 사용 — `(존재 여부 × MaxPerVillage) + 점수 최저` 순으로 파괴 우선.
> 본 문서 §3은 그 시스템 도입 전까지의 정책.

---

## 4. Hearth Stone 비용 하향

**현재**: Hearth `Cost_Stone = 20`

**변경**: Hearth `Cost_Stone = 5`

근거:
- Pop=1 Stone 생산 5h당 +1 → 5 stone = 25h 게임시간
- Tier 승격 24h와 자연스럽게 맞물림 (Stone 모은 후 Hearth 빌드 → Inn 세트 완성 → 이민 시작 → Pop 2~3 도달 → Hamlet 승격)
- 같은 Stone-only 건물인 Well(15)과 차등화 (Hearth가 더 핵심 자원이므로 저렴)

> **데이터 시트 동기화**: `Assets/_BinaryData/TableData/BuildableItemTable.bytes` 직접 수정. Google Sheet도 함께 갱신해야 다음 DownloadTables에서 덮어쓰지 않음.

---

## 5. 시뮬레이션 — Pop=1 시작 시 빌드 순서

전제: Pop=1, Wood/Stone 0에서 시작, 패시브 생산만 있음.

| 게임시간 | 자원 | 1위 후보 (점수) | 동작 |
|----------|------|-----------------|------|
| 0h | 0W 0S | Campfire L1=5000 | Wait (cost 3W) |
| 3h | 3W 0S | Campfire 5000 | Build Campfire (2h) |
| 5h | 0W 0S | Bedroll L2=1000 + L3 게이트=400 = 1400 | Wait (cost 2W) |
| 7h | 2W ≈0S | Bedroll 1400 | Build Bedroll (1.5h) |
| 8.5h | 0W ≈0S | Bed L2=0(housing=1=pop) L3 게이트=400 = 410 | Wait (cost 10W) |
| 18.5h | 10W 2S | Bed 410, CropPlot L3=10+직업가산 = 60 | Build Bed (3h, housing=2) |
| 21.5h | 0W ≈3S | InnBed L3 게이트(Inn 미완)=400+80(세트)=490 | Wait (cost 20W) |
| 41.5h | 20W ≈8S | InnBed 480 | Build InnBed (4h) |
| 45.5h | 0W ≈8S | Hearth L3 게이트=400+80(세트 완성)=490 | Wait (cost 5S) |
| ~46h | 0W 5S | Hearth 480 | Build Hearth (4h) — **Inn 세트 완성!** |
| 50h+ | | 이민 시작, 방문자 도착 | UI에서 고용 → Pop 증가 |
| ~80h | | Pop=3, food≥30, age≥24h, Inn ✓ → **Hamlet 승격** | |

게임시간 ~80h ≈ 실시간 ~40분 (게임시간 배율 2x 가정). 원래 24h 시작 + 3 NPC보다 길지만, 의사결정과 빌드 진행이 끊임없이 일어나는 시간이라 체감은 다름.

---

## 6. 영향 받는 파일

| 파일 | 변경 |
|------|------|
| [`VillageNeedsEvaluator.cs`](../Assets/Scripts/Village/VillageNeedsEvaluator.cs) | 정적 헬퍼. 4 Layer 점수 + `GetRankedCandidates` (List&lt;int&gt; TableId 반환) + 진행 중 task 집계 |
| [`TierGapDetector.cs`](../Assets/Scripts/Village/TierGapDetector.cs) | Tier 승격 미충족 조건 감지 헬퍼 |
| [`BuildTaskSnapshot.cs`](../Assets/Scripts/Village/BuildTaskSnapshot.cs) | 진행 중 task 영구 저장 스냅샷 (Step C, N개 동시 task 세이브) |
| [`System_VillageBuildQueue.cs`](../Assets/Scripts/Common/System/System_VillageBuildQueue.cs) | Phase1 task 풀 순회 완료 처리 + Phase2 Pop 한도 while 루프 신규 시작. TryStartNextTask/TryStartWallTask는 bool 반환. task entity 별도 발급 |
| [`VillageData.cs`](../Assets/Scripts/Village/VillageData.cs) | `ActiveBuildTasks` List 추가, legacy CurrentBuild* 필드는 호환만 |
| [`VillageManager.cs`](../Assets/Scripts/Village/VillageManager.cs) | `SyncTaskToData`/`RestoreTaskFromData`가 ActiveBuildTasks 리스트 기반 + 구 단일 필드 자동 마이그레이션 |
| `VillageBuildRoadmap.cs` | **완전 삭제** — `BuildableItemTable.BuildHours` 컬럼으로 흡수 |
| `VillageNeedsCache.cs` | **완전 삭제** — `VillageNeedsEvaluator`가 매 빌드마다 inline 평가 |
| [`VillageDebugLog.cs`](../Assets/Scripts/Village/VillageDebugLog.cs) | task 풀 순회로 마을별 task 조회 |
| [`Assets/_BinaryData/TableData/BuildableItemTable.bytes`](../Assets/_BinaryData/TableData/BuildableItemTable.bytes) | Hearth `Cost_Stone`: 20 → 5, 모든 항목에 `BuildHours` 컬럼 |

---

## 7. 단계별 마이그레이션

각 단계 끝에서 빌드/플레이 가능 (점진적):

1. **Hearth 비용 하향** — 데이터만, 코드 영향 없음
2. **TierGapDetector 추가** — `System_VillageTierProgression`의 미충족 조건 검사를 외부에서 호출 가능한 헬퍼로 분리
3. **NeedsEvaluation 4 Layer 점수화** — Layer A/B/C/D 함수 분리, GetRankedCandidates 신규
4. **VillageNeedsCache 시그니처 변경** — 단일 RoadmapEntry → List<RoadmapEntry>
5. **BuildQueue 로드맵 fallback 제거 + 2위 시도 루프** — 기존 fallback 로직 삭제, 점수 정렬 후 첫 affordable 채택
6. **VillageBuildRoadmap 정리** — 시퀀스/GetNextTarget 제거, GetBuildHours만 유지
7. **VillageDebugLog 갱신** — NeedsCache 1위 출력
8. **검증** — Pop=1 시뮬레이션과 실제 빌드 순서 일치 확인

---

## 8. 검증 체크리스트

- [ ] 새 게임 시작 → 첫 빌드는 Campfire (Wood 충분 시 즉시)
- [ ] Campfire 1개 후 다시 Campfire 시도 안 함 (MaxPerVillage=1 또는 L1=0)
- [ ] Bedroll 1개 후 Bedroll/Bed L2=0 (housing=1=pop) → 다른 후보로 넘어감
- [ ] Pop=3 도달 시 housing < 3이면 Bed L2 +2000 → 즉시 Bed 빌드 우선
- [ ] Inn 세트 미완 시 InnBed/Hearth L3 게이트 +400 → 다른 일반 후보보다 우선
- [ ] 1위 후보 자원 부족 시 **다른 후보로 우회하지 않고 대기** (점수 우선순위 절대 존중)
- [ ] 1위 후보 자리 없음(영구 블로커) 시 2위 후보로 자동 진행
- [ ] 모든 후보 자리 없음 → 벽 fallback 동작 (위협도 0이어도 자원 남으면)
- [ ] 위협도 5로 강제 시뮬레이션 → 벽이 일반 후보보다 우선
- [ ] Hamlet 승격 후 Hearth가 HAMLET_SEQUENCE 의존 없이 자연 진행
- [ ] VillageDebugLog의 [VillageSnapshot]에 새 점수 시스템 1위 출력
- [ ] **Pop=N일 때 동시에 최대 N개 task 진행** (Step B)
- [ ] **진행 중 task의 TableId가 점수 평가의 existing에 합산되어 동시 동일 항목 중복 빌드 방지**
- [ ] **세이브 → 다중 task가 ActiveBuildTasks 리스트에 모두 보존**
- [ ] **로드 시 각 스냅샷마다 task entity 발급되어 동일 시점 복원**
- [ ] **구 세이브(단일 CurrentBuildTableId) 로드 시 자동으로 ActiveBuildTasks로 마이그레이션**

---

## 9. 미결정/후속 검토

- **위협도 시스템 통합** (Phase F): L4 계수 30이 적정한지는 위협도 도입 후 튜닝
- **Stage별 BaseWeight 차등**: 후반 Stage에서 일반 후보 BaseWeight를 올려 게이트 외 자율 성장 가속? 현재는 모두 10 고정
- **세트 완성 보너스 폭**: 현 80. Tier 게이트 400과의 격차가 적절한지 실측 후 조정 가능
- **오브젝트 파괴/이동 통합** (후속 시스템 의존): 1위 후보가 자리 없음으로 막힐 때, 단순 fallback이 아니라 **필수적이지 않고 우선순위가 가장 떨어지는 기존 오브젝트를 파괴하거나 이동시켜 자리를 확보 후 1위 배치**하는 흐름으로 확장한다. 파괴 후보 선정은 본 점수 시스템의 역방향 사용(점수 최저, MaxPerVillage 미달, Tier 게이트 비기여 우선). 자원 일부 환원 정책 필요. 본 §3 정책은 그 시스템 도입 전 임시 정책이다. (§3 본문 노트 참조)
