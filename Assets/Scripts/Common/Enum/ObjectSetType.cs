namespace ARPG
{
    /// <summary>
    /// Phase D: 마을 오브젝트 세트 종류.
    /// 각 세트의 요구 비트(SetMemberTag) + 판정 범위는 ObjectSetCatalog가 정의.
    /// </summary>
    public enum ObjectSetType
    {
        ForgeBasic,         // Furnace
        ForgeStandard,      // Furnace + Anvil
        ForgePremium,       // Furnace + Anvil + QuenchVat
        Inn,                // InnBed + Hearth (마을 전체)
        Birth,              // Bed + Hearth (3×3) — Phase E 출생용
        Library,            // Bookshelf + Desk (5×5) — Phase F+
    }
}
