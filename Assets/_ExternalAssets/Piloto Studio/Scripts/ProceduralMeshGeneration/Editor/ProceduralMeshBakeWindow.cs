using UnityEditor;
using UnityEngine;

public class ProceduralMeshBakeWindow : EditorWindow
{
    private ProceduralMeshGenerator targetGen;
    private string meshName = "BakedMesh";
    private string assetPath = "Assets/";

    public static void Open(ProceduralMeshGenerator generator)
    {
        var window = GetWindow<ProceduralMeshBakeWindow>("Bake Mesh");
        window.targetGen = generator;
        window.meshName = generator.name + "_Mesh";
        window.assetPath = "Assets/";
        window.minSize = new Vector2(350, 100);
    }

    private void OnGUI()
    {
        if (targetGen == null)
        {
            EditorGUILayout.HelpBox("No generator selected.", MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("Mesh Asset Name", EditorStyles.boldLabel);
        meshName = EditorGUILayout.TextField(meshName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Asset Save Path", EditorStyles.boldLabel);
        assetPath = EditorGUILayout.TextField(assetPath);

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake and Replace"))
        {
            BakeMesh();
        }
    }

    private void BakeMesh()
    {
        // Generate mesh if needed
        Mesh mesh = new Mesh();
        targetGen.GenerateMesh(); // Make sure it's up to date
        mesh.vertices = targetGen.GetComponent<ParticleSystemRenderer>().mesh.vertices;
        mesh.triangles = targetGen.GetComponent<ParticleSystemRenderer>().mesh.triangles;
        mesh.uv = targetGen.GetComponent<ParticleSystemRenderer>().mesh.uv;
        mesh.RecalculateNormals();

        string fullPath = System.IO.Path.Combine(assetPath, meshName + ".asset");

        AssetDatabase.CreateAsset(mesh, fullPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Assign baked mesh
        var psr = targetGen.GetComponent<ParticleSystemRenderer>();
        psr.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(fullPath);

        // Remove generator
        Object.DestroyImmediate(targetGen);

        Debug.Log($"✅ Mesh baked and saved to: {fullPath}");
        Close();
    }
}
