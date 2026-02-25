#nullable enable
using System.Collections.Generic;
using ARPG.Map;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageManager : MonoBehaviour
    {
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
                RegisterVillage(i, villageCenter);
                Debug.Log($"[VillageManager] Initial village {i} created at {villageCenter}");
            }
        }

        public void Reset()
        {
            _villages.Clear();
        }

        public void RegisterVillage(int villageId, Vector2 position)
        {
            if (_villages.ContainsKey(villageId))
            {
                Debug.LogWarning($"[VillageManager] Village {villageId} already registered");
                return;
            }

            _villages[villageId] = new VillageData(villageId, position);
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

            if (data.Resources.ContainsKey(type))
            {
                data.Resources[type] += amount;
            }
            else
            {
                data.Resources[type] = amount;
            }
        }

        public bool ConsumeResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            if (data.Resources.TryGetValue(type, out int current) == false)
                return false;

            if (current < amount)
                return false;

            data.Resources[type] = current - amount;
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
                _villages[data.VillageId] = data;
            }

            Debug.Log($"[VillageManager] Loaded {_villages.Count} villages");
        }
    }
}
