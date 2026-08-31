#nullable enable
using ARPG.Data;
using ARPG.Tables;

namespace ARPG.Item
{
    /// <summary>
    /// 장비 옵션을 Tooltip과 Forge 비교 UI에서 동일하게 표시하는 순수 포맷 책임.
    /// </summary>
    public static class EquipmentOptionText
    {
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
                    || IsPercentFlatStat(mod.TargetStat) == true)
                    value += "%";
                else if (mod.TargetStat == GlobalEnum.Stat.HpGeneration
                    || mod.TargetStat == GlobalEnum.Stat.MpGeneration)
                    value = $"초당 {value}";
            }

            return $"T{tier.Tier} {mod.Name}: {value}";
        }

        public static string GetSlotName(GlobalEnum.ModSlot slot)
        {
            switch (slot)
            {
                case GlobalEnum.ModSlot.Base: return "기본";
                case GlobalEnum.ModSlot.Implicit: return "고유";
                case GlobalEnum.ModSlot.Prefix: return "접두";
                case GlobalEnum.ModSlot.Postfix: return "접미";
                default: return slot.ToString();
            }
        }

        private static bool IsPercentFlatStat(GlobalEnum.Stat stat)
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
    }
}
