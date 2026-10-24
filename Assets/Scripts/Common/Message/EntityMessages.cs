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

        /// <summary>
        /// 회피 여부
        /// </summary>
        public bool IsEvaded;

        /// <summary>
        /// 막기 여부
        /// </summary>
        public bool IsBlocked;

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
    /// 마나 변경 메시지
    /// CurrentMp가 변동되었을 때 전송 (스킬 시전 차감, 자연 회복 등).
    /// HpBarView가 수신하여 MP바 fillAmount 갱신.
    /// </summary>
    public struct ManaChangedMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }
        public int CurrentMp;
        public int MaxMp;
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

    /// <summary>
    /// 스탯 재계산 완료 Broadcast 메시지
    /// System_StatCalculation에서 Final 스탯 재계산 후 전송
    /// UI 등 전역 구독자가 수신
    /// </summary>
    public struct StatRecalculatedMessage
    {
        /// <summary>
        /// 스탯이 재계산된 엔티티 ID
        /// </summary>
        public int EntityId;
    }

    /// <summary>
    /// 플레이어 레벨·경험치·능력치 포인트 변경 Broadcast 메시지 (PLAYER_LEVEL_CHARM_DESIGN.md §8.1)
    /// PlayerManager가 경험치 획득·분배·되돌리기 때 발행. 경험치 바·캐릭터 창·레벨업 알림이 구독한다.
    /// </summary>
    public struct PlayerProgressChangedMessage
    {
        public int Level;
        /// <summary>이번 변경에서 레벨이 올랐다 (레벨업 글자·효과음)</summary>
        public bool LeveledUp;
        /// <summary>이번 레벨업으로 참 판 자리가 새로 열렸다 (§8.1 알림)</summary>
        public bool CharmBoardSlotOpened;
    }

    /// <summary>
    /// 참 판 자리·박힌 참 변경 Broadcast 메시지 (PLAYER_LEVEL_CHARM_DESIGN.md §8.4~§8.6). 참 판 창이 구독해 다시 그린다.
    /// </summary>
    public struct CharmBoardChangedMessage
    {
        /// <summary>바뀐 판 자리 (0~8). 여러 자리가 바뀌면 -1</summary>
        public int SlotIndex;
    }

    /// <summary>
    /// 스킬북 슬롯 변경 Broadcast 메시지 (SKILLBOOK_DESIGN.md §4.1)
    /// PlayerSkillManager.Equip/UnequipSkillBook 성공 시 발행.
    /// UI가 구독해 슬롯·인벤토리 표시를 갱신.
    /// </summary>
    public struct SkillBookChangedMessage
    {
        /// <summary>변경된 스킬 슬롯 인덱스 (0 ~ PLAYER_SKILL_SLOT_COUNT-1)</summary>
        public int SlotIndex;

        /// <summary>새로 장착된 SkillId. 해제(unequip) 시 0.</summary>
        public int NewSkillId;
    }

    /// <summary>
    /// 던전 입구 소문 징후 자막의 표시 상태 변경 메시지
    /// System_DungeonEntranceSign이 상태가 바뀔 때만 전송, DungeonEntranceSignView가 수신해 자막만 켜고 끈다.
    /// </summary>
    public struct DungeonEntranceSignMessage : IEntityMessage
    {
        public int TargetEntityId { get; set; }

        /// <summary>true면 자막 표시, false면 숨김.</summary>
        public bool Active;
    }
}
