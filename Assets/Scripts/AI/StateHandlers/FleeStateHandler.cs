using ARPG.Component;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Flee 상태: 위협 반대 방향으로 도주, 위협 해소 시 Patrol 복귀
    /// NPC 전용 (Courage가 낮은 NPC)
    /// </summary>
    public class FleeStateHandler : IAIStateHandler
    {
        public void OnEnter(int entityId)
        {
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            // 위협 해소 시 Patrol 복귀
            if (cm.HasComponent<AICanSeeTargetTag>(entityId) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false) return;

            // 타겟이 없으면 Patrol 복귀
            if (ai.TargetEntityId == -1)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            // 타겟 위치로부터 도주
            if (cm.TryGetComponent<TransformComponent>(ai.TargetEntityId, out var threatTransform))
            {
                AIStateHelper.MoveAwayFrom(entityId, threatTransform.Position);
            }
        }

        public void OnExit(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }
    }
}
