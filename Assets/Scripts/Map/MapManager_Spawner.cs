using ARPG.Monster;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [Header("Monster Prefabs")]
        [SerializeField] private List<GameObject> _monsterPrefabs = new List<GameObject>();

        public List<GameObject> MonsterPrefabs => _monsterPrefabs;
        public float MonsterSpawnRate => _monsterSpawnRate;

        private void OnResetSpawner()
        {
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

            for (int i = 0; i < chunkData.monsterSpawnPositions.Count; i++)
            {
                Vector2Int spawnPos = chunkData.monsterSpawnPositions[i];
                if (Random.value < _monsterSpawnRate)
                {
                    GameObject randomPrefab = _monsterPrefabs[Random.Range(0, _monsterPrefabs.Count)];
                    Vector3 worldPos = new Vector3(
                        chunkCoord.x * chunkSize + spawnPos.x,
                        chunkCoord.y * chunkSize + spawnPos.y,
                        0
                    );

                    AR.s.Monster.SpawnMonsterAtPosition(randomPrefab, worldPos, chunkCoord);
                }
            }
        }
    }
}
