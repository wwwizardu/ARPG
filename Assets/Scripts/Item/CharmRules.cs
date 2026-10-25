#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Item
{
    /// <summary>
    /// 참 판·참 규칙 (PLAYER_LEVEL_CHARM_DESIGN.md §6). 모양·회전·넣기 판정, 막힌 칸·모양 비중, 참 등급·모양별 옵션 풀, 참 값 표의 모양 칸 읽기.
    /// 가격은 CharmPriceRules가 맡는다. 상태 없는 계산이다. 판과 참의 상태는 ItemData(CharmBoardData·CharmData)가, 판 자리는 PlayerData가 갖는다.
    /// 칸 번호는 판 왼쪽 위부터 행 순서 0~8 (번호 = 행 × 3 + 열).
    /// </summary>
    public static class CharmRules
    {
        public const int BOARD_SIZE = 3;
        public const int CELL_COUNT = BOARD_SIZE * BOARD_SIZE;
        public const int ROTATION_COUNT = 4;

        // 아이템 표 행 (§7): 판 1행, 참은 모양 순서대로 5행
        public const int BOARD_ITEM_ID = 8000;
        public const int CHARM_ITEM_ID_FIRST = 8001;
        public const int SHAPE_COUNT = 5;

        // 판 막힌 칸 수 비중 0·1·2·3칸 (§6.2). 아이템 레벨과 무관
        private static readonly int[] _blockedCountWeights = { 10, 35, 35, 20 };

        // 참 모양 드롭 비중 (§6.3) — CharmShape 순서
        private static readonly int[] _shapeWeights = { 30, 25, 12, 12, 15 };

        // 모양 기본 방향의 칸 (행, 열)
        private static readonly Vector2Int[][] _shapeCells =
        {
            new[] { new Vector2Int(0, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1) },
        };

        // 모양 크기 묶음 (§6.3): 큰 묶음일수록 붙을 수 있는 옵션이 많다
        public const int SIZE_SMALL = 0;    // 1×1·1×2
        public const int SIZE_MEDIUM = 1;   // 1×3·L자
        public const int SIZE_LARGE = 2;    // 2×2
        private static readonly int[] _shapeSizeClass = { SIZE_SMALL, SIZE_SMALL, SIZE_MEDIUM, SIZE_MEDIUM, SIZE_LARGE };

        // 등급 (§6.3): 일반 0개 / 마법 1~2개 (접두·접미 각 1까지) / 희귀 3~4개 (접두·접미 각 2까지)
        public const int MAGIC_OPTION_MIN = 1;
        public const int MAGIC_OPTION_MAX = 2;
        public const int RARE_OPTION_MIN = 3;
        public const int RARE_OPTION_MAX = 4;
        public const int AFFIX_LIMIT = 2;

        // 등급 확률 (%): 일반 10 · 마법 85 · 희귀 5. 아이템 레벨과 무관하다
        public const int MAGIC_RATE = 85;
        public const int RARE_RATE = 5;
        public const int GRADE_ROLL_SCALE = 10000;

        // 참 전용 Mod (§6.3·§7). 시트 AllowedEquipTypes가 비어 있어 장비 롤·마법부여 후보에 들어가지 않는다
        public const int MOD_STR = 1300;
        public const int MOD_DEX = 1301;
        public const int MOD_INT = 1302;
        public const int MOD_EVASION = 1303;
        public const int MOD_CRIT_RATE = 1304;
        public const int MOD_CRIT_DAMAGE = 1305;

        // 옵션 풀 (§6.3): 접두·접미는 Mod 표 Slot 칸이 정한다. 크기 묶음이 그 값 이상인 모양에만 붙는다
        private static readonly int[] _optionModIds =
        {
            1203, 1204, 1205, 1206,         // 화염·냉기·번개·독 저항 (접미)
            1201, 1202,                     // 체력·마나 재생 (접두)
            1200,                           // 최대 마나 (접두)
            1209,                           // 정확도 (접미)
            MOD_EVASION,                    // 회피 (접두)
            MOD_CRIT_RATE, MOD_CRIT_DAMAGE, // 치명타 확률 증가·치명타 피해 증가 (접미)
            1000,                           // 최대 체력 (접두)
            MOD_STR, MOD_DEX, MOD_INT,      // 힘·민첩·지능 (접미)
        };
        private static readonly int[] _optionWeights = { 50, 50, 50, 50, 100, 100, 100, 100, 100, 50, 50, 100, 100, 100, 100 };
        private static readonly int[] _optionMinSizeClass =
        {
            SIZE_SMALL, SIZE_SMALL, SIZE_SMALL, SIZE_SMALL, SIZE_SMALL, SIZE_SMALL,
            SIZE_MEDIUM, SIZE_MEDIUM, SIZE_MEDIUM, SIZE_MEDIUM, SIZE_MEDIUM,
            SIZE_LARGE, SIZE_LARGE, SIZE_LARGE, SIZE_LARGE,
        };

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

        /// <summary>지금 만들 수 있는 모양인가. 옛 세이브 읽기용 Square3은 아니다.</summary>
        public static bool IsValidShape(GE.CharmShape shape)
        {
            return (int)shape >= 0 && (int)shape < SHAPE_COUNT;
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
            return IsValidShape(shape) ? _shapeCells[(int)shape].Length : 0;
        }

        public static int GetSizeClass(GE.CharmShape shape)
        {
            return IsValidShape(shape) ? _shapeSizeClass[(int)shape] : SIZE_SMALL;
        }

        /// <summary>옵션 풀의 optionIndex번 옵션이 이 모양에 붙을 수 있는가 (크기 묶음 조건).</summary>
        public static bool IsOptionAllowed(int optionIndex, GE.CharmShape shape)
        {
            return _optionMinSizeClass[optionIndex] <= GetSizeClass(shape);
        }

        /// <summary>Mod Id가 이 모양의 옵션 풀에 있는가.</summary>
        public static bool IsOptionAllowedForShape(int modId, GE.CharmShape shape)
        {
            int index = System.Array.IndexOf(_optionModIds, modId);
            return index >= 0 && IsOptionAllowed(index, shape);
        }

        public static int GetOptionMinSizeClass(int optionIndex) => _optionMinSizeClass[optionIndex];

        /// <summary>참에만 붙는 Mod인가 (시트 AllowedEquipTypes 빈칸 허용 대상).</summary>
        public static bool IsCharmOnlyMod(int modId)
        {
            return modId >= MOD_STR && modId <= MOD_CRIT_DAMAGE;
        }

        /// <summary>
        /// 등급 판정. 희귀 → 마법 → 일반 순서로 확인한다 — 장비 등급 판정(DropBonusResolver.ResolveGrade)과 같은 식이다.
        /// 희귀 확인 = min(1, 5% × M), 마법 확인 = min(1, 85 ÷ 95 × M). roll은 [0, GRADE_ROLL_SCALE).
        /// M은 아이템 희귀도 배율이다 — 플레이어 드롭만 1보다 크고 NPC 전리품·상점은 1이다.
        /// </summary>
        public static GE.EquipmentGrade ResolveGrade(float rarityMultiplier, int rareRoll, int magicRoll)
        {
            if (rareRoll < Mathf.RoundToInt(GetRareChance(rarityMultiplier) * GRADE_ROLL_SCALE))
                return GE.EquipmentGrade.Rare;
            if (magicRoll < Mathf.RoundToInt(GetMagicCheckChance(rarityMultiplier) * GRADE_ROLL_SCALE))
                return GE.EquipmentGrade.Magic;
            return GE.EquipmentGrade.Normal;
        }

        public static float GetRareChance(float rarityMultiplier)
        {
            return Mathf.Clamp01(RARE_RATE / 100f * rarityMultiplier);
        }

        /// <summary>희귀가 아닐 때의 마법 확인 확률 (전체 확률이 아니다).</summary>
        public static float GetMagicCheckChance(float rarityMultiplier)
        {
            return Mathf.Clamp01((float)MAGIC_RATE / (100 - RARE_RATE) * rarityMultiplier);
        }

        /// <summary>
        /// 참 옵션 값 범위 (§6.3): 참 값 표에서 그 모양 칸의 최소·최대를 그대로 읽는다(1×3·L자는 3칸 칸).
        /// 칸이 비어 있으면(0) 그 모양에 붙지 않는 옵션이라 false.
        /// </summary>
        public static bool TryGetOptionValueRange(CharmModTierTable row, GE.CharmShape shape, out int min, out int max)
        {
            switch (shape)
            {
                case GE.CharmShape.Single:
                    min = row.Min1x1;
                    max = row.Max1x1;
                    break;
                case GE.CharmShape.Line2:
                    min = row.Min1x2;
                    max = row.Max1x2;
                    break;
                case GE.CharmShape.Line3:
                case GE.CharmShape.LShape:
                    min = row.Min3Cell;
                    max = row.Max3Cell;
                    break;
                case GE.CharmShape.Square2:
                    min = row.Min2x2;
                    max = row.Max2x2;
                    break;
                default:
                    min = 0;
                    max = 0;
                    break;
            }
            if (min <= 0 || max < min)
            {
                min = 0;
                max = 0;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 모양·회전·기준 칸으로 차지하는 칸 번호를 구한다. 판 경계를 넘으면 false (§6.4: 참은 한 판 안에만).
        /// 회전은 시계 방향 90도씩이고, 회전 뒤 모양 묶음의 왼쪽 위가 기준 칸이다.
        /// </summary>
        public static bool TryGetCells(GE.CharmShape shape, int rotation, int anchorCell, List<int> outCells)
        {
            outCells.Clear();
            if (anchorCell < 0 || anchorCell >= CELL_COUNT || IsValidShape(shape) == false)
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
