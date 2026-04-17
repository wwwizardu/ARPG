using ARPG.Component;
using ARPG.Skill.Combat;
using UnityEngine;

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
            float hitRadiusSqr = proj.HitRadius * proj.HitRadius;

            // SkillComponent에서 SkillTable 가져오기 (데미지 계산용)
            if (cm.TryGetComponent<SkillComponent>(proj.SkillEntityId, out var skill) == false)
                return false;

            // owner가 몬스터인지 플레이어인지 확인
            bool ownerIsMonster = cm.HasComponent<MonsterTag>(proj.OwnerEntityId);

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

                // 같은 편 제외: 몬스터가 쏜 건 몬스터에게 안 맞음, 플레이어가 쏜 건 플레이어에게 안 맞음
                bool targetIsMonster = cm.HasComponent<MonsterTag>(targetId);
                if (ownerIsMonster == targetIsMonster)
                    continue;

                // StatComponent가 없는 엔티티(아이템, NPC 등)는 제외
                if (cm.HasComponent<StatComponent>(targetId) == false)
                    continue;

                // 공중 무적 상태면 투사체가 아래로 통과 (충돌 자체 스킵)
                if (cm.TryGetComponent<JumpComponent>(targetId, out var targetJump))
                {
                    if (targetJump.Height >= System_Jump.InvincibleHeight)
                        continue;
                }

                TransformComponent targetTransform = transformPool.GetByIndex(i);
                float sqrDistance = (targetTransform.Position - position).sqrMagnitude;

                if (sqrDistance <= hitRadiusSqr)
                {
                    // 데미지 적용
                    if (skill.Table != null)
                    {
                        DamageResult result = DamageCalculator.Calculate(proj.OwnerEntityId, targetId, skill.Table);
                        DamageCalculator.ApplyDamageResult(proj.OwnerEntityId, targetId, result);
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
