# NPC 현재 마을 소속 및 공간 조회 개선 계획

> **상태 (2026-08-23)**: 구현 전 계획. 아래 핵심 계약은 사용자 확인 완료.
>
> **목표**: `NpcSaveData.VillageId`와 `VillageData.NpcEntityIds`를 NPC의 현재 월드 위치와 일치시키고,
> 활성/오프스크린 LOD 전환·파티 Hunt·건물 footprint 검사에서도 같은 공간 정본을 사용한다.
>
> **관련 문서**:
> - [NPC_SIMULATION_DESIGN.md](NPC_SIMULATION_DESIGN.md) — 개인 LOD·오프스크린 이동·파티 Hunt
> - [NPC_ACTIVITY_DESIGN.md](NPC_ACTIVITY_DESIGN.md) — 활동 및 파티 결과
> - [VILLAGE_NPC_BOUNDARY_DESIGN.md](VILLAGE_NPC_BOUNDARY_DESIGN.md) — 물리 NPC와 경제 인구의 경계
> - [../village/VILLAGE_RUNTIME_DESIGN.md](../village/VILLAGE_RUNTIME_DESIGN.md) — 마을 Bounds와 런타임 책임

---

## 1. 확정 계약

### 1.1 `VillageId`의 의미

> **개정 (2026-10-03, [NPC_AMBITION_DESIGN.md](NPC_AMBITION_DESIGN.md) D141)**: 이 절의 정의("현재 좌표가 포함된 마을")는 폐기됐다.
> `VillageId`는 **소속 마을**(상점·치료비·생활비·소문·절도·파티의 기준)이고, 위치 이동으로는 바뀌지 않으며 이주(D138~D140)로만 바뀐다.
> "지금 서 있는 마을"이 필요한 기능은 위치로 계산한다(별도 저장 필드 없음). 아래 원문과 §1.2·§2.1·§5는 이 개정 전의 계획이다.

`NpcSaveData.VillageId`와 `NpcVillageComponent.VillageId`는 **NPC의 현재 좌표가 포함된 마을 ID**다.

- NPC가 마을 Bounds 안에 있으면 해당 `VillageId`.
- 어느 마을 Bounds에도 없으면 `-1`.
- 출신지·고향·귀환처를 뜻하지 않는다.
- 별도의 `HomeVillageId`는 도입하지 않는다.
- `NpcVillageComponent`는 활성 ECS 표현, `NpcSaveData`는 영속 정본이며 두 값은 항상 동기화한다.

### 1.2 `NpcEntityIds`의 의미

`VillageData.NpcEntityIds`는 **현재 그 마을 Bounds 안에 있는 생존 물리 NPC ID 목록**이다.

- NPC가 마을에 들어오면 등록한다.
- 마을을 나가면 제거한다.
- A 마을에서 B 마을로 이동하면 A에서 제거한 뒤 B에 등록한다.
- 같은 NPC가 둘 이상의 마을 목록에 동시에 존재하면 안 된다.
- 이 목록은 경제 인구(`VillageData.Population`)를 구동하지 않는다.
- NPC가 사망하면 현재 마을 목록에서 제거한다.

### 1.3 파티 Hunt 귀환

파티는 특정 마을 ID가 아니라 **파티장이 Hunt를 시작한 실제 월드 좌표**로 귀환한다.

- 출발 시 활성 NPC는 `TransformComponent.Position`, 비활성 NPC는 `NpcSaveData.Position`을 캡처한다.
- `OriginPosition`과 `ReturnPosition`은 이 실제 출발 좌표다.
- 귀환 후 `VillageId`는 귀환 좌표를 포함하는 마을 Bounds로 다시 계산한다.
- 정상적으로 마을 안에서 출발했다면 같은 마을로 돌아오지만, 귀환 시점의 Bounds가 달라졌다면 현재 Bounds 결과를 따른다.
- 파티원 편대 오프셋 때문에 Bounds 밖에 배치되지 않도록 귀환 위치를 Bounds 안의 빈 위치로 보정한다.

---

## 2. 현재 구현의 문제

### 2.1 `VillageId`가 생성 후 고정된다

현재 `VillageId`는 NPC 생성·로드 시 설정되고 위치 이동으로는 바뀌지 않는다.

- 활성 NPC가 다른 마을로 걸어가도 기존 값 유지.
- 오프스크린 Hunt로 마을 밖에 나가도 기존 값 유지.
- `NpcEntityIds`가 현재 체류 목록이 아니라 최초 배분 코호트처럼 동작.
- `NpcSaveData` 생성자의 기본값이 `0`이라 무소속 NPC가 마을 0으로 오분류될 수 있음.

