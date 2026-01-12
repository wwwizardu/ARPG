using ARPG.Component;
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

        public readonly void OnFixedUpdate(float inFixedDeltaTime)
        {
            // AIPerceptionComponent 풀 가져오기
            SparseSet<AIPerceptionComponent> perceptionPool = AR.s.Component.GetComponentPool<AIPerceptionComponent>();
            if (perceptionPool == null || perceptionPool.Count == 0)
                return;

            // 플레이어 엔티티 ID 가져오기
            int playerEntityId = AR.s.Player.MyPlayers.EntityId;
            if (playerEntityId == -1)
                return;

            // 플레이어 Transform 가져오기
            if (!AR.s.Component.TryGetComponent<TransformComponent>(playerEntityId, out var playerTransform))
                return;

            // 모든 AI 엔티티 순회
            for (int i = 0; i < perceptionPool.Count; i++)
            {
                int entityId = perceptionPool.GetEntityId(i);
                AIPerceptionComponent perception = perceptionPool.GetByIndex(i);

                // AI 컴포넌트 가져오기
                if (!AR.s.Component.TryGetComponent<AIComponent>(entityId, out var ai))
                    continue;

                // AI 엔티티 Transform 가져오기
                if (!AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform))
                    continue;

                // 플레이어와의 거리 계산 (SqrMagnitude 사용 - 성능 최적화)
                Vector2 toPlayer = playerTransform.Position - transform.Position;
                float sqrDistance = toPlayer.sqrMagnitude;
                float detectionRangeSqr = perception.DetectionRange * perception.DetectionRange;

                bool canSeeTarget = false;

                // 거리 체크
                if (sqrDistance <= detectionRangeSqr)
                {
                    // 시야각 체크
                    if (perception.FieldOfView >= 360f)
                    {
                        // 전방향 감지
                        canSeeTarget = true;
                    }
                    else
                    {
                        // 정면 방향 계산 (2D에서는 rotation을 사용하여 정면 벡터 계산)
                        // Quaternion * Vector2.right로 정면 방향 구함
                        Vector2 forward = transform.Rotation * Vector2.right;
                        float distanceToPlayer = Mathf.Sqrt(sqrDistance);

                        if (distanceToPlayer > 0.001f)
                        {
                            Vector2 directionToPlayer = toPlayer / distanceToPlayer;
                            float angle = Vector2.Angle(forward, directionToPlayer);

                            if (angle <= perception.FieldOfView * 0.5f)
                            {
                                canSeeTarget = true;
                            }
                        }
                    }
                }

                // 타겟 감지 성공
                if (canSeeTarget)
                {
                    // AICanSeeTargetTag 추가
                    AR.s.Component.AddComponent(entityId, new AICanSeeTargetTag());

                    // AIComponent 업데이트
                    ai.TargetEntityId = playerEntityId;
                    ai.LastKnownTargetPos = playerTransform.Position;
                    AR.s.Component.SetComponent(entityId, ai);

                    // 마지막 감지 시간 업데이트
                    perception.LastDetectionTime = Time.time;
                    AR.s.Component.SetComponent(entityId, perception);
                }
                // 타겟 감지 실패
                else
                {
                    // 타겟을 잃은 범위 체크
                    float loseTargetRangeSqr = perception.LoseTargetRange * perception.LoseTargetRange;

                    if (sqrDistance > loseTargetRangeSqr || Time.time - perception.LastDetectionTime > 5f)
                    {
                        // AICanSeeTargetTag 제거
                        AR.s.Component.RemoveComponent<AICanSeeTargetTag>(entityId);

                        // 일정 시간(5초) 후 타겟 완전히 잃음
                        if (Time.time - perception.LastDetectionTime > 5f)
                        {
                            ai.TargetEntityId = -1;
                            AR.s.Component.SetComponent(entityId, ai);
                        }
                    }
                }
            }
        }
    }
}
