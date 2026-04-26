#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Map;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageManager : MonoBehaviour
    {
        private const int DEFAULT_RESOURCE_CAP = 50;

        private Dictionary<int, VillageData> _villages = new();

        public void Initialize()
        {
            if (_villages.Count > 0)
                return;

            List<MapFileData> villageMaps = AR.s.Map.GetMapFileDataByType(MapType.Village);
            for (int i = 0; i < villageMaps.Count; i++)
            {
                MapFileData mapFileData = villageMaps[i];
                Vector2 villageCenter = new Vector2(
                    mapFileData.StartPosition.x + mapFileData.Width * 0.5f,
                    mapFileData.StartPosition.y + mapFileData.Height * 0.5f
                );
                // 마을 인덱스 + 1 을 VillageTableId로 사용 (관례). 없으면 기본 스폰은 no-op.
                int tableId = i + 1;
                RegisterVillage(i, villageCenter, tableId);
                Debug.Log($"[VillageManager] Initial village {i} created at {villageCenter} (TableId={tableId})");
            }
        }

        public void Reset()
        {
            foreach (VillageData data in _villages.Values)
            {
                if (data.EntityId >= 0)
                {
                    EntityIdHelper.DestroyEntity(data.EntityId, false);
                    data.EntityId = -1;
                }
            }
            _villages.Clear();
        }

        public void RegisterVillage(int villageId, Vector2 position, int tableId = 0)
        {
            if (_villages.ContainsKey(villageId))
            {
                Debug.LogWarning($"[VillageManager] Village {villageId} already registered");
                return;
            }

            VillageData data = new VillageData(villageId, position)
            {
                TableId = tableId,
                RegisteredAt = AR.s.Time.CurrentGameTime,
            };
            _villages[villageId] = data;

            CreateStorageEntity(data);
        }

        public VillageData? GetVillage(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data))
            {
                return data;
            }
            return null;
        }

        public void ProduceResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;

            int cap = GetCap(data, type);
            int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
            int newAmount = Mathf.Min(current + amount, cap);

            data.Resources[type] = newAmount;

            SyncStorageComponent(data);

            AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
        }

        public bool ConsumeResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
            if (current < amount)
                return false;

            data.Resources[type] = current - amount;

            SyncStorageComponent(data);

            AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
            return true;
        }

        private static string BuildResourceStatusNotify(VillageData data)
        {
            int food = GetInt(data, GlobalEnum.ItemType.Food);
            int wood = GetInt(data, GlobalEnum.ItemType.Wood);
            int stone = GetInt(data, GlobalEnum.ItemType.Stone);
            int foodCap = GetCap(data, GlobalEnum.ItemType.Food);
            int woodCap = GetCap(data, GlobalEnum.ItemType.Wood);
            int stoneCap = GetCap(data, GlobalEnum.ItemType.Stone);
            return $"마을 {data.VillageId} [{data.Stage}]: Food {food}/{foodCap}  Wood {wood}/{woodCap}  Stone {stone}/{stoneCap}";
        }

        public int GetResourceAmount(int villageId, GlobalEnum.ItemType type)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return 0;

            if (data.Resources.TryGetValue(type, out int amount))
                return amount;

            return 0;
        }

        public Dictionary<int, VillageData>.ValueCollection GetAllVillages()
        {
            return _villages.Values;
        }

        public int GetVillageCount()
        {
            return _villages.Count;
        }

        /// <summary>
        /// 월드 좌표가 소속된 마을 Id 반환. SpawnRadius 이내에서 가장 가까운 마을 선택.
        /// 해당 범위 내 마을이 없으면 -1.
        /// </summary>
        public int FindVillageContaining(int worldX, int worldY)
        {
            float bestSqrDist = float.MaxValue;
            int bestId = -1;

            foreach (VillageData data in _villages.Values)
            {
                Tables.VillageTable? table = AR.s.Data.GetVillageTable(data.TableId);
                float radius = table != null ? table.SpawnRadius : 0f;
                if (radius <= 0f)
                    continue;

                float dx = data.PositionX - worldX;
                float dy = data.PositionY - worldY;
                float sqr = dx * dx + dy * dy;

                if (sqr > radius * radius)
                    continue;

                if (sqr < bestSqrDist)
                {
                    bestSqrDist = sqr;
                    bestId = data.VillageId;
                }
            }

            return bestId;
        }

        public void RegisterNpcToVillage(int villageId, int npcEntityId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;

            if (data.NpcEntityIds.Contains(npcEntityId))
                return;

            data.NpcEntityIds.Add(npcEntityId);
            data.Population = data.NpcEntityIds.Count;
        }

        public List<VillageData> Save()
        {
            List<VillageData> list = new List<VillageData>(_villages.Count);
            foreach (VillageData village in _villages.Values)
            {
                list.Add(village);
            }
            return list;
        }

        public void Load(List<VillageData> villageDatas)
        {
            _villages.Clear();

            if (villageDatas == null)
                return;

            for (int i = 0; i < villageDatas.Count; i++)
            {
                VillageData data = villageDatas[i];

                // 하위 호환: TableId 필드 없이 저장된 구 세이브 → VillageId+1 관례로 자동 부여
                if (data.TableId <= 0)
                    data.TableId = data.VillageId + 1;

                if (data.ResourceCaps == null)
                    data.ResourceCaps = new Dictionary<GlobalEnum.ItemType, int>();
                if (data.RegisteredAt <= 0f)
                    data.RegisteredAt = AR.s.Time.CurrentGameTime;

                // Phase A: FirstBuildStartedAt 기본 -1 보정 (구 세이브는 0으로 역직렬화될 수 있음)
                if (data.HasCampfire == false && data.FirstBuildStartedAt == 0f)
                    data.FirstBuildStartedAt = -1f;

                // Phase B 마이그레이션
                if (data.PlacedObjectTypeIds == null)
                    data.PlacedObjectTypeIds = new List<int>();

                // HasCampfire == true → PlacedObjectTypeIds에 100 추가 (중복 방지)
                if (data.HasCampfire && data.PlacedObjectTypeIds.Contains(100) == false)
                    data.PlacedObjectTypeIds.Add(100);

                // FirstBuild* 진행 중이던 Campfire 태스크 → CurrentBuild* 필드로 승격
                if (data.CurrentBuildTableId == 0 && data.FirstBuildStartedAt >= 0f)
                {
                    data.CurrentBuildTableId = 100;
                    data.CurrentBuildStartedAt = data.FirstBuildStartedAt;
                    data.CurrentBuildTileX = data.FirstBuildTileX;
                    data.CurrentBuildTileY = data.FirstBuildTileY;
                    data.CurrentBuildReservedWood = 3;  // Phase A CAMPFIRE_WOOD_COST
                    data.CurrentBuildReservedStone = 0;
                }

                // Phase C 마이그레이션
                if (data.WallSegments == null)
                    data.WallSegments = new List<WallSegmentSaveData>();
                // Bounds가 0인 구 세이브 → CreateStorageEntity가 Stage 기준으로 자동 산출

                _villages[data.VillageId] = data;
                CreateStorageEntity(data);
                RestoreTaskFromData(data);
            }

            Debug.Log($"[VillageManager] Loaded {_villages.Count} villages");
        }

        private void CreateStorageEntity(VillageData data)
        {
            int entityId = EntityIdHelper.CreateEntity();
            data.EntityId = entityId;

            VillageStorageComponent storage = new VillageStorageComponent
            {
                VillageId = data.VillageId,
                FoodAmount = GetInt(data, GlobalEnum.ItemType.Food),
                WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood),
                StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone),
                FoodCap = GetCap(data, GlobalEnum.ItemType.Food),
                WoodCap = GetCap(data, GlobalEnum.ItemType.Wood),
                StoneCap = GetCap(data, GlobalEnum.ItemType.Stone),
                StoneTimer = data.StoneTimer,
                HungerHoursAccumulated = data.HungerHoursAccumulated,
                SurplusFlags = 0,
            };
            AR.s.Component.AddComponent(entityId, storage);

            // Phase C: VillageComponent 함께 부착 (Stage / Bounds / ThreatLevel)
            // Bounds가 세이브에 없으면 Stage 기준 기본값 산출
            RectInt bounds;
            if (data.BoundsW > 0 && data.BoundsH > 0)
            {
                bounds = new RectInt(data.BoundsX, data.BoundsY, data.BoundsW, data.BoundsH);
            }
            else
            {
                int radius = GetBoundsRadius(data.Stage);
                bounds = new RectInt(
                    Mathf.FloorToInt(data.PositionX) - radius,
                    Mathf.FloorToInt(data.PositionY) - radius,
                    radius * 2, radius * 2);
                data.BoundsX = bounds.x;
                data.BoundsY = bounds.y;
                data.BoundsW = bounds.width;
                data.BoundsH = bounds.height;
            }
            AR.s.Component.AddComponent(entityId, new VillageComponent
            {
                VillageId = data.VillageId,
                Stage = data.Stage,
                Bounds = bounds,
                ThreatLevel = data.ThreatLevel,
                WallSegmentCount = data.WallSegmentCount,
                CompletedWallSegments = data.CompletedWallSegments,
            });

            // Phase C: 벽 빌더 활성 상태 복원
            if (data.WallPlanRequested)
                AR.s.Component.AddComponent(entityId, new WallPlanRequestTag());
        }

        /// <summary>
        /// Phase C: Stage별 마을 경계 반경. 승격 시 Bounds 확장에 사용.
        /// </summary>
        public static int GetBoundsRadius(VillageStage stage)
        {
            return stage switch
            {
                VillageStage.Settlement => 6,
                VillageStage.Hamlet     => 10,
                VillageStage.Village    => 14,
                VillageStage.Town       => 18,
                VillageStage.City       => 24,
                _ => 6,
            };
        }

        private void SyncStorageComponent(VillageData data)
        {
            if (data.EntityId < 0)
                return;

            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(data.EntityId, out var storage) == false)
                return;

            storage.FoodAmount = GetInt(data, GlobalEnum.ItemType.Food);
            storage.WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood);
            storage.StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone);
            AR.s.Component.SetComponent(data.EntityId, storage);
        }

        private static int GetInt(VillageData data, GlobalEnum.ItemType type)
        {
            return data.Resources.TryGetValue(type, out int v) ? v : 0;
        }

        private static int GetCap(VillageData data, GlobalEnum.ItemType type)
        {
            if (data.ResourceCaps.TryGetValue(type, out int cap))
                return cap;
            return DEFAULT_RESOURCE_CAP;
        }

        // ========== Phase B: 오브젝트 배치 콜백 ==========

        /// <summary>
        /// 오브젝트 배치 완료 시 호출. 데이터 주도 Cap 확장 + 향후 효과 훅.
        /// </summary>
        public void OnObjectPlaced(int villageId, int tableId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false)
                return;

            Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(tableId);
            if (t == null)
                return;

            // Cap 확장: 테이블 컬럼 그대로 가산
            if (t.StorageCap_Food > 0)
                AddCap(v, GlobalEnum.ItemType.Food, t.StorageCap_Food);
            if (t.StorageCap_Wood > 0)
                AddCap(v, GlobalEnum.ItemType.Wood, t.StorageCap_Wood);
            if (t.StorageCap_Stone > 0)
                AddCap(v, GlobalEnum.ItemType.Stone, t.StorageCap_Stone);

            // Function 3 (CropPlot 생산 보너스), 4 (Well 생산 ×1.05) 는 Phase D에서 본격 처리
        }

        private void AddCap(VillageData v, GlobalEnum.ItemType type, int delta)
        {
            int cur = v.ResourceCaps.TryGetValue(type, out int c) ? c : DEFAULT_RESOURCE_CAP;
            int next = cur + delta;
            v.ResourceCaps[type] = next;

            // VillageStorageComponent 동기화
            if (v.EntityId >= 0 &&
                AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s))
            {
                if (type == GlobalEnum.ItemType.Wood) s.WoodCap = next;
                else if (type == GlobalEnum.ItemType.Stone) s.StoneCap = next;
                else if (type == GlobalEnum.ItemType.Food) s.FoodCap = next;
                AR.s.Component.SetComponent(v.EntityId, s);
            }

            Debug.Log($"[Cap] v{v.VillageId} {type} +{delta} → {next}");
        }

        // ========== Phase B: 진행 중 태스크 세이브 동기화 ==========

        /// <summary>
        /// 세이브 직전 호출. ECS 컴포넌트 → VillageData 평면 필드 미러링.
        /// 동기화 대상: ObjectPlacementTaskComponent, VillageComponent, WallPlanRequestTag.
        /// </summary>
        public void SyncTaskToData()
        {
            foreach (VillageData v in _villages.Values)
            {
                if (v.EntityId < 0)
                {
                    v.CurrentBuildTableId = 0;
                    v.CurrentBuildStartedAt = -1f;
                    continue;
                }

                // 진행 중 빌드 태스크
                if (AR.s.Component.TryGetComponent<ObjectPlacementTaskComponent>(v.EntityId, out var task))
                {
                    v.CurrentBuildTableId = task.TargetTableId;
                    v.CurrentBuildStartedAt = task.StartedAt;
                    v.CurrentBuildTileX = task.TileX;
                    v.CurrentBuildTileY = task.TileY;
                    v.CurrentBuildReservedWood = task.ReservedWoodCost;
                    v.CurrentBuildReservedStone = task.ReservedStoneCost;
                }
                else
                {
                    v.CurrentBuildTableId = 0;
                    v.CurrentBuildStartedAt = -1f;
                }

                // Phase C: VillageComponent 미러
                if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
                {
                    v.Stage = vc.Stage;
                    v.BoundsX = vc.Bounds.x;
                    v.BoundsY = vc.Bounds.y;
                    v.BoundsW = vc.Bounds.width;
                    v.BoundsH = vc.Bounds.height;
                    v.ThreatLevel = vc.ThreatLevel;
                    v.WallSegmentCount = vc.WallSegmentCount;
                    v.CompletedWallSegments = vc.CompletedWallSegments;
                }

                // Phase C: 벽 빌더 활성 플래그
                v.WallPlanRequested = AR.s.Component.HasComponent<WallPlanRequestTag>(v.EntityId);
            }
        }

        /// <summary>
        /// 로드 후 호출. VillageData.CurrentBuild* → ObjectPlacementTaskComponent 재구성.
        /// </summary>
        public void RestoreTaskFromData(VillageData v)
        {
            if (v.EntityId < 0)
                return;
            if (v.CurrentBuildTableId <= 0)
                return;

            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = v.CurrentBuildTableId,
                TileX = v.CurrentBuildTileX,
                TileY = v.CurrentBuildTileY,
                StartedAt = v.CurrentBuildStartedAt,
                BuildDurationHours = VillageBuildRoadmap.GetBuildHours(v.CurrentBuildTableId),
                ReservedWoodCost = v.CurrentBuildReservedWood,
                ReservedStoneCost = v.CurrentBuildReservedStone,
            };
            AR.s.Component.AddComponent(v.EntityId, task);

            Debug.Log($"[BuildQueue] v{v.VillageId} 로드 후 태스크 복원: TableId={task.TargetTableId}, tile=({task.TileX},{task.TileY}), 시작={task.StartedAt:F1}h");
        }
    }
}
