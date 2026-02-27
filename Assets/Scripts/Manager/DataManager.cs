#nullable enable
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using ARPG.Npc;
using ARPG.Utility;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        private const ushort CURRENT_WORLD_DATA_VERSION = 2;
        private const ushort CURRENT_PLAYER_DATA_VERSION = 1;
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
            Debug.Log("DataManager Reset");
        }

        public async Task<bool> Load()
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
                    _worldData = JsonConvert.DeserializeObject<WorldData>(worldDataJson) ?? new WorldData();

                    // 버전 체크 및 마이그레이션
                    if (_worldData.Version < CURRENT_WORLD_DATA_VERSION)
                    {
                        MigrateWorldData(_worldData.Version, CURRENT_WORLD_DATA_VERSION);
                        _worldData.Version = CURRENT_WORLD_DATA_VERSION;
                    }

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
                            _worldData = JsonConvert.DeserializeObject<WorldData>(backupJson) ?? new WorldData();

                            // 백업 파일도 버전 체크
                            if (_worldData.Version < CURRENT_WORLD_DATA_VERSION)
                            {
                                MigrateWorldData(_worldData.Version, CURRENT_WORLD_DATA_VERSION);
                                _worldData.Version = CURRENT_WORLD_DATA_VERSION;
                            }

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
            _currentPlayerEntityId = EntityIdHelper.CreateEntity();
            _playerDataList.Clear();
            if (File.Exists(playerDataPath))
            {
                try
                {
                    string playerDataJson = File.ReadAllText(playerDataPath);
                    var loadedList = JsonConvert.DeserializeObject<List<PlayerData>>(playerDataJson);

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
                                playerData.PlayerId = EntityIdHelper.CreateEntity();
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
                            var loadedList = JsonConvert.DeserializeObject<List<PlayerData>>(backupJson);

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
                                        playerData.PlayerId = EntityIdHelper.CreateEntity();
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

            return true;
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

                    // VillageData 저장
                    _worldData.VillageDatas = AR.s.Village.Save();

                    // NpcData 저장
                    _worldData.NpcSaveDatas = AR.s.Npc.Save();

                    // TileModification 저장
                    _worldData.TileModifications = AR.s.Map.SaveTileModifications();

                    // WorldData 저장
                    string worldDataJson = JsonConvert.SerializeObject(_worldData, Formatting.Indented, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        DefaultValueHandling = DefaultValueHandling.Include
                    });
                    await File.WriteAllTextAsync(worldDataPath, worldDataJson);

                    // PlayerData 저장 (여러 플레이어 지원)
                    // 현재 활성 플레이어 저장
                    var currentPlayer = GetPlayerData(_currentPlayerEntityId);
                    if (currentPlayer != null)
                    {
                        AR.s.Player.Save(currentPlayer);
                    }

                    // 전체 플레이어 리스트 저장
                    string playerDataJson = JsonConvert.SerializeObject(_playerDataList, Formatting.Indented, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        DefaultValueHandling = DefaultValueHandling.Include
                    });
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

            // 버전 1 -> 2: TileModifications 필드 추가
            if (fromVersion < 2 && toVersion >= 2)
            {
                if (_worldData.TileModifications == null)
                {
                    _worldData.TileModifications = new();
                }
            }
        }

        // PlayerData 버전 마이그레이션
        private void MigratePlayerData(PlayerData playerData, ushort fromVersion, ushort toVersion)
        {
            Debug.Log($"[DataManager] Migrating PlayerData (ID: {playerData.PlayerId}) from version {fromVersion} to {toVersion}");

            // 예시: 버전 1 -> 2 마이그레이션
            // if (fromVersion == 1 && toVersion >= 2)
            // {
            //     // 새로운 필드 초기화 또는 데이터 변환
            //     // playerData.NewField = defaultValue;
            // }

            // 예시: 버전 2 -> 3 마이그레이션
            // if (fromVersion <= 2 && toVersion >= 3)
            // {
            //     // 추가 마이그레이션 로직
            // }
        }
    }
}
