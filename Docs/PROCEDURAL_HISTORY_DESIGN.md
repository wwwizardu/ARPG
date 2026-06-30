# 정착지 통폐합 / 연대기 시뮬레이션 설계

> **이 문서의 범위 (2026-06-26 재편)**: 정착지(매크로) 층 — 런타임 LOD 시뮬, 연대기(Chronicle) 생성, 왕국/정복-병합 정치 모델, 3층 저장 구조.
> - **NPC 개인 관계**(가족/친구/원한/sentiment/호감도)는 → [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md)
> - **연대기를 씨앗으로 한 도전 이벤트**(고대신 각성/역병 확산/침공 등 — 플레이어가 맞서는 위협)는 → [WORLD_EVENT_DESIGN.md](WORLD_EVENT_DESIGN.md)
> - **구현 계획/진행 상황**은 → [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md)
> - **깊은 과거 백본(창세기/고대/암흑기 — 템플릿 조합 4시대 생성)** 은 보류·미고려로 결정(2026-06-26) → [Docs/archive/PROCEDURAL_HISTORY_BACKBONE.md](archive/PROCEDURAL_HISTORY_BACKBONE.md)로 이관. 본 문서는 그 중 "현존 정착지(현대 시대)" 층만 떼어 런타임 LOD 모델로 구체화한 것이다.

> 상위 컨셉: 오픈월드 + NPC 자율 행동 (코어 키퍼 + 림월드 하이브리드)
> 관련 문서: [gameOverview.md](gameOverview.md) · [MAP_DESIGN.md](MAP_DESIGN.md) · [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md) · [VILLAGE_PHASES.md](VILLAGE_PHASES.md)
>
> **목표**: 절차 배치된 정착지들이 합병/정복/왕국 형성 같은 **통폐합 히스토리**를 갖고, 그 결과가 NPC 관계·맵 표현·퀘스트의 소재가 되게 한다. 풀 시뮬 없이 정착지 granularity의 coarse 시뮬로 "역사처럼 보이는" 결과를 만든다.
>
> **위치**: New Game 시점 결정론적 생성 → 게임 중 런타임 지속 시뮬(LOD). 깊은 과거 백본과 달리 **현존 정착지는 게임 중에도 계속 변동**한다.

---

## 0. 런타임 LOD 시뮬레이션 — 살아있는 정착지

> 현존 정착지는 New Game 1회 생성에 그치지 않고 **게임 중에도 계속 시뮬레이션**된다(플레이어 근처=상세/실체화, 멀리=추상). 검증 프로토타입은 [Tools/ChronicleSimPrototype](../Tools/ChronicleSimPrototype/README.md).

### 0.1 두 단계 + LOD (level-of-detail)

```
① New Game — 결정론적 시뮬 1회
     · 정착지 배치 + 통폐합(합병/정복/왕국 형성) → 초기 세계 상태 + 연대기 생성
     · 같은 시드 = 같은 시작 (결정론은 '생성 시점'까지만 보장 — §0.4)
②   이후 — 런타임 지속 시뮬 (멈추지 않음). 해상도를 플레이어 거리로 결정 (LOD):
     ├─ 근처  → 상세(실체화): NPC 개체 + VillageData + Phase C 시스템
     └─ 멀리  → 추상: 정착지 단위 coarse 시뮬 계속 (저비용)
```

| | 근처 (실체화) | 멀리 (추상) |
|---|---|---|
| 단위 | 개별 NPC 엔티티 + VillageData | 정착지 1개 = 레코드 1개 |
| 시뮬 | Phase C 라이브 시스템(인구/건설/관계 drift) | coarse 틱 (게임 1일/주 단위, time-slice) |
| 비용 | 높음 (활성 마을 1~2개) | 낮음 (전체 정착지, 정수 연산) |
| NPC | 스폰됨, 관계/sentiment 생성 | 없음 (추상 인구 수치만) |

> ★ 성능 전제: 멀리-추상 틱을 가볍게 유지하려면 정착지 수가 관건. 배치 밀도를 region 추첨율로 줄이는 것(예: 2500→~300)이 LOD 런타임 시뮬의 사실상 전제조건.

