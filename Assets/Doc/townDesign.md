# RPG NPC 마을 발전 게임 - 설계 문서

## 프로젝트 개요
NPC들이 자율적으로 행동하며 마을을 발전시키는 RPG 게임. 유저는 NPC와 다양한 상호작용을 통해 마을의 성장 방향에 영향을 준다. NPC의 성격 스탯에 따라 동일한 행동에도 다른 반응이 나온다.

---

## 1. NPC 성격 스탯 (0~100)

| 스탯 | 설명 | 낮을 때 | 높을 때 |
|------|------|---------|---------|
| friendliness (친화성) | 타인에 대한 호의 | 냉담, 적대적 | 따뜻, 환대 |
| courage (용기) | 위험/갈등 상황 반응 | 회피, 도주 | 맞서 싸움 |
| honesty (정직성) | 진실을 말하는 경향 | 거짓말, 기만 | 솔직한 정보 제공 |
| greed (탐욕) | 물질적 이익 집착 | 공정, 관대 | 바가지, 뇌물에 약함 |
| curiosity (호기심) | 새로운 것에 대한 관심 | 무관심 | 질문 많음, 단서 제공 |
| pride (자존심) | 자기 평가 | 비위 맞추기 쉬움 | 칭찬에 기쁨, 모욕에 강한 반응 |
| loyalty (충성심) | 소속 집단에 대한 헌신 | 쉽게 매수됨 | 배신 어려움, 동료 감쌈 |
| patience (인내심) | 자극에 대한 참을성 | 쉽게 화냄, 대화 끊음 | 반복 요청 수용 |

---

## 2. NPC ↔ 유저 관계 스탯 (동적 변동)

| 스탯 | 범위 | 설명 |
|------|------|------|
| affinity (호감도) | -100 ~ +100 | 유저에 대한 전반적 감정 |
| trust (신뢰도) | 0 ~ 100 | 유저를 믿는 정도. 한번 떨어지면 회복 느림 |
| fear (공포) | 0 ~ 100 | 유저를 두려워하는 정도. 높으면 복종하지만 뒤통수 가능 |
| intimacy (친밀도) | 0 ~ 100 | 관계 깊이. 비밀 대화/특별 퀘스트 해금 |
| reputation_awareness (평판 인지) | 0 ~ 100 | 유저의 세계적 명성 인지도 |

---

## 3. NPC 추가 속성 (마을 시스템용)

```
- job: 직업 (farmer, blacksmith, merchant, hunter, builder, scholar, guard, chief 등)
- skill_level: 직업 숙련도 (0~100). 시간 경과 시 상승, 결과물 품질에 영향
- personal_goal: 개인 목표 (bigger_house, rare_material, relationship, skill_mastery 등)
- condition: { hp, hunger, morale } — 컨디션. 낮으면 효율 저하, 극단 시 마을 이탈
```

---

## 4. 유저 행동 스탯

| 스탯 | 설명 |
|------|------|
| persuasion (설득력) | 논리적 설득 성공률 |
| intimidation (위협) | 공포 기반 강압 성공률 |
| charisma (매력) | 첫인상, 유머, 칭찬 등 감성적 접근 효과 |
| insight (통찰력) | NPC 숨겨진 감정/거짓말 간파 |
| knowledge (지식) | 특정 분야 전문 대화 가능 여부 |

---

## 5. 유저 → NPC 상호작용 행동 (30가지)

### 대화 계열
1. casual_talk — 일상 대화 (친밀도 서서히 상승)
2. ask_info — 정보 캐묻기 (퀘스트 단서, 위치 등)
3. spread_rumor — 소문 퍼뜨리기 (진실 또는 거짓)
4. persuade — 설득 (NPC 행동/판단 변경 시도)
5. lie — 거짓말/속이기
6. confide — 비밀 고백 (친밀도 크게 상승, 리스크 있음)
7. ask_advice — 조언 구하기 (NPC 자존심 상승)

### 감정·태도 계열
8. compliment — 칭찬/아첨
9. insult — 모욕/도발
10. threaten — 위협/협박
11. comfort — 위로/격려
12. apologize — 사과
13. confess_love — 고백/구애

### 거래·경제 계열
14. buy_sell — 물건 사기/팔기
15. haggle — 흥정
16. give_gift — 선물 주기
17. bribe — 뇌물
18. gamble — 도박/내기
19. borrow — 물건 빌리기

### 행동·물리 계열
20. attack — 공격/전투
21. pickpocket — 소매치기/훔치기
22. stalk — 미행/감시
23. escort — 호위/보호

### 관계·협력 계열
24. accept_quest — 퀘스트 수주
25. recruit — 동행 제안
26. commission — 의뢰하기
27. betray — 배신/밀고
28. introduce — 다른 NPC에게 소개
29. teach — 가르치기/훈련
30. challenge_duel — 결투 신청

---

## 6. 상호작용 판정 공식 (예시)

