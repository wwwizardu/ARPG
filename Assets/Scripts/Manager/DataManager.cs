#nullable enable
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using ARPG.History;
using ARPG.Npc;
using ARPG.Village;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        // 21: narrative 파트 도입 (NPC_NARRATIVE_CONTENT_DESIGN §7.7). 21 이상은 narrative 파트가 반드시 있어야 한다.
        // 22: exploration 파트(시야 탐험 기억) 도입 뒤 같은 날 제거 (FOG_OF_WAR_DESIGN §6). 22 세이브에 남은 exploration 파트와
        //     던전 기록의 탐험 칸은 읽지 않고, 파트는 다음 저장의 manifest에서 뺀다(OBSOLETE_PARTS). 데이터 이관은 없다.
        private const ushort CURRENT_WORLD_DATA_VERSION = 22;
        private const ushort NARRATIVE_PART_REQUIRED_VERSION = 21;
        private const ushort CURRENT_PLAYER_DATA_VERSION = 2;
        private WorldData _worldData = new();
        public int WorldSeed => _worldData.WorldSeed;
        public float ElapsedWorldGameHours => _worldData.ElapsedWorldGameHours;
        public DungeonSaveRoot Dungeons => _worldData.Dungeons;
        // 공통 콘텐츠 실행 원장의 저장 루트. 원장 수정은 NpcManager만 한다.
        public NarrativeSaveRoot Narrative => _worldData.Narrative;
        public Npc.Narrative.NarrativeContentCatalog NarrativeCatalog { get; } = new();
        public List<WorldDropItemData> WorldDrops => _worldData.WorldDropItemDatas;
        private List<PlayerData> _playerDataList = new();
        private int _currentPlayerEntityId = 0; // 현재 활성화된 플레이어 ID
        // WorldDataVersion 17 마이그레이션 표시 (D121). NpcManager.Load가 ConsumeNpcBasicEquipmentReplacement로 가져간다.
        private bool _needsNpcBasicEquipmentReplacement;
        
        private bool _isSaving = false;
        private bool _needsInitialWorldSave = false;

        // 하위 호환성을 위한 속성 - 현재 활성 플레이어 반환
        public PlayerData Player
        {
            get
            {
                var player = GetPlayerData(_currentPlayerEntityId);
                if (player != null)
                    return player;

                if (_playerDataList.Count > 0)
                    return _playerDataList[0];

                // 플레이어가 없으면 기본 플레이어 생성
                var newPlayer = new PlayerData();
                newPlayer.Initialize(0);
                _playerDataList.Add(newPlayer);
                return newPlayer;
            }
        }

        // 전체 플레이어 목록
        public List<PlayerData> PlayerDataList => _playerDataList;

        // 현재 활성 플레이어 ID
        public int CurrentPlayerEntityId => _currentPlayerEntityId;
        public Dictionary<int, NpcSaveData> NpcSaveDatas => _worldData.NpcSaveDatas;
        public List<NpcAffinityEntry> NpcAffinityList => _worldData.NpcAffinityList;
        public List<PlayerNpcRelationEntry> PlayerNpcRelationList => _worldData.PlayerNpcRelationList;
        public List<NpcRelationEntry> NpcRelationList => _worldData.NpcRelationList;
        public List<NpcPartyState> NpcPartyStates => _worldData.NpcPartyStates;
        public NpcPopulationState NpcPopulation => _worldData.NpcPopulation;
        public NpcCompanionState NpcCompanion => _worldData.NpcCompanion;
        // GOLD_BALANCE_DESIGN D126 — 세계 물가 지수 (World 파티션)
        public GoldPriceIndexState GoldPrice => _worldData.GoldPrice;
        // NAMED_THREAT_DESIGN §10.1 — 네임드 위협 기록 (World 파티션). 소유자는 NamedThreatManager
        public NamedThreatWorldState NamedThreats => _worldData.NamedThreats;
        // WORLD_ORE_VEIN_PLAN §4.2 — 월드 광맥 기록 (ore_veins 파티션). 수정은 OreVeinManager만 한다
        public List<OreVeinRecord> OreVeins => _worldData.OreVeins;
        public Dictionary<int, BuildingSaveData> BuildingSaveDatas => _worldData.BuildingSaveDatas;
        // R1: 정착지 연대기 (New Game 1회 생성 후 저장이 정본)
        public ChronicleSaveData Chronicle { get => _worldData.Chronicle; set => _worldData.Chronicle = value; }
        // PROCEDURAL §0 런타임 LOD — 정착지 라이브 추상 층
        public WorldSimState WorldSim => _worldData.WorldSim;

        public async Task Initialize()
        {
#if UNITY_EDITOR
            _editorMainThreadId = System.Environment.CurrentManagedThreadId;
#endif
            await WaitForSaveIdleAsync();
            // 데이터 초기화 로직
            await LoadTableAsync();
            try
            {
                NarrativeCatalog.Initialize(new Npc.Narrative.BundledNarrativeContentSource(), GetAllDungeonTables(), GetMonster, GetItem);
            }
            catch (System.Exception ex)
            {
                // 콘텐츠 팩이 잘못돼도 게임은 연다. 새 던전에 소문 콘텐츠만 붙지 않는다 (위치 소문은 유지).
                NarrativeCatalog.Reset();
                Debug.LogError("[Narrative] Content pack rejected; dungeon rumors are disabled: " + ex.Message);
            }

            await LoadBaseSpriteAtlas();

            await Load();

            Debug.Log("DataManager Initialized");
        }

        public void Reset()
        {
            // 데이터 리셋 로직
            ResetRuntimeData();   // 런타임(휘발성) 파티션 정리 — DataManager_Runtime
            NarrativeCatalog.Reset();
            ResetPartitionedSaveState();
            Debug.Log("DataManager Reset");
        }

        public async Task<bool> Load()
        {
            if (_isLoading == true) return false;
            _isLoading = true;
            // Invalidate uncommitted snapshots, then drain both writes and maintenance
            // before reading/restoring manifests or replacing runtime state.
            ResetPartitionedSaveState();
            int lifetime = _saveLifetime;
            try
            {
                await WaitForSaveIdleAsync();
                if (lifetime != _saveLifetime) return false;
                bool result = await LoadCoreAsync();
                return result && lifetime == _saveLifetime;
            }
            finally { _isLoading = false; }
        }

        private async Task<bool> LoadCoreAsync()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "Saved");
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(DiagnosticSavePath) == false) savePath = DiagnosticSavePath;
#endif
            string worldDataPath = Path.Combine(savePath, "WorldData.json");
            string playerDataPath = Path.Combine(savePath, "PlayerData.json");
            string worldDataBackupPath = Path.Combine(savePath, "WorldData.json.backup");
            string playerDataBackupPath = Path.Combine(savePath, "PlayerData.json.backup");

            string slotPath = GetSlotPath(savePath);
            bool hasPartitionedSave =
                File.Exists(Path.Combine(slotPath, MANIFEST_FILE)) ||
                File.Exists(Path.Combine(slotPath, PREVIOUS_MANIFEST_FILE));
            bool hasWorldData = File.Exists(worldDataPath);
            bool hasWorldDataBackup = File.Exists(worldDataBackupPath);
            _needsInitialWorldSave = hasPartitionedSave == false &&
                hasWorldData == false && hasWorldDataBackup == false;

            if (hasWorldData == false && hasWorldDataBackup == true)
            {
                Debug.LogWarning("[DataManager] WorldData backup exists without the primary file. " +
                    "Initial world auto-save is disabled to avoid overwriting recoverable data.");
            }

            // 저장 폴더가 없으면 생성
            if (Directory.Exists(savePath) == false)
            {
                Directory.CreateDirectory(savePath);
            }

            if (hasPartitionedSave == true && TryLoadPartitionedSave(savePath) == true)
            {
                _needsInitialWorldSave = false;
                return true;
            }
            if (hasPartitionedSave == true && hasWorldData == false &&
                hasWorldDataBackup == false)
            {
                _needsInitialWorldSave = true;
                Debug.LogError("[DataManager] No valid partition manifest or legacy WorldData was found. " +
                               "A new world save will be created after map initialization.");
            }

            // WorldData 로드
            _worldData.Initialize();
            if (File.Exists(worldDataPath))
            {
                try
                {
                    string worldDataJson = File.ReadAllText(worldDataPath);
                    _worldData = JsonConvert.DeserializeObject<WorldData>(worldDataJson, JsonSettings.Default) ?? new WorldData();

                    // 버전 체크 및 마이그레이션
                    if (_worldData.Version < CURRENT_WORLD_DATA_VERSION)
                    {
                        MigrateWorldData(_worldData.Version, CURRENT_WORLD_DATA_VERSION);
                        _worldData.Version = CURRENT_WORLD_DATA_VERSION;
                    }

                    // saved EntityId 사전 예약 — Village.Load / PlayerData CreateEntity 호출보다 반드시 먼저.
                    // 누락 시 신규 ID가 saved ID와 충돌하여 RegisterExistingEntity LogError.
                    PreReserveSavedEntityIds();

                    _worldData.LoadCompleted();
                    AR.s.Village.Load(_worldData.VillageDatas);
                    AR.s.Map.LoadTileModifications(_worldData.TileModifications);
                    Debug.Log($"[DataManager] WorldData loaded successfully (Version: {_worldData.Version}. Npcs: {_worldData.NpcSaveDatas.Count})");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[DataManager] Failed to load WorldData: {ex.Message}");

                    // 백업 파일로 복구 시도
                    if (File.Exists(worldDataBackupPath))
                    {
                        try
                        {
                            string backupJson = File.ReadAllText(worldDataBackupPath);
                            _worldData = JsonConvert.DeserializeObject<WorldData>(backupJson, JsonSettings.Default) ?? new WorldData();

                            // 백업 파일도 버전 체크
                            if (_worldData.Version < CURRENT_WORLD_DATA_VERSION)
                            {
                                MigrateWorldData(_worldData.Version, CURRENT_WORLD_DATA_VERSION);
                                _worldData.Version = CURRENT_WORLD_DATA_VERSION;
                            }

                            // saved EntityId 사전 예약 — Village.Load / PlayerData CreateEntity 호출보다 반드시 먼저.
                            PreReserveSavedEntityIds();

                            _worldData.LoadCompleted();
                            AR.s.Village.Load(_worldData.VillageDatas);
                            AR.s.Map.LoadTileModifications(_worldData.TileModifications);
                            Debug.Log($"[DataManager] WorldData loaded from backup (Version: {_worldData.Version})");
                        }
                        catch (System.Exception backupEx)
                        {
                            Debug.LogError($"[DataManager] Failed to load WorldData from backup: {backupEx.Message}");
                        }
                    }
                }
            }

            // PlayerData 로드 (여러 플레이어 지원)
            _currentPlayerEntityId = AR.s.Entity.CreateEntity();
            _playerDataList.Clear();
            if (File.Exists(playerDataPath))
            {
                try
                {
                    string playerDataJson = File.ReadAllText(playerDataPath);
                    var loadedList = JsonConvert.DeserializeObject<List<PlayerData>>(playerDataJson, JsonSettings.Default);

                    if (loadedList != null && loadedList.Count > 0)
                    {
                        _playerDataList = loadedList;

                        // 각 플레이어 데이터의 버전 체크 및 마이그레이션
                        for (int i = 0; i < _playerDataList.Count; i++)
                        {
                            var playerData = _playerDataList[i];
                            if (playerData.Version < CURRENT_PLAYER_DATA_VERSION)
                            {
                                MigratePlayerData(playerData, playerData.Version, CURRENT_PLAYER_DATA_VERSION);
                                playerData.Version = CURRENT_PLAYER_DATA_VERSION;
                            }

                            if(i == 0) // 첫 번째 플레이어를 현재 플레이어로 설정
                            {
                                playerData.PlayerId = _currentPlayerEntityId;
                            }
                            else // 나머지 플레이어는 새로운 엔티티 ID 할당
                            {
                                playerData.PlayerId = AR.s.Entity.CreateEntity();
                            }

                            playerData.LoadCompleted();
                        }

                        Debug.Log($"[DataManager] PlayerData loaded successfully. Player count: {_playerDataList.Count}");
                    }
                    else
                    {
                        // 빈 리스트이거나 null인 경우 기본 플레이어 생성
                        var defaultPlayer = new PlayerData();
                        defaultPlayer.Initialize(_currentPlayerEntityId);
                        _playerDataList.Add(defaultPlayer);
                        defaultPlayer.LoadCompleted();
                        Debug.Log("[DataManager] No player data found, created default player");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[DataManager] Failed to load PlayerData: {ex.Message}");

                    // 백업 파일로 복구 시도
                    if (File.Exists(playerDataBackupPath))
                    {
                        try
                        {
                            string backupJson = File.ReadAllText(playerDataBackupPath);
                            var loadedList = JsonConvert.DeserializeObject<List<PlayerData>>(backupJson, JsonSettings.Default);

                            if (loadedList != null && loadedList.Count > 0)
                            {
                                _playerDataList = loadedList;

                                for (int i = 0; i < _playerDataList.Count; i++)
                                {
                                    var playerData = _playerDataList[i];
                                    if (playerData.Version < CURRENT_PLAYER_DATA_VERSION)
                                    {
                                        MigratePlayerData(playerData, playerData.Version, CURRENT_PLAYER_DATA_VERSION);
                                        playerData.Version = CURRENT_PLAYER_DATA_VERSION;
                                    }

                                    if(i == 0) // 첫 번째 플레이어를 현재 플레이어로 설정
                                    {
                                        playerData.PlayerId = _currentPlayerEntityId;
                                    }
                                    else // 나머지 플레이어는 새로운 엔티티 ID 할당
                                    {
                                        playerData.PlayerId = AR.s.Entity.CreateEntity();
                                    }

                                    playerData.LoadCompleted();
                                }

                                Debug.Log($"[DataManager] PlayerData loaded from backup. Player count: {_playerDataList.Count}");
                            }
                        }
                        catch (System.Exception backupEx)
                        {
                            Debug.LogError($"[DataManager] Failed to load PlayerData from backup: {backupEx.Message}");

                            // 모든 복구 실패 시 기본 플레이어 생성
                            var defaultPlayer = new PlayerData();
                            defaultPlayer.Initialize(_currentPlayerEntityId);
                            _playerDataList.Add(defaultPlayer);
                            defaultPlayer.LoadCompleted();
                        }
                    }
                }
            }
            else
            {
                // 파일이 없는 경우 기본 플레이어 생성
                var defaultPlayer = new PlayerData();
                defaultPlayer.Initialize(_currentPlayerEntityId);
                _playerDataList.Add(defaultPlayer);
                defaultPlayer.LoadCompleted();
                Debug.Log("[DataManager] PlayerData file not found, created default player");
            }

            _nextItemInstanceId = System.Math.Max(_nextItemInstanceId,
                FindNextItemInstanceId());

            if (hasWorldData == true || hasWorldDataBackup == true ||
                File.Exists(playerDataPath) == true || File.Exists(playerDataBackupPath) == true)
            {
                await MigrateLegacySaveToPartitionsAsync();
            }

            return true;
        }

        /// <summary>
        /// WorldData가 로드된 직후, saved 영구 EntityId(NPC/Building)를 EntityManager에 일괄 등록한다.
        /// 이 단계 이후의 CreateEntity()는 등록된 ID를 자동으로 건너뛰므로 신규/saved ID 간 충돌이 방지됨.
        ///
        /// 호출 시점은 반드시 WorldData deserialize 직후 — Village.Load() / PlayerData CreateEntity()보다 먼저여야 함.
        /// 새로운 영구 EntityId 매니저가 추가되면 여기에 등록 추가 필수.
        /// </summary>
        private void PreReserveSavedEntityIds()
        {
            if (_worldData.NpcSaveDatas != null)
            {
                List<int> npcIds = new List<int>(_worldData.NpcSaveDatas.Keys);
                for (int i = 0; i < npcIds.Count; i++)
                    AR.s.Entity.RegisterExistingEntity(npcIds[i]);
            }
            if (_worldData.BuildingSaveDatas != null)
            {
                List<int> buildingIds = new List<int>(_worldData.BuildingSaveDatas.Keys);
                for (int i = 0; i < buildingIds.Count; i++)
                    AR.s.Entity.RegisterExistingEntity(buildingIds[i]);
            }
        }

        public async void Save()
        {
            await SaveAsync();
        }

        public async Task<bool> SaveInitialWorldIfNeededAsync()
        {
            if (_needsInitialWorldSave == false)
                return true;

            Debug.Log("[DataManager] Initial world auto-save started.");
            bool succeeded = await SaveAsync(SaveScope.All);
            if (succeeded == false)
            {
                Debug.LogWarning("[DataManager] Initial world auto-save failed. " +
                    "The world remains eligible for a later save attempt.");
                return false;
            }

            _needsInitialWorldSave = false;
            int villageCount = _worldData.VillageDatas != null ? _worldData.VillageDatas.Count : 0;
            int npcCount = _worldData.NpcSaveDatas != null ? _worldData.NpcSaveDatas.Count : 0;
            Debug.Log($"[DataManager] Initial world auto-save completed. " +
                $"Villages: {villageCount}, NPCs: {npcCount}");
            return true;
        }

        public Task<bool> SaveAsync(SaveScope scope = SaveScope.All)
        {
            if (AR.s.Dungeon.IsBusy) return Task.FromResult(false);
            // 결투 중에는 던전 체크포인트를 포함한 모든 일반 저장 요청을 거부한다 (NPC_COMBAT_DESIGN §7.2).
            // 결투 종료 결과는 NpcManager.EndPlayerDuel이 결투 컴포넌트를 제거한 뒤 별도로 저장한다.
            if (AR.s.Npc != null && AR.s.Npc.IsPlayerInDuel == true)
            {
                Debug.Log($"[DataManager] 결투 중 저장 거부 - Scope: {scope}");
                return Task.FromResult(false);
            }
            if (AR.s.Dungeon.CompletionPending && AR.s.Dungeon.IsCommitting == false)
                return AR.s.Dungeon.CompleteAsync(scope);
            return SavePartitionedAsync(scope);
        }

        // Exclusive dungeon map transitions call this after draining existing saves.
        // Boss completion uses the normal save queue after its live result is applied.
        public Task<bool> SaveDungeonTransactionAsync(SaveScope scope, bool rebuildSaveSnapshots = true) =>
            SavePartitionedAsync(scope, rebuildSaveSnapshots);

        public bool DropItem(Vector2 inPosition, ItemData inItem)
        {
            if (inItem == null)
                return false;

            AR.s.Item.RecordContextDrop(inItem, new Vector3(inPosition.x, inPosition.y, -0.01f));
            return true;
        }

        // ==================== 플레이어 관리 메서드 ====================

        /// <summary>
        /// 사용되지 않는 새로운 플레이어 ID 생성
        /// </summary>
        private int CreatePlayerId()
        {
            // 0부터 시작해서 사용되지 않는 첫 번째 ID 찾기
            for (int id = 0; id < int.MaxValue; id++)
            {
                if (_playerDataList.Find(p => p.PlayerId == id) == null)
                {
                    return id;
                }
            }

            // 이론적으로 도달할 수 없지만, 안전을 위해
            Debug.LogError("[DataManager] Failed to create new player ID: all IDs are in use");
            return -1;
        }

        /// <summary>
        /// 플레이어 ID로 플레이어 데이터 가져오기
        /// </summary>
        public PlayerData? GetPlayerData(int inPlayerId)
        {
            return _playerDataList.Find(p => p.PlayerId == inPlayerId);
        }

        /// <summary>
        /// 새 플레이어 추가
        /// </summary>
        public PlayerData AddPlayer()
        {
            int newId = CreatePlayerId();
            if (newId < 0)
            {
                Debug.LogError("[DataManager] Failed to add new player: unable to create player ID");
                return null!;
            }

            var newPlayer = new PlayerData();
            newPlayer.Initialize(newId);
            _playerDataList.Add(newPlayer);

            Debug.Log($"[DataManager] New player added with ID: {newPlayer.PlayerId}");
            return newPlayer;
        }

        /// <summary>
        /// 플레이어 제거
        /// </summary>
        public bool RemovePlayer(int inPlayerId)
        {
            var player = GetPlayerData(inPlayerId);
            if (player == null)
            {
                Debug.LogWarning($"[DataManager] Player with ID {inPlayerId} not found");
                return false;
            }

            _playerDataList.Remove(player);

            // 현재 플레이어가 제거된 경우 다른 플레이어로 전환
            if (_currentPlayerEntityId == inPlayerId)
            {
                if (_playerDataList.Count > 0)
                {
                    _currentPlayerEntityId = _playerDataList[0].PlayerId;
                }
                else
                {
                    _currentPlayerEntityId = 0;
                }
            }

            Debug.Log($"[DataManager] Player {inPlayerId} removed");
            return true;
        }

        /// <summary>
        /// 현재 활성 플레이어 설정
        /// </summary>
        public bool SetCurrentPlayer(int inPlayerId)
        {
            var player = GetPlayerData(inPlayerId);
            if (player == null)
            {
                Debug.LogWarning($"[DataManager] Cannot set current player: Player with ID {inPlayerId} not found");
                return false;
            }

            _currentPlayerEntityId = inPlayerId;
            Debug.Log($"[DataManager] Current player set to ID: {inPlayerId}");
            return true;
        }

        /// <summary>
        /// 인덱스로 플레이어 가져오기
        /// </summary>
        public PlayerData? GetPlayerDataByIndex(int inIndex)
        {
            if (inIndex < 0 || inIndex >= _playerDataList.Count)
                return null;

            return _playerDataList[inIndex];
        }

        /// <summary>
        /// 전체 플레이어 수
        /// </summary>
        public int GetPlayerCount()
        {
            return _playerDataList.Count;
        }

        // ==================== 버전 마이그레이션 ====================

        // WorldData 버전 마이그레이션
        private void MigrateWorldData(ushort fromVersion, ushort toVersion)
        {
            Debug.Log($"[DataManager] Migrating WorldData from version {fromVersion} to {toVersion}");

            // 마이그레이션 대상 데이터(NpcPopulation 등)는 world 파트 밖에 흩어져 있지만
            // WorldDataVersion은 world 파트에만 있다. SavePlayer() 같은 부분 스코프 저장이
            // 버전만 최신으로 올려 두면 다음 로드에서 이 마이그레이션이 영구히 건너뛰어지고
            // 구버전 데이터가 그대로 남는다 — 다음 저장을 전체 스코프로 강제한다.
            _needsFullSaveAfterMigration = true;

            MigrateDeceasedRelationMemories(fromVersion, toVersion);

            // 버전 1 -> 2: TileModifications 필드 추가
            if (fromVersion < 2 && toVersion >= 2)
            {
                if (_worldData.TileModifications == null)
                {
                    _worldData.TileModifications = new();
                }
            }

            // 버전 2 -> 3: MerchantStockEntry 스키마 변경 (ItemTableId/SkillId/SkillEffectId → ItemData).
            // 구 매물은 VillageManager.Load에서 Item=null로 역직렬화되어 자동 제거된다 — 여기서는 명시적 작업 불필요.
            // 다음 상점 진입 시 EnsureMerchantStockFresh가 새 포맷으로 재롤한다.
            if (fromVersion < 3 && toVersion >= 3)
            {
                // no-op (VillageManager.Load에서 처리). 버전 점프 사실만 기록.
            }

            // 버전 3 -> 4: 정착지 연대기(Chronicle) + NPC 관계(NpcRelationList) + NpcSentiment 추가 (R0-B).
            // 신규 필드는 WorldData.LoadCompleted()에서 null 방어로 기본값(빈 컬렉션)이 채워진다 — 여기선 명시 작업 불필요.
            if (fromVersion < 4 && toVersion >= 4)
            {
                // no-op (WorldData.LoadCompleted에서 처리). 버전 점프 사실만 기록.
            }

            // 버전 4 -> 5: NPC 개인 활동 상태(NpcActivityState) + 개인 시뮬레이션 시계 추가.
            // 구 세이브의 기본 상태 보정은 NpcSaveData.OnLoadCompleted에서 처리한다.
            if (fromVersion < 5 && toVersion >= 5)
            {
                // no-op (NpcSaveData.OnLoadCompleted에서 처리).
            }

            // 버전 5 -> 6: NPC 전기(Deeds) + Hunt 식량 보상 스냅샷 추가.
            // 구 세이브의 빈 전기 목록 보정은 NpcSaveData.OnLoadCompleted에서 처리한다.
            if (fromVersion < 6 && toVersion >= 6)
            {
                // no-op (NpcSaveData.OnLoadCompleted에서 처리).
            }

            // 버전 6 -> 7: NPC 활동의 출발/목적지 좌표와 analytic 이동 경로 추가.
            // 기존 활동은 이동 경로 없음(기본 false)으로 안전하게 로드한다.
            if (fromVersion < 7 && toVersion >= 7)
            {
                // no-op (NpcActivityState 기본값으로 처리).
            }

            // 버전 7 -> 8: 활동 경로의 귀환점·구간 비율과 결과 판정 전용 출발 시각 추가.
            // 구 경로의 기본값 복원은 NpcActivityState.EnsureInitialized에서 처리한다.
            if (fromVersion < 8 && toVersion >= 8)
            {
                // no-op (NpcActivityState.EnsureInitialized에서 처리).
            }

            // 버전 8 -> 9: 맵 전체 NPC 목표 수와 점진 보충 상태 추가.
            if (fromVersion < 9 && toVersion >= 9)
            {
                // no-op (WorldData.LoadCompleted에서 처리).
            }

            // 버전 10 -> 11: 플레이어↔NPC 관계(PlayerNpcRelationList) 추가.
            // 구 세이브는 WorldData.LoadCompleted에서 빈 목록으로 보정된다.
            if (fromVersion < 11 && toVersion >= 11)
            {
                // no-op (WorldData.LoadCompleted에서 처리).
            }

            // 버전 11 -> 12: Hunt 파티의 공통 활동 상태 추가.
            // 구 세이브는 빈 목록으로 로드되어 기존 개인 시뮬레이션을 그대로 유지한다.
            if (fromVersion < 12 && toVersion >= 12)
            {
                // no-op (WorldData.LoadCompleted에서 처리).
            }

            // 버전 12 -> 13: 대형 맵의 물리 모험가 목표를 400명에서 1,200명으로 상향한다.
            // 사용자 조정값은 보존하고 기존 기본값 400인 세이브만 한 번에 부족분을 채우도록 표시한다.
            if (fromVersion < 13 && toVersion >= 13 && _worldData.NpcPopulation != null &&
                _worldData.NpcPopulation.TargetNpcCount == NpcPopulationState.LegacyLargeMapTarget)
            {
                _worldData.NpcPopulation.TargetNpcCount = NpcPopulationState.LargeMapTarget;
                _worldData.NpcPopulation.NeedsImmediateTargetTopUp = true;
            }

            // 버전 13 -> 14: 고정 맵 총량 대신 마을당 평균 8명을 보장하는 자동 목표를 사용한다.
            // 기존 기본 목표값만 재계산 대상으로 되돌리고 명시적으로 조정한 사용자 값은 보존한다.
            if (fromVersion < 14 && toVersion >= 14 && _worldData.NpcPopulation != null &&
                (_worldData.NpcPopulation.TargetNpcCount == NpcPopulationState.SmallMapTarget ||
                 _worldData.NpcPopulation.TargetNpcCount == NpcPopulationState.LargeMapTarget))
            {
                _worldData.NpcPopulation.TargetNpcCount = -1;
                _worldData.NpcPopulation.NeedsImmediateTargetTopUp = true;
            }

            // 버전 15 -> 16: 저장 가능한 누적 월드 시간 추가.
            // 구 세이브는 0시간에서 시작하고 이후 저장부터 소수 시간을 포함해 유지한다.
            if (fromVersion < 16 && toVersion >= 16)
            {
                _worldData.ElapsedWorldGameHours = 0f;
            }

            // 버전 16 -> 17: NPC 장착 장비를 상점 기본 아이템으로 일괄 교체 (NPC_SHOP_EQUIPMENT D121).
            // 교체에는 테이블 연결이 끝난 NPC 데이터와 전투력 평가가 필요해 NpcManager.Load가 처리한다 — 여기서는 표시만 한다.
            if (fromVersion < 17 && toVersion >= 17)
            {
                _needsNpcBasicEquipmentReplacement = true;
            }
        }

        /// <summary>NPC 통계·최신 사연 이관은 상태와 호감도 파티션을 함께 다음 저장에 커밋한다.</summary>
        public void RequireNpcMemoryMigrationSave() => _needsFullSaveAfterMigration = true;

        /// <summary>
        /// D121 일괄 교체가 필요한지 돌려주고 표시를 지운다. NpcManager.Load가 NPC 로드 때 한 번 부른다.
        /// </summary>
        public bool ConsumeNpcBasicEquipmentReplacement()
        {
            bool needs = _needsNpcBasicEquipmentReplacement;
            _needsNpcBasicEquipmentReplacement = false;
            return needs;
        }

        // 버전 9 -> 10: 생존 NPC의 사망 관계 기억(DeceasedRelations) 추가.
        // 기존 NPC의 빈 목록 보정은 NpcSaveData.OnLoadCompleted에서 처리한다.
        private void MigrateDeceasedRelationMemories(ushort fromVersion, ushort toVersion)
        {
            if (fromVersion < 10 && toVersion >= 10)
            {
                // no-op (NpcSaveData.OnLoadCompleted에서 처리).
            }
        }

        // PlayerData 버전 마이그레이션
        private void MigratePlayerData(PlayerData playerData, ushort fromVersion, ushort toVersion)
        {
            Debug.Log($"[DataManager] Migrating PlayerData (ID: {playerData.PlayerId}) from version {fromVersion} to {toVersion}");

            // V1 → V2: 스킬북 시스템 도입 (SKILLBOOK_DESIGN.md §3.4)
            // 기존 세이브에는 _skillBookSlots 배열이 없으므로 초기화 + 슬롯 0에 기본 책 시드.
            if (fromVersion <= 1 && toVersion >= 2)
            {
                if (playerData._skillBookSlots == null || playerData._skillBookSlots.Length != GlobalEnum.PLAYER_SKILL_SLOT_COUNT)
                {
                    playerData._skillBookSlots = new ItemData?[GlobalEnum.PLAYER_SKILL_SLOT_COUNT];
                }
                playerData.SeedDefaultStarterSkillBook();
            }
        }
    }
}
