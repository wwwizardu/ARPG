using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Component
{
    public enum PathfindingStatus : byte
    {
        None,       // 길찾기 비활성 — handler가 Goal 설정 안 함
        Computing,  // A* 재계산 필요 (다음 System_Pathfinding 틱에서 처리)
        Following,  // Waypoint를 따라 이동 중
        Failed,     // A* 실패 (비활성 청크 / 경로 없음 / 노드 한계 초과)
    }

    /// <summary>
    /// AI 엔티티의 길찾기 상태. AIStateHelper.SetPathfindingGoal로 Goal 설정,
    /// System_Pathfinding이 A* 실행 + Waypoint 추적 + Velocity 갱신.
    /// </summary>
    public struct PathfindingComponent
    {
        public Vector2Int Goal;              // 목표 타일 (월드 타일 좌표)
        public Vector2Int LastGoal;          // 직전 Goal (변경 감지용)
        public PathfindingStatus Status;
        public List<Vector2Int> Waypoints;   // A* 결과 경로 (start 다음 노드부터 goal까지)
        public int CurrentWaypointIndex;
        public float LastRecomputedTime;     // 마지막 A* 실행 시각

        // Stuck 감지: waypoint와의 거리가 일정 시간 동안 줄어들지 않으면 재계산 트리거
        public float LastProgressDistSqr;    // 직전 진행이 측정된 시점의 waypoint 거리²
        public float LastProgressTime;       // 직전 진행이 측정된 시각
    }
}
