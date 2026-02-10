#nullable enable
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARPG.Component;
using ARPG.Map;
using ARPG.Creature;
using ARPG.Scene;
using ARPG.Utility;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private List<Creature.Monster> _monsters = new();
        private Dictionary<Vector2Int, ChunkMonsterData> _chunkMonsters = new();
        private Dictionary<int, Creature.Monster> _monsterInstanceById = new();
        private int _nextMonsterId = 1;
        
        private Transform? _monsterParent;

        private WaitForSeconds _cleanupInterval = new WaitForSeconds(1f);

        [Header("Monster Spawn")]
        [SerializeField] private List<GameObject> _monsterPrefabs = new();
        [SerializeField] private float _monsterSpawnRate = 0.1f;

        [Header("Monster Lifecycle")]
        [SerializeField] private float _chunkMonsterLifetime = 300f; // 5분
        [SerializeField] private float _activationDistance = 25f; // 몬스터 활성화 거리
        [SerializeField] private float _deactivationDistance = 30f; // 몬스터 비활성화 거리 (하이스테리시스)
        
        private float _activationDistanceSqr;
        private float _deactivationDistanceSqr;
        
        private bool _initialized = false;

        public List<GameObject> MonsterPrefabs => _monsterPrefabs;
        public float MonsterSpawnRate => _monsterSpawnRate;

        public void Initialize()
        {
            _activationDistanceSqr = _activationDistance * _activationDistance;
            _deactivationDistanceSqr = _deactivationDistance * _deactivationDistance;
            
            StartCoroutine(CleanupRoutine());
            _initialized = true;
        }

        public void Reset()
        {
            StopAllCoroutines();

            // 모든 몬스터 제거
            for (int i = 0; i < _monsters.Count; i++)
            {
                var monster = _monsters[i];
                if (monster != null && monster.gameObject != null)
                {
                    DestroyMonster(monster);
                }
            }

            // 컬렉션 정리
            _monsters.Clear();
            _chunkMonsters.Clear();
            _monsterInstanceById.Clear();

            // ID 카운터 리셋
            _nextMonsterId = 1;

            // 코루틴 다시 시작 (이미 초기화된 상태라면)
            if (_initialized)
            {
                StartCoroutine(CleanupRoutine());
            }
        }

        public void SetMorsterRoot(Transform inMonsterRoot)
        {
            _monsterParent = inMonsterRoot;
        }

        public void AddMonster(Creature.Monster monster)
        {
            if (monster == null)
                return;

            if (!_monsters.Contains(monster))
            {
                _monsters.Add(monster);
                monster.transform.SetParent(_monsterParent);
            }
        }

        /// <summary>
        /// 몬스터를 완전히 제거합니다 (Entity + ECS 컴포넌트 + GameObject + 추적 정보)
        /// _monsters 리스트에서의 제거는 호출하는 쪽에서 처리해야 합니다.
        /// </summary>
        /// <param name="monster">제거할 몬스터</param>
        private void DestroyMonster(Creature.Monster monster)
        {
            if (monster == null)
                return;

            // 인스턴스 딕셔너리에서 제거
            int instanceId = monster.GetInstanceId();
            if (instanceId != -1)
            {
                _monsterInstanceById.Remove(instanceId);
            }

            // Entity 제거 (ECS 컴포넌트 + 스킬 엔티티 + EntityIdHelper 등록 해제 포함)
            EntityIdHelper.DestroyEntity(monster.EntityId);

            // GameObject 파괴
            Destroy(monster.gameObject);
        }

        public void CleanupDestroyedMonsters()
        {
            for (int i = _monsters.Count - 1; i >= 0; i--)
            {
                if (_monsters[i] == null)
                {
                    _monsters.RemoveAt(i);
                }
            }
        }

        public void UpdateMpnsterManager(float inDeltaTime)
        {
            if (!_initialized)
                return;
                
            ArpgPlayer? player = AR.s.MyPlayer;
            if (player == null)
                return;

            Vector3 playerPosition = player.transform.position;

            // 죽은 몬스터들을 제거하기 위해 역순으로 순회
            for (int i = _monsters.Count - 1; i >= 0; i--)
            {
                var monster = _monsters[i];
                if (monster != null)
                {
                    // StateComponent를 통해 몬스터가 죽었는지 확인
                    bool isDead = false;
                    if (AR.s.Component.TryGetComponent<StateComponent>(monster.EntityId, out var stateComponent))
                    {
                        isDead = stateComponent.Condition == CharacterConditions.Dead;
                    }

                    if (isDead)
                    {
                        _monsters.RemoveAt(i);
                        DestroyMonster(monster);
                    }
                    else
                    {
                        UpdateMonsterActivationByDistance(monster, playerPosition);
                    }
                }
            }

        }

        private IEnumerator CleanupRoutine()
        {
            while (_initialized)
            {
                yield return _cleanupInterval;
                CleanupDestroyedMonsters();
                CleanupExpiredChunkMonsters();
            }
        }

        public int SpawnMonsterAtPosition(GameObject monsterPrefab, Vector3 position, Vector2Int chunkCoord, bool isOriginalSpawn = true)
        {
            if (monsterPrefab == null)
                return -1;

            if(AR.s.CurrentScene is GameScene gameScene == false)
                return -1;

            Vector3 spawnPos = new Vector3(position.x, position.y, -0.05f) ;

            GameObject monsterObj = Instantiate(monsterPrefab, spawnPos, Quaternion.identity, gameScene.MonsterRoot);
            Creature.Monster monster = monsterObj.GetComponent<Creature.Monster>();
            
            if (monster == null)
            {
                Destroy(monsterObj);
                return -1;
            }

            int monsterTableId = Random.Range(1001, 1003);
            monster.Initialize();
            if (monster.Load(monsterTableId) == false) // 임시로 ID 1 사용
            {
                Debug.LogError($"[MonsterManager] SpawnMonster - Failed to load monster with ID 1");
                Destroy(monsterObj);
                return -1;
            }
            monster.InitializeECSComponents(); // ECS 컴포넌트 초기화

            int monsterId = _nextMonsterId++;
            monster.SetInstanceId(monsterId); // 몬스터에 인스턴스 ID 저장
            _monsterInstanceById[monsterId] = monster;
            AddMonster(monster);

            // 스폰 시 플레이어와의 거리에 따라 초기 활성화 상태 결정
            SetMonsterInitialActivationState(monster, spawnPos);

            if (!_chunkMonsters.ContainsKey(chunkCoord))
            {
                _chunkMonsters[chunkCoord] = new ChunkMonsterData(chunkCoord);
            }

            _chunkMonsters[chunkCoord].spawnedMonsterIds.Add(monsterId);
            _chunkMonsters[chunkCoord].hasSpawned = true;
            
            if (isOriginalSpawn)
            {
                _chunkMonsters[chunkCoord].originalSpawnCount++;
            }

            return monsterId;
        }

        public void ActivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (!_chunkMonsters.ContainsKey(chunkCoord))
                return;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            chunkData.lastActiveTime = Time.time;

            ArpgPlayer? player = AR.s.MyPlayer;
            if (player == null)
                return;

            Vector3 playerPosition = player.transform.position;

            for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
            {
                int monsterId = chunkData.spawnedMonsterIds[i];
                if (_monsterInstanceById.TryGetValue(monsterId, out Creature.Monster monster))
                {
                    if (monster != null && monster.gameObject != null)
                    {
                        UpdateMonsterActivationByDistance(monster, playerPosition);
                    }
                }
            }
        }

        public void DeactivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (!_chunkMonsters.ContainsKey(chunkCoord))
                return;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            chunkData.lastActiveTime = Time.time;

            for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
            {
                int monsterId = chunkData.spawnedMonsterIds[i];
                if (_monsterInstanceById.TryGetValue(monsterId, out Creature.Monster monster))
                {
                    if (monster != null && monster.gameObject != null)
                    {
                        monster.gameObject.SetActive(false);
                    }
                }
            }
        }

        public bool HasChunkSpawned(Vector2Int chunkCoord)
        {
            return _chunkMonsters.ContainsKey(chunkCoord) && _chunkMonsters[chunkCoord].hasSpawned;
        }

        public int GetAliveMonsterCountInChunk(Vector2Int chunkCoord)
        {
            if (!_chunkMonsters.ContainsKey(chunkCoord))
                return 0;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            int aliveCount = 0;

            for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
            {
                int monsterId = chunkData.spawnedMonsterIds[i];
                if (_monsterInstanceById.TryGetValue(monsterId, out Creature.Monster monster))
                {
                    if (monster != null && monster.State != CharacterConditions.Dead)
                    {
                        aliveCount++;
                    }
                }
            }

            return aliveCount;
        }

        public int GetOriginalSpawnCountInChunk(Vector2Int chunkCoord)
        {
            if (!_chunkMonsters.ContainsKey(chunkCoord))
                return 0;

            return _chunkMonsters[chunkCoord].originalSpawnCount;
        }

        public void GetActiveChunksWithMonstersNonAlloc(List<Vector2Int> result)
        {
            result.Clear();

            if (AR.s.Map == null)
                return;

            // Dictionary.KeyCollection의 foreach는 struct enumerator를 사용하므로 GC 할당 없음
            var mapActiveChunkCoords = AR.s.Map.GetActiveChunkCoords();
            foreach (var chunkCoord in mapActiveChunkCoords)
            {
                if (_chunkMonsters.ContainsKey(chunkCoord) && _chunkMonsters[chunkCoord].hasSpawned)
                {
                    result.Add(chunkCoord);
                }
            }
        }

        public void SpawnInitialMonstersInChunk(Vector2Int chunkCoord)
        {
            if (_monsterPrefabs.Count == 0)
                return;

            if (HasChunkSpawned(chunkCoord))
                return;

            if (AR.s.Map == null)
                return;

            if (AR.s.Map.TryGetChunkSpawnPositions(chunkCoord, out var spawnPositions) == false)
                return;

            for (int i = 0; i < spawnPositions.Count; i++)
            {
                Vector2Int spawnPos = spawnPositions[i];
                if (Random.value < _monsterSpawnRate)
                {
                    GameObject randomPrefab = _monsterPrefabs[Random.Range(0, _monsterPrefabs.Count)];
                    Vector3 worldPos = new Vector3(
                        chunkCoord.x * AR.s.Map.chunkSize + spawnPos.x,
                        chunkCoord.y * AR.s.Map.chunkSize + spawnPos.y,
                        -0.05f
                    );
                    SpawnMonsterAtPosition(randomPrefab, worldPos, chunkCoord);
                }
            }
        }

        public int RespawnMonsterInChunk(GameObject monsterPrefab, Vector2Int chunkCoord, Vector2Int spawnPos)
        {
            if (monsterPrefab == null)
                return -1;

            Vector3 worldPos = new Vector3(
                chunkCoord.x * AR.s.Map.chunkSize + spawnPos.x,
                chunkCoord.y * AR.s.Map.chunkSize + spawnPos.y,
                -0.05f
            );

            return SpawnMonsterAtPosition(monsterPrefab, worldPos, chunkCoord, false);
        }

        private void UpdateMonsterActivationByDistance(Creature.Monster monster, Vector3 playerPosition)
        {
            if (monster == null)
                return;

            float distanceSqrToPlayer = (playerPosition - monster.transform.position).sqrMagnitude;
            
            // 하이스테리시스를 사용한 활성화/비활성화 로직
            if (monster.IsActivated())
            {
                // 이미 활성화된 몬스터는 더 먼 거리에서 비활성화
                if (distanceSqrToPlayer > _deactivationDistanceSqr)
                {
                    monster.Deactivate();
                }
            }
            else
            {
                // 비활성화된 몬스터는 가까운 거리에서 활성화
                if (distanceSqrToPlayer <= _activationDistanceSqr)
                {
                    monster.Activate();
                }
            }
        }

        private void SetMonsterInitialActivationState(Creature.Monster monster, Vector3 spawnPosition)
        {
            if (monster == null)
                return;

            ArpgPlayer? player = AR.s.MyPlayer;
            if (player != null)
            {
                float distanceSqrToPlayer = (player.transform.position - spawnPosition).sqrMagnitude;
                if (distanceSqrToPlayer <= _activationDistanceSqr)
                {
                    monster.Activate();
                }
                else
                {
                    monster.Deactivate();
                }
            }
            else
            {
                // 플레이어가 없을 경우 기본적으로 비활성화
                monster.Deactivate();
            }
        }

        private readonly List<Vector2Int> _expiredChunksCache = new();

        private void CleanupExpiredChunkMonsters()
        {
            _expiredChunksCache.Clear();

            // Dictionary의 foreach는 struct enumerator를 사용하므로 GC 할당 없음
            foreach (var kvp in _chunkMonsters)
            {
                ChunkMonsterData chunkData = kvp.Value;
                if (Time.time - chunkData.lastActiveTime > _chunkMonsterLifetime)
                {
                    // 만료된 청크의 몬스터들 정리
                    for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
                    {
                        int monsterId = chunkData.spawnedMonsterIds[i];
                        if (_monsterInstanceById.TryGetValue(monsterId, out Creature.Monster monster))
                        {
                            if (monster != null)
                            {
                                _monsters.Remove(monster);
                                DestroyMonster(monster);
                            }
                            else
                            {
                                _monsterInstanceById.Remove(monsterId);
                            }
                        }
                    }
                    _expiredChunksCache.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _expiredChunksCache.Count; i++)
            {
                _chunkMonsters.Remove(_expiredChunksCache[i]);
            }
        }
    }
}