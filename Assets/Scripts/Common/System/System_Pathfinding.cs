using System.Collections.Generic;
using ARPG.AI;
using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 길찾기 시스템 — PathfindingComponent를 가진 엔티티의 A* 경로 계산 + Waypoint 추적.
    /// System_AI_Behavior(50) 이후, System_Move(100) 이전에 실행되어 Velocity.Direction을 Waypoint 방향으로 덮어씀.
    /// </summary>
    public class System_Pathfinding : IFixedUpdateSystem
    {
        public int Priority => 80;
        public float UpdateInterval => 0f;

        // 한 틱당 A* 호출 최대 수 (성능 보호)
        private const int MAX_RECOMPUTE_PER_TICK = 2;

        // Waypoint 도착 거리² (0.4 unit)
        private const float WAYPOINT_ARRIVAL_DIST_SQR = 0.16f;

        // Stuck 감지 — 이 시간 동안 PROGRESS_EPSILON_SQR 이상 가까워지지 않으면 재계산
        private const float STUCK_TIMEOUT = 2.0f;
        private const float PROGRESS_EPSILON_SQR = 0.0025f;  // 0.05 unit²

        public void OnCreate()
        {
            Debug.Log("System_Pathfinding Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Pathfinding Reset");
        }

        public void OnFixedUpdate(float dt)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<PathfindingComponent> pool = cm.GetComponentPool<PathfindingComponent>();

            int recomputed = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                PathfindingComponent pf = pool.GetByIndex(i);

                if (pf.Status == PathfindingStatus.None) continue;
                if (pf.Status == PathfindingStatus.Failed) continue; // handler가 새 Goal 설정해야 재시도

                bool pfDirty = false;

                // 1) 재계산 단계
                if (pf.Status == PathfindingStatus.Computing)
                {
                    if (recomputed >= MAX_RECOMPUTE_PER_TICK)
                        continue; // 다음 틱에서 재시도

                    if (cm.TryGetComponent<TransformComponent>(entityId, out var startTransform) == false)
                        continue;

                    Vector2Int startTile = new Vector2Int(
                        Mathf.FloorToInt(startTransform.Position.x),
                        Mathf.FloorToInt(startTransform.Position.y));

                    if (pf.Waypoints == null) pf.Waypoints = new List<Vector2Int>();

                    bool found = Pathfinder.TryFindPath(startTile, pf.Goal, pf.Waypoints);
                    pf.LastRecomputedTime = Time.time;
                    pf.CurrentWaypointIndex = 0;
                    pf.Status = (found == true) ? PathfindingStatus.Following : PathfindingStatus.Failed;
                    // Progress tracking 초기화 — 재계산 직후 stuck 타이머 리셋
                    pf.LastProgressDistSqr = float.MaxValue;
                    pf.LastProgressTime = Time.time;
                    pfDirty = true;
                    recomputed++;

                    if (found == false)
                    {
                        cm.SetComponent(entityId, pf);
                        continue;
                    }
                }

                // 2) Waypoint 추적 단계
                if (pf.Status == PathfindingStatus.Following)
                {
                    if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    {
                        if (pfDirty == true) cm.SetComponent(entityId, pf);
                        continue;
                    }

                    // 경로가 비어있으면 (start == goal) 종료
                    if (pf.Waypoints == null || pf.Waypoints.Count == 0)
                    {
                        pf.Status = PathfindingStatus.None;
                        cm.SetComponent(entityId, pf);
                        continue;
                    }

                    // 도착한 waypoint는 건너뛰며 다음 진행 가능한 waypoint 찾기
                    while (pf.CurrentWaypointIndex < pf.Waypoints.Count)
                    {
                        Vector2Int wpTile = pf.Waypoints[pf.CurrentWaypointIndex];
                        Vector2 wpCenter = new Vector2(wpTile.x + 0.5f, wpTile.y + 0.5f);
                        Vector2 toWp = wpCenter - transform.Position;
                        float distSqr = toWp.sqrMagnitude;

                        if (distSqr > WAYPOINT_ARRIVAL_DIST_SQR)
                        {
                            // Stuck 감지 — 진행이 PROGRESS_EPSILON_SQR 이상 있으면 타이머 리셋
                            if (distSqr < pf.LastProgressDistSqr - PROGRESS_EPSILON_SQR)
                            {
                                pf.LastProgressDistSqr = distSqr;
                                pf.LastProgressTime = Time.time;
                                pfDirty = true;
                            }
                            else if (Time.time - pf.LastProgressTime >= STUCK_TIMEOUT)
                            {
                                // STUCK_TIMEOUT 동안 진행 없음 → 재계산 트리거
                                Debug.LogWarning($"[System_Pathfinding] Entity {entityId} stuck at waypoint {wpTile}, recomputing");
                                pf.Status = PathfindingStatus.Computing;
                                pfDirty = true;
                                break;
                            }

                            // 미도착 — 이 waypoint 방향으로 이동
                            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == true)
                            {
                                velocity.Direction = toWp.normalized;
                                cm.SetComponent(entityId, velocity);
                            }
                            break;
                        }

                        // 도착 — 다음 waypoint, progress tracking 리셋
                        pf.CurrentWaypointIndex++;
                        pf.LastProgressDistSqr = float.MaxValue;
                        pf.LastProgressTime = Time.time;
                        pfDirty = true;
                    }

                    // 모든 waypoint 도착 — 경로 종료
                    if (pf.CurrentWaypointIndex >= pf.Waypoints.Count)
                    {
                        pf.Status = PathfindingStatus.None;
                        pfDirty = true;
                    }
                }

                if (pfDirty == true)
                    cm.SetComponent(entityId, pf);
            }
        }
    }
}
