namespace ARPG.Component
{
    /// <summary>
    /// 실행 중인 스킬이 소유자의 일반 이동에 적용하는 런타임 정책입니다.
    /// </summary>
    public struct SkillMovementPenaltyComponent
    {
        public int SkillEntityId;
        public GlobalEnum.SkillMovementMode MovementMode;
        public float BaseMultiplier;
        public float CurrentMultiplier;
        public float EndRecoveryProgress;

        public void Reset()
        {
            SkillEntityId = -1;
            MovementMode = GlobalEnum.SkillMovementMode.FullSpeed;
            BaseMultiplier = 1f;
            CurrentMultiplier = 1f;
            EndRecoveryProgress = 0f;
        }
    }
}
