#nullable enable
using System;
using System.Collections.Generic;

namespace ARPG.Npc
{
    /// <summary>
    /// 맵 전체 모험가 총량의 영속 상태.
    /// 마을 경제 인구와 분리되며, 사망 뒤 점진 보충의 기준으로만 사용한다.
    /// </summary>
    [Serializable]
    public class NpcPopulationState
    {
        public const int SmallMapTarget = 100;
        public const int LegacyLargeMapTarget = 400;
        public const int LargeMapTarget = 1200;
        public const int TargetNpcsPerVillage = 8;
        public const int MinimumNpcsPerVillage = 5;

        public int TargetNpcCount = -1;
        public float NextReplenishGameTime;
        public int ReplenishVillageCursor;
        // 목표 수 상향 마이그레이션 직후 기존 마을의 부족분을 한 번에 재배분한다.
        public bool NeedsImmediateTargetTopUp;
    }

    /// <summary>
    /// 마을별 최소 물리 NPC를 먼저 보장하고, 남은 인원을 역사 인구 가중치로 배분한다.
    /// 저장·스폰 상태를 소유하지 않는 결정론적 순수 계산기다.
    /// </summary>
    public static class NpcPopulationAllocator
    {
        public static int[] AllocateAdditionalCounts(IReadOnlyList<int> currentCounts,
            IReadOnlyList<int> weights, int totalToAllocate, int minimumPerVillage)
        {
            if (currentCounts == null)
                throw new ArgumentNullException(nameof(currentCounts));
            if (weights == null)
                throw new ArgumentNullException(nameof(weights));
            if (currentCounts.Count != weights.Count)
                throw new ArgumentException("Current count and weight lengths must match.");

            int villageCount = currentCounts.Count;
            int[] allocations = new int[villageCount];
            if (villageCount == 0 || totalToAllocate <= 0)
                return allocations;

            int remaining = totalToAllocate;
            int minimum = Math.Max(0, minimumPerVillage);
            for (int level = 1; level <= minimum && remaining > 0; level++)
            {
                for (int i = 0; i < villageCount && remaining > 0; i++)
                {
                    int current = Math.Max(0, currentCounts[i]);
                    if (current + allocations[i] >= level)
                        continue;

                    allocations[i]++;
                    remaining--;
                }
            }

            if (remaining <= 0)
                return allocations;

            DistributeByWeight(allocations, weights, remaining);
            return allocations;
        }

        private static void DistributeByWeight(int[] allocations,
            IReadOnlyList<int> weights, int totalToDistribute)
        {
            long totalWeight = 0;
            for (int i = 0; i < weights.Count; i++)
                totalWeight += Math.Max(0, weights[i]);

            bool useEqualWeights = totalWeight <= 0;
            if (useEqualWeights)
                totalWeight = weights.Count;

            int assigned = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int weight = useEqualWeights ? 1 : Math.Max(0, weights[i]);
                int share = (int)((long)totalToDistribute * weight / totalWeight);
                allocations[i] += share;
                assigned += share;
            }

            bool[] remainderAwarded = new bool[weights.Count];
            for (int remaining = totalToDistribute - assigned; remaining > 0; remaining--)
            {
                int bestIndex = -1;
                long bestRemainder = -1;
                for (int i = 0; i < weights.Count; i++)
                {
                    if (remainderAwarded[i])
                        continue;

                    int weight = useEqualWeights ? 1 : Math.Max(0, weights[i]);
                    long remainder = (long)totalToDistribute * weight % totalWeight;
                    if (remainder <= bestRemainder)
                        continue;

                    bestIndex = i;
                    bestRemainder = remainder;
                }

                if (bestIndex < 0)
                    break;
                allocations[bestIndex]++;
                remainderAwarded[bestIndex] = true;
            }
        }
    }
}
