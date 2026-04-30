#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: MerchantStall 매물 1건. VillageData.MerchantStock에 누적.
    /// 게임시간 24h마다 VillageManager.RollMerchantStock으로 재롤.
    /// 풀 조건: ItemTable.Tier ≤ village.Stage AND ItemTable.BasePrice > 0 (BasePrice가 매물 자격 플래그 겸용).
    /// </summary>
    [Serializable]
    public class MerchantStockEntry
    {
        public int ItemTableId;
        public int RemainingCount;
    }
}
