#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using UnityEngine;

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
        private const float L2_FOOD_DEFICIT_PER = 50f;
        private const int FOOD_PER_NPC_TARGET = 5;

        // L3 — 성장
        private const float L3_TIER_GATE_BONUS = 400f;
        private const float L3_SET_COMPLETION_BONUS = 80f;
        private const float L3_JOB_MATCH_BONUS = 50f;
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
            SetMemberTag covered = AggregateSetMembers(v);
            Dictionary<int, int> inProgress = CountInProgressTasksByTableId(v.VillageId);

            List<(int id, float score)> scored = new();

            for (int id = CANDIDATE_ID_MIN; id < CANDIDATE_ID_MAX; id++)
            {
                if (id == WALL_PALISADE_ID || id == WALL_GATE_ID) continue;
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(id);
                if (t == null) continue;
                if (t.SpawnType == GlobalEnum.BuildableSpawnType.Tile) continue;

                int placedCount = CountByTableId(v, id);
                int inProgressCount = inProgress.TryGetValue(id, out int n) ? n : 0;
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
        /// 마을의 진행 중 task를 TableId별 카운트로 반환. 동시 동일 항목 중복 방지에 사용.
        /// </summary>
        private static Dictionary<int, int> CountInProgressTasksByTableId(int villageId)
        {
            var result = new Dictionary<int, int>();
            var pool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();
            for (int i = 0; i < pool.Count; i++)
            {
                ObjectPlacementTaskComponent t = pool.GetByIndex(i);
                if (t.VillageId != villageId) continue;
                result.TryGetValue(t.TargetTableId, out int n);
                result[t.TargetTableId] = n + 1;
            }
            return result;
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
                int housing = AR.s.Village.CountByService(v.VillageId, ProvidedService.Housing);
                int deficit = v.Population - housing;
                if (deficit > 0) score += deficit * L2_HOUSING_DEFICIT_PER;
            }
            if ((service & ProvidedService.Production) != 0)
            {
                int food = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Food);
                int target = v.Population * FOOD_PER_NPC_TARGET;
                int deficit = target - food;
                if (deficit > 0) score += deficit * L2_FOOD_DEFICIT_PER;
            }

            // L3 — 성장
            score += t.BaseWeight;

            if (TierGapDetector.DoesCandidateFillGap(v, t))
                score += L3_TIER_GATE_BONUS;

            int newSets = SetCompletionBonus(member, covered);
            score += newSets * L3_SET_COMPLETION_BONUS;

            if (t.AssociatedJobType != 0)
                score += L3_JOB_MATCH_BONUS;

            // 자원 잉여 가산 — Cap 압박 해소
            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);
            if (t.Cost_Wood > 0 && wood >= t.Cost_Wood * 3) score += L3_SURPLUS_BONUS;
            if (t.Cost_Stone > 0 && stone >= t.Cost_Stone * 3) score += L3_SURPLUS_BONUS;

            // L4 — 방어 (벽은 별도 ID라 이 풀에 없음. WallPlanner + BuildQueue fallback이 처리)

            // Anti-spam — 같은 TableId 중복 페널티
            score -= existing * DIMINISHING_PER_EXISTING;

            return score;
        }

        // ========== 헬퍼 ==========

        private static SetMemberTag AggregateSetMembers(VillageData v)
        {
            SetMemberTag covered = SetMemberTag.None;
            var entities = PlacedObjectRegistry.GetAllEntitiesInVillage(v.VillageId);
            for (int i = 0; i < entities.Count; i++)
            {
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entities[i], out var po))
                    covered |= po.SetMember;
            }
            return covered;
        }

        private static int SetCompletionBonus(SetMemberTag candidateMember, SetMemberTag covered)
        {
            if (candidateMember == SetMemberTag.None) return 0;
            SetMemberTag after = covered | candidateMember;
            int newlyCompleted = 0;
            foreach (var def in ObjectSetCatalog.All.Values)
            {
                bool wasComplete = (covered & def.RequiredMask) == def.RequiredMask;
                bool isComplete = (after & def.RequiredMask) == def.RequiredMask;
                if (wasComplete == false && isComplete) newlyCompleted++;
            }
            return newlyCompleted;
        }

        private static int CountByTableId(VillageData v, int tableId)
        {
            if (v.PlacedObjectTypeIds == null) return 0;
            int count = 0;
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
                if (v.PlacedObjectTypeIds[i] == tableId) count++;
            return count;
        }
    }
}
