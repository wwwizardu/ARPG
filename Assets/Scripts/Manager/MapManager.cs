using UnityEngine;
using System.Collections.Generic;

namespace ARPG
{
    [System.Serializable]
    public class MapChunk
    {
        public int chunkX;
        public int chunkY;
        public int[,] tiles;
        public bool isGenerated;
        
        public MapChunk(int x, int y, int chunkSize)
        {
            chunkX = x;
            chunkY = y;
            tiles = new int[chunkSize, chunkSize];
            isGenerated = false;
        }
    }
    
    public class MapManager : MonoBehaviour
    {
        [Header("Map Settings")]
        public int chunkSize = 32;
        public int mapSeed = 12345;
        public float noiseScale = 0.1f;
        public float terrainHeight = 10f;
        
        private Dictionary<Vector2Int, MapChunk> _loadedChunks;
        private System.Random _randomGenerator;
        private Vector2Int _currentPlayerChunk;
        private int _loadRadius = 1;
        
        void Start()
        {
            _loadedChunks = new Dictionary<Vector2Int, MapChunk>();
            _randomGenerator = new System.Random(mapSeed);
        }
        
        public MapChunk GenerateChunk(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            
            if (_loadedChunks.ContainsKey(chunkKey))
            {
                return _loadedChunks[chunkKey];
            }
            
            MapChunk newChunk = new MapChunk(chunkX, chunkY, chunkSize);
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunkX * chunkSize + x;
                    int worldY = chunkY * chunkSize + y;
                    
                    float noiseValue = Mathf.PerlinNoise(
                        (worldX + mapSeed) * noiseScale,
                        (worldY + mapSeed) * noiseScale
                    );
                    
                    int tileType = Mathf.FloorToInt(noiseValue * terrainHeight);
                    newChunk.tiles[x, y] = tileType;
                }
            }
            
            newChunk.isGenerated = true;
            _loadedChunks[chunkKey] = newChunk;
            
            return newChunk;
        }
        
        public int GetTileAt(int worldX, int worldY)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            
            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);
            
            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }
            
            MapChunk chunk = GenerateChunk(chunkX, chunkY);
            return chunk.tiles[localX, localY];
        }
        
        public void UnloadChunk(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            if (_loadedChunks.ContainsKey(chunkKey))
            {
                _loadedChunks.Remove(chunkKey);
            }
        }
        
        public void SetSeed(int newSeed)
        {
            mapSeed = newSeed;
            _randomGenerator = new System.Random(mapSeed);
            _loadedChunks.Clear();
        }
        
        public void UpdateChunksAroundPlayer(Vector3 playerPosition)
        {
            Vector2Int playerChunk = WorldPositionToChunk(playerPosition);
            
            if (playerChunk != _currentPlayerChunk)
            {
                _currentPlayerChunk = playerChunk;
                LoadChunksAroundPlayer();
                UnloadDistantChunks();
            }
        }
        
        private Vector2Int WorldPositionToChunk(Vector3 worldPosition)
        {
            int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int chunkY = Mathf.FloorToInt(worldPosition.z / chunkSize);
            return new Vector2Int(chunkX, chunkY);
        }
        
        private void LoadChunksAroundPlayer()
        {
            for (int x = -_loadRadius; x <= _loadRadius; x++)
            {
                for (int y = -_loadRadius; y <= _loadRadius; y++)
                {
                    Vector2Int chunkPos = new Vector2Int(
                        _currentPlayerChunk.x + x,
                        _currentPlayerChunk.y + y
                    );
                    
                    if (!_loadedChunks.ContainsKey(chunkPos))
                    {
                        GenerateChunk(chunkPos.x, chunkPos.y);
                    }
                }
            }
        }
        
        private void UnloadDistantChunks()
        {
            List<Vector2Int> chunksToUnload = new List<Vector2Int>();
            
            foreach (var chunkPair in _loadedChunks)
            {
                Vector2Int chunkPos = chunkPair.Key;
                float distance = Vector2Int.Distance(chunkPos, _currentPlayerChunk);
                
                if (distance > _loadRadius + 1)
                {
                    chunksToUnload.Add(chunkPos);
                }
            }
            
            foreach (var chunkPos in chunksToUnload)
            {
                UnloadChunk(chunkPos.x, chunkPos.y);
            }
        }
        
        public int GetLoadedChunkCount()
        {
            return _loadedChunks.Count;
        }
        
        public List<Vector2Int> GetLoadedChunkPositions()
        {
            return new List<Vector2Int>(_loadedChunks.Keys);
        }
    }
}