### 2.2 마을 포함 판정이 실제 Bounds를 사용하지 않는다

현행 `VillageManager.FindVillageContaining`은 `VillageData.Bounds`가 아니라
`VillageTable.SpawnRadius` 원을 사용한다. `SpawnRadius`는 NPC 생성 반경이며 현재 마을 영역의 정본이 아니다.

### 2.3 활성 위치와 저장 위치가 어긋날 수 있다

활성 NPC는 ECS `TransformComponent.Position`으로 움직이지만 `NpcSaveData.Position`은 강등·저장 전까지
과거 좌표일 수 있다. 현행 파티 모집 거리와 Hunt 출발 좌표는 saveData 위치를 사용하므로 활성 NPC에서
오래된 좌표를 참조할 가능성이 있다.

### 2.4 공간 조회가 전역 NPC 목록을 순회한다

`NpcManager.HasNpcInFootprint`는 건물 후보 하나마다 `_simulationNpcIds` 전체를 순회한다.
활성 NPC는 앞선 `CollisionUtil.IsCreatureOverlappingFootprint`에서도 검사하므로 실패 경로에서 중복 순회가 발생한다.

---

## 3. 마을 영역 판정

### 3.1 판정 정본

현재 마을 판정은 `VillageData.BoundsX/Y/W/H`를 사용한다.

```text
NPC 발 위치 → 월드 타일 좌표 → VillageData.Bounds 포함 여부
```

Bounds가 아직 저장되지 않은 마을은 `Stage` 기반 기본 Bounds를 계산해 판정한다. 마을 ECS 실체화 여부와
무관하게 `VillageData`만으로 판정할 수 있어야 한다.

### 3.2 겹치는 Bounds

둘 이상의 Bounds가 겹치면 다음 순서로 하나를 결정한다.

1. NPC 위치에서 마을 중심까지의 제곱거리가 가까운 마을.
2. 거리까지 같으면 작은 `VillageId`.

이 규칙으로 Dictionary나 등록 순서에 의존하지 않는 결정론을 보장한다.

### 3.3 API 분리

기존 플레이어 서비스 판정의 동작을 한 번에 바꾸지 않도록 Bounds 기반 API를 분리한다.

```csharp
public int FindVillageContainingBounds(Vector2 position)
```

플레이어 서비스도 실제 마을 Bounds를 따라야 한다는 결정이 확인되면 기존
`FindVillageContaining(int worldX, int worldY)`을 이 API로 위임하고 `SpawnRadius` 기반 판정을 제거한다.

---

## 4. 데이터 책임과 단일 변경 진입점

### 4.1 정본과 파생 인덱스

| 데이터 | 책임 |
|---|---|
| `NpcSaveData.Position` | 비활성 NPC 위치 정본, 활성 NPC 저장 스냅샷 |
| `TransformComponent.Position` | 활성 NPC 현재 위치 정본 |
| `NpcSaveData.VillageId` | 영속 가능한 현재 마을 캐시 |
| `NpcVillageComponent.VillageId` | 활성 ECS 현재 마을 미러 |
| `VillageData.NpcEntityIds` | 현재 마을별 NPC 파생 인덱스 |
| `_chunkNpcs` | 현재 저장 위치 기반 청크 파생 인덱스 |

### 4.2 `NpcManager` 단일 진입점

모든 마을 변경은 `NpcManager`의 한 메서드를 통해 수행한다.

```csharp
private void UpdateNpcCurrentVillage(int entityId, NpcSaveData saveData,
    Vector2 currentPosition)
```

처리 순서:

1. Bounds 기반 새 `VillageId` 계산.
2. 기존 값과 같으면 즉시 종료.
3. 기존 마을의 `NpcEntityIds`에서 제거.
4. `NpcSaveData.VillageId` 갱신.
5. 활성 상태면 `NpcVillageComponent.VillageId` 갱신.
6. 새 마을의 `NpcEntityIds`에 중복 없이 등록.
7. 위치 문맥을 사용하는 NPC 대화·소망 캐시 무효화.

메인 스레드에서 위 순서를 연속 실행해 목록과 saveData가 중간 상태로 노출되지 않게 한다.

---

## 5. 위치 변경 경로별 동기화

### 5.1 비활성 NPC

`NpcManager.UpdateOffscreenNpcPosition`에서 위치와 청크 인덱스를 갱신한 직후 현재 마을을 갱신한다.

