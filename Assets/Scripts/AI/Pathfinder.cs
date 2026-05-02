using System.Collections.Generic;
using UnityEngine;

namespace ARPG.AI
{
    /// <summary>
    /// 타일 기반 A* 길찾기. 8방향 이동, Octile 휴리스틱, 대각선 corner-cutting 차단.
    /// 비활성 청크는 통행 불가로 처리. 결과 경로는 시작 타일 다음부터 goal까지의 List&lt;Vector2Int&gt;로 outPath에 채움.
    /// </summary>
    public static class Pathfinder
    {
        public const int MaxNodesExpanded = 512;
        public const int MaxPathLength = 64;

        // 1 unit = 1 tile, 청크는 8x8
        private const int CHUNK_SIZE = 8;

        // 대각선 비용 (√2)
        private const float DIAG_COST = 1.41421356f;

        // 재사용 버퍼 (call마다 Clear) — GC 회피
        private static readonly Dictionary<Vector2Int, float> _gScore = new();
        private static readonly Dictionary<Vector2Int, Vector2Int> _cameFrom = new();
        private static readonly HashSet<Vector2Int> _closed = new();
        private static readonly MinHeap _open = new();

        // 8방향 이웃 오프셋 (직교 4 + 대각선 4)
        private static readonly Vector2Int[] _orthoOffsets =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private static readonly Vector2Int[] _diagOffsets =
        {
            new Vector2Int(1, 1), new Vector2Int(-1, 1),
            new Vector2Int(1, -1), new Vector2Int(-1, -1),
        };

        /// <summary>
        /// start에서 goal까지 경로 탐색. 성공 시 outPath에 [start 다음 타일 ... goal] 채움.
        /// 실패 시 false 반환 + LogWarning. 시작 타일은 항상 통행 가능으로 간주 (캐릭터가 그 위에 있음).
        /// </summary>
        public static bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath)
        {
            outPath.Clear();

            if (start == goal)
                return true;

            // 시작/목표 청크 활성 여부 검사
            if (IsChunkActiveForTile(start) == false)
            {
                Debug.LogWarning($"[Pathfinder] Start tile {start} in inactive chunk");
                return false;
            }
            if (IsChunkActiveForTile(goal) == false)
            {
                Debug.LogWarning($"[Pathfinder] Goal tile {goal} in inactive chunk");
                return false;
            }
            // 목표 타일 자체가 막혀있으면 실패 (시작 타일은 캐릭터 위치이므로 검사 안 함)
            if (AR.s.Map.IsTileBlocked(goal.x, goal.y) == true)
            {
                Debug.LogWarning($"[Pathfinder] Goal tile {goal} is blocked");
                return false;
            }

            _gScore.Clear();
            _cameFrom.Clear();
            _closed.Clear();
            _open.Clear();

            _gScore[start] = 0f;
            _open.Push(start, OctileHeuristic(start, goal));

            int expanded = 0;
            while (_open.Count > 0 && expanded < MaxNodesExpanded)
            {
                Vector2Int current = _open.Pop();
                if (_closed.Contains(current) == true)
                    continue; // stale heap entry

                if (current == goal)
                {
                    return ReconstructPath(start, goal, outPath);
                }

                _closed.Add(current);
                expanded++;

                ExpandNeighbors(current, goal);
            }

            if (expanded >= MaxNodesExpanded)
                Debug.LogWarning($"[Pathfinder] Node expansion limit ({MaxNodesExpanded}) reached: {start} → {goal}");
            else
                Debug.LogWarning($"[Pathfinder] No path: {start} → {goal}");
            return false;
        }

        private static void ExpandNeighbors(Vector2Int current, Vector2Int goal)
        {
            // 직교 이웃
            for (int i = 0; i < _orthoOffsets.Length; i++)
            {
                Vector2Int next = current + _orthoOffsets[i];
                if (IsPassable(next) == false) continue;

                TryRelax(current, next, 1f, goal);
            }

            // 대각선 이웃 — 인접 두 직교 타일 모두 통행 가능해야 함 (corner cutting 차단)
            for (int i = 0; i < _diagOffsets.Length; i++)
            {
                Vector2Int off = _diagOffsets[i];
                Vector2Int next = current + off;
                if (IsPassable(next) == false) continue;

                Vector2Int orthoX = current + new Vector2Int(off.x, 0);
                Vector2Int orthoY = current + new Vector2Int(0, off.y);
                if (IsPassable(orthoX) == false || IsPassable(orthoY) == false) continue;

                TryRelax(current, next, DIAG_COST, goal);
            }
        }

