# PoE 2식 스킬 커서 추적 전환 계획

## 1. 문서 목적

현재 플레이어 스킬은 입력 명령이 생성되고 `System_Skill`이 스킬을 시작할 때 마우스 좌표를 `SkillTargetComponent`에 저장한다. 이후 선딜, 타격, 투사체 생성, 채널링 틱은 저장된 값을 사용하므로 시전 중 마우스를 옮겨도 조준이 갱신되지 않는다.

이 계획의 목적은 키보드·마우스 플레이에서 다음과 같은 PoE 2식 조작감을 구현하는 것이다.

- 스킬 입력 후 선딜 중에도 최신 커서 방향 또는 위치를 추적한다.
- 실제 판정 방향과 캐릭터 주변의 방향 화살표 View(이미지 이펙트)가 같은 조준을 표현한다.
- 이동하면서 스킬을 사용할 때 스킬 동작 구간에 이동 속도 페널티를 적용한다.
- 실제 타격·투사체 생성·장판 생성 직전에 조준을 확정한다.
- 이미 생성된 공격 결과는 이후 커서 이동에 영향을 받지 않는다.
- 스킬 유형별로 추적 종료 시점을 데이터에서 예외 지정할 수 있다.

캐릭터 자체를 회전시키거나 방향별 공격 애니메이션을 추가하지 않는다. 캐릭터 주변을 도는 화살표 View의 회전을 이 프로젝트에서의 방향 애니메이션으로 정의한다.

PoE 2의 Cursor Tracking은 입력을 놓은 뒤에도 스킬의 목표 위치와 방향을 갱신하는 기능이며, WASD에서는 기본 활성화되고 클릭 이동에서는 기본 비활성화된다. 이 프로젝트는 우선 현재 플레이어 입력 방식에 맞춰 기능을 구현하고, 사용자 옵션화는 후속 범위로 둔다.

## 2. 근거와 현행 구조

### 2.1 외부 동작 기준

- PoE 2 0.4.0 패치의 Cursor Tracking 설명: <https://www.pathofexile.com/forum/view-thread/3883495>
- PoE 2 0.3.0의 Cursor/Ground Aiming Mode 설명: <https://www.pathofexile.com/forum/view-thread/3826682>
- PoE 2 0.3.0의 스킬 사용 중 이동 속도 페널티와 완화 설명: <https://www.pathofexile.com/forum/view-thread/3826682>
- PoE 2의 스킬 사용 중 이동 속도 메커니즘 정리: <https://www.poe2wiki.net/wiki/Movement_speed>
- 스킬 동작 중 재조준 의도를 설명한 인터뷰: <https://www.mmorpg.com/interviews/interview-path-of-exile-2s-game-director-jonathan-rogers-chats-gameplay-intentions-inclusions-and-improvements-2000130901>

PoE 2의 모든 스킬 내부 규칙을 그대로 복제하는 것이 아니라, 공개된 조작 원칙을 이 프로젝트의 ECS와 스킬 데이터 구조에 맞게 적용한다.

### 2.2 프로젝트 내부 기준

- 입력은 `System_Input`이 매 Update에 마우스 화면 좌표를 읽고, 스킬 입력 순간 월드 좌표로 변환하여 `SkillCommand`를 생성한다. [src: Assets/Scripts/Common/System/System_Input.cs]
- `System_Skill`은 명령을 처리할 때 `SkillTargetComponent`의 대상 ID, 위치, 방향을 확정한다. [src: Assets/Scripts/Common/System/System_Skill.cs]
- 스킬 시작 시 소유자의 `StateComponent.Condition`은 `UseSkill`로 바뀌지만 `System_Move`는 이 상태를 읽지 않는다. [src: Assets/Scripts/Common/System/System_Skill.cs] [src: Assets/Scripts/Common/System/System_Move.cs]
- 일반 이동은 스킬 실행 여부와 관계없이 `FinalMoveSpeed × FinalMoveSpeedMul`을 그대로 사용하므로 현재는 스킬 중 감속이 없다. 점프 중에만 일반 이동을 완전히 중지한다. [src: Assets/Scripts/Common/System/System_Move.cs]
- 다단 히트와 채널링은 동일한 `SkillTargetComponent`를 재사용한다. [src: Assets/Scripts/Common/System/System_Skill.cs]
- 투사체는 생성 시점의 현재 시전자 위치에서 저장된 목표 위치를 향해 발사된다. 생성된 이후에는 투사체 자체 이동 규칙을 따른다. [src: Assets/Scripts/Common/System/System_Skill.cs]
- 현재 캐릭터 애니메이션은 카테고리와 프레임을 재생하며 공격 방향을 표현하는 구조가 아니다. [src: Assets/Scripts/Common/System/System_Animation.cs]
- 현재 플레이어 엔티티에는 공격 방향만 전담해 표시하는 View가 없다. 화살표는 범용 `EffectManager`의 단발·루프 이펙트가 아니라 플레이어 수명에 귀속된 전용 View로 추가한다. [src: Assets/Scripts/Base/EntityBase.cs] [src: Assets/Scripts/Factory/EntityFactory.cs]
- 점프 스킬은 Start 진입 즉시 `JumpComponent.EndPosition`을 계산하므로 현재 구조에서는 선딜 중 종착점 추적이 불가능하다. [src: Assets/Scripts/Common/System/System_Skill.cs]
- 스킬은 Start → Process → End 단계와 `DamageTime` 비율을 사용한다. [src: Docs/combat/SKILL_DESIGN.md]

## 3. 목표 동작

### 3.1 기본 수명 주기

```text
입력 시작
  → 초기 조준 저장 + 스킬 이동 정책 활성화
  → 방향 화살표 표시 + 선딜 동안 회전 속도로 마우스 방향 추적 + 이동 감속
  → 효과 직전 화살표가 표현하는 현재 조준 방향을 원자적으로 확정
  → 타격 판정 / 투사체 생성 / 장판 생성
  → 후딜 동안 화살표가 확정 방향 유지 + 이동 속도 정상화
  → 스킬 종료 시 화살표 숨김 + 조준/이동 런타임 상태 초기화
```

“효과 직전”은 스킬마다 다음과 같이 정의한다.

| 스킬 결과 | 조준 확정 시점 | 확정 후 커서 이동 |
| --- | --- | --- |
| 즉시 근접·범위 타격 | 첫 타격 쿼리 직전 | 현재 공격에 미반영 |
| 투사체 | 투사체 생성 직전 | 생성된 투사체에 미반영 |
| 위치 장판 | AreaEffect 생성 직전 | 생성된 장판에 미반영 |
| 기본 다단 히트 | 첫 타격 직전 | 남은 타격에도 미반영 |
| 틱별 재조준 다단 히트 | 각 타격 직전 | 다음 타격에는 반영 |
| 채널링 | 각 틱 직전 | 다음 틱에는 반영 |
| 차지 | 입력 해제 후 효과 실행 직전 | 실행 결과에 미반영 |
| 단일 대상 고정 | 명령 처리 시 대상 ID 고정 | 커서 이동으로 대상 변경 안 함 |

### 3.2 방향 화살표 View 회전

