# 몬스터 공격 예고 표시 설계

> 상태: Phase 0~2 구현, Phase 3 일부 구현, Phase 4 미착수 (2026-10-03 코드 대조)  
> - 구현: 2026-09-19 커밋 "몬스터 공격 예고 시스템 추가"(3d85ce7). 커밋 정본화(Resolver·장판 첫 커밋·착지 커밋·발사체 논리/View 분리), `AttackTelegraphComponent`·`System_AttackTelegraph`(900)·`AttackTelegraphBatchView`·셰이더, Owner Circle/Sector·Landing·Projectile Lane/Fan, 다단·채널 재진행, 카메라 여백 Bounds 컬링, 빌드 검증기(`AttackTelegraphBuildValidator`)  
> - 부분: 마지막 펄스는 셰이더에 있으나 경고음 없음(§4.1), LOD enum만 있고 항상 `Full`(§11.3)  
> - 미구현: 취소 페이드(§4.1), 최소 가독 시간 진단(§7.5), EditMode 테스트(§13.1, 빌드 검증기가 대체), Phase 4 프로파일  
> 작성일: 2026-09-14  
> 범위: 몬스터 스킬 시작부터 첫 피해 또는 발사 커밋까지의 바닥 공격 예고, 공통 배치 렌더링, 취소·다단히트·지속 장판 처리

---

## 0. 목적

몬스터가 스킬 사용을 시작한 순간부터 플레이어가 실제로 피해를 받을 수 있는 순간까지 다음 두 정보를 명확히 전달한다.

1. **어디가 위험한가**: 실제 전투 판정과 일치하는 고정된 외곽선과 바닥 영역.
2. **언제 위험해지는가**: 내부 진행 표시가 외곽선에 닿는 순간 첫 피해 또는 발사 커밋이 발생하는 공통 시각 규칙.

몬스터 수가 많아져도 몬스터마다 표시용 GameObject, Renderer, Coroutine을 만들지 않는다. 실행 중인 공격만 ECS 컴포넌트로 추적하고 하나의 중앙 배치 View가 공통 Mesh와 Material로 그린다.

이 문서는 공격 예고 표시의 UX·타이밍·판정·렌더링 계약을 함께 정의한다. 기존 스킬 타이밍의 정본은 `System_Skill`, 범위 판정의 정본은 `HitboxMath`이며, 예고 시스템이 이 계산을 복제해서 별도의 규칙을 만들지 않는다.

---

## 1. 결정 요약

1. 공격 예고는 **스킬 명령이 유효하게 수락된 순간** 시작한다.
2. 전체 위험 영역의 옅은 면과 선명한 외곽선은 시작부터 항상 전부 보인다.
3. 밝은 내부 진행 영역은 0%에서 100%로 확장되며, 외곽선에 닿는 순간 첫 피해 또는 발사 커밋이 일어난다.
4. 표시 진행률은 별도 연출 시간이 아니라 실제 스킬 타이밍과 동일한 공통 Resolver 결과를 사용한다.
5. 표시는 커밋 시점에 진행률 100%에 도달한 뒤 그 상태를 유지하고, 스킬이 종료되거나 취소될 때 제거된다. 다단·채널은 각 커밋에서 다음 커밋을 향해 진행률을 다시 시작한다. 발사체는 발사 커밋에서 100%가 되고 그 뒤 회피 정보의 주체는 발사체 자체로 넘어간다.
6. 지속 장판은 시전 예고와 활성 위험 장판을 시각적으로 구분한다.
7. 일반 공격은 중앙 동적 Mesh 배치 렌더러로 그린다. 희소한 보스 전용 복합 문양만 풀링 프리팹/VFX를 허용한다.
8. 범위 표시는 AI 교전 거리인 `SkillRangeMax`가 아니라 실제 히트 판정 데이터인 `SkillTargetRange1`, `SkillTargetRange2`, Collider 반경을 사용한다.
9. 몬스터 공격 방향은 현행 `SnapshotOnCommand` 계약을 따른다. 실제 판정 중심이 시전자를 따라가는 스킬은 표시 중심도 시전자를 따라간다.
10. 공격 취소·Stun·사망·스킬 완료·청크 비활성화·게임 Reset에서는 표시를 즉시 정리한다.

---

## 2. 비목표

- 플레이어 스킬의 커서 조준 화살표를 대체하지 않는다.
- 플레이어가 사용할 수 있는 스킬 사거리 UI를 만들지 않는다.
- 데미지 숫자, DPS 예상값, 몬스터 쿨다운을 표시하지 않는다.
- 일반 공격마다 독립된 Addressable 프리팹을 만들지 않는다.
- 시각 표시만 늦춰 실제 피해와 다른 시간을 보여주지 않는다.
- 이번 단계에서 GPU Indirect Draw, Compute Shader, RenderTexture 기반 위험 영역 합성까지 도입하지 않는다.

---

## 3. 현행 런타임 기준선

### 3.1 스킬 상태와 타이밍

`System_Skill`은 `Start → Process → End → None` 상태를 FixedUpdate에서 처리한다.

- 유효한 명령을 처리한 뒤 공격·시전 속도를 적용한 `SkillTimingComponent`를 확정한다.
- `Start`는 준비 단계다.
- `Process` 진입 시 애니메이션과 일부 효과가 시작된다.
- 직접/다단 타격의 첫 히트 오프셋은 다음과 같다.

```text
firstHitOffset = Clamp01(DamageTime) × ProcessDuration
firstImpactFromCommand ≈ StartDuration + firstHitOffset
nextHitOffset = firstHitOffset + CurrentHitIndex × HitInterval
```

실제 상태 전이는 FixedUpdate 경계에서 일어나므로 최종 커밋은 고정 틱 단위로 양자화된다. 예고 타이밍도 동일한 FixedUpdate 계약을 따라야 한다.

### 3.2 현재 효과 종류별 커밋 차이

예고 구현 전에는 모든 효과가 `DamageTime`을 같은 방식으로 사용하지 않았다. 아래 "구현 전 커밋/피해 시점" 열은 설계 당시 기준선이며, 마지막 열이 2026-10-03 확인한 현행이다.

| 종류 | 구현 전 커밋/피해 시점 | 예고 구현 전 조치 | 현행 (2026-10-03 확인) |
|---|---|---|---|
| 직접·다단 타격 | `DamageTime × ProcessDuration`, 이후 `HitInterval` | 공통 Resolver로 계산 추출 | 반영됨 — `SkillImpactTimingResolver.Resolve` |
| 발사체 | 직접 타격 스케줄 시점에 발사체 생성 요청 | 발사 커밋을 예고 진행률 100% 지점으로 사용 | 반영됨 — `ProjectileRelease` |
| 채널링 | `ChannelingInterval`마다 타격, 첫 틱도 이 값 기준 | 첫 틱 정책을 공통 Resolver에서 명시 | 반영됨 — 첫 틱 `DamageTime × ProcessDuration`, 이후 `ChannelingInterval` (§7.3) |
| 지속 장판 | Process 진입 즉시 생성, `NextTickIn=0`으로 같은 FixedUpdate에 첫 틱 | `DamageTime` 기반 장판 활성 커밋으로 이동 | 반영됨 — 첫 커밋에서 1회 생성 (§12.1) |
| 점프 공격 | 이동은 Process 진입에 시작하며 타격은 일반 다단 경로를 사용 | 착지형 공격은 별도 Landing 커밋 계약 필요 | 반영됨 — `SkillLandingCommitComponent` (§12.3) |

