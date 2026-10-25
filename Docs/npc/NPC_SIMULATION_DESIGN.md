# NPC 시뮬레이션 설계 (개별 LOD 시뮬 — 로밍·월드 영향)

> **목표**: 개별 NPC(모험가)가 **각자 행동하고, 월드를 돌아다니며, 월드에 영향을 주는 이벤트를 일으킨다.** 그 행적이 illusion(도착 시 생성)이 아니라 **실제로 축적·영속**되게 한다.
>
> **상태 (2026-10-04 갱신)**: 🟩 **MVP 구현 완료** — NS1~NS3의 개인 LOD 시뮬·결과 반영·LOD 전환과 D9 전역 총량/점진 보충이 코드에 반영되었다. 현재 활동은 **Rest, Hunt(솔로·파티), Trade, DungeonDelve(솔로·파티)** 이고, Rest가 끝날 때 소문 교환과 NPC 간 절도 판정이 붙는다. 활동 동기·경제·소문·파티 자동 구성·부상 등급·절도 규칙은 [NPC_AMBITION_DESIGN.md](NPC_AMBITION_DESIGN.md)(P1·P2·P4, D85~D96)가 소유한다(§11.1). 개인 기억은 **중요 사건 이력 + 공동 활동 통계 + 종류별 최신 사연**으로 저장한다(§5.1). 강제 이주는 구현됐으며 자발적 이주·거주 이력·WORLD_EVENT 유발은 후속이다. 잔여 = 실측 기반 튜닝과 §9의 후속/범위 밖 항목.
> **2026-10-25 결정·구현**: 마을 안 표현 개정 — 원정·이주 출발·귀환은 걸어서, 부상 회복은 여관 안, 모든 마을에 여관, 여관 창의 NPC 목록에서 대화(§3.1 D13~D16). 같은 날 구현했다(커밋 대기, 실제 플레이 확인 전).
> ~~상태 (2026-07-13): 현재 활동 범위는 Rest, 솔로 Hunt, Hunt 파티~~
>
> **이 문서의 위치**: NPC "시뮬레이션 **기반(substrate)**" — 개별 NPC를 어느 LOD 층위에서·어떻게 시뮬할지. 그 위에서 돌아가는 **활동 내용(원정 종류·파티·결과 롤)** 은 [NPC_ACTIVITY_DESIGN.md](NPC_ACTIVITY_DESIGN.md)가 담당한다. 본 문서는 NPC_ACTIVITY의 부품(`VentureState`/`RollOutcome`/`ScoreActivity`)을 **"개인·LOD무관"으로 승격**해 재사용한다(§10 supersession).
>
> **선행/관련 문서**:
> - [NPC_ACTIVITY_DESIGN.md](NPC_ACTIVITY_DESIGN.md) — 활동 카탈로그·원정 판정·파티·죽음/부상 (본 문서가 승격해 소비)
> - [PROCEDURAL_HISTORY_DESIGN.md §0](../world_history/PROCEDURAL_HISTORY_DESIGN.md) — 정착지 LOD·추상 인구·시간분할 틱 (동형 패턴 재사용)
> - [NPC_RELATION_HISTORY_DESIGN.md](NPC_RELATION_HISTORY_DESIGN.md) — 관계·Sentiment·backfill(최초 시딩), NPC 전기 로그 채널(§4.2)
> - [NARRATIVE_ENGINE_DESIGN.md §4.8](../world_history/NARRATIVE_ENGINE_DESIGN.md) — 전기 로그·지식 거리·사건 정본화(일관성 소비처)
> - [WORLD_EVENT_DESIGN.md](../world_history/WORLD_EVENT_DESIGN.md) — 행동이 유발하는 도전 이벤트
> - [VILLAGE_NPC_BOUNDARY_DESIGN.md](VILLAGE_NPC_BOUNDARY_DESIGN.md) — 물리 NPC=모험가·경제=추상 인구 분리 (본 설계의 전제)

---

## 1. 문제(구현 전) & 현재 상태

Phase A~D로 마을은 자라지만, 스폰된 NPC는 **개별 이동 AI(Patrol)만** 있고 서로·세계에 영향을 주는 행동이 없다. 게다가:

- 먼 정착지 NPC는 `NpcSaveData`로 존재하되 **오프스크린 시뮬이 없다**(가만히 대기).
- [`MaterializeSettlement`](../../Assets/Scripts/History/HistoryManager.cs)는 인구 숫자만 catch-up하고 NPC 행동은 다루지 않는다.
- NPC↔NPC 변동원(원정)은 [NPC_ACTIVITY](NPC_ACTIVITY_DESIGN.md) Phase E로 미구현, 패시브 drift는 폐기됨.

**결과**: NPC는 "움직이지만 살지는 않는다." 본 설계는 **개별 NPC가 살아 움직이며 세계를 바꾸는** 시뮬 층을 정의한다.

**현재 MVP**: 오프스크린 `NpcSaveData` 상태머신과 `System_NpcSim`이 모든 생존 NPC를 시간분할 처리한다. Rest와 솔로·파티 Hunt, 상점 Trade, 솔로·파티 DungeonDelve의 결과가 Food·전리품·Gold·부상·사망·연대기·전기에 반영되고, 원정 이동은 LOD 전환 중에도 이어진다. Rest 종료 때 소문 교환·NPC 간 절도가 일어난다. 활동 동기와 경제는 [NPC_AMBITION_DESIGN.md](NPC_AMBITION_DESIGN.md)가 소유한다(P1 2026-09-28, P2 던전·소문 2026-09-29, 파티 자동 구성 D85~D90 2026-09-29, 부상 등급 D91~D96 2026-09-30, P4 절도 2026-10-02). 이주·WORLD_EVENT 유발은 아직 구현하지 않았다.

---

## 2. 핵심 결정 (2026-07-09 확정)

| # | 결정 | 근거 |
|---|------|------|
| **D1** | **모든 NPC를 개별로 시뮬한다.** 정착지 숫자로 뭉뚱그리는 "대중 collapse" 층을 두지 않는다 | 개인 서사·월드 영향의 사실성. "가짜 히스토리" 회피 — 개인이 영속하고 행적이 진짜로 쌓임 |
| **D2** | 물리 NPC는 마을당 최소 밀도를 보장하되 **시간분할 LOD와 분할 세이브로 비용을 제한한다** | 마을이 많아도 비어 보이지 않게 최소 5명·평균 8명을 목표로 하고, 프레임 비용은 라운드로빈 예산, 저장 비용은 32샤드로 제한 |
| **D3** | 개별 시뮬 대상 = **모험가**. 정착지 경제 인구는 추상 숫자로 분리 유지 | [마을–NPC 경계](VILLAGE_NPC_BOUNDARY_DESIGN.md) 준수 — 경제(`v.Population`)와 물리 NPC 분리. "규모 축소"는 모험가 코호트만 |
| **D4** | 개별 시뮬은 **2단계 LOD 표현**: 근처=풀 ECS / 멀리=경량 상태 tick. **개별 정체성은 양쪽 다 유지** | ECS 엔티티를 수백~수천 개 유지 불가. 멀리서는 정수 몇 개짜리 상태 전진 |
| **D5** | 오프스크린 = **경량 활동 상태머신 + 시간분할 라운드로빈 틱**, phase 경계에서만 resolve | 정착지 sim(System_SettlementSim) 패턴 재사용 — 프레임 비용 N-무관 상수 |
| **D6** | **발진(launch)도 LOD 무관.** 멀리 있는 개인도 스스로 활동을 시작한다 | NPC_ACTIVITY는 "발진=근처만"이었음 → 본 설계가 승격(§10) |
| **D7** | 결과는 **기존 훅에 되먹임**: 연대기 append + 전기 로그 + WORLD_EVENT 핸드오프 + 관계 갱신 | 새 개념 최소화. 이미 설계된 경로 재사용 |
| **D8** | materialize = **영속 개인의 ECS 승격**(backfill 재생성 아님). backfill은 월드 최초 시딩에만 | 도착 시 지어내지 않음 — 같은 개인 + 실제 축적 이력 |
| **D9** | **스케일 = `max(맵 규모 최소값, 마을 수×8)`**. 작은 맵 최소 100명, 대형 맵 최소 1,200명이며 마을당 최소 5명을 먼저 보장한 뒤 연대기 인구 가중치로 남은 인원을 배분한다. 생존 수가 목표보다 낮으면 6 게임시간마다 최대 1명 보충한다. | 300개 마을이면 목표 2,400명. 개별 시뮬은 시간분할하고 저장은 [SAVE_PARTITION_DESIGN](../tech/SAVE_PARTITION_DESIGN.md)의 32 NPC 샤드를 사용한다. |
| **D10** | **활동 로직은 간단하게.** 원정 등은 **내부 시뮬 없이 시작 기록 → 종료 시 결과 1회**만(단일 구간). 무거운 다항 롤 대신 가벼운 로직 | 오프스크린 처리를 "종료 순간"에 집중 → catch-up 단순화(§9-2). 이해·튜닝 쉬움. NPC_ACTIVITY 부품은 간소화 재사용(§10) |
| **D11** | **오프스크린 이동 = 연속 좌표**(자기 턴마다 `lerp(from,to,경과율)`, O(1)) + 청크 인덱스로 근접 감지. 조우 시 **그 자리에 정지·상호작용(대화→이벤트) 가능**, 시야 밖으로 나가면 **목적지로 이동 재개**. **기본 조우(정지·대화)를 코어 스코프**로(파티 전투 조우는 후속). **예외(2026-10-25, D14)**: 마을 안 원정 출발·귀환은 정지하지 않고 걸어서 보여 준다(§3.1) | 노드 방식은 위치가 없어 이동 중 조우 불가. 연속은 턴당 lerp뿐이라 안 비쌈. 조우+대화 = 창발 이벤트 진입점(§9-9) |
| **D12** | **행동 선택 = 성향 baseline × 맥락 modulate + 난수 → 가중 랜덤.** 작은 후보 메뉴(Hunt/Dungeon/Travel/Rest), 솔로 우선, `Courage`가 리스크 감수를 가름 | 개성(성향)과 반응성(맥락) 양립. NPC_ACTIVITY `ScoreActivity` 간소화판. 계수·비중은 실측 후 튜닝(§4.3) |

