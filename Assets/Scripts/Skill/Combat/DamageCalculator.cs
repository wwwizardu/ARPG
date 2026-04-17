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
        /// UI용 특정 스킬 기준 예상 데미지
        /// Attack 스킬: 무기 Local 데미지 + 캐릭터 Added + 스킬 Added
        /// Spell 스킬:  스킬 베이스 데미지 + 캐릭터 Added
        /// 치명타 기대값 반영, 저항 미적용
        /// </summary>
        public static EstimatedDamage CalculateForSkill(StatComponent attackerStat, int attackerId, Tables.SkillTable skillData)
        {
            bool isAttackSkill = (skillData.Tags & GlobalEnum.SkillTag.Attack) != 0;

            // 베이스 데미지 모으기
            int physMin = 0, physMax = 0;
            int fireMin = 0, fireMax = 0;
            int iceMin = 0, iceMax = 0;
            int lightMin = 0, lightMax = 0;
            int poisMin = 0, poisMax = 0;

            int baseCritRate = 0;

            if (isAttackSkill)
            {
                // 무기 Local 스탯
                var weapon = Utility.WeaponHelper.GetEquippedWeapon(attackerId);
                ARPG.Data.WeaponStatCache ws = weapon != null && weapon.Equipment != null
                    ? weapon.Equipment.WeaponStats
                    : default;

                physMin = ws.Physics.Min;  physMax = ws.Physics.Max;
                fireMin = ws.Fire.Min;     fireMax = ws.Fire.Max;
                iceMin = ws.Ice.Min;       iceMax = ws.Ice.Max;
                lightMin = ws.Lightning.Min; lightMax = ws.Lightning.Max;
                poisMin = ws.Poison.Min;   poisMax = ws.Poison.Max;
                baseCritRate = ws.CriRate;

                // 스킬 자체 Added 데미지 (해당 속성에)
                if (skillData.DamageMax > 0)
                {
                    AddSkillDamageToElement(skillData, ref physMin, ref physMax,
                        ref fireMin, ref fireMax, ref iceMin, ref iceMax,
                        ref lightMin, ref lightMax, ref poisMin, ref poisMax);
                }
            }
            else
            {
                // Spell: 스킬 베이스 데미지
                AddSkillDamageToElement(skillData, ref physMin, ref physMax,
                    ref fireMin, ref fireMax, ref iceMin, ref iceMax,
                    ref lightMin, ref lightMax, ref poisMin, ref poisMax);
                baseCritRate = skillData.BaseCriRate;
            }

            // Character Added (비무기 스탯)
            physMin += attackerStat.FinalAttackMin;
            physMax += attackerStat.FinalAttackMax;
            fireMin += attackerStat.FinalFireAttackMin;
            fireMax += attackerStat.FinalFireAttackMax;
            iceMin += attackerStat.FinalIceAttackMin;
            iceMax += attackerStat.FinalIceAttackMax;
            lightMin += attackerStat.FinalLightningAttackMin;
            lightMax += attackerStat.FinalLightningAttackMax;
            poisMin += attackerStat.FinalPoisonAttackMin;
            poisMax += attackerStat.FinalPoisonAttackMax;

            // 배율 + 치명타 기대값
            float mul = GetSkillDamageMultiplier(attackerStat);
            int totalCritRate = Mathf.Clamp(baseCritRate + attackerStat.FinalCriRate, 0, 100);
            float critExpected = 1f + (totalCritRate / 100f) * (GetCritMultiplier(attackerStat) - 1f);
            float m = mul * critExpected;

            EstimatedDamage result;
            result.PhysMin = Mathf.RoundToInt(physMin * m);
            result.PhysMax = Mathf.RoundToInt(physMax * m);
            result.FireMin = Mathf.RoundToInt(fireMin * m);
            result.FireMax = Mathf.RoundToInt(fireMax * m);
            result.IceMin = Mathf.RoundToInt(iceMin * m);
            result.IceMax = Mathf.RoundToInt(iceMax * m);
            result.LightningMin = Mathf.RoundToInt(lightMin * m);
            result.LightningMax = Mathf.RoundToInt(lightMax * m);
            result.PoisonMin = Mathf.RoundToInt(poisMin * m);
            result.PoisonMax = Mathf.RoundToInt(poisMax * m);
            result.TotalMin = result.PhysMin + result.FireMin + result.IceMin + result.LightningMin + result.PoisonMin;
            result.TotalMax = result.PhysMax + result.FireMax + result.IceMax + result.LightningMax + result.PoisonMax;
            return result;
        }

        /// <summary>
        /// 스킬의 DamageType 속성에 스킬 DamageMin/Max를 추가
        /// </summary>
        private static void AddSkillDamageToElement(Tables.SkillTable skillData,
            ref int physMin, ref int physMax,
            ref int fireMin, ref int fireMax,
            ref int iceMin, ref int iceMax,
            ref int lightMin, ref int lightMax,
            ref int poisMin, ref int poisMax)
        {
            switch (skillData.DamageType)
            {
                case GlobalEnum.DamageType.Physics:
                    physMin += skillData.DamageMin; physMax += skillData.DamageMax;
                    break;
                case GlobalEnum.DamageType.Fire:
                    fireMin += skillData.DamageMin; fireMax += skillData.DamageMax;
                    break;
                case GlobalEnum.DamageType.Ice:
                    iceMin += skillData.DamageMin; iceMax += skillData.DamageMax;
                    break;
                case GlobalEnum.DamageType.Lightning:
                    lightMin += skillData.DamageMin; lightMax += skillData.DamageMax;
                    break;
                case GlobalEnum.DamageType.Poison:
                    poisMin += skillData.DamageMin; poisMax += skillData.DamageMax;
                    break;
            }
        }

        /// <summary>
        /// UI용 예상 치명타 확률 (스킬 기준)
        /// Attack: 무기 치명타 + 캐릭터 치명타
        /// Spell:  스킬 BaseCriRate + 캐릭터 치명타
        /// </summary>
        public static int EstimateCritRate(StatComponent attackerStat, int attackerId, Tables.SkillTable skillData)
        {
            bool isAttackSkill = (skillData.Tags & GlobalEnum.SkillTag.Attack) != 0;
            int baseCrit;
            if (isAttackSkill)
            {
                baseCrit = Utility.WeaponHelper.GetWeaponCriRate(attackerId);
            }
            else
            {
                baseCrit = skillData.BaseCriRate;
            }
            return Mathf.Clamp(baseCrit + attackerStat.FinalCriRate, 0, 100);
        }

        /// <summary>
        /// UI용 예상 공격 속도 (초당 공격 횟수, Attack 기준)
        /// Spell은 시전 속도 별도 표시하므로 여기선 Attack만
        /// </summary>
        public static float EstimateAttackSpeed(StatComponent attackerStat, int attackerId)
        {
            float weaponAS = Utility.WeaponHelper.GetWeaponAttackSpeed(attackerId);
            if (weaponAS <= 0f) weaponAS = 1f;
            return weaponAS * (1f + attackerStat.FinalAttackSpeed / 100f);
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

            // 공중 무적 판정: 타겟이 일정 높이 이상 점프 중이면 회피 처리
            if (AR.s.Component.TryGetComponent<JumpComponent>(targetId, out var jump) == true)
            {
                if (jump.Height >= System_Jump.InvincibleHeight)
                {
                    result.IsEvaded = true;
                    return result;
                }
            }

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

            // ========== 1단계: 베이스 데미지 (PoE 스타일) ==========
            // Attack 스킬: 무기의 Local 파이프라인 완료 데미지
            // Spell 스킬:  스킬 테이블의 DamageMin/Max (DamageType 속성으로만)
            bool isAttackSkill = (skillData.Tags & GlobalEnum.SkillTag.Attack) != 0;
            float physDamage = 0f, fireDamage = 0f, iceDamage = 0f, lightningDamage = 0f, poisonDamage = 0f;

            if (isAttackSkill)
            {
                // 무기에서 속성별 베이스 데미지 가져오기
                WeaponHelper.GetWeaponDamage(attackerId, GlobalEnum.DamageType.Physics, out int wPhysMin, out int wPhysMax);
                physDamage = Random.Range(wPhysMin, wPhysMax + 1);

                WeaponHelper.GetWeaponDamage(attackerId, GlobalEnum.DamageType.Fire, out int wFireMin, out int wFireMax);
                fireDamage = Random.Range(wFireMin, wFireMax + 1);

                WeaponHelper.GetWeaponDamage(attackerId, GlobalEnum.DamageType.Ice, out int wIceMin, out int wIceMax);
                iceDamage = Random.Range(wIceMin, wIceMax + 1);

                WeaponHelper.GetWeaponDamage(attackerId, GlobalEnum.DamageType.Lightning, out int wLightMin, out int wLightMax);
                lightningDamage = Random.Range(wLightMin, wLightMax + 1);

                WeaponHelper.GetWeaponDamage(attackerId, GlobalEnum.DamageType.Poison, out int wPoisMin, out int wPoisMax);
                poisonDamage = Random.Range(wPoisMin, wPoisMax + 1);
            }
            else
            {
                // Spell: 스킬 테이블의 베이스 데미지 (DamageType 속성에만)
                float skillBase = Random.Range(skillData.DamageMin, skillData.DamageMax + 1);
                switch (skillData.DamageType)
                {
                    case GlobalEnum.DamageType.Physics:   physDamage = skillBase; break;
                    case GlobalEnum.DamageType.Fire:      fireDamage = skillBase; break;
                    case GlobalEnum.DamageType.Ice:       iceDamage = skillBase; break;
                    case GlobalEnum.DamageType.Lightning: lightningDamage = skillBase; break;
                    case GlobalEnum.DamageType.Poison:    poisonDamage = skillBase; break;
                }
            }

            // ========== 2단계: Character Added 데미지 ==========
            // 캐릭터 비무기 스탯 (FinalX는 무기 Mod가 제외된 값: 장신구/버프/내재)
            physDamage += Random.Range(attackerStat.FinalAttackMin, attackerStat.FinalAttackMax + 1);
            fireDamage += Random.Range(attackerStat.FinalFireAttackMin, attackerStat.FinalFireAttackMax + 1);
            iceDamage += Random.Range(attackerStat.FinalIceAttackMin, attackerStat.FinalIceAttackMax + 1);
            lightningDamage += Random.Range(attackerStat.FinalLightningAttackMin, attackerStat.FinalLightningAttackMax + 1);
            poisonDamage += Random.Range(attackerStat.FinalPoisonAttackMin, attackerStat.FinalPoisonAttackMax + 1);

            // ========== 2-1단계: Attack 스킬은 스킬 테이블의 추가 데미지도 합산 ==========
            if (isAttackSkill && skillData.DamageMax > 0)
            {
                float skillAdded = Random.Range(skillData.DamageMin, skillData.DamageMax + 1);
                switch (skillData.DamageType)
                {
                    case GlobalEnum.DamageType.Physics:   physDamage += skillAdded; break;
                    case GlobalEnum.DamageType.Fire:      fireDamage += skillAdded; break;
                    case GlobalEnum.DamageType.Ice:       iceDamage += skillAdded; break;
                    case GlobalEnum.DamageType.Lightning: lightningDamage += skillAdded; break;
                    case GlobalEnum.DamageType.Poison:    poisonDamage += skillAdded; break;
                }
            }

            // ========== 3단계: 스킬 배율 (모든 속성에 동일 적용) ==========
            float skillDamageMultiplier = GetSkillDamageMultiplier(attackerStat);
            physDamage *= skillDamageMultiplier;
            fireDamage *= skillDamageMultiplier;
            iceDamage *= skillDamageMultiplier;
            lightningDamage *= skillDamageMultiplier;
            poisonDamage *= skillDamageMultiplier;

            // ========== 4단계: 치명타 판정 ==========
            // Attack 스킬: 무기 치명타 + 캐릭터 치명타
            // Spell 스킬:  스킬 테이블 BaseCriRate + 캐릭터 치명타
            float baseCritRate = isAttackSkill
                ? WeaponHelper.GetWeaponCriRate(attackerId)
                : skillData.BaseCriRate;
            float totalCritRate = baseCritRate + attackerStat.FinalCriRate;

            bool isCrit = Random.Range(0f, 100f) < totalCritRate;
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