예고 표시만 먼저 만들면 특히 지속 장판과 점프 공격에서 실제 피해와 표시가 어긋난다. Phase 0에서 커밋 시점을 정리한 뒤 시각 시스템을 연결한다. (2026-10-03 확인: Phase 0 완료 후 연결됨)

### 3.3 현재 몬스터 공격의 기본 리드 타임 예시

공격·시전 속도 배율이 1이라고 가정하면 현재 테이블의 대략적인 첫 커밋 시간은 다음과 같다.

| 스킬 | 기본 계산 | 비고 |
|---|---:|---|
| `StrikeMonster` | `0 + 0.85 × 0.9 = 0.765초` | 직접 근접 타격 |
| 몬스터 자폭 공격 | `0 + 1.0 × 0.85 = 0.85초` | 원형 직접 타격 |
| 기본 원거리/화염/독 발사체 | `0 + 0.15 × 0.9 = 0.135초` | 피해가 아니라 발사 시점. 이후 이동 시간이 추가됨 |

공격속도가 올라가면 Attack 태그의 Start/Process/End 시간이 함께 짧아진다. 표시도 같은 속도로 진행해야 한다. 플레이어가 읽을 수 없는 공격은 표시만 느리게 만들지 않고 실제 커밋 시간을 조정한다.

---

## 4. 플레이어가 학습할 시각 문법

### 4.1 기본 상태

```text
스킬 명령 수락                                      첫 피해/발사              스킬 종료
      │                                                   │                        │
      ▼                                                   ▼                        ▼
옅은 전체 위험 영역 + 외곽선 ──────────────────────────── 유지 ─────────────────── 제거
밝은 내부 진행 영역       0% ━━━━━━━━━━━━━━━━━━━━━━━▶ 100% ────── 100% 유지 ───▶ 제거
마지막 경고                                         외곽선 펄스/음향             펄스 유지
```

다음 커밋이 남아 있으면 100%에 도달한 진행 면이 그 커밋을 향해 다시 0%부터 확장한다.
마지막 커밋 뒤에는 진행 면이 100%에 고정되고, 스킬이 `SkillState.None`으로 돌아갈 때 표시가 제거된다.

- **옅은 위험 면**: 공격이 닿을 수 있는 전체 영역. 진행률과 무관하게 처음부터 전부 보인다.
- **선명한 외곽선**: 실제 판정 경계.
- **밝은 진행 면**: 중심에서 외곽으로 확장한다. 외곽선에 닿는 순간 커밋한다.
- **마지막 10~15%**: 외곽선 밝기 펄스와 짧은 공통 경고음을 재생할 수 있다.
  (2026-10-03 확인: 펄스는 셰이더에서 진행률 0.85 이후 적용된다(`AttackTelegraphUnlit.shader`의 `finalPulse`). 경고음은 미구현.)
- **커밋 순간**: 진행 면이 외곽선에 닿는다. 여기서 표시를 지우지 않고 100% 상태로 고정한다.
  진행 면이 더 이상 움직이지 않는 것이 "이 커밋은 이미 지나갔다"는 신호이며, 다음 커밋이 있으면
  곧바로 다시 확장하기 시작한다. 남은 후딜(End) 구간에도 위험 영역이 보이므로 플레이어가
  "표시가 사라졌으니 안전하다"는 잘못된 학습을 하지 않는다.
- **취소**: 즉시 흐려지며 짧게 사라진다. 100% 플래시는 재생하지 않는다.
  (2026-10-03 확인: 취소 페이드는 미구현. 현행은 `System_Skill.OnSkillComplete`가 컴포넌트를 제거하면 다음 프레임 배치에서 즉시 빠진다.)

전체 위험 면과 외곽선이 항상 보이므로 플레이어가 현재 밝게 채워진 부분만 안전하지 않다고 오해하지 않게 한다.

### 4.2 색과 접근성

색만으로 상태를 구분하지 않는다.

| 상태 | 색상 예시 | 형태/밝기 규칙 |
|---|---|---|
| 공격 예고 | 적색·주황색 | 고정 외곽선 + 확장 진행 면 |
| 마지막 경고 | 밝은 적색·백색 | 외곽선 펄스 |
| 활성 지속 장판 | 독=녹색, 화염=주황 등 | 반복 텍스처 또는 지속 펄스. 확장 게이지 없음 |
| 취소 | 채도와 알파 감소 | 외곽선이 끊기며 빠르게 소멸 |

색각 이상과 화면 혼잡을 고려해 위험 경계는 충분한 명도 차이를 갖고, 지면 타일보다 위·캐릭터와 투사체보다 아래 Sorting Layer에 둔다.

### 4.3 겹침과 혼잡

- 겹친 공격도 각 외곽선과 진행 면을 유지한다.
- 알파 가산만으로 위험도를 전달하지 않는다. 여러 범위가 겹쳐도 완전 불투명한 덩어리가 되지 않도록 최대 알파를 제한한다.
- 화면 밖이지만 곧 화면 안 플레이어를 타격할 수 있는 범위는 카메라 여백 안에서 유지한다.
- 표시 예산이 부족할 때 플레이어를 실제로 타격할 수 있는 공격을 절대 생략하지 않는다. 먼 공격은 진행 면을 생략하고 외곽선만 그리는 LOD를 사용할 수 있다.
- 일반 공격과 보스 공격은 같은 타이밍 문법을 사용하되, 보스 전용 문양은 추가 장식을 얹을 수 있다.

---

## 5. 공격 유형별 UX 계약

### 5.1 직접 근접·부채꼴 공격

- 시전자 발 위치를 중심으로 한다.
- 방향은 `SkillTargetComponent.TargetDirection`을 사용한다.
- 현행 몬스터 `SnapshotOnCommand`에서는 방향을 명령 시점에 고정한다.
- 실제 판정이 타격 순간 시전자 위치를 읽으므로 중심은 기본적으로 시전자를 따라간다.
- 첫 피해에서 진행 면이 외곽선에 닿고, 그 뒤에는 스킬 종료까지 100% 상태로 유지한다.

### 5.2 자기 중심 원형·자폭

- 위험 원은 시전자 중심을 따라간다.
- 방향 표시는 사용하지 않는다.
- 자폭이 취소되거나 몬스터가 먼저 죽으면 취소 연출로 제거한다.

### 5.3 위치 지정 원형·지속 장판

- **장판은 예고 대상이 아니다.** 장판은 커밋 시점에 즉시 생성되므로 장판 자체의 비주얼이 위험 정보를
  담당한다. 예고를 따로 그리면 같은 위험을 두 번 표시하게 된다.
- 따라서 장판 스킬의 예고 도형은 `None`이 정상이다. 빌드 검증기도 장판 스킬의 `None`을 에러로 보지 않는다.
- **장판과 직접 타격은 공존할 수 있다** (예: 내려찍어 3연타 + 바닥에 불장판 1개). 이때 예고는
  **직접 타격 범위만** 그리고 장판은 예고 없이 생성된다. 직접 타격 예고가 100%에 닿는 순간이 곧 장판이
  깔리는 순간이므로 플레이어가 배울 규칙은 하나로 유지된다.
