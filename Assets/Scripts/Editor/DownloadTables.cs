using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

using ARPG.Tables;
using Newtonsoft.Json;
using System.Collections;

namespace ARPG.Editor
{
    public enum SaveType
    {
        Binary,
        String,
    }

    public class DownloadTables
    {
        private static Dictionary<Type, object> _tableDic;

        [MenuItem("ARPG/Download Table", false, 1)]
        private static async void DownloadTable()
        {
            _tableDic = new();

            await DownloadTable<CreatureTable>("0&range=A:D", 1, SaveType.String);

            await DownloadTable<AiTable>("947794841&range=A:F", 1, SaveType.String);

            await DownloadTable<MonsterTable>("483012127&range=A:H", 1, SaveType.String);

            await DownloadTable<NpcTable>("1460299278&range=A:H", 1, SaveType.String);

            await DownloadTable<StatTable>("318209064&range=A:W", 1, SaveType.String);

            await DownloadTable<ItemTable>("2064107837&range=A:I", 1, SaveType.String);
            
            await DownloadTable<EquipmentTable>("853198133&range=A:H", 1, SaveType.String);

            await DownloadTable<EquipmentStatTable>("488047668&range=A:Q", 1, SaveType.String);
            
            await DownloadTable<DropTable>("1241586373&range=A:J", 1, SaveType.String);

            await DownloadTable<DropCurrencyTable>("2071520432&range=A:V", 1, SaveType.String);

            await DownloadTable<DropEquipmentTable>("1267382287&range=A:V", 1, SaveType.String);

            await DownloadTable<SkillTable>("1267382287&range=A:O", 1, SaveType.String);

            foreach (var tableType in _tableDic.Keys)
            {
                var tableList = (IList)_tableDic[tableType];
                if (tableList == null)
                {
                    Debug.LogError($"[DownloadTables] DownloadTable() - tableList({tableType}) is null");
                    continue;
                }

                SaveTable(tableType.Name, _tableDic[tableType], SaveType.String);
            }

            AssetDatabase.Refresh();
            
            Debug.Log("download Completed...");
        }

        private static async Task<bool> DownloadTable<T>(string inSheet, int inStartLine, SaveType inSaveType) where T : TableBase, new()
        {
            string text = await DownloadTableData($"https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/export?format=tsv&gid={inSheet}");
            var lines = Regex.Split(text, @"\r\n|\n\r|\n|\r");
            if (lines.Length <= 1)
                return false;

            int dataStartLine = inStartLine;

            List<T> tableList = new();
            for (var i = dataStartLine; i < lines.Length; i++)
            {
                CreateTable<T>(lines[i], tableList);
            }

            Type tableType = typeof(T);
            if (_tableDic.ContainsKey(tableType) == false)
            {
                _tableDic.Add(tableType, tableList);
            }

            return true;
        }

        private static bool CreateTable<T>(string inTableData, List<T> inList) where T : TableBase, new()
        {
            var values = inTableData.Split('\t');

            if (values.Length < 1)
                return false;

            T table = new T();
            
            try
            {
                table.Id = int.Parse(values[0]);

                Debug.Log($"[DownloadTables] CreateTable - Creating table, Table({table}), Id({table.Id})");

                if (table is MonsterTable monsterTable)
                {
                    ParseMonsterTable(monsterTable, values);
                }
                else if (table is NpcTable npcTable)
                {
                    ParseNpcTable(npcTable, values);
                }
                else if (table is CreatureTable creatureTable)
                {
                    ParseCreatureTable(creatureTable, values);
                }
                else if (table is StatTable statTable)
                {
                    ParseStatTable(statTable, values);
                }
                else if (table is ItemTable itemTable)
                {
                    ParseItemTable(itemTable, values);
                }
                else if (table is EquipmentTable equipmentTable)
                {
                    ParseEquipmentTable(equipmentTable, values);
                }
                else if (table is EquipmentStatTable equipmentStatTable)
                {
                    ParseEquipmentStatTable(equipmentStatTable, values);
                }
                else if (table is DropTable dropTable)
                {
                    ParseDropTable(dropTable, values);
                }
                else if (table is DropCurrencyTable dropCurrencyTable)
                {
                    ParseDropCurrencyTable(dropCurrencyTable, values);
                }
                else if (table is DropEquipmentTable dropEquipmentTable)
                {
                    ParseDropEquipmentTable(dropEquipmentTable, values);
                }
                else if (table is SkillTable skillTable)
                {
                    ParseSkillTable(skillTable, values);
                }
                else if (table is AiTable aiTable)
                {
                    ParseAiTable(aiTable, values);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DownloadTables] CreateTable - Parsing error: {ex.Message}");
                return false;
            }

            inList.Add(table);
            return true;
        }

