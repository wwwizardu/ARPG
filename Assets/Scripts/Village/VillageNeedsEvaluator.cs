#nullable enable
using System.Collections.Generic;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 자가 건설 후보를 4계층 점수로 평가하여 정렬 반환.
    /// BUILD_PRIORITY_DESIGN.md §2 4-layer 모델.
    ///
    /// L1 존재(Campfire) > L2 결핍(housing/food) > L3 성장(Tier 게이트 + 일반) > L4 방어(위협도)
    ///
    /// 정적 헬퍼 — System_VillageBuildQueue가 빌드 시작 시점마다 inline 호출. 캐시 없음(stale 방지).
    /// </summary>
    public static class VillageNeedsEvaluator
    {
        // L1 — 존재 (BuildQueue가 v.HasCampfire 호환 처리에도 사용 → public)
        public const int CAMPFIRE_TABLE_ID = 100;
        private const float L1_EXISTENCE_SCORE = 5000f;

        // L2 — 결핍
        private const float L2_HOUSING_DEFICIT_PER = 1000f;
        private const float L2_FOOD_CAP_DEFICIT_PER = 50f;
        private const int FOOD_PER_NPC_TARGET = 5;

        // L3 — 성장
        private const float L3_TIER_GATE_BONUS = 400f;
        private const float L3_SET_COMPLETION_BONUS = 80f;
        private const float L3_SURPLUS_BONUS = 5f;

        // Anti-spam
        private const float DIMINISHING_PER_EXISTING = 20f;

        // 후보 풀 ID 범위
        private const int CANDIDATE_ID_MIN = 100;
        private const int CANDIDATE_ID_MAX = 200;
        // 벽 세그먼트는 WallPlanner 전담 — 일반 후보 풀에서 제외
        private const int WALL_PALISADE_ID = 180;
        private const int WALL_GATE_ID = 181;

        /// <summary>
        /// 점수 내림차순 정렬된 후보 TableId 리스트 반환. MaxPerVillage 도달 / SpawnType=Tile / 점수 ≤ 0인 후보는 제외.
        /// 호출자가 BuildableItemTable에서 BuildHours/Cost/Category 등 모두 조회.
        /// Step B: 진행 중 task의 TableId도 existing에 합산해 동시 동일 항목 중복 빌드 방지.
        /// </summary>
        public static List<int> GetRankedCandidates(VillageData v)
        {
            SetMemberTag covered = VillageConstructionLedger.AggregateSetMembers(v);

            List<(int id, float score)> scored = new();

            for (int id = CANDIDATE_ID_MIN; id < CANDIDATE_ID_MAX; id++)
            {
                if (id == WALL_PALISADE_ID || id == WALL_GATE_ID) continue;
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(id);
                if (t == null) continue;
                if (t.SpawnType == GlobalEnum.BuildableSpawnType.Tile) continue;

                int placedCount = VillageConstructionLedger.CountPlaced(v, id);
                int inProgressCount = VillageConstructionLedger.CountInProgress(v, id);
                int existing = placedCount + inProgressCount;
                if (t.MaxPerVillage > 0 && existing >= t.MaxPerVillage) continue;

                float score = ScoreCandidate(t, v, covered, existing);
                if (score <= 0f) continue;

                scored.Add((id, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            List<int> ranked = new(scored.Count);
            for (int i = 0; i < scored.Count; i++)
            {
                ranked.Add(scored[i].id);
            }
            return ranked;
        }

        /// <summary>
        /// 점수 1위 후보 TableId — 디버그 로그 등에서 사용. affordability 검사 X. 후보 없으면 -1.
        /// </summary>
        public static int GetTopCandidate(VillageData v)
        {
            List<int> ranked = GetRankedCandidates(v);
            return ranked.Count == 0 ? -1 : ranked[0];
        }

        // ========== Layer 점수 ==========

        private static float ScoreCandidate(Tables.BuildableItemTable t, VillageData v, SetMemberTag covered, int existing)
        {
            float score = 0f;

            // L1 — 존재 (Campfire)
            if (t.Id == CAMPFIRE_TABLE_ID && existing == 0)
                score += L1_EXISTENCE_SCORE;

            ProvidedService service = (ProvidedService)t.ProvidedService;
            SetMemberTag member = (SetMemberTag)t.SetMembership;

            // L2 — 결핍
            if ((service & ProvidedService.Housing) != 0)
            {
                int housing = VillageConstructionLedger.CountByService(v, ProvidedService.Housing);
                int deficit = v.Population - housing;
                if (deficit > 0) score += deficit * L2_HOUSING_DEFICIT_PER;
            }
            if (t.StorageCap_Food > 0)
            {
                int foodCap = VillageResourceSimulation.GetCap(v, GlobalEnum.ItemType.Food);
                int targetCap = GetFoodCapacityTarget(v);
                int capacityDeficit = targetCap - foodCap;
                if (capacityDeficit > 0)
                    score += capacityDeficit * L2_FOOD_CAP_DEFICIT_PER;
            }

            // L3 — 성장
            score += t.BaseWeight;

            if (TierGapDetector.DoesCandidateFillGap(v, t))
                score += L3_TIER_GATE_BONUS;

            int newSets = SetCompletionBonus(member, covered);
            score += newSets * L3_SET_COMPLETION_BONUS;

            // 자원 잉여 가산 — Cap 압박 해소
            int wood = VillageResourceSimulation.GetAmount(v, GlobalEnum.ItemType.Wood);
            int stone = VillageResourceSimulation.GetAmount(v, GlobalEnum.ItemType.Stone);
            if (t.Cost_Wood > 0 && wood >= t.Cost_Wood * 3) score += L3_SURPLUS_BONUS;
            if (t.Cost_Stone > 0 && stone >= t.Cost_Stone * 3) score += L3_SURPLUS_BONUS;

            // L4 — 방어 (벽은 별도 ID라 이 풀에 없음. WallPlanner + BuildQueue fallback이 처리)

            // Anti-spam — 같은 TableId 중복 페널티
            score -= existing * DIMINISHING_PER_EXISTING;

            return score;
        }

        // ========== 헬퍼 ==========

        private static int SetCompletionBonus(SetMemberTag candidateMember, SetMemberTag covered)
        {
            if (candidateMember == SetMemberTag.None) return 0;
            SetMemberTag after = covered | candidateMember;
            int newlyCompleted = 0;
            List<ObjectSetDefinition> definitions = new List<ObjectSetDefinition>(ObjectSetCatalog.All.Values);
            for (int i = 0; i < definitions.Count; i++)
            {
                ObjectSetDefinition def = definitions[i];
                bool wasComplete = (covered & def.RequiredMask) == def.RequiredMask;
                bool isComplete = (after & def.RequiredMask) == def.RequiredMask;
                if (wasComplete == false && isComplete) newlyCompleted++;
            }
            return newlyCompleted;
        }

        private static int GetFoodCapacityTarget(VillageData v)
        {
            int target = v.Population * FOOD_PER_NPC_TARGET;
            if (v.Stage >= VillageStage.City)
                return target;

            Tables.VillageStageTable? nextStage = AR.s.Data.GetVillageStage(v.Stage + 1);
            if (nextStage != null && nextStage.PromoMinFood > target)
                target = nextStage.PromoMinFood;

            return target;
        }
    }
}
