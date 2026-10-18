#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Creature;
using ARPG.History;
using ARPG.Village;
using UnityEngine;

using ARPG.Data;

namespace ARPG.Npc
{
    /// <summary>
    /// 원정 파티 자동 구성 (NPC_AMBITION_DESIGN §7.5, D55·D85).
    /// - 리더(방금 Hunt/DungeonDelve를 고른 대기 NPC)가 모집 시도 확률을 통과할 때만 동료를 찾는다.
    /// - 후보 = 같은 마을·20타일 이내의 대기 NPC 중 활동 없음(None) 또는 휴식(Rest). 거래(Trade) 중은 제외.
    /// - 후보를 모집 점수순으로 보며 후보마다 수락 확률을 굴린다. 최대 3명(파티 4명). 아무도 수락하지 않으면 솔로.
    /// - 휴식 중 합류하면 휴식이 끊기고 그 휴식의 소문 교환(D74)은 건너뛴다.
    /// 상태는 NpcSaveData·NpcPartyState에 있고, 이 파일의 인스턴스 필드는 휘발성 진단 카운터와 매번 비우는 스크래치 버퍼뿐이다.
    /// </summary>
    public partial class NpcManager
    {
        // 모집 범위: 리더로부터 20타일 (제곱 거리)
        private const float PartyRecruitmentRangeSqr = 400f;
        // 파티 최대 인원 (리더 포함)
        public const int MaxPartySize = 4;

        // 파티 진단 카운터 (휘발성). Reset에서 새로 만든다. 진단 도구는 시뮬레이션 동안 새 인스턴스로 바꿔 끼운다.
        private NpcPartyCounters _partyCounters = new();
        // 모집 후보 스크래치 버퍼 — 사용 전에 매번 Clear하므로 호출 사이에 상태를 남기지 않는다.
        private readonly List<int> _partyCandidateScratch = new();
        private readonly List<int> _partyAcceptedScratch = new();

        public NpcPartyCounters PartyCounters => _partyCounters;