- 마우스가 가리키는 방향을 `DesiredAimDirection`, 화살표가 현재 표현하고 실제 판정이 사용할 방향을 `TargetDirection`으로 구분한다.
- `TargetDirection`은 매 Update에 `DesiredAimDirection`을 향해 스킬의 최대 회전 속도만큼만 회전한다.
- 화살표 View는 `TargetDirection`을 그대로 표시하며 별도의 시각 보간을 추가하지 않는다.
- 커서가 캐릭터 중심과 거의 같은 위치일 때는 0 벡터를 만들지 않고 마지막 유효 방향을 유지한다.
- 커서를 빠르게 반대편으로 옮겨도 화살표는 순간이동하지 않고 최단 각도 방향으로 회전한다.
- 타격 판정은 효과 발생 직전 `SkillTargetComponent.TargetDirection`을 확정해 사용한다. 따라서 타격 순간의 화살표 방향과 판정 방향이 일치한다.
- 화살표 GameObject의 Transform을 역으로 읽어 판정하지 않는다. ECS의 `TargetDirection`이 정본이며 화살표와 전투 로직이 함께 소비한다.
- 화살표는 스킬 Start에서 표시하고, 추적 중에는 회전 속도로 마우스를 따라가며, 확정 후에는 End 후딜이 끝날 때까지 확정 방향을 유지한다. `SkillState.None` 완료, 취소, 사망 시 숨긴다.
- Channeling과 `TrackPerCommit`은 각 확정 사이에도 `TargetDirection`이 계속 마우스 방향을 추적한다.
- 캐릭터 루트, 캐릭터 스프라이트, Collider2D, 그림자는 회전하지 않는다.
- 기존 `System_Animation`의 캐릭터 프레임 재생은 변경하지 않는다. 화살표 View의 Transform 회전 자체를 방향 애니메이션으로 취급한다.
- 회전 제한은 표시 View가 아니라 논리 조준에 적용한다. 화살표에만 추가 보간을 넣어 판정보다 늦게 보이게 만들지 않는다.

## 4. 제안 설계

### 4.1 데이터 기반 조준 정책

`SkillTable`에 `AimTrackingMode`를 추가해 스킬별 동작을 선택한다. 하나의 스킬은 하나의 테이블 행으로 표현한다는 기존 원칙을 유지한다.

| 값 | 의미 | 대표 용도 |
| --- | --- | --- |
| `Default` | 실행·타깃 타입별 프로젝트 기본값 사용 | 대부분의 플레이어 스킬 |
| `SnapshotOnCommand` | 입력 명령 처리 시 즉시 고정 | AI 스킬, 예외 스킬 |
| `TrackUntilCommit` | 효과 확정 직전까지 추적 | 일반 방향/위치 스킬 |
| `TrackPerCommit` | 각 다단 히트 직전까지 다시 추적 | 방향 전환이 허용된 연속 공격 |
| `TrackContinuously` | 채널 종료까지 회전을 계속하고 각 틱에 현재 유효 조준 사용 | 지속 채널링 |

`SkillTable`에 `AimTurnSpeedDegPerSec`를 추가한다.

- 단위는 초당 회전 각도다.
- 값이 없거나 0이면 프로젝트 기본값을 사용한다.
- 첫 밸런스 후보는 초당 720도다. 목표 방향이 정확히 반대편이면 약 0.25초가 걸린다.
- 실제 값은 스킬의 선딜과 무게감에 맞춰 테이블에서 조정한다.
- 지나치게 큰 값으로 사실상 즉시 조준이 필요한 예외도 데이터에서 표현할 수 있다.
- 회전 속도는 공격속도·시전속도와 별개다. 공격속도는 확정까지 주어진 시간을 바꾸고, 회전 속도는 그 시간 동안 방향이 얼마나 따라가는지를 결정한다.

`Default`의 1차 규칙은 다음과 같다.

- 플레이어의 Direction/Position 단발 스킬: `TrackUntilCommit`
- 플레이어의 MultiHit: 첫 타격까지 `TrackUntilCommit`
- 플레이어의 Channeling: `TrackContinuously`
- AI 또는 입력 컴포넌트가 없는 시전자: `SnapshotOnCommand`
- 자기 중심 360도 스킬: 조준 추적 없음
- 점프·이동 스킬: 1차에서는 `SnapshotOnCommand`, 이동 스킬 단계 완료 후 전환

#### 테이블 열 추가와 호환성

기존 SkillTable 열 인덱스를 밀지 않도록 현재 마지막 `BaseProjectileCount` 뒤에 다음 네 열을 순서대로 추가한다.

| 인덱스 | 열 이름 | 빈 셀 기본값 |
| --- | --- | --- |
| 39 | `AimTrackingMode` | `Default` |
| 40 | `AimTurnSpeedDegPerSec` | `0` — 런타임 기본 720도 사용 |
| 41 | `MovementMode` | `Default` |
| 42 | `MoveSpeedWhileUsingMul` | `0` — 런타임 기본 30% 사용 |

- 실제 파서는 `Assets/Scripts/Editor/DownloadTables.cs`의 `ParseSkillTable`에 추가한다.
- Google Sheet 다운로드 범위를 `A:AM`에서 `A:AQ`로 확장한다.
- enum은 빈 셀과 알 수 없는 값에서 `Default`로 안전하게 처리하되 알 수 없는 문자열은 오류를 기록한다.
- `AimTurnSpeedDegPerSec < 0`, `MoveSpeedWhileUsingMul < 0` 또는 `> 100`은 테이블 검증 오류로 처리한다.
- 기존 데이터의 빈 셀은 위 기본값으로 동작해 마이그레이션 중 스킬이 중단되지 않게 한다.
- 다운로드와 검증 후 `Assets/_BinaryData/TableData/SkillTable.bytes`를 재생성한다.

### 4.2 입력 좌표 단일화

`InputComponent`에 현재 프레임의 `MouseWorldPosition`을 보관한다.

- `System_Input`이 화면 좌표와 월드 좌표를 매 Update 한 번만 계산한다.
- 스킬 명령 생성과 FixedUpdate 조준 시스템이 동일한 월드 좌표를 사용한다.
- `Camera.main` 조회와 화면→월드 변환을 스킬 슬롯마다 반복하지 않는다.
- UI가 입력을 소유한 동안에는 스킬 조준도 마지막 유효 위치에서 정지시킨다.

### 4.3 FixedUpdate 조준 시스템

새로운 `System_SkillAim : IFixedUpdateSystem`을 추가한다.

- Priority 190, FixedUpdate 단계에 등록한다.
- Priority 100의 이동과 Priority 150의 투사체 이후, Priority 200의 `System_Skill` 직전에 실행한다.
- 실행 중인 플레이어 스킬만 검사한다.
- `AimTrackingMode`, 스킬 단계, 확정 상태를 확인하고 `SkillTargetComponent`를 갱신한다.
- 마우스 월드 위치에서 시전자 위치를 빼 `DesiredAimDirection`을 계산한다.
- 현재 `TargetDirection`의 각도를 `Mathf.MoveTowardsAngle`로 목표 각도까지 `AimTurnSpeedDegPerSec × fixedDeltaTime`만큼 이동시킨다.
- 갱신된 각도를 정규화된 `SkillTargetComponent.TargetDirection`으로 저장한다.
- 좌우 180도 경계에서는 항상 최단 회전 경로를 사용한다.
- Z 표시 각도는 방향 표시 시스템이 같은 `TargetDirection`으로부터 계산하며 캐릭터 `TransformComponent.Rotation`에는 쓰지 않는다.
- 가능한 경우 기존 컴포넌트 풀을 캐시하고 `for`로 순회하며, 프레임당 할당을 만들지 않는다.

