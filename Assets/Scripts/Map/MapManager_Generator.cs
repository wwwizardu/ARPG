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
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;

                    // 고도 노이즈 (언덕의 높이를 나타냄)
                    float elevationNoise = Mathf.PerlinNoise(
                        (worldX + mapSeed) * (noiseScale * 0.3f),
                        (worldY + mapSeed) * (noiseScale * 0.3f)
                    );
                    
                    float terrainNoise = Mathf.PerlinNoise(
                        (worldX + mapSeed) * noiseScale,
                        (worldY + mapSeed) * noiseScale
                    );
                    
                    // 바닥 타입 결정 (하위 4비트)
                    GlobalEnum.TileType baseTileType;
                    if (terrainNoise > 0.4f)
                        baseTileType = GlobalEnum.TileType.Glass;
                    else
                        baseTileType = GlobalEnum.TileType.Ground;
                    
                    // 언덕 플래그 결정 (5번째 비트)
                    uint hillFlag = 0;
                    if (elevationNoise > 0.6f)
                        hillFlag = (uint)GlobalEnum.TileFlag.Hill;
                    
                    // 몬스터 스폰 플래그 결정 (6번째 비트)
                    uint monsterSpawnFlag = 0;
                    if (hillFlag == 0 && _randomGenerator.NextDouble() < _monsterSpawnRate)
                    {
                        monsterSpawnFlag = (uint)GlobalEnum.TileFlag.MonsterSpawn;
                        chunk.monsterSpawnPositions.Add(new Vector2Int(x, y));
                    }
                    
                    // 타일 데이터 조합
                    uint currentTile = chunk.tiles[x, y];
                    chunk.tiles[x, y] = (currentTile & 0xFFFFFFC0) | ((uint)baseTileType & 0x0000000F) | hillFlag | monsterSpawnFlag;
                }
            }
        }
    
        private void ReturnChunkToPool(int chunkX, int chunkY)
        {
            Vector2Int chunkKey = new Vector2Int(chunkX, chunkY);
            if (_activeChunks.ContainsKey(chunkKey))
            {
                MapChunkData chunk = _activeChunks[chunkKey];
                _activeChunks.Remove(chunkKey);
                
                RemoveChunkFromTilemap(chunk); // 타일맵에서 청크 제거
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
                    
                    if (_activeChunks.ContainsKey(chunkPos) == false)
                    {
                        MapChunkData chunk = GetOrCreateChunk(chunkPos.x, chunkPos.y);
                        if (chunk != null)
                        {
                            RenderChunkToTilemap(chunk);
                            OnChunkActivated(chunkPos, chunk);
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
                OnChunkDeactivated(chunkPos);
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


