#nullable enable
using ARPG.Data;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>합성 무기의 속성별 피해 범위. 쌍수 합성(× 0.7) 결과를 반올림하지 않고 담는다.</summary>
    public struct CombinedDamageRange
    {
        public float Min;
        public float Max;

        public float Average => (Min + Max) * 0.5f;
    }

    /// <summary>
    /// 공격 계산에 쓰는 무기 기본 능력 (Base + 무기 고유 강화). 무기 두 개면 쌍수 합성 결과다.
    /// 일반 장비 옵션(Implicit/Prefix/Postfix)은 포함하지 않는다.
    /// </summary>
    public struct CombinedWeaponStats
    {
        public CombinedDamageRange Physics;
        public CombinedDamageRange Fire;
        public CombinedDamageRange Ice;
        public CombinedDamageRange Lightning;
        public CombinedDamageRange Poison;
        public float CriRate;       // % (예: 7.5 = 7.5%)
        public float AttackSpeed;   // 초당 공격 횟수. 무기가 없으면 0
        public int WeaponCount;     // 0~2
    }

    /// <summary>
    /// 장착 무기의 기본 능력 조회 유틸리티.
    /// Base + 무기 고유 강화 결과만 EquipmentData.WeaponStats에서 읽으며 일반 장비 옵션은 포함하지 않는다.
    /// 실제 공격 프로필(System_SkillAttackProfileCalculation)과 NPC 전투력(NpcPowerEvaluator)이 같은 합성 규칙을 쓴다.
    /// </summary>
    public static class WeaponHelper
    {
        /// <summary>쌍수 무기 피해 계수 (WEAPON_PROGRESSION_DESIGN §쌍수 계산 초기안).</summary>
        public const float DualWieldDamageFactor = 0.7f;

        /// <summary>엔티티의 합성 무기 능력. 플레이어와 저장 데이터가 있는 NPC를 지원한다.</summary>
        public static CombinedWeaponStats GetCombinedWeaponStats(int entityId)
        {
            return GetCombinedWeaponStats(GetEquipment(entityId));
        }

        /// <summary>
        /// 장비 배열의 합성 무기 능력 (WEAPON_PROGRESSION_DESIGN §쌍수 계산 초기안, 2026-10-05 확정).
        /// - 양손 슬롯 모두 무기: 속성별 최소·최대 피해 = (R + L) × 0.7, 공속·기본 치명타 = 두 무기의 평균.
        /// - 무기 하나(다른 손이 비었거나 방패): 그 무기 수치 그대로.
        /// 방패는 무기로 보지 않는다. 피해는 반올림하지 않는다 — 최종 반올림은 공격 프로필에서 한 번만 한다.
        /// </summary>
        public static CombinedWeaponStats GetCombinedWeaponStats(ItemData?[]? equip)
        {
            CombinedWeaponStats result = new CombinedWeaponStats();
            if (equip == null || equip.Length <= (int)GE.EquipSlotType.WeaponLeft)
                return result;

            ItemData? right = AsWeapon(equip[(int)GE.EquipSlotType.WeaponRight]);
            ItemData? left = AsWeapon(equip[(int)GE.EquipSlotType.WeaponLeft]);
            if (right == null && left == null)
                return result;

            if (right == null || left == null)
            {
                WeaponStatCache single = (right ?? left)!.Equipment!.WeaponStats;
                result.Physics = ToRange(single.Physics);
                result.Fire = ToRange(single.Fire);
                result.Ice = ToRange(single.Ice);
                result.Lightning = ToRange(single.Lightning);
                result.Poison = ToRange(single.Poison);
                result.CriRate = single.CriRate;
                result.AttackSpeed = single.AttackSpeed;
                result.WeaponCount = 1;
                return result;
            }

            WeaponStatCache r = right.Equipment!.WeaponStats;
            WeaponStatCache l = left.Equipment!.WeaponStats;
            result.Physics = Combine(r.Physics, l.Physics);
            result.Fire = Combine(r.Fire, l.Fire);
            result.Ice = Combine(r.Ice, l.Ice);
            result.Lightning = Combine(r.Lightning, l.Lightning);
            result.Poison = Combine(r.Poison, l.Poison);
            result.CriRate = (r.CriRate + l.CriRate) * 0.5f;
            result.AttackSpeed = AverageAttackSpeed(r.AttackSpeed, l.AttackSpeed);
            result.WeaponCount = 2;
            return result;
        }

        /// <summary>합성 무기 공격 속도(초당 공격 횟수). 무기 없으면 0.</summary>
        public static float GetWeaponAttackSpeed(int entityId)
        {
            return GetCombinedWeaponStats(entityId).AttackSpeed;
        }

        private static ItemData?[]? GetEquipment(int entityId)
        {
            if (entityId == AR.s.Data.CurrentPlayerEntityId)
                return AR.s.Data.Player._inventoryEquip;
            return AR.s.Npc?.GetSaveData(entityId)?.InventoryEquip;
        }

        private static ItemData? AsWeapon(ItemData? item)
        {
            return item?.Equipment != null && item.Equipment.EquipType == GE.EquipmentTypeMask.Weapon ? item : null;
        }

        private static CombinedDamageRange Combine(DamageRange right, DamageRange left)
        {
            return new CombinedDamageRange
            {
                Min = (right.Min + left.Min) * DualWieldDamageFactor,
                Max = (right.Max + left.Max) * DualWieldDamageFactor,
            };
        }

        private static CombinedDamageRange ToRange(DamageRange range)
        {
            return new CombinedDamageRange { Min = range.Min, Max = range.Max };
        }

        // 공속 값이 없는 무기(0)는 평균에서 빼서 다른 무기의 공속을 깎지 않는다
        private static float AverageAttackSpeed(float right, float left)
        {
            if (right > 0f && left > 0f)
                return (right + left) * 0.5f;
            return right > left ? right : left;
        }
    }
}
