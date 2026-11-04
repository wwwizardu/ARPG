---
name: monster-crowd-movement
description: 몬스터끼리 서로 막고 비켜 가는 군집 이동 모듈의 이동 구조(System→속도→Render), 대기·통과 규칙, 플레이어·NPC 몸 막기, 성능 장치, 검증 계약.
type: topic
status: implemented-contract-verified-play-pending
last_updated: 2026-11-05
sources:
  - Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs
  - Assets/Scripts/Creature/MonsterCrowdMotion.cs
  - Assets/Scripts/Creature/MonsterCrowdGrid.cs
  - Assets/Scripts/Creature/MonsterCrowdSettings.cs
  - Assets/Scripts/Common/Component/MonsterCrowdComponent.cs
  - Assets/Scripts/Common/Component/ResolvedVelocityComponent.cs
  - Assets/Scripts/Manager/MonsterManager_Crowd.cs
  - Assets/Scripts/Common/Utility/CollisionUtil.cs
  - Assets/Scripts/Common/System/System_Render.cs
  - Assets/Scripts/Common/System/System_Move.cs
  - Assets/Scripts/Common/System/System_Pathfinding.cs
  - Assets/Scripts/Common/System/System_Skill.cs
  - Assets/Scripts/Factory/EntityFactory.cs
  - Assets/Scripts/Manager/SystemManager.cs
  - Assets/Scripts/Manager/ComponentManager.cs
  - Assets/Scripts/Editor/MonsterCrowdDiagnostics.cs
  - Assets/Scripts/Common/Utility/SkillHelper.cs
  - Assets/Scripts/AI/StateHandlers/FollowStateHandler.cs
  - Assets/Scripts/Manager/NpcManager_VillageWalk.cs
  - Assets/_BinaryData/TableData/MonsterTable.bytes
  - Assets/_BinaryData/TableData/CreatureTable.bytes
  - Assets/_BinaryData/TableData/AiTable.bytes
  - Assets/_BinaryData/TableData/SkillTable.bytes
  - Docs/map/COLLISION_PATHFINDING_DESIGN.md
---

# 몬스터 군집 이동

> **상태: 구현, 계약 검사 통과, 플레이 확인 전 (2026-11-04).** 첫 구현은 커밋 `7dae4dcb`, 이후 동작·성능·구조·이동 구조를 고쳤다.
> 어떤 엔티티끼리 막는지(충돌 분류)와 이동 파이프라인·stuck 규칙은 `Docs/map/COLLISION_PATHFINDING_DESIGN.md` §1.1·§2·§5.3·§6(2026-11-04 개정)이 정본이고, 세부 동작은 코드가 정본이다.
> [src: Docs/map/COLLISION_PATHFINDING_DESIGN.md §2, Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs, Assets/Scripts/Editor/MonsterCrowdDiagnostics.cs]

## 1. 한 줄 요약

일반 몬스터는 고정 틱마다 서로를 원으로 점유하고, 막히면 30·60·90도로 비켜 가거나 기다린다.
계산 결과는 **위치가 아니라 이번 틱에 쓸 속도**(`ResolvedVelocityComponent`)이고, 실제 위치는 System_Render가 그 속도로 옮긴다.
플레이어와 실체화된 NPC는 몬스터 몸에 막혀 미끄러지지만, 몬스터 계산은 둘을 모른다(몬스터는 비키거나 밀리지 않는다).
[src: Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs, Assets/Scripts/Common/Component/ResolvedVelocityComponent.cs, Assets/Scripts/Common/System/System_Render.cs]

## 2. 결정 요약

