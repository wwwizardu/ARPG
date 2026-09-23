# 스킬 전파 시스템 구현 계획

## 0. 문서 목적과 현재 상태

이 문서는 적중 이후 효과가 다른 대상으로 연쇄되거나 여러 갈래로 분열되는 공용 스킬 전파 시스템의 구현 계약이다. 새 세션에서는 이 문서와 현재 코드를 다시 대조한 뒤 아래 단계 순서대로 작업한다.

첫 수직 슬라이스는 위치 지정 즉발 주문인 **Chain Lightning**이다. 같은 기반에서 다음 기능을 지원하는 것이 목표다.

- 즉발 주문 연쇄와 분열
- 투사체 연쇄와 분열
- 스킬 페이지 또는 고유 스킬 효과로 기존 스킬에 전파 부여
- 전파 자식의 정상적인 피해·치명타·회피·막기·OnHit/OnCrit/OnKill 처리

현재 공용 전파 런타임과 첫 수직 슬라이스인 Chain Lightning이 구현되어 있다. Position 조준, 진영 판정, 투사체, 피해 계산, SkillEffect 트리거, Addressable 풀을 재사용하며 후속 전파는 직전 전달 방향의 데이터 기반 각도 제한도 적용한다. 2026-09-23 코드 리뷰 반영 내용은 §13에 정리했다. 현행 데이터는 Instant 행 하나뿐이라 Projectile 전파 경로는 컴파일만 확인했고 PlayMode 검증은 남아 있다.

관련 기준 문서와 코드:

- [SKILL_DESIGN.md](SKILL_DESIGN.md)
- [combatSystem.md](combatSystem.md)
- [POE2_CURSOR_TRACKING_PLAN.md](POE2_CURSOR_TRACKING_PLAN.md)
- [System_Skill.cs](../../Assets/Scripts/Common/System/System_Skill.cs)
- [System_Projectile.cs](../../Assets/Scripts/Common/System/System_Projectile.cs)
- [ProjectileHelper.cs](../../Assets/Scripts/Common/Utility/ProjectileHelper.cs)
- [SkillEffectExecutor.cs](../../Assets/Scripts/Skill/Combat/SkillEffectExecutor.cs)
- [DamageCalculator.cs](../../Assets/Scripts/Skill/Combat/DamageCalculator.cs)
- [FactionHelper.cs](../../Assets/Scripts/Common/Utility/FactionHelper.cs)

## 1. 확정된 설계 결정

### 1.1 전파 모델

연쇄와 분열을 별도 스킬 실행 타입으로 만들지 않는다. 둘 다 `SkillPropagation`으로 표현하고 다음 두 값으로 구분한다.

| 효과 | `ChildCount` | `MaxDepth` | 의미 |
|---|---:|---:|---|
| 연쇄 | 1 | 1 이상 | 적중마다 다음 대상 하나로 진행 |
| 단발 분열 | 2 이상 | 1 | 적중 후 여러 자식 전달 생성 |
| 재귀 분열 | 2 이상 | 2 이상 | 각 자식이 다음 세대에서도 다시 분열 |

`MaxDepth`는 최초 전달 이후 허용되는 추가 전파 세대 수다.

- `OnSkillCommit`에서 시작하는 Chain Lightning은 최초 대상이 generation 0이다. `MaxDepth=3`이면 최대 총 4명을 적중한다.
- `OnHit`에서 시작하는 분열은 원래 적중이 전파 루트 밖의 source hit다. `MaxDepth=1`이면 source hit 이후 자식 세대 하나를 생성한다.

### 1.2 대상 선택

- 최초 Chain Lightning 대상은 커밋된 `TargetPosition` 반경에서 찾는다.
- 이후 대상은 직전 적중 대상의 **탐색 시점 위치**를 원점으로 찾는다. 대기 중인 요청은 `HopInterval` 동안 직전 적중 대상에 붙어 따라가며, 그 대상이 제거되면 마지막 위치에 남는다(§1.6).
- 최초 `OnSkillCommit` 대상은 이전 진행 방향이 없으므로 각도 제한을 적용하지 않는다.
- 이후 대상은 직전 전달 방향에서 `MaxTurnAngleDeg` 이내에 있는 후보만 허용한다. `180`은 기존과 같은 무제한이다.
- `OnHit` 시작은 투사체 진행 방향을 우선 사용하고, 직접 타격은 시전자에서 피격 대상으로 향하는 방향을 사용한다.
- 시전자와 적대인 `FactionComponent` 대상만 허용한다.
- HP가 0 이하이거나 `DestroyTag`가 있거나 점프 무적 높이 이상인 대상은 제외한다.
- 같은 전파 루트에서 이미 적중했거나 같은 세대에 예약된 대상은 다시 선택하지 않는다.
- 거리가 가까운 순서로 선택하고, 제곱 거리가 같으면 Entity ID가 작은 대상을 먼저 선택한다.
- 후보가 없으면 해당 분기는 즉시 종료한다.
- 거리 비교는 `SqrMagnitude`를 사용한다.

### 1.3 피해와 비용

