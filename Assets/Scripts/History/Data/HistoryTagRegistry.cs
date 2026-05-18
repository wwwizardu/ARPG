using System.Collections.Generic;
using UnityEngine;

namespace ARPG.History
{
    /// <summary>
    /// 태그 문자열 ↔ 비트 ID 매핑. ulong × 2 = 최대 128 태그.
    /// 매칭은 비트 연산 O(1).
    /// </summary>
    public static class HistoryTagRegistry
    {
        public const int MAX_TAGS = 128;

        private static readonly Dictionary<string, int> _tagToBit = new();
        private static int _nextBit = 0;

        public static int RegisteredCount => _nextBit;

        public static int GetOrRegister(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return 0;

            if (_tagToBit.TryGetValue(tag, out int bit))
                return bit;

            if (_nextBit >= MAX_TAGS)
            {
                Debug.LogError($"[HistoryTagRegistry] Tag overflow at {MAX_TAGS}. Drop or alias: {tag}");
                return 0;
            }

            _tagToBit[tag] = _nextBit;
            return _nextBit++;
        }

        public static void ToMask(string[] tags, out ulong low, out ulong high)
        {
            low = 0;
            high = 0;
            if (tags == null)
                return;

            for (int i = 0; i < tags.Length; i++)
            {
                int bit = GetOrRegister(tags[i]);
                if (bit < 64)
                    low |= 1UL << bit;
                else
                    high |= 1UL << (bit - 64);
            }
        }

        public static bool MatchAll(ulong actorLow, ulong actorHigh, ulong reqLow, ulong reqHigh)
        {
            return (actorLow & reqLow) == reqLow && (actorHigh & reqHigh) == reqHigh;
        }

        public static bool MatchAny(ulong actorLow, ulong actorHigh, ulong maskLow, ulong maskHigh)
        {
            return (actorLow & maskLow) != 0UL || (actorHigh & maskHigh) != 0UL;
        }

        public static int PopCount(ulong x)
        {
            // SWAR popcount — Unity .NET Standard 2.1에서 System.Numerics.BitOperations가 internal이라 직접 구현.
            x = x - ((x >> 1) & 0x5555555555555555UL);
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        public static void Reset()
        {
            _tagToBit.Clear();
            _nextBit = 0;
        }
    }
}
