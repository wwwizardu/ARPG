namespace ARPG.Component
{
    /// <summary>Value captured at the lethal HP transition, before the caster or companion can disappear.</summary>
    public struct KillCreditComponent
    {
        public int SourceEntityId;
        public bool PlayerParty;
    }
}
