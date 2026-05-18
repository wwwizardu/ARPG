using ARPG.History;

namespace ARPG.Component
{
    public struct HistoricalEventComponent
    {
        public int TemplateId;
        public int Year;
        public HistoryEra Era;
        public int RegionEntityId;
        public int CausedByEventEntityId;
        public ActorSlotPair Actor0;
        public ActorSlotPair Actor1;
        public ActorSlotPair Actor2;
        public ActorSlotPair Actor3;
    }

    public struct ActorSlotPair
    {
        public int SlotNameHash;
        public int ActorEntityId;
    }
}
