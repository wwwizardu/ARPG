using UnityEngine;
using UnityEditor;

public class ArpgMenu
{
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

    [MenuItem("ARPG/Create Custom Tile", false, 3)]
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
