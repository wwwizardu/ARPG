#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Npc.Narrative;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 단기 목표의 대화·부탁 (NPC_OBJECTIVE_DESIGN §8). 진행 중 화제(극적 목표는 고정)·결말 화제, 원수 갚기 부탁(narrative 원장 Objective 종류).
    /// 부탁 인스턴스는 부탁 대사가 실제로 표시된 뒤 만든다 (§8.3 — 소망처럼 미리 만들지 않는다). 문구는 NpcObjective 표의 칸을 사실로 채워
    /// 정의 스냅샷에 고정한다 (§14 Q5 — NarrativeContent 시트 행 없음). 보고·반응·의뢰 보답·행적 소문 전파는 던전 소문 부탁과 같은 코드를 탄다.
    /// </summary>
    public partial class NpcManager
    {
        // 코드에서 만든 목표 부탁 정의의 ContentId = 기준 + NpcObjective 표 Id (NarrativeContent 시트의 ContentId와 겹치지 않게)
        private const int ObjectiveContentIdBase = 900000;

        // ================= 대화 화제 (§8.2) =================

        /// <summary>
        /// 진행 중 화제(StatusText가 있을 때만, 극적 목표는 고정)와 결말 화제(OutcomeTopicHours 안)를 넣는다.
        /// 사실(대상·계기 인물·단계·진행 값)은 시스템이 정하고 LLM은 말투만 입힌다. LLM이 없으면 표 문구 그대로다.
        /// </summary>
        public void AddObjectiveTopics(List<DialogueTopic> topics, int npcId, NpcSaveData save, float now)
        {
            if (IsNpcAlive(npcId) == false)
                return;
            NpcObjectiveTable? definition = GetObjectiveDefinition(save);
            NpcObjective? objective = save.Objective;
            if (definition != null && objective != null && string.IsNullOrEmpty(definition.StatusText) == false)
            {
                NpcObjectiveStage stage = NpcObjectiveRules.GetStage(definition, objective, now);
                var status = new DialogueTopic
                {
                    Type = DialogueTopicType.ObjectiveStatus,
                    Category = DialogueTopicCategory.Personal,
                    Pinned = definition.Category == NpcObjectiveCategory.Dramatic,
                    BaseLine = FormatObjectiveText(definition.StatusText, save, objective, definition),
                    // 새 목표·대상·단계·진행 값(장비 마련은 골드)이 바뀌면 새 대사
                    CacheVariant = ObjectiveCacheVariant(definition.Id, objective.EventGameTime, (int)stage,
                        definition.Target == NpcObjectiveTargetKind.ShopItem ? objective.TargetBasicItemId * 7919 + save.Gold : 0),
                };
                status.Facts.Add(BuildObjectiveStatusFact(save, objective, definition, stage));
                topics.Add(status);
            }

            NpcObjectiveOutcomeRecord? outcome = save.LastObjectiveOutcome;
            NpcObjectiveTable? outcomeDefinition = outcome != null ? AR.s.Data.GetNpcObjective(outcome.DefinitionId) : null;
            if (outcome == null || outcomeDefinition == null || now >= outcome.When + outcomeDefinition.OutcomeTopicHours)
                return;
            string template = outcome.Fulfilled == true
                ? (outcome.ByOther == true && string.IsNullOrEmpty(outcomeDefinition.FulfilledByOtherText) == false
                    ? outcomeDefinition.FulfilledByOtherText
                    : outcomeDefinition.FulfilledText)
                : outcomeDefinition.AbandonedText;
            if (string.IsNullOrEmpty(template) == true)
                return;
            var result = new DialogueTopic
            {
                Type = DialogueTopicType.ObjectiveOutcome,
                Category = DialogueTopicCategory.Personal,
                BaseLine = FormatObjectiveOutcomeText(template, outcome),
                CacheVariant = ObjectiveCacheVariant(outcome.DefinitionId, outcome.When, outcome.Fulfilled == true ? 1 : 0,
                    outcome.ByOther == true ? 1 : 0),
            };
            result.Facts.Add(BuildObjectiveOutcomeFact(outcome, outcomeDefinition));
            topics.Add(result);
        }

        /// <summary>
        /// 장비 마련 결말 화제가 이 구매를 이미 말하는가 (§5.2 겹침 정리). 같은 시각의 주요 아이템 Deed(자랑)는 내지 않는다.
        /// </summary>
        public bool IsObjectivePurchaseOutcome(NpcSaveData save, float deedWhen)
        {
            NpcObjectiveOutcomeRecord? outcome = save.LastObjectiveOutcome;
            if (outcome == null || outcome.Fulfilled == false || Mathf.Abs(outcome.When - deedWhen) > 0.01f)
                return false;
            NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(outcome.DefinitionId);
            return definition != null && definition.Complete == NpcObjectiveComplete.UpgradeSlot;
        }

        private static int ObjectiveCacheVariant(int definitionId, float eventGameTime, int a, int b)
        {
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash ^ definitionId) * 16777619;
                hash = (hash ^ Mathf.FloorToInt(eventGameTime * 100f)) * 16777619;
                hash = (hash ^ a) * 16777619;
                hash = (hash ^ b) * 16777619;
                return hash;
            }
        }

        private string BuildObjectiveStatusFact(NpcSaveData save, NpcObjective objective, NpcObjectiveTable definition,
            NpcObjectiveStage stage)
        {
            string target = GetObjectiveTargetDisplayName(save, objective, definition);
            string stageText = definition.PrepHours > 0f
                ? (stage == NpcObjectiveStage.Preparing ? " 지금은 사냥과 장비로 준비하는 중이다." : $" 지금은 실행 단계이고 {objective.Attempts}번 다녀왔다.")
                : string.Empty;
            switch (definition.Trigger)
            {
                case NpcObjectiveTrigger.CompanionFellInDungeon:
                    return $"{objective.SubjectName}이(가) {target}에서 죽었다. 이 NPC는 그 던전을 끝내 원수를 갚으려 한다.{stageText}";
                case NpcObjectiveTrigger.SevereWoundInDungeon:
                    return $"이 NPC는 {target}에서 크게 다쳤고, 다시 가서 이겨 내려 한다.{stageText}";
                case NpcObjectiveTrigger.UnaffordableUpgrade:
                    int need = objective.TargetPrice + GetTreatmentFee();
                    return $"이 NPC는 상점의 {target}을(를) 사려고 돈을 모으는 중이다 (가진 돈 {save.Gold}골드, 필요한 돈 {need}골드).";
                default:
                    return $"이 NPC는 요즘 '{definition.Name}'에 매달리고 있다 (대상: {target}).";
            }
        }

        private static string BuildObjectiveOutcomeFact(NpcObjectiveOutcomeRecord outcome, NpcObjectiveTable definition)
        {
            if (outcome.Fulfilled == false)
                return $"이 NPC는 '{definition.Name}'({outcome.TargetDisplayName})을(를) 끝내 이루지 못하고 포기했다.";
            if (outcome.ByOther == true)
                return $"이 NPC가 노리던 '{definition.Name}'({outcome.TargetDisplayName})은(는) 다른 사람이 이뤘다.";
            return $"이 NPC는 '{definition.Name}'({outcome.TargetDisplayName})을(를) 이뤘다.";
        }

        // ================= 부탁 (§8.3) =================

        /// <summary>
        /// 원수 갚기 부탁 화제 (고정, 작성 문구 그대로). 조건: RequestAffinity가 있는 목표의 실행 단계, 플레이어 호감 ≥ 문턱, 부탁 칸 빔,
        /// 목표 던전 입구가 있음. 인스턴스는 대사가 표시된 뒤 CommitNarrativeRequestAsync가 이 화제의 ID로 만든다.
        /// </summary>
        public DialogueTopic? BuildObjectiveRequestTopic(int npcId, NpcSaveData save, float now)
        {
            if (IsNpcAlive(npcId) == false || save.NarrativeRequest != null || save.Objective == null)
                return null;
            NpcObjectiveTable? definition = GetObjectiveDefinition(save);
            if (definition == null || IsObjectiveRequestable(npcId, save, save.Objective.TargetDungeonId, now) == false)
                return null;
            return new DialogueTopic
            {
                Type = DialogueTopicType.ObjectiveRequest,
                Category = DialogueTopicCategory.Rumor,
                Pinned = true,
                BaseLine = FormatObjectiveText(definition.RequestText, save, save.Objective, definition),
                NarrativeInstanceId = NarrativeContentInstance.ObjectiveId(npcId, now),
            };
        }

        // 진행 확인 화제 (고정): "이 마을 동쪽 던전 일은 어떻게 됐소? 철수 일이 아직 마음에 걸리오."
        private DialogueTopic BuildObjectiveRequestProgressTopic(NpcSaveData save, NarrativeContentInstance held)
        {
            string location = DescribeNarrativeLocation(held, save.VillageId);
            string subject = save.Objective != null && save.Objective.TargetDungeonId == held.DungeonId
                ? save.Objective.SubjectName
                : string.Empty;
            var progress = new DialogueTopic
            {
                Type = DialogueTopicType.ObjectiveRequestProgress,
                Category = DialogueTopicCategory.Rumor,
                Pinned = true,
                BaseLine = string.IsNullOrEmpty(subject) == false
                    ? $"{location} 일은 어떻게 됐소? {subject} 일이 아직 마음에 걸리오."
                    : $"{location} 일은 어떻게 됐소? 아직 마음에 걸리오.",
                NarrativeInstanceId = held.InstanceId,
            };
            progress.Facts.Add("이 NPC가 플레이어에게 그 던전을 끝내 달라고 부탁했고 아직 해결되지 않았다.");
            return progress;
        }

        private static bool IsObjectiveRequestId(int npcId, string instanceId)
        {
            return string.IsNullOrEmpty(instanceId) == false &&
                   instanceId.StartsWith(NarrativeContentInstance.ObjectiveIdPrefix(npcId), System.StringComparison.Ordinal) == true;
        }

        /// <summary>그 NPC가 지금 그 던전에 대해 부탁할 수 있는가 (목표·단계·호감 문턱·입구).</summary>
        private bool IsObjectiveRequestable(int npcId, NpcSaveData npc, string dungeonId, float now)
        {
            NpcObjectiveTable? definition = GetObjectiveDefinition(npc);
            NpcObjective? objective = npc.Objective;
            return definition != null && objective != null && definition.RequestAffinity > 0 &&
                   string.IsNullOrEmpty(dungeonId) == false && objective.TargetDungeonId == dungeonId &&
                   NpcObjectiveRules.GetStage(definition, objective, now) == NpcObjectiveStage.Attempting &&
                   GetPlayerRelation(npcId).Affinity >= definition.RequestAffinity &&
                   AR.s.Dungeon != null && AR.s.Dungeon.TryGetNarrativeEntrance(dungeonId, out _, out _) == true;
        }

        /// <summary>부탁 대사가 표시된 뒤 원장에 목표 부탁 인스턴스를 만든다. 조건이 깨졌거나 정책이 없으면 null.</summary>
        private NarrativeContentInstance? CreateObjectiveNarrative(int npcId, NpcSaveData npc, string instanceId,
            out bool addedDefinition, out bool addedPolicy)
        {
            addedDefinition = false;
            addedPolicy = false;
            float now = AR.s.Time.CurrentGameTime;
            NpcObjectiveTable? definition = GetObjectiveDefinition(npc);
            NpcObjective? objective = npc.Objective;
            if (definition == null || objective == null ||
                IsObjectiveRequestable(npcId, npc, objective.TargetDungeonId, now) == false ||
                TryGetDelveSlotRef(npc.VillageId, objective.TargetDungeonId, out int slotIndex, out int generation) == false)
                return null;
            return AddObjectiveNarrative(instanceId, npcId, npc, definition, objective, slotIndex, generation, false, now,
                out addedDefinition, out addedPolicy);
        }

        /// <summary>
        /// 목표 부탁 인스턴스를 원장에 넣는다. 문구는 지금 사실로 채워 정의 스냅샷에 고정한다 — 부탁 = RequestText, 보고 = FulfilledText.
        /// resolvedByPlayer면 플레이어 측이 부탁 없이 대신 닫은 사건(직접 말하기 대기)으로 만든다.
        /// </summary>
        private NarrativeContentInstance? AddObjectiveNarrative(string instanceId, int ownerId, NpcSaveData owner,
            NpcObjectiveTable definition, NpcObjective objective, int slotIndex, int generation, bool resolvedByPlayer, float now,
            out bool addedDefinition, out bool addedPolicy)
        {
            addedDefinition = false;
            addedPolicy = false;
            NarrativePolicySnapshot? policy = AR.s.Data.NarrativeCatalog.CopyCurrentPolicy();
            if (policy == null || GetNarrativeInstance(instanceId) != null)
                return null;
            string intro = FormatObjectiveText(definition.RequestText, owner, objective, definition);
            string resolved = FormatObjectiveText(definition.FulfilledText, owner, objective, definition);
            var content = new NarrativeContentDefinition
            {
                ContentId = ObjectiveContentIdBase + definition.Id,
                Name = definition.Name,
                Kind = NarrativeContentKind.Objective,
                TargetType = NarrativeTargetType.None,
                IntroText = string.IsNullOrWhiteSpace(intro) == false ? intro : definition.Name,
                ResolvedText = string.IsNullOrWhiteSpace(resolved) == false ? resolved : "고맙소.",
                GoalType = NarrativeGoalType.DefeatTarget,
            };
            var snapshot = new NarrativeDefinitionSnapshot { Content = content, Hash = NarrativeContentCatalog.ContentHash(content) };
            if (NarrativeContentCatalog.IsSupported(snapshot) == false)
            {
                Debug.LogWarning($"[NpcObjective] Objective request text for definition {definition.Id} is not supported; npc {ownerId} has no request.");
                return null;
            }
            var instance = new NarrativeContentInstance
            {
                InstanceId = instanceId,
                ContentId = content.ContentId,
                Kind = NarrativeContentKind.Objective,
                DefinitionHash = snapshot.Hash,
                PolicyHash = policy.Hash,
                CreatedGameTime = now,
                DungeonId = objective.TargetDungeonId,
                VillageId = owner.VillageId,
                SlotIndex = slotIndex,
                Generation = generation,
                OwnerNpcId = ownerId,
            };
            if (resolvedByPlayer == true)
            {
                instance.Progress = NarrativeRumorProgress.Resolved;
                instance.ProgressGameTime = now;
                instance.Revision = 1;
            }
            AddNarrativeInstance(instance, snapshot, policy, out addedDefinition, out addedPolicy);
            return instance;
        }

        // 끝 알림: 목표가 이뤄졌으면(남이 닫음) "일이 끝났다더군", 꺾였거나 빠졌으면 "마음을 접기로 했소". 보답은 없다.
        private static string ObjectiveEndNotice(NpcSaveData npc, string location)
        {
            return npc.LastObjectiveOutcome != null && npc.LastObjectiveOutcome.Fulfilled == true
                ? $"{location} 일은 이제 끝났다더군. 신경 써 줘서 고맙네."
                : "그 일은 이제 마음을 접기로 했소. 신경 써 줘서 고맙네.";
        }

        /// <summary>
        /// 목표가 칸에서 나갔다 (§8.3): 그 목표의 열린 부탁은 미해결로 끝낸다 — 부탁 칸을 비우고 살아 있으면 다음 대화에서 알린다.
        /// 플레이어 측이 이미 해결한 부탁(Resolved)은 보고를 위해 그대로 둔다.
        /// </summary>
        private void ReleaseObjectiveRequestOnEnd(int entityId, NpcSaveData saveData, NpcObjective objective,
            NpcObjectiveExit exit, float now)
        {
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (instance.Kind == NarrativeContentKind.Objective && instance.OwnerNpcId == entityId &&
                    instance.DungeonId == objective.TargetDungeonId && instance.IsOpen == true)
                    EndObjectiveNarrativeUnresolved(instance, now);
            }
        }

        private void EndObjectiveNarrativeUnresolved(NarrativeContentInstance instance, float now)
        {
            MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.All);
            instance.Ended = true;
            instance.ProgressGameTime = now;
            instance.Revision++;
            ReleaseRequestOnEnd(instance, out _);
        }

        /// <summary>목표 주인이 살아서 아직 그 던전을 노리는가 (원장 정리의 현재 사건 판정).</summary>
        private bool IsObjectiveNarrativeCurrent(NarrativeContentInstance instance)
        {
            NpcSaveData? owner = GetSaveData(instance.OwnerNpcId);
            return owner != null && IsNpcAlive(instance.OwnerNpcId) == true && owner.Objective != null &&
                   owner.Objective.TargetDungeonId == instance.DungeonId;
        }

        /// <summary>
        /// 플레이어 쪽 던전 완료 스테이징 (§6.4, 완료 저장과 같은 커밋): 그 던전의 열린 목표 부탁은 플레이어 측 처치면 해결, 아니면 미해결 종료.
        /// 플레이어 측이 부탁 없이 대신 닫았으면 그 던전을 노리던 DungeonClosed 목표 주인(동행 제외)에게 직접 말하기 사건을 만든다(보답 없음).
        /// 저장이 실패하면 RollbackObjectiveDungeonCompletion이 되돌린다.
        /// </summary>
        private void StageObjectiveDungeonCompletion(NarrativeCompletionStage stage, DungeonRecord record, int entranceX,
            int entranceY, int companionNpcId, float now)
        {
            bool playerParty = record.BossKilledByPlayerParty;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (instance.Kind != NarrativeContentKind.Objective || instance.DungeonId != record.DungeonId ||
                    instance.IsOpen == false)
                    continue;
                (stage.ObjectiveInstancesBefore ??= new List<NarrativeContentInstance>()).Add(instance.Clone());
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.All);
                instance.HasEntranceContext = true;
                instance.EntranceX = entranceX;
                instance.EntranceY = entranceY;
                instance.ProgressGameTime = now;
                instance.Revision++;
                if (playerParty == true)
                {
                    instance.Progress = NarrativeRumorProgress.Resolved;
                    continue;
                }
                instance.Ended = true;
                NpcSaveData? requester = ReleaseRequestOnEnd(instance, out NpcNarrativeRequest? requestBefore);
                if (requester != null)
                {
                    (stage.ObjectiveRequesters ??= new List<NpcSaveData>()).Add(requester);
                    (stage.ObjectiveRequestsBefore ??= new List<NpcNarrativeRequest?>()).Add(requestBefore);
                }
            }

            if (playerParty == false)
                return;
            VillageData? village = AR.s.Village.GetVillage(record.VillageId);
            if (village == null)
                return;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                int ownerId = roster[i];
                if (ownerId == companionNpcId || _npcSaveDict.TryGetValue(ownerId, out NpcSaveData? owner) == false ||
                    IsNpcAlive(ownerId) == false || owner.Objective == null || owner.Objective.TargetDungeonId != record.DungeonId)
                    continue;
                NpcObjectiveTable? definition = GetObjectiveDefinition(owner);
                if (definition == null || definition.Complete != NpcObjectiveComplete.DungeonClosed ||
                    HasObjectiveNarrative(ownerId, record.DungeonId) == true)
                    continue;
                NarrativeContentInstance? created = AddObjectiveNarrative(NarrativeContentInstance.ObjectiveId(ownerId, now), ownerId,
                    owner, definition, owner.Objective, record.SlotIndex, record.Generation, true, now,
                    out bool addedDefinition, out bool addedPolicy);
                if (created == null)
                    continue;
                created.HasEntranceContext = true;
                created.EntranceX = entranceX;
                created.EntranceY = entranceY;
                (stage.ObjectiveCreated ??= new List<NarrativeContentInstance>()).Add(created);
                (stage.ObjectiveCreatedDefinitions ??= new List<bool>()).Add(addedDefinition);
                (stage.ObjectiveCreatedPolicies ??= new List<bool>()).Add(addedPolicy);
            }
        }

        private bool HasObjectiveNarrative(int ownerId, string dungeonId)
        {
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (instance.Kind == NarrativeContentKind.Objective && instance.OwnerNpcId == ownerId && instance.DungeonId == dungeonId)
                    return true;
            }
            return false;
        }

        private void RollbackObjectiveDungeonCompletion(NarrativeCompletionStage stage)
        {
            if (stage.ObjectiveCreated != null && stage.ObjectiveCreatedDefinitions != null && stage.ObjectiveCreatedPolicies != null)
            {
                for (int i = stage.ObjectiveCreated.Count - 1; i >= 0; i--)
                    RemoveNarrativeInstance(stage.ObjectiveCreated[i], stage.ObjectiveCreatedDefinitions[i],
                        stage.ObjectiveCreatedPolicies[i]);
            }
            if (stage.ObjectiveInstancesBefore != null)
            {
                for (int i = 0; i < stage.ObjectiveInstancesBefore.Count; i++)
                    ReplaceNarrativeInstance(stage.ObjectiveInstancesBefore[i]);
            }
            if (stage.ObjectiveRequesters != null && stage.ObjectiveRequestsBefore != null)
            {
                for (int i = 0; i < stage.ObjectiveRequesters.Count; i++)
                {
                    NpcSaveData requester = stage.ObjectiveRequesters[i];
                    MarkSaveDirty(requester.EntityId, NpcSaveGroup.NarrativeRequest);
                    requester.NarrativeRequest = stage.ObjectiveRequestsBefore[i];
                    _narrative.InvalidateDialogues(requester.EntityId);
                }
            }
        }
    }
}
