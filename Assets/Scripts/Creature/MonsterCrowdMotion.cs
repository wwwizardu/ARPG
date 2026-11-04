using ARPG.Component;
using UnityEngine;

namespace ARPG.Monster
{
    public delegate Vector2 MonsterTerrainSweep(Vector2 position, Vector2 delta, float radius);

    /// <summary>군집 이동의 기하학 계산. 스크래치·설정·개체 상태를 호출자가 소유한다.</summary>
    public static class MonsterCrowdMotion
    {
        // 이보다 얕게 닿은 이웃은 겹침이 아니라 접촉으로 본다 (거리² 기준)
        private const float OverlapToleranceSqr = 0.00001f;

        public static Vector2 ResolveMove(MonsterCrowdGrid grid, int index, Vector2 desiredVelocity,
            float dt, float now, MonsterCrowdSettings settings, ref MonsterCrowdComponent state,
            MonsterTerrainSweep terrain)
        {
            // 통과 중인 상대와 완전히 떨어질 때까지 통과 상태를 이어 간다. 겹친 채 끝나면 다시 막혀 되밀린다.
            if (state.PassUntil > now && grid.IsTouchingPassPartner(index, settings.Clearance) == true)
                state.PassUntil = now + settings.PassHoldTime;
            Vector2 desired = desiredVelocity * dt;
            float distance = desired.magnitude;
            if (distance < 0.00001f)
            {
                state.IsWaiting = false;
                state.WaitBlockerId = 0;
                state.RetryAt = 0f;
                return Vector2.zero;
            }
            if (state.IsWaiting == true && now < state.RetryAt)
                return Vector2.zero;

            MonsterCrowdGrid.Body body = grid.GetBody(index);
            bool wasWaiting = state.IsWaiting;
            Vector2 heading = desired / distance;
            float speed = desiredVelocity.magnitude;
            float probeDistance = Mathf.Max(distance, speed * settings.LookAheadTime);
            // 정면과 후보 방향은 모두 한 프레임 이동(distance)이라 이웃을 한 번만 모아 함께 쓴다.
            grid.GatherNeighbors(index, distance, settings.Clearance);
            Vector2 direct = ResolveCandidate(grid, index, desired, settings.Clearance, dt, terrain,
                out bool crowdBlocked, out int blockerId);
            Vector2 selected = direct;
            int selectedSide = 0;
            // 정면이 완전히 막히면 정면 점수가 0이라 앞보기 결과가 쓰이지 않으므로 검사하지 않는다.
            Vector2 probe = Vector2.zero;
            if (direct.sqrMagnitude >= 0.00000001f)
                probe = grid.ClipLookAhead(index, heading * probeDistance, settings.Clearance,
                    probeDistance / speed);
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
                        Vector2 candidateDelta = direction * distance;
                        // 끝까지 가도 지금 최고 점수를 넘을 수 없는 후보는 지형·이웃 검사를 하지 않는다.
                        // bestScore가 0이면 모든 후보의 막힘 여부(crowdBlocked)가 대기 판정에 필요하므로 건너뛰지 않는다.
                        if (bestScore > 0f
                            && MaximumScore(candidateDelta, heading, distance, side, preferredSide) <= bestScore)
                            continue;
                        Vector2 candidate = ResolveCandidate(grid, index, candidateDelta,
                            settings.Clearance, dt, terrain, out bool candidateCrowdBlocked, out _);
                        crowdBlocked |= candidateCrowdBlocked;
                        // 지형·이웃에 잘린 결과가 뒤로 가면 고르지 않는다. 벽에 막힌 옆 후보는 미끄러진 뒤 뒤쪽 성분만 남을 수 있는데,
                        // 선호 방향 가산점 때문에 그 후보가 기다리기를 이기면 통로에서 앞뒤로 흔들리며 대기·통과·A* 미룸이 작동하지 않는다.
                        // 옆(진행 성분 0)과 앞쪽 미끄러짐은 허용한다. 1%는 순수 옆 후보의 계산 오차 여유다.
                        if (Vector2.Dot(candidate, heading) < -distance * 0.01f)
                            continue;
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
            // 정면이 지형에 막히고 옆 후보만 몬스터에 막힌 대기는 막은 상대가 없다(0). 이 대기는 A* stuck 재계산을 미루지 않는다.
            state.WaitBlockerId = state.IsWaiting == true ? blockerId : 0;
            state.RetryAt = state.IsWaiting == true ? now + settings.RetryInterval : 0f;
            if (state.IsWaiting == true)
            {
                if (wasWaiting == false)
                    state.WaitStartTime = now;
                // 오래 막히면 막은 상대를 통과 상대로 지정한다. 상대를 따라가 내게 돌아오는 고리(서로 막은 둘, 셋 이상의 고리 교착)일 때만
                // 실제로 통과하므로 앞 개체가 나 때문에 막힌 게 아닌 줄 서기는 그대로 기다린다. 움직일 수 없는 개체는 혼자 지정해도 통과한다.
                if (blockerId != 0 && now - state.WaitStartTime >= settings.PassWaitTime)
                {
                    state.PassPartnerId = blockerId;
                    state.PassUntil = now + settings.PassHoldTime;
                }
            }
            return selected;
        }