---

## 3. LOD 층 & 전환

| 층 | 대상 | 표현 | 무엇을 함 | 비용 |
|----|------|------|----------|------|
| **근처 (materialized)** | 활성 청크 NPC | 풀 ECS 엔티티 | 실제 이동·AI·전투·원정 발진·플레이 관측 | O(근처 NPC) |
| **멀리 (offscreen)** | 그 외 모든 모험가 | 경량 상태 레코드(`NpcSaveData` 확장) | coarse 활동 상태머신 tick(§4), 발진·판정·이벤트 유발 | O(틱 예산) — 시간분할 |

**전환** (정착지 fold-back과 동형):
- **멀리→근처 (승격)**: 청크 활성 → 오프스크린 상태 그대로 ECS 엔티티 스폰. 위치 = 그 시점 **analytic 좌표**(정착지에 있으면 노드 위치, 이동 중이면 `lerp` 지점, D11). **이동 중이던 NPC는 그 자리에 정지**해 상호작용 대기(배회 안 함 — 지나가버리면 조우 의미 없음). 단, 그 자리가 마을 안이면 정지 대신 걷고, 여관 안 부상자는 승격하지 않는다(§3.1 D13·D14, 2026-10-25 구현).
- **근처→멀리 (강등)**: 청크 비활성 → ECS 파괴, 현재 위치·상태를 오프스크린 레코드로 접어 넣고 tick 재개. **정지했던 이동 NPC는 현재 위치에서 목적지로 이동 재개**(ETA 재계산).

> 정착지 LOD(추상 인구 ↔ 실체화)와 **별개 층**이다. 정착지 경제 인구는 여전히 추상 숫자(D3), NPC는 그 위에서 개별로 산다.

### 3.1 마을 안 표현 — 원정 출발·귀환, 부상 회복은 여관 (2026-10-25 결정·구현)

> 사용자 결정(2026-10-25): 마을 안에서 NPC가 멈춰 서 있지 않게 한다. 결과 판정·난수·치료 규칙·저장 형식은 바꾸지 않고 **보이는 모습만** 바꾼다. 게임 1시간 = 실제 60초다. [src: Assets/Scripts/Manager/TimeManager.cs]
> 같은 날 구현했다. 구현하면서 정한 세부와 검증은 아래 "구현 기록"에 있다.

**바뀌기 전 (2026-10-25 구현 이전)**

| 경우 | 모습 | 실제 시간 |
|---|---|---|
| 화면 안 NPC가 이동 경로가 있는 활동(Hunt·Delve·Relocate)을 시작 | 조우 대기 태그(`NpcActivityPausedTag`)가 붙어 **그 자리에 멈춰 선다**. 청크가 비활성화돼야 경로를 이어 간다 | 사냥 8분, 던전 12분 |
| 위 상태로 혼자 원정 갔다가 부상 | 태그를 떼지 않아 회복이 끝날 때까지 계속 서 있다 | 가벼운 부상 8~24분, 중상 24~72분 |
| 파티 원정 부상, 부상 중 새로 실체화 | 태그가 없어 기본 AI로 돌아다닌다 | — |
| 화면 밖 NPC가 원정을 마치고 활성 청크 안 귀환점에 도착 | 실체화되지 않는다. 실체화는 청크 활성화·신규 생성 때만 한다 | — |

[src: Assets/Scripts/Manager/NpcManager.cs `PauseMaterializedActivity`·`OnChunkActivated`·`UpdateOffscreenNpcPosition`, Assets/Scripts/Common/System/System_NpcSim.cs `StartNextActivity`·`ResolveHunt`·`ResolvePartyHunt`, Assets/Scripts/Manager/NpcManager_Delve.cs]

여관은 Hamlet 단계부터 생겼고, 가장 작은 마을(Settlement)과 시작 마을(BaseTown)에는 보장되지 않았다. 여관 창(`UIInn`)에는 휴식·세이브 스텁과 "무인 여관" 안내 칸뿐이었다. [src: Assets/Scripts/Village/VillageGrowthPlan.cs `CreatePlan`, Assets/Scripts/History/HistoryManager.cs, Assets/Scripts/UI/UIInn.cs]

**결정**

| # | 결정 | 근거 |
|---|------|------|
| **D13** | 원정 부상자(`Phase = Wounded`)는 치료 여부와 관계없이 **소속 마을 여관 안**에서 회복한다. 여관 안 NPC는 실체화하지 않는다 | 서 있는 부상자를 없앤다. 치료를 안 받은 쪽이 더 오래(24~72분) 서 있으므로 전부 들인다 |
| **D14** | 마을 안 원정 출발·귀환은 **걸어서** 보여 준다. D11의 "이동 중 정지"는 마을 밖 조우에만 적용한다 | 출발 때 멈추는 쪽이 더 어색하다. 출발만 고치면 돌아오는 NPC가 안 보여, 플레이어가 머무는 동안 마을이 빈다(사냥이 활동의 약 38% [src: Assets/Scripts/Npc/NpcActivityResolver.cs `HuntBaseScore` 주석]) |
| **D15** | 사라진 터를 뺀 **모든 마을에 여관**을 둔다. 새 게임과 기존 세이브 모두 | D13의 전제 |
| **D16** | 여관 상호작용 창에 그 마을 여관 안 NPC 목록을 보여 주고, 고르면 NPC 창에서 대화한다 | 부상 중에도 대화(부상 사연 포함)를 할 수 있게 유지 |

**D13 부상 회복 = 여관 안**

- "여관 안" 판정 = `Activity.Phase == Wounded` **그리고** 소속 마을에 여관(Inn 서비스 시설) 기록이 있음(배치 대기 포함). 새 저장 필드가 없으므로 세이브 버전은 올리지 않는다.
- 부상이 확정되는 순간(`ApplyExpeditionWound`):
  - 화면 밖 NPC는 그대로 "여관 안"이다. 위치는 바꾸지 않는다.
  - 화면 안 NPC(파티원, 마을 밖에서 조우 대기 중이던 NPC)는 여관 문까지 걸어가 문에 닿으면 강등(엔티티 제거)한다. 여관이 활성 영역 밖이면 화면 밖으로 벗어날 때 강등한다. 길이 막히거나 제한 시간을 넘기면 그 자리에서 강등한다.
- 청크가 활성화돼도 여관 안 NPC는 실체화하지 않는다(`OnChunkActivated`에서 건너뜀).
- 회복이 끝나면(`System_NpcSim`의 Wounded → Idle): 여관이 배치돼 있으면 위치를 여관 문 앞 빈 칸으로 옮기고, 그 청크가 활성이면 바로 실체화해 기본 AI로 움직인다. 여관이 아직 배치 전이면(마을 미실체화) 위치는 그대로 두고 다음 청크 활성화 때 평소처럼 나타난다.
- 소속 마을이 없거나 사라진 터 소속이면 지금처럼 밖에 둔다. 다만 멈춤 태그는 부상 확정 때 떼어 솔로·파티 모두 기본 AI로 움직이게 한다.
- 부상 판정·중상·치료비·회복 시간·알림은 그대로다(AMBITION D36·D91). 부상으로 동행 예약이 취소되는 규칙도 그대로다.

**D14 원정 출발·귀환을 걸어서**

