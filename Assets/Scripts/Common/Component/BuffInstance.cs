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
        public int StackCount;          // 중복 카운트 (같은 버프가 여러 번 적용되면 증가)

        // 틱 데이터 (DoT/HoT용) - 테이블에서 복사하여 보유
        public float TickInterval;      // 틱 간격 (초), 0이면 틱 없음
        public int TickDamage;          // 틱당 기본 데미지 (음수면 힐)
        public GlobalEnum.DamageType DamageType; // 데미지 타입 (속성 저항 적용)
        public float LastTickTime;      // 마지막 틱 처리 시간 (RemainTime 기준)

        public BuffInstance(int targetEntityId, int buffTableID, float duration)
        {
            TargetEntityId = targetEntityId;
            BuffTableID = buffTableID;
            Duration = duration;
            RemainTime = duration;
            StackCount = 1;

            // 틱 데이터 초기화 (기본값)
            TickInterval = 0f;
            TickDamage = 0;
            DamageType = GlobalEnum.DamageType.Physics;
            LastTickTime = duration; // RemainTime 기준이므로 Duration으로 초기화
        }
    }
}
