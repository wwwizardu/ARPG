#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Utility;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    public static partial class NpcAmbitionSimDiagnostics
    {
        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim/Validate Shared Activities")]
        private static void ValidateSharedActivitiesMenu() => Debug.Log(RunSharedActivityDiagnostics());

        /// <summary>복제 NPC와 임시 매니저만 사용한다. 저장 파일과 실행 중인 게임의 관계는 수정하지 않는다.</summary>
        public static string RunSharedActivityDiagnostics()
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            GameObject fixture = new("SharedActivityDiagnostics") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                NpcManager manager = fixture.AddComponent<NpcManager>();
                Dictionary<int, NpcSaveData> npcs = new()
                {
                    { 1, NewSharedActivityNpc(1, "A") },
                    { 2, NewSharedActivityNpc(2, "B") },
                    { 3, NewSharedActivityNpc(3, "C") },
                    { 4, NewSharedActivityNpc(4, "D") },
                };
                npcs[4].Condition = CharacterConditions.Dead;
                AddLegacySharedDeed(npcs[1], 2, 8, 7f);
                AddLegacySharedDeed(npcs[2], 1, 8, 7f);
                AddLegacySharedDeed(npcs[1], 4, 4, 3f);
                AddLegacySharedDeed(npcs[4], 1, 4, 3f);
                npcs[1].Deeds.Add(new NpcDeedEntry(NpcDeedKind.DungeonCleared, -1, NpcKnowledgeLevel.Did, 2f));
                GetField(manager, "_npcSaveDict").SetValue(manager, npcs);
                int checks = 0;
                void Require(bool passed, string message)
                {
                    if (passed == false)
                        throw new InvalidOperationException("Shared activities: " + message);
                    checks++;
                }
                Require(manager.LoadAffinity(new List<NpcAffinityEntry> { new(1, 2, 10) }), "legacy migration reported");
                NpcSharedActivityStats Stats(int a, int b) => manager.GetSharedActivities(a)
                    .First(e => e.NpcA == b || e.NpcB == b).SharedActivities!;
                Require(manager.GetAffinity(1, 2) == 10, "current affinity preserved");
                Require(Stats(1, 2).AffinityGain == 8, "mirrored legacy gains counted once");
                Require(Stats(1, 2).HuntCount == 0 && Stats(1, 2).DungeonDelveCount == 0 &&
                    Stats(1, 2).HasUncountedHistory, "unknown old counts not invented");
                Require(npcs.Values.All(n => n.Deeds.All(d => d.Kind != NpcDeedKind.PartyAffinityChanged)), "legacy deeds removed");
                Require(npcs[1].Deeds.Single().Kind == NpcDeedKind.DungeonCleared, "notable clear memory retained");
                Require(npcs[1].SharedActivityMemories.Single().OtherNpcName == "D" &&
                    npcs[4].SharedActivityMemories.Single().Activities.AffinityGain == 4, "dead companion history preserved both ways");
                Require(manager.HasMet(1, 4) == false, "dead companion does not reenter active affinity");
                string migrated = JsonConvert.SerializeObject(manager.SaveAffinity(), JsonSettings.Default);
                Require(manager.MigrateLegacyPartyActivities() == 0 &&
                    JsonConvert.SerializeObject(manager.SaveAffinity(), JsonSettings.Default) == migrated, "migration is idempotent");

                manager.ApplyPartyRelationshipOutcome(new[] { 1, 2, 4 },
                    new[] { NpcHuntOutcome.Success, NpcHuntOutcome.Failed, NpcHuntOutcome.Dead }, true, 10f, NpcActivityKind.Hunt);
                Require(Stats(1, 2).HuntCount == 1 && manager.GetAffinity(1, 2) == 14, "hunt counted once per surviving pair");
                Require(manager.HasMet(1, 4) == false, "dead participant excluded");
                manager.ApplyPartyRelationshipOutcome(new[] { 1, 2 }, new[] { NpcHuntOutcome.Failed, NpcHuntOutcome.Failed },
                    false, 11f, NpcActivityKind.Hunt);
                Require(Stats(1, 2).HuntCount == 2 && manager.GetAffinity(1, 2) == 15, "failed hunt still counts and gains one");
                manager.ApplyPartyRelationshipOutcome(new[] { 1, 2, 3 },
                    new[] { NpcHuntOutcome.Success, NpcHuntOutcome.Wounded, NpcHuntOutcome.Success }, true, 20f,
                    NpcActivityKind.DungeonDelve, true);
                Require(Stats(1, 2).DungeonDelveCount == 1 && Stats(1, 3).DungeonDelveCount == 1 &&
                    Stats(2, 3).DungeonDelveCount == 1, "delve counts all surviving pairs");
                Require(Stats(1, 3).DungeonClearCount == 1 && Stats(1, 2).DungeonClearCount == 0 &&
                    Stats(2, 3).DungeonClearCount == 0, "clear counts only successful pairs");
                manager.ApplyPartyRelationshipOutcome(new[] { 1, 2 }, new[] { NpcHuntOutcome.Success, NpcHuntOutcome.Success },
                    true, 21f, NpcActivityKind.DungeonDelve, true);
                Require(Stats(1, 2).DungeonDelveCount == 2 && Stats(1, 2).DungeonClearCount == 1 &&
                    Stats(1, 2).LastActivity == NpcActivityKind.DungeonDelve && Stats(1, 2).LastGameTime == 21f,
                    "clear and last activity tracked separately");
                Require(Stats(1, 2).AffinityGain == 21, "cumulative expedition gain preserved and updated");
                Require(npcs.Values.All(n => n.Deeds.All(d => d.Kind != NpcDeedKind.PartyAffinityChanged)), "new outcomes create no party deeds");
                manager.ApplyPartyRelationshipOutcome(new[] { 3, 3 }, new[] { NpcHuntOutcome.Success, NpcHuntOutcome.Success },
                    true, 22f, NpcActivityKind.Hunt);
                Require(manager.GetSharedActivities(3).All(e => e.NpcA != e.NpcB), "duplicate member creates no self pair");
                List<NpcAffinityEntry> snapshot = manager.SaveAffinity();
                snapshot.First(e => e.NpcA == 1 && e.NpcB == 2).SharedActivities!.HuntCount++;
                Require(Stats(1, 2).HuntCount == 2, "save copy owns its nested statistics");
                string json = JsonConvert.SerializeObject(manager.SaveAffinity(), JsonSettings.Default);
                manager.LoadAffinity(JsonConvert.DeserializeObject<List<NpcAffinityEntry>>(json, JsonSettings.Default));
                Require(Stats(1, 2).HuntCount == 2 && Stats(1, 2).DungeonClearCount == 1, "save load round trip");
                Require(JsonConvert.SerializeObject(new NpcAffinityEntry(5, 6, 7), JsonSettings.Default)
                    .Contains("SharedActivities") == false, "empty relation stats omitted");
                Require(JsonConvert.SerializeObject(new NpcSharedActivityStats { HuntCount = 1 }, JsonSettings.Default)
                    .Contains("DungeonDelveCount") == false, "zero counters omitted");
                var dto = new SaveDtoSnapshot();
                string dtoJson = JsonConvert.SerializeObject(dto.Capture(manager.SaveAffinity()), Formatting.None, JsonSettings.Default);
                Require(dtoJson == JsonConvert.SerializeObject(manager.SaveAffinity(), Formatting.None, JsonSettings.Default),
                    "background save DTO preserves the same serialized statistics");
                Require(JsonConvert.SerializeObject(dto.Capture(new NpcAffinityEntry(5, 6, 7)), JsonSettings.Default)
                    .Contains("SharedActivities") == false, "background save omits empty relation stats");

                npcs[2].Condition = CharacterConditions.Dead;
                manager.RemoveAllAffinityInvolving(2);
                Require(manager.HasMet(1, 2) == false && manager.HasMet(2, 3) == false, "death removes active relation pairs");
                NpcSharedActivityMemory memory = npcs[1].SharedActivityMemories.Single(m => m.OtherNpcId == 2);
                Require(memory.Activities.HuntCount == 2 && memory.Activities.DungeonClearCount == 1 &&
                    memory.LastAffinity == 23, "death archives counters and final affinity");
                Require(manager.TryGetLatestSharedActivity(1, out int otherId, out _, out NpcSharedActivityMemory? latest) &&
                    otherId == 2 && latest != null, "dialogue can retrieve latest archived companion");
                memory.Activities.HuntCount++;
                Require(npcs[2].SharedActivityMemories.Single(m => m.OtherNpcId == 1).Activities.HuntCount == 2,
                    "archived copies do not alias each other");
                string summary = $"[SharedActivities] PASS {checks} checks. ";
                return summary + MeasureSavedSharedActivityMigration(manager);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Random.state = randomState;
            }
        }

        private static NpcSaveData NewSharedActivityNpc(int id, string name) => new(0, Vector2.zero)
        {
            EntityId = id, Name = name,
        };

        private static void AddLegacySharedDeed(NpcSaveData owner, int partnerId, int gain, float when)
        {
            owner.Deeds.Add(new NpcDeedEntry(NpcDeedKind.PartyAffinityChanged, -1, NpcKnowledgeLevel.Did, when)
            {
                AffinityDelta = gain, Participants = new List<int> { owner.EntityId, partnerId },
            });
        }

        private sealed class SavedNpcSlot
        {
            public string ManifestPath = string.Empty;
            public string ManifestJson = string.Empty;
            public SaveManifest Manifest = null!;
            public Dictionary<int, NpcSaveData> Npcs = new();
            public List<NpcAffinityEntry> Affinities = new();
        }

        /// <summary>현재 슬롯의 NPC 상태 샤드와 호감도 파티션을 메모리로 읽는다. 파일은 읽기만 한다. 슬롯이 없으면 null.</summary>
        private static SavedNpcSlot? ReadSavedNpcSlot()
        {
            string slot = Path.Combine(Application.persistentDataPath, "Saved", "Slot_0");
            string manifestPath = Path.Combine(slot, "manifest.json");
            if (File.Exists(manifestPath) == false)
                return null;
            SavedNpcSlot result = new() { ManifestPath = manifestPath, ManifestJson = File.ReadAllText(manifestPath) };
            result.Manifest = JsonConvert.DeserializeObject<SaveManifest>(result.ManifestJson, JsonSettings.Default)!;
            foreach (var part in result.Manifest.Parts)
            {
                if (part.Key.StartsWith("npc_state_", StringComparison.Ordinal) == false && part.Key != "npc_social")
                    continue;
                string json = File.ReadAllText(Path.Combine(slot, "parts", part.Value.FileName));
                if (part.Key == "npc_social")
                    result.Affinities = JsonConvert.DeserializeObject<NpcSocialSaveRoot>(json, JsonSettings.Default)!.Affinities;
                else
                {
                    var shard = JsonConvert.DeserializeObject<NpcShardSaveRoot>(json, JsonSettings.Default)!;
                    foreach (var npc in shard.Npcs)
                        result.Npcs.Add(npc.Key, npc.Value);
                }
            }
            return result;
        }

        /// <summary>
        /// 읽어 온 슬롯을 진단 매니저에 올린다. 호감도를 NPC 없이 먼저 읽어 기준선이 같은 현재 직렬화기를 쓰게 하고,
        /// 이관 자체는 이후 LoadAffinity 호출이 수행한다.
        /// </summary>
        private static void AttachSavedNpcSlot(NpcManager manager, SavedNpcSlot saved)
        {
            GetField(manager, "_npcSaveDict").SetValue(manager, new Dictionary<int, NpcSaveData>());
            manager.LoadAffinity(saved.Affinities);
            GetField(manager, "_npcSaveDict").SetValue(manager, saved.Npcs);
        }

        private static long MeasureNpcSaveBytes(NpcManager manager, Dictionary<int, NpcSaveData> npcs) =>
            Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(npcs, Formatting.None, JsonSettings.Default)) +
            Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(manager.SaveAffinity(), Formatting.None, JsonSettings.Default));

        /// <summary>현재 슬롯을 메모리에 복제해 이관 크기·멱등성·양쪽 보존을 확인한다. 파일은 읽기만 한다.</summary>
        private static string MeasureSavedSharedActivityMigration(NpcManager manager)
        {
            SavedNpcSlot? saved = ReadSavedNpcSlot();
            if (saved == null)
                return "No saved slot to measure.";
            string manifestPath = saved.ManifestPath;
            string manifestJson = saved.ManifestJson;
            SaveManifest manifest = saved.Manifest;
            Dictionary<int, NpcSaveData> npcs = saved.Npcs;
            long Measure() => MeasureNpcSaveBytes(manager, npcs);
            // Baseline uses the same current serializer, isolating this migration's changes.
            AttachSavedNpcSlot(manager, saved);
            int beforeDeeds = npcs.Values.Sum(n => n.Deeds.Count(d => d.Kind == NpcDeedKind.PartyAffinityChanged));
            long before = Measure();
            manager.LoadAffinity(manager.SaveAffinity());
            int removed = beforeDeeds - npcs.Values.Sum(n => n.Deeds.Count(d => d.Kind == NpcDeedKind.PartyAffinityChanged));
            long after = Measure();
            if (removed != beforeDeeds || npcs.Values.Any(n => n.Deeds.Any(d => d.Kind == NpcDeedKind.PartyAffinityChanged)))
                throw new InvalidOperationException("Saved migration left unresolved party deeds.");
            string first = JsonConvert.SerializeObject(manager.SaveAffinity(), JsonSettings.Default) +
                JsonConvert.SerializeObject(npcs, JsonSettings.Default);
            manager.MigrateLegacyPartyActivities();
            string second = JsonConvert.SerializeObject(manager.SaveAffinity(), JsonSettings.Default) +
                JsonConvert.SerializeObject(npcs, JsonSettings.Default);
            if (first != second || File.ReadAllText(manifestPath) != manifestJson)
                throw new InvalidOperationException("Saved migration was not stable or the save changed during inspection.");
            int livePairs = manager.SaveAffinity().Count(e => e.SharedActivities != null);
            int archived = npcs.Values.Sum(n => n.SharedActivityMemories.Count);
            return $"Revision {manifest.Revision}: removed {removed} party deeds; live stats {livePairs} pairs, archived {archived} memories. " +
                $"NPC state + affinities JSON {before} -> {after} bytes (saved {before - after} bytes, {(before - after) / 1024f:F1} KiB).";
        }
    }
}