- 커밋 시점에 실제 `AreaEffect`를 생성하며, 한 실행에 1개만 생성한다 (`SkillComponent.IsAreaEffectCreated`).
  현재 생성 시점은 **첫 커밋**이다. 다연타 스킬에서 "마지막 타격에 장판"을 원하면 별도 작업이 필요하다.
- 생성된 장판이 즉시 첫 틱을 가한다면 같은 FixedUpdate 안에서 `커밋 → 장판 생성 → 첫 틱` 순서를 보장한다.
  `System_Skill`(200) → `System_AreaEffect`(240) 우선순위가 이를 성립시킨다.
- 활성 장판에는 카운트다운 확장 면을 사용하지 않는다.

**예외 — 준비 시간이 있는 장판.** 생성까지 대기가 필요한 장판(깔린 뒤 n초 후 활성화 등)이 추가되면
그 대기 구간에는 예고가 필요하다. 이 용도로 `AttackTelegraphShape.TargetCircle`,
`AttackTelegraphOriginMode.TargetPosition`, 그리고 `AttackTelegraphGeometryInput`의
`HasAreaEffect` / `IsPositionTarget` / `AreaEffectRadius` 입력을 **현재 미사용 상태로 예약해 둔다.**
`AreaEffectTable`에 지연 컬럼이 없으므로 시트 작업이 선행되어야 한다.

### 5.4 발사체 공격

- 발사 전에는 방향선 또는 좁은 발사 부채꼴을 표시한다.
- 진행률 100%는 **피격 시점이 아니라 발사 커밋 시점**이다.
- 발사 후 예고는 100% 상태로 스킬 종료까지 유지되며, 회피 정보의 주체는 실제 투사체의 위치·속도·충돌 반경으로 넘어간다.
- 직선 경로의 가이드 길이는 실제 최대 이동거리와 일치시킨다. 현행 단순 발사체는 `ProjectileTable.Speed × LifeTime`을 기준으로 하며, 향후 별도 거리 제한이 생기면 그 값을 우선한다.
- 경로 폭은 최소한 `ProjectileTable.HitRadius + Player.HitRadius`를 포함해 플레이어 중심 기준 위험 영역과 맞춘다.
- 다중 발사체는 실제 스프레드 각도로 각 경로를 그리거나, 전체 경로를 포함하는 부채꼴 외곽선을 그린다.

### 5.5 다단히트

- 첫 피해까지는 일반 확장 게이지를 사용한다.
- 첫 피해 이후에도 동일 영역이 다시 타격되면 위험 면은 유지한다.
- 다음 타격마다 `nextHitOffset`을 기준으로 짧은 펄스를 다시 진행한다.
- 마지막 타격 후에는 100% 상태로 유지하고 스킬 종료 시 제거한다.
- 타격마다 방향을 재커밋하는 `TrackPerCommit`은 다음 타격 예고도 새 방향으로 갱신한다.

### 5.6 채널링

- 최초 틱까지 확장 게이지를 사용한다.
- 이후 각 `ChannelingInterval`마다 외곽선 펄스로 다음 틱을 알린다.
- `TrackContinuously`라면 실제 타격 방향과 예고 방향을 함께 갱신한다.
- 채널 입력 종료·Stun·사망에서는 즉시 취소한다.

### 5.7 점프·착지 공격

- 이동 경로보다 착지 피해 영역을 우선 표시한다.
- 착지 위치가 확정되는 `LaunchCommit`에서 목표 원을 최종 고정한다.
- 피해가 착지와 동시에 발생하도록 실제 타격 커밋을 `Landing`에 연결한다.
- 비행 중 충돌 또는 경로 타격이 있는 별도 스킬은 Line/Capsule 형태를 추가한다.

---

## 6. 실제 판정과 범위 표시의 일치

### 6.1 플레이어 중심 기준 위험 영역

`System_Skill.CheckCircleRangeEntities`는 시전자 발 위치와 Collider 반경을 사용하고, `HitboxMath.CircleVsSector`는 대상 반경을 거리 한계에 더한다. 따라서 플레이어에게 보여줄 직접 공격의 유효 반경은 다음과 같다.

```text
displayRange = owner.HitRadius
             + SkillTargetRange1
             + player.HitRadius
```

플레이어 중심이 표시 영역 안에 들어오면 현행 직접 공격 판정에서도 맞는 영역을 목표로 한다. 플레이어 Collider가 전투 중 바뀔 수 있다면 표시 반경도 갱신하거나 스킬 시작 시 스냅샷한다. 기본 계약은 실제 타격 시점 값을 따라가는 것이다.

### 6.2 부채꼴 각도

`SkillTargetRange2`는 현행 코드에서 `halfAngleDeg`로 사용한다.

```text
halfAngle = Clamp(SkillTargetRange2, 0, 180)
visualTotalAngle = halfAngle × 2
halfAngle >= 180 → 원형
```

테이블 문서와 에디터 UI에서도 이 필드가 반각이라는 사실을 명시한다. `360`을 입력한 기존 행은 코드상 원형으로 동작하지만, 신규 데이터는 원형을 뜻하는 정규값 `180` 사용 여부를 별도 마이그레이션에서 결정한다.

### 6.3 중심과 방향의 추적 정책

범위 중심과 방향을 하나의 `follow` boolean으로 묶지 않는다.

| 항목 | 정책 | 예시 |
|---|---|---|
| 중심 | `FollowOwner` / `SnapshotOwner` / `TargetPosition` / `LandingPosition` | 근접은 FollowOwner, 장판은 TargetPosition |
| 방향 | `SnapshotOnCommand` / `FollowCommittedAim` | 일반 몬스터는 SnapshotOnCommand |

판정이 시전자의 현재 위치를 읽는데 표시만 시작 위치에 남는 상태, 또는 판정 방향은 고정인데 표시가 플레이어를 계속 추적하는 상태를 금지한다.

### 6.4 자동 형태 결정

MVP는 기존 테이블에서 공통 형태를 자동 결정한다.

```text
ProjectileId > 0                    → ProjectileLane 또는 ProjectileFan
SkillType == Jump && HasDirectImpact
  && ExecutionType != Channeling    → LandingCircle
HasDirectImpact == false            → None
SkillTargetType == Direction
  && SkillTargetRange2 >= 180       → OwnerCircle
SkillTargetType == Direction        → OwnerSector
AreaEffectId > 0 (위에 안 걸림)     → None (장판 비주얼이 담당 — 정상)
그 외                               → None + 개발 빌드 경고
```

향후 라인·캡슐·링·도넛·다중 원처럼 자동 추론할 수 없는 스킬이 추가될 때만 `SkillTable.TelegraphShape`과 전용 파라미터를 추가한다. 표시 지속시간은 테이블에 중복 저장하지 않는다.

---

## 7. 타이밍 정본 설계

### 7.1 공통 용어

