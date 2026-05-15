using System.Collections.Generic;
using ARPG.Component;
using ARPG.Skill.Combat;
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
                ProjectileComponent proj = projectilePool.GetByIndex(i);

                proj.CurrentLifeTime += inFixedDeltaTime;
                if (proj.CurrentLifeTime >= proj.LifeTime)
                {
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

                if (hasHit && proj.IsPiercing == false)
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

            if (factionPool.TryGet(proj.OwnerEntityId, out var ownerFaction))
            {
                FactionHelper.GetEnemyFactionLists(ownerFaction.FactionId, out var primaryList, out var secondaryList);
                bool hitAny = false;
                bool stopEarly = ScanCandidateList(
                    primaryList, projectileEntityId, ref proj, ref skill, position,
                    transformPool, statPool, jumpPool, colliderPool, projectileTagPool, ref hitAny);
                if (stopEarly == false)
                {
                    ScanCandidateList(
                        secondaryList, projectileEntityId, ref proj, ref skill, position,
                        transformPool, statPool, jumpPool, colliderPool, projectileTagPool, ref hitAny);
                }
                return hitAny;
            }

            // Owner has no faction — fall back to transform-pool scan to preserve migration-safe behavior.
            bool fallbackHitAny = false;
            for (int i = 0; i < transformPool.Count; i++)
            {
                int targetId = transformPool.GetEntityId(i);
                if (TryHitCandidate(
                        targetId, projectileEntityId, ref proj, ref skill, position,
                        transformPool, statPool, jumpPool, colliderPool, projectileTagPool))
                {
                    fallbackHitAny = true;
                    if (proj.IsPiercing == false)
                        break;
                }
            }
            return fallbackHitAny;
        }

        // Returns true when the loop should stop early because a non-piercing projectile already hit.
        private bool ScanCandidateList(
            List<int> candidates,
            int projectileEntityId,
            ref ProjectileComponent proj,
            ref SkillComponent skill,
            Vector2 position,
            SparseSet<TransformComponent> transformPool,
            SparseSet<StatComponent> statPool,
            SparseSet<JumpComponent> jumpPool,
            SparseSet<ColliderComponent> colliderPool,
            SparseSet<ProjectileTag> projectileTagPool,
            ref bool hitAny)
        {
            if (candidates == null)
                return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                int targetId = candidates[i];
                if (TryHitCandidate(
                        targetId, projectileEntityId, ref proj, ref skill, position,
                        transformPool, statPool, jumpPool, colliderPool, projectileTagPool))
                {
                    hitAny = true;
                    if (proj.IsPiercing == false)
                        return true;
                }
            }
            return false;
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

            if (skill.Table != null)
            {
                DamageResult result = DamageCalculator.Calculate(proj.SkillEntityId, proj.OwnerEntityId, targetId, skill.Table);
                DamageCalculator.ApplyDamageResult(proj.SkillEntityId, proj.OwnerEntityId, targetId, result);

                SkillEffectContext hitCtx = new()
                {
                    SkillEntityId = proj.SkillEntityId,
                    SkillId = skill.SkillId,
                    OwnerEntityId = proj.OwnerEntityId,
                    TargetEntityId = targetId,
                    ProjectileEntityId = projectileEntityId,
                    DamageResult = result,
                };
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnProjectileHit, ref hitCtx, skill.EffectiveSkillEffectIds);
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnHit, ref hitCtx, skill.EffectiveSkillEffectIds);
                if (result.IsCritical)
                    SkillEffectExecutor.Trigger(GE.SkillTrigger.OnCrit, ref hitCtx, skill.EffectiveSkillEffectIds);
                if (statPool.TryGet(targetId, out var targetStat) && targetStat.CurrentHp <= 0f)
                    SkillEffectExecutor.Trigger(GE.SkillTrigger.OnKill, ref hitCtx, skill.EffectiveSkillEffectIds);
            }

            return true;
        }
    }
}
