#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using ARPG.Tables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace ARPG.Npc.Narrative
{
    /// <summary>작성 팩 입력 경계. 지금은 게임에 포함된 번들만 읽는다 (NPC_NARRATIVE_CONTENT_DESIGN §5.1).</summary>
    public interface INarrativeContentSource
    {
        NarrativeContentPack? Read();
    }

    public sealed class BundledNarrativeContentSource : INarrativeContentSource
    {
        public const string ResourcePath = "Narrative/NarrativeContent";

        public NarrativeContentPack? Read()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException("Missing " + ResourcePath + " content.");
            return JsonConvert.DeserializeObject<NarrativeContentPack>(asset.text, NarrativeContentCatalog.PackSettings);
        }
    }

    /// <summary>
    /// DataManager가 소유하는 불변 콘텐츠 카탈로그 (§1, §5.1~§5.2). 검증된 정의와 현재 반응 정책의 해시 스냅샷을 담는다.
    /// 사건은 생성 시점의 스냅샷 해시를 고정하므로, 이 카탈로그가 바뀌어도 진행 중 사건의 정의·정책은 바뀌지 않는다.
    /// </summary>
    public sealed class NarrativeContentCatalog
    {
        // 던전 슬롯 선정 해시 채널 (DungeonLayout.Hash). 구 시나리오 선정과 같은 값을 쓴다.
        public const int DungeonRumorSelectChannel = 419;

        private static JsonSerializerSettings? _packSettings;
        private static JsonSerializerSettings? _hashSettings;
        private readonly List<NarrativeDefinitionSnapshot> _dungeonRumors = new();
        private readonly List<NarrativeDefinitionSnapshot> _wishes = new();
        private readonly List<NarrativeContentDefinition> _personalStories = new();
        // 네임드 위협 부탁·흔적·보고 문구 (NAMED_THREAT_DESIGN §11.3). NamedThreat 탭의 NarrativeContentId가 가리킨다.
        private readonly List<NarrativeDefinitionSnapshot> _namedThreats = new();
        // 선정 때마다 비우고 쓰는 후보 목록. 선정 사이에 상태를 남기지 않는다.
        private readonly List<NarrativeDefinitionSnapshot> _candidateScratch = new();
        private NarrativePolicySnapshot? _policy;

        /// <summary>번들 JSON 읽기·쓰기 설정. enum은 이름으로 저장한다.</summary>
        public static JsonSerializerSettings PackSettings
        {
            get
            {
                if (_packSettings == null)
                {
                    _packSettings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
                    _packSettings.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
                }
                return _packSettings;
            }
        }

        private static JsonSerializerSettings HashSettings
        {
            get
            {
                if (_hashSettings == null)
                {
                    _hashSettings = new JsonSerializerSettings { Formatting = Formatting.None };
                    _hashSettings.Converters.Add(new StringEnumConverter());
                }
                return _hashSettings;
            }
        }

        public NarrativePolicySnapshot? CurrentPolicy => _policy;
        public int DungeonRumorCount => _dungeonRumors.Count;
        public int WishCount => _wishes.Count;

        public void Initialize(INarrativeContentSource source, IReadOnlyList<DungeonTable> dungeons, Func<int, MonsterTable?> monster,
            Func<int, ItemTable?> item)
        {
            Reset();
            NarrativeContentPack? pack = source.Read();
            Validate(pack, dungeons, monster, item);
            List<NarrativeContentDefinition> contents = new(pack!.Contents);
            contents.Sort((a, b) => a.ContentId.CompareTo(b.ContentId));
            for (int i = 0; i < contents.Count; i++)
            {
                NarrativeContentDefinition content = contents[i];
                var snapshot = new NarrativeDefinitionSnapshot { Hash = ContentHash(content), Content = content };
                if (content.Kind == NarrativeContentKind.DungeonRumor)
                    _dungeonRumors.Add(snapshot);
                else if (content.Kind == NarrativeContentKind.Wish)
                    _wishes.Add(snapshot);
                else if (content.Kind == NarrativeContentKind.PersonalStory)
                    _personalStories.Add(content);
                else if (content.Kind == NarrativeContentKind.NamedThreat)
                    _namedThreats.Add(snapshot);
            }
            _policy = CreatePolicySnapshot(pack.Reactions);
        }

        public void Reset()
        {
            _dungeonRumors.Clear();
            _wishes.Clear();
            _personalStories.Clear();
            _namedThreats.Clear();
            _candidateScratch.Clear();
            _policy = null;
        }

        /// <summary>
        /// 이 대상 종류의 개인 사연 문구 (§2.2 PersonalStory, 4단계). 대상 종류마다 한 행만 허용한다. 없으면 null — 호출자는 코드 기본 문구를 쓴다.
        /// 개인 사연은 목표·영구 효과가 없어 사건으로 저장하지 않으므로 스냅샷 없이 정의를 읽기 전용으로 돌려준다 (§2.4).
        /// </summary>
        public NarrativeContentDefinition? FindPersonalStory(NarrativeTargetType target)
        {
            for (int i = 0; i < _personalStories.Count; i++)
                if (_personalStories[i].TargetType == target)
                    return _personalStories[i];
            return null;
        }

        /// <summary>네임드 위협 문구 정의 (읽기 전용 — 흔적 자막처럼 사건이 아닌 표시용). 없으면 null.</summary>
        public NarrativeContentDefinition? FindNamedThreatContent(int contentId)
        {
            for (int i = 0; i < _namedThreats.Count; i++)
                if (_namedThreats[i].Content.ContentId == contentId)
                    return _namedThreats[i].Content;
            return null;
        }

        /// <summary>네임드 위협 문구의 독립 복사본. 새 네임드 부탁 사건이 고정해 저장한다. 없으면 null.</summary>
        public NarrativeDefinitionSnapshot? CopyNamedThreat(int contentId)
        {
            for (int i = 0; i < _namedThreats.Count; i++)
                if (_namedThreats[i].Content.ContentId == contentId)
                    return Clone(_namedThreats[i]);
            return null;
        }

        /// <summary>소망 콘텐츠 정의 (읽기 전용, ContentId 오름차순). 사건에 넣을 때는 CopyWish로 복사본을 쓴다.</summary>
        public NarrativeContentDefinition GetWishContent(int index) => _wishes[index].Content;

        /// <summary>소망 콘텐츠의 독립 복사본. 새 소망 사건이 고정해 저장한다. 없으면 null.</summary>
        public NarrativeDefinitionSnapshot? CopyWish(int contentId)
        {
            for (int i = 0; i < _wishes.Count; i++)
                if (_wishes[i].Content.ContentId == contentId)
                    return Clone(_wishes[i]);
            return null;
        }

        /// <summary>
        /// 던전 세대 하나에 붙일 소문 콘텐츠를 고른다. 그 던전 표에 실제로 배치할 수 있는 콘텐츠 중에서 고정 무작위로 하나.
        /// 붙일지 말지(40%)는 호출자가 먼저 정한다. 결과는 저장 루트에 넣을 독립 복사본이다.
        /// </summary>
        public NarrativeDefinitionSnapshot? SelectDungeonRumor(int worldSeed, int villageId, int slotIndex, int generation, DungeonTable? table)
        {
            _candidateScratch.Clear();
            for (int i = 0; i < _dungeonRumors.Count; i++)
                if (IsApplicable(_dungeonRumors[i].Content, table) == true)
                    _candidateScratch.Add(_dungeonRumors[i]);
            if (_candidateScratch.Count == 0)
                return null;
            uint hash = Dungeon.DungeonLayout.Hash(worldSeed, villageId, slotIndex, generation, DungeonRumorSelectChannel);
            NarrativeDefinitionSnapshot selected = _candidateScratch[(int)(hash % (uint)_candidateScratch.Count)];
            _candidateScratch.Clear();
            return Clone(selected);
        }

        /// <summary>현재 반응 정책의 독립 복사본. 새 사건이 고정해 저장한다.</summary>
        public NarrativePolicySnapshot? CopyCurrentPolicy() => _policy == null ? null : Clone(_policy);

        /// <summary>
        /// 이 던전 표에 콘텐츠의 대상 몬스터를 실제로 둘 수 있는가 (§2.3). 보스이거나, 일반 몬스터 후보에 있고
        /// 방마다 최소 1마리가 보장될 때만 배치를 보장할 수 있다.
        /// </summary>
        public static bool IsApplicable(NarrativeContentDefinition content, DungeonTable? table)
        {
            if (table == null || content.Kind != NarrativeContentKind.DungeonRumor || content.TargetType != NarrativeTargetType.Monster)
                return false;
            if (table.BossTableId == content.TargetId)
                return true;
            return table.MonsterCountMin > 0 && table.MonsterTableIds != null && table.MonsterTableIds.Contains(content.TargetId);
        }

        public static string ContentHash(NarrativeContentDefinition content)
            => Sha256(JsonConvert.SerializeObject(content, HashSettings));

        public static string PolicyHash(NarrativePolicySnapshot policy)
            => Sha256(JsonConvert.SerializeObject(new { policy.PolicyVersion, policy.Reactions }, HashSettings));

        public static NarrativePolicySnapshot CreatePolicySnapshot(List<NarrativeReactionDefinition> reactions)
        {
            List<NarrativeReactionDefinition> sorted = new(reactions);
            sorted.Sort((a, b) => a.ReactionRuleId.CompareTo(b.ReactionRuleId));
            var policy = new NarrativePolicySnapshot { PolicyVersion = NarrativeReactionRules.PolicyVersion, Reactions = sorted };
            policy.Hash = PolicyHash(policy);
            return policy;
        }

        /// <summary>
        /// 팩 전체 검증 (§5.2). 하나라도 실패하면 예외 — 다운로드는 기존 파일을 보존하고, 런타임은 카탈로그를 비운다.
        /// 오류에는 표·행 ID·열을 적는다. 던전 소문은 몬스터 표·던전 표로, 소망은 아이템 표로 대상을 확인한다.
        /// </summary>
        public static void Validate(NarrativeContentPack? pack, IReadOnlyList<DungeonTable> dungeons, Func<int, MonsterTable?> monster,
            Func<int, ItemTable?> item)
        {
            Require(pack != null && pack.SchemaVersion == 1 && pack.Contents != null && pack.Reactions != null,
                "NarrativeContentPack: unsupported schema or missing lists.");
            Require(dungeons != null, "NarrativeContentPack: DungeonTable reference is missing.");
            var ids = new HashSet<int>();
            var storyTargets = new HashSet<NarrativeTargetType>();
            int rumors = 0;
            int wishes = 0;
            int namedThreats = 0;
            for (int i = 0; i < pack!.Contents.Count; i++)
            {
                NarrativeContentDefinition c = pack.Contents[i];
                Require(c != null, "NarrativeContent row " + (i + 1) + ": empty row.");
                string where = "NarrativeContent ContentId=" + c.ContentId;
                Require(ids.Add(c.ContentId), where + ": duplicate ContentId.");
                ValidateContentShape(c, where);
                if (c.Kind == NarrativeContentKind.DungeonRumor)
                {
                    Require(monster(c.TargetId) != null, where + " TargetId: unknown MonsterTable id " + c.TargetId + ".");
                    bool placeable = false;
                    for (int t = 0; t < dungeons!.Count && placeable == false; t++)
                        placeable = IsApplicable(c, dungeons[t]);
                    Require(placeable, where + " TargetId: no DungeonTable can place monster " + c.TargetId + ".");
                    rumors++;
                }
                else if (c.Kind == NarrativeContentKind.PersonalStory)
                {
                    // 같은 대상 종류의 사연이 둘이면 어느 문구를 쓸지 정할 수 없다.
                    Require(storyTargets.Add(c.TargetType), where + " TargetType: only one PersonalStory row per target type (" + c.TargetType + ").");
                }
                else if (c.Kind == NarrativeContentKind.NamedThreat)
                {
                    // 템플릿 연결(NamedThreat 탭의 NarrativeContentId)은 표 다운로드의 ValidateNamedThreatSetup이 확인한다.
                    namedThreats++;
                }
                else
                {
                    // 없는 아이템을 소망하지 않게 한다: 표에 있고, 상점(BasePrice)이나 드랍(DropRate)으로 얻을 수 있어야 한다 (QUEST §8.2).
                    ItemTable? target = item(c.TargetId);
                    Require(target != null, where + " TargetId: unknown ItemTable id " + c.TargetId + ".");
                    Require(target.BasePrice > 0 || target.DropRate > 0,
                        where + " TargetId: item " + c.TargetId + " cannot be obtained (BasePrice and DropRate are 0).");
                    wishes++;
                }
            }
            Require(rumors > 0, "NarrativeContent: at least one DungeonRumor row is required.");

            var ruleIds = new HashSet<int>();
            for (int i = 0; i < pack.Reactions.Count; i++)
            {
                NarrativeReactionDefinition r = pack.Reactions[i];
                Require(r != null, "NarrativeReaction row " + (i + 1) + ": empty row.");
                string where = "NarrativeReaction ReactionRuleId=" + r.ReactionRuleId;
                Require(ruleIds.Add(r.ReactionRuleId), where + ": duplicate ReactionRuleId.");
                ValidateReactionShape(r, where);
            }
            // 로드 검증(IsSupported)과 같은 기준: 소망 콘텐츠나 소망 반응 행이 있으면 소망 기본 후보도 요구한다.
            RequireSafeDefaults(pack.Reactions, wishes > 0 || HasRequirement(pack.Reactions, NarrativeReactionRequirement.WishFulfilled),
                namedThreats > 0 || HasRequirement(pack.Reactions, NarrativeReactionRequirement.ThreatSlain));
        }

        /// <summary>저장된 정의 스냅샷이 이 코드로 실행 가능한가 (로드 검증, 표 조회 없음).</summary>
        public static bool IsSupported(NarrativeDefinitionSnapshot? snapshot)
        {
            try
            {
                if (snapshot == null || snapshot.Content == null || snapshot.Hash != ContentHash(snapshot.Content))
                    return false;
                ValidateContentShape(snapshot.Content, "snapshot");
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>저장된 정책 스냅샷이 이 코드로 실행 가능한가 (로드 검증).</summary>
        public static bool IsSupported(NarrativePolicySnapshot? policy)
        {
            try
            {
                if (policy == null || policy.Reactions == null || policy.PolicyVersion != NarrativeReactionRules.PolicyVersion ||
                    policy.Hash != PolicyHash(policy))
                    return false;
                var ids = new HashSet<int>();
                for (int i = 0; i < policy.Reactions.Count; i++)
                {
                    NarrativeReactionDefinition r = policy.Reactions[i];
                    if (r == null || ids.Add(r.ReactionRuleId) == false)
                        return false;
                    ValidateReactionShape(r, "snapshot");
                }
                // 소망 열이 생기기 전 정책에는 소망 행이 없다. 소망 행이 있는 정책만 소망 기본 후보를 요구한다.
                RequireSafeDefaults(policy.Reactions, HasRequirement(policy.Reactions, NarrativeReactionRequirement.WishFulfilled),
                    HasRequirement(policy.Reactions, NarrativeReactionRequirement.ThreatSlain));
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        private static void ValidateContentShape(NarrativeContentDefinition c, string where)
        {
            Require(c.ContentId > 0, where + " ContentId: must be positive.");
            Require(string.IsNullOrWhiteSpace(c.Name) == false, where + " Name: required.");
            Require(c.Revision >= 1, where + " Revision: must be 1 or more.");
            Require(c.Enabled == true, where + " Enabled: disabled rows are excluded from the deployed pack.");
            Require(Enum.IsDefined(typeof(NarrativeContentKind), c.Kind) && Enum.IsDefined(typeof(NarrativeTargetType), c.TargetType) &&
                    Enum.IsDefined(typeof(NarrativeGoalType), c.GoalType), where + ": undefined enum value.");
            Require(c.TargetCount == 1, where + " TargetCount: supports 1 only.");
            Require(c.StartConditionGroupId == 0, where + " StartConditionGroupId: condition groups are not supported yet.");
            Require(string.IsNullOrWhiteSpace(c.IntroText) == false, where + " IntroText: required.");
            switch (c.Kind)
            {
                case NarrativeContentKind.DungeonRumor:
                    Require(c.TargetType == NarrativeTargetType.Monster && c.TargetId > 0, where + " TargetType/TargetId: DungeonRumor needs a Monster id.");
                    Require(c.GoalType == NarrativeGoalType.Default, where + " GoalType: DungeonRumor supports Default (boss kill) only.");
                    Require(string.IsNullOrWhiteSpace(c.SignText) == false, where + " SignText: required for DungeonRumor.");
                    Require(string.IsNullOrWhiteSpace(c.ResolvedText) == false, where + " ResolvedText: required for DungeonRumor.");
                    RequireSlots(c.IntroText, LocationSlot, where + " IntroText");
                    RequireSlots(c.ResolvedText, LocationSlot, where + " ResolvedText");
                    RequireSlots(c.SignText, null, where + " SignText");
                    break;
                case NarrativeContentKind.Wish:
                    // 첫 소망 실행은 ItemGift(아이템을 소망 주인에게 건넴)만 지원한다 (§2.2). Default = DeliverToGiver.
                    Require(c.TargetType == NarrativeTargetType.Item && c.TargetId > 0, where + " TargetType/TargetId: Wish needs an Item id.");
                    Require(c.GoalType == NarrativeGoalType.Default || c.GoalType == NarrativeGoalType.DeliverToGiver,
                        where + " GoalType: Wish supports Default/DeliverToGiver only.");
                    Require(string.IsNullOrWhiteSpace(c.SignText) == true, where + " SignText: must be empty for Wish.");
                    Require(string.IsNullOrWhiteSpace(c.ResolvedText) == false, where + " ResolvedText: required for Wish.");
                    RequireSlots(c.IntroText, TargetItemNameSlot, where + " IntroText");
                    RequireSlots(c.ResolvedText, TargetItemNameSlot, where + " ResolvedText");
                    break;
                case NarrativeContentKind.NamedThreat:
                    // 네임드 부탁 (NAMED_THREAT_DESIGN §11.3): TargetId = NamedThreat 탭 템플릿 Id, 목표 = 플레이어 측 처치.
                    // IntroText = 부탁, SignText = 흔적 자막(슬롯 없음), ResolvedText = 보고 대사. 슬롯은 {location}·{threatTitle}.
                    Require(c.TargetType == NarrativeTargetType.NamedThreat && c.TargetId > 0,
                        where + " TargetType/TargetId: NamedThreat needs a NamedThreat template id.");
                    Require(c.GoalType == NarrativeGoalType.Default || c.GoalType == NarrativeGoalType.DefeatTarget,
                        where + " GoalType: NamedThreat supports Default/DefeatTarget only.");
                    Require(string.IsNullOrWhiteSpace(c.SignText) == false, where + " SignText: required for NamedThreat.");
                    Require(string.IsNullOrWhiteSpace(c.ResolvedText) == false, where + " ResolvedText: required for NamedThreat.");
                    RequireSlotsAny(c.IntroText, NamedThreatSlots, where + " IntroText");
                    RequireSlotsAny(c.ResolvedText, NamedThreatSlots, where + " ResolvedText");
                    RequireSlots(c.SignText, null, where + " SignText");
                    break;
                case NarrativeContentKind.PersonalStory:
                    // 첫 사연은 그 NPC의 최근 부상 하나다 (4단계). TargetId 0 = 말하는 NPC 자신의 최신 기록. 목표·후속이 없어 다른 문구 칸은 비운다.
                    Require(c.TargetType == NarrativeTargetType.RecentWound && c.TargetId == 0,
                        where + " TargetType/TargetId: PersonalStory supports RecentWound with TargetId 0 only.");
                    Require(c.GoalType == NarrativeGoalType.Default, where + " GoalType: PersonalStory supports Default only.");
                    Require(string.IsNullOrWhiteSpace(c.SignText) == true && string.IsNullOrWhiteSpace(c.ResolvedText) == true,
                        where + " SignText/ResolvedText: must be empty for PersonalStory.");
                    RequireSlots(c.IntroText, KnownWoundSummarySlot, where + " IntroText");
                    break;
                case NarrativeContentKind.Objective:
                    // 단기 목표 부탁 (NPC_OBJECTIVE_DESIGN §9.2): 표(NarrativeContent 시트)에 행이 없다. 부탁 대사가 표시될 때
                    // NpcObjective 표의 문구를 사실로 채워 코드에서 만든 스냅샷이다 — 그래서 슬롯이 남지 않는다. 목표 = 플레이어 측 보스 처치.
                    Require(c.TargetType == NarrativeTargetType.None && c.TargetId == 0,
                        where + " TargetType/TargetId: Objective has no table target.");
                    Require(c.GoalType == NarrativeGoalType.DefeatTarget, where + " GoalType: Objective supports DefeatTarget only.");
                    Require(string.IsNullOrWhiteSpace(c.SignText) == true, where + " SignText: must be empty for Objective.");
                    Require(string.IsNullOrWhiteSpace(c.ResolvedText) == false, where + " ResolvedText: required for Objective.");
                    RequireSlots(c.IntroText, null, where + " IntroText");
                    RequireSlots(c.ResolvedText, null, where + " ResolvedText");
                    break;
                default:
                    throw new InvalidOperationException(where + " Kind: " + c.Kind + " is not executable yet.");
            }
        }

        private static void ValidateReactionShape(NarrativeReactionDefinition r, string where)
        {
            Require(r.ReactionRuleId > 0, where + ": ReactionRuleId must be positive.");
            Require(Enum.IsDefined(typeof(NarrativeReactionRequirement), r.Requirement) && Enum.IsDefined(typeof(ScenarioReactionKind), r.Kind) &&
                    Enum.IsDefined(typeof(NarrativeReactionScoreProfile), r.ScoreProfile) && Enum.IsDefined(typeof(NarrativeResponseKind), r.Response) &&
                    Enum.IsDefined(typeof(NarrativeRelationLevel), r.RelationLevel) && Enum.IsDefined(typeof(NarrativeReactionAudience), r.Audience),
                    where + ": undefined enum value.");
            Require(r.Requirement != NarrativeReactionRequirement.None, where + " Requirement: required.");
            Require(r.ScoreProfile != NarrativeReactionScoreProfile.None, where + " ScoreProfile: required.");
            Require(r.Response != NarrativeResponseKind.None, where + " Response: required.");
            Require(r.BaseScore >= -100 && r.BaseScore <= 100, where + " BaseScore: must be within -100..100.");
            Require(string.IsNullOrWhiteSpace(r.Text) == false, where + " Text: required.");
            RequireSlots(r.Text, null, where + " Text");
            bool positive = r.RelationLevel == NarrativeRelationLevel.Small || r.RelationLevel == NarrativeRelationLevel.Standard;
            bool negative = r.RelationLevel == NarrativeRelationLevel.Cold || r.RelationLevel == NarrativeRelationLevel.Resentful;
            switch (r.Response)
            {
                case NarrativeResponseKind.Thank:
                    Require(positive, where + " RelationLevel: Thank needs Small or Standard.");
                    break;
                case NarrativeResponseKind.Snub:
                    Require(negative, where + " RelationLevel: Snub needs Cold or Resentful.");
                    break;
                default:
                    Require(r.RelationLevel == NarrativeRelationLevel.None, where + " RelationLevel: " + r.Response + " needs None.");
                    break;
            }
        }

        // 행적 종류와 반응하는 쪽(부탁한 사람·소망 주인 / 소문으로 들은 NPC)마다 필터에 걸리지 않는 후보가 하나는 있어야
        // 반응을 항상 고를 수 있다 (§4.3). 던전 클리어는 부탁한 사람이 없어 들은 쪽만 본다.
        private static void RequireSafeDefaults(List<NarrativeReactionDefinition> reactions, bool wishes, bool namedThreats)
        {
            RequireSafeDefault(reactions, PlayerDeedKind.RumorResolved, true);
            RequireSafeDefault(reactions, PlayerDeedKind.RumorResolved, false);
            RequireSafeDefault(reactions, PlayerDeedKind.DungeonCleared, false);
            if (wishes == true)
            {
                RequireSafeDefault(reactions, PlayerDeedKind.WishFulfilled, true);
                RequireSafeDefault(reactions, PlayerDeedKind.WishFulfilled, false);
            }
            if (namedThreats == true)
            {
                RequireSafeDefault(reactions, PlayerDeedKind.ThreatSlain, true);
                RequireSafeDefault(reactions, PlayerDeedKind.ThreatSlain, false);
            }
        }

        private static void RequireSafeDefault(List<NarrativeReactionDefinition> reactions, PlayerDeedKind deed, bool owner)
        {
            for (int i = 0; i < reactions.Count; i++)
                if (NarrativeReactionRules.Applies(reactions[i].Requirement, deed) && NarrativeReactionRules.IsUnfiltered(reactions[i].Kind) &&
                    NarrativeReactionRules.MatchesAudience(reactions[i].Audience, owner))
                    return;
            throw new InvalidOperationException("NarrativeReaction: no unfiltered reaction for " + deed + (owner ? " (owner)." : " (others)."));
        }

        private static bool HasRequirement(List<NarrativeReactionDefinition> reactions, NarrativeReactionRequirement requirement)
        {
            for (int i = 0; i < reactions.Count; i++)
                if (reactions[i] != null && reactions[i].Requirement == requirement)
                    return true;
            return false;
        }

        // 지원 문구 슬롯은 유형마다 하나다: 던전 소문 {location}, 소망 {targetItemName}, 최근 부상 사연 {knownWoundSummary}.
        // 정의만 보고 숨겨진 대상 이름을 치환하지 않는다 (§3.2). 소망은 NPC가 원하는 아이템을 스스로 말하므로 아이템 이름을 쓴다.
        private const string LocationSlot = "location";
        private const string TargetItemNameSlot = "targetItemName";
        private const string KnownWoundSummarySlot = "knownWoundSummary";
        // 네임드 부탁·보고 문구 슬롯 (NAMED_THREAT_DESIGN §11.3): {location} = 영역 위치, {threatTitle} = 칭호
        private static readonly string[] NamedThreatSlots = { LocationSlot, "threatTitle" };

        /// <summary>최근 부상 사연 문구의 슬롯. 말하는 NPC가 실제로 겪은 최신 부상 요약으로 바꾼다 (DialogueTopicSelector).</summary>
        public const string KnownWoundSummaryToken = "{" + KnownWoundSummarySlot + "}";

        private static void RequireSlots(string text, string? allowedSlot, string where)
        {
            for (int i = 0; i < text.Length; i++)
            {
                Require(text[i] != '}', where + ": stray '}'.");
                if (text[i] != '{')
                    continue;
                int close = text.IndexOf('}', i + 1);
                Require(close > i, where + ": unclosed slot.");
                string slot = text.Substring(i + 1, close - i - 1);
                Require(allowedSlot != null && slot == allowedSlot, where + ": unsupported slot {" + slot + "}.");
                i = close;
            }
        }

        // RequireSlots의 여러 슬롯 허용판. allowed에 없는 슬롯이 있으면 실패한다.
        private static void RequireSlotsAny(string text, string[] allowed, string where)
        {
            for (int i = 0; i < text.Length; i++)
            {
                Require(text[i] != '}', where + ": stray '}'.");
                if (text[i] != '{')
                    continue;
                int close = text.IndexOf('}', i + 1);
                Require(close > i, where + ": unclosed slot.");
                string slot = text.Substring(i + 1, close - i - 1);
                Require(Array.IndexOf(allowed, slot) >= 0, where + ": unsupported slot {" + slot + "}.");
                i = close;
            }
        }

        private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, HashSettings), HashSettings)!;

        private static string Sha256(string json)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        }

        private static void Require([DoesNotReturnIf(false)] bool condition, string message)
        {
            if (condition == false)
                throw new InvalidOperationException(message);
        }
    }
}
