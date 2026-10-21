#nullable enable
using ARPG.Data;
using ARPG.Tables;
using GE = GlobalEnum;

namespace ARPG.Village
{
    /// <summary>
    /// 플레이어 판매가의 장비 배율 (ITEM_DROP_BALANCE_DESIGN.md §9, D10). 상태를 갖지 않는다.
    /// 상인 매입가(기본 가격 → 마을 거래 성향 → 그날 물가)에 장비 등급 배율 × 아이템 레벨 배율을 곱한다.
    /// 레벨 배율은 드롭 구간표 SellPriceMul 칸이고, 아이템 레벨 0(기본·시작·옛 장비)은 레벨 1 구간으로 본다.
    /// NPC 판매가(VillageManager.GetNpcSellPrice)에는 쓰지 않는다 — 마을 골드 밸런스 때문이다.
    /// 배율 값은 골드 수급 설계 때 다시 정하는 초안이다.
    /// </summary>
    public static class PlayerSellPriceRules
    {
        public const int MUL_DENOMINATOR = 100;
        // 등급 배율 초안 (100 = ×1.0): 일반 ×1.0, 마법 ×1.25, 희귀 ×1.5
        public const int NORMAL_GRADE_MUL = 100;
        public const int MAGIC_GRADE_MUL = 125;
        public const int RARE_GRADE_MUL = 150;

        public static int GetGradeMul(GE.EquipmentGrade grade)
        {
            switch (grade)
            {
                case GE.EquipmentGrade.Magic:
                    return MAGIC_GRADE_MUL;
                case GE.EquipmentGrade.Rare:
                    return RARE_GRADE_MUL;
                default:
                    return NORMAL_GRADE_MUL;
            }
        }

        /// <summary>장비가 아니면 그대로. 예: 매입가 36G, 레벨 90 희귀(구간 배율 300) → 36 × 150 × 300 / 10000 = 162G.</summary>
        public static int Apply(int merchantPrice, ItemData item, DropBandTable? band)
        {
            if (merchantPrice <= 0 || item.Equipment == null)
                return merchantPrice;

            int levelMul = band != null ? band.SellPriceMul : MUL_DENOMINATOR;
            long price = (long)merchantPrice * GetGradeMul(item.Equipment.GetGrade()) * levelMul
                / (MUL_DENOMINATOR * MUL_DENOMINATOR);
            return price > int.MaxValue ? int.MaxValue : (int)price;
        }
    }
}
