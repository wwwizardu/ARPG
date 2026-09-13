using System.Collections.Generic;
using ARPG.Map;
using UnityEngine;

namespace ARPG.Systems
{
    public class System_MonsterSpawn : IFixedUpdateSystem
    {
        public int Priority => 130;
        public float UpdateInterval => AR.s.Monster != null
            ? AR.s.Monster.SpawnCheckInterval
            : 0.5f;

        private readonly List<Vector2Int> _activeChunksCache = new();
        private readonly List<SpawnCandidate> _spawnCandidatesCache = new();

        public void OnCreate()
        {
            Debug.Log("System_MonsterSpawn Created");
        }

        public void OnReset()
        {
            _activeChunksCache.Clear();
            _spawnCandidatesCache.Clear();
            Debug.Log("System_MonsterSpawn Reset called");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.Map == null)
                return;

            // Warm TTL/LRU는 슬롯별 타이머 없이 중앙 주기 검사로 처리한다.
            AR.s.Map.CleanupWarmChunks();

            if (AR.s.Monster == null || AR.s.MyPlayer == null)
                return;

            int remainingGroupBudget = AR.s.Monster.MaxSpawnGroupsPerCheck;
            int remainingEntityBudget = AR.s.Monster.MaxSpawnEntitiesPerCheck;
            if (remainingGroupBudget <= 0 || remainingEntityBudget <= 0)
                return;

            CollectSpawnCandidates((Vector2)AR.s.MyPlayer.transform.position);
            _spawnCandidatesCache.Sort(CompareSpawnCandidates);

            for (int i = 0; i < _spawnCandidatesCache.Count; i++)
            {
                if (remainingGroupBudget <= 0 || remainingEntityBudget <= 0)
                    break;

                SpawnCandidate candidate = _spawnCandidatesCache[i];
                if (AR.s.Monster.TrySpawnSlot(candidate.ChunkCoord, candidate.GenerationId,
                        candidate.SlotIndex, remainingEntityBudget, out int scheduledEntityCount) == false)
                {
                    continue;
                }

                remainingGroupBudget--;
                remainingEntityBudget -= scheduledEntityCount;
            }
        }

        private void CollectSpawnCandidates(Vector2 playerPosition)
        {
            _spawnCandidatesCache.Clear();
            AR.s.Map.GetActiveChunkCoordsNonAlloc(_activeChunksCache);
            float currentTime = Time.time;
            float minimumDistanceSqr = AR.s.Monster.MinimumSpawnDistanceSqr;
            float maximumDistanceSqr = AR.s.Monster.MaximumSpawnDistanceSqr;

            for (int chunkIndex = 0; chunkIndex < _activeChunksCache.Count; chunkIndex++)
            {
                Vector2Int chunkCoord = _activeChunksCache[chunkIndex];
                if (AR.s.Map.TryGetActiveChunkSpawnRuntime(chunkCoord,
                        out ChunkSpawnRuntimeData runtime) == false)
                {
                    continue;
                }

                MonsterSpawnSlotState[] slots = runtime.Slots;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    MonsterSpawnSlotState slot = slots[slotIndex];
                    if (slot.IsDisabled == true || slot.IsPending == true || slot.AliveMemberCount > 0 ||
                        currentTime < slot.NextSpawnTime)
                    {
                        continue;
                    }
                    if (AR.s.Map.IsMonsterSpawnSlotTerrainValid(chunkCoord,
                            runtime.GenerationId, slotIndex, true) == false)
                    {
                        continue;
                    }

                    Vector2 worldPosition = AR.s.Map.GetMonsterSpawnSlotWorldPosition(
                        chunkCoord, slot.LocalPosition);
                    float distanceSqr = (worldPosition - playerPosition).sqrMagnitude;
                    if (distanceSqr < minimumDistanceSqr || distanceSqr > maximumDistanceSqr)
                        continue;

                    _spawnCandidatesCache.Add(new SpawnCandidate(
                        chunkCoord,
                        runtime.GenerationId,
                        slotIndex,
                        slot.LastUsedTime,
                        Random.value));
                }
            }
        }

        private static int CompareSpawnCandidates(SpawnCandidate left, SpawnCandidate right)
        {
            int lastUsedComparison = left.LastUsedTime.CompareTo(right.LastUsedTime);
            if (lastUsedComparison != 0)
                return lastUsedComparison;

            return left.RandomTieBreaker.CompareTo(right.RandomTieBreaker);
        }

        private readonly struct SpawnCandidate
        {
            public readonly Vector2Int ChunkCoord;
            public readonly int GenerationId;
            public readonly int SlotIndex;
            public readonly float LastUsedTime;
            public readonly float RandomTieBreaker;

            public SpawnCandidate(Vector2Int chunkCoord, int generationId, int slotIndex,
                float lastUsedTime, float randomTieBreaker)
            {
                ChunkCoord = chunkCoord;
                GenerationId = generationId;
                SlotIndex = slotIndex;
                LastUsedTime = lastUsedTime;
                RandomTieBreaker = randomTieBreaker;
            }
        }
    }
}