        private static void ParseCreatureTable(CreatureTable table, string[] values)
        {
            if (values.Length < 3)
            {
                Debug.LogError($"[ParseCreatureTable] Invalid data length. Expected at least 3, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.StatId = int.Parse(values[2]);
            table.PrefabName = values[3];
        }

        private static void ParseNpcTable(NpcTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseNpcTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.StatId = int.Parse(values[2]);
            table.PrefabName = values[3];
            table.AiTableId = int.Parse(values[4]);
            table.DropId = int.Parse(values[5]);
            table.DropRateBonus = int.Parse(values[6]);
            table.DropRarityBonus = int.Parse(values[7]);
        }

        private static void ParseStatTable(StatTable table, string[] values)
        {
            if (values.Length < 22)
            {
                Debug.LogError($"[ParseStatTable] Invalid data length. Expected at least 20, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[0] 은 Id 이다.
            // values[1], values[2] 는 웹에서만 사용한다.

            table.Str = int.Parse(values[3]);
            table.Dex = int.Parse(values[4]);
            table.Int = int.Parse(values[5]);
            table.MaxHp = int.Parse(values[6]);
            table.MaxMp = int.Parse(values[7]);
            table.HpGeneration = int.Parse(values[8]);
            table.MpGeneration = int.Parse(values[9]);
            table.AttackMin = int.Parse(values[10]);
            table.AttackMax = int.Parse(values[11]);
            table.CriRate = int.Parse(values[12]);
            table.CriDamage = int.Parse(values[13]);
            table.MoveSpeed = int.Parse(values[14]);
            table.AttackSpeed = int.Parse(values[15]);
            table.CastSpeed = int.Parse(values[16]);
            table.Defense = int.Parse(values[17]);
            table.FireResist = int.Parse(values[18]);
            table.IceResist = int.Parse(values[19]);
            table.LightningResist = int.Parse(values[20]);
            table.PoisonResist = int.Parse(values[21]);
            table.Luck = int.Parse(values[22]);
        }

        private static void ParseMonsterTable(MonsterTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseMonsterTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.StatId = int.Parse(values[2]);
            table.PrefabName = values[3];
            table.AiTableId = int.Parse(values[4]);
            table.DropId = int.Parse(values[5]);
            table.DropRateBonus = int.Parse(values[6]);
            table.DropRarityBonus = int.Parse(values[7]);
        }

        private static void ParseItemTable(ItemTable table, string[] values)
        {
            if (values.Length < 9)
            {
                Debug.LogError($"[ParseItemTable] Invalid data length. Expected at least 9, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);
            table.Name = values[2];
            table.ItemType = (GlobalEnum.ItemType)Enum.Parse(typeof(GlobalEnum.ItemType), values[3]);
            table.Stackable = values[4].Trim().ToUpper() == "TRUE";
            table.Description = values[5];
            table.DropRate = int.Parse(values[6]);
            table.EquipmentId = int.Parse(values[7]);
            table.SpriteName = values[8];
        }

        private static void ParseEquipmentTable(EquipmentTable table, string[] values)
        {
            if (values.Length < 8)
            {
                Debug.LogError($"[ParseEquipmentTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[1] 은 웹에서만 사용한다.
            table.EquipType = (GlobalEnum.EquipSlotType)Enum.Parse(typeof(GlobalEnum.EquipSlotType), values[2]);
            table.AttackSpeed = float.Parse(values[3]);
            table.Critical = int.Parse(values[4]);
            table.DamageMin = int.Parse(values[5]);
            table.DamageMax = int.Parse(values[6]);
            table.EquipmentStatId = int.Parse(values[7]);
        }

        private static void ParseEquipmentStatTable(EquipmentStatTable table, string[] values)
        {
            if (values.Length < 17)
            {
                Debug.LogError($"[ParseEquipmentStatTable] Invalid data length. Expected at least 17, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Prefix = new List<Stat>();
            table.Postfix = new List<Stat>();

            // Prefix 시작 인덱스
            int prefixStartIndex = 1;
            for (int i = 0; i < 4; i++)
            {
                int index = prefixStartIndex + (i * 2);
                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = ushort.Parse(values[index + 1]);
                table.Prefix.Add(stat);
            }

            // Postfix 시작 인덱스 (Prefix 4개 스탯 이후)
            int postfixStartIndex = prefixStartIndex + 8;
            for (int i = 0; i < 4; i++)
            {
                int index = postfixStartIndex + (i * 2);
                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = ushort.Parse(values[index + 1]);
                table.Postfix.Add(stat);
            }
        }

        private static void ParseDropTable(DropTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseDropTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);
            table.NothingRate = int.Parse(values[2]);
            table.CurrencyRate = int.Parse(values[3]);
            table.CurrencyId = int.Parse(values[4]);
            table.EquipmentRate = int.Parse(values[5]);
            table.EquipmentId = int.Parse(values[6]);
        }

        private static void ParseDropCurrencyTable(DropCurrencyTable table, string[] values)
        {
            if (values.Length < 22)
            {
                Debug.LogError($"[ParseDropCurrencyTable] Invalid data length. Expected at least 22, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);

            table.DropList = new List<DropInfo>();

            // DropInfo 시작 인덱스
            int dropInfoStartIndex = 2;
            for (int i = 0; i < 10; i++)
            {
                int index = dropInfoStartIndex + (i * 2);
                int id = int.Parse(values[index]);
                if (id == 0)
                    continue;

                DropInfo dropInfo = new DropInfo();
                dropInfo.Id = id;
                dropInfo.Rate = int.Parse(values[index + 1]);
                table.DropList.Add(dropInfo);
            }
        }

        private static void ParseDropEquipmentTable(DropEquipmentTable table, string[] values)
        {
            if (values.Length < 22)
            {
                Debug.LogError($"[ParseDropEquipmentTable] Invalid data length. Expected at least 22, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);

            table.DropList = new List<DropInfo>();

            // DropInfo 시작 인덱스
            int dropInfoStartIndex = 2;
            for (int i = 0; i < 10; i++)
            {
                int index = dropInfoStartIndex + (i * 2);
                int id = int.Parse(values[index]);
                if (id == 0)
                    continue;

                DropInfo dropInfo = new DropInfo();
                dropInfo.Id = id;
                dropInfo.Rate = int.Parse(values[index + 1]);
                table.DropList.Add(dropInfo);
            }
        }

        private static void ParseSkillTable(SkillTable table, string[] values)
        {
            if (values.Length < 14)
            {
                Debug.LogError($"[ParseSkillTable] Invalid data length. Expected at least 14, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Desctiption = values[2];
            table.SkillType = (GlobalEnum.SkillType)Enum.Parse(typeof(GlobalEnum.SkillType), values[3]);
            table.SubType = (GlobalEnum.SkillSubType)Enum.Parse(typeof(GlobalEnum.SkillSubType), values[4]);
            table.SkillRangeMin = float.Parse(values[5]);
            table.SkillRangeMax = float.Parse(values[6]);
            table.Cooltime = float.Parse(values[7]);
            table.Mana = int.Parse(values[8]);
            table.Damage = int.Parse(values[9]);
            table.Duration = int.Parse(values[10]);
            table.SkillTargetType = (GlobalEnum.SkillTargetType)Enum.Parse(typeof(GlobalEnum.SkillTargetType), values[11]);
            table.SkillTargetRange1 = float.Parse(values[12]);
            table.SkillTargetRange2 = float.Parse(values[13]);
            table.AnimationName = values[14];
        }

        private static void ParseAiTable(AiTable table, string[] values)
        {
            if (values.Length < 5)
            {
                Debug.LogError($"[ParseAiTable] Invalid data length. Expected at least 5, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.AiType = (GlobalEnum.AiType)Enum.Parse(typeof(GlobalEnum.AiType), values[2]);
            table.SkillId1 = int.Parse(values[3]);
            table.SkillId2 = int.Parse(values[4]);
            table.SkillId3 = int.Parse(values[5]);
        }

        private static async Task<string> DownloadTableData(string inURL)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(inURL))
            {
                var ao = req.SendWebRequest();

                while (!ao.isDone)
                {
                    await Task.Yield();
                }

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.LogError("Error: " + req.error);
                    return null;
                }

                return req.downloadHandler.text;
            }
        }

        private static bool SaveTable(string inName, object inData, SaveType inSaveType)
        {
            string fileName = $"{inName}.bytes";

            string result = JsonConvert.SerializeObject(inData);
            
            string enData = string.Empty, filePath = string.Empty;
            if (inSaveType == SaveType.String)
            {
                enData = result;
                filePath = Path.Combine(Application.dataPath, Data.DataManager.TablePath, fileName);
            }
            else
            {
                enData = Encrypt(result);
                filePath = Path.Combine(Application.dataPath, Data.DataManager.TablePath, fileName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.Write(enData);
            }

            return true;
        }

        public static string Encrypt(string data)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
            RijndaelManaged rm = CreateRijndaelManaged();
            ICryptoTransform ct = rm.CreateEncryptor();
            byte[] results = ct.TransformFinalBlock(bytes, 0, bytes.Length);

            return System.Convert.ToBase64String(results, 0, results.Length);
        }

        public static string Decrypt(string data)
        {
            byte[] bytes = System.Convert.FromBase64String(data);
            RijndaelManaged rm = CreateRijndaelManaged();
            ICryptoTransform ct = rm.CreateDecryptor();
            byte[] resultArray = ct.TransformFinalBlock(bytes, 0, bytes.Length);

            return System.Text.Encoding.UTF8.GetString(resultArray);
        }

        private static readonly string _privateKey = "1718hy9dsf0jsdefjzs0pa9ids78ehgf81h32re";
        private static RijndaelManaged CreateRijndaelManaged()
        {
            byte[] keyArray = System.Text.Encoding.UTF8.GetBytes(_privateKey);
            RijndaelManaged result = new RijndaelManaged();

            byte[] newKeysArray = new byte[16];
            System.Array.Copy(keyArray, 0, newKeysArray, 0, 16);

            result.Key = newKeysArray;
            result.Mode = CipherMode.ECB;
            result.Padding = PaddingMode.PKCS7;
            return result;
        }
    }
}
