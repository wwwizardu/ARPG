using UnityEngine;

namespace ARPG.Component
{
    public struct MapChunkLoaderComponent
    {
        public Vector2Int CurrentChunk;
        public int LoadRadius;
        public bool IsInitialized;
    }
}
