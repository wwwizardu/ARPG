#nullable enable
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        [MenuItem("ARPG/Reset/Reset NPC Talk History", false, 102)]
        private static void ResetNpcTalkHistory()
        {
            if (EditorUtility.DisplayDialog(
                "Reset NPC Talk History",
                "모든 NPC의 TalkCount와 LastTalkGameTime을 초기화합니다.\n" +
                "호감·신뢰·두려움·친밀도는 유지됩니다.",
                "초기화",
                "취소") == false)
            {
                return;
            }

            if (EditorApplication.isPlaying)
            {
                AR? runtime = Object.FindAnyObjectByType<AR>();
                if (runtime == null || runtime.IsInitialized == false)
                {
                    Debug.LogWarning("[ResetSaveData] AR 런타임 초기화가 끝난 뒤 다시 실행하세요.");
                    return;
                }

                int resetCount = runtime.Npc.ResetPlayerTalkHistory();
                runtime.Data.Save();
                Debug.Log($"[ResetSaveData] 런타임 NPC 대화 이력 초기화 완료 ({resetCount}명). 저장을 요청했습니다.");
                return;
            }

            string worldPath = Path.Combine(SaveDir, "WorldData.json");
            string backupPath = Path.Combine(SaveDir, "WorldData.json.backup");
            bool resetWorld = TryResetNpcTalkHistoryFile(worldPath, out int worldCount);
            bool resetBackup = TryResetNpcTalkHistoryFile(backupPath, out int backupCount);

            if (resetWorld == false && resetBackup == false)
            {
                Debug.Log($"[ResetSaveData] 초기화할 WorldData 파일이 없습니다 (경로: {SaveDir})");
                return;
            }

            Debug.Log($"[ResetSaveData] NPC 대화 이력 초기화 완료 " +
                $"(World: {(resetWorld ? worldCount : 0)}명, Backup: {(resetBackup ? backupCount : 0)}명)");
        }

        [MenuItem("ARPG/Reset/Open Save Folder", false, 103)]
        private static void OpenSaveFolder()
        {
            if (Directory.Exists(SaveDir) == false)
            {
                Directory.CreateDirectory(SaveDir);
            }
            EditorUtility.RevealInFinder(SaveDir);
        }

        private static bool TryResetNpcTalkHistoryFile(string path, out int resetCount)
        {
            resetCount = 0;
            if (File.Exists(path) == false)
                return false;

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JArray? relations = root["PlayerNpcRelationList"] as JArray;
                if (relations != null)
                {
                    for (int i = 0; i < relations.Count; i++)
                    {
                        if (relations[i] is not JObject relation)
                            continue;

                        relation["TalkCount"] = 0;
                        relation["LastTalkGameTime"] = -1f;
                        resetCount++;
                    }
                }

                File.WriteAllText(path, root.ToString(Formatting.Indented));
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ResetSaveData] NPC 대화 이력 초기화 실패 ({path}): {ex.Message}");
                return false;
            }
        }
    }
}
