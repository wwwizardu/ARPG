# SkillEffect 확장 아이디어 — 20종

`SkillEffectType` enum + `SkillEffectExecutor.Execute` switch case + 구글 시트 `SkillEffect` 행 1줄로 합성 가능한 신규 효과 후보 모음.
SKILL_DESIGN.md §6.8 / [skill_effect_system.md](../../../Users/peace/.claude/projects/c--Projects-ARPG/memory/skill_effect_system.md) 의존.

---

## 0. 전제

- **트리거 8종**: `OnSkillCommand` / `OnSkillStart` / `OnHit` / `OnCrit` / `OnKill` / `OnProjectileSpawn` / `OnProjectileHit` / `OnSkillEnd`
- **Context 가용 필드**: `OwnerEntityId`, `TargetEntityId`, `SkillEntityId`, `SkillId`, `TargetPosition`, `DamageResult`, `ProjectileEntityId`, `CancelOriginalCast`
- **현재 구현됨 (4종)**: `LifeStealOnHit` / `ApplyBuffOnHit` / `DelegateToTotem` / `SpawnProjectile`
- 이 문서의 모든 후보는 **enum 1줄 + Executor case 1개 + 시트 행 1개**로 추가 가능. 기존 `SkillTable` 변경 없음.

각 아이디어 카드 구조:
- **EffectType**: 새 enum 값
- **Trigger**: 발화 시점
- **Param 의미**: Param1/2/3 매핑
- **요약**: 한 줄 동작
- **사용 예**: 어떤 스킬/장비/룬에 붙으면 좋은지
- **구현 메모**: Executor 내부에서 어떤 시스템·컴포넌트를 건드려야 하는지

---

## A. 자원 회복 / 자기 강화 (5개)

### 1. ManaRestoreOnKill
- **Trigger**: `OnKill`
- **Param**: `Param1=회복량(flat)`, `Param2=MaxMp 비율(%)`, `Param3=미사용`
- **요약**: 적 처치 시 시전자 MP 회복.
- **사용 예**: 마법사 보조 룬. "처치 시 MP +10" 또는 "MaxMp의 5%".
- **구현 메모**: `StatComponent.CurrentMp += min(Param1 + FinalMaxMp*Param2/100, FinalMaxMp - CurrentMp)`. `SetComponent` 후 `Mp Changed` 메시지 송신 여부 기존 코드 컨벤션 따라가면 됨.

### 2. ManaRestoreOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=회복량`, `Param2=DamageDealt 비율(%)`
- **요약**: 명중 시 MP 회복. 스택 캐스터의 자원 순환용.
- **사용 예**: "마법 무기에 +1 ManaOnHit" 같은 PoE식 옵션.
- **구현 메모**: `LifeStealOnHit`과 거의 동일 구조 — HP 대신 MP 채움.

### 3. ManaRestoreOnSkillEnd
- **Trigger**: `OnSkillEnd`
- **Param**: `Param1=회복량`, `Param2=실제 명중한 적 수당 추가 회복`
- **요약**: 시전 끝나면 일정량 환원. AoE 헛스윙으로 인한 마나 고갈 완화.
- **사용 예**: "Cyclone 류 채널링 스킬에 환원 룬".
- **구현 메모**: 명중 카운트가 필요하면 Context에 `HitCount` 필드 추가하거나 `OnHit`마다 누적 후 `OnSkillEnd`에서 정산.

### 4. ApplyBuffOnSkillStart
- **Trigger**: `OnSkillStart`
- **Param**: `Param1=BuffId`, `Param2=스택`, `Param3=Duration override`
- **요약**: 시전 시작 시 시전자에게 자가 버프 부여 (전투 자세, 광폭화 등).
- **사용 예**: "전투 함성에 광폭화 버프 자동 부여", "시전 시 1초간 무적".
- **구현 메모**: 기존 `ApplyBuffOnHit`의 대상이 `OwnerEntityId`인 변형. `BuffHelper.AddBuff(ctx.OwnerEntityId, ...)`.

### 5. AddSelfStackOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=BuffId`, `Param2=스택당 증가량`, `Param3=최대 스택`
- **요약**: 명중 시 시전자에게 *누적형* 버프 1스택 추가. 스택이 다음 시전 데미지·이속·CritRate에 합산.
- **사용 예**: 로그의 "급소 찌르기" 콤보 보너스, 전사의 "분노 누적".
- **구현 메모**: `BuffHelper.AddBuffStack(ownerId, buffId, +1)` 같은 API 필요 — 현 `BuffComponent`가 스택 지원하는지 확인 필요. 미지원이면 BuffComponent에 `Stack: int` 필드 추가가 선결 조건.