- 솔로 Hunt analytic 이동.
- 파티 Hunt analytic 이동.
- 마을 밖 이동 시 `VillageId = -1`.
- 다른 마을을 통과하면 해당 마을 목록에 체류하는 동안 등록.

별도의 `_simulationNpcIds` 전체 마을 검사는 추가하지 않는다. 기존 `System_NpcSim`의 시간분할 예산 안에서
위치가 실제로 평가되는 NPC만 처리한다.

### 5.2 활성 NPC

활성 NPC의 일반 이동·점프·강제 이동을 빠짐없이 포착하기 위해 단일책임 시스템을 추가한다.

```text
System_NpcVillageMembership
  입력: NpcTag + TransformComponent + NpcVillageComponent
  주기: 0.2~0.3초
  책임: 활성 NPC의 현재 마을 변경 감지 및 NpcManager 위임
```

- 현재 마을 Bounds 안에 계속 있으면 O(1) 빠른 종료.
- Bounds를 벗어났거나 현재 `VillageId == -1`인 경우에만 새 마을 검색.
- 시스템은 상태를 직접 소유하지 않고 `NpcManager` 변경 진입점을 호출한다.
- `SystemManager.Initialize()`에 Village Population 대역 우선순위 주석과 함께 등록한다.

### 5.3 즉시 보정이 필요한 경로

저빈도 시스템 주기 사이의 오차를 없애기 위해 다음 경로는 위치 변경 직후 즉시 갱신한다.

- `SetNpcPosition`을 사용하는 건물 footprint 탈출.
- `TryRelocateMaterializedNpc` 충돌 복구.
- 스폰 위치 보정.
- 활성 NPC 강등 직전 최종 Transform 스냅샷.
- 세이브 직전 활성 NPC 위치 스냅샷.

### 5.4 생성과 로드

- `NpcSaveData` 생성자의 기본 `VillageId`를 `-1`로 변경한다.
- `SpawnNewNpc`는 전달된 위치를 Bounds로 판정해 현재 마을을 설정한다.
- 초기 마을 인구 배분은 마을 안의 스폰 위치를 사용하므로 결과적으로 해당 마을에 등록된다.
- 로드 시 기존 저장 `VillageId`를 신뢰하지 않고 저장 위치로 전 NPC를 재분류한다.
- 모든 `VillageData.NpcEntityIds`를 비운 뒤 재구축해 구 세이브의 고정 소속 값을 자동 교정한다.

---

## 6. 파티 Hunt

### 6.1 모집

파티장은 현재 `VillageId >= 0`인 경우에만 해당 마을의 `NpcEntityIds`에서 후보를 찾는다.

- 후보도 같은 현재 `VillageId`여야 한다.
- 모집 거리는 활성 NPC면 Transform, 비활성 NPC면 saveData 위치로 계산한다.
- 적대 관계와 기존 활동 상태 필터는 유지한다.

### 6.2 출발

파티 생성 시 파티장의 실제 현재 위치를 캡처한다.

```csharp
activity.OriginPosition = departurePosition;
activity.ReturnPosition = departurePosition;
```

`VillageId`는 귀환 목적지가 아니며 Hunt 경로 중 위치에 따라 계속 변한다.

### 6.3 귀환

Hunt 후반 analytic 이동은 `ReturnPosition`으로 보간한다. 종료 시점에는:

1. 파티장 귀환 좌표 계산.
2. 파티원별 편대 오프셋 적용.
3. 파티원이 마을 Bounds 밖 또는 정적 충돌 안이면 Bounds 안의 가까운 빈 위치로 보정.
4. 각 NPC 위치·청크·현재 마을을 갱신.
5. 결과 및 식량 보상을 처리.

### 6.4 식량 보상

현재 정책은 Hunt 결과 시점의 `VillageId`가 유효하면 그 마을에 식량을 지급한다.

- 정상 귀환이면 출발 좌표가 속한 마을에 지급된다.
- 귀환 좌표가 어떤 마을에도 속하지 않으면 마을 식량으로 환산하지 않는다.
- 마을 밖 결과의 전리품을 NPC 인벤토리에 보존할지는 별도 아이템 정책으로 남긴다.

---

## 7. 건물 footprint NPC 검사

### 7.1 책임 분리

건물 후보 검사는 두 단계로 유지한다.

1. `CollisionUtil.IsCreatureOverlappingFootprint`
   - 현재 활성 플레이어/NPC/몬스터의 ECS Collider 검사.
