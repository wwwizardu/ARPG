#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Npc
{
    /// <summary>NPC 개인 전기에 남기는 의미 있는 사건의 종류.</summary>
    public enum NpcDeedKind
    {
        VentureFailed = 0,
        Fell = 1,
        PartyAffinityChanged = 2,
        // 구 세이브 전용 (D91 이전의 원정 부상 기록). 로드 때 NpcSaveData.MigrateLegacyWoundedDeeds가
        // 누적 부상 필드로 합치고 목록에서 지운다 — 새로 기록하지 않는다.
        Wounded = 3,
        NotableItemAcquired = 4,
        // 플레이어와의 결투 결과 (NPC_COMBAT_DESIGN §7.4, B4). 참가자: NPC 본인 + 플레이어
        DuelWon = 5,
        DuelLost = 6,
        DuelDrawn = 7,
        // NPC 던전 완료 (NPC_AMBITION_DESIGN §7.1, D58·D61). 참가자: 완료한 NPC 본인 + 함께 간 파티원
        DungeonCleared = 8,
        // 원정 중상 (NPC_AMBITION_DESIGN D91 ②). 가벼운 부상은 Deed 없이 NpcSaveData 누적 필드만 갱신한다.
        // Kind·When만 남긴다 (참가자·치료·회복·활동 없음). 활동이 필요하면 NpcSaveData.LastWoundActivity를 본다.
        SevereWound = 9,
        // 공격받은 부상 (D91 ③, NPC 결투·배신·플레이어 공격). Kind·When + 가해자(Participants/ParticipantSnapshots 1명)만 남긴다.
        // 발생 경로는 P3·P5에서 연결한다.
        AttackedWound = 10,
        // NPC 간 절도 (NPC_AMBITION_DESIGN §7.6, D106). Kind·When + 노린 아이템(ItemId·ItemDisplayName)만, 본인은 참가자에 넣지 않는다.
        // 피해자 — 안 들킨 도난: 범인을 모르므로 참가자 없음.
        ItemStolen = 11,
        // 피해자 — 현장 적발 (물건은 옮겨지지 않음). 참가자 = 도둑 1명.
        CaughtThiefInAct = 12,
        // 도둑 — 현장 적발. 참가자 = 피해자 1명. 도둑 쪽 기록은 발각됐을 때만 만든다 (안 들킨 절도는 기록 없음).
        CaughtStealing = 13,
        // 늦은 발각 (D103·D106) — 피해자: 도둑맞은 물건의 범인을 소문으로 알게 됨. 참가자 = 도둑 1명. When = 발각 시각.
        TheftDiscovered = 14,
        // 늦은 발각 — 도둑: 훔친 것이 들통남 (도둑 쪽 기록은 이때 처음 생긴다). 참가자 = 피해자 1명.
        TheftExposed = 15,
        // 16·17은 단기 목표(NPC_OBJECTIVE_DESIGN ObjectiveFulfilled·ObjectiveAbandoned)가 예약했다 — 쓰지 않는다.
        // 네임드 위협에게 다침 (NAMED_THREAT_DESIGN §6.3). Kind·When + 참가자 스냅샷 1개(EntityId -1, 칭호)만 남긴다.
        MauledByThreat = 18,
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
    /// 저장 크기 (D91 후속): Kind·When 외의 필드는 기본값이면 저장하지 않고(빈 리스트 포함), 로드 때 필드 초기값으로 되돌아온다.
    /// 기본값: RefEventId -1, 리스트 빈 목록, HowKnown Lived, AffinityDelta 0, RecoveryEndGameTime 0, Treated false,
    /// Activity None, ItemId -1, ItemDisplayName "". 구 세이브(모든 필드 저장)도 그대로 읽힌다.
    /// </summary>
    [Serializable]
    public class NpcDeedEntry
    {
        public NpcDeedKind Kind;
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int RefEventId = -1;
        public List<int> Participants = new();
        public List<NpcDeedParticipant> ParticipantSnapshots = new();
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcKnowledgeLevel HowKnown;
        public float When;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int AffinityDelta;
        // 구 Wounded Deed 전용 (SevereWound는 남기지 않는다, D91 후속).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float RecoveryEndGameTime;
        // 구 Wounded Deed 전용: 부상 시점에 추상 치료 서비스를 샀는지 (NPC_AMBITION_DESIGN D36).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Treated;
        // 구 Wounded Deed 전용: 부상을 입은 활동 (P2c). DungeonDelve만 기록하고, None은 Hunt로 읽는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcActivityKind Activity;
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ItemId = -1;
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
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

        // Newtonsoft 조건부 직렬화 규약: 빈 참가자 목록은 저장하지 않는다 (로드 때 필드 초기값 = 빈 목록).
        public bool ShouldSerializeParticipants() => Participants != null && Participants.Count > 0;

        public bool ShouldSerializeParticipantSnapshots() => ParticipantSnapshots != null && ParticipantSnapshots.Count > 0;
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
