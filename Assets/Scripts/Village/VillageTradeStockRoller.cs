#nullable enable
using System.Collections.Generic;
using ARPG.History;

namespace ARPG.Village
{
    /// <summary>재고 롤 계획 1건. ItemData 인스턴스 완성 전의 결정론 선택 결과다.</summary>
    public struct TradeStockPlanEntry
    {
        public int ItemId;
        public int Count;
        /// <summary>확정 플레이어 구매가 스냅샷 (BasePrice × SellToPlayerBp / 10000, 최소 1).</summary>
        public int UnitBuyPrice;
        public TradeStockSlotKind SlotKind;
        public TradePriceReason Reason;
    }

    /// <summary>
    /// V5 (VILLAGE_REWORK_PLAN §8.4/§8.5) 결정론 상점 재고 롤러.
    /// UnityEngine.Random을 사용하지 않는 순수 로직 — 같은 (mapSeed, settlementKey,
    /// restockCycle, profile, shopTier, facilityTier, 아이템 카탈로그)는 항상 같은 계획을 만든다.
    /// 상태는 소유하지 않으며 ItemData 인스턴스 완성·저장은 VillageManager가 담당한다.
    /// </summary>
    public static class VillageTradeStockRoller
    {
        /// <summary>재입고 회차 — 게임시간을 프로필 재입고 간격으로 나눈 몫.</summary>
        public static int GetRestockCycle(float gameTimeHours, VillageTradeProfile profile)
        {
            if (gameTimeHours <= 0f)
                return 0;
            return (int)(gameTimeHours / profile.RestockIntervalHours);
        }

        /// <summary>
        /// §8.5 — SettlementId + RestockCycle + DerivedSeed 채널로 재입고 seed를 만든다.
        /// settlementKey는 History 정착지의 SettlementId, 수제작 BaseTown은 (-1 - VillageId).
        /// </summary>
        public static ulong CreateStockSeed(int mapSeed, int settlementKey, int restockCycle)
        {
            ulong channel = DerivedSeed.ChannelMerchantStock
                ^ unchecked((ulong)(uint)settlementKey * 0x9E3779B97F4A7C15UL)
                ^ unchecked((ulong)(uint)restockCycle * 0xBF58476D1CE4E5B9UL);
            return DerivedSeed.Derive(mapSeed, channel);
        }

        /// <summary>플레이어 구매가 (§8.3). 판매 가능한 품목은 최소 1골드.</summary>
        public static int GetPlayerBuyPrice(VillageTradeProfile profile, TradeItemInfo item)
        {
            int price = item.BasePrice * profile.GetSellToPlayerBp(item.Tags)
                / VillageTradeProfile.BP_DENOMINATOR;
            return price < 1 ? 1 : price;
        }

        /// <summary>상인 매입가 (§8.3). 0 이상 — 헐값 품목은 0골드일 수 있다(기존과 동일).</summary>
        public static int GetMerchantBuyPrice(VillageTradeProfile profile, TradeItemInfo item)
        {
            int price = item.BasePrice * profile.GetBuyFromPlayerBp(item.Tags)
                / VillageTradeProfile.BP_DENOMINATOR;
            return price < 0 ? 0 : price;
        }

        /// <summary>UI 표시용 가격 사유 (§8.5).</summary>
        public static TradePriceReason GetPriceReason(VillageTradeProfile profile,
            TradeItemInfo item, TradeStockSlotKind slotKind)
        {
            if (slotKind == TradeStockSlotKind.RareImport)
                return TradePriceReason.RareImport;

            int sellBp = profile.GetSellToPlayerBp(item.Tags);
            if (sellBp < profile.DefaultSellToPlayerBp)
                return TradePriceReason.LocalSpecialty;
            if (sellBp > profile.DefaultSellToPlayerBp)
                return TradePriceReason.LocalShortage;
            return slotKind == TradeStockSlotKind.FacilityPool
                ? TradePriceReason.LocalSpecialty
                : TradePriceReason.Standard;
        }

