namespace ARPG.Component
{
    /// <summary>
    /// 넉백 저항(SkillTable.ForcedMoveImmune)이 켜진 스킬의 실행(Process) 단계에 있는 시전자 표시 (SKILL_DESIGN §6.10 적용 규칙 7).
    /// System_Skill이 실행 단계 진입 때 붙이고 후딜 진입·완료·취소·Reset에서 뗀다. ForcedMoveStarter가 이 태그를 가진 대상을 밀지 않는다.
    /// </summary>
    public struct ForcedMoveImmuneTag
    {
    }
}
