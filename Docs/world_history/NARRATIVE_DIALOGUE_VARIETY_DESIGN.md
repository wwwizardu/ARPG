# 내러티브 대화 다양성 — 토픽 확장 설계 (Dialogue Variety)

> **목적**: 현재 대화 토픽 카탈로그(12종)가 "금방 질린다"는 문제를 진단하고, **이미 게임에 존재하는 백본 데이터**에 접지해 다양성을 늘리는 확장안을 모은다. 환각을 피하는 C-method(시스템이 사실을 건네고 LLM은 말투만 입힘) 원칙을 유지한다.
>
> **상태 (2026-07-15)**: 🟨 **Phase 1 구현 완료 (플레이 검증 전)**. §5 Phase 1(사연 토픽 4종 + 캐시 variant·무효화) 코드 반영 — 구현 중 접지 조정 3건은 §5 Phase 1 하단 참조. Phase 2 이후 착수는 플레이 검증 결과를 보고 결정한다.
>
> **문서 성격 — 파생/제안 문서**: 본 문서는 *생성 엔진 정본*이 아니다. 대화 생성의 정본은 [NARRATIVE_ENGINE_DESIGN.md](NARRATIVE_ENGINE_DESIGN.md), 검증 범위는 [NARRATIVE_ENGINE_SPIKE.md](NARRATIVE_ENGINE_SPIKE.md), 소망 데이터 모델은 [QUEST_SYSTEM_DESIGN.md](../npc/QUEST_SYSTEM_DESIGN.md), 대화 소비처는 [interactionSystem.md §4](../npc/interactionSystem.md)가 담당한다. **본 문서는 확정된 아이디어를 이 정본들에 이관한 뒤, `Docs/_archive/`로 이동하거나 정본을 가리키는 stub으로 남긴다** (통합 워크플로는 §7).
>
> **관련 문서**:
> - [NARRATIVE_ENGINE_DESIGN.md](NARRATIVE_ENGINE_DESIGN.md) — 생성 엔진 정본(§4 파이프라인·§4.8 교차 일관성·§8 미결정)
> - [NARRATIVE_ENGINE_SPIKE.md](NARRATIVE_ENGINE_SPIKE.md) — 현행 토픽 카탈로그(§4.5)·후속 항목(§8)
> - [QUEST_SYSTEM_DESIGN.md](../npc/QUEST_SYSTEM_DESIGN.md) — 소망(Wish) 데이터 모델·충족 루프
> - [interactionSystem.md](../npc/interactionSystem.md) — 관계 4축·대화 점진 공개

---

## 1. 문제 진단 — 질림의 두 원인

토픽 "수"만의 문제가 아니다. 실측 전이지만 코드상 두 구조가 반복을 강제한다.

| # | 원인 | 근거 |
|---|------|------|
| 1 | **캐시가 대사를 얼림** | 같은 `(npcId, tier, topicType)` 조합은 한 번 생성 후 글자 그대로 재생 [src: Assets/Scripts/Npc/Narrative/NpcNarrativeService.cs:33-35] |
| 2 | **레퍼토리가 작고 정적 조건 위주** | NPC별 화제 1~3개(npcId 시드 고정), 대부분 "마을에 X 부족" 류 정적 조건 [src: Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs:96-125] |

**체감**: 레퍼토리 N개짜리 NPC를 같은 티어에서 계속 클릭하면 새 대사 N개 → 이후 캐시된 동일 대사 반복. 새 내용은 **티어 상승** 또는 **마을 상태 변화(식량난·자원부족)** 시에만. 단 마을 위협(`ThreatLevel`)은 정적 테이블값이라 플레이 중 안 변한다 [src: Assets/Scripts/Common/Component/VillageComponent.cs:17].

> 따라서 다양성 개선은 **① 새 토픽 소스**뿐 아니라 **② 반복을 죽이는 구조**를 함께 다뤄야 한다.

---

## 2. 현행 토픽 카탈로그 (기준선, 12종)

[src: Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs]

