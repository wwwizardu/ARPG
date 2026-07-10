# NPC 시뮬레이션 설계 (개별 LOD 시뮬 — 로밍·월드 영향)

> **목표**: 개별 NPC(모험가)가 **각자 행동하고, 월드를 돌아다니며, 월드에 영향을 주는 이벤트를 일으킨다.** 그 행적이 illusion(도착 시 생성)이 아니라 **실제로 축적·영속**되게 한다.
>
> **상태 (2026-07-09)**: 🟦 설계 초안 (미구현). **핵심 설계 결정 완료 (D1~D12)** — 개별 시뮬·스케일(보충 유지)·오프스크린 모델·이동/조우·전기 로그·행동 선택·승격/결정론까지. 잔여 = **세부 튜닝**(캡 K·계수·정착지 비율, 실측 후)과 **범위 밖**(노변 조우 이벤트 내용 = WORLD_EVENT/narrative). §9 참조.
>
> **이 문서의 위치**: NPC "시뮬레이션 **기반(substrate)**" — 개별 NPC를 어느 LOD 층위에서·어떻게 시뮬할지. 그 위에서 돌아가는 **활동 내용(원정 종류·파티·결과 롤)** 은 [NPC_ACTIVITY_DESIGN.md](NPC_ACTIVITY_DESIGN.md)가 담당한다. 본 문서는 NPC_ACTIVITY의 부품(`VentureState`/`RollOutcome`/`ScoreActivity`)을 **"개인·LOD무관"으로 승격**해 재사용한다(§10 supersession).
>
> **선행/관련 문서**:
> - [NPC_ACTIVITY_DESIGN.md](NPC_ACTIVITY_DESIGN.md) — 활동 카탈로그·원정 판정·파티·죽음/부상 (본 문서가 승격해 소비)
> - [PROCEDURAL_HISTORY_DESIGN.md §0](../world_history/PROCEDURAL_HISTORY_DESIGN.md) — 정착지 LOD·추상 인구·시간분할 틱 (동형 패턴 재사용)
> - [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md) — 관계·Sentiment·backfill(최초 시딩), NPC 전기 로그 채널(§4.2)
> - [NARRATIVE_ENGINE_DESIGN.md §4.8](../world_history/NARRATIVE_ENGINE_DESIGN.md) — 전기 로그·지식 거리·사건 정본화(일관성 소비처)
> - [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) — 행동이 유발하는 도전 이벤트
> - [NPC_SIMPLIFICATION_PLAN.md](NPC_SIMPLIFICATION_PLAN.md) — 물리 NPC=모험가·경제=추상 인구 분리 (본 설계의 전제)

---

## 1. 문제 & 현재 상태

Phase A~D로 마을은 자라지만, 스폰된 NPC는 **개별 이동 AI(Patrol)만** 있고 서로·세계에 영향을 주는 행동이 없다. 게다가:

- 먼 정착지 NPC는 `NpcSaveData`로 존재하되 **오프스크린 시뮬이 없다**(가만히 대기).
- [`MaterializeSettlement`](../../Assets/Scripts/History/HistoryManager.cs)는 인구 숫자만 catch-up하고 NPC 행동은 다루지 않는다.
- NPC↔NPC 변동원(원정)은 [NPC_ACTIVITY](NPC_ACTIVITY_DESIGN.md) Phase E로 미구현, 패시브 drift는 폐기됨.

**결과**: NPC는 "움직이지만 살지는 않는다." 본 설계는 **개별 NPC가 살아 움직이며 세계를 바꾸는** 시뮬 층을 정의한다.

---

## 2. 핵심 결정 (2026-07-09 확정)

