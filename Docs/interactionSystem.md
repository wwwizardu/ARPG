# NPC 상호작용 시스템 설계 (Interaction System)

## 개요
플레이어와 NPC 간 30가지 상호작용 행동 시스템. 성격, 관계, 플레이어 스탯을 종합하여 판정하고 결과에 따라 관계가 변동한다.

---

## 1. 상호작용 구조

### 상호작용 흐름
```
[플레이어 행동 선택]
        ↓
[판정 공식 계산]
  (성격 + 관계 + 플레이어 스탯)
        ↓
[성공/실패 결정]
        ↓
[관계 변동 적용]
        ↓
[결과 텍스트 & 이벤트]
```

### 상호작용 컴포넌트
```csharp
public struct InteractionComponent
{
    public int InitiatorEntityId;  // 상호작용 시작자 (플레이어)
    public int TargetEntityId;     // 대상 NPC
    public InteractionType Type;   // 상호작용 종류
    public float SuccessRate;      // 성공률 (0~100)
    public bool IsProcessed;       // 처리 완료 여부
}

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

## 2. 플레이어 스탯 (상호작용용)

```csharp
public struct PlayerSocialStats
{
    public int Persuasion;      // 설득력 (0~100)
    public int Intimidation;    // 위협 (0~100)
    public int Charisma;        // 매력 (0~100)
    public int Insight;         // 통찰력 (0~100)
    public int Stealth;         // 은신 (0~100)
}
```

### 스탯 성장
- **레벨업**: 포인트 분배
- **장비**: 악세서리로 스탯 증가 (예: "은혀의 목걸이" = Persuasion +10)
- **퀘스트 보상**: 특정 퀘스트 완료 시 영구 증가

---

## 3. 판정 공식 (Interaction별)

### 기본 공식 구조
```csharp
float CalculateSuccessRate(InteractionType type, int playerEntityId, int npcEntityId)
{
    // 1. 컴포넌트 가져오기
    var playerStats = GetPlayerSocialStats(playerEntityId);
    var npcStat = GetComponent<NpcStatComponent>(npcEntityId);
    var relationship = GetRelationship(playerEntityId, npcEntityId);

    // 2. 베이스 확률
    float baseRate = 50f;

    // 3. 상호작용별 가중치 적용
    float modifiedRate = baseRate + CalculateModifiers(type, playerStats, npcStat, relationship);

    // 4. 최종 확률 (0~100 클램프)
    return Mathf.Clamp(modifiedRate, 0f, 100f);
}
```

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
    + (playerStats.Charisma * 0.3f)
    + (playerStats.Insight * 0.2f)
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
    + (playerStats.Persuasion * 0.6f)
    + (playerStats.Charisma * 0.3f)
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
    + (playerStats.Persuasion * 0.5f)
    + (playerStats.Charisma * 0.3f)
    - (npcStat.Honesty * 0.4f)    // 정직하면 거짓말 간파
    - (npcStat.Curiosity * 0.2f)  // 호기심 높으면 의심
    - (playerStats.Insight * 0.3f); // 통찰력 높으면 거짓말 잘함
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
    + (playerStats.Charisma * 0.5f)
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
    + (playerStats.Intimidation * 0.7f)
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
    + (playerStats.Charisma * 0.4f)
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
    + (playerStats.Persuasion * 0.6f)
    + (playerStats.Charisma * 0.4f)
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
    + (playerStats.Stealth * 0.8f)
    - (npcStat.Curiosity * 0.3f)  // 호기심 높으면 눈치 빠름
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
    + (playerStats.Stealth * 0.7f)
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
    - (npcStat.Greed * 0.1f)
    + (playerStats.Charisma * 0.3f);
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

## 4. 관계 변동 시스템

### 관계 스탯 변동 범위
```csharp
public struct RelationshipComponent
{
    public int Affinity;    // -100 ~ +100
    public int Trust;       // 0 ~ 100
    public int Fear;        // 0 ~ 100
    public int Intimacy;    // 0 ~ 100
}
```

### 변동 규칙
```csharp
void ModifyRelationship(int playerId, int npcId, int affinityDelta, int trustDelta)
{
    var rel = GetRelationship(playerId, npcId);

    // 1. Affinity 변동
    rel.Affinity = Mathf.Clamp(rel.Affinity + affinityDelta, -100, 100);

    // 2. Trust 변동 (감소는 빠르고, 증가는 느림)
    if (trustDelta < 0)
        rel.Trust = Mathf.Max(0, rel.Trust + trustDelta);  // 즉시 감소
    else
        rel.Trust = Mathf.Min(100, rel.Trust + trustDelta / 2);  // 절반만 증가

    // 3. Intimacy는 시간 경과 + 상호작용으로만 증가
    // (일반 상호작용에서는 직접 증가 안 함)

    SetRelationship(playerId, npcId, rel);
}
```

### Trust 회복의 어려움
- **Trust 감소**: 즉시 적용 (배신 시 -80)
- **Trust 회복**: 느리게 증가 (사과 성공 시 +5)
- **Trust 최대치**: 한번 감소하면 최대치도 감소
  - 예: Trust 80에서 20으로 감소 → 회복해도 최대 60까지만

---

## 5. 상호작용 UI

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

## 6. 특수 상호작용 (컨텍스트 기반)

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

## 7. 구현 우선순위

### Phase 1: 기본 상호작용
- [ ] InteractionComponent 정의
- [ ] System_Interaction (판정 로직)
- [ ] 5가지 기본 행동 (CasualTalk, AskInfo, Compliment, GiveGift, Recruit)
- [ ] 상호작용 UI (대화 창)

### Phase 2: 판정 시스템
- [ ] 성공률 계산 공식 구현
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

## 8. 테스트 시나리오

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

## 9. 밸런스 고려사항

### 성공률 범위
- **너무 쉬움 (80% 이상)**: 긴장감 부족
- **적정 (50~70%)**: 선택의 무게감
- **너무 어려움 (30% 이하)**: 좌절감

### 관계 회복 비용
- **Affinity 회복**: 비교적 쉬움 (선물, 칭찬)
- **Trust 회복**: 매우 어려움 (시간 + 여러 번의 사과/도움)
- **Intimacy 증가**: 장기적 상호작용 필요

### 리스크 vs 보상
- **안전한 행동** (CasualTalk, Compliment): 작은 보상
- **위험한 행동** (Lie, Bribe, Pickpocket): 큰 보상 but 실패 시 큰 페널티

---

**Last Updated**: 2026-04-01
**Status**: 설계 완료, 구현 대기 중
**Dependencies**: NPC 시스템, 관계 시스템, 대화 시스템
