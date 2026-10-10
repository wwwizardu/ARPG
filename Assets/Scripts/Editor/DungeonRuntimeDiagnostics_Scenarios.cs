using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Item;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Skill.Combat;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 행적·반응 진단 (NPC_NARRATIVE_CONTENT_DESIGN §8 1~4단계). 작성 팩(시트 두 탭), 반응 점수식, 그리고 실제 원장·저장
    /// 경로를 거치는 흐름(소문 주인 → 부탁 → 보스 처치 → 보고 → 소문 전파 → 반응, 소망 털어놓기 → 아이템 전달 → 반응)을
    /// 격리된 소유자 그래프에서 검사한다. 플레이어 세이브와 다른 Unity 프로젝트에는 쓰지 않는다. Temp 아래 자기 폴더만 쓰고 지운다.
    /// </summary>
    public static partial class DungeonRuntimeDiagnostics
    {
        private static readonly string[] _narrativeFailurePoints = { "serialize", "part-write", "after-part", "before-manifest" };

        [MenuItem("ARPG/Dungeon/Run Scenario Diagnostics")]
        public static async void RunScenarios()
        {
            var report = new StringBuilder();
            try { await ValidateScenarios(report); report.AppendLine("PASS: all narrative content diagnostics."); }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex); }
            Debug.Log(report.ToString());
        }

        private static async Task ValidateScenarios(StringBuilder report)
        {
            Require(EditorApplication.isPlaying == false, "narrative fixture requires edit mode");
            var tables = JsonConvert.DeserializeObject<List<DungeonTable>>(File.ReadAllText("Assets/_BinaryData/TableData/DungeonTable.bytes"));
            var monsters = JsonConvert.DeserializeObject<List<MonsterTable>>(File.ReadAllText("Assets/_BinaryData/TableData/MonsterTable.bytes"));
            var items = JsonConvert.DeserializeObject<List<ItemTable>>(File.ReadAllText("Assets/_BinaryData/TableData/ItemTable.bytes"));
            NarrativeContentPack pack = new BundledNarrativeContentSource().Read();
            ValidateNarrativeSheetContract(pack, tables, monsters, items, report);
            ValidateNarrativeCatalog(pack, tables, monsters, items, report);
            ValidateReactionRules(pack, report);
            var context = new NpcDialogueContext { NpcBlock = "테스트 NPC", SituationBlock = "숨겨진 외부 몬스터 경고" };
            var topic = new DialogueTopic { Type = DialogueTopicType.Greeting, BaseLine = "안녕하세요." };
            Require(NpcNarrativeService.BuildDialoguePrompt(context, AffinityTier.Stranger, PlayerRelation.Default, topic).Contains("숨겨진") == false,
                "unselected threat context cannot enter general dialogue");

            using var fixture = new NarrativeDiagnosticFixture(tables, monsters, items);
            try
            {
                report.AppendLine(ARPG.Editor.NpcSaveSnapshotDiagnostics.VerifyGroupedSnapshotContracts());
                await ValidateNarrativeFlow(fixture, report);
                await ValidateFollowUps(fixture, report);
                ValidateRecentWoundStory(fixture, report);
            }
            finally { await fixture.Data.WaitForSaveIdleAsync(); }
        }

        // ── 작성 팩 (§5.1·§5.2) ──

        private static void ValidateNarrativeSheetContract(NarrativeContentPack pack, List<DungeonTable> tables,
            List<MonsterTable> monsters, List<ItemTable> items, StringBuilder report)
        {
            NarrativeSheetTsv(pack, ARPG.Editor.DownloadTables.NarrativeContentColumns, ARPG.Editor.DownloadTables.NarrativeReactionColumns,
                out string contents, out string reactions);
            var parsed = ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents + "\r\n\t\r\n", reactions);
            Require(JsonConvert.SerializeObject(parsed) == JsonConvert.SerializeObject(pack), "sheet roundtrip preserves every content/reaction field");
            ARPG.Editor.DownloadTables.ValidateNarrativePack(parsed, tables, monsters, items);
            // Audience 빈칸은 All이다 (이 열이 생기기 전 행과 같은 동작).
            Require(JsonConvert.SerializeObject(ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents, reactions.Replace("\"All\"", "\"\""))) ==
                JsonConvert.SerializeObject(pack), "blank Audience cells read as All");

            var reordered = (string[])ARPG.Editor.DownloadTables.NarrativeContentColumns.Clone();
            Array.Reverse(reordered);
            NarrativeSheetTsv(pack, reordered, ARPG.Editor.DownloadTables.NarrativeReactionColumns, out string reversed, out _);
            Require(JsonConvert.SerializeObject(ARPG.Editor.DownloadTables.ParseNarrativeSheets(reversed, reactions)) == JsonConvert.SerializeObject(pack),
                "header-based parsing supports reordered columns");

            var multiline = Copy(pack);
            multiline.Contents[0].IntroText = "{location} 소리 \"확인\"\r\n다음 줄\t한글";
            NarrativeSheetTsv(multiline, ARPG.Editor.DownloadTables.NarrativeContentColumns, ARPG.Editor.DownloadTables.NarrativeReactionColumns,
                out string quoted, out _);
            Require(ARPG.Editor.DownloadTables.ParseNarrativeSheets(quoted, reactions).Contents[0].IntroText == multiline.Contents[0].IntroText,
                "Google TSV quoted multiline/tab/Korean dialogue survives");

            // 선택 열은 빈칸이면 기본값, Enabled=FALSE 행은 배포 팩에서 빠진다 (§2.2).
            var defaults = Copy(pack);
            NarrativeSheetTsv(defaults, ARPG.Editor.DownloadTables.NarrativeContentColumns, ARPG.Editor.DownloadTables.NarrativeReactionColumns,
                out string withDefaults, out _, blankOptional: true);
            var parsedDefaults = ARPG.Editor.DownloadTables.ParseNarrativeSheets(withDefaults, reactions);
            Require(parsedDefaults.Contents.TrueForAll(c => c.GoalType == NarrativeGoalType.Default && c.TargetCount == 1 &&
                c.StartConditionGroupId == 0 && c.Revision == 1 && c.Enabled), "blank optional cells use the documented defaults");
            var disabled = Copy(pack);
            disabled.Contents[0].Enabled = false;
            NarrativeSheetTsv(disabled, ARPG.Editor.DownloadTables.NarrativeContentColumns, ARPG.Editor.DownloadTables.NarrativeReactionColumns,
                out string withDisabled, out _);
            Require(ARPG.Editor.DownloadTables.ParseNarrativeSheets(withDisabled, reactions).Contents.Count == pack.Contents.Count - 1,
                "disabled future rows are excluded from the deployed pack");

            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets("<html>access denied</html>", reactions), "HTTP/auth error body");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents.Replace("\tName\t", "\tKind\t"), reactions), "duplicate/missing headers");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents + "\r\n\"unclosed", reactions), "malformed quoted TSV");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents.Replace("\"DungeonRumor\"", "\"DungeonRumour\""), reactions), "undefined enum name");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents.Replace("\"DungeonRumor\"", "\"1\""), reactions), "numeric enum value");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(contents, reactions.Replace("\"Curious\"", "\"\"")), "blank required reaction cell");
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ParseNarrativeSheets(
                contents.Substring(0, contents.IndexOf("\r\n", StringComparison.Ordinal)), reactions), "header-only content sheet");

            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents.Add(p.Contents[0]), "duplicate ContentId");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].Kind = NarrativeContentKind.PersonalStory, "content kind without an executor");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].Kind = NarrativeContentKind.Wish, "wish row with a monster target");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].TargetId = 999999, "unknown monster reference");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].GoalType = NarrativeGoalType.DefeatTarget, "unsupported goal");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].TargetCount = 2, "unsupported target count");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].IntroText = "{targetName} 이야기", "unsupported text slot");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].SignText = string.Empty, "missing entrance sign");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].ResolvedText = string.Empty, "missing resolved line");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].IntroText = "{targetItemName} 이야기", "item slot in a dungeon rumor");
            // 소망 (3단계): 아이템 대상·얻을 방법·빈 징후·{targetItemName} 슬롯만·전달 목표, 주인/이웃 기본 반응
            // 지금 아이템 표에는 상점가·드랍률이 둘 다 0인 행이 없어서, 그런 행 하나를 더한 복사본 표로 거절을 본다.
            var withUnobtainable = new List<ItemTable>(items) { new ItemTable { Id = 999998, Name = "얻을 수 없는 물건" } };
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).TargetId = 999999, "unknown wish item");
            ExpectInvalidPack(pack, tables, monsters, withUnobtainable, p => WishRow(p).TargetId = 999998, "wish item that cannot be obtained");
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).TargetType = NarrativeTargetType.Monster, "wish with a monster target type");
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).GoalType = NarrativeGoalType.DefeatTarget, "unsupported wish goal");
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).SignText = "징후", "wish with an entrance sign");
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).IntroText = "{location}의 물건", "location slot in a wish");
            ExpectInvalidPack(pack, tables, monsters, items, p => WishRow(p).ResolvedText = string.Empty, "wish without the owner's line");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.RemoveAll(r => r.Requirement == NarrativeReactionRequirement.WishFulfilled &&
                r.Audience == NarrativeReactionAudience.Owner), "wish without an owner reaction");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.RemoveAll(r => r.Requirement == NarrativeReactionRequirement.WishFulfilled &&
                r.Audience == NarrativeReactionAudience.Others && NarrativeReactionRules.IsUnfiltered(r.Kind)), "wish without a safe neighbor reaction");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.ForEach(r =>
                { if (r.Requirement != NarrativeReactionRequirement.WishFulfilled) r.Audience = NarrativeReactionAudience.Owner; }),
                "dungeon deeds without a reaction for NPCs who only heard the rumor");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions[0].Audience = (NarrativeReactionAudience)9, "undefined audience");
            // 개인 사연 (4단계): 최근 부상·대상 ID 0·문구 한 칸·{knownWoundSummary} 슬롯만·대상 종류당 한 행
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).TargetId = 5, "recent-wound story pointing at an id");
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).TargetType = NarrativeTargetType.None, "personal story without a supported target");
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).SignText = "징후", "personal story with an entrance sign");
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).ResolvedText = "해결", "personal story with a resolved line");
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).IntroText = "{location}에서 다쳤소.", "location slot in a personal story");
            ExpectInvalidPack(pack, tables, monsters, items, p => StoryRow(p).GoalType = NarrativeGoalType.Discuss, "unsupported personal story goal");
            ExpectInvalidPack(pack, tables, monsters, items, p =>
            {
                var copy = JsonConvert.DeserializeObject<NarrativeContentDefinition>(JsonConvert.SerializeObject(StoryRow(p)));
                copy.ContentId = 3002;
                p.Contents.Add(copy);
            }, "two recent-wound stories");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Contents[0].IntroText = "{knownWoundSummary} 소리", "wound slot in a dungeon rumor");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.Find(r => r.Response == NarrativeResponseKind.Thank).RelationLevel = NarrativeRelationLevel.None,
                "Thank without Small/Standard");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.Find(r => r.Response == NarrativeResponseKind.Snub).RelationLevel = NarrativeRelationLevel.Small,
                "Snub without Cold/Resentful");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.Add(p.Reactions[0]), "duplicate ReactionRuleId");
            ExpectInvalidPack(pack, tables, monsters, items, p => p.Reactions.RemoveAll(r => NarrativeReactionRules.IsUnfiltered(r.Kind)),
                "no safe unfiltered reaction");

            string directory = Path.GetFullPath(Path.Combine("Temp", "narrative-sheet-" + Guid.NewGuid().ToString("N")));
            string path = Path.Combine(directory, "NarrativeContent.json");
            try
            {
                ARPG.Editor.DownloadTables.WriteNarrativePack(path, pack);
                string before = File.ReadAllText(path);
                Require(before == File.ReadAllText("Assets/Resources/Narrative/NarrativeContent.json"), "bundled pack bytes equal the writer output");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.WriteNarrativePack(path, multiline), "failed replacement");
                Require(File.ReadAllText(path) == before && Directory.GetFiles(directory).Length == 1,
                    "failed atomic replacement keeps original and cleans staged file");
                ARPG.Editor.DownloadTables.WriteNarrativePack(path, multiline);
                Require(JsonConvert.DeserializeObject<NarrativeContentPack>(File.ReadAllText(path), NarrativeContentCatalog.PackSettings)
                    .Contents[0].IntroText == multiline.Contents[0].IntroText, "atomic replacement loads through runtime DTO");
            }
            finally { DeleteDiagnosticDirectory(directory); }
            report.AppendLine("PASS: narrative sheet contract (two tabs, reordered headers, quoted TSV, defaults incl. blank Audience, disabled rows), " +
                              "enum/reference/combination rejection incl. wish item/slot/audience rules and atomic pack replacement.");
        }

        private static NarrativeContentDefinition WishRow(NarrativeContentPack pack) => pack.Contents.Find(c => c.Kind == NarrativeContentKind.Wish);
        private static NarrativeContentDefinition StoryRow(NarrativeContentPack pack) => pack.Contents.Find(c => c.Kind == NarrativeContentKind.PersonalStory);

        private static void ValidateNarrativeCatalog(NarrativeContentPack pack, List<DungeonTable> tables, List<MonsterTable> monsters,
            List<ItemTable> items, StringBuilder report)
        {
            List<NarrativeContentDefinition> rumors = pack.Contents.FindAll(c => c.Kind == NarrativeContentKind.DungeonRumor);
            Require(rumors.Count == 3 && rumors.TrueForAll(c => string.IsNullOrWhiteSpace(c.SignText) == false &&
                string.IsNullOrWhiteSpace(c.ResolvedText) == false), "three dungeon rumors with entrance sign and resolved lines");
            NarrativeContentDefinition wishRow = WishRow(pack);
            ItemTable wishItem = wishRow != null ? items.Find(i => i.Id == wishRow.TargetId) : null;
            Require(wishRow != null && wishRow.ContentId == 2001 && wishRow.TargetType == NarrativeTargetType.Item &&
                wishItem != null && (wishItem.BasePrice > 0 || wishItem.DropRate > 0) && string.IsNullOrEmpty(wishRow.SignText) &&
                wishRow.IntroText.Contains("{targetItemName}"), "one obtainable ItemGift wish (2001) without an entrance sign");
            NarrativeContentDefinition storyRow = StoryRow(pack);
            Require(pack.Contents.Count == 5 && storyRow != null && storyRow.ContentId == 3001 && storyRow.TargetType == NarrativeTargetType.RecentWound &&
                storyRow.TargetId == 0 && storyRow.IntroText.Contains(NarrativeContentCatalog.KnownWoundSummaryToken) &&
                string.IsNullOrEmpty(storyRow.SignText) && string.IsNullOrEmpty(storyRow.ResolvedText),
                "one recent-wound personal story (3001) with only the {knownWoundSummary} line");
            Require(pack.Reactions.Count == 10 && pack.Reactions.FindAll(r => r.Requirement == NarrativeReactionRequirement.WishFulfilled).Count == 3,
                "ten common reactions (101~107 dungeon, 201~203 wish)");
            // Audience 기본값(All)은 쓰지 않으므로 이 열이 생기기 전에 저장된 정책 스냅샷의 해시가 그대로다.
            var legacyPolicy = NarrativeContentCatalog.CreatePolicySnapshot(pack.Reactions.FindAll(r => r.ReactionRuleId < 200));
            Require(JsonConvert.SerializeObject(legacyPolicy.Reactions).Contains("Audience") == false && NarrativeContentCatalog.IsSupported(legacyPolicy),
                "pre-Audience policy snapshots keep their hash and still load without wish rows");
            var catalog = new NarrativeContentCatalog();
            catalog.Initialize(new FixedNarrativeSource(pack), tables, id => monsters.Find(m => m.Id == id), id => items.Find(i => i.Id == id));
            Require(catalog.DungeonRumorCount == 3 && catalog.WishCount == 1 && NarrativeContentCatalog.IsSupported(catalog.CurrentPolicy) &&
                catalog.CurrentPolicy.PolicyVersion == NarrativeReactionRules.PolicyVersion, "catalog snapshots and current policy");
            Require(catalog.FindPersonalStory(NarrativeTargetType.RecentWound)?.ContentId == 3001 && catalog.FindPersonalStory(NarrativeTargetType.Item) == null,
                "personal story lookup by target type");
            NarrativeDefinitionSnapshot wishSnapshot = catalog.CopyWish(2001);
            Require(wishSnapshot != null && NarrativeContentCatalog.IsSupported(wishSnapshot) && ReferenceEquals(wishSnapshot.Content, catalog.GetWishContent(0)) == false &&
                catalog.CopyWish(2002) == null, "wish snapshot is an independent supported copy");
            int placements = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                var slots = DungeonLayout.CreateSlots(seed, 0);
                for (int c = 0; c < rumors.Count; c++)
                {
                    NarrativeContentDefinition content = rumors[c];
                    DungeonRecord layout = DungeonLayout.Generate(seed, 0, slots.Slots[0], tables[0]);
                    Require(NarrativeContentCatalog.IsApplicable(content, tables[0]) && DungeonManager.PlaceNarrativeTarget(layout, content),
                        "rumor monster placement for content " + content.ContentId);
                    Require(layout.Objects.Exists(o => (o.Kind == DungeonObjectKind.Monster || o.Kind == DungeonObjectKind.Boss) &&
                        o.TableId == content.TargetId) && DungeonLayout.Validate(layout, out _), "placed rumor monster keeps a valid layout");
                    placements++;
                }
                NarrativeDefinitionSnapshot a = catalog.SelectDungeonRumor(seed, 0, 0, 0, tables[0]);
                Require(JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(catalog.SelectDungeonRumor(seed, 0, 0, 0, tables[0])) &&
                    NarrativeContentCatalog.IsSupported(a), "deterministic content selection with a supported snapshot");
            }
            var tampered = Copy(catalog.SelectDungeonRumor(1, 0, 0, 0, tables[0]));
            tampered.Content.SignText += "!";
            Require(NarrativeContentCatalog.IsSupported(tampered) == false, "tampered definition snapshot rejected by hash");
            var oldPolicy = Copy(catalog.CurrentPolicy);
            oldPolicy.PolicyVersion = NarrativeReactionRules.PolicyVersion + 1;
            oldPolicy.Hash = NarrativeContentCatalog.PolicyHash(oldPolicy);
            Require(NarrativeContentCatalog.IsSupported(oldPolicy) == false, "unknown policy version rejected");
            report.AppendLine($"PASS: three rumor contents + one wish + one recent-wound story, {placements} real monster placements, deterministic selection, " +
                              "snapshot hash/version checks, legacy policy hash kept.");
        }

        // ── 반응 점수식 (§4.2~§4.5) ──

        private static void ValidateReactionRules(NarrativeContentPack pack, StringBuilder report)
        {
            List<NarrativeReactionDefinition> rules = NarrativeContentCatalog.CreatePolicySnapshot(pack.Reactions).Reactions;
            NarrativeReactionDefinition Rule(int id) => rules.Find(r => r.ReactionRuleId == id);
            NpcStatComponent Traits(int curiosity = 50, int friendliness = 50, int courage = 50, int pride = 50,
                int honesty = 50, int greed = 50, int loyalty = 50)
                => new NpcStatComponent { Curiosity = curiosity, Friendliness = friendliness, Courage = courage, Pride = pride,
                    Honesty = honesty, Greed = greed, Loyalty = loyalty, Patience = 50 };
            PlayerRelation Relation(int affinity, int trust) => new PlayerRelation { Affinity = affinity, Trust = trust, TrustCap = 100 };

            // §4.3 예시 가~라 (성향이 표에 없으면 50)
            var ga = new NarrativeReactionRules.Input(Traits(curiosity: 90, friendliness: 40, courage: 50), Relation(30, 20), false);
            Require(NarrativeReactionRules.Score(Rule(101), ga) == 18 && NarrativeReactionRules.Score(Rule(102), ga) == 11 &&
                NarrativeReactionRules.Score(Rule(104), ga) == 6 && NarrativeReactionRules.Score(Rule(107), ga) == 6 &&
                NarrativeReactionRules.Score(Rule(103), ga) == 4 && NarrativeReactionRules.Select(rules, PlayerDeedKind.RumorResolved, false, ga).ReactionRuleId == 101,
                "example 가: curious 18 > relieved 11 > concerned/suspicious 6 > impressed 4");
            var na = new NarrativeReactionRules.Input(Traits(curiosity: 30, friendliness: 85, courage: 40), Relation(60, 40), false);
            Require(NarrativeReactionRules.Score(Rule(102), na) == 23 && NarrativeReactionRules.Score(Rule(101), na) == 6 &&
                NarrativeReactionRules.Score(Rule(103), na) == 6 && NarrativeReactionRules.PassesFilter(ScenarioReactionKind.Envious, na) == false &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.RumorResolved, false, na).ReactionRuleId == 102,
                "example 나: relieved 23, friend tier cannot envy");
            var da = new NarrativeReactionRules.Input(Traits(curiosity: 40, friendliness: 30, courage: 50, pride: 85), Relation(10, 40), true);
            Require(NarrativeReactionRules.Score(Rule(105), da) == 11 && NarrativeReactionRules.Score(Rule(101), da) == 8 &&
                NarrativeReactionRules.Score(Rule(103), da) == 8 && NarrativeReactionRules.Score(Rule(102), da) == 7 &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.RumorResolved, false, da).ReactionRuleId == 105,
                "example 다: envious 11 with glory ambition");
            var ra = new NarrativeReactionRules.Input(Traits(), Relation(-40, 30), false);
            Require(NarrativeReactionRules.Score(Rule(106), ra) == 13 && NarrativeReactionRules.Score(Rule(101), ra) == 10 &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.RumorResolved, false, ra).ReactionRuleId == 106,
                "example 라: hostile 13 only below zero affinity");
            Require(NarrativeReactionRules.PassesFilter(ScenarioReactionKind.Hostile, ga) == false &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.DungeonCleared, false, ga).Requirement == NarrativeReactionRequirement.AnyKnownReport,
                "dungeon clear deeds use only AnyKnownReport rules; hostile filtered at positive affinity");
            var tie = new List<NarrativeReactionDefinition> { Rule(104), Rule(107) };
            Require(NarrativeReactionRules.Select(tie, PlayerDeedKind.RumorResolved, false, ga).ReactionRuleId == 104, "score tie picks the smaller rule id");

            // 소망 충족 (3단계): 주인은 Owner 행(201 감사)만, 소문으로 들은 이웃은 Others 행(202 흐뭇·203 질투)만 고른다.
            Require(NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.All, true) && NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.All, false) &&
                NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.Owner, true) && NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.Owner, false) == false &&
                NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.Others, false) && NarrativeReactionRules.MatchesAudience(NarrativeReactionAudience.Others, true) == false,
                "audience truth table");
            var loyal = new NarrativeReactionRules.Input(Traits(loyalty: 80), new PlayerRelation { Affinity = 35, Intimacy = 30, TrustCap = 100 }, false);
            Require(NarrativeReactionRules.Score(Rule(201), loyal) == 19, "grateful score = 10 + (loyalty 80 - 50) / 5 + intimacy 30 / 10");
            Require(NarrativeReactionRules.Select(rules, PlayerDeedKind.WishFulfilled, true, da).ReactionRuleId == 201 &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.WishFulfilled, true, ra).ReactionRuleId == 201,
                "the wish owner always answers with the owner row, even an envious or hostile NPC");
            Require(NarrativeReactionRules.Select(rules, PlayerDeedKind.WishFulfilled, false, na).ReactionRuleId == 202 &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.WishFulfilled, false, da).ReactionRuleId == 203 &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.WishFulfilled, false, ga).ReactionRuleId == 202,
                "neighbors pick only Others rows: warm 202, envious 203 below friend tier");
            Require(NarrativeReactionRules.Select(rules, PlayerDeedKind.RumorResolved, true, na).Audience == NarrativeReactionAudience.All &&
                NarrativeReactionRules.Select(rules, PlayerDeedKind.DungeonCleared, true, ga).Requirement == NarrativeReactionRequirement.AnyKnownReport,
                "dungeon rows (All) still serve both the requester and listeners");

            Require(NarrativeReactionRules.PromiseScore(Traits(honesty: 80, greed: 30), Relation(10, 0)) == 11 &&
                NarrativeReactionRules.PromiseScore(Traits(), Relation(10, 0)) == 1 &&
                NarrativeReactionRules.PromiseScore(Traits(), Relation(-40, 0)) == -4, "promise score examples 11 / 1 / -4");

            NarrativeReactionRules.RelationChange(NarrativeRelationLevel.Small, NarrativeRelevance.Requester, out int a1, out int t1, out int i1);
            NarrativeReactionRules.RelationChange(NarrativeRelationLevel.Small, NarrativeRelevance.OtherVillage, out int a2, out int t2, out int i2);
            NarrativeReactionRules.RelationChange(NarrativeRelationLevel.Resentful, NarrativeRelevance.SameVillage, out int a3, out _, out _);
            NarrativeReactionRules.RelationChange(NarrativeRelationLevel.Standard, NarrativeRelevance.Requester, out int a4, out int t4, out int i4);
            NarrativeReactionRules.RelationChange(NarrativeRelationLevel.Cold, NarrativeRelevance.Companion, out int a5, out _, out _);
            Require(a1 == 6 && t1 == 0 && i1 == 3 && a2 == 0 && t2 == 0 && i2 == 0 && a3 == -2 && a4 == 9 && t4 == 12 && i4 == 6 && a5 == -2,
                "relation change = level × relevance, truncated toward zero (Small×0.3 = 0)");
            Require(NarrativeReactionRules.Stronger(NarrativeRelevance.SameVillage, NarrativeRelevance.Companion) == NarrativeRelevance.Companion &&
                NarrativeReactionRules.Stronger(NarrativeRelevance.Victim, NarrativeRelevance.Companion) == NarrativeRelevance.Victim &&
                NarrativeReactionRules.Stronger(NarrativeRelevance.OtherVillage, NarrativeRelevance.SameVillage) == NarrativeRelevance.SameVillage,
                "largest relevance wins without multiplying");
            Require(NarrativeReactionRules.LimitConversationAffinity(15) == 9 && NarrativeReactionRules.LimitConversationAffinity(-7) == -4,
                "conversation affinity cap +9 / -4");
            var amounts = new HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                string id = NarrativeContentInstance.DungeonRumorId("dungeon-" + i);
                int amount = NarrativeReactionRules.RewardAmount(id);
                Require(amount >= 10 && amount <= 20 && amount == NarrativeReactionRules.RewardAmount(id), "reward 10..20 fixed by instance id");
                amounts.Add(amount);
            }
            Require(amounts.Count > 5 && NarrativeReactionRules.EffectiveResponse(NarrativeResponseKind.IntroduceKnownContent) == NarrativeResponseKind.AskMore &&
                NarrativeReactionRules.IsNegative(ScenarioReactionKind.Suspicious) && NarrativeReactionRules.IsNegative(ScenarioReactionKind.Concerned) == false,
                "reward spread, stage 1-2 safe follow-up, negative reaction set");
            report.AppendLine("PASS: reaction scores reproduce §4.3 examples 가~라, filters, ties, promise score, relevance multipliers, caps and reward range; " +
                              "wish owner/neighbor audiences (201 / 202·203).");
        }

        // ── 실제 원장·저장 흐름 (§4.1·§7.4·§7.5) ──

        private static async Task ValidateNarrativeFlow(NarrativeDiagnosticFixture fixture, StringBuilder report)
        {
            NpcManager npc = fixture.Npc;
            DataManager data = fixture.Data;
            DungeonManager dungeon = fixture.Ar.Dungeon;
            int seed = fixture.World.WorldSeed;
            DungeonTable table = fixture.Table;

            // 1. 소문 주인 — 주민 배치 전에는 고르지 않는다.
            DungeonSlot rumorSlot = fixture.Slot(fixture.RumorVillage, 0);
            Require(npc.EnsureDungeonNarrative(fixture.RumorVillage, rumorSlot, false, table) == false && data.Narrative.Selections.Count == 0,
                "no selection before villages are populated");
            npc.SetNarrativeOwnerSelectionReady();
            Require((bool)Invoke(dungeon, "RefreshNarrativeSelections") && data.Narrative.Selections.Count == fixture.TotalSlots,
                "one selection record per current dungeon generation");
            NarrativeContentInstance instance = npc.GetDungeonNarrative(rumorSlot.DungeonId);
            NarrativeContentDefinition content = npc.GetNarrativeContent(instance);
            int owner = fixture.ExpectedOwner(fixture.RumorVillage, rumorSlot, excludedCompanion: 13);
            Require(instance != null && content != null && instance.OwnerNpcId == owner && instance.IsOpen,
                "rumor dungeon owner from sorted living residents by world seed + dungeon id (companion excluded)");
            string rumorId = instance.InstanceId;
            NpcRumor ownerRumor = npc.GetSaveData(owner).Rumors.Find(r => r.Kind == NpcRumorKind.DungeonInfo && r.IsDiscovery && r.InstanceId == rumorId);
            Require(ownerRumor != null && ownerRumor.SourceNpcId < 0, "owner learns the entrance location and sign");
            DungeonSlot genericSlot = fixture.Slot(fixture.GenericVillage, 0);
            Require(npc.GetDungeonNarrative(genericSlot.DungeonId) == null &&
                fixture.World.NpcSaveDatas[30].Rumors.TrueForAll(r => r.IsDiscovery == false || r.VillageId != fixture.GenericVillage || r.SlotIndex != 0),
                "generic dungeon has no owner and nobody knows it");
            Require(npc.GetDungeonNarrative(fixture.Slot(fixture.VisitedVillage, 0).DungeonId) == null,
                "already visited interior keeps location rumors only even when the 40% roll passes");
            NarrativeContentInstance pending = npc.GetDungeonNarrative(fixture.Slot(fixture.EmptyVillage, 0).DungeonId);
            Require(pending != null && pending.OwnerNpcId < 0, "rumor dungeon without residents waits for an owner");
            fixture.AddResident(40, fixture.EmptyVillage, 50, 50);
            npc.RetryPendingRumorOwners(fixture.EmptyVillage);
            Require(npc.GetDungeonNarrative(pending.DungeonId).OwnerNpcId == 40, "a new resident becomes the pending owner");
            int attached = 0, rolls = 0;
            int channel = (int)typeof(NpcManager).GetField("DungeonRumorAttachChannel", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            for (int v = 0; v < 400; v++)
                for (int s = 0; s < 3; s++, rolls++)
                    if (DungeonLayout.Hash(seed, v, s, 0, channel) % 100u < NpcManager.DungeonRumorAttachPercent) attached++;
            Require(attached * 100 >= rolls * 35 && attached * 100 <= rolls * 45, $"content attach rate near 40% ({attached}/{rolls})");
            Require((bool)Invoke(dungeon, "RefreshNarrativeSelections") == false && npc.GetDungeonNarrative(rumorSlot.DungeonId).OwnerNpcId == owner,
                "same generation is never selected again; reload-stable owner");
            report.AppendLine($"PASS: owner selection after population, 40% attach ({attached}/{rolls}), generic/visited/pending owners, idempotent selection.");

            // 2. 부탁 화제·진행 확인·전해 들은 위치 소문
            int[] others = fixture.ResidentsExcept(fixture.RumorVillage, owner, 13);
            int listener = others[0], victim = others[1], extra = others[2];
            DialogueTopic request = npc.BuildNarrativeRequestTopic(owner, npc.GetSaveData(owner));
            Require(request != null && request.Type == DialogueTopicType.DungeonRequest && request.Pinned &&
                request.NarrativeInstanceId == rumorId && request.BaseLine.Contains("{location}") == false && request.BaseLine.Contains("이 마을"),
                "only the owner offers the request with a resolved location");
            Require(npc.BuildNarrativeRequestTopic(listener, npc.GetSaveData(listener))?.NarrativeInstanceId != rumorId,
                "non-owner never requests this rumor (it may own another slot's rumor)");
            float now = fixture.Ar.Time.CurrentGameTime;
            Require(npc.ReceiveRumor(npc.GetSaveData(listener), ownerRumor, owner, now), "location rumor spreads by exchange");
            var listenerTopics = new List<DialogueTopic>();
            DialogueTopicSelector.CollectAmbitionRumorTopics(listener, listenerTopics);
            Require(listenerTopics.Exists(t => t.Type == DialogueTopicType.DungeonRumor && t.BaseLine.Contains("더군") &&
                t.BaseLine.Contains(content.IntroText.Replace("{location}", string.Empty)) == false), "heard rumor tells location and sign only");
            var ownerTopics = new List<DialogueTopic>();
            DialogueTopicSelector.CollectAmbitionRumorTopics(owner, ownerTopics);
            Require(ownerTopics.Exists(t => t.Rumor != null && t.Rumor.InstanceId == rumorId) == false,
                "owner's own location topic is replaced by the request topic");
            ValidatePinnedRotation(owner, request);

            Require(await data.SaveDungeonTransactionAsync(SaveScope.All), "initial runtime checkpoint");
            long baseline = ReadManifest(fixture.SlotPath).Revision;
            foreach (string failure in _narrativeFailurePoints)
            {
                data.DiagnosticFailurePoint = failure;
                Require(await npc.CommitNarrativeRequestAsync(owner, rumorId) == false &&
                    npc.GetSaveData(owner).NarrativeRequest == null && npc.GetNarrativeInstance(rumorId).RequesterNpcId < 0 &&
                    ReadManifest(fixture.SlotPath).Revision == baseline, "request display failure rolls back slot and instance: " + failure);
            }
            data.DiagnosticFailurePoint = null;
            Require(await npc.CommitNarrativeRequestAsync(owner, rumorId) && npc.GetSaveData(owner).NarrativeRequest.InstanceId == rumorId &&
                npc.GetNarrativeInstance(rumorId).RequesterNpcId == owner, "displayed request fills the owner's single slot");
            object snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            Require(((NarrativeSaveRoot)Field(snapshot, "Narrative")).Instances.Find(i => i.InstanceId == rumorId).RequesterNpcId == owner &&
                ((Dictionary<int, NpcSaveData>)Field(snapshot, "Npcs"))[owner].NarrativeRequest.InstanceId == rumorId,
                "request slot and instance commit in one manifest revision");
            DialogueTopic progress = npc.BuildNarrativeRequestTopic(owner, npc.GetSaveData(owner));
            Require(progress != null && progress.Type == DialogueTopicType.DungeonRequestProgress && progress.Pinned &&
                await npc.CommitNarrativeRequestAsync(owner, rumorId) == false, "full slot shows progress check and blocks another request");
            report.AppendLine("PASS: owner-only request, heard rumor location/sign, pinned rotation, request slot commit with four failure points.");

            // 3. 던전: 늑대만 잡으면 미해결, 동행 단독 보스 처치는 해결, 플레이어 측이 아니면 미해결 종료
            instance = npc.GetNarrativeInstance(rumorId);
            content = npc.GetNarrativeContent(instance);
            DungeonRecord record = DungeonLayout.Generate(seed, fixture.RumorVillage, rumorSlot, table);
            Require(DungeonManager.PlaceNarrativeTarget(record, content) &&
                record.Objects.Exists(o => o.TableId == content.TargetId), "first entry places the rumor monster");
            fixture.World.Dungeons.Dungeons.Add(record);
            fixture.Activate(record);
            DungeonObject target = record.Objects.Find(o => o.Kind == DungeonObjectKind.Monster);
            fixture.Kill(101, fixture.PlayerId, target.Id, false);
            Require(npc.GetNarrativeInstance(rumorId).IsOpen && npc.FindNarrativeConversation(owner).Kind == NarrativeConversationKind.None &&
                npc.GetNarrativeSignText(rumorSlot.DungeonId) == content.SignText, "killing only a normal monster leaves the rumor open; sign stays");
            fixture.World.NpcCompanion.ReservedNpcId = 13; fixture.World.NpcCompanion.IsInDungeon = true;
            fixture.Components.SetComponent(13, new NpcCompanionComponent());
            Require(CombatCredit.Resolve(13).PlayerParty && (int)Invoke(dungeon, "CompanionInDungeonId") == 13, "companion counts as the player party");
            int bossId = record.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).Id;
            fixture.Kill(102, 13, bossId, true);
            Set(dungeon, "_completionPending", false);
            Require(record.BossKilledByPlayerParty, "companion solo boss kill credits the player party");
            fixture.Deactivate();
            int companionDeedsBefore = npc.GetSaveData(13).Rumors.Count;
            baseline = ReadManifest(fixture.SlotPath).Revision;
            const SaveScope completionScope = SaveScope.Player | SaveScope.World | SaveScope.NpcState | SaveScope.Dungeons | SaveScope.Narrative;
            foreach (string failure in _narrativeFailurePoints)
            {
                Require(DungeonCompletionTransaction.TryStage(data.Dungeons, data.Player, record, seed, now, record.ReplacementCooldownHours,
                    out var failed), "stage completion");
                using (failed)
                {
                    var stage = npc.StageDungeonCompletion(record, rumorSlot.EntranceX, rumorSlot.EntranceY, 13, now);
                    Require(stage.Resolved && npc.GetNarrativeInstance(rumorId).Progress == NarrativeRumorProgress.Resolved, "staged resolution");
                    data.DiagnosticFailurePoint = failure;
                    Require(await data.SaveDungeonTransactionAsync(completionScope) == false, "injected completion failure " + failure);
                    data.DiagnosticFailurePoint = null;
                    npc.RollbackDungeonCompletion(stage);
                }
                Require(npc.GetNarrativeInstance(rumorId).IsOpen && npc.GetSaveData(13).Rumors.Count == companionDeedsBefore &&
                    npc.GetSaveData(owner).NarrativeRequest != null && ReadManifest(fixture.SlotPath).Revision == baseline,
                    "completion failure rolls back resolution, companion deed and slot: " + failure);
            }
            Require(DungeonCompletionTransaction.TryStage(data.Dungeons, data.Player, record, seed, now, record.ReplacementCooldownHours,
                out var completion), "stage completion retry");
            using (completion)
            {
                npc.StageDungeonCompletion(record, rumorSlot.EntranceX, rumorSlot.EntranceY, 13, now);
                Require(await data.SaveDungeonTransactionAsync(completionScope), "completion checkpoint with narrative");
                completion.Commit();
            }
            fixture.Reindex();
            instance = npc.GetNarrativeInstance(rumorId);
            NpcRumor companionDeed = npc.GetSaveData(13).Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == rumorId);
            Require(instance.Progress == NarrativeRumorProgress.Resolved && instance.HasEntranceContext &&
                companionDeed != null && companionDeed.DeedKind == PlayerDeedKind.RumorResolved && companionDeed.SourceNpcId < 0 &&
                companionDeed.Value == record.CompletionGold && npc.GetNarrativeSignText(rumorSlot.DungeonId) == string.Empty,
                "boss close resolves the rumor, gives the companion a firsthand deed and removes the sign");
            snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            Require(((NarrativeSaveRoot)Field(snapshot, "Narrative")).Instances.Find(i => i.InstanceId == rumorId).Progress == NarrativeRumorProgress.Resolved &&
                ((DungeonSaveRoot)Field(snapshot, "Dungeons")).Dungeons.Find(d => d.DungeonId == record.DungeonId).Completed,
                "resolution and completion share one manifest revision before map deletion");
            fixture.World.NpcCompanion.IsInDungeon = false;
            fixture.Components.RemoveComponent<NpcCompanionComponent>(13);

            // 플레이어 측이 아닌 보스 처치: 미해결 종료로 기록된다 (되돌려 다음 검사에 쓴다).
            DungeonSlot secondSlot = fixture.Slot(fixture.SecondVillage, 0);
            string secondId = npc.GetDungeonNarrative(secondSlot.DungeonId).InstanceId;
            DungeonRecord secondRecord = DungeonLayout.Generate(seed, fixture.SecondVillage, secondSlot, table);
            fixture.World.Dungeons.Dungeons.Add(secondRecord);
            secondRecord.KilledObjects.Add(secondRecord.Objects.Find(o => o.Kind == DungeonObjectKind.Boss).Id);
            secondRecord.BossKilledByPlayerParty = false;
            Require(DungeonCompletionTransaction.TryStage(data.Dungeons, data.Player, secondRecord, seed, now, secondRecord.ReplacementCooldownHours,
                out var unrelated), "stage unrelated completion");
            using (unrelated)
            {
                var stage = npc.StageDungeonCompletion(secondRecord, secondSlot.EntranceX, secondSlot.EntranceY, -1, now);
                Require(stage.Resolved == false && npc.GetNarrativeInstance(secondId).Ended &&
                    npc.GetNarrativeInstance(secondId).Progress == NarrativeRumorProgress.Unresolved, "non-party boss kill ends the rumor unresolved");
                npc.RollbackDungeonCompletion(stage);
            }
            Require(npc.GetNarrativeInstance(secondId).IsOpen, "rollback reopens the unrelated case");
            report.AppendLine("PASS: monster placement, normal kill keeps rumor open, companion solo boss resolution, four completion failure rollbacks, non-party end.");

            // 4. 보고 — 보고 전에는 아무도 반응하지 않는다. 정책은 사건이 고정한 스냅샷을 쓴다.
            Require(npc.FindNarrativeConversation(listener).Kind == NarrativeConversationKind.None, "before the report nobody reacts");
            Require(npc.FindNarrativeConversation(owner).Kind == NarrativeConversationKind.Report, "talking to the requester opens the report");
            NarrativePolicySnapshot pinned = npc.GetNarrativePolicy(instance);
            var changedPack = Copy(new BundledNarrativeContentSource().Read());
            for (int i = 0; i < changedPack.Reactions.Count; i++) changedPack.Reactions[i].Text += " (개정)";
            data.NarrativeCatalog.Initialize(new FixedNarrativeSource(changedPack), new List<DungeonTable> { table }, fixture.Monster, fixture.Item);
            NpcSaveData ownerSave = npc.GetSaveData(owner);
            int ownerVillage = ownerSave.VillageId;
            ownerSave.VillageId = fixture.GenericVillage;   // 이주해도 칸을 유지하고 옮겨 간 마을에서 보고받는다
            PlayerRelation before = npc.GetPlayerRelation(owner);
            bool met = npc.HasMetPlayer(owner);
            int goldBefore = data.Player.Gold;
            int rumorsBefore = ownerSave.Rumors.Count;
            var expected = NarrativeReactionRules.Select(pinned.Reactions, PlayerDeedKind.RumorResolved, true,
                new NarrativeReactionRules.Input(ownerSave.NpcStat, before, IsGlory(ownerSave)));
            NarrativeReactionRules.RelationChange(expected.RelationLevel, NarrativeRelevance.Requester, out int affinity, out int trust, out int intimacy);
            bool refuse = NarrativeReactionRules.IsNegative(expected.Kind) && NarrativeReactionRules.PromiseScore(ownerSave.NpcStat, before) < 0;
            int reward = refuse ? 0 : NarrativeReactionRules.RewardAmount(rumorId);
            baseline = ReadManifest(fixture.SlotPath).Revision;
            foreach (string failure in _narrativeFailurePoints)
            {
                data.DiagnosticFailurePoint = failure;
                Require(await npc.RunNarrativeConversationAsync(owner, new NarrativeConversation(NarrativeConversationKind.Report, rumorId)) == null,
                    "injected report failure " + failure);
                Require(data.Player.Gold == goldBefore && npc.HasMetPlayer(owner) == met && npc.GetPlayerRelation(owner).Affinity == before.Affinity &&
                    ownerSave.NarrativeRequest != null && ownerSave.Rumors.Count == rumorsBefore &&
                    npc.GetNarrativeInstance(rumorId).Reported == false && ReadManifest(fixture.SlotPath).Revision == baseline,
                    "report failure rolls back gold, relation, slot, deed rumor and report: " + failure);
            }
            data.DiagnosticFailurePoint = null;
            NarrativeDialogueResult reportResult = await npc.RunNarrativeConversationAsync(owner, new NarrativeConversation(NarrativeConversationKind.Report, rumorId));
            instance = npc.GetNarrativeInstance(rumorId);
            NpcRumor ownerDeed = ownerSave.Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == rumorId);
            Require(reportResult != null && instance.Reported && instance.ReactionRuleId == expected.ReactionRuleId &&
                instance.ReportRelevance == NarrativeRelevance.Requester && instance.RewardGold == reward &&
                data.Player.Gold == goldBefore + reward && npc.GetPlayerRelation(owner).Affinity == before.Affinity + NarrativeReactionRules.LimitConversationAffinity(affinity) &&
                npc.GetPlayerRelation(owner).Intimacy == before.Intimacy + intimacy && npc.GetPlayerRelation(owner).TalkCount == before.TalkCount &&
                ownerSave.NarrativeRequest == null && ownerDeed != null && ownerDeed.Reacted,
                "report: pre-change relation choice, relevance ×3, one system reward, slot cleared, reacted deed rumor, no Talk reward");
            Require(reportResult.Text.Contains(expected.Text) && reportResult.Text.Contains("(개정)") == false &&
                reportResult.Text.Contains(content.ResolvedText.Replace("{location}", string.Empty).Substring(0, 6)) && reportResult.Text.Contains("예전 마을"),
                "report text uses the pinned policy, reveals the cause and describes the old village after relocation");
            data.NarrativeCatalog.Initialize(new BundledNarrativeContentSource(), new List<DungeonTable> { table }, fixture.Monster, fixture.Item);
            ownerSave.VillageId = ownerVillage;
            Require(npc.FindNarrativeConversation(owner).Kind == NarrativeConversationKind.ReportReplay, "undisplayed report replays");
            NarrativeDialogueResult replay = await npc.RunNarrativeConversationAsync(owner, npc.FindNarrativeConversation(owner));
            NarrativeDialogueResult replayAgain = await npc.RunNarrativeConversationAsync(owner,
                new NarrativeConversation(NarrativeConversationKind.ReportReplay, rumorId));
            Require(replay != null && replayAgain != null && replay.Text == replayAgain.Text && replay.Text.Contains(expected.Text) &&
                data.Player.Gold == goldBefore + reward, "replay shows the saved report without another reward");
            data.DiagnosticFailurePoint = "before-manifest";
            Require(await npc.CommitNarrativeDisplayAsync(owner, replay) == false && npc.GetNarrativeInstance(rumorId).ReportDisplayed == false,
                "display commit failure keeps replay pending");
            data.DiagnosticFailurePoint = null;
            Require(await npc.CommitNarrativeDisplayAsync(owner, replay) && npc.FindNarrativeConversation(owner).Kind == NarrativeConversationKind.None,
                "displayed report ends the exchange; the owner never reacts again");
            snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            var savedInstance = ((NarrativeSaveRoot)Field(snapshot, "Narrative")).Instances.Find(i => i.InstanceId == rumorId);
            Require(savedInstance.Reported && savedInstance.ReportDisplayed && savedInstance.RewardGold == reward &&
                ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0].Gold == goldBefore + reward &&
                ((Dictionary<int, NpcSaveData>)Field(snapshot, "Npcs"))[owner].NarrativeRequest == null,
                "report, reward, relation and slot persist together");
            report.AppendLine($"PASS: report after relocation with pinned policy (rule {expected.ReactionRuleId}, reward {reward}), four failure rollbacks, replay and display commit.");

            // 5. 소문 전파와 반응 — 들은 NPC만 한 번, 관련도만큼, 14일·3개·상한
            npc.ReceiveRumor(npc.GetSaveData(listener), ownerDeed, owner, now);
            NpcRumor heard = npc.GetSaveData(listener).Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == rumorId);
            Require(heard != null && heard.Reacted == false && heard.SourceNpcId == owner, "spread deed copies without the reacted mark");
            NarrativePolicySnapshot deedPolicy = npc.GetNarrativePolicy(npc.GetNarrativeInstance(rumorId));
            await ExpectDeedReaction(fixture, listener, NarrativeRelevance.SameVillage, goldBefore + reward, deedPolicy);
            Require(npc.FindNarrativeConversation(listener).Kind == NarrativeConversationKind.None, "same NPC never reacts twice");
            await ExpectDeedReaction(fixture, 13, NarrativeRelevance.Companion, goldBefore + reward, deedPolicy);
            npc.GetSaveData(victim).LastWoundDungeonId = record.DungeonId;
            npc.ReceiveRumor(npc.GetSaveData(victim), ownerDeed, owner, now);
            await ExpectDeedReaction(fixture, victim, NarrativeRelevance.Victim, goldBefore + reward, deedPolicy);
            npc.ReceiveRumor(fixture.World.NpcSaveDatas[30], ownerDeed, owner, now);
            await ExpectDeedReaction(fixture, 30, NarrativeRelevance.OtherVillage, goldBefore + reward, deedPolicy);
            NpcSaveData extraSave = npc.GetSaveData(extra);
            npc.AddRumor(extraSave, DeedRumor(fixture.RumorVillage, 2, 90, now - NarrativeReactionRules.DeedReactionWindowHours - 1f), now);
            Require(npc.FindNarrativeConversation(extra).Kind == NarrativeConversationKind.None &&
                extraSave.Rumors.Exists(r => r.Kind == NpcRumorKind.PlayerDeed), "deeds older than 14 game days are skipped but kept");
            for (int g = 0; g < 4; g++) npc.AddRumor(extraSave, DeedRumor(fixture.RumorVillage, 2, 50 + g, now - g), now);
            int extraAffinity = npc.GetPlayerRelation(extra).Affinity;
            NarrativeDialogueResult bundle = await npc.RunNarrativeConversationAsync(extra, npc.FindNarrativeConversation(extra));
            int reacted = extraSave.Rumors.FindAll(r => r.Kind == NpcRumorKind.PlayerDeed && r.Reacted).Count;
            int delta = npc.GetPlayerRelation(extra).Affinity - extraAffinity;
            Require(bundle != null && reacted == NarrativeReactionRules.MaxDeedsPerConversation && delta <= 9 && delta >= -4 &&
                bundle.Text.Contains("요즘 네 이야기를 자주 듣는다"), "one conversation reacts to at most three deeds within the +9/-4 cap");
            report.AppendLine("PASS: deed spread without reacted mark, one reaction per NPC, relevance ×1/×2/×3/×0.3, 14-day skip, three-deed bundle and cap.");

            // 6. 소문 전 해결을 주인에게 말하기, 7. 미해결 종료 알림, 8. 칸 주인 사망
            int secondOwner = npc.GetNarrativeInstance(secondId).OwnerNpcId;
            secondRecord.BossKilledByPlayerParty = true;
            Require(DungeonCompletionTransaction.TryStage(data.Dungeons, data.Player, secondRecord, seed, now, secondRecord.ReplacementCooldownHours,
                out var early), "stage pre-request clear");
            using (early)
            {
                npc.StageDungeonCompletion(secondRecord, secondSlot.EntranceX, secondSlot.EntranceY, -1, now);
                Require(await data.SaveDungeonTransactionAsync(completionScope), "pre-request clear checkpoint");
                early.Commit();
            }
            fixture.Reindex();
            int goldBeforeTell = data.Player.Gold;
            Require(npc.FindNarrativeConversation(secondOwner).Kind == NarrativeConversationKind.DirectTell &&
                npc.FindNarrativeConversation(fixture.ResidentsExcept(fixture.SecondVillage, secondOwner, -1)[0]).Kind == NarrativeConversationKind.None,
                "only the rumor owner hears about a pre-request clear");
            NarrativeDialogueResult tell = await npc.RunNarrativeConversationAsync(secondOwner, npc.FindNarrativeConversation(secondOwner));
            NarrativeContentInstance told = npc.GetNarrativeInstance(secondId);
            Require(tell != null && told.Reported && told.ReportRelevance == NarrativeRelevance.SameVillage && told.RewardGold == 0 &&
                data.Player.Gold == goldBeforeTell && npc.GetSaveData(secondOwner).Rumors.Exists(r => r.InstanceId == secondId && r.Reacted) &&
                await npc.CommitNarrativeDisplayAsync(secondOwner, tell) && npc.FindNarrativeConversation(secondOwner).Kind == NarrativeConversationKind.None,
                "direct tell: same-village relevance, no request reward, once");

            NarrativeContentInstance emptyRumor = npc.GetDungeonNarrative(fixture.Slot(fixture.EmptyVillage, 0).DungeonId);
            Require(await npc.CommitNarrativeRequestAsync(40, emptyRumor.InstanceId), "pending owner can request");
            DungeonSlot emptySlot = fixture.Slot(fixture.EmptyVillage, 0);
            npc.EndDungeonNarrativeUnresolved(emptySlot.DungeonId, true, emptySlot.EntranceX, emptySlot.EntranceY, now);
            Require(npc.GetSaveData(40).NarrativeRequest == null && npc.FindNarrativeConversation(40).Kind == NarrativeConversationKind.EndNotice,
                "NPC completion ends the rumor unresolved, clears the slot and leaves a notice");
            int goldBeforeNotice = data.Player.Gold;
            NarrativeDialogueResult notice = await npc.RunNarrativeConversationAsync(40, npc.FindNarrativeConversation(40));
            Require(notice != null && notice.Text.Contains("다른 사람들이 처리했다") && await npc.CommitNarrativeDisplayAsync(40, notice) &&
                npc.FindNarrativeConversation(40).Kind == NarrativeConversationKind.None && data.Player.Gold == goldBeforeNotice &&
                npc.GetSaveData(40).Rumors.TrueForAll(r => r.Kind != NpcRumorKind.PlayerDeed), "unresolved end notice once, no reward or deed");
            npc.GetSaveData(21).NarrativeRequest = new NpcNarrativeRequest { InstanceId = secondId };
            Invoke(npc, "ClearNarrativeRequestOnDeath", 21, npc.GetSaveData(21));
            Require(npc.GetSaveData(21).NarrativeRequest == null, "a dead requester's slot is cleared without reward or spread");
            report.AppendLine("PASS: pre-request direct tell, unresolved end notice, requester death clears the slot.");

            // 9. 소망 (3단계) — 호감 30에 털어놓고 표시 뒤 저장, 아이템 전달로 충족·반응·보답, 소문으로 들은 이웃 반응, 주인 사망
            NarrativeContentDefinition wishContent = data.NarrativeCatalog.GetWishContent(0);
            ItemTable wishTable = fixture.Item(wishContent.TargetId);
            const int wisher = 50, wishNeighbor = 51, doomed = 52;
            fixture.AddResident(wisher, fixture.GenericVillage, 50, 50);
            fixture.AddResident(wishNeighbor, fixture.GenericVillage, 30, 85);
            fixture.AddResident(doomed, fixture.GenericVillage, 50, 50);
            npc.RebuildSaveSnapshot();
            PlayerRelation shy = PlayerRelation.Default;
            shy.Affinity = NpcManager.WishRevealAffinity - 1;
            npc.SetPlayerRelation(wisher, shy);
            Require(npc.BuildNarrativeWishTopic(wisher, npc.GetSaveData(wisher)) == null, "no wish below affinity 30");
            PlayerRelation warm = shy;
            warm.Affinity = NpcManager.WishRevealAffinity;
            npc.SetPlayerRelation(wisher, warm);
            string wishId = NarrativeContentInstance.WishId(wisher, wishContent.ContentId);
            DialogueTopic reveal = npc.BuildNarrativeWishTopic(wisher, npc.GetSaveData(wisher));
            Require(reveal != null && reveal.Type == DialogueTopicType.WishReveal && reveal.Pinned && reveal.NarrativeContentId == wishContent.ContentId &&
                reveal.NarrativeInstanceId == wishId && reveal.BaseLine.Contains(wishTable.Name) && reveal.BaseLine.Contains("{") == false &&
                npc.GetOpenWish(wisher) == null, "affinity 30 reveals the wish with the item name; nothing is saved before display");
            ValidatePinnedRotation(wisher, reveal);
            baseline = ReadManifest(fixture.SlotPath).Revision;
            foreach (string failure in _narrativeFailurePoints)
            {
                data.DiagnosticFailurePoint = failure;
                Require(await npc.CommitNarrativeWishRevealAsync(wisher, wishContent.ContentId) == false && npc.GetNarrativeInstance(wishId) == null &&
                    ReadManifest(fixture.SlotPath).Revision == baseline, "wish reveal failure removes the case: " + failure);
            }
            data.DiagnosticFailurePoint = null;
            Require(await npc.CommitNarrativeWishRevealAsync(wisher, wishContent.ContentId) && npc.GetOpenWish(wisher)?.InstanceId == wishId &&
                npc.GetOpenWish(wisher).WishState == WishState.Revealed && npc.GetOpenWish(wisher).TargetItemId == wishContent.TargetId,
                "displayed reveal creates one revealed wish");
            snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            Require(((NarrativeSaveRoot)Field(snapshot, "Narrative")).Instances.Exists(i => i.InstanceId == wishId && i.WishState == WishState.Revealed) &&
                NarrativeSaveValidation.IsConsistent((NarrativeSaveRoot)Field(snapshot, "Narrative"), seed), "revealed wish persists and passes load validation");
            DialogueTopic reminder = npc.BuildNarrativeWishTopic(wisher, npc.GetSaveData(wisher));
            Require(reminder != null && reminder.Type == DialogueTopicType.WishReminder && reminder.Pinned && reminder.BaseLine.Contains(wishTable.Name) &&
                await npc.CommitNarrativeWishRevealAsync(wisher, wishContent.ContentId) == false, "open wish shows a reminder and cannot be revealed twice");
            Require(npc.BuildNarrativeRequestTopic(wisher, npc.GetSaveData(wisher)) == null && npc.GetSaveData(wisher).NarrativeRequest == null,
                "wishes never use the dungeon request slot");

            // 전달: 다른 아이템은 소모하지 않고, 실패 주입 4종은 아이템까지 되돌리고, 성공은 한 manifest로 저장한다.
            Inventory inventory = fixture.Ar.Player.Inventory;
            var wrong = new ItemData { Id = 1, ItemInstanceId = 9100, Quantity = 1, Table = fixture.Item(1) };
            var gift = new ItemData { Id = wishContent.TargetId, ItemInstanceId = 9101, Quantity = 1, Table = wishTable };
            int wrongSlot = inventory.AddItem(wrong);
            int giftSlot = inventory.AddItem(gift);
            Require(wrongSlot >= 0 && giftSlot >= 0 && npc.IsOpenWishItem(wisher, gift) && npc.IsOpenWishItem(wisher, wrong) == false &&
                npc.IsOpenWishItem(wishNeighbor, gift) == false && await npc.DeliverWishItemAsync(wisher, wrongSlot) == null &&
                inventory.GetItemBySlotIndex(wrongSlot) == wrong, "only the wished item given to its owner counts; another item is not consumed");
            NpcSaveData wisherSave = npc.GetSaveData(wisher);
            NarrativePolicySnapshot wishPolicy = npc.GetNarrativePolicy(npc.GetOpenWish(wisher));
            PlayerRelation wishBefore = npc.GetPlayerRelation(wisher);
            var wishRule = NarrativeReactionRules.Select(wishPolicy.Reactions, PlayerDeedKind.WishFulfilled, true,
                new NarrativeReactionRules.Input(wisherSave.NpcStat, wishBefore, IsGlory(wisherSave)));
            NarrativeReactionRules.RelationChange(wishRule.RelationLevel, NarrativeRelevance.Requester, out int wishAffinity, out _, out int wishIntimacy);
            int wishReward = NarrativeReactionRules.RewardAmount(wishId);
            int goldBeforeWish = data.Player.Gold;
            int wishRumors = wisherSave.Rumors.Count;
            baseline = ReadManifest(fixture.SlotPath).Revision;
            foreach (string failure in _narrativeFailurePoints)
            {
                data.DiagnosticFailurePoint = failure;
                Require(await npc.DeliverWishItemAsync(wisher, giftSlot) == null, "injected wish delivery failure " + failure);
                Require(inventory.GetItemBySlotIndex(giftSlot) == gift && gift.Quantity == 1 && data.Player.Gold == goldBeforeWish &&
                    npc.GetPlayerRelation(wisher).Affinity == wishBefore.Affinity && wisherSave.Rumors.Count == wishRumors &&
                    npc.GetOpenWish(wisher) != null && ReadManifest(fixture.SlotPath).Revision == baseline,
                    "wish delivery failure returns the item, gold, relation, rumor and wish: " + failure);
            }
            data.DiagnosticFailurePoint = null;
            NarrativeDialogueResult delivered = await npc.DeliverWishItemAsync(wisher, giftSlot);
            NarrativeContentInstance fulfilled = npc.GetNarrativeInstance(wishId);
            NpcRumor wishDeed = wisherSave.Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == wishId);
            Require(delivered != null && delivered.Kind == NarrativeConversationKind.WishDelivery && inventory.GetItemBySlotIndex(giftSlot) == null &&
                fulfilled.WishState == WishState.Fulfilled && fulfilled.Reported && fulfilled.ReactionRuleId == wishRule.ReactionRuleId &&
                wishRule.Audience == NarrativeReactionAudience.Owner && fulfilled.RewardGold == wishReward && data.Player.Gold == goldBeforeWish + wishReward &&
                npc.GetPlayerRelation(wisher).Affinity == wishBefore.Affinity + NarrativeReactionRules.LimitConversationAffinity(wishAffinity) &&
                npc.GetPlayerRelation(wisher).Intimacy == wishBefore.Intimacy + wishIntimacy && npc.GetPlayerRelation(wisher).TalkCount == wishBefore.TalkCount &&
                wishDeed != null && wishDeed.DeedKind == PlayerDeedKind.WishFulfilled && wishDeed.Reacted && wishDeed.Value == wishTable.BasePrice &&
                npc.GetOpenWish(wisher) == null,
                "wish delivery: item consumed, owner row, relevance x3, one system reward, reacted deed valued at the item price, no gift/Talk reward");
            Require(delivered.Text.Contains(wishTable.Name) && delivered.Text.Contains(wishRule.Text) &&
                delivered.Text.Contains(wishContent.ResolvedText.Substring(0, 6)), "delivery text names the item, the owner line and the reaction");
            snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            Require(((NarrativeSaveRoot)Field(snapshot, "Narrative")).Instances.Find(i => i.InstanceId == wishId).WishState == WishState.Fulfilled &&
                ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0].Gold == goldBeforeWish + wishReward &&
                ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0]._inventory.TrueForAll(i => i == null || i.ItemInstanceId != gift.ItemInstanceId) &&
                ((Dictionary<int, NpcSaveData>)Field(snapshot, "Npcs"))[wisher].Rumors.Exists(r => r.InstanceId == wishId && r.Reacted),
                "fulfilled wish, reward, consumed item and owner deed commit in one manifest revision");
            Require(npc.FindNarrativeConversation(wisher).Kind == NarrativeConversationKind.ReportReplay, "undisplayed wish delivery replays");
            NarrativeDialogueResult wishReplay = await npc.RunNarrativeConversationAsync(wisher, npc.FindNarrativeConversation(wisher));
            Require(wishReplay != null && wishReplay.Text == delivered.Text && data.Player.Gold == goldBeforeWish + wishReward &&
                await npc.CommitNarrativeDisplayAsync(wisher, wishReplay) && npc.FindNarrativeConversation(wisher).Kind == NarrativeConversationKind.None &&
                npc.BuildNarrativeWishTopic(wisher, wisherSave) == null, "replay without another reward; the same wish is never offered again");

            // 이웃: 소문으로 들은 같은 마을 NPC만 한 번, Others 행으로 ×1 반응한다.
            Require(npc.FindNarrativeConversation(wishNeighbor).Kind == NarrativeConversationKind.None, "a neighbor reacts only after hearing the deed");
            NpcSaveData neighborSave = npc.GetSaveData(wishNeighbor);
            Require(npc.ReceiveRumor(neighborSave, wishDeed, wisher, now), "wish deed spreads by exchange");
            PlayerRelation neighborBefore = npc.GetPlayerRelation(wishNeighbor);
            var neighborRule = NarrativeReactionRules.Select(wishPolicy.Reactions, PlayerDeedKind.WishFulfilled, false,
                new NarrativeReactionRules.Input(neighborSave.NpcStat, neighborBefore, IsGlory(neighborSave)));
            NarrativeReactionRules.RelationChange(neighborRule.RelationLevel, NarrativeRelevance.SameVillage, out int neighborAffinity, out _, out _);
            NarrativeDialogueResult neighborResult = await npc.RunNarrativeConversationAsync(wishNeighbor, npc.FindNarrativeConversation(wishNeighbor));
            Require(neighborResult != null && neighborRule.Audience == NarrativeReactionAudience.Others && neighborResult.Text.Contains(neighborRule.Text) &&
                neighborResult.Text.Contains(wisherSave.Name) &&
                npc.GetPlayerRelation(wishNeighbor).Affinity == neighborBefore.Affinity + NarrativeReactionRules.LimitConversationAffinity(neighborAffinity) &&
                data.Player.Gold == goldBeforeWish + wishReward && npc.FindNarrativeConversation(wishNeighbor).Kind == NarrativeConversationKind.None,
                "a neighbor reacts once with an Others row at x1 and names the wish owner, without a reward");

            // 주인 사망: 열린 소망은 실패로 끝나고 다시 열리지 않는다 (정리는 10에서 본다).
            npc.SetPlayerRelation(doomed, warm);
            string doomedId = NarrativeContentInstance.WishId(doomed, wishContent.ContentId);
            Require(await npc.CommitNarrativeWishRevealAsync(doomed, wishContent.ContentId), "second owner reveals the wish");
            npc.MarkSaveDirty(doomed, NpcSaveGroup.Condition);
            npc.GetSaveData(doomed).Condition = CharacterConditions.Dead;
            Invoke(npc, "FailNarrativeWishesOnDeath", doomed);
            Require(npc.GetNarrativeInstance(doomedId).WishState == WishState.Failed && npc.GetOpenWish(doomed) == null &&
                npc.FindNarrativeConversation(doomed).Kind == NarrativeConversationKind.None &&
                npc.BuildNarrativeWishTopic(doomed, npc.GetSaveData(doomed)) == null && NarrativeSaveValidation.IsConsistent(data.Narrative, seed),
                "the owner's death fails the open wish without reward");
            report.AppendLine($"PASS: wish at affinity {NpcManager.WishRevealAffinity} (item {wishContent.TargetId}), reveal after display with four failure rollbacks, " +
                              $"delivery with item return on four failures (rule {wishRule.ReactionRuleId}, reward {wishReward}), replay, neighbor rule {neighborRule.ReactionRuleId}, owner death.");

            // 10. 재접속과 정리
            Require(await data.SaveDungeonTransactionAsync(SaveScope.All), "full checkpoint before reconnect");
            snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            Require(snapshot != null && NarrativeSaveValidation.IsConsistent((NarrativeSaveRoot)Field(snapshot, "Narrative"), seed),
                "saved narrative part passes load validation");
            fixture.Restore(snapshot);
            Require(npc.GetNarrativeInstance(rumorId).Reported && npc.FindNarrativeConversation(owner).Kind == NarrativeConversationKind.None &&
                npc.FindNarrativeConversation(listener).Kind == NarrativeConversationKind.None &&
                npc.GetDungeonNarrative(fixture.Slot(fixture.EmptyVillage, 0).DungeonId).Ended, "reconnect keeps reports and reactions without repeats");
            Require(npc.GetNarrativeInstance(wishId)?.WishState == WishState.Fulfilled && npc.GetNarrativeInstance(wishId).ReportDisplayed &&
                npc.GetNarrativeInstance(doomedId)?.WishState == WishState.Failed && npc.FindNarrativeConversation(wisher).Kind == NarrativeConversationKind.None &&
                npc.BuildNarrativeWishTopic(wisher, npc.GetSaveData(wisher)) == null &&
                npc.GetSaveData(wishNeighbor).Rumors.Exists(r => r.InstanceId == wishId && r.Reacted),
                "reconnect keeps fulfilled/failed wishes and the neighbor's reaction; the same wish is not offered again");
            Set(fixture.Ar.Time, "_elapsedGameTime", now + NarrativeReactionRules.DeedReactionWindowHours + 24f);
            Require(npc.PruneNarrativeLedger(fixture.Ar.Time.CurrentGameTime) && npc.GetNarrativeInstance(rumorId) == null &&
                data.Narrative.Selections.TrueForAll(s => s.DungeonId != rumorSlot.DungeonId) &&
                NarrativeSaveValidation.IsConsistent(data.Narrative, seed), "settled instances older than 14 days are pruned consistently");
            Require(npc.GetNarrativeInstance(wishId) != null && npc.GetNarrativeInstance(doomedId) == null,
                "pruning keeps a living owner's wish record (one per content) and drops a dead owner's failed wish");
            Require(await data.SaveDungeonTransactionAsync(SaveScope.All) && ReadSnapshot(data, fixture.SlotPath, "manifest.json") != null,
                "pruned ledger saves and reloads");
            ValidateDialogueBinding(fixture, owner);
            report.AppendLine("PASS: reconnect through the real manifest reader (rumors and wishes), pruning after the reaction window, dialogue binding guards.");
        }

        // ── 후속 소개 (5단계, §4.6): 호기심 반응이 아는 다른 소문을 소개하고, 표시 뒤 그 NPC의 부탁 칸에 들어간다. ──
        private static async Task ValidateFollowUps(NarrativeDiagnosticFixture fixture, StringBuilder report)
        {
            NpcManager npc = fixture.Npc;
            DataManager data = fixture.Data;
            int seed = fixture.World.WorldSeed;
            float now = fixture.Ar.Time.CurrentGameTime;
            int village = fixture.GenericVillage, far = fixture.VisitedVillage;
            const int reporter = 70, listener = 71, otherOwner = 30;
            fixture.AddResident(reporter, village, 95, 30);
            fixture.AddResident(listener, far, 95, 30);
            npc.RebuildSaveSnapshot();
            NarrativeContentInstance source = FollowUpCase(fixture, village, 0, reporter);
            NarrativeContentInstance target = FollowUpCase(fixture, village, 1, otherOwner);
            NarrativeContentInstance farTarget = FollowUpCase(fixture, far, 1, -1);
            NpcSaveData reporterSave = npc.GetSaveData(reporter);
            NpcSaveData listenerSave = npc.GetSaveData(listener);
            // 보고를 기다리는 상태: 소문 주인 70이 부탁했고 플레이어 측이 보스를 처치했다.
            data.MarkNarrativeInstanceSaveDirty(source.InstanceId, NarrativeSaveGroup.All);
            source.RequesterNpcId = reporter; source.RequestedGameTime = now;
            source.Progress = NarrativeRumorProgress.Resolved; source.ProgressGameTime = now;
            npc.MarkSaveDirty(reporter, NpcSaveGroup.NarrativeRequest);
            reporterSave.NarrativeRequest = new NpcNarrativeRequest { InstanceId = source.InstanceId, RequestedGameTime = now };
            NarrativeContentInstance Find(NpcSaveData speaker, string sourceId, bool freed)
                => (NarrativeContentInstance)Invoke(npc, "FindFollowUpTarget", speaker, sourceId, freed);

            // 소개 조건: 아는 소문만(주인이거나 위치·징후를 들음), 같은 마을만, 부탁 칸이 비거나 이번 보고로 빌 때만.
            Require(Find(reporterSave, source.InstanceId, true) == null, "an unknown rumor is never introduced");
            npc.AddRumor(reporterSave, DiscoveryRumor(target, otherOwner, now), now);
            npc.AddRumor(listenerSave, DiscoveryRumor(target, otherOwner, now), now);
            npc.AddRumor(listenerSave, DiscoveryRumor(farTarget, otherOwner, now), now);
            Require(Find(reporterSave, source.InstanceId, true)?.InstanceId == target.InstanceId && Find(reporterSave, source.InstanceId, false) == null &&
                Find(listenerSave, source.InstanceId, false)?.InstanceId == farTarget.InstanceId,
                "known same-village rumor only; a full request slot blocks the introduction");
            NarrativePolicySnapshot policy = npc.GetNarrativePolicy(source);
            var expected = NarrativeReactionRules.Select(policy.Reactions, PlayerDeedKind.RumorResolved, true,
                new NarrativeReactionRules.Input(reporterSave.NpcStat, npc.GetPlayerRelation(reporter), IsGlory(reporterSave)));
            Require(expected.Response == NarrativeResponseKind.IntroduceKnownContent, "a curious requester picks the introduce-known-content reaction");

            // 보고 + 소개(대기): 실패 주입 4종은 소개까지 되돌린다.
            Require(await data.SaveDungeonTransactionAsync(SaveScope.All), "follow-up checkpoint");
            long baseline = ReadManifest(fixture.SlotPath).Revision;
            var reportConversation = new NarrativeConversation(NarrativeConversationKind.Report, source.InstanceId);
            foreach (string failure in _narrativeFailurePoints)
            {
                data.DiagnosticFailurePoint = failure;
                Require(await npc.RunNarrativeConversationAsync(reporter, reportConversation) == null &&
                    npc.GetNarrativeInstance(source.InstanceId).FollowUpState == NarrativeFollowUpState.None &&
                    npc.GetNarrativeInstance(source.InstanceId).Reported == false && reporterSave.NarrativeRequest?.InstanceId == source.InstanceId &&
                    npc.GetNarrativeInstance(target.InstanceId).RequesterNpcId < 0 && ReadManifest(fixture.SlotPath).Revision == baseline,
                    "report failure rolls back the pending introduction: " + failure);
            }
            data.DiagnosticFailurePoint = null;
            NarrativeDialogueResult reported = await npc.RunNarrativeConversationAsync(reporter, reportConversation);
            source = npc.GetNarrativeInstance(source.InstanceId);
            target = npc.GetNarrativeInstance(target.InstanceId);
            string introLine = npc.GetNarrativeContent(target).IntroText.Replace("{location}", string.Empty).Trim().Substring(0, 6);
            Require(reported != null && reported.Text.Contains("소식이 하나 더 있네") && reported.Text.Contains(introLine) &&
                source.FollowUpState == NarrativeFollowUpState.Pending && source.FollowUpInstanceId == target.InstanceId &&
                source.Response == NarrativeResponseKind.IntroduceKnownContent && reporterSave.NarrativeRequest == null && target.RequesterNpcId < 0,
                "report text introduces the known rumor; the request waits for display");
            data.DiagnosticFailurePoint = "before-manifest";
            Require(await npc.CommitNarrativeDisplayAsync(reporter, reported) == false &&
                npc.GetNarrativeInstance(source.InstanceId).FollowUpState == NarrativeFollowUpState.Pending &&
                npc.GetNarrativeInstance(source.InstanceId).ReportDisplayed == false && npc.GetNarrativeInstance(target.InstanceId).RequesterNpcId < 0 &&
                reporterSave.NarrativeRequest == null, "display commit failure keeps the introduction pending");
            data.DiagnosticFailurePoint = null;
            Require(npc.FindNarrativeConversation(reporter).Kind == NarrativeConversationKind.ReportReplay, "pending introduction replays with the report");
            NarrativeDialogueResult replay = await npc.RunNarrativeConversationAsync(reporter, npc.FindNarrativeConversation(reporter));
            Require(replay != null && replay.Text == reported.Text && await npc.CommitNarrativeDisplayAsync(reporter, replay), "replay shows the same introduction");
            source = npc.GetNarrativeInstance(source.InstanceId);
            target = npc.GetNarrativeInstance(target.InstanceId);
            Require(source.FollowUpState == NarrativeFollowUpState.Introduced && source.ReportDisplayed && target.RequesterNpcId == reporter &&
                target.IntroducedFromInstanceId == source.InstanceId && target.OwnerNpcId == otherOwner && target.HasEntranceContext &&
                reporterSave.NarrativeRequest?.InstanceId == target.InstanceId, "displayed introduction puts the rumor in the introducer's request slot");
            object snapshot = ReadSnapshot(data, fixture.SlotPath, "manifest.json");
            var savedRoot = (NarrativeSaveRoot)Field(snapshot, "Narrative");
            Require(savedRoot.Instances.Find(i => i.InstanceId == target.InstanceId).RequesterNpcId == reporter &&
                savedRoot.Instances.Find(i => i.InstanceId == source.InstanceId).FollowUpState == NarrativeFollowUpState.Introduced &&
                ((Dictionary<int, NpcSaveData>)Field(snapshot, "Npcs"))[reporter].NarrativeRequest.InstanceId == target.InstanceId &&
                NarrativeSaveValidation.IsConsistent(savedRoot, seed), "introduction, request slot and source record commit in one manifest and load");
            Require(npc.BuildNarrativeRequestTopic(otherOwner, npc.GetSaveData(otherOwner))?.NarrativeInstanceId != target.InstanceId &&
                npc.BuildNarrativeRequestTopic(reporter, reporterSave)?.Type == DialogueTopicType.DungeonRequestProgress,
                "the original owner no longer requests it; the introducer shows the progress check");

            // 이야기 줄기당 한 번: 같은 행적을 들은 이웃은 다시 소개하지 않는다.
            NpcRumor sourceDeed = reporterSave.Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == source.InstanceId);
            npc.ReceiveRumor(listenerSave, sourceDeed, reporter, now);
            NarrativeDialogueResult heard = await npc.RunNarrativeConversationAsync(listener, npc.FindNarrativeConversation(listener));
            Require(heard != null && heard.NeedsDisplayCommit == false && string.IsNullOrEmpty(heard.FollowUpInstanceId),
                "a deed that already introduced a rumor does not introduce again");

            // 소개한 NPC에게 보고: 부탁한 사람 ×3, 의뢰 보답은 소개한 NPC가 준다.
            data.MarkNarrativeInstanceSaveDirty(target.InstanceId, NarrativeSaveGroup.Progress);
            target.Progress = NarrativeRumorProgress.Resolved; target.ProgressGameTime = now;
            int goldBefore = data.Player.Gold;
            Require(npc.FindNarrativeConversation(reporter).Kind == NarrativeConversationKind.Report, "the introducer receives the report");
            NarrativeDialogueResult second = await npc.RunNarrativeConversationAsync(reporter, npc.FindNarrativeConversation(reporter));
            target = npc.GetNarrativeInstance(target.InstanceId);
            int reward = target.RewardRefused ? 0 : NarrativeReactionRules.RewardAmount(target.InstanceId);
            Require(second != null && target.Reported && target.ReportNpcId == reporter && target.ReportRelevance == NarrativeRelevance.Requester &&
                target.RewardGold == reward && data.Player.Gold == goldBefore + reward && reporterSave.NarrativeRequest == null &&
                target.FollowUpState == NarrativeFollowUpState.None && await npc.CommitNarrativeDisplayAsync(reporter, second),
                "report to the introducer: requester x3 and the introducer's reward; no other known rumor to introduce");

            // 소문 반응에서의 소개: 표시 뒤 확정, 그사이 대상이 무효면 확정하지 않음, 실패하면 되돌림.
            NpcRumor targetDeed = reporterSave.Rumors.Find(r => r.Kind == NpcRumorKind.PlayerDeed && r.InstanceId == target.InstanceId);
            npc.ReceiveRumor(listenerSave, targetDeed, reporter, now);
            NarrativeDialogueResult deedResult = await npc.RunNarrativeConversationAsync(listener, npc.FindNarrativeConversation(listener));
            Require(deedResult != null && deedResult.NeedsDisplayCommit && deedResult.InstanceId == target.InstanceId &&
                deedResult.FollowUpInstanceId == farTarget.InstanceId && deedResult.Text.Contains("소식이 하나 더 있네") && listenerSave.NarrativeRequest == null,
                "a curious listener's deed reaction introduces a rumor it knows in its own village");
            data.MarkNarrativeInstanceSaveDirty(farTarget.InstanceId, NarrativeSaveGroup.Introductions);
            farTarget.RequesterNpcId = 99;
            Require(await npc.CommitNarrativeDisplayAsync(listener, deedResult) == false && listenerSave.NarrativeRequest == null &&
                npc.GetNarrativeInstance(target.InstanceId).FollowUpState == NarrativeFollowUpState.None, "an invalid target is not introduced");
            farTarget.RequesterNpcId = -1;
            data.MarkNarrativeInstanceSaveDirty(farTarget.InstanceId, NarrativeSaveGroup.Introductions);
            data.DiagnosticFailurePoint = "part-write";
            Require(await npc.CommitNarrativeDisplayAsync(listener, deedResult) == false && listenerSave.NarrativeRequest == null &&
                npc.GetNarrativeInstance(farTarget.InstanceId).RequesterNpcId < 0 &&
                npc.GetNarrativeInstance(target.InstanceId).FollowUpState == NarrativeFollowUpState.None, "deed introduction failure rolls back");
            data.DiagnosticFailurePoint = null;
            Require(await npc.CommitNarrativeDisplayAsync(listener, deedResult) &&
                npc.GetNarrativeInstance(farTarget.InstanceId).RequesterNpcId == listener &&
                npc.GetNarrativeInstance(farTarget.InstanceId).IntroducedFromInstanceId == target.InstanceId &&
                npc.GetNarrativeInstance(target.InstanceId).FollowUpState == NarrativeFollowUpState.Introduced &&
                listenerSave.NarrativeRequest?.InstanceId == farTarget.InstanceId && NarrativeSaveValidation.IsConsistent(data.Narrative, seed),
                "displayed deed introduction fills the listener's request slot");
            report.AppendLine("PASS: follow-up introduction (5단계): known same-village rumors only, full slot blocks, pending report introduction with " +
                              "four report failures and a display failure, replay, one manifest, introducer becomes requester and pays the reward, " +
                              "one introduction per deed, deed-reaction introduction with invalid-target and failure rollback.");
        }

        private static NarrativeContentInstance FollowUpCase(NarrativeDiagnosticFixture fixture, int villageId, int slotIndex, int ownerId)
        {
            NpcManager npc = fixture.Npc;
            DungeonSlot slot = fixture.Slot(villageId, slotIndex);
            NarrativeContentInstance instance = npc.GetDungeonNarrative(slot.DungeonId);
            if (instance == null)
            {
                NarrativeContentCatalog catalog = fixture.Data.NarrativeCatalog;
                NarrativeDefinitionSnapshot definition = catalog.SelectDungeonRumor(fixture.World.WorldSeed, villageId, slot.SlotIndex, slot.Generation, fixture.Table);
                instance = (NarrativeContentInstance)Invoke(npc, "CreateDungeonRumorInstance", villageId, slot, definition, catalog.CopyCurrentPolicy());
            }
            Require(instance.IsOpen && instance.RequesterNpcId < 0 && slot.HasEntrance, "follow-up fixture case is open, unrequested and has an entrance");
            fixture.Data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Target);
            instance.OwnerNpcId = ownerId;
            return instance;
        }

        private static NpcRumor DiscoveryRumor(NarrativeContentInstance instance, int sourceNpcId, float now)
            => new NpcRumor { Kind = NpcRumorKind.DungeonInfo, IsDiscovery = true, InstanceId = instance.InstanceId, VillageId = instance.VillageId,
                SlotIndex = instance.SlotIndex, Generation = instance.Generation, EventGameTime = now, SourceNpcId = sourceNpcId, SubjectNpcId = -1 };

        // ── 개인 사연 (4단계): 최근 부상 문구를 작성 표에서 읽는다. 화제 선택·저장·대화 보상은 바뀌지 않는다. ──
        private static void ValidateRecentWoundStory(NarrativeDiagnosticFixture fixture, StringBuilder report)
        {
            NpcManager npc = fixture.Npc;
            NarrativeContentDefinition story = fixture.Data.NarrativeCatalog.FindPersonalStory(NarrativeTargetType.RecentWound);
            Require(story != null, "bundled recent-wound story");
            const int wounded = 60;
            fixture.AddResident(wounded, fixture.GenericVillage, 50, 50);
            NpcSaveData save = npc.GetSaveData(wounded);
            float now = fixture.Ar.Time.CurrentGameTime;
            int instancesBefore = fixture.Data.Narrative.Instances.Count;
            Require(WoundTopic(npc, wounded, now) == null, "no recent-wound topic without a wound record");
            save.WoundCount = 1;
            save.LastWoundGameTime = now;
            save.LastWoundActivity = NpcActivityKind.DungeonDelve;
            DialogueTopic normal = WoundTopic(npc, wounded, now);
            Require(normal != null && normal.BaseLine == story.IntroText.Replace(NarrativeContentCatalog.KnownWoundSummaryToken, "지난 던전 원정에서 호되게 당했소."),
                "recent-wound topic uses the sheet line with the speaker's own wound summary");
            (save.PersonalStories ??= new NpcPersonalStories()).LastWoundSevere = true;
            save.LastWoundActivity = NpcActivityKind.Hunt;
            DialogueTopic severe = WoundTopic(npc, wounded, now);
            Require(severe != null && severe.BaseLine == story.IntroText.Replace(NarrativeContentCatalog.KnownWoundSummaryToken, "지난 사냥에서 죽다 살아났소.") &&
                severe.CacheVariant != normal.CacheVariant, "a severe hunting wound changes the summary and the cache variant");
            Require(fixture.Data.Narrative.Instances.Count == instancesBefore, "personal stories are not saved as narrative cases");
            // 작성 표에 사연 행이 없으면 코드 기본 문구로 말한다.
            var withoutStory = Copy(new BundledNarrativeContentSource().Read());
            withoutStory.Contents.RemoveAll(c => c.Kind == NarrativeContentKind.PersonalStory);
            var tables = new List<DungeonTable> { fixture.Table };
            fixture.Data.NarrativeCatalog.Initialize(new FixedNarrativeSource(withoutStory), tables, fixture.Monster, fixture.Item);
            try
            {
                Require(WoundTopic(npc, wounded, now)?.BaseLine == "지난 사냥에서 죽다 살아났소. 이만하길 천만다행이지.",
                    "a pack without the story row falls back to the code line");
            }
            finally { fixture.Data.NarrativeCatalog.Initialize(new BundledNarrativeContentSource(), tables, fixture.Monster, fixture.Item); }
            report.AppendLine("PASS: recent-wound personal story (3001): hidden without a record, sheet line with the speaker's wound summary, " +
                              "severe variant, no saved case, code fallback.");
        }

        private static DialogueTopic WoundTopic(NpcManager npc, int npcId, float now)
        {
            var topics = new List<DialogueTopic>();
            DialogueTopicSelector.CollectPersonalTopics(topics, npcId, npc.GetSaveData(npcId), npc, now);
            return topics.Find(t => t.Type == DialogueTopicType.RecentHunt);
        }

        private static async Task ExpectDeedReaction(NarrativeDiagnosticFixture fixture, int npcId, NarrativeRelevance relevance, int gold,
            NarrativePolicySnapshot policy)
        {
            NpcManager npc = fixture.Npc;
            NpcSaveData save = npc.GetSaveData(npcId);
            PlayerRelation before = npc.GetPlayerRelation(npcId);
            Require(npc.FindNarrativeConversation(npcId).Kind == NarrativeConversationKind.DeedReactions, "deed reaction available for " + npcId);
            var input = new NarrativeReactionRules.Input(save.NpcStat, before, IsGlory(save));
            var rule = NarrativeReactionRules.Select(policy.Reactions, PlayerDeedKind.RumorResolved, false, input);
            NarrativeReactionRules.RelationChange(rule.RelationLevel, relevance, out int affinity, out _, out int intimacy);
            NarrativeDialogueResult result = await npc.RunNarrativeConversationAsync(npcId, new NarrativeConversation(NarrativeConversationKind.DeedReactions, string.Empty));
            Require(result != null && npc.GetPlayerRelation(npcId).Affinity == before.Affinity + NarrativeReactionRules.LimitConversationAffinity(affinity) &&
                npc.GetPlayerRelation(npcId).Intimacy == before.Intimacy + intimacy && fixture.Data.Player.Gold == gold &&
                save.Rumors.Exists(r => r.Kind == NpcRumorKind.PlayerDeed && r.Reacted),
                $"deed reaction for {npcId} uses relevance {relevance} without material reward");
        }

        private static void ValidatePinnedRotation(int npcId, DialogueTopic pinned)
        {
            var topics = new List<DialogueTopic> { pinned };
            DialogueTopicType[] types = { DialogueTopicType.ResourceShortage, DialogueTopicType.CraftMaterial, DialogueTopicType.Medicine,
                DialogueTopicType.SmallTalk, DialogueTopicType.JobMood };
            for (int i = 0; i < types.Length; i++)
                topics.Add(new DialogueTopic { Type = types[i], Category = i < 3 ? DialogueTopicCategory.Need : DialogueTopicCategory.Flavor, BaseLine = types[i].ToString() });
            var repertoire = (List<DialogueTopic>)typeof(DialogueTopicSelector).GetMethod("BuildRepertoire", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { npcId, topics, AffinityTier.Stranger });
            Require(repertoire.Contains(pinned) && repertoire.Count >= 2, "pinned topic always enters the narrow stranger repertoire");
            UnityEngine.Random.InitState(7);
            var recent = new HashSet<DialogueTopicType>();
            int picks = 0;
            for (int i = 0; i < 8; i++)
                if (DialogueTopicSelector.Select(npcId, topics, AffinityTier.Stranger, recent) == pinned) picks++;
            Require(picks >= 1 && picks < 8, $"pinned topic alternates with other topics ({picks}/8)");
        }

        private static void ValidateDialogueBinding(NarrativeDiagnosticFixture fixture, int npcId)
        {
            var view = (ARPG.UI.UINpcInspect)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ARPG.UI.UINpcInspect));
            Set(view, "_viewOpen", true); Set(view, "_entityId", npcId); Set(view, "_bindingVersion", 1); Set(view, "_dialogueRequestId", 2);
            Set(view, "_narrativeLifetime", fixture.Npc.Narrative.Lifetime);
            Require((bool)Invoke(view, "IsCurrentDialogue", npcId, 1, 2), "current dialogue binding accepted");
            Set(view, "_viewOpen", false);
            Require((bool)Invoke(view, "IsCurrentDialogue", npcId, 1, 2) == false, "closed view rejects late response");
            Set(view, "_viewOpen", true); Set(view, "_bindingVersion", 3);
            Require((bool)Invoke(view, "IsCurrentDialogue", npcId, 1, 2) == false, "rebound NPC view rejects late response");
            Set(view, "_bindingVersion", 1); Set(view, "_dialogueRequestId", 4);
            Require((bool)Invoke(view, "IsCurrentDialogue", npcId, 1, 2) == false, "new request rejects previous response");
            Set(view, "_dialogueRequestId", 2); Set(view, "_narrativeLifetime", fixture.Npc.Narrative.Lifetime - 1);
            Require((bool)Invoke(view, "IsCurrentDialogue", npcId, 1, 2) == false, "new world lifetime rejects old response even with reused NPC ID");
        }

        // ── 보조 ──

        private static bool IsGlory(NpcSaveData save)
        {
            NpcAmbitionProfile profile = NpcAmbitionEvaluator.Evaluate(save.NpcStat);
            return profile.Primary == NpcAmbitionKind.Glory || profile.Secondary == NpcAmbitionKind.Glory;
        }

        private static NpcRumor DeedRumor(int village, int slot, int generation, float when)
            => new NpcRumor { Kind = NpcRumorKind.PlayerDeed, DeedKind = PlayerDeedKind.DungeonCleared, VillageId = village, SlotIndex = slot,
                Generation = generation, EventGameTime = when, SourceNpcId = 30, SubjectNpcId = -1, Value = 100 };

        private static void NarrativeSheetTsv(NarrativeContentPack pack, string[] contentColumns, string[] reactionColumns,
            out string contents, out string reactions, bool blankOptional = false)
        {
            string[] optional = { "GoalType", "TargetCount", "StartConditionGroupId", "Revision", "Enabled" };
            var contentRows = new List<Dictionary<string, string>>();
            for (int i = 0; i < pack.Contents.Count; i++)
            {
                NarrativeContentDefinition c = pack.Contents[i];
                contentRows.Add(new Dictionary<string, string>
                {
                    { "ContentId", c.ContentId.ToString() }, { "Name", c.Name }, { "Kind", c.Kind.ToString() },
                    { "TargetType", c.TargetType.ToString() }, { "TargetId", c.TargetId.ToString() }, { "IntroText", c.IntroText },
                    { "SignText", c.SignText }, { "ResolvedText", c.ResolvedText }, { "GoalType", c.GoalType.ToString() },
                    { "TargetCount", c.TargetCount.ToString() }, { "StartConditionGroupId", c.StartConditionGroupId.ToString() },
                    { "Revision", c.Revision.ToString() }, { "Enabled", c.Enabled ? "TRUE" : "FALSE" },
                });
                if (blankOptional)
                    for (int o = 0; o < optional.Length; o++) contentRows[i][optional[o]] = string.Empty;
            }
            var reactionRows = new List<Dictionary<string, string>>();
            for (int i = 0; i < pack.Reactions.Count; i++)
            {
                NarrativeReactionDefinition r = pack.Reactions[i];
                reactionRows.Add(new Dictionary<string, string>
                {
                    { "ReactionRuleId", r.ReactionRuleId.ToString() }, { "Note", r.Note }, { "Requirement", r.Requirement.ToString() },
                    { "Kind", r.Kind.ToString() }, { "BaseScore", r.BaseScore.ToString() }, { "ScoreProfile", r.ScoreProfile.ToString() },
                    { "Response", r.Response.ToString() }, { "RelationLevel", r.RelationLevel.ToString() }, { "Text", r.Text },
                    { "Audience", r.Audience.ToString() },
                });
            }
            contents = Tsv(contentRows, contentColumns);
            reactions = Tsv(reactionRows, reactionColumns);
        }

        private static string Tsv(List<Dictionary<string, string>> rows, string[] columns)
        {
            var lines = new List<string> { string.Join("\t", columns) };
            for (int i = 0; i < rows.Count; i++)
            {
                var cells = new string[columns.Length];
                for (int c = 0; c < columns.Length; c++) cells[c] = "\"" + rows[i][columns[c]].Replace("\"", "\"\"") + "\"";
                lines.Add(string.Join("\t", cells));
            }
            return string.Join("\r\n", lines);
        }

        private static void ExpectNarrativeFailure(Action action, string contract)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Require(rejected, "narrative sheet rejection: " + contract);
        }

        private static void ExpectInvalidPack(NarrativeContentPack pack, List<DungeonTable> tables, List<MonsterTable> monsters,
            List<ItemTable> items, Action<NarrativeContentPack> mutate, string contract)
        {
            var invalid = Copy(pack);
            mutate(invalid);
            ExpectNarrativeFailure(() => ARPG.Editor.DownloadTables.ValidateNarrativePack(invalid, tables, monsters, items), contract);
        }

        private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

        private static object Invoke(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

        private sealed class FixedNarrativeSource : INarrativeContentSource
        {
            private readonly NarrativeContentPack _pack;
            public FixedNarrativeSource(NarrativeContentPack pack) => _pack = pack;
            public NarrativeContentPack Read() => _pack;
        }

        /// <summary>프로젝트 안의 임시 소유자 그래프. 씬·플레이어 세이브·두 번째 프로젝트에 쓰지 않는다.</summary>
        private sealed class NarrativeDiagnosticFixture : IDisposable
        {
            private readonly GameObject _go;
            private readonly FieldInfo _singleton = typeof(PrefabSingleton<AR>).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object _previousAr;
            private readonly UnityEngine.Random.State _previousRandom = UnityEngine.Random.state;
            private readonly string _path;
            private readonly List<MonsterTable> _monsters;
            private readonly List<ItemTable> _items;
            public AR Ar { get; }
            public DataManager Data => Ar.Data;
            public NpcManager Npc => Ar.Npc;
            public ComponentManager Components => Ar.Component;
            public WorldData World { get; private set; }
            public DungeonTable Table { get; }
            public string SlotPath => Path.Combine(_path, "Slot_0");
            public int PlayerId => 1;
            public int RumorVillage { get; }
            public int SecondVillage { get; }
            public int GenericVillage { get; }
            public int VisitedVillage { get; }
            public int EmptyVillage { get; }
            public int TotalSlots { get; private set; }
            public Func<int, MonsterTable> Monster => id => _monsters.Find(m => m.Id == id);
            public Func<int, ItemTable> Item => id => _items.Find(i => i.Id == id);

            public NarrativeDiagnosticFixture(List<DungeonTable> tables, List<MonsterTable> monsters, List<ItemTable> items)
            {
                _monsters = monsters;
                _items = items;
                Table = tables[0];
                _previousAr = _singleton.GetValue(null);
                _path = Path.GetFullPath(Path.Combine("Temp", "DungeonDiagnostics", Guid.NewGuid().ToString("N")));
                _go = UnityEngine.Object.Instantiate(Resources.Load<AR>("AR").gameObject);
                _go.name = "NarrativeDiagnostic"; _go.hideFlags = HideFlags.HideAndDontSave;
                _go.SetActive(false);
                Ar = _go.GetComponent<AR>(); _singleton.SetValue(null, Ar);
                try
                {
                    InvalidateComponentCaches();
                    Set(Ar, "_playerManager", new ARPG.Manager.PlayerManager());
                    World = new WorldData { WorldSeed = 12345 };
                    Set(Data, "_worldData", World); Set(Data, "_currentPlayerEntityId", PlayerId);
                    var player = new PlayerData { PlayerId = PlayerId, CurrentHp = 100, CurrentMp = 100, Gold = 50 };
                    for (int i = 0; i < GlobalEnum.PLAYER_INVENTORY_SLOTCOUNT_MAX; i++) player._inventory.Add(null);
                    Set(Data, "_playerDataList", new List<PlayerData> { player });
                    Set(Data, "_editorMainThreadId", Environment.CurrentManagedThreadId);
                    Data.DiagnosticSavePath = _path;
                    // 격리 DataManager는 표를 읽지 않는다. 던전 세대 선정·행적 가치·소망 아이템이 쓰는 던전·몬스터·아이템 표만 넣는다.
                    Set(Data, "_dungeonTable", System.Collections.Immutable.ImmutableDictionary.CreateRange(
                        tables.ConvertAll(t => new KeyValuePair<int, DungeonTable>(t.Id, t))));
                    Set(Data, "_monsterTable", System.Collections.Immutable.ImmutableDictionary.CreateRange(
                        monsters.ConvertAll(m => new KeyValuePair<int, MonsterTable>(m.Id, m))));
                    Set(Data, "_itemTable", System.Collections.Immutable.ImmutableDictionary.CreateRange(
                        items.ConvertAll(i => new KeyValuePair<int, ItemTable>(i.Id, i))));
                    // 소망 전달은 실제 인벤토리 경로를 쓴다 (선물 목록과 같은 슬롯·변경 표시).
                    Ar.Player.InitializePlayerData();
                    Data.NarrativeCatalog.Initialize(new BundledNarrativeContentSource(), tables, Monster, Item);
                    BindSourceGetter(Npc); BindSourceGetter(Ar.Village);
                    // 결정론 굴림으로 각 역할의 마을을 고른다: 슬롯 0에 소문이 붙는 마을 둘·빈 마을·방문 마을, 붙지 않는 마을 하나.
                    int channel = (int)typeof(NpcManager).GetField("DungeonRumorAttachChannel", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
                    var attaching = new List<int>();
                    int generic = -1;
                    for (int v = 0; v < 400 && (attaching.Count < 4 || generic < 0); v++)
                    {
                        bool attach = DungeonLayout.Hash(World.WorldSeed, v, 0, 0, channel) % 100u < NpcManager.DungeonRumorAttachPercent;
                        if (attach && attaching.Count < 4) attaching.Add(v);
                        else if (attach == false && generic < 0) generic = v;
                    }
                    Require(attaching.Count == 4 && generic >= 0, "fixture villages");
                    RumorVillage = attaching[0]; SecondVillage = attaching[1]; EmptyVillage = attaching[2]; VisitedVillage = attaching[3];
                    GenericVillage = generic;
                    int[] villages = { RumorVillage, SecondVillage, GenericVillage, VisitedVillage, EmptyVillage };
                    for (int i = 0; i < villages.Length; i++)
                    {
                        var village = new VillageData(villages[i], new Vector2(villages[i] * 200f, 0f));
                        ((Dictionary<int, VillageData>)GetPrivate(Ar.Village, "_villages")).Add(villages[i], village);
                        ((List<VillageData>)GetPrivate(Ar.Village, "_villageList")).Add(village);
                        var slots = DungeonLayout.CreateSlots(World.WorldSeed, villages[i], Table.Id);
                        for (int s = 0; s < slots.Slots.Count; s++)
                        {
                            slots.Slots[s].HasEntrance = true;
                            slots.Slots[s].EntranceX = villages[i] * 200 + 20 + s * 12;
                            slots.Slots[s].EntranceY = 20;
                        }
                        World.Dungeons.Villages.Add(slots);
                        TotalSlots += slots.Slots.Count;
                    }
                    World.Dungeons.Dungeons.Add(DungeonLayout.Generate(World.WorldSeed, VisitedVillage, Slot(VisitedVillage, 0), Table));
                    AddResident(10, RumorVillage, 90, 40);
                    AddResident(11, RumorVillage, 30, 85);
                    AddResident(12, RumorVillage, 40, 30, pride: 85);
                    AddResident(13, RumorVillage, 50, 50);
                    AddResident(14, RumorVillage, 50, 50);
                    AddResident(20, SecondVillage, 50, 60);
                    AddResident(21, SecondVillage, 50, 50);
                    AddResident(30, GenericVillage, 50, 50);
                    World.NpcCompanion.ReservedNpcId = 13;
                    Set(Ar.Dungeon, "_components", Components); Set(Ar.Dungeon, "_initialized", true);
                    Set(Ar.WorldMap, "_renderedChunks", new HashSet<Vector2Int>());
                    Reindex();
                    Invoke(Npc, "InitializeNarrative");
                }
                catch { Dispose(); throw; }
            }

            public void AddResident(int id, int villageId, int curiosity, int friendliness, int pride = 50)
            {
                var save = new NpcSaveData(1, Vector2.zero)
                {
                    EntityId = id, VillageId = villageId, Name = "Narrative NPC " + id,
                    NpcStat = new NpcStatComponent { Curiosity = curiosity, Friendliness = friendliness, Courage = 50, Pride = pride,
                        Honesty = 50, Greed = 50, Loyalty = 50, Patience = 50 },
                };
                ((Dictionary<int, NpcSaveData>)GetPrivate(Npc, "_npcSaveDict")).Add(id, save);
                World.NpcSaveDatas.Add(id, save);
                Ar.Village.GetVillage(villageId).NpcEntityIds.Add(id);
            }

            public DungeonSlot Slot(int villageId, int slotIndex)
                => World.Dungeons.Villages.Find(v => v.VillageId == villageId).Slots.Find(s => s.SlotIndex == slotIndex);

            public int ExpectedOwner(int villageId, DungeonSlot slot, int excludedCompanion)
            {
                var candidates = new List<int>(Ar.Village.GetVillage(villageId).NpcEntityIds);
                candidates.Remove(excludedCompanion);
                candidates.Sort();
                int channel = (int)typeof(NpcManager).GetField("RumorOwnerChannel", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
                return candidates[(int)(DungeonLayout.Hash(World.WorldSeed, villageId, slot.SlotIndex, slot.Generation, channel) % (uint)candidates.Count)];
            }

            public int[] ResidentsExcept(int villageId, int owner, int companion)
            {
                var ids = new List<int>(Ar.Village.GetVillage(villageId).NpcEntityIds);
                ids.Remove(owner); ids.Remove(companion);
                ids.Sort();
                return ids.ToArray();
            }

            public void Activate(DungeonRecord record)
            { Set(Ar.Dungeon, "_active", record); Set(Ar.Dungeon, "<ActiveMap>k__BackingField", Ar.WorldMap); }
            public void Deactivate()
            { Set(Ar.Dungeon, "<ActiveMap>k__BackingField", null); Set(Ar.Dungeon, "_active", null); }

            public void Kill(int entity, int attacker, int objectId, bool boss)
            {
                var stat = new StatComponent(); stat.SetCurrentHpDirect(10); Components.SetComponent(entity, stat);
                Components.SetComponent(entity, new DungeonObjectComponent { DungeonId = ((DungeonRecord)GetPrivate(Ar.Dungeon, "_active")).DungeonId,
                    ObjectId = objectId, IsBoss = boss });
                DamageCalculator.ApplyDamageResult(-1, attacker, entity, new DamageResult { FinalDamage = 100 });
                new ARPG.Systems.System_HpCheck().OnFixedUpdate(0.02f);
                Require(Components.TryGetComponent<KillCreditComponent>(entity, out var credit) && credit.SourceEntityId == attacker,
                    "lethal source retained through HP pipeline");
            }

            public void Restore(object snapshot)
            {
                // 실제 manifest reader가 읽은 소유자로 원장·NPC·관계를 다시 만든다 (맵·마을 생성은 하지 않는다).
                var playerRoot = (PlayerSaveRoot)Field(snapshot, "Player");
                var worldRoot = (WorldSaveRoot)Field(snapshot, "World");
                World = new WorldData { WorldSeed = worldRoot.WorldSeed, Dungeons = (DungeonSaveRoot)Field(snapshot, "Dungeons"),
                    Narrative = (NarrativeSaveRoot)Field(snapshot, "Narrative"),
                    NpcSaveDatas = (Dictionary<int, NpcSaveData>)Field(snapshot, "Npcs"), PlayerNpcRelationList = playerRoot.NpcRelations,
                    NpcCompanion = worldRoot.NpcCompanion };
                Set(Data, "_worldData", World); Set(Data, "_playerDataList", playerRoot.Players);
                Ar.Player.InitializePlayerData();
                var dict = (Dictionary<int, NpcSaveData>)GetPrivate(Npc, "_npcSaveDict"); dict.Clear();
                foreach (var pair in World.NpcSaveDatas) dict.Add(pair.Key, pair.Value);
                var relations = (Dictionary<int, PlayerRelation>)GetPrivate(Npc, "_playerRelations"); relations.Clear();
                for (int i = 0; i < World.PlayerNpcRelationList.Count; i++)
                    relations.Add(World.PlayerNpcRelationList[i].NpcId, World.PlayerNpcRelationList[i].ToRelation());
                Npc.RebuildSaveSnapshot();
                Reindex();
                Invoke(Npc, "InitializeNarrative");
            }

            public void Reindex()
            {
                var villages = (Dictionary<int, VillageDungeonSlots>)GetPrivate(Ar.Dungeon, "_villageSlots"); villages.Clear();
                var slots = (Dictionary<string, DungeonSlot>)GetPrivate(Ar.Dungeon, "_slotsById"); slots.Clear();
                var owners = (Dictionary<string, VillageDungeonSlots>)GetPrivate(Ar.Dungeon, "_ownersById"); owners.Clear();
                foreach (var v in Data.Dungeons.Villages)
                { villages.Add(v.VillageId, v); foreach (var s in v.Slots) { slots.Add(s.DungeonId, s); owners.Add(s.DungeonId, v); } }
            }

            public void Dispose()
            {
                _singleton.SetValue(null, _previousAr); InvalidateComponentCaches();
                UnityEngine.Random.state = _previousRandom;
                UnityEngine.Object.DestroyImmediate(_go);
                DeleteDiagnosticDirectory(_path);
            }

            private static object GetPrivate(object owner, string name)
                => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
            private static void BindSourceGetter(object owner)
                => Set(owner, "_saveSourceGetter", Delegate.CreateDelegate(typeof(Func<int, object>), owner,
                    owner.GetType().GetMethod("GetSaveSource", BindingFlags.Instance | BindingFlags.NonPublic)));
            private static void InvalidateComponentCaches()
            {
                var invalidators = (List<Action>)typeof(ComponentManager).GetField("_cacheInvalidators", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                for (int i = 0; i < invalidators.Count; i++) invalidators[i]();
            }
        }
    }
}
