using System;
using UnityEngine;

namespace ARPG.Monster
{
    /// <summary>
    /// MonsterManager 소유의 프레임 스크래치. 셀 heads/세대와 개체의 양방향 연결 배열을 재사용한다.
    /// 플레이어 주변 배열 밖의 개체는 overflow 연결에 보관해 경계에서 충돌을 누락하지 않는다.
    /// </summary>
    public sealed class MonsterCrowdGrid
    {
        public struct Body
        {
            public int EntityId;
            public Vector2 Position;
            public float Radius;
            public bool CanDisplace;
            public float DisplacementWeight;
            public int PassPartnerId;
            // 지난 프레임에 스스로 이동한 속도 (겹침 보정 제외). 앞서가는 이웃이 계속 간다고 예측하는 데만 쓴다 (예측은 덜 막힐 때만 반영).
            public Vector2 PredictedVelocity;
            // 이번 프레임 이동을 이미 마쳤는지. 이동을 마친 이웃은 이번 프레임에 더 움직이지 않는다.
            public bool HasMoved;
            // 통과 상대를 따라가면 자기 자신으로 돌아오는지(ResolvePassCycles가 정한다). 둘이 서로 지정한 경우는 길이 2인 고리다.
            public bool InPassCycle;

            /// <summary>
            /// 통과 상대가 같은 고리에 있거나(서로 막아 기다리는 둘·셋 이상의 고리) 움직일 수 없는 개체면 장애물로 보지 않는다.
            /// 고리에 있으면 통과 상대도 같은 고리다(상대의 다음 상대를 따라가면 나로 돌아온다).
            /// </summary>
            public bool CanPassThrough(in Body other)
            {
                if (PassPartnerId != other.EntityId)
                    return false;
                return other.CanDisplace == false || InPassCycle == true;
            }
        }

        // 통과 상대를 따라가며 고리를 찾을 때 최대 단계 수
        private const int MaxPassCycleLength = 8;

        private int[] _heads = Array.Empty<int>();
        private uint[] _generations = Array.Empty<uint>();
        private Body[] _bodies = Array.Empty<Body>();
        private int[] _next = Array.Empty<int>();
        private int[] _previous = Array.Empty<int>();
        private int[] _cells = Array.Empty<int>();
        private int[] _neighbors = Array.Empty<int>();
        private int[] _gathered = Array.Empty<int>();
        // 고리 판정용: 통과 상대가 있는 몸의 인덱스. ResolvePassCycles가 매번 비우고 쓴다.
        private int[] _partnered = Array.Empty<int>();
        private int _width;
        private float _cellSize;
        private Vector2Int _origin;
        private uint _generation;
        private int _overflowHead = -1;
        private float _maximumRadius;
        // GatherNeighbors로 모은 이웃 목록의 개수·주인·이동 거리 한도. FindNeighbors와 버퍼가 달라 다른 검색에 덮이지 않는다.
        private int _gatheredCount;
        private int _gatheredIndex = -1;
        private float _gatheredReach;

        public int Count { get; private set; }
        public int NeighborCount { get; private set; }
        public int OverflowCount { get; private set; }
        public int CandidateChecks { get; private set; }
        public int NeighborQueries { get; private set; }

        /// <summary>처음 한 번. 개체 배열 용량만 잡는다. 셀 배열은 ApplySettings가 격자 폭에 맞춰 만든다.</summary>
        public void Initialize(int capacity = 512)
        {
            EnsureCapacity(capacity);
        }

        /// <summary>매 프레임 Begin 전에 인스펙터 설정을 적용한다. 격자 폭이 바뀔 때만 셀 배열을 다시 만든다.</summary>
        public void ApplySettings(int width, float cellSize)
        {
            width = Mathf.Clamp(width, 8, 256);
            if (_width != width || _heads.Length == 0)
            {
                _heads = new int[width * width];
                _generations = new uint[width * width];
                _generation = 0;
            }
            _width = width;
            _cellSize = Mathf.Max(0.25f, cellSize);
        }

        public void Begin(Vector2 center)
        {
            _generation++;
            if (_generation == 0)
            {
                Array.Clear(_generations, 0, _generations.Length);
                _generation = 1;
            }
            _origin = new Vector2Int(Mathf.FloorToInt(center.x / _cellSize) - _width / 2,
                Mathf.FloorToInt(center.y / _cellSize) - _width / 2);
            Count = 0;
            NeighborCount = 0;
            OverflowCount = 0;
            CandidateChecks = 0;
            NeighborQueries = 0;
            _maximumRadius = 0f;
            _overflowHead = -1;
            _gatheredIndex = -1;
        }