- 마나와 쿨타임은 원본 스킬 실행에서 한 번만 소비한다.
- 전파 자식은 추가 마나와 쿨타임을 소비하지 않는다.
- 각 자식은 기존 `DamageCalculator`를 통해 피해·치명타·회피·막기·저항을 독립 판정한다.
- 각 자식 적중은 `OnHit`, `OnCrit`, `OnKill`을 정상적으로 발화한다.
- 세대별 피해 배율은 기본 스킬 배율 적용 후, 치명타와 방어 계산 전에 적용한다. 물리 피해의 아머 계산도 감소된 실제 유입 피해를 기준으로 해야 한다.
- 최초 구현의 기본 데이터는 세대별 100% 피해다. 감소 배율 지원은 구현하되 Chain Lightning 첫 행은 100%로 둔다.

### 1.4 전달 방식

`SkillDeliveryType`은 다음 세 값으로 시작한다.

| 값 | 동작 |
|---|---|
| `Inherit` | 원본 스킬의 `ProjectileId` 유무로 `Projectile` 또는 `Instant` 결정 |
| `Instant` | 선택된 대상에게 즉발 피해와 구간 연출 적용 |
| `Projectile` | source 위치에서 선택된 대상 방향으로 새 투사체 생성 |

`AreaEffect` 전달은 데이터 구조에 미리 넣지 않는다. 실제 사용 사례가 승인될 때 후속 확장한다.

### 1.5 안전 상한

- 루트의 방문·예약 목록(`SkillPropagationTargetIds`) Capacity는 16이다. 전달 1건이 쓰는 칸 수가 `DeliveryType`마다 달라 `MaxTotalDeliveries` 상한도 타입별로 다르다.

  | `DeliveryType` | 전달 1건당 예약 | 상한 계산 | `MaxTotalDeliveries` 상한 |
  |---|---:|---|---:|
  | `Instant` | 1 (선택 대상만 적중) | (16 − 1) ÷ 1 | 15 |
  | `Projectile` | 2 (선택 대상 + 경로상 실제 적중 대상) | (16 − 1) ÷ 2 | 7 |
  | `Inherit` | 런타임에 Projectile이 될 수 있어 보수적으로 2 | (16 − 1) ÷ 2 | 7 |

  `− 1`은 OnHit 루트가 source hit 대상으로 먼저 쓰는 1칸이다. 계산은 `SkillPropagationTargetIds.GetMaxTotalDeliveries` 한 곳에 두고 파서가 공유한다.
- `ChildCount` 기본 허용 범위는 1~4다.
- `MaxDepth` 기본 허용 범위는 0~8이다.
- 필드 범위 검증은 **테이블 다운로드 파서가 단독으로 책임진다.** 범위 밖 값은 에러 로그와 함께 클램프해 저장하므로 런타임은 범위를 재검사하지 않는다.
- 재귀 분열의 이론적 전달 수가 `MaxTotalDeliveries`를 초과하는지는 바인딩된 트리거(OnSkillCommit/OnHit)에 따라 달라지므로 런타임이 루트 생성 직전에 검사한다(`IsDeliveryCapValid`).
- 한 스킬의 유효 효과 조합에는 같은 트리거의 활성 `SkillPropagation` 프로필을 하나만 허용한다. 여러 개가 합쳐지면 임의 선택하지 않고, 스킬 엔티티 생성 시(`SkillStatBonusHelper.FilterToActionEffects`) 에러를 1회 기록한 뒤 해당 트리거의 전파 효과를 모두 제외한다. 피격마다 재검사하지 않는다.

### 1.6 주문의 위치

스킬이 만든 주문(대기 중인 전파 요청, 즉발 전달)과 투사체는 모두 `TransformComponent`로 월드 위치를 가진다. 지금 위치를 읽는 로직이 없더라도 유지한다. "지역 안의 주문 해제" 같은 위치 기반 스킬이 이 엔티티들을 Transform 풀로 찾을 수 있어야 하기 때문이다.

- 위치의 정본은 `TransformComponent` 하나다. 같은 위치를 별도 필드로 중복 저장하지 않는다.
- `HopInterval` 동안 살아 있는 주문의 실체는 **전파 요청 엔티티**다. 즉발 전달 엔티티는 생성된 FixedUpdate 안에서 처리·제거되므로 해제 대상이 되기 어렵다.
- 요청은 직전 적중 대상(`SourceTargetEntityId`)에 **붙어** 매 FixedUpdate 그 대상의 Collider 보정 중심으로 이동한다. 커밋 위치에서 시작한 최초 요청은 바닥 지점이므로 따라가지 않는다.
- 따라가던 대상에 `DestroyTag`가 붙거나 대상이 사라지면 추적을 끊고 마지막 위치에서 다음 대상으로 튄다. 한 번 끊은 추적은 재개하지 않아, 나중에 같은 Entity ID를 받은 다른 엔티티를 따라가지 않는다.

## 2. 비목표

- `SkillTargetType.SingleEntity`를 복원하지 않는다.
- `SkillExecutionType`에 Chain/Fork를 추가하지 않는다. 전파는 Single/MultiHit/Channeling과 직교하는 효과다.
- 컴포넌트에 `List<int>`나 `HashSet<int>`를 저장하지 않는다.
- 정적 mutable 방문 대상 캐시를 만들지 않는다.
- 첫 구현에서 유도 투사체, 곡선 경로, 벽 반사, 화면 밖 무한 탐색을 지원하지 않는다.
- 자식 전달에 별도 마나·쿨타임·스킬 애니메이션을 재실행하지 않는다.
- 전파 게임플레이를 Addressable VFX 로딩 완료에 의존시키지 않는다.