최초 방향은 다음처럼 초기화한다.

- 성공적으로 스킬 Start에 진입할 때 입력 명령의 마우스 방향을 `TargetDirection`에 즉시 저장한다.
- 화살표는 이 최초 방향으로 표시되며, 같은 스킬이 실행 중인 동안 발생한 이후 마우스 이동부터 회전 속도를 적용한다.
- 스킬 사이의 이전 화살표 방향은 이어받지 않는다. 스킬 완료 시 `TargetDirection`을 Reset하고 다음 스킬이 새 입력 방향으로 초기화한다.

권장 Update 순서는 다음과 같다.

```text
Update
System_Input (0): 마우스 월드 위치 샘플
  → System_Animation (500)
  → System_SkillAimIndicator (510): 확정적인 ECS 방향 표시
  → System_Jump (800)
  → System_Render (1000)

FixedUpdate
System_SkillMovementPenalty (90)
  → System_Move (100)
  → System_Projectile (150)
  → System_SkillAim (190): fixedDeltaTime만큼 유효 조준 회전
  → System_Skill (200): 같은 FixedUpdate의 방향을 효과 직전 확정
```

한 렌더 프레임에 FixedUpdate가 여러 번 실행되면 각 FixedUpdate가 마지막으로 샘플된 마우스 위치를 같은 목표로 사용하되, `TargetDirection` 회전은 `fixedDeltaTime`만큼 계속 진행한다. 따라서 낮은 렌더 프레임률에서도 스킬 판정 전에 조준 시간이 정상적으로 누적된다.

### 4.4 방향 표시 View 시스템

새로운 `System_SkillAimIndicator : IUpdateSystem`을 추가한다.

- Priority 510, Update 단계에 등록하며 방향 화살표 표시 한 가지 책임만 가진다.
- 실행 중인 스킬의 OwnerId로 플레이어의 `SkillAimIndicatorComponent`를 찾는다.
- 스킬의 `SkillTargetComponent.TargetDirection`과 조준 확정 상태를 읽어 해당 플레이어 화살표의 로컬 Z 회전을 갱신한다.
- `System_SkillAimIndicator`는 회전 속도를 다시 계산하거나 보간하지 않고 `TargetDirection`을 즉시 표시한다.
- 캐릭터 스프라이트나 `TransformComponent.Rotation`은 수정하지 않는다.
- 방향 입력이 필요한 스킬에서만 표시하고 자기 중심 360도 스킬과 무조준 스킬에서는 숨긴다.
- `SnapshotOnCommand` 스킬도 시작 방향을 보여주되 실행 중에는 회전시키지 않는다.
- End 후딜 동안에는 확정 방향으로 계속 표시하고, `SkillState.None` 완료, 취소, 사망, 플레이어 제거, 씬 Reset에서 숨긴다.
- View가 누락되거나 비활성 상태여도 실제 스킬 판정은 정상 진행하는 fail-soft 구조로 둔다.

`SkillAimIndicatorComponent`에는 표시 여부와 마지막 적용 각도처럼 플레이어별 런타임 데이터만 저장한다. 표시·회전·숨김 판단은 `System_SkillAimIndicator`가 담당한다.

화살표는 범용 `EffectManager`에 등록하지 않는다. 플레이어 수명에 귀속되는 전용 `SkillAimIndicatorView : MonoBehaviour`를 추가한다.

- 공용 `Entity.prefab`에는 배치하지 않고, 플레이어 전용 `SkillAimIndicator.prefab`으로 분리한다.
- `EntityFactory.CreatePlayer`만 전용 프리팹을 Addressables로 플레이어 루트 아래에 인스턴스화한다.
- `EntityBase`가 `SkillAimIndicatorView` 참조와 Addressables 인스턴스 핸들을 등록받아 시스템에 제공한다.
- `SkillAimIndicatorView`는 `Show`, `Hide`, `SetAngle`, `ResetView`처럼 표시 동작만 제공하며 전투 판정을 알지 못한다.
- 플레이어가 이동하면 자식 Transform으로 자동 추종하고, 플레이어 제거 시 `Addressables.ReleaseInstance`로 View와 의존성을 해제한다.
- `System_SkillAimIndicator.OnReset`은 남아 있는 View를 숨기고 `SkillAimIndicatorComponent` 상태를 초기화한다.
- View 로드 실패는 표시만 생략하고 플레이어 생성·스킬 판정·피해 처리는 계속한다.

방향 표시 View의 시각 구조는 다음 규칙을 따른다.

- 루트 피벗은 캐릭터 중심이며 플레이어 루트 Transform을 추종한다.
- 화살표 이미지 자식은 로컬 축 한 방향으로 반지름만큼 떨어뜨려 배치한다.
- 루트의 로컬 Z 회전으로 화살표가 캐릭터 주위를 돌며 바깥쪽을 가리키게 한다.
- 이미지가 바라보는 기준 축 차이는 프리팹 또는 읽기 전용 각도 오프셋 한 곳에서만 보정한다.
- SpriteRenderer를 사용하고 Collider2D와 `EffectInstance`는 추가하지 않는다.
- 월드의 캐릭터 스프라이트보다 위에 보이도록 Sorting Layer와 Order를 명시한다.
- 크기, 반지름, 색상, 알파 변화 같은 연출 값은 판정 계산과 분리해 프리팹에서 조정한다.

### 4.5 조준 확정 상태

`SkillComponent`의 런타임 데이터에 최소한 다음 상태를 추가한다.

- 현재 조준이 확정됐는지 여부
- 현재 실행에서 몇 번째 확정을 처리했는지 또는 마지막 확정 히트 인덱스

상태는 컴포넌트에만 저장하고 판단은 시스템에서 수행한다. 스킬 시작, 취소, 정상 종료, 엔티티 재사용 시 반드시 초기화한다.

`System_Skill`은 타격 쿼리나 생성 작업과 같은 코드 경로에서 그 순간의 `TargetDirection`을 확정하고 효과 실행을 연속 처리한다. 화살표 Transform이나 원본 마우스 방향을 다시 읽지 않는다. 확정 플래그만 먼저 세우고 효과 실행을 다음 틱으로 미루지 않는다.

- `TrackUntilCommit`: 확정 후 `System_SkillAim`의 방향 갱신을 중지한다.
- `TrackPerCommit`: 각 효과 사이에는 회전을 재개하고 다음 효과 직전에 현재 방향을 다시 스냅샷한다.
- `TrackContinuously`: 방향 회전은 계속 진행하며 각 채널 틱이 그 순간의 방향을 로컬 스냅샷으로 사용한다.
- 방향 표시 View가 없거나 숨겨져 있어도 ECS의 `TargetDirection`을 그대로 확정한다.

### 4.6 순수 조준 계산

방향 정규화, 0 벡터 처리, 최단 각도 회전, 최대 사거리 위치 제한이 여러 시스템에서 필요해지면 `SkillAimResolver`처럼 책임이 명확한 순수 계산 클래스로 분리한다.

