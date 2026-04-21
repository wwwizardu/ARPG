# 동료 시스템 설계 (Companion System)

## 개요
NPC를 동료로 영입하여 함께 탐험/전투하는 시스템. 관계도, 성격, 직업에 따라 동료의 능력과 행동이 달라진다.

---

## 1. 핵심 컨셉

### 동료란?
- **마을 NPC 중 선택**하여 파티원으로 영입
- **최대 동료 수**: 1~3명 (업그레이드 가능)
- **관계도 기반**: 호감도/신뢰도가 높아야 영입 가능
- **자율 전투**: AI로 자동 전투, 명령으로 행동 조절
- **성장**: 함께 싸우며 레벨업, 장비 강화

### 동료의 장점
1. **전투 지원**: 추가 화력, 탱킹, 힐
2. **특수 능력**: 직업별 고유 스킬 (대장장이 = 장비 수리, 학자 = 버프)
3. **관계 이벤트**: 동료 전용 대화 및 퀘스트
4. **시너지**: 여러 동료 간 콤보 효과

---

## 2. 동료 영입 조건

### 기본 조건
```
1. 관계도 조건
   - Affinity >= 50 (호감도)
   - Trust >= 30 (신뢰도)

2. 성격 조건
   - NpcStatComponent.Loyalty >= 40 (충성심)
   - NpcStatComponent.Courage >= 30 (용기)

3. 상태 조건
   - NPC가 마을에 있어야 함 (작업 중이 아님)
   - 다른 플레이어의 동료가 아님

4. 플레이어 조건
   - 동료 슬롯 여유 있음 (기본 1개, 최대 3개)
   - 특정 퀘스트 완료 (첫 동료 해금용)
```

### 영입 판정 공식
```csharp
float recruitScore = 0f;

// 관계도 영향
recruitScore += (relationship.Affinity - 50) * 0.5f;  // 호감도 50 이상
recruitScore += (relationship.Trust - 30) * 0.3f;     // 신뢰도 30 이상
recruitScore += relationship.Intimacy * 0.2f;         // 친밀도 보너스

// 성격 영향
recruitScore += (npcStat.Loyalty - 40) * 0.4f;        // 충성심
recruitScore += (npcStat.Courage - 30) * 0.2f;        // 용기
recruitScore -= npcStat.Greed * 0.1f;                 // 탐욕 페널티

// 플레이어 카리스마
recruitScore += player.Charisma * 0.3f;

// 성공률
float successRate = Mathf.Clamp(50f + recruitScore, 0f, 100f);
return Random.Range(0f, 100f) < successRate;
```

### 영입 실패 시
- **호감도 감소 없음** (재시도 가능)
- **쿨타임**: 1일 (게임 내 시간)
- **힌트 제공**: "좀 더 친해진 후 다시 요청하세요"

---

## 3. 동료 컴포넌트

### CompanionTag (마커)
```csharp
public struct CompanionTag
{
    // 빈 struct (태그 역할만)
}
```

### CompanionComponent
```csharp
public struct CompanionComponent
{
    // 기본 정보
    public int PlayerEntityId;      // 주인 플레이어 ID
    public int SlotIndex;           // 동료 슬롯 (0~2)

    // 행동 설정
    public CompanionCommand Command; // 현재 명령
    public float FollowDistance;     // 추종 거리 (기본 3.0f)
    public bool AutoAttack;          // 자동 공격 (기본 true)

    // 상태
    public float Loyalty;            // 동료 충성도 (0~100, 감소 시 이탈)
    public float Morale;             // 사기 (0~100, 전투 효율 영향)
    public int BattleCount;          // 함께 싸운 전투 수
    public float TotalDamageDealt;   // 총 입힌 데미지
    public float TotalDamageTaken;   // 총 받은 데미지
}

public enum CompanionCommand
{
    Follow,     // 추종 (기본)
    Attack,     // 공격 우선 (적극적)
    Defend,     // 방어 우선 (플레이어 근처 유지)
    Hold,       // 제자리 대기
    Retreat     // 후퇴 (플레이어 뒤로)
}
```

### CompanionBonusComponent (관계 보너스)
```csharp
public struct CompanionBonusComponent
{
    public float DamageBonus;       // 데미지 증가 (%)
    public float DefenseBonus;      // 방어력 증가 (%)
    public float ExpBonus;          // 경험치 보너스 (%)
    public bool HasSpecialAbility;  // 특수 능력 해금 여부
}
```

