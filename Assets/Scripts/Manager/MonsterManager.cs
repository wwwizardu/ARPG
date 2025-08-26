#nullable enable
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARPG.Map;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private List<Creature.CharacterBase> _monsters = new();
        private Dictionary<Vector2Int, ChunkMonsterData> _chunkMonsters = new();
        private Dictionary<int, Creature.CharacterBase> _monsterInstanceById = new();
        private int _nextMonsterId = 1;
        
        private Transform? _monsterParent;

        private WaitForSeconds _cleanupInterval = new WaitForSeconds(1f);

        [SerializeField] private float _chunkMonsterLifetime = 300f; // 5분
        
        private bool _initialized = false;

        public void Initialize()
        {
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

        public void AddMonster(Creature.CharacterBase monster)
        {
            if (monster == null)
                return;

            if (!_monsters.Contains(monster))
            {
                _monsters.Add(monster);
                monster.transform.SetParent(_monsterParent);
            }
        }

        public void RemoveMonster(Creature.CharacterBase monster)
        {
            if (monster == null)
                return;

            _monsters.Remove(monster);
        }

        public List<Creature.CharacterBase> GetAllMonsters()
        {
            return new List<Creature.CharacterBase>(_monsters);
        }

        public Creature.CharacterBase? GetNearestMonster(Vector3 position, float maxDistance = float.MaxValue)
        {
            Creature.CharacterBase? nearest = null;
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

        public int SpawnMonsterAtPosition(GameObject monsterPrefab, Vector3 position, Vector2Int chunkCoord)
        {
            if (monsterPrefab == null)
                return -1;

            Vector3 spawnPos = new Vector3(position.x, position.y, -0.05f) ;

            GameObject monsterObj = Instantiate(monsterPrefab, spawnPos, Quaternion.identity);
            Creature.CharacterBase monster = monsterObj.GetComponent<Creature.CharacterBase>();
            
            if (monster == null)
            {
                Destroy(monsterObj);
                return -1;
            }

            int monsterId = _nextMonsterId++;
            _monsterInstanceById[monsterId] = monster;
            AddMonster(monster);

            if (!_chunkMonsters.ContainsKey(chunkCoord))
            {
                _chunkMonsters[chunkCoord] = new ChunkMonsterData(chunkCoord);
            }

            _chunkMonsters[chunkCoord].spawnedMonsterIds.Add(monsterId);
            _chunkMonsters[chunkCoord].hasSpawned = true;

            return monsterId;
        }

        public void ActivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (!_chunkMonsters.ContainsKey(chunkCoord))
                return;

            ChunkMonsterData chunkData = _chunkMonsters[chunkCoord];
            chunkData.lastActiveTime = Time.time;

            foreach (int monsterId in chunkData.spawnedMonsterIds)
            {
                if (_monsterInstanceById.TryGetValue(monsterId, out Creature.CharacterBase monster))
                {
                    if (monster != null && monster.gameObject != null)
                    {
                        monster.gameObject.SetActive(true);
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
                if (_monsterInstanceById.TryGetValue(monsterId, out Creature.CharacterBase monster))
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
                        if (_monsterInstanceById.TryGetValue(monsterId, out Creature.CharacterBase monster))
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