        /// <summary>이번 프레임 점유만 비운다 (기능이 꺼진 프레임용). 다음 Begin이 세대를 바꾸므로 셀과 개체 배열은 지우지 않는다.</summary>
        public void Clear()
        {
            Count = 0;
            NeighborCount = 0;
            OverflowCount = 0;
            CandidateChecks = 0;
            NeighborQueries = 0;
            _overflowHead = -1;
            _maximumRadius = 0f;
            _gatheredCount = 0;
            _gatheredIndex = -1;
        }

        /// <summary>Initialize 직후 상태로 되돌린다. 개체 배열 용량은 유지하고 셀 배열은 다음 ApplySettings가 다시 만든다.</summary>
        public void Reset()
        {
            Clear();
            _heads = Array.Empty<int>();
            _generations = Array.Empty<uint>();
            _width = 0;
            _cellSize = 0f;
            _origin = Vector2Int.zero;
            _generation = 0;
            _gatheredReach = 0f;
        }

        public int Add(int entityId, Vector2 position, float radius, bool canDisplace, float displacementWeight = 1f,
            int passPartnerId = 0, Vector2 predictedVelocity = default)
        {
            EnsureCapacity(Count + 1);
            int index = Count++;
            _bodies[index] = new Body { EntityId = entityId, Position = position,
                Radius = radius, CanDisplace = canDisplace,
                DisplacementWeight = canDisplace == true ? Mathf.Max(0.01f, displacementWeight) : 0f,
                PassPartnerId = passPartnerId,
                PredictedVelocity = canDisplace == true ? predictedVelocity : Vector2.zero };
            _maximumRadius = Mathf.Max(_maximumRadius, radius);
            Insert(index, GetCell(position));
            return index;
        }

        public Body GetBody(int index) => _bodies[index];
        public int GetNeighbor(int index) => _neighbors[index];

        public void SetPosition(int index, Vector2 position)
        {
            int cell = GetCell(position);
            if (_cells[index] != cell)
            {
                Remove(index);
                Insert(index, cell);
            }
            _bodies[index].Position = position;
        }

        /// <summary>
        /// 모든 몸을 등록한 뒤 한 번 부른다. 통과 상대를 최대 MaxPassCycleLength 단계까지 따라가 자기 자신으로 돌아오는 몸에 InPassCycle을 켠다.
        /// 서로 막은 둘(A→B→A)과 셋 이상의 고리 교착(A→B→C→A)이 같은 규칙으로 풀린다. 끝이 기다리지 않는 몸으로 끊기는 줄 서기와
        /// 고리에 붙은 꼬리는 고리가 아니라 그대로 기다린다. 통과 상대가 있는 몸은 1초 넘게 막힌 경우뿐이라 수가 적어 선형 탐색으로 충분하다.
        /// </summary>
        public void ResolvePassCycles()
        {
            int partneredCount = 0;
            for (int i = 0; i < Count; i++)
            {
                if (_bodies[i].PassPartnerId != 0)
                    _partnered[partneredCount++] = i;
            }
            for (int p = 0; p < partneredCount; p++)
            {
                int index = _partnered[p];
                int self = _bodies[index].EntityId;
                int current = _bodies[index].PassPartnerId;
                bool inCycle = false;
                for (int hop = 0; hop < MaxPassCycleLength; hop++)
                {
                    if (current == self)
                    {
                        inCycle = true;
                        break;
                    }
                    int next = 0;
                    for (int q = 0; q < partneredCount; q++)
                    {
                        Body candidate = _bodies[_partnered[q]];
                        if (candidate.EntityId == current)
                        {
                            next = candidate.PassPartnerId;
                            break;
                        }
                    }
                    if (next == 0)
                        break;
                    current = next;
                }
                _bodies[index].InPassCycle = inCycle;
            }
        }

        /// <summary>이번 프레임 이동 확정. 이후 이웃의 이동 검사에서 이 개체의 예측 이동을 더 쓰지 않는다.</summary>
        public void CommitMove(int index, Vector2 position)
        {
            SetPosition(index, position);
            _bodies[index].HasMoved = true;
        }