| 용어 | 의미 |
|---|---|
| `WarningStart` | 유효한 스킬 명령이 수락되고 실제 실행 상태로 들어간 시점 |
| `Commit` | 공격 방향·위치·효과가 확정되어 되돌릴 수 없는 시점 |
| `Impact` | 직접 HP 감소 또는 지속 장판 첫 틱이 일어나는 시점 |
| `Release` | 발사체가 생성되어 이동을 시작하는 시점 |
| `NextImpact` | 다단·채널 공격의 다음 피해 시점 |

직접 공격에서는 Commit과 Impact가 같은 FixedUpdate에 일어난다. 발사체에서는 Commit/Release가 같고 Impact는 충돌 시점이므로 예측하지 않는다.

### 7.2 `SkillImpactTimingResolver`

스킬 시스템과 예고 시스템이 공유하는 순수 계산 클래스를 추가한다.

```csharp
public readonly struct SkillImpactSchedule
{
    public readonly SkillImpactKind Kind;
    public readonly float FirstCommitOffset;
    public readonly float RepeatInterval;
    public readonly int CommitCount;
}

public static class SkillImpactTimingResolver
{
    public static SkillImpactSchedule Resolve(
        SkillComponent skill,
        SkillTimingComponent timing);
}
```

이 클래스는 ECS 조회, `Time`, Renderer, GameObject에 접근하지 않는다. `System_Skill`은 이 결과로 실제 커밋을 실행하고, 예고 시스템은 같은 결과로 진행률을 계산한다.

(2026-10-03 확인: 실제 시그니처는 `Resolve(in SkillImpactTimingInput input)`이다. 입력 struct가 실행 모드·Start/Process 시간·`DamageTime`·간격·`HitCount`·직접 타격/발사체/장판/착지 여부를 받고, `SkillImpactSchedule`은 `StartDuration`·`FirstProcessCommitOffset`도 보관한다. FixedUpdate 양자화는 `GetQuantizedCommitOffset`/`GetQuantizedRemainingDelay`가 담당한다 — `Assets/Scripts/Skill/Telegraph/SkillImpactTimingResolver.cs`)

### 7.3 유형별 기본 스케줄

| 유형 | 첫 커밋 | 반복 |
|---|---|---|
| Single/MultiHit 직접 | `StartDuration + DamageTime × ProcessDuration` | `HitInterval` |
| Projectile | 위와 동일한 시점에 Release | 충돌은 별도 |
| AreaEffect | 위와 동일한 시점에 Area 활성화 및 첫 틱 | `AreaEffect.TickInterval`은 활성 장판 책임 |
| Channeling | 기본적으로 `StartDuration + DamageTime × ProcessDuration` | `ChannelingInterval` |
| Landing | 실제 착지 완료 | 단발 (Single/MultiHit 전용) |

Landing 커밋은 Single/MultiHit에만 적용한다(`SkillTable.UsesLandingCommit`). 채널 틱은 `ChannelingInterval`
주기로 발생해 착지라는 한 시점에 정렬할 수 없고, 착지 커밋을 소비하는 경로도 `ProcessMultiHitSkill`뿐이라
채널링에 적용하면 커밋이 영구 잔류해 Process 상태를 벗어나지 못한다.

따라서 채널링 점프 스킬(`SkillType = Jump`, `ArcHeight > 0`, `ExecutionType = Channeling`)은 `ChannelImpact`로
동작한다. 점프 발사는 스케줄과 무관하게 `SkillType`/`ArcHeight`로 판정되므로 도약은 그대로 1회 실행되고,
타격은 `DamageTime` 첫 틱 이후 `ChannelingInterval` 주기로 이어진다. 체공 시간은 `ProcessDuration`으로
고정되므로 AI(입력 없음)는 도약과 채널이 함께 끝나고, 플레이어는 착지 후 지면에서 채널을 계속한다.
예고 도형도 `LandingCircle`이 아니라 시전자를 따라가는 `OwnerCircle`/`OwnerSector`가 되어 실제 판정 중심과 일치한다.

채널링은 현행 첫 틱이 `ChannelingInterval` 기준이므로 공통 계약 적용 시 동작 변경이 발생한다. 마이그레이션 시 기존 콘텐츠가 의도한 첫 틱을 유지하려면 `DamageTime` 값을 조정하고 대표 스킬을 PlayMode에서 재측정한다.

(2026-10-03 확인: 채널 첫 틱은 `DamageTime` 기준으로 확정되어 코드에 반영됐다 — `SkillImpactTimingResolver.Resolve`가 Channeling에 `damageOffset`을 첫 커밋, `ChannelingInterval`을 반복, `CommitCount=-1`(무한)로 반환. 기존 채널 콘텐츠(Whirlwind 등)의 `DamageTime` 조정·PlayMode 재측정 여부는 확인되지 않았다.)

### 7.4 FixedUpdate 양자화

- 게임플레이 커밋 판정은 FixedUpdate에서만 한다.
- Renderer는 Update에서 부드럽게 보간해도 되지만 100% 도달은 예정 FixedUpdate 커밋보다 먼저 보이지 않게 한다.
- 가장 안전한 방식은 런타임 컴포넌트에 `WarningStartFixedTime`, `NextCommitFixedTime`을 저장하고 셰이더에는 현재 보간 시간과 함께 전달하는 것이다.
- FixedUpdate에서 커밋이 실행되면 컴포넌트의 타격 인덱스를 갱신하거나 제거한다. Renderer가 자체 판단으로 피해를 발생시키지 않는다.

### 7.5 최소 가독 시간

시각만 늦추는 최소 시간 보정은 금지한다. 대신 에디터 진단이 다음을 경고한다.

- 직접 공격의 첫 커밋 시간이 프로젝트 설정의 최소 가독 시간보다 짧음.
- 발사체 Release가 매우 빠르고 투사체 속도까지 빨라 총 회피 시간이 부족함.
- 장판이 예고 없이 즉시 첫 틱을 발생시킴.
- 공격속도 상한에서 커밋 시간이 최소 가독 시간 아래로 내려감.

최소 가독 시간 수치는 실제 플레이 테스트 후 확정하며, 부족하면 `StartTime`, `ProcessTime`, `DamageTime`, 공격속도 또는 투사체 속도를 수정한다.

(2026-10-03 확인: 위 진단은 미구현. `AttackTelegraphBuildValidator`는 Resolver 정본 계약과 몬스터 스킬 테이블의 참조·범위·스케줄/도형 생성 가능 여부만 검사하며, 최소 가독 시간 경고는 없다.)

---

## 8. ECS 데이터 구조

### 8.1 컴포넌트

예고는 스킬 엔티티에만 실행 중 일시적으로 부착한다.

```csharp
public enum AttackTelegraphShape : byte
{
    None,
    OwnerCircle,
    OwnerSector,
    TargetCircle,
    ProjectileLane,
    ProjectileFan,
    LandingCircle,
}

public enum AttackTelegraphOriginMode : byte
{
    FollowOwner,
    SnapshotOwner,
    TargetPosition,
    LandingPosition,
}

public struct AttackTelegraphComponent
{
    public int OwnerEntityId;
    public AttackTelegraphShape Shape;
    public AttackTelegraphOriginMode OriginMode;

    public Vector2 SnapshotOrigin;
    public Vector2 TargetPosition;
    public Vector2 Direction;

    public float Range;
    public float HalfAngleDeg;
    public float Width;

    public float WarningStartFixedTime;
    public float NextCommitFixedTime;
    public int CommitIndex;
    public int CommitCount;

    public bool IsCommitted;
    public bool WasCancelled;
}
```

