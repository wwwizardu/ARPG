namespace ARPG.Component
{
    /// <summary>
    /// 대상에게 지금 적용 중인 이동 둔화입니다. 둔화 버프가 여러 개면 가장 강한 하나만 남깁니다.
    /// System_MoveSlow가 매 FixedUpdate 갱신하고, 둔화가 모두 끝나면 제거합니다.
    /// </summary>
    public struct MoveSlowComponent
    {
        public float SlowRate;      // 0~1 (0.4 = 이동 속도 40% 감소)
    }
}
