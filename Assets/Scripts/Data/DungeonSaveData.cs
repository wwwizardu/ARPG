using System;
using System.Collections.Generic;
using ARPG.Map;

namespace ARPG.Data
{
    [Serializable]
    public sealed class DungeonSaveRoot
    {
        public int Version = 1;
        public List<VillageDungeonSlots> Villages = new();
        public List<DungeonRecord> Dungeons = new();
        public long CompletedCount;
    }

    [Serializable]
    public sealed class VillageDungeonSlots
    {
        public int VillageId;
        public int SlotCount;
        public List<DungeonSlot> Slots = new();
        // 마지막 NPC 던전 완료 게임시각 (NPC_AMBITION_DESIGN D58, 마을당 72h 쿨다운). -1 = 기록 없음 (구 세이브 포함).
        public float LastNpcCompletionGameTime = -1f;
    }

    [Serializable]
    public sealed class DungeonSlot
    {
        public int SlotIndex;
        public int Generation;
        public int DungeonTableId;
        public string DungeonId = string.Empty;
        public bool HasEntrance;
        public int EntranceX;
        public int EntranceY;
        public bool HasPreviousEntrance;
        public int PreviousX;
        public int PreviousY;
        public float PlacementReadyGameTime;
        public int NextCandidate;
        // NPC 완료로 만들어진 세대 번호 (NPC_AMBITION_DESIGN D58). 이 값 == Generation이면 현재 세대가 NPC 교체 세대다.
        // -1 = 해당 없음 (구 세이브 포함). 플레이어 완료는 새 슬롯 객체를 만들어 기본값(-1)으로 돌아간다.
        public int NpcReplacedGeneration = -1;
    }

    [Serializable]
    public sealed class DungeonRecord
    {
        public string DungeonId = string.Empty;
        public int VillageId;
        public int SlotIndex;
        public int Generation;
        public int DungeonTableId;
        public int Seed;
        public int GeneratorVersion;
        public int BossRoomId;
        public int CompletionGold;
        public int ChestGold;
        public float ReplacementCooldownHours;
        public bool Completed;
        public List<DungeonRoom> Rooms = new();
        public List<DungeonCorridor> Corridors = new();
        public List<DungeonObject> Objects = new();
        public HashSet<int> KilledObjects = new();
        public HashSet<int> OpenedChests = new();
        public Dictionary<int, DungeonMonsterState> Monsters = new();
        public List<WorldDropItemData> Drops = new();
        public List<ChunkModificationData> TileModifications = new();
    }

    [Serializable]
    public sealed class DungeonRoom
    {
        public int Id;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [Serializable]
    public sealed class DungeonCorridor
    {
        public int FromRoom;
        public int ToRoom;
        public bool HorizontalFirst;
        public int Width = 3;
    }

    public enum DungeonObjectKind { Monster, Boss, Chest }

    [Serializable]
    public sealed class DungeonMonsterState
    {
        public int Hp;
        public float X;
        public float Y;
    }

    [Serializable]
    public sealed class DungeonObject
    {
        public int Id;
        public int RoomId;
        public DungeonObjectKind Kind;
        public int TableId;
        public int Level;
        public int X;
        public int Y;
    }
}