컴포넌트는 데이터만 보관한다. 진행률 계산, 카메라 컬링, Mesh 생성 로직을 넣지 않는다.

`bool` 필드는 구현 과정에서 상태 enum 하나로 치환할 수 있다. 중요한 계약은 렌더링 객체나 풀 핸들을 컴포넌트에 저장하지 않는 것이다.

(2026-10-03 확인: 실제 `AttackTelegraphComponent`에는 `IsCommitted`/`WasCancelled`가 없다 — 취소·완료는 컴포넌트 제거로만 표현한다. 대신 `ProjectileCount`, `ProjectileSpreadAngleDeg`가 추가되어 ProjectileFan을 그린다. 나머지 필드는 위와 같다 — `Assets/Scripts/Common/Component/AttackTelegraphComponent.cs`)

### 8.2 생성과 제거 지점

생성:

1. `ProcessSkillCommands`의 마나·상태·컴포넌트 검증 통과.
2. 공격/시전 속도를 반영한 `SkillTimingComponent` 확정.
3. `SkillTargetComponent`와 `SkillImpactSchedule` 확정.
4. `SkillState.Start` 진입과 함께 `AttackTelegraphComponent` 추가.

갱신:

- 다단/채널의 다음 타격 예약.
- `TrackPerCommit`/`TrackContinuously`의 방향 갱신.
- FollowOwner 중심과 LandingPosition 갱신.

마지막 커밋 이후:

- 컴포넌트를 즉시 제거하지 않고 진행률 100% 상태를 유지한다. 커밋 인덱스가 마지막을 넘어가면
  `NextCommitFixedTime`이 과거가 되므로 셰이더 진행률이 1.0으로 포화되고 추가 갱신이 필요 없다.
- 커밋 직후 제거하지 않는 이유는 두 가지다. 후딜 구간에 위험 영역이 사라져 "안전해졌다"고 오해하게
  만들지 않고, 다단·채널의 다음 커밋과 단발 커밋이 같은 생명주기를 쓰게 해 경로를 하나로 유지한다.

제거:

- 스킬 완료(`SkillState.None` 전이)와 공격 취소·Stun·사망·강제 중단. 두 경로 모두
  `System_Skill.OnSkillComplete` 한 곳에서 제거하므로 커밋 종류별 제거 분기를 두지 않는다.
- `ActivationDistanceComponent.IsActivated == false` 또는 출신 청크 Warm 전환.
- `System_Skill.OnReset`, `System_AttackTelegraph.OnReset`.

### 8.3 컴포넌트 풀

`ComponentManager`에 `AttackTelegraphComponent` 초기 용량을 명시한다. 전체 몬스터 수가 아니라 동시에 시전 중인 공격 수를 기준으로 시작 용량을 잡고, 대표 부하 테스트 결과로 조정한다. 초기 후보는 128이며 SparseSet 확장 여부와 GC를 Profiler로 확인한다.

(2026-10-03 확인: 128로 등록됨 — `Assets/Scripts/Manager/ComponentManager.cs`. Profiler 확인은 미실시.)

---

## 9. 시스템과 책임 경계

### 9.1 `System_Skill`

- 실제 커밋과 피해의 유일한 실행 주체를 유지한다.
- 공통 Timing Resolver를 사용한다.
- 스킬 시작에서 Telegraph 컴포넌트를 생성하고, 각 커밋에서 다음 커밋으로 갱신한다.
- 제거는 취소·완료가 합류하는 `OnSkillComplete` 한 곳에서만 한다. 커밋 종류별 제거 분기를 두지 않는다.
- 렌더러나 Material에 접근하지 않는다.

### 9.2 `System_AttackTelegraph`

```text
Phase: Update
Priority: 900
책임: ECS 예고 데이터 → 중앙 Batch View 입력 스냅샷
```

- `AttackTelegraphComponent` SparseSet만 순회한다.
- `ComponentManager`, 필요한 Pool과 Batch View 참조를 캐시한다.
- `for`를 사용하고 프레임당 임시 컬렉션을 생성하지 않는다.
- 플레이어 위치, 활성화 상태, 카메라 여백으로 표시 대상을 제한한다.
- 중심·방향·현재 진행 상태를 View에 전달한다.
- 게임플레이 타이밍이나 피해를 변경하지 않는다.
- 도형과 중단 판정을 자체 구현하지 않고 §9.4의 공유 정본을 호출한다.

Priority 900은 `System_Jump` 800 이후의 착지 위치를 읽고 `System_Render` 1000 이전에 시각 버퍼를 갱신하기 위한 값이다.

### 9.3 `AttackTelegraphBatchView`

- Scene/Bootstrap에 하나만 존재하는 전용 View다.
- mutable Mesh, 정점/인덱스 버퍼, MaterialPropertyBlock과 슬롯 매핑을 소유한다.
- 게임플레이 컴포넌트나 테이블을 직접 조회하지 않는다.
- Reset 시 Mesh와 활성 슬롯을 모두 비운다.
- 보스 복합 VFX를 관리하지 않는다.

(2026-10-03 확인: Scene/Bootstrap에 배치하지 않는다. `System_AttackTelegraph.OnCreate`가 `AR.s.System` GameObject에서 View를 찾고 없으면 `AddComponent`로 붙인다. View는 MeshFilter/MeshRenderer 없이 `Graphics.DrawMesh`로 메인 카메라에 제출하고, Material은 `Resources.Load("AttackTelegraphMaterial")`(`Assets/Resources/AttackTelegraphMaterial.mat`)로 얻는다. 정점은 `Mesh.SetVertices` 등 List 기반 API로 갱신한다(§10.1의 `SetVertexBufferData` 아님). 전용 Sorting Layer는 지정하지 않으며, 코드 주석상 Z 오프셋(`GROUND_OVERLAY_Z = -0.001`)과 Material 렌더 큐로 지형(sorting order 0) 위·엔티티(sorting order 1) 아래에 둔다 — `Assets/Scripts/Common/Visual/AttackTelegraphBatchView.cs`)

### 9.4 두 시스템이 공유하는 정본

같은 규칙을 `System_Skill`(게임플레이)과 `System_AttackTelegraph`(표시)에 각각 구현하면
표시와 판정이 조용히 어긋난다. 아래 규칙은 한 곳에만 둔다.