## 3. 데이터 계약

### 3.1 신규 enum

`Assets/Scripts/Common/GlobalEnum.cs`에 추가한다.

```csharp
public enum SkillDeliveryType : byte
{
    Inherit,
    Instant,
    Projectile,
}
```

기존 enum에는 다음 값을 추가한다.

```text
SkillTrigger.OnSkillCommit
Stat.SkillPropagation
```

`Stat.SkillPropagation`은 현행 `SkillEffectTable.EffectType`이 수치 Stat과 EffectAction을 함께 표현하는 규칙을 따른다.

### 3.2 신규 `SkillPropagationTable`

시트 범위는 A:M 13개 컬럼으로 정의한다.

| 열 | 필드 | 타입 | 의미 |
|---|---|---|---|
| A | `Id` | int | 전파 프로필 ID |
| B | `Name` | string | 식별 이름 |
| C | `DeliveryType` | enum | Inherit/Instant/Projectile |
| D | `ChildCount` | int | 적중 또는 커밋당 생성할 자식 수 |
| E | `MaxDepth` | int | 추가 전파 세대 수 |
| F | `InitialSearchRadius` | float | OnSkillCommit 최초 대상 탐색 반경 |
| G | `SearchRadius` | float | 후속 전파 탐색 반경 |
| H | `HopInterval` | float | source 이벤트 이후 자식 전달 생성까지 지연 |
| I | `DamageRatioPerGeneration` | float | 세대별 피해 배율(%), 100=유지 |
| J | `MaxTotalDeliveries` | int | 루트 전체 전달 생성 상한. `DeliveryType`별 최대값은 Instant 15, Projectile/Inherit 7(§1.5) |
| K | `AllowRepeatTarget` | bool | 첫 구현 데이터는 항상 false |
| L | `SegmentEffectKey` | string | Instant 구간 Addressable 연출 키 |
| M | `MaxTurnAngleDeg` | float | 직전 전달 방향에서 좌우로 꺾을 수 있는 최대 각도. 0=직진, 180=무제한 |

추가 위치:

- `Assets/Scripts/Common/Tables.cs`
- `Assets/Scripts/Data/DataManager_Table.cs`
- `Assets/Scripts/Editor/DownloadTables.cs`
- `Assets/_BinaryData/TableData/SkillPropagationTable.bytes`

`DownloadTables`에는 전체 등록, 단건 다운로드 메뉴, 파서, enum/수치 검증을 함께 추가한다. 수치 검증은 이 파서가 유일한 책임자다(§1.5).

### 3.3 `SkillEffectTable` 연결

전파 효과 행은 다음 계약을 사용한다.

```text
EffectType = SkillPropagation
Kind       = EffectAction
Trigger    = OnSkillCommit 또는 OnHit
Param1     = SkillPropagationTableId
Param2     = 0
Param3     = 0
```

- `OnSkillCommit`: Position 기반 Chain Lightning처럼 원본 기본 타격 대신 전파를 시작한다.
- `OnHit`: 원본 직접 타격 또는 투사체 적중 이후 전파를 시작한다.
- `InferSkillEffectKindFromEffectType` 같은 보조 추론 경로가 있다면 `SkillPropagation`을 반드시 `EffectAction`으로 분류한다.

### 3.4 첫 콘텐츠 데이터 제안

실제 Google Sheet ID 충돌 여부를 다시 확인한 뒤 사용한다.

```text
SkillTable
Id: 202 (제안)
Name: ChainLightning
Tags: Spell, Lightning
SkillType: Range
SkillTargetType: Position
SkillRangeMax: 6
DamageType: Lightning
ProjectileId: 0
AreaEffectId: 0
ExecutionType: Single
SkillEffectIds: [1020]

SkillEffectTable
Id: 1020 (제안)
Name: ChainLightningPropagation
EffectType: SkillPropagation
Kind: EffectAction
Trigger: OnSkillCommit
Param1: 1

SkillPropagationTable
Id: 1
Name: ChainLightning_4Hits
DeliveryType: Instant
ChildCount: 1
MaxDepth: 3
InitialSearchRadius: 2.5
SearchRadius: 4.0
HopInterval: 0.2
DamageRatioPerGeneration: 100
MaxTotalDeliveries: 4
AllowRepeatTarget: false
SegmentEffectKey: Effects/ChainLightningSegment
MaxTurnAngleDeg: 60
```

> `HopInterval`의 첫 제안값은 0.08이었다. 이 값에서는 4연쇄가 0.32초 안에 끝나고 번개 연출 본체(약 0.2~0.4초)가 3~4개 겹쳐, 연쇄보다 동시 타격처럼 보였다. 2026-09-23 연출 본체가 보이는 시간에 맞춰 0.2로 변경했고 시트와 `.bytes`에 반영했다(4연쇄 약 0.8초).

## 4. ECS 데이터 모델

