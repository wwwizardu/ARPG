using ARPG.Component;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Idle 상태: 대기 중, 타겟 감지 시 Chase로 전환
    /// 몬스터의 기본 상태
    /// </summary>
    public class IdleStateHandler : IAIStateHandler
    {
        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.HasComponent<AICanSeeTargetTag>(entityId))
            {
                AIStateHelper.TransitionToState(entityId, AIState.Chase);
            }
        }

        public void OnExit(int entityId)
        {
        }
    }
}
