#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using Newtonsoft.Json;

namespace ARPG.Editor
{
    /// <summary>
    /// 새 장비의 옵션 티어를 아이템 레벨로 정하는 규칙(ItemLevelModTierRules)과 그 데이터 계약을 로컬 bytes로 검증한다.
    /// 구간표·특수 판정(T11·T12)·몬스터 등급별 확률·아이템 레벨 저장 왕복을 확인한다.
    /// </summary>
    public static partial class ItemEnchantmentDiagnostics
    {
        // 티어가 열리는 아이템 레벨 (2026-10-11 확정 구간표). T11·T12는 특수 판정 전용이며 아이템 레벨 90부터다.
        private static readonly int[] STANDARD_TIER_REQUIRED_LEVELS = { 1, 1, 1, 11, 24, 37, 50, 63, 76, 90, 90, 90 };

        // 아이템 레벨 → 일반 판정에 나오는 최저·최고 티어 (구간 양 끝 레벨)
        private static readonly int[,] EXPECTED_NORMAL_TIER_RANGES =
        {
            { 1, 1, 3 }, { 10, 1, 3 }, { 11, 1, 4 }, { 23, 1, 4 }, { 24, 1, 5 }, { 36, 1, 5 },
            { 37, 2, 6 }, { 49, 2, 6 }, { 50, 3, 7 }, { 62, 3, 7 }, { 63, 4, 8 }, { 75, 4, 8 },
            { 76, 5, 9 }, { 89, 5, 9 }, { 90, 6, 10 }, { 99, 6, 10 },
        };

        private static void VerifyItemLevelTierRules(Dictionary<int, ModTable> modById,
            List<ModTierTable> tiers, List<MonsterArchetypeTable> archetypes,
            List<string> errors, ref int assertions)
        {
            List<ModTierTable> candidates = new List<ModTierTable>();
            List<int> weights = new List<int>();
            List<ModTable> mods = new List<ModTable>(modById.Values);
            mods.Sort((a, b) => a.Id.CompareTo(b.Id));
            int affixCount = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                ModTable mod = mods[i];
                if (mod.Slot != GlobalEnum.ModSlot.Prefix && mod.Slot != GlobalEnum.ModSlot.Postfix)
                    continue;
                affixCount++;
                List<ModTierTable> modTiers = FindTiers(tiers, mod.Id);
                RequireStandardTierCurve(tiers, mod.Id, UsesSecondValue(mod), errors, ref assertions);
                VerifyNormalTierBands(mod.Id, modTiers, candidates, weights, errors, ref assertions);
                VerifySpecialTierRows(mod.Id, modTiers, errors, ref assertions);
            }
            Require(affixCount > 0, "Prefix/Postfix Mod가 없음", errors, ref assertions);

            VerifySpecialTierRoll(errors, ref assertions);
            VerifyArchetypeSpecialChances(archetypes, errors, ref assertions);
            VerifyItemLevelSaveRoundTrip(errors, ref assertions);
        }

