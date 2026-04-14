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

            item.Initialize(itemData);

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

        public bool PickupItem(Item.ItemObject inItem)
        {
            if (inItem == null)
                return false;

            if (0 <= AR.s.Player?.Inventory?.AddItem(inItem.ItemData))
            {
                AR.s.Item.DestroyItem(inItem.ItemData.ItemInstanceId);

                AR.s.Data.Save();
            }

            return false;
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

            // ECS 엔티티 제거 (WorldItemComponent 포함)
            if (item != null && item.EntityId >= 0)
            {
                Utility.EntityIdHelper.DestroyEntity(item.EntityId);
            }

            // Addressables로 생성한 오브젝트 해제
            if (item != null && item.gameObject != null)
            {
                Addressables.ReleaseInstance(item.gameObject);
            }

            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 테이블 업데이트 등으로 Equipment가 누락된 기존 아이템 복구용 (에디터 전용)
        /// </summary>
        public EquipmentData? RepairEquipmentData(ItemTable inTable)
        {
            return CreateEquipmentData(inTable);
        }
#endif

        private EquipmentData? CreateEquipmentData(ItemTable inTable)
        {
            if (inTable == null)
                return null;

            // Implicit Mod가 없는 아이템은 장비가 아님
            var implicits = AR.s.Data.GetItemImplicits(inTable.Id);
            if (implicits.Count == 0)
                return null;

            EquipmentData equipmentData = new EquipmentData()
            {
                Id = inTable.Id,
            };

            // EquipType 설정
            equipmentData.EquipType = Utils.CategoryToEquipmentType(inTable.Category);
            equipmentData.Quality = Random.Range(1, 101);

            // 1. Implicit Mod 생성 (아이템 고정 옵션)
            equipmentData.InitImplicitMods(inTable.Id);

            // 2. Prefix Mod 랜덤 생성
            RollRandomMods(equipmentData, GlobalEnum.ModSlot.Prefix, Random.Range(1, 3));

            // 3. Postfix Mod 랜덤 생성
            RollRandomMods(equipmentData, GlobalEnum.ModSlot.Postfix, Random.Range(1, 3));

            // 4. 생성된 Mod들의 테이블 참조 연결
            for (int i = 0; i < equipmentData.Mods.Count; i++)
            {
                equipmentData.Mods[i].OnLoadCompleted();
            }

            return equipmentData;
        }

        /// <summary>
        /// 지정 슬롯에 랜덤 Mod를 롤링하여 추가
        /// </summary>
        private void RollRandomMods(EquipmentData equipment, GlobalEnum.ModSlot slot, int count)
        {
            List<ModTable> modPool = AR.s.Data.GetModPool(slot);
            if (modPool.Count == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                // 랜덤 Mod 선택
                ModTable selectedMod = modPool[Random.Range(0, modPool.Count)];

                // 해당 Mod의 티어 목록에서 랜덤 선택
                List<ModTierTable> tiers = AR.s.Data.GetModTiers(selectedMod.Id);
                if (tiers.Count == 0)
                    continue;

                ModTierTable selectedTier = tiers[Random.Range(0, tiers.Count)];

                // 값 롤링
                ushort value1 = (ushort)Random.Range(selectedTier.Min1, selectedTier.Max1 + 1);
                ushort value2 = (ushort)Random.Range(selectedTier.Min2, selectedTier.Max2 + 1);

                equipment.Mods.Add(new ModInstance
                {
                    ModTableId = selectedMod.Id,
                    Slot = slot,
                    Tier = selectedTier.Tier,
                    Value1 = value1,
                    Value2 = value2,
                });
            }
        }

    }
}


