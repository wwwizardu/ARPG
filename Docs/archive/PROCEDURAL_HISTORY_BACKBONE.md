# [아카이브] 절차적 역사 생성 — 깊은 과거 백본 (템플릿 4시대 생성)

> **⛔ 아카이브 / 미고려 (2026-06-26 결정)**
> 이 문서는 **깊은 과거 백본(창세기/고대/암흑기 — 사건 템플릿 조합으로 4시대 역사 생성)** 설계의 보존본이다. 현재 로드맵에서 **고려하지 않기로 결정**되어 활성 문서 트리에서 제외했다.
> - 본문 §0(런타임 LOD)과 정착지 통폐합/연대기 내용은 활성 문서 [PROCEDURAL_HISTORY_DESIGN.md](../PROCEDURAL_HISTORY_DESIGN.md)(정착지 통폐합) / [NPC_RELATION_HISTORY_DESIGN.md](../NPC_RELATION_HISTORY_DESIGN.md)(NPC 관계)로 이관됨.
> - 이미 구현된 공용 인프라(`DerivedSeed`/`DeterministicCoord`/`HistoryRng`, §9 KoreanJosaResolver 설계 등)는 **코드를 그대로 유지**하며, 본 문서는 그 설계 기록으로만 남는다.
> - 백본을 부활시키려면 이 문서를 활성 트리로 되돌리고 §13.10(진행 상황)부터 재개.
> - ※ 아래 본문의 상대경로 링크는 원래 위치(`Docs/`) 기준이라 아카이브(`Docs/archive/`)에서는 일부 어긋날 수 있다. 원본 그대로 보존.

---

# 절차적 역사 생성 시스템 설계

> **🔄 방향 갱신 (2026-06-26): 정착지 층 런타임 LOD 시뮬 도입 → §0 참고**
> 현존 정착지는 New Game 1회 생성에 그치지 않고 **게임 중에도 계속 시뮬레이션**된다(플레이어 근처=상세/실체화, 멀리=추상). 깊은 과거 백본(창세기/고대/암흑기, §1~§16)은 기존대로 생성-1회·읽기전용 유지. 정착지 통폐합 시뮬 상세는 [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md), 검증 프로토타입은 [Tools/ChronicleSimPrototype](../Tools/ChronicleSimPrototype/README.md).

> **⚠ 현재 상태 (2026-05-19): 보류 중**
> - POCO 정의 `HistoryEntityTable` / `HistoryTemplateTable`만 [Tables.cs](../Assets/Scripts/Common/Tables.cs)에 선언되어 있고, [DownloadTables.cs](../Assets/Scripts/Editor/DownloadTables.cs)에 다운로드 메뉴/파서가 등록되어 있음
> - 그러나 `.bytes` 파일은 아직 생성되지 않았고 (`Assets/_BinaryData/TableData/`에 없음), 시스템 본체도 미구현. 호출처 0
> - [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs)의 필드 / `LoadTable` 호출 / `GetHistoryEntity` 등 Getter 4종은 **주석 처리**되어 있음 (런타임 로딩 실패 방지)
> - History 시스템 본 구현 시:
>   1. Unity 메뉴 `ARPG → Download Table → HistoryEntityTable / HistoryTemplateTable`로 bytes 생성
>   2. [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs)의 `TODO: History` 마커 주석 일괄 해제
>   3. 그 후 본 문서 §0.1 이후 단계 진행

> 상위 컨셉: 오픈월드 + NPC 자율 행동 (코어 키퍼 + 림월드 하이브리드)
> 관련 문서: [gameOverview.md](gameOverview.md) · [dungeonSystem.md](dungeonSystem.md) · [VILLAGE_GROWTH_STAGES.md](VILLAGE_GROWTH_STAGES.md)
>
> **목표**: Dwarf Fortress 스타일의 깊이를 가지되, 풀 시뮬레이션 없이 "사건 템플릿 조합으로 역사처럼 보이는 인과 사슬을 짧은 시간에 생성"한다. 결과는 맵/유적/NPC 대사/퀘스트의 소재가 된다.
>
> **위치**: New Game 시점 1회 실행 (월드 시드와 결합). 게임 중에는 Live Backbone(현재/미래 사건)만 append-only로 추가.
>
> **로드맵 위치**: [README.md](README.md) 기준 Milestone 4(콘텐츠 확장, Week 21~26) **이후**에 배치되는 별도 트랙. NPC 대사/퀘스트가 어느 정도 갖춰진 시점에 의미가 살아남.

---

## 0. 런타임 LOD 시뮬레이션 — 살아있는 정착지 (2026-06-26 방향 갱신)

> 본 절은 §1·§2의 "생성만 하고 게임 중엔 동결" 모델을 **정착지(현대) 층에 한해** 개정한다.
> 깊은 과거(창세기/고대/암흑기) 백본은 여전히 생성-1회·읽기전용(§2.1)이지만, **현존 정착지는 게임 중에도 계속 시뮬레이션**된다.

### 0.1 두 단계 + LOD (level-of-detail)

```
① New Game — 결정론적 시뮬 1회
     · 정착지 배치 + 통폐합(합병/정복/왕국 형성) → 초기 세계 상태 + 연대기 생성
     · 같은 시드 = 같은 시작 (결정론은 '생성 시점'까지만 보장 — §14.1)
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

### 0.4 v1 범위 / 단계 (2026-06-26 결정)

- **멀리-추상 시뮬은 v1엔 (가) 인구 동역학만.** 멀리서도 전쟁/합병/왕국이 계속 일어나는 **(나) 정치 사건 지속은 나중에 추가** — 저장 구조는 (나)를 수용하되 동작은 보류.
- **결정론은 생성 시점만 보장.** 런타임은 플레이어 행동·방문 시점에 따라 갈라짐(의도된 동작).
- 구현 단계: ① 저장 포맷 + 추상 틱(인구) → ② 실체화(추상→상세) + catch-up → ③ fold-back + 연대기 보존 정책 → (나중) 멀리 정치 사건 지속.

---

## 1. 핵심 철학

1. **시뮬레이션이 아닌 생성(Generation)** — 100년치 실시간 시뮬을 돌리지 않고, 템플릿 조합으로 "역사처럼 보이는" 결과만 만든다. 생성 자체는 New Game 시 수 초 내 완료. *(단, **현존 정착지 층은 §0의 런타임 LOD 시뮬로 개정** — 게임 중에도 계속 시뮬됨. 본 항목은 깊은 과거 백본에만 해당)*
2. **콘텐츠와 규칙의 분리** — 종족/신/괴물(재료) + 사건 템플릿을 데이터 테이블로, 코드는 매처/렌더러만 → 모드/확장 친화.
3. **태그 기반 매칭** — 템플릿 슬롯은 구체 ID가 아닌 태그(`ulong` 비트마스크) 조건으로 명시 → 새 재료 추가 시 기존 템플릿이 자동으로 활용.
4. **결정론** — 모든 random 호출은 시드 기반. 같은 시드 = 같은 역사 = 같은 좌표 = 같은 유적 배치.
5. **ECS 통합** — 생성된 역사는 컴포넌트로 변환되어 World에 살아간다(폐허 NPC가 기억하는 사건, 던전 입구 ambient 태그 등).
6. **프로젝트 규약 준수** — `AR.s` 글로벌 컨테이너, 테이블은 `.bytes` 바이너리 + `ImmutableDictionary<int, Table>`, 저장은 Newtonsoft JSON `WorldData` 직렬화.

---

## 2. 시간축 / 단계 분리

### 2.1 Past Backbone (과거 백본) — 게임 시작 전 생성
- 4개 시대(창세기 / 고대 / 암흑기 / 현대)를 순차 생성
- 결과물: `HistoricalEventComponent`, `SettlementComponent`, `RegionComponent.AmbientTags`
- **읽기 전용**: 게임 중에는 변경되지 않음 (유적, 전설, 책 등으로만 표현) — *단 현대 시대의 **현존 정착지는 §0 런타임 LOD 시뮬로 게임 중에도 변동**. 읽기전용은 창세기/고대/암흑기 백본에 한함*

### 2.2 Live Backbone (현재/미래 백본) — 플레이어 행동으로 추가
- 플레이어가 보스 처치/마을 설립/던전 봉인 등 큰 행동을 하면 새 `HistoricalEvent` append
- NPC 대사, 퀘스트 트리거, 마을 평판에 사용
- **Append-only**: 기존 사건은 수정하지 않고 새 사건만 추가 (인과 추적 단순화)

### 2.3 시대 구조

| 시대 | 길이 | 사건 수 | 주요 사건 | 흔적 |
|------|------|--------|----------|------|
| **창세기 (Genesis)** | 200년 | ~5 | 창조, 신들의 전쟁, 거대생물 출현 | 신화/전설 (책, NPC 대사) |
| **고대 (Ancient)** | 500년 | ~15 | 첫 문명 흥기, 마법 절정, 대재앙 | 유적, 발굴 책 |
| **암흑기 (Dark)** | 300년 | ~12 | 영웅 등장, 괴물 봉인, 부족 형성 | 영웅의 무덤, 봉인된 던전 |
| **현대 (Current)** | 100년 | ~8 | 왕국 형성, 영토 분쟁, 새 위협 | NPC가 직접 기억, 현존 마을 |

> 길이/사건 수 합 ≈ 1100년 / 40 사건. 평균 사건 간 ~27년. `year += rng.Next(10, 50)` 평균 30년과 잘 맞음. MVP 0.3 이후 실측 튜닝.

---

## 3. ECS 데이터 모델

### 3.1 데이터 구분 원칙

| 구분 | 위치 | 형태 |
|------|------|------|
| **카탈로그 (재료/템플릿)** | `Assets/_BinaryData/TableData/HistoryXxxTable.bytes` | `Tables.HistoryXxxTable` POCO → `ImmutableDictionary<int, ...>` |
| **월드 인스턴스 (사건/거점/배우)** | ECS Entity + Component | `SparseSet<XxxComponent>` |
| **공간 인덱스 (조회 캐시)** | `HistoryManager` (AR.s.History) | Dictionary 기반 빠른 조회 |
| **세이브 데이터 (Live Backbone)** | `WorldData.HistorySaveData` | Newtonsoft JSON, `Version` 마이그레이션 |

> **ScriptableObject 미사용 이유**: 프로젝트 전반이 `.bytes` 바이너리 테이블로 통일됨([DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs)). 일관성/시트 워크플로/빌드 사이즈 측면에서 동일 패턴 채택.

### 3.2 카탈로그 테이블 (디자인 타임)

```csharp
// Assets/Scripts/Tables/HistoryEntityTable.cs
namespace ARPG.Tables
{
    public class HistoryEntityTable
    {
        public int Id;                       // 1001: race_dwarf
        public string StringId;              // "race_dwarf" (디버그용)
        public string DisplayName;           // "드워프"
        public HistoryEntityType Type;       // Race, God, Monster, Place, Artifact, Hero
        public string TagsCsv;               // "지능형,산악거주,장인,호전적"  (로드 시 ulong 마스크로 변환)
        public string PreferredTerrainCsv;   // "산맥,동굴"
        public string HostileTagsCsv;        // "숲거주"
        public int StartingPopulation;       // Race일 때만 의미 (Race의 첫 인스턴스화)
        public int ThreatLevel;
        public bool EndsWithJongseong;       // 한국어 조사: 마지막 음절 종성 유무 (외국어 명사도 발음 기준)
    }