| 결정 | 이유 | 출처 |
|------|------|------|
| 군집 시스템은 FixedUpdate 270에서 속도만 내고, 위치는 Render(Update 1000)가 그 속도 × 프레임 시간으로 옮긴다 | 이동 구조 원칙: System이 계산해 속도를 정하고 Render가 위치를 옮긴다 | System_MonsterCrowdMovement.cs, System_Render.cs, Docs/map/COLLISION_PATHFINDING_DESIGN.md §1.1 |
| 우선순위 270은 AI(50)·길찾기(80)·이동(100)·스킬(200)·HP 확인(250) 뒤다 | 스킬이 제자리 채널 시작 순간 속도를 0으로 만드는 것과 같은 틱의 사망까지 반영한다 | SystemManager.cs, System_Skill.cs |
| 희망 속도(`VelocityComponent`)는 바꾸지 않고 확정 속도를 따로 둔다. 맡지 않는 틱(꺼짐·제외·플레이어 없음)에는 `Active`를 꺼 기본 이동으로 돌려준다 | AI·길찾기가 정한 의도를 덮지 않는다. 기능을 꺼도 기존 이동이 그대로 동작한다 | ResolvedVelocityComponent.cs, System_MonsterCrowdMovement.cs |
| 걷기/서기(MoveState)는 System_Move만 정한다. 기다리는 몬스터는 제자리 걷기 모션 | 두 시스템이 번갈아 쓰면 막힌 몬스터의 애니메이션이 고정 틱마다 첫 프레임으로 돌아간다 | System_MonsterCrowdMovement.cs, System_Move.cs |
| `PassWaitTime` 넘게 막히면 막은 몬스터를 통과 상대로 지정한다. 통과 상대를 따라가(최대 8단계) 자기에게 돌아오는 고리면 각자 앞 몬스터를 통과한다. 서로 막은 둘(길이 2)과 셋 이상의 고리 교착이 같은 규칙이다. 움직일 수 없는 몬스터는 혼자 지정해도 통과 | 통로·네거리 교착을 푼다. 끝이 기다리지 않는 앞 몬스터로 끊기는 줄 서기와 고리에 붙은 꼬리는 고리가 아니어서 그대로 기다린다(길목 전투 유지) | MonsterCrowdMotion.cs, MonsterCrowdGrid.cs `ResolvePassCycles`·`CanPassThrough` |
| 몬스터에 막힌 대기만 A* stuck 재계산을 미룬다 | 정면이 지형에 막힌 대기는 길을 다시 찾아야 한다 | System_MonsterCrowdMovement.cs `DeferPathStuck`, System_Pathfinding.cs |
| 뒤로 가는 우회 후보는 고르지 않는다 | 벽에 미끄러진 옆 후보의 뒤쪽 잔여가 선호 가산점으로 대기를 이기면 통로에서 앞뒤로 흔들린다 | MonsterCrowdMotion.cs |
| 앞서가는 이웃은 지난 틱 이동 속도로 예측하고, 덜 막힐 때만 반영한다. 맞닿으면 속도를 맞춘다 | 줄지어 따라갈 때 앞 개체의 이동 전 자리에 막히지 않게 한다 | MonsterCrowdGrid.cs `FollowFraction` |
| 이미 겹친 이웃은 겹침 풀기를 막지 않고, 맞닿은 이웃 쪽 성분만 뺀다 | 세 개체 이상 겹쳐도 멈추지 않고 겹침을 나눠 가지며 풀린다 | MonsterCrowdMotion.cs `RecoverOverlap` |
| 군집 계산의 지형 검사는 `SlideTrajectory`(축 분리 미끄러짐, 충돌 안에서 탈출) | 기본 이동(`ResolveAxisSeparated`)과 같은 벽 미끄러짐 | CollisionUtil.cs, MonsterManager_Crowd.cs |
| 플레이어만 몬스터에 막힌다. 정면이면 멈추고 비스듬하면 몸을 따라 미끄러지며, 겹친 상태에서는 바깥으로만 나간다. 몬스터 계산에는 플레이어를 넣지 않는다 | 몬스터는 공격할 수 있는 자리에서 공격만 하고 플레이어 움직임 때문에 움직이지 않는다. 플레이어는 둘러싸이면 걸어서 빠져나갈 수 없다(긴장감, 2026-11-04 사용자 결정) | Docs/map/COLLISION_PATHFINDING_DESIGN.md §2, System_MonsterCrowdMovement.cs `ResolveBlockedByMonsters`, MonsterCrowdGrid.cs `ClipExternal` |
| 실체화된 NPC도 플레이어와 같은 규칙으로 몬스터에 막힌다. 몬스터에 막힌 동안은 NPC 길찾기의 stuck 재계산을 미룬다 | 플레이어만 막히고 NPC는 뚫고 다니면 어색하다(2026-11-05 사용자 판단). 길찾기는 몬스터를 모르므로 다시 찾아도 같은 길이고 경고만 쌓인다 | Docs/map/COLLISION_PATHFINDING_DESIGN.md §2·§5.3, System_MonsterCrowdMovement.cs `ResolveBlockedByMonsters` |

