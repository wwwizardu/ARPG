#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.Data;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 상점용 기본 아이템 초안 생성 (NPC_SHOP_EQUIPMENT_DESIGN.md §3.1, D116).
    /// 로컬 bytes(Item·EquipmentBaseStat·ItemImplicit·Mod·ModTier)만 읽고, 장비마다 장비 종류별 선호 옵션 3개로 변형을 만든다.
    /// 모든 Tier의 가중치가 옵션 사이에 같아 "가중치 높은 옵션"으로는 고를 수 없으므로 종류별 선호 목록을 쓴다.
    /// 결과는 시트 붙여넣기용 TSV(Logs/BasicItemDraft.tsv)이고, 필요하면 로컬 BasicItemTable.bytes도 쓴다.
    /// 정본은 시트다 — 시트 반영 뒤에는 Download Table로 bytes를 갱신한다.
    /// </summary>
    public static class BasicItemDraftGenerator
    {
        public enum ValueRule
        {
            Tier1Min,
            Tier1Mid,
            Tier1Max,
        }

        public const int DraftWeight = 100;
        public const int VariantIdMultiplier = 10;   // Id = ItemId × 10 + 변형 번호(1~9)

        private const string TableDir = "Assets/_BinaryData/TableData/";
        private const string DraftTsvPath = "Logs/BasicItemDraft.tsv";

        // 저항 4종 — 장비마다 ItemId로 돌아가며 하나를 고른다 (초안의 변화용).
        private static readonly int[] ResistModIds = { 1203, 1204, 1205, 1206 };
        private const int ResistPlaceholder = -1;

        public struct DraftRow
        {
            public BasicItemTable Row;
            public string ItemName;
            public string ModName;
        }

        [MenuItem("ARPG/Tables/Basic Item Draft/Write TSV (Tier1 Mid)", false, 500)]
        private static void WriteTsvMid() => Run(ValueRule.Tier1Mid, false);

        [MenuItem("ARPG/Tables/Basic Item Draft/Write TSV + Local Bytes (Tier1 Mid)", false, 501)]
        private static void WriteTsvAndBytesMid() => Run(ValueRule.Tier1Mid, true);

        /// <summary>초안을 만들어 TSV(필요하면 bytes)를 쓰고 요약을 돌려준다.</summary>
        public static string Run(ValueRule rule, bool writeBytes)
        {
            List<DraftRow> rows = Generate(rule, out List<string> warnings);

            Directory.CreateDirectory(Path.GetDirectoryName(DraftTsvPath)!);
            File.WriteAllText(DraftTsvPath, ToTsv(rows), new UTF8Encoding(false));

            if (writeBytes == true)
            {
                List<BasicItemTable> tableRows = new List<BasicItemTable>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    tableRows.Add(rows[i].Row);
                string json = JsonConvert.SerializeObject(tableRows, JsonSettings.Default);
                File.WriteAllText(TableDir + "BasicItemTable.bytes", json, new UTF8Encoding(false));
                AssetDatabase.Refresh();
            }

            StringBuilder summary = new StringBuilder();
            summary.Append($"[BasicItemDraft] rule={rule} rows={rows.Count} tsv={DraftTsvPath} bytes={writeBytes}");
            for (int i = 0; i < warnings.Count; i++)
                summary.Append("\n  WARN ").Append(warnings[i]);
            Debug.Log(summary.ToString());
            return summary.ToString();
        }

        public static List<DraftRow> Generate(ValueRule rule, out List<string> warnings)
        {
            warnings = new List<string>();
            List<ItemTable> items = LoadRows<ItemTable>("ItemTable.bytes");
            List<EquipmentBaseStatTable> baseStats = LoadRows<EquipmentBaseStatTable>("EquipmentBaseStatTable.bytes");
            List<ItemImplicitTable> implicits = LoadRows<ItemImplicitTable>("ItemImplicitTable.bytes");
            List<ModTable> mods = LoadRows<ModTable>("ModTable.bytes");
            List<ModTierTable> tiers = LoadRows<ModTierTable>("ModTierTable.bytes");

            HashSet<int> equipmentEligible = new HashSet<int>();
            for (int i = 0; i < baseStats.Count; i++)
                equipmentEligible.Add(baseStats[i].ItemId);
            for (int i = 0; i < implicits.Count; i++)
                equipmentEligible.Add(implicits[i].ItemId);

            Dictionary<int, ModTable> modById = new Dictionary<int, ModTable>();
            for (int i = 0; i < mods.Count; i++)
                modById[mods[i].Id] = mods[i];

            Dictionary<int, ModTierTable> lowestTierByMod = new Dictionary<int, ModTierTable>();
            for (int i = 0; i < tiers.Count; i++)
            {
                ModTierTable tier = tiers[i];
                if (lowestTierByMod.TryGetValue(tier.ModId, out ModTierTable existing) == false || tier.Tier < existing.Tier)
                    lowestTierByMod[tier.ModId] = tier;
            }

            items.Sort((a, b) => a.Id.CompareTo(b.Id));
            List<DraftRow> result = new List<DraftRow>();
            List<int> picked = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                ItemTable item = items[i];
                if (item.ItemType != GE.ItemType.Equipment)
                    continue;
                if (equipmentEligible.Contains(item.Id) == false)
                {
                    warnings.Add($"ItemId {item.Id} {item.Name}: Base·Implicit 데이터가 없어 제외");
                    continue;
                }

                GE.EquipmentTypeMask equipType = Utils.CategoryToEquipmentTypeMask(item.Category);
                if (Utils.IsSingleEquipmentTypeMask(equipType) == false)
                {
                    warnings.Add($"ItemId {item.Id} {item.Name}: 장비 종류 미정 (Category={item.Category})");
                    continue;
                }

                picked.Clear();
                int[] preference = GetPreference(equipType);
                for (int p = 0; p < preference.Length; p++)
                {
                    int modId = preference[p] == ResistPlaceholder
                        ? ResistModIds[item.Id % ResistModIds.Length]
                        : preference[p];
                    if (IsUsable(modId, equipType, modById, lowestTierByMod) == true && picked.Contains(modId) == false)
                        picked.Add(modId);
                }

                // 선호 옵션이 모자라면 허용 옵션을 Id 순으로 채워 최소 2개를 맞춘다.
                for (int m = 0; m < mods.Count && picked.Count < 2; m++)
                {
                    int modId = mods[m].Id;
                    if (IsUsable(modId, equipType, modById, lowestTierByMod) == true && picked.Contains(modId) == false)
                        picked.Add(modId);
                }
                if (picked.Count < 2)
                    warnings.Add($"ItemId {item.Id} {item.Name}: 변형 {picked.Count}개 (허용 옵션 부족)");

                for (int v = 0; v < picked.Count; v++)
                {
                    ModTable mod = modById[picked[v]];
                    ModTierTable tier = lowestTierByMod[mod.Id];
                    result.Add(new DraftRow
                    {
                        Row = new BasicItemTable
                        {
                            Id = item.Id * VariantIdMultiplier + v + 1,
                            ItemId = item.Id,
                            ModTableId = mod.Id,
                            Value1 = PickValue(rule, tier.Min1, tier.Max1),
                            Value2 = PickValue(rule, tier.Min2, tier.Max2),
                            Weight = DraftWeight,
                        },
                        ItemName = item.Name,
                        ModName = mod.Name,
                    });
                }
            }
            return result;
        }

        public static string ToTsv(List<DraftRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Id\tItemId\tModTableId\tValue1\tValue2\tWeight\tItemName(보조)\tModName(보조)\n");
            for (int i = 0; i < rows.Count; i++)
            {
                BasicItemTable row = rows[i].Row;
                sb.Append(row.Id).Append('\t').Append(row.ItemId).Append('\t').Append(row.ModTableId).Append('\t')
                    .Append(row.Value1).Append('\t').Append(row.Value2).Append('\t').Append(row.Weight).Append('\t')
                    .Append(rows[i].ItemName).Append('\t').Append(rows[i].ModName).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 장비 종류별 선호 옵션 (Id). 전투력 평가(NpcPowerEvaluator)가 세는 옵션을 앞에 둔다.
        /// 무기: 추가 물리·공격 속도·치명타 확률 / 방어구: 최대 체력·방어력 + 종류 특색 / 장신구: 체력 재생·치명타 피해·저항.
        /// </summary>
        private static int[] GetPreference(GE.EquipmentTypeMask equipType)
        {
            switch (equipType)
            {
                case GE.EquipmentTypeMask.Weapon:
                    return new[] { 1100, 1002, 1003 };
                case GE.EquipmentTypeMask.Gloves:
                    return new[] { 1000, 1001, 1207 };
                case GE.EquipmentTypeMask.Boots:
                    return new[] { 1000, 1001, 1004 };
                case GE.EquipmentTypeMask.Helmet:
                case GE.EquipmentTypeMask.Armor:
                case GE.EquipmentTypeMask.Belt:
                case GE.EquipmentTypeMask.Shield:
                    return new[] { 1000, 1001, ResistPlaceholder };
                default:
                    return new[] { 1201, 1207, ResistPlaceholder };
            }
        }

        private static bool IsUsable(int modId, GE.EquipmentTypeMask equipType, Dictionary<int, ModTable> modById,
            Dictionary<int, ModTierTable> lowestTierByMod)
        {
            if (modById.TryGetValue(modId, out ModTable mod) == false)
                return false;
            if (mod.Slot != GE.ModSlot.Prefix && mod.Slot != GE.ModSlot.Postfix)
                return false;
            if (Utils.ContainsEquipmentType(mod.AllowedEquipTypes, equipType) == false)
                return false;
            return lowestTierByMod.ContainsKey(modId);
        }

        private static int PickValue(ValueRule rule, int min, int max)
        {
            switch (rule)
            {
                case ValueRule.Tier1Min:
                    return min;
                case ValueRule.Tier1Max:
                    return max;
                default:
                    return (min + max) / 2;
            }
        }

        private static List<T> LoadRows<T>(string fileName)
        {
            string path = TableDir + fileName;
            if (File.Exists(path) == false)
                throw new FileNotFoundException("Table bytes not found", path);
            List<T>? rows = JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path, Encoding.UTF8), JsonSettings.Default);
            if (rows == null)
                throw new InvalidDataException($"Failed to deserialize table bytes: {path}");
            return rows;
        }
    }
}