    public enum HistoryEntityType { Race = 0, God = 1, Monster = 2, Place = 3, Artifact = 4, Hero = 5 }
}
```

```csharp
// Assets/Scripts/Tables/HistoryTemplateTable.cs
namespace ARPG.Tables
{
    public class HistoryTemplateTable
    {
        public int Id;                       // 2001: tmpl_monster_invasion
        public string StringId;
        public int Weight;
        public string EraRestrictionCsv;     // "Dark" 또는 "Ancient,Dark"
        public int CooldownYears;
        public string SlotsJson;             // 슬롯 정의는 가변 길이라 JSON 직렬화
        public string TextTemplate;          // "{년도}년, {침략자}이(가) {무대}에..."
        public string EffectsJson;           // 효과 리스트(JSON) — 효과별 type/target/params
        public string CanTriggerIdsCsv;      // "2002,2007,2010"
    }

    [System.Serializable]
    public class SlotDefinition
    {
        public string Name;                  // "침략자"
        public HistoryEntityType TypeFilter;
        public string[] RequiredTags;        // AND
        public string[] PreferredTags;       // 가중치 +2
        public string[] ExcludedTags;
        public string TerrainConstraint;     // "피해자.선호_지형" 같은 cross-slot 제약 (Place 슬롯 전용)
    }
}
```

### 3.3 ECS 컴포넌트 (런타임)

```csharp
// Assets/Scripts/Common/Component/History/HistoryActorComponent.cs
namespace ARPG.Component
{
    public struct HistoryActorComponent
    {
        public int TableId;                  // HistoryEntityTable.Id
        public int Population;               // Race일 때만 유효
        public int Territory;                // 보유 region 수
        public bool IsDead;
    }

    public struct HistoryActorTagsComponent
    {
        public ulong TagsLow;                // 비트마스크 (태그 0-63)
        public ulong TagsHigh;               // 비트마스크 (태그 64-127)
    }

    public struct HistoricalEventComponent
    {
        public int TemplateId;
        public int Year;
        public HistoryEra Era;
        public int RegionEntityId;           // 사건 무대 region entity id (없으면 -1)
        public int CausedByEventEntityId;    // 부모 사건 entity id (없으면 -1)
        public ActorSlotPair Actor0;
        public ActorSlotPair Actor1;
        public ActorSlotPair Actor2;
        public ActorSlotPair Actor3;
    }

    public struct ActorSlotPair
    {
        public int SlotNameHash;             // 슬롯명 hash (예: "침략자".GetHashCode())
        public int ActorEntityId;            // 배정된 actor entity id (-1 = 미사용)
    }

    public struct RegionComponent
    {
        public int RegionId;                 // 결정론적 ID = ChunkOriginX * 100000 + ChunkOriginY
        public Vector2Int ChunkOrigin;       // region이 차지하는 청크 격자의 좌하단 (8×8 청크)
        public int BiomeId;
        public ulong TagsLow;                // 바이옴 + 자원 태그
        public ulong TagsHigh;
        public ulong AmbientTagsLow;         // 사건 결과로 누적되는 ambient (전쟁_흔적, 용_서식지)
        public ulong AmbientTagsHigh;
    }

    public struct SettlementComponent
    {
        public int OwnerActorEntityId;       // 멸망 시 -1
        public int RegionEntityId;
        public Vector2 Coord;                // region 안 결정론적 좌표
        public SettlementType Type;
        public int FoundedYear;
        public int Population;
        public SettlementState State;
    }

    public enum SettlementType
    {
        Capital, City, Village, Outpost,
        Ruin, HeroTomb, SealedDungeon, MonsterLair, AncientLibrary,
    }
    public enum SettlementState { Prosperous, Declining, Ruined, Occupied, Sealed }

    // NPC가 자기 region의 사건을 "기억"하는 컴포넌트 (System_HistoryNpcMemorySync가 갱신)
    public struct HistoryMemoryComponent
    {
        public int RegionEntityId;           // NPC가 속한 region
        public int Event0;                   // 최근 사건 entity id (-1 비어있음)
        public int Event1;
        public int Event2;                   // 최대 3개까지만 기억 (대사 풀 부담 제한)
        public float LastSyncTime;
    }
}
```

> **풀 크기 등록 (검증된 API)** — 런타임 `RegisterComponentPool<T>` API는 존재하지 않음. `ComponentManager._poolSizes` Dictionary에 직접 추가:
> ```csharp
> // ComponentManager.cs _poolSizes 안에 추가
> { typeof(HistoryActorComponent), 64 },           // 종족+신+괴물 ~ 수십
> { typeof(HistoryActorTagsComponent), 64 },
> { typeof(HistoricalEventComponent), 256 },       // ~40 백본 + Live append 여유
> { typeof(RegionComponent), 1024 },               // 32×32 region 격자
> { typeof(SettlementComponent), 512 },
> { typeof(HistoryMemoryComponent), 500 },         // NPC 수와 1:1
> ```

---

## 4. 태그 시스템

### 4.1 권장 태그 축

| 축 | 값 예시 |
|----|--------|
| **도덕** | 선한 / 중립 / 악한 |
| **스케일** | 개인 / 집단 / 거대 / 우주적 |
| **시대** | 고대 / 중세 / 현대 |
| **환경** | 산악 / 숲 / 바다 / 사막 / 화산 / 평원 |
| **사회** | 정착형 / 유목형 / 야생 / 멸종 |
| **종류** | 지능형 / 야수 / 언데드 / 정령 / 신성 |

### 4.2 비트마스크 구현

태그 문자열 → 정수 ID 매핑(`HistoryTagRegistry`) 후 `ulong` 2개(`TagsLow/TagsHigh`)에 비트로 저장. 매칭은 비트 연산 O(1).

```csharp
// Assets/Scripts/History/Data/HistoryTagRegistry.cs
namespace ARPG.History
{
    public static class HistoryTagRegistry
    {
        private static readonly Dictionary<string, int> _tagToBit = new();
        private static int _nextBit = 0;
        public const int MAX_TAGS = 128;     // ulong × 2

        public static int GetOrRegister(string tag)
        {
            if (_tagToBit.TryGetValue(tag, out int bit)) return bit;
            if (_nextBit >= MAX_TAGS)
            {
                Debug.LogError($"[History] Tag overflow at {MAX_TAGS}. Drop or alias: {tag}");
                return 0;
            }
            _tagToBit[tag] = _nextBit;
            return _nextBit++;
        }

        public static void ToMask(string[] tags, out ulong low, out ulong high)
        {
            low = 0; high = 0;
            for (int i = 0; i < tags.Length; i++)
            {
                int bit = GetOrRegister(tags[i]);
                if (bit < 64) low |= 1UL << bit;
                else high |= 1UL << (bit - 64);
            }
        }

        public static bool MatchAll(ulong actorLow, ulong actorHigh, ulong reqLow, ulong reqHigh)
            => (actorLow & reqLow) == reqLow && (actorHigh & reqHigh) == reqHigh;

        public static bool MatchAny(ulong actorLow, ulong actorHigh, ulong maskLow, ulong maskHigh)
            => (actorLow & maskLow) != 0UL || (actorHigh & maskHigh) != 0UL;

