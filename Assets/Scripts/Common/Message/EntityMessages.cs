namespace ARPG.Message
{
    /// <summary>
    /// 데미지 메시지
    /// 엔티티가 데미지를 받았을 때 전송
    /// </summary>
    public struct DamageMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 데미지 양
        /// </summary>
        public int DamageAmount;

        /// <summary>
        /// 공격자 엔티티 ID
        /// </summary>
        public int AttackerEntityId;

        /// <summary>
        /// 데미지 타입
        /// </summary>
        public GlobalEnum.DamageType DamageType;

        /// <summary>
        /// 치명타 여부
        /// </summary>
        public bool IsCritical;

        public int CurrentHp;
        public int MaxHp;
    }

    /// <summary>
    /// 힐 메시지
    /// 엔티티가 회복을 받았을 때 전송
    /// </summary>
    public struct HealMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 회복량
        /// </summary>
        public int HealAmount;

        /// <summary>
        /// 회복을 준 엔티티 ID (0이면 자연 회복)
        /// </summary>
        public int HealerEntityId;
    }

    /// <summary>
    /// 스탯 변경 메시지
    /// 엔티티의 스탯이 변경되었을 때 전송
    /// </summary>
    public struct StatChangedMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 변경된 스탯 타입
        /// </summary>
        public GlobalEnum.Stat StatType;

        /// <summary>
        /// 이전 값
        /// </summary>
        public float OldValue;

        /// <summary>
        /// 새 값
        /// </summary>
        public float NewValue;
    }

    /// <summary>
    /// 버프 변경 메시지
    /// 버프가 추가/제거/스택 변경되었을 때 전송
    /// </summary>
    public struct BuffChangedMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 버프 테이블 ID
        /// </summary>
        public int BuffTableId;

        /// <summary>
        /// 추가 여부 (true: 추가, false: 제거)
        /// </summary>
        public bool IsAdded;

        /// <summary>
        /// 현재 스택 수
        /// </summary>
        public int StackCount;

        /// <summary>
        /// 버프를 건 엔티티 ID
        /// </summary>
        public int SourceEntityId;
    }

    /// <summary>
    /// 사망 메시지
    /// 엔티티가 사망했을 때 전송
    /// </summary>
    public struct DeathMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 킬러 엔티티 ID (0이면 자연사 등)
        /// </summary>
        public int KillerEntityId;
    }

    /// <summary>
    /// 스킬 사용 메시지
    /// 엔티티가 스킬을 사용했을 때 전송
    /// </summary>
    public struct SkillUsedMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>
        /// 사용된 스킬 ID
        /// </summary>
        public int SkillId;

        /// <summary>
        /// 스킬 슬롯 인덱스
        /// </summary>
        public int SlotIndex;
    }
}
