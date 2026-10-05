#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using ARPG.Data;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Newtonsoft.Json;
#endif

namespace ARPG.Npc
{
    public partial class NpcManager
    {
        // 전환 스위치. false면 기존 저장 캡처 경로로 돌아간다.
        public static readonly bool SaveSnapshotEnabled = true;

        // 게시한 DTO는 절대 수정하지 않는다. 다음 변경은 새 DTO로 교체한다.
        private Dictionary<int, object?> _saveSnapshot = new();
        private HashSet<int> _dirtyNpcIds = new();
        private List<int> _dirtyNpcOrder = new();
        // 복사에 실패한 NPC. 이 목록이 비기 전에는 NPC 저장을 조립하지 않는다.
        private List<int> _snapshotFailedIds = new();
        private readonly SaveDtoSnapshot _snapshotCopier = new();
        // 프레임 정리 목록과 별개인 세대. 저장 성공은 DataManager가 캡처했던 값만 확정한다.
        private long[] _saveShardGenerations = new long[DataManager.NPC_SHARD_COUNT];
        public long GetSaveShardGeneration(int shardIndex) => _saveShardGenerations[shardIndex];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _snapshotFlushCount;
        private double _snapshotFlushTotalMs;
        private double _snapshotFlushMaxMs;

        public long SnapshotFlushCount => _snapshotFlushCount;
        public double SnapshotFlushAverageMs => _snapshotFlushCount == 0 ? 0 : _snapshotFlushTotalMs / _snapshotFlushCount;
        public double SnapshotFlushMaxMs => _snapshotFlushMaxMs;
#endif

        public void MarkSaveDirty(int entityId)
        {
            if (SaveSnapshotEnabled == true && _dirtyNpcIds.Add(entityId) == true)
            {
                _saveShardGenerations[DataManager.GetNpcShardIndex(entityId)]++;
                _dirtyNpcOrder.Add(entityId);
            }
        }