### 4.1 전파 루트

한 번의 원본 시전 또는 source hit가 전파 루트 엔티티 하나를 만든다.

```csharp
public struct SkillPropagationComponent
{
    public int OwnerEntityId;
    public int SkillEntityId;
    public int PropagationTableId;
    public int OutstandingDeliveryCount;
    public int SpawnedDeliveryCount;
    public SkillPropagationTargetIds VisitedTargets;
}
```

`SkillPropagationTargetIds`는 `SkillEffectIds`와 같은 인라인 값 구조체이며 초기 Capacity는 16이다. 방문과 예약을 같은 목록에 기록해 서로 다른 분기끼리 같은 대상을 고르지 않게 한다.

### 4.2 자식 분기

실제 Projectile 또는 Instant 전달 엔티티에 붙는다.

```csharp
public struct SkillPropagationBranchComponent
{
    public int RootEntityId;
    public int RemainingDepth;
    public int Generation;
    public Vector2 TravelDirection;
    public int AimedTargetEntityId;
}
```

피해 배율은 `Generation`과 `SkillPropagationTable.DamageRatioPerGeneration`으로 계산한다. 중복 저장하지 않는다.

`Generation`과 `RemainingDepth`의 정본은 이 컴포넌트다. 같은 엔티티의 다른 컴포넌트에 복사하지 않는다.

`AimedTargetEntityId`는 Projectile 전달이 겨냥한 대상이다. 선택된 대상은 선택 시점에 `VisitedTargets`에 예약되므로, 충돌 검사에서 "방문한 대상은 통과하되 겨냥 대상은 맞힌다"를 구분하는 데 쓴다(§6.4). Instant 전달은 `InstantSkillDeliveryComponent.TargetEntityId`를 쓰므로 0이다.

### 4.3 전파 요청

source 커밋이나 적중이 발생하면 즉시 자식을 생성하지 않고 요청 엔티티를 만든다. 요청 엔티티에는 `TransformComponent`를 함께 붙이며, 요청의 source 위치는 이 Transform이 정본이다(§1.6).

```csharp
public struct SkillPropagationRequestComponent
{
    public int RootEntityId;
    public int SourceTargetEntityId;
    public bool IsFollowingSourceTarget;
    public int RemainingDepth;
    public int Generation;
    public float RemainingDelay;
    public float SearchRadius;
    public SkillDeliveryType DeliveryType;
    public Vector2 IncomingDirection;
}
```

- `IsFollowingSourceTarget`은 `SourceTargetEntityId > 0`일 때 true로 시작한다. 대상이 제거(예약)되면 false가 되고 다시 true가 되지 않는다.
- source 위치를 별도 `SourcePosition` 필드로 두지 않는다. 대기 중 위치가 바뀌므로 필드와 Transform이 어긋날 수 있기 때문이다.

### 4.4 즉발 전달

```csharp
public struct InstantSkillDeliveryComponent
{
    public int RootEntityId;
    public int TargetEntityId;
    public Vector2 SourcePosition;
}
```

- 대상 위치는 저장하지 않는다. 처리 시점에 `TryGetEligibleTargetPosition`으로 다시 조회한다. 생성 시점 스냅샷은 대상이 움직였을 수 있다.
- `Generation`/`RemainingDepth`는 같은 엔티티의 `SkillPropagationBranchComponent`에서 읽는다.
- 전달 엔티티를 따로 만드는 이유는 세 가지다. Projectile과 같은 branch 파이프라인을 공유하고, `OutstandingDeliveryCount`의 작업 단위를 엔티티로 추적해 외부 파괴 시에도 카운트가 새지 않게 하며, 대상 선택(210)과 적중 처리(220)를 분리한다. 특히 마지막 분리가 없으면 `ProcessRequest`의 루트 지역 복사본이 적중 처리 중 `TryQueueRequest`가 쓴 루트 변경을 덮어쓴다.

### 4.5 풀 초기 용량

`ComponentManager`에 다음 초기 용량을 추가한다. 이는 상한이 아니라 재할당 감소용 초기값이다.

```text
SkillPropagationComponent          32
SkillPropagationBranchComponent  256
SkillPropagationRequestComponent 128
InstantSkillDeliveryComponent    128
```

## 5. 런타임 흐름

### 5.1 OnSkillCommit 시작

`System_Skill.ProcessSkillHit`에서 조준 커밋 직후, Projectile/Area/직접 타격 분기 전에 `OnSkillCommit`을 발화한다.

`SkillEffectContext`에 다음 필드를 추가한다.

```csharp
public int PropagationRootEntityId;
public bool SuppressDefaultImpact;
```

`SkillPropagation` EffectAction은 다음을 수행한다.

1. `SkillPropagationTable` 검증
2. 전파 루트 생성
3. 커밋 위치를 source로 최초 요청 생성
4. `SuppressDefaultImpact=true`
5. `System_Skill`은 기본 Position 원형 타격을 생략

이 훅은 `OnSkillCommand`보다 늦고 실제 `DamageTime` 커밋과 일치해야 한다. 시전 명령 시점에 피해나 전파 대상을 고정하지 않는다.

