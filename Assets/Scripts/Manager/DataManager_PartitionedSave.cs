#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace ARPG.Data
{
    public partial class DataManager
    {
        private const int PARTITION_FORMAT_VERSION = 1;
        private const int PARTITION_SCHEMA_VERSION = 1;
        // 2의 거듭제곱이어야 한다 (GetNpcShardIndex가 비트 AND로 나눈다).
        public const int NPC_SHARD_COUNT = 32;
        private const string SAVE_SLOT_DIRECTORY = "Slot_0";
        private const string PARTS_DIRECTORY = "parts";
        private const string MANIFEST_FILE = "manifest.json";
        private const string PREVIOUS_MANIFEST_FILE = "manifest.prev.json";
        private const string RECOVERY_DIRECTORY_PREFIX = "recovery_";
        private const int RECOVERY_DIRECTORY_MAX = 999;

        private const string PART_PLAYER = "player";
        private const string PART_WORLD = "world";
        private const string PART_MAP = "map";
        private const string PART_DUNGEONS = "dungeons";
        private const string PART_VILLAGES = "villages";
        private const string PART_BUILDINGS = "buildings";
        private const string PART_CHRONICLE = "chronicle";
        private const string PART_WORLD_SIM = "world_sim";
        private const string PART_NPC_META = "npc_meta";
        private const string PART_NPC_SOCIAL = "npc_social";
        private const string PART_NPC_PARTIES = "npc_parties";
        private const string PART_NPC_STATE_PREFIX = "npc_state_";

        private SaveManifest? _activeSaveManifest;
        // 프레임 정리와 독립된 디스크 확정 세대. 실패/Reset 중인 writer는 이 값을 전진시키지 않는다.
        private readonly Dictionary<string, object> _committedSnapshotGenerations = new();
        // One lane owns disk preparation, manifest replacement and maintenance, across Reset.
        private readonly SemaphoreSlim _saveIoGate = new(1, 1);
        private readonly SaveDtoSnapshot _saveSnapshots = new();
        // 저장 1회의 깊은 복사 누적 시간 ([SaveTiming] 로그). 캡처 시작마다 Reset — 저장 사이에 값을 들고 있지 않는다.
        private readonly System.Diagnostics.Stopwatch _deepCopyTimer = new();
        private readonly System.Diagnostics.Stopwatch _npcSnapshotRefreshTimer = new();
        private readonly System.Diagnostics.Stopwatch _npcSnapshotAssemblyTimer = new();
        private readonly System.Diagnostics.Stopwatch _villageSnapshotRefreshTimer = new();
        private readonly System.Diagnostics.Stopwatch _villageSnapshotAssemblyTimer = new();
        private readonly System.Diagnostics.Stopwatch _simulationSnapshotRefreshTimer = new();
        private readonly System.Diagnostics.Stopwatch _simulationSnapshotAssemblyTimer = new();
        private Task _saveMaintenanceTask = Task.CompletedTask;
        private bool _isLoading;
        private int _saveLifetime;
        private int _saveOperationLifetime;
        private SaveScope _queuedSaveScope = SaveScope.None;
        private bool _queuedSaveSnapshotRebuild;
        private int _nextItemInstanceId = 1;

        /// <summary>
        /// MigrateWorldData가 실행된 뒤 아직 전체 스코프 저장이 끝나지 않았음을 표시한다.
        /// true인 동안 모든 저장은 SaveScope.All로 승격된다 (버전과 마이그레이션 대상 데이터가
        /// 서로 다른 파티션에 있어, 부분 저장이 버전만 올리면 마이그레이션이 유실되기 때문).
        /// </summary>
        private bool _needsFullSaveAfterMigration;
        private TaskCompletionSource<bool>? _saveCompletion;
        private TaskCompletionSource<bool>? _queuedSaveCompletion;
        public bool IsSaving => _isSaving;
        public long SaveRevision => _activeSaveManifest?.Revision ?? 0;

#if UNITY_EDITOR
        // 필드 초기화는 Unity 로딩 스레드에서 실행될 수 있다. Initialize 진입 때 기록한다.
        private int _editorMainThreadId;
        // Editor fault injection is instance-owned and never touches the real slot when an override is set.
        public string? DiagnosticSavePath { get; set; }
        public string? DiagnosticFailurePoint { get; set; }
        public Func<Task>? DiagnosticBeforeCommit { get; set; }
        public Action? DiagnosticAfterCapture { get; set; }
        public Action<string>? DiagnosticWorkerStage { get; set; }
        public bool DiagnosticSkipRuntimeCapture { get; set; }
        public double DiagnosticCaptureMilliseconds { get; private set; }
        public double DiagnosticPrepareMilliseconds { get; private set; }
        public double DiagnosticCommitMilliseconds { get; private set; }
        public SaveScope DiagnosticCommittedScope { get; private set; }
        public int DiagnosticPreparedPartCount { get; private set; }
        public int DiagnosticWrittenPartCount { get; private set; }
#endif

        public async Task WaitForSaveIdleAsync()
        {
            do
            {
                while (_saveCompletion != null)
                    await _saveCompletion.Task;
                await _saveIoGate.WaitAsync();
                _saveIoGate.Release();
                await _saveMaintenanceTask;
                // A new request may have arrived while we were draining maintenance.
            }
            while (_saveCompletion != null);
        }

        public int NextItemInstanceId => _nextItemInstanceId;

        public async void SavePlayer()
        {
            await SaveAsync(SaveScope.Player | SaveScope.World);
        }

        public async void SavePlayerAndVillages()
        {
            await SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Villages);
        }

#if UNITY_EDITOR
        public string DiagnosticCaptureSimulationJson(SaveScope scope, bool useSnapshot)
        {
            object? root;
            if (useSnapshot == true)
            {
                AR.s.Npc.RefreshSimulationSaveSnapshots(scope);
                if (scope == SaveScope.NpcSocial) root = AR.s.Npc.CaptureSocialSaveSnapshotRoot();
                else if (scope == SaveScope.NpcParties) root = AR.s.Npc.CapturePartySaveSnapshotRoot();
                else if (scope == SaveScope.NpcMeta) root = AR.s.Npc.CapturePopulationSaveSnapshotRoot();
                else if (scope == SaveScope.WorldSim) root = AR.s.History.CaptureSaveSnapshotRoot();
                else throw new ArgumentOutOfRangeException(nameof(scope));
            }
            else
            {
                if (scope == SaveScope.NpcSocial) root = new NpcSocialSaveRoot
                    { Affinities = AR.s.Npc.SaveAffinity(), Relations = AR.s.Npc.SaveRelations() };
                else if (scope == SaveScope.NpcParties) root = new NpcPartySaveRoot { Parties = AR.s.Npc.SaveParties() };
                else if (scope == SaveScope.NpcMeta) root = new NpcMetaSaveRoot { Population = _worldData.NpcPopulation };
                else if (scope == SaveScope.WorldSim) root = new WorldSimSaveRoot { WorldSim = _worldData.WorldSim };
                else throw new ArgumentOutOfRangeException(nameof(scope));
                root = _saveSnapshots.Capture(root);
            }
            return JsonConvert.SerializeObject(root, Formatting.None, JsonSettings.Default);
        }

        public string DiagnosticCaptureVillageJson(bool useSnapshot)
        {
            AR.s.Village.SyncTaskToData();
            object? root;
            if (useSnapshot == true)
            {
                AR.s.Village.FlushSaveSnapshot();
                root = AR.s.Village.CaptureSaveSnapshotRoot();
            }
            else
                root = _saveSnapshots.Capture(new VillageSaveRoot { Villages = AR.s.Village.Save() });
            return JsonConvert.SerializeObject(root, Formatting.None, JsonSettings.Default);
        }

        public string[] DiagnosticCaptureNpcShardJson(bool useSnapshot)
        {
            AR.s.Npc.SaveAllActiveNpcs();
            object?[] roots;
            if (useSnapshot == true)
            {
                AR.s.Npc.FlushSaveSnapshot();
                roots = AR.s.Npc.CaptureSaveSnapshotShards();
            }
            else
            {
                Dictionary<int, Npc.NpcSaveData>[] shards = SplitNpcShards(new Dictionary<int, Npc.NpcSaveData>(AR.s.Npc.SaveDataView));
                roots = new object?[NPC_SHARD_COUNT];
                for (int i = 0; i < roots.Length; i++)
                    roots[i] = _saveSnapshots.Capture(new NpcShardSaveRoot { ShardIndex = i, Npcs = shards[i] });
            }
            string[] json = new string[roots.Length];
            for (int i = 0; i < json.Length; i++)
                json[i] = JsonConvert.SerializeObject(roots[i], Formatting.None, JsonSettings.Default);
            return json;
        }
#endif

        private void ResetPartitionedSaveState()
        {
            // Called on the main thread, as is the final manifest replacement. Workers own
            // detached data and may finish, but cannot commit into a different lifetime.
            _saveLifetime++;
            _activeSaveManifest = null;
            _committedSnapshotGenerations.Clear();
            _queuedSaveScope = SaveScope.None;
            _queuedSaveSnapshotRebuild = false;
            _nextItemInstanceId = 1;
            _needsFullSaveAfterMigration = false;
            _needsNpcBasicEquipmentReplacement = false;
        }

        private static string GetSlotPath(string savePath)
        {
            return Path.Combine(savePath, SAVE_SLOT_DIRECTORY);
        }

        private static string GetPartsPath(string slotPath)
        {
            return Path.Combine(slotPath, PARTS_DIRECTORY);
        }

        private bool TryLoadPartitionedSave(string savePath)
        {
            string slotPath = GetSlotPath(savePath);
            string manifestPath = Path.Combine(slotPath, MANIFEST_FILE);
            string previousManifestPath = Path.Combine(slotPath, PREVIOUS_MANIFEST_FILE);

            if (TryLoadManifestSnapshot(slotPath, manifestPath, out SaveManifest? manifest,
                    out PartitionSnapshot? snapshot) == true)
            {
                ApplyPartitionSnapshot(manifest!, snapshot!);
                Debug.Log($"[DataManager] Partitioned save loaded. Revision: {manifest!.Revision}, " +
                          $"NPCs: {_worldData.NpcSaveDatas.Count}");
                return true;
            }

            if (TryLoadManifestSnapshot(slotPath, previousManifestPath, out manifest,
                    out snapshot) == false)
            {
                return false;
            }

            ApplyPartitionSnapshot(manifest!, snapshot!);

            try
            {
                Directory.CreateDirectory(slotPath);
                File.Copy(previousManifestPath, manifestPath, true);
                Debug.LogWarning("[DataManager] Active manifest was invalid. Restored previous manifest.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DataManager] Previous manifest loaded but active restore failed: {ex.Message}");
            }

            return true;
        }

        private bool TryLoadManifestSnapshot(string slotPath, string manifestPath,
            out SaveManifest? manifest, out PartitionSnapshot? snapshot)
        {
            manifest = null;
            snapshot = null;
            if (File.Exists(manifestPath) == false)
                return false;

            try
            {
                string manifestJson = File.ReadAllText(manifestPath);
                manifest = JsonConvert.DeserializeObject<SaveManifest>(manifestJson,
                    JsonSettings.Default);
                if (manifest == null || manifest.FormatVersion != PARTITION_FORMAT_VERSION ||
                    manifest.Parts == null || manifest.NpcShardCount != NPC_SHARD_COUNT)
                {
                    return false;
                }

                if (TryReadPart(slotPath, manifest, PART_PLAYER,
                        out PlayerSaveRoot? player) == false ||
                    TryReadPart(slotPath, manifest, PART_WORLD,
                        out WorldSaveRoot? world) == false ||
                    TryReadPart(slotPath, manifest, PART_MAP,
                        out MapSaveRoot? map) == false ||
                    TryReadPart(slotPath, manifest, PART_VILLAGES,
                        out VillageSaveRoot? villages) == false ||
                    TryReadPart(slotPath, manifest, PART_BUILDINGS,
                        out BuildingSaveRoot? buildings) == false ||
                    TryReadPart(slotPath, manifest, PART_CHRONICLE,
                        out ChronicleSaveRoot? chronicle) == false ||
                    TryReadPart(slotPath, manifest, PART_WORLD_SIM,
                        out WorldSimSaveRoot? worldSim) == false ||
                    TryReadPart(slotPath, manifest, PART_NPC_META,
                        out NpcMetaSaveRoot? npcMeta) == false ||
                    TryReadPart(slotPath, manifest, PART_NPC_SOCIAL,
                        out NpcSocialSaveRoot? npcSocial) == false ||
                    TryReadPart(slotPath, manifest, PART_NPC_PARTIES,
                        out NpcPartySaveRoot? npcParties) == false)
                {
                    return false;
                }

                if (player!.Version != PARTITION_SCHEMA_VERSION ||
                    world!.Version != PARTITION_SCHEMA_VERSION ||
                    map!.Version != PARTITION_SCHEMA_VERSION ||
                    villages!.Version != PARTITION_SCHEMA_VERSION ||
                    buildings!.Version != PARTITION_SCHEMA_VERSION ||
                    chronicle!.Version != PARTITION_SCHEMA_VERSION ||
                    worldSim!.Version != PARTITION_SCHEMA_VERSION ||
                    npcMeta!.Version != PARTITION_SCHEMA_VERSION ||
                    npcSocial!.Version != PARTITION_SCHEMA_VERSION ||
                    npcParties!.Version != PARTITION_SCHEMA_VERSION ||
                    float.IsNaN(world.ElapsedWorldGameHours) ||
                    float.IsInfinity(world.ElapsedWorldGameHours) ||
                    world.ElapsedWorldGameHours < 0f)
                {
                    return false;
                }

                DungeonSaveRoot dungeons = new();
                if (manifest.Parts.ContainsKey(PART_DUNGEONS) == true)
                {
                    if (TryReadPart(slotPath, manifest, PART_DUNGEONS,
                        out DungeonSaveRoot? loadedDungeons) == false || loadedDungeons == null ||
                        loadedDungeons.Version != 1 || loadedDungeons.Villages == null || loadedDungeons.Dungeons == null)
                        return false;
                    dungeons = loadedDungeons;
                }
                else if (world.WorldDataVersion >= 15)
                    return false; // Only old schemas may legitimately omit this partition.
                if (Dungeon.DungeonSaveValidation.IsConsistent(dungeons, player.Players, world.WorldSeed) == false)
                    return false;

                Dictionary<int, Npc.NpcSaveData> npcs = new();
                for (int shardIndex = 0; shardIndex < NPC_SHARD_COUNT; shardIndex++)
                {
                    string partKey = GetNpcShardPartKey(shardIndex);
                    if (TryReadPart(slotPath, manifest, partKey,
                            out NpcShardSaveRoot? shard) == false ||
                        shard == null || shard.Version != PARTITION_SCHEMA_VERSION ||
                        shard.ShardIndex != shardIndex || shard.Npcs == null)
                    {
                        return false;
                    }

                    List<int> shardNpcIds = new List<int>(shard.Npcs.Keys);
                    for (int i = 0; i < shardNpcIds.Count; i++)
                    {
                        int npcId = shardNpcIds[i];
                        if (npcs.TryAdd(npcId, shard.Npcs[npcId]) == false)
                            return false;
                    }
                }

                snapshot = new PartitionSnapshot
                {
                    Player = player!,
                    World = world!,
                    Dungeons = dungeons,
                    Map = map!,
                    Villages = villages!,
                    Buildings = buildings!,
                    Chronicle = chronicle!,
                    WorldSim = worldSim!,
                    NpcMeta = npcMeta!,
                    NpcSocial = npcSocial!,
                    NpcParties = npcParties!,
                    Npcs = npcs,
                };
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DataManager] Partition manifest load failed ({manifestPath}): {ex.Message}");
                manifest = null;
                snapshot = null;
                return false;
            }
        }

        private bool TryReadPart<T>(string slotPath, SaveManifest manifest, string partKey,
            out T? value) where T : class
        {
            value = null;
            if (manifest.Parts.TryGetValue(partKey, out SavePartReference? part) == false ||
                part == null || part.SchemaVersion != PARTITION_SCHEMA_VERSION ||
                string.IsNullOrWhiteSpace(part.FileName))
            {
                return false;
            }

            string partsPath = GetPartsPath(slotPath);
            string fullPath = Path.GetFullPath(Path.Combine(partsPath, part.FileName));
            string expectedRoot = Path.GetFullPath(partsPath) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) == false ||
                File.Exists(fullPath) == false)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (bytes.LongLength != part.Length ||
                string.Equals(ComputeSha256(bytes), part.Sha256,
                    StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }

            string json = Encoding.UTF8.GetString(bytes);
            value = JsonConvert.DeserializeObject<T>(json, JsonSettings.Default);
            return value != null;
        }

        private void ApplyPartitionSnapshot(SaveManifest manifest, PartitionSnapshot snapshot)
        {
            _committedSnapshotGenerations.Clear();
            _worldData = new WorldData
            {
                Version = snapshot.World.WorldDataVersion,
                WorldSeed = snapshot.World.WorldSeed,
                ElapsedWorldGameHours = snapshot.World.ElapsedWorldGameHours,
                Dungeons = snapshot.Dungeons,
                WorldDropItemDatas = snapshot.Map.WorldDropItems ?? new(),
                VillageDatas = snapshot.Villages.Villages ?? new(),
                NpcSaveDatas = snapshot.Npcs,
                NpcAffinityList = snapshot.NpcSocial.Affinities ?? new(),
                PlayerNpcRelationList = snapshot.Player.NpcRelations ?? new(),
                BuildingSaveDatas = snapshot.Buildings.Buildings ?? new(),
                TileModifications = snapshot.Map.TileModifications ?? new(),
                Chronicle = snapshot.Chronicle.Chronicle ?? new(),
                NpcRelationList = snapshot.NpcSocial.Relations ?? new(),
                NpcPartyStates = snapshot.NpcParties.Parties ?? new(),
                NpcPopulation = snapshot.NpcMeta.Population ?? new(),
                NpcCompanion = snapshot.World.NpcCompanion ?? new(),
                GoldPrice = snapshot.World.GoldPrice ?? new(),
                WorldSim = snapshot.WorldSim.WorldSim ?? new(),
            };

            if (_worldData.Version < CURRENT_WORLD_DATA_VERSION)
            {
                MigrateWorldData(_worldData.Version, CURRENT_WORLD_DATA_VERSION);
                _worldData.Version = CURRENT_WORLD_DATA_VERSION;
            }

            _playerDataList = snapshot.Player.Players ?? new();
            _nextItemInstanceId = Math.Max(1, snapshot.World.NextItemInstanceId);
            FinalizeLoadedWorldAndPlayers(snapshot.Player.CurrentPlayerIndex);
            _activeSaveManifest = manifest;
        }

        private void FinalizeLoadedWorldAndPlayers(int currentPlayerIndex)
        {
            PreReserveSavedEntityIds();
            _worldData.LoadCompleted();
            AR.s.Village.Load(_worldData.VillageDatas);
            AR.s.Map.LoadTileModifications(_worldData.TileModifications);

            if (_playerDataList.Count == 0)
            {
                _currentPlayerEntityId = AR.s.Entity.CreateEntity();
                PlayerData defaultPlayer = new PlayerData();
                defaultPlayer.Initialize(_currentPlayerEntityId);
                defaultPlayer.LoadCompleted();
                _playerDataList.Add(defaultPlayer);
            }
            else
            {
                currentPlayerIndex = Mathf.Clamp(currentPlayerIndex, 0,
                    _playerDataList.Count - 1);
                _currentPlayerEntityId = AR.s.Entity.CreateEntity();
                for (int i = 0; i < _playerDataList.Count; i++)
                {
                    PlayerData playerData = _playerDataList[i];
                    if (playerData.Version < CURRENT_PLAYER_DATA_VERSION)
                    {
                        MigratePlayerData(playerData, playerData.Version,
                            CURRENT_PLAYER_DATA_VERSION);
                        playerData.Version = CURRENT_PLAYER_DATA_VERSION;
                    }

                    playerData.PlayerId = i == currentPlayerIndex
                        ? _currentPlayerEntityId
                        : AR.s.Entity.CreateEntity();
                    playerData.LoadCompleted();
                }
            }

            _nextItemInstanceId = Math.Max(_nextItemInstanceId,
                FindNextItemInstanceId());
        }

        private async Task<bool> MigrateLegacySaveToPartitionsAsync()
        {
            if (_activeSaveManifest != null)
                return true;

            _nextItemInstanceId = Math.Max(_nextItemInstanceId,
                FindNextItemInstanceId());
            bool result = await WritePartitionSnapshotAsync(SaveScope.All, false);
            if (result == true)
            {
                Debug.Log("[DataManager] Legacy WorldData/PlayerData migrated to partitioned save. " +
                          "Legacy files were retained for recovery.");
            }
            return result;
        }

        private async Task<bool> SavePartitionedAsync(SaveScope requestedScope, bool rebuildSaveSnapshots = false)
        {
            if (_isLoading == true) return false;
            int requestedLifetime = _saveLifetime;
            while (_isSaving == true && _saveOperationLifetime != requestedLifetime)
            {
                await _saveCompletion!.Task;
                if (requestedLifetime != _saveLifetime || _isLoading == true) return false;
            }
            if (requestedScope == SaveScope.None)
                return true;

            if (_isSaving == true)
            {
                _queuedSaveScope |= requestedScope;
                _queuedSaveSnapshotRebuild |= rebuildSaveSnapshots;
                _queuedSaveCompletion ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Debug.Log($"[DataManager] Save in progress. Queued scope: {_queuedSaveScope}");
                return await _queuedSaveCompletion.Task;
            }

            _isSaving = true;
            _saveOperationLifetime = _saveLifetime;
            _saveCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = RunSaveQueueAsync(requestedScope, request, _saveLifetime, _saveCompletion, rebuildSaveSnapshots);
            return await request.Task;
        }

        private async Task RunSaveQueueAsync(SaveScope scope, TaskCompletionSource<bool> request,
            int lifetime, TaskCompletionSource<bool> idle, bool rebuildSaveSnapshots)
        {
            bool result = false;
            try
            {
                while (scope != SaveScope.None && lifetime == _saveLifetime)
                {
                    result = await WritePartitionSnapshotAsync(scope, true, rebuildSaveSnapshots);
                    // Each caller observes its own committed batch. A later queued failure
                    // must never make an already committed transaction roll back in memory.
                    request.TrySetResult(result);
                    if (result == false || lifetime != _saveLifetime) break;
                    scope = _queuedSaveScope;
                    if (scope == SaveScope.None) break;
                    request = _queuedSaveCompletion!;
                    rebuildSaveSnapshots = _queuedSaveSnapshotRebuild;
                    _queuedSaveScope = SaveScope.None;
                    _queuedSaveSnapshotRebuild = false;
                    _queuedSaveCompletion = null;
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataManager] Save failed: {ex.Message}");
                result = false;
            }
            finally
            {
                request.TrySetResult(false);
                _queuedSaveCompletion?.TrySetResult(false);
                _queuedSaveCompletion = null;
                _queuedSaveScope = SaveScope.None;
                _queuedSaveSnapshotRebuild = false;
                _isSaving = false;
                _saveCompletion = null;
                idle.TrySetResult(result);
            }
        }

        private async Task<bool> WritePartitionSnapshotAsync(SaveScope requestedScope,
            bool captureRuntimeManagers, bool rebuildSaveSnapshots = false)
        {
            int lifetime = _saveLifetime;
            string savePath = Path.Combine(Application.persistentDataPath, "Saved");
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(DiagnosticSavePath) == false) savePath = DiagnosticSavePath;
            if (DiagnosticSkipRuntimeCapture == true) captureRuntimeManagers = false;
