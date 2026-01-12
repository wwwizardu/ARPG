namespace ARPG.Component
{
    public interface IAIBehavior
    {
        void OnEnterState(int entityId, AIState state);
        void OnUpdateState(int entityId, AIState state, float deltaTime);
        void OnExitState(int entityId, AIState state);
    }
}
