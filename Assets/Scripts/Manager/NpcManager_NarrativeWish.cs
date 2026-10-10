#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Item;
using ARPG.Npc.Narrative;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 소망 (NPC_NARRATIVE_CONTENT_DESIGN §2.2 Wish + Item, 3단계 ItemGift). 호감이 30 이상인 NPC가 대화에서 소망을 털어놓으면
    /// 그 대사가 표시된 뒤 사건을 만든다. 플레이어가 선물 목록에서 그 아이템을 건네면 소모·충족·반응·의뢰 보답·행적 소문을 한 번에 저장한다.
    /// 소망은 부탁 칸을 쓰지 않는다. 같은 콘텐츠는 NPC당 한 번이고 열린 소망은 NPC당 하나다 (사용자 결정 2026-10-10).
    /// </summary>
    public partial class NpcManager
    {
        // 소망을 털어놓는 호감 (사용자 결정 2026-10-10: 친구 단계 50 대신 30). 플레이로 조정하는 시작값이다.
        public const int WishRevealAffinity = 30;
        // DungeonLayout.Hash 채널. 던전 채널(입구 101+·레이아웃·47·53·419)과 겹치지 않는다.
        private const int WishSelectChannel = 61;
        private const string TargetItemNameSlot = "{targetItemName}";

        /// <summary>이 NPC의 열린(털어놓았고 아직 채워지지 않은) 소망. 없으면 null.</summary>
        public NarrativeContentInstance? GetOpenWish(int npcId)
        {
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].Kind == NarrativeContentKind.Wish && instances[i].OwnerNpcId == npcId && instances[i].IsOpen == true)
                    return instances[i];
            return null;
        }

        /// <summary>이 아이템이 그 NPC의 열린 소망 대상인가 (선물 목록 표시·전달 분기).</summary>
        public bool IsOpenWishItem(int npcId, ItemData? item)
        {
            NarrativeContentInstance? wish = item != null && item.Quantity > 0 ? GetOpenWish(npcId) : null;
            return wish != null && wish.TargetItemId == item!.Id;
        }

        /// <summary>
        /// 소망 화제 (대화 후보 고정, 다른 화제와 번갈아 나온다). 열린 소망이 있으면 떠올리는 화제, 없으면 호감이 30 이상일 때
        /// 아직 받지 않은 소망 콘텐츠 하나를 고정 무작위로 골라 털어놓는 화제. 사건은 대사가 표시된 뒤 CommitNarrativeWishRevealAsync가 만든다.
        /// </summary>
        public DialogueTopic? BuildNarrativeWishTopic(int npcId, NpcSaveData save)
        {
            if (IsNpcAlive(npcId) == false)
                return null;
            NarrativeContentInstance? open = GetOpenWish(npcId);
            if (open != null)
            {
                string itemName = NpcRumorPhrasing.ItemName(open.TargetItemId);
                var reminder = new DialogueTopic
                {
                    Type = DialogueTopicType.WishReminder, Category = DialogueTopicCategory.Personal, Pinned = true,
                    BaseLine = $"{NpcRumorPhrasing.Josa(itemName, "이", "가")} 아직도 가끔 생각나는군. 혹시 보게 되면 기억해 주게.",
                    NarrativeInstanceId = open.InstanceId, NarrativeContentId = open.ContentId,
                };
                reminder.Facts.Add($"이 NPC는 플레이어에게 {itemName}을(를) 다시 보고 싶다고 털어놓았고 아직 받지 못했다.");
                return reminder;
            }
            NarrativeContentDefinition? content = SelectRevealableWish(npcId);
            if (content == null)
                return null;
            var reveal = new DialogueTopic
            {
                Type = DialogueTopicType.WishReveal, Category = DialogueTopicCategory.Personal, Pinned = true,
                BaseLine = FillWishText(content.IntroText, content.TargetId),
                NarrativeInstanceId = NarrativeContentInstance.WishId(npcId, content.ContentId), NarrativeContentId = content.ContentId,
            };
            reveal.Facts.Add($"이 NPC는 {NpcRumorPhrasing.ItemName(content.TargetId)}을(를) 다시 보고 싶어 한다.");
            return reveal;
        }

        /// <summary>
        /// 소망을 털어놓는 대사가 올바른 화면에 실제로 표시된 뒤 부른다 (§2.4). 소망 사건(Revealed)을 만들고 Narrative를 저장한다
        /// (Narrative를 싣는 저장은 NPC 상태도 함께 싣는다). 실패하면 사건을 지운다.
        /// </summary>
        public async Task<bool> CommitNarrativeWishRevealAsync(int npcId, int contentId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            if (npc == null || _narrativeBusy == true || IsWishRevealable(npcId, contentId) == false)
                return false;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            NarrativeContentInstance? created = null;
            bool addedDefinition = false;
            bool addedPolicy = false;
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime || IsWishRevealable(npcId, contentId) == false) return false;
                NarrativeDefinitionSnapshot? definition = AR.s.Data.NarrativeCatalog.CopyWish(contentId);
                NarrativePolicySnapshot? policy = AR.s.Data.NarrativeCatalog.CopyCurrentPolicy();
                if (definition == null || policy == null) return false;
                float now = AR.s.Time.CurrentGameTime;
                created = new NarrativeContentInstance
                {
                    InstanceId = NarrativeContentInstance.WishId(npcId, contentId),
                    ContentId = contentId,
                    Kind = NarrativeContentKind.Wish,
                    DefinitionHash = definition.Hash,
                    PolicyHash = policy.Hash,
                    CreatedGameTime = now,
                    VillageId = npc.VillageId,
                    TargetItemId = definition.Content.TargetId,
                    OwnerNpcId = npcId,
                    RequesterNpcId = npcId,
                    RequestedGameTime = now,
                    WishState = WishState.Revealed,
                };
                AddNarrativeInstance(created, definition, policy, out addedDefinition, out addedPolicy);
                committed = await AR.s.Data.SaveAsync(SaveScope.Narrative);
                return committed && lifetime == _narrative.Lifetime;
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (created != null && committed == false)
                        RemoveNarrativeInstance(created, addedDefinition, addedPolicy);
                    else if (committed == true)
                        _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
        }

        /// <summary>
        /// 선물 목록에서 소망 아이템을 골랐다 (§2.2·§4.5·§7.5). 아이템 1개를 소모하고 소망을 채운 뒤, 반영 전 관계로 주인의 반응을 고르고
        /// 관계 변화(수준 × 부탁한 사람 ×3)·의뢰 보답·주인의 행적 소문(반응함)을 한 번에 저장한다. 실패하면 아이템까지 모두 되돌린다.
        /// 일반 선물 효과(가격별 호감)는 주지 않는다 (사용자 결정 2026-10-10).
        /// </summary>
        public async Task<NarrativeDialogueResult?> DeliverWishItemAsync(int npcId, int slotIndex)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            if (npc == null || _narrativeBusy == true || AR.s.Player == null)
                return null;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            PlayerData player = AR.s.Data.Player;
            Inventory inventory = AR.s.Player.Inventory;
            NarrativeContentInstance? instance = null;
            NarrativeContentInstance? instanceBefore = null;
            PlayerRelation relationBefore = default;
            bool metBefore = false;
            List<NpcRumor>? rumorsBefore = null;
            ItemData? removed = null;
            int quantityBefore = 0;
            int reward = 0;
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime || IsNpcAlive(npcId) == false) return null;
                instance = GetOpenWish(npcId);
                ItemData? item = inventory.GetItemBySlotIndex(slotIndex);
                if (instance == null || item == null || item.Id != instance.TargetItemId || item.Quantity <= 0) return null;
                NarrativePolicySnapshot? policy = GetNarrativePolicy(instance);
                if (policy == null) return null;
                relationBefore = GetPlayerRelation(npcId);
                metBefore = HasMetPlayer(npcId);
                var input = new NarrativeReactionRules.Input(npc.NpcStat, relationBefore, IsGloryAmbition(npc));
                NarrativeReactionDefinition? rule = NarrativeReactionRules.Select(policy.Reactions, PlayerDeedKind.WishFulfilled, true, input);
                if (rule == null) return null;
                NarrativeReactionRules.RelationChange(rule.RelationLevel, NarrativeRelevance.Requester, out int affinity, out int trust, out int intimacy);
                affinity = NarrativeReactionRules.LimitConversationAffinity(affinity);
                // 긍정 반응이면 항상, 부정 반응이면 약속 점수 0 이상일 때만 준다 (§4.5).
                bool refused = NarrativeReactionRules.IsNegative(rule.Kind) == true &&
                               NarrativeReactionRules.PromiseScore(npc.NpcStat, relationBefore) < 0;
                if (refused == false)
                    reward = NarrativeReactionRules.RewardAmount(instance.InstanceId);

                // 아이템 소모는 필수 도메인 처리다. 무보답 반응을 골라도 충족은 취소되지 않는다 (§4.5).
                quantityBefore = item.Quantity;
                if (inventory.RemoveItem(slotIndex, 1, out _) == false) return null;
                removed = item;
                instanceBefore = instance.Clone();
                rumorsBefore = npc.Rumors;
                float now = AR.s.Time.CurrentGameTime;
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Progress | NarrativeSaveGroup.Reports | NarrativeSaveGroup.Rewards);
                instance.WishState = WishState.Fulfilled;
                instance.ProgressGameTime = now;
                instance.Revision++;
                instance.Reported = true;
                instance.ReportNpcId = npcId;
                instance.ReportRelevance = NarrativeRelevance.Requester;
                instance.ReactionRuleId = rule.ReactionRuleId;
                instance.Reaction = rule.Kind;
                instance.Response = NarrativeReactionRules.EffectiveResponse(rule.Response);
                instance.AffinityDelta = affinity;
                instance.TrustDelta = trust;
                instance.IntimacyDelta = intimacy;
                instance.ReportGameTime = now;
                instance.ReportDisplayed = false;
                instance.RewardGold = reward;
                instance.RewardRefused = refused;
                if (affinity != 0 || trust != 0 || intimacy != 0)
                    ModifyPlayerRelation(npcId, affinity, trust, 0, intimacy);
                if (reward > 0)
                {
                    AR.s.Data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Progress);
                    player.Gold = (int)Math.Min(int.MaxValue, (long)player.Gold + reward);
                }
                // 주인이 행적을 알고 이미 반응했으므로 "반응함"으로 넣는다. 소문 교환으로 같은 마을에 퍼진다 (§4.1).
                PutReactedDeedRumor(npc, CreateDeedRumor(PlayerDeedKind.WishFulfilled, instance.InstanceId, npc.VillageId, 0, 0,
                    now, WishDeedValue(instance), -1, true), now);

                committed = await AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Narrative | SaveScope.NpcState);
                if (committed == false || lifetime != _narrative.Lifetime) return null;
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (instanceBefore != null && committed == false)
                    {
                        ReplaceNarrativeInstance(instanceBefore);
                        RestorePlayerRelation(npcId, relationBefore, metBefore);
                        if (reward > 0)
                        {
                            AR.s.Data.MarkPlayerSaveDirty(player, PlayerSaveGroup.Progress);
                            player.Gold -= reward;
                        }
                        MarkSaveDirty(npcId, NpcSaveGroup.Rumors);
                        npc.Rumors = rumorsBefore!;
                    }
                    if (removed != null && committed == false && inventory.RestoreItem(slotIndex, removed, quantityBefore) == false)
                        Debug.LogError($"[Narrative] Wish item {removed.Id} could not be returned to the inventory after a failed save.");
                    _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
            return new NarrativeDialogueResult { Kind = NarrativeConversationKind.WishDelivery, InstanceId = instance!.InstanceId,
                Text = BuildWishText(npc, instance), NeedsDisplayCommit = true };
        }

        /// <summary>소망 주인이 죽으면 열린 소망은 실패로 끝난다 (QUEST §2.2 Failed). 보답·전파는 없고 사망 체크포인트와 함께 저장된다.</summary>
        private void FailNarrativeWishesOnDeath(int entityId)
        {
            float now = AR.s.Time != null ? AR.s.Time.CurrentGameTime : 0f;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (instance.Kind != NarrativeContentKind.Wish || instance.OwnerNpcId != entityId || instance.IsOpen == false)
                    continue;
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Progress);
                instance.WishState = WishState.Failed;
                instance.ProgressGameTime = now;
                instance.Revision++;
            }
        }

        // 털어놓을 수 있는가: 살아 있고, 호감 30 이상, 열린 소망이 없고, 이 콘텐츠를 아직 받지 않았고, 현재 팩에 그 콘텐츠가 있다.
        private bool IsWishRevealable(int npcId, int contentId)
        {
            if (IsNpcAlive(npcId) == false || GetPlayerRelation(npcId).Affinity < WishRevealAffinity || GetOpenWish(npcId) != null ||
                _narrativeInstances.ContainsKey(NarrativeContentInstance.WishId(npcId, contentId)) == true)
                return false;
            NarrativeContentCatalog catalog = AR.s.Data.NarrativeCatalog;
            for (int i = 0; i < catalog.WishCount; i++)
                if (catalog.GetWishContent(i).ContentId == contentId)
                    return true;
            return false;
        }

        // 아직 받지 않은 소망 콘텐츠 중 월드 시드 + NPC ID의 고정 무작위로 하나. 호감·열린 소망 조건을 못 넘으면 null.
        private NarrativeContentDefinition? SelectRevealableWish(int npcId)
        {
            if (GetPlayerRelation(npcId).Affinity < WishRevealAffinity || GetOpenWish(npcId) != null)
                return null;
            NarrativeContentCatalog catalog = AR.s.Data.NarrativeCatalog;
            int available = 0;
            for (int i = 0; i < catalog.WishCount; i++)
                if (_narrativeInstances.ContainsKey(NarrativeContentInstance.WishId(npcId, catalog.GetWishContent(i).ContentId)) == false)
                    available++;
            if (available == 0)
                return null;
            int pick = (int)(DungeonLayout.Hash(AR.s.Data.WorldSeed, npcId, 0, 0, WishSelectChannel) % (uint)available);
            for (int i = 0; i < catalog.WishCount; i++)
            {
                NarrativeContentDefinition content = catalog.GetWishContent(i);
                if (_narrativeInstances.ContainsKey(NarrativeContentInstance.WishId(npcId, content.ContentId)) == true)
                    continue;
                if (pick == 0)
                    return content;
                pick--;
            }
            return null;
        }

        private string BuildWishText(NpcSaveData npc, NarrativeContentInstance instance)
        {
            NarrativeContentDefinition? content = GetNarrativeContent(instance);
            string itemName = NpcRumorPhrasing.ItemName(instance.TargetItemId);
            var text = new StringBuilder();
            text.Append("플레이어: 찾던 ").Append(NpcRumorPhrasing.Josa(itemName, "을", "를")).Append(" 가져왔소.");
            text.Append('\n').Append(ResolveNpcDisplayName(npc)).Append(": ");
            if (content != null)
                text.Append(FillWishText(content.ResolvedText, instance.TargetItemId)).Append(' ');
            text.Append(FindReactionText(GetNarrativePolicy(instance), instance.ReactionRuleId));
            AppendRewardLine(text, instance);
            AppendRelationLine(text, instance.AffinityDelta, instance.TrustDelta, instance.IntimacyDelta);
            return text.ToString();
        }

        private static string FillWishText(string text, int itemId) => text.Replace(TargetItemNameSlot, NpcRumorPhrasing.ItemName(itemId));

        // 소망 행적 소문의 가치 = 소망 아이템 가격 (던전 행적의 완료 골드에 해당).
        private static int WishDeedValue(NarrativeContentInstance instance)
        {
            Tables.ItemTable? table = AR.s.Data.GetItem(instance.TargetItemId);
            return table != null ? table.BasePrice : 0;
        }
    }
}