- **출발**: 화면 안 NPC가 이동 경로가 있는 활동(`HasTravelPath` — Hunt·Delve·Relocate)을 시작하면 멈춤 태그 대신 "출발 걷기"를 시작한다. 원정 목표 방향으로 화면 밖 지점(활성 청크 안)을 골라 길찾기로 걷는다. 벽이 있는 마을은 문으로 나간다.
  - 플레이어 화면(카메라 영역 + 여유)에서 벗어나는 순간 강등한다. 강등할 때 지금처럼 `ResumeHuntPathFromCurrentPosition`이 현재 위치에서 경로와 도착 시각을 다시 계산한다.
  - 지점에 닿았는데 아직 화면 안이면(플레이어가 따라옴) 다음 지점을 다시 잡아 계속 걷는다.
  - 파티는 파티원이 각자 같은 방향으로 걷고 각자 강등한다. 강등 뒤 위치는 지금처럼 파티 경로로 계산한다.
  - 길이 막히거나(문이 닫힘 등) 제한 시간을 넘기면 그 자리에서 강등한다.
- **귀환**: 화면 밖 NPC의 원정이 성공·실패·도망으로 끝났을 때 귀환점이 활성 청크면, 귀환 경로 쪽 화면 바로 밖 빈 칸에서 실체화해 귀환점까지 걷고, 도착하면 기본 AI로 돌아간다. 그런 칸을 못 찾으면 지금처럼 다음 청크 활성화 때 나타난다. 부상자는 걷지 않고 여관 안으로 간다(D13).
- **이주 도착도 귀환과 같다** (2026-10-25 구현 중 사용자 결정): 화면 밖 NPC가 이주(`Relocate`) 목적지 마을에 도착했을 때 그 자리가 활성 청크면, 출발지 쪽 화면 바로 밖에서 실체화해 도착점까지 걷는다. 이주 출발은 원래 화면 안 NPC에서 시작하지 않으므로(AMBITION D140, `TryStartRelocation`) 출발 걷기는 실제로 원정(Hunt·Delve)에만 생긴다. 이동 중 실체화에서는 이주가 편도라 도착점 쪽에 더 가까우면 오는 구간으로 본다.
- **승격 위치가 마을 안일 때**: 청크 활성화로 이동 중 NPC를 실체화했는데 그 자리가 마을 영역 안이면, 정지(D11) 대신 가는 구간이면 출발 걷기, 돌아오는 구간이면 귀환점까지 걷기를 한다.
- 마을 밖에서 실체화된 이동 중 NPC는 지금처럼 그 자리에 멈춰 조우를 기다린다(D11, [NPC_COMBAT_DESIGN](NPC_COMBAT_DESIGN.md) §3.1 예외 포함).
- 결과 난수는 `OutcomeSeedGameTime` 기준이라 걷기·강등 시점의 영향을 받지 않는다. 도착 시각은 강등 때 다시 계산하는 지금 동작 그대로다.

**D15 모든 마을에 여관**

- 절차 마을: 성장 계획의 여관(153) 마일스톤을 Hamlet에서 **Settlement**로 내린다. 배치 슬롯은 이미 여관 자리(중심 기준 (3, −2))를 갖는다. [src: Assets/Scripts/Village/VillageGrowthPlan.cs `CreatePlan`, Docs/village/VILLAGE_REWORK_PLAN.md §3.3·§V4 레이아웃]
- 시작 마을(BaseTown, `SettlementId < 0`): 새 게임에서 상점 보장(`EnsureNewGameBaseTownShops`)과 같은 방식으로 여관을 보장한다.
- 기존 세이브: 로드할 때 사라진 터가 아닌 마을 중 여관 기록이 없는 마을에 완공·배치 대기(`PlacementPending`) 여관을 추가한다. "없으면 추가"라 다시 적용해도 결과가 같으므로 세이브 버전은 올리지 않는다. 배치는 다음 실체화 때 하고, 실패하면 기존 `PlacementPending` 재시도 규칙을 따른다.

**D16 여관 NPC 목록과 대화**

- `UIInn`의 안내 칸(`visitor-list`) 자리에 그 마을 소속 "여관 안" NPC를 회복이 빨리 끝나는 순으로 보여 준다. 한 줄 = 이름 · 남은 회복 시간. 없으면 "쉬고 있는 손님이 없습니다"를 보여 준다. 여관 세트(Bed + Hearth) 완성 여부와 관계없이 보여 준다.
- 목록의 각 줄은 버튼(`Unity.AppUI.UI.Button`)으로 만든다. `UIManager`가 가진 `ControllerUIFocus`는 열린 창에서 버튼·토글 같은 컨트롤과 정해진 클래스의 칸만 모아 패드 방향키로 고르고 확인 버튼으로 누르게 한다. 그래서 버튼으로 만들면 패드 조작이 따로 손대지 않아도 된다. 버튼이 아닌 요소로 만들면 `ControllerUIFocus.Collect`의 클래스 목록에 추가해야 한다. 키 이름을 화면에 쓸 때는 고정 문자열 대신 `AR.s.UI.Input.Display("<액션>")`을 쓴다. [src: Assets/Scripts/UI/ControllerUIFocus.cs `Collect`·`Activate`, Assets/Scripts/Common/System/System_VillageInteractionPrompt.cs (커밋 664d37b)]
- 고르면 `UINpcInspect`를 그 NPC로 연다. `UIManager.Show`가 기본 창이 아닌 UI Toolkit 창의 정렬값을 연 순서대로 올리므로(20 + 열린 창 수) 여관 창 위에 보인다. 대화·서사 대화는 저장 데이터만 읽으므로 실체화 없이 된다. [src: Assets/Scripts/Manager/UIManager.cs `Show`, Assets/Scripts/Npc/Narrative/ (ECS 컴포넌트 조회 없음), Assets/Scripts/UI/UINpcInspect.cs `RunDialogueAsync`]
- NPC 창 보완:
  - **결투 버튼**: 실체화되지 않은 NPC면 숨긴다. 신청한 자리에서 싸우는 규칙(NPC_COMBAT D20)상 몸이 필요하다. NPC_COMBAT D19의 예외가 된다.
  - **성격 칸**: 지금은 ECS `NpcStatComponent`가 있는 NPC만 표시한다. 없으면 `NpcSaveData.NpcStat`으로 표시한다. [src: Assets/Scripts/UI/UINpcInspect.cs `RebuildPersonality`]
  - **전투 능력치 칸**: 실체화된 NPC만 가진 값이라 비워 두고 "여관에서 쉬는 중"으로 안내한다.
  - 동행 요청은 지금처럼 "부상 중이라 동행할 수 없습니다"로 거절된다. 선물은 저장 데이터로 처리되므로 그대로 된다.
- 여관 휴식·세이브 스텁은 이번 범위가 아니다.

**예시**: 사냥 갔던 로렌이 부상을 입는다. 화면 밖이었다면 바로 여관 안에 있고, 플레이어가 여관에서 상호작용하면 "로렌 — 회복까지 6시간"이 보이며 골라서 "호되게 당했소…"를 듣는다. 치료를 받았으면 8분, 안 받았으면 24분 뒤 여관 문에서 나온다. 같은 마을의 다른 NPC가 사냥을 고르면 마을 밖으로 걸어 나가 화면에서 사라지고, 사냥이 끝나면 화면 가장자리에서 걸어 들어온다.

**구현 메모 (설계 때)**

- 걷기(여관 가기·출발·귀환)는 런타임 전용 ECS 컴포넌트와 시스템 하나가 이동을 소유한다. `System_NpcPickup`(이동 소유·제한 시간·길찾기 연속 실패 처리)을 본보기로 하고, `System_AI_Behavior`는 드랍 줍기처럼 이 컴포넌트가 있으면 건너뛴다. 저장하지 않는다 — 강등되면 사라지고, 다시 실체화될 때 D13·D14 규칙으로 다시 정한다.
- 강등은 지금 청크 단위(`OnChunkDeactivated` → `SaveAndDeactivateNpc`)뿐이므로 NPC 하나를 강등하는 진입점이 필요하다. 실체화는 `SpawnNpc`가 이미 개별이다.
- 여관 문 위치 = 여관 시설 엔티티 위치 근처 빈 칸(`CollisionUtil.TryFindNearestFree`).
- 함께 고친 다른 문서: [NPC_COMBAT_DESIGN](NPC_COMBAT_DESIGN.md) D19(여관 안 NPC 결투 불가), [VILLAGE_RUNTIME_DESIGN](../village/VILLAGE_RUNTIME_DESIGN.md) §4·[VILLAGE_REWORK_PLAN](../village/VILLAGE_REWORK_PLAN.md) §3.3(여관 단계). 이 문서의 D11·§3 전환·§11.1 "LOD와 관측"에도 표시했다.
- 진단·검증 범위는 구현 전에 정한다(AGENTS.md Validation). 여관 단계를 검사하는 유지 진단 — Village V3 Initial Facilities(Settlement 여관 0개 검사)와 V4 Layout(단계별 시설 배치) — 은 같은 변경에서 고친다.
- 코드 대조: 커밋 664d37b(설정 UI·컨트롤러 입력, 2026-10-25) 기준으로 위에서 참조한 NPC·여관·마을 시설 코드와 유지 진단 파일이 바뀌지 않았음을 확인했다. 그 커밋에서 새로 따라야 할 것은 D16의 컨트롤러 포커스·키 표시 규칙뿐이다.

