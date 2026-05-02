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

        // 모든 임계값은 VillageStageTable에서 조회 — System_VillageTierProgression.CanPromoteTo와 동일 정본
        private static string FormatTierCheck(VillageData v, VillageStorageComponent s, float now)
        {
            if (v.Stage >= VillageStage.City) return "";
            VillageStage next = v.Stage + 1;
            Tables.VillageStageTable? t = AR.s.Data.GetVillageStage(next);
            if (t == null || t.PromoMinPopulation < 0) return "";

            int housingCount = AR.s.Village.CountByService(v.VillageId, ProvidedService.Housing);
            float ageHours = now - v.RegisteredAt;

            string parts = $"Pop{Mark(v.Population >= t.PromoMinPopulation, $"{v.Population}/{t.PromoMinPopulation}")}"
                         + $" Housing{Mark(housingCount >= t.PromoMinHousing, $"{housingCount}/{t.PromoMinHousing}")}"
                         + $" Food{Mark(s.FoodAmount >= t.PromoMinFood, $"{s.FoodAmount}/{t.PromoMinFood}")}"
                         + $" Age{Mark(ageHours >= t.PromoMinAgeHours, $"{ageHours:F0}/{t.PromoMinAgeHours:F0}h")}";

            if (t.PromoRequiredSet >= 0)
            {
                var setType = (ObjectSetType)t.PromoRequiredSet;
                bool ok = AR.s.Village.HasObjectSet(v.VillageId, setType);
                parts += $" {setType}{Mark(ok, "")}";
            }
            if (t.PromoRequiredCivic > 0)
            {
                int civic = AR.s.Village.CountByService(v.VillageId, ProvidedService.Civic);
                parts += $" Civic{Mark(civic >= t.PromoRequiredCivic, $"{civic}/{t.PromoRequiredCivic}")}";
            }
            if (t.PromoRequiredShop > 0)
            {
                int shop = AR.s.Village.CountByService(v.VillageId, ProvidedService.Shop);
                parts += $" Shop{Mark(shop >= t.PromoRequiredShop, $"{shop}/{t.PromoRequiredShop}")}";
            }

            return $"TierCheck(→{next}): {parts}";
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

            // Step A: task가 별도 entity에 부착되므로 풀에서 마을 ID 매칭으로 조회
            ObjectPlacementTaskComponent? activeTask = FindActiveTaskForVillage(v.VillageId);
            if (activeTask.HasValue == false)
            {
                // 태스크 없음 → 점수 1위 후보 표시 (BUILD_PRIORITY_DESIGN.md §2)
                int nextId = VillageNeedsEvaluator.GetTopCandidate(v);
                if (nextId < 0)
                    return "✓후보없음";

                Tables.BuildableItemTable? nextTable = AR.s.Data.GetBuildableItem(nextId);
                string nextName = nextTable != null ? nextTable.Name : $"Id{nextId}";
                return $"대기({nextName})";
            }

            ObjectPlacementTaskComponent task = activeTask.Value;
            float total = task.BuildDurationHours;
            float remain = Mathf.Max(0f, total - task.AccumulatedHours);
            int pct = total > 0f
                ? Mathf.Clamp(Mathf.FloorToInt(task.AccumulatedHours / total * 100f), 0, 100)
                : 0;

            Tables.BuildableItemTable? curTable = AR.s.Data.GetBuildableItem(task.TargetTableId);
            string curName = curTable != null ? curTable.Name : $"Id{task.TargetTableId}";
            string npcNote = task.AssignedNpcEntityId >= 0
                ? $", npc{task.AssignedNpcEntityId}"
                : ", 미배정";
            return $"{curName} {pct}%(누적 {task.AccumulatedHours:F1}/{total:F1}h, 남은 {remain:F1}h, tile={task.TileX},{task.TileY}{npcNote})";
        }

        /// <summary>
        /// Step A: task entity 풀에서 villageId가 일치하는 첫 task 반환. 마을당 1개 보장.
        /// </summary>
        private static ObjectPlacementTaskComponent? FindActiveTaskForVillage(int villageId)
        {
            SparseSet<ObjectPlacementTaskComponent> pool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();
            for (int i = 0; i < pool.Count; i++)
            {
                ObjectPlacementTaskComponent t = pool.GetByIndex(i);
                if (t.VillageId == villageId) return t;
            }
            return null;
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
