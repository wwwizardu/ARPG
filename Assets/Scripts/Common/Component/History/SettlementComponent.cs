using UnityEngine;

namespace ARPG.Component
{
    public struct SettlementComponent
    {
        public int OwnerActorEntityId;
        public int RegionEntityId;
        public Vector2 Coord;
        public SettlementType Type;
        public int FoundedYear;
        public int Population;
        public SettlementState State;
    }

    public enum SettlementType
    {
        Capital = 0,
        City = 1,
        Village = 2,
        Outpost = 3,
        Ruin = 4,
        HeroTomb = 5,
        SealedDungeon = 6,
        MonsterLair = 7,
        AncientLibrary = 8,
    }

    public enum SettlementState
    {
        Prosperous = 0,
        Declining = 1,
        Ruined = 2,
        Occupied = 3,
        Sealed = 4,
    }
}
