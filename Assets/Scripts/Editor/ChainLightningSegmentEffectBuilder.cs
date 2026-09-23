#if UNITY_EDITOR
using System;
using System.IO;
using ARPG.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// Builds the pooled two-point VFX used by Chain Lightning. Kept as an explicit
    /// Editor command because prefab/material creation and Addressables registration are
    /// asset-authoring work, not runtime propagation work.
    /// </summary>
    public static class ChainLightningSegmentEffectBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/Game/Effect/Skill/ChainLightningSegment.prefab";
        private const string SourcePrefabPath = "Assets/_ExternalAssets/Cartoon Coffee/2D Deluxe VFX/Prefabs/Bursts/Lightning_Small.prefab";
        private const string Address = "Effects/ChainLightningSegment";
        private const float SourceLength = 10.73f;
        private const float WidthScale = 0.3f;

        [MenuItem("ARPG/Effects/Build Chain Lightning Segment")]
        public static void Build()
        {
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
                throw new InvalidOperationException($"Lightning source prefab is missing: {SourcePrefabPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)!);
            GameObject root = new("ChainLightningSegment");
            try
            {
                root.AddComponent<EffectInstance>();
                SkillPropagationSegmentView segmentView = root.AddComponent<SkillPropagationSegmentView>();
                GameObject effectRoot = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, root.transform);
                effectRoot.name = "Lightning_Small_Oriented";
                effectRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                effectRoot.transform.localScale = Vector3.one * WidthScale;

                SerializedObject serializedView = new(segmentView);
                serializedView.FindProperty("_effectRoot").objectReferenceValue = effectRoot.transform;
                serializedView.FindProperty("_referenceLength").floatValue = SourceLength;
                serializedView.FindProperty("_widthScale").floatValue = WidthScale;
                serializedView.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (success == false)
                    throw new InvalidOperationException($"Failed to save Chain Lightning segment prefab: {PrefabPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            RegisterAddressable(PrefabPath, Address);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ChainLightningSegmentEffectBuilder] Built and registered {Address}.");
        }

        [MenuItem("ARPG/Effects/Validate Chain Lightning Segment")]
        public static void Validate()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<EffectInstance>() == null
                || prefab.GetComponent<SkillPropagationSegmentView>() == null
                || prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
            {
                throw new InvalidOperationException("Chain Lightning segment prefab is incomplete.");
            }

            SerializedObject serializedView = new(prefab.GetComponent<SkillPropagationSegmentView>());
            if (serializedView.FindProperty("_effectRoot").objectReferenceValue == null)
                throw new InvalidOperationException("Chain Lightning segment effect root is not assigned.");

            string[] dependencies = AssetDatabase.GetDependencies(PrefabPath);
            if (Array.IndexOf(dependencies, SourcePrefabPath) < 0)
                throw new InvalidOperationException($"Chain Lightning segment does not reference {SourcePrefabPath}.");

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(PrefabPath));
            if (entry == null || entry.address != Address)
                throw new InvalidOperationException($"Addressables entry is missing: {Address}");

            Debug.Log("[ChainLightningSegmentEffectBuilder] Validation passed.");
        }

        private static void RegisterAddressable(string path, string address)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.DefaultGroup == null)
                throw new InvalidOperationException("Addressables default group is unavailable.");

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException($"Chain Lightning segment prefab GUID missing: {path}");

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = address;
            EditorUtility.SetDirty(settings);
        }
    }
}
#endif
