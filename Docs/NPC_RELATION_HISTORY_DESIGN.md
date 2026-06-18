# NPC 관계 + 정착지 히스토리 설계 (초안)

> **상태 (2026-06-12): 초안 — 미구현**
> 본 문서는 추후 [MAP_DESIGN.md](MAP_DESIGN.md)에 합류될 예정 (합류 지점은 §10 참고).
>
> **목표**: 절차 생성된 마을의 NPC들이 *서로 관계*(가족/친구/원한/파벌)를 가지고, 마을이 *합병되거나 왕국이 되는 히스토리*를 갖게 한다.
>
> **선행/관련 문서**:
> - [MAP_DESIGN.md](MAP_DESIGN.md) — Region/biome/settlement 배치 (§7), Lore Fragment (§6)
> - [PROCEDURAL_HISTORY_DESIGN.md](PROCEDURAL_HISTORY_DESIGN.md) — 4시대 템플릿 백본 (보류 중). 본 문서는 그 중 "현대(Current) 시대"의 정착지 층만 떼어 구체화한 것
> - [VILLAGE_PHASES.md](VILLAGE_PHASES.md) — 마을 도메인 런타임 시스템 (Priority 50-69, §4.1)
> - [INN_HIRING_DESIGN.md](INN_HIRING_DESIGN.md) — InnVisitor → Resident 흐름

---

## 1. 핵심 결정 — "매크로는 시뮬, 개인 관계는 생성"

"관계를 가상으로 생성할 것인가, 시뮬을 돌려 만들 것인가"는 양자택일이 아니라 **어느 granularity에서 시뮬을 멈추느냐**의 문제다.

| 방식 | 마을 합병/왕국 히스토리 | NPC 개인 관계 | 비용 | 제어 | 결정론 |
|------|----------------------|--------------|------|------|--------|
| **순수 생성** (랜덤 backfill) | ❌ 상태 추적이 없어 인과 모순 발생 ("합병된 마을이 그 전에 멸망") | ✅ 충분 | 최소 | 높음 | 쉬움 |
| **풀 시뮬** (NPC 단위, DF 방식) | ✅ 가능하나 과잉 | ⚠️ 가능하나 결과 90%는 플레이어가 영원히 못 봄 | 최대 | 낮음 (emergent 디버깅 지옥) | 어려움 (순서/부동소수점 의존) |
| **채택: 계층 분리** | ✅ 정착지 단위 coarse 시뮬 | ✅ 연대기를 제약으로 한 생성 | 소 | 높음 | 쉬움 (정수 연산 + 시드 채널) |

### 채택안 — 3계층

| 계층 | 시점 | 방식 | 산출물 |
|------|------|------|--------|
| **L1. 정착지 연대기 시뮬** (§4) | New Game 1회 | 정착지(수십~수백 개) 단위, 1년 틱 coarse 시뮬 | Chronicle(연대기) + 정치 스냅샷 (왕국/합병/폐허) |
| **L2. NPC 관계 backfill** (§5) | 마을 첫 population 시 (lazy) | 연대기를 제약조건으로 가족/친구/파벌/원한 생성 | `NpcRelationEntry` + 초기 호감도 시드 |
| **L3. 런타임 연속** (§6) | 게임 중 상시 | 기존 Phase C 시스템이 관계를 이어서 생성/변동 | 호감도 drift, 출생/사망 관계 갱신, Live 연대기 append |

핵심 원칙 (기존 문서들과 동일):
1. **시뮬은 정착지 granularity에서 멈춘다** — NPC 개인은 시뮬 대상이 아니다.
2. **연대기는 저장한다 (재생성하지 않는다)** — 수백 건의 작은 이벤트 리스트라 저장 비용이 무시 가능하고, 생성 규칙이 버전업돼도 구 세이브가 깨지지 않는다. 결정론은 "같은 시드로 New Game 하면 같은 역사"까지만 보장하면 된다.
3. **관계의 '왜'는 연대기 사건 참조 1개로 표현** — `OriginEventId` 링크가 개인 관계를 매크로 히스토리의 증언으로 만든다 ("내 아버지는 그 전쟁에서 죽었지").
4. **append-only** — 연대기 기존 사건은 수정하지 않고 추가만 (PROCEDURAL_HISTORY_DESIGN §2.2와 동일).

---

## 2. 기존 자산 정리 (사실)