- 가변 static 상태를 두지 않는다.
- Unity 오브젝트나 컴포넌트 풀을 직접 조회하지 않는다.
- 입력과 결과가 명확해 EditMode 테스트가 가능해야 한다.
- `Util`, `Helper`, `Common`과 같은 포괄적 이름은 사용하지 않는다.

## 5. 실행 유형별 처리

### 5.1 즉시 타격

`ProcessSkillHit`가 범위 쿼리를 수행하기 직전에 현재 `TargetDirection`, 즉 화살표가 표현하는 방향을 확정한다. Direction 판정은 확정된 방향과 현재 시전자 위치를 사용한다.

Position 타입의 즉시 타격 쿼리는 현재 구현이 없다. 이 계획은 위치 장판 조준을 지원하지만, Position 즉시 타격 자체의 구현은 별도 선행 또는 병행 작업으로 관리한다.

### 5.2 투사체

투사체 생성 직전에 현재 `TargetDirection`을 확정한다. 생성 방향은 다음 값을 그대로 사용한다.

```text
ProjectileDirection = CommittedTargetDirection
```

투사체가 생성된 뒤 커서를 움직여도 이미 생성된 투사체의 방향은 바뀌지 않는다. 유도 투사체는 별도 기능이며 이 계획의 범위가 아니다.

### 5.3 위치 장판

AreaEffect 생성 직전에 현재 `TargetDirection`과 원본 마우스 거리를 사용해 최종 위치를 확정한다. 원본 마우스 위치를 그대로 쓰면 화살표와 다른 방향에 장판이 생기므로 금지한다.

```text
DesiredDistance
    = Clamp(Distance(CasterPosition, MouseWorldPosition), SkillRangeMin, SkillRangeMax)

CommittedTargetPosition
    = CasterPosition + CommittedTargetDirection × DesiredDistance
```

사거리 최솟값이 없으면 0, 최댓값이 없으면 스킬이 정의한 안전 범위를 사용한다.

### 5.4 다단 히트

안전한 기본값은 첫 히트에서 현재 화살표 방향을 확정하고 남은 히트가 같은 방향을 쓰는 것이다. 스킬 데이터가 `TrackPerCommit`을 명시한 경우에만 각 히트 사이에 회전을 재개하고 다음 히트 직전 현재 방향을 다시 확정한다.

기본 MultiHit은 첫 히트 이후 화살표를 확정 방향에 고정한다. `TrackPerCommit`은 각 히트 사이에 다시 최신 커서를 따라 회전한다.

### 5.5 채널링

채널링은 입력 유지 여부와 조준 추적을 분리한다.

- HeldMask는 채널 지속 여부만 결정한다.
- 화살표와 `TargetDirection`은 채널 유지 중 설정된 속도로 계속 마우스를 추적한다.
- 각 데미지 틱 직전에 현재 `TargetDirection`을 로컬 스냅샷으로 읽는다.
- 커서를 움직이면 회전이 진행된 만큼 다음 틱의 방향 또는 위치가 바뀐다.
- 입력 해제 후 종료 처리에 들어간 뒤에는 더 이상 새 조준을 소비하지 않는다.

## 6. 스킬 사용 중 이동 감속

PoE 2에서는 이동하면서 사용할 수 있는 대부분의 공격과 주문이 스킬 동작 중 이동 속도 페널티를 받는다. 이 프로젝트도 조준 추적만 추가해 항상 최고 속도로 공격하게 만들지 않고, 스킬 동작 동안 무게감 있는 감속을 함께 적용한다.

PoE 2의 수치를 코드에 전역 상수로 고정하지 않는다. PoE 2의 일반적인 큰 감속을 기준으로 기본 유지 속도 30%를 첫 밸런스 후보로 사용하되, 실제 값은 스킬 테이블과 플레이 테스트로 조정한다.

### 6.1 스킬별 이동 정책

`SkillTable`에 `MovementMode`(`SkillMovementMode`)와 `MoveSpeedWhileUsingMul`을 추가한다.

| 이동 모드 | 의미 | 대표 용도 |
| --- | --- | --- |
| `Default` | 태그와 스킬 타입으로 프로젝트 기본 정책 해석 | 대부분의 스킬 |
| `FullSpeed` | 스킬 중에도 정상 이동 속도 유지 | 즉시 버프, 예외 스킬 |
| `Reduced` | 동작 중 지정된 배율로 감속 | 이동 가능한 공격·주문 |
| `Locked` | 동작 중 일반 이동 정지 | 제자리 고정이 필요한 강한 스킬 |
| `OwnedMotion` | 일반 이동 대신 스킬 자체 이동 시스템 사용 | 점프, 대시, 돌진 |

`MoveSpeedWhileUsingMul`은 `Reduced`일 때 유지할 최종 이동 속도의 백분율이다.

- `1~100`: 명시적인 유지 속도
- 값이 없거나 `0`: 프로젝트 기본값 30 사용
- 완전 정지는 숫자 0과 혼용하지 않고 `Locked`로 명시
- `100`은 데이터상 허용하지만 의미가 명확한 `FullSpeed` 사용을 권장

`Default`의 1차 해석 규칙은 다음과 같다.

- Attack/Spell이며 스킬 자체 이동이 없는 경우: `Reduced`
- 즉시 Buff/Toggle과 이동에 영향을 줄 필요가 없는 스킬: `FullSpeed`
- Jump 또는 Move 태그로 자체 경로를 실행하는 스킬: `OwnedMotion`
- 제자리 고정 스킬은 테이블에서 `Locked`를 명시
- AI는 기존 이동/공격 상태 전환과 충돌할 수 있으므로 1차에서는 `FullSpeed`를 유지하고 별도 데이터가 있는 경우에만 적용

### 6.2 런타임 컴포넌트

스킬 소유자에게 실행 중에만 `SkillMovementPenaltyComponent`를 부착한다.

- 페널티를 발생시킨 SkillEntityId
- 해석된 이동 모드
- 기본 유지 속도 배율
- 현재 적용 배율
- End 복구 진행률

컴포넌트에는 데이터만 저장한다. `StatComponent.FinalMoveSpeed`나 `FinalMoveSpeedMul`을 직접 변경하지 않는다. 스탯 원본을 수정하면 버프 재계산, 취소, 사망, 엔티티 재사용 때 값이 남거나 덮어써질 수 있기 때문이다.

현재 `UseSkill` 조건으로 동시에 여러 일반 스킬이 실행되지 않지만, 향후 즉시 스킬이나 중첩 실행이 허용되면 가장 강한 제약, 즉 가장 낮은 이동 배율을 선택하는 규칙으로 확장한다.

### 6.3 이동 페널티 시스템

새로운 `System_SkillMovementPenalty : IFixedUpdateSystem`을 추가한다.

- Priority 90으로 `System_Move` Priority 100 직전에 실행한다.
- 활성 `SkillMovementPenaltyComponent`만 순회한다.
- 연결된 `SkillStateComponent`와 `SkillTimingComponent`를 읽어 현재 배율을 계산한다.
- Start와 Process에서는 스킬의 유지 속도 배율을 적용한다.
- End에서는 `EndDuration` 진행률에 따라 유지 속도에서 100%까지 선형 복구한다.
- EndDuration이 0이면 즉시 100%로 복구한다.
- Channeling은 입력을 유지하는 동안 Process 배율을 계속 적용한다.
- 취소, 사망, 스킬 강제 중단, 소유자 제거, Reset에서는 컴포넌트를 제거한다.

