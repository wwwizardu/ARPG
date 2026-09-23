using ARPG.Component;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Propagation
{
    /// <summary>
    /// Creates only the concrete delivery selected by System_SkillPropagation.
    /// Root lifecycle and target selection remain outside this factory.
    /// </summary>
    public static class SkillDeliveryFactory
    {
        public static bool TryCreate(
            int inRootEntityId,
            SkillPropagationComponent inRoot,
            SkillPropagationRequestComponent inRequest,
            Vector2 inSourcePosition,
            int inTargetEntityId,
            Vector2 inTargetPosition,
            SkillPropagationTable inTable)
        {
            GE.SkillDeliveryType deliveryType = ResolveDeliveryType(inRoot.SkillEntityId, inRequest.DeliveryType);
            if (deliveryType == GE.SkillDeliveryType.Projectile)
            {
                if (AR.s.Component.TryGetComponent<SkillComponent>(inRoot.SkillEntityId, out SkillComponent skill) == false
                    || skill.Table == null
                    || skill.Table.ProjectileId <= 0)
                {
                    Debug.LogError($"[SkillDeliveryFactory] Projectile propagation requires SkillEntityId={inRoot.SkillEntityId} to have a ProjectileId.");
                    return false;
                }

                Vector2 direction = inTargetPosition - inSourcePosition;
                Vector2 travelDirection = NormalizeDirection(direction, inRequest.IncomingDirection);
                int projectileEntityId = ProjectileHelper.SpawnProjectile(
                    inRoot.OwnerEntityId,
                    inRoot.SkillEntityId,
                    skill.Table.ProjectileId,
                    inSourcePosition,
                    direction);
                if (projectileEntityId <= 0)
                    return false;

                AR.s.Component.AddComponent(projectileEntityId, new SkillPropagationBranchComponent
                {
                    RootEntityId = inRootEntityId,
                    RemainingDepth = inRequest.RemainingDepth,
                    Generation = inRequest.Generation,
                    TravelDirection = travelDirection,
                    AimedTargetEntityId = inTargetEntityId,
                });
                return true;
            }

            if (deliveryType != GE.SkillDeliveryType.Instant)
                return false;

            Vector2 deliverySourcePosition = ResolveInstantSourcePosition(inRoot, inRequest, inSourcePosition);
            Vector2 instantTravelDirection = NormalizeDirection(inTargetPosition - deliverySourcePosition, inRequest.IncomingDirection);
            int deliveryEntityId = AR.s.Entity.CreateEntity();
            AR.s.Component.AddComponent(deliveryEntityId, new TransformComponent
            {
                Position = deliverySourcePosition,
                Scale = Vector2.one,
            });
            AR.s.Component.AddComponent(deliveryEntityId, new SkillPropagationBranchComponent
            {
                RootEntityId = inRootEntityId,
                RemainingDepth = inRequest.RemainingDepth,
                Generation = inRequest.Generation,
                TravelDirection = instantTravelDirection,
            });
            AR.s.Component.AddComponent(deliveryEntityId, new InstantSkillDeliveryComponent
            {
                RootEntityId = inRootEntityId,
                TargetEntityId = inTargetEntityId,
                SourcePosition = deliverySourcePosition,
            });
            return true;
        }

        private static Vector2 ResolveInstantSourcePosition(
            SkillPropagationComponent inRoot,
            SkillPropagationRequestComponent inRequest,
            Vector2 inSourcePosition)
        {
            if (inRequest.Generation == 0
                && inRequest.SourceTargetEntityId == 0
                && SkillPropagationTargetSelector.TryGetEntityPositionSnapshot(inRoot.OwnerEntityId, out Vector2 ownerPosition) == true)
            {
                return ownerPosition;
            }

            return inSourcePosition;
        }

        private static Vector2 NormalizeDirection(Vector2 inDirection, Vector2 inFallbackDirection)
        {
            if (inDirection.sqrMagnitude > Mathf.Epsilon)
                return inDirection.normalized;
            if (inFallbackDirection.sqrMagnitude > Mathf.Epsilon)
                return inFallbackDirection.normalized;
            return Vector2.zero;
        }

        private static GE.SkillDeliveryType ResolveDeliveryType(int inSkillEntityId, GE.SkillDeliveryType inConfiguredDeliveryType)
        {
            if (inConfiguredDeliveryType != GE.SkillDeliveryType.Inherit)
                return inConfiguredDeliveryType;

            if (AR.s.Component.TryGetComponent<SkillComponent>(inSkillEntityId, out SkillComponent skill) == true
                && skill.Table != null
                && skill.Table.ProjectileId > 0)
            {
                return GE.SkillDeliveryType.Projectile;
            }

            return GE.SkillDeliveryType.Instant;
        }
    }
}
