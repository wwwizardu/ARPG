using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Map
{
    public enum MapChunkRuntimeState
    {
        Active,
        Warm,
        Expired
    }

    public enum MonsterSpawnGroupKind
    {
        Main,
        Sub
    }

    [Serializable]
    public sealed class MonsterSpawnSlotState
    {
        public Vector2Int LocalPosition;
        public MonsterSpawnGroupKind GroupKind;
        public float NextSpawnTime;
        public float LastUsedTime = -1f;
        public bool IsDisabled;
        public bool IsPending;
        public int PendingVersion;
        public int AliveMemberCount;

        public MonsterSpawnSlotState(Vector2Int localPosition, MonsterSpawnGroupKind groupKind)
        {
            LocalPosition = localPosition;
            GroupKind = groupKind;
        }

        public int BeginPending()
        {
            AdvancePendingVersion();
            IsPending = true;
            return PendingVersion;
        }

        public void CancelPending()
        {
            if (IsPending == true)
            {
                AdvancePendingVersion();
            }
            IsPending = false;
        }

        public bool IsPendingReservation(int pendingVersion)
        {
            return IsPending == true && PendingVersion == pendingVersion;
        }

        private void AdvancePendingVersion()
        {
            PendingVersion++;
            if (PendingVersion <= 0)
            {
                PendingVersion = 1;
            }
        }
    }

    [Serializable]
    public sealed class ChunkSpawnRuntimeData
    {
        public int GenerationId;
        public float LastActiveTime;
        public MonsterSpawnSlotState[] Slots = Array.Empty<MonsterSpawnSlotState>();

        public void Initialize(int generationId, float currentTime)
        {
            GenerationId = generationId;
            LastActiveTime = currentTime;
            Slots = Array.Empty<MonsterSpawnSlotState>();
        }

        public void Invalidate()
        {
            GenerationId = 0;
            Slots = Array.Empty<MonsterSpawnSlotState>();
        }
    }

    [Serializable]
    public sealed class ChunkMonsterData
    {
        public Vector2Int ChunkCoord;
        public int GenerationId;
        public readonly List<int> SpawnedMonsterIds = new();

        public ChunkMonsterData(Vector2Int chunkCoord, int generationId)
        {
            ChunkCoord = chunkCoord;
            GenerationId = generationId;
        }
    }

    [Serializable]
    public sealed class MapChunkData
    {
        public int chunkX;
        public int chunkY;
        // 시드 청크 (0,0) 기준 Chebyshev 거리 + 1, [1,99] 클램프. GenerateChunkData에서 채움
        public int zone;
        // 청크 대표 biome (v1.9: 청크 중심 cluster의 biome). MonsterManager 풀 선택처럼 1개 biome 요구하는 caller용.
        // 세밀한 per-cluster biome은 clusterBiomes 참조.
        public GlobalEnum.BiomeType chunkBiome;
        // cluster-grained biomes (v1.9): chunkSize / BiomeNoise.CLUSTER_SIZE per side.
        // 8/2 = 4 → 16 entries. Renderer/Generator가 per-cluster theme 결정에 사용.
        public GlobalEnum.BiomeType[] clusterBiomes;
        public ulong[,] tiles;
        // 마을 청사진 영역(+여유 거리)에 속해 몬스터 스폰이 금지된 타일. GenerateChunkData에서 채움
        public bool[,] villageSpawnExclusion;
        public MapChunkRuntimeState runtimeState;
        public ChunkSpawnRuntimeData spawnRuntime;

        public MapChunkData(int chunkSize)
        {
            tiles = new ulong[chunkSize, chunkSize];
            villageSpawnExclusion = new bool[chunkSize, chunkSize];
            int clustersPerSide = chunkSize / BiomeNoise.CLUSTER_SIZE;
            clusterBiomes = new GlobalEnum.BiomeType[clustersPerSide * clustersPerSide];
            spawnRuntime = new ChunkSpawnRuntimeData();
            runtimeState = MapChunkRuntimeState.Expired;
        }

        public void SetChunkPosition(int x, int y, int generationId, float currentTime)
        {
            chunkX = x;
            chunkY = y;
            runtimeState = MapChunkRuntimeState.Warm;
            spawnRuntime.Initialize(generationId, currentTime);
        }

        public void Activate(float currentTime)
        {
            runtimeState = MapChunkRuntimeState.Active;
            spawnRuntime.LastActiveTime = currentTime;
        }

        public void Warm(float currentTime)
        {
            runtimeState = MapChunkRuntimeState.Warm;
            spawnRuntime.LastActiveTime = currentTime;

            MonsterSpawnSlotState[] slots = spawnRuntime.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].CancelPending();
            }
        }

        public void InvalidateGeneration()
        {
            runtimeState = MapChunkRuntimeState.Expired;
            spawnRuntime.GenerationId = 0;
        }

        public void Deactivate()
        {
            runtimeState = MapChunkRuntimeState.Expired;
            spawnRuntime.Invalidate();
        }
    }
}
