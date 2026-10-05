#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using UnityEngine;

using ARPG.Data;

namespace ARPG.Npc
{
    public partial class NpcManager
    {
        /// <summary>검사창에서 사용하는 NPC 쌍당 공동 활동 통계. 매 프레임 호출하지 않는다.</summary>
        public List<NpcAffinityEntry> GetSharedActivities(int npcId)
        {
            List<NpcAffinityEntry> result = new();
            foreach (var pair in _npcAffinity)
            {
                if ((pair.Key.Lo == npcId || pair.Key.Hi == npcId) && pair.Value.SharedActivities != null)
                    result.Add(pair.Value);
            }
            result.Sort((a, b) =>
            {
                int time = b.SharedActivities!.LastGameTime.CompareTo(a.SharedActivities!.LastGameTime);
                return time != 0 ? time : OtherNpcId(a, npcId).CompareTo(OtherNpcId(b, npcId));
            });
            return result;
        }

        public bool TryGetLatestSharedActivity(int npcId, out int otherId, out NpcSharedActivityStats? activities,
            out NpcSharedActivityMemory? memory)
        {
            otherId = -1;
            activities = null;
            memory = null;
            if (_npcSaveDict.TryGetValue(npcId, out NpcSaveData? save) == false || save.PersonalStories == null)
                return false;
            NpcPersonalStories stories = save.PersonalStories;
            otherId = stories.CompanionId;
            if (otherId < 0)
                return false;
            int index = stories.CompanionMemoryIndex;
            if (index >= 0 && index < save.SharedActivityMemories.Count &&
                save.SharedActivityMemories[index].OtherNpcId == otherId)
            {
                memory = save.SharedActivityMemories[index];
                activities = memory.Activities;
            }
            else if (_npcAffinity.TryGetValue(new NpcPairKey(npcId, otherId), out NpcAffinityEntry? entry) == true)
                activities = entry.SharedActivities;
            return activities != null;
        }

        /// <summary>구 양쪽 Deed를 한 쌍의 통계로 이관한다. 호감도 현재값은 건드리지 않고 과거 횟수도 추정하지 않는다.</summary>
        public int MigrateLegacyPartyActivities()
        {
            Dictionary<NpcPairKey, NpcSharedActivityStats> legacy = new();
            foreach (var owner in _npcSaveDict)
            {
                MarkSaveDirty(owner.Key, NpcSaveGroup.Deeds | NpcSaveGroup.Memories | NpcSaveGroup.Stories);
                NpcSaveData save = owner.Value;
                save.MergeDuplicatePartyAffinityDeeds();
                for (int i = 0; i < save.Deeds.Count; i++)
                {
                    NpcDeedEntry deed = save.Deeds[i];
                    if (deed.Kind != NpcDeedKind.PartyAffinityChanged)
                        continue;
                    int partner = NpcSaveData.GetPartyAffinityPartnerId(deed, owner.Key);
                    if (partner < 0 || partner == owner.Key)
                        continue;
                    NpcPairKey key = new(owner.Key, partner);
                    if (legacy.TryGetValue(key, out NpcSharedActivityStats? stats) == false)
                    {
                        stats = new NpcSharedActivityStats { HasUncountedHistory = true };
                        legacy.Add(key, stats);
                        stats.AffinityGain = deed.AffinityDelta;
                    }
                    // 두 사본을 더하지 않는다. 양쪽 값이 다르면 작은 ID 소유자의 기록을 정본으로 삼는다.
                    if (owner.Key == key.Lo)
                        stats.AffinityGain = deed.AffinityDelta;
                    stats.LastGameTime = Mathf.Max(stats.LastGameTime, deed.When);
                }
            }
            foreach (var pair in legacy)
            {
                bool active = _npcSaveDict.TryGetValue(pair.Key.Lo, out NpcSaveData? a) == true &&
                              _npcSaveDict.TryGetValue(pair.Key.Hi, out NpcSaveData? b) == true &&
                              a.Condition != CharacterConditions.Dead && b.Condition != CharacterConditions.Dead;
                if (active == true)
                {
                    NpcAffinityEntry entry = GetOrCreateAffinity(pair.Key.Lo, pair.Key.Hi);
                    entry.SharedActivities = MergeLegacyActivities(entry.SharedActivities, pair.Value);
                }
                else
                {
                    MarkAffinitySaveDirty(pair.Key.Lo, pair.Key.Hi);
                    _npcAffinity.TryGetValue(pair.Key, out NpcAffinityEntry? entry);
                    NpcSharedActivityStats stats = MergeLegacyActivities(entry?.SharedActivities, pair.Value);
                    ArchiveSharedActivities(pair.Key, stats, entry?.Affinity, true);
                    _npcAffinity.Remove(pair.Key);
                }
            }
            int removed = 0;
            foreach (var owner in _npcSaveDict)
            {
                MarkSaveDirty(owner.Key, NpcSaveGroup.Deeds | NpcSaveGroup.Memories | NpcSaveGroup.Stories);
                removed += owner.Value.Deeds.RemoveAll(deed => deed.Kind == NpcDeedKind.PartyAffinityChanged &&
                    legacy.ContainsKey(new NpcPairKey(owner.Key, NpcSaveData.GetPartyAffinityPartnerId(deed, owner.Key))));
            }
            return removed;
        }