현재 실행 순서에서는 `System_Move` Priority 100이 `System_Skill` Priority 200보다 먼저 실행되므로 스킬이 시작되는 FixedUpdate에는 아직 이동 페널티 컴포넌트가 없다. 첫 FixedUpdate 한 틱은 정상 속도로 이동하는 것을 허용하고, 다음 FixedUpdate부터 감속을 적용한다. 이 허용 오차를 위해 기존 이동·스킬 우선순위를 바꾸지 않는다.

1차 계산식은 다음과 같다.

```text
BaseMovementSpeed
    = FinalMoveSpeed × FinalMoveSpeedMul / 100

SkillMoveSpeedMultiplier
    = Start/Process: MoveSpeedWhileUsingMul / 100
    = End: Lerp(MoveSpeedWhileUsingMul / 100, 1, EndProgress)
    = None/FullSpeed: 1

FinalMovementSpeed
    = BaseMovementSpeed × SkillMoveSpeedMultiplier
```

`System_Move`는 입력 방향이나 기본 스탯 계산을 바꾸지 않고, 마지막 단계에서 `SkillMovementPenaltyComponent.CurrentMultiplier`만 곱한다. 이동 입력이 없으면 기존처럼 속도 0을 유지한다.

공격속도와 시전속도는 Start/Process/End의 실제 지속 시간을 바꾸지만 감속 강도 자체는 바꾸지 않는다. 즉 빠른 스킬은 같은 배율을 더 짧게 적용하고, 느린 스킬은 더 오래 적용한다.

### 6.4 화살표 View와의 관계

- 이동 감속은 조준 방향과 무관하며 커서를 빠르게 바꿔도 배율이 변하지 않는다.
- 화살표는 감속된 캐릭터 루트를 계속 추종한다.
- 조준 확정 후에도 스킬 End까지 화살표와 이동 복구가 각자의 수명 주기를 유지한다.
- 방향 표시 View 누락 또는 비활성 상태는 이동 페널티에 영향을 주지 않는다.
- `OwnedMotion` 스킬은 일반 이동 배율을 적용하지 않고 점프·대시 시스템의 경로와 속도를 사용한다.

## 7. 점프·이동 스킬 별도 단계

현재 점프 스킬은 Start 진입 즉시 종착점과 전체 이동 시간을 확정하고 이동을 시작한다. 이 상태에서 `EndPosition`을 매 프레임 커서에 맞춰 수정하면 다음 문제가 생긴다.

- 이동 속도와 궤적이 프레임마다 변한다.
- 충돌 경로와 화면에 보이는 경로가 어긋날 수 있다.
- 같은 입력에서도 FixedUpdate/Update 비율에 따라 결과가 달라질 수 있다.

따라서 1차 출시에서는 점프·대시·이동 스킬을 기존 스냅샷 방식으로 유지한다. 후속 단계에서 수명 주기를 다음과 같이 분리한다.

```text
Windup: 커서 추적 + 방향 화살표 회전
  → LaunchCommit: 목표 위치와 이동 경로 확정
  → Travel: 확정된 JumpComponent 경로 실행
  → Impact: 착지 판정 실행
  → Recovery
```

이때 `StartJump` 호출을 Start 진입 시점에서 LaunchCommit 시점으로 이동하고, 점프 지속 시간도 전체 스킬 시간과 이동 구간 시간을 분리한다. Rolling Slam처럼 이동 구간 자체가 커서를 계속 따라야 하는 스킬은 `TrackPerCommit` 기반의 다중 이동 세그먼트로 별도 설계하며, 하나의 점프 종착점을 매 프레임 덮어쓰는 방식은 사용하지 않는다.

## 8. 예상 변경 파일

| 파일 | 변경 내용 |
| --- | --- |
| `Assets/Scripts/Common/GlobalEnum.cs` 또는 관련 enum 파일 | `SkillAimTrackingMode`, `SkillMovementMode` 추가 |
| `Assets/Scripts/Common/Tables.cs` | `SkillTable.AimTrackingMode`, `AimTurnSpeedDegPerSec`, `MovementMode`, `MoveSpeedWhileUsingMul` 추가 |
| `Assets/Scripts/Editor/DownloadTables.cs` | 다운로드 범위 `A:AQ`, 신규 열 파싱, 기본값·범위 검증 |
| `Assets/Scripts/Common/Component/InputComponent.cs` | 현재 마우스 월드 좌표 저장 |
| `Assets/Scripts/Common/Component/SkillComponent.cs` | 조준 확정 런타임 상태 저장 및 Reset 보강 |
| `Assets/Scripts/Common/Component/SkillTargetComponent.cs` | `TargetDirection`을 회전 속도가 적용된 유효 조준 방향으로 정의 |
| `Assets/Scripts/Common/Component/SkillAimIndicatorComponent.cs` | 플레이어별 화살표 표시 상태와 마지막 적용 각도 저장 |
| `Assets/Scripts/Common/Component/SkillMovementPenaltyComponent.cs` | 실행 중인 스킬 이동 정책과 현재 배율 저장 |
| `Assets/Scripts/Common/Visual/SkillAimIndicatorView.cs` | 플레이어 자식 화살표의 표시·숨김·각도·Reset 처리 |
| `Assets/Scripts/Base/EntityBase.cs` | 방향 표시 View와 Addressables 인스턴스 핸들의 등록·노출·해제 |
| `Assets/Scripts/Common/System/System_Input.cs` | 월드 좌표를 프레임당 한 번 계산 |
| `Assets/Scripts/Common/System/System_SkillAim.cs` | FixedUpdate 논리 조준 방향 갱신 |
| `Assets/Scripts/Common/System/System_SkillAimIndicator.cs` | 화살표 표시·숨김·회전 동기화 |
| `Assets/Scripts/Common/System/System_SkillMovementPenalty.cs` | 스킬 단계별 이동 배율과 End 복구 계산 |
| `Assets/Scripts/Common/System/System_Move.cs` | 최종 이동 속도에 스킬 런타임 배율 적용 |
| `Assets/Scripts/Common/System/System_Skill.cs` | 조준 확정과 이동 페널티 생성·정리 수명 주기 적용 |
| `Assets/Scripts/Factory/EntityFactory.cs` | 플레이어 생성 경로에서만 방향 표시 프리팹 로드·바인딩 및 표시 컴포넌트 초기화 |
| `Assets/Scripts/Manager/SystemManager.cs` | 조준·방향 표시·이동 페널티 시스템 등록과 우선순위 주석 정리 |
| `Assets/Scripts/Manager/ComponentManager.cs` | 방향 표시와 이동 페널티 컴포넌트 풀 크기 추가 |
| `Assets/Prefabs/Game/Player/SkillAimIndicator.prefab` | 중심 피벗, 자식 이미지 오프셋, 정렬, 초기 숨김 설정 |
| `Assets/Scripts/Skill/Aim/ARPG.SkillAim.asmdef` | 프로젝트 전체를 나누지 않고 순수 조준 계산만 분리하는 좁은 런타임 어셈블리 |
| `Assets/Scripts/Skill/Aim/SkillAimResolver.cs` | 방향 정규화·제한 회전·표시 각도·Position 확정 위치의 순수 계산 |
| `Assets/Tests/EditMode/ARPG.SkillAim.EditModeTests.asmdef` | `ARPG.SkillAim`만 참조하는 EditMode Test Assembly |
| `Assets/Tests/EditMode/SkillAimResolverTests.cs` | 순수 조준 계산 자동 테스트 |

