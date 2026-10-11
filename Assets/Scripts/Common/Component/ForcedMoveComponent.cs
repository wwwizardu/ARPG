using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 넉백·당기기로 강제로 움직이는 중인 엔티티의 상태 (SKILL_DESIGN §6.10).
    /// ForcedMoveStarter가 붙이고 System_ForcedMove가 진행·제거한다. 붙어 있는 동안 자기 이동(Velocity)은 위치에 반영되지 않는다.
    /// 세이브에 저장하지 않는 일시 상태다.
    /// </summary>
    public struct ForcedMoveComponent
    {
        public Vector2 Direction;   // 이동 방향 (정규화). 당기기는 기준점 쪽
        public float Distance;      // 총 이동 거리. 당기기는 멈춤 거리로 이미 잘라 둔 값
        public float Duration;      // 걸리는 시간 (초)
        public float Elapsed;       // 경과 시간 (초)
        public float Traveled;      // 곡선상 지금까지 진행한 거리. 벽에 막혀도 곡선 기준으로 늘어난다
    }
}
