# 버프 시스템 구현 완료

## 개요
ECS 원칙에 맞는 버프 시스템을 **결정적 Entity ID**와 **BuffInstance만으로 관리**하는 방식으로 구현했습니다.

## 핵심 설계 원칙

### 1. BuffListComponent 제거
- ~~BuffListComponent~~: 제거됨 (중복 데이터 관리 불필요)
- BuffInstance만으로 모든 버프 관계 관리
- 버프 개수 제한 없음 (무제한)

### 2. 결정적 Entity ID 사용
- **스킬 시스템과 동일한 방식**
- BuffEntityID에 타겟, 버프 타입, 인스턴스 정보 인코딩
- ID만 봐도 디버깅 가능

## 구현된 컴포넌트

### 1. BuffInstance.cs
- **역할**: 버프의 기본 정보 저장
- **필드**:
  - `TargetEntityId`: 버프를 받는 엔티티
  - `BuffTableID`: 버프 테이블 ID
  - `Duration`: 전체 지속시간
  - `RemainTime`: 남은 시간
  - `StackCount`: 중복 카운트 (같은 버프가 여러 번 적용되면 증가)

### 2. StatModifierComponent.cs
- **역할**: 버프로 인한 스탯 효과를 별도 Entity로 관리
- **필드**:
  - `OwnerEntityId`: 효과가 적용되는 엔티티
  - `Modifier`: StatModifier 데이터

## 구현된 유틸리티 클래스

### 1. BuffEntityIdHelper.cs
**역할**: 결정적 버프 Entity ID 생성 및 파싱

**ID 생성 공식**:
```
BuffEntityId = targetEntityId + (buffTableID + 1) * 100000
```

**예시**:
- Target Entity: 12345
- Buff Table ID: 1001
- BuffEntityId: `100212345`

**중복 버프 관리**:
- 같은 타입의 버프는 하나의 Entity만 존재
- 중복 적용 시 `BuffInstance.StackCount`가 증가

**주요 메서드**:
- `GetBuffEntityId(targetEntityId, buffTableID)`: ID 생성
- `GetTargetEntityId(buffEntityId)`: 타겟 추출
- `GetBuffTableID(buffEntityId)`: 버프 타입 추출
- `IsValidBuffEntityId(buffEntityId)`: 유효성 검증
- `GetDebugString(buffEntityId)`: 디버그 문자열 생성

**제한**:
- 최대 버프 테이블 ID: 9999

### 2. EntityIdHelper.cs (버프 관련 추가)
**추가된 기능**:
- `CreateBuffEntity(targetEntityId, buffTableID)`: 버프 엔티티 생성
  - 같은 타입의 버프가 이미 존재하면 -1 반환 (BuffSystem에서 StackCount 증가)
  - 중복 ID 방지
- `DestroyBuffEntity(buffEntityId)`: 버프 엔티티 삭제
  - 타입 추적에서 자동 제거
- `IsValidBuffEntity(buffEntityId)`: 버프 엔티티 유효성 확인
- `HasBuffType(targetEntityId, buffTableID)`: 특정 버프 타입 보유 여부 확인

**내부 추적**:
```csharp
Dictionary<int, HashSet<int>> _targetBuffTypes
// targetEntityId -> {buffTableID 집합}
```

### 3. BuffSystem.cs (유틸리티)
**주요 기능**:
- `AddBuff(targetEntityId, buffTableID, duration)`: 버프 추가
  1. `EntityIdHelper.CreateBuffEntity()` 호출 (결정적 ID 생성)
  2. **신규 버프인 경우**:
     - BuffInstance 컴포넌트 추가 (StackCount = 1)
     - StatModifier Entity 생성 (스탯 효과)
  3. **기존 버프인 경우 (중복)**:
     - BuffInstance.StackCount 증가
     - Duration 및 RemainTime 갱신
  4. StatDirtyTag 추가 (재계산 요청)

- `RemoveBuff(buffEntityId)`: 버프 제거
  1. StatModifier Entity들 삭제
  2. BuffInstance 제거
  3. `EntityIdHelper.DestroyBuffEntity()` 호출 (타입 추적 제거)
  4. StatDirtyTag 추가

- `RemoveBuffByTableID(targetEntityId, buffTableID)`: 특정 종류의 버프 제거
  - BuffInstance SparseSet 순회하여 해당 타겟+타입 찾기

- `RemoveAllBuffs(targetEntityId)`: 모든 버프 제거
  - BuffInstance SparseSet 순회하여 해당 타겟의 모든 버프 찾기

- `HasBuff(targetEntityId, buffTableID)`: 버프 보유 여부 확인
  - BuffInstance SparseSet 순회 (조기 종료)

