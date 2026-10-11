#nullable enable
using ARPG.Component;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 넉백·당기기 시작 판정의 단일 정본 (SKILL_DESIGN §6.10). 상태를 갖지 않는다.
    /// 적용 여부·방향·멈춤 거리를 계산해 대상에 ForcedMoveComponent를 붙이고, 진행은 System_ForcedMove가 맡는다.
    /// 스킬 적중(SkillEffectExecutor)과 장판(System_AreaEffect)이 같이 쓴다.
    /// </summary>
    public static class ForcedMoveStarter
    {
        // 기준점과 대상이 이보다 가까우면 방향을 정할 수 없다고 본다
        private const float MIN_DIRECTION_SQR = 0.0001f;

        /// <summary>
        /// 스킬 적중에서 시작한다. 기준점은 ctx.HitOriginKind/HitOrigin이고,
        /// 범위 중심이 없는 타격(발사체·연쇄)은 밀기면 날아온 방향, 당기기면 공격자 쪽으로 움직인다.
        /// </summary>
        public static void TryStartFromHit(SkillEffectTable inEffect, in SkillEffectContext inContext)
        {
            // OnHit은 회피해도 실행되므로 여기서 거른다
            if (inContext.DamageResult.IsEvaded == true)
                return;

            ComponentManager cm = AR.s.Component;
            if (CanStart(cm, inContext.TargetEntityId, out Vector2 targetPosition) == false)
                return;

            if (inEffect.EffectType == GE.Stat.Knockback)
            {
                Vector2 direction;
                if (inContext.HitOriginKind == HitOriginKind.None)
                {
                    direction = inContext.ImpactDirection;
                    if (direction.sqrMagnitude < MIN_DIRECTION_SQR
                        && TryGetPosition(cm, inContext.OwnerEntityId, out Vector2 ownerPosition) == true)
                    {
                        direction = targetPosition - ownerPosition;
                    }
                }
                else
                {
                    direction = targetPosition - inContext.HitOrigin;
                    // 기준점에 겹쳐 있으면 공격자 조준 방향으로 민다
                    if (direction.sqrMagnitude < MIN_DIRECTION_SQR
                        && cm.TryGetComponent<SkillTargetComponent>(inContext.SkillEntityId, out SkillTargetComponent skillTarget) == true)
                    {
                        direction = skillTarget.TargetDirection;
                    }
                }

                Attach(cm, inContext.TargetEntityId, direction, inEffect.Param1, inEffect.Param2);
                return;
            }

            Vector2 origin;
            float stopDistance;
            if (inContext.HitOriginKind == HitOriginKind.Point)
            {
                origin = inContext.HitOrigin;
                stopDistance = 0f;
            }
            else
            {
                // Owner는 커밋 시점 공격자 위치, None(발사체·연쇄)은 지금 공격자 위치를 기준점으로 쓴다
                if (inContext.HitOriginKind == HitOriginKind.Owner)
                    origin = inContext.HitOrigin;
                else if (TryGetPosition(cm, inContext.OwnerEntityId, out origin) == false)
                    return;

                stopDistance = GetHitRadius(cm, inContext.OwnerEntityId) + GetHitRadius(cm, inContext.TargetEntityId);
            }

            StartPull(cm, inContext.TargetEntityId, targetPosition, origin, stopDistance, inEffect);
        }

        /// <summary>
        /// 장판·폭발에서 시작한다. 기준점은 장판 중심이고, 당기기는 중심에서 멈춘다.
        /// 장판은 SkillEffectExecutor를 거치지 않으므로 발동 확률도 여기서 판정한다.
        /// </summary>
        public static void TryStartFromArea(int inForcedMoveEffectId, int inTargetEntityId, Vector2 inCenter)
        {
            SkillEffectTable? effect = AR.s.Data.GetSkillEffect(inForcedMoveEffectId);
            if (effect == null || (effect.EffectType != GE.Stat.Knockback && effect.EffectType != GE.Stat.Pull))
            {
                Debug.LogError($"[ForcedMoveStarter] AreaEffect ForcedMoveEffectId={inForcedMoveEffectId}는 Knockback/Pull SkillEffect 행이어야 합니다. (EffectType={effect?.EffectType})");
                return;
            }

            if (effect.Probability <= 0)
                return;
            if (effect.Probability < 100 && Random.Range(0, 100) >= effect.Probability)
                return;

            ComponentManager cm = AR.s.Component;
            if (CanStart(cm, inTargetEntityId, out Vector2 targetPosition) == false)
                return;

            // 중심에 겹친 대상은 방향이 없어 Attach가 거른다
            if (effect.EffectType == GE.Stat.Knockback)
                Attach(cm, inTargetEntityId, targetPosition - inCenter, effect.Param1, effect.Param2);
            else
                StartPull(cm, inTargetEntityId, targetPosition, inCenter, 0f, effect);
        }

        /// <summary>
        /// 밀 수 있는 대상인지. 살아 있고 스스로 움직이며, 점프 중이거나 이미 밀리는 중이 아니어야 한다.
        /// 밀리는 중에 온 새 넉백·당기기는 무시한다 (멀티샷 동시 적중이 한 번만 밀리도록).
        /// </summary>
        private static bool CanStart(ComponentManager inCm, int inTargetEntityId, out Vector2 outPosition)
        {
            outPosition = Vector2.zero;
            if (inTargetEntityId <= 0)
                return false;

            if (inCm.TryGetComponent<StatComponent>(inTargetEntityId, out StatComponent stat) == false || stat.CurrentHp <= 0)
                return false;

            if (inCm.HasComponent<DestroyTag>(inTargetEntityId) == true
                || inCm.HasComponent<ForcedMoveComponent>(inTargetEntityId) == true
                || inCm.HasComponent<JumpComponent>(inTargetEntityId) == true
                || inCm.HasComponent<TotemTag>(inTargetEntityId) == true
                || inCm.HasComponent<VelocityComponent>(inTargetEntityId) == false)
            {
                return false;
            }

            return TryGetPosition(inCm, inTargetEntityId, out outPosition);
        }

        /// <summary>
        /// 기준점 쪽으로 당긴다. 멈춤 거리(기준점에서 inStopDistance) 안쪽이면 움직이지 않고, 설정 거리는 상한이다.
        /// </summary>
        private static void StartPull(ComponentManager inCm, int inTargetEntityId, Vector2 inTargetPosition,
            Vector2 inOrigin, float inStopDistance, SkillEffectTable inEffect)
        {
            Vector2 toOrigin = inOrigin - inTargetPosition;
            float distanceToStop = toOrigin.magnitude - inStopDistance;
            if (distanceToStop <= 0f)
                return;

            Attach(inCm, inTargetEntityId, toOrigin, Mathf.Min(inEffect.Param1, distanceToStop), inEffect.Param2);
        }

        private static void Attach(ComponentManager inCm, int inTargetEntityId, Vector2 inDirection, float inDistance, float inDuration)
        {
            // 거리·시간이 0 이하인 행은 다운로드 검사가 오류로 보고한다
            if (inDirection.sqrMagnitude < MIN_DIRECTION_SQR || inDistance <= 0f || inDuration <= 0f)
                return;

            inCm.AddComponent(inTargetEntityId, new ForcedMoveComponent
            {
                Direction = inDirection.normalized,
                Distance = inDistance,
                Duration = inDuration,
            });
        }

        private static bool TryGetPosition(ComponentManager inCm, int inEntityId, out Vector2 outPosition)
        {
            if (inCm.TryGetComponent<TransformComponent>(inEntityId, out TransformComponent transform) == true)
            {
                outPosition = transform.Position;
                return true;
            }

            outPosition = Vector2.zero;
            return false;
        }

        private static float GetHitRadius(ComponentManager inCm, int inEntityId)
        {
            return inCm.TryGetComponent<ColliderComponent>(inEntityId, out ColliderComponent collider) == true
                ? collider.HitRadius
                : 0f;
        }
    }
}
