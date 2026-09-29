#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
        // 플레이어와의 결투 결과 (NPC_COMBAT_DESIGN §7.4, B4). 참가자: NPC 본인 + 플레이어
        DuelWon = 5,
        DuelLost = 6,
        DuelDrawn = 7,
        // NPC 던전 완료 (NPC_AMBITION_DESIGN §7.1, D58·D61). 참가자: 완료한 NPC 본인 + 함께 간 파티원
        DungeonCleared = 8,
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
        // Wounded 전용: 부상 시점에 추상 치료 서비스를 샀는지 (NPC_AMBITION_DESIGN D36). 구 세이브는 false.
        public bool Treated;
        // Wounded 전용: 부상을 입은 활동 (NPC_AMBITION P2c). DungeonDelve만 기록하고, None은 Hunt(구 세이브·기존 기록 포함)로 읽는다.
        // 기본값이면 저장하지 않아 기존 Deed의 저장 크기는 늘지 않는다. 로드 시 필드가 없으면 None → 보정 불필요.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcActivityKind Activity;
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
