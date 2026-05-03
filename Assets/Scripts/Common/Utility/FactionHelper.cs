using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 진영 기반 적대 판정 + 적 탐색 공용 유틸.
    /// System_Skill, System_AI_Perception, AI Stationary 핸들러 등이 공유.
    /// </summary>
    public static class FactionHelper
    {
        /// <summary>
        /// caster와 target이 적대 관계인지 판단.
        /// caster가 FactionComponent 없으면 true (마이그레이션 안전망).
        /// target이 FactionComponent 없거나 Neutral이면 false (NPC·중립 보호).
        /// 진영이 다르면 true.
        /// </summary>
        public static bool IsHostileTo(int casterEntityId, int targetEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction) == false)
                return true;
            if (cm.TryGetComponent<FactionComponent>(targetEntityId, out var targetFaction) == false)
                return false;
            if (targetFaction.FactionId == Faction.Neutral)
                return false;
            return targetFaction.FactionId != casterFaction.FactionId;
        }

        /// <summary>
        /// 진영이 다른 가장 가까운 적 엔티티를 탐색.
        /// </summary>
        /// <param name="selfEntityId">탐색 주체 엔티티 ID (자기 자신은 제외)</param>
        /// <param name="selfPosition">탐색 주체 위치</param>
        /// <param name="selfRotationDegrees">탐색 주체 회전(시야각 검사용). FOV가 360이면 무시 가능</param>
        /// <param name="selfFaction">탐색 주체 진영</param>
        /// <param name="maxRangeSqr">탐지 최대 거리(제곱). 0 이하면 거리 무제한</param>
        /// <param name="fieldOfView">시야각(도). 360 이상이면 시야각 검사 생략</param>
        /// <param name="requireStatComponent">true면 StatComponent 가진 엔티티만 후보 (살아있는 적만)</param>
        /// <param name="targetPosition">선택된 적의 위치 (없으면 zero)</param>
        /// <param name="nearestEnemySqrDistance">필터 무관 가장 가까운 적과의 거리 제곱 (LoseTargetRange 비교용). 적 없으면 float.MaxValue</param>
        /// <returns>가장 가까운 적 엔티티 ID, 없으면 -1</returns>
        public static int FindNearestEnemy(
            int selfEntityId,
            Vector2 selfPosition,
            float selfRotationDegrees,
            Faction selfFaction,
            float maxRangeSqr,
            float fieldOfView,
            bool requireStatComponent,
            out Vector2 targetPosition,
            out float nearestEnemySqrDistance)
        {
            ComponentManager cm = AR.s.Component;
            targetPosition = Vector2.zero;
            nearestEnemySqrDistance = float.MaxValue;
            int bestEntityId = -1;
            float bestSqrDistance = float.MaxValue;

            bool checkFOV = fieldOfView < 360f;
            Vector2 forward = checkFOV
                ? (Vector2)(Quaternion.Euler(0f, 0f, selfRotationDegrees) * Vector2.right)
                : Vector2.right;

            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
            for (int i = 0; i < factionPool.Count; i++)
            {
                int candidateId = factionPool.GetEntityId(i);
                if (candidateId == selfEntityId)
                    continue;

                FactionComponent candidateFaction = factionPool.GetByIndex(i);
                if (candidateFaction.FactionId == selfFaction || candidateFaction.FactionId == Faction.Neutral)
                    continue;

                if (cm.TryGetComponent<TransformComponent>(candidateId, out var candidateTransform) == false)
                    continue;

                if (requireStatComponent && cm.HasComponent<StatComponent>(candidateId) == false)
                    continue;

                Vector2 toCandidate = candidateTransform.Position - selfPosition;
                float sqrDistance = toCandidate.sqrMagnitude;

                // 필터 무관 가장 가까운 적 추적 (LoseTargetRange 비교용)
                if (sqrDistance < nearestEnemySqrDistance)
                    nearestEnemySqrDistance = sqrDistance;

                // 거리 필터
                if (maxRangeSqr > 0f && sqrDistance > maxRangeSqr)
                    continue;

                // 시야각 필터
                if (checkFOV)
                {
                    float distance = Mathf.Sqrt(sqrDistance);
                    if (distance < 0.001f)
                        continue;
                    Vector2 dirToCandidate = toCandidate / distance;
                    float angle = Vector2.Angle(forward, dirToCandidate);
                    if (angle > fieldOfView * 0.5f)
                        continue;
                }

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestEntityId = candidateId;
                    targetPosition = candidateTransform.Position;
                }
            }

            return bestEntityId;
        }
    }
}