        /// <summary>
        /// 원정 파티 자동 구성 (D85). Hunt와 DungeonDelve가 같은 규칙을 쓴다 (D55 "기존 자동 구성 재사용").
        /// 결정론 RNG 기준 = (리더 EntityId, 리더의 활동 결정 시각 startGameTime, worldSeed).
        /// 파티가 꾸려지면 파티원 상태를 원정 준비 상태로 바꾸고 true. 실패하면 아무 상태도 바꾸지 않고 false(솔로).
        /// </summary>
        public bool TryCreateHuntParty(int leaderId, NpcActivityKind kind, float startGameTime, int worldSeed,
            out NpcPartyState? party)
        {
            MarkSaveDirty(leaderId, NpcSaveGroup.Activity);
            party = null;
            if (NpcActivityResolver.IsExpedition(kind) == false)
                return false;
            if (_npcSaveDict.TryGetValue(leaderId, out NpcSaveData? leader) == false ||
                IsEligibleExpeditionPartyLeader(leader) == false)
                return false;

            VillageData? village = AR.s.Village.GetVillage(leader.VillageId);
            if (village == null)
                return false;

            int kindIndex = kind == NpcActivityKind.DungeonDelve ? 1 : 0;
            _partyCounters.Attempts[kindIndex]++;

            // ① 모집 시도 확률 (D85 ②). 실패하면 후보를 보지 않고 솔로.
            // 단기 목표 원정(AlwaysRecruit)은 시도 확률을 건너뛰고 항상 모집한다 (NPC_OBJECTIVE_DESIGN §7.2). 난수열은 따로라 영향이 없다.
            HistoryRng attemptRng = NpcActivityResolver.CreateRng(leaderId, startGameTime, worldSeed,
                NpcActivityResolver.PartyRecruitAttemptSalt);
            if (ShouldAlwaysRecruitForObjective(leader, kind, startGameTime) == false &&
                attemptRng.NextDouble() >= NpcActivityResolver.GetPartyRecruitAttemptChance(leader.NpcStat))
                return false;
            _partyCounters.AttemptsPassed[kindIndex]++;

            // ② 후보 수집 (D85 ①)
            _partyCandidateScratch.Clear();
            List<int> villageNpcIds = village.NpcEntityIds;
            for (int i = 0; i < villageNpcIds.Count; i++)
            {
                int candidateId = villageNpcIds[i];
                if (candidateId == leaderId || _npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false)
                    continue;
                NpcPartyRejectReason reason = GetPartyCandidateRejectReason(leaderId, leader, candidate, kind, startGameTime);
                if (reason != NpcPartyRejectReason.None)
                {
                    _partyCounters.Rejections[(int)reason]++;
                    // 진단용: 적대 탈락 중 원한(GrudgeAgainst, 방향 무관)이 있는 경우 — 나머지는 라이벌만 (P4a 절도 원한 누적 확인)
                    if (reason == NpcPartyRejectReason.Hostile &&
                        GetRelation(leaderId, candidateId, NpcRelationType.GrudgeAgainst) != null)
                        _partyCounters.HostileGrudge++;
                    continue;
                }
                _partyCandidateScratch.Add(candidateId);
            }
            _partyCounters.RecordCandidateCount(_partyCandidateScratch.Count);

            _partyCandidateScratch.Sort((leftId, rightId) =>
            {
                int leftScore = GetPartyRecruitmentScore(leaderId, leftId);
                int rightScore = GetPartyRecruitmentScore(leaderId, rightId);
                if (leftScore != rightScore)
                    return rightScore.CompareTo(leftScore);
                return leftId.CompareTo(rightId);
            });

            // ③ 수락 확률 (D85 ③). 점수순으로 한 명씩, 리더 소유 난수열에서 후보마다 1개씩 뽑는다.
            _partyAcceptedScratch.Clear();
            HistoryRng acceptRng = NpcActivityResolver.CreateRng(leaderId, startGameTime, worldSeed,
                NpcActivityResolver.PartyAcceptSalt);
            for (int i = 0; i < _partyCandidateScratch.Count; i++)
            {
                if (_partyAcceptedScratch.Count >= MaxPartySize - 1)
                {
                    _partyCounters.Rejections[(int)NpcPartyRejectReason.PartyFull] += _partyCandidateScratch.Count - i;
                    break;
                }
                int candidateId = _partyCandidateScratch[i];
                NpcSaveData candidate = _npcSaveDict[candidateId];
                int affinity = GetAffinity(leaderId, candidateId);
                bool closeRelation = HasClosePartyRelation(leaderId, candidateId);
                float chance = NpcActivityResolver.GetPartyAcceptChance(affinity, closeRelation, candidate.NpcStat.Courage,
                    GetPartyRestedFraction(candidate, startGameTime));
                // 원수 갚기 원정: 그 사망자를 기억하는 후보 +0.20, 상한 그대로 (NPC_OBJECTIVE_DESIGN §7.2)
                float objectiveBonus = GetObjectiveAcceptBonus(leader, candidate, kind, startGameTime);
                if (objectiveBonus > 0f)
                    chance = Mathf.Min(NpcActivityResolver.PartyAcceptChanceMax, chance + objectiveBonus);
                int bucket = NpcPartyCounters.GetAcceptBucket(affinity, closeRelation);
                _partyCounters.AcceptRolls[bucket]++;
                _partyCounters.AcceptChanceSum[bucket] += chance;
                if (acceptRng.NextDouble() >= chance)
                {
                    _partyCounters.Rejections[(int)NpcPartyRejectReason.Declined]++;
                    continue;
                }
                _partyCounters.Accepted[bucket]++;
                _partyAcceptedScratch.Add(candidateId);
            }
            _partyCandidateScratch.Clear();

            if (_partyAcceptedScratch.Count == 0)
            {
                _partyCounters.NobodyAccepted[kindIndex]++;
                return false;
            }

            int partyId = _nextPartyId++;
            NpcPartyState created = new NpcPartyState
            {
                PartyId = partyId,
                LeaderId = leaderId,
            };
            created.MemberIds.Add(leaderId);
            for (int i = 0; i < _partyAcceptedScratch.Count; i++)
                created.MemberIds.Add(_partyAcceptedScratch[i]);
            created.MemberIds.Sort();
            created.Activity.PartyId = partyId;
            _partyCounters.PartiesBySize[kindIndex, created.MemberIds.Count]++;

            // 휴식 중 합류한 파티원은 먼저 휴식을 정리한다 (끊김 → 소문 교환 생략, 이미 끝난 휴식 → 정상 종료).
            for (int i = 0; i < _partyAcceptedScratch.Count; i++)
            {
                int memberId = _partyAcceptedScratch[i];
                MarkSaveDirty(memberId, NpcSaveGroup.Activity);
                NpcSaveData member = _npcSaveDict[memberId];
                if (member.Activity.Kind == NpcActivityKind.Rest)
                    EndRestForParty(memberId, member, startGameTime, worldSeed);
            }
            _partyAcceptedScratch.Clear();

            MarkPartySaveDirty(partyId);
            _npcPartyDict.Add(partyId, created);
            AddPartyToSimulation(partyId);
            for (int i = 0; i < created.MemberIds.Count; i++)
            {
                int memberId = created.MemberIds[i];
                MarkSaveDirty(memberId, NpcSaveGroup.Activity);
                NpcSaveData member = _npcSaveDict[memberId];
                member.Activity.PartyId = partyId;
                member.Activity.Kind = kind;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = kind == NpcActivityKind.Hunt ? "hunt" : "delve";
                member.Activity.HasMovementPath = false;
            }

            party = created;
            return true;
        }