커밋 순간 `InitialSearchRadius` 안에 대상이 없으면 루트를 만들지 않고 허공 연출(`SpawnEmptyCommitSegment`)만 재생한다. 나중에 범위로 걸어 들어온 대상을 지연 루트가 잡지 않게 하려는 의도적인 검사이며, 요청 처리 때 대상 탐색을 한 번 더 하는 것은 중복이 아니다.

커밋 순간에는 대상이 있었지만 `HopInterval` 동안 대상이 사라져 최초 요청(`Generation == 0`, `SourceTargetEntityId == 0`)이 아무도 찾지 못하면, 요청 처리에서도 같은 허공 연출을 재생한다. 기본 타격이 이미 억제된 상태라 연출이 없으면 시전이 무시된 것처럼 보이기 때문이다. 체인 도중(2타 이후) 다음 대상이 없어 끝나는 경우에는 연출을 추가하지 않는다.

### 5.2 OnHit 시작

원본 직접 타격 또는 원본 Projectile이 적중하면 `OnHit`의 `SkillPropagation` EffectAction이 루트와 요청을 생성한다.

- source 위치는 실제 피격 대상의 Collider 보정 중심이다.
- 원래 맞은 대상은 루트 방문 목록에 즉시 기록한다.
- 자식 전달이 이미 `SkillPropagationBranchComponent`를 가지고 있으면 새 루트를 만들지 않는다.
- 자식 적중의 후속 전파는 기존 Root와 `RemainingDepth`를 사용한다.

### 5.3 Outstanding 카운트

루트의 `OutstandingDeliveryCount`는 요청과 실제 전달 엔티티를 합친 미완료 작업 수다.

- 루트 시작과 최초 요청 생성: `+1`
- 요청 소비: `-1`
- 선택된 자식 전달 N개 생성: `+N`
- 전달 완료·실패·Projectile 만료: `-1`
- 전달 완료와 함께 후속 요청 생성: 먼저 요청 `+1`, 그 뒤 현재 전달 `-1`
- 카운트가 0이 되면 루트에 `DestroyTag` 부착

외부 파괴나 씬 정리에서도 카운트가 누수되지 않도록 Projectile/Instant 정상 종료 경로와 `System_EntityDestroy`의 예외 종료 경로를 함께 갱신한다.

### 5.4 대기 중 요청과 타이밍

한 번 튈 때의 흐름은 다음과 같다.

```text
A 적중 → 요청 생성(Transform = A 위치) → HopInterval 대기(매 FixedUpdate A를 따라 이동)
      → 대기 종료 시 요청 Transform 위치에서 다음 대상 B 탐색(210)
      → 전달 생성 → 같은 FixedUpdate에 B 적중(220) → B 기준 새 요청 …
```

- 시간차는 요청 대기(`HopInterval`)에서만 생긴다. 대상 선택부터 적중까지는 같은 FixedUpdate 안에서 끝난다.
- 다음 대상은 대기가 끝난 시점에 고른다. 대기 중 적이 움직이거나 죽으면 선택이 달라진다.
- Segment 연출의 시작점과 Projectile 발사 위치도 대기가 끝난 시점의 요청 위치다.

## 6. 시스템과 책임

### 6.1 `SkillHitProcessor`

현재 `System_Skill`과 `System_Projectile`에 중복된 적중 처리를 하나의 stateless 처리기로 추출한다.

입력 DTO:

```csharp
public struct SkillHitRequest
{
    // 필수 값: readonly, 생성자로만 설정
    public readonly int SkillEntityId;
    public readonly int OwnerEntityId;
    public readonly int TargetEntityId;
    public readonly float DamageMultiplier;

    // 선택 값: 객체 초기화자로 설정
    public int DeliveryEntityId;
    public int PropagationRootEntityId;
    public int Generation;
    public bool IsProjectileDelivery;
    public Vector2 ImpactDirection;

    public SkillHitRequest(int inSkillEntityId, int inOwnerEntityId, int inTargetEntityId, float inDamageMultiplier);
}
```

`DamageMultiplier`가 struct 기본값 0으로 누락되면 모든 피해가 최소 데미지 1로 조용히 붕괴한다. 그래서 필수 값은 생성자로만 받는다. 프로젝트가 C# 9라 기본 생성자 자체는 막을 수 없지만, 초기화자에서 필수 필드를 설정하면 컴파일 에러가 난다.

```csharp
new SkillHitRequest(skillEntityId, ownerId, targetId, 1f) { IsProjectileDelivery = true }
```

책임:

- 피해 계산 및 적용
- HitEffect
- OnHit/OnCrit/OnKill 컨텍스트 구성과 발화
- 자식 전달임을 `PropagationRootEntityId`로 표시

`DamageCalculator.Calculate`에는 추가 피해 배율 overload 또는 명시적 인자를 추가한다. 배율은 방어 계산 전에 적용한다.

### 6.2 `System_SkillPropagation`

- FixedUpdate priority 210
- 대기 중 요청의 Transform을 source 대상 위치로 갱신하고, 대상이 제거되면 추적 종료(§1.6)
- 요청 지연시간 감소
- 루트·테이블 존재 확인. 필드 범위는 파서가 보장하므로 재검사하지 않는다.
- 요청 Transform 위치에서 후보 탐색과 방문/예약 처리
- `ChildCount`만큼 Delivery 생성
- 후보가 없거나 상한 도달 시 요청 종료. 최초 커밋 요청이 아무도 찾지 못했으면 허공 연출(§5.1)