| # | 결정 | 근거 |
|---|------|------|
| **D1** | **모든 NPC를 개별로 시뮬한다.** 정착지 숫자로 뭉뚱그리는 "대중 collapse" 층을 두지 않는다 | 개인 서사·월드 영향의 사실성. "가짜 히스토리" 회피 — 개인이 영속하고 행적이 진짜로 쌓임 |
| **D2** | 감당을 위해 **NPC 규모를 줄인다** | 정착지 밀도 축소(PROCEDURAL §0.1 "2500→~300" 전제) + 정착지당 소수 코호트. "전부 개별"을 예산 안에 넣는 유일한 방법 |
| **D3** | 개별 시뮬 대상 = **모험가**. 정착지 경제 인구는 추상 숫자로 분리 유지 | [NPC_SIMPLIFICATION D2](NPC_SIMPLIFICATION_PLAN.md) 계승 — 경제(`v.Population`)와 물리 NPC 분리. "규모 축소"는 모험가 코호트만 |
| **D4** | 개별 시뮬은 **2단계 LOD 표현**: 근처=풀 ECS / 멀리=경량 상태 tick. **개별 정체성은 양쪽 다 유지** | ECS 엔티티를 수백~수천 개 유지 불가. 멀리서는 정수 몇 개짜리 상태 전진 |
| **D5** | 오프스크린 = **경량 활동 상태머신 + 시간분할 라운드로빈 틱**, phase 경계에서만 resolve | 정착지 sim(System_SettlementSim) 패턴 재사용 — 프레임 비용 N-무관 상수 |
| **D6** | **발진(launch)도 LOD 무관.** 멀리 있는 개인도 스스로 활동을 시작한다 | NPC_ACTIVITY는 "발진=근처만"이었음 → 본 설계가 승격(§10) |
| **D7** | 결과는 **기존 훅에 되먹임**: 연대기 append + 전기 로그 + WORLD_EVENT 핸드오프 + 관계 갱신 | 새 개념 최소화. 이미 설계된 경로 재사용 |
| **D8** | materialize = **영속 개인의 ECS 승격**(backfill 재생성 아님). backfill은 월드 최초 시딩에만 | 도착 시 지어내지 않음 — 같은 개인 + 실제 축적 이력 |
| **D9** | **스케일 = 맵 크기별 고정 총량**(게임 시작 시 선택, 예: 소형 100·중형 200). 정착지에 **초기 인구 비례로 시딩**하고, 그 **총량을 목표로 보충 유지**(죽은 만큼 채움) | 하드 캡 = 시간분할 틱 비용 상한 + 플레이어 규모/난이도 노브. **보충 *방식*·정착지:NPC 비율은 tunable**(§9-1·§9-5) |
| **D10** | **활동 로직은 간단하게.** 원정 등은 **내부 시뮬 없이 시작 기록 → 종료 시 결과 1회**만(단일 구간). 무거운 다항 롤 대신 가벼운 로직 | 오프스크린 처리를 "종료 순간"에 집중 → catch-up 단순화(§9-2). 이해·튜닝 쉬움. NPC_ACTIVITY 부품은 간소화 재사용(§10) |
| **D11** | **오프스크린 이동 = 연속 좌표**(자기 턴마다 `lerp(from,to,경과율)`, O(1)) + 청크 인덱스로 근접 감지. 조우 시 **그 자리에 정지·상호작용(대화→이벤트) 가능**, 시야 밖으로 나가면 **목적지로 이동 재개**. **기본 조우(정지·대화)를 코어 스코프**로(파티 전투 조우는 후속) | 노드 방식은 위치가 없어 이동 중 조우 불가. 연속은 턴당 lerp뿐이라 안 비쌈. 조우+대화 = 창발 이벤트 진입점(§9-9) |
| **D12** | **행동 선택 = 성향 baseline × 맥락 modulate + 난수 → 가중 랜덤.** 작은 후보 메뉴(Hunt/Dungeon/Travel/Rest), 솔로 우선, `Courage`가 리스크 감수를 가름 | 개성(성향)과 반응성(맥락) 양립. NPC_ACTIVITY `ScoreActivity` 간소화판. 계수·비중은 실측 후 튜닝(§4.3) |

---

## 3. LOD 층 & 전환

| 층 | 대상 | 표현 | 무엇을 함 | 비용 |
|----|------|------|----------|------|
| **근처 (materialized)** | 활성 청크 NPC | 풀 ECS 엔티티 | 실제 이동·AI·전투·원정 발진·플레이 관측 | O(근처 NPC) |
| **멀리 (offscreen)** | 그 외 모든 모험가 | 경량 상태 레코드(`NpcSaveData` 확장) | coarse 활동 상태머신 tick(§4), 발진·판정·이벤트 유발 | O(틱 예산) — 시간분할 |

