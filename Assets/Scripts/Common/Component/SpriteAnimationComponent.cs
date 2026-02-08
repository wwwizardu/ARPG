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
    /// 엔티티별 애니메이션 설정 및 로딩 상태를 관리하는 컴포넌트.
    /// AnimationTable 데이터를 기반으로 런타임에 애니메이션을 로드합니다.
    /// </summary>
    public struct SpriteAnimationComponent
    {
        public int AnimationTableId;
        public AnimationLoadState LoadState;
        public float PlaybackSpeed;
    }
}
