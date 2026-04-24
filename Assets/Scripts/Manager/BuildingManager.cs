#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.Component;
using ARPG.Factory;
using ARPG.Utility;
using ARPG.Village;

namespace ARPG.Building
{
    /// <summary>
    /// SpawnType=Entity 건물의 런타임 라이프사이클 관리자.
    /// NpcManager와 동일한 패턴: 상태 dict + 청크 매핑 + OnChunkActivated/Deactivated.
    /// 추가로 `_occupiedTiles` HashSet을 유지하여 VillageTileFinder 등의 빈 칸 조회를 처리한다.
    /// </summary>
    public class BuildingManager : MonoBehaviour
    {
        private Dictionary<int, BuildingSaveData> _buildingSaveDict = new();
        private Dictionary<Vector2Int, List<int>> _chunkBuildings = new();
        private HashSet<Vector2Int> _occupiedTiles = new();
        private Transform? _buildingParent;
        private int _chunkSize;

        public void Initialize()
        {
            _chunkSize = AR.s.Map.chunkSize;

            var savedBuildings = AR.s.Data.BuildingSaveDatas;
            if (savedBuildings != null && savedBuildings.Count > 0)
            {
                Load(savedBuildings, AR.s.Map.chunkSize);
            }
        }

        public void Reset()
        {
            foreach (var kvp in _buildingSaveDict)
            {
                BuildingSaveData saveData = kvp.Value;
                if (saveData.IsActive == false)
                    continue;

                int entityId = kvp.Key;
                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    EntityIdHelper.DestroyEntity(entityId);
                    Destroy(entity.gameObject);
                }
            }

            _buildingSaveDict.Clear();
            _chunkBuildings.Clear();
            _occupiedTiles.Clear();
        }

        public void SetBuildingRoot(Transform inBuildingRoot)
        {
            _buildingParent = inBuildingRoot;
        }

        /// <summary>
        /// 타일 위치가 이미 다른 Entity 건물로 점유되어 있는지 확인.
        /// VillageTileFinder 등이 빈 칸을 찾을 때 호출.
        /// </summary>
        public bool IsTileOccupied(int worldX, int worldY)
        {
            return _occupiedTiles.Contains(new Vector2Int(worldX, worldY));
        }

        /// <summary>
        /// 새 건물을 배치한다. SaveData 등록 + 점유 칸 기록 + 청크 매핑 추가 + 활성 청크면 즉시 스폰.
        /// MapManager.PlaceObject에서 SpawnType=Entity 분기로 호출됨.
        /// </summary>
        public int PlaceBuilding(int worldTileX, int worldTileY, int tableId, int villageId = -1)
        {
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(tableId);
            if (table == null)
            {
                Debug.LogError($"[BuildingManager] PlaceBuilding - BuildableItemTable not found: {tableId}");
                return -1;
            }

            int entityId = EntityIdHelper.CreateEntity();
            BuildingSaveData saveData = new BuildingSaveData(tableId, worldTileX, worldTileY)
            {
                EntityId = entityId,
                VillageId = villageId,
                CurrentHp = table.HP
            };

            _buildingSaveDict[entityId] = saveData;

            // 점유 타일 등록 (멀티타일 대응)
            AddOccupiedTiles(worldTileX, worldTileY, table.Size_Width, table.Size_Height);

            // 청크 매핑
            Vector2Int chunkCoord = TileToChunk(worldTileX, worldTileY);
            AddBuildingToChunk(chunkCoord, entityId);

            // 활성 청크면 즉시 스폰
            if (AR.s.Map.IsChunkActive(chunkCoord))
            {
                SpawnBuilding(entityId, saveData).Forget();
            }

            Debug.Log($"[BuildingManager] PlaceBuilding - EntityId: {entityId}, TableId: {tableId}, Tile: ({worldTileX},{worldTileY}), VillageId: {villageId}");
            return entityId;
        }

