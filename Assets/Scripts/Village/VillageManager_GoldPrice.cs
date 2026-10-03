#nullable enable
using ARPG.Data;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 골드 밸런스 — 세계 물가 지수 갱신과 가격 적용 (GOLD_BALANCE_DESIGN §4.1, D126~D130).
    /// 지수 상태는 WorldData.GoldPrice가 소유하고 규칙은 GoldPriceRules(순수 로직)에 있다.
    /// 가격 배율은 순수 재고 롤(VillageTradeStockRoller)이 아니라 이 매니저가 가격을 확정하는 지점에서만 곱한다
    /// (매물 가격 스냅샷 · 상인 매입가 · 서비스 가격). GoldPriceRules.IndexEnabled가 꺼져 있으면 배율은 항상 1이다.
    /// </summary>
    public partial class VillageManager
    {
        /// <summary>
        /// 세계 Gold = 모든 마을 금고 + 살아 있는 NPC 주머니 (플레이어 제외 — 플레이어 거래는 닫힌 경제 밖, D126).
        /// 진단은 원장 필드를 복제본으로 바꿔 끼운 채 같은 메서드로 잰다.
        /// </summary>
        public long ComputeWorldGold()
        {
            long total = 0;
            for (int i = 0; i < _villageList.Count; i++)
                total += VillageResourceSimulation.GetAmount(_villageList[i], GlobalEnum.ItemType.Gold);
            if (AR.s.Npc != null)
                total += AR.s.Npc.SumLivingNpcGold();
            return total;
        }

        /// <summary>
        /// 게임일 경계를 지났으면 세계 물가 지수를 갱신한다 (하루 1회, D130). 게임은 System_SettlementSim이
        /// GoldPriceRules.IndexEnabled일 때만 부르고, 진단은 복제 상태로 같은 메서드를 직접 부른다.
        /// </summary>
        public void UpdateGoldPriceIndex(float now)
        {
            if (AR.s.Data == null)
                return;
            GoldPriceRules.Advance(AR.s.Data.GoldPrice, ComputeWorldGold(), now);
        }

        /// <summary>등급별 가격 배율 (지수^민감도). 지수를 쓰지 않는 동안은 1.</summary>
        public float GetGoldPriceMultiplier(GoldPriceClass priceClass)
        {
            if (GoldPriceRules.IndexEnabled == false || AR.s.Data == null)
                return 1f;
            return GoldPriceRules.GetMultiplier(AR.s.Data.GoldPrice.Index, priceClass);
        }

        /// <summary>아이템 가격에 그 아이템 등급의 배율을 곱한다 (D128·D129). minPrice는 원래 가격 함수의 하한.</summary>
        public int ApplyItemPriceIndex(int price, Tables.ItemTable? item, int minPrice)
        {
            if (item == null)
                return price;
            float multiplier = GetGoldPriceMultiplier(GoldPriceRules.Classify(item.ItemType, item.Tier));
            return GoldPriceRules.ApplyMultiplier(price, multiplier, minPrice);
        }

        /// <summary>서비스 가격 (치료비·생활비 예비금·보충 NPC 시작 Gold·금고 기준선, D128)에 서비스 배율을 곱한다.</summary>
        public int GetServicePrice(int basePrice)
        {
            return GoldPriceRules.ApplyMultiplier(basePrice, GetGoldPriceMultiplier(GoldPriceClass.Service), 1);
        }
    }
}
