using System.Collections.Generic;
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
        private const float TargetMemoryDuration = 5f;
        // 호출마다 비우는 스크래치. 그룹 전파는 재귀 호출하지 않는다.
        private static readonly List<int> _groupMembersScratch = new();

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

        /// <summary>유효한 적의 피격은 감지 거리와 무관하게 자신과 같은 스폰 그룹에 알린다.</summary>
        public static void AlertMonster(int entityId, int attackerEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.HasComponent<MonsterTag>(entityId) == false ||
                FactionHelper.IsHostileTo(entityId, attackerEntityId) == false ||
                TryGetLivingTarget(cm, attackerEntityId, out Vector2 targetPosition) == false)
                return;

            TryAlertGroupMember(cm, entityId, attackerEntityId, targetPosition);
            ShareGroupTarget(cm, entityId, attackerEntityId, targetPosition);
        }

        /// <summary>비동기 생성 중 뒤늦게 합류한 구성원도 진행 중인 그룹 교전을 이어받는다.</summary>
        public static void InheritSpawnGroupTarget(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (AR.s.Monster == null)
                return;

            AR.s.Monster.CollectSpawnGroupMembers(entityId, _groupMembersScratch);
            for (int i = 0; i < _groupMembersScratch.Count; i++)
            {
                int memberId = _groupMembersScratch[i];
                if (memberId == entityId || IsActiveMonster(cm, memberId) == false ||
                    cm.TryGetComponent<AIPerceptionComponent>(memberId, out var memberPerception) == false ||
                    memberPerception.IsAlerted == false ||
                    cm.TryGetComponent<AIComponent>(memberId, out var memberAi) == false ||
                    TryGetLivingTarget(cm, memberAi.TargetEntityId, out Vector2 targetPosition) == false ||
                    Time.time - memberPerception.LastDetectionTime > TargetMemoryDuration)
                    continue;

                if (TryAlertGroupMember(cm, entityId, memberAi.TargetEntityId, targetPosition) == true)
                {
                    // 새 구성원 때문에 원거리 추적 유예 시간이 연장되지 않게 한다.
                    cm.TryGetComponent<AIPerceptionComponent>(entityId, out var perception);
                    perception.LastDetectionTime = memberPerception.LastDetectionTime;
                    cm.SetComponent(entityId, perception);
                }
                return;
            }
        }

        /// <summary>소환된 몬스터가 주인이 노리던 대상을 바로 이어받는다 (SKILL_DESIGN §6.12).</summary>
        public static void InheritSummonerTarget(int entityId, int ownerEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<AIComponent>(ownerEntityId, out var ownerAi) == false ||
                TryGetLivingTarget(cm, ownerAi.TargetEntityId, out Vector2 targetPosition) == false)
                return;

            TryAlertGroupMember(cm, entityId, ownerAi.TargetEntityId, targetPosition);
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // AIPerceptionComponent 풀 가져오기
            ComponentManager cm = AR.s.Component;
            SparseSet<AIPerceptionComponent> perceptionPool = cm.GetComponentPool<AIPerceptionComponent>();
            if (perceptionPool == null || perceptionPool.Count == 0)
                return;

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

                if (cm.HasComponent<MonsterTag>(entityId) == true)
                {
                    if (IsActiveMonster(cm, entityId) == false)
                    {
                        AIStateHelper.ClearTarget(entityId, ref ai);
                        continue;
                    }

                    if (perception.IsAlerted == true)
                    {
                        if (TryRetainAlertedTarget(cm, entityId, transform.Position, ref ai, ref perception) == true)
                            continue;

                        AIStateHelper.ClearTarget(entityId, ref ai);
                        perception.IsAlerted = false;
                    }
                }

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

                if (targetEntityId != -1 && cm.HasComponent<MonsterTag>(entityId) == true &&
                    TryGetLivingTarget(cm, targetEntityId, out _) == false)
                    targetEntityId = -1;

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
            ref AIPerceptionComponent perception, int targetEntityId, Vector2 targetPosition, bool shareGroup = true)
        {
            bool isMonster = cm.HasComponent<MonsterTag>(entityId);
            bool shouldShare = isMonster == true && shareGroup == true &&
                (perception.IsAlerted == false || ai.TargetEntityId != targetEntityId);
            cm.AddComponent(entityId, new AICanSeeTargetTag());

            ai.TargetEntityId = targetEntityId;
            ai.LastKnownTargetPos = targetPosition;
            cm.SetComponent(entityId, ai);

            perception.LastDetectionTime = Time.time;
            if (isMonster == true)
                perception.IsAlerted = true;
            cm.SetComponent(entityId, perception);

            if (shouldShare == true)
                ShareGroupTarget(cm, entityId, targetEntityId, targetPosition);
        }

        private static bool TryRetainAlertedTarget(ComponentManager cm, int entityId, Vector2 selfPosition,
            ref AIComponent ai, ref AIPerceptionComponent perception)
        {
            if (FactionHelper.IsHostileTo(entityId, ai.TargetEntityId) == false ||
                TryGetLivingTarget(cm, ai.TargetEntityId, out Vector2 targetPosition) == false)
                return false;

            float distanceSqr = (targetPosition - selfPosition).sqrMagnitude;
            if (distanceSqr <= perception.LoseTargetRange * perception.LoseTargetRange)
            {
                MarkTargetSeen(cm, entityId, ref ai, ref perception, ai.TargetEntityId, targetPosition, false);
                return true;
            }

            // 놓침 범위 밖 피격에도 반응하되, 마지막 교전 후 유예 시간이 지나면 해제한다.
            if (Time.time - perception.LastDetectionTime > TargetMemoryDuration)
                return false;

            ai.LastKnownTargetPos = targetPosition;
            cm.SetComponent(entityId, ai);
            cm.AddComponent(entityId, new AICanSeeTargetTag());
            return true;
        }

        private static void ShareGroupTarget(ComponentManager cm, int entityId, int targetEntityId, Vector2 targetPosition)
        {
            if (AR.s.Monster == null)
                return;

            AR.s.Monster.CollectSpawnGroupMembers(entityId, _groupMembersScratch);
            for (int i = 0; i < _groupMembersScratch.Count; i++)
            {
                int memberId = _groupMembersScratch[i];
                if (memberId != entityId)
                    TryAlertGroupMember(cm, memberId, targetEntityId, targetPosition);
            }
        }

        private static bool TryAlertGroupMember(ComponentManager cm, int entityId, int targetEntityId, Vector2 targetPosition)
        {
            if (IsActiveMonster(cm, entityId) == false ||
                FactionHelper.IsHostileTo(entityId, targetEntityId) == false ||
                cm.TryGetComponent<AIComponent>(entityId, out var ai) == false ||
                cm.TryGetComponent<AIPerceptionComponent>(entityId, out var perception) == false)
                return false;

            MarkTargetSeen(cm, entityId, ref ai, ref perception, targetEntityId, targetPosition, false);
            return true;
        }

        private static bool IsActiveMonster(ComponentManager cm, int entityId)
        {
            return cm.HasComponent<MonsterTag>(entityId) == true &&
                cm.HasComponent<DestroyTag>(entityId) == false &&
                cm.TryGetComponent<StatComponent>(entityId, out var stat) == true && stat.CurrentHp > 0 &&
                (cm.TryGetComponent<ActivationDistanceComponent>(entityId, out var activation) == false ||
                 activation.IsActivated == true);
        }

        private static bool TryGetLivingTarget(ComponentManager cm, int entityId, out Vector2 position)
        {
            position = Vector2.zero;
            if (AR.s.Entity.IsEntityRegistered(entityId) == false ||
                cm.HasComponent<DestroyTag>(entityId) == true ||
                cm.TryGetComponent<StatComponent>(entityId, out var stat) == false || stat.CurrentHp <= 0 ||
                cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false ||
                (cm.TryGetComponent<ActivationDistanceComponent>(entityId, out var activation) == true &&
                 activation.IsActivated == false))
                return false;

            position = transform.Position;
            return true;
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
