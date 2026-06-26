# NPC 관계 + 정착지 히스토리 — 구현 계획

> **상태 (2026-06-18): 계획 — 미착수**
> **(2026-06-26 갱신 — 런타임 LOD)**: 정착지 시뮬은 "New Game 1회"가 아니라 **게임 중 지속(근처=상세/멀리=추상)**. 런타임 모델·3층 저장 구조의 정본은 [PROCEDURAL_HISTORY_DESIGN.md §0](PROCEDURAL_HISTORY_DESIGN.md). 이로 인해 **R0-B 저장 구조가 1회 스냅샷 → 라이브 상태(WorldSimState)로 바뀌고**, R1~R4 외에 "런타임 추상 틱 + 실체화/fold-back" 작업이 추가된다(아래 §11 참고, 단계적 도입).
> 본 문서는 [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md)(설계 초안)를 실제 코드 현황에 맞춰 실행 가능한 작업 단위로 분해한 것이다.
> 설계 문서가 "구현됨"으로 가정한 일부 자산이 실제로는 미구현임을 §0에서 먼저 정정하고, 그에 맞춰 Phase 범위를 조정했다.
>
> **선행 문서**: [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md)(설계), [VILLAGE_PHASES.md](VILLAGE_PHASES.md)(런타임 도메인), [MAP_DESIGN.md](MAP_DESIGN.md)(정착지 배치)

---

## 0. 현황 검증 — 설계 문서와의 차이 (착수 전 필독)

설계 §2 "기존 자산 정리" 표를 코드 기준으로 재검증한 결과, **계획의 전제 자체를 바꾸는 차이**가 있다. 설계는 호감도 수치 채널이 이미 있다고 가정하고 backfill/drift를 그 위에 얹었지만, 실제로는 **호감도 채널 전체가 미구현**이다.

### 0.1 설계가 "✅ 구현됨"이라 했으나 실제로 없는 것 (신규 구축 필요)

