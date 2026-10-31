#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using ARPG.History;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Village;

namespace ARPG.Data
{
    public partial class DataManager
    {
        public static readonly bool GroupedSaveSnapshotEnabled = true;
        // DataManager owns these persisted ledgers. Keys for unkeyed drops are runtime record identities only.
        private Dictionary<string, SaveSnapshotCache<object>> _ownedSaveSnapshots = new();
        // Per-part frame flush getters, created once per part and reused. They depend only on this manager and
        // the part name, so they survive ResetPartitionedSaveState.
        private readonly Dictionary<string, Func<object, object?>> _ownedSaveSourceGetters = new();

        public void MarkPlayerSaveDirty(PlayerData player, PlayerSaveGroup groups)
        {
            // Isolated DTO fixtures must not mark the live player's cache.
            for (int i = 0; i < _playerDataList.Count; i++)
                if (ReferenceEquals(player, _playerDataList[i]) == true)
                { MarkOwnedSaveDirty(PART_PLAYER, player.PlayerId, (ulong)groups); return; }
        }

        public void MarkDungeonSaveDirty(DungeonRecord record, DungeonSaveGroup groups) =>
            MarkOwnedSaveDirty(PART_DUNGEONS, ("record", record.DungeonId), (ulong)groups);
        public void MarkDungeonSlotsSaveDirty(int villageId) => MarkOwnedSaveDirty(PART_DUNGEONS, ("village", villageId));
        // narrative 파트 (NPC_NARRATIVE_CONTENT_DESIGN §7.3). 원장 소유자 NpcManager만 부른다 — 사건 revision도 함께 올린다.
        public void MarkNarrativeInstanceSaveDirty(string instanceId, NarrativeSaveGroup groups) =>
            MarkOwnedSaveDirty(PART_NARRATIVE, ("instance", instanceId), (ulong)groups);
        public void MarkNarrativeDefinitionSaveDirty(string hash) => MarkOwnedSaveDirty(PART_NARRATIVE, ("definition", hash));
        public void MarkNarrativePolicySaveDirty(string hash) => MarkOwnedSaveDirty(PART_NARRATIVE, ("policy", hash));
        public void MarkNarrativeSelectionSaveDirty(string dungeonId) => MarkOwnedSaveDirty(PART_NARRATIVE, ("selection", dungeonId));
        public void MarkBuildingSaveDirty(int entityId) => MarkOwnedSaveDirty(PART_BUILDINGS, entityId);
        public void MarkMapChunkSaveDirty(Vector2IntKey chunk) => MarkOwnedSaveDirty(PART_MAP, chunk);
        public void MarkWorldDropSaveDirty(WorldDropItemData drop) => MarkOwnedSaveDirty(PART_MAP, drop);
        // ore_veins 파트 (WORLD_ORE_VEIN_PLAN §8.1). 기록 소유자 OreVeinManager만 부른다 — 생성·잔량·고갈·활성 시각·삭제 모두 청크 키로 표시한다.
        public void MarkOreVeinSaveDirty(int chunkX, int chunkY) => MarkOwnedSaveDirty(PART_ORE_VEINS, new Vector2IntKey(chunkX, chunkY));
        public void MarkPlayerRelationSaveDirty(int npcId) => MarkOwnedSaveDirty(PART_PLAYER, ("relation", npcId));
        public void MarkWorldSaveDirty(WorldSaveGroup groups) => MarkOwnedSaveDirty(PART_WORLD, 0, (ulong)groups);
        public void MarkChronicleEventSaveDirty(int eventId) => MarkOwnedSaveDirty(PART_CHRONICLE, ("event", eventId));
        public void MarkChronicleSettlementSaveDirty(int settlementId) => MarkOwnedSaveDirty(PART_CHRONICLE, ("settlement", settlementId));
        public void MarkChronicleKingdomSaveDirty(int kingdomId) => MarkOwnedSaveDirty(PART_CHRONICLE, ("kingdom", kingdomId));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string DescribeOwnedSaveSnapshots()
        {
            var text = new System.Text.StringBuilder();
            foreach (var pair in _ownedSaveSnapshots) text.Append(pair.Key).Append(':').Append(pair.Value.DescribeState());
            return text.ToString();
        }
#endif

