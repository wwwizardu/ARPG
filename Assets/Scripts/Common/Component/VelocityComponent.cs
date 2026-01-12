using UnityEngine;

namespace ARPG.Component
{
    // 속도 데이터를 저장하는 컴포넌트
    public struct VelocityComponent
    {
        public Vector2 Direction;  // 이동 방향 (normalized)
        public float Speed;         // 기본 이동 속도
        public float SprintMultiplier; // 스프린트 배율

        /// <summary>
        /// 최종 속도 벡터 계산 (Direction * Speed)
        /// </summary>
        public readonly Vector2 Velocity => Direction * Speed;
    }
}
