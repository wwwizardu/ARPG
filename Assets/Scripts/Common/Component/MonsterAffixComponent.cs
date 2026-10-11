namespace ARPG.Component
{
    /// <summary>
    /// 몬스터 개체가 가진 접사 Id 목록 (MONSTER_AFFIX_DESIGN.md §6.1). 접사가 1개 이상인 몬스터에만 붙는다.
    /// 스탯형 접사의 효과는 생성 시점에 StatModifier(출처 MonsterAffix)로 이미 등록됐고,
    /// 이 값은 오라 시스템(System_MonsterAura)과 이름표(MonsterAffixLabelView)가 읽는다.
    /// 무리 생성 때 MonsterAffixRoller가 뽑은 결과를 EntityFactory.CreateMonster에 넘기는 값으로도 쓴다.
    /// </summary>
    public struct MonsterAffixComponent
    {
        public const int MAX_AFFIXES = 10;

        public int Count;
        public int AuraCount;   // Kind=Aura 접사 수. 0이면 오라 시스템이 건너뛴다
        public int Affix0, Affix1, Affix2, Affix3, Affix4, Affix5, Affix6, Affix7, Affix8, Affix9;

        public bool Add(int affixId)
        {
            if (Count >= MAX_AFFIXES)
                return false;

            SetByIndex(Count, affixId);
            Count++;
            return true;
        }

        public readonly int GetByIndex(int index)
        {
            switch (index)
            {
                case 0: return Affix0; case 1: return Affix1; case 2: return Affix2; case 3: return Affix3;
                case 4: return Affix4; case 5: return Affix5; case 6: return Affix6; case 7: return Affix7;
                case 8: return Affix8; case 9: return Affix9;
                default: return 0;
            }
        }

        private void SetByIndex(int index, int affixId)
        {
            switch (index)
            {
                case 0: Affix0 = affixId; break; case 1: Affix1 = affixId; break;
                case 2: Affix2 = affixId; break; case 3: Affix3 = affixId; break;
                case 4: Affix4 = affixId; break; case 5: Affix5 = affixId; break;
                case 6: Affix6 = affixId; break; case 7: Affix7 = affixId; break;
                case 8: Affix8 = affixId; break; case 9: Affix9 = affixId; break;
            }
        }
    }
}
