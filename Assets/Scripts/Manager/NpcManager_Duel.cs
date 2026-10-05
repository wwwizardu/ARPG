#nullable enable
using UnityEngine;
using ARPG.AI;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Utility;

namespace ARPG.Npc
{
    /// <summary>
    /// 플레이어 ↔ NPC 결투 런타임 (NPC_COMBAT_DESIGN §7.1~7.4, B4).
    /// 결투 상태는 두 참가자의 DuelComponent가 소유한다(저장하지 않음). 이 partial은 신청·시작·HP 0 가로채기·종료 처리만 담당하고,
    /// 매 틱 진행(시간 제한·참가자 소멸 감지·패배 확정 종료)은 System_Duel이 호출한다.
    /// 명분 판정·NPC 응답·결과의 사회적 파장은 AMBITION §8.8 소관이며 B4에서는 스텁이다(항상 명분 없음, 항상 수락, Deeds 기록만).
    /// NPC 간 결투(§7.5)는 D24로 제외.
    /// </summary>
    public partial class NpcManager
    {
        /// <summary>결투 시간 제한 — 실제 시간 3분이 지나면 무승부 [D27].</summary>
        public const float DuelTimeLimitSeconds = 180f;

        // 결과 처리 훅에 넘길 입회인(주변 NPC) 수집 반경
        private const float DUEL_WITNESS_RANGE = 10f;
        private const string DUEL_PLAYER_DISPLAY_NAME = "모험가";

        /// <summary>플레이어가 결투 중인지. 저장·상호작용·던전 전환 거부와 결투 메뉴 표시가 사용한다.</summary>
        public bool IsPlayerInDuel
        {
            get
            {
                int playerId = AR.s.Data.CurrentPlayerEntityId;
                return playerId >= 0 && AR.s.Component.HasComponent<DuelComponent>(playerId) == true;
            }
        }

        /// <summary>플레이어와 결투 중인 NPC인지. System_NpcSim 활동 선택·판정 제외 조건(§9).</summary>
        public bool IsInPlayerDuel(int npcId)
        {
            return npcId >= 0 && AR.s.Component.HasComponent<DuelComponent>(npcId) == true;
        }

        #region 신청 / 시작 (대화 메뉴)

