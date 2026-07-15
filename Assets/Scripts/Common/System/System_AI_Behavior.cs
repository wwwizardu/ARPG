using ARPG.Component;
using ARPG.AI;
using UnityEngine;

namespace ARPG.Systems
{
    public class System_AI_Behavior : IFixedUpdateSystem
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
            SparseSet<AIStateComponent> statePool = componentManager.GetComponentPool<AIStateComponent>();

            for (int i = 0; i < behaviorPool.Count; i++)
            {
                int entityId = behaviorPool.GetEntityId(i);

                // 오프스크린 이동 중 승격된 NPC는 해당 위치에서 멈춰 조우를 기다린다.
                // 청크 비활성화 시 엔티티와 태그가 함께 제거되어 오프스크린 경로가 다시 진행된다.
                if (componentManager.HasComponent<NpcActivityPausedTag>(entityId))
                    continue;

                // 비활성 청크의 엔티티는 AI를 돌리지 않는다.
                // (오프스크린 몬스터 수명 잔존·활성 영역 밖으로 이동한 NPC 등 → 비활성 청크 길찾기 경고 원천 차단)
                if (componentManager.TryGetComponent<TransformComponent>(entityId, out var aiTransform) == true &&
                    AR.s.Map.IsChunkActiveAtWorld(aiTransform.Position) == false)
                    continue;

                // AI 상태 컴포넌트 확인
                if (statePool.TryGet(entityId, out var stateComponent) == false)
                    continue;

                // 행동 타입 가져오기
                AIBehaviorTypeComponent behaviorType = behaviorPool.GetByIndex(i);

                // 현재 상태에 맞는 핸들러 가져오기
                IAIStateHandler handler = AIBehaviorFactory.GetStateHandler(
                    behaviorType.BehaviorType, stateComponent.CurrentState);

                if (handler != null)
                {
                    handler.OnUpdate(entityId, inFixedDeltaTime);
                }
            }
        }

        public void OnReset()
        {
            Debug.Log("System_AI_Behavior reset");
        }
    }
}
