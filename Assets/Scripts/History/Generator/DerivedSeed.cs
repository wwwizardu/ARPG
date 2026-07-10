namespace ARPG.History
{
    /// <summary>
    /// worldSeed를 채널별 namespace로 분리. 같은 worldSeed라도 채널마다 다른 시퀀스를 받도록
    /// 보장하여 채널 간 상관관계를 제거한다 (slotPick 결과가 yearStep에 영향 X).
    /// </summary>
    public static class DerivedSeed
    {
        public const ulong ChannelSlotPick     = 0x53_4C_4F_54_50_49_43_4BUL; // "SLOTPICK"
        public const ulong ChannelYearStep     = 0x59_45_41_52_53_54_45_50UL; // "YEARSTEP"
        public const ulong ChannelCanTrigger   = 0x43_41_4E_54_52_49_47_47UL; // "CANTRIGG"
        public const ulong ChannelCoord        = 0x43_4F_4F_52_44_5F_5F_5FUL; // "COORD___"
        public const ulong ChannelRegion       = 0x52_45_47_49_4F_4E_5F_5FUL; // "REGION__"
        public const ulong ChannelVariant      = 0x56_41_52_49_41_4E_54_5FUL; // "VARIANT_"

        // MAP_DESIGN.md §8.3 — per-tile biome 결정용
        public const ulong ChannelWarpX        = 0x57_41_52_50_5F_58_5F_5FUL; // "WARP_X__"
        public const ulong ChannelWarpY        = 0x57_41_52_50_5F_59_5F_5FUL; // "WARP_Y__"
        public const ulong ChannelTemperature  = 0x54_45_4D_50_45_52_41_54UL; // "TEMPERAT"
        public const ulong ChannelHumidity     = 0x48_55_4D_49_44_49_54_59UL; // "HUMIDITY"
        public const ulong ChannelElevation    = 0x45_4C_45_56_41_54_49_4FUL; // "ELEVATIO"

        // MAP_DESIGN.md §7 Race Settlement 배치용
        public const ulong ChannelSettlement   = 0x53_45_54_54_4C_45_4D_4EUL; // "SETTLEMN"

        // MAP_DESIGN.md §6 LoreFragment 배치용
        public const ulong ChannelLoreCount    = 0x4C_4F_52_45_43_4E_54_5FUL; // "LORECNT_"
        public const ulong ChannelLoreProp     = 0x4C_4F_52_45_50_52_4F_50UL; // "LOREPROP"

        // WORLD_HISTORY_PLAN.md R0-B — 연대기 시뮬(L1) / NPC 관계 backfill(L2)용
        public const ulong ChannelChronicle    = 0x43_48_52_4F_4E_49_43_4CUL; // "CHRONICL"
        public const ulong ChannelRelationBackfill = 0x52_45_4C_42_4B_46_49_4CUL; // "RELBKFIL"
        public const ulong ChannelNpcActivity = 0x4E_50_43_41_43_54_49_56UL; // "NPCACTIV"

        public static ulong Derive(int worldSeed, ulong channel)
        {
            ulong x = unchecked((ulong)worldSeed * 0x9E3779B97F4A7C15UL) ^ channel;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