        public void FlushOwnedSaveSnapshots(bool recordFrameTiming = false)
        {
            if (GroupedSaveSnapshotEnabled == false) return;
            using var entries = _ownedSaveSnapshots.GetEnumerator();
            for (; entries.MoveNext() == true;)
            {
                var pair = entries.Current;
                pair.Value.Flush(GetOwnedSaveSourceGetter(pair.Key), recordFrameTiming);
            }
        }

        private void MarkOwnedSaveDirty(string part, object key, ulong groups = ulong.MaxValue)
        {
            if (GroupedSaveSnapshotEnabled == true) GetOwnedSaveCache(part).MarkDirty(key, groups);
        }

        private object? GetOwnedSaveSource(string part, object key)
        {
            if (part == PART_PLAYER)
            {
                if (key is int id) return GetPlayerData(id);
                if (key is ValueTuple<string, int> relation && AR.s.Npc.HasMetPlayer(relation.Item2) == true)
                    return new PlayerNpcRelationEntry(relation.Item2, AR.s.Npc.GetPlayerRelation(relation.Item2));
                return null;
            }
            if (part == PART_WORLD) return new WorldSaveRoot { WorldDataVersion = CURRENT_WORLD_DATA_VERSION,
                WorldSeed = _worldData.WorldSeed, NextItemInstanceId = _nextItemInstanceId,
                ElapsedWorldGameHours = _worldData.ElapsedWorldGameHours, NpcCompanion = _worldData.NpcCompanion, GoldPrice = _worldData.GoldPrice,
                NamedThreats = _worldData.NamedThreats };
            if (part == PART_MAP)
            {
                if (key is WorldDropItemData drop) return _worldData.WorldDropItemDatas.Contains(drop) == true ? drop : null;
                if (key is Vector2IntKey chunk) return AR.s.WorldMap.GetSaveChunkModifications(chunk.X, chunk.Y);
            }
            if (part == PART_ORE_VEINS && key is Vector2IntKey vein)
            {
                List<OreVeinRecord> veins = _worldData.OreVeins;
                for (int i = 0; i < veins.Count; i++)
                    if (veins[i].ChunkX == vein.X && veins[i].ChunkY == vein.Y) return veins[i];
                return null;
            }
            if (part == PART_BUILDINGS && key is int building) return AR.s.Building.GetSaveData(building);
            if (part == PART_NARRATIVE && key is ValueTuple<string, string> narrative)
                return GetNarrativeSaveSource(narrative.Item1, narrative.Item2);
            if (part == PART_DUNGEONS)
            {
                if (key is ValueTuple<string, string> dungeon)
                    for (int i = 0; i < _worldData.Dungeons.Dungeons.Count; i++)
                        if (_worldData.Dungeons.Dungeons[i].DungeonId == dungeon.Item2) return _worldData.Dungeons.Dungeons[i];
                if (key is ValueTuple<string, int> village)
                    for (int i = 0; i < _worldData.Dungeons.Villages.Count; i++)
                        if (_worldData.Dungeons.Villages[i].VillageId == village.Item2) return _worldData.Dungeons.Villages[i];
                return null;
            }
            if (part == PART_CHRONICLE && key is ValueTuple<string, int> chronicle)
            {
                if (chronicle.Item1 == "event")
                    for (int i = 0; i < _worldData.Chronicle.Events.Count; i++)
                        if (_worldData.Chronicle.Events[i].EventId == chronicle.Item2) return _worldData.Chronicle.Events[i];
                if (chronicle.Item1 == "settlement")
                    for (int i = 0; i < _worldData.Chronicle.Settlements.Count; i++)
                        if (_worldData.Chronicle.Settlements[i].SettlementId == chronicle.Item2) return _worldData.Chronicle.Settlements[i];
                if (chronicle.Item1 == "kingdom")
                    for (int i = 0; i < _worldData.Chronicle.Kingdoms.Count; i++)
                        if (_worldData.Chronicle.Kingdoms[i].KingdomId == chronicle.Item2) return _worldData.Chronicle.Kingdoms[i];
            }
            return null;
        }

