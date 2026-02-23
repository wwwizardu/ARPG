using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// NPC/플레이어 현재 활동 상태
    /// System_NpcSchedule에서 성격 기반으로 활동을 결정
    /// </summary>
    public struct NpcScheduleComponent
    {
        public ActivityType CurrentActivity;
        public float ActivityTimer;
        public Vector2 ActivityTarget;
        public int ActivityTargetEntityId;
    }
}