- **니즈(Need)**: `ClearThreat`⚡ / `Food`⚡ / `ResourceShortage` / `CraftMaterial` / `Medicine`
- **소문(Rumor)**: `HistoryRumor` / `SettlementRumor` / `DangerRumor` / `Gossip`
- **잡담(Flavor)**: `SmallTalk` / `JobMood`
- **중립(Neutral)**: `Greeting`(폴백)

한계: 관계는 Rival/Friend만 화제화(`Gossip`), 개인 사연·플레이어 반응·동적 사건 화제 없음.

---

## 3. 확장 아이디어

각 아이디어는 **접지 데이터(이미 존재)** · **비용** · **환각 위험** · **기존 설계 유무**를 함께 표기한다. 원칙: 새 데이터를 만들기 전에 **이미 있는 데이터부터** 화제로 끌어온다.

### ① 새 토픽 소스 — 저비용·고접지

#### 1-A. ★ NPC 전기(Deeds) 기반 동적 토픽 — 최우선 후보
동적으로 쌓이는 개인 사건 로그가 이미 있다. 참가자 이름·시점·아이템명·**지식 거리(`NpcKnowledgeLevel`: Lived/Did/Witnessed/Heard)** 까지 보유. [src: Assets/Scripts/Npc/NpcBiography.cs:8-51]

| 새 토픽 | 접지 소스 | 예시 대사 방향 |
|--------|----------|--------------|
| `RecentHunt` | `Wounded` (※`VentureFailed`는 enum만 있고 기록 경로 없음) | "지난 사냥에서 크게 다쳤소." |
| `LostCompanion` | **`DeceasedRelations`(사별 기억)** — `Fell` deed는 사망자 본인 전기에만 남아 화자 접지 불가 [src: Assets/Scripts/Manager/NpcManager.cs MarkNpcDead/RecordDeceasedRelationMemories] | "○○가 돌아오지 못했소." |
| `Boast` | `NotableItemAcquired` (+ ItemDisplayName) — **기록 경로 미배선**(AddNotableItemDeed 호출부 없음), 배선 시 자동 동작 | "이번에 [아이템]을 손에 넣었소." |
| `PartyBond` | `PartyAffinityChanged` | "○○와는 요즘 사이가 부쩍…" |

- **비용**: 낮음(Collect에 Deed 순회 추가). **환각 위험**: 낮음(사실+참가자 실명 존재).
- **핵심 이점**: 플레이 중 계속 쌓이므로 **캐시 문제를 근본적으로 우회** — 진짜로 매번 새 화제.
- **지식 거리 활용**: `HowKnown`으로 "내가 겪은 일"과 "전해 들은 일"의 어조를 분리(교차 일관성 §4.8과 연결).

#### 1-B. 관계 화제 확장 (현 `Gossip`은 Rival/Friend만)
관계 타입이 Spouse/Sibling/ParentOf/MentorOf/GrudgeAgainst까지 있고 사별 기억(`DeceasedRelationMemory`)도 있으나 대화 미활용. [src: Assets/Scripts/Npc/Narrative/NpcDialogueContextBuilder.cs:144-154, Assets/Scripts/Npc/NpcRelationEntry.cs]

- 새 토픽(안): `FamilyTalk`(가족 안부) / `MentorMemory`(스승 회상) / `Grudge`(원한 험담) / `Mourning`(사별 그리움).
- **비용**: 낮음. **환각 위험**: 낮음(관계 실명+OriginEventId 존재).

#### 1-C. 감정(Sentiment) 기반
특정 정착지·종족 감정 + 원인 사건 참조 이미 구현. [src: Assets/Scripts/Npc/Narrative/NpcDialogueContextBuilder.cs:74-88]

- 새 토픽(안): `Prejudice`(타 종족 편견) / `Nostalgia`(잃은 고향) / `Grievance`(정복 원한).
- **비용**: 낮음. **환각 위험**: 낮음.

#### 1-D. 성격 8축을 화제 선택에 반영
성격 수치는 이미 컨텍스트에 실림. 지금은 어조에만 영향. [src: Assets/Scripts/Npc/Narrative/NpcDialogueContextBuilder.cs:34-37]

