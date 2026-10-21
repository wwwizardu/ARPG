#nullable enable
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 플레이어 드롭 장비 선택 순서 (ITEM_DROP_BALANCE_DESIGN.md §7.2). 상태를 갖지 않는다.
    /// 1. 부위: 장비 분류(Category)별 몫. 2. 구간: 현재 구간(몬스터 레벨 이하 DropLevel 중 가장 큰 값) 80%, 직전 구간 20%.
    /// 3. 아이템: 고른 분류·구간 안에서 ItemTable.DropRate 비율.
    /// 한쪽 구간에 장비가 없으면 다른 쪽에 100%, 두 구간 모두 없으면 그 분류를 빼고 나머지 몫으로 다시 나눈다.
    /// NPC 전리품·상점 매물은 공유 풀(DropPoolBuilder.GetEquipmentPool)을 그대로 쓴다.
    /// </summary>
    public static class PlayerEquipmentDropSelector
    {
        public const int DEFAULT_CURRENT_BAND_SHARE = 80;

        // 부위 몫 (합 100). 방패에는 보조 마법서가 포함된다 (보조 마법서도 Category=Shield). 읽기 전용 설정값.
        private static readonly GE.ItemCategory[] _categories =
        {
            GE.ItemCategory.Weapon, GE.ItemCategory.Helmet, GE.ItemCategory.Armor, GE.ItemCategory.Gloves,
            GE.ItemCategory.Boots, GE.ItemCategory.Belt, GE.ItemCategory.Shield, GE.ItemCategory.Necklace,
            GE.ItemCategory.Ring,
        };
        private static readonly int[] _categoryShares = { 40, 7, 7, 7, 7, 7, 10, 5, 10 };

        // scratch — 쓸 때마다 비운다 (상태를 남기지 않는다)
        private static readonly List<ItemTable> _candidateScratch = new List<ItemTable>();
        private static readonly int[] _categoryWeightScratch = new int[_categories.Length];

        public static IReadOnlyList<GE.ItemCategory> Categories => _categories;
        public static int GetCategoryShare(int categoryIndex) => _categoryShares[categoryIndex];

        /// <summary>
        /// 장비 ItemId를 고른다. 세 roll은 [0, 1) — 부위·구간·아이템 순서. 후보가 없으면 0.
        /// </summary>
        public static int Select(int monsterLevel, int currentBandShare, float categoryRoll01, float bandRoll01,
            float itemRoll01)
        {
            List<ItemTable>? allItems = AR.s.Data?.GetAllItems();
            if (allItems == null)
                return 0;

            ResolveBands(allItems, monsterLevel, out int currentBand, out int previousBand);
            if (currentBand <= 0)
                return 0;

            // 1. 부위: 두 구간 중 하나라도 장비가 있는 분류만 남기고 몫을 다시 나눈다
            int totalShare = 0;
            for (int c = 0; c < _categories.Length; c++)
            {
                bool available = HasCandidate(allItems, _categories[c], currentBand)
                    || (previousBand > 0 && HasCandidate(allItems, _categories[c], previousBand));
                _categoryWeightScratch[c] = available == true ? _categoryShares[c] : 0;
                totalShare += _categoryWeightScratch[c];
            }
            if (totalShare <= 0)
                return 0;

            int categoryIndex = PickIndex(_categoryWeightScratch, totalShare, categoryRoll01);
            GE.ItemCategory category = _categories[categoryIndex];

            // 2. 구간: 현재 구간 currentBandShare%, 직전 구간 나머지. 한쪽이 비면 다른 쪽 100%
            bool hasCurrent = HasCandidate(allItems, category, currentBand);
            bool hasPrevious = previousBand > 0 && HasCandidate(allItems, category, previousBand);
            int band;
            if (hasCurrent == true && hasPrevious == true)
                band = bandRoll01 * 100f < Mathf.Clamp(currentBandShare, 0, 100) ? currentBand : previousBand;
            else
                band = hasCurrent == true ? currentBand : previousBand;

            // 3. 아이템: DropRate 비율
            _candidateScratch.Clear();
            int totalRate = 0;
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemTable item = allItems[i];
                if (IsCandidate(item, category, band) == false)
                    continue;
                _candidateScratch.Add(item);
                totalRate += item.DropRate;
            }
            if (totalRate <= 0)
                return 0;

            int target = Mathf.Min((int)(itemRoll01 * totalRate), totalRate - 1);
            for (int i = 0; i < _candidateScratch.Count; i++)
            {
                target -= _candidateScratch[i].DropRate;
                if (target < 0)
                    return _candidateScratch[i].Id;
            }
            return _candidateScratch[_candidateScratch.Count - 1].Id;
        }

        /// <summary>
        /// 현재 구간 = 몬스터 레벨 이하인 장비 DropLevel 중 가장 큰 값, 직전 구간 = 그다음 값 (없으면 0).
        /// DropLevel 0은 1로 본다.
        /// </summary>
        public static void ResolveBands(List<ItemTable> allItems, int monsterLevel, out int currentBand, out int previousBand)
        {
            currentBand = 0;
            previousBand = 0;
            int level = Mathf.Max(1, monsterLevel);
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemTable item = allItems[i];
                if (item.ItemType != GE.ItemType.Equipment || item.DropRate <= 0)
                    continue;
                int dropLevel = GetDropLevel(item);
                if (dropLevel > level)
                    continue;
                if (dropLevel > currentBand)
                {
                    previousBand = currentBand;
                    currentBand = dropLevel;
                }
                else if (dropLevel < currentBand && dropLevel > previousBand)
                {
                    previousBand = dropLevel;
                }
            }
        }

        private static bool HasCandidate(List<ItemTable> allItems, GE.ItemCategory category, int band)
        {
            for (int i = 0; i < allItems.Count; i++)
            {
                if (IsCandidate(allItems[i], category, band) == true)
                    return true;
            }
            return false;
        }

        private static bool IsCandidate(ItemTable item, GE.ItemCategory category, int band)
        {
            return item.ItemType == GE.ItemType.Equipment && item.DropRate > 0 && item.Category == category
                && GetDropLevel(item) == band;
        }

        private static int GetDropLevel(ItemTable item)
        {
            return Mathf.Max(1, item.DropLevel);
        }

        private static int PickIndex(int[] weights, int total, float roll01)
        {
            int target = Mathf.Min((int)(roll01 * total), total - 1);
            for (int i = 0; i < weights.Length; i++)
            {
                target -= weights[i];
                if (target < 0)
                    return i;
            }
            return weights.Length - 1;
        }
    }
}
