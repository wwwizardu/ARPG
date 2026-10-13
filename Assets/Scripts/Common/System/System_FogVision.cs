#nullable enable
using ARPG.Component;
using ARPG.Map;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 플레이어 시야를 계산한다 (FOG_OF_WAR_DESIGN §4). 매 프레임은 플레이어 실제 위치만 셰이더에 넘기고,
    /// 플레이어가 다른 칸으로 넘어갔거나 막힌 칸·맵이 바뀌었을 때만 8방향 재귀 그림자 투사로 보이는 칸을 다시 구한다.
    /// 1차는 벽만 막아 "벽에 가리지 않음"(화면의 벽 그림자)을, 시설이 있으면 2차로 벽과 시설을 함께 막아
    /// "지금 보임"(엔티티 숨김·이펙트 판정 기준)을 구한다. 두 값의 차이가 반투명 시설 그림자다. 막힌 칸 자체는 보이는 칸이다.
    /// 상태는 FogOfWarManager가 가진다. System_Render(Update 1000)가 이동을 적분한 뒤에 돌아야 원 중심이
    /// 이번 프레임 위치와 맞으므로 LateUpdate에 둔다.
    /// </summary>
    public sealed class System_FogVision : ILateUpdateSystem
    {
        // 8분면 좌표 변환 (열 dx·행 dy → 월드 x·y)
        private static readonly int[] OCTANT_XX = { 1, 0, 0, -1, -1, 0, 0, 1 };
        private static readonly int[] OCTANT_XY = { 0, 1, -1, 0, 0, -1, 1, 0 };
        private static readonly int[] OCTANT_YX = { 0, 1, 1, 0, 0, -1, -1, 0 };
        private static readonly int[] OCTANT_YY = { 1, 0, 0, 1, -1, 0, 0, -1 };

        public int Priority => 880;
        public float UpdateInterval => 0f;

        private SparseSet<TransformComponent> _transformPool = null!;
        private SparseSet<VisionComponent> _visionPool = null!;

        // 시야 계산 1회 안에서만 쓰는 막힘 종류 임시 격자. 계산마다 처음부터 다시 채운다 (계산 사이에 남는 상태 아님).
        // 칸마다 MapManager를 반복 조회하지 않고 계산 범위를 한 번만 읽는다.
        private byte[] _blockers = new byte[0];
        private int _blockerSize;
        private int _blockerOriginX;
        private int _blockerOriginY;

        public void OnCreate()
        {
            ComponentManager cm = AR.s.Component;
            _transformPool = cm.GetComponentPool<TransformComponent>();
            _visionPool = cm.GetComponentPool<VisionComponent>();
        }

        public void OnReset() { }

        public void OnLateUpdate(float deltaTime)
        {
            FogOfWarManager fog = AR.s.Fog;
            if (fog.IsActive == false) return;

            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (_transformPool.TryGet(playerId, out TransformComponent transform) == false ||
                _visionPool.TryGet(playerId, out VisionComponent vision) == false)
                return;

            Vector2 position = transform.Position;
            fog.SetVisionCenter(position, vision.Radius);
            // 언덕 테두리 띠에 발이 들어가 있어도 바닥 칸에서 비춘다. 벽 칸에서 비추면 벽을 따라 양옆이 가려진다
            MapManager map = AR.s.Map;
            Vector2Int standingTile = map.GetStandingTile(position);
            int cellX = standingTile.x;
            int cellY = standingTile.y;
            if (fog.NeedsVisionUpdate(cellX, cellY) == false) return;

            // 실제 위치와 칸 중심의 차이·바이리니어 보간을 위해 세 칸 여유를 둔다. 셰이더는 그림자를 거리 밝기에서 안개 밝기 쪽으로
            // 끌어내리므로, 계산 범위 밖의 0(가림)이 섞이는 곳은 거리 밝기가 이미 안개 밝기와 같은 곳(실제 거리 약 11.3칸 밖)이어야
            // 트인 곳의 시야 가장자리에 어두운 띠가 생기지 않는다. 두 칸 여유면 약 10칸에서 0.8%p 어두워진다.
            int castRadius = Mathf.CeilToInt(vision.Radius) + 3;
            bool hasFacility = FillBlockers(map, cellX, cellY, castRadius);
            fog.BeginVisionUpdate(cellX, cellY, castRadius);

            // 1차: 벽만 막는다. 시설이 없으면 같은 결과를 "지금 보임"으로 함께 표시하고 2차를 건너뛴다.
            fog.SetVisible(cellX, cellY, hasFacility == false);
            for (int octant = 0; octant < 8; octant++)
            {
                CastLight(fog, cellX, cellY, 1, 1f, 0f, castRadius,
                    OCTANT_XX[octant], OCTANT_XY[octant], OCTANT_YX[octant], OCTANT_YY[octant], false, hasFacility == false);
            }

            // 2차: 벽과 시설을 함께 막는다. 1차에서 보였지만 여기서 가려진 칸이 반투명 시설 그림자이고, 그곳의 엔티티는 숨긴다.
            if (hasFacility == true)
            {
                fog.SetClearOfFacilities(cellX, cellY);
                for (int octant = 0; octant < 8; octant++)
                {
                    CastLight(fog, cellX, cellY, 1, 1f, 0f, castRadius,
                        OCTANT_XX[octant], OCTANT_XY[octant], OCTANT_YX[octant], OCTANT_YY[octant], true, false);
                }
            }
            fog.EndVisionUpdate();
        }

        /// <summary>계산 범위(중심 ± 반경)의 막힘 종류를 한 번에 읽는다. 시설 칸이 하나라도 있으면 true.</summary>
        private bool FillBlockers(MapManager map, int centerX, int centerY, int radius)
        {
            _blockerSize = radius * 2 + 1;
            int count = _blockerSize * _blockerSize;
            if (_blockers.Length < count) _blockers = new byte[count];
            _blockerOriginX = centerX - radius;
            _blockerOriginY = centerY - radius;

            bool hasFacility = false;
            for (int localY = 0; localY < _blockerSize; localY++)
            {
                int row = localY * _blockerSize;
                for (int localX = 0; localX < _blockerSize; localX++)
                {
                    VisionBlocker blocker = map.GetVisionBlocker(_blockerOriginX + localX, _blockerOriginY + localY);
                    _blockers[row + localX] = (byte)blocker;
                    if (blocker == VisionBlocker.Facility) hasFacility = true;
                }
            }
            return hasFacility;
        }

        /// <summary>
        /// 재귀 그림자 투사 한 분면. start·end는 아직 가리지 않은 기울기 구간이고, 막힌 칸을 만나면 그 앞까지를 다음 행으로 넘긴다.
        /// facilitiesBlock이면 시설도 막고 보인 칸을 "지금 보임"으로, 아니면 벽만 막고 "벽에 가리지 않음"으로 표시한다.
        /// 재귀 깊이는 계산 반경을 넘지 않는다.
        /// </summary>
        private void CastLight(FogOfWarManager fog, int centerX, int centerY, int row, float start, float end, int radius,
            int xx, int xy, int yx, int yy, bool facilitiesBlock, bool alsoClearOfFacilities)
        {
            if (start < end) return;

            int radiusSqr = radius * radius;
            float newStart = 0f;
            for (int distance = row; distance <= radius; distance++)
            {
                int dy = -distance;
                bool blocked = false;
                for (int dx = -distance; dx <= 0; dx++)
                {
                    float leftSlope = (dx - 0.5f) / (dy + 0.5f);
                    float rightSlope = (dx + 0.5f) / (dy - 0.5f);
                    if (start < rightSlope) continue;
                    if (end > leftSlope) break;

                    int x = centerX + dx * xx + dy * xy;
                    int y = centerY + dx * yx + dy * yy;
                    if (dx * dx + dy * dy < radiusSqr)
                    {
                        if (facilitiesBlock == true) fog.SetClearOfFacilities(x, y);
                        else fog.SetVisible(x, y, alsoClearOfFacilities);
                    }

                    byte blocker = _blockers[(y - _blockerOriginY) * _blockerSize + (x - _blockerOriginX)];
                    bool isBlocked = facilitiesBlock == true ? blocker != (byte)VisionBlocker.None : blocker == (byte)VisionBlocker.Wall;
                    if (blocked == true)
                    {
                        if (isBlocked == true)
                        {
                            newStart = rightSlope;
                            continue;
                        }
                        blocked = false;
                        start = newStart;
                    }
                    else if (isBlocked == true && distance < radius)
                    {
                        blocked = true;
                        CastLight(fog, centerX, centerY, distance + 1, start, leftSlope, radius, xx, xy, yx, yy,
                            facilitiesBlock, alsoClearOfFacilities);
                        newStart = rightSlope;
                    }
                }
                if (blocked == true) break;
            }
        }
    }
}
