namespace ARPG.Component
{
    /// <summary>
    /// HP/MP 재생 마커 + 누적기.
    /// 부착되어 있으면 System_Regen이 처리 (FinalHpGeneration > 0 || FinalMpGeneration > 0 일 때만 부착).
    /// 부착/제거는 System_StatCalculation이 동기화한다.
    /// </summary>
    public struct RegenComponent
    {
        public float HpAccumulator;
        public float MpAccumulator;
    }
}