        public void FindNeighbors(int index, Vector2 delta, float padding)
        {
            Body body = _bodies[index];
            FindAround(body.Position, body.Radius, delta, padding, index);
        }

        /// <summary>
        /// 격자에 없는 원(플레이어)이 delta만큼 움직일 때 격자의 몸들에 막히지 않는 만큼만 돌려준다.
        /// 이번 틱 이동을 마친 위치 기준이고 몸들은 밀리지 않는다. 정면으로 막히면 그 몸의 둘레를 따라 미끄러진다.
        /// </summary>
        public Vector2 ClipExternal(Vector2 position, float radius, Vector2 delta, float padding)
        {
            if (delta.sqrMagnitude < 0.00000001f)
                return Vector2.zero;
            Vector2 allowed = ClipExternalOnce(position, radius, delta, padding, out int blocker);
            if (blocker < 0 || allowed.sqrMagnitude >= delta.sqrMagnitude * 0.99f)
                return allowed;
            // 막은 몸 쪽으로 파고드는 성분만 빼고 다시 검사한다. 정면이면 남는 성분이 없어 그 자리에 멈춘다.
            Vector2 offset = position - _bodies[blocker].Position;
            if (offset.sqrMagnitude < 0.00000001f)
                return allowed;
            Vector2 normal = offset.normalized;
            float inward = Vector2.Dot(delta, normal);
            if (inward >= 0f)
                return allowed;
            Vector2 slid = ClipExternalOnce(position, radius, delta - normal * inward, padding, out _);
            Vector2 heading = delta.normalized;
            return Vector2.Dot(slid, heading) > Vector2.Dot(allowed, heading) ? slid : allowed;
        }

        /// <summary>
        /// index가 reach 이내로 움직일 때 부딪힐 수 있는 이웃을 한 번 모아 따로 보관한다.
        /// 한 프레임 이동인 정면·후보 방향 검사(ClipMoveGathered)가 다시 찾지 않고 이 목록을 함께 쓴다.
        /// 앞보기처럼 멀리 보는 검사는 범위가 달라 따로 찾는다 (넓게 모으면 후보마다 검사할 이웃이 늘어 더 느리다).
        /// </summary>
        public void GatherNeighbors(int index, float reach, float padding)
        {
            FindNeighbors(index, Vector2.zero, padding + reach);
            Array.Copy(_neighbors, _gathered, NeighborCount);
            _gatheredCount = NeighborCount;
            _gatheredIndex = index;
            _gatheredReach = reach;
        }

        /// <summary>정지한 이웃 원에 대한 swept-circle. 이동 직후 점유를 갱신하므로 서로 교차할 수 없다.</summary>
        public Vector2 ClipMove(int index, Vector2 delta, float padding)
        {
            if (delta.sqrMagnitude < 0.00000001f)
                return Vector2.zero;
            FindNeighbors(index, delta, padding);
            return ClipNeighbors(_neighbors, NeighborCount, index, delta, padding, 0f, false, out _);
        }

        /// <summary>
        /// GatherNeighbors 목록으로 하는 이번 프레임 실제 이동 검사. 아직 이동하지 않은 이웃은 frameTime 동안 예측 속도로 간다고 본다.
        /// blockerId = 이동을 가장 먼저 막은 이웃의 EntityId (막히지 않았으면 0).
        /// </summary>
        public Vector2 ClipMoveGathered(int index, Vector2 delta, float padding, float frameTime, out int blockerId)
        {
            RequireGathered(index, delta);
            return ClipNeighbors(_gathered, _gatheredCount, index, delta, padding, frameTime, false, out blockerId);
        }

        /// <summary>앞보기 검사. 이번 프레임 이동 여부와 상관없이 모든 이웃이 lookAheadTime 동안 예측 속도로 간다고 본다.</summary>
        public Vector2 ClipLookAhead(int index, Vector2 delta, float padding, float lookAheadTime)
        {
            if (delta.sqrMagnitude < 0.00000001f)
                return Vector2.zero;
            FindNeighbors(index, delta, padding);
            return ClipNeighbors(_neighbors, NeighborCount, index, delta, padding, lookAheadTime, true, out _);
        }

