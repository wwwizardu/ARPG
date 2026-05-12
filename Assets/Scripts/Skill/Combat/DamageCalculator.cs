using UnityEngine;
using ARPG.Component;
using ARPG.Systems;
using ARPG.Tables;
using ARPG.Utility;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// UI용 속성별 예상 데미지 범위
    /// </summary>
    public struct EstimatedDamage
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
        public int TotalMin;
        public int TotalMax;
    }

    /// <summary>
    /// 데미지 계산 결과
    /// </summary>
    public struct DamageResult
    {
        public float FinalDamage;            // 최종 데미지 (모든 속성 합산)
        public float PhysDamage;             // 물리 데미지 (저항 적용 후)
        public float FireDamage;             // 화염 데미지 (저항 적용 후)
        public float IceDamage;              // 냉기 데미지 (저항 적용 후)
        public float LightningDamage;        // 번개 데미지 (저항 적용 후)
        public float PoisonDamage;           // 독 데미지 (저항 적용 후)
        public bool IsCritical;              // 치명타 여부
        public bool IsEvaded;                // 회피 여부
        public bool IsBlocked;               // 막기 여부
        public float LifeStealAmount;        // 흡혈량
        public float ThornsDamage;           // 반사 데미지
        public GlobalEnum.DamageType DamageType; // 스킬 데미지 타입 (UI/상태이상용)
    }

    /// <summary>
    /// 데미지 계산 유틸리티
    /// 속성별 독립 계산 후 합산하는 데미지 공식
    ///
    /// 사용 예시:
    /// DamageResult result = DamageCalculator.Calculate(attackerId, targetId, skillData);
    /// DamageCalculator.ApplyDamageResult(attackerId, targetId, result);
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// 스킬 데미지 배율 (공통)
        /// </summary>
        private static float GetSkillDamageMultiplier(StatComponent stat)
        {
            return stat.FinalSkillDamage > 0 ? stat.FinalSkillDamage / 100f : 1f;
        }

        /// <summary>
        /// 치명타 배율 (공통)
        /// 기본 1.5배 + FinalCriDamage/100
        /// 예: FinalCriDamage 10 → 1.5 + 0.1 = 1.6배
        /// </summary>
        private static float GetCritMultiplier(StatComponent stat)
        {
            return 1.5f + stat.FinalCriDamage / 100f;
        }

        /// <summary>
        /// 저항 감소율 (공통) - resistance / (resistance + 100)
        /// resistance 100 = 50% 감소, 200 = 66.6% 감소
        /// </summary>
        public static float GetResistanceReduction(int resistance)
        {
            if (resistance <= 0) return 0f;
            return resistance / (resistance + 100f);
        }

        /// <summary>
        /// DamageDefenseCacheComponent를 조회. 없으면 즉시 빌드(lazy).
        /// 정상 흐름에서는 System_StatCalculation이 항상 갱신해두기 때문에 lazy build는 폴백/디버그용.
        /// </summary>
        private static DamageDefenseCacheComponent GetOrBuildDefenseCache(int targetId, StatComponent targetStat)
        {
            if (AR.s.Component.TryGetComponent<DamageDefenseCacheComponent>(targetId, out var cache))
                return cache;

            Debug.LogWarning($"[DamageCalculator] DamageDefenseCache lazy rebuild - invalidation may have been missed (TargetId: {targetId})");
            System_StatCalculation.BuildDefenseCache(targetId, targetStat);
            AR.s.Component.TryGetComponent(targetId, out cache);
            return cache;
        }

        /// <summary>
        /// UI용 특정 스킬 기준 예상 데미지 범위 (저항 미적용, 치명타 기대값 반영).
        /// SkillAttackProfileComponent 캐시를 사용 — 무기/Stat/SkillTable/OnHitBonus 모두 자동 반영.
        /// 캐시가 없으면 lazy build.
        /// </summary>
        public static EstimatedDamage CalculateForSkill(int skillEntityId)
        {
            SkillAttackProfileComponent profile = GetOrBuildProfile(skillEntityId);

            // 기대값 배율: BaseDamageMul × SkillDamageMul × (1 + critRate% × (critMul-1))
            int   clampedCritRate = Mathf.Clamp(profile.TotalCritRate, 0, 100);
            float critExpected = 1f + (clampedCritRate / 100f) * (profile.CritMultiplier - 1f);
            float m = profile.BaseDamageMul * profile.SkillDamageMul * critExpected;

            EstimatedDamage result;
            result.PhysMin = Mathf.RoundToInt(profile.PhysAddedMin * m);
            result.PhysMax = Mathf.RoundToInt(profile.PhysAddedMax * m);
            result.FireMin = Mathf.RoundToInt(profile.FireAddedMin * m);
            result.FireMax = Mathf.RoundToInt(profile.FireAddedMax * m);
            result.IceMin = Mathf.RoundToInt(profile.IceAddedMin * m);
            result.IceMax = Mathf.RoundToInt(profile.IceAddedMax * m);
            result.LightningMin = Mathf.RoundToInt(profile.LightAddedMin * m);
            result.LightningMax = Mathf.RoundToInt(profile.LightAddedMax * m);
            result.PoisonMin = Mathf.RoundToInt(profile.PoisonAddedMin * m);
            result.PoisonMax = Mathf.RoundToInt(profile.PoisonAddedMax * m);
            result.TotalMin = result.PhysMin + result.FireMin + result.IceMin + result.LightningMin + result.PoisonMin;
            result.TotalMax = result.PhysMax + result.FireMax + result.IceMax + result.LightningMax + result.PoisonMax;
            return result;
        }

        /// <summary>
        /// UI용 예상 치명타 확률 (%, 0~100 클램프).
        /// profile.TotalCritRate에 (무기/스킬 BaseCriRate + Stat.FinalCriRate + OnHitBonus.CriRateBonus) 가 합산되어 있음.
        /// </summary>
        public static int EstimateCritRate(int skillEntityId)
        {
            SkillAttackProfileComponent profile = GetOrBuildProfile(skillEntityId);
            return Mathf.Clamp(profile.TotalCritRate, 0, 100);
        }

        /// <summary>
        /// UI용 예상 공격 속도 (초당 공격 횟수).
        /// profile.AttackSpeedPerSecond는 무기 AS × Skill.BaseAttackSpeedMul × (1 + Stat.FinalAttackSpeed%).
        /// </summary>
        public static float EstimateAttackSpeed(int skillEntityId)
        {
            SkillAttackProfileComponent profile = GetOrBuildProfile(skillEntityId);
            return profile.AttackSpeedPerSecond;
        }

        /// <summary>
        /// UI용 예상 데미지 범위 (타겟 없이 공격자 스탯만으로 계산)
        /// 속성별 데미지 + 합산, 치명타 기대값 반영, 저항 미적용
        /// </summary>
        public static EstimatedDamage Calculate(StatComponent attackerStat)
        {
            float mul = GetSkillDamageMultiplier(attackerStat);
            float critRate = Mathf.Clamp(attackerStat.FinalCriRate, 0, 100) / 100f;
            float critExpected = 1f + critRate * (GetCritMultiplier(attackerStat) - 1f);
            float m = mul * critExpected;

            EstimatedDamage result;
            result.PhysMin = Mathf.RoundToInt(attackerStat.FinalAttackMin * m);
            result.PhysMax = Mathf.RoundToInt(attackerStat.FinalAttackMax * m);
            result.FireMin = Mathf.RoundToInt(attackerStat.FinalFireAttackMin * m);
            result.FireMax = Mathf.RoundToInt(attackerStat.FinalFireAttackMax * m);
            result.IceMin = Mathf.RoundToInt(attackerStat.FinalIceAttackMin * m);
            result.IceMax = Mathf.RoundToInt(attackerStat.FinalIceAttackMax * m);
            result.LightningMin = Mathf.RoundToInt(attackerStat.FinalLightningAttackMin * m);
            result.LightningMax = Mathf.RoundToInt(attackerStat.FinalLightningAttackMax * m);
            result.PoisonMin = Mathf.RoundToInt(attackerStat.FinalPoisonAttackMin * m);
            result.PoisonMax = Mathf.RoundToInt(attackerStat.FinalPoisonAttackMax * m);
            result.TotalMin = result.PhysMin + result.FireMin + result.IceMin + result.LightningMin + result.PoisonMin;
            result.TotalMax = result.PhysMax + result.FireMax + result.IceMax + result.LightningMax + result.PoisonMax;
            return result;
        }

        /// <summary>
        /// 데미지 계산 메인 메서드
        /// SkillAttackProfileComponent(공격자측 캐시) + DamageDefenseCacheComponent(타겟측 캐시) 기반.
        /// Weapon/Stat/SkillTable/SkillStatBonusOnHit 합산은 캐시 빌드 시 1회만 수행되고
        /// 매 히트는 Random.Range × 5 + 곱셈 몇 번으로 끝난다.
        /// </summary>
        public static DamageResult Calculate(int skillEntityId, int attackerId, int targetId, SkillTable skillData)
        {
            DamageResult result = new DamageResult
            {
                DamageType = skillData.DamageType
            };

            // 공중 무적 판정: 타겟이 일정 높이 이상 점프 중이면 회피 처리
            if (AR.s.Component.TryGetComponent<JumpComponent>(targetId, out var jump) == true)
            {
                if (jump.Height >= System_Jump.InvincibleHeight)
                {
                    result.IsEvaded = true;
                    return result;
                }
            }

            // 타겟 스탯 (Thorns + DefenseCache lazy build 폴백용)
            if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat) == false)
            {
                Debug.LogWarning($"[DamageCalculator] Target StatComponent not found - TargetId: {targetId}");
                return result;
            }

            // 공격 프로파일 (Weapon + Stat.FinalX + SkillTable + OnHitBonus 모두 합산된 캐시)
            SkillAttackProfileComponent profile = GetOrBuildProfile(skillEntityId);

            // ========== 1~2-2단계: 속성별 Added 데미지 (캐시에서 한 번에) ==========
            float physDamage      = Random.Range(profile.PhysAddedMin,   profile.PhysAddedMax + 1);
            float fireDamage      = Random.Range(profile.FireAddedMin,   profile.FireAddedMax + 1);
            float iceDamage       = Random.Range(profile.IceAddedMin,    profile.IceAddedMax + 1);
            float lightningDamage = Random.Range(profile.LightAddedMin,  profile.LightAddedMax + 1);
            float poisonDamage    = Random.Range(profile.PoisonAddedMin, profile.PoisonAddedMax + 1);

            // ========== 2-3 + 3단계: BaseDamageMul × SkillDamageMul ==========
            float damageMul = profile.BaseDamageMul * profile.SkillDamageMul;
            physDamage      *= damageMul;
            fireDamage      *= damageMul;
            iceDamage       *= damageMul;
            lightningDamage *= damageMul;
            poisonDamage    *= damageMul;

            // ========== 4단계: 치명타 판정 ==========
            if (Random.Range(0f, 100f) < profile.TotalCritRate)
            {
                float critMul = profile.CritMultiplier;
                physDamage      *= critMul;
                fireDamage      *= critMul;
                iceDamage       *= critMul;
                lightningDamage *= critMul;
                poisonDamage    *= critMul;
                result.IsCritical = true;
            }

            // ========== 5단계: 속성별 저항 감소 (DamageDefenseCache 사용) ==========
            DamageDefenseCacheComponent defenseCache = GetOrBuildDefenseCache(targetId, targetStat);
            physDamage      *= defenseCache.PhysReductionMul;
            fireDamage      *= defenseCache.FireReductionMul;
            iceDamage       *= defenseCache.IceReductionMul;
            lightningDamage *= defenseCache.LightningReductionMul;
            poisonDamage    *= defenseCache.PoisonReductionMul;

            // ========== 6단계: 합산 ==========
            float totalDamage = physDamage + fireDamage + iceDamage + lightningDamage + poisonDamage;

            // ========== 7단계: 회피/막기 판정 (합산 후 적용) ==========
            if (Random.Range(0f, 100f) < defenseCache.EvasionRate)
            {
                result.IsEvaded = true;
                result.FinalDamage = 0;
                return result;
            }

            if (Random.Range(0f, 100f) < defenseCache.BlockChance)
            {
                result.IsBlocked = true;
                totalDamage *= (1f - defenseCache.BlockReductionMul);
            }

            // ========== 8단계: 최소 데미지 보장 ==========
            totalDamage = Mathf.Max(totalDamage, 1f);

            result.FinalDamage = totalDamage;
            result.PhysDamage = physDamage;
            result.FireDamage = fireDamage;
            result.IceDamage = iceDamage;
            result.LightningDamage = lightningDamage;
            result.PoisonDamage = poisonDamage;

            // ========== 특수 효과 계산 ==========
            result.LifeStealAmount = totalDamage * profile.LifeStealRatio;
            result.ThornsDamage = targetStat.FinalThorns;

            return result;
        }

        /// <summary>
        /// SkillAttackProfileComponent를 조회. 없으면 즉시 빌드(lazy).
        /// 정상 흐름에서는 CreateSkill/StatRecalc/SkillBookChange 시 dirty 마크되어 시스템이 갱신해두기 때문에
        /// lazy build는 폴백/디버그용. 발생하면 invalidation 누락 가능성을 LogWarning으로 보고.
        /// </summary>
        private static SkillAttackProfileComponent GetOrBuildProfile(int skillEntityId)
        {
            if (AR.s.Component.TryGetComponent<SkillAttackProfileComponent>(skillEntityId, out var profile))
                return profile;

            Debug.LogWarning($"[DamageCalculator] SkillAttackProfile lazy rebuild - invalidation may have been missed (SkillEntityId: {skillEntityId})");
            System_SkillAttackProfileCalculation.BuildProfile(skillEntityId);
            AR.s.Component.TryGetComponent(skillEntityId, out profile);
            return profile;
        }

        /// <summary>
        /// 데미지 결과를 엔티티에 적용
        /// </summary>
        public static void ApplyDamageResult(int skillEntityId, int attackerId, int targetId, DamageResult result)
        {
            // ========== 회피 처리 ==========
            if (result.IsEvaded)
            {
                Debug.Log($"[DamageCalculator] 회피! TargetId: {targetId}");

                if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var evadeStat))
                {
                    AR.s.Message.SendToEntity(new Message.DamageMessage
                    {
                        TargetEntityId = targetId,
                        DamageAmount = 0,
                        AttackerEntityId = attackerId,
                        DamageType = result.DamageType,
                        IsCritical = false,
                        IsEvaded = true,
                        IsBlocked = false,
                        CurrentHp = evadeStat.CurrentHp,
                        MaxHp = evadeStat.FinalMaxHp
                    });
                }

                return;
            }

            // ========== 타겟 HP 감소 ==========
            if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat))
            {
                int damage = Mathf.RoundToInt(result.FinalDamage);
                int newHp = Mathf.Max(0, targetStat.CurrentHp - damage);
                targetStat.SetCurrentHp(targetId, newHp);
                AR.s.Component.SetComponent(targetId, targetStat);

                Debug.Log($"[DamageCalculator] 데미지 적용 - Target: {targetId}, Total: {damage} (Phys:{result.PhysDamage:F0} Fire:{result.FireDamage:F0} Ice:{result.IceDamage:F0} Light:{result.LightningDamage:F0} Poison:{result.PoisonDamage:F0}), Crit: {result.IsCritical}, Block: {result.IsBlocked}, HP: {newHp}/{targetStat.FinalMaxHp}");
            }

            // 토템·소환물의 흡혈/반사는 실제 공격 주체(attackerId)에게 적용한다.
            // 원 시전자 추적이 필요한 보상/경험치 계열은 CasterLinkComponent를 별도 조회해서 처리한다.

            // ========== 생명력 흡수 ==========
            if (result.LifeStealAmount > 0f)
            {
                if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat))
                {
                    int healAmount = Mathf.RoundToInt(result.LifeStealAmount);
                    int newHp = Mathf.Min(attackerStat.FinalMaxHp, attackerStat.CurrentHp + healAmount);
                    attackerStat.SetCurrentHp(attackerId, newHp);
                    AR.s.Component.SetComponent(attackerId, attackerStat);
                }
            }

            // ========== 반사 데미지 ==========
            if (result.ThornsDamage > 0f)
            {
                if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat))
                {
                    int thornsDamage = Mathf.RoundToInt(result.ThornsDamage);
                    int newHp = Mathf.Max(0, attackerStat.CurrentHp - thornsDamage);
                    attackerStat.SetCurrentHp(attackerId, newHp);
                    AR.s.Component.SetComponent(attackerId, attackerStat);
                }
            }

            // ========== 데미지 메시지 전송 (UI 업데이트용) ==========
            AR.s.Message.SendToEntity(new Message.DamageMessage
            {
                TargetEntityId = targetId,
                DamageAmount = Mathf.RoundToInt(result.FinalDamage),
                AttackerEntityId = attackerId,
                DamageType = result.DamageType,
                IsCritical = result.IsCritical,
                IsEvaded = false,
                IsBlocked = result.IsBlocked,
                CurrentHp = targetStat.CurrentHp,
                MaxHp = targetStat.FinalMaxHp
            });

            // ========== 상태이상 독립 판정 (속성별) ==========
            if (result.IsEvaded == false)
            {
                ApplyStatusEffects(skillEntityId, attackerId, targetId, result);
            }
        }

        /// <summary>
        /// 속성별 독립 상태이상 판정
        /// 데미지가 존재하는 각 속성에 대해 개별적으로 상태이상 발동
        /// </summary>
        private static void ApplyStatusEffects(int skillEntityId, int attackerId, int targetId, DamageResult result)
        {
            if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat) == false)
                return;

            // 화염 데미지 > 0 → 점화 판정
            if (result.FireDamage > 0f)
            {
                ApplyIgnite(skillEntityId, attackerId, targetId, attackerStat, result.FireDamage);
            }

            // 냉기 데미지 > 0 → 냉기 자동 적용
            if (result.IceDamage > 0f)
            {
                ApplyChill(targetId);
            }

            // 독 데미지 > 0 → 중독 자동 적용
            if (result.PoisonDamage > 0f)
            {
                ApplyPoison(targetId, result.PoisonDamage);
            }

            // 물리 데미지 > 0 → 출혈 판정
            if (result.PhysDamage > 0f)
            {
                ApplyBleeding(skillEntityId, targetId, attackerStat, result.PhysDamage);
            }
        }

        /// <summary>
        /// 출혈 발동 (물리 DoT)
        /// </summary>
        private static void ApplyBleeding(int skillEntityId, int targetId, StatComponent attackerStat, float physDamage)
        {
            int bloodingRate = attackerStat.FinalBloodingRate + 50;
            if (AR.s.Component.TryGetComponent<SkillStatBonusOnHitComponent>(skillEntityId, out var onHit))
                bloodingRate += onHit.BloodingRateBonus;
            if (Random.Range(0, 100) < bloodingRate)
            {
                int bloodingDamage = Mathf.FloorToInt(physDamage * 0.3f);

                int bloodingBuffTableId = 1;
                float bloodingDuration = 5f;

                int buffEntityId = Utility.BuffHelper.AddBuff(targetId, bloodingBuffTableId, bloodingDuration);

                if (buffEntityId != -1 && AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
                {
                    buff.TickDamage = bloodingDamage;
                    AR.s.Component.SetComponent(buffEntityId, buff);

                    Debug.Log($"[DamageCalculator] 출혈 발동 - Target: {targetId}, PhysDamage: {physDamage:F0}, BloodingDamage: {bloodingDamage}");
                }
            }
        }

        /// <summary>
        /// 점화 발동 (화염 DoT, 스택 가능)
        /// </summary>
        private static void ApplyIgnite(int skillEntityId, int attackerId, int targetId, StatComponent attackerStat, float fireDamage)
        {
            int igniteRate = attackerStat.FinalIgniteRate;
            if (AR.s.Component.TryGetComponent<SkillStatBonusOnHitComponent>(skillEntityId, out var onHit))
                igniteRate += onHit.IgniteRateBonus;
            if (Random.Range(0, 100) < igniteRate)
            {
                int totalIgniteDamage = Mathf.FloorToInt(fireDamage * 0.5f);
                int tickDamage = totalIgniteDamage / 4;

                int igniteBuffId = 2;
                float igniteDuration = 4f;

                int buffEntityId = Utility.BuffHelper.AddBuff(targetId, igniteBuffId, igniteDuration);

                if (buffEntityId != -1 && AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
                {
                    buff.TickDamage = tickDamage;
                    AR.s.Component.SetComponent(buffEntityId, buff);
                }

                Debug.Log($"[DamageCalculator] 점화 발동 - Target: {targetId}, FireDamage: {fireDamage:F0}, TickDamage: {tickDamage}/초");
            }
        }

        /// <summary>
        /// 냉기 적용 (이동/공격 속도 감소, 자동 발동)
        /// </summary>
        private static void ApplyChill(int targetId)
        {
            int chillBuffId = 3;
            float chillDuration = 2f;

            Utility.BuffHelper.AddBuff(targetId, chillBuffId, chillDuration);
        }

        /// <summary>
        /// 중독 적용 (독 DoT + HP 재생 감소, 자동 발동, 스택 가능)
        /// </summary>
        private static void ApplyPoison(int targetId, float poisonDamage)
        {
            int totalPoisonDamage = Mathf.FloorToInt(poisonDamage * 0.2f);
            int tickDamage = totalPoisonDamage / 10;

            int poisonBuffId = 5;
            float poisonDuration = 10f;

            int buffEntityId = Utility.BuffHelper.AddBuff(targetId, poisonBuffId, poisonDuration);

            if (buffEntityId != -1 && AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
            {
                buff.TickDamage = tickDamage;
                AR.s.Component.SetComponent(buffEntityId, buff);
            }

            Debug.Log($"[DamageCalculator] 중독 적용 - Target: {targetId}, PoisonDamage: {poisonDamage:F0}, TickDamage: {tickDamage}/초");
        }
    }
}