| 정본 | 위치 | 내용 |
|---|---|---|
| 타격 유무·착지 커밋 여부 | `SkillTable.HasDirectImpact` / `SkillTable.UsesLandingCommit` | 실제 데미지는 `BaseDamageMul × (무기/스탯 Added + DamageMin/Max)`이므로 `DamageMin/Max`가 0이어도 Attack/Spell 스킬은 타격한다. 플랫 데미지로 판단하지 않는다. |
| 커밋 스케줄 | `SkillImpactTimingResolver` | 효과 유형별 첫/반복 커밋 오프셋 순수 계산 |
| 도형 수치 | `AttackTelegraphGeometryResolver` | 좌표·반경·각도만 받는 순수 계산 |
| 도형 입력 조립·중심 | `AttackTelegraphRuntimeGeometryResolver` | Collider/Stat/Table을 읽어 위 순수 계산의 입력을 만든다. 생성과 매 프레임 갱신이 같은 함수를 쓴다. |
| 중단 판정 | `SkillInterruptRule` | DestroyTag·Stun 이상 상태·사망·거리 비활성화·출신 청크 Warm 전환. 스킬 취소와 예고 제거가 같은 조건을 본다. |

### 9.5 `EffectManager`와의 경계

`EffectManager`의 AddressablePool은 타격 플래시, 폭발, 보스 전용 희소 문양에 사용한다. 모든 일반 예고 범위를 풀링 프리팹으로 만드는 경로는 채택하지 않는다. 풀링은 생성 비용을 줄일 뿐 몬스터별 Transform/Renderer 업데이트와 투명 오버드로우를 제거하지 못한다.

---

## 10. 배치 렌더링 설계

### 10.1 MVP: 단일 동적 Mesh

하나의 `MeshFilter + MeshRenderer`에 활성 예고의 삼각형을 합친다.

- 원: 단위 원 fan mesh 또는 적절한 세그먼트의 ring mesh.
- 부채꼴: 중심 + 호 세그먼트.
- 직선: 4정점 quad 또는 capsule 근사.
- 동일 Material과 Texture Atlas를 사용해 기본 1 draw call을 목표로 한다.
- `Mesh.MarkDynamic`, 사전 할당 배열, `SetVertexBufferData`, `SetIndexBufferData`를 사용한다.
- 활성 개수 변경·형태 변경·FollowOwner 이동 시 필요한 버퍼만 갱신한다.
- 시간 진행은 매 프레임 정점 색을 다시 쓰지 않고 셰이더에서 계산한다.

정점 스트림 예시:

| 스트림 | 데이터 |
|---|---|
| Position | 월드 위치 또는 배치 기준 위치 |
| UV0 | 도형 내부 정규화 좌표 |
| UV1 | `WarningStartFixedTime`, `NextCommitFixedTime` |
| UV2 | 반각, 스타일/형태 식별값 |
| Color32 | 기본색, 위험도, 알파 |

### 10.2 셰이더

- URP 2D 호환 Unlit 단일 패스.
- `ZWrite Off`, 그림자 없음, 고정 Sorting Layer.
- 외곽선, 옅은 위험 면, 진행 면, 마지막 펄스를 한 패스에서 계산한다.
- 전역 현재 시간 하나와 정점별 시작/커밋 시간을 사용한다.
- Material 인스턴스를 공격마다 만들지 않는다.
- 큰 원을 사각 quad 전체로 그려 버리는 방식보다 실제 원/부채꼴 삼각형으로 빈 프래그먼트 오버드로우를 줄인다.

### 10.3 확장 조건

다음 조건이 Profiler에서 확인될 때만 GPU Instancing/StructuredBuffer 방식으로 교체한다.

- 100개 이상 예고가 동시에 이동하며 동적 Mesh 업로드가 병목이다.
- Mesh 재구성 CPU 시간이 목표 예산을 지속적으로 초과한다.
- 도형별 draw call보다 버퍼 관리 비용이 더 커진다.

ECS 컴포넌트와 Timing 계약은 렌더링 구현과 분리하므로 View만 교체할 수 있어야 한다.

---

## 11. 컬링과 성능 정책

### 11.1 조회 범위

전체 `MonsterTag`나 `TransformComponent` 풀을 렌더 목적으로 매 프레임 순회하지 않는다. 활성 공격만 들어있는 `AttackTelegraphComponent` 풀을 순회한다.

표시 조건:

```text
MonsterTag 보유
AND 스킬 실행 중
AND 공격 커밋 전 또는 다음 반복 커밋 존재
AND 출신 청크 Active
AND ActivationDistanceComponent.IsActivated
AND 카메라 확장 경계와 도형 Bounds가 교차
```

카메라 밖 시전자의 공격이라도 도형 Bounds가 화면 안에 들어오면 표시해야 한다. 시전자 위치만으로 컬링하지 않는다.

### 11.2 할당과 업데이트

- 런타임 프레임당 관리 힙 할당 0 B를 목표로 한다.
- `List<T>` 임시 생성, LINQ, Coroutine, 공격별 Material 생성 금지.
- 도형 버퍼와 카메라 컬링 버퍼는 재사용한다.
- 거리 비교는 `sqrMagnitude`를 사용한다.
- 표시 진행률은 셰이더 시간으로 처리해 CPU의 공격별 색상 업데이트를 피한다.
- FollowOwner 또는 연속 조준 스킬만 위치/방향 버퍼를 매 프레임 갱신한다.

### 11.3 예산과 LOD

성능 상한 때문에 실제 위험 공격을 숨기지 않는다. LOD 순서는 다음과 같다.

1. 플레이어와 교차 가능한 공격: 전체 면 + 외곽선 + 진행 표시.
2. 화면 안이지만 플레이어와 먼 공격: 옅은 면 + 외곽선.
3. 카메라 여백만 교차하는 공격: 외곽선만.
4. 카메라 확장 경계와 교차하지 않음: 렌더 제외.

초기 프로파일 기준은 25/50/100 동시 예고이며 실제 몬스터 밀도와 보스 소환 패턴으로 다시 측정한다.

(2026-10-03 확인: LOD는 미적용. `AttackTelegraphRenderDetail`(`Full`/`SurfaceAndOutline`/`OutlineOnly`) enum과 정점 전달 경로는 있으나 `System_AttackTelegraph`가 항상 `Full`로 제출한다. 4번 "렌더 제외"에 해당하는 카메라 확장 경계(여백 2) 대 도형 Bounds 원 교차 컬링만 구현됐다 — `Assets/Scripts/Common/System/System_AttackTelegraph.cs`)

---

## 12. 선행 수정 사항

### 12.1 지속 장판 커밋 통일

현행 `OnEnterProcessState`에서 즉시 `CreateAreaEffect`를 호출하는 경로를 첫 커밋 시점으로 이동한다.

목표 순서:

```text
스킬 Start
→ 예고 표시 시작
→ 공통 Resolver의 첫 커밋 도달
→ 예고 100%/플래시
→ AreaEffect 엔티티 생성
→ System_AreaEffect 첫 틱
→ 활성 장판 시각 유지
```

AreaEffect 생성은 한 실행에서 한 번만 보장해야 한다. MultiHit 데이터가 잘못 조합되어도 장판 엔티티를 매 히트 생성하지 않도록 별도 적용 플래그 또는 효과 실행 계약을 둔다.

(2026-10-03 확인: 반영됨. `OnEnterProcessState`의 즉시 생성 경로는 없어졌고, 공통 스케줄의 커밋 처리에서 `SkillComponent.IsAreaEffectCreated`가 false일 때만 `EntityFactory.CreateAreaEffect`를 호출한 뒤 플래그를 세운다. 플래그는 스킬 명령 처리(`ProcessSkillCommands`) 시 false로 되돌린다 — `Assets/Scripts/Common/System/System_Skill.cs`)

