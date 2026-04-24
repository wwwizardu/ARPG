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

                NpcSaveData saveData = new NpcSaveData(obj.ObjectId, worldPos);

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
                VillageId = villageId
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
                    EntityIdHelper.DestroyEntity(createdId, false);
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
                EntityIdHelper.DestroyEntity(entityId, false);
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

            if (npcSaveDatas == null || npcSaveDatas.Count == 0)
                return;

            _chunkSize = chunkSize;

            foreach (var kvp in npcSaveDatas)
            {
                NpcSaveData saveData = kvp.Value;

                int entityId = EntityIdHelper.CreateEntity();
                saveData.IsActive = false;
                saveData.EntityId = entityId;

                _npcSaveDict[entityId] = saveData;

                Vector2Int chunkCoord = PositionToChunk(saveData.Position);
                AddNpcToChunk(chunkCoord, entityId);

                if (saveData.VillageId >= 0)
                {
                    AR.s.Village.RegisterNpcToVillage(saveData.VillageId, entityId);
                }
            }

            _isInitialLoaded = true;
            Debug.Log($"[NpcManager] Loaded {_npcSaveDict.Count} NPCs from save data");
        }

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
