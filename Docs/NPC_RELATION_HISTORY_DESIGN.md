# NPC 관계 설계

> **이 문서의 범위 (2026-06-26 재편)**: NPC **개인 관계** 층 — 가족/친구/원한(Relation), 마을·왕국에 대한 감정(Sentiment), 쌍 호감도(Affinity) 채널, 관계 backfill·런타임 변동.
> - **정착지(매크로) 통폐합·연대기 시뮬·왕국/정복-병합·런타임 LOD**는 → [PROCEDURAL_HISTORY_DESIGN.md](PROCEDURAL_HISTORY_DESIGN.md)
> - **구현 계획/진행 상황**은 → [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md)

> **상태 (2026-06-26): 초안 — 일부 구현(호감도 채널 ✅)**
> **목표**: 절차 생성된 마을의 NPC들이 *서로 관계*(가족/친구/원한/파벌)를 가지고, 정착지 연대기(합병/정복/전쟁)를 *개인의 사연*으로 갖게 한다 ("내 아버지는 그 전쟁에서 죽었지").
>
> **선행/관련 문서**:
> - [PROCEDURAL_HISTORY_DESIGN.md](PROCEDURAL_HISTORY_DESIGN.md) — 정착지 통폐합/연대기 시뮬 (본 문서 관계 생성의 *입력*: Chronicle/Kingdom/Fate)
> - [MAP_DESIGN.md](MAP_DESIGN.md) — Region/biome/settlement 배치
> - [VILLAGE_PHASES.md](VILLAGE_PHASES.md) — 마을 도메인 런타임 시스템 (Priority 50-69, §4.1)
> - [INN_HIRING_DESIGN.md](INN_HIRING_DESIGN.md) — InnVisitor → Resident 흐름

---

## 1. 핵심 결정 — "개인 관계는 생성"

3계층 모델([PROCEDURAL §1](PROCEDURAL_HISTORY_DESIGN.md))에서 본 문서가 담당하는 것은 **L2/L3 (개인 관계)** 다. 정착지 단위 매크로 히스토리(L1)는 시뮬로 만들지만, **NPC 개인 관계는 풀 시뮬하지 않고 연대기를 제약으로 한 생성(backfill)** 으로 만든다.

| 계층 | 시점 | 방식 | 산출물 | 문서 |
|------|------|------|--------|------|
| L1. 정착지 연대기 시뮬 | New Game + 런타임 지속 | 정착지 단위 coarse 시뮬 | Chronicle + 정치 스냅샷 | [PROCEDURAL §3](PROCEDURAL_HISTORY_DESIGN.md) |
| **L2. NPC 관계 backfill** (§4) | 마을 첫 population 시 (lazy) | 연대기를 제약조건으로 가족/친구/파벌/원한 생성 | `NpcRelationEntry` + 초기 호감도 시드 | **본 문서** |
| **L3. 런타임 연속** (§5) | 게임 중 상시 | 기존 Phase C 시스템이 관계를 이어서 생성/변동 | 호감도 drift, 출생/사망 관계 갱신 | **본 문서** |

핵심 원칙:
1. **NPC 개인은 시뮬 대상이 아니다** — 비활성(청크 unload) NPC 관계는 동결 (오프라인 시뮬 안 함). 단 이는 *개인 관계 granularity*에 한함 — 정착지 추상 시뮬([PROCEDURAL §0](PROCEDURAL_HISTORY_DESIGN.md))과는 별개 층.
2. **관계의 '왜'는 연대기 사건 참조 1개로 표현** — `OriginEventId` 링크가 개인 관계를 매크로 히스토리의 증언으로 만든다.
3. **관계 정본은 엔티티가 아니라 매니저 + `WorldData`** — 관계/호감도는 청크 unload된 NPC 간에도 살아있어야 하므로 엔티티 수명과 분리한다 (§4.4).

---

## 2. 용어 / 기존 자산 (관계 측)

| 용어 | 의미 |
|------|------|
| **Relation** | NPC 쌍의 *유형 있는* 관계 (가족/친구/원한…). 호감도(`_npcAffinity`, 수치)와 별개 채널 |
| **Affinity (호감도)** | NPC 쌍의 *수치* 호감도 (−/+). 대칭, sparse |
| **Sentiment** | NPC → 정착지 단위 감정 (피정복 반감, 왕국 자부심 등). 상대 마을 NPC가 미스폰이어도 표현 가능 |

