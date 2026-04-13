#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using UnityEngine;
using ARPG.Tables;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        public static string TablePath = "_BinaryData/TableData/";

        private ImmutableDictionary<int, Tables.CreatureTable> _creatureTable = null!;
        private ImmutableDictionary<int, Tables.MonsterTable> _monsterTable = null!;
        private ImmutableDictionary<int, Tables.NpcTable> _npcTable = null!;
        private ImmutableDictionary<int, Tables.StatTable> _statTable = null!;
        private ImmutableDictionary<int, Tables.ItemTable> _itemTable = null!;
        private ImmutableDictionary<int, Tables.BuildableItemTable> _buildableItemTable = null!;
        // EquipmentBaseStatTable 제거됨 → ModTable + ItemImplicitTable로 대체
        private ImmutableDictionary<int, Tables.WeaponBaseStatTable> _equipmentTable = null!;
        // EquipmentStatTable 제거됨 → ModTable (Prefix/Postfix)로 대체
        private ImmutableDictionary<int, Tables.DropTable> _dropTable = null!;
        private ImmutableDictionary<int, Tables.DropCurrencyTable> _dropCurrencyTable = null!;
        private ImmutableDictionary<int, Tables.DropEquipmentTable> _dropWeaponBaseStatTable = null!;
        private ImmutableDictionary<int, Tables.SkillTable> _skillTable = null!;
        private ImmutableDictionary<int, Tables.AiTable> _aiTable = null!;
        private ImmutableDictionary<int, Tables.BuffTable> _buffTable = null!;
        private ImmutableDictionary<int, Tables.BuffEffectTable> _buffEffectTable = null!;
        private ImmutableDictionary<int, Tables.AnimationTable> _animationTable = null!;
        private ImmutableDictionary<int, Tables.ProjectileTable> _projectileTable = null!;
        private ImmutableDictionary<int, Tables.ModTable> _modTable = null!;
        private ImmutableDictionary<int, Tables.ModTierTable> _modTierTable = null!;
        private ImmutableDictionary<int, Tables.ItemImplicitTable> _itemImplicitTable = null!;

        public async Task LoadTableAsync()
        {
            // 모든 테이블을 병렬로 로드
            await Task.WhenAll(
                LoadTable<Tables.CreatureTable>("CreatureTable.bytes", tables => _creatureTable = tables),
                LoadTable<Tables.MonsterTable>("MonsterTable.bytes", tables => _monsterTable = tables),
                LoadTable<Tables.NpcTable>("NpcTable.bytes", tables => _npcTable = tables),
                LoadTable<Tables.StatTable>("StatTable.bytes", tables => _statTable = tables),
                LoadTable<Tables.ItemTable>("ItemTable.bytes", tables => _itemTable = tables),
                LoadTable<Tables.BuildableItemTable>("BuildableItemTable.bytes", tables => _buildableItemTable = tables),
                LoadTable<Tables.WeaponBaseStatTable>("WeaponBaseStatTable.bytes", tables => _equipmentTable = tables),
                LoadTable<Tables.DropTable>("DropTable.bytes", tables => _dropTable = tables),
                LoadTable<Tables.DropCurrencyTable>("DropCurrencyTable.bytes", tables => _dropCurrencyTable = tables),
                LoadTable<Tables.DropEquipmentTable>("DropEquipmentTable.bytes", tables => _dropWeaponBaseStatTable = tables),
                LoadTable<Tables.SkillTable>("SkillTable.bytes", tables => _skillTable = tables),
                LoadTable<Tables.AiTable>("AiTable.bytes", tables => _aiTable = tables),
                LoadTable<Tables.BuffTable>("BuffTable.bytes", tables => _buffTable = tables),
                LoadTable<Tables.BuffEffectTable>("BuffEffectTable.bytes", tables => _buffEffectTable = tables),
                LoadTable<Tables.AnimationTable>("AnimationTable.bytes", tables => _animationTable = tables),
                LoadTable<Tables.ProjectileTable>("ProjectileTable.bytes", tables => _projectileTable = tables),
                LoadTable<Tables.ModTable>("ModTable.bytes", tables => _modTable = tables),
                LoadTable<Tables.ModTierTable>("ModTierTable.bytes", tables => _modTierTable = tables),
                LoadTable<Tables.ItemImplicitTable>("ItemImplicitTable.bytes", tables => _itemImplicitTable = tables)
            );

            // 모든 테이블 로드 후 LoadLate 실행
            foreach (var table in _creatureTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _monsterTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _npcTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _statTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _itemTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _equipmentTable.Values)
            {
                table.LoadLate();
            }


            foreach (var table in _dropTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _dropCurrencyTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _dropWeaponBaseStatTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _skillTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _aiTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _buffTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _buffEffectTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _animationTable.Values)
            {
                table.LoadLate();
            }

            foreach (var table in _projectileTable.Values)
            {
                table.LoadLate();
            }

            // ModTierTable → ModTable 참조 연결
            foreach (var table in _modTierTable.Values)
            {
                table.LoadLate();
            }

            // ItemImplicitTable → ModTable/ModTierTable 참조 연결
            foreach (var table in _itemImplicitTable.Values)
            {
                table.LoadLate();
            }

            Debug.Log("Data Tables Loaded");
        }

        public Tables.CreatureTable? GetPlayer(int id)
        {
            if (_creatureTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.MonsterTable? GetMonster(int id)
        {
            if (_monsterTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.NpcTable? GetNpc(int id)
        {
            if (_npcTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.StatTable? GetStat(int id)
        {
            if (_statTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.ItemTable? GetItem(int id)
        {
            if (_itemTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.BuildableItemTable? GetBuildableItem(int id)
        {
            if (_buildableItemTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        // GetEquipmentBaseStat 제거됨 → GetMod + GetItemImplicits로 대체

        public Tables.WeaponBaseStatTable? WeaponBaseStatTable(int id)
        {
            if (_equipmentTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }


        // GetEquipmentStat 제거됨 → GetMod + GetModTiers로 대체

        public Tables.DropTable? GetDrop(int id)
        {
            if (_dropTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.DropCurrencyTable? GetDropCurrency(int id)
        {
            if (_dropCurrencyTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.DropEquipmentTable? GetDropEquipment(int id)
        {
            if (_dropWeaponBaseStatTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.SkillTable? GetSkill(int id)
        {
            if (_skillTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.AiTable? GetAiTable(int id)
        {
            if (_aiTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.BuffTable? GetBuff(int id)
        {
            if (_buffTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.BuffEffectTable? GetBuffEffect(int id)
        {
            if (_buffEffectTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.AnimationTable? GetAnimation(int id)
        {
            if (_animationTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.ProjectileTable? GetProjectile(int id)
        {
            if (_projectileTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.ModTable? GetMod(int id)
        {
            if (_modTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// ModId + Tier 조합으로 ModTierTable 조회
        /// </summary>
        public Tables.ModTierTable? GetModTier(int modId, int tier)
        {
            foreach (var table in _modTierTable.Values)
            {
                if (table.ModId == modId && table.Tier == tier)
                    return table;
            }

            return null;
        }

        /// <summary>
        /// 특정 ModId의 모든 티어 목록 반환
        /// </summary>
        public List<Tables.ModTierTable> GetModTiers(int modId)
        {
            List<Tables.ModTierTable> result = new();
            foreach (var table in _modTierTable.Values)
            {
                if (table.ModId == modId)
                    result.Add(table);
            }
            return result;
        }

        /// <summary>
        /// 특정 아이템의 Implicit Mod 목록 반환
        /// </summary>
        public List<Tables.ItemImplicitTable> GetItemImplicits(int itemId)
        {
            List<Tables.ItemImplicitTable> result = new();
            foreach (var table in _itemImplicitTable.Values)
            {
                if (table.ItemId == itemId)
                    result.Add(table);
            }
            return result;
        }

        /// <summary>
        /// 특정 슬롯/조건에 맞는 Mod 풀 반환 (랜덤 롤링용)
        /// </summary>
        public List<Tables.ModTable> GetModPool(GlobalEnum.ModSlot slot)
        {
            List<Tables.ModTable> result = new();
            foreach (var table in _modTable.Values)
            {
                if (table.Slot == slot)
                    result.Add(table);
            }
            return result;
        }

        private async Task LoadTable<T>(string fileName, System.Action<ImmutableDictionary<int, T>> setTable) where T : Tables.TableBase
        {
            Dictionary<int, T> tables = new Dictionary<int, T>();

            string path = System.IO.Path.Combine(Application.dataPath, $"{TablePath}{fileName}");

            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = await System.IO.File.ReadAllTextAsync(path);
                    var tableList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<T>>(json);
                    if (tableList != null)
                    {
                        for (int i = 0; i < tableList.Count; i++)
                        {
                            var table = tableList[i];
                            if (tables.ContainsKey(table.Id) == true)
                            {
                                Debug.LogWarning($"Duplicate {typeof(T).Name} Id found: {table.Id}. Skipping entry.");
                                continue;
                            }

                            tables.Add(table.Id, table);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"{fileName} not found at path: {path}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error loading {typeof(T).Name}: {ex.Message}");
            }

            setTable(tables.ToImmutableDictionary());
        }
    }
}


