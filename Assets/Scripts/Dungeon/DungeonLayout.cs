using System;
using ARPG.Data;
using ARPG.Tables;

namespace ARPG.Dungeon
{
    /// <summary>Versioned, bounded room graph generation. Saved geometry is the authority.</summary>
    public static class DungeonLayout
    {
        public const int GeneratorVersion = 1;

        public static uint Hash(int seed, int village, int slot, int generation, int channel)
        {
            unchecked
            {
                uint value = (uint)seed ^ 2166136261u;
                value = (value ^ (uint)village) * 16777619u;
                value = (value ^ (uint)slot) * 16777619u;
                value = (value ^ (uint)generation) * 16777619u;
                value = (value ^ (uint)channel) * 16777619u;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }

        public static string Id(int seed, int village, int slot, int generation)
            => FormattableString.Invariant($"{seed:X8}:{village}:{slot}:{generation}");

        public static VillageDungeonSlots CreateSlots(int seed, int village, int dungeonTableId = 1)
        {
            var result = new VillageDungeonSlots { VillageId = village,
                SlotCount = 2 + (int)(Hash(seed, village, 0, 0, 11) & 1) };
            for (int i = 0; i < result.SlotCount; i++)
                result.Slots.Add(new DungeonSlot { SlotIndex = i, DungeonTableId = dungeonTableId,
                    DungeonId = Id(seed, village, i, 0) });
            return result;
        }

        public static bool IsPlacementReady(DungeonSlot slot, float currentGameTime)
            => slot != null && slot.HasEntrance == false &&
               currentGameTime >= slot.PlacementReadyGameTime;

        public static bool NormalizeTableReferences(DungeonSaveRoot root, DungeonTable defaultTable)
        {
            if (root == null || defaultTable == null || defaultTable.Id <= 0)
                return false;

            bool changed = false;
            for (int v = 0; v < root.Villages.Count; v++)
            {
                VillageDungeonSlots village = root.Villages[v];
                for (int s = 0; s < village.Slots.Count; s++)
                {
                    if (village.Slots[s].DungeonTableId > 0) continue;
                    village.Slots[s].DungeonTableId = defaultTable.Id;
                    changed = true;
                }
            }
            for (int i = 0; i < root.Dungeons.Count; i++)
            {
                DungeonRecord record = root.Dungeons[i];
                if (record.DungeonTableId > 0) continue;
                record.DungeonTableId = defaultTable.Id;
                record.ReplacementCooldownHours = Math.Max(0f, defaultTable.ReplacementCooldownHours);
                changed = true;
            }
            return changed;
        }

        public static DungeonRecord Generate(int seed, int village, DungeonSlot slot, DungeonTable table)
        {
            var result = new DungeonRecord { DungeonId = slot.DungeonId, VillageId = village,
                SlotIndex = slot.SlotIndex, Generation = slot.Generation,
                DungeonTableId = table.Id,
                GeneratorVersion = GeneratorVersion,
                Seed = (int)Hash(seed, village, slot.SlotIndex, slot.Generation, 29),
                CompletionGold = table.CompletionGold, ChestGold = table.ChestGold,
                ReplacementCooldownHours = Math.Max(0f, table.ReplacementCooldownHours) };
            int count = Math.Max(6, Math.Min(12, table.RoomCount));
            for (int i = 0; i < count; i++)
            {
                uint hash = Hash(result.Seed, i, 0, 0, 31);
                int row = i / 3;
                int column = row % 2 == 0 ? i % 3 : 2 - i % 3;
                var room = new DungeonRoom { Id = i, X = column * 20 + 3 + (int)(hash % 3),
                    Y = row * 20 + 3 + (int)((hash >> 3) % 3),
                    Width = 9 + (int)((hash >> 6) % 4), Height = 9 + (int)((hash >> 9) % 4) };
                result.Rooms.Add(room);
                if (i > 0)
                {
                    result.Corridors.Add(new DungeonCorridor { FromRoom = i - 1, ToRoom = i,
                        HorizontalFirst = (hash & 1) == 0 });
                    result.Objects.Add(new DungeonObject { Id = i * 10, RoomId = i,
                        Kind = i == count - 1 ? DungeonObjectKind.Boss : DungeonObjectKind.Monster,
                        TableId = i == count - 1 ? table.BossTableId : table.MonsterTableId,
                        Level = i == count - 1 ? table.BossLevel : table.MonsterLevel,
                        X = room.X + room.Width / 2, Y = room.Y + room.Height / 2 });
                    if (i % 2 == 1 && i < count - 1)
                        result.Objects.Add(new DungeonObject { Id = i * 10 + 1, RoomId = i,
                            Kind = DungeonObjectKind.Chest, X = room.X + 2, Y = room.Y + 2 });
                }
            }
            result.BossRoomId = count - 1;
            if (Validate(result, out string error) == false)
                throw new InvalidOperationException(error);
            return result;
        }

        public static bool Validate(DungeonRecord record, out string error)
        {
            error = "Invalid dungeon layout.";
            if (record == null || record.GeneratorVersion != GeneratorVersion)
            {
                error = "Unsupported Dungeon GeneratorVersion. Restore a compatible save/editor version; layout was not regenerated.";
                return false;
            }
            if (record.Rooms == null || record.Rooms.Count < 2 || record.Rooms.Count > 64 ||
                record.Corridors == null || record.Objects == null ||
                record.KilledObjects == null || record.OpenedChests == null ||
                record.CompletionGold < 0 || record.ChestGold < 0 ||
                float.IsNaN(record.ReplacementCooldownHours) ||
                float.IsInfinity(record.ReplacementCooldownHours) ||
                record.ReplacementCooldownHours < 0f ||
                record.BossRoomId <= 0 || record.BossRoomId >= record.Rooms.Count)
                return false;
            var reached = new bool[record.Rooms.Count];
            reached[0] = true;
            for (int i = 0; i < record.Rooms.Count; i++)
            {
                var room = record.Rooms[i];
                if (room == null || room.Id != i || room.Width < 3 || room.Height < 3 ||
                    room.Width > 64 || room.Height > 64 || room.X < -8 || room.Y < -8 ||
                    room.X + room.Width > 128 || room.Y + room.Height > 128)
                    return false;
            }
            for (int pass = 0; pass < reached.Length; pass++)
                for (int i = 0; i < record.Corridors.Count; i++)
                {
                    var c = record.Corridors[i];
                    if (c == null || c.FromRoom < 0 || c.ToRoom < 0 || c.FromRoom >= reached.Length ||
                        c.ToRoom >= reached.Length || c.Width < 2 || c.Width > 5)
                        return false;
                    if (reached[c.FromRoom] == true || reached[c.ToRoom] == true)
                        reached[c.FromRoom] = reached[c.ToRoom] = true;
                }
            for (int i = 0; i < reached.Length; i++)
                if (reached[i] == false) return false;
            var ids = new System.Collections.Generic.HashSet<int>();
            int bosses = 0;
            for (int i = 0; i < record.Objects.Count; i++)
            {
                var obj = record.Objects[i];
                if (obj == null || ids.Add(obj.Id) == false || IsFloor(record, obj.X, obj.Y) == false)
                    return false;
                if (obj.Kind == DungeonObjectKind.Boss) bosses++;
            }
            if (bosses != 1) return false;
            error = string.Empty;
            return true;
        }

        public static bool IsFloor(DungeonRecord record, int x, int y)
        {
            for (int i = 0; i < record.Rooms.Count; i++)
            {
                var r = record.Rooms[i];
                if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height)
                    return true;
            }
            for (int i = 0; i < record.Corridors.Count; i++)
            {
                var c = record.Corridors[i];
                var a = record.Rooms[c.FromRoom];
                var b = record.Rooms[c.ToRoom];
                int ax = a.X + a.Width / 2, ay = a.Y + a.Height / 2;
                int bx = b.X + b.Width / 2, by = b.Y + b.Height / 2;
                int mx = c.HorizontalFirst ? bx : ax, my = c.HorizontalFirst ? ay : by;
                int radius = c.Width / 2;
                if (InSegment(x, y, ax, ay, mx, my, radius) == true || InSegment(x, y, mx, my, bx, by, radius) == true)
                    return true;
            }
            return false;
        }

        private static bool InSegment(int x, int y, int ax, int ay, int bx, int by, int radius)
            => x >= Math.Min(ax, bx) - radius && x <= Math.Max(ax, bx) + radius &&
               y >= Math.Min(ay, by) - radius && y <= Math.Max(ay, by) + radius;
    }
}
