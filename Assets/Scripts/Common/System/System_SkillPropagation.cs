using ARPG.Component;
using ARPG.Skill.Propagation;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Consumes delayed propagation requests and creates deterministic child deliveries.
    /// </summary>
    public class System_SkillPropagation : IFixedUpdateSystem
    {
        public int Priority => 210;
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_SkillPropagation] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager componentManager = AR.s.Component;
            SparseSet<SkillPropagationRequestComponent> requestPool = componentManager.GetComponentPool<SkillPropagationRequestComponent>();
            SparseSet<TransformComponent> transformPool = componentManager.GetComponentPool<TransformComponent>();
            int requestCount = requestPool.Count;
            for (int i = requestCount - 1; i >= 0; i--)
            {
                int requestEntityId = requestPool.GetEntityId(i);
                if (componentManager.HasComponent<DestroyTag>(requestEntityId) == true)
                    continue;

                SkillPropagationRequestComponent request = requestPool.GetByIndex(i);
                FollowSourceTarget(requestEntityId, ref request, transformPool);
                request.RemainingDelay -= inFixedDeltaTime;
                if (request.RemainingDelay > 0f)
                {
                    requestPool.SetByIndex(i, request);
                    continue;
                }

                ProcessRequest(requestEntityId, request, transformPool);
            }
        }

        /// <summary>
        /// 대기 중인 주문을 source 대상에 붙여 따라가게 한다.
        /// 대상이 제거(예약)되면 추적을 끊고 마지막 위치에서 다음 대상으로 튄다.
        /// 추적을 끊어두면 이후 같은 ID를 다른 엔티티가 받더라도 따라가지 않는다.
        /// </summary>
        private static void FollowSourceTarget(
            int inRequestEntityId,
            ref SkillPropagationRequestComponent ioRequest,
            SparseSet<TransformComponent> inTransformPool)
        {
            if (ioRequest.IsFollowingSourceTarget == false)
                return;

            if (AR.s.Component.HasComponent<DestroyTag>(ioRequest.SourceTargetEntityId) == true
                || SkillPropagationTargetSelector.TryGetEntityPositionSnapshot(ioRequest.SourceTargetEntityId, out Vector2 targetPosition) == false)
            {
                ioRequest.IsFollowingSourceTarget = false;
                return;
            }

            if (inTransformPool.TryGet(inRequestEntityId, out TransformComponent requestTransform) == false)
                return;

            requestTransform.Position = targetPosition;
            inTransformPool.Set(inRequestEntityId, requestTransform);
        }

        private void ProcessRequest(
            int inRequestEntityId,
            SkillPropagationRequestComponent inRequest,
            SparseSet<TransformComponent> inTransformPool)
        {
            ComponentManager componentManager = AR.s.Component;
            if (componentManager.TryGetComponent<SkillPropagationComponent>(inRequest.RootEntityId, out SkillPropagationComponent root) == false
                || SkillPropagationRuntime.TryGetTable(root.PropagationTableId, out SkillPropagationTable table) == false
                || inTransformPool.TryGet(inRequestEntityId, out TransformComponent requestTransform) == false)
            {
                SkillPropagationRuntime.CompleteWork(inRequest.RootEntityId, inRequestEntityId);
                componentManager.AddComponent(inRequestEntityId, new DestroyTag());
                return;
            }

            Vector2 sourcePosition = requestTransform.Position;
            int spawnedThisRequest = 0;
            for (int childIndex = 0; childIndex < table.ChildCount; childIndex++)
            {
                if (root.SpawnedDeliveryCount >= table.MaxTotalDeliveries)
                    break;

                if (SkillPropagationTargetSelector.TrySelectClosest(
                        root.OwnerEntityId,
                        sourcePosition,
                        inRequest.SearchRadius,
                        inRequest.IncomingDirection,
                        table.MaxTurnAngleDeg,
                        in root.VisitedTargets,
                        out int targetEntityId,
                        out Vector2 targetPosition) == false)
                    break;

                if (root.VisitedTargets.TryAdd(targetEntityId) == false)
                    break;

                if (SkillDeliveryFactory.TryCreate(inRequest.RootEntityId, root, inRequest, sourcePosition, targetEntityId, targetPosition, table) == false)
                    break;

                root.SpawnedDeliveryCount++;
                root.OutstandingDeliveryCount++;
                spawnedThisRequest++;
            }

            // 커밋 순간엔 대상이 있어 루트가 만들어졌지만 HopInterval 동안 사라진 경우.
            // 기본 타격도 생략된 상태라 아무 연출이 없으면 시전이 무시된 것처럼 보이므로 허공 연출을 재생한다.
            if (spawnedThisRequest == 0 && inRequest.Generation == 0 && inRequest.SourceTargetEntityId == 0)
            {
                SkillPropagationRuntime.SpawnEmptyCommitSegment(table, root.OwnerEntityId, sourcePosition);
            }

            componentManager.SetComponent(inRequest.RootEntityId, root);
            SkillPropagationRuntime.CompleteWork(inRequest.RootEntityId, inRequestEntityId);
            componentManager.AddComponent(inRequestEntityId, new DestroyTag());
        }
    }
}