        /// <summary>
        /// 리더 자격: 지금 막 활동을 고른 대기 NPC(Phase Idle, Kind None). 휴식 중인 NPC는 스스로 파티를 만들지 않는다.
        /// 동행 예약 NPC는 원정을 떠나지 않고(NPC_COMBAT_DESIGN §4.5), 플레이어와 결투 중인 NPC도 제외한다(§9, B4).
        /// </summary>
        private bool IsEligibleExpeditionPartyLeader(NpcSaveData saveData)
        {
            return saveData.Condition != CharacterConditions.Dead &&
                   saveData.Activity.Phase == NpcActivityPhase.Idle &&
                   saveData.Activity.Kind == NpcActivityKind.None &&
                   saveData.Activity.PartyId < 0 &&
                   IsCompanionReserved(saveData.EntityId) == false &&
                   IsInPlayerDuel(saveData.EntityId) == false;
        }

        /// <summary>
        /// 후보 자격 (D85 ①): 사망·파티 소속·동행 예약·결투 중·다른 마을·20타일 밖·Rival/Grudge는 제외.
        /// 대기(Phase Idle) 중 Kind None 또는 Rest만 인정한다(Trade 제외). 리더의 결정 시각보다 늦게 시작한 휴식은 제외한다
        /// (시간 점프 뒤 과거 구간을 결제하는 리더가 "아직 시작하지 않은" 휴식을 끊지 않게).
        /// 실행 단계 단기 목표 주인(SkipOtherParties)은 빠진다 — 같은 던전을 노리는 리더의 던전 원정만 예외 (NPC_OBJECTIVE_DESIGN §7.2).
        /// </summary>
        private NpcPartyRejectReason GetPartyCandidateRejectReason(int leaderId, NpcSaveData leader, NpcSaveData candidate,
            NpcActivityKind kind, float startGameTime)
        {
            NpcActivityState activity = candidate.Activity;
            if (candidate.Condition == CharacterConditions.Dead || activity.Phase == NpcActivityPhase.Dead)
                return NpcPartyRejectReason.Dead;
            if (candidate.VillageId != leader.VillageId)
                return NpcPartyRejectReason.OtherVillage;
            if (activity.PartyId >= 0)
                return NpcPartyRejectReason.InParty;
            if (activity.Phase != NpcActivityPhase.Idle)
                return NpcPartyRejectReason.Busy;
            if (activity.Kind == NpcActivityKind.Trade)
                return NpcPartyRejectReason.Trade;
            if (activity.Kind == NpcActivityKind.Rest)
            {
                if (activity.PhaseStartedGameTime > startGameTime)
                    return NpcPartyRejectReason.RestNotStarted;
            }
            else if (activity.Kind != NpcActivityKind.None)
                return NpcPartyRejectReason.Busy;
            if (IsCompanionReserved(candidate.EntityId) == true || IsInPlayerDuel(candidate.EntityId) == true)
                return NpcPartyRejectReason.ReservedOrDuel;
            Vector2 offset = candidate.Position - leader.Position;
            if (offset.sqrMagnitude > PartyRecruitmentRangeSqr)
                return NpcPartyRejectReason.Distance;
            if (IsHostilePartyRelation(leaderId, candidate.EntityId) == true)
                return NpcPartyRejectReason.Hostile;
            if (IsObjectiveBusyForParty(leader, candidate, kind, startGameTime) == true)
                return NpcPartyRejectReason.ObjectiveFocus;
            return NpcPartyRejectReason.None;
        }

