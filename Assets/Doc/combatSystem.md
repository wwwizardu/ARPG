# 전투 시스템 설계 (Combat System)

## 개요
실시간 탑다운 액션 전투 시스템. 코어 키퍼 스타일의 빠른 템포 전투 + 스킬/장비 기반 성장.

---

## 1. 전투 구조

### 기본 원칙
- **실시간**: 60 FPS Update, 50 FPS FixedUpdate
- **탑다운 시점**: 2D 탑다운, 360도 회전 가능
- **히트박스 기반**: 충돌 감지 (Collider2D)
- **쿨다운 기반**: 스킬/공격마다 독립적인 쿨타임

### 전투 구성 요소
```
[입력] → [스킬 커맨드] → [스킬 시스템] → [히트 판정] → [데미지 계산] → [HP 감소]
   ↓                                                                           ↓
[이동]                                                                    [사망 처리]
```

---

## 2. 스킬 시스템 (이미 구현됨)

### 현재 구현 상태 ✅
- `System_Skill`: 스킬 실행 및 타이밍 관리
- `SkillComponent`: 스킬 기본 데이터
- `SkillStateComponent`: 스킬 실행 상태 (IsRunning, CurrentPhase)
- `SkillTimingComponent`: 타이밍 (ElapsedTime, PhaseDuration)
- `SkillCommandComponent`: 스킬 실행 명령
- `SkillTargetComponent`: 타겟 정보

### 스킬 페이즈
```csharp
public enum SkillPhase
{
    Ready,      // 준비 (0프레임)
    WindUp,     // 선딜레이 (공격 전 모션)
    Active,     // 활성 (실제 히트 판정)
    Recovery,   // 후딜레이 (공격 후 경직)
    Finished    // 종료
}
```

### 스킬 예시
```
[기본 공격]
WindUp: 0.1s → Active: 0.2s → Recovery: 0.3s
총 0.6초, 히트박스는 Active 구간에만 존재

[대시 스킬]
WindUp: 0.05s → Active: 0.3s (무적) → Recovery: 0.2s
총 0.55초, Active 구간 동안 빠른 이동 + 무적

[차징 스킬]
WindUp: 0.5~2.0s (차지 시간) → Active: 0.1s → Recovery: 0.5s
차지 시간에 따라 데미지 증가
```

---

## 3. 전투 스탯 (StatComponent)

### 공격 스탯
| 스탯 | 설명 | 기본값 |
|------|------|--------|
| AttackMin | 최소 공격력 | 10 |
| AttackMax | 최대 공격력 | 15 |
| AttackSpeed | 공격 속도 (쿨타임 감소 %) | 100 |
| CriticalChance | 치명타 확률 (%) | 5 |
| CriticalDamage | 치명타 데미지 배율 (%) | 150 |

### 방어 스탯
| 스탯 | 설명 | 기본값 |
|------|------|--------|
| Defense | 방어력 (데미지 감소) | 5 |
| Evasion | 회피율 (%) | 0 |
| BlockChance | 막기 확률 (%) | 0 |
| BlockReduction | 막기 데미지 감소 (%) | 50 |

### 생존 스탯
| 스탯 | 설명 | 기본값 |
|------|------|--------|
| MaxHp | 최대 체력 | 100 |
| CurrentHp | 현재 체력 | 100 |
| HpRegen | 초당 HP 재생 | 1 |
| MoveSpeed | 이동 속도 | 5.0 |

### 특수 스탯
| 스탯 | 설명 | 기본값 |
|------|------|--------|
| SkillDamage | 스킬 데미지 배율 (%) | 100 |
| CooldownReduction | 쿨타임 감소 (%) | 0 |
| LifeSteal | 생명력 흡수 (%) | 0 |
| Thorns | 반사 데미지 (고정값) | 0 |

---

## 4. 데미지 계산 공식

### 속성별 독립 계산 후 합산 방식
무기는 물리 + 속성 데미지를 동시에 가질 수 있음. 스킬 데미지는 스킬의 DamageType에 해당하는 속성에만 합산.

