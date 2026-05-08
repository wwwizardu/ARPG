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

            // 아이템 데이터 생성 — ItemType별 인스턴스 데이터(SkillBook/SkillPage)도 자동으로 채워짐
            ItemData? itemData = CreateInventoryItemData(inItemId, inQuantity);
            if (itemData == null)
            {
                Addressables.ReleaseInstance(itemObject);
                return false;
            }

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

        /// <summary>
        /// 이미 만들어진 ItemData를 월드에 드랍 (스킬북 등 인스턴스 변동분이 있는 아이템용).
        /// CreateItem(itemId)가 일반 ItemData만 만드는 것과 달리, 외부에서 SkillBookData 등을 미리 채워 전달.
        /// ItemInstanceId가 0이면 새로 발급.
        /// </summary>
        public async Task<bool> CreateItemFromData(ItemData itemData, Vector3 position)
        {
            if (itemData == null || itemData.Table == null)
            {
                Debug.LogError("[ItemManager] CreateItemFromData - itemData or Table is null");
                return false;
            }

            var handle = Addressables.InstantiateAsync("Item/Item", position, Quaternion.identity);
            var itemObject = await handle.Task;
            if (itemObject == null)
            {
                Debug.LogError("[ItemManager] CreateItemFromData - InstantiateAsync returned null");
                return false;
            }

            var item = itemObject.GetComponent<ItemObject>();
            if (item == null)
            {
                Addressables.ReleaseInstance(itemObject);
                Debug.LogError("[ItemManager] CreateItemFromData - ItemObject component missing");
                return false;
            }

            if (itemData.ItemInstanceId == 0)
            {
                itemData.ItemInstanceId = _instanceIdCounter++;
            }

            item.Initialize(itemData);

            if (_itemInstances.ContainsKey(itemData.ItemInstanceId))
            {
                Debug.LogError($"[ItemManager] CreateItemFromData - InstanceId already exists: {itemData.ItemInstanceId}");
                Addressables.ReleaseInstance(itemObject);
                return false;
            }

            _itemInstances[itemData.ItemInstanceId] = item;
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

        // ========== 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.6) ==========

        /// <summary>
        /// 등급 책 ItemId + SkillId 조합으로 스킬북 ItemData 생성.
        /// </summary>
        public ItemData? CreateSkillBook(int itemId, int skillId)
        {
            ItemTable? itemTable = AR.s.Data?.GetItem(itemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemManager] CreateSkillBook - ItemTable not found, itemId({itemId})");
                return null;
            }
            if (itemTable.ItemType != GlobalEnum.ItemType.SkillBook)
            {
                Debug.LogError($"[ItemManager] CreateSkillBook - ItemId({itemId}) is not SkillBook (ItemType={itemTable.ItemType})");
                return null;
            }

            SkillTable? skillTable = AR.s.Data?.GetSkill(skillId);
            if (skillTable == null)
            {
                Debug.LogError($"[ItemManager] CreateSkillBook - SkillTable not found, skillId({skillId})");
                return null;
            }

            // 인스턴스 roll (SKILL_RUNE_DESIGN.md §3.1)
            // - PageCapacityBonus: +1~+5 균등 랜덤 — 같은 등급 책 사이에서도 용량 차별화
            // - PageSlotsBonus  : 50% 확률 +1 — 같은 등급 책 사이에서도 가끔 슬롯 1개 추가
            // v1 정책: 모든 등급에 동일 적용. 향후 등급별 차등이 필요하면 RollSkillBookBonuses에 itemTable.Tier 인자 추가.
            (int capBonus, int slotsBonus) = RollSkillBookBonuses();

            return new ItemData
            {
                Id = itemId,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = itemTable,
                SkillBook = new SkillBookData
                {
                    SkillId = skillId,
                    Table = skillTable,
                    PageCapacityBonus = capBonus,
                    PageSlotsBonus = slotsBonus,
                }
            };
        }

        private static (int CapacityBonus, int SlotsBonus) RollSkillBookBonuses()
        {
            int capBonus = Random.Range(1, 6);              // 1~5 inclusive
            int slotsBonus = Random.Range(0, 2);            // 0 또는 1 (50% 확률 +1)
            return (capBonus, slotsBonus);
        }

        /// <summary>
        /// 지정 SkillId로 스킬북 생성. 책 ItemId는 SkillTable.Tier에 매칭되는 등급 책으로 자동 선택.
        /// 치트/시나리오 보상 등 "이 스킬을 주고 싶다"는 케이스에 사용. 매칭 책이 없거나 SkillTable 부재 시 null.
        /// </summary>
        public ItemData? CreateSkillBookForSkill(int skillId)
        {
            SkillTable? skillTable = AR.s.Data?.GetSkill(skillId);
            if (skillTable == null)
            {
                Debug.LogError($"[ItemManager] CreateSkillBookForSkill - SkillTable not found, skillId({skillId})");
                return null;
            }

            int itemId = GetSkillBookItemIdByTier(skillTable.Tier);
            if (itemId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateSkillBookForSkill - No SkillBook ItemTable for Tier({skillTable.Tier}). SkillId={skillId}, SkillName={skillTable.Name}");
                return null;
            }

            return CreateSkillBook(itemId, skillId);
        }

        /// <summary>
        /// 등급(Tier)에 해당하는 책 ItemId를 찾고, 같은 Tier의 스킬 풀에서 랜덤 SkillId를 뽑아 스킬북 생성.
        /// 드랍/상점 stock 생성 시 사용. 풀이 비어 있으면 null 반환.
        /// </summary>
        public ItemData? CreateRandomSkillBookOfTier(int tier)
        {
            int itemId = GetSkillBookItemIdByTier(tier);
            if (itemId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillBookOfTier - No SkillBook ItemTable for tier({tier})");
                return null;
            }

            int skillId = PickRandomSkillByTier(tier);
            if (skillId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillBookOfTier - No skills with Tier({tier})");
                return null;
            }

            return CreateSkillBook(itemId, skillId);
        }

        /// <summary>
        /// 스킬 페이지 ItemId + SkillEffectId 조합으로 스킬 페이지 ItemData 생성.
        /// </summary>
        public ItemData? CreateSkillPage(int itemId, int skillEffectId)
        {
            ItemTable? itemTable = AR.s.Data?.GetItem(itemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemManager] CreateSkillPage - ItemTable not found, itemId({itemId})");
                return null;
            }
            if (itemTable.ItemType != GlobalEnum.ItemType.SkillPage)
            {
                Debug.LogError($"[ItemManager] CreateSkillPage - ItemId({itemId}) is not SkillPage (ItemType={itemTable.ItemType})");
                return null;
            }

            SkillEffectTable? effectTable = AR.s.Data?.GetSkillEffect(skillEffectId);
            if (effectTable == null)
            {
                Debug.LogError($"[ItemManager] CreateSkillPage - SkillEffectTable not found, skillEffectId({skillEffectId})");
                return null;
            }
            if (effectTable.PageCost <= 0)
            {
                Debug.LogWarning($"[ItemManager] CreateSkillPage - SkillEffect({skillEffectId}) has PageCost <= 0 and cannot be used as a SkillPage");
                return null;
            }

            return new ItemData
            {
                Id = itemId,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = itemTable,
                SkillPage = new SkillPageData
                {
                    SkillEffectId = skillEffectId,
                    Table = effectTable,
                }
            };
        }

        /// <summary>
        /// 등급(Tier)에 해당하는 페이지 ItemId를 찾고, PageCost 범위에 맞는 SkillEffect 중 랜덤으로 페이지 생성.
        /// </summary>
        public ItemData? CreateRandomSkillPageOfTier(int tier)
        {
            int itemId = GetSkillPageItemIdByTier(tier);
            if (itemId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillPageOfTier - No SkillPage ItemTable for tier({tier})");
                return null;
            }

            int skillEffectId = PickRandomSkillPageEffectByTier(tier);
            if (skillEffectId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillPageOfTier - No SkillEffect with PageCost range for Tier({tier})");
                return null;
            }

            return CreateSkillPage(itemId, skillEffectId);
        }

        // ========== 공통 팩토리 ==========

        /// <summary>
        /// ItemId 하나로 인벤토리에 들어갈 ItemData를 ItemType에 맞게 생성한다.
        /// SkillBook/SkillPage는 인스턴스 데이터(SkillBookData/SkillPageData)가 빈 채로 만들어지지 않도록
        /// 등급 기반 랜덤 헬퍼로 위임한다. CreateItem(월드 드랍)·UICheat·DropHelper 모두 이 함수를 거쳐야
        /// 인스턴스 데이터 누락이 일어나지 않는다.
        /// </summary>
        public ItemData? CreateInventoryItemData(int itemId, int quantity = 1)
        {
            ItemTable? itemTable = AR.s.Data?.GetItem(itemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemManager] CreateInventoryItemData - ItemTable not found, itemId({itemId})");
                return null;
            }

            switch (itemTable.ItemType)
            {
                case GlobalEnum.ItemType.SkillBook:
                    return CreateRandomSkillBookOfTier(itemTable.Tier);

                case GlobalEnum.ItemType.SkillPage:
                    return CreateRandomSkillPageOfTier(itemTable.Tier);

                default:
                    return new ItemData
                    {
                        Id = itemId,
                        ItemInstanceId = _instanceIdCounter++,
                        Quantity = quantity,
                        Table = itemTable,
                        Equipment = CreateEquipmentData(itemTable),
                    };
            }
        }

        /// <summary>
        /// ItemTable에서 ItemType==SkillBook && Tier==tier 인 첫 행의 ItemId 반환. 없으면 0.
        /// </summary>
        private int GetSkillBookItemIdByTier(int tier)
        {
            if (AR.s.Data == null)
                return 0;

            List<ItemTable> all = AR.s.Data.GetAllItems();
            for (int i = 0; i < all.Count; i++)
            {
                ItemTable t = all[i];
                if (t.ItemType == GlobalEnum.ItemType.SkillBook && t.Tier == tier)
                    return t.Id;
            }
            return 0;
        }

        /// <summary>
        /// ItemTable에서 ItemType==SkillPage && Tier==tier 인 첫 행의 ItemId 반환. 없으면 0.
        /// </summary>
        private int GetSkillPageItemIdByTier(int tier)
        {
            if (AR.s.Data == null)
                return 0;

            List<ItemTable> all = AR.s.Data.GetAllItems();
            for (int i = 0; i < all.Count; i++)
            {
                ItemTable t = all[i];
                if (t.ItemType == GlobalEnum.ItemType.SkillPage && t.Tier == tier)
                    return t.Id;
            }
            return 0;
        }

        private static int GetSkillPageTierByCost(int pageCost)
        {
            if (pageCost <= 10) return 1;
            if (pageCost <= 25) return 2;
            return 3;
        }

        private int PickRandomSkillPageEffectByTier(int tier)
        {
            if (AR.s.Data == null)
                return 0;

            List<SkillEffectTable> all = AR.s.Data.GetAllSkillEffects();
            List<int> matched = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                SkillEffectTable effect = all[i];
                if (effect.PageCost <= 0)
                    continue;
                if (GetSkillPageTierByCost(effect.PageCost) == tier)
                    matched.Add(effect.Id);
            }

            if (matched.Count == 0)
                return 0;

            return matched[Random.Range(0, matched.Count)];
        }

        /// <summary>
        /// SkillTable.Tier == tier 인 스킬들 중 균등 랜덤 픽. 없으면 0.
        /// </summary>
        private int PickRandomSkillByTier(int tier)
        {
            if (AR.s.Data == null)
                return 0;

            List<SkillTable> all = AR.s.Data.GetAllSkills();
            List<int> matched = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Tier == tier)
                    matched.Add(all[i].Id);
            }

            if (matched.Count == 0)
                return 0;

            return matched[Random.Range(0, matched.Count)];
        }

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


