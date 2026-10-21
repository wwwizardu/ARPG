using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Map;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>Real partition writer/reader fault tests in a unique Temp directory; never the player slot.</summary>
    public static partial class DungeonRuntimeDiagnostics
    {
        private static readonly int[] _diagnosticEliteIds = { 1019, 1020, 1021, 1022, 1023 };

        [MenuItem("ARPG/Dungeon/Run Storage and Generation Diagnostics")]
        public static async void Run()
        {
            var report = new StringBuilder();
            try
            {
                ValidateDungeonTable(report);
                ValidateEliteEncounters(report);
                ValidateLayouts(report);
                ValidateSparseSuspension(report);
                ValidateContextLifetime(report);
                ValidateSnapshotIsolation(report);
                await ValidateScenarios(report);
                await ValidatePersistence(report);
                await ValidateSaveScheduling(report);
                report.AppendLine("PASS: all dungeon generation/storage diagnostics.");
            }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex); }
            Debug.Log(report.ToString());
        }

        private static void ValidateDungeonTable(StringBuilder report)
        {
            string path = Path.Combine("Assets", "_BinaryData", "TableData", "DungeonTable.bytes");
            var rows = JsonConvert.DeserializeObject<List<DungeonTable>>(File.ReadAllText(path));
            Require(rows != null && rows.Count > 0, "DungeonTable contains at least one row");
            var ids = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                DungeonTable table = rows[i];
                Require(table != null && table.Id > 0 && ids.Add(table.Id), "DungeonTable IDs are unique and positive");
                Require(table.RoomCount > 0 && table.MonsterTableIds != null &&
                    table.MonsterTableIds.Count > 0 && table.MonsterTableIds.TrueForAll(id => id > 0) && table.MonsterLevel > 0 &&
                    table.BossTableId > 0 && table.BossLevel > 0 && table.CompletionGold >= 0 &&
                    table.ChestGold >= 0 && float.IsNaN(table.ReplacementCooldownHours) == false &&
                    float.IsInfinity(table.ReplacementCooldownHours) == false && table.ReplacementCooldownHours >= 0f &&
                    table.MonsterCountMin >= 0 && table.MonsterCountMax >= table.MonsterCountMin &&
                    table.MonsterCountMax <= 8 &&
                    table.RoomWidthMin >= 7 && table.RoomWidthMax >= table.RoomWidthMin && table.RoomWidthMax <= 24 &&
                    table.RoomHeightMin >= 7 && table.RoomHeightMax >= table.RoomHeightMin && table.RoomHeightMax <= 24,
                    "DungeonTable row has valid generation, encounter and reward values");
                Require(table.MonsterLevelOffset >= -98 && table.MonsterLevelOffset <= 98 &&
                    table.BossLevelOffset >= -98 && table.BossLevelOffset <= 98 &&
                    table.ChestDropRolls >= 0 && table.ChestDropRolls <= ARPG.Utility.DropBonusResolver.MAX_ROLLS_PER_SOURCE &&
                    table.DropQuantityBonus >= -100 && table.DropRarityBonus >= -100,
                    "DungeonTable row has valid drop values (ITEM_DROP_BALANCE §7.6)");
            }
            report.AppendLine($"PASS: DungeonTable contains {rows.Count} structurally valid row(s).");
        }

        private static void ValidateEliteEncounters(StringBuilder report)
        {
            Require((int)GlobalEnum.MonsterArchetype.Normal == 0 &&
                (int)GlobalEnum.MonsterArchetype.Magic == 1 && (int)GlobalEnum.MonsterArchetype.Rare == 2 &&
                (int)GlobalEnum.MonsterArchetype.Boss == 3 && (int)GlobalEnum.MonsterArchetype.EndgameBoss == 4,
                "existing archetype numeric values remain compatible");
            string folder = Path.Combine("Assets", "_BinaryData", "TableData");
            var monsters = JsonConvert.DeserializeObject<List<MonsterTable>>(File.ReadAllText(Path.Combine(folder, "MonsterTable.bytes")));
            var grades = JsonConvert.DeserializeObject<List<MonsterArchetypeTable>>(File.ReadAllText(Path.Combine(folder, "MonsterArchetypeTable.bytes")));
            MonsterArchetypeTable rare = grades.Find(g => g.Archetype == GlobalEnum.MonsterArchetype.Rare);
            MonsterArchetypeTable elite = grades.Find(g => g.Archetype == GlobalEnum.MonsterArchetype.Elite);
            MonsterArchetypeTable boss = grades.Find(g => g.Archetype == GlobalEnum.MonsterArchetype.Boss);
            Require(rare != null && elite != null && boss != null &&
                rare.HpMul < elite.HpMul && elite.HpMul < boss.HpMul &&
                rare.DmgMul < elite.DmgMul && elite.DmgMul < boss.DmgMul,
                "Elite HP and damage multipliers lie between Rare and Boss");

            var go = new GameObject("ElitePoolDiagnostic") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var data = go.AddComponent<DataManager>();
                Set(data, "_monsterTable", monsters.ToImmutableDictionary(m => m.Id));
                MethodInfo rebuild = typeof(DataManager).GetMethod("RebuildMonsterSpawnPools", BindingFlags.Instance | BindingFlags.NonPublic);
                rebuild.Invoke(data, null);
                IReadOnlyList<int> eliteIds = data.GetEliteMonsterIds();
                int eliteCount = monsters.FindAll(m => m.Archetype == GlobalEnum.MonsterArchetype.Elite).Count;
                Require(eliteCount > 0 && eliteIds.Count == eliteCount, "live Elite candidate pool is complete");
                for (int i = 0; i < eliteIds.Count; i++)
                    Require(data.GetMonster(eliteIds[i]).Archetype == GlobalEnum.MonsterArchetype.Elite &&
                        (i == 0 || eliteIds[i - 1] < eliteIds[i]), "Elite candidates are sorted, unique and Elite only");

                var selected = new HashSet<int>();
                for (int seed = 0; seed < 256; seed++)
                {
                    VillageDungeonSlots slots = DungeonLayout.CreateSlots(seed, 7);
                    DungeonRecord layout = DungeonLayout.Generate(seed, 7, slots.Slots[0], CreateDungeonTable(), eliteIds);
                    DungeonObject encounter = layout.Objects.Find(o => o.Kind == DungeonObjectKind.Boss);
                    Require(data.GetMonster(encounter.TableId).Archetype == GlobalEnum.MonsterArchetype.Elite,
                        "village dungeon boss comes from live Elite candidates");
                    selected.Add(encounter.TableId);
                    string saved = JsonConvert.SerializeObject(layout);
                    Require(saved == JsonConvert.SerializeObject(DungeonLayout.Generate(seed, 7, slots.Slots[0], CreateDungeonTable(), eliteIds)),
                        "boss selection is deterministic");
                    DungeonRecord restored = JsonConvert.DeserializeObject<DungeonRecord>(saved);
                    Require(restored.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).TableId == encounter.TableId,
                        "selected boss survives save/load");
                }
                Require(eliteCount == 1 || selected.Count > 1, "different dungeon seeds select different Elite bosses");

                var legacySlot = DungeonLayout.CreateSlots(42, 7).Slots[0];
                DungeonRecord legacy = DungeonLayout.Generate(42, 7, legacySlot, CreateDungeonTable(), eliteIds);
                legacy.GeneratorVersion = 3;
                legacy.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).TableId = 1008;
                legacy = JsonConvert.DeserializeObject<DungeonRecord>(JsonConvert.SerializeObject(legacy));
                Require(DungeonLayout.Validate(legacy, out _) && legacy.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).TableId == 1008,
                    "previously saved non-Elite bosses remain valid and unchanged");
                bool rejectedEmpty = false;
                try { DungeonLayout.Generate(42, 7, legacySlot, CreateDungeonTable(), Array.Empty<int>()); }
                catch (InvalidOperationException) { rejectedEmpty = true; }
                Require(rejectedEmpty, "empty Elite pool does not fall back to a normal boss");

                // 바이옴 전용·복수 서식지 Elite도 공용 필드 풀과 동일하게 제외한다.
                int syntheticEliteId = int.MaxValue;
                monsters.Add(new MonsterTable { Id = syntheticEliteId, Archetype = GlobalEnum.MonsterArchetype.Elite,
                    Biome = GlobalEnum.BiomeType.Forest, AdditionalBiomes = new[] { GlobalEnum.BiomeType.Desert } });
                Set(data, "_monsterTable", monsters.ToImmutableDictionary(m => m.Id));
                rebuild.Invoke(data, null);
                rebuild.Invoke(data, null);
                Require(data.GetEliteMonsterIds().Count == eliteCount + 1, "pool rebuild replaces cached entries without duplicates");
                IReadOnlyList<int> common = data.GetCommonMonsterIds();
                for (int i = 0; i < common.Count; i++)
                    Require(data.GetMonster(common[i]).Archetype != GlobalEnum.MonsterArchetype.Elite, "common field pool excludes Elite");
                foreach (GlobalEnum.BiomeType biome in Enum.GetValues(typeof(GlobalEnum.BiomeType)))
                {
                    IReadOnlyList<int> specific = data.GetBiomeSpecificMonsterIds(biome);
                    for (int i = 0; i < specific.Count; i++)
                        Require(data.GetMonster(specific[i]).Archetype != GlobalEnum.MonsterArchetype.Elite,
                            "biome and additional-biome field pools exclude Elite");
                }
                report.AppendLine($"PASS: {eliteCount} live Elite bosses; Rare < Elite < Boss multipliers; 256 deterministic selections/save round trips; legacy bosses preserved; empty pool rejected; field pools exclude Elite across rebuilds.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void ValidateLayouts(StringBuilder report)
        {
            int two = 0, three = 0;
            for (int v = 0; v < 300; v++)
            {
                var slots = DungeonLayout.CreateSlots(12345, v);
                Require(slots.SlotCount == 2 || slots.SlotCount == 3, "slot count");
                Require(slots.Slots.Count == slots.SlotCount, "slot cardinality");
                if (slots.SlotCount == 2) two++; else three++;
                var replay = DungeonLayout.CreateSlots(12345, v);
                Require(JsonConvert.SerializeObject(slots) == JsonConvert.SerializeObject(replay), "slot determinism");
                // Slot registration does not allocate room metadata.
                Require(new DungeonSaveRoot().Dungeons.Count == 0, "unvisited layouts");
                if (v >= 32) continue;
                for (int i = 0; i < slots.SlotCount; i++)
                {
                    var layout = DungeonLayout.Generate(12345, v, slots.Slots[i], CreateDungeonTable(), _diagnosticEliteIds);
                    var roundtrip = JsonConvert.DeserializeObject<DungeonRecord>(JsonConvert.SerializeObject(layout));
                    Require(DungeonLayout.Validate(roundtrip, out _), "layout round trip");
                    Require(roundtrip.DungeonTableId == 1 &&
                        Math.Abs(roundtrip.ReplacementCooldownHours - 12f) < 0.001f,
                        "dungeon table snapshot");
                    Require(JsonConvert.SerializeObject(layout) == JsonConvert.SerializeObject(
                        DungeonLayout.Generate(12345, v, slots.Slots[i], CreateDungeonTable(), _diagnosticEliteIds)), "layout determinism");
                    ValidateRasterConnectivity(layout);
                    var first = layout.Rooms[0];
                    for (int x = -8; x < 80; x++)
                        for (int y = -8; y < 80; y++)
                            Require(DungeonLayout.IsFloor(layout, x, y) == DungeonLayout.IsFloor(roundtrip, x, y), "chunk reload raster");
                    layout.KilledObjects.Add(10);
                    layout.OpenedChests.Add(11);
                    var progress = JsonConvert.DeserializeObject<DungeonRecord>(JsonConvert.SerializeObject(layout));
                    Require(progress.KilledObjects.Contains(10) && progress.OpenedChests.Contains(11), "progress round trip");
                    layout.GeneratorVersion = int.MaxValue;
                    Require(DungeonLayout.Validate(layout, out _) == false, "unsupported generator must be rejected");
                }
            }
            Require(two > 0 && three > 0, "both slot counts occur");
            DungeonTable tunedTable = CreateDungeonTable();
            tunedTable.MonsterCountMin = tunedTable.MonsterCountMax = 3;
            tunedTable.RoomWidthMin = tunedTable.RoomWidthMax = 15;
            tunedTable.RoomHeightMin = tunedTable.RoomHeightMax = 17;
            VillageDungeonSlots tunedSlots = DungeonLayout.CreateSlots(12345, 776);
            DungeonRecord tunedLayout = DungeonLayout.Generate(12345, 776, tunedSlots.Slots[0], tunedTable, _diagnosticEliteIds);
            int[] roomMonsterCounts = new int[tunedLayout.Rooms.Count];
            for (int i = 0; i < tunedLayout.Rooms.Count; i++)
                Require(tunedLayout.Rooms[i].Width == 15 && tunedLayout.Rooms[i].Height == 17,
                    "table-driven room dimensions");
            for (int i = 0; i < tunedLayout.Objects.Count; i++)
            {
                DungeonObject obj = tunedLayout.Objects[i];
                if (obj.Kind == DungeonObjectKind.Monster || obj.Kind == DungeonObjectKind.Boss)
                    roomMonsterCounts[obj.RoomId]++;
            }
            Require(roomMonsterCounts[0] == 0, "start room remains safe");
            for (int i = 1; i < tunedLayout.BossRoomId; i++)
                Require(roomMonsterCounts[i] == 3, "table-driven normal room monster count");
            Require(roomMonsterCounts[tunedLayout.BossRoomId] == 1, "boss room remains one boss");
            ValidateEncounterLevels();
            DungeonTable defaultTable = CreateDungeonTable();
            var legacyRoot = new DungeonSaveRoot();
            VillageDungeonSlots legacySlots = DungeonLayout.CreateSlots(12345, 777);
            legacySlots.Slots[0].DungeonTableId = 0;
            legacyRoot.Villages.Add(legacySlots);
            DungeonRecord legacyRecord = DungeonLayout.Generate(12345, 777, legacySlots.Slots[0], defaultTable, _diagnosticEliteIds);
            legacyRecord.DungeonTableId = 0;
            legacyRecord.ReplacementCooldownHours = 0f;
            legacyRoot.Dungeons.Add(legacyRecord);
            Require(DungeonLayout.NormalizeTableReferences(legacyRoot, defaultTable), "legacy table migration");
            Require(legacySlots.Slots[0].DungeonTableId == 1 && legacyRecord.DungeonTableId == 1 &&
                Math.Abs(legacyRecord.ReplacementCooldownHours - 12f) < 0.001f,
                "legacy table migration values");
            Require(DungeonLayout.NormalizeTableReferences(legacyRoot, defaultTable) == false,
                "legacy table migration idempotence");
            report.AppendLine($"PASS: 300 villages (2 slots: {two}, 3 slots: {three}), deterministic IDs/rooms; 32 village layout samples, table-driven room size/monster count, entrance-zone encounter levels, table snapshot/legacy migration, raster connectivity/reload, progress and unsupported versions.");
        }

        /// <summary>
        /// 던전 레벨 = 입구 청크 지역 레벨 + 보정, 1~99로 자른다 (ITEM_DROP_BALANCE_DESIGN §7.6).
        /// 상자 레벨은 일반 몬스터 레벨이고, 지역 정보가 없으면(0) 표의 고정 레벨을 쓴다.
        /// </summary>
        private static void ValidateEncounterLevels()
        {
            DungeonTable table = CreateDungeonTable();
            VillageDungeonSlots slots = DungeonLayout.CreateSlots(12345, 778);
            int[,] cases = { { 30, 32, 34 }, { 1, 3, 5 }, { 98, 99, 99 }, { 0, 1, 5 } };
            for (int c = 0; c < cases.GetLength(0); c++)
            {
                DungeonRecord layout = DungeonLayout.Generate(12345, 778, slots.Slots[0], table, _diagnosticEliteIds, cases[c, 0]);
                int expectedMonster = cases[c, 1];
                int expectedBoss = cases[c, 2];
                bool chestSeen = false;
                for (int i = 0; i < layout.Objects.Count; i++)
                {
                    DungeonObject obj = layout.Objects[i];
                    int expected = obj.Kind == DungeonObjectKind.Boss ? expectedBoss : expectedMonster;
                    Require(obj.Level == expected,
                        $"zone {cases[c, 0]} {obj.Kind} level {obj.Level} (expected {expected})");
                    chestSeen |= obj.Kind == DungeonObjectKind.Chest;
                }
                Require(chestSeen, "encounter level sample has a chest");
            }
            Require(DungeonLayout.ResolveEncounterLevel(-5, 2) == 1 && DungeonLayout.ResolveEncounterLevel(97, 4) == 99,
                "encounter level clamps to 1..99");
        }

        private static void ValidateRasterConnectivity(DungeonRecord record)
        {
            var start = record.Rooms[0];
            var queue = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            var origin = new Vector2Int(start.X + start.Width / 2, start.Y + start.Height / 2);
            queue.Enqueue(origin); seen.Add(origin);
            var steps = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                for (int i = 0; i < steps.Length; i++)
                {
                    var next = p + steps[i];
                    if (next.x < -16 || next.y < -16 || next.x > 128 || next.y > 128 ||
                        DungeonLayout.IsFloor(record, next.x, next.y) == false || seen.Add(next) == false) continue;
                    queue.Enqueue(next);
                }
            }
            for (int i = 0; i < record.Objects.Count; i++)
                Require(seen.Contains(new Vector2Int(record.Objects[i].X, record.Objects[i].Y)), "all objects reachable from entrance");
        }

        private static void ValidateSparseSuspension(StringBuilder report)
        {
            var pool = new SparseSet<int>();
            IComponentPool sameReference = pool;
            pool.Add(100, 123);
            object captured = sameReference.Capture(100);
            sameReference.Remove(100);
            pool.Add(200, 456);
            Require(pool.Count == 1 && pool.Contains(100) == false, "world excluded from active iteration");
            sameReference.Restore(100, captured);
            Require(pool.Get(100) == 123 && pool.Get(200) == 456, "pool identity and context restore");
            report.AppendLine("PASS: SparseSet suspend/restore preserves cached pool identity and isolates iteration.");
        }

        private static void ValidateContextLifetime(StringBuilder report)
        {
            var context = new MapContext();
            var load = context.BeginLoad();
            Require(load.IsCurrent && context.PendingLoads == 1, "load lease tracks pending work");
            typeof(MapContext).GetProperty(nameof(MapContext.Epoch)).SetValue(context, 1);
            Require(load.IsCurrent == false, "old async result invalidated on context reset");
            load.Dispose(); load.Dispose();
            Require(context.PendingLoads == 0, "load lease releases exactly once");
            report.AppendLine("PASS: context epochs reject stale async work; load leases release exactly once.");
        }

        private static async Task ValidatePersistence(StringBuilder report)
        {
            string root = Path.GetFullPath("Temp/DungeonDiagnostics/" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("DungeonStorageDiagnostic");
            var data = go.AddComponent<DataManager>();
            // 격리 fixture는 런타임 Initialize를 생략하므로 현재 Editor 메인 스레드를 등록한다.
            Set(data, "_editorMainThreadId", Environment.CurrentManagedThreadId);
            data.DiagnosticSavePath = root;
            try
            {
                var world = new WorldData();
                world.ElapsedWorldGameHours = 37.5f;
                var slots = DungeonLayout.CreateSlots(world.WorldSeed, 0);
                world.Dungeons.Villages.Add(slots);
                slots.Slots[0].HasEntrance = true;
                slots.Slots[0].EntranceX = 33;
                slots.Slots[0].EntranceY = 14;
                var player = new PlayerData { PlayerId = 1 };
                Set(data, "_worldData", world);
                Set(data, "_playerDataList", new List<PlayerData> { player });
                Require(await Write(data, SaveScope.All), "initial checkpoint");
                string slotPath = Path.Combine(root, "Slot_0");
                var first = ReadManifest(slotPath);
                Require(first.Revision == 1 && first.Parts.ContainsKey("dungeons"), "initial dungeon partition");
                object initialSnapshot = ReadSnapshot(data, slotPath, "manifest.json");
                Require(Math.Abs(((WorldSaveRoot)Field(initialSnapshot, "World")).ElapsedWorldGameHours - 37.5f) < 0.001f,
                    "fractional accumulated world time round trip");
                string v15 = Path.Combine(root, "v15");
                Directory.CreateDirectory(Path.Combine(v15, "parts"));
                SaveManifest v15Manifest = JsonConvert.DeserializeObject<SaveManifest>(
                    JsonConvert.SerializeObject(first));
                var v15Keys = new List<string>(v15Manifest.Parts.Keys);
                for (int i = 0; i < v15Keys.Count; i++)
                {
                    SavePartReference part = v15Manifest.Parts[v15Keys[i]];
                    File.Copy(Path.Combine(slotPath, "parts", part.FileName),
                        Path.Combine(v15, "parts", part.FileName));
                }
                ReplaceFixturePart(v15, v15Manifest, "world",
                    "{\"Version\":1,\"WorldDataVersion\":15,\"WorldSeed\":12345,\"NextItemInstanceId\":1}");
                File.WriteAllText(Path.Combine(v15, "manifest.json"), JsonConvert.SerializeObject(v15Manifest));
                object v15Snapshot = ReadSnapshot(data, v15, "manifest.json");
                Require(v15Snapshot != null &&
                    ((WorldSaveRoot)Field(v15Snapshot, "World")).ElapsedWorldGameHours == 0f &&
                    ((DungeonSaveRoot)Field(v15Snapshot, "Dungeons")).Villages.Count == 1,
                    "v15 dungeon save gains zero-based accumulated world time");
                // narrative 파트 (NPC_NARRATIVE_CONTENT_DESIGN §7.7): 21 미만은 파트가 없어도 빈 원장, 21 이상은 빈 원장으로 진행하지 않는다.
                Require(first.Parts.ContainsKey("narrative"), "initial narrative partition");
                foreach (int version in new[] { 20, 21 })
                {
                    string fixture = Path.Combine(root, "narrative-v" + version);
                    Directory.CreateDirectory(Path.Combine(fixture, "parts"));
                    SaveManifest manifest = JsonConvert.DeserializeObject<SaveManifest>(JsonConvert.SerializeObject(first));
                    manifest.Parts.Remove("narrative");
                    foreach (SavePartReference part in manifest.Parts.Values)
                        File.Copy(Path.Combine(slotPath, "parts", part.FileName), Path.Combine(fixture, "parts", part.FileName));
                    ReplaceFixturePart(fixture, manifest, "world",
                        "{\"Version\":1,\"WorldDataVersion\":" + version + ",\"WorldSeed\":12345,\"NextItemInstanceId\":1}");
                    File.WriteAllText(Path.Combine(fixture, "manifest.json"), JsonConvert.SerializeObject(manifest));
                    object loaded = ReadSnapshot(data, fixture, "manifest.json");
                    if (version < 21)
                        Require(loaded != null && ((NarrativeSaveRoot)Field(loaded, "Narrative")).Instances.Count == 0,
                            "pre-21 save without narrative part opens with an empty ledger");
                    else
                        Require(loaded == null, "v21 save without narrative part is rejected");
                }
                string corrupt = Path.Combine(root, "narrative-corrupt");
                Directory.CreateDirectory(Path.Combine(corrupt, "parts"));
                foreach (SavePartReference part in first.Parts.Values)
                    File.Copy(Path.Combine(slotPath, "parts", part.FileName), Path.Combine(corrupt, "parts", part.FileName));
                File.WriteAllText(Path.Combine(corrupt, "manifest.json"), JsonConvert.SerializeObject(first));
                File.AppendAllText(Path.Combine(corrupt, "parts", first.Parts["narrative"].FileName), "corrupt");
                Require(ReadSnapshot(data, corrupt, "manifest.json") == null, "corrupt narrative part rejected");
                report.AppendLine("PASS: narrative partition presence, pre-21 empty ledger, v21 missing/corrupt rejection.");
                // 제거한 exploration 파트 (FOG_OF_WAR_DESIGN §6): 같은 날 잠시 쓰인 WorldDataVersion 22 세이브에 남아 있어도
                // 로드를 막지 않고, 다음 저장의 manifest로 옮기지 않는다. 던전 기록의 옛 탐험 칸도 무시하고 읽는다.
                Require(first.Parts.ContainsKey("exploration") == false && first.Parts.Count == 44, "current writer has no exploration partition");
                string obsolete = Path.Combine(root, "obsolete-exploration");
                Directory.CreateDirectory(Path.Combine(obsolete, "parts"));
                SaveManifest obsoleteManifest = JsonConvert.DeserializeObject<SaveManifest>(JsonConvert.SerializeObject(first));
                foreach (SavePartReference part in obsoleteManifest.Parts.Values)
                    File.Copy(Path.Combine(slotPath, "parts", part.FileName), Path.Combine(obsolete, "parts", part.FileName));
                obsoleteManifest.Parts["exploration"] = new SavePartReference
                {
                    FileName = "exploration.r00000001.obsolete.json", SchemaVersion = 1, Revision = 1,
                };
                ReplaceFixturePart(obsolete, obsoleteManifest, "exploration",
                    "{\"Version\":1,\"World\":[{\"X\":-3,\"Y\":2,\"Bits\":\"AQAAAAAAAIA=\"}]}");
                ReplaceFixturePart(obsolete, obsoleteManifest, "world",
                    "{\"Version\":1,\"WorldDataVersion\":22,\"WorldSeed\":12345,\"NextItemInstanceId\":1}");
                File.WriteAllText(Path.Combine(obsolete, "manifest.json"), JsonConvert.SerializeObject(obsoleteManifest));
                Require(ReadSnapshot(data, obsolete, "manifest.json") != null, "v22 save with an obsolete exploration partition loads");
                var cloned = (SaveManifest)typeof(DataManager).GetMethod("CloneManifest", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { obsoleteManifest, obsoleteManifest.Revision + 1 });
                Require(cloned.Parts.ContainsKey("exploration") == false && cloned.Parts.Count == 44, "next manifest drops the obsolete exploration partition");
                var oldRecord = JsonConvert.DeserializeObject<DungeonRecord>(
                    "{\"DungeonId\":\"old\",\"ExploredBlocks\":[{\"X\":0,\"Y\":0,\"Bits\":\"AA==\"}],\"ExploredGameTime\":3.5}", JsonSettings.Default);
                Require(oldRecord != null && oldRecord.DungeonId == "old", "old dungeon exploration fields are ignored");
                report.AppendLine("PASS: obsolete v22 exploration partition and dungeon exploration fields load without effect; the partition leaves the next manifest.");
                data.DiagnosticBeforeCommit = () =>
                {
                    typeof(DataManager).GetMethod("ResetPartitionedSaveState", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(data, null);
                    return Task.CompletedTask;
                };
                Require(await Write(data, SaveScope.Dungeons) == false, "Reset cancels stale save before manifest commit");
                Require(ReadManifest(slotPath).Revision == 1, "Reset leaves last manifest intact");
                data.DiagnosticBeforeCommit = null;
                Set(data, "_activeSaveManifest", first);
                report.AppendLine("PASS: Reset during an in-flight save cancels its manifest commit and preserves the previous revision.");

                var record = DungeonLayout.Generate(world.WorldSeed, 0, slots.Slots[0], CreateDungeonTable(), _diagnosticEliteIds);
                world.Dungeons.Dungeons.Add(record);
                player.CurrentMapKind = MapKind.Dungeon;
                player.ActiveDungeonId = record.DungeonId;
                player.WorldReturnX = 33.5f; player.WorldReturnY = 13.5f;
                player.DungeonPositionX = record.Rooms[0].X + 2;
                player.DungeonPositionY = record.Rooms[0].Y + 2;
                string[] failures = { "serialize", "part-write", "after-part", "before-manifest" };
                for (int failure = 0; failure < failures.Length; failure++)
                {
                    data.DiagnosticFailurePoint = failures[failure];
                    Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons) == false, "injected entry failure");
                    Require(ReadManifest(slotPath).Revision == 1, "failed entry preserves active manifest");
                    Require(ReadSnapshot(data, slotPath, "manifest.json") != null, "last checkpoint readable after interruption");
                }
                data.DiagnosticFailurePoint = null;
                Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "entry checkpoint");
                var entered = ReadManifest(slotPath);
                Require(entered.Parts["player"].Revision == entered.Revision &&
                    entered.Parts["dungeons"].Revision == entered.Revision &&
                    entered.Parts["world"].FileName == first.Parts["world"].FileName,
                    "entry manifest atomically selects changed player/dungeon and unchanged world");
                Require(entered.Parts["npc_state_00"].FileName == first.Parts["npc_state_00"].FileName &&
                    entered.Parts["villages"].FileName == first.Parts["villages"].FileName, "unrelated partitions unchanged");
                object snapshot = ReadSnapshot(data, slotPath, "manifest.json");
                var loadedPlayer = ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0];
                var loadedDungeon = ((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons[0];
                Require(loadedPlayer.ActiveDungeonId == loadedDungeon.DungeonId &&
                    loadedDungeon.GeneratorVersion == DungeonLayout.GeneratorVersion, "reconnect exact active structure");

                record.KilledObjects.Add(10); record.OpenedChests.Add(11);
                Require(await Write(data, SaveScope.Player | SaveScope.Dungeons), "progress checkpoint");
                snapshot = ReadSnapshot(data, slotPath, "manifest.json");
                loadedDungeon = ((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons[0];
                Require(loadedDungeon.KilledObjects.Contains(10) && loadedDungeon.OpenedChests.Contains(11), "kills and chests survive reconnect");

                // Death/ESC checkpoint returns to the old entrance while preserving interior progress.
                player.CurrentMapKind = MapKind.World;
                player.ActiveDungeonId = string.Empty;
                player.PositionX = player.WorldReturnX; player.PositionZ = player.WorldReturnY;
                player.CurrentHp = 100;
                Require(await Write(data, SaveScope.Player | SaveScope.Dungeons), "death/exit checkpoint");
                snapshot = ReadSnapshot(data, slotPath, "manifest.json");
                Require(((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons[0].KilledObjects.Contains(10), "exit preserves kills");
                player.CurrentMapKind = MapKind.Dungeon; player.ActiveDungeonId = record.DungeonId;
                int bossId = record.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).Id;
                record.KilledObjects.Add(bossId);
                Require(await Write(data, SaveScope.Player | SaveScope.Dungeons), "boss kill checkpoint before completion");
                snapshot = ReadSnapshot(data, slotPath, "manifest.json");
                Require(DungeonCompletionTransaction.NeedsCompletion(((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons[0]),
                    "reconnect between boss kill and completion resumes transaction");
                int originalGold = player.Gold;
                Require(DungeonCompletionTransaction.TryStage(world.Dungeons, player, record, world.WorldSeed,
                    100f, record.ReplacementCooldownHours, out var completion), "stage completion");
                using (completion)
                {
                    Require(DungeonCompletionTransaction.TryStage(world.Dungeons, player, record, world.WorldSeed,
                        100f, record.ReplacementCooldownHours, out _) == false, "duplicate staged boss event");
                    data.DiagnosticFailurePoint = "before-manifest";
                    Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons) == false, "completion failure");
                }
                data.DiagnosticFailurePoint = null;
                Require(record.Completed == false && player.Gold == originalGold && slots.Slots[0].Generation == 0,
                    "failed completion rolls back reward, slot and completion together");
                Require(DungeonCompletionTransaction.TryStage(world.Dungeons, player, record, world.WorldSeed,
                    100f, record.ReplacementCooldownHours, out completion), "completion retry");
                using (completion)
                {
                    Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "completion checkpoint");
                    completion.Commit();
                }
                Require(DungeonCompletionTransaction.TryStage(world.Dungeons, player, record, world.WorldSeed,
                    100f, record.ReplacementCooldownHours, out _) == false, "duplicate committed boss event");
                Require(player.Gold == originalGold + record.CompletionGold && slots.Slots.Count == slots.SlotCount &&
                    slots.Slots[0].HasEntrance == false && slots.Slots[0].HasPreviousEntrance && slots.Slots[0].PreviousX == 33 &&
                    slots.Slots[0].Generation == 1 && Math.Abs(slots.Slots[0].PlacementReadyGameTime - 112f) < 0.001f &&
                    world.Dungeons.CompletedCount == 1, "one reward and 12-hour pending replacement");
                Require(DungeonLayout.IsPlacementReady(slots.Slots[0], 111.999f) == false &&
                    DungeonLayout.IsPlacementReady(slots.Slots[0], 112f),
                    "replacement remains absent until the exact cooldown boundary");
                snapshot = ReadSnapshot(data, slotPath, "manifest.json");
                Require(snapshot != null && ((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons[0].Completed &&
                    Math.Abs(((DungeonSaveRoot)Field(snapshot, "Dungeons")).Villages[0].Slots[0].PlacementReadyGameTime - 112f) < 0.001f &&
                    ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0].ActiveDungeonId == record.DungeonId,
                    "completed active session and cooldown survive reconnect");
                var reconnectedRoot = (DungeonSaveRoot)Field(snapshot, "Dungeons");
                var reconnectedPlayer = ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0];
                Require(DungeonCompletionTransaction.TryStage(reconnectedRoot, reconnectedPlayer,
                    reconnectedRoot.Dungeons[0], world.WorldSeed, 100f, record.ReplacementCooldownHours, out _) == false &&
                    reconnectedPlayer.Gold == originalGold + record.CompletionGold,
                    "reconnected completed checkpoint cannot grant the reward twice");
                player.CurrentMapKind = MapKind.World; player.ActiveDungeonId = string.Empty;
                world.Dungeons.Dungeons.Remove(record);
                Require(await Write(data, SaveScope.Player | SaveScope.Dungeons), "completed exit cleanup");
                Require(((DungeonSaveRoot)Field(ReadSnapshot(data, slotPath, "manifest.json"), "Dungeons")).Dungeons.Count == 0,
                    "completed layout pruned only on exit");
                report.AppendLine("PASS: death/ESC checkpoint shape, duplicate boss events (staged/committed), failure rollback, one reward, same slot count, 12-hour replacement cooldown, completed reconnect and bounded history cleanup.");

                var latest = ReadManifest(slotPath);
                File.AppendAllText(Path.Combine(slotPath, "parts", latest.Parts["dungeons"].FileName), "corrupt");
                Require(ReadSnapshot(data, slotPath, "manifest.json") == null, "corrupt part rejected");
                Require(ReadSnapshot(data, slotPath, "manifest.prev.json") != null, "previous whole revision available");

                // Valid old manifest: remove only the optional v14 dungeon partition and world seed.
                string legacy = Path.Combine(root, "legacy");
                Directory.CreateDirectory(Path.Combine(legacy, "parts"));
                var old = first;
                old.Parts.Remove("dungeons");
                var keys = new List<string>(old.Parts.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var part = old.Parts[keys[i]];
                    // Original revision files are retained by the fixture copy below via previous parts.
                    string source = Path.Combine(slotPath, "parts", part.FileName);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(legacy, "parts", part.FileName));
                }
                // Use the current valid previous manifest for all other parts; v14 field defaults are additive.
                old = JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(slotPath, "manifest.prev.json")));
                old.Parts.Remove("dungeons");
                keys = new List<string>(old.Parts.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var part = old.Parts[keys[i]];
                    File.Copy(Path.Combine(slotPath, "parts", part.FileName), Path.Combine(legacy, "parts", part.FileName), true);
                }
                ReplaceFixturePart(legacy, old, "world", "{\"Version\":1,\"WorldDataVersion\":14,\"NextItemInstanceId\":1}");
                ReplaceFixturePart(legacy, old, "player", JsonConvert.SerializeObject(new PlayerSaveRoot { Players = new List<PlayerData> { new PlayerData() } }));
                File.WriteAllText(Path.Combine(legacy, "manifest.json"), JsonConvert.SerializeObject(old));
                snapshot = ReadSnapshot(data, legacy, "manifest.json");
                Require(snapshot != null && ((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons.Count == 0 &&
                    ((WorldSaveRoot)Field(snapshot, "World")).WorldSeed == 12345 &&
                    ((WorldSaveRoot)Field(snapshot, "World")).ElapsedWorldGameHours == 0f,
                    "v14 manifest migration defaults");
                WorldData legacyWorld = JsonConvert.DeserializeObject<WorldData>("{\"Version\":14}");
                Require(legacyWorld.WorldSeed == 12345 && legacyWorld.ElapsedWorldGameHours == 0f,
                    "legacy monolith world defaults");
                report.AppendLine("PASS: actual partition writer/reader, entry atomic revision, accumulated world time, two injected crash points, unchanged NPC/village parts, progress/reconnect, corruption/previous-revision recovery, v15 dungeon save and v14 manifest/monolith defaults.");
            }
            finally
            {
                try
                {
                    await data.WaitForSaveIdleAsync();
                    UnityEngine.Object.DestroyImmediate(go);
                }
                finally { DeleteDiagnosticDirectory(root); }
            }
        }

        private static void ValidateSnapshotIsolation(StringBuilder report)
        {
            // Populate every persisted public field, including future nested DTO additions.
            // Constructors are deliberately skipped: this test must never initialize gameplay.
            object world = CreateSnapshotFixture(typeof(WorldData), 0);
            object player = CreateSnapshotFixture(typeof(PlayerSaveRoot), 0);
            var copier = new SaveDtoSnapshot();
            object[] roots = { world, player };
            for (int i = 0; i < roots.Length; i++)
            {
                object copy = copier.Capture(roots[i]);
                string expected = JsonConvert.SerializeObject(roots[i], JsonSettings.Default);
                string captured = JsonConvert.SerializeObject(copy, JsonSettings.Default);
                Require(Newtonsoft.Json.Linq.JToken.DeepEquals(
                    Newtonsoft.Json.Linq.JToken.Parse(expected), Newtonsoft.Json.Linq.JToken.Parse(captured)),
                    "snapshot preserves all persisted fields, enum names, nulls and JSON member names");
                var runtimeReferences = new HashSet<object>();
                CollectReferences(roots[i], runtimeReferences);
                AssertDetached(copy, runtimeReferences);
            }
            var item = new ItemData { Equipment = new EquipmentData(), SkillBook = new SkillBookData() };
            item.Equipment.WeaponLocalEnhancements.Add(new WeaponLocalEnhancementInstance { Value = 17 });
            item.SkillBook.SocketedPages.Add(new ItemData { Quantity = 3 });
            var p = new PlayerData { _inventory = new List<ItemData> { item } };
            var d = new DungeonRecord();
            d.KilledObjects.Add(7); d.OpenedChests.Add(8);
            d.Monsters[7] = new DungeonMonsterState { Hp = 23 };
            d.Drops.Add(new WorldDropItemData { ItemData = item });
            var snapshot = copier.Capture(new PlayerSaveRoot { Players = new List<PlayerData> { p } });
            string playerJson = JsonConvert.SerializeObject(snapshot, JsonSettings.Default);
            object dungeonSnapshot = copier.Capture(d);
            string dungeonJson = JsonConvert.SerializeObject(dungeonSnapshot, JsonSettings.Default);
            item.Equipment.WeaponLocalEnhancements[0].Value = 999;
            item.SkillBook.SocketedPages[0].Quantity = 999;
            p._inventory.Clear(); d.KilledObjects.Clear(); d.OpenedChests.Clear(); d.Monsters[7].Hp = 999;
            Require(JsonConvert.SerializeObject(snapshot, JsonSettings.Default) == playerJson &&
                JsonConvert.SerializeObject(dungeonSnapshot, JsonSettings.Default) == dungeonJson,
                "nested item, list, hash set and dictionary mutations cannot change captured DTOs");
            item.SkillBook.SocketedPages.Add(item);
            bool rejected = false;
            try { copier.Capture(item); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "cyclic runtime data fails capture safely");
            report.AppendLine("PASS: populated World/Player graphs match the existing JSON schema; every copied container is detached, including nested equipment/pages, dungeon sets/monsters/drops, NPCs, villages, chronicle and world simulation. Cycles fail safely.");
        }

        private static object CreateSnapshotFixture(Type type, int depth)
        {
            if (type == typeof(string)) return "fixture";
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            if (type == typeof(bool)) return true;
            if (type.IsPrimitive || type == typeof(decimal)) return Convert.ChangeType(2, type);
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return CreateSnapshotFixture(nullable, depth);
            if (depth > 9) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (type.IsArray)
            {
                var array = Array.CreateInstance(type.GetElementType(), 1);
                array.SetValue(CreateSnapshotFixture(type.GetElementType(), depth + 1), 0);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var map = (System.Collections.IDictionary)Activator.CreateInstance(type);
                Type[] args = type.GetGenericArguments();
                map.Add(CreateSnapshotFixture(args[0], depth + 1), CreateSnapshotFixture(args[1], depth + 1));
                return map;
            }
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) ||
                type.GetGenericTypeDefinition() == typeof(HashSet<>)))
            {
                object list = Activator.CreateInstance(type);
                type.GetMethod("Add").Invoke(list, new[] { CreateSnapshotFixture(type.GetGenericArguments()[0], depth + 1) });
                return list;
            }
            object result = type.IsValueType ? Activator.CreateInstance(type) :
                System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsNotSerialized || fields[i].GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                fields[i].SetValue(result, CreateSnapshotFixture(fields[i].FieldType, depth + 1));
            }
            return result;
        }

        private static void CollectReferences(object value, HashSet<object> references)
        {
            if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            if (references.Add(value) == false) return;
            if (value is System.Collections.IDictionary dictionary)
            {
                var iterator = dictionary.GetEnumerator();
                try { for (; iterator.MoveNext();) CollectReferences(iterator.Value, references); }
                finally { (iterator as IDisposable)?.Dispose(); }
            }
            else if (value is System.Collections.IEnumerable sequence)
            {
                var iterator = sequence.GetEnumerator();
                try { for (; iterator.MoveNext();) CollectReferences(iterator.Current, references); }
                finally { (iterator as IDisposable)?.Dispose(); }
            }
            FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++) CollectReferences(fields[i].GetValue(value), references);
        }

        private static void AssertDetached(object value, HashSet<object> runtime)
        {
            if (value == null || value is string || value is decimal || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            Require(runtime.Contains(value) == false, "no runtime references in a DTO snapshot");
            if (value is Dictionary<string, object> fields)
            {
                var values = new List<object>(fields.Values);
                for (int i = 0; i < values.Count; i++) AssertDetached(values[i], runtime);
            }
            else if (value is List<object> entries)
            {
                for (int i = 0; i < entries.Count; i++) AssertDetached(entries[i], runtime);
            }
            else throw new InvalidOperationException("Unexpected runtime type in snapshot: " + value.GetType());
        }

        private static async Task ValidateSaveScheduling(StringBuilder report)
        {
            string root = Path.GetFullPath("Temp/SaveScheduling/" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("SaveSchedulingDiagnostic");
            var data = go.AddComponent<DataManager>();
            Set(data, "_editorMainThreadId", Environment.CurrentManagedThreadId);
            data.DiagnosticSavePath = root;
            data.DiagnosticSkipRuntimeCapture = true;
            var world = new WorldData();
            var player = new PlayerData { Gold = 10 };
            Set(data, "_worldData", world);
            Set(data, "_playerDataList", new List<PlayerData> { player });
            string slot = Path.Combine(root, "Slot_0");
            using var release = new ManualResetEventSlim(false);
            try
            {
                Require(await QueueWrite(data, SaveScope.All), "initial queued save");
                await data.WaitForSaveIdleAsync();
                int captureThread = Thread.CurrentThread.ManagedThreadId;
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int preparations = 0;
                data.DiagnosticWorkerStage = stage =>
                {
                    if (stage == "prepare")
                    {
                        Require(Thread.CurrentThread.ManagedThreadId != captureThread, "serialization runs off Unity main thread");
                        if (Interlocked.Increment(ref preparations) == 1)
                        {
                            entered.TrySetResult(true);
                            Require(release.Wait(15000), "prepare gate timeout");
                        }
                    }
                };
                var autoSave = QueueWrite(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons);
                await entered.Task;
                player.Gold = 20;
                var manualSave = QueueWrite(data, SaveScope.All);
                var anotherAutoSave = QueueWrite(data, SaveScope.Player);
                Require(autoSave.IsCompleted == false && manualSave.IsCompleted == false, "overlapping callers await their writes");
                release.Set();
                Require(await autoSave && await manualSave && await anotherAutoSave, "auto/manual coalesced saves");
                await data.WaitForSaveIdleAsync();
                Require(ReadManifest(slot).Revision == 3 && preparations == 2, "queued scopes coalesce into one subsequent revision");
                Require(((PlayerSaveRoot)Field(ReadSnapshot(data, slot, "manifest.prev.json"), "Player")).Players[0].Gold == 10 &&
                    ((PlayerSaveRoot)Field(ReadSnapshot(data, slot, "manifest.json"), "Player")).Players[0].Gold == 20,
                    "mutation during background write is isolated, next capture sees latest value");

                var commitReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var allowCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                data.DiagnosticBeforeCommit = async () => { commitReached.TrySetResult(true); await allowCommit.Task; };
                var successfulBatch = QueueWrite(data, SaveScope.Player);
                await commitReached.Task;
                data.DiagnosticFailurePoint = "serialize";
                var failingBatch = QueueWrite(data, SaveScope.All);
                data.DiagnosticBeforeCommit = null;
                allowCommit.TrySetResult(true);
                Require(await successfulBatch && await failingBatch == false,
                    "later queued failure cannot change a previous caller's committed success");
                data.DiagnosticFailurePoint = null;
                await data.WaitForSaveIdleAsync();

                // Cleanup owns the file lane after save success. Reset cannot release it early.
                release.Reset();
                entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                data.DiagnosticWorkerStage = stage =>
                {
                    if (stage != "cleanup") return;
                    entered.TrySetResult(true);
                    Require(release.Wait(15000), "cleanup gate timeout");
                };
                Require(await QueueWrite(data, SaveScope.Player), "commit succeeds independently of cleanup");
                await entered.Task;
                Task<bool> waitingSave = QueueWrite(data, SaveScope.All);
                Task idle = data.WaitForSaveIdleAsync();
                Require(waitingSave.IsCompleted == false && idle.IsCompleted == false, "next save and idle drain wait for cleanup");
                ResetSave(data);
                Task<bool> loading = data.Load();
                Require(loading.IsCompleted == false, "Load drains pending save/cleanup before touching disk");
                ResetSave(data); // cancel the load while it is still draining; fixture needs no live AR managers.
                release.Set();
                Require(await waitingSave == false, "Reset invalidates the waiting old-lifetime save");
                await idle;
                Require(await loading == false, "Reset cancels a draining Load");
                Require(ReadSnapshot(data, slot, "manifest.json") != null, "Reset leaves committed revision readable");
                Set(data, "_activeSaveManifest", ReadManifest(slot));
                data.DiagnosticWorkerStage = null;

                string orphan = Path.Combine(slot, "parts", "orphan.json");
                File.WriteAllText(orphan, "orphan");
                data.DiagnosticFailurePoint = "cleanup";
                Require(await QueueWrite(data, SaveScope.Player), "cleanup failure does not invalidate commit");
                await data.WaitForSaveIdleAsync();
                Require(File.Exists(orphan), "failed cleanup retained for retry");
                data.DiagnosticFailurePoint = null;
                Require(await QueueWrite(data, SaveScope.Player), "save after cleanup failure");
                await data.WaitForSaveIdleAsync();
                Require(File.Exists(orphan) == false && ReadSnapshot(data, slot, "manifest.prev.json") != null,
                    "next successful cleanup retries orphans and preserves previous revision");

                long revision = ReadManifest(slot).Revision;
                using (var lockedManifest = new FileStream(Path.Combine(slot, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    Require(await QueueWrite(data, SaveScope.Player) == false, "actual manifest replacement I/O failure");
                Require(ReadManifest(slot).Revision == revision && ReadSnapshot(data, slot, "manifest.prev.json") != null,
                    "failed replacement preserves active AND previous manifests");

                // Migration must capture all 44 partitions (narrative 포함); unchanged files can keep their creation revision.
                typeof(DataManager).GetMethod("MigrateWorldData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(data, new object[] { (ushort)15, (ushort)16 });
                data.DiagnosticFailurePoint = "serialize";
                Require(await QueueWrite(data, SaveScope.Player) == false, "failed migration checkpoint");
                Require((bool)typeof(DataManager).GetField("_needsFullSaveAfterMigration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(data),
                    "migration promotion retained after failure");
                data.DiagnosticFailurePoint = null;
                Require(await QueueWrite(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "first post-migration full save");
                await data.WaitForSaveIdleAsync();
                SaveManifest migrated = ReadManifest(slot);
                var parts = new List<SavePartReference>(migrated.Parts.Values);
                Require(parts.Count == 44, "all partitions including 32 NPC shards and narrative");
                Require(data.DiagnosticCommittedScope == SaveScope.All && data.DiagnosticPreparedPartCount == 44,
                    "migration captures every part before content reuse");
                string migratedNpcFile = migrated.Parts["npc_state_00"].FileName;
                Require(await QueueWrite(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "normal save after migration");
                await data.WaitForSaveIdleAsync();
                Require(ReadManifest(slot).Parts["npc_state_00"].FileName == migratedNpcFile && data.DiagnosticCommittedScope != SaveScope.All,
                    "migration flag clears only after full commit");

                // Synthetic, reproducible scale fixture with an active nine-room dungeon.
                data.DiagnosticAfterCapture = () => Require(Thread.CurrentThread.ManagedThreadId == captureThread, "Unity capture thread");
                data.DiagnosticBeforeCommit = () =>
                {
                    Require(Thread.CurrentThread.ManagedThreadId == captureThread, "Unity commit thread");
                    return Task.CompletedTask;
                };
                data.DiagnosticWorkerStage = stage => Require(Thread.CurrentThread.ManagedThreadId != captureThread, "Unity background stage: " + stage);
                var dungeonSlots = DungeonLayout.CreateSlots(world.WorldSeed, 0);
                world.Dungeons.Villages.Add(dungeonSlots);
                var dungeon = DungeonLayout.Generate(world.WorldSeed, 0, dungeonSlots.Slots[0], CreateDungeonTable(), _diagnosticEliteIds);
                world.Dungeons.Dungeons.Add(dungeon);
                player.CurrentMapKind = MapKind.Dungeon;
                player.ActiveDungeonId = dungeon.DungeonId;
                for (int i = 0; i < dungeon.Objects.Count; i++)
                {
                    var obj = dungeon.Objects[i];
                    if (obj.Kind != DungeonObjectKind.Chest)
                        dungeon.Monsters[obj.Id] = new DungeonMonsterState { Hp = 100, X = obj.X, Y = obj.Y };
                }
                dungeon.Drops.Add(new WorldDropItemData { ItemData = new ItemData { Id = 1, Quantity = 2 } });
                var npc = (ARPG.Npc.NpcSaveData)CreateSnapshotFixture(typeof(ARPG.Npc.NpcSaveData), 2);
                npc.InventoryEquip = new ItemData[(int)GlobalEnum.EquipSlotType.Max];
                npc.InventoryEquip[0] = new ItemData { Id = 1, Equipment = new EquipmentData() };
                npc.Inventory = new List<ItemData> { new ItemData
                {
                    Id = 2, SkillBook = new SkillBookData
                    {
                        SkillId = 1, SocketedPages = new List<ItemData> { new ItemData { Id = 3, SkillPage = new SkillPageData() } },
                    },
                } };
                for (int i = 1; i <= 2400; i++) world.NpcSaveDatas[i] = npc;
                Require(await QueueWrite(data, SaveScope.All), "2400 NPC full checkpoint");
                await data.WaitForSaveIdleAsync();
                report.AppendLine($"MEASURE: synthetic 2400 NPC All: capture {data.DiagnosticCaptureMilliseconds:F2} ms, background prepare {data.DiagnosticPrepareMilliseconds:F2} ms, main-thread commit {data.DiagnosticCommitMilliseconds:F2} ms.");
                Require(await QueueWrite(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "three-part checkpoint after All");
                await data.WaitForSaveIdleAsync();
                report.AppendLine($"MEASURE: Player|World|Dungeons: capture {data.DiagnosticCaptureMilliseconds:F2} ms, background prepare {data.DiagnosticPrepareMilliseconds:F2} ms, main-thread commit {data.DiagnosticCommitMilliseconds:F2} ms.");
                Require(ReadSnapshot(data, slot, "manifest.json") != null, "large checkpoint roundtrip");
                await ValidateLegacyWriter(report, root);
                report.AppendLine("PASS: consecutive/coalesced auto/manual saves with per-batch outcomes, snapshot mutation during write, cleanup/next-save exclusion, Reset drain, cleanup retry, actual manifest replacement failure, migration full-save promotion and retry, 2400 NPC roundtrip.");
                report.AppendLine("PASS: Unity main-thread capture/commit; worker preparation; Load waits for save/cleanup and Reset cancels a draining Load.");
            }
            finally
            {
                release.Set();
                try
                {
                    await data.WaitForSaveIdleAsync();
                    UnityEngine.Object.DestroyImmediate(go);
                }
                finally { DeleteDiagnosticDirectory(root); }
            }
        }

        private static void ResetSave(DataManager data)
            => typeof(DataManager).GetMethod("ResetPartitionedSaveState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(data, null);

        private static Task<bool> QueueWrite(DataManager data, SaveScope scope)
            => (Task<bool>)typeof(DataManager).GetMethod("SavePartitionedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(data, new object[] { scope, false });

        private static async Task ValidateLegacyWriter(StringBuilder report, string parent)
        {
            string root = Path.Combine(parent, "monolith-migration");
            Directory.CreateDirectory(root);
            const string worldJson = "{\"Version\":15}";
            const string playerJson = "[{\"Version\":2}]";
            File.WriteAllText(Path.Combine(root, "WorldData.json"), worldJson);
            File.WriteAllText(Path.Combine(root, "PlayerData.json"), playerJson);
            var go = new GameObject("LegacyWriterDiagnostic");
            var data = go.AddComponent<DataManager>();
            Set(data, "_editorMainThreadId", Environment.CurrentManagedThreadId);
            data.DiagnosticSavePath = root;
            data.DiagnosticSkipRuntimeCapture = true;
            Set(data, "_worldData", JsonConvert.DeserializeObject<WorldData>(worldJson));
            Set(data, "_playerDataList", JsonConvert.DeserializeObject<List<PlayerData>>(playerJson));
            try
            {
                typeof(DataManager).GetMethod("MigrateWorldData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(data, new object[] { (ushort)15, (ushort)16 });
                bool migrated = await (Task<bool>)typeof(DataManager).GetMethod("MigrateLegacySaveToPartitionsAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(data, null);
                await data.WaitForSaveIdleAsync();
                string slot = Path.Combine(root, "Slot_0");
                Require(migrated && ReadManifest(slot).Parts.Count == 44 && ReadSnapshot(data, slot, "manifest.json") != null,
                    "legacy monolith migration writes a readable full revision");
                Require(File.ReadAllText(Path.Combine(root, "WorldData.json")) == worldJson &&
                    File.ReadAllText(Path.Combine(root, "PlayerData.json")) == playerJson, "legacy files preserved");
                Require(await QueueWrite(data, SaveScope.Player | SaveScope.World | SaveScope.Dungeons), "first save after monolith migration");
                await data.WaitForSaveIdleAsync();
                Require(ReadManifest(slot).Parts["npc_state_00"].Revision == 1, "successful migration does not need another full checkpoint");
                report.AppendLine("PASS: v15 monolith DTO migration uses the real full writer, preserves legacy files, reloads and permits a three-part first follow-up save.");
            }
            finally { await data.WaitForSaveIdleAsync(); UnityEngine.Object.DestroyImmediate(go); }
        }
        private static Task<bool> Write(DataManager data, SaveScope scope)
            => (Task<bool>)typeof(DataManager).GetMethod("WritePartitionSnapshotAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(data, new object[] { scope, false, false });

        private static void DeleteDiagnosticDirectory(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string tempRoot = Path.GetFullPath("Temp") + Path.DirectorySeparatorChar;
            Require(fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase),
                "diagnostic cleanup remains under project Temp");
            if (Directory.Exists(fullPath) == true)
                Directory.Delete(fullPath, true);
        }

        private static DungeonTable CreateDungeonTable()
        {
            return new DungeonTable
            {
                Id = 1,
                Name = "Diagnostic Dungeon",
                RoomCount = 9,
                MonsterTableIds = new List<int> { 1009 },
                MonsterLevel = 1,
                BossTableId = 1008,
                BossLevel = 5,
                CompletionGold = 100,
                ChestGold = 20,
                ReplacementCooldownHours = 12f,
                MonsterCountMin = 1,
                MonsterCountMax = 1,
                RoomWidthMin = 9,
                RoomWidthMax = 12,
                RoomHeightMin = 9,
                RoomHeightMax = 12,
                // 던전 드롭 (ITEM_DROP_BALANCE_DESIGN §7.6) 초안 값
                MonsterLevelOffset = 2,
                BossLevelOffset = 4,
                ChestDropRolls = 3,
                DropQuantityBonus = 70,
                DropRarityBonus = 0,
            };
        }

        private static object ReadSnapshot(DataManager data, string path, string manifest)
        {
            object[] args = { path, Path.Combine(path, manifest), null, null };
            bool success = (bool)typeof(DataManager).GetMethod("TryLoadManifestSnapshot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(data, args);
            return success ? args[3] : null;
        }

        private static void ReplaceFixturePart(string root, SaveManifest manifest, string key, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var part = manifest.Parts[key];
            File.WriteAllBytes(Path.Combine(root, "parts", part.FileName), bytes);
            part.Length = bytes.Length;
            using var sha = SHA256.Create();
            part.Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        private static SaveManifest ReadManifest(string path) => JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(path, "manifest.json")));
        private static object Field(object target, string name) => target.GetType().GetField(name).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static void Require(bool value, string label) { if (value == false) throw new InvalidOperationException(label); }
    }
}
