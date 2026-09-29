using System;
using ARPG.Data;
using ARPG.Tables;

namespace ARPG.Dungeon
{
    /// <summary>Versioned, bounded room graph generation. Saved geometry is the authority.</summary>
    public static class DungeonLayout
    {
        public const int GeneratorVersion = 2;

        private const int MONSTER_OBJECT_STRIDE = 10;
        private const int CHEST_OBJECT_OFFSET = 1;
        private const int MONSTER_POSITION_MARGIN = 2;

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

        /// <summary>현재 세대가 NPC 완료로 교체된 세대인가 (NPC_AMBITION_DESIGN D58). NPC 완료는 항상 1 이상의 세대를 만든다.</summary>
        public static bool IsNpcReplaced(DungeonSlot slot)
            => slot != null && slot.NpcReplacedGeneration > 0 && slot.NpcReplacedGeneration == slot.Generation;

        /// <summary>
        /// NPC 완료 필드의 로드 보정 (D58, 스키마 버전 유지). 구 세이브는 필드가 없어 기본값(-1)이 들어오고,
        /// 비정상 값(NaN·음수 시각, 현재 세대와 무관한 세대 표시)은 "기록 없음"으로 되돌린다. 바뀐 게 있으면 true.
        /// </summary>
        public static bool NormalizeNpcCompletionFields(DungeonSaveRoot root)
        {
            if (root == null || root.Villages == null)
                return false;

            bool changed = false;
            for (int v = 0; v < root.Villages.Count; v++)
            {
                VillageDungeonSlots village = root.Villages[v];
                if (village == null)
                    continue;
                if (float.IsNaN(village.LastNpcCompletionGameTime) || float.IsInfinity(village.LastNpcCompletionGameTime) ||
                    village.LastNpcCompletionGameTime < -1f)
                {
                    village.LastNpcCompletionGameTime = -1f;
                    changed = true;
                }
                if (village.Slots == null)
                    continue;
                for (int s = 0; s < village.Slots.Count; s++)
                {
                    DungeonSlot slot = village.Slots[s];
                    if (slot == null || slot.NpcReplacedGeneration == -1)
                        continue;
                    if (slot.NpcReplacedGeneration <= 0 || slot.NpcReplacedGeneration > slot.Generation)
                    {
                        slot.NpcReplacedGeneration = -1;
                        changed = true;
                    }
                }
            }
            return changed;
        }

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
            int monsterCountMin = Math.Max(0, Math.Min(8, table.MonsterCountMin));
            int monsterCountMax = Math.Max(monsterCountMin, Math.Min(8, table.MonsterCountMax));
            int roomWidthMin = Math.Max(7, Math.Min(24, table.RoomWidthMin));
            int roomWidthMax = Math.Max(roomWidthMin, Math.Min(24, table.RoomWidthMax));
            int roomHeightMin = Math.Max(7, Math.Min(24, table.RoomHeightMin));
            int roomHeightMax = Math.Max(roomHeightMin, Math.Min(24, table.RoomHeightMax));
            int horizontalSpacing = roomWidthMax + 8;
            int verticalSpacing = roomHeightMax + 8;
            for (int i = 0; i < count; i++)
            {
                uint hash = Hash(result.Seed, i, 0, 0, 31);
                int row = i / 3;
                int column = row % 2 == 0 ? i % 3 : 2 - i % 3;
                var room = new DungeonRoom { Id = i,
                    X = column * horizontalSpacing + 3 + (int)(hash % 3),
                    Y = row * verticalSpacing + 3 + (int)((hash >> 3) % 3),
                    Width = SampleInclusive(hash >> 6, roomWidthMin, roomWidthMax),
                    Height = SampleInclusive(hash >> 9, roomHeightMin, roomHeightMax) };
                result.Rooms.Add(room);
                if (i > 0)
                {
                    result.Corridors.Add(new DungeonCorridor { FromRoom = i - 1, ToRoom = i,
                        HorizontalFirst = (hash & 1) == 0 });
                    bool isBossRoom = i == count - 1;
                    bool hasChest = i % 2 == 1 && isBossRoom == false;
                    int monsterCount = isBossRoom
                        ? 1
                        : SampleInclusive(hash >> 12, monsterCountMin, monsterCountMax);
                    for (int monsterIndex = 0; monsterIndex < monsterCount; monsterIndex++)
                    {
                        if (TryGetMonsterPosition(result, room, monsterIndex, hasChest,
                            out int monsterX, out int monsterY) == false)
                            throw new InvalidOperationException($"Room {room.Id} has no valid monster position.");
                        int objectOffset = monsterIndex == 0 ? 0 : monsterIndex + 1;
                        result.Objects.Add(new DungeonObject { Id = i * MONSTER_OBJECT_STRIDE + objectOffset,
                            RoomId = i,
                            Kind = isBossRoom ? DungeonObjectKind.Boss : DungeonObjectKind.Monster,
                            TableId = isBossRoom
                                ? table.BossTableId
                                : SelectMonsterTableId(table, result.Seed, i, monsterIndex),
                            Level = isBossRoom ? table.BossLevel : table.MonsterLevel,
                            X = monsterX, Y = monsterY });
                    }
                    if (hasChest == true)
                        result.Objects.Add(new DungeonObject { Id = i * MONSTER_OBJECT_STRIDE + CHEST_OBJECT_OFFSET, RoomId = i,
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
            if (record == null || record.GeneratorVersion < 1 || record.GeneratorVersion > GeneratorVersion)
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
            var occupiedPositions = new System.Collections.Generic.HashSet<long>();
            int bosses = 0;
            for (int i = 0; i < record.Objects.Count; i++)
            {
                var obj = record.Objects[i];
                if (obj == null || ids.Add(obj.Id) == false || obj.RoomId < 0 || obj.RoomId >= record.Rooms.Count)
                    return false;
                var room = record.Rooms[obj.RoomId];
                long positionKey = ((long)obj.X << 32) ^ (uint)obj.Y;
                if (obj.X < room.X || obj.Y < room.Y || obj.X >= room.X + room.Width ||
                    obj.Y >= room.Y + room.Height || occupiedPositions.Add(positionKey) == false)
                    return false;
                if (obj.Kind == DungeonObjectKind.Boss)
                {
                    if (obj.RoomId != record.BossRoomId) return false;
                    bosses++;
                }
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

        private static int SampleInclusive(uint value, int min, int max)
            => min + (int)(value % (uint)(max - min + 1));

        private static int SelectMonsterTableId(DungeonTable table, int seed, int roomId, int monsterIndex)
        {
            if (table.MonsterTableIds == null || table.MonsterTableIds.Count == 0)
                throw new InvalidOperationException($"DungeonTable Id {table.Id} has no MonsterTableIds.");
            if (table.MonsterTableIds.Count == 1)
                return table.MonsterTableIds[0];
            uint hash = Hash(seed, roomId, monsterIndex, 0, 37);
            return table.MonsterTableIds[SampleInclusive(hash, 0, table.MonsterTableIds.Count - 1)];
        }

        private static bool TryGetMonsterPosition(DungeonRecord record, DungeonRoom room,
            int monsterIndex, bool hasChest, out int x, out int y)
        {
            if (monsterIndex == 0)
            {
                x = room.X + room.Width / 2;
                y = room.Y + room.Height / 2;
                return IsObjectPositionAvailable(record, room, hasChest, x, y);
            }

            int innerWidth = room.Width - MONSTER_POSITION_MARGIN * 2;
            int innerHeight = room.Height - MONSTER_POSITION_MARGIN * 2;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                uint hash = Hash(record.Seed, room.Id, monsterIndex, attempt, 43);
                x = room.X + MONSTER_POSITION_MARGIN + (int)(hash % (uint)innerWidth);
                y = room.Y + MONSTER_POSITION_MARGIN + (int)((hash >> 16) % (uint)innerHeight);
                if (IsObjectPositionAvailable(record, room, hasChest, x, y) == true)
                    return true;
            }

            for (int localY = 0; localY < innerHeight; localY++)
            {
                for (int localX = 0; localX < innerWidth; localX++)
                {
                    x = room.X + MONSTER_POSITION_MARGIN + localX;
                    y = room.Y + MONSTER_POSITION_MARGIN + localY;
                    if (IsObjectPositionAvailable(record, room, hasChest, x, y) == true)
                        return true;
                }
            }

            x = 0;
            y = 0;
            return false;
        }

        private static bool IsObjectPositionAvailable(DungeonRecord record, DungeonRoom room,
            bool hasChest, int x, int y)
        {
            if (hasChest == true && x == room.X + 2 && y == room.Y + 2)
                return false;
            for (int i = 0; i < record.Objects.Count; i++)
            {
                var obj = record.Objects[i];
                if (obj.RoomId == room.Id && obj.X == x && obj.Y == y)
                    return false;
            }
            return true;
        }

        private static bool InSegment(int x, int y, int ax, int ay, int bx, int by, int radius)
            => x >= Math.Min(ax, bx) - radius && x <= Math.Max(ax, bx) + radius &&
               y >= Math.Min(ay, by) - radius && y <= Math.Max(ay, by) + radius;
    }
}
