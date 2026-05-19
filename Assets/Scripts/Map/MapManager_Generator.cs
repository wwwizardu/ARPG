using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        private List<Vector2Int> _candidateSpawnPositions = new List<Vector2Int>();

        public static ulong CombineTileData(ulong currentTile, GlobalEnum.TileType inBaseTileType, uint inHillFlag, uint inMonsterSpawnFlag, ulong inObjectId = 0)
        {
            // 기존 타일 데이터에서 수정할 비트들을 제외한 나머지 비트 유지
            // GroundLayerMask(0-9비트), ObjectLayerMask(10-19비트), Hill(20비트), MonsterSpawn(21비트)를 제거
            ulong preservedBits = currentTile & ~((ulong)GlobalEnum.TileFlag.GroundLayerMask |
                                                  (ulong)GlobalEnum.TileFlag.ObjectLayerMask |
                                                  (ulong)GlobalEnum.TileFlag.Hill |
                                                  (ulong)GlobalEnum.TileFlag.MonsterSpawn);

            // 새로운 값 설정: 바닥 타입(0-9비트) + 오브젝트 ID(10-19비트) + Hill 플래그 + MonsterSpawn 플래그
            return preservedBits |
                   ((ulong)inBaseTileType & (ulong)GlobalEnum.TileFlag.GroundLayerMask) |
                   ((inObjectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) |
                   inHillFlag |
                   inMonsterSpawnFlag;
        }

        private void InitializeChunkPool()
        {
            for (int i = 0; i < POOL_SIZE; i++)
            {
                _chunkPool.Push(new MapChunkData(chunkSize));
            }

            LoadFixedMapFiles();
        }

        private MapChunkData GetOrCreateChunk(int chunkX, int chunkY)
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
            chunk.monsterSpawnPositions.Clear();

            // Zone = max(|cx|, |cy|) + 1, [1, 99] 클램프. 시드 청크 (0,0)이 Zone 1.
            int dist = Mathf.Max(Mathf.Abs(chunk.chunkX), Mathf.Abs(chunk.chunkY));
            chunk.zone = Mathf.Clamp(dist + 1, 1, 99);

            // v1.9 cluster-grained biome — 청크 내 cluster마다 별도 biome 결정.
            // 청크 8×8 / cluster 2×2 → 4×4 = 16 cluster per chunk. 시각 자연스러움 + race는 region histogram으로.
            int clustersPerSide = chunkSize / BiomeNoise.CLUSTER_SIZE;
            int clusterCount = clustersPerSide * clustersPerSide;
            if (chunk.clusterBiomes == null || chunk.clusterBiomes.Length != clusterCount)
                chunk.clusterBiomes = new GlobalEnum.BiomeType[clusterCount];

            for (int cx = 0; cx < clustersPerSide; cx++)
            {
                for (int cy = 0; cy < clustersPerSide; cy++)
                {
                    int globalClusterX = chunk.chunkX * clustersPerSide + cx;
                    int globalClusterY = chunk.chunkY * clustersPerSide + cy;
                    chunk.clusterBiomes[cy * clustersPerSide + cx] = BiomeNoise.ComputeClusterBiome(globalClusterX, globalClusterY, mapSeed);
                }
            }

            // 청크 대표 biome = 청크 중심 cluster의 biome (MonsterManager 풀 선택 등 legacy 1-biome caller용).
            chunk.chunkBiome = BiomeNoise.ComputeChunkBiome(chunk.chunkX, chunk.chunkY, chunkSize, mapSeed);

            // 몬스터 스폰 후보 위치 임시 저장
            _candidateSpawnPositions.Clear();

            // 1. 타일 데이터 생성 — cluster 단위 biome 적용 (cluster당 BiomeTable 룩업 1회 캐시).
            //    셀이 속한 cluster의 biome으로 baseTileType/hillThreshold/spawnRate 결정.
            GlobalEnum.TileType[] clusterBaseTileTypes = new GlobalEnum.TileType[clusterCount];
            float[] clusterHillThresholds = new float[clusterCount];
            float[] clusterSpawnRates = new float[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                GlobalEnum.BiomeType cb = chunk.clusterBiomes[i];
                clusterBaseTileTypes[i] = BiomeTable.GetGroundTileType(cb);
                clusterHillThresholds[i] = BiomeTable.GetHillThreshold(cb);
                clusterSpawnRates[i] = _monsterSpawnRate * BiomeTable.GetMonsterSpawnMultiplier(cb);
            }

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;

                    int clusterIdx = (y / BiomeNoise.CLUSTER_SIZE) * clustersPerSide + (x / BiomeNoise.CLUSTER_SIZE);
                    GlobalEnum.TileType baseTileType = clusterBaseTileTypes[clusterIdx];
                    float hillThreshold = clusterHillThresholds[clusterIdx];
                    float spawnRate = clusterSpawnRates[clusterIdx];

                    // Hill 비트 — cluster biome별 임계값. elevation 노이즈는 *변주*용으로 유지 (Hill 분포 결정)
                    float elevationNoise = Mathf.PerlinNoise(
                        (worldX + mapSeed) * (noiseScale * 0.3f),
                        (worldY + mapSeed) * (noiseScale * 0.3f)
                    );
                    uint hillFlag = 0;
                    if (elevationNoise > hillThreshold)
                        hillFlag = (uint)GlobalEnum.TileFlag.Hill;

                    // 몬스터 스폰 후보 — cluster biome 보정 곱하기
                    uint monsterSpawnFlag = 0;
                    if (hillFlag == 0 && _randomGenerator.NextDouble() < spawnRate)
                    {
                        monsterSpawnFlag = (uint)GlobalEnum.TileFlag.MonsterSpawn;
                        _candidateSpawnPositions.Add(new Vector2Int(x, y));
                    }

                    chunk.tiles[x, y] = CombineTileData(0UL, baseTileType, hillFlag, monsterSpawnFlag);
                }
            }

            // 2. MapFileData로 타일 데이터 덮어쓰기
            OverlayMapFileData(chunk);

            // 3. 플레이어 배치 오브젝트 오버레이
            OverlayTileModifications(chunk);

            // 4. 후보 위치 중 최종적으로 몬스터 스폰 플래그가 유지된 위치만 수집
            foreach (var pos in _candidateSpawnPositions)
            {
                ulong tileData = chunk.tiles[pos.x, pos.y];
                if ((tileData & (uint)GlobalEnum.TileFlag.MonsterSpawn) != 0)
                {
                    chunk.monsterSpawnPositions.Add(pos);
                }
            }
        }

        private void OverlayMapFileData(MapChunkData chunk)
        {
            foreach (var mapFileEntry in _mapFileDataDic)
            {
                Vector2Int mapFileChunkPos = mapFileEntry.Key;
                MapFileData mapFileData = mapFileEntry.Value;

                // 현재 청크와 MapFileData의 위치 관계 확인
                Vector2Int chunkWorldStart = new Vector2Int(chunk.chunkX * chunkSize, chunk.chunkY * chunkSize);
                Vector2Int chunkWorldEnd = new Vector2Int(chunkWorldStart.x + chunkSize - 1, chunkWorldStart.y + chunkSize - 1);

                Vector2Int mapFileWorldStart = mapFileData.StartPosition;
                Vector2Int mapFileWorldEnd = new Vector2Int(mapFileWorldStart.x + mapFileData.Width - 1, mapFileWorldStart.y + mapFileData.Height - 1);

                // 겹치는 영역이 있는지 확인
                if (chunkWorldEnd.x < mapFileWorldStart.x || chunkWorldStart.x > mapFileWorldEnd.x ||
                    chunkWorldEnd.y < mapFileWorldStart.y || chunkWorldStart.y > mapFileWorldEnd.y)
                {
                    continue; // 겹치는 영역이 없음
                }

                // 겹치는 영역의 범위 계산
                int overlapStartX = Mathf.Max(chunkWorldStart.x, mapFileWorldStart.x);
                int overlapEndX = Mathf.Min(chunkWorldEnd.x, mapFileWorldEnd.x);
                int overlapStartY = Mathf.Max(chunkWorldStart.y, mapFileWorldStart.y);
                int overlapEndY = Mathf.Min(chunkWorldEnd.y, mapFileWorldEnd.y);

                // 겹치는 영역에 대해 MapFileData의 타일 데이터로 덮어쓰기
                for (int worldX = overlapStartX; worldX <= overlapEndX; worldX++)
                {
                    for (int worldY = overlapStartY; worldY <= overlapEndY; worldY++)
                    {
                        // 청크 내 로컬 좌표
                        int chunkLocalX = worldX - chunkWorldStart.x;
                        int chunkLocalY = worldY - chunkWorldStart.y;

                        // MapFileData 내 로컬 좌표
                        int mapFileLocalX = worldX - mapFileWorldStart.x;
                        int mapFileLocalY = worldY - mapFileWorldStart.y;

                        // MapFileData에서 타일 데이터 가져와서 청크에 덮어쓰기
                        ulong mapTileData = mapFileData.GetTile(mapFileLocalX, mapFileLocalY);
                        if (mapTileData != 0) // 0이 아닌 경우에만 덮어쓰기
                        {
                            chunk.tiles[chunkLocalX, chunkLocalY] = mapTileData;
                        }
                    }
                }
            }
        }

        private void OverlayTileModifications(MapChunkData chunk)
        {
            Vector2Int chunkCoord = new Vector2Int(chunk.chunkX, chunk.chunkY);
            if (_tileModifications.TryGetValue(chunkCoord, out List<TileModification> modifications) == false)
                return;

            for (int i = 0; i < modifications.Count; i++)
            {
                TileModification mod = modifications[i];
                ulong currentTile = chunk.tiles[mod.LocalX, mod.LocalY];

                if (mod.IsRemoval)
                {
                    // 오브젝트 비트 및 Blocked 플래그 클리어
                    currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                    currentTile &= ~(ulong)GlobalEnum.TileFlag.Blocked;
                }
                else
                {
                    // 기존 오브젝트 비트 클리어 후 새 오브젝트 설정
                    currentTile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                    currentTile |= ((ulong)mod.ObjectId << 10) & (ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                    currentTile |= (ulong)GlobalEnum.TileFlag.Blocked;
                    currentTile &= ~(ulong)GlobalEnum.TileFlag.MonsterSpawn;
                }

                chunk.tiles[mod.LocalX, mod.LocalY] = currentTile;
            }
        }

        private void ReturnChunkToPool(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            if (_activeChunks.ContainsKey(chunkKey))
            {
                MapChunkData chunk = _activeChunks[chunkKey];
                _activeChunks.Remove(chunkKey);

                if (_renderedChunks.Remove(chunkKey))
                {
                    RemoveChunkFromTilemap(chunk); // 타일맵에서 청크 제거
                }

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
                Vector2Int chunkCoord = new Vector2Int(chunk.chunkX, chunk.chunkY);
                if (_renderedChunks.Contains(chunkCoord))
                {
                    RemoveChunkFromTilemap(chunk);
                }

                chunk.Deactivate();
                _chunkPool.Push(chunk);
            }
            _activeChunks.Clear();
            _renderedChunks.Clear();
            _currentPlayerChunk = InvalidChunk;
        }

        private Vector2Int WorldPositionToChunk(Vector3 worldPosition)
        {
            int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int chunkY = Mathf.FloorToInt(worldPosition.y / chunkSize);
            return new Vector2Int(chunkX, chunkY);
        }

        private void LoadChunksAroundPlayer()
        {
            for (int x = -_loadRadius; x <= _loadRadius; x++)
            {
                for (int y = -_loadRadius; y <= _loadRadius; y++)
                {
                    Vector2Int chunkPos = new Vector2Int(_currentPlayerChunk.x + x, _currentPlayerChunk.y + y);

                    MapChunkData chunk;
                    if (_activeChunks.TryGetValue(chunkPos, out chunk) == false)
                    {
                        chunk = GetOrCreateChunk(chunkPos.x, chunkPos.y);
                    }

                    if (chunk != null && _renderedChunks.Contains(chunkPos) == false)
                    {
                        RenderChunkToTilemap(chunk);
                        _renderedChunks.Add(chunkPos);
                        OnChunkActivated(chunkPos, chunk);
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
                int deltaX = Mathf.Abs(chunkPos.x - _currentPlayerChunk.x);
                int deltaY = Mathf.Abs(chunkPos.y - _currentPlayerChunk.y);
                int maxDistance = Mathf.Max(deltaX, deltaY); // Chebyshev 거리

                if (maxDistance > _loadRadius)
                {
                    chunksToReturn.Add(chunkPos);
                }
            }

            foreach (var chunkPos in chunksToReturn)
            {
                if (_renderedChunks.Contains(chunkPos))
                {
                    OnChunkDeactivated(chunkPos);
                }

                ReturnChunkToPool(chunkPos.x, chunkPos.y);
            }
        }

        public int GetActiveChunkCount()
        {
            return _renderedChunks.Count;
        }

        public int GetPooledChunkCount()
        {
            return _chunkPool.Count;
        }

        public List<Vector2Int> GetActiveChunkPositions()
        {
            return new List<Vector2Int>(_renderedChunks);
        }

        public List<MapFileData> GetMapFileDataByType(MapType mapType)
        {
            List<MapFileData> result = new List<MapFileData>();
            foreach (var pair in _mapFileDataDic)
            {
                if (pair.Value.MapType == mapType)
                {
                    result.Add(pair.Value);
                }
            }
            return result;
        }

        private void LoadFixedMapFiles()
        {
            _mapFileDataDic.Clear();

            string filePath = Path.Combine(Application.dataPath, "_BinaryData", "TilemapData", "BaseTown.bytes");

            if (File.Exists(filePath))
            {
                try
                {
                    using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    using (BinaryReader reader = new BinaryReader(fileStream))
                    {
                        MapFileData mapFileData = MapFileData.ReadFromBinary(reader);

                        Vector2Int mapKey = mapFileData.StartPosition;

                        if (_mapFileDataDic.ContainsKey(mapKey))
                        {
                            Debug.LogWarning($"[MapManager] MapFileData already exists for key {mapKey}. Overwriting with BaseTown.bytes data.");
                        }

                        _mapFileDataDic[mapKey] = mapFileData;

                        Debug.Log($"[MapManager] Loaded BaseTown.bytes - Size: {mapFileData.Width}x{mapFileData.Height}, Start: {mapFileData.StartPosition}, MapType: {mapFileData.MapType}, MapKey: {mapKey}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MapManager] Failed to load BaseTown.bytes: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[MapManager] BaseTown.bytes not found at path: {filePath}");
            }
        }
    }
}


