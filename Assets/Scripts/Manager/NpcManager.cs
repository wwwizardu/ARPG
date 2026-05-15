#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Scene;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Village;

namespace ARPG.Npc
{
    public class NpcManager : MonoBehaviour
    {
        private Dictionary<int, NpcSaveData> _npcSaveDict = new();
        private Dictionary<Vector2Int, List<int>> _chunkNpcs = new();
        // Inn 고용 시스템 — 마을별 방문자 EntityId 인덱스 (런타임 캐시, 세이브 대상 아님)
        private Dictionary<int, List<int>> _innVisitorsByVillageId = new();
        private Transform? _npcParent;
        private bool _isInitialLoaded = false;
        private int _chunkSize;

        // INN_HIRING_DESIGN.md §2.3 / §2.10
        private const int INN_CAPACITY = 2;
        private const float VISITOR_STAY_DURATION_HOURS = 24f;

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
                    EntityIdHelper.DestroyEntity(entityId);
                    Destroy(entity.gameObject);
                }
            }

            _npcSaveDict.Clear();
            _chunkNpcs.Clear();
            _innVisitorsByVillageId.Clear();
            _isInitialLoaded = false;
        }

        public void SetNpcRoot(Transform inNpcRoot)
        {
            _npcParent = inNpcRoot;
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
                int entityId = EntityIdHelper.CreateEntity();

                NpcSaveData saveData = new NpcSaveData(obj.ObjectId, worldPos)
                {
                    EntityId = entityId,  // 일관성 — 다른 생성 경로(SpawnNewNpc)도 채워줌
                    Description = PickDescription(GetPreferredJobType(obj.ObjectId)),
                };

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
            int entityId = EntityIdHelper.CreateEntity();
            NpcSaveData saveData = new NpcSaveData(npcTableId, position)
            {
                EntityId = entityId,
                VillageId = villageId,
                Description = PickDescription(GetPreferredJobType(npcTableId)),
            };

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
        /// 마을이 비어있으면(or 최초) 기본 NPC를 스폰한다.
        /// - 최초: 즉시 스폰
        /// - 전멸 후: 쿨타임 경과 시 재스폰
        /// </summary>
        public void EnsureVillagePopulated(int villageId)
        {
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return;

            if (village.TableId <= 0)
                return;

            int aliveCount = CountAliveNpcs(village);
            if (aliveCount > 0)
                return;

            VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            if (table == null || table.DefaultNpcIds.Count == 0)
            {
                Debug.LogWarning($"[EnsureVillagePopulated] v{villageId} TableId={village.TableId} 테이블 없음 or DefaultNpcList 비어있음");
                return;
            }

            if (village.HasBeenPopulated == false)
            {
                // 최초 생성 → 즉시 스폰
                SpawnDefaultNpcsForVillage(village, table);
                village.HasBeenPopulated = true;
                village.DepletedAt = 0f;
                return;
            }

            // 전멸 상태 → 쿨타임 체크
            float now = AR.s.Time.CurrentGameTime;
            float elapsed = now - village.DepletedAt;
            if (elapsed >= table.RespawnCooldown)
            {
                Debug.Log($"[EnsureVillagePopulated] v{villageId} 쿨타임 만료, 재스폰 (elapsed={elapsed:F2}h, cooldown={table.RespawnCooldown}h)");
                SpawnDefaultNpcsForVillage(village, table);
                village.DepletedAt = 0f;
            }
            else
            {
                Debug.Log($"[EnsureVillagePopulated] v{villageId} 쿨타임 대기 중 (elapsed={elapsed:F2}h / cooldown={table.RespawnCooldown}h, DepletedAt={village.DepletedAt:F2}, now={now:F2})");
            }
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

        private void SpawnDefaultNpcsForVillage(VillageData village, VillageTable table)
        {
            for (int i = 0; i < table.DefaultNpcIds.Count; i++)
            {
                int npcTableId = table.DefaultNpcIds[i];
                Vector2 spawnPos = GetRandomSpawnPositionInVillage(village, table.SpawnRadius);
                SpawnNewNpc(npcTableId, spawnPos, village.VillageId);
            }

            Debug.Log($"[NpcManager] Spawned {table.DefaultNpcIds.Count} default NPCs for village {village.VillageId}");
        }

        private Vector2 GetRandomSpawnPositionInVillage(VillageData village, float radius)
        {
            if (radius <= 0f)
                return village.Position;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(0f, radius);
            return village.Position + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        private int CountAliveNpcs(VillageData village)
        {
            int count = 0;
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
            {
                int eid = village.NpcEntityIds[i];
                if (_npcSaveDict.TryGetValue(eid, out NpcSaveData? saveData) == false)
                    continue;

                if (saveData.Condition != CharacterConditions.Dead)
                    count++;
            }
            return count;
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

                // 발급된 EntityId를 전달하여 동일한 ID로 엔티티 생성
                var (createdId, entity) = await EntityFactory.CreateNpc(saveData.NpcTableId, spawnPos3D, _npcParent, entityId);

                if (createdId < 0 || entity == null)
                    return;

                // await 도중 청크가 비활성화되었으면 GameObject 폐기 (orphan 방지)
                Vector2Int chunkCoord = PositionToChunk(saveData.Position);
                if (AR.s.Map == null || AR.s.Map.IsChunkActive(chunkCoord) == false)
                {
                    EntityIdHelper.DestroyEntity(createdId, allowRecycle: false, keepRegistered: true);
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

                if (AR.s.Component.TryGetComponent<NpcJobComponent>(createdId, out var job))
                {
                    job.JobType = saveData.JobType;
                    job.SkillLevel = saveData.SkillLevel;
                    AR.s.Component.SetComponent(createdId, job);
                }

                saveData.EntityId = createdId;
                saveData.IsActive = true;

                // 진행 중 build task에 이 NPC가 배정돼 있었다면 NpcBuildAssignmentComponent 재부착 + Build 상태 전이.
                // 청크 비활성으로 일시 디스폰됐다가 돌아온 경우, 그리고 게임 로드 후 첫 스폰 모두 동일 경로.
                // EntityId가 세션 간 보존되므로(EntityIdHelper.RegisterExistingEntity) task.AssignedNpcEntityId가
                // 그대로 이 NPC의 createdId와 일치 → 매칭 성공 → 같은 NPC가 같은 빌딩을 이어서 짓는 상태로 복원.
                ARPG.Systems.System_VillageBuildQueue.ReattachBuildAssignmentIfAny(createdId);
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

            if (AR.s.Component.TryGetComponent<NpcJobComponent>(entityId, out var job))
            {
                saveData.JobType = job.JobType;
                saveData.SkillLevel = job.SkillLevel;
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
                EntityIdHelper.DestroyEntity(entityId, allowRecycle: false, keepRegistered: true);
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

            // Visitor가 사망/이탈하면 인덱스에서 제거 (마을 전멸 체크는 Resident만)
            if (saveData.Status == NpcStatus.InnVisitor)
            {
                RemoveVisitorFromIndex(saveData.StayingAtVillageId, entityId);
                return;
            }

            // 사망이면 마을 전멸 체크
            if (saveData.Condition == CharacterConditions.Dead && saveData.VillageId >= 0)
            {
                VillageData? village = AR.s.Village.GetVillage(saveData.VillageId);
                if (village != null && village.DepletedAt <= 0f && CountAliveNpcs(village) == 0)
                {
                    village.DepletedAt = AR.s.Time.CurrentGameTime;
                    Debug.Log($"[NpcManager] Village {saveData.VillageId} depleted at game time {village.DepletedAt}");
                }
            }
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
            _innVisitorsByVillageId.Clear();

            if (npcSaveDatas == null || npcSaveDatas.Count == 0)
                return;

            _chunkSize = chunkSize;

            foreach (var kvp in npcSaveDatas)
            {
                NpcSaveData saveData = kvp.Value;

                // 저장된 EntityId 그대로 재사용 — 세션 간 NPC 동일성 보장.
                // task.AssignedNpcEntityId 등 다른 시스템이 참조하는 ID가 stale 안 되도록.
                // dict key가 정본(저장 시점의 EntityId), saveData.EntityId는 백업 (둘 다 동일하나 안전망).
                // 등록은 AR.PreReserveSavedEntityIds()가 Initialize 단계에서 일괄 처리 — 여기서 다시 등록하면 이중 등록 LogError.
                int entityId = kvp.Key > 0 ? kvp.Key
                              : (saveData.EntityId > 0 ? saveData.EntityId : EntityIdHelper.CreateEntity());

                saveData.IsActive = false;
                saveData.EntityId = entityId;

                // 구 세이브 호환 — Description 필드가 없던 시점의 데이터는 직업 풀에서 채워준다
                if (string.IsNullOrEmpty(saveData.Description))
                    saveData.Description = PickDescription(GetPreferredJobType(saveData.NpcTableId));

                _npcSaveDict[entityId] = saveData;

                // Inn 시스템 옵션 1: Visitor는 월드에 출현하지 않으므로 _chunkNpcs/마을 등록 모두 생략
                if (saveData.Status == NpcStatus.InnVisitor)
                {
                    AddVisitorToIndex(saveData.StayingAtVillageId, entityId);
                }
                else
                {
                    Vector2Int chunkCoord = PositionToChunk(saveData.Position);
                    AddNpcToChunk(chunkCoord, entityId);

                    if (saveData.VillageId >= 0)
                        AR.s.Village.RegisterNpcToVillage(saveData.VillageId, entityId);
                }
            }

            _isInitialLoaded = true;
            Debug.Log($"[NpcManager] Loaded {_npcSaveDict.Count} NPCs from save data");
        }

        #region Inn 고용 시스템 (INN_HIRING_DESIGN.md)

        /// <summary>
        /// EntityId에 매핑된 NpcSaveData를 반환. 없으면 null.
        /// UI 등 외부에서 NPC 메타데이터를 조회할 때 사용.
        /// </summary>
        public NpcSaveData? GetSaveData(int entityId)
        {
            return _npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) ? saveData : null;
        }

        /// <summary>
        /// 여관에 머물 수 있는 방문자 수. 현재 고정 2 (§2.3).
        /// 향후 Inn 업그레이드 시스템 도입 시 villageId 기반으로 InnLevel을 조회하도록 변경.
        /// </summary>
        public int GetInnCapacity(int villageId) => INN_CAPACITY;

        /// <summary>
        /// 마을의 현재 방문자 EntityId 목록을 반환. 만료된 방문자는 포함될 수 있음 — 호출자가 EvictExpiredVisitors 선행 권장.
        /// </summary>
        public List<int> GetInnVisitors(int villageId)
        {
            if (_innVisitorsByVillageId.TryGetValue(villageId, out List<int>? list) == false)
                return new List<int>();
            return list;
        }

        public int GetInnVisitorCount(int villageId)
        {
            if (_innVisitorsByVillageId.TryGetValue(villageId, out List<int>? list) == false)
                return 0;
            return list.Count;
        }

        /// <summary>
        /// Visitor의 남은 체류 시간(게임시간 시) 반환. Resident이거나 만료/없으면 0.
        /// </summary>
        public float GetVisitorRemainingHours(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false) return 0f;
            if (saveData.Status != NpcStatus.InnVisitor) return 0f;

            float elapsed = AR.s.Time.CurrentGameTime - saveData.ArrivedGameTime;
            float remaining = VISITOR_STAY_DURATION_HOURS - elapsed;
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// 방문자 NPC를 등록한다 — 월드에 출현하지 않고 SaveData/UI 인덱스에만 존재.
        /// 실제 GameObject/AI는 플레이어가 고용(<see cref="HireVisitor"/>)하는 시점에 처음 생성된다.
        /// 도착 시각(ArrivedGameTime)을 기록하여 §2.10 만료 시스템과 연동.
        /// </summary>
        public int SpawnVisitorNpc(int npcTableId, Vector2 position, int villageId)
        {
            int entityId = EntityIdHelper.CreateEntity();
            NpcSaveData saveData = new NpcSaveData(npcTableId, position)
            {
                EntityId = entityId,
                VillageId = -1,
                Status = NpcStatus.InnVisitor,
                StayingAtVillageId = villageId,
                ArrivedGameTime = AR.s.Time.CurrentGameTime,
                Description = PickDescription(GetPreferredJobType(npcTableId)),
            };

            _npcSaveDict[entityId] = saveData;
            AddVisitorToIndex(villageId, entityId);
            // Visitor는 월드에 출현하지 않으므로 _chunkNpcs/SpawnNpc는 호출하지 않는다.
            // saveData.Position은 고용 시 마을원으로 첫 출현할 좌표로 보존됨.

            Debug.Log($"[NpcManager] Visitor 등록 v{villageId} entity={entityId} npcTableId={npcTableId} (UI 전용)");
            return entityId;
        }

        /// <summary>
        /// 방문자를 마을 정식 거주자로 승격한다.
        /// 실패 사유는 failReason에 한국어 메시지로 반환 — UI 안내용.
        /// </summary>
        public bool HireVisitor(int entityId, out string failReason)
        {
            failReason = string.Empty;

            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
            {
                failReason = "방문자 정보를 찾을 수 없습니다.";
                return false;
            }

            if (saveData.Status != NpcStatus.InnVisitor)
            {
                failReason = "고용 가능한 방문자가 아닙니다.";
                return false;
            }

            int villageId = saveData.StayingAtVillageId;
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
            {
                failReason = "마을을 찾을 수 없습니다.";
                return false;
            }

            // 비용 계산 (§2.7) — Step 7에서 직업 보너스까지 합산
            int hireCost = CalculateHireCost(saveData, village);
            int playerGold = AR.s.Data.Player?.Gold ?? 0;
            if (playerGold < hireCost)
            {
                failReason = $"골드 부족 (보유 {playerGold}G / 필요 {hireCost}G)";
                return false;
            }

            // 식량 체크/차감 — 새 거주자 1명분(5)
            const int FOOD_PER_NPC = 5;
            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(village.EntityId, out var storage) == false)
            {
                failReason = "마을 자원 정보를 찾을 수 없습니다.";
                return false;
            }
            if (storage.FoodAmount < FOOD_PER_NPC)
            {
                failReason = $"식량 부족 (보유 {storage.FoodAmount} / 필요 {FOOD_PER_NPC})";
                return false;
            }

            // 통과 — 자원 차감 + 상태 전이
            if (hireCost > 0 && AR.s.Data.Player != null)
                AR.s.Data.Player.Gold -= hireCost;

            storage.FoodAmount -= FOOD_PER_NPC;
            AR.s.Component.SetComponent(village.EntityId, storage);

            RemoveVisitorFromIndex(villageId, entityId);
            saveData.Status = NpcStatus.Resident;
            saveData.VillageId = villageId;
            saveData.StayingAtVillageId = 0;
            saveData.JobType = saveData.JobType != GlobalEnum.JobType.None
                ? saveData.JobType
                : GetPreferredJobType(saveData.NpcTableId);

            AR.s.Village.RegisterNpcToVillage(villageId, entityId);

            // Visitor는 월드에 없던 상태 — 고용 시점에 처음 출현시킨다.
            // SpawnNpc가 NpcVillageComponent.VillageId / NpcJobComponent.JobType을 saveData에서 동기화함.
            Vector2Int chunkCoord = PositionToChunk(saveData.Position);
            AddNpcToChunk(chunkCoord, entityId);
            if (AR.s.Map.IsChunkActive(chunkCoord))
                SpawnNpc(entityId, saveData).Forget();

            Debug.Log($"[NpcManager] Visitor 고용 v{villageId} entity={entityId} cost={hireCost}G");
            return true;
        }

        /// <summary>
        /// 마을의 방문자 중 체류 시간 만료(24h 경과)된 자를 디스폰한다.
        /// 매 이민 틱 진입 시 호출 (§2.10) — 별도 스케줄러 없음.
        /// </summary>
        public void EvictExpiredVisitors(int villageId)
        {
            if (_innVisitorsByVillageId.TryGetValue(villageId, out List<int>? list) == false)
                return;
            if (list.Count == 0) return;

            float now = AR.s.Time.CurrentGameTime;

            // 뒤에서부터 순회 — 제거 중 인덱스 보존
            for (int i = list.Count - 1; i >= 0; i--)
            {
                int entityId = list[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                {
                    list.RemoveAt(i);
                    continue;
                }

                float elapsed = now - saveData.ArrivedGameTime;
                if (elapsed < VISITOR_STAY_DURATION_HOURS) continue;

                Debug.Log($"[NpcManager] Visitor 만료 이탈 v{villageId} entity={entityId} elapsed={elapsed:F1}h");
                DespawnVisitor(entityId, saveData);
                list.RemoveAt(i);
            }
        }

        // ========== 내부 헬퍼 ==========

        private void AddVisitorToIndex(int villageId, int entityId)
        {
            if (_innVisitorsByVillageId.TryGetValue(villageId, out List<int>? list) == false)
            {
                list = new List<int>();
                _innVisitorsByVillageId[villageId] = list;
            }
            if (list.Contains(entityId) == false)
                list.Add(entityId);
        }

        private void RemoveVisitorFromIndex(int villageId, int entityId)
        {
            if (_innVisitorsByVillageId.TryGetValue(villageId, out List<int>? list))
                list.Remove(entityId);
        }

        private void DespawnVisitor(int entityId, NpcSaveData saveData)
        {
            // 옵션 1: Visitor는 월드에 출현하지 않으므로 IsActive=false가 일반적이지만,
            // 고용 후 즉시 만료 등의 레이스를 대비해 활성 엔티티도 안전하게 정리.
            if (saveData.IsActive && AR.s.Message.TryGetEntity(entityId, out var entity))
            {
                if (entity != null)
                    Destroy(entity.gameObject);
            }

            // EntityIdHelper에서 ID 회수 (출현 여부와 무관하게 SpawnVisitorNpc에서 발급된 ID 해제)
            EntityIdHelper.DestroyEntity(entityId, false);

            // Visitor는 _chunkNpcs에 등록되지 않지만, 고용 직후 만료 케이스 대비 안전 호출
            Vector2Int chunkCoord = PositionToChunk(saveData.Position);
            RemoveNpcFromChunk(chunkCoord, entityId);
            _npcSaveDict.Remove(entityId);
        }

        /// <summary>
        /// INN_HIRING_DESIGN.md §2.7 — HireCost = BaseCost[Stage] + JobBonusCost[JobType].
        /// 직업별 보너스는 단일 사전으로 시작(시트 컬럼 신설 회피); 후속 튜닝 시 JobBonusTable로 이관 검토.
        /// </summary>
        private int CalculateHireCost(NpcSaveData saveData, VillageData village)
        {
            Tables.VillageStageTable? stageTable = AR.s.Data.GetVillageStage(village.Stage);
            int baseCost = stageTable != null ? stageTable.HireBaseCost : 0;
            if (stageTable == null)
                Debug.LogError($"[NpcManager] VillageStageTable not loaded — stage={village.Stage}");

            GlobalEnum.JobType desiredJob = saveData.JobType != GlobalEnum.JobType.None
                ? saveData.JobType
                : GetPreferredJobType(saveData.NpcTableId);

            return baseCost + GetJobBonusCost(desiredJob);
        }

        /// <summary>
        /// §2.7 직업별 가산 비용 — 노동/채집은 0, 전문직일수록 비싸짐.
        /// </summary>
        private static int GetJobBonusCost(GlobalEnum.JobType jobType)
        {
            return jobType switch
            {
                GlobalEnum.JobType.None       => 0,
                GlobalEnum.JobType.Gatherer   => 0,    // 만능 fallback
                GlobalEnum.JobType.Woodcutter => 10,
                GlobalEnum.JobType.Farmer     => 20,
                GlobalEnum.JobType.Hunter     => 30,
                GlobalEnum.JobType.Miner      => 30,
                GlobalEnum.JobType.Builder    => 40,
                GlobalEnum.JobType.Guard      => 50,
                GlobalEnum.JobType.Merchant   => 50,
                GlobalEnum.JobType.Blacksmith => 100,
                GlobalEnum.JobType.Scholar    => 120,
                GlobalEnum.JobType.Chief      => 200,
                _ => 0,
            };
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