후보 선택은 `SkillPropagationTargetSelector`에 둔다. 이 클래스는 조회와 선택만 담당하고 실행 상태를 소유하지 않는다. 후보 스캔 루프에서는 시전자 진영을 루프 밖에서 한 번 조회하고, 후보 Transform은 순회 중인 풀에서 직접 꺼내 넘긴다. 후보마다 재조회하지 않는다.

### 6.3 `System_InstantSkillDelivery`

- FixedUpdate priority 220
- 선택된 대상이 여전히 유효한지 재검증
- `SkillHitProcessor`로 즉발 피해 적용
- Segment VFX 요청
- `RemainingDepth > 0`이면 후속 전파 요청 생성
- 현재 전달 완료 처리

### 6.4 `System_Projectile`

기존 Projectile이 `SkillPropagationBranchComponent`를 가질 수 있도록 확장한다.

적중 순서:

1. `SkillHitProcessor` 호출
2. 후속 전파 요청 생성 여부 결정
3. 현재 전파 자식 완료 처리
4. 전파 자식 Projectile은 적중 후 제거
5. 전파가 없는 기존 Projectile만 기존 `IsPiercing` 정책 적용

첫 구현은 적중한 Projectile의 방향을 꺾어 재사용하지 않는다. source 적중 지점에서 새 자식 Projectile을 생성한다. 이 방식이 Chain과 Fork를 동일하게 처리한다.

자식 Projectile은 직전 적중 대상 위치에서 생성되므로 그 대상의 Collider와 즉시 겹친다. 그래서 전파 Projectile의 충돌 검사는 **루트 `VisitedTargets`에 있으면서 `AimedTargetEntityId`가 아닌 대상을 통과**한다. 이 규칙이 없으면 직전 대상을 다시 때리고, 그 대상에서 전파가 다시 시작된다. 경로상의 새 대상은 정상적으로 맞히고 실제 적중 대상으로 `VisitedTargets`에 기록한다.

같은 FixedUpdate 안에서 이미 `DestroyTag`가 붙은 Projectile은 처리하지 않는다. 프레임이 `fixedDeltaTime`보다 길면 LateUpdate 제거 전에 FixedUpdate가 두 번 돌 수 있기 때문이다.

### 6.5 실행 우선순위

```text
150 System_Projectile
190 System_SkillAim
200 System_Skill
210 System_SkillPropagation
220 System_InstantSkillDelivery
240 System_AreaEffect
250 System_HpCheck
```

`SystemManager.Initialize()`에 우선순위 주석과 함께 등록한다.

## 7. Delivery 생성

신규 `SkillDeliveryFactory`는 선택된 대상에 맞는 전달 엔티티만 생성한다.

### 7.1 Instant

- `InstantSkillDeliveryComponent` 생성
- `SkillPropagationBranchComponent` 생성
- `TransformComponent`는 source 위치를 사용(§1.6)
- 대상 ID만 저장하고 대상 위치는 처리 시점에 재조회
- source 위치는 요청 Transform 위치를 인자로 받는다. 최초 커밋 요청만 시전자 위치에서 시작한다.

### 7.2 Projectile

- 기존 `ProjectileHelper.SpawnProjectile` 사용
- 요청 Transform 위치에서 target Collider 중심 방향으로 발사
- 반환된 Projectile 엔티티에 `SkillPropagationBranchComponent` 추가. 선택된 대상을 `AimedTargetEntityId`에 기록
- 기존 Projectile Prefab과 Addressable 로딩 경로 재사용
- 투사체가 선택된 대상이 아닌 다른 적에게 먼저 충돌하면 실제 충돌 대상을 source hit로 인정

### 7.3 Inherit

- `SkillTable.ProjectileId > 0`이면 Projectile
- 그 외에는 Instant
- `AreaEffectId`는 첫 버전에서 전달 상속 판정에 사용하지 않는다.

## 8. 연출

현재 `EffectManager`는 위치·회전 기반 Spawn만 지원한다. Instant 전파 구간을 위해 두 점을 받는 전용 View를 추가한다.

신규 항목:

- `SkillPropagationSegmentView`
- `Effects/ChainLightningSegment` Addressable Prefab
- `EffectManager.SpawnSegment(key, start, end)` 또는 동등한 명시적 API

연출 계약:

- 시작점과 끝점은 게임플레이 적중 시점의 snapshot을 사용한다.
- 비동기 로딩 중 대상이 움직이거나 사라져도 피해 판정을 다시 계산하지 않는다.
- VFX 로드 실패는 피해와 전파 진행을 막지 않는다.
- Projectile 전달은 기존 Projectile View를 사용하고 Segment View를 생성하지 않는다.
- `SpawnSegment`는 호출마다 풀에서 별도 인스턴스를 꺼낸다. `HopInterval`이 연출 길이보다 짧으면 여러 구간이 동시에 겹쳐 재생된다.
- 임시 대체 연출(fallback)은 두지 않는다. 키가 Addressables에 등록되지 않았으면 키당 경고를 1회 찍고 연출 없이 진행한다. 이 사전 검사는 로드 실패 `LogError`가 hop마다 반복되지 않게 하는 역할도 한다.
- [추론] 현재 `ChainLightningSegment` 프리팹은 루트 파티클 설정 때문에 `EffectInstance` 자동 수명이 약 4.8초로 계산된다. 보이는 시간은 약 0.4초라 풀 점유가 길다. 필요하면 `_overrideDuration`으로 줄인다.

