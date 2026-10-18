#nullable enable
using System;

namespace ARPG.Data
{
    [Flags]
    public enum NpcSaveGroup : ulong
    {
        Identity = 1, Location = 2, Condition = 4, Activity = 8, Wallet = 16,
        Inventory = 32, Equipment = 64, Rumors = 128, Theft = 256, Deeds = 512,
        // NarrativeRequest(부탁 칸)는 구 ScenarioKnowledge 자리(4096)를 이어받았다 (NPC_NARRATIVE_CONTENT_DESIGN §7.2).
        Memories = 1024, Stories = 2048, NarrativeRequest = 4096, All = 8191,
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

    /// <summary>narrative 파트 갱신 묶음 (NPC_NARRATIVE_CONTENT_DESIGN §7.3). 인스턴스·정의·정책·선정 기록이 같은 묶음 함수를 쓴다.</summary>
    [Flags]
    public enum NarrativeSaveGroup : ulong
    {
        Identity = 1, Target = 2, Progress = 4, Introductions = 8, Reports = 16, Rewards = 32, Continuations = 64, All = 127,
    }

    [Flags]
    public enum WorldSaveGroup : ulong
    {
        // NamedThreat: 네임드 위협 기록 (NAMED_THREAT_DESIGN §10.1). 기록 안 목록이 바뀌면 소유자 NamedThreatManager가 표시한다.
        Metadata = 1, Companion = 2, GoldPrice = 4, NamedThreat = 8, All = 15,
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
            "Condition" or "WoundCount" or "LastWoundGameTime" or "LastWoundActivity" or "LastWoundDungeonId" or "LastWoundThreatId" => NpcSaveGroup.Condition,
            "Activity" or "LastSimGameTime" => NpcSaveGroup.Activity,
            "Gold" or "LastTradeGameTime" => NpcSaveGroup.Wallet,
            "Inventory" => NpcSaveGroup.Inventory,
            "InventoryEquip" => NpcSaveGroup.Equipment,
            "Rumors" => NpcSaveGroup.Rumors,
            "LastTheftGameTime" or "TheftClues" => NpcSaveGroup.Theft,
            "Deeds" => NpcSaveGroup.Deeds,
            "DeceasedRelations" or "SharedActivityMemories" or "Sentiments" => NpcSaveGroup.Memories,
            "PersonalStories" => NpcSaveGroup.Stories,
            "NarrativeRequest" => NpcSaveGroup.NarrativeRequest,
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
            "NamedThreats" => WorldSaveGroup.NamedThreat,
            _ => WorldSaveGroup.Metadata,
        });
        public static ulong Party(string field) => (ulong)(field == "Activity" ? PartySaveGroup.Activity : PartySaveGroup.Members);
        public static ulong Dungeon(string field) => (ulong)(field switch
        {
            "Rooms" or "Corridors" or "Objects" => DungeonSaveGroup.Layout,
            "Completed" or "KilledObjects" or "OpenedChests" or "BossKilledByPlayerParty" => DungeonSaveGroup.Progress,
            "Monsters" => DungeonSaveGroup.Monsters,
            "Drops" => DungeonSaveGroup.Drops,
            "TileModifications" => DungeonSaveGroup.Tiles,
            _ => DungeonSaveGroup.Identity,
        });
        public static ulong Narrative(string field) => (ulong)(field switch
        {
            "DungeonId" or "VillageId" or "SlotIndex" or "Generation" or "TargetMonsterId" or
                "HasEntranceContext" or "EntranceX" or "EntranceY" or "TargetItemId" or "ThreatId" or "OwnerNpcId" => NarrativeSaveGroup.Target,
            "Progress" or "WishState" or "ProgressGameTime" or "Ended" or "EndNoticePending" or "Revision" => NarrativeSaveGroup.Progress,
            "RequesterNpcId" or "RequestedGameTime" or "IntroducedFromInstanceId" => NarrativeSaveGroup.Introductions,
            "FollowUpInstanceId" or "FollowUpState" => NarrativeSaveGroup.Continuations,
            "Reported" or "ReportNpcId" or "ReportRelevance" or "ReactionRuleId" or "Reaction" or "Response" or
                "AffinityDelta" or "TrustDelta" or "IntimacyDelta" or "ReportGameTime" or "ReportDisplayed" => NarrativeSaveGroup.Reports,
            "RewardGold" or "RewardRefused" => NarrativeSaveGroup.Rewards,
            _ => NarrativeSaveGroup.Identity,
        });
    }
}
