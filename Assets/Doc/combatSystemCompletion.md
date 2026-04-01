# 전투 시스템 완성 계획 (Combat System Completion Plan)

**작성일**: 2026-04-01
**현재 진행도**: 40%
**목표 진행도**: 100%
**예상 소요 시간**: 8~10일

---

## 목차
1. [현황 분석](#1-현황-분석)
2. [완성 로드맵](#2-완성-로드맵)
3. [구현 세부 계획](#3-구현-세부-계획)
4. [테스트 시나리오](#4-테스트-시나리오)
5. [리스크 관리](#5-리스크-관리)

---

## 1. 현황 분석

### ✅ 이미 구현된 기능 (40%)

#### System_Skill.cs - 스킬 실행 시스템
```
✅ 스킬 페이즈 관리 (Start → Process → End)
✅ 스킬 타이밍 시스템 (SkillTimingComponent)
✅ 스킬 커맨드 처리 (SkillCommandComponent)
✅ 히트 판정 로직:
   - GetEntitiesInSkillRange()
   - CheckCircleRangeEntities()
   - FindClosestEntity()
✅ 기본 데미지 적용:
   - ApplySkillEffectToEntity()
   - Random.Range(DamageMin, DamageMax)
   - HP 감소 처리
✅ 스킬 실행 타입:
   - Single / MultiHit
   - Channeling
   - Charge
   - Toggle
✅ 애니메이션 통합
✅ 출혈 버프 적용 (물리 데미지 시)
```

#### System_HpCheck.cs - HP 관리 시스템
```
✅ HP 체크 및 사망 처리
✅ HpDirtyTag 기반 변경 감지
✅ StateComponent 업데이트 (Dead)
✅ 스킬 중지 처리
✅ 드랍 아이템 처리
✅ DestroyTag 추가 (엔티티 제거)
✅ DeathMessage 전송
```

#### AI 시스템 (기본 구조)
```
✅ System_AI_Perception (타겟 감지)
✅ System_AI_Behavior (행동 실행)
✅ AIComponent, AIStateComponent
✅ AICanSeeTargetTag
```

### ❌ 미구현 기능 (60%)

#### Phase 1: 고급 데미지 계산 (20%)
- [ ] 치명타 시스템 (CriticalChance, CriticalDamage)
- [ ] 방어력 감소 공식 (Defense / (Defense + 100))
- [ ] 회피 판정 (Evasion)
- [ ] 막기 판정 (BlockChance, BlockReduction)
- [ ] 스킬 데미지 배율 (SkillDamage %)
- [ ] 생명력 흡수 (LifeSteal)
- [ ] 반사 데미지 (Thorns)

#### Phase 2: AI 전투 통합 (15%)
- [ ] AI 상태 머신 (Idle/Chase/Attack/Retreat)
- [ ] AI 스킬 사용 로직
- [ ] AI 공격 범위 체크
- [ ] AI 쿨타임 관리
- [ ] 몬스터 종류별 AI 패턴

#### Phase 3: 전투 피드백 (15%)
- [ ] VFX 통합:
  - 히트 이펙트
  - 사망 이펙트
  - 스킬 시전 이펙트
- [ ] 사운드 통합:
  - 공격 사운드
  - 피격 사운드
  - 사망 사운드
- [ ] 히트 스톱 (타격감)
- [ ] 데미지 텍스트 표시

#### Phase 4: 고급 전투 기능 (10%)
- [ ] 넉백 시스템
- [ ] 상태이상 (독, 화상, 빙결)
- [ ] 발사체 스킬 (Projectile)
- [ ] 범위 스킬 (AoE) 개선
- [ ] 콤보 시스템

---

## 2. 완성 로드맵

### Day 1-2: Phase 1 - 고급 데미지 계산 ✨
**목표**: 완전한 데미지 공식 구현

```
[기존 코드]
int damage = Random.Range(skill.Table.DamageMin, skill.Table.DamageMax + 1);

[개선 코드]
DamageResult result = DamageCalculator.Calculate(attackerId, targetId, skillData);
- 치명타 판정 ✓
- 방어력 감소 ✓
- 회피/막기 ✓
- 스탯 배율 ✓
- 최종 데미지 ✓
```

**작업 내용**:
1. `DamageCalculator.cs` 생성
   - Calculate(attackerId, targetId, skillData) 메서드
   - 단계별 데미지 계산 (6단계)
   - DamageResult 구조체 반환
2. `System_Skill.cs` 수정
   - ApplySkillEffectToEntity() 개선
   - DamageCalculator 호출로 변경
3. 특수 효과 구현
   - LifeSteal (생명력 흡수)
   - Thorns (반사 데미지)
4. DamageMessage 확장
   - IsCritical 플래그
   - DamageType 구분

**예상 시간**: 2일

---

### Day 3-4: Phase 2 - AI 전투 통합 🤖
**목표**: 몬스터가 플레이어를 추격하고 공격

```
[AI 상태 흐름]
Idle (순찰)
  ↓ (플레이어 감지)
Chase (추격)
  ↓ (공격 범위 진입)
Attack (스킬 사용)
  ↓ (HP < 30%)
Retreat (후퇴)
```

**작업 내용**:
1. AIState enum 정의
   - Idle, Chase, Attack, Retreat, Dead
2. `System_AI_Behavior.cs` 개선
   - 상태 전환 로직
   - Chase: 타겟 추격 (MovementComponent 조작)
   - Attack: 스킬 커맨드 발행
   - Retreat: HP 낮을 때 도망
3. AI 스킬 사용
   - SkillCommandComponent 생성
   - 쿨타임 관리 (AIAttackCooldown)
   - 공격 범위 체크 (AIAttackRange)
4. 몬스터 AI 데이터
   - AIBehaviorData ScriptableObject
   - 몬스터별 공격 패턴

**예상 시간**: 2일

---

### Day 5-6: Phase 3 - 전투 피드백 💥
**목표**: 타격감 있는 전투 연출

**작업 내용**:
1. VFX 시스템
   - VFXManager 생성
   - Particle System 풀링
   - 스킬별 이펙트 프리팹 설정
   - 히트 위치에 이펙트 스폰
2. 사운드 시스템
   - AudioManager 생성
   - SFX 풀링
   - 스킬별 사운드 재생
3. 히트 스톱
   - Time.timeScale 조작 (0.1초)
   - 강한 공격 시 느린 모션 연출
4. 데미지 텍스트
   - DamageText UI
   - 데미지 숫자 팝업
   - 치명타 시 빨간색 강조
5. 카메라 쉐이크 (옵션)
   - Cinemachine Impulse
   - 강한 타격 시 화면 흔들림

**예상 시간**: 2일

---

### Day 7-8: Phase 4 - 고급 전투 기능 🎯
**목표**: 넉백, 상태이상, 발사체

**작업 내용**:
1. 넉백 시스템
   - KnockbackComponent 추가
   - System_Knockback 생성
   - 히트 시 밀려남 효과
2. 상태이상 시스템
   - StatusEffectComponent
   - System_StatusEffect
   - 독 (DoT 데미지)
   - 화상 (DoT + 방어력 감소)
   - 빙결 (이동 속도 감소)
3. 발사체 스킬
   - ProjectileComponent
   - System_Projectile
   - 직선 발사, 포물선 발사
   - 충돌 감지 및 데미지
4. 범위 스킬 개선
   - 부채꼴 AoE 정확도 개선
   - 원형 AoE 최적화
   - 다단 히트 지원

**예상 시간**: 2일

---

### Day 9-10: 테스트 & 밸런싱 🧪
**목표**: 버그 수정 및 전투 밸런싱

**작업 내용**:
1. 통합 테스트
   - 전투 시나리오 테스트
   - AI 행동 검증
   - 버그 수정
2. 밸런싱
   - 데미지 수치 조정
   - HP/방어력 조정
   - 스킬 쿨타임 조정
3. 성능 최적화
   - 히트 판정 최적화
   - VFX 풀링 개선
   - 프로파일링
4. 문서 업데이트
   - combatSystem.md 업데이트
   - implementationStatus.md 진행도 변경

**예상 시간**: 2일

---

## 3. 구현 세부 계획

### 3.1 DamageCalculator.cs 구조

```csharp
namespace ARPG.Combat
{
    /// <summary>
    /// 데미지 계산 결과
    /// </summary>
    public struct DamageResult
    {
        public float FinalDamage;       // 최종 데미지
        public bool IsCritical;         // 치명타 여부
        public bool IsEvaded;           // 회피 여부
        public bool IsBlocked;          // 막기 여부
        public float LifeStealAmount;   // 흡혈량
        public float ThornsDamage;      // 반사 데미지
        public GlobalEnum.DamageType DamageType; // 데미지 타입
    }

    /// <summary>
    /// 데미지 계산 유틸리티
    /// combatSystem.md의 공식을 정확히 구현
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// 데미지 계산 메인 메서드
        /// </summary>
        public static DamageResult Calculate(
            int attackerId,
            int targetId,
            SkillTableData skillData)
        {
            DamageResult result = new DamageResult();

            // 공격자/타겟 스탯 가져오기
            if (!AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat))
                return result;
            if (!AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat))
                return result;

            // 1단계: 기본 데미지 (랜덤)
            float baseDamage = UnityEngine.Random.Range(
                attackerStat.FinalAttackMin,
                attackerStat.FinalAttackMax
            );

            // 2단계: 스킬 배율 적용
            float skillMultiplier = skillData.DamageMultiplier;
            float skillDamage = baseDamage * skillMultiplier * (attackerStat.FinalSkillDamage / 100f);

            // 3단계: 치명타 판정
            bool isCrit = UnityEngine.Random.Range(0f, 100f) < attackerStat.FinalCriticalChance;
            if (isCrit)
            {
                skillDamage *= (attackerStat.FinalCriticalDamage / 100f);
                result.IsCritical = true;
            }

            // 4단계: 방어력 감소
            float defense = targetStat.FinalDefense;
            float damageReduction = defense / (defense + 100f);
            float finalDamage = skillDamage * (1f - damageReduction);

            // 5단계: 회피/막기 판정
            if (UnityEngine.Random.Range(0f, 100f) < targetStat.FinalEvasion)
            {
                result.IsEvaded = true;
                result.FinalDamage = 0;
                return result;
            }

            if (UnityEngine.Random.Range(0f, 100f) < targetStat.FinalBlockChance)
            {
                result.IsBlocked = true;
                finalDamage *= (1f - targetStat.FinalBlockReduction / 100f);
            }

            // 6단계: 최소 데미지 보장
            finalDamage = Mathf.Max(finalDamage, 1f);
            result.FinalDamage = finalDamage;
            result.DamageType = skillData.DamageType;

            // 특수 효과
            result.LifeStealAmount = finalDamage * (attackerStat.FinalLifeSteal / 100f);
            result.ThornsDamage = targetStat.FinalThorns;

            return result;
        }

        /// <summary>
        /// 데미지 결과를 엔티티에 적용
        /// </summary>
        public static void ApplyDamageResult(
            int attackerId,
            int targetId,
            DamageResult result)
        {
            // 회피 시 데미지 없음
            if (result.IsEvaded)
            {
                // TODO: 회피 이펙트/사운드
                Debug.Log($"[DamageCalculator] Evaded! TargetId: {targetId}");
                return;
            }

            // HP 감소
            if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat))
            {
                int newHp = Mathf.Max(0, targetStat.CurrentHp - Mathf.RoundToInt(result.FinalDamage));
                targetStat.SetCurrentHp(targetId, newHp);
                AR.s.Component.SetComponent(targetId, targetStat);

                // HpDirtyTag 추가 (System_HpCheck에서 처리)
                AR.s.Component.AddComponent(targetId, new HpDirtyTag());
            }

            // 생명력 흡수
            if (result.LifeStealAmount > 0f &&
                AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat))
            {
                int healAmount = Mathf.RoundToInt(result.LifeStealAmount);
                int newHp = Mathf.Min(attackerStat.FinalMaxHp, attackerStat.CurrentHp + healAmount);
                attackerStat.SetCurrentHp(attackerId, newHp);
                AR.s.Component.SetComponent(attackerId, attackerStat);

                Debug.Log($"[DamageCalculator] Life Steal: {healAmount} HP restored");
            }

            // 반사 데미지
            if (result.ThornsDamage > 0f &&
                AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var reflectStat))
            {
                int reflectDamage = Mathf.RoundToInt(result.ThornsDamage);
                int newHp = Mathf.Max(0, reflectStat.CurrentHp - reflectDamage);
                reflectStat.SetCurrentHp(attackerId, newHp);
                AR.s.Component.SetComponent(attackerId, reflectStat);
                AR.s.Component.AddComponent(attackerId, new HpDirtyTag());

                Debug.Log($"[DamageCalculator] Thorns: {reflectDamage} damage reflected");
            }

            // 데미지 메시지 전송
            AR.s.Message.SendToEntity(new Message.DamageMessage
            {
                TargetEntityId = targetId,
                DamageAmount = Mathf.RoundToInt(result.FinalDamage),
                AttackerEntityId = attackerId,
                DamageType = result.DamageType,
                IsCritical = result.IsCritical,
                CurrentHp = targetStat.CurrentHp,
                MaxHp = targetStat.FinalMaxHp
            });

            Debug.Log($"[DamageCalculator] Damage Applied - Target: {targetId}, Damage: {result.FinalDamage:F1}, Critical: {result.IsCritical}, Blocked: {result.IsBlocked}");
        }
    }
}
```

### 3.2 System_Skill.cs 수정 사항

```csharp
// 기존 코드 (line 683-687)
int damage = UnityEngine.Random.Range(skill.Table.DamageMin, skill.Table.DamageMax + 1);
int newHp = Mathf.Max(0, targetStat.CurrentHp - damage);
targetStat.SetCurrentHp(targetEntityId, newHp);
AR.s.Component.SetComponent(targetEntityId, targetStat);

// 새 코드 (DamageCalculator 사용)
DamageResult result = DamageCalculator.Calculate(skill.OwnerEntityId, targetEntityId, skill.Table);
DamageCalculator.ApplyDamageResult(skill.OwnerEntityId, targetEntityId, result);
```

### 3.3 AI 상태 머신 구조

```csharp
// AIStateComponent.cs
public struct AIStateComponent
{
    public AIState CurrentState;
    public AIState PreviousState;
    public float StateTimer;        // 현재 상태 유지 시간
    public float LastAttackTime;    // 마지막 공격 시간 (쿨타임용)
}

public enum AIState
{
    Idle,       // 대기 (순찰)
    Chase,      // 추격
    Attack,     // 공격
    Retreat,    // 후퇴
    Dead        // 사망
}

// System_AI_Behavior.cs 개선
public void OnFixedUpdate(float inFixedDeltaTime)
{
    SparseSet<AIStateComponent> statePool = AR.s.Component.GetComponentPool<AIStateComponent>();

    for (int i = 0; i < statePool.Count; i++)
    {
        int entityId = statePool.GetEntityId(i);
        AIStateComponent aiState = statePool.GetByIndex(i);

        // AI 로직 실행
        UpdateAIState(entityId, ref aiState, inFixedDeltaTime);

        // 상태 저장
        AR.s.Component.SetComponent(entityId, aiState);
    }
}

private void UpdateAIState(int entityId, ref AIStateComponent aiState, float deltaTime)
{
    // 상태 타이머 증가
    aiState.StateTimer += deltaTime;

    // 사망 확인
    if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
    {
        if (state.Condition == CharacterConditions.Dead)
        {
            aiState.CurrentState = AIState.Dead;
            return;
        }
    }

    // HP 체크 - 낮으면 후퇴
    if (AR.s.Component.TryGetComponent<StatComponent>(entityId, out var stat))
    {
        if (stat.CurrentHp < stat.FinalMaxHp * 0.3f && aiState.CurrentState != AIState.Retreat)
        {
            ChangeState(ref aiState, AIState.Retreat);
        }
    }

    // 타겟 확인
    if (!AR.s.Component.TryGetComponent<AIComponent>(entityId, out var ai))
        return;

    bool hasTarget = ai.TargetEntityId > 0 &&
                     AR.s.Component.HasComponent<TransformComponent>(ai.TargetEntityId);

    // 상태별 처리
    switch (aiState.CurrentState)
    {
        case AIState.Idle:
            ProcessIdleState(entityId, ref aiState, ref ai, hasTarget);
            break;

        case AIState.Chase:
            ProcessChaseState(entityId, ref aiState, ref ai, hasTarget);
            break;

        case AIState.Attack:
            ProcessAttackState(entityId, ref aiState, ref ai, hasTarget);
            break;

        case AIState.Retreat:
            ProcessRetreatState(entityId, ref aiState, ref ai);
            break;
    }
}

private void ProcessAttackState(int entityId, ref AIStateComponent aiState, ref AIComponent ai, bool hasTarget)
{
    if (!hasTarget)
    {
        ChangeState(ref aiState, AIState.Idle);
        return;
    }

    // 타겟과의 거리 체크
    float distance = GetDistanceToTarget(entityId, ai.TargetEntityId);

    // 공격 범위 밖이면 추격
    if (distance > ai.AttackRange)
    {
        ChangeState(ref aiState, AIState.Chase);
        return;
    }

    // 쿨타임 체크
    float timeSinceLastAttack = Time.time - aiState.LastAttackTime;
    if (timeSinceLastAttack < ai.AttackCooldown)
        return;

    // 스킬 사용
    UseAISkill(entityId, ai.TargetEntityId);
    aiState.LastAttackTime = Time.time;
}

private void UseAISkill(int entityId, int targetEntityId)
{
    // 스킬 엔티티 ID 가져오기 (슬롯 0 = 기본 공격)
    int skillEntityId = EntityIdHelper.GetDeterministicId(entityId, EntityIdCategory.Skill, 0);
    if (skillEntityId == -1)
        return;

    // 스킬 컴포넌트 확인
    if (!AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
        return;

    // 타겟 위치 가져오기
    if (!AR.s.Component.TryGetComponent<TransformComponent>(targetEntityId, out var targetTransform))
        return;

    // 스킬 커맨드 생성
    SkillCommandComponent command = new SkillCommandComponent();
    command.SkillEntityId = skillEntityId;
    command.TargetType = skill.Table.SkillTargetType;
    command.TargetPosition = targetTransform.Position;

    // 커맨드 추가 (System_Skill에서 처리)
    AR.s.Component.AddComponent(entityId, command);

    Debug.Log($"[AI] Skill used - EntityId: {entityId}, SkillId: {skill.SkillId}, Target: {targetEntityId}");
}
```

---

## 4. 테스트 시나리오

### 시나리오 1: 기본 전투
```
1. 플레이어가 슬라임 공격
2. 데미지 계산 확인 (정상 데미지)
3. 슬라임 HP 감소 확인
4. 슬라임 사망 시 드랍 아이템 확인
```

### 시나리오 2: 치명타 & 방어력
```
1. 플레이어 치명타 확률 50%로 설정
2. 슬라임 공격 10회
3. 약 5회 치명타 발생 확인
4. 치명타 데미지가 일반 데미지의 1.5배 확인
5. 방어력 100인 적 공격
6. 데미지가 약 50% 감소 확인
```

### 시나리오 3: 회피 & 막기
```
1. 플레이어 회피율 30%로 설정
2. 슬라임에게 피격 10회
3. 약 3회 회피 확인
4. 막기 확률 20%로 설정
5. 피격 시 데미지 50% 감소 확인
```

### 시나리오 4: AI 전투
```
1. 플레이어가 고블린 근처로 이동
2. 고블린이 플레이어 감지 (Chase 상태)
3. 고블린이 공격 범위 진입 (Attack 상태)
4. 고블린이 스킬 사용 확인
5. 고블린 HP 30% 이하 시 후퇴 확인
```

### 시나리오 5: 생명력 흡수 & 반사
```
1. 플레이어 생명력 흡수 20%로 설정
2. 슬라임 공격 (100 데미지)
3. 플레이어 HP 20 회복 확인
4. 슬라임 반사 데미지 10으로 설정
5. 공격 시 플레이어도 10 데미지 받는지 확인
```

### 시나리오 6: VFX & 사운드
```
1. 플레이어 공격 시 이펙트 표시 확인
2. 히트 시 이펙트 표시 확인
3. 사망 시 이펙트 표시 확인
4. 각 상황에서 사운드 재생 확인
```

---

## 5. 리스크 관리

### Risk 1: 데미지 계산 복잡도
**문제**: 6단계 데미지 계산 로직이 복잡하여 버그 발생 가능
**대응**:
- 단계별 유닛 테스트 작성
- 각 단계의 중간값 로그 출력
- 기획 문서의 공식과 일치하는지 검증

### Risk 2: AI 성능
**문제**: 모든 AI가 매 프레임 업데이트하면 성능 저하
**대응**:
- UpdateInterval 사용 (0.1초마다 업데이트)
- 화면 밖 AI는 업데이트 주기 증가 (0.5초)
- 프로파일러로 성능 측정

### Risk 3: VFX 오버헤드
**문제**: 많은 히트 이펙트가 동시에 생성되면 성능 저하
**대응**:
- Particle System 오브젝트 풀링
- 이펙트 생성 개수 제한 (최대 20개)
- 이펙트 라이프타임 단축 (0.5초)

### Risk 4: 히트 판정 정확도
**문제**: Collider2D 기반 히트 판정이 부정확할 수 있음
**대응**:
- Debug.DrawLine으로 히트박스 시각화
- 히트 판정 로그 상세히 출력
- 필요시 수동 거리 체크로 보완

### Risk 5: 밸런싱
**문제**: 데미지/HP/방어력 수치 밸런싱 어려움
**대응**:
- 모든 수치를 ScriptableObject/Table로 관리
- 치트 시스템으로 실시간 수정 가능하도록
- 플레이 테스트 반복

---

## 6. 체크리스트

### Day 1-2: 고급 데미지 계산
- [ ] DamageCalculator.cs 생성
- [ ] DamageResult 구조체 정의
- [ ] Calculate() 메서드 구현
  - [ ] 1단계: 기본 데미지
  - [ ] 2단계: 스킬 배율
  - [ ] 3단계: 치명타
  - [ ] 4단계: 방어력
  - [ ] 5단계: 회피/막기
  - [ ] 6단계: 최소 데미지
- [ ] ApplyDamageResult() 메서드 구현
- [ ] 생명력 흡수 로직
- [ ] 반사 데미지 로직
- [ ] System_Skill.cs 수정
- [ ] DamageMessage 확장
- [ ] 유닛 테스트 작성
- [ ] 실제 전투 테스트

### Day 3-4: AI 전투 통합
- [ ] AIState enum 정의
- [ ] AIStateComponent 수정
- [ ] System_AI_Behavior 개선
  - [ ] UpdateAIState() 메서드
  - [ ] ProcessIdleState()
  - [ ] ProcessChaseState()
  - [ ] ProcessAttackState()
  - [ ] ProcessRetreatState()
- [ ] UseAISkill() 메서드 구현
- [ ] AI 쿨타임 관리
- [ ] AI 공격 범위 체크
- [ ] 몬스터 AI 테스트

### Day 5-6: 전투 피드백
- [ ] VFXManager 생성
- [ ] Particle System 풀링
- [ ] 스킬별 이펙트 프리팹
- [ ] 히트 이펙트 스폰
- [ ] AudioManager 생성
- [ ] 사운드 풀링
- [ ] 스킬별 사운드 재생
- [ ] 히트 스톱 구현
- [ ] DamageText UI
- [ ] 카메라 쉐이크 (옵션)

### Day 7-8: 고급 전투 기능
- [ ] KnockbackComponent
- [ ] System_Knockback
- [ ] 넉백 효과 구현
- [ ] StatusEffectComponent
- [ ] System_StatusEffect
- [ ] 독 효과 구현
- [ ] 화상 효과 구현
- [ ] 빙결 효과 구현
- [ ] ProjectileComponent
- [ ] System_Projectile
- [ ] 발사체 이동 로직
- [ ] 발사체 충돌 감지
- [ ] 범위 스킬 개선

### Day 9-10: 테스트 & 밸런싱
- [ ] 시나리오 1 테스트 (기본 전투)
- [ ] 시나리오 2 테스트 (치명타 & 방어력)
- [ ] 시나리오 3 테스트 (회피 & 막기)
- [ ] 시나리오 4 테스트 (AI 전투)
- [ ] 시나리오 5 테스트 (생명력 흡수 & 반사)
- [ ] 시나리오 6 테스트 (VFX & 사운드)
- [ ] 버그 수정
- [ ] 데미지 수치 밸런싱
- [ ] HP/방어력 밸런싱
- [ ] 스킬 쿨타임 밸런싱
- [ ] 성능 프로파일링
- [ ] 문서 업데이트

---

## 7. 완료 조건

### 전투 시스템 완료 조건
- ✅ 모든 데미지 공식 구현 (치명타, 방어력, 회피, 막기)
- ✅ AI가 자율적으로 전투 (감지 → 추격 → 공격)
- ✅ VFX 및 사운드 통합
- ✅ 타격감 있는 전투 (히트 스톱, 카메라 쉐이크)
- ✅ 넉백 시스템
- ✅ 상태이상 시스템 (독, 화상, 빙결)
- ✅ 모든 테스트 시나리오 통과
- ✅ 버그 없음
- ✅ 문서 업데이트

### 진행도 업데이트
- 현재: 40% → 목표: 100%
- implementationStatus.md: 🚧 → ✅
- combatSystem.md: Phase 1 완료 표시

---

## 8. 참고 자료

### 기획 문서
- [combatSystem.md](./combatSystem.md) - 전투 시스템 설계
- [implementationStatus.md](./implementationStatus.md) - 구현 현황

### 기술 문서
- [CLAUDE.md](../CLAUDE.md) - 코딩 컨벤션

### 구현 파일
- `System_Skill.cs` - 스킬 실행 시스템 (이미 구현)
- `System_HpCheck.cs` - HP 관리 시스템 (이미 구현)
- `System_AI_Perception.cs` - AI 감지 시스템 (기본 구조)
- `System_AI_Behavior.cs` - AI 행동 시스템 (개선 필요)

### Unity API
- `Physics2D.OverlapCollider()` - 히트 판정
- `Particle System` - VFX
- `AudioSource` - 사운드
- `Time.timeScale` - 히트 스톱
- `Cinemachine` - 카메라 쉐이크

---

**작성자**: Claude (Game Planner Agent)
**최종 업데이트**: 2026-04-01