| 자산 | 위치 | 상태 | 본 설계에서의 역할 |
|------|------|------|------------------|
| `RaceSettlementPlacer.ComputeAll()` | [RaceSettlementPlacer.cs](../Assets/Scripts/Map/Race/RaceSettlementPlacer.cs) | 골격 ✅ (청사진 콘텐츠 대기) | **L1 시뮬의 입력** — 정착지 시드 목록 (좌표/종족/biome) |
| `DerivedSeed` / `DeterministicCoord` / `RegionCoord` | Scripts/History/Generator, Scripts/Map/Region | ✅ 구현됨 | 시드 채널 분리, 결정론 좌표 |
| `VillageData` / `VillageManager` / Phase C 시스템 | [VillageData.cs](../Assets/Scripts/Village/VillageData.cs) 등 | ✅ 런타임 동작 중 | **L3의 본체** — 인구/Tier/건설은 이미 라이브 시뮬 |
| `NpcSaveData` (`NpcStat` 8성향, `VillageId`, `JobType`) | [NpcSaveData.cs](../Assets/Scripts/Npc/NpcSaveData.cs) | ✅ | backfill의 입력 (성격 기반 관계 가중치) |
| `NpcPairKey` + `_npcAffinity` + `WorldData.NpcAffinityList` | [NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs), [WorldData.cs](../Assets/Scripts/Data/WorldData.cs#L18) | ✅ 구현됨 | **호감도 수치 채널** — backfill이 초기값 시드, L3가 변동 |
| `RelationshipComponent` + `System_Relationship` | [RelationshipComponent.cs](../Assets/Scripts/Common/Component/RelationshipComponent.cs) | 빈 골격 (TODO만 있음, 호출처 0) | **폐기 권장** (§5.4 결정) |
| `HistoryEntityTable`/`HistoryTemplateTable` POCO | [Tables.cs](../Assets/Scripts/Common/Tables.cs) | 선언만, .bytes 없음 | 본 설계 범위 밖 (4시대 백본은 계속 보류) |

---

## 3. 용어 / 데이터 구분

| 용어 | 의미 |
|------|------|
| **Settlement** | `RaceSettlementPlacer`가 배치한 절차 정착지. L1 시뮬의 에이전트 단위 |
| **Chronicle** | L1 시뮬 + 게임 중 Live append로 누적되는 사건 목록. `WorldData`에 저장 |
| **Kingdom** | L1 시뮬 결과로 정착지들이 묶인 정치 단위. 수도 정착지 1개 보유 |
| **Relation** | NPC 쌍의 *유형 있는* 관계 (가족/친구/원한…). 호감도(`_npcAffinity`, 수치)와 별개 채널 |

| 데이터 | 위치 | 형태 |
|--------|------|------|
| 종족 간 기본 성향 (카탈로그) | `RaceRelationTable.bytes` (신규) | POCO → `ImmutableDictionary` |
| Chronicle / Kingdom (월드 인스턴스) | `WorldData.ChronicleSaveData` (신규) | Newtonsoft JSON, append-only |
| NPC Relation (월드 인스턴스) | `WorldData.NpcRelationList` (신규) | `NpcAffinityList`와 동일 패턴 |
| 호감도 수치 | `WorldData.NpcAffinityList` (기존) | 변경 없음 |

---

## 4. L1 — 정착지 연대기 시뮬 (New Game 1회)

### 4.1 입력

```
RaceSettlementPlacer.ComputeAll(mapSeed, ...)  →  List<SettlementPlacement>
```

- 맵 경계 [-98, 98] 청크 = 49×49 region → 정착지 후보 최대 ~2,400개 (종족 비호환 region 제외 시 실제는 그 이하)
- 시뮬 에이전트 상태는 placement당 아래 구조체 1개:

```csharp
// 시뮬 내부 전용 (저장 안 함 — 최종 스냅샷만 저장)
public struct SettlementSimState
{
    public int SettlementId;            // placement 인덱스 기반 결정론 ID
    public GlobalEnum.RaceType Race;
    public Vector2Int RegionCoord;
    public int Population;              // 종족/biome 기반 초기값
    public int KingdomId;               // -1 = 무소속
    public SettlementFate Fate;         // Alive / Ruined / Merged
    public int MergedIntoId;            // Fate == Merged일 때
    public int FoundedYear;
}

public enum SettlementFate { Alive = 0, Ruined = 1, Merged = 2 }
```

- 정착지 간 우호도: `Dictionary<(int, int), int>` — **이웃만** (region 거리 ≤ 3, 정규화 페어). 전쌍 O(N²)을 만들지 않는다.

### 4.2 틱 루프

- **틱 = 1년, 기본 150년** (튜닝 항목). 모든 난수는 `DerivedSeed.Derive(mapSeed, ChannelChronicle)` 계열 채널에서 SplitMix64 — `UnityEngine.Random` / `System.Random` 사용 금지 (MAP_DESIGN §2.4의 상태 의존 PRNG 함정 회피).
- 틱당 처리:

```
1. 인구 성장:    pop += growthRate(race, biome) ± noise
2. 이웃 페어 이벤트 추첨 (페어당 연 1회 roll):
     가중치 = f(종족 호환(RaceRelationTable), 거리, 인구비, 현재 우호도)
     → Trade / Skirmish / War / Alliance / Merge 중 0~1개
3. 사건 효과 적용:  우호도/인구 가감, Fate 변경
4. 왕국 검사:     동맹 클러스터 크기 ≥ K → KingdomFound (최대 인구 정착지 = 수도)
                  기존 왕국 인접 + 우호 ≥ T → KingdomJoin
5. 사건을 Chronicle에 append
```

### 4.3 사건 카탈로그 (v1)

| ChronicleEventType | 조건 (요약) | 효과 | NPC backfill에 주는 제약 (§5.3) |
|---|---|---|---|
| `Trade` | 우호 ≥ 0, 거리 가까움 | 우호 +, 양쪽 pop + | 상인 NPC의 우호 대사 |
| `Skirmish` | 우호 < 0 | 우호 −, pop 소폭 − | 경계 대사 |
| `War` | 우호 ≤ −T, 종족 적대 | 우호 −−, 패자 pop −−, 폐허 확률 | **원한 관계 + 유족 서사** |
| `Alliance` | 우호 ≥ +T | 동맹 마크 (왕국 전 단계) | 혼인 가계(두 마을에 걸친 가족) |
| `Merge` | 동맹 + 인구비 ≥ 3:1, 또는 War 패배 | 작은 쪽 `Fate=Merged`, 인구 흡수 | **두 founder 혈통 파벌** |
| `Ruin` | pop ≤ 0 | `Fate=Ruined` | (NPC 없음 — 맵 표현만, §4.5) |
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

### 4.4 출력 / 저장

```csharp
// WorldData에 추가
public ChronicleSaveData Chronicle = new();

[Serializable]
public class ChronicleSaveData
{
    public int Version = 1;
    public int SimulatedYears;
    public List<ChronicleEvent> Events = new();              // 수백 건 수준
    public List<SettlementSnapshot> Settlements = new();     // 최종 상태만
    public List<KingdomData> Kingdoms = new();
}

[Serializable]
public class KingdomData
{
    public int KingdomId;
    public string Name;                  // 종족 어근 기반 절차 작명 (v1은 "{종족} 왕국 N" 수준)
    public int CapitalSettlementId;
    public List<int> MemberSettlementIds = new();
}
```

- **저장이 정본**: 한 번 생성된 연대기는 시드로 재생성하지 않는다 (원칙 §1-2).
- VillageManager가 정착지를 실제 `VillageData`로 등록할 때 `SettlementId ↔ VillageId` 매핑을 보관 → 연대기 조회 경로.

### 4.5 맵 표현과의 연결 (MAP_DESIGN 합류 포인트)

- `Fate == Ruined / Merged` 정착지 → 마을 청사진 대신 **폐허 variant** 배치. Lore Fragment(§6)의 L3 청사진과 같은 메커니즘을 재사용하고, `FateKeyword`를 연대기 사건에서 가져온다 (War로 폐허 → `burned`, 합병 → `forgotten` 등).
- 즉 기존 Lore Fragment의 "랜덤 fate" 일부가 "연대기가 실제로 기록한 fate"로 승격된다. 랜덤 fragment는 그대로 공존 (고대 유적용).

### 4.6 비용 추정

정착지 ≤ 2,400 × 이웃 ≤ ~8 × 150틱 ≈ 정수 연산 수백만 회 — **New Game 시 수십 ms**. PROCEDURAL_HISTORY_DESIGN §1의 "수 초 내 완료" 예산에 충분히 들어간다. 필요 시 정착지 수를 region 추첨율로 줄이는 노브가 이미 placer에 있다.

---

## 5. L2 — NPC 관계 backfill (마을 첫 population 시)

### 5.1 시점과 트리거

- `VillageData.HasBeenPopulated`가 false → true 되는 **기본 NPC 첫 스폰 직후 1회** (NpcManager 신규 생성 경로와 동일한 자리 — NPC 장비 시드(`SeedNpcEquipment`)와 같은 패턴).
- 세이브 로드 경로는 backfill하지 않는다 (관계가 이미 `WorldData`에 있음).
- 이민자/InnVisitor는 backfill 대상 아님 — **관계 없이 진입**해서 L3에서 자연 형성 (외지인이라는 사실 자체가 서사).

### 5.2 데이터 모델

기존 호감도(수치, 대칭)와 분리된 **유형 채널**을 추가한다. 저장/복원은 `NpcAffinityList`와 동일 패턴.

```csharp
public enum NpcRelationType
{
    // 방향성 (From → To)
    ParentOf = 0,        // From이 To의 부모
    MentorOf = 1,        // 직업 사제
    GrudgeAgainst = 2,   // From이 To에게 원한

    // 대칭 (NpcPairKey 정규화, Lo→Hi로 1건만 저장)
    Spouse = 100,
    Sibling = 101,
    Friend = 102,
    Rival = 103,
}

[Serializable]
public class NpcRelationEntry
{
    public int FromEntityId;
    public int ToEntityId;
    public NpcRelationType Type;
    public int OriginEventId;     // ChronicleEvent 참조. 연대기와 무관하면 -1
}
```

- 런타임 보관: `NpcManager`에 `Dictionary<int, List<NpcRelationEntry>>` (key = EntityId, 양방향 인덱스) — 호감도와 같은 매니저에 두어 조회 API를 한 곳으로 모은다 (`GetRelations(npcId)`, `GetRelation(npcA, npcB, type)`).
- **NPC ↔ 마을 감정** (상대 마을 NPC가 아직 미스폰일 수 있는 문제 해결): 개인 → 정착지 단위 태그를 `NpcSaveData`에 둔다.

```csharp
// NpcSaveData에 추가
public List<NpcSentiment> Sentiments = new();   // 보통 0~1개

[Serializable]
public class NpcSentiment
{
    public NpcSentimentType Type;    // LostFamilyToWar, ProudOfKingdom, ResentsMerge, ...
    public int TargetSettlementId;   // 감정의 대상 정착지 (-1 = 자기 마을)
    public int OriginEventId;
}
```

### 5.3 생성 알고리즘

입력: 마을 NPC 목록(`NpcSaveData` — 성향/직업), 이 마을의 연대기 추출(`Events.Where(SettlementId 관련)`), 마을 시드(`DerivedSeed.Derive(mapSeed ^ settlementId, ChannelRelationBackfill)`).

```
1. 가구(household) 분할:  인구를 1~5인 가구로 결정론 분할
2. 가족 관계:           가구 내 Spouse/ParentOf/Sibling 배정 (세대 수 ≤ 2, v1은 족보 단순)
3. 직업 관계:           같은 JobType 선임→후임 MentorOf
4. 성격 관계:           NpcStat 기반 가중 추첨
                         - Friendliness 높음 → Friend 엔트리 수 ↑
                         - Pride 높음 × 같은 직업 → Rival 확률 ↑
5. 연대기 제약 주입:
   - Merge 사건 있음   → NPC를 두 출신 파벌로 분할, 파벌 간 초기 호감도 −,
                         일부에 ResentsMerge Sentiment (OriginEventId)
   - War 사건 있음     → 인구 비례 일부 NPC에 GrudgeAgainst(상대 마을 스폰 시 연결) 또는
                         LostFamilyToWar Sentiment + TargetSettlementId
   - KingdomFound/Join → ProudOfKingdom / ResentsKingdom Sentiment를 Loyalty 성향 따라 분포
6. 호감도 시드:         RelationType → 초기 Affinity 매핑 후 _npcAffinity 등록
                         (Spouse +60, Sibling +40, Friend +30, Rival −20, Grudge −50, 파벌 교차 −10 등)
```

- 관계 밀도 목표: NPC당 가족 1~4 + 비가족 1~3 엔트리. Settlement(3~5명)~City(40명) 어디서도 수백 엔트리 이하 — 저장/메모리 무해.
- **결정론은 생성 시 1회만 필요** (생성 직후 영속화되므로). 그래도 시드 채널을 쓰는 이유는 같은 시드 New Game의 재현성과 디버깅.

### 5.4 기존 골격 처리 — `RelationshipComponent` 폐기

| 항목 | 결정 | 사유 |
|------|------|------|
| `RelationshipComponent` (4축: Affinity/Trust/Fear/Intimacy) | **폐기** | 관계는 청크 unload된 NPC 간에도 존재해야 함 → 정본은 매니저+`WorldData`. 4축 세분화는 소비처(대사/AI)가 생기기 전엔 과잉. 필요해지면 `NpcRelationEntry`에 축 추가가 더 싸다 |
| `System_Relationship` (Priority 58, TODO만 존재) | **폐기 → §6.2의 `System_NpcAffinityDrift`로 대체** | 의도했던 "패시브 관계 변동"을 정본 저장소(NpcManager) 기준으로 다시 구현 |
| `EntityIdHelper.CreateRelationshipEntity()` | 폐기 | 위와 동일 |

> ECS "상태는 컴포넌트에" 원칙의 예외지만, 호감도(`_npcAffinity`)가 이미 같은 예외 전례다 — 관계 데이터는 *오프라인 NPC 간에도 살아있어야 하는* 월드 데이터라 엔티티 수명과 분리가 맞다.

---

## 6. L3 — 런타임 연속 (기존 Phase C 시스템에 hook)

### 6.1 생명주기 hook (신규 시스템 아님 — 기존 경로에 추가)

| 이벤트 | 기존 위치 | 추가 동작 |
|--------|----------|----------|
| 출생 | Phase C 출생 시스템 | 부모와 `ParentOf` + 형제 `Sibling` 추가, 가족 호감도 시드 |
| 분가 | Phase C 분가 | 관계 유지 (가족 관계는 마을이 달라져도 보존 — 마을 간 연결고리가 됨) |
| 이민/Inn 고용 | NpcManager / INN_HIRING | 관계 없이 진입. `Sentiments`에 출신 정착지 1개 부여 가능 (v2) |
| NPC 영구 사망 | `RemoveAllAffinityInvolving` 호출부 | relation 엔트리도 함께 정리 + **유족에 Grudge/Sentiment 생성 후보** (사인이 명확할 때) |

### 6.2 호감도 자연 변동 — `System_NpcAffinityDrift` (신규)

- `IFixedUpdateSystem`, **Priority 58** (구 System_Relationship 자리 — Population 밴드 55-59 내), `UpdateInterval` 수 초.
- 같은 마을 + 둘 다 활성(스폰된) NPC 페어만 대상: 근접/같은 작업장이면 `ModifyAffinity(+1)`, `NpcStat`(Friendliness/Patience)이 변동 속도 가중. Rival/Grudge 관계는 음수 drift.
- 활성 NPC만 보므로 비용은 활성 마을 1~2개 × 페어 수십 — 무해. 비활성 NPC 관계는 동결 (오프라인 시뮬 안 함 — 원칙 §1-1).

### 6.3 라이브 연대기 (Live append)

- 게임 중 큰 사건을 `Chronicle.Events`에 append (EventId 이어서 발급):
  - 마을 전멸 (`DepletedAt` 확정 시) → `Ruin`
  - VillageStage 승격 Town/City 도달 → `Promotion` (신규 타입)
  - 플레이어 개입 사건 (보스 처치로 마을 구함 등) → PROCEDURAL_HISTORY_DESIGN §8 트리거 설계 재사용
- **라이브 마을 합병/왕국 편입은 v1 범위 밖** — 절차 마을의 Tier/외교 런타임 규칙이 더 필요. hook 자리만 명시해 두고 보류.

---

## 7. 소비처 (이 데이터를 누가 읽나)

우선순위 순. v1은 1~2만으로도 투자 가치가 보인다.

1. **NPC 대사**: 관계/Sentiment → 대사 풀 분기. `OriginEventId` → 연대기 사건을 1줄 회고로 변환 ("30년 전 {상대마을}과의 전쟁에서...")
2. **NpcInspect UI**: 관계 탭 — 가족/친구/원한 목록 + 호감도 (이미 수정 중인 [NpcInspect.uxml](../Assets/UI/NpcInspect/NpcInspect.uxml)에 섹션 추가)
3. **AI 행동 분기**: `GetAffinity` 기존 API 그대로 — backfill이 초기값을 깔아주므로 첫 만남부터 차등 행동
4. **퀘스트 시드** (v2): Grudge/LostFamily Sentiment가 자연스러운 퀘스트 발주처
5. **왕국 표현** (v2): 수도 마을 비주얼 차별화, 같은 왕국 마을 간 상인 재고/가격 연동

---

## 8. 시드 채널 / 풀 사이즈 / 테이블 추가 목록

```csharp
// DerivedSeed 채널 추가
ChannelChronicle          // L1 시뮬 PRNG
ChannelRelationBackfill   // L2 마을별 backfill PRNG

// 신규 테이블 (.bytes, 시트 작업 필요)
RaceRelationTable { Id, RaceA, RaceB, BaseAffinity, WarWeight, TradeWeight, MergeAllowed }

// WorldData 추가 필드
public ChronicleSaveData Chronicle = new();
public List<NpcRelationEntry> NpcRelationList = new();
// NpcSaveData 추가 필드
public List<NpcSentiment> Sentiments = new();
```

ECS 풀 추가 없음 — 본 설계는 의도적으로 ECS 컴포넌트를 만들지 않는다 (§5.4).

---

## 9. 구현 Phase 분해

| Phase | 내용 | 분량 | 선행 |
|-------|------|------|------|
| **R0. 데이터 모델 + 저장** | ChronicleEvent/SaveData, NpcRelationEntry, NpcSentiment, WorldData 직렬화 + 마이그레이션 | 1~2일 | 없음 |
| **R1. 연대기 시뮬** | SettlementChronicleSimulator (New Game 1회), RaceRelationTable, 디버그 텍스트 덤프 ("연대기 출력" 에디터 메뉴) | 3~4일 | R0, RaceSettlementPlacer 배치 콘텐츠와는 독립 (placement 좌표만 필요) |
| **R2. NPC backfill** | 가구/가족/성격/파벌 생성기, 호감도 시드, HasBeenPopulated hook | 3~4일 | R0 (R1 없이도 연대기-무관 관계는 동작 — 단계 분리 가능) |
| **R3. 런타임 연속** | System_NpcAffinityDrift, 출생/사망 hook, 구 RelationshipComponent/System_Relationship 제거 | 2일 | R2 |
| **R4. 표현** | NpcInspect 관계 탭, 대사 분기 최소 1종 (Sentiment 회고 1줄) | 2~3일 | R2 (R1 있으면 회고 대사 가능) |
| 보류 | 라이브 합병/왕국, 왕국 게임플레이(세금/평판), 4축 관계, LLM 연대기 문장화, 혼인 기반 마을 간 가계 | — | — |

**검증 마일스톤**: R1 완료 시 "연대기 텍스트 덤프"가 읽을 만한 역사인지 눈으로 평가 (사건 평탄화 함정 — PROCEDURAL_HISTORY_DESIGN §14.3). 재미없으면 R2 진입 전에 사건 카탈로그/가중치만 튜닝.

---

## 10. MAP_DESIGN.md 합류 지점 (예정)

| 본 문서 | MAP_DESIGN 합류 위치 |
|---------|---------------------|
| §4 연대기 시뮬 | §7 (Race Settlement) 뒤에 "§7.x 정착지 히스토리" 로 |
| §4.5 폐허 표현 | §6 Lore Fragment — FateKeyword 공급원 추가 |
| §5~6 NPC 관계 | MAP_DESIGN 범위 밖 — NPC 문서 또는 본 문서 잔류분으로 유지 |

---

## 11. 미결정 사항 (구현 전 결정 필요)

1. **시뮬 연수/사건 밀도** — 150년 기본값은 가설. R1 덤프 보고 튜닝.
2. **RaceRelationTable 외부화 범위** — 종족 쌍 성향을 시트로 뺄지, v1은 코드 상수로 시작할지.
3. **왕국 이름 작명** — v1 절차 작명("{종족} 왕국 N")으로 충분한가, 어근 조합 작명기를 만들 것인가.
4. **NpcStat 생성의 시드화** — 현재 `UnityEngine.Random`(비결정론, 저장되므로 무해). 같은 시드 New Game 재현성을 원하면 backfill 채널로 통일할지.
5. **InnVisitor 출신 정착지 부여** — 방문자에게 연대기 기반 출신지를 줄지 (v2 후보).
