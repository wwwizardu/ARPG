using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// System_Jump가 실제 착지 위치를 확정한 뒤 System_Skill에 전달하는 일회성 커밋 신호입니다.
    /// </summary>
    public struct SkillLandingCommitComponent
    {
        public Vector2 LandingPosition;
    }
}
