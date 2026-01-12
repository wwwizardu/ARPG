namespace ARPG.Component
{
    /// <summary>
    /// AI 감지/인지 컴포넌트
    /// 타겟을 감지하고 추적하는 데 필요한 범위 및 시야 정보
    /// </summary>
    public struct AIPerceptionComponent
    {
        /// <summary>
        /// 타겟을 감지할 수 있는 최대 거리
        /// </summary>
        public float DetectionRange;

        /// <summary>
        /// 공격 가능한 범위 (타겟과의 거리 체크용)
        /// </summary>
        public float AttackRange;

        /// <summary>
        /// 타겟을 완전히 잃는 범위
        /// 타겟이 이 거리보다 멀어지면 추적 중단
        /// </summary>
        public float LoseTargetRange;

        /// <summary>
        /// 시야각 (degrees)
        /// 360도면 전방향 감지, 120도면 정면 120도 내에서만 감지
        /// </summary>
        public float FieldOfView;

        /// <summary>
        /// 마지막으로 타겟을 감지한 시간 (Time.time)
        /// </summary>
        public float LastDetectionTime;
    }
}
