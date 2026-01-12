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
        Return          // 귀환
    }

    public struct AIStateComponent
    {
        public AIState CurrentState;
        public AIState PreviousState;
        public float StateEnterTime;    // 상태 진입 시간
        public Vector2 PatrolTarget;    // 순찰 목표 지점
        public Vector2 SpawnPosition;   // 스폰 위치 (귀환용)
    }
}
