#nullable enable
using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Tables;
using Cysharp.Threading.Tasks;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Propagation
{
    /// <summary>
    /// Stateless lifecycle operations for a propagation root and its delayed work.
    /// Components own every mutable value; this class only performs atomic ECS updates.
    /// </summary>
    public static class SkillPropagationRuntime
    {
        public static bool TryStartOnSkillCommit(int inPropagationTableId, ref SkillEffectContext inContext)
        {
            if (inContext.PropagationRootEntityId > 0)
            {
                Debug.LogError($"[SkillPropagation] OnSkillCommit received more than one active profile for SkillEntityId={inContext.SkillEntityId}.");
                return false;
            }

            if (TryGetTable(inPropagationTableId, out SkillPropagationTable table) == false)
                return false;
            if (IsDeliveryCapValid(table, GE.SkillTrigger.OnSkillCommit) == false)
                return false;

            // Chain Lightning is still a valid cast when its committed point has no
            // target. Do not create a delayed root that could acquire a target which
            // walks into range later; only play the cast-to-point visual snapshot.
            SkillPropagationTargetIds noReservedTargets = default;
            if (SkillPropagationTargetSelector.TrySelectClosest(
                    inContext.OwnerEntityId,
                    inContext.TargetPosition,
                    table.InitialSearchRadius,
                    Vector2.zero,
                    table.MaxTurnAngleDeg,
                    in noReservedTargets,
                    out _,
                    out _) == false)
            {
                SpawnEmptyCommitSegment(table, inContext.OwnerEntityId, inContext.TargetPosition);
                return true;
            }

            int rootEntityId = CreateRoot(inContext.OwnerEntityId, inContext.SkillEntityId, inPropagationTableId);
            if (TryQueueRequest(rootEntityId, 0, inContext.TargetPosition, table.MaxDepth, 0, table.HopInterval, table.InitialSearchRadius, table.DeliveryType, Vector2.zero) == false)
            {
                AR.s.Component.AddComponent(rootEntityId, new DestroyTag());
                return false;
            }

            inContext.PropagationRootEntityId = rootEntityId;
            return true;
        }

        public static bool TryStartOnHit(int inPropagationTableId, ref SkillEffectContext inContext)
        {
            // A propagation child continues its existing root in System_InstantSkillDelivery
            // (and later System_Projectile), never by creating a second root from its OnHit.
            if (inContext.PropagationRootEntityId > 0)
                return true;

            if (TryGetTable(inPropagationTableId, out SkillPropagationTable table) == false)
                return false;
            if (IsDeliveryCapValid(table, GE.SkillTrigger.OnHit) == false)
                return false;

            // OnHit treats the source hit as generation zero outside the propagation tree.
            if (table.MaxDepth <= 0)
                return true;

            if (SkillPropagationTargetSelector.TryGetEntityPositionSnapshot(inContext.TargetEntityId, out Vector2 sourcePosition) == false)
                return false;

            int rootEntityId = CreateRoot(inContext.OwnerEntityId, inContext.SkillEntityId, inPropagationTableId);
            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<SkillPropagationComponent>(rootEntityId, out SkillPropagationComponent root) == false)
                return false;

            if (root.VisitedTargets.TryAdd(inContext.TargetEntityId) == false)
            {
                componentManager.AddComponent(rootEntityId, new DestroyTag());
                return false;
            }

            componentManager.SetComponent(rootEntityId, root);
            if (TryQueueRequest(rootEntityId, inContext.TargetEntityId, sourcePosition, table.MaxDepth - 1, 1, table.HopInterval, table.SearchRadius, table.DeliveryType, inContext.ImpactDirection) == false)
            {
                componentManager.AddComponent(rootEntityId, new DestroyTag());
                return false;
            }

            inContext.PropagationRootEntityId = rootEntityId;
            return true;
        }

        public static bool TryGetTable(int inPropagationTableId, out SkillPropagationTable outTable)
        {
            outTable = null!;
            SkillPropagationTable? table = AR.s.Data.GetSkillPropagation(inPropagationTableId);
            if (table == null)
            {
                Debug.LogError($"[SkillPropagation] SkillPropagationTable not found: id={inPropagationTableId}.");
                return false;
            }

            // 필드 범위는 DownloadTables.ParseSkillPropagationTable이 다운로드 시 클램프하므로 런타임에서 재검사하지 않는다.
            outTable = table;
            return true;
        }

        public static bool TryQueueRequest(
            int inRootEntityId,
            int inSourceTargetEntityId,
            Vector2 inSourcePosition,
            int inRemainingDepth,
            int inGeneration,
            float inDelay,
            float inSearchRadius,
            GE.SkillDeliveryType inDeliveryType,
            Vector2 inIncomingDirection)
        {
            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<SkillPropagationComponent>(inRootEntityId, out SkillPropagationComponent root) == false)
                return false;
            if (componentManager.HasComponent<DestroyTag>(inRootEntityId) == true)
                return false;

            root.OutstandingDeliveryCount++;
            componentManager.SetComponent(inRootEntityId, root);

            int requestEntityId = AR.s.Entity.CreateEntity();
            componentManager.AddComponent(requestEntityId, new TransformComponent
            {
                Position = inSourcePosition,
                Scale = Vector2.one,
            });
            componentManager.AddComponent(requestEntityId, new SkillPropagationRequestComponent
            {
                RootEntityId = inRootEntityId,
                SourceTargetEntityId = inSourceTargetEntityId,
                IsFollowingSourceTarget = inSourceTargetEntityId > 0,
                RemainingDepth = inRemainingDepth,
                Generation = inGeneration,
                RemainingDelay = Mathf.Max(0f, inDelay),
                SearchRadius = Mathf.Max(0f, inSearchRadius),
                DeliveryType = inDeliveryType,
                IncomingDirection = inIncomingDirection.sqrMagnitude > Mathf.Epsilon
                    ? inIncomingDirection.normalized
                    : Vector2.zero,
            });
            return true;
        }

        public static void CompleteWork(int inRootEntityId, int inWorkEntityId)
        {
            ComponentManager componentManager = AR.s.Component;
            if (inWorkEntityId > 0)
            {
                if (componentManager.HasComponent<SkillPropagationWorkCompletedTag>(inWorkEntityId) == true)
                    return;

                componentManager.AddComponent(inWorkEntityId, new SkillPropagationWorkCompletedTag());
            }

            CompleteOutstandingDelivery(inRootEntityId);
        }

        public static void CompleteOutstandingDelivery(int inRootEntityId)
        {
            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<SkillPropagationComponent>(inRootEntityId, out SkillPropagationComponent root) == false)
                return;

            if (root.OutstandingDeliveryCount <= 0)
            {
                Debug.LogError($"[SkillPropagation] Outstanding count underflow prevented for RootEntityId={inRootEntityId}.");
                root.OutstandingDeliveryCount = 0;
                componentManager.SetComponent(inRootEntityId, root);
                componentManager.AddComponent(inRootEntityId, new DestroyTag());
                return;
            }

            root.OutstandingDeliveryCount--;
            componentManager.SetComponent(inRootEntityId, root);
            if (root.OutstandingDeliveryCount == 0)
            {
                componentManager.AddComponent(inRootEntityId, new DestroyTag());
            }
        }

        public static float GetDamageMultiplier(SkillPropagationTable inTable, int inGeneration)
        {
            float perGenerationRatio = Mathf.Max(0f, inTable.DamageRatioPerGeneration) / 100f;
            return Mathf.Pow(perGenerationRatio, Mathf.Max(0, inGeneration));
        }

        /// <summary>
        /// 대상 없이 커밋 지점을 향해 허공에 치는 연출. 커밋 순간 대상이 없을 때와,
        /// 커밋 후 HopInterval 동안 대상이 사라져 첫 요청이 아무도 못 찾았을 때 둘 다 사용한다.
        /// </summary>
        public static void SpawnEmptyCommitSegment(SkillPropagationTable inTable, int inOwnerEntityId, Vector2 inTargetPosition)
        {
            if (inTable.DeliveryType != GE.SkillDeliveryType.Instant
                || string.IsNullOrEmpty(inTable.SegmentEffectKey) == true
                || SkillPropagationTargetSelector.TryGetEntityPositionSnapshot(inOwnerEntityId, out Vector2 sourcePosition) == false)
            {
                return;
            }

            AR.s.Effect.SpawnSegment(inTable.SegmentEffectKey, sourcePosition, inTargetPosition).Forget();
        }

        private static int CreateRoot(int inOwnerEntityId, int inSkillEntityId, int inPropagationTableId)
        {
            int rootEntityId = AR.s.Entity.CreateEntity();
            AR.s.Component.AddComponent(rootEntityId, new SkillPropagationComponent
            {
                OwnerEntityId = inOwnerEntityId,
                SkillEntityId = inSkillEntityId,
                PropagationTableId = inPropagationTableId,
            });
            return rootEntityId;
        }

        private static bool IsDeliveryCapValid(SkillPropagationTable inTable, GE.SkillTrigger inTrigger)
        {
            int generationCount = inTrigger == GE.SkillTrigger.OnSkillCommit
                ? inTable.MaxDepth + 1
                : inTable.MaxDepth;
            long totalDeliveries = 0;
            long deliveriesInGeneration = 1;
            for (int i = 0; i < generationCount; i++)
            {
                deliveriesInGeneration *= inTable.ChildCount;
                totalDeliveries += deliveriesInGeneration;
                if (totalDeliveries > inTable.MaxTotalDeliveries)
                {
                    Debug.LogError($"[SkillPropagation] Theoretical {inTrigger} deliveries ({totalDeliveries}) exceed MaxTotalDeliveries ({inTable.MaxTotalDeliveries}) for Id={inTable.Id}({inTable.Name}).");
                    return false;
                }
            }

            return true;
        }
    }
}
