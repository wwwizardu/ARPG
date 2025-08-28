#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using UnityEngine;

namespace ARPG.Data
{

    public class DataManager : MonoBehaviour
    {
        private ImmutableDictionary<int, Tables.CreatureTable> _creatureTable = null!;

        public void Initialize()
        {
            // 데이터 초기화 로직
            Debug.Log("DataManager Initialized");
        }

        public void Reset()
        {
            // 데이터 리셋 로직
            Debug.Log("DataManager Reset");
        }

        public async Task LoadTableAsync()
        {
            await LoadCreatureTable();

            Debug.Log("Data Tables Loaded");
        }

        public Tables.CreatureTable? GetCreature(int id)
        {
            if (_creatureTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        private async Task LoadCreatureTable()
        {
            // 크리처 테이블 로드 로직
            Dictionary<int, Tables.CreatureTable> creatureTables = new Dictionary<int, Tables.CreatureTable>();

            string path = System.IO.Path.Combine(Application.dataPath, "[TableData]/Creature.bytes");

            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = await System.IO.File.ReadAllTextAsync(path);
                    var tableList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Tables.CreatureTable>>(json);
                    if (tableList != null)
                    {
                        for (int i = 0; i < tableList.Count; i++)
                        {
                            var table = tableList[i];
                            if (creatureTables.ContainsKey(table.Id) == true)
                            {
                                Debug.LogWarning($"Duplicate CreatureTable Id found: {table.Id}. Skipping entry.");
                                continue;
                            }

                            creatureTables.Add(table.Id, table);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"CreatureTable.json not found at path: {path}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error loading CreatureTable: {ex.Message}");
            }

            _creatureTable = creatureTables.ToImmutableDictionary();
        }

    }
}
