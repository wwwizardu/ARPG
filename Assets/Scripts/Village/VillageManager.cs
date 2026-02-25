#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageManager : MonoBehaviour
    {
        private Dictionary<int, VillageData> _villages = new();

        public void Initialize()
        {
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
    }
}
