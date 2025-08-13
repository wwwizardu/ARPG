using UnityEngine;

namespace ARPG.Map
{
    public class MapChunkObject : MonoBehaviour
    {
        private MapChunkData _chunkData;

        public void Initialize(MapChunkData inChunkData)
        {
            _chunkData = inChunkData;
        }

        
    }
}