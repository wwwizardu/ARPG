using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬 엔티티에 부착되는 stat 보너스. 스킬북 페이지의 SkillEffectType.AddStat 효과로 빌드됨.
    /// 트리거별 컴포넌트로 분리되어 발동 시점에 해당 컴포넌트만 조회 → 순환 없는 O(1)+jump-table 조회.
    /// 각 컴포넌트는 그 트리거에서 의미 있는 stat 필드만 보유.
    /// </summary>

    /// <summary>시전 명령 처리 단계. 시전 속도 등.</summary>
    public struct SkillStatBonusOnSkillCommandComponent
    {
        public int CastSpeedMulBonus;

        public int GetBonus(GE.Stat s)
        {
            switch (s)
            {
                case GE.Stat.CastSpeedMul: return CastSpeedMulBonus;
                default: return 0;
            }
        }

        public void Add(GE.Stat s, int value)
        {
            switch (s)
            {
                case GE.Stat.CastSpeedMul: CastSpeedMulBonus += value; break;
                default:
                    Debug.LogWarning($"[SkillStatBonusOnSkillCommand] Unsupported stat: {s}");
                    break;
            }
        }
    }

    /// <summary>시전 확정(Process 진입) 단계. 발사체 개수 등.</summary>
    public struct SkillStatBonusOnSkillStartComponent
    {
        public int ProjectileCountAddBonus;

        public int GetBonus(GE.Stat s)
        {
            switch (s)
            {
                case GE.Stat.ProjectileCountAdd: return ProjectileCountAddBonus;
                default: return 0;
            }
        }

        public void Add(GE.Stat s, int value)
        {
            switch (s)
            {
                case GE.Stat.ProjectileCountAdd: ProjectileCountAddBonus += value; break;
                default:
                    Debug.LogWarning($"[SkillStatBonusOnSkillStart] Unsupported stat: {s}");
                    break;
            }
        }
    }

    /// <summary>적중 시 발동. 치명타·상태이상 확률 등.</summary>
    public struct SkillStatBonusOnHitComponent
    {
        public int CriRateBonus;
        public int CriDamageMulBonus;
        public int IgniteRateBonus;
        public int BloodingRateBonus;
        public int PoisonRateBonus;
        public int PhysDamageAddBonusMin;
        public int PhysDamageAddBonusMax;
        public int FireDamageAddBonusMin;
        public int FireDamageAddBonusMax;
        public int IceDamageAddBonusMin;
        public int IceDamageAddBonusMax;
        public int LightningDamageAddBonusMin;
        public int LightningDamageAddBonusMax;
        public int PoisonDamageAddBonusMin;
        public int PoisonDamageAddBonusMax;

        public void Add(GE.Stat s, int value)
        {
            switch (s)
            {
                case GE.Stat.CriRate:            CriRateBonus += value; break;
                case GE.Stat.CriDamageMul:       CriDamageMulBonus += value; break;
                case GE.Stat.IgniteRate:         IgniteRateBonus += value; break;
                case GE.Stat.BloodingRate:       BloodingRateBonus += value; break;
                case GE.Stat.PoisonRate:         PoisonRateBonus += value; break;
                case GE.Stat.AttackMin:          PhysDamageAddBonusMin += value; break;
                case GE.Stat.AttackMax:          PhysDamageAddBonusMax += value; break;
                case GE.Stat.FireAttackMin:      FireDamageAddBonusMin += value; break;
                case GE.Stat.FireAttackMax:      FireDamageAddBonusMax += value; break;
                case GE.Stat.IceAttackMin:       IceDamageAddBonusMin += value; break;
                case GE.Stat.IceAttackMax:       IceDamageAddBonusMax += value; break;
                case GE.Stat.LightningAttackMin: LightningDamageAddBonusMin += value; break;
                case GE.Stat.LightningAttackMax: LightningDamageAddBonusMax += value; break;
                case GE.Stat.PoisonAttackMin:    PoisonDamageAddBonusMin += value; break;
                case GE.Stat.PoisonAttackMax:    PoisonDamageAddBonusMax += value; break;
                default:
                    Debug.LogWarning($"[SkillStatBonusOnHit] Unsupported stat: {s}");
                    break;
            }
        }
    }

    /// <summary>치명타 적중 시. 현재 후보 stat 없음 — 향후 필드 추가 시 활성화.</summary>
    public struct SkillStatBonusOnCritComponent
    {
        public int GetBonus(GE.Stat s) => 0;

        public void Add(GE.Stat s, int value)
        {
            Debug.LogWarning($"[SkillStatBonusOnCrit] Unsupported stat: {s}");
        }
    }

    /// <summary>적 처치 시. 현재 후보 stat 없음 — 향후 필드 추가 시 활성화.</summary>
    public struct SkillStatBonusOnKillComponent
    {
        public int GetBonus(GE.Stat s) => 0;

        public void Add(GE.Stat s, int value)
        {
            Debug.LogWarning($"[SkillStatBonusOnKill] Unsupported stat: {s}");
        }
    }
}