- 화제 자체를 성격으로 편향: 탐욕高→돈/거래, 호기심高→바깥세상 질문, 자존심高→자랑, 용기高→모험담.
- **효과**: 같은 마을 NPC라도 화제 구성이 달라져 개성 부여. **비용**: 낮음(SortKey에 성격 가중치 추가).

#### 1-E. 마을 성장 반응
`Stage`·건물·인구 변화는 이미 시뮬됨. [src: Assets/Scripts/Village/VillageData.cs, Assets/Scripts/Village/VillageResourceSimulation.cs]

- 새 토픽(안): `VillageProud`(새 시설) / `Newcomer`(이민자 도착).
- **비용**: 중(변화 감지 훅 필요). **환각 위험**: 낮음.

### ② 반복을 죽이는 구조 — 토픽 수보다 효과 큼

| 장치 | 내용 | 기존 설계 |
|------|------|----------|
| **캐시 무효화** | 티어 상승·Deed 추가·마을 상태 변동 시 관련 캐시만 폐기 → 다음 대화 재생성 | NARRATIVE_ENGINE §8 #2 (미결정) |
| **variant 로테이션** | 한 조합에 대사 2~3개 캐시 후 순환(온도 0.8 살림) | 신규 |
| **시간대/맥락 접지** | SmallTalk에 아침/밤·계절 등 주입 | SPIKE §4.5 "SmallTalk 시간 접지" 후속 |
| **티어별 레퍼토리 확장** | 친해질수록 레퍼토리 캡 상향(더 많은 얘기) | 신규 |

### ③ 창발·고티어 — 재미의 핵심(설계 선행 필요)

| 아이디어 | 내용 | 기존 설계 |
|---------|------|----------|
| **사연(Personal) 갈래** | Friend+ 개인 과거 회고 | SPIKE §4.5·§8 (예정), R4 회고 대사 |
| **NPC 간 교차 대화** | A가 B 얘기 → B에게 물으면 일관 | NARRATIVE_ENGINE §4.8 (사건 정본화·지식 거리, 미구현) |
| **플레이어 반응** | 4축(Fear/Trust/Intimacy)·선물/대화 기억 반영 | interactionSystem §3·§4 (관계 4축 구현됨 [src: Assets/Scripts/Manager/NpcManager.cs:1010]) |
| **소망 연계 대화** | 소망 힌트 → 충족 후 감사(`FulfillDialogue`) | QUEST_SYSTEM (충족 루프 미구현). `FulfillDialogue` 필드는 생성만, 출력부 없음 [src: Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs] |
| **소문 전파** | 플레이어 행적이 마을 소문으로 확산 | 신규 |

---

## 4. 우선순위 제안 (가성비 순)

1. **1-A Deeds 토픽 + ② 캐시 무효화** — 데이터가 실시간으로 쌓이므로 적은 코드로 "매번 새롭다"를 실현. 질림의 근본 해결.
2. **1-B/1-C/1-D 관계·감정·성격 반영** — 접지 데이터가 모두 있어 환각 위험 낮고 캐릭터성이 크게 상승.
3. **③ 사연·교차 대화·소망 연계** — 창발의 핵심이나 설계 선행 필요(§4.8, R4, 충족 루프). 검증 후 본 구현 대상.

---

## 5. 구현 계획 (2026-07-15 수립)

§4 우선순위를 Phase로 옮긴 실행 계획. **신규 파일 없음** — 전부 기존 Narrative 스파이크 파일의 확장이다(스파이크 footprint 유지).

### 진행 게이트 (권장 순서)

```
재미 검증 1회전 (현행 기준선 측정: 질림 체감·반복 빈도 기록)
  → Phase 1 구현 → 재검증 (질림 개선 측정: 같은 NPC 반복 대화 신선도)
  → Phase 2 구현 → 재검증 (캐릭터성 측정: NPC 간 화제 차별화)
  → Phase 3 은 별도 설계 선행 후 판단
```

기준선 없이 Phase 1을 먼저 넣으면 "개선됐는지"를 측정할 수 없다 — 검증 1회전을 먼저 도는 것을 권장한다. 각 Phase 완료 시 §7 이관 워크플로를 실행한다.

