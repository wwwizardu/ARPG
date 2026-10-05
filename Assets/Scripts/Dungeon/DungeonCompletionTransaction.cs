using System;
using ARPG.Data;

namespace ARPG.Dungeon
{
    /// <summary>Reversible completion proposal. Dispose rolls back unless the manifest commit succeeded.</summary>
    public sealed class DungeonCompletionTransaction : IDisposable
    {
        private readonly DungeonSaveRoot _root;
        private readonly PlayerData _player;
        private readonly DungeonRecord _record;
        private readonly VillageDungeonSlots _village;
        private readonly DungeonSlot _previous;
        private readonly int _previousGold;
        private bool _committed;
        private bool _disposed;

        public static bool NeedsCompletion(DungeonRecord record)
        {
            if (record == null || record.Completed == true) return false;
            for (int i = 0; i < record.Objects.Count; i++)
                if (record.Objects[i].Kind == DungeonObjectKind.Boss &&
                    record.KilledObjects.Contains(record.Objects[i].Id) == true) return true;
            return false;
        }

        public static bool TryStage(DungeonSaveRoot root, PlayerData player, DungeonRecord record, int worldSeed,
            float currentGameTime, float replacementCooldownHours,
            out DungeonCompletionTransaction transaction)
        {
            transaction = null;
            if (record == null || record.Completed == true || record.CompletionGold < 0 ||
                (long)player.Gold + record.CompletionGold > int.MaxValue || root.CompletedCount == long.MaxValue)
                return false;
            if (NeedsCompletion(record) == false) return false;
            for (int i = 0; i < root.Villages.Count; i++)
            {
                var village = root.Villages[i];
                if (village.VillageId != record.VillageId || record.SlotIndex < 0 || record.SlotIndex >= village.Slots.Count) continue;
                var slot = village.Slots[record.SlotIndex];
                if (slot.DungeonId != record.DungeonId || slot.Generation != record.Generation ||
                    slot.Generation == int.MaxValue || slot.HasEntrance == false) return false;
                transaction = new DungeonCompletionTransaction(root, player, record, village, slot, worldSeed,
                    currentGameTime, replacementCooldownHours);
                return true;
            }
            return false;
        }

        public void Commit() { _committed = true; }

        public void Dispose()
        {
            if (_disposed == true) return;
            _disposed = true;
            if (_committed == true) return;
            AR.s?.Data?.MarkDungeonSlotsSaveDirty(_village.VillageId);
            AR.s?.Data?.MarkDungeonSaveDirty(_record, DungeonSaveGroup.Progress);
            _village.Slots[_previous.SlotIndex] = _previous;
            _player.Gold = _previousGold;
            _record.Completed = false;
            _root.CompletedCount--;
        }

        private DungeonCompletionTransaction(DungeonSaveRoot root, PlayerData player, DungeonRecord record,
            VillageDungeonSlots village, DungeonSlot slot, int worldSeed, float currentGameTime,
            float replacementCooldownHours)
        {
            _root = root; _player = player; _record = record; _village = village; _previous = slot;
            _previousGold = player.Gold;
            AR.s?.Data?.MarkDungeonSlotsSaveDirty(village.VillageId);
            AR.s?.Data?.MarkDungeonSaveDirty(record, DungeonSaveGroup.Progress);
            village.Slots[slot.SlotIndex] = new DungeonSlot
            {
                SlotIndex = slot.SlotIndex, Generation = slot.Generation + 1,
                DungeonTableId = slot.DungeonTableId,
                DungeonId = DungeonLayout.Id(worldSeed, village.VillageId, slot.SlotIndex, slot.Generation + 1),
                HasPreviousEntrance = true, PreviousX = slot.EntranceX, PreviousY = slot.EntranceY,
                PlacementReadyGameTime = Math.Max(0f, currentGameTime) +
                    Math.Max(0f, replacementCooldownHours),
            };
            player.Gold += record.CompletionGold;
            record.Completed = true;
            root.CompletedCount++;
        }
    }
}