        /// <summary>
        /// 결투 신청 가능 여부. 플레이어 → NPC 신청에는 NPC 상태·장소 제한이 없다 [D19] — 부상·원정·동행 중·던전 안 모두 가능.
        /// 여기서는 실체화 여부와 진행 중인 결투(한 번에 1건) 같은 기술적 조건만 본다. 재신청 쿨다운은 없다(Q15).
        /// </summary>
        public bool CanRequestDuel(int npcId, out string reason)
        {
            reason = string.Empty;
            ComponentManager cm = AR.s.Component;
            int playerId = AR.s.Data.CurrentPlayerEntityId;

            if (AR.s.Dungeon.GameplayPaused == true)
            {
                reason = "지금은 결투를 신청할 수 없습니다.";
                return false;
            }
            if (playerId < 0 || cm.HasComponent<TransformComponent>(playerId) == false ||
                cm.TryGetComponent<StatComponent>(playerId, out StatComponent playerStat) == false ||
                playerStat.CurrentHp <= 0)
            {
                reason = "지금은 결투를 신청할 수 없습니다.";
                return false;
            }
            if (cm.HasComponent<DuelComponent>(playerId) == true)
            {
                reason = "이미 결투 중입니다.";
                return false;
            }

            NpcSaveData? saveData = GetSaveData(npcId);
            if (saveData == null)
            {
                reason = "알 수 없는 NPC입니다.";
                return false;
            }
            if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
            {
                reason = "세상을 떠난 NPC입니다.";
                return false;
            }
            if (cm.HasComponent<NpcTag>(npcId) == false || cm.HasComponent<TransformComponent>(npcId) == false ||
                cm.HasComponent<AIStateComponent>(npcId) == false || cm.HasComponent<DestroyTag>(npcId) == true ||
                cm.TryGetComponent<StatComponent>(npcId, out StatComponent npcStat) == false || npcStat.CurrentHp <= 0)
            {
                reason = "지금은 결투할 수 없는 상대입니다.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 대화 메뉴 "결투 신청". 명분 판정·NPC 응답은 스텁(항상 명분 없음, 항상 수락)이다(§7.4, C2에서 교체).
        /// 수락되면 그 자리에서 바로 결투를 시작한다 [D20].
        /// </summary>
        public bool RequestDuel(int npcId, out string message)
        {
            if (CanRequestDuel(npcId, out message) == false)
                return false;

            DuelGrievance grievance = ResolveDuelGrievance(npcId);
            if (ResolveDuelResponse(npcId, grievance) == false)
            {
                message = $"{GetDisplayName(npcId)}: 싸울 이유가 없소.";
                return false;
            }

            StartPlayerDuel(AR.s.Data.CurrentPlayerEntityId, npcId, grievance);
            message = $"{GetDisplayName(npcId)}: 좋소, 받아들이지.";
            return true;
        }

        /// <summary>명분 판정 스텁 (AMBITION §8.8). B4는 항상 "명분 없음".</summary>
        private static DuelGrievance ResolveDuelGrievance(int npcId)
        {
            return DuelGrievance.None;
        }

        /// <summary>NPC 응답 스텁 (AMBITION §8.8). B4는 항상 수락.</summary>
        private static bool ResolveDuelResponse(int npcId, DuelGrievance grievance)
        {
            return true;
        }

        private void StartPlayerDuel(int playerId, int npcId, DuelGrievance grievance)
        {
            ComponentManager cm = AR.s.Component;
            cm.AddComponent(playerId, new DuelComponent
            {
                OpponentId = npcId,
                ElapsedSeconds = 0f,
                PendingLoserId = -1,
                Grievance = grievance,
            });
            cm.AddComponent(npcId, new DuelComponent
            {
                OpponentId = playerId,
                ElapsedSeconds = 0f,
                PendingLoserId = -1,
                Grievance = grievance,
            });

            // 원정 중 화면 안에서 대기하던 NPC(NpcActivityPausedTag)도 결투할 수 있다 [D19].
            // 결투 동안만 대기를 풀고, 활동 판정은 System_NpcSim이 결투 중 NPC를 건너뛰어 보류한다(§9). 종료 시 다시 대기시킨다.
            cm.RemoveComponent<NpcActivityPausedTag>(npcId);
            cm.AddComponent(npcId, new NpcCombatEnabledTag());

            if (cm.TryGetComponent<AIComponent>(npcId, out AIComponent ai) == true &&
                cm.TryGetComponent<TransformComponent>(playerId, out TransformComponent playerTransform) == true)
            {
                ai.TargetEntityId = playerId;
                ai.LastKnownTargetPos = playerTransform.Position;
                cm.SetComponent(npcId, ai);
                cm.AddComponent(npcId, new AICanSeeTargetTag());
            }
            AIStateHelper.TransitionToState(npcId, AIState.Chase);

            NotifyDuel($"{GetDisplayName(npcId)}와(과)의 결투가 시작되었습니다! (제한 시간 {DuelTimeLimitSeconds / 60f:F0}분)");
            Debug.Log($"[NpcDuel] 결투 시작 - Player: {playerId}, Npc: {npcId}, Grievance: {grievance}");
        }

        #endregion

        #region 진행 (System_HpCheck / System_Duel)

        /// <summary>
        /// System_HpCheck가 HP 0 이하 엔티티를 사망 처리하기 전에 호출한다 [D22].
        /// 결투 참가자면 HP 1로 두고 패배를 확정해(종료는 System_Duel이 다음 틱에 처리) 사망 경로를 막는다.
        /// 원인과 관계없다 — 몬스터 공격으로 HP 0이 되어도 그 참가자가 패자다(Q11).
        /// </summary>
        /// <returns>결투 참가자라 사망을 가로챘으면 true</returns>
        public bool TryInterceptDuelDefeat(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<DuelComponent>(entityId, out DuelComponent duel) == false)
                return false;

            RaiseHpToOne(entityId);

            // 같은 틱에 두 참가자가 모두 HP 0이 되면 먼저 처리된 쪽이 패자다. 아무도 죽지 않도록 상대 HP도 1 이상으로 둔다.
            if (duel.PendingLoserId < 0)
            {
                duel.PendingLoserId = entityId;
                cm.SetComponent(entityId, duel);
                if (cm.TryGetComponent<DuelComponent>(duel.OpponentId, out DuelComponent opponentDuel) == true)
                {
                    opponentDuel.PendingLoserId = entityId;
                    cm.SetComponent(duel.OpponentId, opponentDuel);
                }
            }
            RaiseHpToOne(duel.OpponentId);
            return true;
        }

        /// <summary>
        /// 결투를 끝낸다. System_Duel이 패배 확정·시간 제한·참가자 소멸 시 호출한다.
        /// 결과 처리 훅(§7.4) → 컴포넌트·태그 제거, AI 원복(NPC는 대기 상태) → 동행 중이던 NPC는 마을로 귀환 [D26] → 한 번 저장.
        /// </summary>
        /// <param name="participantId">결투 참가자 중 하나 (플레이어 또는 NPC)</param>
        /// <param name="loserId">패자 EntityId. 무승부면 -1</param>
        /// <param name="saveAfter">false면 결투 뒤 저장을 하지 않는다 (던전 입장·퇴장·완료 커밋이 대신 저장)</param>
        public void EndPlayerDuel(int participantId, DuelEndReason reason, int loserId, bool saveAfter = true)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<DuelComponent>(participantId, out DuelComponent duel) == false)
                return;