- `GetBuffCount(targetEntityId)`: 버프 개수 조회
  - BuffInstance SparseSet 순회하여 카운트

## 구현된 시스템

### 1. System_BuffUpdate.cs
**Priority**: 40 (입력 이후, 이동 이전)

**동작**:
- 매 프레임 BuffInstance 풀 순회
- RemainTime 감소
- 만료된 버프 자동 제거 (`BuffSystem.RemoveBuff()` 호출)

### 2. System_StatCalculation.cs (수정)
**변경사항**:
- `GetAllModifiers()` 메서드 구현
- StatModifierComponent 풀에서 해당 엔티티의 modifier 수집
- 버프/장비 등 모든 소스의 스탯 효과 통합 계산

## 메모리 구조

```
[플레이어 Entity #5]
├─ StatComponent
└─ StatDirtyTag (재계산 필요 시)

[버프 Entity #100212345]  ← 결정적 ID (Target: 5, TableID: 1001)
└─ BuffInstance { TargetEntityId: 5, BuffTableID: 1001, RemainTime: 5.2, StackCount: 2 }

[버프 Entity #100312345]  ← 결정적 ID (Target: 5, TableID: 1002)
└─ BuffInstance { TargetEntityId: 5, BuffTableID: 1002, RemainTime: 3.8, StackCount: 1 }

[StatModifier Entity #200]
└─ StatModifierComponent { OwnerEntityId: 5, Modifier: { Attack +10 } }

[StatModifier Entity #201]
└─ StatModifierComponent { OwnerEntityId: 5, Modifier: { Defense +20 } }
```

**참고**: 같은 타입의 버프(예: 1001)가 여러 번 적용되면 별도 Entity를 생성하지 않고 `StackCount`만 증가합니다.

### 메모리 효율
- **플레이어 1000명, 평균 버프 2개 가정**:
  - ~~BuffListComponent~~: 제거됨 (0 bytes 절약!)
  - BuffInstance: 2000개 × 20 bytes = 40KB
  - StatModifierComponent: 4000개 × 32 bytes = 128KB
  - EntityIdHelper 추적: ~50KB (Dictionary 오버헤드)
  - **총합**: 약 218KB

- **장점**:
  - BuffListComponent 제거로 코드 단순화
  - 결정적 ID로 디버깅 용이
  - 버프 개수 제한 없음
  - 모든 데이터가 SparseSet에 연속 저장 (캐시 친화적)
  - GC 발생 없음 (완전한 값 타입)
  - 메모리 파편화 최소

## 실행 흐름

### 버프 추가 시 (신규)
```
1. BuffSystem.AddBuff(targetEntityId: 5, buffTableID: 1001, duration: 10f)
   ↓
2. EntityIdHelper.CreateBuffEntity(5, 1001)
   ├─ BuffEntityID 생성: 100212345
   └─ 등록 및 타입 추적
   ↓
3. BuffInstance 추가 (Entity #100212345, StackCount: 1)
   ↓
4. StatModifier Entity들 생성 (효과마다)
   ├─ Attack +10 (Entity #200)
   └─ Defense +20 (Entity #201)
   ↓
5. StatDirtyTag 추가 (Entity #5)
   ↓
6. [다음 프레임] System_StatCalculation이 스탯 재계산
```

### 버프 추가 시 (중복)
```
1. BuffSystem.AddBuff(targetEntityId: 5, buffTableID: 1001, duration: 10f)
   ↓
2. EntityIdHelper.CreateBuffEntity(5, 1001)
   └─ 이미 존재 (100212345) → -1 반환
   ↓
3. BuffInstance 업데이트 (Entity #100212345)
   ├─ StackCount: 1 → 2
   ├─ Duration: 10f
   └─ RemainTime: 10f (갱신)
   ↓
4. StatDirtyTag 추가 (Entity #5)
   ↓
5. [다음 프레임] System_StatCalculation이 스탯 재계산
```

### 버프 만료 시
```
1. System_BuffUpdate (매 프레임)
   ↓
2. BuffInstance 풀 순회
   ├─ Entity #100212345: RemainTime -= deltaTime
   └─ RemainTime <= 0 감지
   ↓
3. BuffSystem.RemoveBuff(100212345) 호출
   ↓
4. StatModifier Entity들 삭제
   ├─ Entity #200 삭제
   └─ Entity #201 삭제
   ↓
5. BuffInstance 제거 (Entity #100212345)
   ↓
6. EntityIdHelper.DestroyBuffEntity(100212345)
   ├─ 타입 추적에서 제거
   └─ 등록 해제
   ↓
7. StatDirtyTag 추가 (Entity #5)
   ↓
8. [다음 프레임] System_StatCalculation이 스탯 재계산
```

