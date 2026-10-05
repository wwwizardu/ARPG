#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using ARPG.Data;

namespace ARPG.Npc
{
    public partial class NpcManager
    {
        public static readonly bool SaveSnapshotEnabled = true;
        private SaveSnapshotCache<int> _saveSnapshot = new("Npc", SaveSnapshotGroups.Npc);
        private readonly SaveDtoSnapshot _snapshotCopier = new();
        // Created once in Initialize and reused; passing the method group directly allocates a delegate every frame.
        private Func<int, object?> _saveSourceGetter = null!;
        private long[] _saveShardGenerations = new long[DataManager.NPC_SHARD_COUNT];
        public long GetSaveShardGeneration(int index) => _saveShardGenerations[index];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public long SnapshotFlushCount => _saveSnapshot.FlushCount;
        public double SnapshotFlushAverageMs => _saveSnapshot.FlushAverageMs;
        public double SnapshotFlushMaxMs => _saveSnapshot.FlushMaxMs;
        public string DescribeSaveSnapshot() => _saveSnapshot.DescribeState();
#endif

        public void MarkSaveDirty(int id, NpcSaveGroup groups = NpcSaveGroup.All)
        {
            if (SaveSnapshotEnabled == false) return;
            _saveShardGenerations[DataManager.GetNpcShardIndex(id)]++;
            _saveSnapshot.MarkDirty(id, (ulong)groups);
        }

        public IDisposable FreezeSaveSnapshot() => _saveSnapshot.Freeze();
        public void FlushSaveSnapshot(bool recordFrameTiming = false)
        {
            if (SaveSnapshotEnabled == true) _saveSnapshot.Flush(_saveSourceGetter, recordFrameTiming);
        }

        public void RebuildSaveSnapshot()
        {
            for (int i = 0; i < _saveShardGenerations.Length; i++) _saveShardGenerations[i]++;
            if (SaveSnapshotEnabled == true) _saveSnapshot.Rebuild(_npcSaveDict.Keys, _saveSourceGetter);
        }

        public object?[] CaptureSaveSnapshotShards()
        {
            _saveSnapshot.PrepareForSave(_saveSourceGetter);
            object?[] roots = new object?[DataManager.NPC_SHARD_COUNT];
            Dictionary<string, object?>[] entries = new Dictionary<string, object?>[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new NpcShardSaveRoot { ShardIndex = i })!;
                roots[i] = root;
                entries[i] = (Dictionary<string, object?>)root[nameof(NpcShardSaveRoot.Npcs)]!;
            }
            foreach (int id in _npcSaveDict.Keys)
                entries[DataManager.GetNpcShardIndex(id)].Add(id.ToString(CultureInfo.InvariantCulture), _saveSnapshot.Get(id));
            return roots;
        }

        private static bool IsPositionField(string field) => field == nameof(NpcSaveData.PositionX) || field == nameof(NpcSaveData.PositionY);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int ValidateSaveSnapshot()
        {
            int mismatches = _saveSnapshot.Validate(_npcSaveDict.Keys, _saveSourceGetter,
                (id, field) => IsSavePositionDerived(id) == true && IsPositionField(field) == true);
            // Diagnostic repair changes DTOs too; an incremental save must not reuse their old shard tokens.
            if (mismatches > 0)
                for (int i = 0; i < _saveShardGenerations.Length; i++) _saveShardGenerations[i]++;
            return mismatches;
        }
        public bool TryFindUnmarkedSaveChange(int id, List<string> fields)
        {
            bool changed = _saveSnapshot.TryFindUnmarkedChange(id, _saveSourceGetter, fields,
                field => IsSavePositionDerived(id) == true && IsPositionField(field) == true);
            if (changed == true) _saveShardGenerations[DataManager.GetNpcShardIndex(id)]++;
            return changed;
        }
#endif

        private object? GetSaveSource(int id) => _npcSaveDict.TryGetValue(id, out NpcSaveData? live) == true ? live : null;
        private void ResetSaveSnapshot()
        {
            for (int i = 0; i < _saveShardGenerations.Length; i++) _saveShardGenerations[i]++;
            _saveSnapshot.Reset();
        }
    }
}