        /// <summary>
        /// 슬롯별 재고 계획 생성 (§8.4 — 보장/특산/희귀+특수 시설 풀).
        /// 같은 아이템은 회차 안에서 중복 진열하지 않는다. 풀이 비면 해당 슬롯은 건너뛴다.
        /// </summary>
        public static List<TradeStockPlanEntry> RollStockPlan(VillageTradeProfile profile,
            int shopTier, int facilityTier, IReadOnlyList<TradeItemInfo> items,
            HistoryRng rng)
        {
            List<TradeStockPlanEntry> plan = new List<TradeStockPlanEntry>();
            if (items == null || items.Count == 0)
                return plan;

            TradeSlotPlan slots = profile.GetSlotPlan(shopTier);
            int maxItemTier = profile.GetMaxItemTier(shopTier);
            HashSet<int> pickedItemIds = new HashSet<int>();

            // 1. 보장 품목 — 우선순위 목록에서 Tier 게이트 통과분을 앞에서부터 채운다 (RNG 미사용).
            int guaranteedCount = 0;
            for (int i = 0; i < profile.GuaranteedItemIds.Count; i++)
            {
                if (guaranteedCount >= slots.Guaranteed)
                    break;
                int itemId = profile.GuaranteedItemIds[i];
                if (TryFindItem(items, itemId, out TradeItemInfo item) == false)
                    continue;
                if (item.BasePrice <= 0 || item.Tier > maxItemTier)
                    continue;
                if (pickedItemIds.Add(itemId) == false)
                    continue;
                plan.Add(CreateEntry(profile, item, TradeStockSlotKind.Guaranteed, rng));
                guaranteedCount++;
            }

            // 2. 지역 특산 — 가중 풀에서 중복 없이 뽑는다. 풀이 비면(저Tier에서 특산이
            //    Tier 게이트에 걸리는 경우 등) 일반 품목 fallback으로 슬롯 수를 유지한다 (§8.4).
            List<TradeItemInfo> candidates = new List<TradeItemInfo>();
            List<int> weights = new List<int>();
            for (int slot = 0; slot < slots.Specialty; slot++)
            {
                BuildWeightedCandidates(profile.SpecialtyPools, items, maxItemTier,
                    pickedItemIds, candidates, weights);
                if (candidates.Count == 0)
                {
                    BuildGenericFallbackCandidates(items, maxItemTier, pickedItemIds,
                        candidates, weights);
                }
                if (TryPickWeighted(candidates, weights, rng, out TradeItemInfo picked) == false)
                    break;
                pickedItemIds.Add(picked.Id);
                plan.Add(CreateEntry(profile, picked, TradeStockSlotKind.Specialty, rng));
            }

            // 3. 희귀/수입 + 특수 시설 추가 풀 — 시설 Tier가 열려 있으면 두 풀을 병합해 뽑는다.
            bool facilityOpen = facilityTier >= profile.FacilityPoolMinTier
                && profile.FacilityPools.Count > 0;
            int facilityMaxTier = facilityOpen
                ? ClampItemTier(facilityTier + 1)
                : 0;
            for (int slot = 0; slot < slots.RareImport; slot++)
            {
                BuildWeightedCandidates(profile.RareImportPools, items, maxItemTier,
                    pickedItemIds, candidates, weights);
                int rareCandidateCount = candidates.Count;
                bool usedGenericFallback = false;
                if (facilityOpen == true)
                {
                    AppendWeightedCandidates(profile.FacilityPools, items, facilityMaxTier,
                        pickedItemIds, candidates, weights);
                }
                if (candidates.Count == 0)
                {
                    usedGenericFallback = true;
                    BuildGenericFallbackCandidates(items, maxItemTier, pickedItemIds,
                        candidates, weights);
                }
                if (TryPickWeighted(candidates, weights, rng, out TradeItemInfo picked) == false)
                    break;
                pickedItemIds.Add(picked.Id);
                bool fromFacilityPool = usedGenericFallback == false
                    && facilityOpen == true
                    && IndexOfItem(candidates, picked.Id) >= rareCandidateCount;
                TradeStockSlotKind kind = fromFacilityPool
                    ? TradeStockSlotKind.FacilityPool
                    : TradeStockSlotKind.RareImport;
                plan.Add(CreateEntry(profile, picked, kind, rng));
            }

            return plan;
        }

        /// <summary>
        /// 기본 장비 칸 계획 (NPC_SHOP_EQUIPMENT_DESIGN §4, D117). basicEquipmentItems는 기본 아이템 변형이 있는 장비만
        /// ItemId 오름차순으로 넘긴다. 상점 Tier 게이트 안에서 같은 확률로, 칸끼리 중복 없이 뽑는다.
        /// 기존 칸(RollStockPlan)과 품목이 겹쳐도 된다. 호출자는 RollStockPlan 뒤에 같은 rng로 이어 부른다.
        /// </summary>
        public static List<TradeStockPlanEntry> RollBasicEquipmentPlan(VillageTradeProfile profile,
            int shopTier, IReadOnlyList<TradeItemInfo> basicEquipmentItems, HistoryRng rng)
        {
            List<TradeStockPlanEntry> plan = new List<TradeStockPlanEntry>();
            if (basicEquipmentItems == null || basicEquipmentItems.Count == 0)
                return plan;

            int slotCount = profile.GetBasicEquipmentSlotCount(shopTier);
            int maxItemTier = profile.GetMaxItemTier(shopTier);
            List<TradeItemInfo> candidates = new List<TradeItemInfo>();
            List<int> weights = new List<int>();
            for (int i = 0; i < basicEquipmentItems.Count; i++)
            {
                TradeItemInfo item = basicEquipmentItems[i];
                if (item.BasePrice <= 0 || item.Tier > maxItemTier)
                    continue;
                candidates.Add(item);
                weights.Add(1);
            }

            for (int slot = 0; slot < slotCount; slot++)
            {
                if (TryPickWeighted(candidates, weights, rng, out TradeItemInfo picked) == false)
                    break;
                int index = IndexOfItem(candidates, picked.Id);
                candidates.RemoveAt(index);
                weights.RemoveAt(index);
                plan.Add(CreateEntry(profile, picked, TradeStockSlotKind.BasicEquipment, rng));
            }
            return plan;
        }

