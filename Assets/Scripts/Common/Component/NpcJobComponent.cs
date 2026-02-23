namespace ARPG.Component
{
    /// <summary>
    /// NPC/플레이어 직업 정보
    /// </summary>
    public struct NpcJobComponent
    {
        public GlobalEnum.JobType JobType;
        public int SkillLevel;          // 직업 숙련도 (0~100)
        public int PersonalGoalType;    // 개인 목표 ID (나중에 정의)
    }
}
