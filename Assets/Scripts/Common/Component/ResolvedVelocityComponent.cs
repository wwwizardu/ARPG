using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 이동을 조정하는 시스템(예: System_MonsterCrowdMovement)이 이번 고정 틱에 확정한 이동 속도.
    /// Active면 System_Render가 희망 속도(VelocityComponent) 대신 이 속도로 위치를 옮긴다. 희망 속도는 바꾸지 않는다.
    /// 조정 시스템이 더 이상 맡지 않으면 Active를 꺼 기본 이동으로 돌려준다.
    /// </summary>
    public struct ResolvedVelocityComponent
    {
        public Vector2 Velocity;
        public bool Active;
    }
}
