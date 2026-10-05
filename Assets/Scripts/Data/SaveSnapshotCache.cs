#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json;
#endif

namespace ARPG.Data
{
    /// <summary>소유 원장의 변경 묶음을 반영한다. 배치 lease 동안 DTO 수정 없이 변경만 누적한다.</summary>
    public sealed class SaveSnapshotCache<TKey> where TKey : notnull
    {
        private readonly string _name;
        private readonly SaveDtoSnapshot _copier = new();
        private readonly Func<string, ulong>? _getGroup;
        private State _state = new();
        private long _generation;
        public long Generation => _generation;
        public bool HasPendingChanges => _state.Dirty.Count > 0 || _state.Failed.Count > 0;
        public bool IsFrozen => _state.Readers > 0;
        public IEnumerable<TKey> Keys => _state.Snapshots.Keys;
        public bool Contains(TKey key) => _state.Snapshots.ContainsKey(key);
        public bool TryGetSnapshot(TKey key, out object? snapshot) => _state.Snapshots.TryGetValue(key, out snapshot);
#if UNITY_EDITOR
        public Action<TKey>? DirtyObserver { get; set; }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _flushCount;
        private double _flushTotalMs;
        private double _flushMaxMs;
        private long _groupCaptureCount;
        public long FlushCount => _flushCount;
        public double FlushAverageMs => _flushCount == 0 ? 0 : _flushTotalMs / _flushCount;
        public double FlushMaxMs => _flushMaxMs;
        public long GroupCaptureCount => _groupCaptureCount;
#endif

        public SaveSnapshotCache(string name, Func<string, ulong>? getGroup = null)
        {
            _name = name;
            _getGroup = getGroup;
        }

        public void MarkDirty(TKey key, ulong groups = ulong.MaxValue)
        {
            if (groups == 0) return;
#if UNITY_EDITOR
            // Existing records have a comparison baseline. A first explicit mark initializes a new record.
            // The observer excludes already marked fields, not the entire item.
            if (_state.Snapshots.ContainsKey(key) == true || _state.Failed.ContainsKey(key) == true)
                DirtyObserver?.Invoke(key);
#endif
            _state.Dirty.TryGetValue(key, out ulong existing);
            if ((existing | groups) == existing) return;
            _state.Dirty[key] = existing | groups;
            _generation++;
        }

        public IDisposable Freeze()
        {
            _state.Readers++;
            return new Lease(_state);
        }

        public void Flush(Func<TKey, object?> getLive, bool recordFrameTiming = false)
        {
            if (IsFrozen == true || _state.Dirty.Count == 0) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            var work = _state.Dirty;
            _state.Dirty = new();
            using var entries = work.GetEnumerator();
            for (; entries.MoveNext() == true;)
                TryCapture(entries.Current.Key, entries.Current.Value, getLive);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (recordFrameTiming == true)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _flushCount++;
                _flushTotalMs += ms;
                _flushMaxMs = Math.Max(_flushMaxMs, ms);
            }
#endif
        }

        public void Rebuild(IEnumerable<TKey> keys, Func<TKey, object?> getLive)
        {
            // Includes old keys so deletion and clear are reflected after thaw too.
            HashSet<TKey> all = new(keys);
            all.UnionWith(_state.Snapshots.Keys);
            foreach (TKey key in all) MarkDirty(key);
            Flush(getLive);
        }

        public void PrepareForSave(Func<TKey, object?> getLive)
        {
            if (IsFrozen == true) throw new InvalidOperationException(_name + " snapshot is still being read by a save batch.");
            Flush(getLive);
            var retry = new Dictionary<TKey, ulong>(_state.Failed);
            foreach (var pair in retry) TryCapture(pair.Key, pair.Value, getLive);
            if (_state.Failed.Count > 0)
                throw new InvalidOperationException(_name + " save snapshot capture failed: " + string.Join(", ", _state.Failed.Keys));
        }

        public object? Get(TKey key)
        {
            if (_state.Failed.ContainsKey(key) == true)
                throw new InvalidOperationException(_name + " save snapshot capture failed: " + key);
            if (_state.Snapshots.TryGetValue(key, out object? snapshot) == false)
                throw new InvalidOperationException(_name + " save snapshot missing: " + key);
            return snapshot;
        }

