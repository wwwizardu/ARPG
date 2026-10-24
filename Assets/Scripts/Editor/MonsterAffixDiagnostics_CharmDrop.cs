#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 참 판·참 드롭 분포 (PLAYER_LEVEL_CHARM_DESIGN.md §6.2·§6.3·§6.6). Monster Affix Distribution 메뉴가 함께 실행한다.
    /// 드롭 표의 참·판 몫, 실제 생성기(ItemManager)의 막힌 칸·모양·옵션 수·옵션 값 분포, 넣기 규칙, 판 가격, 상점 칸 수를 문서와 대조한다.
    /// </summary>
    public static partial class MonsterAffixDiagnostics
    {
        private const int CharmBoardSampleSize = 20000;
        private const int CharmSampleSize = 30000;

        // §6.2 막힌 칸 비중, §6.3 모양 비중 (%)
        private static readonly int[] ExpectedBlockedShare = { 10, 35, 35, 20 };
        private static readonly int[] ExpectedShapeShare = { 30, 25, 12, 12, 15, 6 };
        private static readonly int[] ExpectedBoardPrice = { 160, 60, 40, 24 };

        private static void AppendCharmDropDistribution(StringBuilder report, double[] meanCounts)
        {
            int failures = 0;
            report.AppendLine("■ 참 판·참 드롭 (PLAYER_LEVEL_CHARM_DESIGN §6)");
            failures += AppendCharmDropShares(report, meanCounts);
            failures += AppendBoardSamples(report);
            failures += AppendCharmSamples(report, 5);
            failures += AppendCharmSamples(report, 90);
            failures += AppendCharmValueExamples(report);
            failures += AppendPlacementRules(report);
            failures += AppendShopCharmSlots(report);
            report.AppendLine(failures == 0 ? "  참 판·참 결과: PASS" : $"  참 판·참 결과: FAIL {failures}건");
        }

        /// <summary>§6.6: 참 3·판 1, 꽝 48 → 마리당 참 약 0.055개, 판 약 0.018개, 장비 몫 34.1%.</summary>
        private static int AppendCharmDropShares(StringBuilder report, double[] meanCounts)
        {
            DropTable? drop = AR.s.Data.GetDrop(FieldDropTableId);
            if (drop == null)
                return CheckCharm(report, false, "드롭 1번 행 없음");

            double[] shares = { FieldNormalShare, FieldMagicShare, FieldRareShare };
            double fieldRolls = 0;
            for (int a = 0; a < Archetypes.Length; a++)
            {
                MonsterArchetypeTable? table = AR.s.Data.GetMonsterArchetype(Archetypes[a]);
                if (table != null)
                    fieldRolls += shares[a] * (Math.Max(1, table.DropRolls) + table.DropRollsPerAffix * meanCounts[a]);
            }

            int items = drop.CurrencyRate + drop.EquipmentRate + drop.SkillBookRate + drop.SkillPageRate + drop.CharmRate + drop.CharmBoardRate;
            int total = items + drop.NothingRate;
            DropBandTable? baseBand = AR.s.Data.GetDropBand(1);
            double rolls = fieldRolls * (1d + (baseBand != null ? baseBand.QuantityBonus : 0) / 100d);
            double charmPerMonster = rolls * drop.CharmRate / Math.Max(1, total);
            double boardPerMonster = rolls * drop.CharmBoardRate / Math.Max(1, total);
            double equipmentShare = 100d * drop.EquipmentRate / Math.Max(1, items);
            bool ok = drop.CharmRate == 3 && drop.CharmBoardRate == 1 && drop.NothingRate == 48
                && Math.Abs(charmPerMonster - 0.055) < 0.004 && Math.Abs(boardPerMonster - 0.018) < 0.002
                && Math.Abs(equipmentShare - 34.1) < 0.1;
            return CheckCharm(report, ok,
                $"드롭 몫: 참 {drop.CharmRate}·판 {drop.CharmBoardRate}·꽝 {drop.NothingRate} (3·1·48) → 필드 마리당 참 {charmPerMonster:F3} (0.055)·판 {boardPerMonster:F3} (0.018), 아이템 중 장비 {equipmentShare:F1}% (34.1)");
        }

        private static int AppendBoardSamples(StringBuilder report)
        {
            int[] histogram = new int[CharmRules.CELL_COUNT + 1];
            bool maskOk = true;
            long blockedSum = 0;
            for (int i = 0; i < CharmBoardSampleSize; i++)
            {
                ItemData? board = AR.s.Item.CreateRandomCharmBoard();
                if (board?.CharmBoard == null)
                    return CheckCharm(report, false, "판 생성 실패");
                int count = board.CharmBoard.GetBlockedCount();
                histogram[count]++;
                blockedSum += count;
                maskOk &= (board.CharmBoard.BlockedMask & ~((1 << CharmRules.CELL_COUNT) - 1)) == 0
                    && board.CharmBoard.Charms.Count == 0;
            }

            StringBuilder line = new StringBuilder("판 막힌 칸 분포: ");
            bool ok = maskOk && histogram[4] == 0;
            for (int c = 0; c < ExpectedBlockedShare.Length; c++)
            {
                double share = 100d * histogram[c] / CharmBoardSampleSize;
                ok &= Math.Abs(share - ExpectedBlockedShare[c]) < 1.0;
                line.Append($"{c}칸 {share:F1}% ({ExpectedBlockedShare[c]}) ");
            }
            double mean = (double)blockedSum / CharmBoardSampleSize;
            ok &= Math.Abs(mean - 1.65) < 0.03;
            line.Append($"| 평균 {mean:F2}칸 (1.65)");
            int failures = CheckCharm(report, ok, line.ToString());

            ItemTable? boardTable = AR.s.Data.GetItem(CharmRules.BOARD_ITEM_ID);
            StringBuilder priceLine = new StringBuilder("판 기본 가격: ");
            bool priceOk = boardTable != null && boardTable.BasePrice == 40;
            for (int blocked = 0; blocked < ExpectedBoardPrice.Length; blocked++)
            {
                ItemData sample = new ItemData { Id = CharmRules.BOARD_ITEM_ID, Table = boardTable, CharmBoard = new CharmBoardData { BlockedMask = (1 << blocked) - 1 } };
                int price = CharmPriceRules.GetInstanceBasePrice(sample);
                priceOk &= price == ExpectedBoardPrice[blocked];
                priceLine.Append($"{blocked}칸 {price}G ({ExpectedBoardPrice[blocked]}) ");
            }
            failures += CheckCharm(report, priceOk, priceLine.ToString());
            return failures;
        }

        private static int AppendCharmSamples(StringBuilder report, int itemLevel)
        {
            int[] shapeCounts = new int[CharmRules.SHAPE_COUNT];
            int[,] optionCounts = new int[CharmRules.SHAPE_COUNT, 5];
            bool modsOk = true;
            int maxTier = 0;
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < CharmSampleSize; i++)
            {
                ItemData? item = AR.s.Item.CreateRandomCharm(itemLevel);
                CharmData? charm = item?.Charm;
                if (charm == null)
                    return CheckCharm(report, false, $"참 생성 실패 (아이템 레벨 {itemLevel})");

                int shape = (int)charm.Shape;
                shapeCounts[shape]++;
                optionCounts[shape, Math.Min(4, charm.Mods.Count)]++;
                modsOk &= item!.Id == CharmRules.GetCharmItemId(charm.Shape) && charm.ItemLevel == itemLevel;
                seen.Clear();
                int ratio = CharmRules.GetOptionRatioPercent(charm.Shape, charm.Mods.Count);
                for (int m = 0; m < charm.Mods.Count; m++)
                {
                    ModInstance mod = charm.Mods[m];
                    maxTier = Math.Max(maxTier, mod.Tier);
                    ModTierTable? tier = FindTier(mod.ModTableId, mod.Tier);
                    int min = 0;
                    int max = 0;
                    if (tier != null)
                        CharmRules.GetOptionValueRange(tier, ratio, out min, out max);
                    modsOk &= seen.Add(mod.ModTableId) && mod.Slot == GE.ModSlot.Charm && mod.Tier <= CharmRules.MAX_CHARM_TIER
                        && tier != null && mod.Value1 >= min && mod.Value1 <= max && mod.Value1 >= 1;
                }
            }

            StringBuilder line = new StringBuilder($"참 모양 (레벨 {itemLevel}): ");
            bool ok = modsOk;
            for (int s = 0; s < CharmRules.SHAPE_COUNT; s++)
            {
                double share = 100d * shapeCounts[s] / CharmSampleSize;
                ok &= Math.Abs(share - ExpectedShapeShare[s]) < 1.0;
                line.Append($"{CharmTooltipText.GetShapeName((GE.CharmShape)s)} {share:F1}% ({ExpectedShapeShare[s]}) ");
            }
            line.Append($"| 최고 티어 T{maxTier} (≤10), 옵션 중복·범위·슬롯 이상 없음={modsOk}");
            int failures = CheckCharm(report, ok, line.ToString());

            // 옵션 수 (§6.3): 1×1 1, 1×2 1/2 반반, 1×3·L 2, 2×2 2/3 반반, 3×3 3/4 반반
            StringBuilder optionLine = new StringBuilder($"참 옵션 수 (레벨 {itemLevel}): ");
            bool optionOk = true;
            for (int s = 0; s < CharmRules.SHAPE_COUNT; s++)
            {
                GE.CharmShape shape = (GE.CharmShape)s;
                int min = CharmRules.GetOptionCountMin(shape);
                int max = CharmRules.GetOptionCountMax(shape);
                int total = Math.Max(1, shapeCounts[s]);
                double minShare = 100d * optionCounts[s, min] / total;
                optionOk &= optionCounts[s, min] + (max != min ? optionCounts[s, max] : 0) == shapeCounts[s];
                if (max != min)
                    optionOk &= Math.Abs(minShare - 50d) < 3.5;
                optionLine.Append(max == min
                    ? $"{CharmTooltipText.GetShapeName(shape)} {min}개 "
                    : $"{CharmTooltipText.GetShapeName(shape)} {min}개 {minShare:F0}%/{max}개 {100d - minShare:F0}% ");
            }
            failures += CheckCharm(report, optionOk, optionLine.ToString());
            return failures;
        }

        /// <summary>§6.3 예시 (아이템 레벨 90, T10): 1×1 최대 HP 19~24, 1×1 힘 9~12, 1×1 화염 저항 3~4, 3×3 옵션 4개 중 최대 HP 58~75.</summary>
        private static int AppendCharmValueExamples(StringBuilder report)
        {
            int[] modIds = { 1000, CharmRules.MOD_STR, 1203, 1000 };
            GE.CharmShape[] shapes = { GE.CharmShape.Single, GE.CharmShape.Single, GE.CharmShape.Single, GE.CharmShape.Square3 };
            int[] optionCounts = { 1, 1, 1, 4 };
            int[,] expected = { { 19, 24 }, { 9, 12 }, { 3, 4 }, { 58, 75 } };
            StringBuilder line = new StringBuilder("참 옵션 값 예시 (T10): ");
            bool ok = true;
            for (int i = 0; i < modIds.Length; i++)
            {
                ModTierTable? tier = FindTier(modIds[i], 10);
                if (tier == null)
                    return CheckCharm(report, false, $"Mod {modIds[i]} T10 행 없음");
                CharmRules.GetOptionValueRange(tier, CharmRules.GetOptionRatioPercent(shapes[i], optionCounts[i]), out int min, out int max);
                ok &= min == expected[i, 0] && max == expected[i, 1];
                line.Append($"Mod {modIds[i]} {CharmTooltipText.GetShapeName(shapes[i])} {min}~{max} ({expected[i, 0]}~{expected[i, 1]}) ");
            }
            return CheckCharm(report, ok, line.ToString());
        }

        /// <summary>§6.4: 판 경계 안, 막힌 칸·다른 참과 겹치지 않음. 3×3은 막힌 칸 0인 판에만, 1×3은 한 줄이 비어야.</summary>
        private static int AppendPlacementRules(StringBuilder report)
        {
            List<int> scratch = new List<int>();
            CharmBoardData empty = new CharmBoardData();
            CharmBoardData oneBlocked = new CharmBoardData { BlockedMask = 1 << 4 };   // 가운데 막힘
            bool ok = CharmRules.CanPlace(empty, GE.CharmShape.Square3, 0, 0, -1, scratch) == true
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Square3, 0, 0, -1, scratch) == false
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Line3, 0, 0, -1, scratch) == true       // 윗줄 가로
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Line3, 1, 1, -1, scratch) == false      // 가운데 세로 — 막힌 칸
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Line3, 1, 0, -1, scratch) == true       // 왼쪽 세로
                && CharmRules.CanPlace(empty, GE.CharmShape.Line2, 0, 2, -1, scratch) == false           // 오른쪽 경계 넘음
                && CharmRules.CanPlace(empty, GE.CharmShape.Square2, 0, 4, -1, scratch) == true
                && CharmRules.CanPlace(empty, GE.CharmShape.Square2, 0, 5, -1, scratch) == false;

            CharmBoardData occupied = new CharmBoardData();
            occupied.Charms.Add(new CharmPlacement
            {
                Charm = new ItemData { Id = CharmRules.GetCharmItemId(GE.CharmShape.Square2), Charm = new CharmData { Shape = GE.CharmShape.Square2 } },
                AnchorCell = 0,
                Rotation = 0,
            });
            ok &= CharmRules.CanPlace(occupied, GE.CharmShape.Single, 0, 4, -1, scratch) == false    // 2×2와 겹침
                && CharmRules.CanPlace(occupied, GE.CharmShape.Single, 0, 2, -1, scratch) == true
                && CharmRules.CanPlace(occupied, GE.CharmShape.Square2, 0, 1, 0, scratch) == true;   // 자기 자신은 빼고 옮김

            // 회전: L자 4방향 모두 3칸, 경계 안
            for (int r = 0; r < CharmRules.ROTATION_COUNT; r++)
                ok &= CharmRules.TryGetCells(GE.CharmShape.LShape, r, 0, scratch) == true && scratch.Count == 3;
            return CheckCharm(report, ok, "넣기 규칙: 3×3·1×3·경계·겹침·자기 이동·회전 판정이 문서 §6.4와 같다");
        }

        /// <summary>§6.6 상점 참 칸: 등급 1~2 판 1·참 1, 3~4 판 1·참 2.</summary>
        private static int AppendShopCharmSlots(StringBuilder report)
        {
            VillageTradeProfile profile = new VillageTradeProfile("diag", Array.Empty<int>(), Array.Empty<TradeWeightedTagPool>(),
                Array.Empty<TradeWeightedTagPool>(), Array.Empty<TradeWeightedTagPool>(), 1, Array.Empty<TradePriceRule>(),
                10000, 5000, 1, 1, 1, 1, 24f);
            bool ok = true;
            StringBuilder line = new StringBuilder("상점 참 칸: ");
            int[] expectedCharms = { 1, 1, 2, 2 };
            for (int tier = 1; tier <= 4; tier++)
            {
                int boards = profile.GetCharmBoardSlotCount(tier);
                int charms = profile.GetCharmSlotCount(tier);
                ok &= boards == 1 && charms == expectedCharms[tier - 1];
                line.Append($"등급 {tier} 판 {boards}·참 {charms} ");
            }
            return CheckCharm(report, ok, line.ToString() + "(1·1 / 1·1 / 1·2 / 1·2)");
        }

        private static ModTierTable? FindTier(int modId, int tierNumber)
        {
            List<ModTierTable> tiers = AR.s.Data.GetModTiers(modId);
            if (tiers == null)
                return null;
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i].Tier == tierNumber)
                    return tiers[i];
            }
            return null;
        }

        private static int CheckCharm(StringBuilder report, bool ok, string message)
        {
            report.Append(ok == true ? "  [PASS] " : "  [FAIL] ").AppendLine(message);
            return ok == true ? 0 : 1;
        }
    }
}
