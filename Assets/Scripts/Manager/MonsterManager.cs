#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Scene;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private const float DefaultMoveRadius = 0.3f;

        private readonly List<EntityBase> _monsters = new();
        private readonly Dictionary<Vector2Int, ChunkMonsterData> _chunkMonsters = new();
        private readonly Dictionary<int, MonsterSpawnOrigin> _monsterOrigins = new();
        private readonly List<PendingSpawnReservation> _pendingSpawnReservations = new();
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

        [Header("Monster Spawn Director")]
        [SerializeField] private float _spawnCheckInterval = 0.5f;
        [SerializeField] private float _minimumSpawnDistance = 10f;
        [SerializeField] private float _maximumSpawnDistance = 24f;
        [SerializeField] private int _maxSpawnGroupsPerCheck = 1;
        [SerializeField] private int _maxSpawnEntitiesPerCheck = 12;
        [SerializeField] private Vector2 _slotCooldownRange = new Vector2(45f, 75f);

        [Header("Monster Activation")]
        [SerializeField] private float _activationDistance = 25f;
        [SerializeField] private float _deactivationDistance = 30f;

        public enum GroupDistribution
        {
            Uniform,
            GaussianFromCenter
        }

        public float MinimumSpawnDistanceSqr =>
            Mathf.Max(0f, _minimumSpawnDistance) * Mathf.Max(0f, _minimumSpawnDistance);

        public float SpawnCheckInterval => Mathf.Max(0.02f, _spawnCheckInterval);

        public float MaximumSpawnDistanceSqr
        {
            get
            {
                float maximum = Mathf.Max(0f, Mathf.Max(_minimumSpawnDistance, _maximumSpawnDistance));
                return maximum * maximum;
            }
        }

        public int MaxSpawnGroupsPerCheck => Mathf.Max(0, _maxSpawnGroupsPerCheck);
        public int MaxSpawnEntitiesPerCheck => Mathf.Max(0, _maxSpawnEntitiesPerCheck);

        public void Initialize()
        {
        }

        public void Reset()
        {
            _monsterOrigins.Clear();
            _chunkMonsters.Clear();
            _pendingSpawnReservations.Clear();

            while (_monsters.Count > 0)
            {
                EntityBase entity = _monsters[_monsters.Count - 1];
                _monsters.RemoveAt(_monsters.Count - 1);
                if (entity != null)
                {
                    DestroyMonsterEntity(entity.EntityId, entity);
                }
            }
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
        /// System_EntityDestroy에서 MonsterTag 확인 후 호출한다.
        /// 일반 사망/제거는 출신 슬롯의 생존 수를 감소시킨다.
        /// </summary>
        public void UnregisterMonsterByEntityId(int entityId)
        {
            RemoveMonsterFromGlobalList(entityId);

            if (_monsterOrigins.TryGetValue(entityId, out MonsterSpawnOrigin origin) == false)
                return;

            _monsterOrigins.Remove(entityId);
            if (_chunkMonsters.TryGetValue(origin.ChunkCoord, out ChunkMonsterData? chunkMonsters) &&
                chunkMonsters.GenerationId == origin.GenerationId)
            {
                RemoveEntityId(chunkMonsters.SpawnedMonsterIds, entityId);
            }

            if (AR.s.Map == null ||
                AR.s.Map.TryGetChunkSpawnRuntime(origin.ChunkCoord, origin.GenerationId,
                    out ChunkSpawnRuntimeData runtime) == false ||
                origin.SlotIndex < 0 || origin.SlotIndex >= runtime.Slots.Length)
            {
                return;
            }

            MonsterSpawnSlotState slot = runtime.Slots[origin.SlotIndex];
            slot.AliveMemberCount = Mathf.Max(0, slot.AliveMemberCount - 1);
            if (slot.AliveMemberCount == 0)
            {
                BeginSlotCooldown(slot);
            }
        }

        public void ResolveSpawnSlotGenerationParams(int zone,
            out Vector2Int mainCountRange,
            out Vector2Int subCountRange,
            out float interGroupMinDistance)
        {
            ResolveSpawnParams(zone,
                out mainCountRange, out _,
                out subCountRange, out _,
                out _, out interGroupMinDistance);
        }

        public bool TrySpawnSlot(Vector2Int chunkCoord, int generationId, int slotIndex,
            int remainingEntityBudget, out int scheduledEntityCount)
        {
            scheduledEntityCount = 0;
            if (remainingEntityBudget <= 0 || AR.s.Map == null)
                return false;
            if (AR.s.Map.TryGetActiveChunkSpawnRuntime(chunkCoord, out ChunkSpawnRuntimeData runtime) == false ||
                runtime.GenerationId != generationId ||
                slotIndex < 0 || slotIndex >= runtime.Slots.Length)
            {
                return false;
            }

            MonsterSpawnSlotState slot = runtime.Slots[slotIndex];
            if (slot.IsDisabled == true || slot.IsPending == true || slot.AliveMemberCount > 0 ||
                Time.time < slot.NextSpawnTime)
            {
                return false;
            }
            if (AR.s.Map.IsMonsterSpawnSlotTerrainValid(chunkCoord, generationId, slotIndex, true) == false)
                return false;

            int zone = AR.s.Map.GetZone(chunkCoord);
            ResolveSpawnParams(zone,
                out _, out Vector2Int mainSizeRange,
                out _, out Vector2Int subSizeRange,
                out float groupRadius, out float interGroupMinDistance);

            Vector2 anchorWorld = AR.s.Map.GetMonsterSpawnSlotWorldPosition(chunkCoord, slot.LocalPosition);
            if (AR.s.Map.IsMonsterSpawnAnchorSeparated(anchorWorld, interGroupMinDistance,
                    chunkCoord, generationId, slotIndex) == false)
            {
                return false;
            }

            Vector2Int sizeRange = slot.GroupKind == MonsterSpawnGroupKind.Main
                ? mainSizeRange
                : subSizeRange;
            int minimumSize = Mathf.Max(0, Mathf.Min(sizeRange.x, sizeRange.y));
            int maximumSize = Mathf.Max(0, Mathf.Max(sizeRange.x, sizeRange.y));
            if (maximumSize <= 0)
            {
                slot.IsDisabled = true;
                return false;
            }

            int targetSize = UnityEngine.Random.Range(minimumSize, maximumSize + 1);
            if (targetSize <= 0 || targetSize > remainingEntityBudget)
                return false;

            GlobalEnum.BiomeType chunkBiome = AR.s.Map.GetChunkBiome(chunkCoord);
            int fallbackMonsterId = slot.GroupKind == MonsterSpawnGroupKind.Main
                ? _mainMonsterTableId
                : _subMonsterTableId;
            int monsterTableId = BiomeMonsterMapping.SampleMonsterId(chunkBiome, fallbackMonsterId);
            float monsterRadius = ResolveMonsterMoveRadius(monsterTableId);

            // Addressables 요청 전에 슬롯을 예약한다.
            int pendingVersion = slot.BeginPending();
            List<Vector3> positions = BuildGroupPositions(chunkCoord, generationId, slotIndex,
                pendingVersion, anchorWorld, targetSize, groupRadius, monsterRadius);
            if (positions.Count == 0)
            {
                slot.CancelPending();
                return false;
            }

            slot.LastUsedTime = Time.time;
            for (int i = 0; i < positions.Count; i++)
            {
                _pendingSpawnReservations.Add(new PendingSpawnReservation(
                    chunkCoord, generationId, slotIndex, pendingVersion,
                    positions[i], monsterRadius));
            }

            scheduledEntityCount = positions.Count;
            SpawnSlotGroupAsync(chunkCoord, generationId, slotIndex, pendingVersion, monsterTableId,
                positions, monsterRadius, interGroupMinDistance).Forget();
            return true;
        }

        public void ActivateChunkMonsters(Vector2Int chunkCoord)
        {
            if (_chunkMonsters.TryGetValue(chunkCoord, out ChunkMonsterData? chunkData) == false)
                return;
            if (AR.s.Map == null || AR.s.Map.IsChunkGenerationActive(chunkCoord, chunkData.GenerationId) == false)
                return;

            for (int i = 0; i < chunkData.SpawnedMonsterIds.Count; i++)
            {
                SynchronizeActivationState(chunkData.SpawnedMonsterIds[i]);
            }
        }

        public void DeactivateChunkMonsters(Vector2Int chunkCoord)
        {
            RemovePendingReservations(chunkCoord, -1, -1, -1);

            if (_chunkMonsters.TryGetValue(chunkCoord, out ChunkMonsterData? chunkData) == false)
                return;

            for (int i = 0; i < chunkData.SpawnedMonsterIds.Count; i++)
            {
                int entityId = chunkData.SpawnedMonsterIds[i];
                if (AR.s.Component.TryGetComponent<ActivationDistanceComponent>(entityId, out var activation))
                {
                    activation.IsActivated = false;
                    AR.s.Component.SetComponent(entityId, activation);
                }

                if (AR.s.Message.TryGetEntity(entityId, out var entity) && entity.gameObject != null)
                {
                    entity.gameObject.SetActive(false);
                }
            }
        }

        public void ExpireChunkMonsters(Vector2Int chunkCoord, int generationId)
        {
            RemovePendingReservations(chunkCoord, generationId, -1, -1);

            if (_chunkMonsters.TryGetValue(chunkCoord, out ChunkMonsterData? chunkData) == false ||
                chunkData.GenerationId != generationId)
            {
                return;
            }

            _chunkMonsters.Remove(chunkCoord);
            for (int i = chunkData.SpawnedMonsterIds.Count - 1; i >= 0; i--)
            {
                int entityId = chunkData.SpawnedMonsterIds[i];
                if (_monsterOrigins.TryGetValue(entityId, out MonsterSpawnOrigin origin) &&
                    origin.GenerationId == generationId)
                {
                    _monsterOrigins.Remove(entityId);
                }

                RemoveMonsterFromGlobalList(entityId);
                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    DestroyMonsterEntity(entityId, entity);
                }
                else
                {
                    DestroyMonsterEntityWithoutRegistryEntry(entityId);
                }
            }
        }

        public bool IsMonsterSourceChunkActive(int entityId)
        {
            if (_monsterOrigins.TryGetValue(entityId, out MonsterSpawnOrigin origin) == false)
                return true;
            if (AR.s.Map == null)
                return false;

            return AR.s.Map.IsChunkGenerationActive(origin.ChunkCoord, origin.GenerationId);
        }

        private void ResolveSpawnParams(int zone,
            out Vector2Int mainCountRange, out Vector2Int mainSizeRange,
            out Vector2Int subCountRange, out Vector2Int subSizeRange,
            out float groupRadius, out float interGroupMinDistance)
        {
            var row = AR.s.Data != null ? AR.s.Data.GetZone(zone) : null;
            if (row != null)
            {
                mainCountRange = new Vector2Int(row.MainGroupCountMin, row.MainGroupCountMax);
                mainSizeRange = new Vector2Int(row.MainGroupSizeMin, row.MainGroupSizeMax);
                subCountRange = new Vector2Int(row.SubGroupCountMin, row.SubGroupCountMax);
                subSizeRange = new Vector2Int(row.SubGroupSizeMin, row.SubGroupSizeMax);
                groupRadius = row.GroupRadius > 0f ? row.GroupRadius : _groupRadius;
                interGroupMinDistance = row.InterGroupMinDistance > 0f
                    ? row.InterGroupMinDistance
                    : _interGroupMinDistance;
                return;
            }

            mainCountRange = _mainGroupCount;
            mainSizeRange = _mainGroupSize;
            subCountRange = _subGroupCount;
            subSizeRange = _subGroupSize;
            groupRadius = _groupRadius;
            interGroupMinDistance = _interGroupMinDistance;
        }

        private async UniTaskVoid SpawnSlotGroupAsync(Vector2Int chunkCoord, int generationId,
            int slotIndex, int pendingVersion, int monsterTableId, List<Vector3> positions,
            float monsterRadius, float interGroupMinDistance)
        {
            int successfulSpawnCount = 0;
            try
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    if (IsSpawnReservationValid(chunkCoord, generationId, slotIndex, pendingVersion,
                            interGroupMinDistance) == false)
                    {
                        break;
                    }

                    (int entityId, EntityBase? entity) result;
                    try
                    {
                        result = await CreateMonsterAsync(monsterTableId, positions[i], chunkCoord);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"[MonsterSpawn] CreateMonster failed: {exception.Message}");
                        continue;
                    }

                    if (result.entityId < 0 || result.entity == null)
                        continue;

                    if (IsSpawnReservationValid(chunkCoord, generationId, slotIndex, pendingVersion,
                            interGroupMinDistance) == false ||
                        AR.s.Map.IsMonsterSpawnWorldPositionValid(chunkCoord, generationId,
                            positions[i]) == false ||
                        CanPlaceMonsterAt(positions[i], monsterRadius,
                            chunkCoord, generationId, slotIndex, pendingVersion) == false)
                    {
                        DestroyMonsterEntity(result.entityId, result.entity);
                        continue;
                    }

                    AddMonster(result.entity);
                    EntityFactory.AddActivationComponent(result.entityId,
                        _activationDistance, _deactivationDistance);
                    RegisterMonsterOrigin(result.entityId, chunkCoord, generationId, slotIndex);
                    SynchronizeActivationState(result.entityId);
                    successfulSpawnCount++;
                }
            }
            finally
            {
                RemovePendingReservations(chunkCoord, generationId, slotIndex, pendingVersion);

                if (AR.s.Map != null &&
                    AR.s.Map.TryGetChunkSpawnRuntime(chunkCoord, generationId,
                        out ChunkSpawnRuntimeData runtime) &&
                    slotIndex >= 0 && slotIndex < runtime.Slots.Length)
                {
                    MonsterSpawnSlotState slot = runtime.Slots[slotIndex];
                    if (slot.IsPendingReservation(pendingVersion) == true)
                    {
                        slot.IsPending = false;
                        if (successfulSpawnCount > 0 && slot.AliveMemberCount == 0)
                        {
                            BeginSlotCooldown(slot);
                        }
                    }
                }
            }
        }

        private async UniTask<(int entityId, EntityBase? entity)> CreateMonsterAsync(
            int monsterTableId, Vector3 position, Vector2Int chunkCoord)
        {
            if (AR.s.CurrentScene is GameScene gameScene == false)
                return (-1, null);

            int monsterLevel = AR.s.Map.GetZone(chunkCoord);
            return await EntityFactory.CreateMonster(monsterTableId, position,
                gameScene.MonsterRoot, monsterLevel);
        }

        private List<Vector3> BuildGroupPositions(Vector2Int chunkCoord, int generationId,
            int slotIndex, int pendingVersion, Vector2 anchorWorld, int targetSize,
            float groupRadius, float monsterRadius)
        {
            List<Vector3> result = new List<Vector3>(targetSize);
            int attemptsPerMember = Mathf.Max(1, _anchorRejectionAttempts);
            float memberMinimumDistance = monsterRadius * 2f;
            float memberDistanceSqr = memberMinimumDistance * memberMinimumDistance;

            for (int memberIndex = 0; memberIndex < targetSize; memberIndex++)
            {
                bool found = false;
                for (int attempt = 0; attempt < attemptsPerMember; attempt++)
                {
                    Vector2 offset = SampleGroupOffset(groupRadius);
                    Vector3 position = new Vector3(anchorWorld.x + offset.x,
                        anchorWorld.y + offset.y, -0.05f);
                    if (AR.s.Map.IsMonsterSpawnWorldPositionValid(chunkCoord, generationId,
                            position) == false ||
                        CanPlaceMonsterAt(position, monsterRadius,
                            chunkCoord, generationId, slotIndex, pendingVersion) == false)
                    {
                        continue;
                    }

                    bool overlapsPlannedMember = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        Vector2 delta = (Vector2)(result[i] - position);
                        if (delta.sqrMagnitude < memberDistanceSqr)
                        {
                            overlapsPlannedMember = true;
                            break;
                        }
                    }

                    if (overlapsPlannedMember == true)
                        continue;

                    result.Add(position);
                    found = true;
                    break;
                }

                if (found == false)
                    break;
            }

            return result;
        }

        private bool IsSpawnReservationValid(Vector2Int chunkCoord, int generationId,
            int slotIndex, int pendingVersion, float interGroupMinDistance)
        {
            if (AR.s.Map == null || AR.s.MyPlayer == null ||
                AR.s.Map.TryGetActiveChunkSpawnRuntime(chunkCoord,
                    out ChunkSpawnRuntimeData runtime) == false ||
                runtime.GenerationId != generationId ||
                slotIndex < 0 || slotIndex >= runtime.Slots.Length)
            {
                return false;
            }

            MonsterSpawnSlotState slot = runtime.Slots[slotIndex];
            if (slot.IsPendingReservation(pendingVersion) == false || slot.IsDisabled == true ||
                AR.s.Map.IsMonsterSpawnSlotTerrainValid(chunkCoord, generationId,
                    slotIndex, true) == false)
            {
                return false;
            }

            Vector2 anchorWorld = AR.s.Map.GetMonsterSpawnSlotWorldPosition(chunkCoord,
                slot.LocalPosition);
            Vector2 playerPosition = AR.s.MyPlayer.transform.position;
            float distanceSqr = (anchorWorld - playerPosition).sqrMagnitude;
            if (distanceSqr < MinimumSpawnDistanceSqr || distanceSqr > MaximumSpawnDistanceSqr)
                return false;

            return AR.s.Map.IsMonsterSpawnAnchorSeparated(anchorWorld, interGroupMinDistance,
                chunkCoord, generationId, slotIndex);
        }

        private bool CanPlaceMonsterAt(Vector3 position, float radius,
            Vector2Int chunkCoord, int generationId, int slotIndex, int pendingVersion)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                EntityBase entity = _monsters[i];
                if (entity == null ||
                    AR.s.Component.TryGetComponent<TransformComponent>(entity.EntityId,
                        out var transform) == false)
                {
                    continue;
                }

                float otherRadius = DefaultMoveRadius;
                if (AR.s.Component.TryGetComponent<ColliderComponent>(entity.EntityId, out var collider))
                {
                    otherRadius = collider.MoveRadius;
                }

                float minimumDistance = radius + otherRadius;
                Vector2 delta = transform.Position - (Vector2)position;
                if (delta.sqrMagnitude < minimumDistance * minimumDistance)
                    return false;
            }

            for (int i = 0; i < _pendingSpawnReservations.Count; i++)
            {
                PendingSpawnReservation reservation = _pendingSpawnReservations[i];
                if (reservation.ChunkCoord == chunkCoord &&
                    reservation.GenerationId == generationId &&
                    reservation.SlotIndex == slotIndex &&
                    reservation.PendingVersion == pendingVersion)
                {
                    continue;
                }

                float minimumDistance = radius + reservation.Radius;
                Vector2 delta = (Vector2)(reservation.Position - position);
                if (delta.sqrMagnitude < minimumDistance * minimumDistance)
                    return false;
            }

            return true;
        }

        private void RegisterMonsterOrigin(int entityId, Vector2Int chunkCoord,
            int generationId, int slotIndex)
        {
            if (_chunkMonsters.TryGetValue(chunkCoord, out ChunkMonsterData? chunkData) == false ||
                chunkData.GenerationId != generationId)
            {
                chunkData = new ChunkMonsterData(chunkCoord, generationId);
                _chunkMonsters[chunkCoord] = chunkData;
            }

            chunkData.SpawnedMonsterIds.Add(entityId);
            _monsterOrigins[entityId] = new MonsterSpawnOrigin(chunkCoord, generationId, slotIndex);

            if (AR.s.Map.TryGetChunkSpawnRuntime(chunkCoord, generationId,
                    out ChunkSpawnRuntimeData runtime) &&
                slotIndex >= 0 && slotIndex < runtime.Slots.Length)
            {
                runtime.Slots[slotIndex].AliveMemberCount++;
            }
        }

        private void SynchronizeActivationState(int entityId)
        {
            if (AR.s.Component.TryGetComponent<ActivationDistanceComponent>(entityId,
                    out var activation) == false ||
                AR.s.Message.TryGetEntity(entityId, out var entity) == false ||
                entity.gameObject == null)
            {
                return;
            }

            bool shouldActivate = false;
            if (IsMonsterSourceChunkActive(entityId) == true && AR.s.MyPlayer != null &&
                AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
            {
                Vector2 playerPosition = AR.s.MyPlayer.transform.position;
                shouldActivate = (transform.Position - playerPosition).sqrMagnitude <=
                                 activation.ActivationDistanceSqr;
            }

            activation.IsActivated = shouldActivate;
            AR.s.Component.SetComponent(entityId, activation);
            entity.gameObject.SetActive(shouldActivate);
        }

        private float ResolveMonsterMoveRadius(int monsterTableId)
        {
            var table = AR.s.Data != null ? AR.s.Data.GetMonster(monsterTableId) : null;
            return table != null && table.MoveRadius > 0f ? table.MoveRadius : DefaultMoveRadius;
        }

        private Vector2 SampleGroupOffset(float groupRadius)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radius;
            if (_groupDistribution == GroupDistribution.GaussianFromCenter)
            {
                float u1 = 1f - UnityEngine.Random.value;
                float u2 = 1f - UnityEngine.Random.value;
                float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) *
                          Mathf.Cos(2f * Mathf.PI * u2);
                radius = Mathf.Min(Mathf.Abs(z) * (groupRadius * 0.5f), groupRadius);
            }
            else
            {
                radius = groupRadius * Mathf.Sqrt(UnityEngine.Random.value);
            }

            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        private void BeginSlotCooldown(MonsterSpawnSlotState slot)
        {
            float minimum = Mathf.Max(0f, Mathf.Min(_slotCooldownRange.x, _slotCooldownRange.y));
            float maximum = Mathf.Max(0f, Mathf.Max(_slotCooldownRange.x, _slotCooldownRange.y));
            slot.NextSpawnTime = Time.time + UnityEngine.Random.Range(minimum, maximum);
            slot.LastUsedTime = Time.time;
        }

        private void RemovePendingReservations(Vector2Int chunkCoord, int generationId, int slotIndex,
            int pendingVersion)
        {
            for (int i = _pendingSpawnReservations.Count - 1; i >= 0; i--)
            {
                PendingSpawnReservation reservation = _pendingSpawnReservations[i];
                if (reservation.ChunkCoord != chunkCoord)
                    continue;
                if (generationId >= 0 && reservation.GenerationId != generationId)
                    continue;
                if (slotIndex >= 0 && reservation.SlotIndex != slotIndex)
                    continue;
                if (pendingVersion >= 0 && reservation.PendingVersion != pendingVersion)
                    continue;

                _pendingSpawnReservations.RemoveAt(i);
            }
        }

        private void RemoveMonsterFromGlobalList(int entityId)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                EntityBase entity = _monsters[i];
                if (entity == null || entity.EntityId != entityId)
                    continue;

                int lastIndex = _monsters.Count - 1;
                _monsters[i] = _monsters[lastIndex];
                _monsters.RemoveAt(lastIndex);
                return;
            }
        }

        private static void RemoveEntityId(List<int> entityIds, int entityId)
        {
            for (int i = 0; i < entityIds.Count; i++)
            {
                if (entityIds[i] != entityId)
                    continue;

                int lastIndex = entityIds.Count - 1;
                entityIds[i] = entityIds[lastIndex];
                entityIds.RemoveAt(lastIndex);
                return;
            }
        }

        private static void DestroyMonsterEntity(int entityId, EntityBase entity)
        {
            var animationSystem = AR.s.System.GetSystem<Systems.System_Animation>();
            if (animationSystem != null)
            {
                animationSystem.UnregisterSpriteAnimation(entityId);
            }

            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.UnregisterEntity(entityId);
            }

            entity.OnEntityDestroy();
            AR.s.Entity.DestroyEntity(entityId);
            UnityEngine.Object.Destroy(entity.gameObject);
        }

        private static void DestroyMonsterEntityWithoutRegistryEntry(int entityId)
        {
            var animationSystem = AR.s.System.GetSystem<Systems.System_Animation>();
            if (animationSystem != null)
            {
                animationSystem.UnregisterSpriteAnimation(entityId);
            }

            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                GameObject gameObject = renderSystem.GetGameObject(entityId);
                renderSystem.UnregisterEntity(entityId);
                if (gameObject != null)
                {
                    UnityEngine.Object.Destroy(gameObject);
                }
            }

            AR.s.Entity.DestroyEntity(entityId);
        }

        private readonly struct MonsterSpawnOrigin
        {
            public readonly Vector2Int ChunkCoord;
            public readonly int GenerationId;
            public readonly int SlotIndex;

            public MonsterSpawnOrigin(Vector2Int chunkCoord, int generationId, int slotIndex)
            {
                ChunkCoord = chunkCoord;
                GenerationId = generationId;
                SlotIndex = slotIndex;
            }
        }

        private readonly struct PendingSpawnReservation
        {
            public readonly Vector2Int ChunkCoord;
            public readonly int GenerationId;
            public readonly int SlotIndex;
            public readonly int PendingVersion;
            public readonly Vector3 Position;
            public readonly float Radius;

            public PendingSpawnReservation(Vector2Int chunkCoord, int generationId,
                int slotIndex, int pendingVersion, Vector3 position, float radius)
            {
                ChunkCoord = chunkCoord;
                GenerationId = generationId;
                SlotIndex = slotIndex;
                PendingVersion = pendingVersion;
                Position = position;
                Radius = radius;
            }
        }
    }
}