### 0.2 실체화 전환 (추상 ↔ 상세) — 살아있는 세계의 핵심 신규 로직

| 방향 | 시점 | 동작 |
|------|------|------|
| **추상→상세** (materialize) | 플레이어 접근 | 추상 상태 + 연대기 → NPC 스폰·관계 backfill → VillageData 생성(SettlementId 링크). 오래 비웠으면 `LastSimGameTime`→현재 **fast-forward(catch-up)** 후 실체화 |
| **상세→추상** (fold-back) | 플레이어 이탈 | 라이브 VillageData(인구/사망/건물/플레이어가 일으킨 사건)를 **추상 상태·연대기로 접어 넣고** 추상 틱 재개 |

이 양방향 동기화(특히 fold-back의 라이브→추상 환산)가 정확성의 핵심 난제이자 버그 온상.

> NPC 관계 backfill(materialize 시점에 호출되는 L2)의 알고리즘은 → [NPC_RELATION_HISTORY_DESIGN.md §4](NPC_RELATION_HISTORY_DESIGN.md).

### 0.3 저장 구조 (3층 — lazy materialization)

```
WorldData (세이브 루트, Newtonsoft JSON)
│
├─【1. 추상 층】 모든 정착지 — 항상 저장, 작음 (수백개 × 정수 = 수십 KB)
│   └ WorldSimState
│       · List<SettlementLiveState> Settlements   // '최종 스냅샷' 아니라 '현재 상태' (계속 변함)
│       · List<PairAffinity> Affinities           // 이웃 우호도 — 추상 사건 구동의 핵심 상태
│       · List<KingdomData> Kingdoms
│       · long LastSimGameTime , ulong SimRngState // catch-up / 결정론 틱 커서
│
├─【2. 연대기】 append-only 사건 로그 (계속 누적 → 보존 정책 필요)
│   └ List<ChronicleEvent> Events
│
└─【3. 실체화 층】 방문한 마을만 — 무겁고 lazy
    · List<VillageData> VillageDatas (+ SettlementId 링크)
    · Dictionary<int,NpcSaveData> NpcSaveDatas (+ Sentiments)
    · List<NpcRelationEntry> NpcRelationList , List<NpcAffinityEntry> NpcAffinityList
```

```csharp
class SettlementLiveState {
    int SettlementId; RaceType Race; BiomeType Biome; Vector2Int RegionCoord, WorldCoord;
    int Pop;                  // 라이브 추상 인구 (틱마다 변함; 실제 NPC 수 아님 = 세력 가중치)
    int KingdomId; SettlementFate Fate; int ConqueredByKingdomId; int FoundedYear;
    bool IsMaterialized;      // 실체화되면 true → 추상 틱 정지
    int VillageId;            // 실체화 링크 (-1 = 미실체화)
    long LastReconciledTime;
}
```