        private static void VerifyNormalTierBands(int modId, List<ModTierTable> modTiers,
            List<ModTierTable> candidates, List<int> weights, List<string> errors, ref int assertions)
        {
            for (int row = 0; row < EXPECTED_NORMAL_TIER_RANGES.GetLength(0); row++)
            {
                int level = EXPECTED_NORMAL_TIER_RANGES[row, 0];
                int minTier = EXPECTED_NORMAL_TIER_RANGES[row, 1];
                int maxTier = EXPECTED_NORMAL_TIER_RANGES[row, 2];
                ItemLevelModTierRules.BuildNormalTierCandidates(modTiers, level, candidates, weights);
                bool valid = candidates.Count == maxTier - minTier + 1
                    && candidates[0].Tier == minTier
                    && candidates[candidates.Count - 1].Tier == maxTier;
                Require(valid, $"Mod {modId} 아이템 레벨 {level}의 일반 판정 티어가 T{minTier}~T{maxTier}가 아님: " +
                    DescribeTiers(candidates), errors, ref assertions);
            }

            // 레벨이 1 오를 때마다 평균 티어가 줄지 않고(구간 경계 포함), 일반 판정에는 T11 이상이 나오지 않는다
            double previousAverage = 0d;
            int decreasedLevel = 0;
            int specialLevel = 0;
            for (int level = ItemLevelModTierRules.MIN_ITEM_LEVEL; level <= ItemLevelModTierRules.MAX_ITEM_LEVEL; level++)
            {
                ItemLevelModTierRules.BuildNormalTierCandidates(modTiers, level, candidates, weights);
                double average = GetWeightedAverageTier(candidates, weights);
                if (decreasedLevel == 0 && average + 1e-9d < previousAverage)
                    decreasedLevel = level;
                previousAverage = average;
                if (specialLevel == 0 && candidates.Count > 0
                    && candidates[candidates.Count - 1].Tier >= ItemLevelModTierRules.FIRST_SPECIAL_TIER)
                    specialLevel = level;
            }
            Require(decreasedLevel == 0, $"Mod {modId} 아이템 레벨 {decreasedLevel}에서 평균 티어가 이전 레벨보다 낮아짐",
                errors, ref assertions);
            Require(specialLevel == 0, $"Mod {modId} 아이템 레벨 {specialLevel}의 일반 판정에 T11 이상이 나옴",
                errors, ref assertions);

            // 같은 구간 안에서도 레벨이 높을수록 최고 티어가 잘 나온다 (마지막 구간 90 < 95 < 99)
            double top90 = GetTopTierShare(modTiers, 90, candidates, weights);
            double top95 = GetTopTierShare(modTiers, 95, candidates, weights);
            double top99 = GetTopTierShare(modTiers, 99, candidates, weights);
            Require(top90 < top95 && top95 < top99,
                $"Mod {modId} 마지막 구간 최고 티어 확률이 레벨에 따라 늘지 않음: 90={top90:P1}, 95={top95:P1}, 99={top99:P1}",
                errors, ref assertions);
        }

        private static void VerifySpecialTierRows(int modId, List<ModTierTable> modTiers,
            List<string> errors, ref int assertions)
        {
            int first = ItemLevelModTierRules.FIRST_SPECIAL_TIER;
            int top = ItemLevelModTierRules.TOP_SPECIAL_TIER;
            Require(ItemLevelModTierRules.FindSpecialTier(modTiers, first, 89) == null
                && ItemLevelModTierRules.FindSpecialTier(modTiers, top, 89) == null
                && ItemLevelModTierRules.FindSpecialTier(modTiers, first, 90)?.Tier == first
                && ItemLevelModTierRules.FindSpecialTier(modTiers, top, 90)?.Tier == top,
                $"Mod {modId}의 T{first}·T{top}는 아이템 레벨 90부터만 자격이어야 함", errors, ref assertions);
        }

        private static void VerifySpecialTierRoll(List<string> errors, ref int assertions)
        {
            int first = ItemLevelModTierRules.FIRST_SPECIAL_TIER;
            int top = ItemLevelModTierRules.TOP_SPECIAL_TIER;
            // T11 0.5%(50) / T12 0.1%(10): [0,10) → T12, [10,60) → T11, 그 뒤 실패
            Require(ItemLevelModTierRules.ResolveSpecialTier(50, 10, 0) == top
                && ItemLevelModTierRules.ResolveSpecialTier(50, 10, 9) == top
                && ItemLevelModTierRules.ResolveSpecialTier(50, 10, 10) == first
                && ItemLevelModTierRules.ResolveSpecialTier(50, 10, 59) == first
                && ItemLevelModTierRules.ResolveSpecialTier(50, 10, 60) == 0
                && ItemLevelModTierRules.ResolveSpecialTier(0, 0, 0) == 0,
                "특수 판정 확률 경계가 잘못됨", errors, ref assertions);
            // 두 확률 합이 100%를 넘으면 T12를 우선하고 T11은 남은 범위로 자른다
            Require(ItemLevelModTierRules.ResolveSpecialTier(9000, 5000, 4999) == top
                && ItemLevelModTierRules.ResolveSpecialTier(9000, 5000, 9999) == first,
                "특수 판정 확률 상한 보정이 잘못됨", errors, ref assertions);
        }

