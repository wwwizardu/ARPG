#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Data;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json;
#endif

namespace ARPG.Village
{
    public partial class VillageManager
    {
        // 전환 스위치. false면 기존 저장 캡처 경로로 돌아간다.
        public static readonly bool SaveSnapshotEnabled = true;

        // 게시한 DTO는 절대 수정하지 않는다. 다음 변경은 새 DTO로 교체한다.
        private Dictionary<int, object?> _saveSnapshot = new();
        private HashSet<int> _dirtyVillageIds = new();
        private List<int> _dirtyVillageOrder = new();
        // 복사에 실패한 Village. 이 목록이 비기 전에는 Village 저장을 조립하지 않는다.
        private List<int> _snapshotFailedIds = new();
        private readonly SaveDtoSnapshot _snapshotCopier = new();
        private long _saveSnapshotGeneration;
        public long SaveSnapshotGeneration => _saveSnapshotGeneration;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _snapshotFlushCount;
        private double _snapshotFlushTotalMs;
        private double _snapshotFlushMaxMs;

        public long SnapshotFlushCount => _snapshotFlushCount;
        public double SnapshotFlushAverageMs => _snapshotFlushCount == 0 ? 0 : _snapshotFlushTotalMs / _snapshotFlushCount;
        public double SnapshotFlushMaxMs => _snapshotFlushMaxMs;
#endif

#if UNITY_EDITOR
        // 명시적 복제 진단에서만 설정한다. 다음 표시가 이전의 미표시 변경을 가리기 전에 검사한다.
        private Action<int>? _snapshotDirtyObserver;
#endif

        public void MarkSaveDirty(int villageId)
        {
            if (SaveSnapshotEnabled == false)
                return;
#if UNITY_EDITOR
            if (_dirtyVillageIds.Contains(villageId) == false)
                _snapshotDirtyObserver?.Invoke(villageId);
#endif
            if (_dirtyVillageIds.Add(villageId) == true)
            {
                _saveSnapshotGeneration++;
                _dirtyVillageOrder.Add(villageId);
            }
        }

        public void FlushSaveSnapshot(bool recordFrameTiming = false)
        {
            if (SaveSnapshotEnabled == false || _dirtyVillageOrder.Count == 0)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            for (int i = 0; i < _dirtyVillageOrder.Count; i++)
            {
                int id = _dirtyVillageOrder[i];
                if (_villages.TryGetValue(id, out VillageData? live) == true)
                {
                    if (TryCaptureSaveSnapshot(id, live, _saveSnapshot) == true)
                        _snapshotFailedIds.Remove(id);
                }
                else
                {
                    _saveSnapshot.Remove(id);
                    _snapshotFailedIds.Remove(id);
                }
            }
            _dirtyVillageIds.Clear();
            _dirtyVillageOrder.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (recordFrameTiming == true)
            {
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;
                _snapshotFlushCount++;
                _snapshotFlushTotalMs += elapsedMs;
                _snapshotFlushMaxMs = Math.Max(_snapshotFlushMaxMs, elapsedMs);
            }
#endif
        }

        public void RebuildSaveSnapshot()
        {
            _saveSnapshotGeneration++;
            Dictionary<int, object?> rebuilt = new(_villages.Count);
            foreach (KeyValuePair<int, VillageData> pair in _villages)
            {
                if (TryCaptureSaveSnapshot(pair.Key, pair.Value, rebuilt) == true)
                    _snapshotFailedIds.Remove(pair.Key);
            }
            for (int i = _snapshotFailedIds.Count - 1; i >= 0; i--)
            {
                if (_villages.ContainsKey(_snapshotFailedIds[i]) == false)
                    _snapshotFailedIds.RemoveAt(i);
            }
            _saveSnapshot = rebuilt;
            _dirtyVillageIds.Clear();
            _dirtyVillageOrder.Clear();
        }

        /// <summary>새 루트와 목록만 만들고, 게시한 마을 DTO는 수정 없이 공유한다.</summary>
        public object? CaptureSaveSnapshotRoot()
        {
            ThrowIfSaveSnapshotIncomplete();
            var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new VillageSaveRoot())!;
            var villages = (List<object?>)root[nameof(VillageSaveRoot.Villages)]!;
            // 기존 Save()의 목록 순서와 같아야 JSON 바이트도 동일하다.
            for (int i = 0; i < _villageList.Count; i++)
                villages.Add(_saveSnapshot[_villageList[i].VillageId]);
            return root;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>비싼 전수 비교는 명시적 진단에서만 호출한다. 누락은 Village와 최상위 저장 항목별로 보고한다.</summary>
        public int ValidateSaveSnapshot()
        {
            FlushSaveSnapshot();
            int mismatches = 0;
            foreach (KeyValuePair<int, VillageData> pair in _villages)
            {
                if (_snapshotFailedIds.Contains(pair.Key) == true)
                {
                    Debug.LogError($"[VillageSaveSnapshot] Village {pair.Key}: snapshot capture failed");
                    mismatches++;
                    continue;
                }
                var fresh = (Dictionary<string, object?>)_snapshotCopier.Capture(pair.Value)!;
                if (_saveSnapshot.TryGetValue(pair.Key, out object? cached) == false ||
                    cached is not Dictionary<string, object?> snapshot)
                {
                    Debug.LogError($"[VillageSaveSnapshot] Village {pair.Key}: snapshot missing");
                    mismatches++;
                    continue;
                }
                HashSet<string> fields = new(fresh.Keys, StringComparer.Ordinal);
                fields.UnionWith(snapshot.Keys);
                foreach (string field in fields)
                {
                    bool hasFresh = fresh.TryGetValue(field, out object? freshValue);
                    bool hasSnapshot = snapshot.TryGetValue(field, out object? snapshotValue);
                    if (hasFresh == hasSnapshot && JsonConvert.SerializeObject(freshValue, JsonSettings.Default) ==
                        JsonConvert.SerializeObject(snapshotValue, JsonSettings.Default))
                        continue;
                    Debug.LogError($"[VillageSaveSnapshot] Village {pair.Key}: {field} differs");
                    mismatches++;
                }
            }
            foreach (int id in _saveSnapshot.Keys)
            {
                if (_villages.ContainsKey(id) == true) continue;
                Debug.LogError($"[VillageSaveSnapshot] Village {id}: stale snapshot");
                mismatches++;
            }
            return mismatches;
        }

