using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {


        private void InitializeChunkPool()
        {
            for (int i = 0; i < POOL_SIZE; i++)
            {
                _chunkPool.Push(new MapChunkData(chunkSize));
            }
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
                    
                    uint currentTile = chunk.tiles[x, y];
                    chunk.tiles[x, y] = (currentTile & 0xFFFFFFF0) | ((uint)tileType & 0x0000000F);
                }
            }
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
        
        public GlobalEnum.TileType GetTileTypeAt(int worldX, int worldY)
        {
            uint tileData = GetTileAt(worldX, worldY);
            if (tileData == 0) return GlobalEnum.TileType.Ground; // 기본값
            
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
                        MapChunkData chunk = GetOrCreateChunk(chunkPos.x, chunkPos.y);
                        if (chunk != null)
                        {
                            RenderChunkToTilemap(chunk);
                        }
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


