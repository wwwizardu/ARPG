#nullable enable
using ARPG.Npc;
using ARPG.Scene;
using ARPG.Creature;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 개별 NPC의 오프스크린 활동을 시간분할로 전진한다.
    /// 상태는 NpcSaveData가 소유하며, 이 시스템은 틱마다 한정된 수만 처리한다.
    /// </summary>
    public class System_NpcSim : IFixedUpdateSystem
    {
        // 총 NPC 수와 무관하게 틱당 비용을 제한한다.
        private const int TickBudget = 8;
        // 시간 점프 후 한 NPC가 과거 활동을 무한히 결제하지 않도록 하는 안전 상한.
        private const int MaxPhaseTransitionsPerNpc = 4;

        public int Priority => 59;
        public float UpdateInterval => 0.25f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            float now = AR.s.Time.CurrentGameTime;
            int worldSeed = AR.s.Map.mapSeed;

            // NpcManager가 정렬된 ID 순서와 커서를 소유한다. 시스템은 순회 상태를 저장하지 않는다.
            for (int i = 0; i < TickBudget; i++)
            {
                if (AR.s.Npc.TryGetNextSimulationWork(out int entityId, out NpcSaveData? saveData,
                    out NpcPartyState? party) == false)
                    break;

                if (party != null)
                    AdvancePartyActivity(party, now, worldSeed);
                else if (saveData != null)
                    AdvanceNpcActivity(saveData, entityId, now, worldSeed);
            }

            AR.s.Npc.TryReplenishGlobalPopulation(now);
        }

        private static void AdvanceNpcActivity(NpcSaveData saveData, int entityId, float now, int worldSeed)
        {
            // 평상시에는 now에서 다음 활동을 시작한다. 시간 점프 후에는 이전 phase 종료시각을
            // 다음 활동의 시작시각으로 사용해 과거 구간을 시간 순서대로 결제한다.
            float decisionTime = now;
            for (int i = 0; i < MaxPhaseTransitionsPerNpc; i++)
            {
                NpcActivityState activity = saveData.Activity;
                if (activity.Phase == NpcActivityPhase.Dead)
                    return;
                if (activity.PartyId >= 0)
                    return;

                UpdateAnalyticPosition(saveData, entityId, now);

                if (activity.Kind == NpcActivityKind.None && activity.Phase == NpcActivityPhase.Idle)
                {
                    StartNextActivity(saveData, entityId, decisionTime, worldSeed);
                    // 새 활동도 이미 종료됐을 수 있으므로 즉시 다음 반복에서 종료시각을 확인한다.
                    continue;
                }

                // 활동 중간에는 어떤 롤도 하지 않는다. 대부분의 라운드로빈 방문은 여기서 끝난다.
                if (activity.PhaseEndGameTime > now)
                    return;

                float transitionTime = activity.PhaseEndGameTime;
                if (transitionTime < activity.PhaseStartedGameTime)
                    // 잘못된/구 세이브 시간값은 현재 시각으로 보정해 역방향 시간을 방지한다.
                    transitionTime = now;
                decisionTime = transitionTime;

                if (activity.Phase == NpcActivityPhase.Wounded)
                {
                    // 회복이 끝나면 결과를 추가로 만들지 않고 다음 행동 선택이 가능한 Idle로 복귀한다.
                    activity.Phase = NpcActivityPhase.Idle;
                    activity.Kind = NpcActivityKind.None;
                    activity.TargetKey = string.Empty;
                    activity.PhaseStartedGameTime = transitionTime;
                    activity.PhaseEndGameTime = 0f;
                    saveData.LastSimGameTime = transitionTime;
                    AR.s.Npc.ResumeMaterializedNpcActivity(entityId);
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Rest)
                {
                    // Rest는 별도 결과가 없는 시간 소모 활동이다.
                    activity.Kind = NpcActivityKind.None;
                    activity.TargetKey = string.Empty;
                    activity.PhaseStartedGameTime = transitionTime;
                    activity.PhaseEndGameTime = 0f;
                    saveData.LastSimGameTime = transitionTime;
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Hunt)
                {
                    // Hunt만 종료 시점에 한 번의 결정론적 결과 롤을 수행한다.
                    ResolveHunt(saveData, entityId, transitionTime, worldSeed);
                    continue;
                }

                activity.Phase = NpcActivityPhase.Idle;
                activity.Kind = NpcActivityKind.None;
                activity.TargetKey = string.Empty;
                activity.PhaseStartedGameTime = transitionTime;
                activity.PhaseEndGameTime = 0f;
                saveData.LastSimGameTime = transitionTime;
            }
        }

        private static void StartNextActivity(NpcSaveData saveData, int entityId, float startGameTime, int worldSeed)
        {
            NpcActivityState activity = saveData.Activity;
            NpcActivityKind kind = NpcActivityResolver.SelectNextActivity(saveData, entityId, startGameTime, worldSeed);

            if (kind == NpcActivityKind.Hunt && AR.s.Npc.TryCreateHuntParty(entityId, out NpcPartyState? party) &&
                party != null)
            {
                StartPartyHunt(party, startGameTime, worldSeed);
                return;
            }

            activity.Kind = kind;
            // 휴식은 제자리 활동이므로 Idle 표현을 유지하고, Hunt만 Venturing으로 표시한다.
            activity.Phase = kind == NpcActivityKind.Hunt ? NpcActivityPhase.Venturing : NpcActivityPhase.Idle;
            activity.TargetKey = kind == NpcActivityKind.Hunt ? "hunt" : "rest";
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime +
                (kind == NpcActivityKind.Hunt ? NpcActivityResolver.HuntDurationHours : NpcActivityResolver.RestDurationHours);
            activity.HasMovementPath = kind == NpcActivityKind.Hunt;
            if (activity.HasMovementPath)
            {
                float minX = AR.s.Map.minChunkX * AR.s.Map.chunkSize;
                float maxX = (AR.s.Map.maxChunkX + 1) * AR.s.Map.chunkSize - 1f;
                float minY = AR.s.Map.minChunkY * AR.s.Map.chunkSize;
                float maxY = (AR.s.Map.maxChunkY + 1) * AR.s.Map.chunkSize - 1f;
                activity.OriginPosition = saveData.Position;
                activity.TargetPosition = NpcActivityResolver.GetHuntTargetPosition(saveData.Position, entityId,
                    startGameTime, worldSeed, minX, maxX, minY, maxY);
                activity.ReturnPosition = saveData.Position;
                activity.OutboundFraction = 0.5f;
                activity.OutcomeSeedGameTime = startGameTime;
            }
            saveData.LastSimGameTime = startGameTime;
            if (kind == NpcActivityKind.Hunt)
                AR.s.Npc.PauseMaterializedNpcActivity(entityId);
        }

        private static void StartPartyHunt(NpcPartyState party, float startGameTime, int worldSeed)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            if (leader == null)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            NpcActivityState activity = party.Activity;
            float minX = AR.s.Map.minChunkX * AR.s.Map.chunkSize;
            float maxX = (AR.s.Map.maxChunkX + 1) * AR.s.Map.chunkSize - 1f;
            float minY = AR.s.Map.minChunkY * AR.s.Map.chunkSize;
            float maxY = (AR.s.Map.maxChunkY + 1) * AR.s.Map.chunkSize - 1f;
            activity.PartyId = party.PartyId;
            activity.Kind = NpcActivityKind.Hunt;
            activity.Phase = NpcActivityPhase.Venturing;
            activity.TargetKey = "hunt";
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.HuntDurationHours;
            activity.HasMovementPath = true;
            activity.OriginPosition = leader.Position;
            activity.TargetPosition = NpcActivityResolver.GetHuntTargetPosition(leader.Position, party.PartyId,
                startGameTime, worldSeed, minX, maxX, minY, maxY);
            activity.ReturnPosition = leader.Position;
            activity.OutboundFraction = 0.5f;
            activity.OutcomeSeedGameTime = startGameTime;

            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                NpcSaveData? member = AR.s.Npc.GetSaveData(memberId);
                if (member == null)
                    continue;

                member.Activity.PartyId = party.PartyId;
                member.Activity.Kind = NpcActivityKind.Hunt;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = "hunt";
                member.Activity.HasMovementPath = false;
                member.LastSimGameTime = startGameTime;
                AR.s.Npc.PauseMaterializedNpcPartyActivity(memberId);
            }

            UpdatePartyAnalyticPositions(party, startGameTime);
        }

        private static void AdvancePartyActivity(NpcPartyState party, float now, int worldSeed)
        {
            NpcActivityState activity = party.Activity;
            if (activity.Kind != NpcActivityKind.Hunt || activity.Phase != NpcActivityPhase.Venturing)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            UpdatePartyAnalyticPositions(party, now);
            if (activity.PhaseEndGameTime > now)
                return;

            ResolvePartyHunt(party, activity.PhaseEndGameTime, worldSeed);
        }

        private static void UpdatePartyAnalyticPositions(NpcPartyState party, float now)
        {
            NpcActivityState activity = party.Activity;
            if (activity.HasMovementPath == false)
                return;

            float evaluationTime = now < activity.PhaseEndGameTime ? now : activity.PhaseEndGameTime;
            Vector2 partyPosition = NpcActivityResolver.EvaluateHuntPosition(activity, evaluationTime);
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                NpcSaveData? member = AR.s.Npc.GetSaveData(memberId);
                if (member == null)
                    continue;

                Vector2 formationOffset = GetPartyFormationOffset(i);
                AR.s.Npc.UpdateOffscreenNpcPosition(memberId, partyPosition + formationOffset);
            }
        }

        private static Vector2 GetPartyFormationOffset(int memberIndex)
        {
            return memberIndex switch
            {
                0 => Vector2.zero,
                1 => new Vector2(-1.25f, -1f),
                2 => new Vector2(1.25f, -1f),
                _ => new Vector2(0f, -2f),
            };
        }

        private static void UpdateAnalyticPosition(NpcSaveData saveData, int entityId, float now)
        {
            NpcActivityState activity = saveData.Activity;
            if (activity.Kind != NpcActivityKind.Hunt || activity.HasMovementPath == false)
                return;

            float evaluationTime = now < activity.PhaseEndGameTime ? now : activity.PhaseEndGameTime;
            AR.s.Npc.UpdateOffscreenNpcPosition(entityId,
                NpcActivityResolver.EvaluateHuntPosition(activity, evaluationTime));
        }

        private static void ResolvePartyHunt(NpcPartyState party, float outcomeGameTime, int worldSeed)
        {
            List<NpcSaveData> members = new List<NpcSaveData>(party.MemberIds.Count);
            List<int> memberIds = new List<int>(party.MemberIds.Count);
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                NpcSaveData? member = AR.s.Npc.GetSaveData(memberId);
                if (member == null || member.Condition == CharacterConditions.Dead)
                    continue;

                members.Add(member);
                memberIds.Add(memberId);
            }

            if (members.Count < 2)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            NpcActivityState partyActivity = party.Activity;
            float partyPower = NpcActivityResolver.GetPartyHuntPower(members);
            NpcHuntOutcome[] outcomes = new NpcHuntOutcome[members.Count];
            int successCount = 0;
            int survivorCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                NpcHuntOutcome outcome = NpcActivityResolver.ResolvePartyMemberHunt(members[i], partyPower,
                    members.Count, party.PartyId, partyActivity.OutcomeSeedGameTime, worldSeed, i);
                outcomes[i] = outcome;
                if (outcome == NpcHuntOutcome.Success)
                    successCount++;
                if (outcome != NpcHuntOutcome.Dead)
                    survivorCount++;
            }

            int foodYield = successCount > 0
                ? NpcActivityResolver.GetPartyHuntFoodYield(partyPower, party.PartyId,
                    partyActivity.OutcomeSeedGameTime, worldSeed)
                : 0;
            partyActivity.LastHuntOutcome = successCount > 0 ? NpcHuntOutcome.Success : NpcHuntOutcome.Failed;
            partyActivity.LastHuntFoodYield = foodYield;
            partyActivity.LastOutcomeGameTime = outcomeGameTime;

            for (int i = 0; i < members.Count; i++)
            {
                NpcSaveData member = members[i];
                NpcActivityState memberActivity = member.Activity;
                memberActivity.LastHuntOutcome = outcomes[i];
                memberActivity.LastHuntFoodYield = memberIds[i] == party.LeaderId &&
                    outcomes[i] == NpcHuntOutcome.Success ? foodYield : 0;
                memberActivity.LastOutcomeGameTime = outcomeGameTime;
                memberActivity.HasMovementPath = false;
                memberActivity.TargetKey = string.Empty;
                memberActivity.PhaseStartedGameTime = outcomeGameTime;
                member.LastSimGameTime = outcomeGameTime;

                if (outcomes[i] == NpcHuntOutcome.Wounded)
                {
                    memberActivity.Phase = NpcActivityPhase.Wounded;
                    memberActivity.Kind = NpcActivityKind.None;
                    memberActivity.PhaseEndGameTime = outcomeGameTime + NpcActivityResolver.WoundedRecoveryHours;
                    AR.s.Npc.AddWoundedDeed(memberIds[i], outcomeGameTime, memberActivity.PhaseEndGameTime);
                }
                else if (outcomes[i] != NpcHuntOutcome.Dead)
                {
                    memberActivity.Phase = NpcActivityPhase.Idle;
                    memberActivity.Kind = NpcActivityKind.None;
                    memberActivity.PhaseEndGameTime = 0f;
                }
            }

            AR.s.Npc.DissolveParty(party.PartyId);

            if (foodYield > 0 && members[0].VillageId >= 0)
                AR.s.Village.ProduceResource(members[0].VillageId, GlobalEnum.ItemType.Food, foodYield, false);

            ApplyPartyRelationshipOutcome(memberIds, outcomes, successCount > 0, outcomeGameTime);
            for (int i = 0; i < members.Count; i++)
            {
                int memberId = memberIds[i];
                if (outcomes[i] == NpcHuntOutcome.Dead)
                    AR.s.Npc.MarkNpcDead(memberId, outcomeGameTime);
                else
                    AR.s.Npc.ResumeMaterializedNpcActivity(memberId);
            }

            NotifyPartyHuntOutcome(party, successCount, survivorCount, foodYield);
        }

        private static void ApplyPartyRelationshipOutcome(IReadOnlyList<int> memberIds,
            IReadOnlyList<NpcHuntOutcome> outcomes, bool hasSuccess, float outcomeGameTime)
        {
            int affinityDelta = hasSuccess ? 4 : 1;
            for (int i = 0; i < memberIds.Count; i++)
            {
                if (outcomes[i] == NpcHuntOutcome.Dead)
                    continue;

                for (int j = i + 1; j < memberIds.Count; j++)
                {
                    if (outcomes[j] == NpcHuntOutcome.Dead)
                        continue;
                    AR.s.Npc.ModifyAffinity(memberIds[i], memberIds[j], affinityDelta);
                    AR.s.Npc.AddPartyAffinityDeed(memberIds[i], memberIds[j], affinityDelta, outcomeGameTime);
                    AR.s.Npc.AddPartyAffinityDeed(memberIds[j], memberIds[i], affinityDelta, outcomeGameTime);
                }
            }
        }

        private static void NotifyPartyHuntOutcome(NpcPartyState party, int successCount, int survivorCount,
            int foodYield)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            if (leader == null || leader.IsActive == false)
                return;

            string leaderName = AR.s.Npc.GetDisplayName(party.LeaderId);
            string message = successCount > 0
                ? $"{leaderName} 원정대: 사냥 성공, 식량 +{foodYield}"
                : $"{leaderName} 원정대: 사냥에 실패했습니다.";
            if (survivorCount < party.MemberIds.Count)
                message += " 전사자가 발생했습니다.";

            AR.s.UI.PushWorldFeed(message);
        }

        private static void ResolveHunt(NpcSaveData saveData, int entityId, float outcomeGameTime, int worldSeed)
        {
            NpcActivityState activity = saveData.Activity;
            float huntStartedGameTime = activity.PhaseStartedGameTime;
            float outcomeSeedGameTime = activity.OutcomeSeedGameTime > 0f
                ? activity.OutcomeSeedGameTime
                : huntStartedGameTime;
            NpcHuntOutcome outcome = NpcActivityResolver.ResolveHunt(saveData, entityId,
                outcomeSeedGameTime, worldSeed);

            activity.LastHuntOutcome = outcome;
            activity.LastHuntFoodYield = 0;
            // NS2에서 이 스냅샷을 소비해 보상, 관계 변화, 전기·연대기 반영을 수행한다.
            activity.LastOutcomeGameTime = outcomeGameTime;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = outcomeGameTime;
            saveData.LastSimGameTime = outcomeGameTime;

            if (outcome == NpcHuntOutcome.Dead)
            {
                // 매니저가 관계·호감도·마을 명단·시뮬레이션 순회·연대기를 함께 정리한다.
                NotifyHuntOutcome(saveData, entityId, outcome, 0);
                AR.s.Npc.MarkNpcDead(entityId, outcomeGameTime);
                return;
            }

            if (outcome == NpcHuntOutcome.Wounded)
            {
                // 부상자는 회복 종료시각까지 다음 Hunt 선택에서 제외된다.
                NotifyHuntOutcome(saveData, entityId, outcome, 0);
                activity.Phase = NpcActivityPhase.Wounded;
                activity.PhaseEndGameTime = outcomeGameTime + NpcActivityResolver.WoundedRecoveryHours;
                AR.s.Npc.AddWoundedDeed(entityId, outcomeGameTime, activity.PhaseEndGameTime);
                return;
            }

            activity.Phase = NpcActivityPhase.Idle;
            activity.PhaseEndGameTime = 0f;
            AR.s.Npc.ResumeMaterializedNpcActivity(entityId);

            if (outcome == NpcHuntOutcome.Success && saveData.VillageId >= 0)
            {
                int foodYield = NpcActivityResolver.GetHuntFoodYield(saveData, entityId,
                    outcomeSeedGameTime, worldSeed);
                activity.LastHuntFoodYield = foodYield;
                // 오프스크린 Hunt마다 UI 알림을 만들지 않도록 조용히 자원만 반영한다.
                AR.s.Village.ProduceResource(saveData.VillageId, GlobalEnum.ItemType.Food, foodYield, false);
            }

            NotifyHuntOutcome(saveData, entityId, outcome, activity.LastHuntFoodYield);
        }

        private static void NotifyHuntOutcome(NpcSaveData saveData, int entityId,
            NpcHuntOutcome outcome, int foodYield)
        {
            // 보이지 않는 NPC의 평범한 성공·실패는 피드를 채우지 않는다.
            // 부상과 사망은 위치와 관계없이 중요한 세계 변화이므로 항상 알린다.
            if (saveData.IsActive == false && outcome != NpcHuntOutcome.Wounded && outcome != NpcHuntOutcome.Dead)
                return;

            string name = AR.s.Npc.GetDisplayName(entityId);
            string message = outcome switch
            {
                NpcHuntOutcome.Success => $"{name}: 사냥 성공, 식량 +{foodYield}",
                NpcHuntOutcome.Failed => $"{name}: 사냥에 실패했습니다.",
                NpcHuntOutcome.Wounded => $"{name}: 사냥 중 부상을 입었습니다.",
                NpcHuntOutcome.Dead => $"{name}: 사냥에서 돌아오지 못했습니다.",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(message) == false)
                AR.s.UI.PushWorldFeed(message);
        }
    }
}
