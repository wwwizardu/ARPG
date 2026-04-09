namespace ARPG.AI
{
    /// <summary>
    /// AI 상태별 핸들러 인터페이스
    /// 각 상태(Idle, Chase, Attack 등)를 독립 클래스로 구현
    /// AIBehaviorFactory에서 (BehaviorType, AIState) 조합으로 적절한 핸들러를 선택
    /// </summary>
    public interface IAIStateHandler
    {
        void OnEnter(int entityId);
        void OnUpdate(int entityId, float deltaTime);
        void OnExit(int entityId);
    }
}
