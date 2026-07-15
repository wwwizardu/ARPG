#nullable enable
using System;
using ARPG.Data;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: MerchantStall 매물 1건. VillageData.MerchantStock에 누적.
    /// V5: 재입고 회차(MerchantRestockCycle)마다 VillageManager.RollMerchantStock이
    /// TradeProfile 기반 결정론 RNG(HistoryRng)로 재롤한다 (§8.5 — UnityEngine.Random 제거).
    ///
    /// 매물은 roll 시점에 ItemData 인스턴스(Equipment Mod / SkillBook SkillId / SkillPage SkillEffectId까지 완성)를
    /// 미리 만들어 보관한다. 구매 시점에 다시 생성하지 않기 때문에 상인이 진열한 그대로 손에 들어온다.
    /// </summary>
    [Serializable]
    public class MerchantStockEntry
    {
        public ItemData Item = null!;       // roll 시점에 완성된 매물 인스턴스 (Table은 OnLoadCompleted에서 연결).
        public int RemainingCount;          // 남은 수량. Non-Stackable은 1, Stackable은 TradeProfile 수량 범위.

        // V5 (§8.5): 확정 가격·사유 스냅샷. 재롤 직후 세이브 없이도 같은 (Profile, 회차)에서
        // 같은 결과가 재현되고, 저장된 매물은 이 스냅샷을 그대로 사용한다.
        public int UnitPrice;               // 확정 플레이어 구매가. 0 = 구 포맷 → 로드 시 Profile로 보정(§11.5).
        public int RestockCycle;            // roll 시점 재입고 회차 (재현성 진단용).
        public string TradeProfileId = string.Empty;  // roll 시점 TradeProfile 스냅샷.
        public TradeStockSlotKind SlotKind; // 보장/특산/희귀/특수 시설 슬롯 구분.
        public TradePriceReason PriceReason;// UI 표시 사유 — 지역 특산/현지 부족/희귀 수입.
    }
}
