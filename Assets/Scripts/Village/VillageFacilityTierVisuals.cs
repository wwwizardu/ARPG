#nullable enable

namespace ARPG.Village
{
    /// <summary>
    /// V4 (VILLAGE_REWORK_PLAN §3.6/§7.1): 시설 Tier 교체 자산 키 규칙.
    /// Tier가 올라도 새 건물·외형 모듈을 추가하지 않고 같은 Entity·같은 슬롯에서
    /// 스프라이트 자산만 교체한다. Tier 자산 키는 `{기본 ResourceName}_T{tier}` 관례를 쓰고,
    /// Addressables에 해당 키가 없으면 호출자(BuildingFactory)가 기본 자산으로 대체한다
    /// (신규 아트 도착 전 임시 대체 — 기능 차이는 UIForge의 FacilityTier 게이트가 담당).
    /// </summary>
    public static class VillageFacilityTierVisuals
    {
        /// <summary>
        /// Tier 교체 자산 키. V4 수직 슬라이스에서는 Dwarf 단일 제련소(160)의 Tier 2/3만 대상이다.
        /// false = 기본 자산 사용 (Tier 1, 교체 그룹이 아닌 시설, 잘못된 입력).
        /// </summary>
        public static bool TryGetTierResourceName(int tableId, int facilityTier,
            string? baseResourceName, out string resourceName)
        {
            resourceName = string.Empty;
            if (facilityTier < 2 || facilityTier > 3)
                return false;
            if (string.IsNullOrEmpty(baseResourceName) == true)
                return false;
            if (tableId != VillageInitialFacilityCatalog.FORGE_TABLE_ID)
                return false;

            resourceName = $"{baseResourceName}_T{facilityTier}";
            return true;
        }
    }
}
