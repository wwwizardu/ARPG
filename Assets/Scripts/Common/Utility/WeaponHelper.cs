#nullable enable
using ARPG.Data;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 장착 무기의 기본 능력 조회 유틸리티.
    /// Base + 무기 고유 강화 결과만 EquipmentData.WeaponStats에서 읽으며 일반 장비 옵션은 포함하지 않는다.
    /// </summary>
    public static class WeaponHelper
    {
        /// <summary>
        /// 엔티티의 장착 무기 반환. 플레이어와 저장 데이터가 있는 NPC를 지원한다.
        /// WeaponRight(메인) 우선 → WeaponLeft(쌍수 보조). 슬롯에 방패가 들어있으면 무기로 인식하지 않는다.
        /// </summary>
        public static ItemData? GetEquippedWeapon(int entityId)
        {
            ItemData?[]? equip = null;
            if (entityId == AR.s.Data.CurrentPlayerEntityId)
            {
                equip = AR.s.Data.Player._inventoryEquip;
            }
            else
            {
                equip = AR.s.Npc?.GetSaveData(entityId)?.InventoryEquip;
            }

            if (equip == null || equip.Length <= (int)GE.EquipSlotType.WeaponLeft)
                return null;

            ItemData? rightWeapon = equip[(int)GE.EquipSlotType.WeaponRight];
            if (rightWeapon?.Equipment != null && rightWeapon.Equipment.EquipType == GE.EquipmentTypeMask.Weapon)
                return rightWeapon;

            ItemData? leftWeapon = equip[(int)GE.EquipSlotType.WeaponLeft];
            if (leftWeapon?.Equipment != null && leftWeapon.Equipment.EquipType == GE.EquipmentTypeMask.Weapon)
                return leftWeapon;

            return null;
        }

        /// <summary>
        /// 특정 속성의 강화 반영 무기 기본 데미지 범위.
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
        /// 강화가 반영된 무기 기본 치명타 확률(%). 무기 없으면 0.
        /// </summary>
        public static int GetWeaponCriRate(int entityId)
        {
            ItemData? weapon = GetEquippedWeapon(entityId);
            if (weapon == null || weapon.Equipment == null)
                return 0;

            return weapon.Equipment.WeaponStats.CriRate;
        }

        /// <summary>
        /// 강화가 반영된 무기 기본 공격 속도(초당 공격 횟수). 무기 없으면 0.
        /// </summary>
        public static float GetWeaponAttackSpeed(int entityId)
        {
            ItemData? weapon = GetEquippedWeapon(entityId);
            if (weapon == null || weapon.Equipment == null)
                return 0f;

            return weapon.Equipment.WeaponStats.AttackSpeed;
        }

    }
}
