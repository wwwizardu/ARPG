#nullable enable
using System.Collections.Generic;
using ARPG.Data;

namespace ARPG.History
{
    public partial class HistoryManager
    {
        public static readonly bool SaveSnapshotEnabled = true;
        private SaveSnapshotCache<int> _settlementSaveSnapshot = new("WorldSim");
        private readonly SaveDtoSnapshot _snapshotCopier = new();
        public object SaveSnapshotGeneration => (_settlementSaveSnapshot, _settlementSaveSnapshot.Generation,
            AR.s.Data.WorldSim.Version, AR.s.Data.WorldSim.IsInitialized, AR.s.Data.WorldSim.Settlements?.Length);

        // 배열 인덱스는 Initialize 후 고정이며 저장 순서와 같다.
        public void MarkSaveDirty(int index)
        {
            if (SaveSnapshotEnabled == true) _settlementSaveSnapshot.MarkDirty(index);
        }

        public void FlushSaveSnapshot(bool recordFrameTiming = false)
        {
            if (SaveSnapshotEnabled == true && _settlementSaveSnapshot.HasPendingChanges == true)
                _settlementSaveSnapshot.Flush(GetSettlementSaveSource, recordFrameTiming);
        }

        public void RebuildSaveSnapshot()
        {
            if (SaveSnapshotEnabled == true) _settlementSaveSnapshot.Rebuild(GetSettlementSaveIndices(), GetSettlementSaveSource);
        }

        public object? CaptureSaveSnapshotRoot()
        {
            _settlementSaveSnapshot.PrepareForSave(GetSettlementSaveSource);
            WorldSimState live = AR.s.Data.WorldSim;
            var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new WorldSimSaveRoot
                { WorldSim = new WorldSimState { Version = live.Version } })!;
            var sim = (Dictionary<string, object?>)root[nameof(WorldSimSaveRoot.WorldSim)]!;
            var settlements = (List<object?>)sim[nameof(WorldSimState.Settlements)]!;
            sim[nameof(WorldSimState.IsInitialized)] = live.IsInitialized;
            int count = live.Settlements?.Length ?? 0;
            for (int i = 0; i < count; i++) settlements.Add(_settlementSaveSnapshot.Get(i));
            if (live.Settlements == null) sim[nameof(WorldSimState.Settlements)] = null;
            return root;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int ValidateSaveSnapshot() => SaveSnapshotEnabled == true
            ? _settlementSaveSnapshot.Validate(GetSettlementSaveIndices(), GetSettlementSaveSource) : 0;

        public bool TryFindUnmarkedSaveChange(int index, List<string> fields) =>
            _settlementSaveSnapshot.TryFindUnmarkedChange(index, GetSettlementSaveSource, fields);

        public string DescribeSaveSnapshot() => _settlementSaveSnapshot.DescribeState();
#endif

        private IEnumerable<int> GetSettlementSaveIndices()
        {
            int count = AR.s.Data.WorldSim.Settlements?.Length ?? 0;
            for (int i = 0; i < count; i++) yield return i;
        }

        private object? GetSettlementSaveSource(int index)
        {
            SettlementLiveState[]? settlements = AR.s.Data.WorldSim.Settlements;
            return settlements != null && index >= 0 && index < settlements.Length ? settlements[index] : null;
        }
    }
}
