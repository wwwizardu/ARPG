# NPC 상호작용 시스템 설계 (Interaction System)

## 개요
플레이어와 NPC 간 30가지 상호작용 행동 시스템. 성격과 관계를 종합하여 판정하고 결과에 따라 관계가 변동한다.

> **설계 노트**: 플레이어 전용 사회 스탯(설득력·매력·은신 등)은 두지 않는다. 플레이어와 NPC는 가능한 한 **통합 스탯**을 공유하며, 상호작용 판정은 **대상 NPC의 성향(`NpcStatComponent`) + 플레이어와의 관계 4축**만으로 계산한다. 구현은 ECS 컴포넌트/시스템이 아니라 **서비스(`InteractionHelper`) + 매니저(`NpcManager`) 데이터** 구조로 간다 (§1 참조).

---

## 1. 상호작용 구조

### 상호작용 흐름
```
[플레이어 행동 선택]
        ↓
[판정 공식 계산]
  (성격 + 관계)
        ↓
[성공/실패 결정]
        ↓
[관계 변동 적용]
        ↓
[결과 텍스트 & 이벤트]
```

### 아키텍처: 서비스 + 매니저 데이터 (ECS 시스템 아님)

상호작용은 플레이어가 UI에서 대상 NPC에게 일으키는 **1회성 이산 이벤트**다. 매 프레임 다수 엔티티를 순회하는 연속 시뮬레이션이 아니므로 ECS 컴포넌트/시스템(폴링)으로 모델링하지 않는다. 대신 네 계층으로 나눈다.

| 계층 | 역할 | 구현 위치 |
|------|------|-----------|
| **데이터** | 4축 관계 보관 + 불변식(Trust 캡 등) | `NpcManager` (npcId 키 영속, ECS 컴포넌트 아님) |
| **서비스** | 판정(성공률) + 결과 산출 | `InteractionHelper` (static, 무상태) |
| **UI** | 행동 선택·표시·트리거 | `UINpcInteract` |
| **부수효과** | 전투/AI/동료/퀘스트 실행 | 기존 ECS 시스템에 위임 |

```
UI(UINpcInteract) ──선택──▶ InteractionHelper.Resolve()
                                 │ 읽기: NpcStatComponent / GetPlayerRelation
                                 │ 쓰기: NpcManager.ModifyPlayerRelation()  ← Trust 캡 등 불변식
                                 └ 위임: 전투/AI/동료/퀘스트 (기존 ECS)
                            ◀─InteractionResult(반환 struct)──┘
NpcManager._playerRelations ──Save/Load──▶ WorldData.PlayerNpcRelationList ──▶ 세이브
```

> NPC의 영속 id == 엔티티 id이므로(NPC↔NPC 호감도와 동일 규약), 서비스는 같은 id로 `NpcStatComponent`(라이브 컴포넌트)와 관계 데이터(영속)를 함께 조회한다. 상호작용은 NPC가 눈앞에 스폰돼 있을 때만 일어나므로 오프라인 NPC 조회 문제는 없다.

### 서비스 API (InteractionHelper — static·무상태)

```csharp
// UI가 성공률을 미리 표시
public static float GetSuccessRate(int npcId, InteractionType type);

// 굴림 → 관계 변동 적용 → 부수효과 위임 → UI용 결과 반환
public static InteractionResult Resolve(int npcId, InteractionType type);

// UI 표시용 반환 struct (ECS 컴포넌트 아님)
public struct InteractionResult
{
    public bool Success;
    public int AffinityDelta, TrustDelta, FearDelta, IntimacyDelta;
    public ResultKind Kind;   // 성공 / 실패 / 전투개시 / 경비호출 등
}
```

타입별 공식·결과 델타는 v1 5종만 `switch`로 시작하고, 밸런싱 단계(§8)에서 계수를 시트/SO로 이관한다.