        /// <summary>통과 상대와 아직 맞닿아 있는지. 완전히 떨어질 때까지 통과 상태를 이어 가는 데 쓴다.</summary>
        public bool IsTouchingPassPartner(int index, float padding)
        {
            Body body = _bodies[index];
            if (body.PassPartnerId == 0)
                return false;
            FindNeighbors(index, Vector2.zero, padding);
            for (int i = 0; i < NeighborCount; i++)
            {
                Body other = _bodies[_neighbors[i]];
                if (other.EntityId != body.PassPartnerId)
                    continue;
                if (body.CanPassThrough(other) == false)
                    return false;
                float radius = body.Radius + other.Radius + padding;
                return (body.Position - other.Position).sqrMagnitude <= radius * radius;
            }
            return false;
        }

        /// <summary>모은 목록이 이 개체의 것이고 이동이 모은 거리 안인지 (Editor 전용). 어기면 이웃을 놓쳐 서로 파고든다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void RequireGathered(int index, Vector2 delta)
        {
            if (_gatheredIndex != index || delta.sqrMagnitude > _gatheredReach * _gatheredReach + 0.0001f)
                throw new InvalidOperationException(
                    "[MonsterCrowdGrid] GatherNeighbors must be called for this body with a reach covering the move.");
        }

        /// <summary>주어진 이웃 목록(FindNeighbors 또는 GatherNeighbors 결과)에 대해서만 검사한다.</summary>
        private Vector2 ClipNeighbors(int[] neighbors, int neighborCount, int index, Vector2 delta, float padding,
            float leadTime, bool leadMoved, out int blockerId)
        {
            blockerId = 0;
            if (delta.sqrMagnitude < 0.00000001f)
                return Vector2.zero;
            Body body = _bodies[index];
            float fraction = 1f;
            for (int i = 0; i < neighborCount; i++)
            {
                Body other = _bodies[neighbors[i]];
                CandidateChecks++;
                if (body.CanPassThrough(other) == true)
                    continue;
                Vector2 offset = body.Position - other.Position;
                float radius = body.Radius + other.Radius + padding;
                float limit = SweepFraction(offset, delta, radius);
                // 이웃이 그동안 갈 거리를 감안해 덜 막힐 때만 그 값을 쓴다.
                // 줄지어 따라갈 때 앞 개체의 이동 전 위치에 막히거나 앞보기가 막혀 옆으로 비켜서지 않게 한다.
                if (limit < fraction && leadTime > 0f && (leadMoved == true || other.HasMoved == false)
                    && other.PredictedVelocity.sqrMagnitude > 0.000001f)
                    limit = Mathf.Max(limit, FollowFraction(offset, delta, other.PredictedVelocity * leadTime, radius));
                if (limit < fraction)
                {
                    fraction = limit;
                    blockerId = other.EntityId;
                }
            }
            return delta * fraction;
        }

        /// <summary>
        /// 이웃이 lead만큼 함께 움직일 때 갈 수 있는 비율 (0~1). 떨어져 있으면 상대 이동으로 접촉 시점까지 가고,
        /// 맞닿아 있으면 이웃이 멀어지는 만큼만 따라간다 (느린 앞 개체 뒤에서는 멈추지 않고 속도를 맞춘다).
        /// </summary>
        private static float FollowFraction(Vector2 offset, Vector2 delta, Vector2 lead, float radius)
        {
            if (offset.sqrMagnitude - radius * radius > 0.000001f)
                return SweepFraction(offset, delta - lead, radius);
            float inward = -Vector2.Dot(offset, delta);
            if (inward <= 0.000001f)
                return 1f;
            return Mathf.Clamp01(-Vector2.Dot(offset, lead) / inward);
        }

        /// <summary>offset만큼 떨어진 원이 delta만큼 움직일 때 접촉 전까지 갈 수 있는 비율 (0~1).</summary>
        private static float SweepFraction(Vector2 offset, Vector2 delta, float radius)
        {
            float lengthSqr = delta.sqrMagnitude;
            if (lengthSqr < 0.00000001f)
                return 1f;
            float c = offset.sqrMagnitude - radius * radius;
            float b = Vector2.Dot(offset, delta);
            // 접촉/기존 겹침에서 더 파고드는 이동만 막고, 탈출·접선 이동은 허용한다.
            if (c <= 0.000001f)
                return b < -0.000001f ? 0f : 1f;
            if (b >= 0f)
                return 1f;
            float discriminant = b * b - lengthSqr * c;
            if (discriminant <= 0f)
                return 1f;
            float contact = (-b - Mathf.Sqrt(discriminant)) / lengthSqr;
            return contact >= 0f && contact < 1f ? Mathf.Max(0f, contact - 0.0001f) : 1f;
        }

