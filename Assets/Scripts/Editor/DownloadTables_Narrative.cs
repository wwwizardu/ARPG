using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ARPG.Npc.Narrative;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// NPC 대화 콘텐츠 작성 팩 다운로드 (NPC_NARRATIVE_CONTENT_DESIGN §5.1). NarrativeContent·NarrativeReaction 두 탭이 한 팩이며
    /// 절반만 저장하지 않는다. 모든 셀은 값 하나이고 JSON 셀은 받지 않는다. 검증에 실패하면 기존 번들을 그대로 둔다.
    /// </summary>
    public partial class DownloadTables
    {
        // ARPG Tables 시트의 NarrativeContent·NarrativeReaction 탭 gid
        private const string _narrativeContentSheet = "1817791258&range=A:M";
        private const string _narrativeReactionSheet = "486265495&range=A:J";
        private const string _narrativePackPath = "Resources/Narrative/NarrativeContent.json";

        internal static readonly string[] NarrativeContentColumns =
        {
            "ContentId", "Name", "Kind", "TargetType", "TargetId", "IntroText", "SignText", "ResolvedText",
            "GoalType", "TargetCount", "StartConditionGroupId", "Revision", "Enabled",
        };
        // 빈칸이면 기본값을 쓰는 선택 열 (§2.2): GoalType=Default, TargetCount=1, StartConditionGroupId=0, Revision=1, Enabled=true.
        private static readonly string[] _narrativeContentOptional =
        {
            "SignText", "ResolvedText", "GoalType", "TargetCount", "StartConditionGroupId", "Revision", "Enabled",
        };
        internal static readonly string[] NarrativeReactionColumns =
        {
            "ReactionRuleId", "Note", "Requirement", "Kind", "BaseScore", "ScoreProfile", "Response", "RelationLevel", "Text", "Audience",
        };
        // Audience 빈칸 = All (부탁한 사람·소망 주인과 소문으로 들은 NPC 모두)
        private static readonly string[] _narrativeReactionOptional = { "Note", "Audience" };

        [MenuItem("ARPG/Download Table/NarrativeContentTable", false, 100)]
        private static async void DownloadNarrativeContentTable()
        {
            NarrativeContentPack pack = await DownloadNarrativePackAsync(null, null, null);
            if (pack == null || SaveNarrativePack(pack) == false) return;
            AssetDatabase.Refresh();
            Debug.Log($"[DownloadTables] NarrativeContent + NarrativeReaction downloaded: {pack.Contents.Count} contents, {pack.Reactions.Count} reactions.");
        }

        /// <summary>두 탭의 TSV를 팩으로 바꾼다. Enabled=false 행은 배포 팩에서 뺀다. 오류에는 탭·행·열을 적는다.</summary>
        internal static NarrativeContentPack ParseNarrativeSheets(string contents, string reactions)
        {
            var pack = new NarrativeContentPack();
            List<Dictionary<string, string>> contentRows = ParseNarrativeRows(contents, NarrativeContentColumns, _narrativeContentOptional, "NarrativeContent");
            for (int i = 0; i < contentRows.Count; i++)
            {
                Dictionary<string, string> row = contentRows[i];
                string where = "NarrativeContent row " + row["#row"];
                var content = new NarrativeContentDefinition
                {
                    ContentId = ParseNarrativeInt(row, "ContentId", 0, where),
                    Name = row["Name"],
                    Kind = ParseNarrativeEnum<NarrativeContentKind>(row, "Kind", NarrativeContentKind.None, where),
                    TargetType = ParseNarrativeEnum<NarrativeTargetType>(row, "TargetType", NarrativeTargetType.None, where),
                    TargetId = ParseNarrativeInt(row, "TargetId", 0, where),
                    IntroText = row["IntroText"],
                    SignText = row["SignText"],
                    ResolvedText = row["ResolvedText"],
                    GoalType = ParseNarrativeEnum(row, "GoalType", NarrativeGoalType.Default, where),
                    TargetCount = ParseNarrativeInt(row, "TargetCount", 1, where),
                    StartConditionGroupId = ParseNarrativeInt(row, "StartConditionGroupId", 0, where),
                    Revision = ParseNarrativeInt(row, "Revision", 1, where),
                    Enabled = ParseNarrativeBool(row, "Enabled", true, where),
                };
                if (content.Enabled == true) pack.Contents.Add(content);
            }
            List<Dictionary<string, string>> reactionRows = ParseNarrativeRows(reactions, NarrativeReactionColumns, _narrativeReactionOptional, "NarrativeReaction");
            for (int i = 0; i < reactionRows.Count; i++)
            {
                Dictionary<string, string> row = reactionRows[i];
                string where = "NarrativeReaction row " + row["#row"];
                pack.Reactions.Add(new NarrativeReactionDefinition
                {
                    ReactionRuleId = ParseNarrativeInt(row, "ReactionRuleId", 0, where),
                    Note = row["Note"],
                    Requirement = ParseNarrativeEnum<NarrativeReactionRequirement>(row, "Requirement", NarrativeReactionRequirement.None, where),
                    Kind = ParseNarrativeEnum<ScenarioReactionKind>(row, "Kind", ScenarioReactionKind.Relieved, where),
                    BaseScore = ParseNarrativeInt(row, "BaseScore", 0, where),
                    ScoreProfile = ParseNarrativeEnum<NarrativeReactionScoreProfile>(row, "ScoreProfile", NarrativeReactionScoreProfile.None, where),
                    Response = ParseNarrativeEnum<NarrativeResponseKind>(row, "Response", NarrativeResponseKind.None, where),
                    RelationLevel = ParseNarrativeEnum<NarrativeRelationLevel>(row, "RelationLevel", NarrativeRelationLevel.None, where),
                    Text = row["Text"],
                    Audience = ParseNarrativeEnum(row, "Audience", NarrativeReactionAudience.All, where),
                });
            }
            if (pack.Contents.Count == 0) throw new InvalidDataException("NarrativeContent contains no enabled rows.");
            return pack;
        }

        internal static void ValidateNarrativePack(NarrativeContentPack pack, List<DungeonTable> dungeons, List<MonsterTable> monsters,
            List<ItemTable> items)
        {
            if (dungeons == null || monsters == null || items == null)
                throw new InvalidDataException("Dungeon/Monster/Item reference tables are missing.");
            NarrativeContentCatalog.Validate(pack, dungeons, id => monsters.Find(t => t.Id == id), id => items.Find(t => t.Id == id));
        }

        private static async Task<NarrativeContentPack> DownloadNarrativePackAsync(List<DungeonTable> dungeons, List<MonsterTable> monsters,
            List<ItemTable> items)
        {
            try
            {
                const string prefix = "https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/export?format=tsv&gid=";
                var sheets = await Task.WhenAll(DownloadTableData(prefix + _narrativeContentSheet), DownloadTableData(prefix + _narrativeReactionSheet));
                NarrativeContentPack pack = ParseNarrativeSheets(sheets[0], sheets[1]);
                // 전체 다운로드는 새로 받은 참조 표를, 단독 다운로드는 현재 게임 표를 쓴다.
                if (dungeons == null) dungeons = ReadNarrativeReferenceTable<DungeonTable>();
                if (monsters == null) monsters = ReadNarrativeReferenceTable<MonsterTable>();
                if (items == null) items = ReadNarrativeReferenceTable<ItemTable>();
                ValidateNarrativePack(pack, dungeons, monsters, items);
                return pack;
            }
            catch (Exception exception)
            {
                Debug.LogError("[DownloadTables] Narrative content download rejected; existing content preserved. " + exception.Message);
                return null;
            }
        }

        private static List<T> ReadNarrativeReferenceTable<T>()
        {
            string path = Path.Combine(Application.dataPath, Data.DataManager.TablePath, typeof(T).Name + ".bytes");
            return JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path));
        }

        private static bool SaveNarrativePack(NarrativeContentPack pack)
        {
            string path = Path.Combine(Application.dataPath, _narrativePackPath);
            try
            {
                WriteNarrativePack(path, pack);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[DownloadTables] Narrative content write failed. " + exception.Message);
                return false;
            }
        }

        /// <summary>임시 파일에 쓴 뒤 교체한다. 실패하면 기존 파일과 디렉터리를 그대로 둔다.</summary>
        internal static void WriteNarrativePack(string path, NarrativeContentPack pack)
        {
            string json = JsonConvert.SerializeObject(pack, Formatting.Indented, NarrativeContentCatalog.PackSettings);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        // 머리글 이름으로 열을 찾는다(순서 무관). 모든 열이 있어야 하고 필수 열은 빈칸일 수 없다. 빈 줄은 건너뛴다.
        private static List<Dictionary<string, string>> ParseNarrativeRows(string text, string[] columns, string[] optional, string sheet)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(sheet + " download is empty.");
            List<List<string>> rows = ParseNarrativeTsv(text);
            if (rows.Count == 0) throw new InvalidDataException(sheet + " header is missing.");
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 0; c < rows[0].Count; c++)
            {
                string name = rows[0][c].Trim().TrimStart('﻿');
                if (Array.IndexOf(columns, name) < 0 || indexes.ContainsKey(name))
                    throw new InvalidDataException(sheet + " unknown/duplicate column: " + name);
                indexes.Add(name, c);
            }
            if (indexes.Count != columns.Length) throw new InvalidDataException(sheet + " required columns are missing.");
            var result = new List<Dictionary<string, string>>();
            for (int r = 1; r < rows.Count; r++)
            {
                if (rows[r].TrueForAll(string.IsNullOrWhiteSpace)) continue;
                if (rows[r].Count != columns.Length)
                    throw new InvalidDataException(sheet + " row " + (r + 1) + ": wrong column count.");
                var row = new Dictionary<string, string>(StringComparer.Ordinal) { { "#row", (r + 1).ToString(CultureInfo.InvariantCulture) } };
                for (int c = 0; c < columns.Length; c++)
                {
                    string raw = rows[r][indexes[columns[c]]];
                    if (string.IsNullOrWhiteSpace(raw) && Array.IndexOf(optional, columns[c]) < 0)
                        throw new InvalidDataException(sheet + " row " + (r + 1) + " column " + columns[c] + ": required.");
                    row.Add(columns[c], string.IsNullOrWhiteSpace(raw) ? string.Empty : raw);
                }
                result.Add(row);
            }
            return result;
        }

        private static int ParseNarrativeInt(Dictionary<string, string> row, string column, int fallback, string where)
        {
            string raw = row[column].Trim();
            if (raw.Length == 0) return fallback;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) == false)
                throw new InvalidDataException(where + " column " + column + ": not an integer '" + raw + "'.");
            return value;
        }

        // enum은 이름만 받는다. 숫자·정의되지 않은 이름은 거절한다 (§1.1).
        private static T ParseNarrativeEnum<T>(Dictionary<string, string> row, string column, T fallback, string where) where T : struct, Enum
        {
            string raw = row[column].Trim();
            if (raw.Length == 0) return fallback;
            if (int.TryParse(raw, out _) || Enum.TryParse(raw, false, out T value) == false || Enum.IsDefined(typeof(T), value) == false)
                throw new InvalidDataException(where + " column " + column + ": unknown " + typeof(T).Name + " '" + raw + "'.");
            return value;
        }

        private static bool ParseNarrativeBool(Dictionary<string, string> row, string column, bool fallback, string where)
        {
            string raw = row[column].Trim();
            if (raw.Length == 0) return fallback;
            if (string.Equals(raw, "TRUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "FALSE", StringComparison.OrdinalIgnoreCase)) return false;
            throw new InvalidDataException(where + " column " + column + ": expected TRUE or FALSE, got '" + raw + "'.");
        }

        // Google TSV는 따옴표·탭·줄바꿈이 든 셀을 따옴표로 감싼다. 줄 단위로 자르면 대사가 깨진다.
        private static List<List<string>> ParseNarrativeTsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            bool closed = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else cell.Append(ch);
                    continue;
                }
                if (ch == '\t' || ch == '\r' || ch == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (ch != '\t')
                    {
                        rows.Add(row); row = new List<string>();
                        if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                }
                else if (closed) throw new InvalidDataException("Unexpected character after a quoted TSV cell.");
                else if (ch == '"' && cell.Length == 0) quoted = true;
                else cell.Append(ch);
            }
            if (quoted) throw new InvalidDataException("Unclosed quoted TSV cell.");
            if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
