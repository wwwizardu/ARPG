using UnityEngine;

namespace ARPG.Map
{
    public class MapChunkObject : MonoBehaviour
    {
        private MapchunkData _chunkData;

        public void Initialize(MapChunkData inChunkData)
        {
            _chunkData = inChunkData;
        }

        
    }
}