**전환** (정착지 fold-back과 동형):
- **멀리→근처 (승격)**: 청크 활성 → 오프스크린 상태 그대로 ECS 엔티티 스폰. 위치 = 그 시점 **analytic 좌표**(정착지에 있으면 노드 위치, 이동 중이면 `lerp` 지점, D11). **이동 중이던 NPC는 그 자리에 정지**해 상호작용 대기(배회 안 함 — 지나가버리면 조우 의미 없음).
- **근처→멀리 (강등)**: 청크 비활성 → ECS 파괴, 현재 위치·상태를 오프스크린 레코드로 접어 넣고 tick 재개. **정지했던 이동 NPC는 현재 위치에서 목적지로 이동 재개**(ETA 재계산).

> 정착지 LOD(추상 인구 ↔ 실체화)와 **별개 층**이다. 정착지 경제 인구는 여전히 추상 숫자(D3), NPC는 그 위에서 개별로 산다.

---

## 4. 오프스크린 상태 모델 (핵심)

각 NPC가 들고 있는 **경량 활동 상태머신**. 매 프레임 계산이 아니라 **phase 경계에서만** 일한다.

### 4.1 상태 (개념)

```
per-NPC 오프스크린 상태 (NpcSaveData에 얹음 — 이미 개별 영속):
  Activity        : Idle / Traveling / Venturing / Returning / Wounded / Dead
  LocationRef     : settlementId | "en route(from→to)" | targetRef(폐허/Zone)
  TargetKey       : 활동 대상 (hunt zone / dungeon seed / 이주 목적지)
  PhaseEndGameTime: 현재 활동 종료 게임시간 (= 다음에 깨어날 시각)
  LastSimGameTime : 개인 시계 (catch-up)
```

정수·enum 몇 개 → 수백~수천도 메모리·세이브 무해.

### 4.2 틱 방식 (System_SettlementSim 패턴 재사용)

- **모든 오프스크린 NPC를 꾸준히 순회**(라운드로빈): 200명이면 0.1초당 5~10명 식으로 계속 돌린다 → 각 NPC는 ~수초마다 방문됨. **프레임 비용은 N-무관 상수**(한 번에 배치 아님).
- **경계에서만 일함(early-out)**: 방문한 NPC의 활동 종료시각 `PhaseEndGameTime > now`면 no-op. `<= now`면 결과 처리 + 다음 활동 결정. 대부분 방문은 no-op라 실질 비용이 작다.
- **평상시 catch-up 불필요**: 계속 순회하므로 실시간 플레이에선 아무도 밀리지 않는다. 밀림은 **시간 점프**(여관 휴식·빨리감기·오래된 세이브)에서만 발생 → 처리 §9-2. (개인 `LastSimGameTime`으로 경과 계산 — 정착지 `SettlementPopulationModel`와 동형)

### 4.3 Phase 경계 3단계

1. **결정** (Idle → 무엇을, §2 D12): 후보 활동(`Hunt`/`DungeonDelve`/`Travel`/`Rest`)에 **utility 점수 = 성향 baseline + 맥락 보정 + 난수** → **가중 랜덤** 선택(argmax 아님 → 다양성). 성향이 baseline(겁쟁이는 잘 안 나감), 맥락(마을 위협·관계)이 modulate. 자기보존 vs 무모함은 `Courage`가 가름(무모한 죽음도 허용 = 드라마). v1 **솔로 우선**(파티 매치메이킹은 후속). = [NPC_ACTIVITY §7.1 `ScoreActivity`](NPC_ACTIVITY_DESIGN.md)의 **간소화판**, 계수·비중은 실측 후 튜닝(D10).
2. **활동 (타이머)**: 소요 시간만큼 `PhaseEndGameTime` 설정, Venturing/Traveling 전환. 이동은 **연속 좌표**(자기 턴마다 `lerp(from,to)`, D11)로 추적 — 조우 시 그 위치에 정지.
3. **결과 (롤)**: = [NPC_ACTIVITY §7.3 `RollOutcome`](NPC_ACTIVITY_DESIGN.md)의 **간소화 버전**(§10). 성공/부상/사망/이벤트유발 + 전리품. **결정론 시드**(NPC id + 출발 시각).

