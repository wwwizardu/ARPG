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
        // Zone 상한 99 + 시드 청크 (0,0) → Chebyshev 거리 98까지 도달 가능해야 함
        public int minChunkX = -98;
        public int maxChunkX = 98;
        public int minChunkY = -98;
        public int maxChunkY = 98;

        [Header("Warm Chunk Cache")]
        [SerializeField] private float _warmChunkLifetime = 300f;
        [SerializeField] private int _maxWarmChunkCount = 20;

        [Header("Monster Spawn Exclusion")]
        // 마을 청사진 직사각형을 사방으로 넓혀 몬스터 스폰을 금지하는 여유 거리(타일)
        [SerializeField] private int _villageSpawnExclusionMargin = 5;

        private static readonly Vector2Int InvalidChunk = new Vector2Int(-100000, -100000);

        // _activeChunks는 Active/Warm 청크 모두를 보존한다. _renderedChunks는 Active 부분집합이다.
        private Dictionary<Vector2Int, MapChunkData> _activeChunks;
        private HashSet<Vector2Int> _renderedChunks;
        private List<Vector2Int> _loadedChunkCoords;
        private List<Vector2Int> _renderedChunkCoords;
        private Dictionary<Vector2Int, MapFileData> _mapFileDataDic = new();
        // R1: 절차 정착지 청사진 origin(StartPosition) → 연대기 SettlementId(placement 인덱스). 마을↔정착지 매핑용
        private Dictionary<Vector2Int, int> _settlementIdByOrigin = new();
        private Dictionary<Vector2Int, List<TileModification>> _tileModifications = new();
        // v1.9 region histogram 캐시 — biome 통계 lazy compute (region 첫 조회 시 256 cluster 샘플).
        // 메모리: region당 Dictionary<BiomeType,int> ~수십 바이트, 무해.
        private Dictionary<Vector2Int, RegionData> _regionData = new();
        private Stack<MapChunkData> _chunkPool;
        private Vector2Int _currentPlayerChunk = InvalidChunk;
        private int _loadRadius = 2;
        private const int POOL_SIZE = 20;
        private int _nextChunkGenerationId = 1;
        public MapContext Context { get; private set; } = new();

        public void Initialize()
        {
            Context.IsActive = true;
            Context.Epoch++;
            if (Context.Kind == MapKind.World) mapSeed = AR.s.Data.WorldSeed;
            _activeChunks = new Dictionary<Vector2Int, MapChunkData>();
            _renderedChunks = new HashSet<Vector2Int>();
            _loadedChunkCoords = new List<Vector2Int>();
            _renderedChunkCoords = new List<Vector2Int>();
            _chunkPool = new Stack<MapChunkData>();

            // BiomeThemeEntry[] → Dictionary 변환 (MAP_DESIGN.md §4.5)
            BuildThemeDictionary();

            InitializeChunkPool();

            // BuildableTileRegistry 로드 완료 시 활성 청크 재렌더 (lazy Addressable 로드 대응)
            BuildableTileRegistry.TileLoaded += OnBuildableTileLoaded;
        }

        public void Reset()
        {
            Context.IsActive = false;
            Context.Epoch++;
            for (int i = 0; i < _loadedChunkCoords.Count; i++)
            {
                Vector2Int chunkCoord = _loadedChunkCoords[i];
                MapChunkData chunk = _activeChunks[chunkCoord];
                if (_renderedChunks.Contains(chunkCoord))
                {
                    RemoveChunkFromTilemap(chunk);
                }

                chunk.InvalidateGeneration();
                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }

            _activeChunks.Clear();
            _renderedChunks.Clear();
            _loadedChunkCoords.Clear();
            _renderedChunkCoords.Clear();
            _tileModifications.Clear();
            _regionData.Clear();
            _currentPlayerChunk = InvalidChunk;

            ClearMissingTileLog();

            OnResetSpawner();

            BuildableTileRegistry.TileLoaded -= OnBuildableTileLoaded;
        }

        /// <summary>
        /// BuildableTileRegistry 로드 완료 콜백.
        /// Phase A는 단순하게 활성 청크 전부 재렌더. 규모가 커지면 Id → 청크 역인덱스로 세분화.
        /// </summary>
        private void OnBuildableTileLoaded(int buildableId)
        {
            for (int i = 0; i < _renderedChunkCoords.Count; i++)
            {
                Vector2Int chunkCoord = _renderedChunkCoords[i];
                if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk))
                {
                    RenderChunkToTilemap(chunk);
                }
            }
        }

        public void CreateMap(int inSeed, Vector3 playerPosition)
        {
            if (inSeed != mapSeed)
                throw new System.InvalidOperationException("World seed must be loaded before map initialization.");
            // MapFileData의 NPC 오브젝트를 NpcManager에 1회 등록
            RegisterAllNpcsToManager();

            // MapFileData에 배치된 NPC가 없는 마을은 VillageTable 기반으로 기본 NPC 스폰 (쿨타임 포함)
            AR.s.Npc.EnsureAllVillagesPopulated();
            // 던전 소문 선정과 소문 주인 선택은 마을 주민이 배치된 뒤에 한다 (NPC_NARRATIVE_CONTENT_DESIGN §4.1, 2026-10-10).
            AR.s.Dungeon.OnVillagesPopulated();

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
            if (Context.Kind == MapKind.Dungeon)
            {
                _currentPlayerChunk = WorldPositionToChunk(playerPosition);
                UnloadDistantChunks();
                LoadDungeonChunksBudgeted(AR.s.Dungeon.Settings.ChunksPerFrame);
                CleanupWarmChunks();
                return;
            }
            Vector2Int playerChunk = WorldPositionToChunk(playerPosition);

            if (playerChunk != _currentPlayerChunk)
            {
                _currentPlayerChunk = playerChunk;
                LoadChunksAroundPlayer();
                UnloadDistantChunks();
            }
        }
        
        public bool IsHillAt(int worldX, int worldY)
        {
            ulong tileData = GetTileAt(worldX, worldY);
            return (tileData & (uint)GlobalEnum.TileFlag.Hill) != 0;
        }
        
        public ulong GetTileAt(int worldX, int worldY)
        {
            // Collision/path queries outside the visible interior ring must not allocate chunks.
            if (Context.Kind == MapKind.Dungeon) return SampleDungeonTile(worldX, worldY);
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);

            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);

            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }

            if (IsWorldTileInBounds(worldX, worldY) == false)
            {
                Debug.LogWarning($"[MapManager] GetTileAt - Chunk ({chunkX}, {chunkY}) is out of bounds.");
                return 0;
            }

            MapChunkData chunk = GetOrCreateChunk(chunkX, chunkY);
            if (chunk == null)
            {
                Debug.LogWarning($"[MapManager] GetTileAt - Chunk ({chunkX}, {chunkY}) is out of bounds.");
                return 0; // 맵 범위 밖
            }

            return chunk.tiles[localX, localY];
        }

        public bool IsWorldTileInBounds(int worldX, int worldY)
        {
            if (chunkSize <= 0)
                return false;

            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            return chunkX >= minChunkX && chunkX <= maxChunkX &&
                   chunkY >= minChunkY && chunkY <= maxChunkY;
        }

        public bool IsWorldTileFootprintInBounds(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return false;

            long maxX = (long)worldX + width - 1;
            long maxY = (long)worldY + height - 1;
            if (maxX > int.MaxValue || maxY > int.MaxValue)
                return false;

            return IsWorldTileInBounds(worldX, worldY) &&
                   IsWorldTileInBounds((int)maxX, (int)maxY);
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

        public IEnumerable<Vector2Int> GetActiveChunkCoords()
        {
            return _renderedChunkCoords;
        }

        public void GetActiveChunkCoordsNonAlloc(List<Vector2Int> result)
        {
            result.Clear();
            for (int i = 0; i < _renderedChunkCoords.Count; i++)
            {
                result.Add(_renderedChunkCoords[i]);
            }
        }

        public bool IsChunkActive(Vector2Int chunkCoord)
        {
            return _renderedChunks.Contains(chunkCoord);
        }

        /// <summary>
        /// 월드 좌표가 속한 청크가 현재 활성(렌더링)인지 검사한다.
        /// AI·길찾기 시스템이 비활성 청크의 엔티티를 건너뛰는 데 사용.
        /// </summary>
        public bool IsChunkActiveAtWorld(Vector2 worldPosition)
        {
            int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int chunkY = Mathf.FloorToInt(worldPosition.y / chunkSize);
            return IsChunkActive(new Vector2Int(chunkX, chunkY));
        }

        public bool TryGetActiveChunkSpawnRuntime(Vector2Int chunkCoord, out ChunkSpawnRuntimeData spawnRuntime)
        {
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk) &&
                chunk.runtimeState == MapChunkRuntimeState.Active &&
                _renderedChunks.Contains(chunkCoord))
            {
                spawnRuntime = chunk.spawnRuntime;
                return true;
            }

            spawnRuntime = null;
            return false;
        }

        public bool TryGetChunkSpawnRuntime(Vector2Int chunkCoord, int generationId,
            out ChunkSpawnRuntimeData spawnRuntime)
        {
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk) &&
                chunk.runtimeState != MapChunkRuntimeState.Expired &&
                chunk.spawnRuntime.GenerationId == generationId)
            {
                spawnRuntime = chunk.spawnRuntime;
                return true;
            }

            spawnRuntime = null;
            return false;
        }

        public bool IsChunkGenerationActive(Vector2Int chunkCoord, int generationId)
        {
            return _activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk) &&
                   chunk.runtimeState == MapChunkRuntimeState.Active &&
                   chunk.spawnRuntime.GenerationId == generationId &&
                   _renderedChunks.Contains(chunkCoord);
        }

        public Vector2 GetMonsterSpawnSlotWorldPosition(Vector2Int chunkCoord, Vector2Int localPosition)
        {
            return new Vector2(
                chunkCoord.x * chunkSize + localPosition.x,
                chunkCoord.y * chunkSize + localPosition.y);
        }

        public bool IsMonsterSpawnSlotTerrainValid(Vector2Int chunkCoord, int generationId, int slotIndex,
            bool disableWhenBlocked)
        {
            if (TryGetChunkSpawnRuntime(chunkCoord, generationId, out ChunkSpawnRuntimeData runtime) == false ||
                slotIndex < 0 || slotIndex >= runtime.Slots.Length)
            {
                return false;
            }

            MonsterSpawnSlotState slot = runtime.Slots[slotIndex];
            bool isValid = IsMonsterSpawnLocalPositionValid(chunkCoord, generationId, slot.LocalPosition);
            if (isValid == false && disableWhenBlocked)
            {
                slot.IsDisabled = true;
                slot.CancelPending();
            }

            return isValid;
        }

        public bool IsMonsterSpawnWorldPositionValid(Vector2Int chunkCoord, int generationId, Vector2 worldPosition)
        {
            int worldTileX = Mathf.FloorToInt(worldPosition.x);
            int worldTileY = Mathf.FloorToInt(worldPosition.y);
            int positionChunkX = Mathf.FloorToInt((float)worldTileX / chunkSize);
            int positionChunkY = Mathf.FloorToInt((float)worldTileY / chunkSize);
            if (positionChunkX != chunkCoord.x || positionChunkY != chunkCoord.y)
                return false;

            Vector2Int localPosition = new Vector2Int(
                worldTileX - chunkCoord.x * chunkSize,
                worldTileY - chunkCoord.y * chunkSize);
            return IsMonsterSpawnLocalPositionValid(chunkCoord, generationId, localPosition);
        }

        public bool IsMonsterSpawnAnchorSeparated(Vector2 anchorWorld, float minimumDistance,
            Vector2Int sourceChunkCoord, int sourceGenerationId, int sourceSlotIndex)
        {
            float minimumDistanceSqr = minimumDistance * minimumDistance;
            for (int i = 0; i < _loadedChunkCoords.Count; i++)
            {
                Vector2Int chunkCoord = _loadedChunkCoords[i];
                MapChunkData chunk = _activeChunks[chunkCoord];
                if (chunk.runtimeState == MapChunkRuntimeState.Expired)
                    continue;

                MonsterSpawnSlotState[] slots = chunk.spawnRuntime.Slots;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    MonsterSpawnSlotState slot = slots[slotIndex];
                    if (slot.AliveMemberCount <= 0 && slot.IsPending == false)
                        continue;
                    if (chunkCoord == sourceChunkCoord &&
                        chunk.spawnRuntime.GenerationId == sourceGenerationId &&
                        slotIndex == sourceSlotIndex)
                    {
                        continue;
                    }

                    Vector2 otherAnchor = GetMonsterSpawnSlotWorldPosition(chunkCoord, slot.LocalPosition);
                    if ((otherAnchor - anchorWorld).sqrMagnitude < minimumDistanceSqr)
                        return false;
                }
            }

            return true;
        }

        private bool IsMonsterSpawnLocalPositionValid(Vector2Int chunkCoord, int generationId,
            Vector2Int localPosition)
        {
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk) == false ||
                chunk.runtimeState == MapChunkRuntimeState.Expired ||
                chunk.spawnRuntime.GenerationId != generationId)
            {
                return false;
            }
            if (localPosition.x < 0 || localPosition.x >= chunkSize ||
                localPosition.y < 0 || localPosition.y >= chunkSize)
            {
                return false;
            }

            if (chunk.villageSpawnExclusion[localPosition.x, localPosition.y] == true)
                return false;

            ulong tileData = chunk.tiles[localPosition.x, localPosition.y];
            if ((tileData & (ulong)GlobalEnum.TileFlag.Hill) != 0 ||
                (tileData & (ulong)GlobalEnum.TileFlag.Blocked) != 0 ||
                (tileData & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) != 0)
            {
                return false;
            }

            int worldTileX = chunkCoord.x * chunkSize + localPosition.x;
            int worldTileY = chunkCoord.y * chunkSize + localPosition.y;
            return AR.s.Building == null || AR.s.Building.IsTileOccupied(worldTileX, worldTileY) == false;
        }

        /// <summary>
        /// 청크의 Zone 번호 조회. 시드 청크 (0,0) = Zone 1, Chebyshev 거리 + 1, [1,99] 클램프.
        /// 활성 청크면 캐시 값, 아니면 좌표로 즉시 계산.
        /// </summary>
        public int GetZone(Vector2Int chunkCoord)
        {
            if (_activeChunks.TryGetValue(chunkCoord, out var chunk))
                return chunk.zone;

            int dist = Mathf.Max(Mathf.Abs(chunkCoord.x), Mathf.Abs(chunkCoord.y));
            return Mathf.Clamp(dist + 1, 1, 99);
        }

        /// <summary>
        /// 월드 좌표가 속한 청크의 Zone 번호. 규칙은 GetZone과 같다.
        /// </summary>
        public int GetZoneAtWorld(Vector2 worldPosition)
        {
            int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int chunkY = Mathf.FloorToInt(worldPosition.y / chunkSize);
            return GetZone(new Vector2Int(chunkX, chunkY));
        }

        /// <summary>
        /// 청크의 대표 biome 조회. MAP_DESIGN.md §6.5.2.
        /// 활성 청크면 캐시 값(`chunk.chunkBiome`), 아니면 좌표로 즉시 계산 — 둘 다 결정론.
        /// </summary>
        public GlobalEnum.BiomeType GetChunkBiome(Vector2Int chunkCoord)
        {
            if (_activeChunks != null && _activeChunks.TryGetValue(chunkCoord, out var chunk))
                return chunk.chunkBiome;

            return BiomeNoise.ComputeChunkBiome(chunkCoord.x, chunkCoord.y, chunkSize, mapSeed);
        }

        /// <summary>
        /// region의 biome histogram 조회 (lazy compute + cache). v1.9 cluster-grained 정책.
        /// race/settlement 결정 등에서 DominantBiome 또는 PickWeightedBiome 사용.
        /// </summary>
        public RegionData GetOrCreateRegionData(Vector2Int regionCoord)
        {
            if (_regionData.TryGetValue(regionCoord, out RegionData cached))
                return cached;

            RegionData fresh = RegionData.Compute(regionCoord, chunkSize, mapSeed);
            _regionData[regionCoord] = fresh;
            return fresh;
        }

        /// <summary>청크 좌표 기반 편의 함수 — region 변환 후 GetOrCreateRegionData 호출.</summary>
        public RegionData GetRegionDataForChunk(Vector2Int chunkCoord)
        {
            return GetOrCreateRegionData(RegionCoord.ChunkToRegion(chunkCoord));
        }

        /// <summary>
        /// 타일 좌표 기준 통행 차단 여부. Blocked 비트 + 건물 footprint 동시 검사.
        /// 충돌 시스템(System_Render) 위치 적분 단계에서 호출.
        /// </summary>
        public bool IsTileBlocked(int worldTileX, int worldTileY)
        {
            if (IsWorldTileInBounds(worldTileX, worldTileY) == false)
                return true;

            ulong tile = GetTileAt(worldTileX, worldTileY);
            if ((tile & (ulong)GlobalEnum.TileFlag.Blocked) != 0)
                return true;

            if (Context.Kind == MapKind.World && AR.s.Building != null && AR.s.Building.IsTileOccupied(worldTileX, worldTileY) == true)
                return true;

            return false;
        }

        /// <summary>
        /// 시야 판정용 막힘 종류 (FOG_OF_WAR_DESIGN §4). IsTileBlocked와 같은 규칙을 벽(맵 밖·타일 Blocked 비트)과
        /// 시설(월드의 건물 점유 칸)로 나눈다. 벽 뒤와 시설 뒤는 서로 다른 밝기의 반투명 그림자로 그린다.
        /// </summary>
        public VisionBlocker GetVisionBlocker(int worldTileX, int worldTileY)
        {
            if (IsWorldTileInBounds(worldTileX, worldTileY) == false)
                return VisionBlocker.Wall;

            ulong tile = GetTileAt(worldTileX, worldTileY);
            if ((tile & (ulong)GlobalEnum.TileFlag.Blocked) != 0)
                return VisionBlocker.Wall;

            if (Context.Kind == MapKind.World && AR.s.Building != null && AR.s.Building.IsTileOccupied(worldTileX, worldTileY) == true)
                return VisionBlocker.Facility;

            return VisionBlocker.None;
        }

        public bool IsWalkable(Vector3 worldPosition)
        {
            Vector3Int cellPos = _tileMap.WorldToCell(worldPosition);
            if (IsWorldTileInBounds(cellPos.x, cellPos.y) == false)
                return false;

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
            return PlaceObjectInternal(worldX, worldY, objectId, isUnderConstruction,
                out _, -1, -1);
        }

        /// <summary>
        /// PlaceObject의 out 변형. SpawnType=Entity면 생성된 빌딩 EntityId를 반환 (Tile 경로는 -1).
        /// </summary>
        public bool PlaceObject(int worldX, int worldY, int objectId, bool isUnderConstruction, out int buildingEntityId)
        {
            return PlaceObjectInternal(worldX, worldY, objectId, isUnderConstruction,
                out buildingEntityId, -1, -1);
        }

        /// <summary>
        /// VillageData 복원용 Entity 배치. 소속 마을과 건설 진행 HP를 BuildingSaveData 생성 전에 전달한다.
        /// Tile 경로에서는 villageId/currentHp가 무시된다.
        /// </summary>
        public bool PlaceObject(int worldX, int worldY, int objectId,
            bool isUnderConstruction, int villageId, int currentHp,
            out int buildingEntityId)
        {
            return PlaceObjectInternal(worldX, worldY, objectId, isUnderConstruction,
                out buildingEntityId, villageId, currentHp);
        }

        private bool PlaceObjectInternal(int worldX, int worldY, int objectId,
            bool isUnderConstruction, out int buildingEntityId, int villageId,
            int currentHp)
        {
            buildingEntityId = -1;
            if (Context.Kind == MapKind.Dungeon) return false;
            if (objectId <= 0)
                return false;
            if (IsWorldTileInBounds(worldX, worldY) == false)
            {
                Debug.LogWarning($"[MapManager] PlaceObject - Tile ({worldX}, {worldY}) is out of bounds.");
                return false;
            }

            // SpawnType=Entity면 타일 비트 무변경, BuildingManager에 위임
            Tables.BuildableItemTable table = AR.s.Data.GetBuildableItem(objectId);
            if (table != null && IsWorldTileFootprintInBounds(worldX, worldY, table.Size_Width, table.Size_Height) == false)
            {
                Debug.LogWarning($"[MapManager] PlaceObject - Footprint ({worldX}, {worldY}, {table.Size_Width}, {table.Size_Height}) is out of bounds.");
                return false;
            }
            if (table != null && table.SpawnType == GlobalEnum.BuildableSpawnType.Entity)
            {
                // AR 프리팹에 BuildingManager 컴포넌트 연결이 누락되면 NullReference 방지
                if (AR.s.Building == null)
                {
                    Debug.LogError($"[MapManager] PlaceObject - AR.s.Building is null. AR 프리팹에 BuildingManager 컴포넌트 연결 확인 필요. objectId={objectId}");
                    return false;
                }
                int resolvedVillageId = villageId >= 0
                    ? villageId
                    : (AR.s.Village != null
                        ? AR.s.Village.FindVillageContaining(worldX, worldY)
                        : -1);
                int entityId = AR.s.Building.PlaceBuilding(worldX, worldY, objectId,
                    resolvedVillageId, isUnderConstruction, currentHp);
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

            // Active/Warm 보존 청크에 즉시 반영. Warm은 재진입 시 재생성하지 않는다.
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk))
            {
                // 타일 데이터 갱신
                ulong currentTile = chunk.tiles[localX, localY];
                currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile |= ((ulong)objectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile |= (ulong)GlobalEnum.TileFlag.Blocked;
                currentTile &= ~(ulong)GlobalEnum.TileFlag.MonsterSpawn;
                chunk.tiles[localX, localY] = currentTile;
                DisableMonsterSpawnSlotAtLocal(chunk, new Vector2Int(localX, localY));
                // 막힌 칸이 생겼다 (벽·닫힌 문). 시야를 다시 계산한다 (FOG_OF_WAR_DESIGN §4)
                if (AR.s != null) AR.s.Fog.MarkVisionDirty();

                if (_renderedChunks.Contains(chunkCoord))
                {
                    RenderSingleObjectTile(worldX, worldY, (ulong)objectId);
                }
            }

            return true;
        }

        /// <summary>
        /// 월드 좌표의 플레이어 배치 오브젝트를 제거한다.
        /// </summary>
        public bool RemoveObject(int worldX, int worldY)
        {
            if (IsWorldTileInBounds(worldX, worldY) == false)
            {
                Debug.LogWarning($"[MapManager] RemoveObject - Tile ({worldX}, {worldY}) is out of bounds.");
                return false;
            }

            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / chunkSize);
            int localX = worldX - (chunkX * chunkSize);
            int localY = worldY - (chunkY * chunkSize);

            if (localX < 0) { chunkX--; localX += chunkSize; }
            if (localY < 0) { chunkY--; localY += chunkSize; }

            Vector2Int chunkCoord = new Vector2Int(chunkX, chunkY);

            // 수정 기록 저장
            SetTileModification(chunkCoord, localX, localY, 0, true);

            // Active/Warm 보존 청크에 즉시 반영.
            if (_activeChunks.TryGetValue(chunkCoord, out MapChunkData chunk))
            {
                ulong currentTile = chunk.tiles[localX, localY];
                currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                currentTile &= ~(ulong)GlobalEnum.TileFlag.Blocked;
                chunk.tiles[localX, localY] = currentTile;
                // 막힌 칸이 열렸다 (문 열기·벽 파괴). 시야를 다시 계산한다 (FOG_OF_WAR_DESIGN §4)
                if (AR.s != null) AR.s.Fog.MarkVisionDirty();

                if (_renderedChunks.Contains(chunkCoord))
                {
                    RenderSingleObjectTile(worldX, worldY, 0);
                }
            }

            return true;
        }

        private void SetTileModification(Vector2Int chunkCoord, int localX, int localY, int objectId, bool isRemoval)
        {
            if (AR.s?.Data != null)
            {
                if (ReferenceEquals(this, AR.s.WorldMap) == true)
                    AR.s.Data.MarkMapChunkSaveDirty(new ARPG.Data.DataManager.Vector2IntKey(chunkCoord.x, chunkCoord.y));
                else if (AR.s.Dungeon.IsInside == true)
                    AR.s.Data.MarkDungeonSaveDirty(AR.s.Dungeon.ActiveRecord, ARPG.Data.DungeonSaveGroup.Tiles);
            }
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

        private static void DisableMonsterSpawnSlotAtLocal(MapChunkData chunk, Vector2Int localPosition)
        {
            MonsterSpawnSlotState[] slots = chunk.spawnRuntime.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].LocalPosition != localPosition)
                    continue;

                slots[i].IsDisabled = true;
                slots[i].CancelPending();
                return;
            }
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

        public ChunkModificationData GetSaveChunkModifications(int x, int y)
        {
            return _tileModifications.TryGetValue(new Vector2Int(x, y), out var modifications) == true
                ? new ChunkModificationData { ChunkX = x, ChunkY = y, Modifications = modifications } : null;
        }

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


