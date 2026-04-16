namespace ARPG.Component
{
    public enum AnimationLoadState : byte
    {
        None = 0,
        Loading = 1,
        Loaded = 2,
        Failed = 3
    }

    /// <summary>
    /// 엔티티별 애니메이션 설정 및 프레임 기반 재생 상태를 관리하는 컴포넌트.
    /// SpriteLibraryAsset 카테고리/라벨 기반으로 스프라이트를 직접 제어.
    /// </summary>
    public struct SpriteAnimationComponent
    {
        public int AnimationTableId;
        public AnimationLoadState LoadState;
        public float PlaybackSpeed;

        public GlobalEnum.AnimCategory CurrentCategory;  // 현재 애니메이션 카테고리
        public int CurrentFrame;                          // 현재 프레임 인덱스 (0부터)
        public float FrameTimer;                          // 현재 프레임 경과 시간
        public float FrameDuration;                       // 프레임당 시간 (초)
        public bool IsLooping;                            // 루프 여부
        public bool IsPlaying;                            // 재생 중 여부
    }
}
