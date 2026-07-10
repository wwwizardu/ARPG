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
        private const ushort CURRENT_WORLD_DATA_VERSION = 10;
        private const ushort CURRENT_PLAYER_DATA_VERSION = 2;
        private WorldData _worldData = new();
        private List<PlayerData> _playerDataList = new();
        private int _currentPlayerEntityId = 0; // 현재 활성화된 플레이어 ID
        
        private bool _isSaving = false;
        private bool _needsAnotherSave = false;

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
        public List<NpcRelationEntry> NpcRelationList => _worldData.NpcRelationList;
        public NpcPopulationState NpcPopulation => _worldData.NpcPopulation;
        public Dictionary<int, BuildingSaveData> BuildingSaveDatas => _worldData.BuildingSaveDatas;
        // R1: 정착지 연대기 (New Game 1회 생성 후 저장이 정본)
        public ChronicleSaveData Chronicle { get => _worldData.Chronicle; set => _worldData.Chronicle = value; }
        // PROCEDURAL §0 런타임 LOD — 정착지 라이브 추상 층
        public WorldSimState WorldSim => _worldData.WorldSim;

        public async Task Initialize()
        {
            // 데이터 초기화 로직
            await LoadTableAsync();

            await LoadBaseSpriteAtlas();

            await Load();

            Debug.Log("DataManager Initialized");
        }

        public void Reset()
        {
            // 데이터 리셋 로직
            ResetRuntimeData();   // 런타임(휘발성) 파티션 정리 — DataManager_Runtime
            Debug.Log("DataManager Reset");
        }

        public Task<bool> Load()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "Saved");
            string worldDataPath = Path.Combine(savePath, "WorldData.json");
            string playerDataPath = Path.Combine(savePath, "PlayerData.json");
            string worldDataBackupPath = Path.Combine(savePath, "WorldData.json.backup");
            string playerDataBackupPath = Path.Combine(savePath, "PlayerData.json.backup");

            // 저장 폴더가 없으면 생성
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
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

            return Task.FromResult(true);
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
                foreach (var kvp in _worldData.NpcSaveDatas)
                    AR.s.Entity.RegisterExistingEntity(kvp.Key);
            }
            if (_worldData.BuildingSaveDatas != null)
            {
                foreach (var kvp in _worldData.BuildingSaveDatas)
                    AR.s.Entity.RegisterExistingEntity(kvp.Key);
            }
        }

        public async void Save()
        {
            await SaveAsync();
        }

        public async Task<bool> SaveAsync()
        {
            // 저장 중이면 다음 저장 예약
            if (_isSaving)
            {
                _needsAnotherSave = true;
                Debug.Log("[DataManager] Save already in progress, queued for next save");
                return true;
            }

            _isSaving = true;
            bool finalResult = true;

            do
            {
                _needsAnotherSave = false;

                string savePath = Path.Combine(Application.persistentDataPath, "Saved");
                string worldDataPath = Path.Combine(savePath, "WorldData.json");
                string playerDataPath = Path.Combine(savePath, "PlayerData.json");
                string worldDataBackupPath = Path.Combine(savePath, "WorldData.json.backup");
                string playerDataBackupPath = Path.Combine(savePath, "PlayerData.json.backup");

                // 저장 폴더가 없으면 생성
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                try
                {
                    // 기존 파일 백업
                    if (File.Exists(worldDataPath))
                    {
                        File.Copy(worldDataPath, worldDataBackupPath, true);
                    }

                    if (File.Exists(playerDataPath))
                    {
                        File.Copy(playerDataPath, playerDataBackupPath, true);
                    }

                    // VillageData 저장 (세이브 직전 진행 중 태스크를 정본 필드로 동기화)
                    AR.s.Village.SyncTaskToData();
                    _worldData.VillageDatas = AR.s.Village.Save();

                    // NpcData 저장
                    _worldData.NpcSaveDatas = AR.s.Npc.Save();
                    _worldData.NpcAffinityList = AR.s.Npc.SaveAffinity();
                    _worldData.NpcRelationList = AR.s.Npc.SaveRelations();

                    // BuildingData 저장
                    _worldData.BuildingSaveDatas = AR.s.Building.Save();

                    // TileModification 저장
                    _worldData.TileModifications = AR.s.Map.SaveTileModifications();

                    // WorldData 저장
                    string worldDataJson = JsonConvert.SerializeObject(_worldData, Formatting.Indented, JsonSettings.Default);
                    await File.WriteAllTextAsync(worldDataPath, worldDataJson);

                    // PlayerData 저장 (여러 플레이어 지원)
                    // 현재 활성 플레이어 저장
                    var currentPlayer = GetPlayerData(_currentPlayerEntityId);
                    if (currentPlayer != null)
                    {
                        AR.s.Player.Save(currentPlayer);
                    }

                    // 전체 플레이어 리스트 저장
                    string playerDataJson = JsonConvert.SerializeObject(_playerDataList, Formatting.Indented, JsonSettings.Default);
                    await File.WriteAllTextAsync(playerDataPath, playerDataJson);

                    // 저장 성공 시 백업 파일 삭제
                    if (File.Exists(worldDataBackupPath))
                    {
                        File.Delete(worldDataBackupPath);
                    }

                    if (File.Exists(playerDataBackupPath))
                    {
                        File.Delete(playerDataBackupPath);
                    }

                    Debug.Log($"[DataManager] Save completed. Path: {savePath}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[DataManager] Save failed: {ex.Message}");

                    // 저장 실패 시 백업 파일로 복구
                    try
                    {
                        if (File.Exists(worldDataBackupPath))
                        {
                            File.Copy(worldDataBackupPath, worldDataPath, true);
                            File.Delete(worldDataBackupPath);
                        }

                        if (File.Exists(playerDataBackupPath))
                        {
                            File.Copy(playerDataBackupPath, playerDataPath, true);
                            File.Delete(playerDataBackupPath);
                        }

                        Debug.Log($"[DataManager] Restored from backup after save failure");
                    }
                    catch (System.Exception restoreEx)
                    {
                        Debug.LogError($"[DataManager] Failed to restore backup: {restoreEx.Message}");
                    }

                    finalResult = false;
                }
            }
            while (_needsAnotherSave);

            _isSaving = false;
            return finalResult;
        }

        public bool DropItem(Vector2 inPosition, ItemData inItem)
        {
            if (inItem == null)
                return false;

            return _worldData.AddDropItem(inPosition.x, inPosition.y, inItem);
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
