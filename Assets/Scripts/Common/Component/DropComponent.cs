namespace ARPG.Component
{
    public struct DropComponent
    {
        public int DropId;
        public int DropRateBonus;
        public int DropRarityBonus;
        public int MonsterLevel;
        public int DropRolls;   // 사망 시 드랍 판정 횟수 (몬스터 등급에서 결정, 최소 1)
    }
}
