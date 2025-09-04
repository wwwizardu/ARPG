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

            await DownloadTable<CreatureTable>("0&range=A:V", 1, SaveType.String);

            await DownloadTable<ItemTable>("2064107837&range=A:D", 1, SaveType.String);
            
            await DownloadTable<EquipmentTable>("853198133&range=A:Q", 1, SaveType.String);

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

                if (table is CreatureTable creatureTable)
                {
                    ParseCreatureTable(creatureTable, values);
                }
                else if (table is ItemTable itemTable)
                {
                    ParseItemTable(itemTable, values);
                }
                else if (table is EquipmentTable equipmentTable)
                {
                    ParseEquipmentTable(equipmentTable, values);
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
            if (values.Length < 22) return;

            table.Name = values[1];
            table.Str = int.Parse(values[2]);
            table.Dex = int.Parse(values[3]);
            table.Int = int.Parse(values[4]);
            table.MaxHp = int.Parse(values[5]);
            table.MaxMp = int.Parse(values[6]);
            table.HpGeneration = int.Parse(values[7]);
            table.MpGeneration = int.Parse(values[8]);
            table.AttackMin = int.Parse(values[9]);
            table.AttackMax = int.Parse(values[10]);
            table.CriRate = int.Parse(values[11]);
            table.CriDamage = int.Parse(values[12]);
            table.MoveSpeed = int.Parse(values[13]);
            table.AttackSpeed = int.Parse(values[14]);
            table.CastSpeed = int.Parse(values[15]);
            table.Defense = int.Parse(values[16]);
            table.FireResist = int.Parse(values[17]);
            table.IceResist = int.Parse(values[18]);
            table.LightningResist = int.Parse(values[19]);
            table.PoisonResist = int.Parse(values[20]);
            table.Luck = int.Parse(values[21]);
        }

        private static void ParseItemTable(ItemTable table, string[] values)
        {
            if (values.Length < 3) return;

            table.Name = values[1];
            table.EquipmentId = int.Parse(values[2]);
            table.SpriteName = values[3];
        }

        private static void ParseEquipmentTable(EquipmentTable table, string[] values)
        {
            if (values.Length < 17) return;

            table.Prefix = new List<Stat>();
            table.Postfix = new List<Stat>();

            for (int i = 0; i < 4; i++)
            {
                int index = (i * 2) + 1;
                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = int.Parse(values[index + 1]);
                table.Prefix.Add(stat);
            }

            for (int i = 0; i < 4; i++)
            {
                int index = 8 + (i * 2) + 1;
                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = int.Parse(values[index+1]);
                table.Postfix.Add(stat);
            }
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
                // filePath = Path.Combine(Application.dataPath, $"[ServerTableData]", fileName);
                filePath = Path.Combine(Application.dataPath, $"[TableData]", fileName);
            }
            else
            {
                enData = Encrypt(result);
                filePath = Path.Combine(Application.dataPath, $"[TableData]", fileName);
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