#endif
            string slotPath = GetSlotPath(savePath);
            await _saveIoGate.WaitAsync();
            bool maintenanceOwnsGate = false;
            try
            {
                if (lifetime != _saveLifetime) return false;
                SaveScope scope = requestedScope;
                if (captureRuntimeManagers == true && AR.s.Dungeon.RequiresCheckpoint == true)
                    scope |= SaveScope.Player | SaveScope.World | SaveScope.Dungeons;
                // NPC 던전 완료(NPC_AMBITION D58)는 즉시 저장하지 않는다. 대신 슬롯 변경(Dungeons)과 NPC 보상(NpcState)이
                // 한쪽만 저장되지 않도록, 둘 중 하나를 싣는 다음 저장이 나머지도 함께 싣는다.
                const SaveScope npcCompletionScope = SaveScope.Dungeons | SaveScope.NpcState;
                long npcCompletionRevision = AR.s.Dungeon.NpcCompletionRevision;
                if (_activeSaveManifest == null || _needsFullSaveAfterMigration == true)
                    scope = SaveScope.All;
                // 여섯 시뮬 파트는 한 상태로 확정한다. 변경 없는 파일도 다음 manifest에 기존 참조로 포함한다.
                if ((scope & SaveScope.Simulation) != 0)
                    scope |= SaveScope.Simulation;
                if (captureRuntimeManagers == true && AR.s.Dungeon.HasUnsavedNpcCompletion == true &&
                    (scope & (SaveScope.Dungeons | SaveScope.Simulation)) != 0)
                    scope |= npcCompletionScope | SaveScope.Simulation;

                // No awaits during capture: all parts describe the same main-thread state.
#if UNITY_EDITOR
                AssertEditorMainThread("save snapshot capture");
#endif
                var totalTimer = System.Diagnostics.Stopwatch.StartNew();
                int gcCountBefore = GC.CollectionCount(0);
                _deepCopyTimer.Reset();
                _npcSnapshotRefreshTimer.Reset();
                _npcSnapshotAssemblyTimer.Reset();
                _villageSnapshotRefreshTimer.Reset();
                _villageSnapshotAssemblyTimer.Reset();
                _simulationSnapshotRefreshTimer.Reset();
                _simulationSnapshotAssemblyTimer.Reset();
                var captureTimer = System.Diagnostics.Stopwatch.StartNew();
                Dictionary<string, PendingSavePart> pending = new();
                CapturePendingParts(scope, captureRuntimeManagers, pending, rebuildSaveSnapshots || scope == SaveScope.All);
                SaveManifest? previous = _activeSaveManifest;
                Dictionary<string, object> capturedGenerations = SelectIncrementalSnapshotParts(pending, captureRuntimeManagers);
                SaveManifest next = CloneManifest(previous, (previous?.Revision ?? 0) + 1);
                captureTimer.Stop();
                string? failurePoint = null;
                Action<string>? workerStage = null;
#if UNITY_EDITOR
                DiagnosticCaptureMilliseconds = captureTimer.Elapsed.TotalMilliseconds;
                DiagnosticPreparedPartCount = pending.Count;
                DiagnosticAfterCapture?.Invoke();
                failurePoint = DiagnosticFailurePoint;
                workerStage = DiagnosticWorkerStage;
#endif
                // Only independent DTOs and immutable arguments cross this boundary.
                var prepared = await Task.Run(() =>
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    string path = PreparePartitionFiles(slotPath, pending, next,
                        previous == null, failurePoint, workerStage);
                    return (Path: path, Milliseconds: timer.Elapsed.TotalMilliseconds);
                });