        public static int PopCount(ulong x)
            => System.Numerics.BitOperations.PopCount(x);
    }
}
```

---

## 5. 핵심 알고리즘

### 5.1 슬롯 매칭

```csharp
public static int FindCandidates(in SlotDefinition slot, List<(int entityId, int weight)> outBuffer)
{
    outBuffer.Clear();
    var cm = AR.s.Component;
    SparseSet<HistoryActorComponent> pool = cm.GetComponentPool<HistoryActorComponent>();

    HistoryTagRegistry.ToMask(slot.RequiredTags, out ulong reqLow, out ulong reqHigh);
    HistoryTagRegistry.ToMask(slot.ExcludedTags, out ulong exLow, out ulong exHigh);
    HistoryTagRegistry.ToMask(slot.PreferredTags, out ulong prefLow, out ulong prefHigh);

    for (int i = 0; i < pool.Count; i++)
    {
        int entityId = pool.GetEntityId(i);
        HistoryActorComponent actor = pool.GetByIndex(i);
        if (actor.IsDead == true) continue;

        var data = AR.s.Data.GetHistoryEntity(actor.TableId);
        if (data.Type != slot.TypeFilter) continue;

        if (cm.TryGetComponent<HistoryActorTagsComponent>(entityId, out var tags) == false)
            continue;
        if (HistoryTagRegistry.MatchAll(tags.TagsLow, tags.TagsHigh, reqLow, reqHigh) == false) continue;
        if (HistoryTagRegistry.MatchAny(tags.TagsLow, tags.TagsHigh, exLow, exHigh) == true) continue;

        int weight = 1;
        weight += HistoryTagRegistry.PopCount(tags.TagsLow & prefLow) * 2;
        weight += HistoryTagRegistry.PopCount(tags.TagsHigh & prefHigh) * 2;
        outBuffer.Add((entityId, weight));
    }
    return outBuffer.Count;
}
```

### 5.2 템플릿 추첨

```csharp
public HistoryTemplateTable PickTemplate(HistoryEra era, int currentYear, IRng rng,
    Dictionary<int, int> lastUsedYearByTemplateId)
{
    _candidatePool.Clear();
    for (int i = 0; i < _allTemplates.Count; i++)
    {
        var t = _allTemplates[i];
        if (EraMatches(t, era) == false) continue;
        if (lastUsedYearByTemplateId.TryGetValue(t.Id, out int lastYear)
            && currentYear - lastYear < t.CooldownYears) continue;

        bool allFillable = true;
        for (int s = 0; s < t.ParsedSlots.Length; s++)
        {
            if (FindCandidates(t.ParsedSlots[s], _scratchCandidates) == 0)
            { allFillable = false; break; }
        }
        if (allFillable == false) continue;
        _candidatePool.Add((t, t.Weight));
    }
    return WeightedPick(_candidatePool, rng);
}
```

### 5.3 메인 생성 루프

```csharp
public HistoryWorldData GenerateHistory(int worldSeed)
{
    // 시드 namespace 분리 — 채널별 별개 PRNG (xoshiro 권장: 결정론 보장)
    var slotRng  = new HistoryRng(DerivedSeed(worldSeed, ChannelSlotPick));
    var yearRng  = new HistoryRng(DerivedSeed(worldSeed, ChannelYearStep));
    var chainRng = new HistoryRng(DerivedSeed(worldSeed, ChannelCanTrigger));

    GenerateRegionGrid(worldSeed);             // 지리 먼저 (RegionComponent들 생성)
    LoadStartingActors(worldSeed);             // 종족/신/괴물 → HistoryActorComponent entity로 instantiate

    HistoryEra[] eras = { HistoryEra.Genesis, HistoryEra.Ancient, HistoryEra.Dark, HistoryEra.Current };
    for (int e = 0; e < eras.Length; e++)
    {
        int year = HistoryConfig.EraStartYear(eras[e]);
        int target = HistoryConfig.EraEventCount(eras[e]);
        int generated = 0;
        int failStreak = 0;

        while (generated < target && failStreak < 10)  // 무한루프 가드
        {
            year += yearRng.Range(10, 50);
            var template = PickTemplate(eras[e], year, slotRng, _lastUsed);
            if (template == null) { failStreak++; continue; }

            int eventId = GenerateEvent(template, year, eras[e], slotRng);
            _lastUsed[template.Id] = year;
            generated++;
            failStreak = 0;

            // 30% 확률 후속 연쇄
            int[] followIds = template.ParsedCanTriggerIds;
            for (int t = 0; t < followIds.Length; t++)
            {
                if (chainRng.NextDouble() >= 0.3) continue;
                var follow = GetTemplate(followIds[t]);
                if (follow == null) continue;
                int followYear = year + chainRng.Range(1, 20);
                int followEvtId = GenerateEvent(follow, followYear, eras[e], slotRng);
                LinkCausation(followEvtId, parentEventId: eventId);
            }
        }
    }
    BuildSpatialIndex();
    return _historyWorldData;
}
```

### 5.4 사건 생성과 효과 적용 — Effect ↔ ECS 통합

`IHistoryEffect.Apply()`가 직접 ECS Entity를 만들거나 변경한다. `HistoryGenerator`는 카탈로그 단계와 ECS 단계를 잇는 책임:

```csharp
public interface IHistoryEffect
{
    void Apply(int eventEntityId, ActorBinding actors, HistoryWorldData world);
}

// 예: 거점 자동 생성
public class Effect_CreateSettlement : IHistoryEffect
{
    public SettlementType Type;
    public string OwnerSlotName;             // "침략자"
    public string RegionSlotName;            // "무대"

    public void Apply(int eventEntityId, ActorBinding actors, HistoryWorldData world)
    {
        int owner = actors.GetActorBySlot(OwnerSlotName);
        int region = actors.GetActorBySlot(RegionSlotName);

        int settlementEntityId = EntityIdHelper.CreateEntity();    // 검증된 API
        var coord = DeterministicCoord.InRegion(settlementEntityId, region, world.Seed);

        AR.s.Component.AddComponent(settlementEntityId, new SettlementComponent
        {
            OwnerActorEntityId = owner,
            RegionEntityId = region,
            Coord = coord,
            Type = Type,
            FoundedYear = world.GetEventYear(eventEntityId),
            Population = 0,
            State = SettlementState.Prosperous,
        });
        world.SpatialIndex.RegisterSettlement(settlementEntityId, region);
    }
}

// 다른 효과들: Effect_SettlementsToRuins, Effect_AddAmbientTag, Effect_ModifyActorState, Effect_SealDungeon
```

> **결정론 ID 카테고리**: settlement entity는 일반 `CreateEntity()`로 발급 (시드 의존이라 같은 seed에서 같은 순서로 만들어지면 같은 ID 받음). 좌표는 `DeterministicCoord.InRegion(...)`이 별도 시드 채널로 결정.

---

## 6. 공간 배치 (Map 시스템 통합)

### 6.1 검증된 Map 구조

- [MapManager](../Assets/Scripts/Manager/MapManager.cs)는 `mapSeed = 12345` (int) 한 개로 Perlin Noise + 청크 zone(`max(|cx|,|cy|)+1`) 결정
- [MapChunkData](../Assets/Scripts/Map/MapChunkData.cs)는 `chunkX, chunkY`, `tiles[chunkSize, chunkSize]`, `zone`, `monsterSpawnPositions` 보유
- 청크 풀(`_chunkPool`) + `_activeChunks` Dictionary 운용
- 청크 범위는 `minChunkX/maxChunkX/minChunkY/maxChunkY`로 제한

### 6.2 Region 격자 — 청크 위에 한 단계 추가
- **Region 1칸 = 8×8 청크** (chunkSize가 변동 가능하므로 청크 단위로 정의)
- Region 좌표 = `(chunkX / 8, chunkY / 8)`
- `GenerateRegionGrid(worldSeed)`가 활성 청크 범위에 대해 `RegionComponent`를 미리 생성. 각 Region은 결정론 ID = `regionGridX * 100000 + regionGridY`.
- Region의 `BiomeId`는 worldSeed 기반 Perlin (Map의 zone과 별개 채널)

### 6.3 장소 슬롯 처리
- 템플릿 슬롯 `TypeFilter == Place` → region 풀에서 `RegionComponent` 후보 추출
- `TerrainConstraint` 필드로 "피해자.선호_지형 ∩ 무대.Tags" cross-slot 제약 표현 → 가중치 +5

### 6.4 결정론 좌표 — 시드 namespace 분리

```csharp
// Assets/Scripts/History/World/DeterministicCoord.cs
public static class DeterministicCoord
{
    private const ulong COORD_CHANNEL = 0xC0E1FA17u;

    public static Vector2 InRegion(int settlementId, int regionEntityId, int worldSeed)
    {
        if (AR.s.Component.TryGetComponent<RegionComponent>(regionEntityId, out var region) == false)
            return Vector2.zero;

        ulong h = Splitmix64(unchecked((ulong)worldSeed * 0x9E3779B97F4A7C15UL)
                            ^ ((ulong)regionEntityId * 0xBF58476D1CE4E5B9UL)
                            ^ ((ulong)settlementId * 0x94D049BB133111EBUL)
                            ^ COORD_CHANNEL);

        int chunkSize = AR.s.Map.ChunkSize;
        int regionPixels = chunkSize * 8;
        int dx = (int)(h % (ulong)regionPixels) - regionPixels / 2;
        int dz = (int)((h >> 32) % (ulong)regionPixels) - regionPixels / 2;

        Vector2 centerWorld = new Vector2(
            (region.ChunkOrigin.x + 4) * chunkSize,    // region 중심
            (region.ChunkOrigin.y + 4) * chunkSize);
        Vector2 raw = centerWorld + new Vector2(dx, dz);
        return AdjustForTerrain(raw, region);          // 물/언덕 보정
    }

