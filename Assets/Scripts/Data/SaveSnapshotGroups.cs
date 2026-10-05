#nullable enable
using System;

namespace ARPG.Data
{
    [Flags]
    public enum NpcSaveGroup : ulong
    {
        Identity = 1, Location = 2, Condition = 4, Activity = 8, Wallet = 16,
        Inventory = 32, Equipment = 64, Rumors = 128, Theft = 256, Deeds = 512,
        Memories = 1024, Stories = 2048, All = 4095,
        // System_NpcSim activity state transitions (and the diagnostics that replay them) mark these together.
        ActivityTransition = Activity | Location | Condition,
    }

    [Flags]
    public enum VillageSaveGroup : ulong
    {
        Identity = 1, Population = 2, Resources = 4, Construction = 8,
        Facilities = 16, Walls = 32, Shop = 64, All = 127,
    }

    [Flags]
    public enum PlayerSaveGroup : ulong
    {
        Identity = 1, Progress = 2, Vitality = 4, Location = 8,
        Inventory = 16, Equipment = 32, SkillBooks = 64, All = 127,
    }

    [Flags]
    public enum DungeonSaveGroup : ulong
    {
        Identity = 1, Layout = 2, Progress = 4, Monsters = 8, Drops = 16, Tiles = 32, All = 63,
    }

    [Flags]
    public enum WorldSaveGroup : ulong
    {
        Metadata = 1, Companion = 2, GoldPrice = 4, All = 7,
    }

    [Flags]
    public enum PartySaveGroup : ulong
    {
        Members = 1, Activity = 2, All = 3,
    }

    /// <summary>메모리 갱신 묶음. 물리 JSON에는 새 필드를 추가하지 않는다.</summary>
    public static class SaveSnapshotGroups
    {
        public static ulong Npc(string field) => (ulong)(field switch
        {
            "PositionX" or "PositionY" or "VillageId" or "IsActive" or "AbandonedSiteSinceGameTime" => NpcSaveGroup.Location,
            "Condition" or "WoundCount" or "LastWoundGameTime" or "LastWoundActivity" => NpcSaveGroup.Condition,
            "Activity" or "LastSimGameTime" => NpcSaveGroup.Activity,
            "Gold" or "LastTradeGameTime" => NpcSaveGroup.Wallet,
            "Inventory" => NpcSaveGroup.Inventory,
            "InventoryEquip" => NpcSaveGroup.Equipment,
            "Rumors" => NpcSaveGroup.Rumors,
            "LastTheftGameTime" or "TheftClues" => NpcSaveGroup.Theft,
            "Deeds" => NpcSaveGroup.Deeds,
            "DeceasedRelations" or "SharedActivityMemories" or "Sentiments" => NpcSaveGroup.Memories,
            "PersonalStories" => NpcSaveGroup.Stories,
            _ => NpcSaveGroup.Identity,
        });

        public static ulong Village(string field) => (ulong)(field switch
        {
            "Population" or "FixedPopulation" or "Stage" or "BoundsX" or "BoundsY" or "BoundsW" or "BoundsH" or "ThreatLevel" or "HasBeenPopulated" => VillageSaveGroup.Population,
            "Resources" or "ResourceCaps" or "HungerHoursAccumulated" => VillageSaveGroup.Resources,
            "ActiveBuildTasks" or "LastConstructionSimGameTime" or "LastConstructionTierCheckGameTime" => VillageSaveGroup.Construction,
            "PlacedObjects" or "PlacedObjectTypeIds" or "InitialDevelopmentApplied" or "AppliedGrowthMilestones" or "HasCampfire" => VillageSaveGroup.Facilities,
            "WallSegments" or "WallSegmentCount" or "CompletedWallSegments" or "WallPlanRequested" => VillageSaveGroup.Walls,
            "MerchantStock" or "ShopTier" or "LastMerchantRollGameTime" or "MerchantRestockCycle" or "NpcPurchaseCycle" or "NpcPurchaseCount" or "LastNpcTradeGameTime" => VillageSaveGroup.Shop,
            _ when field.EndsWith("Carry", StringComparison.Ordinal) || field.EndsWith("Timer", StringComparison.Ordinal) => VillageSaveGroup.Resources,
            _ when field.StartsWith("CurrentBuild", StringComparison.Ordinal) || field.StartsWith("FirstBuild", StringComparison.Ordinal) => VillageSaveGroup.Construction,
            _ => VillageSaveGroup.Identity,
        });

        public static ulong Player(string field) => (ulong)(field switch
        {
            "Level" or "Exp" or "Gold" => PlayerSaveGroup.Progress,
            "CurrentHp" or "CurrentMp" => PlayerSaveGroup.Vitality,
            "_inventory" => PlayerSaveGroup.Inventory,
            "_inventoryEquip" => PlayerSaveGroup.Equipment,
            "_skillBookSlots" => PlayerSaveGroup.SkillBooks,
            "Version" or "PlayerId" or "PlayerTableId" => PlayerSaveGroup.Identity,
            _ => PlayerSaveGroup.Location,
        });

        public static ulong World(string field) => (ulong)(field switch
        {
            "NpcCompanion" => WorldSaveGroup.Companion,
            "GoldPrice" => WorldSaveGroup.GoldPrice,
            _ => WorldSaveGroup.Metadata,
        });
        public static ulong Party(string field) => (ulong)(field == "Activity" ? PartySaveGroup.Activity : PartySaveGroup.Members);
        public static ulong Dungeon(string field) => (ulong)(field switch
        {
            "Rooms" or "Corridors" or "Objects" => DungeonSaveGroup.Layout,
            "Completed" or "KilledObjects" or "OpenedChests" => DungeonSaveGroup.Progress,
            "Monsters" => DungeonSaveGroup.Monsters,
            "Drops" => DungeonSaveGroup.Drops,
            "TileModifications" => DungeonSaveGroup.Tiles,
            _ => DungeonSaveGroup.Identity,
        });
    }
}
