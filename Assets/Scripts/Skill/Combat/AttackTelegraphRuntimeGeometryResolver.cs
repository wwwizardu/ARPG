#nullable enable
using ARPG.Component;
using ARPG.Skill.Telegraph;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 살아있는 ECS/테이블 값을 읽어 예고 도형 입력을 조립하는 단일 정본.
    /// 순수 계산은 <see cref="AttackTelegraphGeometryResolver"/>가 담당하고,
    /// 이 클래스는 Collider/Stat/Table 조회만 맡는다.
    /// System_Skill(예고 생성)과 System_AttackTelegraph(매 프레임 갱신)이 같은 결과를 봐야 하므로
    /// 두 시스템에 조회 로직을 복제하지 않고 이 클래스만 사용한다.
    /// </summary>
    public static class AttackTelegraphRuntimeGeometryResolver
    {
        /// <summary>
        /// 위험 영역은 플레이어 중심 기준이라 모든 도형이 플레이어 Collider 반경을 더한다.
        /// 프레임당 한 번만 조회해서 <see cref="Resolve"/>에 넘긴다.
        /// </summary>
        public static float ResolvePlayerHitRadius(ComponentManager inComponentManager)
        {
            int playerEntityId = AR.s.Data != null ? AR.s.Data.CurrentPlayerEntityId : 0;
            if (playerEntityId <= 0
                || inComponentManager.TryGetComponent<ColliderComponent>(playerEntityId, out ColliderComponent playerCollider) == false)
            {
                return 0f;
            }

            return Mathf.Max(0f, playerCollider.HitRadius);
        }

        /// <summary>
        /// 실제 Collider·테이블·발사체 수 보너스를 반영한 예고 도형을 만든다.
        /// Shape가 None이면 이 스킬은 예고 대상이 아니다.
        /// </summary>
        public static AttackTelegraphGeometry Resolve(
            ComponentManager inComponentManager,
            int inSkillEntityId,
            SkillComponent inSkill,
            float inPlayerHitRadius)
        {
            SkillTable? table = inSkill.Table;
            if (table == null || table.ProjectileId > 0)
                return default;

            float ownerHitRadius = 0f;
            if (inComponentManager.TryGetComponent<ColliderComponent>(inSkill.OwnerEntityId, out ColliderComponent ownerCollider) == true)
                ownerHitRadius = ownerCollider.HitRadius;

            AreaEffectTable? areaTable = table.AreaEffectId > 0 && AR.s.Data != null
                ? AR.s.Data.GetAreaEffect(table.AreaEffectId)
                : null;
            ProjectileTable? projectileTable = table.ProjectileId > 0 && AR.s.Data != null
                ? AR.s.Data.GetProjectile(table.ProjectileId)
                : null;

            int ownerAdditionalProjectiles = 0;
            if (inComponentManager.TryGetComponent<StatComponent>(inSkill.OwnerEntityId, out StatComponent ownerStat) == true)
                ownerAdditionalProjectiles = ownerStat.FinalProjectileCountAdd;

            int skillAdditionalProjectiles = 0;
            if (inComponentManager.TryGetComponent<SkillStatBonusOnSkillStartComponent>(inSkillEntityId, out SkillStatBonusOnSkillStartComponent skillBonus) == true)
                skillAdditionalProjectiles = skillBonus.ProjectileCountAddBonus;

            AttackTelegraphGeometryInput input = new(
                table.SkillTargetType == GE.SkillTargetType.Direction,
                table.SkillTargetType == GE.SkillTargetType.Position,
                table.HasDirectImpact,
                areaTable != null,
                projectileTable != null,
                table.UsesLandingCommit,
                table.SkillTargetRange1,
                table.SkillTargetRange2,
                ownerHitRadius,
                inPlayerHitRadius,
                areaTable != null ? areaTable.Radius : 0f,
                projectileTable != null ? projectileTable.Speed : 0f,
                projectileTable != null ? projectileTable.LifeTime : 0f,
                projectileTable != null ? projectileTable.HitRadius : 0f,
                AttackTelegraphGeometryResolver.ResolveProjectileCount(
                    table.BaseProjectileCount,
                    ownerAdditionalProjectiles,
                    skillAdditionalProjectiles),
                AttackTelegraphGeometryResolver.DefaultProjectileSpreadAngleDeg,
                table.UsesDash,
                table.DashDistance);
            return AttackTelegraphGeometryResolver.Resolve(input);
        }

        /// <summary>
        /// 예고 중심 좌표. FollowOwner는 발 좌표를, 발사체 계열은 몸통 중심(HitOffset)을 기준으로 한다.
        /// 실제 판정 기준점과 같은 규칙을 써야 하므로 생성/갱신 양쪽이 이 함수를 공유한다.
        /// </summary>
        public static Vector2 ResolveOrigin(
            ComponentManager inComponentManager,
            int inOwnerEntityId,
            AttackTelegraphShape inShape,
            Vector2 inOwnerPosition)
        {
            if (inShape != AttackTelegraphShape.ProjectileLane
                && inShape != AttackTelegraphShape.ProjectileFan)
            {
                return inOwnerPosition;
            }

            if (inComponentManager.TryGetComponent<ColliderComponent>(inOwnerEntityId, out ColliderComponent ownerCollider) == true)
                return inOwnerPosition + ownerCollider.HitOffset;

            return inOwnerPosition;
        }
    }
}
