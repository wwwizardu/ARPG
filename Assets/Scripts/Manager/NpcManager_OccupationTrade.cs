#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 상점을 거친 NPC 간 연결의 NPC 쪽 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §7, 1단계). 기존 상점 거래(TradeAtShop) 안에서 차례로 부른다:
    /// 위탁 대금 정산 → (판매: 광석은 재고 기반 가격, 자기 제작용 1회분은 남김) → 제작품 위탁 → 제작용 광석 구매 → 빈 칸 보충 → 업그레이드.
    /// 모든 대금은 NPC Gold ↔ 마을 Gold ↔ 미정산 위탁 대금 사이에서만 움직인다.
    /// </summary>
    public partial class NpcManager
    {
        // 정산 때 꺼낸 위탁 대금을 담는 스크래치 버퍼 — 사용 전에 매번 비운다.
        private readonly List<VillageConsignmentProceeds> _proceedsScratch = new();

        /// <summary>
        /// 위탁 대금 정산 (§7.2): 팔린 내 위탁품의 대금을 받는다. 이때 누가 샀는지 알게 되어 생활 칸에 제작 자랑이 남는다 (§7.3·§9.2 ②).
        /// 상점이 끝까지 안 팔린 물건을 사들인 대금은 자랑이 되지 않는다.
        /// </summary>
        private void SettleConsignmentProceeds(int entityId, NpcSaveData saveData, VillageData village)
        {
            if (VillageManager.HasConsignmentProceeds(village, entityId) == false)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Wallet);
            _proceedsScratch.Clear();
            int total = AR.s.Village.TakeConsignmentProceeds(village, entityId, _proceedsScratch);
            saveData.Gold += total;
            _occupationCounters.ProceedsSettled += _proceedsScratch.Count;
            _occupationCounters.ProceedsSettledGold += total;
            for (int i = 0; i < _proceedsScratch.Count; i++)
            {
                VillageConsignmentProceeds proceeds = _proceedsScratch[i];
                if (proceeds.BuyerNpcId < 0)
                    continue;
                RecordLifeStory(entityId, saveData, new NpcLifeStory
                {
                    Kind = NpcLifeStoryKind.CraftSold,
                    When = proceeds.SoldGameTime,
                    ItemId = proceeds.ItemId,
                    ItemName = proceeds.ItemName,
                    OtherNpcId = proceeds.BuyerNpcId,
                    OtherName = proceeds.BuyerName,
                    MadeForBuyer = proceeds.MadeForBuyer,
                    LostItemName = proceeds.MadeForBuyer == true ? GetLostItemName(proceeds.BuyerNpcId, proceeds.ItemName) : string.Empty,
                });
                _occupationCounters.CraftSoldStories++;
            }
            _proceedsScratch.Clear();
            Debug.Log($"[NpcOccupation] Settle npc={entityId} v={village.VillageId} gold=+{total}");
        }

        // 산 NPC가 잃었던 장비 이름 (제작 자랑 "○○가 △△ 잃었다길래"). 기록이 없으면 산 장비 이름으로 대신한다.
        private string GetLostItemName(int buyerId, string fallback)
        {
            if (_npcSaveDict.TryGetValue(buyerId, out NpcSaveData? buyer) == true && buyer.PersonalStories?.Loss != null &&
                string.IsNullOrEmpty(buyer.PersonalStories.Loss.ItemName) == false)
                return buyer.PersonalStories.Loss.ItemName;
            return fallback;
        }

        /// <summary>
        /// 광석 판매 (§5.5·§7.1). 상점이 재고 기반 가격으로 상한(20개)까지만 산다. 지금 제작 수요가 있으면 그 광석은 다음 제작 1회분(3개)을
        /// 남긴다. 못 판 광석은 가방에 남고, 상점이 받아 주지 않은 시각을 기록한다 (§9.4 광부 대사).
        /// </summary>
        private void SellOreToShop(int entityId, NpcSaveData saveData, VillageData village, int slotIndex, ItemData item,
            ref int reserveLeft, int reserveOreId, ref NpcTradeReport report)
        {
            int quantity = Mathf.Max(1, item.Quantity);
            int keep = 0;
            if (item.Id == reserveOreId && reserveLeft > 0)
            {
                keep = Mathf.Min(quantity, reserveLeft);
                reserveLeft -= keep;
                _occupationCounters.OreReserved += keep;
            }
            int sellable = quantity - keep;
            if (sellable <= 0)
                return;

            Tables.ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
            int unitPrice = table != null ? AR.s.Village.GetOreShopBuyPrice(village, table) : -1;
            int room = VillageManager.GetOreStockRoom(village, item.Id);
            int count = Mathf.Min(sellable, room);
            if (unitPrice < 0 || count <= 0)
            {
                report.SellRejectedCount++;
                _occupationCounters.OreSellRejectedCap++;
                MarkOreSellRejected(entityId, saveData);
                return;
            }
            if (AR.s.Village.TryCommitNpcOreSale(village.VillageId, item.Id, count, unitPrice) == false)
            {
                report.SellRejectedCount++;
                _occupationCounters.OreSellRejectedGold++;
                MarkOreSellRejected(entityId, saveData);
                return;
            }

            int gold = unitPrice * count;
            saveData.Gold += gold;
            if (count >= quantity)
                saveData.Inventory[slotIndex] = null;
            else
                item.Quantity = quantity - count;
            report.SoldCount++;
            report.SoldGold += gold;
            _occupationCounters.OreSold += count;
            _occupationCounters.OreSoldGold += gold;
            if (count < sellable)
            {
                _occupationCounters.OreSellRejectedCap++;
                MarkOreSellRejected(entityId, saveData);
            }
        }

        private void MarkOreSellRejected(int entityId, NpcSaveData saveData)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Identity);
            (saveData.Work ??= new NpcWorkRecord()).LastOreSellRejectedGameTime = AR.s.Time.CurrentGameTime;
        }

        /// <summary>
        /// 내 제작품 위탁 (§7.2): 일반 매각 대신 위탁 칸에 맡긴다 (마을당 8칸, 제작자당 2개). 자리가 없으면 가방에 둔다(버리지 않음).
        /// 마지막 제작품이 장비를 잃은 이웃을 보고 만든 것이면 그 사실을 위탁 칸에 남긴다 (§9.2 ②).
        /// </summary>
        private void ConsignCraftedItems(int entityId, NpcSaveData saveData, VillageData village, ref NpcTradeReport report)
        {
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (IsOwnUnconsignedCraft(entityId, item) == false)
                    continue;
                if (VillageManager.CanConsign(village, entityId) == false)
                {
                    _occupationCounters.ConsignNoSlot++;
                    return;
                }
                MarkSaveDirty(entityId, NpcSaveGroup.Inventory);
                int madeFor = saveData.Work != null && saveData.Work.LastCraftItemInstanceId == item!.ItemInstanceId
                    ? saveData.Work.LastCraftForNpcId : -1;
                saveData.Inventory[i] = null;
                AR.s.Village.AddConsignment(village, item!, entityId, GetDisplayName(entityId), madeFor);
                report.ConsignedCount++;
                _occupationCounters.Consigned++;
                Debug.Log($"[NpcOccupation] Consign npc={entityId} v={village.VillageId} item={DescribeItem(item!)} madeFor={madeFor}");
            }
        }

        /// <summary>
        /// 제작용 광석 구매 (§7.1 ③·§6.2 재료 확보): 제작 수요가 있는데 그 광석이 3개 모자라면 상점 재고에서 산다(소지금 − 치료비 예비금 안에서).
        /// 재고가 없으면 못 만든 시각을 남긴다 (§9.4 "철만 넉넉하면").
        /// </summary>
        private void BuyOreForCrafting(int entityId, NpcSaveData saveData, VillageData village)
        {
            if (TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out _) == false)
                return;
            int need = NpcOccupationRules.OresPerCraft - CountBagItem(saveData, recipe.OreItemId);
            if (need <= 0)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Wallet | NpcSaveGroup.Inventory | NpcSaveGroup.Identity);
            int stock = VillageManager.GetOreStock(village, recipe.OreItemId);
            int unitPrice = AR.s.Village.GetOreShopSellPrice(village, recipe.OreItemId);
            if (stock < need || unitPrice <= 0)
            {
                _occupationCounters.OreBuyNoStock++;
                NpcWorkRecord work = saveData.Work ??= new NpcWorkRecord();
                work.CraftBlockedGameTime = AR.s.Time.CurrentGameTime;
                work.CraftBlockedOreItemId = recipe.OreItemId;
                return;
            }
            int cost = unitPrice * need;
            if (cost > saveData.Gold - GetTreatmentFee())
            {
                _occupationCounters.OreBuyNoMoney++;
                return;
            }
            ItemData? ore = AR.s.Item.CreateInventoryItemData(recipe.OreItemId, need);
            if (ore == null || CreateNpcInventoryView(saveData).AddItem(ore) < 0)
                return;
            if (AR.s.Village.TryCommitNpcOrePurchase(village, recipe.OreItemId, need, unitPrice) == false)
            {
                ConsumeBagItem(saveData, recipe.OreItemId, need);
                return;
            }
            saveData.Gold -= cost;
            _occupationCounters.OreBought += need;
            _occupationCounters.OreBoughtGold += cost;
            if (saveData.Work != null)
            {
                saveData.Work.CraftBlockedGameTime = -1f;
                saveData.Work.CraftBlockedOreItemId = 0;
            }
        }

        /// <summary>
        /// 상점에서 할 일이 있는가 (Trade 기회의 NPC 생활 부분): 받을 위탁 대금, 맡길 제작품, 살 제작용 광석, 빈 칸에 맞는 매물.
        /// </summary>
        private bool HasOccupationTradeOpportunity(int entityId, NpcSaveData saveData, VillageData village)
        {
            if (VillageManager.HasConsignmentProceeds(village, entityId) == true)
                return true;
            if (CountUnconsignedCrafts(entityId, saveData) > 0 && VillageManager.CanConsign(village, entityId) == true)
                return true;
            if (TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out _) == true)
            {
                int need = NpcOccupationRules.OresPerCraft - CountBagItem(saveData, recipe.OreItemId);
                if (need > 0 && VillageManager.GetOreStock(village, recipe.OreItemId) >= need)
                    return true;
            }
            return HasRefillCandidate(entityId, saveData, village);
        }

        // 빈 칸에 맞는 매물이 상점에 있고 살 돈이 있는가 (전투력 평가 없이 칸·가격만 — 기회 판정용).
        private bool HasRefillCandidate(int entityId, NpcSaveData saveData, VillageData village)
        {
            if (saveData.HasPendingEquipmentLoss(out NpcEquipmentLoss? loss) == false || loss == null || loss.Slot < 0)
                return false;
            int budget = saveData.Gold - GetTreatmentFee();
            if (budget <= 0)
                return false;
            GlobalEnum.EquipmentTypeMask allowed = ARPG.Utility.EquipHelper.SlotToEquipmentTypeMask((GlobalEnum.EquipSlotType)loss.Slot);
            for (int i = 0; i < village.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = village.MerchantStock[i];
                if (AR.s.Village.IsNpcPurchasableEntry(village, entry) == true &&
                    Utils.ContainsEquipmentType(allowed, entry.Item.Equipment!.EquipType) == true &&
                    VillageManager.GetNpcPurchasePrice(entry) <= budget)
                    return true;
            }
            for (int i = 0; i < village.Consignments.Count; i++)
            {
                VillageConsignment consignment = village.Consignments[i];
                if (consignment.MakerNpcId != entityId && consignment.Item?.Equipment != null &&
                    Utils.ContainsEquipmentType(allowed, consignment.Item.Equipment.EquipType) == true &&
                    AR.s.Village.GetConsignmentPrice(village, consignment) <= budget)
                    return true;
            }
            return false;
        }

        /// <summary>제작자가 마을을 떠날 때 위탁 정산 (§7.2 제작자 이주). 이주 시작 때 부른다.</summary>
        private void SettleConsignmentsOnDeparture(int entityId, NpcSaveData saveData)
        {
            if (saveData.VillageId < 0)
                return;
            int gold = AR.s.Village.SettleDepartingMaker(saveData.VillageId, entityId);
            if (gold <= 0)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Wallet);
            saveData.Gold += gold;
        }
    }
}
