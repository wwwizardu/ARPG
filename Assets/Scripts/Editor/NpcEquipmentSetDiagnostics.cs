#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.Tables;
using ARPG.Utility;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 고정 장비 세트의 테이블 참조, 장비 생성 자격, 슬롯 호환성을 검사한다.
    /// 무기와 스킬의 호환성은 스킬 요구 무기 데이터가 추가된 뒤 별도 검증한다.
    /// </summary>
    public static class NpcEquipmentSetDiagnostics
    {
        private const string NpcTablePath =
            "Assets/_BinaryData/TableData/NpcTable.bytes";
        private const string NpcEquipTablePath =
            "Assets/_BinaryData/TableData/NpcEquipTable.bytes";
        private const string ItemTablePath =
            "Assets/_BinaryData/TableData/ItemTable.bytes";
        private const string EquipmentBaseStatTablePath =
            "Assets/_BinaryData/TableData/EquipmentBaseStatTable.bytes";
        private const string ItemImplicitTablePath =
            "Assets/_BinaryData/TableData/ItemImplicitTable.bytes";

        [MenuItem("ARPG/Npc/Run Equipment Set Diagnostics", false, 420)]
        public static void Run()
        {
            List<string> errors = new List<string>();
            List<string> summaries = new List<string>();

            try
            {
                List<NpcTable> npcs = LoadRows<NpcTable>(NpcTablePath);
                List<NpcEquipTable> equipSets =
                    LoadRows<NpcEquipTable>(NpcEquipTablePath);
                List<ItemTable> items = LoadRows<ItemTable>(ItemTablePath);
                List<EquipmentBaseStatTable> baseStats =
                    LoadRows<EquipmentBaseStatTable>(EquipmentBaseStatTablePath);
                List<ItemImplicitTable> implicits =
                    LoadRows<ItemImplicitTable>(ItemImplicitTablePath);

                Dictionary<int, NpcEquipTable> equipSetById =
                    IndexById(equipSets, "NpcEquipTable", errors);
                Dictionary<int, ItemTable> itemById =
                    IndexById(items, "ItemTable", errors);
                HashSet<int> baseStatItemIds = IndexBaseStatItemIds(baseStats, errors);
                HashSet<int> implicitItemIds = IndexImplicitItemIds(implicits);

                for (int i = 0; i < equipSets.Count; i++)
                {
                    ValidateEquipSet(equipSets[i], itemById, baseStatItemIds,
                        implicitItemIds, errors);
                }

                int referencedSetCount = ValidateNpcEquipmentReferences(npcs,
                    equipSetById, itemById, summaries, errors);

                for (int i = 0; i < summaries.Count; i++)
                    Debug.Log($"[NpcEquipmentSetDiagnostics] {summaries[i]}");

                if (errors.Count == 0)
                {
                    Debug.Log($"[NpcEquipmentSetDiagnostics] PASS: NPC({npcs.Count}), "
                        + $"referenced sets({referencedSetCount}), errors(0)");
                    return;
                }

                for (int i = 0; i < errors.Count; i++)
                    Debug.LogError($"[NpcEquipmentSetDiagnostics] {errors[i]}");

                Debug.LogError($"[NpcEquipmentSetDiagnostics] FAIL: NPC({npcs.Count}), "
                    + $"summaries({summaries.Count}), errors({errors.Count})");
            }
            catch (Exception exception)
            {
                Debug.LogError("[NpcEquipmentSetDiagnostics] Failed to read table bytes: "
                    + exception);
            }
        }

        private static List<T> LoadRows<T>(string path)
        {
            if (File.Exists(path) == false)
                throw new FileNotFoundException("Table bytes not found", path);

            List<T>? rows = JsonConvert.DeserializeObject<List<T>>(
                File.ReadAllText(path, Encoding.UTF8));
            if (rows == null)
                throw new InvalidDataException($"Failed to deserialize table bytes: {path}");
            return rows;
        }

        private static Dictionary<int, T> IndexById<T>(List<T> rows, string tableName,
            List<string> errors) where T : TableBase
        {
            Dictionary<int, T> result = new Dictionary<int, T>();
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (result.ContainsKey(row.Id))
                {
                    errors.Add($"{tableName} has duplicate Id({row.Id})");
                    continue;
                }

                result.Add(row.Id, row);
            }
            return result;
        }

        private static HashSet<int> IndexBaseStatItemIds(
            List<EquipmentBaseStatTable> rows, List<string> errors)
        {
            HashSet<int> result = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (result.Add(rows[i].ItemId) == false)
                    errors.Add("EquipmentBaseStatTable has duplicate ItemId("
                        + $"{rows[i].ItemId})");
            }
            return result;
        }

        private static HashSet<int> IndexImplicitItemIds(List<ItemImplicitTable> rows)
        {
            HashSet<int> result = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
                result.Add(rows[i].ItemId);
            return result;
        }

        private static int ValidateNpcEquipmentReferences(List<NpcTable> npcs,
            Dictionary<int, NpcEquipTable> equipSetById,
            Dictionary<int, ItemTable> itemById, List<string> summaries,
            List<string> errors)
        {
            Dictionary<int, int> equipSetOwnerById = new Dictionary<int, int>();

            for (int i = 0; i < npcs.Count; i++)
            {
                NpcTable npc = npcs[i];
                if (npc.EquipTableId <= 0)
                    continue;

                if (equipSetById.TryGetValue(npc.EquipTableId,
                        out NpcEquipTable equipSet) == false)
                {
                    errors.Add($"NpcTable Id({npc.Id}) references missing "
                        + $"NpcEquipTable Id({npc.EquipTableId})");
                    continue;
                }

                if (equipSetOwnerById.TryGetValue(npc.EquipTableId,
                        out int existingNpcId))
                {
                    errors.Add($"NpcTable Id({npc.Id}) and Id({existingNpcId}) share "
                        + $"NpcEquipTable Id({npc.EquipTableId}); fixed-set phase requires "
                        + "one set per NPC table row");
                }
                else
                {
                    equipSetOwnerById.Add(npc.EquipTableId, npc.Id);
                }

                summaries.Add(BuildSummary(npc, equipSet, itemById));
            }

            return equipSetOwnerById.Count;
        }

        private static void ValidateEquipSet(NpcEquipTable equipSet,
            Dictionary<int, ItemTable> itemById, HashSet<int> baseStatItemIds,
            HashSet<int> implicitItemIds, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(equipSet.Desc))
                errors.Add($"NpcEquipTable Id({equipSet.Id}) has an empty Desc");

            int slotMax = (int)GE.EquipSlotType.Max;
            for (int i = 0; i < slotMax; i++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)i;
                int itemId = equipSet.GetItemId(slot);
                int equipChance = equipSet.GetEquipChance(slot);
                if (equipChance < 0 || equipChance > 100)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) has "
                        + $"invalid Chance({equipChance}); expected 0~100");
                }
                if (itemId == 0)
                {
                    if (equipChance != 0)
                    {
                        errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) has "
                            + $"ItemId(0) but Chance({equipChance})");
                    }
                    continue;
                }
                if (itemId < 0)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) has "
                        + $"invalid negative ItemId({itemId})");
                    continue;
                }

                if (itemById.TryGetValue(itemId, out ItemTable item) == false)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) references "
                        + $"missing ItemTable Id({itemId})");
                    continue;
                }

                if (slot == GE.EquipSlotType.WeaponRight && equipChance != 100)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) right-hand weapon "
                        + $"ItemId({itemId}) must have Chance(100), got({equipChance})");
                }
                if (slot == GE.EquipSlotType.WeaponLeft
                    && item.Category == GE.ItemCategory.Shield
                    && equipChance != 100)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) left-hand shield "
                        + $"ItemId({itemId}) must have Chance(100), got({equipChance})");
                }

                bool hasBaseStat = baseStatItemIds.Contains(itemId);
                bool hasImplicit = implicitItemIds.Contains(itemId);
                if (hasBaseStat == false && hasImplicit == false)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) ItemId({itemId}) "
                        + "cannot create EquipmentData because it has no Base or Implicit data");
                    continue;
                }

                if (TryGetEquipmentType(item.Category,
                        out GE.EquipmentTypeMask equipType) == false)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) ItemId({itemId}) "
                        + $"has non-equipment Category({item.Category})");
                    continue;
                }

                GE.EquipmentTypeMask allowedTypes =
                    EquipHelper.SlotToEquipmentTypeMask(slot);
                if (Utils.ContainsEquipmentType(allowedTypes, equipType) == false)
                {
                    errors.Add($"NpcEquipTable Id({equipSet.Id}) slot({slot}) ItemId({itemId}) "
                        + $"has incompatible EquipType({equipType})");
                }
            }
        }

        private static string BuildSummary(NpcTable npc, NpcEquipTable equipSet,
            Dictionary<int, ItemTable> itemById)
        {
            int filledSlotCount = 0;
            StringBuilder equipment = new StringBuilder();
            int slotMax = (int)GE.EquipSlotType.Max;
            for (int i = 0; i < slotMax; i++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)i;
                int itemId = equipSet.GetItemId(slot);
                if (itemId <= 0)
                    continue;
                int equipChance = equipSet.GetEquipChance(slot);

                if (filledSlotCount > 0)
                    equipment.Append(", ");

                string itemName = itemById.TryGetValue(itemId, out ItemTable item)
                    ? item.Name
                    : "<missing>";
                equipment.Append($"{slot}={itemId}:{itemName}@{equipChance}%");
                filledSlotCount++;
            }

            return $"NpcTable({npc.Id}:{npc.Name}) -> "
                + $"EquipTable({equipSet.Id}:{equipSet.Desc}), "
                + $"filled slots({filledSlotCount}) [{equipment}]";
        }

        private static bool TryGetEquipmentType(GE.ItemCategory category,
            out GE.EquipmentTypeMask equipType)
        {
            switch (category)
            {
                case GE.ItemCategory.Weapon:
                case GE.ItemCategory.Shield:
                case GE.ItemCategory.Helmet:
                case GE.ItemCategory.Armor:
                case GE.ItemCategory.Gloves:
                case GE.ItemCategory.Boots:
                case GE.ItemCategory.Necklace:
                case GE.ItemCategory.Ring:
                case GE.ItemCategory.Belt:
                case GE.ItemCategory.Earring:
                    equipType = Utils.CategoryToEquipmentTypeMask(category);
                    return true;
                default:
                    equipType = GE.EquipmentTypeMask.None;
                    return false;
            }
        }
    }
}
