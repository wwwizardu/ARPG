namespace ARPG.Component
{
    /// <summary>
    /// 애니메이션 재생 요청을 저장하는 컴포넌트.
    /// AnimCategory enum 기반으로 카테고리를 지정하고,
    /// RequestedDuration > 0이면 스킬 타이밍에 맞춰 FrameDuration을 자동 계산.
    /// </summary>
    public struct AnimatorComponent
    {
        public GlobalEnum.AnimCategory RequestedCategory;  // 재생 요청된 애니메이션 카테고리
        public bool Force;                                  // 강제 재생 플래그
        public float RequestedDuration;                     // 총 재생 시간 (0이면 기본 FrameDuration 사용)
        public bool HasRequest;                             // 애니메이션 재생 요청 플래그

        /// <summary>
        /// 애니메이션 재생 요청
        /// </summary>
        /// <param name="category">애니메이션 카테고리</param>
        /// <param name="isForce">강제 재생 여부</param>
        /// <param name="duration">총 재생 시간 (0이면 기본 FrameDuration 사용, >0이면 자동 계산)</param>
        public void RequestAnimation(GlobalEnum.AnimCategory category, bool isForce = false, float duration = 0f)
        {
            RequestedCategory = category;
            Force = isForce;
            RequestedDuration = duration;
            HasRequest = true;
        }

        /// <summary>
        /// 애니메이션 재생 요청 완료 처리
        /// </summary>
        public void ClearRequest()
        {
            RequestedDuration = 0f;
            HasRequest = false;
        }
    }
}
