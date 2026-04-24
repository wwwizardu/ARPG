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
            return true;
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

                _villages[data.VillageId] = data;
                CreateStorageEntity(data);
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
    }
}
