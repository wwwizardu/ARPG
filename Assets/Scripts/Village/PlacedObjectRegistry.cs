#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: 활성 청크의 PlacedObject 엔티티에 대한 빠른 조회 인덱스.
    /// 마을별로 두 인덱스 보관:
    ///   - tableId → entityIds (HasObjectSet의 마을 전체 검사, MaxPerVillage 카운트 등)
    ///   - tileXY  → entityId  (특정 좌표의 PlacedObject 조회)
    ///
    /// 이 레지스트리는 **휘발성** — 세이브에는 들어가지 않는다.
    /// 정본은 VillageData.PlacedObjects (좌표/HP 등). 로드 시 VillageManager가 정본을 순회하며 재구성.
    /// </summary>
    public static class PlacedObjectRegistry
    {
        private class VillageIndex
        {
            public readonly Dictionary<int, List<int>> ByTable = new();          // tableId → entityIds
            public readonly Dictionary<Vector2Int, int> ByTile = new();          // tile → entityId
        }

        private static readonly Dictionary<int, VillageIndex> _byVillage = new();

        public static void Register(int villageId, int entityId, int tableId, Vector2Int tile)
        {
            if (_byVillage.TryGetValue(villageId, out var index) == false)
            {
                index = new VillageIndex();
                _byVillage[villageId] = index;
            }

            // tableId 인덱스
            if (index.ByTable.TryGetValue(tableId, out var list) == false)
            {
                list = new List<int>();
                index.ByTable[tableId] = list;
            }
            if (list.Contains(entityId) == false)
                list.Add(entityId);

            // tile 인덱스 (덮어쓰기 — 한 타일에 여러 오브젝트는 없음)
            index.ByTile[tile] = entityId;
        }

        public static void Unregister(int villageId, int entityId)
        {
            if (_byVillage.TryGetValue(villageId, out var index) == false) return;

            // tableId 인덱스에서 제거
            int? matchedTableId = null;
            foreach (var kv in index.ByTable)
            {
                if (kv.Value.Remove(entityId))
                {
                    matchedTableId = kv.Key;
                    break;
                }
            }
            if (matchedTableId.HasValue && index.ByTable[matchedTableId.Value].Count == 0)
                index.ByTable.Remove(matchedTableId.Value);

            // tile 인덱스에서 제거
            Vector2Int? matchedTile = null;
            foreach (var kv in index.ByTile)
            {
                if (kv.Value == entityId)
                {
                    matchedTile = kv.Key;
                    break;
                }
            }
            if (matchedTile.HasValue)
                index.ByTile.Remove(matchedTile.Value);
        }

        /// <summary>
        /// 마을 내 특정 TableId의 엔티티 목록. 없으면 null.
        /// </summary>
        public static List<int>? GetEntitiesByTableId(int villageId, int tableId)
        {
            if (_byVillage.TryGetValue(villageId, out var index) == false) return null;
            return index.ByTable.TryGetValue(tableId, out var list) ? list : null;
        }

        /// <summary>
        /// 특정 좌표의 PlacedObject 엔티티 ID. 없으면 -1.
        /// </summary>
        public static int GetEntityAtTile(int villageId, Vector2Int tile)
        {
            if (_byVillage.TryGetValue(villageId, out var index) == false) return -1;
            return index.ByTile.TryGetValue(tile, out int entityId) ? entityId : -1;
        }

        /// <summary>
        /// 마을 내 모든 PlacedObject 엔티티 (Range 0 = 마을 전체 검사 시 사용).
        /// </summary>
        public static List<int> GetAllEntitiesInVillage(int villageId)
        {
            var result = new List<int>();
            if (_byVillage.TryGetValue(villageId, out var index) == false) return result;

            foreach (var list in index.ByTable.Values)
            {
                for (int i = 0; i < list.Count; i++)
                    result.Add(list[i]);
            }
            return result;
        }

        /// <summary>
        /// Bounds 내 PlacedObject 엔티티 (HasObjectSet 5×5 검사, ServiceProximity 등).
        /// 단순 Bounds.Contains 필터 — 마을 오브젝트 수가 적어 O(N) 무관.
        /// </summary>
        public static List<int> GetAllEntitiesInBounds(int villageId, RectInt bounds)
        {
            var result = new List<int>();
            if (_byVillage.TryGetValue(villageId, out var index) == false) return result;

            foreach (var kv in index.ByTile)
            {
                if (bounds.Contains(kv.Key))
                    result.Add(kv.Value);
            }
            return result;
        }

        /// <summary>
        /// 마을의 전체 인덱스 비우기 (Reset 시 호출).
        /// </summary>
        public static void Clear(int villageId)
        {
            _byVillage.Remove(villageId);
        }

        /// <summary>
        /// 모든 마을 인덱스 비우기 (전체 Reset).
        /// </summary>
        public static void ClearAll()
        {
            _byVillage.Clear();
        }
    }
}
