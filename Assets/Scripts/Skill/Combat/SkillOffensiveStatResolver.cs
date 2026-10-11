#nullable enable
using System;
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

        // 속성별 피해 증가율(%) — 속성 전용 증가 + 모든 피해 증가(DamageInc). 프로필의 속성별 피해 합계에 곱한다.
        public int PhysDamageIncrease;
        public int FireDamageIncrease;
        public int IceDamageIncrease;
        public int LightningDamageIncrease;
        public int PoisonDamageIncrease;
    }

    /// <summary>
    /// 스킬 태그에 맞는 공격용 StatModifier를 Add → Increased 합계 → Override 순으로 해석한다.
    /// Modifier의 Tags가 None이면 모든 스킬에, 값이 있으면 그 태그를 모두 가진 스킬에 적용한다.
    /// </summary>
    public static class SkillOffensiveStatResolver
    {
        private const int STAT_BUCKET_COUNT = (int)GE.Stat.DelegateToTotem + 1;

        /// <summary>
        /// Resolve()가 실제로 결과 스냅샷에 담아 내보내는 스탯 목록.
        ///
        /// 태그(Tags != None)가 붙은 StatModifier는 System_StatCalculation에서 의도적으로 제외된다
        /// (StatComponent는 엔티티당 하나뿐이라 "어떤 스킬인가"라는 문맥을 담을 수 없다).
        /// 그래서 태그 붙은 mod가 효과를 내는 유일한 통로가 이 resolver인데, Resolve()는 값을 전부
        /// 배열에 모으면서도 아래 목록만 꺼내 쓴다. 목록 밖 스탯은 조용히 버려진다.
        ///
        /// 확장하려면 이 목록에 추가하는 것만으로는 부족하다 — SkillOffensiveStatSnapshot에 필드를
        /// 만들고 Resolve()에서 읽어낸 뒤, BuildProfile이 그 값을 소비하도록 배선해야 한다.
        /// </summary>
        private static readonly GE.Stat[] TagSupportedStats =
        {
            GE.Stat.AttackMin,          GE.Stat.AttackMax,
            GE.Stat.FireAttackMin,      GE.Stat.FireAttackMax,
            GE.Stat.IceAttackMin,       GE.Stat.IceAttackMax,
            GE.Stat.LightningAttackMin, GE.Stat.LightningAttackMax,
            GE.Stat.PoisonAttackMin,    GE.Stat.PoisonAttackMax,
            GE.Stat.CriRate,            GE.Stat.CriRateMul,
            GE.Stat.AttackSpeed,        GE.Stat.AttackSpeedMul,
            GE.Stat.PhysDamageInc,      GE.Stat.FireDamageInc,
            GE.Stat.IceDamageInc,       GE.Stat.LightningDamageInc,
            GE.Stat.PoisonDamageInc,    GE.Stat.DamageInc,
        };

        /// <summary>
        /// 태그가 붙은 StatModifier가 이 스탯에 대해 실제로 효과를 낼 수 있는지 검사한다.
        /// false면 해당 mod는 StatComponent에도, 스킬 프로필에도 반영되지 않는다.
        /// </summary>
        public static bool IsTagSupportedStat(GE.Stat statType)
        {
            for (int i = 0; i < TagSupportedStats.Length; i++)
            {
                if (TagSupportedStats[i] == statType)
                    return true;
            }
            return false;
        }

        // 호출 간 상태를 남기지 않는 GC 회피용 스크래치 버퍼 — 진입할 때마다 Array.Clear로 초기화한다.
        // Resolve는 메인 스레드 전용이고 재귀·중첩 호출이 없으며, 반환형이 struct라 배열 참조가 밖으로 나가지 않는다.
        // (CLAUDE.md Logic Placement: 매 사용 전 Clear하는 스크래치는 상태를 소유하지 않으므로 static 허용)
        private static readonly int[] _addedByStat = new int[STAT_BUCKET_COUNT];
        private static readonly int[] _increasedByStat = new int[STAT_BUCKET_COUNT];
        private static readonly int[] _overrideByStat = new int[STAT_BUCKET_COUNT];
        private static readonly int[] _overridePriorityByStat = new int[STAT_BUCKET_COUNT];
        private static readonly bool[] _hasOverrideByStat = new bool[STAT_BUCKET_COUNT];

        public static SkillOffensiveStatSnapshot Resolve(
            StatComponent baseStat,
            List<StatModifier>? modifiers,
            GE.SkillTag skillTags)
        {
            int[] addedByStat = _addedByStat;
            int[] increasedByStat = _increasedByStat;
            int[] overrideByStat = _overrideByStat;
            int[] overridePriorityByStat = _overridePriorityByStat;
            bool[] hasOverrideByStat = _hasOverrideByStat;

            Array.Clear(addedByStat, 0, STAT_BUCKET_COUNT);
            Array.Clear(increasedByStat, 0, STAT_BUCKET_COUNT);
            Array.Clear(overrideByStat, 0, STAT_BUCKET_COUNT);
            Array.Clear(overridePriorityByStat, 0, STAT_BUCKET_COUNT);
            Array.Clear(hasOverrideByStat, 0, STAT_BUCKET_COUNT);

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

            int allDamageIncrease = ResolveIncreaseBucket(GE.Stat.DamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.PhysDamageIncrease = allDamageIncrease + ResolveIncreaseBucket(GE.Stat.PhysDamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.FireDamageIncrease = allDamageIncrease + ResolveIncreaseBucket(GE.Stat.FireDamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.IceDamageIncrease = allDamageIncrease + ResolveIncreaseBucket(GE.Stat.IceDamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.LightningDamageIncrease = allDamageIncrease + ResolveIncreaseBucket(GE.Stat.LightningDamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);
            result.PoisonDamageIncrease = allDamageIncrease + ResolveIncreaseBucket(GE.Stat.PoisonDamageInc,
                addedByStat, increasedByStat, overrideByStat, hasOverrideByStat);

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