        public void FlushSaveSnapshot(bool recordFrameTiming = false)
        {
            if (SaveSnapshotEnabled == false || _dirtyNpcOrder.Count == 0)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            for (int i = 0; i < _dirtyNpcOrder.Count; i++)
            {
                int id = _dirtyNpcOrder[i];
                if (_npcSaveDict.TryGetValue(id, out NpcSaveData? live) == true)
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
            _dirtyNpcIds.Clear();
            _dirtyNpcOrder.Clear();
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
            for (int i = 0; i < _saveShardGenerations.Length; i++) _saveShardGenerations[i]++;
            Dictionary<int, object?> rebuilt = new(_npcSaveDict.Count);
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
            {
                if (TryCaptureSaveSnapshot(pair.Key, pair.Value, rebuilt) == true)
                    _snapshotFailedIds.Remove(pair.Key);
            }
            for (int i = _snapshotFailedIds.Count - 1; i >= 0; i--)
            {
                if (_npcSaveDict.ContainsKey(_snapshotFailedIds[i]) == false)
                    _snapshotFailedIds.RemoveAt(i);
            }
            _saveSnapshot = rebuilt;
            _dirtyNpcIds.Clear();
            _dirtyNpcOrder.Clear();
        }

        /// <summary>호출 전에 Flush/Rebuild한다. 루트만 복제하고 게시한 NPC DTO를 공유한다.</summary>
        public object?[] CaptureSaveSnapshotShards()
        {
            ThrowIfSaveSnapshotIncomplete();
            int shardCount = DataManager.NPC_SHARD_COUNT;
            object?[] roots = new object?[shardCount];
            Dictionary<string, object?>[] containers = new Dictionary<string, object?>[shardCount];
            for (int i = 0; i < shardCount; i++)
            {
                var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new NpcShardSaveRoot { ShardIndex = i })!;
                roots[i] = root;
                containers[i] = (Dictionary<string, object?>)root[nameof(NpcShardSaveRoot.Npcs)]!;
            }
            // SplitNpcShards와 같은 분배 함수·키 순서. 반환 후에는 이 컨테이너도 수정하지 않는다.
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
                containers[DataManager.GetNpcShardIndex(pair.Key)].Add(pair.Key.ToString(CultureInfo.InvariantCulture), _saveSnapshot[pair.Key]);
            return roots;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>비싼 전수 비교는 명시적 진단에서만 호출한다. 누락은 NPC와 최상위 저장 항목별로 보고한다.</summary>
        public int ValidateSaveSnapshot()
        {
            FlushSaveSnapshot();
            int mismatches = 0;
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
            {
                if (_snapshotFailedIds.Contains(pair.Key) == true)
                {
                    Debug.LogError($"[NpcSaveSnapshot] NPC {pair.Key}: snapshot capture failed");
                    mismatches++;
                    continue;
                }
                var fresh = (Dictionary<string, object?>)_snapshotCopier.Capture(pair.Value)!;
                if (_saveSnapshot.TryGetValue(pair.Key, out object? cached) == false ||
                    cached is not Dictionary<string, object?> snapshot)
                {
                    Debug.LogError($"[NpcSaveSnapshot] NPC {pair.Key}: snapshot missing");
                    mismatches++;
                    continue;
                }
                HashSet<string> fields = new(fresh.Keys, StringComparer.Ordinal);
                fields.UnionWith(snapshot.Keys);
                bool positionDerived = IsSavePositionDerived(pair.Key);
                foreach (string field in fields)
                {
                    if (positionDerived == true && IsPositionField(field) == true)
                        continue;
                    bool hasFresh = fresh.TryGetValue(field, out object? freshValue);
                    bool hasSnapshot = snapshot.TryGetValue(field, out object? snapshotValue);
                    if (hasFresh == hasSnapshot && JsonConvert.SerializeObject(freshValue, JsonSettings.Default) ==
                        JsonConvert.SerializeObject(snapshotValue, JsonSettings.Default))
                        continue;
                    Debug.LogError($"[NpcSaveSnapshot] NPC {pair.Key}: {field} differs");
                    mismatches++;
                }
            }
            foreach (int id in _saveSnapshot.Keys)
            {
                if (_npcSaveDict.ContainsKey(id) == true) continue;
                Debug.LogError($"[NpcSaveSnapshot] NPC {id}: stale snapshot");
                mismatches++;
            }
            return mismatches;
        }

        /// <summary>
        /// 진단 전용. 변경 표시 없이 바뀐 NPC면 달라진 최상위 저장 항목을 채우고 true를 돌려준다.
        /// 같은 누락을 한 번만 세도록 스냅샷을 지금 데이터로 맞춘다. 표시된 NPC는 곧 정리되므로 보지 않는다.
        /// </summary>
        public bool TryFindUnmarkedSaveChange(int entityId, List<string> changedFields)
        {
            changedFields.Clear();
            if (SaveSnapshotEnabled == false || _dirtyNpcIds.Contains(entityId) == true ||
                _snapshotFailedIds.Contains(entityId) == true ||
                _npcSaveDict.TryGetValue(entityId, out NpcSaveData? live) == false)
                return false;

            object? fresh = _snapshotCopier.Capture(live);
            _saveSnapshot.TryGetValue(entityId, out object? cached);
            if (fresh is Dictionary<string, object?> freshFields && cached is Dictionary<string, object?> cachedFields)
            {
                // 이동 중 위치는 저장하지 않는 계산값이라 비교하지 않는다 (S13).
                bool positionDerived = IsSavePositionDerived(entityId);
                foreach (KeyValuePair<string, object?> pair in freshFields)
                {
                    if (positionDerived == true && IsPositionField(pair.Key) == true)
                        continue;
                    if (cachedFields.TryGetValue(pair.Key, out object? old) == false ||
                        SnapshotValueEquals(pair.Value, old) == false)
                        changedFields.Add(pair.Key);
                }
                foreach (KeyValuePair<string, object?> pair in cachedFields)
                {
                    if (positionDerived == true && IsPositionField(pair.Key) == true)
                        continue;
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
            _saveSnapshot[entityId] = fresh;
            _saveShardGenerations[DataManager.GetNpcShardIndex(entityId)]++;
            return true;
        }

        private static bool IsPositionField(string field)
        {
            return field == nameof(NpcSaveData.PositionX) || field == nameof(NpcSaveData.PositionY);
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

        /// <summary>NPC 하나를 복사한다. 실패해도 예외를 밖으로 내보내지 않아 다른 NPC의 복사는 계속된다.</summary>
        private bool TryCaptureSaveSnapshot(int id, NpcSaveData live, Dictionary<int, object?> target)
        {
            try
            {
                target[id] = _snapshotCopier.Capture(live);
                return true;
            }
            catch (Exception ex)
            {
                // 같은 NPC가 계속 실패해도 로그는 처음 한 번만 남긴다.
                if (_snapshotFailedIds.Contains(id) == false)
                {
                    _snapshotFailedIds.Add(id);
                    Debug.LogError($"[NpcSaveSnapshot] NPC {id}: snapshot capture failed. Every save that includes simulation data (shop, duel, dungeon saves too) is blocked until it succeeds.\n{ex}");
                }
                return false;
            }
        }

        /// <summary>복사에 실패했던 NPC를 다시 복사해 본다. 그래도 실패하면 낡은 데이터를 저장하지 않도록 저장을 중단시킨다.</summary>
        private void ThrowIfSaveSnapshotIncomplete()
        {
            for (int i = _snapshotFailedIds.Count - 1; i >= 0; i--)
            {
                int id = _snapshotFailedIds[i];
                if (_npcSaveDict.TryGetValue(id, out NpcSaveData? live) == false)
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
                throw new InvalidOperationException("NPC save snapshot capture failed: " + string.Join(", ", _snapshotFailedIds));
        }

        private void ResetSaveSnapshot()
        {
            for (int i = 0; i < _saveShardGenerations.Length; i++) _saveShardGenerations[i]++;
            _saveSnapshot.Clear();
            _dirtyNpcIds.Clear();
            _dirtyNpcOrder.Clear();
            _snapshotFailedIds.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _snapshotFlushCount = 0;
            _snapshotFlushTotalMs = 0;
            _snapshotFlushMaxMs = 0;
#endif
        }
    }
}
