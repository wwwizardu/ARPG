using ARPG.Component;
using UnityEngine;

namespace ARPG.AI.StateHandlers
{
    /// <summary>
    /// Build 상태: NpcBuildAssignmentComponent 부착 시 진입.
    /// 건설지로 이동 → site 작업 영역(옆 타일 포함)에 도달하면 정지하여 작업.
    /// 위협 감지 시 Patrol과 동일하게 Flee/Chase 전이.
    /// 컴포넌트 제거(=Task 완료/취소) 시 기본 상태로 복귀.
    ///
    /// 진행 시간 누적은 System_VillageBuildQueue가 담당 — 이 핸들러는 NPC가 site 작업 영역에 있는지만
    /// 판정해서 Queue 시스템이 거리 검사로 IsActivelyWorking을 호출.
    /// 빌딩 자체는 충돌체로 막혀있어 NPC는 빌딩 외곽 인접 타일에 자연스럽게 정착함.
    /// </summary>
    public class BuildStateHandler : IAIStateHandler
    {
        // site 작업 영역 — 빌딩 중심에서 √2.5(≈1.58)타일 반경.
        //   직교 인접 타일 중심: 거리²=1.0 ✓
        //   대각선 인접 타일 중심: 거리²=2.0 ✓
        //   충돌로 빌딩 외곽에 정착한 NPC: 거리² ≈ 0.8~1.6 ✓
        // 모든 정상 접근 케이스 커버.
        private const float SITE_WORK_RANGE_SQR = 2.5f;

        private const float TRAVEL_SPEED_MULTIPLIER = 1f;
        private const int COURAGE_THRESHOLD = 70;

        public void OnEnter(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        public void OnUpdate(int entityId, float deltaTime)
        {
            ComponentManager cm = AR.s.Component;

            // 배정 해제됨 → 기본 상태(NPC=Patrol) 복귀
            if (cm.TryGetComponent<NpcBuildAssignmentComponent>(entityId, out var build) == false)
            {
                AIStateHelper.TransitionToState(entityId, AIStateHelper.GetDefaultState(entityId));
                return;
            }

            // 위협 감지 — Patrol과 동일 로직
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

            Vector2 sitePos = build.BuildSitePosition;
            float distToSiteSqr = (sitePos - transform.Position).sqrMagnitude;

            if (distToSiteSqr > SITE_WORK_RANGE_SQR)
            {
                // 작업 영역 밖 → site로 이동 (충돌이 빌딩 외곽에서 자동 정착시킴)
                AIStateHelper.MoveToward(entityId, sitePos, TRAVEL_SPEED_MULTIPLIER);
            }
            else
            {
                // 작업 영역 내 → 정지하여 작업 (System_VillageBuildQueue가 진행시간 누적)
                AIStateHelper.StopMovement(entityId);
            }
        }

        public void OnExit(int entityId)
        {
            AIStateHelper.StopMovement(entityId);
        }

        /// <summary>
        /// NPC가 현재 건설지 작업 영역에 있어 진행시간 누적이 가능한 상태인지 외부 판정.
        /// System_VillageBuildQueue가 매 틱 호출 — true 반환 시 dt만큼 AccumulatedHours += .
        /// </summary>
        public static bool IsActivelyWorking(int npcEntityId, Vector2 sitePos)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<AIStateComponent>(npcEntityId, out var aiState) == false) return false;
            if (aiState.CurrentState != AIState.Build) return false;
            if (cm.TryGetComponent<TransformComponent>(npcEntityId, out var transform) == false) return false;

            float sqr = (sitePos - transform.Position).sqrMagnitude;
            return sqr <= SITE_WORK_RANGE_SQR;
        }
    }
}
