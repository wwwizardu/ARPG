# 충돌 처리 & NPC 길찾기 — 설계 문서

> 관련 문서: [PHASE_C_DESIGN.md](PHASE_C_DESIGN.md) · [PHASE_D_DESIGN.md](PHASE_D_DESIGN.md) · [monsterAIPattern.md](monsterAIPattern.md)
>
> **목표**: 현재 플레이어/NPC/몬스터가 지형·건물·오브젝트를 모두 통과하는 문제를 해결한다. 플레이어는 **축 분리 슬라이딩(Axis-separated sliding)** 으로 자연스럽게 벽을 따라 미끄러지고, NPC는 **타일 기반 A\* 경로 탐색**으로 장애물을 우회한다. Unity Physics2D를 도입하지 않고 커스텀 ECS 위에서 해결한다.

---

## 1. 현재 상태 분석

### 1.1 이동 파이프라인

| 단계 | 시스템 | Priority / Phase | 역할 |
|------|--------|------------------|------|
| ① 입력 | `System_Input` | 0 / Update | 키보드 → `InputComponent.MoveDirection` |
| ② 의도 결정 | `System_Move` | 100 / FixedUpdate | Input → `VelocityComponent.{Direction, Speed}` |
| ③ AI 의도 결정 | AI State Handlers | — | `AIStateHelper.MoveToward()` 등으로 `VelocityComponent` 직접 설정 |
| ④ 위치 적용 | [System_Render.cs:109](../Assets/Scripts/Common/System/System_Render.cs#L109) | 1000 / Update | `Position += Velocity * deltaTime` 후 `gameObject.transform.position` 동기화 |

**중요**: 위치가 실제로 변경되는 지점은 ④ `System_Render`다. `System_Move`(50Hz)에서 의도만 만들고, `System_Render`(60Hz)가 매 프레임 위치를 적분한다.

### 1.2 사용 가능한 충돌 데이터 (이미 존재)

| 데이터 소스 | 위치 | 상태 |
|-------------|------|------|
| `TileFlag.Blocked` 비트 | [GlobalEnum.cs](../Assets/Scripts/Common/GlobalEnum.cs) bit 22 | 생성 시 기록되지만 이동 코드에서 조회 안 함 |
| `MapManager.IsWalkable(Vector3)` | [MapManager.cs:217](../Assets/Scripts/Manager/MapManager.cs#L217) | 구현 완료. ObjectLayer + Blocked 플래그 모두 검사 |
| `MapManager.GetTileAt(int, int)` | [MapManager.cs:164](../Assets/Scripts/Manager/MapManager.cs#L164) | 정수 타일 좌표 기반 직접 조회 |
| `BuildingManager._occupiedTiles` | [BuildingManager.cs](../Assets/Scripts/Manager/BuildingManager.cs) | 건물 점유 타일 `HashSet<Vector2Int>`. 건설 배치 검사용으로만 사용 중 |

### 1.3 빠진 것

1. **엔티티 크기/반경 정보 없음** — 모든 엔티티가 점(point)으로 취급됨
2. **이동 시점에 충돌 검사 없음** — `Position += Velocity * dt`가 무조건 적용됨
3. **AI 경로 탐색 부재** — `AIStateHelper.MoveToward()`는 직선으로만 이동
4. **TODO 훅 미구현** — [System_Move.cs:53-54](../Assets/Scripts/Common/System/System_Move.cs#L53-L54) 의 주석 처리된 `ApplyCollision` 콜

### 1.4 좌표계 가정

- **1 Unity unit = 1 tile** (1:1, 변환 불필요)
- 엔티티 `Position`은 월드 좌표 (Vector2). 타일 좌표는 `Mathf.FloorToInt(Position.x / 1f)` = `(int)Position.x` (음수 주의)

---

## 2. 충돌 분류

본 시스템은 두 축으로 분류된다.

|  | **Static** (정적) | **Dynamic** (동적) |
|---|---|---|
| **Hard** (관통 불가) | 지형 Blocked 타일, 건물 footprint, 나무·바위 등 ObjectLayer | 미구현 (Phase 3 — 엔티티간 충돌) |
| **Soft** (느린 통과/밀어내기) | — | 향후 NPC끼리 가벼운 분리 (선택) |

**Phase 1·2 범위**: Static-Hard만 다룬다. 엔티티끼리는 **통과 허용**(현재 동작 유지). 이는 ARPG 장르 관행이며 (Diablo·POE 등), 캐릭터 간 막힘은 게임 플레이 스트레스를 만든다.

---

## 3. 데이터 모델

### 3.1 신규 컴포넌트: `ColliderComponent`

```csharp
public struct ColliderComponent
{
    public float Radius;        // 원형 충돌 반경 (월드 단위)
    public CollisionLayer Layer;// 어떤 레이어와 충돌하는지 (비트마스크)
}

[System.Flags]
public enum CollisionLayer : byte
{
    None      = 0,
    Terrain   = 1 << 0,  // Blocked 타일
    Building  = 1 << 1,  // 건물 footprint
    Object    = 1 << 2,  // 나무·바위 등 ObjectLayer
    Entity    = 1 << 3,  // (Phase 3 — 미사용)
    All       = Terrain | Building | Object,
}
```

**원형 충돌만 지원**한다. 박스/캡슐은 도입하지 않는다. 이유: 2D 탑다운 ARPG에서 캐릭터 충돌은 발 밑 원 하나로 충분하며, 축 분리 슬라이딩은 원-AABB(타일) 검사로 단순화 가능.

### 3.2 엔티티별 기본 반경

| 엔티티 | 반경 | 비고 |
|--------|------|------|
| Player | 0.30 | 1타일의 30%, 좁은 통로 통과 가능 |
| Monster (소형) | 0.30 | |
| Monster (대형 — 보스) | 0.60 | 테이블 컬럼화 (`MonsterTable.ColliderRadius`) |
| NPC | 0.30 | |
| Building / Object | — | `ColliderComponent` 없음. 정적 footprint는 타일 데이터로 표현 |

`EntityFactory.CreatePlayer/CreateMonster/CreateNpc`에서 일괄 추가한다. 건물은 추가하지 않는다.

### 3.3 정적 장애물 표현

새 자료구조는 도입하지 않는다. 기존 두 소스를 합친 **단일 질의 함수**를 `MapManager`에 추가:

```csharp
// MapManager에 추가
public bool IsTileBlocked(int worldTileX, int worldTileY)
{
    // 1) Blocked 비트
    ulong tile = GetTileAt(worldTileX, worldTileY);
    if ((tile & (ulong)GlobalEnum.TileFlag.Blocked) != 0) return true;

    // 2) 건물 footprint
    if (AR.s.Building.IsTileOccupied(worldTileX, worldTileY)) return true;

    return false;
}
```

`BuildingManager`에는 `IsTileOccupied(int, int)` 공개 헬퍼를 추가 (`_occupiedTiles.Contains(new Vector2Int(x,y))`).

> **선택**: 잦은 호출 대비 캐시. 일단 직접 호출로 가고, 프로파일러에서 병목이면 청크 단위 비트맵 캐시 도입.

---

## 4. 플레이어 충돌 — Axis-separated Sliding

### 4.1 요구사항 재진술

> "진행하려던 방향에 지형이나 오브젝트가 있다면 가지 못하고, 다른 쪽 방향의 키가 같이 눌려있다면 그 방향으로 미끄러지도록"

이는 표준 **축 분리 응답**(axis-separated response)이다. 의도 이동을 X, Y 두 축으로 분해하고 각각 독립 검사한다.

- W+D 누르고 동쪽이 벽 → X는 차단되어 0, Y는 살아있어 위로 이동 (북향 슬라이드)
- D만 누르고 동쪽이 벽 → X 차단, Y는 0 → 정지

### 4.2 알고리즘

각 프레임 (또는 FixedUpdate 틱) 마다:

```
intendedDelta = velocity.Velocity * deltaTime
collider      = ColliderComponent.Radius

// X축 단독 시도
testPos = currentPos + (intendedDelta.x, 0)
if (CircleHitsStatic(testPos, collider) == false)
    currentPos.x = testPos.x

// Y축 단독 시도
testPos = currentPos + (0, intendedDelta.y)
if (CircleHitsStatic(testPos, collider) == false)
    currentPos.y = testPos.y
```

`CircleHitsStatic(pos, radius)`은 원이 걸치는 모든 타일 셀(최대 4개, 반경 0.3 기준 보통 1~2개)을 `IsTileBlocked`로 검사한다.

```csharp
// 의사코드
int minX = Mathf.FloorToInt(pos.x - radius);
int maxX = Mathf.FloorToInt(pos.x + radius);
int minY = Mathf.FloorToInt(pos.y - radius);
int maxY = Mathf.FloorToInt(pos.y + radius);

for (int tx = minX; tx <= maxX; tx++)
for (int ty = minY; ty <= maxY; ty++)
{
    if (AR.s.Map.IsTileBlocked(tx, ty) == false) continue;

    // 원-AABB 거리 검사
    float closestX = Mathf.Clamp(pos.x, tx, tx + 1);
    float closestY = Mathf.Clamp(pos.y, ty, ty + 1);
    float dx = pos.x - closestX;
    float dy = pos.y - closestY;
    if (dx*dx + dy*dy < radius*radius) return true;
}
return false;
```

### 4.3 미세 이슈 / 결정

| 이슈 | 결정 |
|------|------|
| **터널링** (한 프레임 이동량 > 타일 크기) | Player 속도 ≤ 8 unit/s, dt ≤ 1/30 → 0.27 unit/frame. 반경 0.3 보다 작아 안전. 보스 돌진 같은 고속 케이스가 생기면 sweep 도입 |
| **모서리 끼임** (대각선에서 두 축 동시 차단) | X·Y 둘 다 차단되면 정지 (정상 동작) |
| **반경이 겹친 시작 상태** (스폰 위치가 벽 안쪽) | 검사를 무시하고 통과 허용 (빠져나가는 방향). 근본 해결은 스폰 검증 — 별도 이슈로 분리 |
| **계단/언덕 (Hill 비트)** | 본 문서 범위 외. Hill은 시각 효과·지각 거리에만 영향, 충돌 무관 |

### 4.4 적용 지점

**선택지 비교**:

| 안 | 적용 위치 | 장점 | 단점 |
|----|-----------|------|------|
| A | `System_Move`에서 Direction을 클램프 | 기존 TODO 훅 사용 | FixedUpdate(50Hz)와 위치 적분(60Hz, Update) 불일치. 클램프 후에도 다음 Update 프레임에서 그대로 통과 가능 |
| **B (권장)** | `System_Render`의 위치 적분 직전에 충돌 해결 | 실제 위치가 바뀌는 시점에 검사 → 정확 | `System_Render`가 비대해짐 |
| C | 신규 `System_Collision` (Update, Priority 950) | 책임 분리 | `System_Render`의 적분 코드를 분리하거나 `Position` 한 번 더 갱신해야 함 |

**B안 채택**. [System_Render.cs:104-114](../Assets/Scripts/Common/System/System_Render.cs#L104-L114)의 `transformComponent.Position += velocity.Velocity * inDeltaTime` 한 줄을 다음으로 교체:

```csharp
Vector2 intended = velocity.Velocity * inDeltaTime;
if (_componentManager.TryGetComponent<ColliderComponent>(entityId, out var collider))
{
    transformComponent.Position = CollisionUtil.ResolveAxisSeparated(
        transformComponent.Position, intended, collider.Radius);
}
else
{
    transformComponent.Position += intended;  // 충돌체 없는 엔티티는 통과
}
```

`CollisionUtil`은 `Assets/Scripts/Common/Utility/`에 신규 정적 클래스로 추가.

> **System_Render 비대화 우려**: 위치 적분 로직만 같은 파일 내 별도 메서드(`ApplyVelocityWithCollision`)로 분리. 대규모 분기 시 추후 `System_PositionIntegration`으로 추출.

---

## 5. NPC 길찾기 — 답변과 권장안

### 5.1 "NPC는 따로 길찾기 루틴이 있어야 되나?" — **그렇다**

근거:

1. **현재 AI는 직선 추적뿐** — `AIStateHelper.MoveToward()`는 `target - self` 정규화 후 그 방향으로 속도 설정. 사이에 건물 있으면 영원히 벽에 박힌다.
2. **마을 컨셉이 도입됨 (Phase D)** — 마을 NPC는 Campfire ↔ 침대 ↔ 작업장 ↔ 우물을 매일 왕복. 건물 사이를 우회해야 한다.
3. **축 분리 슬라이딩만으로는 불충분** — 슬라이딩은 사용자 입력 의도가 매 프레임 갱신될 때 작동한다. AI 의도는 "타깃 방향 직선"으로 한 번 정해지면 갱신이 느려, 벽 모서리에 닿으면 진동(jitter)하거나 멈춘다.

### 5.2 옵션 비교

| 안 | 방식 | 비용 | 정확도 | NPC 동작 |
|----|------|------|--------|----------|
| **i** | 회피 스티어링만 (raycast + side-step) | 매 프레임 cheap | 낮음 — 凹 형 함정에 빠짐 | 단순 도주/추격엔 OK, 마을 이동엔 부족 |
| **ii** | 타일 기반 A\* (전체 경로) | 호출 시 비싸지만 캐시 가능 | 높음 | 모든 시나리오 OK |
| **iii** | A\* + 경로 따라가는 동안 슬라이딩 | (ii) + 약간 | 매우 높음 | 장기 경로 + 단기 회피 동시 |

**(iii) 채택**. 장거리는 A\*, 경로 웨이포인트 사이는 §4의 슬라이딩이 자연스레 미세 회피를 처리.

### 5.3 A\* 사양

**그리드**: 월드 타일 그대로 사용 (1 unit = 1 cell). 이미 `IsTileBlocked` 함수가 있으므로 노드 통행 가능 판정은 그것 한 번.

**휴리스틱**: 옥타일(Octile) 거리 — 8방향 이동 가정.

```
h(a, b) = max(|dx|, |dy|) + (√2 − 1) × min(|dx|, |dy|)
```

**대각선 통과 규칙**: 대각선 이동은 **인접한 두 직교 타일 모두 통행 가능**해야 허용. (모서리 끼임 방지)

**검색 한계**:
- `MaxNodesExpanded = 512` — 한 호출에 너무 오래 걸리면 실패 처리
- `MaxPathLength = 64 tiles` — 긴 경로는 잘라 부분 경로만 반환
- 둘 중 하나 초과 시 `ChaseStateHandler`는 추격 포기 (return-to-leash), `NPC` daily routine은 다음 틱 재시도

**경로 갱신 빈도**:
- 동적 타깃 추격 (`ChaseStateHandler`): 타깃 타일이 변하면 자동 재계산 (`MoveToward`에서 Goal 갱신 감지)
- 정적 목적지 (Patrol/Build/일과): 도착할 때까지 유지, 경로 진행이 막히면 자동 재계산
- **Stuck 감지**: 현재 waypoint와의 거리가 `STUCK_TIMEOUT(2초)` 동안 `PROGRESS_EPSILON_SQR(0.0025=0.05²)` 이상 줄어들지 않으면 Status를 `Computing`으로 전환 → 다음 틱에 A\* 재실행

### 5.4 신규 컴포넌트 / 시스템

```csharp
public struct PathfindingComponent
{
    public Vector2Int Goal;            // 목표 타일
    public List<Vector2Int> Waypoints; // 결과 경로 (원거리부터 채워짐)
    public int CurrentWaypointIndex;
    public float LastRecomputedTime;
    public PathfindingStatus Status;   // None / Computing / Following / Failed
}
```

```csharp
public class System_Pathfinding : IFixedUpdateSystem
{
    public int Priority => 80;  // System_Move(100)보다 먼저
    public float UpdateInterval => 0f;

    public void OnFixedUpdate(float dt)
    {
        // 1) 재계산이 필요한 PathfindingComponent를 찾아 A* 수행
        // 2) 현재 웨이포인트로 향하는 Direction 계산하여 VelocityComponent 갱신
        // 3) 웨이포인트 도착 (거리 < 0.2) 시 다음 웨이포인트로 진행
    }
}
```

A\* 자체는 `Pathfinder` 정적 클래스(`Assets/Scripts/AI/Pathfinder.cs`)에 분리. 시스템은 호출자.

**예산**: 한 FixedUpdate 틱에 최대 **2개 엔티티**의 A\*만 새로 계산. 나머지는 다음 틱으로 큐잉. 60FPS 기준 100 NPC × 0.5초 갱신이면 초당 200 호출 → 100Hz, 분산하면 틱당 2개로 충분.

### 5.5 AI 핸들러 통합

핸들러 코드는 **변경하지 않음**. `AIStateHelper.MoveToward`가 `PathfindingComponent` 보유 여부를 자동 판단하여 PF가 있으면 Goal 갱신, 없으면 기존 직선 이동으로 fallback. `StopMovement`도 PF Status를 None으로 클리어.

| 핸들러 | 동작 |
|--------|------|
| `ChaseStateHandler` | MoveToward → 타깃 타일 변경 시 자동 재계산 |
| `PatrolStateHandler` | MoveToward(target, 0.5×) → A\* 경로 따라 천천히 이동 |
| `BuildStateHandler` | MoveToward(sitePos) → 건설 위치까지 A\* |
| `FleeStateHandler` | MoveAwayFrom 사용 (PF 미트리거) — 직선 + 슬라이딩으로 도망 |
| `RetreatStateHandler` | MoveAwayFrom 사용 (PF 미트리거) — 짧은 후퇴는 직선이면 충분 |
| 신규 NPC 일과 핸들러 (Phase D) | MoveToward 호출만으로 A\* 자동 적용 |

---

## 6. 시스템 구조 / 우선순위

| 우선순위 | 시스템 | Phase | 변경 |
|----------|--------|-------|------|
| 0 | `System_Input` | Update | 변경 없음 |
| 80 | **`System_Pathfinding`** (신규) | FixedUpdate | A\* + 웨이포인트 추적 |
| 100 | `System_Move` | FixedUpdate | TODO 주석 제거. Direction 클램프 안 함 |
| 1000 | `System_Render` | Update | 위치 적분 직전에 `CollisionUtil.ResolveAxisSeparated` 호출 |

`SystemManager.Initialize()`에 `RegisterSystems(new System_Pathfinding())` 추가. CLAUDE.md의 우선순위 가이드에 따르면 80은 "AI perception" 대역인데, Pathfinding을 **이동 의도가 결정되기 전(Move:100)** 으로 두기 위한 의도적 선택.

---

## 7. 구현 단계 (Phase 분할)

### Phase 1 — 정적 충돌 (플레이어 우선)

1. `ColliderComponent` 정의 + `ComponentManager` pool 등록
2. `EntityFactory.CreatePlayer/CreateMonster/CreateNpc`에 `ColliderComponent` 추가
3. `MapManager.IsTileBlocked` + `BuildingManager.IsTileOccupied` 구현
4. `CollisionUtil.ResolveAxisSeparated` 구현
5. `System_Render`의 위치 적분에 통합
6. **검증**: 플레이어가 Blocked 타일·건물·나무 통과 못하고, 대각선 입력 시 슬라이딩 동작 확인

### Phase 2 — NPC/몬스터 길찾기 (구현 완료)

1. ✅ `Pathfinder` 정적 클래스 (A\* + Octile 휴리스틱, 8방향, corner-cutting 차단, MaxNodesExpanded=512, MaxPathLength=64)
2. ✅ `PathfindingComponent` + `System_Pathfinding` (FixedUpdate Priority 80, MAX_RECOMPUTE_PER_TICK=2)
3. ✅ AIStateHelper.MoveToward에 자동 통합 — 핸들러 코드 변경 없음
4. ✅ EntityFactory에서 모든 AI 엔티티에 PathfindingComponent 부착
5. ✅ 비활성 청크 통행 차단 + LogWarning + 실패 처리
6. NPC 일과 핸들러 (Phase D 진행도에 따라 — 별도 작업)
7. **검증**: Unity 에디터에서 마을 NPC가 건물 사이를 우회하는지, 몬스터 추격이 벽에서 막히지 않는지 플레이 테스트 필요

### Phase 3 — 엔티티간 충돌 (선택, 후순위)

ARPG 관행상 보류. 도입 시 quadtree/grid hash 필요. 본 문서 범위 외.

---

## 8. 성능 고려사항

| 항목 | 예상 부하 | 대책 |
|------|-----------|------|
| 매 프레임 충돌 검사 | 100 엔티티 × 1~4 타일 = 400 `IsTileBlocked` 호출/frame | `BuildingManager._occupiedTiles`는 HashSet, `GetTileAt`은 chunk 캐시. 둘 다 O(1) |
| A\* 호출 | 평균 32노드 확장 × 활성 NPC 100명 × 2Hz = 6400/s | 틱당 2개 제한 + 결과 캐시. 안전 |
| 메모리 | `ColliderComponent`(8B) × 1000 엔티티 = 8KB. `PathfindingComponent`(웨이포인트 리스트 포함) 평균 64B × 100 = 6.4KB | 무시 가능 |
| 청크 언로드 시 경로 무효화 | 진행 중 경로의 미래 타일이 비활성 청크로 진입하면 GetTileAt이 `0` 반환 → blocked 아님으로 오판 | A\* 시작 시 경로 전체가 활성 청크 내인지 확인. 아니면 실패 처리 |

---

## 9. 결정 요약 (TL;DR)

1. **Unity Physics2D 도입 안 함** — 커스텀 ECS 일관성 유지, 현재 파이프라인 최소 침습
2. **원형 충돌만 사용** — Player·NPC·Monster 모두 반경 하나
3. **축 분리 슬라이딩** — `System_Render`의 위치 적분 직전에 X축·Y축 독립 검사
4. **플레이어와 NPC 모두 같은 정적 충돌 검사** 사용 — `MapManager.IsTileBlocked`
5. **NPC는 추가로 A\* 길찾기** — 직선 의도만으로는 마을 환경에서 막힘. `System_Pathfinding`이 `System_Move` 보다 먼저(Priority 80) 실행되어 의도된 Direction을 웨이포인트 방향으로 갱신
6. **엔티티간 충돌은 Phase 3로 보류** — ARPG 관행과 성능 트레이드오프

---

## 10. 점프/돌진 스킬 충돌 (Phase 1.5)

`SkillType.Jump` 한 종류가 점프·대시·돌진 모두를 담당 (ArcHeight 0이면 ground charge, >0이면 포물선 점프). [System_Jump](../Assets/Scripts/Common/System/System_Jump.cs)가 매 프레임 `Position = Lerp(StartPosition, EndPosition, t)`로 좌표를 직접 쓰기 때문에 §4의 `System_Render` 충돌 처리를 우회한다.

### 10.1 채택 방식 — Per-frame freeze

**원칙**: Duration·애니메이션·스킬 상태 머신은 그대로 진행하되, **벽을 가로지르는 좌표 갱신을 차단**한다.

매 프레임 (`System_Jump.OnUpdate`):
1. `lerpedPos = Lerp(StartPosition, EndPosition, t)` — 의도된 위치
2. `reachable = CollisionUtil.ClipTrajectory(currentPos, lerpedPos, radius)` — 현재 위치에서 lerp까지 직선 경로 검사, 벽 직전까지만 반환
3. `transform.Position = reachable`

**왜 lerp 위치만 검사하면 안 되는가**: lerp는 매 프레임 `StartPosition + (EndPosition - StartPosition) * t`로 새로 계산된다. 벽이 trajectory 중간에 있고 EndPosition이 벽 너머에 있으면, lerp가 일정 t 이후엔 벽 반대편 빈 공간으로 내려간다. 이때 단순히 `IsBlockedAt(lerp)`만 검사하면 벽 안쪽 t 구간만 freeze되고, 벽을 통과한 후엔 다시 Position이 갱신되어 **캐릭터가 벽 너머로 텔레포트**된다.

`ClipTrajectory`는 이전 프레임 Position(이미 도달한 곳)에서 lerp까지의 경로를 sub-step으로 검사하므로, 벽이 사이에 있으면 절대 그 너머로 못 간다.

착지 시 (`System_Jump.Land`):
1. `reachable = ClipTrajectory(currentPos, EndPosition, radius)` — EndPosition까지 도달 가능한 곳까지만
2. `reachable`의 중심 타일(`floor(x), floor(y)`)이 Blocked인지 검사
3. Blocked → `TryFindNearestFree`로 spiral 탐색하여 빈 타일로 push
4. Blocked 아님 → `reachable` 유지

**Lenient escape 판정**: 탈출(spiral) 트리거는 "원 충돌"(circle-IsBlockedAt)이 아니라 **"중심 좌표의 타일이 Blocked인지"**(`MapManager.IsTileBlocked(floor(x), floor(y))`)로 판정한다. 반경이 인접 벽에 살짝 닿는 정도(원 overlap)는 무시하고 그 자리에 둔다 — 캐릭터 발 밑이 빈 타일이면 정상 위치로 간주, 다음 이동 시 슬라이딩으로 자연스럽게 풀린다.

**Spiral 탐색 사양**: 1~4타일 외곽 링을 순회하며 처음 발견된 빈 타일 중심으로 텔레포트. 4타일까지 못 찾으면 LogError + 위치 유지(다음 프레임 자체 stuck escape에서 재시도, 추후 도입 예정).

### 10.2 동작 시나리오

| 상황 | 결과 |
|------|------|
| 벽으로 돌진 | 벽 직전에서 좌표 정지, 남은 시간/애니메이션은 그대로 진행, 정지 위치에서 착지 |
| 벽 너머로 점프 시도 (trajectory가 벽을 가로지름) | 벽 직전에서 정지. lerp가 벽 너머로 가도 ClipTrajectory가 막아서 텔레포트 차단 |
| 비행 중 EndPosition에 건물 생성 | 새 벽 직전에서 정지, 그 자리 착지 |
| 비행 중 정지 위치 중심 타일이 Blocked가 됨 (극단) | Land에서 spiral 탐색으로 1~4타일 내 빈 칸 push |
| 정지 위치 중심은 빈 타일, 반경만 벽에 살짝 닿음 | spiral 탈출 안 함, 그 자리 유지 (다음 이동 슬라이딩으로 풀림) |
| 끝점·중간 모두 비어있음 | 정상 점프, EndPosition 도달 |

### 10.3 다른 안을 채택하지 않은 이유

| 기각된 안 | 이유 |
|-----------|------|
| 시작 시점 endpoint clipping | Duration이 그대로면 짧은 거리를 같은 시간에 이동 → "느린 점프" 시각적 부작용. 비행 중 동적으로 생긴 건물 대응 불가 |
| Duration 비례 단축 | `SkillTimingComponent`(StartTime/ProcessTime/EndTime)와 desync. 애니메이션·히트 윈도우 꼬임 |
| 착지 시점 spiral 탐색 fallback | 비행 중 freeze로 자연 처리되므로 불필요. 코드 단순화 |
| `JumpComponent`에 `LastClearPosition` 필드 | `transform.Position` 자체를 갱신 안 하면 자연스레 마지막 valid 값이 남으므로 별도 추적 불필요 |

### 10.4 변경 사항

- `CollisionUtil.IsBlockedAt`을 public으로 노출 (기존 `CircleHitsStatic`을 rename + 가시성 변경)
- `CollisionUtil.ClipTrajectory(from, to, radius)` 추가 (직선 경로 sub-step 검사, 벽 가로지르기 차단)
- `CollisionUtil.TryFindNearestFree` 추가 (spiral 탐색, 4타일 반경)
- `System_Jump.OnUpdate`: lerp 단독 검사 → `ClipTrajectory(currentPos, lerp)` 경로 검사로 변경
- `System_Jump.Land`: `ClipTrajectory(currentPos, EndPosition)` 후 발 밑 타일이 Blocked면 spiral fallback

`StartJump`(System_Skill.cs)는 변경 없음. `JumpComponent` 스키마도 그대로.

---

## 11. 오픈 이슈

- [ ] `MonsterTable`에 `ColliderRadius` 컬럼 추가 시점 (지금? 보스 도입 시?)
- [x] ~~고속 이동(돌진 스킬) 시 sweep 충돌 필요 여부~~ → §10 per-frame freeze로 해결 (Lerp 단계가 sub-step 역할)
- [x] ~~NPC가 비활성 청크로 향하는 경로 처리~~ → 실패 처리 + LogWarning (Phase 2에서 적용)
- [x] ~~플레이어의 점프 중(`JumpComponent`) 충돌~~ → §10에서 해결
- [ ] 건물 철거 시 `_occupiedTiles` 동기화 (이미 되어있는지 검증)
- [ ] 시작 위치가 벽 안에 끼어있는 엣지 케이스 (스폰 버그 등) — 현재 `ResolveAxisSeparated`가 영구 stuck 가능. 추후 stuck escape 룰 별도 검토
- [x] ~~Pathfinding stuck 감지~~ → System_Pathfinding에 progress tracking 추가 완료. waypoint 거리가 2초 동안 0.05 unit 이상 가까워지지 않으면 자동으로 Status=Computing으로 전환되어 다음 틱에 재계산. PathfindingComponent에 `LastProgressDistSqr`/`LastProgressTime` 필드
