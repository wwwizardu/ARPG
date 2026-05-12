using ARPG.Component;
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
            EntityIdHelper.ForEachOwnedSkill(ownerEntityId, skillEntityId =>
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

            profile.PhysAddedMin   = wPhysMin  + ownerStat.FinalAttackMin          + tPhysMin  + onHit.PhysDamageAddBonusMin;
            profile.PhysAddedMax   = wPhysMax  + ownerStat.FinalAttackMax          + tPhysMax  + onHit.PhysDamageAddBonusMax;
            profile.FireAddedMin   = wFireMin  + ownerStat.FinalFireAttackMin      + tFireMin  + onHit.FireDamageAddBonusMin;
            profile.FireAddedMax   = wFireMax  + ownerStat.FinalFireAttackMax      + tFireMax  + onHit.FireDamageAddBonusMax;
            profile.IceAddedMin    = wIceMin   + ownerStat.FinalIceAttackMin       + tIceMin   + onHit.IceDamageAddBonusMin;
            profile.IceAddedMax    = wIceMax   + ownerStat.FinalIceAttackMax       + tIceMax   + onHit.IceDamageAddBonusMax;
            profile.LightAddedMin  = wLightMin + ownerStat.FinalLightningAttackMin + tLightMin + onHit.LightningDamageAddBonusMin;
            profile.LightAddedMax  = wLightMax + ownerStat.FinalLightningAttackMax + tLightMax + onHit.LightningDamageAddBonusMax;
            profile.PoisonAddedMin = wPoisMin  + ownerStat.FinalPoisonAttackMin    + tPoisMin  + onHit.PoisonDamageAddBonusMin;
            profile.PoisonAddedMax = wPoisMax  + ownerStat.FinalPoisonAttackMax    + tPoisMax  + onHit.PoisonDamageAddBonusMax;

            int baseCritRate = isAttackSkill ? weaponCriRate : table.BaseCriRate;
            profile.TotalCritRate  = baseCritRate + ownerStat.FinalCriRate + onHit.CriRateBonus;
            profile.CritMultiplier = 1.5f + ownerStat.FinalCriDamage / 100f;

            profile.SkillDamageMul = ownerStat.FinalSkillDamage > 0 ? ownerStat.FinalSkillDamage / 100f : 1f;
            profile.BaseDamageMul  = table.BaseDamageMul / 100f;
            profile.LifeStealRatio = ownerStat.FinalLifeSteal / 100f;

            // UI/타이밍용 공격속도: 무기 AS × Skill.BaseAttackSpeedMul × (1 + Stat.FinalAttackSpeed%)
            float weaponAS = WeaponHelper.GetWeaponAttackSpeed(ownerEntityId);
            if (weaponAS <= 0f) weaponAS = 1f;
            float skillSpeedMul = table.BaseAttackSpeedMul > 0 ? table.BaseAttackSpeedMul / 100f : 1f;
            profile.AttackSpeedPerSecond = weaponAS * skillSpeedMul * (1f + ownerStat.FinalAttackSpeed / 100f);

            profile.IsAttackSkill  = isAttackSkill;
            profile.DamageType     = table.DamageType;

            AR.s.Component.SetComponent(skillEntityId, profile);
        }
    }
}
