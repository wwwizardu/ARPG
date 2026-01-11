namespace ARPG.Component
{
    /// <summary>
    /// 개별 StatModifier를 Entity로 관리
    /// 각 StatModifier Entity는 이 컴포넌트를 가짐
    /// </summary>
    public struct StatModifierComponent
    {
        public int OwnerEntityId;   // 이 modifier가 적용되는 엔티티
        public StatModifier Modifier;

        public StatModifierComponent(int ownerEntityId, StatModifier modifier)
        {
            OwnerEntityId = ownerEntityId;
            Modifier = modifier;
        }
    }
}