---

## 4. 동료 행동 AI (System_CompanionAI)

### 우선순위
```
1. Command 명령 처리
2. 전투 상황 판단
   - 적이 있고 AutoAttack = true → 공격
   - 적이 없거나 AutoAttack = false → 추종
3. 플레이어 거리 유지
4. 대기
```

### 구현 예시
```csharp
public class System_CompanionAI : IFixedUpdateSystem
{
    public int Priority => 45; // AI 시스템보다 약간 늦게
    public float UpdateInterval => 0.1f; // 0.1초마다

    public void OnFixedUpdate(float deltaTime)
    {
        ComponentManager cm = AR.s.Component;
        SparseSet<CompanionComponent> companionPool = cm.GetComponentPool<CompanionComponent>();

        for (int i = 0; i < companionPool.Count; i++)
        {
            int entityId = companionPool.GetEntityId(i);
            CompanionComponent companion = companionPool.GetByIndex(i);

            // 플레이어 위치 가져오기
            if (!cm.TryGetComponent<TransformComponent>(companion.PlayerEntityId, out var playerTransform))
                continue;

            // 자신 위치
            if (!cm.TryGetComponent<TransformComponent>(entityId, out var myTransform))
                continue;

            // 명령별 행동
            switch (companion.Command)
            {
                case CompanionCommand.Follow:
                    FollowPlayer(entityId, myTransform, playerTransform, companion.FollowDistance);
                    if (companion.AutoAttack)
                        AttackNearbyEnemies(entityId);
                    break;

                case CompanionCommand.Attack:
                    AttackNearbyEnemies(entityId, aggressive: true);
                    break;

                case CompanionCommand.Defend:
                    StayNearPlayer(entityId, myTransform, playerTransform, radius: 2f);
                    AttackNearbyEnemies(entityId, onlyIfClose: true);
                    break;

                case CompanionCommand.Hold:
                    // 제자리 대기 (이동 안 함)
                    break;

                case CompanionCommand.Retreat:
                    RetreatToPlayer(entityId, myTransform, playerTransform);
                    break;
            }

            companionPool.SetByIndex(i, companion);
        }
    }

    private void FollowPlayer(int entityId, TransformComponent myPos, TransformComponent playerPos, float distance)
    {
        float currentDistance = Vector2.Distance(myPos.Position, playerPos.Position);

        // 거리 멀면 추종
        if (currentDistance > distance + 1f)
        {
            Vector2 direction = (playerPos.Position - myPos.Position).normalized;
            SetMovementDirection(entityId, direction);
        }
        // 거리 가까우면 멈춤
        else if (currentDistance < distance - 1f)
        {
            SetMovementDirection(entityId, Vector2.zero);
        }
    }

    private void AttackNearbyEnemies(int entityId, bool aggressive = false, bool onlyIfClose = false)
    {
        // AIPerceptionComponent 활용하여 적 탐지
        if (AR.s.Component.TryGetComponent<AIPerceptionComponent>(entityId, out var perception))
        {
            if (perception.TargetEntityId > 0)
            {
                // 적과의 거리 체크
                float distance = GetDistance(entityId, perception.TargetEntityId);
                float attackRange = aggressive ? 10f : 5f;

                if (onlyIfClose)
                    attackRange = 3f;

                if (distance <= attackRange)
                {
                    // 공격 실행
                    ExecuteAttack(entityId, perception.TargetEntityId);
                }
            }
        }
    }
}
```

---

## 5. 관계도 기반 보너스

### 보너스 계산
```csharp
// 호감도 보너스
float affinityBonus = Mathf.Clamp(relationship.Affinity / 100f, 0f, 1f);

// 신뢰도 보너스
float trustBonus = Mathf.Clamp(relationship.Trust / 100f, 0f, 1f);

// 친밀도 보너스
float intimacyBonus = Mathf.Clamp(relationship.Intimacy / 100f, 0f, 1f);

// 최종 보너스
companionBonus.DamageBonus = 10f + (affinityBonus * 20f);      // 10~30% 증가
companionBonus.DefenseBonus = 5f + (trustBonus * 15f);         // 5~20% 증가
companionBonus.ExpBonus = intimacyBonus * 50f;                  // 0~50% 증가

// 특수 능력 해금 (친밀도 80 이상)
companionBonus.HasSpecialAbility = (relationship.Intimacy >= 80);
```

