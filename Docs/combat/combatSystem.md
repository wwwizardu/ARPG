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
| 스탯 | 현행 역할 |
|------|-----------|
| `Base/Final[Element]AttackMin/Max` | 캐릭터 기본 피해와 태그가 맞는 글로벌 추가 피해. 무기 Base 피해와는 별도다. |
| `FinalCriRate` | 캐릭터 기본 치명타와 글로벌 고정 치명타의 합계. 스킬 프로필에서 증가율 적용 전 값으로 사용한다. |
| `FinalCriRateIncrease` | 글로벌 치명타 확률 증가율 합계. |
| `FinalAttackSpeed` | 무기 APS와 분리된 글로벌 공격속도 증가율 합계. `0`이 기본이다. |
| `FinalCriDamage` | 기본 1.5배에 더하는 추가 치명타 피해율. |

### 장비 옵션 책임 경계

장착 부위는 일반 옵션의 롤 가능 여부만 제한하며 계산 경로를 바꾸지 않는다.

| 구분 | 저장 데이터 | 적용 규칙 |
|------|-------------|-----------|
| 무기 기본 능력 | `EquipmentBaseStatTable`에서 만든 `ModSlot.Base` | 무기 피해·기본 치명타·기본 APS의 원본이다. 무기 캐시에서만 소비하며 글로벌 Modifier로 등록하지 않는다. |
| 무기 고유 강화 | `EquipmentData.WeaponLocalEnhancements` | 개별 무기의 Base에만 적용하는 증가율이다. 일반 Mod 슬롯 및 글로벌 Modifier 경로를 사용하지 않는다. |
| 글로벌 장비 옵션 | 모든 Implicit/Prefix/Postfix | 무기·방어구·장신구 구분 없이 `StatModifier` 또는 `ModPoolComponent`에 등록한다. |

`ModTable.Tags=None`인 글로벌 옵션은 모든 스킬에 적용한다. 태그가 있으면 스킬이 요구 태그를 모두 가질 때만 적용한다. `AllowedEquipTypes`는 롤 가능한 장비 종류만 제한한다. `Group` 중복은 같은 `ModSlot` 안에서만 금지한다. 즉 Base는 Base끼리, Implicit은 Implicit끼리, Prefix는 Prefix끼리, Postfix는 Postfix끼리 검사하며 서로 다른 슬롯 사이의 같은 Group은 허용한다.

무기 캐시는 Base와 고유 강화만 읽는다. 고유 강화 값은 피해 5속성·치명타·공격속도의 증가율이며 종류별 합계를 Base에 한 번 곱한다.

```text
EnhancedWeaponStat
    = WeaponBaseStat * (1 + Sum(WeaponLocalIncrease) / 100)
```

강화 원본은 `WeaponLocalEnhancementInstance(StatType, Value)`로 저장한다. 재료 규칙용 `WeaponLocalEnhancementMaterialTable` 스키마는 `ItemId`, `StatType`, `MinValue`, `MaxValue`, `MaxTotal`, `Weight`를 가지며 무기 종류 제한은 두지 않는다. 실제 재료 데이터·테이블 등록·후보 롤·상한·소비 트랜잭션·제련 UI는 재료 추가 시 구현한다.

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

### 스킬 공격 프로필 구성

`System_SkillAttackProfileCalculation`이 무기·캐릭터 글로벌 Modifier·`SkillTable`·스킬 페이지 OnHit 보너스를 한 번 합산한다. 예상 피해, UI와 실제 타격은 같은 `SkillAttackProfileComponent`를 소비한다.

고정값은 먼저 합산하고, 같은 Stat의 증가율은 모두 더한 뒤 한 번만 곱한다. Override가 있으면 우선순위가 가장 높은 값을 마지막에 적용한다.

```text
FinalStat
    = (Base + Sum(GlobalFlat)) * (1 + Sum(GlobalIncreased) / 100)

10% 증가 + 20% 증가 = 30% 증가
```

Attack 스킬만 강화 반영 무기 피해를 더한다. 글로벌 추가 피해는 `ModTable.Tags`가 스킬 태그와 일치할 때 적용하며, 현재 비-Base 추가 피해 Mod는 `Attack` 태그를 사용한다. `SkillTable.DamageMin/Max`는 지정된 `DamageType`에, 스킬 페이지 보너스는 해당 속성에 더한다.

```text
ProfileElementDamage
    = EnhancedWeaponElementDamage       // Attack만
    + TagMatchedGlobalElementDamage
    + SkillTableElementDamage
    + SkillPageOnHitElementDamage
```

치명타 확률은 정수 반올림하지 않고 `float`를 유지한다. Attack은 강화 반영 무기 치명타를, Spell은 `SkillTable.BaseCriRate`를 기본값으로 사용한다.

```text
FinalCrit = Clamp(
    (SkillBaseCrit + CharacterBaseCrit + Sum(GlobalFlatCrit) + OnHitCrit)
    * (1 + Sum(GlobalCritIncrease) / 100),
    0, 100)
```

### 속성별 실제 타격 처리

무기는 물리와 여러 속성 피해를 동시에 가질 수 있다. 프로필의 속성별 범위에서 각각 난수를 뽑은 뒤 공통 배율과 치명타를 적용하고, 대상 방어를 적용한 뒤 합산한다.