**구현 기록 (2026-10-25, 커밋 대기)**

어디에 있나:

| 무엇 | 코드 |
|---|---|
| 걷기 상태 (출발·귀환·여관 가기, 저장 안 함) | `NpcVillageWalkComponent` — [src: Assets/Scripts/Common/Component/NpcVillageWalkComponent.cs] |
| 걷기 진행·강등 판정 | `System_NpcVillageWalk` Priority 76, 월드 전용 — [src: Assets/Scripts/Common/System/System_NpcVillageWalk.cs] |
| 출발·귀환·이동 중 실체화·결투 뒤 모습, 한 명 강등(`DemoteMaterializedNpc`) | [src: Assets/Scripts/Manager/NpcManager_VillageWalk.cs] |
| "여관 안" 판정, 여관 창 목록, 회복 뒤 여관 문에서 나오기 | [src: Assets/Scripts/Manager/NpcManager_InnRecovery.cs] |
| 여관 마을 판정·여관 문 위치·마을 영역 판정 (저장 기록 기준) | [src: Assets/Scripts/Village/VillageManager_Inn.cs] |
| 여관 최소 보장·로드 보정 | `VillageInitialDevelopment.EnsureMinimumInn`, `HistoryManager.EnsureVillageInns` — [src: Assets/Scripts/Village/VillageInitialDevelopment.cs, Assets/Scripts/History/HistoryManager.cs] |
| 화면 처리 호출 자리 | `System_NpcSim`(활동 시작·원정 종료·이주 도착·회복), `SpawnNpc`, 결투 시작·종료 — [src: Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager.cs, Assets/Scripts/Manager/NpcManager_Duel.cs] |

구현하면서 정한 세부:

| 항목 | 정한 내용 |
|---|---|
| 화면 밖 | 메인 카메라 직교 영역에 1.5칸 여유를 더한 사각형 밖 |
| 화면 밖 지점 | 목표 쪽으로 화면을 벗어나는 점에서 3칸 더 간 칸. 활성 청크 안 빈 칸이어야 한다. 막히면 한 칸씩 더 나가 보고(최대 6칸), 그래도 없으면 방향을 ±25°·±50° 돌린다 |
| 마을 영역 | 저장된 마을 경계(Bounds) 사각형. 경계가 아직 없으면 Stage 반경. 사라진 터를 포함한 모든 마을을 본다 |
| 여관 찾기 | ECS 시설 엔티티 대신 마을 저장 기록에서 Inn 서비스이면서 배치된(좌표가 있는) 기록 |
| 여관 문 | 여관 건물 가로 가운데의 바로 아래 칸. 막혔으면 활성 청크 안 가장 가까운 빈 칸 |
| 제한 | 구간(지점 하나)당 40초, 길찾기 연속 실패 3초. 다음 지점을 다시 잡으면 구간 시간을 새로 센다 |
| 실패 처리 | 출발·여관 가기는 그 자리에서 강등. 귀환은 화면 안에서 사라지지 않게 걷기만 끝내 그 자리에서 기본 AI. 활성 청크 밖으로 나간 귀환 NPC는 귀환점에 두고 강등 |
| 귀환점이 이미 화면 밖 | 걷지 않고 귀환점에 바로 실체화 |
| 걷기가 끝난 뒤 | 기본 AI 순찰은 실체화 자리(`SpawnPosition`) 주변을 돌므로 순찰 기준점을 도착 자리로 옮긴다. 결투로 끊긴 귀환은 귀환점을 기준점으로 둔다 |
| 여관 창 한 줄 | 버튼 "이름 — 회복까지 N시간"(올림, 1시간 미만은 "곧 회복"). 목록은 스크롤 칸. 창을 열 때마다 다시 만든다 |
| 여관 보장 시점 | `HistoryManager.Initialize`에서 매 부팅 — 새 게임은 시작 마을만 새로 생기고, 로드는 여관 없는 마을을 채운다 |

설계와 다르게 구현한 점과 그 이유:

- **부상 순간의 화면 처리는 `ApplyExpeditionWound` 안이 아니라 그 직후 `System_NpcSim`에서 한다.** 야망 시뮬 진단이 NPC 데이터를 복제해 `ApplyExpeditionWound`·`ResolveSoloDelve`·`CompleteRelocation`을 직접 부르는데, 복제본에도 저장된 `IsActive` 값이 들어와 그 안에서 ECS를 건드리면 실제 화면 NPC가 움직일 수 있다. 같은 틱이라 시점은 같다.
- **귀환 실체화는 그 NPC의 판정이 다 끝난 뒤에 판단한다.** `System_NpcSim`은 원정을 마친 같은 틱에 다음 활동을 바로 고르므로, 곧바로 새 원정을 골랐으면 실체화하지 않는다. 실체화 중(비동기)에 새 원정을 골랐으면 만든 엔티티를 버린다. 귀환 실체화는 엔티티만 화면 밖에 만들고 저장 위치·청크 색인은 귀환점 그대로 둔다 — 같은 틱에 고른 새 원정의 출발점이 화면 가장자리로 바뀌지 않게 한다.
- 이주 도착도 귀환과 같다(D14, 사용자 결정).

알려진 영향 (결과·난수는 그대로):

- 화면 안에서 출발한 원정은 강등 때 지금처럼 경로·도착 시각을 다시 계산하므로 길이가 달라진다. 사냥 목표가 출발지에서 8~16칸이라, 화면 밖까지 걸어 나간 만큼 줄거나 늘어 사냥 8시간이 약 4.5~9시간이 된다. 바뀌기 전에도 멈춰 서 있던 시간만큼 원정이 길어졌다. [src: Assets/Scripts/Npc/NpcActivityResolver.cs `GetHuntTargetPosition`, Assets/Scripts/Manager/NpcManager.cs `ResumeHuntPathFromCurrentPosition`]
- 부상자는 마을에서 보이지 않는다. 라이브 세이브(게임 시각 약 69시간)를 로드했을 때 생존 NPC 2,401명 중 470명(약 20%)이 부상 중이었고 모두 "여관 안"으로 판정됐다.

검증 (2026-10-25, 진단은 구현 전에 사용자가 고른 묶음):

| 검사 | 결과 |
|---|---|
| Unity 컴파일, `dotnet build --no-restore` Runtime·Editor | 오류 0. 바꾼 파일에서 새 경고 0 |
| Village V3 Initial Facilities | 27 통과 / 0 실패 — Settlement 여관 1개, 여관 최소 보장 6개 검사(새로 추가: 시작 마을 생성·반복·기존 여관 유지·구 원장 보정과 저장/복원·불일치 거부·테이블 누락) 포함 |
| Village V4 Layout | 9 통과 / 0 실패 — Settlement~City 단계마다 여관 1개 포함 슬롯 배치(Settlement 여관 (103,48)) |
| 라이브 세이브 로드 | 여관 보장으로 300개 마을 중 28곳에 여관 추가, 오류 0. 사라진 터 30곳 제외 전부 여관 마을. 시작 마을 여관은 로드 직후 배치됐고 나머지 268곳은 배치 대기 |
| Validate Village / Npc Save Snapshot | 로드 직후와 약 2게임시간 실행 뒤 두 번 모두 PASS (마을 300 누락 0, NPC 2,401 누락 0·샤드 32/32 일치). 그 사이 화면 밖에서 사냥을 시작한 시작 마을 NPC 1명이 바로 강등되고 경로가 다시 계산된 것을 확인했다 |

남은 확인: 실제 플레이 장면 — 걸어 나가기·걸어 들어오기·여관으로 걸어가기, 여관 창 목록·대화·결투 버튼 숨김, 패드로 목록 고르기. 플레이 모드 장면 확인은 이번 진단 묶음에서 뺐다.

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