        private object? GetNarrativeSaveSource(string kind, string id)
        {
            NarrativeSaveRoot root = _worldData.Narrative;
            switch (kind)
            {
                case "instance":
                    for (int i = 0; i < root.Instances.Count; i++)
                        if (root.Instances[i].InstanceId == id) return root.Instances[i];
                    return null;
                case "definition":
                    for (int i = 0; i < root.Definitions.Count; i++)
                        if (root.Definitions[i].Hash == id) return root.Definitions[i];
                    return null;
                case "policy":
                    for (int i = 0; i < root.Policies.Count; i++)
                        if (root.Policies[i].Hash == id) return root.Policies[i];
                    return null;
                case "selection":
                    for (int i = 0; i < root.Selections.Count; i++)
                        if (root.Selections[i].DungeonId == id) return root.Selections[i];
                    return null;
            }
            return null;
        }

        private Func<object, object?> GetOwnedSaveSourceGetter(string part)
        {
            if (_ownedSaveSourceGetters.TryGetValue(part, out var getter) == true) return getter;
            getter = CreateOwnedSaveSourceGetter(part);
            _ownedSaveSourceGetters.Add(part, getter);
            return getter;
        }

        // Kept separate: a lambda capturing `part` in the method above would allocate its closure on every call.
        private Func<object, object?> CreateOwnedSaveSourceGetter(string part) => key => GetOwnedSaveSource(part, key);

        private SaveSnapshotCache<object> GetOwnedSaveCache(string part)
        {
            if (_ownedSaveSnapshots.TryGetValue(part, out var cache) == true) return cache;
            Func<string, ulong>? groups = part switch
            {
                PART_PLAYER => SaveSnapshotGroups.Player,
                PART_WORLD => SaveSnapshotGroups.World,
                PART_DUNGEONS => SaveSnapshotGroups.Dungeon,
                PART_NARRATIVE => SaveSnapshotGroups.Narrative,
                _ => null,
            };
            cache = new SaveSnapshotCache<object>(part, groups);
            _ownedSaveSnapshots.Add(part, cache);
            return cache;
        }

        private SaveSnapshotCache<object> PrepareOwnedSaveCache(string part, Dictionary<object, object?> sources, bool rebuild)
        {
            var cache = GetOwnedSaveCache(part);
            List<object> old = new(cache.Keys);
            for (int i = 0; i < old.Count; i++)
                if (sources.ContainsKey(old[i]) == false) cache.MarkDirty(old[i]);
            foreach (object key in sources.Keys)
                if (rebuild == true || cache.Contains(key) == false) cache.MarkDirty(key);
            cache.PrepareForSave(key => sources.TryGetValue(key, out object? value) == true ? value : null);
            return cache;
        }

        private Dictionary<string, object?> EmptySnapshotRoot(object root) => (Dictionary<string, object?>)_saveSnapshots.Capture(root)!;

