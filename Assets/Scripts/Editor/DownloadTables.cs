using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

using ARPG.Component;
using ARPG.Tables;
using ARPG.Utility;
using Newtonsoft.Json;
using System.Collections;

namespace ARPG.Editor
{
    public enum SaveType
    {
        Binary,
        String,
    }

    public class DownloadTables
    {
        private static Dictionary<Type, object> _tableDic;

        private class TableEntry
        {
            public Type Type;
            public string Sheet;
            public int StartLine;
            public SaveType SaveType;
            public Func<Task<bool>> DownloadAction;
        }

        private static readonly List<TableEntry> _tableEntries = BuildTableEntries();

        private static List<TableEntry> BuildTableEntries()
        {
            var list = new List<TableEntry>();
            Register<CreatureTable>(list, "0&range=A:I");
            Register<AiTable>(list, "947794841&range=A:L");
            Register<MonsterTable>(list, "483012127&range=A:P");
            Register<NpcTable>(list, "1460299278&range=A:O");
            Register<MonsterArchetypeTable>(list, "1141585255&range=A:E");
            Register<StatTable>(list, "318209064&range=A:AJ");
            Register<ItemTable>(list, "2064107837&range=A:R");
            Register<BuildableItemTable>(list, "534887250&range=A:Z");
            // Phase D: JobBonusTable (직업별 시간당 가산 자원)
            Register<JobBonusTable>(list, "470575350&range=A:F");
            Register<DropTable>(list, "1241586373&range=A:K");
            Register<DropCurrencyTable>(list, "2071520432&range=A:V");
            Register<DropEquipmentTable>(list, "1267382287&range=A:V");
            Register<SkillTable>(list, "92727160&range=A:AM");
            // Phase 1: SkillEffect 합성 시스템용 테이블 (+ 스킬 페이지 비용 메타데이터 + Kind 분류 컬럼)
            Register<SkillEffectTable>(list, "1681865950&range=A:M");
            Register<SkillBookTable>(list, "1726438368&range=A:C");
            // 장판(지속 영역 효과) 테이블
            Register<AreaEffectTable>(list, "1891935594&range=A:M");
            Register<BuffTable>(list, "127577579&range=A:J");
            Register<AnimationTable>(list, "747631090&range=A:G");
            Register<ProjectileTable>(list, "1810235418&range=A:G");
            Register<ModTable>(list, "1571193978&range=A:J");
            Register<ModTierTable>(list, "1782637736&range=A:J");
            Register<ItemImplicitTable>(list, "547967325&range=A:D");
            // EquipmentBaseStat: A~N가 실제 데이터, O~S는 시트 가독성용 보조 컬럼(다운로드 제외).
            // 슬롯당 3컬럼(ModId, Value1, Value2) × 4 슬롯 = 12컬럼 + Id + ItemId = 14컬럼.
            Register<EquipmentBaseStatTable>(list, "972309111&range=A:N");
            Register<VillageTable>(list, "441028134&range=A:F");
            Register<VillageStageTable>(list, "467145019&range=A:Q");
            // 청크 Zone(시드 청크 (0,0) 기준 Chebyshev 거리+1)별 몬스터 스폰 파라미터
            Register<ZoneTable>(list, "848445893&range=A:M");
            // 절차적 역사 시스템 (Phase 1) — 카탈로그(재료) + 사건 템플릿
            Register<HistoryEntityTable>(list, "2122459380&range=A:J");
            Register<HistoryTemplateTable>(list, "948737654&range=A:I");
            //Register<BuffEffectTable>(list, "2104311648&range=A:K");
            return list;
        }

        private static void Register<T>(List<TableEntry> list, string sheet, int startLine = 1, SaveType saveType = SaveType.String)
            where T : TableBase, new()
        {
            list.Add(new TableEntry
            {
                Type = typeof(T),
                Sheet = sheet,
                StartLine = startLine,
                SaveType = saveType,
                DownloadAction = () => DownloadTable<T>(sheet, startLine, saveType)
            });
        }

        [MenuItem("ARPG/Download Table/All", false, 1)]
        private static async void DownloadAllTables()
        {
            _tableDic = new();

            for (int i = 0; i < _tableEntries.Count; i++)
                await _tableEntries[i].DownloadAction();

            foreach (var tableType in _tableDic.Keys)
            {
                var tableList = (IList)_tableDic[tableType];
                if (tableList == null)
                {
                    Debug.LogError($"[DownloadTables] DownloadAllTables() - tableList({tableType}) is null");
                    continue;
                }

                SaveTable(tableType.Name, _tableDic[tableType], SaveType.String);
            }

            ValidateAiSetup();

            AssetDatabase.Refresh();

            Debug.Log("download Completed...");
        }

        private static async Task DownloadSingleTable<T>() where T : TableBase, new()
        {
            TableEntry entry = null;
            for (int i = 0; i < _tableEntries.Count; i++)
            {
                if (_tableEntries[i].Type == typeof(T))
                {
                    entry = _tableEntries[i];
                    break;
                }
            }

            if (entry == null)
            {
                Debug.LogError($"[DownloadTables] DownloadSingleTable - No entry registered for {typeof(T).Name}");
                return;
            }

            _tableDic = new();
            await entry.DownloadAction();

            if (_tableDic.TryGetValue(typeof(T), out var tableList) == false || tableList == null)
            {
                Debug.LogError($"[DownloadTables] DownloadSingleTable - {typeof(T).Name} download failed");
                return;
            }

            SaveTable(typeof(T).Name, tableList, entry.SaveType);

            AssetDatabase.Refresh();

            Debug.Log($"download {typeof(T).Name} Completed...");
        }

        // 알파벳 순으로 개별 테이블 다운로드 메뉴 (priority 100 → All과 분리되는 separator)
        [MenuItem("ARPG/Download Table/AiTable", false, 100)]
        private static async void DownloadAiTable() => await DownloadSingleTable<AiTable>();

        [MenuItem("ARPG/Download Table/AnimationTable", false, 100)]
        private static async void DownloadAnimationTable() => await DownloadSingleTable<AnimationTable>();

        [MenuItem("ARPG/Download Table/AreaEffectTable", false, 100)]
        private static async void DownloadAreaEffectTable() => await DownloadSingleTable<AreaEffectTable>();

        [MenuItem("ARPG/Download Table/BuffTable", false, 100)]
        private static async void DownloadBuffTable() => await DownloadSingleTable<BuffTable>();

        [MenuItem("ARPG/Download Table/BuildableItemTable", false, 100)]
        private static async void DownloadBuildableItemTable() => await DownloadSingleTable<BuildableItemTable>();

        [MenuItem("ARPG/Download Table/CreatureTable", false, 100)]
        private static async void DownloadCreatureTable() => await DownloadSingleTable<CreatureTable>();

        [MenuItem("ARPG/Download Table/DropCurrencyTable", false, 100)]
        private static async void DownloadDropCurrencyTable() => await DownloadSingleTable<DropCurrencyTable>();

        [MenuItem("ARPG/Download Table/DropEquipmentTable", false, 100)]
        private static async void DownloadDropEquipmentTable() => await DownloadSingleTable<DropEquipmentTable>();

        [MenuItem("ARPG/Download Table/DropTable", false, 100)]
        private static async void DownloadDropTable() => await DownloadSingleTable<DropTable>();

        [MenuItem("ARPG/Download Table/EquipmentBaseStatTable", false, 100)]
        private static async void DownloadEquipmentBaseStatTable() => await DownloadSingleTable<EquipmentBaseStatTable>();

        [MenuItem("ARPG/Download Table/HistoryEntityTable", false, 100)]
        private static async void DownloadHistoryEntityTable() => await DownloadSingleTable<HistoryEntityTable>();

        [MenuItem("ARPG/Download Table/HistoryTemplateTable", false, 100)]
        private static async void DownloadHistoryTemplateTable() => await DownloadSingleTable<HistoryTemplateTable>();

        [MenuItem("ARPG/Download Table/ItemImplicitTable", false, 100)]
        private static async void DownloadItemImplicitTable() => await DownloadSingleTable<ItemImplicitTable>();