        /// <summary>
        /// 진단 전용. 변경 표시 없이 바뀐 Village면 달라진 최상위 저장 항목을 채우고 true를 돌려준다.
        /// 같은 누락을 한 번만 세도록 스냅샷을 지금 데이터로 맞춘다. 표시된 Village는 곧 정리되므로 보지 않는다.
        /// </summary>
        public bool TryFindUnmarkedSaveChange(int villageId, List<string> changedFields)
        {
            changedFields.Clear();
            if (SaveSnapshotEnabled == false || _dirtyVillageIds.Contains(villageId) == true ||
                _snapshotFailedIds.Contains(villageId) == true ||
                _villages.TryGetValue(villageId, out VillageData? live) == false)
                return false;

            object? fresh = _snapshotCopier.Capture(live);
            _saveSnapshot.TryGetValue(villageId, out object? cached);
            if (fresh is Dictionary<string, object?> freshFields && cached is Dictionary<string, object?> cachedFields)
            {
                foreach (KeyValuePair<string, object?> pair in freshFields)
                {
                    if (cachedFields.TryGetValue(pair.Key, out object? old) == false ||
                        SnapshotValueEquals(pair.Value, old) == false)
                        changedFields.Add(pair.Key);
                }
                foreach (KeyValuePair<string, object?> pair in cachedFields)
                {
                    if (freshFields.ContainsKey(pair.Key) == false)
                        changedFields.Add(pair.Key);
                }
            }
            else
            {
                changedFields.Add("(snapshot missing)");
            }
            if (changedFields.Count == 0)
                return false;
            _saveSnapshot[villageId] = fresh;
            _saveSnapshotGeneration++;
            return true;
        }

        // 복사기 출력(문자열 키 사전·리스트·원시값)끼리 내용을 비교한다. JSON 직렬화보다 훨씬 싸다.
        private static bool SnapshotValueEquals(object? a, object? b)
        {
            if (ReferenceEquals(a, b) == true)
                return true;
            if (a == null || b == null)
                return false;
            if (a is Dictionary<string, object?> dictA)
            {
                if (b is not Dictionary<string, object?> dictB || dictA.Count != dictB.Count)
                    return false;
                foreach (KeyValuePair<string, object?> pair in dictA)
                {
                    if (dictB.TryGetValue(pair.Key, out object? other) == false ||
                        SnapshotValueEquals(pair.Value, other) == false)
                        return false;
                }
                return true;
            }
            if (a is List<object?> listA)
            {
                if (b is not List<object?> listB || listA.Count != listB.Count)
                    return false;
                for (int i = 0; i < listA.Count; i++)
                {
                    if (SnapshotValueEquals(listA[i], listB[i]) == false)
                        return false;
                }
                return true;
            }
            return a.Equals(b);
        }
#endif

        /// <summary>Village 하나를 복사한다. 실패해도 예외를 밖으로 내보내지 않아 다른 Village의 복사는 계속된다.</summary>
        private bool TryCaptureSaveSnapshot(int id, VillageData live, Dictionary<int, object?> target)
        {
            try
            {
                target[id] = _snapshotCopier.Capture(live);
                return true;
            }
            catch (Exception ex)
            {
                // 같은 Village가 계속 실패해도 로그는 처음 한 번만 남긴다.
                if (_snapshotFailedIds.Contains(id) == false)
                {
                    _snapshotFailedIds.Add(id);
                    Debug.LogError($"[VillageSaveSnapshot] Village {id}: snapshot capture failed. Every save that includes simulation data (shop, duel, dungeon saves too) is blocked until it succeeds.\n{ex}");
                }
                return false;
            }
        }

        /// <summary>복사에 실패했던 Village를 다시 복사해 본다. 그래도 실패하면 낡은 데이터를 저장하지 않도록 저장을 중단시킨다.</summary>
        private void ThrowIfSaveSnapshotIncomplete()
        {
            for (int i = _snapshotFailedIds.Count - 1; i >= 0; i--)
            {
                int id = _snapshotFailedIds[i];
                if (_villages.TryGetValue(id, out VillageData? live) == false)
                {
                    _saveSnapshot.Remove(id);
                    _snapshotFailedIds.RemoveAt(i);
                }
                else if (TryCaptureSaveSnapshot(id, live, _saveSnapshot) == true)
                {
                    _snapshotFailedIds.RemoveAt(i);
                }
            }
            if (_snapshotFailedIds.Count > 0)
                throw new InvalidOperationException("Village save snapshot capture failed: " + string.Join(", ", _snapshotFailedIds));
        }

        private void ResetSaveSnapshot()
        {
            _saveSnapshotGeneration++;
            _saveSnapshot.Clear();
            _dirtyVillageIds.Clear();
            _dirtyVillageOrder.Clear();
            _snapshotFailedIds.Clear();
#if UNITY_EDITOR
            _snapshotDirtyObserver = null;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _snapshotFlushCount = 0;
            _snapshotFlushTotalMs = 0;
            _snapshotFlushMaxMs = 0;
#endif
        }
    }
}
