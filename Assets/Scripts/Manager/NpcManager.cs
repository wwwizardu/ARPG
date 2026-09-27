#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.AI;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.History;
using ARPG.Map;
using ARPG.Npc.Narrative;
using ARPG.Scene;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Village;

namespace ARPG.Npc
{
    public partial class NpcManager : MonoBehaviour
    {
        private Dictionary<int, NpcSaveData> _npcSaveDict = new();
        private Dictionary<Vector2Int, List<int>> _chunkNpcs = new();
        // 결정론적 오프스크린 시뮬레이션 순서. Dictionary 열거 순서에 의존하지 않는다.
        private List<int> _simulationNpcIds = new();
        private int _simulationCursor;
        private Dictionary<int, NpcPartyState> _npcPartyDict = new();
        private List<int> _simulationPartyIds = new();
        private int _simulationWorkCursor;
        private int _nextPartyId = 1;
        // NPC 간 호감도 (양방향 대칭, sparse — 만난 적 있는 페어만 등록)
        private Dictionary<NpcPairKey, int> _npcAffinity = new();
        // 플레이어↔NPC 관계. NPC ID가 키이며, 청크 unload와 무관하게 유지한다.
        private Dictionary<int, PlayerRelation> _playerRelations = new();
        // 호감도 clamp 범위 (drift 누적 폭주 방지)
        public const int AFFINITY_MIN = -100;
        public const int AFFINITY_MAX = 100;
        // NPC 간 유형 관계 (가족/친구/원한 등). 양방향 인덱스 — 같은 엔트리가 From/To 두 키에 등록됨
        private Dictionary<int, List<NpcRelationEntry>> _npcRelations = new();
        // NPC 서사(대화·소망) 세션 런타임 — 휘발성(세이브 대상 아님). static 대신 매니저가 소유해 Reset()에서 정리
        private readonly NpcNarrativeRuntime _narrative = new();
        public NpcNarrativeRuntime Narrative => _narrative;
        private Transform? _npcParent;
        private bool _isInitialLoaded = false;
        private int _chunkSize;

        [Header("NPC Activation")]
        [SerializeField] private float _activationDistance = 30f;
        [SerializeField] private float _deactivationDistance = 35f;

        private const float DEFAULT_NPC_MOVE_RADIUS = 0.30f;

        public void Initialize()
        {
            // RegisterNpcsFromMapFile/Load 경로를 타지 않아도 SpawnNewNpc가 바로 동작하도록 항상 세팅
            _chunkSize = AR.s.Map.chunkSize;

            var savedNpcs = AR.s.Data.NpcSaveDatas;
            if (savedNpcs != null && savedNpcs.Count > 0)
            {
                Load(savedNpcs, AR.s.Map.chunkSize);
            }

            LoadAffinity(AR.s.Data.NpcAffinityList);
            LoadPlayerRelations(AR.s.Data.PlayerNpcRelationList);
            LoadRelations(AR.s.Data.NpcRelationList);
            LoadParties(AR.s.Data.NpcPartyStates);
            ValidateCompanionState();
        }

        public void Reset()
        {
            foreach (var kvp in _npcSaveDict)
            {
                NpcSaveData saveData = kvp.Value;
                if (saveData.IsActive == false)
                    continue;

                int entityId = kvp.Key;
                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    AR.s.Entity.DestroyEntity(entityId);
                    Destroy(entity.gameObject);
                }
            }

            _npcSaveDict.Clear();
            _chunkNpcs.Clear();
            _simulationNpcIds.Clear();
            _simulationCursor = 0;
            _npcPartyDict.Clear();
            _simulationPartyIds.Clear();
            _simulationWorkCursor = 0;
            _nextPartyId = 1;
            _npcAffinity.Clear();
            _playerRelations.Clear();
            _npcRelations.Clear();
            _narrative.Reset();
            _nameBag.Clear();
            _nameBagCursor = 0;
            _usedNames.Clear();
            _isInitialLoaded = false;
        }

        public void SetNpcRoot(Transform inNpcRoot)
        {
            _npcParent = inNpcRoot;
        }

        /// <summary>
        /// 비활성 또는 비동기 스폰 중인 생존 NPC 이동 원이 건물 footprint와 겹치는지 검사한다.
        /// 활성 NPC는 CollisionUtil.IsCreatureOverlappingFootprint에서 검사한다.
        /// </summary>
        public bool HasNpcInFootprint(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return false;

            Vector2 min = new Vector2(worldX, worldY);
            Vector2 max = new Vector2(worldX + width, worldY + height);
            for (int i = 0; i < _simulationNpcIds.Count; i++)
            {
                int entityId = _simulationNpcIds[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;
                if (saveData.Condition == CharacterConditions.Dead ||
                    saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                if (saveData.IsActive == true)
                    continue;

                Vector2 position = saveData.Position;
                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId,
                        out var transform) == true)
                    position = transform.Position;

                float moveRadius = GetNpcMoveRadius(entityId, saveData);
                if (CollisionUtil.CircleOverlapsAabb(position, moveRadius, min,
                        max) == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 이미 좌표가 확정된 건물 footprint 안의 NPC를 가장 가까운 정적 빈 타일로 이동한다.
        /// 저장 위치와 활성 ECS 위치, 청크 인덱스를 함께 갱신한다.
        /// </summary>
        public int RelocateNpcsOutsideFootprint(int worldX, int worldY,
            int width, int height)
        {
            if (width <= 0 || height <= 0)
                return 0;

            int relocated = 0;
            Vector2 min = new Vector2(worldX, worldY);
            Vector2 max = new Vector2(worldX + width, worldY + height);
            for (int i = 0; i < _simulationNpcIds.Count; i++)
            {
                int entityId = _simulationNpcIds[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;
                if (saveData.Condition == CharacterConditions.Dead ||
                    saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;

                Vector2 position = saveData.Position;
                bool hasTransform = AR.s.Component.TryGetComponent<TransformComponent>(
                    entityId, out var transform);
                if (hasTransform == true)
                    position = transform.Position;

                float moveRadius = GetNpcMoveRadius(entityId, saveData);
                if (CollisionUtil.CircleOverlapsAabb(position, moveRadius, min,
                        max) == false)
                    continue;
                if (CollisionUtil.TryFindNearestFree(position, moveRadius,
                        out Vector2 freePosition, hasTransform) == false)
                {
                    Debug.LogError($"[NpcManager] Entity {entityId} cannot escape building footprint " +
                                   $"({worldX}, {worldY}, {width}, {height})");
                    continue;
                }

                SetNpcPosition(entityId, saveData, freePosition);
                relocated++;
                Debug.LogWarning($"[NpcManager] Entity {entityId} moved out of building footprint: " +
                                 $"{position} -> {freePosition}");
            }

            return relocated;
        }

        /// <summary>
        /// 활성 NPC의 강제 위치 보정 시 저장 위치와 청크 인덱스까지 함께 갱신한다.
        /// 길찾기 등 ECS 시스템이 정적 충돌 내부에서 NPC를 복구할 때 사용한다.
        /// </summary>
        public bool TryRelocateMaterializedNpc(int entityId, Vector2 position)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return false;
            if (AR.s.Component.HasComponent<NpcTag>(entityId) == false)
                return false;
            // 던전 동행 NPC는 던전 좌표를 월드 저장 위치·청크 인덱스에 쓰지 않는다 (호출자가 Transform만 보정)
            if (IsCompanionInDungeon(entityId) == true)
                return false;

            SetNpcPosition(entityId, saveData, position);
            return true;
        }

        /// <summary>
        /// 플레이어 청크 + 인접 8청크의 활성 NPC 중 maxRange 이내 가장 가까운 NPC를 찾는다.
        /// 청크 인덱스(_chunkNpcs) 기반이라 NPC 수와 무관하게 비용 일정.
        /// 죽은 NPC, IsActive=false는 제외.
        /// </summary>
        /// <returns>entityId. 없으면 -1. 거리 sqr은 out.</returns>
        public int FindNearestActiveNpc(Vector2 worldPos, float maxRange, out float distanceSqr)
        {
            Vector2Int center = PositionToChunk(worldPos);
            float maxSqr = maxRange * maxRange;
            int bestId = -1;
            float bestSqr = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2Int c = new Vector2Int(center.x + dx, center.y + dy);
                    if (_chunkNpcs.TryGetValue(c, out List<int>? ids) == false)
                        continue;

                    for (int i = 0; i < ids.Count; i++)
                    {
                        int id = ids[i];
                        if (_npcSaveDict.TryGetValue(id, out NpcSaveData? sd) == false)
                            continue;
                        if (sd.IsActive == false)
                            continue;
                        if (sd.Condition == CharacterConditions.Dead)
                            continue;
                        if (AR.s.Component.TryGetComponent<TransformComponent>(id, out var tr) == false)
                            continue;

                        float ddx = tr.Position.x - worldPos.x;
                        float ddy = tr.Position.y - worldPos.y;
                        float sqr = ddx * ddx + ddy * ddy;
                        if (sqr > maxSqr)
                            continue;
                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            bestId = id;
                        }
                    }
                }
            }

            distanceSqr = bestId >= 0 ? bestSqr : float.MaxValue;
            return bestId;
        }

        /// <summary>
        /// NpcSaveData 생성 직후 호출. NpcTable.EquipTableId가 지정돼 있으면 NpcEquipTable 기반 시작 장비를 시드한다.
        /// 신규 NPC 생성 경로 3곳(MapFile/SpawnNewNpc/Visitor)에서 모두 호출 — Load 경로는 호출하지 않음(기존 장비 보존).
        /// </summary>
        private void SeedNpcEquipment(NpcSaveData saveData)
        {
            if (saveData == null)
                return;

            NpcTable? table = AR.s.Data?.GetNpc(saveData.NpcTableId);
            if (table == null || table.EquipTableId <= 0)
                return;

            NpcEquipHelper.SeedInitialEquipment(saveData, table.EquipTableId);
        }

        /// <summary>
        /// 맵 로드 시 1회 호출. MapFileData의 모든 NPC에 EntityId를 발급하고
        /// NpcSaveData로 등록, 청크별 매핑(_chunkNpcs)을 생성한다.
        /// </summary>
        public void RegisterNpcsFromMapFile(List<MapFileObjectData> allNpcObjects, Vector2Int mapFileStartPos, int chunkSize, int villageId = -1)
        {
            if (_isInitialLoaded)
                return;

            _chunkSize = chunkSize;

            for (int i = 0; i < allNpcObjects.Count; i++)
            {
                MapFileObjectData obj = allNpcObjects[i];

                Vector2 worldPos = new Vector2(
                    mapFileStartPos.x + obj.X,
                    mapFileStartPos.y + obj.Y
                );

                // EntityId 발급 (재활용 안 되도록 등록만)
                int entityId = AR.s.Entity.CreateEntity();

                NpcSaveData saveData = new NpcSaveData(obj.ObjectId, worldPos)
                {
                    EntityId = entityId,  // 일관성 — 다른 생성 경로(SpawnNewNpc)도 채워줌
                    Name = PickName(),
                    Description = PickDescription(GetPreferredJobType(obj.ObjectId)),
                };

                SeedNpcEquipment(saveData);

                if (villageId >= 0)
                {
                    saveData.VillageId = villageId;
                    AR.s.Village.RegisterNpcToVillage(villageId, entityId);
                }

                _npcSaveDict[entityId] = saveData;
                AddNpcToSimulation(entityId);

                Vector2Int chunkCoord = PositionToChunk(worldPos);
                AddNpcToChunk(chunkCoord, entityId);
            }

            _isInitialLoaded = true;
        }

        /// <summary>
        /// 청크 활성화 시 호출. _chunkNpcs에서 해당 청크의 NPC 목록을 O(1)로 조회한다.
        /// </summary>
        public void OnChunkActivated(Vector2Int chunkCoord)
        {
            if (_chunkNpcs.TryGetValue(chunkCoord, out List<int>? entityIds) == false)
                return;

            for (int i = 0; i < entityIds.Count; i++)
            {
                int entityId = entityIds[i];

                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;

                // IsSpawning: 이전 호출의 CreateNpc가 아직 대기 중 → 재호출 차단 (이중 스폰 방지)
                if (saveData.IsActive || saveData.IsSpawning)
                    continue;

                if (saveData.Condition == CharacterConditions.Dead)
                    continue;

                SpawnNpc(entityId, saveData).Forget();
            }
        }

        /// <summary>
        /// 청크 비활성화 시 호출. 해당 청크의 NPC를 저장하고 제거한다.
        /// </summary>
        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (_chunkNpcs.TryGetValue(chunkCoord, out List<int>? entityIds) == false)
                return;

            for (int i = 0; i < entityIds.Count; i++)
            {
                int entityId = entityIds[i];

                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;

                if (saveData.IsActive == false)
                    continue;

                SaveAndDeactivateNpc(entityId, saveData, chunkCoord);
            }
        }