        private static TradeStockPlanEntry CreateEntry(VillageTradeProfile profile,
            TradeItemInfo item, TradeStockSlotKind slotKind, HistoryRng rng)
        {
            int count = 1;
            if (item.Stackable == true)
            {
                bool isTradeGood = (item.Tags & TradeTag.TradeGood) != TradeTag.None;
                count = isTradeGood
                    ? rng.Range(profile.TradeGoodCountMin, profile.TradeGoodCountMax + 1)
                    : rng.Range(profile.StackableCountMin, profile.StackableCountMax + 1);
            }

            return new TradeStockPlanEntry
            {
                ItemId = item.Id,
                Count = count,
                UnitBuyPrice = GetPlayerBuyPrice(profile, item),
                SlotKind = slotKind,
                Reason = GetPriceReason(profile, item, slotKind),
            };
        }

        /// <summary>
        /// 풀 규칙에 맞는 후보와 가중치를 구성한다. Tags == None 풀은 "태그 없는 일반 품목"
        /// (RareImport/TradeGood 태그가 없는 미태그 아이템)을 뜻한다. 후보는 ItemId 순으로
        /// 정렬된 입력을 그대로 순회해 결정론을 유지한다.
        /// </summary>
        private static void BuildWeightedCandidates(
            IReadOnlyList<TradeWeightedTagPool> pools, IReadOnlyList<TradeItemInfo> items,
            int maxItemTier, HashSet<int> pickedItemIds,
            List<TradeItemInfo> candidates, List<int> weights)
        {
            candidates.Clear();
            weights.Clear();
            AppendWeightedCandidates(pools, items, maxItemTier, pickedItemIds,
                candidates, weights);
        }

        private static void AppendWeightedCandidates(
            IReadOnlyList<TradeWeightedTagPool> pools, IReadOnlyList<TradeItemInfo> items,
            int maxItemTier, HashSet<int> pickedItemIds,
            List<TradeItemInfo> candidates, List<int> weights)
        {
            for (int i = 0; i < items.Count; i++)
            {
                TradeItemInfo item = items[i];
                if (item.BasePrice <= 0 || item.Tier > maxItemTier)
                    continue;
                if (pickedItemIds.Contains(item.Id) == true)
                    continue;
                if (IndexOfItem(candidates, item.Id) >= 0)
                    continue;

                int weight = 0;
                for (int p = 0; p < pools.Count; p++)
                {
                    TradeWeightedTagPool pool = pools[p];
                    bool matches;
                    if (pool.Tags == TradeTag.None)
                    {
                        matches = item.Tags == TradeTag.None;
                    }
                    else
                    {
                        matches = (item.Tags & pool.Tags) != TradeTag.None;
                    }
                    if (matches == true && pool.Weight > weight)
                        weight = pool.Weight;
                }

                if (weight <= 0)
                    continue;
                candidates.Add(item);
                weights.Add(weight);
            }
        }

        /// <summary>
        /// 일반 품목 fallback 풀 — 태그 없는 미태그 아이템 균등 가중치.
        /// RareImport/TradeGood 등 태그가 붙은 품목은 지역 풀 밖에서 진열되지 않는다.
        /// </summary>
        private static void BuildGenericFallbackCandidates(
            IReadOnlyList<TradeItemInfo> items, int maxItemTier,
            HashSet<int> pickedItemIds, List<TradeItemInfo> candidates, List<int> weights)
        {
            candidates.Clear();
            weights.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                TradeItemInfo item = items[i];
                if (item.BasePrice <= 0 || item.Tier > maxItemTier)
                    continue;
                if (item.Tags != TradeTag.None)
                    continue;
                if (pickedItemIds.Contains(item.Id) == true)
                    continue;
                candidates.Add(item);
                weights.Add(1);
            }
        }

        private static bool TryPickWeighted(List<TradeItemInfo> candidates,
            List<int> weights, HistoryRng rng, out TradeItemInfo picked)
        {
            picked = default;
            int totalWeight = 0;
            for (int i = 0; i < weights.Count; i++)
                totalWeight += weights[i];
            if (totalWeight <= 0)
                return false;

            int roll = rng.Range(0, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                {
                    picked = candidates[i];
                    return true;
                }
            }
            picked = candidates[candidates.Count - 1];
            return true;
        }

        private static bool TryFindItem(IReadOnlyList<TradeItemInfo> items, int itemId,
            out TradeItemInfo item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id == itemId)
                {
                    item = items[i];
                    return true;
                }
            }
            item = default;
            return false;
        }

        private static int IndexOfItem(List<TradeItemInfo> candidates, int itemId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Id == itemId)
                    return i;
            }
            return -1;
        }

        private static int ClampItemTier(int tier)
        {
            if (tier < 1) return 1;
            if (tier > 3) return 3;
            return tier;
        }
    }
}