            int playerId = AR.s.Data.CurrentPlayerEntityId;
            int npcId = participantId == playerId ? duel.OpponentId : participantId;

            NpcDuelOutcome outcome = new NpcDuelOutcome
            {
                PlayerId = playerId,
                NpcId = npcId,
                LoserId = loserId,
                WinnerId = loserId < 0 ? -1 : (loserId == playerId ? npcId : playerId),
                Reason = reason,
                Grievance = duel.Grievance,
                EndedGameTime = AR.s.Time.CurrentGameTime,
            };
            CollectDuelWitnesses(outcome);

            // 적대 전환은 결투 중에만 — 끝나면 즉시 원래대로 (§7.2)
            cm.RemoveComponent<DuelComponent>(playerId);
            cm.RemoveComponent<DuelComponent>(npcId);
            ClearDuelParticipantBuffs(playerId, npcId);
            RaiseHpToOne(playerId);
            RaiseHpToOne(npcId);

            RestoreNpcAfterDuel(npcId);
            ApplyDuelOutcome(outcome);
            NotifyDuel(BuildDuelResultMessage(outcome));
            Debug.Log($"[NpcDuel] 결투 종료 - Npc: {npcId}, Reason: {reason}, Winner: {outcome.WinnerId}, " +
                      $"Loser: {outcome.LoserId}, Witnesses: {outcome.Witnesses.Count}");

            // 동행 중(던전 안)인 NPC와의 결투는 결과와 관계없이 동행을 끝내고 마을로 돌려보낸다 [D26]
            EndCompanionAfterDuel(npcId);

            // 일반 결투 종료 결과를 한 번 저장한다 (§7.2). Deeds(NPC 파티션)가 남도록 NpcState를 포함한다.
            if (saveAfter == true)
            {
                SaveScope scope = SaveScope.Player | SaveScope.World | SaveScope.NpcState |
                                  (AR.s.Dungeon.IsInside == true ? SaveScope.Dungeons : SaveScope.None);
                _ = AR.s.Data.SaveAsync(scope);
            }
        }

        #endregion

        #region 종료 처리

        /// <summary>결투 중 적용된 DoT를 포함해 두 참가자의 해제 가능한 버프·디버프를 제거한다.</summary>
        private static void ClearDuelParticipantBuffs(int playerId, int npcId)
        {
            BuffHelper.RemoveAllDispellableBuffs(playerId);
            BuffHelper.RemoveAllDispellableBuffs(npcId);

#if UNITY_EDITOR
            if (BuffHelper.GetDispellableBuffCount(playerId) != 0 ||
                BuffHelper.GetDispellableBuffCount(npcId) != 0)
                Debug.LogError($"[NpcDuel] 결투 종료 후 상태이상 정리 실패 - Player: {playerId}, Npc: {npcId}");
#endif
        }

        /// <summary>
        /// 결투 뒤 NPC는 싸움을 멈추고 대기 상태(기본 AI 상태)로 돌아간다 (§7.2).
        /// 전투 허용 태그는 동행 중이 아닌 NPC만 뗀다(동행 NPC는 이어지는 귀환 처리가 정리). 원정 대기 중이던 NPC는 다시 대기시킨다.
        /// </summary>
        private void RestoreNpcAfterDuel(int npcId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.HasComponent<NpcTag>(npcId) == false)
                return;

