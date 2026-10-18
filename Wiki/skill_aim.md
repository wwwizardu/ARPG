# PoE2식 스킬 조준 추적

---
name: skill-aim
description: 플레이어 커서·AI 대상 조준의 추적·커밋 정책, 방향 화살표 View, 스킬 사용 중 이동 감속의 현행 런타임 구조.
type: topic
last_updated: 2026-10-15
sources:
  - Docs/combat/POE2_CURSOR_TRACKING_PLAN.md
  - Docs/combat/SKILL_DESIGN.md
  - Docs/combat/MONSTER_ATTACK_TELEGRAPH_DESIGN.md
  - Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md
  - Assets/Scripts/Common/System/System_Input.cs
  - Assets/Scripts/Common/System/System_SkillAim.cs
  - Assets/Scripts/Common/System/System_Skill.cs
  - Assets/Scripts/Common/System/System_SkillMovementPenalty.cs
  - Assets/Scripts/Common/System/System_SkillAimIndicator.cs
  - Assets/Scripts/Base/EntityBase.cs
  - Assets/Scripts/Factory/EntityFactory.cs
  - Assets/Prefabs/Game/Player/SkillAimIndicator.prefab
  - Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset
  - Assets/Scripts/Skill/SkillUsePolicyResolver.cs
  - Assets/Scripts/Skill/Aim/SkillAimResolver.cs
  - Assets/_BinaryData/TableData/SkillTable.bytes
---

## 경계

플레이어의 커서 월드 좌표는 입력 시스템이 프레임마다 한 번 계산해 `InputComponent.MouseWorldPosition`에 보관하고, 스킬 조준 시스템은 FixedUpdate에서 이 값과 시전자 위치를 이용해 `SkillTargetComponent.TargetDirection`을 회전시킨다. 효과 생성과 피해 판정은 렌더 Transform이 아니라 이 ECS 방향을 사용한다. [src: Assets/Scripts/Common/System/System_Input.cs, Assets/Scripts/Common/System/System_SkillAim.cs, Assets/Scripts/Common/System/System_Skill.cs] 2026-10-13부터 같은 시스템이 AI 시전자도 처리하며, 커서 대신 AI가 노리는 대상(`AIComponent.TargetEntityId`)의 발 위치를 쫓는다(아래 "AI 조준 추적"). [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15, Assets/Scripts/Common/System/System_SkillAim.cs `TryGetAiAimPoint`]

조준 로직의 순서는 이동 감속 priority 90 → 이동 priority 100 → 조준 추적 priority 190 → 스킬 실행·커밋 priority 200이다. 화살표는 애니메이션 뒤 priority 510의 Update View가 ECS 방향을 읽어 표시하며, 실제 효과 시스템이나 `EffectManager`의 수명주기에 참여하지 않는다. [src: Assets/Scripts/Manager/SystemManager.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs, Assets/Scripts/Common/Visual/SkillAimIndicatorView.cs]

## 조준 정책과 커밋

| 정책 | 현행 의미 |
|---|---|
| `SnapshotOnCommand` | 명령 시 받은 방향을 유지한다. 빈 칸인 AI 스킬과 비방향성 스킬의 기본값이다(2026-10-13부터 AI도 명시값은 따른다). |
| `TrackUntilCommit` | 시작·선딜 동안 추적하고 첫 효과 커밋 뒤 고정한다. 일반 플레이어 공격·투사체·위치형 스킬의 기본값이다. |
| `TrackPerCommit` | 다단 타격의 각 타격 직전에 방향을 다시 커밋한다. |
| `TrackContinuously` | 채널링의 반복 효과 직전에 현재 방향을 계속 커밋한다. |

정책은 `SkillTable`의 명시값이 우선하며, 빈 값은 타깃 타입·스킬 타입·효과 타입으로 안전한 기본값을 정한다. 회전은 `AimTurnSpeedDegPerSec × fixedDeltaTime`으로 제한되고, 0/360도 경계에서는 최단 각도를 사용한다. [src: Assets/Scripts/Skill/SkillUsePolicyResolver.cs, Assets/Scripts/Skill/Aim/SkillAimResolver.cs, Assets/Scripts/Common/System/System_SkillAim.cs]