    // SplitMix64 — 분포 균일하고 결정론적. System.HashCode는 .NET 버전별로 다를 수 있어 직접 구현.
    private static ulong Splitmix64(ulong x)
    {
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
```

> **결정론 주의**: .NET의 `System.Random` 구현은 .NET 버전별로 분포가 다르다(Net Framework vs .NET 6+). 본 시스템 PRNG는 **xoshiro256\*\*** 또는 **SplitMix64** 자체 구현 권장 — `HistoryRng` 클래스에 캡슐화.

### 6.5 공간 인덱스 — 청크 조회 API

```csharp
// Assets/Scripts/History/World/HistorySpatialIndex.cs
public class HistorySpatialIndex
{
    // Region entity → Region 정보 + 거기 속한 거점/사건
    private readonly Dictionary<int, RegionRecord> _byRegionEntityId = new();
    // 청크 좌표 → 거점 entity 리스트 (청크 진입 시 즉시 조회)
    private readonly Dictionary<Vector2Int, List<int>> _settlementsByChunk = new();

    public class RegionRecord
    {
        public int RegionEntityId;
        public List<int> SettlementEntityIds = new();
        public List<int> EventEntityIds = new();
    }

    public IReadOnlyList<int> GetSettlementsInChunk(Vector2Int chunkCoord)
    {
        if (_settlementsByChunk.TryGetValue(chunkCoord, out var list))
            return list;
        return System.Array.Empty<int>();
    }

    public RegionRecord GetRegion(int regionEntityId)
        => _byRegionEntityId.TryGetValue(regionEntityId, out var r) ? r : null;

    public void RegisterSettlement(int settlementEntityId, int regionEntityId) { ... }
}
```

### 6.6 청크 생성 파이프라인 — `_tileModifications` 우회

#### 6.6.1 검증된 기존 흐름

[MapManager_Generator.cs:73-143](../Assets/Scripts/Map/MapManager_Generator.cs#L73-L143) `GenerateChunkData`:
```
1. Perlin 지형 + 몬스터 스폰 후보 (mapSeed 기반)
2. OverlayMapFileData          ← BaseTown.bytes 등 손으로 만든 마을 청사진 오버레이
3. OverlayTileModifications    ← 플레이어가 PlaceObject로 누적한 영구 기록 (_tileModifications)
4. 몬스터 스폰 후보 최종 확정
```

청크 활성화는 [MapManager_Spawner.cs](../Assets/Scripts/Map/MapManager_Spawner.cs)의 `OnChunkActivated(chunkCoord, chunk)`에서 처리되며, NPC/Building이 같은 hook 패턴을 사용한다.

#### 6.6.2 History 통합 — 신규 단계

본 시스템은 기존 단계 2 안에 통합되고, 별도 단계 3을 추가한다:

```
1. Perlin 지형
2. OverlayMapFileData(chunk)
     ├─ 고정 청사진 (_mapFileDataDic — BaseTown 등 위치 고정 일품)
     └─ 백본 청사진 (_historicalBlueprints + HistorySpatialIndex — 위치 동적, 같은 청사진 N회 재사용)
3. OverlayHistoricalMarkers(chunk)   ← NEW: L1/L2 마커 (단순 비트 패턴, .bytes 없음)
4. OverlayTileModifications(chunk)   ← 기존: 플레이어 행동 (반드시 마지막에 와야 백본을 덮어쓸 수 있음)
5. 몬스터 스폰 후보 최종 확정
```

> **순서 핵심**: `History → TileModifications` 순서가 강제. 거꾸로 두면 플레이어가 폐허 위에 놓은 침대가 다음 청크 재생성 시 사라짐.

#### 6.6.3 `_tileModifications` 우회 메커니즘

**핵심 원칙**: 백본 거점은 `MapManager.PlaceObject()`를 호출하지 않는다. 대신 `chunk.tiles[x, y]` 비트만 직접 갱신.

```csharp
// 우회 — SetTileModification() 호출 X
ulong cur = chunk.tiles[localX, localY];
cur &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
cur |= ((ulong)objectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask;
cur |= (ulong)GlobalEnum.TileFlag.Blocked;
cur &= ~(ulong)GlobalEnum.TileFlag.MonsterSpawn;
chunk.tiles[localX, localY] = cur;
```

→ [SaveTileModifications](../Assets/Scripts/Manager/MapManager.cs#L454)는 `_tileModifications` 딕셔너리만 직렬화하므로 백본 거점은 세이브에 0바이트.
→ 로드 시 시드 기반으로 `GenerateChunkData`가 동일 비트 재생성. 결정론 보장이 전제.

**세이브 크기 비교** (60 거점 가정):

| 접근 | 추가 바이트 |
|------|-----------|
| `PlaceObject` 호출 | ~8.6KB (540 entry × 16B) |
| 본 우회 + Live Delta만 | <100B (변경된 거점만 `HistorySaveData.LiveSettlements` 델타) |

#### 6.6.4 거점 비주얼 표현 — 4단계 레벨

거점 *내부 타일 배치는 절차 생성이 아닌 손으로 만든 청사진*. 절차적 측면은 위치/타입/variant 선택뿐 (Skyrim 모델, §16 참고).

| 레벨 | 표현 | 예시 | 데이터 위치 |
|------|------|------|-----------|
| **L1** 마커 | 1×1 타일 비트 (단일 objectId) | 비석, 영웅 묘비, 봉인 표식 | `BuildableItemTable` ID |
| **L2** 소구조 | 2×2~3×3 타일 비트 패턴 (코드 하드코딩) | 폐허 잔해, 동굴 입구 | `MapHistoryBinding.Patterns[]` |
| **L3** 청사진 | `.bytes` 오버레이 (5×5+, 손으로 디자인) | 영웅 묘소, 고대 도서관, 큰 폐허 도시 | `Assets/_BinaryData/TilemapData/Ruin_*.bytes` |
| **L4** 내부 던전 | 입구 1칸만 청크에 두고, 입장 시 던전 시스템이 내부 생성 | SealedDungeon | 별도 시스템 (미구현) |

L1/L2는 `OverlayHistoricalMarkers`에서, L3는 `OverlayMapFileData`에 통합되어 처리. L4 내부는 본 시스템 책임 밖.

#### 6.6.5 `OverlayMapFileData` 통합 모델

L3 청사진과 BaseTown은 본질이 같으므로 함수 통합. 데이터 채널만 분리:

```
[데이터 채널 — 분리 유지]
_mapFileDataDic       : Dictionary<Vector2Int(StartPosition), MapFileData>   (BaseTown 류, 위치 .bytes 내부)
_historicalBlueprints : Dictionary<string(blueprintKey), MapFileData>        (Ruin_Small 등, 1회 로드 후 N회 재사용)
HistorySpatialIndex._settlementsByChunk : Dictionary<Vector2Int, List<int>>  (청크 → 거점 entity id)

[적용 로직 — 통합]
OverlayMapFileData(chunk)
  ├─ for each entry in _mapFileDataDic:
  │    └─ OverlayBlueprintAt(chunk, entry.Value, entry.Value.StartPosition)  ← 공유 헬퍼
  │
  └─ for each settlementId in spatialIndex.GetSettlementsInChunk(chunkCoord):
       ├─ Component.Get<SettlementComponent>(settlementId) → coord, type, state
       ├─ blueprint = _historicalBlueprints[MapHistoryBinding.Resolve(type, state).BlueprintKey]
       └─ OverlayBlueprintAt(chunk, blueprint, settlement.Coord)             ← 동일 헬퍼
```

신규 헬퍼 `OverlayBlueprintAt(chunk, blueprint, worldOrigin)` — 기존 `OverlayMapFileData` 본문의 겹침 계산 로직([MapManager_Generator.cs:145-194](../Assets/Scripts/Map/MapManager_Generator.cs#L145-L194))을 추출. BaseTown은 `entry.Value.StartPosition`, 거점은 `settlement.Coord`를 origin으로 전달.

#### 6.6.6 청사진 variant 결정론 선택

같은 거점 타입이 N variant를 가질 때 어느 것을 쓸지:

```csharp
ulong h = Splitmix64((ulong)settlement.RegionEntityId * 0xA001UL
                   ^ (ulong)settlementEntityId);
int variantIndex = (int)(h % (ulong)binding.VariantCount);
MapFileData blueprint = _historicalBlueprints[binding.BlueprintKeys[variantIndex]];
```

같은 시드 = 같은 region/settlementId = 같은 variant.

#### 6.6.7 `OnChunkActivated` hook은 ECS/VFX 트리거 전용

청사진/마커 적용은 `GenerateChunkData` 안에서 처리되므로, `OnChunkActivated`에 추가하는 hook은 ECS 측 작업만:

```
OnChunkActivated(chunkCoord, chunk)
  ├─ Monster.ActivateChunkMonsters         (기존)
  ├─ Npc.OnChunkActivated                  (기존)
  ├─ Building.OnChunkActivated             (기존)
  └─ History.OnChunkActivated              ← NEW
       ├─ AmbientTag → VFX/조명 트리거 (System_HistoryAmbientRefresh가 처리)
       └─ Lore 아이템 spawn (책/유물 entity 생성)
```

타일 비트는 `GenerateChunkData` 단계에서 이미 들어가 있으므로 `OnChunkActivated`는 *시각/엔티티 효과* 위주.

---

## 7. System / Priority 배치 (검증됨)

### 7.1 빈 슬롯 검증

[SystemManager.cs](../Assets/Scripts/Manager/SystemManager.cs)에서 기존 사용 priority:

| Phase | 사용 priority |
|-------|--------------|
| Update | 0, 5, 40, 50, 55, 500(Anim), 1000 |
| FixedUpdate | 30, 50, 52, 56, 58.5, 60, 61, 66, 67, 68, 70, 80, 100, 120(MapChunkLoader), 130, 150, 200, 220, 240, 245, 250, 500(EntityActivation) |
| LateUpdate | 900, 950 |

> ⚠️ 초안의 120/500은 **MapChunkLoader/Animation/EntityActivation과 충돌** → 아래로 재배정.

### 7.2 신규 시스템

| 시스템 | Phase | Priority | 책임 |
|--------|-------|----------|------|
| `System_HistoryLiveEventCollector` | FixedUpdate | **260** | broadcast 메시지(BossDefeated/VillageFounded 등) → `HistoricalEventComponent` append |
| `System_HistoryNpcMemorySync` | FixedUpdate | **480** | UpdateInterval 60s. NPC가 자기 region의 최근 사건을 `HistoryMemoryComponent`로 캐시 → 대사 풀에 노출 |
| `System_HistoryAmbientRefresh` | LateUpdate | **820** | UpdateInterval 1s. 청크 진입/전환 시 region.AmbientTags → VFX/조명 트리거. EntityMessage(900) 전에 끝나야 함 |

> 역사 생성 본체(`HistoryGenerator`)는 **시스템이 아님**. `AR.s.History.GenerateOrLoad(worldSeed)`를 `AR.Initialize()`에서 `_componentManager.Initialize()` 직후에 한 번 호출 → 결과를 `HistoryManager`에 보관 → 이후 시스템들이 읽기 전용으로 참조.

### 7.3 등록 예시 (SystemManager.Initialize() 추가)

```csharp
// Priority 260: History Live Event Collector (FixedUpdate)
//   - broadcast 메시지를 받아 새 HistoricalEventComponent를 append (보스 처치/마을 설립/봉인)
RegisterSystems(new System_HistoryLiveEventCollector());

// Priority 480: History NPC Memory Sync (FixedUpdate, 60s interval)
//   - NPC가 속한 region의 최근 사건 3개를 HistoryMemoryComponent로 갱신
RegisterSystems(new System_HistoryNpcMemorySync());

// Priority 820: History Ambient Refresh (LateUpdate, 1s interval)
//   - 청크 전환 시 region.AmbientTags를 VFX/조명 트리거에 매핑
RegisterSystems(new System_HistoryAmbientRefresh());
```

---

## 8. Live Backbone — 트리거와 직렬화

### 8.1 트리거 소스 (브로드캐스트 메시지)

기존 [MessageManager.Broadcast<T>](../Assets/Scripts/Manager/MessageManager.cs)를 활용. 새 브로드캐스트 메시지 정의:

```csharp
// Assets/Scripts/Common/Message/HistoryMessages.cs
namespace ARPG.Message
{
    public struct BossDefeatedBroadcast       // System_HpCheck가 보스 사망 시 발행
    {
        public int BossEntityId;
        public int RegionEntityId;
        public int PlayerEntityId;
    }
    public struct VillageFoundedBroadcast     // Village 도메인이 새 마을 등록 시 발행
    {
        public int VillageId;
        public int RegionEntityId;
    }
    public struct DungeonSealedBroadcast { ... }
}
```

`System_HistoryLiveEventCollector`는 `OnCreate()`에서 `AR.s.Message.Subscribe<BossDefeatedBroadcast>(OnBossDefeated)` 식으로 구독. 핸들러는 해당 region의 `RegionEntityId`와 사건 템플릿을 찾아 `HistoricalEventComponent`를 새 entity로 append하고 `HistoryWorldData.LiveEvents`에 ID를 등록.

### 8.2 직렬화 (`WorldData` 확장)

```csharp
// WorldData.cs 확장 — Phase H에서 추가
public class WorldData
{
    public ushort Version = ...;       // CURRENT_WORLD_DATA_VERSION 올림
    // ... 기존 필드 ...
    public HistorySaveData HistorySaveData = new();
}

[System.Serializable]
public class HistorySaveData
{
    public int WorldSeed;                      // 같은 시드 = 같은 백본 재생성 (저장 불요)
    public int LastEventIdSequence;            // append 순서 추적
    public List<HistoricalEventSnapshot> LiveEvents = new();   // 백본 이후 추가된 사건만
    public List<SettlementSnapshot> LiveSettlements = new();   // 플레이어 발견/생성 거점
    public List<RegionAmbientDelta> RegionDeltas = new();      // 백본 이후 ambient 변화
}

[System.Serializable]
public class HistoricalEventSnapshot
{
    public int TemplateId;
    public int Year;
    public HistoryEra Era;
    public int RegionEntityId;
    public int CausedByEventId;
    public List<ActorSlotSnapshot> Actors = new();
    public string CachedRenderedText;          // 한국어 조사 처리 완료된 최종 텍스트
}
```

> **백본은 저장하지 않는다** — `WorldSeed`만 있으면 결정론적으로 재생성 가능. Live Backbone(append만) + Delta(변경분)만 직렬화.

---

## 9. 한국어 조사 처리

```csharp
// Assets/Scripts/History/Generator/TextTemplating/KoreanJosaResolver.cs
public static class KoreanJosaResolver
{
    public enum JosaType { IGa, EulReul, EunNeun, WaGwa, EuroRo }

    // 한글 마지막 음절 종성(받침) 유무 자동 판단
    public static bool HasJongseong(char c)
    {
        if (c >= 0xAC00 && c <= 0xD7A3)
            return ((c - 0xAC00) % 28) != 0;
        return false;  // 한글 외 문자
    }

    // 외국어 명사는 HistoryEntityTable.EndsWithJongseong 필드 사용
    public static string Pick(string noun, JosaType type, bool fallbackJongseong = false)
    {
        bool hasJ = false;
        if (string.IsNullOrEmpty(noun) == false)
        {
            char last = noun[noun.Length - 1];
            if (last >= 0xAC00 && last <= 0xD7A3)
                hasJ = HasJongseong(last);
            else
                hasJ = fallbackJongseong;
        }
        switch (type)
        {
            case JosaType.IGa:    return hasJ ? "이" : "가";
            case JosaType.EulReul: return hasJ ? "을" : "를";
            case JosaType.EunNeun: return hasJ ? "은" : "는";
            case JosaType.WaGwa:   return hasJ ? "과" : "와";
            case JosaType.EuroRo:  return hasJ ? "으로" : "로";
        }
        return "";
    }
}
```

템플릿은 `"{침략자}이(가) {무대}에..."` 형태로 두 후보를 같이 적고, 렌더링 시 `{침략자}이(가)` 토큰을 `{침략자}` + 조사로 교체.

---

## 10. 파일 구조

```
Assets/Scripts/
├── History/
│   ├── HistoryManager.cs                # MonoBehaviour, AR.s.History — Initialize/GenerateOrLoad/Reset
│   ├── Data/
│   │   ├── HistoryDataLoader.cs         # .bytes 테이블 로드 후 DataManager에 등록
│   │   ├── HistoryTagRegistry.cs        # 태그 ↔ 비트 ID
│   │   └── HistoryConfig.cs             # ScriptableObject 또는 정적 클래스 (시대 길이/사건 수)
│   ├── Generator/
│   │   ├── HistoryGenerator.cs          # 메인 루프 (§5.3)
│   │   ├── SlotMatcher.cs               # §5.1
│   │   ├── TemplatePicker.cs            # §5.2 + WeightedPick
│   │   ├── EventGenerator.cs            # 슬롯 배정 → effects 적용
│   │   ├── HistoryRng.cs                # SplitMix64 기반 자체 PRNG (결정론 보장)
│   │   ├── DerivedSeed.cs               # 채널 namespace 분리
│   │   ├── TextTemplating/
│   │   │   ├── HistoryTextRenderer.cs
│   │   │   └── KoreanJosaResolver.cs    # §9
│   │   └── Effects/
│   │       ├── IHistoryEffect.cs
│   │       ├── Effect_CreateSettlement.cs
│   │       ├── Effect_SettlementsToRuins.cs
│   │       ├── Effect_AddAmbientTag.cs
│   │       ├── Effect_ModifyActorState.cs
│   │       └── Effect_SealDungeon.cs
│   ├── World/
│   │   ├── HistoryWorldData.cs          # 생성 결과 보관 (entity id 모음)
│   │   ├── HistorySpatialIndex.cs       # §6.5
│   │   ├── RegionGenerator.cs           # §6.2 (worldSeed → RegionComponent들)
│   │   └── DeterministicCoord.cs        # §6.4
│   └── System/
│       ├── System_HistoryLiveEventCollector.cs    # Priority 260
│       ├── System_HistoryNpcMemorySync.cs         # Priority 480 (60s)
│       └── System_HistoryAmbientRefresh.cs        # Priority 820 (1s, LateUpdate)
├── Common/Component/History/
│   ├── HistoryActorComponent.cs
│   ├── HistoryActorTagsComponent.cs
│   ├── HistoricalEventComponent.cs
│   ├── RegionComponent.cs
│   ├── SettlementComponent.cs
│   └── HistoryMemoryComponent.cs
├── Common/Message/
│   └── HistoryMessages.cs               # BossDefeatedBroadcast 등 §8.1
└── Tables/
    ├── HistoryEntityTable.cs
    └── HistoryTemplateTable.cs

Assets/_BinaryData/TableData/             # 기존 테이블과 같은 위치 + 같은 .bytes 포맷
├── HistoryEntityTable.bytes
└── HistoryTemplateTable.bytes
```

> **테이블 로더 추가**: [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs)의 `LoadTableAsync()` 내부 `Task.WhenAll(...)`에 `LoadTable<HistoryEntityTable>("HistoryEntityTable.bytes", t => _historyEntityTable = t)` 추가.

---

## 11. 데이터 예시

### 11.1 HistoryEntityTable (CSV → .bytes로 변환)

| Id | StringId | DisplayName | Type | TagsCsv | PreferredTerrainCsv | EndsWithJongseong |
|----|----------|-------------|------|---------|---------------------|-------------------|
| 1001 | race_dwarf | 드워프 | Race | 지능형,정착형,산악거주,장인,호전적 | 산맥,동굴 | false |
| 1002 | race_elf | 엘프 | Race | 지능형,정착형,숲거주,장인,평화적 | 숲,평원 | true |
| 2001 | monster_drakaar | 드라카르 | Monster | 거대,고대,화염,용 | 화산,산맥 | true |

### 11.2 HistoryTemplateTable (괴물 침공 예시)

| Id | StringId | Weight | EraRestrictionCsv | CooldownYears |
|----|----------|--------|-------------------|---------------|
| 3001 | tmpl_monster_invasion | 10 | Dark | 100 |

**SlotsJson**:
```json
[
  { "Name": "침략자", "TypeFilter": "Monster", "RequiredTags": ["거대"],
    "PreferredTags": ["고대", "화염"], "ExcludedTags": [] },
  { "Name": "피해자", "TypeFilter": "Race",
    "RequiredTags": ["지능형", "정착형"], "PreferredTags": [], "ExcludedTags": ["야생"] },
  { "Name": "무대", "TypeFilter": "Place", "RequiredTags": [],
    "PreferredTags": [], "ExcludedTags": [], "TerrainConstraint": "피해자.선호_지형" }
]
```

**TextTemplate**: `"{년도}년, {침략자}이(가) {무대}에 나타나 {피해자}의 도시들을 차례로 무너뜨렸다."`

**EffectsJson**:
```json
[
  { "Type": "ModifyActorState", "TargetSlot": "피해자",
    "Field": "Population", "Op": "Multiply", "Value": 0.3 },
  { "Type": "SettlementsToRuins", "RegionSlot": "무대", "OwnerSlot": "피해자" },
  { "Type": "AddAmbientTag", "RegionSlot": "무대", "Tags": ["위험지대", "황폐화"] },
  { "Type": "CreateSettlement", "RegionSlot": "무대",
    "OwnerSlot": "침략자", "SettlementType": "MonsterLair" }
]
```

**CanTriggerIdsCsv**: `3002,3007,3010` (영웅 도전, 대탈출, 암흑기 본격화)

---

## 12. 구현 로드맵 (MVP 단계)

> 각 단계는 이전 단계를 깨지 않고 점진적으로 빌드. README Milestone 4 이후에 배치 (NPC 대사 시스템 의존).

| MVP | 범위 | 산출물 |
|-----|------|--------|
| **0.1** 테이블 로딩 | `HistoryEntityTable`/`HistoryTemplateTable` 정의 + DataManager 통합 | 콘솔에 종족 3, 신 2 로드 출력 |
| **0.2** 단일 템플릿 실행 | 슬롯 매칭 + 텍스트 렌더 (조사 처리 포함) | 사건 1건 콘솔 출력 |
| **0.3** 다중 템플릿 + 시간 흐름 | 템플릿 5개, 100년 시뮬 | 시대 1개에서 N개 사건 시퀀스 |
| **0.4** 인과 사슬 | `CanTriggerIds` 30% 확률 연쇄 | 부모/자식 사건 트리 출력 |
| **0.5** 상태 변화 | `IHistoryEffect` 핸들러, IsDead 가드 | 멸망 종족은 이후 슬롯에서 제외 |
| **0.6** 지리 통합 | `RegionGenerator` + Place 슬롯 | region에 종족 자동 배치 |
| **0.7** 거점 시스템 | `SettlementComponent` + 결정론 좌표 (SplitMix64) | 폐허/괴물둥지 자동 생성 |
| **0.8** 공간 인덱스 | `HistorySpatialIndex` | 청크 좌표 → 거점 조회 API |
| **0.9** 청크 생성 연동 | AmbientTag → 분위기 / 구조물 배치 (MapManager hook) | 게임 안에서 폐허 진입 가능 |
| **1.0** Live Backbone | `System_HistoryLiveEventCollector` + `HistoryMemoryComponent` + `WorldData.HistorySaveData` | 플레이어 행동이 NPC 대사에 반영, 저장/로드 |

---

## 13. 작업 계획 (Phase 분해)

§12 MVP 단계를 PR/일감 단위로 재구성. 각 Phase 끝에서 **빌드 성공 + 동작 가능**한 상태로 마감하여 단독 PR 가능.

```
Phase 0 (인프라)        ──┐
Phase 1 (카탈로그+단일)   ├─→ Phase 2 (루프+효과)
Phase 3 (지리+거점)      ──┴─→ Phase 4 (청크 통합)
Phase 5 (Live Backbone)
```

총 **약 19~25 작업일** + 콘텐츠(템플릿/엔티티 .bytes 행) 작성 별도.

### 13.1 Phase 0 — 인프라 셋업 (2~3일)

**목표**: 다른 Phase들이 의존할 뼈대 마련. 이 Phase 끝에서는 사건 생성 0이지만 빌드 성공 + `HistoryManager Initialized` 로그.

| 신규 파일 | 책임 |
|-----------|------|
| `Assets/Scripts/History/HistoryManager.cs` | MonoBehaviour, `AR.s.History` 노출. `Initialize/GenerateOrLoad/Reset` 골격 |
| `Assets/Scripts/History/Generator/HistoryRng.cs` | SplitMix64 PRNG (`Range/NextDouble/NextULong`) |
| `Assets/Scripts/History/Generator/DerivedSeed.cs` | `Derive(worldSeed, channel)` 채널 분리 |
| `Assets/Scripts/History/Data/HistoryTagRegistry.cs` | 태그 ↔ 비트 ID + 비트 매칭 유틸 |
| `Assets/Scripts/History/Data/HistoryEra.cs` | enum (Genesis/Ancient/Dark/Current) |
| `Assets/Scripts/Common/Component/History/HistoryActorComponent.cs` | struct |
| `Assets/Scripts/Common/Component/History/HistoryActorTagsComponent.cs` | struct (TagsLow/TagsHigh) |
| `Assets/Scripts/Common/Component/History/HistoricalEventComponent.cs` | struct + `ActorSlotPair` |
| `Assets/Scripts/Common/Component/History/RegionComponent.cs` | struct |
| `Assets/Scripts/Common/Component/History/SettlementComponent.cs` | struct + `SettlementType/State` enums |
| `Assets/Scripts/Common/Component/History/HistoryMemoryComponent.cs` | struct |

| 수정 파일 | 변경 |
|-----------|------|
| [AR.cs](../Assets/Scripts/Manager/AR.cs) | `_historyManager` 필드, `History` 프로퍼티, `Initialize/Reset`에 호출 추가 |
| [ComponentManager.cs](../Assets/Scripts/Manager/ComponentManager.cs) `_poolSizes` | History 컴포넌트 6종 풀 크기 등록 |

**DoD**:
- Play 진입 시 `HistoryManager Initialized` 로그
- ECS 컴포넌트 풀 6개 생성 로그 확인
- `HistoryRng(42).NextULong()` 두 번 호출 시 결정론 (단위 테스트)

### 13.2 Phase 1 — 카탈로그 + 단일 사건 (3~4일, MVP 0.1~0.2)

**목표**: 테이블 → 슬롯 매칭 → 텍스트 렌더 → 콘솔에 사건 한 줄 출력.

| 신규 파일 | 책임 |
|-----------|------|
| `Assets/Scripts/Tables/HistoryEntityTable.cs` | POCO 테이블 |
| `Assets/Scripts/Tables/HistoryTemplateTable.cs` | POCO + `ParsedSlots/ParsedEffects` 캐시 |
| `Assets/Scripts/History/Data/HistoryDataLoader.cs` | 테이블에서 `HistoryActorComponent` 인스턴스화 |
| `Assets/Scripts/History/Generator/SlotMatcher.cs` | `FindCandidates(slot, outBuffer)` |
| `Assets/Scripts/History/Generator/TextTemplating/HistoryTextRenderer.cs` | 토큰 치환 + 조사 적용 |
| `Assets/Scripts/History/Generator/TextTemplating/KoreanJosaResolver.cs` | §9 그대로 |
| `Assets/Scripts/History/Generator/HistoryGenerator.cs` | `GenerateSingleEventForTest()` 진입점 |
| `Assets/_BinaryData/TableData/HistoryEntityTable.bytes` | 종족 3 + 신 1 + 괴물 1 |
| `Assets/_BinaryData/TableData/HistoryTemplateTable.bytes` | `tmpl_monster_invasion` 1개 |

| 수정 파일 | 변경 |
|-----------|------|
| [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs) | `LoadTableAsync` `Task.WhenAll`에 2줄 추가. `_historyEntityTable` 필드 + `GetHistoryEntity(int id)` 메서드 |
| `HistoryManager.cs` | 1차 골격 — 테이블 → actor entity instantiate |

**검증**: 임시 cheat 명령으로 한 줄 출력 `[423년, 드라카르가 산맥에 나타나 드워프의 도시들을...]` + KoreanJosaResolver 5케이스 단위 테스트.

**선행 확인**: `.bytes` 직렬화 포맷(MessagePack/Protobuf/Newtonsoft) 30분 조사 — 기존 `LoadTable<T>` 내부 구현 1줄만 확인.

### 13.3 Phase 2 — 시뮬레이션 루프 + 효과 (4~5일, MVP 0.3~0.5)

**목표**: 100년 시뮬, 5+ 템플릿, 인과 사슬, 상태 변화, 멸망 가드.

| 신규 파일 | 책임 |
|-----------|------|
| `Assets/Scripts/History/Data/HistoryConfig.cs` | 시대별 시작년/길이/사건 수 |
| `Assets/Scripts/History/Generator/TemplatePicker.cs` | `PickTemplate` + `WeightedPick` |
| `Assets/Scripts/History/Generator/EventGenerator.cs` | 슬롯 배정 → 효과 적용 → entity 생성 |
| `Assets/Scripts/History/Generator/Effects/IHistoryEffect.cs` | 인터페이스 + `ActorBinding` |
| `Assets/Scripts/History/Generator/Effects/Effect_ModifyActorState.cs` | Population/IsDead 변경 |
| `Assets/Scripts/History/Generator/Effects/Effect_AddAmbientTag.cs` | RegionComponent.AmbientTags 비트 ON |
| `Assets/Scripts/History/World/HistoryWorldData.cs` | 생성 결과 entity id 모음 + `_lastUsedYearByTemplateId` |
| `Assets/Scripts/History/World/EffectFactory.cs` | EffectsJson → IHistoryEffect 변환 |
| 템플릿 4개 추가 (`.bytes`) | `tmpl_first_civilization`, `tmpl_great_plague`, `tmpl_hero_challenge`, `tmpl_great_exodus` |

| 수정 파일 | 변경 |
|-----------|------|
| `HistoryGenerator.cs` | §5.3 메인 루프 완성. `failStreak < 10` 가드, 30% 후속 연쇄 |

**검증**: `cheat history full 12345` → 4시대 × ~40 사건 출력. 같은 시드 두 번 실행 100% 동일. 멸망 종족 후속 슬롯 미등장 확인.

### 13.4 Phase 3 — 지리 + 거점 (4~5일, MVP 0.6~0.8)

**목표**: Region 격자, Place 슬롯, 거점 자동 생성, 결정론 좌표, 공간 인덱스.

| 신규 파일 | 책임 |
|-----------|------|
| `Assets/Scripts/History/World/RegionGenerator.cs` | 32×32 region 격자 + 바이옴 Perlin (별도 시드 채널) |
| `Assets/Scripts/History/World/DeterministicCoord.cs` | §6.4 SplitMix64 좌표 |
| `Assets/Scripts/History/World/HistorySpatialIndex.cs` | `_byRegionEntityId` + `_settlementsByChunk` |
| `Assets/Scripts/History/Generator/Effects/Effect_CreateSettlement.cs` | §5.4 그대로 |
| `Assets/Scripts/History/Generator/Effects/Effect_SettlementsToRuins.cs` | owner의 settlement.State = Ruined |
| `Assets/Scripts/History/Generator/Effects/Effect_SealDungeon.cs` | settlement.State = Sealed, Type = SealedDungeon |

| 수정 파일 | 변경 |
|-----------|------|
| `SlotMatcher.cs` | `TypeFilter == Place` 처리 + `TerrainConstraint` cross-slot 가중치 |
| `HistoryGenerator.cs` | `GenerateRegionGrid` 호출 추가 |
| `HistoryWorldData.cs` | `SpatialIndex` 필드 + `BuildSpatialIndex()` |

**검증**: `cheat history dumpRegions` → region 32×32 출력. 같은 시드에서 settlement 좌표 100% 재현.

### 13.5 Phase 4 — 청크 통합 (3~4일, MVP 0.9)

**목표**: 게임 플레이 중 폐허/구조물이 청크에 가시화. **`_tileModifications` 우회** 메커니즘 적용 ([§6.6.3](#663-_tilemodifications-우회-메커니즘)).

#### 단계 분할 (2 step)

| Step | 작업 | 일수 |
|------|------|------|
| 4a (첫 통과) | 별도 함수 `OverlayHistoricalStructures`로 우선 추가. L1/L2 마커만 (단일 타일 + 3×3 패턴). BaseTown 코드는 건드리지 않음 — 회귀 위험 0. | 1~2일 |
| 4b (헬퍼 추출) | 동작 검증 후 `OverlayBlueprintAt` 헬퍼 추출 → BaseTown(`OverlayMapFileData`)과 L3 청사진을 같은 헬퍼로 통합 ([§6.6.5](#665-overlaymapfiledata-통합-모델)). | 1~2일 |

L3 청사진 데이터(.bytes 파일들) 작성은 콘텐츠 작업 ([§13.8](#138-작업-분량-요약-코드--콘텐츠))에 포함되며 Phase 4 코드 작업과 분리.

#### 신규 파일

| 파일 | 책임 |
|------|------|
| `Assets/Scripts/History/Integration/MapHistoryBinding.cs` | `Resolve(SettlementType, SettlementState)` → `StructureLevel + objectIds/Patterns/BlueprintKeys[variantCount]` 매핑 |
| `Assets/Scripts/History/Integration/OverlayHistoricalMarkers.cs` | L1/L2 마커 적용 (단일 ID + 3×3 패턴, `_tileModifications` 우회) |
| `Assets/Scripts/History/Integration/ChunkAmbientApplier.cs` | AmbientTag → VFX/조명 트리거 (`OnChunkActivated`에서 호출) |
| `Assets/Scripts/History/Integration/LoreItemPlacer.cs` | 책/비석 entity spawn (Phase 4는 stub, Phase 5에서 본격) |

#### 수정 파일

| 파일 | 변경 |
|------|------|
| [MapManager_Generator.cs](../Assets/Scripts/Map/MapManager_Generator.cs) `GenerateChunkData` | Step 2와 3 사이에 신규 `OverlayHistoricalMarkers(chunk)` 호출 ([§6.6.2](#662-history-통합--신규-단계)). 4b에서 `OverlayMapFileData`를 통합 모델로 리팩토링 |
| [MapManager.cs](../Assets/Scripts/Manager/MapManager.cs) | `chunkSize`는 이미 public 필드 — 추가 작업 없음 |
| [MapManager_Spawner.cs](../Assets/Scripts/Map/MapManager_Spawner.cs) `OnChunkActivated` | `AR.s.History.OnChunkActivated(chunkCoord)` 호출 추가 (VFX/Lore 트리거) |
| `HistoryManager.cs` | `OnChunkActivated(chunkCoord)` 메서드 추가, L3 청사진 `.bytes` lazy load (`_historicalBlueprints` 캐시) |

#### 검증

- 시드 12345 백본 → 폐허 region 청크 진입 시 마커 visible
- 청크 unload → reload 시 결정론 유지 (`_tileModifications`는 비어있어야 함 — 디버그 로그 확인)
- 플레이어가 폐허 칸 위에 빌딩 배치 → 빌딩이 폐허를 덮어씀 → 다음 청크 재생성 시 빌딩 유지 (`OverlayTileModifications`가 마지막에 적용되므로)
- `SaveTileModifications` 호출 후 entry 수가 플레이어 행동 수와 일치 (백본 거점 entry 0)

#### 리스크

- `GenerateChunkData`는 hot path. Step 4a에서 청크 생성 시간 측정 (BaseTown만 있을 때 vs 백본 거점 60개 등록 후). 60 거점 × bounding box 검사가 부담되면 Step 4b에서 공간 인덱스 도입 (`_settlementsByChunk` 직접 조회)
- 폐러 스프라이트 placeholder: Phase C `PalisadeGate` 패턴(임시 색 박스)으로 시작 → 콘텐츠 Phase에서 정식 아트로 교체
- L3 청사진의 좌표 충돌 — settlement.Coord가 region 경계 근처면 청사진이 다른 region까지 침범 가능. `OverlayBlueprintAt`의 bounding box 클리핑이 자동 처리하므로 코드는 안전하나, 비주얼이 잘릴 수 있음 → 거점 좌표 결정 시 청사진 크기만큼 가장자리 마진 두기 (`DeterministicCoord.InRegion` 보정)

#### 선행 확인 (Phase 4 시작 30분)

- [MapManager_Generator.cs](../Assets/Scripts/Map/MapManager_Generator.cs)의 `GenerateChunkData`가 `_chunkPool` 재사용 시 호출 빈도 확인 (한 청크 unload → reload 시 다시 호출되어야 결정론 유지)
- `MapFileData.bytes` 포맷 + `ReadFromBinary` 시그니처 확인 ([MapFileData.cs](../Assets/Scripts/Map/MapFileData.cs)) — 청사진 데이터 작성 도구 결정에 영향

### 13.6 Phase 5 — Live Backbone + 저장 (3~4일, MVP 1.0)

**목표**: 플레이어 행동 → 사건 append, NPC 기억 갱신, 세이브/로드.

| 신규 파일 | 책임 |
|-----------|------|
| `Assets/Scripts/Common/Message/HistoryMessages.cs` | `BossDefeatedBroadcast/VillageFoundedBroadcast/DungeonSealedBroadcast` structs |
| `Assets/Scripts/History/System/System_HistoryLiveEventCollector.cs` | Priority **260** FixedUpdate. OnCreate에서 Subscribe |
| `Assets/Scripts/History/System/System_HistoryNpcMemorySync.cs` | Priority **480** FixedUpdate, 60s interval. NPC 위치 → region → 최근 사건 3 |
| `Assets/Scripts/History/System/System_HistoryAmbientRefresh.cs` | Priority **820** LateUpdate, 1s interval |
| `Assets/Scripts/Data/HistorySaveData.cs` | §8.2 직렬화 클래스 |

| 수정 파일 | 변경 |
|-----------|------|
| [WorldData.cs](../Assets/Scripts/Data/WorldData.cs) | `HistorySaveData` 필드 추가 |
| [DataManager.cs](../Assets/Scripts/Manager/DataManager.cs) | `CURRENT_WORLD_DATA_VERSION` 3→4, `MigrateWorldData` 분기, `LoadCompleted`에서 `AR.s.History.GenerateOrLoad(worldSeed)` 호출 |
| [SystemManager.cs](../Assets/Scripts/Manager/SystemManager.cs) | 3개 시스템 등록 (priority 주석 포함) |
| [System_HpCheck.cs](../Assets/Scripts/Common/System/System_HpCheck.cs) | 보스 사망 시 `Broadcast<BossDefeatedBroadcast>` |
| Village 등록 위치 | 새 마을 등록 시 `Broadcast<VillageFoundedBroadcast>` |

**검증**: 보스 처치 → 세이브 → 재시작 → NPC가 사건 언급(stub). v3 세이브 마이그레이션 정상.

**리스크**:
- `BossTag` 같은 보스 식별 컴포넌트 존재 여부 grep 필요 (Phase 5 시작 시).
- 세이브 마이그레이션은 [DataManager.cs](../Assets/Scripts/Manager/DataManager.cs)의 backup 로직 활용.

### 13.7 Phase별 선행 확인 사항

각 Phase 시작 직전에 30분 이내로 확인:

| Phase | 확인 항목 | 방법 |
|-------|-----------|------|
| 1 | `.bytes` 직렬화 포맷 | `LoadTable<T>` 구현 1줄 보기 |
| 4 | `MapManager.ChunkSize` 노출 여부 | grep |
| 4 | `GenerateChunkData` 호출 빈도 (재로드 시 hook 중복?) | 호출처 grep |
| 5 | 보스 식별 컴포넌트 (`BossTag` 등) 존재 | grep |
| 5 | VillageManager에 마을 등록 hook 존재 | `RegisterVillage` grep |

### 13.8 작업 분량 요약 (코드 + 콘텐츠)

| Phase | 코드 일수 | 콘텐츠 일수 | 신규 .cs | 신규 .bytes | 수정 파일 |
|-------|----------|------------|---------|------------|----------|
| 0 | 2~3 | 0 | 11 | 0 | 2 |
| 1 | 3~4 | 2 (시드 데이터: 종족 3+신 1+괴물 1, 템플릿 1) | 7 | 2 | 1 |
| 2 | 4~5 | 3 (시대 1개분 템플릿 ~10개) | 8 | 0 | 1 |
| 3 | 4~5 | 2 (특수 Place 행, Region 바이옴 분포 튜닝) | 6 | 0 | 3 |
| 4 | 3~4 | 1 (폐허/거점 placeholder 스프라이트) | 3 | 0 | 2 |
| 5 | 3~4 | 0 | 5 | 0 | 5 |
| 추가 콘텐츠 | - | 8~12 (나머지 시대 30~50 템플릿 + 시드별 플레이테스트 튜닝) | 0 | ~60행 누적 | 0 |
| **합계** | **19~25** | **16~20** | **40** | **2 + 약 60행** | **14** |

→ 운영 가능한 시스템까지 **약 35~45 작업일**. 누적 콘텐츠 약 **60~90행** (종족 5~8 + 신 3~5 + 괴물 8~12 + 특수 Place 0~5 + 시대당 템플릿 10~15 × 4).

**콘텐츠 작성 시간 가정**:
- 종족/괴물 1행: 30분~1시간 (태그 분류 + 선호 지형 + 적대 태그 + 균형)
- 템플릿 1개: 1~2시간 (슬롯 정의 JSON + 텍스트 + 효과 JSON + 후속 ID + 시드 1~2개 테스트)

**선행 확인 (Phase 0 시작 전 30분)**: 기존 `.bytes` 변환 파이프라인 조사 — 구글 시트가 있는지, 변환 스크립트가 어디 있는지. 기존 시트에 탭만 추가하면 되는 경우와 새 변환 툴 필요한 경우 작업량 차이가 큼.

### 13.9 의존성 / 병렬 가능성

```
Phase 0 ──→ Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4
                                                  └──→ Phase 5
```

- **Phase 5는 Phase 4 완료 전에 시작 가능** (메시지 구독은 청크 hook과 독립).
- **Phase 3과 Phase 5 병렬 작업 가능** (지리 통합 vs 메시지 통합).

### 13.10 현재 진행 상황 (2026-05-19)

#### Phase 0 — 인프라 셋업 ✅ 코드 완료

신규 파일 11개 작성, 수정 파일 2개:

- 컴포넌트 6종: `HistoryActorComponent`, `HistoryActorTagsComponent`, `HistoricalEventComponent` (+`ActorSlotPair`), `RegionComponent`, `SettlementComponent` (+`SettlementType`/`State`), `HistoryMemoryComponent` — 모두 `Assets/Scripts/Common/Component/History/`
- 유틸 4개: `HistoryEra.cs`, `HistoryRng.cs` (SplitMix64), `DerivedSeed.cs` (6채널), `HistoryTagRegistry.cs` (SWAR popcount) — `Assets/Scripts/History/`
- 매니저 1개: `HistoryManager.cs` (Initialize/Reset/GenerateOrLoad/OnChunkActivated 골격)
- 수정: [ComponentManager.cs](../Assets/Scripts/Manager/ComponentManager.cs) `_poolSizes` 6종 등록, [AR.cs](../Assets/Scripts/Manager/AR.cs) 필드/프로퍼티/Initialize/Reset 4지점

**남은 작업 (사용자 직접)**:
- Unity 에디터에서 AR 프리팹에 `HistoryManager` 컴포넌트 추가 + `_historyManager` 필드 드래그 연결
- Play 진입 시 콘솔 로그 확인 (`HistoryManager Initialized` + 풀 6개 생성)

#### Phase 1 — 카탈로그 + 단일 사건 🟡 데이터 인프라 완료 / 본 코드 작업 보류

**완료**:
- 구글 시트 `HistoryEntity` 탭 시드 5행 (드워프/엘프/인간/화염신/드라카르)
- 구글 시트 `HistoryTemplate` 탭 시드 1행 (`tmpl_monster_invasion`)
- POCO 2종 `HistoryEntityTable`, `HistoryTemplateTable` — [Tables.cs](../Assets/Scripts/Common/Tables.cs) 끝부분
- [DownloadTables.cs](../Assets/Scripts/Editor/DownloadTables.cs) 통합: Register × 2, MenuItem × 2, CreateTable 분기 × 2, Parse 메서드 × 2, `ParseBoolSafe` 헬퍼 추가
- [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs) 통합: 필드 × 2, LoadTable 호출 × 2, `GetHistoryEntity`/`GetAllHistoryEntities`/`GetHistoryTemplate`/`GetAllHistoryTemplates`
- LLM 인프라 검증 완료 ([LLMManager.cs](../Assets/Scripts/Manager/LLMManager.cs), [LLMClient.cs](../Assets/Scripts/Manager/LLMClient.cs))

**남은 작업 (사용자 직접)**:
- Unity 메뉴 `ARPG → Download Table → HistoryEntityTable` / `HistoryTemplateTable` 실행 → `.bytes` 2개 생성

**보류 — 결정 필요 사항** (다음 세션 진입 시 결정 후 진행):

1. **SlotsJson 정규화 방향**: 현재 한 셀에 raw JSON 통째 — 디자이너 가독성 낮음
   - 옵션 A: `HistorySlot` 시트 분리 (외래키 `TemplateId`, 1슬롯 = 1행) — **추천**
   - 옵션 B: `HistoryTemplate`에 슬롯 4개 고정 컬럼 펼치기 (24컬럼)
   - 옵션 C: 미니 CSV 포맷 (`침략자:Monster:거대:고대,화염::;...`)

2. **EffectsJson도 함께 정규화?**: 같은 가독성 문제. `HistoryEffect` 시트 분리하면 일관성 ↑

3. **TextTemplate 컬럼의 역할 (LLM 도입 후)**:
   - 1A: 그대로 유지 (의미 시드 + 폴백) — 변경 0
   - 1B: 제거하고 `EventTags` 영문 키워드로 교체
   - 1C: 유지 + `ToneHint` (예: tragic/ominous) + `LoreFormat` (예: epitaph/prophecy/chronicle) 추가 — **추천**

4. **유적 글귀 저장 단위**:
   - 사건 단위 (`Event.CachedShortText`): 1사건 = 1글귀 (같은 사건의 여러 유적 = 동일 텍스트)
   - 유적 단위 (`LoreItem.InscribedText`): 1유적 = 1글귀 (같은 사건이라도 유적마다 다른 표현) — **추천**

5. **LLM 호출 정책**:
   - Lazy (유적 진입 / NPC 대화 시점) — **추천 (게임 시작 빠름)**
   - Eager (백본 생성 직후 일괄 — 비추, 시작 지연 + 비용)

#### 다음 세션 진입 절차

1. 위 5개 결정 항목 확정
2. 결정에 따라 시트 컬럼 재구성 (`SlotsJson`/`EffectsJson` 정규화, `TextTemplate` 컬럼 조정)
3. POCO + DownloadTables + DataManager_Table 갱신
4. 기존 시드 1행을 새 형식으로 재입력
5. Phase 1 본 코드 작업 진입:
   - `HistoryEntityType` enum, `SlotDefinition`/`EffectDefinition` POCO
   - `HistoryDataLoader` (테이블 → `HistoryActorComponent` 인스턴스화)
   - `SlotMatcher`, `KoreanJosaResolver`, `HistoryTextRenderer`
   - LLM 통합 — `LoreInscriptionGenerator`, `NpcDialogueGenerator` (Phase 5 NPC 시스템과 연결)
   - `HistoryGenerator.GenerateSingleEventForTest()` + cheat 명령

---

## 14. 주의사항 / 함정

### 14.1 결정론 (가장 중요)
- **`UnityEngine.Random` 절대 금지** — 전역 상태라 호출 순서가 바뀌면 결과 변동
- **`System.Random`도 권장 안 함** — .NET Framework / .NET 6+ 구현이 다르고, Unity 버전 업데이트 시 분포 변경 가능성
- 자체 PRNG (`HistoryRng` = SplitMix64 또는 xoshiro256\*\*) 사용
- 채널별 namespace 분리: `DerivedSeed(worldSeed, channelId)` — slotPick / yearStep / canTrigger / coord 채널 분리
- **결정론적 순회**: `Dictionary` 순회 금지(.NET 버전 따라 순서 다름). `SortedDictionary` 또는 `List` 사용

### 14.2 슬롯 매칭 실패
- **폴백 풀** 마련 — 매칭 실패 시 일반화된 사건(`tmpl_minor_event`)으로 대체
- 또는 해당 템플릿 스킵 → 다음 추첨 (위 §5.3의 `failStreak` 가드 사용 — 10회 연속 실패 시 시대 종료)
- 로그에 매칭 실패 이유(필수 태그 부족? 후보 0?)를 남겨야 데이터 균형 잡기 쉬움

### 14.3 사건 평탄화
- **대사건**(시대당 1~2개) vs **소사건**(다수)을 가중치/태그(`거대`, `우주적`)로 분리
- 대사건 사용 후 같은 시대 내 동급 사건 가중치를 임시로 낮추는 식 (cooldown만으로는 부족)

### 14.4 한국어 조사
- 한글 음절: 종성(받침) 유무로 자동 판단 — `(unicode - 0xAC00) % 28 != 0`
- 외국어 명사는 `HistoryEntityTable.EndsWithJongseong` 필드로 수동 지정 (발음 기준)

### 14.5 메모리
- 모든 region을 메모리에 로드하지 않음 — 플레이어 근처(N×N 청크 반경)만 lazy load
- 사건 본문(긴 텍스트)이 필요해지면 별도 파일 분리 검토 (`Assets/Resources/HistoryTexts/{eventId}.txt`)
- `HistoricalEventComponent`는 가벼운 메타데이터만 보유 → 렌더링 결과는 `HistoricalEventSnapshot.CachedRenderedText`에 캐시

### 14.6 디버깅
- **WorldSeed 항상 기록** ([WorldData.HistorySaveData.WorldSeed](#82-직렬화-worlddata-확장))
- 사건 발생 시 슬롯 매칭 과정 로그: `[History] tmpl_monster_invasion: 침략자=Drakaar(w=5), 피해자=Dwarves(w=3), 무대=R_3_7(w=8)`
- "왜 이 사건이 발생했는가" 추적 — `CausedByEventEntityId` 체인을 Editor 윈도우로 표시

### 14.7 .bytes 테이블 직렬화 시 주의
- `string[]`은 Newtonsoft JSON에서는 OK, MessagePack/Protobuf 사용 시 표현 확인
- 가변 길이 데이터(슬롯/효과)는 별도 컬럼에 JSON 문자열로 저장 → 런타임에 `ParsedSlots`로 디시리얼라이즈해 캐시

---

## 15. 사전 결정 사항

| 항목 | 결정 | 근거 |
|------|------|------|
| **언어/엔진** | C# / Unity 6 + 커스텀 ECS | 본 프로젝트 스택 |
| **글로벌 컨테이너** | `AR.s.History` (PrefabSingleton 패턴) | [AR.cs](../Assets/Scripts/Manager/AR.cs) 일관성 |
| **테이블 포맷** | `.bytes` 바이너리 + `ImmutableDictionary<int, Table>` | [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs) 일관성 |
| **태그 저장** | `ulong` × 2 비트마스크 (최대 128 태그) | O(1) 매칭 |
| **PRNG** | SplitMix64 자체 구현 | 결정론 (.NET 버전 무관) |
| **Region 크기** | 8×8 청크 (chunkSize 의존) | Map 청크 8×8 = 256 블록 안팎 |
| **시대 길이/사건 수** | Genesis 200/5, Ancient 500/15, Dark 300/12, Current 100/8 | MVP 0.3 후 튜닝 |
| **거점 밀도** | region당 거점 ≤ 3 | 폐허 포함 |
| **유적 비주얼** | Phase 1: 공용 1종 / Phase 2: 종족별 차별화 | 스프라이트 비용 |
| **표현 채널** | NPC 대사 > 책 > 비석 | 림월드 NPC 시스템 결합 |
| **재료 정의 권한** | 시트 기반 .bytes (구글 시트 워크플로 일관성) | 능력치는 properties dict 확장 가능 |
| **이름 생성** | Phase 1: 종족별 prefix/suffix 풀 / Phase 2: 절차적 음절 조합 | MVP 1.0 이후 |
| **System Priority** | 260 / 480 / 820 (LateUpdate) | 검증된 빈 슬롯 |
| **표현 시스템** | LLM 동적 생성 + KoreanJosaResolver 폴백 | NPC 대사 매번 다양화, 유적 글귀 다양화 (Phase 1 결정) |
| **LLM 인프라** | `AR.s.LLM` (Groq Llama 3.3 70B, UniTask 비동기) | [LLMClient.cs](../Assets/Scripts/Manager/LLMClient.cs) 검증 완료 |
| **유적 글귀 캐싱** | 첫 노출 시 LLM 1회 호출 → `WorldData`에 영구 저장 | 결정론 보장 + 비용/지연 분산 (lazy) |
| **NPC 대사 캐싱** | 매번 LLM 호출, 캐시 없음 | 휘발성 OK — 같은 의미 다른 표현 (사용자 의도) |
| **`KoreanJosaResolver`** | 폴백용 유지 (LLM 실패/오프라인 시 단순 토큰 치환) | API 키 없는 사용자/네트워크 오류 대비 |

---

## 16. 영감/참고

- **Dwarf Fortress** — 풀 시뮬레이션 원조
- **Caves of Qud** — 부분 시뮬레이션 + 백본 결합 (본 시스템의 모델)
- **RimWorld** — 캐릭터 단위 서사 (NPC 기억 시스템 참고)
- **Ultima Ratio Regum** — 문화/종교까지 절차적
- **Skyrim** — 손으로 쓴 백본 + 절차적 디테일

---

**문서 상태**: 설계 v3 (Phase 0 코드 완료 / Phase 1 데이터 인프라 완료 / 본 코드 작업 보류)
**작성일**: 2026-05-18
**최근 갱신**: 2026-06-26 — §0 런타임 LOD 시뮬레이션(살아있는 정착지) 도입: New Game 결정론 생성 → 런타임 지속 시뮬(근처 상세/멀리 추상), 3층 저장 구조. v1은 멀리-추상 인구 동역학만(정치 사건 지속은 추후). 이전: 2026-05-19 LLM 표현 시스템 결정
**선행 의존**: Map 청크 시스템 (✅) · ECS 기반 (✅) · MessageManager Broadcast (✅) · WorldData 직렬화 (✅) · LLM 인프라 (✅ Groq Llama 3.3 70B) · NPC 대사 시스템 (Milestone 4 이후)
**비검증 항목**: NPC 대사 시스템은 현재 미구현(README 35%). 본 시스템의 NPC 기억 부분(`System_HistoryNpcMemorySync`)은 그 시스템이 갖춰진 뒤 의미가 살아남.
