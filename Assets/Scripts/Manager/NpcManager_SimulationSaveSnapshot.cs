#nullable enable
using System.Collections.Generic;
using ARPG.Data;

namespace ARPG.Npc
{
    public partial class NpcManager
    {
        public static readonly bool SimulationSaveSnapshotEnabled = true;
        private SaveSnapshotCache<NpcPairKey> _affinitySaveSnapshot = new("NpcAffinity");
        private SaveSnapshotCache<int> _relationSaveSnapshot = new("NpcRelations");
        private SaveSnapshotCache<int> _partySaveSnapshot = new("NpcParties");
        private SaveSnapshotCache<int> _populationSaveSnapshot = new("NpcPopulation");

        // 캐시 인스턴스도 포함해 Load/진단의 캐시 교체를 이전 세대와 구별한다.
        public object SocialSaveSnapshotGeneration => (_affinitySaveSnapshot, _affinitySaveSnapshot.Generation,
            _relationSaveSnapshot, _relationSaveSnapshot.Generation);
        public object PartySaveSnapshotGeneration => (_partySaveSnapshot, _partySaveSnapshot.Generation);
        public object PopulationSaveSnapshotGeneration => (_populationSaveSnapshot, _populationSaveSnapshot.Generation);

        public void MarkAffinitySaveDirty(int a, int b)
        {
            if (SimulationSaveSnapshotEnabled == true)
                _affinitySaveSnapshot.MarkDirty(new NpcPairKey(a, b));
        }

        public void MarkRelationSaveDirty(int fromId)
        {
            if (SimulationSaveSnapshotEnabled == true) _relationSaveSnapshot.MarkDirty(fromId);
        }

        public void MarkPartySaveDirty(int partyId)
        {
            if (SimulationSaveSnapshotEnabled == true) _partySaveSnapshot.MarkDirty(partyId);
        }

        public void MarkPopulationSaveDirty()
        {
            if (SimulationSaveSnapshotEnabled == true) _populationSaveSnapshot.MarkDirty(0);
        }

        public void RefreshSimulationSaveSnapshots(SaveScope scope, bool rebuild = false, bool recordFrameTiming = false)
        {
            if (SimulationSaveSnapshotEnabled == false) return;
            if ((scope & SaveScope.NpcSocial) != 0)
            {
                if (rebuild == true)
                {
                    _affinitySaveSnapshot.Rebuild(_npcAffinity.Keys, GetAffinitySaveSource);
                    _relationSaveSnapshot.Rebuild(_npcRelations.Keys, GetRelationSaveSource);
                }
                else
                {
                    if (_affinitySaveSnapshot.HasPendingChanges == true)
                        _affinitySaveSnapshot.Flush(GetAffinitySaveSource, recordFrameTiming);
                    if (_relationSaveSnapshot.HasPendingChanges == true)
                        _relationSaveSnapshot.Flush(GetRelationSaveSource, recordFrameTiming);
                }
            }
            if ((scope & SaveScope.NpcParties) != 0)
            {
                if (rebuild == true) _partySaveSnapshot.Rebuild(_npcPartyDict.Keys, GetPartySaveSource);
                else if (_partySaveSnapshot.HasPendingChanges == true) _partySaveSnapshot.Flush(GetPartySaveSource, recordFrameTiming);
            }
            if ((scope & SaveScope.NpcMeta) != 0)
            {
                if (rebuild == true) _populationSaveSnapshot.Rebuild(new[] { 0 }, GetPopulationSaveSource);
                else if (_populationSaveSnapshot.HasPendingChanges == true) _populationSaveSnapshot.Flush(GetPopulationSaveSource, recordFrameTiming);
            }
        }

