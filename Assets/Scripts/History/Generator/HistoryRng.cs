namespace ARPG.History
{
    /// <summary>
    /// SplitMix64 기반 결정론 PRNG.
    /// .NET 버전에 의존하지 않으며, 같은 seed에서 같은 시퀀스를 보장한다.
    /// </summary>
    public class HistoryRng
    {
        private ulong _state;

        public HistoryRng(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public HistoryRng(int seed) : this(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL)) { }

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1) 범위의 균일 분포 double.</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>[minInclusive, maxExclusive) 범위 int.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        public ulong CurrentState => _state;
    }
}
