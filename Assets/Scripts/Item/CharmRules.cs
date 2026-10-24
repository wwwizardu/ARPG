#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Item
{
    /// <summary>
    /// 참 판·참 규칙 (PLAYER_LEVEL_CHARM_DESIGN.md §6). 모양·회전·넣기 판정, 막힌 칸·모양·옵션 비중, 가격 배율.
    /// 가격은 CharmPriceRules가 맡는다. 상태 없는 계산이다. 판과 참의 상태는 ItemData(CharmBoardData·CharmData)가, 판 자리는 PlayerData가 갖는다.
    /// 칸 번호는 판 왼쪽 위부터 행 순서 0~8 (번호 = 행 × 3 + 열).
    /// </summary>
    public static class CharmRules
    {
        public const int BOARD_SIZE = 3;
        public const int CELL_COUNT = BOARD_SIZE * BOARD_SIZE;
        public const int ROTATION_COUNT = 4;

        // 아이템 표 행 (§7): 판 1행, 참은 모양 순서대로 6행
        public const int BOARD_ITEM_ID = 8000;
        public const int CHARM_ITEM_ID_FIRST = 8001;
        public const int SHAPE_COUNT = 6;

        // 판 막힌 칸 수 비중 0·1·2·3칸 (§6.2). 아이템 레벨과 무관
        private static readonly int[] _blockedCountWeights = { 10, 35, 35, 20 };

        // 참 모양 드롭 비중 (§6.3) — CharmShape 순서
        private static readonly int[] _shapeWeights = { 30, 25, 12, 12, 15, 6 };

        // 모양 기본 방향의 칸 (행, 열)
        private static readonly Vector2Int[][] _shapeCells =
        {
            new[] { new Vector2Int(0, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1) },
            new[]
            {
                new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2),
                new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, 2),
                new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(2, 2),
            },
        };

        // 모양별 옵션 수 후보 (반반) — 두 값이 같으면 고정 (§6.3)
        private static readonly int[] _optionCountMin = { 1, 1, 2, 2, 2, 3 };
        private static readonly int[] _optionCountMax = { 1, 2, 2, 2, 3, 4 };

        // 옵션 풀 (§6.3): 힘·민첩·지능·회피는 참 전용 Mod, 나머지는 장비 Mod를 같이 쓴다
        public const int MOD_STR = 1300;
        public const int MOD_DEX = 1301;
        public const int MOD_INT = 1302;
        public const int MOD_EVASION = 1303;
        private static readonly int[] _optionModIds =
        {
            MOD_STR, MOD_DEX, MOD_INT,
            1000,   // 최대 체력
            1200,   // 최대 마나
            1201,   // 체력 재생
            1202,   // 마나 재생
            1209,   // 정확도
            MOD_EVASION,
            1203, 1204, 1205, 1206, // 화염·냉기·번개·독 저항
        };
        private static readonly int[] _optionWeights = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 50, 50, 50, 50 };

        // 참은 일반 판정 T1~T10만 쓴다 (T11·T12 특수 판정 없음)
        public const int MAX_CHARM_TIER = 10;

        public static IReadOnlyList<int> OptionModIds => _optionModIds;
        public static IReadOnlyList<int> OptionWeights => _optionWeights;
        public static IReadOnlyList<int> BlockedCountWeights => _blockedCountWeights;
        public static IReadOnlyList<int> ShapeWeights => _shapeWeights;

        public static bool IsCharmItemId(int itemId)
        {
            return itemId >= CHARM_ITEM_ID_FIRST && itemId < CHARM_ITEM_ID_FIRST + SHAPE_COUNT;
        }

        public static int GetCharmItemId(GE.CharmShape shape)
        {
            return CHARM_ITEM_ID_FIRST + (int)shape;
        }

        public static GE.CharmShape GetShapeOfItemId(int itemId)
        {
            return IsCharmItemId(itemId) ? (GE.CharmShape)(itemId - CHARM_ITEM_ID_FIRST) : GE.CharmShape.Single;
        }

        public static int GetCellCount(GE.CharmShape shape)
        {
            return _shapeCells[(int)shape].Length;
        }

        public static int GetOptionCountMin(GE.CharmShape shape) => _optionCountMin[(int)shape];
        public static int GetOptionCountMax(GE.CharmShape shape) => _optionCountMax[(int)shape];

        /// <summary>
        /// 옵션 1개 값 = 같은 티어 장비 기준값 × 이 비율(%) (§6.3).
        /// 비율 = 칸당 8% × 칸 수 × 크기 보너스(1~2칸 ×1.0, 3칸 ×1.1, 4칸 ×1.2, 9칸 ×1.4) ÷ 옵션 수, 문서 표 값으로 반올림.
        /// </summary>
        public static int GetOptionRatioPercent(GE.CharmShape shape, int optionCount)
        {
            switch (shape)
            {
                case GE.CharmShape.Single: return 8;
                case GE.CharmShape.Line2: return optionCount <= 1 ? 16 : 8;
                case GE.CharmShape.Line3:
                case GE.CharmShape.LShape: return 13;
                case GE.CharmShape.Square2: return optionCount <= 2 ? 19 : 13;
                case GE.CharmShape.Square3: return optionCount <= 3 ? 34 : 25;
                default: return 8;
            }
        }

        /// <summary>참 옵션 값 범위: 장비 티어 최솟값·최댓값에 비율을 곱해 각각 반올림, 최소 1 (§6.3).</summary>
        public static void GetOptionValueRange(ModTierTable tier, int ratioPercent, out int min, out int max)
        {
            min = Mathf.Max(1, Mathf.RoundToInt(tier.Min1 * ratioPercent / 100f));
            max = Mathf.Max(min, Mathf.RoundToInt(tier.Max1 * ratioPercent / 100f));
        }

        /// <summary>
        /// 모양·회전·기준 칸으로 차지하는 칸 번호를 구한다. 판 경계를 넘으면 false (§6.4: 참은 한 판 안에만).
        /// 회전은 시계 방향 90도씩이고, 회전 뒤 모양 묶음의 왼쪽 위가 기준 칸이다.
        /// </summary>
        public static bool TryGetCells(GE.CharmShape shape, int rotation, int anchorCell, List<int> outCells)
        {
            outCells.Clear();
            if (anchorCell < 0 || anchorCell >= CELL_COUNT)
                return false;

            Vector2Int[] cells = _shapeCells[(int)shape];
            int turns = ((rotation % ROTATION_COUNT) + ROTATION_COUNT) % ROTATION_COUNT;
            int minRow = int.MaxValue;
            int minCol = int.MaxValue;
            for (int i = 0; i < cells.Length; i++)
            {
                Vector2Int rotated = Rotate(cells[i], turns);
                minRow = Mathf.Min(minRow, rotated.x);
                minCol = Mathf.Min(minCol, rotated.y);
            }

            int anchorRow = anchorCell / BOARD_SIZE;
            int anchorCol = anchorCell % BOARD_SIZE;
            for (int i = 0; i < cells.Length; i++)
            {
                Vector2Int rotated = Rotate(cells[i], turns);
                int row = anchorRow + rotated.x - minRow;
                int col = anchorCol + rotated.y - minCol;
                if (row < 0 || row >= BOARD_SIZE || col < 0 || col >= BOARD_SIZE)
                {
                    outCells.Clear();
                    return false;
                }
                outCells.Add(row * BOARD_SIZE + col);
            }
            return true;
        }

        /// <summary>박힌 참이 차지한 칸 비트. ignoreIndex번 참은 빼고 센다(옮기는 중인 참).</summary>
        public static int GetOccupiedMask(CharmBoardData board, int ignoreIndex, List<int> scratch)
        {
            int mask = 0;
            for (int i = 0; i < board.Charms.Count; i++)
            {
                if (i == ignoreIndex)
                    continue;
                CharmPlacement placement = board.Charms[i];
                if (placement.Charm?.Charm == null)
                    continue;
                if (TryGetCells(placement.Charm.Charm.Shape, placement.Rotation, placement.AnchorCell, scratch) == false)
                    continue;
                for (int c = 0; c < scratch.Count; c++)
                    mask |= 1 << scratch[c];
            }
            return mask;
        }

        /// <summary>넣을 수 있는가 — 판 경계 안, 막힌 칸·다른 참과 겹치지 않음 (§6.4).</summary>
        public static bool CanPlace(CharmBoardData board, GE.CharmShape shape, int rotation, int anchorCell,
            int ignoreIndex, List<int> scratch)
        {
            int occupied = GetOccupiedMask(board, ignoreIndex, scratch) | board.BlockedMask;
            if (TryGetCells(shape, rotation, anchorCell, scratch) == false)
                return false;
            for (int i = 0; i < scratch.Count; i++)
            {
                if ((occupied & (1 << scratch[i])) != 0)
                    return false;
            }
            return true;
        }

        /// <summary>칸 번호가 어느 박힌 참에 속하는지. 없으면 -1.</summary>
        public static int FindPlacementAtCell(CharmBoardData board, int cell, List<int> scratch)
        {
            for (int i = 0; i < board.Charms.Count; i++)
            {
                CharmPlacement placement = board.Charms[i];
                if (placement.Charm?.Charm == null)
                    continue;
                if (TryGetCells(placement.Charm.Charm.Shape, placement.Rotation, placement.AnchorCell, scratch) == false)
                    continue;
                if (scratch.Contains(cell) == true)
                    return i;
            }
            return -1;
        }

        /// <summary>가중치 목록에서 roll(0 ≤ roll &lt; 합계)에 해당하는 칸 번호.</summary>
        public static int SelectWeighted(IReadOnlyList<int> weights, int roll)
        {
            int cumulative = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                    return i;
            }
            return weights.Count - 1;
        }

        public static int SumWeights(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++)
                total += weights[i];
            return total;
        }

        private static Vector2Int Rotate(Vector2Int cell, int turns)
        {
            int row = cell.x;
            int col = cell.y;
            for (int i = 0; i < turns; i++)
            {
                int nextRow = col;
                int nextCol = -row;
                row = nextRow;
                col = nextCol;
            }
            return new Vector2Int(row, col);
        }
    }
}
