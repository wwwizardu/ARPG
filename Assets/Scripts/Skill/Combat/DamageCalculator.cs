using UnityEngine;
using ARPG.Component;
using ARPG.Tables;

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
        /// 속성별 독립 계산 → 저항 적용 → 합산
        /// </summary>
        public static DamageResult Calculate(int attackerId, int targetId, SkillTable skillData)
        {
            DamageResult result = new DamageResult
            {
                DamageType = skillData.DamageType
            };

            // 공격자/타겟 스탯 가져오기
            if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat) == false)
            {
                Debug.LogWarning($"[DamageCalculator] Attacker StatComponent not found - AttackerId: {attackerId}");
                return result;
            }

            if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat) == false)
            {
                Debug.LogWarning($"[DamageCalculator] Target StatComponent not found - TargetId: {targetId}");
                return result;
            }

            // ========== 1단계: 속성별 기본 데미지 ==========
            float physDamage = Random.Range(attackerStat.FinalAttackMin, attackerStat.FinalAttackMax + 1);
            float fireDamage = Random.Range(attackerStat.FinalFireAttackMin, attackerStat.FinalFireAttackMax + 1);
            float iceDamage = Random.Range(attackerStat.FinalIceAttackMin, attackerStat.FinalIceAttackMax + 1);
            float lightningDamage = Random.Range(attackerStat.FinalLightningAttackMin, attackerStat.FinalLightningAttackMax + 1);
            float poisonDamage = Random.Range(attackerStat.FinalPoisonAttackMin, attackerStat.FinalPoisonAttackMax + 1);

            // ========== 2단계: 스킬 데미지를 해당 속성에 합산 ==========
            float skillBaseDamage = Random.Range(skillData.DamageMin, skillData.DamageMax + 1);
            switch (skillData.DamageType)
            {
                case GlobalEnum.DamageType.Physics:
                    physDamage += skillBaseDamage;
                    break;
                case GlobalEnum.DamageType.Fire:
                    fireDamage += skillBaseDamage;
                    break;
                case GlobalEnum.DamageType.Ice:
                    iceDamage += skillBaseDamage;
                    break;
                case GlobalEnum.DamageType.Lightning:
                    lightningDamage += skillBaseDamage;
                    break;
                case GlobalEnum.DamageType.Poison:
                    poisonDamage += skillBaseDamage;
                    break;
            }

            // ========== 3단계: 스킬 배율 (모든 속성에 동일 적용) ==========
            float skillDamageMultiplier = GetSkillDamageMultiplier(attackerStat);
            physDamage *= skillDamageMultiplier;
            fireDamage *= skillDamageMultiplier;
            iceDamage *= skillDamageMultiplier;
            lightningDamage *= skillDamageMultiplier;
            poisonDamage *= skillDamageMultiplier;

            // ========== 4단계: 치명타 판정 (모든 속성에 동일 적용) ==========
            bool isCrit = Random.Range(0f, 100f) < attackerStat.FinalCriRate;
            if (isCrit)
            {
                float critMul = GetCritMultiplier(attackerStat);
                physDamage *= critMul;
                fireDamage *= critMul;
                iceDamage *= critMul;
                lightningDamage *= critMul;
                poisonDamage *= critMul;
                result.IsCritical = true;
            }

            // ========== 5단계: 속성별 저항 감소 ==========
            physDamage *= (1f - GetResistanceReduction(targetStat.FinalDefense));
            fireDamage *= (1f - GetResistanceReduction(targetStat.FinalFireResist));
            iceDamage *= (1f - GetResistanceReduction(targetStat.FinalIceResist));
            lightningDamage *= (1f - GetResistanceReduction(targetStat.FinalLightningResist));
            poisonDamage *= (1f - GetResistanceReduction(targetStat.FinalPoisonResist));

            // ========== 6단계: 합산 ==========
            float totalDamage = physDamage + fireDamage + iceDamage + lightningDamage + poisonDamage;

            // ========== 7단계: 회피/막기 판정 (합산 후 적용) ==========
            if (Random.Range(0f, 100f) < targetStat.FinalEvasion)
            {
                result.IsEvaded = true;
                result.FinalDamage = 0;
                return result;
            }

            if (Random.Range(0f, 100f) < targetStat.FinalBlockChance)
            {
                result.IsBlocked = true;
                float blockReduction = targetStat.FinalBlockReduction > 0
                    ? targetStat.FinalBlockReduction / 100f
                    : 0.5f;
                totalDamage *= (1f - blockReduction);
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
            result.LifeStealAmount = totalDamage * (attackerStat.FinalLifeSteal / 100f);
            result.ThornsDamage = targetStat.FinalThorns;

            return result;
        }

        /// <summary>
        /// 데미지 결과를 엔티티에 적용
        /// </summary>
        public static void ApplyDamageResult(int attackerId, int targetId, DamageResult result)
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
                ApplyStatusEffects(attackerId, targetId, result);
            }
        }

        /// <summary>
        /// 속성별 독립 상태이상 판정
        /// 데미지가 존재하는 각 속성에 대해 개별적으로 상태이상 발동
        /// </summary>
        private static void ApplyStatusEffects(int attackerId, int targetId, DamageResult result)
        {
            if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat) == false)
                return;

            // 화염 데미지 > 0 → 점화 판정
            if (result.FireDamage > 0f)
            {
                ApplyIgnite(attackerId, targetId, attackerStat, result.FireDamage);
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
                ApplyBleeding(targetId, attackerStat, result.PhysDamage);
            }
        }

        /// <summary>
        /// 출혈 발동 (물리 DoT)
        /// </summary>
        private static void ApplyBleeding(int targetId, StatComponent attackerStat, float physDamage)
        {
            int bloodingRate = attackerStat.FinalBloodingRate + 50;
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
        private static void ApplyIgnite(int attackerId, int targetId, StatComponent attackerStat, float fireDamage)
        {
            int igniteRate = attackerStat.FinalIgniteRate;
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
