using System.Collections.Generic;
using ARPG.Data;
using UnityEngine;

namespace ARPG.Item
{
    public partial class ItemManager
    {
        private List<WorldDropItemData> ContextDrops => AR.s.Dungeon.IsInside
            ? AR.s.Dungeon.ActiveRecord.Drops : AR.s.Data.WorldDrops;

        public void RecordContextDrop(ItemData data, Vector3 position)
        {
            var drops = ContextDrops;
            for (int i = 0; i < drops.Count; i++)
                if (drops[i].ItemData.ItemInstanceId == data.ItemInstanceId) return;
            drops.Add(new WorldDropItemData { ItemData = data, PositionX = position.x, PositionZ = position.y });
        }

        public void RemoveContextDrop(int instanceId)
        {
            var drops = ContextDrops;
            for (int i = drops.Count - 1; i >= 0; i--)
                if (drops[i].ItemData.ItemInstanceId == instanceId) drops.RemoveAt(i);
        }

        public void UnloadContextDrops()
        {
            var ids = new List<int>(_itemInstances.Keys);
            for (int i = 0; i < ids.Count; i++) DestroyItem(ids[i]);
        }

        public void RestoreContextDrops()
        {
            var drops = ContextDrops;
            for (int i = 0; i < drops.Count; i++)
                if (_itemInstances.ContainsKey(drops[i].ItemData.ItemInstanceId) == false)
                    _ = CreateItemFromData(drops[i].ItemData, new Vector3(drops[i].PositionX, drops[i].PositionZ, -0.01f));
        }
    }
}
