using UnityEngine;
using UnityEditor;
using ARPG.Map;

[CustomEditor(typeof(MapFileSaver))]
public class MapFileSaverEditor : Editor
{
    private SerializedProperty _saveFileNameProp;
    private SerializedProperty _loadFileNameProp;
    private SerializedProperty _themeTileSetProp;

    private void OnEnable()
    {
        _saveFileNameProp = serializedObject.FindProperty("_saveFileName");
        _loadFileNameProp = serializedObject.FindProperty("_loadFileName");
        _themeTileSetProp = serializedObject.FindProperty("_themeTileSet");
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        
        MapFileSaver mapFileSaver = (MapFileSaver)target;
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Map File Operations", EditorStyles.boldLabel);
        
        // 저장 섹션
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Save Map Data", EditorStyles.boldLabel);
        
        EditorGUILayout.PropertyField(_saveFileNameProp, new GUIContent("Save File Name:"));
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Current Map", GUILayout.Height(30)))
        {
            string fileName = string.IsNullOrEmpty(_saveFileNameProp.stringValue) ? null : _saveFileNameProp.stringValue;
            bool success = mapFileSaver.SaveMapData(fileName);
            
            if (success)
            {
                string usedFileName = string.IsNullOrEmpty(fileName) ? "default" : fileName;

                // 에셋 데이터베이스 새로고침하여 유니티에서 파일 변경사항을 즉시 인식
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Success", $"Map saved successfully as '{usedFileName}.bytes'", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Failed to save map data. Check console for details.", "OK");
            }
        }
        
        if (GUILayout.Button("Open Save Folder", GUILayout.Height(30)))
        {
            string saveFolderPath = System.IO.Path.Combine(Application.dataPath, "_BinaryData", "TilemapData");
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
        
        EditorGUILayout.PropertyField(_loadFileNameProp, new GUIContent("Load File Name:"));
        EditorGUILayout.EndVertical();
        
        if (GUILayout.Button("Browse and Load Map", GUILayout.Height(30)))
        {
            string saveFolderPath = System.IO.Path.Combine(Application.dataPath, "_BinaryData", "TilemapData");

            // 폴더가 존재하지 않으면 생성
            if (!System.IO.Directory.Exists(saveFolderPath))
            {
                System.IO.Directory.CreateDirectory(saveFolderPath);
            }

            string selectedPath = EditorUtility.OpenFilePanel("Select Map File", saveFolderPath, "bytes");

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // 파일명에서 확장자 제거
                string fileName = System.IO.Path.GetFileNameWithoutExtension(selectedPath);

                var mapData = mapFileSaver.LoadMapData(fileName);

                if (mapData != null)
                {
                    bool success = mapFileSaver.ApplyMapData(mapData);
                    if (success)
                    {
                        _saveFileNameProp.stringValue = fileName;
                        Debug.Log($"Map '{fileName}.bytes' loaded and applied successfully!");
                    }
                    else
                    {
                        Debug.LogError("Map data loaded but failed to apply. Check console for details.");
                    }
                }
                else
                {
                    Debug.LogError("Failed to load map data. Check if file exists and console for details.");
                }
            }
        }
        
        EditorGUILayout.Space(5);
        
        // 정보 표시
        var tilemapGroundField = serializedObject.FindProperty("_tilemapGround");
        var tilemapObjectField = serializedObject.FindProperty("_tilemapObject");

        if (tilemapGroundField.objectReferenceValue == null && tilemapObjectField.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("No Tilemap assigned. Please assign at least one Tilemap in the inspector.", MessageType.Warning);
        }
        else
        {
            // Ground 타일맵 정보 표시
            if (tilemapGroundField.objectReferenceValue != null)
            {
                var tilemapGround = tilemapGroundField.objectReferenceValue as UnityEngine.Tilemaps.Tilemap;
                if (tilemapGround != null)
                {
                    var boundsGround = tilemapGround.cellBounds;
                    EditorGUILayout.HelpBox($"Ground Tilemap Bounds: Position({boundsGround.xMin}, {boundsGround.yMin}), Size({boundsGround.size.x} x {boundsGround.size.y})", MessageType.Info);
                }
            }

            // Object 타일맵 정보 표시
            if (tilemapObjectField.objectReferenceValue != null)
            {
                var tilemapObject = tilemapObjectField.objectReferenceValue as UnityEngine.Tilemaps.Tilemap;
                if (tilemapObject != null)
                {
                    var boundsObject = tilemapObject.cellBounds;
                    EditorGUILayout.HelpBox($"Object Tilemap Bounds: Position({boundsObject.xMin}, {boundsObject.yMin}), Size({boundsObject.size.x} x {boundsObject.size.y})", MessageType.Info);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}