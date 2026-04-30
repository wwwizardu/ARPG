#nullable enable
using System.Collections.Generic;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: 오브젝트 세트의 요구 비트 + 판정 범위 정의.
    /// VillageManager.HasObjectSet의 정본 사전 — 새 세트 추가는 여기에 한 줄 + SetMemberTag enum 비트 추가만.
    ///
    /// Range == 0 → 마을 전체 검사
    /// Range >  0 → anchor 기준 N×N 영역 검사 (체비셰프 거리 N/2)
    /// </summary>
    public readonly struct ObjectSetDefinition
    {
        public readonly SetMemberTag RequiredMask;
        public readonly int Range;

        public ObjectSetDefinition(SetMemberTag mask, int range)
        {
            RequiredMask = mask;
            Range = range;
        }
    }

    public static class ObjectSetCatalog
    {
        public static readonly Dictionary<ObjectSetType, ObjectSetDefinition> All = new()
        {
            [ObjectSetType.ForgeBasic]    = new(SetMemberTag.Forge_Heat, 5),
            [ObjectSetType.ForgeStandard] = new(SetMemberTag.Forge_Heat | SetMemberTag.Forge_Anvil, 5),
            [ObjectSetType.ForgePremium]  = new(SetMemberTag.Forge_Heat | SetMemberTag.Forge_Anvil | SetMemberTag.Forge_Quench, 5),
            [ObjectSetType.Inn]           = new(SetMemberTag.Inn_Bed | SetMemberTag.Inn_Hearth, 0),
            [ObjectSetType.Birth]         = new(SetMemberTag.Birth_Bed | SetMemberTag.Birth_Hearth, 3),
            [ObjectSetType.Library]       = new(SetMemberTag.Library_Book | SetMemberTag.Library_Desk, 5),
        };
    }
}
