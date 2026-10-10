using System.Collections.Generic;
using ARPG.Data;
using ARPG.Map;

namespace ARPG.Dungeon
{
    public static class DungeonSaveValidation
    {
        public static bool IsConsistent(DungeonSaveRoot root, List<PlayerData> players, int worldSeed)
        {
            if (root == null || (root.Version != 1 && root.Version != 2) || root.Villages == null || root.Dungeons == null || players == null)
                return false;
            var slots = new Dictionary<string, DungeonSlot>();
            var villages = new Dictionary<int, VillageDungeonSlots>();
            for (int v = 0; v < root.Villages.Count; v++)
            {
                var village = root.Villages[v];
                if (village == null || villages.TryAdd(village.VillageId, village) == false ||
                    village.SlotCount < 2 || village.SlotCount > 3 || village.Slots == null ||
                    village.Slots.Count != village.SlotCount) return false;
                for (int s = 0; s < village.Slots.Count; s++)
                {
                    var slot = village.Slots[s];
                    if (slot == null || slot.SlotIndex != s || slot.Generation < 0 ||
                        slot.DungeonTableId < 0 || slot.NextCandidate < 0 ||
                        float.IsNaN(slot.PlacementReadyGameTime) || float.IsInfinity(slot.PlacementReadyGameTime) ||
                        slot.PlacementReadyGameTime < 0f ||
                        slot.DungeonId != DungeonLayout.Id(worldSeed, village.VillageId, s, slot.Generation) ||
                        slots.TryAdd(slot.DungeonId, slot) == false) return false;
                    if (slot.HasEntrance == true && slot.HasPreviousEntrance == true &&
                        slot.EntranceX == slot.PreviousX && slot.EntranceY == slot.PreviousY) return false;
                }
            }
            var records = new Dictionary<string, DungeonRecord>();
            for (int i = 0; i < root.Dungeons.Count; i++)
            {
                var record = root.Dungeons[i];
                if (record == null || string.IsNullOrEmpty(record.DungeonId) == true || records.TryAdd(record.DungeonId, record) == false ||
                    villages.TryGetValue(record.VillageId, out var village) == false ||
                    record.SlotIndex < 0 || record.SlotIndex >= village.SlotCount || record.Generation < 0 ||
                    record.DungeonTableId < 0 || record.CompletionGold < 0 || record.ChestGold < 0 ||
                    float.IsNaN(record.ReplacementCooldownHours) ||
                    float.IsInfinity(record.ReplacementCooldownHours) || record.ReplacementCooldownHours < 0f ||
                    record.DungeonId != DungeonLayout.Id(worldSeed, record.VillageId, record.SlotIndex, record.Generation) ||
                    record.Rooms == null || record.Corridors == null || record.Objects == null || record.Monsters == null ||
                    record.KilledObjects == null || record.OpenedChests == null || record.Drops == null || record.TileModifications == null)
                    return false;
                var slot = village.Slots[record.SlotIndex];
                if (record.DungeonTableId > 0 && slot.DungeonTableId > 0 &&
                    record.DungeonTableId != slot.DungeonTableId)
                    return false;
                if (record.Completed ? slot.Generation != (long)record.Generation + 1 : slot.DungeonId != record.DungeonId)
                    return false;
            }
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) return false;
                if (player.CurrentMapKind == MapKind.Dungeon &&
                    (string.IsNullOrEmpty(player.ActiveDungeonId) == true || records.ContainsKey(player.ActiveDungeonId) == false)) return false;
                if (player.CurrentMapKind != MapKind.World && player.CurrentMapKind != MapKind.Dungeon) return false;
            }
            return true;
        }
    }
}
