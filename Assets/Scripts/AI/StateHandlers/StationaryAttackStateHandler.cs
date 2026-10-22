using ARPG.Component;
using ARPG.Manager;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// 정지형(Stationary) Attack 상태: 토템·터렛처럼 한자리에 머물며 사거리 내 적 자동 시전.
    /// 이동 없이 대상을 계속 조준한다. 슬롯 0 스킬을 사용한다.
    /// 적이 없으면 같은 상태에 머물며 다음 틱에 재탐색.
    /// </summary>
    public class StationaryAttackStateHandler : IAIStateHandler
    {
        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.HasComponent<DestroyTag>(entityId) == true)
                return;

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<FactionComponent>(entityId, out var faction) == false) return;

            // 슬롯 0 스킬
            int skillEntityId = EntityManager.GetDeterministicId(entityId, EntityIdCategory.Skill, 0);
            if (cm.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false) return;
            if (skill.Table == null) return;

            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false)
                ai = new AIComponent { TargetEntityId = -1 };

            // 시전 중에도 대상의 이동·사망·사거리 이탈을 반영한다. 유효한 현재 대상은 계속 유지한다.
            int targetId = ai.TargetEntityId;
            Vector2 targetPosition;
            if (TryGetTarget(entityId, targetId, transform.Position, skill.Table.SkillRangeMax, out targetPosition) == false)
            {
                float effectiveRange = SkillHelper.GetEntityHitRadius(entityId) + skill.Table.SkillRangeMax;
                targetId = FactionHelper.FindNearestEnemy(entityId, transform.Position, transform.Rotation,
                    faction.FactionId, effectiveRange * effectiveRange, 360f, true, out targetPosition, out _,
                    requireLivingTarget: true);
                if (TryGetTarget(entityId, targetId, transform.Position, skill.Table.SkillRangeMax, out targetPosition) == false)
                    targetId = -1;
            }

            ai.TargetEntityId = targetId;
            if (targetId >= 0)
                ai.LastKnownTargetPos = targetPosition;
            cm.SetComponent(entityId, ai);

            if (targetId == -1)
                return;

            if (cm.HasComponent<SkillCommandComponent>(entityId) == true)
                return;
            if (cm.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState) == true
                && (skillState.IsRunning == true || skillState.IsCooldownReady == false))
                return;

            // SkillCommandComponent 부착 → System_Skill이 다음 프레임에 처리
            if (SkillHelper.GetSkillCommandComponent(0, entityId, targetPosition, out var command))
            {
                cm.SetComponent(entityId, command);
            }
        }

        public void OnExit(int entityId)
        {
        }

        private static bool TryGetTarget(int ownerId, int targetId, Vector2 origin, float range, out Vector2 position)
        {
            ComponentManager cm = AR.s.Component;
            position = default;
            if (targetId < 0 || cm.HasComponent<DestroyTag>(targetId) == true
                || cm.TryGetComponent<StatComponent>(targetId, out var stat) == false || stat.CurrentHp <= 0
                || cm.TryGetComponent<TransformComponent>(targetId, out var transform) == false
                || FactionHelper.IsHostileTo(ownerId, targetId) == false)
                return false;

            float effectiveRange = SkillHelper.GetEntityHitRadius(ownerId) + range + SkillHelper.GetEntityHitRadius(targetId);
            if ((transform.Position - origin).sqrMagnitude > effectiveRange * effectiveRange)
                return false;

            position = transform.Position;
            return true;
        }
    }
}
