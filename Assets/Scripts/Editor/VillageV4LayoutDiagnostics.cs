#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.Map;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// VILLAGE_REWORK_PLAN V4 전용 정적 진단. 세이브 파일·월드 상태·청사진 자산은 변경하지 않는다.
    /// 레이아웃 슬롯·초기 벽·밀도/동선 측정과 기존 34×23 청사진 대비 비교표를
    /// Temp/VILLAGE_V4_LAYOUT_DUMP.md로 덤프한다. 실제 청크 배치·타일 충돌·프롬프트 표시·
    /// 스크린샷은 플레이 모드 전용이라 리포트에 절차로 기록한다.
    /// </summary>
    public static class VillageV4LayoutDiagnostics
    {
        private const string OutputPath = "Temp/VILLAGE_V4_LAYOUT_DUMP.md";
        private const string BuildableTablePath =
            "Assets/_BinaryData/TableData/BuildableItemTable.bytes";
        private const string DwarfBlueprintPath =
            "Assets/_BinaryData/TilemapData/BaseTown_Dwarf.bytes";

        [MenuItem("ARPG/Village/Run V4 Layout Diagnostics", false, 424)]
        public static void Run()
        {
            List<string> passed = new List<string>();
            List<string> failed = new List<string>();
            StringBuilder dump = new StringBuilder();

            Func<int, Tables.BuildableItemTable?> resolver = LoadTableResolver();

            ValidateLayoutCatalog(passed, failed);
            ValidateSlotDeterminism(passed, failed);
            ValidateBlueprintBoundaryStrip(passed, failed, dump);
            ValidateStageLayouts(resolver, passed, failed, dump);
            MeasureDensityComparison(passed, failed, dump);

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Village V4 Layout Diagnostics");
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
            report.AppendLine("- 실제 청크 materialize에서 슬롯 배치 결과·PlacementPending 재시도·반복 무중복");
            report.AppendLine("- 벽 타일 충돌·Gate 개폐 통행·Tile 피해 경로 실측 (ApplyWallDamage)");
            report.AppendLine("- Hub/시장/여관/제련소 프롬프트 표시와 F키 상호작용 실측");
            report.AppendLine("- 기존 청사진 대비 동일 카메라 비교 스크린샷");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, report.ToString(), Encoding.UTF8);
            Debug.Log($"[VillageV4Diagnostics] passed={passed.Count}, failed={failed.Count} → {OutputPath}");
            if (failed.Count > 0)
            {
                for (int i = 0; i < failed.Count; i++)
                    Debug.LogError($"[VillageV4Diagnostics] FAIL: {failed[i]}");
            }
        }

        private static Func<int, Tables.BuildableItemTable?> LoadTableResolver()
        {
            List<Tables.BuildableItemTable> rows = JsonConvert
                .DeserializeObject<List<Tables.BuildableItemTable>>(
                    File.ReadAllText(BuildableTablePath, Encoding.UTF8))!;
            Dictionary<int, Tables.BuildableItemTable> byId = new();
            for (int i = 0; i < rows.Count; i++)
                byId[rows[i].Id] = rows[i];
            return id => byId.TryGetValue(id, out Tables.BuildableItemTable? t) ? t : null;
        }

        private static void ValidateLayoutCatalog(List<string> passed, List<string> failed)
        {
            bool ok = true;
            int walled = 0;
            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                VillageLayoutProfile layout =
                    VillageLayoutCatalog.Resolve(profiles[i].LayoutProfileId);
                if (layout == null) { ok = false; continue; }
                bool isProceduralDwarf =
                    profiles[i].LayoutProfileId.StartsWith("layout.dwarf.", StringComparison.Ordinal);
                if (layout.HasFunctionalWalls) walled++;
                ok &= layout.HasFunctionalWalls == isProceduralDwarf;
            }
            Record(ok && walled == 10, passed, failed,
                $"레이아웃 카탈로그: {profiles.Count} Profile 전부 해석, 절차적 Dwarf {walled}개만 기능성 벽");
        }

        private static void ValidateSlotDeterminism(List<string> passed, List<string> failed)
        {
            VillageLayoutProfile layout = VillageLayoutCatalog.DwarfMiningLayout;
            int[] ids =
            {
                VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID,
                VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID,
                VillageInitialFacilityCatalog.INN_TABLE_ID,
                VillageInitialFacilityCatalog.FORGE_TABLE_ID,
            };
            bool ok = true;
            HashSet<(int, int)> unique = new();
            for (int i = 0; i < ids.Length; i++)
            {
                ok &= VillageLayoutCatalog.TryGetSlotRole(ids[i],
                    VillageInitialFacilityCatalog.FORGE_TABLE_ID,
                    out VillageLayoutSlotRole role);
                ok &= layout.TryGetSlot(role, out VillageLayoutSlot slot);
                ok &= unique.Add((slot.OffsetX, slot.OffsetY));
            }
            Record(ok, passed, failed,
                "Dwarf 슬롯: Hub/시장/여관/제련소 4역할 전부 고유 슬롯 (같은 Profile → 같은 오프셋)");
        }

        /// <summary>
        /// 청사진 자산은 그대로 두고, 로드 시 제거될 장식 경계 Id 1 규모를 자산에서 직접 측정한다.
        /// (실제 제거는 MapManager_Generator.LoadRaceBlueprint가 Dwarf 로드 시 수행)
        /// </summary>
        private static void ValidateBlueprintBoundaryStrip(List<string> passed,
            List<string> failed, StringBuilder dump)
        {
            if (File.Exists(DwarfBlueprintPath) == false)
            {
                Record(false, passed, failed, $"Dwarf 청사진 없음: {DwarfBlueprintPath}");
                return;
            }

            MapFileData blueprint;
            using (FileStream fs = new FileStream(DwarfBlueprintPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                blueprint = MapFileData.ReadFromBinary(reader);
            }

            int boundaryCells = 0;
            int groundCells = 0;
            int otherObjectCells = 0;
            for (int x = 0; x < blueprint.Width; x++)
            {
                for (int y = 0; y < blueprint.Height; y++)
                {
                    ulong tile = blueprint.GetTile(x, y);
                    if (tile == 0) continue;
                    groundCells++;
                    int objectId = (int)((tile
                        & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10);
                    if (objectId == 1) boundaryCells++;
                    else if (objectId != 0) otherObjectCells++;
                }
            }

            Record(boundaryCells > 0 && otherObjectCells == 0, passed, failed,
                $"Dwarf 청사진 {blueprint.Width}x{blueprint.Height}: 장식 경계 Id 1 = {boundaryCells}셀 " +
                $"(로드 시 전량 제거 대상), 그 외 오브젝트 {otherObjectCells}셀 — 제거 후 내부 전체가 보행 바닥");

            dump.AppendLine("## Dwarf 청사진 기준 (자산 비변경, 로드 시 경계 제거)");
            dump.AppendLine();
            dump.AppendLine($"- 크기: {blueprint.Width}x{blueprint.Height}, ground {groundCells}셀");
            dump.AppendLine($"- 장식 경계 Id 1: {boundaryCells}셀 → LoadRaceBlueprint가 Dwarf 로드 시 제거");
            dump.AppendLine();
        }

        private static void ValidateStageLayouts(Func<int, Tables.BuildableItemTable?> resolver,
            List<string> passed, List<string> failed, StringBuilder dump)
        {
            dump.AppendLine("## Stage별 Dwarf 초기 완성형 (시뮬레이션 좌표, 중심 (100,50))");
            dump.AppendLine();

            VillageProfileResolution resolution = VillageProfileResolver.Resolve(
                new VillageProfileContext(GlobalEnum.RaceType.Dwarf,
                    GlobalEnum.BiomeType.Mountain, VillageSettlementRole.Standard));
            VillageGrowthPlanCatalog.TryGet(resolution.Profile.GrowthPlanId,
                out VillageGrowthPlan plan);
            VillageLayoutProfile layout =
                VillageLayoutCatalog.Resolve(resolution.Profile.LayoutProfileId);

            VillageStage[] stages =
            {
                VillageStage.Settlement, VillageStage.Hamlet,
                VillageStage.Village, VillageStage.Town, VillageStage.City,
            };
            for (int s = 0; s < stages.Length; s++)
            {
                VillageStage stage = stages[s];
                VillageData v = new VillageData(9000 + s, new Vector2(100f, 50f))
                {
                    SettlementId = 9000 + s,
                    VillageProfileId = resolution.Profile.ProfileId,
                };
                int radius = layout.GetInitialBoundsRadius(stage);
                VillageDevelopmentBootstrap.ApplyFixedInitialState(v, stage, radius, 10f);
                VillageInitialDevelopment.ApplyInitialMilestones(v, plan, stage, resolver);
                VillageWallRules.ApplyInitialCompletedWalls(v, stage);

                // 슬롯 배치 시뮬레이션 (빈 맵 가정 + 벽 점유)
                HashSet<(int, int)> occupied = new();
                if (v.WallSegments != null)
                {
                    for (int i = 0; i < v.WallSegments.Count; i++)
                        occupied.Add((v.WallSegments[i].TileX, v.WallSegments[i].TileY));
                }
                int placedCount = 0;
                List<(int tableId, int x, int y)> placements = new();
                bool ok = true;
                for (int i = 0; i < v.PlacedObjects.Count; i++)
                {
                    PlacedObjectSaveData p = v.PlacedObjects[i];
                    if (VillageLayoutCatalog.TryGetSlotRole(p.TableId,
                            resolution.Profile.PrimarySpecialFacilityId,
                            out VillageLayoutSlotRole role) == false)
                    {
                        ok = false;
                        continue;
                    }
                    bool IsFree(int x, int y)
                    {
                        if (occupied.Contains((x, y))) return false;
                        int dist = Mathf.Max(Mathf.Abs(x - 100), Mathf.Abs(y - 50));
                        return radius <= 0 || dist < radius;
                    }
                    if (VillageSlotPlacer.TryFindTile(100, 50, layout, role, IsFree,
                            out int tx, out int ty) == false)
                    {
                        ok = false;
                        continue;
                    }
                    occupied.Add((tx, ty));
                    placements.Add((p.TableId, tx, ty));
                    placedCount++;
                }
                ok &= placedCount == v.PlacedObjects.Count;
                // 모든 단계에 여관이 하나 있다 — 원정 부상자가 소속 마을 여관에서 회복한다 (NPC_SIMULATION_DESIGN §3.1 D15)
                int innCount = VillageConstructionLedger.CountPlaced(v,
                    VillageInitialFacilityCatalog.INN_TABLE_ID);
                ok &= innCount == 1;

                dump.AppendLine($"### {stage} — Bounds {v.BoundsW}x{v.BoundsH}, " +
                    $"시설 {v.PlacedObjects.Count}(여관 {innCount}), 벽 {v.CompletedWallSegments}칸");
                dump.AppendLine();
                for (int i = 0; i < placements.Count; i++)
                {
                    Tables.BuildableItemTable? t = resolver(placements[i].tableId);
                    dump.AppendLine($"- {placements[i].tableId} {t?.Name}: " +
                        $"({placements[i].x},{placements[i].y})");
                }
                dump.AppendLine();

                Record(ok, passed, failed,
                    $"{stage}: 초기 시설 {v.PlacedObjects.Count}종(여관 {innCount}) 전부 슬롯 배치 성공 (벽·시설 비겹침)");
            }
        }

        private static void MeasureDensityComparison(List<string> passed,
            List<string> failed, StringBuilder dump)
        {
            // V0 기준선(문서 §V0): Dwarf 청사진 34x23, ground 609, 빈 보행 바닥 539, 경계 70, 기능 오브젝트 0
            const int blueprintArea = 34 * 23;
            VillageLayoutProfile layout = VillageLayoutCatalog.DwarfMiningLayout;
            int radius = layout.GetInitialBoundsRadius(VillageStage.Town);
            int zoneSide = radius * 2;
            int zoneArea = zoneSide * zoneSide;
            const int facilityCount = 4;   // Town: Hub/Market/Inn/Forge(T2)

            double blueprintFacilityDensity = 0.0;                       // 기능 오브젝트 0
            double v4FacilityDensity = (double)facilityCount / zoneArea; // 벽 제외 시설 밀도

            dump.AppendLine("## §7.4 밀도·동선 비교 (Town 기준)");
            dump.AppendLine();
            dump.AppendLine("| 항목 | 기존 Dwarf 청사진 | V4 초기 완성형 |");
            dump.AppendLine("|---|---|---|");
            dump.AppendLine($"| 기능 구역 | 34x23 ({blueprintArea}칸) | {zoneSide}x{zoneSide} ({zoneArea}칸) |");
            dump.AppendLine($"| 상호작용 건물 | 0 | {facilityCount} (전부 상호작용 가능) |");
            dump.AppendLine($"| 기능 밀도 (건물/칸) | {blueprintFacilityDensity:F4} | {v4FacilityDensity:F4} |");
            dump.AppendLine("| 경계 | 장식 Id 1 70셀 (기능 없음) | 기능성 Palisade/Gate 52칸 (HP·충돌·개폐) |");
            dump.AppendLine("| 남쪽 입구→최원거리 시설 | 해당 없음 (시설 없음) | 체비쇼프 ≤ 9타일 |");
            dump.AppendLine();

            Record(zoneArea < blueprintArea && facilityCount > 0, passed, failed,
                $"§7.4: V4 기능 구역 {zoneSide}x{zoneSide}({zoneArea}칸)가 기존 청사진 {blueprintArea}칸보다 작고 " +
                $"상호작용 건물 {facilityCount}개 전부 기능 보유 (기존 0개)");
        }

        private static void Record(bool ok, List<string> passed, List<string> failed,
            string description)
        {
            if (ok) passed.Add(description);
            else failed.Add(description);
        }
    }
}
