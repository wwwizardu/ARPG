#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 플레이어 행적에 대한 NPC 반응의 순수 계산 (NPC_NARRATIVE_CONTENT_DESIGN §4.2~§4.5).
    /// 상태를 갖지 않는다. 입력(행적 종류·성향·반영 전 관계·관련도)을 받아 반응·관계 변화·보답을 계산해 돌려준다.
    /// 상수를 바꾸면 PolicyVersion을 올려 진행 중 사건의 정책 해시가 바뀌지 않게 한다.
    /// </summary>
    public static class NarrativeReactionRules
    {
        // 점수식·관련도·관계 변화·보답 계산 코드의 버전. 정책 스냅샷 해시에 들어간다.
        public const int PolicyVersion = 1;
        // 소문으로 아는 NPC는 사건 시각부터 14 게임일이 지난 행적에는 반응하지 않는다 (§4.1).
        public const float DeedReactionWindowHours = 14f * 24f;
        // 한 대화에서 반응하는 행적 수와 관계 변화 합계 상한 (§4.1·§4.5)
        public const int MaxDeedsPerConversation = 3;
        public const int ConversationAffinityGainCap = 9;
        public const int ConversationAffinityLossCap = -4;
        // 의뢰 보답 (§4.5): 시스템 생성 10~20골드, 인스턴스 ID로 결정
        public const int RewardGoldMin = 10;
        public const int RewardGoldMax = 20;
        // 친구 단계(호감 50) 이상은 질투하지 않고, 호감이 0 미만일 때만 반감한다 (§4.5 부정 반응의 선, 2026-10-10 코드 규칙)
        public const int EnviousAffinityBelow = 50;
        public const int HostileAffinityBelow = 0;
        // 명예(Glory)가 주·보조 야망이면 질투 성향 가산에 더한다 (§4.3). 문서 예시(다: 질투 11)대로 ±10 제한 뒤에 더한다.
        public const int EnviousGloryBonus = 5;
        private const int AddendLimit = 10;

        /// <summary>반응 점수 입력. 관계는 이번 반응을 반영하기 전 값이다.</summary>
        public readonly struct Input
        {
            public readonly NpcStatComponent Traits;
            public readonly PlayerRelation Relation;
            public readonly bool GloryAmbition;

            public Input(NpcStatComponent traits, PlayerRelation relation, bool gloryAmbition)
            {
                Traits = traits;
                Relation = relation;
                GloryAmbition = gloryAmbition;
            }
        }

        /// <summary>관련도 배율(10분의 1 단위). 부탁한 사람·피해자 30, 동행 20, 같은 마을 10, 다른 마을 3.</summary>
        public static int RelevanceTenths(NarrativeRelevance relevance) => relevance switch
        {
            NarrativeRelevance.Requester => 30,
            NarrativeRelevance.Victim => 30,
            NarrativeRelevance.Companion => 20,
            NarrativeRelevance.SameVillage => 10,
            NarrativeRelevance.OtherVillage => 3,
            _ => 0,
        };

        /// <summary>둘 중 배율이 큰 관련도. 곱하지 않는다 (§4.2).</summary>
        public static NarrativeRelevance Stronger(NarrativeRelevance a, NarrativeRelevance b)
            => RelevanceTenths(b) > RelevanceTenths(a) ? b : a;

        /// <summary>기본 결과 조건이 이 행적에 적용되는가 (§4.3).</summary>
        public static bool Applies(NarrativeReactionRequirement requirement, PlayerDeedKind deed) => requirement switch
        {
            NarrativeReactionRequirement.AnyKnownReport => deed == PlayerDeedKind.RumorResolved || deed == PlayerDeedKind.DungeonCleared,
            NarrativeReactionRequirement.RumorResolved => deed == PlayerDeedKind.RumorResolved,
            NarrativeReactionRequirement.WishFulfilled => deed == PlayerDeedKind.WishFulfilled,
            _ => false,
        };

        /// <summary>
        /// 이 반응 행을 지금 반응하는 쪽이 쓸 수 있는가. owner = 부탁한 사람·소망 주인이 직접 반응(보고·소문 전 해결·소망 전달),
        /// 아니면 소문으로 행적을 들은 NPC (사용자 결정 2026-10-10).
        /// </summary>
        public static bool MatchesAudience(NarrativeReactionAudience audience, bool owner) => audience switch
        {
            NarrativeReactionAudience.Owner => owner,
            NarrativeReactionAudience.Others => owner == false,
            _ => true,
        };

        /// <summary>반드시 막아야 하는 조건 (§4.3 추가 필터). 조건 표 대신 반응 종류의 코드 규칙으로 둔다.</summary>
        public static bool PassesFilter(ScenarioReactionKind kind, in Input input) => kind switch
        {
            ScenarioReactionKind.Envious => input.Relation.Affinity < EnviousAffinityBelow,
            ScenarioReactionKind.Hostile => input.Relation.Affinity < HostileAffinityBelow,
            _ => true,
        };

        /// <summary>필터에 걸리지 않는 반응인가. 행적마다 안전한 기본 후보가 있는지 검증할 때 쓴다.</summary>
        public static bool IsUnfiltered(ScenarioReactionKind kind)
            => kind != ScenarioReactionKind.Envious && kind != ScenarioReactionKind.Hostile;

        public static bool IsNegative(ScenarioReactionKind kind)
            => kind == ScenarioReactionKind.Envious || kind == ScenarioReactionKind.Hostile || kind == ScenarioReactionKind.Suspicious;

        /// <summary>점수 = 기본 점수 + 성향 가산 + 관계 가산. 각 가산은 ±10, 나눗셈은 0 쪽으로 버린다 (§4.3).</summary>
        public static int Score(NarrativeReactionDefinition rule, in Input input)
        {
            NpcStatComponent t = input.Traits;
            PlayerRelation r = input.Relation;
            int trait = 0;
            int relation = 0;
            switch (rule.ScoreProfile)
            {
                case NarrativeReactionScoreProfile.Curious:
                    trait = Limit((t.Curiosity - 50) / 5);
                    break;
                case NarrativeReactionScoreProfile.Warm:
                    trait = Limit((t.Friendliness - 50) / 5);
                    relation = Limit(r.Affinity / 10);
                    break;
                case NarrativeReactionScoreProfile.Admiring:
                    trait = Limit((t.Courage - 50) / 5);
                    relation = Limit((r.Trust - 50) / 5);
                    break;
                case NarrativeReactionScoreProfile.Cautious:
                    trait = Limit((50 - t.Courage) / 5);
                    relation = Limit((50 - r.Trust) / 5);
                    break;
                case NarrativeReactionScoreProfile.Grateful:
                    trait = Limit((t.Loyalty - 50) / 5);
                    relation = Limit(r.Intimacy / 10);
                    break;
                case NarrativeReactionScoreProfile.Envious:
                    trait = Limit((t.Pride - 50) / 5) + (input.GloryAmbition == true ? EnviousGloryBonus : 0);
                    relation = Limit(-r.Affinity / 10);
                    break;
                case NarrativeReactionScoreProfile.Hostile:
                    relation = Limit(-r.Affinity / 5);
                    break;
                case NarrativeReactionScoreProfile.Suspicious:
                    relation = Limit((50 - r.Trust) / 5);
                    break;
            }
            return rule.BaseScore + trait + relation;
        }

        /// <summary>
        /// 이 행적에 적용되고 반응하는 쪽(owner)이 쓸 수 있는 후보 중 점수가 가장 높은 반응. 동점이면 ReactionRuleId가 작은 쪽 (§4.3).
        /// </summary>
        public static NarrativeReactionDefinition? Select(List<NarrativeReactionDefinition> rules, PlayerDeedKind deed, bool owner, in Input input)
        {
            NarrativeReactionDefinition? best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < rules.Count; i++)
            {
                NarrativeReactionDefinition rule = rules[i];
                if (Applies(rule.Requirement, deed) == false || MatchesAudience(rule.Audience, owner) == false ||
                    PassesFilter(rule.Kind, input) == false)
                    continue;
                int score = Score(rule, input);
                if (best == null || score > bestScore || (score == bestScore && rule.ReactionRuleId < best.ReactionRuleId))
                {
                    best = rule;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>
        /// 약속 점수 = (정직 − 50) ÷ 5 − (탐욕 − 50) ÷ 5 + 호감 ÷ 10 (§4.5). 각 항 ±10, 0 쪽으로 버림. 관계는 반영 전 값.
        /// 부정 반응을 고른 NPC는 0 이상일 때만 의뢰 보답을 준다.
        /// </summary>
        public static int PromiseScore(NpcStatComponent traits, PlayerRelation relation)
            => Limit((traits.Honesty - 50) / 5) - Limit((traits.Greed - 50) / 5) + Limit(relation.Affinity / 10);

        /// <summary>의뢰 보답 액수 10~20골드. 인스턴스 ID로 정하므로 재시도해도 같다 (§4.5).</summary>
        public static int RewardAmount(string instanceId)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < instanceId.Length; i++)
            {
                hash ^= instanceId[i];
                hash *= 16777619u;
            }
            return RewardGoldMin + (int)(hash % (uint)(RewardGoldMax - RewardGoldMin + 1));
        }

        /// <summary>
        /// 관계 변화 = 수준의 기본 변화 × 관련도 배율, 0 쪽으로 버린다 (§4.5).
        /// trustInput은 ModifyPlayerRelation에 넘기는 입력값이다 (실제 신뢰 상승은 그 절반).
        /// </summary>
        public static void RelationChange(NarrativeRelationLevel level, NarrativeRelevance relevance,
            out int affinity, out int trustInput, out int intimacy)
        {
            int baseAffinity = 0, baseTrust = 0, baseIntimacy = 0;
            switch (level)
            {
                case NarrativeRelationLevel.Small: baseAffinity = 2; baseIntimacy = 1; break;
                case NarrativeRelationLevel.Standard: baseAffinity = 3; baseTrust = 4; baseIntimacy = 2; break;
                case NarrativeRelationLevel.Cold: baseAffinity = -1; break;
                case NarrativeRelationLevel.Resentful: baseAffinity = -2; break;
            }
            int tenths = RelevanceTenths(relevance);
            affinity = baseAffinity * tenths / 10;
            trustInput = baseTrust * tenths / 10;
            intimacy = baseIntimacy * tenths / 10;
        }

        /// <summary>한 대화의 호감 변화 합계 상한 (+9 / −4).</summary>
        public static int LimitConversationAffinity(int total)
            => Math.Clamp(total, ConversationAffinityLossCap, ConversationAffinityGainCap);

        /// <summary>
        /// 소개할 후속 소문이 없을 때 실제로 실행하는 대응 (§4.5). "아는 다른 소문 소개"는 후보가 없으면 더 묻기로 끝낸다.
        /// 후보가 있으면 호출자가 소개 대응을 그대로 기록한다 (5단계, 2026-10-10).
        /// </summary>
        public static NarrativeResponseKind EffectiveResponse(NarrativeResponseKind response)
            => response == NarrativeResponseKind.IntroduceKnownContent ? NarrativeResponseKind.AskMore : response;

        private static int Limit(int value) => Math.Clamp(value, -AddendLimit, AddendLimit);
    }
}
