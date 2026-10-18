#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Npc.Narrative;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 말을 걸었을 때의 내러티브 처리 (NPC_NARRATIVE_CONTENT_DESIGN §4.1·§4.3·§7.5): 부탁 표시, 보고, 소문 전 해결 말하기,
    /// 미해결 종료 알림, 소문으로 들은 행적 반응. 효과가 있는 처리는 모두 참여 파트를 한 번에 저장하고, 실패하면 함께 되돌린다.
    /// 반응 선택은 이번 반응을 반영하기 전 관계로 한다. 일반 대화 보상(+1/+1)은 주지 않는다.
    /// </summary>
    public partial class NpcManager
    {
        private bool _narrativeBusy;

        public bool IsNarrativeBusy => _narrativeBusy;

        // ================= 대화 결정 =================

        /// <summary>
        /// 일반 대화보다 먼저 처리할 내러티브 대화를 고른다 (읽기 전용). 우선순위: 표시 취소된 보고 재생 → 부탁 칸 보고 →
        /// 미해결 종료 알림 → 소문 전 해결 말하기 → 소문으로 들은 행적 반응.
        /// </summary>
        public NarrativeConversation FindNarrativeConversation(int npcId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            if (npc == null || IsNpcAlive(npcId) == false || _narrativeBusy == true)
                return default;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].Reported == true && instances[i].ReportDisplayed == false && instances[i].ReportNpcId == npcId)
                    return new NarrativeConversation(NarrativeConversationKind.ReportReplay, instances[i].InstanceId);
            NarrativeContentInstance? requested = GetNarrativeInstance(npc.NarrativeRequest?.InstanceId);
            if (requested != null && requested.Progress == NarrativeRumorProgress.Resolved && requested.Reported == false)
                return new NarrativeConversation(NarrativeConversationKind.Report, requested.InstanceId);
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].EndNoticePending == true && instances[i].RequesterNpcId == npcId)
                    return new NarrativeConversation(NarrativeConversationKind.EndNotice, instances[i].InstanceId);
            for (int i = 0; i < instances.Count; i++)
                if (IsDirectTellAvailable(instances[i], npcId) == true)
                    return new NarrativeConversation(NarrativeConversationKind.DirectTell, instances[i].InstanceId);
            float now = AR.s.Time.CurrentGameTime;
            for (int i = 0; i < npc.Rumors.Count; i++)
                if (IsDeedReactionCandidate(npc, npc.Rumors[i], now) == true)
                    return new NarrativeConversation(NarrativeConversationKind.DeedReactions, string.Empty);
            return default;
        }

        /// <summary>내러티브 대화를 실행한다. 효과가 있으면 저장까지 마친 뒤 대사를 돌려준다. 실패·취소면 null.</summary>
        public async Task<NarrativeDialogueResult?> RunNarrativeConversationAsync(int npcId, NarrativeConversation conversation)
        {
            switch (conversation.Kind)
            {
                case NarrativeConversationKind.ReportReplay:
                {
                    NarrativeContentInstance? instance = GetNarrativeInstance(conversation.InstanceId);
                    NpcSaveData? npc = GetSaveData(npcId);
                    if (instance == null || npc == null || instance.Reported == false || instance.ReportNpcId != npcId)
                        return null;
                    // 표시가 취소된 보고 또는 소망 전달을 그대로 다시 보여 준다.
                    string replay = instance.Kind == NarrativeContentKind.Wish ? BuildWishText(npc, instance) : BuildReportText(npc, instance);
                    return new NarrativeDialogueResult { Kind = conversation.Kind, InstanceId = instance.InstanceId,
                        Text = replay, NeedsDisplayCommit = true };
                }
                case NarrativeConversationKind.Report:
                    return await RunReportAsync(npcId, conversation.InstanceId, true);
                case NarrativeConversationKind.DirectTell:
                    return await RunReportAsync(npcId, conversation.InstanceId, false);
                case NarrativeConversationKind.EndNotice:
                {
                    NarrativeContentInstance? instance = GetNarrativeInstance(conversation.InstanceId);
                    NpcSaveData? npc = GetSaveData(npcId);
                    if (instance == null || npc == null || instance.EndNoticePending == false || instance.RequesterNpcId != npcId)
                        return null;
                    string location = DescribeNarrativeLocation(instance, npc.VillageId);
                    string notice = instance.Kind == NarrativeContentKind.NamedThreat
                        ? NamedThreatEndNotice(instance)
                        : $"{location} 일은 다른 사람들이 처리했다더군. 신경 써 줘서 고맙네.";
                    return new NarrativeDialogueResult { Kind = conversation.Kind, InstanceId = instance.InstanceId,
                        Text = $"{ResolveNpcDisplayName(npc)}: {notice}", NeedsDisplayCommit = true };
                }
                case NarrativeConversationKind.DeedReactions:
                    return await RunDeedReactionsAsync(npcId);
            }
            return null;
        }

        /// <summary>
        /// 대사가 올바른 화면에 실제로 표시된 뒤 부른다. 보고·소망 전달은 표시 완료를, 미해결 알림은 알림 완료를 저장한다 (§7.6).
        /// 보고에 후속 소개가 붙어 있었으면 같은 저장에서 그 소문을 말한 NPC의 부탁 칸에 넣는다(대상이 그사이 무효면 취소).
        /// 소문 반응에 붙은 후속 소개도 여기서 확정한다. 실패하면 다음 대화에서 같은 보고를 다시 보여 준다(소문 반응의 소개는 사라진다).
        /// </summary>
        public async Task<bool> CommitNarrativeDisplayAsync(int npcId, NarrativeDialogueResult result)
        {
            if (result.NeedsDisplayCommit == false)
                return true;
            if (result.Kind == NarrativeConversationKind.DeedReactions)
                return await CommitDeedFollowUpAsync(npcId, result);
            NarrativeContentInstance? instance = GetNarrativeInstance(result.InstanceId);
            NpcSaveData? npc = GetSaveData(npcId);
            if (instance == null || npc == null || _narrativeBusy == true)
                return false;
            bool report = result.Kind == NarrativeConversationKind.Report || result.Kind == NarrativeConversationKind.DirectTell ||
                          result.Kind == NarrativeConversationKind.ReportReplay || result.Kind == NarrativeConversationKind.WishDelivery;
            if (report == true ? instance.ReportNpcId != npcId || instance.Reported == false : instance.RequesterNpcId != npcId)
                return false;
            if (report == true ? instance.ReportDisplayed == true : instance.EndNoticePending == false)
                return true;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            NarrativeContentInstance? before = null;
            var followUp = new FollowUpStage();
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime) return false;
                before = instance.Clone();
                MarkNarrativeInstanceDirty(instance, report == true ? NarrativeSaveGroup.Reports | NarrativeSaveGroup.Continuations : NarrativeSaveGroup.Progress);
                if (report == true)
                {
                    instance.ReportDisplayed = true;
                    if (instance.FollowUpState == NarrativeFollowUpState.Pending)
                    {
                        NarrativeContentInstance? target = GetNarrativeInstance(instance.FollowUpInstanceId);
                        if (target != null && IsFollowUpTargetValid(npc, target, instance.InstanceId, false) == true)
                            StageFollowUpRequest(npc, instance, target, followUp);
                        else
                            instance.FollowUpState = NarrativeFollowUpState.Cancelled;
                    }
                }
                else instance.EndNoticePending = false;
                committed = await AR.s.Data.SaveAsync(followUp.Target != null ? SaveScope.Narrative | SaveScope.NpcState : SaveScope.Narrative);
                return committed && lifetime == _narrative.Lifetime;
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (before != null && committed == false)
                    {
                        ReplaceNarrativeInstance(before);
                        UndoFollowUpRequest(npc, followUp);
                    }
                    else if (committed == true && followUp.Target != null)
                        _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
        }

        // ================= 후속 소개 (§4.6, 5단계) =================

        // 표시 뒤 소개 확정의 되돌리기 정보. Target이 있으면 소개한 소문과 부탁 칸을 바꿨다.
        private sealed class FollowUpStage
        {
            public NarrativeContentInstance? Target;
            public NarrativeContentInstance? TargetBefore;
            public NpcNarrativeRequest? RequestBefore;
        }

        /// <summary>
        /// 후속 소개 후보 (§4.6): 말하는 NPC가 아는(주인이거나 위치·징후를 들은) 같은 마을의 미해결·미부탁 던전 소문 중 입구가 있는 것,
        /// ID 순으로 첫 번째. 말하는 NPC의 부탁 칸이 차 있으면 없다(slotFreed = 이번 보고로 칸이 비는 경우).
        /// </summary>
        private NarrativeContentInstance? FindFollowUpTarget(NpcSaveData speaker, string sourceInstanceId, bool slotFreed)
        {
            NarrativeContentInstance? best = null;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance candidate = instances[i];
                if (IsFollowUpTargetValid(speaker, candidate, sourceInstanceId, slotFreed) == true &&
                    (best == null || string.CompareOrdinal(candidate.InstanceId, best.InstanceId) < 0))
                    best = candidate;
            }
            return best;
        }

        private bool IsFollowUpTargetValid(NpcSaveData speaker, NarrativeContentInstance target, string sourceInstanceId, bool slotFreed)
            => target.Kind == NarrativeContentKind.DungeonRumor && target.InstanceId != sourceInstanceId && target.IsOpen == true &&
               target.RequesterNpcId < 0 && target.VillageId == speaker.VillageId && (speaker.NarrativeRequest == null || slotFreed == true) &&
               IsNpcAlive(speaker.EntityId) == true && GetNarrativeContent(target) != null && KnowsRumor(speaker, target) == true &&
               AR.s.Dungeon != null && AR.s.Dungeon.TryGetNarrativeEntrance(target.DungeonId, out _, out _) == true;

        // 지식 범위 KnownToSpeaker (§4.6): 그 소문의 주인이거나, 소문 교환으로 그 사건의 위치·징후를 들었다.
        private static bool KnowsRumor(NpcSaveData speaker, NarrativeContentInstance target)
        {
            if (target.OwnerNpcId == speaker.EntityId)
                return true;
            for (int i = 0; i < speaker.Rumors.Count; i++)
                if (speaker.Rumors[i].Kind == NpcRumorKind.DungeonInfo && speaker.Rumors[i].InstanceId == target.InstanceId)
                    return true;
            return false;
        }

        // 소개 대사: 코드 기본 연결 문구 + 그 소문의 원래 부탁 대사(위치는 말하는 NPC 기준).
        private string FollowUpLine(NarrativeContentInstance target, int speakerVillageId)
        {
            NarrativeContentDefinition? content = GetNarrativeContent(target);
            return content == null
                ? string.Empty
                : " 그러고 보니 소식이 하나 더 있네. " + content.IntroText.Replace("{location}", DescribeNarrativeLocation(target, speakerVillageId));
        }

        // 소개한 소문을 말한 NPC의 부탁 칸에 넣고 소개 기록을 남긴다. 소개한 쪽 사건(source)의 사본은 호출자가 잡는다.
        private void StageFollowUpRequest(NpcSaveData npc, NarrativeContentInstance source, NarrativeContentInstance target, FollowUpStage stage)
        {
            float now = AR.s.Time.CurrentGameTime;
            stage.Target = target;
            stage.TargetBefore = target.Clone();
            stage.RequestBefore = npc.NarrativeRequest;
            MarkNarrativeInstanceDirty(target, NarrativeSaveGroup.Introductions | NarrativeSaveGroup.Target);
            target.RequesterNpcId = npc.EntityId;
            target.RequestedGameTime = now;
            target.IntroducedFromInstanceId = source.InstanceId;
            if (AR.s.Dungeon.TryGetNarrativeEntrance(target.DungeonId, out int x, out int y) == true)
            {
                target.HasEntranceContext = true;
                target.EntranceX = x;
                target.EntranceY = y;
            }
            MarkSaveDirty(npc.EntityId, NpcSaveGroup.NarrativeRequest);
            npc.NarrativeRequest = new NpcNarrativeRequest { InstanceId = target.InstanceId, RequestedGameTime = now };
            source.FollowUpInstanceId = target.InstanceId;
            source.FollowUpState = NarrativeFollowUpState.Introduced;
        }

        private void UndoFollowUpRequest(NpcSaveData npc, FollowUpStage stage)
        {
            if (stage.TargetBefore == null)
                return;
            ReplaceNarrativeInstance(stage.TargetBefore);
            MarkSaveDirty(npc.EntityId, NpcSaveGroup.NarrativeRequest);
            npc.NarrativeRequest = stage.RequestBefore;
        }

        /// <summary>
        /// 소문 반응 대사에 붙인 후속 소개를 표시 뒤 확정한다. 그 행적(source)이 아직 아무 소개도 하지 않았고 대상이 여전히 유효할 때만,
        /// 대상 소문을 그 NPC의 부탁 칸에 넣고 행적에 소개 완료를 기록해 Narrative + NPC 상태를 함께 저장한다. 실패하면 둘 다 되돌린다.
        /// </summary>
        private async Task<bool> CommitDeedFollowUpAsync(int npcId, NarrativeDialogueResult result)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            NarrativeContentInstance? source = GetNarrativeInstance(result.InstanceId);
            NarrativeContentInstance? target = GetNarrativeInstance(result.FollowUpInstanceId);
            if (npc == null || source == null || target == null || _narrativeBusy == true)
                return false;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            NarrativeContentInstance? sourceBefore = null;
            var followUp = new FollowUpStage();
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime || source.FollowUpState != NarrativeFollowUpState.None ||
                    IsFollowUpTargetValid(npc, target, source.InstanceId, false) == false)
                    return false;
                sourceBefore = source.Clone();
                MarkNarrativeInstanceDirty(source, NarrativeSaveGroup.Continuations);
                StageFollowUpRequest(npc, source, target, followUp);
                committed = await AR.s.Data.SaveAsync(SaveScope.Narrative | SaveScope.NpcState);
                return committed && lifetime == _narrative.Lifetime;
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (sourceBefore != null && committed == false)
                    {
                        ReplaceNarrativeInstance(sourceBefore);
                        UndoFollowUpRequest(npc, followUp);
                    }
                    else if (committed == true)
                        _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
        }

        // ================= 부탁 (§4.1 부탁 칸) =================

        /// <summary>
        /// 부탁 화제 또는 진행 확인 화제 (대화 후보 고정, 기존 순환으로 다른 화제와 번갈아 나온다).
        /// 칸이 찬 동안에는 진행 확인만, 칸이 비었으면 이 NPC가 주인인 미해결 소문 하나의 부탁을 만든다.
        /// </summary>
        public DialogueTopic? BuildNarrativeRequestTopic(int npcId, NpcSaveData save)
        {
            if (IsNpcAlive(npcId) == false)
                return null;
            if (save.NarrativeRequest != null)
            {
                NarrativeContentInstance? held = GetNarrativeInstance(save.NarrativeRequest.InstanceId);
                if (held == null || held.IsOpen == false)
                    return null;
                if (held.Kind == NarrativeContentKind.NamedThreat)
                    return BuildNamedThreatProgressTopic(save, held);
                string heldLocation = DescribeNarrativeLocation(held, save.VillageId);
                var progress = new DialogueTopic
                {
                    Type = DialogueTopicType.DungeonRequestProgress, Category = DialogueTopicCategory.Rumor, Pinned = true,
                    BaseLine = $"{heldLocation} 일은 어떻게 됐나? 아직 마음에 걸리는군.",
                    NarrativeInstanceId = held.InstanceId,
                };
                progress.Facts.Add("이 NPC가 플레이어에게 그 던전의 일을 부탁했고 아직 해결되지 않았다.");
                return progress;
            }
            NarrativeContentInstance? best = FindRequestableInstance(npcId);
            NarrativeContentDefinition? content = GetNarrativeContent(best);
            if (best == null || content == null)
                return null;
            if (best.Kind == NarrativeContentKind.NamedThreat)
                return BuildNamedThreatRequestTopic(save, best, content);
            string location = DescribeNarrativeLocation(best, save.VillageId);
            var request = new DialogueTopic
            {
                Type = DialogueTopicType.DungeonRequest, Category = DialogueTopicCategory.Rumor, Pinned = true,
                BaseLine = content.IntroText.Replace("{location}", location),
                NarrativeInstanceId = best.InstanceId,
            };
            request.Facts.Add("알려진 지역 관찰: " + content.SignText);
            return request;
        }

        /// <summary>그 NPC가 주인인 이 소문을 부탁·진행 확인 화제로 말하는 중인가. 그때는 같은 소문의 위치 화제를 따로 내지 않는다.</summary>
        public bool IsNarrativeOwnTopic(int npcId, NarrativeContentInstance instance)
        {
            NpcSaveData? save = GetSaveData(npcId);
            if (save == null || instance.OwnerNpcId != npcId || instance.IsOpen == false)
                return false;
            if (save.NarrativeRequest != null)
                return save.NarrativeRequest.InstanceId == instance.InstanceId;
            return FindRequestableInstance(npcId) == instance;
        }

        /// <summary>
        /// 부탁 대사가 올바른 화면에 실제로 표시된 뒤 부른다 (§2.4). 그 소문을 부탁 칸에 넣고 사건에 부탁한 NPC를 기록해
        /// Narrative + NPC 상태를 함께 저장한다. 실패하면 둘 다 되돌린다.
        /// </summary>
        public async Task<bool> CommitNarrativeRequestAsync(int npcId, string instanceId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            NarrativeContentInstance? instance = GetNarrativeInstance(instanceId);
            if (npc == null || instance == null || _narrativeBusy == true || IsRequestable(npcId, npc, instance) == false)
                return false;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            NarrativeContentInstance? before = null;
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime || IsRequestable(npcId, npc, instance) == false) return false;
                before = instance.Clone();
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Introductions | NarrativeSaveGroup.Target);
                MarkSaveDirty(npcId, NpcSaveGroup.NarrativeRequest);
                float now = AR.s.Time.CurrentGameTime;
                instance.RequesterNpcId = npcId;
                instance.RequestedGameTime = now;
                if (AR.s.Dungeon.TryGetNarrativeEntrance(instance.DungeonId, out int x, out int y) == true)
                {
                    instance.HasEntranceContext = true;
                    instance.EntranceX = x;
                    instance.EntranceY = y;
                }
                npc.NarrativeRequest = new NpcNarrativeRequest { InstanceId = instance.InstanceId, RequestedGameTime = now };
                // 네임드 부탁은 칭호 공개 뒤에만 생기므로 그 기록(World)을 같은 저장에 싣는다 — 부탁 칸만 남고 공개가 빠지지 않게
                committed = await AR.s.Data.SaveAsync(instance.Kind == NarrativeContentKind.NamedThreat
                    ? SaveScope.Narrative | SaveScope.NpcState | SaveScope.World
                    : SaveScope.Narrative | SaveScope.NpcState);
                return committed && lifetime == _narrative.Lifetime;
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (before != null && committed == false)
                    {
                        ReplaceNarrativeInstance(before);
                        MarkSaveDirty(npcId, NpcSaveGroup.NarrativeRequest);
                        npc.NarrativeRequest = null;
                    }
                    else if (committed == true)
                        _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
        }

        private NarrativeContentInstance? FindRequestableInstance(int npcId)
        {
            NpcSaveData? save = GetSaveData(npcId);
            if (save == null || save.NarrativeRequest != null)
                return null;
            NarrativeContentInstance? best = null;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (IsRequestable(npcId, save, instance) == true &&
                    (best == null || string.CompareOrdinal(instance.InstanceId, best.InstanceId) < 0))
                    best = instance;
            }
            return best;
        }

        // 부탁은 소문 주인만, 칸이 비어 있고, 미해결이고, 입구가 있고, 아직 아무에게도 부탁하지 않았을 때 (§4.1).
        // 이미 해결된 소문은 새로 부탁하지 않는다 — 모든 소문은 사실이어야 한다 (D72).
        // 네임드 위협 (NAMED_THREAT_DESIGN §8.4): 입구 대신 네임드가 활성이고 칭호가 공개됐을 때(첫 마주침 뒤 — 부탁 대사가 피해를 말한다).
        private bool IsRequestable(int npcId, NpcSaveData npc, NarrativeContentInstance instance)
        {
            if (instance.OwnerNpcId != npcId || npc.NarrativeRequest != null || instance.IsOpen == false || instance.RequesterNpcId >= 0 ||
                IsNpcAlive(npcId) == false || GetNarrativeContent(instance) == null)
                return false;
            if (instance.Kind == NarrativeContentKind.NamedThreat)
            {
                NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetActive(instance.ThreatId) : null;
                return record != null && record.TitleRevealed == true;
            }
            return instance.Kind == NarrativeContentKind.DungeonRumor &&
                   AR.s.Dungeon.TryGetNarrativeEntrance(instance.DungeonId, out _, out _) == true;
        }

        // ================= 보고 (§4.1 보고와 전파·§4.5) =================

        private bool IsDirectTellAvailable(NarrativeContentInstance instance, int npcId)
            => (instance.Kind == NarrativeContentKind.DungeonRumor || instance.Kind == NarrativeContentKind.NamedThreat) &&
               instance.OwnerNpcId == npcId && instance.Progress == NarrativeRumorProgress.Resolved &&
               instance.Reported == false && instance.RequesterNpcId < 0;

        /// <summary>
        /// 부탁한 NPC에게 보고(requested) 또는 소문 전 해결을 주인에게 말하기. 반영 전 관계로 반응을 고르고, 관계 변화(수준 × 관련도),
        /// 의뢰 보답(부탁한 사람만, 부정 반응이면 약속 점수), 칸 비우기, 그 NPC의 행적 소문(반응함)을 한 번에 저장한다.
        /// </summary>
        private async Task<NarrativeDialogueResult?> RunReportAsync(int npcId, string instanceId, bool requested)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            NarrativeContentInstance? instance = GetNarrativeInstance(instanceId);
            if (npc == null || instance == null || _narrativeBusy == true || IsReportable(npcId, npc, instance, requested) == false)
                return null;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            PlayerData player = AR.s.Data.Player;
            NarrativeContentInstance? instanceBefore = null;
            PlayerRelation relationBefore = default;
            bool metBefore = false;
            NpcNarrativeRequest? requestBefore = null;
            List<NpcRumor>? rumorsBefore = null;
            int reward = 0;
            bool committed = false;
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime || IsReportable(npcId, npc, instance, requested) == false) return null;
                NarrativePolicySnapshot? policy = GetNarrativePolicy(instance);
                if (policy == null) return null;
                relationBefore = GetPlayerRelation(npcId);
                metBefore = HasMetPlayer(npcId);
                var input = new NarrativeReactionRules.Input(npc.NpcStat, relationBefore, IsGloryAmbition(npc));
                // 보고·소문 전 해결 말하기는 소문 주인이 직접 반응한다 (Audience Owner·All 행).
                bool namedThreat = instance.Kind == NarrativeContentKind.NamedThreat;
                PlayerDeedKind deedKind = namedThreat == true ? PlayerDeedKind.ThreatSlain : PlayerDeedKind.RumorResolved;
                NarrativeReactionDefinition? rule = NarrativeReactionRules.Select(policy.Reactions, deedKind, true, input);
                if (rule == null) return null;
                NarrativeRelevance relevance = requested == true ? NarrativeRelevance.Requester : NarrativeRelevance.SameVillage;
                NarrativeReactionRules.RelationChange(rule.RelationLevel, relevance, out int affinity, out int trust, out int intimacy);
                affinity = NarrativeReactionRules.LimitConversationAffinity(affinity);
                bool refused = false;
                if (requested == true)
                {
                    // 긍정 반응이면 항상, 부정 반응이면 약속 점수 0 이상일 때만 준다. 대신 주는 NPC는 없다 (§4.5).
                    if (NarrativeReactionRules.IsNegative(rule.Kind) == true &&
                        NarrativeReactionRules.PromiseScore(npc.NpcStat, relationBefore) < 0)
                        refused = true;
                    else
                        reward = namedThreat == true
                            ? NamedThreat.NamedThreatRules.GetRequestReward(instance.InstanceId)   // 기존 보답의 3배 (Q5)
                            : NarrativeReactionRules.RewardAmount(instance.InstanceId);
                }

                // 후속 소개 (§4.6): "아는 다른 소문 소개"를 골랐고 이 행적이 아직 소개하지 않았으면, 이 NPC가 아는 다른 소문을 고른다.
                // 보고로 부탁 칸이 비므로(requested) 그 칸을 쓸 수 있다. 실제 부탁 칸 넣기는 대사가 표시된 뒤 한다(대기 상태로 재생).
                NarrativeContentInstance? followUp = rule.Response == NarrativeResponseKind.IntroduceKnownContent &&
                                                     instance.FollowUpState == NarrativeFollowUpState.None
                    ? FindFollowUpTarget(npc, instance.InstanceId, requested)
                    : null;

                instanceBefore = instance.Clone();
                requestBefore = npc.NarrativeRequest;
                rumorsBefore = npc.Rumors;
                float now = AR.s.Time.CurrentGameTime;
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Reports | NarrativeSaveGroup.Rewards | NarrativeSaveGroup.Continuations);
                instance.Reported = true;
                instance.ReportNpcId = npcId;
                instance.ReportRelevance = relevance;
                instance.ReactionRuleId = rule.ReactionRuleId;
                instance.Reaction = rule.Kind;
                instance.Response = followUp != null ? rule.Response : NarrativeReactionRules.EffectiveResponse(rule.Response);
                if (followUp != null)
                {
                    instance.FollowUpInstanceId = followUp.InstanceId;
                    instance.FollowUpState = NarrativeFollowUpState.Pending;
                }
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
                if (requested == true)
                {
                    MarkSaveDirty(npcId, NpcSaveGroup.NarrativeRequest);
                    npc.NarrativeRequest = null;
                }
                // 이 NPC가 행적을 알게 됐다. 이미 반응했으므로 "반응함"으로 넣고, 소문 교환으로 퍼진다 (§4.1).
                NpcRumor deedRumor = namedThreat == true
                    ? CreateThreatDeedRumor(instance, DeedValue(instance), -1, true)
                    : CreateDeedRumor(PlayerDeedKind.RumorResolved, instance.InstanceId, instance.VillageId,
                        instance.SlotIndex, instance.Generation, instance.ProgressGameTime, DeedValue(instance), -1, true);
                PutReactedDeedRumor(npc, deedRumor, now);

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
                        MarkSaveDirty(npcId, NpcSaveGroup.NarrativeRequest | NpcSaveGroup.Rumors);
                        npc.NarrativeRequest = requestBefore;
                        npc.Rumors = rumorsBefore!;
                    }
                    _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
            return new NarrativeDialogueResult { Kind = requested == true ? NarrativeConversationKind.Report : NarrativeConversationKind.DirectTell,
                InstanceId = instance.InstanceId, Text = BuildReportText(npc, instance), NeedsDisplayCommit = true };
        }

        private bool IsReportable(int npcId, NpcSaveData npc, NarrativeContentInstance instance, bool requested)
        {
            if (IsNpcAlive(npcId) == false || instance.Progress != NarrativeRumorProgress.Resolved || instance.Reported == true)
                return false;
            // 보고에는 기한이 없고, 이주했어도 옮겨 간 마을에서 받는다 (§4.1).
            return requested == true
                ? npc.NarrativeRequest != null && npc.NarrativeRequest.InstanceId == instance.InstanceId
                : IsDirectTellAvailable(instance, npcId);
        }

        private string BuildReportText(NpcSaveData npc, NarrativeContentInstance instance)
        {
            NarrativeContentDefinition? content = GetNarrativeContent(instance);
            string location = DescribeNarrativeLocation(instance, npc.VillageId);
            var text = new StringBuilder();
            if (instance.Kind == NarrativeContentKind.NamedThreat)
            {
                // 네임드 보고 (NAMED_THREAT_DESIGN §8.4): 칭호로 말한다. 기록이 정리됐으면 "그 짐승".
                string title = ThreatTitleOrDefault(instance.ThreatId);
                text.Append("플레이어: ").Append(instance.ReportRelevance == NarrativeRelevance.Requester
                    ? $"{title}, 그놈은 내가 쓰러뜨렸소."
                    : $"{NamedThreat.NamedThreatManager.Josa(title, "은", "는")} 내가 이미 처리했소.");
                text.Append('\n').Append(ResolveNpcDisplayName(npc)).Append(": ");
                if (content != null)
                    text.Append(content.ResolvedText.Replace("{location}", location).Replace("{threatTitle}", title)).Append(' ');
            }
            else
            {
                text.Append("플레이어: ").Append(location);
                text.Append(instance.ReportRelevance == NarrativeRelevance.Requester
                    ? "의 보스를 쓰러뜨려 던전을 닫았소."
                    : "은 내가 이미 정리했소.");
                text.Append('\n').Append(ResolveNpcDisplayName(npc)).Append(": ");
                if (content != null)
                    text.Append(content.ResolvedText.Replace("{location}", location)).Append(' ');
            }
            text.Append(FindReactionText(GetNarrativePolicy(instance), instance.ReactionRuleId));
            // 표시를 기다리는 후속 소개. 그사이 대상이 무효가 됐으면 붙이지 않는다(표시 뒤 확정에서 취소된다).
            if (instance.FollowUpState == NarrativeFollowUpState.Pending)
            {
                NarrativeContentInstance? target = GetNarrativeInstance(instance.FollowUpInstanceId);
                if (target != null && IsFollowUpTargetValid(npc, target, instance.InstanceId, false) == true)
                    text.Append(FollowUpLine(target, npc.VillageId));
            }
            AppendRewardLine(text, instance);
            AppendRelationLine(text, instance.AffinityDelta, instance.TrustDelta, instance.IntimacyDelta);
            return text.ToString();
        }

        // 의뢰 보답 줄. 부정 반응이어도 약속 점수로 줬으면 퉁명스럽게, 주지 않았으면 이유를 말한다 (§4.5).
        private static void AppendRewardLine(StringBuilder text, NarrativeContentInstance instance)
        {
            if (instance.RewardGold > 0)
            {
                if (NarrativeReactionRules.IsNegative(instance.Reaction) == true)
                    text.Append(" 흥, 약속은 약속이니 받아 가게.");
                text.Append("\n(의뢰 보답 ").Append(instance.RewardGold).Append("골드)");
            }
            else if (instance.RewardRefused == true)
                text.Append(" 그 정도 일로 뭘 바라나.");
        }

        // ================= 소문으로 들은 행적 반응 (§4.1 반응하는 때와 묶음) =================

        // 소문으로 아는 NPC는 아직 반응하지 않았고 14 게임일이 지나지 않은 행적에만 반응한다. 칸의 주인은 자기 부탁 행적에
        // 소문으로 반응하지 않고 보고 때 반응한다. 소문 전 해결 말하기를 기다리는 주인도 그 대화에서 반응한다.
        // 소망 주인은 전달 때 이미 반응했다(ReportNpcId).
        private bool IsDeedReactionCandidate(NpcSaveData npc, NpcRumor rumor, float now)
        {
            if (rumor.Kind != NpcRumorKind.PlayerDeed || rumor.Reacted == true ||
                (rumor.DeedKind != PlayerDeedKind.RumorResolved && rumor.DeedKind != PlayerDeedKind.DungeonCleared &&
                 rumor.DeedKind != PlayerDeedKind.WishFulfilled && rumor.DeedKind != PlayerDeedKind.ThreatSlain))
                return false;
            float age = now - rumor.EventGameTime;
            if (age < -0.01f || age > NarrativeReactionRules.DeedReactionWindowHours)
                return false;
            if (string.IsNullOrEmpty(rumor.InstanceId) == false)
            {
                if (npc.NarrativeRequest != null && npc.NarrativeRequest.InstanceId == rumor.InstanceId)
                    return false;
                NarrativeContentInstance? instance = GetNarrativeInstance(rumor.InstanceId);
                if (instance != null && (instance.ReportNpcId == npc.EntityId || IsDirectTellAvailable(instance, npc.EntityId) == true))
                    return false;
            }
            return true;
        }

        private NarrativeRelevance GetDeedRelevance(NpcSaveData npc, NpcRumor rumor)
        {
            NarrativeRelevance relevance = npc.VillageId == rumor.VillageId ? NarrativeRelevance.SameVillage : NarrativeRelevance.OtherVillage;
            // 소망 행적에는 동행·피해자가 없다. 슬롯·세대 칸이 비어 있어 던전 ID로 비교하지 않는다.
            if (rumor.DeedKind == PlayerDeedKind.WishFulfilled)
                return relevance;
            // 네임드 처치 (NAMED_THREAT_DESIGN §9.3): 그 네임드에게 다친 NPC는 피해자(×3). 필드에는 동행이 없다(D97).
            if (rumor.DeedKind == PlayerDeedKind.ThreatSlain)
                return IsNamedThreatVictim(npc, rumor.ThreatId) == true
                    ? NarrativeReactionRules.Stronger(relevance, NarrativeRelevance.Victim)
                    : relevance;
            if (rumor.SourceNpcId < 0)
                relevance = NarrativeReactionRules.Stronger(relevance, NarrativeRelevance.Companion);
            if (string.IsNullOrEmpty(npc.LastWoundDungeonId) == false &&
                npc.LastWoundDungeonId == DungeonLayout.Id(AR.s.Data.WorldSeed, rumor.VillageId, rumor.SlotIndex, rumor.Generation))
                relevance = NarrativeReactionRules.Stronger(relevance, NarrativeRelevance.Victim);
            return relevance;
        }

        /// <summary>
        /// 아는 행적을 관련도가 큰 순으로 최대 3개 모아 한 번에 반응한다. 같은 반영 전 관계로 모두 고르고, 호감 합계를 +9/−4로 제한한다.
        /// 의뢰 보답은 없다. 각 소문에 "반응함"을 표시하고 NPC 상태 + 플레이어 관계를 저장한다.
        /// </summary>
        private async Task<NarrativeDialogueResult?> RunDeedReactionsAsync(int npcId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            if (npc == null || _narrativeBusy == true || IsNpcAlive(npcId) == false)
                return null;
            _narrativeBusy = true;
            int lifetime = _narrative.Lifetime;
            PlayerRelation relationBefore = default;
            bool metBefore = false;
            List<NpcRumor>? rumorsBefore = null;
            bool committed = false;
            var picks = new List<(int Index, NarrativeRelevance Relevance, NarrativeReactionDefinition Rule)>();
            try
            {
                await AR.s.Data.WaitForSaveIdleAsync();
                if (lifetime != _narrative.Lifetime) return null;
                float now = AR.s.Time.CurrentGameTime;
                relationBefore = GetPlayerRelation(npcId);
                metBefore = HasMetPlayer(npcId);
                var input = new NarrativeReactionRules.Input(npc.NpcStat, relationBefore, IsGloryAmbition(npc));
                var candidates = new List<(int Index, NarrativeRelevance Relevance)>();
                for (int i = 0; i < npc.Rumors.Count; i++)
                    if (IsDeedReactionCandidate(npc, npc.Rumors[i], now) == true)
                        candidates.Add((i, GetDeedRelevance(npc, npc.Rumors[i])));
                candidates.Sort((a, b) =>
                {
                    int byRelevance = NarrativeReactionRules.RelevanceTenths(b.Relevance).CompareTo(NarrativeReactionRules.RelevanceTenths(a.Relevance));
                    return byRelevance != 0 ? byRelevance : npc.Rumors[b.Index].EventGameTime.CompareTo(npc.Rumors[a.Index].EventGameTime);
                });
                for (int i = 0; i < candidates.Count && picks.Count < NarrativeReactionRules.MaxDeedsPerConversation; i++)
                {
                    NpcRumor rumor = npc.Rumors[candidates[i].Index];
                    // 사건이 고정한 정책을 쓴다. 사건 기록이 없는 던전 클리어는 현재 정책을 쓴다.
                    NarrativePolicySnapshot? policy = GetNarrativePolicy(GetNarrativeInstance(rumor.InstanceId)) ??
                                                      AR.s.Data.NarrativeCatalog.CurrentPolicy;
                    NarrativeReactionDefinition? rule = policy != null
                        ? NarrativeReactionRules.Select(policy.Reactions, rumor.DeedKind, false, input,
                            candidates[i].Relevance == NarrativeRelevance.Victim)
                        : null;
                    if (rule != null)
                        picks.Add((candidates[i].Index, candidates[i].Relevance, rule));
                }
                if (picks.Count == 0) return null;

                int affinity = 0, trust = 0, intimacy = 0;
                for (int i = 0; i < picks.Count; i++)
                {
                    NarrativeReactionRules.RelationChange(picks[i].Rule.RelationLevel, picks[i].Relevance, out int a, out int t, out int m);
                    affinity += a;
                    trust += t;
                    intimacy += m;
                }
                affinity = NarrativeReactionRules.LimitConversationAffinity(affinity);

                NpcRumor top = npc.Rumors[picks[0].Index];
                // 후속 소개 (§4.6): 대사의 중심 반응이 "아는 다른 소문 소개"이고, 그 행적에 사건 기록이 있으며 아직 아무도 소개하지 않았을 때.
                // 소문 반응은 다시 보여 주지 않으므로 소개는 표시 뒤 확정한다(CommitDeedFollowUpAsync). 사건 기록이 없는 던전 클리어는 소개하지 않는다.
                NarrativeContentInstance? deedSource = GetNarrativeInstance(top.InstanceId);
                NarrativeContentInstance? followUp = picks[0].Rule.Response == NarrativeResponseKind.IntroduceKnownContent &&
                                                     deedSource != null && deedSource.FollowUpState == NarrativeFollowUpState.None
                    ? FindFollowUpTarget(npc, deedSource.InstanceId, false)
                    : null;

                rumorsBefore = npc.Rumors;
                MarkSaveDirty(npcId, NpcSaveGroup.Rumors);
                // 저장된 스냅샷이 공유하는 소문 객체는 고치지 않고 표시한 사본으로 바꿔 끼운다.
                var rumors = new List<NpcRumor>(npc.Rumors);
                for (int i = 0; i < picks.Count; i++)
                {
                    NpcRumor reacted = rumors[picks[i].Index].Clone();
                    reacted.Reacted = true;
                    rumors[picks[i].Index] = reacted;
                }
                npc.Rumors = rumors;
                if (affinity != 0 || trust != 0 || intimacy != 0)
                    ModifyPlayerRelation(npcId, affinity, trust, 0, intimacy);

                committed = await AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.NpcState);
                if (committed == false || lifetime != _narrative.Lifetime) return null;

                var text = new StringBuilder();
                text.Append(ResolveNpcDisplayName(npc)).Append(": ").Append(DeedFactLine(top, picks[0].Relevance)).Append(' ')
                    .Append(picks[0].Rule.Text);
                if (picks.Count > 1)
                    text.Append(" 요즘 네 이야기를 자주 듣는다.");
                if (followUp != null)
                    text.Append(FollowUpLine(followUp, npc.VillageId));
                AppendRelationLine(text, affinity, trust, intimacy);
                return followUp != null
                    ? new NarrativeDialogueResult { Kind = NarrativeConversationKind.DeedReactions, Text = text.ToString(),
                        InstanceId = deedSource!.InstanceId, FollowUpInstanceId = followUp.InstanceId, NeedsDisplayCommit = true }
                    : new NarrativeDialogueResult { Kind = NarrativeConversationKind.DeedReactions, Text = text.ToString() };
            }
            finally
            {
                if (lifetime == _narrative.Lifetime)
                {
                    if (rumorsBefore != null && committed == false)
                    {
                        MarkSaveDirty(npcId, NpcSaveGroup.Rumors);
                        npc.Rumors = rumorsBefore;
                        RestorePlayerRelation(npcId, relationBefore, metBefore);
                    }
                    _narrative.InvalidateDialogues(npcId);
                    _narrativeBusy = false;
                }
            }
        }

        private string DeedFactLine(NpcRumor rumor, NarrativeRelevance relevance)
        {
            if (rumor.DeedKind == PlayerDeedKind.WishFulfilled)
            {
                NarrativeContentInstance? wish = GetNarrativeInstance(rumor.InstanceId);
                NpcSaveData? owner = wish != null ? GetSaveData(wish.OwnerNpcId) : null;
                return owner != null ? ResolveNpcDisplayName(owner) + "의 소망을 네가 들어줬다더군." : "누군가의 소망을 네가 들어줬다더군.";
            }
            if (rumor.DeedKind == PlayerDeedKind.ThreatSlain)
            {
                string title = ThreatTitleOrDefault(rumor.ThreatId);
                return relevance == NarrativeRelevance.Victim
                    ? $"나를 다치게 한 {NamedThreat.NamedThreatManager.Josa(title, "을", "를")} 네가 쓰러뜨렸다더군."
                    : $"{NamedThreat.NamedThreatManager.Josa(title, "이라는", "라는")} 짐승을 네가 쓰러뜨렸다더군.";
            }
            if (relevance == NarrativeRelevance.Companion)
                return "그 던전에서 함께 싸운 일 말이야.";
            if (relevance == NarrativeRelevance.Victim)
                return "내가 다쳤던 그 던전을 네가 정리했다더군.";
            return rumor.DeedKind == PlayerDeedKind.RumorResolved
                ? "소문이 돌던 던전을 네가 정리했다더군."
                : "네가 던전 하나를 정리했다더군.";
        }

        // ================= 공용 =================

        // 같은 행적은 NPC당 1개 (§4.1). 이미 들은 같은 행적은 "반응함" 사본으로 바꾸고, 없으면 새로 넣는다. 목록은 사본으로 교체한다.
        private void PutReactedDeedRumor(NpcSaveData npc, NpcRumor deed, float now)
        {
            MarkSaveDirty(npc.EntityId, NpcSaveGroup.Rumors);
            var rumors = new List<NpcRumor>(npc.Rumors);
            npc.Rumors = rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                if (IsSameRumorTarget(rumors[i], deed) == false)
                    continue;
                NpcRumor reacted = rumors[i].Clone();
                reacted.Reacted = true;
                rumors[i] = reacted;
                return;
            }
            AddRumor(npc, deed, now);
            _rumorCounters.CreatedPlayerDeed++;
        }

        // 행적 소문의 가치 = 그 던전의 완료 골드 (사용자 결정 2026-10-10). 네임드 처치는 전리품 판매 기준가 (NAMED_THREAT_DESIGN §9.3).
        private static int DeedValue(NarrativeContentInstance instance)
        {
            if (instance.Kind == NarrativeContentKind.NamedThreat)
                return AR.s.NamedThreat != null ? AR.s.NamedThreat.GetTrophyValue(instance.ThreatId) : 0;
            return AR.s.Dungeon != null ? AR.s.Dungeon.GetSlotCompletionGold(instance.VillageId, instance.SlotIndex) : 0;
        }

        private void RestorePlayerRelation(int npcId, PlayerRelation previous, bool previouslyMet)
        {
            AR.s.Data.MarkPlayerRelationSaveDirty(npcId);
            if (previouslyMet == true) SetPlayerRelation(npcId, previous);
            else _playerRelations.Remove(npcId);
        }

        /// <summary>위치 표현. 입구가 있으면 지금 좌표, 던전이 닫혔으면 사건에 남긴 좌표를 쓴다.</summary>
        public string DescribeNarrativeLocation(NarrativeContentInstance instance, int speakerVillageId)
        {
            if (instance.Kind == NarrativeContentKind.NamedThreat)
            {
                NamedThreatRecord? record = AR.s.NamedThreat?.FindRecord(instance.ThreatId);
                return record != null ? DescribeThreatLocation(record, speakerVillageId) : "근처";
            }
            if (AR.s.Dungeon != null && AR.s.Dungeon.TryGetNarrativeEntrance(instance.DungeonId, out int x, out int y) == true)
                return AR.s.Dungeon.DescribeLocation(instance.VillageId, x, y, speakerVillageId);
            if (instance.HasEntranceContext == true && AR.s.Dungeon != null)
                return AR.s.Dungeon.DescribeLocation(instance.VillageId, instance.EntranceX, instance.EntranceY, speakerVillageId);
            return "근처 던전";
        }

        private static string FindReactionText(NarrativePolicySnapshot? policy, int ruleId)
        {
            if (policy == null)
                return string.Empty;
            for (int i = 0; i < policy.Reactions.Count; i++)
                if (policy.Reactions[i].ReactionRuleId == ruleId)
                    return policy.Reactions[i].Text;
            return string.Empty;
        }

        // 실제 신뢰 상승은 입력의 절반이다 (ModifyPlayerRelation 규칙).
        private static void AppendRelationLine(StringBuilder text, int affinity, int trustInput, int intimacy)
        {
            int trust = trustInput > 0 ? trustInput / 2 : trustInput;
            if (affinity == 0 && trust == 0 && intimacy == 0)
                return;
            text.Append('\n');
            bool first = true;
            AppendDelta(text, "호감도", affinity, ref first);
            AppendDelta(text, "친밀도", intimacy, ref first);
            AppendDelta(text, "신뢰도", trust, ref first);
        }

        private static void AppendDelta(StringBuilder text, string label, int value, ref bool first)
        {
            if (value == 0)
                return;
            if (first == false)
                text.Append(", ");
            text.Append(label).Append(' ').Append(value > 0 ? "+" : string.Empty).Append(value);
            first = false;
        }
    }
}