- **모든 오프스크린 NPC를 꾸준히 순회**(라운드로빈): 목표 2,400명이어도 한 틱 처리 예산만큼 계속 돌린다. 전체 순회 주기는 인원에 비례하지만 **프레임당 비용은 고정 예산**이다.
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
// 개념 모델. 거주 이력은 후속이며 실제 저장 타입은 NpcSaveData를 따른다.
public NpcActivityState Activity;      // §4.1 상태머신 (enum + LocationRef + TargetKey + PhaseEndGameTime)
public float LastSimGameTime;          // 개인 catch-up 시계
// 전기 로그 (biography) — 상세 §5.1. 지식 거리(§8)의 입력.
public List<ResidenceEntry> Residences;   // {SettlementId, from, to(§5.1-⑤ 시간창)}
public List<DeedEntry> Deeds;             // 중요 사건 이력 — 반복 파티 호감도·절도 피해는 별도 저장
```

- 원정 진행 상태는 NPC_ACTIVITY `VentureState`를 **개인 단위로 흡수**하거나 참조(파티는 멤버 id 목록).
- 정본은 `NpcManager` + `WorldData`(엔티티 수명과 분리 — 관계/원정 선례). **ECS 풀 추가 없음.**
- 현재 저장에는 `NpcSaveData.PersonalStories`(최신 사연), `SharedActivityMemories`(떠난 동료의 공동 활동 기억), `NpcAffinityEntry.SharedActivities`(활성 NPC 쌍의 통계)가 있다. 필드 계약은 [NPC_AMBITION_DESIGN §12.1](NPC_AMBITION_DESIGN.md)을 따른다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs]

### 5.1 전기 로그 (biography) — 결정 (§9-8 해소)

NPC가 *어디 살았고 무엇을 겪었나*의 개인 기록. **지식 거리(§8·[NARRATIVE_ENGINE §4.8](../world_history/NARRATIVE_ENGINE_DESIGN.md))의 입력**이자 대사 회고의 근거.

- **무엇을 기록 (①)** — 두 granularity:
  - **정착지 매크로**: 관련 `ChronicleEvent`(War/Conquer/Ruin 등) → `RefEventId`로 **참조**.
  - **NPC 레벨 (열린 카탈로그)**: 실제 종류는 `NpcDeedKind`가 정본이다. 사망(`Fell`)·중상(`SevereWound`)·주요 아이템 획득(`NotableItemAcquired`)·던전 완료(`DungeonCleared`)·결투·발각된 도둑의 행적 등 중요 사건을 보존한다. `PartyAffinityChanged`·`Wounded`는 구 세이브 호환용이며 새 반복 기록을 만들지 않는다. 구출·배신·월드이벤트 등 추가 종류는 각 후속 기능에서 확정한다. 참가자는 EntityId와 표시명 스냅샷을 함께 남긴다. [src: Assets/Scripts/Npc/NpcBiography.cs, Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs]
  - 평범·무탈한 활동(그냥 사냥 성공)은 로그 안 함.
- **누가 기록 (③ — 단일 계산 출처)**: 리졸버가 결과를 1회 계산하고 해당 NPC의 중요 이력·요약을 갱신한다. 파티 공동 활동 통계는 생존 멤버 **쌍당 1회** 저장하며 양쪽 전기에 호감도 Deed를 복사하지 않는다. 정착지 매크로는 EventId 참조다. [src: Assets/Scripts/Common/System/System_NpcSim.cs, Assets/Scripts/Manager/NpcManager_Party.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs]
- **보존 (② — 2026-10-04 개정)**: 중요 `Deeds`와 사망 관계 기억은 계속 보존한다. 반복 파티 활동은 상대별 사냥·던전 원정·공동 클리어 횟수와 누적 호감도 변화로 집계한다. 절도 피해 3종은 합쳐 **최신 1건만** 저장한다. 대화는 부상·동료·아이템·던전 완료·절도 피해·사별 **6종의 최신 요약/참조**를 직접 읽는다. 아이템·던전 완료·사별의 전체 이력은 유지하므로 모든 역사 데이터에 상한을 둔 구조는 아니다. [src: Assets/Scripts/Npc/NpcSaveData.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs]
- **HowKnown (④)**: `Lived / Did / Witnessed / Heard` — 지식 거리 등급(§4.8 그대로). Heard(소문)는 전파 시스템 연결 후(§9-9/WORLD_EVENT §8).
- **거주 시간창 (⑤ — 나중)**: MVP는 `{SettlementId}`만(살았음→앎), `{from,to}` 시간창은 후속.
- **시딩 (⑥)**: 거주 = 출생 정착지 시드. deed = 거의 빈 상태로 시작 → 시뮬로 축적(관계 `OriginEventId` 몇 개는 씨앗).
- **영구 유대는 여기 아님**: 가족·친구·라이벌 등 지속 관계는 **관계 채널**(`NpcRelationEntry`)이 담당. 전기 deed는 **에피소드 사건**만(중복 금지).

### 5.2 사망자 최소 기록 — 결정 (2026-10-18, 미구현)

> 사용자 결정(2026-10-18): 죽은 NPC는 전체 저장 데이터 대신 **사망자 최소 기록**만 남긴다. §9-13(런타임 ID와 역사 신원 분리)의 첫 단계다. ID는 계속 예약하므로 ID 재활용은 이 단계에서 하지 않는다.

**지금**: 죽은 NPC도 살아 있을 때와 같은 `NpcSaveData`가 통째로 남는다. 저장은 `_npcSaveDict` 전체를 NPC 샤드에 쓰고, 죽은 NPC를 정리하는 코드는 없다. [src: Assets/Scripts/Manager/NpcManager_SaveSnapshot.cs `CaptureSaveSnapshotShards`]

| `MarkNpcDead`가 정리하는 것 | 그대로 남는 것 |
|---|---|
| 소문, 부탁 칸, 열린 소망(실패 처리), 호감도·관계 채널, 마을 명단 | 장비 12칸, 가방 아이템, 골드, 성격 8수치, 활동 상태, 위치, 행적 전체, 최신 사연 요약, 공동 활동 기억, 사망 관계 기억, 감정, 도난 단서, 부상 요약 |

골드는 규칙상 사망 시 소멸(AMBITION D46)이지만 값은 기록에 남아 있다. [src: Assets/Scripts/Manager/NpcManager.cs `MarkNpcDead`]

**기록 자체는 지우지 않는 이유**:
- **ID 예약**: 불러올 때 저장된 NPC 목록의 ID를 먼저 예약한다. 기록을 지우면 그 ID가 몬스터나 새 NPC에게 다시 발급될 수 있다. 생존자의 사망 관계 기억·공동 활동 기억, 행적 참가자, narrative 부탁 기록, 네임드 위협 기록이 그 ID를 가리키므로, 다시 발급되면 "죽은 부탁 주인이 살아 있다"고 판정하는 식의 오류가 생긴다. [src: Assets/Scripts/Manager/DataManager.cs `RegisterExistingEntity` 호출부, Assets/Scripts/Manager/NpcManager_Narrative.cs `IsNpcAlive`]
- **이름 조회**: 이름 스냅샷이 없는 옛 기록은 그 ID로 저장 데이터를 찾아 이름을 읽는다. [src: Assets/Scripts/Npc/Narrative/NpcRumorPhrasing.cs `NpcName`]

**바뀐 뒤**: `MarkNpcDead`가 생존자 기억(사망 관계 기억·공동 활동 기억)과 사망 Deed를 남긴 **뒤**, 죽은 NPC의 기록을 아래만 남기고 줄인다.

| 남김 | 이유 |
|---|---|
| EntityId (샤드 키) | ID 예약 |
| 이름, NPC 테이블 번호 | 이름·성별 조회 |
| 소속 마을 | 어느 마을 사람이었는지 |
| 상태 = 사망 (`Condition`, `Activity.Phase`) | 모든 반복문이 이미 이것으로 건너뛴다 |
| 사망 Deed (`Fell`: 시각, 연대기 EventId, 던전) | 단기 목표의 원수 갚기가 읽는다([NPC_OBJECTIVE_DESIGN](NPC_OBJECTIVE_DESIGN.md) §6.1). 던전 필드는 그 문서 §9.1이 더한다 |

지우는 것: 장비, 가방, 골드, 성격, 활동 상태의 나머지, 위치, 설명, 사망 Deed 외 행적(아래 열린 질문), 최신 사연 요약, 공동 활동 기억, 사망 관계 기억, 감정, 도난 단서, 부상 요약, 전투력 기준값, 구매·절도·터 체류 시각.

**예시**

```
철수 (ID 1234)
  이름: 철수 / NPC 테이블 번호: 12 / 소속 마을: 동쪽 마을
  상태: 사망
  사망 Deed: 3일, 동쪽 던전
