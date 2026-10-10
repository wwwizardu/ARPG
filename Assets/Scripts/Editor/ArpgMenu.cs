using UnityEngine;
using UnityEditor;
using System.IO;

public class ArpgMenu
{
    [MenuItem("ARPG/Open Save Directory", false, 2)]
    public static void OpenSaveDirectory()
    {
        string directory = Path.Combine(Application.persistentDataPath, "Saved");
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            if (Directory.Exists(directory) == false)
            {
                Debug.LogError($"Save directory does not exist: {directory}");
                return;
            }

            string winPath = directory.Replace("/", "\\");
            Debug.Log($"Attempting to open explorer with path: {winPath}");

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", winPath);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Debug.LogError($"Failed to open explorer: {ex.Message}");
            }
        }
    }

    // 시야·안개 켜기/끄기 (FOG_OF_WAR_DESIGN). 플레이 중에만 쓴다. 끄면 덮개가 사라지고 숨은 이동 엔티티가 모두 보인다.
    [MenuItem("ARPG/Toggle Fog Of War", false, 5)]
    private static void ToggleFogOfWar()
    {
        if (Application.isPlaying == false || ARPG.AR.s.IsInitialized == false)
        {
            Debug.LogWarning("[Fog] 플레이 모드 GameScene에서 실행하세요.");
            return;
        }

        ARPG.Map.FogOfWarManager fog = ARPG.AR.s.Fog;
        fog.SetEnabled(fog.IsEnabled == false);
        Debug.Log($"[Fog] Fog of war {(fog.IsEnabled == true ? "enabled" : "disabled")}.");
    }

    [MenuItem("ARPG/Toggle Fog Of War", true)]
    private static bool ValidateToggleFogOfWar() => Application.isPlaying;
    [MenuItem("ARPG/Create Theme Tile Set", false, 3)]
    private static void CreateThemeTileSet()
    {
        string folderPath = "Assets/Art/Tilemap/TileSet";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Art", "Tilemap");
            AssetDatabase.CreateFolder("Assets/Art/Tilemap", "TileSet");
        }

        string filePath = $"{folderPath}/DefaultTileSet.asset";

        if (System.IO.File.Exists(filePath))
        {
            Debug.LogError($"File already exists: {filePath}");
            return;
        }

        ThemeTileSet asset = ScriptableObject.CreateInstance<ThemeTileSet>();
        AssetDatabase.CreateAsset(asset, filePath);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("ARPG/Create Custom Tile", false, 4)]
    private static void CreateCustomTile()
    {
        string folderPath = "Assets/Art/Tilemap/CustomTiles";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Art", "Tilemap");
            AssetDatabase.CreateFolder("Assets/Art/Tilemap", "CustomTiles");
        }

        string fileName = "CustomTile";
        string uniqueFileName = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{fileName}.asset");

        CustomTile customTile = ScriptableObject.CreateInstance<CustomTile>();
        customTile.SetCustomData(0);

        AssetDatabase.CreateAsset(customTile, uniqueFileName);
        AssetDatabase.SaveAssets();

        Selection.activeObject = customTile;
        EditorGUIUtility.PingObject(customTile);

        Debug.Log($"Custom tile created at: {uniqueFileName}");
    }
}
