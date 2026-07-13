#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// VillageData의 진행 공사와 완공 오브젝트 원장을 조회·갱신한다.
    /// ECS 엔티티 존재 여부와 무관하게 같은 결과를 반환한다.
    /// </summary>
    public static class VillageConstructionLedger
    {
        public static int CountPlaced(VillageData village, int tableId)
        {
            int count = 0;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                if (village.PlacedObjectTypeIds[i] == tableId)
                    count++;
            }
            return count;
        }

        public static int CountInProgress(VillageData village, int tableId)
        {
            int count = 0;
            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                if (village.ActiveBuildTasks[i].TableId == tableId)
                    count++;
            }
            return count;
        }

        public static int CountByService(VillageData village, ProvidedService service)
        {
            int count = 0;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(village.PlacedObjectTypeIds[i]);
                if (table != null && ((ProvidedService)table.ProvidedService & service) != 0)
                    count++;
            }
            return count;
        }

        public static SetMemberTag AggregateSetMembers(VillageData village)
        {
            SetMemberTag covered = SetMemberTag.None;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(village.PlacedObjectTypeIds[i]);
                if (table != null)
                    covered |= (SetMemberTag)table.SetMembership;
            }
            return covered;
        }

        public static bool HasObjectSet(VillageData village, ObjectSetType setType)
        {
            if (ObjectSetCatalog.All.TryGetValue(setType, out ObjectSetDefinition definition) == false)
                return false;

            if (definition.Range == 0)
                return (AggregateSetMembers(village) & definition.RequiredMask) == definition.RequiredMask;

            SetMemberTag unassignedCovered = SetMemberTag.None;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (VillageConstructionPosition.IsAssigned(placed))
                    continue;

                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(placed.TableId);
                if (table != null)
                    unassignedCovered |= (SetMemberTag)table.SetMembership;
            }
            if ((unassignedCovered & definition.RequiredMask) == definition.RequiredMask)
                return true;

            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData candidate = village.PlacedObjects[i];
                if (VillageConstructionPosition.IsAssigned(candidate) == false)
                    continue;
                if (HasObjectSet(village, setType, new Vector2Int(candidate.TileX, candidate.TileY)))
                    return true;
            }
            return false;
        }

        public static bool HasObjectSet(VillageData village, ObjectSetType setType, Vector2Int anchor)
        {
            if (ObjectSetCatalog.All.TryGetValue(setType, out ObjectSetDefinition definition) == false)
                return false;
            if (definition.Range == 0)
                return (AggregateSetMembers(village) & definition.RequiredMask) == definition.RequiredMask;

            int half = definition.Range / 2;
            SetMemberTag covered = SetMemberTag.None;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (VillageConstructionPosition.IsAssigned(placed) == false)
                    continue;
                if (Mathf.Abs(placed.TileX - anchor.x) > half || Mathf.Abs(placed.TileY - anchor.y) > half)
                    continue;

                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(placed.TableId);
                if (table != null)
                    covered |= (SetMemberTag)table.SetMembership;
            }
            return (covered & definition.RequiredMask) == definition.RequiredMask;
        }

        public static void UpsertTask(VillageData village, BuildTaskSnapshot snapshot)
        {
            int index = FindTaskIndex(village, snapshot.TableId, snapshot.TileX, snapshot.TileY,
                snapshot.BuildingEntityId);
            if (index >= 0)
                village.ActiveBuildTasks[index] = snapshot;
            else
                village.ActiveBuildTasks.Add(snapshot);
        }

        public static void RemoveTask(VillageData village, int tableId, int tileX, int tileY,
            int buildingEntityId)
        {
            int index = FindTaskIndex(village, tableId, tileX, tileY, buildingEntityId);
            if (index >= 0)
                village.ActiveBuildTasks.RemoveAt(index);
        }

        public static void ApplyCompletedBuild(VillageData village, Tables.BuildableItemTable table,
            int tileX, int tileY, int buildingEntityId = -1)
        {
            village.PlacedObjectTypeIds.Add(table.Id);
            village.PlacedObjects.Add(new PlacedObjectSaveData
            {
                TableId = table.Id,
                TileX = tileX,
                TileY = tileY,
                Hp = table.HP,
                MaxHp = table.HP,
                LastUseGameTime = 0f,
                BuildingEntityId = buildingEntityId,
                PlacementPending = VillageConstructionPosition.IsAssigned(tileX, tileY) == false,
            });

            if (table.StorageCap_Food > 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Food, table.StorageCap_Food);
            if (table.StorageCap_Wood > 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Wood, table.StorageCap_Wood);
            if (table.StorageCap_Stone > 0)
                VillageResourceSimulation.AddCap(village, GlobalEnum.ItemType.Stone, table.StorageCap_Stone);

            if (table.Id == VillageNeedsEvaluator.CAMPFIRE_TABLE_ID)
                village.HasCampfire = true;
        }

        private static int FindTaskIndex(VillageData village, int tableId, int tileX, int tileY,
            int buildingEntityId)
        {
            for (int i = 0; i < village.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot task = village.ActiveBuildTasks[i];
                if (buildingEntityId >= 0 && task.BuildingEntityId == buildingEntityId)
                    return i;
                if (task.TableId == tableId && task.TileX == tileX && task.TileY == tileY)
                    return i;
            }
            return -1;
        }
    }
}