        /// <summary>
        /// 런타임에 새 NPC를 생성하는 통합 진입점.
        /// SaveData/_chunkNpcs/VillageData 등록을 모두 처리하며,
        /// 해당 청크가 활성 상태면 실제 엔티티도 즉시 생성한다.
        /// </summary>
        public int SpawnNewNpc(int npcTableId, Vector2 position, int villageId = -1)
        {
            int entityId = AR.s.Entity.CreateEntity();
            NpcSaveData saveData = new NpcSaveData(npcTableId, position)
            {
                EntityId = entityId,
                VillageId = villageId,
                Name = PickName(),
                Description = PickDescription(GetPreferredJobType(npcTableId)),
            };

            SeedNpcEquipment(saveData);

            _npcSaveDict[entityId] = saveData;
            AddNpcToSimulation(entityId);

            Vector2Int chunkCoord = PositionToChunk(position);
            AddNpcToChunk(chunkCoord, entityId);

            if (villageId >= 0)
                AR.s.Village.RegisterNpcToVillage(villageId, entityId);

            if (AR.s.Map.IsChunkActive(chunkCoord))
                SpawnNpc(entityId, saveData).Forget();

            return entityId;
        }

        /// <summary>
        /// 단일 마을 초기화 호환 진입점. 전역 총량 배분을 보장하기 위해 모든 마을을 함께 처리한다.
        /// </summary>
        public void EnsureVillagePopulated(int villageId)
        {
            EnsureAllVillagesPopulated();
        }

        /// <summary>
        /// 새 월드의 물리 모험가를 전역 목표 수 안에서 정착지 추상 인구 비율로 최초 배분한다.
        /// 이미 로드된 월드는 HasBeenPopulated 가드로 보존한다.
        /// </summary>
        public void EnsureAllVillagesPopulated()
        {
            List<VillageData> candidates = CollectPopulationCandidates();
            if (candidates.Count == 0)
                return;

            NpcPopulationState population = AR.s.Data.NpcPopulation;
            if (population.TargetNpcCount < 0)
                population.TargetNpcCount = DetermineGlobalNpcTarget(candidates.Count);

            bool topUpExistingVillages = population.NeedsImmediateTargetTopUp;
            int[] allocations = AllocateInitialNpcCounts(candidates,
                Mathf.Max(0, population.TargetNpcCount - GetLivingNpcCount()));

            for (int i = 0; i < candidates.Count; i++)
            {
                VillageData village = candidates[i];
                bool wasPopulated = village.HasBeenPopulated;
                if (wasPopulated && topUpExistingVillages == false)
                    continue;

                VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
                if (table == null || table.DefaultNpcIds.Count == 0)
                    continue;

                SpawnDefaultNpcsForVillage(village, table, allocations[i]);
                if (wasPopulated == false && village.NpcEntityIds.Count > 0)
                    NpcRelationBackfill.Generate(village, village.NpcEntityIds);
                village.HasBeenPopulated = true;
            }

            population.NeedsImmediateTargetTopUp = false;

            if (population.NextReplenishGameTime <= 0f)
                population.NextReplenishGameTime = AR.s.Time.CurrentGameTime + ReplenishIntervalHours;
        }

        /// <summary>
        /// 전역 목표보다 생존 모험가가 적을 때, 일정 간격으로 한 명만 보충한다.
        /// 경제 인구에는 영향을 주지 않으며, 보충 대상은 초기 배분 비율 대비 부족한 마을이다.
        /// </summary>
        public void TryReplenishGlobalPopulation(float now)
        {
            NpcPopulationState population = AR.s.Data.NpcPopulation;
            if (population.TargetNpcCount <= 0 || now < population.NextReplenishGameTime)
                return;

            population.NextReplenishGameTime = now + ReplenishIntervalHours;
            if (GetLivingNpcCount() >= population.TargetNpcCount)
                return;

            List<VillageData> candidates = CollectPopulationCandidates();
            if (candidates.Count == 0)
                return;

            int villageIndex = SelectReplenishVillage(candidates, population);
            if (villageIndex < 0)
                return;

            VillageData village = candidates[villageIndex];
            VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            if (table == null || table.DefaultNpcIds.Count == 0)
                return;

            int tableIndex = (GetLivingNpcCount() + population.ReplenishVillageCursor) % table.DefaultNpcIds.Count;
            int npcTableId = table.DefaultNpcIds[tableIndex];
            Vector2 spawnPosition = GetRandomSpawnPositionInVillage(village,
                table.SpawnRadius, npcTableId);
            SpawnNewNpc(npcTableId, spawnPosition, village.VillageId);
            population.ReplenishVillageCursor = (villageIndex + 1) % candidates.Count;
        }

        private const float ReplenishIntervalHours = 6f;

        /// <summary>
        /// 맵 규모별 최소 총량과 마을당 목표 인원을 함께 적용해 전역 목표 모험가 수를 정한다.
        /// 마을 수가 줄거나 늘어도 마을당 평균 밀도는 TargetNpcsPerVillage 아래로 내려가지 않는다.
        /// 맵 생성 UI의 규모 선택이 생기면 이 값을 저장된 preset으로 대체한다.
        /// </summary>
        private int DetermineGlobalNpcTarget(int villageCount)
        {
            int width = AR.s.Map.maxChunkX - AR.s.Map.minChunkX + 1;
            int height = AR.s.Map.maxChunkY - AR.s.Map.minChunkY + 1;
            int longestSide = Mathf.Max(width, height);
            int mapSizeTarget = longestSide <= 64
                ? NpcPopulationState.SmallMapTarget
                : NpcPopulationState.LargeMapTarget;
            int villageDensityTarget = Mathf.Max(0, villageCount)
                * NpcPopulationState.TargetNpcsPerVillage;
            return Mathf.Max(mapSizeTarget, villageDensityTarget);
        }

