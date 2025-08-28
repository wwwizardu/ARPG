using ARPG.Monster;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [Header("Monster Prefabs")]
        [SerializeField] private List<GameObject> _monsterPrefabs = new List<GameObject>();

        private float _respawnCheckInterval = 5f; // 리스폰 체크 간격
        
        private Dictionary<Vector2Int, bool> _chunkSpawnStatus = new Dictionary<Vector2Int, bool>();
        private Coroutine _respawnCoroutine;
        private WaitForSeconds _respawnWait;

        private void OnResetSpawner()
        {
            _chunkSpawnStatus.Clear();
            _respawnWait = new WaitForSeconds(_respawnCheckInterval);

            // 기존 리스폰 코루틴이 있다면 중지
            if (_respawnCoroutine != null)
            {
                StopCoroutine(_respawnCoroutine);
            }

            // 새로운 리스폰 코루틴 시작
            _respawnCoroutine = StartCoroutine(MonsterRespawnCoroutine());
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

        private IEnumerator MonsterRespawnCoroutine()
        {
            while (true)
            {
                // 30초마다 리스폰 체크
                yield return _respawnWait;

                if (AR.s.Monster == null || _monsterPrefabs.Count == 0)
                    continue;

                // 활성화된 청크들에서 죽은 몬스터 리스폰
                List<Vector2Int> activeChunks = AR.s.Monster.GetActiveChunksWithMonsters();
                
                foreach (Vector2Int chunkCoord in activeChunks)
                {
                    int originalCount = AR.s.Monster.GetOriginalSpawnCountInChunk(chunkCoord);
                    int aliveCount = AR.s.Monster.GetAliveMonsterCountInChunk(chunkCoord);
                    int deadCount = originalCount - aliveCount;

                    if (deadCount > 0)
                    {
                        // 청크 데이터를 가져와서 몬스터 스폰 위치 확인
                        if (_activeChunks != null && _activeChunks.ContainsKey(chunkCoord))
                        {
                            MapChunkData chunkData = _activeChunks[chunkCoord];
                            List<Vector2Int> availableSpawnPositions = new List<Vector2Int>(chunkData.monsterSpawnPositions);

                            // 죽은 몬스터 수만큼 리스폰
                            for (int i = 0; i < deadCount && i < availableSpawnPositions.Count; i++)
                            {
                                //if (Random.value < _monsterSpawnRate)
                                {
                                    GameObject randomPrefab = _monsterPrefabs[Random.Range(0, _monsterPrefabs.Count)];
                                    Vector2Int spawnPos = availableSpawnPositions[Random.Range(0, availableSpawnPositions.Count)];
                                    
                                    int monsterId = AR.s.Monster.RespawnMonsterInChunk(randomPrefab, chunkCoord, spawnPos);
                                    
                                    if (monsterId != -1)
                                    {
                                        Debug.Log($"Respawned monster {monsterId} at chunk {chunkCoord} position {spawnPos}");
                                    }
                                    
                                    availableSpawnPositions.Remove(spawnPos); // 같은 위치에 중복 스폰 방지
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}


