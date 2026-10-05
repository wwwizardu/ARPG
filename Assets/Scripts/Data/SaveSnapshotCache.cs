#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json;
#endif

namespace ARPG.Data
{
    /// <summary>소유 매니저의 변경 항목을 불변 저장 DTO로 게시한다. null 원장은 삭제를 뜻한다.</summary>
    public sealed class SaveSnapshotCache<TKey> where TKey : notnull
    {
        private readonly string _name;
        private readonly SaveDtoSnapshot _copier = new();
        private Dictionary<TKey, object?> _snapshots = new();
        private readonly HashSet<TKey> _dirty = new();
        private readonly List<TKey> _dirtyOrder = new();
        private readonly List<TKey> _failed = new();
        private long _generation;
        public long Generation => _generation;
        public bool HasPendingChanges => _dirtyOrder.Count > 0;
#if UNITY_EDITOR
        private Action<TKey>? _dirtyObserver;
        public Action<TKey>? DirtyObserver { get => _dirtyObserver; set => _dirtyObserver = value; }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _flushCount;
        private double _flushTotalMs;
        private double _flushMaxMs;
        public long FlushCount => _flushCount;
        public double FlushAverageMs => _flushCount == 0 ? 0 : _flushTotalMs / _flushCount;
        public double FlushMaxMs => _flushMaxMs;
#endif

        public SaveSnapshotCache(string name) => _name = name;

        public void MarkDirty(TKey key)
        {
#if UNITY_EDITOR
            if (_dirty.Contains(key) == false)
                _dirtyObserver?.Invoke(key);
#endif
            if (_dirty.Add(key) == true)
            {
                _generation++;
                _dirtyOrder.Add(key);
            }
        }

        public void Flush(Func<TKey, object?> getLive, bool recordFrameTiming = false)
        {
            if (_dirtyOrder.Count == 0)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            for (int i = 0; i < _dirtyOrder.Count; i++)
            {
                TKey key = _dirtyOrder[i];
                if (TryCapture(key, getLive, _snapshots) == true)
                    _failed.Remove(key);
            }
            _dirty.Clear();
            _dirtyOrder.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (recordFrameTiming == true)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;
                _flushCount++;
                _flushTotalMs += ms;
                _flushMaxMs = Math.Max(_flushMaxMs, ms);
            }
#endif
        }

        public void Rebuild(IEnumerable<TKey> keys, Func<TKey, object?> getLive)
        {
            _generation++;
            Dictionary<TKey, object?> rebuilt = new();
            HashSet<TKey> current = new();
            foreach (TKey key in keys)
            {
                current.Add(key);
                if (TryCapture(key, getLive, rebuilt) == true)
                    _failed.Remove(key);
            }
            for (int i = _failed.Count - 1; i >= 0; i--)
            {
                if (current.Contains(_failed[i]) == false)
                    _failed.RemoveAt(i);
            }
            _snapshots = rebuilt;
            _dirty.Clear();
            _dirtyOrder.Clear();
        }

        /// <summary>실패 항목만 다시 복사한다. 실패가 남으면 낡은 DTO를 저장하지 않는다.</summary>
        public void PrepareForSave(Func<TKey, object?> getLive)
        {
            Flush(getLive);
            for (int i = _failed.Count - 1; i >= 0; i--)
            {
                if (TryCapture(_failed[i], getLive, _snapshots) == true)
                    _failed.RemoveAt(i);
            }
            if (_failed.Count > 0)
                throw new InvalidOperationException(_name + " save snapshot capture failed: " + string.Join(", ", _failed));
        }

        public object? Get(TKey key)
        {
            if (_failed.Contains(key) == true)
                throw new InvalidOperationException(_name + " save snapshot capture failed: " + key);
            if (_snapshots.TryGetValue(key, out object? snapshot) == false)
                throw new InvalidOperationException(_name + " save snapshot missing: " + key);
            return snapshot;
        }