버린 선택지:
- 플레이어를 군집 격자에 장애물로 등록: 몬스터가 다가오며 앞보기에 플레이어 몸이 걸려 옆으로 돌고, 겹치면 몬스터가 밀려나 "몬스터는 플레이어 때문에 움직이지 않는다"와 어긋난다 [추론: 2026-11-04 작업 판단]
- Update에서 위치를 직접 옮기고 Render 적분을 건너뛰기(첫 구현): 이동 구조 원칙에 어긋나 FixedUpdate + 확정 속도로 바꿨다 [src: Docs/map/COLLISION_PATHFINDING_DESIGN.md §1.1, Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs]
- 앞보기 거리만큼 이웃을 넓게 모아 공유: 후보마다 검사할 이웃이 늘어 밀집에서 더 느리다 [src: Assets/Scripts/Creature/MonsterCrowdGrid.cs `GatherNeighbors` 주석]
- 기다릴 때 Idle 표시: 재시도마다 대기와 이동이 바뀌어 다시 깜빡인다 [추론: 2026-11-03 작업 판단]

## 3. 고정 틱 흐름

1. FixedUpdate: AI(50)·System_Pathfinding(80)·System_Move(100)·System_Skill(200)이 희망 속도와 MoveState를 정한다 [src: Assets/Scripts/Manager/SystemManager.cs]
2. FixedUpdate 270 System_MonsterCrowdMovement:
   1. 설정 적용 → 플레이어 중심 격자 시작 → 참여 몬스터 등록(MonsterTag, 살아 있음, 반경 > 0, 점프·돌진·넉백 아님, 활성 청크) [src: System_MonsterCrowdMovement.cs]
   2. 겹침 풀기 `RecoveryIterations`회 (격자 안 위치만 옮긴다) [src: System_MonsterCrowdMovement.cs, MonsterCrowdMotion.cs]
   3. 틱마다 시작 순서를 돌려, 몬스터마다 경로 앞보기 → 이동 결정 → 격자 위치 확정 → 확정 속도 = (겹침 보정 + 이동) ÷ 틱 시간 기록 → 몬스터에 막힌 대기면 길찾기 진행 시각 갱신 [src: System_MonsterCrowdMovement.cs]
   4. 플레이어·NPC 몸 막기(`PlayerBlockedByMonsters`·`NpcBlockedByMonsters`): 몬스터 이동을 모두 마친 격자 위치에 대해 플레이어와 활성 NPC의 이번 틱 희망 이동을 자른다. 막은 몬스터 쪽으로 파고드는 성분만 빼고 한 번 더 검사해 몸을 따라 미끄러진다. 결과는 각자의 확정 속도다. 이동이 줄었으면 길찾기 진행 시각을 갱신한다. 넉백·돌진·점프 중이거나 비활성·제거 중인 NPC는 기본 이동으로 돌려준다 [src: System_MonsterCrowdMovement.cs `ResolveBlockedByMonsters`, MonsterCrowdGrid.cs `ClipExternal`]
   5. `TransformComponent`는 바꾸지 않는다 [src: System_MonsterCrowdMovement.cs `Simulate` 주석]
3. Update 1000 System_Render: 확정 속도가 켜져 있으면 희망 속도 대신 그 속도로 `ResolveAxisSeparated` 적분을 한다. 넉백·돌진 중이면 둘 다 쓰지 않는다 [src: Assets/Scripts/Common/System/System_Render.cs]