            if (cm.HasComponent<NpcCompanionComponent>(npcId) == false)
                cm.RemoveComponent<NpcCombatEnabledTag>(npcId);

            if (cm.TryGetComponent<AIComponent>(npcId, out AIComponent ai) == true)
                AIStateHelper.ClearTarget(npcId, ref ai);
            AIStateHelper.StopMovement(npcId);
            AIStateHelper.TransitionToState(npcId, AIStateHelper.GetDefaultState(npcId));

            // 원정(Hunt 경로·파티) 중 화면 안에서 대기하던 NPC는 활동이 아직 진행 중이면 다시 대기 상태로 둔다
            PauseMaterializedNpcActivity(npcId);
        }

        /// <summary>
        /// 결과 처리 훅 (§7.4). AMBITION §8.8 규칙이 끼워질 자리다.
        /// B4: 명분·응답은 스텁, 관계 변화 없이 Deeds에 결과만 기록한다(Q12). 플레이어 쪽 Deeds 저장소는 아직 없다.
        /// </summary>
        private void ApplyDuelOutcome(NpcDuelOutcome outcome)
        {
            MarkSaveDirty(outcome.NpcId, NpcSaveGroup.Deeds | NpcSaveGroup.Stories | NpcSaveGroup.Activity | NpcSaveGroup.Condition);
            if (_npcSaveDict.TryGetValue(outcome.NpcId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedKind kind = outcome.IsDraw == true
                ? NpcDeedKind.DuelDrawn
                : (outcome.WinnerId == outcome.NpcId ? NpcDeedKind.DuelWon : NpcDeedKind.DuelLost);

            NpcDeedEntry entry = new NpcDeedEntry(kind, -1, NpcKnowledgeLevel.Did, outcome.EndedGameTime);
            AddDeedParticipant(entry, outcome.NpcId);
            entry.Participants.Add(outcome.PlayerId);
            entry.ParticipantSnapshots.Add(new NpcDeedParticipant(outcome.PlayerId, DUEL_PLAYER_DISPLAY_NAME));
            saveData.RecordDeed(entry);
            _narrative.InvalidateDialogues(outcome.NpcId);
        }

        /// <summary>입회인: NPC 참가자 주변(반경 10타일)의 실체화된 NPC. 참가자는 제외한다 (§7.2, 목격 소문은 AMBITION 소관).</summary>
        private static void CollectDuelWitnesses(NpcDuelOutcome outcome)
        {
            ComponentManager cm = AR.s.Component;
            int centerId = cm.HasComponent<TransformComponent>(outcome.NpcId) == true ? outcome.NpcId : outcome.PlayerId;
            if (cm.TryGetComponent<TransformComponent>(centerId, out TransformComponent center) == false)
                return;

            float rangeSqr = DUEL_WITNESS_RANGE * DUEL_WITNESS_RANGE;
            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            for (int i = 0; i < npcPool.Count; i++)
            {
                int candidateId = npcPool.GetEntityId(i);
                if (candidateId == outcome.NpcId || candidateId == outcome.PlayerId)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(candidateId, out TransformComponent candidate) == false)
                    continue;
                if ((candidate.Position - center.Position).sqrMagnitude > rangeSqr)
                    continue;

                outcome.Witnesses.Add(candidateId);
            }
        }

        private string BuildDuelResultMessage(NpcDuelOutcome outcome)
        {
            string npcName = GetDisplayName(outcome.NpcId);
            if (outcome.IsDraw == false)
                return outcome.WinnerId == outcome.PlayerId
                    ? $"{npcName}와(과)의 결투에서 승리했습니다."
                    : $"{npcName}와(과)의 결투에서 패배했습니다.";

            return outcome.Reason switch
            {
                DuelEndReason.Timeout => $"{npcName}와(과)의 결투가 시간 초과로 무승부가 되었습니다.",
                _ => $"{npcName}와(과)의 결투가 무승부로 끝났습니다.",
            };
        }

        private static void RaiseHpToOne(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == false || stat.CurrentHp > 0)
                return;

            stat.SetCurrentHp(entityId, 1);
            cm.SetComponent(entityId, stat);
        }

        private static void NotifyDuel(string message)
        {
            Debug.Log("[NpcDuel] " + message);
            AR.s.UI.SetNotify(message);
        }

        #endregion
    }
}
