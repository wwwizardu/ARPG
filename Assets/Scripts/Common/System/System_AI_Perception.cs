using ARPG.AI;
using ARPG.AI.StateHandlers;
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

                // 전투 허용 상태가 아닌 NPC는 타겟을 인식하지 않음 (NPC_COMBAT_DESIGN §3.4)
                // 화면 안 사냥 중(NpcActivityPausedTag)인 NPC는 태그가 있어도 제외 — 결과는 계산 판정 소관
                if (cm.HasComponent<NpcTag>(entityId) == true &&
                    (cm.HasComponent<NpcCombatEnabledTag>(entityId) == false ||
                     cm.HasComponent<NpcActivityPausedTag>(entityId) == true))
                    continue;

                // AI 컴포넌트
                if (cm.TryGetComponent<AIComponent>(entityId, out var ai) == false)
                    continue;

                // AI 엔티티 Transform
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                // 결투 중인 NPC는 인식 대상을 결투 상대로 고정한다 (NPC_COMBAT_DESIGN §3.4, Q10: 상대만 노린다).
                // 결투 영역이 없으므로 [D20] 거리·시야와 무관하게 상대를 계속 본다.
                if (cm.TryGetComponent<DuelComponent>(entityId, out var npcDuel) == true &&
                    cm.HasComponent<NpcTag>(entityId) == true)
                {
                    if (cm.TryGetComponent<TransformComponent>(npcDuel.OpponentId, out var opponentTransform) == true &&
                        cm.HasComponent<DestroyTag>(npcDuel.OpponentId) == false)
                    {
                        MarkTargetSeen(cm, entityId, ref ai, ref perception, npcDuel.OpponentId, opponentTransform.Position);
                    }
                    else
                    {
                        AIStateHelper.ClearTarget(entityId, ref ai);
                    }
                    continue;
                }

                // 동행 NPC가 리더에게서 추종 반경 밖으로 멀어지면 타겟을 놓고 Follow로 복귀 (§4.2)
                if (cm.TryGetComponent<NpcCompanionComponent>(entityId, out var companion) == true &&
                    cm.TryGetComponent<TransformComponent>(companion.LeaderEntityId, out var leaderTransform) == true &&
                    (leaderTransform.Position - transform.Position).sqrMagnitude >
                    FollowStateHandler.LeashDropDistance * FollowStateHandler.LeashDropDistance)
                {
                    AIStateHelper.ClearTarget(entityId, ref ai);
                    continue;
                }

                // AI 자신의 진영 (없으면 중립으로 간주 → 타겟 못 잡음)
                if (cm.TryGetComponent<FactionComponent>(entityId, out var selfFaction) == false)
                    continue;
                if (selfFaction.FactionId == Faction.Neutral)
                    continue;

                // 결투 참가자가 근처에 있는 몬스터는 참가자(플레이어·NPC) 중 무작위로 타겟을 고른다 (§3.3 D21).
                // 결투와 무관한 몬스터(근처에 참가자 없음)는 아래 기존 규칙(가장 가까운 적)을 그대로 쓴다.
                if (selfFaction.FactionId == Faction.Hostile &&
                    TrySelectDuelParticipantTarget(cm, transform.Position, perception, ai.TargetEntityId,
                        out int duelTargetId, out Vector2 duelTargetPosition) == true)
                {
                    MarkTargetSeen(cm, entityId, ref ai, ref perception, duelTargetId, duelTargetPosition);
                    continue;
                }

                // 진영 기반으로 가장 가까운 적 탐색 (FactionHelper 위임)
                int targetEntityId = FactionHelper.FindNearestEnemy(
                    entityId,
                    transform.Position,
                    transform.Rotation,
                    selfFaction.FactionId,
                    perception.DetectionRange * perception.DetectionRange,
                    perception.FieldOfView,
                    requireStatComponent: true,
                    out Vector2 targetPosition,
                    out float targetSqrDistance);

                // 타겟 감지 성공
                if (targetEntityId != -1)
                {
                    MarkTargetSeen(cm, entityId, ref ai, ref perception, targetEntityId, targetPosition);
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

        private static void MarkTargetSeen(ComponentManager cm, int entityId, ref AIComponent ai,
            ref AIPerceptionComponent perception, int targetEntityId, Vector2 targetPosition)
        {
            cm.AddComponent(entityId, new AICanSeeTargetTag());

            ai.TargetEntityId = targetEntityId;
            ai.LastKnownTargetPos = targetPosition;
            cm.SetComponent(entityId, ai);

            perception.LastDetectionTime = Time.time;
            cm.SetComponent(entityId, perception);
        }

        /// <summary>
        /// 몬스터의 결투 참가자 타겟 선택 (§3.3 D21). 어그로 시스템이 없으므로 감지 범위 안의 참가자 중 무작위로 고른다.
        /// 현재 타겟이 놓침 범위 안의 참가자면 유지한다 — 인식 틱(0.2초)마다 다시 뽑아 타겟이 흔들리지 않게 하기 위함.
        /// </summary>
        private static bool TrySelectDuelParticipantTarget(ComponentManager cm, Vector2 selfPosition,
            AIPerceptionComponent perception, int currentTargetId, out int targetEntityId, out Vector2 targetPosition)
        {
            targetEntityId = -1;
            targetPosition = Vector2.zero;

            SparseSet<DuelComponent> duelPool = cm.GetComponentPool<DuelComponent>();
            if (duelPool.Count == 0)
                return false;

            float loseRangeSqr = perception.LoseTargetRange * perception.LoseTargetRange;
            if (currentTargetId >= 0 &&
                duelPool.Contains(currentTargetId) == true &&
                cm.HasComponent<DestroyTag>(currentTargetId) == false &&
                cm.TryGetComponent<TransformComponent>(currentTargetId, out var currentTransform) == true &&
                (currentTransform.Position - selfPosition).sqrMagnitude <= loseRangeSqr)
            {
                targetEntityId = currentTargetId;
                targetPosition = currentTransform.Position;
                return true;
            }

            float detectionRangeSqr = perception.DetectionRange * perception.DetectionRange;
            int candidateCount = 0;
            for (int i = 0; i < duelPool.Count; i++)
            {
                if (IsDuelCandidateInRange(cm, duelPool.GetEntityId(i), selfPosition, detectionRangeSqr, out _) == true)
                    candidateCount++;
            }
            if (candidateCount == 0)
                return false;

            int pick = Random.Range(0, candidateCount);
            for (int i = 0; i < duelPool.Count; i++)
            {
                int candidateId = duelPool.GetEntityId(i);
                if (IsDuelCandidateInRange(cm, candidateId, selfPosition, detectionRangeSqr, out Vector2 candidatePosition) == false)
                    continue;
                if (pick > 0)
                {
                    pick--;
                    continue;
                }

                targetEntityId = candidateId;
                targetPosition = candidatePosition;
                return true;
            }

            return false;
        }

        private static bool IsDuelCandidateInRange(ComponentManager cm, int candidateId, Vector2 selfPosition,
            float rangeSqr, out Vector2 candidatePosition)
        {
            candidatePosition = Vector2.zero;
            if (cm.HasComponent<DestroyTag>(candidateId) == true ||
                cm.HasComponent<StatComponent>(candidateId) == false ||
                cm.TryGetComponent<TransformComponent>(candidateId, out var candidateTransform) == false)
                return false;
            if ((candidateTransform.Position - selfPosition).sqrMagnitude > rangeSqr)
                return false;

            candidatePosition = candidateTransform.Position;
            return true;
        }
    }
}
