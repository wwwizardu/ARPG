using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 진영(적대) 판정과 가장 가까운 적 탐색.
    /// 판정은 FactionComponent를 직접 조회한다 — 역방향 인덱스/캐시 없음.
    /// 소비자는 자기 후보 집합(예: TransformComponent 풀)을 순회하며 IsHostileTo/IsFactionHostile로 적을 거른다.
    /// </summary>
    public static class FactionHelper
    {
        /// <summary>
        /// 해소된 두 진영값 사이의 적대 판정. 중립(Neutral) 타겟은 결코 적대가 아니다.
        /// (caster/target 진영이 이미 확정된 핫루프용 — 단일 규칙 chokepoint)
        /// </summary>
        public static bool IsFactionHostile(Faction casterFaction, Faction targetFaction)
        {
            if (targetFaction == Faction.Neutral)
                return false;
            return targetFaction != casterFaction;
        }

        /// <summary>
        /// caster와 target이 서로 적대인지. caster 진영이 없으면 마이그레이션 안전을 위해 적대로 간주.
        /// target 진영이 없거나 Neutral이면 적대 아님.
        /// </summary>
        public static bool IsHostileTo(int casterEntityId, int targetEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction) == false)
                return true;
            if (cm.TryGetComponent<FactionComponent>(targetEntityId, out var targetFaction) == false)
                return false;
            return IsFactionHostile(casterFaction.FactionId, targetFaction.FactionId);
        }

        /// <summary>
        /// TransformComponent를 가진 엔티티를 순회하며 self와 적대인 가장 가까운 적을 찾는다.
        /// 역방향 인덱스 없이 후보를 진영(IsFactionHostile)으로 거른다.
        /// </summary>
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
            float halfFovCos = checkFOV ? Mathf.Cos(fieldOfView * 0.5f * Mathf.Deg2Rad) : -1f;

            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
            SparseSet<StatComponent> statPool = cm.GetComponentPool<StatComponent>();
            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int candidateId = transformPool.GetEntityId(i);
                if (candidateId == selfEntityId)
                    continue;

                // 진영 필터 — 후보가 self와 적대일 때만 (진영 없으면 제외)
                if (factionPool.TryGet(candidateId, out var candidateFaction) == false)
                    continue;
                if (IsFactionHostile(selfFaction, candidateFaction.FactionId) == false)
                    continue;

                if (requireStatComponent && statPool.Contains(candidateId) == false)
                    continue;

                TransformComponent candidateTransform = transformPool.GetByIndex(i);
                Vector2 toCandidate = candidateTransform.Position - selfPosition;
                float sqrDistance = toCandidate.sqrMagnitude;

                if (sqrDistance < nearestEnemySqrDistance)
                    nearestEnemySqrDistance = sqrDistance;

                if (maxRangeSqr > 0f && sqrDistance > maxRangeSqr)
                    continue;

                if (checkFOV)
                {
                    float distance = Mathf.Sqrt(sqrDistance);
                    if (distance < 0.001f)
                        continue;

                    float dot = Vector2.Dot(forward, toCandidate) / distance;
                    if (dot < halfFovCos)
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
