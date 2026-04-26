#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase C: 마을 외곽 벽 계획 시스템.
    /// WallPlanRequestTag가 부착된 마을(Town 도달 시 부여)의 Bounds 외곽선을 추출해
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
            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;

                // 처리 대상: WallPlanRequestTag가 부착된 마을만
                if (AR.s.Component.HasComponent<WallPlanRequestTag>(v.EntityId) == false)
                    continue;

                PlanWallsForVillage(v);

                // 처리 완료 — 태그 제거
                AR.s.Component.RemoveComponent<WallPlanRequestTag>(v.EntityId);
                v.WallPlanRequested = false;
            }
        }

        private static void PlanWallsForVillage(VillageData v)
        {
            if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc) == false)
            {
                Debug.LogWarning($"[WallPlanner] v{v.VillageId} VillageComponent 없음 — 벽 계획 스킵");
                return;
            }

            RectInt b = vc.Bounds;
            if (b.width <= 2 || b.height <= 2)
            {
                Debug.LogWarning($"[WallPlanner] v{v.VillageId} Bounds 너무 작음 ({b}) — 벽 계획 스킵");
                return;
            }

            if (v.WallSegments == null)
                v.WallSegments = new System.Collections.Generic.List<WallSegmentSaveData>();

            // 기존 세그먼트 보존 — 중복 계획 방지 (이미 계획된 마을은 스킵)
            if (v.WallSegments.Count > 0)
            {
                Debug.LogWarning($"[WallPlanner] v{v.VillageId} 이미 {v.WallSegments.Count}개 세그먼트 존재 — 재계획 스킵");
                return;
            }

            int x0 = b.xMin;
            int y0 = b.yMin;
            int x1 = b.xMax - 1;
            int y1 = b.yMax - 1;

            // 게이트 후보: 북쪽 변 중앙, 남쪽 변 중앙
            int gateNorthX = (x0 + x1) / 2;
            int gateSouthX = gateNorthX;
            int gateY_N = y1;
            int gateY_S = y0;

            int segId = 0;
            int gateCount = 0;

            // 외곽 둘레 4변 순회 (Bounds 외곽선)
            // 위쪽 변 (y = y1) — x: x0 → x1
            for (int x = x0; x <= x1; x++)
            {
                bool isCorner = (x == x0 || x == x1);
                bool isGate = (x == gateNorthX);
                AddSegment(v, ref segId, x, y1, ChooseOrient(WallEdge.Top, isCorner, isGate, x, y1, x0, y0, x1, y1), isGate);
                if (isGate) gateCount++;
            }
            // 아래쪽 변 (y = y0) — x: x0 → x1, 코너는 위에서 이미 추가됐으니 스킵
            for (int x = x0; x <= x1; x++)
            {
                if (x == x0 || x == x1) continue;  // 좌상/우상 코너는 위 루프, 좌하/우하 코너는 여기 직접
                // 좌하/우하 코너는 별도 처리 안 됨 — 아래 별도 추가
            }
            // 좌하 / 우하 코너 + 아래쪽 변 직선
            for (int x = x0; x <= x1; x++)
            {
                bool isCorner = (x == x0 || x == x1);
                bool isGate = (x == gateSouthX);
                AddSegment(v, ref segId, x, y0, ChooseOrient(WallEdge.Bottom, isCorner, isGate, x, y0, x0, y0, x1, y1), isGate);
                if (isGate) gateCount++;
            }
            // 좌측 변 (x = x0, 코너 제외)
            for (int y = y0 + 1; y < y1; y++)
            {
                AddSegment(v, ref segId, x0, y, WallOrientation.Vertical, false);
            }
            // 우측 변 (x = x1, 코너 제외)
            for (int y = y0 + 1; y < y1; y++)
            {
                AddSegment(v, ref segId, x1, y, WallOrientation.Vertical, false);
            }

            // VillageComponent 통계 갱신
            vc.WallSegmentCount = v.WallSegments.Count;
            v.WallSegmentCount = vc.WallSegmentCount;
            AR.s.Component.SetComponent(v.EntityId, vc);

            Debug.Log($"[WallPlanner] v{v.VillageId} Town 벽 계획: 외곽 {v.WallSegments.Count}칸, 게이트 {gateCount}개 (Bounds={b})");
        }

        private enum WallEdge { Top, Bottom, Left, Right }

        private static WallOrientation ChooseOrient(
            WallEdge edge, bool isCorner, bool isGate,
            int x, int y, int x0, int y0, int x1, int y1)
        {
            if (isGate) return WallOrientation.Gate;
            if (isCorner)
            {
                if (x == x0 && y == y1) return WallOrientation.CornerNW;
                if (x == x1 && y == y1) return WallOrientation.CornerNE;
                if (x == x0 && y == y0) return WallOrientation.CornerSW;
                if (x == x1 && y == y0) return WallOrientation.CornerSE;
            }
            return (edge == WallEdge.Top || edge == WallEdge.Bottom)
                ? WallOrientation.Horizontal
                : WallOrientation.Vertical;
        }

        private const int PALISADE_HP = 100;

        private static void AddSegment(VillageData v, ref int segId, int x, int y, WallOrientation orient, bool isGate)
        {
            v.WallSegments.Add(new WallSegmentSaveData
            {
                SegmentId = segId++,
                TileX = x,
                TileY = y,
                Type = (int)WallType.Palisade,
                Orient = (int)orient,
                CurrentHp = PALISADE_HP,
                MaxHp = PALISADE_HP,
                IsBuilt = false,
            });
        }
    }
}
