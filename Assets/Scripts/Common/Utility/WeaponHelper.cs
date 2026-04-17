#nullable enable
using ARPG.Data;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 무기 Local 스탯 조회 유틸리티
    /// 무기의 데미지/치명타/공속은 StatModifier를 거치지 않고 EquipmentData.WeaponStats 캐시에서 직접 조회
    /// → Attack 태그가 있는 스킬에만 무기 스탯 적용
    /// </summary>
    public static class WeaponHelper
    {
        /// <summary>
        /// 엔티티의 장착 무기 반환. 플레이어 외에는 null.
        /// Left → Right 순서로 조회.
        /// </summary>
        public static ItemData? GetEquippedWeapon(int entityId)
        {
            if (entityId != AR.s.Data.CurrentPlayerEntityId)
                return null;

            ItemData?[] equip = AR.s.Data.Player._inventoryEquip;
            ItemData? leftWeapon = equip[(int)GE.EquipSlotType.WeaponLeft];
            ItemData? rightWeapon = equip[(int)GE.EquipSlotType.WeaponRight];
            return leftWeapon ?? rightWeapon;
        }

        /// <summary>
        /// 특정 속성의 무기 데미지 범위 (Local 파이프라인 완료 값).
        /// 무기 없으면 (0, 0).
        /// </summary>
        public static void GetWeaponDamage(int entityId, GE.DamageType type, out int min, out int max)
        {
            min = 0;
            max = 0;

            ItemData? weapon = GetEquippedWeapon(entityId);
            if (weapon == null || weapon.Equipment == null)
                return;

            WeaponStatCache stats = weapon.Equipment.WeaponStats;
            DamageRange range = type switch
            {
                GE.DamageType.Physics => stats.Physics,
                GE.DamageType.Fire => stats.Fire,
                GE.DamageType.Ice => stats.Ice,
                GE.DamageType.Lightning => stats.Lightning,
                GE.DamageType.Poison => stats.Poison,
                _ => default,
            };

            min = range.Min;
            max = range.Max;
        }

        /// <summary>
        /// 무기 치명타 확률 (%, Local 파이프라인 완료). 무기 없으면 0.
        /// </summary>
        public static int GetWeaponCriRate(int entityId)
        {
            ItemData? weapon = GetEquippedWeapon(entityId);
            if (weapon == null || weapon.Equipment == null)
                return 0;

            return weapon.Equipment.WeaponStats.CriRate;
        }

        /// <summary>
        /// 무기 공격 속도 (초당 공격 횟수, Local 파이프라인 완료). 무기 없으면 0.
        /// </summary>
        public static float GetWeaponAttackSpeed(int entityId)
        {
            ItemData? weapon = GetEquippedWeapon(entityId);
            if (weapon == null || weapon.Equipment == null)
                return 0f;

            return weapon.Equipment.WeaponStats.AttackSpeed;
        }

        /// <summary>
        /// 주어진 Mod가 무기 전용 스탯인지 판별.
        /// EquipHelper에서 StatModifier 등록 스킵 여부 결정에 사용.
        /// </summary>
        public static bool IsWeaponExclusiveMod(GE.ModEffectType effectType, GE.Stat targetStat)
        {
            // Added 데미지 계열은 모두 무기 전용
            if (effectType == GE.ModEffectType.AddedPhysDamage
                || effectType == GE.ModEffectType.AddedFireDamage
                || effectType == GE.ModEffectType.AddedIceDamage
                || effectType == GE.ModEffectType.AddedLightningDamage
                || effectType == GE.ModEffectType.AddedPoisonDamage)
            {
                return true;
            }

            // FlatStat/IncreasedStat 중 데미지/치명타/공속 관련 TargetStat
            if (effectType == GE.ModEffectType.FlatStat
                || effectType == GE.ModEffectType.IncreasedStat)
            {
                return IsWeaponExclusiveStat(targetStat);
            }

            return false;
        }

        private static bool IsWeaponExclusiveStat(GE.Stat stat)
        {
            return stat == GE.Stat.AttackMin
                || stat == GE.Stat.AttackMax
                || stat == GE.Stat.FireAttackMin
                || stat == GE.Stat.FireAttackMax
                || stat == GE.Stat.IceAttackMin
                || stat == GE.Stat.IceAttackMax
                || stat == GE.Stat.LightningAttackMin
                || stat == GE.Stat.LightningAttackMax
                || stat == GE.Stat.PoisonAttackMin
                || stat == GE.Stat.PoisonAttackMax
                || stat == GE.Stat.CriRate
                || stat == GE.Stat.AttackSpeed
                || stat == GE.Stat.AttackSpeedMul;
        }
    }
}
