---
name: create-sprite-library
description: 스프라이트 폴더 하나를 지정하면 Bear 포맷의 Unity SpriteLibraryAsset(.spriteLib)을 Assets/Art/Animation/ 에 생성한다. 카테고리는 Idle/Move/Attack/Dead 고정(Die 파일은 Dead로 매핑). "스프라이트 라이브러리 만들어줘", "SpriteLibrary 생성", "create sprite library", 또는 $create-sprite-library 호출 시 실행. 생성 후 GetSprite로 missing 0 검증까지 수행.
---

# create-sprite-library

스프라이트 폴더(예: `Assets/Art/Sprites/Monster/Wolf`)를 입력하면 Bear와 동일한 포맷의
`.spriteLib` 소스 에셋을 `Assets/Art/Animation/<폴더명>.spriteLib` 에 생성한다.

**Unity MCP(UnityMCP 인스턴스)가 실행 중이어야 한다.** 정확한 스프라이트 fileID는 임포트된
에셋에서만 얻을 수 있기 때문에 반드시 에디터 안에서 생성한다.

## 기본값

- 입력: 스프라이트 폴더 경로 하나 (`<Base>_<Category><N>.png` 규칙의 PNG들이 들어있음)
- 출력: `Assets/Art/Animation/<폴더명>.spriteLib` (`<폴더명>`이 라이브러리 이름 = Base)
- 카테고리(고정 4종, 이 순서로): `Idle`, `Move`, `Attack`, `Dead`
- 파일명 접두사 매핑: `Idle→Idle`, `Move→Move`, `Attack→Attack`, `Die→Dead`, `Dead→Dead`
- 라벨: 각 카테고리 내 인덱스 오름차순으로 `<Category>1..N` 재부여 (예: `Die1..3` → `Dead1..3`)
- 그 외 접두사(예: `Cast`, `Hurt`)는 **건너뛰고 WARN 로그**를 남긴다

## 왜 이렇게 하나 (실패했던 함정)

1. **fileID는 meta를 신뢰하지 말 것.** 같은 프로젝트 안에서도 스프라이트 임포트 방식이 섞여 있다.
   - mosaic 모드: 스프라이트 이름이 `<Base>_Idle1_0`, fileID = 고유 internalID
   - 클래식 Single 모드: 이름 `<Base>_Idle1`, fileID = `21300000`
   - `.png.meta`의 `internalIDToNameTable`에 internalID가 있어도 Single 모드면 실제 참조는 `21300000`이라
     meta 파싱으로는 틀린다. 반드시 `AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, ...)`로
     **로드된 Sprite의 실제 fileID**를 쓴다. (이 스크립트가 그렇게 한다.)
2. **해시는 Unity가 계산.** 카테고리/라벨의 `m_Hash`는 `SpriteLibraryUtility.Bit30Hash_GetStringHash`로
   구한다. (검증됨: "Idle"→1008081451 등 Bear 값과 정확히 일치.)
3. `SpriteLibrarySourceAssetImporter.SaveSpriteLibrarySourceAsset(...)` 내부 API는 초기화 요구가 까다로워
   NRE가 난다 → 사용하지 않는다. 대신 Bear와 동일한 YAML을 직접 써서 `ImportAsset`한다.

## 순서

1. 사용자에게 받은 폴더 경로를 확인한다. 라이브러리 이름 = 폴더명(Base).
2. 출력 파일(`Assets/Art/Animation/<Base>.spriteLib`)이 이미 있으면 덮어쓸지 확인한다.
   (덮어쓰기 시 기존 `.meta`/guid는 Unity가 보존하므로 다른 곳의 참조는 유지된다.)
3. 아래 C# 스크립트에서 `FOLDER` 한 줄만 대상 폴더로 바꿔 `mcp__UnityMCP__execute_code`(action=execute)로 실행한다.
4. 결과의 `VERIFY ok=.. missing=..` 를 확인한다. `missing > 0` 이거나 `WARN` 이 있으면 사용자에게 보고한다.
5. 생성된 경로, 카테고리별 프레임 수, 검증 결과, 건너뛴 프레임(있으면)을 보고한다.

## 생성 스크립트 (검증됨: Wolf 13/13 ok)

`FOLDER` 값만 대상 스프라이트 폴더로 교체해 그대로 실행한다. (method body로 실행되므로 `using` 금지 — 전부 정규화된 이름을 쓴다.)