#if UNITY_EDITOR
                DiagnosticPrepareMilliseconds = prepared.Milliseconds;
                if (DiagnosticBeforeCommit != null) await DiagnosticBeforeCommit();
#endif
                // Main-thread transaction boundary. Reset/Load cannot interleave with this
                // replacement and publication. Disk commit is irrevocable success.
                if (lifetime != _saveLifetime) return false;
#if UNITY_EDITOR
                AssertEditorMainThread("save manifest commit");
#endif
                var commitTimer = System.Diagnostics.Stopwatch.StartNew();
                CommitManifest(slotPath, prepared.Path);
                _activeSaveManifest = next;
                // 현재 매니저 세대가 아니라 캡처했던 세대만 확정한다. await 도중 생긴 변경은 다음 저장에 남는다.
                foreach (string key in pending.Keys) _committedSnapshotGenerations.Remove(key);
                foreach (var pair in capturedGenerations) _committedSnapshotGenerations[pair.Key] = pair.Value;
                if (scope == SaveScope.All) _needsFullSaveAfterMigration = false;
                if (captureRuntimeManagers == true && (scope & npcCompletionScope) == npcCompletionScope)
                    AR.s.Dungeon.MarkNpcCompletionSaved(npcCompletionRevision);
                commitTimer.Stop();
