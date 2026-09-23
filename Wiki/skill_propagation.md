---
title: 스킬 전파 (연쇄·분열)
sources:
  - Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md
  - Docs/combat/SKILL_DESIGN.md
  - Assets/Scripts/Common/System/System_SkillPropagation.cs
  - Assets/Scripts/Common/System/System_InstantSkillDelivery.cs
  - Assets/Scripts/Common/System/System_Projectile.cs
  - Assets/Scripts/Common/System/System_EntityDestroy.cs
  - Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs
  - Assets/Scripts/Skill/Propagation/SkillPropagationTargetSelector.cs
  - Assets/Scripts/Skill/Propagation/SkillDeliveryFactory.cs
  - Assets/Scripts/Skill/Combat/SkillHitProcessor.cs
  - Assets/Scripts/Common/Component/SkillPropagationRequestComponent.cs
  - Assets/Scripts/Common/Component/SkillPropagationBranchComponent.cs
  - Assets/Scripts/Common/Component/InstantSkillDeliveryComponent.cs
  - Assets/Scripts/Common/Component/SkillPropagationTargetIds.cs
  - Assets/Scripts/Common/Utility/SkillStatBonusHelper.cs
  - Assets/Scripts/Manager/EffectManager.cs
  - Assets/Scripts/Editor/DownloadTables.cs
  - Assets/_BinaryData/TableData/SkillPropagationTable.bytes
last_synced: 2026-09-23
status: draft
---

# 스킬 전파 (연쇄·분열)

## 한 줄 요약

연쇄와 분열을 별도 실행 타입으로 만들지 않고 `SkillPropagation` 효과 하나로 표현한다. 효과가 시작되면 전파 루트가 방문 대상 장부를 들고, 대기 중인 요청(주문)이 직전 적중 대상에 붙어 있다가 `HopInterval` 뒤 다음 대상을 골라 Instant 또는 Projectile로 전달한다. 첫 콘텐츠는 Instant 4연쇄 Chain Lightning이다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §1.1·§3.4, Assets/_BinaryData/TableData/SkillPropagationTable.bytes]

## 문서 정본

구현 계약·데이터 스키마·런타임 흐름·검증 계획의 정본은 `SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md`다. 2026-09-23 코드 리뷰로 바뀐 계약은 같은 문서 §13에 요약돼 있다. `SKILL_DESIGN.md`는 Chain Lightning의 카탈로그 재도입 방침과 이 계획서로의 포인터만 가진다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §0·§13, Docs/combat/SKILL_DESIGN.md]

## 왜 이렇게 설계했나

- **결정: 주문과 투사체는 모두 월드 위치(Transform)를 가진다.** 지금 위치를 읽는 로직이 없어도 유지한다. "지역 안의 주문 해제" 같은 위치 기반 스킬이 Transform 풀로 이 엔티티들을 찾을 수 있어야 하기 때문이다. 위치의 정본은 Transform 하나이고, 같은 위치를 별도 필드로 두지 않는다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §1.6]
  - 버린 대안: 코드 리뷰는 즉발 전달 엔티티의 Transform을 "읽는 곳이 없다"는 이유로 제거하자고 제안했다. 위 원칙에 따라 기각했다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §13]
- **결정: 대기 중인 주문은 맞은 대상에 붙어서 따라간다.** 요청은 `HopInterval` 동안 직전 적중 대상의 Collider 보정 중심으로 이동한다. 대상이 제거되면 마지막 위치에서 다음 대상으로 튀고 체인은 끊기지 않는다. 한 번 끊은 추적은 재개하지 않아서, 같은 ID를 받은 다른 엔티티를 따라가지 않는다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §1.6, Assets/Scripts/Common/System/System_SkillPropagation.cs:L55-75]
  - 버린 대안: 맞은 자리에 고정하는 방식. 대기 시간을 늘리면 번개가 대상이 떠난 빈자리에서 튀어나가 보인다.
  - 버린 대안: 대상 사망 시 체인 종료. 처치하며 이어지는 연쇄감을 우선했다.
