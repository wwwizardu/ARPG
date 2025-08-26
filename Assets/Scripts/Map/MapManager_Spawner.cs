using ARPG.Monster;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [Header("Monster Prefabs")]
        [SerializeField] private List<GameObject> _monsterPrefabs = new List<GameObject>();
        
        private Dictionary<Vector2Int, bool> _chunkSpawnStatus = new Dictionary<Vector2Int, bool>();

        private void OnResetSpawner()
        {
            _chunkSpawnStatus.Clear();
        }

        private void UpdateMonsterSpawner(float inDeltaTime)
        {
            if (AR.s.Monster == null)
                return;
        }

        public void OnChunkActivated(Vector2Int chunkCoord, MapChunkData chunkData)
        {
            if (AR.s.Monster == null)
                return;

            if (AR.s.Monster.HasChunkSpawned(chunkCoord))
            {
                AR.s.Monster.ActivateChunkMonsters(chunkCoord);
            }
            else
            {
                SpawnMonstersInChunk(chunkCoord, chunkData);
            }
        }

        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (AR.s.Monster == null)
                return;

            AR.s.Monster.DeactivateChunkMonsters(chunkCoord);
        }

        private void SpawnMonstersInChunk(Vector2Int chunkCoord, MapChunkData chunkData)
        {
            if (_monsterPrefabs.Count == 0)
                return;

            foreach (Vector2Int spawnPos in chunkData.monsterSpawnPositions)
            {
                if (Random.value < _monsterSpawnRate)
                {
                    GameObject randomPrefab = _monsterPrefabs[Random.Range(0, _monsterPrefabs.Count)];
                    Vector3 worldPos = new Vector3(
                        chunkCoord.x * chunkSize + spawnPos.x,
                        chunkCoord.y * chunkSize + spawnPos.y,
                        0
                    );

                    int monsterId = AR.s.Monster.SpawnMonsterAtPosition(randomPrefab, worldPos, chunkCoord);
                    
                    if (monsterId != -1)
                    {
                        Debug.Log($"Spawned monster {monsterId} at chunk {chunkCoord} position {worldPos}");
                    }
                }
            }
        }
    }
}