### 상호작용 타입
```csharp
public enum InteractionType
{
    // 대화 계열 (7가지)
    CasualTalk,     // 일상 대화
    AskInfo,        // 정보 캐묻기
    SpreadRumor,    // 소문 퍼뜨리기
    Persuade,       // 설득
    Lie,            // 거짓말
    Confide,        // 비밀 고백
    AskAdvice,      // 조언 구하기

    // 감정·태도 계열 (6가지)
    Compliment,     // 칭찬
    Insult,         // 모욕
    Threaten,       // 위협
    Comfort,        // 위로
    Apologize,      // 사과
    ConfessLove,    // 고백

    // 거래·경제 계열 (6가지)
    BuySell,        // 물건 사기/팔기
    Haggle,         // 흥정
    GiveGift,       // 선물
    Bribe,          // 뇌물
    Gamble,         // 도박
    Borrow,         // 빌리기

    // 행동·물리 계열 (4가지)
    Attack,         // 공격
    Pickpocket,     // 소매치기
    Stalk,          // 미행
    Escort,         // 호위

    // 관계·협력 계열 (7가지)
    AcceptQuest,    // 퀘스트 수주
    Recruit,        // 동행 제안
    Commission,     // 의뢰
    Betray,         // 배신
    Introduce,      // 소개
    Teach,          // 가르치기
    ChallengeDuel   // 결투 신청
}
```

---

## 2. 판정 공식 (Interaction별)

### 기본 공식 구조
```csharp
float CalculateSuccessRate(InteractionType type, int playerEntityId, int npcEntityId)
{
    // 1. 컴포넌트 가져오기 (플레이어 측 입력 항 없음 — NPC 성향 + 관계만)
    var npcStat = GetComponent<NpcStatComponent>(npcEntityId);
    var relationship = GetRelationship(playerEntityId, npcEntityId);

    // 2. 베이스 확률
    float baseRate = 50f;

    // 3. 상호작용별 가중치 적용
    float modifiedRate = baseRate + CalculateModifiers(type, npcStat, relationship);

    // 4. 최종 확률 (0~100 클램프)
    return Mathf.Clamp(modifiedRate, 0f, 100f);
}
```

> ※ 플레이어 사회 스탯을 제거하면서 각 공식의 player 항이 빠졌다. base 수치와 NPC/관계 가중치는 **추후 재밸런싱 대상**(§8 참조).

### 대화 계열

#### 1. CasualTalk (일상 대화)
**효과**: 친밀도 서서히 상승, 정보 획득 가능

**판정**: 항상 성공 (100%)

**결과**:
- 성공: Intimacy +1, Affinity +1
- 대화 주제에 따라 NPC 반응 다름

---

#### 2. AskInfo (정보 캐묻기)
**효과**: 퀘스트 단서, 위치, 소문 등 정보 획득

**판정 공식**:
```csharp
successRate = 50
    + (relationship.Affinity * 0.3f)
    - (npcStat.Honesty * 0.2f)  // 정직하면 쉽게 알려줌
    + (relationship.Trust * 0.2f);
```

**결과**:
- 성공: 정보 획득, Intimacy +2
- 실패: "그건 말할 수 없어요", Affinity -1

---

#### 3. Persuade (설득)
**효과**: NPC 행동/판단 변경 시도

**판정 공식**:
```csharp
successRate = 40
    + (relationship.Trust * 0.4f)
    - (npcStat.Pride * 0.3f)      // 자존심 높으면 설득 어려움
    - (npcStat.Greed * 0.2f);     // 탐욕 높으면 이익 없이는 설득 어려움
```

**결과**:
- 성공: 요청 수락, Trust +3, Affinity +2
- 실패: "그렇게 생각하지 않아요", Trust -2

---

#### 4. Lie (거짓말)
**효과**: NPC를 속이기

**판정 공식**:
```csharp
successRate = 30
    - (npcStat.Honesty * 0.4f)    // 정직하면 거짓말 간파
    - (npcStat.Curiosity * 0.2f); // 호기심 높으면 의심
```

