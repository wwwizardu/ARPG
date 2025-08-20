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

public class DownloadTables
{
    [MenuItem("SP/Download Table", false, 1)]
    private static async void DownloadTable()
    {


        Debug.Log("download Completed...");
    }



    private static async Task<bool> DownloadCareTable(string inSheetId, SaveType inSaveType)
    {
        string text = await DownloadTableData($"https://docs.google.com/spreadsheets/d/1P6NB8oWa211jQM6JyFElfxT6M4lb3-hXyU1C4xH8h_s/export?format=tsv&gid={inSheetId}&range=A:H");
        var lines = Regex.Split(text, @"\r\n|\n\r|\n|\r");
        if (lines.Length <= 1)
            return false;

        int dataStartLine = 3;

        List<CreatureTable> careTableList = new();
        for (var i = dataStartLine; i < lines.Length; i++)
        {
            var line = lines[i];
            var values = line.Split('\t');

            if (values.Length < 8)
                continue;

            if (int.TryParse(values[0], out int key) == false || int.TryParse(values[4], out int coolTime) == false ||
                int.TryParse(values[6], out int rewardType) == false || int.TryParse(values[7], out int rewardCount) == false)
                continue;

            CreatureTable careTable = new CreatureTable();
            careTable.Key = key;
            careTableList.Add(careTable);
        }

        string result = JsonConvert.SerializeObject(careTableList.ToArray());

        return SaveTable("Care", result, inSaveType);
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
        // if (inSaveType == SaveType.Server)
        // {
        //     enData = inData;
        //     filePath = Path.Combine(Application.dataPath, $"[ServerTableData]", fileName);
        // }
        // else
        // {
        //     enData = SaveManager.Encrypt(inData);
        //     filePath = Path.Combine(Application.dataPath, $"SP/[Table]", fileName);
        // }

        // ������ �������� ������ ����
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        // �ؽ�Ʈ �����͸� ���Ͽ� ����
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