```
흥정 성공률 = (player.persuasion * 0.6 + player.charisma * 0.4) - (npc.greed * 0.5 - npc.affinity_to_player * 0.3)
위협 성공률 = (player.intimidation * 0.7) - (npc.courage * 0.5 + npc.pride * 0.3) + (npc.fear * 0.2)
거짓말 성공률 = (player.persuasion * 0.5 + player.charisma * 0.3) - (npc.honesty * 0.4 + npc.insight * 0.3)
```

---

## 7. 시간 시스템

- 게임 내 시간은 존재하지만 밤/낮 구분 없음
- NPC 행동은 AI State Strategy 패턴으로 구동 (System_AI_Behavior + IAIStateHandler)

---

## 8. NPC 기본 행동 패턴

### 핵심 개념
NPC는 기존 AI 행동 시스템(System_AI_Behavior)을 확장하여 동작한다.
각 AI 상태(Idle, Patrol, Chase, Attack, Retreat, Flee)는 독립된 StateHandler 클래스로 구현되며,
AIBehaviorFactory가 (BehaviorType, AIState) 조합으로 적절한 핸들러를 선택한다.

### AI State Strategy 구조

```
IAIStateHandler (인터페이스)
  ├── IdleStateHandler          — 공통: 대기, 타겟 감지 → Chase
  ├── PatrolStateHandler        — NPC: 스폰 위치 근처 배회, 위협 → Flee/Chase
  ├── ChaseStateHandler         — 공통: 타겟 추적, 공격 범위 도달 → Attack
  ├── MeleeAttackStateHandler   — 근접: 정지 + 스킬, KeepDistance 체크
  ├── RangedAttackStateHandler  — 원거리: 정지 + 스킬, KeepDistance 체크
  ├── RetreatStateHandler       — 공통: 타겟 반대 방향 이동
  └── FleeStateHandler          — NPC: 위협 반대 방향 도주
```

### 프로필 매핑

| BehaviorType | Idle | Patrol | Chase | Attack | Retreat | Flee |
|-------------|------|--------|-------|--------|---------|------|
| Melee (근접 몬스터) | Idle | - | Chase | MeleeAttack | Retreat | - |
| Ranged (원거리 몬스터) | Idle | - | Chase | RangedAttack | Retreat | - |
| Patrol (NPC) | Idle | Patrol | Chase | MeleeAttack | Retreat | Flee |

### 상태 흐름

```
몬스터:  Idle → (타겟 감지) → Chase → Attack ↔ Retreat → (타겟 상실) → Idle
NPC:     Patrol → (위협 감지) → Flee or Chase → Attack ↔ Retreat → (해소) → Patrol
```

### NPC vs 몬스터 구분
- NpcTag 보유 여부로 판별
- NPC: 기본 상태 = Patrol, 위협 시 Courage < 70이면 Flee, 아니면 Chase
- 몬스터: 기본 상태 = Idle, 위협 시 항상 Chase

### 성격 스탯 영향 (현재)
- **courage 높음**: Flee 대신 전투(Chase) 선택
- 나머지 성격 스탯은 추후 상호작용/관계 시스템에서 활용 예정

### 직업 활동 (향후 확장)
건물/작업장 시스템 구현 후 Work 상태와 직업별 StateHandler를 추가하여 확장 예정.

---

## 9. NPC AI 거리 기반 LOD (향후 구현)

### 거리 기반 LOD 업데이트

NPC 수가 많아지면 매 프레임 전원을 처리하는 것은 비효율적이다.
플레이어와의 거리에 따라 **업데이트 주기를 차등 적용**한다.

#### 업데이트 티어

| 티어 | 거리 | 업데이트 주기 | 처리 방식 |
|------|------|--------------|-----------|
| **Near** | 0 ~ 15 | 매 프레임 | 실시간 이동/애니메이션 정상 동작 |
| **Mid** | 15 ~ 40 | 5초 | 경과 시간 합산 후 일괄 처리. 이동은 텔레포트 방식 |
| **Far** | 40+ | 15초 | 비주얼 비활성화, 결과만 적용 |

#### 티어 전환 시 처리
- **Far → Near**: 합산된 결과를 즉시 적용 후 실시간 모드로 전환
- **Near → Far**: 현재 활동 상태를 저장하고 합산 모드로 전환
- 히스테리시스 적용: Near→Mid 전환은 17, Mid→Near 전환은 13 (떨림 방지)

#### 티어 판정
- `System_EntityActivation`이 이미 거리 기반으로 엔티티를 관리하므로 이를 활용

---

## 10. 마을 자원 시스템

### 기본 자원

| 자원 | 생산 NPC | 용도 |
|------|----------|------|
| food (식량) | farmer, hunter, fisher | NPC 생존, morale 유지 |
| wood (목재) | lumberjack | 건물 건설, 도구 제작 |
| stone (석재) | miner | 건물 업그레이드, 성벽 |
| ore/metal (광석/금속) | miner → blacksmith | 무기, 도구, 방어구 |
| gold (골드) | merchant (교역 수익), tax | 건설 비용, NPC 고용 |
| herbs (약초/마법 재료) | herbalist, scholar | 치료, 마법 연구 |

