#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ARPG.Item
{
    public class ItemManager : MonoBehaviour
    {
        private int _instanceIdCounter = 0;
        private Dictionary<int, ItemObject> _itemInstances = new Dictionary<int, ItemObject>();
        public void Initialize()
        {
            _instanceIdCounter = 1;
            Debug.Log("ItemManager Initialized");
        }

        public bool Load()
        {
            return true;
        }

        public bool Save()
        {
            return true;
        }

        public async Task<bool> CreateItem(int inItemId, int inQuantity, Vector3 inPosition)
        {
            if (inQuantity <= 0)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - inQuantity is invalid, inItemId({inItemId}), inQuantity({inQuantity})");
                return false;
            }
                
            var handle = Addressables.InstantiateAsync("Item/Item", inPosition, Quaternion.identity);
            var itemObject = await handle.Task;

            if (itemObject == null)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - itemObject is null");
                return false;
            }

            var item = itemObject.GetComponent<ItemObject>();
            if (item == null)
            {
                Addressables.ReleaseInstance(itemObject);
                Debug.Log("[Monster] DropItemObjectAsync - item is null");
                return false;
            }

            // 아이템 데이터 생성
            ItemTable? itemTable = AR.s.Data?.GetItem(inItemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemData] Initialize - ItemTable not found for Id: {inItemId}");
                return false;
            }

            ItemData itemData = new ItemData()
            {
                Table = itemTable,
                Id = inItemId,
                ItemInstanceId = _instanceIdCounter++,
                Equipment = CreateEquipmentData(itemTable),
                Quantity = inQuantity,
            };

            // 아이템 데이터 세팅
            if (item.SetItem(itemData) == false)
            {
                Addressables.ReleaseInstance(itemObject);
                Debug.Log($"[Monster] Item SetItem failed, ID({inItemId})");
                return false;
            }

            // 아이템 인스턴스 등록
            if (_itemInstances.ContainsKey(itemData.ItemInstanceId))
            {
                Debug.LogError($"[ItemManager] CreateItem - Item instance ID already exists, ID({itemData.ItemInstanceId})");
                Addressables.ReleaseInstance(itemObject);
                return false;
            }
            
            _itemInstances[itemData.ItemInstanceId] = item;

            Debug.Log("[Monster] Item dropped successfully");

            return true;
        }

        public bool DestroyItem(int inItemInstanceId)
        {
            if (_itemInstances.TryGetValue(inItemInstanceId, out var item) == false)
            {
                Debug.LogWarning($"[ItemManager] DestroyItem - Item instance not found, ID({inItemInstanceId})");
                return false;
            }

            // Dictionary에서 제거
            _itemInstances.Remove(inItemInstanceId);

            // Addressables로 생성한 오브젝트 해제
            if (item != null && item.gameObject != null)
            {
                Addressables.ReleaseInstance(item.gameObject);
            }

            return true;
        }

        private EquipmentData? CreateEquipmentData(ItemTable inTable)
        {
            if (inTable == null || inTable.Equipment == null)
                return null;

            return null;
        }

    }
}


