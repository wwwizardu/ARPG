#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Village;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 야망·모험가 경제 P1a (NPC_AMBITION_DESIGN §5, §7.1, §10.1).
    /// - 장비 반영 전투력 런타임 캐시 (저장하지 않음, 장비가 바뀔 때만 다시 계산)
    /// - 개인 상한 기준값(InitialPower/PowerCapBonus)과 시작 Gold의 1회 결정론 초기화 (D37, D38)
    /// - Hunt 성공 전리품·장물의 개인 인벤토리 적재, 가득 차면 가치 최저 버림 (D35). 장착은 하지 않는다 (NPC_SHOP_EQUIPMENT D119)
    /// - 문턱·상한을 지키는 업그레이드 장착 (상점 구매 전용), 기존 세이브 장비의 기본 아이템 일괄 교체 (D121)
    /// </summary>
    public partial class NpcManager
    {
        // 개인 편차 (D38, §10.1). entityId 해시로 강자 여부와 편차를 정한다.
        private const float StrongNpcChance = 0.10f;
        private const float StrongPowerCapBonusMin = 0.35f;
        private const float StrongPowerCapBonusMax = 0.50f;
        private const float NormalPowerCapBonus = 0.20f;
        // 장비가 하나도 없어 전투력이 0인 NPC도 "초기화됨"으로 표시하기 위한 하한 (0 이하 = 미초기화 표시와 구분).
        private const float MinInitialPower = 0.01f;
        // 시작 Gold (D37). entityId 해시 절반 + 탐욕·인내 각 1/4 비중으로 30~90G.
        private const int StartGoldMin = 30;
        private const int StartGoldMax = 90;
        // 업그레이드 장착 문턱 (§7.2·§10.1). 전리품은 장착하지 않으므로(NPC_SHOP_EQUIPMENT D119) 상점 구매에만 쓴다.
        private const float UpgradeMinGainRatio = 0.15f;
        private const float NearCapUpgradeMinGainRatio = 0.30f;
        private const float NearCapPowerRatio = 0.80f;

        // 장비 전투력 캐시 (EntityId → NpcPower). 휘발성 — 세이브 대상 아님. Reset/Load에서 비운다.
        private readonly Dictionary<int, float> _npcPowerCache = new();
        // 실체화(또는 스폰 대기) 중에 산 업그레이드 장비의 ItemInstanceId.
        // 실체화 중 장비를 바꾸면 ECS 스탯 재적용이 필요하므로 비실체화 시점까지 장착을 미룬다. 휘발성.
        private readonly Dictionary<int, List<int>> _deferredPurchaseUpgradeItemIds = new();
        // 업그레이드 평가용 스크래치 버퍼. 사용 전에 매번 현재 장비로 덮어쓰므로 호출 사이에 상태를 남기지 않는다.
        private readonly ItemData?[] _upgradeScratchEquip = new ItemData?[(int)GE.EquipSlotType.Max];

        [Header("NPC Ambition")]
        // 전리품 적재·버림 로그. 사냥마다 찍히므로 기본은 끈다. 업그레이드 장착 로그는 항상 남긴다 (P1c 진단 집계용).
        [SerializeField] private bool _logAmbitionLoot = false;

        /// <summary>진단 도구용 읽기 전용 뷰. Save()와 달리 활성 NPC 스냅샷 같은 부수 효과가 없다.</summary>
        public IReadOnlyDictionary<int, NpcSaveData> SaveDataView => _npcSaveDict;

        /// <summary>장비 점수 + 스킬 점수 (용기 제외, D31). 캐시에 없으면 계산해 넣는다.</summary>
        public float GetNpcPower(int entityId)
        {
            if (_npcPowerCache.TryGetValue(entityId, out float cached))
                return cached;
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return 0f;

            float power = NpcPowerEvaluator.Evaluate(saveData);
            _npcPowerCache[entityId] = power;
            return power;
        }

        /// <summary>Hunt 판정용 capability(0~1). 솔로·파티·Food 보상이 모두 이 값을 쓴다.</summary>
        public float GetNpcHuntCapability(int entityId)
        {
            return NpcActivityResolver.GetHuntCapability(GetNpcPower(entityId));
        }

        /// <summary>개인 상한 = InitialPower × (1 + PowerCapBonus). 미초기화면 0.</summary>
        public static float GetNpcPowerCap(NpcSaveData saveData)
        {
            if (saveData.InitialPower <= 0f)
                return 0f;
            return saveData.InitialPower * (1f + saveData.PowerCapBonus);
        }

        /// <summary>
        /// Idle 전환 시 다음 활동을 고른다 (P1c, NPC_AMBITION_DESIGN §6, D39, D40).
        /// NPC·상점 상태로 필요·기회를 채워 NpcActivityResolver의 효용 선택에 넘긴다. 동행 예약·결투 제외는 호출자 몫.
        /// </summary>
        public NpcActivityKind SelectNextActivity(int entityId, NpcSaveData saveData, float startGameTime, int worldSeed)
        {
            // Idle 전환 = 단기 목표 평가 시점 (NPC_OBJECTIVE_DESIGN §4.7): 조건형 계기·기한·대상 유효·같은 종류 갱신·다시 계산.
            EvaluateObjectiveOnIdle(entityId, saveData, startGameTime);
            NpcActivityChoiceContext context = BuildActivityChoiceContext(entityId, saveData, startGameTime);
            return NpcActivityResolver.SelectNextActivity(saveData, context, entityId, startGameTime, worldSeed);
        }

        /// <summary>
        /// §6.2 필요(골드 부족·가방 가득·부상 회복 직후)와 Trade·Delve 기회, 단기 목표 배율(NPC_OBJECTIVE_DESIGN §7.1).
        /// 장비 부족·라이벌 필요는 기준 데이터가 생기는 P3 이후에 추가한다.
        /// </summary>
        public NpcActivityChoiceContext BuildActivityChoiceContext(int entityId, NpcSaveData saveData, float gameTime)
        {
            // 판정만 하므로 표시하지 않는다. 칸 보정은 생성·로드 때 끝나 있어 여기서 바뀌는 게 없다.
            saveData.EnsureSlotsInitialized();
            int freeSlots = 0;
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                if (saveData.Inventory[i] == null)
                    freeSlots++;
            }

            NpcActivityChoiceContext context = new NpcActivityChoiceContext
            {
                GoldShort = saveData.Gold < GetTreatmentFee(),
                BagFull = freeSlots <= NpcActivityResolver.BagFullFreeSlotThreshold,
                RecentlyRecovered = NpcActivityResolver.IsRecentlyRecovered(saveData.Activity, gameTime),
                TradeAvailable = HasTradeOpportunity(entityId, saveData),
                DelveAvailable = HasDelveOpportunity(saveData),
            };
            ApplyObjectiveActivityMultipliers(saveData, gameTime, ref context);
            return context;
        }

        /// <summary>장비가 바뀐 뒤 호출한다. 다음 조회 때 다시 계산된다.</summary>
        public void InvalidateNpcPower(int entityId)
        {
            _npcPowerCache.Remove(entityId);
        }

        /// <summary>
        /// Hunt·Delve 성공 전리품을 NPC 개인 인벤토리에 넣는다 (D35). 규칙은 GrantAcquiredItem과 같다.
        /// </summary>
        public void GrantHuntLoot(int entityId, NpcSaveData saveData, ItemData loot, float gameTime)
        {
            GrantAcquiredItem(entityId, saveData, loot, gameTime, "Loot");
        }

        /// <summary>
        /// 새로 얻은 아이템을 개인 인벤토리에 넣는다(가득 차면 가치 최저 버림, D35). 전리품(Hunt·Delve)과 절도(D101)가 같이 쓴다.
        /// 장비여도 장착하지 않는다 — 가방에 두었다가 Trade에서 판다. NPC가 강해지는 경로는 상점의 기본 아이템 구매뿐이다
        /// (NPC_SHOP_EQUIPMENT D119). source는 진단 로그 구분용("Loot"·"Theft").
        /// </summary>
        public void GrantAcquiredItem(int entityId, NpcSaveData saveData, ItemData item, float gameTime, string source)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Inventory | NpcSaveGroup.Equipment);
            if (item == null)
                return;

            saveData.EnsureSlotsInitialized();
            EnsureAmbitionInitialized(entityId, saveData);

            if (_logAmbitionLoot == true)
                Debug.Log($"[NpcAmbition] {source} npc={entityId} item={DescribeItem(item)} t={gameTime:F2}");

            StoreInInventory(entityId, saveData, item);
        }

        /// <summary>
        /// 소속 마을 청크의 Zone (D35). 마을이 없는 NPC는 현재 위치 청크로 대신한다.
        /// </summary>
        public int GetNpcLootZoneLevel(NpcSaveData saveData)
        {
            Vector2 position = saveData.Position;
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            if (village != null)
                position = village.Position;

            // 월드 시뮬 경로라 던전 안에서도 월드 맵 Zone을 본다 (DUNGEON_WORLD_SIM §4)
            int chunkSize = _chunkSize > 0 ? _chunkSize : AR.s.WorldMap.chunkSize;
            Vector2Int chunk = new Vector2Int(Mathf.FloorToInt(position.x / chunkSize),
                Mathf.FloorToInt(position.y / chunkSize));
            return AR.s.WorldMap.GetZone(chunk);
        }

        /// <summary>
        /// InitialPower가 0 이하(미초기화)면 현재 장비로 개인 상한 기준값·편차·시작 Gold를 1회 기록한다 (D37, D38).
        /// 신규 NPC는 장비 시드 직후, 기존 NPC는 로드 직후 같은 함수로 처리한다.
        /// </summary>
        private void EnsureAmbitionInitialized(int entityId, NpcSaveData saveData)
        {
            if (saveData.InitialPower > 0f)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Identity | NpcSaveGroup.Wallet);

            float power = NpcPowerEvaluator.Evaluate(saveData);
            _npcPowerCache[entityId] = power;

            // worldSeed와 무관한 entityId 해시 — 같은 NPC는 어느 시점에 초기화돼도 같은 편차·Gold를 받는다.
            HistoryRng rng = new HistoryRng(DerivedSeed.Derive(entityId, DerivedSeed.ChannelNpcAmbition));
            double strongRoll = rng.NextDouble();
            double bonusRoll = rng.NextDouble();
            double goldRoll = rng.NextDouble();

            saveData.InitialPower = Mathf.Max(power, MinInitialPower);
            saveData.PowerCapBonus = strongRoll < StrongNpcChance
                ? Mathf.Lerp(StrongPowerCapBonusMin, StrongPowerCapBonusMax, (float)bonusRoll)
                : NormalPowerCapBonus;

            float greed = Mathf.Clamp01(saveData.NpcStat.Greed / 100f);
            float patience = Mathf.Clamp01(saveData.NpcStat.Patience / 100f);
            float goldFactor = Mathf.Clamp01((float)goldRoll * 0.5f + greed * 0.25f + patience * 0.25f);
            int startGold = StartGoldMin + Mathf.RoundToInt((StartGoldMax - StartGoldMin) * goldFactor);
            // GOLD_BALANCE D128: 시작 Gold(보충 NPC 포함)는 서비스 물가를 따른다 — 닫힌 경제 밖의 생성처로 문서화돼 있다.
            saveData.Gold = AR.s.Village != null ? AR.s.Village.GetServicePrice(startGold) : startGold;
        }

        /// <summary>치료비 = 기본 10G × 서비스 물가 배율 (D36·GOLD_BALANCE D128). 치료 판정·구매 예산·골드 부족 필요가 같은 값을 쓴다.</summary>
        public static int GetTreatmentFee()
        {
            return AR.s.Village != null
                ? AR.s.Village.GetServicePrice(NpcActivityResolver.TreatmentFee)
                : NpcActivityResolver.TreatmentFee;
        }

        /// <summary>
        /// 기존 세이브 일괄 교체 (NPC_SHOP_EQUIPMENT D121, WorldDataVersion 17). 장착 장비를 같은 ItemId의 기본 아이템 변형
        /// (시작 장비와 같은 entityId·슬롯 해시)으로 바꾼다. ItemData 객체와 ItemInstanceId는 그대로 두어 장비 소문(ItemOwner)이
        /// 계속 유효하다. 가방·플레이어·상점 재고는 건드리지 않는다. 이미 기본 아이템인 장비는 건너뛴다.
        /// 교체했으면 개인 상한 기준(InitialPower)만 새 장비로 다시 계산한다 — 시작 Gold·상한 편차는 그대로다
        /// (EnsureAmbitionInitialized는 Gold도 다시 정하므로 쓰지 않는다). 교체한 장비 수를 돌려준다.
        /// </summary>
        private int ReplaceEquipmentWithBasicItems(int entityId, NpcSaveData saveData, ref int skippedItems)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Equipment | NpcSaveGroup.Identity | NpcSaveGroup.Inventory);
            saveData.EnsureSlotsInitialized();
            int replaced = 0;
            int slotMax = (int)GE.EquipSlotType.Max;
            for (int s = 0; s < slotMax; s++)
            {
                ItemData? item = saveData.InventoryEquip[s];
                if (item == null || item.Equipment == null)
                    continue;
                if (item.Equipment.IsIntactBasicItem() == true)
                    continue;

                ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
                BasicItemTable? row = table != null
                    ? NpcEquipHelper.PickBasicVariant(entityId, (GE.EquipSlotType)s, item.Id)
                    : null;
                if (table == null || row == null)
                {
                    skippedItems++;
                    Debug.LogWarning($"[NpcAmbition] D121 replacement skipped npc={entityId} slot={(GE.EquipSlotType)s} " +
                                     $"item={DescribeItem(item)} (no basic variant)");
                    continue;
                }

                item.Equipment = AR.s.Item.CreateBasicEquipmentData(row, table);
                replaced++;
            }

            if (replaced > 0)
            {
                InvalidateNpcPower(entityId);
                if (saveData.InitialPower > 0f)
                {
                    float power = NpcPowerEvaluator.Evaluate(saveData);
                    _npcPowerCache[entityId] = power;
                    saveData.InitialPower = Mathf.Max(power, MinInitialPower);
                }
            }
            return replaced;
        }

        /// <summary>비실체화 직후 호출. 실체화 중에 사서 미뤄 둔 업그레이드 장비를 다시 평가해 장착한다.</summary>
        private void ApplyDeferredPurchaseUpgrades(int entityId, NpcSaveData saveData)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Inventory | NpcSaveGroup.Equipment);
            if (_deferredPurchaseUpgradeItemIds.TryGetValue(entityId, out List<int>? pending) == false)
                return;
            _deferredPurchaseUpgradeItemIds.Remove(entityId);

            float now = AR.s.Time.CurrentGameTime;
            for (int i = 0; i < pending.Count; i++)
            {
                int slotIndex = FindInventorySlot(saveData, pending[i]);
                if (slotIndex < 0)
                    continue;

                ItemData? item = saveData.Inventory[slotIndex];
                if (item == null)
                    continue;

                // 인벤토리에서 먼저 꺼낸 뒤 장착을 시도한다. 실패하면 같은 칸에 되돌린다.
                saveData.Inventory[slotIndex] = null;
                if (TryEquipUpgrade(entityId, saveData, item, now, "Trade") == false)
                    saveData.Inventory[slotIndex] = item;
            }
        }

        /// <summary>
        /// 장비를 문턱·상한 규칙으로 다시 평가해 장착한다. 장착하면 벗은 장비는 인벤토리로 간다.
        /// </summary>
        private bool TryEquipUpgrade(int entityId, NpcSaveData saveData, ItemData item, float gameTime, string source)
        {
            if (item.Equipment == null)
                return false;
            if (EvaluateBestUpgrade(entityId, saveData, item, out GE.EquipSlotType slot, out float currentPower,
                    out float newPower) == false)
                return false;
            return ApplyUpgrade(entityId, saveData, item, slot, currentPower, newPower, gameTime, source);
        }

        /// <summary>
        /// 평가를 통과한 장비를 슬롯에 장착하고 전투력 캐시를 무효화한다. 벗은 장비는 인벤토리로 간다.
        /// 상점 구매 장착(P1b)과 실체화 중 구매의 지연 장착이 쓴다. source는 진단 로그 구분용.
        /// </summary>
        private bool ApplyUpgrade(int entityId, NpcSaveData saveData, ItemData item, GE.EquipSlotType slot,
            float currentPower, float newPower, float gameTime, string source)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Inventory | NpcSaveGroup.Equipment);
            if (saveData.EquipItem(slot, item, out ItemData? replacedItem) == false)
                return false;

            InvalidateNpcPower(entityId);
            float cap = GetNpcPowerCap(saveData);
            Debug.Log($"[NpcAmbition] Upgrade npc={entityId} slot={slot} item={DescribeItem(item)} " +
                      $"replaced={(replacedItem != null ? DescribeItem(replacedItem) : "none")} " +
                      $"power={currentPower:F2}->{newPower:F2} cap={cap:F2} source={source} t={gameTime:F2}");

            if (replacedItem != null)
                StoreInInventory(entityId, saveData, replacedItem);
            // 소문 (P2b, D71·D72): 교체돼 무효가 된 ItemOwner 소문 정리 + 소유자 본인의 새 ItemOwner 소문
            NotifyNpcEquipmentChanged(entityId, saveData, slot, item, gameTime);
            // 자랑 Deed (P2c, D61): 업그레이드 장착만 기록한다 (전리품·구매·실체화 후 지연 장착 모두 이 경로). 일반 전리품은 기록하지 않는다.
            AddNotableItemDeed(entityId, item.Id, item.Table != null ? item.Table.Name : string.Empty, gameTime);
            return true;
        }

        /// <summary>
        /// 호환 슬롯(기존 장비 API의 슬롯 마스크 — WeaponRight/Left 정책 포함)마다 교체 후 전투력을 계산해
        /// 가장 높은 슬롯을 고른다. 교체 후 전투력이 현재의 +15%(개인 상한 80% 초과 시 +30%) 이상이고
        /// 개인 상한 이하일 때만 true (§7.2, §10.1).
        /// </summary>
        private bool EvaluateBestUpgrade(int entityId, NpcSaveData saveData, ItemData loot,
            out GE.EquipSlotType bestSlot, out float currentPower, out float bestPower)
        {
            bestSlot = GE.EquipSlotType.Max;
            currentPower = GetNpcPower(entityId);
            bestPower = currentPower;
            if (loot.Equipment == null)
                return false;

            float cap = GetNpcPowerCap(saveData);
            if (cap <= 0f)
                return false;

            float minGainRatio = currentPower > cap * NearCapPowerRatio
                ? NearCapUpgradeMinGainRatio
                : UpgradeMinGainRatio;
            float requiredPower = currentPower * (1f + minGainRatio);

            int slotMax = (int)GE.EquipSlotType.Max;
            for (int s = 0; s < slotMax; s++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)s;
                GE.EquipmentTypeMask allowedMask = EquipHelper.SlotToEquipmentTypeMask(slot);
                if (Utils.ContainsEquipmentType(allowedMask, loot.Equipment.EquipType) == false)
                    continue;

                for (int i = 0; i < slotMax; i++)
                    _upgradeScratchEquip[i] = saveData.InventoryEquip[i];
                _upgradeScratchEquip[s] = loot;
                float candidatePower = NpcPowerEvaluator.EvaluateBreakdown(_upgradeScratchEquip,
                    saveData.SkillLevel).Total;

                if (candidatePower > bestPower && candidatePower >= requiredPower && candidatePower <= cap)
                {
                    bestPower = candidatePower;
                    bestSlot = slot;
                }
            }

            for (int i = 0; i < slotMax; i++)
                _upgradeScratchEquip[i] = null;
            return bestSlot != GE.EquipSlotType.Max;
        }

        /// <summary>
        /// 개인 인벤토리(5칸)에 넣는다. 줍기(B5)와 같은 Inventory.AddItem 규칙(겹치기 → 빈 칸)을 쓰고,
        /// 가득 차면 새 아이템을 포함해 가치(BasePrice × 수량)가 가장 낮은 것을 버린다 (D35).
        /// 동점이면 새 아이템을 버려 기존 가방을 유지한다.
        /// </summary>
        private void StoreInInventory(int entityId, NpcSaveData saveData, ItemData item)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Inventory);
            if (CreateNpcInventoryView(saveData).AddItem(item) >= 0)
                return;

            int lowestSlot = -1;
            int lowestValue = GetItemValue(item);
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? existing = saveData.Inventory[i];
                if (existing == null)
                    continue;

                int value = GetItemValue(existing);
                if (value < lowestValue)
                {
                    lowestValue = value;
                    lowestSlot = i;
                }
            }

            if (lowestSlot < 0)
            {
                if (_logAmbitionLoot == true)
                    Debug.Log($"[NpcAmbition] Discard npc={entityId} item={DescribeItem(item)} value={lowestValue} (new, bag full)");
                return;
            }

            ItemData? discarded = saveData.Inventory[lowestSlot];
            saveData.Inventory[lowestSlot] = item;
            if (_logAmbitionLoot == true && discarded != null)
                Debug.Log($"[NpcAmbition] Discard npc={entityId} item={DescribeItem(discarded)} value={lowestValue} " +
                          $"(replaced by {DescribeItem(item)})");
        }

        /// <summary>아이템 가치 = BasePrice × 수량 (가방 버림 D35·소문 가치·절도 가중치 D99가 같은 값을 쓴다).</summary>
        public static int GetItemValue(ItemData item)
        {
            ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
            int basePrice = table != null ? table.BasePrice : 0;
            return basePrice * Mathf.Max(1, item.Quantity);
        }

        private static int FindInventorySlot(NpcSaveData saveData, int itemInstanceId)
        {
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (item != null && item.ItemInstanceId == itemInstanceId)
                    return i;
            }
            return -1;
        }

        private static string DescribeItem(ItemData item)
        {
            string name = item.Table != null ? item.Table.Name : $"Item {item.Id}";
            return $"{item.Id}:{name}#{item.ItemInstanceId}";
        }

        private void ResetAmbitionRuntime()
        {
            _npcPowerCache.Clear();
            _deferredPurchaseUpgradeItemIds.Clear();
            for (int i = 0; i < _upgradeScratchEquip.Length; i++)
                _upgradeScratchEquip[i] = null;
        }
    }
}