### Phase 1 — Deeds 동적 토픽 + 캐시 무효화 (질림의 근본 해결) — ✅ 구현 완료 (2026-07-15, 플레이 검증 전)

**목표**: 플레이 중 쌓이는 개인 사건이 자동으로 새 화제가 되고, 사건 발생 시 낡은 캐시가 갱신된다.

| 스텝 | 변경 | 접점 (파일·위치) | 내용 |
|------|------|-----------------|------|
| P1-1 ✅ | 타입 확장 | [NpcNarrativeTypes.cs](../../Assets/Scripts/Npc/Narrative/NpcNarrativeTypes.cs) | ① `DialogueTopicCategory`에 `Personal` 추가 ② `DialogueTopicType`에 `RecentHunt`/`LostCompanion`/`Boast`/`PartyBond` 추가 ③ `DialogueTopic`에 `CacheVariant`(int, 기본 0) 필드 추가 |
| P1-2 ✅ | 사건→토픽 산출 | [DialogueTopicSelector.cs](../../Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs) `AddPersonal` | `save.Deeds` **최신순, 타입당 1건·합계 최대 2건** 토픽화(결정론 — 난수 불사용) + **사별 기억(`DeceasedRelations`) 최신 1건 → `LostCompanion`**(구현 중 접지 변경 — §3 1-A 표 참조). `Facts`에 시점(`When`)·참가자 실명(`ParticipantSnapshots`)·아이템명, `HowKnown`을 "직접 겪음/목격/전해 들음" 어조 힌트로 명시. `CacheVariant`에 사건 식별자(Deeds 인덱스는 append-only·불삭제라 안정, 사별은 사망자 entityId) 기록. `RelationLabel`은 NpcDialogueContextBuilder 것을 public 승격해 공유 |
| P1-3 ✅ | 캐시 키 확장 | [NpcNarrativeRuntime.cs](../../Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs) + [NpcNarrativeService.cs](../../Assets/Scripts/Npc/Narrative/NpcNarrativeService.cs) `GetDialogueAsync` | 대화 캐시를 `npcId → (tier, topicType, variant) → 대사` 2단 구조로 재편(NPC 단위 무효화 O(1)). 일반 토픽은 variant=0(기존 동작 불변), 사연 토픽은 P1-2의 식별자 → **같은 토픽 타입이라도 새 사건이면 새 대사** |
| P1-4 ✅ | 무효화 API + 훅 | NpcNarrativeRuntime + [NpcManager.cs](../../Assets/Scripts/Manager/NpcManager.cs) | `InvalidateDialogues(npcId)`(해당 NPC 대화 캐시만 제거, 셔플백·소망 캐시는 유지) 신설. `AddDeed`/`AddPartyAffinityDeed`/`AddWoundedDeed`/`AddNotableItemDeed` 말미 + **`RecordDeceasedRelationMemories`(생존자별)**에서 호출 — 사건이 생기면 다른 화제도 다음 대화에서 재생성 |
| P1-5 ✅ | 우선순위 재배열 | DialogueTopicSelector `SortKey` | `Need=4 > Personal=3 > Rumor=2 > Flavor=1`. 긴급(⚡) 새치기는 기존 유지 |

> **구현 중 계획 조정 (2026-07-15)**: ① `LostCompanion`은 Deeds가 아니라 `DeceasedRelations`로 접지(`Fell` deed는 사망자 본인 전기에만 남음) — Phase 2의 `Mourning`(P2-1)과 소스가 같아져 **LostCompanion이 이를 선행 구현**, P2-1은 잔여 3종만. ② `Boast`는 코드 완성이나 `NotableItemAcquired` 기록 경로가 미배선이라 **휴면**(배선 시 자동 동작). ③ `VentureFailed`는 기록 경로가 없어 `RecentHunt`는 `Wounded`만 접지. ④ 프롬프트 토픽 라벨(`TopicKorean`) 4종 추가.