        private object CaptureOwnedPlayer(PlayerSaveRoot live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            var playerCache = GetOwnedSaveCache(PART_PLAYER);
            for (int i = 0; i < live.Players.Count; i++)
            {
                PlayerData player = live.Players[i];
                sources.Add(player.PlayerId, player);
                // ECS/map transactions write scalar mirrors directly. Compare those cheap fields only.
                if (playerCache.TryGetSnapshot(player.PlayerId, out object? prior) == true && prior is Dictionary<string, object?> fields)
                    playerCache.MarkDirty(player.PlayerId, _saveSnapshots.FindChangedScalarGroups(player,
                        fields, SaveSnapshotGroups.Player));
            }
            for (int i = 0; i < live.NpcRelations.Count; i++) sources.Add(("relation", live.NpcRelations[i].NpcId), live.NpcRelations[i]);
            var cache = PrepareOwnedSaveCache(PART_PLAYER, sources, rebuild);
            var root = EmptySnapshotRoot(new PlayerSaveRoot { CurrentPlayerIndex = live.CurrentPlayerIndex });
            var players = (List<object?>)root[nameof(PlayerSaveRoot.Players)]!;
            for (int i = 0; i < live.Players.Count; i++) players.Add(cache.Get(live.Players[i].PlayerId));
            var relations = (List<object?>)root[nameof(PlayerSaveRoot.NpcRelations)]!;
            for (int i = 0; i < live.NpcRelations.Count; i++) relations.Add(cache.Get(("relation", live.NpcRelations[i].NpcId)));
            return root;
        }

        private object? CaptureOwnedWorld(WorldSaveRoot live, bool rebuild)
        {
            // Four scalar metadata values are sampled at capture; companion/price/named-threat changes have their own marks.
            // Named-threat lists change through NamedThreatManager, which marks WorldSaveGroup.NamedThreat itself.
            MarkWorldSaveDirty(WorldSaveGroup.Metadata);
            var cache = GetOwnedSaveCache(PART_WORLD);
            if (cache.TryGetSnapshot(0, out object? prior) == true && prior is Dictionary<string, object?> old)
            {
                ObserveWorldGroup(live.NpcCompanion, old, nameof(WorldSaveRoot.NpcCompanion), WorldSaveGroup.Companion);
                ObserveWorldGroup(live.GoldPrice, old, nameof(WorldSaveRoot.GoldPrice), WorldSaveGroup.GoldPrice);
                ObserveWorldGroup(live.NamedThreats, old, nameof(WorldSaveRoot.NamedThreats), WorldSaveGroup.NamedThreat);
            }
            return PrepareOwnedSaveCache(PART_WORLD, new() { { 0, live } }, rebuild).Get(0);
        }

        private void ObserveWorldGroup(object? live, Dictionary<string, object?> old, string field, WorldSaveGroup group)
        {
            old.TryGetValue(field, out object? previous);
            if (live == null) { if (previous != null) MarkWorldSaveDirty(group); }
            else if (previous is not Dictionary<string, object?> fields ||
                _saveSnapshots.FindChangedScalarGroups(live, fields, name => (ulong)group) != 0) MarkWorldSaveDirty(group);
        }

        private object CaptureOwnedMap(MapSaveRoot live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            for (int i = 0; i < live.WorldDropItems.Count; i++) sources.Add(live.WorldDropItems[i], live.WorldDropItems[i]);
            for (int i = 0; i < live.TileModifications.Count; i++)
            {
                var chunk = live.TileModifications[i];
                sources.Add(new Vector2IntKey(chunk.ChunkX, chunk.ChunkY), chunk);
            }
            var cache = PrepareOwnedSaveCache(PART_MAP, sources, rebuild);
            var root = EmptySnapshotRoot(new MapSaveRoot());
            var drops = (List<object?>)root[nameof(MapSaveRoot.WorldDropItems)]!;
            for (int i = 0; i < live.WorldDropItems.Count; i++) drops.Add(cache.Get(live.WorldDropItems[i]));
            var tiles = (List<object?>)root[nameof(MapSaveRoot.TileModifications)]!;
            for (int i = 0; i < live.TileModifications.Count; i++)
                tiles.Add(cache.Get(new Vector2IntKey(live.TileModifications[i].ChunkX, live.TileModifications[i].ChunkY)));
            return root;
        }

