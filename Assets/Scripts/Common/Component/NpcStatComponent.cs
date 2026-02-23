namespace ARPG.Component
{
    /// <summary>
    /// NPC 고유 성향 스탯
    /// NPC의 성격과 행동 패턴을 결정하는 고정 스탯
    /// </summary>
    public struct NpcStatComponent
    {
        public int Friendliness;    // 친화력 (높을수록 플레이어에게 호의적)
        public int Honesty;         // 정직함 (높을수록 거짓말을 하지 않음)
        public int Greed;           // 탐욕 (높을수록 이익을 추구)
        public int Loyalty;         // 충성심 (높을수록 배신하지 않음)
        public int Courage;         // 용기 (높을수록 위험에 맞섬)
        public int Curiosity;       // 호기심 (높을수록 새로운 것에 관심)
        public int Pride;           // 자존심 (높을수록 모욕에 강한 반응)
        public int Patience;        // 인내심 (높을수록 참을성 있음)
    }
}
