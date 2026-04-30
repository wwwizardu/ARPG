namespace ARPG
{
    /// <summary>
    /// Phase D: BuildableItem의 카테고리. VillageTileFinder의 클러스터 가산점/외곽 마진 판정에 사용.
    /// BuildableItemTable.Category 컬럼에 입력.
    /// </summary>
    public enum BuildableCategory
    {
        None,
        Housing,        // Bedroll, Bed, InnBed
        Storage,        // Woodpile, Chest, Stockpile
        Production,     // CropPlot, ChoppingBlock, DryingRack, MiningCart
        Cooking,        // Hearth
        Forge,          // Furnace, Anvil, QuenchVat
        Service,        // MerchantStall, TownPost, Shrine, Well
        Defense,        // Palisade, PalisadeGate, SignalBrazier (외곽 띠 점유)
        Decor,
    }
}