```
예시: 무기(물리 20-30, 화염 10-15), Fire 스킬(데미지 5-10)

1단계: 속성별 기본 데미지
  물리 = Random(FinalAttackMin, FinalAttackMax)
  화염 = Random(FinalFireAttackMin, FinalFireAttackMax)
  냉기/번개/독 = 각각 Random(Final[속성]AttackMin, Max)

2단계: 스킬 데미지를 해당 속성에 합산
  스킬 DamageType = Fire → 화염에만 합산
  화염 += Random(SkillDamageMin, SkillDamageMax)

3단계: 스킬 배율 (모든 속성에 동일)
  각 속성 × (SkillDamage / 100)

4단계: 치명타 (모든 속성에 동일)
  각 속성 × (CriticalDamage / 100)

5단계: 속성별 저항 감소 (공식: resistance / (resistance + 100))
  물리 × (1 - Defense / (Defense + 100))
  화염 × (1 - FireResist / (FireResist + 100))
  냉기 × (1 - IceResist / (IceResist + 100))
  번개 × (1 - LightningResist / (LightningResist + 100))
  독   × (1 - PoisonResist / (PoisonResist + 100))

6단계: 합산
  totalDamage = 물리 + 화염 + 냉기 + 번개 + 독

7단계: 회피/막기 (합산 후 적용)
  회피 → totalDamage = 0
  막기 → totalDamage × (1 - BlockReduction/100)

8단계: 최소 데미지 보장
  Max(totalDamage, 1)
```

### 상태이상 독립 판정
데미지가 존재하는 각 속성별로 독립적으로 상태이상 발동:
- 물리 > 0 → 출혈 판정 (BloodingRate%)
- 화염 > 0 → 점화 판정 (IgniteRate%)
- 냉기 > 0 → 냉기 자동 적용
- 독 > 0 → 중독 자동 적용
- 한 번의 공격으로 여러 상태이상이 동시에 걸릴 수 있음

### 저항 공식 참고
| 저항값 | 감소율 |
|--------|--------|
| 50 | 33.3% |
| 100 | 50.0% |
| 200 | 66.6% |
| 500 | 83.3% |

### 특수 효과
```csharp
// 생명력 흡수 (합산 데미지 기준)
float healAmount = totalDamage * (attacker.LifeSteal / 100f);

// 반사 데미지
if (target.Thorns > 0)
    attacker.CurrentHp -= target.Thorns;
```

---

## 5. 히트 판정 시스템

### 방식 1: Collider2D (현재 추천)
**장점**: Unity 물리 엔진 활용, 시각적 일치
**단점**: 약간의 성능 오버헤드

```csharp
// SkillComponent에 히트박스 정보 저장
public struct SkillComponent
{
    public int OwnerEntityId;
    public int SkillTableID;
    public Collider2D HitboxCollider; // 히트박스 (BoxCollider2D, CircleCollider2D 등)
}

// Active 페이즈에서 히트 체크
void OnSkillActive(int skillEntityId, SkillComponent skill)
{
    // 1. 히트박스 활성화
    skill.HitboxCollider.enabled = true;

    // 2. OverlapCollider로 히트된 엔티티 수집
    Collider2D[] hits = Physics2D.OverlapCollider(skill.HitboxCollider, contactFilter);

    // 3. 각 히트된 엔티티에 데미지 처리
    foreach (var hit in hits)
    {
        int targetEntityId = hit.GetComponent<EntityData>().EntityId;
        ApplyDamage(skill.OwnerEntityId, targetEntityId, skill.SkillTableID);
    }

    // 4. 히트박스 비활성화 (Recovery 페이즈)
    skill.HitboxCollider.enabled = false;
}
```

### 방식 2: 수동 히트 체크 (최적화)
**장점**: 높은 성능
**단점**: 구현 복잡도 증가

