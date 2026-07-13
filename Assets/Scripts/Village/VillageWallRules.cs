#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// VillageData.Bounds만으로 벽 계획과 진행 상태를 다루는 데이터 전용 규칙.
    /// 맵 점유와 타일 비주얼 생성은 실체화된 호출자가 담당한다.
    /// </summary>
    public static class VillageWallRules
    {
        public const int PalisadeTableId = 180;
        public const int PalisadeGateTableId = 181;
        public const float PalisadeBuildHours = 0.5f;

        private const int PALISADE_HP = 100;

        public static bool IsWallTable(int tableId)
        {
            return tableId == PalisadeTableId || tableId == PalisadeGateTableId;
        }

        public static bool PlanWalls(VillageData village)
        {
            if (village.WallSegments == null)
                village.WallSegments = new List<WallSegmentSaveData>();
            if (village.WallSegments.Count > 0)
            {
                village.WallSegmentCount = village.WallSegments.Count;
                village.WallPlanRequested = false;
                return true;
            }
            if (village.BoundsW <= 2 || village.BoundsH <= 2)
                return false;

            int x0 = village.BoundsX;
            int y0 = village.BoundsY;
            int x1 = village.BoundsX + village.BoundsW - 1;
            int y1 = village.BoundsY + village.BoundsH - 1;
            int gateX = (x0 + x1) / 2;
            int segmentId = 0;

            for (int x = x0; x <= x1; x++)
            {
                bool isCorner = x == x0 || x == x1;
                bool isGate = x == gateX;
                AddSegment(village, ref segmentId, x, y1,
                    ChooseHorizontalOrientation(isCorner, isGate, x, y1, x0, y0, x1, y1));
            }
            for (int x = x0; x <= x1; x++)
            {
                bool isCorner = x == x0 || x == x1;
                bool isGate = x == gateX;
                AddSegment(village, ref segmentId, x, y0,
                    ChooseHorizontalOrientation(isCorner, isGate, x, y0, x0, y0, x1, y1));
            }
            for (int y = y0 + 1; y < y1; y++)
                AddSegment(village, ref segmentId, x0, y, WallOrientation.Vertical);
            for (int y = y0 + 1; y < y1; y++)
                AddSegment(village, ref segmentId, x1, y, WallOrientation.Vertical);

            village.WallSegmentCount = village.WallSegments.Count;
            village.CompletedWallSegments = WallSegmentRegistry.CountBuilt(village);
            village.WallPlanRequested = false;
            return true;
        }

        public static int GetTableId(WallSegmentSaveData segment)
        {
            return segment.Orient == (int)WallOrientation.Gate
                ? PalisadeGateTableId
                : PalisadeTableId;
        }

        public static bool MarkSegmentBuiltAt(VillageData village, int tileX, int tileY)
        {
            WallSegmentSaveData? segment = FindUnbuiltSegmentAt(village, tileX, tileY);
            if (segment == null)
                return false;
            WallSegmentRegistry.MarkSegmentBuilt(village, segment.SegmentId);
            return true;
        }

        public static bool CanBuildAnotherSegment(VillageData village, float completionRatioLimit)
        {
            if (village.WallSegmentCount <= 0 || completionRatioLimit <= 0f)
                return false;
            if (completionRatioLimit >= 1f)
                return village.CompletedWallSegments < village.WallSegmentCount;

            int limit = (int)(village.WallSegmentCount * completionRatioLimit);
            return village.CompletedWallSegments < limit;
        }

        public static float GetBootstrapCompletionRatio(VillageStage stage, float maturity,
            float seedFactor)
        {
            float min;
            float max;
            if (stage <= VillageStage.Settlement)
            {
                min = 0f;
                max = 0f;
            }
            else if (stage == VillageStage.Hamlet)
            {
                min = 0f;
                max = 0.2f;
            }
            else if (stage == VillageStage.Village)
            {
                min = 0.2f;
                max = 0.6f;
            }
            else
            {
                min = 0.6f;
                max = 1f;
            }

            float shaped = Mathf.Clamp01(maturity * 0.85f + seedFactor * 0.15f);
            return Mathf.Lerp(min, max, shaped);
        }

        private static WallSegmentSaveData? FindUnbuiltSegmentAt(VillageData village,
            int tileX, int tileY)
        {
            for (int i = 0; i < village.WallSegments.Count; i++)
            {
                WallSegmentSaveData segment = village.WallSegments[i];
                if (segment.IsBuilt == false
                    && segment.TileX == tileX
                    && segment.TileY == tileY)
                    return segment;
            }
            return null;
        }

        private static WallOrientation ChooseHorizontalOrientation(bool isCorner, bool isGate,
            int x, int y, int x0, int y0, int x1, int y1)
        {
            if (isGate)
                return WallOrientation.Gate;
            if (isCorner)
            {
                if (x == x0 && y == y1)
                    return WallOrientation.CornerNW;
                if (x == x1 && y == y1)
                    return WallOrientation.CornerNE;
                if (x == x0 && y == y0)
                    return WallOrientation.CornerSW;
                if (x == x1 && y == y0)
                    return WallOrientation.CornerSE;
            }
            return WallOrientation.Horizontal;
        }

        private static void AddSegment(VillageData village, ref int segmentId, int x, int y,
            WallOrientation orientation)
        {
            village.WallSegments.Add(new WallSegmentSaveData
            {
                SegmentId = segmentId++,
                TileX = x,
                TileY = y,
                Type = (int)WallType.Palisade,
                Orient = (int)orientation,
                CurrentHp = PALISADE_HP,
                MaxHp = PALISADE_HP,
                IsBuilt = false,
            });
        }
    }
}
