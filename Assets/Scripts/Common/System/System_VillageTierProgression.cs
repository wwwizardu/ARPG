#nullable enable
using System.Collections.Generic;
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
    public class System_VillageTierProgression : IFixedUpdateSystem, IWorldMapSystem
    {
        // 도메인 대역 (CLAUDE.md): 60-64 Lifecycle
        public int Priority => 60;
        public float UpdateInterval => 5.0f;     // 실시간 5s 호출, 게임시간 4h 게이트 내장

        private const float CHECK_INTERVAL_HOURS = 4f;
        private float _lastCheckGameTime = -1f;

        // Phase D: TableId 하드코딩 제거. 승격 조건은 ProvidedService 비트 + HasObjectSet 사용.

        public void OnCreate()
        {
            _lastCheckGameTime = AR.s.Time.CurrentGameTime;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // V2 (VILLAGE_REWORK_PLAN §3.4): 초기 Stage는 생성 시 확정되고 런타임 승격은
            // 후속 발전 Phase(V8) 전까지 실행하지 않는다. 조건 평가·적용 코드는 보존.
            if (VillageProgressionRules.RuntimeProgressionEnabled == false) return;

            float now = AR.s.Time.CurrentGameTime;
            if (now - _lastCheckGameTime < CHECK_INTERVAL_HOURS) return;
            _lastCheckGameTime = now;

            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData v = villages[i];
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
            if (v.Stage >= VillageStage.City) return v.Stage;

            VillageStage next = v.Stage + 1;
            if (CanPromoteTo(v, next, now))
                return next;
            return v.Stage;
        }

        /// <summary>
        /// VillageStageTable의 진입 게이트(Pop/Housing/Food/Age/RequiredSet/RequiredCivic/RequiredShop)를
        /// 모두 통과하면 true. PromoMinPopulation &lt; 0이면 진입 불가 (City 등 미구현 단계).
        /// 빌드 점수 게이트(TierGapDetector)와 동일 헬퍼를 공유해 이중 진실 소스 제거.
        /// </summary>
        public static bool CanPromoteTo(VillageData v, VillageStage next, float now)
        {
            Tables.VillageStageTable? t = AR.s.Data.GetVillageStage(next);
            if (t == null)
            {
                Debug.LogError($"[VillageTierProgression] VillageStageTable not loaded — stage={next}");
                return false;
            }
            return VillageProgressionRules.CanPromoteTo(v, t, now);
        }

        // ========== 승격 적용 ==========

        private static void Promote(VillageData v, VillageStage next)
        {
            VillageStage prev = v.Stage;
            int radius = VillageManager.GetBoundsRadius(next);
            bool requestWallPlan = VillageProgressionRules.ApplyPromotion(v, next, radius);
            RectInt newBounds = new RectInt(v.BoundsX, v.BoundsY, v.BoundsW, v.BoundsH);

            if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
            {
                vc.Stage = next;
                vc.Bounds = newBounds;
                AR.s.Component.SetComponent(v.EntityId, vc);
            }

            // 2. Hamlet 진입 시 벽 빌더 활성화 (이전엔 Town이었음 — 너무 늦어서 앞당김)
            //    Hamlet Bounds(20×20) 기준 외곽 벽 큐잉. 이후 Stage 승격은 Bounds만 확장하고
            //    벽은 그대로 유지 (성벽 안쪽이 마을 코어, 바깥은 농지/외곽 영역으로 자라남).
            if (requestWallPlan)
            {
                AR.s.Component.AddComponent(v.EntityId, new WallPlanRequestTag());
            }

            // 경계 확장으로 이전 PlacementPending 건물이 들어갈 자리가 생겼을 수 있다.
            AR.s.Village.RetryPendingPlacements(v.VillageId);

            AR.s.UI.SetNotify($"마을 {v.VillageId} 승격: {prev} → {next}");

            // R3: Live 연대기 — 승격 사건 (연대기에 연결된 마을만)
            if (v.SettlementId >= 0)
                AR.s.History.AppendLiveEvent(ARPG.History.ChronicleEventType.Promotion, v.SettlementId);

            Debug.Log($"[TierProgression] v{v.VillageId} {prev} → {next} (Bounds={newBounds}, Pop={v.Population})");
        }
    }
}