---

## B. 군중 제어 (CC) (4개)

### 6. KnockbackOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=넉백 거리`, `Param2=지속시간(이동 보간)`, `Param3=확률(%)`
- **요약**: 명중한 적을 시전자→타겟 방향으로 밀어냄.
- **사용 예**: A1 Slam, 방패 강타, "넉백 룬".
- **구현 메모**: 타겟에 `KnockbackComponent { TargetPos, Duration }` 추가하고 별도 `System_Knockback` (Priority 110~120 권장)이 Lerp로 처리. 이미 유사 시스템이 있으면 재활용.

### 7. StunOnCrit
- **Trigger**: `OnCrit`
- **Param**: `Param1=지속시간(초)`, `Param2=확률(%)`, `Param3=면역시간(중첩 방지)`
- **요약**: 치명타 시 적을 행동 불가 상태로.
- **사용 예**: 메이스 무기군의 강점, "충격" 룬.
- **구현 메모**: `BuffEffectType.Stun` 추가 → `BuffHelper.AddBuff(targetId, Stun, duration)`. AI/Movement/Skill 시스템이 `HasBuff(Stun)` 체크하여 입력 차단.

### 8. ChillOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=감속(%)`, `Param2=지속시간`, `Param3=확률(%)`
- **요약**: 명중 시 이동/공격 속도 감소. (`BuffEffectType.Chill` 이미 enum 존재 — 미사용 슬롯 활용)
- **사용 예**: 냉기 속성 무기, S6 Frost Bolt.
- **구현 메모**: 이미 `BuffEffectType.Chill`이 정의돼 있으나 BuffTable 행이 없을 수 있음. `ApplyBuffOnHit`로 흡수 가능하면 별도 EffectType 불필요. **이 항목은 ApplyBuffOnHit + BuffTable 행 추가로 대체 가능 — EffectType 신설 전 검토 필요**.

### 9. TauntOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=지속시간`, `Param2=확률(%)`
- **요약**: 명중한 적의 AI 타겟을 강제로 시전자로 변경.
- **사용 예**: 탱커의 도발, "어그로 룬".
- **구현 메모**: `AIComponent`에 `ForcedTargetId`, `ForcedTargetUntil` 필드 추가. AI 타겟 선정 시점에 forced가 살아있으면 우선. 또는 `TauntComponent` 별도 컴포넌트로 분리.

---

## C. 발사체 / AoE 후속 (5개)

### 10. SpawnAreaEffectOnHit
- **Trigger**: `OnHit`
- **Param**: `Param1=AreaEffectId(테이블)`, `Param2=확률(%)`, `Param3=오프셋(0=명중 위치)`
- **요약**: 명중 위치에 작은 폭발/장판 생성.
- **사용 예**: "폭발 화살", "독 구름 룬".
- **구현 메모**: `EntityFactory.CreateAreaEffect(skillId, position, ownerId)` 같은 팩토리가 필요 (현재 AreaEffect 생성 경로 확인 후 매핑).

### 11. SpawnAreaEffectOnKill
- **Trigger**: `OnKill`
- **Param**: `Param1=AreaEffectId`, `Param2=반경 배율(%)`, `Param3=시체 위치 사용 여부(0=시전자, 1=타겟)`
- **요약**: 처치 시 시체 위치(또는 시전자 위치)에 폭발/장판.
- **사용 예**: 디아블로식 "시체 폭발", "사신 룬: 처치 시 영혼 폭발".
- **구현 메모**: `OnKill` 트리거는 `TargetEntityId`가 사망 직전 좌표 보존 — TransformComponent 조회 시점이 사망 처리 *이전*이어야 함 (시체 정리 순서 확인).

### 12. ProjectilePierceOnSpawn
- **Trigger**: `OnProjectileSpawn`
- **Param**: `Param1=관통 횟수`, `Param2=관통당 데미지 감쇠(%)`, `Param3=미사용`
- **요약**: 생성된 발사체에 관통 횟수를 부여.
- **사용 예**: 사수의 "관통 화살", 마법사의 "관통 빔".
- **구현 메모**: `ProjectileComponent`에 `PiercesLeft: int`, `DamageDecayPerPierce: float` 필드 필요. 충돌 처리에서 hit 후 `PiercesLeft--`, 0 미만이면 소멸. 기존 발사체 충돌 코드의 OnHit→Destroy 흐름을 분기.

