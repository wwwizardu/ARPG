namespace ARPG.Component
{
    /// <summary>
    /// 이동 둔화(Slow) 버프 엔티티에 붙는 효과 데이터입니다.
    /// System_MoveSlow가 같은 엔티티의 BuffInstance.RemainTime과 함께 읽어 대상의 현재 둔화율을 계산합니다.
    /// </summary>
    public struct MoveSlowBuffComponent
    {
        public float SlowRate;      // 최대 둔화율 (0.4 = 이동 속도 40% 감소)
        public float FadeOutTime;   // 남은 시간이 이 값보다 작아지면 둔화율이 0까지 선형으로 줄어든다
    }
}
