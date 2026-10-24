#nullable enable
using System.Collections.Generic;
using System.Text;
using ARPG.Data;
using ARPG.Tables;
using GE = GlobalEnum;

namespace ARPG.Item
{
    /// <summary>
    /// 참 판·참 툴팁 문구 (PLAYER_LEVEL_CHARM_DESIGN.md §8.7). 툴팁 매니저와 참 판 창이 같이 쓴다.
    /// 그림 대신 글자 3×3 격자(X 막힌 칸, ■ 박힌 참, □ 빈칸)를 쓰는 임시 표시다.
    /// </summary>
    public static class CharmTooltipText
    {
        // 호출 간 상태를 남기지 않는 스크래치 — 사용 직전 비운다
        private static readonly List<int> _cellScratch = new List<int>();
        private static readonly Dictionary<int, int> _totalScratch = new Dictionary<int, int>();
        private static readonly List<int> _totalOrderScratch = new List<int>();

        public static string GetShapeName(GE.CharmShape shape)
        {
            switch (shape)
            {
                case GE.CharmShape.Single: return "1×1";
                case GE.CharmShape.Line2: return "1×2";
                case GE.CharmShape.Line3: return "1×3";
                case GE.CharmShape.LShape: return "L자";
                case GE.CharmShape.Square2: return "2×2";
                default: return "3×3";
            }
        }

