#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.History;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// VILLAGE_REWORK_PLAN V6 전용 정적 진단. 세이브·월드 상태·테이블 자산을 변경하지 않는다.
    /// Race별 상권 세분화(품목·가격 구분), V5 교역 차익 불변식 유지(66프로필), TradeHub·Capital
    /// 역할 효과, Race별 Layout variant 결정론·구분을 검증하고 표본을 Temp/VILLAGE_V6_PROFILE_DUMP.md로
    /// 덤프한다. 특수 시설은 사용자 결정에 따라 Dwarf만 확정(다른 Race는 확장점) — 이 진단은 그 전제를
    /// 명시 기록한다. 실제 상점 UI·배치 시각 실측은 플레이 모드 전용이라 절차로 기록한다.
    /// </summary>
    public static class VillageV6ProfileDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V6_PROFILE_DUMP.md";
        private const string ItemTablePath = "Assets/_BinaryData/TableData/ItemTable.bytes";

        private static readonly (string Race, string Trade, string Layout)[] Races =
        {
            ("Dwarf", "trade.dwarf.mountain.standard", "layout.dwarf.mountain.standard"),
            ("Elf", "trade.elf.forest.standard", "layout.elf.forest.standard"),
            ("Orc", "trade.orc.steppe.standard", "layout.orc.steppe.standard"),
            ("Nomad", "trade.nomad.desert.standard", "layout.nomad.desert.standard"),
            ("Human", "trade.human.plains.standard", "layout.human.plains.standard"),
            ("Lizardman", "trade.lizardman.swamp.standard", "layout.lizardman.swamp.standard"),
            ("Yeti", "trade.yeti.tundra.standard", "layout.yeti.tundra.standard"),
        };

        [Serializable]
        private sealed class ItemRow
        {
            public int Id;
            public int Tier;
            public string Name = string.Empty;
            public int BasePrice;
            public bool Stackable;
        }

        [MenuItem("ARPG/Village/Run V6 Profile Diagnostics", false, 426)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();
            StringBuilder dump = new StringBuilder();

            List<ItemRow> rows = JsonConvert.DeserializeObject<List<ItemRow>>(
                File.ReadAllText(ItemTablePath, Encoding.UTF8))!;
            List<TradeItemInfo> items = new List<TradeItemInfo>();
            Dictionary<int, string> names = new Dictionary<int, string>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].BasePrice <= 0)
                    continue;
                items.Add(new TradeItemInfo(rows[i].Id, rows[i].Tier, rows[i].BasePrice,
                    rows[i].Stackable, ItemTradeTagCatalog.GetTags(rows[i].Id)));
                names[rows[i].Id] = rows[i].Name;
            }
            items.Sort((a, b) => a.Id.CompareTo(b.Id));

            ValidateCatalog(items, passed, failed);
            ValidateArbitrage(items, passed, failed);
            ValidatePerRacePrices(items, passed, failed);
            ValidateNonDwarfNoOre(items, passed, failed);
            ValidateTradeHubRole(items, passed, failed);
            ValidateCapitalRole(passed, failed);
            ValidateLayoutVariants(passed, failed);
            DumpPriceTable(items, names, dump);
            DumpLayoutTable(dump);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V6 Profile Diagnostics");
            report.AppendLine();
            report.AppendLine($"- Unity: {Application.unityVersion}");
            report.AppendLine($"- Generated UTC: {DateTime.UtcNow:O}");
            report.AppendLine($"- Passed: {passed.Count}");
            report.AppendLine($"- Failed: {failed.Count}");
            report.AppendLine();
            report.AppendLine("## Passed");
            report.AppendLine();
            for (int i = 0; i < passed.Count; i++)
                report.AppendLine($"- {passed[i]}");
            report.AppendLine();
            if (failed.Count > 0)
            {
                report.AppendLine("## Failed");
                report.AppendLine();
                for (int i = 0; i < failed.Count; i++)
                    report.AppendLine($"- {failed[i]}");
                report.AppendLine();
            }
            report.Append(dump);
            report.AppendLine();
            report.AppendLine("## V6 범위 전제 (사용자 결정)");
            report.AppendLine();
            report.AppendLine("- 주 특수 시설은 **Dwarf(제련소 160)만 확정**. 다른 Race는 0(없음) — 신규 UI/시트 없이");
            report.AppendLine("  재사용 가능한 시설 서비스가 Forge·Shrine뿐이라, 상권·배치로 차별화하고 특수 시설은");
            report.AppendLine("  확장점만 유지한다. 따라서 V6 검증 기준 '모든 Profile이 주 특수 시설 보유'는 의도적 미달.");
            report.AppendLine("- 차별화 축: 상권(품목·가격) + 배치(시설 슬롯 오프셋) 2축 이상.");
            report.AppendLine();
            report.AppendLine("## 플레이 모드 전용 항목 (이 진단이 다루지 못하는 것)");
            report.AppendLine();
            report.AppendLine("- Race별 마을 첫 방문 시 상점 품목·가격 사유 표기가 실제로 갈리는지 실측");
            report.AppendLine("- Race별 시설 슬롯 배치가 화면상 구분되는지(시장/여관/허브 위치) 실측");
            report.AppendLine("- TradeHub 마을에서 광물 매각 프리미엄, Capital 마을 자원 Cap 상향 실측");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, report.ToString(), Encoding.UTF8);
            Debug.Log($"[VillageV6Diagnostics] passed={passed.Count}, failed={failed.Count} → {OutputPath}");
            for (int i = 0; i < failed.Count; i++)
                Debug.LogError($"[VillageV6Diagnostics] FAIL: {failed[i]}");
        }

        private static VillageTradeProfile Trade(string tradeProfileId)
        {
            if (VillageTradeProfileCatalog.TryGet(tradeProfileId,
                    out VillageTradeProfile profile) == false)
                throw new InvalidOperationException($"trade profile missing: {tradeProfileId}");
            return profile;
        }

        private static List<TradeStockPlanEntry> Roll(VillageTradeProfile profile,
            int shopTier, int facilityTier, List<TradeItemInfo> items,
            int mapSeed, int settlementKey, int cycle)
        {
            HistoryRng rng = new HistoryRng(VillageTradeStockRoller.CreateStockSeed(
                mapSeed, settlementKey, cycle));
            return VillageTradeStockRoller.RollStockPlan(profile, shopTier, facilityTier,
                items, rng);
        }

        private static void Record(bool ok, List<string> passed, List<string> failed,
            string name)
        {
            if (ok) passed.Add(name);
            else failed.Add(name);
        }

        private static void ValidateCatalog(List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            int resolved = 0;
            bool guaranteedValid = true;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (VillageTradeProfileCatalog.TryGet(profiles[i].TradeProfileId,
                        out VillageTradeProfile trade) == false)
                    continue;
                resolved++;
                for (int g = 0; g < trade.GuaranteedItemIds.Count; g++)
                {
                    bool found = false;
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (items[k].Id == trade.GuaranteedItemIds[g])
                        {
                            found = items[k].BasePrice > 0;
                            break;
                        }
                    }
                    guaranteedValid &= found;
                }
            }
            Record(resolved == profiles.Count && profiles.Count == 66 && guaranteedValid,
                passed, failed,
                $"카탈로그: Profile {profiles.Count}개 전부 TradeProfileId 해석({resolved}) + 보장 품목 유효");
        }

        private static void ValidateArbitrage(List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            List<string> profileIds =
                new List<string>(VillageTradeProfileCatalog.AllTradeProfileIds);
            bool sameProfileOk = true;
            bool crossOk = true;
            for (int k = 0; k < items.Count; k++)
            {
                TradeItemInfo item = items[k];
                int minSell = int.MaxValue;
                int maxBuy = int.MinValue;
                for (int p = 0; p < profileIds.Count; p++)
                {
                    VillageTradeProfile profile = Trade(profileIds[p]);
                    int sell = VillageTradeStockRoller.GetPlayerBuyPrice(profile, item);
                    int buy = VillageTradeStockRoller.GetMerchantBuyPrice(profile, item);
                    sameProfileOk &= buy <= sell;
                    if (sell < minSell) minSell = sell;
                    if (buy > maxBuy) maxBuy = buy;
                }
                if ((item.Tags & TradeTag.TradeGood) == TradeTag.None)
                    crossOk &= maxBuy <= minSell;
            }
            Record(sameProfileOk && crossOk, passed, failed,
                "차익 불변식: 전 66프로필 왕복 손해 + 非TradeGood 운송 차익 없음");
        }

        private static void ValidatePerRacePrices(List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            TradeItemInfo arcane = Find(items, 24);
            TradeItemInfo ranged = Find(items, 22);
            TradeItemInfo heavy = Find(items, 27);
            bool elfCheapArcane = VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.elf.forest.standard"), arcane)
                < VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.dwarf.mountain.standard"), arcane);
            bool orcCheapRanged = VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.orc.steppe.standard"), ranged)
                < VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.elf.forest.standard"), ranged);
            bool yetiCheapHeavy = VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.yeti.tundra.standard"), heavy)
                < VillageTradeStockRoller.GetPlayerBuyPrice(Trade("trade.elf.forest.standard"), heavy);
            Record(elfCheapArcane && orcCheapRanged && yetiCheapHeavy, passed, failed,
                "가격 구분: Elf 비전·Orc 원거리·Yeti 중장구 특산 할인이 타 Race보다 저렴");
        }

        private static void ValidateNonDwarfNoOre(List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            bool ok = true;
            for (int r = 0; r < Races.Length; r++)
            {
                if (Races[r].Race == "Dwarf")
                    continue;
                VillageTradeProfile profile = Trade(Races[r].Trade);
                for (int cycle = 0; cycle < 40; cycle++)
                {
                    List<TradeStockPlanEntry> plan = Roll(profile, 4, 0, items, 3, 2, cycle);
                    for (int i = 0; i < plan.Count; i++)
                        ok &= (ItemTradeTagCatalog.GetTags(plan[i].ItemId)
                            & TradeTag.TradeGood) == TradeTag.None;
                }
            }
            Record(ok, passed, failed, "非Dwarf 6종 상점 모두 광물 미진열 (순환 차익 차단)");
        }

        private static void ValidateTradeHubRole(List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            TradeItemInfo iron = Find(items, 2003);
            VillageTradeProfile hub = Trade("trade.nomad.desert.tradehub");
            VillageTradeProfile std = Trade("trade.nomad.desert.standard");
            int hubBuy = VillageTradeStockRoller.GetMerchantBuyPrice(hub, iron);
            int stdBuy = VillageTradeStockRoller.GetMerchantBuyPrice(std, iron);
            int hubSell = VillageTradeStockRoller.GetPlayerBuyPrice(hub, iron);
            Record(hubBuy > stdBuy && hubBuy < hubSell, passed, failed,
                $"TradeHub 역할: 광물 매입 프리미엄 {hubBuy}G > Standard {stdBuy}G, 판매 {hubSell}G 미만");
        }

        private static void ValidateCapitalRole(List<string> passed, List<string> failed)
        {
            int capital = SumFoodCap("growth.human.plains.capital");
            int standard = SumFoodCap("growth.human.plains.standard");
            Record(capital == standard + 50, passed, failed,
                $"Capital 역할: 최종 Food Cap {capital} = Standard {standard} + 50");
        }

        private static int SumFoodCap(string growthPlanId)
        {
            if (VillageGrowthPlanCatalog.TryGet(growthPlanId, out VillageGrowthPlan plan) == false)
                throw new InvalidOperationException($"growth plan missing: {growthPlanId}");
            int sum = 0;
            for (int i = 0; i < plan.Milestones.Count; i++)
                sum += plan.Milestones[i].CapAddFood;
            return sum;
        }

        private static void ValidateLayoutVariants(List<string> passed, List<string> failed)
        {
            VillageLayoutProfile dwarf = VillageLayoutCatalog.Resolve("layout.dwarf.mountain.standard");
            bool dwarfOk = dwarf.UsesCompactBounds && dwarf.HasFunctionalWalls;
            HashSet<string> markets = new HashSet<string>(StringComparer.Ordinal);
            bool nonDwarfNoWalls = true;
            for (int r = 0; r < Races.Length; r++)
            {
                VillageLayoutProfile layout = VillageLayoutCatalog.Resolve(Races[r].Layout);
                if (Races[r].Race != "Dwarf")
                    nonDwarfNoWalls &= layout.HasFunctionalWalls == false
                        && layout.UsesCompactBounds == false;
                layout.TryGetSlot(VillageLayoutSlotRole.Market, out VillageLayoutSlot m);
                markets.Add($"{m.OffsetX},{m.OffsetY}");
            }
            Record(dwarfOk && nonDwarfNoWalls && markets.Count == 7, passed, failed,
                $"Layout: Dwarf 콤팩트+벽, 非Dwarf 벽 없음, 7종 시장 슬롯 오프셋 고유({markets.Count}/7)");
        }

        private static void DumpPriceTable(List<TradeItemInfo> items,
            Dictionary<int, string> names, StringBuilder dump)
        {
            dump.AppendLine("## Race별 가격 표본 (구매가/매입가, bp=10,000분율)");
            dump.AppendLine();
            dump.AppendLine("| Race | 철(TradeGood) | 지팡이(Arcane) | 목궁(Ranged) | 철갑옷(Heavy) | 스킬북(Rare) |");
            dump.AppendLine("|---|---|---|---|---|---|");
            int[] sample = { 2003, 24, 22, 27, 5000 };
            for (int r = 0; r < Races.Length; r++)
            {
                VillageTradeProfile p = Trade(Races[r].Trade);
                StringBuilder row = new StringBuilder($"| {Races[r].Race} ");
                for (int s = 0; s < sample.Length; s++)
                {
                    TradeItemInfo item = Find(items, sample[s]);
                    int buy = VillageTradeStockRoller.GetPlayerBuyPrice(p, item);
                    int sell = VillageTradeStockRoller.GetMerchantBuyPrice(p, item);
                    row.Append($"| {buy}/{sell} ");
                }
                row.Append("|");
                dump.AppendLine(row.ToString());
            }
            dump.AppendLine();

            dump.AppendLine("## Race별 상위 진열 품목 (Town ShopTier3, 120회차 상위 6)");
            dump.AppendLine();
            for (int r = 0; r < Races.Length; r++)
            {
                VillageTradeProfile p = Trade(Races[r].Trade);
                int facilityTier = Races[r].Race == "Dwarf" ? 2 : 0;
                Dictionary<int, int> counts = new Dictionary<int, int>();
                for (int cycle = 0; cycle < 120; cycle++)
                {
                    List<TradeStockPlanEntry> plan = Roll(p, 3, facilityTier, items, 7, 11, cycle);
                    for (int i = 0; i < plan.Count; i++)
                    {
                        counts.TryGetValue(plan[i].ItemId, out int c);
                        counts[plan[i].ItemId] = c + 1;
                    }
                }
                List<KeyValuePair<int, int>> top = new List<KeyValuePair<int, int>>(counts);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                StringBuilder line = new StringBuilder($"- **{Races[r].Race}**: ");
                for (int i = 0; i < top.Count && i < 6; i++)
                    line.Append($"{ItemName(names, top[i].Key)}({top[i].Value}) ");
                dump.AppendLine(line.ToString());
            }
            dump.AppendLine();
        }

        private static void DumpLayoutTable(StringBuilder dump)
        {
            dump.AppendLine("## Race별 Layout 슬롯 오프셋 (중심 기준)");
            dump.AppendLine();
            dump.AppendLine("| Race | Market | Inn | Hub | 콤팩트 | 벽 |");
            dump.AppendLine("|---|---|---|---|---|---|");
            for (int r = 0; r < Races.Length; r++)
            {
                VillageLayoutProfile layout = VillageLayoutCatalog.Resolve(Races[r].Layout);
                layout.TryGetSlot(VillageLayoutSlotRole.Market, out VillageLayoutSlot m);
                layout.TryGetSlot(VillageLayoutSlotRole.Inn, out VillageLayoutSlot inn);
                layout.TryGetSlot(VillageLayoutSlotRole.Hub, out VillageLayoutSlot hub);
                dump.AppendLine($"| {Races[r].Race} | ({m.OffsetX},{m.OffsetY}) " +
                    $"| ({inn.OffsetX},{inn.OffsetY}) | ({hub.OffsetX},{hub.OffsetY}) " +
                    $"| {layout.UsesCompactBounds} | {layout.HasFunctionalWalls} |");
            }
            dump.AppendLine();
        }

        private static TradeItemInfo Find(List<TradeItemInfo> items, int itemId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id == itemId)
                    return items[i];
            }
            throw new InvalidOperationException($"item missing: {itemId}");
        }

        private static string ItemName(Dictionary<int, string> names, int itemId)
        {
            return names.TryGetValue(itemId, out string? name)
                ? $"{itemId} {name}"
                : itemId.ToString();
        }
    }
}
