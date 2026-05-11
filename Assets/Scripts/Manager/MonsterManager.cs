#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.Base;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Scene;
using ARPG.Utility;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private List<EntityBase> _monsters = new();
        private Dictionary<Vector2Int, ChunkMonsterData> _chunkMonsters = new();
        private Transform? _monsterParent;


        [Header("Monster Spawn (Group-based)")]
        [SerializeField] private int _mainMonsterTableId = 1003;
        [SerializeField] private int _subMonsterTableId = 1003;
        // x = min, y = max (둘 다 inclusive)
        [SerializeField] private Vector2Int _mainGroupCount = new Vector2Int(1, 2);
        [SerializeField] private Vector2Int _mainGroupSize = new Vector2Int(8, 12);
        [SerializeField] private Vector2Int _subGroupCount = new Vector2Int(2, 3);
        [SerializeField] private Vector2Int _subGroupSize = new Vector2Int(3, 5);
        [SerializeField] private float _groupRadius = 2.5f;
        [SerializeField] private GroupDistribution _groupDistribution = GroupDistribution.GaussianFromCenter;
        [SerializeField] private float _interGroupMinDistance = 4f;
        [SerializeField] private int _anchorRejectionAttempts = 8;

        [Header("Monster Lifecycle")]
        [SerializeField] private float _chunkMonsterLifetime = 300f; // 5분
        [SerializeField] private float _activationDistance = 25f; // 몬스터 활성화 거리
        [SerializeField] private float _deactivationDistance = 30f; // 몬스터 비활성화 거리 (하이스테리시스)

        // SpawnInitialMonstersInChunk 호출마다 재사용 — GC 회피
        private readonly List<Vector2> _placedAnchorsCache = new();

        // 현재 처리 중인 청크의 Zone 파라미터 (ResolveSpawnParams가 세팅, SpawnGroup/SampleGroupOffset이 읽음)
        private float _currentGroupRadius;
        private float _currentInterGroupMinDistance;

        public enum GroupDistribution
        {
            Uniform,             // 반경 내 균등
            GaussianFromCenter   // 중심 가까이 더 빽빽
        }

        public void Initialize()
        {
        }

        public void Reset()
        {
            // 모든 몬스터 제거
            for (int i = 0; i < _monsters.Count; i++)
            {
                var entity = _monsters[i];
                if (entity != null && entity.gameObject != null)
                {
                    EntityIdHelper.DestroyEntity(entity.EntityId);
                    Destroy(entity.gameObject);
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

        public void AddMonster(EntityBase entity)
        {
            if (entity == null)
                return;

            if (_monsters.Contains(entity) == false)
            {
                _monsters.Add(entity);
                entity.transform.SetParent(_monsterParent);
            }
        }

        /// <summary>
        /// System_EntityDestroy에서 MonsterTag 확인 후 호출
        /// MonsterManager의 추적 정보에서 몬스터를 제거
        /// </summary>
        public void UnregisterMonsterByEntityId(int entityId)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                if (_monsters[i] != null && _monsters[i].EntityId == entityId)
                {
                    _monsters.RemoveAt(i);
                    return;
                }
            }
        }

        public async Cysharp.Threading.Tasks.UniTask<int> SpawnMonsterAtPosition(Vector3 position, Vector2Int chunkCoord, int monsterTableId, bool isOriginalSpawn = true)
        {
            if (AR.s.CurrentScene is GameScene gameScene == false)
                return -1;

            Vector3 spawnPos = new Vector3(position.x, position.y, -0.05f);

            // EntityFactory를 통해 몬스터 생성 (Addressable)
            var (entityId, entity) = await EntityFactory.CreateMonster(monsterTableId, spawnPos, gameScene.MonsterRoot);

            if (entityId < 0 || entity == null)
                return -1;

            AddMonster(entity);

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
                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    if (entity.gameObject != null)
                    {
                        entity.gameObject.SetActive(false);

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
            if (HasChunkSpawned(chunkCoord))
                return;

            if (AR.s.Map == null)
                return;

            if (AR.s.Map.TryGetChunkSpawnPositions(chunkCoord, out var spawnPositions) == false)
                return;

            if (spawnPositions.Count == 0)
                return;

            // Zone별 스폰 파라미터 룩업 (Cap 방식). 시트 미정의/누락 시 인스펙터 값 fallback.
            int zone = AR.s.Map.GetZone(chunkCoord);
            ResolveSpawnParams(zone,
                out Vector2Int mainCountRange, out Vector2Int mainSizeRange,
                out Vector2Int subCountRange, out Vector2Int subSizeRange,
                out float groupRadius, out float interGroupMinDistance);

            // 다른 메서드(SpawnGroup, SampleGroupOffset)와 공유하기 위해 현재 청크의 값으로 임시 세팅
            _currentGroupRadius = groupRadius;
            _currentInterGroupMinDistance = interGroupMinDistance;

            _placedAnchorsCache.Clear();

            int mainCount = Random.Range(mainCountRange.x, mainCountRange.y + 1);
            for (int i = 0; i < mainCount; i++)
            {
                int size = Random.Range(mainSizeRange.x, mainSizeRange.y + 1);
                SpawnGroup(chunkCoord, spawnPositions, _mainMonsterTableId, size);
            }

            int subCount = Random.Range(subCountRange.x, subCountRange.y + 1);
            for (int i = 0; i < subCount; i++)
            {
                int size = Random.Range(subSizeRange.x, subSizeRange.y + 1);
                SpawnGroup(chunkCoord, spawnPositions, _subMonsterTableId, size);
            }

            // 그룹이 하나도 안 떨어진 경우(거부 샘플링 실패 누적, Zone 1 안전지대 등)에도 청크는 스폰 처리됨으로 간주
            // — 다음 0.5초마다 재시도 방지.
            if (_chunkMonsters.ContainsKey(chunkCoord) == false)
            {
                _chunkMonsters[chunkCoord] = new ChunkMonsterData(chunkCoord);
                _chunkMonsters[chunkCoord].hasSpawned = true;
            }
        }

        /// <summary>
        /// Zone → 스폰 파라미터. ZoneTable 행 없으면 인스펙터 글로벌 값으로 fallback.
        /// </summary>
        private void ResolveSpawnParams(int zone,
            out Vector2Int mainCountRange, out Vector2Int mainSizeRange,
            out Vector2Int subCountRange, out Vector2Int subSizeRange,
            out float groupRadius, out float interGroupMinDistance)
        {
            var row = AR.s.Data != null ? AR.s.Data.GetZone(zone) : null;
            if (row != null)
            {
                mainCountRange = new Vector2Int(row.MainGroupCountMin, row.MainGroupCountMax);
                mainSizeRange  = new Vector2Int(row.MainGroupSizeMin,  row.MainGroupSizeMax);
                subCountRange  = new Vector2Int(row.SubGroupCountMin,  row.SubGroupCountMax);
                subSizeRange   = new Vector2Int(row.SubGroupSizeMin,   row.SubGroupSizeMax);
                groupRadius           = row.GroupRadius           > 0f ? row.GroupRadius           : _groupRadius;
                interGroupMinDistance = row.InterGroupMinDistance > 0f ? row.InterGroupMinDistance : _interGroupMinDistance;
                Debug.Log($"<color=yellow>[ZoneSpawn] zone={zone} capRowId={row.Id} main={mainCountRange.x}~{mainCountRange.y}×{mainSizeRange.x}~{mainSizeRange.y} sub={subCountRange.x}~{subCountRange.y}×{subSizeRange.x}~{subSizeRange.y}</color>");
                return;
            }

            mainCountRange = _mainGroupCount;
            mainSizeRange  = _mainGroupSize;
            subCountRange  = _subGroupCount;
            subSizeRange   = _subGroupSize;
            groupRadius           = _groupRadius;
            interGroupMinDistance = _interGroupMinDistance;
            Debug.Log($"<color=yellow>[ZoneSpawn] zone={zone} FALLBACK(inspector) dataNull={AR.s.Data == null} main={mainCountRange.x}~{mainCountRange.y}×{mainSizeRange.x}~{mainSizeRange.y} sub={subCountRange.x}~{subCountRange.y}×{subSizeRange.x}~{subSizeRange.y}</color>");
        }

        private void SpawnGroup(Vector2Int chunkCoord, List<Vector2Int> spawnPositions, int monsterTableId, int size)
        {
            if (size <= 0 || spawnPositions.Count == 0)
                return;

            // anchor: 거부 샘플링으로 무리 간 최소 거리 확보 (Zone별 값 사용)
            float interGroupSqr = _currentInterGroupMinDistance * _currentInterGroupMinDistance;
            Vector2Int anchorLocal = default;
            bool anchorFound = false;
            for (int attempt = 0; attempt < _anchorRejectionAttempts; attempt++)
            {
                Vector2Int candidate = spawnPositions[Random.Range(0, spawnPositions.Count)];
                Vector2 candidateWorld = ChunkLocalToWorld(chunkCoord, candidate);

                bool tooClose = false;
                for (int i = 0; i < _placedAnchorsCache.Count; i++)
                {
                    if ((_placedAnchorsCache[i] - candidateWorld).sqrMagnitude < interGroupSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose == false)
                {
                    anchorLocal = candidate;
                    anchorFound = true;
                    break;
                }
            }

            if (anchorFound == false)
                return; // 청크가 좁아 더 둘 자리 없음

            Vector2 anchorWorld = ChunkLocalToWorld(chunkCoord, anchorLocal);
            _placedAnchorsCache.Add(anchorWorld);

            for (int i = 0; i < size; i++)
            {
                Vector2 offset = SampleGroupOffset();
                Vector3 worldPos = new Vector3(anchorWorld.x + offset.x, anchorWorld.y + offset.y, -0.05f);
                SpawnMonsterAtPosition(worldPos, chunkCoord, monsterTableId).Forget();
            }
        }

        private static Vector2 ChunkLocalToWorld(Vector2Int chunkCoord, Vector2Int localPos)
        {
            int chunkSize = AR.s.Map.chunkSize;
            return new Vector2(chunkCoord.x * chunkSize + localPos.x, chunkCoord.y * chunkSize + localPos.y);
        }

        private Vector2 SampleGroupOffset()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = _currentGroupRadius;

            float r;
            if (_groupDistribution == GroupDistribution.GaussianFromCenter)
            {
                // Box-Muller로 표준정규 → |z|*sigma. sigma=R/2, 그 이상은 R로 클램프 (~95% 안에 들어옴)
                float u1 = 1f - Random.value;
                float u2 = 1f - Random.value;
                float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
                r = Mathf.Min(Mathf.Abs(z) * (radius * 0.5f), radius);
            }
            else
            {
                // Uniform disk: sqrt(u) * R
                r = radius * Mathf.Sqrt(Random.value);
            }

            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        public async Cysharp.Threading.Tasks.UniTask<int> RespawnMonsterInChunk(Vector2Int chunkCoord, Vector2Int spawnPos)
        {
            Vector3 worldPos = new Vector3(
                chunkCoord.x * AR.s.Map.chunkSize + spawnPos.x,
                chunkCoord.y * AR.s.Map.chunkSize + spawnPos.y,
                -0.05f
            );

            // 리스폰 시점에 어떤 종이 죽었는지 추적하지 않으므로 메인 종으로 통일.
            // 종별 추적이 필요해지면 ChunkMonsterData에 TableId 기록 추가.
            return await SpawnMonsterAtPosition(worldPos, chunkCoord, _mainMonsterTableId, false);
        }

        private readonly List<Vector2Int> _expiredChunksCache = new();

        public void CleanupExpiredChunkMonsters()
        {
            _expiredChunksCache.Clear();

            foreach (var kvp in _chunkMonsters)
            {
                ChunkMonsterData chunkData = kvp.Value;
                if (Time.time - chunkData.lastActiveTime > _chunkMonsterLifetime)
                {
                    // 만료된 청크의 몬스터들 정리
                    for (int i = 0; i < chunkData.spawnedMonsterIds.Count; i++)
                    {
                        int entityId = chunkData.spawnedMonsterIds[i];
                        if (AR.s.Message.TryGetEntity(entityId, out var entity))
                        {
                            _monsters.Remove(entity);
                            EntityIdHelper.DestroyEntity(entityId);
                            Destroy(entity.gameObject);
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
