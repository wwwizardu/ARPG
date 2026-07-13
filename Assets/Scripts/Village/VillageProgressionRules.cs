#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// VillageData만으로 Tier 승격 가능 여부와 승격 후 영속 상태를 계산한다.
    /// ECS 태그·알림·연대기 이벤트는 호출 시스템이 처리한다.
    /// </summary>
    public static class VillageProgressionRules
    {
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
