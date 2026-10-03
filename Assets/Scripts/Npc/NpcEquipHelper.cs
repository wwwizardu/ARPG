#nullable enable
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 시작 장비 시드 헬퍼. NpcManager에서 NpcSaveData를 처음 만들 때 호출한다.
    /// 시드 정책: NpcEquipTable의 슬롯별 확률을 최초 생성 시 한 번 판정하고, 성공한 ItemId는 상점용 기본 아이템
    /// (고정 옵션, NPC_SHOP_EQUIPMENT D118)으로 만든다. 변형은 entityId·슬롯 해시로 고른다.
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

                ItemData? item = CreateBasicEquipment(saveData.EntityId, slot, itemId);
                if (item == null)
                {
                    Debug.LogWarning($"[NpcEquipHelper] Basic item creation failed, "
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

        /// <summary>
        /// 슬롯 장비 ItemId의 기본 아이템 변형을 entityId·슬롯 해시로 고른다 (D118). 변형이 없으면 null.
        /// 시작 장비 시드와 기존 세이브 일괄 교체(D121)가 같은 선택을 쓴다.
        /// </summary>
        public static BasicItemTable? PickBasicVariant(int entityId, GE.EquipSlotType slot, int itemId)
        {
            return AR.s.Item.PickBasicItemVariant(itemId, GetBasicVariantRoll(entityId, slot));
        }

        private static ItemData? CreateBasicEquipment(int entityId, GE.EquipSlotType slot, int itemId)
        {
            BasicItemTable? row = PickBasicVariant(entityId, slot, itemId);
            if (row == null)
            {
                Debug.LogWarning($"[NpcEquipHelper] No basic item variant, itemId({itemId}) — Base만 있는 장비로 대신한다");
                return AR.s.Item.CreateBaseEquipment(itemId);
            }
            return AR.s.Item.CreateBasicItem(row.Id);
        }

        /// <summary>[0, 1) 변형 선택 롤. worldSeed와 무관해 같은 NPC·슬롯은 언제 계산해도 같다.</summary>
        private static double GetBasicVariantRoll(int entityId, GE.EquipSlotType slot)
        {
            ulong channel = DerivedSeed.ChannelNpcBasicEquipment
                ^ unchecked((ulong)((int)slot + 1) * 0x9E3779B97F4A7C15UL);
            return new HistoryRng(DerivedSeed.Derive(entityId, channel)).NextDouble();
        }
    }
}
