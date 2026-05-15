# ECS 성능 최적화 계획 (요약)

> 대상: `SparseSet<T>`, `ComponentManager`, `EntityIdHelper`, 핫 ECS 시스템
> 목표: 게임플레이 동작 변경 없이 프레임당 컴포넌트 조회 비용 감소

---

## 1. 핵심 문제

- `SparseSet<T>._sparse`가 `Dictionary<int, int>` → 컴포넌트 간 조회마다 해시 비용
- 핫 시스템에서 조회 누적:
  - `System_Projectile`: Transform/Velocity/Skill/Faction/Stat/Jump/Collider 다중 조회
  - `System_Render`: Velocity/Collider/Jump 조회
  - `System_Skill`: owner/target 조회 다수
  - `FactionHelper`: Transform/Stat 조회
- Skill/Buff/Relationship은 결정론적 큰 ID(최대 10_000_000+ 단위) 사용 → 직접 배열 인덱싱 불가
  - Skill: `ownerId + (slotIndex + 1) * 1_000_000`
  - Buff: `targetId + (buffTableId + 1) * 100_000`
  - Relationship: `fromId + (toId + 1) * 10_000_000`

---

## 2. 단계별 진행 현황

| Phase | 내용 | 위험도 | 상태 |
|-------|------|--------|------|
| 0 | 프로파일러 베이스라인 측정 | — | 선행 작업 |
| 1 | `SparseSet`: 배열(`int[]`) + 폴백 Dictionary | 저 | **구현 완료** |
| 1.5 | `System_Projectile` 풀 캐싱 + Faction 후보 좁히기 | 저 | **구현 완료** |
| 1.6 | `System_Skill` 주요 핫 경로 매니저 우회 | 저 | **핫 경로 구현 완료** |
| 1.7 | `System_Render` 매니저 우회 | 저 | **구현 완료** |
| 1.8 | `System_AI_Behavior` 매니저 우회 | 저 | **구현 완료** |
| 1.9 | `FactionHelper` 이벤트 기반 Player/Hostile 인덱스 | 중 | **구현 완료** |
| 2 | Skill/Buff/Relationship → 작은 ID + 키 맵 | 중/고 | **현재 보류** |
| 3 | 핫 컴포넌트 매핑 테이블 | — | **현재 보류** |
| 4 | EntityRef 세대(generation) 안전성 | — | 별도 리팩토링 |

---

## 3. Phase 1: SparseSet 배열 + 폴백

**아이디어**: `Dictionary<int, int> _sparse`를 다음으로 교체.

```text
작은 EntityId  → int[] _sparse (직접 인덱싱)
큰 EntityId    → Dictionary<int, int> _fallbackSparse
```

**규칙**:
- `MaxDirectEntityId = 65536` 이하만 직접 배열 사용 (풀당 ~256KB)
- 그 이상은 폴백 Dictionary
- `_sparse[id] = denseIndex + 1` (0 = 없음)

**핵심 주의**: `Remove`의 swap-and-pop 후 마지막 엔티티의 sparse 매핑을 반드시 갱신해야 stale 조회 방지.

**공개 API 변경 없음** → 모든 호출 지점 그대로.

---

## 4. Phase 1.5~1.9: 핫 시스템 매니저 우회

공통 패턴: 시스템 시작 시 풀을 한 번 캐시하고 루프 안에서는 `componentManager.TryGetComponent`가 아닌 `pool.TryGet` 직접 호출.

### 1.5 Projectile
- `CheckCollision`이 `FactionHelper.GetEnemyFactionLists`로 적 faction 후보만 순회
- owner Player → Hostile 리스트, owner Hostile → Player 리스트, owner Neutral → 둘 다
- per-candidate faction 필터 제거 (리스트가 이미 필터링)
- 폴백: owner가 faction 없으면 Transform 풀 순회 (마이그레이션 안전)
- 공통 `TryHitCandidate`/`ScanCandidateList` 헬퍼로 두 경로 공유

### 1.6 Skill
- `SkillRuntimePools`로 FixedUpdate 시작 시 필요한 풀을 한 번 묶어서 캐시
- 메인 루프: `Skill/SkillState/SkillTiming/SkillCommand/State` 직접 풀 읽기
- `ProcessSkillCommands`: `State/SkillState/SkillTarget/Transform/SkillTiming/Stat` 직접 풀 읽기
- `UpdateSkillState`와 상태 처리: 메인 루프에서 읽은 `SkillComponent`를 `ref`로 전달하여 재조회 제거
- `ProcessMultiHitSkill`/`ProcessChannelingSkill`/`ProcessToggleSkill`: `SkillTiming/Input/SkillTarget` 등 직접 풀 읽기
- `ProcessSkillHit`: `SkillTarget/Transform/Collider/Stat/SkillStatBonus` 직접 풀 읽기
- `ApplySkillEffectToEntity`: 데미지 전후 `StatComponent` 재조회는 유지하되 `Stat` 풀로 직접 읽기
- `FindClosestEntity` & `CheckCircleRangeEntities`: 적 faction 후보 리스트만 순회 (전체 transform 풀 스캔 제거)
  - owner Player → Hostile 리스트, owner Hostile → Player 리스트, owner Neutral → 둘 다
  - owner faction 없으면 transform 풀 폴백 (IsHostileTo "모두 적대" 의미 보존)
