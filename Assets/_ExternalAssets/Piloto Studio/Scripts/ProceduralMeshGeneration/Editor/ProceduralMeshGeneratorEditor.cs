using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProceduralMeshGenerator))]
public class ProceduralMeshGeneratorEditor : Editor
{
    // Cached properties
    SerializedProperty shapeProp, tilingProp, uvRotation, autoUpdateProp;
    SerializedProperty quadSizeProp, border;
    SerializedProperty innerRadiusProp, outerRadiusProp, angleProp, segmentsProp, ringsProp;
    SerializedProperty cylinderHeightProp, cylinderSegmentsProp, cylinderRingsProp, cylinderRadius, cylinderProfile;
    SerializedProperty sphereSegmentsProp,sphereRingsProp, sphereRadiusProp;


    private void OnEnable()
    {
        shapeProp = serializedObject.FindProperty("shape");
        tilingProp = serializedObject.FindProperty("tiling");
        uvRotation = serializedObject.FindProperty("uvRotation");
        autoUpdateProp = serializedObject.FindProperty("autoUpdate");

        quadSizeProp = serializedObject.FindProperty("quadSize");
        border = serializedObject.FindProperty("border");

        innerRadiusProp = serializedObject.FindProperty("innerRadius");
        outerRadiusProp = serializedObject.FindProperty("outerRadius");
        angleProp = serializedObject.FindProperty("angle");
        segmentsProp = serializedObject.FindProperty("segments");
        ringsProp = serializedObject.FindProperty("rings");

        cylinderHeightProp = serializedObject.FindProperty("cylinderHeight");
        cylinderSegmentsProp = serializedObject.FindProperty("cylinderSegments");
        cylinderRingsProp = serializedObject.FindProperty("cylinderRings");
        cylinderRadius = serializedObject.FindProperty("cylinderRadius");
        cylinderProfile = serializedObject.FindProperty("cylinderProfile");

        sphereSegmentsProp = serializedObject.FindProperty("sphereSegments");
        sphereRingsProp = serializedObject.FindProperty("sphereRings");
        sphereRadiusProp = serializedObject.FindProperty("sphereRadius");

    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var shape = (ProceduralMeshGenerator.ShapeType)shapeProp.enumValueIndex;

        EditorGUILayout.PropertyField(shapeProp);
        EditorGUILayout.PropertyField(tilingProp);
        EditorGUILayout.PropertyField(uvRotation);
        EditorGUILayout.PropertyField(autoUpdateProp);
        EditorGUILayout.Space();


        float min = innerRadiusProp.floatValue;
        float max = outerRadiusProp.floatValue;

        switch (shape)
        {
            case ProceduralMeshGenerator.ShapeType.Quad:
                EditorGUILayout.LabelField("Quad Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(quadSizeProp);
                break;

            case ProceduralMeshGenerator.ShapeType.NineSliceQuad:
                EditorGUILayout.PropertyField(quadSizeProp);
                EditorGUILayout.PropertyField(border);                
                break;

            case ProceduralMeshGenerator.ShapeType.Radial:
                EditorGUILayout.LabelField("Radial Settings", EditorStyles.boldLabel);;
                EditorGUILayout.MinMaxSlider(new GUIContent("Radius Range"), ref min, ref max, 0f, 10f);

                min = Mathf.Max(0.001f, min);
                max = Mathf.Max(min + 0.001f, max);
                innerRadiusProp.floatValue = min;
                outerRadiusProp.floatValue = max;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Inner", GUILayout.Width(50));
                innerRadiusProp.floatValue = EditorGUILayout.FloatField(innerRadiusProp.floatValue);
                EditorGUILayout.LabelField("Outer", GUILayout.Width(50));
                outerRadiusProp.floatValue = EditorGUILayout.FloatField(outerRadiusProp.floatValue);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(angleProp);
                EditorGUILayout.IntSlider(segmentsProp, 3, 256);
                EditorGUILayout.IntSlider(ringsProp, 1, 64);
                break;

            case ProceduralMeshGenerator.ShapeType.Cylinder:
                EditorGUILayout.LabelField("Cylinder Settings", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(cylinderHeightProp);
                EditorGUILayout.IntSlider(cylinderSegmentsProp, 3, 128);
                EditorGUILayout.IntSlider(cylinderRingsProp, 1, 64);
                EditorGUILayout.LabelField("Radius Profile", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(cylinderRadius);
                EditorGUILayout.PropertyField(cylinderProfile);
                break;

            case ProceduralMeshGenerator.ShapeType.Sphere:
                EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(sphereRadiusProp);
                EditorGUILayout.IntSlider(sphereSegmentsProp, 3, 128);
                EditorGUILayout.IntSlider(sphereRingsProp, 2, 128);
                break;

        }

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        if (GUILayout.Button("Bake Mesh to Asset"))
        {
            ProceduralMeshBakeWindow.Open((ProceduralMeshGenerator)target);
        }
        if (GUILayout.Button("Reassign Particle Mesh"))
        {
            ((ProceduralMeshGenerator)target).ReassignParticleMesh();
        }


        serializedObject.ApplyModifiedProperties();
    }
}
