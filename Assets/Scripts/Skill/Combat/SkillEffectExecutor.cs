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
            // 같은 트리거의 SkillPropagation 중복도 그 시점에 SkillStatBonusHelper.FilterToActionEffects가 제거한다.
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

                case GE.Stat.SkillPropagation:
                    ExecuteSkillPropagation(effect, ref ctx);
                    break;

                case GE.Stat.Knockback:
                case GE.Stat.Pull:
                    // 판정 범위 중심(ctx.HitOrigin)에서 밀거나 당긴다. 판정·방향 계산은 ForcedMoveStarter가 정본
                    ForcedMoveStarter.TryStartFromHit(effect, ctx);
                    break;

                case GE.Stat.SummonMonster:
                    ExecuteSummonMonster(effect, ref ctx);
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
        /// 시전자 둘레에 몬스터를 부른다 (SKILL_DESIGN §6.12). 같은 효과로 부른 몬스터가 최대 수(Param2)보다 적으면 모자란 만큼만 부른다.
        /// Param1: MonsterTable Id, Param2: 한 번에 부르는 수 겸 동시 최대 수, Param3: 생존 시간(초).
        /// 소환 몬스터는 적 진영으로 생성되므로 몬스터 시전자 전용이다.
        /// </summary>
        private static void ExecuteSummonMonster(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            int monsterTableId = Mathf.RoundToInt(effect.Param1);
            if (monsterTableId <= 0 || effect.Param3 <= 0f)
            {
                Debug.LogError($"[SkillEffect] SummonMonster requires Param1=MonsterTable Id and Param3=lifetime > 0. EffectId={effect.Id}");
                return;
            }

            ComponentManager cm = AR.s.Component;
            if (cm.HasComponent<MonsterTag>(ctx.OwnerEntityId) == false)
            {
                Debug.LogWarning($"[SkillEffect] SummonMonster is monster-only. Owner({ctx.OwnerEntityId}) EffectId={effect.Id}");
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(ctx.OwnerEntityId, out TransformComponent ownerTransform) == false)
                return;

            int spawnCount = MonsterSummonRules.GetMaxCount(effect)
                - MonsterSummonRules.CountActiveSummons(ctx.OwnerEntityId, effect.Id);
            for (int i = 0; i < spawnCount; i++)
            {
                MonsterSummonRules.TryPickPosition(ownerTransform.Position, out Vector2 position);
                EntityFactory.CreateSummonedMonster(ctx.OwnerEntityId, effect.Id, monsterTableId, position, effect.Param3).Forget();
            }
        }

        private static void ExecuteSkillPropagation(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            int propagationTableId = Mathf.RoundToInt(effect.Param1);
            if (propagationTableId <= 0)
            {
                Debug.LogError($"[SkillEffect] SkillPropagation requires Param1=SkillPropagationTable Id. EffectId={effect.Id}");
                return;
            }

            if (effect.Trigger == GE.SkillTrigger.OnSkillCommit)
            {
                if (ARPG.Skill.Propagation.SkillPropagationRuntime.TryStartOnSkillCommit(propagationTableId, ref ctx) == true)
                {
                    ctx.SuppressDefaultImpact = true;
                }
                return;
            }

            if (effect.Trigger == GE.SkillTrigger.OnHit)
            {
                ARPG.Skill.Propagation.SkillPropagationRuntime.TryStartOnHit(propagationTableId, ref ctx);
                return;
            }

            Debug.LogError($"[SkillEffect] SkillPropagation only supports OnSkillCommit or OnHit. EffectId={effect.Id}, Trigger={effect.Trigger}");
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

            BuffTable? buffTable = AR.s.Data.GetBuff(buffId);
            if (buffTable == null)
            {
                Debug.LogWarning($"[SkillEffect] ApplyBuffOnHit - BuffTable not found, BuffId={buffId}");
                return;
            }

            float duration = effect.Param3 > 0f ? effect.Param3 : buffTable.Duration;
            if (buffTable.EffectType == GE.BuffEffectType.Blooding)
            {
                BuffHelper.AddDamageBasedBuff(ctx.TargetEntityId, buffId, ctx.DamageResult.FinalDamage, duration, ctx.OwnerEntityId);
                return;
            }

            BuffHelper.AddBuff(ctx.TargetEntityId, buffId, duration, ctx.OwnerEntityId);
        }
    }
}