> **원정 = 단일 구간(중간 시뮬 없음).** 하위 phase가 없다 — **시작을 기록**하고 `PhaseEndGameTime`까지 **중간엔 시뮬 안 하며**, **종료 시 결과 1회만** 롤·기록한다. 그래서 오프스크린 처리는 사실상 "활동 종료 순간"에만 일어난다(§4.2 early-out과 맞물림). 이 단순함이 §9-2 catch-up 딜레마를 없앤다. (D10)

---

## 5. 데이터 모델

```csharp
// NpcSaveData 확장 (개별 영속)
public NpcActivityState Activity;      // §4.1 상태머신 (enum + LocationRef + TargetKey + PhaseEndGameTime)
public float LastSimGameTime;          // 개인 catch-up 시계
// 전기 로그 (biography) — 상세 §5.1. 지식 거리(§8)의 입력.
public List<ResidenceEntry> Residences;   // {SettlementId, from, to(§5.1-⑤ 시간창)}
public List<DeedEntry> Deeds;             // 겪은 일 — DeedEntry{ DeedKind Kind; int RefEventId=-1; List<int> Participants; HowKnown How; float When; }
```

- 원정 진행 상태는 NPC_ACTIVITY `VentureState`를 **개인 단위로 흡수**하거나 참조(파티는 멤버 id 목록).
- 정본은 `NpcManager` + `WorldData`(엔티티 수명과 분리 — 관계/원정 선례). **ECS 풀 추가 없음.**

### 5.1 전기 로그 (biography) — 결정 (§9-8 해소)

NPC가 *어디 살았고 무엇을 겪었나*의 개인 기록. **지식 거리(§8·[NARRATIVE_ENGINE §4.8](../world_history/NARRATIVE_ENGINE_DESIGN.md))의 입력**이자 대사 회고의 근거.

- **무엇을 기록 (①)** — 두 granularity:
  - **정착지 매크로**: 관련 `ChronicleEvent`(War/Conquer/Ruin 등) → `RefEventId`로 **참조**.
  - **NPC 레벨 (다양·열린 카탈로그)**: 원정 결과를 중심으로 한 **`DeedKind` enum** — 예) `VentureSuccess`/`VentureFailed`/`CompanionDied`(전우 사망)/`Rescued`(구출)/`Betrayed`(배신)/`NotableFind`(발견)/`Delved`(던전 공략)/`TriggeredEvent`(월드이벤트 유발) … **죽음만이 아니라 계속 확장 가능**. 참가자 있으면 `Participants`에 NPC ids.
  - 평범·무탈한 활동(그냥 사냥 성공)은 로그 안 함.
- **누가 기록 (③ — 로컬 복사)**: NPC 레벨 이벤트는 **원정 리졸버가 결과를 1회 계산해 참가자 전원 전기에 같은 내용 기록**. 단일 출처라 사본이어도 **모순 없음**(§8 일관성 원칙은 *LLM 독립 생성*에만 적용 — 시뮬 계산값은 해당 없음). 정착지 매크로는 EventId 참조.
- **보존 (② — NPC당 캡)**: deed 로그는 NPC당 상한 K. 넘치면 **중요도 가중**으로 밀어냄(가족 죽음·이벤트 유발 같은 정의적 사건은 오래 유지, 사소한 건 먼저 폐기). K·가중치는 tunable. 거주 이력은 작아 캡 불필요.
- **HowKnown (④)**: `Lived / Did / Witnessed / Heard` — 지식 거리 등급(§4.8 그대로). Heard(소문)는 전파 시스템 연결 후(§9-9/WORLD_EVENT §8).
- **거주 시간창 (⑤ — 나중)**: MVP는 `{SettlementId}`만(살았음→앎), `{from,to}` 시간창은 후속.
- **시딩 (⑥)**: 거주 = 출생 정착지 시드. deed = 거의 빈 상태로 시작 → 시뮬로 축적(관계 `OriginEventId` 몇 개는 씨앗).
- **영구 유대는 여기 아님**: 가족·친구·라이벌 등 지속 관계는 **관계 채널**(`NpcRelationEntry`)이 담당. 전기 deed는 **에피소드 사건**만(중복 금지).

