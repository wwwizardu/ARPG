#if UNITY_EDITOR
using System.IO;
using ARPG.Base;
using ARPG.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// Rebuilds the two Fire Ring particle prefabs from the project's existing fire VFX materials.
    /// </summary>
    public static class FireRingEffectBuilder
    {
        private const string BURST_PATH = "Assets/Prefabs/Game/Effect/FireRingBurst.prefab";
        private const string AREA_PATH = "Assets/Prefabs/Game/Effect/AreaEffect/FireRing.prefab";
        private const string RING_MATERIAL_PATH = "Assets/_ExternalAssets/Cartoon Coffee/2D Deluxe VFX/Materials/CartoonCoffee_Fire_Ring_001-Basic-[2.0].mat";
        private const string EMBER_MATERIAL_PATH = "Assets/_ExternalAssets/AllIn1VfxToolkit/Demo & Assets/Demo/Materials/FireSparks2.mat";
        private const string GLOW_MATERIAL_PATH = "Assets/_ExternalAssets/AllIn1VfxToolkit/Demo & Assets/Demo/Materials/ImpactCenterGlow.mat";
        private const string BUILD_REQUEST_PATH = "Temp/FireRingEffectBuild.request";
        private const string BURST_PREVIEW_PATH = "Temp/FireRingBurstPreview.png";
        private const string AREA_PREVIEW_PATH = "Temp/FireRingAreaPreview.png";

        [InitializeOnLoadMethod]
        private static void BuildAfterReloadIfRequested()
        {
            if (File.Exists(BUILD_REQUEST_PATH) == false)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (File.Exists(BUILD_REQUEST_PATH) == false)
                {
                    return;
                }

                Build();
                File.Delete(BUILD_REQUEST_PATH);
            };
        }

        [MenuItem("ARPG/Effects/Build Fire Ring")]
        public static void Build()
        {
            Material ringMaterial = LoadMaterial(RING_MATERIAL_PATH);
            Material emberMaterial = LoadMaterial(EMBER_MATERIAL_PATH);
            Material glowMaterial = LoadMaterial(GLOW_MATERIAL_PATH);

            BuildBurst(ringMaterial, emberMaterial, glowMaterial);
            BuildArea(ringMaterial, emberMaterial, glowMaterial);
            RegisterAddressable(BURST_PATH, "Effects/FireRingBurst");
            RegisterAddressable(AREA_PATH, "Prefabs/AreaEffect/FireRing");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();
            RenderPreview(BURST_PATH, BURST_PREVIEW_PATH, 0.26f, false);
            RenderPreview(AREA_PATH, AREA_PREVIEW_PATH, 0.7f, true);
            Debug.Log("[FireRingEffectBuilder] Fire Ring burst and area prefabs are ready.");
        }

        [MenuItem("ARPG/Effects/Validate Fire Ring")]
        public static void Validate()
        {
            GameObject burst = AssetDatabase.LoadAssetAtPath<GameObject>(BURST_PATH);
            GameObject area = AssetDatabase.LoadAssetAtPath<GameObject>(AREA_PATH);
            if (burst == null || burst.GetComponent<EffectInstance>() == null)
            {
                throw new System.InvalidOperationException("Fire Ring burst prefab or EffectInstance is missing.");
            }

            EntityBase areaEntity = area != null ? area.GetComponent<EntityBase>() : null;
            if (areaEntity == null || areaEntity.Visual == null)
            {
                throw new System.InvalidOperationException("Fire Ring area prefab needs an EntityBase.Visual child.");
            }

            ValidateParticles(burst);
            ValidateParticles(area);
            ValidateAddress(BURST_PATH, "Effects/FireRingBurst");
            ValidateAddress(AREA_PATH, "Prefabs/AreaEffect/FireRing");
            Debug.Log("[FireRingEffectBuilder] Prefab components, particle materials, and Addressables are valid.");
        }

        private static Material LoadMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new System.InvalidOperationException($"Fire Ring material missing: {path}");
            }

            return material;
        }

        private static void BuildBurst(Material ringMaterial, Material emberMaterial, Material glowMaterial)
        {
            GameObject root = new GameObject("FireRingBurst");
            root.AddComponent<EffectInstance>();

            ParticleSystem ring = CreateParticle(root.transform, "ExpandingRing", ringMaterial, 30);
            ConfigureRing(ring, false);

            ParticleSystem glow = CreateParticle(root.transform, "HotCenter", glowMaterial, 29);
            ConfigureGlow(glow);

            ParticleSystem embers = CreateParticle(root.transform, "FlyingEmbers", emberMaterial, 31);
            ConfigureEmbers(embers, false);

            SavePrefab(root, BURST_PATH);
        }

        private static void BuildArea(Material ringMaterial, Material emberMaterial, Material glowMaterial)
        {
            GameObject root = new GameObject("FireRing");
            EntityBase entityBase = root.AddComponent<EntityBase>();
            PooledParticleEffectView pooledView = root.AddComponent<PooledParticleEffectView>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            ParticleSystem ring = CreateParticle(visual.transform, "BurningRim", ringMaterial, 22);
            ConfigureRing(ring, true);

            ParticleSystem embers = CreateParticle(visual.transform, "RisingEmbers", emberMaterial, 24);
            ConfigureEmbers(embers, true);

            ParticleSystem glow = CreateParticle(visual.transform, "GroundGlow", glowMaterial, 21);
            ConfigureAreaGlow(glow);

            SerializedObject entitySerialized = new SerializedObject(entityBase);
            entitySerialized.FindProperty("_visual").objectReferenceValue = visual;
            entitySerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject viewSerialized = new SerializedObject(pooledView);
            SerializedProperty systems = viewSerialized.FindProperty("_systems");
            systems.arraySize = 3;
            systems.GetArrayElementAtIndex(0).objectReferenceValue = ring;
            systems.GetArrayElementAtIndex(1).objectReferenceValue = embers;
            systems.GetArrayElementAtIndex(2).objectReferenceValue = glow;
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, AREA_PATH);
        }

        private static ParticleSystem CreateParticle(Transform parent, string name, Material material, int sortingOrder)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            ParticleSystem particle = child.AddComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            return particle;
        }

        private static void ConfigureRing(ParticleSystem particle, bool isArea)
        {
            ParticleSystem.MainModule main = particle.main;
            main.loop = isArea;
            main.duration = isArea ? 1.6f : 0.55f;
            main.startLifetime = isArea ? 2.0f : 0.55f;
            main.startSpeed = 0f;
            main.startSize = isArea ? 2.0f : 3.6f;
            main.startColor = new Color(1f, 0.9f, 0.65f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = isArea ? 4 : 1;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = isArea ? 0.7f : 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, isArea
                ? new AnimationCurve(new Keyframe(0f, 0.93f), new Keyframe(0.5f, 1.04f), new Keyframe(1f, 0.95f))
                : new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.2f, 0.7f), new Keyframe(1f, 1f)));

            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(CreateRingGradient(isArea));
        }

        private static void ConfigureGlow(ParticleSystem particle)
        {
            ParticleSystem.MainModule main = particle.main;
            main.loop = false;
            main.duration = 0.35f;
            main.startLifetime = 0.38f;
            main.startSpeed = 0f;
            main.startSize = 1.6f;
            main.startColor = new Color(1f, 0.45f, 0.12f, 0.28f);
            main.maxParticles = 1;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1.45f)));

            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
                new Color(1f, 0.65f, 0.15f), new Color(1f, 0.23f, 0.06f), 0.42f, 0f));
        }

        private static void ConfigureEmbers(ParticleSystem particle, bool isArea)
        {
            ParticleSystem.MainModule main = particle.main;
            main.loop = isArea;
            main.duration = isArea ? 1f : 0.45f;
            main.startLifetime = isArea
                ? new ParticleSystem.MinMaxCurve(0.45f, 0.85f)
                : new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = isArea
                ? new ParticleSystem.MinMaxCurve(0.1f, 0.35f)
                : new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
            main.startSize = isArea
                ? new ParticleSystem.MinMaxCurve(0.12f, 0.28f)
                : new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            main.startColor = new Color(1f, 0.55f, 0.2f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = isArea ? 80 : 32;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = isArea ? 26f : 0f;
            if (isArea == false)
            {
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });
            }

            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = isArea ? 0.88f : 0.08f;
            shape.radiusThickness = isArea ? 0.14f : 1f;

            ParticleSystem.VelocityOverLifetimeModule velocity = particle.velocityOverLifetime;
            velocity.enabled = isArea;
            if (isArea)
            {
                velocity.y = new ParticleSystem.MinMaxCurve(0.3f);
            }

            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
                new Color(1f, 0.9f, 0.35f), new Color(1f, 0.25f, 0.06f), 1f, 0f));

            ParticleSystem.TextureSheetAnimationModule sheet = particle.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        }

        private static void ConfigureAreaGlow(ParticleSystem particle)
        {
            ParticleSystem.MainModule main = particle.main;
            main.loop = true;
            main.duration = 1.5f;
            main.startLifetime = 1.8f;
            main.startSpeed = 0f;
            main.startSize = 1.3f;
            main.startColor = new Color(1f, 0.22f, 0.04f, 0.13f);
            main.maxParticles = 4;

            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0.8f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(CreateGradient(
                new Color(1f, 0.35f, 0.1f), new Color(1f, 0.2f, 0.05f), 0.09f, 0f));
        }

        private static Gradient CreateGradient(Color start, Color end, float startAlpha, float endAlpha)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(endAlpha, 1f) });
            return gradient;
        }

        private static Gradient CreateRingGradient(bool isArea)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0f),
                    new GradientColorKey(new Color(1f, 0.48f, 0.15f), 1f)
                },
                isArea
                    ? new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                    : new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 0.7f),
                        new GradientAlphaKey(0f, 1f)
                    });
            return gradient;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (success == false)
            {
                throw new System.InvalidOperationException($"Failed to save Fire Ring prefab: {path}");
            }
        }

        private static void RegisterAddressable(string path, string address)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.DefaultGroup == null)
            {
                throw new System.InvalidOperationException("Addressables default group is unavailable.");
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                throw new System.InvalidOperationException($"Fire Ring prefab GUID missing: {path}");
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = address;
            EditorUtility.SetDirty(settings);
        }

        private static void ValidateParticles(GameObject prefab)
        {
            ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length != 3)
            {
                throw new System.InvalidOperationException($"{prefab.name} needs three particle systems.");
            }

            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystemRenderer renderer = particles[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader.isSupported == false)
                {
                    throw new System.InvalidOperationException($"{prefab.name}/{particles[i].name} has an invalid particle material.");
                }
            }
        }

        private static void ValidateAddress(string path, string address)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
            if (entry == null || entry.address != address)
            {
                throw new System.InvalidOperationException($"Addressables entry is missing: {address}");
            }
        }

        private static void RenderPreview(string prefabPath, string outputPath, float time, bool isArea)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject cameraObject = new GameObject("FireRingPreviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RenderTexture target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                if (isArea)
                {
                    EntityBase entity = instance.GetComponent<EntityBase>();
                    entity.Visual.transform.localScale = new Vector3(2f, 2f, 1f);
                }

                ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    particles[i].Simulate(time, false, true);
                }

                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 3f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.07f, 0.06f, 1f);
                camera.targetTexture = target;
                target.Create();
                camera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, 512f, 512f), 0, 0);
                image.Apply();
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(instance);
            }
        }
    }
}
#endif
