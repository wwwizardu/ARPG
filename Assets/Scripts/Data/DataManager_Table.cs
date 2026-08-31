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
        private ImmutableDictionary<int, Tables.MonsterArchetypeTable> _monsterArchetypeTable = null!;
        // Archetype enum → Table 빠른 조회 캐시 (LoadLate에서 구축)
        private Dictionary<GlobalEnum.MonsterArchetype, Tables.MonsterArchetypeTable> _monsterArchetypeByEnum = new();
        // biome → 그 biome 전용 몬스터 Id 목록 (Any 제외). LoadTableAsync에서 구축. BiomeMonsterMapping이 소비.
        private Dictionary<GlobalEnum.BiomeType, List<int>> _monsterIdsByBiome = new();
        // 공용 몬스터(Biome=Any) Id 목록 — 모든 biome 스폰 풀에 포함.
        private readonly List<int> _commonMonsterIds = new();
        private ImmutableDictionary<int, Tables.NpcTable> _npcTable = null!;
        // NPC 시작 장비 세트 (Option A — 고정 세트). NpcTable.EquipTableId가 참조.
        private ImmutableDictionary<int, Tables.NpcEquipTable> _npcEquipTable = null!;
        private ImmutableDictionary<int, Tables.StatTable> _statTable = null!;
        private ImmutableDictionary<int, Tables.ItemTable> _itemTable = null!;
        private ImmutableDictionary<int, Tables.BuildableItemTable> _buildableItemTable = null!;
        // 일반 장비의 표준 베이스 스탯 (ItemId → Base ModId/Tier 매핑). 장비 자격 판별 기준.
        private ImmutableDictionary<int, Tables.EquipmentBaseStatTable> _equipmentBaseStatTable = null!;
        // ItemId → EquipmentBaseStatTable 빠른 조회용 인덱스
        private Dictionary<int, Tables.EquipmentBaseStatTable> _equipmentBaseStatByItemId = new();
        private ImmutableDictionary<int, Tables.DropTable> _dropTable = null!;
        private ImmutableDictionary<int, Tables.DropCurrencyTable> _dropCurrencyTable = null!;
        private ImmutableDictionary<int, Tables.DropEquipmentTable> _dropWeaponBaseStatTable = null!;
        private ImmutableDictionary<int, Tables.SkillTable> _skillTable = null!;
        private ImmutableDictionary<int, Tables.SkillEffectTable> _skillEffectTable = null!;
        private ImmutableDictionary<int, Tables.SkillBookTable> _skillBookTable = null!;
        private ImmutableDictionary<int, Tables.AiTable> _aiTable = null!;
        private ImmutableDictionary<int, Tables.BuffTable> _buffTable = null!;
        private Dictionary<GlobalEnum.BuffEffectType, Tables.BuffTable> _buffByEffectType = new();
        private ImmutableDictionary<int, Tables.BuffEffectTable> _buffEffectTable = null!;
        private ImmutableDictionary<int, Tables.AnimationTable> _animationTable = null!;
        private ImmutableDictionary<int, Tables.ProjectileTable> _projectileTable = null!;
        private ImmutableDictionary<int, Tables.AreaEffectTable> _areaEffectTable = null!;
        private ImmutableDictionary<int, Tables.ModTable> _modTable = null!;
        private ImmutableDictionary<int, Tables.ModTierTable> _modTierTable = null!;
        private ImmutableDictionary<int, Tables.ItemImplicitTable> _itemImplicitTable = null!;
        private ImmutableDictionary<int, Tables.ItemEnchantmentCostTable> _itemEnchantmentCostTable = null!;
        private Dictionary<GlobalEnum.ItemEnchantmentOperation, Tables.ItemEnchantmentCostTable>
            _itemEnchantmentCostByOperation = new();
        private ImmutableDictionary<int, Tables.VillageTable> _villageTable = null!;
        private ImmutableDictionary<int, Tables.VillageStageTable> _villageStageTable = null!;
        private ImmutableDictionary<int, Tables.JobBonusTable> _jobBonusTable = null!;
        // Phase D: JobType → JobBonusTable 빠른 조회 인덱스 (LoadLate에서 구축)
        private Dictionary<int, Tables.JobBonusTable> _jobBonusByJobType = new();
        private ImmutableDictionary<int, Tables.ZoneTable> _zoneTable = null!;
        // ZoneTable의 Zone 번호(=Id) 오름차순 정렬 캐시 — Cap 조회 시 사용
        private List<int> _zoneTableSortedIds = new();
        // 절차적 역사 시스템 (Phase 1) 카탈로그
        // TODO: HistoryEntityTable / HistoryTemplateTable — bytes 파일 미생성 + 사용처 없음. History 시스템 구현 시 주석 해제.
        // private ImmutableDictionary<int, Tables.HistoryEntityTable> _historyEntityTable = null!;
        // private ImmutableDictionary<int, Tables.HistoryTemplateTable> _historyTemplateTable = null!;

        public async Task LoadTableAsync()
        {
            // 모든 테이블을 병렬로 로드
            await Task.WhenAll(
                LoadTable<Tables.CreatureTable>("CreatureTable.bytes", tables => _creatureTable = tables),
                LoadTable<Tables.MonsterTable>("MonsterTable.bytes", tables => _monsterTable = tables),
                LoadTable<Tables.MonsterArchetypeTable>("MonsterArchetypeTable.bytes", tables => _monsterArchetypeTable = tables),
                LoadTable<Tables.NpcTable>("NpcTable.bytes", tables => _npcTable = tables),
                LoadTable<Tables.NpcEquipTable>("NpcEquipTable.bytes", tables => _npcEquipTable = tables),
                LoadTable<Tables.StatTable>("StatTable.bytes", tables => _statTable = tables),
                LoadTable<Tables.ItemTable>("ItemTable.bytes", tables => _itemTable = tables),
                LoadTable<Tables.BuildableItemTable>("BuildableItemTable.bytes", tables => _buildableItemTable = tables),
                LoadTable<Tables.EquipmentBaseStatTable>("EquipmentBaseStatTable.bytes", tables => _equipmentBaseStatTable = tables),
                LoadTable<Tables.DropTable>("DropTable.bytes", tables => _dropTable = tables),
                LoadTable<Tables.DropCurrencyTable>("DropCurrencyTable.bytes", tables => _dropCurrencyTable = tables),
                LoadTable<Tables.DropEquipmentTable>("DropEquipmentTable.bytes", tables => _dropWeaponBaseStatTable = tables),
                LoadTable<Tables.SkillTable>("SkillTable.bytes", tables => _skillTable = tables),
                LoadTable<Tables.SkillEffectTable>("SkillEffectTable.bytes", tables => _skillEffectTable = tables),
                LoadTable<Tables.SkillBookTable>("SkillBookTable.bytes", tables => _skillBookTable = tables),
                LoadTable<Tables.AiTable>("AiTable.bytes", tables => _aiTable = tables),
                LoadTable<Tables.BuffTable>("BuffTable.bytes", tables => _buffTable = tables),
                LoadTable<Tables.BuffEffectTable>("BuffEffectTable.bytes", tables => _buffEffectTable = tables),
                LoadTable<Tables.AnimationTable>("AnimationTable.bytes", tables => _animationTable = tables),
                LoadTable<Tables.ProjectileTable>("ProjectileTable.bytes", tables => _projectileTable = tables),
                LoadTable<Tables.AreaEffectTable>("AreaEffectTable.bytes", tables => _areaEffectTable = tables),
                LoadTable<Tables.ModTable>("ModTable.bytes", tables => _modTable = tables),
                LoadTable<Tables.ModTierTable>("ModTierTable.bytes", tables => _modTierTable = tables),
                LoadTable<Tables.ItemImplicitTable>("ItemImplicitTable.bytes", tables => _itemImplicitTable = tables),
                LoadTable<Tables.ItemEnchantmentCostTable>("ItemEnchantmentCostTable.bytes", tables => _itemEnchantmentCostTable = tables),
                LoadTable<Tables.VillageTable>("VillageTable.bytes", tables => _villageTable = tables),
                LoadTable<Tables.VillageStageTable>("VillageStageTable.bytes", tables => _villageStageTable = tables),
                LoadTable<Tables.JobBonusTable>("JobBonusTable.bytes", tables => _jobBonusTable = tables),
                LoadTable<Tables.ZoneTable>("ZoneTable.bytes", tables => _zoneTable = tables)
                // TODO: History 시스템 구현 시 주석 해제 + bytes 파일 생성 (ARPG → Download Table 메뉴)
                // , LoadTable<Tables.HistoryEntityTable>("HistoryEntityTable.bytes", tables => _historyEntityTable = tables)
                // , LoadTable<Tables.HistoryTemplateTable>("HistoryTemplateTable.bytes", tables => _historyTemplateTable = tables)
            );

            // 모든 테이블 로드 후 LoadLate 실행
            foreach (var table in _creatureTable.Values)
            {
                table.LoadLate();
            }

            // MonsterTable LoadLate + biome 스폰 풀 인덱스 구축 (공용/biome전용 분리)
            _monsterIdsByBiome.Clear();
            _commonMonsterIds.Clear();
            foreach (var table in _monsterTable.Values)
            {
                table.LoadLate();

                if (table.Biome == GlobalEnum.BiomeType.Any)
                {
                    _commonMonsterIds.Add(table.Id);
                }
                else
                {
                    if (_monsterIdsByBiome.TryGetValue(table.Biome, out var biomeList) == false)
                    {
                        biomeList = new List<int>();
                        _monsterIdsByBiome.Add(table.Biome, biomeList);
                    }
                    biomeList.Add(table.Id);
                }
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

            _buffByEffectType.Clear();
            foreach (var table in _buffTable.Values)
            {
                table.LoadLate();
                if (_buffByEffectType.TryGetValue(table.EffectType, out var existing) == false || table.Id < existing.Id)
                {
                    _buffByEffectType[table.EffectType] = table;
                }
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

            // ItemEnchantmentCostTable -> Currency ItemTable 참조 연결 + Operation 인덱스 구축.
            _itemEnchantmentCostByOperation.Clear();
            List<Tables.ItemEnchantmentCostTable> enchantmentCosts =
                new List<Tables.ItemEnchantmentCostTable>(_itemEnchantmentCostTable.Values);
            enchantmentCosts.Sort((a, b) => a.Id.CompareTo(b.Id));
            HashSet<GlobalEnum.ItemEnchantmentOperation> duplicateOperations =
                new HashSet<GlobalEnum.ItemEnchantmentOperation>();
            for (int i = 0; i < enchantmentCosts.Count; i++)
            {
                Tables.ItemEnchantmentCostTable table = enchantmentCosts[i];
                table.LoadLate();
                if (table.IsValid == false)
                    continue;
                if (duplicateOperations.Contains(table.Operation) == true)
                    continue;
                if (_itemEnchantmentCostByOperation.TryGetValue(table.Operation,
                        out Tables.ItemEnchantmentCostTable existing) == true)
                {
                    Debug.LogError($"[DataManager_Table] Duplicate ItemEnchantmentCost operation: " +
                        $"{table.Operation} (row Ids={existing.Id},{table.Id}). " +
                        "모든 중복 행을 비활성화합니다.");
                    _itemEnchantmentCostByOperation.Remove(table.Operation);
                    duplicateOperations.Add(table.Operation);
                    continue;
                }
                _itemEnchantmentCostByOperation.Add(table.Operation, table);
            }

            // EquipmentBaseStatTable → ModTable/ModTierTable 참조 연결 + ItemId 인덱스 구축
            _equipmentBaseStatByItemId.Clear();
            foreach (var table in _equipmentBaseStatTable.Values)
            {
                table.LoadLate();
                if (table.ItemId > 0 && _equipmentBaseStatByItemId.ContainsKey(table.ItemId) == false)
                    _equipmentBaseStatByItemId.Add(table.ItemId, table);
            }

            foreach (var table in _villageTable.Values)
            {
                table.LoadLate();
            }

            // Phase D: JobBonusTable LoadLate + JobType 인덱스 구축
            _jobBonusByJobType.Clear();
            foreach (var table in _jobBonusTable.Values)
            {
                table.LoadLate();
                if (table.JobType > 0 && _jobBonusByJobType.ContainsKey(table.JobType) == false)
                    _jobBonusByJobType.Add(table.JobType, table);
            }

            // MonsterArchetypeTable: Archetype enum 인덱스 구축
            _monsterArchetypeByEnum.Clear();
            foreach (var table in _monsterArchetypeTable.Values)
            {
                table.LoadLate();
                if (_monsterArchetypeByEnum.ContainsKey(table.Archetype) == false)
                    _monsterArchetypeByEnum.Add(table.Archetype, table);
            }

            // ZoneTable: Cap 조회용 정렬 인덱스 구축 (Zone 번호=Id 오름차순)
            _zoneTableSortedIds.Clear();
            foreach (var key in _zoneTable.Keys)
                _zoneTableSortedIds.Add(key);
            _zoneTableSortedIds.Sort();
            foreach (var table in _zoneTable.Values)
                table.LoadLate();

            Debug.Log($"<color=yellow>[ZoneTable] Loaded rows={_zoneTable.Count} ids=[{string.Join(",", _zoneTableSortedIds)}]</color>");

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

        /// <summary>
        /// 공용 몬스터(Biome=Any) Id 목록. 모든 biome 스폰 풀에 포함된다. (읽기 전용, 캐시된 참조)
        /// </summary>
        public IReadOnlyList<int> GetCommonMonsterIds()
        {
            return _commonMonsterIds;
        }

        /// <summary>
        /// 특정 biome 전용 몬스터 Id 목록. 해당 biome 몬스터가 없으면 빈 목록. (읽기 전용, 캐시된 참조)
        /// </summary>
        public IReadOnlyList<int> GetBiomeSpecificMonsterIds(GlobalEnum.BiomeType biome)
        {
            if (_monsterIdsByBiome.TryGetValue(biome, out var list))
            {
                return list;
            }

            return System.Array.Empty<int>();
        }

        /// <summary>
        /// MonsterArchetype enum으로 해당 등급 테이블 조회.
        /// </summary>
        public Tables.MonsterArchetypeTable? GetMonsterArchetype(GlobalEnum.MonsterArchetype archetype)
        {
            if (_monsterArchetypeByEnum.TryGetValue(archetype, out var table))
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

        public Tables.NpcEquipTable? GetNpcEquipTable(int id)
        {
            if (_npcEquipTable.TryGetValue(id, out var table))
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

        /// <summary>
        /// 전체 아이템 테이블 목록 반환 (드롭 풀 빌드용)
        /// </summary>
        public List<Tables.ItemTable> GetAllItems()
        {
            return new List<Tables.ItemTable>(_itemTable.Values);
        }

        public Tables.BuildableItemTable? GetBuildableItem(int id)
        {
            if (_buildableItemTable.TryGetValue(id, out var table))
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

        /// <summary>
        /// 전체 스킬 테이블 목록 반환 (스킬북 풀 빌드 등에 사용)
        /// </summary>
        public List<Tables.SkillTable> GetAllSkills()
        {
            return new List<Tables.SkillTable>(_skillTable.Values);
        }

        public Tables.AiTable? GetAiTable(int id)
        {
            if (_aiTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        public Tables.VillageTable? GetVillageTable(int id)
        {
            if (_villageTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// preferredId에 해당하는 VillageTable 행이 있으면 그대로, 없으면 존재하는 가장 작은 Id로 폴백한다.
        /// 마을 인스턴스 수 > 테이블 행 수일 때, 위치 관례(i+1)가 가리키는 빈 행을 유효 행으로 라우팅하기 위한 것.
        /// 행이 하나도 없으면 -1 (호출자는 후보에서 제외됨).
        /// </summary>
        public int ResolveVillageTableId(int preferredId)
        {
            if (_villageTable.ContainsKey(preferredId))
                return preferredId;

            int fallback = -1;
            foreach (int id in _villageTable.Keys)
            {
                if (fallback < 0 || id < fallback)
                    fallback = id;
            }
            return fallback;
        }

        /// <summary>
        /// 마을 단계 파라미터 조회. 미로딩/누락 시 null — 호출자는 명시적 에러 처리 필요.
        /// </summary>
        public Tables.VillageStageTable? GetVillageStage(VillageStage stage)
        {
            if (_villageStageTable != null && _villageStageTable.TryGetValue((int)stage, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// Phase D: 직업별 시간당 가산 자원 조회. JobType 정수 또는 enum 모두 허용.
        /// </summary>
        public Tables.JobBonusTable? GetJobBonusByJobType(GlobalEnum.JobType jobType)
        {
            if (_jobBonusByJobType.TryGetValue((int)jobType, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// Zone 번호 → ZoneTable 행. Cap 방식: zone 이하 최대 정의 Id의 행을 반환.
        /// 시트에 일부 Zone(예: 1,2,5,10,30)만 입력하면 사이 Zone은 이전 행을 그대로 사용.
        /// Zone 1 행이 비어 있으면 null 반환 — 호출자는 인스펙터 fallback을 사용해야 함.
        /// </summary>
        public Tables.ZoneTable? GetZone(int zone)
        {
            if (_zoneTableSortedIds.Count == 0)
                return null;

            // zone 이하 가장 큰 정의 Id 탐색 (이진 탐색, 오름차순 정렬 기준)
            int lo = 0;
            int hi = _zoneTableSortedIds.Count - 1;
            int best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int id = _zoneTableSortedIds[mid];
                if (id <= zone)
                {
                    best = id;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            if (best < 0)
                return null;

            return _zoneTable.TryGetValue(best, out var table) ? table : null;
        }

        // TODO: History 시스템 구현 시 주석 해제 (필드/LoadTable 같이 복구)
        // public Tables.HistoryEntityTable? GetHistoryEntity(int id)
        // {
        //     if (_historyEntityTable.TryGetValue(id, out var table))
        //     {
        //         return table;
        //     }
        //     return null;
        // }
        //
        // public List<Tables.HistoryEntityTable> GetAllHistoryEntities()
        // {
        //     return new List<Tables.HistoryEntityTable>(_historyEntityTable.Values);
        // }
        //
        // public Tables.HistoryTemplateTable? GetHistoryTemplate(int id)
        // {
        //     if (_historyTemplateTable.TryGetValue(id, out var table))
        //     {
        //         return table;
        //     }
        //     return null;
        // }
        //
        // public List<Tables.HistoryTemplateTable> GetAllHistoryTemplates()
        // {
        //     return new List<Tables.HistoryTemplateTable>(_historyTemplateTable.Values);
        // }

        public Tables.SkillEffectTable? GetSkillEffect(int id)
        {
            if (_skillEffectTable.TryGetValue(id, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// 전체 SkillEffect 목록 반환. 스킬 페이지 풀 생성 등에 사용.
        /// </summary>
        public List<Tables.SkillEffectTable> GetAllSkillEffects()
        {
            return new List<Tables.SkillEffectTable>(_skillEffectTable.Values);
        }

        public Tables.BuffTable? GetBuffByEffectType(GlobalEnum.BuffEffectType effectType)
        {
            if (_buffByEffectType.TryGetValue(effectType, out var table))
            {
                return table;
            }

            return null;
        }

        /// <summary>
        /// 스킬북 등급별 룰(페이지 용량/슬롯) 조회. 테이블 누락 시 설계 초안 기본값으로 fallback.
        /// </summary>
        public Tables.SkillBookTable? GetSkillBook(int tier)
        {
            if (_skillBookTable != null && _skillBookTable.TryGetValue(tier, out var table))
            {
                return table;
            }

            return tier switch
            {
                1 => new Tables.SkillBookTable { Id = 1, PageCapacity = 8, PageSlots = 1 },
                2 => new Tables.SkillBookTable { Id = 2, PageCapacity = 24, PageSlots = 3 },
                3 => new Tables.SkillBookTable { Id = 3, PageCapacity = 60, PageSlots = 5 },
                _ => null,
            };
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

        public Tables.AreaEffectTable? GetAreaEffect(int id)
        {
            if (_areaEffectTable.TryGetValue(id, out var table))
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
        /// 특정 아이템의 베이스 스탯 테이블 행 반환. 없으면 null (=장비 아님).
        /// </summary>
        public Tables.EquipmentBaseStatTable? GetEquipmentBaseStat(int itemId)
        {
            if (_equipmentBaseStatByItemId.TryGetValue(itemId, out var table))
                return table;
            return null;
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
        public List<Tables.ModTable> GetModPool(GlobalEnum.ModSlot slot, GlobalEnum.EquipmentTypeMask equipType)
        {
            List<Tables.ModTable> result = new();
            if (Utils.IsSingleEquipmentTypeMask(equipType) == false)
            {
                UnityEngine.Debug.LogError($"[DataManager_Table] GetModPool - EquipType must be a single bit. Got: {equipType}");
                return result;
            }

            foreach (var table in _modTable.Values)
            {
                if (table.Slot != slot)
                    continue;
                if (Utils.ContainsEquipmentType(table.AllowedEquipTypes, equipType) == false)
                    continue;
                result.Add(table);
            }
            return result;
        }

        /// <summary>
        /// 요청한 랜덤 마법부여 작업의 유효한 비용 규칙을 반환한다.
        /// 잘못된 행과 중복 Operation은 로드 진단에서 제외된다.
        /// </summary>
        public Tables.ItemEnchantmentCostTable? GetItemEnchantmentCost(
            GlobalEnum.ItemEnchantmentOperation operation)
        {
            if (_itemEnchantmentCostByOperation.TryGetValue(operation, out var table) == true)
                return table;
            return null;
        }

        public List<Tables.ItemEnchantmentCostTable> GetAllItemEnchantmentCosts()
        {
            List<Tables.ItemEnchantmentCostTable> result =
                new List<Tables.ItemEnchantmentCostTable>(_itemEnchantmentCostTable.Values);
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
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
                    var tableList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<T>>(json, JsonSettings.Default);
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