- **티어 게이트(안)**: `RecentHunt`/`Boast`=Stranger, `LostCompanion`/`PartyBond`=Acquaintance+ (사적 사연은 안면 이후 — interactionSystem §4.3 점진 공개와 정합).
- **SPIKE §4.5와의 관계 — 해석 확정 (2026-07-15, 사람 확인)**: "사연(Personal)은 관계 계층 연결 후"의 "관계 계층"은 **NPC와 상호작용하며 호감도를 올리는 계층**(상호작용 → 호감도 반영 → 티어 산출)을 뜻한다고 확정. 이 계층은 구현돼 있으므로(Talk/Gift → `ModifyPlayerRelation` → 실호감도 티어 산출) 보류 조건 충족 [src: Assets/Scripts/Manager/NpcManager.cs:1010, Assets/Scripts/Common/Utility/InteractionHelper.cs:73, Assets/Scripts/UI/UINpcInspect.cs:604-610].
- **예상 규모**: 수정 4파일 + 훅 1파일, 신규 0. LLM 호출 증가는 "Deed 발생 시 1회 재생성" 수준 — R4 예산 내 [추론].

**Phase 1 검증 지표**
1. Deed 0건 NPC: 기존 12종 동작 그대로(회귀 없음).
2. Hunt 부상/사망/명품 획득 발생 → 다음 대화에서 해당 사건이 실명·아이템명으로 언급되는가(`[Narrative]` 로그).
3. 같은 NPC 반복 대화 시 "새 사건 → 새 대사" 갱신 확인(캐시 무효화 동작).
4. 창작(가짜 인물/사건) 오류율 ≈0 유지 — Facts 밖 서술 여부 수동 검수(R1과 동일 기준).

### Phase 2 — 관계·감정·성격 확장 (캐릭터성)

**목표**: NPC마다 "어떤 얘기를 하는 사람인지"가 달라진다.

| 스텝 | 변경 | 접점 | 내용 |
|------|------|------|------|
| P2-1 | 관계 토픽 3종 | DialogueTopicSelector | `FamilyTalk`/`MentorMemory`/`Grudge` — `GetRelations` 전 타입 순회. (~~Mourning~~ 은 Phase 1의 `LostCompanion`이 `DeceasedRelations` 접지로 선행 구현) 티어(안): 3종 모두 Acquaintance |
| P2-2 | 감정 토픽 3종 | DialogueTopicSelector | `Prejudice`/`Nostalgia`/`Grievance` — `save.Sentiments` 순회, `OriginEventId` 사실을 Facts로. **착수 시 `NpcSentiment.Type` 실제 값 목록 확인 후 매핑 확정** [src: Assets/Scripts/Npc/NpcSentiment.cs] |
| P2-3 | 성격 화제 편향 | DialogueTopicSelector `SortKey` | 토픽별 관련 성격축 매핑(안): 탐욕→`Boast`·거래류, 호기심→소문류, 자존심→`Boast`/`JobMood`, 용기→`RecentHunt`/`DangerRumor`. 해당 축 수치가 높으면 SortKey 가산 → 레퍼토리 선점 |
| P2-4 | 티어별 레퍼토리 캡 | DialogueTopicSelector `RepertoireSize` | `(npcId)` → `(npcId, tier)`: 기본 1~3 유지 + Friend +1, Confidant +1 (상한 4~5). "친해질수록 더 많은 얘기" |

**Phase 2 검증 지표**: 같은 마을 NPC 5~6명의 레퍼토리 구성이 서로 다른가(화제 겹침률), 성격과 화제의 납득감(수동 평가), 티어 상승 시 화제 폭 증가 확인.

### Phase 3 — 검증 후 본 구현 (설계 선행 필요, 본 계획 범위 밖)

| 항목 | 선행 조건 |
|------|----------|
| variant 로테이션·시간대 접지 | R4 비용 실측 후 예산 판단 |
| 마을 성장 반응(1-E) | 변화 감지 훅 설계(마을 도메인과 경계 협의) |
| 사연 심화·NPC 간 교차 대화 | NARRATIVE_ENGINE §4.8 (사건 정본화·지식 거리) 구현 |
| 소망 연계·충족 대사 | QUEST_SYSTEM 충족 루프 본 구현 |
| 소문 전파 | 신규 설계 필요 |