        private object CaptureOwnedOreVeins(List<OreVeinRecord> live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            for (int i = 0; i < live.Count; i++) sources.Add(new Vector2IntKey(live[i].ChunkX, live[i].ChunkY), live[i]);
            var cache = PrepareOwnedSaveCache(PART_ORE_VEINS, sources, rebuild);
            var root = EmptySnapshotRoot(new OreVeinSaveRoot());
            var veins = (List<object?>)root[nameof(OreVeinSaveRoot.Veins)]!;
            for (int i = 0; i < live.Count; i++) veins.Add(cache.Get(new Vector2IntKey(live[i].ChunkX, live[i].ChunkY)));
            return root;
        }

        private object CaptureOwnedBuildings(Dictionary<int, BuildingSaveData> live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            foreach (var pair in live) sources.Add(pair.Key, pair.Value);
            var cache = PrepareOwnedSaveCache(PART_BUILDINGS, sources, rebuild);
            var root = EmptySnapshotRoot(new BuildingSaveRoot());
            var buildings = (Dictionary<string, object?>)root[nameof(BuildingSaveRoot.Buildings)]!;
            foreach (var pair in live) buildings.Add(pair.Key.ToString(CultureInfo.InvariantCulture), cache.Get(pair.Key));
            return root;
        }

        private object CaptureOwnedChronicle(ChronicleSaveData live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            for (int i = 0; i < live.Events.Count; i++) sources.Add(("event", live.Events[i].EventId), live.Events[i]);
            for (int i = 0; i < live.Settlements.Count; i++) sources.Add(("settlement", live.Settlements[i].SettlementId), live.Settlements[i]);
            for (int i = 0; i < live.Kingdoms.Count; i++) sources.Add(("kingdom", live.Kingdoms[i].KingdomId), live.Kingdoms[i]);
            var cache = PrepareOwnedSaveCache(PART_CHRONICLE, sources, rebuild);
            var root = EmptySnapshotRoot(new ChronicleSaveRoot { Chronicle = new ChronicleSaveData { Version = live.Version, SimulatedYears = live.SimulatedYears } });
            var chronicle = (Dictionary<string, object?>)root[nameof(ChronicleSaveRoot.Chronicle)]!;
            var events = (List<object?>)chronicle[nameof(ChronicleSaveData.Events)]!;
            for (int i = 0; i < live.Events.Count; i++) events.Add(cache.Get(("event", live.Events[i].EventId)));
            var settlements = (List<object?>)chronicle[nameof(ChronicleSaveData.Settlements)]!;
            for (int i = 0; i < live.Settlements.Count; i++) settlements.Add(cache.Get(("settlement", live.Settlements[i].SettlementId)));
            var kingdoms = (List<object?>)chronicle[nameof(ChronicleSaveData.Kingdoms)]!;
            for (int i = 0; i < live.Kingdoms.Count; i++) kingdoms.Add(cache.Get(("kingdom", live.Kingdoms[i].KingdomId)));
            return root;
        }

        private object CaptureOwnedDungeons(DungeonSaveRoot live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            for (int i = 0; i < live.Dungeons.Count; i++) sources.Add(("record", live.Dungeons[i].DungeonId), live.Dungeons[i]);
            for (int i = 0; i < live.Villages.Count; i++) sources.Add(("village", live.Villages[i].VillageId), live.Villages[i]);
            var cache = PrepareOwnedSaveCache(PART_DUNGEONS, sources, rebuild);
            var root = EmptySnapshotRoot(new DungeonSaveRoot { Version = live.Version, CompletedCount = live.CompletedCount });
            var dungeons = (List<object?>)root[nameof(DungeonSaveRoot.Dungeons)]!;
            for (int i = 0; i < live.Dungeons.Count; i++) dungeons.Add(cache.Get(("record", live.Dungeons[i].DungeonId)));
            var villages = (List<object?>)root[nameof(DungeonSaveRoot.Villages)]!;
            for (int i = 0; i < live.Villages.Count; i++) villages.Add(cache.Get(("village", live.Villages[i].VillageId)));
            return root;
        }