#if UNITY_EDITOR
                DiagnosticCommitMilliseconds = commitTimer.Elapsed.TotalMilliseconds;
                DiagnosticCommittedScope = scope;
                DiagnosticWrittenPartCount = CountWrittenParts(next);
#endif
                // Hand off the SAME lane before reporting success. No subsequent preparation,
                // quarantine or Load may overlap this cleanup, even after Reset.
                _saveMaintenanceTask = CleanupAfterSaveAsync(slotPath, next, previous, failurePoint, workerStage);
                maintenanceOwnsGate = true;
                Debug.Log($"[DataManager] Partitioned save completed. Revision: {next.Revision}, Scope: {scope}");
                totalTimer.Stop();
                LogSaveTiming(next, scope, pending, captureTimer.Elapsed.TotalMilliseconds,
                    prepared.Milliseconds, commitTimer.Elapsed.TotalMilliseconds,
                    totalTimer.Elapsed.TotalMilliseconds, GC.CollectionCount(0) - gcCountBefore);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataManager] Partitioned save failed: {ex}");
                return false;
            }
            finally
            {
                if (maintenanceOwnsGate == false) _saveIoGate.Release();
            }
        }

        private static string PreparePartitionFiles(string slotPath,
            Dictionary<string, PendingSavePart> pending, SaveManifest next, bool quarantine,
            string? failurePoint, Action<string>? workerStage)
        {
            workerStage?.Invoke("prepare");
            string partsPath = GetPartsPath(slotPath);
            if (quarantine == true && TryQuarantineOrphanedSave(slotPath, partsPath) == false)
                throw new IOException("Could not preserve the previous save.");
            Directory.CreateDirectory(partsPath);
            // Own settings per worker; do not race the shared lazy JsonSettings instance.
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new StringEnumConverter());
#if UNITY_EDITOR
            if (failurePoint == "serialize") settings.Converters.Insert(0, new DiagnosticFailingSaveConverter());
