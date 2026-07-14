using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        private List<Vector2Int> _candidateSpawnPositions = new List<Vector2Int>();

        public static ulong CombineTileData(ulong currentTile, uint inGroundTileId, uint inHillFlag, uint inMonsterSpawnFlag, ulong inObjectId = 0)
        {
            // 기존 타일 데이터에서 수정할 비트들을 제외한 나머지 비트 유지
            // GroundLayerMask(0-9비트), ObjectLayerMask(10-19비트), Hill(20비트), MonsterSpawn(21비트)를 제거
            ulong preservedBits = currentTile & ~((ulong)GlobalEnum.TileFlag.GroundLayerMask |
                                                  (ulong)GlobalEnum.TileFlag.ObjectLayerMask |
                                                  (ulong)GlobalEnum.TileFlag.Hill |
                                                  (ulong)GlobalEnum.TileFlag.MonsterSpawn);

            // 새로운 값 설정: GroundTileSet 인덱스(0-9비트) + 오브젝트 ID(10-19비트) + Hill 플래그 + MonsterSpawn 플래그
            return preservedBits |
                   ((ulong)inGroundTileId & (ulong)GlobalEnum.TileFlag.GroundLayerMask) |
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
            PlaceRaceSettlements();
        }

        /// <summary>
        /// Phase 2 Race Settlement 절차 배치 — MAP_DESIGN.md §7. 결정론 (같은 mapSeed → 같은 결과).
        /// 알고리즘: 2×2 region 블록당 최대 2개 + chunk-grain
        /// (region dominant biome → race → 호환 chunk 후보 풀 → hash 선택 → chunk 중심)
        /// 청사진: 명명 규칙 `BaseTown_<Race>.bytes` (e.g., BaseTown_Dwarf.bytes). 파일 없으면 LogError + skip.
        /// </summary>
        private void PlaceRaceSettlements()
        {
            var placements = RaceSettlementPlacer.ComputeAll(
                mapSeed, chunkSize, minChunkX, maxChunkX, minChunkY, maxChunkY);

            Debug.Log($"[MapManager] Race Settlement 계산 완료: {placements.Count}개 region에 배치");
            int appliedCount = 0;
            int skippedNoBlueprint = 0;
            int skippedOverlap = 0;
            int skippedOutOfBounds = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];

                // 1) race별 청사진 로드 (명명 규칙)
                MapFileData blueprint = LoadRaceBlueprint(p.Race);
                if (blueprint == null)
                {
                    skippedNoBlueprint++;
                    continue;   // LoadRaceBlueprint 내부에서 LogError 처리
                }

                // 2) 청사진 origin을 settlement 좌표로 이동 (chunk 중심 기준, 청사진 좌하단 = center - 사이즈/2)
                Vector2Int newOrigin = new Vector2Int(
                    p.WorldCoord.x - blueprint.Width / 2,
                    p.WorldCoord.y - blueprint.Height / 2);
                blueprint.SetStartPosition(newOrigin);

                if (IsWorldTileFootprintInBounds(newOrigin.x, newOrigin.y, blueprint.Width, blueprint.Height) == false)
                {
                    skippedOutOfBounds++;
                    Debug.LogWarning($"[MapManager] Settlement skip (out of bounds): region=({p.RegionCoord.x},{p.RegionCoord.y}) race={p.Race} origin=({newOrigin.x},{newOrigin.y}) size={blueprint.Width}x{blueprint.Height}");
                    continue;
                }

                // 3) 충돌 검사 — 기존 _mapFileDataDic 엔트리들과 bounding box 비교
                if (HasMapFileOverlap(blueprint))
                {
                    skippedOverlap++;
                    Debug.LogWarning($"[MapManager] Settlement skip (overlap): region=({p.RegionCoord.x},{p.RegionCoord.y}) race={p.Race} origin=({newOrigin.x},{newOrigin.y}) size={blueprint.Width}x{blueprint.Height}");
                    continue;
                }

                // 4) 등록 — 이후 OverlayMapFileData가 자동으로 청크에 적용
                _mapFileDataDic[newOrigin] = blueprint;
                _settlementIdByOrigin[newOrigin] = i;   // R1: placement 인덱스 = 연대기 SettlementId
                appliedCount++;
                Debug.Log($"  [Settlement] region=({p.RegionCoord.x},{p.RegionCoord.y}) " +
                          $"dominant={p.RegionDominantBiome} chunkBiome={p.ChunkBiome} " +
                          $"→ Race={p.Race} Size={p.Size} " +
                          $"chunk=({p.ChunkCoord.x},{p.ChunkCoord.y}) origin=({newOrigin.x},{newOrigin.y}) {blueprint.Width}x{blueprint.Height}");
            }

            Debug.Log($"[MapManager] Settlement 배치 결과: 적용={appliedCount} / 청사진없음={skippedNoBlueprint} / 경계스킵={skippedOutOfBounds} / 충돌스킵={skippedOverlap}");
        }

        /// <summary>
        /// 명명 규칙 `BaseTown_<Race>.bytes`로 청사진 로드. 파일 없거나 읽기 실패 시 LogError 후 null 반환.
        /// </summary>
        private MapFileData LoadRaceBlueprint(GlobalEnum.RaceType race)
        {
            string filename = $"BaseTown_{race}.bytes";
            string filePath = Path.Combine(Application.dataPath, "_BinaryData", "TilemapData", filename);

            if (File.Exists(filePath) == false)
            {
                Debug.LogError($"[MapManager] Race 청사진 파일 없음 — {filename} (race={race}). 경로: {filePath}. Race settlement이 배치되지 않음.");
                return null;
            }

            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    MapFileData blueprint = MapFileData.ReadFromBinary(reader);

                    // V4 (VILLAGE_REWORK_PLAN §7.5 1안, V0 선결): Dwarf 수직 슬라이스는
                    // 기능 없는 청사진 장식 경계(Id 1)를 제거하고 초기 Stage에 맞는 기능성
                    // Palisade/Gate(180/181)로 대체한다. .bytes 자산(MapFileSaver 산출물)은
                    // 수정하지 않고 로드 시 결정론적으로 제거한다. 다른 Race는 V6에서 처리.
                    if (race == GlobalEnum.RaceType.Dwarf && blueprint != null)
                    {
                        int stripped = StripBlueprintObjectId(blueprint,
                            DECORATIVE_BOUNDARY_OBJECT_ID);
                        if (stripped > 0)
                            Debug.Log($"[MapManager] {filename} 장식 경계 Id " +
                                $"{DECORATIVE_BOUNDARY_OBJECT_ID} 제거: {stripped}셀 (V4 벽 정책 1안)");
                    }
                    return blueprint;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MapManager] Race 청사진 로드 실패 — {filename}: {e.Message}");
                return null;
            }
        }

        // V0 기준선에서 확인된 청사진 시각 경계 오브젝트 (나무 벽, 기능 없음)
        private const int DECORATIVE_BOUNDARY_OBJECT_ID = 1;

        /// <summary>
        /// 청사진 타일 데이터에서 특정 ObjectId의 오브젝트 비트(및 Blocked)를 제거한다.
        /// ground 등 나머지 비트는 보존. 제거된 셀 수를 반환.
        /// </summary>
        private static int StripBlueprintObjectId(MapFileData blueprint, int objectId)
        {
            int stripped = 0;
            for (int x = 0; x < blueprint.Width; x++)
            {
                for (int y = 0; y < blueprint.Height; y++)
                {
                    ulong tile = blueprint.GetTile(x, y);
                    if (tile == 0)
                        continue;
                    int tileObjectId = (int)((tile
                        & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10);
                    if (tileObjectId != objectId)
                        continue;

                    tile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                    tile &= ~(ulong)GlobalEnum.TileFlag.Blocked;
                    blueprint.SetTile(x, y, tile);
                    stripped++;
                }
            }
            return stripped;
        }

        /// <summary>새 청사진과 기존 _mapFileDataDic 엔트리들 간 bounding box 충돌 검사.</summary>
        private bool HasMapFileOverlap(MapFileData candidate)
        {
            int candStartX = candidate.StartPosition.x;
            int candStartY = candidate.StartPosition.y;
            int candEndX = candStartX + candidate.Width - 1;
            int candEndY = candStartY + candidate.Height - 1;

            foreach (var pair in _mapFileDataDic)
            {
                MapFileData existing = pair.Value;
                int exStartX = existing.StartPosition.x;
                int exStartY = existing.StartPosition.y;
                int exEndX = exStartX + existing.Width - 1;
                int exEndY = exStartY + existing.Height - 1;

                bool overlaps =
                    candStartX <= exEndX && candEndX >= exStartX &&
                    candStartY <= exEndY && candEndY >= exStartY;
                if (overlaps)
                    return true;
            }
            return false;
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
            //    절차 생성 셀의 ground 비트는 0 (passthrough — 렌더러가 cluster biome.Ground로 직접 룩업).
            float[] clusterHillThresholds = new float[clusterCount];
            float[] clusterSpawnRates = new float[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                GlobalEnum.BiomeType cb = chunk.clusterBiomes[i];
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

                    chunk.tiles[x, y] = CombineTileData(0UL, 0u, hillFlag, monsterSpawnFlag);
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

        /// <summary>청사진 origin(StartPosition)에 연결된 연대기 SettlementId. 절차 정착지가 아니면 -1.</summary>
        public int GetSettlementIdByOrigin(Vector2Int origin)
            => _settlementIdByOrigin.TryGetValue(origin, out int id) ? id : -1;

        private void LoadFixedMapFiles()
        {
            _mapFileDataDic.Clear();
            _settlementIdByOrigin.Clear();

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


