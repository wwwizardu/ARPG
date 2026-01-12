using ARPG.Component;
using ARPG.AI;
using UnityEngine;

namespace ARPG.Systems
{
    public struct System_AI_Behavior : IFixedUpdateSystem
    {
        public int Priority => 50;  // Perception(30) 이후, Movement(100) 이전
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("System_AI_Behavior created");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager componentManager = AR.s.Component;
            SparseSet<AIBehaviorTypeComponent> behaviorPool = componentManager.GetComponentPool<AIBehaviorTypeComponent>();

            for (int i = 0; i < behaviorPool.Count; i++)
            {
                int entityId = behaviorPool.GetEntityId(i);

                // AI 상태 컴포넌트 확인
                if (!componentManager.TryGetComponent<AIStateComponent>(entityId, out var stateComponent))
                    continue;

                // 행동 타입 가져오기
                AIBehaviorTypeComponent behaviorType = behaviorPool.GetByIndex(i);

                // 해당 타입의 행동 실행
                IAIBehavior behavior = AIBehaviorFactory.GetBehavior(behaviorType.BehaviorType);
                behavior.OnUpdateState(entityId, stateComponent.CurrentState, inFixedDeltaTime);
            }
        }

        public void OnReset()
        {
            Debug.Log("System_AI_Behavior reset");
        }
    }
}