        public void Reset()
        {
            _generation++;
            // Old readers own the old State. Releasing them cannot thaw the new lifetime.
            _state = new();
#if UNITY_EDITOR
            DirtyObserver = null;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _flushCount = 0; _flushTotalMs = 0; _flushMaxMs = 0; _groupCaptureCount = 0;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool TryFindUnmarkedChange(TKey key, Func<TKey, object?> getLive, List<string> fields,
            Func<string, bool>? ignore = null)
        {
            fields.Clear();
            object? live = getLive(key);
            object? fresh = _copier.Capture(live);
            bool exists = _state.Snapshots.TryGetValue(key, out object? cached);
            _state.Dirty.TryGetValue(key, out ulong pending);
            _state.Failed.TryGetValue(key, out ulong failed);
            pending |= failed;
            if (live == null && exists == false) return false;
            if (fresh is Dictionary<string, object?> a && cached is Dictionary<string, object?> b)
            {
                HashSet<string> names = new(a.Keys, StringComparer.Ordinal);
                names.UnionWith(b.Keys);
                foreach (string name in names)
                {
                    if (ignore?.Invoke(name) == true || (pending & (_getGroup?.Invoke(name) ?? ulong.MaxValue)) != 0) continue;
                    bool inA = a.TryGetValue(name, out object? valueA);
                    bool inB = b.TryGetValue(name, out object? valueB);
                    if (inA != inB || ValuesEqual(valueA, valueB) == false) fields.Add(name);
                }
            }
            else if (pending == 0 && ValuesEqual(fresh, cached) == false) fields.Add("(contents)");
            if (fields.Count == 0) return false;
            // Never mutate even from a diagnostic while a worker holds this state.
            if (IsFrozen == false)
            {
                if (live == null) _state.Snapshots.Remove(key);
                else _state.Snapshots[key] = fresh;
            }
            else
            {
                ulong groups = 0;
                for (int i = 0; i < fields.Count; i++) groups |= _getGroup?.Invoke(fields[i]) ?? ulong.MaxValue;
                _state.Dirty[key] = pending | groups;
            }
            _generation++;
            return true;
        }

        public int Validate(IEnumerable<TKey> keys, Func<TKey, object?> getLive, Func<TKey, string, bool>? ignore = null)
        {
            Flush(getLive);
            HashSet<TKey> all = new(keys);
            all.UnionWith(_state.Snapshots.Keys);
            all.UnionWith(_state.Failed.Keys);
            List<string> fields = new();
            int mismatches = 0;
            foreach (TKey key in all)
            {
                if (_state.Failed.ContainsKey(key) == true || TryFindUnmarkedChange(key, getLive, fields,
                    ignore == null ? null : field => ignore(key, field)) == true)
                {
                    Debug.LogError($"[{_name}SaveSnapshot] {key}: {string.Join(", ", fields)} differs or capture failed");
                    mismatches++;
                }
            }
            return mismatches;
        }

        public string DescribeState() => JsonConvert.SerializeObject(new object?[] { _state.Snapshots,
            _state.Dirty, _state.Failed, _generation, _state.Readers, _flushCount, _flushTotalMs, _flushMaxMs, _groupCaptureCount,
#if UNITY_EDITOR
            DirtyObserver == null ? 0 : DirtyObserver.GetHashCode(),
#endif
        }, JsonSettings.Default);

        private static bool ValuesEqual(object? a, object? b)
        {
            if (ReferenceEquals(a, b) == true) return true;
            if (a == null || b == null) return false;
            if (a is Dictionary<string, object?> dictA)
            {
                if (b is not Dictionary<string, object?> dictB || dictA.Count != dictB.Count) return false;
                foreach (var pair in dictA)
                    if (dictB.TryGetValue(pair.Key, out object? value) == false || ValuesEqual(pair.Value, value) == false) return false;
                return true;
            }
            if (a is List<object?> listA)
            {
                if (b is not List<object?> listB || listA.Count != listB.Count) return false;
                for (int i = 0; i < listA.Count; i++) if (ValuesEqual(listA[i], listB[i]) == false) return false;
                return true;
            }
            return a.Equals(b);
        }
#endif

        private void TryCapture(TKey key, ulong groups, Func<TKey, object?> getLive)
        {
            object? live;
            try { live = getLive(key); }
            catch (Exception exception) { Fail(key, groups, exception); return; }
            if (live == null) { _state.Snapshots.Remove(key); _state.Failed.Remove(key); return; }
            if (_getGroup == null || _state.Snapshots.TryGetValue(key, out object? cached) == false ||
                cached is not Dictionary<string, object?> fields)
            {
                try { _state.Snapshots[key] = _copier.Capture(live); _state.Failed.Remove(key); }
                catch (Exception exception) { Fail(key, groups, exception); }
                return;
            }
            // Each bit is its own atomic preparation/application. A failed group blocks saving.
            ulong allGroups;
            try { allGroups = _copier.GetGroupMask(live, _getGroup); }
            catch (Exception exception) { Fail(key, groups, exception); return; }
            groups &= allGroups;
            // A full mark is one plain capture instead of one contract pass per group. On failure, fall back to
            // per-group passes so the other groups still update and only the failing group blocks saving.
            if (groups != 0 && groups == allGroups)
            {
                try { _state.Snapshots[key] = _copier.Capture(live); _state.Failed.Remove(key); return; }
                catch (Exception) { }
            }
            for (int bit = 0; bit < 64; bit++)
            {
                ulong group = 1UL << bit;
                if ((groups & group) == 0) continue;
                try
                {
                    _copier.UpdateGroup(live, fields, group, _getGroup);
                    if (_state.Failed.TryGetValue(key, out ulong failed) == true)
                    {
                        failed &= ~group;
                        if (failed == 0) _state.Failed.Remove(key);
                        else _state.Failed[key] = failed;
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _groupCaptureCount++;
#endif
                }
                catch (Exception exception) { Fail(key, group, exception); }
            }
        }

        private void Fail(TKey key, ulong groups, Exception exception)
        {
            _state.Failed.TryGetValue(key, out ulong existing);
            _state.Failed[key] = existing | groups;
            if (existing == 0) Debug.LogError($"[{_name}SaveSnapshot] {key}: snapshot capture failed; save blocked until retry.\n{exception}");
        }

        private sealed class State
        {
            public readonly Dictionary<TKey, object?> Snapshots = new();
            public Dictionary<TKey, ulong> Dirty = new();
            public readonly Dictionary<TKey, ulong> Failed = new();
            public int Readers;
        }

        private sealed class Lease : IDisposable
        {
            private State? _state;
            public Lease(State state) => _state = state;
            public void Dispose()
            {
                if (_state == null) return;
                _state.Readers--;
                _state = null;
            }
        }
    }
}
