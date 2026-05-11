#nullable enable
using ARPG.Component;
using ARPG.Factory;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// SkillEffect 디스패처. 각 트리거 시점에 System_Skill 등이 호출하면
    /// 해당 효과 ID 리스트를 SkillEffectTable에서 조회해 트리거가 일치하는 효과만 실행한다.
    ///
    /// 사용 예:
    ///   SkillEffectContext ctx = new() { SkillEntityId = ..., OwnerEntityId = ..., ... };
    ///   SkillEffectExecutor.Trigger(SkillTrigger.OnHit, ref ctx, skill.Table?.SkillEffectIds);
    ///   if (ctx.CancelOriginalCast) return;  // OnSkillCommand 트리거 후 캔슬 여부 확인
    /// </summary>
    public static class SkillEffectExecutor
    {
        /// <summary>
        /// 지정 트리거에 매칭되는 효과를 모두 실행.
        /// effectIds.Count == 0이면 즉시 반환 (무비용).
        /// </summary>
        public static void Trigger(GE.SkillTrigger trigger, ref SkillEffectContext ctx, in SkillEffectIds effectIds)
        {
            if (effectIds.Count == 0)
                return;

            // 호출 시점에 전달되는 effectIds는 EntityFactory.CreateSkill에서 이미 EffectAction만 남도록 필터링된 인라인 배열(SkillComponent.EffectiveSkillEffectIds).
            // StatBonus는 SkillStatBonusHelper가 컴포넌트로 사전 합산해 DamageCalculator가 소비하므로 여기 도달하지 않는다.
            for (int i = 0; i < effectIds.Count; i++)
            {
                int effectId = effectIds.Get(i);
                SkillEffectTable? effect = AR.s.Data.GetSkillEffect(effectId);
                if (effect == null)
                {
                    Debug.LogWarning($"[SkillEffectExecutor] SkillEffectTable not found: id={effectId}");
                    continue;
                }

                if (effect.Trigger != trigger)
                    continue;

                // Probability: 0 이하 = 절대 발동 안 함, 100 이상 = 항상 발동, 그 외 = 확률 롤
                if (effect.Probability <= 0)
                    continue;
                if (effect.Probability < 100 && Random.Range(0, 100) >= effect.Probability)
                    continue;

                Execute(effect, ref ctx);
            }
        }

        /// <summary>
        /// EffectType별 분기. 새 효과 추가 시 case 1개만 추가하면 데이터로 합성 가능.
        /// </summary>
        private static void Execute(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            switch (effect.EffectType)
            {
                case GE.Stat.ApplyBuff:
                    ExecuteApplyBuffOnHit(effect, ref ctx);
                    break;

                case GE.Stat.DelegateToTotem:
                    ExecuteDelegateToTotem(effect, ref ctx);
                    break;

                default:
                    // 시트에서 Kind=EffectAction으로 분류됐는데 여기 case가 없음 = 미구현 액션.
                    Debug.LogWarning($"[SkillEffectExecutor] EffectAction case 누락: EffectType={effect.EffectType}, EffectId={effect.Id}");
                    break;
            }
        }

        // === EffectType 구현 ===

        /// <summary>
        /// 시전 명령을 가로채 토템 엔티티가 대신 자율 시전하도록 위임.
        /// Param1: 토템 생존시간(초). 0 이하면 기본 8초 사용.
        /// 효과 발동 시 ctx.CancelOriginalCast = true로 시전자의 원래 시전을 캔슬한다.
        /// </summary>
        private static void ExecuteDelegateToTotem(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            // 토템이 사용하는 스킬은 원본 SkillEffectIds를 공유하더라도 다시 토템 소환으로 위임하지 않는다.
            // 여기서 아무 것도 하지 않으면 CancelOriginalCast가 false로 유지되어 System_Skill이 원래 스킬을 계속 실행한다.
            if (AR.s.Component.HasComponent<TotemTag>(ctx.OwnerEntityId))
                return;

            float duration = effect.Param1 > 0f ? effect.Param1 : 8f;

            int totemId = EntityFactory.CreateTotem(ctx.OwnerEntityId, ctx.SkillId, ctx.TargetPosition, duration);
            if (totemId == -1)
            {
                Debug.LogWarning($"[SkillEffect] DelegateToTotem - CreateTotem failed for SkillId({ctx.SkillId}), Owner({ctx.OwnerEntityId})");
                return;
            }

            ctx.CancelOriginalCast = true;
            Debug.Log($"[SkillEffect] DelegateToTotem - Caster({ctx.OwnerEntityId}) → Totem({totemId}) casting SkillId({ctx.SkillId}) for {duration}s");
        }

        /// <summary>
        /// 명중 시 타겟에게 버프(또는 디버프)를 부여.
        /// Param1: BuffTable ID, Param3: 지속시간 오버라이드(0=BuffTable 기본 사용)
        /// </summary>
        private static void ExecuteApplyBuffOnHit(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            int buffId = (int)effect.Param1;
            if (buffId <= 0 || ctx.TargetEntityId <= 0)
                return;

            float duration = effect.Param3;
            if (duration <= 0f)
            {
                BuffTable? buffTable = AR.s.Data.GetBuff(buffId);
                if (buffTable == null)
                {
                    Debug.LogWarning($"[SkillEffect] ApplyBuffOnHit - BuffTable not found, BuffId={buffId}");
                    return;
                }
                duration = buffTable.Duration;
            }

            BuffHelper.AddBuff(ctx.TargetEntityId, buffId, duration);
        }
    }
}