| 설계 표기 | 실제 상태 | 영향 |
|-----------|----------|------|
| `NpcPairKey` + `_npcAffinity` + `WorldData.NpcAffinityList` "✅ 구현됨" | **존재하지 않음** (코드 전역 검색 0건) | **R0에 "호감도 채널 신규 구축" 추가.** L2 backfill의 "호감도 시드"와 L3 drift가 모두 이 채널에 의존하므로 가장 먼저 만들어야 함 |
| `ModifyAffinity` / `GetAffinity` / `RemoveAllAffinityInvolving` API | **존재하지 않음** | 위와 동일. R0에서 NpcManager에 신규 작성 |
| `NpcSaveData`의 `NpcStat` 8성향 | 8성향은 ECS 컴포넌트 `NpcStatComponent`로 존재. **`NpcSaveData`엔 없고 영속화도 안 됨** | backfill이 성격을 읽으려면 활성 NPC의 `NpcStatComponent`를 조회해야 함 (§R2.3). 비활성/방문자 NPC엔 성향 컴포넌트가 없음([UINpcInspect.cs:193](../Assets/Scripts/UI/UINpcInspect.cs#L193) 주석 확인) |
| `SettlementId ↔ VillageId` 매핑 (설계 §4.4) | **매핑 개념 없음.** `VillageId`가 유일 식별자, 정착지(`RaceSettlementPlacer` 산출)와 `VillageData`는 미연결 | R1 산출물에 "정착지↔마을 매핑" 신규 설계 포함 |

### 0.2 설계대로 확인된 자산 (재사용 가능)

| 자산 | 위치 | 확인된 사실 |
|------|------|------------|
| `RaceSettlementPlacer.ComputeAll(mapSeed, chunkSize, minX, maxX, minY, maxY)` | [RaceSettlementPlacer.cs:31](../Assets/Scripts/Map/Race/RaceSettlementPlacer.cs#L31) | **동작 중.** `List<SettlementPlacement>`(RegionCoord/ChunkCoord/WorldCoord/Race/Size/ChunkBiome/RegionDominantBiome) 반환. Size는 현재 `Village` 고정 |
| `DerivedSeed.Derive(int worldSeed, ulong channel)` | [DerivedSeed.cs:30](../Assets/Scripts/History/Generator/DerivedSeed.cs#L30) | SplitMix64. 채널 상수 다수 존재(`ChannelSettlement` 등). **`ChannelChronicle`/`ChannelRelationBackfill` 추가 필요** |
| `DeterministicCoord` / `RegionCoord` | Assets/Scripts/Map/Region/ | 동작 중. `RegionCoord.CHUNKS_PER_REGION=4`, `ToRegionId` 등 |
| `VillageData.HasBeenPopulated` / `DepletedAt` | [VillageData.cs:20-21](../Assets/Scripts/Village/VillageData.cs#L20) | 존재. backfill 트리거(§R2.1)/Live Ruin(§R3.3) 훅 자리로 사용 |
| `RelationshipComponent`(Affinity/Trust/Fear/Intimacy 4축) | [RelationshipComponent.cs](../Assets/Scripts/Common/Component/RelationshipComponent.cs) | 스켈레톤. **폐기 대상**(§R3) |
| `System_Relationship` | [System_Relationship.cs](../Assets/Scripts/Common/System/System_Relationship.cs) | **Priority 58.5**(설계는 58로 표기), `UpdateInterval` 3.0s, OnFixedUpdate TODO만. **폐기→`System_NpcAffinityDrift`로 대체** |
| `EntityIdHelper` 관계 인프라 | [EntityIdHelper.cs](../Assets/Scripts/Common/EntityIdHelper.cs) | `_relationshipTargets`/`_relationshipSources` + Relationship 카테고리 offset 10,000,000 존재. **단, `CreateRelationshipEntity()` 메서드는 없음**(설계가 폐기 대상으로 적었으나 애초에 없음) |
| Tables 로딩 | [Tables.cs](../Assets/Scripts/Common/Tables.cs), [DataManager_Table.cs](../Assets/Scripts/Data/DataManager_Table.cs) | POCO(`[JsonProperty]`) → `ImmutableDictionary<int,T>`, `.bytes`를 `LoadTableAsync`로 병렬 로드. **`RaceRelationTable` 추가는 이 패턴 그대로** |
| `WorldData` | [WorldData.cs](../Assets/Scripts/Data/WorldData.cs) | `Version`(ushort) 필드 존재하나 **마이그레이션 로직 없음**(`LoadCompleted()` no-op). Newtonsoft JSON 전체 직렬화 |
| NpcManager 생성 경로 | [NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs) | 3개 경로: `RegisterNpcsFromMapFile`(L144), `SpawnNewNpc`(L239), `SpawnVisitorNpc`(L661). `SeedNpcEquipment`(L128)가 backfill 삽입 패턴의 본보기 |

### 0.3 결론 — 설계 대비 범위 조정

- **R0이 가장 무거워진다**: 데이터 모델뿐 아니라 **호감도 채널 전체(R0-A)** 를 먼저 만들어야 한다. 이것이 없으면 R2의 "호감도 시드"와 R3 drift가 동작할 수 없다.
- **R1에 정착지↔마을 매핑 설계가 추가**된다.
- 나머지(R2~R4)는 설계 의도 유지. 단 R2는 성격 입력을 `NpcStatComponent`(런타임)에서 읽는다는 점 명시.

---

## 1. Phase 요약

| Phase | 내용 | 핵심 산출물 | 선행 | 추정 |
|-------|------|------------|------|------|
| **R0-A** | 호감도 수치 채널 (설계가 있다고 가정했으나 미구현) | `NpcPairKey`, NpcManager 호감도 API, `WorldData.NpcAffinityList` | 없음 | 1.5~2일 |
| **R0-B** | 히스토리/관계 데이터 모델 + 저장 + 마이그레이션 | `ChronicleEvent/SaveData`, `NpcRelationEntry`, `NpcSentiment`, WorldData 직렬화 | R0-A | 1.5~2일 |
| **R1** | 정착지 연대기 시뮬 (New Game 1회) + **정복-병합/과확장(Tier 2)** + 정착지↔마을 매핑 | `SettlementChronicleSimulator`, `RaceRelationTable`, 디버그 덤프 | R0-B | 4~5일 |
| **R2** | NPC 관계 backfill (마을 첫 population) | 가구/가족/성격/파벌 생성기, 호감도 시드, `HasBeenPopulated` 훅 | R0-B (R1 선택) | 3~4일 |
| **R3** | 런타임 연속 + 구 골격 제거 | `System_NpcAffinityDrift`(Priority 58.5), 출생/사망 훅, `RelationshipComponent`/`System_Relationship` 삭제 | R2 | 2일 |
| **R4** | 표현 | NpcInspect 관계 탭, Sentiment 회고 대사 1종 | R2 (R1 있으면 회고 가능) | 2~3일 |

**의존 그래프**

```
R0-A ──▶ R0-B ──▶ R1 ──┐
                  └──▶ R2 ──▶ R3
                        └────▶ R4
            (R2는 R0-B만 있으면 연대기-무관 관계까지는 동작; R1 있으면 연대기 제약 주입 + 회고 가능)
```

**검증 마일스톤** (설계 §9): R1 완료 시 "연대기 텍스트 덤프"가 읽을 만한 역사인지 눈으로 평가. 특히 **다종족 제국이 형성되는지** + **한 왕국이 맵을 다 먹지 않고 여러 지역 제국이 공존하는지**(과확장 페널티 작동) 확인. 단조롭거나 한 덩어리로 수렴하면 R2 진입 전 사건 카탈로그/정복·과확장 가중치만 튜닝.

---

## 2. R0-A — 호감도 수치 채널 (신규)

> **왜 먼저인가**: 설계 §5.3-6은 "RelationType → 초기 Affinity 매핑 후 `_npcAffinity` 등록", §6.2는 "`ModifyAffinity(+1)`"을 호출한다. 두 채널 모두 존재하지 않으므로 **모든 후속 Phase의 전제**다.

### 2.1 목표

오프라인(청크 unload) NPC 간에도 살아있는 NPC 쌍 호감도 수치 채널. 정본 저장소는 NpcManager + WorldData(엔티티 수명과 분리 — 설계 §5.4 단서와 동일 근거).

### 2.2 산출물 (파일별)

| 파일 | 작업 |
|------|------|
| `Assets/Scripts/Npc/NpcPairKey.cs` (신규) | 정규화 페어 키 struct: `(Lo, Hi)` 항상 `Lo < Hi`, `IEquatable<NpcPairKey>` + `GetHashCode`. 생성자에서 정렬 |
| [NpcManager.cs](../Assets/Scripts/Manager/NpcManager.cs) | `Dictionary<NpcPairKey, int> _npcAffinity` 필드 + API: `int GetAffinity(int a, int b)`(없으면 0), `void ModifyAffinity(int a, int b, int delta)`(clamp), `void SetAffinity(int a, int b, int value)`, `void RemoveAllAffinityInvolving(int npcId)`(영구 사망 정리) |
| [WorldData.cs](../Assets/Scripts/Data/WorldData.cs) | `public List<NpcAffinityEntry> NpcAffinityList = new();` (Dictionary는 직렬화 친화 리스트로) + `NpcAffinityEntry{ int A; int B; int Value; }` |
| NpcManager `Save()`/`Load()` | `_npcAffinity` ↔ `NpcAffinityList` 변환. Load 경로에서 복원, Save에서 스냅샷 |

### 2.3 작업 체크리스트

- [ ] `NpcPairKey` struct (정규화 + equality). EntityId 음수/0 방어
- [ ] 호감도 clamp 범위 상수 정의 (예: −100~+100)
- [ ] `_npcAffinity` 필드 + 4개 API 작성 (CLAUDE.md 멤버 `_` 접두사, public→private 순서)
- [ ] `WorldData.NpcAffinityList` + 엔트리 클래스, Save/Load 변환
- [ ] `RemoveAllAffinityInvolving`를 NPC **영구 사망** 경로에 연결 (현 사망 처리 위치 확인 후 호출 — R3에서 relation 정리와 같은 자리)

### 2.4 검증 기준

- [ ] `SetAffinity(a,b,50)` 후 `GetAffinity(b,a) == 50` (대칭/정규화 확인)
- [ ] Save→Load 라운드트립 후 호감도 보존
- [ ] 존재하지 않는 페어 조회 시 0 반환 (예외 없음)

---

## 3. R0-B — 히스토리 / 관계 데이터 모델 + 저장

### 3.1 목표

설계 §3~§5.2의 모든 직렬화 타입 정의 + WorldData 합류 + 버전 마이그레이션 골격. **이 Phase는 시뮬/생성 로직 없이 "데이터가 저장/복원되는가"만 책임진다.**

> **⚠ 런타임 LOD 반영 (2026-06-26)**: 저장 구조는 [PROCEDURAL §0.3](PROCEDURAL_HISTORY_DESIGN.md)의 **3층(추상 라이브 / 연대기 / 실체화)** 을 따른다. 즉 추상 층은 *최종 스냅샷*이 아니라 **라이브 상태**: `SettlementSnapshot` → **`SettlementLiveState`**(Pop/KingdomId/Fate/IsMaterialized/VillageId + LastReconciledTime), 그리고 **`PairAffinity`(이웃 우호도)·`LastSimGameTime`·`SimRngState`** 를 담는 `WorldSimState`를 추가한다. `Chronicle.Events`는 append-only. 정착지↔마을 링크 `SettlementId`는 R1 산출물.

### 3.2 산출물 (파일별)

| 파일 | 내용 (설계 §4.3~4.4, §5.2) |
|------|---------------------------|
| `Assets/Scripts/History/Chronicle/ChronicleEvent.cs` (신규) | `ChronicleEvent{ int EventId; int Year; ChronicleEventType Type; int SubjectSettlementId; int ObjectSettlementId; int KingdomId; }` + `enum ChronicleEventType{ Trade, Skirmish, War, Conquer, Alliance, Merge, Ruin, KingdomFound, KingdomJoin, Promotion }` ※`Conquer`는 Tier 2(정복-병합) |
| `.../ChronicleSaveData.cs` (신규) | `ChronicleSaveData{ int Version=1; int SimulatedYears; List<ChronicleEvent> Events; List<SettlementSnapshot> Settlements; List<KingdomData> Kingdoms; }` |
| `.../SettlementSnapshot.cs` (신규) | 시뮬 최종 상태만: `SettlementId, Race, RegionCoord, Population, KingdomId, Fate, MergedIntoId, ConqueredByKingdomId, FoundedYear` (= `SettlementSimState`에서 시뮬-전용 필드 제외) + `enum SettlementFate{ Alive, Ruined, Merged, Conquered }` |
| `.../KingdomData.cs` (신규) | `KingdomData{ int KingdomId; string Name; GlobalEnum.RaceType FoundingRace; int CapitalSettlementId; List<int> MemberSettlementIds; }` — `FoundingRace`(건국 종족)로 다종족 제국의 지배/피지배 종족 구분 |
| `Assets/Scripts/Npc/NpcRelationEntry.cs` (신규) | `NpcRelationEntry{ int FromEntityId; int ToEntityId; NpcRelationType Type; int OriginEventId; }` + `enum NpcRelationType{ ParentOf=0, MentorOf=1, GrudgeAgainst=2, Spouse=100, Sibling=101, Friend=102, Rival=103 }` |
| `Assets/Scripts/Npc/NpcSentiment.cs` (신규) | `NpcSentiment{ NpcSentimentType Type; int TargetSettlementId; int OriginEventId; }` + `enum NpcSentimentType{ LostFamilyToWar, ProudOfKingdom, ResentsMerge, ResentsKingdom, ... }` |
| [WorldData.cs](../Assets/Scripts/Data/WorldData.cs) | `public ChronicleSaveData Chronicle = new();` + `public List<NpcRelationEntry> NpcRelationList = new();` |
| [NpcSaveData.cs](../Assets/Scripts/Npc/NpcSaveData.cs) | `public List<NpcSentiment> Sentiments = new();` (보통 0~1개) |
| [DerivedSeed.cs](../Assets/Scripts/History/Generator/DerivedSeed.cs) | 채널 상수 추가: `ChannelChronicle`, `ChannelRelationBackfill` (기존 8글자 ASCII 패턴 따라 `"CHRONICL"`, `"RELBKFIL"` 등) |
| WorldData `Version` 마이그레이션 | 현 `Version` 활용: 구 세이브(`Chronicle==null` 등) 로드 시 기본값 채우기. `LoadCompleted()`에 가드 추가 |

### 3.3 작업 체크리스트

- [ ] 위 타입 전부 정의 (`[Serializable]`, Newtonsoft 직렬화 기본 필드)
- [ ] `WorldData`/`NpcSaveData` 필드 추가, null-안전(구 세이브 로드 시 빈 컬렉션 보장)
- [ ] `WorldData.Version` 증가 + `LoadCompleted()`에 신규 필드 초기화 가드
- [ ] `DerivedSeed` 채널 2종 추가
- [ ] NpcManager에 관계 보관소: `Dictionary<int, List<NpcRelationEntry>>`(양방향 인덱스) + 조회 API `GetRelations(npcId)`, `GetRelation(a, b, type)`, Save/Load 변환(`WorldData.NpcRelationList` ↔)

### 3.4 검증 기준

- [ ] 빈 New Game 저장→로드 시 신규 컬렉션 모두 정상 (NPE 없음)
- [ ] **구버전 세이브(신규 필드 없는 JSON) 로드 시 크래시 없이 기본값 채워짐** ← 마이그레이션 핵심
- [ ] `NpcRelationEntry` 추가→Save→Load 후 양방향 조회 동일

---

## 4. R1 — 정착지 연대기 시뮬 (New Game 1회)

### 4.1 목표

설계 §4. `RaceSettlementPlacer.ComputeAll` 산출 정착지들을 1년 틱 coarse 시뮬로 돌려 Chronicle + Kingdom 스냅샷을 만들고 `WorldData.Chronicle`에 1회 저장. **저장이 정본(재생성 안 함).**

### 4.2 산출물 (파일별)

| 파일 | 내용 |
|------|------|
| `Assets/Scripts/History/Chronicle/SettlementSimState.cs` (신규, 시뮬 전용) | 설계 §4.1 struct (저장 안 함). `SettlementSnapshot`로 평탄화하여 저장 |
| `Assets/Scripts/History/Chronicle/SettlementChronicleSimulator.cs` (신규) | `ChronicleSaveData Simulate(int mapSeed, List<SettlementPlacement> placements, int years=150)`. 틱 루프(설계 §4.2): 인구성장 → 이웃 페어 이벤트 추첨 → 효과 적용 → 왕국 검사 → append. 모든 난수 `DerivedSeed.Derive(mapSeed, ChannelChronicle)` 계열만 (UnityEngine/System.Random 금지) |
| **정복-병합 (Tier 2)** | War 해소 분기: 승자가 인구 우위 + 과확장 여유면 패자 `Fate=Conquered`(KingdomId=승자국, **타종족 가능**, 정착지 생존) → `Conquer` 이벤트. 아니면 기존 pop −−/폐허 |
| **과확장 페널티 (Tier 2 필수 제동)** | 왕국 멤버 수가 클수록 추가 정복/병합 가중치 감쇠. **없으면 정복이 단일 제국 수렴을 악화** → 자기 제한으로 여러 다종족 지역 제국 공존 보장 |
| `Tables.cs` + `.bytes` | `RaceRelationTable{ Id, RaceA, RaceB, BaseAffinity, WarWeight, TradeWeight, MergeAllowed }`. **미결정(§7-2)**: v1은 코드 상수 dict로 시작 가능 → 시트 작업 분리 |
| 이웃 그래프 | `Dictionary<(int,int),int>` 우호도, **이웃만**(region 거리 ≤ 3, 정규화 페어). 전쌍 O(N²) 금지 |
| New Game 호출부 | 맵 생성 직후(placement 확정 시점) 1회 호출 → `WorldData.Chronicle` 대입. 세이브 로드 경로는 호출 안 함 |
| **정착지↔마을 매핑** (신규 설계) | `SettlementId`(placement 인덱스 결정론 ID)와 `VillageData.VillageId` 연결. 방안: `VillageData`에 `SettlementId` 필드 추가 + VillageManager 등록 시 매핑 기록. 연대기 조회 경로(설계 §4.4) |
| 에디터 디버그 덤프 | "연대기 출력" 메뉴 → Chronicle을 사람이 읽는 텍스트로(년/사건/주체). **검증 마일스톤의 도구** |

### 4.3 작업 체크리스트

- [ ] `SettlementSimState` + 이웃 그래프 구축(region 거리 ≤ 3)
- [ ] 틱 루프 5단계 구현, 사건 카탈로그 v1(설계 §4.3 표 **9종**, `Conquer` 포함)
- [ ] **정복-병합**: War 해소 시 Conquer 분기(패자 `Fate=Conquered`, KingdomId 재지정, 타종족 가능)
- [ ] **과확장 페널티**: 왕국 크기별 확장 가중치 감쇠 (자기 제한 제동) + `KingdomData.FoundingRace` 기록
- [ ] 왕국 클러스터 검사(`KingdomFound`/`KingdomJoin`), 수도 = 최대 인구
- [ ] `RaceRelationTable` (v1 코드 상수 or .bytes — §9 결정 따름)
- [ ] `SettlementSnapshot`/`KingdomData` 평탄화 저장
- [ ] `SettlementId↔VillageId` 매핑 (`VillageData.SettlementId` 추가 + 등록 훅)
- [ ] New Game 1회 호출 배선 + 로드 경로 비호출 가드
- [ ] 에디터 "연대기 덤프" 메뉴

### 4.4 검증 기준

- [ ] 같은 `mapSeed` New Game 2회 → **동일 Chronicle**(결정론)
- [ ] 인과 모순 없음: `Merge`/`Ruin` 정착지가 이후 능동 사건 주체로 등장 안 함, `Ruin` 후 부활 없음, `Conquered` 정착지는 생존하되 KingdomId가 승자국
- [ ] **다종족 제국이 실제로 형성**된다 (`KingdomData.MemberSettlementIds`에 `FoundingRace`와 다른 종족 정착지 포함)
- [ ] **단일 제국 수렴 안 함** — 한 왕국이 전체 정착지의 과반(예: >40%)을 먹지 않고 여러 지역 제국 공존 (과확장 페널티 작동 확인). 안 되면 페널티/연수 튜닝
- [ ] 비용: 정착지 ~2,400 × 이웃 ~8 × 150틱이 New Game 시 **수십 ms** (설계 §4.6)
- [ ] **덤프가 "읽을 만한 역사"인가** (사건 평탄화 함정 — 재미없으면 가중치 튜닝)

### 4.5 맵 표현 연결 (설계 §4.5, MAP_DESIGN 합류)

`Fate==Ruined/Merged` 정착지 → 폐허 variant + `FateKeyword`(War→`burned`, Merge→`forgotten`). **R1 범위에선 데이터만 노출**, 실제 폐허 배치는 MAP_DESIGN Lore Fragment 작업과 합류 시(보류 가능).

---

## 5. R2 — NPC 관계 backfill (마을 첫 population)

### 5.1 목표

설계 §5. `VillageData.HasBeenPopulated` false→true 되는 **기본 NPC 첫 스폰 직후 1회**, 연대기를 제약으로 가족/직업/성격/파벌 관계 + Sentiment + 호감도 시드 생성. 세이브 로드/이민/InnVisitor는 대상 아님.

### 5.2 트리거 위치

- `SeedNpcEquipment`([NpcManager.cs:128](../Assets/Scripts/Manager/NpcManager.cs#L128))와 동일한 신규 생성 경로. 마을 단위로 첫 NPC 일괄 스폰이 끝난 직후 `HasBeenPopulated` 토글 자리에 backfill 1회.
- **주의**: 성격 입력은 `NpcStatComponent`(활성 NPC ECS 컴포넌트)에서 읽는다. backfill이 스폰 직후 실행되므로 컴포넌트가 존재함 — 이 타이밍 의존을 코드 주석으로 명시.

### 5.3 산출물 (파일별)

| 파일 | 내용 |
|------|------|
| `Assets/Scripts/Npc/Relation/NpcRelationBackfill.cs` (신규) | `void Generate(int villageId, int settlementId, IReadOnlyList<int> npcEntityIds)`. 마을 시드 `DerivedSeed.Derive(mapSeed ^ settlementId, ChannelRelationBackfill)`. 6단계(설계 §5.3): ①가구 분할 ②가족(Spouse/ParentOf/Sibling, 세대 ≤ 2) ③직업 MentorOf ④성격 가중 추첨(Friendliness→Friend↑, Pride×동일직업→Rival↑) ⑤연대기 제약 주입 ⑥호감도 시드 |
| 호감도 시드 매핑 | RelationType→초기 Affinity (Spouse +60 / Sibling +40 / Friend +30 / Rival −20 / Grudge −50 / 파벌교차 −10). R0-A의 `SetAffinity` 사용 |
| 연대기 제약 주입 | 이 마을 관련 `Chronicle.Events` 추출 → Merge: 두 파벌 분할 + `ResentsMerge`; War: `GrudgeAgainst`(상대 마을 스폰 시 연결) 또는 `LostFamilyToWar` Sentiment; **Conquer(피정복지): 다수 NPC에 `ResentsKingdom`(Loyalty 낮을수록 강함), 지배종족(`KingdomData.FoundingRace`) 거주민과 초기 호감도 −, 한 마을 안 지배/피지배 파벌 형성**; KingdomFound/Join: Loyalty 성향 따라 `ProudOfKingdom`/`ResentsKingdom` 분포 |
| 매니저 배선 | 생성된 엔트리 → NpcManager 관계 보관소(R0-B), Sentiment → 각 `NpcSaveData.Sentiments` |

### 5.4 작업 체크리스트

- [ ] 가구 결정론 분할(1~5인), 가족 관계 배정
- [ ] 직업/성격 관계 (`NpcStatComponent` 조회)
- [ ] 연대기 제약 주입 5종 (R1 없으면 이 단계만 skip — 연대기-무관 관계는 동작)
- [ ] 호감도 시드 → `SetAffinity`
- [ ] `HasBeenPopulated` 토글 자리에 1회 호출, 로드/이민/방문자 제외
- [ ] 관계 밀도 목표 준수: NPC당 가족 1~4 + 비가족 1~3

### 5.5 검증 기준

- [ ] Settlement(3~5명)~City(40명) 모두 수백 엔트리 이하
- [ ] 같은 시드 New Game → 동일 관계 (결정론; `NpcStatComponent`가 `UnityEngine.Random`이라 **성격은 비결정론** — §7-4 미결정 사항으로 명시. 결정론 원하면 성향 생성도 시드 채널화 필요)
- [ ] backfill은 마을당 정확히 1회 (재스폰/로드 시 미발동)
- [ ] Merge 마을: 두 파벌 간 초기 호감도 음수 확인

---

## 6. R3 — 런타임 연속 + 구 골격 제거

### 6.1 목표

설계 §6. 출생/사망 시 관계 갱신 + 호감도 자연 변동 + 구 `RelationshipComponent`/`System_Relationship` 제거.

### 6.2 산출물 (파일별)

| 파일 | 내용 |
|------|------|
| `Assets/Scripts/Common/System/System_NpcAffinityDrift.cs` (신규) | `IFixedUpdateSystem`, **Priority 58.5**(구 System_Relationship 자리, Population 밴드 55-59), `UpdateInterval` 수 초. 같은 마을 + 둘 다 활성 NPC 페어만: 근접/같은 작업장 `ModifyAffinity(+1)`, `NpcStatComponent`(Friendliness/Patience) 가중. Rival/Grudge는 음수 drift |
| 출생 훅 | Phase C 출생 시스템: 부모 `ParentOf` + 형제 `Sibling` + 가족 호감도 시드 |
| 분가 훅 | Phase C 분가: 가족 관계 유지(마을 달라져도 보존 → 마을 간 연결고리) |
| 사망 훅 | NPC 영구 사망 경로: `RemoveAllAffinityInvolving`(R0-A) + relation 엔트리 정리 + 유족 Grudge/Sentiment 후보(사인 명확 시) |
| Live 연대기 | 마을 전멸(`DepletedAt` 확정)→`Ruin` append, VillageStage Town/City 도달→`Promotion` append (EventId 이어서 발급) |
| **제거** | `RelationshipComponent.cs`, `System_Relationship.cs`, SystemManager 등록 제거, `EntityIdHelper` 관계 인프라 정리(`_relationshipTargets`/`_relationshipSources`가 다른 곳에서 안 쓰이면 제거) |

### 6.3 작업 체크리스트

- [ ] `System_NpcAffinityDrift` 작성 + SystemManager 등록(구 System_Relationship 등록 줄 교체, Priority 58.5 유지)
- [ ] 출생/분가/사망 훅을 Phase C 기존 경로에 삽입 (신규 시스템 아님)
- [ ] Live `Ruin`/`Promotion` append
- [ ] `RelationshipComponent`/`System_Relationship`/관련 EntityIdHelper 코드 삭제 + 빌드 확인
- [ ] **멀리(추상) 정치 사건 지속(합병/왕국)은 v1 범위 밖** = [PROCEDURAL §0.4](PROCEDURAL_HISTORY_DESIGN.md) (나). v1 멀리-시뮬은 인구 동역학(가)만 — 훅 자리만 주석으로 명시

### 6.4 검증 기준

- [ ] drift: 같은 작업장 두 NPC 호감도가 시간 경과로 상승
- [ ] 비활성 마을 NPC 관계 **동결**(오프라인 시뮬 없음 — 원칙 §1-1)
- [ ] 영구 사망 후 해당 NPC 관여 호감도/관계 잔존물 없음
- [ ] 구 골격 제거 후 컴파일/기존 기능 회귀 없음

---

## 7. R4 — 표현 (소비처)

### 7.1 목표

설계 §7. 데이터를 플레이어가 볼 수 있게: NpcInspect 관계 탭 + Sentiment 회고 대사 1종.

### 7.2 산출물 (파일별)

| 파일 | 내용 |
|------|------|
| [NpcInspect.uxml](../Assets/UI/NpcInspect/NpcInspect.uxml) + [UINpcInspect.cs](../Assets/UI/UINpcInspect.cs) | 관계 탭: 가족/친구/원한 목록 + 호감도 수치. (성향 표시처럼 활성 NPC만 — 비활성은 관계는 보이되 성향은 비표시 패턴 유지) |
| 대사 분기 1종 | 관계/Sentiment → 대사 풀 분기. `OriginEventId` → 연대기 사건 1줄 회고("30년 전 {상대마을}과의 전쟁에서…"). R1 있으면 회고 가능, 없으면 관계 기반 대사만 |

### 7.3 작업 체크리스트

- [ ] NpcInspect 관계 탭 UI + 바인딩(`GetRelations`/`GetAffinity`)
- [ ] Sentiment/관계 → 대사 분기 최소 1종
- [ ] `OriginEventId` → 연대기 사건 텍스트화 헬퍼 (R1 의존)

### 7.4 검증 기준

- [ ] backfill된 마을 NPC inspect 시 가족/친구/원한 표시
- [ ] Merge/War 마을 NPC가 회고 대사 노출

---

## 8. 시드 채널 / 테이블 / 풀 추가 (설계 §8)

```csharp
// DerivedSeed 채널 추가 (R0-B)
ChannelChronicle          // L1 시뮬 PRNG
ChannelRelationBackfill   // L2 마을별 backfill PRNG

// 신규 테이블 (R1, 시트 작업은 §9 미결정 따라 결정)
RaceRelationTable { Id, RaceA, RaceB, BaseAffinity, WarWeight, TradeWeight, MergeAllowed }

// WorldData 추가 (R0-A: NpcAffinityList, R0-B: Chronicle, NpcRelationList)
// NpcSaveData 추가 (R0-B: Sentiments)
// VillageData 추가 (R1: SettlementId)
```

**ECS 풀 추가 없음** — 본 설계는 의도적으로 ECS 컴포넌트를 만들지 않는다(설계 §5.4). 오히려 `RelationshipComponent`를 제거한다.

---

## 9. 리스크 / 미결정 사항 (착수 전·중 결정)

**✅ 결정됨 (2026-06-18)** — 정치 모델은 **Tier 2 (정복-병합 + 과확장 페널티)** 채택. 전쟁 승자가 패자를 자국에 편입(타종족 가능) → 다종족 제국 + 피정복민 서사. 과확장 페널티로 단일 제국 수렴 방지. **분열 동역학(Tier 3: Secession / 왕국 간 전쟁 / 자동 스윕 튜닝 하니스)은 보류** — Tier 2 안정화 후, 덤프가 너무 정적이면 *하나씩* 증분 도입(반대 힘 동시 튜닝 회피). 상세는 [설계 §11.1](NPC_RELATION_HISTORY_DESIGN.md).

설계 §11 + 본 검증에서 추가된 미결정 항목.

| # | 항목 | 출처 | 결정 시점 |
|---|------|------|----------|
| 1 | **호감도 채널이 미구현** — R0-A가 신규 작업이며 전체 일정의 전제 | 본 검증 §0.1 | 착수 즉시 (R0-A 우선) |
| 2 | 시뮬 연수/사건 밀도 (150년 가설) | 설계 §11-1 | R1 덤프 보고 튜닝 |
| 3 | `RaceRelationTable` 외부화 — 시트 vs 코드 상수 | 설계 §11-2 | R1 착수 시 (v1 코드 상수 권장) |
| 4 | 왕국 작명 — 절차("{종족} 왕국 N") vs 어근 조합기 | 설계 §11-3 | R1 (v1 절차 권장) |
| 5 | **`NpcStatComponent` 비결정론** — `UnityEngine.Random` 생성, 영속화 안 됨. 같은 시드 재현성 원하면 backfill 채널로 시드화 필요 | 설계 §11-4 + 본 검증 §0.1 | R2 착수 시 |
| 6 | `SettlementId↔VillageId` 매핑 방식 — `VillageData`에 필드 추가 vs 별도 매핑 테이블 | 본 검증 §0.1 | R1 |
| 7 | InnVisitor 출신 정착지 부여 | 설계 §11-5 | v2 |
| 8 | **정복 annex 발동 조건 / 과확장 페널티 곡선** — War 승리→Conquer 전환 확률, 왕국 크기별 확장 가중치 감쇠. "단일 제국 수렴 ↔ 과도 파편화" 밸런스 | 설계 §11-6 | R1 덤프 보고 튜닝 |

**보류**: 멀리(추상) **정치 사건 지속**([PROCEDURAL §0.4](PROCEDURAL_HISTORY_DESIGN.md) (나) — v1 멀리-시뮬은 인구 동역학(가)만), Tier 3(Secession / 왕국 간 전쟁 / 자동 스윕 튜닝 하니스), 왕국 게임플레이(세금/평판), 4축 관계, LLM 연대기 문장화, 혼인 기반 마을 간 가계.

> **런타임 LOD 추가 작업 (R1~R4 외, 단계적)** — [PROCEDURAL §0](PROCEDURAL_HISTORY_DESIGN.md) 기준: ① 멀리-추상 틱(인구 동역학) 백그라운드 시스템 ② 실체화(추상→상세) + catch-up ③ fold-back(상세→추상) + 연대기 보존 정책. ④(나) 멀리 정치 사건 지속은 그 다음.

---

## 10. 권장 진행 순서 요약

1. **R0-A** (호감도 채널) — 없으면 그 무엇도 동작 안 함. 가장 먼저.
2. **R0-B** (데이터 모델 + 마이그레이션) — 빈 저장/로드 라운드트립으로 검증.
3. **R1** (연대기 시뮬) — 덤프로 "읽을 만한 역사" 검증 (마일스톤). 재미없으면 여기서 튜닝하고 멈출 수 있음.
4. **R2** (backfill) — R1 없이도 연대기-무관 관계까지는 착수 가능 (단계 분리).
5. **R3 / R4** 병렬 가능 (둘 다 R2 의존).
