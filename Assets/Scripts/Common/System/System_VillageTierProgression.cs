#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase C: 마을 Tier 승격 시스템.
    /// 게임시간 4h마다 각 마을의 승격 조건 체크 + 실제 Stage 전환.
    /// 승격 시 사이드 이펙트: Bounds 확장 + Town 진입 시 WallPlanRequestTag 부착.
    /// (Stage 0~2 마을 경계는 시각화 X — 디자인 결정. NPC 행동/지형으로 추측)
    ///
    /// Stage 4 (City) 승격은 Phase C+ 이관 — Stage 3 도달 후 멈춤.
    /// 세트 판정(Furnace+Anvil)은 PlacedObjectTypeIds 단순 카운트로 대체. Phase D HasObjectSet에서 정교화.
    /// </summary>
    public class System_VillageTierProgression : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 60-64 Lifecycle
        public int Priority => 60;
        public float UpdateInterval => 5.0f;     // 실시간 5s 호출, 게임시간 4h 게이트 내장

        private const float CHECK_INTERVAL_HOURS = 4f;
        private float _lastCheckGameTime = -1f;

        // 승격 조건 참조 TableId (PHASE_C_DESIGN.md §3.1)
        private const int BED_ID = 102;
        private const int TOWNPOST_ID = 152;
        private const int FURNACE_ID = 160;
        private const int ANVIL_ID = 161;
        private const int MERCHANTSTALL_ID = 151;

        public void OnCreate()
        {
            _lastCheckGameTime = AR.s.Time.CurrentGameTime;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            if (now - _lastCheckGameTime < CHECK_INTERVAL_HOURS) return;
            _lastCheckGameTime = now;

            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;

                VillageStage next = EvaluateNextStage(v, now);
                if (next != v.Stage)
                    Promote(v, next);
            }
        }

        public void OnReset()
        {
            _lastCheckGameTime = -1f;
        }

        // ========== 조건 평가 ==========

        private static VillageStage EvaluateNextStage(VillageData v, float now)
        {
            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false)
                return v.Stage;

            int bedCount = CountInPlaced(v, BED_ID);
            float ageHours = now - v.RegisteredAt;

            switch (v.Stage)
            {
                case VillageStage.Settlement:
                    if (v.Population >= 3 && bedCount >= 2 && s.FoodAmount >= 30 && ageHours >= 24f)
                        return VillageStage.Hamlet;
                    break;
                case VillageStage.Hamlet:
                    if (v.Population >= 8 && bedCount >= 4 && s.FoodAmount >= 80 && ageHours >= 72f
                        && CountInPlaced(v, TOWNPOST_ID) >= 1)
                        return VillageStage.Village;
                    break;
                case VillageStage.Village:
                    if (v.Population >= 15 && bedCount >= 8 && s.FoodAmount >= 200 && ageHours >= 168f
                        && CountInPlaced(v, FURNACE_ID) >= 1
                        && CountInPlaced(v, ANVIL_ID) >= 1
                        && CountInPlaced(v, MERCHANTSTALL_ID) >= 1)
                        return VillageStage.Town;
                    break;
                // Town → City (Stage 4)는 Phase C+ 이관
            }
            return v.Stage;
        }

        private static int CountInPlaced(VillageData v, int tableId)
        {
            if (v.PlacedObjectTypeIds == null) return 0;
            int count = 0;
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
                if (v.PlacedObjectTypeIds[i] == tableId) count++;
            return count;
        }

        // ========== 승격 적용 ==========

        private static void Promote(VillageData v, VillageStage next)
        {
            VillageStage prev = v.Stage;
            v.Stage = next;

            // 1. Bounds 확장 + VillageComponent 동기화
            int radius = VillageManager.GetBoundsRadius(next);
            RectInt newBounds = new RectInt(
                Mathf.FloorToInt(v.PositionX) - radius,
                Mathf.FloorToInt(v.PositionY) - radius,
                radius * 2, radius * 2
            );
            v.BoundsX = newBounds.x;
            v.BoundsY = newBounds.y;
            v.BoundsW = newBounds.width;
            v.BoundsH = newBounds.height;

            if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
            {
                vc.Stage = next;
                vc.Bounds = newBounds;
                AR.s.Component.SetComponent(v.EntityId, vc);
            }

            // 2. Hamlet 진입 시 벽 빌더 활성화 (이전엔 Town이었음 — 너무 늦어서 앞당김)
            //    Hamlet Bounds(20×20) 기준 외곽 벽 큐잉. 이후 Stage 승격은 Bounds만 확장하고
            //    벽은 그대로 유지 (성벽 안쪽이 마을 코어, 바깥은 농지/외곽 영역으로 자라남).
            if (next == VillageStage.Hamlet && prev < VillageStage.Hamlet)
            {
                AR.s.Component.AddComponent(v.EntityId, new WallPlanRequestTag());
                v.WallPlanRequested = true;
            }

            AR.s.UI.SetNotify($"마을 {v.VillageId} 승격: {prev} → {next}");

            Debug.Log($"[TierProgression] v{v.VillageId} {prev} → {next} (Bounds={newBounds}, Pop={v.Population})");
        }
    }
}
