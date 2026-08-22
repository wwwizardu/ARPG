#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Data
{
    public partial class DataManager
    {
        private const int PARTITION_FORMAT_VERSION = 1;
        private const int PARTITION_SCHEMA_VERSION = 1;
        private const int NPC_SHARD_COUNT = 32;
        private const string SAVE_SLOT_DIRECTORY = "Slot_0";
        private const string PARTS_DIRECTORY = "parts";
        private const string MANIFEST_FILE = "manifest.json";
        private const string PREVIOUS_MANIFEST_FILE = "manifest.prev.json";
        private const string RECOVERY_DIRECTORY_PREFIX = "recovery_";
        private const int RECOVERY_DIRECTORY_MAX = 999;

        private const string PART_PLAYER = "player";
        private const string PART_WORLD = "world";
        private const string PART_MAP = "map";
        private const string PART_VILLAGES = "villages";
        private const string PART_BUILDINGS = "buildings";
        private const string PART_CHRONICLE = "chronicle";
        private const string PART_WORLD_SIM = "world_sim";
        private const string PART_NPC_META = "npc_meta";
        private const string PART_NPC_SOCIAL = "npc_social";
        private const string PART_NPC_PARTIES = "npc_parties";

        private SaveManifest? _activeSaveManifest;
        private SaveScope _queuedSaveScope = SaveScope.None;
        private int _nextItemInstanceId = 1;

        /// <summary>
        /// MigrateWorldData가 실행된 뒤 아직 전체 스코프 저장이 끝나지 않았음을 표시한다.
        /// true인 동안 모든 저장은 SaveScope.All로 승격된다 (버전과 마이그레이션 대상 데이터가
        /// 서로 다른 파티션에 있어, 부분 저장이 버전만 올리면 마이그레이션이 유실되기 때문).
        /// </summary>
        private bool _needsFullSaveAfterMigration;

        public int NextItemInstanceId => _nextItemInstanceId;

        public async void SavePlayer()
        {
            await SaveAsync(SaveScope.Player | SaveScope.World);
        }

        public async void SavePlayerAndVillages()
        {
            await SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Villages);
        }

        private void ResetPartitionedSaveState()
        {
            _activeSaveManifest = null;
            _queuedSaveScope = SaveScope.None;
            _nextItemInstanceId = 1;
            _needsFullSaveAfterMigration = false;
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
                    npcParties!.Version != PARTITION_SCHEMA_VERSION)
                {
                    return false;
                }

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
            _worldData = new WorldData
            {
                Version = snapshot.World.WorldDataVersion,
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

        private async Task<bool> SavePartitionedAsync(SaveScope requestedScope)
        {
            if (requestedScope == SaveScope.None)
                return true;

            if (_isSaving == true)
            {
                _queuedSaveScope |= requestedScope;
                Debug.Log($"[DataManager] Save in progress. Queued scope: {_queuedSaveScope}");
                return true;
            }

            _isSaving = true;
            bool finalResult = true;
            SaveScope scope = requestedScope;

            do
            {
                _queuedSaveScope = SaveScope.None;
                bool result = await WritePartitionSnapshotAsync(scope, true);
                if (result == false)
                    finalResult = false;
                scope = _queuedSaveScope;
            }
            while (scope != SaveScope.None);

            _isSaving = false;
            return finalResult;
        }

        private async Task<bool> WritePartitionSnapshotAsync(SaveScope requestedScope,
            bool captureRuntimeManagers)
        {
            string savePath = Path.Combine(Application.persistentDataPath, "Saved");
            string slotPath = GetSlotPath(savePath);
            string partsPath = GetPartsPath(slotPath);

            // _activeSaveManifest가 null이면 이 저장은 rev 1부터 다시 시작한다.
            // 이때 디스크에 남아 있는 구세대 파트 파일은 nextManifest가 참조하지 않으므로
            // CleanupUnreferencedPartFiles가 전량 삭제해 버린다 — manifest.prev.json이 가리키던
            // 복구본까지 함께 사라진다. 지우는 대신 recovery_XXX 폴더로 통째로 옮겨 보존한다.
            if (_activeSaveManifest == null &&
                TryQuarantineOrphanedSave(slotPath, partsPath) == false)
            {
                return false;
            }

            Directory.CreateDirectory(partsPath);

            SaveScope scope = requestedScope;
            if (_activeSaveManifest == null)
                scope = SaveScope.All;

            // 마이그레이션 결과는 world 파트 밖(NpcPopulation 등)에도 걸쳐 있는데
            // WorldDataVersion만 world 파트에 있다. 부분 스코프 저장이 버전만 올려 두면
            // 다음 로드에서 마이그레이션이 영구히 건너뛰어지므로 전체 스코프로 승격한다.
            if (_needsFullSaveAfterMigration == true)
                scope = SaveScope.All;

            long revision = (_activeSaveManifest?.Revision ?? 0) + 1;
            Dictionary<string, PendingSavePart> pending = new();

            try
            {
                CapturePendingParts(scope, captureRuntimeManagers, pending);

                SaveManifest nextManifest = CloneManifest(_activeSaveManifest, revision);
                List<string> pendingKeys = new List<string>(pending.Keys);
                for (int i = 0; i < pendingKeys.Count; i++)
                {
                    string partKey = pendingKeys[i];
                    SavePartReference part = await WritePartFileAsync(partsPath, partKey,
                        pending[partKey], revision);
                    nextManifest.Parts[partKey] = part;
                }

                nextManifest.SavedAtUtc = DateTime.UtcNow.ToString("O");
                await CommitManifestAsync(slotPath, nextManifest);

                SaveManifest? previousManifest = _activeSaveManifest;
                _activeSaveManifest = nextManifest;
                if (scope == SaveScope.All)
                    _needsFullSaveAfterMigration = false;
                // previousManifest가 null인 경우는 TryQuarantineOrphanedSave가 parts를 비워 둔
                // 직후뿐이다 — 이 시점의 partsPath에는 nextManifest가 참조하는 파일만 있다.
                CleanupUnreferencedPartFiles(partsPath, nextManifest, previousManifest);
                Debug.Log($"[DataManager] Partitioned save completed. Revision: {revision}, " +
                          $"Scope: {scope}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataManager] Partitioned save failed: {ex.Message}");
                return false;
            }
        }

        private void CapturePendingParts(SaveScope scope, bool captureRuntimeManagers,
            Dictionary<string, PendingSavePart> pending)
        {
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
                }
                WorldSaveRoot root = new WorldSaveRoot
                {
                    WorldDataVersion = CURRENT_WORLD_DATA_VERSION,
                    NextItemInstanceId = _nextItemInstanceId,
                };
                AddPendingPart(pending, PART_WORLD, root);
            }

            if ((scope & SaveScope.Map) != 0)
            {
                MapSaveRoot root = new MapSaveRoot
                {
                    WorldDropItems = _worldData.WorldDropItemDatas,
                    TileModifications = captureRuntimeManagers == true
                        ? AR.s.Map.SaveTileModifications()
                        : _worldData.TileModifications,
                };
                AddPendingPart(pending, PART_MAP, root);
            }

            if ((scope & SaveScope.Villages) != 0)
            {
                List<Village.VillageData> villages;
                if (captureRuntimeManagers == true)
                {
                    AR.s.Village.SyncTaskToData();
                    villages = AR.s.Village.Save();
                }
                else
                {
                    villages = _worldData.VillageDatas;
                }
                AddPendingPart(pending, PART_VILLAGES,
                    new VillageSaveRoot { Villages = villages });
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
                AddPendingPart(pending, PART_WORLD_SIM,
                    new WorldSimSaveRoot { WorldSim = _worldData.WorldSim });
            }

            if ((scope & SaveScope.NpcMeta) != 0)
            {
                AddPendingPart(pending, PART_NPC_META,
                    new NpcMetaSaveRoot { Population = _worldData.NpcPopulation });
            }

            if ((scope & SaveScope.NpcSocial) != 0)
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

            if ((scope & SaveScope.NpcParties) != 0)
            {
                List<Npc.NpcPartyState> parties = captureRuntimeManagers == true
                    ? AR.s.Npc.SaveParties()
                    : _worldData.NpcPartyStates;
                AddPendingPart(pending, PART_NPC_PARTIES,
                    new NpcPartySaveRoot { Parties = parties });
            }

            if ((scope & SaveScope.NpcState) != 0)
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

        private static void AddPendingPart<T>(Dictionary<string, PendingSavePart> pending,
            string key, T root)
        {
            string json = JsonConvert.SerializeObject(root, Formatting.None,
                JsonSettings.Default);
            pending[key] = new PendingSavePart(PARTITION_SCHEMA_VERSION, json);
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
                int shardIndex = npcId & (NPC_SHARD_COUNT - 1);
                shards[shardIndex][npcId] = npcs[npcId];
            }
            return shards;
        }

        private static string GetNpcShardPartKey(int shardIndex)
        {
            return $"npc_state_{shardIndex:D2}";
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
                result.Parts[partKey] = source.Parts[partKey];
            }
            return result;
        }

        private static async Task<SavePartReference> WritePartFileAsync(string partsPath,
            string partKey, PendingSavePart pending, long revision)
        {
            return await Task.Run(() =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(pending.Json);
                string hash = ComputeSha256(bytes);
                string safeKey = partKey.Replace(Path.DirectorySeparatorChar, '_')
                    .Replace(Path.AltDirectorySeparatorChar, '_');
                string fileName = $"{safeKey}.r{revision:D8}.{Guid.NewGuid():N}.json";
                string finalPath = Path.Combine(partsPath, fileName);
                string tempPath = finalPath + ".tmp";

                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, finalPath);

                return new SavePartReference
                {
                    FileName = fileName,
                    SchemaVersion = pending.SchemaVersion,
                    Revision = revision,
                    Length = bytes.LongLength,
                    Sha256 = hash,
                };
            });
        }

        private static async Task CommitManifestAsync(string slotPath,
            SaveManifest manifest)
        {
            string manifestPath = Path.Combine(slotPath, MANIFEST_FILE);
            string previousPath = Path.Combine(slotPath, PREVIOUS_MANIFEST_FILE);
            string tempPath = Path.Combine(slotPath,
                $"manifest.{Guid.NewGuid():N}.tmp");
            string json = JsonConvert.SerializeObject(manifest, Formatting.None,
                JsonSettings.Default);

            await Task.Run(() =>
            {
                Directory.CreateDirectory(slotPath);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(manifestPath) == false)
                {
                    File.Move(tempPath, manifestPath);
                    return;
                }

                if (File.Exists(previousPath) == true)
                    File.Delete(previousPath);
                File.Replace(tempPath, manifestPath, previousPath, true);
            });
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
            try
            {
                HashSet<string> retained = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                AddManifestFiles(retained, current);
                if (previous != null)
                    AddManifestFiles(retained, previous);

                string[] files = Directory.GetFiles(partsPath, "*.json",
                    SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = Path.GetFileName(files[i]);
                    if (retained.Contains(fileName) == false)
                        File.Delete(files[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DataManager] Save part cleanup skipped: {ex.Message}");
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
            public readonly string Json;

            public PendingSavePart(int schemaVersion, string json)
            {
                SchemaVersion = schemaVersion;
                Json = json;
            }
        }
    }
}
