#nullable enable
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    public struct DropPoolEntry
    {
        public int ItemId;
        public int Weight;
        public int Tier;
    }

    /// <summary>
    /// 몬스터 레벨 기반 드롭 풀 빌드 + 캐싱 + 가중치 랜덤 선택
    /// </summary>
    public static class DropPoolBuilder
    {
        private static readonly Dictionary<int, List<DropPoolEntry>> _currencyPoolCache = new();
        private static readonly Dictionary<int, List<DropPoolEntry>> _equipmentPoolCache = new();

        /// <summary>
        /// 캐시 초기화 (테이블 리로드 시 호출)
        /// </summary>
        public static void ClearCache()
        {
            _currencyPoolCache.Clear();
            _equipmentPoolCache.Clear();
        }

        /// <summary>
        /// 몬스터 레벨에 맞는 Currency 드롭 풀 반환 (캐싱)
        /// </summary>
        public static List<DropPoolEntry> GetCurrencyPool(int monsterLevel)
        {
            if (_currencyPoolCache.TryGetValue(monsterLevel, out var cached))
                return cached;

            var pool = BuildPool(GE.ItemType.Currency, monsterLevel);
            _currencyPoolCache[monsterLevel] = pool;
            return pool;
        }

        /// <summary>
        /// 몬스터 레벨에 맞는 Equipment 드롭 풀 반환 (캐싱)
        /// </summary>
        public static List<DropPoolEntry> GetEquipmentPool(int monsterLevel)
        {
            if (_equipmentPoolCache.TryGetValue(monsterLevel, out var cached))
                return cached;

            var pool = BuildPool(GE.ItemType.Equipment, monsterLevel);
            _equipmentPoolCache[monsterLevel] = pool;
            return pool;
        }

        /// <summary>
        /// ItemTable에서 조건에 맞는 아이템으로 드롭 풀 생성
        /// </summary>
        private static List<DropPoolEntry> BuildPool(GE.ItemType itemType, int monsterLevel)
        {
            var pool = new List<DropPoolEntry>();
            var allItems = AR.s.Data?.GetAllItems();

            if (allItems == null)
                return pool;

            for (int i = 0; i < allItems.Count; i++)
            {
                var item = allItems[i];
                if (item.ItemType != itemType)
                    continue;
                if (item.DropRate <= 0)
                    continue;
                if (item.DropLevel > monsterLevel && item.DropLevel > 0)
                    continue;

                pool.Add(new DropPoolEntry
                {
                    ItemId = item.Id,
                    Weight = item.DropRate,
                    Tier = item.Tier
                });
            }

            return pool;
        }

        /// <summary>
        /// 드롭 풀에서 가중치 랜덤으로 아이템 선택
        /// </summary>
        /// <param name="pool">드롭 풀</param>
        /// <param name="dropRarityBonus">높은 Tier 아이템 가중치 보너스 (%)</param>
        /// <returns>선택된 아이템 ID (풀이 비면 0)</returns>
        public static int SelectWeightedRandom(List<DropPoolEntry> pool, int dropRarityBonus)
        {
            if (pool == null || pool.Count == 0)
                return 0;

            // 풀의 최소 Tier 계산 (보너스 적용 기준)
            int minTier = int.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].Tier < minTier)
                    minTier = pool[i].Tier;
            }

            // 가중치 합산 (DropRarityBonus: 높은 Tier 아이템에 보너스)
            int totalWeight = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                int weight = pool[i].Weight;
                if (dropRarityBonus > 0 && pool[i].Tier > minTier)
                {
                    weight = weight * (100 + dropRarityBonus) / 100;
                }
                totalWeight += weight;
            }

            if (totalWeight <= 0)
                return 0;

            int randomValue = Random.Range(0, totalWeight);
            int cumulative = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                int weight = pool[i].Weight;
                if (dropRarityBonus > 0 && pool[i].Tier > minTier)
                {
                    weight = weight * (100 + dropRarityBonus) / 100;
                }

                cumulative += weight;
                if (randomValue < cumulative)
                {
                    Debug.Log($"[DropPoolBuilder] Selected ItemId={pool[i].ItemId}, Tier={pool[i].Tier}, Weight={weight}/{totalWeight}");
                    return pool[i].ItemId;
                }
            }

            return 0;
        }
    }
}
