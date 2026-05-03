using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// 정지형(Stationary) Attack 상태: 토템·터렛처럼 한자리에 머물며 사거리 내 적 자동 시전.
    /// 추적/이동 없음. 슬롯 0 스킬을 사용한다.
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

            // 이미 시전 명령이 부착되어 있으면 다음 프레임 대기
            if (cm.HasComponent<SkillCommandComponent>(entityId))
                return;

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<FactionComponent>(entityId, out var faction) == false) return;

            // 슬롯 0 스킬
            int skillEntityId = EntityIdHelper.GetDeterministicId(entityId, EntityIdCategory.Skill, 0);
            if (cm.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false) return;
            if (skill.Table == null) return;

            // 쿨다운 / 실행 중이면 대기
            if (cm.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState))
            {
                if (skillState.IsRunning || skillState.IsCooldownReady == false)
                    return;
            }

            // 사거리 내 적 탐색 (FOV 360 = 전방위)
            float rangeSqr = skill.Table.SkillRangeMax * skill.Table.SkillRangeMax;
            int targetId = FactionHelper.FindNearestEnemy(
                entityId,
                transform.Position,
                transform.Rotation,
                faction.FactionId,
                rangeSqr,
                fieldOfView: 360f,
                requireStatComponent: true,
                out Vector2 targetPosition,
                out _);

            if (targetId == -1)
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
    }
}
