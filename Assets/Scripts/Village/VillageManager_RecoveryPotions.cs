#nullable enable
namespace ARPG.Village
{
    public partial class VillageManager
    {
        private void EnsureRecoveryPotionStock(VillageData village, VillageTradeProfile trade, int cycle)
        {
            for (int itemId = 9000; itemId <= 9001; itemId++)
            {
                bool exists = false;
                for (int i = 0; i < village.MerchantStock.Count; i++)
                    if (village.MerchantStock[i].Item.Id == itemId) { exists = true; break; }
                if (exists == true) continue;
                var table = AR.s.Data.GetItem(itemId);
                var item = AR.s.Item.CreateInventoryItemData(itemId);
                if (table == null || item == null) continue;
                MarkSaveDirty(village.VillageId, ARPG.Data.VillageSaveGroup.Shop);
                village.MerchantStock.Add(new MerchantStockEntry
                {
                    Item = item, RemainingCount = 20,
                    UnitPrice = ApplyItemPriceIndex(table.BasePrice, table, 1),
                    RestockCycle = cycle, TradeProfileId = trade.TradeProfileId,
                    SlotKind = TradeStockSlotKind.RecoverySupply, PriceReason = TradePriceReason.Standard,
                });
            }
        }
    }
}