        public static Vector2 RecoverOverlap(MonsterCrowdGrid grid, int index, float maximumDistance,
            MonsterTerrainSweep terrain)
        {
            MonsterCrowdGrid.Body body = grid.GetBody(index);
            if (body.CanDisplace == false || maximumDistance <= 0f)
                return Vector2.zero;
            // 보정으로 새로 닿을 수 있는 이웃까지 찾는다.
            grid.FindNeighbors(index, Vector2.zero, maximumDistance);
            Vector2 correction = Vector2.zero;
            int contacts = 0;
            for (int i = 0; i < grid.NeighborCount; i++)
            {
                MonsterCrowdGrid.Body other = grid.GetBody(grid.GetNeighbor(i));
                if (body.CanPassThrough(other) == true)
                    continue;
                Vector2 offset = body.Position - other.Position;
                float radii = body.Radius + other.Radius;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr >= radii * radii - OverlapToleranceSqr)
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

            // 이미 겹친 이웃은 위 보정량에 반영됐으므로 그쪽으로 더 들어가도 막지 않는다.
            // 그래서 양쪽이 막힌 세 개체 이상의 겹침도 멈추지 않고 겹침을 나눠 가지며 풀린다.
            // 겹치지 않은 이웃에 새로 닿는 경우만 그쪽으로 파고드는 성분을 빼고 옆으로 빗겨 보낸다.
            for (int i = 0; i < grid.NeighborCount; i++)
            {
                MonsterCrowdGrid.Body other = grid.GetBody(grid.GetNeighbor(i));
                if (body.CanPassThrough(other) == true)
                    continue;
                Vector2 offset = body.Position - other.Position;
                float radii = body.Radius + other.Radius;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr < radii * radii - OverlapToleranceSqr || distanceSqr < 0.00000001f)
                    continue;
                if ((offset + correction).sqrMagnitude >= radii * radii)
                    continue;
                Vector2 normal = offset / Mathf.Sqrt(distanceSqr);
                float inward = Vector2.Dot(correction, normal);
                if (inward < 0f)
                    correction -= normal * inward;
            }
            // 지형은 절대 제약이므로 마지막에 적용한다.
            return terrain(body.Position, correction, body.Radius) - body.Position;
        }

        private static Vector2 ResolveCandidate(MonsterCrowdGrid grid, int index, Vector2 delta,
            float padding, float dt, MonsterTerrainSweep terrain, out bool crowdBlocked, out int blockerId)
        {
            MonsterCrowdGrid.Body body = grid.GetBody(index);
            Vector2 clipped = terrain(body.Position, delta, body.Radius) - body.Position;
            Vector2 allowed = grid.ClipMoveGathered(index, clipped, padding, dt, out blockerId);
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

        /// <summary>
        /// 후보 delta가 받을 수 있는 Score 상한. 지형 미끄러짐은 sub-step마다 축 성분만 버리고 이웃 검사는 길이만 줄이므로,
        /// 진행 방향 성분은 원래 벡터·x성분만·y성분만·0 중 가장 큰 값을 넘지 않고 길이는 delta를 넘지 않는다.
        /// </summary>
        private static float MaximumScore(Vector2 delta, Vector2 heading, float desiredDistance,
            int side, int preferredSide)
        {
            float alongHeading = Mathf.Max(Mathf.Max(0f, Vector2.Dot(delta, heading)),
                Mathf.Max(delta.x * heading.x, delta.y * heading.y));
            return alongHeading + delta.magnitude * 0.3f
                + (side == preferredSide ? desiredDistance * 0.08f : 0f);
        }
    }
}
