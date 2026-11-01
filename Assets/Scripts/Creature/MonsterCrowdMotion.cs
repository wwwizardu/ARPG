using ARPG.Component;
using UnityEngine;

namespace ARPG.Monster
{
    public delegate Vector2 MonsterTerrainSweep(Vector2 position, Vector2 delta, float radius);

    /// <summary>군집 이동의 기하학 계산. 스크래치·설정·개체 상태를 호출자가 소유한다.</summary>
    public static class MonsterCrowdMotion
    {
        public static Vector2 ResolveMove(MonsterCrowdGrid grid, int index, Vector2 desiredVelocity,
            float dt, float now, MonsterCrowdSettings settings, ref MonsterCrowdComponent state,
            MonsterTerrainSweep terrain)
        {
            Vector2 desired = desiredVelocity * dt;
            float distance = desired.magnitude;
            if (distance < 0.00001f)
            {
                state.IsWaiting = false;
                state.RetryAt = 0f;
                return Vector2.zero;
            }
            if (state.IsWaiting == true && now < state.RetryAt)
                return Vector2.zero;

            MonsterCrowdGrid.Body body = grid.GetBody(index);
            Vector2 heading = desired / distance;
            Vector2 direct = ResolveCandidate(grid, index, desired, settings.Clearance, terrain, out bool crowdBlocked);
            Vector2 selected = direct;
            int selectedSide = 0;
            float probeDistance = Mathf.Max(distance, desiredVelocity.magnitude * settings.LookAheadTime);
            Vector2 probe = grid.ClipMove(index, heading * probeDistance, settings.Clearance);
            bool obstructed = direct.sqrMagnitude < desired.sqrMagnitude * 0.99f
                || probe.sqrMagnitude < probeDistance * probeDistance * 0.99f;

            if (settings.AvoidanceEnabled == true && obstructed == true)
            {
                int preferredSide = state.AvoidUntil > now && state.AvoidSide != 0
                    ? state.AvoidSide : ((body.EntityId & 1) == 0 ? 1 : -1);
                float bestScore = Score(direct, heading, distance, 0, preferredSide);
                if (probeDistance > 0.00001f)
                    bestScore *= 0.75f + 0.25f * Mathf.Clamp01(probe.magnitude / probeDistance);
                for (int step = 1; step <= 3; step++)
                {
                    float angle = step * 30f * Mathf.Deg2Rad;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                    {
                        int side = sideIndex == 0 ? preferredSide : -preferredSide;
                        Vector2 direction = new Vector2(heading.x * cosine - heading.y * sine * side,
                            heading.y * cosine + heading.x * sine * side);
                        Vector2 candidate = ResolveCandidate(grid, index, direction * distance,
                            settings.Clearance, terrain, out bool candidateCrowdBlocked);
                        crowdBlocked |= candidateCrowdBlocked;
                        float score = Score(candidate, heading, distance, side, preferredSide);
                        if (score > bestScore + 0.000001f)
                        {
                            bestScore = score;
                            selected = candidate;
                            selectedSide = side;
                        }
                    }
                }
            }

            if (selectedSide != 0)
            {
                if (state.AvoidSide != selectedSide || state.AvoidUntil <= now)
                    state.AvoidUntil = now + settings.SideHoldTime;
                state.AvoidSide = selectedSide;
            }
            else if (state.AvoidUntil <= now)
                state.AvoidSide = 0;
            // 지형만으로 막힌 정지는 군집 대기가 아니다. A*의 기존 stuck 재계산을 유지한다.
            state.IsWaiting = selected.sqrMagnitude < 0.00000001f && crowdBlocked == true;
            state.RetryAt = state.IsWaiting == true ? now + settings.RetryInterval : 0f;
            return selected;
        }

        public static Vector2 RecoverOverlap(MonsterCrowdGrid grid, int index, float maximumDistance,
            MonsterTerrainSweep terrain)
        {
            MonsterCrowdGrid.Body body = grid.GetBody(index);
            if (body.CanDisplace == false || maximumDistance <= 0f)
                return Vector2.zero;
            grid.FindNeighbors(index, Vector2.zero, 0f);
            Vector2 correction = Vector2.zero;
            int contacts = 0;
            for (int i = 0; i < grid.NeighborCount; i++)
            {
                MonsterCrowdGrid.Body other = grid.GetBody(grid.GetNeighbor(i));
                Vector2 offset = body.Position - other.Position;
                float radii = body.Radius + other.Radius;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr >= radii * radii - 0.00001f)
                    continue;
                float distance = Mathf.Sqrt(distanceSqr);
                Vector2 direction;
                if (distance > 0.00001f)
                    direction = offset / distance;
                else
                {
                    // 같은 두 개체는 반드시 반대 방향으로 분리한다. 랜덤 상태와 프레임 순서에 의존하지 않는다.
                    int low = Mathf.Min(body.EntityId, other.EntityId);
                    int high = Mathf.Max(body.EntityId, other.EntityId);
                    float angle = ((unchecked(low * 73856093 ^ high * 19349663) & 1023) / 1024f)
                        * Mathf.PI * 2f;
                    direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (body.EntityId > other.EntityId)
                        direction = -direction;
                }
                float share = body.DisplacementWeight / (body.DisplacementWeight + other.DisplacementWeight);
                correction += direction * ((radii - distance + 0.0001f) * share);
                contacts++;
            }
            if (contacts == 0)
                return Vector2.zero;
            correction = Vector2.ClampMagnitude(correction / contacts, maximumDistance);
            Vector2 clipped = terrain(body.Position, correction, body.Radius) - body.Position;
            return grid.ClipMove(index, clipped, 0f);
        }

        private static Vector2 ResolveCandidate(MonsterCrowdGrid grid, int index, Vector2 delta,
            float padding, MonsterTerrainSweep terrain, out bool crowdBlocked)
        {
            MonsterCrowdGrid.Body body = grid.GetBody(index);
            Vector2 clipped = terrain(body.Position, delta, body.Radius) - body.Position;
            Vector2 allowed = grid.ClipMove(index, clipped, padding);
            crowdBlocked = allowed.sqrMagnitude + 0.00000001f < clipped.sqrMagnitude;
            return allowed;
        }

        private static float Score(Vector2 delta, Vector2 heading, float desiredDistance,
            int side, int preferredSide)
        {
            if (delta.sqrMagnitude < 0.00000001f)
                return 0f;
            return Vector2.Dot(delta, heading) + delta.magnitude * 0.3f
                + (side == preferredSide ? desiredDistance * 0.08f : 0f);
        }
    }
}