```csharp
// ComponentManager에서 적 엔티티들 순회
SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
for (int i = 0; i < transformPool.Count; i++)
{
    int targetEntityId = transformPool.GetEntityId(i);

    // 적 태그 확인
    if (!cm.HasComponent<MonsterTag>(targetEntityId))
        continue;

    // 거리 체크 (SqrMagnitude 사용)
    TransformComponent targetPos = transformPool.GetByIndex(i);
    float sqrDistance = (targetPos.Position - skillOrigin).sqrMagnitude;

    if (sqrDistance <= skillRangeSqr)
    {
        ApplyDamage(attackerEntityId, targetEntityId, skillTableID);
    }
}
```

---

## 6. 전투 상태 (StateComponent)

### 전투 관련 상태
```csharp
public enum CharacterState
{
    Idle,           // 대기
    Move,           // 이동
    Attack,         // 공격 (스킬 사용 중)
    Hit,            // 피격 (넉백, 경직)
    Dash,           // 회피/대시 (무적)
    Dead,           // 사망
    Stun,           // 기절 (행동 불가)
    Channeling      // 차징/채널링
}
```

### 상태 전환 규칙
```
Idle ←→ Move
  ↓      ↓
Attack (스킬 사용)
  ↓
Hit (피격 시)
  ↓
Idle (복구)

특수 상태:
- Dash: 무적, 이동 가능, 공격 불가
- Stun: 모든 행동 불가
- Dead: 게임오버 또는 리스폰
```

---

## 7. AI 전투 로직 (System_AI_Behavior)

### AI 상태
```csharp
public enum AIState
{
    Idle,       // 대기 (순찰)
    Chase,      // 추격
    Attack,     // 공격
    Retreat,    // 후퇴 (HP 낮음)
    Dead        // 사망
}
```

### AI 행동 트리 (간소화)
```
Root
├─ Selector (우선순위 높은 순)
│   ├─ Sequence: [HP < 30%] → Retreat (후퇴)
│   ├─ Sequence: [Target in Range] → Attack (공격)
│   ├─ Sequence: [Target Visible] → Chase (추격)
│   └─ Idle (대기/순찰)
```

### 구현 예시
```csharp
public void OnFixedUpdate(float deltaTime)
{
    SparseSet<AIComponent> aiPool = cm.GetComponentPool<AIComponent>();

    for (int i = 0; i < aiPool.Count; i++)
    {
        int entityId = aiPool.GetEntityId(i);
        AIComponent ai = aiPool.GetByIndex(i);

        // 1. HP 체크
        if (cm.TryGetComponent<StatComponent>(entityId, out var stat))
        {
            if (stat.CurrentHp < stat.MaxHp * 0.3f)
            {
                ai.State = AIState.Retreat;
                // 도망 로직
                continue;
            }
        }

        // 2. 타겟 감지 (AIPerceptionComponent)
        if (cm.TryGetComponent<AIPerceptionComponent>(entityId, out var perception))
        {
            if (perception.TargetEntityId > 0)
            {
                float distance = GetDistance(entityId, perception.TargetEntityId);

                // 3. 공격 범위 내
                if (distance <= ai.AttackRange)
                {
                    ai.State = AIState.Attack;
                    ExecuteAIAttack(entityId, perception.TargetEntityId);
                }
                // 4. 시야 내
                else if (distance <= ai.SightRange)
                {
                    ai.State = AIState.Chase;
                    MoveTowardsTarget(entityId, perception.TargetEntityId);
                }
            }
            else
            {
                ai.State = AIState.Idle;
            }
        }

        aiPool.SetByIndex(i, ai);
    }
}
```

---

## 8. 동료 전투 (Companion Combat)

### 동료 AI 특징
- **플레이어 추종**: 일정 거리 유지
- **자동 전투**: 근처 적 자동 공격
- **명령 시스템**: 공격/대기/후퇴 명령

### 동료 컴포넌트
```csharp
public struct CompanionComponent
{
    public int PlayerEntityId;      // 주인 (플레이어)
    public float FollowDistance;    // 추종 거리 (2~5 유닛)
    public CompanionCommand Command; // 명령 (Attack/Hold/Retreat)
    public bool AutoAttack;         // 자동 공격 활성화
}

public enum CompanionCommand
{
    Follow,     // 추종 (기본)
    Attack,     // 공격 우선
    Hold,       // 제자리 대기
    Retreat     // 플레이어 근처로 후퇴
}
```

