#nullable enable
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ARPG.Data;
using ARPG.History;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Editor
{
    public static partial class NpcSaveSnapshotDiagnostics
    {
        /// <summary>기존 저장 진단의 묶음/슬롯/동결/전체 파티션 캡처 검사. 격리된 DTO만 사용한다.</summary>
        public static string VerifyGroupedSnapshotContracts()
        {
            int checks = 0;
            void Require(bool passed, string label)
            {
                checks++;
                if (passed == false) throw new InvalidOperationException("[GroupedSnapshotContracts] " + label);
            }
            SaveDtoSnapshot copier = new();
            string Json(object? value) => JsonConvert.SerializeObject(value, Formatting.None, JsonSettings.Default);
            Dictionary<string, object?> Fields(object? value) => (Dictionary<string, object?>)value!;
            List<string> changed = new();
            var village = new FaultVillageData(1);
            village.MerchantStock.Add(new MerchantStockEntry { RemainingCount = 3 });
            var cache = new SaveSnapshotCache<int>("GroupedContract", SaveSnapshotGroups.Village);
            object? Source(int id) => id == 1 ? village : null;
            cache.Rebuild(new[] { 1 }, Source);
            var old = Fields(cache.Get(1));
            object? shop = old[nameof(VillageData.MerchantStock)];
            object? facilities = old[nameof(VillageData.PlacedObjects)];
            object? walls = old[nameof(VillageData.WallSegments)];
            village.Cycle = village;
            village.Resources[GlobalEnum.ItemType.Gold] = 10;
            cache.MarkDirty(1, (ulong)VillageSaveGroup.Resources);
            cache.Flush(Source);
            Require(ReferenceEquals(shop, old[nameof(VillageData.MerchantStock)]) && ReferenceEquals(facilities, old[nameof(VillageData.PlacedObjects)]) && ReferenceEquals(walls, old[nameof(VillageData.WallSegments)]), "resources retain shop/facility/wall containers and never read cyclic identity group");
            village.Cycle = null;
            Require(Json(old) == Json(copier.Capture(village)), "selective update same JSON and field order");
            string frozen = Json(old);
            long generation = cache.Generation;
            using (cache.Freeze())
            {
                village.Resources[GlobalEnum.ItemType.Gold] = 20;
                cache.MarkDirty(1, (ulong)VillageSaveGroup.Resources);
                cache.MarkDirty(1, (ulong)VillageSaveGroup.Resources);
                village.MerchantStock.Clear();
                cache.MarkDirty(1, (ulong)VillageSaveGroup.Shop);
                cache.Flush(Source);
                cache.Rebuild(new[] { 1 }, Source);
                Require(Json(old) == frozen && cache.HasPendingChanges == true && cache.Generation > generation, "frozen flush/rebuild accumulate masks without touching writer DTO");
                bool blocked = false;
                try { cache.PrepareForSave(Source); } catch (InvalidOperationException) { blocked = true; }
                Require(blocked == true, "second capture cannot read incomplete frozen ledger");
            }
            cache.PrepareForSave(Source);
            Require(Json(cache.Get(1)) == Json(copier.Capture(village)), "thaw applies merged marks, clear and repeated marks");
            IDisposable previousLifetime = cache.Freeze();
            object? previousDto = cache.Get(1);
            string priorJson = Json(previousDto);
            cache.Reset();
            cache.Rebuild(new[] { 1 }, Source);
            using (cache.Freeze())
            {
                previousLifetime.Dispose();
                Require(cache.IsFrozen == true && Json(previousDto) == priorJson, "old lease cannot thaw new lifetime or clear old DTO");
            }
            village.ShopTier++;
            cache.MarkDirty(1, (ulong)VillageSaveGroup.Resources);
            Require(cache.TryFindUnmarkedChange(1, Source, changed) && changed.Contains(nameof(VillageData.ShopTier)), "pending resource mark cannot hide omitted shop mark");
            cache.Flush(Source);
            village.ShopTier++;
            int omissions = 0;
            cache.DirtyObserver = id => { if (cache.TryFindUnmarkedChange(id, Source, changed)) omissions++; };
            cache.MarkDirty(1, (ulong)VillageSaveGroup.Shop);
            Require(omissions == 1, "next mark observes prior omission of same group");
            cache.DirtyObserver = null;
            cache.Flush(Source);

            var faulty = new FaultVillageData(99);
            var failedCache = new SaveSnapshotCache<int>("Village", SaveSnapshotGroups.Village);
            object? FailedSource(int id) => faulty;
            failedCache.Rebuild(new[] { 99 }, FailedSource);
            var failedFields = Fields(failedCache.Get(99));
            object? oldIdentity = failedFields[nameof(VillageData.TableId)];
            ILogHandler originalLog = Debug.unityLogger.logHandler;
            var expectedFailure = new ExpectedVillageFailureLogHandler(originalLog);
            try
            {
                Debug.unityLogger.logHandler = expectedFailure;
                faulty.Cycle = faulty; faulty.TableId++; faulty.Resources[GlobalEnum.ItemType.Gold] = 7;
                failedCache.MarkDirty(99, (ulong)(VillageSaveGroup.Identity | VillageSaveGroup.Resources));
                failedCache.Flush(FailedSource);
                Require(Equals(failedFields[nameof(VillageData.TableId)], oldIdentity) && failedFields[nameof(FaultVillageData.Cycle)] == null,
                    "failed group never exposes a partially prepared identity");
                bool failedBlocked = false;
                try { failedCache.PrepareForSave(FailedSource); } catch (InvalidOperationException) { failedBlocked = true; }
                Require(failedBlocked && expectedFailure.Count == 1, "group failure blocks save and logs once while other groups can update");
                faulty.Cycle = null;
                failedCache.PrepareForSave(FailedSource);
                Require(Json(failedCache.Get(99)) == Json(copier.Capture(faulty)) && failedCache.HasPendingChanges == false,
                    "failed group retries without losing successful resource updates");
            }
            finally { Debug.unityLogger.logHandler = originalLog; }

            var npc = new NpcSaveData(0, Vector2.zero);
            var npcCache = new SaveSnapshotCache<int>("NpcGroupedContract", SaveSnapshotGroups.Npc);
            object? NpcSource(int id) => npc;
            npcCache.Rebuild(new[] { 32 }, NpcSource);
            var npcFields = Fields(npcCache.Get(32));
            object? bag = npcFields[nameof(NpcSaveData.Inventory)];
            object? equipment = npcFields[nameof(NpcSaveData.InventoryEquip)];
            object? deeds = npcFields[nameof(NpcSaveData.Deeds)];
            npc.Gender = NpcGender.Female;
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Identity);
            npcCache.Flush(NpcSource);
            Require(Json(npcFields) == Json(copier.Capture(npc)) && ReferenceEquals(bag, npcFields[nameof(NpcSaveData.Inventory)]) &&
                ReferenceEquals(equipment, npcFields[nameof(NpcSaveData.InventoryEquip)]), "NPC gender updates with Identity and retains inventory/equipment");
            NpcSaveData genderRoundTrip = JsonConvert.DeserializeObject<NpcSaveData>(Json(npcFields), JsonSettings.Default)!;
            Require(genderRoundTrip.Gender == NpcGender.Female, "NPC gender survives save/load");
            npc.Activity.TargetKey = "new route";
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Activity);
            npcCache.Flush(NpcSource);
            Require(ReferenceEquals(bag, npcFields[nameof(NpcSaveData.Inventory)]) && ReferenceEquals(equipment, npcFields[nameof(NpcSaveData.InventoryEquip)]) && ReferenceEquals(deeds, npcFields[nameof(NpcSaveData.Deeds)]), "NPC activity retains inventory/equipment/deeds");
            npc.WoundCount = 1; npc.LastWoundGameTime = 5; npc.LastWoundActivity = NpcActivityKind.Hunt;
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Condition); npcCache.Flush(NpcSource);
            npc.WoundCount = 0; npc.LastWoundGameTime = -1; npc.LastWoundActivity = NpcActivityKind.None;
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Condition); npcCache.Flush(NpcSource);
            Require(Json(npcFields) == Json(copier.Capture(npc)), "ignored defaults remove old keys");
            npc.WoundCount = 2; npc.LastWoundGameTime = 9; npc.LastWoundActivity = NpcActivityKind.DungeonDelve;
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Condition); npcCache.Flush(NpcSource);
            Require(Json(npcFields) == Json(copier.Capture(npc)), "omitted fields reappear in original contract order");
            // NPC_NARRATIVE_CONTENT_DESIGN §4.2·§7.2: 피해자 판정 던전은 Condition, 부탁 칸은 NarrativeRequest 묶음이며 비면 생략한다.
            npc.LastWoundDungeonId = "dungeon-a";
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.Condition); npcCache.Flush(NpcSource);
            Require(Json(npcFields) == Json(copier.Capture(npc)), "last wound dungeon refreshes with the Condition group");
            npc.NarrativeRequest = new NpcNarrativeRequest { InstanceId = "rumor/dungeon-a", RequestedGameTime = 3f };
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.NarrativeRequest); npcCache.Flush(NpcSource);
            Require(Json(npcFields) == Json(copier.Capture(npc)) && ReferenceEquals(bag, npcFields[nameof(NpcSaveData.Inventory)]),
                "request slot group updates alone");
            npc.NarrativeRequest = null;
            npcCache.MarkDirty(32, (ulong)NpcSaveGroup.NarrativeRequest); npcCache.Flush(NpcSource);
            Require(npcFields.ContainsKey(nameof(NpcSaveData.NarrativeRequest)) == false && Json(npcFields) == Json(copier.Capture(npc)),
                "empty request slot is omitted");

            GameObject owner = new("Grouped snapshot diagnostics") { hideFlags = HideFlags.HideAndDontSave };
            DataManager data = owner.AddComponent<DataManager>();
            object? Capture(string method, object source, bool rebuild = false) => typeof(DataManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data, new[] { source, (object)rebuild });
            ItemData item = new() { Id = 1, Quantity = 2 };
            ItemData retainedItem = new() { Id = 2, Quantity = 1 };
            PlayerData player = new() { PlayerId = 1, _inventory = new List<ItemData?> { item, null, retainedItem } };
            var players = new PlayerSaveRoot { Players = new() { player }, NpcRelations = new() { new PlayerNpcRelationEntry(4, PlayerRelation.Default) } };
            typeof(DataManager).GetField("_playerDataList", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(data, players.Players);
            try
            {
                object? playerRoot = Capture("CaptureOwnedPlayer", players);
                Require(Json(playerRoot) == Json(copier.Capture(players)), "player root, null slots and relation order");
                var playerFields = Fields(((List<object?>)Fields(playerRoot)[nameof(PlayerSaveRoot.Players)]!)[0]);
                var slots = (List<object?>)playerFields[nameof(PlayerData._inventory)]!;
                object? kept = slots[2];
                item.Quantity++;
                data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                playerRoot = Capture("CaptureOwnedPlayer", players);
                var updated = (List<object?>)playerFields[nameof(PlayerData._inventory)]!;
                Require(Json(playerRoot) == Json(copier.Capture(players)) && ReferenceEquals(updated[2], kept), "same item reference quantity change copies changed slot only");
                player._inventory.Reverse(); data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                Require(Json(Capture("CaptureOwnedPlayer", players)) == Json(copier.Capture(players)), "slot reorder including null");
                player._inventory.Clear(); data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                Require(Json(Capture("CaptureOwnedPlayer", players)) == Json(copier.Capture(players)), "inventory clear");
                var book = new ItemData { Id = 3, SkillBook = new SkillBookData() };
                var gear = new ItemData { Id = 4, Equipment = new EquipmentData { Id = 4, Quality = 1 } };
                player._inventory.Add(book); player._inventory.Add(gear);
                data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory); Capture("CaptureOwnedPlayer", players);
                book.SkillBook!.SocketedPages.Add(new ItemData { Id = 5, SkillPage = new SkillPageData { SkillEffectId = 6 } });
                gear.Equipment!.Quality = 9;
                gear.Equipment.WeaponLocalEnhancements.Add(new WeaponLocalEnhancementInstance { Value = 10 });
                data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                Require(Json(Capture("CaptureOwnedPlayer", players)) == Json(copier.Capture(players)), "same-reference options/socket mutation");
                book.SkillBook.SocketedPages.Clear(); gear.Equipment.WeaponLocalEnhancements.Clear();
                data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                Require(Json(Capture("CaptureOwnedPlayer", players)) == Json(copier.Capture(players)), "nested clear removes conditional equipment keys");
                var arDataField = typeof(AR).GetField("_dataManager", BindingFlags.NonPublic | BindingFlags.Instance)!;
                object? liveData = arDataField.GetValue(AR.s);
                try
                {
                    var row = new Tables.BasicItemTable { Id = 123, ItemId = gear.Id, IsValid = true };
                    var rows = System.Collections.Immutable.ImmutableDictionary<int, Tables.BasicItemTable>.Empty.Add(row.Id, row);
                    typeof(DataManager).GetField("_basicItemTable", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(data, rows);
                    arDataField.SetValue(AR.s, data);
                    gear.Equipment.ApplyBasicItemTemplate(row);
                    data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                    string basicJson = Json(Capture("CaptureOwnedPlayer", players));
                    Require(basicJson == JsonConvert.SerializeObject(players, Formatting.None, JsonSettings.Default) && basicJson.Contains("\"BasicItemId\":123") && basicJson.Contains("\"Quality\"") == false,
                        "basic equipment omits Mods/Quality in cached JSON");
                    gear.Equipment.BasicItemId = 0;
                    gear.Equipment.Mods.Add(new Tables.ModInstance { Value1 = 7 });
                    data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Inventory);
                    string regularJson = Json(Capture("CaptureOwnedPlayer", players));
                    Require(regularJson == JsonConvert.SerializeObject(players, Formatting.None, JsonSettings.Default) && regularJson.Contains("\"Quality\":1") && regularJson.Contains("\"BasicItemId\"") == false,
                        "same-reference basic-to-regular restores omitted equipment fields in contract order");
                }
                finally { arDataField.SetValue(AR.s, liveData); }
                WorldSaveRoot world = new() { NpcCompanion = new(), GoldPrice = new() };
                Require(Json(Capture("CaptureOwnedWorld", world)) == Json(copier.Capture(world)), "world metadata/companion/price");
                world.NpcCompanion.ReservedNpcId = 4;
                Require(Json(Capture("CaptureOwnedWorld", world)) == Json(copier.Capture(world)), "small world group change");
                var drop = new WorldDropItemData { ItemData = retainedItem };
                MapSaveRoot map = new() { WorldDropItems = new() { drop }, TileModifications = new() { new ChunkModificationData { ChunkX = 1, ChunkY = 2 } } };
                Require(Json(Capture("CaptureOwnedMap", map)) == Json(copier.Capture(map)), "map drop identity and chunk keys");
                drop.ItemData.Quantity++; data.MarkWorldDropSaveDirty(drop);
                map.TileModifications[0].Modifications.Add(new TileModification { ObjectId = 5 }); data.MarkMapChunkSaveDirty(new DataManager.Vector2IntKey(1, 2));
                Require(Json(Capture("CaptureOwnedMap", map)) == Json(copier.Capture(map)), "map item/chunk change");
                map.WorldDropItems.Clear(); map.TileModifications.Clear();
                Require(Json(Capture("CaptureOwnedMap", map)) == Json(copier.Capture(map)), "map deletion and clear");
                var building = new BuildingSaveData(1, 2, 3) { EntityId = 5 };
                var buildings = new Dictionary<int, BuildingSaveData> { { 5, building } };
                Require(Json(Capture("CaptureOwnedBuildings", buildings)) == Json(copier.Capture(new BuildingSaveRoot { Buildings = buildings })), "building record root");
                building.FacilityTier++; data.MarkBuildingSaveDirty(5);
                Require(Json(Capture("CaptureOwnedBuildings", buildings)) == Json(copier.Capture(new BuildingSaveRoot { Buildings = buildings })), "building tier");
                buildings.Clear();
                Require(Json(Capture("CaptureOwnedBuildings", buildings)) == Json(copier.Capture(new BuildingSaveRoot { Buildings = buildings })), "building deletion");
                var chronicle = new ChronicleSaveData { Events = new() { new ChronicleEvent { EventId = 1 } }, Settlements = new() { new SettlementSnapshot { SettlementId = 2 } }, Kingdoms = new() { new KingdomData { KingdomId = 3 } } };
                var chronicleRoot = Fields(Capture("CaptureOwnedChronicle", chronicle));
                var events = (List<object?>)Fields(chronicleRoot[nameof(ChronicleSaveRoot.Chronicle)])[nameof(ChronicleSaveData.Events)]!;
                object? keptEvent = events[0];
                chronicle.Events.Add(new ChronicleEvent { EventId = 2 });
                object? newChronicle = Capture("CaptureOwnedChronicle", chronicle);
                Require(Json(newChronicle) == Json(copier.Capture(new ChronicleSaveRoot { Chronicle = chronicle })) && ReferenceEquals(keptEvent, ((List<object?>)Fields(Fields(newChronicle)[nameof(ChronicleSaveRoot.Chronicle)])[nameof(ChronicleSaveData.Events)]!)[0]), "append-only chronicle retains prior event DTO");
                var dungeon = new DungeonRecord { DungeonId = "test", Rooms = new() { new DungeonRoom { Id = 1 } }, Monsters = new() { { 2, new DungeonMonsterState { Hp = 10 } } } };
                var dungeons = new DungeonSaveRoot { Dungeons = new() { dungeon }, Villages = new() { new VillageDungeonSlots { VillageId = 1, Slots = new() { new DungeonSlot() } } } };
                object? dungeonRoot = Capture("CaptureOwnedDungeons", dungeons);
                var dungeonFields = Fields(((List<object?>)Fields(dungeonRoot)[nameof(DungeonSaveRoot.Dungeons)]!)[0]);
                object? layout = dungeonFields[nameof(DungeonRecord.Rooms)];
                dungeon.Monsters[2].Hp--; data.MarkDungeonSaveDirty(dungeon, DungeonSaveGroup.Monsters);
                object? updatedRoot = Capture("CaptureOwnedDungeons", dungeons);
                Require(Json(updatedRoot) == Json(copier.Capture(dungeons)) && ReferenceEquals(layout, dungeonFields[nameof(DungeonRecord.Rooms)]), "dungeon monster update retains fixed layout");
                dungeons.Dungeons.Clear();
                Require(Json(Capture("CaptureOwnedDungeons", dungeons)) == Json(copier.Capture(dungeons)), "completed/deleted dungeon cannot resurrect");
                dungeons.Dungeons.Add(dungeon);
                Require(Json(Capture("CaptureOwnedDungeons", dungeons)) == Json(copier.Capture(dungeons)), "delete/re-add record initializes all groups");
                // narrative 파트 (NPC_NARRATIVE_CONTENT_DESIGN §7.3): 인스턴스·정의·정책·선정 기록 묶음 갱신과 삭제 뒤 되살아나지 않음.
                var narrative = new NarrativeSaveRoot();
                var instance = new NarrativeContentInstance { InstanceId = "rumor/test", ContentId = 1002, DungeonId = "test",
                    DefinitionHash = "hash", PolicyHash = "policy", OwnerNpcId = 4, TargetMonsterId = 1009 };
                narrative.Instances.Add(instance);
                narrative.Definitions.Add(new NarrativeDefinitionSnapshot { Hash = "hash", Content = new NarrativeContentDefinition { ContentId = 1002 } });
                narrative.Policies.Add(new NarrativePolicySnapshot { Hash = "policy", PolicyVersion = 1 });
                narrative.Selections.Add(new NarrativeSelectionRecord { DungeonId = "test", InstanceId = "rumor/test" });
                object? narrativeRoot = Capture("CaptureOwnedNarrative", narrative);
                Require(Json(narrativeRoot) == Json(copier.Capture(narrative)), "narrative root initial capture");
                object? definitionDto = ((List<object?>)Fields(narrativeRoot)[nameof(NarrativeSaveRoot.Definitions)]!)[0];
                instance.Progress = NarrativeRumorProgress.Resolved; instance.ProgressGameTime = 12f; instance.Revision++;
                data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Progress);
                instance.Reported = true; instance.ReportNpcId = 4; instance.ReportRelevance = NarrativeRelevance.Requester; instance.RewardGold = 14;
                data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Reports | NarrativeSaveGroup.Rewards);
                object? narrativeUpdated = Capture("CaptureOwnedNarrative", narrative);
                Require(Json(narrativeUpdated) == Json(copier.Capture(narrative)) &&
                    ReferenceEquals(definitionDto, ((List<object?>)Fields(narrativeUpdated)[nameof(NarrativeSaveRoot.Definitions)]!)[0]),
                    "narrative progress/report/reward groups match complete DTO and retain the definition DTO");
                instance.RequesterNpcId = 4; instance.RequestedGameTime = 2f;
                data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Introductions);
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)), "narrative introduction group");
                // 3단계 소망: 대상 아이템은 Target, 소망 진행은 Progress 묶음이다. 기본값(Hidden·0)은 던전 소문 DTO에서 생략된다.
                var wish = new NarrativeContentInstance { InstanceId = "wish/4/2001", ContentId = 2001, Kind = NarrativeContentKind.Wish,
                    DefinitionHash = "hash", PolicyHash = "policy", OwnerNpcId = 4, RequesterNpcId = 4, WishState = WishState.Revealed };
                narrative.Instances.Add(wish);
                data.MarkNarrativeInstanceSaveDirty(wish.InstanceId, NarrativeSaveGroup.All);
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)) &&
                    Json(copier.Capture(instance)).Contains(nameof(NarrativeContentInstance.WishState)) == false,
                    "wish instance capture; rumor DTO omits default wish fields");
                wish.TargetItemId = 16;
                data.MarkNarrativeInstanceSaveDirty(wish.InstanceId, NarrativeSaveGroup.Target);
                wish.WishState = WishState.Fulfilled; wish.ProgressGameTime = 20f;
                data.MarkNarrativeInstanceSaveDirty(wish.InstanceId, NarrativeSaveGroup.Progress);
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)),
                    "wish target item and state refresh with the Target/Progress groups");
                // 5단계 후속 소개: 소개한 쪽 기록은 Continuations, 소개받은 쪽 기록은 Introductions 묶음이다.
                instance.FollowUpInstanceId = "rumor/next"; instance.FollowUpState = NarrativeFollowUpState.Pending;
                data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Continuations);
                instance.IntroducedFromInstanceId = "rumor/prev";
                data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.Introductions);
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)),
                    "follow-up fields refresh with the Continuations/Introductions groups");
                narrative.Instances.Clear(); narrative.Selections.Clear();
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)), "pruned narrative instance/selection cannot resurrect");
                narrative.Definitions.Clear(); narrative.Policies.Clear();
                Require(Json(Capture("CaptureOwnedNarrative", narrative)) == Json(copier.Capture(narrative)), "pruned definition/policy cannot resurrect");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            return $"[GroupedSnapshotContracts] {checks}/{checks} PASS: selective groups, changed slots, omission observers, freeze/reset lifetimes, Player/World/Map/Buildings/Chronicle/Dungeons/Narrative roots.";
        }

        /// <summary>동일 합성 마을과 변경 패턴으로 전체/묶음 복사를 비교한다. 기존 Village 진단에서 명시적으로 실행한다.</summary>
        public static string MeasureGroupedSnapshotUpdates()
        {
            const int count = 300;
            const int samples = 20;
            Dictionary<int, VillageData> villages = new();
            for (int i = 0; i < count; i++)
            {
                var village = new VillageData(i, Vector2.zero);
                village.Resources[GlobalEnum.ItemType.Gold] = 0;
                for (int j = 0; j < 12; j++)
                {
                    village.MerchantStock.Add(new MerchantStockEntry { Item = new ItemData { Id = j + 1, Quantity = 3 }, RemainingCount = 3 });
                    village.PlacedObjects.Add(new PlacedObjectSaveData { TableId = j + 1, TileX = j, Hp = 10 });
                }
                for (int j = 0; j < 32; j++) village.WallSegments.Add(new WallSegmentSaveData { SegmentId = j, CurrentHp = 10 });
                villages.Add(i, village);
            }
            object? Source(int id) => villages[id];
            var full = new SaveSnapshotCache<int>("Baseline");
            var grouped = new SaveSnapshotCache<int>("Grouped", SaveSnapshotGroups.Village);
            full.Rebuild(villages.Keys, Source); grouped.Rebuild(villages.Keys, Source);
            string Measure(string label, Action action, int iterations = samples)
            {
                action(); // warm contracts/JIT before measured samples
                double[] times = new double[iterations];
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                int gc0 = GC.CollectionCount(0);
                for (int i = 0; i < iterations; i++)
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    action();
                    times[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                gc0 = GC.CollectionCount(0) - gc0;
                double total = 0;
                for (int i = 0; i < iterations; i++) total += times[i];
                Array.Sort(times);
                string allocation = allocated == 0 ? "unavailable (counter returned 0)" : (allocated / iterations) + "B/sample";
                return $"{label}: avg={total / iterations:F3}ms p95={times[(int)Math.Ceiling(iterations * 0.95) - 1]:F3}ms max={times[iterations - 1]:F3}ms alloc={allocation} GC0={gc0}";
            }
            void MarkAll(SaveSnapshotCache<int> cache, ulong mask)
            {
                for (int i = 0; i < count; i++) cache.MarkDirty(i, mask);
            }
            void Mutate() { for (int i = 0; i < count; i++) villages[i].Resources[GlobalEnum.ItemType.Gold]++; }
            var report = new StringBuilder($"[GroupedSnapshotPerformance] synthetic {count} villages, {samples} samples, resource-only updates; 12 stock/12 facilities/32 walls each\n");
            report.AppendLine(Measure("full item update", () => { Mutate(); MarkAll(full, ulong.MaxValue); full.Flush(Source); }));
            report.AppendLine(Measure("grouped resource update", () => { Mutate(); MarkAll(grouped, (ulong)VillageSaveGroup.Resources); grouped.Flush(Source); }));
            report.AppendLine(Measure("unchanged flush", () => grouped.Flush(Source)));
            report.AppendLine(Measure("grouped full rebuild", () => grouped.Rebuild(villages.Keys, Source), 5));
            report.AppendLine(Measure("root assembly", () => { var root = new List<object?>(count); for (int i = 0; i < count; i++) root.Add(grouped.Get(i)); }));
            long captured = grouped.GroupCaptureCount;
            using (grouped.Freeze())
            {
                report.AppendLine(Measure("frozen accumulate/flush", () => { Mutate(); MarkAll(grouped, (ulong)VillageSaveGroup.Resources); grouped.Flush(Source); }));
                if (grouped.GroupCaptureCount != captured) throw new InvalidOperationException("Frozen benchmark mutated cache.");
            }
            long thawStart = System.Diagnostics.Stopwatch.GetTimestamp();
            grouped.PrepareForSave(Source);
            double thaw = (System.Diagnostics.Stopwatch.GetTimestamp() - thawStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            report.AppendLine($"thaw: 300 accumulated resource groups applied once, {thaw:F3}ms; frozen captures=0");
            var copier = new SaveDtoSnapshot();
            for (int i = 0; i < count; i++)
                if (JsonConvert.SerializeObject(grouped.Get(i), JsonSettings.Default) != JsonConvert.SerializeObject(copier.Capture(villages[i]), JsonSettings.Default))
                    throw new InvalidOperationException("Benchmark JSON mismatch: " + i);
            return report.Append("PASS: 300/300 final JSON equals full capture.").ToString();
        }
    }
}
#endif
