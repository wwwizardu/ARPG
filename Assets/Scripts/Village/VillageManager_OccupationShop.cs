#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 상점을 거친 NPC 간 연결 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §7). 상점 쪽 원장만 다룬다:
    /// - 광석 재고와 재고 기반 가격 (§7.1): 판 광석은 그 마을 상점 재고로 쌓이고, 값은 재고 수에서 계산한다. 플레이어도 같은 재고·같은 값에 판다.
    /// - 위탁 칸 (§7.2): 제작자가 맡긴 기본 아이템 장비. 팔리면 수수료를 뺀 대금이 제작자 몫으로 남고 다음 거래 때 정산한다.
    /// - 재입고 회차 처리: 광석 재고 10% 감소, 7일 지난 위탁품 상점 매입, 죽은 제작자 몫 금고 귀속.
    /// 대금은 NPC Gold ↔ 마을 Gold ↔ 미정산 위탁 대금 사이에서만 움직인다 (닫힌 경제, 세계 Gold = 금고 + NPC + 미정산 대금).
    /// </summary>
    public partial class VillageManager
    {
        // 진단 카운터 (휘발성 — 저장하지 않는다). Ambition Sim이 실행 동안 새 인스턴스로 바꿔 끼운다.
        private VillageOccupationShopCounters _occupationShopCounters = new();

        public VillageOccupationShopCounters OccupationShopCounters => _occupationShopCounters;

        // ================= 광석 재고 (§7.1) =================

        public static int GetOreStock(VillageData v, int oreItemId)
            => v.OreStock != null && v.OreStock.TryGetValue(oreItemId, out int count) ? count : 0;

        /// <summary>상한까지 남은 자리. 0이면 상점이 그 광석을 사지 않는다.</summary>
        public static int GetOreStockRoom(VillageData v, int oreItemId)
            => Mathf.Max(0, NpcOccupationRules.OreStockCap - GetOreStock(v, oreItemId));

        /// <summary>상점이 광석 1개를 사 주는 값. 재고가 상한이면 -1 (사지 않음).</summary>
        public int GetOreShopBuyPrice(VillageData v, Tables.ItemTable table)
        {
            int stock = GetOreStock(v, table.Id);
            if (stock >= NpcOccupationRules.OreStockCap)
                return -1;
            float price = GetOreRegionBase(v, table) * NpcOccupationRules.GetOreStockMultiplier(stock, true);
            return ApplyItemPriceIndex(Mathf.Max(1, Mathf.FloorToInt(price)), table, 1);
        }

        /// <summary>상점이 광석 1개를 파는 값. 재고가 없으면 -1.</summary>
        public int GetOreShopSellPrice(VillageData v, int oreItemId)
        {
            int stock = GetOreStock(v, oreItemId);
            Tables.ItemTable? table = AR.s.Data.GetItem(oreItemId);
            if (stock <= 0 || table == null)
                return -1;
            float price = GetOreRegionBase(v, table) * NpcOccupationRules.GetOreStockMultiplier(stock, false);
            return ApplyItemPriceIndex(Mathf.Max(1, Mathf.RoundToInt(price)), table, 1);
        }

        /// <summary>
        /// 광석 매입가 / 기본 가격 (채광 선택 점수의 배율, §3·§5.5). 상한이면 0. 지역 기준값 차이는 그대로 반영한다.
        /// </summary>
        public float GetOreBuyPriceFactor(VillageData v, int oreItemId)
        {
            Tables.ItemTable? table = AR.s.Data.GetItem(oreItemId);
            if (table == null || table.BasePrice <= 0)
                return 0f;
            int price = GetOreShopBuyPrice(v, table);
            return price <= 0 ? 0f : price / (float)table.BasePrice;
        }

        /// <summary>
        /// 지역 기준값 (§7.1): 기본 가격, 광석이 특산(매입 비율이 기본보다 낮은)인 상권은 × 0.8. 지역 교역 규칙은 기준값만 정한다.
        /// </summary>
        private float GetOreRegionBase(VillageData v, Tables.ItemTable table)
        {
            VillageTradeProfile trade = ResolveTradeProfile(v);
            int buyBp = trade.GetBuyFromPlayerBp(ItemTradeTagCatalog.GetTags(table.Id));
            float mul = buyBp < trade.DefaultBuyFromPlayerBp ? NpcOccupationRules.OreSpecialtyBaseMul : 1f;
            return table.BasePrice * mul;
        }

        private void AddOreStock(VillageData v, int oreItemId, int count)
        {
            if (count <= 0)
                return;
            MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop);
            int stock = Mathf.Min(NpcOccupationRules.OreStockCap, GetOreStock(v, oreItemId) + count);
            v.OreStock[oreItemId] = stock;
        }

        private void RemoveOreStock(VillageData v, int oreItemId, int count)
        {
            MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop);
            int stock = GetOreStock(v, oreItemId) - count;
            if (stock > 0)
                v.OreStock[oreItemId] = stock;
            else
                v.OreStock.Remove(oreItemId);
        }

        /// <summary>
        /// NPC 광석 판매 확정 (§7.1 ①②). 금고에서 대금을 꺼내고 그 광석을 상점 재고에 쌓는다. 금고가 모자라면 false (NPC가 계속 보유).
        /// 호출자가 자리(GetOreStockRoom)와 값을 먼저 확인한다.
        /// </summary>
        public bool TryCommitNpcOreSale(int villageId, int oreItemId, int count, int unitPrice)
        {
            if (count <= 0 || unitPrice < 0 || _villages.TryGetValue(villageId, out VillageData? v) == false)
                return false;
            if (GetOreStockRoom(v, oreItemId) < count)
                return false;
            if (TryWithdrawNpcGold(villageId, unitPrice * count) == false)
                return false;
            AddOreStock(v, oreItemId, count);
            return true;
        }

        /// <summary>
        /// NPC 광석 구매 확정 (§7.1 ③). 재고에서 빼고 대금을 금고에 넣는다. 호출자가 NPC Gold를 확인하고 뺀다.
        /// 개수는 재고 안에서만, 값은 사기 시작할 때의 재고로 정한다.
        /// </summary>
        public bool TryCommitNpcOrePurchase(VillageData v, int oreItemId, int count, int unitPrice)
        {
            if (count <= 0 || unitPrice <= 0 || GetOreStock(v, oreItemId) < count)
                return false;
            if (DepositNpcGold(v.VillageId, unitPrice * count) == false)
                return false;
            RemoveOreStock(v, oreItemId, count);
            return true;
        }

        // ================= 위탁 칸 (§7.2) =================

        public static int CountConsignments(VillageData v, int makerNpcId)
        {
            int count = 0;
            for (int i = 0; i < v.Consignments.Count; i++)
            {
                if (v.Consignments[i].MakerNpcId == makerNpcId)
                    count++;
            }
            return count;
        }

        /// <summary>위탁할 자리가 있는가 (마을당 8칸, 제작자당 2개).</summary>
        public static bool CanConsign(VillageData v, int makerNpcId)
            => v.Consignments.Count < NpcOccupationRules.ConsignmentSlotsPerVillage &&
               CountConsignments(v, makerNpcId) < NpcOccupationRules.ConsignmentsPerMaker;

        /// <summary>이 장비가 상점에 있는가 — 기본 장비 칸(남은 수량 있음) 또는 위탁 칸 (빈 칸 장비 제작 판단, §6.2).</summary>
        public bool HasShopEquipment(VillageData v, int itemId)
        {
            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = v.MerchantStock[i];
                if (entry.Item != null && entry.Item.Id == itemId && IsNpcPurchasableEntry(v, entry) == true)
                    return true;
            }
            for (int i = 0; i < v.Consignments.Count; i++)
            {
                if (v.Consignments[i].Item != null && v.Consignments[i].Item.Id == itemId)
                    return true;
            }
            return false;
        }

        public void AddConsignment(VillageData v, ItemData item, int makerNpcId, string makerName, int madeForNpcId)
        {
            MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop);
            v.Consignments.Add(new VillageConsignment
            {
                Item = item,
                MakerNpcId = makerNpcId,
                MakerName = makerName ?? string.Empty,
                ConsignedGameTime = AR.s.Time.CurrentGameTime,
                MadeForNpcId = madeForNpcId,
            });
        }

        /// <summary>
        /// 위탁품의 지금 값 (§7.2): 같은 장비의 상점 가격(지역 교역 규칙·물가 지수 반영)의 80%에서 하루 5%씩 4일 내리고 하한 60%.
        /// 플레이어와 NPC가 같은 값에 산다.
        /// </summary>
        public int GetConsignmentPrice(VillageData v, VillageConsignment consignment)
        {
            Tables.ItemTable? table = consignment.Item?.Table ?? (consignment.Item != null ? AR.s.Data.GetItem(consignment.Item.Id) : null);
            if (table == null || table.BasePrice <= 0)
                return -1;
            VillageTradeProfile trade = ResolveTradeProfile(v);
            int shopPrice = ApplyItemPriceIndex(VillageTradeStockRoller.GetPlayerBuyPrice(trade, CreateTradeItemInfo(table)), table, 1);
            int days = NpcOccupationRules.GetDaysListed(consignment.ConsignedGameTime, AR.s.Time.CurrentGameTime);
            int percent = NpcOccupationRules.GetConsignmentPricePercent(days);
            return Mathf.Max(1, Mathf.RoundToInt(shopPrice * percent / 100f));
        }

        /// <summary>
        /// NPC가 위탁품을 산다 (§7.2). 호출자가 NPC Gold·자격을 확인했다고 가정한다. 대금의 10%는 마을 금고(수수료),
        /// 나머지는 제작자 몫으로 남는다 — 제작자가 죽었으면 전부 금고로 간다. 위탁 칸에서 빼고 물건을 돌려준다.
        /// </summary>
        public ItemData? CommitConsignmentPurchase(VillageData v, int index, int buyerEntityId, string buyerName, int price)
        {
            if (index < 0 || index >= v.Consignments.Count || price <= 0)
                return null;
            VillageConsignment consignment = v.Consignments[index];
            int fee = Mathf.Clamp(Mathf.RoundToInt(price * NpcOccupationRules.ConsignmentFeePercent / 100f), 0, price);
            bool makerAlive = IsLivingMaker(consignment.MakerNpcId);
            int vaultShare = makerAlive == true ? fee : price;
            if (DepositNpcGold(v.VillageId, vaultShare) == false)
                return null;

            MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop | VillageSaveGroup.Resources);
            v.Consignments.RemoveAt(index);
            if (makerAlive == true)
            {
                v.ConsignmentProceeds.Add(new VillageConsignmentProceeds
                {
                    MakerNpcId = consignment.MakerNpcId,
                    Gold = price - fee,
                    ItemId = consignment.Item.Id,
                    ItemName = consignment.Item.Table != null ? consignment.Item.Table.Name : string.Empty,
                    SoldGameTime = AR.s.Time.CurrentGameTime,
                    BuyerNpcId = buyerEntityId,
                    BuyerName = buyerName ?? string.Empty,
                    MadeForBuyer = consignment.MadeForNpcId >= 0 && consignment.MadeForNpcId == buyerEntityId,
                });
            }
            return consignment.Item;
        }

        /// <summary>이 제작자의 미정산 대금을 꺼낸다 (다음 상점 거래 때 정산, §7.2). 꺼낸 건은 into에 담는다.</summary>
        public int TakeConsignmentProceeds(VillageData v, int makerNpcId, List<VillageConsignmentProceeds> into)
        {
            int total = 0;
            for (int i = v.ConsignmentProceeds.Count - 1; i >= 0; i--)
            {
                VillageConsignmentProceeds proceeds = v.ConsignmentProceeds[i];
                if (proceeds.MakerNpcId != makerNpcId)
                    continue;
                MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop);
                total += proceeds.Gold;
                into.Add(proceeds);
                v.ConsignmentProceeds.RemoveAt(i);
            }
            into.Reverse();
            return total;
        }

        public static bool HasConsignmentProceeds(VillageData v, int makerNpcId)
        {
            for (int i = 0; i < v.ConsignmentProceeds.Count; i++)
            {
                if (v.ConsignmentProceeds[i].MakerNpcId == makerNpcId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 떠나는 제작자 정산 (§7.2 제작자 이주): 상점이 그 위탁품을 기본 가격의 20%에 사들이고, 미정산 대금과 함께 돌려준다.
        /// 금고가 모자라 못 산 위탁품은 그대로 두고 제작자가 죽은 것처럼 다룬다 — 마을 사이에 대금을 보내는 기능은 두지 않는다.
        /// 돌려준 Gold 합계를 돌려준다 (호출자가 NPC Gold에 더한다).
        /// </summary>
        public int SettleDepartingMaker(int villageId, int makerNpcId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false)
                return 0;
            int total = 0;
            for (int i = v.Consignments.Count - 1; i >= 0; i--)
            {
                VillageConsignment consignment = v.Consignments[i];
                if (consignment.MakerNpcId != makerNpcId)
                    continue;
                int buyout = GetConsignmentBuyoutPrice(consignment);
                if (buyout <= 0 || TryWithdrawNpcGold(villageId, buyout) == false)
                {
                    // 못 산 위탁품은 계속 진열하되, 팔리면 대금은 금고로 간다 (제작자 없음 처리)
                    MarkSaveDirty(villageId, VillageSaveGroup.Shop);
                    consignment.MakerNpcId = 0;
                    continue;
                }
                MarkSaveDirty(villageId, VillageSaveGroup.Shop);
                v.Consignments.RemoveAt(i);
                total += buyout;
                _occupationShopCounters.ConsignmentBuyouts++;
            }
            List<VillageConsignmentProceeds> taken = new List<VillageConsignmentProceeds>();
            total += TakeConsignmentProceeds(v, makerNpcId, taken);
            return total;
        }

        /// <summary>세계 Gold에 들어가는 미정산 위탁 대금 합계 (닫힌 경제 측정, GOLD_BALANCE D126).</summary>
        public long SumPendingConsignmentProceeds()
        {
            long total = 0;
            for (int i = 0; i < _villageList.Count; i++)
            {
                List<VillageConsignmentProceeds> list = _villageList[i].ConsignmentProceeds;
                for (int p = 0; p < list.Count; p++)
                    total += list[p].Gold;
            }
            return total;
        }

        /// <summary>로드 보정: null 목록을 비우고, 위탁품 ItemData의 테이블 참조(장비 종류 등 계산값)를 복원한다. 아이템이 없는 칸은 지운다.</summary>
        public static void RestoreOccupationShopOnLoad(VillageData data)
        {
            data.OreStock ??= new Dictionary<int, int>();
            data.Consignments ??= new List<VillageConsignment>();
            data.ConsignmentProceeds ??= new List<VillageConsignmentProceeds>();
            for (int i = data.Consignments.Count - 1; i >= 0; i--)
            {
                VillageConsignment? consignment = data.Consignments[i];
                if (consignment == null || consignment.Item == null)
                {
                    data.Consignments.RemoveAt(i);
                    continue;
                }
                consignment.MakerName ??= string.Empty;
                consignment.Item.OnLoadCompleted();
            }
            for (int i = data.ConsignmentProceeds.Count - 1; i >= 0; i--)
            {
                if (data.ConsignmentProceeds[i] == null)
                    data.ConsignmentProceeds.RemoveAt(i);
            }
        }

        // ================= 재입고 회차 처리 =================

        /// <summary>
        /// 재입고 회차가 바뀌었으면 한 번 처리한다 (§7.1·§7.2): 광석 재고 10% 감소(지난 회차 수만큼), 7일 지난 NPC 위탁품 상점 매입
        /// (금고가 모자라면 하한가로 남겨 다음 회차에 다시), 죽은 제작자의 미정산 대금 금고 귀속. 상점을 여는 모든 경로(NPC 거래·플레이어 상점)가 부른다.
        /// </summary>
        private void ProcessShopEconomyCycle(VillageData v, int cycle)
        {
            if (v.ShopEconomyCycle == cycle)
                return;
            MarkSaveDirty(v.VillageId, VillageSaveGroup.Shop);
            int previous = v.ShopEconomyCycle;
            v.ShopEconomyCycle = cycle;
            if (previous < 0 || cycle < previous)
                return;

            int elapsed = Mathf.Min(cycle - previous, 30);
            if (v.OreStock.Count > 0)
            {
                List<int> ores = new List<int>(v.OreStock.Keys);
                for (int i = 0; i < ores.Count; i++)
                {
                    int stock = v.OreStock[ores[i]];
                    for (int c = 0; c < elapsed && stock > 0; c++)
                        stock -= NpcOccupationRules.GetOreStockDecay(stock);
                    _occupationShopCounters.OreDecayed += v.OreStock[ores[i]] - stock;
                    if (stock > 0)
                        v.OreStock[ores[i]] = stock;
                    else
                        v.OreStock.Remove(ores[i]);
                }
            }

            float now = AR.s.Time.CurrentGameTime;
            for (int i = v.Consignments.Count - 1; i >= 0; i--)
            {
                VillageConsignment consignment = v.Consignments[i];
                if (NpcOccupationRules.GetDaysListed(consignment.ConsignedGameTime, now) < NpcOccupationRules.ConsignmentDays)
                    continue;
                bool makerAlive = IsLivingMaker(consignment.MakerNpcId);
                int buyout = GetConsignmentBuyoutPrice(consignment);
                if (makerAlive == true)
                {
                    if (buyout <= 0 || TryWithdrawNpcGold(v.VillageId, buyout) == false)
                    {
                        _occupationShopCounters.ConsignmentBuyoutsDeferred++;
                        continue;
                    }
                    v.ConsignmentProceeds.Add(new VillageConsignmentProceeds
                    {
                        MakerNpcId = consignment.MakerNpcId,
                        Gold = buyout,
                        ItemId = consignment.Item.Id,
                        ItemName = consignment.Item.Table != null ? consignment.Item.Table.Name : string.Empty,
                        SoldGameTime = now,
                    });
                }
                // 제작자가 없으면 상점이 대금 없이 거둔다 (받을 사람이 없는 대금은 금고에 남는다)
                v.Consignments.RemoveAt(i);
                _occupationShopCounters.ConsignmentBuyouts++;
            }

            for (int i = v.ConsignmentProceeds.Count - 1; i >= 0; i--)
            {
                VillageConsignmentProceeds proceeds = v.ConsignmentProceeds[i];
                if (IsLivingMaker(proceeds.MakerNpcId) == true)
                    continue;
                if (DepositNpcGold(v.VillageId, proceeds.Gold) == false)
                    continue;
                v.ConsignmentProceeds.RemoveAt(i);
            }
        }

        private static int GetConsignmentBuyoutPrice(VillageConsignment consignment)
        {
            Tables.ItemTable? table = consignment.Item?.Table ?? (consignment.Item != null ? AR.s.Data.GetItem(consignment.Item.Id) : null);
            if (table == null || table.BasePrice <= 0)
                return 0;
            return Mathf.Max(1, Mathf.RoundToInt(table.BasePrice * NpcOccupationRules.ConsignmentBuyoutPercent / 100f));
        }

        private static bool IsLivingMaker(int makerNpcId)
        {
            if (makerNpcId <= 0 || AR.s.Npc == null)
                return false;
            NpcSaveData? maker = AR.s.Npc.GetSaveData(makerNpcId);
            return maker != null && maker.Condition != CharacterConditions.Dead && maker.Activity.Phase != NpcActivityPhase.Dead;
        }
    }

    /// <summary>상점 위탁·광석 재고 진단 카운터 (휘발성). Ambition Sim이 새 인스턴스로 바꿔 끼운다.</summary>
    public sealed class VillageOccupationShopCounters
    {
        public long OreDecayed;
        public long ConsignmentBuyouts;
        public long ConsignmentBuyoutsDeferred;
        public long PlayerOreSold;
    }
}