### 적용 시점
- **동료 영입 시**: 초기 보너스 계산
- **관계도 변경 시**: 보너스 재계산
- **전투 중**: 스탯에 실시간 반영

---

## 6. 동료 충성도 & 이탈

### 충성도 변동
```csharp
// 증가 요인
+ 전투 승리: +2
+ 플레이어가 위험에서 구해줌: +5
+ 선물 주기: +3~10
+ 함께 퀘스트 완료: +5

// 감소 요인
- 전투에서 동료 HP 0 도달: -10
- 동료 무시 (오래 방치): -1/일
- 플레이어가 동료 공격: -20
- 동료의 개인 목표 무시: -5
```

### 이탈 조건
```
Loyalty < 20 AND (전투 중 사망 OR 플레이어 배신 행동)
```

### 이탈 시
1. **경고 메시지**: "○○○이(가) 당신을 더 이상 믿을 수 없다고 말합니다."
2. **파티 탈퇴**: CompanionComponent 제거
3. **관계도 대폭 감소**: Affinity -50, Trust -80
4. **마을로 귀환**: NPC는 마을로 돌아가지만 적대적

---

## 7. 직업별 특수 능력

### 전사 (Warrior)
- **패시브**: 방어력 +20%
- **액티브**: "방패 올리기" - 5초간 데미지 50% 감소, 쿨타임 15초

### 궁수 (Archer)
- **패시브**: 공격 사거리 +30%
- **액티브**: "집중 사격" - 3연사, 쿨타임 10초

### 대장장이 (Blacksmith)
- **패시브**: 플레이어 장비 내구도 감소 -50%
- **액티브**: "현장 수리" - 던전에서 장비 수리 가능, 쿨타임 30초

### 학자 (Scholar)
- **패시브**: 경험치 획득 +15%
- **액티브**: "지식의 축복" - 파티 전체 스킬 데미지 +30%, 10초 지속, 쿨타임 20초

### 사냥꾼 (Hunter)
- **패시브**: 이동 속도 +10%
- **액티브**: "함정 설치" - 적 이동 속도 감소 및 데미지, 쿨타임 15초

### 상인 (Merchant)
- **패시브**: 아이템 드랍률 +20%
- **액티브**: "행운의 주화" - 다음 전투에서 골드 2배, 쿨타임 60초

### 약초사 (Herbalist)
- **패시브**: HP 자동 재생 +50%
- **액티브**: "치유의 향기" - 파티 전체 HP 30% 회복, 쿨타임 25초

---

## 8. 동료 성장

### 경험치 & 레벨
- **경험치 획득**: 플레이어와 동일 (50% 분배)
- **레벨업**: 플레이어와 독립적으로 성장
- **레벨 차이**: 플레이어보다 5레벨 이상 낮으면 경험치 보정 (+50%)

### 장비
- **동료 전용 슬롯**: 무기, 방어구, 악세서리 각 1개
- **장비 착용**: 플레이어가 인벤토리에서 지급
- **내구도**: 전투 중 감소, 대장장이가 있으면 자동 수리

### 스킬 포인트
- **레벨업 시 획득**: 1포인트/레벨
- **스킬 트리**: 직업별 3가지 스킬 (패시브/액티브)
- **리셋**: 마을의 학자에게 골드 지불

---

## 9. 동료 UI

### 파티 UI (HUD)
```
┌─────────────────────────────────┐
│ [플레이어 HP바] [Lv 15]         │
│ ─────────────────────────────── │
│ [동료1 HP바] [Lv 13] [명령: 공격]│
│ [동료2 HP바] [Lv 12] [명령: 추종]│
└─────────────────────────────────┘
```

### 동료 관리 창 (마을)
```
┌──────────────────────────────────────┐
│  동료: ○○○ (대장장이, Lv 13)        │
│  ────────────────────────────────── │
│  관계:                              │
│    호감도: 75/100 ████████░░        │
│    신뢰도: 60/100 ██████░░░░        │
│    친밀도: 40/100 ████░░░░░░        │
│  ────────────────────────────────── │
│  보너스:                            │
│    데미지: +20%                     │
│    방어력: +12%                     │
│    경험치: +20%                     │
│  ────────────────────────────────── │
│  장비:                              │
│    [무기] [방어구] [악세서리]       │
│  ────────────────────────────────── │
│  특수 능력: 현장 수리 (쿨타임 30초) │
│  ────────────────────────────────── │
│  [스킬 트리] [장비 변경] [파티 해체]│
└──────────────────────────────────────┘
```

