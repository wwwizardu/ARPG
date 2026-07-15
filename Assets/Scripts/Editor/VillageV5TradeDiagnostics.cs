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
    /// VILLAGE_REWORK_PLAN V5 전용 정적 진단. 세이브 파일·월드 상태·테이블 자산은 변경하지 않는다.
    /// TradeProfile 카탈로그·결정론 재고 롤·분포 구분·교역 차익 불변식·ShopTier/FacilityTier
    /// 풀 게이트·JSON 왕복을 검증하고 분포·가격 표본을 Temp/VILLAGE_V5_TRADE_DUMP.md로 덤프한다.
    /// 실제 상점 UI 표시·구매/판매·ItemData 인스턴스(Mod/Quality) 결정론 실측은
    /// 플레이 모드 전용이라 리포트에 절차로 기록한다.
    /// </summary>
    public static class VillageV5TradeDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V5_TRADE_DUMP.md";
        private const string ItemTablePath = "Assets/_BinaryData/TableData/ItemTable.bytes";

        [Serializable]
        private sealed class ItemRow
        {
            public int Id;
            public int Tier;
            public string Name = string.Empty;
            public int BasePrice;
            public bool Stackable;
        }

        [MenuItem("ARPG/Village/Run V5 Trade Diagnostics", false, 425)]
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

            VillageTradeProfile dwarf = Get("trade.dwarf.mountain.standard");
            VillageTradeProfile general = Get("trade.human.plains.standard");

            ValidateCatalog(items, passed, failed);
            ValidateDeterminism(dwarf, items, passed, failed);
            ValidateGuaranteedAndSlots(dwarf, general, items, passed, failed);
            ValidateDistribution(dwarf, general, items, passed, failed, dump, names);
            ValidateArbitrage(items, passed, failed, dump, names);
            ValidateFacilityGate(dwarf, items, passed, failed);
            ValidateJsonRoundtrip(passed, failed);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V5 Trade Diagnostics");
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
            report.AppendLine("## 플레이 모드 전용 항목 (이 진단이 다루지 못하는 것)");
            report.AppendLine();
            report.AppendLine("- UIShopMerchant 구매 탭의 스냅샷 가격·사유(지역 특산/현지 부족/희귀 수입) 표시 실측");
            report.AppendLine("- 매각 탭의 TradeProfile 매입가·사유 표시와 SellItemToMerchant 지급액 일치 실측");
            report.AppendLine("- BuyItemFromMerchant가 UnitPrice 스냅샷으로 차감하는지, 재롤 없이 세이브/로드 후 같은 진열 실측");
            report.AppendLine("- 같은 회차 재롤의 ItemData 인스턴스(Equipment Mod/Quality, SkillBook SkillId) 결정론 실측");
            report.AppendLine("- 구 세이브(가격 필드 없는 MerchantStock) 로드 → 진열 유지 + 가격 보정 로그 확인");
            report.AppendLine("- Dwarf 광산촌에서 광물 구매 → 비-Dwarf 마을 매각의 실제 차익 실측 (TradeGood 한정)");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, report.ToString(), Encoding.UTF8);
            Debug.Log($"[VillageV5Diagnostics] passed={passed.Count}, failed={failed.Count} → {OutputPath}");
            if (failed.Count > 0)
            {
                for (int i = 0; i < failed.Count; i++)
                    Debug.LogError($"[VillageV5Diagnostics] FAIL: {failed[i]}");
            }
        }

        private static VillageTradeProfile Get(string tradeProfileId)
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

        private static void ValidateDeterminism(VillageTradeProfile dwarf,
            List<TradeItemInfo> items, List<string> passed, List<string> failed)
        {
            string first = PlanKey(Roll(dwarf, 3, 2, items, 12345, 77, 10));
            bool same = true;
            for (int i = 0; i < 100; i++)
                same &= PlanKey(Roll(dwarf, 3, 2, items, 12345, 77, 10)) == first;
            HashSet<string> byCycle = new HashSet<string>(StringComparer.Ordinal);
            for (int cycle = 0; cycle < 20; cycle++)
                byCycle.Add(PlanKey(Roll(dwarf, 3, 2, items, 12345, 77, cycle)));
            Record(same && first.Length > 0 && byCycle.Count >= 10, passed, failed,
                $"결정론: 같은 (seed, 정착지, 회차) 100회 동일 + 20회차 고유 {byCycle.Count}개");
        }

        private static void ValidateGuaranteedAndSlots(VillageTradeProfile dwarf,
            VillageTradeProfile general, List<TradeItemInfo> items,
            List<string> passed, List<string> failed)
        {
            bool ok = true;
            int[] expectedTotals = { 5, 5, 6, 7, 8 };
            for (int shopTier = 0; shopTier <= 4; shopTier++)
            {
                List<TradeStockPlanEntry> plan = Roll(dwarf, shopTier, 0, items, 42, 1, 3);
                ok &= plan.Count == expectedTotals[shopTier];
            }
            for (int cycle = 0; cycle < 30; cycle++)
            {
                List<TradeStockPlanEntry> dPlan = Roll(dwarf, 1, 0, items, 999, 5, cycle);
                ok &= Contains(dPlan, 2003) && Contains(dPlan, 2002);
                List<TradeStockPlanEntry> gPlan = Roll(general, 1, 0, items, 999, 5, cycle);
                ok &= Contains(gPlan, 6001) && Contains(gPlan, 5000);
            }
            Record(ok, passed, failed,
                "슬롯·보장 품목: ShopTier 0/1→5, 2→6, 3→7, 4→8 + Dwarf 광물/비-Dwarf 페이지·북 보장");
        }

        private static void ValidateDistribution(VillageTradeProfile dwarf,
            VillageTradeProfile general, List<TradeItemInfo> items,
            List<string> passed, List<string> failed, StringBuilder dump,
            Dictionary<int, string> names)
        {
            Dictionary<int, int> dwarfCounts = new Dictionary<int, int>();
            Dictionary<int, int> generalCounts = new Dictionary<int, int>();
            int dwarfTagged = 0;
            int generalTagged = 0;
            for (int cycle = 0; cycle < 200; cycle++)
            {
                List<TradeStockPlanEntry> dPlan = Roll(dwarf, 3, 2, items, 7, 11, cycle);
                for (int i = 0; i < dPlan.Count; i++)
                {
                    Accumulate(dwarfCounts, dPlan[i].ItemId);
                    if ((ItemTradeTagCatalog.GetTags(dPlan[i].ItemId)
                        & (TradeTag.Ore | TradeTag.HeavyArmor)) != TradeTag.None)
                        dwarfTagged++;
                }
                List<TradeStockPlanEntry> gPlan = Roll(general, 3, 0, items, 7, 11, cycle);
                for (int i = 0; i < gPlan.Count; i++)
                {
                    Accumulate(generalCounts, gPlan[i].ItemId);
                    if ((ItemTradeTagCatalog.GetTags(gPlan[i].ItemId)
                        & (TradeTag.Ore | TradeTag.HeavyArmor)) != TradeTag.None)
                        generalTagged++;
                }
            }
            Record(dwarfTagged >= 600 && generalTagged == 0, passed, failed,
                $"분포 구분: 200회차 Dwarf 광물+중장구 {dwarfTagged}건 vs 비-Dwarf {generalTagged}건");

            dump.AppendLine("## 분포 표본 (Town ShopTier3, 200회차 — 상위 12품목)");
            dump.AppendLine();
            dump.AppendLine("| Dwarf(제련소 T2) | 횟수 | 비-Dwarf | 횟수 |");
            dump.AppendLine("|---|---:|---|---:|");
            List<KeyValuePair<int, int>> dTop = TopEntries(dwarfCounts, 12);
            List<KeyValuePair<int, int>> gTop = TopEntries(generalCounts, 12);
            for (int i = 0; i < 12; i++)
            {
                string dName = i < dTop.Count
                    ? $"{ItemName(names, dTop[i].Key)}" : string.Empty;
                string dCount = i < dTop.Count ? dTop[i].Value.ToString() : string.Empty;
                string gName = i < gTop.Count
                    ? $"{ItemName(names, gTop[i].Key)}" : string.Empty;
                string gCount = i < gTop.Count ? gTop[i].Value.ToString() : string.Empty;
                dump.AppendLine($"| {dName} | {dCount} | {gName} | {gCount} |");
            }
            dump.AppendLine();
        }

        private static void ValidateArbitrage(List<TradeItemInfo> items,
            List<string> passed, List<string> failed, StringBuilder dump,
            Dictionary<int, string> names)
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
                    VillageTradeProfile profile = Get(profileIds[p]);
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
                "차익 불변식: 전 66 프로필 왕복 손해 + 非TradeGood 운송 차익 없음");

            VillageTradeProfile dwarf = Get("trade.dwarf.mountain.standard");
            VillageTradeProfile general = Get("trade.human.plains.standard");
            dump.AppendLine("## 가격 표본 (bp = 10,000분율)");
            dump.AppendLine();
            dump.AppendLine("| 품목 | 기본가 | Dwarf 구매가 | Dwarf 매입가 | 비-Dwarf 구매가 | 비-Dwarf 매입가 |");
            dump.AppendLine("|---|---:|---:|---:|---:|---:|");
            int[] sampleIds = { 2003, 2002, 27, 48, 5000, 12 };
            for (int i = 0; i < sampleIds.Length; i++)
            {
                TradeItemInfo item = Find(items, sampleIds[i]);
                dump.AppendLine($"| {ItemName(names, item.Id)} | {item.BasePrice} " +
                    $"| {VillageTradeStockRoller.GetPlayerBuyPrice(dwarf, item)} " +
                    $"| {VillageTradeStockRoller.GetMerchantBuyPrice(dwarf, item)} " +
                    $"| {VillageTradeStockRoller.GetPlayerBuyPrice(general, item)} " +
                    $"| {VillageTradeStockRoller.GetMerchantBuyPrice(general, item)} |");
            }
            dump.AppendLine();
            TradeItemInfo iron = Find(items, 2003);
            int profit = VillageTradeStockRoller.GetMerchantBuyPrice(general, iron)
                - VillageTradeStockRoller.GetPlayerBuyPrice(dwarf, iron);
            Record(profit > 0, passed, failed,
                $"TradeGood 차익: 철 Dwarf→비-Dwarf +{profit}G/개 (재고 4~8개로 제한)");
        }

        private static void ValidateFacilityGate(VillageTradeProfile dwarf,
            List<TradeItemInfo> items, List<string> passed, List<string> failed)
        {
            int open = 0;
            bool closedOk = true;
            for (int cycle = 0; cycle < 100; cycle++)
            {
                List<TradeStockPlanEntry> openPlan = Roll(dwarf, 2, 2, items, 21, 4, cycle);
                for (int i = 0; i < openPlan.Count; i++)
                {
                    if (openPlan[i].SlotKind == TradeStockSlotKind.FacilityPool)
                        open++;
                }
                List<TradeStockPlanEntry> closedPlan = Roll(dwarf, 2, 1, items, 21, 4, cycle);
                for (int i = 0; i < closedPlan.Count; i++)
                    closedOk &= closedPlan[i].SlotKind != TradeStockSlotKind.FacilityPool;
            }
            List<TradeStockPlanEntry> firstCity = Roll(dwarf, 4, 3, items, 3, 6, 0);
            Record(open > 0 && closedOk && firstCity.Count == 8, passed, failed,
                $"특수 시설 풀: 제련소 T2 개방 {open}건/100회차, T1 닫힘, City 회차 0부터 8슬롯");
        }

        private static void ValidateJsonRoundtrip(List<string> passed, List<string> failed)
        {
            VillageData village = new VillageData(3, new Vector2(10f, 20f))
            {
                VillageProfileId = "village.dwarf.mountain.standard",
                ShopTier = 3,
                MerchantRestockCycle = 42,
            };
            village.MerchantStock.Add(new MerchantStockEntry
            {
                Item = new ARPG.Data.ItemData { Id = 2003, Quantity = 1 },
                RemainingCount = 6,
                UnitPrice = 8,
                RestockCycle = 42,
                TradeProfileId = "trade.dwarf.mountain.standard",
                SlotKind = TradeStockSlotKind.Guaranteed,
                PriceReason = TradePriceReason.LocalSpecialty,
            });
            string json = JsonConvert.SerializeObject(village);
            VillageData loaded = JsonConvert.DeserializeObject<VillageData>(json)!;
            MerchantStockEntry entry = loaded.MerchantStock[0];
            bool roundtrip = loaded.MerchantRestockCycle == 42
                && entry.UnitPrice == 8
                && entry.TradeProfileId == "trade.dwarf.mountain.standard"
                && entry.SlotKind == TradeStockSlotKind.Guaranteed
                && entry.PriceReason == TradePriceReason.LocalSpecialty;

            string legacy = "{\"VillageId\":7,\"PositionX\":1.0,\"PositionY\":2.0," +
                "\"Stage\":3,\"MerchantStock\":[{\"Item\":{\"Id\":26,\"Quantity\":1}," +
                "\"RemainingCount\":2}],\"LastMerchantRollGameTime\":100.0}";
            VillageData legacyLoaded = JsonConvert.DeserializeObject<VillageData>(legacy)!;
            MerchantStockEntry legacyEntry = legacyLoaded.MerchantStock[0];
            bool legacyOk = legacyLoaded.MerchantRestockCycle == -1
                && legacyEntry.UnitPrice == 0
                && legacyEntry.Item.Id == 26;

            Record(roundtrip && legacyOk, passed, failed,
                "JSON 왕복: 신규 스냅샷 필드 보존 + 구 포맷은 진열 유지·UnitPrice=0·회차 -1");
        }

        private static string PlanKey(List<TradeStockPlanEntry> plan)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < plan.Count; i++)
            {
                sb.Append(plan[i].ItemId).Append(':').Append(plan[i].Count).Append(':')
                    .Append(plan[i].UnitBuyPrice).Append(':').Append((int)plan[i].SlotKind)
                    .Append('|');
            }
            return sb.ToString();
        }

        private static bool Contains(List<TradeStockPlanEntry> plan, int itemId)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].ItemId == itemId)
                    return true;
            }
            return false;
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

        private static void Accumulate(Dictionary<int, int> counts, int itemId)
        {
            counts.TryGetValue(itemId, out int current);
            counts[itemId] = current + 1;
        }

        private static List<KeyValuePair<int, int>> TopEntries(
            Dictionary<int, int> counts, int limit)
        {
            List<KeyValuePair<int, int>> entries =
                new List<KeyValuePair<int, int>>(counts);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (entries.Count > limit)
                entries.RemoveRange(limit, entries.Count - limit);
            return entries;
        }

        private static string ItemName(Dictionary<int, string> names, int itemId)
        {
            return names.TryGetValue(itemId, out string? name)
                ? $"{itemId} {name}"
                : itemId.ToString();
        }
    }
}
