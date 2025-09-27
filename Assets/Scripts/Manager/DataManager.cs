#nullable enable
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        private WorldData _worldData = new();
        private PlayerData _playerData = new();

        public PlayerData Player => _playerData;

        public async Task Initialize()
        {
            // 데이터 초기화 로직
            await LoadTableAsync();

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
            _worldData.Initialize();

            _playerData.Initialize(60); // 인벤토리 슬롯 60개로 초기화

            return true;
        }

        public async Task<bool> Save()
        {

            return true;
        }

        public bool DropItem(Vector2 inPosition, ItemData inItem)
        {
            if (inItem == null)
                return false;

            return _worldData.AddDropItem(inPosition.x, inPosition.y, inItem);
        }
    }
}
