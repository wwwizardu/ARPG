#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Dungeon;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// narrative 파트 로드 검증 (NPC_NARRATIVE_CONTENT_DESIGN §7.3). 파트 안의 정합성만 본다.
    /// 실패하면 manifest 로드가 실패해 이전 manifest로 돌아간다. NPC·던전과의 교차 상태는 로드 뒤 원장이 정리한다.
    /// </summary>
    public static class NarrativeSaveValidation
    {
        public static bool IsConsistent(NarrativeSaveRoot? root, int worldSeed)
        {
            if (root == null || root.Version != 1 || root.Instances == null || root.Definitions == null ||
                root.Policies == null || root.Selections == null)
                return false;
            var definitions = new Dictionary<string, NarrativeDefinitionSnapshot>();
            for (int i = 0; i < root.Definitions.Count; i++)
            {
                NarrativeDefinitionSnapshot snapshot = root.Definitions[i];
                if (NarrativeContentCatalog.IsSupported(snapshot) == false || definitions.TryAdd(snapshot.Hash, snapshot) == false)
                    return false;
            }
            var policies = new HashSet<string>();
            for (int i = 0; i < root.Policies.Count; i++)
            {
                NarrativePolicySnapshot policy = root.Policies[i];
                if (NarrativeContentCatalog.IsSupported(policy) == false || policies.Add(policy.Hash) == false)
                    return false;
            }
            var instances = new Dictionary<string, NarrativeContentInstance>();
            for (int i = 0; i < root.Instances.Count; i++)
            {
                NarrativeContentInstance instance = root.Instances[i];
                if (instance == null || string.IsNullOrEmpty(instance.InstanceId) == true ||
                    instances.TryAdd(instance.InstanceId, instance) == false || IsInstanceConsistent(instance, definitions, policies, worldSeed) == false)
                    return false;
            }
            var selections = new HashSet<string>();
            for (int i = 0; i < root.Selections.Count; i++)
            {
                NarrativeSelectionRecord selection = root.Selections[i];
                if (selection == null || selection.SlotIndex < 0 || selection.Generation < 0 ||
                    selection.DungeonId != DungeonLayout.Id(worldSeed, selection.VillageId, selection.SlotIndex, selection.Generation) ||
                    selections.Add(selection.DungeonId) == false || selection.InstanceId == null)
                    return false;
                if (selection.InstanceId.Length > 0 &&
                    (instances.TryGetValue(selection.InstanceId, out NarrativeContentInstance? linked) == false || linked.DungeonId != selection.DungeonId))
                    return false;
            }
            return true;
        }

        private static bool IsInstanceConsistent(NarrativeContentInstance instance,
            Dictionary<string, NarrativeDefinitionSnapshot> definitions, HashSet<string> policies, int worldSeed)
        {
            if (definitions.TryGetValue(instance.DefinitionHash, out NarrativeDefinitionSnapshot? definition) == false ||
                definition.Content.ContentId != instance.ContentId || definition.Content.Kind != instance.Kind ||
                policies.Contains(instance.PolicyHash) == false)
                return false;
            if (instance.Kind == NarrativeContentKind.Wish)
                return IsWishConsistent(instance, definition.Content);
            if (instance.Kind != NarrativeContentKind.DungeonRumor || instance.WishState != WishState.Hidden || instance.TargetItemId != 0)
                return false;
            if (instance.SlotIndex < 0 || instance.Generation < 0 || instance.Revision < 0 ||
                instance.DungeonId != DungeonLayout.Id(worldSeed, instance.VillageId, instance.SlotIndex, instance.Generation) ||
                instance.InstanceId != NarrativeContentInstance.DungeonRumorId(instance.DungeonId) ||
                instance.TargetMonsterId != definition.Content.TargetId)
                return false;
            // 부탁은 소문 주인, 또는 후속 소개로 이 소문을 소개한 NPC가 한다 (§4.1·§4.6). 소개 기록은 부탁한 NPC가 있을 때만 있다.
            bool introduced = string.IsNullOrEmpty(instance.IntroducedFromInstanceId) == false;
            if ((instance.RequesterNpcId >= 0 && instance.RequesterNpcId != instance.OwnerNpcId && introduced == false) ||
                (introduced == true && (instance.RequesterNpcId < 0 || instance.IntroducedFromInstanceId == instance.InstanceId)))
                return false;
            if (IsFollowUpConsistent(instance) == false)
                return false;
            if (instance.Ended == true && instance.Progress != NarrativeRumorProgress.Unresolved)
                return false;
            if (instance.EndNoticePending == true && (instance.Ended == false || instance.RequesterNpcId < 0))
                return false;
            if (instance.Reported == true)
            {
                if (instance.Progress != NarrativeRumorProgress.Resolved || instance.ReportNpcId < 0 ||
                    (instance.ReportRelevance != NarrativeRelevance.Requester && instance.ReportRelevance != NarrativeRelevance.SameVillage))
                    return false;
                // 의뢰 보답은 부탁한 사람에게 보고할 때만, 10~20골드 (§4.5)
                bool rewardRange = instance.RewardGold == 0 ||
                    (instance.RewardGold >= NarrativeReactionRules.RewardGoldMin && instance.RewardGold <= NarrativeReactionRules.RewardGoldMax);
                if (rewardRange == false || (instance.RewardGold > 0 && instance.ReportRelevance != NarrativeRelevance.Requester) ||
                    (instance.RewardRefused == true && instance.RewardGold > 0))
                    return false;
            }
            else if (instance.RewardGold != 0 || instance.RewardRefused == true || instance.ReportDisplayed == true)
                return false;
            return true;
        }

        // 후속 소개 (5단계): 상태가 있으면 소개한 소문 ID가 있고 자기 자신이 아니다. 대기(재생)는 보고 기록이 있을 때만이다.
        private static bool IsFollowUpConsistent(NarrativeContentInstance instance)
        {
            if (Enum.IsDefined(typeof(NarrativeFollowUpState), instance.FollowUpState) == false)
                return false;
            if (instance.FollowUpState == NarrativeFollowUpState.None)
                return string.IsNullOrEmpty(instance.FollowUpInstanceId) == true;
            if (string.IsNullOrEmpty(instance.FollowUpInstanceId) == true || instance.FollowUpInstanceId == instance.InstanceId)
                return false;
            return instance.FollowUpState != NarrativeFollowUpState.Pending || instance.Reported == true;
        }

        // 소망 (3단계): 털어놓은 주인 = 부탁한 사람, 던전 칸은 비어 있고, 충족(전달)과 보고 기록이 함께 있다.
        private static bool IsWishConsistent(NarrativeContentInstance instance, NarrativeContentDefinition content)
        {
            if (instance.OwnerNpcId < 0 || instance.RequesterNpcId != instance.OwnerNpcId ||
                instance.InstanceId != NarrativeContentInstance.WishId(instance.OwnerNpcId, instance.ContentId) ||
                instance.TargetItemId != content.TargetId || string.IsNullOrEmpty(instance.DungeonId) == false ||
                instance.SlotIndex != 0 || instance.Generation != 0 || instance.TargetMonsterId != 0 || instance.HasEntranceContext == true ||
                instance.Progress != NarrativeRumorProgress.Unresolved || instance.Ended == true || instance.EndNoticePending == true ||
                instance.Revision < 0 || string.IsNullOrEmpty(instance.IntroducedFromInstanceId) == false || IsFollowUpConsistent(instance) == false)
                return false;
            if (instance.WishState != WishState.Revealed && instance.WishState != WishState.Fulfilled && instance.WishState != WishState.Failed)
                return false;
            if (instance.Reported != (instance.WishState == WishState.Fulfilled))
                return false;
            if (instance.Reported == true)
            {
                bool rewardRange = instance.RewardGold == 0 ||
                    (instance.RewardGold >= NarrativeReactionRules.RewardGoldMin && instance.RewardGold <= NarrativeReactionRules.RewardGoldMax);
                return instance.ReportNpcId == instance.OwnerNpcId && instance.ReportRelevance == NarrativeRelevance.Requester &&
                       rewardRange == true && (instance.RewardRefused == false || instance.RewardGold == 0);
            }
            return instance.RewardGold == 0 && instance.RewardRefused == false && instance.ReportDisplayed == false;
        }
    }
}