캐릭터 방향 표시를 위해 `TransformComponent.Rotation`이나 `FacingComponent`를 사용하지 않는다. 논리 조준은 `SkillTargetComponent`, 화면 표시는 `SkillAimIndicatorComponent`와 플레이어 귀속 `SkillAimIndicatorView`가 담당한다.

현재 프로젝트 런타임 코드는 별도 asmdef 없이 사전 정의 어셈블리 `Assembly-CSharp`에 포함된다. 따라서 `Assembly-CSharp`의 시스템을 직접 참조하는 독립 PlayMode 테스트 asmdef만 추가하는 안은 사용하지 않는다. 이 기능에서는 프로젝트 코드에 의존하지 않는 순수 조준 계산만 `ARPG.SkillAim`으로 작게 분리해 EditMode 자동 테스트하고, 입력→ECS 시스템→View→판정의 통합 검증은 현재 Unity 프로젝트에서 수동 PlayMode 시나리오로 수행한다. 전체 런타임 asmdef 마이그레이션이 별도 승인되면 그때 PlayMode Test Assembly를 추가한다.

## 9. 구현 단계

### 단계 0 — 기준선 고정

- 대표 Direction, Position AreaEffect, Projectile, MultiHit, Channeling, Jump 스킬을 선정한다.
- 순수 조준 계산을 위한 좁은 `ARPG.SkillAim` 런타임 asmdef와 이를 참조하는 EditMode 테스트 asmdef를 먼저 구성한다.
- 프로젝트 전체 asmdef 마이그레이션 없이 `Assembly-CSharp` 시스템을 참조하는 PlayMode 테스트 asmdef는 만들지 않고, 통합 동작은 단계 10.2의 수동 PlayMode 시나리오로 검증한다.
- 입력 시점, 첫 타격 시점, 투사체 생성 시점, 목표 방향을 개발 빌드에서만 추적할 수 있게 한다.
- 현재 체감과 회귀 기준을 영상 또는 테스트 시나리오로 남긴다.

완료 조건: 현재 스냅샷 방식과 각 스킬의 실제 확정 시점을 재현할 수 있다.

### 단계 1 — 입력과 조준 기반

- `MouseWorldPosition`을 입력 컴포넌트에 추가한다.
- `SkillAimTrackingMode`, `AimTurnSpeedDegPerSec`와 기본 해석 규칙을 추가한다.
- `System_SkillAim`을 Priority 190 FixedUpdate에 등록한다.
- 스킬 Start에서 최초 방향은 현재 마우스 방향으로 즉시 초기화하고 이후 변경부터 속도 제한을 적용한다.
- 일반 Direction/Position 비이동 스킬의 선딜 중 `TargetDirection`이 회전 속도로 마우스 방향을 추적하게 한다.
- AI 스킬은 기존 스냅샷 방식을 유지한다.

완료 조건: 선딜 중 마우스를 빠르게 옮기면 `TargetDirection`이 설정된 초당 각도로 최단 경로를 따라 회전한다. 이 단계에서는 대표 단발 스킬만 연결하고, 다단 히트·채널링·차지의 확정/재확정 규칙은 단계 2에서 활성화한다.

### 단계 2 — 효과 시점 확정

- 즉시 타격과 투사체는 현재 `TargetDirection`, AreaEffect는 해당 방향과 제한된 마우스 거리로 확정한다.
- 조준 확정 상태를 스킬 시작·취소·종료에 맞춰 초기화한다.
- 첫 히트 고정과 `TrackPerCommit` 다단 히트를 분리한다.
- 채널링 틱은 `TrackContinuously`를 적용한다.
- 차지 실행 경로가 완성돼 있다면 입력 해제 후 효과 직전에 확정한다.

완료 조건: 확정 전에는 회전이 진행된 만큼만 반영되고, 확정 순간 화살표가 가리킨 방향으로 판정되며, 확정 후 이동은 이미 생성된 결과에 영향을 주지 않는다.

### 단계 3 — 방향 화살표 View 동기화

- 공용 `Entity.prefab`에서 방향 표시 계층과 Sprite 직접 참조를 제거한다.
- 초기 숨김 상태의 플레이어 전용 `SkillAimIndicator.prefab`을 Addressable로 등록하고 `CreatePlayer`에서만 로드한다.
- `EntityBase`가 View 참조와 Addressables 인스턴스 핸들을 등록받고 제거 시 해제하게 한다.
- `SkillAimIndicatorComponent`와 Priority 510의 `System_SkillAimIndicator`를 추가한다.
- 화살표는 `TargetDirection`을 즉시 표시하고 자체 회전 보간이나 별도 회전 속도를 갖지 않게 한다.
- Start에서 표시, 추적 중 회전, 확정 후 End 후딜까지 방향 유지, `SkillState.None` 완료·취소·사망에서 숨김 수명 주기를 연결한다.
- `System_SkillAimIndicator.OnReset`과 플레이어 제거 경로에서 `ResetView`를 호출한다.
- 화살표 기준 축의 각도 오프셋, 반지름, Sorting Layer를 실제 이미지로 검증한다.
- `EffectManager`, 캐릭터 `Transform`, Collider2D, 그림자, `System_Animation`이 바뀌지 않는지 확인한다.

완료 조건: 커서를 급하게 바꿔도 화살표와 판정이 같은 방향을 가리키며 캐릭터 자체는 회전하지 않는다.

### 단계 4 — 스킬 사용 중 이동 감속

- `SkillMovementMode` enum과 `MovementMode`, `MoveSpeedWhileUsingMul` 테이블 필드를 추가한다.
- 스킬 시작 시 소유자에게 `SkillMovementPenaltyComponent`를 생성하고 모든 종료 경로에서 제거한다.
- Priority 90의 `System_SkillMovementPenalty`가 Start/Process 감속과 End 복구를 계산하게 한다.
- `System_Move`의 최종 속도에 계산된 런타임 배율을 곱한다.
- 스킬이 시작된 첫 FixedUpdate 한 틱은 정상 속도를 허용하고 다음 FixedUpdate부터 감속되는지 확인한다.
- Reduced, FullSpeed, Locked, Channeling, 취소·사망·Reset 경로를 검증한다.
- 첫 밸런스 후보인 유지 속도 30%와 대표 스킬별 예외값을 플레이 테스트로 조정한다.

완료 조건: 이동 가능한 일반 스킬은 시작 첫 FixedUpdate 한 틱을 제외하고 테이블 배율로 감속되고 End에서 정상 속도로 복구되며, 종료 후 런타임 페널티가 남지 않는다.

### 단계 5 — 점프·이동 스킬

- Windup과 Travel을 분리한다.
- `StartJump`를 LaunchCommit으로 이동한다.
- 이동 시작 직전 목표와 경로를 한 번 확정한다.
- 충돌, 착지 판정, 취소, 이동 속도 스케일을 다시 검증한다.
- 필요한 스킬만 다중 이동 세그먼트 재조준을 데이터로 허용한다.

완료 조건: 선딜 중 목표가 바뀌고 이동 시작 후에는 경로가 안정적으로 고정된다.

