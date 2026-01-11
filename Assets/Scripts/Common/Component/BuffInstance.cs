namespace ARPG.Component
{
    /// <summary>
    /// 버프 인스턴스 데이터
    /// 각 버프 Entity에 붙어있는 기본 정보
    /// </summary>
    public struct BuffInstance
    {
        public int TargetEntityId;      // 이 버프를 받는 엔티티
        public int BuffTableID;         // 버프 테이블 ID
        public float Duration;          // 전체 지속시간
        public float RemainTime;        // 남은 시간

        public BuffInstance(int targetEntityId, int buffTableID, float duration)
        {
            TargetEntityId = targetEntityId;
            BuffTableID = buffTableID;
            Duration = duration;
            RemainTime = duration;
        }
    }
}
