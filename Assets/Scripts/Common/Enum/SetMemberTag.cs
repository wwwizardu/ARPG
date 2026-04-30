using System;

namespace ARPG
{
    /// <summary>
    /// Phase D: 오브젝트 세트의 부품 식별 비트마스크.
    /// BuildableItemTable.SetMembership 컬럼에 입력 (여러 비트 OR 가능).
    /// 세트 정의 + 요구 비트 조합은 ObjectSetCatalog 참조.
    /// </summary>
    [Flags]
    public enum SetMemberTag : int
    {
        None         = 0,
        Forge_Heat   = 1 << 0,   // Furnace
        Forge_Anvil  = 1 << 1,   // Anvil
        Forge_Quench = 1 << 2,   // QuenchVat
        Inn_Bed      = 1 << 3,   // InnBed
        Inn_Hearth   = 1 << 4,   // Hearth (Inn 측)
        Birth_Bed    = 1 << 5,   // Bed/Bedroll (Phase E 출생용)
        Birth_Hearth = 1 << 6,   // Hearth (Birth 측. Inn_Hearth와 동시 보유)
        Library_Book = 1 << 7,   // Bookshelf (Phase F+)
        Library_Desk = 1 << 8,   // Desk (Phase F+)
    }
}
