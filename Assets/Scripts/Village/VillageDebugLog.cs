#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 상태 스냅샷 로그 유틸 (테스트용 수동 호출).
    /// Phase A는 화면 UI를 만들지 않고 이 유틸로 디버깅.
    /// 사용자가 이후 UI를 추가하면 동일 데이터 접근 패턴을 재사용 가능.
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

            string food = Fmt("Food", s.FoodAmount, s.FoodCap, (s.SurplusFlags & VillageSurplusFlags.Food) != 0);
            string wood = Fmt("Wood", s.WoodAmount, s.WoodCap, (s.SurplusFlags & VillageSurplusFlags.Wood) != 0);
            string stone = Fmt("Stone", s.StoneAmount, s.StoneCap, (s.SurplusFlags & VillageSurplusFlags.Stone) != 0);

            Debug.Log($"[VillageSnapshot] v{v.VillageId} Stage={v.Stage} Pop={v.Population}/{targetPop} {food} {wood} {stone} Hunger={s.HungerHoursAccumulated}h StoneTimer={s.StoneTimer}/5 Build={build}");
        }

        private static string Fmt(string label, int amount, int cap, bool surplus)
        {
            string tail = surplus ? "*" : "";
            return $"{label}={amount}/{cap}{tail}";
        }

        private static string FormatBuild(VillageData v, float now)
        {
            if (v.HasCampfire)
                return "✓완성";
            if (v.FirstBuildStartedAt < 0f)
                return "대기";
            float elapsed = now - v.FirstBuildStartedAt;
            float total = 2f;
            float remain = Mathf.Max(0f, total - elapsed);
            int pct = Mathf.Clamp(Mathf.FloorToInt(elapsed / total * 100f), 0, 100);
            return $"제작중 {pct}%(남은 {remain:F1}h, tile={v.FirstBuildTileX},{v.FirstBuildTileY})";
        }
    }
}
