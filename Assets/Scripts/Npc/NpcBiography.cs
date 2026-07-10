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
        public NpcKnowledgeLevel HowKnown;
        public float When;

        public NpcDeedEntry() { }

        public NpcDeedEntry(NpcDeedKind kind, int refEventId, NpcKnowledgeLevel howKnown, float when)
        {
            Kind = kind;
            RefEventId = refEventId;
            HowKnown = howKnown;
            When = when;
        }
    }
}
