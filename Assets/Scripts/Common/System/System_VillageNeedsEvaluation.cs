#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase D: 마을 빌드 후보를 점수 기반으로 평가. 게임시간 2h마다 1마을 1회 재계산 + VillageNeedsCache 갱신.
    /// BuildQueue가 이 캐시를 우선 채택, 후보 없으면 VillageBuildRoadmap fallback.
    ///
    /// 점수 함수 (PHASE_D_DESIGN.md §9.2):
    ///   - BaseWeight (테이블)
    ///   - 주거 결손 × 50  (Housing 서비스인 후보만)
    ///   - 식량 결손 × 40  (Production 서비스인 후보만)
    ///   - Cap 초과 잉여 × 0.3
    ///   - 세트 완성 보너스 × 80 (이 멤버 추가가 새 세트 완성하는가)
    ///   - 벽 결손 × 위협도 × 30 (Defense 카테고리)
    ///   - 직업 수요 × 15 (AssociatedJobType이 있는 후보)
    ///   - MaxPerVillage 도달 시 후보 제외
    /// </summary>
    public class System_VillageNeedsEvaluation : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 60-64 Lifecycle
        public int Priority => 61;
        public float UpdateInterval => 5.0f;

        private const float CHECK_INTERVAL_HOURS = 2f;
        private float _lastCheckGameTime = -1f;

        public void OnCreate()
        {
            _lastCheckGameTime = AR.s.Time.CurrentGameTime;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            if (now - _lastCheckGameTime < CHECK_INTERVAL_HOURS) return;
            _lastCheckGameTime = now;

            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;
                if (v.Population < 1) continue;

                var top = EvaluateTopCandidate(v);
                if (top.HasValue)
                {
                    VillageNeedsCache.Set(v.VillageId, top.Value);
                    Debug.Log($"[Needs] v{v.VillageId} top={top.Value.TableId} (BuildHours={top.Value.BuildHours:F1}h)");
                }
                else
                {
                    VillageNeedsCache.Clear(v.VillageId);
                }
            }
        }

        public void OnReset()
        {
            _lastCheckGameTime = -1f;
            VillageNeedsCache.ClearAll();
        }

        // ========== 점수 계산 ==========

        private static RoadmapEntry? EvaluateTopCandidate(VillageData v)
        {
            // 마을 현재 SetMember 비트 OR (세트 완성 보너스 계산용)
            SetMemberTag covered = AggregateSetMembers(v);

            // 후보 풀 — 모든 BuildableItem 중 비-Tile + 비용 ≥ 1인 것 (벽 제외)
            List<Tables.BuildableItemTable> stageEligible = new List<Tables.BuildableItemTable>();
            int stageInt = (int)v.Stage;

            // 모든 BuildableItem 순회
            // (전체 테이블 조회는 DataManager에 helper가 없어 BuildQueue처럼 GetBuildableItem 반복)
            // → 대안: AR.s.Data._buildableItemTable이 private이라 직접 못 봄. 후보를 코드 상수로 두지 않으려면 모든 ID 범위 스캔 필요.
            // 임시: id 100~199 범위 스캔 (벽 180/181 제외). Phase D 후속에 GetAllBuildables() 헬퍼 추가 가능.
            for (int id = 100; id < 200; id++)
            {
                if (id == 180 || id == 181) continue;  // Wall은 WallPlanner가 전담
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(id);
                if (t == null) continue;
                if (t.SpawnType == GlobalEnum.BuildableSpawnType.Tile) continue;  // 벽 등 Tile 경로 제외
                if (t.MaxPerVillage > 0 && CountByTableId(v, t.Id) >= t.MaxPerVillage) continue;
                stageEligible.Add(t);
            }

            if (stageEligible.Count == 0) return null;

            float bestScore = float.NegativeInfinity;
            Tables.BuildableItemTable? best = null;

            for (int i = 0; i < stageEligible.Count; i++)
            {
                Tables.BuildableItemTable t = stageEligible[i];

                // 자원 부족 → 후보 제외
                int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
                int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);
                if (wood < t.Cost_Wood || stone < t.Cost_Stone) continue;

                float score = t.BaseWeight;
                ProvidedService service = (ProvidedService)t.ProvidedService;
                SetMemberTag member = (SetMemberTag)t.SetMembership;

                // 주거 결손 (이미 보유한 Housing 수만큼 가중치 감소)
                if ((service & ProvidedService.Housing) != 0)
                {
                    int housing = AR.s.Village.CountByService(v.VillageId, ProvidedService.Housing);
                    score += Mathf.Max(0, v.Population - housing) * 50f;
                }

                // 식량 균형 — 부족 시 가산, 풍족 시 감산 (이미 식량 충분이면 Production 우선순위 ↓)
                if ((service & ProvidedService.Production) != 0)
                {
                    int food = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Food);
                    int target = v.Population * 5;
                    int deficit = target - food;
                    if (deficit > 0) score += deficit * 0.5f;            // 식량 결손 가중
                    else if (food > target * 2) score -= 15f;            // 식량 2배 이상 풍족 → Production 후보 페널티
                }

                // Cap 초과 잉여 — Wood/Stone 비축 많을수록 우선 소비
                if (t.Cost_Wood > 0 && wood >= t.Cost_Wood * 3) score += 5f;
                if (t.Cost_Stone > 0 && stone >= t.Cost_Stone * 3) score += 5f;

                // 세트 완성 보너스
                score += SetCompletionBonus(member, covered) * 80f;

                // 직업 수요 — AssociatedJobType이 있으면 가중 (Phase D MVP는 단순)
                if (t.AssociatedJobType != 0)
                    score += 15f;

                // ★ 체감(diminishing returns) — 같은 타입 보유 개수당 점수 차감.
                // 동일 후보가 무한 1위로 굳는 문제 방지 (CropPlot/Bed 등이 영원히 1위가 되지 않게).
                int existing = CountByTableId(v, t.Id);
                score -= existing * 12f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }

            if (best == null) return null;
            return new RoadmapEntry(best.Id, VillageBuildRoadmap.GetBuildHours(best.Id));
        }

        /// <summary>
        /// 마을의 모든 PlacedObject SetMember 비트를 OR 누적 — 세트 완성 검사용 캐시.
        /// </summary>
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

        /// <summary>
        /// 후보 멤버 비트가 새 세트를 완성시키면 그 개수 반환 (보통 0 또는 1).
        /// </summary>
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