계산은 고정 틱 이동량 기준이고 실제 이동은 프레임 시간 적분이라, 틱 사이 프레임 길이에 따라 아주 작은 겹침이 생길 수 있다. 다음 틱 겹침 풀기가 정리한다 [추론].

## 4. 성능 장치

- 정면과 후보 6방향은 한 틱 이동 범위로 이웃을 한 번 모아 같이 쓰고, 앞보기는 따로 검색한다 [src: MonsterCrowdMotion.cs, MonsterCrowdGrid.cs]
- 끝까지 가도 최고 점수를 못 넘는 후보는 검사하지 않고(`MaximumScore`), 정면이 막히면 앞보기를 생략한다. 결과는 같다 [src: MonsterCrowdMotion.cs]
- 기다리는 몬스터는 `RetryInterval`마다만 다시 시도한다 [src: MonsterCrowdMotion.cs]
- 경로 앞보기: 지금 향하는 경유점은 검사하지 않고, 2칸 안의 다음 경유점 3개만, `PathLookAheadTicks`마다 몬스터별로 엇갈려 본다 [src: System_MonsterCrowdMovement.cs `ResolvePathHeading`]
- 고정 틱(50Hz)에 돌아 프레임이 50fps보다 높으면 초당 계산량이 줄고, 낮으면 한 프레임에 틱이 여러 번 돌 수 있다 [추론: ProjectSettings/TimeManager 고정 틱 0.02초 기준]

## 5. 튜닝값 (MonsterManager 인스펙터)

| 설정 | 기본값 | 의미 |
|------|--------|------|
| Enabled | 켜짐 | 모듈 사용 |
| AvoidanceEnabled / OverlapRecoveryEnabled | 켜짐 / 켜짐 | 비켜 가기 / 겹침 풀기 |
| PlayerBlockedByMonsters / NpcBlockedByMonsters | 켜짐 / 켜짐 | 플레이어 / 실체화된 NPC가 몬스터 몸에 막힌다. 군집 이동이 꺼지면 함께 꺼진다 |
| GridWidth / CellSize | 64 / 1 | 플레이어 중심 배열 격자. 밖은 overflow 목록 |
| Clearance | 0.025 | 이동 검사 여유 거리 |
| LookAheadTime | 0.18초 | 앞보기 거리 |
| SideHoldTime / RetryInterval | 0.5초 / 0.12초 | 비켜 갈 쪽 유지 / 대기 재시도 |
| PassWaitTime / PassHoldTime | 1초 / 0.3초 | 통과까지 대기 / 떨어진 뒤 통과 유지 |
| RecoveryIterations / RecoverySpeed | 2 / 2 | 겹침 풀기 반복 수 / 초당 최대 거리 |
| PathLookAheadTicks | 3 | 경로 앞보기 간격(고정 틱) |
| ExcludedMonsterTableIds | 없음 | 제외할 몬스터 종 |

[src: Assets/Scripts/Creature/MonsterCrowdSettings.cs]

## 6. 검증

- 계약: `ARPG/Diagnostics/Monster Crowd/Run Contracts` (EditMode, 몇 초). 격자·접촉·겹침·대기·통과·고리 교착(격자 단위 고리 판정, 네 마리 네모 교착 풀림)·길찾기 미루기·따라가기·앞보기 횟수·확정 속도 넘기기와 플레이어 몸 막기(정면에서 멈춤, 몸 따라 미끄러짐, 겹친 채 파고들기 막힘·걸어 나가기, 몬스터는 플레이어를 무시, 설정을 끄면 기본 이동), NPC 몸 막기(정면에서 멈춤, 막힌 동안 길찾기 미룸, 몸 따라 미끄러짐, 몬스터는 NPC를 무시, 비활성·설정 끔이면 기본 이동)를 검사한다. 검사도 Render처럼 확정 속도 × 시간으로 위치를 옮긴다. 가짜 지형이라 실제 벽 미끄러짐과 System_Pathfinding 동작은 검사하지 않는다 [src: Assets/Scripts/Editor/MonsterCrowdDiagnostics.cs]
- 부하: `ARPG/Diagnostics/Monster Crowd/Measure 50 100 200` (EditMode). 평지에서 고정 틱 계산 한 번의 시간·메모리 할당·이웃 검사·주변 검색·지형 검사 횟수를 잰다. Render 쪽 비용은 포함하지 않는다 [src: Assets/Scripts/Editor/MonsterCrowdDiagnostics.cs]