### 13. ProjectileChainOnHit
- **Trigger**: `OnProjectileHit`
- **Param**: `Param1=연쇄 횟수`, `Param2=탐색 반경`, `Param3=감쇠(%)`
- **요약**: 발사체가 적중 시 다음 가까운 적을 자동 탐색해 재발사.
- **사용 예**: "체인 라이트닝", 번개 마법.
- **구현 메모**: 적중 처리에서 (a) 반경 내 적 탐색, (b) 새 발사체 스폰(또는 동일 발사체 재사용), (c) 연쇄 카운터 감소. `ProjectileComponent.ChainsLeft` 필드 필요.

### 14. ProjectileForkOnHit
- **Trigger**: `OnProjectileHit`
- **Param**: `Param1=분기 발사체 수`, `Param2=분기 각도(°)`, `Param3=감쇠(%)`
- **요약**: 적중 시 발사체가 N개로 분기. (체인과 달리 적 탐색 없이 같은 방향 부채꼴)
- **사용 예**: "분열 화살", 마법 "프리즘".
- **구현 메모**: 현행 `SpawnProjectile`(부채꼴 분산)과 거의 동일 로직 — context의 위치만 ProjectileEntityId 위치로 바꿔 재사용 가능.

---

## D. 시전 위임 / 자율 공격체 (3개)

### 15. DelegateToMine
- **Trigger**: `OnSkillCommand`
- **Param**: `Param1=지뢰 생존시간`, `Param2=감지 반경`, `Param3=발화 지연(초)`
- **요약**: 시전을 캔슬하고 지정 위치에 지뢰 생성. 적 진입 시 원본 스킬 발사.
- **사용 예**: 함정 사수, 폭파 전문가.
- **구현 메모**: `DelegateToTotem`과 동일 패턴. `EntityFactory.CreateMine(ownerId, skillId, pos, lifetime, detectRadius, fuse)` 추가 + `MineTag` 컴포넌트. 발화 로직은 `System_Mine`이 매 fixed update에 감지 반경 내 적 검사 후 시전.

### 16. DelegateToTrap
- **Trigger**: `OnSkillCommand`
- **Param**: `Param1=함정 생존시간`, `Param2=Arming Time(설치 후 무장까지)`, `Param3=발사 횟수`
- **요약**: 일정 시간 후 무장되어 적이 밟으면 발동. 지뢰와 다른 점: "단발 충격형" vs 지뢰 "투척형".
- **사용 예**: 도적의 점착 함정, 사수의 끈끈이.
- **구현 메모**: `TrapTag` + `ArmingTimer` 컴포넌트. 충돌 트리거(2D Collider Trigger) 또는 Owner 진영 외 엔티티 진입 검사.

### 17. SpawnSummonOnSkillStart
- **Trigger**: `OnSkillStart`
- **Param**: `Param1=SummonEntityId(MonsterTable.Id 활용)`, `Param2=수`, `Param3=생존시간`
- **요약**: 시전 시작 시 시전자 옆에 미니언 N마리 소환. 시전 자체는 정상 진행 (위임 아님).
- **사용 예**: 강령술 보조, "그림자 분신".
- **구현 메모**: `EntityFactory.CreateMonster(...)` + 팀 강제(시전자와 같은 Faction) + `LifetimeComponent`. 토템과 달리 자체 AI로 자율 행동.

---

## E. 메타 / 조건부 (3개)

### 18. CooldownReduceOnKill
- **Trigger**: `OnKill`
- **Param**: `Param1=감소량(초)`, `Param2=대상 SkillId(0=모든 스킬)`, `Param3=확률(%)`
- **요약**: 적 처치 시 다른 스킬의 쿨타임을 일정 시간 단축.
- **사용 예**: "정복자의 분노" 룬, 특정 강력기 회전율 향상.
- **구현 메모**: `SkillStateComponent` (혹은 별도 쿨다운 트래커)의 `CooldownEndTime -= Param1`. 모든 스킬 대상이면 PlayerSkill 매니저의 슬롯 전체 순회.