        private Vector2 ClipExternalOnce(Vector2 position, float radius, Vector2 delta, float padding, out int blocker)
        {
            blocker = -1;
            if (delta.sqrMagnitude < 0.00000001f)
                return Vector2.zero;
            FindAround(position, radius, delta, padding, -1);
            float fraction = 1f;
            for (int i = 0; i < NeighborCount; i++)
            {
                Body other = _bodies[_neighbors[i]];
                CandidateChecks++;
                float limit = SweepFraction(position - other.Position, delta, radius + other.Radius + padding);
                if (limit < fraction)
                {
                    fraction = limit;
                    blocker = _neighbors[i];
                }
            }
            return delta * fraction;
        }

        /// <summary>position에서 delta만큼 움직이는 radius 원과 닿을 수 있는 몸을 모은다. self는 결과에서 뺀다(-1 = 없음).</summary>
        private void FindAround(Vector2 position, float radius, Vector2 delta, float padding, int self)
        {
            NeighborQueries++;
            float extent = radius + _maximumRadius + padding;
            Vector2 min = Vector2.Min(position, position + delta) - Vector2.one * extent;
            Vector2 max = Vector2.Max(position, position + delta) + Vector2.one * extent;
            int minX = Mathf.Max(0, Mathf.FloorToInt(min.x / _cellSize) - _origin.x);
            int maxX = Mathf.Min(_width - 1, Mathf.FloorToInt(max.x / _cellSize) - _origin.x);
            int minY = Mathf.Max(0, Mathf.FloorToInt(min.y / _cellSize) - _origin.y);
            int maxY = Mathf.Min(_width - 1, Mathf.FloorToInt(max.y / _cellSize) - _origin.y);
            NeighborCount = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = y * _width + x;
                    if (_generations[cell] != _generation)
                        continue;
                    Collect(_heads[cell], self, min, max);
                }
            }
            Collect(_overflowHead, self, min, max);
        }

        private void EnsureCapacity(int capacity)
        {
            if (_bodies.Length >= capacity)
                return;
            int nextCapacity = Mathf.Max(512, Mathf.Max(capacity, _bodies.Length * 2));
            Array.Resize(ref _bodies, nextCapacity);
            Array.Resize(ref _next, nextCapacity);
            Array.Resize(ref _previous, nextCapacity);
            Array.Resize(ref _cells, nextCapacity);
            Array.Resize(ref _neighbors, nextCapacity);
            Array.Resize(ref _gathered, nextCapacity);
            Array.Resize(ref _partnered, nextCapacity);
        }

        private int GetCell(Vector2 position)
        {
            int x = Mathf.FloorToInt(position.x / _cellSize) - _origin.x;
            int y = Mathf.FloorToInt(position.y / _cellSize) - _origin.y;
            return x >= 0 && x < _width && y >= 0 && y < _width ? y * _width + x : -1;
        }

        private void Insert(int index, int cell)
        {
            int head;
            if (cell < 0)
            {
                head = _overflowHead;
                _overflowHead = index;
                OverflowCount++;
            }
            else
            {
                head = _generations[cell] == _generation ? _heads[cell] : -1;
                _generations[cell] = _generation;
                _heads[cell] = index;
            }
            _cells[index] = cell;
            _previous[index] = -1;
            _next[index] = head;
            if (head >= 0)
                _previous[head] = index;
        }

        private void Remove(int index)
        {
            int previous = _previous[index];
            int next = _next[index];
            int cell = _cells[index];
            if (previous >= 0)
                _next[previous] = next;
            else if (cell < 0)
                _overflowHead = next;
            else
                _heads[cell] = next;
            if (next >= 0)
                _previous[next] = previous;
            if (cell < 0)
                OverflowCount--;
        }

        private void Collect(int head, int self, Vector2 min, Vector2 max)
        {
            for (int index = head; index >= 0; index = _next[index])
            {
                Vector2 position = _bodies[index].Position;
                if (index != self && position.x >= min.x && position.x <= max.x
                    && position.y >= min.y && position.y <= max.y)
                    _neighbors[NeighborCount++] = index;
            }
        }
    }
}
