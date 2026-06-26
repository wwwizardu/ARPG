namespace ChronicleSim;

/// <summary>
/// Unity 측 ARPG.History.DerivedSeed와 byte 단위로 동일한 SplitMix64 믹서.
/// worldSeed를 채널별로 분리해 결정론 시퀀스를 보장한다.
/// (실제 시뮬 R1 구현 시 이 로직이 SettlementChronicleSimulator로 그대로 옮겨감)
/// </summary>
public static class DerivedSeed
{
    // 실제 코드에 추가될 신규 채널 (설계/계획 R0-B). 8글자 ASCII = "CHRONICL"
    public const ulong ChannelChronicle = 0x43_48_52_4F_4E_49_43_4CUL;

    // L2 NPC 관계 backfill PRNG. "RELBKFIL"
    public const ulong ChannelRelationBackfill = 0x52_45_4C_42_4B_46_49_4CUL;

    public static ulong Derive(int worldSeed, ulong channel)
    {
        ulong x = unchecked((ulong)worldSeed * 0x9E3779B97F4A7C15UL) ^ channel;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}

/// <summary>
/// 시뮬 틱 루프용 결정론 PRNG. DerivedSeed.Derive(mapSeed, ChannelChronicle)로 시드.
/// 정착지/페어 순회 순서가 결정론이면 같은 시드 → 같은 역사.
/// </summary>
public sealed class SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>[0,1) double</summary>
    public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>[0,n) int</summary>
    public int NextInt(int n) => n <= 0 ? 0 : (int)(Next() % (ulong)n);

    /// <summary>[minInclusive, maxInclusive]</summary>
    public int Range(int minInclusive, int maxInclusive)
        => maxInclusive <= minInclusive ? minInclusive : minInclusive + NextInt(maxInclusive - minInclusive + 1);

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>[-1,1] 근사 노이즈</summary>
    public double Noise() => NextDouble() * 2.0 - 1.0;
}
