#nullable enable
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        private const ushort CURRENT_WORLD_DATA_VERSION = 1;
        private const ushort CURRENT_PLAYER_DATA_VERSION = 1;
        private WorldData _worldData = new();
        private PlayerData _playerData = new();

        private bool _isSaving = false;
        private bool _needsAnotherSave = false;

        public PlayerData Player => _playerData;

        public async Task Initialize()
        {
            // 데이터 초기화 로직
            await LoadTableAsync();

            await LoadBaseSpriteAtlas();

            Load();

            Debug.Log("DataManager Initialized");
        }

        public void Reset()
        {
            // 데이터 리셋 로직
            Debug.Log("DataManager Reset");
        }

        public bool Load()
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
                    Debug.Log($"[DataManager] WorldData loaded successfully (Version: {_worldData.Version})");
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
                            Debug.Log($"[DataManager] WorldData loaded from backup (Version: {_worldData.Version})");
                        }
                        catch (System.Exception backupEx)
                        {
                            Debug.LogError($"[DataManager] Failed to load WorldData from backup: {backupEx.Message}");
                        }
                    }
                }
            }

            // PlayerData 로드
            _playerData.Initialize();
            if (File.Exists(playerDataPath))
            {
                try
                {
                    string playerDataJson = File.ReadAllText(playerDataPath);
                    _playerData = JsonConvert.DeserializeObject<PlayerData>(playerDataJson) ?? new PlayerData();

                    // 버전 체크 및 마이그레이션
                    if (_playerData.Version < CURRENT_PLAYER_DATA_VERSION)
                    {
                        MigratePlayerData(_playerData.Version, CURRENT_PLAYER_DATA_VERSION);
                        _playerData.Version = CURRENT_PLAYER_DATA_VERSION;
                    }

                    _playerData.LoadCompleted();
                    Debug.Log($"[DataManager] PlayerData loaded successfully (Version: {_playerData.Version})");
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
                            _playerData = JsonConvert.DeserializeObject<PlayerData>(backupJson) ?? new PlayerData();

                            // 백업 파일도 버전 체크
                            if (_playerData.Version < CURRENT_PLAYER_DATA_VERSION)
                            {
                                MigratePlayerData(_playerData.Version, CURRENT_PLAYER_DATA_VERSION);
                                _playerData.Version = CURRENT_PLAYER_DATA_VERSION;
                            }

                            _playerData.LoadCompleted();
                            Debug.Log($"[DataManager] PlayerData loaded from backup (Version: {_playerData.Version})");
                        }
                        catch (System.Exception backupEx)
                        {
                            Debug.LogError($"[DataManager] Failed to load PlayerData from backup: {backupEx.Message}");
                        }
                    }
                }
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

                    // WorldData 저장
                    string worldDataJson = JsonConvert.SerializeObject(_worldData, Formatting.Indented, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        DefaultValueHandling = DefaultValueHandling.Include
                    });
                    await File.WriteAllTextAsync(worldDataPath, worldDataJson);

                    // PlayerData 저장
                    AR.s.MyPlayer?.Save(_playerData);
                    string playerDataJson = JsonConvert.SerializeObject(_playerData, Formatting.Indented, new JsonSerializerSettings
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

        // WorldData 버전 마이그레이션
        private void MigrateWorldData(ushort fromVersion, ushort toVersion)
        {
            Debug.Log($"[DataManager] Migrating WorldData from version {fromVersion} to {toVersion}");

            // 예시: 버전 1 -> 2 마이그레이션
            // if (fromVersion == 1 && toVersion >= 2)
            // {
            //     // 새로운 필드 초기화 또는 데이터 변환
            // }

            // 예시: 버전 2 -> 3 마이그레이션
            // if (fromVersion <= 2 && toVersion >= 3)
            // {
            //     // 추가 마이그레이션 로직
            // }
        }

        // PlayerData 버전 마이그레이션
        private void MigratePlayerData(ushort fromVersion, ushort toVersion)
        {
            Debug.Log($"[DataManager] Migrating PlayerData from version {fromVersion} to {toVersion}");

            // 예시: 버전 1 -> 2 마이그레이션
            // if (fromVersion == 1 && toVersion >= 2)
            // {
            //     // 새로운 필드 초기화 또는 데이터 변환
            //     // _playerData.NewField = defaultValue;
            // }

            // 예시: 버전 2 -> 3 마이그레이션
            // if (fromVersion <= 2 && toVersion >= 3)
            // {
            //     // 추가 마이그레이션 로직
            // }
        }
    }
}
