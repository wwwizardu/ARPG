namespace ARPG.Component
{
    /// <summary>
    /// 애니메이션 재생 요청을 저장하는 컴포넌트
    /// Animator 참조는 System_Animation의 Dictionary에서 관리
    /// </summary>
    public struct AnimatorComponent
    {
        public int RequestedAnimationHash;  // 재생 요청된 애니메이션 해시값 (Animator.StringToHash)
        public bool Force;                  // 강제 재생 플래그
        public float BlendTime;             // 블렌딩 시간 (0이면 즉시 전환)
        public bool HasRequest;             // 애니메이션 재생 요청 플래그

        /// <summary>
        /// 애니메이션 재생 요청
        /// </summary>
        public void RequestAnimation(int animationHash, bool isForce = false, float blendTime = 0f)
        {
            RequestedAnimationHash = animationHash;
            Force = isForce;
            BlendTime = blendTime;
            HasRequest = true;
        }

        /// <summary>
        /// 애니메이션 재생 요청 완료 처리
        /// </summary>
        public void ClearRequest()
        {
            RequestedAnimationHash = 0;
            BlendTime = 0f;
            HasRequest = false;
        }
    }
}
