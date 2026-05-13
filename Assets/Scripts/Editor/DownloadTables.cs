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

        [MenuItem("ARPG/Download Table", false, 1)]
        private static async void DownloadTable()
        {
            _tableDic = new();

            await DownloadTable<CreatureTable>("0&range=A:I", 1, SaveType.String);

            await DownloadTable<AiTable>("947794841&range=A:K", 1, SaveType.String);

            await DownloadTable<MonsterTable>("483012127&range=A:P", 1, SaveType.String);

            await DownloadTable<NpcTable>("1460299278&range=A:O", 1, SaveType.String);

            await DownloadTable<MonsterArchetypeTable>("1141585255&range=A:E", 1, SaveType.String);

            await DownloadTable<StatTable>("318209064&range=A:AJ", 1, SaveType.String);

            await DownloadTable<ItemTable>("2064107837&range=A:R", 1, SaveType.String);

            await DownloadTable<BuildableItemTable>("534887250&range=A:Z", 1, SaveType.String);

            // Phase D: JobBonusTable (직업별 시간당 가산 자원)
            await DownloadTable<JobBonusTable>("470575350&range=A:F", 1, SaveType.String);

            await DownloadTable<WeaponBaseStatTable>("853198133&range=A:H", 1, SaveType.String);
            
            await DownloadTable<DropTable>("1241586373&range=A:K", 1, SaveType.String);

            await DownloadTable<DropCurrencyTable>("2071520432&range=A:V", 1, SaveType.String);

            await DownloadTable<DropEquipmentTable>("1267382287&range=A:V", 1, SaveType.String);

            await DownloadTable<SkillTable>("92727160&range=A:AM", 1, SaveType.String);

            // Phase 1: SkillEffect 합성 시스템용 테이블 (+ 스킬 페이지 비용 메타데이터 + Kind 분류 컬럼)
            await DownloadTable<SkillEffectTable>("1681865950&range=A:M", 1, SaveType.String);

            await DownloadTable<SkillBookTable>("1726438368&range=A:C", 1, SaveType.String);

            // 장판(지속 영역 효과) 테이블
            await DownloadTable<AreaEffectTable>("1891935594&range=A:M", 1, SaveType.String);

            await DownloadTable<BuffTable>("127577579&range=A:J", 1, SaveType.String);

            await DownloadTable<AnimationTable>("747631090&range=A:G", 1, SaveType.String);

            await DownloadTable<ProjectileTable>("1810235418&range=A:G", 1, SaveType.String);

            await DownloadTable<ModTable>("1571193978&range=A:J", 1, SaveType.String);

            await DownloadTable<ModTierTable>("1782637736&range=A:J", 1, SaveType.String);
            
            await DownloadTable<ItemImplicitTable>("547967325&range=A:D", 1, SaveType.String);

            await DownloadTable<VillageTable>("441028134&range=A:F", 1, SaveType.String);

            await DownloadTable<VillageStageTable>("467145019&range=A:Q", 1, SaveType.String);

            // 청크 Zone(시드 청크 (0,0) 기준 Chebyshev 거리+1)별 몬스터 스폰 파라미터
            await DownloadTable<ZoneTable>("848445893&range=A:M", 1, SaveType.String);

            //await DownloadTable<BuffEffectTable>("2104311648&range=A:K", 1, SaveType.String);

            foreach (var tableType in _tableDic.Keys)
            {
                var tableList = (IList)_tableDic[tableType];
                if (tableList == null)
                {
                    Debug.LogError($"[DownloadTables] DownloadTable() - tableList({tableType}) is null");
                    continue;
                }

                SaveTable(tableType.Name, _tableDic[tableType], SaveType.String);
            }

            AssetDatabase.Refresh();
            
            Debug.Log("download Completed...");
        }

        private static async Task<bool> DownloadTable<T>(string inSheet, int inStartLine, SaveType inSaveType) where T : TableBase, new()
        {
            string text = await DownloadTableData($"https://docs.google.com/spreadsheets/d/13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA/export?format=tsv&gid={inSheet}");
            var lines = Regex.Split(text, @"\r\n|\n\r|\n|\r");
            if (lines.Length <= 1)
                return false;

            int dataStartLine = inStartLine;

            List<T> tableList = new();
            for (var i = dataStartLine; i < lines.Length; i++)
            {
                CreateTable<T>(lines[i], tableList);
            }

            Type tableType = typeof(T);
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
                else if (table is WeaponBaseStatTable weaponBaseStatTable)
                {
                    ParseWeaponBaseStatTable(weaponBaseStatTable, values);
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
            table.EquipmentBastStatId = int.Parse(values[10]);
            table.EquipmentStatId = int.Parse(values[11]);
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

        private static void ParseWeaponBaseStatTable(WeaponBaseStatTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseWeaponBaseStatTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            // values[1] 은 웹에서만 사용한다.
            table.EquipType = (GlobalEnum.EquipmentType)Enum.Parse(typeof(GlobalEnum.EquipmentType), values[2]);
            table.AttackSpeed = float.Parse(values[3]);
            table.Critical = int.Parse(values[4]);
            table.DamageMin = int.Parse(values[5]);
            table.DamageMax = int.Parse(values[6]);
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
                case GlobalEnum.Stat.LifeSteal:
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
            if (values.Length < 11)
            {
                Debug.LogError($"[ParseAiTable] Invalid data length. Expected at least 11, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.AiType = (GlobalEnum.AiType)Enum.Parse(typeof(GlobalEnum.AiType), values[2]);
            table.BehaviorType = (AIBehaviorType)Enum.Parse(typeof(AIBehaviorType), values[3]);
            table.DetectionRange = float.Parse(values[4]);
            table.SkillId1 = int.Parse(values[5]);
            table.SkillWeight1 = int.Parse(values[6]);
            table.SkillId2 = int.Parse(values[7]);
            table.SkillWeight2 = int.Parse(values[8]);
            table.SkillId3 = int.Parse(values[9]);
            table.SkillWeight3 = int.Parse(values[10]);
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
        /// GlobalEnum.EquipmentTypeMask 파싱. 파이프 구분('Weapon|AllArmor') 지원.
        /// Implicit slot은 빈 칸 허용 (ItemImplicitTable이 ItemId 단위로 매칭하므로 풀 필터링과 무관).
        /// Prefix/Postfix는 빈 칸 시 LogError. 숫자 문자열도 거부.
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

            if (Enum.TryParse(typeof(GlobalEnum.EquipmentTypeMask), raw, true, out object parsed))
                return (GlobalEnum.EquipmentTypeMask)parsed;

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