        public object? CaptureSocialSaveSnapshotRoot()
        {
            _affinitySaveSnapshot.PrepareForSave(GetAffinitySaveSource);
            _relationSaveSnapshot.PrepareForSave(GetRelationSaveSource);
            var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new NpcSocialSaveRoot())!;
            var affinities = (List<object?>)root[nameof(NpcSocialSaveRoot.Affinities)]!;
            foreach (NpcPairKey key in _npcAffinity.Keys)
                affinities.Add(_affinitySaveSnapshot.Get(key));
            var relations = (List<object?>)root[nameof(NpcSocialSaveRoot.Relations)]!;
            // 기존 SaveRelations와 같은 From 인덱스·목록 순서로 중복 없이 게시한다.
            foreach (int key in _npcRelations.Keys)
                relations.AddRange((List<object?>)_relationSaveSnapshot.Get(key)!);
            return root;
        }

        public object? CapturePartySaveSnapshotRoot()
        {
            _partySaveSnapshot.PrepareForSave(GetPartySaveSource);
            var root = (Dictionary<string, object?>)_snapshotCopier.Capture(new NpcPartySaveRoot())!;
            var parties = (List<object?>)root[nameof(NpcPartySaveRoot.Parties)]!;
            for (int i = 0; i < _simulationPartyIds.Count; i++)
            {
                int id = _simulationPartyIds[i];
                if (_npcPartyDict.ContainsKey(id) == true) parties.Add(_partySaveSnapshot.Get(id));
            }
            return root;
        }

        public object? CapturePopulationSaveSnapshotRoot()
        {
            _populationSaveSnapshot.PrepareForSave(GetPopulationSaveSource);
            return _populationSaveSnapshot.Get(0);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int ValidateSimulationSaveSnapshots()
        {
            if (SimulationSaveSnapshotEnabled == false) return 0;
            return _affinitySaveSnapshot.Validate(_npcAffinity.Keys, GetAffinitySaveSource) +
                _relationSaveSnapshot.Validate(_npcRelations.Keys, GetRelationSaveSource) +
                _partySaveSnapshot.Validate(_npcPartyDict.Keys, GetPartySaveSource) +
                _populationSaveSnapshot.Validate(new[] { 0 }, GetPopulationSaveSource);
        }

        public string DescribeSimulationSaveSnapshots() => _affinitySaveSnapshot.DescribeState() +
            _relationSaveSnapshot.DescribeState() + _partySaveSnapshot.DescribeState() + _populationSaveSnapshot.DescribeState();

        public bool TryFindUnmarkedAffinitySaveChange(NpcPairKey key, List<string> fields) =>
            _affinitySaveSnapshot.TryFindUnmarkedChange(key, GetAffinitySaveSource, fields);

        public bool TryFindUnmarkedRelationSaveChange(int key, List<string> fields) =>
            _relationSaveSnapshot.TryFindUnmarkedChange(key, GetRelationSaveSource, fields);

        public bool TryFindUnmarkedPartySaveChange(int key, List<string> fields) =>
            _partySaveSnapshot.TryFindUnmarkedChange(key, GetPartySaveSource, fields);

        public bool TryFindUnmarkedPopulationSaveChange(List<string> fields) =>
            _populationSaveSnapshot.TryFindUnmarkedChange(0, GetPopulationSaveSource, fields);
#endif

        private object? GetAffinitySaveSource(NpcPairKey key) =>
            _npcAffinity.TryGetValue(key, out NpcAffinityEntry? entry) == true ? entry : null;

        private object? GetRelationSaveSource(int id)
        {
            if (_npcRelations.TryGetValue(id, out List<NpcRelationEntry>? entries) == false) return null;
            List<NpcRelationEntry> outgoing = new();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].FromEntityId == id) outgoing.Add(entries[i]);
            }
            return outgoing;
        }

        private object? GetPartySaveSource(int id) =>
            _npcPartyDict.TryGetValue(id, out NpcPartyState? party) == true ? party : null;

        private object GetPopulationSaveSource(int id) => new NpcMetaSaveRoot { Population = AR.s.Data.NpcPopulation };

        private void ResetSimulationSaveSnapshots()
        {
            _affinitySaveSnapshot.Reset();
            _relationSaveSnapshot.Reset();
            _partySaveSnapshot.Reset();
            _populationSaveSnapshot.Reset();
        }
    }
}