핵심 원칙:
1. **추상 ≠ 실체화 데이터 분리** — 안 가본 마을은 정수 몇 개(LiveState)만. NPC 50명을 수백 마을에 저장하지 않음. (기존 코드가 이미 이 방식: NpcSaveData는 스폰된 것만, [VillageData.HasBeenPopulated](../Assets/Scripts/Village/VillageData.cs#L20) 존재)
2. **`SettlementId`가 두 층을 잇는 단일 키** — LiveState.VillageId ↔ VillageData.SettlementId.
3. **연대기 append-only = 저장이 정본** — 플레이어 개입으로 세계가 시드 재생성과 달라지므로 다시 못 만든다. 따라서 LiveState/Affinity/Chronicle을 **반드시 영속화** (프로토타입은 메모리만, 실제는 세이브 필수).

> 【3. 실체화 층】의 NPC 관계/sentiment 필드(`NpcRelationList`/`Sentiments` 등) 상세는 → [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md). 본 문서는 추상 층(1)·연대기(2)의 정본을 담당한다.

### 0.4 v1 범위 / 단계 (2026-06-26 결정)

- **멀리-추상 시뮬은 v1엔 (가) 인구 동역학만.** 멀리서도 전쟁/합병/왕국이 계속 일어나는 **(나) 정치 사건 지속은 나중에 추가** — 저장 구조는 (나)를 수용하되 동작은 보류.
- **결정론은 생성 시점만 보장.** 런타임은 플레이어 행동·방문 시점에 따라 갈라짐(의도된 동작).
- 구현 단계: ① 저장 포맷 + 추상 틱(인구) → ② 실체화(추상→상세) + catch-up → ③ fold-back + 연대기 보존 정책 → (나중) 멀리 정치 사건 지속.

---

## 1. 핵심 결정 — "매크로는 시뮬, 개인 관계는 생성"

"히스토리를 가상으로 생성할 것인가, 시뮬을 돌려 만들 것인가"는 양자택일이 아니라 **어느 granularity에서 시뮬을 멈추느냐**의 문제다.

| 방식 | 마을 합병/왕국 히스토리 | NPC 개인 관계 | 비용 | 제어 | 결정론 |
|------|----------------------|--------------|------|------|--------|
| **순수 생성** (랜덤 backfill) | ❌ 상태 추적이 없어 인과 모순 발생 ("합병된 마을이 그 전에 멸망") | ✅ 충분 | 최소 | 높음 | 쉬움 |
| **풀 시뮬** (NPC 단위, DF 방식) | ✅ 가능하나 과잉 | ⚠️ 가능하나 결과 90%는 플레이어가 영원히 못 봄 | 최대 | 낮음 (emergent 디버깅 지옥) | 어려움 (순서/부동소수점 의존) |
| **채택: 계층 분리** | ✅ 정착지 단위 coarse 시뮬 (본 문서) | ✅ 연대기를 제약으로 한 생성 ([관계 문서](NPC_RELATION_HISTORY_DESIGN.md)) | 소 | 높음 | 쉬움 (정수 연산 + 시드 채널) |

### 채택안 — 3계층

| 계층 | 시점 | 방식 | 산출물 | 문서 |
|------|------|------|--------|------|
| **L1. 정착지 연대기 시뮬** | New Game 1회 **+ 런타임 지속(LOD, §0)** | 정착지(수십~수백 개) 단위, 1년 틱 coarse 시뮬 | Chronicle(연대기) + 정치 스냅샷 (왕국/합병/폐허) | **본 문서 §3** |
| **L2. NPC 관계 backfill** | 마을 첫 population 시 (lazy, 실체화) | 연대기를 제약조건으로 가족/친구/파벌/원한 생성 | `NpcRelationEntry` + 초기 호감도 시드 | [관계 §4](NPC_RELATION_HISTORY_DESIGN.md) |
| **L3. 런타임 연속** | 게임 중 상시 | 기존 Phase C 시스템이 관계/연대기를 이어서 생성/변동 | 호감도 drift, Live 연대기 append | 본 문서 §5(연대기) + [관계 §5](NPC_RELATION_HISTORY_DESIGN.md)(관계) |

핵심 원칙:
1. **시뮬은 정착지 granularity에서 멈춘다** — NPC 개인은 시뮬 대상이 아니다. ("오프라인 시뮬 안 함"은 NPC 개인 관계 granularity에 한함 — 정착지 추상 시뮬(§0)과는 별개 층)
2. **연대기는 저장한다 (재생성하지 않는다)** — 수백 건의 작은 이벤트 리스트라 저장 비용이 무시 가능하고, 생성 규칙이 버전업돼도 구 세이브가 깨지지 않는다. 결정론은 "같은 시드로 New Game 하면 같은 역사"까지만 보장하면 된다.
3. **관계의 '왜'는 연대기 사건 참조 1개로 표현** — `OriginEventId` 링크가 개인 관계를 매크로 히스토리의 증언으로 만든다 ("내 아버지는 그 전쟁에서 죽었지"). 상세는 [관계 문서](NPC_RELATION_HISTORY_DESIGN.md).
4. **append-only** — 연대기 기존 사건은 수정하지 않고 추가만.

---

## 2. 용어 / 기존 자산 (정착지 측)

| 용어 | 의미 |
|------|------|
| **Settlement** | `RaceSettlementPlacer`가 배치한 절차 정착지. L1 시뮬의 에이전트 단위 |
| **Chronicle** | L1 시뮬 + 게임 중 Live append로 누적되는 사건 목록. `WorldData`에 저장 |
| **Kingdom** | L1 시뮬 결과로 정착지들이 묶인 정치 단위. 수도 정착지 1개 보유 |

| 자산 | 위치 | 상태 | 역할 |
|------|------|------|------|
| `RaceSettlementPlacer.ComputeAll()` | [RaceSettlementPlacer.cs](../Assets/Scripts/Map/Race/RaceSettlementPlacer.cs) | 골격 ✅ (청사진 콘텐츠 대기) | **L1 시뮬의 입력** — 정착지 시드 목록 (좌표/종족/biome) |
| `DerivedSeed` / `DeterministicCoord` / `RegionCoord` | [DerivedSeed.cs](../Assets/Scripts/History/Generator/DerivedSeed.cs), Scripts/Map/Region | ✅ 구현됨 | 시드 채널 분리, 결정론 좌표 (백본 Phase 0 산출물이나 본 트랙도 사용) |
| `VillageData` / `VillageManager` / Phase C 시스템 | [VillageData.cs](../Assets/Scripts/Village/VillageData.cs) 등 | ✅ 런타임 동작 중 | **실체화 층(L3)의 본체** — 인구/Tier/건설은 이미 라이브 시뮬 |

| 데이터 | 위치 | 형태 |
|--------|------|------|
| 종족 간 기본 성향 (카탈로그) | `RaceRelationTable.bytes` (신규) | POCO → `ImmutableDictionary` |
| Chronicle / Kingdom (월드 인스턴스) | `WorldData.ChronicleSaveData` (신규) | Newtonsoft JSON, append-only |

---

## 3. L1 — 정착지 연대기 시뮬 (New Game 1회 생성 + 런타임 지속)

> 본 절은 **초기 생성(New Game 1회)** 을 기술한다. 생성된 정착지는 게임 중에도 멀리=추상/근처=실체화로 **계속 시뮬레이션**된다(런타임 LOD 모델·저장 구조는 §0, 런타임 사건 append는 §5).

### 3.1 입력

```
RaceSettlementPlacer.ComputeAll(mapSeed, ...)  →  List<SettlementPlacement>
```

- 맵 경계 [-98, 98] 청크 = 49×49 region → 정착지 후보 최대 ~2,400개 (종족 비호환 region 제외 시 실제는 그 이하)
- 시뮬 에이전트 상태는 placement당 아래 구조체 1개:

```csharp
// 시뮬 내부 전용 (저장 안 함 — 추상 층 SettlementLiveState로 평탄화하여 저장, §0.3)
public struct SettlementSimState
{
    public int SettlementId;            // placement 인덱스 기반 결정론 ID
    public GlobalEnum.RaceType Race;
    public Vector2Int RegionCoord;
    public int Population;              // 종족/biome 기반 초기값
    public int KingdomId;               // -1 = 무소속
    public SettlementFate Fate;         // Alive / Ruined / Merged / Conquered
    public int MergedIntoId;            // Fate == Merged일 때
    public int ConqueredByKingdomId;    // Fate == Conquered일 때 (정복국 KingdomId). 정착지는 생존
    public int FoundedYear;
}

public enum SettlementFate { Alive = 0, Ruined = 1, Merged = 2, Conquered = 3 }
```

- 정착지 간 우호도: `Dictionary<(int, int), int>` — **이웃만** (region 거리 ≤ 3, 정규화 페어). 전쌍 O(N²)을 만들지 않는다.

### 3.2 틱 루프

- **틱 = 1년, 기본 150년** (튜닝 항목). 모든 난수는 `DerivedSeed.Derive(mapSeed, ChannelChronicle)` 계열 채널에서 SplitMix64 — `UnityEngine.Random` / `System.Random` 사용 금지 (상태 의존 PRNG 함정 회피).
- 틱당 처리:

```
1. 인구 성장:    pop += growthRate(race, biome) ± noise
2. 이웃 페어 이벤트 추첨 (페어당 연 1회 roll):
     가중치 = f(종족 호환(RaceRelationTable), 거리, 인구비, 현재 우호도,
                정복국 과확장 페널티)
     → Trade / Skirmish / War / Alliance / Merge / Conquer 중 0~1개
3. 사건 효과 적용:  우호도/인구 가감, Fate 변경
     - War 해소 분기: 승자가 충분히 강하면(인구 우위 + 과확장 여유) 패자를 Conquer
       (패자 KingdomId = 승자국, 정착지 생존, 타종족 가능) — 아니면 기존대로 pop −− / 폐허 확률
4. 왕국 검사:     동맹 클러스터 크기 ≥ K → KingdomFound (최대 인구 정착지 = 수도)
                  기존 왕국 인접 + 우호 ≥ T → KingdomJoin
     - 과확장 페널티: 왕국 멤버 수가 클수록 추가 정복/병합 가중치 감소 (자기 제한).
       단일 제국 수렴을 막아 여러 다종족 지역 제국이 공존하게 하는 핵심 제동
5. 사건을 Chronicle에 append
```

### 3.3 사건 카탈로그 (v1)

| ChronicleEventType | 조건 (요약) | 효과 | NPC backfill에 주는 제약 ([관계 §4.3](NPC_RELATION_HISTORY_DESIGN.md)) |
|---|---|---|---|
| `Trade` | 우호 ≥ 0, 거리 가까움 | 우호 +, 양쪽 pop + | 상인 NPC의 우호 대사 |
| `Skirmish` | 우호 < 0 | 우호 −, pop 소폭 − | 경계 대사 |
| `War` | 우호 ≤ −T, 종족 적대 | 우호 −−, 패자 pop −−, 폐허 확률 | **원한 관계 + 유족 서사** |
| `Conquer` | War 승리 + 인구 우위 + 승자국 과확장 여유 | 패자 `Fate=Conquered`, 정착지 생존하나 `KingdomId=승자국` (타종족 가능) | **피정복민 서사 — `ResentsKingdom` + 지배종족 거주민 대비 파벌 긴장** |
| `Alliance` | 우호 ≥ +T | 동맹 마크 (왕국 전 단계) | 혼인 가계(두 마을에 걸친 가족) |
| `Merge` | 동맹 + 인구비 ≥ 3:1, 또는 War 패배 | 작은 쪽 `Fate=Merged`, 인구 흡수 | **두 founder 혈통 파벌** |
| `Ruin` | pop ≤ 0 | `Fate=Ruined` | (NPC 없음 — 맵 표현만, §3.5) |
| `KingdomFound` | 동맹 클러스터 ≥ K | KingdomId 부여, 수도 지정 | 수도 자부심/변경 대사 |
| `KingdomJoin` | 왕국 인접 + 우호 ≥ T | KingdomId 부여 | 충성/불만 성향 분포 |

```csharp
[Serializable]
public class ChronicleEvent
{
    public int EventId;                 // 발급 순번 (append-only)
    public int Year;
    public ChronicleEventType Type;
    public int SubjectSettlementId;
    public int ObjectSettlementId;      // 단독 사건이면 -1
    public int KingdomId;               // 관련 없으면 -1
}
```

### 3.4 출력 / 저장

```csharp
// WorldData에 추가
public ChronicleSaveData Chronicle = new();

[Serializable]
public class ChronicleSaveData
{
    public int Version = 1;
    public int SimulatedYears;
    public List<ChronicleEvent> Events = new();              // 수백 건 수준
    public List<SettlementSnapshot> Settlements = new();     // 최종 상태만 (런타임은 §0.3 SettlementLiveState)
    public List<KingdomData> Kingdoms = new();
}

[Serializable]
public class KingdomData
{
    public int KingdomId;
    public string Name;                  // 종족 어근 기반 절차 작명 (v1은 "{종족} 왕국 N" 수준)
    public GlobalEnum.RaceType FoundingRace;  // 건국 종족(= 수도 종족). 피정복 타종족과 대비 → backfill 파벌 기준
    public int CapitalSettlementId;
    public List<int> MemberSettlementIds = new();  // 정복으로 타종족 정착지 포함 가능 → 다종족 제국
}
```

- **저장이 정본**: 한 번 생성된 연대기는 시드로 재생성하지 않는다 (원칙 §1-2).
- VillageManager가 정착지를 실제 `VillageData`로 등록할 때 `SettlementId ↔ VillageId` 매핑을 보관 → 연대기 조회 경로. (`VillageData.SettlementId` 필드 추가 필요 — [PLAN R1](WORLD_HISTORY_PLAN.md))

### 3.5 맵 표현과의 연결 (MAP_DESIGN 합류 포인트)

- `Fate == Ruined / Merged` 정착지 → 마을 청사진 대신 **폐허 variant** 배치. Lore Fragment의 L3 청사진과 같은 메커니즘을 재사용하고, `FateKeyword`를 연대기 사건에서 가져온다 (War로 폐허 → `burned`, 합병 → `forgotten` 등).
- `Fate == Conquered` 정착지는 **폐허가 아니다** — 정복국 깃발/점령 variant로 *살아있는 마을*로 표현(`FateKeyword = occupied`). 피정복 종족과 지배 종족이 한 마을·왕국에 공존하는 비주얼이 다종족 제국의 시각적 증거가 된다.
- 즉 기존 Lore Fragment의 "랜덤 fate" 일부가 "연대기가 실제로 기록한 fate"로 승격된다. 랜덤 fragment는 그대로 공존 (고대 유적용).

### 3.6 비용 추정

정착지 ≤ 2,400 × 이웃 ≤ ~8 × 150틱 ≈ 정수 연산 수백만 회 — **New Game 시 수십 ms**. 필요 시 정착지 수를 region 추첨율로 줄이는 노브가 이미 placer에 있다.

---

## 4. 정치 모델 단계 (2026-06-18 결정)

| Tier | 범위 | 상태 |
|------|------|------|
| Tier 1 | 단일종족 지역 왕국 (Trade/War/Alliance/Merge/Kingdom) | 기반 |
| **Tier 2 (채택)** | **+ 정복-병합(Conquer) + 과확장 페널티** → 다종족 제국 + 피정복민 서사 | **현 갱신 범위** |
| Tier 3 (보류) | + 분열(Secession) + 왕국 간 전쟁 + 자동 스윕 튜닝 하니스 | 정복-병합 안정화 후 증분 도입 |

> Tier 3는 통합/분열의 *반대 힘 균형* 튜닝이 비싸므로, Tier 2를 먼저 안정화하고 덤프가 너무 정적이면 분열을 *하나씩* 얹는다(모든 힘 동시 튜닝 회피). 자동 스윕 하니스는 그때 도입.

**검증 마일스톤**: New Game 연대기 텍스트 덤프가 "읽을 만한 역사"인지 눈으로 평가. 특히 **다종족 제국이 형성되는지** + **한 왕국이 맵을 다 먹지 않고 여러 지역 제국이 공존하는지**(과확장 페널티 작동) 확인. 단조롭거나 한 덩어리로 수렴하면 사건 카탈로그/정복·과확장 가중치만 튜닝.

---

## 5. 런타임 Live 연대기 (Live append)

- 게임 중 큰 사건을 `Chronicle.Events`에 append (EventId 이어서 발급):
  - 마을 전멸 (`DepletedAt` 확정 시) → `Ruin`
  - VillageStage 승격 Town/City 도달 → `Promotion` (신규 타입)
  - 플레이어 개입 사건 (보스 처치로 마을 구함 등) → `MessageManager.Broadcast`로 수집하는 패턴 (구 백본 §8 Live Backbone 트리거 설계 참고 — [archive](archive/PROCEDURAL_HISTORY_BACKBONE.md))
  - **도전 이벤트 결과** (각성/역병/침공의 발생·해결) → [WORLD_EVENT_DESIGN.md](WORLD_EVENT_DESIGN.md). 신규 사건 타입(`Awakening`/`Blight`/`Siege` 등)은 §3.3 카탈로그에 합류(append-only)
- **멀리(추상) 정착지의 정치 사건(합병/왕국/전쟁) 지속은 v1 범위 밖** = §0.4의 **(나)**. v1 런타임 멀리-시뮬은 **(가) 인구 동역학만** — 합병/왕국은 New Game 생성 시점까지만 일어남. 저장 구조(SettlementLiveState/Affinity)는 (나)를 수용하되 **동작은 추후** (백그라운드 정치 틱 추가 시 활성화).

> NPC 개인 관계의 런타임 변동(호감도 drift, 출생/사망 관계 갱신)은 → [NPC_RELATION_HISTORY_DESIGN.md §5](NPC_RELATION_HISTORY_DESIGN.md).

---

## 6. 시드 채널 / 저장 추가 (정착지 측)

```csharp
// DerivedSeed 채널 추가
ChannelChronicle          // L1 시뮬 PRNG  (관계 backfill용 ChannelRelationBackfill은 관계 문서)

// 신규 테이블 (.bytes, 시트 작업 필요)
RaceRelationTable { Id, RaceA, RaceB, BaseAffinity, WarWeight, TradeWeight, MergeAllowed }

// WorldData 추가 필드
public ChronicleSaveData Chronicle = new();   // (런타임 추상 층 WorldSimState 구조는 §0.3)
// VillageData 추가 필드
public int SettlementId;                       // 연대기 ↔ 마을 링크 (-1 = 미연결)
```

ECS 풀 추가 없음 — 본 설계는 의도적으로 ECS 컴포넌트를 만들지 않는다 (정본은 매니저 + `WorldData`).

---

## 7. 미결정 사항 (구현 전 결정 필요)

1. **시뮬 연수/사건 밀도** — 150년 기본값은 가설. New Game 덤프 보고 튜닝.
2. **RaceRelationTable 외부화 범위** — 종족 쌍 성향을 시트로 뺄지, v1은 코드 상수로 시작할지 (v1 코드 상수 권장).
3. **왕국 이름 작명** — v1 절차 작명("{종족} 왕국 N")으로 충분한가, 어근 조합 작명기를 만들 것인가 (v1 절차 권장).
4. **정복 annex 발동 조건 / 과확장 페널티** (2026-06-18 추가) — War 승리 시 Conquer 전환 확률, 왕국 멤버 수에 따른 확장 가중치 감쇠 곡선. "단일 제국 수렴 ↔ 과도한 파편화" 사이 밸런스. 시뮬이 빠르고 결정론이라 덤프를 눈으로 보며 튜닝.
5. **`SettlementId ↔ VillageId` 매핑 방식** — `VillageData`에 필드 추가 vs 별도 매핑 테이블 ([PLAN R1](WORLD_HISTORY_PLAN.md)).

> NPC 관계 측 미결정(`NpcStat` 시드화, InnVisitor 출신 정착지 등)은 → [NPC_RELATION_HISTORY_DESIGN.md §8](NPC_RELATION_HISTORY_DESIGN.md).

---

## 8. 구현 계획 / 진행 상황

상세 Phase 분해(R0~R4)와 현황은 → [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md).
정착지 통폐합 시뮬 검증 프로토타입은 → [Tools/ChronicleSimPrototype](../Tools/ChronicleSimPrototype/README.md), 배치 결과 export는 [ChronicleSimExport.cs](../Assets/Scripts/Editor/ChronicleSimExport.cs).

---

**문서 상태**: 정착지 통폐합/연대기 설계 (런타임 LOD 도입)
**최근 갱신**: 2026-06-26 — 깊은 과거 백본(템플릿 4시대 생성) 분리·[archive](archive/PROCEDURAL_HISTORY_BACKBONE.md) 이관, 정착지 통폐합 층으로 재편. NPC 개인 관계는 [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md)로 분리.
**영감/참고**: Dwarf Fortress(legends), Caves of Qud(백본+부분시뮬), RimWorld(NPC 서사)