        private NpcAffinityEntry GetOrCreateAffinity(int a, int b)
        {
            MarkAffinitySaveDirty(a, b);
            NpcPairKey key = new(a, b);
            if (_npcAffinity.TryGetValue(key, out NpcAffinityEntry? entry) == false)
            {
                entry = new NpcAffinityEntry(key.Lo, key.Hi, 0);
                _npcAffinity.Add(key, entry);
            }
            return entry;
        }

        private void RecordSharedActivity(int a, int b, NpcActivityKind activity, int affinityDelta, float when,
            bool dungeonCleared)
        {
            // 파티 목록에 같은 ID가 겹쳐도 자기 자신과의 쌍을 만들지 않는다 (SetAffinity·ModifyAffinity와 같은 규칙).
            if (a == b)
                return;
            NpcAffinityEntry entry = GetOrCreateAffinity(a, b);
            NpcSharedActivityStats stats = entry.SharedActivities ??= new NpcSharedActivityStats();
            if (activity == NpcActivityKind.Hunt)
                stats.HuntCount++;
            else if (activity == NpcActivityKind.DungeonDelve)
                stats.DungeonDelveCount++;
            if (activity == NpcActivityKind.DungeonDelve && dungeonCleared == true)
                stats.DungeonClearCount++;
            stats.AffinityGain += affinityDelta;
            if (when >= stats.LastGameTime)
            {
                stats.LastGameTime = when;
                stats.LastActivity = activity;
            }
            RecordLatestCompanion(a, b, stats, -1);
            RecordLatestCompanion(b, a, stats, -1);
            _narrative.InvalidateDialogues(a);
            _narrative.InvalidateDialogues(b);
        }

        private void ArchiveSharedActivities(NpcPairKey key, NpcSharedActivityStats stats, int? affinity,
            bool fromLegacyDeeds = false)
        {
            StoreSharedActivityMemory(key.Lo, key.Hi, stats, affinity, fromLegacyDeeds);
            StoreSharedActivityMemory(key.Hi, key.Lo, stats, affinity, fromLegacyDeeds);
        }

        private void StoreSharedActivityMemory(int ownerId, int otherId, NpcSharedActivityStats stats, int? affinity,
            bool fromLegacyDeeds)
        {
            MarkSaveDirty(ownerId, NpcSaveGroup.Memories | NpcSaveGroup.Stories);
            if (_npcSaveDict.TryGetValue(ownerId, out NpcSaveData? owner) == false)
                return;
            NpcSharedActivityMemory? memory = null;
            for (int i = 0; i < owner.SharedActivityMemories.Count; i++)
            {
                if (owner.SharedActivityMemories[i].OtherNpcId == otherId)
                {
                    memory = owner.SharedActivityMemories[i];
                    break;
                }
            }
            if (memory == null)
            {
                // 구 Deed 이관 때만 당시 이름을 찾는다. 런타임 보관 시점에는 구 Deed가 로드 때 이미 지워져 있다.
                string name = fromLegacyDeeds == true ? FindLegacyPartnerName(owner, ownerId, otherId) : string.Empty;
                if (string.IsNullOrEmpty(name) == true)
                    name = ResolveNpcDisplayName(otherId);
                memory = new NpcSharedActivityMemory { OtherNpcId = otherId, OtherNpcName = name };
                owner.SharedActivityMemories.Add(memory);
            }
            memory.Activities = stats.Copy();
            memory.LastAffinity = affinity ?? memory.LastAffinity;
            RecordLatestCompanion(ownerId, otherId, memory.Activities, owner.SharedActivityMemories.IndexOf(memory));
            _narrative.InvalidateDialogues(ownerId);
        }

