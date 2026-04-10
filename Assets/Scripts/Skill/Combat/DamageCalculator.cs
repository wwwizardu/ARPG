using UnityEngine;
using ARPG.Component;
using ARPG.Tables;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 데미지 계산 결과
    /// </summary>
    public struct DamageResult
    {
        public float FinalDamage;            // 최종 데미지
        public bool IsCritical;              // 치명타 여부
        public bool IsEvaded;                // 회피 여부
        public bool IsBlocked;               // 막기 여부
        public float LifeStealAmount;        // 흡혈량
        public float ThornsDamage;           // 반사 데미지
        public GlobalEnum.DamageType DamageType; // 데미지 타입
    }

    /// <summary>
    /// 데미지 계산 유틸리티
    /// combatSystem.md의 6단계 데미지 공식을 정확히 구현
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
        /// </summary>
        private static float GetCritMultiplier(StatComponent stat)
        {
            return stat.FinalCriDamage > 0 ? stat.FinalCriDamage / 100f : 1.5f;
        }

        /// <summary>
        /// UI용 예상 데미지 범위 (타겟 없이 공격자 스탯만으로 계산)
        /// Calculate와 동일한 1~3단계 공식 사용, 방어력/회피/막기 미적용
        /// 치명타는 기대값으로 반영: damage × (1 + critRate/100 × (critMultiplier - 1))
        /// </summary>
        public static (int min, int max) Calculate(StatComponent attackerStat)
        {
            float skillMultiplier = GetSkillDamageMultiplier(attackerStat);
            float critRate = Mathf.Clamp(attackerStat.FinalCriRate, 0, 100) / 100f;
            float critMultiplier = GetCritMultiplier(attackerStat);
            float critExpected = 1f + critRate * (critMultiplier - 1f);

            int min = Mathf.RoundToInt(attackerStat.FinalAttackMin * skillMultiplier * critExpected);
            int max = Mathf.RoundToInt(attackerStat.FinalAttackMax * skillMultiplier * critExpected);
            return (min, max);
        }

        /// <summary>
        /// 데미지 계산 메인 메서드
        /// </summary>
        /// <param name="attackerId">공격자 엔티티 ID</param>
        /// <param name="targetId">타겟 엔티티 ID</param>
        /// <param name="skillData">스킬 테이블 데이터</param>
        /// <returns>계산된 데미지 결과</returns>
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

            // ========== 1단계: 기본 데미지 (랜덤) ==========
            float baseDamage = Random.Range(attackerStat.FinalAttackMin, attackerStat.FinalAttackMax);

            // ========== 2단계: 스킬 배율 적용 ==========
            float skillDamageMultiplier = GetSkillDamageMultiplier(attackerStat);

            // 스킬 테이블의 데미지 범위 (DamageMin ~ DamageMax)를 기본 데미지에 더함
            float skillBaseDamage = Random.Range(skillData.DamageMin, skillData.DamageMax + 1);
            float totalBaseDamage = baseDamage + skillBaseDamage;

            float skillDamage = totalBaseDamage * skillDamageMultiplier;

            // ========== 3단계: 치명타 판정 ==========
            bool isCrit = Random.Range(0f, 100f) < attackerStat.FinalCriRate;
            if (isCrit)
            {
                skillDamage *= GetCritMultiplier(attackerStat);
                result.IsCritical = true;
            }

            // ========== 4단계: 방어력 감소 ==========
            float defense = targetStat.FinalDefense;
            float damageReduction = defense / (defense + 100f); // 방어력 100 = 50% 감소
            float finalDamage = skillDamage * (1f - damageReduction);

            // ========== 5단계: 회피/막기 판정 ==========
            // 5-1. 회피 판정 (회피 성공 시 데미지 0)
            if (Random.Range(0f, 100f) < targetStat.FinalEvasion)
            {
                result.IsEvaded = true;
                result.FinalDamage = 0;
                return result; // 회피 성공 시 즉시 리턴
            }

            // 5-2. 막기 판정 (막기 성공 시 데미지 감소)
            if (Random.Range(0f, 100f) < targetStat.FinalBlockChance)
            {
                result.IsBlocked = true;

                // 막기 감소율이 0이면 50으로 처리 (기본값)
                float blockReduction = targetStat.FinalBlockReduction > 0
                    ? targetStat.FinalBlockReduction / 100f
                    : 0.5f;

                finalDamage *= (1f - blockReduction);
            }

            // ========== 6단계: 최소 데미지 보장 ==========
            finalDamage = Mathf.Max(finalDamage, 1f);
            result.FinalDamage = finalDamage;

            // ========== 특수 효과 계산 ==========
            // 생명력 흡수 (공격자가 회복할 양)
            result.LifeStealAmount = finalDamage * (attackerStat.FinalLifeSteal / 100f);

            // 반사 데미지 (타겟 → 공격자)
            result.ThornsDamage = targetStat.FinalThorns;

            return result;
        }

        /// <summary>
        /// 데미지 결과를 엔티티에 적용
        /// </summary>
        /// <param name="attackerId">공격자 엔티티 ID</param>
        /// <param name="targetId">타겟 엔티티 ID</param>
        /// <param name="result">데미지 계산 결과</param>
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

                return; // 회피 시 데미지 없음
            }

            // ========== 타겟 HP 감소 ==========
            if (AR.s.Component.TryGetComponent<StatComponent>(targetId, out var targetStat))
            {
                int damage = Mathf.RoundToInt(result.FinalDamage);
                int newHp = Mathf.Max(0, targetStat.CurrentHp - damage);
                targetStat.SetCurrentHp(targetId, newHp);
                AR.s.Component.SetComponent(targetId, targetStat);

                // HpDirtyTag는 SetCurrentHp에서 자동 추가됨

                Debug.Log($"[DamageCalculator] 데미지 적용 - Target: {targetId}, Damage: {damage}, Critical: {result.IsCritical}, Blocked: {result.IsBlocked}, RemainingHP: {newHp}/{targetStat.FinalMaxHp}");
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

                    Debug.Log($"[DamageCalculator] 생명력 흡수 - Attacker: {attackerId}, Heal: {healAmount}");
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

                    Debug.Log($"[DamageCalculator] 반사 데미지 - Attacker: {attackerId}, Thorns: {thornsDamage}");
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

            // ========== 상태 이상 발동 (데미지 타입별) ==========
            if (result.IsEvaded == false) // 회피하지 않은 경우만 상태 이상 적용
            {
                ApplyStatusEffects(attackerId, targetId, result);
            }
        }

        /// <summary>
        /// 데미지 타입에 따라 상태 이상 적용
        /// </summary>
        private static void ApplyStatusEffects(int attackerId, int targetId, DamageResult result)
        {
            if (AR.s.Component.TryGetComponent<StatComponent>(attackerId, out var attackerStat) == false)
                return;

            switch (result.DamageType)
            {
                case GlobalEnum.DamageType.Fire:
                    ApplyIgnite(attackerId, targetId, attackerStat, result.FinalDamage);
                    break;

                case GlobalEnum.DamageType.Ice:
                    ApplyChill(targetId);
                    break;

                case GlobalEnum.DamageType.Poison:
                    ApplyPoison(targetId, result.FinalDamage);
                    break;

                // Physics 타입의 출혈은 System_Skill.cs에서 이미 처리 중
            }
        }

        /// <summary>
        /// 점화 발동 (화염 DoT, 스택 가능)
        /// </summary>
        private static void ApplyIgnite(int attackerId, int targetId, StatComponent attackerStat, float damage)
        {
            int igniteRate = attackerStat.FinalIgniteRate;
            if (Random.Range(0, 100) < igniteRate)
            {
                // 점화 데미지: 원본 데미지의 50%, 4초간, 1초마다 틱
                int totalIgniteDamage = Mathf.FloorToInt(damage * 0.5f);
                int tickDamage = totalIgniteDamage / 4; // 4초간 나눠서

                int igniteBuffId = 2;
                float igniteDuration = 4f;

                // 버프 추가
                int buffEntityId = Utility.BuffHelper.AddBuff(targetId, igniteBuffId, igniteDuration);

                // BuffInstance의 TickDamage 설정 (스택별로 적용됨)
                if (buffEntityId != -1 && AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
                {
                    buff.TickDamage = tickDamage;
                    AR.s.Component.SetComponent(buffEntityId, buff);
                }

                Debug.Log($"[DamageCalculator] 점화 발동 - Target: {targetId}, TotalDamage: {totalIgniteDamage}, TickDamage: {tickDamage}/초");
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
            Debug.Log($"[DamageCalculator] 냉기 적용 - Target: {targetId}, Duration: {chillDuration}초");
        }

        /// <summary>
        /// 중독 적용 (독 DoT + HP 재생 감소, 자동 발동, 스택 가능)
        /// </summary>
        private static void ApplyPoison(int targetId, float damage)
        {
            // 중독 데미지: 원본 데미지의 20%, 10초간, 1초마다 틱
            int totalPoisonDamage = Mathf.FloorToInt(damage * 0.2f);
            int tickDamage = totalPoisonDamage / 10; // 10초간 나눠서

            int poisonBuffId = 5;
            float poisonDuration = 10f;

            // 버프 추가
            int buffEntityId = Utility.BuffHelper.AddBuff(targetId, poisonBuffId, poisonDuration);

            // BuffInstance의 TickDamage 설정
            if (buffEntityId != -1 && AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out var buff))
            {
                buff.TickDamage = tickDamage;
                AR.s.Component.SetComponent(buffEntityId, buff);
            }

            Debug.Log($"[DamageCalculator] 중독 적용 - Target: {targetId}, TotalDamage: {totalPoisonDamage}, TickDamage: {tickDamage}/초");
        }
    }
}