---

## 6. 아키텍처

```
System_NpcSim (무상태 디렉터/리졸버, 시간분할 라운드로빈)
  매 FixedUpdate 커서에서 K개 NPC 방문:
    · PhaseEndGameTime > now → skip
    · <= now → Resolve: ①결과 롤(RollOutcome) ②되먹임(§7) ③다음 활동 결정(ScoreActivity)
  근처(materialized) NPC는 ECS AI가 처리, 오프스크린만 이 시스템이 tick
        │ 읽기: NpcStat / 관계·호감도 / 마을 필요도 / Zone·던전 난이도
        │ 위임(math): NPC_ACTIVITY VentureHelper (RollOutcome / ScoreActivity / EstimateDuration)
        ▼
NpcManager ── 개별 상태·전기 로그 소유·영속 ── WorldData
        │ 결과 적용 시 위임: SpawnNpc(승격) / UnregisterNpc(오프라인 죽음) / 연대기 append / WORLD_EVENT
```

- `IFixedUpdateSystem`, Priority는 Village Population 밴드(55-59) — NPC_ACTIVITY가 제안한 59 근처. `UpdateInterval`은 게임시간 기반.
- **무상태**: 상태는 전부 `NpcSaveData` + `NpcManager`.

---

## 7. 세계 영향 배선 (목표의 핵심)

phase 결과가 세계에 실제로 남는다 — 전부 기존 훅 재사용:

| 결과 | 되먹임 |
|------|--------|
| 사망 | Dead 마킹 + `RemoveAll*Involving` 관계 정리 + 연대기 `Fell` append + 유족 Grudge/Sentiment |
| 이벤트 유발 | WORLD_EVENT 디렉터 핸드오프 + 연대기 `Awakening`/`Blight`/… append ([NPC_ACTIVITY §9](NPC_ACTIVITY_DESIGN.md)) |
| 전리품 | 마을 자원/상점 재고 |
| 이주 | 거주 이력(전기 로그) append + 정착지 소속 변경 |
| 굵직한 행적 전반 | 전기 로그 `Deeds` append (`Did`/`Witnessed`) → 대사 회고의 근거 |

→ 오프스크린 개인이 **진짜로** 원정을 뛰고 죽고 무언가를 깨우며, 그게 연대기·전기에 남는다.

---

## 8. 일관성 (NARRATIVE_ENGINE §4.8 연결)

- 전기 로그는 §4.8 **지식 거리**의 입력이 된다 — 이 NPC가 어느 사건을 얼마나 직접 아는지(직접 겪음/거주/소문).
- 개별 시뮬로 행적이 **진짜**이므로, "도착 시 가짜 생성"이 사라진다 — 정본화 층의 부담도 준다(개인이 실제로 겪은 건 그 개인이 정확히 앎).
- 다만 **여러 NPC가 같은 사건을 회고**할 때의 사실 정합은 여전히 §4.8 사건 정본화가 담당.

---

## 9. 미결정 (착수 전·중 결정)

