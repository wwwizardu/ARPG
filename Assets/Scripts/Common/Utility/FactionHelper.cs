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
        /// 엔티티 단위 적대 판정 — 결투 규칙(NPC_COMBAT_DESIGN §3.3)을 진영 판정보다 우선 적용하는 단일 chokepoint.
        /// 스킬 피격·발사체·장판·전파·AI 타겟 탐색은 모두 이 함수를 거친다.
        /// 1) 결투 쌍이면 적대 (동행 NPC와의 결투도 진영과 무관하게 적대)
        /// 2) 결투 중인 NPC는 결투 상대만 노린다 (Q10) — 다른 대상은 적대 아님
        /// 3) 결투 중인 NPC는 적대 진영(몬스터)의 적대 대상이 된다 [D21] (평소 몬스터는 Neutral NPC를 무시)
        /// 4) 그 외에는 기존 진영 판정
        /// 토템·장판 등 대리 시전체(CasterLinkComponent)는 1)·2)를 원 시전자 기준으로 본다 — 플레이어 토템도 결투 상대를 공격한다.
        /// 반대로 결투 중 NPC는 상대의 토템·소환물을 공격하지 않는다(2). 어그로 시스템 도입 때 재검토.
        /// 결투가 없으면(풀 비어 있음) 조회 한 번으로 진영 판정에 바로 위임한다.
        /// </summary>
        public static bool IsEntityHostile(int casterEntityId, Faction casterFaction, int targetEntityId, Faction targetFaction)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DuelComponent> duelPool = cm.GetComponentPool<DuelComponent>();
            if (duelPool.Count == 0)
                return IsFactionHostile(casterFaction, targetFaction);

            int duelCasterId = casterEntityId;
            if (cm.TryGetComponent<CasterLinkComponent>(casterEntityId, out CasterLinkComponent casterLink) == true)
                duelCasterId = casterLink.CasterEntityId;

            if (duelPool.TryGet(duelCasterId, out DuelComponent casterDuel) == true)
            {
                if (casterDuel.OpponentId == targetEntityId)
                    return true;
                if (cm.HasComponent<NpcTag>(duelCasterId) == true)
                    return false;
            }

            if (casterFaction == Faction.Hostile &&
                duelPool.Contains(targetEntityId) == true &&
                cm.HasComponent<NpcTag>(targetEntityId) == true)
                return true;

            return IsFactionHostile(casterFaction, targetFaction);
        }

        /// <summary>
        /// caster와 target이 서로 적대인지. caster 진영이 없으면 마이그레이션 안전을 위해 적대로 간주.
        /// target 진영이 없거나 Neutral이면 적대 아님. 결투 쌍 규칙은 IsEntityHostile이 우선 적용한다.
        /// </summary>
        public static bool IsHostileTo(int casterEntityId, int targetEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction) == false)
                return true;
            if (cm.TryGetComponent<FactionComponent>(targetEntityId, out var targetFaction) == false)
                return false;
            return IsEntityHostile(casterEntityId, casterFaction.FactionId, targetEntityId, targetFaction.FactionId);
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
            out float nearestEnemySqrDistance,
            bool requireLivingTarget = false)
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

                // 진영 필터 — 후보가 self와 적대일 때만 (진영 없으면 제외). 결투 쌍 규칙 포함
                if (factionPool.TryGet(candidateId, out var candidateFaction) == false)
                    continue;
                if (IsEntityHostile(selfEntityId, selfFaction, candidateId, candidateFaction.FactionId) == false)
                    continue;

                if (requireStatComponent && statPool.Contains(candidateId) == false)
                    continue;

                if (requireLivingTarget == true
                    && (cm.HasComponent<DestroyTag>(candidateId) == true
                        || statPool.TryGet(candidateId, out StatComponent candidateStat) == false
                        || candidateStat.CurrentHp <= 0))
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
