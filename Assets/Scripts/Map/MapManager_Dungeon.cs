using System.Collections.Generic;
using ARPG.Data;
using ARPG.Dungeon;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public partial class MapManager
    {
        private readonly List<MapFileData> _placementBlueprints = new();
        public bool CanCreateDungeonMap => _tileMap != null && _tileMap_Hill != null && _tileMap_Object != null;

        public MapManager CreateDungeonMap(DungeonRecord record)
        {
            if (CanCreateDungeonMap == false) throw new System.InvalidOperationException("Missing dungeon tilemap templates.");
            var root = new GameObject("DungeonMap", typeof(Grid));
            root.transform.SetParent(transform, false);
            var map = root.AddComponent<MapManager>();
            map.Context = new MapContext(MapKind.Dungeon, record);
            map.chunkSize = chunkSize;
            map.mapSeed = record.Seed;
            map.minChunkX = -2;
            map.minChunkY = -2;
            map.maxChunkX = 16;
            map.maxChunkY = 16;
            map._themeTileSet = _themeTileSet;
            map._themeEntries = _themeEntries;
            map._groundTileSet = _groundTileSet;
            map._ruleTile_Hill = _ruleTile_Hill;
            map._ruleTile_Object = _ruleTile_Object;
            map._tileMap = CloneEmptyTilemap(_tileMap, root.transform);
            map._tileMap_Hill = CloneEmptyTilemap(_tileMap_Hill, root.transform);
            map._tileMap_Object = CloneEmptyTilemap(_tileMap_Object, root.transform);
            map.Initialize();
            map.LoadTileModifications(record.TileModifications);
            return map;
        }

        public void SuspendWorld()
        {
            Context.IsActive = false;
            Context.Epoch++;
            for (int i = _renderedChunkCoords.Count - 1; i >= 0; i--)
            {
                var coord = _renderedChunkCoords[i];
                _activeChunks[coord].Warm(Time.time);
                OnChunkDeactivated(coord);
                RemoveChunkFromTilemap(_activeChunks[coord]);
            }
            _renderedChunks.Clear();
            _renderedChunkCoords.Clear();
            _currentPlayerChunk = InvalidChunk;
        }

        public void ResumeWorld(Vector3 position)
        {
            Context.IsActive = true;
            Context.Epoch++;
            UpdateChunksAroundPlayer(position);
        }

        public bool IsOutsideBlueprints(int x, int y, int margin)
        {
            if (_placementBlueprints.Count != _mapFileDataDic.Count)
            {
                _placementBlueprints.Clear();
                _placementBlueprints.AddRange(_mapFileDataDic.Values);
            }
            for (int i = 0; i < _placementBlueprints.Count; i++)
            {
                var b = _placementBlueprints[i];
                if (x >= b.StartPosition.x - margin && x < b.StartPosition.x + b.Width + margin &&
                    y >= b.StartPosition.y - margin && y < b.StartPosition.y + b.Height + margin)
                    return false;
            }
            return true;
        }

        /// <summary>Pure terrain sampling for placement: no chunk allocation or monster RNG consumption.</summary>
        public bool IsEntranceTileClear(int x, int y)
        {
            if (IsWorldTileInBounds(x, y) == false || IsOutsideBlueprints(x, y, 1) == false)
                return false;
            var biome = BiomeNoise.ComputeClusterBiome(Mathf.FloorToInt((float)x / BiomeNoise.CLUSTER_SIZE),
                Mathf.FloorToInt((float)y / BiomeNoise.CLUSTER_SIZE), mapSeed);
            float elevation = Mathf.PerlinNoise((x + mapSeed) * noiseScale * 0.3f,
                (y + mapSeed) * noiseScale * 0.3f);
            if (elevation > BiomeTable.GetHillThreshold(biome)) return false;
            var coord = new Vector2Int(Mathf.FloorToInt((float)x / chunkSize), Mathf.FloorToInt((float)y / chunkSize));
            if (_tileModifications.TryGetValue(coord, out var mods) == true)
                for (int i = 0; i < mods.Count; i++)
                    if (mods[i].LocalX == x - coord.x * chunkSize && mods[i].LocalY == y - coord.y * chunkSize &&
                        mods[i].IsRemoval == false && mods[i].ObjectId > 0) return false;
            return AR.s.Building.IsTileOccupied(x, y) == false;
        }

        private static Tilemap CloneEmptyTilemap(Tilemap source, Transform parent)
        {
            var copy = Instantiate(source, parent);
            copy.name = "Dungeon_" + source.name;
            copy.ClearAllTiles();
            return copy;
        }

        private void GenerateDungeonChunk(MapChunkData chunk)
        {
            chunk.zone = 1;
            chunk.chunkBiome = GlobalEnum.BiomeType.Forest;
            for (int i = 0; i < chunk.clusterBiomes.Length; i++) chunk.clusterBiomes[i] = chunk.chunkBiome;
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    chunk.tiles[x, y] = DungeonLayout.IsFloor(Context.Dungeon,
                        chunk.chunkX * chunkSize + x, chunk.chunkY * chunkSize + y)
                        ? 0UL : (ulong)(GlobalEnum.TileFlag.Hill | GlobalEnum.TileFlag.Blocked);
        }

        private ulong SampleDungeonTile(int x, int y)
        {
            ulong tile = DungeonLayout.IsFloor(Context.Dungeon, x, y)
                ? 0UL : (ulong)(GlobalEnum.TileFlag.Hill | GlobalEnum.TileFlag.Blocked);
            var coord = new Vector2Int(Mathf.FloorToInt((float)x / chunkSize), Mathf.FloorToInt((float)y / chunkSize));
            if (_tileModifications.TryGetValue(coord, out var modifications) == true)
                for (int i = 0; i < modifications.Count; i++)
                {
                    var mod = modifications[i];
                    if (mod.LocalX != x - coord.x * chunkSize || mod.LocalY != y - coord.y * chunkSize) continue;
                    tile &= ~(ulong)GlobalEnum.TileFlag.ObjectLayerMask;
                    if (mod.IsRemoval == true) tile &= ~(ulong)GlobalEnum.TileFlag.Blocked;
                    else tile |= ((ulong)mod.ObjectId << 10) | (ulong)GlobalEnum.TileFlag.Blocked;
                }
            return tile;
        }

        private void LoadDungeonChunksBudgeted(int budget)
        {
            // Nearest first; only the bounded visible ring is rasterized, never the whole dungeon.
            for (int radius = 0; radius <= _loadRadius && budget > 0; radius++)
                for (int x = -radius; x <= radius && budget > 0; x++)
                    for (int y = -radius; y <= radius && budget > 0; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;
                        var coord = _currentPlayerChunk + new Vector2Int(x, y);
                        if (_renderedChunks.Contains(coord) == true) continue;
                        var chunk = GetOrCreateChunk(coord.x, coord.y);
                        if (chunk == null) continue;
                        chunk.Activate(Time.time);
                        RenderChunkToTilemap(chunk);
                        _renderedChunks.Add(coord);
                        _renderedChunkCoords.Add(coord);
                        budget--;
                    }
        }
    }
}
