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
        private Dictionary<Vector2Int, List<TileModification>> _tileModifications = new();
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

            // BuildableTileRegistry 로드 완료 시 활성 청크 재렌더 (lazy Addressable 로드 대응)
            BuildableTileRegistry.TileLoaded += OnBuildableTileLoaded;
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
            _tileModifications.Clear();
            _currentPlayerChunk = Vector2Int.zero;

            OnResetSpawner();

            BuildableTileRegistry.TileLoaded -= OnBuildableTileLoaded;
        }

        /// <summary>
        /// BuildableTileRegistry 로드 완료 콜백.
        /// Phase A는 단순하게 활성 청크 전부 재렌더. 규모가 커지면 Id → 청크 역인덱스로 세분화.
        /// </summary>
        private void OnBuildableTileLoaded(int buildableId)
        {
            foreach (var chunk in _activeChunks.Values)
            {
                RenderChunkToTilemap(chunk);
            }
        }

        public void CreateMap(int inSeed, Vector3 playerPosition)
        {
            _randomSeed = inSeed;
            _randomGenerator = new System.Random(_randomSeed);

            // MapFileData의 NPC 오브젝트를 NpcManager에 1회 등록
            RegisterAllNpcsToManager();

            // MapFileData에 배치된 NPC가 없는 마을은 VillageTable 기반으로 기본 NPC 스폰 (쿨타임 포함)
            AR.s.Npc.EnsureAllVillagesPopulated();

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

            int villageIndex = 0;
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

                // Village 타입이면 villageId 전달, 아니면 -1
                int villageId = -1;
                if (mapFileData.MapType == MapType.Village)
                {
                    villageId = villageIndex;
                    villageIndex++;
                }

                if (npcObjects.Count > 0)
                {
                    AR.s.Npc.RegisterNpcsFromMapFile(npcObjects, mapFileData.StartPosition, chunkSize, villageId);
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

        /// <summary>
        /// 월드 좌표의 ObjectLayer 비트에 기록된 오브젝트 Id를 조회한다.
        /// 0 = 빈 타일. 0 초과 = BuildableItemTable.Id 또는 레거시 ObjectType 값.
        /// PlaceObject가 (objectId &lt;&lt; 10) & ObjectLayerMask로 기록하는 것의 대칭 연산.
        /// </summary>
        public int GetObjectIdAt(int worldX, int worldY)
        {
            ulong tile = GetTileAt(worldX, worldY);
            return (int)((tile & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10);
        }

        public Dictionary<Vector2Int, MapChunkData>.KeyCollection GetActiveChunkCoords()
        {
            return _activeChunks.Keys;
        }

        public bool IsChunkActive(Vector2Int chunkCoord)
        {
            return _activeChunks.ContainsKey(chunkCoord);
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

        /// <summary>
        /// 타일 좌표 기준 통행 차단 여부. Blocked 비트 + 건물 footprint 동시 검사.
        /// 충돌 시스템(System_Render) 위치 적분 단계에서 호출.
        /// </summary>
        public bool IsTileBlocked(int worldTileX, int worldTileY)
        {
            ulong tile = GetTileAt(worldTileX, worldTileY);
            if ((tile & (ulong)GlobalEnum.TileFlag.Blocked) != 0)
                return true;

            if (AR.s.Building != null && AR.s.Building.IsTileOccupied(worldTileX, worldTileY) == true)
                return true;

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

        // ==================== 타일 수정 (오브젝트 배치/제거) ====================

        /// <summary>
        /// 월드 좌표에 오브젝트를 배치한다.
        /// SpawnType=Entity인 건물은 타일 비트를 건드리지 않고 BuildingManager에 전적으로 위임한다.
        /// </summary>
        public bool PlaceObject(int worldX, int worldY, int objectId, bool isUnderConstruction = false)
        {
            return PlaceObjectInternal(worldX, worldY, objectId, isUnderConstruction, out _);
        }

        /// <summary>
        /// PlaceObject의 out 변형. SpawnType=Entity면 생성된 빌딩 EntityId를 반환 (Tile 경로는 -1).
        /// </summary>
        public bool PlaceObject(int worldX, int worldY, int objectId, bool isUnderConstruction, out int buildingEntityId)
        {
            return PlaceObjectInternal(worldX, worldY, objectId, isUnderConstruction, out buildingEntityId);
        }

        private bool PlaceObjectInternal(int worldX, int worldY, int objectId, bool isUnderConstruction, out int buildingEntityId)
        {
            buildingEntityId = -1;
            if (objectId <= 0)
                return false;

            // SpawnType=Entity면 타일 비트 무변경, BuildingManager에 위임
            Tables.BuildableItemTable table = AR.s.Data.GetBuildableItem(objectId);
            if (table != null && table.SpawnType == GlobalEnum.BuildableSpawnType.Entity)
            {
                // AR 프리팹에 BuildingManager 컴포넌트 연결이 누락되면 NullReference 방지
                if (AR.s.Building == null)
                {
                    Debug.LogError($"[MapManager] PlaceObject - AR.s.Building is null. AR 프리팹에 BuildingManager 컴포넌트 연결 확인 필요. objectId={objectId}");
                    return false;
                }
                int villageId = AR.s.Village != null ? AR.s.Village.FindVillageContaining(worldX, worldY) : -1;
                int entityId = AR.s.Building.PlaceBuilding(worldX, worldY, objectId, villageId, isUnderConstruction);
                buildingEntityId = entityId;
                return entityId >= 0;
            }

            // Tile 경로: 기존 로직 (objectId + Blocked 비트 기록 + Tilemap 렌더)
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);

            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }

            Vector2Int chunkCoord = new Vector2Int(chunkX, chunkY);

            // 수정 기록 저장 (같은 위치면 덮어쓰기)
            SetTileModification(chunkCoord, localX, localY, objectId, false);

            // 활성 청크면 즉시 반영
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk))
            {
                // 타일 데이터 갱신
                ulong currentTile = chunk.tiles[localX, localY];
                currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile |= ((ulong)objectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile |= (ulong)GlobalEnum.TileFlag.Blocked;
                currentTile &= ~(ulong)GlobalEnum.TileFlag.MonsterSpawn;
                chunk.tiles[localX, localY] = currentTile;

                // 타일맵에 즉시 렌더링
                RenderSingleObjectTile(worldX, worldY, (ulong)objectId);
            }

            return true;
        }

        /// <summary>
        /// 월드 좌표의 플레이어 배치 오브젝트를 제거한다.
        /// </summary>
        public bool RemoveObject(int worldX, int worldY)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);

            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }

            Vector2Int chunkCoord = new Vector2Int(chunkX, chunkY);

            // 수정 기록 저장
            SetTileModification(chunkCoord, localX, localY, 0, true);

            // 활성 청크면 즉시 반영
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk))
            {
                ulong currentTile = chunk.tiles[localX, localY];
                currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile &= ~(ulong)GlobalEnum.TileFlag.Blocked;
                chunk.tiles[localX, localY] = currentTile;

                RenderSingleObjectTile(worldX, worldY, 0);
            }

            return true;
        }

        private void SetTileModification(Vector2Int chunkCoord, int localX, int localY, int objectId, bool isRemoval)
        {
            if (_tileModifications.TryGetValue(chunkCoord, out List<TileModification> modifications) == false)
            {
                modifications = new List<TileModification>();
                _tileModifications[chunkCoord] = modifications;
            }

            // 같은 위치에 기존 수정이 있으면 덮어쓰기
            for (int i = 0; i < modifications.Count; i++)
            {
                if (modifications[i].LocalX == localX && modifications[i].LocalY == localY)
                {
                    modifications[i].ObjectId = objectId;
                    modifications[i].IsRemoval = isRemoval;
                    return;
                }
            }

            modifications.Add(new TileModification
            {
                LocalX = localX,
                LocalY = localY,
                ObjectId = objectId,
                IsRemoval = isRemoval,
            });
        }

        private void RenderSingleObjectTile(int worldX, int worldY, ulong objectId)
        {
            if (_tileMap_Object == null)
                return;

            Vector3Int cellPos = new Vector3Int(worldX, worldY, 0);

            if (objectId > 0)
            {
                // 1) BuildableTileRegistry 우선 (Addressable 로드 캐시)
                // 2) 미스 시 레거시 ThemeTileSet.ObjectSet fallback
                TileBase tile = BuildableTileRegistry.Get((int)objectId);
                if (tile == null
                    && _themeTileSet != null
                    && _themeTileSet.ObjectSet != null
                    && objectId < (ulong)_themeTileSet.ObjectSet.Length)
                {
                    tile = _themeTileSet.ObjectSet[objectId];
                }
                _tileMap_Object.SetTile(cellPos, tile);
            }
            else
            {
                _tileMap_Object.SetTile(cellPos, null);
            }
        }

        // ==================== 타일 수정 저장/로드 ====================

        public List<ChunkModificationData> SaveTileModifications()
        {
            List<ChunkModificationData> result = new List<ChunkModificationData>();

            foreach (var kvp in _tileModifications)
            {
                if (kvp.Value.Count == 0)
                    continue;

                ChunkModificationData data = new ChunkModificationData
                {
                    ChunkX = kvp.Key.x,
                    ChunkY = kvp.Key.y,
                    Modifications = new List<TileModification>(kvp.Value),
                };
                result.Add(data);
            }

            return result;
        }

        public void LoadTileModifications(List<ChunkModificationData> modifications)
        {
            _tileModifications.Clear();

            if (modifications == null)
                return;

            for (int i = 0; i < modifications.Count; i++)
            {
                ChunkModificationData data = modifications[i];
                Vector2Int chunkCoord = new Vector2Int(data.ChunkX, data.ChunkY);
                _tileModifications[chunkCoord] = new List<TileModification>(data.Modifications);
            }
        }

    }
}


