#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Utility;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    public static partial class NpcAmbitionSimDiagnostics
    {
        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim/Validate Personal Stories")]
        private static void ValidatePersonalStoriesMenu() => Debug.Log(RunPersonalStoryDiagnostics());

        /// <summary>종류별 최신 슬롯·이관·저장·사연 후보·캐시를 복제 데이터로 검증한다.</summary>
        public static string RunPersonalStoryDiagnostics()
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            GameObject fixture = new("PersonalStoryDiagnostics") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                NpcManager manager = fixture.AddComponent<NpcManager>();
                NpcSaveData npc = NewSharedActivityNpc(1, "A");
                var npcs = new Dictionary<int, NpcSaveData>
                {
                    { 1, npc }, { 2, NewSharedActivityNpc(2, "B") }, { 3, NewSharedActivityNpc(3, "C") },
                };
                GetField(manager, "_npcSaveDict").SetValue(manager, npcs);
                int checks = 0;
                void Require(bool passed, string message)
                {
                    if (passed == false)
                        throw new InvalidOperationException("Personal stories: " + message);
                    checks++;
                }
                List<DialogueTopic> Topics(NpcSaveData owner)
                {
                    List<DialogueTopic> result = new();
                    DialogueTopicSelector.CollectPersonalTopics(result, owner.EntityId, owner, manager, 100f);
                    return result;
                }
                Require(JsonConvert.SerializeObject(npc, JsonSettings.Default).Contains("PersonalStories") == false,
                    "empty NPC adds no story save field");
                Require(Topics(npc).Count == 0, "empty NPC adds no personal candidates");
                NpcDeedEntry Deed(NpcDeedKind kind, float time, string name = "") => new(kind, -1, NpcKnowledgeLevel.Did, time)
                {
                    ItemId = 10, ItemDisplayName = name,
                };
                npc.Deeds.Add(Deed(NpcDeedKind.ItemStolen, 5f, "옛 물건"));
                npc.Deeds.Add(Deed(NpcDeedKind.NotableItemAcquired, 8f, "새 검"));
                npc.Deeds.Add(Deed(NpcDeedKind.DungeonCleared, 4f));
                npc.Deeds.Add(Deed(NpcDeedKind.TheftDiscovered, 7f, "잃어버린 검"));
                npc.Deeds.Add(Deed(NpcDeedKind.NotableItemAcquired, 2f, "옛 검"));
                npc.Deeds.Add(Deed(NpcDeedKind.CaughtThiefInAct, 6f, "지킨 물건"));
                npc.Deeds.Add(Deed(NpcDeedKind.Fell, 1f));
                npc.Deeds.Add(Deed(NpcDeedKind.DuelWon, 3f));
                npc.Deeds.Add(Deed(NpcDeedKind.SevereWound, 9f));
                npc.WoundCount = 2;
                npc.LastWoundGameTime = 9f;
                npc.LastWoundActivity = NpcActivityKind.Hunt;
                npc.DeceasedRelations.Add(new(80, "최근 고인", NpcRelationType.Friend, true, -1, 11f));
                npc.DeceasedRelations.Add(new(81, "예전 고인", NpcRelationType.Friend, true, -1, 10f));
                Require(manager.LoadAffinity(new List<NpcAffinityEntry>()), "migration requests a save");
                Require(npc.Deeds.Count == 6 && npc.Deeds.All(d => NpcSaveData.IsTheftVictimStory(d.Kind) == false),
                    "three victim episodes become one bounded story");
                Require(npc.PersonalStories!.TheftVictim!.When == 7f, "latest victim chosen by time, not list order");
                Require(npc.GetLatestPersonalDeed(NpcDeedKind.NotableItemAcquired)!.ItemDisplayName == "새 검",
                    "latest item reference survives compaction");
                Require(npc.GetLatestPersonalDeed(NpcDeedKind.DungeonCleared)!.When == 4f,
                    "clear remains a separate latest slot");
                Require(npc.Deeds.Count(d => d.Kind == NpcDeedKind.NotableItemAcquired) == 2 &&
                    npc.Deeds.Any(d => d.Kind == NpcDeedKind.Fell) && npc.Deeds.Any(d => d.Kind == NpcDeedKind.DuelWon),
                    "important item, death and duel histories preserved");
                Require(npc.GetLatestLoss()!.DeceasedNpcEntityId == 80 && npc.DeceasedRelations.Count == 2,
                    "latest loss by time, full loss memories preserved");
                Require(npc.PersonalStories.LastWoundSevere, "legacy latest severe wound restored once");
                List<DialogueTopic> first = Topics(npc);
                Require(first.Count == 3 && first[0].Type == DialogueTopicType.RecentHunt &&
                    first[1].Type == DialogueTopicType.Boast && first[2].Type == DialogueTopicType.LostCompanion,
                    "latest two candidates plus loss keep prior topic budget");
                Require(first[0].BaseLine.Contains("죽다 살아") && first[1].BaseLine.Contains("새 검"),
                    "latest slots produce correct wound and item wording");
                string migrated = JsonConvert.SerializeObject(npc, JsonSettings.Default);
                Require(manager.LoadAffinity(manager.SaveAffinity()) == false &&
                    JsonConvert.SerializeObject(npc, JsonSettings.Default) == migrated, "migration is idempotent");

                manager.Narrative.SetDialogue(1, AffinityTier.Stranger, GreetingEncounter.Regular, DialogueTopicType.Boast, 0, "old");
                manager.AddNotableItemDeed(1, 11, "최신 도끼", 15f);
                Require(manager.Narrative.TryGetDialogue(1, AffinityTier.Stranger, GreetingEncounter.Regular, DialogueTopicType.Boast, 0, out _) == false,
                    "new personal episode invalidates dialogue cache");
                DialogueTopic newBoast = Topics(npc).First(t => t.Type == DialogueTopicType.Boast);
                Require(newBoast.BaseLine.Contains("최신 도끼") && newBoast.CacheVariant != first[1].CacheVariant,
                    "new item changes story and cache variant");
                manager.AddNotableItemDeed(1, 12, "늦게 들어온 옛 기록", 12f);
                Require(npc.GetLatestPersonalDeed(NpcDeedKind.NotableItemAcquired)!.ItemId == 11,
                    "out of order events do not replace a newer story");
                for (int i = 0; i < 1000; i++)
                    npc.RecordDeed(Deed(NpcDeedKind.ItemStolen, 20f + i, "물건"));
                Require(npc.Deeds.Count == 8 && npc.PersonalStories.TheftVictim!.When == 1019f,
                    "one thousand repeated thefts keep one story and no history growth");
                npc.RecordDeed(Deed(NpcDeedKind.ItemStolen, 1f, "오래된 도난"));
                Require(npc.PersonalStories.TheftVictim.When == 1019f, "older theft cannot replace latest");
                npc.RecordExpeditionWound(1020f, NpcActivityKind.DungeonDelve, false);
                Require(npc.PersonalStories.LastWoundSevere == false && Topics(npc)[0].BaseLine.Contains("던전 원정"),
                    "light wound replaces severe story without scanning severe history");
                int historyCount = npc.Deeds.Count;
                for (int i = 0; i < 10000; i++)
                    npc.Deeds.Add(Deed(NpcDeedKind.DuelDrawn, 2000f + i));
                Require(Topics(npc).Any(t => t.Type == DialogueTopicType.TheftVictim) && npc.Deeds.Count == historyCount + 10000,
                    "unrelated long history does not displace stored personal stories");
                manager.ApplyPartyRelationshipOutcome(new[] { 1, 3, 2 },
                    new[] { NpcHuntOutcome.Success, NpcHuntOutcome.Success, NpcHuntOutcome.Success }, true, 1021f,
                    NpcActivityKind.Hunt);
                Require(manager.TryGetLatestSharedActivity(1, out int partner, out _, out _) && partner == 2,
                    "same time companion uses deterministic lower ID");
                Require(Topics(npc)[0].Type == DialogueTopicType.PartyBond, "companion slot participates in latest sorting");
                npcs[2].Condition = CharacterConditions.Dead;
                manager.RemoveAllAffinityInvolving(2);
                Require(manager.TryGetLatestSharedActivity(1, out partner, out _, out NpcSharedActivityMemory? memory) &&
                    partner == 2 && memory != null, "latest companion pointer moves to archived memory on death");
                Require(Topics(npc)[0].BaseLine.Contains("기억"), "archived companion uses past tense wording");
                string json = JsonConvert.SerializeObject(npc, Formatting.None, JsonSettings.Default);
                string snapshotJson = JsonConvert.SerializeObject(new SaveDtoSnapshot().Capture(npc), Formatting.None, JsonSettings.Default);
                Require(json == snapshotJson, "background DTO preserves compact story slots");
                Require(json.Contains("NotableItemIndex") == false && json.Contains("LossIndex") == false,
                    "rebuilt story indices are not saved");
                NpcSaveData restored = JsonConvert.DeserializeObject<NpcSaveData>(json, JsonSettings.Default)!;
                restored.OnLoadCompleted();
                Require(restored.GetLatestPersonalDeed(NpcDeedKind.NotableItemAcquired)!.ItemId == 11 &&
                    restored.PersonalStories!.TheftVictim!.When == 1019f && restored.GetLatestLoss()!.DeceasedNpcEntityId == 80,
                    "story slots survive save and load");
                NpcSaveData damaged = JsonConvert.DeserializeObject<NpcSaveData>(json, JsonSettings.Default)!;
                int lossCount = damaged.DeceasedRelations.Count;
                damaged.DeceasedRelations.Add(null!);
                damaged.PersonalStories!.TheftVictim!.Participants = null!;
                damaged.OnLoadCompleted();
                Require(damaged.DeceasedRelations.Count == lossCount && damaged.PersonalStories.TheftVictim.Participants != null,
                    "damaged loss and theft entries are repaired on load");
                npcs[1] = restored;
                manager.LoadAffinity(manager.SaveAffinity());
                Require(manager.TryGetLatestSharedActivity(1, out partner, out _, out memory) && partner == 2 && memory != null,
                    "archived companion index restored after reload");
                manager.Narrative.SetDialogue(1, AffinityTier.Stranger, GreetingEncounter.Regular, DialogueTopicType.TheftVictim, 0, "old theft");
                typeof(NpcManager).GetMethod("AddTheftDeed", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.Invoke(manager, new object[]
                    { 1, NpcDeedKind.TheftDiscovered, NpcKnowledgeLevel.Heard, 3, 14, "도난 도끼", 1022f });
                Require(restored.PersonalStories!.TheftVictim!.Kind == NpcDeedKind.TheftDiscovered &&
                    restored.PersonalStories.TheftVictim.ParticipantSnapshots.Single().DisplayName == "C" &&
                    restored.Deeds.Count == npc.Deeds.Count && manager.Narrative.TryGetDialogue(1, AffinityTier.Stranger,
                        GreetingEncounter.Regular, DialogueTopicType.TheftVictim, 0, out _) == false,
                    "theft writer replaces story, preserves names and invalidates cached dialogue");
                Require(Topics(restored)[0].Type == DialogueTopicType.TheftVictim &&
                    Topics(restored)[0].BaseLine.Contains("C") && Topics(restored)[0].Facts[0].Contains("소문"),
                    "latest discovery retains knowledge and culprit facts");
                TopicAudit audit = new();
                AuditTheftVictimTopic(audit, 1, restored, "A", Topics(restored)[0]);
                Require(audit.TheftVictimDeedNpcs == 1 && audit.TheftVictimTopicNpcs == 1 && audit.TheftTopicLineFail == 0,
                    "maintained theft audit follows the latest story storage");
                return $"[PersonalStories] PASS {checks} checks. " + MeasureSavedPersonalStories(manager);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Random.state = randomState;
            }
        }

        private static string MeasureSavedPersonalStories(NpcManager manager)
        {
            SavedNpcSlot? saved = ReadSavedNpcSlot();
            if (saved == null)
                return "No saved slot to measure.";
            string path = saved.ManifestPath;
            string manifestJson = saved.ManifestJson;
            SaveManifest manifest = saved.Manifest;
            Dictionary<int, NpcSaveData> npcs = saved.Npcs;
            AttachSavedNpcSlot(manager, saved);
            long Measure() => MeasureNpcSaveBytes(manager, npcs);
            long before = Measure();
            manager.LoadAffinity(manager.SaveAffinity());
            long after = Measure();
            string first = JsonConvert.SerializeObject(npcs, JsonSettings.Default);
            RequireStablePersonalSave(manager, npcs, first);
            foreach (var npc in npcs.Values)
            {
                if (npc.GetLatestPersonalDeed(NpcDeedKind.NotableItemAcquired) != npc.Deeds
                    .Where(d => d.Kind == NpcDeedKind.NotableItemAcquired).OrderBy(d => d.When).LastOrDefault() ||
                    npc.GetLatestPersonalDeed(NpcDeedKind.DungeonCleared) != npc.Deeds
                    .Where(d => d.Kind == NpcDeedKind.DungeonCleared).OrderBy(d => d.When).LastOrDefault())
                    throw new InvalidOperationException("Saved latest deed differs from full history scan.");
                if (npc.GetLatestLoss() != npc.DeceasedRelations.OrderBy(m => m.DeathGameTime).LastOrDefault())
                    throw new InvalidOperationException("Saved latest loss differs from full memory scan.");
                NpcAffinityEntry? firstPair = manager.GetSharedActivities(npc.EntityId).FirstOrDefault();
                NpcSharedActivityStats? expected = firstPair?.SharedActivities;
                int expectedId = firstPair == null ? -1 : firstPair.NpcA == npc.EntityId ? firstPair.NpcB : firstPair.NpcA;
                foreach (var memory in npc.SharedActivityMemories)
                {
                    if (expected == null || memory.Activities.LastGameTime > expected.LastGameTime ||
                        (memory.Activities.LastGameTime == expected.LastGameTime && memory.OtherNpcId < expectedId))
                    {
                        expected = memory.Activities;
                        expectedId = memory.OtherNpcId;
                    }
                }
                bool found = manager.TryGetLatestSharedActivity(npc.EntityId, out int actualId,
                    out NpcSharedActivityStats? actual, out _);
                if (found != (expected != null) || (found && (actualId != expectedId || actual != expected)))
                    throw new InvalidOperationException("Saved latest companion differs from full relation scan.");
            }
            if (File.ReadAllText(path) != manifestJson)
                throw new InvalidOperationException("Save changed during read-only measurement.");
            return $"Revision {manifest.Revision}, {npcs.Count} NPCs: latest references match full scans; " +
                $"NPC + affinity JSON {before} -> {after} bytes ({(before - after) / 1024f:F1} KiB smaller).";
        }

        private static void RequireStablePersonalSave(NpcManager manager, Dictionary<int, NpcSaveData> npcs, string expected)
        {
            manager.LoadAffinity(manager.SaveAffinity());
            if (JsonConvert.SerializeObject(npcs, JsonSettings.Default) != expected)
                throw new InvalidOperationException("Personal story migration is not idempotent.");
        }
    }
}