        private object CaptureOwnedNarrative(NarrativeSaveRoot live, bool rebuild)
        {
            Dictionary<object, object?> sources = new();
            for (int i = 0; i < live.Instances.Count; i++) sources.Add(("instance", live.Instances[i].InstanceId), live.Instances[i]);
            for (int i = 0; i < live.Definitions.Count; i++) sources.Add(("definition", live.Definitions[i].Hash), live.Definitions[i]);
            for (int i = 0; i < live.Policies.Count; i++) sources.Add(("policy", live.Policies[i].Hash), live.Policies[i]);
            for (int i = 0; i < live.Selections.Count; i++) sources.Add(("selection", live.Selections[i].DungeonId), live.Selections[i]);
            var cache = PrepareOwnedSaveCache(PART_NARRATIVE, sources, rebuild);
            var root = EmptySnapshotRoot(new NarrativeSaveRoot { Version = live.Version });
            var instances = (List<object?>)root[nameof(NarrativeSaveRoot.Instances)]!;
            for (int i = 0; i < live.Instances.Count; i++) instances.Add(cache.Get(("instance", live.Instances[i].InstanceId)));
            var definitions = (List<object?>)root[nameof(NarrativeSaveRoot.Definitions)]!;
            for (int i = 0; i < live.Definitions.Count; i++) definitions.Add(cache.Get(("definition", live.Definitions[i].Hash)));
            var policies = (List<object?>)root[nameof(NarrativeSaveRoot.Policies)]!;
            for (int i = 0; i < live.Policies.Count; i++) policies.Add(cache.Get(("policy", live.Policies[i].Hash)));
            var selections = (List<object?>)root[nameof(NarrativeSaveRoot.Selections)]!;
            for (int i = 0; i < live.Selections.Count; i++) selections.Add(cache.Get(("selection", live.Selections[i].DungeonId)));
            return root;
        }

        private void FreezeSaveSnapshotBatch(Dictionary<string, PendingSavePart> pending, List<IDisposable> leases)
        {
            if (pending.ContainsKey(PART_VILLAGES) == true && VillageManager.SaveSnapshotEnabled == true) leases.Add(AR.s.Village.FreezeSaveSnapshot());
            if (pending.ContainsKey(PART_WORLD_SIM) == true && HistoryManager.SaveSnapshotEnabled == true) leases.Add(AR.s.History.FreezeSaveSnapshot());
            SaveScope simulation = SaveScope.None;
            if (pending.ContainsKey(PART_NPC_SOCIAL) == true) simulation |= SaveScope.NpcSocial;
            if (pending.ContainsKey(PART_NPC_PARTIES) == true) simulation |= SaveScope.NpcParties;
            if (pending.ContainsKey(PART_NPC_META) == true) simulation |= SaveScope.NpcMeta;
            if (NpcManager.SimulationSaveSnapshotEnabled == true) AR.s.Npc.FreezeSimulationSaveSnapshots(simulation, leases);
            for (int i = 0; i < NPC_SHARD_COUNT; i++)
                if (pending.ContainsKey(GetNpcShardPartKey(i)) == true && NpcManager.SaveSnapshotEnabled == true)
                { leases.Add(AR.s.Npc.FreezeSaveSnapshot()); break; }
            foreach (var pair in _ownedSaveSnapshots)
                if (pending.ContainsKey(pair.Key) == true) leases.Add(pair.Value.Freeze());
        }

        // Runtime chunk key, independent of Unity DTO serialization or persistent IDs.
        public readonly struct Vector2IntKey : IEquatable<Vector2IntKey>
        {
            private readonly int _x;
            private readonly int _y;
            public int X => _x;
            public int Y => _y;
            public Vector2IntKey(int x, int y) { _x = x; _y = y; }
            public bool Equals(Vector2IntKey other) => _x == other._x && _y == other._y;
            public override bool Equals(object? other) => other is Vector2IntKey key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(_x, _y);
        }
    }
}
