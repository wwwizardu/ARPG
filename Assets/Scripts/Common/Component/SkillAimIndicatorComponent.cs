namespace ARPG.Component
{
    /// <summary>
    /// 플레이어 전용 조준 방향 View의 런타임 표시 상태입니다.
    /// </summary>
    public struct SkillAimIndicatorComponent
    {
        public int ActiveSkillEntityId;
        public bool IsVisible;
        public float LastAppliedAngle;

        public void Reset()
        {
            ActiveSkillEntityId = -1;
            IsVisible = false;
            LastAppliedAngle = 0f;
        }
    }
}