        private static void VerifyArchetypeSpecialChances(List<MonsterArchetypeTable> archetypes,
            List<string> errors, ref int assertions)
        {
            int previous11 = 0;
            int previous12 = 0;
            foreach (GlobalEnum.MonsterArchetype archetype in Enum.GetValues(typeof(GlobalEnum.MonsterArchetype)))
            {
                MonsterArchetypeTable? row = null;
                for (int r = 0; r < archetypes.Count; r++)
                {
                    if (archetypes[r].Archetype == archetype)
                        row = archetypes[r];
                }
                Require(row != null, $"MonsterArchetypeTable에 {archetype} 행이 없음", errors, ref assertions);
                if (row == null)
                    continue;

                Require(row.ModTier11ChanceBp > 0 && row.ModTier12ChanceBp > 0
                    && row.ModTier12ChanceBp <= row.ModTier11ChanceBp
                    && row.ModTier11ChanceBp >= previous11 && row.ModTier12ChanceBp >= previous12,
                    $"{archetype}의 T11·T12 확률({row.ModTier11ChanceBp}/{row.ModTier12ChanceBp})은 0보다 크고 " +
                    "T12 ≤ T11이며 낮은 등급보다 작지 않아야 함", errors, ref assertions);
                previous11 = row.ModTier11ChanceBp;
                previous12 = row.ModTier12ChanceBp;
            }
        }

        private static void VerifyItemLevelSaveRoundTrip(List<string> errors, ref int assertions)
        {
            ItemData leveled = new ItemData
            {
                Id = 1,
                ItemInstanceId = 7101,
                Quantity = 1,
                Equipment = new EquipmentData { Id = 1, Quality = 50, ItemLevel = 77 },
            };
            ItemData legacy = new ItemData
            {
                Id = 1,
                ItemInstanceId = 7102,
                Quantity = 1,
                Equipment = new EquipmentData { Id = 1, Quality = 50 },
            };
            string leveledJson = JsonConvert.SerializeObject(leveled, Formatting.None, JsonSettings.Default);
            string legacyJson = JsonConvert.SerializeObject(legacy, Formatting.None, JsonSettings.Default);
            ItemData? loadedLeveled = JsonConvert.DeserializeObject<ItemData>(leveledJson, JsonSettings.Default);
            ItemData? loadedLegacy = JsonConvert.DeserializeObject<ItemData>(legacyJson, JsonSettings.Default);
            Require(leveledJson.Contains("\"ItemLevel\":77") && loadedLeveled?.Equipment?.ItemLevel == 77,
                "아이템 레벨이 저장 왕복에서 유지되지 않음", errors, ref assertions);
            Require(legacyJson.Contains("ItemLevel") == false && loadedLegacy?.Equipment?.ItemLevel == 0,
                "아이템 레벨 0(없음)은 저장하지 않고 0으로 읽혀야 함", errors, ref assertions);
        }

        private static bool UsesSecondValue(ModTable mod)
        {
            return mod.EffectType == GlobalEnum.ModEffectType.AddedPhysDamage
                || mod.EffectType == GlobalEnum.ModEffectType.AddedFireDamage
                || mod.EffectType == GlobalEnum.ModEffectType.AddedIceDamage
                || mod.EffectType == GlobalEnum.ModEffectType.AddedLightningDamage
                || mod.EffectType == GlobalEnum.ModEffectType.AddedPoisonDamage;
        }

        private static double GetWeightedAverageTier(List<ModTierTable> candidates, List<int> weights)
        {
            long total = 0;
            long weighted = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += weights[i];
                weighted += (long)weights[i] * candidates[i].Tier;
            }
            return total > 0 ? (double)weighted / total : 0d;
        }

        private static double GetTopTierShare(List<ModTierTable> modTiers, int itemLevel,
            List<ModTierTable> candidates, List<int> weights)
        {
            ItemLevelModTierRules.BuildNormalTierCandidates(modTiers, itemLevel, candidates, weights);
            int total = ItemLevelModTierRules.GetTotalWeight(weights);
            return total > 0 ? (double)weights[weights.Count - 1] / total : 0d;
        }

        private static string DescribeTiers(List<ModTierTable> candidates)
        {
            if (candidates.Count == 0)
                return "(없음)";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('T').Append(candidates[i].Tier);
            }
            return sb.ToString();
        }
    }
}
