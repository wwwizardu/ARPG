#nullable enable
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARPG.Map;
using ARPG.Creature;

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

        [SerializeField] private float _chunkMonsterLifetime = 300f; // 5분
        [SerializeField] private float _activationDistance = 20f; // 몬스터 활성화 거리
        [SerializeField] private float _deactivationDistance = 25f; // 몬스터 비활성화 거리 (하이스테리시스)
        
        private float _activationDistanceSqr;
        private float _deactivationDistanceSqr;
        
        private bool _initialized = false;

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
            foreach (var monster in _monsters)
            {
                if (monster != null && monster.gameObject != null)
                {
                    Destroy(monster.gameObject);
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

        public void RemoveMonster(Creature.Monster monster)
        {
            if (monster == null)
                return;

            _monsters.Remove(monster);
        }

        public List<Creature.Monster> GetAllMonsters()
        {
            return new List<Creature.Monster>(_monsters);
        }

        public Creature.Monster? GetNearestMonster(Vector3 position, float maxDistance = float.MaxValue)
        {
            Creature.Monster? nearest = null;
            float nearestDistance = maxDistance;

            foreach (var monster in _monsters)
            {
                if (monster == null)
                    continue;

                float distance = Vector3.Distance(position, monster.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            return nearest;
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
                    // 몬스터가 죽었는지 확인
                    if (monster.State == CharacterConditions.Dead)
                    {
                        // 리스트에서 제거
                        _monsters.RemoveAt(i);
                        
                        // 인스턴스 딕셔너리에서 제거 (몬스터의 인스턴스 ID 사용)
                        int instanceId = monster.GetInstanceId();
                        if (instanceId != -1)
                        {
                            _monsterInstanceById.Remove(instanceId);
                        }
                        
                        // 게임 오브젝트 파괴
                        Destroy(monster.gameObject);
                    }
                    else
                    {
                        UpdateMonsterActivationByDistance(monster, playerPosition);
                        
                        //monster.UpdateMonster(inDeltaTime); // 몬스터 업데이트 로직 호출
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

            Vector3 spawnPos = new Vector3(position.x, position.y, -0.05f) ;

            GameObject monsterObj = Instantiate(monsterPrefab, spawnPos, Quaternion.identity);
            Creature.Monster monster = monsterObj.GetComponent<Creature.Monster>();
            
            if (monster == null)
            {
                Destroy(monsterObj);
                return -1;
            }

            monster.Initialize();
            monster.LoadData(2); // 임시로 ID 1 사용

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

            foreach (int monsterId in chunkData.spawnedMonsterIds)
            {
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

            foreach (int monsterId in chunkData.spawnedMonsterIds)
            {
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

            foreach (int monsterId in chunkData.spawnedMonsterIds)
            {
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

        public List<Vector2Int> GetActiveChunksWithMonsters()
        {
            List<Vector2Int> activeChunksWithMonsters = new List<Vector2Int>();
            
            // MapManager의 실제 활성 청크 중에서 몬스터가 스폰된 청크만 반환
            if (AR.s.Map != null)
            {
                var mapActiveChunkCoords = AR.s.Map.GetActiveChunkCoords();
                foreach (var chunkCoord in mapActiveChunkCoords)
                {
                    // 해당 청크에 몬스터가 스폰된 적이 있는지 확인
                    if (_chunkMonsters.ContainsKey(chunkCoord) && _chunkMonsters[chunkCoord].hasSpawned)
                    {
                        activeChunksWithMonsters.Add(chunkCoord);
                    }
                }
            }
            
            return activeChunksWithMonsters;
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

        private void CleanupExpiredChunkMonsters()
        {
            List<Vector2Int> expiredChunks = new List<Vector2Int>();

            foreach (var kvp in _chunkMonsters)
            {
                ChunkMonsterData chunkData = kvp.Value;
                if (Time.time - chunkData.lastActiveTime > _chunkMonsterLifetime)
                {
                    // 만료된 청크의 몬스터들 정리
                    foreach (int monsterId in chunkData.spawnedMonsterIds)
                    {
                        if (_monsterInstanceById.TryGetValue(monsterId, out Creature.Monster monster))
                        {
                            if (monster != null)
                            {
                                RemoveMonster(monster);
                                Destroy(monster.gameObject);
                            }
                            _monsterInstanceById.Remove(monsterId);
                        }
                    }
                    expiredChunks.Add(kvp.Key);
                }
            }

            foreach (Vector2Int chunkCoord in expiredChunks)
            {
                _chunkMonsters.Remove(chunkCoord);
            }
        }
    }
}