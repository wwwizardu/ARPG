#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 상태 스냅샷 로그 유틸 (테스트용 수동 호출).
    /// Phase B에서는 진행 중인 ObjectPlacementTaskComponent + PlacedObjectTypeIds 함께 노출.
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

            Debug.Log($"[VillageSnapshot] v{v.VillageId} Stage={v.Stage} Pop={v.Population}/{targetPop} {food} {wood} {stone} Hunger={s.HungerHoursAccumulated}h StoneTimer={s.StoneTimer}/5 Build={build} Placed=[{placed}]");
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
