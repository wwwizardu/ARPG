#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase C: 마을 외곽 벽 계획 시스템.
    /// WallPlanRequestTag가 부착된 마을(Hamlet 도달 시 부여)의 데이터 Bounds 외곽선을 추출해
    /// VillageData.WallSegments에 세그먼트 큐를 채운다. 처리 후 태그 자동 제거.
    ///
    /// 실제 배치는 System_VillageBuildQueue가 담당 (다음 틱부터 1칸씩 순차 진행).
    /// </summary>
    public class System_VillageWallPlanner : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 65-69 Construction
        public int Priority => 67;
        public float UpdateInterval => 5.0f;

        // 게이트 위치: 마을 중심 기준 N/S 방위 1개씩 (Phase C 단순화)
        public int Priority_OnReset => 0;

        public void OnCreate() { }
        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            List<VillageData> villages = new List<VillageData>(AR.s.Village.GetAllVillages());
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData v = villages[i];
                if (v.EntityId < 0) continue;

                // 처리 대상: WallPlanRequestTag가 부착된 마을만
                if (AR.s.Component.HasComponent<WallPlanRequestTag>(v.EntityId) == false)
                    continue;

                if (VillageWallRules.PlanWalls(v) == false)
                {
                    Debug.LogWarning($"[WallPlanner] v{v.VillageId} Bounds 너무 작음 "
                        + $"({v.BoundsX},{v.BoundsY},{v.BoundsW},{v.BoundsH}) — 벽 계획 대기");
                    continue;
                }

                if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
                {
                    vc.WallSegmentCount = v.WallSegmentCount;
                    vc.CompletedWallSegments = v.CompletedWallSegments;
                    AR.s.Component.SetComponent(v.EntityId, vc);
                }

                // 처리 완료 — 태그 제거
                AR.s.Component.RemoveComponent<WallPlanRequestTag>(v.EntityId);
                v.WallPlanRequested = false;
                Debug.Log($"[WallPlanner] v{v.VillageId} 벽 계획: 외곽 "
                    + $"{v.WallSegmentCount}칸 (Bounds="
                    + $"{v.BoundsX},{v.BoundsY},{v.BoundsW},{v.BoundsH})");
            }
        }
    }
}
