using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

using ARPG.Tables;
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

            await DownloadTable<CreatureTable>("0&range=A:F", 1, SaveType.String);

            await DownloadTable<AiTable>("947794841&range=A:H", 1, SaveType.String);

            await DownloadTable<MonsterTable>("483012127&range=A:K", 1, SaveType.String);

            await DownloadTable<NpcTable>("1460299278&range=A:L", 1, SaveType.String);

            await DownloadTable<StatTable>("318209064&range=A:AF", 1, SaveType.String);

            await DownloadTable<ItemTable>("2064107837&range=A:M", 1, SaveType.String);

            await DownloadTable<BuildableItemTable>("534887250&range=A:K", 1, SaveType.String);           

            await DownloadTable<EquipmentBaseStatTable>("972309111&range=A:L", 1, SaveType.String);           
            
            await DownloadTable<WeaponBaseStatTable>("853198133&range=A:H", 1, SaveType.String);


            await DownloadTable<EquipmentStatTable>("488047668&range=A:R", 1, SaveType.String);
            
            await DownloadTable<DropTable>("1241586373&range=A:J", 1, SaveType.String);

            await DownloadTable<DropCurrencyTable>("2071520432&range=A:V", 1, SaveType.String);

            await DownloadTable<DropEquipmentTable>("1267382287&range=A:V", 1, SaveType.String);

            await DownloadTable<SkillTable>("92727160&range=A:W", 1, SaveType.String);

            await DownloadTable<BuffTable>("127577579&range=A:J", 1, SaveType.String);

            await DownloadTable<AnimationTable>("747631090&range=A:E", 1, SaveType.String);

            await DownloadTable<ProjectileTable>("1810235418&range=A:G", 1, SaveType.String);

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
                else if (table is EquipmentBaseStatTable equipmentBaseStatTable)
                {
                    ParseEquipmentBaseStatTable(equipmentBaseStatTable, values);
                }
                else if (table is WeaponBaseStatTable weaponBaseStatTable)
                {
                    ParseWeaponBaseStatTable(weaponBaseStatTable, values);
                }
                else if (table is EquipmentStatTable equipmentStatTable)
                {
                    ParseEquipmentStatTable(equipmentStatTable, values);
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
            if (values.Length < 5)
            {
                Debug.LogError($"[ParseCreatureTable] Invalid data length. Expected at least 5, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.PrefabName = values[4];
            table.AnimationId = int.Parse(values[5]);
        }

        private static void ParseNpcTable(NpcTable table, string[] values)
        {
            if (values.Length < 12)
            {
                Debug.LogError($"[ParseNpcTable] Invalid data length. Expected at least 12, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.PrefabName = values[4];
            table.AnimationId = int.Parse(values[5]);
            table.JobType = (GlobalEnum.JobType)Enum.Parse(typeof(GlobalEnum.JobType), values[6]);
            table.WeaponId = int.Parse(values[7]);
            table.AiTableId = int.Parse(values[8]);
            table.DropId = int.Parse(values[9]);
            table.DropRateBonus = int.Parse(values[10]);
            table.DropRarityBonus = int.Parse(values[11]);
        }

        private static void ParseStatTable(StatTable table, string[] values)
        {
            if (values.Length < 32)
            {
                Debug.LogError($"[ParseStatTable] Invalid data length. Expected at least 32, got {values.Length}. Id: {table.Id}");
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
            table.Luck = int.Parse(values[22]);
            table.BloodingRate = int.Parse(values[23]);
            table.IgniteRate = int.Parse(values[24]);

            // 전투 시스템 확장 스탯 (2026-04-01 추가)
            table.Evasion = int.Parse(values[25]);
            table.BlockChance = int.Parse(values[26]);
            table.BlockReduction = int.Parse(values[27]);
            table.SkillDamage = int.Parse(values[28]);
            table.CooldownReduction = int.Parse(values[29]);
            table.LifeSteal = int.Parse(values[30]);
            table.Thorns = int.Parse(values[31]);
        }

        private static void ParseMonsterTable(MonsterTable table, string[] values)
        {
            if (values.Length < 9)
            {
                Debug.LogError($"[ParseMonsterTable] Invalid data length. Expected at least 9, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            // values[2]는 웹에서만 사용한다.
            table.StatId = int.Parse(values[3]);
            table.PrefabName = values[4];
            table.AnimationId = int.Parse(values[5]);
            table.WeaponId = int.Parse(values[6]);
            table.AiTableId = int.Parse(values[7]);
            table.DropId = int.Parse(values[8]);
            table.DropRateBonus = int.Parse(values[9]);
            table.DropRarityBonus = int.Parse(values[10]);
        }

        private static void ParseItemTable(ItemTable table, string[] values)
        {
            if (values.Length < 13)
            {
                Debug.LogError($"[ParseItemTable] Invalid data length. Expected at least 13, got {values.Length}. Id: {table.Id}");
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
        }

        private static void ParseBuildableItemTable(BuildableItemTable table, string[] values)
        {
            // 전체 범위: A:K = 11개 컬럼
            if (values.Length < 11)
            {
                Debug.LogError($"[ParseBuildableItemTable] Invalid data length. Expected at least 11, got {values.Length}. Id: {table.Id}");
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
            table.Function = int.Parse(values[9]);
            table.ResourceName = values[10];
        }

        private static void ParseEquipmentBaseStatTable(EquipmentBaseStatTable table, string[] values)
        {
            // 컬럼 구조: Id, Name, Description, Type1, Value1, Type2, Value2, Type3, Value3, Type4, Value4
            table.Stats.Clear();

            for (int i = 3; i + 1 < values.Length; i += 2)
            {
                if (string.IsNullOrEmpty(values[i]) == true)
                    break;

                GlobalEnum.Stat statType = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[i]);
                ushort statValue = ushort.Parse(values[i + 1]);

                table.Stats.Add(new Stat() { Type = statType, Value = statValue });
            }
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


        private static void ParseEquipmentStatTable(EquipmentStatTable table, string[] values)
        {
            if (values.Length < 17)
            {
                Debug.LogError($"[ParseEquipmentStatTable] Invalid data length. Expected at least 17, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Prefix = new List<Stat>();
            table.Postfix = new List<Stat>();

            // 컬럼 구조: Id, Name, Description, Type1, Value1, ... Type4, Value4, Type5, Value5, ... Type8, Value8
            // Prefix 시작 인덱스 (Name, Description 다음)
            int prefixStartIndex = 3;
            for (int i = 0; i < 4; i++)
            {
                int index = prefixStartIndex + (i * 2);
                if (index + 1 >= values.Length || string.IsNullOrEmpty(values[index]) == true)
                    break;

                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = ushort.Parse(values[index + 1]);
                table.Prefix.Add(stat);
            }

            // Postfix 시작 인덱스 (Prefix 4개 스탯 이후)
            int postfixStartIndex = prefixStartIndex + 8;
            for (int i = 0; i < 4; i++)
            {
                int index = postfixStartIndex + (i * 2);
                if (index + 1 >= values.Length || string.IsNullOrEmpty(values[index]) == true)
                    break;

                Stat stat = new Stat();
                stat.Type = (GlobalEnum.Stat)Enum.Parse(typeof(GlobalEnum.Stat), values[index]);
                stat.Value = ushort.Parse(values[index + 1]);
                table.Postfix.Add(stat);
            }
        }

        private static void ParseDropTable(DropTable table, string[] values)
        {
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseDropTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Tier = int.Parse(values[1]);
            table.NothingRate = int.Parse(values[2]);
            table.CurrencyRate = int.Parse(values[3]);
            table.CurrencyId = int.Parse(values[4]);
            table.EquipmentRate = int.Parse(values[5]);
            table.EquipmentId = int.Parse(values[6]);
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
            if (values.Length < 23)
            {
                Debug.LogError($"[ParseSkillTable] Invalid data length. Expected at least 23, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.Desctiption = values[2];
            table.Tags = ParseSkillTags(values[3]);
            table.SkillType = (GlobalEnum.SkillType)Enum.Parse(typeof(GlobalEnum.SkillType), values[4]);
            table.SubType = (GlobalEnum.SkillSubType)Enum.Parse(typeof(GlobalEnum.SkillSubType), values[5]);
            table.SkillRangeMin = float.Parse(values[6]);
            table.SkillRangeMax = float.Parse(values[7]);
            table.Cooltime = float.Parse(values[8]);
            table.Mana = int.Parse(values[9]);
            table.DamageTime = float.Parse(values[10]);
            table.DamageType = (GlobalEnum.DamageType)Enum.Parse(typeof(GlobalEnum.DamageType), values[11]);
            table.DamageMin = int.Parse(values[12]);
            table.DamageMax = int.Parse(values[13]);
            table.Duration = int.Parse(values[14]);
            table.SkillTargetType = (GlobalEnum.SkillTargetType)Enum.Parse(typeof(GlobalEnum.SkillTargetType), values[15]);
            table.SkillTargetRange1 = float.Parse(values[16]);
            table.SkillTargetRange2 = float.Parse(values[17]);
            table.AnimationName = values[18];
            table.StartEffectName = values[19];
            table.ActivateName = values[20];
            table.HitEffect = values[21];
            table.ProjectileId = int.Parse(values[22]);
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
            if (values.Length < 7)
            {
                Debug.LogError($"[ParseAiTable] Invalid data length. Expected at least 7, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.AiType = (GlobalEnum.AiType)Enum.Parse(typeof(GlobalEnum.AiType), values[2]);
            table.BehaviorType = (ARPG.Component.AIBehaviorType)Enum.Parse(typeof(ARPG.Component.AIBehaviorType), values[3]);
            table.DetectionRange = float.Parse(values[4]);
            table.SkillId1 = int.Parse(values[5]);
            table.SkillId2 = int.Parse(values[6]);
            table.SkillId3 = int.Parse(values[7]);
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
            // 전체 범위: A:E = 5개 컬럼
            if (values.Length < 5)
            {
                Debug.LogError($"[ParseAnimationTable] Invalid data length. Expected at least 5, got {values.Length}. Id: {table.Id}");
                return;
            }

            table.Name = values[1];
            table.SpriteLibraryPath = values[2];
            table.AnimClipPath = values[3];
            table.ClipNames = values[4];
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

            string result = JsonConvert.SerializeObject(inData);
            
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
