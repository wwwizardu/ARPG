using System;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Monster;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 일반 몬스터 이동의 선택적 소유자. 처리한 프레임만 Render의 기본 적분을 건너뛴다.
    /// 비활성화/제외/시스템 미등록 시 기본 이동으로 복귀한다. 희망 속도와 A* 목표는 보존한다.
    /// </summary>
    public sealed class System_MonsterCrowdMovement : IUpdateSystem
    {
        public int Priority => 450;
        public float UpdateInterval => 0f;

        public void OnUpdate(float dt)
        {
            ComponentManager cm = AR.s.Component;
            MonsterManager manager = AR.s.Monster;
            if (manager == null || AR.s.IsInitialized == false
                || cm.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId, out var player) == false)
                return;
            Simulate(cm, manager.CrowdSettings, manager.CrowdGrid, player.Position, dt, Time.time,
                Time.frameCount, manager.CrowdTerrainSweep, manager.CrowdActivePredicate);
        }

        public void OnReset()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<MonsterCrowdComponent> pool = cm.GetComponentPool<MonsterCrowdComponent>();
            for (int i = 0; i < pool.Count; i++)
            {
                MonsterCrowdComponent state = pool.GetByIndex(i);
                pool.SetByIndex(i, new MonsterCrowdComponent { MonsterTableId = state.MonsterTableId });
            }
            if (AR.s.Monster != null)
                AR.s.Monster.CrowdGrid.Reset();
        }

        /// <summary>동일 실행 경로를 Editor 진단에서도 사용한다. 전달 delegate는 초기화 때 캐시한다.</summary>
        public static void Simulate(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            Vector2 center, float dt, float now, int frame, MonsterTerrainSweep terrain, Func<int, bool> isActive)
        {
            if (settings.Enabled == false || dt <= 0f)
            {
                grid.Reset();
                return;
            }
            grid.Initialize(settings.GridWidth, settings.CellSize);
            grid.Begin(center);
            SparseSet<MonsterCrowdComponent> crowdPool = cm.GetComponentPool<MonsterCrowdComponent>();
            for (int i = 0; i < crowdPool.Count; i++)
            {
                int entityId = crowdPool.GetEntityId(i);
                MonsterCrowdComponent state = crowdPool.GetByIndex(i);
                if (cm.HasComponent<MonsterTag>(entityId) == false
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
                    state.RetryAt = 0f;
                    state.AvoidSide = 0;
                    state.ActualVelocity = Vector2.zero;
                    crowdPool.SetByIndex(i, state);
                    continue;
                }
                bool canDisplace = stat.FinalMoveSpeedValue > 0f
                    && (cm.TryGetComponent<AIBehaviorTypeComponent>(entityId, out var behavior) == false
                        || behavior.BehaviorType != AIBehaviorType.Stationary);
                float weight = cm.TryGetComponent<VelocityComponent>(entityId, out var intended) == true
                    && intended.Velocity.sqrMagnitude > 0.000001f ? 1f : 0.2f;
                grid.Add(entityId, transform.Position, collider.MoveRadius, canDisplace, weight);
            }

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
            int first = grid.Count > 0 ? (frame & int.MaxValue) % grid.Count : 0;
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
                        body.Radius, desiredVelocity, now, terrain);
                }
                Vector2 delta = MonsterCrowdMotion.ResolveMove(grid, index, desiredVelocity, dt,
                    now, settings, ref state, terrain);
                Vector2 position = body.Position + delta;
                grid.SetPosition(index, position);
                state.ActualVelocity = (position - transform.Position) / dt;
                state.HasIntegrated = true;
                state.IntegratedFrame = frame;
                transform.Position = position;
                cm.SetComponent(body.EntityId, transform);
                crowdPool.Set(body.EntityId, state);

                if (cm.TryGetComponent<StateComponent>(body.EntityId, out var movement) == true)
                {
                    MovementStates next = delta.sqrMagnitude > 0.00000001f ? MovementStates.Walking : MovementStates.Idle;
                    if (movement.MoveState != next)
                    {
                        movement.MovementStatePrev = movement.MoveState;
                        movement.MoveState = next;
                        cm.SetComponent(body.EntityId, movement);
                    }
                }
            }
        }

        private static Vector2 ResolvePathHeading(ComponentManager cm, int entityId, Vector2 position,
            float radius, Vector2 desiredVelocity, float now, MonsterTerrainSweep terrain)
        {
            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var path) == false
                || path.Status != PathfindingStatus.Following || path.Waypoints == null
                || path.CurrentWaypointIndex >= path.Waypoints.Count)
                return desiredVelocity;
            int selected = path.CurrentWaypointIndex;
            int end = Mathf.Min(path.Waypoints.Count - 1, selected + 3);
            Vector2 destination = position;
            for (int i = end; i >= selected; i--)
            {
                Vector2 target = (Vector2)path.Waypoints[i] + Vector2.one * 0.5f;
                // 미래 waypoint를 매 프레임 넘겨 장거리 전체를 반복 검사하지 않도록 실제 거리도 제한한다.
                if (i > path.CurrentWaypointIndex && (target - position).sqrMagnitude > 4f)
                    continue;
                Vector2 reached = terrain(position, target - position, radius);
                if ((reached - target).sqrMagnitude > 0.0001f)
                    continue;
                destination = target;
                selected = i;
                break;
            }
            if (selected != path.CurrentWaypointIndex)
            {
                path.CurrentWaypointIndex = selected;
                path.LastProgressDistSqr = float.MaxValue;
                path.LastProgressTime = now;
                cm.SetComponent(entityId, path);
            }
            Vector2 direction = destination - position;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized * desiredVelocity.magnitude : desiredVelocity;
        }
    }
}