### 12.2 발사체 로직과 시각 로딩 분리

현행 `ProjectileHelper.SpawnProjectile`은 AddressablePool의 GameObject를 기다린 다음 ECS 발사체를 만든다. 풀 미예열 또는 로드 지연이 Release 이후 실제 이동 시작 시간을 바꿀 수 있다.

다음 중 하나를 적용한다.

1. 모든 몬스터 발사체 프리팹을 전투 진입 전에 Preload한다.
2. 권장: 커밋 FixedUpdate에 Projectile ECS 엔티티를 즉시 생성하고, GameObject 시각은 비동기로 연결한다. 늦게 도착한 시각은 엔티티 세대/존재를 검증한 뒤 등록한다.

게임플레이 타이밍은 Addressable 로딩 시간과 독립적이어야 한다.

(2026-10-03 확인: 권장안 2로 반영됨. `ProjectileHelper.SpawnProjectile`이 Release FixedUpdate에 논리 엔티티를 먼저 만들고, `AttachProjectileViewAsync`가 GameObject를 비동기로 붙인다. 늦게 도착한 View는 `DestroyTag`·Owner·ProjectileTableId·`VisualRequestToken`이 일치할 때만 등록하고 아니면 풀에 반환한다 — `Assets/Scripts/Common/Utility/ProjectileHelper.cs`)

### 12.3 점프 피해 커밋

착지 공격은 `System_Jump`의 착지 완료와 `System_Skill`의 피해 커밋 사이에 명시적인 연결이 필요하다. 현재 Process 시간 비율만으로 타격하면 프레임·충돌 보정에 따라 실제 착지 위치와 피해 중심이 어긋날 수 있다.

Landing 이벤트 또는 컴포넌트 상태를 게임플레이 정본으로 사용하고 예고도 같은 착지 위치를 소비한다.

(2026-10-03 확인: 반영됨. `System_Jump`가 착지 시 보정된 위치를 `SkillLandingCommitComponent.LandingPosition`으로 스킬 엔티티에 붙이고, 예고의 `TargetPosition`/`NextCommitFixedTime`도 함께 갱신한다. `System_Skill`은 이 컴포넌트가 있을 때 착지 커밋을 한 번 실행한 뒤 제거한다. 적용 대상은 `SkillTable.UsesLandingCommit`(Jump·`ArcHeight > 0`·비채널·직접 타격)이다 — `Assets/Scripts/Common/System/System_Jump.cs`, `Assets/Scripts/Common/System/System_Skill.cs`, `Assets/Scripts/Common/Component/SkillLandingCommitComponent.cs`)

---

## 13. 구현 파일 계획