### 자원 체인
```
생산(raw) → 가공(processed) → 소비/건설(consumed)
예: 광부(철광석) → 대장장이(도구) → 벌목꾼(효율↑) → 목재↑ → 건축가(건물)
체인 끊김 → 마을 발전 정체/퇴보
```

---

## 11. 마을 발전 단계

| 단계 | 이름 | NPC 수 | 특징 |
|------|------|--------|------|
| 1 | settlement (정착지) | 3~5 | 텐트, 기본 식량 생산 |
| 2 | hamlet (작은 마을) | 8~12 | 목조 건물, 상점/대장간 등장 |
| 3 | village (마을) | 15~20 | 석조 건물, 성벽/시장/교회 |
| 4 | town (소도시) | 25~30 | 전문 시설, 외부 교역 |
| 5 | city (도시) | 40+ | 대규모 시설, 정치 체계, 군사력 |

발전 조건: 인구 수 + 특정 건물 + NPC 평균 morale + 유저 평판 등 복합 조건

---

## 12. 건물 시스템

| 건물 | 필요 자원 | 효과 |
|------|----------|------|
| farm | wood, gold | 식량 생산↑ |
| forge | stone, metal, gold | 무기/도구 제작 |
| market | wood, gold | 교역 수익↑, 외부 상인 방문 |
| wall | stone (대량) | 방어력↑, 습격 피해↓ |
| housing | wood, stone | NPC 수용 인원↑ |
| tavern | wood, gold | NPC morale↑, 정보 수집 장소 |
| library | stone, gold, rare materials | 학자 연구↑, 마법 해금 |
| training_ground | wood, metal | 경비병 전투력↑ |

건설 과정: 건축가 NPC + 자원 + 시간 필요. 유저 참여 시 속도↑

---

## 13. NPC 간 관계

- NPC끼리도 affinity, trust 등 관계 스탯 보유
- 시간 경과에 따라 친구/연인/라이벌 관계 형성
- NPC 간 갈등 발생 시 유저가 중재 가능

---

## 14. NPC 자율 발전 행동

- 대장장이 숙련도↑ → 대장간 업그레이드 제안
- 상인 골드 충분 → 시장 확장 추진
- 촌장이 인구 증가 감지 → 주택 건설 지시
- 학자 연구 완료 → 새 기술 마을 전체 해금

---

## 15. 유저 역할

### 직접 개입
- 자원 기부, 건설 참여
- NPC에게 직접 지시 (촌장 직위 시)
- 몬스터로부터 마을 방어

### 간접 개입
- NPC 설득으로 행동 방향 변경
- NPC 간 갈등 중재
- 외부 희귀 자원 반입
- 새 NPC를 마을로 영입

### 방치 시
- NPC 자율 운영 지속
- 몬스터 습격 시 피해 증가
- 예상치 못한 상황 발생 가능

---

## 16. 외부 위협/이벤트

| 유형 | 설명 |
|------|------|
| monster_raid | 마을 발전 단계↑ → 더 강한 적. 성벽/경비병/유저로 방어 |
| natural_disaster | 가뭄(식량↓), 폭풍(건물 피해), 전염병(NPC 컨디션↓) |
| external_force | 도적단 약탈, 타 영주 세금 요구, 전쟁 징집 |
| wanderer | 새 NPC 방문. 인력↑이지만 식량 소모↑, 간첩 가능성 |
| random_event | 희귀 광맥 발견, 상인단 방문, NPC 분쟁 등 |

---

## 17. 전체 시스템 구조

```
TimeSystem (틱 기반 클럭)
  ├── NPCAgent (각 NPC별 에이전트 루프)
  │     ├── PersonalityStats (성격)
  │     ├── SkillSystem (직업 숙련도)
  │     ├── ConditionSystem (체력/배고픔/사기)
  │     ├── GoalSystem (개인 목표)
  │     └── DecisionEngine (행동 선택: 생존→직업→목표→사회)
  ├── RelationshipSystem
  │     ├── NPC ↔ Player 관계
  │     └── NPC ↔ NPC 관계
  ├── ResourceSystem
  │     ├── 생산 → 가공 → 소비 체인
  │     └── 마을 자원 저장소
  ├── VillageSystem
  │     ├── BuildingManager (건물 건설/업그레이드)
  │     ├── DevelopmentStage (발전 단계 판정)
  │     └── PopulationManager (인구 관리)
  ├── InteractionSystem
  │     ├── 30가지 유저→NPC 행동
  │     ├── 판정 공식 (성격+관계+유저스탯 조합)
  │     └── 결과 분기 처리
  └── EventSystem
        ├── 외부 위협 (습격, 재해, 외부 세력)
        └── 랜덤 이벤트 생성기
```