## 9. 구현 단계

### Phase P0 — 문서와 기준선

- 이 문서와 현재 코드를 다시 대조
- Google Sheet의 Skill/SkillEffect ID 충돌 확인
- `SKILL_DESIGN.md`의 `OnProjectileSpawn` 상태를 실제 코드에 맞게 정정
- 기존 Strike, Lacerate, MultiShot, PoisonCloud의 정적 기준선 확인

### Phase P1 — 적중 처리 단일화

- `SkillHitProcessor` 추가
- `System_Skill` 직접 타격을 새 처리기로 전환
- `System_Projectile` 적중을 새 처리기로 전환
- 기존 피해·트리거·HitEffect 동작이 변하지 않았는지 검증

### Phase P2 — 데이터와 컴포넌트

- enum, `SkillPropagationTable`, DataManager, DownloadTables 추가
- 전파 루트·분기·요청·Instant 컴포넌트 추가
- 인라인 방문 목록과 풀 크기 추가
- 파서 범위·상한·참조 검증 추가

### Phase P3 — 공용 전파 런타임

- `OnSkillCommit` 훅과 기본 타격 억제 구현
- `SkillPropagationTargetSelector` 구현
- `System_SkillPropagation`과 `System_InstantSkillDelivery` 등록
- 루트 Outstanding 카운트와 정상/예외 정리 구현

### Phase P4 — Chain Lightning 수직 슬라이스

- 제안 Skill/SkillEffect/Propagation 행 추가
- Position 커밋 → 첫 적 탐색 → 최대 4명 연쇄 구현
- Segment VFX와 Addressable 등록
- 기본 Position 광역 타격이 중복되지 않는지 확인

### Phase P5 — Projectile 연쇄와 분열

- Projectile 자식 전달 생성
- 원본 OnHit에서 전파 루트 시작
- 자식 적중이 새 루트를 중복 생성하지 않도록 컨텍스트 연결
- Projectile 만료·외부 파괴 시 Outstanding 정리
- 기존 관통과 전파 정책의 우선순위 검증

### Phase P6 — Instant 주문 분열

- `ChildCount > 1`, `MaxDepth=1` 데이터 추가
- 한 세대에서 서로 다른 대상 예약
- 분기끼리 같은 대상에 합류하지 않는지 검증
- 재귀 분열은 안전 상한 검증 후에만 활성화

### Phase P7 — 통합 검증과 문서 갱신

- Unity 컴파일
- EditMode 순수 선택 테스트
- PlayMode 대표 시나리오
- Profiler 할당과 최악 전달 수 확인
- `SKILL_DESIGN.md`, `Docs/README.md`, 필요 시 관련 Wiki 갱신 제안

## 10. 검증 계획

### 10.1 EditMode

- 가장 가까운 적 선택
- 동거리 Entity ID tie-break
- 적대 진영만 선택
- HP 0, DestroyTag, 점프 무적 제외
- 방문 대상 제외
- 직전 진행 방향의 허용 각도 밖 후보 제외
- `MaxTurnAngleDeg=180` 기존 무제한 동작 보존
- 같은 세대 자식 중복 예약 방지
- `ChildCount=1` Chain 계산
- `ChildCount>1` Fork 계산
- 깊이와 총 전달 상한 계산
- 방문 목록 Capacity 초과 실패 안전성
- 세대별 피해 배율 계산

### 10.2 PlayMode

- 지정 위치에 대상 없음: 피해 없이 정상 종료
- 대상 1명: 한 번만 적중
- Chain Lightning: 최대 4명, 재적중 없음
- Chain Lightning: 직전 진행 방향의 좌우 60도 밖에 있는 적에게 역방향 연쇄되지 않음
- 중간 대상 사망·삭제: 다음 후보 또는 분기 종료
- 즉발 주문 3갈래 분열
- 투사체 2갈래 분열
- 투사체 3회 연쇄
- 자식별 OnHit/OnCrit/OnKill 정확히 1회
- 원본 Position 타격과 전파 피해 중복 없음
- 자식은 마나·쿨타임을 추가 소비하지 않음
- Projectile 만료·씬 Reset·던전 정리에서 루트 누수 없음
- VFX 로드 실패에도 동일한 게임플레이 결과
- 대기 중 직전 대상이 이동하면 다음 hop이 대상의 현재 위치에서 시작
- 대기 중 직전 대상이 사망하면 사망 위치에서 다음 대상으로 연쇄
- 커밋 후 `HopInterval` 동안 최초 대상이 범위를 벗어나면 허공 연출 재생
- 전파 Projectile이 생성 위치의 직전 대상과 경로상 방문 대상을 통과하고 겨냥 대상만 적중

### 10.3 성능

