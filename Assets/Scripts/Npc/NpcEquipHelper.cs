#nullable enable
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 시작 장비 시드 헬퍼. NpcManager에서 NpcSaveData를 처음 만들 때 호출한다.
    /// 시드 정책: NpcEquipTable의 슬롯별 확률을 최초 생성 시 한 번 판정하고, 성공한 ItemId만
    /// ItemManager.CreateInventoryItemData()로 Prefix/Postfix가 굴려진 인스턴스를 생성한다.
    /// </summary>
    public static class NpcEquipHelper
    {
        /// <summary>
        /// EquipTableId가 가리키는 NpcEquipTable을 읽어 확률에 당첨된 장비를 슬롯에 시드한다.
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

                int equipChance = table.GetEquipChance(slot);
                if (equipChance < 0 || equipChance > 100)
                {
                    Debug.LogWarning($"[NpcEquipHelper] Invalid equip chance, "
                        + $"equipTableId({equipTableId}) slot({slot}) itemId({itemId}) "
                        + $"chance({equipChance})");
                    continue;
                }
                if (equipChance == 0)
                    continue;
                if (equipChance < 100
                    && UnityEngine.Random.Range(0, 100) >= equipChance)
                {
                    continue;
                }

                ItemData? item = AR.s.Item?.CreateInventoryItemData(itemId);
                if (item == null)
                {
                    Debug.LogWarning($"[NpcEquipHelper] CreateInventoryItemData failed, "
                        + $"equipTableId({equipTableId}) slot({slot}) itemId({itemId})");
                    continue;
                }

                if (saveData.EquipItem(slot, item, out _) == false)
                {
                    string equipType = item.Equipment != null
                        ? item.Equipment.EquipType.ToString()
                        : "None";
                    Debug.LogWarning($"[NpcEquipHelper] Equipment is incompatible with slot, "
                        + $"equipTableId({equipTableId}) slot({slot}) itemId({itemId}) "
                        + $"equipType({equipType})");
                    continue;
                }
            }
        }
    }
}