### 명령 휠 (전투 중)
```
        [공격]
          ↑
  [방어] ← ● → [추종]
          ↓
        [대기]
```
- **단축키**: Tab 키 or 마우스 휠

---

## 10. 동료 이벤트

### 동료 전용 대화
- **휴식 시**: 캠프파이어 대화 (친밀도 +1)
- **전투 후**: "잘 싸웠어요!" (사기 +5)
- **레벨업**: "더 강해진 기분이에요!" (호감도 +2)

### 동료 퀘스트
- **개인 목표 달성**: 동료의 개인 목표 도와주기
  - 예: "희귀한 광석을 찾고 싶어요" (대장장이)
  - 보상: 친밀도 +20, 특별 장비 제작

### 관계 이벤트
- **친밀도 50**: 첫 번째 깊은 대화 (과거 이야기)
- **친밀도 80**: 특수 능력 해금
- **친밀도 100**: 최종 이벤트 (결혼/혈맹 등)

---

## 11. 다중 동료 시너지

### 2인 콤보
- **전사 + 궁수**: 탱킹 + 원거리 화력
- **대장장이 + 사냥꾼**: 현장 수리 + 자원 수집
- **학자 + 약초사**: 버프 + 힐 (생존력 극대화)

### 3인 콤보
- **밸런스 파티**: 전사 + 궁수 + 약초사 (탱킹/딜/힐)
- **딜러 파티**: 궁수 + 사냥꾼 + 학자 (높은 화력)
- **서포트 파티**: 대장장이 + 약초사 + 상인 (생존/파밍)

---

## 12. 구현 우선순위

### Phase 1: 기본 동료
- [ ] CompanionComponent 정의
- [ ] 동료 영입 로직 (관계도 체크)
- [ ] System_CompanionAI (추종 + 자동 공격)
- [ ] 동료 HP바 UI

### Phase 2: 동료 명령
- [ ] CompanionCommand 시스템
- [ ] 명령 UI (명령 휠)
- [ ] 명령별 AI 행동 구현

### Phase 3: 동료 성장
- [ ] 동료 레벨/경험치 시스템
- [ ] 동료 장비 시스템
- [ ] 스킬 트리 (직업별 3개)

### Phase 4: 관계 보너스
- [ ] CompanionBonusComponent
- [ ] 관계도 기반 보너스 계산
- [ ] 충성도 변동 로직
- [ ] 이탈 시스템

### Phase 5: 특수 능력
- [ ] 직업별 특수 능력 구현
- [ ] 특수 능력 UI
- [ ] 다중 동료 시너지 효과

### Phase 6: 동료 이벤트
- [ ] 동료 전용 대화
- [ ] 동료 퀘스트
- [ ] 친밀도 이벤트

---

## 13. 테스트 시나리오

### 테스트 1: 동료 영입
1. NPC와 호감도 50 이상 달성
2. "동료 요청" 상호작용 선택
3. 영입 판정 성공
4. NPC가 플레이어 파티에 합류
5. 파티 UI에 동료 HP바 표시

### 테스트 2: 동료 AI
1. 플레이어가 이동 시 동료가 자동 추종
2. 적 근처 시 동료가 자동 공격
3. 명령 변경 (Follow → Attack)
4. 동료 행동 변화 확인

### 테스트 3: 동료 충성도
1. 전투 중 동료 HP 0 도달
2. 충성도 -10 감소
3. 충성도 20 미만 시 이탈 경고
4. 다시 충성도 회복 시도

---

## 14. 밸런스 고려사항

### 동료 강도
- **동료 1명**: 플레이어 전투력의 40%
- **동료 2명**: 플레이어 전투력의 70% (각 35%)
- **동료 3명**: 플레이어 전투력의 90% (각 30%)

### 제한 사항
- **최대 동료 수**: 3명 (업그레이드 필요)
- **영입 쿨타임**: 1일 (실패 시)
- **동료 교체**: 마을에서만 가능

---

**Last Updated**: 2026-04-01
**Status**: 설계 완료, 구현 대기 중
**Dependencies**: 관계 시스템, AI 시스템, 전투 시스템
