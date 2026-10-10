namespace ARPG.Component
{
    /// <summary>
    /// 시야 밖에서 숨길 이동 엔티티(몬스터·NPC·적 투사체)에 붙는다. System_FogEntityVisibility가 판단하고,
    /// 바뀐 상태만 엔티티의 Visual에 적용한다 (FOG_OF_WAR_DESIGN §5). 생성 직후 보이지 않도록 숨김 상태로 시작한다.
    /// 진영이 Player가 되면(동행 NPC) 항상 보인다.
    /// </summary>
    public struct FogVisibilityComponent
    {
        public bool IsVisible;   // 마지막으로 Visual에 적용한 표시 상태
    }
}