        [MenuItem("ARPG/Download Table/ItemTable", false, 100)]
        private static async void DownloadItemTable() => await DownloadSingleTable<ItemTable>();

        [MenuItem("ARPG/Download Table/JobBonusTable", false, 100)]
        private static async void DownloadJobBonusTable() => await DownloadSingleTable<JobBonusTable>();

        [MenuItem("ARPG/Download Table/ModTable", false, 100)]
        private static async void DownloadModTable() => await DownloadSingleTable<ModTable>();

        [MenuItem("ARPG/Download Table/ModTierTable", false, 100)]
        private static async void DownloadModTierTable() => await DownloadSingleTable<ModTierTable>();

        [MenuItem("ARPG/Download Table/MonsterArchetypeTable", false, 100)]
        private static async void DownloadMonsterArchetypeTable() => await DownloadSingleTable<MonsterArchetypeTable>();

        [MenuItem("ARPG/Download Table/MonsterTable", false, 100)]
        private static async void DownloadMonsterTable() => await DownloadSingleTable<MonsterTable>();

        [MenuItem("ARPG/Download Table/NpcTable", false, 100)]
        private static async void DownloadNpcTable() => await DownloadSingleTable<NpcTable>();

        [MenuItem("ARPG/Download Table/ProjectileTable", false, 100)]
        private static async void DownloadProjectileTable() => await DownloadSingleTable<ProjectileTable>();

        [MenuItem("ARPG/Download Table/SkillBookTable", false, 100)]
        private static async void DownloadSkillBookTable() => await DownloadSingleTable<SkillBookTable>();

        [MenuItem("ARPG/Download Table/SkillEffectTable", false, 100)]
        private static async void DownloadSkillEffectTable() => await DownloadSingleTable<SkillEffectTable>();

        [MenuItem("ARPG/Download Table/SkillTable", false, 100)]
        private static async void DownloadSkillTable() => await DownloadSingleTable<SkillTable>();

        [MenuItem("ARPG/Download Table/StatTable", false, 100)]
        private static async void DownloadStatTable() => await DownloadSingleTable<StatTable>();

        [MenuItem("ARPG/Download Table/VillageStageTable", false, 100)]
        private static async void DownloadVillageStageTable() => await DownloadSingleTable<VillageStageTable>();

        [MenuItem("ARPG/Download Table/VillageTable", false, 100)]
        private static async void DownloadVillageTable() => await DownloadSingleTable<VillageTable>();

        [MenuItem("ARPG/Download Table/ZoneTable", false, 100)]
        private static async void DownloadZoneTable() => await DownloadSingleTable<ZoneTable>();

        /// <summary>
        /// AI 관련 테이블 정합성 검증 — 다운로드 완료 후 실행.
        /// 디자이너에게 시트 입력 실수를 즉시 피드백.
        /// </summary>
        private static void ValidateAiSetup()
        {
            List<AiTable> aiTables = GetTableList<AiTable>();
            List<SkillTable> skillTables = GetTableList<SkillTable>();
            List<MonsterTable> monsterTables = GetTableList<MonsterTable>();
            List<NpcTable> npcTables = GetTableList<NpcTable>();

            if (aiTables == null)
            {
                Debug.LogWarning("[ValidateAi] AiTable이 로드되지 않아 검증 건너뜀");
                return;
            }

            Dictionary<int, AiTable> aiTableById = new();
            for (int i = 0; i < aiTables.Count; i++)
                aiTableById[aiTables[i].Id] = aiTables[i];

            Dictionary<int, SkillTable> skillTableById = new();
            if (skillTables != null)
            {
                for (int i = 0; i < skillTables.Count; i++)
                    skillTableById[skillTables[i].Id] = skillTables[i];
            }

            int errors = 0;
            int warnings = 0;

            // ── Phase 1: AiTable 자체 검증 ──────────────────────────
            for (int i = 0; i < aiTables.Count; i++)
            {
                AiTable ai = aiTables[i];
                int[] skillIds = { ai.SkillId1, ai.SkillId2, ai.SkillId3 };
                int[] weights = { ai.SkillWeight1, ai.SkillWeight2, ai.SkillWeight3 };

                // [3] SkillId broken reference
                for (int s = 0; s < skillIds.Length; s++)
                {
                    if (skillIds[s] == 0) continue;
                    if (skillTableById.ContainsKey(skillIds[s]) == false)
                    {
                        Debug.LogError($"[ValidateAi] AiTable Id={ai.Id}({ai.Name}): SkillId{s + 1}={skillIds[s]} 가 SkillTable에 없음 (broken reference)");
                        errors++;
                    }
                }

                // [4] 스킬은 설정됐는데 모든 SkillWeight=0
                bool hasAnySkill = skillIds[0] > 0 || skillIds[1] > 0 || skillIds[2] > 0;
                int totalWeight = weights[0] + weights[1] + weights[2];
                if (hasAnySkill == true && totalWeight <= 0)
                {
                    Debug.LogWarning($"[ValidateAi] AiTable Id={ai.Id}({ai.Name}): SkillId가 설정됐는데 모든 SkillWeight=0 → AI가 스킬을 절대 발사하지 않음");
                    warnings++;
                }

                // [4-1] Weight>0인데 SkillId=0 (역방향)
                for (int s = 0; s < skillIds.Length; s++)
                {
                    if (weights[s] > 0 && skillIds[s] == 0)
                    {
                        Debug.LogWarning($"[ValidateAi] AiTable Id={ai.Id}({ai.Name}): SkillWeight{s + 1}={weights[s]} > 0인데 SkillId{s + 1}=0 → 가중치 무효");
                        warnings++;
                    }
                }

                // [5] DetectionRange=0
                if (ai.DetectionRange <= 0f)
                {
                    Debug.LogWarning($"[ValidateAi] AiTable Id={ai.Id}({ai.Name}): DetectionRange={ai.DetectionRange} → AI가 적을 감지 못 함");
                    warnings++;
                }
            }

            // ── Phase 2: SkillTable 자체 검증 ───────────────────────
            if (skillTables != null)
            {
                for (int i = 0; i < skillTables.Count; i++)
                {
                    SkillTable sk = skillTables[i];

                    // [2] SkillRangeMin > SkillRangeMax
                    if (sk.SkillRangeMin > 0f && sk.SkillRangeMax > 0f && sk.SkillRangeMin > sk.SkillRangeMax)
                    {
                        Debug.LogError($"[ValidateAi] SkillTable Id={sk.Id}({sk.Name}): SkillRangeMin({sk.SkillRangeMin}) > SkillRangeMax({sk.SkillRangeMax}) → 영원히 발사 불가");
                        errors++;
                    }
                }
            }

            // ── Phase 3: Creature × AiTable 조합 검증 (Check 1) ────
            if (monsterTables != null)
            {
                for (int i = 0; i < monsterTables.Count; i++)
                {
                    ValidateCreatureAiPair("MonsterTable", monsterTables[i].Id, monsterTables[i].Name,
                        monsterTables[i].HitRadius, monsterTables[i].AiTableId,
                        aiTableById, skillTableById, ref errors, ref warnings);
                }
            }
            if (npcTables != null)
            {
                for (int i = 0; i < npcTables.Count; i++)
                {
                    ValidateCreatureAiPair("NpcTable", npcTables[i].Id, npcTables[i].Name,
                        npcTables[i].HitRadius, npcTables[i].AiTableId,
                        aiTableById, skillTableById, ref errors, ref warnings);
                }
            }

            if (errors == 0 && warnings == 0)
                Debug.Log("[ValidateAi] AI 테이블 검증 완료 — 이상 없음");
            else
                Debug.Log($"[ValidateAi] AI 테이블 검증 완료 — Errors: {errors}, Warnings: {warnings}");
        }