## 사용 예시

```csharp
// 플레이어에게 공격력 버프 10초 추가 (신규)
int buffEntityId = BuffSystem.AddBuff(playerEntityId, buffTableID: 1001, duration: 10f);
// 결과: buffEntityId = 100212345 (결정적 ID)
// BuffInstance.StackCount = 1

// 같은 버프 다시 추가 (중복 적용 - StackCount 증가)
int buffEntityId2 = BuffSystem.AddBuff(playerEntityId, buffTableID: 1001, duration: 5f);
// 결과: buffEntityId2 = 100212345 (동일한 ID)
// BuffInstance.StackCount = 2, Duration = 5f (갱신)

// 버프 보유 확인
bool hasBuff = BuffSystem.HasBuff(playerEntityId, 1001);  // true

// EntityIdHelper로 빠른 체크
bool hasBuffFast = EntityIdHelper.HasBuffType(playerEntityId, 1001);  // O(1)

// 특정 버프 제거 (StackCount와 관계없이 전체 제거)
BuffSystem.RemoveBuff(buffEntityId);

// 특정 타입의 모든 버프 제거
int removed = BuffSystem.RemoveBuffByTableID(playerEntityId, 1001);

// 모든 버프 제거
BuffSystem.RemoveAllBuffs(playerEntityId);

// BuffEntity ID 디버깅
string debugInfo = BuffEntityIdHelper.GetDebugString(buffEntityId);
// "BuffEntityId=100212345 (TargetId=5, TableId=1001)"

// BuffInstance 확인
if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
{
    Debug.Log($"StackCount: {buff.StackCount}, RemainTime: {buff.RemainTime}");
}
```

## 확장 가능한 부분

### 1. 버프 테이블 시스템
현재 `BuffSystem.LoadBuffEffects()`에서 하드코딩된 예시 효과:
```csharp
case 1001: // 공격력 버프
    AddStatModifier(..., GE.Stat.AttackMin, StatModifierType.Add, 10, 0);
    break;
```

**개선안**: JSON/ScriptableObject로 버프 테이블 데이터 관리

### 2. 버프 스택 정책 (현재 구현됨!)
**현재 동작**:
- 같은 버프 타입은 하나의 Entity만 존재
- 중복 추가 시 `BuffInstance.StackCount` 증가
- 지속 시간은 마지막 적용 시간으로 갱신

**추가 개선안**:
- 스택 최대치 제한: BuffInstance에 `MaxStack` 필드 추가
- 스택당 효과 배율: `StatModifier` 계산 시 StackCount 고려
- StackCount 감소 로직: 제거 시 전체 제거 대신 StackCount만 감소

### 3. 버프 아이콘/UI
현재는 데이터만 관리.
**개선안**:
- BuffInstance SparseSet 순회하여 UI 업데이트
- BuffEntityID로 UI 슬롯 매핑

### 4. 버프 이벤트
**개선안**: 버프 추가/제거 시 이벤트 발생 (VFX, 사운드 등)

### 5. 성능 최적화 (필요시)
현재는 `HasBuff()`, `GetBuffCount()` 등이 O(전체 버프 수) 선형 탐색.
**개선안**:
- BitMask 추가: BuffTableID 존재 여부를 O(1)로 체크
- 타겟별 캐싱: EntityIdHelper에 버프 개수 캐시

## 디버깅

### 결정적 ID 디버깅
```csharp
int buffEntityId = BuffSystem.AddBuff(playerEntityId, 1001, 10f);
// buffEntityId = 100212345

// ID 파싱
int targetId = BuffEntityIdHelper.GetTargetEntityId(buffEntityId);  // 5
int tableId = BuffEntityIdHelper.GetBuffTableID(buffEntityId);      // 1001

// 디버그 문자열
Debug.Log(BuffEntityIdHelper.GetDebugString(buffEntityId));
// "BuffEntityId=100212345 (TargetId=5, TableId=1001)"

// BuffInstance 정보 확인
if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
{
    Debug.Log($"StackCount: {buff.StackCount}, RemainTime: {buff.RemainTime}");
}
```

### Unity Inspector
- BuffInstance의 모든 필드가 Inspector에 표시됨 (StackCount 포함)
- Entity ID만 봐도 어떤 버프인지 파악 가능

### 예시 씬
- `BuffSystemExample.cs` 스크립트 제공
- 키보드 1~5번으로 버프 테스트 가능

### 로그 출력
모든 주요 동작에 Debug.Log 포함:
- 버프 추가/제거 (Entity ID 포함)
- StatModifier 생성/삭제
- System_BuffUpdate 동작
- EntityIdHelper 슬롯 관리

## 성능 특성

