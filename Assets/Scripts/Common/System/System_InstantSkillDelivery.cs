using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Skill.Propagation;
using ARPG.Tables;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Resolves snapshot-based propagation deliveries after target eligibility is
    /// rechecked at the instant of impact.
    /// </summary>
    public class System_InstantSkillDelivery : IFixedUpdateSystem
    {
        public int Priority => 220;
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_InstantSkillDelivery] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager componentManager = AR.s.Component;
            SparseSet<InstantSkillDeliveryComponent> deliveryPool = componentManager.GetComponentPool<InstantSkillDeliveryComponent>();
            int deliveryCount = deliveryPool.Count;
            for (int i = deliveryCount - 1; i >= 0; i--)
            {
                int deliveryEntityId = deliveryPool.GetEntityId(i);
                if (componentManager.HasComponent<DestroyTag>(deliveryEntityId) == true)
                    continue;

                InstantSkillDeliveryComponent delivery = deliveryPool.GetByIndex(i);
                ProcessDelivery(deliveryEntityId, delivery);
            }
        }

        private void ProcessDelivery(int inDeliveryEntityId, InstantSkillDeliveryComponent inDelivery)
        {
            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<SkillPropagationComponent>(inDelivery.RootEntityId, out SkillPropagationComponent root) == false
                || componentManager.TryGetComponent<SkillPropagationBranchComponent>(inDeliveryEntityId, out SkillPropagationBranchComponent branch) == false
                || SkillPropagationRuntime.TryGetTable(root.PropagationTableId, out SkillPropagationTable table) == false
                || componentManager.TryGetComponent<SkillComponent>(root.SkillEntityId, out SkillComponent skill) == false)
            {
                SkillPropagationRuntime.CompleteWork(inDelivery.RootEntityId, inDeliveryEntityId);
                componentManager.AddComponent(inDeliveryEntityId, new DestroyTag());
                return;
            }

            if (SkillPropagationTargetSelector.TryGetEligibleTargetPosition(root.OwnerEntityId, inDelivery.TargetEntityId, out Vector2 targetPosition) == true)
            {
                Vector2 travelDirection = targetPosition - inDelivery.SourcePosition;
                if (travelDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    travelDirection.Normalize();
                }
                else
                {
                    travelDirection = branch.TravelDirection;
                }

                SkillHitProcessor.Process(new SkillHitRequest(
                    root.SkillEntityId,
                    root.OwnerEntityId,
                    inDelivery.TargetEntityId,
                    SkillPropagationRuntime.GetDamageMultiplier(table, branch.Generation))
                {
                    DeliveryEntityId = inDeliveryEntityId,
                    PropagationRootEntityId = inDelivery.RootEntityId,
                    Generation = branch.Generation,
                    IsProjectileDelivery = false,
                    ImpactDirection = travelDirection,
                }, skill);

                if (string.IsNullOrEmpty(table.SegmentEffectKey) == false)
                {
                    AR.s.Effect.SpawnSegment(table.SegmentEffectKey, inDelivery.SourcePosition, targetPosition).Forget();
                }

                if (branch.RemainingDepth > 0)
                {
                    SkillPropagationRuntime.TryQueueRequest(
                        inDelivery.RootEntityId,
                        inDelivery.TargetEntityId,
                        targetPosition,
                        branch.RemainingDepth - 1,
                        branch.Generation + 1,
                        table.HopInterval,
                        table.SearchRadius,
                        table.DeliveryType,
                        travelDirection);
                }
            }

            SkillPropagationRuntime.CompleteWork(inDelivery.RootEntityId, inDeliveryEntityId);
            componentManager.AddComponent(inDeliveryEntityId, new DestroyTag());
        }
    }
}
