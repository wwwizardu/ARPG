namespace ARPG.Component
{
    public struct DropComponent
    {
        public int DropId;
        public int DropQuantityBonus;   // 몬스터 종 수량 보너스 (%, ITEM_DROP_BALANCE_DESIGN §8.3 ③ 묶음)
        public int DropRarityBonus;     // 몬스터 종·등급·접사 수 희귀도 (%, ③ 묶음). 생성 때 합산해 둔다
        public int MonsterLevel;    // 드랍 장비의 아이템 레벨도 이 값이다
        public int DropRolls;   // 기본 드랍 판정 횟수 (등급 판정 + 접사당 판정, 최소 1). 수량 보너스는 죽을 때 곱한다
        public int ModTier11ChanceBp;   // 드랍 장비 1개당 T11 특수 판정 확률 (만분율, 몬스터 등급에서 결정)
        public int ModTier12ChanceBp;   // 드랍 장비 1개당 T12 특수 판정 확률 (만분율, 몬스터 등급에서 결정)
    }
}