### 공통 리스크·주의

- **비용 상승**: 무효화·Deed variant 는 LLM 재생성을 늘린다. `[Narrative]` 로그로 호출 횟수를 함께 기록해 R4(지연·비용) 지표와 같이 판정한다.
- **난수 관례**: world_history 도메인은 `DerivedSeed` 채널 원칙(UnityEngine.Random 금지) — 신규 코드는 결정론(최신순 N건·안정 해시)으로 작성한다. 기존 `AddRumors`의 `Random.Range`는 본 계획 범위 밖(별도 정리 후보) [src: Wiki/world_history.md §6].
- **Deed 희소 NPC**: 갓 생성된 NPC는 Deed가 없어 Phase 1 효과가 없다 — Phase 2(관계·감정·성격)가 이 공백을 메운다.
- **컴파일·회귀 확인**: 각 Phase 완료 시 Unity 컴파일 + 기존 12종 토픽 회귀(특히 `Greeting` 폴백·긴급 새치기) 확인.

---

## 6. 경계 / 미결정

- **검증과의 관계**: ③의 상당수(사연·교차 대화·캐시 무효화)는 정본이 "검증 후 본 구현"으로 미뤄둔 항목이다. **검증 전 선구현은 스파이크 취지와 충돌**하므로, 재미 검증 데이터를 본 뒤 착수 범위를 정한다(§5 진행 게이트).
- **고유명사 캐논 부재**: 연대기가 `"정착지#5"` 숫자 ID로 평문화됨 — 소문/역사 토픽 다양화 시 몰입 저해 [src: Assets/Scripts/Npc/Narrative/DialogueTopicSelector.cs:399-404]. 지명·인명 사전 미설계(NARRATIVE_ENGINE §5, WORLD_HISTORY_PLAN 잔여).
- **캐시 vs 비용 트레이드오프**: 무효화·variant·Deeds 토픽은 LLM 호출 빈도를 올린다. 비용/지연 예산(R4)과 함께 판단.
- **세이브 영속**: 현재 캐시는 런타임 dict [src: Assets/Scripts/Npc/Narrative/NpcNarrativeRuntime.cs:8-10]. variant·무효화 정책은 영속 캐시 설계와 함께 정리 필요.

---

## 7. 통합(이관) 워크플로

본 문서는 제안 backlog다. 각 아이디어가 **구현 완료되면** 확정 결정을 아래 정본으로 접는다.

| 확정 대상 | 이관 정본 |
|----------|----------|
| 새 토픽 카탈로그(①) | NARRATIVE_ENGINE_SPIKE §4.5 표 확장 → 안정화 후 NARRATIVE_ENGINE_DESIGN |
| 캐시 무효화·variant·시간 접지(②) | NARRATIVE_ENGINE_DESIGN §6.3(캐시)·§8 #2 |
| 사연·교차 대화(③) | NARRATIVE_ENGINE_DESIGN §4.8 |
| 소망 연계·충족 대사(③) | QUEST_SYSTEM_DESIGN(충족 루프) + interactionSystem §4 |

**절차**: (1) 정본 편집 diff는 LLM이 초안 → 사람(이상훈) 승인·확정(`Docs/` 편집은 사람 권한). (2) 이관 완료 시 본 문서는 `Docs/_archive/`로 이동하거나 정본 링크 stub만 남긴다. (3) 위키 페이지([world_history.md](../../Wiki/world_history.md)·[npc.md](../../Wiki/npc.md)) 정본 매핑 갱신(Ingest).

---

**Last Updated**: 2026-07-15
**Status**: 🟨 Phase 1 구현 완료 (플레이 검증 전) — 다음: 플레이 검증(§5 Phase 1 지표) → Phase 2 판단. ※기준선 선측정 게이트는 사용자 결정으로 생략(B 경로)
**Dependencies**: 현행 대화 스파이크(`DialogueTopicSelector`/`NpcNarrativeService`/`NpcNarrativeRuntime`) · 백본 데이터(Deeds/관계/감정/성격) ✅ 존재
