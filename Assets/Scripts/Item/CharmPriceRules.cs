#nullable enable
using ARPG.Data;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Item
{
    /// <summary>
    /// 참 판·참 가격 규칙 (PLAYER_LEVEL_CHARM_DESIGN.md §6.2·§6.3·§6.5). 상태 없는 계산이다.
    /// - 판: 표 기본 가격(40G) × 막힌 칸 배율(0칸 ×4, 1칸 ×1.5, 2칸 ×1, 3칸 ×0.6). 아이템 레벨 배율 없음.
    /// - 참: 표 기본 가격(칸당 10G). 플레이어가 팔 때만 아이템 레벨 구간 판매 배율(DropBand.SellPriceMul)을 곱한다. 등급 배율 없음.
    /// - 판을 팔면 박힌 참도 함께 팔린다 — 판매가는 판 + 참 합 (VillageManager.GetPlayerSellPrice).
    /// </summary>
    public static class CharmPriceRules
    {
        private static readonly int[] _boardPriceMulPercent = { 400, 150, 100, 60 };

        /// <summary>인스턴스 기본 가격 (상점 진열가 계산용). 판은 막힌 칸 배율을 곱하고, 그 밖은 표 기본 가격.</summary>
        public static int GetInstanceBasePrice(ItemData item)
        {
            int basePrice = item.Table != null ? item.Table.BasePrice : 0;
            return item.CharmBoard != null ? ApplyBoardBlockedMul(basePrice, item.CharmBoard) : basePrice;
        }

        public static int ApplyBoardBlockedMul(int price, CharmBoardData board)
        {
            int index = Mathf.Clamp(board.GetBlockedCount(), 0, _boardPriceMulPercent.Length - 1);
            return Mathf.Max(0, Mathf.RoundToInt(price * _boardPriceMulPercent[index] / 100f));
        }

        public static int ApplyCharmLevelMul(int price, DropBandTable? band)
        {
            int levelMul = band != null ? band.SellPriceMul : PlayerSellPriceRules.MUL_DENOMINATOR;
            long result = (long)price * levelMul / PlayerSellPriceRules.MUL_DENOMINATOR;
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