- FixedUpdate 핫루프에서 `foreach`와 per-hop `List`/LINQ 할당 금지
- 후보 검색은 `TransformComponent`/`FactionComponent` 풀을 `for`로 순회
- 거리 비교는 제곱 거리 사용
- 최대 16 전달 기준 GC Alloc 0 확인
- 실제 대규모 전투에서 필요할 때만 공간 파티션 도입 검토

## 11. 수정·신규 파일 예상

### 신규

```text
Assets/Scripts/Common/Component/SkillPropagationComponent.cs
Assets/Scripts/Common/Component/SkillPropagationBranchComponent.cs
Assets/Scripts/Common/Component/SkillPropagationRequestComponent.cs
Assets/Scripts/Common/Component/InstantSkillDeliveryComponent.cs
Assets/Scripts/Common/Component/SkillPropagationTargetIds.cs
Assets/Scripts/Common/System/System_SkillPropagation.cs
Assets/Scripts/Common/System/System_InstantSkillDelivery.cs
Assets/Scripts/Skill/Combat/SkillHitProcessor.cs
Assets/Scripts/Skill/Propagation/SkillPropagationTargetSelector.cs
Assets/Scripts/Skill/Propagation/SkillDeliveryFactory.cs
Assets/Scripts/Common/Visual/SkillPropagationSegmentView.cs
Assets/_BinaryData/TableData/SkillPropagationTable.bytes
Assets/Prefabs/Game/Effect/Skill/ChainLightningSegment.prefab
```

### 수정

```text
Assets/Scripts/Common/GlobalEnum.cs
Assets/Scripts/Common/Tables.cs
Assets/Scripts/Data/DataManager_Table.cs
Assets/Scripts/Editor/DownloadTables.cs
Assets/Scripts/Manager/ComponentManager.cs
Assets/Scripts/Manager/SystemManager.cs
Assets/Scripts/Manager/EffectManager.cs
Assets/Scripts/Common/System/System_Skill.cs
Assets/Scripts/Common/System/System_Projectile.cs
Assets/Scripts/Common/System/System_EntityDestroy.cs
Assets/Scripts/Common/Utility/ProjectileHelper.cs
Assets/Scripts/Skill/Combat/SkillEffectContext.cs
Assets/Scripts/Skill/Combat/SkillEffectExecutor.cs
Assets/Scripts/Skill/Combat/DamageCalculator.cs
Docs/combat/SKILL_DESIGN.md
Docs/README.md
```

파일은 책임이 실제로 분리될 때만 신규 생성한다. 이미 적절한 책임을 가진 파일이 있으면 해당 위치를 확장하고, `*Util`, `*Helper`, 범용 `*Manager`는 새로 만들지 않는다.

## 12. 완료 정의

- Chain Lightning이 지정 위치에서 첫 적을 찾고 최대 설정 횟수만큼 서로 다른 적에게 연쇄된다.
- 동일 전파 기반으로 Projectile Chain, Projectile Fork, Instant Spell Fork가 동작한다.
- 기존 직접 타격과 Projectile의 피해·트리거 결과가 회귀하지 않는다.
- 전파 자식이 마나·쿨타임을 중복 소비하지 않는다.
- 필드 범위와 `DeliveryType`별 최대 전달 수는 데이터 다운로드 파서가 보호하고, 트리거별 이론 전달 수와 방문 목록 Capacity는 런타임이 보호한다.
- 모든 전파 루트와 자식 엔티티가 완료·실패·만료·Reset에서 정리된다.
- 게임플레이는 VFX 로딩 성공 여부와 독립적이다.
- Unity 컴파일, EditMode 선택기 테스트, PlayMode 대표 시나리오, GC Alloc 검증이 완료된다.

## 13. 변경 이력

- 2026-09-22~23: 첫 구현의 코드 리뷰 반영.
  - **정합성:** Projectile `DestroyTag` 가드, 경로상 실제 적중 대상 방문 기록, `DeliveryType`별 `MaxTotalDeliveries` 상한(§1.5), 전파 Projectile의 방문 대상 통과와 `AimedTargetEntityId`(§4.2·§6.4)를 추가했다.
  - **주문의 위치 원칙(§1.6):** 요청 엔티티가 Transform을 갖고 직전 대상에 붙어 따라가도록 했다. `SkillPropagationRequestComponent.SourcePosition`은 제거했다.
  - **데이터 정리:** `InstantSkillDeliveryComponent`에서 중복·미사용 필드(`TargetPosition`/`Generation`/`RemainingDepth`)를 제거했다. `SkillHitRequest`의 필수 값은 생성자로 받는다(§6.1).
  - **검증 위치:** 필드 범위 검증을 파서로 일원화하고 런타임 `IsRuntimeValid`를 제거했다. 활성 전파 프로필 중복 검사는 피격마다 하지 않고 스킬 생성 시 1회로 옮겼다.
  - **연출:** 최초 요청이 대상을 잃었을 때 허공 연출을 추가했다(§5.1). 임시 fallback 연출과 참조 없는 머티리얼은 제거했다(§8).
  - **데이터:** `ChainLightning_4Hits`의 `HopInterval`을 0.08에서 0.2로 변경했다(§3.4).
