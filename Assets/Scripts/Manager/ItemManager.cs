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

            EquipmentData? equipmentData = null;
            if (inTable.Equipment.EquipmentStat != null) // 옵션이 정해져 있는 장비
            {
                equipmentData = new EquipmentData()
                {
                    Id = inTable.Equipment.Id,
                    Table = inTable.Equipment
                };

                equipmentData.StatData = new()
                {
                    Prefix = inTable.Equipment.EquipmentStat.Prefix != null ? new List<Stat>(inTable.Equipment.EquipmentStat.Prefix) : new List<Stat>(),
                    Postfix = inTable.Equipment.EquipmentStat.Postfix != null ? new List<Stat>(inTable.Equipment.EquipmentStat.Postfix) : new List<Stat>(),
                };
            }
            else // 옵션이 생성되어야 하는 장비
            {
                equipmentData = new EquipmentData()
                {
                    Id = inTable.Equipment.Id,
                    Table = inTable.Equipment
                };

                equipmentData.StatData = new()
                {
                    Prefix = new List<Stat>(),
                    Postfix = new List<Stat>(),
                };

                // 접두사 옵션 생성
                CreatePrefixOptions(equipmentData.StatData);

                // 접미사 옵션 생성
                CreatePostfixOptions(equipmentData.StatData);
            }

            equipmentData.Quality = Random.Range(1, 101); // 1~100 사이의 품질 값 설정
            equipmentData.Update();

            return equipmentData;
        }
        
        private void CreatePrefixOptions(EquipmentStatData equipmentStatData)
        {
            int prefixCount = Random.Range(1, 3); // 접두사 옵션 개수 (1~2개)
            for (int i = 0; i < prefixCount; i++)
            {
                // 랜덤 스탯 타입 선택
                GlobalEnum.Stat randomStatType = (GlobalEnum.Stat)Random.Range(0, System.Enum.GetValues(typeof(GlobalEnum.Stat)).Length);

                // 랜덤 스탯 값 (1~5)
                ushort randomStatValue = (ushort)Random.Range(1, 6);

                Stat newStat = new Stat()
                {
                    Type = randomStatType,
                    Value = randomStatValue
                };

                equipmentStatData.Prefix.Add(newStat);
            }
        }

        private void CreatePostfixOptions(EquipmentStatData equipmentStatData)
        {
            int postfixCount = Random.Range(1, 3); // 접미사 옵션 개수 (1~2개)
            for (int i = 0; i < postfixCount; i++)
            {
                // 랜덤 스탯 타입 선택
                GlobalEnum.Stat randomStatType = (GlobalEnum.Stat)Random.Range(0, System.Enum.GetValues(typeof(GlobalEnum.Stat)).Length);

                // 랜덤 스탯 값 (1~5)
                ushort randomStatValue = (ushort)Random.Range(1, 6);

                Stat newStat = new Stat()
                {
                    Type = randomStatType,
                    Value = randomStatValue
                };

                equipmentStatData.Postfix.Add(newStat);
            }
        }

    }
}


