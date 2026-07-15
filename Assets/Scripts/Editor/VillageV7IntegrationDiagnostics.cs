#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.History;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// VILLAGE_REWORK_PLAN V7 전용 통합 진단. 세이브·월드 상태·테이블 자산을 변경하지 않는다.
    ///
    /// 범위(사용자 결정 2026-07-15): 세이브를 새로 시작하므로 구 세이브 마이그레이션은 생략하고
    /// New Game 완성형 마을의 통합 검증에 집중한다. 고정 인구·Profile·초기 Stage·누적 시설·상점
    /// 스냅샷이 save/load 왕복, 동결(무발전) advance, fold-back 반복을 거쳐도 불변이며 중복되지
    /// 않음을 VillageSimulationParity.CompareFixedState로 확인하고, 표본을
    /// Temp/VILLAGE_V7_INTEGRATION_DUMP.md로 덤프한다.
    ///
    /// Temp/V7StaticHarness(net10.0 콘솔, 컴파일된 Assembly-CSharp 참조)와 같은 검증을 Unity 안에서
    /// 재실행한다. 실제 청크 materialize·Addressables·UI 실측은 플레이 모드 전용이라 절차로 기록한다.
    /// </summary>
    public static class VillageV7IntegrationDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V7_INTEGRATION_DUMP.md";
        private const string ItemTablePath = "Assets/_BinaryData/TableData/ItemTable.bytes";
        private const string BuildableTablePath =
            "Assets/_BinaryData/TableData/BuildableItemTable.bytes";
        private const int MapSeed = 12345;

        [Serializable]
        private sealed class ItemRow
        {
            public int Id;
            public int Tier;
            public int BasePrice;
            public bool Stackable;
        }

        [MenuItem("ARPG/Village/Run V7 Integration Diagnostics", false, 427)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();
            StringBuilder dump = new StringBuilder();

            List<ItemRow> rows = JsonConvert.DeserializeObject<List<ItemRow>>(
                File.ReadAllText(ItemTablePath, Encoding.UTF8))!;
            List<TradeItemInfo> items = new List<TradeItemInfo>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].BasePrice <= 0)
                    continue;
                items.Add(new TradeItemInfo(rows[i].Id, rows[i].Tier, rows[i].BasePrice,
                    rows[i].Stackable, ItemTradeTagCatalog.GetTags(rows[i].Id)));
            }
            items.Sort((a, b) => a.Id.CompareTo(b.Id));

            List<BuildableItemTable> buildables =
                JsonConvert.DeserializeObject<List<BuildableItemTable>>(
                    File.ReadAllText(BuildableTablePath, Encoding.UTF8))!;
            Dictionary<int, BuildableItemTable> byId = new Dictionary<int, BuildableItemTable>();
            for (int i = 0; i < buildables.Count; i++)
                byId[buildables[i].Id] = buildables[i];
            Func<int, BuildableItemTable?> resolver =
                id => byId.TryGetValue(id, out BuildableItemTable? t) ? t : null;

            VillageProfile dwarfCity = FindProfile(p =>
                p.PrimarySpecialFacilityId == 160 && p.MaxStage == VillageStage.City
                && p.TradeProfileId.Contains(".standard"));
            VillageProfile elfCity = FindProfile(p =>
                p.PrimarySpecialFacilityId == 0 && p.MaxStage == VillageStage.City
                && p.TradeProfileId.Contains("elf"));
            VillageProfile baseTown = FindProfile(p => p.ProfileId.Contains("base_town"));

            ValidateInitialStageThresholds(passed, failed);
            ValidateCompleteness("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateCompleteness("Elf City", elfCity, 3500, items, resolver, passed, failed);
            ValidateCompleteness("Dwarf Town", dwarfCity, 2600, items, resolver, passed, failed);
            ValidateRoundtrip("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateRoundtrip("Elf City", elfCity, 3500, items, resolver, passed, failed);
            ValidateRoundtrip("BaseTown", baseTown, 12, items, resolver, passed, failed);
            ValidateIdempotent("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateFreeze("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateFreeze("Elf City", elfCity, 3500, items, resolver, passed, failed);
            ValidateFoldback("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateLedger("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);
            ValidateComparatorDrift("Dwarf City", dwarfCity, 3500, items, resolver, passed, failed);

            DumpDevelopedVillage(dump, "Dwarf City", dwarfCity, 3500, items, resolver);
            DumpDevelopedVillage(dump, "Elf City", elfCity, 3500, items, resolver);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V7 Integration Diagnostics");
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
            report.AppendLine("## V7 범위 전제 (사용자 결정 2026-07-15)");
            report.AppendLine();
            report.AppendLine("- 세이브를 새로 시작하므로 **구 세이브 마이그레이션은 생략**한다(WorldData 버전");
            report.AppendLine("  증가·구조 backfill 코드 없음). New Game 완성형 마을 검증에 집중한다.");
            report.AppendLine("- 이 진단은 Temp/V7StaticHarness와 같은 통합 불변식을 Unity 안에서 재실행한다.");
            report.AppendLine();
            report.AppendLine("## 플레이 모드 전용 항목 (이 진단이 다루지 못하는 것 — 사용자 실행)");
            report.AppendLine();
            AppendPlayModeChecklist(report);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, report.ToString(), Encoding.UTF8);
            Debug.Log($"[VillageV7Diagnostics] passed={passed.Count}, failed={failed.Count} "
                + $"→ {OutputPath}");
            for (int i = 0; i < failed.Count; i++)
                Debug.LogError($"[VillageV7Diagnostics] FAIL: {failed[i]}");
        }

        private static VillageProfile FindProfile(Func<VillageProfile, bool> predicate)
        {
            IReadOnlyList<VillageProfile> all = VillageProfileCatalog.AllProfiles;
            for (int i = 0; i < all.Count; i++)
            {
                if (predicate(all[i]))
                    return all[i];
            }
            throw new InvalidOperationException("no matching VillageProfile");
        }

        // ========== 완성형 마을 빌더 (HistoryManager.BootstrapNewGameVillageDevelopment 미러) ==========

        private static VillageData BuildDeveloped(int villageId, int settlementKey, int rawPop,
            VillageProfile profile, List<TradeItemInfo> items,
            Func<int, BuildableItemTable?> resolver, float gameTime)
        {
            VillageData v = new VillageData(villageId, new Vector2(100f, 200f))
            {
                SettlementId = settlementKey,
                VillageProfileId = profile.ProfileId,
                Population = VillageManager.QuantizeAbstractPopulation(rawPop),
            };

            VillageStage stage = VillageProgressionRules.CalculateInitialStage(rawPop,
                profile.MaxStage);
            VillageLayoutProfile layout = VillageLayoutCatalog.Resolve(profile.LayoutProfileId);
            // Layout이 콤팩트 반경을 주면 사용, 아니면 <=0을 그대로 전달(materialize가 Stage 기준 산출).
            int boundsRadius = layout.GetInitialBoundsRadius(stage);
            VillageDevelopmentBootstrap.ApplyFixedInitialState(v, stage, boundsRadius, gameTime);

            if (layout.HasFunctionalWalls)
                VillageWallRules.ApplyInitialCompletedWalls(v, stage);

            VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(profile, out _);
            VillageInitialDevelopment.ApplyInitialMilestones(v, plan, stage, resolver);

            RollStock(v, settlementKey, profile, items, gameTime, 5);
            return v;
        }

        private static void RollStock(VillageData v, int settlementKey, VillageProfile profile,
            List<TradeItemInfo> items, float gameTime, int cycle)
        {
            v.MerchantStock.Clear();
            if (VillageTradeProfileCatalog.TryGet(profile.TradeProfileId,
                    out VillageTradeProfile trade) == false)
                return;
            int facilityTier = FacilityTierOf(v, profile.PrimarySpecialFacilityId);
            HistoryRng rng = new HistoryRng(VillageTradeStockRoller.CreateStockSeed(
                MapSeed, settlementKey, cycle));
            List<TradeStockPlanEntry> plan = VillageTradeStockRoller.RollStockPlan(trade,
                v.ShopTier, facilityTier, items, rng);
            for (int i = 0; i < plan.Count; i++)
            {
                TradeStockPlanEntry e = plan[i];
                v.MerchantStock.Add(new MerchantStockEntry
                {
                    Item = new ARPG.Data.ItemData { Id = e.ItemId, Quantity = 1 },
                    RemainingCount = e.Count,
                    UnitPrice = e.UnitBuyPrice,
                    RestockCycle = cycle,
                    TradeProfileId = trade.TradeProfileId,
                    SlotKind = e.SlotKind,
                    PriceReason = e.Reason,
                });
            }
            v.MerchantRestockCycle = cycle;
            v.LastMerchantRollGameTime = gameTime;
        }

        private static int FacilityTierOf(VillageData v, int tableId)
        {
            if (tableId <= 0)
                return 0;
            for (int i = 0; i < v.PlacedObjects.Count; i++)
            {
                if (v.PlacedObjects[i].TableId == tableId)
                    return v.PlacedObjects[i].FacilityTier;
            }
            return 0;
        }

        private static VillageData JsonClone(VillageData v)
        {
            string json = JsonConvert.SerializeObject(v);
            return JsonConvert.DeserializeObject<VillageData>(json)!;
        }

        private static void Record(bool ok, List<string> passed, List<string> failed, string name)
        {
            if (ok) passed.Add(name);
            else failed.Add(name);
        }

        // ========== 검증 (Temp/V7StaticHarness와 동일 불변식) ==========

        private static void ValidateInitialStageThresholds(List<string> passed,
            List<string> failed)
        {
            VillageStage MaxCity(int pop) =>
                VillageProgressionRules.CalculateInitialStage(pop, VillageStage.City);
            bool ok =
                MaxCity(500) == VillageStage.Settlement
                && MaxCity(597) == VillageStage.Hamlet
                && MaxCity(1679) == VillageStage.Village
                && MaxCity(2592) == VillageStage.Town
                && MaxCity(3419) == VillageStage.City
                && MaxCity(3418) == VillageStage.Town
                && VillageProgressionRules.CalculateInitialStage(5000, VillageStage.Town)
                    == VillageStage.Town;
            Record(ok, passed, failed,
                "초기 Stage 임계값 597/1679/2592/3419 + MaxStage 클램프");
        }

        private static void ValidateCompleteness(string label, VillageProfile profile,
            int rawPop, List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(1, 11, rawPop, profile, items, resolver, 100f);
            bool ok = v.InitialDevelopmentApplied
                && v.Stage == VillageProgressionRules.CalculateInitialStage(rawPop, profile.MaxStage)
                && v.AppliedGrowthMilestones.Count > 0
                && v.PlacedObjects.Count > 0
                && v.ShopTier >= 1
                && v.MerchantStock.Count > 0
                && v.ActiveBuildTasks.Count == 0;
            Record(ok, passed, failed,
                $"완성형: {label} — Stage={v.Stage}, 시설 {v.PlacedObjects.Count}, "
                + $"마일스톤 {v.AppliedGrowthMilestones.Count}, ShopTier {v.ShopTier}, "
                + $"매물 {v.MerchantStock.Count}, task {v.ActiveBuildTasks.Count}");
        }

        private static void ValidateRoundtrip(string label, VillageProfile profile, int rawPop,
            List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(2, 22, rawPop, profile, items, resolver, 100f);
            VillageData loaded = JsonClone(v);
            bool ok = VillageSimulationParity.CompareFixedState(v, loaded, out string diff);
            Record(ok, passed, failed,
                $"save/load 왕복: {label} 고정 상태 불변" + (ok ? "" : $" — {diff}"));
        }

        private static void ValidateIdempotent(string label, VillageProfile profile, int rawPop,
            List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(3, 33, rawPop, profile, items, resolver, 100f);
            VillageData baseline = JsonClone(v);
            VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(profile, out _);
            VillageInitialDevelopmentResult result =
                VillageInitialDevelopment.ApplyInitialMilestones(v, plan, v.Stage, resolver);
            bool ok = result.MilestonesApplied == 0 && result.FacilitiesCreated == 0
                && result.FacilityTierUpgrades == 0
                && VillageSimulationParity.CompareFixedState(baseline, v, out string diff);
            Record(ok, passed, failed,
                $"무중복: {label} 마일스톤 재적용 시 신규 0 + 고정 상태 불변");
        }

        private static void ValidateFreeze(string label, VillageProfile profile, int rawPop,
            List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(4, 44, rawPop, profile, items, resolver, 100f);
            VillageData baseline = JsonClone(v);
            VillageAbstractAdvanceResult result =
                VillageAbstractConstruction.AdvanceAbstractConstruction(v, 100f + 10000f,
                    new VillageAbstractAdvanceOptions
                    {
                        AllowDevelopment = false,
                        IsOperational = true,
                    });
            bool ok = result.IsCaughtUp
                && VillageSimulationParity.CompareFixedState(baseline, v, out string diff);
            Record(ok, passed, failed,
                $"동결 advance: {label} 10,000h 후 Stage·시설·ShopTier·상점 불변");
        }

        private static void ValidateFoldback(string label, VillageProfile profile, int rawPop,
            List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData original = BuildDeveloped(5, 55, rawPop, profile, items, resolver, 100f);
            VillageData baseline = JsonClone(original);
            VillageData current = JsonClone(original);
            VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(profile, out _);
            bool ok = true;
            float t = 100f;
            for (int i = 0; i < 5; i++)
            {
                current = JsonClone(current);
                t += 1000f;
                VillageAbstractConstruction.AdvanceAbstractConstruction(current, t,
                    new VillageAbstractAdvanceOptions { AllowDevelopment = false });
                VillageInitialDevelopment.ApplyInitialMilestones(current, plan, current.Stage,
                    resolver);
                if (VillageSimulationParity.CompareFixedState(baseline, current, out _) == false)
                {
                    ok = false;
                    break;
                }
            }
            Record(ok, passed, failed, $"fold-back 반복 5회: {label} 고정 상태·무중복 유지");
        }

        private static void ValidateLedger(string label, VillageProfile profile, int rawPop,
            List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(6, 66, rawPop, profile, items, resolver, 100f);
            bool ok = v.ActiveBuildTasks.Count == 0
                && v.PlacedObjectTypeIds.Count == v.PlacedObjects.Count;
            Record(ok, passed, failed,
                $"원장 정합: {label} 진행 task 0 + TypeIds==PlacedObjects "
                + $"({v.PlacedObjectTypeIds.Count})");
        }

        private static void ValidateComparatorDrift(string label, VillageProfile profile,
            int rawPop, List<TradeItemInfo> items, Func<int, BuildableItemTable?> resolver,
            List<string> passed, List<string> failed)
        {
            VillageData v = BuildDeveloped(7, 77, rawPop, profile, items, resolver, 100f);
            VillageData shopDrift = JsonClone(v);
            shopDrift.ShopTier += 1;
            VillageData milestoneDrift = JsonClone(v);
            milestoneDrift.AppliedGrowthMilestones.Add("__drift__");
            VillageData facilityDrift = JsonClone(v);
            if (facilityDrift.PlacedObjects.Count > 0)
                facilityDrift.PlacedObjects[0].FacilityTier += 5;
            VillageData stockDrift = JsonClone(v);
            if (stockDrift.MerchantStock.Count > 0)
                stockDrift.MerchantStock[0].UnitPrice += 99;
            bool ok =
                VillageSimulationParity.CompareFixedState(v, shopDrift, out _) == false
                && VillageSimulationParity.CompareFixedState(v, milestoneDrift, out _) == false
                && VillageSimulationParity.CompareFixedState(v, facilityDrift, out _) == false
                && VillageSimulationParity.CompareFixedState(v, stockDrift, out _) == false;
            Record(ok, passed, failed,
                $"음성 검증: {label} 비교기가 ShopTier·마일스톤·FacilityTier·상점가 차이 검출");
        }

        // ========== 덤프 ==========

        private static void DumpDevelopedVillage(StringBuilder dump, string label,
            VillageProfile profile, int rawPop, List<TradeItemInfo> items,
            Func<int, BuildableItemTable?> resolver)
        {
            VillageData v = BuildDeveloped(1, 11, rawPop, profile, items, resolver, 100f);
            dump.AppendLine();
            dump.AppendLine($"## 표본: {label} (raw pop {rawPop}, Profile {profile.ProfileId})");
            dump.AppendLine();
            dump.AppendLine($"- Stage: {v.Stage} / 경제 인구: {v.Population} / ShopTier: {v.ShopTier}");
            dump.AppendLine($"- 마일스톤 {v.AppliedGrowthMilestones.Count}개, 시설 "
                + $"{v.PlacedObjects.Count}개, 매물 {v.MerchantStock.Count}개, "
                + $"진행 task {v.ActiveBuildTasks.Count}개");
            dump.AppendLine();
            dump.AppendLine("| 시설 TableId | FacilityTier | PlacementPending |");
            dump.AppendLine("|---:|---:|:---:|");
            for (int i = 0; i < v.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData p = v.PlacedObjects[i];
                dump.AppendLine($"| {p.TableId} | {p.FacilityTier} | {p.PlacementPending} |");
            }
            dump.AppendLine();
            dump.AppendLine("| 매물 ItemId | 수량 | 구매가 | 슬롯 | 사유 |");
            dump.AppendLine("|---:|---:|---:|---|---|");
            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                MerchantStockEntry e = v.MerchantStock[i];
                dump.AppendLine($"| {(e.Item != null ? e.Item.Id : -1)} | {e.RemainingCount} "
                    + $"| {e.UnitPrice} | {e.SlotKind} | {e.PriceReason} |");
            }
        }

        private static void AppendPlayModeChecklist(StringBuilder report)
        {
            report.AppendLine("### A. 고정 인구 불변 (§12 런타임 고정)");
            report.AppendLine("- [ ] New Game 후 1일·30일·1년 경과, 실체화/추상 전환, 물리 NPC 사망·보충");
            report.AppendLine("      뒤에도 `SettlementLiveState.Pop`·`VillageData.Population` 불변");
            report.AppendLine("- [ ] Food 0·건물 파괴가 고정 인구를 바꾸지 않음");
            report.AppendLine();
            report.AppendLine("### B. 첫 materialize 완성형 (§14-2)");
            report.AppendLine("- [ ] 원거리 생성 마을 첫 방문 시 인구에 맞는 Stage·필수 시설·특수 시설·상점이");
            report.AppendLine("      이미 완성 (건설 과정이 눈앞에서 진행되지 않음)");
            report.AppendLine("- [ ] materialize/fold-back 반복 후 초기 마일스톤·재고 무중복");
            report.AppendLine("- [ ] PlacementPending 시설이 다음 materialize/Bounds 확장 후 배치");
            report.AppendLine();
            report.AppendLine("### C. 상호작용 (§12 상호작용)");
            report.AppendLine("- [ ] 화면의 모든 일반·특수 건물에 상호작용 프롬프트가 뜨고 F키 대상이 일치");
            report.AppendLine("- [ ] VillageHub=체크포인트 세이브, 시장=거래, 여관=휴식, 제련소=제련·수리");
            report.AppendLine("- [ ] Furnace/Anvil/QuenchVat 같은 비상호작용 건물이 별도 생성되지 않음");
            report.AppendLine("- [ ] 제련소 Tier 1/2/3에서 UIForge 기능(분해/재롤/추가+할인)이 게이트됨");
            report.AppendLine();
            report.AppendLine("### D. 벽·Gate (Dwarf, §7.5 1안)");
            report.AppendLine("- [ ] Hamlet 이상 Dwarf 마을에 둘레 Palisade + N/S Gate 2개 생성");
            report.AppendLine("- [ ] 벽 충돌(통행 차단)·HP 피해 경로·Gate 개폐 동작, 청사진 경계와 벽 미중복");
            report.AppendLine();
            report.AppendLine("### E. 상점 스냅샷·차익 (§8, V5)");
            report.AppendLine("- [ ] 상점 UI 스냅샷 가격·사유(지역 특산/현지 부족/희귀 수입) 표기와 차감액 일치");
            report.AppendLine("- [ ] 재롤 직후 세이브 없이 종료→재로드→같은 회차 재진입 시 같은 진열");
            report.AppendLine("- [ ] Dwarf 광산촌 철/구리 구매 → 非Dwarf 매각 양수 차익, 일반 장비 왕복 손해");
            report.AppendLine("- [ ] Town/City 첫 방문 상점에 T2/T3 품목·특수 시설 풀 등장");
            report.AppendLine();
            report.AppendLine("### F. Race별 상권·배치·역할 (V6)");
            report.AppendLine("- [ ] Race별 상점 품목·가격 사유가 갈림(Elf 비전 할인·Dwarf 비전 프리미엄 등)");
            report.AppendLine("- [ ] Race별 시장/여관/허브 슬롯 위치가 화면상 구분");
            report.AppendLine("- [ ] TradeHub 마을 광물 매각 프리미엄·Capital 마을 자원 Cap 상향 실측");
        }
    }
}
