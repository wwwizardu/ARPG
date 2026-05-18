using UnityEngine;

namespace ARPG.Component
{
    public struct RegionComponent
    {
        public int RegionId;
        public Vector2Int ChunkOrigin;
        public int BiomeId;
        public ulong TagsLow;
        public ulong TagsHigh;
        public ulong AmbientTagsLow;
        public ulong AmbientTagsHigh;
    }
}