**결과**:
- 성공: NPC가 믿음, Affinity +3 (일시적)
- 실패: "거짓말하지 마세요!", Trust -20, Affinity -10

---

#### 5. Confide (비밀 고백)
**효과**: 플레이어의 비밀을 NPC에게 털어놓기

**판정 공식**:
```csharp
successRate = 50
    + (relationship.Intimacy * 0.5f)
    + (npcStat.Friendliness * 0.3f)
    + (npcStat.Loyalty * 0.2f)
    - (npcStat.Greed * 0.3f);     // 탐욕 높으면 비밀 이용 가능
```

**결과**:
- 성공: Intimacy +10, Trust +5, 특별 대화 해금
- 실패: "그건... 너무 무거운 얘기네요", Intimacy -3

**리스크**: 실패 시 비밀이 마을에 퍼질 수 있음 (Loyalty 낮을 때)

---

### 감정·태도 계열

#### 6. Compliment (칭찬)
**효과**: 호감도 상승

**판정 공식**:
```csharp
successRate = 60
    + (npcStat.Pride * 0.3f)      // 자존심 높으면 칭찬 좋아함
    - (npcStat.Honesty * 0.2f);   // 정직하면 아첨 싫어함
```

**결과**:
- 성공: Affinity +5, "고마워요!"
- 실패: "아첨은 통하지 않아요", Affinity -2

---

#### 7. Insult (모욕)
**효과**: 도발 또는 적대 관계 형성

**판정**: 항상 성공 (100%)

**결과**:
- Affinity -20, Trust -10
- npcStat.Patience 낮으면 즉시 전투 or 마을 추방
- npcStat.Pride 높으면 결투 신청

---

#### 8. Threaten (위협)
**효과**: 강압적으로 요구 관철

**판정 공식**:
```csharp
successRate = 30
    - (npcStat.Courage * 0.5f)    // 용기 높으면 위협 안 먹힘
    - (npcStat.Pride * 0.3f)      // 자존심 높으면 저항
    + (relationship.Fear * 0.3f); // 이미 두려워하면 쉬움
```

**결과**:
- 성공: 요구 수락, Fear +10, Affinity -10, Trust -5
- 실패: "감히...!", 전투 시작 or 마을 경비 호출

---

#### 9. Apologize (사과)
**효과**: 나쁜 관계 회복

**판정 공식**:
```csharp
successRate = 40
    + (npcStat.Friendliness * 0.3f)
    + (npcStat.Patience * 0.3f)
    - (relationship.Affinity < 0 ? -relationship.Affinity : 0); // 호감도 낮을수록 어려움
```

**결과**:
- 성공: Affinity +10, Trust +5, "괜찮아요"
- 실패: "진심이 느껴지지 않아요", Affinity -2

---

### 거래·경제 계열

#### 10. Haggle (흥정)
**효과**: 거래 가격 낮추기

**판정 공식**:
```csharp
successRate = 50
    - (npcStat.Greed * 0.5f)      // 탐욕 높으면 흥정 어려움
    + (relationship.Affinity * 0.3f);
```

**결과**:
- 성공: 가격 10~30% 감소
- 실패: "이건 최선의 가격이에요"

---

#### 11. GiveGift (선물)
**효과**: 호감도 상승

**판정**: 항상 성공 (100%)

**결과** (선물 가치에 비례):
- 저가 (10G 이하): Affinity +3
- 중가 (10~50G): Affinity +7
- 고가 (50G 이상): Affinity +15, Intimacy +5
- NPC 선호 아이템: 추가 보너스 +10

---

#### 12. Bribe (뇌물)
**효과**: 불법적 요구 관철

**판정 공식**:
```csharp
successRate = 20
    + (npcStat.Greed * 0.7f)      // 탐욕 높으면 뇌물 쉽게 받음
    - (npcStat.Honesty * 0.6f)    // 정직하면 뇌물 거부
    - (npcStat.Loyalty * 0.3f)    // 충성심 높으면 배신 안 함
    + (relationship.Fear * 0.2f);
```

