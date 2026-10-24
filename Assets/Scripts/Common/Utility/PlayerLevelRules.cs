#nullable enable
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 플레이어 레벨·경험치·능력치 포인트·판 자리 규칙 (PLAYER_LEVEL_CHARM_DESIGN.md §3·§4·§6.1).
    /// 상태는 PlayerData가 갖고, 여기는 표·식만 계산한다.
    /// </summary>
    public static class PlayerLevelRules
    {
        public const int MIN_LEVEL = 1;
        public const int MAX_LEVEL = 100;
        public const int ATTRIBUTE_POINTS_PER_LEVEL = 5;

        // 플레이어 근처(칸)에서 죽은 몬스터는 누가 잡았든 경험치를 준다 (§3.2 제안 초안)
        public const float NEARBY_KILL_RADIUS = 20f;

        // 되돌리기 비용 (§4.2): 19레벨까지 무료, 20레벨부터 1포인트 = ceil(15G × 플레이어 레벨 구간 판매 배율)
        public const int FREE_RESPEC_MAX_LEVEL = 19;
        public const int RESPEC_BASE_GOLD = 15;

        // 판 자리 9칸이 열리는 레벨. 3×3 왼쪽 위부터 행 순서 — [1][10][20] / [70][80][30] / [60][50][40] (§6.1)
        public const int CHARM_BOARD_SLOT_COUNT = 9;
        private static readonly int[] _charmBoardSlotUnlockLevels = { 1, 10, 20, 70, 80, 30, 60, 50, 40 };

        public static int ClampLevel(int level)
        {
            return Mathf.Clamp(level, MIN_LEVEL, MAX_LEVEL);
        }

        /// <summary>이 레벨 → 다음 레벨 필요 경험치 R(L). 최고 레벨이거나 표가 없으면 0.</summary>
        public static int GetRequiredExp(int level)
        {
            if (level >= MAX_LEVEL)
                return 0;

            ExpTable? row = AR.s.Data.GetExp(ClampLevel(level));
            return row != null ? Mathf.Max(0, row.RequiredExp) : 0;
        }

        /// <summary>몬스터 기본 경험치 E(L). 표 범위 밖 레벨은 1~100으로 보정한다.</summary>
        public static int GetMonsterBaseExp(int monsterLevel)
        {
            ExpTable? row = AR.s.Data.GetExp(ClampLevel(monsterLevel));
            return row != null ? Mathf.Max(0, row.MonsterExp) : 0;
        }

        /// <summary>
        /// 레벨 차이 보정 (§3.2 제안 초안). 안전 범위 = 3 + floor(플레이어 레벨 / 16),
        /// 초과 차이 d = max(|플레이어 − 몬스터| − 안전 범위, 0), 보정 = ((PL + 5) / (PL + 5 + d^2.5))^1.5.
        /// </summary>
        public static float GetLevelDifferenceMultiplier(int playerLevel, int monsterLevel)
        {
            int safeRange = 3 + playerLevel / 16;
            int excess = Mathf.Max(Mathf.Abs(playerLevel - monsterLevel) - safeRange, 0);
            if (excess == 0)
                return 1f;

            float baseValue = playerLevel + 5f;
            return Mathf.Pow(baseValue / (baseValue + Mathf.Pow(excess, 2.5f)), 1.5f);
        }

        /// <summary>받는 경험치 = 기본 경험치(몬스터 레벨) × 등급 배율 × 레벨 차이 보정 (§3.2).</summary>
        public static int GetKillExp(int playerLevel, int monsterLevel, float expMul)
        {
            float exp = GetMonsterBaseExp(monsterLevel) * Mathf.Max(0f, expMul)
                * GetLevelDifferenceMultiplier(ClampLevel(playerLevel), ClampLevel(monsterLevel));
            return Mathf.Max(0, Mathf.RoundToInt(exp));
        }

        /// <summary>이 레벨까지 받은 능력치 포인트 총합 (레벨업마다 5).</summary>
        public static int GetTotalAttributePoints(int level)
        {
            return (ClampLevel(level) - MIN_LEVEL) * ATTRIBUTE_POINTS_PER_LEVEL;
        }

        /// <summary>1포인트 되돌리기 골드. 19레벨까지 0.</summary>
        public static int GetRespecCostPerPoint(int playerLevel)
        {
            if (playerLevel <= FREE_RESPEC_MAX_LEVEL)
                return 0;

            DropBandTable? band = AR.s.Data.GetDropBand(playerLevel);
            int sellPriceMul = band != null ? band.SellPriceMul : ARPG.Village.PlayerSellPriceRules.MUL_DENOMINATOR;
            return Mathf.CeilToInt(RESPEC_BASE_GOLD * sellPriceMul / (float)ARPG.Village.PlayerSellPriceRules.MUL_DENOMINATOR);
        }

        public static int GetCharmBoardSlotUnlockLevel(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _charmBoardSlotUnlockLevels.Length)
                return int.MaxValue;
            return _charmBoardSlotUnlockLevels[slotIndex];
        }

        public static bool IsCharmBoardSlotUnlocked(int slotIndex, int playerLevel)
        {
            return playerLevel >= GetCharmBoardSlotUnlockLevel(slotIndex);
        }

        /// <summary>이 레벨에 도달하는 순간 새로 열리는 판 자리가 있는가 (레벨업 알림용).</summary>
        public static bool OpensCharmBoardSlotAt(int level)
        {
            for (int i = 0; i < _charmBoardSlotUnlockLevels.Length; i++)
            {
                if (_charmBoardSlotUnlockLevels[i] == level)
                    return true;
            }
            return false;
        }
    }
}
