#nullable enable
using System.Collections.Generic;
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
        /// effectIds가 null/empty면 즉시 반환 (무비용).
        /// </summary>
        public static void Trigger(GE.SkillTrigger trigger, ref SkillEffectContext ctx, List<int>? effectIds)
        {
            if (effectIds == null || effectIds.Count == 0)
                return;

            for (int i = 0; i < effectIds.Count; i++)
            {
                int effectId = effectIds[i];
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
                case GE.SkillEffectType.None:
                    break;

                case GE.SkillEffectType.LifeStealOnHit:
                    ExecuteLifeStealOnHit(effect, ref ctx);
                    break;

                case GE.SkillEffectType.ApplyBuffOnHit:
                    ExecuteApplyBuffOnHit(effect, ref ctx);
                    break;

                case GE.SkillEffectType.DelegateToTotem:
                    ExecuteDelegateToTotem(effect, ref ctx);
                    break;

                case GE.SkillEffectType.SpawnProjectile:
                    ExecuteSpawnProjectile(effect, ref ctx);
                    break;

                default:
                    Debug.LogWarning($"[SkillEffectExecutor] Unhandled EffectType: {effect.EffectType}");
                    break;
            }
        }

        // === EffectType 구현 ===

        /// <summary>
        /// 명중 시 데미지의 일정 비율을 시전자 HP로 회복.
        /// Param1: 흡혈 비율(%) — 예: 15 → FinalDamage의 15%
        /// </summary>
        private static void ExecuteLifeStealOnHit(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            if (effect.Param1 <= 0f || ctx.DamageResult.FinalDamage <= 0f)
                return;

            if (AR.s.Component.TryGetComponent<StatComponent>(ctx.OwnerEntityId, out var ownerStat) == false)
                return;

            int healAmount = Mathf.RoundToInt(ctx.DamageResult.FinalDamage * effect.Param1 / 100f);
            if (healAmount <= 0)
                return;

            int newHp = Mathf.Min(ownerStat.CurrentHp + healAmount, ownerStat.FinalMaxHp);
            ownerStat.SetCurrentHp(ctx.OwnerEntityId, newHp);
            AR.s.Component.SetComponent(ctx.OwnerEntityId, ownerStat);

            Debug.Log($"<color=yellow>[SkillEffect] LifeStealOnHit - Owner({ctx.OwnerEntityId}) +{healAmount} HP ({effect.Param1}% of {ctx.DamageResult.FinalDamage:F0}) → {newHp}/{ownerStat.FinalMaxHp}</color>");
        }

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
        /// 스킬 본인의 발사체와 무관한 추가 발사체를 부채꼴로 스폰.
        /// 용도: "스킬 효과로 다른 종류의 발사체를 추가 발사" — 예: 화염 폭발 시 작은 폭탄 N개, 시체 폭발의 파편 등.
        /// 일반 Multi Shot(스킬 자체 발사체 개수 조절)은 SkillTable.BaseProjectileCount + Stat.ProjectileCountAdd로 처리하므로 이 EffectType은 사용하지 않음.
        ///
        /// Param1: 별도 ProjectileId (스킬의 ProjectileId가 아님 — 효과 전용)
        /// Param2: 기본 발사 개수 (Stat.ProjectileCountAdd가 추가 합산됨)
        /// Param3: 현재 미사용 (분산 각도는 코드 상수)
        /// </summary>
        private static void ExecuteSpawnProjectile(SkillEffectTable effect, ref SkillEffectContext ctx)
        {
            const float SPREAD_ANGLE_PER_SHOT = 15f;  // 발사체 간 각도(도). 추후 Stat 또는 Param3로 교체 예정

            int projectileId = (int)effect.Param1;
            if (projectileId <= 0)
            {
                Debug.LogWarning($"[SkillEffect] SpawnProjectile - invalid ProjectileId: {projectileId}");
                return;
            }

            int baseCount = Mathf.Max(1, (int)effect.Param2);
            int extraCount = 0;
            if (AR.s.Component.TryGetComponent<StatComponent>(ctx.OwnerEntityId, out var ownerStat))
                extraCount = ownerStat.FinalProjectileCountAdd;
            int finalCount = Mathf.Max(1, baseCount + extraCount);

            if (AR.s.Component.TryGetComponent<TransformComponent>(ctx.OwnerEntityId, out var ownerTr) == false)
            {
                Debug.LogWarning($"[SkillEffect] SpawnProjectile - Owner({ctx.OwnerEntityId}) has no TransformComponent");
                return;
            }

            Vector2 baseDir = ctx.TargetPosition - ownerTr.Position;
            if (baseDir.sqrMagnitude < 0.0001f)
                baseDir = Vector2.right;
            else
                baseDir.Normalize();
            float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;

            // 부채꼴 분산 — 발사체 간 SPREAD_ANGLE_PER_SHOT 도씩 벌림
            float totalSpread = (finalCount - 1) * SPREAD_ANGLE_PER_SHOT;
            float startOffset = -totalSpread * 0.5f;

            for (int i = 0; i < finalCount; i++)
            {
                float angle = baseAngle + startOffset + SPREAD_ANGLE_PER_SHOT * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                ProjectileHelper.SpawnProjectile(ctx.OwnerEntityId, ctx.SkillEntityId, projectileId, ownerTr.Position, dir);
            }

            Debug.Log($"[SkillEffect] SpawnProjectile - Owner({ctx.OwnerEntityId}) fired {finalCount} projectiles (base={baseCount}+stat={extraCount}) of Id={projectileId}");
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
