using UnityEngine;
using UnityEditor;
using ARPG.Map;

[CustomEditor(typeof(MapFileSaver))]
public class MapFileSaverEditor : Editor
{
    private string _saveFileName = "";
    private string _loadFileName = "";
    
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        MapFileSaver mapFileSaver = (MapFileSaver)target;
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Map File Operations", EditorStyles.boldLabel);
        
        // 저장 섹션
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Save Map Data", EditorStyles.boldLabel);
        
        _saveFileName = EditorGUILayout.TextField("Save File Name:", _saveFileName);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Current Map", GUILayout.Height(30)))
        {
            string fileName = string.IsNullOrEmpty(_saveFileName) ? null : _saveFileName;
            bool success = mapFileSaver.SaveMapData(fileName);
            
            if (success)
            {
                string usedFileName = string.IsNullOrEmpty(fileName) ? "default" : fileName;
                EditorUtility.DisplayDialog("Success", $"Map saved successfully as '{usedFileName}.dat'", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Failed to save map data. Check console for details.", "OK");
            }
        }
        
        if (GUILayout.Button("Open Save Folder", GUILayout.Height(30)))
        {
            string saveFolderPath = System.IO.Path.Combine(Application.persistentDataPath, "SavedMaps");
            if (System.IO.Directory.Exists(saveFolderPath))
            {
                EditorUtility.RevealInFinder(saveFolderPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Folder Not Found", "Save folder doesn't exist yet. Save a map first.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(5);
        
        // 로드 섹션
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Load Map Data", EditorStyles.boldLabel);
        
        _loadFileName = EditorGUILayout.TextField("Load File Name:", _loadFileName);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Map Data", GUILayout.Height(30)))
        {
            string fileName = string.IsNullOrEmpty(_loadFileName) ? null : _loadFileName;
            var mapData = mapFileSaver.LoadMapData(fileName);
            
            if (mapData != null)
            {
                bool success = mapFileSaver.ApplyMapData(mapData);
                if (success)
                {
                    string usedFileName = string.IsNullOrEmpty(fileName) ? "default" : fileName;
                    EditorUtility.DisplayDialog("Success", $"Map '{usedFileName}.dat' loaded and applied successfully!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Map data loaded but failed to apply. Check console for details.", "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Failed to load map data. Check if file exists and console for details.", "OK");
            }
        }
        
        if (GUILayout.Button("Load to Custom Position", GUILayout.Height(30)))
        {
            if (!string.IsNullOrEmpty(_loadFileName))
            {
                var mapData = mapFileSaver.LoadMapData(_loadFileName);
                if (mapData != null)
                {
                    // 사용자가 좌표를 입력할 수 있는 팝업 창을 만들 수도 있지만, 
                    // 간단하게 (0,0) 위치에 로드하는 예제
                    bool success = mapFileSaver.ApplyMapData(mapData, 0, 0);
                    if (success)
                    {
                        EditorUtility.DisplayDialog("Success", $"Map '{_loadFileName}.dat' loaded at position (0,0)!", "OK");
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please enter a file name to load.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(5);
        
        // 정보 표시
        var tilemapField = serializedObject.FindProperty("_tilemap");
        if (tilemapField.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("No Tilemap assigned. Please assign a Tilemap in the inspector.", MessageType.Warning);
        }
        else
        {
            // 현재 타일맵 정보 표시
            var tilemap = tilemapField.objectReferenceValue as UnityEngine.Tilemaps.Tilemap;
            if (tilemap != null)
            {
                var bounds = tilemap.cellBounds;
                EditorGUILayout.HelpBox($"Current Tilemap Bounds: Position({bounds.xMin}, {bounds.yMin}), Size({bounds.size.x} x {bounds.size.y})", MessageType.Info);
            }
        }
    }
}