### 단계 6 — 데이터 전환과 출시

- SkillTable 시트 끝에 네 열을 추가하고 다운로드 범위를 `A:AQ`로 확장한 뒤 바이너리를 재생성한다.
- 모든 SkillTable 행에 조준과 이동 정책의 기본값 또는 명시적 예외값을 채운다.
- 기존 스킬을 플레이어 추적, AI 스냅샷, 단일 대상 고정, 감속, 완전 정지, 자체 이동 스킬로 분류한다.
- 성능 측정 후 불필요한 전체 엔티티 순회를 제거한다.
- 전투 문서와 위키를 실제 구현에 맞춰 갱신한다.

완료 조건: 표본 스킬뿐 아니라 전체 스킬 테이블이 유효한 조준 정책과 이동 정책을 가진다.

## 10. 검증 계획

### 10.1 EditMode 자동 테스트

- 커서 위치→방향 정규화와 Z 각도 계산
- 커서와 캐릭터가 같은 위치일 때 마지막 유효 방향 유지
- `AimTurnSpeedDegPerSec × fixedDeltaTime`을 넘지 않는 최대 회전량
- 0/360도 경계와 정반대 방향에서의 최단 회전 경로
- 서로 다른 렌더 프레임률에서도 같은 누적 FixedUpdate 시간 후 동일한 조준 각도
- 화살표 이미지 기준 축을 포함한 최종 표시 각도 계산
- Position 확정 위치가 화살표 방향과 제한된 마우스 거리로 계산되는지 검증

이 테스트는 `ARPG.SkillAim` 안의 순수 계산만 검증한다. `Assembly-CSharp`에 있는 테이블 정책 해석, ECS 상태 전환, View 수명 주기, 이동 페널티 수명 주기는 아래 PlayMode 시나리오에서 검증하고, 향후 런타임 asmdef 마이그레이션 후 통합 자동 테스트로 승격한다. 테이블 정책 해석의 대체 검증 항목은 §10.3에 따로 둔다.

### 10.2 플레이 검증 시나리오

1. 선딜이 긴 방향 스킬을 오른쪽으로 시작하고 타격 직전 왼쪽으로 옮긴다. 화살표가 설정된 속도로 왼쪽을 향해 회전하고 판정은 타격 순간의 화살표 방향을 사용해야 한다.
2. 선딜 동안 오른쪽→왼쪽→위쪽으로 빠르게 이동한다. 화살표는 순간이동하지 않고 매 FixedUpdate 최대 회전량 안에서 최단 경로로 따라가야 한다.
3. 투사체 생성 직전의 화살표 방향으로 발사되고, 생성 후 커서를 옮겨도 기존 투사체는 바뀌지 않아야 한다.
4. 위치 장판은 원본 커서 방향이 아니라 생성 순간의 화살표 방향에 제한된 커서 거리만큼 떨어져 생겨야 한다.
5. 기본 MultiHit은 첫 타격 이후 방향이 고정되고, `TrackPerCommit` 스킬만 다음 히트가 새 방향을 사용해야 한다.
6. Channeling은 커서를 옮긴 뒤 화살표가 회전한 만큼 다음 틱 방향이 달라져야 한다.
7. 자기 중심 360도 스킬은 화살표를 표시하지 않되, 테이블에 `AimTrackingMode`가 명시되어 있으면 그 정책대로 방향을 추적해야 한다.
8. 커서를 캐릭터 중심에 둬도 NaN, 0 방향, 화살표의 비정상적인 순간 회전이 발생하지 않아야 한다.
9. UI가 입력을 소유하는 동안 조준이 예기치 않게 점프하지 않아야 한다.
10. 공격 중 취소, 사망, 스턴, 엔티티 재사용 뒤 이전 확정 상태가 남지 않아야 한다.
11. 공격 속도가 매우 빠르거나 StartDuration이 0이어도 효과 직전의 현재 `TargetDirection`을 안전하게 소비해야 한다.
12. 모든 조준 방향에서 캐릭터, Collider2D, 그림자, 피격 범위는 회전하지 않아야 한다.
13. 단계 5 이후 점프는 선딜 중 재조준되고 이동 시작 뒤 종착점이 흔들리지 않아야 한다.
14. 화살표는 End 후딜 동안 확정 방향으로 남아 있고 `SkillState.None` 완료·취소·사망·씬 Reset에서 숨겨져야 한다.
15. 플레이어 전용 방향 표시 Addressable이 누락되거나 로드에 실패해도 스킬 판정과 데미지는 정상 동작해야 한다.
16. `Reduced` 스킬 시작 첫 FixedUpdate는 정상 속도이고, 그 다음 FixedUpdate부터 이동 거리가 테이블 배율과 일치해야 한다.
17. Start와 Process에서는 일정하게 감속되고 End에서는 정상 속도까지 끊김 없이 복구돼야 한다.
18. Channeling을 유지하는 동안 감속이 유지되고 입력을 놓으면 End 복구가 시작돼야 한다.
19. `FullSpeed` 스킬은 감속되지 않고 `Locked` 스킬은 일반 이동이 정지해야 한다.
20. 점프·대시의 `OwnedMotion`에는 일반 감속이 중복 적용되지 않아야 한다.
21. 스킬 취소·사망·스턴·씬 Reset 뒤 다음 이동에 이전 스킬의 감속이 남지 않아야 한다.
22. 감속 중에도 방향 화살표가 캐릭터를 정확히 추종하고 판정 방향과 일치해야 한다.
23. 같은 마우스 이동을 서로 다른 렌더 프레임률에서 재현해도 동일한 경과 시간의 화살표 방향이 허용 오차 안에서 같아야 한다.

Unity 검증은 현재 프로젝트와 기존 에디터 경로에서 수행한다. 테스트를 위해 두 번째 Unity 프로젝트를 만들지 않는다.

### 10.3 테이블 정책 해석 검증

`SkillUsePolicyResolver`의 `Default` 해석 규칙은 §12의 결정에 따라 EditMode 자동 테스트 대상이 아니다. 자동 테스트를 대체해 아래 항목을 PlayMode에서 실측한다.

24. AI 몬스터가 스킬을 사용하면 조준은 `SnapshotOnCommand`, 이동은 `FullSpeed`로 해석돼야 한다.
25. `AimTrackingMode`가 빈 셀인 플레이어 스킬은 일반 실행에서 `TrackUntilCommit`, Channeling 실행에서 `TrackContinuously`로 동작해야 한다.
26. `MovementMode`가 빈 셀일 때 Jump·`Move` 태그 스킬은 감속되지 않고, Toggle·Buff 스킬도 감속되지 않으며, Attack·Spell 스킬만 감속돼야 한다.
27. `AimTurnSpeedDegPerSec`와 `MoveSpeedWhileUsingMul`이 빈 셀(0)인 행은 각각 초당 720도와 30%로 동작해야 한다.
28. 조준·이동 정책이 명시된 행은 위 유도 규칙과 무관하게 항상 명시값이 적용돼야 한다.

이 항목이 회귀하면 원인은 대개 `Default` 해석 순서다. 데이터 계층 asmdef 분리가 승인되면 이 절은 EditMode 자동 테스트로 승격하고 목록에서 제거한다.

## 11. 성능 및 안전 기준