#endif
            List<string> keys = new(pending.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                workerStage?.Invoke("serialize");
                string json = JsonConvert.SerializeObject(pending[key].Snapshot, Formatting.None, settings);
                next.Parts[key] = WritePartFile(partsPath, key, pending[key].SchemaVersion,
                    json, next.Revision, failurePoint, next.Parts.TryGetValue(key, out SavePartReference? existing) ? existing : null);
                if (failurePoint == "after-part") throw new IOException("Injected after-part failure");
            }
            next.SavedAtUtc = DateTime.UtcNow.ToString("O");
            workerStage?.Invoke("manifest-serialize");
            string manifestJson = JsonConvert.SerializeObject(next, Formatting.None, settings);
            string tempPath = Path.Combine(slotPath, $"manifest.{Guid.NewGuid():N}.tmp");
            WritePreparedFile(tempPath, Encoding.UTF8.GetBytes(manifestJson));
            if (failurePoint == "before-manifest") throw new IOException("Injected before-manifest failure");
            return tempPath;
        }

        private async Task CleanupAfterSaveAsync(string slotPath, SaveManifest current,
            SaveManifest? previous, string? failurePoint, Action<string>? workerStage)
        {
            try
            {
                await Task.Run(() =>
                {
                    workerStage?.Invoke("cleanup");
                    if (failurePoint == "cleanup") throw new IOException("Injected cleanup failure");
                    CleanupUnreferencedPartFiles(GetPartsPath(slotPath), current, previous);
                    string[] abandoned = Directory.GetFiles(slotPath, "manifest.*.tmp");
                    for (int i = 0; i < abandoned.Length; i++) File.Delete(abandoned[i]);
                });
            }
            catch (Exception ex)
            {
                // The next successful save scans again, including any files left by a
                // failed write or interrupted cleanup. A committed save remains successful.
                Debug.LogWarning("[DataManager] Saved; cleanup will retry after the next save: " + ex.Message);
            }
            finally { _saveIoGate.Release(); }
        }

        private void CapturePendingParts(SaveScope scope, bool captureRuntimeManagers,
            Dictionary<string, PendingSavePart> pending, bool rebuildSaveSnapshots = false)
        {
            if (captureRuntimeManagers == true && Npc.NpcManager.SimulationSaveSnapshotEnabled == true)
            {
                _simulationSnapshotRefreshTimer.Start();
                AR.s.Npc.RefreshSimulationSaveSnapshots(scope, rebuildSaveSnapshots);
                _simulationSnapshotRefreshTimer.Stop();
            }
            if (captureRuntimeManagers == true && History.HistoryManager.SaveSnapshotEnabled == true &&
                (scope & SaveScope.WorldSim) != 0)
            {
                _simulationSnapshotRefreshTimer.Start();
                if (rebuildSaveSnapshots == true) AR.s.History.RebuildSaveSnapshot();
                else AR.s.History.FlushSaveSnapshot();
                _simulationSnapshotRefreshTimer.Stop();
            }
            if ((scope & SaveScope.Dungeons) != 0)
            {
                if (captureRuntimeManagers == true) AR.s.Dungeon.CaptureCheckpoint();
                AddPendingPart(pending, PART_DUNGEONS, _worldData.Dungeons);
            }
            if ((scope & SaveScope.Player) != 0)
            {
                if (captureRuntimeManagers == true)
                {
                    PlayerData? currentPlayer = GetPlayerData(_currentPlayerEntityId);
                    if (currentPlayer != null)
                        AR.s.Player.Save(currentPlayer);
                }

                PlayerSaveRoot root = new PlayerSaveRoot
                {
                    CurrentPlayerIndex = GetCurrentPlayerIndex(),
                    Players = _playerDataList,
                    NpcRelations = captureRuntimeManagers == true
                        ? AR.s.Npc.SavePlayerRelations()
                        : _worldData.PlayerNpcRelationList,
                };
                AddPendingPart(pending, PART_PLAYER, root);
            }

            if ((scope & SaveScope.World) != 0)
            {
                if (captureRuntimeManagers == true)
                {
                    _nextItemInstanceId = Math.Max(_nextItemInstanceId,
                        AR.s.Item.NextItemInstanceId);
                    _worldData.ElapsedWorldGameHours = AR.s.Time.CurrentGameTime;
                }
                WorldSaveRoot root = new WorldSaveRoot
                {
                    WorldDataVersion = CURRENT_WORLD_DATA_VERSION,
                    NextItemInstanceId = _nextItemInstanceId,
                    WorldSeed = _worldData.WorldSeed,
                    ElapsedWorldGameHours = _worldData.ElapsedWorldGameHours,
                    NpcCompanion = _worldData.NpcCompanion,
                    GoldPrice = _worldData.GoldPrice,
                };
                AddPendingPart(pending, PART_WORLD, root);
            }

            if ((scope & SaveScope.Map) != 0)
            {
                MapSaveRoot root = new MapSaveRoot
                {
                    WorldDropItems = _worldData.WorldDropItemDatas,
                    TileModifications = captureRuntimeManagers == true
                        ? AR.s.WorldMap.SaveTileModifications()
                        : _worldData.TileModifications,
                };
                AddPendingPart(pending, PART_MAP, root);
            }

            if ((scope & SaveScope.Villages) != 0)
            {
                if (captureRuntimeManagers == true)
                    AR.s.Village.SyncTaskToData();
                if (captureRuntimeManagers == true && Village.VillageManager.SaveSnapshotEnabled == true)
                {
                    _villageSnapshotRefreshTimer.Start();
                    if (rebuildSaveSnapshots == true) AR.s.Village.RebuildSaveSnapshot();
                    else AR.s.Village.FlushSaveSnapshot();
                    _villageSnapshotRefreshTimer.Stop();
                    _villageSnapshotAssemblyTimer.Start();
                    AddPendingSnapshotPart(pending, PART_VILLAGES, AR.s.Village.CaptureSaveSnapshotRoot());
                    _villageSnapshotAssemblyTimer.Stop();
                }
                else
                {
                    List<Village.VillageData> villages = captureRuntimeManagers == true
                        ? AR.s.Village.Save()
                        : _worldData.VillageDatas;
                    AddPendingPart(pending, PART_VILLAGES, new VillageSaveRoot { Villages = villages });
                }
            }

            if ((scope & SaveScope.Buildings) != 0)
            {
                Dictionary<int, Village.BuildingSaveData> buildings =
                    captureRuntimeManagers == true
                        ? AR.s.Building.Save()
                        : _worldData.BuildingSaveDatas;
                AddPendingPart(pending, PART_BUILDINGS,
                    new BuildingSaveRoot { Buildings = buildings });
            }

            if ((scope & SaveScope.Chronicle) != 0)
            {
                AddPendingPart(pending, PART_CHRONICLE,
                    new ChronicleSaveRoot { Chronicle = _worldData.Chronicle });
            }

            if ((scope & SaveScope.WorldSim) != 0)
            {
                if (captureRuntimeManagers == true && History.HistoryManager.SaveSnapshotEnabled == true)
                {
                    _simulationSnapshotAssemblyTimer.Start();
                    AddPendingSnapshotPart(pending, PART_WORLD_SIM, AR.s.History.CaptureSaveSnapshotRoot());
                    _simulationSnapshotAssemblyTimer.Stop();
                }
                else AddPendingPart(pending, PART_WORLD_SIM,
                    new WorldSimSaveRoot { WorldSim = _worldData.WorldSim });
            }

            if ((scope & SaveScope.NpcMeta) != 0)
            {
                if (captureRuntimeManagers == true && Npc.NpcManager.SimulationSaveSnapshotEnabled == true)
                {
                    _simulationSnapshotAssemblyTimer.Start();
                    AddPendingSnapshotPart(pending, PART_NPC_META, AR.s.Npc.CapturePopulationSaveSnapshotRoot());
                    _simulationSnapshotAssemblyTimer.Stop();
                }
                else AddPendingPart(pending, PART_NPC_META,
                    new NpcMetaSaveRoot { Population = _worldData.NpcPopulation });
            }

            if ((scope & SaveScope.NpcSocial) != 0)
            {
                if (captureRuntimeManagers == true && Npc.NpcManager.SimulationSaveSnapshotEnabled == true)
                {
                    _simulationSnapshotAssemblyTimer.Start();
                    AddPendingSnapshotPart(pending, PART_NPC_SOCIAL, AR.s.Npc.CaptureSocialSaveSnapshotRoot());
                    _simulationSnapshotAssemblyTimer.Stop();
                }
                else
                {
                    NpcSocialSaveRoot root = new NpcSocialSaveRoot
                    {
                        Affinities = captureRuntimeManagers == true
                            ? AR.s.Npc.SaveAffinity()
                            : _worldData.NpcAffinityList,
                        Relations = captureRuntimeManagers == true
                            ? AR.s.Npc.SaveRelations()
                            : _worldData.NpcRelationList,
                    };
                    AddPendingPart(pending, PART_NPC_SOCIAL, root);
                }
            }

            if ((scope & SaveScope.NpcParties) != 0)
            {
                if (captureRuntimeManagers == true && Npc.NpcManager.SimulationSaveSnapshotEnabled == true)
                {
                    _simulationSnapshotAssemblyTimer.Start();
                    AddPendingSnapshotPart(pending, PART_NPC_PARTIES, AR.s.Npc.CapturePartySaveSnapshotRoot());
                    _simulationSnapshotAssemblyTimer.Stop();
                }
                else
                {
                    List<Npc.NpcPartyState> parties = captureRuntimeManagers == true
                        ? AR.s.Npc.SaveParties()
                        : _worldData.NpcPartyStates;
                    AddPendingPart(pending, PART_NPC_PARTIES,
                        new NpcPartySaveRoot { Parties = parties });
                }
            }

            if ((scope & SaveScope.NpcState) != 0)
            {
                if (captureRuntimeManagers == true && Npc.NpcManager.SaveSnapshotEnabled == true)
                {
                    AR.s.Npc.SaveAllActiveNpcs();
                    _npcSnapshotRefreshTimer.Start();
                    if (rebuildSaveSnapshots == true) AR.s.Npc.RebuildSaveSnapshot();
                    else AR.s.Npc.FlushSaveSnapshot();
                    _npcSnapshotRefreshTimer.Stop();
                    _npcSnapshotAssemblyTimer.Start();
                    object?[] roots = AR.s.Npc.CaptureSaveSnapshotShards();
                    for (int i = 0; i < roots.Length; i++)
                        AddPendingSnapshotPart(pending, GetNpcShardPartKey(i), roots[i]);
                    _npcSnapshotAssemblyTimer.Stop();
                }
                else
                {
                    Dictionary<int, Npc.NpcSaveData> npcs = captureRuntimeManagers == true
                        ? AR.s.Npc.Save()
                        : _worldData.NpcSaveDatas;
                    Dictionary<int, Npc.NpcSaveData>[] shards = SplitNpcShards(npcs);
                    for (int shardIndex = 0; shardIndex < NPC_SHARD_COUNT; shardIndex++)
                    {
                        NpcShardSaveRoot root = new NpcShardSaveRoot
                        {
                            ShardIndex = shardIndex,
                            Npcs = shards[shardIndex],
                        };
                        AddPendingPart(pending, GetNpcShardPartKey(shardIndex), root);
                    }
                }
            }
        }

        private void AddPendingPart<T>(Dictionary<string, PendingSavePart> pending,
            string key, T root)
        {
            _deepCopyTimer.Start();
            object? snapshot = _saveSnapshots.Capture(root);
            _deepCopyTimer.Stop();
            AddPendingSnapshotPart(pending, key, snapshot);
        }

        private static void AddPendingSnapshotPart(Dictionary<string, PendingSavePart> pending,
            string key, object? snapshot)
        {
            pending[key] = new PendingSavePart(PARTITION_SCHEMA_VERSION, snapshot);
        }

        private Dictionary<string, object> SelectIncrementalSnapshotParts(Dictionary<string, PendingSavePart> pending,
            bool captureRuntimeManagers)
        {
            Dictionary<string, object> captured = new();
            if (captureRuntimeManagers == false) return captured;
            List<string> keys = new(pending.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                object? generation = null;
                if (key == PART_VILLAGES && Village.VillageManager.SaveSnapshotEnabled == true)
                    generation = (AR.s.Village, AR.s.Village.SaveSnapshotGeneration);
                else if (key == PART_WORLD_SIM && History.HistoryManager.SaveSnapshotEnabled == true)
                    generation = AR.s.History.SaveSnapshotGeneration;
                else if (Npc.NpcManager.SimulationSaveSnapshotEnabled == true)
                {
                    if (key == PART_NPC_SOCIAL) generation = AR.s.Npc.SocialSaveSnapshotGeneration;
                    else if (key == PART_NPC_PARTIES) generation = AR.s.Npc.PartySaveSnapshotGeneration;
                    else if (key == PART_NPC_META) generation = AR.s.Npc.PopulationSaveSnapshotGeneration;
                }
                if (key.StartsWith(PART_NPC_STATE_PREFIX, StringComparison.Ordinal) == true && Npc.NpcManager.SaveSnapshotEnabled == true)
                {
                    int shard = int.Parse(key.Substring(PART_NPC_STATE_PREFIX.Length), System.Globalization.CultureInfo.InvariantCulture);
                    generation = (AR.s.Npc, AR.s.Npc.GetSaveShardGeneration(shard));
                }
                if (generation == null) continue;
                captured[key] = generation;
                if (_activeSaveManifest?.Parts.TryGetValue(key, out SavePartReference? part) == true &&
                    part.SchemaVersion == PARTITION_SCHEMA_VERSION &&
                    _committedSnapshotGenerations.TryGetValue(key, out object? committed) == true && committed.Equals(generation) == true)
                    pending.Remove(key);
            }
            return captured;
        }

        private static int CountWrittenParts(SaveManifest manifest)
        {
            int count = 0;
            foreach (SavePartReference part in manifest.Parts.Values)
            {
                if (part.Revision == manifest.Revision) count++;
            }
            return count;
        }


        /// <summary>
        /// 저장 1회의 비용과 크기를 한 줄로 남긴다. 메인 스레드에서 도는 구간은 복사(매니저 저장 데이터 정리 + 깊은 복사)와
        /// manifest 교체이고, JSON 변환·파일 쓰기는 백그라운드다. 복사가 짧은데도 경직이 있으면 GC 횟수를 본다.
        /// </summary>
        private void LogSaveTiming(SaveManifest next, SaveScope scope, Dictionary<string, PendingSavePart> pending,
            double captureMs, double prepareMs, double commitMs, double totalMs, int gcCount)
        {
            double deepCopyMs = _deepCopyTimer.Elapsed.TotalMilliseconds;
            double snapshotRefreshMs = _npcSnapshotRefreshTimer.Elapsed.TotalMilliseconds;
            double snapshotAssemblyMs = _npcSnapshotAssemblyTimer.Elapsed.TotalMilliseconds;
            double villageRefreshMs = _villageSnapshotRefreshTimer.Elapsed.TotalMilliseconds;
            double villageAssemblyMs = _villageSnapshotAssemblyTimer.Elapsed.TotalMilliseconds;
            double simulationRefreshMs = _simulationSnapshotRefreshTimer.Elapsed.TotalMilliseconds;
            double simulationAssemblyMs = _simulationSnapshotAssemblyTimer.Elapsed.TotalMilliseconds;
            string frameTiming = string.Empty;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Npc.NpcManager.SaveSnapshotEnabled == true)
                frameTiming = $" 프레임정리={AR.s.Npc.SnapshotFlushAverageMs:F3}ms평균/" +
                    $"{AR.s.Npc.SnapshotFlushMaxMs:F3}ms최대/{AR.s.Npc.SnapshotFlushCount}회";
            if (Village.VillageManager.SaveSnapshotEnabled == true)
                frameTiming += $" 마을프레임정리={AR.s.Village.SnapshotFlushAverageMs:F3}ms평균/" +
                    $"{AR.s.Village.SnapshotFlushMaxMs:F3}ms최대/{AR.s.Village.SnapshotFlushCount}회";
