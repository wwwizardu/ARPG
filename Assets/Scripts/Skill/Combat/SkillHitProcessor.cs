#nullable enable
using ARPG.Component;
using Cysharp.Threading.Tasks;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// A single delivered skill hit. Direct hits, projectiles, and future propagation
    /// deliveries share this request so their damage and SkillEffect ordering stay identical.
    ///
    /// 필수 값(스킬/시전자/대상/피해 배수)은 readonly + 생성자로만 설정한다.
    /// struct 기본값으로 DamageMultiplier가 0이 되면 모든 피해가 최소 데미지(1)로 조용히 붕괴하므로
    /// 객체 초기화자에서 누락될 수 없게 막는다. 선택 값은 초기화자로 지정한다.
    ///   new SkillHitRequest(skillEntityId, ownerId, targetId, 1f) { IsProjectileDelivery = true, ... }
    /// </summary>
    public struct SkillHitRequest
    {
        public readonly int SkillEntityId;
        public readonly int OwnerEntityId;
        public readonly int TargetEntityId;
        public readonly float DamageMultiplier;

        public int DeliveryEntityId;
        public int PropagationRootEntityId;
        public int Generation;
        public bool IsProjectileDelivery;
        public Vector2 ImpactDirection;
        public HitOriginKind HitOriginKind;   // 판정 범위 중심 종류. 발사체·연쇄는 None
        public Vector2 HitOrigin;             // 판정 범위 중심 (발 위치 기준)

        public SkillHitRequest(int inSkillEntityId, int inOwnerEntityId, int inTargetEntityId, float inDamageMultiplier)
        {
            SkillEntityId = inSkillEntityId;
            OwnerEntityId = inOwnerEntityId;
            TargetEntityId = inTargetEntityId;
            DamageMultiplier = inDamageMultiplier;
            DeliveryEntityId = 0;
            PropagationRootEntityId = 0;
            Generation = 0;
            IsProjectileDelivery = false;
            ImpactDirection = Vector2.zero;
            HitOriginKind = HitOriginKind.None;
            HitOrigin = Vector2.zero;
        }
    }

    /// <summary>
    /// Applies one skill hit and dispatches its follow-up effects.
    /// Delivery systems own collision and range selection; this processor owns only the
    /// shared hit contract so every delivery uses DamageCalculator and trigger ordering.
    /// </summary>
    public static class SkillHitProcessor
    {
        public static void Process(in SkillHitRequest inRequest, SkillComponent inSkill)
        {
            if (inSkill.Table == null)
            {
                Debug.LogError($"[SkillHitProcessor] SkillTable is null - SkillId: {inSkill.SkillId}");
                return;
            }

            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<StatComponent>(inRequest.TargetEntityId, out StatComponent targetStat) == false)
            {
                Debug.LogWarning($"[SkillHitProcessor] Target entity has no StatComponent - TargetEntityId: {inRequest.TargetEntityId}");
                return;
            }

            DamageResult damageResult = DamageCalculator.Calculate(
                inRequest.SkillEntityId,
                inRequest.OwnerEntityId,
                inRequest.TargetEntityId,
                inSkill.Table,
                inRequest.DamageMultiplier);
            DamageCalculator.ApplyDamageResult(
                inRequest.SkillEntityId,
                inRequest.OwnerEntityId,
                inRequest.TargetEntityId,
                damageResult);

            if (componentManager.TryGetComponent<StatComponent>(inRequest.TargetEntityId, out targetStat) == false)
            {
                Debug.LogWarning($"[SkillHitProcessor] Target StatComponent lost after damage - TargetEntityId: {inRequest.TargetEntityId}");
                return;
            }

            SkillEffectContext hitContext = new()
            {
                SkillEntityId = inRequest.SkillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inRequest.OwnerEntityId,
                TargetEntityId = inRequest.TargetEntityId,
                ProjectileEntityId = inRequest.IsProjectileDelivery == true ? inRequest.DeliveryEntityId : 0,
                PropagationRootEntityId = inRequest.PropagationRootEntityId,
                DamageResult = damageResult,
                ImpactDirection = ResolveImpactDirection(inRequest),
                HitOriginKind = inRequest.HitOriginKind,
                HitOrigin = inRequest.HitOrigin,
            };

            if (inRequest.IsProjectileDelivery == true)
            {
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnProjectileHit, ref hitContext, inSkill.EffectiveSkillEffectIds);
            }

            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnHit, ref hitContext, inSkill.EffectiveSkillEffectIds);
            if (damageResult.IsCritical == true)
            {
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnCrit, ref hitContext, inSkill.EffectiveSkillEffectIds);
            }
            if (targetStat.CurrentHp <= 0f)
            {
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnKill, ref hitContext, inSkill.EffectiveSkillEffectIds);
            }

            // Projectile hits now share the direct-hit visual contract: only a non-evaded
            // hit creates the configured HitEffect, and its position is the target body center.
            if (damageResult.IsEvaded == false && string.IsNullOrEmpty(inSkill.Table.HitEffect) == false)
            {
                if (componentManager.TryGetComponent<TransformComponent>(inRequest.TargetEntityId, out TransformComponent targetTransform) == true)
                {
                    Vector2 effectPosition = targetTransform.Position;
                    if (componentManager.TryGetComponent<ColliderComponent>(inRequest.TargetEntityId, out ColliderComponent targetCollider) == true)
                    {
                        effectPosition += targetCollider.HitOffset;
                    }

                    AR.s.Effect.Spawn(inSkill.Table.HitEffect, effectPosition, Quaternion.identity).Forget();
                }
            }

            Debug.Log($"[SkillHitProcessor] SkillEntityId: {inRequest.SkillEntityId}, SkillId: {inSkill.SkillId}, TargetEntityId: {inRequest.TargetEntityId}, Damage: {Mathf.RoundToInt(damageResult.FinalDamage)}, Critical: {damageResult.IsCritical}, Evaded: {damageResult.IsEvaded}, Blocked: {damageResult.IsBlocked}, RemainingHP: {targetStat.CurrentHp}/{targetStat.FinalMaxHp}");
        }

        private static Vector2 ResolveImpactDirection(in SkillHitRequest inRequest)
        {
            if (inRequest.ImpactDirection.sqrMagnitude > Mathf.Epsilon)
                return inRequest.ImpactDirection.normalized;

            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<TransformComponent>(inRequest.OwnerEntityId, out TransformComponent ownerTransform) == false
                || componentManager.TryGetComponent<TransformComponent>(inRequest.TargetEntityId, out TransformComponent targetTransform) == false)
            {
                return Vector2.zero;
            }

            Vector2 ownerPosition = ownerTransform.Position;
            if (componentManager.TryGetComponent<ColliderComponent>(inRequest.OwnerEntityId, out ColliderComponent ownerCollider) == true)
            {
                ownerPosition += ownerCollider.HitOffset;
            }

            Vector2 targetPosition = targetTransform.Position;
            if (componentManager.TryGetComponent<ColliderComponent>(inRequest.TargetEntityId, out ColliderComponent targetCollider) == true)
            {
                targetPosition += targetCollider.HitOffset;
            }

            Vector2 direction = targetPosition - ownerPosition;
            return direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector2.zero;
        }
    }
}
