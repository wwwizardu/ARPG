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
    /// 드롭 표의 참·판 몫, 실제 생성기(ItemManager)의 막힌 칸·모양·등급·접두/접미·모양별 옵션 풀·옵션 값 분포,
    /// 등급 판정식, 넣기 규칙, 판 가격, 상점 칸 수를 문서와 대조한다.
    /// </summary>
    public static partial class MonsterAffixDiagnostics
    {
        private const int CharmBoardSampleSize = 20000;
        private const int CharmSampleSize = 30000;

        // §6.2 막힌 칸 비중, §6.3 모양 비중 (가중치 30·25·12·12·15, 합 94)·등급 비중 (%)
        private static readonly int[] ExpectedBlockedShare = { 10, 35, 35, 20 };
        private static readonly double[] ExpectedShapeShare = { 31.9, 26.6, 12.8, 12.8, 16.0 };
        private static readonly double[] ExpectedGradeShare = { 10, 85, 5 };
        private static readonly int[] ExpectedBoardPrice = { 80, 60, 40, 24 };

        private static void AppendCharmDropDistribution(StringBuilder report, double[] meanCounts)
        {
            int failures = 0;
            report.AppendLine("■ 참 판·참 드롭 (PLAYER_LEVEL_CHARM_DESIGN §6)");
            failures += AppendCharmDropShares(report, meanCounts);
            failures += AppendBoardSamples(report);
            failures += AppendCharmSamples(report, 5);
            failures += AppendCharmSamples(report, 90);
            failures += AppendCharmGradeFormula(report);
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
            int[] gradeCounts = new int[3];
            int[,] gradeOptionCounts = new int[3, CharmRules.RARE_OPTION_MAX + 1];
            int[] optionHits = new int[CharmRules.OptionModIds.Count];
            bool modsOk = true;
            bool affixOk = true;
            bool poolOk = true;
            int maxTier = 0;
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < CharmSampleSize; i++)
            {
                ItemData? item = AR.s.Item.CreateRandomCharm(itemLevel);
                CharmData? charm = item?.Charm;
                if (charm == null)
                    return CheckCharm(report, false, $"참 생성 실패 (아이템 레벨 {itemLevel})");

                shapeCounts[(int)charm.Shape]++;
                GE.EquipmentGrade grade = charm.GetGrade();
                gradeCounts[(int)grade]++;
                gradeOptionCounts[(int)grade, Math.Min(CharmRules.RARE_OPTION_MAX, charm.Mods.Count)]++;
                modsOk &= item!.Id == CharmRules.GetCharmItemId(charm.Shape) && charm.ItemLevel == itemLevel;

                // 접두·접미 상한: 마법 각 1, 희귀 각 2 (§6.3)
                charm.CountAffixes(out int prefixCount, out int postfixCount);
                int limit = grade == GE.EquipmentGrade.Rare ? CharmRules.AFFIX_LIMIT : 1;
                affixOk &= prefixCount <= limit && postfixCount <= limit && prefixCount + postfixCount == charm.Mods.Count;

                seen.Clear();
                for (int m = 0; m < charm.Mods.Count; m++)
                {
                    ModInstance mod = charm.Mods[m];
                    maxTier = Math.Max(maxTier, mod.Tier);
                    // 값 범위는 참 값 표의 그 티어·모양 칸 (§6.3)
                    CharmModTierTable? valueRow = AR.s.Data.GetCharmModTier(mod.ModTableId, mod.Tier);
                    int min = 0;
                    int max = 0;
                    bool hasRange = valueRow != null && CharmRules.TryGetOptionValueRange(valueRow, charm.Shape, out min, out max);
                    modsOk &= seen.Add(mod.ModTableId) && mod.Table != null && mod.Slot == mod.Table.Slot
                        && mod.Tier <= CharmRules.MAX_CHARM_TIER
                        && hasRange == true && mod.Value1 >= min && mod.Value1 <= max && mod.Value1 >= 1;
                    poolOk &= CharmRules.IsOptionAllowedForShape(mod.ModTableId, charm.Shape);
                    int optionIndex = IndexOfCharmOption(mod.ModTableId);
                    if (optionIndex >= 0)
                        optionHits[optionIndex]++;
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
            line.Append($"| 최고 티어 T{maxTier} (≤10), 옵션 중복·범위·접두접미 칸 이상 없음={modsOk}");
            int failures = CheckCharm(report, ok, line.ToString());

            // 등급 비중 10·85·5와 등급별 옵션 수: 일반 0, 마법 1/2 반반, 희귀 3/4 반반 (§6.3)
            StringBuilder gradeLine = new StringBuilder($"참 등급 (레벨 {itemLevel}): ");
            bool gradeOk = affixOk;
            string[] gradeNames = { "일반", "마법", "희귀" };
            for (int g = 0; g < 3; g++)
            {
                double share = 100d * gradeCounts[g] / CharmSampleSize;
                gradeOk &= Math.Abs(share - ExpectedGradeShare[g]) < 0.8;
                gradeLine.Append($"{gradeNames[g]} {share:F1}% ({ExpectedGradeShare[g]}) ");
            }
            int magic = Math.Max(1, gradeCounts[(int)GE.EquipmentGrade.Magic]);
            int rare = Math.Max(1, gradeCounts[(int)GE.EquipmentGrade.Rare]);
            double magicOne = 100d * gradeOptionCounts[(int)GE.EquipmentGrade.Magic, 1] / magic;
            double rareThree = 100d * gradeOptionCounts[(int)GE.EquipmentGrade.Rare, 3] / rare;
            gradeOk &= gradeOptionCounts[(int)GE.EquipmentGrade.Normal, 0] == gradeCounts[(int)GE.EquipmentGrade.Normal]
                && gradeOptionCounts[(int)GE.EquipmentGrade.Magic, 1] + gradeOptionCounts[(int)GE.EquipmentGrade.Magic, 2] == gradeCounts[(int)GE.EquipmentGrade.Magic]
                && gradeOptionCounts[(int)GE.EquipmentGrade.Rare, 3] + gradeOptionCounts[(int)GE.EquipmentGrade.Rare, 4] == gradeCounts[(int)GE.EquipmentGrade.Rare]
                && Math.Abs(magicOne - 50d) < 2.0 && Math.Abs(rareThree - 50d) < 6.0;
            gradeLine.Append($"| 마법 1개 {magicOne:F0}%·2개 {100d - magicOne:F0}%, 희귀 3개 {rareThree:F0}%·4개 {100d - rareThree:F0}% (반반), 접두·접미 상한(마법 1·희귀 2) 지킴={affixOk}");
            failures += CheckCharm(report, gradeOk, gradeLine.ToString());

            // 모양별 옵션 풀: 붙은 옵션은 모두 그 모양에 허용된 것이고, 풀의 옵션은 모두 한 번 이상 나온다
            StringBuilder poolLine = new StringBuilder($"참 모양별 옵션 풀 (레벨 {itemLevel}): 허용 밖 옵션 없음={poolOk}");
            bool allHit = true;
            for (int o = 0; o < optionHits.Length; o++)
            {
                if (optionHits[o] > 0)
                    continue;
                allHit = false;
                poolLine.Append($", Mod {CharmRules.OptionModIds[o]} 0회");
            }
            poolLine.Append(allHit == true ? ", 15종 모두 나옴" : string.Empty);
            failures += CheckCharm(report, poolOk && allHit, poolLine.ToString());
            return failures;
        }

        private static int IndexOfCharmOption(int modId)
        {
            for (int i = 0; i < CharmRules.OptionModIds.Count; i++)
            {
                if (CharmRules.OptionModIds[i] == modId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// §6.3 등급 판정식: 희귀 확인 = min(1, 5% × M), 마법 확인 = min(1, 85/95 × M). 희귀도 배율 M이 크면 희귀가 늘고 일반이 준다.
        /// </summary>
        private static int AppendCharmGradeFormula(StringBuilder report)
        {
            float rare1 = CharmRules.GetRareChance(1f);
            float magic1 = CharmRules.GetMagicCheckChance(1f);
            float rare2 = CharmRules.GetRareChance(2f);
            float magic2 = CharmRules.GetMagicCheckChance(2f);
            bool ok = Math.Abs(rare1 - 0.05f) < 0.0001f && Math.Abs(magic1 - 85f / 95f) < 0.0001f
                && Math.Abs(rare2 - 0.10f) < 0.0001f && magic2 == 1f
                && CharmRules.ResolveGrade(1f, 499, 0) == GE.EquipmentGrade.Rare
                && CharmRules.ResolveGrade(1f, 500, 8946) == GE.EquipmentGrade.Magic
                && CharmRules.ResolveGrade(1f, 500, 8948) == GE.EquipmentGrade.Normal
                && CharmRules.ResolveGrade(2f, 999, 9999) == GE.EquipmentGrade.Rare
                && CharmRules.ResolveGrade(2f, 1000, 9999) == GE.EquipmentGrade.Magic;
            return CheckCharm(report, ok,
                $"참 등급 판정식: M=1 희귀 {rare1:P1}·마법 확인 {magic1:P1} (5%·89.5%), M=2 희귀 {rare2:P1}·마법 확인 {magic2:P0} (10%·100%, 일반 없음)");
        }

        /// <summary>
        /// §6.3 참 값 표: 옵션 풀 15종 × T1~T10 행이 모두 있고, 예시 값(T10, 모양별 칸)이 문서와 같다 —
        /// 화염 저항 1×1 3·1×2 5·1×3 8~9·2×2 12, 1×3 치명타 확률 증가 9~10, 2×2 최대 HP 45~58, 2×2 치명타 피해 증가 23~25.
        /// 시트에서 값을 고치면 이 예시도 같이 고친다.
        /// </summary>
        private static int AppendCharmValueExamples(StringBuilder report)
        {
            int missing = 0;
            StringBuilder missingLine = new StringBuilder();
            for (int o = 0; o < CharmRules.OptionModIds.Count; o++)
            {
                for (int tier = 1; tier <= CharmRules.MAX_CHARM_TIER; tier++)
                {
                    if (AR.s.Data.GetCharmModTier(CharmRules.OptionModIds[o], tier) != null)
                        continue;
                    missing++;
                    if (missing <= 5)
                        missingLine.Append($" Mod {CharmRules.OptionModIds[o]} T{tier}");
                }
            }
            int failures = CheckCharm(report, missing == 0,
                $"참 값 표: 옵션 15종 × T1~T10 = {CharmRules.OptionModIds.Count * CharmRules.MAX_CHARM_TIER}행 중 빠진 행 {missing}개{missingLine}");

            int[] modIds = { 1203, 1203, 1203, 1203, CharmRules.MOD_CRIT_RATE, 1000, CharmRules.MOD_CRIT_DAMAGE };
            GE.CharmShape[] shapes =
            {
                GE.CharmShape.Single, GE.CharmShape.Line2, GE.CharmShape.Line3, GE.CharmShape.Square2,
                GE.CharmShape.Line3, GE.CharmShape.Square2, GE.CharmShape.Square2,
            };
            int[,] expected = { { 3, 3 }, { 5, 5 }, { 8, 9 }, { 12, 12 }, { 9, 10 }, { 45, 58 }, { 23, 25 } };
            StringBuilder line = new StringBuilder("참 옵션 값 예시 (T10): ");
            bool ok = true;
            for (int i = 0; i < modIds.Length; i++)
            {
                CharmModTierTable? valueRow = AR.s.Data.GetCharmModTier(modIds[i], 10);
                if (valueRow == null)
                    return failures + CheckCharm(report, false, $"참 값 표 Mod {modIds[i]} T10 행 없음");
                ok &= CharmRules.TryGetOptionValueRange(valueRow, shapes[i], out int min, out int max) == true
                    && min == expected[i, 0] && max == expected[i, 1];
                line.Append($"Mod {modIds[i]} {CharmTooltipText.GetShapeName(shapes[i])} {min}~{max} ({expected[i, 0]}~{expected[i, 1]}) ");
            }
            return failures + CheckCharm(report, ok, line.ToString());
        }

        /// <summary>§6.4: 판 경계 안, 막힌 칸·다른 참과 겹치지 않음. 2×2는 가운데가 막히면 못 넣고, 1×3은 한 줄이 비어야.</summary>
        private static int AppendPlacementRules(StringBuilder report)
        {
            List<int> scratch = new List<int>();
            CharmBoardData empty = new CharmBoardData();
            CharmBoardData oneBlocked = new CharmBoardData { BlockedMask = 1 << 4 };   // 가운데 막힘
            bool ok = CharmRules.CanPlace(empty, GE.CharmShape.Square2, 0, 0, -1, scratch) == true
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Square2, 0, 0, -1, scratch) == false     // 2×2 네 자리 모두 가운데를 지난다
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Square2, 0, 1, -1, scratch) == false
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Square2, 0, 3, -1, scratch) == false
                && CharmRules.CanPlace(oneBlocked, GE.CharmShape.Square2, 0, 4, -1, scratch) == false
                && CharmRules.CanPlace(empty, GE.CharmShape.Square3, 0, 0, -1, scratch) == false          // 옛 세이브 읽기용 모양은 넣을 수 없다
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
            return CheckCharm(report, ok, "넣기 규칙: 2×2·1×3·경계·겹침·자기 이동·회전 판정이 문서 §6.4와 같다");
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

        private static int CheckCharm(StringBuilder report, bool ok, string message)
        {
            report.Append(ok == true ? "  [PASS] " : "  [FAIL] ").AppendLine(message);
            return ok == true ? 0 : 1;
        }
    }
}
