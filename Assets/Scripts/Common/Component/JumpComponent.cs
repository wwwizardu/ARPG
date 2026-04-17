using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 점프 중인 엔티티의 높이 상태를 저장하는 컴포넌트
    /// 점프 스킬 시작 시 추가되고, 착지 시 제거됨
    /// </summary>
    public struct JumpComponent
    {
        public float Height;             // 현재 높이 (0 = 지면)
        public float Elapsed;            // 점프 시작 후 경과 시간
        public float Duration;           // 총 체공 시간
        public float MaxHeight;          // 최대 높이 (SkillTable.ArcHeight 값)
        public Vector2 StartPosition;    // 점프 시작 위치 (지면 기준 XY)
        public Vector2 EndPosition;      // 착지 예정 위치 (지면 기준 XY)
    }
}
