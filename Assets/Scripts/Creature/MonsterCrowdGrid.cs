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
        }

        private int[] _heads = Array.Empty<int>();
        private uint[] _generations = Array.Empty<uint>();
        private Body[] _bodies = Array.Empty<Body>();
        private int[] _next = Array.Empty<int>();
        private int[] _previous = Array.Empty<int>();
        private int[] _cells = Array.Empty<int>();
        private int[] _neighbors = Array.Empty<int>();
        private int _width;
        private float _cellSize;
        private Vector2Int _origin;
        private uint _generation;
        private int _overflowHead = -1;
        private float _maximumRadius;

        public int Count { get; private set; }
        public int NeighborCount { get; private set; }
        public int OverflowCount { get; private set; }
        public int CandidateChecks { get; private set; }

        public void Initialize(int width, float cellSize, int capacity = 512)
        {
            width = Mathf.Clamp(width, 8, 256);
            cellSize = Mathf.Max(0.25f, cellSize);
            if (_width != width || _heads.Length == 0)
            {
                _heads = new int[width * width];
                _generations = new uint[width * width];
                _generation = 0;
            }
            _width = width;
            _cellSize = cellSize;
            EnsureCapacity(capacity);
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
            _maximumRadius = 0f;
            _overflowHead = -1;
        }

        public void Reset()
        {
            Count = 0;
            NeighborCount = 0;
            OverflowCount = 0;
            CandidateChecks = 0;
            _overflowHead = -1;
            _maximumRadius = 0f;
            // 다음 Begin이 세대를 바꾸므로 셀과 개체 배열을 지울 필요가 없다.
        }

        public int Add(int entityId, Vector2 position, float radius, bool canDisplace, float displacementWeight = 1f)
        {
            EnsureCapacity(Count + 1);
            int index = Count++;
            _bodies[index] = new Body { EntityId = entityId, Position = position,
                Radius = radius, CanDisplace = canDisplace,
                DisplacementWeight = canDisplace == true ? Mathf.Max(0.01f, displacementWeight) : 0f };
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

        public void FindNeighbors(int index, Vector2 delta, float padding)
        {
            Body body = _bodies[index];
            float extent = body.Radius + _maximumRadius + padding;
            Vector2 min = Vector2.Min(body.Position, body.Position + delta) - Vector2.one * extent;
            Vector2 max = Vector2.Max(body.Position, body.Position + delta) + Vector2.one * extent;
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
                    Collect(_heads[cell], index, min, max);
                }
            }
            Collect(_overflowHead, index, min, max);
        }

        /// <summary>정지한 이웃 원에 대한 swept-circle. 이동 직후 점유를 갱신하므로 서로 교차할 수 없다.</summary>
        public Vector2 ClipMove(int index, Vector2 delta, float padding)
        {
            float lengthSqr = delta.sqrMagnitude;
            if (lengthSqr < 0.00000001f)
                return Vector2.zero;
            FindNeighbors(index, delta, padding);
            Body body = _bodies[index];
            float fraction = 1f;
            for (int i = 0; i < NeighborCount; i++)
            {
                Body other = _bodies[_neighbors[i]];
                CandidateChecks++;
                Vector2 offset = body.Position - other.Position;
                float radius = body.Radius + other.Radius + padding;
                float c = offset.sqrMagnitude - radius * radius;
                float b = Vector2.Dot(offset, delta);
                if (c <= 0.000001f)
                {
                    // 접촉/기존 겹침에서 더 파고드는 이동만 막고, 탈출·접선 이동은 허용한다.
                    if (b < -0.000001f)
                        fraction = 0f;
                    continue;
                }
                if (b >= 0f)
                    continue;
                float discriminant = b * b - lengthSqr * c;
                if (discriminant <= 0f)
                    continue;
                float contact = (-b - Mathf.Sqrt(discriminant)) / lengthSqr;
                if (contact >= 0f && contact < fraction)
                    fraction = Mathf.Max(0f, contact - 0.0001f);
            }
            return delta * fraction;
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