**결과**:
- 성공: 요구 수락, Affinity -5, Trust -10 (불법 관계)
- 실패: "나를 모욕하는 거야?!", Affinity -20, 경비 호출

---

### 행동·물리 계열

#### 13. Attack (공격)
**효과**: 전투 시작

**판정**: 항상 성공 (100%)

**결과**:
- 전투 돌입
- Affinity -50, Trust -50
- 마을 NPC 전체가 적대 관계 (Reputation -20)

---

#### 14. Pickpocket (소매치기)
**효과**: NPC 아이템 훔치기

**판정 공식**:
```csharp
successRate = 30
    - (npcStat.Curiosity * 0.3f)      // 호기심 높으면 눈치 빠름
    - (relationship.Intimacy * 0.2f); // 친밀하면 더 주시함
```

**결과**:
- 성공: 아이템 획득 (골드 or 소지품)
- 실패: "도둑이야!", Affinity -50, Trust -80, 경비 호출

---

#### 15. Stalk (미행)
**효과**: NPC 행동 정보 수집

**판정 공식**:
```csharp
successRate = 40
    - (npcStat.Curiosity * 0.4f);
```

**결과**:
- 성공: NPC 일일 루틴 정보 획득
- 실패: "왜 따라오는 거죠?", Affinity -10, Intimacy -5

---

### 관계·협력 계열

#### 16. Recruit (동료 영입)
**효과**: NPC를 파티원으로

**판정 공식**: (companionSystem.md 참조)
```csharp
successRate = 30
    + (relationship.Affinity - 50) * 0.5f
    + (relationship.Trust - 30) * 0.3f
    + (relationship.Intimacy * 0.2f)
    + (npcStat.Loyalty - 40) * 0.4f
    + (npcStat.Courage - 30) * 0.2f
    - (npcStat.Greed * 0.1f);
```

**결과**:
- 성공: NPC가 동료로 합류
- 실패: "죄송하지만 어려울 것 같아요", 재시도 쿨타임 1일

---

#### 17. Betray (배신)
**효과**: NPC를 배신 (적에게 넘기기, 거짓 정보 등)

**판정**: 항상 성공 (100%)

**결과**:
- NPC와의 관계 완전 파탄: Affinity -100, Trust -100
- 마을 전체 평판 하락: Reputation -30
- NPC가 복수 퀘스트 시작 가능

---

#### 18. ChallengeDuel (결투 신청)
**효과**: 명예 결투 (사망 없음)

**판정 공식**:
```csharp
successRate = 50
    + (npcStat.Courage * 0.5f)
    + (npcStat.Pride * 0.4f)
    - (npcStat.Patience * 0.2f);
```

**결과**:
- 성공: 결투 시작 (HP 30% 이하 시 항복)
- 승리: Reputation +10, 패자의 Trust +5 (명예로운 싸움)
- 패배: Reputation -5, 승자의 Affinity +10
- 실패: "결투는 사양할게요", Affinity -3

---

## 3. 관계 변동 시스템

### 관계 데이터 (NpcManager 소유)
```csharp
// 런타임 — NpcManager가 Dictionary<int npcId, PlayerRelation> 로 보관
public struct PlayerRelation
{
    public int Affinity;    // -100 ~ +100
    public int Trust;       // 0 ~ 100
    public int Fear;        // 0 ~ 100
    public int Intimacy;    // 0 ~ 100
    public int TrustCap;    // 0 ~ 100, Trust 상한 (한번 깎이면 같이 하락)
}

// 세이브 DTO — WorldData.PlayerNpcRelationList 에 List 로 직렬화
[Serializable]
public class PlayerNpcRelationEntry
{
    public int NpcId;
    public int Affinity, Trust, Fear, Intimacy;
    public int TrustCap = 100;
}
```

