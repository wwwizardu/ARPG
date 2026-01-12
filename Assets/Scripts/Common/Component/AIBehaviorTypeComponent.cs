namespace ARPG.Component
{
    public enum AIBehaviorType
    {
        Melee,          // 근접 공격형
        Ranged,         // 원거리 공격형
        Patrol,         // 순찰형
        Defensive,      // 방어형
        Aggressive,     // 공격적
        Support         // 지원형
    }

    public struct AIBehaviorTypeComponent
    {
        public AIBehaviorType BehaviorType;
        public float AggroRange;        // 적대 범위
        public float AttackRange;       // 공격 범위
        public float RetreatThreshold;  // 후퇴 체력 임계값
    }
}
