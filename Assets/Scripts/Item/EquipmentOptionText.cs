#nullable enable
using ARPG.Data;
using ARPG.Tables;

namespace ARPG.Item
{
    /// <summary>
    /// 장비 능력·옵션을 Tooltip(단일·비교)과 Forge 비교 UI에서 동일하게 표시하는 순수 포맷 책임.
    /// </summary>
    public static class EquipmentOptionText
    {
        public static string? FormatWeaponDamageLine(string label, DamageRange range)
        {
            return range.Max > 0 ? $"{label}: {range.Min}~{range.Max}" : null;
        }

        public static string? FormatWeaponCritLine(int criRate)
        {
            return criRate > 0 ? $"치명타 확률: {criRate}%" : null;
        }

        public static string? FormatWeaponAttackSpeedLine(float attackSpeed)
        {
            return attackSpeed > 0f ? $"초당 공격 횟수: {attackSpeed:F2}" : null;
        }

        public static string FormatWeaponEnhancementLine(WeaponLocalEnhancementInstance enhancement)
        {
            string label = enhancement.StatType switch
            {
                GlobalEnum.WeaponLocalStatType.PhysicsDamage => "물리 피해 강화",
                GlobalEnum.WeaponLocalStatType.FireDamage => "화염 피해 강화",
                GlobalEnum.WeaponLocalStatType.IceDamage => "냉기 피해 강화",
                GlobalEnum.WeaponLocalStatType.LightningDamage => "번개 피해 강화",
                GlobalEnum.WeaponLocalStatType.PoisonDamage => "독 피해 강화",
                GlobalEnum.WeaponLocalStatType.CriticalRate => "치명타 확률 강화",
                GlobalEnum.WeaponLocalStatType.AttackSpeed => "공격속도 강화",
                _ => enhancement.StatType.ToString(),
            };
            return $"{label}: +{enhancement.Value}%";
        }

