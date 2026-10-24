#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ARPG.Item
{
    /// <summary>
    /// 스킬북 인스턴스 보너스 범위 (ITEM_DROP_BALANCE_DESIGN §7.4). 플레이어 드롭은 구간표 값을 넘기고,
    /// 넘기지 않으면(NPC 전리품·상점) 옛 고정 규칙(용량 +1~5, 슬롯 +1 50%)과 결정론 난수 순서를 그대로 쓴다.
    /// </summary>
    public readonly struct SkillBookBonusRange
    {
        public readonly int CapacityMin;
        public readonly int CapacityMax;
        public readonly int SlotChancePercent;

        public SkillBookBonusRange(int capacityMin, int capacityMax, int slotChancePercent)
        {
            CapacityMin = capacityMin;
            CapacityMax = Mathf.Max(capacityMin, capacityMax);
            SlotChancePercent = Mathf.Clamp(slotChancePercent, 0, 100);
        }
    }

    public partial class ItemManager : MonoBehaviour
    {
        // DataManager가 ItemManager.Initialize보다 먼저 신규 PlayerData를 만들 수 있으므로
        // 필드 초기값부터 유효 ID 범위(1+)를 사용한다.
        private int _instanceIdCounter = 1;
        private Dictionary<int, ItemObject> _itemInstances = new Dictionary<int, ItemObject>();
        private ItemEnchantmentService _enchantment = null!;
        // 옵션 티어 선택용 scratch — 쓸 때마다 비운다
        private readonly List<ModTierTable> _tierCandidateScratch = new List<ModTierTable>();
        private readonly List<int> _tierWeightScratch = new List<int>();
        private readonly List<int> _specialModIndexScratch = new List<int>();
        public int NextItemInstanceId => _instanceIdCounter;
        public ItemEnchantmentService Enchantment => _enchantment;

        public void Initialize()
        {
            _instanceIdCounter = Mathf.Max(1, AR.s.Data.NextItemInstanceId);
            _enchantment = new ItemEnchantmentService();
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

        public async Task<bool> CreateItem(int inItemId, int inQuantity, Vector3 inPosition,
            EquipmentRollOptions rollOptions = default)
        {
            if (inQuantity <= 0) return false;
            ItemData? item = CreateInventoryItemData(inItemId, inQuantity, null, rollOptions);
            return item != null && await CreateItemFromData(item, inPosition);
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

            using var contextLoad = AR.s.Map.Context.BeginLoad();
            if (itemData.ItemInstanceId == 0) itemData.ItemInstanceId = _instanceIdCounter++;
            RecordContextDrop(itemData, position);
            var handle = Addressables.InstantiateAsync("Item/Item", position, Quaternion.identity);
            var itemObject = await handle.Task;
            if (contextLoad.IsCurrent == false)
            {
                if (itemObject != null) Addressables.ReleaseInstance(itemObject);
                return false;
            }
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
                RemoveContextDrop(inItem.ItemData.ItemInstanceId);
                AR.s.Item.DestroyItem(inItem.ItemData.ItemInstanceId);

                _ = AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | (AR.s.Dungeon.IsInside ? SaveScope.Dungeons : SaveScope.Map));
                return true;
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
                item.OnEntityDestroy();
                AR.s.System.GetSystem<Systems.System_Render>()?.UnregisterEntity(item.EntityId);
                AR.s.Entity.DestroyEntity(item.EntityId);
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
        /// inRng가 있으면 인스턴스 보너스 롤도 결정론 RNG를 사용한다 (V5 상점 재고 §8.5).
        /// </summary>
        public ItemData? CreateSkillBook(int itemId, int skillId, HistoryRng? inRng = null,
            SkillBookBonusRange? bonusRange = null)
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
            // 플레이어 드롭은 구간표 범위를 쓴다 (ITEM_DROP_BALANCE_DESIGN §7.4. 레벨 90: 용량 +3~5, 슬롯 70%).
            (int capBonus, int slotsBonus) = RollSkillBookBonuses(inRng, bonusRange);

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

        private static (int CapacityBonus, int SlotsBonus) RollSkillBookBonuses(
            HistoryRng? inRng, SkillBookBonusRange? bonusRange)
        {
            if (bonusRange.HasValue == true)
            {
                SkillBookBonusRange range = bonusRange.Value;
                int bandCapacity = RollRange(inRng, range.CapacityMin, range.CapacityMax + 1);
                int bandSlots = RollRange(inRng, 0, 100) < range.SlotChancePercent ? 1 : 0;
                return (bandCapacity, bandSlots);
            }

            // 옛 고정 규칙 — NPC 전리품·상점 재고의 결정론 난수 순서를 바꾸지 않는다
            int capBonus = RollRange(inRng, 1, 6);          // 1~5 inclusive
            int slotsBonus = RollRange(inRng, 0, 2);        // 0 또는 1 (50% 확률 +1)
            return (capBonus, slotsBonus);
        }

        /// <summary>
        /// V5 (§8.5): 결정론 RNG가 주어지면 HistoryRng, 없으면 기존 UnityEngine.Random.
        /// 두 경로 모두 [minInclusive, maxExclusive) 의미가 같다.
        /// </summary>
        private static int RollRange(HistoryRng? inRng, int minInclusive, int maxExclusive)
        {
            if (inRng == null)
                return Random.Range(minInclusive, maxExclusive);
            return inRng.Range(minInclusive, maxExclusive);
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
        /// 등급(Tier)에 해당하는 책 ItemId를 찾고, 같은 Tier의 DropEnabled 스킬 풀에서 랜덤 SkillId를 뽑아 스킬북 생성.
        /// 드랍/상점 stock 생성 시 사용. 풀이 비어 있으면 null 반환.
        /// </summary>
        public ItemData? CreateRandomSkillBookOfTier(int tier, HistoryRng? inRng = null,
            SkillBookBonusRange? bonusRange = null)
        {
            int itemId = GetSkillBookItemIdByTier(tier);
            if (itemId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillBookOfTier - No SkillBook ItemTable for tier({tier})");
                return null;
            }

            int skillId = PickRandomSkillByTier(tier, inRng);
            if (skillId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillBookOfTier - No drop-enabled skills with Tier({tier})");
                return null;
            }

            return CreateSkillBook(itemId, skillId, inRng, bonusRange);
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
        /// 등급(Tier)에 해당하는 페이지 ItemId와 SkillEffect 중 하나를 골라 페이지를 생성.
        /// </summary>
        public ItemData? CreateRandomSkillPageOfTier(int tier, HistoryRng? inRng = null)
        {
            int itemId = GetSkillPageItemIdByTier(tier);
            if (itemId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillPageOfTier - No SkillPage ItemTable for tier({tier})");
                return null;
            }

            int skillEffectId = PickRandomSkillPageEffectByTier(tier, inRng);
            if (skillEffectId == 0)
            {
                Debug.LogWarning($"[ItemManager] CreateRandomSkillPageOfTier - No usable SkillEffect for Tier({tier})");
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
        /// rollOptions는 장비일 때만 쓰인다(아이템 레벨·특수 티어 확률). 기본값은 아이템 레벨 1, 특수 판정 없음.
        /// </summary>
        public ItemData? CreateInventoryItemData(int itemId, int quantity = 1,
            HistoryRng? inRng = null, EquipmentRollOptions rollOptions = default)
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
                    return CreateRandomSkillBookOfTier(itemTable.Tier, inRng);

                case GlobalEnum.ItemType.SkillPage:
                    return CreateRandomSkillPageOfTier(itemTable.Tier, inRng);

                // 참 판·참 (PLAYER_LEVEL_CHARM_DESIGN §6). 참은 행이 곧 모양이고, 아이템 레벨은 장비와 같은 rollOptions 값
                case GlobalEnum.ItemType.CharmBoard:
                    return CreateRandomCharmBoard(inRng);

                case GlobalEnum.ItemType.Charm:
                    return CreateCharm(CharmRules.GetShapeOfItemId(itemId), rollOptions.ItemLevel, inRng);

                default:
                    return new ItemData
                    {
                        Id = itemId,
                        ItemInstanceId = _instanceIdCounter++,
                        Quantity = quantity,
                        Table = itemTable,
                        Equipment = CreateEquipmentData(itemTable, inRng, rollOptions),
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

        private int PickRandomSkillPageEffectByTier(int tier, HistoryRng? inRng = null)
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
                if (effect.Tier == tier)
                    matched.Add(effect.Id);
            }

            if (matched.Count == 0)
                return 0;

            return matched[RollRange(inRng, 0, matched.Count)];
        }

        /// <summary>
        /// SkillTable.Tier == tier && DropEnabled인 스킬들 중 균등 랜덤 픽. 없으면 0.
        /// </summary>
        private int PickRandomSkillByTier(int tier, HistoryRng? inRng = null)
        {
            if (tier <= 0 || AR.s.Data == null)
                return 0;

            List<SkillTable> all = AR.s.Data.GetAllSkills();
            List<int> matched = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Tier == tier && all[i].DropEnabled == true)
                    matched.Add(all[i].Id);
            }

            if (matched.Count == 0)
                return 0;

            return matched[RollRange(inRng, 0, matched.Count)];
        }

        private EquipmentData? CreateEquipmentData(ItemTable inTable, HistoryRng? inRng = null,
            EquipmentRollOptions rollOptions = default)
        {
            if (inTable == null)
                return null;

            // 장비 자격 판별: 베이스 스탯이 있거나(일반 장비) Implicit이 있으면(유니크/특수) 장비.
            // 둘 다 없으면 비장비 아이템.
            bool hasBaseStat = AR.s.Data.GetEquipmentBaseStat(inTable.Id) != null;
            bool hasImplicit = AR.s.Data.GetItemImplicits(inTable.Id).Count > 0;
            if (hasBaseStat == false && hasImplicit == false)
                return null;

            int itemLevel = ItemLevelModTierRules.ClampItemLevel(rollOptions.ItemLevel);
            EquipmentData equipmentData = new EquipmentData()
            {
                Id = inTable.Id,
                ItemLevel = itemLevel,
            };

            // EquipType 설정
            equipmentData.EquipType = Utils.CategoryToEquipmentTypeMask(inTable.Category);
            if (rollOptions.HasDropGrade == false)
            {
                // 옛 품질 판정 자리(1~100). 값은 쓰지 않지만(D14) NPC 전리품·상점 매물의 결정론 난수 순서를 지키려고 굴린다
                RollRange(inRng, 1, 101);
            }

            // 1. Base Mod 생성 (일반 장비의 표준 베이스 스탯)
            equipmentData.InitBaseMods(inTable.Id);

            // 2. Implicit Mod 생성 (유니크/특수 아이템의 추가 옵션)
            equipmentData.InitImplicitMods(inTable.Id);

            // 3·4. Prefix/Postfix Mod 랜덤 생성
            if (rollOptions.HasDropGrade == true)
            {
                RollDropGradeMods(equipmentData, rollOptions, itemLevel, inRng);
            }
            else
            {
                // 공유 경로(NPC 전리품·상점 랜덤 매물): 접두 1~3 + 접미 1~3, 일반 티어 규칙 (D12)
                RollRandomMods(equipmentData, GlobalEnum.ModSlot.Prefix, RollRange(inRng, 1, 4), equipmentData.EquipType,
                    itemLevel, false, inRng);
                RollRandomMods(equipmentData, GlobalEnum.ModSlot.Postfix, RollRange(inRng, 1, 4), equipmentData.EquipType,
                    itemLevel, false, inRng);
            }

            // 5. T11·T12 특수 판정 (몬스터 드랍만 확률이 있다). 등급과 관계없이 장비 1개당 1번, 옵션이 없으면 대상이 없다
            TryApplySpecialTier(equipmentData, itemLevel, rollOptions, inRng);

            // 6. 생성된 Mod들의 테이블 참조 연결
            for (int i = 0; i < equipmentData.Mods.Count; i++)
            {
                equipmentData.Mods[i].OnLoadCompleted();
            }

            return equipmentData;
        }

        /// <summary>
        /// 시작 무기/시나리오 보상 등 "Mod 없는 깔끔한 베이스" 장비 생성.
        /// Base + Implicit만 적용, Prefix/Postfix 랜덤 롤 없음(일반 등급). 품질 0.
        /// </summary>
        public ItemData? CreateBaseEquipment(int itemId)
        {
            ItemTable? itemTable = AR.s.Data.GetItem(itemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemManager] CreateBaseEquipment - ItemTable not found, itemId({itemId})");
                return null;
            }

            bool hasBaseStat = AR.s.Data.GetEquipmentBaseStat(itemId) != null;
            bool hasImplicit = AR.s.Data.GetItemImplicits(itemId).Count > 0;
            if (hasBaseStat == false && hasImplicit == false)
            {
                Debug.LogError($"[ItemManager] CreateBaseEquipment - Item({itemId}) is not equipment (no Base/Implicit data)");
                return null;
            }

            EquipmentData equipmentData = new EquipmentData()
            {
                Id = itemId,
                Quality = EquipmentData.BasicItemQuality,
            };
            equipmentData.EquipType = Utils.CategoryToEquipmentTypeMask(itemTable.Category);

            equipmentData.InitBaseMods(itemId);
            equipmentData.InitImplicitMods(itemId);

            for (int i = 0; i < equipmentData.Mods.Count; i++)
            {
                equipmentData.Mods[i].OnLoadCompleted();
            }

            return new ItemData
            {
                Id = itemId,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = itemTable,
                Equipment = equipmentData,
            };
        }

        // ========== 상점 기본 아이템 (NPC_SHOP_EQUIPMENT_DESIGN.md §3.2, D116) ==========

        /// <summary>
        /// 기본 아이템 행 번호로 장비를 만든다: Base·Implicit(기존 테이블) + 고정 옵션 1개, 품질 고정, 무작위 롤 없음.
        /// 상점 진열(보장·특산 칸)·NPC 시작 장비가 이 진입점을 쓴다. 행이 없거나 잘못됐으면 null.
        /// </summary>
        public ItemData? CreateBasicItem(int basicItemId)
        {
            BasicItemTable? row = AR.s.Data.GetBasicItem(basicItemId);
            if (row == null)
            {
                Debug.LogError($"[ItemManager] CreateBasicItem - BasicItemTable not found or invalid, basicItemId({basicItemId})");
                return null;
            }

            ItemTable? itemTable = AR.s.Data.GetItem(row.ItemId);
            if (itemTable == null)
            {
                Debug.LogError($"[ItemManager] CreateBasicItem - ItemTable not found, itemId({row.ItemId})");
                return null;
            }

            return new ItemData
            {
                Id = row.ItemId,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = itemTable,
                Equipment = CreateBasicEquipmentData(row, itemTable),
            };
        }

        /// <summary>
        /// 기본 아이템 행으로 장비 데이터만 만든다. 기존 ItemData의 장비를 바꿔 끼울 때(인스턴스 번호 유지) 쓴다.
        /// </summary>
        public EquipmentData CreateBasicEquipmentData(BasicItemTable row, ItemTable itemTable)
        {
            EquipmentData equipmentData = new EquipmentData
            {
                Id = row.ItemId,
                EquipType = Utils.CategoryToEquipmentTypeMask(itemTable.Category),
            };
            equipmentData.ApplyBasicItemTemplate(row);
            return equipmentData;
        }

        /// <summary>
        /// 장비 아이템의 기본 아이템 변형 하나를 가중치로 고른다. roll01은 [0, 1) 값으로,
        /// 상점은 회차 RNG, NPC는 해시에서 만들어 결정론을 유지한다. 변형이 없으면 null.
        /// </summary>
        public BasicItemTable? PickBasicItemVariant(int itemId, double roll01)
        {
            IReadOnlyList<BasicItemTable> variants = AR.s.Data.GetBasicItemVariants(itemId);
            if (variants.Count == 0)
                return null;

            int totalWeight = 0;
            for (int i = 0; i < variants.Count; i++)
                totalWeight += variants[i].Weight;

            if (roll01 < 0d) roll01 = 0d;
            int target = (int)(roll01 * totalWeight);
            if (target >= totalWeight) target = totalWeight - 1;
            for (int i = 0; i < variants.Count; i++)
            {
                target -= variants[i].Weight;
                if (target < 0)
                    return variants[i];
            }
            return variants[variants.Count - 1];
        }

        /// <summary>
        /// 플레이어 드롭 등급별 옵션 (ITEM_DROP_BALANCE_DESIGN §4.3·§4.4, 품질 §5).
        /// - 일반: 옵션 없음, 품질은 드롭 쪽이 정한 값.
        /// - 마법: 1개면 접두·접미 후보 전체에서, 2개면 접두 1 + 접미 1. 티어는 마법 규칙(위 2단계 가중치 2배).
        /// - 희귀: 총수를 접두·접미에 각 3개 이하로 나눈다. 한쪽 후보가 모자라면 다른 쪽으로 채운다. 일반 티어 규칙.
        /// </summary>
        private void RollDropGradeMods(EquipmentData equipment, EquipmentRollOptions rollOptions, int itemLevel,
            HistoryRng? inRng)
        {
            GlobalEnum.EquipmentTypeMask equipType = equipment.EquipType;
            switch (rollOptions.Grade)
            {
                case GlobalEnum.EquipmentGrade.Normal:
                    equipment.Quality = EquipmentQualityRules.ClampQuality(rollOptions.Quality);
                    return;

                case GlobalEnum.EquipmentGrade.Magic:
                    if (rollOptions.AffixCount <= 1)
                    {
                        RollRandomModAnySlot(equipment, equipType, itemLevel, true, inRng);
                    }
                    else
                    {
                        RollRandomMods(equipment, GlobalEnum.ModSlot.Prefix, 1, equipType, itemLevel, true, inRng);
                        RollRandomMods(equipment, GlobalEnum.ModSlot.Postfix, 1, equipType, itemLevel, true, inRng);
                    }
                    return;

                default:
                {
                    int limit = ItemEnchantmentRules.AFFIX_LIMIT;
                    int total = Mathf.Clamp(rollOptions.AffixCount, 1, limit * 2);
                    int prefixTarget = RollRange(inRng, Mathf.Max(0, total - limit), Mathf.Min(limit, total) + 1);
                    int prefixAdded = RollRandomMods(equipment, GlobalEnum.ModSlot.Prefix, prefixTarget, equipType,
                        itemLevel, false, inRng);
                    int postfixAdded = RollRandomMods(equipment, GlobalEnum.ModSlot.Postfix,
                        Mathf.Min(limit, total - prefixAdded), equipType, itemLevel, false, inRng);
                    int remaining = total - prefixAdded - postfixAdded;
                    if (remaining > 0 && prefixAdded < limit)
                        RollRandomMods(equipment, GlobalEnum.ModSlot.Prefix, Mathf.Min(remaining, limit - prefixAdded),
                            equipType, itemLevel, false, inRng);
                    return;
                }
            }
        }

        /// <summary>마법 장비 옵션 1개: 접두·접미 후보 전체에서 옵션을 균등하게 고른다.</summary>
        private void RollRandomModAnySlot(EquipmentData equipment, GlobalEnum.EquipmentTypeMask equipType, int itemLevel,
            bool magicRule, HistoryRng? inRng)
        {
            List<ModTable> prefixPool = AR.s.Data.GetModPool(GlobalEnum.ModSlot.Prefix, equipType);
            List<ModTable> postfixPool = AR.s.Data.GetModPool(GlobalEnum.ModSlot.Postfix, equipType);
            int total = prefixPool.Count + postfixPool.Count;
            if (total <= 0)
                return;

            GlobalEnum.ModSlot slot = RollRange(inRng, 0, total) < prefixPool.Count
                ? GlobalEnum.ModSlot.Prefix : GlobalEnum.ModSlot.Postfix;
            if (RollRandomMods(equipment, slot, 1, equipType, itemLevel, magicRule, inRng) == 0)
            {
                // 고른 쪽에 티어가 열린 옵션이 없으면 반대쪽에서 붙인다
                GlobalEnum.ModSlot other = slot == GlobalEnum.ModSlot.Prefix
                    ? GlobalEnum.ModSlot.Postfix : GlobalEnum.ModSlot.Prefix;
                RollRandomMods(equipment, other, 1, equipType, itemLevel, magicRule, inRng);
            }
        }

        /// <summary>
        /// 지정 슬롯에 랜덤 Mod를 롤링하여 추가. 티어는 아이템 레벨의 일반 판정 후보(T1~T10)에서 가중치로 고른다.
        /// magicRule이면 후보 중 위 2단계 가중치를 2배로 준다(마법 규칙). 실제로 붙인 개수를 돌려준다.
        /// </summary>
        private int RollRandomMods(EquipmentData equipment, GlobalEnum.ModSlot slot, int count,
            GlobalEnum.EquipmentTypeMask equipType, int itemLevel, bool magicRule, HistoryRng? inRng = null)
        {
            List<ModTable> modPool = AR.s.Data.GetModPool(slot, equipType);
            for (int i = modPool.Count - 1; i >= 0; i--)
            {
                if (equipment.HasModGroupInSlot(slot, modPool[i].Group) == true)
                    modPool.RemoveAt(i);
            }

            int addedCount = 0;
            while (addedCount < count && modPool.Count > 0)
            {
                int selectedIndex = RollRange(inRng, 0, modPool.Count);
                ModTable selectedMod = modPool[selectedIndex];
                List<ModTierTable> tiers = AR.s.Data.GetModTiers(selectedMod.Id);
                if (ItemLevelModTierRules.BuildNormalTierCandidates(tiers, itemLevel,
                        _tierCandidateScratch, _tierWeightScratch, magicRule) == 0)
                {
                    modPool.RemoveAt(selectedIndex);
                    continue;
                }

                // 난수 1회 — 결정론 RNG 경로(상점·NPC 전리품)의 뽑기 횟수는 이전과 같다
                int tierRoll = RollRange(inRng, 0, ItemLevelModTierRules.GetTotalWeight(_tierWeightScratch));
                ModTierTable selectedTier =
                    _tierCandidateScratch[ItemLevelModTierRules.SelectWeightedIndex(_tierWeightScratch, tierRoll)];

                // 값 롤링
                ushort value1 = (ushort)RollRange(inRng, selectedTier.Min1, selectedTier.Max1 + 1);
                ushort value2 = (ushort)RollRange(inRng, selectedTier.Min2, selectedTier.Max2 + 1);

                equipment.Mods.Add(new ModInstance
                {
                    ModTableId = selectedMod.Id,
                    Slot = slot,
                    Tier = selectedTier.Tier,
                    Value1 = value1,
                    Value2 = value2,
                });
                addedCount++;

                if (string.IsNullOrEmpty(selectedMod.Group) == false)
                {
                    for (int i = modPool.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(modPool[i].Group, selectedMod.Group, System.StringComparison.Ordinal) == true)
                            modPool.RemoveAt(i);
                    }
                }
            }
            return addedCount;
        }

        /// <summary>
        /// T11·T12 특수 판정: 장비 하나당 한 번 굴려 성공하면 이미 붙은 Prefix/Postfix 하나를 그 티어로 올리고 수치를 다시 뽑는다.
        /// 확률이 없으면(몬스터 드랍 외 경로) 난수를 쓰지 않는다. 그 티어 행이 없거나 RequiredLevel(아이템 레벨 90)에
        /// 못 미치는 옵션은 대상에서 빠지고, 대상이 없으면 아무것도 바꾸지 않는다.
        /// </summary>
        private void TryApplySpecialTier(EquipmentData equipment, int itemLevel, EquipmentRollOptions rollOptions,
            HistoryRng? inRng)
        {
            if (rollOptions.HasSpecialTierChance == false)
                return;

            int targetTier = ItemLevelModTierRules.ResolveSpecialTier(rollOptions.Tier11ChanceBp,
                rollOptions.Tier12ChanceBp, RollRange(inRng, 0, ItemLevelModTierRules.CHANCE_SCALE));
            if (targetTier == 0)
                return;

            _tierCandidateScratch.Clear();
            _specialModIndexScratch.Clear();
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                ModInstance mod = equipment.Mods[i];
                if (mod.Slot != GlobalEnum.ModSlot.Prefix && mod.Slot != GlobalEnum.ModSlot.Postfix)
                    continue;
                ModTierTable? special = ItemLevelModTierRules.FindSpecialTier(
                    AR.s.Data.GetModTiers(mod.ModTableId), targetTier, itemLevel);
                if (special == null)
                    continue;
                _tierCandidateScratch.Add(special);
                _specialModIndexScratch.Add(i);
            }
            if (_specialModIndexScratch.Count == 0)
                return;

            int pick = RollRange(inRng, 0, _specialModIndexScratch.Count);
            ModTierTable selectedTier = _tierCandidateScratch[pick];
            ModInstance target = equipment.Mods[_specialModIndexScratch[pick]];
            target.Tier = selectedTier.Tier;
            target.Value1 = (ushort)RollRange(inRng, selectedTier.Min1, selectedTier.Max1 + 1);
            target.Value2 = (ushort)RollRange(inRng, selectedTier.Min2, selectedTier.Max2 + 1);
            Debug.Log($"[ItemManager] 특수 티어 T{selectedTier.Tier} 부여 — ItemId={equipment.Id}, " +
                $"ItemLevel={itemLevel}, ModId={target.ModTableId}");
        }

    }
}


