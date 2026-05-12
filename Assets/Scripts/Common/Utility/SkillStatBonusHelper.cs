using System.Collections.Generic;
using ARPG.Component;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 스킬 엔티티의 트리거별 SkillStatBonus*Component를 빌드/제거.
    /// SkillEffectTable의 EffectType=AddStat 행들을 Trigger별로 분류해 합산한다.
    /// 스킬북 페이지 변경 시 호출.
    /// </summary>
    public static class SkillStatBonusHelper
    {
        public static void Rebuild(int skillEntityId, IReadOnlyList<int> effectIds)
        {
            var onCmd   = default(SkillStatBonusOnSkillCommandComponent);
            var onStart = default(SkillStatBonusOnSkillStartComponent);
            var onHit   = default(SkillStatBonusOnHitComponent);
            var onCrit  = default(SkillStatBonusOnCritComponent);
            var onKill  = default(SkillStatBonusOnKillComponent);

            bool hasCmd = false, hasStart = false, hasHit = false, hasCrit = false, hasKill = false;

            if (effectIds != null)
            {
                for (int i = 0; i < effectIds.Count; i++)
                {
                    SkillEffectTable t = AR.s.Data.GetSkillEffect(effectIds[i]);
                    if (t == null)
                        continue;

                    // 액션류(LifeSteal/ApplyBuff/DelegateToTotem 등)는 SkillEffectExecutor가 트리거 시점에 직접 실행. 여기선 스킵.
                    if (t.Kind != GE.SkillEffectKind.StatBonus)
                        continue;

                    GE.Stat stat = t.EffectType;
                    int value = Mathf.RoundToInt(t.Param1);

                    switch (t.Trigger)
                    {
                        case GE.SkillTrigger.OnSkillCommand:
                            onCmd.Add(stat, value);
                            hasCmd = true;
                            break;
                        case GE.SkillTrigger.OnSkillStart:
                            onStart.Add(stat, value);
                            hasStart = true;
                            break;
                        case GE.SkillTrigger.OnHit:
                            onHit.Add(stat, value);
                            hasHit = true;
                            break;
                        case GE.SkillTrigger.OnCrit:
                            onCrit.Add(stat, value);
                            hasCrit = true;
                            break;
                        case GE.SkillTrigger.OnKill:
                            onKill.Add(stat, value);
                            hasKill = true;
                            break;
                        default:
                            Debug.LogWarning($"[SkillStatBonusHelper] Unsupported trigger '{t.Trigger}' for stat bonus (SkillEffectId={t.Id})");
                            break;
                    }
                }
            }

            SetOrRemove(skillEntityId, hasCmd,   onCmd);
            SetOrRemove(skillEntityId, hasStart, onStart);
            SetOrRemove(skillEntityId, hasHit,   onHit);
            SetOrRemove(skillEntityId, hasCrit,  onCrit);
            SetOrRemove(skillEntityId, hasKill,  onKill);

            // OnHit StatBonus가 변경되었으므로 공격 프로파일 캐시 재계산 요청
            AR.s.Component.AddComponent(skillEntityId, new SkillAttackProfileDirtyTag());
        }

        private static void SetOrRemove<T>(int entityId, bool has, T value) where T : struct
        {
            if (has)
            {
                AR.s.Component.SetComponent(entityId, value);
            }
            else if (AR.s.Component.HasComponent<T>(entityId))
            {
                AR.s.Component.RemoveComponent<T>(entityId);
            }
        }

        /// <summary>
        /// Rebuild로 SkillStatBonusOn*Component에 흡수된 StatBonus 행을 제외하고 EffectAction만 남긴 SkillEffectIds 반환.
        /// SkillComponent.EffectiveSkillEffectIds에 저장될 런타임 트리거 순회용 인라인 배열을 만든다.
        /// </summary>
        public static SkillEffectIds FilterToActionEffects(IReadOnlyList<int> effectIds)
        {
            SkillEffectIds result = default;
            if (effectIds == null || effectIds.Count == 0)
                return result;

            for (int i = 0; i < effectIds.Count; i++)
            {
                SkillEffectTable t = AR.s.Data.GetSkillEffect(effectIds[i]);
                if (t == null)
                    continue;
                if (t.Kind != GE.SkillEffectKind.EffectAction)
                    continue;
                result.Add(effectIds[i]);
            }
            return result;
        }
    }
}
