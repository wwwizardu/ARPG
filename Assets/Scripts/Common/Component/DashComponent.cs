using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 땅 위 직선 돌진 중인 엔티티의 상태 (SKILL_DESIGN §6.11).
    /// System_Skill이 돌진 출발 때 붙이고 System_Dash가 진행한다. 끝나면 IsFinished만 표시하고,
    /// 마지막 경로 판정을 마친 System_Skill이 제거한다. 붙어 있는 동안 자기 이동(Velocity)은 위치에 반영되지 않는다.
    /// 세이브에 저장하지 않는 일시 상태다.
    /// </summary>
    public struct DashComponent
    {
        public int SkillEntityId;   // 돌진을 시작한 스킬 엔티티 ID
        public Vector2 Direction;   // 돌진 방향 (정규화). 출발 때 고정
        public float Distance;      // 총 돌진 거리 (칸)
        public float Duration;      // 걸리는 시간 (초)
        public float Elapsed;       // 경과 시간 (초)
        public float Traveled;      // 지금까지 실제로 이동한 거리
        public bool IsFinished;     // 거리를 다 갔거나 벽에 닿거나 밀려서 끝남
    }
}
