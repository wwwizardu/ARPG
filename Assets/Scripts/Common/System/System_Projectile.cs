using ARPG.Component;
using ARPG.Factory;
using ARPG.Skill.Combat;
using ARPG.Skill.Propagation;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 발사체의 충돌과 수명을 처리한다. Priority 150: 이동 의도 뒤, 스킬 후속 시스템 앞.
    /// 직선 발사체의 위치는 다른 이동과 같이 System_Render가 속도로 옮긴다. 이 시스템은 지난 검사 위치부터
    /// 지금 위치까지의 선분으로 충돌을 검사한다. 포물선 발사체만 정해진 경로를 이 시스템이 직접 계산한다.
    /// </summary>
    public class System_Projectile : IFixedUpdateSystem
    {
        public int Priority => 150;
        public float UpdateInterval => 0f;

        // 관통 발사체가 이번 검사에서 새로 들어온 대상. 판정 뒤 몰아서 맞힌다 (매 검사 전에 비우는 임시 목록)
        private readonly System.Collections.Generic.List<int> _enteredTargetsScratch = new(16);

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
            SparseSet<SkillComponent> skillPool = cm.GetComponentPool<SkillComponent>();
            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
            SparseSet<StatComponent> statPool = cm.GetComponentPool<StatComponent>();
            SparseSet<JumpComponent> jumpPool = cm.GetComponentPool<JumpComponent>();
            SparseSet<ColliderComponent> colliderPool = cm.GetComponentPool<ColliderComponent>();
            SparseSet<ProjectileTag> projectileTagPool = cm.GetComponentPool<ProjectileTag>();
            SparseSet<ProjectileArcComponent> arcPool = cm.GetComponentPool<ProjectileArcComponent>();

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

                // 포물선 발사체는 수명·충돌 대신 정해진 경로를 날아가 착지 지점에 장판을 깐다.
                if (arcPool.TryGet(entityId, out ProjectileArcComponent arc) == true)
                {
                    UpdateArcFlight(cm, entityId, proj.OwnerEntityId, ref arc, inFixedDeltaTime, transformPool);
                    arcPool.Set(entityId, arc);
                    continue;
                }

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

                // 위치는 System_Render가 이미 옮겼다. 지난 검사 위치부터 지금 위치까지 지나온 선분을 검사한다.
                Vector2 sweepStart = proj.LastSweepPosition;
                Vector2 sweepEnd = transform.Position;
                proj.LastSweepPosition = sweepEnd;

                bool hasHit = CheckCollision(
                    entityId,
                    ref proj,
                    sweepStart,
                    sweepEnd,
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

        private static void UpdateArcFlight(
            ComponentManager cm,
            int entityId,
            int ownerEntityId,
            ref ProjectileArcComponent arc,
            float inFixedDeltaTime,
            SparseSet<TransformComponent> transformPool)
        {
            arc.Elapsed += inFixedDeltaTime;
            float progress = arc.FlightDuration > 0f ? Mathf.Clamp01(arc.Elapsed / arc.FlightDuration) : 1f;

            if (transformPool.TryGet(entityId, out TransformComponent transform) == true)
            {
                transform.Position = Vector2.Lerp(arc.StartPosition, arc.EndPosition, progress);
                transformPool.Set(entityId, transform);
            }

            // 출발 높이(몸통 중심)는 착지까지 0으로 줄고, 그 위에 최대 ArcHeight인 포물선을 더한다.
            arc.Height = Mathf.Lerp(arc.StartHeight, 0f, progress) + 4f * arc.ArcHeight * progress * (1f - progress);
            if (progress < 1f)
                return;

            EntityFactory.CreateAreaEffect(ownerEntityId, arc.LandingAreaEffectId, arc.EndPosition, arc.SkillId, arc.CasterFaction);
            cm.AddComponent(entityId, new DestroyTag());
        }

        /// <summary>
        /// 지난 검사 위치 → 지금 위치 선분에 걸린 적을 맞힌다. 하나라도 맞혔으면 true.
        /// 첫 적중에 사라지는 발사체(일반·전파)는 선분에서 먼저 닿은 대상 하나만 맞힌다.
        /// 관통 발사체는 새로 들어온 대상을 모두 맞히고, 맞힌 뒤 아직 겹쳐 있는 대상은 다시 맞히지 않는다.
        /// 빠져나간 대상은 목록에서 빠지므로 다시 들어오면 또 맞는다.
        /// </summary>
        private bool CheckCollision(
            int projectileEntityId,
            ref ProjectileComponent proj,
            Vector2 sweepStart,
            Vector2 sweepEnd,
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

            bool stopsOnFirstHit = proj.IsPiercing == false || isPropagationDelivery == true;
            bool hasFirstTarget = false;
            int firstTargetId = 0;
            float firstEntryDistance = float.MaxValue;

            // 관통: 이미 맞힌 대상 중 이번 검사에서도 겹쳐 있는 대상만 다음 목록으로 옮긴다.
            // 옮기지 못한 대상(빠져나감·사망·적대 해제)은 목록에서 빠진다.
            SkillPropagationTargetIds stillOverlapping = default;
            _enteredTargetsScratch.Clear();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int targetId = transformPool.GetEntityId(i);
                if (targetId == proj.OwnerEntityId || targetId == projectileEntityId)
                    continue;

                if (ownerHasFaction)
                {
                    if (factionPool.TryGet(targetId, out var targetFaction) == false)
                        continue;
                    if (FactionHelper.IsEntityHostile(proj.OwnerEntityId, ownerFaction.FactionId, targetId, targetFaction.FactionId) == false)
                        continue;
                }

                if (skipVisitedTargets == true
                    && targetId != branch.AimedTargetEntityId
                    && propagationRoot.VisitedTargets.Contains(targetId) == true)
                    continue;

                // 맞을 수 있는 대상은 스탯이 있는 생명체뿐이다 (다른 발사체 제외)
                if (statPool.Contains(targetId) == false || projectileTagPool.Contains(targetId) == true)
                    continue;

                GetHitCircle(targetId, transformPool.GetByIndex(i).Position, colliderPool, out Vector2 targetCenter, out float targetRadius);
                if (HitboxMath.SegmentVsCircle(sweepStart, sweepEnd, proj.HitRadius, targetCenter, targetRadius) == false)
                    continue;

                if (stopsOnFirstHit == true)
                {
                    if (IsJumpInvincible(targetId, jumpPool) == true)
                        continue;

                    float entryDistance = HitboxMath.SegmentCircleEntryDistance(sweepStart, sweepEnd, proj.HitRadius, targetCenter, targetRadius);
                    if (hasFirstTarget == false || entryDistance < firstEntryDistance)
                    {
                        hasFirstTarget = true;
                        firstTargetId = targetId;
                        firstEntryDistance = entryDistance;
                    }
                    continue;
                }

                // 겹쳐 있는지는 위치로만 판단한다. 맞은 뒤 겹친 채 점프한 대상을 목록에서 빼면 착지할 때 또 맞는다.
                if (proj.OverlappingHitTargets.Contains(targetId) == true)
                {
                    stillOverlapping.TryAdd(targetId);
                    continue;
                }

                if (IsJumpInvincible(targetId, jumpPool) == true)
                    continue;

                _enteredTargetsScratch.Add(targetId);
            }

            if (stopsOnFirstHit == true)
            {
                if (hasFirstTarget == false)
                    return false;

                ApplyHit(firstTargetId, projectileEntityId, ref proj, ref skill);
                return true;
            }

            // 맞히기는 대상 순회가 끝난 뒤 한다. 적중 처리(사망·효과 발동)가 순회 중인 풀을 바꾸지 않게 한다.
            bool hitAny = false;
            for (int i = 0; i < _enteredTargetsScratch.Count; i++)
            {
                // 목록이 가득 차면 자리가 날 때까지 새 대상을 맞히지 않는다. 기록 없이 맞히면 겹쳐 있는 동안 검사마다 다시 맞는다.
                if (stillOverlapping.Count >= SkillPropagationTargetIds.Capacity)
                    break;

                int targetId = _enteredTargetsScratch[i];
                stillOverlapping.TryAdd(targetId);
                ApplyHit(targetId, projectileEntityId, ref proj, ref skill);
                hitAny = true;
            }

            proj.OverlappingHitTargets = stillOverlapping;
            return hitAny;
        }

        private static void GetHitCircle(
            int targetId,
            Vector2 targetPosition,
            SparseSet<ColliderComponent> colliderPool,
            out Vector2 center,
            out float radius)
        {
            if (colliderPool.TryGet(targetId, out ColliderComponent collider) == true)
            {
                center = targetPosition + collider.HitOffset;
                radius = collider.HitRadius;
                return;
            }

            center = targetPosition;
            radius = 0f;
        }

        private static bool IsJumpInvincible(int targetId, SparseSet<JumpComponent> jumpPool)
        {
            return jumpPool.TryGet(targetId, out JumpComponent jump) == true
                && jump.Height >= System_Jump.InvincibleHeight;
        }

        /// <summary>
        /// 대상 하나에 발사체 적중을 처리한다 (피해·OnHit 효과, 전파 발사체면 방문 기록과 다음 전파 요청).
        /// </summary>
        private static void ApplyHit(
            int targetId,
            int projectileEntityId,
            ref ProjectileComponent proj,
            ref SkillComponent skill)
        {
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
        }
    }
}
