using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬 엔티티마다 미리 계산해두는 공격 프로파일 캐시.
    /// 매 히트마다 Weapon/StatComponent/SkillTable/SkillStatBonusOnHit을 다시 조회·합산하지 않도록
    /// 5속성 Added min/max + 치명타·배율 상수를 미리 합산해 저장한다.
    /// System_SkillAttackProfileCalculation이 SkillAttackProfileDirtyTag를 감지해 갱신.
    /// 캐시가 없을 경우 DamageCalculator가 lazy build 한다.
    /// </summary>
    public struct SkillAttackProfileComponent
    {
        // === 5속성 Added 데미지 범위 (Weapon + Stat.FinalX + SkillTable.DamageX(DamageType일치) + OnHitBonus) ===
        public int PhysAddedMin, PhysAddedMax;
        public int FireAddedMin, FireAddedMax;
        public int IceAddedMin,  IceAddedMax;
        public int LightAddedMin, LightAddedMax;
        public int PoisonAddedMin, PoisonAddedMax;

        // === 치명타 ===
        public int   TotalCritRate;          // (Weapon CriRate or SkillTable.BaseCriRate) + Stat.FinalCriRate + OnHitBonus.CriRateBonus
        public float CritMultiplier;         // 1.5 + Stat.FinalCriDamage/100

        // === 배율 ===
        public float SkillDamageMul;         // Stat.FinalSkillDamage/100 (>0이면, 아니면 1)
        public float BaseDamageMul;          // SkillTable.BaseDamageMul/100

        // === 부수 효과 비율 ===
        public float LifeStealRatio;         // Stat.FinalLifeSteal/100

        // === UI/타이밍용 ===
        public float AttackSpeedPerSecond;   // WeaponAS × SkillTable.BaseAttackSpeedMul/100 × (1 + Stat.FinalAttackSpeed/100)

        // === 메타 ===
        public bool          IsAttackSkill;  // (SkillTable.Tags & Attack) != 0
        public GE.DamageType DamageType;     // SkillTable.DamageType (UI/상태이상용)
    }

    /// <summary>
    /// SkillAttackProfileComponent 재계산 요청 태그. System_SkillAttackProfileCalculation이 처리 후 제거.
    /// </summary>
    public struct SkillAttackProfileDirtyTag { }
}
