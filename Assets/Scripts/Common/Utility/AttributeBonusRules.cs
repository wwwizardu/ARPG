namespace ARPG.Utility
{
    /// <summary>
    /// 힘·민첩·지능 → 최대 HP·회피·최대 마나 고정값 보너스 (PLAYER_LEVEL_CHARM_DESIGN.md §4.1, LV6·LV10).
    /// 플레이어·NPC·몬스터가 같은 계산을 거친다(몬스터는 능력치가 0이라 보너스가 없다).
    /// 보너스는 최종 능력치로 계산해 가산 항으로 넣으므로 "최대 HP 증가 %"가 힘에서 온 HP에도 붙는다.
    /// </summary>
    public static class AttributeBonusRules
    {
        public const int HP_PER_STR = 2;
        public const int EVASION_PER_DEX = 2;
        public const int MP_PER_INT = 1;

        public static int GetBonusHp(int str) => str > 0 ? str * HP_PER_STR : 0;
        public static int GetBonusEvasion(int dex) => dex > 0 ? dex * EVASION_PER_DEX : 0;
        public static int GetBonusMp(int intelligence) => intelligence > 0 ? intelligence * MP_PER_INT : 0;
    }
}
