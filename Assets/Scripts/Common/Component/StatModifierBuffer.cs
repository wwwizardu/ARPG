using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// 스탯 보정 타입
    /// </summary>
    public enum StatModifierType
    {
        Add,        // 더하기 (예: +10 공격력)
        Multiply,   // 곱하기 (예: +50% 공격력 = 1.5배)
        Override,   // 덮어쓰기 (특수한 경우에만 사용)
    }

    /// <summary>
    /// 스탯 보정 소스 타입
    /// </summary>
    public enum StatModifierSource
    {
        Equipment,  // 장비에 의한 보정
        Buff,       // 버프에 의한 보정
        Skill,      // 스킬에 의한 보정
        Terrain,    // 지형 효과
        Party,      // 파티 버프
    }

    /// <summary>
    /// 개별 스탯 보정 데이터
    /// 장비, 버프 등에서 스탯을 수정할 때 사용
    /// </summary>
    public struct StatModifier
    {
        public GE.Stat StatType;                // 어떤 스탯을 수정할지
        public StatModifierType ModifierType;   // 어떻게 수정할지 (더하기/곱하기/덮어쓰기)
        public StatModifierSource Source;       // 보정의 출처
        public int SourceId;                    // 출처의 ID (장비 ID, 버프 ID 등)
        public int Value;                       // 보정 값
        public int Priority;                    // 적용 우선순위 (낮을수록 먼저 적용)

        public StatModifier(GE.Stat statType, StatModifierType modifierType, StatModifierSource source, int sourceId, int value, int priority = 0)
        {
            StatType = statType;
            ModifierType = modifierType;
            Source = source;
            SourceId = sourceId;
            Value = value;
            Priority = priority;
        }

        /// <summary>
        /// 특정 소스로부터의 modifier인지 확인
        /// </summary>
        public bool IsFromSource(StatModifierSource source, int sourceId)
        {
            return Source == source && SourceId == sourceId;
        }
    }

    /// <summary>
    /// 엔티티에 적용된 모든 스탯 보정을 저장하는 동적 버퍼
    /// 장비 착용, 버프 적용 등으로 동적으로 추가/제거됨
    /// </summary>
    public struct StatModifierBufferElement
    {
        public StatModifier Modifier;
    }
}
