#nullable enable
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Village;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ARPG.Editor
{
    public static class NpcSaveSnapshotDiagnostics
    {
        [MenuItem("ARPG/Save/Diagnostics/Verify Simulation Snapshot Save Round Trip")]
        public static async void VerifySimulationSaveRoundTripMenu()
        {
            try { Debug.Log(await VerifySimulationSaveRoundTripAsync()); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>별도 DataManager와 프로젝트 Temp 경로에서만 실제 writer/reader를 검증한다.</summary>
        public static async Task<string> VerifySimulationSaveRoundTripAsync()
        {
            if (Application.isPlaying == false || AR.s == null || AR.s.IsInitialized == false ||
                AR.s.Data.DiagnosticSavePath != null || AR.s.Dungeon.RequiresCheckpoint == true)
                throw new InvalidOperationException("진단 경로가 설정되지 않은 월드 GameScene 플레이 모드에서 실행하세요.");
            string tempParent = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SaveSnapshotDiagnostics"));
            bool createdParent = Directory.Exists(tempParent) == false;
            string root = Path.GetFullPath(Path.Combine(tempParent, Guid.NewGuid().ToString("N")));
            if (root.StartsWith(tempParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
                throw new InvalidOperationException("Diagnostic path escaped project Temp.");
            GameObject owner = new("Simulation partition diagnostics") { hideFlags = HideFlags.HideAndDontSave };
            DataManager data = owner.AddComponent<DataManager>();
            // 이 fixture는 Initialize/Load를 호출하지 않으며 실제 세이브 경로를 사용할 수 없다.
            data.DiagnosticSavePath = root;
            FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            object liveWorld = Field(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
            object world = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(liveWorld, null)!;
            Field(data, "_worldData").SetValue(data, world);
            Field(data, "_playerDataList").SetValue(data, new List<PlayerData>(AR.s.Data.PlayerDataList));
            // 플레이어 ECS 되쓰기가 라이브 PlayerData를 고치지 않도록 현재 플레이어는 fixture에 등록하지 않는다.
            Field(data, "_currentPlayerEntityId").SetValue(data, -1);
            Field(data, "_editorMainThreadId").SetValue(data, Environment.CurrentManagedThreadId);
            object dungeonManager = AR.s.Dungeon;
            FieldInfo completionSaved = Field(dungeonManager, "_savedNpcCompletionRevision");
            object? savedCompletion = completionSaved.GetValue(dungeonManager);
            float timeScale = Time.timeScale;
            SaveScope[] scopes = { SaveScope.NpcSocial, SaveScope.NpcParties, SaveScope.NpcMeta, SaveScope.WorldSim };
            string[] partNames = { "npc_social", "npc_parties", "npc_meta", "world_sim" };
            SaveDtoSnapshot copier = new();
            string Json(object? value) => JsonConvert.SerializeObject(copier.Capture(value), Formatting.None, JsonSettings.Default);
            string slotPath = Path.Combine(root, "Slot_0");
            object ReadSnapshot()
            {
                object?[] args = { slotPath, Path.Combine(slotPath, "manifest.json"), null, null };
                bool success = (bool)typeof(DataManager).GetMethod("TryLoadManifestSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(data, args)!;
                if (success == false) throw new InvalidOperationException("Temporary partition snapshot was rejected by the reader.");
                return args[3]!;
            }
            object LoadedRoot(object snapshot, int index) => snapshot.GetType().GetField(new[] { "NpcSocial", "NpcParties", "NpcMeta", "WorldSim" }[index])!.GetValue(snapshot)!;
            void Require(bool condition, string message)
            {
                if (condition == false) throw new InvalidOperationException(message);
            }
            try
            {
                Time.timeScale = 0f;
                await AR.s.Data.WaitForSaveIdleAsync();
                // 파티션 writer/reader를 그대로 쓰며 원래 DataManager의 manifest는 건드리지 않는다.
                Require(await data.SaveAsync(SaveScope.All), "Temporary initial save failed.");
                await data.WaitForSaveIdleAsync();
                object loaded = ReadSnapshot();
                SaveManifest first = JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(slotPath, "manifest.json")), JsonSettings.Default)!;
                object?[] published = { AR.s.Npc.CaptureSocialSaveSnapshotRoot(), AR.s.Npc.CapturePartySaveSnapshotRoot(),
                    AR.s.Npc.CapturePopulationSaveSnapshotRoot(), AR.s.History.CaptureSaveSnapshotRoot() };
                string[] previousJson = new string[scopes.Length];
                for (int i = 0; i < scopes.Length; i++)
                {
                    previousJson[i] = JsonConvert.SerializeObject(published[i], Formatting.None, JsonSettings.Default);
                    Require(Json(LoadedRoot(loaded, i)) == previousJson[i], partNames[i] + " save/read DTO mismatch.");
                    Require(File.ReadAllText(Path.Combine(slotPath, "parts", first.Parts[partNames[i]].FileName)) == previousJson[i], partNames[i] + " saved JSON mismatch.");
                }
                Require(await data.SaveAsync(SaveScope.NpcSocial | SaveScope.NpcParties | SaveScope.NpcMeta | SaveScope.WorldSim), "Temporary partial save failed.");
                await data.WaitForSaveIdleAsync();
                loaded = ReadSnapshot();
                SaveManifest second = JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(slotPath, "manifest.json")), JsonSettings.Default)!;
                Require(second.Revision == first.Revision + 1, "Manifest revision did not advance.");
                for (int i = 0; i < scopes.Length; i++)
                {
                    Require(second.Parts[partNames[i]].Sha256 == first.Parts[partNames[i]].Sha256, partNames[i] + " unchanged state has different SHA-256.");
                    Require(second.Parts[partNames[i]].FileName == first.Parts[partNames[i]].FileName, partNames[i] + " unchanged file was rewritten.");
                    Require(Json(LoadedRoot(loaded, i)) == previousJson[i], partNames[i] + " second read DTO mismatch.");
                    Require(JsonConvert.SerializeObject(published[i], Formatting.None, JsonSettings.Default) == previousJson[i], partNames[i] + " previously published DTO changed.");
                }
                string incremental = await VerifyIncrementalSaveContractsAsync();
                return "[SimulationSaveRoundTrip] PASS 4/4: actual writer/reader, same JSON, SHA-256 and file references, previous DTOs unchanged.\n" + incremental;
            }
            finally
            {
                try { await data.WaitForSaveIdleAsync(); }
                finally
                {
                    completionSaved.SetValue(dungeonManager, savedCompletion);
                    Time.timeScale = timeScale;
                    UnityEngine.Object.DestroyImmediate(owner);
                    if (Directory.Exists(root) == true) Directory.Delete(root, true);
                    if (createdParent == true && Directory.Exists(tempParent) == true && Directory.GetFileSystemEntries(tempParent).Length == 0)
                        Directory.Delete(tempParent);
                }
            }
        }

        /// <summary>기존 왕복 메뉴에서 실행하는 묶음·증분·실패 계약. 플레이를 잠시 멈추고 원장/캐시를 모두 격리한다.</summary>
        private static async Task<string> VerifyIncrementalSaveContractsAsync()
        {
            string parent = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SaveSnapshotDiagnostics"));
            string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            GameObject owner = new("Incremental save diagnostics") { hideFlags = HideFlags.HideAndDontSave };
            DataManager data = owner.AddComponent<DataManager>();
            NpcManager npc = owner.AddComponent<NpcManager>();
            VillageManager village = owner.AddComponent<VillageManager>();
            History.HistoryManager history = owner.AddComponent<History.HistoryManager>();
            List<(object Target, FieldInfo Field, object? Original)> swaps = new();
            FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            void Swap(object target, string name, object value)
            {
                FieldInfo field = Field(target, name);
                swaps.Add((target, field, field.GetValue(target)));
                field.SetValue(target, value);
            }
            int checks = 0;
            void Require(bool condition, string label)
            {
                checks++;
                if (condition == false) throw new InvalidOperationException("[IncrementalSaveContracts] " + label);
            }
            bool paused = EditorApplication.isPaused;
            ILogHandler originalLog = Debug.unityLogger.logHandler;
            string slot = Path.Combine(root, "Slot_0");
            SaveManifest Manifest() => JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(slot, "manifest.json")), JsonSettings.Default)!;
            object Read(string manifest = "manifest.json")
            {
                object?[] args = { slot, Path.Combine(slot, manifest), null, null };
                Require((bool)typeof(DataManager).GetMethod("TryLoadManifestSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(data, args)!, "manifest reader rejected snapshot");
                return args[3]!;
            }
            T Part<T>(object loaded, string name) => (T)loaded.GetType().GetField(name)!.GetValue(loaded)!;
            void ChangedFiles(SaveManifest before, SaveManifest after, params string[] changed)
            {
                HashSet<string> expected = new(changed);
                Require(after.Revision == before.Revision + 1, "manifest revision advances once");
                foreach (var pair in after.Parts)
                {
                    SavePartReference prior = before.Parts[pair.Key];
                    Require((pair.Value.FileName != prior.FileName) == expected.Contains(pair.Key), "unexpected file change: " + pair.Key);
                    if (expected.Contains(pair.Key) == false)
                        Require(pair.Value.Revision == prior.Revision && pair.Value.Sha256 == prior.Sha256 && pair.Value.Length == prior.Length,
                            "reused reference changed: " + pair.Key);
                }
                Require(data.DiagnosticWrittenPartCount == expected.Count, "new file count");
                Read();
            }
            async Task Save(SaveScope scope)
            {
                Require(await data.SaveAsync(scope), "save failed: " + scope);
                await data.WaitForSaveIdleAsync();
                Require((scope & SaveScope.Simulation) == 0 || (data.DiagnosticCommittedScope & SaveScope.Simulation) == SaveScope.Simulation, "all six simulation parts included");
            }
            WorldData world = new();
            var a = new NpcSaveData(0, Vector2.zero) { Gold = 10 };
            var b = new NpcSaveData(0, Vector2.one) { Gold = 20 };
            world.NpcSaveDatas.Add(32, a); world.NpcSaveDatas.Add(33, b);
            var v = new VillageData(42, Vector2.zero);
            world.VillageDatas.Add(v);
            var party = new NpcPartyState { PartyId = 7, LeaderId = 32, MemberIds = new List<int> { 32, 33 } };
            world.NpcPartyStates.Add(party);
            world.NpcAffinityList.Add(new NpcAffinityEntry(32, 33, 10));
            world.WorldSim.Settlements = new[] { new History.SettlementLiveState { SettlementId = 42, VillageId = -1, Pop = 10 } };
            void Flush()
            {
                npc.FlushSaveSnapshot(); village.FlushSaveSnapshot();
                npc.RefreshSimulationSaveSnapshots(SaveScope.Simulation); history.FlushSaveSnapshot();
            }
            void ChangeBundle()
            {
                npc.MarkSaveDirty(32); a.Gold++;
                village.MarkSaveDirty(42); v.ShopTier++;
                npc.MarkPartySaveDirty(7); party.Activity.TargetKey += "x";
                npc.ModifyAffinity(32, 33, 1);
                npc.MarkPopulationSaveDirty(); world.NpcPopulation.TargetNpcCount++;
                history.MarkSaveDirty(0); world.WorldSim.Settlements[0].Pop++;
                Flush(); // LateUpdate가 목록을 비운 뒤에도 디스크 미확정 변경을 보존해야 한다.
            }
            string[] bundleFiles = { "npc_state_00", "villages", "npc_social", "npc_parties", "npc_meta", "world_sim" };
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                EditorApplication.isPaused = true;
                data.DiagnosticSavePath = root;
                Field(data, "_worldData").SetValue(data, world);
                Field(data, "_editorMainThreadId").SetValue(data, Environment.CurrentManagedThreadId);
                // RequiresCheckpoint는 Data.Player를 읽는다. 빈 목록이면 테이블 초기화를 시도하므로 fixture 플레이어를 둔다.
                Field(data, "_playerDataList").SetValue(data, new List<PlayerData> { new PlayerData { PlayerId = 1 } });
                Field(data, "_currentPlayerEntityId").SetValue(data, -1);
                Swap(AR.s, "_dataManager", data); Swap(AR.s, "_npcManager", npc);
                Swap(AR.s, "_villageManager", village); Swap(AR.s, "_historyManager", history);
                Swap(AR.s.Dungeon, "_npcCompletionRevision", 0L); Swap(AR.s.Dungeon, "_savedNpcCompletionRevision", 0L);
                Field(npc, "_npcSaveDict").SetValue(npc, world.NpcSaveDatas);
                Field(npc, "_npcPartyDict").SetValue(npc, new Dictionary<int, NpcPartyState> { { 7, party } });
                Field(npc, "_simulationPartyIds").SetValue(npc, new List<int> { 7 });
                Field(village, "_villages").SetValue(village, new Dictionary<int, VillageData> { { 42, v } });
                Field(village, "_villageList").SetValue(village, world.VillageDatas);
                npc.LoadAffinity(world.NpcAffinityList);
                npc.RebuildSaveSnapshot(); village.RebuildSaveSnapshot();
                npc.RefreshSimulationSaveSnapshots(SaveScope.Simulation, rebuild: true); history.RebuildSaveSnapshot();
                // 최초/이관의 원장 경로와 런타임 캐시 경로 사이에서도 동일한 파일은 재사용한다.
                data.DiagnosticSkipRuntimeCapture = true;
                await Save(SaveScope.All);
                SaveManifest before = Manifest();
                data.DiagnosticSkipRuntimeCapture = false;
                await Save(SaveScope.Villages);
                ChangedFiles(before, Manifest()); before = Manifest();
                SaveScope[] scopes = { SaveScope.Villages, SaveScope.WorldSim, SaveScope.NpcMeta, SaveScope.NpcState, SaveScope.NpcSocial, SaveScope.NpcParties };
                for (int i = 0; i < scopes.Length; i++)
                {
                    await Save(scopes[i]);
                    ChangedFiles(before, Manifest()); before = Manifest();
                    Require(data.DiagnosticPreparedPartCount == 0, "unchanged simulation skips serialization");
                }
                await Save(SaveScope.Player | SaveScope.World);
                Require((data.DiagnosticCommittedScope & SaveScope.Simulation) == 0, "player-only request keeps scope");
                before = Manifest();
                npc.MarkSaveDirty(32); a.Gold++; Flush();
                await Save(SaveScope.Villages);
                ChangedFiles(before, Manifest(), "npc_state_00"); before = Manifest();
                Require(Part<Dictionary<int, NpcSaveData>>(Read(), "Npcs")[32].Gold == a.Gold, "changed NPC survives reader");
                village.MarkSaveDirty(42); v.ShopTier++; Flush();
                await Save(SaveScope.NpcMeta);
                ChangedFiles(before, Manifest(), "villages"); before = Manifest();
                npc.MarkSaveDirty(34); world.NpcSaveDatas.Add(34, new NpcSaveData(0, Vector2.zero)); Flush();
                await Save(SaveScope.WorldSim);
                ChangedFiles(before, Manifest(), "npc_state_02"); before = Manifest();
                npc.MarkSaveDirty(33); world.NpcSaveDatas.Remove(33); Flush();
                await Save(SaveScope.NpcParties);
                ChangedFiles(before, Manifest(), "npc_state_01"); before = Manifest();
                Require(Part<Dictionary<int, NpcSaveData>>(Read(), "Npcs").ContainsKey(33) == false, "empty shard persists deletion");

                // 전수 재생성은 세대를 바꾸지만 같은 JSON이면 새 파일 수는 0이다.
                Require(await data.SaveDungeonTransactionAsync(SaveScope.Simulation), "rebuild transaction");
                await data.WaitForSaveIdleAsync(); ChangedFiles(before, Manifest()); before = Manifest();
                npc.MarkSaveDirty(32); Flush(); // 변경 전 입구 표시/되쓰기가 아무 값도 바꾸지 않은 경우.
                await Save(SaveScope.NpcState); ChangedFiles(before, Manifest()); before = Manifest();

                // 미저장 NPC 던전 완료는 묶음 요청에서 슬롯과 보상을 함께 확정한다.
                Field(AR.s.Dungeon, "_npcCompletionRevision").SetValue(AR.s.Dungeon, 1L);
                world.Dungeons.CompletedCount++;
                npc.MarkSaveDirty(32); a.Gold++; Flush();
                data.DiagnosticAfterCapture = () =>
                {
                    data.DiagnosticAfterCapture = null;
                    Field(AR.s.Dungeon, "_npcCompletionRevision").SetValue(AR.s.Dungeon, 2L);
                    world.Dungeons.CompletedCount++;
                    npc.MarkSaveDirty(32); a.Gold++; Flush();
                };
                await Save(SaveScope.Villages);
                Require((data.DiagnosticCommittedScope & SaveScope.Dungeons) != 0 && AR.s.Dungeon.HasUnsavedNpcCompletion == true,
                    "capture only acknowledges its dungeon completion revision");
                ChangedFiles(before, Manifest(), "npc_state_00", "dungeons"); before = Manifest();
                await Save(SaveScope.WorldSim);
                Require(AR.s.Dungeon.HasUnsavedNpcCompletion == false, "later completion committed with simulation bundle");
                ChangedFiles(before, Manifest(), "npc_state_00", "dungeons"); before = Manifest();

                int capturedGold = a.Gold;
                int capturedTier = v.ShopTier;
                float capturedPop = world.WorldSim.Settlements[0].Pop;
                Task<bool>? queued = null;
                data.DiagnosticAfterCapture = () =>
                {
                    data.DiagnosticAfterCapture = null;
                    ChangeBundle();
                    queued = data.SaveAsync(SaveScope.Villages);
                };
                Require(await data.SaveAsync(SaveScope.NpcSocial), "first batch success");
                Require(queued != null && await queued, "queued batch success");
                await data.WaitForSaveIdleAsync();
                object priorSnapshot = Read("manifest.prev.json");
                object latest = Read();
                Require(Part<Dictionary<int, NpcSaveData>>(priorSnapshot, "Npcs")[32].Gold == capturedGold &&
                    Part<VillageSaveRoot>(priorSnapshot, "Villages").Villages[0].ShopTier == capturedTier &&
                    Part<WorldSimSaveRoot>(priorSnapshot, "WorldSim").WorldSim.Settlements[0].Pop == capturedPop,
                    "in-flight snapshot is immutable");
                Require(Part<Dictionary<int, NpcSaveData>>(latest, "Npcs")[32].Gold == a.Gold &&
                    Part<VillageSaveRoot>(latest, "Villages").Villages[0].ShopTier == v.ShopTier &&
                    Part<WorldSimSaveRoot>(latest, "WorldSim").WorldSim.Settlements[0].Pop == world.WorldSim.Settlements[0].Pop,
                    "queued save includes later changes");
                Require(Part<NpcMetaSaveRoot>(latest, "NpcMeta").Population.TargetNpcCount == world.NpcPopulation.TargetNpcCount &&
                    Part<NpcPartySaveRoot>(latest, "NpcParties").Parties[0].Activity.TargetKey == party.Activity.TargetKey &&
                    Part<NpcSocialSaveRoot>(latest, "NpcSocial").Affinities[0].Affinity == npc.GetAffinity(32, 33),
                    "queued social/party/population state agrees with the other three parts");
                Require(Manifest().Revision == before.Revision + 2, "queued revisions");
                before = JsonConvert.DeserializeObject<SaveManifest>(File.ReadAllText(Path.Combine(slot, "manifest.prev.json")), JsonSettings.Default)!;
                ChangedFiles(before, Manifest(), bundleFiles); before = Manifest();

                Debug.unityLogger.logHandler = new ExpectedPartitionFailureLogHandler(originalLog);
                string[] failures = { "serialize", "part-write", "after-part", "before-manifest" };
                for (int i = 0; i < failures.Length; i++)
                {
                    ChangeBundle();
                    int duringFailure = a.Gold;
                    string committed = File.ReadAllText(Path.Combine(slot, "manifest.json"));
                    data.DiagnosticFailurePoint = failures[i];
                    data.DiagnosticAfterCapture = () => { data.DiagnosticAfterCapture = null; ChangeBundle(); };
                    Require(await data.SaveAsync(SaveScope.Villages) == false, "injected failure: " + failures[i]);
                    await data.WaitForSaveIdleAsync();
                    Require(File.ReadAllText(Path.Combine(slot, "manifest.json")) == committed, "failed batch preserves whole manifest");
                    Read();
                    data.DiagnosticFailurePoint = null;
                    await Save(SaveScope.NpcMeta);
                    ChangedFiles(before, Manifest(), bundleFiles); before = Manifest();
                    Require(Part<Dictionary<int, NpcSaveData>>(Read(), "Npcs")[32].Gold == duringFailure + 1, "failed and later changes survive retry");
                }
                Debug.unityLogger.logHandler = originalLog;
                // Reset 직후 이전 writer가 세대를 확정할 수 없어야 한다. 취소 뒤 마지막 manifest를 다시 읽어 재시도한다.
                ChangeBundle();
                data.DiagnosticBeforeCommit = () =>
                {
                    typeof(DataManager).GetMethod("ResetPartitionedSaveState", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(data, null);
                    return Task.CompletedTask;
                };
                Require(await data.SaveAsync(SaveScope.Villages) == false, "Reset cancels stale writer");
                await data.WaitForSaveIdleAsync(); data.DiagnosticBeforeCommit = null;
                Require(Manifest().Revision == before.Revision, "Reset leaves disk manifest");
                Field(data, "_activeSaveManifest").SetValue(data, Manifest());
                await Save(SaveScope.Villages); ChangedFiles(before, Manifest(), bundleFiles); before = Manifest();
                // 이전 manifest가 재사용 파일을 공유해도 하나의 전체 상태로 복구한다.
                npc.MarkSaveDirty(32); a.Gold++; Flush();
                await Save(SaveScope.NpcState);
                File.WriteAllText(Path.Combine(slot, "parts", Manifest().Parts["npc_state_00"].FileName), "corrupt");
                object?[] corruptArgs = { slot, Path.Combine(slot, "manifest.json"), null, null };
                Require((bool)typeof(DataManager).GetMethod("TryLoadManifestSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(data, corruptArgs)! == false,
                    "corrupt active snapshot rejected as a whole");
                // 로드 후 실체화/전역 EntityId 예약은 실행하지 않는다. 같은 loader의 reader로 previous 전체를 검증한다.
                object recovered = Read("manifest.prev.json");
                Require(Part<Dictionary<int, NpcSaveData>>(recovered, "Npcs")[32].Gold == a.Gold - 1 &&
                    Part<VillageSaveRoot>(recovered, "Villages").Villages[0].ShopTier == v.ShopTier &&
                    Part<NpcMetaSaveRoot>(recovered, "NpcMeta").Population.TargetNpcCount == world.NpcPopulation.TargetNpcCount,
                    "previous whole snapshot remains readable with shared file references");
                return $"[IncrementalSaveContracts] PASS {checks} checks: six-part bundle, unchanged references, changed shards/parts, deletion, rebuild, queued mutations, four failures/retry, Reset and previous fallback. Isolated files removed.";
            }
            finally
            {
                data.DiagnosticAfterCapture = null; data.DiagnosticBeforeCommit = null;
                try { await data.WaitForSaveIdleAsync(); }
                finally
                {
                    Debug.unityLogger.logHandler = originalLog;
                    for (int i = swaps.Count - 1; i >= 0; i--) swaps[i].Field.SetValue(swaps[i].Target, swaps[i].Original);
                    EditorApplication.isPaused = paused;
                    UnityEngine.Object.DestroyImmediate(owner);
                    if (Directory.Exists(root) == true) Directory.Delete(root, true);
                }
            }
        }

        private sealed class ExpectedPartitionFailureLogHandler : ILogHandler
        {
            private readonly ILogHandler _original;
            public ExpectedPartitionFailureLogHandler(ILogHandler original) => _original = original;
            public void LogFormat(LogType type, UnityEngine.Object? context, string format, params object[] args)
            {
                if (type == LogType.Error && string.Format(format, args).StartsWith("[DataManager] Partitioned save failed:", StringComparison.Ordinal)) return;
                _original.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, UnityEngine.Object? context) => _original.LogException(exception, context);
        }

        [MenuItem("ARPG/Save/Diagnostics/Validate Simulation Save Snapshots")]
        public static void ValidateSimulation()
        {
            if (Application.isPlaying == false || AR.s == null || AR.s.IsInitialized == false ||
                NpcManager.SimulationSaveSnapshotEnabled == false || History.HistoryManager.SaveSnapshotEnabled == false)
            {
                Debug.LogError("[SimulationSaveSnapshot] 스냅샷이 켜진 GameScene 플레이 모드에서 실행하세요.");
                return;
            }
            int mismatches = AR.s.Npc.ValidateSimulationSaveSnapshots() + AR.s.History.ValidateSaveSnapshot();
            SaveScope[] scopes = { SaveScope.NpcSocial, SaveScope.NpcParties, SaveScope.NpcMeta, SaveScope.WorldSim };
            int equal = 0;
            for (int i = 0; i < scopes.Length; i++)
            {
                string fresh = AR.s.Data.DiagnosticCaptureSimulationJson(scopes[i], useSnapshot: false);
                string cached = AR.s.Data.DiagnosticCaptureSimulationJson(scopes[i], useSnapshot: true);
                if (fresh == cached) equal++;
                else Debug.LogError($"[SimulationSaveSnapshot] {scopes[i]}: 기존 JSON 불일치");
            }
            string result = $"[SimulationSaveSnapshot] 누락={mismatches} 경로동등성={equal}/{scopes.Length}";
            if (mismatches == 0 && equal == scopes.Length) Debug.Log(result + " PASS");
            else Debug.LogError(result + " FAIL");
        }

        [MenuItem("ARPG/Save/Diagnostics/Verify Simulation Snapshot Contracts")]
        public static void VerifySimulationSnapshotContracts() => Debug.Log(VerifySimulationContracts());

        /// <summary>격리된 원장·캐시로 실패 격리, 불변성, 삭제, 수명, 사회·파티 경로를 검사한다.</summary>
        public static string VerifySimulationContracts()
        {
            StringBuilder report = new("[SimulationSnapshotContracts]\n");
            int checks = 0;
            int failures = 0;
            void Check(bool passed, string label)
            {
                checks++;
                if (passed == false) failures++;
                report.AppendLine((passed ? "PASS " : "FAIL ") + label);
            }
            string Json(object? value) => JsonConvert.SerializeObject(value, Formatting.None, JsonSettings.Default);
            SaveDtoSnapshot copier = new();
            var cache = new SaveSnapshotCache<int>("SimulationContract");
            Dictionary<int, object?> live = new();
            object? Source(int id) => live.TryGetValue(id, out object? value) == true ? value : null;
            List<string> fields = new();
            NpcPartyState a = new() { PartyId = 42, LeaderId = 3, MemberIds = new List<int> { 3, 9 } };
            NpcPartyState b = new() { PartyId = 7, LeaderId = 8, MemberIds = new List<int> { 8, 10 } };
            live.Add(42, a); live.Add(7, b);
            cache.Rebuild(live.Keys, Source);
            object? old = cache.Get(42);
            string before = Json(old);
            Check(before == Json(copier.Capture(a)), "복사기와 같은 DTO·중첩 목록");
            a.Activity.TargetKey = "unmarked";
            Check(cache.TryFindUnmarkedChange(42, Source, fields) == true && fields.Contains(nameof(NpcPartyState.Activity)), "표시 누락·항목 이름 검출");
            Check(cache.TryFindUnmarkedChange(42, Source, fields) == false, "같은 누락 중복 집계 방지");
            int observed = 0;
            cache.DirtyObserver = id =>
            {
                if (cache.TryFindUnmarkedChange(id, Source, fields) == true) observed++;
            };
            a.Activity.TargetKey = "prior omission";
            cache.MarkDirty(42); cache.MarkDirty(42);
            Check(observed == 1, "다음 정상 표시가 이전 누락을 가리기 전 검사");
            cache.DirtyObserver = null;
            a.MemberIds.Remove(9);
            a.Activity.TargetKey = "marked";
            Check(cache.TryFindUnmarkedChange(42, Source, fields) == false, "표시된 변경 제외");
            cache.Flush(Source, recordFrameTiming: true);
            Check(cache.FlushCount == 1 && Json(cache.Get(42)) == Json(copier.Capture(a)), "중복 표시 1회·프레임 측정·최종 상태 캡처");
            Check(Json(old) == before, "이전 저장의 중첩 DTO 불변");
            Check(cache.Validate(live.Keys, Source) == 0, "전수 비교 동일");

            SnapshotFaultDto bad = new();
            bad.Cycle = bad;
            live.Add(99, bad);
            cache.MarkDirty(99); cache.MarkDirty(7);
            b.LeaderId = 10;
            ILogHandler original = Debug.unityLogger.logHandler;
            ExpectedSimulationFailureLogHandler expected = new(original);
            try
            {
                Debug.unityLogger.logHandler = expected;
                cache.Flush(Source);
                Check(Json(cache.Get(7)) == Json(copier.Capture(b)), "실패 항목 격리·다른 항목 계속");
                cache.Flush(Source, recordFrameTiming: true);
                Check(cache.FlushCount == 1, "실패 후 변경 목록 비움");
                bool blocked = false;
                try { cache.PrepareForSave(Source); }
                catch (InvalidOperationException) { blocked = true; }
                Check(blocked == true && expected.Count == 1, "실패 저장 중단·반복 로그 1회");
                bad.Cycle = null;
                cache.PrepareForSave(Source);
                Check(Json(cache.Get(99)) == Json(copier.Capture(bad)), "새 표시 없이 저장 직전 재시도 복구");
                bad.Cycle = bad;
                cache.MarkDirty(99);
                cache.Flush(Source);
                live.Remove(99);
                cache.PrepareForSave(Source);
                Check(cache.Validate(live.Keys, Source) == 0, "실패 항목 삭제 뒤 저장 복구");
            }
            finally { Debug.unityLogger.logHandler = original; }
            live.Remove(7); cache.MarkDirty(7); cache.Flush(Source);
            bool missing = false;
            try { cache.Get(7); }
            catch (InvalidOperationException) { missing = true; }
            Check(missing == true, "삭제한 DTO 제거·없는 항목 저장 거부");
            a.LeaderId++;
            cache.Rebuild(live.Keys, Source);
            Check(Json(cache.Get(42)) == Json(copier.Capture(a)), "전체 재생성으로 누락 복구");
            cache.MarkDirty(42); cache.DirtyObserver = id => { };
            cache.Reset();
            Check(cache.FlushCount == 0 && cache.DirtyObserver == null, "Reset 측정·진단 관찰자 정리");
            cache.Rebuild(live.Keys, Source);
            Check(cache.Validate(live.Keys, Source) == 0 && Json(old) == before, "Reset 뒤 로드 재생성·이전 DTO 보존");

            GameObject owner = new("Simulation snapshot diagnostics") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            NpcManager manager = owner.AddComponent<NpcManager>();
            object Field(string name) => typeof(NpcManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            void FlushSocial()
            {
                manager.RefreshSimulationSaveSnapshots(SaveScope.NpcSocial | SaveScope.NpcParties);
            }
            string FreshSocial() => Json(copier.Capture(new NpcSocialSaveRoot
                { Affinities = manager.SaveAffinity(), Relations = manager.SaveRelations() }));
            try
            {
                FlushSocial();
                Check(Json(manager.CaptureSocialSaveSnapshotRoot()) == FreshSocial(), "빈 사회 루트");
                Check(Json(manager.CapturePartySaveSnapshotRoot()) == Json(copier.Capture(new NpcPartySaveRoot())), "빈 파티 루트");
                manager.SetAffinity(9, 3, 15);
                manager.SetAffinity(8, 2, 20);
                manager.AddRelation(9, 3, NpcRelationType.GrudgeAgainst, 4);
                manager.AddRelation(3, 9, NpcRelationType.GrudgeAgainst, 5);
                manager.AddRelation(8, 2, NpcRelationType.Friend);
                FlushSocial();
                object? social = manager.CaptureSocialSaveSnapshotRoot();
                string socialBefore = Json(social);
                Check(socialBefore == FreshSocial(), "사회 목록 순서·방향 원한·양방향 중복 제외");
                manager.AddRelation(2, 8, NpcRelationType.Friend);
                manager.ModifyAffinity(9, 3, 5);
                FlushSocial();
                Check(Json(manager.CaptureSocialSaveSnapshotRoot()) == FreshSocial() && Json(social) == socialBefore, "변경한 호감도만 복사·사회 DTO 불변");
                var affinities = (Dictionary<NpcPairKey, NpcAffinityEntry>)Field("_npcAffinity");
                affinities[new NpcPairKey(9, 3)].SharedActivities = new NpcSharedActivityStats { HuntCount = 1 };
                Check(manager.TryFindUnmarkedAffinitySaveChange(new NpcPairKey(9, 3), fields) == true && fields.Contains(nameof(NpcAffinityEntry.SharedActivities)), "공동 활동 통계 누락 검출");
                // 제거 경로에서는 저장된 통계의 사망 기억 보관을 별도 진단이 맡는다.
                manager.MarkAffinitySaveDirty(9, 3);
                affinities[new NpcPairKey(9, 3)].SharedActivities = null;
                FlushSocial();
                manager.RemoveAllAffinityInvolving(9);
                manager.RemoveAllRelationsInvolving(3);
                FlushSocial();
                Check(Json(manager.CaptureSocialSaveSnapshotRoot()) == FreshSocial(), "호감도·양쪽 방향 관계 삭제");
                manager.LoadAffinity(new List<NpcAffinityEntry> { new(4, 5, 7) });
                manager.LoadRelations(new List<NpcRelationEntry> { new(5, 4, NpcRelationType.Spouse) });
                FlushSocial();
                Check(Json(manager.CaptureSocialSaveSnapshotRoot()) == FreshSocial(), "사회 로드에서 이전 캐시 정리");

                var npcs = (Dictionary<int, NpcSaveData>)Field("_npcSaveDict");
                var ids = (List<int>)Field("_simulationNpcIds");
                npcs.Add(3, new NpcSaveData(0, Vector2.zero)); npcs.Add(9, new NpcSaveData(0, Vector2.zero));
                ids.Add(3); ids.Add(9);
                NpcPartyState party = new() { PartyId = 42, LeaderId = 9, MemberIds = new List<int> { 9, 3, 3, 123 } };
                manager.LoadParties(new List<NpcPartyState> { party });
                FlushSocial();
                object? partyRoot = manager.CapturePartySaveSnapshotRoot();
                string partyBefore = Json(partyRoot);
                Check(partyBefore == Json(copier.Capture(new NpcPartySaveRoot { Parties = manager.SaveParties() })) &&
                    party.MemberIds.Count == 2 && party.MemberIds[0] == 3, "파티 로드 보정 뒤 캡처·멤버 순서");
                manager.MarkPartySaveDirty(42);
                party.Activity.TargetKey = "changed path";
                FlushSocial();
                Check(Json(partyRoot) == partyBefore && Json(manager.CapturePartySaveSnapshotRoot()) ==
                    Json(copier.Capture(new NpcPartySaveRoot { Parties = manager.SaveParties() })), "파티 활동·경로 변경과 이전 DTO 불변");
                manager.DissolveParty(42); FlushSocial();
                Check(Json(manager.CapturePartySaveSnapshotRoot()) == Json(copier.Capture(new NpcPartySaveRoot())), "해산한 파티 저장에서 제거");
                typeof(NpcManager).GetMethod("ResetSimulationSaveSnapshots", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, null);
                manager.RefreshSimulationSaveSnapshots(SaveScope.NpcSocial | SaveScope.NpcParties, rebuild: true);
                Check(Json(manager.CaptureSocialSaveSnapshotRoot()) == FreshSocial(), "소유 매니저 Reset 뒤 재생성");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Random.state = randomState;
            }
            report.Append($"{checks - failures}/{checks} PASS, failures={failures}");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            return report.ToString();
        }

        private sealed class SnapshotFaultDto
        {
            public SnapshotFaultDto? Cycle;
        }

        private sealed class ExpectedSimulationFailureLogHandler : ILogHandler
        {
            private readonly ILogHandler _original;
            public int Count { get; private set; }
            public ExpectedSimulationFailureLogHandler(ILogHandler original) => _original = original;
            public void LogFormat(LogType type, UnityEngine.Object? context, string format, params object[] args)
            {
                string message = string.Format(format, args);
                if (type == LogType.Error && message.StartsWith("[SimulationContractSaveSnapshot]", StringComparison.Ordinal)) Count++;
                else _original.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, UnityEngine.Object? context) => _original.LogException(exception, context);
        }

        [MenuItem("ARPG/Save/Diagnostics/Validate Npc Save Snapshot")]
        public static void Validate()
        {
            if (Application.isPlaying == false || AR.s == null || AR.s.IsInitialized == false ||
                NpcManager.SaveSnapshotEnabled == false)
            {
                Debug.LogError("[NpcSaveSnapshot] 스냅샷이 켜진 GameScene 플레이 모드에서 실행하세요.");
                return;
            }
            int mismatches = AR.s.Npc.ValidateSaveSnapshot();
            string[] fresh = AR.s.Data.DiagnosticCaptureNpcShardJson(useSnapshot: false);
            string[] cached = AR.s.Data.DiagnosticCaptureNpcShardJson(useSnapshot: true);
            int shardMismatches = 0;
            for (int i = 0; i < fresh.Length; i++)
            {
                if (string.Equals(fresh[i], cached[i], StringComparison.Ordinal) == true) continue;
                if (string.Equals(StripDerivedPositions(fresh[i]), StripDerivedPositions(cached[i]),
                        StringComparison.Ordinal) == true) continue;
                Debug.LogError($"[NpcSaveSnapshot] shard {i:D2}: 기존 캡처와 JSON 바이트 불일치");
                shardMismatches++;
            }
            string result = $"[NpcSaveSnapshot] NPC={AR.s.Npc.SaveDataView.Count} 누락={mismatches} " +
                $"경로동등성={fresh.Length - shardMismatches}/{fresh.Length} " +
                $"프레임정리 평균={AR.s.Npc.SnapshotFlushAverageMs:F3}ms 최대={AR.s.Npc.SnapshotFlushMaxMs:F3}ms";
            if (mismatches == 0 && shardMismatches == 0) Debug.Log(result + " PASS");
            else Debug.LogError(result + " FAIL");
        }

        [MenuItem("ARPG/Save/Diagnostics/Validate Village Save Snapshot")]
        public static void ValidateVillages()
        {
            if (Application.isPlaying == false || AR.s == null || AR.s.IsInitialized == false ||
                VillageManager.SaveSnapshotEnabled == false)
            {
                Debug.LogError("[VillageSaveSnapshot] 스냅샷이 켜진 GameScene 플레이 모드에서 실행하세요.");
                return;
            }
            // ECS 되쓰기가 먼저 표시해 버리면 원장 누락이 가려지므로 전수 검사는 먼저 한다.
            int mismatches = AR.s.Village.ValidateSaveSnapshot();
            string fresh = AR.s.Data.DiagnosticCaptureVillageJson(useSnapshot: false);
            string cached = AR.s.Data.DiagnosticCaptureVillageJson(useSnapshot: true);
            bool equal = string.Equals(fresh, cached, StringComparison.Ordinal);
            string result = $"[VillageSaveSnapshot] 마을={AR.s.Village.GetVillageCount()} 누락={mismatches} " +
                $"경로동등성={equal} 프레임정리 평균={AR.s.Village.SnapshotFlushAverageMs:F3}ms " +
                $"최대={AR.s.Village.SnapshotFlushMaxMs:F3}ms";
            if (mismatches == 0 && equal == true) Debug.Log(result + " PASS");
            else Debug.LogError(result + " FAIL");
        }

        [MenuItem("ARPG/Save/Diagnostics/Verify Village Snapshot Contracts")]
        public static void VerifyVillageSnapshotContracts() => Debug.Log(VerifyVillageContracts());

        /// <summary>격리된 원장으로 순서·불변성·표시·실패 격리·수명을 검사한다. 실제 저장은 쓰지 않는다.</summary>
        public static string VerifyVillageContracts()
        {
            GameObject owner = new GameObject("Village snapshot diagnostics");
            VillageManager manager = owner.AddComponent<VillageManager>();
            SaveDtoSnapshot copier = new SaveDtoSnapshot();
            List<string> fields = new List<string>();
            StringBuilder report = new StringBuilder("[VillageSnapshotContracts]\n");
            int checks = 0;
            int failures = 0;
            void Check(bool passed, string label)
            {
                checks++;
                if (passed == false) failures++;
                report.AppendLine((passed ? "PASS " : "FAIL ") + label);
            }
            string Json(object? value) => JsonConvert.SerializeObject(value, Formatting.None, JsonSettings.Default);
            object Field(string name) => typeof(VillageManager).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            void Add(VillageData village) => typeof(VillageManager).GetMethod("AddVillage",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, new object[] { village });
            try
            {
                manager.RebuildSaveSnapshot();
                Check(Json(manager.CaptureSaveSnapshotRoot()) == Json(copier.Capture(new VillageSaveRoot())), "빈 루트");
                VillageData a = new VillageData(42, Vector2.one);
                VillageData b = new VillageData(7, Vector2.zero);
                a.MerchantStock.Add(new MerchantStockEntry { RemainingCount = 3, NpcBuyerName = "before" });
                Add(a); Add(b);
                manager.FlushSaveSnapshot();
                object? oldRoot = manager.CaptureSaveSnapshotRoot();
                string oldJson = Json(oldRoot);
                Check(oldJson == Json(copier.Capture(new VillageSaveRoot { Villages = manager.Save() })), "기존 JSON·목록 순서 동일");
                a.NpcEntityIds.Add(123);
                a.EntityId = 123;
                Check(manager.TryFindUnmarkedSaveChange(a.VillageId, fields) == false, "JsonIgnore 런타임 필드 제외");
                a.EntityId = -1;
                a.ShopTier++;
                Check(manager.TryFindUnmarkedSaveChange(a.VillageId, fields) == true && fields.Contains(nameof(VillageData.ShopTier)), "표시 누락·항목 이름 검출");
                int observedOmissions = 0;
                typeof(VillageManager).GetField("_snapshotDirtyObserver", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(manager, new Action<int>(id =>
                    {
                        if (manager.TryFindUnmarkedSaveChange(id, fields) == true) observedOmissions++;
                    }));
                a.ShopTier++;
                manager.MarkSaveDirty(a.VillageId);
                manager.MarkSaveDirty(a.VillageId);
                Check(observedOmissions == 1, "다음 정상 표시 전에 미표시 변경 검출");
                typeof(VillageManager).GetField("_snapshotDirtyObserver", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(manager, null);
                Check(((List<int>)Field("_dirtyVillageOrder")).Count == 1, "중복 표시 1회");
                a.MerchantStock[0].RemainingCount--;
                a.MerchantStock[0].NpcBuyerName = "after";
                a.Resources[GlobalEnum.ItemType.Gold] = 100;
                manager.FlushSaveSnapshot(recordFrameTiming: true);
                Check(Json(oldRoot) == oldJson, "게시한 루트·중첩 DTO 불변");
                Check(Json(manager.CaptureSaveSnapshotRoot()) == Json(copier.Capture(new VillageSaveRoot { Villages = manager.Save() })), "변경한 마을만 복사한 JSON 동일");
                Check(manager.SnapshotFlushCount == 1, "프레임 측정");
                manager.DepositNpcGold(a.VillageId, 5);
                Check(manager.TryFindUnmarkedSaveChange(a.VillageId, fields) == false, "입금 경로 표시");
                manager.FlushSaveSnapshot();
                Check(manager.ValidateSaveSnapshot() == 0, "입금 후 전수 비교");

                FaultVillageData bad = new FaultVillageData(99);
                bad.Cycle = bad;
                Add(bad);
                ILogHandler original = Debug.unityLogger.logHandler;
                ExpectedVillageFailureLogHandler expected = new ExpectedVillageFailureLogHandler(original);
                try
                {
                    Debug.unityLogger.logHandler = expected;
                    manager.MarkSaveDirty(b.VillageId);
                    b.ShopTier = 2;
                    manager.FlushSaveSnapshot();
                    Check(((List<int>)Field("_dirtyVillageOrder")).Count == 0 &&
                        Json(((Dictionary<int, object?>)Field("_saveSnapshot"))[b.VillageId]) == Json(copier.Capture(b)), "복사 실패 격리·목록 비움·다른 마을 계속");
                    manager.MarkSaveDirty(bad.VillageId);
                    manager.FlushSaveSnapshot();
                    bool blocked = false;
                    try { manager.CaptureSaveSnapshotRoot(); }
                    catch (InvalidOperationException) { blocked = true; }
                    Check(blocked == true && expected.Count == 1, "실패 시 저장 중단·로그 1회");
                    bad.Cycle = null;
                    // 새 표시 없이도 조립 직전 실패 항목을 다시 복사한다.
                    manager.CaptureSaveSnapshotRoot();
                    Check(((List<int>)Field("_snapshotFailedIds")).Count == 0, "조립 직전 재시도 복구");
                }
                finally { Debug.unityLogger.logHandler = original; }

                ((Dictionary<int, VillageData>)Field("_villages")).Remove(b.VillageId);
                ((List<VillageData>)Field("_villageList")).Remove(b);
                manager.MarkSaveDirty(b.VillageId);
                manager.FlushSaveSnapshot();
                Check(((Dictionary<int, object?>)Field("_saveSnapshot")).ContainsKey(b.VillageId) == false, "제거한 마을 DTO 정리");
                a.ShopTier++;
                manager.RebuildSaveSnapshot();
                Check(manager.ValidateSaveSnapshot() == 0, "재생성으로 표시 누락 복구");
                typeof(VillageManager).GetMethod("ClearVillages", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, null);
                Check(((Dictionary<int, object?>)Field("_saveSnapshot")).Count == 0 &&
                    ((List<int>)Field("_snapshotFailedIds")).Count == 0 && manager.SnapshotFlushCount == 0, "Reset·Load 공통 수명 정리");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            report.Append($"{checks - failures}/{checks} PASS, failures={failures}");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            return report.ToString();
        }

        private sealed class FaultVillageData : VillageData
        {
            public FaultVillageData? Cycle;
            public FaultVillageData(int id) : base(id, Vector2.zero) { }
        }

        private sealed class ExpectedVillageFailureLogHandler : ILogHandler
        {
            private readonly ILogHandler _original;
            public int Count { get; private set; }
            public ExpectedVillageFailureLogHandler(ILogHandler original) => _original = original;
            public void LogFormat(LogType logType, UnityEngine.Object? context, string format, params object[] args)
            {
                string message = string.Format(format, args);
                if (logType == LogType.Error && message.StartsWith("[VillageSaveSnapshot] Village 99: snapshot capture failed", StringComparison.Ordinal))
                    Count++;
                else _original.LogFormat(logType, context, format, args);
            }
            public void LogException(Exception exception, UnityEngine.Object? context) => _original.LogException(exception, context);
        }

        // 화면 밖에서 이동 중인 NPC의 위치는 저장하지 않는 계산값이라 두 경로에서 달라도 된다 (SAVE_SNAPSHOT S13).
        private static string StripDerivedPositions(string shardJson)
        {
            JObject root = JObject.Parse(shardJson);
            if (root[nameof(NpcShardSaveRoot.Npcs)] is JObject npcs)
            {
                List<JProperty> entries = new List<JProperty>(npcs.Properties());
                for (int i = 0; i < entries.Count; i++)
                {
                    if (int.TryParse(entries[i].Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int npcId) == false ||
                        AR.s.Npc.IsSavePositionDerived(npcId) == false ||
                        entries[i].Value is not JObject fields)
                        continue;
                    fields.Remove(nameof(NpcSaveData.PositionX));
                    fields.Remove(nameof(NpcSaveData.PositionY));
                }
            }
            return root.ToString(Formatting.None);
        }
    }
}
#endif
