using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 비행 중 방향을 바꾸는 직선 발사체의 상태 (SKILL_DESIGN §6.4, 예: 전기 불꽃).
    /// ProjectileHelper가 ProjectileTable.HasSteering인 발사체에 붙이고, System_ProjectileSteering이
    /// 속도의 방향만 바꾼다. 위치는 다른 발사체처럼 System_Render가 옮긴다.
    /// </summary>
    public struct ProjectileSteeringComponent
    {
        public Vector2 Heading;         // 지그재그의 기준 방향 (정규화). 발사 방향에서 시작해 WanderDrift만큼씩 돌아가고, 벽에 튕기면 같이 뒤집힌다
        public float WanderInterval;    // 방향을 다시 고르는 간격 (초). 0이면 흔들리지 않음
        public float WanderAngle;       // 기준 방향에서 꺾는 최대 각도 (도)
        public float WanderDrift;       // 방향을 고를 때마다 기준 방향 자체가 돌아가는 최대 각도 (도). 0이면 곧게 나아감
        public float WanderTimer;       // 다음 방향 선택까지 남은 시간 (초)
        public bool BounceOnWall;       // 벽에 닿으면 튕기는지
        public Vector2 LastPosition;    // 지난 틱 위치. 프레임이 끊겨 한 번에 벽 안으로 들어갔을 때 들어온 축을 찾는다
    }
}