        /// <summary>
        /// 청크 활성화 시 호출. 해당 청크의 건물들을 SpawnBuilding으로 복원.
        /// </summary>
        public void OnChunkActivated(Vector2Int chunkCoord)
        {
            if (_chunkBuildings.TryGetValue(chunkCoord, out List<int>? entityIds) == false)
                return;

            for (int i = 0; i < entityIds.Count; i++)
            {
                int entityId = entityIds[i];

                if (_buildingSaveDict.TryGetValue(entityId, out BuildingSaveData? saveData) == false)
                    continue;

                // IsSpawning: 이전 PlaceBuilding/OnChunkActivated의 CreateBuilding이 아직 대기 중 → 재호출 차단
                if (saveData.IsActive || saveData.IsSpawning)
                    continue;

                if (saveData.CurrentHp <= 0)
                    continue;

                SpawnBuilding(entityId, saveData).Forget();
            }
        }

        /// <summary>
        /// 청크 비활성화 시 호출. 해당 청크의 건물을 저장 후 Destroy.
        /// </summary>
        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (_chunkBuildings.TryGetValue(chunkCoord, out List<int>? entityIds) == false)
                return;

            for (int i = 0; i < entityIds.Count; i++)
            {
                int entityId = entityIds[i];

                if (_buildingSaveDict.TryGetValue(entityId, out BuildingSaveData? saveData) == false)
                    continue;

                if (saveData.IsActive == false)
                    continue;

                SaveAndDeactivateBuilding(entityId, saveData);
            }
        }

        /// <summary>
        /// System_EntityDestroy에서 BuildingTag 확인 후 호출.
        /// 건물이 파괴되면 점유 타일/청크 매핑에서 제거하고 SaveDict에서도 삭제.
        /// </summary>
        public void UnregisterBuildingByEntityId(int entityId)
        {
            if (_buildingSaveDict.TryGetValue(entityId, out BuildingSaveData? saveData) == false)
                return;

            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(saveData.TableId);
            int w = table != null ? table.Size_Width : 1;
            int h = table != null ? table.Size_Height : 1;

            RemoveOccupiedTiles(saveData.WorldTileX, saveData.WorldTileY, w, h);

            Vector2Int chunkCoord = TileToChunk(saveData.WorldTileX, saveData.WorldTileY);
            RemoveBuildingFromChunk(chunkCoord, entityId);

            _buildingSaveDict.Remove(entityId);
        }

        /// <summary>
        /// 비동기 건물 엔티티 생성.
        /// await 도중 청크가 비활성화되거나 재활성화 레이스가 발생해도 일관성 유지:
        ///   1) IsSpawning 플래그로 중복 호출 차단 (이중 스폰 방지)
        ///   2) CreateBuilding 완료 후 청크 상태 재확인 → 비활성이면 GameObject 즉시 폐기 (orphan 방지)
        /// </summary>
        private async UniTask SpawnBuilding(int entityId, BuildingSaveData saveData)
        {
            if (saveData.IsActive || saveData.IsSpawning)
                return;
            saveData.IsSpawning = true;

            try
            {
                var (createdId, entity) = await BuildingFactory.CreateBuilding(
                    saveData.TableId,
                    saveData.WorldTileX,
                    saveData.WorldTileY,
                    saveData.VillageId,
                    entityId,
                    saveData.CurrentHp > 0 ? saveData.CurrentHp : -1);

                if (createdId < 0 || entity == null)
                    return;

                // await 도중 청크가 비활성화되었으면 GameObject 폐기 (orphan 방지)
                Vector2Int chunkCoord = TileToChunk(saveData.WorldTileX, saveData.WorldTileY);
                if (AR.s.Map == null || AR.s.Map.IsChunkActive(chunkCoord) == false)
                {
                    EntityIdHelper.DestroyEntity(createdId, false);
                    if (entity != null)
                        Destroy(entity.gameObject);
                    return;
                }

                if (_buildingParent != null)
                    entity.transform.SetParent(_buildingParent, true);

                saveData.EntityId = createdId;
                saveData.IsActive = true;
            }
            finally
            {
                saveData.IsSpawning = false;
            }
        }

        private void SaveAndDeactivateBuilding(int entityId, BuildingSaveData saveData)
        {
            if (AR.s.Component.TryGetComponent<BuildingComponent>(entityId, out var buildingComp))
            {
                saveData.CurrentHp = buildingComp.CurrentHp;
                saveData.VillageId = buildingComp.VillageId;
            }

            if (AR.s.Message.TryGetEntity(entityId, out var entity))
            {
                // ID 재활용 방지 (다음 로드 시 같은 ID 사용)
                EntityIdHelper.DestroyEntity(entityId, false);
                Destroy(entity.gameObject);
            }

            saveData.IsActive = false;
        }

        public void SaveAllActiveBuildings()
        {
            foreach (var kvp in _buildingSaveDict)
            {
                BuildingSaveData saveData = kvp.Value;
                if (saveData.IsActive == false)
                    continue;

                int entityId = kvp.Key;
                if (AR.s.Component.TryGetComponent<BuildingComponent>(entityId, out var buildingComp))
                {
                    saveData.CurrentHp = buildingComp.CurrentHp;
                    saveData.VillageId = buildingComp.VillageId;
                }
            }
        }

        public Dictionary<int, BuildingSaveData> Save()
        {
            SaveAllActiveBuildings();
            return new Dictionary<int, BuildingSaveData>(_buildingSaveDict);
        }

        public void Load(Dictionary<int, BuildingSaveData> savedBuildings, int chunkSize)
        {
            _buildingSaveDict.Clear();
            _chunkBuildings.Clear();
            _occupiedTiles.Clear();

            if (savedBuildings == null || savedBuildings.Count == 0)
                return;

            _chunkSize = chunkSize;

            foreach (var kvp in savedBuildings)
            {
                BuildingSaveData saveData = kvp.Value;

                // 파괴된 건물 (HP<=0)은 무시
                if (saveData.CurrentHp <= 0)
                    continue;

                int entityId = EntityIdHelper.CreateEntity();
                saveData.IsActive = false;
                saveData.EntityId = entityId;

                _buildingSaveDict[entityId] = saveData;

                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(saveData.TableId);
                int w = table != null ? table.Size_Width : 1;
                int h = table != null ? table.Size_Height : 1;
                AddOccupiedTiles(saveData.WorldTileX, saveData.WorldTileY, w, h);

                Vector2Int chunkCoord = TileToChunk(saveData.WorldTileX, saveData.WorldTileY);
                AddBuildingToChunk(chunkCoord, entityId);
            }

            Debug.Log($"[BuildingManager] Loaded {_buildingSaveDict.Count} buildings from save data");
        }

        #region 유틸리티

        private Vector2Int TileToChunk(int worldX, int worldY)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / _chunkSize);
            int chunkY = Mathf.FloorToInt((float)worldY / _chunkSize);
            return new Vector2Int(chunkX, chunkY);
        }

        private void AddBuildingToChunk(Vector2Int chunkCoord, int entityId)
        {
            if (_chunkBuildings.TryGetValue(chunkCoord, out List<int>? list) == false)
            {
                list = new List<int>();
                _chunkBuildings[chunkCoord] = list;
            }
            list.Add(entityId);
        }

        private void RemoveBuildingFromChunk(Vector2Int chunkCoord, int entityId)
        {
            if (_chunkBuildings.TryGetValue(chunkCoord, out List<int>? list))
            {
                list.Remove(entityId);
            }
        }

        private void AddOccupiedTiles(int worldX, int worldY, int width, int height)
        {
            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    _occupiedTiles.Add(new Vector2Int(worldX + dx, worldY + dy));
                }
            }
        }

        private void RemoveOccupiedTiles(int worldX, int worldY, int width, int height)
        {
            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    _occupiedTiles.Remove(new Vector2Int(worldX + dx, worldY + dy));
                }
            }
        }

        #endregion
    }
}