        private static void TryRelax(Vector2Int from, Vector2Int to, float stepCost, Vector2Int goal)
        {
            if (_closed.Contains(to) == true) return;

            float tentative = _gScore[from] + stepCost;
            if (_gScore.TryGetValue(to, out float existing) == true && tentative >= existing)
                return;

            _gScore[to] = tentative;
            _cameFrom[to] = from;
            float f = tentative + OctileHeuristic(to, goal);
            _open.Push(to, f);
        }

        private static bool IsPassable(Vector2Int tile)
        {
            if (IsChunkActiveForTile(tile) == false) return false;
            if (AR.s.Map.IsTileBlocked(tile.x, tile.y) == true) return false;
            return true;
        }

        private static bool IsChunkActiveForTile(Vector2Int tile)
        {
            int chunkX = Mathf.FloorToInt((float)tile.x / CHUNK_SIZE);
            int chunkY = Mathf.FloorToInt((float)tile.y / CHUNK_SIZE);
            return AR.s.Map.IsChunkActive(new Vector2Int(chunkX, chunkY));
        }

        private static float OctileHeuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            int min = Mathf.Min(dx, dy);
            int max = Mathf.Max(dx, dy);
            return max + (DIAG_COST - 1f) * min;
        }

        private static bool ReconstructPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath)
        {
            // goal부터 cameFrom 따라 start 직전까지 거꾸로 수집 → 역순으로 outPath에 push
            // 시작 타일은 outPath에 포함하지 않음 (엔티티가 이미 거기 있음)
            Vector2Int cursor = goal;
            int safety = MaxPathLength + 1;
            while (cursor != start && safety > 0)
            {
                outPath.Add(cursor);
                if (_cameFrom.TryGetValue(cursor, out Vector2Int prev) == false)
                {
                    Debug.LogError($"[Pathfinder] Path reconstruction broken at {cursor}");
                    outPath.Clear();
                    return false;
                }
                cursor = prev;
                safety--;
            }

            if (safety <= 0)
            {
                Debug.LogWarning($"[Pathfinder] Path length exceeded {MaxPathLength}, truncating");
                outPath.Clear();
                return false;
            }

            // 역순 (goal→start) → 정순 (start→goal)으로 뒤집기
            outPath.Reverse();
            return outPath.Count > 0;
        }

        // 단순 binary min-heap. 키는 우선순위(f), 값은 타일 좌표.
        // Decrease-key 미지원 → "lazy deletion": 더 작은 f로 다시 push, pop 시 closed 검사로 stale 스킵.
        private class MinHeap
        {
            private readonly List<Entry> _items = new();

            public int Count => _items.Count;
            public void Clear() => _items.Clear();

            public void Push(Vector2Int tile, float priority)
            {
                _items.Add(new Entry { Tile = tile, Priority = priority });
                SiftUp(_items.Count - 1);
            }

            public Vector2Int Pop()
            {
                Vector2Int result = _items[0].Tile;
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                if (_items.Count > 0) SiftDown(0);
                return result;
            }

            private void SiftUp(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_items[i].Priority >= _items[parent].Priority) break;
                    (_items[i], _items[parent]) = (_items[parent], _items[i]);
                    i = parent;
                }
            }

            private void SiftDown(int i)
            {
                int n = _items.Count;
                while (true)
                {
                    int left = (i << 1) + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < n && _items[left].Priority < _items[smallest].Priority) smallest = left;
                    if (right < n && _items[right].Priority < _items[smallest].Priority) smallest = right;
                    if (smallest == i) break;
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }
            }

            private struct Entry
            {
                public Vector2Int Tile;
                public float Priority;
            }
        }
    }
}