        public static string BuildBoardBody(ItemData board)
        {
            CharmBoardData? data = board.CharmBoard;
            if (data == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append("막힌 칸 ").Append(data.GetBlockedCount()).Append("칸\n");
            AppendBoardGrid(sb, data);

            if (data.Charms.Count > 0)
            {
                sb.Append("\n박힌 참 ").Append(data.Charms.Count).Append("개\n");
                for (int i = 0; i < data.Charms.Count; i++)
                {
                    ItemData? charm = data.Charms[i].Charm;
                    if (charm?.Charm == null)
                        continue;
                    sb.Append("· ").Append(charm.Table?.Name ?? "참").Append(": ");
                    AppendModsInline(sb, charm.Charm.Mods);
                    sb.Append('\n');
                }

                sb.Append("\n옵션 합계\n");
                AppendTotals(sb, data);
            }

            sb.Append("\n기본 가격 ").Append(GetBaseSellValue(board).ToString("N0")).Append("골드 (판 + 참)");
            return sb.ToString();
        }

        public static string BuildCharmBody(ItemData charm, string? locationLine)
        {
            CharmData? data = charm.Charm;
            if (data == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append("모양 ").Append(GetShapeName(data.Shape)).Append(" (").Append(CharmRules.GetCellCount(data.Shape)).Append("칸)\n");
            AppendShapeGrid(sb, data.Shape);
            if (data.ItemLevel > 0)
                sb.Append("\n아이템 레벨 ").Append(data.ItemLevel).Append('\n');
            for (int i = 0; i < data.Mods.Count; i++)
            {
                ModInstance mod = data.Mods[i];
                if (mod.Table == null)
                    mod.OnLoadCompleted();
                sb.Append(EquipmentOptionText.FormatOptionLine(mod)).Append('\n');
            }
            if (string.IsNullOrEmpty(locationLine) == false)
                sb.Append('\n').Append(locationLine);
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>여러 판의 참 옵션 합계 줄 (참 판 창 합계 칸). 옵션이 없으면 빈 문자열.</summary>
        public static string BuildTotals(IReadOnlyList<ItemData?> boards, int playerLevel)
        {
            _totalScratch.Clear();
            _totalOrderScratch.Clear();
            for (int slot = 0; slot < boards.Count; slot++)
            {
                CharmBoardData? data = boards[slot]?.CharmBoard;
                if (data == null || ARPG.Utility.PlayerLevelRules.IsCharmBoardSlotUnlocked(slot, playerLevel) == false)
                    continue;
                AccumulateTotals(data);
            }
            StringBuilder sb = new StringBuilder();
            AppendAccumulatedTotals(sb);
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>판 기본 가격 + 박힌 참 기본 가격 × 레벨 배율 (마을 거래 성향·물가 전).</summary>
        public static int GetBaseSellValue(ItemData board)
        {
            if (board.CharmBoard == null)
                return 0;
            long total = CharmPriceRules.GetInstanceBasePrice(board);
            for (int i = 0; i < board.CharmBoard.Charms.Count; i++)
            {
                ItemData? charm = board.CharmBoard.Charms[i].Charm;
                if (charm?.Charm == null || charm.Table == null)
                    continue;
                total += CharmPriceRules.ApplyCharmLevelMul(charm.Table.BasePrice, AR.s.Data.GetDropBand(charm.Charm.ItemLevel));
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static void AppendBoardGrid(StringBuilder sb, CharmBoardData data)
        {
            int occupied = CharmRules.GetOccupiedMask(data, -1, _cellScratch);
            for (int row = 0; row < CharmRules.BOARD_SIZE; row++)
            {
                for (int col = 0; col < CharmRules.BOARD_SIZE; col++)
                {
                    int bit = 1 << (row * CharmRules.BOARD_SIZE + col);
                    sb.Append((data.BlockedMask & bit) != 0 ? "X" : (occupied & bit) != 0 ? "■" : "□");
                    if (col < CharmRules.BOARD_SIZE - 1)
                        sb.Append(' ');
                }
                sb.Append('\n');
            }
        }

        private static void AppendShapeGrid(StringBuilder sb, GE.CharmShape shape)
        {
            CharmRules.TryGetCells(shape, 0, 0, _cellScratch);
            int maxRow = 0;
            int maxCol = 0;
            for (int i = 0; i < _cellScratch.Count; i++)
            {
                maxRow = System.Math.Max(maxRow, _cellScratch[i] / CharmRules.BOARD_SIZE);
                maxCol = System.Math.Max(maxCol, _cellScratch[i] % CharmRules.BOARD_SIZE);
            }
            for (int row = 0; row <= maxRow; row++)
            {
                for (int col = 0; col <= maxCol; col++)
                {
                    sb.Append(_cellScratch.Contains(row * CharmRules.BOARD_SIZE + col) ? "■" : "  ");
                    if (col < maxCol)
                        sb.Append(' ');
                }
                sb.Append('\n');
            }
        }

        private static void AppendModsInline(StringBuilder sb, List<ModInstance> mods)
        {
            for (int m = 0; m < mods.Count; m++)
            {
                if (mods[m].Table == null)
                    mods[m].OnLoadCompleted();
                if (m > 0)
                    sb.Append(", ");
                sb.Append(EquipmentOptionText.FormatModLine(mods[m]));
            }
        }

        private static void AppendTotals(StringBuilder sb, CharmBoardData data)
        {
            _totalScratch.Clear();
            _totalOrderScratch.Clear();
            AccumulateTotals(data);
            AppendAccumulatedTotals(sb);
        }

        private static void AccumulateTotals(CharmBoardData data)
        {
            for (int i = 0; i < data.Charms.Count; i++)
            {
                CharmData? charm = data.Charms[i].Charm?.Charm;
                if (charm == null)
                    continue;
                for (int m = 0; m < charm.Mods.Count; m++)
                {
                    int modId = charm.Mods[m].ModTableId;
                    if (_totalScratch.TryGetValue(modId, out int sum) == false)
                    {
                        sum = 0;
                        _totalOrderScratch.Add(modId);
                    }
                    _totalScratch[modId] = sum + charm.Mods[m].Value1;
                }
            }
        }

        private static void AppendAccumulatedTotals(StringBuilder sb)
        {
            for (int i = 0; i < _totalOrderScratch.Count; i++)
            {
                int modId = _totalOrderScratch[i];
                ModInstance total = new ModInstance
                {
                    ModTableId = modId,
                    Slot = GE.ModSlot.Charm,
                    Value1 = (ushort)System.Math.Min(_totalScratch[modId], ushort.MaxValue),
                };
                total.OnLoadCompleted();
                sb.Append(EquipmentOptionText.FormatModLine(total)).Append('\n');
            }
        }
    }
}
