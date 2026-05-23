#nullable enable
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 시작 장비 시드 헬퍼. NpcManager에서 NpcSaveData를 처음 만들 때 호출한다.
    /// 시드 정책(MEMORY: project_npc_equipment): NpcEquipTable의 슬롯별 ItemId로
    /// ItemManager.CreateInventoryItemData()를 호출해 Prefix/Postfix가 굴려진 인스턴스를 생성한다.
    /// </summary>
    public static class NpcEquipHelper
    {
        /// <summary>
        /// EquipTableId가 가리키는 NpcEquipTable을 읽어 saveData.InventoryEquip 슬롯에 장비를 시드한다.
        /// 이미 슬롯에 데이터가 있으면 덮어쓰지 않는다 (재호출 안전).
        /// </summary>
        public static void SeedInitialEquipment(NpcSaveData saveData, int equipTableId)
        {
            if (saveData == null)
                return;

            if (equipTableId <= 0)
                return;

            NpcEquipTable? table = AR.s.Data?.GetNpcEquipTable(equipTableId);
            if (table == null)
            {
                Debug.LogWarning($"[NpcEquipHelper] NpcEquipTable not found, equipTableId({equipTableId})");
                return;
            }

            int slotMax = (int)GE.EquipSlotType.Max;
            for (int i = 0; i < slotMax; i++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)i;
                if (saveData.InventoryEquip[i] != null)
                    continue;

                int itemId = table.GetItemId(slot);
                if (itemId <= 0)
                    continue;

                ItemData? item = AR.s.Item?.CreateInventoryItemData(itemId);
                if (item == null)
                {
                    Debug.LogWarning($"[NpcEquipHelper] CreateInventoryItemData failed, itemId({itemId}) slot({slot})");
                    continue;
                }

                if (item.Equipment == null)
                {
                    Debug.LogWarning($"[NpcEquipHelper] Item({itemId}) is not equipment — skipping slot({slot})");
                    continue;
                }

                saveData.InventoryEquip[i] = item;
            }
        }
    }
}