#endif
            long totalBytes = 0;
            long npcStateBytes = 0;
            int npcShardCount = 0;
            StringBuilder parts = new StringBuilder();
            List<string> keys = new List<string>(pending.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                if (next.Parts.TryGetValue(keys[i], out SavePartReference? part) == false || part == null)
                    continue;
                if (part.Revision != next.Revision) continue;
                totalBytes += part.Length;
                if (keys[i].StartsWith(PART_NPC_STATE_PREFIX, StringComparison.Ordinal) == true)
                {
                    npcStateBytes += part.Length;
                    npcShardCount++;
                    continue;
                }
                parts.Append(", ").Append(keys[i]).Append('=').Append(FormatSaveBytes(part.Length));
            }

            Debug.Log($"[SaveTiming] rev={next.Revision} scope={scope} 메인복사={captureMs:F1}ms " +
                      $"(매니저정리 등={captureMs - deepCopyMs - snapshotRefreshMs - snapshotAssemblyMs - villageRefreshMs - villageAssemblyMs - simulationRefreshMs - simulationAssemblyMs:F1}ms, " +
                      $"깊은복사={deepCopyMs:F1}ms, NPC스냅샷정리={snapshotRefreshMs:F3}ms, 조립={snapshotAssemblyMs:F3}ms, 마을스냅샷정리={villageRefreshMs:F3}ms, 마을조립={villageAssemblyMs:F3}ms, 시뮬스냅샷정리={simulationRefreshMs:F3}ms, 시뮬조립={simulationAssemblyMs:F3}ms) " +
                      $"백그라운드={prepareMs:F1}ms 교체={commitMs:F1}ms 메인합계={captureMs + commitMs:F1}ms " +
                      $"전체={totalMs:F1}ms 새파일={CountWrittenParts(next)} 재사용={next.Parts.Count - CountWrittenParts(next)} 새파일크기={FormatSaveBytes(totalBytes)} " +
                      $"(NPC상태 {npcShardCount}샤드={FormatSaveBytes(npcStateBytes)}{parts}) GC0=+{gcCount}{frameTiming}");
        }

        private static string FormatSaveBytes(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2}MB";
            return $"{bytes / 1024.0:F0}KB";
        }

        private static Dictionary<int, Npc.NpcSaveData>[] SplitNpcShards(
            Dictionary<int, Npc.NpcSaveData> npcs)
        {
            Dictionary<int, Npc.NpcSaveData>[] shards =
                new Dictionary<int, Npc.NpcSaveData>[NPC_SHARD_COUNT];
            for (int i = 0; i < shards.Length; i++)
                shards[i] = new Dictionary<int, Npc.NpcSaveData>();

            List<int> npcIds = new List<int>(npcs.Keys);
            for (int i = 0; i < npcIds.Count; i++)
            {
                int npcId = npcIds[i];
                shards[GetNpcShardIndex(npcId)][npcId] = npcs[npcId];
            }
            return shards;
        }

        /// <summary>NPC가 들어갈 샤드 번호. 기존 경로(SplitNpcShards)와 스냅샷 조립(NpcManager)이 같이 쓴다.</summary>
        public static int GetNpcShardIndex(int npcId)
        {
            return npcId & (NPC_SHARD_COUNT - 1);
        }

        private static string GetNpcShardPartKey(int shardIndex)
        {
            return $"{PART_NPC_STATE_PREFIX}{shardIndex:D2}";
        }

        private static SaveManifest CloneManifest(SaveManifest? source, long revision)
        {
            SaveManifest result = new SaveManifest
            {
                FormatVersion = PARTITION_FORMAT_VERSION,
                Revision = revision,
                NpcShardCount = NPC_SHARD_COUNT,
            };
            if (source?.Parts == null)
                return result;

            List<string> partKeys = new List<string>(source.Parts.Keys);
            for (int i = 0; i < partKeys.Count; i++)
            {
                string partKey = partKeys[i];
                SavePartReference part = source.Parts[partKey];
                result.Parts[partKey] = new SavePartReference
                {
                    FileName = part.FileName, SchemaVersion = part.SchemaVersion,
                    Revision = part.Revision, Length = part.Length, Sha256 = part.Sha256,
                };
            }
            return result;
        }

        private static SavePartReference WritePartFile(string partsPath, string partKey,
            int schemaVersion, string json, long revision, string? failurePoint, SavePartReference? existing = null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string hash = ComputeSha256(bytes);
            // 재생성/스위치 OFF/최초 런타임 저장도 동일한 내용은 새 파일을 만들지 않는다.
            if (existing != null && existing.SchemaVersion == schemaVersion && existing.Length == bytes.LongLength &&
                existing.Sha256 == hash && File.Exists(Path.Combine(partsPath, existing.FileName)) == true)
                return existing;
            string safeKey = partKey.Replace(Path.DirectorySeparatorChar, '_')
                .Replace(Path.AltDirectorySeparatorChar, '_');
            string fileName = $"{safeKey}.r{revision:D8}.{Guid.NewGuid():N}.json";
            string finalPath = Path.Combine(partsPath, fileName);
            string tempPath = finalPath + ".tmp";
            WritePreparedFile(tempPath, bytes, failurePoint == "part-write");
            File.Move(tempPath, finalPath);
            return new SavePartReference
            {
                FileName = fileName, SchemaVersion = schemaVersion, Revision = revision,
                Length = bytes.LongLength, Sha256 = hash,
            };
        }

        private static void WritePreparedFile(string path, byte[] bytes, bool failDuringWrite = false)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (failDuringWrite == true)
            {
                stream.Write(bytes, 0, bytes.Length / 2);
                throw new IOException("Injected partial partition write failure");
            }
            stream.Write(bytes, 0, bytes.Length);
            // Dispose flushes managed buffers and closes the file before rename, matching the original
            // writer's process-crash guarantees without forcing a disk flush per partition.
        }

        private static void CommitManifest(string slotPath, string tempPath)
        {
            string manifestPath = Path.Combine(slotPath, MANIFEST_FILE);
            string previousPath = Path.Combine(slotPath, PREVIOUS_MANIFEST_FILE);
            if (File.Exists(manifestPath) == false)
                File.Move(tempPath, manifestPath);
            else
                // Do not delete the existing backup first: failed replacement must preserve it.
                File.Replace(tempPath, manifestPath, previousPath, true);
        }

        /// <summary>
        /// 활성 매니페스트를 모르는 상태(_activeSaveManifest == null)에서 저장을 시작하기 전에,
        /// 디스크에 남아 있는 기존 파티션 세이브를 recovery_XXX 폴더로 통째로 옮긴다.
        /// 삭제하지 않으므로 손상된 세이브도 수동 복구 여지가 남는다.
        /// 옮길 대상이 없으면 아무것도 하지 않고 true를 반환한다.
        /// 격리에 실패하면 false를 반환해 저장 자체를 중단시킨다 — 조용한 데이터 소실보다
        /// 눈에 보이는 저장 실패가 낫다.
        /// </summary>
        private static bool TryQuarantineOrphanedSave(string slotPath, string partsPath)
        {
            string manifestPath = Path.Combine(slotPath, MANIFEST_FILE);
            string previousManifestPath = Path.Combine(slotPath, PREVIOUS_MANIFEST_FILE);

            bool hasParts = Directory.Exists(partsPath) == true &&
                Directory.GetFiles(partsPath, "*.json", SearchOption.TopDirectoryOnly).Length > 0;
            bool hasManifest = File.Exists(manifestPath) == true ||
                File.Exists(previousManifestPath) == true;
            if (hasParts == false && hasManifest == false)
                return true;

            if (TryCreateRecoveryDirectory(slotPath, out string recoveryPath) == false)
            {
                Debug.LogError("[DataManager] Save aborted: no free recovery slot to preserve " +
                    $"the existing save under '{slotPath}'. Remove old {RECOVERY_DIRECTORY_PREFIX}* " +
                    "directories and try again.");
                return false;
            }

            try
            {
                Directory.CreateDirectory(recoveryPath);
                if (Directory.Exists(partsPath) == true)
                    Directory.Move(partsPath, Path.Combine(recoveryPath, PARTS_DIRECTORY));
                if (File.Exists(manifestPath) == true)
                    File.Move(manifestPath, Path.Combine(recoveryPath, MANIFEST_FILE));
                if (File.Exists(previousManifestPath) == true)
                {
                    File.Move(previousManifestPath,
                        Path.Combine(recoveryPath, PREVIOUS_MANIFEST_FILE));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[DataManager] Save aborted: failed to preserve the existing save " +
                    $"to '{recoveryPath}': {ex.Message}");
                return false;
            }

            Debug.LogWarning("[DataManager] Active manifest is unknown while a previous save exists. " +
                $"The previous save was moved to '{recoveryPath}' instead of being overwritten.");
            return true;
        }

        private static bool TryCreateRecoveryDirectory(string slotPath, out string recoveryPath)
        {
            for (int i = 1; i <= RECOVERY_DIRECTORY_MAX; i++)
            {
                string candidate = Path.Combine(slotPath, $"{RECOVERY_DIRECTORY_PREFIX}{i:D3}");
                if (Directory.Exists(candidate) == false && File.Exists(candidate) == false)
                {
                    recoveryPath = candidate;
                    return true;
                }
            }

            recoveryPath = string.Empty;
            return false;
        }

        private static void CleanupUnreferencedPartFiles(string partsPath,
            SaveManifest current, SaveManifest? previous)
        {
            HashSet<string> retained = new(StringComparer.OrdinalIgnoreCase);
            AddManifestFiles(retained, current);
            if (previous != null) AddManifestFiles(retained, previous);
            string[] files = Directory.GetFiles(partsPath, "*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if ((fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) &&
                    retained.Contains(fileName) == false)
                    File.Delete(files[i]);
            }
        }

        private static void AddManifestFiles(HashSet<string> retained,
            SaveManifest manifest)
        {
            List<SavePartReference> parts = new List<SavePartReference>(
                manifest.Parts.Values);
            for (int i = 0; i < parts.Count; i++)
            {
                SavePartReference part = parts[i];
                if (string.IsNullOrWhiteSpace(part.FileName) == false)
                    retained.Add(part.FileName);
            }
        }

        private int GetCurrentPlayerIndex()
        {
            for (int i = 0; i < _playerDataList.Count; i++)
            {
                if (_playerDataList[i].PlayerId == _currentPlayerEntityId)
                    return i;
            }
            return 0;
        }

        private int FindNextItemInstanceId()
        {
            int maxId = 0;
            for (int i = 0; i < _playerDataList.Count; i++)
            {
                PlayerData player = _playerDataList[i];
                if (player._inventoryEquip != null)
                {
                    for (int slot = 0; slot < player._inventoryEquip.Length; slot++)
                        ScanItemInstanceId(player._inventoryEquip[slot], ref maxId);
                }
                if (player._inventory != null)
                {
                    for (int slot = 0; slot < player._inventory.Count; slot++)
                        ScanItemInstanceId(player._inventory[slot], ref maxId);
                }
                if (player._skillBookSlots != null)
                {
                    for (int slot = 0; slot < player._skillBookSlots.Length; slot++)
                        ScanItemInstanceId(player._skillBookSlots[slot], ref maxId);
                }
            }

            List<Npc.NpcSaveData> npcs = new List<Npc.NpcSaveData>(
                _worldData.NpcSaveDatas.Values);
            for (int i = 0; i < npcs.Count; i++)
            {
                Npc.NpcSaveData npc = npcs[i];
                if (npc.InventoryEquip != null)
                {
                    for (int slot = 0; slot < npc.InventoryEquip.Length; slot++)
                        ScanItemInstanceId(npc.InventoryEquip[slot], ref maxId);
                }
                if (npc.Inventory != null)
                {
                    for (int slot = 0; slot < npc.Inventory.Count; slot++)
                        ScanItemInstanceId(npc.Inventory[slot], ref maxId);
                }
            }

            for (int i = 0; i < _worldData.WorldDropItemDatas.Count; i++)
                ScanItemInstanceId(_worldData.WorldDropItemDatas[i].ItemData, ref maxId);
            for (int d = 0; d < _worldData.Dungeons.Dungeons.Count; d++)
                for (int i = 0; i < _worldData.Dungeons.Dungeons[d].Drops.Count; i++)
                    ScanItemInstanceId(_worldData.Dungeons.Dungeons[d].Drops[i].ItemData, ref maxId);

            for (int i = 0; i < _worldData.VillageDatas.Count; i++)
            {
                List<Village.MerchantStockEntry> stock =
                    _worldData.VillageDatas[i].MerchantStock;
                if (stock == null)
                    continue;
                for (int j = 0; j < stock.Count; j++)
                    ScanItemInstanceId(stock[j].Item, ref maxId);
            }

            return maxId == int.MaxValue ? int.MaxValue : maxId + 1;
        }

        private static void ScanItemInstanceId(ItemData? item, ref int maxId)
        {
            if (item == null)
                return;
            if (item.ItemInstanceId > maxId)
                maxId = item.ItemInstanceId;
            if (item.SkillBook?.SocketedPages == null)
                return;
            for (int i = 0; i < item.SkillBook.SocketedPages.Count; i++)
                ScanItemInstanceId(item.SkillBook.SocketedPages[i], ref maxId);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return BytesToHex(sha.ComputeHash(bytes));
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("X2"));
            return builder.ToString();
        }

        private sealed class PartitionSnapshot
        {
            public PlayerSaveRoot Player = null!;
            public WorldSaveRoot World = null!;
            public DungeonSaveRoot Dungeons = new();
            public MapSaveRoot Map = null!;
            public VillageSaveRoot Villages = null!;
            public BuildingSaveRoot Buildings = null!;
            public ChronicleSaveRoot Chronicle = null!;
            public WorldSimSaveRoot WorldSim = null!;
            public NpcMetaSaveRoot NpcMeta = null!;
            public NpcSocialSaveRoot NpcSocial = null!;
            public NpcPartySaveRoot NpcParties = null!;
            public Dictionary<int, Npc.NpcSaveData> Npcs = null!;
        }

        private readonly struct PendingSavePart
        {
            public readonly int SchemaVersion;
            public readonly object? Snapshot;

            public PendingSavePart(int schemaVersion, object? snapshot)
            {
                SchemaVersion = schemaVersion;
                Snapshot = snapshot;
            }
        }

#if UNITY_EDITOR
        private void AssertEditorMainThread(string operation)
        {
            Debug.Assert(Environment.CurrentManagedThreadId == _editorMainThreadId,
                $"[DataManager] {operation} left the Unity main thread. " +
                $"Expected thread {_editorMainThreadId}, actual {Environment.CurrentManagedThreadId}.");
        }

        private sealed class DiagnosticFailingSaveConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => true;
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
                => throw new JsonSerializationException("Injected JSON converter failure");
            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }
#endif
    }
}
