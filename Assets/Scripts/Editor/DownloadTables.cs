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

namespace ARPG.Editor
{
    public enum SaveType
    {
        Binary,
        String,
    }

    public class DownloadTables
    {
        [MenuItem("ARPG/Download Table", false, 1)]
        private static async void DownloadTable()
        {
            await DownloadCreatureTable("0", SaveType.String);

            Debug.Log("download Completed...");
        }



        private static async Task<bool> DownloadCreatureTable(string inSheetId, SaveType inSaveType)
        {
            string text = await DownloadTableData($"https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/export?format=tsv&gid={inSheetId}&range=A:V");
            var lines = Regex.Split(text, @"\r\n|\n\r|\n|\r");
            if (lines.Length <= 1)
                return false;

            int dataStartLine = 1;

            List<CreatureTable> creatureTableList = new();
            for (var i = dataStartLine; i < lines.Length; i++)
            {
                var line = lines[i];
                var values = line.Split('\t');

                if (values.Length < 8)
                    continue;

                CreatureTable careTable = new CreatureTable();
                try
                {
                    careTable.Id = int.Parse(values[0]);
                    careTable.Name = values[1];
                    careTable.Str = int.Parse(values[2]);
                    careTable.Dex = int.Parse(values[3]);
                    careTable.Int = int.Parse(values[4]);
                    careTable.MaxHp = int.Parse(values[5]);
                    careTable.MaxMp = int.Parse(values[6]);
                    careTable.HpGeneration = int.Parse(values[7]);
                    careTable.MpGeneration = int.Parse(values[8]);
                    careTable.AttackMin = int.Parse(values[9]);
                    careTable.AttackMax = int.Parse(values[10]);
                    careTable.CriRate = int.Parse(values[11]);
                    careTable.CriDamage = int.Parse(values[12]);
                    careTable.MoveSpeed = int.Parse(values[13]);
                    careTable.AttackSpeed = int.Parse(values[14]);
                    careTable.CastSpeed = int.Parse(values[15]);
                    careTable.Defense = int.Parse(values[16]);
                    careTable.FireResist = int.Parse(values[17]);
                    careTable.IceResist = int.Parse(values[18]);
                    careTable.LightningResist = int.Parse(values[19]);
                    careTable.PoisonResist = int.Parse(values[20]);
                    careTable.Luck = int.Parse(values[21]);
                }
                catch
                {
                    Debug.LogError("[DownloadTables] DownloadCreatureTable - Parsing error at line " + (i + 1));
                    continue;
                }

                creatureTableList.Add(careTable);
            }

            string result = JsonConvert.SerializeObject(creatureTableList.ToArray());

            return SaveTable("Creature", result, inSaveType);
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

        private static bool SaveTable(string inName, string inData, SaveType inSaveType)
        {
            string fileName = $"{inName}.bytes";

            string enData = string.Empty, filePath = string.Empty;
            if (inSaveType == SaveType.String)
            {
                enData = inData;
                // filePath = Path.Combine(Application.dataPath, $"[ServerTableData]", fileName);
                filePath = Path.Combine(Application.dataPath, $"[TableData]", fileName);
            }
            else
            {
                enData = Encrypt(inData);
                filePath = Path.Combine(Application.dataPath, $"[TableData]", fileName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.Write(enData);
            }

            AssetDatabase.Refresh();

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
