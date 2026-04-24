using System.IO;
using UnityEditor;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// 개발 중 세이브 파일 삭제용 메뉴.
    /// WorldData 삭제: 마을/NPC/건물/드롭 초기화 (PlayerData 유지)
    /// </summary>
    public static class ResetSaveData
    {
        private static string SaveDir => Path.Combine(Application.persistentDataPath, "Saved");

        [MenuItem("ARPG/Reset/Delete WorldData", false, 100)]
        private static void DeleteWorldData()
        {
            string worldPath = Path.Combine(SaveDir, "WorldData.json");
            string backupPath = Path.Combine(SaveDir, "WorldData.json.backup");

            bool hasWorld = File.Exists(worldPath);
            bool hasBackup = File.Exists(backupPath);

            if (hasWorld == false && hasBackup == false)
            {
                Debug.Log($"[ResetSaveData] WorldData 파일 없음 (경로: {SaveDir})");
                return;
            }

            if (EditorUtility.DisplayDialog(
                "Delete WorldData",
                $"WorldData.json{(hasBackup ? " + backup" : "")}을 삭제합니다.\nPlayerData는 유지됩니다.\n\n경로: {SaveDir}",
                "삭제",
                "취소") == false)
            {
                return;
            }

            if (hasWorld) File.Delete(worldPath);
            if (hasBackup) File.Delete(backupPath);

            Debug.Log($"[ResetSaveData] WorldData 삭제 완료 (World: {hasWorld}, Backup: {hasBackup})");
        }

        [MenuItem("ARPG/Reset/Delete All Save Data", false, 101)]
        private static void DeleteAllSaveData()
        {
            if (Directory.Exists(SaveDir) == false)
            {
                Debug.Log($"[ResetSaveData] Save 폴더 없음 (경로: {SaveDir})");
                return;
            }

            if (EditorUtility.DisplayDialog(
                "Delete All Save Data",
                $"모든 세이브 파일(WorldData + PlayerData)을 삭제합니다.\n되돌릴 수 없습니다.\n\n경로: {SaveDir}",
                "전부 삭제",
                "취소") == false)
            {
                return;
            }

            Directory.Delete(SaveDir, true);
            Debug.Log($"[ResetSaveData] 전체 세이브 삭제 완료");
        }

        [MenuItem("ARPG/Reset/Open Save Folder", false, 102)]
        private static void OpenSaveFolder()
        {
            if (Directory.Exists(SaveDir) == false)
            {
                Directory.CreateDirectory(SaveDir);
            }
            EditorUtility.RevealInFinder(SaveDir);
        }
    }
}