저장은 NPC↔NPC 호감도와 **동일 패턴**: `NpcManager._playerRelations`(런타임 Dict) ↔ `WorldData.PlayerNpcRelationList`(세이브). 키는 npcId 단일(1명의 플레이어 → 다수 NPC라 페어 키 불필요). NPC **영구 삭제** 시 `NpcManager`의 기존 정리 훅에서 함께 제거하고, 청크 unload(일시 비활성)에는 유지한다.

### 매니저 API (NpcManager, 플레이어 관계 region)
```csharp
public PlayerRelation GetPlayerRelation(int npcId);              // 없으면 기본값(전부 0, TrustCap=100)
public bool HasMetPlayer(int npcId);
public void ModifyPlayerRelation(int npcId, int dAff, int dTrust, int dFear, int dIntimacy);
public void SetPlayerRelation(int npcId, PlayerRelation rel);    // 디버그/특수 세팅
public List<PlayerNpcRelationEntry> SavePlayerRelations();
public void LoadPlayerRelations(List<PlayerNpcRelationEntry>? list);
```

### 변동 규칙 (ModifyPlayerRelation 내부 — 불변식)
변동 로직은 매니저에 둔다. 어떤 호출자(서비스·이벤트·퀘스트 보상)든 동일 규칙(Trust 비대칭·캡)을 강제로 따르게 하기 위함.
```csharp
public void ModifyPlayerRelation(int npcId, int dAff, int dTrust, int dFear, int dIntimacy)
{
    var rel = GetPlayerRelation(npcId);

    // 1. Affinity
    rel.Affinity = Mathf.Clamp(rel.Affinity + dAff, -100, 100);

    // 2. Trust (감소는 즉시 / 증가는 절반, 상한은 TrustCap)
    if (dTrust < 0)
    {
        rel.Trust = Mathf.Max(0, rel.Trust + dTrust);
        rel.TrustCap = Mathf.Max(rel.Trust, rel.TrustCap + dTrust / 2); // 큰 폭 하락 시 상한도 영구 하락
    }
    else
    {
        rel.Trust = Mathf.Min(rel.TrustCap, rel.Trust + dTrust / 2);
    }

    // 3. Fear / Intimacy
    rel.Fear = Mathf.Clamp(rel.Fear + dFear, 0, 100);
    rel.Intimacy = Mathf.Clamp(rel.Intimacy + dIntimacy, 0, 100); // 일반 상호작용 외 시간 경과로도 증가

    SetPlayerRelation(npcId, rel);
}
```

### Trust 회복의 어려움
- **Trust 감소**: 즉시 적용 (배신 시 -80)
- **Trust 회복**: 느리게 증가 (사과 성공 시 +5)
- **Trust 최대치**: 한번 감소하면 최대치도 감소
  - 예: Trust 80에서 20으로 감소 → 회복해도 최대 60까지만

---

## 4. 상호작용 UI

### 대화 창
```
┌─────────────────────────────────────────────┐
│  ○○○ (대장장이, Lv 12)                      │
│  호감도: ████████░░ (75)                    │
│  신뢰도: ██████░░░░ (60)                    │
│  ────────────────────────────────────────── │
│  "무엇을 도와드릴까요?"                     │
│  ────────────────────────────────────────── │
│  [일상 대화]     [정보 캐묻기]              │
│  [설득하기]      [선물하기]                 │
│  [흥정하기]      [동료 영입]                │
│  ────────────────────────────────────────── │
│  [더 보기...]    [나가기]                   │
└─────────────────────────────────────────────┘
```

### 판정 결과 UI
```
┌─────────────────────────────────────┐
│  설득 시도 중...                    │
│  ──────────────────────────────── │
│  성공률: 75%                        │
│  ██████████████████████████████     │
│  ──────────────────────────────── │
│  [주사위 굴리기]                    │
└─────────────────────────────────────┘

→ 성공 시:
┌─────────────────────────────────────┐
│  성공!                              │
│  ──────────────────────────────── │
│  ○○○: "알겠어요, 도와드릴게요."    │
│  ──────────────────────────────── │
│  신뢰도 +3, 호감도 +2               │
└─────────────────────────────────────┘
```

