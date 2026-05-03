namespace ARPG.Component
{
    /// <summary>
    /// 자연 만료 엔티티용 컴포넌트. Remaining이 0 이하가 되면 System_Lifetime이 DestroyTag를 부착해 정리.
    /// 토템·지뢰·함정·지속형 장판·발사체 시각 효과 등에 공용으로 사용 가능.
    /// </summary>
    public struct LifetimeComponent
    {
        public float Remaining;     // 남은 수명 (초)
    }
}
