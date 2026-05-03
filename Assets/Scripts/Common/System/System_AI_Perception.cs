using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// AI 감지/인지 시스템
    /// AI 엔티티가 타겟(플레이어)을 감지하고 추적하는 시스템
    ///
    /// 실행 흐름:
    /// 1. AIPerceptionComponent를 가진 모든 엔티티 순회
    /// 2. 플레이어와의 거리 및 시야각 체크
    /// 3. 감지 성공 시 AICanSeeTargetTag 추가, AIComponent 업데이트
    /// 4. 감지 실패 시 AICanSeeTargetTag 제거, 일정 시간 후 타겟 잃음
    /// </summary>
    public partial struct System_AI_Perception : IFixedUpdateSystem
    {
        public int Priority => 30;  // 입력(0) 이후, 버프(40) 이전 실행

        /// <summary>
        /// AI 감지는 0.2초마다 실행 (매 프레임 불필요, 성능 최적화)
        /// </summary>
        public readonly float UpdateInterval => 0.2f;

        public void OnCreate()
        {
            Debug.Log("[System_AI_Perception] Created with UpdateInterval: 0.2s");
        }

        public void OnReset()
        {
            Debug.Log("[System_AI_Perception] Reset called");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // AIPerceptionComponent 풀 가져오기
            SparseSet<AIPerceptionComponent> perceptionPool = AR.s.Component.GetComponentPool<AIPerceptionComponent>();
            if (perceptionPool == null || perceptionPool.Count == 0)
                return;

            ComponentManager cm = AR.s.Component;

            // 모든 AI 엔티티 순회
            for (int i = 0; i < perceptionPool.Count; i++)
            {
                int entityId = perceptionPool.GetEntityId(i);
                AIPerceptionComponent perception = perceptionPool.GetByIndex(i);

                // NPC는 타겟을 인식하지 않음 (관계 기반 행동은 추후 구현)
                if (cm.HasComponent<NpcTag>(entityId))
                    continue;

                // AI 컴포넌트
                if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false)
                    continue;

                // AI 엔티티 Transform
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                // AI 자신의 진영 (없으면 중립으로 간주 → 타겟 못 잡음)
                if (cm.TryGetComponent<FactionComponent>(entityId, out var selfFaction) == false)
                    continue;
                if (selfFaction.FactionId == Faction.Neutral)
                    continue;

                // 진영 기반으로 가장 가까운 적 탐색 (FactionHelper 위임)
                int targetEntityId = FactionHelper.FindNearestEnemy(
                    entityId,
                    transform.Position,
                    transform.Rotation,
                    selfFaction.FactionId,
                    perception.DetectionRange * perception.DetectionRange,
                    perception.FieldOfView,
                    requireStatComponent: false,
                    out Vector2 targetPosition,
                    out float targetSqrDistance);

                // 타겟 감지 성공
                if (targetEntityId != -1)
                {
                    cm.AddComponent(entityId, new AICanSeeTargetTag());

                    ai.TargetEntityId = targetEntityId;
                    ai.LastKnownTargetPos = targetPosition;
                    cm.SetComponent(entityId, ai);

                    perception.LastDetectionTime = Time.time;
                    cm.SetComponent(entityId, perception);
                }
                // 타겟 감지 실패
                else
                {
                    // 마지막 알려진 타겟이 LoseTargetRange를 벗어났거나 5초 경과 시 타겟 잃음
                    float loseTargetRangeSqr = perception.LoseTargetRange * perception.LoseTargetRange;

                    if (targetSqrDistance > loseTargetRangeSqr || Time.time - perception.LastDetectionTime > 5f)
                    {
                        cm.RemoveComponent<AICanSeeTargetTag>(entityId);

                        if (Time.time - perception.LastDetectionTime > 5f)
                        {
                            ai.TargetEntityId = -1;
                            cm.SetComponent(entityId, ai);
                        }
                    }
                }
            }
        }

    }
}