```csharp
var sb = new System.Text.StringBuilder();
// ---- PARAMS: 이 줄만 바꾼다 ----
string FOLDER = "Assets/Art/Sprites/Monster/Wolf";
// --------------------------------
string outDir = "Assets/Art/Animation";
string importerGuid = "a5e6fedc2472449cead18ef23b5cb30d"; // SpriteLibrarySourceAsset ScriptedImporter
string baseName = System.IO.Path.GetFileName(FOLDER.TrimEnd('/'));
string outPath = outDir + "/" + baseName + ".spriteLib";
var utilT = System.Type.GetType("UnityEngine.U2D.Animation.SpriteLibraryUtility, Unity.2D.Animation.Runtime");
var hashMi = utilT.GetMethod("Bit30Hash_GetStringHash", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
System.Func<string,int> H = (s) => (int)hashMi.Invoke(null, new object[]{ s });
var guids = UnityEditor.AssetDatabase.FindAssets("t:Sprite", new[]{ FOLDER });
string[] order = { "Idle", "Move", "Attack", "Dead" };
var map = new System.Collections.Generic.Dictionary<string,string>(){ {"Idle","Idle"},{"Move","Move"},{"Attack","Attack"},{"Die","Dead"},{"Dead","Dead"} };
var buckets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, UnityEngine.Sprite>>>();
var rx = new System.Text.RegularExpressions.Regex(@"^(.+)_([A-Za-z]+?)(\d+)$");
foreach (var g in guids)
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    if (!p.EndsWith(".png")) continue;
    string fn = System.IO.Path.GetFileNameWithoutExtension(p);
    var mt = rx.Match(fn); if (!mt.Success) continue;
    string prefix = mt.Groups[2].Value; int idx = int.Parse(mt.Groups[3].Value);
    if (!map.ContainsKey(prefix)) { sb.AppendLine("WARN unknown category '"+prefix+"' skipped: "+fn); continue; }
    string cat = map[prefix];
    var spr = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(p);
    if (spr == null) { sb.AppendLine("WARN no sprite sub-asset: "+p); continue; }
    if (!buckets.ContainsKey(cat)) buckets[cat] = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, UnityEngine.Sprite>>();
    buckets[cat].Add(new System.Collections.Generic.KeyValuePair<int, UnityEngine.Sprite>(idx, spr));
}
var y = new System.Text.StringBuilder();
y.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &1\nMonoBehaviour:\n");
y.Append("  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n");
y.Append("  m_Script: {fileID: 11500000, guid: " + importerGuid + ", type: 3}\n");
y.Append("  m_Name: \n  m_EditorClassIdentifier: Unity.2D.Animation.Runtime::UnityEngine.U2D.Animation.SpriteLibrarySourceAsset\n  m_Library:\n");
int totalEntries = 0;
for (int oi=0; oi<order.Length; oi++)
{
    string cat = order[oi]; if (!buckets.ContainsKey(cat)) continue;
    var list = buckets[cat]; list.Sort((a,b)=>a.Key.CompareTo(b.Key));
    y.Append("  - m_Name: " + cat + "\n    m_Hash: " + H(cat) + "\n    m_CategoryList: []\n    m_OverrideEntries:\n");
    for (int i=0;i<list.Count;i++)
    {
        string label = cat + (i+1);
        long fid; string sg; UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(list[i].Value, out sg, out fid);
        string reff = "{fileID: " + fid + ", guid: " + sg + ", type: 3}";
        y.Append("    - m_Name: " + label + "\n      m_Hash: " + H(label) + "\n      m_Sprite: " + reff + "\n      m_FromMain: 0\n      m_SpriteOverride: " + reff + "\n");
        totalEntries++;
    }
    y.Append("    m_FromMain: 0\n    m_EntryOverrideCount: " + list.Count + "\n");
}
y.Append("  m_PrimaryLibraryGUID: \n  m_ModificationHash: " + (System.Math.Abs((long)H(baseName)) * 100 + totalEntries) + "\n  m_Version: 1\n");
string projRoot = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.Length-6);
System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, outPath), y.ToString());
UnityEditor.AssetDatabase.ImportAsset(outPath, UnityEditor.ImportAssetOptions.ForceUpdate);
var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.Animation.SpriteLibraryAsset>(outPath);
int ok=0, miss=0;
if (lib==null) sb.AppendLine("VERIFY asset NULL");
else foreach (var c in order){ if(!buckets.ContainsKey(c))continue; int cnt=buckets[c].Count; for(int i=1;i<=cnt;i++){ if(lib.GetSprite(c,c+i)!=null)ok++; else{miss++;sb.AppendLine("MISSING "+c+i);} } }
sb.AppendLine("OUT=" + outPath + " base=" + baseName + " total=" + totalEntries + " VERIFY ok=" + ok + " missing=" + miss);
return sb.ToString();
```

## 여러 폴더 한 번에

`FOLDER`를 배열로 만들고 스캔~검증 블록을 `foreach`로 감싸면 배치 생성도 된다. 필요할 때 사용자에게
대상 폴더 목록을 받아 확장한다.

## 하지 않는 것

- 스프라이트 자체 생성/편집(그건 `generate-monster-sprites` 스킬), 임포트 설정 변경, 프리팹/애니메이터/클립 생성.
- 표준 4종(Idle/Move/Attack/Dead) 외 카테고리 추가. 다른 카테고리가 필요하면 사용자에게 먼저 확인한다.
