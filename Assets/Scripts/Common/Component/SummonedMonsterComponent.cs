namespace ARPG.Component
{
    /// <summary>
    /// 다른 몬스터의 소환 효과(SkillEffect SummonMonster)로 생긴 몬스터. 저장하지 않는 일시 존재다.
    /// 주인이 죽거나 비활성되면 System_SummonedMonster가 제거하고, 생존 시간은 LifetimeComponent가 센다 (SKILL_DESIGN §6.12).
    /// </summary>
    public struct SummonedMonsterComponent
    {
        public int OwnerEntityId;   // 소환한 몬스터
        public int SummonEffectId;  // 소환한 SkillEffectTable Id. 같은 효과로 부른 수만 최대 수에 센다
    }
}