### 19. ExecuteOnLowHp
- **Trigger**: `OnHit`
- **Param**: `Param1=HP 임계(%, 이하 시 발동)`, `Param2=즉사 여부(1=즉사 / 0=배율 데미지)`, `Param3=배율(%, Param2=0일 때)`
- **요약**: 타겟 HP가 N% 이하면 즉사 또는 데미지 N배.
- **사용 예**: 처형(Execute) 룬, 보스 처치 시간 단축형 빌드.
- **구현 메모**: 타겟 `StatComponent.CurrentHp / FinalMaxHp`가 임계 미만일 때 `DamageResult.FinalDamage *= Param3/100` 또는 `targetStat.SetCurrentHp(0)`. 보스 면역 처리는 `BossTag` 체크 가능.

### 20. ConsumeBuffForBonus
- **Trigger**: `OnSkillCommand`
- **Param**: `Param1=소모 BuffId`, `Param2=소모 스택 수`, `Param3=데미지 배율(%)`
- **요약**: 시전 직전 시전자의 특정 버프 스택을 소모해 이번 시전의 데미지를 증폭. 소모 못하면 일반 시전.
- **사용 예**: "광폭화 스택 → 강타에 폭발 데미지", PoE의 Frenzy/Power Charge 소비 스킬.
- **구현 메모**: `BuffHelper.TryConsumeStack(ownerId, buffId, count)` API 필요. 성공 시 `Context`에 `BonusDamageMul` 필드 추가하여 `System_Skill`의 데미지 계산 단계에서 합산. **Context 구조 확장이 필요한 첫 항목** — 기존 5개 트리거 시점에는 데미지 배율 전달 통로가 없음.

---

## 우선순위 권고

> 모든 아이디어를 한 번에 구현할 필요는 없음. 다음 묶음으로 단계 도입 권장.

| 단계 | 효과 | 이유 |
|---|---|---|
| 1차 (즉시) | #1 ManaRestoreOnKill, #2 ManaRestoreOnHit, #4 ApplyBuffOnSkillStart | `LifeStealOnHit` / `ApplyBuffOnHit` 구조 100% 재사용. 코드 변경 최소. |
| 1차 | #6 KnockbackOnHit, #7 StunOnCrit | 컴포넌트 1~2개만 추가하면 됨. 게임 체감 변화 큼. |
| 2차 | #10 SpawnAreaEffectOnHit, #11 SpawnAreaEffectOnKill | `EntityFactory.CreateAreaEffect` 진입점 확립이 선결. AoE 시스템 정리와 함께 진행. |
| 2차 | #12~14 발사체 옵션 (Pierce/Chain/Fork) | `ProjectileComponent` 필드 확장 + 충돌 처리 분기. Stat합산형(`Stat.ProjectilePierceAdd`)과 짝지어 도입 권장. |
| 3차 | #15 DelegateToMine, #16 DelegateToTrap | `DelegateToTotem` 패턴 재사용. 단 Mine/Trap 시스템 자체 신규 구현 필요. |
| 3차 | #17 SpawnSummonOnSkillStart | Faction/Lifetime/AI 의존. SKILL_DESIGN §6.9 의존성 정리 후. |
| 4차 (구조 변경 동반) | #5 AddSelfStackOnHit, #18 CooldownReduceOnKill, #19 ExecuteOnLowHp, #20 ConsumeBuffForBonus | BuffComponent 스택 지원·Context 확장·SkillStateComponent 쿨다운 노출 등 **선결 인프라 작업** 필요. |

**참고**: #8 ChillOnHit는 이미 `BuffEffectType.Chill`이 enum에 정의돼 있어 `ApplyBuffOnHit` + BuffTable 행으로 흡수 가능 — **별도 EffectType 신설 비권장**. 동일 논리로 Freeze / Poison / Ignite도 `ApplyBuffOnHit`로 통합 가능.

---

## 신규 EffectType 추가 체크리스트

새 효과 1개 추가 시 검증 항목:

- [ ] `GlobalEnum.SkillEffectType`에 enum 값 + 한 줄 주석 (Param 의미 명시)
- [ ] `SkillEffectExecutor.Execute` switch에 case 추가, private static 메서드 1개 작성
- [ ] 기존 `BuffEffectType` / `ApplyBuffOnHit`로 흡수 가능한지 먼저 확인 (중복 enum 방지)
- [ ] Context 신규 필드가 필요하면 `SkillEffectContext` 확장 + 모든 호출 지점에서 채우는지 확인
- [ ] 구글 시트 `SkillEffect` 시트에 행 추가: `Id / Name / EffectType / Trigger / Probability / Param1~3`
- [ ] 사용할 SkillTable 행의 `SkillEffectIds`에 ID 합성

---

## 변경 이력

- 2026-05-08: 초안 작성, 20개 후보 + 우선순위 권고