| # | 항목 | 메모 |
|---|------|------|
| 1 | ~~스케일 예산~~ → **결정(D9)**: 맵 크기별 고정 총량(소형 100·중형 200…) 선택 + 초기 인구 비례 시딩 + 총량 보충 유지. 이 전역 예산 할당이 기존 per-village `ComputeTargetNpcCount` 스폰을 대체 | **정착지:NPC 비율/밀도는 플레이하며 튜닝**(지금 고정 안 함). 시간분할 K는 그 위에서 |
| 2 | ~~catch-up 세밀도~~ → **해소**: 원정=단일 구간(중간 시뮬 없음, §4.3) + 모든 NPC 꾸준한 라운드로빈(§4.2)이라 평상시 catch-up 없음. **시간 점프 시에만** 그 사이 완료된 원정을 결제(각 1회 롤), 많으면 상한 캡 | 세밀도 딜레마 없음 (D10) |
| 3 | ~~오프스크린 이동 표현~~ → **결정(D11)**: 연속 좌표(턴마다 lerp) + 청크 인덱스. 조우 시 정지·상호작용, 시야 밖 재이동. 오픈월드 기본 조우 코어 편입 | 경로 표현(직선 vs coarse 웨이포인트)·근접 체크 세밀도는 tunable |
| 4 | ~~발진 빈도·최소 상주~~ → **보류**: 선택 로직(D12)의 **창발 결과**라 따로 정하지 않음. 마을 공동화가 실제로 문제되면 그때 최소 상주 가드 추가(NPC_ACTIVITY §3.3) | 실측 후 |
| 5 | ~~보충 여부~~ → **결정: 보충함**(총량 목표 유지, D9). **단 보충 *방식*은 나중에 결정** — 소스(정착지 추상 인구/출생/외부 유입)·속도(즉시 vs drip)·목표 분배 미정 | [SIMPLIFICATION D5](NPC_SIMPLIFICATION_PLAN.md) "재스폰 보류"를 이 설계가 해제 |
| 6 | ~~승격 좌표~~ → **결정(기본값)**: 정착지에 있으면 **반경 내 랜덤**(현행 `GetRandomSpawnPositionInVillage`), 이동 중이면 **analytic 좌표 그 지점**(D11) | — |
| 7 | ~~결정론~~ → **결정(기본값)**: 모든 롤은 **`DerivedSeed` 계열 시드(NPC id + 출발 시각)**, `UnityEngine.Random` 금지(프로젝트 규약). 세이브/로드·리플레이 일관 | — |
| 8 | ~~전기 로그 스키마~~ → **결정(§5.1)**: 정착지 매크로(EventId 참조) + NPC 레벨 **열린 이벤트 카탈로그**(원정 성공/실패·전우사망·구출·배신·발견·던전공략·이벤트유발…). 원정 리졸버가 참가자에 1회 기록(로컬 복사·단일출처). NPC당 캡(중요도 가중). 영구 유대는 관계 채널 | 시간창(⑤)·캡 K·가중치는 tunable/나중 |
| 9 | **노변 조우 이벤트 내용** → **범위 밖(보류)**: 이동 NPC 대화가 낳는 이벤트 카탈로그·발화는 **WORLD_EVENT/narrative 소관**, 본 문서 밖. D11이 진입점만 열어둠 | 나중 |
| 10 | ~~사망 관계 기억~~ → **구현 완료**: 관계 그래프에서는 사망 NPC를 제거하되, 생존 NPC에는 `DeceasedRelationMemory`(사망 NPC id·표시명 스냅샷·사망 전 관계 유형·방향·`Fell` EventId·사망 시각)를 남긴다 | `MarkNpcDead`가 `Fell` 연대기 이벤트를 먼저 만든 뒤 활성 관계 제거 전에 생존 NPC에 기억을 기록한다. Inspect 관계 목록은 “고인 배우자 ○○”처럼 표시한다. 호감도/활성 관계 엔트리는 제거해 현재 관계 판정에는 참여하지 않음 |
| 11 | **보충 NPC의 지역 난이도 적합성** → 전역 총량 보충 시 단순 정착지 부족 비율만으로 배정하지 않는다. 신규/저레벨 모험가의 시작 레벨·장비·전투력과 정착지 주변 Zone 위험도를 비교해 안전한 정착지에 우선 배치한다 | 이후 구현. 고난이도 지역에는 해당 위험도에 맞는 숙련 모험가만 보충하거나, 저레벨 NPC는 저위험 지역에서 성장·이동한 뒤 진입하게 한다. 지역 위험도·NPC 성장·이주/호위 규칙이 함께 필요 |

