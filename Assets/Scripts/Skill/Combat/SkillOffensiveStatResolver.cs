#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 캐릭터 기본값과 현재 스킬 태그에 맞는 글로벌 Modifier를 합산한 공격 스탯 원본.
    /// 무기 기본 능력과 SkillTable 수치는 포함하지 않는다.
    /// </summary>
    public struct SkillOffensiveStatSnapshot
    {
        public int PhysMin;
        public int PhysMax;
        public int FireMin;
        public int FireMax;
        public int IceMin;
        public int IceMax;
        public int LightningMin;
        public int LightningMax;
        public int PoisonMin;
        public int PoisonMax;

        public int CharacterCritFlat;
        public int CritIncrease;
        public int AttackSpeedIncrease;
    }

    /// <summary>
    /// 스킬 태그에 맞는 공격용 StatModifier를 Add → Increased 합계 → Override 순으로 해석한다.
    /// Modifier의 Tags가 None이면 모든 스킬에, 값이 있으면 그 태그를 모두 가진 스킬에 적용한다.
    /// </summary>
    public static class SkillOffensiveStatResolver
    {
        private const int STAT_BUCKET_COUNT = (int)GE.Stat.DelegateToTotem + 1;

        public static SkillOffensiveStatSnapshot Resolve(
            StatComponent baseStat,
            List<StatModifier>? modifiers,
            GE.SkillTag skillTags)
        {
            int[] addedByStat = new int[STAT_BUCKET_COUNT];
            int[] increasedByStat = new int[STAT_BUCKET_COUNT];
            int[] overrideByStat = new int[STAT_BUCKET_COUNT];
            int[] overridePriorityByStat = new int[STAT_BUCKET_COUNT];
            bool[] hasOverrideByStat = new bool[STAT_BUCKET_COUNT];

            if (modifiers != null)
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    StatModifier modifier = modifiers[i];
                    if (MatchesSkillTags(modifier.Tags, skillTags) == false)
                        continue;

                    int statIndex = (int)modifier.StatType;
                    if (statIndex < 0 || statIndex >= STAT_BUCKET_COUNT)
                        continue;

                    switch (modifier.ModifierType)
                    {
                        case StatModifierType.Add:
                            addedByStat[statIndex] += modifier.Value;
                            break;
                        case StatModifierType.Multiply:
                            increasedByStat[statIndex] += modifier.Value;
                            break;
                        case StatModifierType.Override:
                            if (hasOverrideByStat[statIndex] == false
                                || modifier.Priority >= overridePriorityByStat[statIndex])
                            {
                                hasOverrideByStat[statIndex] = true;
                                overridePriorityByStat[statIndex] = modifier.Priority;
                                overrideByStat[statIndex] = modifier.Value;
                            }
                            break;
                    }
                }
            }

            SkillOffensiveStatSnapshot result;
            result.PhysMin = ResolveValue(baseStat.BaseAttackMin, GE.Stat.AttackMin,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.PhysMax = ResolveValue(baseStat.BaseAttackMax, GE.Stat.AttackMax,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.FireMin = ResolveValue(baseStat.BaseFireAttackMin, GE.Stat.FireAttackMin,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.FireMax = ResolveValue(baseStat.BaseFireAttackMax, GE.Stat.FireAttackMax,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.IceMin = ResolveValue(baseStat.BaseIceAttackMin, GE.Stat.IceAttackMin,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.IceMax = ResolveValue(baseStat.BaseIceAttackMax, GE.Stat.IceAttackMax,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.LightningMin = ResolveValue(baseStat.BaseLightningAttackMin, GE.Stat.LightningAttackMin,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.LightningMax = ResolveValue(baseStat.BaseLightningAttackMax, GE.Stat.LightningAttackMax,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.PoisonMin = ResolveValue(baseStat.BasePoisonAttackMin, GE.Stat.PoisonAttackMin,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.PoisonMax = ResolveValue(baseStat.BasePoisonAttackMax, GE.Stat.PoisonAttackMax,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);

            int criRateIndex = (int)GE.Stat.CriRate;
            result.CharacterCritFlat = baseStat.BaseCriRate + addedByStat[criRateIndex];
            if (hasOverrideByStat[criRateIndex] == true)
                result.CharacterCritFlat = overrideByStat[criRateIndex];

            result.CritIncrease = increasedByStat[criRateIndex]
                + ResolveIncreaseBucket(GE.Stat.CriRateMul,
                    addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);

            int attackSpeedIndex = (int)GE.Stat.AttackSpeed;
            result.AttackSpeedIncrease = addedByStat[attackSpeedIndex]
                + increasedByStat[attackSpeedIndex]
                + ResolveIncreaseBucket(GE.Stat.AttackSpeedMul,
                    addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            if (hasOverrideByStat[attackSpeedIndex] == true)
                result.AttackSpeedIncrease = overrideByStat[attackSpeedIndex];

            return result;
        }

        private static bool MatchesSkillTags(GE.SkillTag requiredTags, GE.SkillTag skillTags)
        {
            return requiredTags == GE.SkillTag.None
                || (skillTags & requiredTags) == requiredTags;
        }

        private static int ResolveValue(
            int baseValue,
            GE.Stat stat,
            int[] addedByStat,
            int[] increasedByStat,
            int[] overrideByStat,
            bool[] hasOverrideByStat)
        {
            int index = (int)stat;
            int value = Mathf.RoundToInt(
                (baseValue + addedByStat[index]) * (1f + increasedByStat[index] / 100f));

            return hasOverrideByStat[index] == true ? overrideByStat[index] : value;
        }

        private static int ResolveIncreaseBucket(
            GE.Stat stat,
            int[] addedByStat,
            int[] increasedByStat,
            int[] overrideByStat,
            bool[] hasOverrideByStat)
        {
            int index = (int)stat;
            if (hasOverrideByStat[index] == true)
                return overrideByStat[index];

            return addedByStat[index] + increasedByStat[index];
        }
    }
}
