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
using ARPG.Item;
using ARPG.Npc;
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

    public partial class DownloadTables
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
            // M열 PatrolRadius (NAMED_THREAT_DESIGN §5.5) — 빈칸 = 0 = 순찰 안 함
            Register<AiTable>(list, "947794841&range=A:M");
            // R·S열 AccuracyMul·EvasionMul (PLAYER_LEVEL_CHARM_DESIGN §4.4) — 빈칸 = 1.0
            Register<MonsterTable>(list, "483012127&range=A:S");
            Register<NpcTable>(list, "1460299278&range=A:P");
            // NpcEquipTable — NPC 시작 장비 세트. Desc + 슬롯별 ItemId/Chance 쌍.
            Register<NpcEquipTable>(list, "1566085089&range=A:Z");
            // N열 RarityPerAffix (ITEM_DROP_BALANCE_DESIGN §8.4), O열 ExpMul (PLAYER_LEVEL_CHARM_DESIGN §3.2)
            Register<MonsterArchetypeTable>(list, "1141585255&range=A:O");
            // 몬스터 접사 (MONSTER_AFFIX_DESIGN.md §5). A~S 데이터, T열 Note는 시트 가독성용(다운로드 제외)
            Register<MonsterAffixTable>(list, "1703620452&range=A:S");
            // 네임드 위협 템플릿 (NAMED_THREAT_DESIGN §11.1). A~S 데이터, T열 Note는 시트 가독성용(다운로드 제외)
            Register<NamedThreatTable>(list, "1768678022&range=A:S");
            // NPC 단기 목표 정의 (NPC_OBJECTIVE_DESIGN §4.1, D147). 33열 A~AG — 새 탭은 33열 격자로 만들었다.
            Register<NpcObjectiveTable>(list, "182851152&range=A:AG");
            Register<StatTable>(list, "318209064&range=A:AJ");
            Register<ItemTable>(list, "2064107837&range=A:S");
            Register<BuildableItemTable>(list, "534887250&range=A:AA");
            // Phase D: JobBonusTable (직업별 시간당 가산 자원)
            Register<JobBonusTable>(list, "470575350&range=A:F");
            // L·M열 CharmRate·CharmBoardRate (PLAYER_LEVEL_CHARM_DESIGN §6.6)
            Register<DropTable>(list, "1241586373&range=A:M");
            Register<DropCurrencyTable>(list, "2071520432&range=A:V");
            Register<DropEquipmentTable>(list, "1267382287&range=A:V");
            // 드롭 구간표 (ITEM_DROP_BALANCE_DESIGN §7.1). A~Q 데이터, R열 Note는 시트 가독성용(다운로드 제외)
            Register<DropBandTable>(list, "1276985023&range=A:Q");
            // 플레이어 레벨 경험치 표 (PLAYER_LEVEL_CHARM_DESIGN §3.4). A=Id(레벨), B=RequiredExp, C=MonsterExp
            Register<ExpTable>(list, "1501748408&range=A:C");
            Register<SkillTable>(list, "92727160&range=A:AS");
            // Phase 1: SkillEffect 합성 시스템용 테이블 (+ 스킬 페이지 Tier/비용 메타데이터 + Kind 분류 컬럼)
            Register<SkillEffectTable>(list, "1681865950&range=A:N");
            Register<SkillPropagationTable>(list, "155977417&range=A:M");
            Register<SkillBookTable>(list, "1726438368&range=A:C");
            // 장판(지속 영역 효과) 테이블
            Register<AreaEffectTable>(list, "1891935594&range=A:P");
            Register<BuffTable>(list, "127577579&range=A:T");
            Register<AnimationTable>(list, "747631090&range=A:G");
            Register<ProjectileTable>(list, "1810235418&range=A:G");
            Register<ModTable>(list, "1571193978&range=A:J");
            Register<ModTierTable>(list, "1782637736&range=A:J");
            // 참 옵션 값 (PLAYER_LEVEL_CHARM_DESIGN §6.3). A=Id(ModId×100+Tier), B=ModName(가독성용), C=ModId, D=Tier,
            // E~L = 1×1·1×2·3칸(1×3·L자)·2×2의 최소·최대 정수. 빈칸 = 그 모양에 붙지 않음
            Register<CharmModTierTable>(list, "1876501976&range=A:L");
            Register<ItemImplicitTable>(list, "547967325&range=A:D");
            // BasicItem — 상점용 기본 아이템 (NPC_SHOP_EQUIPMENT_DESIGN §3.1). A~F 데이터, G~H는 보조 컬럼(다운로드 제외).
            Register<BasicItemTable>(list, "786446863&range=A:F");
            // EquipmentBaseStat: A~N가 실제 데이터, O~S는 시트 가독성용 보조 컬럼(다운로드 제외).
            // 슬롯당 3컬럼(ModId, Value1, Value2) × 4 슬롯 = 12컬럼 + Id + ItemId = 14컬럼.
            Register<EquipmentBaseStatTable>(list, "972309111&range=A:N");
            // Q~U열 던전 드롭 (ITEM_DROP_BALANCE_DESIGN §7.6): 레벨 보정 2칸, 상자 판정, 장소 수량·희귀도
            Register<DungeonTable>(list, "1672490546&range=A:U");
            Register<VillageTable>(list, "441028134&range=A:F");
            Register<VillageStageTable>(list, "467145019&range=A:Q");
            // 청크 Zone(시드 청크 (0,0) 기준 Chebyshev 거리+1)별 몬스터 스폰 파라미터
            Register<ZoneTable>(list, "848445893&range=A:O");
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

            var narrative = await DownloadNarrativePackAsync(GetTableList<DungeonTable>(), GetTableList<MonsterTable>(), GetTableList<ItemTable>());
            if (narrative == null)
            {
                Debug.LogError("[DownloadTables] NPC 대화 콘텐츠 다운로드 또는 검증 실패로 전체 테이블 저장을 중단합니다.");
                return;
            }

            if (ValidateModSetup() == false)
            {
                Debug.LogError("[DownloadTables] Mod/ModTier 검증 실패로 전체 테이블 저장을 중단합니다.");
                return;
            }

            if (ValidateDropBandSetup() == false)
            {
                Debug.LogError("[DownloadTables] DropBand 검증 실패로 전체 테이블 저장을 중단합니다.");
                return;
            }

            if (ValidateExpSetup() == false)
            {
                Debug.LogError("[DownloadTables] Exp 검증 실패로 전체 테이블 저장을 중단합니다.");
                return;
            }

            if (ValidateCharmModTierSetup() == false)
            {
                Debug.LogError("[DownloadTables] CharmModTier 검증 실패로 전체 테이블 저장을 중단합니다.");
                return;
            }

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
            ValidateForcedMoveSetup();
            ValidateNamedThreatSetup(narrative);

            if (SaveNarrativePack(narrative) == false) return;
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

        private static async Task DownloadModTablesTogether()
        {
            TableEntry modEntry = GetTableEntry<ModTable>();
            TableEntry tierEntry = GetTableEntry<ModTierTable>();
            if (modEntry == null || tierEntry == null)
            {
                Debug.LogError("[DownloadTables] ModTable 또는 ModTierTable 등록 정보를 찾을 수 없습니다.");
                return;
            }

            _tableDic = new();
            bool modDownloaded = await modEntry.DownloadAction();
            bool tierDownloaded = await tierEntry.DownloadAction();
            if (modDownloaded == false || tierDownloaded == false
                || ValidateModSetup() == false)
            {
                Debug.LogError("[DownloadTables] Mod/ModTier 다운로드 또는 검증 실패로 저장하지 않습니다.");
                return;
            }

            SaveTable(typeof(ModTable).Name, _tableDic[typeof(ModTable)], modEntry.SaveType);
            SaveTable(typeof(ModTierTable).Name, _tableDic[typeof(ModTierTable)],
                tierEntry.SaveType);

            AssetDatabase.Refresh();
            Debug.Log("download ModTable + ModTierTable Completed...");
        }

        private static TableEntry GetTableEntry<T>() where T : TableBase
        {
            for (int i = 0; i < _tableEntries.Count; i++)
            {
                if (_tableEntries[i].Type == typeof(T))
                    return _tableEntries[i];
            }

            return null;
        }

        // 알파벳 순으로 개별 테이블 다운로드 메뉴 (priority 100 → All과 분리되는 separator)
        [MenuItem("ARPG/Download Table/AiTable", false, 100)]
        private static async void DownloadAiTable() => await DownloadSingleTable<AiTable>();

        [MenuItem("ARPG/Download Table/AnimationTable", false, 100)]
        private static async void DownloadAnimationTable() => await DownloadSingleTable<AnimationTable>();

        [MenuItem("ARPG/Download Table/AreaEffectTable", false, 100)]
        private static async void DownloadAreaEffectTable() => await DownloadSingleTable<AreaEffectTable>();

        [MenuItem("ARPG/Download Table/BasicItemTable", false, 100)]
        private static async void DownloadBasicItemTable() => await DownloadSingleTable<BasicItemTable>();

        [MenuItem("ARPG/Download Table/BuffTable", false, 100)]
        private static async void DownloadBuffTable() => await DownloadSingleTable<BuffTable>();

        [MenuItem("ARPG/Download Table/BuildableItemTable", false, 100)]
        private static async void DownloadBuildableItemTable() => await DownloadSingleTable<BuildableItemTable>();

        [MenuItem("ARPG/Download Table/CreatureTable", false, 100)]
        private static async void DownloadCreatureTable() => await DownloadSingleTable<CreatureTable>();

        [MenuItem("ARPG/Download Table/DropBandTable", false, 100)]
        private static async void DownloadDropBandTable()
        {
            TableEntry entry = GetTableEntry<DropBandTable>();
            if (entry == null)
            {
                Debug.LogError("[DownloadTables] DropBandTable 등록 정보를 찾을 수 없습니다.");
                return;
            }

            _tableDic = new();
            if (await entry.DownloadAction() == false || ValidateDropBandSetup() == false)
            {
                Debug.LogError("[DownloadTables] DropBand 다운로드 또는 검증 실패로 저장하지 않습니다.");
                return;
            }

            SaveTable(typeof(DropBandTable).Name, _tableDic[typeof(DropBandTable)], entry.SaveType);
            AssetDatabase.Refresh();
            Debug.Log("download DropBandTable Completed...");
        }

        [MenuItem("ARPG/Download Table/DropCurrencyTable", false, 100)]
        private static async void DownloadDropCurrencyTable() => await DownloadSingleTable<DropCurrencyTable>();

        [MenuItem("ARPG/Download Table/DropEquipmentTable", false, 100)]
        private static async void DownloadDropEquipmentTable() => await DownloadSingleTable<DropEquipmentTable>();

        [MenuItem("ARPG/Download Table/CharmModTierTable", false, 100)]
        private static async void DownloadCharmModTierTable()
        {
            TableEntry entry = GetTableEntry<CharmModTierTable>();
            if (entry == null)
            {
                Debug.LogError("[DownloadTables] CharmModTierTable 등록 정보를 찾을 수 없습니다.");
                return;
            }

            _tableDic = new();
            if (await entry.DownloadAction() == false || ValidateCharmModTierSetup() == false)
            {
                Debug.LogError("[DownloadTables] CharmModTier 다운로드 또는 검증 실패로 저장하지 않습니다.");
                return;
            }

            SaveTable(typeof(CharmModTierTable).Name, _tableDic[typeof(CharmModTierTable)], entry.SaveType);
            AssetDatabase.Refresh();
            Debug.Log("download CharmModTierTable Completed...");
        }

        [MenuItem("ARPG/Download Table/DropTable", false, 100)]
        private static async void DownloadDropTable() => await DownloadSingleTable<DropTable>();

        [MenuItem("ARPG/Download Table/ExpTable", false, 100)]
        private static async void DownloadExpTable()
        {
            TableEntry entry = GetTableEntry<ExpTable>();
            if (entry == null)
            {
                Debug.LogError("[DownloadTables] ExpTable 등록 정보를 찾을 수 없습니다.");
                return;
            }

            _tableDic = new();
            if (await entry.DownloadAction() == false || ValidateExpSetup() == false)
            {
                Debug.LogError("[DownloadTables] Exp 다운로드 또는 검증 실패로 저장하지 않습니다.");
                return;
            }

            SaveTable(typeof(ExpTable).Name, _tableDic[typeof(ExpTable)], entry.SaveType);
            AssetDatabase.Refresh();
            Debug.Log("download ExpTable Completed...");
        }

        [MenuItem("ARPG/Download Table/DungeonTable", false, 100)]
        private static async void DownloadDungeonTable() => await DownloadSingleTable<DungeonTable>();

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
        private static async void DownloadModTable() => await DownloadModTablesTogether();

        [MenuItem("ARPG/Download Table/ModTierTable", false, 100)]
        private static async void DownloadModTierTable() => await DownloadModTablesTogether();

        [MenuItem("ARPG/Download Table/MonsterArchetypeTable", false, 100)]
        private static async void DownloadMonsterArchetypeTable() => await DownloadSingleTable<MonsterArchetypeTable>();

        [MenuItem("ARPG/Download Table/MonsterAffixTable", false, 100)]
        private static async void DownloadMonsterAffixTable() => await DownloadSingleTable<MonsterAffixTable>();

        [MenuItem("ARPG/Download Table/NamedThreatTable", false, 100)]
        private static async void DownloadNamedThreatTable() => await DownloadSingleTable<NamedThreatTable>();

        [MenuItem("ARPG/Download Table/MonsterTable", false, 100)]
        private static async void DownloadMonsterTable() => await DownloadSingleTable<MonsterTable>();

        [MenuItem("ARPG/Download Table/NpcTable", false, 100)]
        private static async void DownloadNpcTable() => await DownloadSingleTable<NpcTable>();

        [MenuItem("ARPG/Download Table/NpcEquipTable", false, 100)]
        private static async void DownloadNpcEquipTable() => await DownloadSingleTable<NpcEquipTable>();

        [MenuItem("ARPG/Download Table/NpcObjectiveTable", false, 100)]
        private static async void DownloadNpcObjectiveTable() => await DownloadSingleTable<NpcObjectiveTable>();

        [MenuItem("ARPG/Download Table/ProjectileTable", false, 100)]
        private static async void DownloadProjectileTable() => await DownloadSingleTable<ProjectileTable>();

        [MenuItem("ARPG/Download Table/SkillBookTable", false, 100)]
        private static async void DownloadSkillBookTable() => await DownloadSingleTable<SkillBookTable>();

        [MenuItem("ARPG/Download Table/SkillEffectTable", false, 100)]
        private static async void DownloadSkillEffectTable() => await DownloadSingleTable<SkillEffectTable>();

        [MenuItem("ARPG/Download Table/SkillPropagationTable", false, 100)]
        private static async void DownloadSkillPropagationTable() => await DownloadSingleTable<SkillPropagationTable>();

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
        /// <summary>
        /// AreaEffect.ForcedMoveEffectId가 Knockback/Pull SkillEffect 행을 가리키는지 확인 (SKILL_DESIGN §6.10).
        /// 두 표를 함께 받는 전체 다운로드에서만 대조한다. 단일 다운로드의 잘못된 Id는 실행 시 오류 로그 후 무시된다.
        /// </summary>
        private static void ValidateForcedMoveSetup()
        {
            List<AreaEffectTable> areaEffectTables = GetTableList<AreaEffectTable>();
            List<SkillEffectTable> skillEffectTables = GetTableList<SkillEffectTable>();
            if (areaEffectTables == null || skillEffectTables == null)
                return;

            for (int i = 0; i < areaEffectTables.Count; i++)
            {
                AreaEffectTable area = areaEffectTables[i];
                if (area.ForcedMoveEffectId <= 0)
                    continue;

                SkillEffectTable effect = null;
                for (int j = 0; j < skillEffectTables.Count; j++)
                {
                    if (skillEffectTables[j].Id == area.ForcedMoveEffectId)
                    {
                        effect = skillEffectTables[j];
                        break;
                    }
                }

                if (effect == null)
                    Debug.LogError($"[ValidateForcedMove] AreaEffect Id={area.Id}({area.Name}): ForcedMoveEffectId={area.ForcedMoveEffectId} 행이 SkillEffect에 없습니다.");
                else if (effect.EffectType != GlobalEnum.Stat.Knockback && effect.EffectType != GlobalEnum.Stat.Pull)
                    Debug.LogError($"[ValidateForcedMove] AreaEffect Id={area.Id}({area.Name}): ForcedMoveEffectId={area.ForcedMoveEffectId}는 Knockback/Pull이어야 합니다. (EffectType={effect.EffectType})");
            }
        }

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

        private static bool ValidateModSetup()
        {
            List<ModTable> mods = GetTableList<ModTable>();
            List<ModTierTable> tiers = GetTableList<ModTierTable>();
            if (mods == null || tiers == null)
            {
                Debug.LogError("[ValidateMod] ModTable과 ModTierTable을 함께 다운로드해야 합니다.");
                return false;
            }

            int errors = 0;
            HashSet<int> modIds = new HashSet<int>();
            for (int i = 0; i < mods.Count; i++)
            {
                ModTable mod = mods[i];
                if (modIds.Add(mod.Id) == false)
                {
                    Debug.LogError($"[ValidateMod] ModTable 중복 Id={mod.Id}");
                    errors++;
                }

                bool isAffix = mod.Slot == GlobalEnum.ModSlot.Prefix
                    || mod.Slot == GlobalEnum.ModSlot.Postfix;
                if (isAffix == true && string.IsNullOrWhiteSpace(mod.Group) == true)
                {
                    Debug.LogError($"[ValidateMod] Mod Id={mod.Id}의 Group이 비어 있습니다.");
                    errors++;
                }

                // ModSlot.Charm은 옛 세이브 읽기용이다. 참 옵션도 장비처럼 Prefix/Postfix다 (PLAYER_LEVEL_CHARM_DESIGN §6.3)
                if (mod.Slot == GlobalEnum.ModSlot.Charm)
                {
                    Debug.LogError($"[ValidateMod] Mod Id={mod.Id}의 Slot이 Charm입니다. 참 옵션은 Prefix/Postfix로 적습니다.");
                    errors++;
                }

                // 참 전용 옵션은 AllowedEquipTypes를 비워 장비 롤·마법부여 후보에서 빠진다. 장비 옵션은 반드시 채운다
                bool isCharmOnly = CharmRules.IsCharmOnlyMod(mod.Id);
                if (isAffix == true && isCharmOnly == false
                    && mod.AllowedEquipTypes == GlobalEnum.EquipmentTypeMask.None)
                {
                    Debug.LogError($"[ValidateMod] Mod Id={mod.Id}의 AllowedEquipTypes가 비어 있습니다.");
                    errors++;
                }
                if (isCharmOnly == true && mod.AllowedEquipTypes != GlobalEnum.EquipmentTypeMask.None)
                {
                    Debug.LogError($"[ValidateMod] 참 전용 Mod Id={mod.Id}의 AllowedEquipTypes가 채워져 있어 장비에도 붙습니다. 비워 두세요.");
                    errors++;
                }
            }

            // 참 옵션 풀(CharmRules)의 Mod는 모두 있고 Prefix/Postfix여야 한다 — 접두·접미 구분을 이 칸에서 읽는다
            Dictionary<int, ModTable> modById = new Dictionary<int, ModTable>();
            for (int i = 0; i < mods.Count; i++)
                modById[mods[i].Id] = mods[i];
            for (int i = 0; i < CharmRules.OptionModIds.Count; i++)
            {
                int charmModId = CharmRules.OptionModIds[i];
                if (modById.TryGetValue(charmModId, out ModTable charmMod) == false)
                {
                    Debug.LogError($"[ValidateMod] 참 옵션 Mod Id={charmModId}가 ModTable에 없습니다.");
                    errors++;
                }
                else if (charmMod.Slot != GlobalEnum.ModSlot.Prefix && charmMod.Slot != GlobalEnum.ModSlot.Postfix)
                {
                    Debug.LogError($"[ValidateMod] 참 옵션 Mod Id={charmModId}의 Slot({charmMod.Slot})이 Prefix/Postfix가 아닙니다.");
                    errors++;
                }
            }

            HashSet<int> tierIds = new HashSet<int>();
            HashSet<long> modTierPairs = new HashSet<long>();
            HashSet<int> modsWithTier = new HashSet<int>();
            for (int i = 0; i < tiers.Count; i++)
            {
                ModTierTable tier = tiers[i];
                if (tierIds.Add(tier.Id) == false)
                {
                    Debug.LogError($"[ValidateMod] ModTierTable 중복 Id={tier.Id}");
                    errors++;
                }

                if (modIds.Contains(tier.ModId) == false)
                {
                    Debug.LogError($"[ValidateMod] ModTier Id={tier.Id}가 없는 Mod Id={tier.ModId}를 참조합니다.");
                    errors++;
                }
                else
                {
                    modsWithTier.Add(tier.ModId);
                }

                long pairKey = ((long)tier.ModId << 32) | (uint)tier.Tier;
                if (modTierPairs.Add(pairKey) == false)
                {
                    Debug.LogError($"[ValidateMod] Mod Id={tier.ModId}, Tier={tier.Tier} 조합이 중복됩니다.");
                    errors++;
                }

                if (tier.Tier <= 0 || tier.Min1 > tier.Max1 || tier.Min2 > tier.Max2
                    || tier.RequiredLevel < 0 || tier.Weight <= 0)
                {
                    Debug.LogError($"[ValidateMod] ModTier Id={tier.Id}의 Tier/범위/레벨/가중치가 올바르지 않습니다.");
                    errors++;
                }

                if (tier.Tier > ItemLevelModTierRules.TOP_SPECIAL_TIER)
                {
                    Debug.LogError($"[ValidateMod] ModTier Id={tier.Id}의 Tier={tier.Tier}는 최고 티어 " +
                        $"T{ItemLevelModTierRules.TOP_SPECIAL_TIER}를 넘습니다.");
                    errors++;
                }
            }

            // 아이템 레벨 티어 규칙(ItemLevelModTierRules)은 Mod마다 T1부터 빈칸 없이 이어지고,
            // 티어가 올라갈수록 RequiredLevel(티어가 열리는 아이템 레벨)이 줄지 않는다고 가정한다.
            Dictionary<int, List<ModTierTable>> tiersByMod = new Dictionary<int, List<ModTierTable>>();
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiersByMod.TryGetValue(tiers[i].ModId, out List<ModTierTable> list) == false)
                {
                    list = new List<ModTierTable>();
                    tiersByMod.Add(tiers[i].ModId, list);
                }
                list.Add(tiers[i]);
            }
            foreach (KeyValuePair<int, List<ModTierTable>> pair in tiersByMod)
            {
                List<ModTierTable> modTiers = pair.Value;
                modTiers.Sort((a, b) => a.Tier.CompareTo(b.Tier));
                for (int i = 0; i < modTiers.Count; i++)
                {
                    if (modTiers[i].Tier != i + 1)
                    {
                        Debug.LogError($"[ValidateMod] Mod Id={pair.Key}의 Tier가 1부터 이어지지 않습니다 " +
                            $"(순서 {i + 1}번째가 T{modTiers[i].Tier}).");
                        errors++;
                        break;
                    }
                    if (i > 0 && modTiers[i].RequiredLevel < modTiers[i - 1].RequiredLevel)
                    {
                        Debug.LogError($"[ValidateMod] Mod Id={pair.Key}의 T{modTiers[i].Tier} RequiredLevel" +
                            $"({modTiers[i].RequiredLevel})이 아래 티어보다 낮습니다.");
                        errors++;
                        break;
                    }
                }
            }

            for (int i = 0; i < mods.Count; i++)
            {
                ModTable mod = mods[i];
                bool needsTiers = mod.Slot == GlobalEnum.ModSlot.Prefix
                    || mod.Slot == GlobalEnum.ModSlot.Postfix;
                if (needsTiers == true && modsWithTier.Contains(mod.Id) == false)
                {
                    Debug.LogError($"[ValidateMod] Prefix/Postfix Mod Id={mod.Id}에 Tier 행이 없습니다.");
                    errors++;
                }
            }

            if (errors == 0)
                Debug.Log($"[ValidateMod] PASS — Mods={mods.Count}, Tiers={tiers.Count}");

            return errors == 0;
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

            // 체크박스/서식이 있는 빈 행도 TSV에 포함될 수 있으므로 ID 없는 행은 건너뛴다.
            if (values.Length < 1 || string.IsNullOrWhiteSpace(values[0]) == true)
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
                else if (table is MonsterAffixTable monsterAffixTable)
                {
                    ParseMonsterAffixTable(monsterAffixTable, values);
                }
                else if (table is NamedThreatTable namedThreatTable)
                {
                    ParseNamedThreatTable(namedThreatTable, values);
                }
                else if (table is NpcObjectiveTable npcObjectiveTable)
                {
                    ParseNpcObjectiveTable(npcObjectiveTable, values);
                }
                else if (table is NpcTable npcTable)
                {
                    ParseNpcTable(npcTable, values);
                }
                else if (table is NpcEquipTable npcEquipTable)
                {
                    ParseNpcEquipTable(npcEquipTable, values);
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
                else if (table is DropBandTable dropBandTable)
                {
                    ParseDropBandTable(dropBandTable, values);
                }
                else if (table is CharmModTierTable charmModTierTable)
                {
                    ParseCharmModTierTable(charmModTierTable, values);
                }
                else if (table is ExpTable expTable)
                {
                    ParseExpTable(expTable, values);
                }
                else if (table is SkillTable skillTable)
                {
                    ParseSkillTable(skillTable, values);
                }
                else if (table is SkillEffectTable skillEffectTable)
                {
                    ParseSkillEffectTable(skillEffectTable, values);
                }
                else if (table is SkillPropagationTable skillPropagationTable)
                {
                    ParseSkillPropagationTable(skillPropagationTable, values);
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
                else if (table is BasicItemTable basicItemTable)
                {
                    ParseBasicItemTable(basicItemTable, values);
                }
                else if (table is EquipmentBaseStatTable equipmentBaseStatTable)
                {
                    ParseEquipmentBaseStatTable(equipmentBaseStatTable, values);
                }
                else if (table is DungeonTable dungeonTable)
                {
                    ParseDungeonTable(dungeonTable, values);
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

        private static void ParseDungeonTable(DungeonTable table, string[] values)
        {
            if (values.Length < 16)
            {
                Debug.LogError($"[ParseDungeonTable] Invalid data length. Expected at least 16, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.RoomCount = int.Parse(values[2]);
            // 시트 컬럼명은 MonsterTableId 유지. "1009|1010"처럼 |로 여러 개 입력 가능
            table.MonsterTableIds = ParseIntList(values[3]) ?? new List<int>();
            table.MonsterLevel = int.Parse(values[4]);
            table.BossTableId = int.Parse(values[5]);
            table.BossLevel = int.Parse(values[6]);
            table.CompletionGold = int.Parse(values[7]);
            table.ChestGold = int.Parse(values[8]);
            table.ReplacementCooldownHours = ParseFloatSafe(values, 9);
            table.MonsterCountMin = int.Parse(values[10]);
            table.MonsterCountMax = int.Parse(values[11]);
            table.RoomWidthMin = int.Parse(values[12]);
            table.RoomWidthMax = int.Parse(values[13]);
            table.RoomHeightMin = int.Parse(values[14]);
            table.RoomHeightMax = int.Parse(values[15]);
            // 던전 드롭 (ITEM_DROP_BALANCE_DESIGN §7.6). 빈 셀이면 0
            table.MonsterLevelOffset = ParseIntSafe(values, 16);
            table.BossLevelOffset = ParseIntSafe(values, 17);
            table.ChestDropRolls = ParseIntSafe(values, 18);
            table.DropQuantityBonus = ParseIntSafe(values, 19);
            table.DropRarityBonus = ParseIntSafe(values, 20);

            if (table.MonsterLevelOffset < -98 || table.MonsterLevelOffset > 98 ||
                table.BossLevelOffset < -98 || table.BossLevelOffset > 98 ||
                table.ChestDropRolls < 0 || table.ChestDropRolls > DropBonusResolver.MAX_ROLLS_PER_SOURCE ||
                table.DropQuantityBonus < -100 || table.DropRarityBonus < -100)
            {
                throw new FormatException($"DungeonTable Id {table.Id} contains an out-of-range drop value.");
            }

            if (table.RoomCount < 6 || table.RoomCount > 12 ||
                HasValidIds(table.MonsterTableIds) == false || table.MonsterLevel <= 0 ||
                table.BossTableId <= 0 || table.BossLevel <= 0 ||
                table.CompletionGold < 0 || table.ChestGold < 0 ||
                table.ReplacementCooldownHours < 0f ||
                table.MonsterCountMin < 0 || table.MonsterCountMax < table.MonsterCountMin ||
                table.MonsterCountMax > 8 ||
                table.RoomWidthMin < 7 || table.RoomWidthMax < table.RoomWidthMin ||
                table.RoomWidthMax > 24 ||
                table.RoomHeightMin < 7 || table.RoomHeightMax < table.RoomHeightMin ||
                table.RoomHeightMax > 24)
            {
                throw new FormatException($"DungeonTable Id {table.Id} contains an out-of-range value.");
            }
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
            if (values.Length < 16)
            {
                Debug.LogError($"[ParseNpcTable] Invalid data length. Expected at least 16, got {values.Length}. Id: {table.Id}");
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
            // values[10]: 구 WeaponId 자리. 시트 컬럼명을 EquipTableId로 교체하고 NpcEquipTable.Id를 입력하라.
            table.EquipTableId = int.Parse(values[10]);
            table.AiTableId = int.Parse(values[11]);
            table.DropId = int.Parse(values[12]);
            table.DropRateBonus = int.Parse(values[13]);
            table.DropRarityBonus = int.Parse(values[14]);
            // P열 Gender: NPC 종류마다 Male/Female 중 하나. 범용(빈칸·Unknown)은 허용하지 않는다.
            if (Enum.TryParse(values[15].Trim(), out NpcGender gender) == false ||
                (gender != NpcGender.Male && gender != NpcGender.Female))
                Debug.LogError($"[ParseNpcTable] Id={table.Id}({table.Name}): Gender='{values[15]}' — Male 또는 Female이어야 합니다.");
            table.Gender = gender;
        }

        /// <summary>
        /// NpcEquipTable 파싱. A=Id, B=Desc, 이후 C:Z는 슬롯별 ItemId/Chance 쌍이다.
        /// </summary>
        private static void ParseNpcEquipTable(NpcEquipTable table, string[] values)
        {
            if (values.Length < 26)
            {
                Debug.LogError($"[ParseNpcEquipTable] Invalid data length. "
                    + $"Expected at least 26, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Desc = values[1];
            table.WeaponRight = ParseIntSafe(values, 2);
            table.WeaponRightChance = ParseIntSafe(values, 3);
            table.WeaponLeft = ParseIntSafe(values, 4);
            table.WeaponLeftChance = ParseIntSafe(values, 5);
            table.Helmet = ParseIntSafe(values, 6);
            table.HelmetChance = ParseIntSafe(values, 7);
            table.Armor = ParseIntSafe(values, 8);
            table.ArmorChance = ParseIntSafe(values, 9);
            table.Gloves = ParseIntSafe(values, 10);
            table.GlovesChance = ParseIntSafe(values, 11);
            table.Boots = ParseIntSafe(values, 12);
            table.BootsChance = ParseIntSafe(values, 13);
            table.Necklace = ParseIntSafe(values, 14);
            table.NecklaceChance = ParseIntSafe(values, 15);
            table.RingRight = ParseIntSafe(values, 16);
            table.RingRightChance = ParseIntSafe(values, 17);
            table.RingLeft = ParseIntSafe(values, 18);
            table.RingLeftChance = ParseIntSafe(values, 19);
            table.Belt = ParseIntSafe(values, 20);
            table.BeltChance = ParseIntSafe(values, 21);
            table.EarringRight = ParseIntSafe(values, 22);
            table.EarringRightChance = ParseIntSafe(values, 23);
            table.EarringLeft = ParseIntSafe(values, 24);
            table.EarringLeftChance = ParseIntSafe(values, 25);
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
            table.MoveSpeed = float.Parse(values[14]);
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
            // M열 DropQuantityBonus (옛 DropRateBonus 자리, ITEM_DROP_BALANCE_DESIGN §8.9)
            table.DropQuantityBonus = int.Parse(values[12]);
            table.DropRarityBonus = int.Parse(values[13]);
            table.Level = int.Parse(values[14]);
            // Archetype은 시트에 컬럼 있으면 읽음. 없으면 C# 기본값 Normal 유지.
            table.Archetype = ParseEnumSafe(values, 15, GlobalEnum.MonsterArchetype.Normal);
            // Q열은 단일 바이옴 또는 쉼표로 구분한 서식지 목록. 빈 셀/누락은 Any=공용.
            ParseMonsterBiomes(table, values);
            // R·S열 적중 판정 종 배율 (PLAYER_LEVEL_CHARM_DESIGN §4.4). 빈 셀이면 1.0
            table.AccuracyMul = ParseMulOrDefault(values, 17, table.Id, "AccuracyMul");
            table.EvasionMul = ParseMulOrDefault(values, 18, table.Id, "EvasionMul");
        }

        /// <summary>빈 칸은 1.0, 음수는 0으로 막고 오류를 남긴다.</summary>
        private static float ParseMulOrDefault(string[] values, int index, int tableId, string column)
        {
            if (index >= values.Length || string.IsNullOrWhiteSpace(values[index]))
                return 1f;

            float value = ParseFloatSafe(values, index);
            if (value < 0f)
            {
                Debug.LogError($"[ParseMonsterTable] {column} must be >= 0 (Id={tableId}, value={value})");
                return 0f;
            }
            return value;
        }

        private static void ParseMonsterBiomes(MonsterTable table, string[] values)
        {
            table.AdditionalBiomes = null;
            if (values.Length <= 16 || values[16].Contains(",") == false)
            {
                table.Biome = ParseEnumSafe(values, 16, GlobalEnum.BiomeType.Any);
                return;
            }

            string[] names = values[16].Split(',');
            var biomes = new List<GlobalEnum.BiomeType>();
            for (int i = 0; i < names.Length; i++)
            {
                if (Enum.TryParse(names[i].Trim(), out GlobalEnum.BiomeType biome) == false
                    || Enum.IsDefined(typeof(GlobalEnum.BiomeType), biome) == false
                    || biome == GlobalEnum.BiomeType.Any)
                {
                    throw new FormatException($"Monster {table.Id}: invalid biome list '{values[16]}'. Any must be used alone.");
                }

                if (biomes.Contains(biome) == false)
                    biomes.Add(biome);
            }

            table.Biome = biomes[0];
            if (biomes.Count > 1)
                table.AdditionalBiomes = biomes.GetRange(1, biomes.Count - 1).ToArray();
        }

        /// <summary>
        /// MonsterArchetypeTable 파싱.
        /// 컬럼: A=Id, B=Archetype, C=HpMul, D=DmgMul, E=Note, F=ResistAdd, G=DropRolls, H=DropRarityBonus
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

            // 등급 보정·보상 (MONSTER_RARITY_DESIGN.md §3). 빈 셀이면 저항 0, 드랍 1회, 보너스 0
            table.ResistAdd = ParseIntSafe(values, 5);
            table.DropRolls = Mathf.Max(1, ParseIntSafe(values, 6));
            table.DropRarityBonus = ParseIntSafe(values, 7);

            // 장비 옵션 T11·T12 특수 판정 확률 (만분율). 빈 셀이면 0 = 판정 없음
            table.ModTier11ChanceBp = Mathf.Clamp(ParseIntSafe(values, 8), 0, ItemLevelModTierRules.CHANCE_SCALE);
            table.ModTier12ChanceBp = Mathf.Clamp(ParseIntSafe(values, 9), 0, ItemLevelModTierRules.CHANCE_SCALE);

            // 접사 개수·접사당 드랍 판정 (MONSTER_AFFIX_DESIGN.md §3·§8). 빈 셀이면 접사 없음, 접사당 판정 0
            table.AffixBaseCount = Mathf.Clamp(ParseIntSafe(values, 10), 0, MonsterAffixComponent.MAX_AFFIXES);
            table.AffixExtraChance = Mathf.Clamp(ParseIntSafe(values, 11), 0, 100);
            table.DropRollsPerAffix = Mathf.Max(0, ParseIntSafe(values, 12));

            // 접사 1개당 아이템 희귀도 (ITEM_DROP_BALANCE_DESIGN §8.4). 빈 셀이면 0
            table.RarityPerAffix = Mathf.Max(0, ParseIntSafe(values, 13));

            // 처치 경험치 등급 배율 (PLAYER_LEVEL_CHARM_DESIGN §3.2). 빈 셀이면 1.0
            table.ExpMul = values.Length > 14 && string.IsNullOrWhiteSpace(values[14]) == false
                ? Mathf.Max(0f, ParseFloatSafe(values, 14))
                : 1f;
        }

        /// <summary>
        /// CharmModTierTable 파싱 (PLAYER_LEVEL_CHARM_DESIGN §6.3).
        /// 컬럼: Id, ModName(읽지 않음), ModId, Tier, Min1x1, Max1x1, Min1x2, Max1x2, Min3Cell, Max3Cell, Min2x2, Max2x2. 빈칸은 0
        /// </summary>
        private static void ParseCharmModTierTable(CharmModTierTable table, string[] values)
        {
            if (values.Length < 4)
            {
                Debug.LogError($"[ParseCharmModTierTable] Invalid data length. Expected at least 4, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.ModId = int.Parse(values[2]);
            table.Tier = int.Parse(values[3]);
            table.Min1x1 = ParseIntSafe(values, 4);
            table.Max1x1 = ParseIntSafe(values, 5);
            table.Min1x2 = ParseIntSafe(values, 6);
            table.Max1x2 = ParseIntSafe(values, 7);
            table.Min3Cell = ParseIntSafe(values, 8);
            table.Max3Cell = ParseIntSafe(values, 9);
            table.Min2x2 = ParseIntSafe(values, 10);
            table.Max2x2 = ParseIntSafe(values, 11);
        }

        // 참 값 표의 모양 칸 순서와 각 칸을 대표하는 모양 (1×3·L자는 3칸 칸 하나)
        private static readonly GlobalEnum.CharmShape[] CharmValueColumnShapes =
        {
            GlobalEnum.CharmShape.Single, GlobalEnum.CharmShape.Line2, GlobalEnum.CharmShape.Line3, GlobalEnum.CharmShape.Square2,
        };
        private static readonly string[] CharmValueColumnNames = { "1×1", "1×2", "3칸", "2×2" };

        private static void GetCharmValueColumn(CharmModTierTable row, int column, out int min, out int max)
        {
            switch (column)
            {
                case 0: min = row.Min1x1; max = row.Max1x1; break;
                case 1: min = row.Min1x2; max = row.Max1x2; break;
                case 2: min = row.Min3Cell; max = row.Max3Cell; break;
                default: min = row.Min2x2; max = row.Max2x2; break;
            }
        }

        /// <summary>
        /// CharmModTierTable 검증 (§6.3): 참 옵션 풀(CharmRules)의 Mod마다 T1~T10이 빠짐없이 있고, 풀 밖 Mod·범위 밖 티어·겹침이 없다.
        /// 모양 칸은 그 옵션이 붙는 모양(CharmRules 크기 묶음)이면 정수 1 이상·최대 ≥ 최소·티어가 올라갈수록 줄지 않아야 하고,
        /// 붙지 않는 모양이면 비어 있어야(0) 한다.
        /// </summary>
        private static bool ValidateCharmModTierSetup()
        {
            List<CharmModTierTable> rows = GetTableList<CharmModTierTable>();
            if (rows == null || rows.Count == 0)
            {
                Debug.LogError("[ValidateCharmModTier] CharmModTierTable 행이 없습니다.");
                return false;
            }

            int errors = 0;
            HashSet<int> poolModIds = new HashSet<int>();
            for (int i = 0; i < CharmRules.OptionModIds.Count; i++)
                poolModIds.Add(CharmRules.OptionModIds[i]);

            Dictionary<long, CharmModTierTable> byModTier = new Dictionary<long, CharmModTierTable>();
            for (int i = 0; i < rows.Count; i++)
            {
                CharmModTierTable row = rows[i];
                if (poolModIds.Contains(row.ModId) == false)
                {
                    Debug.LogError($"[ValidateCharmModTier] Id={row.Id}의 ModId={row.ModId}는 참 옵션 풀에 없습니다.");
                    errors++;
                }
                if (row.Tier < 1 || row.Tier > CharmRules.MAX_CHARM_TIER)
                {
                    Debug.LogError($"[ValidateCharmModTier] Id={row.Id}의 Tier={row.Tier}가 1~{CharmRules.MAX_CHARM_TIER} 밖입니다.");
                    errors++;
                }
                for (int column = 0; column < CharmValueColumnShapes.Length; column++)
                {
                    GetCharmValueColumn(row, column, out int min, out int max);
                    bool allowed = CharmRules.IsOptionAllowedForShape(row.ModId, CharmValueColumnShapes[column]);
                    if (allowed == true && (min < 1 || max < min))
                    {
                        Debug.LogError($"[ValidateCharmModTier] Id={row.Id}의 {CharmValueColumnNames[column]} 값({min}~{max})이 올바르지 않습니다. " +
                            "이 모양에 붙는 옵션이라 정수 1 이상이고 최대 ≥ 최소여야 합니다.");
                        errors++;
                    }
                    else if (allowed == false && (min != 0 || max != 0))
                    {
                        Debug.LogError($"[ValidateCharmModTier] Id={row.Id}의 {CharmValueColumnNames[column]} 칸({min}~{max})은 비워야 합니다. " +
                            "이 모양에 붙지 않는 옵션입니다(CharmRules 모양별 옵션).");
                        errors++;
                    }
                }
                long key = ((long)row.ModId << 32) | (uint)row.Tier;
                if (byModTier.ContainsKey(key) == true)
                {
                    Debug.LogError($"[ValidateCharmModTier] ModId={row.ModId}, Tier={row.Tier} 조합이 겹칩니다 (Id={row.Id}).");
                    errors++;
                    continue;
                }
                byModTier.Add(key, row);
            }

            foreach (int modId in poolModIds)
            {
                CharmModTierTable previous = null;
                for (int tier = 1; tier <= CharmRules.MAX_CHARM_TIER; tier++)
                {
                    if (byModTier.TryGetValue(((long)modId << 32) | (uint)tier, out CharmModTierTable row) == false)
                    {
                        Debug.LogError($"[ValidateCharmModTier] 참 옵션 Mod {modId}의 T{tier} 행이 없습니다.");
                        errors++;
                        previous = null;
                        continue;
                    }
                    for (int column = 0; previous != null && column < CharmValueColumnShapes.Length; column++)
                    {
                        GetCharmValueColumn(row, column, out int min, out int max);
                        GetCharmValueColumn(previous, column, out int previousMin, out int previousMax);
                        if (min < previousMin || max < previousMax)
                        {
                            Debug.LogError($"[ValidateCharmModTier] 참 옵션 Mod {modId}의 T{tier} {CharmValueColumnNames[column]} 값({min}~{max})이 " +
                                $"아래 티어({previousMin}~{previousMax})보다 작습니다.");
                            errors++;
                        }
                    }
                    previous = row;
                }
            }

            if (errors == 0)
                Debug.Log($"[ValidateCharmModTier] PASS — Rows={rows.Count}");
            return errors == 0;
        }

        /// <summary>ExpTable 파싱 (PLAYER_LEVEL_CHARM_DESIGN §3.4). 컬럼: Id(레벨), RequiredExp, MonsterExp</summary>
        private static void ParseExpTable(ExpTable table, string[] values)
        {
            if (values.Length < 3)
            {
                Debug.LogError($"[ParseExpTable] Invalid data length. Expected at least 3, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.RequiredExp = int.Parse(values[1]);
            table.MonsterExp = int.Parse(values[2]);
        }

        /// <summary>
        /// ExpTable 검증: 레벨 1~최고 레벨이 빈틈없이 있고, 최고 레벨 전까지 RequiredExp가 양수이며 MonsterExp가 양수다.
        /// </summary>
        private static bool ValidateExpSetup()
        {
            List<ExpTable> rows = GetTableList<ExpTable>();
            if (rows == null || rows.Count == 0)
            {
                Debug.LogError("[ValidateExp] ExpTable 행이 없습니다.");
                return false;
            }

            int errors = 0;
            HashSet<int> levels = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                ExpTable row = rows[i];
                if (levels.Add(row.Id) == false)
                {
                    Debug.LogError($"[ValidateExp] 레벨 {row.Id} 중복");
                    errors++;
                }
                if (row.MonsterExp <= 0)
                {
                    Debug.LogError($"[ValidateExp] 레벨 {row.Id} MonsterExp가 0 이하입니다.");
                    errors++;
                }
                if (row.Id < PlayerLevelRules.MAX_LEVEL && row.RequiredExp <= 0)
                {
                    Debug.LogError($"[ValidateExp] 레벨 {row.Id} RequiredExp가 0 이하입니다.");
                    errors++;
                }
            }

            for (int level = 1; level <= PlayerLevelRules.MAX_LEVEL; level++)
            {
                if (levels.Contains(level) == false)
                {
                    Debug.LogError($"[ValidateExp] 레벨 {level} 행이 없습니다.");
                    errors++;
                }
            }

            if (errors == 0)
                Debug.Log($"[ValidateExp] PASS — Levels={rows.Count}");
            return errors == 0;
        }

        /// <summary>
        /// MonsterAffixTable 파싱 (MONSTER_AFFIX_DESIGN.md §5.1).
        /// 컬럼: Id, Name, Kind, Group, Weight, MinLevel, AllowedArchetypes, RequiredDamageType,
        ///       Stat1, ModType1, Value1, Stat2, ModType2, Value2, Stat3, ModType3, Value3, AuraBuffId, AuraRadius
        /// </summary>
        private static void ParseMonsterAffixTable(MonsterAffixTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseMonsterAffixTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1].Trim();
            table.Kind = ParseEnumSafe(values, 2, GlobalEnum.MonsterAffixKind.Stat, table.Id);
            table.Group = values[3].Trim();
            table.Weight = Mathf.Max(0, ParseIntSafe(values, 4));
            table.MinLevel = Mathf.Max(0, ParseIntSafe(values, 5));
            table.AllowedArchetypes = ParseMonsterArchetypeMask(values, 6, table.Id);

            // 빈 칸 또는 None이면 피해 종류 제한 없음
            string damageTypeRaw = values.Length > 7 ? values[7].Trim() : string.Empty;
            table.HasRequiredDamageType = string.IsNullOrEmpty(damageTypeRaw) == false
                && string.Equals(damageTypeRaw, "None", StringComparison.OrdinalIgnoreCase) == false;
            table.RequiredDamageType = table.HasRequiredDamageType
                ? ParseEnumSafe(values, 7, GlobalEnum.DamageType.Physics, table.Id)
                : GlobalEnum.DamageType.Physics;

            table.Stat1 = ParseEnumSafe(values, 8, GlobalEnum.Stat.Str, table.Id);
            table.ModType1 = ParseEnumSafe(values, 9, StatModifierType.Add, table.Id);
            table.Value1 = ParseIntSafe(values, 10);
            table.Stat2 = ParseEnumSafe(values, 11, GlobalEnum.Stat.Str, table.Id);
            table.ModType2 = ParseEnumSafe(values, 12, StatModifierType.Add, table.Id);
            table.Value2 = ParseIntSafe(values, 13);
            table.Stat3 = ParseEnumSafe(values, 14, GlobalEnum.Stat.Str, table.Id);
            table.ModType3 = ParseEnumSafe(values, 15, StatModifierType.Add, table.Id);
            table.Value3 = ParseIntSafe(values, 16);
            table.AuraBuffId = ParseIntSafe(values, 17);
            table.AuraRadius = ParseFloatSafe(values, 18);

            if (string.IsNullOrEmpty(table.Name))
                Debug.LogError($"[ParseMonsterAffixTable] Name is empty (Id={table.Id})");
            if (table.AllowedArchetypes == GlobalEnum.MonsterArchetypeMask.None)
                Debug.LogError($"[ParseMonsterAffixTable] AllowedArchetypes is empty — this affix never rolls (Id={table.Id})");
            if (table.Kind == GlobalEnum.MonsterAffixKind.Stat && table.Value1 == 0 && table.Value2 == 0 && table.Value3 == 0)
                Debug.LogError($"[ParseMonsterAffixTable] Stat affix has no stat value (Id={table.Id})");
            if (table.Kind == GlobalEnum.MonsterAffixKind.Aura && (table.AuraBuffId <= 0 || table.AuraRadius <= 0f))
                Debug.LogError($"[ParseMonsterAffixTable] Aura affix needs AuraBuffId and AuraRadius > 0 (Id={table.Id})");
        }

        /// <summary>
        /// MonsterArchetypeMask 파싱. "All", "Magic|Rare", "Magic, Rare" 모두 허용. 빈 칸은 None.
        /// </summary>
        private static GlobalEnum.MonsterArchetypeMask ParseMonsterArchetypeMask(string[] values, int index, int tableId)
        {
            if (index >= values.Length || string.IsNullOrWhiteSpace(values[index]))
                return GlobalEnum.MonsterArchetypeMask.None;

            string raw = values[index].Trim().Replace("|", ",");
            if (int.TryParse(raw, out _) == false
                && Enum.TryParse(raw, true, out GlobalEnum.MonsterArchetypeMask mask))
            {
                return mask & GlobalEnum.MonsterArchetypeMask.All;
            }

            Debug.LogError($"[ParseMonsterAffixTable] Unknown AllowedArchetypes value: '{values[index]}' (Id={tableId})");
            return GlobalEnum.MonsterArchetypeMask.None;
        }

        private static void ParseItemTable(ItemTable table, string[] values)
        {
            // 전체 범위: A:S = 19개 컬럼 (Phase D에서 4개 + 트랙 A SourceMonsterId 1개 추가)
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
            // 장비는 인스턴스마다 옵션이 달라 겹치면 두 번째 옵션이 사라진다 (Inventory.AddItem은 같은 Id를 수량으로 합친다).
            if (table.ItemType == GlobalEnum.ItemType.Equipment && table.Stackable == true)
                Debug.LogError($"[ParseItemTable] Id={table.Id}({table.Name}): 장비는 Stackable이 FALSE여야 합니다.");
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

            // 트랙 A 신규 컬럼 (시트 갱신 전에는 길이 < 19일 수 있어 안전 가드)
            table.SourceMonsterId = ParseIntSafe(values, 18);
        }

        private static void ParseBuildableItemTable(BuildableItemTable table, string[] values)
        {
            // 전체 범위: A:AA = 27개 컬럼 (Z=MinSeparation, AA=BuildHours)
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
            table.BuildHours = ParseFloatSafe(values, 26);
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

        /// <summary>
        /// Y/N bool 컬럼 파서 (NpcObjective 표). "Y"/"N" (대소문자 무관)와 ParseBoolSafe가 받는 값을 모두 허용한다.
        /// 해석할 수 없는 값은 시트 저작 오류로 기록하고 defaultValue를 쓴다.
        /// </summary>
        private static bool ParseYesNoSafe(string[] values, int index, bool defaultValue, int rowId)
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (string.Equals(s, "Y", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "N", StringComparison.OrdinalIgnoreCase)) return false;
            if (bool.TryParse(s, out bool v)) return v;
            if (s == "1") return true;
            if (s == "0") return false;
            Debug.LogError($"[ParseYesNoSafe] Id={rowId}: '{s}' is not Y/N. Defaulting to {defaultValue}.");
            return defaultValue;
        }

        /// <summary>문자열 컬럼. 줄 끝 빈 칸이 TSV에서 빠져도 빈 문자열로 읽는다.</summary>
        private static string ParseTextSafe(string[] values, int index)
        {
            return index < values.Length ? values[index]?.Trim() ?? string.Empty : string.Empty;
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
        /// <param name="rowId">진단 메시지에 표시할 테이블 행 Id. 넘기지 않으면 생략된다.</param>
        private static T ParseEnumSafe<T>(string[] values, int index, T defaultValue, int rowId = -1) where T : struct, Enum
        {
            if (index >= values.Length) return defaultValue;
            string s = values[index]?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (Enum.TryParse<T>(s, ignoreCase: true, out T v) && Enum.IsDefined(typeof(T), v)) return v;

            // 해석 불가한 enum 문자열은 언제나 시트 저작 오류이므로 오류로 기록한다.
            string rowInfo = rowId >= 0 ? $" Id={rowId}:" : "";
            Debug.LogError($"[ParseEnumSafe]{rowInfo} '{s}' is not a valid {typeof(T).Name}. Defaulting to {defaultValue}.");
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
            // 전체 범위: A:O = 15개 컬럼
            // (Id=Zone, Name, Description(웹용), MainGroupCountMin/Max, MainGroupSizeMin/Max,
            //  SubGroupCountMin/Max, SubGroupSizeMin/Max, GroupRadius, InterGroupMinDistance,
            //  MagicPackRate, RarePackRate) — 마지막 2개는 빈 셀/누락 시 0(등급 무리 없음)
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
            table.MagicPackRate         = ParseIntSafe(values, 13);
            table.RarePackRate          = ParseIntSafe(values, 14);
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
            // 참·참 판 가중치 (PLAYER_LEVEL_CHARM_DESIGN §6.6). 빈 칸이면 0 = 드랍 없음
            table.CharmRate = Mathf.Max(0, ParseIntSafe(values, 11));
            table.CharmBoardRate = Mathf.Max(0, ParseIntSafe(values, 12));
        }

        /// <summary>
        /// DropBandTable 파싱 (ITEM_DROP_BALANCE_DESIGN §7.1).
        /// 컬럼: Id, MinLevel, MaxLevel, NormalRate, MagicRate, RareRate, RareModMin, RareModMax,
        ///       NormalQualityMin, NormalQualityMax, QuantityBonus, HigherCurrencyMul,
        ///       BookCapacityMin, BookCapacityMax, BookSlotChance, CurrentBandShare, SellPriceMul (R열 Note 제외)
        /// </summary>
        private static void ParseDropBandTable(DropBandTable table, string[] values)
        {
            if (values.Length < 17)
                throw new FormatException($"DropBandTable Id {table.Id}: expected at least 17 columns, got {values.Length}.");

            table.MinLevel = int.Parse(values[1]);
            table.MaxLevel = int.Parse(values[2]);
            table.NormalRate = int.Parse(values[3]);
            table.MagicRate = int.Parse(values[4]);
            table.RareRate = int.Parse(values[5]);
            table.RareModMin = int.Parse(values[6]);
            table.RareModMax = int.Parse(values[7]);
            table.NormalQualityMin = int.Parse(values[8]);
            table.NormalQualityMax = int.Parse(values[9]);
            table.QuantityBonus = int.Parse(values[10]);
            table.HigherCurrencyMul = int.Parse(values[11]);
            table.BookCapacityMin = int.Parse(values[12]);
            table.BookCapacityMax = int.Parse(values[13]);
            table.BookSlotChance = int.Parse(values[14]);
            table.CurrentBandShare = int.Parse(values[15]);
            table.SellPriceMul = int.Parse(values[16]);

            if (table.MinLevel < ItemLevelModTierRules.MIN_ITEM_LEVEL || table.MaxLevel > ItemLevelModTierRules.MAX_ITEM_LEVEL ||
                table.MinLevel > table.MaxLevel ||
                table.NormalRate < 0 || table.MagicRate < 0 || table.RareRate < 0 ||
                table.NormalRate + table.MagicRate + table.RareRate != 100 || table.RareRate >= 100 ||
                table.RareModMin < 3 || table.RareModMax > 6 || table.RareModMin > table.RareModMax ||
                table.NormalQualityMin < 0 || table.NormalQualityMax > EquipmentQualityRules.MAX_QUALITY ||
                table.NormalQualityMin > table.NormalQualityMax ||
                table.QuantityBonus < -100 || table.HigherCurrencyMul < 0 ||
                table.BookCapacityMin < 0 || table.BookCapacityMin > table.BookCapacityMax ||
                table.BookSlotChance < 0 || table.BookSlotChance > 100 ||
                table.CurrentBandShare < 0 || table.CurrentBandShare > 100 || table.SellPriceMul < 0)
            {
                throw new FormatException($"DropBandTable Id {table.Id} contains an out-of-range value.");
            }
        }

        /// <summary>
        /// 드롭 구간표가 레벨 1~99를 겹침·빈틈 없이 덮는지 확인한다. 레벨 오름차순 행이어야 한다.
        /// 표를 받지 않은 다운로드(다른 단일 표)에서는 검사하지 않는다.
        /// </summary>
        private static bool ValidateDropBandSetup()
        {
            List<DropBandTable> bands = GetTableList<DropBandTable>();
            if (bands == null)
                return true;

            bands.Sort((a, b) => a.MinLevel.CompareTo(b.MinLevel));
            int expectedMin = ItemLevelModTierRules.MIN_ITEM_LEVEL;
            for (int i = 0; i < bands.Count; i++)
            {
                if (bands[i].MinLevel != expectedMin)
                {
                    Debug.LogError($"[ValidateDropBand] Id={bands[i].Id}: MinLevel {bands[i].MinLevel} — {expectedMin}이어야 합니다(겹침 또는 빈틈).");
                    return false;
                }
                expectedMin = bands[i].MaxLevel + 1;
            }
            if (expectedMin != ItemLevelModTierRules.MAX_ITEM_LEVEL + 1)
            {
                Debug.LogError($"[ValidateDropBand] 마지막 구간이 레벨 {ItemLevelModTierRules.MAX_ITEM_LEVEL}까지 덮지 않습니다.");
                return false;
            }

            Debug.Log($"[ValidateDropBand] PASS — Bands={bands.Count}");
            return true;
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
            table.SkillEffectIds     = ParseIntList(values.Length > 33 ? values[33] : "");
            table.ExecutionType      = values.Length > 34 && string.IsNullOrWhiteSpace(values[34]) == false
                                       ? (SkillExecutionType)Enum.Parse(typeof(SkillExecutionType), values[34])
                                       : SkillExecutionType.MultiHit;
            table.ChannelingInterval = values.Length > 35 && float.TryParse(values[35], out var ci) ? ci : 0f;
            table.MaxChargeTime      = values.Length > 36 && float.TryParse(values[36], out var mct) ? mct : 0f;
            table.MinChargeRatio     = values.Length > 37 && float.TryParse(values[37], out var mcr) ? mcr : 0f;
            // Phase 3: BaseProjectileCount (빈 셀이면 1로 기본값. 발사 0개 방지)
            table.BaseProjectileCount = values.Length > 38 && int.TryParse(values[38], out var bpc) ? Mathf.Max(1, bpc) : 1;

            table.AimTrackingMode = ParseEnumSafe(values, 39, GlobalEnum.SkillAimTrackingMode.Default, table.Id);
            table.AimTurnSpeedDegPerSec = ParseFloatSafe(values, 40);
            table.MovementMode = ParseEnumSafe(values, 41, GlobalEnum.SkillMovementMode.Default, table.Id);
            table.MoveSpeedWhileUsingMul = ParseFloatSafe(values, 42);
            // AR열: 드롭을 명시적으로 허용한 스킬만 랜덤 스킬북 후보에 포함. 빈 셀/구 스키마는 false.
            table.DropEnabled = ParseBoolSafe(values, 43);
            // AS열: 실행 단계 넉백 저항 (SKILL_DESIGN §6.10 적용 규칙 7). 빈 셀/구 스키마는 false.
            table.ForcedMoveImmune = ParseBoolSafe(values, 44);

            // 두 수치 컬럼은 0이 "미설정 → 런타임 기본값 사용"을 뜻한다(POE2_CURSOR_TRACKING_PLAN §4.1 빈 셀 기본값 표).
            // 따라서 범위를 벗어난 값을 0으로 되돌리면 로그가 말하는 값과 실제 적용값이 달라진다.
            // 여기서는 (1) 실제로 적용될 값을 메시지에 그대로 밝히고, (2) 의도에 가장 가까운 유효값으로 보정한다.
            // 참고: "회전 금지"는 AimTrackingMode=SnapshotOnCommand, "완전 정지"는 MovementMode=Locked로 표현하므로
            //       0을 미설정 센티널로 쓰는 데서 오는 표현력 손실은 없다.
            if (table.AimTurnSpeedDegPerSec < 0f)
            {
                Debug.LogError($"[ParseSkillTable] Skill Id={table.Id}({table.Name}): AimTurnSpeedDegPerSec({table.AimTurnSpeedDegPerSec})는 0 이상이어야 합니다. " +
                               $"0(미설정)으로 되돌리므로 런타임 기본값 {ARPG.Skill.SkillUsePolicyResolver.DefaultAimTurnSpeedDegPerSec}도/초가 적용됩니다. " +
                               $"회전을 막으려면 AimTrackingMode=SnapshotOnCommand를 사용하세요.");
                table.AimTurnSpeedDegPerSec = 0f;
            }

            if (table.MoveSpeedWhileUsingMul > 100f)
            {
                // 0으로 되돌리면 런타임 기본값 30%가 적용되어 "빠르게 하려다 오히려 느려지는" 정반대 결과가 된다.
                Debug.LogError($"[ParseSkillTable] Skill Id={table.Id}({table.Name}): MoveSpeedWhileUsingMul({table.MoveSpeedWhileUsingMul})는 0~100 범위여야 합니다. " +
                               $"의도에 가장 가까운 100(감속 없음)으로 보정합니다.");
                table.MoveSpeedWhileUsingMul = 100f;
            }
            else if (table.MoveSpeedWhileUsingMul < 0f)
            {
                float defaultPercent = ARPG.Skill.SkillUsePolicyResolver.DefaultMoveSpeedWhileUsingMultiplier * 100f;
                Debug.LogError($"[ParseSkillTable] Skill Id={table.Id}({table.Name}): MoveSpeedWhileUsingMul({table.MoveSpeedWhileUsingMul})는 0~100 범위여야 합니다. " +
                               $"0(미설정)으로 되돌리므로 런타임 기본값 {defaultPercent}%가 적용됩니다. " +
                               $"완전히 멈추려면 MovementMode=Locked를 사용하세요.");
                table.MoveSpeedWhileUsingMul = 0f;
            }

            // 점프 이동 구간은 Process 단계에서만 실행되므로 ProcessTime이 체공 시간이 된다.
            // 조건은 런타임의 StartJump 발동 조건(SkillType=Jump && ArcHeight>0)과 동일하게 맞춘다.
            // 값은 고치지 않는다 — 런타임이 하한으로 대체하며 로그를 남기고, 여기서는 저작 시점에 드러내는 역할만 한다.
            if (table.SkillType == GlobalEnum.SkillType.Jump && table.ArcHeight > 0f && table.ProcessTime <= 0f)
            {
                Debug.LogError($"[ParseSkillTable] Skill Id={table.Id}({table.Name}): SkillType=Jump, ArcHeight={table.ArcHeight}인데 ProcessTime={table.ProcessTime}입니다. " +
                               $"점프 체공 시간은 ProcessTime에서 오므로 ProcessTime > 0 이어야 합니다. " +
                               $"현재 값이면 포물선 없이 착지 지점으로 즉시 이동합니다. " +
                               $"(StartTime={table.StartTime}, ProcessTime={table.ProcessTime}, EndTime={table.EndTime})");
            }

            if (table.UsesDash == true)
                ValidateDashSkill(table);
        }

        /// <summary>
        /// 돌진(SkillType=Dash) 데이터 제약 (SKILL_DESIGN §6.11). 값은 고치지 않고 저작 시점에 드러낸다.
        /// </summary>
        private static void ValidateDashSkill(SkillTable table)
        {
            string prefix = $"[ParseSkillTable] Skill Id={table.Id}({table.Name}): SkillType=Dash";
            if (table.SkillTargetType != GlobalEnum.SkillTargetType.Direction)
                Debug.LogError($"{prefix}는 SkillTargetType=Direction이어야 합니다. (현재 {table.SkillTargetType})");
            if (table.SkillTargetRange2 <= 0f)
                Debug.LogError($"{prefix}의 돌진 거리 SkillTargetRange2는 0보다 커야 합니다. (현재 {table.SkillTargetRange2})");
            if (table.ProcessTime <= 0f)
                Debug.LogError($"{prefix}의 돌진 시간 ProcessTime은 0보다 커야 합니다. 0이면 첫 프레임에 거리를 한 번에 이동합니다.");
            if (table.ProjectileId > 0 || table.AreaEffectId > 0)
                Debug.LogError($"{prefix}는 ProjectileId·AreaEffectId를 쓸 수 없습니다. (현재 {table.ProjectileId}/{table.AreaEffectId})");
            if (table.ExecutionType != SkillExecutionType.Single && table.ExecutionType != SkillExecutionType.MultiHit)
                Debug.LogError($"{prefix}의 ExecutionType은 Single 또는 MultiHit이어야 합니다. (현재 {table.ExecutionType})");
        }

        /// <summary>
        /// Phase 1: SkillEffectTable 파서.
        /// 컬럼: Id, Name, Description, Tier, EffectType, Kind, Trigger, Param1~3, Probability, PageCost, Condition, ConditionParam
        /// </summary>
        private static void ParseSkillEffectTable(SkillEffectTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseSkillEffectTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Description = values[2];
            table.Tier = ParseIntSafe(values, 3);
            table.EffectType = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[4]);
            // Kind 컬럼(F, EffectType 바로 뒤). 빈 셀이면 EffectType으로 추론(구 IsEffectAction 화이트리스트와 동일) → 시트 마이그레이션 종료 후 이 fallback 제거 가능.
            table.Kind = ParseEnumSafe(values, 5, InferSkillEffectKindFromEffectType(table.EffectType));
            table.Trigger = (GlobalEnum.SkillTrigger)Enum.Parse(typeof(GlobalEnum.SkillTrigger), values[6]);
            table.Param1 = values.Length > 7 && float.TryParse(values[7], out var p1) ? p1 : 0f;
            table.Param2 = values.Length > 8 && float.TryParse(values[8], out var p2) ? p2 : 0f;
            table.Param3 = values.Length > 9 && float.TryParse(values[9], out var p3) ? p3 : 0f;
            table.Probability = values.Length > 10 && int.TryParse(values[10], out var prob) ? prob : 100;
            table.PageCost = ParseIntSafe(values, 11);
            table.Condition = ParseEnumSafe(values, 12, GlobalEnum.PageCondition.None);
            table.ConditionParam = ParseFloatSafe(values, 13);

            if (table.EffectType == GlobalEnum.Stat.Knockback || table.EffectType == GlobalEnum.Stat.Pull)
                ValidateForcedMoveEffect(table);
        }

        /// <summary>
        /// 넉백·당기기 행 검사 (SKILL_DESIGN §6.10). 명중 시 발동하는 액션이어야 하고 거리·시간이 0보다 커야 한다.
        /// </summary>
        private static void ValidateForcedMoveEffect(SkillEffectTable table)
        {
            if (table.Kind != GlobalEnum.SkillEffectKind.EffectAction || table.Trigger != GlobalEnum.SkillTrigger.OnHit)
                Debug.LogError($"[ParseSkillEffectTable] Id={table.Id}({table.Name}): {table.EffectType}는 Kind=EffectAction, Trigger=OnHit이어야 합니다. (Kind={table.Kind}, Trigger={table.Trigger})");
            if (table.Param1 <= 0f || table.Param2 <= 0f)
                Debug.LogError($"[ParseSkillEffectTable] Id={table.Id}({table.Name}): {table.EffectType}는 Param1(거리)과 Param2(시간)가 0보다 커야 합니다. (Param1={table.Param1}, Param2={table.Param2})");
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
                case GlobalEnum.Stat.SkillPropagation:
                case GlobalEnum.Stat.Knockback:
                case GlobalEnum.Stat.Pull:
                    return GlobalEnum.SkillEffectKind.EffectAction;
                default:
                    return GlobalEnum.SkillEffectKind.StatBonus;
            }
        }

        /// <summary>
        /// SkillPropagationTable parser. Column M is the per-hop maximum turn angle.
        /// Legacy 12-column rows remain unrestricted until their sheet is migrated.
        /// </summary>
        private static void ParseSkillPropagationTable(SkillPropagationTable table, string[] values)
        {
            if (values.Length < 12)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Invalid data length. Expected at least 12, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            if (Enum.TryParse(values[2], true, out GlobalEnum.SkillDeliveryType deliveryType) == false
                || Enum.IsDefined(typeof(GlobalEnum.SkillDeliveryType), deliveryType) == false)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): invalid DeliveryType '{values[2]}'; Inherit is used.");
                deliveryType = GlobalEnum.SkillDeliveryType.Inherit;
            }

            table.DeliveryType = deliveryType;
            table.ChildCount = ParseIntSafe(values, 3);
            table.MaxDepth = ParseIntSafe(values, 4);
            table.InitialSearchRadius = ParseFloatSafe(values, 5);
            table.SearchRadius = ParseFloatSafe(values, 6);
            table.HopInterval = ParseFloatSafe(values, 7);
            table.DamageRatioPerGeneration = ParseFloatSafe(values, 8);
            table.MaxTotalDeliveries = ParseIntSafe(values, 9);
            table.AllowRepeatTarget = string.Equals(values[10].Trim(), "true", StringComparison.OrdinalIgnoreCase);
            table.SegmentEffectKey = values[11];
            table.MaxTurnAngleDeg = values.Length > 12 && string.IsNullOrWhiteSpace(values[12]) == false
                ? ParseFloatSafe(values, 12)
                : 180f;

            if (table.ChildCount < 1 || table.ChildCount > 4)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): ChildCount must be 1~4; clamping {table.ChildCount}.");
                table.ChildCount = Mathf.Clamp(table.ChildCount, 1, 4);
            }
            if (table.MaxDepth < 0 || table.MaxDepth > 8)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): MaxDepth must be 0~8; clamping {table.MaxDepth}.");
                table.MaxDepth = Mathf.Clamp(table.MaxDepth, 0, 8);
            }
            if (table.InitialSearchRadius < 0f || table.SearchRadius < 0f || table.HopInterval < 0f || table.DamageRatioPerGeneration < 0f)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): radii, interval, and damage ratio must be non-negative; negative values are set to zero.");
                table.InitialSearchRadius = Mathf.Max(0f, table.InitialSearchRadius);
                table.SearchRadius = Mathf.Max(0f, table.SearchRadius);
                table.HopInterval = Mathf.Max(0f, table.HopInterval);
                table.DamageRatioPerGeneration = Mathf.Max(0f, table.DamageRatioPerGeneration);
            }
            if (table.MaxTurnAngleDeg < 0f || table.MaxTurnAngleDeg > 180f)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): MaxTurnAngleDeg must be 0~180; clamping {table.MaxTurnAngleDeg}.");
                table.MaxTurnAngleDeg = Mathf.Clamp(table.MaxTurnAngleDeg, 0f, 180f);
            }
            int maxTotalDeliveries = SkillPropagationTargetIds.GetMaxTotalDeliveries(table.DeliveryType);
            if (table.MaxTotalDeliveries < 1 || table.MaxTotalDeliveries > maxTotalDeliveries)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): MaxTotalDeliveries must be 1~{maxTotalDeliveries} for DeliveryType={table.DeliveryType}; clamping {table.MaxTotalDeliveries}.");
                table.MaxTotalDeliveries = Mathf.Clamp(table.MaxTotalDeliveries, 1, maxTotalDeliveries);
            }
            if (table.AllowRepeatTarget == true)
            {
                Debug.LogError($"[ParseSkillPropagationTable] Id={table.Id}({table.Name}): AllowRepeatTarget is unsupported and is forced to false.");
                table.AllowRepeatTarget = false;
            }

            // The delivery count depends on the SkillEffect trigger: OnSkillCommit
            // includes the first selected generation, while OnHit starts after its
            // source hit. Runtime validates the actual bound trigger before it
            // creates a root, so a valid OnHit fork is not rejected as a commit tree.
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
        /// '|' 구분 정수 리스트 파싱. 빈 문자열이면 null 반환.
        /// 시트에서 ','는 천 단위 구분과 혼동되므로 '|'만 사용한다.
        /// 예: "1001|1002" → [1001, 1002], "" → null
        /// </summary>
        private static List<int> ParseIntList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var list = new List<int>();
            var tokens = raw.Split('|');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i].Trim(), out var v))
                    list.Add(v);
            }
            return list.Count > 0 ? list : null;
        }

        private static bool HasValidIds(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] <= 0)
                    return false;
            }
            return true;
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
            // M열 (NAMED_THREAT_DESIGN §5.5). 빈칸 = 0 = 순찰 안 함
            table.PatrolRadius = Mathf.Max(0f, ParseFloatSafe(values, 12));
        }

        /// <summary>
        /// NamedThreatTable 파싱 (NAMED_THREAT_DESIGN §11.1). A:S = Id, Name, Kind, MonsterId, AiTableId, MinZone, MaxZone, Biome,
        /// Weight, Title1~3, Affix1, Affix2, Stage2Affix, Stage3Affix, TrophyItemId, NarrativeContentId, Enabled.
        /// 다른 표를 보는 검증은 전체 다운로드의 ValidateNamedThreatSetup이 한다.
        /// </summary>
        private static void ParseNamedThreatTable(NamedThreatTable table, string[] values)
        {
            if (values.Length < 18)
            {
                Debug.LogError($"[ParseNamedThreatTable] Invalid data length. Expected at least 18, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1].Trim();
            table.Kind = ParseEnumSafe(values, 2, GlobalEnum.NamedThreatKind.Predator, table.Id);
            table.MonsterId = ParseIntSafe(values, 3);
            table.AiTableId = ParseIntSafe(values, 4);
            table.MinZone = ParseIntSafe(values, 5, 1);
            table.MaxZone = ParseIntSafe(values, 6, 51);
            table.Biome = ParseEnumSafe(values, 7, GlobalEnum.BiomeType.Any, table.Id);
            table.Weight = Mathf.Max(0, ParseIntSafe(values, 8));
            table.Title1 = values[9].Trim();
            table.Title2 = values[10].Trim();
            table.Title3 = values[11].Trim();
            table.Affix1 = ParseIntSafe(values, 12);
            table.Affix2 = ParseIntSafe(values, 13);
            table.Stage2Affix = ParseIntSafe(values, 14);
            table.Stage3Affix = ParseIntSafe(values, 15);
            table.TrophyItemId = ParseIntSafe(values, 16);
            table.NarrativeContentId = ParseIntSafe(values, 17);
            table.Enabled = ParseBoolSafe(values, 18, true);

            if (string.IsNullOrEmpty(table.Title1) == true && string.IsNullOrEmpty(table.Title2) == true &&
                string.IsNullOrEmpty(table.Title3) == true)
                Debug.LogError($"[ParseNamedThreatTable] Id={table.Id}: 칭호 후보(Title1~3)가 모두 비어 있습니다.");
            if (table.MinZone < 1 || table.MaxZone > 51 || table.MinZone > table.MaxZone)
                Debug.LogError($"[ParseNamedThreatTable] Id={table.Id}: 구역 범위 {table.MinZone}~{table.MaxZone}는 1~51 안이어야 합니다.");
        }

        /// <summary>
        /// NpcObjectiveTable 파싱 (NPC_OBJECTIVE_DESIGN §4.1). A:AG = Id, Name, Category, Priority, Trigger, Target, Complete,
        /// SameKindRefresh, ReactChance, ReactProfile, PrepHours, DeadlineHours, TimeFrom, MaxAttempts, PrepRest·Hunt·Trade·Delve,
        /// ActRest·Hunt·Trade·Delve, AlwaysRecruit, SkipOtherParties, RequestAffinity, RecordDeed, OutcomeTopicHours,
        /// InspectText, StatusText, RequestText, FulfilledText, FulfilledByOtherText, AbandonedText. bool 칸은 Y/N.
        /// </summary>
        private static void ParseNpcObjectiveTable(NpcObjectiveTable table, string[] values)
        {
            if (values.Length < 27)
            {
                Debug.LogError($"[ParseNpcObjectiveTable] Invalid data length. Expected at least 27, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = ParseTextSafe(values, 1);
            table.Category = ParseEnumSafe(values, 2, ARPG.Npc.NpcObjectiveCategory.None, table.Id);
            table.Priority = ParseIntSafe(values, 3);
            table.Trigger = ParseEnumSafe(values, 4, ARPG.Npc.NpcObjectiveTrigger.None, table.Id);
            table.Target = ParseEnumSafe(values, 5, ARPG.Npc.NpcObjectiveTargetKind.None, table.Id);
            table.Complete = ParseEnumSafe(values, 6, ARPG.Npc.NpcObjectiveComplete.None, table.Id);
            table.SameKindRefresh = ParseYesNoSafe(values, 7, false, table.Id);
            table.ReactChance = Mathf.Clamp01(ParseFloatSafe(values, 8, 1f));
            table.ReactProfile = ParseEnumSafe(values, 9, ARPG.Npc.NpcObjectiveReactProfile.None, table.Id);
            table.PrepHours = Mathf.Max(0f, ParseFloatSafe(values, 10));
            table.DeadlineHours = Mathf.Max(0f, ParseFloatSafe(values, 11));
            table.TimeFrom = ParseEnumSafe(values, 12, ARPG.Npc.NpcObjectiveTimeFrom.Created, table.Id);
            table.MaxAttempts = Mathf.Max(0, ParseIntSafe(values, 13));
            table.PrepRest = Mathf.Max(0f, ParseFloatSafe(values, 14, 1f));
            table.PrepHunt = Mathf.Max(0f, ParseFloatSafe(values, 15, 1f));
            table.PrepTrade = Mathf.Max(0f, ParseFloatSafe(values, 16, 1f));
            table.PrepDelve = Mathf.Max(0f, ParseFloatSafe(values, 17, 1f));
            table.ActRest = Mathf.Max(0f, ParseFloatSafe(values, 18, 1f));
            table.ActHunt = Mathf.Max(0f, ParseFloatSafe(values, 19, 1f));
            table.ActTrade = Mathf.Max(0f, ParseFloatSafe(values, 20, 1f));
            table.ActDelve = Mathf.Max(0f, ParseFloatSafe(values, 21, 1f));
            table.AlwaysRecruit = ParseYesNoSafe(values, 22, false, table.Id);
            table.SkipOtherParties = ParseYesNoSafe(values, 23, false, table.Id);
            table.RequestAffinity = Mathf.Max(0, ParseIntSafe(values, 24));
            table.RecordDeed = ParseYesNoSafe(values, 25, false, table.Id);
            table.OutcomeTopicHours = Mathf.Max(0f, ParseFloatSafe(values, 26));
            table.InspectText = ParseTextSafe(values, 27);
            table.StatusText = ParseTextSafe(values, 28);
            table.RequestText = ParseTextSafe(values, 29);
            table.FulfilledText = ParseTextSafe(values, 30);
            table.FulfilledByOtherText = ParseTextSafe(values, 31);
            table.AbandonedText = ParseTextSafe(values, 32);

            if (table.Category == ARPG.Npc.NpcObjectiveCategory.None || table.Trigger == ARPG.Npc.NpcObjectiveTrigger.None ||
                table.Target == ARPG.Npc.NpcObjectiveTargetKind.None || table.Complete == ARPG.Npc.NpcObjectiveComplete.None)
                Debug.LogError($"[ParseNpcObjectiveTable] Id={table.Id}: Category·Trigger·Target·Complete는 None일 수 없습니다.");
            if (table.DeadlineHours <= 0f || table.PrepHours >= table.DeadlineHours)
                Debug.LogError($"[ParseNpcObjectiveTable] Id={table.Id}: 기한({table.DeadlineHours}h)은 0보다 크고 준비({table.PrepHours}h)보다 길어야 합니다.");
            if (table.RequestAffinity > 0 && string.IsNullOrEmpty(table.RequestText) == true)
                Debug.LogError($"[ParseNpcObjectiveTable] Id={table.Id}: RequestAffinity가 있으면 RequestText가 필요합니다.");
            if (string.IsNullOrEmpty(table.InspectText) == true)
                Debug.LogError($"[ParseNpcObjectiveTable] Id={table.Id}: InspectText가 비어 있습니다.");
        }

        /// <summary>
        /// 네임드 템플릿이 가리키는 종·AI·전리품·문구 행과 고정 접사 종류를 확인한다 (NAMED_THREAT_DESIGN §11.1).
        /// 오라 접사는 같은 무리에게 주는 효과라 혼자인 네임드에 쓰지 않는다 (§5.2). 오류는 콘솔에 남긴다.
        /// </summary>
        private static void ValidateNamedThreatSetup(ARPG.Npc.Narrative.NarrativeContentPack narrative)
        {
            List<NamedThreatTable> threats = GetTableList<NamedThreatTable>();
            List<MonsterTable> monsters = GetTableList<MonsterTable>();
            List<AiTable> ais = GetTableList<AiTable>();
            List<ItemTable> items = GetTableList<ItemTable>();
            List<MonsterAffixTable> affixes = GetTableList<MonsterAffixTable>();
            List<MonsterArchetypeTable> archetypes = GetTableList<MonsterArchetypeTable>();
            if (threats == null || monsters == null || ais == null || items == null || affixes == null || archetypes == null)
                return;

            int errors = 0;
            if (archetypes.Exists(a => a.Archetype == GlobalEnum.MonsterArchetype.Named) == false)
            {
                Debug.LogError("[ValidateNamedThreat] MonsterArchetype에 Named 행이 없습니다.");
                errors++;
            }
            for (int i = 0; i < threats.Count; i++)
            {
                NamedThreatTable t = threats[i];
                string where = $"[ValidateNamedThreat] NamedThreat Id={t.Id}({t.Name})";
                if (monsters.Exists(m => m.Id == t.MonsterId) == false) { Debug.LogError($"{where}: MonsterId={t.MonsterId} 행이 없습니다."); errors++; }
                AiTable ai = ais.Find(a => a.Id == t.AiTableId);
                if (ai == null) { Debug.LogError($"{where}: AiTableId={t.AiTableId} 행이 없습니다."); errors++; }
                else if (ai.PatrolRadius <= 0f) Debug.LogWarning($"{where}: AiTableId={t.AiTableId}의 PatrolRadius가 0이라 순찰·귀환하지 않습니다.");
                ItemTable trophy = items.Find(it => it.Id == t.TrophyItemId);
                if (trophy == null) { Debug.LogError($"{where}: TrophyItemId={t.TrophyItemId} 행이 없습니다."); errors++; }
                else if (trophy.SourceMonsterId != t.MonsterId) Debug.LogWarning($"{where}: 전리품 SourceMonsterId({trophy.SourceMonsterId})가 기본 종({t.MonsterId})과 다릅니다.");
                if (narrative != null && narrative.Contents.Exists(c => c.ContentId == t.NarrativeContentId &&
                        c.Kind == ARPG.Npc.Narrative.NarrativeContentKind.NamedThreat) == false)
                {
                    Debug.LogError($"{where}: NarrativeContentId={t.NarrativeContentId}에 NamedThreat 문구 행이 없습니다.");
                    errors++;
                }
                int[] fixedAffixes = { t.Affix1, t.Affix2, t.Stage2Affix, t.Stage3Affix };
                for (int a = 0; a < fixedAffixes.Length; a++)
                {
                    if (fixedAffixes[a] <= 0) continue;
                    MonsterAffixTable affix = affixes.Find(x => x.Id == fixedAffixes[a]);
                    if (affix == null) { Debug.LogError($"{where}: 접사 {fixedAffixes[a]} 행이 없습니다."); errors++; }
                    else if (affix.Kind != GlobalEnum.MonsterAffixKind.Stat) { Debug.LogError($"{where}: 접사 {fixedAffixes[a]}는 스탯형이 아닙니다(오라 금지)."); errors++; }
                }
            }
            if (errors == 0)
                Debug.Log($"[ValidateNamedThreat] PASS — templates={threats.Count}");
        }

        private static void ParseBuffTable(BuffTable table, string[] values)
        {
            // 전체 범위: A:T = 20개 컬럼 (K열 FadeOutTime은 비어 있으면 0, L~T열 스탯 칸은 비어 있으면 미사용)
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
            table.FadeOutTime = ParseFloatSafe(values, 10);

            // L~T열: EffectType=StatModifier용 스탯 보정 3칸 (Stat, ModType, Value). 빈 칸이면 Value 0 = 미사용
            table.Stat1 = ParseEnumSafe(values, 11, GlobalEnum.Stat.Str, table.Id);
            table.ModType1 = ParseEnumSafe(values, 12, StatModifierType.Add, table.Id);
            table.Value1 = ParseIntSafe(values, 13);
            table.Stat2 = ParseEnumSafe(values, 14, GlobalEnum.Stat.Str, table.Id);
            table.ModType2 = ParseEnumSafe(values, 15, StatModifierType.Add, table.Id);
            table.Value2 = ParseIntSafe(values, 16);
            table.Stat3 = ParseEnumSafe(values, 17, GlobalEnum.Stat.Str, table.Id);
            table.ModType3 = ParseEnumSafe(values, 18, StatModifierType.Add, table.Id);
            table.Value3 = ParseIntSafe(values, 19);
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
            // 전체 범위: A:P = 16개 컬럼. 신규 칸이 없는 기존 행은 지연 없는 반복 장판, 넉백 없음.
            // (Id, Name, Description, DamageType, Damage, Radius, Duration, TickInterval,
            //  OnTickBuffId, OnEnterBuffId, TargetFaction, TickEffectName, PrefabKey, ActivationDelay, SingleShot, ForcedMoveEffectId)
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
            table.ActivationDelay = Mathf.Max(0f, ParseFloatSafe(values, 13));
            table.SingleShot = values.Length > 14 && values[14].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            table.ForcedMoveEffectId = ParseIntSafe(values, 15);
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
            // 참 전용 옵션은 장비에 붙지 않으므로 빈 칸을 허용한다 (PLAYER_LEVEL_CHARM_DESIGN §7)
            bool allowsEmpty = isImplicit || CharmRules.IsCharmOnlyMod(tableId);

            if (values.Length <= index || string.IsNullOrEmpty(values[index]))
            {
                if (allowsEmpty == false)
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

        private static void ParseBasicItemTable(BasicItemTable table, string[] values)
        {
            // 컬럼: Id, ItemId, ModTableId, Value1, Value2, Weight (G열부터는 시트 가독성용 보조 컬럼)
            if (values.Length < 6)
            {
                Debug.LogError($"[ParseBasicItemTable] Invalid data length. Expected at least 6, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.ItemId = int.Parse(values[1]);
            table.ModTableId = int.Parse(values[2]);
            table.Value1 = ParseIntSafe(values, 3);
            table.Value2 = ParseIntSafe(values, 4);
            table.Weight = ParseIntSafe(values, 5);
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
