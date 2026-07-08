using UnityEngine;

namespace ARPG.Component
{
    public enum AIState
    {
        Idle,           // 대기
        Patrol,         // 순찰
        Chase,          // 추격
        Attack,         // 공격
        Retreat,        // 후퇴
        Flee,           // 도주 (NPC 위협 대응)
        Return          // 귀환
    }

    public struct AIStateComponent
    {
        public AIState CurrentState;
        public AIState PreviousState;
        public float StateEnterTime;    // 상태 진입 시간
        public float PatrolArrivalTime; // 순찰 목적지 도착 시간 (대기 타이머용)
        public Vector2 PatrolTarget;    // 순찰 목표 지점
        public Vector2 SpawnPosition;   // 스폰 위치 (귀환용)
    }
}
