#nullable enable
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Item
{
    /// <summary>
    /// 장비 랜덤 옵션 생성 입력. ItemLevel은 1~99로 보정한다(0 = 기본값 1).
    /// 특수 티어(T11·T12) 확률은 만분율이며 몬스터 드랍만 0보다 크다.
    /// 등급(일반·마법·희귀)·품질은 플레이어 드롭 전용이다(ITEM_DROP_BALANCE_DESIGN §4·§5). HasDropGrade가 false면
    /// 공유 경로(NPC 전리품·상점 랜덤 매물)의 옛 규칙(접두 1~3 + 접미 1~3, 품질 0)과 결정론 난수 순서를 그대로 쓴다.
    /// </summary>
    public readonly struct EquipmentRollOptions
    {
        public readonly int ItemLevel;
        public readonly int Tier11ChanceBp;
        public readonly int Tier12ChanceBp;
        public readonly bool HasDropGrade;
        public readonly GlobalEnum.EquipmentGrade Grade;
        /// <summary>접두·접미 총수. 희귀 3~6, 마법 1~2, 일반 0.</summary>
        public readonly int AffixCount;
        /// <summary>일반일 때 품질 (0~20). 마법·희귀는 0이다.</summary>
        public readonly int Quality;

        public EquipmentRollOptions(int itemLevel, int tier11ChanceBp = 0, int tier12ChanceBp = 0)
        {
            ItemLevel = itemLevel;
            Tier11ChanceBp = tier11ChanceBp;
            Tier12ChanceBp = tier12ChanceBp;
            HasDropGrade = false;
            Grade = GlobalEnum.EquipmentGrade.Rare;
            AffixCount = 0;
            Quality = 0;
        }

        private EquipmentRollOptions(int itemLevel, int tier11ChanceBp, int tier12ChanceBp,
            GlobalEnum.EquipmentGrade grade, int affixCount, int quality)
        {
            ItemLevel = itemLevel;
            Tier11ChanceBp = tier11ChanceBp;
            Tier12ChanceBp = tier12ChanceBp;
            HasDropGrade = true;
            Grade = grade;
            AffixCount = affixCount;
            Quality = quality;
        }

        /// <summary>플레이어 드롭(몬스터·상자) 장비. 등급·옵션 수·품질은 드롭 쪽이 미리 정해 넘긴다.</summary>
        public static EquipmentRollOptions ForPlayerDrop(int itemLevel, int tier11ChanceBp, int tier12ChanceBp,
            GlobalEnum.EquipmentGrade grade, int affixCount, int quality)
        {
            return new EquipmentRollOptions(itemLevel, tier11ChanceBp, tier12ChanceBp, grade, affixCount, quality);
        }

        public bool HasSpecialTierChance => Tier11ChanceBp > 0 || Tier12ChanceBp > 0;
    }

    /// <summary>
    /// 장비의 Prefix/Postfix 옵션 티어를 아이템 레벨로 정하는 규칙. 새 장비 생성과 마법부여(ITEM_DROP_BALANCE_DESIGN D9)가 같이 쓴다.
    /// 일반 판정(T1~T10): ModTierTable.RequiredLevel이 아이템 레벨 이하인 티어 중 가장 높은 티어(상한)에서
    /// 아래로 NORMAL_TIER_WINDOW 단계까지만 후보로 삼고, Weight에 구간 진행도 배율을 곱한 가중치로 고른다.
    /// 구간은 상한 티어가 열린 레벨부터 다음 티어가 열리기 직전 레벨까지다.
    /// 마법 규칙(§6.2): 마법 드롭과 마법 상태에서의 진화·변화·엑잘티드는 후보 중 위 2단계의 가중치를 2배로 준다.
    /// 특수 판정(T11·T12): 일반 판정 뒤 장비 하나당 한 번 굴려 이미 붙은 옵션 하나를 올린다. 레벨 조건은 같은 RequiredLevel 칸이다.
    /// </summary>
    public static class ItemLevelModTierRules
    {
        public const int MIN_ITEM_LEVEL = 1;
        public const int MAX_ITEM_LEVEL = 99;
        /// <summary>이 티어부터는 일반 판정에 나오지 않고 특수 판정으로만 붙는다.</summary>
        public const int FIRST_SPECIAL_TIER = 11;
        public const int TOP_SPECIAL_TIER = 12;
        /// <summary>상한을 포함해 아래로 몇 단계까지 후보인지. 5면 상한 T10일 때 T6~T10.</summary>
        public const int NORMAL_TIER_WINDOW = 5;
        /// <summary>특수 판정 확률의 분모 (만분율).</summary>
        public const int CHANCE_SCALE = 10000;
        /// <summary>마법 규칙: 상한부터 몇 단계의 가중치를 키우는지 (상한 T10이면 T9·T10).</summary>
        public const int MAGIC_TOP_TIER_COUNT = 2;
        /// <summary>마법 규칙: 위 단계 가중치 배율.</summary>
        public const int MAGIC_TOP_TIER_WEIGHT_MUL = 2;

        // 구간 진행도(0→1)에 따른 가중치 배율. 새로 열린 상한 티어는 서서히 늘고, 곧 빠질 하한 티어는 서서히 준다.
        private const float CAP_START_SCALE = 0.3f;
        private const float CAP_END_SCALE = 1f;
        // 다음 티어가 없는 마지막 구간에서는 상한 티어가 계속 늘어 최고 레벨에서 이 배율이 된다.
        private const float TOP_BAND_CAP_END_SCALE = 2f;
        private const float FLOOR_END_SCALE = 0.3f;
        // 배율이 곱해진 가중치를 정수로 바꿀 때의 정밀도
        private const int WEIGHT_PRECISION = 100;

        public static int ClampItemLevel(int itemLevel)
        {
            return Mathf.Clamp(itemLevel, MIN_ITEM_LEVEL, MAX_ITEM_LEVEL);
        }

        /// <summary>
        /// 한 Mod의 티어 행 전체(순서 무관)로 일반 판정 후보와 정수 가중치를 티어 오름차순으로 채운다.
        /// 두 목록은 비운 뒤 다시 채운다. 후보 수를 돌려준다. magicRule이면 위 2단계 가중치를 2배로 준다.
        /// </summary>
        public static int BuildNormalTierCandidates(IReadOnlyList<ModTierTable> modTiers, int itemLevel,
            List<ModTierTable> outTiers, List<int> outWeights, bool magicRule = false)
        {
            outTiers.Clear();
            outWeights.Clear();
            int level = ClampItemLevel(itemLevel);

            ModTierTable? cap = null;
            int nextBandLevel = int.MaxValue;
            for (int i = 0; i < modTiers.Count; i++)
            {
                ModTierTable tier = modTiers[i];
                if (IsRollableNormalTier(tier) == false)
                    continue;
                if (tier.RequiredLevel <= level)
                {
                    if (cap == null || tier.Tier > cap.Tier)
                        cap = tier;
                }
                else if (tier.RequiredLevel < nextBandLevel)
                {
                    nextBandLevel = tier.RequiredLevel;
                }
            }
            if (cap == null)
                return 0;

            bool isTopBand = nextBandLevel == int.MaxValue;
            int bandLastLevel = isTopBand ? MAX_ITEM_LEVEL : nextBandLevel - 1;
            float progress = bandLastLevel > cap.RequiredLevel
                ? Mathf.Clamp01((float)(level - cap.RequiredLevel) / (bandLastLevel - cap.RequiredLevel))
                : 1f;
            float capScale = Mathf.Lerp(CAP_START_SCALE, isTopBand == true ? TOP_BAND_CAP_END_SCALE : CAP_END_SCALE,
                progress);
            float floorScale = Mathf.Lerp(1f, FLOOR_END_SCALE, progress);
            int floorTier = cap.Tier - (NORMAL_TIER_WINDOW - 1);

            for (int i = 0; i < modTiers.Count; i++)
            {
                ModTierTable tier = modTiers[i];
                if (IsRollableNormalTier(tier) == false || tier.RequiredLevel > level || tier.Tier < floorTier)
                    continue;

                float scale = 1f;
                if (tier.Tier == cap.Tier)
                    scale = capScale;
                else if (tier.Tier == floorTier)
                    scale = floorScale;
                if (magicRule == true && tier.Tier > cap.Tier - MAGIC_TOP_TIER_COUNT)
                    scale *= MAGIC_TOP_TIER_WEIGHT_MUL;
                int weight = Mathf.Max(1, Mathf.RoundToInt(tier.Weight * scale * WEIGHT_PRECISION));
                InsertByTier(outTiers, outWeights, tier, weight);
            }
            return outTiers.Count;
        }

        public static int GetTotalWeight(List<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++)
                total += weights[i];
            return total;
        }

        /// <summary>roll은 [0, GetTotalWeight) 범위다.</summary>
        public static int SelectWeightedIndex(List<int> weights, int roll)
        {
            int accumulated = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                accumulated += weights[i];
                if (roll < accumulated)
                    return i;
            }
            return weights.Count - 1;
        }

        /// <summary>
        /// 특수 판정. roll은 [0, CHANCE_SCALE) 범위다. 실패하면 0, 성공하면 올릴 티어(T12를 먼저 판정)를 돌려준다.
        /// </summary>
        public static int ResolveSpecialTier(int tier11ChanceBp, int tier12ChanceBp, int roll)
        {
            int tier12Chance = Mathf.Clamp(tier12ChanceBp, 0, CHANCE_SCALE);
            int tier11Chance = Mathf.Clamp(tier11ChanceBp, 0, CHANCE_SCALE - tier12Chance);
            if (roll < tier12Chance)
                return TOP_SPECIAL_TIER;
            if (roll < tier12Chance + tier11Chance)
                return FIRST_SPECIAL_TIER;
            return 0;
        }

        /// <summary>목표 특수 티어 행이 있고 아이템 레벨이 그 행의 RequiredLevel 이상이면 돌려준다.</summary>
        public static ModTierTable? FindSpecialTier(IReadOnlyList<ModTierTable> modTiers, int targetTier, int itemLevel)
        {
            int level = ClampItemLevel(itemLevel);
            for (int i = 0; i < modTiers.Count; i++)
            {
                ModTierTable tier = modTiers[i];
                if (tier.Tier == targetTier && tier.RequiredLevel <= level && HasValidRange(tier) == true)
                    return tier;
            }
            return null;
        }

        private static bool IsRollableNormalTier(ModTierTable tier)
        {
            return tier.Tier > 0 && tier.Tier < FIRST_SPECIAL_TIER && tier.Weight > 0 && HasValidRange(tier) == true;
        }

        private static bool HasValidRange(ModTierTable tier)
        {
            return tier.Min1 >= 0 && tier.Min1 <= tier.Max1 && tier.Max1 <= ushort.MaxValue
                && tier.Min2 >= 0 && tier.Min2 <= tier.Max2 && tier.Max2 <= ushort.MaxValue;
        }

        private static void InsertByTier(List<ModTierTable> tiers, List<int> weights, ModTierTable tier, int weight)
        {
            int index = tiers.Count;
            while (index > 0 && tiers[index - 1].Tier > tier.Tier)
                index--;
            tiers.Insert(index, tier);
            weights.Insert(index, weight);
        }
    }
}
