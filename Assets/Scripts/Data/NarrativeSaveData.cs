#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using ARPG.Npc.Narrative;
using Newtonsoft.Json;

namespace ARPG.Data
{
    /// <summary>
    /// 공통 콘텐츠 실행 기록의 독립 저장 루트 — narrative 파트 (NPC_NARRATIVE_CONTENT_DESIGN §7.1~§7.2).
    /// 런타임 원장 소유자는 NpcManager, 저장 I/O는 DataManager다. NPC 지식(부탁 칸·행적 소문)은 NPC 파트에 둔다.
    /// </summary>
    [Serializable]
    public sealed class NarrativeSaveRoot
    {
        public int Version = 1;
        public List<NarrativeContentInstance> Instances = new();
        // 사건이 고정해 쓰는 작성 정의·반응 정책 (해시별 한 번)
        public List<NarrativeDefinitionSnapshot> Definitions = new();
        public List<NarrativePolicySnapshot> Policies = new();
        // 던전 세대별 콘텐츠 선정 완료 기록. 같은 세대에서 다시 고르지 않는다.
        public List<NarrativeSelectionRecord> Selections = new();
    }

    /// <summary>
    /// 실제 플레이에서 생긴 사건 하나 (§2.5·§7.2). 같은 콘텐츠를 두 던전에서 써도 인스턴스는 다르다.
    /// 기본값인 필드는 저장하지 않는다.
    /// </summary>
    [Serializable]
    public sealed class NarrativeContentInstance
    {
        // ── 식별·정책 ──
        public string InstanceId = string.Empty;
        public int ContentId;
        public NarrativeContentKind Kind;
        public string DefinitionHash = string.Empty;
        public string PolicyHash = string.Empty;
        public float CreatedGameTime;

        // ── 실제 대상 연결: 던전 세대 ──
        public string DungeonId = string.Empty;
        public int VillageId;
        public int SlotIndex;
        public int Generation;
        public int TargetMonsterId;
        // 입구 좌표 문맥. 던전이 닫혀 슬롯이 다음 세대로 넘어간 뒤 보고·알림 대사의 위치 표현에 쓴다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasEntranceContext;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int EntranceX;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int EntranceY;

        // ── 실제 대상 연결: 소망 아이템 (ItemTable 행 번호) ──
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int TargetItemId;

        // ── 실제 대상 연결: 네임드 위협 (NAMED_THREAT_DESIGN §10.3). VillageId = 주 마을 ──
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ThreatId;

        // ── 소문 주인·소망 주인·부탁 ──
        // 소문 주인 또는 소망 주인. 소문은 -1 = 아직 고르지 못함(그 마을에 고를 주민이 없었음) — 새 주민이 등록되면 다시 고른다.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int OwnerNpcId = -1;
        // 부탁한 NPC (조회용 — 소문은 정본이 그 NPC의 부탁 칸). 소문 주인 또는 후속 소개로 이 소문을 소개한 NPC다. 소망은 털어놓은 주인이다.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int RequesterNpcId = -1;
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float RequestedGameTime = -1f;
        // 후속 소개로 부탁이 시작됐으면 소개한 쪽 사건 (§4.6, 5단계). 이때 부탁한 NPC는 소문 주인이 아니라 소개한 NPC일 수 있다.
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string IntroducedFromInstanceId = string.Empty;

        // ── 진행 ──
        // 던전 소문의 진행. 소망은 쓰지 않는다(늘 Unresolved).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NarrativeRumorProgress Progress;
        // 소망의 진행 (QUEST_SYSTEM_DESIGN §2.2). 털어놓은 때 만들므로 Revealed → Fulfilled(아이템 전달) 또는 Failed(주인 사망).
        // 던전 소문은 쓰지 않는다(늘 Hidden).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public WishState WishState;
        // 해결·충족 또는 미해결 종료·실패 시각 (행적의 사건 시각)
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float ProgressGameTime = -1f;
        // 플레이어와 무관하게 미해결로 끝남 (NPC 원정 완료·플레이어 측이 아닌 보스 처치)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Ended;
        // 부탁한 NPC가 다음 대화에서 "다른 사람들이 처리했다"고 알릴 차례
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool EndNoticePending;
        // 진행 revision. 같은 완료 신호의 재전달은 올리지 않는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Revision;

        // ── 보고 (부탁한 NPC에게 보고, 소문 전 해결을 소문 주인에게 말함, 또는 소망 아이템 전달) ──
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Reported;
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ReportNpcId = -1;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NarrativeRelevance ReportRelevance;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ReactionRuleId;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ScenarioReactionKind Reaction;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NarrativeResponseKind Response;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int AffinityDelta;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int TrustDelta;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int IntimacyDelta;
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float ReportGameTime = -1f;
        // 보고 대사가 올바른 화면에 실제로 표시됐음. false면 다음 대화에서 같은 내용을 다시 보여 준다(효과는 다시 적용하지 않음).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool ReportDisplayed;

        // ── 의뢰 보답 (행적당 최대 1번, 0 = 주지 않음) ──
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int RewardGold;
        // 부정 반응 + 약속 점수 0 미만으로 주지 않았음 (대사 재생용)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool RewardRefused;

        // ── 후속 소개 (이 행적에서 이어 소개한 소문, 이야기 줄기당 한 번, §4.6) ──
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string FollowUpInstanceId = string.Empty;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NarrativeFollowUpState FollowUpState;

        /// <summary>아직 진행 중인가. 던전 소문은 미해결이고 끝나지 않았을 때, 소망은 털어놓았고 아직 채워지지 않았을 때.</summary>
        [JsonIgnore]
        public bool IsOpen => Kind == NarrativeContentKind.Wish
            ? WishState == WishState.Revealed
            : Ended == false && Progress == NarrativeRumorProgress.Unresolved;

        /// <summary>되돌리기용 사본. 필드가 모두 값·문자열이라 얕은 복사로 충분하다.</summary>
        public NarrativeContentInstance Clone() => (NarrativeContentInstance)MemberwiseClone();

        /// <summary>던전 소문 인스턴스 ID. 던전 세대(DungeonId)마다 하나라 저장·소개 재시도가 같은 ID를 쓴다.</summary>
        public static string DungeonRumorId(string dungeonId) => "rumor/" + dungeonId;

        /// <summary>소망 인스턴스 ID. 같은 콘텐츠는 NPC당 한 번이라 (주인, 콘텐츠)마다 하나다 (사용자 결정 2026-10-10).</summary>
        public static string WishId(int ownerNpcId, int contentId) => "wish/" + ownerNpcId + "/" + contentId;

        /// <summary>네임드 위협 부탁 인스턴스 ID. 네임드마다 하나다 (NAMED_THREAT_DESIGN §8.4).</summary>
        public static string NamedThreatId(int threatId) => "threat/" + threatId;
    }

    /// <summary>
    /// 던전 세대 하나의 콘텐츠 선정 기록 (§7.2 Selections). InstanceId가 비어 있으면 일반 던전(콘텐츠 없음)이다.
    /// </summary>
    [Serializable]
    public sealed class NarrativeSelectionRecord
    {
        public string DungeonId = string.Empty;
        public int VillageId;
        public int SlotIndex;
        public int Generation;
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string InstanceId = string.Empty;
    }
}
