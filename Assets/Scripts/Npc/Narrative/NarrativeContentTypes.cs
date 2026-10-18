#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ARPG.Npc.Narrative
{
    // NPC 대화 콘텐츠 공통 구조 (Docs/npc/NPC_NARRATIVE_CONTENT_DESIGN.md §1.1).
    // 동작은 enum, 데이터 참조는 int. enum 값은 저장되므로 숫자를 바꾸거나 재사용하지 않는다. 끝에만 추가한다.

    /// <summary>
    /// 콘텐츠 유형 (§1.1). DungeonRumor(1·2단계), Wish + Item(3단계, ItemGift), PersonalStory + RecentWound(4단계, 최근 부상 문구)를 실행한다.
    /// </summary>
    public enum NarrativeContentKind
    {
        None = 0,
        DungeonRumor = 1,
        Wish = 2,
        PersonalStory = 3,
        SmallTalk = 4,
        // 단기 목표 부탁 (NPC_OBJECTIVE_DESIGN §8.3·§9.2). 대상 = 목표 던전, 부탁한 NPC = 목표 주인. 부탁 대사가 표시된 뒤 만든다.
        Objective = 5,
        // 네임드 위협 부탁 (NAMED_THREAT_DESIGN §8.4·§10.3). 인스턴스 = threat/{네임드 ID}, 목표 = DefeatTarget(플레이어 측 처치)
        NamedThreat = 6,
    }

    /// <summary>콘텐츠가 가리키는 대상 종류 (§2.1).</summary>
    public enum NarrativeTargetType
    {
        None = 0,
        Monster = 1,
        Item = 2,
        Npc = 3,
        RecentWound = 4,
        // NamedThreat 탭의 템플릿 Id (NAMED_THREAT_DESIGN §11.3)
        NamedThreat = 5,
    }

    /// <summary>진행 방식 (§2.2). DungeonRumor의 Default 목표는 그 던전의 보스 처치(던전 종료)다.</summary>
    public enum NarrativeGoalType
    {
        Default = 0,
        DefeatTarget = 1,
        DeliverToGiver = 2,
        TalkToTarget = 3,
        Discuss = 4,
    }

    /// <summary>던전 소문 진행 (§3.1): 미해결 → 해결. 해결은 플레이어 측 보스 처치로만 성립한다.</summary>
    public enum NarrativeRumorProgress
    {
        Unresolved = 0,
        Resolved = 1,
    }

    /// <summary>
    /// 반응 관련도 (§4.2). 여러 개에 해당하면 가장 큰 배율 하나만 쓴다.
    /// 부탁한 사람·피해자 ×3, 동행 ×2, 같은 마을 ×1, 다른 마을 ×0.3.
    /// </summary>
    public enum NarrativeRelevance
    {
        None = 0,
        Requester = 1,
        Victim = 2,
        Companion = 3,
        SameVillage = 4,
        OtherVillage = 5,
    }

    /// <summary>
    /// NPC 반응 종류. 구 시나리오 값 Relieved=0, Curious=1, Impressed=2, Concerned=3을 유지하고 4~7을 추가했다 (§1.1).
    /// </summary>
    public enum ScenarioReactionKind
    {
        Relieved = 0,
        Curious = 1,
        Impressed = 2,
        Concerned = 3,
        Grateful = 4,
        Envious = 5,
        Hostile = 6,
        Suspicious = 7,
    }

    /// <summary>반응 점수 방식 (§4.3 점수식 표).</summary>
    public enum NarrativeReactionScoreProfile
    {
        None = 0,
        Curious = 1,
        Warm = 2,
        Admiring = 3,
        Cautious = 4,
        Grateful = 5,
        Envious = 6,
        Hostile = 7,
        Suspicious = 8,
    }

    /// <summary>
    /// 반응 기본 결과 조건 (§4.3). AnyKnownReport = 소문 해결·던전 클리어, RumorResolved = 소문 해결·네임드 처치,
    /// WishFulfilled = 소망 충족, ThreatSlain = 네임드 처치만.
    /// </summary>
    public enum NarrativeReactionRequirement
    {
        None = 0,
        AnyKnownReport = 1,
        RumorResolved = 2,
        WishFulfilled = 3,
        // 네임드 처치 행적에만 적용되는 행 (NAMED_THREAT_DESIGN §9.3, 피해자 감사). 기존 RumorResolved 행도 이 행적에 적용된다.
        ThreatSlain = 4,
    }

    /// <summary>NPC 대응 방식 (§4.5).</summary>
    public enum NarrativeResponseKind
    {
        None = 0,
        Acknowledge = 1,
        Thank = 2,
        AskMore = 3,
        IntroduceKnownContent = 4,
        Snub = 5,
    }

    /// <summary>관계 변화 수준 (§4.5). 실제 변화 = 기본 변화 × 관련도 배율, 0 쪽으로 버린다.</summary>
    public enum NarrativeRelationLevel
    {
        None = 0,
        Small = 1,
        Standard = 2,
        Cold = 3,
        Resentful = 4,
    }

    /// <summary>
    /// 후속 소개 상태 (§4.6, 5단계). 소개한 쪽 사건에 둔다. Pending = 보고 대사에 소개를 붙였고 표시를 기다림(재생 대상),
    /// Introduced = 소개한 NPC의 부탁 칸에 들어감, Cancelled = 표시 전에 대상이 더 이상 유효하지 않아 소개하지 않음.
    /// 이야기 줄기(사건) 하나에 한 번만 소개한다.
    /// </summary>
    public enum NarrativeFollowUpState
    {
        None = 0,
        Pending = 1,
        Introduced = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// 반응 행을 누가 쓸 수 있는가 (사용자 결정 2026-10-10). Owner = 부탁한 사람·소망 주인이 직접 반응할 때,
    /// Others = 소문으로 행적을 들은 NPC가 반응할 때, All = 둘 다.
    /// </summary>
    public enum NarrativeReactionAudience
    {
        All = 0,
        Owner = 1,
        Others = 2,
        // 그 행적의 피해자(네임드에게 다친 NPC)가 소문으로 들었을 때만 (NAMED_THREAT_DESIGN §9.3)
        Victim = 3,
    }

    /// <summary>
    /// 통합 콘텐츠 표 NarrativeContent의 한 행 (§2.1·§2.2). 불변 정의로 다루며 실행 기록은 NarrativeContentInstance에 둔다.
    /// 던전 소문: IntroText는 소문 주인의 부탁 대사, SignText는 입구 징후(자막 겸 NPC가 전하는 관찰), ResolvedText는 보고 때 원인을 밝히는 대사.
    /// 소망: IntroText는 소망을 털어놓는 대사, SignText는 비우고, ResolvedText는 소망 아이템을 받았을 때 주인의 대사.
    /// 개인 사연(최근 부상): IntroText만 쓴다({knownWoundSummary} 슬롯). 사건으로 저장하지 않는다.
    /// </summary>
    [Serializable]
    public sealed class NarrativeContentDefinition
    {
        public int ContentId;
        public string Name = string.Empty;
        public NarrativeContentKind Kind;
        public NarrativeTargetType TargetType;
        public int TargetId;
        public string IntroText = string.Empty;
        public string SignText = string.Empty;
        public string ResolvedText = string.Empty;
        public NarrativeGoalType GoalType;
        public int TargetCount = 1;
        public int StartConditionGroupId;
        public int Revision = 1;
        public bool Enabled = true;
    }

    /// <summary>
    /// 공통 반응 표 NarrativeReaction의 한 행 (§4.3). 콘텐츠 ID로 지정하지 않고 행적 종류·결과·성향·관계로 고른다.
    /// Note는 작성용 메모(행적 열)이며 판정에 쓰지 않는다.
    /// </summary>
    [Serializable]
    public sealed class NarrativeReactionDefinition
    {
        public int ReactionRuleId;
        public string Note = string.Empty;
        public NarrativeReactionRequirement Requirement;
        public ScenarioReactionKind Kind;
        public int BaseScore;
        public NarrativeReactionScoreProfile ScoreProfile;
        public NarrativeResponseKind Response;
        public NarrativeRelationLevel RelationLevel;
        public string Text = string.Empty;
        // 기본값(All)이면 쓰지 않는다. 그래서 이 열이 생기기 전의 정책 스냅샷 해시가 바뀌지 않는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NarrativeReactionAudience Audience;
    }

    /// <summary>작성 팩 (Resources/Narrative/NarrativeContent.json). 시트 두 탭을 한 팩으로만 저장한다.</summary>
    [Serializable]
    public sealed class NarrativeContentPack
    {
        public int SchemaVersion = 1;
        public List<NarrativeContentDefinition> Contents = new();
        public List<NarrativeReactionDefinition> Reactions = new();
    }

    /// <summary>사건 하나가 고정해 쓰는 콘텐츠 정의 스냅샷 (§5.2). 해시별로 한 번 저장하고 인스턴스가 참조한다.</summary>
    [Serializable]
    public sealed class NarrativeDefinitionSnapshot
    {
        public string Hash = string.Empty;
        public NarrativeContentDefinition Content = new();
    }

    /// <summary>
    /// 말을 걸었을 때 일반 대화보다 먼저 처리할 내러티브 대화 (§4.1·§4.3 선택 순서). 저장하지 않는 세션 값이다.
    /// </summary>
    public enum NarrativeConversationKind
    {
        None = 0,
        // 표시가 취소된 보고를 그대로 다시 보여 준다 (효과 없음)
        ReportReplay = 1,
        // 부탁 칸의 소문이 해결됐다 — 반응·의뢰 보답·칸 비우기·행적 소문
        Report = 2,
        // 부탁받기 전에 해결한 소문을 소문 주인에게 말한다 — 보답 없음, 같은 마을 ×1
        DirectTell = 3,
        // 부탁한 소문이 플레이어와 무관하게 끝났음을 알린다
        EndNotice = 4,
        // 소문으로 들은 행적에 반응한다 (최대 3개)
        DeedReactions = 5,
        // 선물 목록에서 소망 아이템을 건넸다 — 소모·충족·반응·의뢰 보답·행적 소문 (3단계)
        WishDelivery = 6,
    }

    public readonly struct NarrativeConversation
    {
        public readonly NarrativeConversationKind Kind;
        public readonly string InstanceId;

        public NarrativeConversation(NarrativeConversationKind kind, string instanceId)
        {
            Kind = kind;
            InstanceId = instanceId;
        }
    }

    /// <summary>
    /// 내러티브 대화 결과. 효과는 이미 저장됐고, NeedsDisplayCommit이면 실제 표시 뒤 표시 기록을 저장한다.
    /// 소문 반응에 후속 소개를 붙였으면 InstanceId는 소개한 쪽(행적) 사건, FollowUpInstanceId는 소개한 소문이다.
    /// </summary>
    public sealed class NarrativeDialogueResult
    {
        public NarrativeConversationKind Kind;
        public string InstanceId = string.Empty;
        public string Text = string.Empty;
        public bool NeedsDisplayCommit;
        public string FollowUpInstanceId = string.Empty;
    }

    /// <summary>
    /// 사건이 고정해 쓰는 반응·보답 정책 스냅샷 (§5.2). PolicyVersion은 점수식·관련도·보답 계산 코드의 버전이다.
    /// </summary>
    [Serializable]
    public sealed class NarrativePolicySnapshot
    {
        public string Hash = string.Empty;
        public int PolicyVersion;
        public List<NarrativeReactionDefinition> Reactions = new();
    }
}