2. `NpcManager.HasNpcInFootprint`
   - 첫 검사가 놓치는 비활성·비동기 스폰 중 NPC 검사.

`HasNpcInFootprint`에서는 정상 활성 NPC를 건너뛴다. `RelocateNpcsOutsideFootprint`는 활성 NPC도 실제로
옮겨야 하므로 같은 필터를 적용하지 않는다. 질의와 상태 변경 함수는 통합하지 않는다.

### 7.2 청크 기반 후보 축소

`HasNpcInFootprint`가 `_simulationNpcIds` 전체를 순회하지 않도록 `_chunkNpcs`를 broad phase로 사용한다.

1. footprint AABB를 모든 NPC의 최대 MoveRadius만큼 확장.
2. 확장 AABB가 걸치는 청크 범위 계산.
3. 해당 `_chunkNpcs` 목록의 비활성 NPC만 순회.
4. `CollisionUtil.CircleOverlapsAabb`로 최종 정확 판정.

비활성 NPC는 위치 변경 시 `_chunkNpcs`가 즉시 갱신되므로 공간 인덱스를 신뢰할 수 있다. 활성 NPC는
실시간 이동 중 청크 인덱스가 늦을 수 있으므로 앞선 Collider 검사에서 담당한다.

### 7.3 `NpcEntityIds` 사용 범위

현재 마을 체류 목록으로 고친 뒤에도 footprint 정본은 `_chunkNpcs`로 유지한다.

- 건물 후보가 마을 외곽 Bounds를 걸칠 수 있음.
- 중심은 마을 밖이지만 이동 원이 footprint와 겹칠 수 있음.
- 다른 마을 또는 무소속 NPC가 경계를 통과할 수 있음.

`NpcEntityIds`는 UI·파티 모집·마을 체류 인원처럼 **마을 멤버십 질의**에 사용하고,
`_chunkNpcs`는 정확한 **공간 충돌 질의**에 사용한다.

---

## 8. 성능 예산

목표 규모는 NPC 2,400명, 마을 약 300개다.

### 8.1 금지 패턴

```text
매 프레임 2,400 NPC × 300 마을 전체 교차 검사
```

전체 `_simulationNpcIds`를 새 시스템에서 순회하지 않는다.

### 8.2 비활성 NPC

- 기존 `System_NpcSim` 예산: 0.25초마다 작업 최대 12개.
- 위치가 평가된 NPC만 Bounds 검사.
- 300마을 선형 검색도 기본 최대 `12 × 300 = 3,600` 비교/틱.
- 필요 시 마을 Bounds를 청크별로 인덱싱하지만 초기 구현의 필수 조건은 아니다.

### 8.3 활성 NPC

- 활성 `NpcTag` 풀만 0.2~0.3초 간격으로 순회.
- 기존 마을 Bounds 내부면 O(1) 종료.
- 마을 밖 NPC의 전체 마을 검색이 Profiler에서 병목이면
  `Dictionary<Vector2Int, List<int>>` 형태의 마을 청크 인덱스를 추가한다.

### 8.4 목록과 캐시

- 마을당 목표 NPC는 평균 8명 수준이므로 `List.Remove/Add` 비용은 작다.
- `VillageId`가 실제로 바뀐 순간에만 목록과 대화 캐시를 갱신한다.
- 런타임 경로에서 LINQ, 임시 List, delegate 할당을 사용하지 않는다.

### 8.5 로드

구 세이브 재분류의 `2,400 × 300 = 720,000` Bounds 비교는 로드 시 한 번만 발생하므로 허용한다.
실측 전에는 로드 경로용 별도 공간 인덱스를 추가하지 않는다.

---

## 9. 대화·서사 영향

`NpcDialogueContextBuilder`와 `DialogueTopicSelector`는 `VillageId`로 현재 정착지 연대기·자원·종족을 조회한다.

- 마을 변경 시 NPC 단위 대화 캐시를 무효화한다.
- 소망 후보가 현재 마을 문맥에 의존하면 소망 캐시도 무효화한다.
- `homeProfile`, 홈 정체성, 고향, 향수처럼 고정 출신지를 전제하는 명칭·프롬프트를 제거하거나
  현재 지역 문화 문맥으로 바꾼다.
- 개인의 과거 거주 이력이 필요해지면 `VillageId`를 재사용하지 않고 전기 로그에 별도 사건으로 기록한다.

---

## 10. 구현 단계

### Phase CV-1 — 계약과 판정 기반

