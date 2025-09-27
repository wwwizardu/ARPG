using ARPG.Base;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

namespace ARPG.UI
{
    public class UICharacter : UIBaseForm
    {
        [SerializeField] private Transform _characterRoot;
        [SerializeField] private Transform _inventoryRoot;

        private UIInventory _inventoryUI;
        private CharacterUI _characterUI;
        private bool _loadCompleted = false;

        public bool LoadCompleted => _loadCompleted;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            LoadUI();
        }

        public override bool UpdateInput(Input.ArpgInput inInput)
        {
            if(_loadCompleted == false)
                return false;

            if (base.UpdateInput(inInput) == true)
                return true;

            if (inInput.UI.CloseInventory.WasReleasedThisFrame() == true)
            {
                Close();
                return true;
            }

            return false;
        }

        private async void LoadUI()
        {
            var loadTasks = new List<UniTask>();

            if (_characterUI == null)
            {
                loadTasks.Add(LoadCharacterUI());
            }

            if (_inventoryUI == null)
            {
                loadTasks.Add(LoadInventoryUI());
            }

            await UniTask.WhenAll(loadTasks);

            _loadCompleted = true;
            Debug.Log("UICharacter LoadUI completed");
        }

        private async UniTask LoadCharacterUI()
        {
            // 캐릭터 UI 로드 로직 (필요시 구현)
            _characterUI = await LoadUIAsync<CharacterUI>("UI/CharacterUI", _characterRoot);
            _characterUI.Initialize("UI/CharacterUI"); 
        }

        private async UniTask LoadInventoryUI()
        {
            _inventoryUI = await LoadUIAsync<UIInventory>(AddressablePath.Inventory, _inventoryRoot);
            _inventoryUI.Initialize(AddressablePath.Inventory, AR.s.Data.Player._inventory, AR.s.Data.Player._inventory.Count); // 슬롯 40개로 초기화
        }

        private async UniTask<T> LoadUIAsync<T>(string inName, Transform inParent) where T : UIBase
        {
            var handle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(inName, inParent);
            await handle.ToUniTask();

            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[UICharacter] Failed to load UI: {inName}");
                return null;
            }

            var uiComponent = handle.Result.GetComponent<T>();
            if (uiComponent == null)
            {
                Debug.LogError($"[UICharacter] UI component {typeof(T).Name} not found on loaded prefab: {inName}");
                return null;
            }

            return uiComponent;
        }

    }
}