        private static void ValidateCreatureAiPair(
            string sourceTable, int creatureId, string creatureName, float creatureHitRadius, int aiTableId,
            Dictionary<int, AiTable> aiTableById, Dictionary<int, SkillTable> skillTableById,
            ref int errors, ref int warnings)
        {
            if (aiTableId == 0) return; // AI 없음

            if (aiTableById.TryGetValue(aiTableId, out AiTable ai) == false)
            {
                Debug.LogError($"[ValidateAi] {sourceTable} Id={creatureId}({creatureName}): AiTableId={aiTableId} 가 AiTable에 없음 (broken reference)");
                errors++;
                return;
            }

            // 보수적 engagement 하한 = attacker.HitRadius + max(SkillRangeMax over slots)
            // target.HitRadius=0 가정 — 이 값보다 KeepDistance가 크면 어떤 타겟이어도 핑퐁
            float maxSkillRange = 0f;
            int[] skillIds = { ai.SkillId1, ai.SkillId2, ai.SkillId3 };
            for (int s = 0; s < skillIds.Length; s++)
            {
                if (skillIds[s] == 0) continue;
                if (skillTableById.TryGetValue(skillIds[s], out SkillTable sk) == false) continue;
                if (sk.SkillRangeMax > maxSkillRange)
                    maxSkillRange = sk.SkillRangeMax;
            }

            // [1] KeepDistance >= conservative engagement → 핑퐁 위험
            if (maxSkillRange > 0f && ai.KeepDistance > 0f)
            {
                float conservativeEngagement = creatureHitRadius + maxSkillRange;
                if (ai.KeepDistance >= conservativeEngagement)
                {
                    Debug.LogWarning($"[ValidateAi] {sourceTable} Id={creatureId}({creatureName}) + AiTable Id={aiTableId}({ai.Name}): " +
                        $"KeepDistance({ai.KeepDistance}) >= attacker.HitRadius({creatureHitRadius}) + maxSkillRangeMax({maxSkillRange}) = {conservativeEngagement} " +
                        $"→ Attack 진입 즉시 Retreat 핑퐁 위험. KeepDistance를 {conservativeEngagement:F2} 미만으로 낮추거나 SkillRangeMax를 늘리세요");
                    warnings++;
                }
            }
        }

        private static List<T> GetTableList<T>() where T : TableBase
        {
            if (_tableDic == null) return null;
            if (_tableDic.TryGetValue(typeof(T), out object obj) == false) return null;
            return obj as List<T>;
        }

        private static async Task<bool> DownloadTable<T>(string inSheet, int inStartLine, SaveType inSaveType) where T : TableBase, new()
        {
            string text = await DownloadTableData($"https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/export?format=tsv&gid={inSheet}");
            var lines = Regex.Split(text, @"\r\n|\n\r|\n|\r");

            Type tableType = typeof(T);

            // 헤더만 있고 데이터 행이 0개인 경우에도 빈 리스트로 등록해 .bytes 파일이 갱신되도록 함.
            // (이 조기 종료가 없으면 옛 .bytes 파일이 그대로 남아 stale 데이터로 동작)
            if (lines.Length <= 1)
            {
                if (_tableDic.ContainsKey(tableType) == false)
                    _tableDic.Add(tableType, new List<T>());
                return true;
            }

            int dataStartLine = inStartLine;

            List<T> tableList = new();
            for (var i = dataStartLine; i < lines.Length; i++)
            {
                CreateTable<T>(lines[i], tableList);
            }

            if (_tableDic.ContainsKey(tableType) == false)
            {
                _tableDic.Add(tableType, tableList);
            }

            return true;
        }

