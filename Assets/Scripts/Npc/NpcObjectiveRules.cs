#nullable enable
using ARPG.Component;
using ARPG.History;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 단기 목표의 순수 규칙 (NPC_OBJECTIVE_DESIGN §4·§6.2·§7.1·§10). 상태를 갖지 않는다 — 정의 행·목표·시각만 보고 계산한다.
    /// 게임(NpcManager·System_NpcSim)과 진단(NpcAmbitionSimDiagnostics)이 같은 메서드를 불러 결과가 어긋나지 않게 한다.
    /// 수치는 시작값이다 (§3 — 구현 뒤 진단으로 맞춘다).
    /// </summary>
    public static class NpcObjectiveRules
    {
        // RNG salt (NPC_SIMULATION §11.1). 200 = 극적 목표 생성 판정 — 소유자 = 판정하는 NPC, 시각 = 계기 사건 시각이라
        // 사건 시점 판정과 다시 계산이 같은 결과를 낸다 (§4.8). 201~209는 단기 목표 예비. 일상 목표는 RNG를 쓰지 않는다.
        public const int ReactSalt = 200;

        // 다시 계산이 보는 기록 범위 (§4.8 — 극적 목표 기한 14일). 그보다 오래된 기록은 계산하지 않는다.
        public const float RecalculateWindowHours = 336f;

        // 원수 갚기 관계 배율 (§6.2, D149). 여러 관계면 가장 높은 것. 라이벌·원한만 있으면 0(제외).
        public const float FamilyRelationMultiplier = 1.0f;
        public const float FriendRelationMultiplier = 0.7f;
        public const float CompanionRelationMultiplier = 0.5f;
        // 함께 원정한 동료로 치는 사망 때 호감 (공동 활동 기억 LastAffinity)
        public const int CompanionMinAffinity = 50;

        // 성격 배율 (§6.2): 1 + clamp(Σ 계수 × (성격 − 50)/50, ±0.5)
        public const float PersonalityMaxDeviation = 0.5f;
        private const float LoyalLoyaltyWeight = 0.25f;
        private const float LoyalPrideWeight = 0.15f;
        private const float LoyalCourageWeight = 0.10f;
        private const float LoyalPatienceWeight = -0.20f;
        private const float ProudPrideWeight = 0.25f;
        private const float ProudCourageWeight = 0.20f;
        private const float ProudCautiousPrimaryWeight = -0.30f;
        private const float ProudCautiousSecondaryWeight = -0.15f;

        // 원수 갚기 원정 수락 보너스 (§7.2): 죽은 NPC의 사망 관계 기억·공동 활동 기억이 있는 후보. 상한(0.90)은 그대로 둔다.
        public const float RevengeAcceptBonus = 0.20f;

        /// <summary>준비 끝·기한의 기준 시각 (§4.1 TimeFrom).</summary>
        public static float GetBaseTime(NpcObjectiveTable definition, NpcObjective objective)
        {
            return definition.TimeFrom == NpcObjectiveTimeFrom.Event ? objective.EventGameTime : objective.CreatedGameTime;
        }

        /// <summary>단계 (§4.4). 저장하지 않고 계산한다. PrepHours가 0이면 바로 실행 단계다.</summary>
        public static NpcObjectiveStage GetStage(NpcObjectiveTable definition, NpcObjective objective, float now)
        {
            return now < GetBaseTime(definition, objective) + definition.PrepHours
                ? NpcObjectiveStage.Preparing
                : NpcObjectiveStage.Attempting;
        }

        /// <summary>기한 시각 (§4.1 DeadlineHours, 기준 시각부터).</summary>
        public static float GetDeadline(NpcObjectiveTable definition, NpcObjective objective)
        {
            return GetBaseTime(definition, objective) + definition.DeadlineHours;
        }

        /// <summary>
        /// 목표 칸 비교 (§2.2, D143). 분류(강제 > 극적 > 일상) → 같은 분류면 Priority. a가 높으면 양수, 같으면 0, 낮으면 음수.
        /// </summary>
        public static int CompareRank(NpcObjectiveTable a, NpcObjectiveTable b)
        {
            if (a.Category != b.Category)
                return a.Category > b.Category ? 1 : -1;
            return a.Priority.CompareTo(b.Priority);
        }

        /// <summary>사망 관계 기억의 관계 유형 배율 (§6.2). 가족 1.0 / 친구·사제 0.7 / 라이벌·원한 0.</summary>
        public static float GetRelationMultiplier(NpcRelationType type)
        {
            return type switch
            {
                NpcRelationType.Spouse or NpcRelationType.Sibling or NpcRelationType.ParentOf => FamilyRelationMultiplier,
                NpcRelationType.Friend or NpcRelationType.MentorOf => FriendRelationMultiplier,
                _ => 0f,
            };
        }

        /// <summary>성격 배율 규칙 (§6.2, ReactProfile). None이면 1.</summary>
        public static float GetPersonalityMultiplier(NpcObjectiveReactProfile profile, in NpcStatComponent stat,
            in NpcAmbitionProfile ambition)
        {
            float deviation;
            switch (profile)
            {
                case NpcObjectiveReactProfile.Loyal:
                    deviation = NpcActivityResolver.CenteredTrait(stat.Loyalty) * LoyalLoyaltyWeight
                                + NpcActivityResolver.CenteredTrait(stat.Pride) * LoyalPrideWeight
                                + NpcActivityResolver.CenteredTrait(stat.Courage) * LoyalCourageWeight
                                + NpcActivityResolver.CenteredTrait(stat.Patience) * LoyalPatienceWeight;
                    break;
                case NpcObjectiveReactProfile.Proud:
                    deviation = NpcActivityResolver.CenteredTrait(stat.Pride) * ProudPrideWeight
                                + NpcActivityResolver.CenteredTrait(stat.Courage) * ProudCourageWeight;
                    if (ambition.Primary == NpcAmbitionKind.Cautious)
                        deviation += ProudCautiousPrimaryWeight;
                    else if (ambition.Secondary == NpcAmbitionKind.Cautious)
                        deviation += ProudCautiousSecondaryWeight;
                    break;
                default:
                    return 1f;
            }
            return 1f + Mathf.Clamp(deviation, -PersonalityMaxDeviation, PersonalityMaxDeviation);
        }

        /// <summary>생성 확률 (§4.3 ⑤) = ReactChance × 관계 배율 × 성격 배율, 0~1.</summary>
        public static float GetReactChance(NpcObjectiveTable definition, float relationMultiplier, in NpcStatComponent stat)
        {
            if (definition.ReactChance >= 1f && relationMultiplier >= 1f && definition.ReactProfile == NpcObjectiveReactProfile.None)
                return 1f;
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(stat);
            return Mathf.Clamp01(definition.ReactChance * relationMultiplier *
                                 GetPersonalityMultiplier(definition.ReactProfile, stat, ambition));
        }

        /// <summary>
        /// 생성 판정 (salt 200). 확률이 1 이상이면 굴리지 않는다. 같은 NPC·같은 사건 시각이면 다시 계산해도 같은 결과다 (§4.8).
        /// </summary>
        public static bool RollReaction(int entityId, float eventGameTime, int worldSeed, float chance)
        {
            if (chance >= 1f)
                return true;
            if (chance <= 0f)
                return false;
            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, eventGameTime, worldSeed, ReactSalt);
            return rng.NextDouble() < chance;
        }

        /// <summary>단계별 활동 배율 (§7.1). 성향 ±20% 클램프 밖에서 곱한다.</summary>
        public static float GetActivityMultiplier(NpcObjectiveTable definition, NpcObjectiveStage stage, NpcActivityKind kind)
        {
            bool preparing = stage == NpcObjectiveStage.Preparing;
            return kind switch
            {
                NpcActivityKind.Rest => preparing ? definition.PrepRest : definition.ActRest,
                NpcActivityKind.Hunt => preparing ? definition.PrepHunt : definition.ActHunt,
                NpcActivityKind.Trade => preparing ? definition.PrepTrade : definition.ActTrade,
                NpcActivityKind.DungeonDelve => preparing ? definition.PrepDelve : definition.ActDelve,
                _ => 1f,
            };
        }
    }
}
