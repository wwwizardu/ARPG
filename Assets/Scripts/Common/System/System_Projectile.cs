using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 발사체 이동 + 충돌 체크 + 수명 관리
    /// Priority 150: System_Move(100) 이후, System_Skill 이전
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
            SparseSet<ProjectileComponent> pool = cm.GetComponentPool<ProjectileComponent>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);
                ProjectileComponent proj = pool.GetByIndex(i);

                // 수명 체크
                proj.CurrentLifeTime += inFixedDeltaTime;
                if (proj.CurrentLifeTime >= proj.LifeTime)
                {
                    cm.AddComponent(entityId, new DestroyTag());
                    continue;
                }

                // 이동
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                if (cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == false)
                    continue;

                transform.Position += velocity.Direction * velocity.Speed * inFixedDeltaTime;
                cm.SetComponent(entityId, transform);

                // 충돌 체크
                bool hasHit = CheckCollision(cm, entityId, ref proj, transform.Position);

                if (hasHit && proj.IsPiercing == false)
                {
                    cm.AddComponent(entityId, new DestroyTag());
                    continue;
                }

                cm.SetComponent(entityId, proj);
            }
        }

        private bool CheckCollision(ComponentManager cm, int projectileEntityId, ref ProjectileComponent proj, Vector2 position)
        {
            bool hitAny = false;

            // SkillComponent에서 SkillTable 가져오기 (데미지 계산용)
            if (cm.TryGetComponent<SkillComponent>(proj.SkillEntityId, out var skill) == false)
                return false;

            // 발사자의 진영 (없으면 마이그레이션 안전망 — 모든 적 가능)
            bool ownerHasFaction = cm.TryGetComponent<FactionComponent>(proj.OwnerEntityId, out var ownerFaction);

            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int targetId = transformPool.GetEntityId(i);

                // 자기 자신 제외
                if (targetId == projectileEntityId)
                    continue;

                // 발사자 제외
                if (targetId == proj.OwnerEntityId)
                    continue;

                // 다른 발사체 제외
                if (cm.HasComponent<ProjectileTag>(targetId))
                    continue;

                // 진영 필터: 같은 진영, 중립, 진영 없는 엔티티는 제외
                if (ownerHasFaction)
                {
                    if (cm.TryGetComponent<FactionComponent>(targetId, out var targetFaction) == false)
                        continue;
                    if (targetFaction.FactionId == Faction.Neutral)
                        continue;
                    if (targetFaction.FactionId == ownerFaction.FactionId)
                        continue;
                }

                // StatComponent가 없는 엔티티(아이템, NPC 등)는 제외
                if (cm.HasComponent<StatComponent>(targetId) == false)
                    continue;

                // 공중 무적 상태면 투사체가 아래로 통과 (충돌 자체 스킵)
                if (cm.TryGetComponent<JumpComponent>(targetId, out var targetJump))
                {
                    if (targetJump.Height >= System_Jump.InvincibleHeight)
                        continue;
                }

                // 타겟 충돌 중심 = 발 좌표 + HitOffset, 타겟 반경 = HitRadius
                TransformComponent targetTransform = transformPool.GetByIndex(i);
                Vector2 targetCenter;
                float targetRadius;
                if (cm.TryGetComponent<ColliderComponent>(targetId, out var targetCollider))
                {
                    targetCenter = targetTransform.Position + targetCollider.HitOffset;
                    targetRadius = targetCollider.HitRadius;
                }
                else
                {
                    targetCenter = targetTransform.Position;
                    targetRadius = 0f;
                }

                if (HitboxMath.CircleVsCircle(position, proj.HitRadius, targetCenter, targetRadius))
                {
                    // 데미지 적용
                    if (skill.Table != null)
                    {
                        DamageResult result = DamageCalculator.Calculate(proj.OwnerEntityId, targetId, skill.Table);
                        DamageCalculator.ApplyDamageResult(proj.OwnerEntityId, targetId, result);

                        // [SkillEffect] OnProjectileHit 트리거 (+ OnHit/OnCrit/OnKill 동시 발화)
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
                        if (cm.TryGetComponent<StatComponent>(targetId, out var targetStat) && targetStat.CurrentHp <= 0f)
                            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnKill, ref hitCtx, skill.EffectiveSkillEffectIds);
                    }

                    hitAny = true;

                    if (proj.IsPiercing == false)
                        break;
                }
            }

            return hitAny;
        }
    }
}
