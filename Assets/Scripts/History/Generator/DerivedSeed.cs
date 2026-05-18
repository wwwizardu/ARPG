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

        public static ulong Derive(int worldSeed, ulong channel)
        {
            ulong x = unchecked((ulong)worldSeed * 0x9E3779B97F4A7C15UL) ^ channel;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