### 13.1 신규 파일

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/Common/Component/AttackTelegraphComponent.cs` | 예고 도형·타이밍 순수 데이터 |
| `Assets/Scripts/Skill/Telegraph/SkillImpactTimingResolver.cs` | 효과 유형별 커밋 스케줄 순수 계산 |
| `Assets/Scripts/Skill/Telegraph/AttackTelegraphGeometryResolver.cs` | 좌표·반경·각도만 받는 도형 순수 계산 |
| `Assets/Scripts/Skill/Combat/AttackTelegraphRuntimeGeometryResolver.cs` | 살아있는 Collider/Stat/Table에서 위 순수 계산의 입력과 중심 좌표 조립 |
| `Assets/Scripts/Skill/Combat/SkillInterruptRule.cs` | 시전자 측 스킬 중단 사유 판정 (취소·예고 제거 공용) |
| `Assets/Scripts/Common/System/System_AttackTelegraph.cs` | 활성 예고 ECS 데이터를 View로 전달 |
| `Assets/Scripts/Common/Visual/AttackTelegraphBatchView.cs` | 동적 Mesh와 재사용 버퍼 소유 |
| `Assets/Shaders/AttackTelegraphUnlit.shader` | 위험 면·외곽선·진행률 단일 패스 렌더링 |
| `Assets/Tests/EditMode/SkillImpactTimingResolverTests.cs` | 속도·DamageTime·다단·채널 스케줄 검증 |
| `Assets/Tests/EditMode/AttackTelegraphGeometryResolverTests.cs` | 원·부채꼴·발사체 경로와 Collider 반경 검증 |

파일 경로는 실제 asmdef와 테스트 구조 확인 후 조정할 수 있지만 각 책임은 합치지 않는다. `*Helper`, 범용 `*Manager` 같은 grab-bag 클래스를 만들지 않는다.

(2026-10-03 확인: 테스트 2종을 제외한 신규 파일은 모두 존재한다. `Assets/Tests` 폴더와 EditMode 테스트는 없다. 대신 `Assets/Scripts/Editor/AttackTelegraphBuildValidator.cs`(`IPreprocessBuildWithReport`, 메뉴 `ARPG/Validation/Attack Telegraph Build Contract`)가 빌드 직전에 Resolver 정본 계약(`ValidateCanonicalResolverContracts`)과 몬스터 AI가 참조하는 스킬 테이블(`ValidateMonsterSkillTables`)을 검사하고, 실패하면 빌드를 중단한다. 계획에 없던 추가 파일: `SkillLandingCommitComponent.cs`, `Assets/Resources/AttackTelegraphMaterial.mat`.)

### 13.2 수정 파일

| 파일 | 변경 |
|---|---|
| `System_Skill.cs` | 공통 Timing Resolver 사용, 예고 생성/갱신/제거, AreaEffect 커밋 이동 |
| `System_AreaEffect.cs` | 생성 직후 첫 틱 계약 검증 |
| `ProjectileHelper.cs` / `EntityFactory.cs` | 발사체 로직 엔티티와 비동기 View 로드 분리 |
| `SystemManager.cs` | `System_AttackTelegraph` priority 900 등록 |
| `ComponentManager.cs` | Telegraph 컴포넌트 풀 크기 등록 |
| Scene/Bootstrap 또는 전용 Prefab | `AttackTelegraphBatchView` 단일 인스턴스 연결 (2026-10-03 확인: 미사용 — `System_AttackTelegraph.OnCreate`가 런타임에 `AR.s.System`에 붙인다, §9.3) |

복합 도형을 데이터로 지정해야 할 때만 `Tables.cs`, `DownloadTables.cs`, Skill 시트를 후속 수정한다. MVP 자동 형태만으로 기존 몬스터 스킬을 먼저 검증한다.

---

## 14. 구현 순서

> 2026-10-03 코드 대조 기준 진행 표시: ✅ 구현 / ◐ 부분 / ❌ 미구현. 각 Phase "완료 조건"의 PlayMode 일치 검증 기록은 없다.

### Phase 0 — 게임플레이 커밋 정본화 ✅ *(3d85ce7, 2026-09-19)*

1. ✅ `SkillImpactTimingResolver` 추가.
2. ✅ 직접/다단 타격이 Resolver를 사용하도록 변경.
3. ◐ 채널 첫 틱 정책 확정과 대표 콘텐츠 마이그레이션. — 정책(`DamageTime` 기준)은 코드 반영, 콘텐츠 재측정은 미확인 (§7.3)
4. ✅ AreaEffect 생성을 첫 커밋으로 이동. (§12.1)
5. ✅ 착지 공격 커밋 연결. (§12.3)
6. ✅ 발사체 게임플레이 생성과 비동기 View 로드 분리 또는 사전 로드 보장. (§12.2)

완료 조건: 표시가 없어도 기존 공격들의 실제 효과 발생 시점을 자동 테스트로 설명할 수 있다. (2026-10-03 확인: 자동 테스트 대신 빌드 검증기가 Resolver 계약을 검사한다, §13.1)

### Phase 1 — 예고 데이터와 기본 View ✅

1. ✅ `AttackTelegraphComponent` 추가와 풀 크기 등록.
2. ✅ 스킬 시작/취소/완료/커밋 생명주기 연결.
3. ✅ 단일 Circle/Sector 동적 Mesh View 구현.
4. ✅ 위험 면·외곽선·진행 면 셰이더 구현.
5. ✅ `System_AttackTelegraph` priority 900 등록.

완료 조건: `StrikeMonster`와 자폭 공격의 표시 100%가 실제 피해 FixedUpdate와 일치한다.

### Phase 2 — 발사체·장판·반복 공격 ✅

1. ✅ ProjectileLane/ProjectileFan.
2. ✅ TargetCircle과 활성 장판 전환. — 정책 변경으로 대체: 장판은 예고 `None`, TargetCircle은 준비 시간 장판용으로 예약만 함 (§5.3)
3. ✅ MultiHit 다음 타격 펄스. — 커밋마다 `AdvanceAttackTelegraph`가 다음 커밋으로 진행률을 재시작
4. ✅ Channeling 반복 펄스와 연속 방향 갱신.

완료 조건: 모든 현행 몬스터 공격 스킬이 누락 없이 올바른 예고 정책을 갖는다.

### Phase 3 — 혼잡도·접근성·콘텐츠 검증 ◐

1. ✅ 화면 여백/도형 Bounds 컬링. — 카메라 여백 2, Bounds 원 대 확장 사각형 교차
2. ❌ 거리별 outline-only LOD. — enum만 있고 항상 `Full` (§11.3)
3. ◐ 겹침 알파와 Sorting Layer 튜닝. — Z 오프셋·렌더 큐로 지형 위 배치(§9.3). 겹침 알파 튜닝 여부는 미확인
4. ◐ 마지막 경고 음향과 취소 연출. — 셰이더 마지막 펄스만 있고 음향·취소 페이드 없음 (§4.1)
5. ❌ 최소 가독 시간 Editor 진단. (§7.5)

### Phase 4 — 프로파일과 필요 시 렌더러 교체 ❌

1. 25/50/100 동시 예고 테스트 씬 또는 진단 메뉴.
2. CPU, Render Thread, Batches, SetPass, GC Alloc, GPU Overdraw 측정.
3. 동적 Mesh 업로드가 실제 병목일 때만 Instancing/StructuredBuffer View로 교체.

---

## 15. 검증 계획

### 15.1 EditMode

- `DamageTime=0/0.5/1`의 첫 커밋 오프셋.
- 공격속도·시전속도 적용 후 표시 시간과 실제 시간 일치.
- MultiHit의 모든 `nextHitOffset`.
- Channeling의 최초/반복 틱.
- Direction 45가 전체 90도 부채꼴로 계산됨.
- Range에 owner/player HitRadius가 정확히 한 번씩 더해짐.
- AreaEffect Radius와 TargetPosition 사용.
- Projectile 다중 발사 스프레드와 경로 폭.
- 0 방향 입력의 fallback.

### 15.2 PlayMode

- `StrikeMonster`: 표시 시작, 진행, 피해, 제거 순서.
- 자폭: 몬스터 이동/사망/취소 시 중심과 제거 동작.
- 원거리: 100%에서 발사되고 이후 투사체가 회피 정보 인계.
- Poison/AreaEffect: 예고가 100%에 닿기 전 첫 틱 없음, 100%에 닿는 FixedUpdate에 첫 틱.
- MultiHit/Channeling: 매 타격 전 펄스와 실제 피해 일치.
- Stun·사망·강제 Reset에서 고아 표시 0개.
- Active → Warm 청크 전환과 재진입에서 고아/중복 표시 0개.
- 공격속도 변경 전후 실제 타격과 표시의 동기화.
- 낮은/높은 프레임레이트에서 FixedUpdate 커밋 일치.

### 15.3 성능

Warm-up 이후 다음을 측정한다.

- 관리 힙 할당: 0 B/frame.
- 일반 도형 Material: 기본 1 draw call 목표, 기능상 필요한 경우 형태별 최대 3 draw call 허용.
- 공격별 GameObject/Renderer/Coroutine 생성: 0.
- 25/50/100 동시 예고에서 `System_AttackTelegraph` CPU 시간.
- 동적 Mesh 업로드 시간과 GPU 투명 오버드로우.
- 카메라 밖 대형 범위가 Bounds 교차 기준으로 올바르게 포함/제외되는지.

성능 수치의 절대 합격값은 목표 플랫폼과 실제 전투 씬에서 기준선을 측정한 뒤 고정한다.

---

## 16. 완료 조건

- 플레이어가 처음 보는 일반 공격도 “밝은 진행 면이 외곽선에 닿으면 피해/발사”라는 규칙으로 회피 타이밍을 판단할 수 있다.
- 표시 범위와 직접 공격 판정이 Collider 반경을 포함해 일치한다.
- 공격속도·시전속도·DamageTime 변경 시 표시와 실제 커밋이 함께 바뀐다.
- 직접, 다단, 채널, 장판, 발사체, 착지 공격의 커밋 의미가 문서와 테스트에 고정돼 있다.
- 공격 취소·사망·Stun·청크 비활성화·Reset에서 표시가 남지 않는다.
- 일반 공격 예고는 몬스터별 GameObject 없이 중앙 배치로 렌더링된다.
- 대표 최대 부하에서 프레임당 GC 할당이 없고, 목표 draw call 및 CPU/GPU 예산을 충족한다.

---

## 17. 관련 문서와 코드

- `Docs/combat/SKILL_DESIGN.md` — SkillTable 타이밍·타겟·효과 의미.
- `Docs/combat/POE2_CURSOR_TRACKING_PLAN.md` — 조준 추적과 커밋 정책.
- `Docs/combat/combatSystem.md` — 전투 상태와 공격속도 계약.
- `Wiki/skill_aim.md` — 현행 조준 구현 종합.
- `Wiki/monster_spawn_runtime.md` — 몬스터 Active/Warm/Expired 생명주기.
- `Assets/Scripts/Common/System/System_Skill.cs` — 실제 상태 전이와 피해 커밋.
- `Assets/Scripts/Common/Utility/HitboxMath.cs` — 원/부채꼴 판정.
- `Assets/Scripts/Common/System/System_AreaEffect.cs` — 활성 장판 틱.
- `Assets/Scripts/Common/Utility/ProjectileHelper.cs` — 발사체 생성.
- `Assets/Scripts/Common/System/System_EntityActivation.cs` — 거리·청크 기반 활성화.
- `Assets/Scripts/Manager/EffectManager.cs` — 희소 VFX용 AddressablePool 경로.