```

**크기**: [추론] NPC 1명은 평균 약 1.2KB(57게임시간 세이브 2,400명 2.85MB 기준)이고 가방에 전리품이 있으면 더 크다. 최소 기록은 100바이트 안팎이다. 사망은 30일에 10~17명(GOLD_BALANCE §11.2)이라 게임 시간 1년이면 120~200명이 쌓인다.

**구현 순서**: 단기 목표 1단계([NPC_OBJECTIVE_DESIGN](NPC_OBJECTIVE_DESIGN.md) §12)를 먼저 끝낸 뒤 별도 작업으로 한다(2026-10-18). 두 작업 모두 `MarkNpcDead`를 건드린다.

**기존 세이브**: 불러올 때 죽은 NPC를 최소 기록으로 줄이는 로드 보정을 둔다. 그 뒤 저장부터 작아진다. 남기는 필드는 지금 형식 그대로라 세이브 버전은 올리지 않는다.

**구현 전 확인** (읽기 전용):
1. 죽은 NPC의 저장 데이터를 읽는 곳 전수 확인(진단 코드 포함). 지금까지 찾은 곳은 이름 조회와 생존 판정뿐이다.
2. 닫힌 경제 검산이 죽은 NPC의 골드를 세는지. 센다면 사망 때 소멸한 몫으로 따로 집계해야 한다.
3. 불러오기 경로가 죽은 NPC를 위치로 청크 색인에 넣는지. 넣는다면 위치를 지우기 전에 건너뛰게 한다.
4. 진단이 사망 정리를 복제하고 있으면 같은 공용 메서드를 부르게 한다(AMBITION P4 진단 복제 함정).

**열린 질문**: 사망 Deed 말고 **나머지 행적**도 남길까? 지금 죽은 NPC의 행적을 읽는 곳은 찾지 못했다. 죽은 NPC는 검사 창을 열 수 없고, 생존자는 자기 기억에 이름을 따로 갖고 있다. 다만 `MarkNpcDead` 주석에 "전기·연대기 회고를 위해 보존"이라는 의도가 있다. 기본안은 **지움**이고, 회고 대사(미구현)를 만들 때 필요한 것만 다시 정한다.

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
| 1 | ~~스케일 예산~~ → **구현(D9)**: `max(작은 맵 100/대형 맵 1,200, 마을 수×8)` + 마을당 최소 5명 + 연대기 인구 가중 배분 + 6 게임시간 단위 총량 보충 | 300개 마을 기준 목표 2,400명. 시간분할 K와 실제 마을 체감 밀도는 플레이하며 튜닝 |
| 2 | ~~catch-up 세밀도~~ → **해소**: 원정=단일 구간(중간 시뮬 없음, §4.3) + 모든 NPC 꾸준한 라운드로빈(§4.2)이라 평상시 catch-up 없음. **시간 점프 시에만** 그 사이 완료된 원정을 결제(각 1회 롤), 많으면 상한 캡 | 세밀도 딜레마 없음 (D10) |
| 3 | ~~오프스크린 이동 표현~~ → **결정(D11)**: 연속 좌표(턴마다 lerp) + 청크 인덱스. 조우 시 정지·상호작용, 시야 밖 재이동. 오픈월드 기본 조우 코어 편입 | 경로 표현(직선 vs coarse 웨이포인트)·근접 체크 세밀도는 tunable |
| 4 | ~~발진 빈도·최소 상주~~ → **보류**: 선택 로직(D12)의 **창발 결과**라 따로 정하지 않음. 마을 공동화가 실제로 문제되면 그때 최소 상주 가드 추가(NPC_ACTIVITY §3.3) | 실측 후 |
| 5 | ~~보충 여부~~ → **구현: 보충함**(총량 목표 유지, D9). 현재는 기본 NPC 로스터에서 6 게임시간마다 1명씩 보충하고, 정착지 인구 비율 대비 부족한 정착지에 배정한다. | 장기 소스(출생/외부 유입)와 지역 위험도 적합성은 후속 결정. 보충은 물리 모험가 수만 다루며 [마을–NPC 경계](VILLAGE_NPC_BOUNDARY_DESIGN.md)의 경제 인구는 변경하지 않는다. |
| 6 | ~~승격 좌표~~ → **결정(기본값)**: 정착지에 있으면 **반경 내 랜덤**(현행 `GetRandomSpawnPositionInVillage`), 이동 중이면 **analytic 좌표 그 지점**(D11) | — |
| 7 | ~~결정론~~ → **결정(기본값)**: 모든 롤은 **`DerivedSeed` 계열 시드(NPC id + 출발 시각)**, `UnityEngine.Random` 금지(프로젝트 규약). 세이브/로드·리플레이 일관 | — |
| 8 | ~~전기 로그 스키마~~ → **결정(§5.1, 2026-10-04 개정)**: 중요 사건 이력 + 공동 활동 통계 + 종류별 최신 사연. 영구 유대는 관계 채널 | 반복 파티 Deed는 통계로 이관, 절도 피해는 최신 1건. 중요 이력·사망 기억은 보존 |
| 9 | **노변 조우 이벤트 내용** → **범위 밖(보류)**: 이동 NPC 대화가 낳는 이벤트 카탈로그·발화는 **WORLD_EVENT/narrative 소관**, 본 문서 밖. D11이 진입점만 열어둠 | 나중 |
| 10 | ~~사망 관계 기억~~ → **구현 완료**: 관계 그래프에서는 사망 NPC를 제거하되, 생존 NPC에는 `DeceasedRelationMemory`(사망 NPC id·표시명 스냅샷·사망 전 관계 유형·방향·`Fell` EventId·사망 시각)를 남긴다 | `MarkNpcDead`가 `Fell` 연대기 이벤트를 먼저 만든 뒤 활성 관계 제거 전에 생존 NPC에 기억을 기록한다. Inspect 관계 목록은 “고인 배우자 ○○”처럼 표시한다. 호감도/활성 관계 엔트리는 제거해 현재 관계 판정에는 참여하지 않음 |
| 11 | **보충 NPC의 지역 난이도 적합성** → 전역 총량 보충 시 단순 정착지 부족 비율만으로 배정하지 않는다. 신규/저레벨 모험가의 시작 레벨·장비·전투력과 정착지 주변 Zone 위험도를 비교해 안전한 정착지에 우선 배치한다 | 이후 구현. 고난이도 지역에는 해당 위험도에 맞는 숙련 모험가만 보충하거나, 저레벨 NPC는 저위험 지역에서 성장·이동한 뒤 진입하게 한다. 지역 위험도·NPC 성장·이주/호위 규칙이 함께 필요 |

| 12 | **오프스크린 Hunt 결과 알림의 플레이어 친분도 게이트** — 화면에 있는 NPC의 Hunt 성공/실패/부상/사망 알림은 그대로 표시한다. 화면 밖 NPC는 플레이어와 `Acquaintance` 이상일 때만 부상·사망을 알린다. | 선행 구현 필요: 현재 `AffinityTier`는 대화 스파이크용 enum이며, 플레이어↔NPC 친분도 저장·증감(`ModifyPlayerRelation`)은 미구현이다. 관계 데이터가 구현될 때 Hunt 알림 조건에 연결한다. 그 전에는 현행처럼 모든 오프스크린 부상·사망을 알린다. |
| 13 | **NPC 런타임 ID와 역사 신원 분리** — 장기적으로 `EntityId`는 ECS/현재 생존 NPC에만 쓰고, `NpcHistoryId`는 전기·연대기·고인 관계 기억을 위한 영구 신원으로 분리한다. 사망 후에는 최소 역사 레코드만 보존하고 `NpcSaveData`와 런타임 ID는 정리·재활용할 수 있게 한다. | 현재는 `EntityId`가 영구 신원을 겸하며 사망 NPC `NpcSaveData`도 보존한다. **첫 단계(2026-10-18 결정, 미구현)**: ID는 그대로 예약한 채 사망 NPC 기록을 **사망자 최소 기록**으로 줄인다(§5.2) — 세이브 크기 문제는 대부분 이것으로 해결된다. ID 분리·재활용은 예약 ID 증가가 실질적인 문제가 될 때 구현한다. 이행 시 `DeceasedRelationMemory`는 `DeceasedNpcHistoryId`를 참조하고, 살아 있는 NPC 간 실시간 관계·ECS는 계속 `EntityId`를 사용한다. |

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

| Phase | 내용 | 규모 | 상태 |
|-------|------|------|------|
| **NS1** | 오프스크린 상태 모델 + `System_NpcSim` 시간분할 틱 (솔로) | M | **완료** — NS1 당시 Rest/Hunt만 구현. 이후 Trade(2026-09-28)·DungeonDelve(2026-09-29) 추가(§11.1) |
| **NS2** | 결과 되먹임 배선 (죽음·전리품·연대기 append) + 전기 로그 `Deeds` | M | **완료** |
| **NS3** | LOD 승격/강등 (오프스크린 ↔ ECS) + 좌표 실체화 | M | **완료** |
| **NS4** | 이주(거주 이력) + 이벤트 유발(WORLD_EVENT) + 파티 | L | 파티: Hunt·DungeonDelve 파티와 파티 결과 이력 구현, 원정 파티 자동 구성 개정(AMBITION D85~D90, 2026-09-29). 이주: **강제 이주만 구현**(2026-10-03, AMBITION D138~D141 — 사라진 터 소속 NPC가 72h 뒤 가장 가까운 상점 마을로 `Relocate`). 자발적 이주·거주 이력 Deed·WORLD_EVENT 유발은 미착수 |
| **NS5** | 스케일 튜닝(밀도·틱 예산·시간 점프 검증) + 결정론 검증 | M | 부분 완료 — 전역 총량/보충 구현, 실측·튜닝 대기 |

> **선행 검증**: NS1 전, 스케일 예산(§9-1)을 잡아 "전부 개별 tick"이 목표 규모에서 프레임 비용 상수인지 프로토타입으로 확인 권장.

### 11.1 현재 구현 상태 (2026-10-04 갱신)

#### MVP 활동과 결과

- `System_NpcSim`은 `Priority = 59`, 0.25초 간격, 한 번에 최대 12개(NPC와 파티를 합친 통합 목록)를 라운드로빈 처리한다. 시간 점프 시 NPC당 phase 전환은 최대 4회로 제한한다. 목록에 NPC·파티가 추가·삭제되면 순회 커서는 0으로 돌아가지 않고 위치만 보정한다(2026-09-28 수정 — 이전에는 파티가 생길 때마다 커서가 0으로 돌아가 앞쪽 NPC만 활동했다).
- 구현된 활동은 **Rest(4 게임시간, 무료)**, **Hunt(8 게임시간, 솔로 또는 파티)**, **Trade(1 게임시간, 마을 상점)**, **DungeonDelve(12 게임시간, 솔로 또는 파티 — 아래 별도 항목)** 다. 다음 활동은 성향·야망·필요 기반 효용 선택으로 결정한다([NPC_AMBITION_DESIGN.md](NPC_AMBITION_DESIGN.md) §6, D39·D40).
- Hunt 결과는 `Success / Failed / Wounded / Dead`다. 전투력은 장비 반영 `NpcPower`로 판정한다(AMBITION §5.1). 성공하면 마을 Food를 1~4만큼 추가하고 개인 전리품을 1회 롤한다(AMBITION §7.1, 골드는 주지 않음). 전리품은 장착하지 않고 Trade에서 판다 — NPC 장비는 상점 기본 아이템이고 구매로만 바뀐다(2026-10-03 [NPC_SHOP_EQUIPMENT](NPC_SHOP_EQUIPMENT_DESIGN.md) D118·D119). 사망 확률은 30게임일 기준 0.5~1% 수준으로 매우 낮다(AMBITION D48). 부상은 24 게임시간 회복 후 다시 활동 가능해지며, Gold가 있으면 추상 치료(10G, 소속 마을 Gold로)로 8 게임시간으로 줄일 수 있다(AMBITION §9, D36). 부상은 누적 횟수·마지막 부상으로 요약하고 중상만 `Deeds`에 기록하며(D91~D93), 파티 Hunt 결과는 생존 멤버 쌍의 호감도와 공동 활동 통계를 갱신한다(§5.1, AMBITION §12.1). 사망은 시뮬레이션 목록·활성 관계·호감도·마을 명단에서 제거되며 `Fell` 연대기와 `Deeds`를 남긴다.
- **DungeonDelve(12 게임시간, 솔로 또는 파티, 2026-09-29 P2a)**: 소속 마을 던전 슬롯 하나로 추상 원정을 간다. 보상은 개인 전리품뿐이고(골드·Food 없음) 던전 내부 저장 상태를 바꾸지 않는다. 성공하면 드물게 NPC가 던전을 완료해 슬롯 세대를 넘긴다(보호 규칙 포함). 규칙은 [NPC_AMBITION_DESIGN.md](NPC_AMBITION_DESIGN.md) §7.1, D55·D63~D68.
- **소문(2026-09-29 P2b)**: NPC마다 사실만 담은 소문(`NpcSaveData.Rumors`, 최대 8)을 가지며, Rest가 끝날 때 30~50% 확률로 같은 마을 대기 NPC 1~2명과 교환한다. 유망한 소문은 그 던전으로의 확인 원정(Delve ×1.5)을 만든다. 규칙은 AMBITION §5.2·§7.4, D70~D79.
- 활동 선택·Hunt 결과·Food 보상·목표 좌표·개인 전리품·치료 판정은 `DerivedSeed.ChannelNpcActivity` 기반으로 결정된다(salt 0 선택, 1 결과, 2 보상, 3 목표, 4 솔로 전리품, 5 솔로 치료, 10+i 파티원 결과, 20 파티 보상, 30+i 파티원 전리품, 40+i 파티원 치료). Hunt는 출발지→목표지→귀환지의 analytic 좌표를 사용한다.
  - DungeonDelve(AMBITION D69): 50 솔로 결과, 51 솔로 전리품, 53 솔로 완료 판정, 54 솔로 완료 보상, 55 솔로 치료, 56 대상 선택, 60+i 파티원 결과, 70+i 파티원 전리품, 90+i 파티원 치료, 100 파티 완료 판정, 110+i 파티원 완료 보상. 입구 없는 원정 목적지는 3을 재사용한다.
  - 소문 교환(AMBITION D77, 기준 = Rest 중이던 NPC·Rest 시작 시각): 120 교환 발생·두 번째 상대, 121 상대 고르기, 122+i·124+i 수용 판정.
  - 파티 모집(AMBITION D88, 기준 = 리더 EntityId·리더 결정 시각): 130 모집 시도, 131 수락 난수열. 132~139는 파티 모집 예비.
  - 중상 판정(AMBITION D94): 140 솔로 Hunt, 141 솔로 Delve, 150+i 파티원 Hunt, 160+i 파티원 Delve. 142~149는 부상 예비.
  - NPC 간 절도(AMBITION D100, 기준 = 도둑 EntityId·도둑의 Rest 시작 시각): 170 시도 난수열(시도 확률 → 피해자 → 물건 순), 171 발각. 172~179는 절도 예비.
  - 생활비 소멸(GOLD_BALANCE D132 B-주, 2026-10-03, 기준 = NPC EntityId·Rest 시작 시각): 180 소멸액 소수 부분의 확률 반올림 1회(소수가 0이면 굴리지 않음). 181~189는 골드 흐름 예비.
  - 이주(AMBITION D140, 2026-10-03, 기준 = NPC EntityId·이주 시작 시각): 190 목적지 마을 안 도착 좌표(각도 → 거리). 191~199는 이주 예비.
  - 단기 목표([NPC_OBJECTIVE_DESIGN](NPC_OBJECTIVE_DESIGN.md) §10, 2026-10-18, 기준 = 판정하는 NPC EntityId·계기 사건 시각): 200 극적 목표 생성 판정(사건 시점 판정과 다시 계산이 같은 결과를 낸다). 201~209는 단기 목표 예비. 일상 목표는 RNG를 쓰지 않는다.
  - 네임드 위협 마주침([NAMED_THREAT_DESIGN](../world_history/NAMED_THREAT_DESIGN.md) §12.3, 2026-10-18, 기준 = 솔로 NPC EntityId 또는 PartyId·사냥 결과 시드 시각): 210 솔로 마주침, 211 솔로 결과(부상/도망), 212 파티 마주침(파티당 1회), 213 솔로 중상(30%), 220+i 파티원 결과, 230+i 파티원 중상. 214~219·224~229·234~239는 네임드 예비. 마주치면 일반 사냥 결과 판정(salt 1·10+i)을 굴리지 않는다.
  - **비워 둔 번호(재사용 금지)**: 52·80+i(제거한 희귀 보너스 롤), 126~129(소문 예비), 132~139(파티 예비), 142~149(부상 예비), 172~179(절도 예비), 181~189(골드 흐름 예비), 191~199(이주 예비), 201~209(단기 목표 예비), 214~219·224~229·234~239(네임드 예비). 새 salt는 240번 이후에서 쓴다.
  - `ChannelNpcActivity` salt가 아닌 별도 채널: `ChannelNpcAmbition`(entityId 해시 — 개인 편차·시작 Gold, D37·D38), `ChannelNpcBasicEquipment`("NPCBSEQP", 2026-10-03 — entityId ^ 장비 슬롯 해시로 시작 장비·일괄 교체의 기본 아이템 변형 선택, [NPC_SHOP_EQUIPMENT](NPC_SHOP_EQUIPMENT_DESIGN.md) D118·D121). 둘 다 worldSeed와 무관하다. 네임드 위협 감독의 선택(템플릿·칭호·영역·소문 주인·먼 소문·배회 지점)은 `ChannelNamedThreat`("NAMEDTHR", worldSeed + 네임드 일련번호 + 용도)를 쓴다.
- **부상 등급(2026-09-30 AMBITION D91~D96)**: 원정 부상의 약 10%는 중상(회복 72h, 치료 시 24h)이다. 가벼운 부상은 행적(Deed)을 남기지 않고 누적 횟수·마지막 부상만 갱신하며, 중상은 `Kind`·`When`만 담은 Deed를 남긴다.
- **NPC 간 절도(2026-10-02 AMBITION P4, D98~D115)**: Rest가 끝날 때(소문 교환 다음) 기회 야망 NPC가 같은 마을 대기 NPC의 가방 아이템을 훔칠 수 있다(도둑 168h·마을 72h 쿨다운). 들키면 물건은 주인에게 남고 관계가 틀어지며(호감도 −40·원한), 안 들키면 물건이 도둑 가방으로 옮겨지고(장착하지 않음, 2026-10-03 NPC_SHOP_EQUIPMENT D119) 피해자에게 단서가 남아 나중에 판매 소문으로 늦게 발각될 수 있다. Rest 종료 처리는 게임·파티 합류 경로·진단이 같은 공용 진입점(`NpcManager.ResolveRestEnd`)을 부른다(D107). 피해자 사연은 최신 1건으로 교체하고 도둑의 발각 이력은 보존한다(2026-10-04, AMBITION §12.1).
- **생활비(2026-10-03 GOLD_BALANCE D131·D132)**: 같은 진입점에서 절도 판정 다음에, 예비금(치료비 × 3)을 넘은 Gold의 70%를 소속 마을에 낸다(1.8% 소멸, salt 180). 파티 합류 경로는 내지 않는다.
- **강제 이주(2026-10-03 AMBITION D138~D141)**: 사라진 터(폐허·합병) 소속 NPC는 72h 체류 뒤 다음 활동 선택 때 `Relocate`(편도 Venturing, 12칸/h)로 가장 가까운 상점 마을에 가서 소속을 옮긴다. 시작은 `NpcManager.TryStartRelocation`, 도착은 `CompleteRelocation` — 게임과 진단이 같은 메서드를 부른다. 위치 갱신·화면 안 일시정지·경로 재계산은 원정과 같은 analytic 경로(`NpcActivityResolver.HasTravelPath`)를 쓴다. 최초 배분·전역 보충은 터를 후보에서 뺀다.
- **원정 파티 자동 구성(2026-09-29 AMBITION D85~D90)**: Hunt·Delve 리더가 모집 시도 확률을 통과하면 같은 마을 대기·**Rest 중** NPC에게 수락 확률을 굴려 최대 4인 파티를 만든다. 이전에는 후보가 "활동 없음(`Kind == None`)"만 인정돼 활동이 끝나면 바로 다음 활동을 시작하는 구조상 후보가 거의 없었다(파티 비율 4.5% → 43%).

#### 개인 기억과 대화 (2026-10-04)

- 사건 기록 시 최신 사연 요약/참조를 갱신하고, 로드 시 구 기록을 이관하며 참조를 재구성한다. 대화할 때 `Deeds`·사망 기억·전체 호감도 그래프를 다시 검색하지 않는다. 중요한 전체 이력과 대화에 쓸 최신 기록은 같은 원본을 참조한다. [src: Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs, Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs]
- 원정 실패·부상이어도 살아 돌아온 쌍은 사냥/던전 원정 횟수에 포함한다. 공동 클리어는 실제 던전 완료와 두 멤버의 성공을 모두 만족할 때만 센다. 호감도 누적 변화와 클리어 횟수는 별개의 값이다. [src: Assets/Scripts/Manager/NpcManager_Party.cs]
- 저장 필드·이관·검증은 [NPC_AMBITION_DESIGN §12.1–12.2](NPC_AMBITION_DESIGN.md), 최신 사연 후보·캐시 정책은 [NARRATIVE_DIALOGUE_VARIETY_DESIGN §5](../world_history/NARRATIVE_DIALOGUE_VARIETY_DESIGN.md)를 따른다.

#### LOD와 관측

- 마을 밖에서 실체화된 이동 중 NPC는 기본 AI 이동을 일시 정지하고, 오프스크린 전환 시 현재 좌표에서 목적지까지의 경로와 ETA를 다시 계산해 이어서 이동한다. 마을 안에서는 걸어서 출발·귀환하고, 부상자는 여관 안에서 회복한다(§3.1, 2026-10-25 구현).
- 활성 NPC의 Hunt 결과는 모두 단일 토스트 알림으로 표시한다. 오프스크린 NPC는 토스트 덮어쓰기를 피하기 위해 현재 부상·사망만 알린다. 플레이어-NPC 친분도 게이트는 §9-12의 후속 작업이다.
- `UINpcInspect`는 현재 활동, 남은 시간, 마지막 Hunt 결과와 성공 시 Food 획득량을 표시한다. 행적 목록은 보존된 중요 `Deeds`와 최신 절도 피해 1건을 최신순으로 표시한다. 관계 목록은 현재 호감도와 상대별 공동 활동 통계, 떠난 동료의 마지막 호감도·활동 기억, “고인 배우자 ○○” 같은 사망 관계 기억을 표시한다. 중상·사망·기본 장비 구매 업그레이드·던전 완료·결투·도둑의 발각 행적은 계속 보존하며, `AttackedWound`는 생성 경로가 P3·P5의 후속 작업이다. [src: Assets/Scripts/UI/UINpcInspect.cs, Assets/Scripts/Npc/NpcSaveData_PersonalStories.cs, Assets/Scripts/Manager/NpcManager_SharedActivities.cs]

#### 전역 총량과 저장

- `NpcPopulationState`가 전역 목표 수·다음 보충 시각·정착지 순회 커서를 저장한다. 최초 배분은 정착지 연대기 인구 비율을 사용한다.
- 현재 목표 수는 `max(맵 규모 최소값, 마을 수×8)`이다. 작은 맵 최소값은 100명, 대형 맵은 1,200명이며 마을당 최소 5명을 먼저 보장한다. 300개 마을에서는 2,400명이 목표다. 생존 수가 목표보다 낮으면 6 게임시간마다 한 명씩 목표 배분 대비 부족한 정착지에 기본 NPC 로스터로 보충한다.
- **인구 비례 배분은 모든 마을이 스폰 후보일 때만 의미가 있다.** 마을은 `VillageId`에 매핑된 `VillageTable` 행(+`DefaultNpcIds`)이 있어야 후보가 된다. 마을 인스턴스 수 > `VillageTable` 행 수라, `VillageManager`가 각 마을의 선호 tableId(등록 인덱스+1)를 존재하는 행으로 라우팅한다 — `DataManager.ResolveVillageTableId`(없으면 가장 작은 Id로 폴백), 세이브 로드 경로도 동일. 이 라우팅이 없으면 선호 tableId가 유일 행(현재 Id=1)과 맞는 시작 마을 **하나만** 후보가 되어, 전역 목표 수 전체가 시작 지점에 쏠린다(초기 대량 스폰 버그). [src: Assets/Scripts/Village/VillageManager.cs, Assets/Scripts/Data/DataManager_Table.cs]
- 현재 `WorldDataVersion`은 19다. 레거시 `WorldData`는 버전 마이그레이션 뒤 도메인 파티션과 NPC 32샤드로 변환하며, 기존 파티션 세이브의 기억 이관은 다음 저장을 전체 범위로 승격한다. `NpcState`와 `NpcSocial`은 부분 저장에서도 함께 커밋한다. 파티션 구조·원자 커밋·복구 계약은 [SAVE_PARTITION_DESIGN](../tech/SAVE_PARTITION_DESIGN.md)이 정본이다. 기존 세이브의 null 컬렉션은 로드 시 보정한다.

---

## 12. 한 줄 요약

> **"규모를 줄인 모험가들이 각자 경량 상태머신으로 살아가며(근처=풀 ECS/멀리=시간분할 tick), 스스로 원정을 떠나 싸우고·죽고·무언가를 깨우고, 그 행적이 연대기와 개인 전기에 진짜로 남는다."**

---

**Status**: MVP 구현 완료 — Rest·Hunt(솔로·파티)·Trade·DungeonDelve(솔로·파티), Rest 종료 소문 교환·절도, 개인 LOD·결과 반영·전역 보충·Inspect 표시까지 구현. 잔여 = NS4(자발적 이주·거주 이력·WORLD_EVENT), NS5 실측/튜닝, §9 후속 항목.
**Last Updated**: 2026-10-25
**Dependencies**: NPC_ACTIVITY(활동 부품), PROCEDURAL_HISTORY(LOD·틱 패턴·정착지 밀도), NPC_RELATION_HISTORY(전기 채널·backfill), NARRATIVE_ENGINE §4.8(전기·지식거리·일관성), WORLD_EVENT(이벤트 트리거)
