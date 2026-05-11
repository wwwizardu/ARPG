using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Map
{
    [System.Serializable]
    public class ChunkMonsterData
    {
        public Vector2Int chunkCoord;
        public List<int> spawnedMonsterIds;
        public float lastActiveTime;
        public bool hasSpawned;
        public int originalSpawnCount;
        
        public ChunkMonsterData(Vector2Int coord)
        {
            chunkCoord = coord;
            spawnedMonsterIds = new List<int>();
            lastActiveTime = Time.time;
            hasSpawned = false;
            originalSpawnCount = 0;
        }
    }
}

namespace ARPG.Map
{
    [System.Serializable]
    public class MapChunkData
    {
        public int chunkX;
        public int chunkY;
        // 시드 청크 (0,0) 기준 Chebyshev 거리 + 1, [1,99] 클램프. GenerateChunkData에서 채움
        public int zone;
        public ulong[,] tiles;
        public bool isActive;
        public List<Vector2Int> monsterSpawnPositions;

        public MapChunkData(int chunkSize)
        {
            tiles = new ulong[chunkSize, chunkSize];
            monsterSpawnPositions = new List<Vector2Int>();
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
            monsterSpawnPositions.Clear();
        }
    }
}