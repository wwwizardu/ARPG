#nullable enable
using System;
using ARPG.Data;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: MerchantStall 매물 1건. VillageData.MerchantStock에 누적.
    /// 게임시간 24h마다 VillageManager.RollMerchantStock으로 재롤.
    /// 풀 조건: ItemTable.Tier ≤ village.Stage AND ItemTable.BasePrice > 0 (BasePrice가 매물 자격 플래그 겸용).
    ///
    /// 매물은 roll 시점에 ItemData 인스턴스(Equipment Mod / SkillBook SkillId / SkillPage SkillEffectId까지 완성)를
    /// 미리 만들어 보관한다. 구매 시점에 다시 생성하지 않기 때문에 상인이 진열한 그대로 손에 들어온다.
    /// </summary>
    [Serializable]
    public class MerchantStockEntry
    {
        public ItemData Item = null!;       // roll 시점에 완성된 매물 인스턴스 (Table은 OnLoadCompleted에서 연결).
        public int RemainingCount;          // 남은 수량. Non-Stackable은 1, Stackable은 3~10.
    }
}