- **결정: 전달 엔티티를 따로 만든다.** 이유는 세 가지다. Instant와 Projectile이 같은 branch 파이프라인을 공유한다. `OutstandingDeliveryCount`의 작업 단위를 엔티티로 추적해 외부 파괴에도 카운트가 새지 않는다. 대상 선택(210)과 적중 처리(220)가 분리된다. 마지막 분리가 없으면 요청 처리의 루트 지역 복사본이 적중 중 `TryQueueRequest`가 쓴 루트 변경을 덮어쓴다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §4.4, Assets/Scripts/Common/System/System_SkillPropagation.cs:L77-129, Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs:L115-156]
- **결정: 테이블 필드 범위 검증은 파서가 단독으로 책임진다.** 파서가 범위 밖 값을 클램프해 저장하므로 런타임 재검사는 절대 실패하지 않는 중복이었다. 오히려 상한 숫자가 두 곳에 박혀 한쪽만 바꾸면 테이블이 전부 거부되는 위험이 있어 제거했다. 트리거에 따라 결과가 달라지는 이론 전달 수 검사(`IsDeliveryCapValid`)만 런타임에 남긴다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §1.5, Assets/Scripts/Editor/DownloadTables.cs:L1490-1523, Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs:L100-113·L229]
- **결정: 커밋 순간 대상이 없으면 루트를 만들지 않는다.** 대기 중 범위로 걸어 들어온 적을 잡지 않게 하려는 의도적 검사다. 요청 처리에서 한 번 더 탐색하는 것은 중복이 아니다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §5.1, Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs:L30-46]

## 어떻게 동작하나

### 엔티티 구성

| 엔티티 | 컴포넌트 | 수명 | 위치 |
|---|---|---|---|
| 루트 | `SkillPropagationComponent` (시전자·스킬·테이블·방문 목록·작업 수) | 체인 시작~작업 수 0 | 없음 (장부) |
| 요청 (대기 중인 주문) | `SkillPropagationRequestComponent` + Transform | `HopInterval` 동안 | 직전 대상에 붙어 이동 |
| Instant 전달 | `InstantSkillDeliveryComponent` + branch + Transform | 같은 FixedUpdate 안 | source 위치 |
| Projectile 전달 | 투사체 컴포넌트 + branch(`AimedTargetEntityId`) | 적중·만료까지 | 비행 |

[src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §4, Assets/Scripts/Common/Component/SkillPropagationRequestComponent.cs, Assets/Scripts/Common/Component/InstantSkillDeliveryComponent.cs, Assets/Scripts/Common/Component/SkillPropagationBranchComponent.cs]

### 한 hop의 흐름

1. A가 적중하면 요청이 생긴다. 요청의 Transform은 A 위치에서 시작하고, A에 붙어 따라간다. [src: Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs:L115-156]
2. `HopInterval`이 끝나면 요청 Transform 위치에서 가장 가까운 적대 대상을 고른다. 이미 방문한 대상, 각도 제한 밖의 대상은 제외한다. [src: Assets/Scripts/Common/System/System_SkillPropagation.cs:L77-129, Assets/Scripts/Skill/Propagation/SkillPropagationTargetSelector.cs]
3. `SkillDeliveryFactory`가 전달을 만들고, 같은 FixedUpdate 220에서 Instant 적중을 처리한다. 적중 처리는 `SkillHitProcessor`를 거쳐 피해 판정과 OnHit/OnCrit/OnKill 발화까지 한다. [src: Assets/Scripts/Skill/Propagation/SkillDeliveryFactory.cs, Assets/Scripts/Common/System/System_InstantSkillDelivery.cs, Assets/Scripts/Skill/Combat/SkillHitProcessor.cs]
4. 남은 깊이가 있으면 B 기준으로 1번부터 반복한다.

시간차는 요청 대기에서만 생긴다. 첫 값 `HopInterval=0.08`에서는 4연쇄가 0.32초 안에 끝나고 번개 연출이 3~4개 겹쳐 동시 타격처럼 보였다. 그래서 연출 본체가 보이는 시간에 맞춰 0.2로 바꿨다(4연쇄 약 0.8초). [src: Assets/_BinaryData/TableData/SkillPropagationTable.bytes, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §3.4·§5.4]

### 안전장치

