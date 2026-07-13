#nullable enable
using ARPG.Tables;

namespace ARPG.Village
{
    /// <summary>
    /// Tier 승격 미충족 조건을 감지하고, 후보 오브젝트가 그 조건을 채우는지 판정.
    /// <see cref="ARPG.Systems.System_VillageTierProgression"/>와 동일한 VillageStageTable 데이터를 공유 —
    /// 이중 진실 소스 없이 같은 게이트를 빌드 점수 평가기에서 조회.
    /// BUILD_PRIORITY_DESIGN.md §2.3 Tier Gate Bonus.
    ///
    /// Food 현재량/시간/Pop은 빌드 행위로 직접 채울 수 없으므로 다루지 않는다.
    /// 단 Food 저장 상한은 StorageCap_Food 건물로 확장할 수 있으므로 Tier gap으로 평가한다.
    /// </summary>
    public static class TierGapDetector
    {
        /// <summary>
        /// 이 후보 오브젝트가 다음 Stage 승격에 필요한 미충족 조건 중 하나라도 채우면 true.
        /// </summary>
        public static bool DoesCandidateFillGap(VillageData v, BuildableItemTable t)
        {
            if (v.Stage >= VillageStage.City) return false;
            VillageStage next = v.Stage + 1;
            VillageStageTable? stageTable = AR.s.Data.GetVillageStage(next);
            if (stageTable == null) return false;
            if (stageTable.PromoMinPopulation < 0) return false;  // 진입 불가 단계

            ProvidedService service = (ProvidedService)t.ProvidedService;
            SetMemberTag member = (SetMemberTag)t.SetMembership;

            // Food 저장 상한 부족 — 현재량은 생산으로 채우되, 상한은 건물로 먼저 확보해야 함
            if (stageTable.PromoMinFood > 0
                && VillageResourceSimulation.GetCap(v, GlobalEnum.ItemType.Food) < stageTable.PromoMinFood
                && t.StorageCap_Food > 0)
                return true;

            // Housing 부족
            if (stageTable.PromoMinHousing > 0
                && VillageConstructionLedger.CountByService(v, ProvidedService.Housing) < stageTable.PromoMinHousing
                && (service & ProvidedService.Housing) != 0)
                return true;

            // 필수 세트 미완성 — 후보가 그 세트의 부품 비트를 가지면 채울 수 있음
            if (stageTable.PromoRequiredSet >= 0)
            {
                var setType = (ObjectSetType)stageTable.PromoRequiredSet;
                if (VillageConstructionLedger.HasObjectSet(v, setType) == false
                    && ObjectSetCatalog.All.TryGetValue(setType, out var def)
                    && (member & def.RequiredMask) != 0)
                    return true;
            }

            // Civic 부족
            if (stageTable.PromoRequiredCivic > 0
                && VillageConstructionLedger.CountByService(v, ProvidedService.Civic) < stageTable.PromoRequiredCivic
                && (service & ProvidedService.Civic) != 0)
                return true;

            // Shop 부족
            if (stageTable.PromoRequiredShop > 0
                && VillageConstructionLedger.CountByService(v, ProvidedService.Shop) < stageTable.PromoRequiredShop
                && (service & ProvidedService.Shop) != 0)
                return true;

            return false;
        }
    }
}
