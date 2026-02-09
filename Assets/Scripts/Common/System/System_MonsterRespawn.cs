using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Systems
{
    public struct System_MonsterRespawn : IFixedUpdateSystem
    {
        public int Priority => 130;
        public float UpdateInterval => 5f;

        // static readonly: System은 struct이므로 instance 필드를 사용하면
        // OnCreate() 호출 시 ValueType.GetHashCode()가 변경되어
        // SystemManager의 Dictionary에서 KeyNotFoundException이 발생한다.
        private static readonly List<Vector2Int> _activeChunksCache = new();
        private static readonly List<Vector2Int> _availableSpawnCache = new();

        public void OnCreate()
        {
            Debug.Log("System_MonsterRespawn Created");
        }

        public void OnReset()
        {
            _activeChunksCache.Clear();
            _availableSpawnCache.Clear();
            Debug.Log("System_MonsterRespawn Reset called");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.Monster == null || AR.s.Map == null)
                return;

            List<GameObject> monsterPrefabs = AR.s.Map.MonsterPrefabs;
            if (monsterPrefabs == null || monsterPrefabs.Count == 0)
                return;

            AR.s.Monster.GetActiveChunksWithMonstersNonAlloc(_activeChunksCache);

            for (int i = 0; i < _activeChunksCache.Count; i++)
            {
                Vector2Int chunkCoord = _activeChunksCache[i];
                int originalCount = AR.s.Monster.GetOriginalSpawnCountInChunk(chunkCoord);
                int aliveCount = AR.s.Monster.GetAliveMonsterCountInChunk(chunkCoord);
                int deadCount = originalCount - aliveCount;

                if (deadCount <= 0)
                    continue;

                if (AR.s.Map.TryGetChunkSpawnPositions(chunkCoord, out var spawnPositions) == false)
                    continue;

                if (spawnPositions.Count == 0)
                    continue;

                // 재사용 리스트에 스폰 가능 위치 복사
                _availableSpawnCache.Clear();
                for (int j = 0; j < spawnPositions.Count; j++)
                {
                    _availableSpawnCache.Add(spawnPositions[j]);
                }

                // 죽은 몬스터 수만큼 리스폰
                for (int j = 0; j < deadCount && _availableSpawnCache.Count > 0; j++)
                {
                    GameObject randomPrefab = monsterPrefabs[Random.Range(0, monsterPrefabs.Count)];
                    int randomIndex = Random.Range(0, _availableSpawnCache.Count);
                    Vector2Int spawnPos = _availableSpawnCache[randomIndex];

                    AR.s.Monster.RespawnMonsterInChunk(randomPrefab, chunkCoord, spawnPos);

                    // 같은 위치에 중복 스폰 방지 (swap-and-pop)
                    int lastIndex = _availableSpawnCache.Count - 1;
                    _availableSpawnCache[randomIndex] = _availableSpawnCache[lastIndex];
                    _availableSpawnCache.RemoveAt(lastIndex);
                }
            }
        }
    }
}
