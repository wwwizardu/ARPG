using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Return 상태: 목표를 잃은 순찰 몬스터가 순찰 기준점(AIStateComponent.SpawnPosition)으로 돌아간다 (NAMED_THREAT_DESIGN §5.5).
    /// 돌아가는 길에 다시 적을 보면 Chase, 기준점에 닿거나 너무 오래 걸리면(길이 막힘) Patrol로 넘어간다.
    /// 영역 밖으로 끌고 가도 결국 영역으로 돌아간다. 순찰 반경이 없는 몬스터는 이 상태를 쓰지 않는다 (기본 상태 Idle).
    /// </summary>
    public class ReturnStateHandler : IAIStateHandler
    {
        private const float ARRIVAL_DISTANCE_SQR = 2.25f;
        private const float GIVE_UP_SECONDS = 20f;

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
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var aiState) == false) return;

            Vector2 diff = aiState.SpawnPosition - transform.Position;
            if (diff.sqrMagnitude <= ARRIVAL_DISTANCE_SQR || Time.time - aiState.StateEnterTime >= GIVE_UP_SECONDS)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Patrol);
                return;
            }

            AIStateHelper.MoveToward(entityId, aiState.SpawnPosition);
        }

        public void OnExit(int entityId)
        {
        }
    }
}