## 7. 연결

- [monster_spawn_runtime.md](monster_spawn_runtime.md): 몬스터를 만들 때 EntityFactory가 `MonsterCrowdComponent`와 `ResolvedVelocityComponent`를 붙인다. 플레이어(`CreatePlayer`)와 NPC(`CreateNpc`)도 `ResolvedVelocityComponent`를 받는다 [src: Assets/Scripts/Factory/EntityFactory.cs]
- [npc.md](npc.md): 실체화된 NPC의 이동(마을 걷기·전투·동행)이 몬스터 몸에 막힌다 [src: Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs]
- `Docs/map/COLLISION_PATHFINDING_DESIGN.md`: 지형 충돌·A*·stuck 감지와 엔티티 충돌 분류(§2 2026-11-04·05 개정: 몬스터끼리·플레이어와 NPC→몬스터 막음, 플레이어↔NPC·NPC끼리 통과)의 원본 [src: Docs/map/COLLISION_PATHFINDING_DESIGN.md §2·§5.3·§6]

## 8. 미해결 / 주의

- 기본값이 켜짐이고 씬·프리팹에 저장된 군집 설정이 없어 모든 일반 몬스터에 적용된다 [src: Assets/Scripts/Creature/MonsterCrowdSettings.cs, Assets/Scenes/GameScene.unity]
- 고리 판정은 통과 상대를 최대 8단계까지만 따라간다. 9개체 이상이 한 고리로 서로 막는 경우는 풀지 않는다 [src: Assets/Scripts/Creature/MonsterCrowdGrid.cs `MaxPassCycleLength`]
- 앞 몬스터가 갑자기 멈춘 틱에는 한 틱 이동량만큼 겹칠 수 있고, 다음 틱 겹침 풀기가 정리한다 [추론]
- 둘러싸임의 실제 강도: 몬스터는 교전 사거리(피격 반경 + 스킬 최대 사거리 + 상대 피격 반경)에서 멈춘다. 현재 22종 모두 그 거리가 1.7칸 이상이고, 몸이 닿는 거리(0.5~0.85칸)보다 길어 공격하며 플레이어와 겹치는 종은 없다. 그래서 둘러싸도 몬스터 사이 틈이 넓어 걸어서 빠져나가기 쉽다. 더 강하게 하려면 근접 공격 시작 거리를 줄여야 한다 [src: Assets/Scripts/Common/Utility/SkillHelper.cs `GetEngagementRangeSqr`, Assets/_BinaryData/TableData/MonsterTable.bytes, Assets/_BinaryData/TableData/CreatureTable.bytes, Assets/_BinaryData/TableData/AiTable.bytes, Assets/_BinaryData/TableData/SkillTable.bytes]
- 플레이어↔NPC, NPC↔NPC는 통과한다(군집 격자에 들어가는 몸은 몬스터뿐) [src: Assets/Scripts/Common/System/System_MonsterCrowdMovement.cs]
- 동행 NPC는 몬스터 줄에 막혀 뒤처질 수 있다. 리더와 15칸 넘게 떨어지면 기존 규칙대로 순간이동한다. 마을 걷기는 한 구간 40초 제한에 걸리면 걷기 실패로 처리되지만 화면 표현에만 쓰는 규칙이다 [src: Assets/Scripts/AI/StateHandlers/FollowStateHandler.cs, Assets/Scripts/Manager/NpcManager_VillageWalk.cs]
- 플레이 확인 전: 움직임이 끊기지 않는지, 실제 벽 미끄러짐·가장자리 탈출, 대기 모션, 통로 통과, 지형 대기의 길 다시 찾기, 줄지어 따라가기, 플레이어가 몬스터 몸에 막히는 느낌, NPC가 몬스터 사이를 지나갈 때·마을에 몬스터가 들어왔을 때 NPC 걷기
