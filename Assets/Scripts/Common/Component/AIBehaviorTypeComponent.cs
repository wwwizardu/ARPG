namespace ARPG.Component
{
    public enum AIBehaviorType
    {
        Melee,          // 근접 공격형
        Ranged,         // 원거리 공격형
        Patrol,         // 순찰형 (NPC 근접)
        PatrolRanged,   // 순찰형 (NPC 원거리)
        Defensive,      // 방어형
        Aggressive,     // 공격적
        Support,        // 지원형
        Stationary      // 정지형 (토템·터렛·지뢰 — 이동 없이 사거리 내 적 자동 시전)
    }

    public struct AIBehaviorTypeComponent
    {
        public AIBehaviorType BehaviorType;
        public float AggroRange;        // 적대 범위
        public float AttackRange;       // 공격 범위
        public float RetreatThreshold;  // 후퇴 체력 임계값
        public float KeepDistance;      // 유지 거리 (0=겹침 허용, >0=해당 거리 유지)
    }
}