        /// <summary>쉰 정도 0~1: 휴식 중이면 경과 시간 / 휴식 길이, 활동 없음(None)이면 1.</summary>
        private static float GetPartyRestedFraction(NpcSaveData candidate, float startGameTime)
        {
            if (candidate.Activity.Kind != NpcActivityKind.Rest)
                return 1f;
            return Mathf.Clamp01((startGameTime - candidate.Activity.PhaseStartedGameTime) /
                                 NpcActivityResolver.RestDurationHours);
        }

        /// <summary>
        /// 파티 합류로 휴식을 끝낸다. 휴식이 리더 결정 시각 전에 이미 끝나 있었으면(순회가 아직 방문하지 않았을 뿐)
        /// 정상 종료처럼 소문 교환(D74)을 한다. 아직 휴식 중이면 끊기고 소문 교환은 건너뛴다(D85 ①).
        /// 이후 상태는 휴식이 그 시각에 끝난 활동 없음(None) 대기 NPC와 같다 — 파티원 전이는 호출자가 이어서 한다.
        /// </summary>
        private void EndRestForParty(int entityId, NpcSaveData saveData, float startGameTime, int worldSeed)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Activity);
            NpcActivityState activity = saveData.Activity;
            float endTime;
            if (activity.PhaseEndGameTime <= startGameTime)
            {
                endTime = activity.PhaseEndGameTime < activity.PhaseStartedGameTime
                    ? startGameTime
                    : activity.PhaseEndGameTime;
                _partyCounters.RestCompletedBeforeJoin++;
                // Rest 종료 공용 진입점 (D107). 파티 합류로 정리하는 휴식에서는 절도를 판정하지 않는다 (D100).
                ResolveRestEnd(entityId, saveData, activity.PhaseStartedGameTime, endTime, worldSeed, false);
            }
            else
            {
                endTime = startGameTime;
                _partyCounters.RestInterrupted++;
                // 진단용: 건너뛴 휴식에서 교환 확률 롤이 통과했을지 (같은 RNG로 계산만 하고 상태는 바꾸지 않는다)
                HistoryRng exchangeRng = NpcActivityResolver.CreateRng(entityId, activity.PhaseStartedGameTime, worldSeed,
                    NpcActivityResolver.RumorExchangeSalt);
                if (exchangeRng.NextDouble() < NpcActivityResolver.GetRumorExchangeChance(saveData.NpcStat.Friendliness))
                    _partyCounters.RumorExchangesSkipped++;
            }

            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.PhaseStartedGameTime = endTime;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = startGameTime;
        }

        /// <summary>
        /// 파티 원정(Hunt·Delve) 결과의 호감도 변화 (§7.5). 생존한 파티원 쌍마다 성공이 하나라도 있으면 +4, 아니면 +1을 주고
        /// 공동 활동 횟수와 마지막 시각은 NPC 쌍의 호감도 통계에 저장한다. 게임과 진단이 같은 진입점을 사용한다.
        /// </summary>
        public void ApplyPartyRelationshipOutcome(IReadOnlyList<int> memberIds, IReadOnlyList<NpcHuntOutcome> outcomes,
            bool hasSuccess, float outcomeGameTime, NpcActivityKind activity, bool dungeonCompleted = false)
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
                    ModifyAffinity(memberIds[i], memberIds[j], affinityDelta);
                    bool clearedTogether = dungeonCompleted == true && outcomes[i] == NpcHuntOutcome.Success &&
                                           outcomes[j] == NpcHuntOutcome.Success;
                    RecordSharedActivity(memberIds[i], memberIds[j], activity, affinityDelta, outcomeGameTime,
                        clearedTogether);
                }
            }
        }

        private bool IsHostilePartyRelation(int leaderId, int candidateId)
        {
            return GetRelation(leaderId, candidateId, NpcRelationType.Rival) != null ||
                   GetRelation(leaderId, candidateId, NpcRelationType.GrudgeAgainst) != null;
        }

        /// <summary>리더와 친구·형제·배우자 중 하나라도 있는가 (수락 확률 관계 보너스).</summary>
        private bool HasClosePartyRelation(int leaderId, int candidateId)
        {
            return GetRelation(leaderId, candidateId, NpcRelationType.Spouse) != null ||
                   GetRelation(leaderId, candidateId, NpcRelationType.Sibling) != null ||
                   GetRelation(leaderId, candidateId, NpcRelationType.Friend) != null;
        }

        private int GetPartyRecruitmentScore(int leaderId, int candidateId)
        {
            int score = GetAffinity(leaderId, candidateId);
            if (GetRelation(leaderId, candidateId, NpcRelationType.Spouse) != null)
                score += 160;
            else if (GetRelation(leaderId, candidateId, NpcRelationType.Sibling) != null)
                score += 120;
            else if (GetRelation(leaderId, candidateId, NpcRelationType.Friend) != null)
                score += 80;

            if (_npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate))
                score += candidate.NpcStat.Courage / 10 + candidate.SkillLevel * 2;
            return score;
        }

        private void ResetPartyRuntime()
        {
            _partyCounters = new NpcPartyCounters();
            _partyCandidateScratch.Clear();
            _partyAcceptedScratch.Clear();
        }
    }

    /// <summary>파티 후보 탈락 사유 (진단 카운터 인덱스).</summary>
    public enum NpcPartyRejectReason
    {
        None,
        Dead,
        OtherVillage,
        InParty,
        Busy,             // 원정·부상 중 (Phase가 Idle 아님)
        Trade,
        RestNotStarted,   // 리더 결정 시각보다 늦게 시작한 휴식
        ReservedOrDuel,
        Distance,
        Hostile,
        Declined,         // 수락 확률 롤 실패
        PartyFull,        // 이미 3명이 수락해 보지 않은 후보
        ObjectiveFocus,   // 실행 단계 단기 목표 주인 (다른 리더의 모집에서 빠짐, NPC_OBJECTIVE_DESIGN §7.2)
        Count,
    }

    /// <summary>
    /// 파티 구성 진단 카운터 (휘발성, 세이브 대상 아님). NpcManager 인스턴스가 소유하고 Reset에서 새로 만든다.
    /// 진단 도구가 시뮬레이션 동안 새 인스턴스로 바꿔 끼워 읽는다. 인덱스 0 = Hunt, 1 = DungeonDelve.
    /// </summary>
    public sealed class NpcPartyCounters
    {
        public static readonly int[] CandidateBucketUpper = { 0, 1, 2, 3, 5, 9, int.MaxValue };

        public readonly long[] Attempts = new long[2];          // 자격 있는 리더가 원정을 고른 수
        public readonly long[] AttemptsPassed = new long[2];    // 모집 시도 확률 통과
        public readonly long[] NobodyAccepted = new long[2];    // 통과했지만 수락자 0 → 솔로
        public readonly long[,] PartiesBySize = new long[2, NpcManager.MaxPartySize + 1];
        public readonly long[] Rejections = new long[(int)NpcPartyRejectReason.Count];
        public readonly long[] CandidateBuckets = new long[7];  // 후보 수 0 / 1 / 2 / 3 / 4-5 / 6-9 / 10+
        public long CandidateTotal;
        public long CandidateSamples;
        public long RestInterrupted;
        public long RestCompletedBeforeJoin;
        public long RumorExchangesSkipped;   // 끊긴 휴식 중 교환 확률 롤이 통과했을 수
        public long HostileGrudge;           // Hostile 탈락 중 원한이 있는 수 (나머지 = 라이벌만)
        // 수락 롤 구간: 0 = 친구·형제·배우자(호감도 무관), 1 = 호감도 < 0, 2 = 0~49, 3 = 50 이상 (관계 없는 후보)
        public const int AcceptBucketCount = 4;
        public readonly long[] AcceptRolls = new long[AcceptBucketCount];
        public readonly long[] Accepted = new long[AcceptBucketCount];
        public readonly double[] AcceptChanceSum = new double[AcceptBucketCount];

        public static int GetAcceptBucket(int affinity, bool closeRelation)
        {
            if (closeRelation == true)
                return 0;
            if (affinity < 0)
                return 1;
            return affinity < 50 ? 2 : 3;
        }

        public void RecordCandidateCount(int count)
        {
            CandidateTotal += count;
            CandidateSamples++;
            for (int i = 0; i < CandidateBucketUpper.Length; i++)
            {
                if (count <= CandidateBucketUpper[i])
                {
                    CandidateBuckets[i]++;
                    return;
                }
            }
        }
    }
}
