#nullable enable
using System.Collections.Generic;
using ARPG.History;
using ARPG.Scene;
using ARPG.Village;

namespace ARPG.Systems
{
    /// <summary>
    /// PROCEDURAL_HISTORY §0 — 정착지 런타임 LOD의 추상(멀리) 층 tick.
    ///
    /// 실체화되지 않은(플레이어에게서 먼) 정착지의 인구를 게임 시간에 따라 계속 굴린다.
    /// 액션 게임 프레임 스파이크 방지를 위해 **시간 분할(라운드로빈)**: 매 tick마다 커서에서
    /// TICK_BUDGET개만 처리하여 정착지 총수와 무관하게 프레임당 비용을 상수로 유지한다.
    /// 실제 인구 전진은 <see cref="ARPG.History.HistoryManager.AdvanceAbstractSlice"/>(정착지별 O(1) catch-up).
    ///
    /// 인구 cursor/라이브 데이터는 HistoryManager/WorldData가 소유하고, 건설 cursor는 이 시스템이
    /// 런타임 상태로 소유하여 OnReset에서 초기화한다.
    /// Priority 50: Village 도메인 per-village 시스템(52~68)보다 먼저 world 추상 상태를 갱신.
    /// </summary>
    public class System_SettlementSim : IFixedUpdateSystem
    {
        public int Priority => 50;
        public float UpdateInterval => 0.5f;   // 라운드로빈이라 자주 돌 필요 없음 — CPU 절약

        // 프레임당 처리 상한. 게임-일 단위 성장이라 대부분 early-out → 여유롭게 낮춰도 됨.
        private const int TICK_BUDGET = 48;
        private const int CONSTRUCTION_VILLAGE_BUDGET = 12;
        private const int CONSTRUCTION_GENERAL_EVENT_BUDGET = 32;
        private const int CONSTRUCTION_WALL_EVENT_BUDGET = 16;

        private int _constructionCursor;

        public void OnCreate()
        {
            _constructionCursor = 0;
        }

        public void OnReset()
        {
            _constructionCursor = 0;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            // 1) 멀리(추상) 정착지 인구 tick — 시간 분할
            AR.s.History.AdvanceAbstractSlice(TICK_BUDGET);

            // 2) 멀리 있는 마을의 자원/건설/Tier/벽 tick — 별도 cursor와 이벤트 예산으로 분산.
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            float now = AR.s.Time.CurrentGameTime;
            VillageAbstractSliceResult construction =
                VillageAbstractConstructionScheduler.AdvanceSlice(villages,
                    _constructionCursor, CONSTRUCTION_VILLAGE_BUDGET, now,
                    ShouldVisitConstruction, CreateConstructionOptions);
            _constructionCursor = construction.NextCursor;

            // 3) 실체화 전환 (§0.2) — 근처가 되면 materialize, 멀어지면 fold-back
            AR.s.History.UpdateMaterialization();

            // 4) v.Population 동기화 — 마을 경제 인구를 §0 추상 인구에서 소싱 (VILLAGE_NPC_BOUNDARY_DESIGN §2).
            //    per-village 시스템(생산 52, Tier 60, 건설 66)보다 먼저(Priority 50) 갱신되어 항상 최신 값 사용.
            AR.s.Village.RefreshAllPopulationsFromAbstract();
        }

        private static bool ShouldVisitConstruction(VillageData village)
        {
            if (village.SettlementId < 0
                || AR.s.History.TryGetLiveSettlement(village.SettlementId,
                    out SettlementLiveState settlement) == false)
                return false;
            return settlement.IsMaterialized == false;
        }

        private static VillageAbstractAdvanceOptions CreateConstructionOptions(
            VillageData village)
        {
            bool isOperational = false;
            if (AR.s.History.TryGetLiveSettlement(village.SettlementId,
                    out SettlementLiveState settlement) == true)
            {
                village.Population = VillageManager.QuantizeAbstractPopulation(
                    (int)settlement.Pop);
                isOperational = VillageDevelopmentBootstrap.IsOperationalFate(
                    settlement.Fate);
            }
            return new VillageAbstractAdvanceOptions
            {
                GeneralEventBudget = CONSTRUCTION_GENERAL_EVENT_BUDGET,
                WallEventBudget = CONSTRUCTION_WALL_EVENT_BUDGET,
                IsOperational = isOperational,
            };
        }
    }
}
