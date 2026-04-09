using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Patrol 상태: 스폰 위치 근처 배회, 위협 감지 시 Flee 또는 Chase
    /// NPC 기본 상태
    /// </summary>
    public class PatrolStateHandler : IAIStateHandler
    {
        private const float PATROL_RADIUS = 5f;
        private const float ARRIVAL_DISTANCE_SQR = 1f;
        private const float PATROL_WAIT_TIME = 3f;
        private const float PATROL_SPEED_MULTIPLIER = 0.5f;
        private const int COURAGE_THRESHOLD = 70;

        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
            SetNewPatrolTarget(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            // 위협 감지 시 대응
            if (cm.HasComponent<AICanSeeTargetTag>(entityId))
            {
                if (cm.TryGetComponent<NpcStatComponent>(entityId, out var npcStat)
                    && npcStat.Courage < COURAGE_THRESHOLD)
                {
                    AIStateHelper.TransitionToState(entityId, AIState.Flee);
                }
                else
                {
                    AIStateHelper.TransitionToState(entityId, AIState.Chase);
                }
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var aiState) == false) return;

            Vector2 diff = aiState.PatrolTarget - transform.Position;
            float distSqr = diff.sqrMagnitude;

            // 목적지 도착
            if (distSqr <= ARRIVAL_DISTANCE_SQR)
            {
                AIStateHelper.StopMovement(entityId);

                // 도착 시점 기록 (최초 도착 시에만)
                if (aiState.PatrolArrivalTime <= 0f)
                {
                    aiState.PatrolArrivalTime = Time.time;
                    cm.SetComponent(entityId, aiState);
                }

                // 대기 시간 후 새 목적지
                float elapsed = Time.time - aiState.PatrolArrivalTime;
                if (elapsed >= PATROL_WAIT_TIME)
                {
                    SetNewPatrolTarget(entityId);
                }
            }
            else
            {
                // 목적지로 이동 (느린 속도)
                AIStateHelper.MoveToward(entityId, aiState.PatrolTarget, PATROL_SPEED_MULTIPLIER);
            }
        }

        public void OnExit(int entityId)
        {
        }

        private void SetNewPatrolTarget(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out var aiState) == false) return;

            Vector2 offset = Random.insideUnitCircle * PATROL_RADIUS;
            aiState.PatrolTarget = aiState.SpawnPosition + offset;
            aiState.PatrolArrivalTime = 0f;
            cm.SetComponent(entityId, aiState);
        }
    }
}