| 자산 | 위치 | 상태 | 역할 |
|------|------|------|------|
| `NpcSaveData` (`NpcStat` 8성향, `VillageId`, `JobType`) | [NpcSaveData.cs](../Assets/Scripts/Npc/NpcSaveData.cs) | ✅ | backfill의 입력 (성격 기반 관계 가중치) |
| `NpcPairKey` + `_npcAffinity` + `WorldData.NpcAffinityList` | [NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs#L626), [WorldData.cs](../Assets/Scripts/Data/WorldData.cs#L18) | ✅ 구현됨 (§3) | **호감도 수치 채널** — backfill이 초기값 시드, L3가 변동 |
| `RelationshipComponent` + `System_Relationship` | [RelationshipComponent.cs](../Assets/Scripts/Common/Component/RelationshipComponent.cs) | 빈 골격 (TODO만 있음, 호출처 0) | **폐기 권장** (§4.4 결정) |

| 데이터 | 위치 | 형태 |
|--------|------|------|
| NPC Relation (월드 인스턴스) | `WorldData.NpcRelationList` (신규) | `NpcAffinityList`와 동일 패턴 |
| 호감도 수치 | `WorldData.NpcAffinityList` (기존) | 변경 없음 |
| NPC → 정착지 감정 | `NpcSaveData.Sentiments` (신규) | NPC별 0~1개 |

---

## 3. 호감도 채널 (수치, 대칭) — 이미 구현됨

backfill·drift가 의존하는 기반 채널. 정본 저장소는 `NpcManager` + `WorldData`(엔티티 수명과 분리).

- 런타임: `NpcManager._npcAffinity : Dictionary<NpcPairKey, int>` ([NpcManager.cs:23](../Assets/Scripts/Manager/NpcManager.cs#L23)). `NpcPairKey`는 EntityId 쌍을 `Lo<Hi`로 정규화(대칭).
- 영속: `WorldData.NpcAffinityList : List<NpcAffinityEntry>` ([WorldData.cs:18](../Assets/Scripts/Data/WorldData.cs#L18)) — 평면 리스트. `SaveAffinity`/`LoadAffinity`로 Dictionary ↔ List 변환.
- API ([NpcManager.cs:626~](../Assets/Scripts/Manager/NpcManager.cs#L626)): `GetAffinity`(없으면 0), `HasMet`, `SetAffinity`, `ModifyAffinity`(delta), `RemoveAllAffinityInvolving`(영구 사망 정리).

> ⚠ 현황: API·저장은 있으나 **호출처가 아직 없다** — backfill 시드(§4)도 drift(§5)도 미구현이라 실질 호감도는 전부 0. clamp 범위(예 −100~+100)도 미적용. ([PLAN R0-A](WORLD_HISTORY_PLAN.md))

---

## 4. L2 — NPC 관계 backfill (마을 첫 population 시)

### 4.1 시점과 트리거

- `VillageData.HasBeenPopulated`가 false → true 되는 **기본 NPC 첫 스폰 직후 1회** ([NpcManager.EnsureVillagePopulated](../Assets/Scripts/Manager/NpcManager.cs#L295) — NPC 장비 시드(`SeedNpcEquipment`)와 같은 패턴 자리). 마을 단위 일괄 스폰이 끝나 전체 roster가 확보된 직후.
- 세이브 로드 경로는 backfill하지 않는다 (관계가 이미 `WorldData`에 있음).
- 재스폰(전멸 후)·이민자·InnVisitor는 backfill 대상 아님 — **관계 없이 진입**해서 L3에서 자연 형성 (외지인이라는 사실 자체가 서사).

### 4.2 데이터 모델

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

### 4.3 생성 알고리즘

입력: 마을 NPC 목록(`NpcSaveData` — 성향/직업), 이 마을의 연대기 추출(`Chronicle.Events.Where(SettlementId 관련)` — [PROCEDURAL §3](PROCEDURAL_HISTORY_DESIGN.md)), 마을 시드(`DerivedSeed.Derive(mapSeed ^ settlementId, ChannelRelationBackfill)`).

```
1. 가구(household) 분할:  인구를 1~5인 가구로 결정론 분할
2. 가족 관계:           가구 내 Spouse/ParentOf/Sibling 배정 (세대 수 ≤ 2, v1은 족보 단순)
3. 직업 관계:           같은 JobType 선임→후임 MentorOf
4. 성격 관계:           NpcStat 기반 가중 추첨
                         - Friendliness 높음 → Friend 엔트리 수 ↑
                         - Pride 높음 × 같은 직업 → Rival 확률 ↑
5. 연대기 제약 주입 (연대기 사건 종류는 PROCEDURAL §3.3):
   - Merge 사건 있음   → NPC를 두 출신 파벌로 분할, 파벌 간 초기 호감도 −,
                         일부에 ResentsMerge Sentiment (OriginEventId)
   - War 사건 있음     → 인구 비례 일부 NPC에 GrudgeAgainst(상대 마을 스폰 시 연결) 또는
                         LostFamilyToWar Sentiment + TargetSettlementId
   - Conquer 사건 있음 → 이 마을이 피정복지면: 다수 NPC에 ResentsKingdom Sentiment
                         (Loyalty 낮을수록 강함), 지배종족(KingdomData.FoundingRace) 거주민과
                         초기 호감도 −, 전쟁 유족엔 LostFamilyToWar.
                         지배종족 NPC는 ProudOfKingdom 경향 → 한 마을 안에 지배/피지배 파벌
   - KingdomFound/Join → ProudOfKingdom / ResentsKingdom Sentiment를 Loyalty 성향 따라 분포
6. 호감도 시드:         RelationType → 초기 Affinity 매핑 후 _npcAffinity 등록 (§3의 SetAffinity)
                         (Spouse +60, Sibling +40, Friend +30, Rival −20, Grudge −50, 파벌 교차 −10 등)
```

- 관계 밀도 목표: NPC당 가족 1~4 + 비가족 1~3 엔트리. Settlement(3~5명)~City(40명) 어디서도 수백 엔트리 이하 — 저장/메모리 무해.
- **결정론은 생성 시 1회만 필요** (생성 직후 영속화되므로). 그래도 시드 채널을 쓰는 이유는 같은 시드 New Game의 재현성과 디버깅. (단 `NpcStat` 자체가 비결정론 — §8)
- 참조 구현: [Tools/ChronicleSimPrototype/NpcBackfill.cs](../Tools/ChronicleSimPrototype/NpcBackfill.cs) (오프라인 검증본). 런타임 포팅 시 프로토타입의 "NPC 인원 생성" 부분은 빼고, 이미 스폰된 roster에 관계/sentiment/호감도를 입히는 부분만 가져온다.

### 4.4 기존 골격 처리 — `RelationshipComponent` 폐기

| 항목 | 결정 | 사유 |
|------|------|------|
| `RelationshipComponent` (4축: Affinity/Trust/Fear/Intimacy) | **폐기** | 관계는 청크 unload된 NPC 간에도 존재해야 함 → 정본은 매니저+`WorldData`. 4축 세분화는 소비처(대사/AI)가 생기기 전엔 과잉. 필요해지면 `NpcRelationEntry`에 축 추가가 더 싸다 |
| `System_Relationship` (Priority 58.5, TODO만 존재) | **폐기 → §5.2의 `System_NpcRelationship`로 대체** | 의도했던 "패시브 관계 변동"을 정본 저장소(NpcManager) 기준으로 다시 구현 |
| `EntityIdHelper` 관계 인프라 | 다른 곳에서 안 쓰이면 정리 | 위와 동일 |

> ECS "상태는 컴포넌트에" 원칙의 예외지만, 호감도(`_npcAffinity`)가 이미 같은 예외 전례다 — 관계 데이터는 *오프라인 NPC 간에도 살아있어야 하는* 월드 데이터라 엔티티 수명과 분리가 맞다.

---

## 5. L3 — 런타임 연속 (기존 Phase C 시스템에 hook)

### 5.1 생명주기 hook (신규 시스템 아님 — 기존 경로에 추가)

| 이벤트 | 기존 위치 | 추가 동작 |
|--------|----------|----------|
| 출생 | Phase C 출생 시스템 | 부모와 `ParentOf` + 형제 `Sibling` 추가, 가족 호감도 시드 |
| 분가 | Phase C 분가 | 관계 유지 (가족 관계는 마을이 달라져도 보존 — 마을 간 연결고리가 됨) |
| 이민/Inn 고용 | NpcManager / INN_HIRING | 관계 없이 진입. `Sentiments`에 출신 정착지 1개 부여 가능 (v2) |
| NPC 영구 사망 | `RemoveAllAffinityInvolving` 호출부 | relation 엔트리도 함께 정리 + **유족에 Grudge/Sentiment 생성 후보** (사인이 명확할 때) |

### 5.2 호감도 자연 변동 — `System_NpcRelationship` (신규)

- `IFixedUpdateSystem`, **Priority 58.5** (구 System_Relationship 자리 — Population 밴드 55-59 내), `UpdateInterval` 수 초.
- 같은 마을 + 둘 다 활성(스폰된) NPC 페어만 대상: 근접/같은 작업장이면 `ModifyAffinity(+1)`, `NpcStat`(Friendliness/Patience)이 변동 속도 가중. Rival/Grudge 관계는 음수 drift.
- 활성 NPC만 보므로 비용은 활성 마을 1~2개 × 페어 수십 — 무해. 비활성 NPC 관계는 동결.

> 정착지 단위 Live 연대기 append(마을 전멸→`Ruin`, 승격→`Promotion`, 플레이어 개입)는 → [PROCEDURAL §5](PROCEDURAL_HISTORY_DESIGN.md).

---

## 6. 소비처 (이 데이터를 누가 읽나)

우선순위 순. v1은 1~2만으로도 투자 가치가 보인다.

1. **NPC 대사**: 관계/Sentiment → 대사 풀 분기. `OriginEventId` → 연대기 사건을 1줄 회고로 변환 ("30년 전 {상대마을}과의 전쟁에서...")
2. **NpcInspect UI**: 관계 탭 — 가족/친구/원한 목록 + 호감도 ([NpcInspect.uxml](../Assets/UI/NpcInspect/NpcInspect.uxml)에 섹션 추가)
3. **AI 행동 분기**: `GetAffinity` 기존 API 그대로 — backfill이 초기값을 깔아주므로 첫 만남부터 차등 행동
4. **퀘스트 시드** (v2): Grudge/LostFamily Sentiment가 자연스러운 퀘스트 발주처
5. **왕국 표현** (v2): 수도 마을 비주얼 차별화, 같은 왕국 마을 간 상인 재고/가격 연동

---

## 7. 시드 채널 / 저장 추가 (관계 측)

```csharp
// DerivedSeed 채널 추가
ChannelRelationBackfill   // L2 마을별 backfill PRNG  (L1 시뮬용 ChannelChronicle은 PROCEDURAL)

// WorldData 추가 필드
public List<NpcRelationEntry> NpcRelationList = new();
// NpcSaveData 추가 필드
public List<NpcSentiment> Sentiments = new();
```

ECS 풀 추가 없음 — 의도적으로 ECS 컴포넌트를 만들지 않는다 (§4.4).

---

## 8. 미결정 사항 (구현 전 결정 필요)

1. **NpcStat 생성의 시드화** — 현재 [NpcSaveData 생성자](../Assets/Scripts/Npc/NpcSaveData.cs#L87)에서 `UnityEngine.Random`(비결정론, 저장되므로 무해). backfill이 성격을 입력으로 쓰는데 성향 생성이 비결정론이라, 같은 시드 New Game 재현성을 원하면 backfill 채널로 통일 필요. 직업/종족 무반영(순수 균등 랜덤)인 점도 함께 검토.
2. **호감도 clamp 범위** — `ModifyAffinity`에 범위 제한 미적용. 상수 정의 필요 (§3).
3. **InnVisitor 출신 정착지 부여** — 방문자에게 연대기 기반 출신지를 줄지 (v2 후보).
4. **관계 backfill의 연대기 의존** — 연대기(PROCEDURAL L1)가 아직 런타임에 없으면, §4.3의 5번(연대기 제약 주입)만 skip하고 1~4·6(연대기-무관 가족/직업/성격 관계 + 호감도 시드)은 단독 동작 가능 ([PLAN R2](WORLD_HISTORY_PLAN.md)).

---

## 9. 구현 계획 / 진행 상황

상세 Phase 분해(R0~R4)와 현황은 → [WORLD_HISTORY_PLAN.md](WORLD_HISTORY_PLAN.md).

| 본 문서 | MAP_DESIGN.md 합류 위치 |
|---------|---------------------|
| §4~5 NPC 관계 | MAP_DESIGN 범위 밖 — 본 문서/NPC 문서로 유지 |
| (정착지 연대기·폐허 표현) | → [PROCEDURAL §3.5](PROCEDURAL_HISTORY_DESIGN.md) |

---

**문서 상태**: NPC 관계 설계 (호감도 채널 일부 구현)
**최근 갱신**: 2026-06-26 — 정착지 통폐합/연대기 시뮬(L1)을 [PROCEDURAL_HISTORY_DESIGN.md](PROCEDURAL_HISTORY_DESIGN.md)로 분리, 본 문서는 NPC 개인 관계(L2/L3)만 담당하도록 재편.
