#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using ARPG.Data;

namespace ARPG.Village
{
    public partial class VillageManager
    {
        public static readonly bool SaveSnapshotEnabled = true;
        private SaveSnapshotCache<int> _saveSnapshot = new("Village", SaveSnapshotGroups.Village);
        private readonly SaveDtoSnapshot _snapshotCopier = new();
        // Created once in Load/Initialize (whichever runs first) and reused; passing the method group directly
        // allocates a delegate every frame.
        private Func<int, object?> _saveSourceGetter = null!;
        public long SaveSnapshotGeneration => _saveSnapshot.Generation;
#if UNITY_EDITOR
        public Action<int>? SnapshotDirtyObserver { get => _saveSnapshot.DirtyObserver; set => _saveSnapshot.DirtyObserver = value; }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public long SnapshotFlushCount => _saveSnapshot.FlushCount;
        public double SnapshotFlushAverageMs => _saveSnapshot.FlushAverageMs;
        public double SnapshotFlushMaxMs => _saveSnapshot.FlushMaxMs;
        public string DescribeSaveSnapshot() => _saveSnapshot.DescribeState();
#endif

        public void MarkSaveDirty(int id, VillageSaveGroup groups = VillageSaveGroup.All)
        {
            if (SaveSnapshotEnabled == false) return;

            _saveSnapshot.MarkDirty(id, (ulong)groups);
        }

        public IDisposable FreezeSaveSnapshot() => _saveSnapshot.Freeze();
        public void FlushSaveSnapshot(bool recordFrameTiming = false)
        {
            if (SaveSnapshotEnabled == true) _saveSnapshot.Flush(_saveSourceGetter, recordFrameTiming);
        }

        public void RebuildSaveSnapshot()
        {
            if (SaveSnapshotEnabled == true) _saveSnapshot.Rebuild(_villages.Keys, _saveSourceGetter);
        }

        public object? CaptureSaveSnapshotRoot()
        {
            _saveSnapshot.PrepareForSave(_saveSourceGetter);
            var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new VillageSaveRoot())!;
            var entries = (List<object?>)root[nameof(VillageSaveRoot.Villages)]!;
            for (int i = 0; i < _villageList.Count; i++) entries.Add(_saveSnapshot.Get(_villageList[i].VillageId));
            return root;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int ValidateSaveSnapshot() => _saveSnapshot.Validate(_villages.Keys, _saveSourceGetter);
        public bool TryFindUnmarkedSaveChange(int id, List<string> fields) =>
            _saveSnapshot.TryFindUnmarkedChange(id, _saveSourceGetter, fields);
#endif

        private object? GetSaveSource(int id) => _villages.TryGetValue(id, out VillageData? live) == true ? live : null;
        private void ResetSaveSnapshot()
        {
            _saveSnapshot.Reset();
        }
    }
}