        /// <summary>구 Deed의 당시 이름. 상대가 이미 사망하거나 이름이 바뀌어도 같은 회고가 남는다. 없으면 빈 문자열.</summary>
        private static string FindLegacyPartnerName(NpcSaveData owner, int ownerId, int otherId)
        {
            string name = string.Empty;
            for (int d = 0; d < owner.Deeds.Count; d++)
            {
                NpcDeedEntry deed = owner.Deeds[d];
                if (deed.Kind != NpcDeedKind.PartyAffinityChanged ||
                    NpcSaveData.GetPartyAffinityPartnerId(deed, ownerId) != otherId)
                    continue;
                for (int p = 0; p < deed.ParticipantSnapshots.Count; p++)
                {
                    NpcDeedParticipant participant = deed.ParticipantSnapshots[p];
                    if (participant.EntityId == otherId && string.IsNullOrEmpty(participant.DisplayName) == false)
                        name = participant.DisplayName;
                }
            }
            return name;
        }

        private static NpcSharedActivityStats MergeLegacyActivities(NpcSharedActivityStats? current,
            NpcSharedActivityStats legacy)
        {
            if (current == null)
                return legacy.Copy();
            if (current.HasUncountedHistory == false)
                current.AffinityGain += legacy.AffinityGain;
            current.HasUncountedHistory = true;
            if (legacy.LastGameTime > current.LastGameTime)
            {
                current.LastGameTime = legacy.LastGameTime;
                current.LastActivity = NpcActivityKind.None;
            }
            return current;
        }

        /// <summary>한 번의 로드에서만 전체 이력을 읽고 종류별 최신 사연을 복원한다.</summary>
        private bool RestorePersonalStories()
        {
            bool changed = false;
            Dictionary<int, (int Id, int Memory)> previous = new();
            foreach (var npc in _npcSaveDict)
            {
                MarkSaveDirty(npc.Key, NpcSaveGroup.Stories | NpcSaveGroup.Deeds);
                changed |= npc.Value.MigratePersonalStories();
                NpcPersonalStories? stories = npc.Value.PersonalStories;
                previous.Add(npc.Key, (stories?.CompanionId ?? -1, stories?.CompanionMemoryIndex ?? -1));
                if (stories != null)
                {
                    stories.CompanionId = -1;
                    stories.CompanionMemoryIndex = -1;
                }
            }
            foreach (var pair in _npcAffinity)
            {
                NpcSharedActivityStats? stats = pair.Value.SharedActivities;
                if (stats == null)
                    continue;
                RecordLatestCompanion(pair.Key.Lo, pair.Key.Hi, stats, -1);
                RecordLatestCompanion(pair.Key.Hi, pair.Key.Lo, stats, -1);
            }
            foreach (var npc in _npcSaveDict)
            {
                for (int i = 0; i < npc.Value.SharedActivityMemories.Count; i++)
                {
                    NpcSharedActivityMemory memory = npc.Value.SharedActivityMemories[i];
                    RecordLatestCompanion(npc.Key, memory.OtherNpcId, memory.Activities, i);
                }
                NpcPersonalStories? stories = npc.Value.PersonalStories;
                changed |= previous[npc.Key] != (stories?.CompanionId ?? -1, stories?.CompanionMemoryIndex ?? -1);
            }
            return changed;
        }

        private void RecordLatestCompanion(int npcId, int otherId, NpcSharedActivityStats stats, int memoryIndex)
        {
            MarkSaveDirty(npcId, NpcSaveGroup.Stories);
            if (_npcSaveDict.TryGetValue(npcId, out NpcSaveData? save) == false)
                return;
            bool hasLatest = TryGetLatestSharedActivity(npcId, out int latestId, out NpcSharedActivityStats? latest, out _);
            if (hasLatest == true && latestId != otherId && IsLaterActivity(stats, otherId, latest, latestId) == false)
                return;
            NpcPersonalStories stories = save.PersonalStories ??= new();
            stories.CompanionId = otherId;
            stories.CompanionMemoryIndex = memoryIndex;
        }

        private static int OtherNpcId(NpcAffinityEntry entry, int owner) => entry.NpcA == owner ? entry.NpcB : entry.NpcA;

        private static bool IsLaterActivity(NpcSharedActivityStats candidate, int candidateId,
            NpcSharedActivityStats? current, int currentId) => current == null ||
            candidate.LastGameTime > current.LastGameTime ||
            (candidate.LastGameTime == current.LastGameTime && candidateId < currentId);
    }
}