```
예시: 무기(물리 20-30, 화염 10-15), Fire 스킬(데미지 5-10)

1단계: 프로필 속성별 범위에서 난수 선택
  물리 = Random(Profile.PhysAddedMin, Profile.PhysAddedMax)
  화염 = Random(Profile.FireAddedMin, Profile.FireAddedMax)
  냉기/번개/독도 같은 방식

2단계: 공통 배율
  각 속성 × Profile.BaseDamageMul × Profile.SkillDamageMul

3단계: 치명타 판정 성공 시
  각 속성 × Profile.CritMultiplier
  CritMultiplier = 1.5 + FinalCriDamage / 100

4단계: 속성별 방어 적용
  물리 감소율 = Armor / (Armor + 10 × 해당 물리 피해)
  원소 감소율 = Clamp(Resistance, 0, MaxResistance) / 100

5단계: 합산
  totalDamage = 물리 + 화염 + 냉기 + 번개 + 독

6단계: 회피/막기 (합산 후 적용)
  회피 → totalDamage = 0
  막기 → totalDamage × (1 - BlockReduction/100)

7단계: 최소 데미지 보장
  Max(totalDamage, 1)
```

### 상태이상 독립 판정
데미지가 존재하는 각 속성별로 독립적으로 상태이상 발동:
- 물리 > 0 → 출혈 판정 (BloodingRate%)
- 화염 > 0 → 점화 판정 (IgniteRate%)
- 냉기 > 0 → 냉기 자동 적용
- 독 > 0 → 중독 자동 적용
- 한 번의 공격으로 여러 상태이상이 동시에 걸릴 수 있음

### 방어 공식 참고

- 물리 방어는 들어오는 물리 피해에 따라 감소율이 달라지므로 매 타격 계산한다.
- 원소 저항은 단순 백분율이며 각 속성의 최대 저항과 하드 캡을 넘지 못한다.
- 대상의 원소 감소 배율·회피·막기는 `DamageDefenseCacheComponent`에 캐시한다.

### 특수 효과
```csharp
// 생명력 흡수 (합산 데미지 기준)
float healAmount = totalDamage * (attacker.LifeSteal / 100f);

// 반사 데미지
if (target.Thorns > 0)
    attacker.CurrentHp -= target.Thorns;
```

### 공격 속도 / 시전 속도
스킬 태그(SkillTag)에 따라 적용되는 속도 스탯이 다름.

#### Attack 태그 스킬

```text
EnhancedWeaponAPS
    = WeaponBaseAPS * (1 + Sum(WeaponLocalAttackSpeedIncrease) / 100)

ProfileAPS
    = EnhancedWeaponAPS
    * SkillTable.BaseAttackSpeedMul / 100
    * (1 + Sum(TagMatchedGlobalAttackSpeedIncrease) / 100)
```

- 무기가 없거나 APS가 0 이하면 계산용 무기 APS는 `1.0`을 사용한다.
- 최종 프로필 APS는 최소 `0.1`이다.
- 실제 Start/Process/End 기본 시간은 프로필의 `1 / ProfileAPS` 비율로 함께 조정한다.
- 일반 Implicit/Prefix/Postfix의 공격속도 증가는 무기에 붙어도 글로벌 증가 버킷에 들어간다.

#### Spell 태그 스킬
- 기본 시간 = `SkillTable.StartTime / ProcessTime / EndTime`
- 시전 속도 배율 = `(100 + FinalCastSpeed) / 100`을 `0.1~5.0`으로 제한
- 최종 Start/Process/End 시간 = 각 기본 시간 / 시전 속도 배율
- 공격 프로필의 `AttackSpeedPerSecond`는 `0`이며 무기 치명타·APS를 사용하지 않는다.

#### 공통
- 배율이 적용되면 Start/Process/End Duration이 모두 비율 유지하며 조절
- 애니메이션 속도도 동일 배율로 조절 (PlaybackSpeed)
- 쿨타임 감소: `실제 쿨타임 = Table.Cooltime × (1 - CooldownReduction / 100)` (최대 90%)

### 갱신·저장·표시

- 장착·해제 직후 소유 스킬 공격 프로필을 다시 만들고, 스탯 재계산 뒤에도 파생 프로필을 동기화한다.
- 구 세이브에서 `WeaponLocalEnhancements`가 없으면 빈 목록으로 복구하고 무기 캐시를 무효화한다. 플레이어·NPC 인벤토리/장비와 월드 드랍이 같은 `ItemData.OnLoadCompleted()` 경로를 사용한다.
- 장비 툴팁은 `기본 능력`, `무기 고유 강화`, `장비 옵션` 영역을 분리하고 고정 치명타와 증가 치명타 문구를 구분한다.
- 캐릭터 창(`UICharacter_Stats.cs`)은 기본 스킬(슬롯 0) 프로필의 치명타를 소수 둘째 자리까지 표시한다. 주문의 공격속도 행 숨김은 후속 UI 작업이며 현재는 프로필 값 `0`을 표시한다.

#### 스킬 태그 (SkillTag)
`[Flags]` enum으로 비트 플래그 조합. 테이블에서 콤마 구분 문자열로 입력, 저장 시 int로 변환.
```
Attack, Spell, Physics, Fire, Ice, Lightning, Poison,
Melee, Ranged, AoE, Projectile, Buff, Debuff
```
예시: `Attack,Physics,Melee` → 근접 물리 공격 스킬

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
- [x] 넉백 시스템 — 넉백·당기기 구현 (2026-10-12, [SKILL_DESIGN.md §6.10](SKILL_DESIGN.md))
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

**Last Updated**: 2026-08-22
**Status**: 장비 옵션 글로벌화·공격 프로필 통합 구현, 무기 고유 강화 서비스와 플레이 모드 검증은 후속
