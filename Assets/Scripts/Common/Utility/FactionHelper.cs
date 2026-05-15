using System.Collections.Generic;
using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// Shared faction checks and enemy search helpers.
    /// </summary>
    public static class FactionHelper
    {
        private static readonly List<int> _playerFactionEntities = new List<int>(16);
        private static readonly List<int> _hostileFactionEntities = new List<int>(256);

        public static void OnFactionComponentSet(int entityId, Faction previousFaction, Faction nextFaction, bool hadPreviousFaction)
        {
            if (hadPreviousFaction)
            {
                if (previousFaction == nextFaction)
                {
                    AddToFactionList(entityId, nextFaction);
                    return;
                }

                RemoveFromFactionList(entityId, previousFaction);
            }

            AddToFactionList(entityId, nextFaction);
        }

        public static void OnFactionComponentRemoved(int entityId, Faction previousFaction)
        {
            RemoveFromFactionList(entityId, previousFaction);
        }

        public static void OnEntityRemoved(int entityId)
        {
            RemoveFromFactionList(entityId, Faction.Player);
            RemoveFromFactionList(entityId, Faction.Hostile);
        }

        public static void ClearFactionIndex()
        {
            _playerFactionEntities.Clear();
            _hostileFactionEntities.Clear();
        }

        /// <summary>
        /// Returns whether caster and target are hostile to each other.
        /// Missing caster faction is treated as hostile for migration safety.
        /// Missing target faction or Neutral target is never hostile.
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
        /// Finds the nearest entity from hostile faction candidate lists.
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

            List<int> primaryEnemyList = GetPrimaryEnemyList(selfFaction);
            if (primaryEnemyList != null)
            {
                SearchCandidates(
                    primaryEnemyList,
                    selfEntityId,
                    selfPosition,
                    maxRangeSqr,
                    checkFOV,
                    forward,
                    halfFovCos,
                    requireStatComponent,
                    transformPool,
                    statPool,
                    ref targetPosition,
                    ref nearestEnemySqrDistance,
                    ref bestEntityId,
                    ref bestSqrDistance);
            }
            else
            {
                // Preserve previous fallback behavior: Neutral/self-unknown searches non-neutral factions.
                SearchCandidates(
                    _playerFactionEntities,
                    selfEntityId,
                    selfPosition,
                    maxRangeSqr,
                    checkFOV,
                    forward,
                    halfFovCos,
                    requireStatComponent,
                    transformPool,
                    statPool,
                    ref targetPosition,
                    ref nearestEnemySqrDistance,
                    ref bestEntityId,
                    ref bestSqrDistance);

                SearchCandidates(
                    _hostileFactionEntities,
                    selfEntityId,
                    selfPosition,
                    maxRangeSqr,
                    checkFOV,
                    forward,
                    halfFovCos,
                    requireStatComponent,
                    transformPool,
                    statPool,
                    ref targetPosition,
                    ref nearestEnemySqrDistance,
                    ref bestEntityId,
                    ref bestSqrDistance);
            }

            return bestEntityId;
        }

        private static List<int> GetFactionList(Faction faction)
        {
            switch (faction)
            {
                case Faction.Player:
                    return _playerFactionEntities;
                case Faction.Hostile:
                    return _hostileFactionEntities;
                default:
                    return null;
            }
        }

        private static List<int> GetPrimaryEnemyList(Faction selfFaction)
        {
            switch (selfFaction)
            {
                case Faction.Player:
                    return _hostileFactionEntities;
                case Faction.Hostile:
                    return _playerFactionEntities;
                default:
                    return null;
            }
        }

        private static void AddToFactionList(int entityId, Faction faction)
        {
            List<int> list = GetFactionList(faction);
            if (list == null || list.Contains(entityId))
                return;

            list.Add(entityId);
        }

        private static void RemoveFromFactionList(int entityId, Faction faction)
        {
            List<int> list = GetFactionList(faction);
            if (list == null)
                return;

            int index = list.IndexOf(entityId);
            if (index < 0)
                return;

            list.RemoveAt(index);
        }

        private static void SearchCandidates(
            List<int> candidates,
            int selfEntityId,
            Vector2 selfPosition,
            float maxRangeSqr,
            bool checkFOV,
            Vector2 forward,
            float halfFovCos,
            bool requireStatComponent,
            SparseSet<TransformComponent> transformPool,
            SparseSet<StatComponent> statPool,
            ref Vector2 targetPosition,
            ref float nearestEnemySqrDistance,
            ref int bestEntityId,
            ref float bestSqrDistance)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                int candidateId = candidates[i];
                if (candidateId == selfEntityId)
                    continue;

                if (transformPool.TryGet(candidateId, out var candidateTransform) == false)
                    continue;

                if (requireStatComponent && statPool.Contains(candidateId) == false)
                    continue;

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
        }
    }
}
