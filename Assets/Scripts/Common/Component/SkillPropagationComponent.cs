namespace ARPG.Component
{
    /// <summary>
    /// State shared by every delivery created from one skill commit or source hit.
    /// </summary>
    public struct SkillPropagationComponent
    {
        public int OwnerEntityId;
        public int SkillEntityId;
        public int PropagationTableId;
        public int OutstandingDeliveryCount;
        public int SpawnedDeliveryCount;
        public SkillPropagationTargetIds VisitedTargets;
    }
}
