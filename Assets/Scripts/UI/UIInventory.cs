#nullable enable
using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ARPG.UI
{
    public class UIInventory : Base.UIBase
    {
        [SerializeField] int SlotCount = 20;
        [SerializeField] Transform _slotRoot;

        private SlotUI? []? _slotUIs = null;
        private Inventory? _inventory;

        public async void Initialize(string inName, int inSlotMaxCount)
        {
            SlotCount = inSlotMaxCount;
            base.Initialize(inName, false);
            
            if (_inventory == null)
                _inventory = new Inventory();

            _inventory.Initialize(SlotCount);

            var loadResult = await LoadSlots();
            if (loadResult == false)
            {
                Debug.LogError("[UIInventory] Failed to load slots");
                return;
            }
        }

        public override void Initialize(string inName, bool isForm = false)
        {
            Debug.LogError("[UIInventory] Initialize called, use Initialize(string inName, int inSlotMaxCount)");
        }
        
        private async Task<bool> LoadSlots()
        {
            try
            {
                _slotUIs = new SlotUI?[SlotCount];

                for (int i = 0; i < SlotCount; i++)
                {
                    var handle = Addressables.InstantiateAsync(AddressablePath.SlotUI, _slotRoot);
                    var slotObject = await handle.Task;

                    if (slotObject == null)
                    {
                        Debug.LogError($"[UIInventory] Failed to load slot {i}");
                        return false;
                    }

                    SlotUI slotUI = slotObject.GetComponent<SlotUI>();
                    if (slotUI == null)
                    {
                        Debug.LogError($"[UIInventory] Slot prefab does not contain SlotUI component at index {i}");
                        return false;
                    }

                    slotUI.Initialize(i);
                    _slotUIs[i] = slotUI;
                }

                Debug.Log($"[UIInventory] Successfully loaded {SlotCount} slots");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UIInventory] Failed to load slots: {ex.Message}");
                return false;
            }
        }

    }
}


