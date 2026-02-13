#nullable enable
using System.Collections.Generic;
using UnityEngine;
using ARPG.Component;
using ARPG.Map;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Scene;
using ARPG.Utility;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private List<Creature.Monster> _monsters = new();
        private Dictionary<Vector2Int, ChunkMonsterData> _chunkMonsters = new();
        private Transform? _monsterParent;


        [Header("Monster Spawn")]
        [SerializeField] private List<GameObject> _monsterPrefabs = new();
        [SerializeField] private float _monsterSpawnRate = 0.1f;

        [Header("Monster Lifecycle")]
        [SerializeField] private float _chunkMonsterLifetime = 300f; // 5분
        [SerializeField] private float _activationDistance = 25f; // 몬스터 활성화 거리
        [SerializeField] private float _deactivationDistance = 30f; // 몬스터 비활성화 거리 (하이스테리시스)
        
        public List<GameObject> MonsterPrefabs => _monsterPrefabs;
        public float MonsterSpawnRate => _monsterSpawnRate;

        public void Initialize()
        {
        }

        public void Reset()
        {
            // 모든 몬스터 제거
            for (int i = 0; i < _monsters.Count; i++)
            {
                var monster = _monsters[i];
                if (monster != null && monster.gameObject != null)
                {
                    EntityIdHelper.DestroyEntity(monster.EntityId);
                    Destroy(monster.gameObject);
                }
            }

            // 컬렉션 정리
            _monsters.Clear();
            _chunkMonsters.Clear();

        }

        public void SetMorsterRoot(Transform inMonsterRoot)
        {
            _monsterParent = inMonsterRoot;
        }

        public void AddMonster(Creature.Monster monster)
        {
            if (monster == null)
                return;

            if (_monsters.Contains(monster) == false)
            {
                _monsters.Add(monster);
                monster.transform.SetParent(_monsterParent);
            }
        }

        /// <summary>
        /// Monster.OnEntityDestroy()에서 호출됨
        /// MonsterManager의 추적 정보에서 몬스터를 제거
        /// ECS 컴포넌트 정리와 GameObject 파괴는 System_EntityDestroy가 담당
        /// </summary>
        public void UnregisterMonster(Creature.Monster monster)
        {
            if (monster == null)
                return;

            _monsters.Remove(monster);
        }

        public int SpawnMonsterAtPosition(GameObject monsterPrefab, Vector3 position, Vector2Int chunkCoord, bool isOriginalSpawn = true)
        {
            if (monsterPrefab == null)
                return -1;

            if (AR.s.CurrentScene is GameScene gameScene == false)
                return -1;

            Vector3 spawnPos = new Vector3(position.x, position.y, -0.05f);

            // EntityFactory를 통해 몬스터 생성 (Initialize + Load + ECS 컴포넌트 추가)
            int monsterTableId = Random.Range(1001, 1003);
            int entityId = EntityFactory.CreateMonster(monsterTableId, monsterPrefab, spawnPos, gameScene.MonsterRoot, out var monster);

            if (entityId < 0 || monster == null)
                return -1;

            AddMonster(monster);

            // ActivationDistanceComponent 추가
            EntityFactory.AddActivationComponent(entityId, _activationDistance, _deactivationDistance);

            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
            {
                _chunkMonsters[chunkCoord] = new ChunkMonsterData(chunkCoord);
            }

            _chunkMonsters[chunkCoord].spawnedMonsterIds.Add(entityId);
            _chunkMonsters[chunkCoord].hasSpawned = true;

            if (isOriginalSpawn)
            {
                _chunkMonsters[chunkCoord].originalSpawnCount++;
            }

            return entityId;
        }

        public void ActivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
                return;

            // 시간 갱신만 수행, 거리 기반 활성화는 System_EntityActivation이 담당
            _chunkMonsters[chunkCoord].lastActiveTime = Time.time;
        }

        public void DeactivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
                return;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            chunkData.lastActiveTime = Time.time;

            for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
            {
                int entityId = chunkData.spawnedMonsterIds[i];
                if (AR.s.Message.TryGetEntity<Creature.Monster>(entityId, out var monster))
                {
                    if (monster.gameObject != null)
                    {
                        monster.gameObject.SetActive(false);

                        // ECS 컴포넌트도 비활성화 상태로 동기화
                        if (AR.s.Component.TryGetComponent<ActivationDistanceComponent>(entityId, out var activation))
                        {
                            activation.IsActivated = false;
                            AR.s.Component.SetComponent(entityId, activation);
                        }
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
            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
                return 0;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            int aliveCount = 0;

            for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
            {
                int entityId = chunkData.spawnedMonsterIds[i];
                if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
                {
                    if (state.Condition != CharacterConditions.Dead)
                    {
                        aliveCount++;
                    }
                }
            }

            return aliveCount;
        }

        public int GetOriginalSpawnCountInChunk(Vector2Int chunkCoord)
        {
            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
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

        private readonly List<Vector2Int> _expiredChunksCache = new();

        public void CleanupExpiredChunkMonsters()
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
                        int entityId = chunkData.spawnedMonsterIds[i];
                        if (AR.s.Message.TryGetEntity<Creature.Monster>(entityId, out var monster))
                        {
                            _monsters.Remove(monster);
                            EntityIdHelper.DestroyEntity(entityId);
                            Destroy(monster.gameObject);
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