| 12 | **오프스크린 Hunt 결과 알림의 플레이어 친분도 게이트** — 화면에 있는 NPC의 Hunt 성공/실패/부상/사망 알림은 그대로 표시한다. 화면 밖 NPC는 플레이어와 `Acquaintance` 이상일 때만 부상·사망을 알린다. | 선행 구현 필요: 현재 `AffinityTier`는 대화 스파이크용 enum이며, 플레이어↔NPC 친분도 저장·증감(`ModifyPlayerRelation`)은 미구현이다. 관계 데이터가 구현될 때 Hunt 알림 조건에 연결한다. 그 전에는 현행처럼 모든 오프스크린 부상·사망을 알린다. |
| 13 | **NPC 런타임 ID와 역사 신원 분리** — 장기적으로 `EntityId`는 ECS/현재 생존 NPC에만 쓰고, `NpcHistoryId`는 전기·연대기·고인 관계 기억을 위한 영구 신원으로 분리한다. 사망 후에는 최소 역사 레코드만 보존하고 `NpcSaveData`와 런타임 ID는 정리·재활용할 수 있게 한다. | 현재는 `EntityId`가 영구 신원을 겸하며 사망 NPC `NpcSaveData`도 보존한다. 사망자 누적으로 세이브 크기 또는 예약 ID 증가가 실질적인 문제가 될 때 구현한다. 이행 시 `DeceasedRelationMemory`는 `DeceasedNpcHistoryId`를 참조하고, 살아 있는 NPC 간 실시간 관계·ECS는 계속 `EntityId`를 사용한다. |

---

## 10. NPC_ACTIVITY와의 관계 (supersession)

| NPC_ACTIVITY 기존 | 본 설계 |
|-------------------|---------|
| 발진 = **materialized 마을만** (D4) | **LOD 무관 발진** — 오프스크린 개인도 스스로(§D6) |
| 판정 = LOD 무관(D4) | 그대로 계승 |
| 원정 상태 = 별도 `VentureState` dict, 근처 편성 | 개인 활동 상태에 **흡수/참조**, 어디서나 |
| Anchored/Worker/Adventurer 3분류 | 폐기됨(SIMPLIFICATION D1) — 모험가 단일, 전부 시뮬 |

→ NPC_ACTIVITY의 **부품**(활동 카탈로그·`RollOutcome`·`ScoreActivity`·`PartyPower`·죽음/부상·이벤트 트리거)을 **간소화해 재사용**한다(D10) — 원정 내부 phase 없이 시작/종료만 다루므로 무거운 다항 롤이 아니라 **가벼운 로직**(단일 결과 롤)으로 축약. **"언제·어디서 시뮬되나"의 프레임**은 본 문서가 재정의. NPC_ACTIVITY §6.2("편성=LOD-gated, 판정=LOD-무관")는 본 설계의 D6로 **편성도 LOD-무관**으로 갱신 필요.

---

## 11. 구현 단계 (제안)

| Phase | 내용 | 규모 |
|-------|------|------|
| **NS1** | 오프스크린 상태 모델 + `System_NpcSim` 시간분할 틱 (Idle↔Venturing만, 솔로) | M |
| **NS2** | 결과 되먹임 배선 (죽음·전리품·연대기 append) + 전기 로그 `Deeds` | M |
| **NS3** | LOD 승격/강등 (오프스크린 ↔ ECS) + 좌표 실체화 | M |
| **NS4** | 이주(거주 이력) + 이벤트 유발(WORLD_EVENT) + 파티 | L |
| **NS5** | 스케일 튜닝(밀도 축소·틱 예산·catch-up 세밀도) + 결정론 검증 | M |

> **선행 검증**: NS1 전, 스케일 예산(§9-1)을 잡아 "전부 개별 tick"이 목표 규모에서 프레임 비용 상수인지 프로토타입으로 확인 권장.

---

## 12. 한 줄 요약

> **"규모를 줄인 모험가들이 각자 경량 상태머신으로 살아가며(근처=풀 ECS/멀리=시간분할 tick), 스스로 원정을 떠나 싸우고·죽고·무언가를 깨우고, 그 행적이 연대기와 개인 전기에 진짜로 남는다."**

---

**Status**: 설계 초안 (미구현) — 개별 LOD 시뮬. **핵심 결정 D1~D12 완료**, 잔여 = 세부 튜닝(실측 후) + 범위 밖 항목(§9)
**Last Updated**: 2026-07-09
**Dependencies**: NPC_ACTIVITY(활동 부품), PROCEDURAL_HISTORY(LOD·틱 패턴·정착지 밀도), NPC_RELATION_HISTORY(전기 채널·backfill), NARRATIVE_ENGINE §4.8(전기·지식거리·일관성), WORLD_EVENT(이벤트 트리거)