        private static bool CreateTable<T>(string inTableData, List<T> inList) where T : TableBase, new()
        {
            var values = inTableData.Split('\t');

            if (values.Length < 1)
                return false;

            T table = new T();
            
            try
            {
                table.Id = int.Parse(values[0]);

                Debug.Log($"[DownloadTables] CreateTable - Creating table, Table({table}), Id({table.Id})");

                if (table is MonsterTable monsterTable)
                {
                    ParseMonsterTable(monsterTable, values);
                }
                else if (table is MonsterArchetypeTable monsterArchetypeTable)
                {
                    ParseMonsterArchetypeTable(monsterArchetypeTable, values);
                }
                else if (table is NpcTable npcTable)
                {
                    ParseNpcTable(npcTable, values);
                }
                else if (table is CreatureTable creatureTable)
                {
                    ParseCreatureTable(creatureTable, values);
                }
                else if (table is StatTable statTable)
                {
                    ParseStatTable(statTable, values);
                }
                else if (table is ItemTable itemTable)
                {
                    ParseItemTable(itemTable, values);
                }
                else if (table is BuildableItemTable buildableItemTable)
                {
                    ParseBuildableItemTable(buildableItemTable, values);
                }
                else if (table is DropTable dropTable)
                {
                    ParseDropTable(dropTable, values);
                }
                else if (table is DropCurrencyTable dropCurrencyTable)
                {
                    ParseDropCurrencyTable(dropCurrencyTable, values);
                }
                else if (table is DropEquipmentTable dropEquipmentTable)
                {
                    ParseDropEquipmentTable(dropEquipmentTable, values);
                }
                else if (table is SkillTable skillTable)
                {
                    ParseSkillTable(skillTable, values);
                }
                else if (table is SkillEffectTable skillEffectTable)
                {
                    ParseSkillEffectTable(skillEffectTable, values);
                }
                else if (table is SkillBookTable skillBookTable)
                {
                    ParseSkillBookTable(skillBookTable, values);
                }
                else if (table is AiTable aiTable)
                {
                    ParseAiTable(aiTable, values);
                }
                else if (table is BuffTable buffTable)
                {
                    ParseBuffTable(buffTable, values);
                }
                else if (table is BuffEffectTable buffEffectTable)
                {
                    ParseBuffEffectTable(buffEffectTable, values);
                }
                else if (table is AnimationTable animationTable)
                {
                    ParseAnimationTable(animationTable, values);
                }
                else if (table is ProjectileTable projectileTable)
                {
                    ParseProjectileTable(projectileTable, values);
                }
                else if (table is AreaEffectTable areaEffectTable)
                {
                    ParseAreaEffectTable(areaEffectTable, values);
                }
                else if (table is ModTable modTable)
                {
                    ParseModTable(modTable, values);
                }
                else if (table is ModTierTable modTierTable)
                {
                    ParseModTierTable(modTierTable, values);
                }
                else if (table is ItemImplicitTable itemImplicitTable)
                {
                    ParseItemImplicitTable(itemImplicitTable, values);
                }
                else if (table is EquipmentBaseStatTable equipmentBaseStatTable)
                {
                    ParseEquipmentBaseStatTable(equipmentBaseStatTable, values);
                }
                else if (table is VillageTable villageTable)
                {
                    ParseVillageTable(villageTable, values);
                }
                else if (table is VillageStageTable villageStageTable)
                {
                    ParseVillageStageTable(villageStageTable, values);
                }
                else if (table is JobBonusTable jobBonusTable)
                {
                    ParseJobBonusTable(jobBonusTable, values);
                }
                else if (table is ZoneTable zoneTable)
                {
                    ParseZoneTable(zoneTable, values);
                }
                else if (table is HistoryEntityTable historyEntityTable)
                {
                    ParseHistoryEntityTable(historyEntityTable, values);
                }
                else if (table is HistoryTemplateTable historyTemplateTable)
                {
                    ParseHistoryTemplateTable(historyTemplateTable, values);
                }
                else
                {
                    Debug.LogError($"[DownloadTables] CreateTable - Unknown table type: {typeof(T)}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DownloadTables] CreateTable - Parsing error: {ex.Message}");
                return false;
            }

            inList.Add(table);
            return true;
        }

        private static void ParseCreatureTable(CreatureTable table, string[] values)
        {
            if (values.Length < 9)
            {
                Debug.LogError($"[ParseCreatureTable] Invalid data length. Expected at least 9, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.MoveRadius = ParseFloatSafe(values, 4);
            table.HitRadius = ParseFloatSafe(values, 5);
            table.HitOffsetY = ParseFloatSafe(values, 6);
            table.PrefabName = values[7];
            table.AnimationId = int.Parse(values[8]);
        }

        private static void ParseVillageTable(VillageTable table, string[] values)
        {
            if (values.Length < 6)
            {
                Debug.LogError($"[ParseVillageTable] Invalid data length. Expected at least 6, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.DefaultNpcList = values[3];
            table.RespawnCooldown = float.Parse(values[4]);
            table.SpawnRadius = float.Parse(values[5]);
        }

        private static void ParseVillageStageTable(VillageStageTable table, string[] values)
        {
            // 전체 범위: A:Q = 17개 컬럼 (Id, Name, Description, BoundsRadius, ImmigrationCheckHours, ImmigrationArriveChance,
            //                            HireBaseCost, PromoMinPopulation, PromoMinHousing, PromoMinFood, PromoMinAgeHours,
            //                            PromoRequiredSet, PromoRequiredCivic, PromoRequiredShop,
            //                            RoadReserveRadius, RoadReserveHalfWidth, PlazaRadius)
            if (values.Length < 17)
            {
                Debug.LogError($"[ParseVillageStageTable] Invalid data length. Expected at least 17, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.BoundsRadius = ParseIntSafe(values, 3);
            table.ImmigrationCheckHours = ParseFloatSafe(values, 4);
            table.ImmigrationArriveChance = ParseFloatSafe(values, 5);
            table.HireBaseCost = ParseIntSafe(values, 6);
            table.PromoMinPopulation = ParseIntSafe(values, 7);
            table.PromoMinHousing = ParseIntSafe(values, 8);
            table.PromoMinFood = ParseIntSafe(values, 9);
            table.PromoMinAgeHours = ParseFloatSafe(values, 10);
            table.PromoRequiredSet = ParseIntSafe(values, 11);
            table.PromoRequiredCivic = ParseIntSafe(values, 12);
            table.PromoRequiredShop = ParseIntSafe(values, 13);
            table.RoadReserveRadius = ParseIntSafe(values, 14);
            table.RoadReserveHalfWidth = ParseIntSafe(values, 15);
            table.PlazaRadius = ParseIntSafe(values, 16);
        }

        private static void ParseNpcTable(NpcTable table, string[] values)
        {
            if (values.Length < 15)
            {
                Debug.LogError($"[ParseNpcTable] Invalid data length. Expected at least 15, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.MoveRadius = ParseFloatSafe(values, 4);
            table.HitRadius = ParseFloatSafe(values, 5);
            table.HitOffsetY = ParseFloatSafe(values, 6);
            table.PrefabName = values[7];
            table.AnimationId = int.Parse(values[8]);
            table.JobType = (GlobalEnum.JobType)Enum.Parse(typeof(GlobalEnum.JobType), values[9]);
            table.WeaponId = int.Parse(values[10]);
            table.AiTableId = int.Parse(values[11]);
            table.DropId = int.Parse(values[12]);
            table.DropRateBonus = int.Parse(values[13]);
            table.DropRarityBonus = int.Parse(values[14]);
        }

        private static void ParseStatTable(StatTable table, string[] values)
        {
            if (values.Length < 32)
            {
                Debug.LogError($"[ParseStatTable] Invalid data length. Expected at least 32, got {values.Length}. Id: {table.Id}");
                return;
            }

            bool hasMaxResistColumns = values.Length >= 36;
            if (values.Length > 32 && hasMaxResistColumns == false)
            {
                Debug.LogError($"[ParseStatTable] Invalid data length. Expected 32 legacy columns or at least 36 columns with MaxResist, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[0] 은 Id 이다.
            // values[1], values[2] 는 웹에서만 사용한다.

            table.Str = int.Parse(values[3]);
            table.Dex = int.Parse(values[4]);
            table.Int = int.Parse(values[5]);
            table.MaxHp = int.Parse(values[6]);
            table.MaxMp = int.Parse(values[7]);
            table.HpGeneration = int.Parse(values[8]);
            table.MpGeneration = int.Parse(values[9]);
            table.AttackMin = int.Parse(values[10]);
            table.AttackMax = int.Parse(values[11]);
            table.CriRate = int.Parse(values[12]);
            table.CriDamage = int.Parse(values[13]);
            table.MoveSpeed = int.Parse(values[14]);
            table.AttackSpeed = int.Parse(values[15]);
            table.CastSpeed = int.Parse(values[16]);
            table.Defense = int.Parse(values[17]);
            table.FireResist = int.Parse(values[18]);
            table.IceResist = int.Parse(values[19]);
            table.LightningResist = int.Parse(values[20]);
            table.PoisonResist = int.Parse(values[21]);

            int nextIndex = 22;
            if (hasMaxResistColumns)
            {
                table.MaxFireResist = ParseIntSafe(values, nextIndex++, BalanceConstants.BaseMaxResistance);
                table.MaxIceResist = ParseIntSafe(values, nextIndex++, BalanceConstants.BaseMaxResistance);
                table.MaxLightningResist = ParseIntSafe(values, nextIndex++, BalanceConstants.BaseMaxResistance);
                table.MaxPoisonResist = ParseIntSafe(values, nextIndex++, BalanceConstants.BaseMaxResistance);
            }
            else
            {
                table.MaxFireResist = BalanceConstants.BaseMaxResistance;
                table.MaxIceResist = BalanceConstants.BaseMaxResistance;
                table.MaxLightningResist = BalanceConstants.BaseMaxResistance;
                table.MaxPoisonResist = BalanceConstants.BaseMaxResistance;
            }

            table.Luck = int.Parse(values[nextIndex++]);
            table.BloodingRate = int.Parse(values[nextIndex++]);
            table.IgniteRate = int.Parse(values[nextIndex++]);

            // 전투 시스템 확장 스탯 (2026-04-01 추가)
            table.Evasion = int.Parse(values[nextIndex++]);
            table.BlockChance = int.Parse(values[nextIndex++]);
            table.BlockReduction = int.Parse(values[nextIndex++]);
            table.SkillDamage = int.Parse(values[nextIndex++]);
            table.CooldownReduction = int.Parse(values[nextIndex++]);
            table.LifeSteal = int.Parse(values[nextIndex++]);
            table.Thorns = int.Parse(values[nextIndex]);
        }

        private static void ParseMonsterTable(MonsterTable table, string[] values)
        {
            if (values.Length < 15)
            {
                Debug.LogError($"[ParseMonsterTable] Invalid data length. Expected at least 15, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.MoveRadius = ParseFloatSafe(values, 4);
            table.HitRadius = ParseFloatSafe(values, 5);
            table.HitOffsetY = ParseFloatSafe(values, 6);
            table.PrefabName = values[7];
            table.AnimationId = int.Parse(values[8]);
            table.WeaponId = int.Parse(values[9]);
            table.AiTableId = int.Parse(values[10]);
            table.DropId = int.Parse(values[11]);
            table.DropRateBonus = int.Parse(values[12]);
            table.DropRarityBonus = int.Parse(values[13]);
            table.Level = int.Parse(values[14]);
            // Archetype은 시트에 컬럼 있으면 읽음. 없으면 C# 기본값 Normal 유지.
            table.Archetype = ParseEnumSafe(values, 15, GlobalEnum.MonsterArchetype.Normal);
        }

        /// <summary>
        /// MonsterArchetypeTable 파싱.
        /// 컬럼: A=Id, B=Archetype, C=HpMul, D=DmgMul, E=Note
        /// </summary>
        private static void ParseMonsterArchetypeTable(MonsterArchetypeTable table, string[] values)
        {
            if (values.Length < 4)
            {
                Debug.LogError($"[ParseMonsterArchetypeTable] Invalid data length. Expected at least 4, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Archetype = ParseEnumSafe(values, 1, GlobalEnum.MonsterArchetype.Normal);
            table.HpMul = ParseFloatSafe(values, 2);
            table.DmgMul = ParseFloatSafe(values, 3);
            if (values.Length > 4)
            {
                table.Note = values[4];
            }
        }

        private static void ParseItemTable(ItemTable table, string[] values)
        {
            // 전체 범위: A:R = 18개 컬럼 (Phase D에서 4개 추가)
            if (values.Length < 14)
            {
                Debug.LogError($"[ParseItemTable] Invalid data length. Expected at least 14, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);
            table.Name = values[2];
            table.ItemType = (GlobalEnum.ItemType)Enum.Parse(typeof(GlobalEnum.ItemType), values[3]);
            table.Category = (GlobalEnum.ItemCategory)Enum.Parse(typeof(GlobalEnum.ItemCategory), values[4]);
            table.Stackable = values[5].Trim().ToUpper() == "TRUE";
            table.MaxStack = int.Parse(values[6]);
            table.Description = values[7];
            table.DropRate = int.Parse(values[8]);
            table.BuildableItemId = int.Parse(values[9]);
            // values[10] (BaseStatId), values[11] (StatId) 는 dead column. Item.EquipmentBastStatId/EquipmentStatId 필드 제거됨.
            table.SpriteName = values[12];
            table.DropLevel = int.Parse(values[13]);

            // Phase D 신규 컬럼 (시트 갱신 전에는 길이 < 18일 수 있어 안전 가드)
            table.BasePrice = ParseIntSafe(values, 14);
            table.SellRatioBp = ParseIntSafe(values, 15);
            table.ReturnResourceType = ParseIntSafe(values, 16);
            table.ReturnRatioBp = ParseIntSafe(values, 17);
        }

        private static void ParseBuildableItemTable(BuildableItemTable table, string[] values)
        {
            // 전체 범위: A:Z = 26개 컬럼 (Z=MinSeparation, 신규)
            // 컬럼 9 (구 Function)는 유지하되 코드에서 읽지 않음 (시트 정리는 별도 작업)
            if (values.Length < 19)
            {
                Debug.LogError($"[ParseBuildableItemTable] Invalid data length. Expected at least 19, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Tooltip = values[2];
            table.IsBreakable = values[3].Trim().ToUpper() == "TRUE";
            table.HP = int.Parse(values[4]);
            table.DropItemId = int.Parse(values[5]);
            table.Size_Width = int.Parse(values[6]);
            table.Size_Height = int.Parse(values[7]);
            table.Recipe = int.Parse(values[8]);
            // values[9] = 구 Function 컬럼 (Phase D에서 폐기, 데이터 무시)
            table.ResourceName = values[10];
            table.SpawnType = (GlobalEnum.BuildableSpawnType)Enum.Parse(typeof(GlobalEnum.BuildableSpawnType), values[11]);
            table.AnimationId = int.Parse(values[12]);

            // Phase B 신규 컬럼
            table.Cost_Wood = int.Parse(values[13]);
            table.Cost_Stone = int.Parse(values[14]);
            table.StorageCap_Food = int.Parse(values[15]);
            table.StorageCap_Wood = int.Parse(values[16]);
            table.StorageCap_Stone = int.Parse(values[17]);

            // Phase C 신규 컬럼
            table.Cost_Metal = int.Parse(values[18]);

            // Phase D 신규 컬럼 (시트 갱신 전에는 길이 < 25일 수 있어 안전 가드)
            table.ProvidedService = ParseIntSafe(values, 19);
            table.Category = ParseEnumSafe(values, 20, BuildableCategory.None);
            table.SetMembership = ParseIntSafe(values, 21);
            table.AssociatedJobType = ParseIntSafe(values, 22);
            table.BaseWeight = ParseIntSafe(values, 23, defaultValue: 10);
            table.MaxPerVillage = ParseIntSafe(values, 24);
            table.MinSeparation = ParseIntSafe(values, 25);
        }

        private static int ParseIntSafe(string[] values, int index, int defaultValue = 0)
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            return int.TryParse(s, out int v) ? v : defaultValue;
        }

        private static float ParseFloatSafe(string[] values, int index, float defaultValue = 0f)
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            return float.TryParse(s, out float v) ? v : defaultValue;
        }

        /// <summary>
        /// bool 컬럼 파서. "true"/"false" (대소문자 무관), "1"/"0", 빈 칸 모두 허용.
        /// </summary>
        private static bool ParseBoolSafe(string[] values, int index, bool defaultValue = false)
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (bool.TryParse(s, out bool v)) return v;
            if (s == "1") return true;
            if (s == "0") return false;
            return defaultValue;
        }

        private static void ParseHistoryEntityTable(HistoryEntityTable table, string[] values)
        {
            if (values.Length < 10)
            {
                Debug.LogError($"[ParseHistoryEntityTable] Invalid data length. Expected at least 10, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.StringId            = values[1];
            table.DisplayName         = values[2];
            table.Type                = ParseIntSafe(values, 3);
            table.TagsCsv             = values[4];
            table.PreferredTerrainCsv = values[5];
            table.HostileTagsCsv      = values[6];
            table.StartingPopulation  = ParseIntSafe(values, 7);
            table.ThreatLevel         = ParseIntSafe(values, 8);
            table.EndsWithJongseong   = ParseBoolSafe(values, 9);
        }

        private static void ParseHistoryTemplateTable(HistoryTemplateTable table, string[] values)
        {
            if (values.Length < 8)
            {
                Debug.LogError($"[ParseHistoryTemplateTable] Invalid data length. Expected at least 8, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.StringId          = values[1];
            table.Weight            = ParseIntSafe(values, 2);
            table.EraRestrictionCsv = values[3];
            table.CooldownYears     = ParseIntSafe(values, 4);
            table.SlotsJson         = values[5];
            table.TextTemplate      = values[6];
            table.EffectsJson       = values[7];
            // CanTriggerIdsCsv는 옵션 — 마지막 행에서 빈 셀이면 TSV split에 값이 누락될 수 있음
            table.CanTriggerIdsCsv  = values.Length > 8 ? values[8] : string.Empty;
        }

        /// <summary>
        /// enum 컬럼 파서. 문자열("Housing") 또는 정수("1") 모두 허용.
        /// 빈 칸/누락/파싱 실패 시 defaultValue 반환.
        /// </summary>
        private static T ParseEnumSafe<T>(string[] values, int index, T defaultValue) where T : struct, Enum
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (Enum.TryParse<T>(s, ignoreCase: true, out T v) && Enum.IsDefined(typeof(T), v)) return v;
            Debug.LogWarning($"[ParseEnumSafe] '{s}' not a valid {typeof(T).Name} — defaulting to {defaultValue}");
            return defaultValue;
        }

        private static void ParseJobBonusTable(JobBonusTable table, string[] values)
        {
            // 전체 범위: A:F = 6개 컬럼 (Id, JobType, Resource1Type, Resource1PerHour, Resource2Type, Resource2PerHour)
            if (values.Length < 6)
            {
                Debug.LogError($"[ParseJobBonusTable] Invalid data length. Expected at least 6, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.JobType = ParseIntSafe(values, 1);
            table.Resource1Type = ParseIntSafe(values, 2);
            table.Resource1PerHour = ParseFloatSafe(values, 3);
            table.Resource2Type = ParseIntSafe(values, 4);
            table.Resource2PerHour = ParseFloatSafe(values, 5);
        }

        private static void ParseZoneTable(ZoneTable table, string[] values)
        {
            // 전체 범위: A:M = 13개 컬럼
            // (Id=Zone, Name, Description(웹용), MainGroupCountMin/Max, MainGroupSizeMin/Max,
            //  SubGroupCountMin/Max, SubGroupSizeMin/Max, GroupRadius, InterGroupMinDistance)
            if (values.Length < 13)
            {
                Debug.LogError($"[ParseZoneTable] Invalid data length. Expected at least 13, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[1]=Name(웹용), values[2]=Description(웹용) — 코드는 읽지 않음
            table.MainGroupCountMin     = ParseIntSafe(values, 3);
            table.MainGroupCountMax     = ParseIntSafe(values, 4);
            table.MainGroupSizeMin      = ParseIntSafe(values, 5);
            table.MainGroupSizeMax      = ParseIntSafe(values, 6);
            table.SubGroupCountMin      = ParseIntSafe(values, 7);
            table.SubGroupCountMax      = ParseIntSafe(values, 8);
            table.SubGroupSizeMin       = ParseIntSafe(values, 9);
            table.SubGroupSizeMax       = ParseIntSafe(values, 10);
            table.GroupRadius           = ParseFloatSafe(values, 11);
            table.InterGroupMinDistance = ParseFloatSafe(values, 12);
        }

        private static void ParseDropTable(DropTable table, string[] values)
        {
            if (values.Length < 9)
            {
                Debug.LogError($"[ParseDropTable] Invalid data length. Expected at least 9, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);
            table.NothingRate = int.Parse(values[2]);
            table.CurrencyRate = int.Parse(values[3]);
            table.CurrencyId = int.Parse(values[4]);
            table.EquipmentRate = int.Parse(values[5]);
            table.EquipmentId = int.Parse(values[6]);
            table.CurrencyPoolMode = int.Parse(values[7]);
            table.EquipmentPoolMode = int.Parse(values[8]);

            // 스킬북 드랍 가중치 (SKILLBOOK_DESIGN.md §10) — 신규 컬럼, 빈 셀 허용 (= 0)
            table.SkillBookRate = values.Length > 9 && int.TryParse(values[9], out var sbr) ? sbr : 0;
            // 스킬 페이지 드랍 가중치 (SKILL_RUNE_DESIGN.md §8.1) — 신규 컬럼, 빈 셀 허용 (= 0)
            table.SkillPageRate = ParseIntSafe(values, 10);
        }

        private static void ParseDropCurrencyTable(DropCurrencyTable table, string[] values)
        {
            if (values.Length < 22)
            {
                Debug.LogError($"[ParseDropCurrencyTable] Invalid data length. Expected at least 22, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);

            table.DropList = new List<DropInfo>();

            // DropInfo 시작 인덱스
            int dropInfoStartIndex = 2;
            for (int i = 0; i < 10; i++)
            {
                int index = dropInfoStartIndex + (i * 2);
                int id = int.Parse(values[index]);
                if (id == 0)
                    continue;

                DropInfo dropInfo = new DropInfo();
                dropInfo.Id = id;
                dropInfo.Rate = int.Parse(values[index + 1]);
                table.DropList.Add(dropInfo);
            }
        }

        private static void ParseDropEquipmentTable(DropEquipmentTable table, string[] values)
        {
            if (values.Length < 22)
            {
                Debug.LogError($"[ParseDropEquipmentTable] Invalid data length. Expected at least 22, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);

            table.DropList = new List<DropInfo>();

            // DropInfo 시작 인덱스
            int dropInfoStartIndex = 2;
            for (int i = 0; i < 10; i++)
            {
                int index = dropInfoStartIndex + (i * 2);
                int id = int.Parse(values[index]);
                if (id == 0)
                    continue;

                DropInfo dropInfo = new DropInfo();
                dropInfo.Id = id;
                dropInfo.Rate = int.Parse(values[index + 1]);
                table.DropList.Add(dropInfo);
            }
        }

        private static void ParseSkillTable(SkillTable table, string[] values)
        {
            if (values.Length < 33)
            {
                Debug.LogError($"[ParseSkillTable] Invalid data length. Expected at least 33, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Desctiption = values[2];
            // 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.3) — Desctiption 다음 컬럼. ItemTable.Tier 체계 공유
            table.Tier = values.Length > 3 && int.TryParse(values[3], out var tier) ? tier : 0;
            table.Tags = ParseSkillTags(values[4]);
            table.SkillType = (GlobalEnum.SkillType)Enum.Parse(typeof(GlobalEnum.SkillType), values[5]);
            table.SubType = (GlobalEnum.SkillSubType)Enum.Parse(typeof(GlobalEnum.SkillSubType), values[6]);
            table.SkillRangeMin = float.Parse(values[7]);
            table.SkillRangeMax = float.Parse(values[8]);
            table.Cooltime = float.Parse(values[9]);
            table.Mana = int.Parse(values[10]);
            table.StartTime = float.Parse(values[11]);
            table.ProcessTime = float.Parse(values[12]);
            table.EndTime = float.Parse(values[13]);
            table.DamageTime = float.Parse(values[14]);
            table.HitCount = int.Parse(values[15]);
            table.HitInterval = float.Parse(values[16]);
            table.DamageType = (GlobalEnum.DamageType)Enum.Parse(typeof(GlobalEnum.DamageType), values[17]);
            table.DamageMin = int.Parse(values[18]);
            table.DamageMax = int.Parse(values[19]);
            table.SkillTargetType = (GlobalEnum.SkillTargetType)Enum.Parse(typeof(GlobalEnum.SkillTargetType), values[20]);
            table.SkillTargetRange1 = float.Parse(values[21]);
            table.SkillTargetRange2 = float.Parse(values[22]);
            table.AnimationName = values[23];
            table.StartEffectName = values[24];
            table.ActivateName = values[25];
            table.HitEffect = values[26];
            table.ProjectileId = int.Parse(values[27]);
            table.AreaEffectId = ParseIntSafe(values, 28);   // 장판 테이블 ID (0=없음)
            table.ArcHeight = float.Parse(values[29]);
            table.BaseCriRate = int.Parse(values[30]);
            table.BaseDamageMul = int.Parse(values[31]);
            table.BaseAttackSpeedMul = int.Parse(values[32]);

            // Phase 1·2: SkillEffect 합성 + ExecutionType 컬럼화 (빈 셀 허용)
            table.SkillEffectIds     = ParseIntCsv(values.Length > 33 ? values[33] : "");
            table.ExecutionType      = values.Length > 34 && string.IsNullOrWhiteSpace(values[34]) == false
                                       ? (SkillExecutionType)Enum.Parse(typeof(SkillExecutionType), values[34])
                                       : SkillExecutionType.MultiHit;
            table.ChannelingInterval = values.Length > 35 && float.TryParse(values[35], out var ci) ? ci : 0f;
            table.MaxChargeTime      = values.Length > 36 && float.TryParse(values[36], out var mct) ? mct : 0f;
            table.MinChargeRatio     = values.Length > 37 && float.TryParse(values[37], out var mcr) ? mcr : 0f;
            // Phase 3: BaseProjectileCount (빈 셀이면 1로 기본값. 발사 0개 방지)
            table.BaseProjectileCount = values.Length > 38 && int.TryParse(values[38], out var bpc) ? Mathf.Max(1, bpc) : 1;
        }

        /// <summary>
        /// Phase 1: SkillEffectTable 파서. 기본 9개 컬럼 + 스킬 페이지 메타데이터(PageCost, Condition, ConditionParam)
        /// </summary>
        private static void ParseSkillEffectTable(SkillEffectTable table, string[] values)
        {
            if (values.Length < 6)
            {
                Debug.LogError($"[ParseSkillEffectTable] Invalid data length. Expected at least 6, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Description = values[2];
            table.EffectType = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[3]);
            // Kind 컬럼(E, EffectType 바로 뒤). 빈 셀이면 EffectType으로 추론(구 IsEffectAction 화이트리스트와 동일) → 시트 마이그레이션 종료 후 이 fallback 제거 가능.
            table.Kind = ParseEnumSafe(values, 4, InferSkillEffectKindFromEffectType(table.EffectType));
            table.Trigger = (GlobalEnum.SkillTrigger)Enum.Parse(typeof(GlobalEnum.SkillTrigger), values[5]);
            table.Param1 = values.Length > 6 && float.TryParse(values[6], out var p1) ? p1 : 0f;
            table.Param2 = values.Length > 7 && float.TryParse(values[7], out var p2) ? p2 : 0f;
            table.Param3 = values.Length > 8 && float.TryParse(values[8], out var p3) ? p3 : 0f;
            table.Probability = values.Length > 9 && int.TryParse(values[9], out var prob) ? prob : 100;
            table.PageCost = ParseIntSafe(values, 10);
            table.Condition = ParseEnumSafe(values, 11, GlobalEnum.PageCondition.None);
            table.ConditionParam = ParseFloatSafe(values, 12);
        }

        /// <summary>
        /// SkillEffect 시트의 Kind 컬럼이 비어있을 때 구 동작을 보존하기 위한 fallback.
        /// 시트에 Kind 컬럼을 모든 행에 채워 넣은 뒤에는 이 메서드 제거 가능.
        /// </summary>
        private static GlobalEnum.SkillEffectKind InferSkillEffectKindFromEffectType(GlobalEnum.Stat effectType)
        {
            switch (effectType)
            {
                case GlobalEnum.Stat.ApplyBuff:
                case GlobalEnum.Stat.DelegateToTotem:
                    return GlobalEnum.SkillEffectKind.EffectAction;
                default:
                    return GlobalEnum.SkillEffectKind.StatBonus;
            }
        }

        /// <summary>
        /// SKILL_RUNE_DESIGN P-1: SkillBookTable 파서. 컬럼: Id(=Tier), PageCapacity, PageSlots
        /// </summary>
        private static void ParseSkillBookTable(SkillBookTable table, string[] values)
        {
            if (values.Length < 3)
            {
                Debug.LogError($"[ParseSkillBookTable] Invalid data length. Expected at least 3, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.PageCapacity = ParseIntSafe(values, 1);
            table.PageSlots = ParseIntSafe(values, 2);
        }

        /// <summary>
        /// CSV 형식 정수 리스트 파싱. 빈 문자열이면 null 반환.
        /// 예: "1001,1002" → [1001, 1002], "" → null
        /// </summary>
        private static List<int> ParseIntCsv(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var list = new List<int>();
            var tokens = raw.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i].Trim(), out var v))
                    list.Add(v);
            }
            return list.Count > 0 ? list : null;
        }

        private static GlobalEnum.SkillTag ParseSkillTags(string tagsRaw)
        {
            if (string.IsNullOrEmpty(tagsRaw))
                return GlobalEnum.SkillTag.None;

            GlobalEnum.SkillTag result = GlobalEnum.SkillTag.None;
            string[] tags = tagsRaw.Split(',');
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i].Trim();
                if (System.Enum.TryParse<GlobalEnum.SkillTag>(tag, true, out var parsed))
                {
                    result |= parsed;
                }
            }
            return result;
        }

        private static void ParseAiTable(AiTable table, string[] values)
        {
            if (values.Length < 12)
            {
                Debug.LogError($"[ParseAiTable] Invalid data length. Expected at least 12, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.AiType = (GlobalEnum.AiType)Enum.Parse(typeof(GlobalEnum.AiType), values[2]);
            table.BehaviorType = (AIBehaviorType)Enum.Parse(typeof(AIBehaviorType), values[3]);
            table.DetectionRange = float.Parse(values[4]);
            table.KeepDistance = ParseFloatSafe(values, 5);
            table.SkillId1 = int.Parse(values[6]);
            table.SkillWeight1 = int.Parse(values[7]);
            table.SkillId2 = int.Parse(values[8]);
            table.SkillWeight2 = int.Parse(values[9]);
            table.SkillId3 = int.Parse(values[10]);
            table.SkillWeight3 = int.Parse(values[11]);
        }

        private static void ParseBuffTable(BuffTable table, string[] values)
        {
            // 전체 범위: A:J = 10개 컬럼
            if (values.Length < 10)
            {
                Debug.LogError($"[ParseBuffTable] Invalid data length. Expected at least 10, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Description = values[2];
            table.BuffType = (GlobalEnum.BuffType)Enum.Parse(typeof(GlobalEnum.BuffType), values[3]);
            table.Duration = float.Parse(values[4]);
            table.TickInterval = float.Parse(values[5]);
            table.MaxStack = int.Parse(values[6]);
            table.IsDispellable = values[7].Trim().ToUpper() == "TRUE";
            table.EffectType = (GlobalEnum.BuffEffectType)Enum.Parse(typeof(GlobalEnum.BuffEffectType), values[8]);
            table.EffectValue = int.Parse(values[9]);
        }

        private static void ParseAnimationTable(AnimationTable table, string[] values)
        {
            // 전체 범위: A:G = 7개 컬럼 (Id, Name, SpriteLibraryPath, IdleFrame, MoveFrame, AttackFrame, DeadFrame)
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseAnimationTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.SpriteLibraryPath = values[2];
            table.IdleFrame = float.Parse(values[3]);
            table.MoveFrame = float.Parse(values[4]);
            table.AttackFrame = float.Parse(values[5]);
            table.DeadFrame = float.Parse(values[6]);
        }

        private static void ParseProjectileTable(ProjectileTable table, string[] values)
        {
            // 전체 범위: A:G = 7개 컬럼 (Id, Name, Speed, LifeTime, HitRadius, IsPiercing, PrefabKey)
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseProjectileTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Speed = float.Parse(values[2]);
            table.LifeTime = float.Parse(values[3]);
            table.HitRadius = float.Parse(values[4]);
            table.IsPiercing = values[5].Trim().ToUpper() == "TRUE";
            table.PrefabKey = values[6];
        }

        private static void ParseAreaEffectTable(AreaEffectTable table, string[] values)
        {
            // 전체 범위: A:M = 13개 컬럼
            // (Id, Name, Description, DamageType, Damage, Radius, Duration, TickInterval,
            //  OnTickBuffId, OnEnterBuffId, TargetFaction, TickEffectName, PrefabKey)
            if (values.Length < 12)
            {
                Debug.LogError($"[ParseAreaEffectTable] Invalid data length. Expected at least 12, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Description = values[2];
            table.DamageType = (GlobalEnum.DamageType)Enum.Parse(typeof(GlobalEnum.DamageType), values[3]);
            table.Damage = ParseIntSafe(values, 4);
            table.Radius = ParseFloatSafe(values, 5);
            table.Duration = ParseFloatSafe(values, 6);
            table.TickInterval = ParseFloatSafe(values, 7);
            table.OnTickBuffId = ParseIntSafe(values, 8);
            table.OnEnterBuffId = ParseIntSafe(values, 9);
            table.TargetFaction = ParseEnumSafe(values, 10, Faction.Neutral);
            table.TickEffectName = values[11];
            table.PrefabKey = values.Length > 12 ? values[12] : string.Empty;
        }

        private static void ParseModTable(ModTable table, string[] values)
        {
            // 컬럼: Id, Name, EffectType, ApplyType, Slot, Group, Element, Tags, TargetStat, AllowedEquipTypes
            if (values.Length < 10)
            {
                Debug.LogError($"[ParseModTable] Invalid data length. Expected at least 10, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.EffectType = (GlobalEnum.ModEffectType)Enum.Parse(typeof(GlobalEnum.ModEffectType), values[2]);
            table.ApplyType = (GlobalEnum.ModApplyType)Enum.Parse(typeof(GlobalEnum.ModApplyType), values[3]);
            table.Slot = (GlobalEnum.ModSlot)Enum.Parse(typeof(GlobalEnum.ModSlot), values[4]);
            table.Group = values[5];
            table.Element = string.IsNullOrEmpty(values[6]) == false
                ? (GlobalEnum.DamageType)Enum.Parse(typeof(GlobalEnum.DamageType), values[6])
                : GlobalEnum.DamageType.Physics;
            table.Tags = ParseSkillTags(values[7]);
            table.TargetStat = ParseStatStrict(values, 8, table.Id, table.EffectType);
            table.AllowedEquipTypes = ParseEquipMaskStrict(values, 9, table.Id, table.Slot);
        }

        /// <summary>
        /// GlobalEnum.Stat 파싱: 빈 문자열은 default, 숫자 문자열은 거부(시트는 enum 이름으로 입력).
        /// FlatStat/IncreasedStat은 TargetStat 필수 — 빈 칸이면 LogError.
        /// 그 외 EffectType은 TargetStat 미사용이므로 빈 칸 허용.
        /// </summary>
        private static GlobalEnum.Stat ParseStatStrict(string[] values, int index, int tableId, GlobalEnum.ModEffectType effectType)
        {
            bool requiresTargetStat = (effectType == GlobalEnum.ModEffectType.FlatStat
                                    || effectType == GlobalEnum.ModEffectType.IncreasedStat);

            if (values.Length <= index || string.IsNullOrEmpty(values[index]))
            {
                if (requiresTargetStat)
                    Debug.LogError($"[ParseModTable] TargetStat required for {effectType} (Mod Id={tableId})");
                return default;
            }

            string raw = values[index].Trim();
            if (int.TryParse(raw, out _))
            {
                Debug.LogError($"[ParseModTable] TargetStat must be enum name, not integer. Got: '{raw}' (Mod Id={tableId})");
                return default;
            }

            if (Enum.TryParse(typeof(GlobalEnum.Stat), raw, true, out object parsed))
                return (GlobalEnum.Stat)parsed;

            Debug.LogError($"[ParseModTable] Unknown TargetStat name: '{raw}' (Mod Id={tableId})");
            return default;
        }

        /// <summary>
        /// Parses ModTable.AllowedEquipTypes. Pipe-separated values are accepted.
        /// </summary>
        private static GlobalEnum.EquipmentTypeMask ParseEquipMaskStrict(string[] values, int index, int tableId, GlobalEnum.ModSlot slot)
        {
            bool isImplicit = (slot == GlobalEnum.ModSlot.Implicit);

            if (values.Length <= index || string.IsNullOrEmpty(values[index]))
            {
                if (isImplicit == false)
                    Debug.LogError($"[ParseModTable] AllowedEquipTypes must be set for non-Implicit mod (Mod Id={tableId})");
                return GlobalEnum.EquipmentTypeMask.None;
            }

            string raw = values[index].Trim();
            if (int.TryParse(raw, out _))
            {
                Debug.LogError($"[ParseModTable] AllowedEquipTypes must be enum name, not integer. Got: '{raw}' (Mod Id={tableId})");
                return GlobalEnum.EquipmentTypeMask.None;
            }

            string enumRaw = raw.Replace("|", ",");
            if (Enum.TryParse(typeof(GlobalEnum.EquipmentTypeMask), enumRaw, true, out object parsed))
            {
                GlobalEnum.EquipmentTypeMask mask = (GlobalEnum.EquipmentTypeMask)parsed;
                if (Utils.IsValidEquipmentTypeMask(mask, isImplicit))
                    return mask;

                Debug.LogError($"[ParseModTable] Invalid AllowedEquipTypes mask: '{raw}' (Mod Id={tableId})");
                return GlobalEnum.EquipmentTypeMask.None;
            }

            Debug.LogError($"[ParseModTable] Unknown AllowedEquipTypes value: '{raw}' (Mod Id={tableId})");
            return GlobalEnum.EquipmentTypeMask.None;
        }

        private static void ParseModTierTable(ModTierTable table, string[] values)
        {
            // 컬럼: Id, ModName(설명용-스킵), ModId, Tier, Min1, Max1, Min2, Max2, RequiredLevel, Weight
            if (values.Length < 10)
            {
                Debug.LogError($"[ParseModTierTable] Invalid data length. Expected at least 10, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[1]은 ModName (시트 열람용 설명 컬럼, 파싱 스킵)
            table.ModId = int.Parse(values[2]);
            table.Tier = int.Parse(values[3]);
            table.Min1 = int.Parse(values[4]);
            table.Max1 = int.Parse(values[5]);
            table.Min2 = int.Parse(values[6]);
            table.Max2 = int.Parse(values[7]);
            table.RequiredLevel = int.Parse(values[8]);
            table.Weight = int.Parse(values[9]);
        }

        private static void ParseItemImplicitTable(ItemImplicitTable table, string[] values)
        {
            // 컬럼: Id, ItemId, ModId, Tier
            if (values.Length < 4)
            {
                Debug.LogError($"[ParseItemImplicitTable] Invalid data length. Expected at least 4, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.ItemId = int.Parse(values[1]);
            table.ModId = int.Parse(values[2]);
            table.Tier = int.Parse(values[3]);
        }

        private static void ParseEquipmentBaseStatTable(EquipmentBaseStatTable table, string[] values)
        {
            // 컬럼: Id, ItemId, (Base1ModId, Base1Value1, Base1Value2),
            //                  (Base2ModId, Base2Value1, Base2Value2),
            //                  (Base3ModId, Base3Value1, Base3Value2),
            //                  (Base4ModId, Base4Value1, Base4Value2)
            // 슬롯당 3컬럼 × 4 슬롯 = 12 + Id + ItemId = 14컬럼
            if (values.Length < 14)
            {
                Debug.LogError($"[ParseEquipmentBaseStatTable] Invalid data length. Expected at least 14, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.ItemId = int.Parse(values[1]);

            table.Base1ModId  = int.Parse(values[2]);
            table.Base1Value1 = int.Parse(values[3]);
            table.Base1Value2 = int.Parse(values[4]);

            table.Base2ModId  = int.Parse(values[5]);
            table.Base2Value1 = int.Parse(values[6]);
            table.Base2Value2 = int.Parse(values[7]);

            table.Base3ModId  = int.Parse(values[8]);
            table.Base3Value1 = int.Parse(values[9]);
            table.Base3Value2 = int.Parse(values[10]);

            table.Base4ModId  = int.Parse(values[11]);
            table.Base4Value1 = int.Parse(values[12]);
            table.Base4Value2 = int.Parse(values[13]);
        }

        private static void ParseBuffEffectTable(BuffEffectTable table, string[] values)
        {
            // 전체 범위: A:K = 11개 컬럼 (Id, Name, 웹용, 4개 효과 * 2컬럼)
            if (values.Length < 11)
            {
                Debug.LogError($"[ParseBuffEffectTable] Invalid data length. Expected at least 11, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];

            // values[2]는 웹에서만 사용

            // BuffEffectList 파싱 (values[3]부터 시작, 최대 4개 효과 * 2 컬럼 = 8개 컬럼)
            // values[3-4]: BuffEffect 1 (Type, Value)
            // values[5-6]: BuffEffect 2 (Type, Value)
            // values[7-8]: BuffEffect 3 (Type, Value)
            // values[9-10]: BuffEffect 4 (Type, Value)
            int buffEffectStartIndex = 3;
            for (int i = 0; i < 4; i++)
            {
                int index = buffEffectStartIndex + (i * 2);

                // 배열 범위 체크
                if (values.Length <= index + 1)
                    break;

                // 버프 효과 타입이 비어있으면 스킵
                if (string.IsNullOrEmpty(values[index]))
                    continue;

                // 첫 번째 효과를 발견했을 때만 리스트 생성
                table.BuffEffectList ??= new();

                BuffEffect buffEffect = new()
                {
                    Type = (GlobalEnum.BuffEffectType)Enum.Parse(typeof(GlobalEnum.BuffEffectType), values[index]),
                    Value = ushort.Parse(values[index + 1])
                };

                table.BuffEffectList.Add(buffEffect);
            }
        }

        private static async Task<string> DownloadTableData(string inURL)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(inURL))
            {
                var ao = req.SendWebRequest();

                while (!ao.isDone)
                {
                    await Task.Yield();
                }

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.LogError("Error: " + req.error);
                    return null;
                }

                return req.downloadHandler.text;
            }
        }

        private static bool SaveTable(string inName, object inData, SaveType inSaveType)
        {
            string fileName = $"{inName}.bytes";

            string result = JsonConvert.SerializeObject(inData, ARPG.Data.JsonSettings.Default);
            
            string enData = string.Empty, filePath = string.Empty;
            if (inSaveType == SaveType.String)
            {
                enData = result;
                filePath = Path.Combine(Application.dataPath, Data.DataManager.TablePath, fileName);
            }
            else
            {
                enData = Encrypt(result);
                filePath = Path.Combine(Application.dataPath, Data.DataManager.TablePath, fileName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.Write(enData);
            }

            return true;
        }

        public static string Encrypt(string data)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
            RijndaelManaged rm = CreateRijndaelManaged();
            ICryptoTransform ct = rm.CreateEncryptor();
            byte[] results = ct.TransformFinalBlock(bytes, 0, bytes.Length);

            return System.Convert.ToBase64String(results, 0, results.Length);
        }

        public static string Decrypt(string data)
        {
            byte[] bytes = System.Convert.FromBase64String(data);
            RijndaelManaged rm = CreateRijndaelManaged();
            ICryptoTransform ct = rm.CreateDecryptor();
            byte[] resultArray = ct.TransformFinalBlock(bytes, 0, bytes.Length);

            return System.Text.Encoding.UTF8.GetString(resultArray);
        }

        private static readonly string _privateKey = "1718hy9dsf0jsdefjzs0pa9ids78ehgf81h32re";
        private static RijndaelManaged CreateRijndaelManaged()
        {
            byte[] keyArray = System.Text.Encoding.UTF8.GetBytes(_privateKey);
            RijndaelManaged result = new RijndaelManaged();

            byte[] newKeysArray = new byte[16];
            System.Array.Copy(keyArray, 0, newKeysArray, 0, 16);

            result.Key = newKeysArray;
            result.Mode = CipherMode.ECB;
            result.Padding = PaddingMode.PKCS7;
            return result;
        }
    }
}
