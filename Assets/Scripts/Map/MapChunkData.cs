using UnityEngine;

namespace ARPG.Map
{
    [System.Serializable]
    public class MapChunkData
    {
        public int chunkX;
        public int chunkY;
        public uint[,] tiles;
        public bool isActive;
        
        public MapChunkData(int chunkSize)
        {
            tiles = new uint[chunkSize, chunkSize];
            isActive = false;
        }
        
        public void SetChunkPosition(int x, int y)
        {
            chunkX = x;
            chunkY = y;
            isActive = true;
        }
        
        public void Deactivate()
        {
            isActive = false;
        }
    }
}