### 동료 행동 우선순위
```
1. Command 명령 처리
2. AutoAttack이고 근처 적 있으면 → 공격
3. 플레이어와 거리 멀면 → 추종
4. 대기
```

---

## 9. 전투 이펙트

### VFX 트리거
- **스킬 사용**: WindUp 시작 시 이펙트 스폰
- **히트**: Active 페이즈 히트 시 이펙트
- **사망**: HP 0 도달 시 사망 이펙트

### 이펙트 시스템 (Unity VFX Graph 또는 Particle System)
```csharp
// SkillComponent에 VFX 프리팹 참조
public struct SkillComponent
{
    public GameObject WindUpVFX;    // 선딜 이펙트
    public GameObject HitVFX;       // 히트 이펙트
    public GameObject ProjectileVFX; // 발사체 (원거리)
}

// Active 페이즈에서 히트 이펙트 생성
void OnHit(Vector2 hitPosition)
{
    GameObject vfx = Instantiate(skill.HitVFX, hitPosition, Quaternion.identity);
    Destroy(vfx, 1f); // 1초 후 삭제
}
```

---

## 10. 구현 우선순위

### Phase 1: 기본 전투 ✅ (일부 완료)
- [X] 스킬 시스템 (System_Skill)
- [X] 스탯 시스템 (StatComponent)
- [ ] 데미지 계산 로직
- [ ] 히트 판정 (Collider2D 방식)
- [ ] HP 감소 및 사망 처리

### Phase 2: AI 전투
- [ ] AI 상태 머신 (Idle/Chase/Attack)
- [ ] AI 타겟 감지 (System_AI_Perception 개선)
- [ ] AI 스킬 사용 로직
- [ ] 몬스터 종류별 AI 패턴

### Phase 3: 동료 전투
- [ ] CompanionComponent 정의
- [ ] 동료 AI (추종 + 자동 공격)
- [ ] 동료 명령 시스템
- [ ] 동료 UI (HP바, 명령 버튼)

### Phase 4: 전투 피드백
- [ ] VFX 통합 (히트, 스킬 이펙트)
- [ ] 사운드 (공격, 피격, 사망)
- [ ] 히트 스톱 (타격감)
- [ ] 카메라 쉐이크

### Phase 5: 고급 전투
- [ ] 넉백 시스템
- [ ] 콤보 시스템
- [ ] 원거리 스킬 (발사체)
- [ ] 범위 스킬 (AoE)
- [ ] 상태이상 (독, 화상, 빙결 등)

---

## 11. 테스트 시나리오

### 테스트 1: 기본 공격
1. 플레이어가 기본 공격 스킬 사용
2. WindUp → Active → Recovery 페이즈 확인
3. 적 히트 시 데미지 적용 및 HP 감소
4. VFX 및 사운드 재생

### 테스트 2: AI 전투
1. 몬스터가 플레이어 감지
2. Chase 상태로 전환, 추격
3. 공격 범위 도달 시 Attack 상태
4. 스킬 사용 후 쿨타임 대기

### 테스트 3: 동료 전투
1. NPC를 동료로 영입
2. 플레이어 이동 시 자동 추종
3. 적 근처 시 자동 공격
4. 명령 변경 (Hold/Attack/Retreat)

---

## 12. 참고 자료

### 데미지 계산 밸런스
- **방어력 공식**: `Reduction = Defense / (Defense + 100)`
  - Defense 100 = 50% 감소
  - Defense 200 = 66.6% 감소
  - Defense 500 = 83.3% 감소

### 스킬 쿨타임
- 기본 공격: 0.6초 (약 100 DPS)
- 일반 스킬: 3~5초
- 궁극기: 10~15초

### AI 감지 거리
- 시야 범위 (SightRange): 10 유닛
- 공격 범위 (AttackRange): 2 유닛 (근접), 7 유닛 (원거리)

---

**Last Updated**: 2026-04-01
**Status**: 설계 완료, 구현 진행 중 (Phase 1 일부 완료)
