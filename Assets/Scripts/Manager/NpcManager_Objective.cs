#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Npc.Narrative;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 단기 목표 (NPC_OBJECTIVE_DESIGN, D142~D152). 사건·상황이 며칠짜리 목표가 되어 활동 확률·대상·파티·대화·표시를 기울인다.
    /// 활동 판정 공식은 바꾸지 않는다 (D146). 상태는 NpcSaveData.Objective·LastObjectiveOutcome뿐이고 새 System은 없다.
    /// 후보 판정·다시 계산·교체·평가·알림 진입점이 여기 있다. 게임(System_NpcSim)과 진단이 같은 메서드를 부른다.
    /// 사건 시점 판정과 다시 계산(§4.8)은 같은 후보 판정 메서드(TryBuild*Candidate)를 쓴다.
    /// </summary>
    public partial class NpcManager
    {
        private NpcObjectiveCounters _objectiveCounters = new();
        // 칸이 비어 다음 Idle 전환 때 한 번 다시 계산할 NPC (§4.7). 휘발성 — 저장하지 않는다(빈 칸 직후 몇 시간 안에 소비된다).
        private HashSet<int> _objectiveRecalcPending = new();
        // 진단만 켠다: 사건 시점 판정 뒤 같은 NPC의 기록으로 다시 판정해 결과를 대조한다 (§12 결정론).
        private bool _objectiveDeterminismAudit;
        // 다시 계산 후보 스크래치 (매번 비우고 쓴다 — 호출 사이에 상태를 남기지 않는다)
        private readonly List<ObjectiveCandidate> _objectiveCandidateScratch = new();
        private readonly List<ObjectiveCandidate> _objectiveAuditScratch = new();

        private const int ObjectiveKindCount = NpcObjectiveCounters.KindCount;

        /// <summary>진단 카운터 (§12). 진단 도구가 시뮬레이션 동안 바꿔 끼운다.</summary>
        public NpcObjectiveCounters ObjectiveCounters => _objectiveCounters;

        /// <summary>목표 후보 1건 (§4.3). 사건 시점 판정과 다시 계산이 같은 모양을 만든다.</summary>
        private struct ObjectiveCandidate
        {
            public NpcObjectiveTable? Definition;
            public float EventGameTime;
            public string DungeonId;
            public int BasicItemId;
            public int EquipSlot;
            public int Price;
            public float Power;
            public int SubjectNpcId;
            public string SubjectName;
            public float ReactChance;
        }

        private void ResetObjectiveRuntime()
        {
            _objectiveCounters = new NpcObjectiveCounters();
            _objectiveRecalcPending.Clear();
            _objectiveDeterminismAudit = false;
            _objectiveCandidateScratch.Clear();
        }

        // ================= 조회 =================

        /// <summary>지금 목표의 정의. 목표가 없거나 표에 없으면 null.</summary>
        public NpcObjectiveTable? GetObjectiveDefinition(NpcSaveData saveData)
        {
            return saveData.Objective != null ? AR.s.Data.GetNpcObjective(saveData.Objective.DefinitionId) : null;
        }

        /// <summary>지금 목표의 단계 (§4.4). 목표가 없으면 None.</summary>
        public NpcObjectiveStage GetObjectiveStage(NpcSaveData saveData, float now)
        {
            NpcObjectiveTable? definition = GetObjectiveDefinition(saveData);
            return definition != null && saveData.Objective != null
                ? NpcObjectiveRules.GetStage(definition, saveData.Objective, now)
                : NpcObjectiveStage.None;
        }

        /// <summary>
        /// 실행 단계의 던전 목표 대상 (§7.2). 리더가 목표 원정을 고를 때 이 던전으로 간다. 소속 마을 현재 슬롯에 없으면 false.
        /// </summary>
        public bool TryGetAttemptingObjectiveDungeon(NpcSaveData saveData, float now, out NpcObjectiveTable? definition,
            out int slotIndex)
        {
            slotIndex = -1;
            definition = GetObjectiveDefinition(saveData);
            NpcObjective? objective = saveData.Objective;
            if (definition == null || objective == null || definition.Target != NpcObjectiveTargetKind.Dungeon ||
                NpcObjectiveRules.GetStage(definition, objective, now) != NpcObjectiveStage.Attempting)
                return false;
            return TryGetDelveSlotRef(saveData.VillageId, objective.TargetDungeonId, out slotIndex, out _) == true;
        }

        // ================= 활동 선택 연결 (§7.1) =================

        /// <summary>목표 단계의 활동 배율을 선택 문맥에 넣는다. 목표가 없으면 모두 1.</summary>
        private void ApplyObjectiveActivityMultipliers(NpcSaveData saveData, float now, ref NpcActivityChoiceContext context)
        {
            context.ObjectiveRestMultiplier = 1f;
            context.ObjectiveHuntMultiplier = 1f;
            context.ObjectiveTradeMultiplier = 1f;
            context.ObjectiveDelveMultiplier = 1f;
            NpcObjectiveTable? definition = GetObjectiveDefinition(saveData);
            if (definition == null || saveData.Objective == null)
                return;
            NpcObjectiveStage stage = NpcObjectiveRules.GetStage(definition, saveData.Objective, now);
            context.ObjectiveRestMultiplier = NpcObjectiveRules.GetActivityMultiplier(definition, stage, NpcActivityKind.Rest);
            context.ObjectiveHuntMultiplier = NpcObjectiveRules.GetActivityMultiplier(definition, stage, NpcActivityKind.Hunt);
            context.ObjectiveTradeMultiplier = NpcObjectiveRules.GetActivityMultiplier(definition, stage, NpcActivityKind.Trade);
            context.ObjectiveDelveMultiplier = NpcObjectiveRules.GetActivityMultiplier(definition, stage, NpcActivityKind.DungeonDelve);
        }

        // ================= Idle 전환 평가 (§4.7) =================

        /// <summary>
        /// Idle 전환(활동 선택 직전) 평가: 결말 화제 만료 → 기한·대상 유효·같은 종류 갱신 → (칸이 빈 직후 1회) 다시 계산 →
        /// 조건형 계기(소문 확인). SelectNextActivity가 부른다 — 게임과 진단이 같은 경로를 탄다.
        /// </summary>
        public void EvaluateObjectiveOnIdle(int entityId, NpcSaveData saveData, float now)
        {
            if (saveData.Condition == CharacterConditions.Dead)
                return;
            NpcObjectiveOutcomeRecord? outcome = saveData.LastObjectiveOutcome;
            if (outcome != null)
            {
                NpcObjectiveTable? outcomeDefinition = AR.s.Data.GetNpcObjective(outcome.DefinitionId);
                if (outcomeDefinition == null || now >= outcome.When + outcomeDefinition.OutcomeTopicHours)
                {
                    MarkSaveDirty(entityId, NpcSaveGroup.Objective);
                    saveData.LastObjectiveOutcome = null;
                    _narrative.InvalidateDialogues(entityId);
                }
            }

            if (saveData.Objective != null)
                ValidateObjectiveOnIdle(entityId, saveData, now);

            // 다시 계산은 칸이 빈 뒤 첫 Idle 전환 때 한 번만 (§4.8). 그 뒤로는 새 사건과 조건형 계기만 기다린다.
            if (_objectiveRecalcPending.Remove(entityId) == true && saveData.Objective == null)
                RecalculateObjective(entityId, saveData, now);

            // 조건형 계기 — 유망한 소문 보유 (§5.1). 같은 종류면 가장 좋은 소문으로 대상을 맞춘다.
            if (TryBuildRumorCandidate(saveData, now, out ObjectiveCandidate rumor) == true)
                OfferObjectiveCandidate(entityId, saveData, rumor, now, false);
        }

        /// <summary>기한·대상 유효 확인 (§4.5·§4.7). 끝나면 칸을 비운다.</summary>
        private void ValidateObjectiveOnIdle(int entityId, NpcSaveData saveData, float now)
        {
            NpcObjective objective = saveData.Objective!;
            NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(objective.DefinitionId);
            if (definition == null)
            {
                EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
                return;
            }

            // 소문 확인: 유망한 소문이 없어지면(무효·만료·확인됨) 빠진다. 있으면 아래 조건형 계기가 대상을 갱신한다.
            if (definition.Trigger == NpcObjectiveTrigger.PromisingRumor)
            {
                if (TryBuildRumorCandidate(saveData, now, out _) == false)
                    EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
                return;
            }

            if (now >= NpcObjectiveRules.GetDeadline(definition, objective))
            {
                EndObjective(entityId, saveData, NpcObjectiveExit.Abandoned, false, string.Empty, now);
                return;
            }

            switch (definition.Target)
            {
                case NpcObjectiveTargetKind.Dungeon:
                    if (TryGetDelveSlotRef(saveData.VillageId, objective.TargetDungeonId, out _, out _) == true)
                        return;
                    // 던전이 닫혔다. 완료 알림(NPC 파티·플레이어)이 먼저 처리하므로 여기는 놓친 경우의 안전망이다 — 남이 이룬 것으로 본다.
                    if (definition.Complete == NpcObjectiveComplete.DungeonClosed)
                        EndObjective(entityId, saveData, NpcObjectiveExit.Fulfilled, true, string.Empty, now);
                    else
                        EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
                    return;
                case NpcObjectiveTargetKind.ShopItem:
                    if (TryEvaluateObjectiveShopTarget(entityId, saveData, objective, out _, out _) == false)
                        EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
                    return;
            }
        }

        /// <summary>
        /// 다시 계산 (§4.8): 최근 14일 기록에서 후보를 모아 생성 확률을 통과한 것 중 가장 높은 하나를 세운다.
        /// 성격 판정은 결정론(salt 200, 사건 시각)이라 다시 굴려도 같다. 시도 횟수만 0부터다.
        /// </summary>
        private void RecalculateObjective(int entityId, NpcSaveData saveData, float now)
        {
            List<ObjectiveCandidate> candidates = _objectiveCandidateScratch;
            candidates.Clear();
            GatherRecordCandidates(entityId, saveData, now, NpcObjectiveTrigger.None, -1, string.Empty, candidates);

            bool found = false;
            ObjectiveCandidate best = default;
            int worldSeed = AR.s.Map.mapSeed;
            for (int i = 0; i < candidates.Count; i++)
            {
                ObjectiveCandidate candidate = candidates[i];
                if (NpcObjectiveRules.RollReaction(entityId, candidate.EventGameTime, worldSeed, candidate.ReactChance) == false)
                    continue;
                if (found == true)
                {
                    int rank = NpcObjectiveRules.CompareRank(candidate.Definition!, best.Definition!);
                    // 같은 순위면 최근 사건
                    if (rank < 0 || (rank == 0 && candidate.EventGameTime <= best.EventGameTime))
                        continue;
                }
                best = candidate;
                found = true;
            }
            candidates.Clear();
            if (found == true)
            {
                best.ReactChance = 1f;   // 이미 굴렸다
                OfferObjectiveCandidate(entityId, saveData, best, now, true);
            }
        }

        /// <summary>
        /// 기록 기반 후보 수집 (§4.8 표). filter가 None이면 전부, 아니면 그 계기만(진단 대조용: subjectId·dungeonId로 사건을 좁힌다).
        /// 기록은 최근 것부터 거꾸로 훑다가 14일보다 오래된 것이 나오면 멈춘다.
        /// </summary>
        private void GatherRecordCandidates(int entityId, NpcSaveData saveData, float now, NpcObjectiveTrigger filter,
            int subjectId, string dungeonId, List<ObjectiveCandidate> output)
        {
            float windowStart = now - NpcObjectiveRules.RecalculateWindowHours;
            ObjectiveCandidate candidate;
            if (filter == NpcObjectiveTrigger.None)
            {
                if (TryBuildRumorCandidate(saveData, now, out candidate) == true)
                    output.Add(candidate);
                if (TryBuildEquipmentCandidate(entityId, saveData, out candidate) == true)
                    output.Add(candidate);
            }

            if (filter == NpcObjectiveTrigger.None || filter == NpcObjectiveTrigger.SevereWoundInDungeon)
            {
                List<NpcDeedEntry> deeds = saveData.Deeds;
                for (int i = deeds.Count - 1; i >= 0; i--)
                {
                    NpcDeedEntry deed = deeds[i];
                    if (deed.When < windowStart)
                        break;
                    if (deed.Kind != NpcDeedKind.SevereWound || string.IsNullOrEmpty(deed.DungeonId) == true)
                        continue;
                    if (filter != NpcObjectiveTrigger.None && deed.DungeonId != dungeonId)
                        continue;
                    if (TryBuildRedemptionCandidate(saveData, deed.DungeonId, deed.When, now, out candidate) == true)
                        output.Add(candidate);
                }
            }

            if (filter == NpcObjectiveTrigger.None || filter == NpcObjectiveTrigger.CompanionFellInDungeon)
            {
                List<DeceasedRelationMemory> losses = saveData.DeceasedRelations;
                int lastSubject = -1;
                for (int i = losses.Count - 1; i >= 0; i--)
                {
                    DeceasedRelationMemory loss = losses[i];
                    if (loss.DeathGameTime < windowStart)
                        break;
                    int deceasedId = loss.DeceasedNpcEntityId;
                    if (deceasedId == lastSubject || (filter != NpcObjectiveTrigger.None && deceasedId != subjectId))
                        continue;
                    lastSubject = deceasedId;
                    if (TryBuildRevengeCandidate(saveData, deceasedId, now, out candidate) == true)
                        output.Add(candidate);
                }
                List<NpcSharedActivityMemory> companions = saveData.SharedActivityMemories;
                for (int i = 0; i < companions.Count; i++)
                {
                    int deceasedId = companions[i].OtherNpcId;
                    if (filter != NpcObjectiveTrigger.None && deceasedId != subjectId)
                        continue;
                    // 사망 관계 기억으로 이미 본 사망자는 같은 후보라 건너뛴다 (관계 배율은 빌더가 두 기억을 함께 본다).
                    if (HasDeceasedRelationMemory(saveData, deceasedId, windowStart) == true)
                        continue;
                    if (TryBuildRevengeCandidate(saveData, deceasedId, now, out candidate) == true)
                        output.Add(candidate);
                }
            }
        }

        // ================= 목표 칸 (§2.2·§4.3) =================

        /// <summary>
        /// 후보를 목표 칸과 비교해 넣는다 (§4.3 ④~⑥). 같은 종류면 SameKindRefresh, 낮으면 버리고, 생성 확률(salt 200)을 통과하면
        /// 칸에 넣고 밀린 목표는 빠짐으로 지운다. 확률은 결정론이라 순위 비교보다 먼저 굴려도 결과가 같다 — 반응률을 칸과 무관하게 센다.
        /// </summary>
        private bool OfferObjectiveCandidate(int entityId, NpcSaveData saveData, in ObjectiveCandidate candidate, float now,
            bool recalculated)
        {
            NpcObjectiveTable? definition = candidate.Definition;
            if (definition == null)
                return false;
            int kind = (int)definition.Trigger;

            bool passed = true;
            if (candidate.ReactChance < 1f)
            {
                passed = NpcObjectiveRules.RollReaction(entityId, candidate.EventGameTime, AR.s.Map.mapSeed, candidate.ReactChance);
                if (recalculated == false)
                {
                    _objectiveCounters.ReactRolls[kind]++;
                    if (passed == true)
                        _objectiveCounters.ReactPassed[kind]++;
                    if (_objectiveDeterminismAudit == true)
                        AuditObjectiveDeterminism(entityId, saveData, candidate, passed, now);
                }
            }

            NpcObjective? current = saveData.Objective;
            NpcObjectiveTable? currentDefinition = current != null ? AR.s.Data.GetNpcObjective(current.DefinitionId) : null;
            if (current != null && currentDefinition != null)
            {
                if (currentDefinition.Id == definition.Id)
                {
                    if (definition.SameKindRefresh == true && passed == true)
                        RefreshObjectiveTarget(entityId, saveData, current, candidate);
                    return false;
                }
                if (NpcObjectiveRules.CompareRank(definition, currentDefinition) <= 0)
                {
                    // 조건형(소문 확인)은 Idle 전환마다 다시 오므로 사건형만 센다.
                    if (definition.Trigger != NpcObjectiveTrigger.PromisingRumor)
                        _objectiveCounters.RejectedByRank[kind]++;
                    return false;
                }
            }
            if (passed == false)
                return false;

            if (current != null)
            {
                int currentKind = currentDefinition != null ? (int)currentDefinition.Trigger : 0;
                _objectiveCounters.Displaced[kind * ObjectiveKindCount + currentKind]++;
                EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
            }

            MarkSaveDirty(entityId, NpcSaveGroup.Objective);
            saveData.Objective = new NpcObjective
            {
                DefinitionId = definition.Id,
                EventGameTime = candidate.EventGameTime,
                CreatedGameTime = now,
                TargetDungeonId = candidate.DungeonId ?? string.Empty,
                TargetBasicItemId = candidate.BasicItemId,
                TargetEquipSlot = definition.Target == NpcObjectiveTargetKind.ShopItem ? candidate.EquipSlot : -1,
                TargetPrice = candidate.Price,
                SubjectNpcId = candidate.SubjectNpcId,
                SubjectName = candidate.SubjectName ?? string.Empty,
            };
            _objectiveRecalcPending.Remove(entityId);
            _objectiveCounters.Created[kind]++;
            // 소문 확인은 조건형이라 원래 매번 다시 본다 — 기록에서 "돌아온" 목표로 세지 않는다.
            if (recalculated == true && definition.Trigger != NpcObjectiveTrigger.PromisingRumor)
                _objectiveCounters.Recalculated[kind]++;
            _narrative.InvalidateDialogues(entityId);
            return true;
        }

        /// <summary>같은 종류 갱신 (§2.2 SameKindRefresh): 소문 확인은 가장 좋은 소문, 장비 마련은 교체 후 전투력이 더 높은 매물.</summary>
        private void RefreshObjectiveTarget(int entityId, NpcSaveData saveData, NpcObjective current,
            in ObjectiveCandidate candidate)
        {
            NpcObjectiveTable definition = candidate.Definition!;
            if (definition.Target == NpcObjectiveTargetKind.Dungeon)
            {
                if (current.TargetDungeonId == candidate.DungeonId && current.EventGameTime == candidate.EventGameTime)
                    return;
                MarkSaveDirty(entityId, NpcSaveGroup.Objective);
                current.TargetDungeonId = candidate.DungeonId ?? string.Empty;
                current.EventGameTime = candidate.EventGameTime;
            }
            else if (definition.Target == NpcObjectiveTargetKind.ShopItem)
            {
                if (current.TargetBasicItemId == candidate.BasicItemId && current.TargetEquipSlot == candidate.EquipSlot)
                    return;
                float currentPower = TryEvaluateObjectiveShopTarget(entityId, saveData, current, out _, out float power) == true
                    ? power
                    : 0f;
                if (candidate.Power <= currentPower)
                    return;
                MarkSaveDirty(entityId, NpcSaveGroup.Objective);
                current.TargetBasicItemId = candidate.BasicItemId;
                current.TargetEquipSlot = candidate.EquipSlot;
                current.TargetPrice = candidate.Price;
            }
            else
            {
                return;
            }
            _objectiveCounters.Refreshed[(int)definition.Trigger]++;
            _narrative.InvalidateDialogues(entityId);
        }

        /// <summary>
        /// 목표가 칸에서 나간다 (§4.5). 이룸·꺾임은 RecordDeed면 결말 Deed, 결말 화제(OutcomeTopicHours)를 남긴다.
        /// 빠짐·주인 사망은 기록하지 않는다. 주인 사망이 아니면 다음 Idle 전환 때 한 번 다시 계산한다.
        /// </summary>
        /// <param name="actualTargetName">결말 화제의 대상 이름 (장비 마련 이룸 = 실제로 산 물건). 비면 목표 대상 이름.</param>
        private void EndObjective(int entityId, NpcSaveData saveData, NpcObjectiveExit exit, bool byOther,
            string actualTargetName, float now)
        {
            NpcObjective? objective = saveData.Objective;
            if (objective == null)
                return;
            NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(objective.DefinitionId);
            int kind = definition != null ? (int)definition.Trigger : 0;
            MarkSaveDirty(entityId, NpcSaveGroup.Objective);
            switch (exit)
            {
                case NpcObjectiveExit.Fulfilled:
                    _objectiveCounters.Fulfilled[kind]++;
                    if (byOther == true)
                        _objectiveCounters.FulfilledByOther[kind]++;
                    break;
                case NpcObjectiveExit.Abandoned:
                    _objectiveCounters.Abandoned[kind]++;
                    break;
                case NpcObjectiveExit.Dropped:
                    _objectiveCounters.Dropped[kind]++;
                    break;
                case NpcObjectiveExit.OwnerDied:
                    _objectiveCounters.OwnerDied[kind]++;
                    break;
            }

            if (definition != null && (exit == NpcObjectiveExit.Fulfilled || exit == NpcObjectiveExit.Abandoned))
            {
                bool fulfilled = exit == NpcObjectiveExit.Fulfilled;
                if (definition.RecordDeed == true)
                    AddObjectiveOutcomeDeed(entityId, saveData, definition, objective, fulfilled, now);
                string text = fulfilled
                    ? (byOther == true && string.IsNullOrEmpty(definition.FulfilledByOtherText) == false
                        ? definition.FulfilledByOtherText
                        : definition.FulfilledText)
                    : definition.AbandonedText;
                if (definition.OutcomeTopicHours > 0f && string.IsNullOrEmpty(text) == false)
                {
                    saveData.LastObjectiveOutcome = new NpcObjectiveOutcomeRecord
                    {
                        DefinitionId = definition.Id,
                        Fulfilled = fulfilled,
                        ByOther = byOther,
                        TargetDisplayName = string.IsNullOrEmpty(actualTargetName) == false
                            ? actualTargetName
                            : GetObjectiveTargetDisplayName(saveData, objective, definition),
                        SubjectName = objective.SubjectName,
                        When = now,
                    };
                }
            }

            saveData.Objective = null;
            ReleaseObjectiveRequestOnEnd(entityId, saveData, objective, exit, now);
            if (exit != NpcObjectiveExit.OwnerDied)
                _objectiveRecalcPending.Add(entityId);
            _narrative.InvalidateDialogues(entityId);
        }

        /// <summary>결말 Deed (§4.5·§9.1): ObjectiveFulfilled·ObjectiveAbandoned + 정의·대상 던전. 원수 갚기는 참가자에 죽은 NPC.</summary>
        private void AddObjectiveOutcomeDeed(int entityId, NpcSaveData saveData, NpcObjectiveTable definition,
            NpcObjective objective, bool fulfilled, float now)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Deeds | NpcSaveGroup.Stories);
            NpcDeedEntry entry = new NpcDeedEntry(fulfilled ? NpcDeedKind.ObjectiveFulfilled : NpcDeedKind.ObjectiveAbandoned,
                -1, NpcKnowledgeLevel.Lived, now)
            {
                ObjectiveDefinitionId = definition.Id,
                DungeonId = objective.TargetDungeonId ?? string.Empty,
            };
            if (objective.SubjectNpcId >= 0)
            {
                entry.Participants.Add(objective.SubjectNpcId);
                entry.ParticipantSnapshots.Add(new NpcDeedParticipant(objective.SubjectNpcId,
                    string.IsNullOrEmpty(objective.SubjectName) == false
                        ? objective.SubjectName
                        : ResolveNpcDisplayName(objective.SubjectNpcId)));
            }
            saveData.RecordDeed(entry);
        }

        // ================= 후보 판정 (사건 시점·다시 계산 공용) =================

        /// <summary>소문 확인 후보 (§5.1): 유망한 소문 중 가치가 가장 높은 것(D76 규칙 그대로)의 던전. 기한 = 소문 사건 + 72h.</summary>
        private bool TryBuildRumorCandidate(NpcSaveData saveData, float now, out ObjectiveCandidate candidate)
        {
            candidate = default;
            if (AR.s.Data.TryGetNpcObjectiveByTrigger(NpcObjectiveTrigger.PromisingRumor, out NpcObjectiveTable? definition) == false ||
                definition == null || HasDelveOpportunity(saveData) == false ||
                TryGetPromisingRumor(saveData, now, out NpcRumor? rumor) == false || rumor == null)
                return false;
            candidate = new ObjectiveCandidate
            {
                Definition = definition,
                EventGameTime = rumor.EventGameTime,
                DungeonId = DungeonLayout.Id(AR.s.Data.WorldSeed, rumor.VillageId, rumor.SlotIndex, rumor.Generation),
                EquipSlot = -1,
                SubjectNpcId = -1,
                SubjectName = string.Empty,
                ReactChance = 1f,
            };
            return true;
        }

        /// <summary>장비 마련 후보 (§5.2): 소속 마을 상점에서 예산만 넘어 못 사는 업그레이드 중 교체 후 전투력이 가장 높은 1개.</summary>
        private bool TryBuildEquipmentCandidate(int entityId, NpcSaveData saveData, out ObjectiveCandidate candidate)
        {
            candidate = default;
            if (AR.s.Data.TryGetNpcObjectiveByTrigger(NpcObjectiveTrigger.UnaffordableUpgrade, out NpcObjectiveTable? definition) == false ||
                definition == null || saveData.VillageId < 0)
                return false;
            VillageData? village = AR.s.Village.PrepareNpcTrade(saveData.VillageId);
            if (village == null)
                return false;
            int index = FindBestUnaffordableUpgrade(entityId, saveData, village, out int price, out float power,
                out GE.EquipSlotType slot);
            if (index < 0)
                return false;
            ItemData item = village.MerchantStock[index].Item;
            candidate = new ObjectiveCandidate
            {
                Definition = definition,
                EventGameTime = AR.s.Time.CurrentGameTime,
                DungeonId = string.Empty,
                BasicItemId = item.Equipment != null ? item.Equipment.BasicItemId : 0,
                EquipSlot = (int)slot,
                Price = price,
                Power = power,
                SubjectNpcId = -1,
                SubjectName = string.Empty,
                ReactChance = 1f,
            };
            return candidate.BasicItemId > 0;
        }

        /// <summary>
        /// 설욕 후보 (§6.1): 본인의 던전 원정 중상(던전·시각). 기한 안, 그 던전이 소속 마을에 아직 있음, 같은 던전의 설욕 결말 Deed 없음.
        /// </summary>
        private bool TryBuildRedemptionCandidate(NpcSaveData saveData, string dungeonId, float eventGameTime, float now,
            out ObjectiveCandidate candidate)
        {
            candidate = default;
            if (AR.s.Data.TryGetNpcObjectiveByTrigger(NpcObjectiveTrigger.SevereWoundInDungeon, out NpcObjectiveTable? definition) == false ||
                definition == null || string.IsNullOrEmpty(dungeonId) == true ||
                now >= eventGameTime + definition.DeadlineHours ||
                TryGetDelveSlotRef(saveData.VillageId, dungeonId, out _, out _) == false ||
                HasObjectiveOutcomeDeed(saveData, definition.Id, dungeonId, -1, false, now) == true)
                return false;
            candidate = new ObjectiveCandidate
            {
                Definition = definition,
                EventGameTime = eventGameTime,
                DungeonId = dungeonId,
                EquipSlot = -1,
                SubjectNpcId = -1,
                SubjectName = string.Empty,
                ReactChance = NpcObjectiveRules.GetReactChance(definition, 1f, saveData.NpcStat),
            };
            return true;
        }

        /// <summary>
        /// 원수 갚기 후보 (§6.1·§6.2): 같은 마을의 죽은 NPC의 사망 Deed(던전·시각)와 본인의 사망 관계 기억·공동 활동 기억(관계 배율).
        /// 기한 안, 던전이 아직 열림, 같은 사망자의 꺾임 Deed 없음. 라이벌·원한만 있으면 후보가 아니다.
        /// </summary>
        private bool TryBuildRevengeCandidate(NpcSaveData saveData, int deceasedId, float now, out ObjectiveCandidate candidate)
        {
            candidate = default;
            if (AR.s.Data.TryGetNpcObjectiveByTrigger(NpcObjectiveTrigger.CompanionFellInDungeon, out NpcObjectiveTable? definition) == false ||
                definition == null || deceasedId < 0 || deceasedId == saveData.EntityId ||
                _npcSaveDict.TryGetValue(deceasedId, out NpcSaveData? deceased) == false ||
                deceased.Condition != CharacterConditions.Dead || deceased.VillageId != saveData.VillageId)
                return false;
            NpcDeedEntry? fell = FindLastDeed(deceased, NpcDeedKind.Fell);
            if (fell == null || string.IsNullOrEmpty(fell.DungeonId) == true || now >= fell.When + definition.DeadlineHours)
                return false;
            float relationMultiplier = GetRevengeRelationMultiplier(saveData, deceasedId);
            if (relationMultiplier <= 0f ||
                TryGetDelveSlotRef(saveData.VillageId, fell.DungeonId, out _, out _) == false ||
                HasObjectiveOutcomeDeed(saveData, definition.Id, fell.DungeonId, deceasedId, true, now) == true)
                return false;
            candidate = new ObjectiveCandidate
            {
                Definition = definition,
                EventGameTime = fell.When,
                DungeonId = fell.DungeonId,
                EquipSlot = -1,
                SubjectNpcId = deceasedId,
                SubjectName = ResolveNpcDisplayName(deceased),
                ReactChance = NpcObjectiveRules.GetReactChance(definition, relationMultiplier, saveData.NpcStat),
            };
            return true;
        }

        /// <summary>원수 갚기 관계 배율 (§6.2): 사망 관계 기억의 유형, 공동 활동 기억(사망 때 호감 50 이상 = 동료 0.5) 중 가장 높은 것.</summary>
        private static float GetRevengeRelationMultiplier(NpcSaveData saveData, int deceasedId)
        {
            float best = 0f;
            List<DeceasedRelationMemory> losses = saveData.DeceasedRelations;
            for (int i = 0; i < losses.Count; i++)
            {
                if (losses[i].DeceasedNpcEntityId == deceasedId)
                    best = Mathf.Max(best, NpcObjectiveRules.GetRelationMultiplier(losses[i].RelationType));
            }
            List<NpcSharedActivityMemory> companions = saveData.SharedActivityMemories;
            for (int i = 0; i < companions.Count; i++)
            {
                NpcSharedActivityMemory memory = companions[i];
                if (memory.OtherNpcId == deceasedId && memory.LastAffinity.HasValue == true &&
                    memory.LastAffinity.Value >= NpcObjectiveRules.CompanionMinAffinity)
                    best = Mathf.Max(best, NpcObjectiveRules.CompanionRelationMultiplier);
            }
            return best;
        }

        /// <summary>그 사망자를 기억하는가 (사망 관계 기억 또는 공동 활동 기억). 원수 갚기 원정 수락 보너스(§7.2)에 쓴다.</summary>
        private static bool RemembersDeceased(NpcSaveData saveData, int deceasedId)
        {
            List<DeceasedRelationMemory> losses = saveData.DeceasedRelations;
            for (int i = 0; i < losses.Count; i++)
            {
                if (losses[i].DeceasedNpcEntityId == deceasedId)
                    return true;
            }
            List<NpcSharedActivityMemory> companions = saveData.SharedActivityMemories;
            for (int i = 0; i < companions.Count; i++)
            {
                if (companions[i].OtherNpcId == deceasedId)
                    return true;
            }
            return false;
        }

        private static bool HasDeceasedRelationMemory(NpcSaveData saveData, int deceasedId, float windowStart)
        {
            List<DeceasedRelationMemory> losses = saveData.DeceasedRelations;
            for (int i = losses.Count - 1; i >= 0; i--)
            {
                if (losses[i].DeathGameTime < windowStart)
                    break;
                if (losses[i].DeceasedNpcEntityId == deceasedId)
                    return true;
            }
            return false;
        }

        private static NpcDeedEntry? FindLastDeed(NpcSaveData saveData, NpcDeedKind kind)
        {
            List<NpcDeedEntry> deeds = saveData.Deeds;
            for (int i = deeds.Count - 1; i >= 0; i--)
            {
                if (deeds[i].Kind == kind)
                    return deeds[i];
            }
            return null;
        }

        /// <summary>
        /// 이미 끝난 목표인가 (§4.3 ③·§4.8): 같은 정의·같은 대상 던전(원수 갚기는 같은 사망자)의 결말 Deed. 결말은 사건 뒤라 최근 14일만 본다.
        /// abandonedOnly면 꺾임 Deed만 본다 (원수 갚기 — 이룸은 던전이 닫혀 이미 후보가 아니다).
        /// </summary>
        private static bool HasObjectiveOutcomeDeed(NpcSaveData saveData, int definitionId, string dungeonId, int subjectId,
            bool abandonedOnly, float now)
        {
            float windowStart = now - NpcObjectiveRules.RecalculateWindowHours;
            List<NpcDeedEntry> deeds = saveData.Deeds;
            for (int i = deeds.Count - 1; i >= 0; i--)
            {
                NpcDeedEntry deed = deeds[i];
                if (deed.When < windowStart)
                    break;
                if (deed.Kind != NpcDeedKind.ObjectiveAbandoned &&
                    (abandonedOnly == true || deed.Kind != NpcDeedKind.ObjectiveFulfilled))
                    continue;
                if (deed.ObjectiveDefinitionId != definitionId || deed.DungeonId != dungeonId)
                    continue;
                if (subjectId < 0 || deed.Participants.Contains(subjectId) == true)
                    return true;
            }
            return false;
        }

        // ================= 장비 마련 매물 (§5.2) =================

        /// <summary>
        /// 예산(Gold − 치료비 예비금)만 넘어 못 사는 업그레이드 중 교체 후 전투력이 가장 높은 1개(동점이면 싼 것)의 재고 인덱스. 없으면 -1.
        /// 재고 조건·D45 문턱·개인 상한은 실제 구매(FindBestShopUpgrade)와 같은 메서드로 거른다.
        /// </summary>
        private int FindBestUnaffordableUpgrade(int entityId, NpcSaveData saveData, VillageData village, out int bestPrice,
            out float bestPower, out GE.EquipSlotType bestSlot)
        {
            int budget = saveData.Gold - GetTreatmentFee();
            int bestIndex = -1;
            bestPrice = int.MaxValue;
            bestPower = 0f;
            bestSlot = GE.EquipSlotType.Max;
            for (int i = 0; i < village.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = village.MerchantStock[i];
                if (AR.s.Village.IsNpcPurchasableEntry(village, entry) == false)
                    continue;
                int price = VillageManager.GetNpcPurchasePrice(entry);
                if (price <= 0 || price <= budget)
                    continue;
                if (EvaluateBestUpgrade(entityId, saveData, entry.Item, out GE.EquipSlotType slot, out _,
                        out float newPower) == false)
                    continue;
                if (bestIndex < 0 || newPower > bestPower || (newPower == bestPower && price < bestPrice))
                {
                    bestIndex = i;
                    bestPrice = price;
                    bestPower = newPower;
                    bestSlot = slot;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// 장비 마련 대상이 아직 유효한가 (§5.2 빠짐): 소속 마을 재고에 그 기본 아이템이 있고 여전히 그 칸의 업그레이드다.
        /// 돈이 모였는지는 보지 않는다 (산 뒤에야 이룸).
        /// </summary>
        private bool TryEvaluateObjectiveShopTarget(int entityId, NpcSaveData saveData, NpcObjective objective,
            out int stockIndex, out float power)
        {
            stockIndex = -1;
            power = 0f;
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.PrepareNpcTrade(saveData.VillageId) : null;
            if (village == null || objective.TargetBasicItemId <= 0)
                return false;
            for (int i = 0; i < village.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = village.MerchantStock[i];
                if (entry.Item == null || entry.Item.Equipment == null ||
                    entry.Item.Equipment.BasicItemId != objective.TargetBasicItemId ||
                    AR.s.Village.IsNpcPurchasableEntry(village, entry) == false)
                    continue;
                if (EvaluateBestUpgrade(entityId, saveData, entry.Item, out GE.EquipSlotType slot, out _,
                        out float newPower) == false || (int)slot != objective.TargetEquipSlot)
                    continue;
                stockIndex = i;
                power = newPower;
                return true;
            }
            return false;
        }

        // ================= 사건 알림 (§4.6) =================

        /// <summary>
        /// 거래 종료 (§4.6): 대상 칸을 업그레이드했으면 장비 마련 이룸(산 물건 이름으로 결말 화제), 그다음 예산만 넘어 못 산 업그레이드가
        /// 있으면 장비 마련 계기(같은 종류면 더 좋은 매물로 갱신). TradeAtShop이 부른다.
        /// </summary>
        private void NotifyObjectiveTradeEnded(int entityId, NpcSaveData saveData, in NpcTradeReport report, float now)
        {
            NpcObjective? objective = saveData.Objective;
            NpcObjectiveTable? definition = GetObjectiveDefinition(saveData);
            if (objective != null && definition != null && definition.Complete == NpcObjectiveComplete.UpgradeSlot &&
                report.Bought == true && report.BoughtEquipSlot == objective.TargetEquipSlot)
            {
                ItemTable? bought = AR.s.Data.GetItem(report.BoughtItemId);
                EndObjective(entityId, saveData, NpcObjectiveExit.Fulfilled, false, bought != null ? bought.Name : string.Empty,
                    now);
            }

            if (TryBuildEquipmentCandidate(entityId, saveData, out ObjectiveCandidate candidate) == false)
                return;
            _objectiveCounters.UnaffordableUpgradeTrades++;
            OfferObjectiveCandidate(entityId, saveData, candidate, now, false);
        }

        /// <summary>던전 원정 중상 (§4.6·§6.1): 중상 Deed(던전)를 남긴 직후 설욕 판정. ApplyExpeditionWound가 부른다.</summary>
        private void NotifyObjectiveSevereWound(int entityId, NpcSaveData saveData, string dungeonId, float when)
        {
            _objectiveCounters.DungeonSevereWounds++;
            if (TryBuildRedemptionCandidate(saveData, dungeonId, when, when, out ObjectiveCandidate candidate) == true)
                OfferObjectiveCandidate(entityId, saveData, candidate, when, false);
        }

        /// <summary>
        /// NPC 사망 (§4.5 주인 사망·§6.1 원수 갚기 계기). 사망 Deed(던전)와 고인 기억이 남고 마을 명단에서 빠진 뒤 부른다.
        /// 게임의 MarkNpcDead와 진단의 축약 사망 정리가 같은 메서드를 부른다.
        /// ① 주인 사망: 목표를 지운다. 목표 던전 원정에서 죽었으면 연쇄로 센다(§6.5).
        /// ② 던전 원정 사망이면 같은 마을 관계자가 각자 원수 갚기를 판정한다 (D149).
        /// </summary>
        public void NotifyObjectiveNpcDied(int entityId, NpcSaveData saveData, float when, string? dungeonId)
        {
            NpcObjective? objective = saveData.Objective;
            if (objective != null)
            {
                if (string.IsNullOrEmpty(dungeonId) == false && objective.TargetDungeonId == dungeonId)
                    _objectiveCounters.ChainDeaths++;
                EndObjective(entityId, saveData, NpcObjectiveExit.OwnerDied, false, string.Empty, when);
            }
            if (saveData.LastObjectiveOutcome != null)
            {
                MarkSaveDirty(entityId, NpcSaveGroup.Objective);
                saveData.LastObjectiveOutcome = null;
            }
            _objectiveRecalcPending.Remove(entityId);

            if (string.IsNullOrEmpty(dungeonId) == true)
                return;
            _objectiveCounters.DungeonDeaths++;
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            if (village == null)
                return;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                int survivorId = roster[i];
                if (survivorId == entityId || _npcSaveDict.TryGetValue(survivorId, out NpcSaveData? survivor) == false ||
                    survivor.Condition == CharacterConditions.Dead)
                    continue;
                if (TryBuildRevengeCandidate(survivor, entityId, when, out ObjectiveCandidate candidate) == false)
                    continue;
                _objectiveCounters.RevengeJudgments++;
                OfferObjectiveCandidate(survivorId, survivor, candidate, when, false);
            }
        }

        /// <summary>
        /// 던전 원정 귀환 (§4.6): 그 던전을 목표로 가진 살아 돌아온 참가자의 시도·완료. 던전이 닫혔으면 그 마을의 DungeonClosed 목표를
        /// 이룸으로 끝낸다(같은 파티 = 직접, 그 밖 = 남이). 사망자는 사망 정리(NotifyObjectiveNpcDied)가 처리한다.
        /// </summary>
        private void NotifyObjectiveDelveReturned(NpcDelveReport report, float now)
        {
            if (string.IsNullOrEmpty(report.DungeonId) == true)
                return;
            if (report.Completed == true)
                NotifyObjectiveDungeonClosed(report.VillageId, report.DungeonId, report, -1, now);

            for (int i = 0; i < report.MemberIds.Count; i++)
            {
                if (report.Outcomes[i] == NpcHuntOutcome.Dead)
                    continue;
                int memberId = report.MemberIds[i];
                if (_npcSaveDict.TryGetValue(memberId, out NpcSaveData? member) == false)
                    continue;
                NpcObjective? objective = member.Objective;
                NpcObjectiveTable? definition = GetObjectiveDefinition(member);
                if (objective == null || definition == null || objective.TargetDungeonId != report.DungeonId ||
                    NpcObjectiveRules.GetStage(definition, objective, now) != NpcObjectiveStage.Attempting)
                    continue;

                MarkSaveDirty(memberId, NpcSaveGroup.Objective);
                objective.Attempts++;
                bool fulfilled = definition.Complete == NpcObjectiveComplete.VisitDungeon ||
                                 (definition.Complete == NpcObjectiveComplete.ExpeditionSuccess &&
                                  report.Outcomes[i] == NpcHuntOutcome.Success);
                if (fulfilled == true)
                    EndObjective(memberId, member, NpcObjectiveExit.Fulfilled, false, string.Empty, now);
                else if (definition.MaxAttempts > 0 && objective.Attempts >= definition.MaxAttempts)
                    EndObjective(memberId, member, NpcObjectiveExit.Abandoned, false, string.Empty, now);
            }
        }

        /// <summary>
        /// 플레이어 측 던전 완료 (§4.6·§6.4): 저장이 커밋된 뒤 DungeonManager가 부른다. 그 던전을 노리던 마을 NPC의 DungeonClosed 목표는
        /// 이룸 — 동행으로 데려간 목표 주인은 "함께", 나머지는 "대신·남이". NPC 쪽 변화는 즉시 저장하지 않는다 (D47).
        /// </summary>
        public void NotifyObjectiveDungeonClosedByPlayer(int villageId, string dungeonId, int companionNpcId, float now)
        {
            NotifyObjectiveDungeonClosed(villageId, dungeonId, null, companionNpcId, now);
        }

        /// <param name="report">NPC 파티가 닫았으면 그 원정 결과 — 살아 돌아온 파티원은 "직접", 그 원정에서 죽은 주인은 사망 정리 몫이라 건너뛴다.</param>
        private void NotifyObjectiveDungeonClosed(int villageId, string dungeonId, NpcDelveReport? report,
            int companionNpcId, float now)
        {
            VillageData? village = villageId >= 0 ? AR.s.Village.GetVillage(villageId) : null;
            if (village == null || string.IsNullOrEmpty(dungeonId) == true)
                return;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                int ownerId = roster[i];
                if (_npcSaveDict.TryGetValue(ownerId, out NpcSaveData? owner) == false || owner.Objective == null ||
                    owner.Objective.TargetDungeonId != dungeonId)
                    continue;
                NpcObjectiveTable? definition = GetObjectiveDefinition(owner);
                if (definition == null || definition.Complete != NpcObjectiveComplete.DungeonClosed)
                    continue;
                int memberIndex = report != null ? report.MemberIds.IndexOf(ownerId) : -1;
                if (memberIndex >= 0 && report!.Outcomes[memberIndex] == NpcHuntOutcome.Dead)
                    continue;
                bool direct = ownerId == companionNpcId || memberIndex >= 0;
                EndObjective(ownerId, owner, NpcObjectiveExit.Fulfilled, direct == false, string.Empty, now);
            }
        }

        /// <summary>이주 시작 (§4.6): 목표는 빠짐. 대상이 옛 마을이라 다시 계산해도 돌아오지 않는다.</summary>
        private void DropObjectiveOnRelocation(int entityId, NpcSaveData saveData, float now)
        {
            if (saveData.Objective != null)
                EndObjective(entityId, saveData, NpcObjectiveExit.Dropped, false, string.Empty, now);
        }

        // ================= 원정 대상·파티 (§7.2) =================

        /// <summary>
        /// 원정 대상 슬롯 (§7.2): 리더(솔로 = 본인)의 실행 단계 던전 목표. 없으면 -1 → 호출자가 균등 선택한다.
        /// 소문 확인 목표면 옛 확인 원정과 같이 소문 노출·확인 원정으로 센다 (D76 집계 유지).
        /// </summary>
        private int PickObjectiveDelveSlot(int holderId, int villageId, float now, out bool rumorConfirmation)
        {
            rumorConfirmation = false;
            if (holderId < 0 || _npcSaveDict.TryGetValue(holderId, out NpcSaveData? holder) == false ||
                holder.VillageId != villageId ||
                TryGetAttemptingObjectiveDungeon(holder, now, out NpcObjectiveTable? definition, out int slotIndex) == false ||
                definition == null)
                return -1;
            _objectiveCounters.ObjectiveDelves++;
            if (definition.Trigger == NpcObjectiveTrigger.PromisingRumor &&
                TryGetPromisingRumor(holder, now, out NpcRumor? rumor) == true && rumor != null && rumor.SlotIndex == slotIndex)
            {
                RecordRumorExposure(rumor);
                _rumorCounters.ConfirmationDelves++;
                rumorConfirmation = true;
            }
            return slotIndex;
        }

        /// <summary>목표 원정 리더면 모집 시도 확률을 건너뛴다 (§7.2 AlwaysRecruit).</summary>
        private bool ShouldAlwaysRecruitForObjective(NpcSaveData leader, NpcActivityKind kind, float now)
        {
            return kind == NpcActivityKind.DungeonDelve &&
                   TryGetAttemptingObjectiveDungeon(leader, now, out NpcObjectiveTable? definition, out _) == true &&
                   definition != null && definition.AlwaysRecruit == true;
        }

        /// <summary>
        /// 다른 리더의 모집 후보에서 빠지는가 (§7.2 SkipOtherParties): 실행 단계 목표 주인은 빠지되, 같은 던전을 노리는 리더의
        /// 던전 원정에는 후보로 남는다.
        /// </summary>
        private bool IsObjectiveBusyForParty(NpcSaveData leader, NpcSaveData candidate, NpcActivityKind kind, float now)
        {
            NpcObjectiveTable? definition = GetObjectiveDefinition(candidate);
            NpcObjective? objective = candidate.Objective;
            if (definition == null || objective == null || definition.SkipOtherParties == false ||
                NpcObjectiveRules.GetStage(definition, objective, now) != NpcObjectiveStage.Attempting)
                return false;
            if (kind != NpcActivityKind.DungeonDelve || leader.Objective == null)
                return true;
            return TryGetAttemptingObjectiveDungeon(leader, now, out _, out _) == false ||
                   leader.Objective.TargetDungeonId != objective.TargetDungeonId;
        }

        /// <summary>원수 갚기 원정 수락 보너스 (§7.2): 리더의 사망자를 기억하는 후보 +0.20 (상한 그대로).</summary>
        private float GetObjectiveAcceptBonus(NpcSaveData leader, NpcSaveData candidate, NpcActivityKind kind, float now)
        {
            if (kind != NpcActivityKind.DungeonDelve || leader.Objective == null || leader.Objective.SubjectNpcId < 0 ||
                TryGetAttemptingObjectiveDungeon(leader, now, out _, out _) == false)
                return 0f;
            return RemembersDeceased(candidate, leader.Objective.SubjectNpcId) == true ? NpcObjectiveRules.RevengeAcceptBonus : 0f;
        }

        // ================= 진단 대조 (§12 결정론) =================

        /// <summary>
        /// 사건 시점 판정 결과를 같은 NPC의 기록 기반 판정(다시 계산 경로)과 대조한다. 진단만 켠다.
        /// 기록이 사건보다 늦게 남거나 14일 훑기가 놓치면 불일치로 잡힌다.
        /// </summary>
        private void AuditObjectiveDeterminism(int entityId, NpcSaveData saveData, in ObjectiveCandidate eventCandidate,
            bool eventPassed, float now)
        {
            NpcObjectiveTable? definition = eventCandidate.Definition;
            if (definition == null)
                return;
            List<ObjectiveCandidate> candidates = _objectiveAuditScratch;
            candidates.Clear();
            GatherRecordCandidates(entityId, saveData, now, definition.Trigger, eventCandidate.SubjectNpcId,
                eventCandidate.DungeonId, candidates);
            bool recordPassed = false;
            bool sameFacts = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                ObjectiveCandidate candidate = candidates[i];
                if (candidate.EventGameTime != eventCandidate.EventGameTime || candidate.DungeonId != eventCandidate.DungeonId)
                    continue;
                sameFacts = true;
                recordPassed = NpcObjectiveRules.RollReaction(entityId, candidate.EventGameTime, AR.s.Map.mapSeed,
                    candidate.ReactChance);
                break;
            }
            candidates.Clear();
            _objectiveCounters.DeterminismChecks++;
            if (sameFacts == false || recordPassed != eventPassed)
                _objectiveCounters.DeterminismMismatches++;
        }

        // ================= 표시 (§8) =================

        /// <summary>목표 대상 이름: 던전은 "이 마을 동쪽 던전"처럼 방향, 매물은 아이템 이름.</summary>
        public string GetObjectiveTargetDisplayName(NpcSaveData saveData, NpcObjective objective, NpcObjectiveTable definition)
        {
            if (definition.Target == NpcObjectiveTargetKind.ShopItem)
                return GetObjectiveItemName(objective.TargetBasicItemId);
            return GetObjectiveDungeonDisplayName(saveData.VillageId, objective.TargetDungeonId);
        }

        private string GetObjectiveDungeonDisplayName(int villageId, string dungeonId)
        {
            if (TryGetDelveSlotRef(villageId, dungeonId, out int slotIndex, out _) == true)
            {
                NpcRumorPlace place = ResolveDungeonSlotPlace(villageId, slotIndex, out int bearing);
                if (place == NpcRumorPlace.Bearing || place == NpcRumorPlace.Nearby)
                    return NpcRumorPhrasing.PlaceCore(place, bearing) + " 던전";
            }
            return "그 던전";
        }

        private static string GetObjectiveItemName(int basicItemId)
        {
            BasicItemTable? basic = AR.s.Data.GetBasicItem(basicItemId);
            ItemTable? item = basic != null ? AR.s.Data.GetItem(basic.ItemId) : null;
            return item != null && string.IsNullOrEmpty(item.Name) == false ? item.Name : "장비";
        }

        /// <summary>
        /// 목표 문구 채우기 (§4.1): {target} 대상, {item} 매물 이름, {gold} 지금 Gold, {need} 가격 + 치료비 예비금,
        /// {remain} 모자란 골드, {subject} 사망자 이름. 자리표시 바로 뒤의 조사(을/를·이/가·은/는·와/과)는 받침에 맞춘다.
        /// </summary>
        public string FormatObjectiveText(string template, NpcSaveData saveData, NpcObjective objective,
            NpcObjectiveTable definition)
        {
            if (string.IsNullOrEmpty(template) == true)
                return string.Empty;
            string text = template;
            if (text.Contains("{target}") == true)
                text = FillObjectiveSlot(text, "{target}", GetObjectiveTargetDisplayName(saveData, objective, definition));
            if (text.Contains("{item}") == true)
                text = FillObjectiveSlot(text, "{item}", GetObjectiveItemName(objective.TargetBasicItemId));
            if (text.Contains("{subject}") == true)
                text = FillObjectiveSlot(text, "{subject}", string.IsNullOrEmpty(objective.SubjectName) == false
                    ? objective.SubjectName
                    : NpcRumorPhrasing.NpcName(objective.SubjectNpcId));
            int need = objective.TargetPrice + GetTreatmentFee();
            text = text.Replace("{gold}", saveData.Gold.ToString())
                .Replace("{need}", need.ToString())
                .Replace("{remain}", Mathf.Max(0, need - saveData.Gold).ToString());
            return text;
        }

        /// <summary>결말 화제 문구 채우기 — 결말 기록의 대상·사망자 이름 스냅샷을 쓴다.</summary>
        public static string FormatObjectiveOutcomeText(string template, NpcObjectiveOutcomeRecord outcome)
        {
            if (string.IsNullOrEmpty(template) == true)
                return string.Empty;
            string text = FillObjectiveSlot(template, "{target}", outcome.TargetDisplayName);
            text = FillObjectiveSlot(text, "{item}", outcome.TargetDisplayName);
            return FillObjectiveSlot(text, "{subject}", outcome.SubjectName);
        }

        private static readonly string[][] ObjectiveJosaPairs =
        {
            new[] { "을", "를" }, new[] { "이", "가" }, new[] { "은", "는" }, new[] { "과", "와" },
        };

        private static string FillObjectiveSlot(string text, string slot, string value)
        {
            if (text.Contains(slot) == false)
                return text;
            for (int i = 0; i < ObjectiveJosaPairs.Length; i++)
            {
                string withFinal = ObjectiveJosaPairs[i][0];
                string withoutFinal = ObjectiveJosaPairs[i][1];
                string josa = NpcRumorPhrasing.Josa(value, withFinal, withoutFinal);
                text = text.Replace(slot + withFinal, value + josa).Replace(slot + withoutFinal, value + josa);
            }
            return text.Replace(slot, value);
        }

        /// <summary>검사 창 한 줄 (§8.1): "목표: …" + 극적 목표는 단계. 목표가 없으면 빈 문자열.</summary>
        public string GetObjectiveInspectLine(NpcSaveData saveData, float now)
        {
            NpcObjectiveTable? definition = GetObjectiveDefinition(saveData);
            NpcObjective? objective = saveData.Objective;
            if (definition == null || objective == null)
                return string.Empty;
            string line = FormatObjectiveText(definition.InspectText, saveData, objective, definition);
            if (definition.PrepHours > 0f)
                line += NpcObjectiveRules.GetStage(definition, objective, now) == NpcObjectiveStage.Preparing
                    ? " — 준비 중"
                    : " — 실행 중";
            return line;
        }

        // ================= 로드 =================

        /// <summary>
        /// 로드 보정 (§9.1): 정의 표에 없는 목표·결말은 지우고, 시도 횟수는 0~MaxAttempts로 자른다.
        /// 대상이 없어졌으면 여기서 지우지 않는다 — 다음 Idle 전환 때 판정이 빠짐으로 처리한다.
        /// </summary>
        private static void NormalizeObjectiveOnLoad(NpcSaveData saveData)
        {
            NpcObjective? objective = saveData.Objective;
            if (objective != null)
            {
                NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(objective.DefinitionId);
                if (definition == null)
                {
                    saveData.Objective = null;
                }
                else
                {
                    int maxAttempts = definition.MaxAttempts > 0 ? definition.MaxAttempts : int.MaxValue;
                    objective.Attempts = Mathf.Clamp(objective.Attempts, 0, maxAttempts);
                }
            }

            NpcObjectiveOutcomeRecord? outcome = saveData.LastObjectiveOutcome;
            if (outcome != null && AR.s.Data.GetNpcObjective(outcome.DefinitionId) == null)
                saveData.LastObjectiveOutcome = null;
        }
    }
}
