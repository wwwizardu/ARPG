#nullable enable
using ARPG.Tables;

namespace ARPG.Village
{
    /// <summary>
    /// Tier 승격 미충족 조건을 감지하고, 후보 오브젝트가 그 조건을 채우는지 판정.
    /// <see cref="ARPG.Systems.System_VillageTierProgression"/>의 EvaluateNextStage와 짝을 이룸 —
    /// 같은 조건을 외부(점수 평가기)에서 조회 가능하게 만든 read-only 헬퍼.
    /// BUILD_PRIORITY_DESIGN.md §2.3 Tier Gate Bonus.
    ///
    /// 자원/시간 조건(Food, age, Pop)은 빌드 행위로 채울 수 없으므로 여기서 다루지 않는다.
    /// </summary>
    public static class TierGapDetector
    {
        /// <summary>
        /// 이 후보 오브젝트가 다음 Stage 승격에 필요한 미충족 조건 중 하나라도 채우면 true.
        /// </summary>
        public static bool DoesCandidateFillGap(VillageData v, BuildableItemTable t)
        {
            ProvidedService service = (ProvidedService)t.ProvidedService;
            SetMemberTag member = (SetMemberTag)t.SetMembership;

            switch (v.Stage)
            {
                case VillageStage.Settlement:
                    // Settlement → Hamlet: housing≥2, Inn 세트
                    if (HousingShort(v, 2) && (service & ProvidedService.Housing) != 0) return true;
                    if (AR.s.Village.HasObjectSet(v.VillageId, ObjectSetType.Inn) == false
                        && (member & (SetMemberTag.Inn_Bed | SetMemberTag.Inn_Hearth)) != 0) return true;
                    return false;

                case VillageStage.Hamlet:
                    // Hamlet → Village: housing≥4, TownPost(Civic)
                    if (HousingShort(v, 4) && (service & ProvidedService.Housing) != 0) return true;
                    if (AR.s.Village.CountByService(v.VillageId, ProvidedService.Civic) < 1
                        && (service & ProvidedService.Civic) != 0) return true;
                    return false;

                case VillageStage.Village:
                    // Village → Town: housing≥8, Forge 세트, Shop
                    if (HousingShort(v, 8) && (service & ProvidedService.Housing) != 0) return true;
                    if (AR.s.Village.HasObjectSet(v.VillageId, ObjectSetType.ForgeStandard) == false
                        && (member & (SetMemberTag.Forge_Heat | SetMemberTag.Forge_Anvil)) != 0) return true;
                    if (AR.s.Village.CountByService(v.VillageId, ProvidedService.Shop) < 1
                        && (service & ProvidedService.Shop) != 0) return true;
                    return false;
            }
            return false;
        }

        private static bool HousingShort(VillageData v, int target)
        {
            return AR.s.Village.CountByService(v.VillageId, ProvidedService.Housing) < target;
        }
    }
}
