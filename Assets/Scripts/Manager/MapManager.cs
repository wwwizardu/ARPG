using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace ARPG.Map
{
    public class MapManager : MonoBehaviour
    {
        [Header("Map Settings")]
        public int chunkSize = 32;
        public int mapSeed = 12345;
        public float noiseScale = 0.1f;
        public float terrainHeight = 10f;
        
        [Header("Map Bounds")]
        public int minChunkX = -50;
        public int maxChunkX = 50;
        public int minChunkY = -50;
        public int maxChunkY = 50;
        
        private Dictionary<Vector2Int, MapChunkData> _activeChunks;
        private Stack<MapChunkData> _chunkPool;
        private System.Random _randomGenerator;
        private Vector2Int _currentPlayerChunk;
        private int _loadRadius = 1;
        private const int POOL_SIZE = 20;
        
        public void Initialize()
        {
            _activeChunks = new Dictionary<Vector2Int, MapChunkData>();
            _chunkPool = new Stack<MapChunkData>();
            _randomGenerator = new System.Random(mapSeed);
            
            InitializeChunkPool();
        }
        
        private void InitializeChunkPool()
        {
            for (int i = 0; i < POOL_SIZE; i++)
            {
                _chunkPool.Push(new MapChunkData(chunkSize));
            }
        }
        
        public MapChunkData GetOrCreateChunk(int chunkX, int chunkY)
        {
            if (chunkX < minChunkX || chunkX > maxChunkX || 
                chunkY < minChunkY || chunkY > maxChunkY)
            {
                return null;
            }
            
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            
            if (_activeChunks.ContainsKey(chunkKey))
            {
                return _activeChunks[chunkKey];
            }
            
            MapChunkData chunk = GetChunkFromPool();
            chunk.SetChunkPosition(chunkX, chunkY);
            GenerateChunkData(chunk);
            _activeChunks[chunkKey] = chunk;
            
            return chunk;
        }
        
        private MapChunkData GetChunkFromPool()
        {
            if (_chunkPool.Count > 0)
            {
                return _chunkPool.Pop();
            }
            else
            {
                return new MapChunkData(chunkSize);
            }
        }
        
        private void GenerateChunkData(MapChunkData chunk)
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
                    
                    GlobalEnum.TileType tileType = noiseValue > 0.4f ? 
                        GlobalEnum.TileType.Glass : GlobalEnum.TileType.Ground;
                    
                    int currentTile = chunk.tiles[x, y];
                    chunk.tiles[x, y] = (int)((currentTile & 0xFFFFFFF0) | ((int)tileType & 0xF));
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
            
            MapChunkData chunk = GetOrCreateChunk(chunkX, chunkY);
            if (chunk == null) return -1; // 맵 범위 밖
            
            return chunk.tiles[localX, localY];
        }
        
        public GlobalEnum.TileType GetTileTypeAt(int worldX, int worldY)
        {
            int tileData = GetTileAt(worldX, worldY);
            if (tileData == -1) return GlobalEnum.TileType.Ground; // 기본값
            
            return (GlobalEnum.TileType)(tileData & 0xF);
        }
        
        private void ReturnChunkToPool(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            if (_activeChunks.ContainsKey(chunkKey))
            {
                MapChunkData chunk = _activeChunks[chunkKey];
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


