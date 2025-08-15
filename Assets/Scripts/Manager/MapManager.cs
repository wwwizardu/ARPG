using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [SerializeField] private Tilemap _tileMap;
        
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

        public void Reset()
        {
            foreach (var chunk in _activeChunks.Values)
            {
                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }
            
            _activeChunks.Clear();
            _currentPlayerChunk = Vector2Int.zero;
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
        
        public void GenerateMapAroundPlayer(Vector3 playerPosition)
        {
            Vector2Int playerChunk = WorldPositionToChunk(playerPosition);
            
            for (int x = -_loadRadius; x <= _loadRadius; x++)
            {
                for (int y = -_loadRadius; y <= _loadRadius; y++)
                {
                    Vector2Int chunkPos = new Vector2Int(
                        playerChunk.x + x,
                        playerChunk.y + y
                    );
                    
                    MapChunkData chunk = GetOrCreateChunk(chunkPos.x, chunkPos.y);
                    if (chunk != null)
                    {
                        RenderChunkToTilemap(chunk);
                    }
                }
            }
        }
        
        private void RenderChunkToTilemap(MapChunkData chunk)
        {
            if (_tileMap == null || _tileAssets == null || _tileAssets.Length < 2) return;
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;
                    
                    uint tileData = chunk.tiles[x, y];
                    int tileType = (int)(tileData & 0xF); // 하위 4비트 읽기
                    
                    Vector3Int tilePosition = new Vector3Int(worldX, worldY, 0);
                    
                    if (tileType < _tileAssets.Length)
                    {
                        _tileMap.SetTile(tilePosition, _tileAssets[tileType]);
                    }
                }
            }
        }
    }
}


