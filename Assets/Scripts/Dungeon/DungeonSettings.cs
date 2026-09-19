using System;

namespace ARPG.Dungeon
{
    [Serializable]
    public sealed class DungeonSettings
    {
        public int DefaultDungeonTableId = 1;
        public int CandidatesPerFrame = 2;
        public int TileQueriesPerFrame = 32;
        public int ChunksPerFrame = 2;
        public float PlacementMilliseconds = 1.5f;
        public float ApproachDistance = 64f;
        public float AutoSaveSeconds = 30f;
    }
}
