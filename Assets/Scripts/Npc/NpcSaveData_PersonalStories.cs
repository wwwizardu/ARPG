#nullable enable
using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Npc
{
    public partial class NpcSaveData
    {
        /// <summary>사건 발생 시 최신 사연을 갱신한다. 반복 피해 사연은 이력에 누적하지 않는다.</summary>
        public void RecordDeed(NpcDeedEntry entry)
        {
            if (IsTheftVictimStory(entry.Kind) == true)
            {
                NpcPersonalStories stories = PersonalStories ??= new();
                if (stories.TheftVictim == null || entry.When >= stories.TheftVictim.When)
                    stories.TheftVictim = entry;
                return;
            }
            int index = Deeds.Count;
            Deeds.Add(entry);
            if (entry.Kind == NpcDeedKind.NotableItemAcquired || entry.Kind == NpcDeedKind.DungeonCleared)
            {
                NpcDeedEntry? previous = GetLatestPersonalDeed(entry.Kind);
                if (previous != null && previous.When > entry.When)
                    return;
                NpcPersonalStories stories = PersonalStories ??= new();
                if (entry.Kind == NpcDeedKind.NotableItemAcquired)
                    stories.NotableItemIndex = index;
                else
                    stories.DungeonClearIndex = index;
            }
        }

        public NpcDeedEntry? GetLatestPersonalDeed(NpcDeedKind kind)
        {
            if (PersonalStories == null)
                return null;
            if (IsTheftVictimStory(kind) == true)
                return PersonalStories.TheftVictim?.Kind == kind ? PersonalStories.TheftVictim : null;
            int index = kind == NpcDeedKind.NotableItemAcquired ? PersonalStories.NotableItemIndex :
                kind == NpcDeedKind.DungeonCleared ? PersonalStories.DungeonClearIndex : -1;
            return index >= 0 && index < Deeds.Count && Deeds[index].Kind == kind ? Deeds[index] : null;
        }

        public void RecordDeceasedRelation(DeceasedRelationMemory memory)
        {
            NpcPersonalStories stories = PersonalStories ??= new();
            DeceasedRelationMemory? previous = GetLatestLoss();
            int index = DeceasedRelations.Count;
            DeceasedRelations.Add(memory);
            if (previous == null || memory.DeathGameTime >= previous.DeathGameTime)
                stories.LossIndex = index;
        }

        public DeceasedRelationMemory? GetLatestLoss()
        {
            int index = PersonalStories?.LossIndex ?? -1;
            return index >= 0 && index < DeceasedRelations.Count ? DeceasedRelations[index] : null;
        }

        /// <summary>로드 때 한 번: 구 사건을 최신 슬롯으로 이관하고 주요 이력 참조를 재구성한다.</summary>
        public bool MigratePersonalStories()
        {
            NpcPersonalStories? previous = PersonalStories;
            NpcDeedEntry? oldTheft = previous?.TheftVictim;
            if (PersonalStories != null)
            {
                PersonalStories.NotableItemIndex = -1;
                PersonalStories.DungeonClearIndex = -1;
                PersonalStories.LossIndex = -1;
            }
            int write = 0;
            for (int read = 0; read < Deeds.Count; read++)
            {
                NpcDeedEntry deed = Deeds[read];
                if (IsTheftVictimStory(deed.Kind) == true)
                {
                    NpcPersonalStories stories = PersonalStories ??= new();
                    if (stories.TheftVictim == null || deed.When >= stories.TheftVictim.When)
                        stories.TheftVictim = deed;
                    continue;
                }
                Deeds[write] = deed;
                if (deed.Kind == NpcDeedKind.NotableItemAcquired || deed.Kind == NpcDeedKind.DungeonCleared)
                {
                    NpcDeedEntry? latest = GetLatestPersonalDeed(deed.Kind);
                    if (latest == null || deed.When >= latest.When)
                    {
                        NpcPersonalStories stories = PersonalStories ??= new();
                        if (deed.Kind == NpcDeedKind.NotableItemAcquired)
                            stories.NotableItemIndex = write;
                        else
                            stories.DungeonClearIndex = write;
                    }
                }
                if (previous == null && deed.Kind == NpcDeedKind.SevereWound && deed.When == LastWoundGameTime)
                    (PersonalStories ??= new()).LastWoundSevere = true;
                write++;
            }
            int removed = Deeds.Count - write;
            if (removed > 0)
                Deeds.RemoveRange(write, removed);
            if (WoundCount > 0)
                PersonalStories ??= new();
            for (int i = 0; i < DeceasedRelations.Count; i++)
            {
                DeceasedRelationMemory? latest = GetLatestLoss();
                if (latest == null || DeceasedRelations[i].DeathGameTime >= latest.DeathGameTime)
                    (PersonalStories ??= new()).LossIndex = i;
            }
            // 아이템·던전 완료·사별 인덱스는 저장하지 않으므로 다시 구성해도 저장할 변화로 보지 않는다.
            return removed > 0 || (previous == null && PersonalStories != null) || oldTheft != PersonalStories?.TheftVictim;
        }

        public static bool IsTheftVictimStory(NpcDeedKind kind) => kind == NpcDeedKind.ItemStolen ||
            kind == NpcDeedKind.CaughtThiefInAct || kind == NpcDeedKind.TheftDiscovered;
    }

    /// <summary>
    /// 개인 사연 여섯 종류의 최신 슬롯. 인덱스는 로드 시 다시 구성하며 대화에서는 O(1)로 읽는다.
    /// 아이템·던전 완료·사별 인덱스는 OnLoadCompleted가 항상 다시 만들므로 저장하지 않는다. 동료 참조는 호감도 로드 때
    /// 다시 맞추지만, 호감도를 다시 읽지 않는 복제본(진단 시뮬)도 쓰도록 저장한다.
    /// </summary>
    [Serializable]
    public class NpcPersonalStories
    {
        [JsonIgnore]
        public int NotableItemIndex = -1;
        [JsonIgnore]
        public int DungeonClearIndex = -1;
        [JsonIgnore]
        public int LossIndex = -1;
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int CompanionId = -1;
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int CompanionMemoryIndex = -1;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool LastWoundSevere;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcDeedEntry? TheftVictim;
    }
}
