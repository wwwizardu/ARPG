#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// Tools/comfyui_generate.py로 생성된 이미지를 정식 위치로 옮기고 Addressable 등록.
///
/// 사용 흐름:
///   1. Tools/comfyui_generate.py 실행 → Assets/Art/Sprites/Items/Generated/{Name}_{1,2,3}.png 39장
///   2. 사용자가 원하지 않는 이미지 직접 삭제 (각 오브젝트당 1장씩 남기는 것을 권장)
///   3. Unity 메뉴 ARPG/Sprites/Import Generated Items 클릭
///   4. 결과 콘솔 로그 확인
///
/// 동작:
///   - Generated/{Name}_N.png → Items/{Name}.png 로 이동 (같은 Name이 여러 장이면 _1 우선, 없으면 _2, _3 순)
///   - Sprite Import 설정 적용 (Pivot Bottom, PixelsPerUnit 등)
///   - Addressable 그룹 (Default Local Group)에 키 "Sprites/Items/{Name}" 로 등록
///   - 이미 등록된 키는 entry만 갱신
/// </summary>
public static class ImportGeneratedSprites
{
    private const string SOURCE_DIR = "Assets/Art/Sprites/Items/Generated";
    private const string TARGET_DIR = "Assets/Art/Sprites/Items";
    private const string ADDRESSABLE_KEY_PREFIX = "Sprites/Items/";

    // Sprite import 설정 (Phase B Campfire 등 기존 자산과 일관성 맞춤)
    private const float PIXELS_PER_UNIT = 100f;
    private const SpriteAlignment PIVOT_ALIGNMENT = SpriteAlignment.BottomCenter;

    [MenuItem("ARPG/Sprites/Import Generated Items", false, 100)]
    public static void Import()
    {
        if (Directory.Exists(SOURCE_DIR) == false)
        {
            Debug.LogError($"[ImportGeneratedSprites] 소스 폴더 없음: {SOURCE_DIR}");
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[ImportGeneratedSprites] AddressableAssetSettings 없음. Addressables 패키지 셋업 확인.");
            return;
        }
        var group = settings.DefaultGroup;
        if (group == null)
        {
            Debug.LogError("[ImportGeneratedSprites] Addressables Default Group 없음.");
            return;
        }

        // {Name} → {Name}_N.png 파일명 사용 가능 후보들
        Dictionary<string, List<string>> candidatesByName = ScanCandidates();
        if (candidatesByName.Count == 0)
        {
            Debug.LogWarning($"[ImportGeneratedSprites] {SOURCE_DIR} 에 옮길 이미지 없음. 먼저 comfyui_generate.py 실행.");
            return;
        }

        StringBuilder report = new StringBuilder();
        int moved = 0;
        int registered = 0;
        int skippedExisting = 0;

        // ===== Phase 1: 이동만 (StartAssetEditing 블록 안에서) =====
        // 주의: StartAssetEditing 블록 안에서는 AssetDatabase가 아직 새 경로를 인덱싱 안 한 상태라
        //       MoveAsset 직후 AssetPathToGUID/CreateOrMoveEntry 호출 시 빈 GUID 반환됨.
        //       따라서 GUID 추출 + Addressable 등록은 Phase 2에서 처리.
        List<(string targetPath, string name)> moveTargets = new();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var pair in candidatesByName)
            {
                string name = pair.Key;
                List<string> sources = pair.Value;
                sources.Sort();  // _1, _2, _3 순

                string sourcePath = sources[0];                 // 가장 작은 인덱스 사용
                string targetPath = $"{TARGET_DIR}/{name}.png";

                if (File.Exists(targetPath))
                {
                    Debug.LogWarning($"  - 이미 존재: {targetPath} → 덮어쓰기 진행");
                    AssetDatabase.DeleteAsset(targetPath);
                }

                string error = AssetDatabase.MoveAsset(sourcePath, targetPath);
                if (string.IsNullOrEmpty(error) == false)
                {
                    Debug.LogError($"  - 이동 실패 {sourcePath} → {targetPath}: {error}");
                    continue;
                }
                moved++;
                report.AppendLine($"  ✓ {Path.GetFileName(sourcePath)} → {Path.GetFileName(targetPath)}");

                ApplySpriteImportSettings(targetPath);
                moveTargets.Add((targetPath, name));

                // 같은 name의 나머지 후보 파일들은 그대로 둠 (사용자가 보존하길 원했을 수 있음)
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // StopAssetEditing이 호출된 이후 Refresh로 AssetDatabase 인덱스 갱신
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ===== Phase 2: Addressable 등록 (이제 GUID 추출 가능) =====
        for (int i = 0; i < moveTargets.Count; i++)
        {
            string targetPath = moveTargets[i].targetPath;
            string name = moveTargets[i].name;

            string guid = AssetDatabase.AssetPathToGUID(targetPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"  - GUID 추출 실패 (Phase 2 후에도): {targetPath}");
                continue;
            }

            string address = $"{ADDRESSABLE_KEY_PREFIX}{name}";
            AddressableAssetEntry? existing = settings.FindAssetEntry(guid);
            if (existing != null && existing.address == address)
            {
                skippedExisting++;
                report.AppendLine($"      Addressable: 이미 등록됨 ({address})");
            }
            else
            {
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = address;
                registered++;
                report.AppendLine($"      Addressable: {address}");
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(settings);

        Debug.Log($"[ImportGeneratedSprites] 완료\n  이동: {moved}, Addressable 신규/갱신: {registered}, 이미 등록: {skippedExisting}\n{report}");

        // 안내 — Fast Mode(Use Asset Database)면 빌드 불필요. Existing Build/Packed Mode 또는 배포 빌드 시점에만 필요.
        if (moved > 0)
            Debug.Log("[ImportGeneratedSprites] Play Mode가 Existing Build / Packed Mode 또는 배포 빌드 직전이라면 'ARPG/Sprites/Build Addressables' 실행. Fast Mode 개발 중이면 불필요.");
    }

    [MenuItem("ARPG/Sprites/Build Addressables", false, 101)]
    public static void BuildAddressables()
    {
        AddressableAssetSettings.BuildPlayerContent();
        Debug.Log("[ImportGeneratedSprites] Addressables Build 완료");
    }

    /// <summary>
    /// Generated/{Name}_N.{ext} 패턴 스캔 → {Name}: [path1, path2, ...] 매핑.
    /// </summary>
    private static Dictionary<string, List<string>> ScanCandidates()
    {
        Dictionary<string, List<string>> result = new();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SOURCE_DIR });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            string fileNoExt = Path.GetFileNameWithoutExtension(path);

            // {Name}_{N} 패턴 분리. _가 없으면 그 자체를 Name으로.
            int idx = fileNoExt.LastIndexOf('_');
            string name;
            if (idx > 0 && idx < fileNoExt.Length - 1 && IsAllDigits(fileNoExt, idx + 1))
                name = fileNoExt.Substring(0, idx);
            else
                name = fileNoExt;

            if (result.TryGetValue(name, out var list) == false)
            {
                list = new List<string>();
                result[name] = list;
            }
            list.Add(path);
        }
        return result;
    }

    private static bool IsAllDigits(string s, int startIdx)
    {
        for (int i = startIdx; i < s.Length; i++)
            if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    /// <summary>
    /// Sprite import 설정 — Phase B/C 자산과 일관된 형태 (Pivot Bottom, PixelsPerUnit 100).
    /// </summary>
    private static void ApplySpriteImportSettings(string assetPath)
    {
        TextureImporter? importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PIXELS_PER_UNIT;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;

        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)PIVOT_ALIGNMENT;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }
}
