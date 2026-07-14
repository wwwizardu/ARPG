#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// VillageData만으로 Tier 승격 가능 여부와 승격 후 영속 상태를 계산한다.
    /// ECS 태그·알림·연대기 이벤트는 호출 시스템이 처리한다.
    ///
    /// V2 (VILLAGE_REWORK_PLAN §3): 일반 런타임 시간 경과에 의한 승격·자동 시설 추가·Bounds 확장은
    /// <see cref="RuntimeProgressionEnabled"/>로 중단한다. 초기 Stage는 절차적 역사 완료 시점의
    /// 원본 인구(경제 인구 아님)로 1회 계산한다(<see cref="CalculateInitialStage"/>).
    /// </summary>
    public static class VillageProgressionRules
    {
        /// <summary>
        /// V2: 런타임 승격·자동 건설·벽 계획·Stage 기반 Bounds 확장의 전역 스위치.
        /// 후속 런타임 발전 Phase(V8)에서 재미 검증과 함께 재설계하기 전까지 false로 유지한다.
        /// (const가 아닌 readonly — 게이트된 시스템 코드가 unreachable 경고 없이 보존되도록.)
        /// </summary>
        public static readonly bool RuntimeProgressionEnabled = false;

        // V0 확정 초기 Stage 임계값 — 입력은 절차적 역사 완료 시점의 원본 인구(raw).
        // 경제 인구(1~25)가 아니다. VILLAGE_REWORK_PLAN §10 V0 "City 상위 5%안" 채택 결과.
        // VillageStageTable.PromoMinPopulation은 경제 인구 단위 런타임 게이트라 여기 사용하지 않는다.
        public const int INITIAL_STAGE_MIN_RAW_POP_HAMLET = 597;
        public const int INITIAL_STAGE_MIN_RAW_POP_VILLAGE = 1679;
        public const int INITIAL_STAGE_MIN_RAW_POP_TOWN = 2592;
        public const int INITIAL_STAGE_MIN_RAW_POP_CITY = 3419;

        /// <summary>
        /// 고정된 원본 역사 완료 인구로 게임 시작 시 마을이 이미 달성한 초기 Stage를 계산한다.
        /// Housing/Food/Set/Civic/Shop·자원·건물 수 게이트는 사용하지 않으며,
        /// <paramref name="maxStage"/>(VillageProfile.MaxStage)보다 높은 Stage는 적용하지 않는다.
        /// </summary>
        public static VillageStage CalculateInitialStage(int rawHistoryPopulation,
            VillageStage maxStage)
        {
            VillageStage stage;
            if (rawHistoryPopulation >= INITIAL_STAGE_MIN_RAW_POP_CITY)
                stage = VillageStage.City;
            else if (rawHistoryPopulation >= INITIAL_STAGE_MIN_RAW_POP_TOWN)
                stage = VillageStage.Town;
            else if (rawHistoryPopulation >= INITIAL_STAGE_MIN_RAW_POP_VILLAGE)
                stage = VillageStage.Village;
            else if (rawHistoryPopulation >= INITIAL_STAGE_MIN_RAW_POP_HAMLET)
                stage = VillageStage.Hamlet;
            else
                stage = VillageStage.Settlement;

            return stage <= maxStage ? stage : maxStage;
        }

        public static bool CanPromoteTo(VillageData village, Tables.VillageStageTable stageTable,
            float now)
        {
            if (stageTable.PromoMinPopulation < 0)
                return false;

            if (village.Population < stageTable.PromoMinPopulation)
                return false;
            if (VillageConstructionLedger.CountByService(village, ProvidedService.Housing)
                < stageTable.PromoMinHousing)
                return false;
            if (VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Food)
                < stageTable.PromoMinFood)
                return false;
            if (now - village.RegisteredAt < stageTable.PromoMinAgeHours)
                return false;

            if (stageTable.PromoRequiredSet >= 0
                && VillageConstructionLedger.HasObjectSet(village,
                    (ObjectSetType)stageTable.PromoRequiredSet) == false)
                return false;
            if (stageTable.PromoRequiredCivic > 0
                && VillageConstructionLedger.CountByService(village, ProvidedService.Civic)
                    < stageTable.PromoRequiredCivic)
                return false;
            if (stageTable.PromoRequiredShop > 0
                && VillageConstructionLedger.CountByService(village, ProvidedService.Shop)
                    < stageTable.PromoRequiredShop)
                return false;

            return true;
        }

        public static bool ApplyPromotion(VillageData village, VillageStage next, int boundsRadius)
        {
            VillageStage previous = village.Stage;
            village.Stage = next;
            village.BoundsX = Mathf.FloorToInt(village.PositionX) - boundsRadius;
            village.BoundsY = Mathf.FloorToInt(village.PositionY) - boundsRadius;
            village.BoundsW = boundsRadius * 2;
            village.BoundsH = boundsRadius * 2;

            bool requestWallPlan = next == VillageStage.Hamlet && previous < VillageStage.Hamlet;
            if (requestWallPlan)
                village.WallPlanRequested = true;
            return requestWallPlan;
        }
    }
}
