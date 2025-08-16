using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [SerializeField] private Tilemap _tileMap;
        [SerializeField] private Tilemap _tileMap_Hill;
        
        [Header("Map Settings")]
        public int chunkSize = 8;
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
        private Vector2Int _currentPlayerChunk = new Vector2Int(-100000, -100000);
        private int _loadRadius = 2;
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
        
        public GlobalEnum.TileType GetTileTypeAt(int worldX, int worldY)
        {
            uint tileData = GetTileAt(worldX, worldY);
            if (tileData == 0) return GlobalEnum.TileType.Ground; // 기본값
            
            return (GlobalEnum.TileType)(tileData & 0xF);
        }
        
        public bool IsHillAt(int worldX, int worldY)
        {
            uint tileData = GetTileAt(worldX, worldY);
            return (tileData & (uint)GlobalEnum.TileFlag.Hill) != 0;
        }
        
        public uint GetTileAt(int worldX, int worldY)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);

            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);

            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }

            MapChunkData chunk = GetOrCreateChunk(chunkX, chunkY);
            if (chunk == null)
            {
                Debug.LogWarning($"[MapManager] GetTileAt - Chunk ({chunkX}, {chunkY}) is out of bounds.");
                return 0; // 맵 범위 밖
            }

            return chunk.tiles[localX, localY];
        }
        
    }
}


