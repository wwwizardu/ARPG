namespace ARPG.Component
{
    /// <summary>
    /// 몬스터 개체의 등급 (MONSTER_RARITY_DESIGN.md §5). EntityFactory.CreateMonster가 모든 몬스터에 붙인다.
    /// 등급 배율·드랍 보상은 생성 시점에 이미 반영됐고, 이 값은 HP바 색처럼 등급을 알아야 하는 쪽이 읽는다.
    /// 무작위 접사는 이 컴포넌트에 넣지 않고 MonsterAffixComponent로 따로 붙인다 (MONSTER_AFFIX_DESIGN.md §6.1).
    /// </summary>
    public struct MonsterArchetypeComponent
    {
        public GlobalEnum.MonsterArchetype Archetype;
    }
}
