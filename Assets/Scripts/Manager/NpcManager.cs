#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Npc.Narrative;
using ARPG.Scene;
using ARPG.Tables;
using ARPG.Village;

namespace ARPG.Npc
{
    public class NpcManager : MonoBehaviour
    {
        private Dictionary<int, NpcSaveData> _npcSaveDict = new();
        private Dictionary<Vector2Int, List<int>> _chunkNpcs = new();
        // NPC 간 호감도 (양방향 대칭, sparse — 만난 적 있는 페어만 등록)
        private Dictionary<NpcPairKey, int> _npcAffinity = new();
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
            LoadRelations(AR.s.Data.NpcRelationList);
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
            _npcAffinity.Clear();
            _npcRelations.Clear();
            _narrative.Reset();
            _isInitialLoaded = false;
        }

        public void SetNpcRoot(Transform inNpcRoot)
        {
            _npcParent = inNpcRoot;
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
                    Description = PickDescription(GetPreferredJobType(obj.ObjectId)),
                };

                SeedNpcEquipment(saveData);

                if (villageId >= 0)
                {
                    saveData.VillageId = villageId;
                    AR.s.Village.RegisterNpcToVillage(villageId, entityId);
                }

                _npcSaveDict[entityId] = saveData;

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
                Description = PickDescription(GetPreferredJobType(npcTableId)),
            };

            SeedNpcEquipment(saveData);

            _npcSaveDict[entityId] = saveData;

            Vector2Int chunkCoord = PositionToChunk(position);
            AddNpcToChunk(chunkCoord, entityId);

            if (villageId >= 0)
                AR.s.Village.RegisterNpcToVillage(villageId, entityId);

            if (AR.s.Map.IsChunkActive(chunkCoord))
                SpawnNpc(entityId, saveData).Forget();

            return entityId;
        }

        /// <summary>
        /// 마을 초기 1회 모험가 코호트 스폰 (NPC_SIMPLIFICATION_PLAN D5).
        /// 스폰된 물리 NPC는 마을 경제와 분리된 모험가 — 재스폰/전멸 복구 없음 (초기 코호트로 재미 검증 먼저).
        /// 맵파일에 NPC가 배치된 마을(NpcEntityIds already filled)은 기존과 동일하게 스킵.
        /// </summary>
        public void EnsureVillagePopulated(int villageId)
        {
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return;

            if (village.TableId <= 0)
                return;

            if (village.HasBeenPopulated)
                return;

            if (village.NpcEntityIds.Count > 0)
                return;

            VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            if (table == null || table.DefaultNpcIds.Count == 0)
            {
                Debug.LogWarning($"[EnsureVillagePopulated] v{villageId} TableId={village.TableId} 테이블 없음 or DefaultNpcList 비어있음");
                return;
            }

            SpawnDefaultNpcsForVillage(village, table);
            // R2: 첫 population 직후 1회 — 함께 태어난 모험가 코호트의 가족/관계 + 호감도 시드 + 연대기 sentiment
            NpcRelationBackfill.Generate(village, village.NpcEntityIds);
            village.HasBeenPopulated = true;
        }

        /// <summary>
        /// 등록된 모든 마을에 대해 EnsureVillagePopulated 호출.
        /// </summary>
        public void EnsureAllVillagesPopulated()
        {
            var villages = AR.s.Village.GetAllVillages();
            foreach (var village in villages)
            {
                EnsureVillagePopulated(village.VillageId);
            }
        }

        // ── 인구 비례 NPC 스폰 (정착지 인구 → 물리 모험가 코호트 수 매핑) ──
        // 정량화 상수는 VillageManager와 공유 — v.Population(경제)과 동일 캘리브레이션 (NPC_SIMPLIFICATION_PLAN §3.2).
        private const int NPC_POP_DIVISOR = VillageManager.ABSTRACT_POP_DIVISOR;
        private const int NPC_POP_CAP = VillageManager.ABSTRACT_POP_CAP;

        /// <summary>
        /// 마을에 스폰할 목표 NPC 수를 정착지 인구에 비례해 계산한다.
        /// count = clamp(round(pop / NPC_POP_DIVISOR), floor, NPC_POP_CAP).
        /// floor = min(roster, cap) — 디자이너 지정 로스터를 바닥 보장하되 roster가 cap 초과 시 Clamp 안전.
        /// 연대기 링크 없음(pop &lt;= 0, 예: 손으로 만든 BaseTown)이면 기존 roster 개수 유지.
        /// </summary>
        private int ComputeTargetNpcCount(VillageData village, VillageTable table)
        {
            int roster = table.DefaultNpcIds.Count;
            if (roster == 0)
                return 0;

            int pop = AR.s.History.GetSettlementPopulation(village.SettlementId);
            if (pop <= 0)
                return roster;

            int scaled = Mathf.RoundToInt(pop / (float)NPC_POP_DIVISOR);
            int floor = Mathf.Min(roster, NPC_POP_CAP);
            return Mathf.Clamp(scaled, floor, NPC_POP_CAP);
        }

        private void SpawnDefaultNpcsForVillage(VillageData village, VillageTable table)
        {
            int roster = table.DefaultNpcIds.Count;
            if (roster == 0)
                return;

            int target = ComputeTargetNpcCount(village, table);
            for (int i = 0; i < target; i++)
            {
                int npcTableId = table.DefaultNpcIds[i % roster];  // 0..roster-1은 지정 로스터, 초과분은 아키타입 순환
                Vector2 spawnPos = GetRandomSpawnPositionInVillage(village, table.SpawnRadius);
                SpawnNewNpc(npcTableId, spawnPos, village.VillageId);
            }

            Debug.Log($"[NpcManager] Spawned {target} NPCs for village {village.VillageId} (roster={roster}, pop-scaled)");
        }

        private Vector2 GetRandomSpawnPositionInVillage(VillageData village, float radius)
        {
            if (radius <= 0f)
                return village.Position;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(0f, radius);
            return village.Position + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        /// <summary>
        /// 비동기 NPC 엔티티 생성.
        /// await 도중 청크가 비활성화되거나 재활성화 레이스가 발생해도 일관성 유지:
        ///   1) IsSpawning 플래그로 중복 호출 차단 (이중 스폰 방지)
        ///   2) CreateNpc 완료 후 청크 상태 재확인 → 비활성이면 GameObject 즉시 폐기 (orphan 방지)
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

            if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
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

            // (제거) 물리-사망 기반 마을 전멸(DepletedAt/Ruin) 트리거 — NPC_SIMPLIFICATION_PLAN D7.
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

                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
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

        public void Load(Dictionary<int, NpcSaveData> npcSaveDatas, int chunkSize)
        {
            _npcSaveDict.Clear();
            _chunkNpcs.Clear();

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

                // 구 세이브 호환 — Description 필드가 없던 시점의 데이터는 직업 풀에서 채워준다
                if (string.IsNullOrEmpty(saveData.Description))
                    saveData.Description = PickDescription(GetPreferredJobType(saveData.NpcTableId));

                _npcSaveDict[entityId] = saveData;

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

        // (제거) Inn 고용/방문자 시스템 — NPC_SIMPLIFICATION_PLAN §4.1 (D6).
        // INN_HIRING_DESIGN.md는 deprecated. 여관 건물/서비스 근접은 유지되나 고용 경로는 없다.

        /// <summary>
        /// EntityId에 매핑된 NpcSaveData를 반환. 없으면 null.
        /// UI 등 외부에서 NPC 메타데이터를 조회할 때 사용.
        /// </summary>
        public NpcSaveData? GetSaveData(int entityId)
        {
            return _npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) ? saveData : null;
        }

        private GlobalEnum.JobType GetPreferredJobType(int npcTableId)
        {
            Tables.NpcTable? table = AR.s.Data.GetNpc(npcTableId);
            return table != null ? table.JobType : GlobalEnum.JobType.None;
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