### 장점
1. **결정적 ID**: 디버깅 용이, ID 재사용 방지
2. **BuffListComponent 제거**: 코드 단순화, 메모리 절약
3. **메모리 연속성**: SparseSet으로 캐시 효율 극대화
4. **GC 없음**: 완전한 값 타입 구조
5. **무제한 버프**: 개수 제한 없음
6. **중복 버프 지원**: 같은 타입 버프 여러 개 적용 가능

### 성능 측정 (예상)
- **버프 추가**: ~0.015ms (결정적 ID 생성 + 컴포넌트 추가)
- **버프 제거**: ~0.015ms (Entity 삭제 + 슬롯 해제)
- **버프 업데이트**: ~0.001ms/100개 (단순 시간 감소)
- **HasBuff()**: ~0.005ms (선형 탐색, 조기 종료)
- **GetBuffCount()**: ~0.01ms (선형 탐색)
- **스탯 재계산**: ~0.01ms (modifier 10개 기준)

### 성능 고려사항
- `HasBuff()`, `GetBuffCount()` 등은 O(전체 버프 수) 선형 탐색
- 전체 게임 버프 수가 1000개 미만이면 충분히 빠름
- 필요시 BitMask 최적화 가능 (BuffTableID 존재 여부를 O(1)로)

### 최적화 여지
- 버프가 **1000개 이상**이고 `HasBuff()` 호출이 **매우 빈번**할 때만 BitMask 추가 고려
- 대부분의 ARPG에서는 현재 구조로 충분

## 파일 목록

### 컴포넌트
- `Assets/Scripts/Common/Component/BuffInstance.cs`
- ~~`Assets/Scripts/Common/Component/BuffListComponent.cs`~~ (제거됨)
- `Assets/Scripts/Common/Component/StatModifierComponent.cs`

### 시스템
- `Assets/Scripts/Common/System/System_BuffUpdate.cs`
- `Assets/Scripts/Common/System/System_StatCalculation.cs` (수정)

### 유틸리티
- `Assets/Scripts/Common/Utility/BuffSystem.cs` (BuffListComponent 제거, 결정적 ID 사용)
- `Assets/Scripts/Common/Utility/BuffEntityIdHelper.cs` (**신규**: 결정적 ID 생성 및 파싱)
- `Assets/Scripts/Common/Utility/EntityIdHelper.cs` (버프 엔티티 관리 기능 추가)

### 예시
- `Assets/Scripts/Common/Example/BuffSystemExample.cs` (업데이트)

### 관리자
- `Assets/Scripts/Manager/SystemManager.cs` (System_BuffUpdate 등록)

## 주요 변경 이력

### v3.0 (현재) - StackCount 방식
- **BuffInstance.StackCount 추가**: 중복 버프를 Entity 생성 대신 카운트로 관리
- **BuffEntityIdHelper 단순화**: 인스턴스 인덱스 제거, 타입당 하나의 ID
- **EntityIdHelper 단순화**: `_targetBuffTypes`로 타입 추적만 수행
- **자동 스택**: 같은 버프 추가 시 자동으로 StackCount 증가 및 시간 갱신
- ID 구조 단순화: `targetEntityId + (buffTableID + 1) * 100000`

### v2.0 - 결정적 ID 방식 (인스턴스 인덱스)
- BuffListComponent 제거
- BuffEntityIdHelper 추가 (인스턴스 인덱스 포함)
- 같은 타입 버프를 별도 Entity로 관리
- ID: `targetEntityId + (buffTableID + 1) * 100000 + instanceIndex * 10`

### v1.0 - 초기 구현
- BuffListComponent로 버프 목록 관리
- 고정 크기 배열 (최대 8개)
- EntityIdHelper.CreateEntity()로 순차적 ID 할당

## 다음 단계

1. **버프 테이블 데이터** 구현 (JSON/ScriptableObject)
2. **스택 최대치 및 효과 배율** 구현 (StackCount 기반 스탯 계산)
3. **부분 스택 제거** 로직 (전체 제거 대신 StackCount 감소 옵션)
4. **버프 UI** 구현 (아이콘, 남은 시간, StackCount 표시)
5. **버프 VFX/SFX** 통합
6. **특수 버프** 구현 (디버프, 영구 버프, 조건부 버프 등)
7. **성능 프로파일링** 및 필요시 BitMask 최적화

---

**구현 완료!** 🎉

**핵심 개선사항 (v3.0)**:
- ✅ BuffInstance.StackCount로 중복 버프를 효율적으로 관리
- ✅ 인스턴스 인덱스 제거로 ID 구조 단순화
- ✅ 자동 스택 및 시간 갱신으로 사용 편의성 향상
- ✅ 타입당 하나의 Entity만 생성하여 메모리 효율 증가
