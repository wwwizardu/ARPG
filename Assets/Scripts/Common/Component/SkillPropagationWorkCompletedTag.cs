namespace ARPG.Component
{
    /// <summary>
    /// Prevents System_EntityDestroy from decrementing a propagation root twice after
    /// its normal request or delivery completion path has already done so.
    /// </summary>
    public struct SkillPropagationWorkCompletedTag
    {
    }
}
