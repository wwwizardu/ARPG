namespace ARPG.Component
{
    public struct DropComponent
    {
        public int DropId;
        public int DropRateBonus;
        public int DropRarityBonus;
        public int MonsterLevel;    // 드랍 장비의 아이템 레벨도 이 값이다
        public int DropRolls;   // 사망 시 드랍 판정 횟수 (몬스터 등급에서 결정, 최소 1)
        public int ModTier11ChanceBp;   // 드랍 장비 1개당 T11 특수 판정 확률 (만분율, 몬스터 등급에서 결정)
        public int ModTier12ChanceBp;   // 드랍 장비 1개당 T12 특수 판정 확률 (만분율, 몬스터 등급에서 결정)
    }
}