커밋은 투사체 생성, 범위 효과 생성, 단일/다단 타격 판정 바로 전에 일어난다. 위치형 스킬은 커밋된 방향과 현재 커서 거리로 사거리 제한 위치를 다시 계산하며, 투사체는 커밋된 방향을 직접 소비한다. Jump는 Start 단계에서 조준을 추적하고 Process 진입을 `LaunchCommit`으로 삼아 그때 이동을 시작한다. [src: Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_Jump.cs] 돌진(`SkillType.Dash`, 2026-10-13)도 같은 `LaunchCommit`에서 방향을 고정하고 출발한다. 돌진 자체는 [combat.md](combat.md)의 돌진 절에 정리한다. [src: Docs/combat/SKILL_DESIGN.md §6.11, Assets/Scripts/Common/System/System_Skill.cs `OnEnterProcessState`·`StartDash`]

## AI 조준 추적 (2026-10-13 구현)

멧돼지 돌진을 만들며 "몬스터도 방향을 갱신하되 느리게 돌아, 넉백으로 밀어내 피할 수 있게" 하기로 했고, 같은 날 다른 몬스터 스킬에도 넓혔다. 이전에는 입력이 없는 시전자가 표 값과 상관없이 `SnapshotOnCommand`였다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15]

- **규칙**: `AimTrackingMode`가 명시된 스킬은 AI에도 그 값을 적용하고, 빈 칸이면 지금처럼 명령 시점에 고정한다. 회전 속도 제한과 커밋 규칙은 플레이어와 같다. 대상이 없거나 사라지면 방향을 그대로 두며, `AIComponent`가 없는 토템은 추적하지 않는다. [src: Assets/Scripts/Skill/SkillUsePolicyResolver.cs `ResolveAimTrackingMode`, Assets/Scripts/Common/System/System_SkillAim.cs]
- **위치형 스킬**: 커밋 때 방향은 회전된 값, 거리는 대상까지의 현재 거리를 `SkillRangeMin` ~ `시전자 HitRadius + SkillRangeMax + 대상 HitRadius`(AI가 스킬을 고르는 사거리)로 제한해 쓴다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15, Assets/Scripts/Common/System/System_Skill.cs `CommitAim`]
- **적용한 스킬(시트)**: 몬스터 평타(2) 90°/초, 원거리 4·204·205와 위치형 207·208은 180°/초, 멧돼지 돌진(52) 45°/초, 모두 `TrackUntilCommit`. 자폭(3)과 빠른 점프(5·6)는 그대로다. 원거리 스킬은 시작 0.1~0.2초 뒤 발사라 체감 차이가 작다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15, Assets/_BinaryData/TableData/SkillTable.bytes]
- **함께 바뀐 것**: NPC도 같은 AI(1·2번)와 스킬(2·4번)을 써서 함께 돈다. 공격 예고는 이미 매 프레임 현재 방향을 따라 그리므로 부채꼴·띠가 같이 돈다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15, Assets/Scripts/Common/System/System_AttackTelegraph.cs]
- **검증**: PlayMode에서 늑대 평타가 고정 틱마다 1.8°(초당 90°), 멧돼지 돌진 준비가 0.9°(초당 45°)씩 돌고 예고 각도가 같았다. 손으로 피해 보는 체감은 확인하지 않았다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §15 검증 결과, Docs/combat/SKILL_DESIGN.md §6.11 검증 결과]

## 후속 범위 설계

무기 도달거리와 범위 증가 옵션은 2026-10-05 **설계 확정·미구현**이다. 거리·반경은 `스킬 기본값 × 무기 배율 × (1 + 범위 증가 합계 / 100)`로 계산하고 부채꼴 각도는 유지한다. 구현 시 위치 커밋의 사용 거리 제한과 실제 타격 형상에 각 용도에 맞는 계산을 연결해야 하며, 현재 조준·커밋 코드에 이미 적용된 규칙으로 읽지 않는다. 상세 정본은 `WEAPON_SKILL_RANGE_DESIGN.md`, 요약은 [combat.md](combat.md)에 있다. [src: Docs/combat/WEAPON_SKILL_RANGE_DESIGN.md §확정 규칙·§현행 코드와 구현 경계]

## 이동 감속과 View

