#nullable enable
using System;
using System.Collections.Generic;

namespace ARPG.Npc
{
    /// <summary>NPC 개인 전기에 남기는 의미 있는 사건의 종류.</summary>
    public enum NpcDeedKind
    {
        VentureFailed = 0,
        Fell = 1,
        PartyAffinityChanged = 2,
        Wounded = 3,
        NotableItemAcquired = 4,
    }

    /// <summary>NPC가 사건을 알게 된 직접성 등급. 내러티브 지식 거리의 입력이다.</summary>
    public enum NpcKnowledgeLevel
    {
        Lived = 0,
        Did = 1,
        Witnessed = 2,
        Heard = 3,
    }

    /// <summary>
    /// NPC가 직접 겪거나 목격한 단일 에피소드. 정착지 매크로 사건은 RefEventId로 참조한다.
    /// </summary>
    [Serializable]
    public class NpcDeedEntry
    {
        public NpcDeedKind Kind;
        public int RefEventId = -1;
        public List<int> Participants = new();
        public List<NpcDeedParticipant> ParticipantSnapshots = new();
        public NpcKnowledgeLevel HowKnown;
        public float When;
        public int AffinityDelta;
        public float RecoveryEndGameTime;
        public int ItemId = -1;
        public string ItemDisplayName = string.Empty;

        public NpcDeedEntry() { }

        public NpcDeedEntry(NpcDeedKind kind, int refEventId, NpcKnowledgeLevel howKnown, float when)
        {
            Kind = kind;
            RefEventId = refEventId;
            HowKnown = howKnown;
            When = when;
        }

        public void EnsureInitialized()
        {
            if (Participants == null)
                Participants = new List<int>();
            if (ParticipantSnapshots == null)
                ParticipantSnapshots = new List<NpcDeedParticipant>();
            if (ItemDisplayName == null)
                ItemDisplayName = string.Empty;
        }
    }

    [Serializable]
    public class NpcDeedParticipant
    {
        public int EntityId = -1;
        public string DisplayName = string.Empty;

        public NpcDeedParticipant() { }

        public NpcDeedParticipant(int entityId, string displayName)
        {
            EntityId = entityId;
            DisplayName = displayName;
        }
    }
}
