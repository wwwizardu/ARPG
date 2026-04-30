using System;

namespace ARPG
{
    /// <summary>
    /// Phase D: PlacedObject가 플레이어에게 제공하는 서비스 비트마스크.
    /// BuildableItemTable.ProvidedService 컬럼에 입력.
    /// </summary>
    [Flags]
    public enum ProvidedService : int
    {
        None        = 0,
        Housing     = 1 << 0,   // Bedroll, Bed, InnBed
        Storage     = 1 << 1,   // Woodpile, Stockpile, Chest
        Production  = 1 << 2,   // CropPlot, ChoppingBlock, MiningCart, DryingRack
        Cooking     = 1 << 3,   // Hearth (Food 보존)
        Shop        = 1 << 4,   // MerchantStall
        Forge       = 1 << 5,   // Furnace, Anvil
        Quench      = 1 << 6,   // QuenchVat
        Inn         = 1 << 7,   // InnBed
        Shrine      = 1 << 8,   // Shrine, Altar
        Signal      = 1 << 9,   // SignalBrazier (Phase F)
        Civic       = 1 << 10,  // TownPost
        Beacon      = 1 << 11,  // Well
    }
}
