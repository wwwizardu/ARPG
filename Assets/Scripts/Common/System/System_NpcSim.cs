#nullable enable
using ARPG.Data;
using ARPG.NamedThreat;
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
    /// 던전 안에서도 돈다 (IWorldSimSystem) — 월드 맵은 AR.s.WorldMap으로만 본다 (DUNGEON_WORLD_SIM_DESIGN §2·§4).
    /// </summary>
    public class System_NpcSim : IFixedUpdateSystem, IWorldSimSystem
    {
        // 총 NPC 수와 무관하게 틱당 비용을 제한한다.
        private const int TickBudget = 12;
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
            int worldSeed = AR.s.WorldMap.mapSeed;

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
            // 던전 안에서 동행 중인 NPC는 활동 선택·판정에서 제외한다 (NPC_COMBAT_DESIGN §4.5)
            if (AR.s.Npc.IsCompanionInDungeon(entityId) == true)
                return;
            // 플레이어와 결투 중인 NPC도 제외한다 — 결투 도중 사냥을 시작하면 조우 대기 태그가 붙어 인식이 꺼진다 (§9, B4)
            if (AR.s.Npc.IsInPlayerDuel(entityId) == true)
                return;

            // 화면 밖에서 원정·이주를 마치고 돌아왔으면, 이 NPC의 판정(곧바로 고르는 다음 활동 포함)이 다 끝난 뒤
            // 귀환점 쪽 화면 바로 밖에서 실체화해 걸어 들어오게 한다 (NPC_SIMULATION_DESIGN §3.1 D14).
            if (AdvanceNpcActivityPhases(saveData, entityId, now, worldSeed, out Vector2 arrivedFrom) == true)
                AR.s.Npc.TryMaterializeArrivingNpc(entityId, arrivedFrom);
        }

        /// <summary>활동 phase를 시간 순서대로 전진한다. 원정·이주가 이번 호출에서 끝났으면 true와 귀환 경로가 오는 쪽을 돌려준다.</summary>
        private static bool AdvanceNpcActivityPhases(NpcSaveData saveData, int entityId, float now, int worldSeed,
            out Vector2 arrivedFrom)
        {
            arrivedFrom = default;
            bool arrived = false;

            // 평상시에는 now에서 다음 활동을 시작한다. 시간 점프 후에는 이전 phase 종료시각을
            // 다음 활동의 시작시각으로 사용해 과거 구간을 시간 순서대로 결제한다.
            float decisionTime = now;
            for (int i = 0; i < MaxPhaseTransitionsPerNpc; i++)
            {
                NpcActivityState activity = saveData.Activity;
                if (activity.Phase == NpcActivityPhase.Dead)
                    return arrived;
                if (activity.PartyId >= 0)
                    return arrived;

                AR.s.Npc.UpdateSoloTravelPosition(entityId, saveData, now);

                if (activity.Kind == NpcActivityKind.None && activity.Phase == NpcActivityPhase.Idle)
                {
                    // 순회 함수는 표시하지 않으므로 바꾸기 직전에 표시한다.
                    // 이동 중 위치는 경로로 다시 계산되는 값이라 표시하지 않는다 (SAVE_SNAPSHOT S13).
                    AR.s.Npc.MarkSaveDirty(entityId, NpcSaveGroup.ActivityTransition);
                    StartNextActivity(saveData, entityId, decisionTime, worldSeed);
                    // 새 활동도 이미 종료됐을 수 있으므로 즉시 다음 반복에서 종료시각을 확인한다.
                    continue;
                }

                // 활동 중간에는 어떤 롤도 하지 않는다. 대부분의 라운드로빈 방문은 여기서 끝난다.
                if (activity.PhaseEndGameTime > now)
                    return arrived;

                // 여기부터는 모든 경로가 이 NPC의 활동 상태를 바꾼다.
                AR.s.Npc.MarkSaveDirty(entityId, NpcSaveGroup.ActivityTransition);

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
                    // "부상 회복 직후" 필요 판정의 기준 시각 (NPC_AMBITION_DESIGN §6.2)
                    activity.RecoveredGameTime = transitionTime;
                    saveData.LastSimGameTime = transitionTime;
                    // 여관 안에서 회복했으면 여관 문 앞에서 나온다 (NPC_SIMULATION_DESIGN §3.1 D13)
                    AR.s.Npc.ApplyWoundRecoveredPresentation(entityId);
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Rest)
                {
                    // Rest는 보상 없는 시간 소모 활동이다. 종료 처리(소문 교환 D74 등)는 공용 진입점이 맡는다 (D107).
                    // RNG는 Rest 시작 시각 기준이라 상태를 바꾸기 전에 부른다.
                    AR.s.Npc.ResolveRestEnd(entityId, saveData, activity.PhaseStartedGameTime, transitionTime,
                        worldSeed, true);
                    activity.Kind = NpcActivityKind.None;
                    activity.TargetKey = string.Empty;
                    activity.PhaseStartedGameTime = transitionTime;
                    activity.PhaseEndGameTime = 0f;
                    saveData.LastSimGameTime = transitionTime;
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Trade)
                {
                    // Trade는 종료 시점에 소속 마을 상점과 1회 거래한다 (D39). 판매·구매 결과는 TradeAtShop이 확정한다.
                    ResolveTrade(saveData, entityId, transitionTime);
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Hunt)
                {
                    // Hunt는 종료 시점에 한 번의 결정론적 결과 롤을 수행한다.
                    arrivedFrom = GetExpeditionFromPosition(activity);
                    arrived = true;
                    ResolveHunt(saveData, entityId, transitionTime, worldSeed);
                    continue;
                }

                if (activity.Kind == NpcActivityKind.DungeonDelve)
                {
                    // Delve도 종료 시점 1회 판정 (NPC_AMBITION_DESIGN §7.1, D55). 전리품·NPC 던전 완료까지 매니저가 확정한다.
                    arrivedFrom = GetExpeditionFromPosition(activity);
                    arrived = true;
                    ResolveDelve(saveData, entityId, transitionTime, worldSeed);
                    continue;
                }

                if (activity.Kind == NpcActivityKind.Relocate)
                {
                    // 이주 도착 (D140): 소속·마을 명단을 목적지로 옮긴다. 진단과 같은 공용 메서드.
                    // 도착도 귀환처럼 걸어 들어온다 (NPC_SIMULATION_DESIGN §3.1 D14, 2026-10-25 사용자 결정)
                    arrivedFrom = GetExpeditionFromPosition(activity);
                    arrived = true;
                    AR.s.Npc.CompleteRelocation(entityId, saveData, transitionTime);
                    AR.s.Npc.ApplyExpeditionEndPresentation(entityId);
                    continue;
                }

                activity.Phase = NpcActivityPhase.Idle;
                activity.Kind = NpcActivityKind.None;
                activity.TargetKey = string.Empty;
                activity.PhaseStartedGameTime = transitionTime;
                activity.PhaseEndGameTime = 0f;
                saveData.LastSimGameTime = transitionTime;
            }
            return arrived;
        }

        /// <summary>
        /// 귀환 경로가 오는 쪽 = 원정 목표 지점. 강등 때 오는 구간으로 다시 계산돼 목표와 귀환점이 같아졌거나 편도(이주)면 출발 지점을 쓴다.
        /// </summary>
        private static Vector2 GetExpeditionFromPosition(NpcActivityState activity)
        {
            if ((activity.TargetPosition - activity.ReturnPosition).sqrMagnitude > 1f)
                return activity.TargetPosition;
            return activity.OriginPosition;
        }

        private static void StartNextActivity(NpcSaveData saveData, int entityId, float startGameTime, int worldSeed)
        {
            NpcActivityState activity = saveData.Activity;
            bool companionReserved = AR.s.Npc.IsCompanionReserved(entityId);
            // 사라진 터 소속이고 체류 기한(72h)이 지났으면 효용 선택 대신 가장 가까운 상점 마을로 이주한다 (D139·D140).
            if (companionReserved == false &&
                AR.s.Npc.TryStartRelocation(entityId, saveData, startGameTime, worldSeed) == true)
                return;
            // 동행 예약 NPC는 원정(Hunt)을 떠나지 않도록 Rest만 고른다 (NPC_COMBAT_DESIGN §4.5)
            // 그 밖에는 필요·야망·성향·기회로 Rest/Hunt/Trade 중 하나를 고른다 (NPC_AMBITION_DESIGN §6)
            NpcActivityKind kind = companionReserved == true
                ? NpcActivityKind.Rest
                : AR.s.Npc.SelectNextActivity(entityId, saveData, startGameTime, worldSeed);

            // 원정 파티 자동 구성 (D85): 모집 시도 확률 → 후보(대기 None/Rest)별 수락 확률. 아무도 없으면 솔로.
            if (kind == NpcActivityKind.Hunt &&
                AR.s.Npc.TryCreateHuntParty(entityId, kind, startGameTime, worldSeed, out NpcPartyState? party) &&
                party != null)
            {
                StartPartyHunt(party, startGameTime, worldSeed);
                return;
            }

            // DungeonDelve (D55): 파티가 꾸려지면 파티로, 아니면 솔로로 간다. 대상 던전은 소속 마을 슬롯 중 결정론 선택.
            string delveDungeonId = string.Empty;
            Vector2 delveTarget = saveData.Position;
            if (kind == NpcActivityKind.DungeonDelve)
            {
                if (AR.s.Npc.TryCreateHuntParty(entityId, kind, startGameTime, worldSeed,
                        out NpcPartyState? delveParty) && delveParty != null)
                {
                    StartPartyDelve(delveParty, startGameTime, worldSeed);
                    return;
                }
                // 유망한 소문이 있으면 그 슬롯으로 가는 확인 원정 (D76)
                if (AR.s.Npc.TryPrepareDelveTarget(entityId, entityId, saveData.VillageId, saveData.Position,
                        startGameTime, worldSeed, out delveDungeonId, out delveTarget, out _) == false)
                    kind = NpcActivityKind.Rest;   // 후보 판정 뒤 슬롯이 사라진 드문 경우 — 쉬고 다시 고른다
            }

            bool isExpedition = NpcActivityResolver.IsExpedition(kind);
            activity.Kind = kind;
            // 휴식·거래는 제자리 활동이므로 Idle 표현을 유지하고, 원정(Hunt·Delve)만 Venturing으로 표시한다.
            // 거래는 P1에서 상점으로 이동하는 연출이 없다 (D39) — 화면 안 NPC도 상태만 Trade로 바뀌고 기본 AI를 유지한다.
            activity.Phase = isExpedition ? NpcActivityPhase.Venturing : NpcActivityPhase.Idle;
            activity.TargetKey = kind switch
            {
                NpcActivityKind.Hunt => "hunt",
                NpcActivityKind.Trade => "trade",
                NpcActivityKind.DungeonDelve => delveDungeonId,
                _ => "rest",
            };
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.GetActivityDurationHours(kind);
            activity.HasMovementPath = isExpedition;
            if (activity.HasMovementPath)
            {
                float minX = AR.s.WorldMap.minChunkX * AR.s.WorldMap.chunkSize;
                float maxX = (AR.s.WorldMap.maxChunkX + 1) * AR.s.WorldMap.chunkSize - 1f;
                float minY = AR.s.WorldMap.minChunkY * AR.s.WorldMap.chunkSize;
                float maxY = (AR.s.WorldMap.maxChunkY + 1) * AR.s.WorldMap.chunkSize - 1f;
                activity.OriginPosition = saveData.Position;
                activity.TargetPosition = kind == NpcActivityKind.DungeonDelve
                    ? delveTarget
                    : NpcActivityResolver.GetHuntTargetPosition(saveData.Position, entityId,
                        startGameTime, worldSeed, minX, maxX, minY, maxY);
                activity.ReturnPosition = saveData.Position;
                activity.OutboundFraction = 0.5f;
                activity.OutcomeSeedGameTime = startGameTime;
            }
            saveData.LastSimGameTime = startGameTime;
            // 화면 안 NPC는 멈추지 않고 걸어서 떠난다 (NPC_SIMULATION_DESIGN §3.1 D14)
            if (isExpedition)
                AR.s.Npc.ApplyActivityStartPresentation(entityId);
        }

        private static void StartPartyHunt(NpcPartyState party, float startGameTime, int worldSeed)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            if (leader == null)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            float minX = AR.s.WorldMap.minChunkX * AR.s.WorldMap.chunkSize;
            float maxX = (AR.s.WorldMap.maxChunkX + 1) * AR.s.WorldMap.chunkSize - 1f;
            float minY = AR.s.WorldMap.minChunkY * AR.s.WorldMap.chunkSize;
            float maxY = (AR.s.WorldMap.maxChunkY + 1) * AR.s.WorldMap.chunkSize - 1f;
            Vector2 target = NpcActivityResolver.GetHuntTargetPosition(leader.Position, party.PartyId,
                startGameTime, worldSeed, minX, maxX, minY, maxY);
            StartPartyExpedition(party, leader, NpcActivityKind.Hunt, "hunt", target, startGameTime);
        }

        /// <summary>파티 Delve 시작. 대상 던전·경로 목표는 PartyId 기준 결정론으로 정한다 (D55).</summary>
        private static void StartPartyDelve(NpcPartyState party, float startGameTime, int worldSeed)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            // 리더의 유망한 소문이 있으면 그 슬롯으로 가는 확인 원정 (D76)
            if (leader == null || AR.s.Npc.TryPrepareDelveTarget(party.PartyId, party.LeaderId, leader.VillageId,
                    leader.Position, startGameTime, worldSeed, out string dungeonId, out Vector2 target, out _) == false)
            {
                // 파티원을 Idle로 되돌려 다음 순회에서 다시 고르게 한다.
                for (int i = 0; i < party.MemberIds.Count; i++)
                {
                    NpcSaveData? member = AR.s.Npc.GetSaveData(party.MemberIds[i]);
                    if (member == null)
                        continue;
                    AR.s.Npc.MarkSaveDirty(party.MemberIds[i], NpcSaveGroup.ActivityTransition);
                    member.Activity.Kind = NpcActivityKind.None;
                    member.Activity.Phase = NpcActivityPhase.Idle;
                    member.Activity.TargetKey = string.Empty;
                }
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }
            StartPartyExpedition(party, leader, NpcActivityKind.DungeonDelve, dungeonId, target, startGameTime);
        }

        /// <summary>원정 파티(Hunt·DungeonDelve) 공통 시작. 파티가 시간축·경로를 소유하고 파티원은 PartyId만 가진다.</summary>
        private static void StartPartyExpedition(NpcPartyState party, NpcSaveData leader, NpcActivityKind kind,
            string targetKey, Vector2 targetPosition, float startGameTime)
        {
            AR.s.Npc.MarkPartySaveDirty(party.PartyId, PartySaveGroup.Activity);
            NpcActivityState activity = party.Activity;
            activity.PartyId = party.PartyId;
            activity.Kind = kind;
            activity.Phase = NpcActivityPhase.Venturing;
            activity.TargetKey = targetKey;
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.GetActivityDurationHours(kind);
            activity.HasMovementPath = true;
            activity.OriginPosition = leader.Position;
            activity.TargetPosition = targetPosition;
            activity.ReturnPosition = leader.Position;
            activity.OutboundFraction = 0.5f;
            activity.OutcomeSeedGameTime = startGameTime;

            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                NpcSaveData? member = AR.s.Npc.GetSaveData(memberId);
                if (member == null)
                    continue;

                AR.s.Npc.MarkSaveDirty(memberId, NpcSaveGroup.ActivityTransition);
                member.Activity.PartyId = party.PartyId;
                member.Activity.Kind = kind;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = targetKey;
                member.Activity.HasMovementPath = false;
                member.LastSimGameTime = startGameTime;
                // 화면 안 파티원은 각자 같은 방향으로 걸어서 떠나고 각자 강등된다 (D14). 강등 뒤 위치는 파티 경로가 정한다.
                AR.s.Npc.ApplyActivityStartPresentation(memberId);
            }

            AR.s.Npc.UpdatePartyTravelPositions(party, startGameTime);
        }

        private static void AdvancePartyActivity(NpcPartyState party, float now, int worldSeed)
        {
            NpcActivityState activity = party.Activity;
            if (NpcActivityResolver.IsExpedition(activity.Kind) == false || activity.Phase != NpcActivityPhase.Venturing)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            AR.s.Npc.UpdatePartyTravelPositions(party, now);
            if (activity.PhaseEndGameTime > now)
                return;

            // 파티원이 플레이어와 결투 중이면 파티 판정을 결투가 끝날 때까지 미룬다 (§9, B4)
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                if (AR.s.Npc.IsInPlayerDuel(party.MemberIds[i]) == true)
                    return;
            }

            if (activity.Kind == NpcActivityKind.DungeonDelve)
                ResolvePartyDelve(party, activity.PhaseEndGameTime, worldSeed);
            else
                ResolvePartyHunt(party, activity.PhaseEndGameTime, worldSeed);
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
            // 파티원 전투력은 장비 반영 NpcPower를 capability로 옮긴 값 하나로 판정한다 (NPC_AMBITION_DESIGN §5.1)
            float[] capabilities = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                capabilities[i] = AR.s.Npc.GetNpcHuntCapability(memberIds[i]);
            float partyPower = NpcActivityResolver.GetPartyHuntPower(capabilities);
            NpcHuntOutcome[] outcomes = new NpcHuntOutcome[members.Count];
            int successCount = 0;
            int survivorCount = 0;
            // 네임드 위협 마주침 (NAMED_THREAT_DESIGN §6.1·§6.2): 주 마을 파티면 결과 판정 앞에 파티당 한 번. 마주치면 구성원마다 부상 또는 도망
            int threatId = AR.s.Npc.RollNamedThreatEncounter(members[0].VillageId, party.PartyId,
                partyActivity.OutcomeSeedGameTime, worldSeed, NamedThreatRules.PartyEncounterSalt);
            int threatWounded = 0;
            for (int i = 0; i < members.Count; i++)
            {
                NpcHuntOutcome outcome = threatId > 0
                    ? NamedThreatRules.RollEncounterOutcome(
                        NpcActivityResolver.GetPartyMemberCapability(capabilities[i], partyPower, members.Count),
                        party.PartyId, partyActivity.OutcomeSeedGameTime, worldSeed, NamedThreatRules.PartyResultSaltBase + i)
                    : NpcActivityResolver.ResolvePartyMemberHunt(capabilities[i], partyPower,
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
                AR.s.Npc.MarkSaveDirty(memberIds[i], NpcSaveGroup.ActivityTransition);
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
                    // 부상 시점 1회 중상·추상 치료 판정 + 상태 전이·기록 (NPC_AMBITION_DESIGN D36·D91). 네임드는 중상 30%
                    AR.s.Npc.ApplyExpeditionWound(memberIds[i], member, NpcActivityKind.Hunt, party.PartyId,
                        partyActivity.OutcomeSeedGameTime, worldSeed, NpcActivityResolver.PartyTreatmentSaltBase + i,
                        threatId > 0 ? NamedThreatRules.PartySevereSaltBase + i : NpcActivityResolver.PartySevereWoundHuntSaltBase + i,
                        outcomeGameTime, out _, out bool severe, null,
                        threatId > 0 ? NamedThreatRules.SevereWoundChance : -1f, threatId);
                    if (threatId > 0)
                    {
                        AR.s.Npc.RecordNamedThreatEncounter(memberIds[i], member, threatId, true, severe, outcomeGameTime);
                        threatWounded++;
                    }
                }
                else if (outcomes[i] != NpcHuntOutcome.Dead)
                {
                    memberActivity.Phase = NpcActivityPhase.Idle;
                    memberActivity.Kind = NpcActivityKind.None;
                    memberActivity.PhaseEndGameTime = 0f;
                    // 네임드에게서 도망쳐 온 구성원도 소문을 직접 겪은 것으로 얻는다 (§6.2)
                    if (threatId > 0)
                        AR.s.Npc.RecordNamedThreatEncounter(memberIds[i], member, threatId, false, false, outcomeGameTime);
                }
            }

            AR.s.Npc.DissolveParty(party.PartyId);

            if (foodYield > 0 && members[0].VillageId >= 0)
                AR.s.Village.ProduceResource(members[0].VillageId, GlobalEnum.ItemType.Food, foodYield, false);

            // 분배 규칙은 P3 — P1a는 Success 결과를 받은 파티원 각자가 개인 전리품을 1회씩 롤한다 (관리 세션 결정)
            for (int i = 0; i < members.Count; i++)
            {
                if (outcomes[i] != NpcHuntOutcome.Success)
                    continue;
                GrantHuntLoot(members[i], memberIds[i], party.PartyId, partyActivity.OutcomeSeedGameTime, worldSeed,
                    NpcActivityResolver.PartyHuntLootSaltBase + i, outcomeGameTime);
            }

            AR.s.Npc.ApplyPartyRelationshipOutcome(memberIds, outcomes, successCount > 0, outcomeGameTime,
                NpcActivityKind.Hunt);
            Vector2 partyFrom = GetExpeditionFromPosition(partyActivity);
            for (int i = 0; i < members.Count; i++)
            {
                int memberId = memberIds[i];
                if (outcomes[i] == NpcHuntOutcome.Dead)
                {
                    AR.s.Npc.MarkNpcDead(memberId, outcomeGameTime);
                    continue;
                }
                // 화면 안이면 부상자는 여관으로, 나머지는 기본 AI로. 화면 밖이면 귀환점 쪽에서 걸어 들어온다 (NPC_SIMULATION_DESIGN §3.1)
                AR.s.Npc.ApplyExpeditionEndPresentation(memberId);
                AR.s.Npc.TryMaterializeArrivingNpc(memberId, partyFrom);
            }

            NotifyPartyHuntOutcome(party, successCount, survivorCount, foodYield, threatId, threatWounded);
        }

        private static void NotifyPartyHuntOutcome(NpcPartyState party, int successCount, int survivorCount,
            int foodYield, int threatId, int threatWounded)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            // 네임드에게 당한 파티는 보이는 곳이 아니어도 강조색으로 알린다 (NAMED_THREAT_DESIGN §6.5)
            NamedThreatRecord? threat = threatId > 0 && threatWounded > 0 ? AR.s.NamedThreat.GetActive(threatId) : null;
            if (leader != null && threat != null)
            {
                AR.s.UI.PushWorldFeed($"{AR.s.Npc.GetDisplayName(party.LeaderId)} 원정대: 사냥 중 " +
                    $"{NamedThreatManager.GetFeedName(threat)}에게 당해 {threatWounded}명이 부상을 입었습니다.", true);
                return;
            }
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
            // 장비 반영 전투력(용기 제외)을 판정 곡선의 capability로 옮긴다 (NPC_AMBITION_DESIGN §5.1, D31)
            float capability = AR.s.Npc.GetNpcHuntCapability(entityId);
            // 네임드 위협 마주침 (NAMED_THREAT_DESIGN §6.1·§6.2): 주 마을 사냥이면 결과 판정 앞에 한 번. 마주치면 부상 또는 도망(사망 없음)
            int threatId = AR.s.Npc.RollNamedThreatEncounter(saveData.VillageId, entityId, outcomeSeedGameTime, worldSeed,
                NamedThreatRules.SoloEncounterSalt);
            NpcHuntOutcome outcome = threatId > 0
                ? NamedThreatRules.RollEncounterOutcome(capability, entityId, outcomeSeedGameTime, worldSeed, NamedThreatRules.SoloResultSalt)
                : NpcActivityResolver.ResolveHunt(capability, entityId, outcomeSeedGameTime, worldSeed);

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
                NotifyHuntOutcome(saveData, entityId, outcome, 0, threatId);
                // 부상 시점 1회 중상·추상 치료 판정 + 상태 전이·기록 (NPC_AMBITION_DESIGN D36·D91). 네임드는 중상 30%
                AR.s.Npc.ApplyExpeditionWound(entityId, saveData, NpcActivityKind.Hunt, entityId, outcomeSeedGameTime,
                    worldSeed, NpcActivityResolver.TreatmentSalt,
                    threatId > 0 ? NamedThreatRules.SoloSevereSalt : NpcActivityResolver.SevereWoundHuntSalt, outcomeGameTime,
                    out _, out bool severe, null, threatId > 0 ? NamedThreatRules.SevereWoundChance : -1f, threatId);
                if (threatId > 0)
                    AR.s.Npc.RecordNamedThreatEncounter(entityId, saveData, threatId, true, severe, outcomeGameTime);
                // 화면 안 부상자는 여관 문으로 걸어가 들어간다. 화면 밖 부상자는 그대로 여관 안이다 (NPC_SIMULATION_DESIGN §3.1 D13)
                AR.s.Npc.ApplyExpeditionEndPresentation(entityId);
                return;
            }

            activity.Phase = NpcActivityPhase.Idle;
            activity.PhaseEndGameTime = 0f;
            AR.s.Npc.ApplyExpeditionEndPresentation(entityId);

            if (outcome == NpcHuntOutcome.Success && saveData.VillageId >= 0)
            {
                int foodYield = NpcActivityResolver.GetHuntFoodYield(capability, entityId,
                    outcomeSeedGameTime, worldSeed);
                activity.LastHuntFoodYield = foodYield;
                // 오프스크린 Hunt마다 UI 알림을 만들지 않도록 조용히 자원만 반영한다.
                AR.s.Village.ProduceResource(saveData.VillageId, GlobalEnum.ItemType.Food, foodYield, false);
            }

            // Food는 마을로, 전리품은 개인 인벤토리로 간다. Hunt는 골드를 주지 않는다 (D33, D35)
            if (outcome == NpcHuntOutcome.Success)
                GrantHuntLoot(saveData, entityId, entityId, outcomeSeedGameTime, worldSeed,
                    NpcActivityResolver.HuntLootSalt, outcomeGameTime);

            // 네임드에게서 도망쳐 왔다 (빈손). 소문은 직접 겪은 것으로 얻는다 (§6.2)
            if (threatId > 0)
                AR.s.Npc.RecordNamedThreatEncounter(entityId, saveData, threatId, false, false, outcomeGameTime);

            NotifyHuntOutcome(saveData, entityId, outcome, activity.LastHuntFoodYield);
        }

        private static void ResolveDelve(NpcSaveData saveData, int entityId, float outcomeGameTime, int worldSeed)
        {
            NpcDelveReport report = AR.s.Npc.ResolveSoloDelve(entityId, saveData, outcomeGameTime, worldSeed);
            NpcHuntOutcome outcome = report.Outcomes.Count > 0 ? report.Outcomes[0] : NpcHuntOutcome.Failed;
            NotifyDelveOutcome(saveData, entityId, outcome);
            if (outcome == NpcHuntOutcome.Dead)
            {
                // 던전 원정 사망은 사망 Deed에 던전을 남긴다 (NPC_OBJECTIVE_DESIGN §6.1 원수 갚기 대상)
                AR.s.Npc.MarkNpcDead(entityId, outcomeGameTime, report.DungeonId);
                return;
            }
            // 화면 안이면 부상자는 여관으로, 나머지는 기본 AI로 (Hunt와 같음, NPC_SIMULATION_DESIGN §3.1)
            AR.s.Npc.ApplyExpeditionEndPresentation(entityId);
        }

        private static void ResolvePartyDelve(NpcPartyState party, float outcomeGameTime, int worldSeed)
        {
            int leaderId = party.LeaderId;
            int partySize = party.MemberIds.Count;
            Vector2 partyFrom = GetExpeditionFromPosition(party.Activity);
            NpcDelveReport? report = AR.s.Npc.ResolvePartyDelve(party, outcomeGameTime, worldSeed);
            if (report == null)
                return;

            // 파티 호감도 변화는 Hunt와 같은 규칙 (진단과 같은 공용 메서드, D107)
            AR.s.Npc.ApplyPartyRelationshipOutcome(report.MemberIds, report.Outcomes, report.SuccessCount > 0,
                outcomeGameTime, NpcActivityKind.DungeonDelve, report.Completed);
            int survivorCount = 0;
            for (int i = 0; i < report.MemberIds.Count; i++)
            {
                int memberId = report.MemberIds[i];
                if (report.Outcomes[i] == NpcHuntOutcome.Dead)
                {
                    AR.s.Npc.MarkNpcDead(memberId, outcomeGameTime, report.DungeonId);
                    continue;
                }
                survivorCount++;
                // 화면 안이면 부상자는 여관으로, 나머지는 기본 AI로. 화면 밖이면 귀환점 쪽에서 걸어 들어온다 (NPC_SIMULATION_DESIGN §3.1)
                AR.s.Npc.ApplyExpeditionEndPresentation(memberId);
                AR.s.Npc.TryMaterializeArrivingNpc(memberId, partyFrom);
            }

            NpcSaveData? leader = AR.s.Npc.GetSaveData(leaderId);
            if (leader == null || leader.IsActive == false)
                return;
            string leaderName = AR.s.Npc.GetDisplayName(leaderId);
            string message = report.SuccessCount > 0
                ? $"{leaderName} 원정대: 던전 원정에서 돌아왔습니다."
                : $"{leaderName} 원정대: 던전 원정에 실패했습니다.";
            if (survivorCount < partySize)
                message += " 전사자가 발생했습니다.";
            AR.s.UI.PushWorldFeed(message);
        }

        private static void NotifyDelveOutcome(NpcSaveData saveData, int entityId, NpcHuntOutcome outcome)
        {
            // Hunt와 같은 기준: 보이지 않는 NPC의 평범한 성공·실패는 알리지 않고, 부상·사망은 항상 알린다.
            // NPC 던전 완료 자체는 피드로 따로 알리지 않는다 (D59: 소문·Deed·대화로만 드러낸다).
            if (saveData.IsActive == false && outcome != NpcHuntOutcome.Wounded && outcome != NpcHuntOutcome.Dead)
                return;

            string name = AR.s.Npc.GetDisplayName(entityId);
            string message = outcome switch
            {
                NpcHuntOutcome.Success => $"{name}: 던전 원정에서 돌아왔습니다.",
                NpcHuntOutcome.Failed => $"{name}: 던전 원정에 실패했습니다.",
                NpcHuntOutcome.Wounded => $"{name}: 던전 원정 중 부상을 입었습니다.",
                NpcHuntOutcome.Dead => $"{name}: 던전 원정에서 돌아오지 못했습니다.",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(message) == false)
                AR.s.UI.PushWorldFeed(message);
        }

        private static void ResolveTrade(NpcSaveData saveData, int entityId, float outcomeGameTime)
        {
            NpcActivityState activity = saveData.Activity;
            activity.Phase = NpcActivityPhase.Idle;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.PhaseStartedGameTime = outcomeGameTime;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = outcomeGameTime;

            // 거래는 즉시 저장하지 않는다 (D47 ④, Hunt Food와 같은 정책). 소속 마을이 없으면 빈손으로 끝난다.
            if (saveData.VillageId >= 0)
                AR.s.Npc.TradeAtShop(entityId, saveData.VillageId);
        }

        private static void GrantHuntLoot(NpcSaveData saveData, int entityId, int rngOwnerId, float seedGameTime,
            int worldSeed, int salt, float outcomeGameTime)
        {
            int zoneLevel = AR.s.Npc.GetNpcLootZoneLevel(saveData);
            ItemData? loot = NpcActivityResolver.RollHuntLoot(rngOwnerId, seedGameTime, worldSeed, salt, zoneLevel);
            if (loot != null)
                AR.s.Npc.GrantHuntLoot(entityId, saveData, loot, outcomeGameTime);
        }

        private static void NotifyHuntOutcome(NpcSaveData saveData, int entityId,
            NpcHuntOutcome outcome, int foodYield, int threatId = 0)
        {
            // 네임드에게 당한 부상은 칭호(들은 뒤) 또는 "큰 짐승"(듣기 전)으로 강조색 줄을 띄운다 (NAMED_THREAT_DESIGN §6.5)
            NamedThreatRecord? threat = threatId > 0 && outcome == NpcHuntOutcome.Wounded ? AR.s.NamedThreat.GetActive(threatId) : null;
            if (threat != null)
            {
                AR.s.UI.PushWorldFeed($"{AR.s.Npc.GetDisplayName(entityId)}: 사냥 중 {NamedThreatManager.GetFeedName(threat)}에게 당해 " +
                    "부상을 입었습니다.", true);
                return;
            }

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