        private List<VillageData> CollectPopulationCandidates()
        {
            List<VillageData> result = new();
            IReadOnlyList<VillageData> allVillages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < allVillages.Count; i++)
            {
                VillageData village = allVillages[i];
                VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
                if (table == null || table.DefaultNpcIds.Count == 0)
                    continue;
                result.Add(village);
            }
            result.Sort((a, b) => a.VillageId.CompareTo(b.VillageId));
            return result;
        }

        private int[] AllocateInitialNpcCounts(List<VillageData> villages, int totalToAllocate)
        {
            int[] weights = new int[villages.Count];
            int[] currentCounts = new int[villages.Count];
            for (int i = 0; i < villages.Count; i++)
            {
                int population = AR.s.History.GetSettlementPopulation(villages[i].SettlementId);
                weights[i] = population > 0 ? population : 1;
                currentCounts[i] = GetLivingNpcCountForVillage(villages[i].VillageId);
            }

            int[] allocations = NpcPopulationAllocator.AllocateAdditionalCounts(
                currentCounts, weights, totalToAllocate,
                NpcPopulationState.MinimumNpcsPerVillage);
            EnsureHandcraftedVillageQuota(villages, currentCounts, allocations);
            return allocations;
        }

        /// <summary>
        /// 손제작 마을(SettlementId&lt;0)은 최소 인구 가중치(1) 탓에 인구 비례 배분에서 0명이 되기 쉽다.
        /// 플레이어가 최초로 만나는 마을이라 1명을 보장한다. 전역 총량 유지를 위해 최다 배분
        /// 절차 마을에서 1명을 이관한다(이관할 마을이 없으면 총량 +1을 감수하고 플레이어 마을을 우선).
        /// </summary>
        private static void EnsureHandcraftedVillageQuota(List<VillageData> villages,
            int[] currentCounts, int[] allocations)
        {
            for (int i = 0; i < villages.Count; i++)
            {
                if (villages[i].SettlementId >= 0 ||
                    currentCounts[i] + allocations[i] > 0)
                    continue;

                int donor = -1;
                int donorMax = 0;
                for (int j = 0; j < villages.Count; j++)
                {
                    if (j == i || villages[j].SettlementId < 0)
                        continue;
                    if (allocations[j] > donorMax &&
                        currentCounts[j] + allocations[j] > 1)
                    {
                        donorMax = allocations[j];
                        donor = j;
                    }
                }

                if (donor >= 0)
                    allocations[donor]--;   // 총량 보존: 최다 절차 마을에서 1명 이관
                allocations[i] = 1;
            }
        }

        private int SelectReplenishVillage(List<VillageData> villages, NpcPopulationState population)
        {
            int[] weights = new int[villages.Count];
            int[] emptyCounts = new int[villages.Count];
            for (int i = 0; i < villages.Count; i++)
            {
                int settlementPopulation = AR.s.History.GetSettlementPopulation(villages[i].SettlementId);
                weights[i] = settlementPopulation > 0 ? settlementPopulation : 1;
            }
            int[] desiredCounts = NpcPopulationAllocator.AllocateAdditionalCounts(
                emptyCounts, weights, population.TargetNpcCount,
                NpcPopulationState.MinimumNpcsPerVillage);

            int bestIndex = -1;
            int bestDeficit = int.MinValue;
            int start = population.ReplenishVillageCursor % villages.Count;
            for (int offset = 0; offset < villages.Count; offset++)
            {
                int index = (start + offset) % villages.Count;
                int deficit = desiredCounts[index]
                    - GetLivingNpcCountForVillage(villages[index].VillageId);
                if (deficit > bestDeficit)
                {
                    bestIndex = index;
                    bestDeficit = deficit;
                }
            }

            return bestIndex;
        }

        private int GetLivingNpcCount()
        {
            int count = 0;
            for (int i = 0; i < _simulationNpcIds.Count; i++)
            {
                int entityId = _simulationNpcIds[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;
                if (saveData.Condition != CharacterConditions.Dead &&
                    saveData.Activity.Phase != NpcActivityPhase.Dead)
                    count++;
            }
            return count;
        }

        private int GetLivingNpcCountForVillage(int villageId)
        {
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return 0;

            int count = 0;
            List<int> villageNpcIds = village.NpcEntityIds;
            for (int i = 0; i < villageNpcIds.Count; i++)
            {
                int entityId = villageNpcIds[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;
                if (saveData.VillageId != villageId || saveData.Condition == CharacterConditions.Dead ||
                    saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                count++;
            }
            return count;
        }

        private void SpawnDefaultNpcsForVillage(VillageData village, VillageTable table, int target)
        {
            int roster = table.DefaultNpcIds.Count;
            if (roster == 0 || target <= 0)
                return;

            for (int i = 0; i < target; i++)
            {
                int npcTableId = table.DefaultNpcIds[i % roster];  // 0..roster-1은 지정 로스터, 초과분은 아키타입 순환
                Vector2 spawnPos = GetRandomSpawnPositionInVillage(village,
                    table.SpawnRadius, npcTableId);
                SpawnNewNpc(npcTableId, spawnPos, village.VillageId);
            }

            Debug.Log($"[NpcManager] Spawned {target} NPCs for village {village.VillageId} (roster={roster}, pop-scaled)");
        }

        private Vector2 GetRandomSpawnPositionInVillage(VillageData village, float radius,
            int npcTableId)
        {
            NpcTable? npcTable = AR.s.Data.GetNpc(npcTableId);
            float moveRadius = npcTable != null && npcTable.MoveRadius > 0f
                ? npcTable.MoveRadius
                : DEFAULT_NPC_MOVE_RADIUS;

            const int MAX_SPAWN_POSITION_ATTEMPTS = 8;
            for (int attempt = 0; attempt < MAX_SPAWN_POSITION_ATTEMPTS; attempt++)
            {
                Vector2 candidate = village.Position;
                if (radius > 0f)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float r = Random.Range(0f, radius);
                    candidate += new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                }

                if (CollisionUtil.IsBlockedAt(candidate, moveRadius) == false)
                    return candidate;
            }

            if (CollisionUtil.TryFindNearestFree(village.Position, moveRadius,
                    out Vector2 fallback) == true)
                return fallback;

            Debug.LogWarning($"[NpcManager] No free spawn position near village " +
                             $"{village.VillageId}; using center {village.Position}");
            return village.Position;
        }

        /// <summary>
        /// 비동기 NPC 엔티티 생성.
        /// await 도중 청크가 비활성화되거나 재활성화 레이스가 발생해도 일관성 유지:
        ///   1) 생성 전에 스폰 위치를 확정 (막힌 자리면 프리팹 인스턴스화 자체를 하지 않음)
        ///   2) IsSpawning 플래그로 중복 호출 차단 (이중 스폰 방지)
        ///   3) CreateNpc 완료 후 청크 상태 재확인 → 비활성이면 GameObject 즉시 폐기 (orphan 방지)
        /// </summary>
        private async UniTask SpawnNpc(int entityId, NpcSaveData saveData)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            if (saveData.IsActive || saveData.IsSpawning)
                return;
            saveData.IsSpawning = true;

            try
            {
                // 위치 확정은 엔티티 생성보다 먼저 한다. 생성 후 검증하면 실패할 때마다
                // 프리팹 인스턴스화와 장비 modifier 적용을 통째로 버리게 된다.
                // 생성 후 검증과 입력값은 동일하다 — TransformComponent는 EntityBase.SetupEntityId가
                // spawnPos3D로 만들고, moveRadius는 양쪽 다 NpcTable에서 나온다.
                if (TryResolveNpcSpawnPosition(entityId, saveData) == false)
                    return;

                Vector3 spawnPos3D = new Vector3(saveData.Position.x, saveData.Position.y, -0.05f);

                // 발급된 EntityId를 전달하여 동일한 ID로 엔티티 생성. saveData를 같이 넘겨 장비 modifier도 복원.
                var (createdId, entity) = await EntityFactory.CreateNpc(saveData.NpcTableId, spawnPos3D, _npcParent, entityId, saveData);

                if (createdId < 0 || entity == null)
                    return;

                // await 도중 청크가 비활성화되었으면 GameObject 폐기 (orphan 방지)
                Vector2Int chunkCoord = PositionToChunk(saveData.Position);
                if (AR.s.Map == null || AR.s.Map.IsChunkActive(chunkCoord) == false)
                {
                    AR.s.Entity.DestroyEntity(createdId, allowRecycle: false, keepRegistered: true);
                    if (entity != null)
                        Destroy(entity.gameObject);
                    return;
                }

                // NpcTag는 EntityFactory.CreateNpc가 AI 컴포넌트 부착 전에 추가함 (isNpc 판정용)
                EntityFactory.AddActivationComponent(createdId, _activationDistance, _deactivationDistance);

                PauseMaterializedActivity(createdId, saveData);

                // 마을 데이터 복원
                if (AR.s.Component.TryGetComponent<NpcVillageComponent>(createdId, out var village))
                {
                    village.VillageId = saveData.VillageId;
                    AR.s.Component.SetComponent(createdId, village);
                }

                saveData.EntityId = createdId;
                saveData.IsActive = true;
            }
            finally
            {
                saveData.IsSpawning = false;
            }
        }

        /// <summary>
        /// 엔티티 생성 전에 saveData.Position이 스폰 가능한 자리인지 확인하고, 막혀 있으면
        /// 활성 청크 안의 가장 가까운 빈 타일로 보정한다. 보정에 성공하면 saveData.Position과
        /// 청크 인덱스가 갱신된 상태로 true를 반환한다.
        ///
        /// false는 오류가 아니라 정상적인 재시도 경로다 — NPC 레코드는 그대로 유지되고,
        /// 장애물이 치워지거나 추상 시뮬레이션이 NPC를 옮기면 다음 청크 활성화에서 다시 시도된다.
        /// 생성 전에 판정하므로 실패 비용은 타일 검사뿐이다.
        /// </summary>
        private bool TryResolveNpcSpawnPosition(int entityId, NpcSaveData saveData)
        {
            Vector2 position = saveData.Position;
            float moveRadius = GetNpcMoveRadius(entityId, saveData);
            if (CollisionUtil.IsBlockedAt(position, moveRadius) == false)
                return true;

            if (CollisionUtil.TryFindNearestFree(position, moveRadius,
                    out Vector2 freePosition, requireActiveChunk: true) == false)
            {
                int tileX = Mathf.FloorToInt(position.x);
                int tileY = Mathf.FloorToInt(position.y);
                Debug.LogWarning($"[NpcManager] Entity {entityId} spawn deferred: blocked tile " +
                                 $"({tileX}, {tileY}) and no active free tile was found");
                return false;
            }

            SetNpcPosition(entityId, saveData, freePosition);
            Debug.LogWarning($"[NpcManager] Entity {entityId} spawn corrected: " +
                             $"{position} -> {freePosition}");
            return true;
        }

        /// <summary>
        /// NPC 상태를 저장하고 엔티티를 제거한다.
        /// NPC가 이동하여 청크가 바뀌었으면 _chunkNpcs 매핑을 갱신한다.
        /// </summary>
        private void SaveAndDeactivateNpc(int entityId, NpcSaveData saveData, Vector2Int originalChunk)
        {
            if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
            {
                saveData.Position = new Vector2(transform.Position.x, transform.Position.y);
            }

            if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
            {
                saveData.Condition = state.Condition;
            }

            // 마을 데이터 스냅샷
            if (AR.s.Component.TryGetComponent<NpcVillageComponent>(entityId, out var village))
            {
                saveData.VillageId = village.VillageId;
            }

            // 성향 스냅샷 (런타임 변동 대비)
            if (AR.s.Component.TryGetComponent<NpcStatComponent>(entityId, out var npcStat))
            {
                saveData.NpcStat = npcStat;
            }

            ResumeHuntPathFromCurrentPosition(saveData, AR.s.Time.CurrentGameTime);

            // 청크 매핑 갱신 (이동으로 청크가 바뀌었으면)
            Vector2Int currentChunk = PositionToChunk(saveData.Position);
            if (currentChunk != originalChunk)
            {
                RemoveNpcFromChunk(originalChunk, entityId);
                AddNpcToChunk(currentChunk, entityId);
            }

            if (AR.s.Message.TryGetEntity(entityId, out var entity))
            {
                // ID를 재활용하지 않음 (다음 스폰 시 동일 ID 사용)
                AR.s.Entity.DestroyEntity(entityId, allowRecycle: false, keepRegistered: true);
                Destroy(entity.gameObject);
            }

            saveData.IsActive = false;
        }

        private static void PauseMaterializedActivity(int entityId, NpcSaveData saveData)
        {
            NpcActivityState activity = saveData.Activity;
            if (activity.PartyId >= 0)
            {
                AR.s.Component.AddComponent(entityId, new NpcActivityPausedTag());
                AIStateHelper.StopMovement(entityId);
                return;
            }
            if (activity.Kind != NpcActivityKind.Hunt || activity.HasMovementPath == false)
                return;
            if (activity.PhaseEndGameTime <= AR.s.Time.CurrentGameTime)
                return;

            // Patrol이 새 스폰 직후 위치를 떠나지 않도록 AI/경로 이동을 중지한다.
            AR.s.Component.AddComponent(entityId, new NpcActivityPausedTag());
            AIStateHelper.StopMovement(entityId);
        }

        private static void ResumeHuntPathFromCurrentPosition(NpcSaveData saveData, float now)
        {
            NpcActivityState activity = saveData.Activity;
            if (activity.Kind != NpcActivityKind.Hunt || activity.HasMovementPath == false)
                return;
            if (activity.PhaseEndGameTime <= now)
                return;

            float oldDuration = activity.PhaseEndGameTime - activity.PhaseStartedGameTime;
            if (oldDuration <= 0f)
                return;

            float oldPathLength = Vector2.Distance(activity.OriginPosition, activity.TargetPosition) +
                Vector2.Distance(activity.TargetPosition, activity.ReturnPosition);
            if (oldPathLength <= 0.01f)
                return;

            float oldProgress = Mathf.Clamp01((now - activity.PhaseStartedGameTime) / oldDuration);
            float speed = oldPathLength / oldDuration;
            Vector2 currentPosition = saveData.Position;
            Vector2 target;
            Vector2 returnPosition;

            if (oldProgress < activity.OutboundFraction)
            {
                target = activity.TargetPosition;
                returnPosition = activity.ReturnPosition;
            }
            else
            {
                // 귀환 구간에 진입했다면 현재 위치에서 귀환점으로 바로 이동한다.
                target = activity.ReturnPosition;
                returnPosition = activity.ReturnPosition;
            }

            float outboundLength = Vector2.Distance(currentPosition, target);
            float returnLength = Vector2.Distance(target, returnPosition);
            float remainingLength = outboundLength + returnLength;
            if (remainingLength <= 0.01f)
            {
                activity.OriginPosition = currentPosition;
                activity.TargetPosition = currentPosition;
                activity.ReturnPosition = currentPosition;
                activity.OutboundFraction = 1f;
                activity.PhaseStartedGameTime = now;
                activity.PhaseEndGameTime = now;
                return;
            }

            activity.OriginPosition = currentPosition;
            activity.TargetPosition = target;
            activity.ReturnPosition = returnPosition;
            activity.OutboundFraction = outboundLength / remainingLength;
            activity.PhaseStartedGameTime = now;
            activity.PhaseEndGameTime = now + remainingLength / speed;
        }

        /// <summary>
        /// System_EntityDestroy에서 NpcTag 확인 후 호출.
        /// entityId가 곧 딕셔너리 키이므로 직접 조회한다.
        /// </summary>
        public void UnregisterNpcByEntityId(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            Vector2Int oldChunk = PositionToChunk(saveData.Position);

            if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
            {
                saveData.Condition = state.Condition;
            }

            // 던전 동행 NPC의 Transform은 던전 좌표다 — 월드 저장 위치·청크 인덱스를 덮지 않는다
            if (IsCompanionInDungeon(entityId) == false &&
                AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
            {
                saveData.Position = new Vector2(transform.Position.x, transform.Position.y);

                Vector2Int newChunk = PositionToChunk(saveData.Position);
                if (newChunk != oldChunk)
                {
                    RemoveNpcFromChunk(oldChunk, entityId);
                    AddNpcToChunk(newChunk, entityId);
                }
            }

            saveData.IsActive = false;

            // 물리 NPC 사망은 마을 Fate를 직접 바꾸지 않는다 — VILLAGE_NPC_BOUNDARY_DESIGN §8.
            // 마을 폐허는 "추상 인구 0 → Ruin" 규칙으로 재정의하되 구현은 보류. 모험가 사망은 경제와 무관.
        }

        /// <summary>
        /// 게임 세이브 시 현재 활성 NPC들의 상태를 저장한다.
        /// </summary>
        public void SaveAllActiveNpcs()
        {
            foreach (var kvp in _npcSaveDict)
            {
                NpcSaveData saveData = kvp.Value;
                if (saveData.IsActive == false)
                    continue;

                int entityId = kvp.Key;

                // 던전 동행 NPC는 입장 전 월드 위치를 유지한다. 던전 좌표가 저장되면 재접속 시 월드 청크 인덱스가 오염된다.
                if (IsCompanionInDungeon(entityId) == false &&
                    AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
                {
                    saveData.Position = new Vector2(transform.Position.x, transform.Position.y);
                }

                if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
                {
                    saveData.Condition = state.Condition;
                }
            }
        }

        public Dictionary<int, NpcSaveData> Save()
        {
            SaveAllActiveNpcs();
            return new Dictionary<int, NpcSaveData>(_npcSaveDict);
        }

        public List<NpcPartyState> SaveParties()
        {
            List<NpcPartyState> result = new List<NpcPartyState>(_simulationPartyIds.Count);
            for (int i = 0; i < _simulationPartyIds.Count; i++)
            {
                int partyId = _simulationPartyIds[i];
                if (_npcPartyDict.TryGetValue(partyId, out NpcPartyState? party))
                    result.Add(party);
            }
            return result;
        }

        public void Load(Dictionary<int, NpcSaveData> npcSaveDatas, int chunkSize)
        {
            _npcSaveDict.Clear();
            _chunkNpcs.Clear();
            _simulationNpcIds.Clear();
            _simulationCursor = 0;
            _simulationWorkCursor = 0;

            if (npcSaveDatas == null || npcSaveDatas.Count == 0)
                return;

            _chunkSize = chunkSize;

            foreach (var kvp in npcSaveDatas)
            {
                NpcSaveData saveData = kvp.Value;

                // 저장된 EntityId 그대로 재사용 — 세션 간 NPC 동일성 보장 (관계/호감도 인덱스가 ID 참조).
                // dict key가 정본(저장 시점의 EntityId), saveData.EntityId는 백업 (둘 다 동일하나 안전망).
                // 등록은 AR.PreReserveSavedEntityIds()가 Initialize 단계에서 일괄 처리 — 여기서 다시 등록하면 이중 등록 LogError.
                int entityId = kvp.Key > 0 ? kvp.Key
                              : (saveData.EntityId > 0 ? saveData.EntityId : AR.s.Entity.CreateEntity());

                saveData.IsActive = false;
                saveData.EntityId = entityId;

                // 장비/인벤토리 ItemData의 ItemTable 참조 복원 (PlayerData.LoadCompleted와 동일 패턴)
                saveData.OnLoadCompleted();

                // 이름 순환 상태에 기존 이름을 등록해 신규 스폰이 재사용하지 않도록 한다.
                // 구 세이브 호환 — Name 필드가 없던 시점의 데이터는 이름 풀에서 채워준다(등록은 PickName 내부에서).
                if (string.IsNullOrEmpty(saveData.Name))
                    saveData.Name = PickName();
                else
                    _usedNames.Add(saveData.Name);

                // 구 세이브 호환 — Description 필드가 없던 시점의 데이터는 직업 풀에서 채워준다
                if (string.IsNullOrEmpty(saveData.Description))
                    saveData.Description = PickDescription(GetPreferredJobType(saveData.NpcTableId));

                _npcSaveDict[entityId] = saveData;

                // 사망 기록은 전기/연대기 조회를 위해 저장 딕셔너리에만 보존한다.
                // 런타임 시뮬레이션/청크/마을 명단에 다시 넣으면 로드할 때마다 죽은 NPC가 후보군에 복귀한다.
                if (saveData.Condition == CharacterConditions.Dead ||
                    saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;

                AddNpcToSimulation(entityId);

                // 구 세이브의 Inn 방문자(월드 미출현 NPC)는 Status 필드 제거로 일반 NPC와 동일 취급.
                // VillageId=-1이라 마을 등록 없이 마지막 좌표에서 모험가로 복원된다 (§10 세이브 마이그레이션).
                Vector2Int chunkCoord = PositionToChunk(saveData.Position);
                AddNpcToChunk(chunkCoord, entityId);

                if (saveData.VillageId >= 0)
                    AR.s.Village.RegisterNpcToVillage(saveData.VillageId, entityId);
            }

            _isInitialLoaded = true;
            Debug.Log($"[NpcManager] Loaded {_npcSaveDict.Count} NPCs from save data");
        }

        public void LoadParties(List<NpcPartyState>? partyStates)
        {
            _npcPartyDict.Clear();
            _simulationPartyIds.Clear();
            _simulationWorkCursor = 0;
            _nextPartyId = 1;

            for (int i = 0; i < _simulationNpcIds.Count; i++)
            {
                int entityId = _simulationNpcIds[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                    continue;

                saveData.Activity.PartyId = -1;
            }

            if (partyStates == null)
                return;

            for (int i = 0; i < partyStates.Count; i++)
            {
                NpcPartyState party = partyStates[i];
                party.EnsureInitialized();
                if (party.PartyId <= 0 || _npcPartyDict.ContainsKey(party.PartyId))
                    continue;

                List<int> validMembers = new List<int>(party.MemberIds.Count);
                for (int memberIndex = 0; memberIndex < party.MemberIds.Count; memberIndex++)
                {
                    int memberId = party.MemberIds[memberIndex];
                    if (_npcSaveDict.TryGetValue(memberId, out NpcSaveData? member) == false)
                        continue;
                    if (member.Condition == CharacterConditions.Dead ||
                        member.Activity.Phase == NpcActivityPhase.Dead || validMembers.Contains(memberId))
                        continue;

                    validMembers.Add(memberId);
                }

                if (validMembers.Count < 2)
                    continue;

                validMembers.Sort();
                party.MemberIds = validMembers;
                if (validMembers.Contains(party.LeaderId) == false)
                    party.LeaderId = validMembers[0];

                party.Activity.PartyId = party.PartyId;
                _npcPartyDict.Add(party.PartyId, party);
                AddPartyToSimulation(party.PartyId);
                if (party.PartyId >= _nextPartyId)
                    _nextPartyId = party.PartyId + 1;

                for (int memberIndex = 0; memberIndex < validMembers.Count; memberIndex++)
                {
                    int memberId = validMembers[memberIndex];
                    NpcSaveData member = _npcSaveDict[memberId];
                    member.Activity.PartyId = party.PartyId;
                    member.Activity.Kind = NpcActivityKind.Hunt;
                    member.Activity.Phase = NpcActivityPhase.Venturing;
                }
            }
        }

        #region NPC 호감도 (양방향 대칭, sparse)

        /// <summary>
        /// 두 NPC 사이의 호감도 조회. 만난 적 없으면 0 반환.
        /// AI 행동 분기에서 매 프레임 호출 가능 — Dictionary lookup O(1).
        /// </summary>
        public int GetAffinity(int npcA, int npcB)
        {
            if (npcA == npcB)
                return 0;
            return _npcAffinity.TryGetValue(new NpcPairKey(npcA, npcB), out int value) ? value : 0;
        }

        /// <summary>
        /// 두 NPC가 만난 적 있는지 (호감도 엔트리가 등록되어 있는지) 확인.
        /// </summary>
        public bool HasMet(int npcA, int npcB)
        {
            if (npcA == npcB)
                return false;
            return _npcAffinity.ContainsKey(new NpcPairKey(npcA, npcB));
        }

        /// <summary>
        /// 호감도 값을 절대값으로 설정. 페어가 없으면 자동 등록.
        /// </summary>
        public void SetAffinity(int npcA, int npcB, int value)
        {
            if (npcA == npcB)
                return;
            _npcAffinity[new NpcPairKey(npcA, npcB)] = value;
        }

        /// <summary>
        /// 호감도 값을 delta만큼 가감. 페어가 없으면 0에서 시작하여 등록 — '만남'의 기본 의미.
        /// </summary>
        public void ModifyAffinity(int npcA, int npcB, int delta)
        {
            if (npcA == npcB || delta == 0)
            {
                if (npcA != npcB)
                {
                    NpcPairKey key0 = new NpcPairKey(npcA, npcB);
                    if (_npcAffinity.ContainsKey(key0) == false)
                        _npcAffinity[key0] = 0;
                }
                return;
            }

            NpcPairKey key = new NpcPairKey(npcA, npcB);
            _npcAffinity.TryGetValue(key, out int current);
            _npcAffinity[key] = Mathf.Clamp(current + delta, AFFINITY_MIN, AFFINITY_MAX);
        }

        /// <summary>
        /// 특정 NPC가 포함된 모든 호감도 엔트리 제거. NPC 영구 삭제 시 호출.
        /// 청크 비활성화 등 일시적 제거에서는 호출하지 말 것.
        /// </summary>
        public void RemoveAllAffinityInvolving(int entityId)
        {
            if (_npcAffinity.Count == 0)
                return;

            List<NpcPairKey> toRemove = new List<NpcPairKey>();
            foreach (var kvp in _npcAffinity)
            {
                if (kvp.Key.Lo == entityId || kvp.Key.Hi == entityId)
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                _npcAffinity.Remove(toRemove[i]);
        }

        /// <summary>
        /// 직렬화용 List 변환. DataManager.Save에서 호출.
        /// </summary>
        public List<NpcAffinityEntry> SaveAffinity()
        {
            List<NpcAffinityEntry> result = new List<NpcAffinityEntry>(_npcAffinity.Count);
            foreach (var kvp in _npcAffinity)
                result.Add(new NpcAffinityEntry(kvp.Key.Lo, kvp.Key.Hi, kvp.Value));
            return result;
        }

        /// <summary>
        /// 직렬화 List → Dictionary 복원. NpcManager.Initialize에서 호출.
        /// </summary>
        public void LoadAffinity(List<NpcAffinityEntry>? list)
        {
            _npcAffinity.Clear();
            if (list == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                NpcAffinityEntry entry = list[i];
                _npcAffinity[new NpcPairKey(entry.NpcA, entry.NpcB)] = entry.Affinity;
            }
        }

        #region Player-NPC Relationship

        /// <summary>
        /// 플레이어와 NPC 사이의 관계를 조회한다. 아직 상호작용하지 않은 NPC는 기본 관계를 반환한다.
        /// </summary>
        public PlayerRelation GetPlayerRelation(int npcId)
        {
            return _playerRelations.TryGetValue(npcId, out PlayerRelation relation)
                ? relation
                : PlayerRelation.Default;
        }

        /// <summary>
        /// 플레이어가 해당 NPC와 관계를 만든 적이 있는지 반환한다.
        /// </summary>
        public bool HasMetPlayer(int npcId)
        {
            return _playerRelations.ContainsKey(npcId);
        }

        /// <summary>
        /// 플레이어 관계를 절대값으로 설정한다. 모든 관계 변경은 이 메서드의 범위 보정을 거친다.
        /// </summary>
        public void SetPlayerRelation(int npcId, PlayerRelation relation)
        {
            if (npcId < 0)
                return;

            relation.Affinity = Mathf.Clamp(relation.Affinity, AFFINITY_MIN, AFFINITY_MAX);
            relation.TrustCap = Mathf.Clamp(relation.TrustCap, 0, 100);
            relation.Trust = Mathf.Clamp(relation.Trust, 0, relation.TrustCap);
            relation.Fear = Mathf.Clamp(relation.Fear, 0, 100);
            relation.Intimacy = Mathf.Clamp(relation.Intimacy, 0, 100);
            relation.TalkCount = Mathf.Max(0, relation.TalkCount);
            relation.LastTalkGameTime = Mathf.Max(-1f, relation.LastTalkGameTime);
            _playerRelations[npcId] = relation;
        }

        /// <summary>
        /// 플레이어 관계를 delta만큼 변경한다. Trust는 하락이 빠르고 회복은 절반 속도이며,
        /// 큰 하락은 회복 가능한 상한(TrustCap)도 낮춘다.
        /// </summary>
        public void ModifyPlayerRelation(int npcId, int dAff, int dTrust, int dFear, int dIntimacy)
        {
            if (npcId < 0)
                return;

            PlayerRelation relation = GetPlayerRelation(npcId);
            relation.Affinity = Mathf.Clamp(relation.Affinity + dAff, AFFINITY_MIN, AFFINITY_MAX);

            if (dTrust < 0)
            {
                relation.Trust = Mathf.Max(0, relation.Trust + dTrust);
                relation.TrustCap = Mathf.Max(relation.Trust, relation.TrustCap + dTrust / 2);
            }
            else if (dTrust > 0)
            {
                relation.Trust = Mathf.Min(relation.TrustCap, relation.Trust + dTrust / 2);
            }

            relation.Fear = Mathf.Clamp(relation.Fear + dFear, 0, 100);
            relation.Intimacy = Mathf.Clamp(relation.Intimacy + dIntimacy, 0, 100);
            SetPlayerRelation(npcId, relation);
        }

        /// <summary>
        /// 성립한 Talk의 만남 이력을 기록한다. 관계 수치와 함께 저장되어 첫 만남·재방문 대사에 사용된다.
        /// </summary>
        public void RecordPlayerTalk(int npcId, float gameTime)
        {
            if (npcId < 0)
                return;

            PlayerRelation relation = GetPlayerRelation(npcId);
            relation.TalkCount++;
            relation.LastTalkGameTime = Mathf.Max(0f, gameTime);
            SetPlayerRelation(npcId, relation);
        }

        /// <summary>
        /// 대화 테스트용으로 모든 플레이어↔NPC 관계의 만남 이력만 초기화한다.
        /// 호감·신뢰·두려움·친밀도는 유지한다.
        /// </summary>
        public int ResetPlayerTalkHistory()
        {
            List<int> npcIds = new List<int>(_playerRelations.Keys);
            int resetCount = 0;
            for (int i = 0; i < npcIds.Count; i++)
            {
                int npcId = npcIds[i];
                PlayerRelation relation = _playerRelations[npcId];
                if (relation.TalkCount == 0 && relation.LastTalkGameTime < 0f)
                    continue;

                relation.TalkCount = 0;
                relation.LastTalkGameTime = -1f;
                SetPlayerRelation(npcId, relation);
                resetCount++;
            }
            return resetCount;
        }

        /// <summary>
        /// 직렬화용 List 변환. DataManager.Save에서 호출한다.
        /// </summary>
        public List<PlayerNpcRelationEntry> SavePlayerRelations()
        {
            List<PlayerNpcRelationEntry> result = new List<PlayerNpcRelationEntry>(_playerRelations.Count);
            List<int> npcIds = new List<int>(_playerRelations.Keys);
            for (int i = 0; i < npcIds.Count; i++)
            {
                int npcId = npcIds[i];
                result.Add(new PlayerNpcRelationEntry(npcId, _playerRelations[npcId]));
            }
            return result;
        }

        /// <summary>
        /// 직렬화 List에서 플레이어 관계를 복원한다. 구 세이브의 null 목록은 빈 상태로 처리한다.
        /// </summary>
        public void LoadPlayerRelations(List<PlayerNpcRelationEntry>? list)
        {
            _playerRelations.Clear();
            if (list == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                PlayerNpcRelationEntry entry = list[i];
                SetPlayerRelation(entry.NpcId, entry.ToRelation());
            }
        }

        /// <summary>
        /// NPC 영구 삭제 시 플레이어 관계를 제거한다. 일시적 청크 비활성화에서는 호출하지 않는다.
        /// </summary>
        public void RemovePlayerRelation(int npcId)
        {
            _playerRelations.Remove(npcId);
        }

        #endregion

        #endregion

        #region NPC 관계 (유형 채널, 양방향 인덱스, sparse)

        /// <summary>
        /// 관계 추가. 같은 엔트리를 From/To 양쪽 인덱스에 등록(양방향 조회). 정본은 매니저+WorldData.
        /// 같은 (From,To,Type) 중복은 무시. self 관계(a==b)는 무시.
        /// </summary>
        public void AddRelation(int fromEntityId, int toEntityId, NpcRelationType type, int originEventId = -1)
        {
            if (fromEntityId == toEntityId)
                return;
            if (GetRelation(fromEntityId, toEntityId, type) != null)
                return;

            NpcRelationEntry entry = new NpcRelationEntry(fromEntityId, toEntityId, type, originEventId);
            AddToRelationIndex(fromEntityId, entry);
            AddToRelationIndex(toEntityId, entry);
        }

        /// <summary>
        /// 해당 NPC가 관여한 모든 관계 엔트리(From 또는 To). 없으면 빈 리스트.
        /// </summary>
        public IReadOnlyList<NpcRelationEntry> GetRelations(int npcId)
        {
            if (_npcRelations.TryGetValue(npcId, out var list))
                return list;
            return System.Array.Empty<NpcRelationEntry>();
        }

        /// <summary>
        /// 두 NPC 사이의 특정 유형 관계 1건 조회(방향 무관). 없으면 null.
        /// </summary>
        public NpcRelationEntry? GetRelation(int npcA, int npcB, NpcRelationType type)
        {
            if (npcA == npcB)
                return null;
            if (_npcRelations.TryGetValue(npcA, out var list) == false)
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                NpcRelationEntry e = list[i];
                if (e.Type != type)
                    continue;
                if ((e.FromEntityId == npcA && e.ToEntityId == npcB) ||
                    (e.FromEntityId == npcB && e.ToEntityId == npcA))
                    return e;
            }
            return null;
        }

        /// <summary>
        /// 특정 NPC가 관여한 모든 관계 엔트리 제거. NPC 영구 삭제 시 호출(R3에서 사망 경로 배선).
        /// 양방향 인덱스라 상대편 리스트에서도 제거한다.
        /// </summary>
        public void RemoveAllRelationsInvolving(int entityId)
        {
            if (_npcRelations.TryGetValue(entityId, out var list) == false)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                NpcRelationEntry e = list[i];
                int other = e.FromEntityId == entityId ? e.ToEntityId : e.FromEntityId;
                if (_npcRelations.TryGetValue(other, out var otherList))
                    otherList.Remove(e);
            }
            _npcRelations.Remove(entityId);
        }

        /// <summary>
        /// 직렬화용 List 변환. 양방향 인덱스라 From 키에서만 추출하여 중복 제거. DataManager.Save에서 호출.
        /// </summary>
        public List<NpcRelationEntry> SaveRelations()
        {
            List<NpcRelationEntry> result = new List<NpcRelationEntry>();
            foreach (var kvp in _npcRelations)
            {
                List<NpcRelationEntry> list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    // 각 엔트리는 From/To 두 인덱스에 들어있음 → From 키에서만 emit하여 1회만 저장
                    if (list[i].FromEntityId == kvp.Key)
                        result.Add(list[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 직렬화 List → 양방향 인덱스 복원. NpcManager.Initialize에서 호출.
        /// </summary>
        public void LoadRelations(List<NpcRelationEntry>? list)
        {
            _npcRelations.Clear();
            if (list == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                NpcRelationEntry entry = list[i];
                if (entry.FromEntityId == entry.ToEntityId)
                    continue;
                AddToRelationIndex(entry.FromEntityId, entry);
                AddToRelationIndex(entry.ToEntityId, entry);
            }
        }

        private void AddToRelationIndex(int key, NpcRelationEntry entry)
        {
            if (_npcRelations.TryGetValue(key, out var list) == false)
            {
                list = new List<NpcRelationEntry>();
                _npcRelations[key] = list;
            }
            list.Add(entry);
        }

        #endregion

        #region NPC 메타데이터 조회 / flavor

        // Inn 고용/방문자 시스템은 사용하지 않는다 — VILLAGE_NPC_BOUNDARY_DESIGN §5.
        // 여관 건물/서비스 근접은 유지되나 고용 경로는 없다.

        /// <summary>
        /// EntityId에 매핑된 NpcSaveData를 반환. 없으면 null.
        /// UI 등 외부에서 NPC 메타데이터를 조회할 때 사용.
        /// </summary>
        public NpcSaveData? GetSaveData(int entityId)
        {
            return _npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) ? saveData : null;
        }

        public bool TryCreateHuntParty(int leaderId, out NpcPartyState? party)
        {
            party = null;
            if (_npcSaveDict.TryGetValue(leaderId, out NpcSaveData? leader) == false ||
                IsEligibleHuntPartyMember(leader) == false)
                return false;

            VillageData? village = AR.s.Village.GetVillage(leader.VillageId);
            if (village == null)
                return false;

            const float recruitmentRangeSqr = 400f;
            List<int> candidates = new List<int>();
            List<int> villageNpcIds = village.NpcEntityIds;
            for (int i = 0; i < villageNpcIds.Count; i++)
            {
                int candidateId = villageNpcIds[i];
                if (candidateId == leaderId || _npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false)
                    continue;
                if (candidate.VillageId != leader.VillageId || IsEligibleHuntPartyMember(candidate) == false)
                    continue;

                Vector2 offset = candidate.Position - leader.Position;
                if (offset.sqrMagnitude > recruitmentRangeSqr || IsHostilePartyRelation(leaderId, candidateId))
                    continue;

                candidates.Add(candidateId);
            }

            candidates.Sort((leftId, rightId) =>
            {
                int leftScore = GetPartyRecruitmentScore(leaderId, leftId);
                int rightScore = GetPartyRecruitmentScore(leaderId, rightId);
                if (leftScore != rightScore)
                    return rightScore.CompareTo(leftScore);
                return leftId.CompareTo(rightId);
            });

            if (candidates.Count == 0)
                return false;

            int partyId = _nextPartyId++;
            NpcPartyState created = new NpcPartyState
            {
                PartyId = partyId,
                LeaderId = leaderId,
            };
            created.MemberIds.Add(leaderId);
            for (int i = 0; i < candidates.Count && created.MemberIds.Count < 4; i++)
                created.MemberIds.Add(candidates[i]);
            created.MemberIds.Sort();
            created.Activity.PartyId = partyId;

            _npcPartyDict.Add(partyId, created);
            AddPartyToSimulation(partyId);
            for (int i = 0; i < created.MemberIds.Count; i++)
            {
                int memberId = created.MemberIds[i];
                NpcSaveData member = _npcSaveDict[memberId];
                member.Activity.PartyId = partyId;
                member.Activity.Kind = NpcActivityKind.Hunt;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = "hunt";
                member.Activity.HasMovementPath = false;
            }

            party = created;
            return true;
        }

        public void DissolveParty(int partyId)
        {
            if (_npcPartyDict.TryGetValue(partyId, out NpcPartyState? party) == false)
                return;

            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                if (_npcSaveDict.TryGetValue(memberId, out NpcSaveData? member) &&
                    member.Activity.PartyId == partyId)
                    member.Activity.PartyId = -1;
            }

            _npcPartyDict.Remove(partyId);
            RemovePartyFromSimulation(partyId);
        }

        public bool TryGetNextSimulationWork(out int entityId, out NpcSaveData? saveData,
            out NpcPartyState? party)
        {
            entityId = -1;
            saveData = null;
            party = null;

            int partyCount = _simulationPartyIds.Count;
            int totalCount = partyCount + _simulationNpcIds.Count;
            for (int i = 0; i < totalCount; i++)
            {
                if (_simulationWorkCursor >= totalCount)
                    _simulationWorkCursor = 0;

                int workIndex = _simulationWorkCursor;
                _simulationWorkCursor++;
                if (workIndex < partyCount)
                {
                    int partyId = _simulationPartyIds[workIndex];
                    if (_npcPartyDict.TryGetValue(partyId, out NpcPartyState? candidateParty))
                    {
                        party = candidateParty;
                        return true;
                    }
                    continue;
                }

                int npcIndex = workIndex - partyCount;
                int candidateId = _simulationNpcIds[npcIndex];
                if (_npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false ||
                    candidate.Condition == CharacterConditions.Dead ||
                    candidate.Activity.Phase == NpcActivityPhase.Dead || candidate.Activity.PartyId >= 0)
                    continue;

                entityId = candidateId;
                saveData = candidate;
                return true;
            }

            return false;
        }

        public void PauseMaterializedNpcPartyActivity(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false ||
                saveData.IsActive == false)
                return;

            AR.s.Component.AddComponent(entityId, new NpcActivityPausedTag());
            AIStateHelper.StopMovement(entityId);
        }

        /// <summary>
        /// 시간분할 NPC 시뮬레이션용 다음 NPC를 결정론적 순서로 반환한다.
        /// 활성 NPC도 같은 활동 시계로 처리하되, 위치 표현만 ECS 조우 상태에 맡긴다.
        /// </summary>
        public bool TryGetNextSimulationNpc(out int entityId, out NpcSaveData? saveData)
        {
            entityId = -1;
            saveData = null;

            int count = _simulationNpcIds.Count;
            for (int i = 0; i < count; i++)
            {
                // 커서가 끝에 도달하면 처음으로 돌아가 모든 NPC를 공정하게 순회한다.
                if (_simulationCursor >= _simulationNpcIds.Count)
                    _simulationCursor = 0;

                int candidateId = _simulationNpcIds[_simulationCursor];
                _simulationCursor++;

                if (_npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false)
                    continue;
                if (candidate.Condition == CharacterConditions.Dead ||
                    candidate.Activity.Phase == NpcActivityPhase.Dead)
                    continue;

                entityId = candidateId;
                saveData = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 활성 청크 NPC가 Hunt를 시작하면 그 자리에서 AI를 정지시켜 조우 상태로 만든다.
        /// 오프스크린 NPC는 다음 승격 시 SpawnNpc에서 같은 처리를 받는다.
        /// </summary>
        public void PauseMaterializedNpcActivity(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.IsActive == false)
                return;

            PauseMaterializedActivity(entityId, saveData);
        }

        /// <summary>활동 결과가 확정된 활성 NPC의 조우 대기 태그를 제거해 기본 AI를 재개한다.</summary>
        public void ResumeMaterializedNpcActivity(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.IsActive == false)
                return;

            AR.s.Component.RemoveComponent<NpcActivityPausedTag>(entityId);
        }

        /// <summary>
        /// 오프스크린 활동의 analytic 좌표를 저장하고 청크 인덱스를 함께 갱신한다.
        /// 다음 청크 활성화는 이 인덱스로 NPC를 현재 위치에 승격한다.
        /// </summary>
        public void UpdateOffscreenNpcPosition(int entityId, Vector2 position)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.IsActive)
                return;

            Vector2Int oldChunk = PositionToChunk(saveData.Position);
            saveData.Position = position;
            Vector2Int newChunk = PositionToChunk(position);
            if (oldChunk == newChunk)
                return;

            RemoveNpcFromChunk(oldChunk, entityId);
            AddNpcToChunk(newChunk, entityId);
        }

        /// <summary>
        /// ECS 엔티티가 없는 NPC의 영구 사망을 정리한다.
        /// NPC 저장 데이터는 전기/연대기 회고를 위해 보존하되, 활동 순회와 관계 채널에서는 제거한다.
        /// </summary>
        public void MarkNpcDead(int entityId, float when)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Dead)
                return;

            saveData.Condition = CharacterConditions.Dead;
            saveData.Activity.Phase = NpcActivityPhase.Dead;
            saveData.Activity.Kind = NpcActivityKind.None;
            saveData.Activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = when;

            int settlementId = AR.s.Village.GetSettlementId(saveData.VillageId);
            int eventId = settlementId >= 0
                ? AR.s.History.AppendLiveEvent(ChronicleEventType.Fell, settlementId)
                : -1;

            RemoveNpcFromSimulation(entityId);
            RemoveAllAffinityInvolving(entityId);
            RemovePlayerRelation(entityId);
            RecordDeceasedRelationMemories(entityId, ResolveNpcDisplayName(saveData), eventId, when);
            RemoveAllRelationsInvolving(entityId);
            AR.s.Village.UnregisterNpcFromVillage(saveData.VillageId, entityId);

            AddDeed(entityId, NpcDeedKind.Fell, eventId, NpcKnowledgeLevel.Did, when, entityId);

            if (saveData.IsActive)
            {
                if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var state))
                {
                    state.Condition = CharacterConditions.Dead;
                    AR.s.Component.SetComponent(entityId, state);
                }

                // LateUpdate의 System_EntityDestroy가 UnregisterNpcByEntityId와 GameObject 파괴를 수행한다.
                AR.s.Component.AddComponent(entityId, new DestroyTag());
            }
        }

        private void RecordDeceasedRelationMemories(int deceasedEntityId, string deceasedName,
            int fellEventId, float deathGameTime)
        {
            if (_npcRelations.TryGetValue(deceasedEntityId, out List<NpcRelationEntry>? relations) == false)
                return;

            for (int i = 0; i < relations.Count; i++)
            {
                NpcRelationEntry relation = relations[i];
                int survivorEntityId = relation.FromEntityId == deceasedEntityId
                    ? relation.ToEntityId
                    : relation.FromEntityId;
                if (_npcSaveDict.TryGetValue(survivorEntityId, out NpcSaveData? survivor) == false)
                    continue;
                if (survivor.Condition == CharacterConditions.Dead ||
                    survivor.Activity.Phase == NpcActivityPhase.Dead)
                    continue;

                bool wasFromEntity = relation.FromEntityId == survivorEntityId;
                survivor.DeceasedRelations.Add(new DeceasedRelationMemory(deceasedEntityId, deceasedName,
                    relation.Type, wasFromEntity, fellEventId, deathGameTime));
                _narrative.InvalidateDialogues(survivorEntityId);   // 상실 발생 → 생존자 대화 캐시 폐기 (LostCompanion 반영)
            }
        }

        private static string ResolveNpcDisplayName(NpcSaveData saveData)
        {
            // 인스턴스 고유 이름 우선. 구 세이브/미시드 방어로 테이블 이름 → #EntityId 폴백.
            if (string.IsNullOrEmpty(saveData.Name) == false)
                return saveData.Name;
            return AR.s.Data.GetNpc(saveData.NpcTableId)?.Name ?? $"NPC #{saveData.EntityId}";
        }

        /// <summary>
        /// NPC 주요 사건 이력에 항목을 추가한다. 이력은 NPC별로 영속하며 삭제하지 않는다.
        /// </summary>
        public void AddDeed(int entityId, NpcDeedKind kind, int refEventId,
            NpcKnowledgeLevel howKnown, float when, int participantId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(kind, refEventId, howKnown, when);
            AddDeedParticipant(entry, participantId);
            saveData.Deeds.Add(entry);
            _narrative.InvalidateDialogues(entityId);   // 새 사건 → 낡은 대화 캐시 폐기 (NARRATIVE_DIALOGUE_VARIETY §5 P1-4)
        }

        public void AddPartyAffinityDeed(int entityId, int otherEntityId, int affinityDelta, float when)
        {
            if (affinityDelta == 0 || _npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(NpcDeedKind.PartyAffinityChanged, -1,
                NpcKnowledgeLevel.Did, when)
            {
                AffinityDelta = affinityDelta,
            };
            AddDeedParticipant(entry, entityId);
            AddDeedParticipant(entry, otherEntityId);
            saveData.Deeds.Add(entry);
            _narrative.InvalidateDialogues(entityId);
        }

        public void AddWoundedDeed(int entityId, float when, float recoveryEndGameTime)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(NpcDeedKind.Wounded, -1,
                NpcKnowledgeLevel.Did, when)
            {
                RecoveryEndGameTime = recoveryEndGameTime,
            };
            AddDeedParticipant(entry, entityId);
            saveData.Deeds.Add(entry);
            _narrative.InvalidateDialogues(entityId);
        }

        public void AddNotableItemDeed(int entityId, int itemId, string itemDisplayName, float when)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(NpcDeedKind.NotableItemAcquired, -1,
                NpcKnowledgeLevel.Did, when)
            {
                ItemId = itemId,
                ItemDisplayName = itemDisplayName ?? string.Empty,
            };
            AddDeedParticipant(entry, entityId);
            saveData.Deeds.Add(entry);
            _narrative.InvalidateDialogues(entityId);
        }

        private void AddDeedParticipant(NpcDeedEntry entry, int participantId)
        {
            if (participantId < 0)
                return;

            entry.Participants.Add(participantId);
            entry.ParticipantSnapshots.Add(new NpcDeedParticipant(participantId,
                ResolveNpcDisplayName(participantId)));
        }

        private string ResolveNpcDisplayName(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData))
                return ResolveNpcDisplayName(saveData);
            return $"NPC #{entityId}";
        }

        /// <summary>
        /// NPC의 표시 이름. 인스턴스 고유 이름 → 테이블 이름 → "NPC #id" 폴백.
        /// 이름 표기 정책 단일 진입점 — UI/피드/시뮬 모두 이 경로로 통일한다.
        /// </summary>
        public string GetDisplayName(int entityId) => ResolveNpcDisplayName(entityId);

        private GlobalEnum.JobType GetPreferredJobType(int npcTableId)
        {
            Tables.NpcTable? table = AR.s.Data.GetNpc(npcTableId);
            return table != null ? table.JobType : GlobalEnum.JobType.None;
        }

        // ========== 이름 풀 + 배정(셔플백 순환) ==========
        // NpcSaveData.Name 생성용. 같은 NpcTableId(공유 테이블 이름)라도 인스턴스마다 다른 이름 → "누가 누군지" 식별.
        // 서양 판타지 + 순우리말 혼합. 종족/성별 분기는 v1 미적용(인스턴스에 데이터 없음).
        // 배정 방식: 풀을 셔플해 순차 소비 → 한 바퀴(=풀 크기) 안에서는 중복 0.
        //   풀이 300보다 크므로 300명 규모 월드도 첫 바퀴에서 동명이인 없음.
        //   셔플 순서·커서·사용집합은 매니저 인스턴스가 소유(Reset()에서 정리). static 금지(CLAUDE.md Logic Placement).
        // 새 이름 추가는 이 배열 한 곳만 수정.
        private static readonly string[] _namePool =
        {
            // ── 서양 판타지 (남성 계열) ──
            "아서", "롤랜드", "가레스", "트리스탄", "세드릭", "에드먼드", "고드릭", "알드릭", "레온하르트", "카시우스",
            "마르쿠스", "루시안", "발레리안", "아드리안", "도미닉", "세바스찬", "율리안", "라파엘", "가브리엘", "데미안",
            "다리우스", "케일럼", "가웨인", "코르빈", "로렌", "에이든", "던컨", "말콤", "이완", "핀레이",
            "알리스터", "브로디", "캘런", "라클런", "브레넌", "데클란", "로넌", "킬리언", "니얼", "오웬",
            "로건", "데릭", "콜린", "그렉", "알렌", "로한", "테론", "라이너스", "발렌", "드레이크",
            "가론", "엘드린", "세라스", "라그나", "비요른", "스벤", "군나르", "하콘", "에릭", "올라프",
            "아이나르", "레이프", "토르발드", "발더", "군터", "지크프리트", "볼프강", "하인리히", "프리드리히", "콘라트",
            "오토", "루트비히", "빌헬름", "알브레히트", "디트리히", "라이문트", "베른하르트", "게르하르트", "라인홀트", "롤프",
            "브란", "아론델", "엘론", "카일", "레온", "아모스", "사일러스", "이지키엘", "조슈아", "일라이",
            "바나비", "토비아스", "재스퍼", "오스카", "펠릭스", "에밀", "클라우스", "게오르크", "하랄트", "카스파",
            // ── 서양 판타지 (여성 계열) ──
            "엘라라", "세라핀", "이졸데", "로웬", "리아나", "아리아", "셀레네", "미리엘", "이오나", "브리엔",
            "카산드라", "로잘린", "이블린", "세라피나", "릴리안", "비비안", "아드리아나", "이자벨", "에일린", "모르가나",
            "프레야", "시그리드", "아스트리드", "잉그리드", "헬가", "군힐드", "브룬힐드", "다그니", "소냐", "에이라",
            "리브", "사가", "이둔", "난나", "시프", "게르드", "엘리자", "오필리아", "코델리아", "로자먼드",
            "위노나", "에스텔", "로렐라이", "셀레스틴", "페넬로페", "아나스타샤", "예카테리나", "로자", "마르그리트", "아델하이트",
            "힐데가르트", "게르트루트", "크림힐트", "이졸린", "로스비타", "발푸르가", "쿠니군데", "마틸데", "아그네스", "엘프리데",
            // ── 서양 판타지 (중성/기타) ──
            "로빈", "모건", "렌", "카이", "로안", "에덴", "노바", "아리스", "세이지", "로엔",
            "아셀", "카렌", "에런", "셰인", "웬디", "로라", "클레어", "헬렌", "마야", "노라",
            // ── 순우리말 ──
            "가온", "노을", "다온", "라온", "마루", "바다", "새론", "아라", "여울", "이든",
            "하람", "해울", "가람", "누리", "다솜", "미르", "보라", "서리", "아름", "예송",
            "이레", "하늘", "한별", "가율", "단하", "무결", "벼리", "새벽", "아린", "여름",
            "하진", "해온", "가빈", "도담", "미강", "봄내", "슬아", "우담", "예린", "하율",
            "한솔", "늘봄", "다래", "미리내", "샘이", "여리", "나래", "다올", "별하", "초롱",
            "한울", "보름", "소담", "은하", "파랑", "나린", "미쁨", "가랑", "도래", "마중",
            "새하", "아토", "하마루", "나비", "다봄", "소리", "여든", "하랑", "해달", "너울",
            "다흰", "새암", "가온비", "노아", "라희", "마음", "바오", "산들", "아름드리", "온새미",
            "자미", "초록", "하람비", "가을", "겨울", "이슬", "달래", "슬기", "예솔", "하봄",
            "나울", "다솔", "미소", "바름", "새미", "하은", "해나", "나봄", "별찌", "다붓",
            // ── 서양 판타지 (라틴/게르만 확장 — 남성) ──
            "아길", "브라이스", "코맥", "데스몬드", "로드릭", "오스릭", "레지날드", "험프리", "발타자르", "카를로스",
            "엔리케", "로드리고", "알바로", "디에고", "마테오", "세자르", "이그나시오", "로렌초", "마르코", "지오반니",
            "안토니오", "프란체스코", "살바토레", "비토리오", "에밀리오", "파올로", "레오나르도", "라파엘로", "도나토", "마시모",
            // ── 서양 판타지 (라틴 확장 — 여성) ──
            "비앙카", "지네브라", "루크레치아", "프란체스카", "이사벨라", "카테리나", "발렌티나", "로사리아", "안젤리카", "에밀리아",
            "카밀라", "리비아", "옥타비아", "클라우디아", "플라비아", "실비아", "코르넬리아", "아우렐리아", "발레리아", "레티치아",
            // ── 서양 판타지 (슬라브/프랑스 확장) ──
            "미하일", "드미트리", "이반", "니콜라이", "알렉세이", "보리스", "유리", "로만", "안톤", "파벨",
            "나탈리아", "옥사나", "카티야", "이리나", "라리사", "갈리나", "베로니카", "류드밀라", "조야", "타티아나",
            "앙리", "필리프", "자크", "티에리", "마티외", "뤼시앵", "가스파르", "레미", "티보", "아드리앵",
            "아멜리", "콜레트", "쥘리에트", "마르고", "델핀", "실비", "오데트", "이본", "잔", "뤼시",
        };

        // 이름 배정 상태 — 매니저 인스턴스 소유(Reset()에서 정리). 셔플백 순환으로 중복 최소화.
        private readonly List<int> _nameBag = new();          // _namePool 인덱스의 셔플된 소비 순서
        private int _nameBagCursor;                            // _nameBag 소비 위치
        private readonly HashSet<string> _usedNames = new();   // 현재 사용 중 이름(로드 시 기존 NPC로 시드)

        /// <summary>
        /// 셔플백에서 아직 안 쓰인 이름을 배정한다. 신규 NPC 생성 시 1회 호출하여 NpcSaveData.Name에 박는다.
        /// 풀 소진 시(사용 중 NPC 수 &gt; 풀 크기)에만 순환 순서대로 중복을 허용한다.
        /// </summary>
        private string PickName()
        {
            if (_namePool.Length == 0)
                return string.Empty;

            // 풀 한 바퀴를 훑으며 아직 안 쓰인 이름을 우선 배정 (HashSet.Add == false → 이미 사용 중)
            for (int tries = 0; tries < _namePool.Length; tries++)
            {
                if (_nameBagCursor >= _nameBag.Count)
                    RebuildNameBag();

                string candidate = _namePool[_nameBag[_nameBagCursor]];
                _nameBagCursor++;
                if (_usedNames.Add(candidate))
                    return candidate;
            }

            // 풀 소진 — 유일 이름이 없으니 순환 순서대로 중복 허용
            if (_nameBagCursor >= _nameBag.Count)
                RebuildNameBag();
            return _namePool[_nameBag[_nameBagCursor++]];
        }

        /// <summary>_namePool 인덱스를 Fisher-Yates로 셔플해 소비 순서를 다시 만든다.</summary>
        private void RebuildNameBag()
        {
            _nameBag.Clear();
            for (int i = 0; i < _namePool.Length; i++)
                _nameBag.Add(i);
            for (int i = _nameBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = _nameBag[i];
                _nameBag[i] = _nameBag[j];
                _nameBag[j] = tmp;
            }
            _nameBagCursor = 0;
        }

        // ========== 직업별 flavor 풀 ==========
        // NpcSaveData.Description 생성용. 같은 직업이라도 인스턴스마다 다른 소개 → 마을 분위기 다양성.
        // 새 직업/문구 추가는 이 사전 한 곳만 수정.
        private static readonly Dictionary<GlobalEnum.JobType, string[]> _descriptionPool = new()
        {
            [GlobalEnum.JobType.None] = new[]
            {
                "이곳에서 새 출발을 하고 싶습니다.",
                "조용히 지낼 곳이 필요합니다.",
                "잠시 쉴 자리만 있으면 됩니다.",
            },
            [GlobalEnum.JobType.Farmer] = new[]
            {
                "고향에선 보리농사를 지었습니다.",
                "땅을 일구는 일이라면 자신 있어요.",
                "올해 작황이 안 좋아 떠나왔습니다.",
            },
            [GlobalEnum.JobType.Hunter] = new[]
            {
                "활과 덫 하나는 자신 있습니다.",
                "산짐승 흔적은 누구보다 잘 읽습니다.",
                "고기 손질도 깔끔하게 합니다.",
            },
            [GlobalEnum.JobType.Merchant] = new[]
            {
                "물건 보는 눈은 자신 있어요.",
                "장사로 잔뼈가 굵었습니다.",
                "셈은 누구보다 빠릅니다.",
            },
            [GlobalEnum.JobType.Blacksmith] = new[]
            {
                "쇠 다루는 일이라면 맡겨주세요.",
                "망치질로 단련된 팔뚝입니다.",
                "스승 밑에서 십 년을 배웠습니다.",
            },
            [GlobalEnum.JobType.Woodcutter] = new[]
            {
                "도끼질이라면 누구한테도 안 집니다.",
                "어떤 나무든 한나절이면 베어냅니다.",
                "목재 보는 눈도 좋습니다.",
            },
            [GlobalEnum.JobType.Miner] = new[]
            {
                "산속 광맥은 제 손바닥 안에 있습니다.",
                "곡괭이 하나로 살아왔습니다.",
                "광석 종류는 한눈에 알아봅니다.",
            },
            [GlobalEnum.JobType.Builder] = new[]
            {
                "벽 한 줄이라도 더 잘 쌓겠습니다.",
                "집 짓는 일이라면 자신 있습니다.",
                "재료만 주시면 뭐든 만들어 드립니다.",
            },
            [GlobalEnum.JobType.Guard] = new[]
            {
                "마을은 제가 지킵니다.",
                "검술이라면 부족하지 않습니다.",
                "야간 경비는 제 전문입니다.",
            },
            [GlobalEnum.JobType.Scholar] = new[]
            {
                "조용히 책 읽을 자리만 있으면 됩니다.",
                "고서를 정리하는 일이라면 맡겨주세요.",
                "약초학에도 조예가 있습니다.",
            },
            [GlobalEnum.JobType.Chief] = new[]
            {
                "사람을 모으는 일이 적성에 맞습니다.",
                "마을을 이끌어 본 경험이 있습니다.",
            },
            [GlobalEnum.JobType.Gatherer] = new[]
            {
                "닥치는 대로 일할 수 있습니다.",
                "허드렛일도 마다하지 않습니다.",
                "튼튼한 손과 등이 자랑입니다.",
            },
        };

        /// <summary>
        /// 직업 기반 소개 문구를 풀에서 랜덤 선택. NPC 생성 시 1회 호출하여 NpcSaveData.Description에 박는다.
        /// </summary>
        private static string PickDescription(GlobalEnum.JobType jobType)
        {
            if (_descriptionPool.TryGetValue(jobType, out string[]? pool) == false || pool.Length == 0)
                pool = _descriptionPool[GlobalEnum.JobType.None];
            return pool[Random.Range(0, pool.Length)];
        }

        #endregion

        #region 유틸리티

        private float GetNpcMoveRadius(int entityId, NpcSaveData saveData)
        {
            if (AR.s.Component.TryGetComponent<ColliderComponent>(entityId,
                    out var collider) == true && collider.MoveRadius > 0f)
                return collider.MoveRadius;

            NpcTable? table = AR.s.Data.GetNpc(saveData.NpcTableId);
            return table != null && table.MoveRadius > 0f
                ? table.MoveRadius
                : DEFAULT_NPC_MOVE_RADIUS;
        }

        private void SetNpcPosition(int entityId, NpcSaveData saveData, Vector2 position)
        {
            Vector2Int oldChunk = PositionToChunk(saveData.Position);
            saveData.Position = position;

            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
            {
                transform.Position = position;
                cm.SetComponent(entityId, transform);
            }

            if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == true)
            {
                velocity.Direction = Vector2.zero;
                cm.SetComponent(entityId, velocity);
            }

            if (cm.TryGetComponent<PathfindingComponent>(entityId, out var pathfinding) == true &&
                pathfinding.Status != PathfindingStatus.None)
            {
                pathfinding.Status = PathfindingStatus.Computing;
                pathfinding.CurrentWaypointIndex = 0;
                pathfinding.LastProgressDistSqr = float.MaxValue;
                pathfinding.LastProgressTime = Time.time;
                cm.SetComponent(entityId, pathfinding);
            }

            Vector2Int newChunk = PositionToChunk(position);
            if (oldChunk == newChunk)
                return;

            RemoveNpcFromChunk(oldChunk, entityId);
            AddNpcToChunk(newChunk, entityId);
        }

        private Vector2Int PositionToChunk(Vector2 position)
        {
            int chunkX = Mathf.FloorToInt(position.x / _chunkSize);
            int chunkY = Mathf.FloorToInt(position.y / _chunkSize);
            return new Vector2Int(chunkX, chunkY);
        }

        private void AddNpcToChunk(Vector2Int chunkCoord, int entityId)
        {
            if (_chunkNpcs.TryGetValue(chunkCoord, out List<int>? list) == false)
            {
                list = new List<int>();
                _chunkNpcs[chunkCoord] = list;
            }
            list.Add(entityId);
        }

        private void AddNpcToSimulation(int entityId)
        {
            // 엔티티 ID 오름차순을 유지해 생성/로드 Dictionary의 열거 순서와 무관한 순회를 보장한다.
            int index = _simulationNpcIds.BinarySearch(entityId);
            if (index >= 0)
                return;

            int insertIndex = ~index;
            _simulationNpcIds.Insert(insertIndex, entityId);
            // 현재 커서 앞에 삽입된 항목 때문에 다음 대상이 밀리지 않도록 보정한다.
            if (insertIndex <= _simulationCursor)
                _simulationCursor++;
            if (_simulationCursor >= _simulationNpcIds.Count)
                _simulationCursor = 0;
        }

        private void RemoveNpcFromSimulation(int entityId)
        {
            int index = _simulationNpcIds.BinarySearch(entityId);
            if (index < 0)
                return;

            _simulationNpcIds.RemoveAt(index);
            if (index < _simulationCursor)
                _simulationCursor--;
            if (_simulationCursor < 0 || _simulationCursor >= _simulationNpcIds.Count)
                _simulationCursor = 0;
        }

        private static bool IsEligibleHuntPartyMember(NpcSaveData saveData)
        {
            // 동행 예약 NPC는 다른 NPC의 사냥 파티에 끌려가지 않는다 (NPC_COMBAT_DESIGN §4.5)
            // 플레이어와 결투 중인 NPC도 파티에 뽑히지 않는다 (§9, B4)
            return saveData.Condition != CharacterConditions.Dead &&
                   saveData.Activity.Phase == NpcActivityPhase.Idle &&
                   saveData.Activity.Kind == NpcActivityKind.None &&
                   saveData.Activity.PartyId < 0 &&
                   AR.s.Data.NpcCompanion.ReservedNpcId != saveData.EntityId &&
                   AR.s.Component.HasComponent<DuelComponent>(saveData.EntityId) == false;
        }

        private bool IsHostilePartyRelation(int leaderId, int candidateId)
        {
            return GetRelation(leaderId, candidateId, NpcRelationType.Rival) != null ||
                   GetRelation(leaderId, candidateId, NpcRelationType.GrudgeAgainst) != null;
        }

        private int GetPartyRecruitmentScore(int leaderId, int candidateId)
        {
            int score = GetAffinity(leaderId, candidateId);
            if (GetRelation(leaderId, candidateId, NpcRelationType.Spouse) != null)
                score += 160;
            else if (GetRelation(leaderId, candidateId, NpcRelationType.Sibling) != null)
                score += 120;
            else if (GetRelation(leaderId, candidateId, NpcRelationType.Friend) != null)
                score += 80;

            if (_npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate))
                score += candidate.NpcStat.Courage / 10 + candidate.SkillLevel * 2;
            return score;
        }

        private void AddPartyToSimulation(int partyId)
        {
            int index = _simulationPartyIds.BinarySearch(partyId);
            if (index >= 0)
                return;

            _simulationPartyIds.Insert(~index, partyId);
            if (_simulationWorkCursor > 0)
                _simulationWorkCursor = 0;
        }

        private void RemovePartyFromSimulation(int partyId)
        {
            int index = _simulationPartyIds.BinarySearch(partyId);
            if (index < 0)
                return;

            _simulationPartyIds.RemoveAt(index);
            _simulationWorkCursor = 0;
        }

        private void RemoveNpcFromChunk(Vector2Int chunkCoord, int entityId)
        {
            if (_chunkNpcs.TryGetValue(chunkCoord, out List<int>? list))
            {
                list.Remove(entityId);
            }
        }

        #endregion
    }
}
