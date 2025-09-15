using UnityEngine;
using UnityEditor;

public class ArpgMenu
{
    [MenuItem("ARPG/Download Table", false, 1)]
    private static void CreateTile()
    {

    }

    [MenuItem("ARPG/Create Theme Tile Set", false, 2)]
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
}
