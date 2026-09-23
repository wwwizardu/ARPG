using ARPG.Component;
using ARPG.Systems;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Skill.Propagation
{
    /// <summary>
    /// Stateless hostile-target query for a propagation hop. It never owns target state;
    /// the root's inline reservation set supplies branch-wide duplicate prevention.
    /// </summary>
    public static class SkillPropagationTargetSelector
    {
        public static bool TrySelectClosest(
            int inOwnerEntityId,
            Vector2 inSourcePosition,
            float inSearchRadius,
            Vector2 inIncomingDirection,
            float inMaxTurnAngleDeg,
            in SkillPropagationTargetIds inVisitedTargets,
            out int outTargetEntityId,
            out Vector2 outTargetPosition)
        {
            outTargetEntityId = 0;
            outTargetPosition = Vector2.zero;

            ComponentManager componentManager = AR.s.Component;
            SparseSet<FactionComponent> factionPool = componentManager.GetComponentPool<FactionComponent>();
            if (factionPool.TryGet(inOwnerEntityId, out FactionComponent ownerFaction) == false)
                return false;

            SparseSet<TransformComponent> transformPool = componentManager.GetComponentPool<TransformComponent>();
            float radiusSqr = inSearchRadius * inSearchRadius;
            float selectedDistanceSqr = float.MaxValue;
            for (int i = 0; i < transformPool.Count; i++)
            {
                int candidateEntityId = transformPool.GetEntityId(i);
                if (candidateEntityId == inOwnerEntityId || inVisitedTargets.Contains(candidateEntityId) == true)
                    continue;

                // 시전자 진영과 후보 Transform은 루프에서 이미 확보했으므로 후보마다 재조회하지 않는다.
                if (TryResolveEligiblePosition(
                        componentManager,
                        inOwnerEntityId,
                        ownerFaction,
                        candidateEntityId,
                        transformPool.GetByIndex(i),
                        out Vector2 candidatePosition) == false)
                    continue;

                Vector2 candidateOffset = candidatePosition - inSourcePosition;
                float distanceSqr = candidateOffset.sqrMagnitude;
                if (distanceSqr > radiusSqr)
                    continue;
                if (IsWithinTurnAngle(inIncomingDirection, candidateOffset, inMaxTurnAngleDeg) == false)
                    continue;

                if (outTargetEntityId == 0
                    || distanceSqr < selectedDistanceSqr
                    || (Mathf.Approximately(distanceSqr, selectedDistanceSqr) == true && candidateEntityId < outTargetEntityId))
                {
                    outTargetEntityId = candidateEntityId;
                    outTargetPosition = candidatePosition;
                    selectedDistanceSqr = distanceSqr;
                }
            }

            return outTargetEntityId > 0;
        }

        public static bool TryGetEligibleTargetPosition(int inOwnerEntityId, int inCandidateEntityId, out Vector2 outTargetPosition)
        {
            outTargetPosition = Vector2.zero;

            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<FactionComponent>(inOwnerEntityId, out FactionComponent ownerFaction) == false
                || componentManager.TryGetComponent<TransformComponent>(inCandidateEntityId, out TransformComponent candidateTransform) == false)
            {
                return false;
            }

            return TryResolveEligiblePosition(
                componentManager,
                inOwnerEntityId,
                ownerFaction,
                inCandidateEntityId,
                candidateTransform,
                out outTargetPosition);
        }

        /// <summary>
        /// 적격성 판정 본체. 시전자 진영과 후보 Transform은 호출자가 조회해서 넘긴다.
        /// </summary>
        private static bool TryResolveEligiblePosition(
            ComponentManager inComponentManager,
            int inOwnerEntityId,
            FactionComponent inOwnerFaction,
            int inCandidateEntityId,
            TransformComponent inCandidateTransform,
            out Vector2 outTargetPosition)
        {
            outTargetPosition = Vector2.zero;
            if (inCandidateEntityId <= 0 || inCandidateEntityId == inOwnerEntityId)
                return false;

            if (inComponentManager.HasComponent<DestroyTag>(inCandidateEntityId) == true)
                return false;
            if (inComponentManager.TryGetComponent<FactionComponent>(inCandidateEntityId, out FactionComponent candidateFaction) == false
                || FactionHelper.IsFactionHostile(inOwnerFaction.FactionId, candidateFaction.FactionId) == false)
            {
                return false;
            }
            if (inComponentManager.TryGetComponent<StatComponent>(inCandidateEntityId, out StatComponent candidateStat) == false
                || candidateStat.CurrentHp <= 0f)
            {
                return false;
            }
            if (inComponentManager.TryGetComponent<JumpComponent>(inCandidateEntityId, out JumpComponent candidateJump) == true
                && candidateJump.Height >= System_Jump.InvincibleHeight)
            {
                return false;
            }

            outTargetPosition = inCandidateTransform.Position;
            if (inComponentManager.TryGetComponent<ColliderComponent>(inCandidateEntityId, out ColliderComponent targetCollider) == true)
            {
                outTargetPosition += targetCollider.HitOffset;
            }

            return true;
        }

        /// <summary>
        /// Returns the last available collider-center snapshot without requiring a living
        /// or selectable entity. OnHit propagation needs this even when its source was
        /// killed by the source hit itself, and an empty commit uses it for the caster.
        /// </summary>
        public static bool TryGetEntityPositionSnapshot(int inEntityId, out Vector2 outTargetPosition)
        {
            outTargetPosition = Vector2.zero;
            if (inEntityId <= 0)
                return false;

            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<TransformComponent>(inEntityId, out TransformComponent targetTransform) == false)
                return false;

            outTargetPosition = targetTransform.Position;
            if (componentManager.TryGetComponent<ColliderComponent>(inEntityId, out ColliderComponent targetCollider) == true)
            {
                outTargetPosition += targetCollider.HitOffset;
            }

            return true;
        }

        private static bool IsWithinTurnAngle(Vector2 inIncomingDirection, Vector2 inCandidateOffset, float inMaxTurnAngleDeg)
        {
            if (inIncomingDirection.sqrMagnitude <= Mathf.Epsilon
                || inCandidateOffset.sqrMagnitude <= Mathf.Epsilon
                || inMaxTurnAngleDeg >= 180f)
            {
                return true;
            }

            float clampedAngle = Mathf.Clamp(inMaxTurnAngleDeg, 0f, 180f);
            float minimumDot = Mathf.Cos(clampedAngle * Mathf.Deg2Rad);
            float normalizedDot = Vector2.Dot(inIncomingDirection, inCandidateOffset)
                / Mathf.Sqrt(inIncomingDirection.sqrMagnitude * inCandidateOffset.sqrMagnitude);
            return normalizedDot >= minimumDot;
        }
    }
}