- `NpcSaveData.VillageId` 기본값 `-1`.
- `VillageManager.FindVillageContainingBounds` 추가.
- 겹치는 Bounds 결정론 규칙 구현.
- `NpcManager.UpdateNpcCurrentVillage` 단일 변경 진입점 구현.
- `VillageData.NpcEntityIds`와 컴포넌트 주석을 현재 체류 의미로 변경.

### Phase CV-2 — 위치 경로 연결

- 오프스크린 위치 갱신에 현재 마을 동기화 연결.
- `SetNpcPosition`, 스폰 보정, 강등, 세이브 경로 연결.
- 활성 NPC용 `System_NpcVillageMembership` 추가 및 등록.
- 로드 시 위치 기반 전 목록 재구축.

### Phase CV-3 — 파티 Hunt 정합성

- 파티 모집 거리와 출발 좌표를 실제 현재 위치로 변경.
- 귀환 편대 위치를 Bounds 안의 빈 위치로 보정.
- 위치·청크·현재 마을 갱신 후 Hunt 결과 처리 순서 보장.
- 마을 밖 보상 처리 확인.

### Phase CV-4 — footprint 최적화

- `HasNpcInFootprint` 활성 NPC 스킵.
- 비활성 NPC 청크 범위 조회 구현.
- 최대 MoveRadius 산출 및 경계 청크 누락 방지.
- `RelocateNpcsOutsideFootprint`는 별도 상태 변경 함수로 유지.

### Phase CV-5 — 서사와 문서 정리

- 현재 위치 문맥 변경 시 대화·소망 캐시 무효화.
- 홈/고향 전제 코드와 프롬프트 제거 또는 재정의.
- `NPC_SIMULATION_DESIGN`, `VILLAGE_NPC_BOUNDARY_DESIGN`, `Wiki/npc.md`, `Wiki/village.md` 갱신.

---

## 11. 검증 계획

### 11.1 정적·단위 검증

- Bounds 내부·외부·경계 타일 판정.
- 겹치는 Bounds의 거리·ID tie-break.
- A→밖→B 전환 시 목록 원자성.
- 중복 등록과 이전 마을 잔존 없음.
- 마을 밖 NPC의 `VillageId == -1`.
- 구 세이브 위치 기반 재분류.
- footprint 확장 AABB가 청크 경계의 NPC 이동 원을 놓치지 않음.

### 11.2 플레이 모드 검증

- 활성 NPC가 마을 출입할 때 0.3초 이내 UI 목록 반영.
- 점프·충돌 탈출·강제 재배치 후 현재 마을 정합.
- 비활성 솔로 Hunt가 마을 밖에서 `-1`, 복귀 후 원래 좌표의 마을로 재등록.
- 파티원 전원이 사냥 중 목록에서 빠지고 귀환 후 재등록.
- 귀환 편대가 벽·건물·Bounds 밖에 생성되지 않음.
- 마을 변경 후 대화가 이전 마을 자원·연대기·종족 문맥을 재사용하지 않음.
- 마을 밖 사망이 잘못된 정착지 연대기에 기록되지 않음.
- 저장·로드 후 `VillageId`, `NpcVillageComponent`, `NpcEntityIds`, `_chunkNpcs` 정합.

### 11.3 성능 검증

- NPC 2,400명·마을 300개에서 `System_NpcVillageMembership` CPU와 GC 측정.
- 활성 NPC가 모두 마을 밖인 최악 표본 측정.
- 건물 후보 다중 탐색에서 기존 전역 NPC 스캔과 청크 조회 비교.
- 런타임 GC Alloc 0 확인.
- 필요할 때만 마을 청크 인덱스 도입.

---

## 12. 완료 조건

- `VillageId`가 모든 경로에서 현재 좌표의 마을 또는 `-1`을 나타낸다.
- `NpcEntityIds`가 현재 Bounds 안의 생존 NPC와 정확히 일치한다.
- NPC가 동시에 두 마을 목록에 존재하지 않는다.
- 활성/비활성 LOD 전환과 save/load 후에도 값이 드리프트하지 않는다.
- 파티 Hunt가 실제 출발 좌표로 돌아오고 귀환 위치에서 현재 마을이 재계산된다.
- footprint 질의가 비활성 NPC를 놓치지 않으면서 `_simulationNpcIds` 전체 순회를 제거한다.
- 목표 규모에서 새 시스템의 런타임 GC Alloc이 0이고 허용 가능한 프레임 비용을 유지한다.
- 홈 마을을 전제하는 코드·주석·정본 문서가 남지 않는다.
