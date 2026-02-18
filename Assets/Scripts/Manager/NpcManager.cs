#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Scene;
using ARPG.Utility;

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
        public void RegisterNpcsFromMapFile(List<MapFileObjectData> allNpcObjects, Vector2Int mapFileStartPos, int chunkSize)
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

                if (saveData.IsActive)
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

        private async UniTask SpawnNpc(int entityId, NpcSaveData saveData)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            if (saveData.IsActive)
                return;

            Vector3 spawnPos3D = new Vector3(saveData.Position.x, saveData.Position.y, -0.05f);

            // 발급된 EntityId를 전달하여 동일한 ID로 엔티티 생성
            var (createdId, entity) = await EntityFactory.CreateNpc(saveData.NpcTableId, spawnPos3D, _npcParent, entityId);

            if (createdId < 0 || entity == null)
                return;

            AR.s.Component.AddComponent(createdId, new NpcTag());
            EntityFactory.AddActivationComponent(createdId, _activationDistance, _deactivationDistance);

            saveData.EntityId = createdId;
            saveData.IsActive = true;
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
