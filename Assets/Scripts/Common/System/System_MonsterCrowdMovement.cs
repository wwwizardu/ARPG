using System;
using ARPG.Component;
using ARPG.Monster;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 일반 몬스터가 서로 막고 비켜 가게 이동을 조정하고, 플레이어·NPC 이동은 몬스터 몸에 막히게 한다(몬스터는 둘을 모른다).
    /// 고정 틱마다 점유·우회·대기·겹침 보정을 계산해
    /// 이번 틱에 쓸 속도를 ResolvedVelocityComponent로 내고, 실제 위치는 System_Render가 그 속도로 옮긴다.
    /// 꺼짐/제외/맡을 수 없는 틱에는 Active를 꺼 기본 이동으로 돌려준다. 희망 속도(VelocityComponent)와 A* 목표는 바꾸지 않는다.
    /// </summary>
    public sealed class System_MonsterCrowdMovement : IFixedUpdateSystem
    {
        // AI(50)·길찾기(80)·이동(100)·스킬(200, 제자리 채널 시작 시 속도 0)·HP 확인(250)이 이번 틱 희망 속도와 생존을 정한 뒤
        public int Priority => 270;
        public float UpdateInterval => 0f;

        public void OnFixedUpdate(float dt)
        {
            ComponentManager cm = AR.s.Component;
            MonsterManager manager = AR.s.Monster;
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
            if (manager == null || AR.s.IsInitialized == false
                || cm.TryGetComponent<TransformComponent>(playerEntityId, out var player) == false)
            {
                // 맡을 수 없는 틱에는 확정 속도를 꺼서 지난 틱 속도로 계속 움직이지 않게 한다.
                ReleaseMotion(cm, playerEntityId);
                return;
            }
            // 처리 순서 돌리기·경로 앞보기 간격의 기준. 같은 고정 틱이면 같은 값이다.
            int tick = Mathf.RoundToInt(Time.fixedTime / Time.fixedDeltaTime);
            Simulate(cm, manager.CrowdSettings, manager.CrowdGrid, playerEntityId, player.Position, dt, Time.time,
                tick, manager.CrowdTerrainSweep, manager.CrowdActivePredicate);
        }

        // 상태 없는 시스템이라 정리할 것이 없다. 격자는 MonsterManager.Reset이, 군집 컴포넌트는 ComponentManager.Reset이 비운다.
        public void OnReset() { }

        /// <summary>
        /// 고정 틱 한 번의 계산. 위치(TransformComponent)는 바꾸지 않고 ResolvedVelocityComponent만 쓴다.
        /// 동일 실행 경로를 Editor 진단에서도 사용한다. 전달 delegate는 초기화 때 캐시한다.
        /// </summary>
        public static void Simulate(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            int playerEntityId, Vector2 center, float dt, float now, int tick, MonsterTerrainSweep terrain,
            Func<int, bool> isActive)
        {
            if (settings.Enabled == false || dt <= 0f)
            {
                grid.Clear();
                ReleaseMotion(cm, playerEntityId);
                return;
            }
            grid.ApplySettings(settings.GridWidth, settings.CellSize);
            grid.Begin(center);
            SparseSet<MonsterCrowdComponent> crowdPool = cm.GetComponentPool<MonsterCrowdComponent>();
            SparseSet<ResolvedVelocityComponent> resolvedPool = cm.GetComponentPool<ResolvedVelocityComponent>();
            for (int i = 0; i < crowdPool.Count; i++)
            {
                int entityId = crowdPool.GetEntityId(i);
                MonsterCrowdComponent state = crowdPool.GetByIndex(i);
                // ResolvedVelocityComponent가 없으면 계산 결과를 Render에 넘길 수 없으므로 참여시키지 않는다.
                if (cm.HasComponent<MonsterTag>(entityId) == false
                    || resolvedPool.TryGet(entityId, out var resolved) == false
                    || cm.HasComponent<MonsterCrowdExcludedTag>(entityId) == true
                    || cm.HasComponent<DestroyTag>(entityId) == true
                    || settings.Includes(state.MonsterTableId) == false
                    || cm.TryGetComponent<StatComponent>(entityId, out var stat) == false || stat.CurrentHp <= 0
                    || cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false
                    || cm.TryGetComponent<ColliderComponent>(entityId, out var collider) == false || collider.MoveRadius <= 0f
                    || cm.HasComponent<JumpComponent>(entityId) == true
                    || cm.HasComponent<DashComponent>(entityId) == true
                    || cm.HasComponent<ForcedMoveComponent>(entityId) == true
                    || (cm.TryGetComponent<ActivationDistanceComponent>(entityId, out var activation) == true
                        && activation.IsActivated == false)
                    || isActive(entityId) == false)
                {
                    state.IsWaiting = false;
                    state.WaitBlockerId = 0;
                    state.RetryAt = 0f;
                    state.AvoidSide = 0;
                    state.PassPartnerId = 0;
                    state.PassUntil = 0f;
                    state.ActualVelocity = Vector2.zero;
                    crowdPool.SetByIndex(i, state);
                    Release(resolvedPool, entityId);
                    continue;
                }
                bool canDisplace = stat.FinalMoveSpeedValue > 0f
                    && (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false
                        || behavior.BehaviorType != AIBehaviorType.Stationary);
                float weight = cm.TryGetComponent<VelocityComponent>(entityId, out var intended) == true
                    && intended.Velocity.sqrMagnitude > 0.000001f ? 1f : 0.2f;
                int passPartnerId = state.PassUntil > now ? state.PassPartnerId : 0;
                // 바로 앞 틱에 군집 이동이 맡았던(Active) 개체의 속도만 예측에 쓴다 (제외·비활성 뒤 복귀한 값은 오래됐다).
                Vector2 predictedVelocity = resolved.Active == true ? state.ActualVelocity : Vector2.zero;
                grid.Add(entityId, transform.Position, collider.MoveRadius, canDisplace, weight, passPartnerId,
                    predictedVelocity);
            }

            // 통과 상대 고리(서로 막은 둘, 셋 이상의 고리 교착)를 찾는다. 겹침 풀기와 이동 검사가 모두 이 판정을 쓴다.
            grid.ResolvePassCycles();

            if (settings.OverlapRecoveryEnabled == true)
            {
                float stepLimit = settings.RecoverySpeed * dt / settings.RecoveryIterations;
                for (int iteration = 0; iteration < settings.RecoveryIterations; iteration++)
                {
                    for (int i = 0; i < grid.Count; i++)
                    {
                        Vector2 correction = MonsterCrowdMotion.RecoverOverlap(grid, i, stepLimit, terrain);
                        grid.SetPosition(i, grid.GetBody(i).Position + correction);
                    }
                }
            }

            // 시작 인덱스를 순환해 SparseSet 앞쪽 개체만 계속 진행권을 얻지 않게 한다.
            int first = grid.Count > 0 ? (tick & int.MaxValue) % grid.Count : 0;
            for (int offset = 0; offset < grid.Count; offset++)
            {
                int index = (first + offset) % grid.Count;
                MonsterCrowdGrid.Body body = grid.GetBody(index);
                if (cm.TryGetComponent<TransformComponent>(body.EntityId, out var transform) == false
                    || crowdPool.TryGet(body.EntityId, out var state) == false)
                    continue;
                Vector2 desiredVelocity = Vector2.zero;
                if (body.CanDisplace == true
                    && cm.TryGetComponent<VelocityComponent>(body.EntityId, out var velocity) == true)
                    desiredVelocity = velocity.Velocity;
                if (body.CanDisplace == true && desiredVelocity.sqrMagnitude > 0.000001f)
                {
                    desiredVelocity = ResolvePathHeading(cm, body.EntityId, body.Position,
                        body.Radius, desiredVelocity, now, tick, settings.PathLookAheadTicks, terrain);
                }
                Vector2 delta = MonsterCrowdMotion.ResolveMove(grid, index, desiredVelocity, dt,
                    now, settings, ref state, terrain);
                Vector2 position = body.Position + delta;
                grid.CommitMove(index, position);
                // 겹침 보정 밀림은 빼고 스스로 이동한 양만 기록한다. 다음 틱 이웃이 이 개체의 이동을 예측하는 데 쓴다.
                state.ActualVelocity = delta / dt;
                crowdPool.Set(body.EntityId, state);
                // 겹침 보정과 이동을 합친 이번 틱 변위를 속도로 넘긴다. 위치는 System_Render가 이 속도로 옮긴다.
                resolvedPool.Set(body.EntityId, new ResolvedVelocityComponent
                    { Velocity = (position - transform.Position) / dt, Active = true });
                if (state.IsWaiting == true && state.WaitBlockerId != 0)
                    DeferPathStuck(cm, body.EntityId, now);
                // 걷기/서기(MoveState)는 System_Move가 희망 속도로 정한다. 여기서 실제 이동량으로 다시 쓰면
                // 막힌 개체가 고정 틱마다 Walking↔Idle로 뒤집혀 애니메이션이 첫 프레임으로 되돌아간다.
            }

            // 몬스터 이동을 모두 마친 위치에 대해 플레이어·NPC 이동만 막는다. 몬스터 계산은 둘을 모르므로
            // 몬스터는 플레이어·NPC 때문에 비키거나 밀리지 않고, 공격 사거리에서 멈추는 것은 AI가 정한다.
            if (settings.PlayerBlockedByMonsters == true)
                ResolveBlockedByMonsters(cm, grid, resolvedPool, playerEntityId, dt, now, settings.Clearance);
            else
                Release(resolvedPool, playerEntityId);
            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            for (int i = 0; i < npcPool.Count; i++)
            {
                int npcEntityId = npcPool.GetEntityId(i);
                if (settings.NpcBlockedByMonsters == true && cm.HasComponent<DestroyTag>(npcEntityId) == false
                    && isActive(npcEntityId) == true)
                    ResolveBlockedByMonsters(cm, grid, resolvedPool, npcEntityId, dt, now, settings.Clearance);
                else
                    Release(resolvedPool, npcEntityId);
            }
        }

        /// <summary>
        /// 플레이어·NPC의 이번 틱 희망 이동을 몬스터 몸에 대해 잘라 확정 속도로 넘긴다. 정면이면 멈추고 비스듬하면 몸을 따라 미끄러진다.
        /// 몬스터에 막힌 동안은 길찾기 stuck 재계산을 미룬다(길찾기는 몬스터를 모르므로 다시 찾아도 같은 길이다).
        /// 넉백·돌진·점프 중이거나 필요한 컴포넌트가 없으면 기본 이동으로 돌려준다.
        /// </summary>
        private static void ResolveBlockedByMonsters(ComponentManager cm, MonsterCrowdGrid grid,
            SparseSet<ResolvedVelocityComponent> resolvedPool, int entityId, float dt, float now, float padding)
        {
            if (resolvedPool.Contains(entityId) == false)
                return;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false
                || cm.TryGetComponent<ColliderComponent>(entityId, out var collider) == false || collider.MoveRadius <= 0f
                || cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false
                || cm.HasComponent<JumpComponent>(entityId) == true
                || cm.HasComponent<DashComponent>(entityId) == true
                || cm.HasComponent<ForcedMoveComponent>(entityId) == true)
            {
                Release(resolvedPool, entityId);
                return;
            }
            Vector2 desired = velocity.Velocity * dt;
            Vector2 allowed = grid.ClipExternal(transform.Position, collider.MoveRadius, desired, padding);
            resolvedPool.Set(entityId, new ResolvedVelocityComponent { Velocity = allowed / dt, Active = true });
            if (allowed.sqrMagnitude < desired.sqrMagnitude * 0.99f)
                DeferPathStuck(cm, entityId, now);
        }

        /// <summary>맡던 몬스터·플레이어·NPC의 확정 속도를 모두 꺼서 Render가 희망 속도로 기본 이동하게 한다.</summary>
        private static void ReleaseMotion(ComponentManager cm, int playerEntityId)
        {
            SparseSet<MonsterCrowdComponent> crowdPool = cm.GetComponentPool<MonsterCrowdComponent>();
            SparseSet<ResolvedVelocityComponent> resolvedPool = cm.GetComponentPool<ResolvedVelocityComponent>();
            for (int i = 0; i < crowdPool.Count; i++)
                Release(resolvedPool, crowdPool.GetEntityId(i));
            Release(resolvedPool, playerEntityId);
            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            for (int i = 0; i < npcPool.Count; i++)
                Release(resolvedPool, npcPool.GetEntityId(i));
        }

        private static void Release(SparseSet<ResolvedVelocityComponent> resolvedPool, int entityId)
        {
            if (resolvedPool.TryGet(entityId, out var resolved) == true && resolved.Active == true)
                resolvedPool.Set(entityId, new ResolvedVelocityComponent());
        }

        /// <summary>
        /// 몬스터에 막힌 대기는 경로 실패가 아니므로 A*의 stuck 재계산(진행 없이 2초)을 미룬다.
        /// 막은 몬스터가 없는 대기(정면이 지형에 막힘)는 미루지 않아 System_Pathfinding이 길을 다시 찾게 둔다.
        /// </summary>
        private static void DeferPathStuck(ComponentManager cm, int entityId, float now)
        {
            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var path) == false
                || path.Status != PathfindingStatus.Following)
                return;
            path.LastProgressTime = now;
            cm.SetComponent(entityId, path);
        }

        private static Vector2 ResolvePathHeading(ComponentManager cm, int entityId, Vector2 position,
            float radius, Vector2 desiredVelocity, float now, int tick, int lookAheadTicks,
            MonsterTerrainSweep terrain)
        {
            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var path) == false
                || path.Status != PathfindingStatus.Following || path.Waypoints == null
                || path.CurrentWaypointIndex >= path.Waypoints.Count)
                return desiredVelocity;
            int current = path.CurrentWaypointIndex;
            // 지금 향하는 경유점은 직선 확인 없이 그쪽으로 간다. 막혀 있어도 A* 방향이 같은 경유점을 가리키므로
            // 거리에 비례하는 검사(넉백으로 멀어지면 수십 번)를 할 이유가 없다.
            Vector2 destination = (Vector2)path.Waypoints[current] + Vector2.one * 0.5f;
            // 앞 경유점 건너뛰기 확인은 몇 틱에 한 번, 개체마다 엇갈려서 한다.
            // 고른 경유점은 CurrentWaypointIndex로 남으므로 사이 틱에도 그 방향으로 간다.
            if (((tick + entityId) & int.MaxValue) % lookAheadTicks == 0)
            {
                int end = Mathf.Min(path.Waypoints.Count - 1, current + 3);
                for (int i = end; i > current; i--)
                {
                    Vector2 target = (Vector2)path.Waypoints[i] + Vector2.one * 0.5f;
                    // 검사 비용이 거리에 비례하므로 2칸 밖의 경유점은 확인하지 않는다.
                    if ((target - position).sqrMagnitude > 4f)
                        continue;
                    Vector2 reached = terrain(position, target - position, radius);
                    if ((reached - target).sqrMagnitude > 0.0001f)
                        continue;
                    destination = target;
                    path.CurrentWaypointIndex = i;
                    path.LastProgressDistSqr = float.MaxValue;
                    path.LastProgressTime = now;
                    cm.SetComponent(entityId, path);
                    break;
                }
            }
            Vector2 direction = destination - position;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized * desiredVelocity.magnitude : desiredVelocity;
        }
    }
}