스킬 시작 시 소유자에게 `SkillMovementPenaltyComponent`를 붙이되 첫 FixedUpdate는 배율 1을 유지한다. 이후 Start/Process에서는 정책 배율을 적용하고 End에서는 종료 시간에 맞춰 1로 선형 복구한다. Jump·Dash·Move는 `OwnedMotion`, AI는 기본 `FullSpeed`, 플레이어의 일반 Attack·Spell은 기본 `Reduced`다. 멧돼지 돌진은 준비 중 제자리에 서도록 `Locked`를 명시한다. [src: Assets/Scripts/Skill/SkillUsePolicyResolver.cs `ResolveMovementMode`, Assets/_BinaryData/TableData/SkillTable.bytes] 예외로 `LockedThroughEnd`(2026-10-15 사용자 결정)는 `Locked`처럼 0이지만 End에서 복구하지 않고 0을 유지하다가 스킬이 끝나는 순간 정상 속도가 된다. 몬스터 점프(Skill 6)가 착지 뒤 후딜 1초 동안 완전히 서 있도록 쓴다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §6.1·§6.3, Docs/combat/SKILL_DESIGN.md §4 M1, Assets/Scripts/Skill/SkillUsePolicyResolver.cs `RecoversMovementDuringEnd`, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/_BinaryData/TableData/SkillTable.bytes] 취소·완료·시스템 Reset에서는 감속 컴포넌트를 제거한다. [src: Assets/Scripts/Common/System/System_Skill.cs, Assets/Scripts/Common/System/System_SkillMovementPenalty.cs, Assets/Scripts/Common/System/System_Move.cs, Assets/Scripts/Skill/SkillUsePolicyResolver.cs]

방향 화살표는 공용 `Entity.prefab`과 분리된 플레이어 전용 Addressable `SkillAimIndicator.prefab`이다. `EntityFactory.CreatePlayer`만 이를 플레이어 루트 아래에 인스턴스화하고, `EntityBase`가 View 참조와 인스턴스 핸들을 소유해 플레이어 제거 시 해제한다. 로드 실패 시 표시만 생략하며 스킬 판정은 ECS 방향을 계속 사용한다. View는 가시성과 표시 각도만 담당하고, 조준 가능한 실행 중 스킬이 없거나 플레이어가 Stun이면 숨는다. 화살표 Sprite는 `Resources` 밖의 프로젝트 UI Sprite 경로에 있다. [src: Assets/Prefabs/Game/Creature/Entity.prefab, Assets/Prefabs/Game/Player/SkillAimIndicator.prefab, Assets/Art/Sprites/UI/SkillAimArrow.png, Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset, Assets/Scripts/Base/EntityBase.cs, Assets/Scripts/Factory/EntityFactory.cs, Assets/Scripts/Common/System/System_SkillAimIndicator.cs]

## 데이터와 검증 상태

`SkillTable`은 `AimTrackingMode`, `AimTurnSpeedDegPerSec`, `MovementMode`, `MoveSpeedWhileUsingMul` 네 필드를 포함한다(AN~AQ열). Skill 시트 다운로드 등록 범위는 2026-10-13에 `ForcedMoveImmune`(AS열)을 넣으며 `A:AS`로 늘었다. [src: Assets/Scripts/Editor/DownloadTables.cs `Register<SkillTable>`, Docs/combat/SKILL_DESIGN.md §6.11 검증 결과] 알 수 없는 enum이나 허용 범위를 벗어난 수치는 오류를 기록하고 안전한 기본값으로 폴백한다. [src: Assets/Scripts/Common/Tables.cs, Assets/Scripts/Editor/DownloadTables.cs, Assets/_BinaryData/TableData/SkillTable.bytes]

방향 정규화, 같은 위치 폴백, 회전 속도 제한, 0/360도 경계, 반대 방향, fixedDeltaTime 누적 독립성, 표시 축 보정, 위치 커밋 사거리 계약은 좁은 EditMode 테스트 어셈블리에 고정되어 있다. 실제 플레이에서는 Direction·Position·Area·Projectile·MultiHit·Channeling·Jump 대표 스킬의 화살표 일치, 프레임레이트별 추적, 감속 시작·복구, 취소·사망·Reset 정리를 Unity Test Runner와 PlayMode로 최종 실측해야 한다. [src: Assets/Tests/EditMode/SkillAimResolverTests.cs, Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §0·§검증 체크리스트]

`SkillUsePolicyResolver`의 정책 해석은 `Assembly-CSharp` 소속이라 EditMode 테스트 어셈블리에서 접근할 수 없고, 데이터 계층 asmdef 분리 전까지는 PlayMode 실측으로만 검증한다. [src: Docs/combat/POE2_CURSOR_TRACKING_PLAN.md §10.3·§12]