---

## 5. 특수 상호작용 (컨텍스트 기반)

### 상황별 추가 행동
```
전투 중:
  - [도와주기]: 적 처치 시 Affinity +10
  - [방치하기]: NPC 사망 시 Affinity -20

위험 상황:
  - [구출하기]: 함정/몬스터로부터 구출, Affinity +20

개인 목표:
  - [도와주기]: NPC 개인 목표 달성 지원, Intimacy +15
```

---

## 6. 구현 우선순위

### Phase 1: 기본 상호작용
- [ ] PlayerRelation 데이터 + NpcManager 관계 API (Get/Modify/Save/Load + 영구삭제 정리 훅)
- [ ] 영속 배선 (WorldData.PlayerNpcRelationList ↔ DataManager Save/Load, Version 증가)
- [ ] InteractionHelper 서비스 (GetSuccessRate / Resolve → InteractionResult)
- [ ] 5가지 기본 행동 (CasualTalk, AskInfo, Compliment, GiveGift, Recruit)
- [ ] 상호작용 UI (UINpcInteract — 대화 창)

### Phase 2: 판정 시스템
- [ ] 성공률 계산 공식 구현 (NPC 성향 + 관계 4축)
- [ ] 주사위 굴리기 UI
- [ ] 결과 텍스트 시스템

### Phase 3: 관계 변동
- [ ] 관계 변동 로직
- [ ] Trust 회복 난이도
- [ ] 관계 히스토리 기록

### Phase 4: 고급 상호작용
- [ ] 15가지 추가 행동 구현
- [ ] 특수 상호작용 (컨텍스트)
- [ ] 상호작용 결과 이벤트

### Phase 5: 피드백
- [ ] 상호작용 애니메이션
- [ ] 사운드 효과
- [ ] 관계 변화 알림

---

## 7. 테스트 시나리오

### 테스트 1: 기본 대화
1. NPC에게 다가가기
2. "일상 대화" 선택
3. 대화 텍스트 확인
4. Intimacy +1 확인

### 테스트 2: 설득 판정
1. "설득하기" 선택
2. 성공률 표시 확인 (예: 65%)
3. 주사위 굴리기
4. 성공 시: Trust +3 확인
5. 실패 시: Trust -2 확인

### 테스트 3: 관계 악화
1. "모욕하기" 선택
2. Affinity -20 확인
3. NPC 반응 변화 (적대적)
4. 재대화 시 옵션 제한 확인

---

## 8. 밸런스 고려사항

### 성공률 범위
- **너무 쉬움 (80% 이상)**: 긴장감 부족
- **적정 (50~70%)**: 선택의 무게감
- **너무 어려움 (30% 이하)**: 좌절감

> 플레이어 사회 스탯 제거로 판정이 NPC 성향 + 관계 4축에만 의존한다. 따라서 동일 NPC·동일 관계 상태에서는 성공률이 고정되므로, 관계를 쌓아 성공률을 끌어올리는 흐름이 자연스럽도록 base/가중치를 튜닝한다.

### 관계 회복 비용
- **Affinity 회복**: 비교적 쉬움 (선물, 칭찬)
- **Trust 회복**: 매우 어려움 (시간 + 여러 번의 사과/도움)
- **Intimacy 증가**: 장기적 상호작용 필요

### 리스크 vs 보상
- **안전한 행동** (CasualTalk, Compliment): 작은 보상
- **위험한 행동** (Lie, Bribe, Pickpocket): 큰 보상 but 실패 시 큰 페널티

---

**Last Updated**: 2026-06-26
**Status**: 설계 완료, 구현 대기 중 (플레이어 사회 스탯 제거 — 판정은 NPC 성향 + 관계 4축 / 서비스 + 매니저 데이터 구조, ECS 아님)
**Dependencies**: NPC 시스템, 관계 시스템, 대화 시스템
