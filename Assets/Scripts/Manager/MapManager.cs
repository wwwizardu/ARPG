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
        public bool isActive;
        
        public MapChunk(int chunkSize)
        {
            tiles = new int[chunkSize, chunkSize];
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
    
    public class MapManager : MonoBehaviour
    {
        [Header("Map Settings")]
        public int chunkSize = 32;
        public int mapSeed = 12345;
        public float noiseScale = 0.1f;
        public float terrainHeight = 10f;
        
        private Dictionary<Vector2Int, MapChunk> _activeChunks;
        private Stack<MapChunk> _chunkPool;
        private System.Random _randomGenerator;
        private Vector2Int _currentPlayerChunk;
        private int _loadRadius = 1;
        private const int POOL_SIZE = 20;
        
        void Start()
        {
            _activeChunks = new Dictionary<Vector2Int, MapChunk>();
            _chunkPool = new Stack<MapChunk>();
            _randomGenerator = new System.Random(mapSeed);
            
            InitializeChunkPool();
        }
        
        private void InitializeChunkPool()
        {
            for (int i = 0; i < POOL_SIZE; i++)
            {
                _chunkPool.Push(new MapChunk(chunkSize));
            }
        }
        
        public MapChunk GetOrCreateChunk(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            
            if (_activeChunks.ContainsKey(chunkKey))
            {
                return _activeChunks[chunkKey];
            }
            
            MapChunk chunk = GetChunkFromPool();
            chunk.SetChunkPosition(chunkX, chunkY);
            GenerateChunkData(chunk);
            _activeChunks[chunkKey] = chunk;
            
            return chunk;
        }
        
        private MapChunk GetChunkFromPool()
        {
            if (_chunkPool.Count > 0)
            {
                return _chunkPool.Pop();
            }
            else
            {
                return new MapChunk(chunkSize);
            }
        }
        
        private void GenerateChunkData(MapChunk chunk)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;
                    
                    float noiseValue = Mathf.PerlinNoise(
                        (worldX + mapSeed) * noiseScale,
                        (worldY + mapSeed) * noiseScale
                    );
                    
                    int tileType = Mathf.FloorToInt(noiseValue * terrainHeight);
                    chunk.tiles[x, y] = tileType;
                }
            }
        }
        
        public int GetTileAt(int worldX, int worldY)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            
            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);
            
            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }
            
            MapChunk chunk = GetOrCreateChunk(chunkX, chunkY);
            return chunk.tiles[localX, localY];
        }
        
        private void ReturnChunkToPool(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            if (_activeChunks.ContainsKey(chunkKey))
            {
                MapChunk chunk = _activeChunks[chunkKey];
                _activeChunks.Remove(chunkKey);
                
                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }
        }
        
        public void SetSeed(int newSeed)
        {
            mapSeed = newSeed;
            _randomGenerator = new System.Random(mapSeed);
            
            foreach (var chunk in _activeChunks.Values)
            {
                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }
            _activeChunks.Clear();
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
                    
                    if (!_activeChunks.ContainsKey(chunkPos))
                    {
                        GetOrCreateChunk(chunkPos.x, chunkPos.y);
                    }
                }
            }
        }
        
        private void UnloadDistantChunks()
        {
            List<Vector2Int> chunksToReturn = new List<Vector2Int>();
            
            foreach (var chunkPair in _activeChunks)
            {
                Vector2Int chunkPos = chunkPair.Key;
                float distance = Vector2Int.Distance(chunkPos, _currentPlayerChunk);
                
                if (distance > _loadRadius + 1)
                {
                    chunksToReturn.Add(chunkPos);
                }
            }
            
            foreach (var chunkPos in chunksToReturn)
            {
                ReturnChunkToPool(chunkPos.x, chunkPos.y);
            }
        }
        
        public int GetActiveChunkCount()
        {
            return _activeChunks.Count;
        }
        
        public int GetPooledChunkCount()
        {
            return _chunkPool.Count;
        }
        
        public List<Vector2Int> GetActiveChunkPositions()
        {
            return new List<Vector2Int>(_activeChunks.Keys);
        }
    }
}


