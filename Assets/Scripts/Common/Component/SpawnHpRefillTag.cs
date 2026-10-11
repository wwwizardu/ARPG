namespace ARPG.Component
{
    /// <summary>
    /// 스폰 직후 다음 스탯 재계산에서 현재 HP를 최대 HP로 채우라는 일회용 태그 (MONSTER_AFFIX_DESIGN.md §6.1).
    /// 생명력 증가 접사처럼 생성 뒤에 붙는 보정은 최대 HP만 늘리고 현재 HP를 그대로 두기 때문이다.
    /// System_StatCalculation이 처리 후 제거한다.
    /// </summary>
    public struct SpawnHpRefillTag
    {
    }
}
