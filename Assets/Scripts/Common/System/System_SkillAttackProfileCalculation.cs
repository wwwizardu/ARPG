using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 스킬 엔티티의 SkillAttackProfileComponent 재계산 시스템.
    /// SkillAttackProfileDirtyTag가 있는 엔티티만 처리.
    /// Priority 55 — System_StatCalculation(50) 직후, 다음 시스템(이동/입력 등)보다 앞.
    /// 트리거: (1) CreateSkill 직후, (2) SkillStatBonusHelper.Rebuild 직후.
    /// 소유자의 StatComponent 재계산 시에는 System_StatCalculation이 BuildOwnedProfiles로 즉시 갱신.
    /// </summary>
    public class System_SkillAttackProfileCalculation : IUpdateSystem
    {
        public int Priority => 55;

        public void OnCreate()
        {
            Debug.Log("System_SkillAttackProfileCalculation Created");
        }

        public void OnReset()
        {
            Debug.Log("System_SkillAttackProfileCalculation Reset called");
        }

        public void OnUpdate(float inDeltaTime)
        {
            SparseSet<SkillAttackProfileDirtyTag> dirtyPool = AR.s.Component.GetComponentPool<SkillAttackProfileDirtyTag>();
            if (dirtyPool == null || dirtyPool.Count == 0)
                return;

            // 처리 도중 RemoveComponent로 풀이 변하므로 역순 순회로 안전 처리
            for (int i = dirtyPool.Count - 1; i >= 0; i--)
            {
                int skillEntityId = dirtyPool.GetEntityId(i);
                BuildProfile(skillEntityId);
                AR.s.Component.RemoveComponent<SkillAttackProfileDirtyTag>(skillEntityId);
            }
        }

        /// <summary>
        /// 소유자의 모든 스킬 공격 프로파일을 즉시 빌드/갱신.
        /// StatRecalculatedMessage처럼 같은 프레임에 최신 UI/데미지 값이 필요한 경로에서 사용.
        /// </summary>
        public static void BuildOwnedProfiles(int ownerEntityId)
        {
            AR.s.Entity.ForEachOwnedSkill(ownerEntityId, skillEntityId =>
            {
                BuildProfile(skillEntityId);
                AR.s.Component.RemoveComponent<SkillAttackProfileDirtyTag>(skillEntityId);
            });
        }

        /// <summary>
        /// SkillAttackProfileComponent를 빌드/갱신. DamageCalculator의 lazy build 경로에서도 호출됨.
        /// SkillComponent가 없거나 Table이 null이면 조용히 종료(스킬 아닌 엔티티에 태그가 잘못 붙은 경우 방어).
        /// </summary>
        public static void BuildProfile(int skillEntityId)
        {
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
                return;
            if (skill.Table == null)
                return;

            SkillTable table = skill.Table;
            int ownerEntityId = skill.OwnerEntityId;

            // 소유자 스탯 (없으면 0으로 채워서라도 진행 — UI/시뮬레이션 대비)
            AR.s.Component.TryGetComponent<StatComponent>(ownerEntityId, out var ownerStat);

            bool isAttackSkill = (table.Tags & GE.SkillTag.Attack) != 0;

            // 캐릭터 기본값 + 현재 스킬 태그에 맞는 글로벌 장비/버프 Modifier.
            // 무기 Base와 고유 강화는 아래 WeaponHelper 경로에서 별도로 더한다.
            SkillOffensiveStatSnapshot offensive = SkillOffensiveStatResolver.Resolve(
                ownerStat,
                AR.s.Data.GetModifiers(ownerEntityId),
                table.Tags);

            // 무기 스탯 (Attack 태그일 때만)
            int wPhysMin = 0, wPhysMax = 0;
            int wFireMin = 0, wFireMax = 0;
            int wIceMin = 0, wIceMax = 0;
            int wLightMin = 0, wLightMax = 0;
            int wPoisMin = 0, wPoisMax = 0;
            int weaponCriRate = 0;
            if (isAttackSkill)
            {
                WeaponHelper.GetWeaponDamage(ownerEntityId, GE.DamageType.Physics, out wPhysMin, out wPhysMax);
                WeaponHelper.GetWeaponDamage(ownerEntityId, GE.DamageType.Fire, out wFireMin, out wFireMax);
                WeaponHelper.GetWeaponDamage(ownerEntityId, GE.DamageType.Ice, out wIceMin, out wIceMax);
                WeaponHelper.GetWeaponDamage(ownerEntityId, GE.DamageType.Lightning, out wLightMin, out wLightMax);
                WeaponHelper.GetWeaponDamage(ownerEntityId, GE.DamageType.Poison, out wPoisMin, out wPoisMax);
                weaponCriRate = WeaponHelper.GetWeaponCriRate(ownerEntityId);
            }

            // SkillTable의 DamageMin/Max는 DamageType 속성에만 합산
            int tableMin = table.DamageMax > 0 ? table.DamageMin : 0;
            int tableMax = table.DamageMax > 0 ? table.DamageMax : 0;
            int tPhysMin = 0, tPhysMax = 0;
            int tFireMin = 0, tFireMax = 0;
            int tIceMin = 0, tIceMax = 0;
            int tLightMin = 0, tLightMax = 0;
            int tPoisMin = 0, tPoisMax = 0;
            switch (table.DamageType)
            {
                case GE.DamageType.Physics:   tPhysMin = tableMin;  tPhysMax = tableMax;  break;
                case GE.DamageType.Fire:      tFireMin = tableMin;  tFireMax = tableMax;  break;
                case GE.DamageType.Ice:       tIceMin = tableMin;   tIceMax = tableMax;   break;
                case GE.DamageType.Lightning: tLightMin = tableMin; tLightMax = tableMax; break;
                case GE.DamageType.Poison:    tPoisMin = tableMin;  tPoisMax = tableMax;  break;
            }

            // SkillStatBonusOnHitComponent (스킬북 페이지 효과)
            AR.s.Component.TryGetComponent<SkillStatBonusOnHitComponent>(skillEntityId, out var onHit);

            SkillAttackProfileComponent profile;

            profile.PhysAddedMin   = wPhysMin  + offensive.PhysMin      + tPhysMin  + onHit.PhysDamageAddBonusMin;
            profile.PhysAddedMax   = wPhysMax  + offensive.PhysMax      + tPhysMax  + onHit.PhysDamageAddBonusMax;
            profile.FireAddedMin   = wFireMin  + offensive.FireMin      + tFireMin  + onHit.FireDamageAddBonusMin;
            profile.FireAddedMax   = wFireMax  + offensive.FireMax      + tFireMax  + onHit.FireDamageAddBonusMax;
            profile.IceAddedMin    = wIceMin   + offensive.IceMin       + tIceMin   + onHit.IceDamageAddBonusMin;
            profile.IceAddedMax    = wIceMax   + offensive.IceMax       + tIceMax   + onHit.IceDamageAddBonusMax;
            profile.LightAddedMin  = wLightMin + offensive.LightningMin + tLightMin + onHit.LightningDamageAddBonusMin;
            profile.LightAddedMax  = wLightMax + offensive.LightningMax + tLightMax + onHit.LightningDamageAddBonusMax;
            profile.PoisonAddedMin = wPoisMin  + offensive.PoisonMin    + tPoisMin  + onHit.PoisonDamageAddBonusMin;
            profile.PoisonAddedMax = wPoisMax  + offensive.PoisonMax    + tPoisMax  + onHit.PoisonDamageAddBonusMax;

            // 속성별 피해 % 증가는 합계 전체에 곱한다 (MONSTER_AFFIX_DESIGN.md §6.2). 증가가 없으면 값이 그대로다.
            profile.PhysAddedMin   = ApplyDamageIncrease(profile.PhysAddedMin,   offensive.PhysDamageIncrease);
            profile.PhysAddedMax   = ApplyDamageIncrease(profile.PhysAddedMax,   offensive.PhysDamageIncrease);
            profile.FireAddedMin   = ApplyDamageIncrease(profile.FireAddedMin,   offensive.FireDamageIncrease);
            profile.FireAddedMax   = ApplyDamageIncrease(profile.FireAddedMax,   offensive.FireDamageIncrease);
            profile.IceAddedMin    = ApplyDamageIncrease(profile.IceAddedMin,    offensive.IceDamageIncrease);
            profile.IceAddedMax    = ApplyDamageIncrease(profile.IceAddedMax,    offensive.IceDamageIncrease);
            profile.LightAddedMin  = ApplyDamageIncrease(profile.LightAddedMin,  offensive.LightningDamageIncrease);
            profile.LightAddedMax  = ApplyDamageIncrease(profile.LightAddedMax,  offensive.LightningDamageIncrease);
            profile.PoisonAddedMin = ApplyDamageIncrease(profile.PoisonAddedMin, offensive.PoisonDamageIncrease);
            profile.PoisonAddedMax = ApplyDamageIncrease(profile.PoisonAddedMax, offensive.PoisonDamageIncrease);

            int baseCritRate = isAttackSkill == true ? weaponCriRate : table.BaseCriRate;
            profile.TotalCritRate = Mathf.Clamp(
                (baseCritRate + offensive.CharacterCritFlat + onHit.CriRateBonus)
                * (1f + offensive.CritIncrease / 100f),
                0f,
                100f);
            profile.CritMultiplier = 1.5f + ownerStat.FinalCriDamage / 100f;

            profile.SkillDamageMul = ownerStat.FinalSkillDamage > 0 ? ownerStat.FinalSkillDamage / 100f : 1f;
            profile.BaseDamageMul  = table.BaseDamageMul / 100f;
            profile.LifeStealRatio = (ownerStat.FinalLifeSteal + onHit.LifeStealBonus) / 100f;

            // 주문에는 무기 공격속도를 적용하거나 공격속도 값을 만들지 않는다.
            profile.AttackSpeedPerSecond = 0f;
            if (isAttackSkill == true)
            {
                float weaponAS = WeaponHelper.GetWeaponAttackSpeed(ownerEntityId);
                if (weaponAS <= 0f)
                    weaponAS = 1f;

                float skillSpeedMul = table.BaseAttackSpeedMul > 0
                    ? table.BaseAttackSpeedMul / 100f
                    : 1f;
                float globalSpeedMul = 1f + offensive.AttackSpeedIncrease / 100f;
                profile.AttackSpeedPerSecond = Mathf.Max(0.1f, weaponAS * skillSpeedMul * globalSpeedMul);
            }

            profile.IsAttackSkill  = isAttackSkill;
            profile.DamageType     = table.DamageType;

            AR.s.Component.SetComponent(skillEntityId, profile);
        }

        private static int ApplyDamageIncrease(int value, int increasePercent)
        {
            if (increasePercent == 0)
                return value;

            return Mathf.Max(0, Mathf.RoundToInt(value * (1f + increasePercent / 100f)));
        }
    }
}