- **방문 목록 Capacity 16 → `DeliveryType`별 `MaxTotalDeliveries` 상한:** Instant 15, Projectile/Inherit 7. 계산은 `GetMaxTotalDeliveries` 한 곳에 있다. [src: Assets/Scripts/Common/Component/SkillPropagationTargetIds.cs:L34-40]
- **전파 Projectile의 방문 대상 통과:** 자식 투사체는 직전 대상 위치에서 생성되므로, `VisitedTargets`에 있으면서 겨냥 대상이 아닌 적을 통과한다. [src: Assets/Scripts/Common/System/System_Projectile.cs:L127-148]
- **`DestroyTag` 가드:** 제거 예약된 투사체가 한 프레임 안에서 FixedUpdate를 두 번 도는 경우 재처리하지 않는다. [src: Assets/Scripts/Common/System/System_Projectile.cs:L54]
- **전파 프로필 중복:** 같은 트리거에 활성 전파 프로필이 2개 이상이면, 스킬 생성 시 에러를 1회 기록하고 그 트리거의 전파를 모두 제외한다. [src: Assets/Scripts/Common/Utility/SkillStatBonusHelper.cs:L111]
- **`SkillHitRequest` 필수 값:** 피해 배율 등 필수 값은 생성자로만 받는다. 값이 빠져서 피해가 1로 조용히 붕괴하는 것을 막는다. [src: Assets/Scripts/Skill/Combat/SkillHitProcessor.cs:L31]
- **외부 파괴:** 요청이나 전달이 정상 경로 밖에서 파괴되면 `System_EntityDestroy`가 작업 수를 대신 해제한다. [src: Assets/Scripts/Common/System/System_EntityDestroy.cs:L51-65]

### 연출

- 번개 구간은 호출마다 풀에서 별도 인스턴스를 꺼내므로 겹쳐 재생된다. [src: Assets/Scripts/Manager/EffectManager.cs:L118]
- 임시 대체 연출은 없다. 키가 등록되지 않았으면 경고를 1회 찍고 연출 없이 진행한다. [src: Assets/Scripts/Manager/EffectManager.cs:L118]
- 커밋 시 대상이 없을 때와, 최초 요청이 대기 중 대상을 잃었을 때는 허공 연출을 재생한다. [src: Assets/Scripts/Skill/Propagation/SkillPropagationRuntime.cs:L205, Assets/Scripts/Common/System/System_SkillPropagation.cs:L123]

## 연결된 것들

- 커밋 방향·위치를 공급하는 조준 파이프라인: [skill_aim.md](skill_aim.md)
- 피해 계산 경로(`DamageCalculator`)와 공격 프로필: [combat.md](combat.md)
- 전체 구현 현황: [current_status.md](current_status.md)

## 미해결 / 주의

- `HopInterval=0.2`의 연쇄감은 플레이 테스트로 확인해야 한다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §3.4]
- 현행 데이터는 Instant 행 하나뿐이다. Projectile 연쇄·분열, Instant 분열, OnHit 시작 경로는 컴파일만 확인했고 PlayMode 검증이 없다. [src: Assets/_BinaryData/TableData/SkillPropagationTable.bytes, Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §10.2]
- 2026-09-23 리뷰 반영분(주문 추적, 허공 연출 보완, 방문 대상 통과 등)은 플레이 테스트 전이다. [추론: 리뷰 세션에서 Unity 컴파일·콘솔 확인만 수행]
- [추론] 번개 프리팹의 `EffectInstance` 자동 수명이 약 4.8초로 계산된다. 보이는 시간(약 0.4초)보다 길게 풀을 점유하므로 `_overrideDuration` 조정을 검토한다. [src: Docs/combat/SKILL_PROPAGATION_IMPLEMENTATION_PLAN.md §8]
- 요청 엔티티가 Transform 풀에 들어가도 투사체 충돌·대상 탐색·`FactionHelper`·`System_Render` 루프는 진영/스탯/EntityBase로 먼저 걸러낸다. 나중에 Transform 풀을 "생명체 목록"으로 가정하는 새 루프를 만들면 주문 엔티티가 섞인다는 점에 주의한다. [추론]