- 공통 헬퍼 `FindClosestInList`/`CollectSectorHitsFromList`로 두 경로 공유
- `AddComponent`/`SetComponent`/`RemoveComponent` 같은 쓰기와 생명주기 추적은 `ComponentManager` 경로 유지
- 잔여 매니저 조회: `StartSkillInternal` 계열과 `IsSkillRunning` 같은 저빈도 helper에만 남김

### 1.7 Render
- `Transform/Velocity/Collider/Jump` 풀 캐시
- 변경된 transform은 `transformPool.SetByIndex(i, ...)`로 직접 저장

### 1.8 AI Behavior
- `AIStateComponent` 풀 캐시 (디스패치 루프 한정)
- 개별 핸들러(Chase/Melee/Ranged/Patrol/Build)는 미터치

### 1.9 FactionHelper (가장 큰 효과)
- **이벤트 기반 인덱스**: `ComponentManager`가 `FactionComponent` Add/Set/Remove 시 Player/Hostile 리스트 자동 갱신
- `FindNearestEnemy`가 전체 faction 풀 대신 **적 리스트만** 순회
- 예: 몬스터 200 + 플레이어 1 → 200×201 후보 방문이 200×1로 감소

### 검증
- `dotnet build Assembly-CSharp.csproj --no-restore` 통과
- 신규 컴파일 오류 없음
- 기존 Unity API/nullable 관련 경고만 남음

---

## 5. Phase 2: 결정론적 ID 제거 (현재 보류)

**현재 판단**: 지금은 진행하지 않음. Phase 1 계열 최적화 이후 큰 결정론적 ID가 실제 병목으로 확인될 때만 재검토.

**의도**: EntityId 숫자에 의미를 인코딩하지 않음.

```text
의미 조회 (드묾)     : 키 맵 사용 (생성/UI/명령 구성 시)
컴포넌트 조회 (빈번) : 작은 entityId로 직접 sparse 배열
```

**키 맵 구조**:
- `Dictionary<SkillKey(owner, slot), int>` ↔ `Dictionary<int, SkillKey>`
- Buff/Relationship도 동일 패턴

**마이그레이션**: 호환성 래퍼 유지 → 키 맵 추가 → 호출 지점 교체 → 공식 제거 → 세이브/로드 검증 후 폴백 제거.

**리스크**: 세이브 데이터가 결정론적 ID를 저장 중일 수 있음 → 런타임 파생 엔티티(skill/buff/relationship)는 로드 시 재구성 권장.

---

## 6. Phase 3: 핫 컴포넌트 매핑 테이블 (현재 보류)

**현재 판단**: 지금은 진행하지 않음. Phase 1 계열 최적화와 재프로파일링 이후에도 핫 시스템의 sparse 조회 비용이 의미 있게 남을 때만 검토.

Phase 1+2 후에도 부족하면, 핫 컴포넌트의 dense 인덱스를 엔티티별로 캐시.

```csharp
struct HotComponentMap {
    int TransformIndex, VelocityIndex, StatIndex,
        FactionIndex, ColliderIndex, JumpIndex;
}
```

- 대상: Transform/Velocity/Stat/Faction/Collider/Jump (+ State)
- 제거 시 swap된 엔티티 매핑도 함께 갱신 필요 → `SparseRemoveResult` 도입
- 프로파일링으로 핫 시스템에서만 사용

---

## 7. Phase 4: Entity Generation 안전성

**문제**: ID 재사용 후 stale 참조가 무관한 엔티티를 가리킴.

**대안**:
- A: `EntityRef { Index, Generation }` 패킹 — 깔끔하지만 컴포넌트 필드 대량 변경
- B: `int EntityId` 유지 + `EntityIdHelper` 검증 테이블

영향 필드: AI/Projectile/AreaEffect/Buff/Skill/Npc/Relationship 등 다수.

**Phase 1과 결합 금지** — 정확성 작업으로 별도 진행.

---

## 8. 권장 순서

1. 프로파일링 → 베이스라인 확보
2. Phase 1 (`SparseSet` 배열+폴백) 구현·검증
3. 재프로파일링
4. Phase 2/3은 현재 진행하지 않음
5. 이후 프로파일링에서 명확한 병목으로 확인될 때만 Phase 2 또는 Phase 3 재검토
6. Phase 4 (generation)는 별도 안전성 리팩토링

---

## 9. 초기 권장

**Phase 1만 시작.** 위험/보상 비율이 가장 좋고 공개 API를 유지함. `EntityIdHelper` 재작성은 프로파일링 확인된 첫 개선 이후로 미룰 것.
