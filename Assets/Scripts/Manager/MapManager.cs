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
        [SerializeField] private Tilemap _tileMap_Object;
        
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

        [Header("Monster Spawn")]
        [SerializeField] private float _monsterSpawnRate = 0.1f;

        private int _randomSeed = 12345;
        private Dictionary<Vector2Int, MapChunkData> _activeChunks;
        private Dictionary<Vector2Int, MapFileData> _mapFileDataDic = new();
        private Stack<MapChunkData> _chunkPool;
        private System.Random _randomGenerator;
        private Vector2Int _currentPlayerChunk = new Vector2Int(-100000, -100000);
        private int _loadRadius = 2;
        private const int POOL_SIZE = 20;

        public void Initialize()
        {
            _activeChunks = new Dictionary<Vector2Int, MapChunkData>();
            _chunkPool = new Stack<MapChunkData>();

            InitializeChunkPool();
        }

        public void Reset()
        {
            _randomGenerator = new System.Random(_randomSeed);

            foreach (var chunk in _activeChunks.Values)
            {
                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }

            _activeChunks.Clear();
            _currentPlayerChunk = Vector2Int.zero;

            OnResetSpawner();
        }

        public void CreateMap(int inSeed, Vector3 playerPosition)
        {
            _randomSeed = inSeed;
            _randomGenerator = new System.Random(_randomSeed);

            // MapFileData의 NPC 오브젝트를 NpcManager에 1회 등록
            RegisterAllNpcsToManager();

            UpdateChunksAroundPlayer(playerPosition);

            OnResetSpawner();
        }

        /// <summary>
        /// 모든 MapFileData에서 NPC 오브젝트를 찾아 NpcManager에 등록한다. (맵 로드 시 1회)
        /// </summary>
        private void RegisterAllNpcsToManager()
        {
            if (AR.s.Npc == null)
                return;

            foreach (var mapFileEntry in _mapFileDataDic)
            {
                MapFileData mapFileData = mapFileEntry.Value;
                List<MapFileObjectData> objects = mapFileData.GetObjects();
                if (objects == null || objects.Count == 0)
                    continue;

                // NPC 타입만 필터링
                List<MapFileObjectData> npcObjects = new();
                for (int i = 0; i < objects.Count; i++)
                {
                    if (objects[i].ObjectType == (int)GlobalEnum.ObjectType.Npc)
                    {
                        npcObjects.Add(objects[i]);
                    }
                }

                if (npcObjects.Count > 0)
                {
                    AR.s.Npc.RegisterNpcsFromMapFile(npcObjects, mapFileData.StartPosition, chunkSize);
                }
            }
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
            ulong tileData = GetTileAt(worldX, worldY);
            if (tileData == 0) return GlobalEnum.TileType.Ground; // 기본값
            
            return (GlobalEnum.TileType)(tileData & 0xF);
        }
        
        public bool IsHillAt(int worldX, int worldY)
        {
            ulong tileData = GetTileAt(worldX, worldY);
            return (tileData & (uint)GlobalEnum.TileFlag.Hill) != 0;
        }
        
        public ulong GetTileAt(int worldX, int worldY)
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

        public Dictionary<Vector2Int, MapChunkData>.KeyCollection GetActiveChunkCoords()
        {
            return _activeChunks.Keys;
        }

        public bool TryGetChunkSpawnPositions(Vector2Int chunkCoord, out List<Vector2Int> spawnPositions)
        {
            if (_activeChunks.ContainsKey(chunkCoord))
            {
                spawnPositions = _activeChunks[chunkCoord].monsterSpawnPositions;
                return true;
            }
            spawnPositions = null;
            return false;
        }

        public bool IsWalkable(Vector3 worldPosition)
        {
            Vector3Int cellPos = _tileMap.WorldToCell(worldPosition);

            // 1. Object 레이어 체크 (벽, 나무 등 장애물)
            TileBase objectTile = _tileMap_Object.GetTile(cellPos);
            if (objectTile != null)
            {
                // CustomTile이면 IsWalkable 플래그 확인
                if (objectTile is CustomTile customTile)
                {
                    return customTile.IsWalkable;
                }
                // CustomTile이 아니면 기본적으로 이동 불가
                return false;
            }

            // 2. 기존 타일 데이터 시스템 활용 - Blocked 플래그 체크
            ulong tileData = GetTileAt(cellPos.x, cellPos.y);
            bool isBlocked = (tileData & (ulong)GlobalEnum.TileFlag.Blocked) != 0;

            return !isBlocked; // Blocked가 아니면 이동 가능
        }

        public Vector3 GetCellCenterWorld(Vector3 worldPosition)
        {
            Vector3Int cellPos = _tileMap.WorldToCell(worldPosition);
            return _tileMap.GetCellCenterWorld(cellPos);
        }

    }
}


