using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Skill.Propagation;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// Moves projectiles, checks collision, and handles lifetime.
    /// Priority 150: after movement, before skill follow-up systems.
    /// </summary>
    public class System_Projectile : IFixedUpdateSystem
    {
        public int Priority => 150;
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_Projectile] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<ProjectileComponent> projectilePool = cm.GetComponentPool<ProjectileComponent>();

            if (projectilePool == null || projectilePool.Count == 0)
                return;

            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
            SparseSet<VelocityComponent> velocityPool = cm.GetComponentPool<VelocityComponent>();
            SparseSet<SkillComponent> skillPool = cm.GetComponentPool<SkillComponent>();
            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
            SparseSet<StatComponent> statPool = cm.GetComponentPool<StatComponent>();
            SparseSet<JumpComponent> jumpPool = cm.GetComponentPool<JumpComponent>();
            SparseSet<ColliderComponent> colliderPool = cm.GetComponentPool<ColliderComponent>();
            SparseSet<ProjectileTag> projectileTagPool = cm.GetComponentPool<ProjectileTag>();

            for (int i = projectilePool.Count - 1; i >= 0; i--)
            {
                int entityId = projectilePool.GetEntityId(i);

                // DestroyTag는 System_EntityDestroy(LateUpdate 950)가 소비하므로
                // 한 프레임이 fixedDeltaTime보다 길면 제거 예약된 투사체가 이 루프를
                // 한 번 더 통과한다. 재처리하면 같은 대상을 두 번 때리고 전파 요청도
                // 중복 생성된다. 형제 전파 시스템들과 같은 위치에서 걸러낸다.
                if (cm.HasComponent<DestroyTag>(entityId) == true)
                    continue;

                ProjectileComponent proj = projectilePool.GetByIndex(i);

                proj.CurrentLifeTime += inFixedDeltaTime;
                if (proj.CurrentLifeTime >= proj.LifeTime)
                {
                    if (cm.TryGetComponent<SkillPropagationBranchComponent>(entityId, out SkillPropagationBranchComponent expiredBranch) == true)
                    {
                        SkillPropagationRuntime.CompleteWork(expiredBranch.RootEntityId, entityId);
                    }
                    cm.AddComponent(entityId, new DestroyTag());
                    continue;
                }

                if (transformPool.TryGet(entityId, out var transform) == false)
                    continue;

                if (velocityPool.TryGet(entityId, out var velocity) == false)
                    continue;

                transform.Position += velocity.Direction * velocity.Speed * inFixedDeltaTime;
                transformPool.Set(entityId, transform);

                bool hasHit = CheckCollision(
                    entityId,
                    ref proj,
                    transform.Position,
                    transformPool,
                    skillPool,
                    factionPool,
                    statPool,
                    jumpPool,
                    colliderPool,
                    projectileTagPool);

                bool isPropagationDelivery = cm.HasComponent<SkillPropagationBranchComponent>(entityId);
                if (hasHit && (proj.IsPiercing == false || isPropagationDelivery == true))
                {
                    cm.AddComponent(entityId, new DestroyTag());
                    continue;
                }

                projectilePool.SetByIndex(i, proj);
            }
        }

        private bool CheckCollision(
            int projectileEntityId,
            ref ProjectileComponent proj,
            Vector2 position,
            SparseSet<TransformComponent> transformPool,
            SparseSet<SkillComponent> skillPool,
            SparseSet<FactionComponent> factionPool,
            SparseSet<StatComponent> statPool,
            SparseSet<JumpComponent> jumpPool,
            SparseSet<ColliderComponent> colliderPool,
            SparseSet<ProjectileTag> projectileTagPool)
        {
            if (skillPool.TryGet(proj.SkillEntityId, out var skill) == false)
                return false;

            // 역방향 인덱스 없이 transform 풀을 순회하며 진영으로 적만 거른다.
            // 오너에 faction이 있으면 적대 대상만, 없으면 전부(마이그레이션 안전 동작 보존).
            bool ownerHasFaction = factionPool.TryGet(proj.OwnerEntityId, out var ownerFaction);
            ComponentManager cm = AR.s.Component;
            bool isPropagationDelivery = cm.TryGetComponent<SkillPropagationBranchComponent>(projectileEntityId, out SkillPropagationBranchComponent branch);

            // 전파 투사체는 직전 적중 대상 위치에서 생성되므로 그 대상과 즉시 겹친다.
            // 이미 방문한 대상은 건너뛰어 재타격(과 그 대상에서의 전파 재시작)을 막는다.
            // 겨냥 대상은 선택 시점에 VisitedTargets에 예약돼 있으므로 예외로 둔다.
            SkillPropagationComponent propagationRoot = default;
            bool skipVisitedTargets = isPropagationDelivery == true
                && cm.TryGetComponent<SkillPropagationComponent>(branch.RootEntityId, out propagationRoot) == true;

            bool hitAny = false;
            for (int i = 0; i < transformPool.Count; i++)
            {
                int targetId = transformPool.GetEntityId(i);
                if (targetId == proj.OwnerEntityId)
                    continue;

                if (ownerHasFaction)
                {
                    if (factionPool.TryGet(targetId, out var targetFaction) == false)
                        continue;
                    if (FactionHelper.IsFactionHostile(ownerFaction.FactionId, targetFaction.FactionId) == false)
                        continue;
                }

                if (skipVisitedTargets == true
                    && targetId != branch.AimedTargetEntityId
                    && propagationRoot.VisitedTargets.Contains(targetId) == true)
                    continue;

                if (TryHitCandidate(
                        targetId, projectileEntityId, ref proj, ref skill, position,
                        transformPool, statPool, jumpPool, colliderPool, projectileTagPool))
                {
                    hitAny = true;
                    if (proj.IsPiercing == false || isPropagationDelivery == true)
                        break;
                }
            }
            return hitAny;
        }


        private bool TryHitCandidate(
            int targetId,
            int projectileEntityId,
            ref ProjectileComponent proj,
            ref SkillComponent skill,
            Vector2 position,
            SparseSet<TransformComponent> transformPool,
            SparseSet<StatComponent> statPool,
            SparseSet<JumpComponent> jumpPool,
            SparseSet<ColliderComponent> colliderPool,
            SparseSet<ProjectileTag> projectileTagPool)
        {
            if (targetId == projectileEntityId)
                return false;
            if (targetId == proj.OwnerEntityId)
                return false;
            if (statPool.Contains(targetId) == false)
                return false;
            if (projectileTagPool.Contains(targetId))
                return false;

            if (jumpPool.TryGet(targetId, out var targetJump))
            {
                if (targetJump.Height >= System_Jump.InvincibleHeight)
                    return false;
            }

            if (transformPool.TryGet(targetId, out var targetTransform) == false)
                return false;

            Vector2 targetCenter;
            float targetRadius;
            if (colliderPool.TryGet(targetId, out var targetCollider))
            {
                targetCenter = targetTransform.Position + targetCollider.HitOffset;
                targetRadius = targetCollider.HitRadius;
            }
            else
            {
                targetCenter = targetTransform.Position;
                targetRadius = 0f;
            }

            if (HitboxMath.CircleVsCircle(position, proj.HitRadius, targetCenter, targetRadius) == false)
                return false;

            bool isPropagationDelivery = AR.s.Component.TryGetComponent<SkillPropagationBranchComponent>(projectileEntityId, out SkillPropagationBranchComponent branch);
            SkillPropagationTable propagationTable = null;
            float damageMultiplier = 1f;
            int propagationRootEntityId = 0;
            int generation = 0;
            Vector2 impactDirection = Vector2.zero;
            if (isPropagationDelivery == true)
            {
                propagationRootEntityId = branch.RootEntityId;
                generation = branch.Generation;
                impactDirection = branch.TravelDirection;
                if (AR.s.Component.TryGetComponent<SkillPropagationComponent>(branch.RootEntityId, out SkillPropagationComponent root) == true)
                {
                    // 투사체는 선택된 대상이 아닌 대상을 비행 경로에서 맞을 수 있다.
                    // 실제 적중 대상을 루트 방문 목록에 즉시 기록하지 않으면
                    // 다음 세대의 후보 탐색이 같은 대상을 다시 고른다.
                    if (root.VisitedTargets.TryAdd(targetId) == true)
                    {
                        AR.s.Component.SetComponent(branch.RootEntityId, root);
                    }

                    if (SkillPropagationRuntime.TryGetTable(root.PropagationTableId, out propagationTable) == true)
                    {
                        damageMultiplier = SkillPropagationRuntime.GetDamageMultiplier(propagationTable, branch.Generation);
                    }
                }
            }
            else if (AR.s.Component.TryGetComponent<VelocityComponent>(projectileEntityId, out VelocityComponent projectileVelocity) == true)
            {
                impactDirection = projectileVelocity.Direction;
            }

            if (skill.Table != null)
            {
                SkillHitProcessor.Process(new SkillHitRequest(proj.SkillEntityId, proj.OwnerEntityId, targetId, damageMultiplier)
                {
                    DeliveryEntityId = projectileEntityId,
                    PropagationRootEntityId = propagationRootEntityId,
                    Generation = generation,
                    IsProjectileDelivery = true,
                    ImpactDirection = impactDirection,
                }, skill);
            }

            if (isPropagationDelivery == true)
            {
                if (propagationTable != null
                    && branch.RemainingDepth > 0
                    && SkillPropagationTargetSelector.TryGetEntityPositionSnapshot(targetId, out Vector2 targetPosition) == true)
                {
                    SkillPropagationRuntime.TryQueueRequest(
                        branch.RootEntityId,
                        targetId,
                        targetPosition,
                        branch.RemainingDepth - 1,
                        branch.Generation + 1,
                        propagationTable.HopInterval,
                        propagationTable.SearchRadius,
                        propagationTable.DeliveryType,
                        branch.TravelDirection);
                }

                SkillPropagationRuntime.CompleteWork(branch.RootEntityId, projectileEntityId);
            }

            return true;
        }
    }
}