        /// <summary>
        /// 옵션의 스킬 태그 적용 조건 문구. 태그가 모두 일치하는 스킬에만 적용된다 (SkillOffensiveStatResolver.MatchesSkillTags).
        /// 태그가 없으면 빈 문자열.
        /// </summary>
        public static string FormatSkillTagCondition(GlobalEnum.SkillTag tags)
        {
            if (tags == GlobalEnum.SkillTag.None)
                return string.Empty;

            System.Text.StringBuilder sb = new();
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Attack, "공격");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Spell, "주문");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Melee, "근접");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Ranged, "원거리");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.AoE, "범위");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Projectile, "투사체");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Physics, "물리");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Fire, "화염");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Ice, "냉기");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Lightning, "번개");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Poison, "독");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Buff, "버프");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Debuff, "디버프");
            AppendSkillTag(sb, tags, GlobalEnum.SkillTag.Move, "이동");
            return sb.Length > 0 ? $"{sb} 스킬 전용" : string.Empty;
        }

        public static string FormatModLine(ModInstance mod)
        {
            if (mod == null || mod.Table == null)
                return string.Empty;

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat
                && mod.Table.TargetStat == GlobalEnum.Stat.CriRate)
            {
                return $"치명타 확률 +{mod.Value1}%";
            }

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat
                && mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeed)
            {
                if (mod.Slot == GlobalEnum.ModSlot.Base)
                    return $"공격 속도: {mod.Value1 * 0.01f:0.##}/초";
                return $"공격속도 {mod.Value1}% 증가";
            }

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat)
            {
                if (mod.Table.TargetStat == GlobalEnum.Stat.Mp)
                    return $"최대 마나 +{mod.Value1}";

                if (mod.Table.TargetStat == GlobalEnum.Stat.HpGeneration)
                    return $"초당 체력 재생 +{mod.Value1}";

                if (mod.Table.TargetStat == GlobalEnum.Stat.MpGeneration)
                    return $"초당 마나 재생 +{mod.Value1}";

                if (mod.Table.TargetStat == GlobalEnum.Stat.CriDamageMul)
                    return $"치명타 피해 +{mod.Value1}%";

                if (IsResistanceStat(mod.Table.TargetStat) == true)
                    return $"{mod.Table.Name} +{mod.Value1}%";
            }

            // 드롭 보너스 스탯은 FlatStat·IncreasedStat 모두 증가율 묶음이다 (ITEM_DROP_BALANCE_DESIGN §8.5)
            string? dropBonusName = GetDropBonusStatName(mod.Table.TargetStat);
            if (dropBonusName != null
                && (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat
                    || mod.Table.EffectType == GlobalEnum.ModEffectType.IncreasedStat))
                return $"{dropBonusName} {mod.Value1}% 증가";

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.IncreasedStat)
            {
                if (mod.Table.TargetStat == GlobalEnum.Stat.CriRate
                    || mod.Table.TargetStat == GlobalEnum.Stat.CriRateMul)
                    return $"치명타 확률 {mod.Value1}% 증가";

                if (mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeed
                    || mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeedMul)
                    return $"공격속도 {mod.Value1}% 증가";

                if (mod.Table.TargetStat == GlobalEnum.Stat.CastSpeedMul)
                    return $"시전속도 {mod.Value1}% 증가";

                return $"{mod.Table.Name}: {mod.Value1}% 증가";
            }

            return mod.Value2 > 0
                ? $"{mod.Table.Name}: +{mod.Value1}~{mod.Value2}"
                : $"{mod.Table.Name}: +{mod.Value1}";
        }

        public static string FormatOptionLine(ModInstance mod)
        {
            string tier = mod.Tier > 0 ? $" T{mod.Tier}" : string.Empty;
            return $"[{GetSlotName(mod.Slot)}{tier}] {FormatModLine(mod)}";
        }

        public static string FormatTierRange(ModTable mod, ModTierTable tier)
        {
            string value = tier.Max2 > 0 || tier.Min2 > 0
                ? $"{tier.Min1}~{tier.Max1} / {tier.Min2}~{tier.Max2}"
                : $"{tier.Min1}~{tier.Max1}";

            if (tier.Max2 == 0 && tier.Min2 == 0)
            {
                if (mod.EffectType == GlobalEnum.ModEffectType.IncreasedStat
                    || IsPercentFlatStat(mod.TargetStat) == true
                    || GetDropBonusStatName(mod.TargetStat) != null)
                    value += "%";
                else if (mod.TargetStat == GlobalEnum.Stat.HpGeneration
                    || mod.TargetStat == GlobalEnum.Stat.MpGeneration)
                    value = $"초당 {value}";
            }

            return $"T{tier.Tier} {mod.Name}: {value}";
        }

        /// <summary>장비 등급 이름 (ITEM_DROP_BALANCE_DESIGN §4.1).</summary>
        public static string GetGradeName(GlobalEnum.EquipmentGrade grade)
        {
            switch (grade)
            {
                case GlobalEnum.EquipmentGrade.Magic: return "마법";
                case GlobalEnum.EquipmentGrade.Rare: return "희귀";
                default: return "일반";
            }
        }

        /// <summary>품질 줄 "품질 +15%". 품질 0이거나 장비가 없으면 null (§5).</summary>
        public static string? FormatQualityLine(EquipmentData? equipment)
        {
            return equipment != null && equipment.Quality > 0 ? $"품질 +{equipment.Quality}%" : null;
        }

        /// <summary>아이템 수량·희귀도 스탯 이름. 다른 스탯이면 null.</summary>
        public static string? GetDropBonusStatName(GlobalEnum.Stat stat)
        {
            switch (stat)
            {
                case GlobalEnum.Stat.ItemQuantity: return "아이템 수량";
                case GlobalEnum.Stat.ItemRarity: return "아이템 희귀도";
                default: return null;
            }
        }

        public static string GetSlotName(GlobalEnum.ModSlot slot)
        {
            switch (slot)
            {
                case GlobalEnum.ModSlot.Base: return "기본";
                case GlobalEnum.ModSlot.Implicit: return "고유";
                case GlobalEnum.ModSlot.Prefix: return "접두";
                case GlobalEnum.ModSlot.Postfix: return "접미";
                case GlobalEnum.ModSlot.Charm: return "참";
                default: return slot.ToString();
            }
        }

        /// <summary>FlatStat 값이 확률·비율(%)인 스탯. 비교 차이는 %p로 표시한다.</summary>
        public static bool IsPercentFlatStat(GlobalEnum.Stat stat)
        {
            return stat == GlobalEnum.Stat.CriRate
                || stat == GlobalEnum.Stat.CriDamageMul
                || stat == GlobalEnum.Stat.BloodingRate
                || stat == GlobalEnum.Stat.IgniteRate
                || stat == GlobalEnum.Stat.PoisonRate
                || IsResistanceStat(stat) == true;
        }

        private static bool IsResistanceStat(GlobalEnum.Stat stat)
        {
            return stat == GlobalEnum.Stat.FireResist
                || stat == GlobalEnum.Stat.IceResist
                || stat == GlobalEnum.Stat.LightningResist
                || stat == GlobalEnum.Stat.PoisonResist;
        }

        private static void AppendSkillTag(System.Text.StringBuilder sb, GlobalEnum.SkillTag tags, GlobalEnum.SkillTag tag, string name)
        {
            if ((tags & tag) == 0)
                return;
            if (sb.Length > 0)
                sb.Append('·');
            sb.Append(name);
        }
    }
}
