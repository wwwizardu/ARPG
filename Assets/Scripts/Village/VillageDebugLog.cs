#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 상태 스냅샷 로그 유틸 (테스트용 수동 호출).
    /// Phase B에서는 진행 중인 ObjectPlacementTaskComponent + PlacedObjectTypeIds 함께 노출.
    /// Phase D: TableId 하드코딩 제거 — TierCheck는 ProvidedService 비트 + HasObjectSet 사용.
    ///          Snapshot에 활성 서비스 / Jobs 할당률 추가.
    /// </summary>
    public static class VillageDebugLog
    {
        public static void SnapshotAll()
        {
            foreach (VillageData v in AR.s.Village.GetAllVillages())
                Snapshot(v);
        }

        public static void Snapshot(int villageId)
        {
            VillageData? v = AR.s.Village.GetVillage(villageId);
            if (v == null)
            {
                Debug.LogWarning($"[VillageSnapshot] v{villageId} 존재하지 않음");
                return;
            }
            Snapshot(v);
        }

        private static void Snapshot(VillageData v)
        {
            if (v.EntityId < 0 || AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false)
            {
                Debug.LogWarning($"[VillageSnapshot] v{v.VillageId} StorageComponent 없음");
                return;
            }

            Tables.VillageTable? t = AR.s.Data.GetVillageTable(v.TableId);
            int targetPop = t != null ? t.DefaultNpcIds.Count : 0;
            string build = FormatBuild(v, AR.s.Time.CurrentGameTime);
            string placed = FormatPlaced(v);

            string food = Fmt("Food", s.FoodAmount, s.FoodCap, (s.SurplusFlags & VillageSurplusFlags.Food) != 0);
            string wood = Fmt("Wood", s.WoodAmount, s.WoodCap, (s.SurplusFlags & VillageSurplusFlags.Wood) != 0);
            string stone = Fmt("Stone", s.StoneAmount, s.StoneCap, (s.SurplusFlags & VillageSurplusFlags.Stone) != 0);

            // Phase C: Bounds + Wall + TierCheck
            string bounds = $"Bounds=({v.BoundsX},{v.BoundsY},{v.BoundsW},{v.BoundsH})";
            string wall = v.WallSegmentCount > 0
                ? $" Wall={v.CompletedWallSegments}/{v.WallSegmentCount}"
                : "";
            string threat = v.ThreatLevel > 0f ? $" Threat={v.ThreatLevel:F2}" : "";
            string tier = FormatTierCheck(v, s, AR.s.Time.CurrentGameTime);

            // Phase D: 활성 서비스 + Jobs 할당률
            string services = FormatServices(v);
            string jobs = FormatJobs(v);

            Debug.Log($"[VillageSnapshot] v{v.VillageId} Stage={v.Stage} Pop={v.Population}/{targetPop} {food} {wood} {stone} Hunger={s.HungerHoursAccumulated}h StoneTimer={s.StoneTimer}/5 {bounds}{wall}{threat} {services} {jobs} Build={build} Placed=[{placed}] {tier}");
        }

        // Phase D: TableId 하드코딩 제거 — 모든 카운트는 CountByService 사용
        private static string FormatTierCheck(VillageData v, VillageStorageComponent s, float now)
        {
            int housingCount = AR.s.Village.CountByService(v.VillageId, ProvidedService.Housing);
            int civicCount = AR.s.Village.CountByService(v.VillageId, ProvidedService.Civic);
            int shopCount = AR.s.Village.CountByService(v.VillageId, ProvidedService.Shop);
            bool forgeStandard = AR.s.Village.HasObjectSet(v.VillageId, ObjectSetType.ForgeStandard);
            float ageHours = now - v.RegisteredAt;

            return v.Stage switch
            {
                VillageStage.Settlement => $"TierCheck(→Hamlet): Pop{Mark(v.Population >= 3, $"{v.Population}/3")} Housing{Mark(housingCount >= 2, $"{housingCount}/2")} Food{Mark(s.FoodAmount >= 30, $"{s.FoodAmount}/30")} Age{Mark(ageHours >= 24f, $"{ageHours:F0}/24h")}",
                VillageStage.Hamlet => $"TierCheck(→Village): Pop{Mark(v.Population >= 8, $"{v.Population}/8")} Housing{Mark(housingCount >= 4, $"{housingCount}/4")} Food{Mark(s.FoodAmount >= 80, $"{s.FoodAmount}/80")} Age{Mark(ageHours >= 72f, $"{ageHours:F0}/72h")} TownPost{Mark(civicCount >= 1, "")}",
                VillageStage.Village => $"TierCheck(→Town): Pop{Mark(v.Population >= 15, $"{v.Population}/15")} Housing{Mark(housingCount >= 8, $"{housingCount}/8")} Food{Mark(s.FoodAmount >= 200, $"{s.FoodAmount}/200")} Age{Mark(ageHours >= 168f, $"{ageHours:F0}/168h")} Forge{Mark(forgeStandard, "")} Stall{Mark(shopCount >= 1, "")}",
                _ => "",
            };
        }

        private static string Mark(bool ok, string detail)
        {
            string sym = ok ? "✓" : "✗";
            return string.IsNullOrEmpty(detail) ? sym : $"{sym}({detail})";
        }

        // Phase D: 마을 활성 서비스 비트 OR 출력
        private static string FormatServices(VillageData v)
        {
            ProvidedService all = ProvidedService.None;
            var entities = PlacedObjectRegistry.GetAllEntitiesInVillage(v.VillageId);
            for (int i = 0; i < entities.Count; i++)
            {
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entities[i], out var po))
                    all |= po.Service;
            }
            if (all == ProvidedService.None) return "Services=-";
            return $"Services={all}";
        }

        // Phase D: 마을 NPC 중 NpcAssignmentComponent 보유 수 / 전체
        private static string FormatJobs(VillageData v)
        {
            int total = v.NpcEntityIds.Count;
            int assigned = 0;
            for (int i = 0; i < total; i++)
            {
                if (AR.s.Component.HasComponent<NpcAssignmentComponent>(v.NpcEntityIds[i]))
                    assigned++;
            }
            return $"Jobs={assigned}/{total}";
        }

        private static string Fmt(string label, int amount, int cap, bool surplus)
        {
            string tail = surplus ? "*" : "";
            return $"{label}={amount}/{cap}{tail}";
        }

        private static string FormatBuild(VillageData v, float now)
        {
            if (v.EntityId < 0)
                return "대기";

            if (AR.s.Component.TryGetComponent<ObjectPlacementTaskComponent>(v.EntityId, out var task) == false)
            {
                // 태스크 없음 → 다음 후보 표시
                RoadmapEntry? next = VillageBuildRoadmap.GetNextTarget(v);
                if (next.HasValue == false)
                    return "✓로드맵완료";

                Tables.BuildableItemTable? nextTable = AR.s.Data.GetBuildableItem(next.Value.TableId);
                string nextName = nextTable != null ? nextTable.Name : $"Id{next.Value.TableId}";
                return $"대기({nextName})";
            }

            float elapsed = now - task.StartedAt;
            float total = task.BuildDurationHours;
            float remain = Mathf.Max(0f, total - elapsed);
            int pct = total > 0f
                ? Mathf.Clamp(Mathf.FloorToInt(elapsed / total * 100f), 0, 100)
                : 0;

            Tables.BuildableItemTable? curTable = AR.s.Data.GetBuildableItem(task.TargetTableId);
            string curName = curTable != null ? curTable.Name : $"Id{task.TargetTableId}";
            return $"{curName} {pct}%(남은 {remain:F1}h, tile={task.TileX},{task.TileY})";
        }

        private static string FormatPlaced(VillageData v)
        {
            if (v.PlacedObjectTypeIds == null || v.PlacedObjectTypeIds.Count == 0)
                return "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
            {
                if (i > 0) sb.Append(',');
                int id = v.PlacedObjectTypeIds[i];
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(id);
                sb.Append(table != null ? table.Name : $"Id{id}");
            }
            return sb.ToString();
        }
    }
}
