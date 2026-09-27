using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Follow 상태: 동행 NPC가 리더(플레이어)와 일정 거리를 유지하며 따라간다 (NPC_COMBAT_DESIGN §4.2).
    /// 리더 근처에서 적을 인식하면 기존 Chase/Attack으로 전환하고, 타겟을 잃으면 GetDefaultState로 돌아온다.
    /// 너무 멀어지거나 길찾기가 계속 실패하면 리더 옆 통행 가능한 타일로 순간이동한다 (§4.3, B2는 던전 안만).
    /// </summary>
    public class FollowStateHandler : IAIStateHandler
    {
        // 추종 거리 (Q6: 2~4타일). 멈춤/재출발 거리를 분리해 제자리 떨림을 막는다.
        private const float FOLLOW_STOP_DISTANCE_SQR = 2f * 2f;
        private const float FOLLOW_START_DISTANCE_SQR = 4f * 4f;
        // 리더와 이 거리 안에 있을 때만 교전을 시작한다. 해제 거리는 System_AI_Perception의 LeashDropDistance.
        private const float ENGAGE_LEADER_DISTANCE_SQR = 6f * 6f;
        // 순간이동 조건 (Q6: 약 15타일, 길찾기 연속 실패 시간)
        private const float TELEPORT_DISTANCE_SQR = 15f * 15f;
        private const float PATH_FAIL_TELEPORT_SECONDS = 3f;
        private const float DEFAULT_MOVE_RADIUS = 0.3f;

        /// <summary>동행 NPC가 리더에게서 이 거리보다 멀어지면 인식 단계에서 타겟을 해제한다 (추종 반경 밖 적은 쫓지 않음).</summary>
        public const float LeashDropDistance = 10f;

        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.TryGetComponent<NpcCompanionComponent>(entityId, out var companion) == false)
            {
                // 동행 종료 후 남은 Follow 상태 → 기본 상태로 복귀
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            if (cm.TryGetComponent<TransformComponent>(companion.LeaderEntityId, out var leaderTransform) == false)
            {
                AIStateHelper.StopMovement(entityId);
                return;
            }

            float leaderSqrDistance = (leaderTransform.Position - transform.Position).sqrMagnitude;

            // 리더 근처에서 적을 인식하면 교전
            if (cm.HasComponent<AICanSeeTargetTag>(entityId) == true && leaderSqrDistance <= ENGAGE_LEADER_DISTANCE_SQR)
            {
                AIStateHelper.TransitionToState(entityId, AIState.Chase);
                return;
            }

            if (ShouldTeleport(entityId, ref companion, leaderSqrDistance) == true)
            {
                TeleportNearLeader(entityId, leaderTransform.Position);
                return;
            }

            bool isMoving = cm.TryGetComponent<VelocityComponent>(entityId, out var velocity) == true && velocity.Speed > 0f;
            if (leaderSqrDistance > FOLLOW_START_DISTANCE_SQR ||
                (isMoving == true && leaderSqrDistance > FOLLOW_STOP_DISTANCE_SQR))
            {
                AIStateHelper.MoveToward(entityId, leaderTransform.Position);
            }
            else if (isMoving == true)
            {
                AIStateHelper.StopMovement(entityId);
            }
        }

        public void OnExit(int entityId)
        {
        }

        private static bool ShouldTeleport(int entityId, ref NpcCompanionComponent companion, float leaderSqrDistance)
        {
            // B2: 던전 안에서만 순간이동한다. 필드 순간이동은 C1(필드 동행)에서 적용.
            if (AR.s.Dungeon.IsInside == false)
                return false;
            if (leaderSqrDistance >= TELEPORT_DISTANCE_SQR)
                return true;

            ComponentManager cm = AR.s.Component;
            bool pathFailed = cm.TryGetComponent<PathfindingComponent>(entityId, out var pf) == true &&
                              pf.Status == PathfindingStatus.Failed;
            if (pathFailed == false)
            {
                if (companion.PathFailedSince > 0f)
                {
                    companion.PathFailedSince = 0f;
                    cm.SetComponent(entityId, companion);
                }
                return false;
            }

            if (companion.PathFailedSince <= 0f)
            {
                companion.PathFailedSince = Time.time;
                cm.SetComponent(entityId, companion);
                return false;
            }

            return Time.time - companion.PathFailedSince >= PATH_FAIL_TELEPORT_SECONDS;
        }

        private static void TeleportNearLeader(int entityId, Vector2 leaderPosition)
        {
            ComponentManager cm = AR.s.Component;
            float moveRadius = cm.TryGetComponent<ColliderComponent>(entityId, out var collider) == true && collider.MoveRadius > 0f
                ? collider.MoveRadius
                : DEFAULT_MOVE_RADIUS;
            if (CollisionUtil.TryFindNearestFree(leaderPosition, moveRadius, out Vector2 destination, requireActiveChunk: true) == false)
                return;

            AIStateHelper.StopMovement(entityId);
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
            {
                transform.Position = destination;
                cm.SetComponent(entityId, transform);
            }
            if (cm.TryGetComponent<NpcCompanionComponent>(entityId, out var companion) == true)
            {
                companion.PathFailedSince = 0f;
                cm.SetComponent(entityId, companion);
            }
        }
    }
}
