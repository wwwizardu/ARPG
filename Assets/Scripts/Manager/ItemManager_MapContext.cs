#nullable enable
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
            var drop = new WorldDropItemData { ItemData = data, PositionX = position.x, PositionZ = position.y };
            drops.Add(drop);
            if (AR.s.Dungeon.IsInside == true) AR.s.Data.MarkDungeonSaveDirty(AR.s.Dungeon.ActiveRecord, DungeonSaveGroup.Drops);
            else AR.s.Data.MarkWorldDropSaveDirty(drop);
        }

        public void RemoveContextDrop(int instanceId)
        {
            var drops = ContextDrops;
            for (int i = drops.Count - 1; i >= 0; i--)
                if (drops[i].ItemData.ItemInstanceId == instanceId)
                {
                    if (AR.s.Dungeon.IsInside == true) AR.s.Data.MarkDungeonSaveDirty(AR.s.Dungeon.ActiveRecord, DungeonSaveGroup.Drops);
                    else AR.s.Data.MarkWorldDropSaveDirty(drops[i]);
                    drops.RemoveAt(i);
                }
        }

        /// <summary>현재 맵 컨텍스트에 이 드랍이 월드 오브젝트로 실체화돼 있는지.</summary>
        public bool IsContextDropSpawned(int instanceId)
        {
            return _itemInstances.ContainsKey(instanceId);
        }

        /// <summary>
        /// 현재 맵 컨텍스트(월드 또는 던전)의 드랍 중 center에서 range 안에 있는 가장 가까운 드랍을 찾는다.
        /// 월드 오브젝트로 실체화된 드랍만 대상이다 (NPC 줍기, NPC_COMBAT_DESIGN §6).
        /// </summary>
        public bool TryFindNearestContextDrop(Vector2 center, float range, out int instanceId, out Vector2 position)
        {
            instanceId = 0;
            position = Vector2.zero;
            float bestSqr = range * range;
            bool found = false;
            var drops = ContextDrops;
            for (int i = 0; i < drops.Count; i++)
            {
                WorldDropItemData drop = drops[i];
                if (drop.ItemData == null || _itemInstances.ContainsKey(drop.ItemData.ItemInstanceId) == false)
                    continue;

                Vector2 dropPosition = new Vector2(drop.PositionX, drop.PositionZ);
                float sqr = (dropPosition - center).sqrMagnitude;
                if (sqr > bestSqr)
                    continue;

                bestSqr = sqr;
                instanceId = drop.ItemData.ItemInstanceId;
                position = dropPosition;
                found = true;
            }
            return found;
        }

        /// <summary>실체화된 컨텍스트 드랍의 ItemData. 없으면 null.</summary>
        public ItemData? GetContextDropItem(int instanceId)
        {
            if (_itemInstances.TryGetValue(instanceId, out ItemObject item) == false || item == null)
                return null;
            return item.ItemData;
        }

        /// <summary>
        /// 플레이어가 아닌 인벤토리(NPC 등)로 드랍을 줍는다 (NPC_COMBAT_DESIGN §6).
        /// 월드에서 제거하는 순서는 플레이어 PickupItem과 같다: 인벤토리 추가 성공 → 컨텍스트 드랍 기록 제거 → 월드 오브젝트 제거.
        /// 드랍 제거는 플레이어 경로처럼 즉시 맵(또는 던전) 파티션에 저장한다. 받는 쪽 인벤토리의 저장은 호출자 소관이다 —
        /// 드랍 제거가 먼저 파일에 남으므로 비정상 종료 시 아이템이 사라질 수는 있어도 바닥과 인벤토리에 복제되지는 않는다.
        /// </summary>
        /// <returns>주웠으면 true. 드랍이 없거나 인벤토리에 자리가 없으면 false</returns>
        public bool PickupContextDropInto(int instanceId, Inventory inventory)
        {
            if (inventory == null)
                return false;
            if (_itemInstances.TryGetValue(instanceId, out ItemObject item) == false || item == null)
                return false;
            if (inventory.AddItem(item.ItemData) < 0)
                return false;

            RemoveContextDrop(instanceId);
            DestroyItem(instanceId);

            _ = AR.s.Data.SaveAsync(AR.s.Dungeon.IsInside ? SaveScope.Dungeons : SaveScope.Map);
            return true;
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
