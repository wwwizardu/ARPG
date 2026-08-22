#nullable enable
using System;
using System.Collections.Generic;
using ARPG.History;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Village;

namespace ARPG.Data
{
    [Flags]
    public enum SaveScope
    {
        None = 0,
        Player = 1 << 0,
        World = 1 << 1,
        Map = 1 << 2,
        Villages = 1 << 3,
        Buildings = 1 << 4,
        Chronicle = 1 << 5,
        WorldSim = 1 << 6,
        NpcMeta = 1 << 7,
        NpcState = 1 << 8,
        NpcSocial = 1 << 9,
        NpcParties = 1 << 10,
        All = Player | World | Map | Villages | Buildings | Chronicle |
              WorldSim | NpcMeta | NpcState | NpcSocial | NpcParties,
    }

    [Serializable]
    public sealed class SaveManifest
    {
        public int FormatVersion = 1;
        public long Revision;
        public int NpcShardCount = 32;
        public string SavedAtUtc = string.Empty;
        public Dictionary<string, SavePartReference> Parts = new();
    }

    [Serializable]
    public sealed class SavePartReference
    {
        public string FileName = string.Empty;
        public int SchemaVersion;
        public long Revision;
        public long Length;
        public string Sha256 = string.Empty;
    }

    [Serializable]
    public sealed class PlayerSaveRoot
    {
        public int Version = 1;
        public int CurrentPlayerIndex;
        public List<PlayerData> Players = new();
        public List<PlayerNpcRelationEntry> NpcRelations = new();
    }

    [Serializable]
    public sealed class WorldSaveRoot
    {
        public int Version = 1;
        public ushort WorldDataVersion = 14;
        public int NextItemInstanceId = 1;
    }

    [Serializable]
    public sealed class MapSaveRoot
    {
        public int Version = 1;
        public List<WorldDropItemData> WorldDropItems = new();
        public List<ChunkModificationData> TileModifications = new();
    }

    [Serializable]
    public sealed class VillageSaveRoot
    {
        public int Version = 1;
        public List<VillageData> Villages = new();
    }

    [Serializable]
    public sealed class BuildingSaveRoot
    {
        public int Version = 1;
        public Dictionary<int, BuildingSaveData> Buildings = new();
    }

    [Serializable]
    public sealed class ChronicleSaveRoot
    {
        public int Version = 1;
        public ChronicleSaveData Chronicle = new();
    }

    [Serializable]
    public sealed class WorldSimSaveRoot
    {
        public int Version = 1;
        public WorldSimState WorldSim = new();
    }

    [Serializable]
    public sealed class NpcMetaSaveRoot
    {
        public int Version = 1;
        public NpcPopulationState Population = new();
    }

    [Serializable]
    public sealed class NpcSocialSaveRoot
    {
        public int Version = 1;
        public List<NpcAffinityEntry> Affinities = new();
        public List<NpcRelationEntry> Relations = new();
    }

    [Serializable]
    public sealed class NpcPartySaveRoot
    {
        public int Version = 1;
        public List<NpcPartyState> Parties = new();
    }

    [Serializable]
    public sealed class NpcShardSaveRoot
    {
        public int Version = 1;
        public int ShardIndex;
        public Dictionary<int, NpcSaveData> Npcs = new();
    }
}