- FixedUpdate 조준 시스템은 실행 중인 스킬 엔티티만 순회한다.
- Update 방향 표시 시스템은 `SkillAimIndicatorComponent`를 가진 플레이어만 순회한다.
- `ComponentManager`와 필요한 풀 참조를 초기화 시 캐시한다.
- 런타임 순회는 `for`를 사용한다.
- 거리·0 방향 판정은 `SqrMagnitude`를 사용한다.
- 매 프레임 LINQ, 배열 생성, Delegate 생성, `Camera.main` 반복 조회를 피한다.
- 컴포넌트에는 데이터만 저장하고 추적·확정 판단은 시스템에 둔다.
- 새 시스템은 조준 갱신, 방향 표시, 이동 페널티 계산을 각각 한 가지 책임으로 분리한다.
- 이동 페널티 시스템은 활성 `SkillMovementPenaltyComponent`만 순회한다.
- `StatComponent.FinalMoveSpeed`와 `FinalMoveSpeedMul`은 런타임 스킬 감속을 위해 수정하지 않는다.
- 개발 로그와 시각화는 릴리스 빌드에서 제거하거나 비활성화한다.

## 12. 주요 위험과 결정 사항

| 항목 | 권장 결정 | 이유 |
| --- | --- | --- |
| 전체 플레이어 스킬 기본 활성화 여부 | `Default`는 활성화, 테이블 예외 허용 | PoE 2식 체감을 기본으로 하면서 개별 스킬 보호 |
| MultiHit 재조준 | 첫 타격 고정이 기본 | 화살표와 판정이 타격마다 과도하게 꺾이는 것을 방지 |
| 단일 대상 재선택 | 대상 ID 고정, 기본 화살표 미표시 | 방향이 적중을 결정하는 것처럼 오해시키지 않음 |
| 기본 조준 회전 속도 | 초당 720도에서 플레이 테스트 시작 | 정반대 방향을 약 0.25초에 추적하며 스킬별 조정 가능 |
| 화살표 추가 보간 | 미적용 | 논리 `TargetDirection` 자체가 속도 제한되므로 별도 보간 시 판정보다 늦게 보임 |
| 방향 판정의 정본 | ECS `TargetDirection` | 방향 표시 View의 존재 여부에 전투 결과가 의존하지 않음 |
| 조준 갱신 단계 | FixedUpdate Priority 190 | 저프레임에서도 Skill Priority 200 확정 전에 회전 시간을 누적 |
| 점프 중 종착점 추적 | 이동 시작 전까지만 | 궤적과 충돌 결정성 유지 |
| 캐릭터 자체 회전 | 미적용 | 방향 표시는 전용 화살표 View가 담당 |
| 화살표 View 수명 | 플레이어 생성 경로에서만 전용 Addressable 인스턴스 생성 | 공용 Entity의 오브젝트·Sprite 의존성을 제거하고, 인스턴스 핸들을 플레이어 제거 시 명시적으로 해제 |
| 일반 스킬 기본 유지 속도 | 30%에서 플레이 테스트 시작 | PoE 2식 무게감을 기준으로 하되 테이블에서 조정 가능 |
| 감속 시작 첫 FixedUpdate | 정상 속도 한 틱 허용 | 기존 Move 100 → Skill 200 실행 순서를 유지 |
| 이동 페널티 적용 구간 | Start·Process 감속, End 선형 복구 | 스킬 전체 동작과 감속 수명을 일치시킴 |
| 스탯과 스킬 감속 결합 | 최종 이동 스탯 계산 후 런타임 배율 적용 | 영구 스탯 오염과 버프 재계산 충돌 방지 |
| 점프·대시 감속 | `OwnedMotion`으로 일반 감속 우회 | 자체 경로 속도와 이중 감속 방지 |
| 정책 해석(`SkillUsePolicyResolver`)의 자동 테스트 | 현 구조에서는 미적용, §10.3 PlayMode 실측으로 대체 | 의존하는 `SkillTable`·`SkillComponent`·`GlobalEnum`이 `Assembly-CSharp`에 있고 asmdef 어셈블리는 사전 정의 어셈블리를 참조할 수 없다. 데이터 계층 asmdef 분리가 선행 조건이며 §13에 따라 이번 범위에서 제외한다 |
| 컨트롤러 지원 | 별도 단계 | 커서 추적과 다른 입력·조준 정책이 필요함 |

현재 `System_Jump`의 실제 Update 단계·Priority와 `SystemManager`의 등록 주석이 일치하지 않는다. 구현 시작 전에 주석과 실행 순서를 확인해 조준→점프→렌더 순서를 명시적으로 고정해야 한다. [src: Assets/Scripts/Common/System/System_Jump.cs] [src: Assets/Scripts/Manager/SystemManager.cs]

## 13. 범위 제외

- 유도 투사체 또는 이미 생성된 투사체의 커서 추적
- Position 타입 즉시 타격 쿼리 자체의 신규 구현
- 컨트롤러 자동 조준과 타깃 우선순위
- 캐릭터 방향별 스프라이트와 공격 애니메이션 제작
- 방향 화살표 원본 이미지의 아트 제작 자체. 플레이어 자식 View·프리팹·런타임 연동은 구현 범위에 포함한다.
- 모든 이동 스킬의 곡선 조향
- 패시브·장비·보조 젬으로 “스킬 사용 중 이동 페널티 감소” 스탯을 제공하는 빌드 시스템. 런타임 배율 구조는 후속 확장을 막지 않게 설계한다.
- 프로젝트 전체 런타임 asmdef 마이그레이션과 `Assembly-CSharp` 시스템을 직접 참조하는 PlayMode 통합 테스트 자동화. 현재 단계에서는 좁은 EditMode 자동 테스트와 수동 PlayMode 시나리오를 사용한다.
- PvP 또는 네트워크 입력 예측

## 14. 완료 정의

다음 조건을 모두 만족하면 PoE 2식 커서 추적 전환을 완료한 것으로 본다.

- 일반 플레이어 방향/위치 스킬의 `TargetDirection`이 효과 확정 직전까지 설정된 회전 속도로 최신 커서를 추적한다.
- 효과 발생 순간 화살표가 가리키는 `TargetDirection`과 실제 판정 방향이 일치하며 캐릭터 자체는 회전하지 않는다.
- 효과가 확정되거나 오브젝트가 생성된 뒤의 커서 이동은 과거 결과를 바꾸지 않는다.
- MultiHit, Channeling, AI, 점프 스킬이 문서화된 예외 규칙대로 동작한다.
- 취소와 엔티티 재사용에서 조준 상태가 완전히 초기화된다.
- 스킬 수명 주기에 맞춰 화살표가 표시·회전되고, End 후딜 동안 확정 방향을 유지한 뒤 `SkillState.None` 완료·취소·사망·Reset에서 숨겨진다.
- 일반 스킬은 테이블 정책에 따라 감속·정지·정상 이동하고 End에서 정상 속도로 복구된다.
- 점프·대시의 자체 이동에는 일반 스킬 감속이 중복 적용되지 않는다.
- 취소·사망·Reset 뒤 `SkillMovementPenaltyComponent`가 남지 않는다.
- 순수 조준 계산 EditMode 자동 테스트와 수동 PlayMode 검증 시나리오가 통과한다.
- 대표 전투에서 프레임당 할당과 유의미한 Update/FixedUpdate 비용 증가가 없다.
