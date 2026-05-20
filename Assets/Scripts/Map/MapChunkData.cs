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
        // 청크 대표 biome (v1.9: 청크 중심 cluster의 biome). MonsterManager 풀 선택처럼 1개 biome 요구하는 caller용.
        // 세밀한 per-cluster biome은 clusterBiomes 참조.
        public GlobalEnum.BiomeType chunkBiome;
        // cluster-grained biomes (v1.9): chunkSize / BiomeNoise.CLUSTER_SIZE per side.
        // 8/2 = 4 → 16 entries. Renderer/Generator가 per-cluster theme 결정에 사용.
        public GlobalEnum.BiomeType[] clusterBiomes;
        public ulong[,] tiles;
        public bool isActive;
        public List<Vector2Int> monsterSpawnPositions;

        public MapChunkData(int chunkSize)
        {
            tiles = new ulong[chunkSize, chunkSize];
            int clustersPerSide = chunkSize / BiomeNoise.CLUSTER_SIZE;
            clusterBiomes = new GlobalEnum.BiomeType[clustersPerSide * clustersPerSide];
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