        public void Reset()
        {
            _generation++;
            _snapshots.Clear();
            _dirty.Clear();
            _dirtyOrder.Clear();
            _failed.Clear();
#if UNITY_EDITOR
            _dirtyObserver = null;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _flushCount = 0;
            _flushTotalMs = 0;
            _flushMaxMs = 0;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>명시적 진단 전용. 다음 정상 표시가 이전 누락을 가리기 전에 검사한다.</summary>
        public bool TryFindUnmarkedChange(TKey key, Func<TKey, object?> getLive, List<string> fields)
        {
            fields.Clear();
            if (_dirty.Contains(key) == true || _failed.Contains(key) == true)
                return false;
            object? live = getLive(key);
            object? fresh = _copier.Capture(live);
            bool exists = _snapshots.TryGetValue(key, out object? cached);
            if (live == null && exists == false)
                return false;
            if (live != null && exists == true && ValuesEqual(fresh, cached) == true)
                return false;
            if (fresh is Dictionary<string, object?> a && cached is Dictionary<string, object?> b)
            {
                HashSet<string> names = new(a.Keys, StringComparer.Ordinal);
                names.UnionWith(b.Keys);
                foreach (string name in names)
                {
                    bool inA = a.TryGetValue(name, out object? valueA);
                    bool inB = b.TryGetValue(name, out object? valueB);
                    if (inA != inB || ValuesEqual(valueA, valueB) == false)
                        fields.Add(name);
                }
            }
            else fields.Add("(contents)");
            if (live == null) _snapshots.Remove(key);
            else _snapshots[key] = fresh;
            _generation++;
            return true;
        }

        public int Validate(IEnumerable<TKey> keys, Func<TKey, object?> getLive)
        {
            Flush(getLive);
            HashSet<TKey> all = new(keys);
            all.UnionWith(_snapshots.Keys);
            all.UnionWith(_failed);
            List<string> fields = new();
            int mismatches = 0;
            foreach (TKey key in all)
            {
                if (_failed.Contains(key) == true || TryFindUnmarkedChange(key, getLive, fields) == true)
                {
                    Debug.LogError($"[{_name}SaveSnapshot] {key}: {string.Join(", ", fields)} differs or capture failed");
                    mismatches++;
                }
            }
            return mismatches;
        }

        public string DescribeState()
        {
            List<object?> entries = new();
            foreach (var pair in _snapshots)
                entries.Add(new object?[] { pair.Key, pair.Value });
            return JsonConvert.SerializeObject(new object?[] { entries, _dirtyOrder, _failed, _generation,
                _flushCount, _flushTotalMs, _flushMaxMs,
#if UNITY_EDITOR
                _dirtyObserver == null ? 0 : _dirtyObserver.GetHashCode(),
#endif
            }, JsonSettings.Default);
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (ReferenceEquals(a, b) == true) return true;
            if (a == null || b == null) return false;
            if (a is Dictionary<string, object?> dictA)
            {
                if (b is not Dictionary<string, object?> dictB || dictA.Count != dictB.Count) return false;
                foreach (var pair in dictA)
                {
                    if (dictB.TryGetValue(pair.Key, out object? value) == false || ValuesEqual(pair.Value, value) == false)
                        return false;
                }
                return true;
            }
            if (a is List<object?> listA)
            {
                if (b is not List<object?> listB || listA.Count != listB.Count) return false;
                for (int i = 0; i < listA.Count; i++)
                {
                    if (ValuesEqual(listA[i], listB[i]) == false) return false;
                }
                return true;
            }
            return a.Equals(b);
        }
#endif

        private bool TryCapture(TKey key, Func<TKey, object?> getLive, Dictionary<TKey, object?> target)
        {
            try
            {
                object? live = getLive(key);
                if (live == null) target.Remove(key);
                else target[key] = _copier.Capture(live);
                return true;
            }
            catch (Exception exception)
            {
                if (_failed.Contains(key) == false)
                {
                    _failed.Add(key);
                    Debug.LogError($"[{_name}SaveSnapshot] {key}: snapshot capture failed. Every save that includes simulation data (shop, duel, dungeon saves too) is blocked until it succeeds.\n{exception}");
                }
                return false;
            }
        }
    }
}
